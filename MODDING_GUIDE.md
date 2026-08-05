# NG4 Mod 制作指南

本指南覆盖 RONIN 原型中的基础模型、纹理和 `.ng4mod` v2 制作流程。

## 准备

1. 准备 Ninja Gaiden 4 的 `Assets` 目录，目录中应包含 `AssetDatabase.dat`、CSV 和 DAT 文件。
2. 关闭游戏后再读取资产；RONIN 只读取源资产，不会改写游戏目录。
3. 准备一个能保持骨架、网格身份和 UV 的 GLB 编辑器。RONIN 负责 MDL 与 GLB 的转换，编辑器只负责修改 GLB。

## 制作模型 Mod

1. 启动 RONIN，选择 **File → Load AssetDatabase**，打开 `Assets/AssetDatabase.dat`。
2. 在资产树或列表中选择一个 `SkeletalMesh` 包，确认右侧子文件中存在 `modeldata.mdl`。
3. 选择 **Modding Tools → NG4 Mod → Export selected model workspace (GLB + TGA)**，指定工作区根目录。
4. RONIN 会按资产逻辑路径生成：

   ```text
   <workspace-root>/<asset-logical-path>/<asset-name>.glb
   <workspace-root>/<asset-logical-path>/textures/*.tga
   <workspace-root>/<asset-logical-path>/textures/ronin-texture-set.json
   ```

5. 在 GLB 编辑器中修改网格、UV、法线、顶点色或权重。保留骨架节点、批次身份和工作区生成的结构信息。

## 制作纹理 Mod

- **Export selected texture set (PNG)** 生成 schema 2 PNG 工作区。
- **Export selected texture set (TGA)** 生成 schema 3 TGA 工作区，也是模型 workspace 的默认格式。
- `ronin-texture-set.json` 是源纹理 UUID、逻辑路径、尺寸、格式、mip 数量和 SHA-256 的清单，不要删除或手动改写这些字段。
- 只会把内容发生变化的纹理放入最终包；导入时会按源纹理的格式和 mip 数量重建 DDS 数据。
- 纹理候选来自模型邻近的 `Texture` 路径以及已有材质实例引用的纹理；RONIN 不创建新的材质或 UUID。

## 导出 `.ng4mod`

1. 完成 GLB 和纹理编辑后，选择 **Modding Tools → NG4 Mod → Package edited workspace as NG4MOD v2**。
2. 选择工作区中的 `.glb`。该 GLB 旁必须存在 `textures/ronin-texture-set.json`。
3. 填写 Mod ID、版本、名称、作者和说明，可选 PNG 封面。
4. 选择输出位置。RONIN 会：

   - 用原始 `modeldata.mdl` 作为模板写回 GLB；
   - 将替换模型同步到包内所有 MDL LOD；
   - 将 `LodParam.bin` 的子 LOD 阈值设为安全值；
   - 只加入发生变化的纹理父包；
   - 写出 `NG4MOD2` v2 数据、manifest 和 SHA-256；
   - 在落盘前重新读取并验证整个包。

输出包不会修改源 CSV、DAT 或 `AssetDatabase.dat`。生成后需要交给能够识别 `.ng4mod` v2 的 Mod 管理器安装和启用；RONIN 本身只负责制作与校验。

## 当前边界

- 当前 GUI 入口针对 `SkeletalMesh`；StaticMesh 没有默认制作入口。
- 模型必须保留原始骨架、批次和材质槽身份；不支持通过本流程创建新材质资产。
- `.ng4mod` 生成端使用未压缩游戏 payload，以便在包内进行确定性校验。
- 游戏内显示、安装顺序和不同版本游戏的兼容性仍需在目标环境中验证。

## 本地验证

在仓库根目录运行：

```powershell
dotnet build YakumoLib/YakumoLib.csproj -c Release
dotnet build RONIN/RONIN.csproj -c Release
dotnet build RONIN.Test/RONIN.Test.csproj -c Release
dotnet run --project RONIN.Test/RONIN.Test.csproj -c Release -- --ng4mod-package-tests --ng4mod-exporter-tests --ng4mod-workspace-tests --parent-payload-tests --ng4-lod-param-tests
dotnet run --project RONIN.Test/RONIN.Test.csproj -c Release -- --model-texture-set-tests
dotnet run --project RONIN.Test/RONIN.Test.csproj -c Release -- --mdl-gltf-tests
```

没有本机模型夹具时，最后一项会安全跳过真实模型检查；其余契约测试仍应通过。

要验证完整的真实资产流程，可先设置 `RONIN_NG4_ASSETS` 指向游戏的 `Assets` 目录，再运行 `--ng4mod-real-workflow-tests`。该测试只在临时目录生成并删除验证包。
