<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-07-14 | Updated: 2026-07-14 -->

# Modding

## Purpose
Infrastructure for the modding pipeline — tracks asset modifications, generates @patch_*.csv/.dat files, and manages file backups before destructive operations.

## Key Files
| File | Description |
|------|-------------|
| `BackupManager.cs` | Timestamped backup of game files before any mod operation — skips duplicates, supports individual and recursive restore |
| `ModifiedAssetEntry.cs` | Record tracking a single modified asset — stores original/modified bytes, parent asset reference, compress flag |
| `PatchGenerator.cs` | Generates `@patch_<N>.csv` and `@patch_<N>.dat` files — auto-increments patch index, writes modified asset data, supports GDeflate compression |

## For AI Agents

### Working In This Directory
- All modding operations route through BackupManager before modifying game files
- PatchGenerator emits files directly into the game Assets directory
- ModifiedAssetEntry records are collected by MainWindowViewModel during a session and flushed on GeneratePatch

### Common Patterns
- Static utility classes (PatchGenerator)
- Sealed record types for immutable data transfer (ModifiedAssetEntry)
- `using YakumoLib.DEFLATE` for compress/decompress interop

## Dependencies

### Internal
- `YakumoLib/DEFLATE/DeflateSharp.cs` — GDeflate compress/decompress P/Invoke
- `YakumoLib/Assets/AssetEntry.cs` — Asset entry model

### External
- (none)
