<!-- Generated: 2026-07-14 | Updated: 2026-07-14 -->

# RONIN

## Purpose
A GUI asset editor and previewer for Ninja Gaiden 4 game assets, supporting texture, model, font, and asset table previews. Built with WPF (.NET 8) following MVVM principles, with a native C++ DEFLATE compression wrapper.

## Key Files
| File | Description |
|------|-------------|
| `README.md` | Project overview, supported games and asset types |
| `RONIN.slnx` | .NET solution file linking all four projects |
| `LICENSE` | License information |

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `DeflateSharp/` | Native C++ DEFLATE compression wrapper for C# interop (see `DeflateSharp/AGENTS.md`) |
| `RONIN/` | WPF GUI application with parsers, previewers, and MVVM architecture (see `RONIN/AGENTS.md`) |
| `RONIN.Test/` | CLI test harness for exercising library features without GUI (see `RONIN.Test/AGENTS.md`) |
| `YakumoLib/` | Core library for Platinum Engine asset formats and database reading (see `YakumoLib/AGENTS.md`) |

## For AI Agents

### Working In This Directory
- The solution uses .NET 8 with WPF for the GUI layer and native interop via C++/CLI for DEFLATE
- RONIN depends on YakumoLib; YakumoLib depends on DeflateSharp for decompression
- Follow MVVM pattern in RONIN project

### Testing Requirements
- RONIN.Test is a CLI-based test project, not a unit test framework — run it to verify library output
- No automated test runner is configured; manual smoke testing via RONIN.Test

### Common Patterns
- C# projects use nullable enable, implicit usings, and target net8.0
- WPF project uses `CommunityToolkit.Mvvm` for ViewModel bindings
- 3D model rendering via `HelixToolkit.Wpf.SharpDX`

## Dependencies

### Internal
- DeflateSharp (native) → YakumoLib (C# interop) → RONIN (WPF GUI)

### External
- .NET 8 SDK - Build and runtime framework
- CommunityToolkit.Mvvm 8.4.2 - MVVM infrastructure
- HelixToolkit.Wpf.SharpDX 3.1.2 - 3D model rendering
- Pfim 0.11.4 - Image decoding
- Visual Studio 2022 - C++ native toolchain for DeflateSharp

<!-- MANUAL: -->