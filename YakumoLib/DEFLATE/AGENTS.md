<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-07-14 | Updated: 2026-07-14 -->

# DEFLATE

## Purpose
C# P/Invoke interop layer for the native DeflateSharp DLL. Provides managed-code access to Microsoft GDeflate decompression for extracting compressed game assets.

## Key Files
| File | Description |
|------|-------------|
| `DeflateSharp.cs` | Internal static partial class with `[LibraryImport]` P/Invoke declaration for `GDeflate_Decompress` |

## Subdirectories
*(None)*

## For AI Agents

### Working In This Directory
- Uses `[LibraryImport]` (source-generated, not `[DllImport]`) for native interop
- `[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]` specifies cdecl calling convention
- The class is `internal` — not exposed to consumers of YakumoLib

### Testing Requirements
- Requires compiled `DeflateSharp.dll` to be available at runtime
- Test with a known compressed buffer to verify round-trip decompression

### Common Patterns
- Source-generated P/Invoke (`[LibraryImport]` + `partial` method)
- `IntPtr` + `nuint` for native pointer parameters

## Dependencies

### Internal
- `DeflateSharp` (native C++ project) — The actual DLL being called

### External
- .NET 8 — System.Runtime.InteropServices

<!-- MANUAL: -->