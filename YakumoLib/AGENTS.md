<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-07-14 | Updated: 2026-07-14 -->

# YakumoLib

## Purpose
Core library wrapping Platinum Engine game asset formats. Provides the asset database reader (used by Ninja Gaiden 4), asset entry management, DEFLATE decompression interop, and UUID handling. All format-specific parsing lives in the consuming projects (RONIN).

## Key Files
| File | Description |
|------|-------------|
| `YakumoLib.csproj` | .NET 8 class library project (allows unsafe blocks for interop) |
| `UUID.cs` | UUID implementation matching the game engine's asset identifier format |

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `Assets/` | Asset entry models, library container, extraction, and parser interfaces (see `Assets/AGENTS.md`) |
| `DEFLATE/` | C# P/Invoke interop for the native DeflateSharp decompression DLL (see `DEFLATE/AGENTS.md`) |
| `Database/` | AssetDatabase.dat reader — header, file entries, and CSV-based metadata tables (see `Database/AGENTS.md`) |
| `Modding/` | Modding infrastructure — patch file generation, backup management, modification tracking (see `Modding/AGENTS.md`) |

## For AI Agents

### Working In This Directory
- Library only — no executable entry point
- Unsafe blocks enabled for pointer-based interop with native DEFLATE code
- No external NuGet dependencies (pure .NET 8)

### Testing Requirements
- Use RONIN.Test to exercise library features independently of the WPF UI
- Verify AssetDatabase loading and asset entry enumeration

### Common Patterns
- Static factory methods for loading (`AssetLibrary.Load`)
- Internal `DeflateSharp` class for P/Invoke (not publicly accessible)
- UUID-based asset identification with dictionary lookups

## Dependencies

### Internal
- `DeflateSharp` — Native DLL (linked by project dependency)

### External
- .NET 8 SDK

<!-- MANUAL: -->