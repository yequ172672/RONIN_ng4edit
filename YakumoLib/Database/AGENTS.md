<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-07-14 | Updated: 2026-07-14 -->

# Database

## Purpose
Reads and parses the game's binary asset database format (`AssetDatabase.dat`). Handles the binary header, file entry table, string table, and CSV-based metadata files that describe asset properties.

## Key Files
| File | Description |
|------|-------------|
| `AssetDatabaseReader.cs` | Binary reader for AssetDatabase.dat — parses magic header, group info, language IDs, file entries, and string table |
| `AssetDatabaseHeader.cs` | Struct representing the file header (magic, version, counts of groups/languages/files) |
| `AssetDatabaseFileEntry.cs` | Struct for each file entry in the database (UUID, path offset, type, size, compression info) |
| `CsvReader.cs` | Reads CSV metadata files that supplement the binary database with additional asset attributes |
| `CsvAssetLine.cs` | Struct representing a single line/row in a CSV metadata file |

## Subdirectories
*(None)*

## For AI Agents

### Working In This Directory
- `AssetDatabaseReader.Read()` is the entry point — reads a binary file or byte span, returns `IReadOnlyList<AssetEntry>`
- Magic number: `1111770444` (0x42444442 = "BDDB")
- String table is read after file entries — offsets index into a flat string data block
- `CsvReader.LoadAssetInfo()` merges CSV metadata into the asset dictionary, handling patch files (`@patch_` prefix)

### Testing Requirements
- Test with a real `AssetDatabase.dat` file to verify header parsing, file entry count, and string table
- Test CSV loading with sample `.csv` files
- Verify patch file precedence (later patches override earlier)

### Common Patterns
- `MemoryMarshal.Read<T>()` for direct struct deserialization from byte spans
- Fixed-size structs with `[StructLayout(LayoutKind.Sequential)]` for binary interop
- ConcurrentDictionary for parallel CSV file processing

## Dependencies

### Internal
- `YakumoLib/Assets/AssetEntry` — Asset entry model populated by the database reader
- `YakumoLib/UUID.cs` — UUID type used in file entries

### External
- .NET 8 — System.Buffers.Binary, System.Runtime.InteropServices, System.IO.Enumeration

<!-- MANUAL: -->