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

## Backward Compatibility

The public extension methods are compatible with the original `Avalonia.Diagnostics`. Most existing code using `this.AttachDevTools()` will work by simply replacing the NuGet package and updating namespaces where internal types were used.

## License

This project is licensed under the [MIT License](LICENSE).

---

*Looking for the official tools? Visit the [Avalonia Documentation](https://docs.avaloniaui.net/tools/developer-tools) for the standalone Developer Tools.*
