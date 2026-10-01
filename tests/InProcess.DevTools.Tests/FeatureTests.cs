using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;

namespace InProcess.DevTools.Tests;

public class FeatureTests
{
    private static async Task<(ScreenFactory.Screen Screen, McpHost Host)> OpenSettingsTabAsync()
    {
        var screen = ScreenFactory.Create();
        var host = new McpHost(screen.Window);
        await host.CallAsync("devtools_select_item", J.Obj(("path", await host.PathOfAsync("#MainTabs")), ("index", 1)));
        return (screen, host);
    }

    [AvaloniaFact]
    public async Task TypeText_RoutesThroughTextInput_SoBindingsAndTextChangedFire()
    {
        var (screen, host) = await OpenSettingsTabAsync();
        using var hostScope = host;
        var textChanged = 0;
        screen.Name.TextChanged += (_, _) => textChanged++;

        var path = await host.PathOfAsync("#NameBox");
        var result = await host.CallAsync("devtools_type_text", J.Obj(("path", path), ("text", "Hello")));

        Assert.Equal("Hello", screen.Name.Text);
        Assert.Equal("Hello", screen.ViewModel.Title);
        Assert.True(textChanged > 0);
        Assert.Equal("Hello", result["text"]!.GetValue<string>());

        await host.CallAsync("devtools_type_text", J.Obj(("path", path), ("text", "World"), ("clear", true)));
        Assert.Equal("World", screen.ViewModel.Title);
    }

    [AvaloniaFact]
    public async Task SendKeys_SupportsTextSpecialKeysAndModifiers()
    {
        var (screen, host) = await OpenSettingsTabAsync();
        using var hostScope = host;
        var path = await host.PathOfAsync("#NameBox");

        await host.CallAsync("devtools_send_keys", J.Obj(("path", path), ("keys", "abcd{Left}{Left}X{Backspace}{Backspace}")));
        Assert.Equal("acd", screen.Name.Text);

        // Without a path the focused element receives the keys.
        await host.CallAsync("devtools_send_keys", J.Obj(("keys", "{End}!{{}}")));
        Assert.Equal("acd!{}", screen.Name.Text);

        // Tab moves focus on to the next control.
        await host.CallAsync("devtools_send_keys", J.Obj(("keys", "{Tab}")));
        Assert.False(screen.Name.IsFocused);

        var bad = await host.TryCallAsync("devtools_send_keys", J.Obj(("keys", "{Hyper}")));
        Assert.True(bad.IsError);
        Assert.Contains("unknown key", bad.Text);
    }

    [AvaloniaFact]
    public async Task SendKeys_F2AndEscapeDriveDataGridEditing()
    {
        var screen = ScreenFactory.Create();
        using var host = new McpHost(screen.Window);
        var gridPath = await host.PathOfAsync("#ObjectsGrid");

        await host.CallAsync("devtools_click", J.Obj(("path", gridPath)));
        await host.CallAsync("devtools_begin_edit", J.Obj(("path", gridPath), ("row", 1), ("column", 1)));
        await host.CallAsync("devtools_send_keys", J.Obj(("path", gridPath), ("keys", "{Escape}")));
        var items = await host.CallAsync("devtools_get_items", J.Obj(("path", gridPath)));
        Assert.False(items["items"]![1]!["isEditing"]!.GetValue<bool>());
        Assert.Equal("Queues", screen.ViewModel.Rows[1].Kind);
    }

    [AvaloniaFact]
    public async Task DoubleClick_OnEditableCell_StartsEditing()
    {
        var screen = ScreenFactory.Create();
        using var host = new McpHost(screen.Window);
        var gridPath = await host.PathOfAsync("#ObjectsGrid");

        var cells = await host.CallAsync("devtools_find", J.Obj(("filter", J.Obj(("types", new JsonArray("DataGridCell")))), ("path", gridPath)));
        // Row 0 has 3 cells: name, kind, credential.
        var kindCell = cells["matches"]![1]!["path"]!.GetValue<string>();

        var result = await host.CallAsync("devtools_double_click", J.Obj(("path", kindCell)));
        Assert.True(result["startedEdit"]!.GetValue<bool>());
        Assert.NotNull(result["editingPath"]);

        var editing = await host.CallAsync("devtools_get_dom", J.Obj(("path", result["editingPath"]!.GetValue<string>()), ("maxDepth", 0)));
        Assert.Equal("Avalonia.Controls.ComboBox", editing["type"]!.GetValue<string>());

        await host.CallAsync("devtools_cancel_edit", J.Obj(("path", result["editingPath"]!.GetValue<string>())));
        var items = await host.CallAsync("devtools_get_items", J.Obj(("path", gridPath)));
        Assert.False(items["items"]![0]!["isEditing"]!.GetValue<bool>());
    }

    [AvaloniaFact]
    public async Task BeginEdit_RejectsReadOnlyColumns_AndBadRows_WithHelpfulErrors()
    {
        var screen = ScreenFactory.Create();
        using var host = new McpHost(screen.Window);
        var gridPath = await host.PathOfAsync("#ObjectsGrid");

        var readOnly = await host.TryCallAsync("devtools_begin_edit", J.Obj(("path", gridPath), ("row", 0), ("column", "Name")));
        Assert.True(readOnly.IsError);
        Assert.Contains("read-only", readOnly.Text);

        var row = await host.TryCallAsync("devtools_begin_edit", J.Obj(("path", gridPath), ("row", 7), ("column", "Kind")));
        Assert.True(row.IsError);
        Assert.Contains("0..1", row.Text);

        var column = await host.TryCallAsync("devtools_begin_edit", J.Obj(("path", gridPath), ("row", 0), ("column", "Nope")));
        Assert.True(column.IsError);
        Assert.Contains("Columns:", column.Text);
    }

    [AvaloniaFact]
    public async Task SetProperty_WithRaiseEvents_FiresSelectionChangedForTabsAndGrids()
    {
        var screen = ScreenFactory.Create();
        using var host = new McpHost(screen.Window);
        var tabsPath = await host.PathOfAsync("#MainTabs");
        var gridPath = await host.PathOfAsync("#ObjectsGrid");
        var gridEvents = 0;
        screen.Grid.SelectionChanged += (_, _) => gridEvents++;

        var tab = await host.CallAsync("devtools_set_property", J.Obj(("path", tabsPath), ("propertyName", "SelectedIndex"), ("value", 1), ("raise_events", true)));
        Assert.True(screen.ViewModel.TabChanged);
        Assert.Equal(1, screen.ViewModel.SelectedTab);
        Assert.Contains("SelectionChanged", tab["eventsRaised"]!.AsArray().Select(n => n!.GetValue<string>()).Concat(tab["eventsSynthesized"]!.AsArray().Select(n => n!.GetValue<string>())));

        await host.CallAsync("devtools_select_item", J.Obj(("path", tabsPath), ("index", 0)));
        var grid = await host.CallAsync("devtools_set_property", J.Obj(("path", gridPath), ("propertyName", "SelectedIndex"), ("value", 1), ("raise_events", true)));
        Assert.True(gridEvents > 0);
        Assert.Same(screen.ViewModel.Rows[1], screen.Grid.SelectedItem);
        Assert.Equal(1, grid["eventsRaised"]!.AsArray().Count + grid["eventsSynthesized"]!.AsArray().Count);

        // Changing a TabItem's IsSelected through its own path also notifies the TabControl.
        screen.ViewModel.TabChanged = false;
        var tabItemPath = tabsPath + "/0/0/0/1";
        var found = await host.CallAsync("devtools_find", J.Obj(("filter", J.Obj(("types", new JsonArray("TabItem")))), ("path", tabsPath)));
        Assert.True(found["count"]!.GetValue<int>() >= 2);
        var secondTab = found["matches"]![1]!["path"]!.GetValue<string>();
        await host.CallAsync("devtools_set_property", J.Obj(("path", secondTab), ("propertyName", "IsSelected"), ("value", true), ("raise_events", true)));
        Assert.True(screen.ViewModel.TabChanged);
        Assert.NotNull(tabItemPath);
    }

    [AvaloniaFact]
    public async Task Click_SelectsTabs_TogglesCheckBoxes_SelectsGridRows_AndRunsCommands()
    {
        var screen = ScreenFactory.Create();
        using var host = new McpHost(screen.Window);
        var tabs = await host.CallAsync("devtools_find", J.Obj(("filter", J.Obj(("types", new JsonArray("TabItem"))))));
        var settingsTab = tabs["matches"]![1]!["path"]!.GetValue<string>();

        await host.CallAsync("devtools_click", J.Obj(("path", settingsTab)));
        Assert.Equal(1, screen.ViewModel.SelectedTab);
        Assert.True(screen.ViewModel.TabChanged);

        var check = await host.PathOfAsync("#FlagCheck");
        await host.CallAsync("devtools_click", J.Obj(("path", check)));
        Assert.True(screen.ViewModel.Flag);

        var run = await host.PathOfAsync("#RunButton");
        await host.CallAsync("devtools_invoke_command", J.Obj(("path", run)));
        Assert.Equal(1, screen.ViewModel.Executed);
        var missing = await host.TryCallAsync("devtools_invoke_command", J.Obj(("path", run), ("commandProperty", "Nope")));
        Assert.True(missing.IsError);
        Assert.Contains("ICommand properties: Command", missing.Text);

        await host.CallAsync("devtools_click", J.Obj(("path", tabs["matches"]![0]!["path"]!.GetValue<string>())));
        var rows = await host.CallAsync("devtools_find", J.Obj(("filter", J.Obj(("types", new JsonArray("DataGridRow"))))));
        string? rowOne = null;
        foreach (var match in rows["matches"]!.AsArray())
        {
            var path = match!["path"]!.GetValue<string>();
            var dom = await host.CallAsync("devtools_get_dom", J.Obj(("path", path), ("maxDepth", 0)));
            if (dom["state"]!["index"]!.GetValue<int>() == 1)
            {
                rowOne = path;
            }
        }

        Assert.NotNull(rowOne);
        await host.CallAsync("devtools_click", J.Obj(("path", rowOne)));
        Assert.Same(screen.ViewModel.Rows[1], screen.Grid.SelectedItem);
    }

    [AvaloniaFact]
    public async Task GetProperty_And_GetDataContext_ReadViewModelValues()
    {
        var screen = ScreenFactory.Create();
        using var host = new McpHost(screen.Window);
        screen.ViewModel.Title = "Integration";
        var tabs = await host.PathOfAsync("#MainTabs");
        Assert.Equal(2, (await host.CallAsync("devtools_get_property", J.Obj(("path", await host.PathOfAsync("#ObjectsGrid")), ("propertyName", "ItemsSourceCount"))))["value"]!.GetValue<int>());
        await host.CallAsync("devtools_select_item", J.Obj(("path", tabs), ("index", 1)));

        Assert.Equal(1, (await host.CallAsync("devtools_get_property", J.Obj(("path", tabs), ("propertyName", "SelectedIndex"))))["value"]!.GetValue<int>());
        var item = await host.CallAsync("devtools_get_property", J.Obj(("path", tabs), ("propertyName", "SelectedItem")));
        Assert.Equal("Avalonia.Controls.TabItem", item["value"]!["type"]!.GetValue<string>());
        Assert.Equal("Integration", (await host.CallAsync("devtools_get_property", J.Obj(("path", await host.PathOfAsync("#NameBox")), ("propertyName", "Text"))))["value"]!.GetValue<string>());
        Assert.Equal(typeof(ScreenViewModel).FullName, (await host.CallAsync("devtools_get_property", J.Obj(("path", await host.PathOfAsync("#NameBox")), ("propertyName", "DataContextType"))))["value"]!.GetValue<string>());
        Assert.False((await host.CallAsync("devtools_get_property", J.Obj(("path", await host.PathOfAsync("#FlagCheck")), ("propertyName", "IsChecked"))))["value"]!.GetValue<bool>());

        var missing = await host.TryCallAsync("devtools_get_property", J.Obj(("path", tabs), ("propertyName", "Nope")));
        Assert.True(missing.IsError);

        var context = await host.CallAsync("devtools_get_datacontext", J.Obj(("path", tabs), ("properties", new JsonArray("Title", "SelectedTab", "Missing"))));
        Assert.Equal("Integration", context["properties"]!["Title"]!["value"]!.GetValue<string>());
        Assert.Equal(1, context["properties"]!["SelectedTab"]!["value"]!.GetValue<int>());
        Assert.NotNull(context["properties"]!["Missing"]!["error"]);

        var names = await host.CallAsync("devtools_get_datacontext", J.Obj(("path", tabs)));
        Assert.Contains("Title", names["availableProperties"]!.AsArray().Select(n => n!.GetValue<string>()));

        var combo = await host.CallAsync("devtools_get_items", J.Obj(("path", tabs)));
        Assert.Equal(new[] { "Objects", "Settings" }, combo["items"]!.AsArray().Select(n => n!["text"]!.GetValue<string>()).ToArray());
    }

    [AvaloniaFact]
    public async Task WaitIdle_WaitsForAsyncViewModelWork_AndReportsTimeouts()
    {
        var (screen, host) = await OpenSettingsTabAsync();
        using var hostScope = host;

        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => screen.ViewModel.Title = "loaded");
        });

        var result = await host.CallAsync("devtools_wait_idle", J.Obj(
            ("timeoutMs", 5000),
            ("conditions", new JsonArray(J.Obj(("kind", "property_equals"), ("path", await host.PathOfAsync("#NameBox")), ("propertyName", "Text"), ("value", "loaded"))))));
        Assert.True(result["satisfied"]!.GetValue<bool>());
        Assert.Equal("loaded", screen.ViewModel.Title);

        var timeout = await host.CallAsync("devtools_wait_idle", J.Obj(
            ("timeoutMs", 300),
            ("conditions", new JsonArray(J.Obj(("kind", "exists"), ("query", "#NeverThere"))))));
        Assert.True(timeout["timedOut"]!.GetValue<bool>());

        var bad = await host.TryCallAsync("devtools_wait_idle", J.Obj(("conditions", new JsonArray(J.Obj(("kind", "sleeping"))))));
        Assert.True(bad.IsError);

        var box = await host.CallAsync("devtools_wait_idle", J.Obj(("timeoutMs", 1000), ("conditions", new JsonArray(J.Obj(("kind", "visible"), ("query", "#PasswordBox"))))));
        Assert.True(box["satisfied"]!.GetValue<bool>());
        var secret = await host.TryCallAsync("devtools_wait_idle", J.Obj(("timeoutMs", 1000), ("conditions", new JsonArray(J.Obj(("kind", "property_equals"), ("query", "#PasswordBox"), ("propertyName", "Text"), ("value", "x"))))));
        Assert.True(secret.IsError);
    }

    [AvaloniaFact]
    public async Task ActionTools_AcceptWaitIdle()
    {
        var screen = ScreenFactory.Create();
        using var host = new McpHost(screen.Window);

        var result = await host.CallAsync("devtools_select_item", J.Obj(("path", await host.PathOfAsync("#MainTabs")), ("index", 1), ("wait_idle", true)));
        Assert.True(result["waitIdle"]!["satisfied"]!.GetValue<bool>());
    }

    [AvaloniaFact]
    public async Task GetDom_FilterMaxNodesAndFileOutput()
    {
        var (screen, host) = await OpenSettingsTabAsync();
        using var hostScope = host;

        var filtered = await host.CallAsync("devtools_get_dom", J.Obj(("rootIndex", 0), ("maxDepth", 64),
            ("filter", J.Obj(("types", new JsonArray("TextBox")), ("isVisible", true)))));
        var matched = new List<string>();
        void Walk(JsonNode node)
        {
            if (node["matched"]!.GetValue<bool>())
            {
                matched.Add(node["name"]?.GetValue<string>() ?? node["type"]!.GetValue<string>());
            }

            foreach (var child in node["children"]!.AsArray())
            {
                Walk(child!);
            }
        }

        Walk(filtered);
        Assert.Contains("NameBox", matched);
        Assert.Contains("PasswordBox", matched);
        Assert.All(matched, name => Assert.True(name is "NameBox" or "PasswordBox" || name.EndsWith("TextBox")));

        var interactive = await host.CallAsync("devtools_find", J.Obj(("filter", J.Obj(("interactiveOnly", true), ("textContains", "Run")))));
        Assert.Equal("RunButton", interactive["matches"]![0]!["name"]!.GetValue<string>());

        var small = await host.CallAsync("devtools_get_dom", J.Obj(("rootIndex", 0), ("maxDepth", 64), ("maxNodes", 10)));
        Assert.True(small["truncated"]!.GetValue<bool>());
        Assert.Equal(10, small["nodeCount"]!.GetValue<int>());

        var tree = await host.CallAsync("devtools_get_tree", J.Obj(("rootIndex", 0), ("maxDepth", 2)));
        Assert.NotNull(tree["children"]);
        Assert.NotNull(tree["childCount"]);

        var file = await host.CallAsync("devtools_get_dom", J.Obj(("rootIndex", 0), ("maxDepth", 64), ("output", "file")));
        var json = await File.ReadAllTextAsync(file["path"]!.GetValue<string>());
        Assert.Equal(file["bytes"]!.GetValue<int>(), System.Text.Encoding.UTF8.GetByteCount(json));
        Assert.Equal("Avalonia.Controls.Window", JsonNode.Parse(json)!["type"]!.GetValue<string>());
    }

    [AvaloniaFact]
    public async Task Capabilities_AreOffByDefault_AndBlockedInOptimizedBuilds()
    {
        var screen = ScreenFactory.Create();
        using (var host = new McpHost(screen.Window, new McpServerOptions { EnableDomInspection = true }))
        {
            var list = await host.RpcAsync("tools/list");
            var names = list["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToArray();
            Assert.Contains("devtools_find", names);
            foreach (var hidden in new[]
                     {
                         "devtools_begin_edit", "devtools_commit_edit", "devtools_cancel_edit", "devtools_send_keys", "devtools_type_text",
                         "devtools_select_item", "devtools_get_property", "devtools_get_items", "devtools_get_datacontext",
                         "devtools_wait_idle", "devtools_invoke_command", "devtools_double_click", "devtools_click", "devtools_set_property"
                     })
            {
                Assert.DoesNotContain(hidden, names);
            }

            var denied = await host.RpcAsync("tools/call", J.Obj(("name", "devtools_select_item")));
            Assert.Equal(-32602, denied["error"]!["code"]!.GetValue<int>());

            var file = await host.TryCallAsync("devtools_get_dom", J.Obj(("output", "file")));
            Assert.True(file.IsError);
            Assert.Contains("EnableFileOutput", file.Text);
        }

        using (var host = new McpHost(ScreenFactory.Create().Window, McpHost.Everything(), isOptimizedBuild: () => true))
        {
            var names = (await host.RpcAsync("tools/list"))["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToArray();
            Assert.Contains("devtools_click", names);
            Assert.Contains("devtools_set_property", names);
            Assert.DoesNotContain("devtools_select_item", names);
            Assert.DoesNotContain("devtools_begin_edit", names);
            Assert.DoesNotContain("devtools_get_property", names);
            Assert.DoesNotContain("devtools_double_click", names);
        }

        var allowed = McpHost.Everything();
        allowed.AllowInReleaseBuilds = true;
        using (var host = new McpHost(ScreenFactory.Create().Window, allowed, isOptimizedBuild: () => true))
        {
            var names = (await host.RpcAsync("tools/list"))["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToArray();
            Assert.Contains("devtools_select_item", names);
        }

    }

    [AvaloniaFact]
    public async Task EveryToolIsDocumentedWithItsParametersAndPathConventions()
    {
        var screen = ScreenFactory.Create();
        using var host = new McpHost(screen.Window);
        var tools = (await host.RpcAsync("tools/list"))["result"]!["tools"]!.AsArray();

        foreach (var tool in tools)
        {
            var name = tool!["name"]!.GetValue<string>();
            var description = tool["description"]!.GetValue<string>();
            Assert.True(description.Length > 40, name);
            if (name is not ("devtools_get_status" or "devtools_list_roots"))
            {
                Assert.Contains(name == "devtools_wait_idle" || name == "devtools_find" ? "rootIndex" : "path", description + tool["inputSchema"]!.ToJsonString());
            }

            foreach (var property in tool["inputSchema"]!["properties"]!.AsObject())
            {
                Assert.True(property.Value!["description"] is not null || property.Key == "value", $"{name}.{property.Key} has no description");
            }
        }

        Assert.True(tools.Count >= 22);
    }
}
