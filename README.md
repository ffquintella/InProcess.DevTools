# InProcess.DevTools

[![NuGet](https://img.shields.io/nuget/v/InProcess.DevTools.svg)](https://www.nuget.org/packages/InProcess.DevTools/)

> **Fork of Avalonia.Diagnostics** — This package brings the legacy in-process DevTools to **Avalonia 12+** while maintaining backward compatibility with code written for the original `Avalonia.Diagnostics` package.

## About This Fork

The original `Avalonia.Diagnostics` package was deprecated and removed from recent Avalonia versions. This fork revives it for developers who:
- Need in-process DevTools for debugging Avalonia applications
- Want to maintain legacy code that depends on `Avalonia.Diagnostics`
- Prefer the lightweight in-process DevTools over the standalone Developer Tools

This package provides an in-process DevTools window for inspecting the visual tree, styles, properties, and events of Avalonia applications directly within your running app.

## Installation

```bash
dotnet add package InProcess.DevTools
```

## Quick Start

### Attach DevTools to a Window

```csharp
using Avalonia;
using Avalonia.Controls;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

#if DEBUG
        this.AttachDevTools();  // Opens with F12 key
#endif
    }
}
```

### Attach to Application

```csharp
public partial class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();

#if DEBUG
        this.AttachDevTools();
#endif
    }
}
```

### Custom Hotkey

```csharp
using Avalonia.Input;

this.AttachDevTools(new KeyGesture(Key.F11, KeyModifiers.Control));  // Ctrl+F11
```

### Custom Options

```csharp
this.AttachDevTools(new Avalonia.Diagnostics.DevToolsOptions()
{
    StartupScreenIndex = 1,  // Start on secondary monitor
    ShowFpsCounter = true
});
```

## Backward Compatibility

The public API is **100% compatible** with the original `Avalonia.Diagnostics`:
- All extension methods (`AttachDevTools`) work identically
- All public types are accessible via the `Avalonia.Diagnostics` namespace
- Existing code will compile without changes

Internally, implementation types use the `InProcess.DevTools` namespace, but this is transparent to consumers.

## Features

- ✅ Visual Tree Inspector
- ✅ Style Debugger
- ✅ Property Inspector with live editing
- ✅ Event Logger with filtering
- ✅ Layout Explorer
- ✅ Keyboard Shortcuts Configuration
- ✅ Screenshot capture

## Differences from Original

- **Avalonia 12+ support** — Updated to work with current Avalonia versions
- **Package name** — Published as `InProcess.DevTools` (but internal namespace preserves `Avalonia.Diagnostics` for compatibility)
- **Bug fixes** — Minor compatibility improvements for modern Avalonia

## License

This project is licensed under the [MIT License](LICENSE).

## See Also

- [Original Avalonia Repository](https://github.com/AvaloniaUI/Avalonia)
- [Standalone AvaloniaUI Developer Tools](https://docs.avaloniaui.net/tools/developer-tools/installation)

