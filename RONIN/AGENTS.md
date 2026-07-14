<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-07-14 | Updated: 2026-07-14 -->

# RONIN

## Purpose
The main WPF GUI application for browsing and previewing Ninja Gaiden 4 game assets. Provides file-browser navigation, asset selection, and real-time previews for textures, 3D models, fonts, and asset tables. Follows MVVM architecture with CommunityToolkit.Mvvm.

## Key Files
| File | Description |
|------|-------------|
| `App.xaml` / `App.xaml.cs` | Application entry point and XAML resources |
| `MainWindow.xaml` | Main window layout with asset browser and preview panel |
| `MainWindow.xaml.cs` | Main window code-behind |
| `MainWindowViewModel.cs` | Main ViewModel — asset loading, navigation, preview orchestration, modding commands |
| `MainWindowModding.cs` | Partial class extending MainWindow — wires modding menu items programmatically, handles file dialogs for export/replace/patch |
| `ModdingDialog.xaml` / `ModdingDialog.xaml.cs` | Patch generator settings dialog — game directory path, compress toggle, generate patch |
| `AssemblyInfo.cs` | Assembly metadata attributes |
| `RONIN.csproj` | .NET 8 WPF project file with HelixToolkit and MVVM dependencies |
| `DeflateSharp.dll` | Prebuilt native DEFLATE DLL (convenience copy) |

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `Browser/` | Asset tree-view navigation (FolderNode tree building and ViewModel wrappers) (see `Browser/AGENTS.md`) |
| `Formats/` | Asset format parsers (asset tables, bitmaps, DDS textures, MDL models) (see `Formats/AGENTS.md`) |
| `Preview/` | Preview providers for each asset type — textures, models, fonts, animation textures (see `Preview/AGENTS.md`) |

## For AI Agents

### Working In This Directory
- WPF with nullable enabled and implicit usings
- ViewModel pattern: `MainWindowViewModel` is the primary ViewModel with `[ObservableProperty]` and `[RelayCommand]` attributes
- Asset loading runs on background threads via `Task.Run` to keep UI responsive
- Preview providers are registered in the constructor via `AssetPreviewRegistry`

### Testing Requirements
- Run the application and verify asset loading from an `AssetDatabase.dat` file
- Test each preview type (texture, model, font, asset table)

### Common Patterns
- `AssetPreviewRegistry.Register(I)` + `LoadPreviewAsync` for extensible preview pipeline
- `FolderNode.BuildTree()` for hierarchical asset tree construction
- Async commands via `[RelayCommand]` on async Task methods
- DDS texture loading via `Pfim` library, 3D rendering via `HelixToolkit.Wpf.SharpDX`

## Dependencies

### Internal
- `YakumoLib` — Core asset library and database reader
- `Browser/` — Asset tree building
- `Formats/` — Specialized format parsers
- `Preview/` — Asset preview providers

### External
- CommunityToolkit.Mvvm 8.4.2 — ObservableObject, RelayCommand, ObservableProperty
- HelixToolkit.Wpf.SharpDX 3.1.2 — WPF 3D rendering for model preview
- Pfim 0.11.4 — DDS/BCn texture decoding
- .NET 8 SDK — Build and runtime

<!-- MANUAL: -->