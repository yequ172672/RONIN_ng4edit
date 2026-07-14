<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-07-14 | Updated: 2026-07-14 -->

# RONIN.Test

## Purpose
A simple CLI console application for testing asset parsing and decompression features without loading the WPF GUI. Useful for debugging format parsers and validating library output in isolation.

## Key Files
| File | Description |
|------|-------------|
| `Program.cs` | CLI entry point — manual test routines for library features |
| `RONIN.Test.csproj` | .NET 8 console project referencing YakumoLib |

## Subdirectories
*(None)*

## For AI Agents

### Working In This Directory
- Output type is `Exe` — produces a standalone console application
- References YakumoLib but not RONIN (no WPF dependency)
- Used for manual smoke testing, not automated unit tests

### Testing Requirements
- Run `dotnet run` in this directory to execute test routines
- Verify console output matches expected values

### Common Patterns
- Simple top-level statements in `Program.cs`

## Dependencies

### Internal
- `YakumoLib` — Asset library and format parsing

### External
- .NET 8 SDK

<!-- MANUAL: -->