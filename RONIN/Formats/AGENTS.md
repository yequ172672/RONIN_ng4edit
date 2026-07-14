<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-07-14 | Updated: 2026-07-14 -->

# Formats

## Purpose
Contains format-specific parsers and converters for game asset binary data. Handles asset table entries, DDS texture decompression, bitmap conversion, and MDL 3D model parsing.

## Key Files
| File | Description |
|------|-------------|
| `AssetTableParser.cs` | Parses asset table entries from binary data — reads table names, UUID references, and offset fields |
| `BitmapHelper.cs` | Converts raw RGBA byte arrays to WPF `BitmapSource` with red-blue channel swapping |
| `DDSTexture.cs` | DDS texture loader using the Pfim library — handles BCn-compressed textures and converts to RGBA |
| `MDLParser.cs` | MDL model parser — extracts vertex buffers (positions, UVs, normals, tangents) and index buffers for HelixToolkit rendering |
| `MDLParserExtended.cs` | Extended MDL parser producing `MDLFullData`/`MDLBatchData` with full header preservation, bone data, and vertex property extraction |
| `MDLWriter.cs` | Serializes `MDLFullData` back to MDL binary format — preserves header, recalculates offsets, writes vertex/index/batch tables |
| `MdlToFbxConverter.cs` | Converts parsed MDL model data to ASCII FBX format (FBX 2020) with mesh geometry, normals, UVs |
| `FbxToMdlConverter.cs` | Parses ASCII FBX text and produces MDL binary data — handles triangulation, UV mapping, index remapping |

## Subdirectories
*(None)*

## For AI Agents

### Working In This Directory
- Parsers operate on raw `BinaryReader` or `byte[]` streams from asset extraction
- `DDSTexture.Load()` uses Pfim for DDS decompression and returns RGBA byte arrays
- `BitmapHelper.CreateFromData()` swaps R/B channels (BGRA32 pixel format) for WPF rendering
- `MDLParser` produces `MDLHelixBuffers` structs consumable by HelixToolkit's mesh geometry

### Testing Requirements
- Use RONIN.Test to load sample assets and verify parser output
- Test edge cases: different DDS formats, empty asset tables, MDL with no UVs

### Common Patterns
- Static utility classes with `BinaryReader`-based parsing
- Structs for parsed data (e.g., `AssetTableEntry`, `MDLHelixBuffers`, `MDLBatch`)

## Dependencies

### Internal
- `YakumoLib/UUID.cs` — UUID type used in asset table entries
- `YakumoLib/Assets/AssetEntry` — Asset entry type for format identification

### External
- `Pfim` — DDS/BCn texture decoding
- `System.Numerics` — Vector3/Vector2/Vector4 for model data
- Windows Presentation Foundation — `BitmapSource`, `PixelFormats`

<!-- MANUAL: -->