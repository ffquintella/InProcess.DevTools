[![NuGet](https://img.shields.io/nuget/v/Avalonia.Diagnostics.svg)](https://www.nuget.org/packages/Avalonia.Diagnostics/)

# Avalonia.Diagnostics (Legacy)

> **⚠️ This package is deprecated.** The legacy in-process DevTools have been replaced by the standalone [AvaloniaUI Developer Tools](https://docs.avaloniaui.net/tools/developer-tools/installation). Please migrate to the new tool — see the [migration guide](#migrating-to-developer-tools) below.

This repository is a read-only archive of the legacy `Avalonia.Diagnostics` package, previously part of the [Avalonia](https://github.com/AvaloniaUI/Avalonia) repository.
It provided an in-process DevTools window for inspecting the visual tree, styles, and properties of Avalonia applications.

## Migrating to Developer Tools

1. Remove the `Avalonia.Diagnostics` package:
   ```
   dotnet remove package Avalonia.Diagnostics
   ```

2. Install the new diagnostics support package:
   ```
   dotnet add package AvaloniaUI.DiagnosticsSupport
   ```

3. Install the standalone Developer Tools:
   ```
   dotnet tool install --global AvaloniaUI.DeveloperTools
   ```

4. Update your `Application` class:
   ```csharp
   public override void Initialize()
   {
       AvaloniaXamlLoader.Load(this);
   #if DEBUG
       this.AttachDeveloperTools();
   #endif
   }
   ```

5. Run your app and press **F12** to launch Developer Tools.

The standalone Developer Tools includes a free **Community edition** that covers all features available in the legacy `Avalonia.Diagnostics` package — no license required.

For full details, see the [Developer Tools documentation](https://docs.avaloniaui.net/tools/developer-tools/installation).

## License

This project is licensed under the [MIT License](LICENSE).
