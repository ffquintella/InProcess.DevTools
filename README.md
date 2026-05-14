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
        EnableStateMutation = true
    }
});
#endif
```

MCP capabilities are controlled by `McpServerOptions`:

- `EnableDomInspection`: exposes `devtools_list_roots`, `devtools_get_dom`, and `devtools_get_tree`. Default: `true`.
- `EnableScreenshots`: exposes `devtools_capture_screenshot`. Default: `false`.
- `EnableNavigation`: exposes `devtools_focus` and `devtools_click`. Default: `false`.
- `EnableEvents`: exposes `devtools_raise_event`. Default: `false`.
- `EnableStateMutation`: exposes `devtools_set_property`. Default: `false`.

Disabled capabilities are not advertised in `tools/list`, and direct calls to disabled tools are rejected.

Depending on those flags, the MCP endpoint exposes these tools:

- `devtools_get_status`: returns the endpoint and attached root summary.
- `devtools_list_roots`: lists attached Avalonia top-level roots.
- `devtools_get_dom`: returns a DOM-like visual or logical tree with stable `rootIndex` and `path` selectors, bounds, text, classes, visibility, enabled state, and common control state.
- `devtools_get_tree`: returns a compact visual or logical tree snapshot.
- `devtools_capture_screenshot`: captures a target control as PNG and returns base64 data.
- `devtools_focus`: moves keyboard focus to a target control.
- `devtools_click`: navigates the application by invoking supported click behavior on `Button`, `ToggleButton`, and `MenuItem`, or focusing a generic `Control`.
- `devtools_raise_event`: raises supported high-level events. Current values are `click` and `focus`.
- `devtools_set_property`: manipulates state by setting writable public CLR properties such as `Text`, `IsChecked`, `SelectedIndex`, or `Value`.

Use `devtools_get_dom` first to find the target `rootIndex` and `path`, then pass those values to screenshot, focus, click, event, or property tools. Paths are slash-delimited child indexes from the selected tree; an empty path targets the root.

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
