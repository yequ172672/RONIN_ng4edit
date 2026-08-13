<img width="1260" height="706" alt="RONIN 模型预览器中显示的隼龙" src="https://github.com/user-attachments/assets/57d1bbe1-ddf4-4dbe-82f5-a2d29033b37a" />

# RONIN：NINJA GAIDEN 4 MOD 制作原型

本分支基于原始 [RONIN](https://github.com/Gaming-With-Portals/RONIN) 项目，保留其《NINJA GAIDEN 4》资源读取、解析和预览能力，并增加一套尽量精简的模型与纹理 MOD 制作流程。

## 主要功能

- 浏览《NINJA GAIDEN 4》的 `AssetDatabase.dat`、CSV 和 DAT 资源。
- 预览纹理、骨骼模型和原项目支持的其他资源类型。
- 将骨骼模型导出为 GLB 工作区，并同时导出对应的 PNG 或 TGA 纹理。
- 将编辑后的 GLB 转换回游戏 MDL，保留原模型骨架、批次、材质槽和资源身份。
- 支持常见的 24/32 位、未压缩或 RLE TGA；支持 PNG/TGA 纹理修改。
- 允许修改纹理分辨率，并自动重建 DDS 头、mip 链和纹理父包布局。
- 从工作区中的任意模型开始，收集同一工作区内所有发生变化的模型和纹理。
- 使用导出时记录的哈希过滤未修改内容，避免把没有变化的资源加入 MOD。
- 将变化内容封装为经过完整性校验的 `.ng4mod` v2 文件。
- 自动将模型替换应用到对应资源包内的全部 MDL LOD，并处理基础 LOD 参数。
- 兼容用于隐藏部件的极小占位网格在常见编辑器往返过程中产生的辅助骨骼绑定。

## 基本流程

1. 加载游戏 `Assets/AssetDatabase.dat`。
2. 选择一个 `SkeletalMesh` 资源。
3. 导出 GLB + PNG 或 GLB + TGA 模型工作区。
4. 使用支持 GLB 的三维软件修改模型，并使用图像编辑器修改纹理。
5. 在 RONIN 中选择工作区里的任意一个已编辑 GLB。
6. 填写 MOD 信息并导出 `.ng4mod`。

完整安装方法、操作步骤和注意事项请阅读：

**[RONIN《NINJA GAIDEN 4》MOD 制作教程](https://www.caimogu.cc/post/2457337.html)**

## 使用边界

- 当前制作入口主要面向 `SkeletalMesh`。
- 编辑模型时应保留原有骨架、批次身份和材质槽；本原型不负责创建新的游戏材质资产或 UUID。
- `.ng4mod` 只包含检测到的模型和纹理变化，不会直接修改游戏源资源。
- MOD 的安装、加载顺序以及不同游戏版本的兼容性需要由支持 `.ng4mod` v2 的 MOD 管理器和实际游戏环境验证。

## 项目结构

- `RONIN`：桌面界面、资源浏览与 MOD 工作流入口。
- `YakumoLib`：游戏资源格式、GLB/MDL 转换、纹理处理和 `.ng4mod` 封装逻辑。
- `RONIN.Test`：项目维护者使用的命令行验证程序。
- `DeflateSharp`：原始 RONIN 项目使用的 GDeflate 解压边界。

## 版权与许可证

本项目是在原始 RONIN 项目基础上进行的扩展开发。原项目及原作者的版权和归属声明保持不变。

本仓库继续遵循 [GNU General Public License v3.0](LICENSE)。再发布或修改本项目时，必须遵守该许可证的完整条款，并保留相应的版权、许可证及源码提供义务。

第三方组件仍分别受其自身许可证约束，详情参见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。《NINJA GAIDEN 4》及相关名称、角色和素材的权利归其各自权利人所有；本项目与游戏发行商或开发商不存在官方关联。
