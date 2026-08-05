# Third-party notices

RONIN 使用以下直接 NuGet 依赖。版本固定在 `YakumoLib/YakumoLib.csproj`，恢复时请遵循各项目随包提供的完整许可证文本。

| Dependency | Version | License | Source |
| --- | --- | --- | --- |
| BCnEncoder.Net | 2.3.0 | MIT OR Unlicense | [Nominom/BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET) |
| SharpGLTF.Core | 1.0.6 | MIT | [vpenades/SharpGLTF](https://github.com/vpenades/SharpGLTF) |
| SharpGLTF.Toolkit | 1.0.6 | MIT | [vpenades/SharpGLTF](https://github.com/vpenades/SharpGLTF) |
| SixLabors.ImageSharp | 3.1.12 | Six Labors Split License 1.0 | [SixLabors/ImageSharp](https://github.com/SixLabors/ImageSharp) |

`SixLabors.ImageSharp` 的许可证文本位于 NuGet 包内的 `LICENSE` 文件；本项目以源代码形式发布，使用时应同时遵守该许可证的适用条件。

`DeflateSharp` 是原始 RONIN 主线已有的本地 GDeflate 解压边界，源代码位于 `DeflateSharp/`，托管声明位于 `YakumoLib/DEFLATE/DeflateSharp.cs`。本仓库不额外打包其外部 GDeflate/libdeflate 依赖；从源码重建原生 DLL 时，应按上游项目许可证取得对应开发库。
