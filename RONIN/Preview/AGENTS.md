<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-07-14 | Updated: 2026-07-14 -->

# Preview

## Purpose
Provides extensible preview providers for each supported asset type. The `AssetPreviewRegistry` acts as a provider lookup, and each concrete provider handles loading and rendering its specific asset type (textures, 3D models, fonts, animation textures, asset tables).

## Key Files
| File | Description |
|------|-------------|
| `IAssetPreviewProvider.cs` | Interface with `CanPreview(AssetEntry)` and `LoadPreviewAsync(AssetEntry)` |
| `AssetPreviewRegistry.cs` | Registry that collects providers and finds the matching one for an asset |
| `TexturePreviewProvider.cs` | Preview for standard textures — loads DDS via Pfim, converts to BitmapSource |
| `AtlasTexturePreviewProvider.cs` | Preview for atlas textures (texture arrays/atlases) |
| `AnimationPreviewProvider.cs` | Preview for animation texture sequences |
| `FontPreviewProvider.cs` | Preview for font atlas textures |
| `ModelPreviewProvider.cs` | Preview for skeletal/static meshes using HelixToolkit 3D rendering |
| `AssetTablePreviewProvider.cs` | Preview for asset table data in a readable format |

## Subdirectories
*(None)*

## For AI Agents

### Working In This Directory
- To add a new preview type: implement `IAssetPreviewProvider`, then register it in `MainWindowViewModel` constructor
- `CanPreview` should be fast (no I/O) — it's called synchronously to determine which provider to use
- `LoadPreviewAsync` runs on background threads and returns the WPF-compatible content object
- Preview content is assigned to `MainWindowViewModel.PreviewContent` (type `object?`)

### Testing Requirements
- Test each provider with real asset files
- Verify `CanPreview` returns correct results for matching/non-matching asset types
- Ensure `LoadPreviewAsync` handles corrupt or incomplete asset data gracefully

### Common Patterns
- Provider registration in `MainWindowViewModel` constructor
- `FirstOrDefault` lookup in `AssetPreviewRegistry.GetProvider()`
- Async loading with `Task<object?>` return type

## Dependencies

### Internal
- `YakumoLib/Assets/AssetEntry` — Asset entry used for type matching and data loading
- `RONIN/Formats/` — Format parsers (DDSTexture, MDLParser, BitmapHelper)
- `YakumoLib/Assets/AssetExtractor` — Raw blob extraction from archives

### External
- HelixToolkit.Wpf.SharpDX — 3D mesh rendering for model previews
- Pfim — DDS texture decoding
- CommunityToolkit.Mvvm — ObservableObject for ViewModel

<!-- MANUAL: -->