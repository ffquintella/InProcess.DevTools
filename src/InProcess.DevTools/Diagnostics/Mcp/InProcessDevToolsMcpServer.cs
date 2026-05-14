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
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace InProcess.DevTools.Mcp
{
    internal sealed class InProcessDevToolsMcpServer : IDisposable
    {
        private readonly IDevToolsTopLevelGroup _topLevelGroup;
        private readonly McpServerOptions _options;
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Task _listenTask;
        private readonly string _path;

        public InProcessDevToolsMcpServer(IDevToolsTopLevelGroup topLevelGroup, McpServerOptions options)
        {
            _topLevelGroup = topLevelGroup ?? throw new ArgumentNullException(nameof(topLevelGroup));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _path = NormalizePath(_options.Path);

            _listener.Prefixes.Add($"http://{_options.Host}:{_options.Port}/");
            _listener.Start();
            _listenTask = Task.Run(ListenAsync);
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
                catch when (_shutdown.IsCancellationRequested)
                {
                    return;
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
                var request = JsonNode.Parse(requestText)?.AsObject();

                if (request is null)
                {
                    await WriteJsonAsync(context.Response, Error(null, -32700, "Parse error")).ConfigureAwait(false);
                    return;
                }

                var id = request["id"];
                var method = request["method"]?.GetValue<string>();

                if (id is null)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                    context.Response.Close();
                    return;
                }

                await WriteJsonAsync(context.Response, Dispatch(id, method, request["params"]?.AsObject())).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await WriteJsonAsync(context.Response, Error(null, -32603, ex.Message)).ConfigureAwait(false);
            }
        }

        private object Dispatch(JsonNode id, string? method, JsonObject? parameters)
        {
            return method switch
            {
                "initialize" => Result(id, new
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
                }),
                "ping" => Result(id, new { }),
                "tools/list" => Result(id, new { tools = CreateTools() }),
                "tools/call" => CallTool(id, parameters),
                _ => Error(id, -32601, $"Method '{method}' not found")
            };
        }

        private object CallTool(JsonNode id, JsonObject? parameters)
        {
            var name = parameters?["name"]?.GetValue<string>();
            var arguments = parameters?["arguments"]?.AsObject();

            if (name is not null && !IsToolEnabled(name))
            {
                return Error(id, -32602, $"Tool '{name}' is disabled by McpServerOptions.");
            }

            return name switch
            {
                "devtools_get_status" => TextResult(id, CaptureJson(() => GetStatus())),
                "devtools_list_roots" => TextResult(id, CaptureJson(() => GetRoots())),
                "devtools_get_dom" => TextResult(id, CaptureJson(() => GetDom(arguments))),
                "devtools_get_tree" => TextResult(id, CaptureJson(() => GetTree(arguments))),
                "devtools_capture_screenshot" => TextResult(id, CaptureJson(() => CaptureScreenshot(arguments))),
                "devtools_focus" => TextResult(id, CaptureJson(() => Focus(arguments))),
                "devtools_click" => TextResult(id, CaptureJson(() => Click(arguments))),
                "devtools_raise_event" => TextResult(id, CaptureJson(() => RaiseSupportedEvent(arguments))),
                "devtools_set_property" => TextResult(id, CaptureJson(() => SetProperty(arguments))),
                _ => Error(id, -32602, $"Unknown tool '{name}'")
            };
        }

        private object GetStatus()
        {
            var roots = _topLevelGroup.Items
                .Where(item => item is not Views.MainWindow)
                .Select((item, index) => DescribeRoot(item, index))
                .ToArray();

            return new
            {
                endpoint = Endpoint,
                rootCount = roots.Length,
                roots
            };
        }

        private object GetRoots()
        {
            return _topLevelGroup.Items
                .Where(item => item is not Views.MainWindow)
                .Select((item, index) => DescribeRoot(item, index))
                .ToArray();
        }

        private object GetTree(JsonObject? arguments)
        {
            var tree = arguments?["tree"]?.GetValue<string>() ?? "visual";
            var maxDepth = Math.Clamp(arguments?["maxDepth"]?.GetValue<int>() ?? 4, 0, 12);
            var rootIndex = arguments?["rootIndex"]?.GetValue<int>();
            var roots = _topLevelGroup.Items.Where(item => item is not Views.MainWindow).ToArray();

            if (rootIndex is not null)
            {
                if (rootIndex < 0 || rootIndex >= roots.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(rootIndex), "The requested rootIndex does not exist.");
                }

                return DescribeNode(roots[rootIndex.Value], tree, 0, maxDepth);
            }

            return roots.Select(root => DescribeNode(root, tree, 0, maxDepth)).ToArray();
        }

        private object GetDom(JsonObject? arguments)
        {
            var tree = arguments?["tree"]?.GetValue<string>() ?? "visual";
            var maxDepth = Math.Clamp(arguments?["maxDepth"]?.GetValue<int>() ?? 6, 0, 16);
            var rootIndex = arguments?["rootIndex"]?.GetValue<int>();
            var roots = _topLevelGroup.Items.Where(item => item is not Views.MainWindow).ToArray();

            if (rootIndex is not null)
            {
                if (rootIndex < 0 || rootIndex >= roots.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(rootIndex), "The requested rootIndex does not exist.");
                }

                return DescribeDomNode(roots[rootIndex.Value], tree, rootIndex.Value, string.Empty, 0, maxDepth);
            }

            return roots.Select((root, index) => DescribeDomNode(root, tree, index, string.Empty, 0, maxDepth)).ToArray();
        }

        private object CaptureScreenshot(JsonObject? arguments)
        {
            var target = ResolveTarget(arguments, requireControl: true);
            var dpi = arguments?["dpi"]?.GetValue<double>() ?? 96;

            if (target.Node is not Control control)
            {
                throw new InvalidOperationException("The target node is not a Control and cannot be rendered.");
            }

            using var stream = new MemoryStream();
            control.RenderTo(stream, dpi);

            return new
            {
                target = target.DomId,
                mimeType = "image/png",
                base64 = Convert.ToBase64String(stream.ToArray())
            };
        }

        private object Focus(JsonObject? arguments)
        {
            var target = ResolveTarget(arguments, requireControl: true);

            if (target.Node is not Control control)
            {
                throw new InvalidOperationException("The target node is not focusable.");
            }

            return new
            {
                target = target.DomId,
                focused = control.Focus()
            };
        }

        private object Click(JsonObject? arguments)
        {
            var target = ResolveTarget(arguments, requireControl: true);
            InvokeClick(target.Node);

            return new
            {
                target = target.DomId,
                eventName = "click",
                raised = true
            };
        }

        private object RaiseSupportedEvent(JsonObject? arguments)
        {
            var eventName = arguments?["eventName"]?.GetValue<string>() ?? "click";
            var target = ResolveTarget(arguments, requireControl: true);

            switch (eventName.ToLowerInvariant())
            {
                case "click":
                    InvokeClick(target.Node);
                    break;
                case "focus":
                    if (target.Node is not Control control)
                    {
                        throw new InvalidOperationException("The target node is not focusable.");
                    }

                    control.Focus();
                    break;
                default:
                    throw new NotSupportedException($"Event '{eventName}' is not supported by the MCP server.");
            }

            return new
            {
                target = target.DomId,
                eventName,
                raised = true
            };
        }

        private object SetProperty(JsonObject? arguments)
        {
            var propertyName = arguments?["propertyName"]?.GetValue<string>();
            var value = arguments?["value"];
            var target = ResolveTarget(arguments, requireControl: false);

            if (string.IsNullOrWhiteSpace(propertyName))
            {
                throw new ArgumentException("propertyName is required.");
            }

            var property = target.Node.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property is null || !property.CanWrite)
            {
                throw new InvalidOperationException($"Property '{propertyName}' is not a writable CLR property on {target.Node.GetType().FullName}.");
            }

            var convertedValue = ConvertJsonValue(value, property.PropertyType);
            property.SetValue(target.Node, convertedValue);

            return new
            {
                target = target.DomId,
                propertyName,
                value = convertedValue?.ToString(),
                updated = true
            };
        }

        private static object DescribeRoot(TopLevel root, int index)
        {
            return new
            {
                index,
                type = root.GetType().FullName,
                name = (root as INamed)?.Name,
                title = root is Window window ? window.Title : null,
                isVisible = root.IsVisible
            };
        }

        private static object DescribeNode(AvaloniaObject node, string tree, int depth, int maxDepth)
        {
            var children = depth >= maxDepth
                ? Array.Empty<object>()
                : GetChildren(node, tree).Select(child => DescribeNode(child, tree, depth + 1, maxDepth)).ToArray();

            return new
            {
                type = node.GetType().FullName,
                name = (node as INamed)?.Name,
                classes = node is StyledElement styledElement ? styledElement.Classes.ToArray() : Array.Empty<string>(),
                bounds = node is Visual visual ? visual.Bounds.ToString() : null,
                children
            };
        }

        private static object DescribeDomNode(AvaloniaObject node, string tree, int rootIndex, string path, int depth, int maxDepth)
        {
            var children = GetChildren(node, tree).ToArray();
            var childDescriptions = depth >= maxDepth
                ? Array.Empty<object>()
                : children.Select((child, index) => DescribeDomNode(child, tree, rootIndex, AppendPath(path, index), depth + 1, maxDepth)).ToArray();

            return new
            {
                domId = CreateDomId(rootIndex, path),
                rootIndex,
                path,
                type = node.GetType().FullName,
                name = (node as INamed)?.Name,
                text = GetText(node),
                classes = node is StyledElement styledElement ? styledElement.Classes.ToArray() : Array.Empty<string>(),
                bounds = node is Visual visual ? visual.Bounds.ToString() : null,
                isVisible = node is Visual { IsVisible: var isVisible } ? isVisible : null as bool?,
                isEnabled = node is InputElement { IsEnabled: var isEnabled } ? isEnabled : null as bool?,
                state = GetState(node),
                children = childDescriptions
            };
        }

        private static IEnumerable<AvaloniaObject> GetChildren(AvaloniaObject node, string tree)
        {
            if (string.Equals(tree, "logical", StringComparison.OrdinalIgnoreCase) && node is ILogical logical)
            {
                return logical.LogicalChildren.OfType<AvaloniaObject>();
            }

            if (node is Visual visual)
            {
                return visual.GetVisualChildren().OfType<AvaloniaObject>();
            }

            return Enumerable.Empty<AvaloniaObject>();
        }

        private string CaptureJson(Func<object> capture)
        {
            var value = Dispatcher.UIThread.CheckAccess()
                ? capture()
                : Dispatcher.UIThread.InvokeAsync(capture).GetAwaiter().GetResult();

            return JsonSerializer.Serialize(value, JsonOptions);
        }

        private TargetNode ResolveTarget(JsonObject? arguments, bool requireControl)
        {
            var tree = arguments?["tree"]?.GetValue<string>() ?? "visual";
            var rootIndex = arguments?["rootIndex"]?.GetValue<int>() ?? 0;
            var path = arguments?["path"]?.GetValue<string>() ?? string.Empty;
            var roots = _topLevelGroup.Items.Where(item => item is not Views.MainWindow).ToArray();

            if (rootIndex < 0 || rootIndex >= roots.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(rootIndex), "The requested rootIndex does not exist.");
            }

            AvaloniaObject current = roots[rootIndex];
            foreach (var segment in ParsePath(path))
            {
                var children = GetChildren(current, tree).ToArray();
                if (segment < 0 || segment >= children.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(path), $"Path segment '{segment}' does not exist.");
                }

                current = children[segment];
            }

            if (requireControl && current is not Control)
            {
                throw new InvalidOperationException("The target node must be a Control.");
            }

            return new TargetNode(current, CreateDomId(rootIndex, path));
        }

        private static void InvokeClick(AvaloniaObject node)
        {
            switch (node)
            {
                case ToggleButton toggleButton:
                    toggleButton.IsChecked = toggleButton.IsChecked != true;
                    if (toggleButton.Command?.CanExecute(toggleButton.CommandParameter) == true)
                    {
                        toggleButton.Command.Execute(toggleButton.CommandParameter);
                    }

                    return;
                case Button button:
                    if (button.Command?.CanExecute(button.CommandParameter) == true)
                    {
                        button.Command.Execute(button.CommandParameter);
                    }

                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return;
                case MenuItem menuItem:
                    if (menuItem.Command?.CanExecute(menuItem.CommandParameter) == true)
                    {
                        menuItem.Command.Execute(menuItem.CommandParameter);
                    }

                    menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    return;
                case Control control:
                    control.Focus();
                    return;
                default:
                    throw new InvalidOperationException("The target node does not support click navigation.");
            }
        }

        private static object GetState(AvaloniaObject node)
        {
            return node switch
            {
                TextBox textBox => new
                {
                    text = textBox.Text
                },
                ToggleButton toggleButton => new
                {
                    isChecked = toggleButton.IsChecked
                },
                SelectingItemsControl selectingItemsControl => new
                {
                    selectedIndex = selectingItemsControl.SelectedIndex,
                    selectedItem = selectingItemsControl.SelectedItem?.ToString()
                },
                RangeBase rangeBase => new
                {
                    value = rangeBase.Value,
                    minimum = rangeBase.Minimum,
                    maximum = rangeBase.Maximum
                },
                _ => new { }
            };
        }

        private static string? GetText(AvaloniaObject node)
        {
            return node switch
            {
                TextBlock textBlock => textBlock.Text,
                TextBox textBox => textBox.Text,
                HeaderedContentControl headeredContentControl => headeredContentControl.Header?.ToString(),
                ContentControl contentControl => contentControl.Content?.ToString(),
                _ => null
            };
        }

        private static object? ConvertJsonValue(JsonNode? value, Type targetType)
        {
            if (value is null)
            {
                return null;
            }

            var nullableType = Nullable.GetUnderlyingType(targetType);
            var actualType = nullableType ?? targetType;

            if (actualType == typeof(string))
            {
                return value.GetValue<string>();
            }

            if (actualType == typeof(bool))
            {
                return value.GetValue<bool>();
            }

            if (actualType == typeof(int))
            {
                return value.GetValue<int>();
            }

            if (actualType == typeof(double))
            {
                return value.GetValue<double>();
            }

            if (actualType.IsEnum)
            {
                return Enum.Parse(actualType, value.GetValue<string>(), ignoreCase: true);
            }

            return Convert.ChangeType(value.ToString(), actualType);
        }

        private static IEnumerable<int> ParsePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                yield break;
            }

            foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                yield return int.Parse(segment);
            }
        }

        private static string AppendPath(string path, int index)
        {
            return string.IsNullOrEmpty(path) ? index.ToString() : $"{path}/{index}";
        }

        private static string CreateDomId(int rootIndex, string path)
        {
            return string.IsNullOrEmpty(path) ? $"root:{rootIndex}" : $"root:{rootIndex}:{path}";
        }

        private object[] CreateTools()
        {
            var tools = new List<object>
            {
                new
                {
                    name = "devtools_get_status",
                    description = "Returns the InProcess.DevTools MCP endpoint and attached Avalonia root summary.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { }
                    }
                },
            };

            if (_options.EnableDomInspection)
            {
                tools.Add(new
                {
                    name = "devtools_list_roots",
                    description = "Lists attached Avalonia top-level roots available for inspection.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { }
                    }
                });
                tools.Add(new
                {
                    name = "devtools_get_dom",
                    description = "Returns a DOM-like snapshot with stable rootIndex/path selectors, bounds, text, classes, and control state.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            tree = new
                            {
                                type = "string",
                                description = "Tree kind: visual or logical.",
                                @default = "visual"
                            },
                            rootIndex = new
                            {
                                type = "integer",
                                description = "Optional root index from devtools_list_roots."
                            },
                            maxDepth = new
                            {
                                type = "integer",
                                description = "Maximum depth to return. Default is 6, maximum is 16.",
                                @default = 6
                            }
                        }
                    }
                });
                tools.Add(new
                {
                    name = "devtools_get_tree",
                    description = "Returns a read-only snapshot of the visual or logical tree.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            tree = new
                            {
                                type = "string",
                                description = "Tree kind: visual or logical.",
                                @default = "visual"
                            },
                            rootIndex = new
                            {
                                type = "integer",
                                description = "Optional root index from devtools_list_roots."
                            },
                            maxDepth = new
                            {
                                type = "integer",
                                description = "Maximum depth to return. Default is 4, maximum is 12.",
                                @default = 4
                            }
                        }
                    }
                });
            }

            if (_options.EnableScreenshots)
            {
                tools.Add(new
                {
                    name = "devtools_capture_screenshot",
                    description = "Captures a PNG screenshot of a target control and returns it as base64.",
                    inputSchema = TargetSchema(new
                    {
                        dpi = new
                        {
                            type = "number",
                            description = "Screenshot DPI. Default is 96.",
                            @default = 96
                        }
                    })
                });
            }

            if (_options.EnableNavigation)
            {
                tools.Add(new
                {
                    name = "devtools_focus",
                    description = "Moves keyboard focus to a target control.",
                    inputSchema = TargetSchema()
                });
                tools.Add(new
                {
                    name = "devtools_click",
                    description = "Navigates the application by invoking a supported click action on Button, ToggleButton, MenuItem, or focusing a generic Control.",
                    inputSchema = TargetSchema()
                });
            }

            if (_options.EnableEvents)
            {
                tools.Add(new
                {
                    name = "devtools_raise_event",
                    description = "Raises a supported UI event on a target. Supported eventName values are click and focus.",
                    inputSchema = TargetSchema(new
                    {
                        eventName = new
                        {
                            type = "string",
                            description = "Supported values: click, focus.",
                            @default = "click"
                        }
                    })
                });
            }

            if (_options.EnableStateMutation)
            {
                tools.Add(new
                {
                    name = "devtools_set_property",
                    description = "Sets a writable public CLR property on a target Avalonia object for state manipulation.",
                    inputSchema = TargetSchema(new
                    {
                        propertyName = new
                        {
                            type = "string",
                            description = "Writable public CLR property name, for example Text, IsChecked, SelectedIndex, or Value."
                        },
                        value = new
                        {
                            description = "New property value."
                        }
                    })
                });
            }

            return tools.ToArray();
        }

        private string[] GetEnabledToolNames()
        {
            return ToolNames.Where(IsToolEnabled).ToArray();
        }

        private bool IsToolEnabled(string name)
        {
            return name switch
            {
                "devtools_get_status" => true,
                "devtools_list_roots" => _options.EnableDomInspection,
                "devtools_get_dom" => _options.EnableDomInspection,
                "devtools_get_tree" => _options.EnableDomInspection,
                "devtools_capture_screenshot" => _options.EnableScreenshots,
                "devtools_focus" => _options.EnableNavigation,
                "devtools_click" => _options.EnableNavigation,
                "devtools_raise_event" => _options.EnableEvents,
                "devtools_set_property" => _options.EnableStateMutation,
                _ => false
            };
        }

        private static object TargetSchema(object? extraProperties = null)
        {
            var properties = new Dictionary<string, object?>
            {
                ["tree"] = new
                {
                    type = "string",
                    description = "Tree kind used to resolve path: visual or logical.",
                    @default = "visual"
                },
                ["rootIndex"] = new
                {
                    type = "integer",
                    description = "Root index from devtools_list_roots.",
                    @default = 0
                },
                ["path"] = new
                {
                    type = "string",
                    description = "Slash-delimited child path from devtools_get_dom, for example \"0/2/1\". Empty string targets the root.",
                    @default = string.Empty
                }
            };

            if (extraProperties is not null)
            {
                foreach (var property in extraProperties.GetType().GetProperties())
                {
                    properties[property.Name] = property.GetValue(extraProperties);
                }
            }

            return new
            {
                type = "object",
                properties
            };
        }

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
            WriteIndented = true
        };

        private static readonly string[] ToolNames =
        {
            "devtools_get_status",
            "devtools_list_roots",
            "devtools_get_dom",
            "devtools_get_tree",
            "devtools_capture_screenshot",
            "devtools_focus",
            "devtools_click",
            "devtools_raise_event",
            "devtools_set_property"
        };

        private readonly record struct TargetNode(AvaloniaObject Node, string DomId);
    }
}
