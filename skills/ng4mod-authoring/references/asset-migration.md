# 资源替换转换为 NG4MOD

本流程处理已编辑的游戏资源；FBX/PNG/WAV 等源素材仍需作者现有模型、纹理或音频转换器。容器封装不会自动把它们变成游戏格式。

## 找到资产身份与基线

从用户合法持有的 AssetDatabase、基础与 Patch CSV/DAT、现有包或导出工作区清单，建立每个替换项的 UUID、逻辑路径、asset_type、storage_path、来源归档和有效优先级。Patch 可覆盖基础数据；不能总取第一个同名文件。定位失败时请求相应清单／原始 parent，不按文件名猜 UUID。

NG4 UUID 使用四组八位十六进制，不能套普通 GUID 的 8-4-4-4-12 序列化。
现有 DAT/CSV Patch 必须读取每行 parent 与 child 表，不能按目录名或 DAT 字节搜索自行拼装身份。

## 重建完整父容器

1. 按真实格式解压原 parent 与子文件；v2 当前只存未压缩载荷，`game_compression=none`、`compressed_size=0`。
2. 保留完整 parent 前缀／头部、未知字段和未修改 child。真实游戏 parent 通常有非空前缀；把散装 `modeldata.mdl` 或 `dynajoint.bin` 放在 parent offset 0 不能替代重建。禁止把“拼接全部 child”当所有格式的通用 writer。
3. 用对应格式 writer 替换目标 child 并重建内部表、偏移、长度与必要对齐；原始前缀可能需要修正，不能在长度改变后机械原样拼接。
4. 保留 CSV 的 `csv_unknown` 与 `csv_metadata`，不透明字段不能凭经验重算。每个 child 的 `addressing` 来自真实 CSV，不按 asset_type 推断。原 global child 转为 parent 内相对偏移后，核对它确实位于最终 parent slice；不在原 parent 内时先通过该格式 writer 重建规范 parent，不能保留跨 slice 引用。
5. manifest 中 `sub_entries.offset` 始终相对当前 parent；只有输出游戏 CSV 时，global_offset 才是包内 parent 起点加该 offset，local_offset 则为 0。local child 的 global_offset=0、local_offset=相对偏移。
6. `intent.replaced_sub_entries` 只列真实替换 child，每项都须存在；`payload_path=payload/<asset_id>.bin` 是逻辑标识，不创建虚构 ZIP entry。

纹理不能只塞 DDS：核对 Image.img、Metadata.bin、global mip、DDS header 和 descriptor。模型需要同步所有实际改变的 LOD、骨表、materialmap 等；原始模板和工作区身份必须匹配。以游戏格式回读验证，不以 Blender／图片预览代替。

## 依赖与混合包

替换原有已注册资源时保留目标 UUID。引用新材质、纹理或其他资源时，检查目标引用与按语言划分的依赖闭包；不把“包内有文件”当游戏一定会加载它。

确需发布数据库时，`files` 只能声明完整 `Assets/AssetDatabase.dat`（kind=`asset_database`），管理器同一时间仅允许一个启用 Mod 拥有它。仅在具备经过验证的专用运行时依赖提供器时才改用 DLL 注入；本技能不附带通用依赖 Hook 或可盲用的 RVA。

资源加 DLL 使用同一个 v2 manifest，物理顺序 assets → files → native_libraries。不要为了封装 DLL 覆盖原包资源／files；读取来源包后显式保留所需项，重建全部偏移和摘要。

## 打包与复核

按 [完整规范](ng4mod-v2/README.md)生成 raw v2。已有 NG4 Tool 源码时，优先复用 `Ng4ModPackageExporter.Export/Repack/VerifyPackage` 和对应父容器服务；它们属于 NG4 Tool，不能假设历史 RONIN 的同名功能已经支持此版本。`Repack` 的 files/native 保留策略需显式检查，不能依赖默认值。

独立工具缺少这些库时，依据随附规范实现对应适配器：先完成 parent 重建，再按第 6.2 节写包、第 6.1 节独立读取验证。完整验收包含 manifest 必填字段、路径／身份唯一性、全部 slices、零填充、cover、checked Int64 以及哈希，不只检查 EOF magic。

从成品回读每个 parent／child，对未修改 child 比较解压后的原始字节，对修改 child 用格式 reader 核对实际值；重建 CSV/DAT 后再次解析寻址并比对 payload hash。游戏 DAT 只包含 payload prefix，不能包含 JSON/trailer。纯 DLL 包不生成伪造空 DAT/CSV。
