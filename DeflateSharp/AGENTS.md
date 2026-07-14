<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-07-14 | Updated: 2026-07-14 -->

# DeflateSharp

## Purpose
Native C++/CLI DLL wrapper exposing Microsoft's GDeflate decompression algorithm for use from C#. Acts as a bridge between managed .NET code and the native DirectStorage GDeflate library.

## Key Files
| File | Description |
|------|-------------|
| `DeflateSharp.cpp` | Main implementation — exports `GDeflate_Decompress` as a C-compatible function |
| `DeflateSharp.vcxproj` | Visual Studio 2022 project file targeting v143 toolset, C++20 |
| `DeflateSharp.vcxproj.filters` | Visual Studio filter organization for source/header files |
| `dllmain.cpp` | DLL entry point (standard DllMain) |
| `framework.h` | Standard Windows SDK includes |
| `pch.h` | Precompiled header file |
| `pch.cpp` | Precompiled header source |

## Subdirectories
*(None)*

## For AI Agents

### Working In This Directory
- Requires Visual Studio 2022 with C++ desktop workload and the DirectStorage SDK
- Builds as a Windows DLL (DynamicLibrary configuration)
- Links against `deflate.lib` and `GDeflate.lib` from DirectStorage SDK
- Precompiled headers are enabled — modify `pch.h` for new includes

### Testing Requirements
- Must be compiled and the resulting `DeflateSharp.dll` placed where YakumoLib can load it
- A precompiled binary is also checked into `RONIN/DeflateSharp.dll` for convenience

### Common Patterns
- Exports use `extern "C"` + `_declspec(dllexport)` for C# P/Invoke interop
- Single exported function: `GDeflate_Decompress(compressedData, compressedSize, outputBuffer, outputSize, numWorkers)`

## Dependencies

### Internal
- YakumoLib/DEFLATE/DeflateSharp.cs — P/Invoke wrapper that calls this DLL

### External
- DirectStorage SDK (GDeflate) — native decompression library
- Windows SDK 10.0 — Windows platform headers
- Visual Studio 2022 v143 toolchain — C++20 compiler

<!-- MANUAL: -->