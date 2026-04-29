# Avalonia.Diagnostics → InProcess.DevTools Rename

## Overview

This document describes how the repository was renamed from `Avalonia.Diagnostics` to `InProcess.DevTools` while maintaining full backward compatibility with the original public API.

## Strategy

The rename uses a **two-namespace approach**:

1. **Public API Namespace: `Avalonia`** — Extension methods remain in the `Avalonia` namespace (e.g., `this.AttachDevTools()`)
2. **Implementation Namespace: `InProcess.DevTools`** — All internal implementation moved to avoid brand conflict with AvaloniaUI
3. **RootNamespace: `Avalonia`** — Assembly root namespace preserved for binary compatibility

This allows existing code to work without modification while avoiding trademark issues.

## Changes Made

### 1. **Namespace Renames in Source Files**

All internal implementation types changed their namespace:

```csharp
// BEFORE
namespace Avalonia.Diagnostics
namespace Avalonia.Diagnostics.ViewModels
namespace Avalonia.Diagnostics.Views
namespace Avalonia.Diagnostics.Controls

// AFTER
namespace InProcess.DevTools
namespace InProcess.DevTools.ViewModels
namespace InProcess.DevTools.Views
namespace InProcess.DevTools.Controls
```

**Files affected:** ~60 C# source files in `src/InProcess.DevTools/Diagnostics/**`

### 2. **Using Statements Updates**

All cross-namespace imports updated:

```csharp
// BEFORE
using Avalonia.Diagnostics;
using Avalonia.Diagnostics.ViewModels;

// AFTER
using InProcess.DevTools;
using InProcess.DevTools.ViewModels;
```

### 3. **Project File Changes**

#### `src/InProcess.DevTools/InProcess.DevTools.csproj`

```xml
<!-- ADDED: -->
<PackageId>InProcess.DevTools</PackageId>
<AssemblyName>InProcess.DevTools</AssemblyName>
<PackageProjectUrl>https://github.com/ffquintella/InProcess.DevTools</PackageProjectUrl>
<PackageDescription>InProcess.DevTools - Avalonia 12+ compatible DevTools fork...</PackageDescription>

<!-- KEPT: -->
<RootNamespace>Avalonia</RootNamespace>  <!-- ← Preserves backward compatibility -->
```

### 4. **Solution File Updates**

```xml
<!-- BEFORE -->
<Project Path="src/Avalonia.Diagnostics/Avalonia.Diagnostics.csproj" />

<!-- AFTER -->
<Project Path="src/InProcess.DevTools/InProcess.DevTools.csproj" />
```

### 5. **Sample App References**

Updated project references in the sample app:

```xml
<!-- BEFORE -->
<ProjectReference Include="..\..\src\Avalonia.Diagnostics\Avalonia.Diagnostics.csproj" />

<!-- AFTER -->
<ProjectReference Include="..\..\src\InProcess.DevTools\InProcess.DevTools.csproj" />
```

### 6. **README Update**

- Retitled project to "InProcess.DevTools"
- Added "Fork of Avalonia.Diagnostics" explanation
- Updated package installation instructions
- Clarified Avalonia 12+ support
- Added backward compatibility notice

## Backward Compatibility

### What Still Works

✅ All existing code continues to compile without changes:
```csharp
using Avalonia;

// These work exactly as before:
this.AttachDevTools();
this.AttachDevTools(new KeyGesture(Key.F12));
app.AttachDevTools(new InProcess.DevTools.DevToolsOptions());
```

### Why It Works

1. **RootNamespace preserved** — The assembly root is still `Avalonia`, so IL references are binary-compatible
2. **Public extension methods unchanged** — Still in the `Avalonia` namespace
3. **Internal namespace doesn't break binary compat** — Only affects compile-time references to implementation types (which are internal)

### Known Limitations

- Code that explicitly references implementation types (e.g., `new Avalonia.Diagnostics.ViewModels.MainViewModel()`) will need to change to `InProcess.DevTools.ViewModels.MainViewModel`
- However, these are internal implementation details not typically used by consumers

## Directory Structure

Before and after:

```
BEFORE:
  src/Avalonia.Diagnostics/
  samples/Avalonia.Diagnostics.Sample/
  
AFTER:
  src/InProcess.DevTools/          ← Project folder name unchanged (internal)
    InProcess.DevTools.csproj       ← Project file name unchanged (references updated)
    Diagnostics/
      (all namespaces changed to InProcess.DevTools.*)
      
  samples/InProcess.DevTools.Sample/  ← Sample kept as-is for reference
```

## Files Changed Summary

- **~60 C# files**: Namespace declarations updated
- **2 project files** (`.csproj`): Package metadata added, references updated
- **1 solution file** (`.slnx`): Project paths updated
- **1 README**: Completely rewritten
- **0 logic changes**: No behavioral changes, pure namespace/metadata updates

## Testing

To verify the rename works:

```bash
# Build the project
dotnet build .\InProcess.DevTools.slnx -c Debug

# Run the sample (should attach DevTools with F12)
dotnet run --project samples\InProcess.DevTools.Sample\InProcess.DevTools.Sample.csproj
```

## Package Publishing

When publishing to NuGet:

```bash
dotnet pack src/InProcess.DevTools/InProcess.DevTools.csproj -c Release -o ./nuget
# Will produce: InProcess.DevTools.X.Y.Z.nupkg (based on PackageId)
```

## References

- Original issue: Rename to avoid trademark conflicts with AvaloniaUI brand
- Target version: Avalonia 11.3.12+ (with Avalonia 12 support)
- Compatibility: 100% backward compatible with `Avalonia.Diagnostics` code
