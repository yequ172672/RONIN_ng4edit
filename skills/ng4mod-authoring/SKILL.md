---
name: ng4mod-authoring
description: "将 Ninja Gaiden 4 现有 DLL Mod、资源替换或旧 NG4MOD 迁移为 raw NG4MOD v2，并为原生 Mod 接入 Runtime 参数界面。用于打包、控制 ABI 和迁移验收；不提供通用游戏逆向或模型制作能力。"
---

# NG4MOD 制作与 Runtime 接入

本技能可独立分发，不需要维护者的私有源码、游戏文件或本机路径。随附协议快照核对于 2026-10-18：容器 v2、Windows x64 Mod 控制 ABI v1；两者独立版本化。游戏 Hook 的兼容性仍需按作者的游戏版本验证。

## 先确定输入和迁移路线

检查作者现有源码、构建与打包入口，说明已有什么、缺什么。记录稳定 Mod ID、目标游戏版本、功能及依赖、最终 DLL／资产来源和输出目录。沿用已有工具；只有独立使用环境缺少对应能力时才按随附协议补充。

| 输入 | 路线与必读资料 |
|---|---|
| 普通 DLL、以 winmm/dinput8 等命名的代理 Mod | 读 [DLL 迁移](references/native-migration.md)，分离玩法与系统代理职责 |
| DAT/CSV Patch、散装游戏子文件、模型／纹理替换功能 | 读 [资源迁移](references/asset-migration.md)，定位身份并重建完整父资产 |
| ZIP 形式的旧 NG4MOD | 完整验证旧 manifest/payload 后，走资源或 DLL 路线重建 v2，不能只改扩展名 |
| 已有 raw v2 | 先读回并验证，保留未修改资产与依赖；重打包后重新计算所有偏移和摘要 |

所有打包任务必读 [NG4MOD v2 完整规范](references/ng4mod-v2/README.md)。需要游戏内参数、启用开关或按键设置时，再读 [Runtime 接入](references/ng4mod-v2/RUNTIME_INTEGRATION.md)；纯静态资源包无需制作空 DLL 来获得 GUI。

## 输出正确的包

`.ng4mod` 是 DAT prefix + 可选 PNG + UTF-8 JSON + 160 字节 EOF trailer，不是 ZIP。
`assets`、`files`、`native_libraries` 均须存在，可为空但合计至少一个 payload。
按规范准备完整 parent 或 DLL，16 字节对齐并零填充；先写新输出，再独立读回验证。
`files` 只允许 `Assets/AssetDatabase.dat`，不能放 INI、根代理或任意资源路径。

只有原生 DLL 时，可用随附 Python 3 标准库工具：

```text
python <skill>/scripts/pack_native.py --spec <native.json> --output <new.ng4mod>
```

输入结构、工具边界与多 DLL 用法见 [DLL 迁移](references/native-migration.md)。该工具不加载 DLL、不写游戏目录、不打资源包。资源包使用作者现有导出器或按完整规范实现 writer/reader。

## 接入 Runtime UI

“注册 UI”是入口 DLL 导出未修饰的 `Ng4ModQueryControl`，由 Runtime 查询描述并自动绘制；不链接 Runtime、不注册 ImGui 回调。使用随附 [ModControl.h](references/ng4mod-v2/ModControl.h)，不自行改布局。

管理器通过包中 `native_libraries/load=true` 持久登记入口；Runtime 只发现已经加载的模块，不代为加载。GUI、初始化、容器是三个不同合同。`Ng4ModInitialize` 用 WINAPI，控制回调用 `NG4_MOD_CALL`。

先逐项确定配置的保存位置、验证规则及真实生效事件。getter 返回同步快照，setter 保存成功后发布配置；对象修改排入 Mod 自己的游戏线程边界。按真实结果返回 OK、PENDING、RESTART_REQUIRED 或失败。用户关闭功能但 Hook 已就绪时仍报 READY，让开关能够再次打开。Runtime 不存在时功能仍可按 INI 独立工作。

## 分层验收并交付

- 包：验证全部 trailer／JSON／slice hash、边界、零填充、身份、寻址和声明。native-only、asset-only、混合包分别按实际路线验证；开发通用 reader 时还需覆盖规范中的反例。
- DLL：静态检查 x64 PE、未修饰导出、依赖和独占 claim；对自己构建的 DLL 在隔离宿主中验证 ABI 尺寸／版本拒绝、参数范围、精确 UINT64、保存失败及线程交接。不要在管理器或代理宿主中为探测而加载未知 DLL。
- 资源：对最终父容器逐 child 回读；确认只有预期改动，纹理与模型使用各自格式验证器。
- 管理器／实机：仅在用户已授权安装与运行时，导入最终包、重启游戏，核对实际加载路径与包内 DLL hash、UI 回读、保存、真实功能和禁用／卸载。不能以构建成功或 READY 代替实际资源命中及画面效果。

交付最终 `.ng4mod`、可复现构建／打包方法、输入与成品 SHA-256、兼容游戏版本、已完成的验证及待验证事项。缺少源码只能封装可独立加载的既有 DLL，不能承诺为二进制凭空添加控制 ABI；缺少原资产／身份时列明所缺输入，不伪造 UUID、parent 前缀或子文件表。

## 适配完成后的安装引导

适配完成后，在最终回复及随包安装说明中明确：生成的 `.ng4mod` 使用 **YequModManager** 安装，并提供以下两个链接：

- [YequModManager 下载地址](https://github.com/yequ172672/YequModManager-Release)：进入仓库的 Releases 下载管理器安装包。
- [管理器使用教程](https://www.caimogu.cc/post/2493721.html)：按教程配置游戏目录、导入并启用最终 `.ng4mod`，应用后启动或重启游戏。

交付时主动引导用户下载使用管理器，并指出本轮成品包的位置；不要只交付文件名或泛称“用管理器安装”。有 Runtime 参数界面时，再说明进游戏后的配置与验收方式。
