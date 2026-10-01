using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace InProcess.DevTools.Tests;

public class AcceptanceTests
{
    [AvaloniaFact]
    public async Task ComboBoxCellEdit_BeginSelectCommit_UpdatesGridAndViewModel()
    {
        var screen = ScreenFactory.Create();
        using var host = new McpHost(screen.Window);

        var gridPath = await host.PathOfAsync("#ObjectsGrid");

        var begin = await host.CallAsync("devtools_begin_edit", J.Obj(("path", gridPath), ("row", 0), ("column", "Kind")));
        var editingPath = begin["editingPath"]!.GetValue<string>();
        Assert.Equal("Avalonia.Controls.ComboBox", begin["editingType"]!.GetValue<string>());

        // The editing control is addressable by path in the very next call.
        var dom = await host.CallAsync("devtools_get_dom", J.Obj(("path", editingPath), ("maxDepth", 0)));
        Assert.Equal("Avalonia.Controls.ComboBox", dom["type"]!.GetValue<string>());

        await host.CallAsync("devtools_select_item", J.Obj(("path", editingPath), ("text", "Servers")));
        var commit = await host.CallAsync("devtools_commit_edit", J.Obj(("path", gridPath)));
        Assert.True(commit["committed"]!.GetValue<bool>());
        Assert.False(commit["isEditing"]!.GetValue<bool>());

        var items = await host.CallAsync("devtools_get_items", J.Obj(("path", gridPath)));
        var row0 = items["items"]!.AsArray().Single(r => r!["index"]!.GetValue<int>() == 0)!;
        var kindCell = row0["cells"]!.AsArray().Single(c => c!["header"]!.GetValue<string>() == "Kind")!;
        Assert.Equal("Servers", kindCell["text"]!.GetValue<string>());
        Assert.Equal("Servers", screen.ViewModel.Rows[0].Kind);
        Assert.Equal("Queues", screen.ViewModel.Rows[1].Kind);
    }

    [AvaloniaFact]
    public async Task SelectItemOnTabControl_FiresSelectionChanged_AndFlipsViewModelFlag()
    {
        var screen = ScreenFactory.Create();
        using var host = new McpHost(screen.Window);
        var tabsPath = await host.PathOfAsync("#MainTabs");
        Assert.False(screen.ViewModel.TabChanged);

        var result = await host.CallAsync("devtools_select_item", J.Obj(("path", tabsPath), ("text", "Settings")));

        Assert.True(screen.ViewModel.TabChanged);
        Assert.Equal(1, screen.ViewModel.SelectedTab);
        Assert.True(result["selectionChangedRaised"]!.GetValue<bool>());

        // The newly selected tab content is part of the tree right away.
        await host.PathOfAsync("#NameBox");
    }

    [AvaloniaFact]
    public async Task GetDom_WithPath_ReturnsSubtreeBeyondTheGlobalDepthLimit()
    {
        var window = new Window { Width = 400, Height = 300 };
        Control inner = new TextBlock { Name = "leaf", Text = "deep leaf" };
        for (var i = 22; i >= 0; i--)
        {
            inner = new Border { Name = $"n{i}", Child = inner };
        }

        window.Content = inner;
        using var host = new McpHost(window);

        var path = await host.PathOfAsync("#n18");
        Assert.True(path.Split('/').Length >= 18, $"expected a deep path, got '{path}'");

        // From the root, the default depth cuts the branch off and says so.
        var whole = await host.CallAsync("devtools_get_dom", J.Obj(("rootIndex", 0), ("maxDepth", 16)));
        Assert.True(whole["depthLimited"]!.GetValue<bool>());

        // Starting at the deep node gives it its own depth budget.
        var sub = await host.CallAsync("devtools_get_dom", J.Obj(("path", path), ("maxDepth", 10)));
        Assert.Equal("n18", sub["name"]!.GetValue<string>());
        Assert.Equal(path, sub["path"]!.GetValue<string>());
        Assert.False(sub["depthLimited"]!.GetValue<bool>());

        var names = new List<string>();
        void Collect(JsonNode node)
        {
            if (node["name"]?.GetValue<string>() is { } n)
            {
                names.Add(n);
            }

            foreach (var child in node["children"]!.AsArray())
            {
                Collect(child!);
            }
        }

        Collect(sub);
        Assert.Contains("n22", names);
        Assert.Contains("leaf", names);
        Assert.Equal("deep leaf", (await host.CallAsync("devtools_find", J.Obj(("query", "#leaf"))))["matches"]![0]!["text"]!.GetValue<string>());
    }

    [AvaloniaFact]
    public async Task InvalidPath_ReturnsToolErrorWithHint_NeverAMalformedResult()
    {
        var screen = ScreenFactory.Create();
        using var host = new McpHost(screen.Window);

        var raw = await host.RpcAsync("tools/call", J.Obj(("name", "devtools_get_dom"), ("arguments", J.Obj(("path", "0/0/99/3")))));

        Assert.Equal("2.0", raw["jsonrpc"]!.GetValue<string>());
        Assert.NotNull(raw["id"]);
        Assert.Null(raw["error"]);
        var result = raw["result"]!.AsObject();
        Assert.True(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("not found", text);
        Assert.Contains("closest valid prefix: '0/0'", text);

        var badRoot = await host.TryCallAsync("devtools_get_dom", J.Obj(("rootIndex", 9)));
        Assert.True(badRoot.IsError);
        Assert.Contains("rootIndex 9 does not exist", badRoot.Text);

        var badType = await host.TryCallAsync("devtools_get_dom", J.Obj(("maxDepth", "deep")));
        Assert.True(badType.IsError);

        var unknownTool = await host.RpcAsync("tools/call", J.Obj(("name", "devtools_nope")));
        Assert.Equal(-32602, unknownTool["error"]!["code"]!.GetValue<int>());
        Assert.NotNull(unknownTool["id"]);

        var parseError = await host.RawAsync("{ not json");
        Assert.Equal(-32700, parseError["error"]!["code"]!.GetValue<int>());
    }

    [AvaloniaFact]
    public async Task Screenshot_WithOutputFile_WritesReadablePng()
    {
        var screen = ScreenFactory.Create();
        using var host = new McpHost(screen.Window);

        var result = await host.CallAsync("devtools_capture_screenshot", J.Obj(("output", "file")));

        var path = result["path"]!.GetValue<string>();
        Assert.True(File.Exists(path));
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(result["bytes"]!.GetValue<int>(), bytes.Length);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes.Take(8).ToArray());
        Assert.Equal(800, result["width"]!.GetValue<int>());
        Assert.Equal(600, result["height"]!.GetValue<int>());
        Assert.Null(result["base64"]);

        var half = await host.CallAsync("devtools_capture_screenshot", J.Obj(("output", "file"), ("scale", 0.5)));
        Assert.Equal(400, half["width"]!.GetValue<int>());

        var crop = await host.CallAsync("devtools_capture_screenshot", J.Obj(
            ("output", "file"),
            ("region", J.Obj(("x", 0), ("y", 0), ("width", 100), ("height", 50)))));
        Assert.Equal(100, crop["width"]!.GetValue<int>());
        Assert.Equal(50, crop["height"]!.GetValue<int>());

        var inline = await host.CallAsync("devtools_capture_screenshot");
        Assert.False(string.IsNullOrEmpty(inline["base64"]!.GetValue<string>()));
    }

    [AvaloniaFact]
    public async Task PasswordBox_IsRedactedInGetPropertyGetItemsAndDom()
    {
        var screen = ScreenFactory.Create();
        using var host = new McpHost(screen.Window);
        const string secret = "s3cr3t-value";

        await host.CallAsync("devtools_select_item", J.Obj(("path", await host.PathOfAsync("#MainTabs")), ("index", 1)));
        var boxPath = await host.PathOfAsync("#PasswordBox");

        var property = await host.CallAsync("devtools_get_property", J.Obj(("path", boxPath), ("propertyName", "Text")));
        Assert.True(property["redacted"]!.GetValue<bool>());
        Assert.DoesNotContain(secret, property.ToJsonString());

        var length = await host.CallAsync("devtools_get_property", J.Obj(("path", boxPath), ("propertyName", "Text.Length")));
        Assert.True(length["redacted"]!.GetValue<bool>());

        var dom = await host.CallAsync("devtools_get_dom", J.Obj(("path", boxPath), ("maxDepth", 0)));
        Assert.DoesNotContain(secret, dom.ToJsonString());

        var found = await host.CallAsync("devtools_find", J.Obj(("query", "text:s3cr3t")));
        Assert.Equal(0, found["count"]!.GetValue<int>());

        var set = await host.CallAsync("devtools_set_property", J.Obj(("path", boxPath), ("propertyName", "Text"), ("value", "another-secret")));
        Assert.DoesNotContain("another-secret", set.ToJsonString());

        // Grid cells: the Credential column hosts a PasswordChar TextBox bound to "hunter2".
        await host.CallAsync("devtools_select_item", J.Obj(("path", await host.PathOfAsync("#MainTabs")), ("index", 0)));
        var items = await host.CallAsync("devtools_get_items", J.Obj(("path", await host.PathOfAsync("#ObjectsGrid"))));
        Assert.DoesNotContain("hunter2", items.ToJsonString());
        var credential = items["items"]![0]!["cells"]!.AsArray().Single(c => c!["header"]!.GetValue<string>() == "Credential")!;
        Assert.True(credential["redacted"]!.GetValue<bool>());
        Assert.Equal("***REDACTED***", credential["text"]!.GetValue<string>());

        // View-model reads never reveal values that equal a password box's text, or secret-looking names.
        var context = await host.CallAsync("devtools_get_datacontext", J.Obj(("path", await host.PathOfAsync("#ObjectsGrid")), ("properties", new JsonArray("Rows.Count", "Title", "Password"))));
        Assert.Equal(2, context["properties"]!["Rows.Count"]!["value"]!.GetValue<int>());
        Assert.True(context["properties"]!["Password"]!["redacted"]!.GetValue<bool>());
    }
}
