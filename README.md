# InProcess.DevTools

[![NuGet](https://img.shields.io/nuget/v/InProcess.DevTools.svg)](https://www.nuget.org/packages/InProcess.DevTools/)

An in-process DevTools window for inspecting the visual tree, styles, properties, and events of Avalonia applications directly within your running app.

---

## ⚠️ Important Disclaimer

**InProcess.DevTools is an unofficial fork of the original `Avalonia.Diagnostics` project.**

- **No Official Relation:** This project is independent and has **no relation** to the official Avalonia team or the AvaloniaUI organization.
- **No Feature Parity:** This fork does **not** aim to maintain feature parity with the original `Avalonia.Diagnostics` or any newer official Avalonia debugging tools (like the standalone DevTools).
- **Maintenance:** It is provided "as-is" to support developers who specifically prefer the legacy in-process debugging experience in modern Avalonia versions (12+).

---

## About This Fork

The original `Avalonia.Diagnostics` package was deprecated and removed from recent Avalonia versions in favor of standalone developer tools. This fork revives and maintains the in-process experience for developers who:
- Prefer an integrated debugging window over a separate application.
- Want to maintain legacy codebases that depend on the `AttachDevTools()` extension methods.
- Require lightweight, in-process inspection during development.

## Installation

```bash
dotnet add package InProcess.DevTools
```

## Usage & Samples

### 1. Basic Attachment (Window)
Attach to a specific window. By default, it opens when you press **F12**.

```csharp
using Avalonia;
using Avalonia.Controls;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools(); 
#endif
    }
}
```

### 2. Global Attachment (Application)
Attach to the entire application. This is often the preferred way as it works across all windows.

```csharp
public partial class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        // ... usual initialization ...
        base.OnFrameworkInitializationCompleted();

#if DEBUG
        this.AttachDevTools();
#endif
    }
}
```

### 3. Custom Hotkey
Change the key gesture used to trigger the DevTools window.

```csharp
using Avalonia.Input;

// Opens with Ctrl+F11
this.AttachDevTools(new KeyGesture(Key.F11, KeyModifiers.Control));
```

### 4. Advanced Options
Configure startup behavior, monitor selection, and UI features.

```csharp
using InProcess.DevTools;

this.AttachDevTools(new DevToolsOptions()
{
    StartupScreenIndex = 0,
    ShowAsChildWindow = true,
    Size = new Avalonia.Size(1024, 768)
});
```

### 5. Optional MCP Server
InProcess.DevTools can expose a localhost MCP server for AI coding agents. It is disabled by default and must be explicitly enabled when attaching DevTools.

> Warning: only enable the MCP server in development environments. Depending on the capability flags below, it can expose live UI structure to local MCP clients, capture screenshots, raise supported UI events, navigate the app, and mutate writable public control properties.

```csharp
using InProcess.DevTools;

#if DEBUG
this.AttachDevTools(new DevToolsOptions()
{
    EnableMcpServer = true,
    McpServer = new McpServerOptions()
    {
        Host = "127.0.0.1",
        Port = 43210,
        Path = "/mcp",

        // Enabled by default when the MCP server is enabled.
        EnableDomInspection = true,

        // Disabled by default. Enable only when the agent needs them.
        EnableScreenshots = true,
        EnableNavigation = true,
        EnableEvents = true,
        EnableStateMutation = true,

        // Agent-driving capabilities, all off by default (see the table below).
        EnableEditing = true,
        EnableInput = true,
        EnableSelection = true,
        EnableMutationEvents = true,
        EnableValueInspection = true,
        EnableWaiting = true,
        EnableCommands = true,
        EnableFileOutput = true
    }
});
#endif
```

MCP capabilities are controlled by `McpServerOptions`:

- `EnableDomInspection`: exposes `devtools_list_roots`, `devtools_get_dom`, `devtools_get_tree` and `devtools_find`. Default: `true`.
- `EnableScreenshots`: exposes `devtools_capture_screenshot`. Default: `false`.
- `EnableNavigation`: exposes `devtools_focus`, `devtools_click` and `devtools_double_click`. Default: `false`.
- `EnableEvents`: exposes `devtools_raise_event`. Default: `false`.
- `EnableStateMutation`: exposes `devtools_set_property`. Default: `false`.
- `EnableMutationEvents`: lets `devtools_set_property` take `raise_events: true`. Needs `EnableStateMutation`. Default: `false`.
- `EnableEditing`: exposes `devtools_begin_edit`, `devtools_commit_edit`, `devtools_cancel_edit`. Default: `false`.
- `EnableInput`: exposes `devtools_send_keys` and `devtools_type_text`. Default: `false`.
- `EnableSelection`: exposes `devtools_select_item`. Default: `false`.
- `EnableValueInspection`: exposes `devtools_get_property`, `devtools_get_items`, `devtools_get_datacontext`. Default: `false`.
- `EnableWaiting`: exposes `devtools_wait_idle` and the `wait_idle` option of action tools. Default: `false`.
- `EnableCommands`: exposes `devtools_invoke_command`. Default: `false`.
- `EnableFileOutput`: allows `output: "file"` (temp-file output for screenshots and JSON dumps). Default: `false`.
- `AllowInReleaseBuilds`: the capabilities from `EnableMutationEvents` down, plus `devtools_double_click`, are **silently disabled when the host application is an optimized (Release) build**, even if their flag is set, unless this is `true`. Default: `false`.

Disabled capabilities are not advertised in `tools/list`, and direct calls to disabled tools are rejected.

#### Addressing nodes

A node is identified by `(tree, rootIndex, path)`:

- `tree` is `visual` (default) or `logical`. The two trees number their children differently, so reuse the tree that produced a path.
- `rootIndex` is the top-level index from `devtools_list_roots`. It is **never** part of `path`.
- `path` is a slash-separated list of zero-based child indexes below that root. `""` is the root, `"0"` its first child, `"0/2/1"` child 1 of child 2 of child 0.

Take paths from `devtools_get_dom`, `devtools_get_tree` or `devtools_find`. A wrong path is reported as a tool result with `isError: true` and a message such as `path '0/0/99/3' not found ...: closest valid prefix: '0/0'`.

#### Tools

Always available:

- `devtools_get_status`: endpoint and attached root summary.

Inspection (`EnableDomInspection`):

- `devtools_list_roots`: attached top-level roots.
- `devtools_get_dom`: DOM-like snapshot with `rootIndex`/`path`, bounds, text, classes, state and `childCount`. Extra arguments: `path` (start node; `maxDepth` counts from it, up to 64, so deep panels get their own budget), `filter` (`types[]`, `name`, `textContains`, `isVisible`, `interactiveOnly`; the result keeps matches plus their ancestors), `maxNodes` (default 5000) and `output`. Results carry `truncated` (node budget hit) and `depthLimited` (children cut by `maxDepth`, flagged per node with `childrenTruncated`).
- `devtools_get_tree`: compact snapshot, same `path`/`filter`/`maxNodes`/`output` options.
- `devtools_find`: `query` (`text`, `#Name`, `type:Button`, `text:Save`) and/or `filter`; returns matching `rootIndex`/`path`.

Screenshots (`EnableScreenshots`):

- `devtools_capture_screenshot`: PNG of a control. `scale`, `region {x,y,width,height}`, `dpi`, and `output: "file"` which writes to a temp file and returns `{path, bytes, width, height}` instead of ~200k characters of inline base64.

Navigation and events:

- `devtools_focus`, `devtools_click` (Button, ToggleButton/CheckBox, MenuItem, TabItem, list/combo items, DataGridRow/Cell), `devtools_double_click` (also starts editing an editable DataGrid cell), `devtools_raise_event`.
- `devtools_set_property`: sets a writable CLR property. With `raise_events: true` the notifications user input would raise (`SelectionChanged`, `TextChanged`, `IsCheckedChanged`) are guaranteed and listed in `eventsRaised`/`eventsSynthesized`.
- `devtools_select_item`: user-like selection by `index`, `value` or `text` in ComboBox, ListBox, TabControl and DataGrid.
- `devtools_invoke_command`: runs the `ICommand` in `commandProperty` (default `Command`) with its parameter, honouring `CanExecute`.

DataGrid editing (`EnableEditing`): `devtools_begin_edit(path, row, column)` creates the cell's editing control and returns its `editingPath`, which `get_dom`/`find`/`select_item`/`type_text` accept immediately; then `devtools_commit_edit` or `devtools_cancel_edit`.

Keyboard (`EnableInput`):

- `devtools_send_keys(path | focused, keys)`: text plus `{Enter}`, `{Tab}`, `{Escape}`, `{F2}`, arrows and chords such as `{Ctrl+A}`, `{Shift+Tab}`; `{{` and `}}` are literal braces.
- `devtools_type_text(path, text, clear?)`: real `TextInput` events, so bindings and validation fire.

Reads (`EnableValueInspection`):

- `devtools_get_property(path, propertyName)`: dotted names allowed (`SelectedItem.Name`); `Count` and `$type` segments; aliases `DataContextType`, `ItemsSourceCount`.
- `devtools_get_items(path)`: realised items of an ItemsControl, ComboBox, TabControl or DataGrid, with display text per column.
- `devtools_get_datacontext(path, properties[])`: named view-model properties; without `properties` it lists the readable names.

Timing (`EnableWaiting`): `devtools_wait_idle(timeoutMs, settleMs, conditions[])` waits for the dispatcher to drain and for conditions (`exists`, `not_exists`, `visible`, `property_equals`). Any action tool accepts `wait_idle: true`.

#### Secrets

`TextBox` controls with a `PasswordChar`, controls (and their descendants) marked with `McpRedaction.IsSensitive="True"`, DataGrid columns or view-model properties whose name looks secret (password, secret, token, api key, ...) and view-model values equal to a password box's text are always returned as `***REDACTED***`, in DOM text, `get_property`, `get_items`, `get_datacontext`, `find`, `wait_idle` and `set_property` echoes. Screenshots show what is on screen, so mark nothing sensitive in a captured area that you do not want to share.

```xml
<TextBox xmlns:dt="using:InProcess.DevTools" dt:McpRedaction.IsSensitive="True" />
```

None of the tools evaluate scripts or call arbitrary methods: reads only evaluate public property getters, and `devtools_invoke_command` only runs commands that the UI already exposes.

#### Configure Codex
Start your Avalonia application with `EnableMcpServer = true`, then add the HTTP MCP server to `~/.codex/config.toml`:

```toml
[mcp_servers.inprocess-devtools]
url = "http://127.0.0.1:43210/mcp"
enabled = true
required = false
```

You can also add it from the Codex CLI:

```bash
codex mcp add inprocess-devtools --url http://127.0.0.1:43210/mcp
```

OpenAI documents Codex MCP setup with `codex mcp add --url` and direct `~/.codex/config.toml` entries under `[mcp_servers]`: https://platform.openai.com/docs/docs-mcp

#### Configure Claude Code
Start your Avalonia application with `EnableMcpServer = true`, then register the HTTP MCP server:

```bash
claude mcp add --transport http inprocess-devtools http://127.0.0.1:43210/mcp
```

Or add it via JSON configuration:

```json
{
  "mcpServers": {
    "inprocess-devtools": {
      "type": "http",
      "url": "http://127.0.0.1:43210/mcp"
    }
  }
}
```

Claude Code also accepts `streamable-http` as an alias for `http` in JSON configuration: https://code.claude.com/docs/en/mcp

## Sample Project
A complete working example is included in this repository under `samples/InProcess.DevTools.Sample`.

To run the sample:
1. Clone this repository.
2. Open a terminal in the root folder.
3. Run the following command:
   ```bash
   dotnet run --project samples/InProcess.DevTools.Sample/InProcess.DevTools.Sample.csproj
   ```

## Features

- ✅ **Visual Tree Inspector:** Explore the logical and visual tree of your application.
- ✅ **Style Debugger:** Inspect applied styles and troubleshoot selectors.
- ✅ **Property Editor:** View and edit control properties in real-time.
- ✅ **Event Logger:** Monitor and filter routed events as they fire.
- ✅ **Layout Explorer:** Visualize control bounds, margins, and padding.
- ✅ **Screenshot Tool:** Capture snapshots of controls or windows.

- **Optional MCP Server:** Expose localhost DOM inspection, screenshots, navigation, event, and state manipulation tools for AI coding agents during development.

## Backward Compatibility

The public extension methods are compatible with the original `Avalonia.Diagnostics`. Most existing code using `this.AttachDevTools()` will work by simply replacing the NuGet package and updating namespaces where internal types were used.

## License

This project is licensed under the [MIT License](LICENSE).

---

*Looking for the official tools? Visit the [Avalonia Documentation](https://docs.avaloniaui.net/tools/developer-tools) for the standalone Developer Tools.*
