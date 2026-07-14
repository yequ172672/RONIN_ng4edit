<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-07-14 | Updated: 2026-07-14 -->

# Assets

## Purpose
Core asset data model layer. Defines asset entry types, the asset library container, extraction logic for decompressing asset blobs, and parser interfaces for format-specific processing.

## Key Files
| File | Description |
|------|-------------|
| `AssetEntry.cs` | Immutable record representing a single game asset — path, type, UUID, size, offset, archive source |
| `AssetType.cs` | Enum of all supported asset types (Texture, StaticMesh, SkeletalMesh, etc.) |
| `AssetLibrary.cs` | In-memory asset database — loads from disk, indexes by UUID and path, provides lookup methods |
| `AssetExtractor.cs` | Static utility for reading and decompressing asset binary data from archive files |
| `IAssetParser.cs` | Interface for implementing asset format parsers |
| `IAssetRegistry.cs` | Interface for asset type registry/lookup |
| `AssetParserResult.cs` | Result type for parser output |
| `SubAssetEntry.cs` | Represents a sub-asset within a parent container |

## Subdirectories
*(None)*

## For AI Agents

### Working In This Directory
- `AssetEntry` is a `record` type with `required` properties — uses init-only setters
- `AssetLibrary` loads by calling `AssetDatabaseReader` then `CsvReader` for metadata
- `AssetExtractor` handles DEFLATE decompression via `DeflateSharp` P/Invoke
- Asset types are identified by numeric IDs in the enum (`AssetType`)

### Testing Requirements
- Verify `AssetLibrary.Load()` with a test AssetDatabase.dat
- Test `AssetExtractor` decompression with known compressed blobs
- Test UUID lookup round-trip

### Common Patterns
- Dictionary-based indexing (`_byId` for UUID, `_byPath` for path)
- Static factory method (`AssetLibrary.Load`)
- `required` record properties for compile-time safety

## Dependencies

### Internal
- `YakumoLib/Database/` — AssetDatabase binary reader and CSV metadata reader
- `YakumoLib/DEFLATE/` — DEFLATE decompression interop
- `YakumoLib/UUID.cs` — UUID type for asset identification

### External
- .NET 8 — System.IO, System.Runtime.InteropServices

<!-- MANUAL: -->