using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using InProcess.DevTools.Mcp;

namespace InProcess.DevTools.Tests;

/// <summary>Runs the MCP server for a window and speaks real JSON-RPC to it over HTTP.</summary>
public sealed class McpHost : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly InProcessDevToolsMcpServer _server;
    private int _nextId = 1;

    public McpHost(Window window, McpServerOptions? options = null, Func<bool>? isOptimizedBuild = null)
    {
        Port = FreePort();
        Options = options ?? Everything();
        Options.Port = Port;
        window.Show();
        _server = new InProcessDevToolsMcpServer(new SingleViewTopLevelGroup(window), Options, isOptimizedBuild ?? (() => false));
    }

    public int Port { get; }

    public McpServerOptions Options { get; }

    public static McpServerOptions Everything() => new()
    {
        EnableDomInspection = true,
        EnableScreenshots = true,
        EnableNavigation = true,
        EnableEvents = true,
        EnableStateMutation = true,
        EnableEditing = true,
        EnableInput = true,
        EnableSelection = true,
        EnableMutationEvents = true,
        EnableValueInspection = true,
        EnableWaiting = true,
        EnableCommands = true,
        EnableFileOutput = true
    };

    public async Task<JsonObject> RawAsync(string body)
    {
        var response = await _http.PostAsync($"http://127.0.0.1:{Port}/mcp", new StringContent(body, Encoding.UTF8, "application/json"));
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }

    public Task<JsonObject> RpcAsync(string method, JsonObject? parameters = null)
    {
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = _nextId++, ["method"] = method };
        if (parameters is not null)
        {
            request["params"] = parameters;
        }

        return RawAsync(request.ToJsonString());
    }

    /// <summary>Calls a tool and returns its parsed JSON payload; fails the test if the call is an error.</summary>
    public async Task<JsonNode> CallAsync(string tool, JsonObject? arguments = null)
    {
        var (payload, isError, text) = await TryCallAsync(tool, arguments);
        Assert.False(isError, $"{tool} failed: {text}");
        return payload!;
    }

    public async Task<(JsonNode? Payload, bool IsError, string Text)> TryCallAsync(string tool, JsonObject? arguments = null)
    {
        var response = await RpcAsync("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments ?? new JsonObject() });
        Assert.Null(response["error"]);
        var result = response["result"]!.AsObject();
        Assert.NotNull(result["content"]);
        var text = result["content"]!.AsArray()[0]!["text"]!.GetValue<string>();
        var isError = result["isError"]?.GetValue<bool>() ?? false;
        return (isError ? null : JsonNode.Parse(text), isError, text);
    }

    public async Task<string> PathOfAsync(string query, string tree = "visual")
    {
        var found = await CallAsync("devtools_find", new JsonObject { ["query"] = query, ["tree"] = tree });
        var matches = found["matches"]!.AsArray();
        Assert.True(matches.Count > 0, $"nothing matched '{query}'");
        return matches[0]!["path"]!.GetValue<string>();
    }

    public void Dispose()
    {
        _server.Dispose();
        _http.Dispose();
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public static class J
{
    public static JsonObject Obj(params (string Key, object? Value)[] items)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in items)
        {
            obj[key] = value switch
            {
                null => null,
                JsonNode node => node,
                _ => JsonValue.Create(value)
            };
        }

        return obj;
    }
}
