using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace InProcess.DevTools.Mcp
{
    internal sealed partial class InProcessDevToolsMcpServer : IDisposable
    {
        private readonly IDevToolsTopLevelGroup _topLevelGroup;
        private readonly McpServerOptions _options;
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Task _listenTask;
        private readonly string _path;
        private readonly NodeTree _tree;
        private readonly bool _extendedCapabilitiesAllowed;
        private readonly IReadOnlyList<ToolDefinition> _tools;

        public InProcessDevToolsMcpServer(IDevToolsTopLevelGroup topLevelGroup, McpServerOptions options)
            : this(topLevelGroup, options, IsOptimizedBuild)
        {
        }

        internal InProcessDevToolsMcpServer(IDevToolsTopLevelGroup topLevelGroup, McpServerOptions options, Func<bool> isOptimizedBuild)
        {
            _topLevelGroup = topLevelGroup ?? throw new ArgumentNullException(nameof(topLevelGroup));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _path = NormalizePath(_options.Path);
            _tree = new NodeTree(_topLevelGroup);
            _extendedCapabilitiesAllowed = _options.AllowInReleaseBuilds || !isOptimizedBuild();
            _tools = BuildTools();

            _listener.Prefixes.Add($"http://{_options.Host}:{_options.Port}/");
            _listener.Start();
            _listenTask = Task.Run(ListenAsync);
        }

        /// <summary>
        /// True when the host application was compiled with optimizations (a Release build).
        /// </summary>
        private static bool IsOptimizedBuild()
        {
            var assembly = Assembly.GetEntryAssembly();
            if (assembly is null)
            {
                return false;
            }

            var debuggable = assembly.GetCustomAttribute<System.Diagnostics.DebuggableAttribute>();
            return debuggable is null || !debuggable.IsJITOptimizerDisabled;
        }

        public void Dispose()
        {
            _shutdown.Cancel();
            _listener.Close();

            try
            {
                _listenTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }

            _shutdown.Dispose();
        }

        private async Task ListenAsync()
        {
            while (!_shutdown.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(context), _shutdown.Token);
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                if (!IsLocalRequest(context.Request) || context.Request.Url?.AbsolutePath != _path)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    context.Response.Close();
                    return;
                }

                if (context.Request.HttpMethod == "GET")
                {
                    await WriteJsonAsync(context.Response, new
                    {
                        name = "InProcess.DevTools MCP",
                        endpoint = Endpoint,
                        tools = GetEnabledToolNames()
                    }).ConfigureAwait(false);
                    return;
                }

                if (context.Request.HttpMethod != "POST")
                {
                    context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    context.Response.Close();
                    return;
                }

                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                var requestText = await reader.ReadToEndAsync().ConfigureAwait(false);

                JsonObject? request;
                try
                {
                    request = JsonNode.Parse(requestText) as JsonObject;
                }
                catch (JsonException)
                {
                    await WriteJsonAsync(context.Response, Error(null, -32700, "Parse error")).ConfigureAwait(false);
                    return;
                }

                if (request is null)
                {
                    await WriteJsonAsync(context.Response, Error(null, -32600, "Invalid Request: expected a single JSON-RPC request object.")).ConfigureAwait(false);
                    return;
                }

                var id = request["id"];
                if (id is null)
                {
                    // Notification: no response body.
                    context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                    context.Response.Close();
                    return;
                }

                object response;
                try
                {
                    response = await DispatchAsync(id, request["method"]?.GetValue<string>(), request["params"] as JsonObject).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // The id is known here, so always answer with a well-formed JSON-RPC error that carries it.
                    response = Error(id, -32603, ex.Message);
                }

                await WriteJsonAsync(context.Response, response).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    context.Response.Abort();
                }
                catch
                {
                }
            }
        }

        private async Task<object> DispatchAsync(JsonNode id, string? method, JsonObject? parameters)
        {
            switch (method)
            {
                case "initialize":
                    return Result(id, new
                    {
                        protocolVersion = parameters?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
                        capabilities = new
                        {
                            tools = new { listChanged = false }
                        },
                        serverInfo = new
                        {
                            name = "InProcess.DevTools",
                            version = typeof(DevTools).Assembly.GetName().Version?.ToString() ?? "unknown"
                        }
                    });
                case "ping":
                    return Result(id, new { });
                case "tools/list":
                    return Result(id, new { tools = CreateTools() });
                case "tools/call":
                    return await CallToolAsync(id, parameters).ConfigureAwait(false);
                default:
                    return Error(id, -32601, $"Method '{method}' not found");
            }
        }

        private async Task<object> CallToolAsync(JsonNode id, JsonObject? parameters)
        {
            var name = parameters?["name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name))
            {
                return Error(id, -32602, "Tool name is required.");
            }

            var tool = _tools.FirstOrDefault(t => t.Name == name);
            if (tool is null)
            {
                return Error(id, -32602, $"Unknown tool '{name}'");
            }

            if (!tool.Enabled())
            {
                return Error(id, -32602, $"Tool '{name}' is disabled by McpServerOptions.");
            }

            try
            {
                var args = new McpArgs(parameters!["arguments"] as JsonObject);
                if (tool.IsAction && args.Bool("wait_idle") && !Extended(_options.EnableWaiting))
                {
                    return ErrorResult(id, "wait_idle requires McpServerOptions.EnableWaiting (and is unavailable in optimized builds unless AllowInReleaseBuilds is set).");
                }

                var value = await tool.Handler(args).ConfigureAwait(false);
                if (tool.IsAction)
                {
                    // Let templates, tab content and editing controls created by the action take part in the next snapshot.
                    await Dispatcher.UIThread.InvokeAsync(UpdateLayouts).GetTask().ConfigureAwait(false);
                }

                var node = JsonSerializer.SerializeToNode(value, JsonOptions) ?? new JsonObject();

                if (tool.IsAction && args.Bool("wait_idle"))
                {
                    var idle = await WaitIdleFromArgsAsync(args).ConfigureAwait(false);
                    if (node is JsonObject obj)
                    {
                        obj["waitIdle"] = JsonSerializer.SerializeToNode(idle, JsonOptions);
                    }
                }

                return TextResult(id, node.ToJsonString(JsonOptions));
            }
            catch (McpToolException ex)
            {
                return ErrorResult(id, ex.Message);
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException { InnerException: { } cause } ? cause : ex;
                return ErrorResult(id, $"{inner.GetType().Name}: {inner.Message}");
            }
        }

        /// <summary>Runs a tool body on the UI thread.</summary>
        private static Func<McpArgs, Task<object?>> Ui(Func<McpArgs, object?> body)
        {
            return args => Dispatcher.UIThread.InvokeAsync(() => body(args)).GetTask();
        }

        private string[] GetEnabledToolNames()
        {
            return _tools.Where(t => t.Enabled()).Select(t => t.Name).ToArray();
        }

        private object[] CreateTools()
        {
            return _tools.Where(t => t.Enabled()).Select(t => (object)new
            {
                name = t.Name,
                description = t.Description,
                inputSchema = t.InputSchema
            }).ToArray();
        }

        private bool Extended(bool flag) => flag && _extendedCapabilitiesAllowed;

        // ----- JSON-RPC / MCP result shapes -----

        private static object TextResult(JsonNode id, string text)
        {
            return Result(id, new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text
                    }
                }
            });
        }

        /// <summary>A valid MCP tool result that reports a failure (isError=true) instead of a protocol error.</summary>
        private static object ErrorResult(JsonNode id, string message)
        {
            return Result(id, new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = message
                    }
                },
                isError = true
            });
        }

        private static object Result(JsonNode id, object result)
        {
            return new
            {
                jsonrpc = "2.0",
                id = Clone(id),
                result
            };
        }

        private static object Error(JsonNode? id, int code, string message)
        {
            return new
            {
                jsonrpc = "2.0",
                id = id is null ? null : Clone(id),
                error = new
                {
                    code,
                    message
                }
            };
        }

        private static JsonNode? Clone(JsonNode node)
        {
            return JsonNode.Parse(node.ToJsonString());
        }

        private static bool IsLocalRequest(HttpListenerRequest request)
        {
            return IPAddress.IsLoopback(request.RemoteEndPoint.Address);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/mcp";
            }

            return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
        }

        private string Endpoint => $"http://{_options.Host}:{_options.Port}{_path}";

        private static async Task WriteJsonAsync(HttpListenerResponse response, object value)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions));
            response.ContentType = "application/json";
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            response.Close();
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private sealed record ToolDefinition(
            string Name,
            string Description,
            object InputSchema,
            Func<bool> Enabled,
            Func<McpArgs, Task<object?>> Handler,
            bool IsAction = false);
    }
}
