# NG4 模型替换 MOD 操作指南

## 游戏材质/纹理架构概览

```
模型包 (SkeletalMesh/StaticMesh)
├── modeldata.mdl       ← 网格几何体（顶点、UV、蒙皮权重、LOD）
├── LOD1.mdl / LOD2.mdl ← 细节层次网格
├── materialmap.bin     ← 材质映射表（batch → 材质资产 UUID）
├── baseSettings.bin    ← 基础属性
├── MeshAABB.bin        ← 包围盒
├── Shadow.bin          ← 阴影参数
├── ...其他 bin 文件

材质系统（独立资产）
├── MaterialInstance ← 材质实例（引用材质模板 + 贴图）
├── Texture          ← 贴图资源（DDS 格式，含 mipmap）
```

**关键概念：** 模型文件 (.mdl) 只包含**几何数据**——顶点位置、法线、UV、蒙皮权重。它不存材质或贴图。模型通过 `materialID`（batch 里的一个数字）引用外部材质资源。贴图完全由材质系统管理，独立于模型文件。

---

## 一、加载资源数据库

1. 打开 RONIN 程序
2. 菜单 **File → Load AssetDatabase**
3. 选择游戏目录下的 `Assets/AssetDatabase.dat`（或任意 `.csv` 文件）
4. 左侧树形面板出现资产浏览结构，中间显示该目录下的资产列表

### 加载后界面布局

```
┌──────────────┬──────────────────────────────┬──────────────┐
│  资产树      │  资产列表（中间）              │  属性面板    │
│  (左侧)      │  - 包名 / 类型 / 文件数       │  包属性      │
│              │  - 压缩大小 / UUID             │  子文件列表  │
│              │                              │              │
│              │  预览区（中间下方）             │              │
└──────────────┴──────────────────────────────┴──────────────┘
```

---

## 二、找到想替换的模型

在资产树中浏览，SkeletalMesh（骨架网格）和 StaticMesh（静态网格）是可替换的 3D 模型。

### 典型模型路径

```
Assets/Character/         ← 角色模型
Assets/Environment/       ← 场景模型
Assets/Weapon/            ← 武器
Assets/Props/             ← 道具
```

选中后在右侧属性面板可以看到子文件清单，其中 `modeldata.mdl` 是主要网格文件，`LOD1.mdl`/`LOD2.mdl` 是细节层次文件。

---

## 三、导出 FBX（在 3D 软件中编辑）

### 方法 A：通过 GUI 导出

1. 选中一个 SkeletalMesh 或 StaticMesh 资产
2. 菜单 **Modding Tools → Export Selected Package**（导出原始数据）
3. 或使用命令行提取后转换：

```
dotnet run --project RONIN.Test -- <游戏路径>
```

程序会自动提取模型、导出 FBX 到 `ExtractedModels/` 目录。

### 方法 B：手动导出

1. 找到目标资产（例如 `SK_gimmick_irondoor_a05`）
2. 提取 `LOD1.mdl` 子文件（通常未压缩，可以直接读取）
3. 运行转换：`MdlToFbxConverter.ConvertToFbz(mdlData)`
4. 输出为 ASCII FBX 格式，可在 Blender / 3ds Max / Maya 中打开

### 导出的 FBX 包含

| 数据 | 支持情况 |
|------|---------|
| 顶点位置 | ✅ float3 |
| 法线 | ✅ float3（ByPolygonVertex） |
| UV | ✅ float2（回环精确：delta=0） |
| 切线 | ✅ float3（ByPolygonVertex） |
| 蒙皮权重 | ✅ Deformer/Cluster |
| 骨骼层级 | ✅ 骨骼节点 |
| 材质引用 | ⚠️ 材质 ID 保留，贴图需手动指定 |

---

## 四、在 Blender 中编辑模型

### 导入 FBX
1. Blender → File → Import → FBX
2. 选择导出的 `.fbx` 文件
3. 检查网格、UV、法线是否正确

### 编辑注意事项
- **不要改变顶点数**（如果想保持蒙皮完整）：直接编辑顶点位置
- **可以增减面**：但骨骼蒙皮区域需要重新分配权重
- **UV 可以自由修改**：UV 回环已通过验证（1268 顶点 delta=0），Blender 中修改 UV 后导入管线完整保留
- **法线应该保留**：或在导出时设置正确的法线方向

### 导出回 FBX
1. Blender → File → Export → FBX
2. 设置：
   - Format: **ASCII**（不可用 Binary！）
   - Path Mode: **Copy**
   - Include: **Selected Objects**
   - Transform: **Scale 1.0, Apply Scalings**
   - Geometry: **Apply Modifiers**
   - **Forward: -Z Forward, Up: Y Up**（匹配游戏坐标系）

---

## 五、导入修改后的 FBX 并生成补丁

### 通过代码导入

```csharp
// 1. 读取原始的 MDL 数据（用于保留头信息）
byte[] originalMdl = File.ReadAllBytes("e2e_original.mdl");

// 2. FBX → MDL 转换
var fbxToMdl = FbxToMdlConverter.Convert("modified_model.fbx", originalMdl);

// 3. 保存为 MDL
File.WriteAllBytes("modified_model.mdl", fbxToMdl);
```

### 生成游戏补丁文件

```csharp
// 配置
string gameDir = @"D:\BaiduNetdiskDownload\NINJA GAIDEN 4 The Two Masters";
string assetsDir = Path.Combine(gameDir, "Assets");

// 1. 备份原始文件（自动时间戳）
var backup = new BackupManager(gameDir);
backup.Backup(Path.Combine(assetsDir, "@image0.dat"));

// 2. 创建修改记录
var modified = new ModifiedAssetEntry
{
    ParentEntry = selectedAsset,  // AssetEntry from AssetLibrary
    SubEntry = modelDataSub,     // modeldata.mdl SubAssetEntry
    ModifiedData = fbxToMdl,     // 修改后的 MDL 数据
    OriginalData = originalMdl,  // 原始数据
    Compress = false             // 不压缩（游戏支持）
};

// 3. 生成补丁文件（不会修改原始 .dat 文件）
PatchGenerator.GeneratePatch(assetsDir, new[] { modified }, compressData: false);

// 这会在 Assets/ 下创建:
//   @patch_{N}.csv  ← 补丁索引（记录新文件位置）
//   @patch_{N}.dat  ← 补丁数据（包含修改后的模型）
```

### 补丁文件格式

`@patch_N.csv` 内容示例：
```
Path,Offset,Unknown,Size,CompressedSize,FileCount,Unknown2,SubEntries
Assets/Environment/.../modeldata.mdl,0,0,82817,0,15,0,modeldata.mdl/0/0/82817/0/
```

`@patch_N.dat` 包含修改后的二进制数据，追加在文件末尾。

---

## 六、材质和纹理问题（重要）

### 材质工作流

**模型几何和材质是分离的。** 游戏引擎在加载模型时：

1. 读取 `.mdl` → 加载网格数据（顶点、UV、索引）
2. 读取 `materialmap.bin` → 确定每个 meshGroup 对应的材质 UUID
3. 按 UUID 查找 `MaterialInstance` 资产 → 获取渲染参数和贴图引用
4. 按贴图引用查找 `Texture` 资产 → 加载 DDS 贴图

### 替换模型后纹理能否正常工作？

| 情况 | 结果 | 原因 |
|------|------|------|
| **只改了顶点位置**（UV不变） | ✅ 纹理正常工作 | UV 和材质引用未变 |
| **改了 UV** | ✅ 纹理按新 UV 映射 | UV 回环验证通过（1268顶点 delta=0） |
| **新增/删除了材质 slot** | ⚠️ 可能显示错误 | `materialID` 映射关系可能断开 |
| **替换了整个包**（含 materialmap.bin） | ❌ 材质可能丢失 | 需同时补丁 MaterialInstance |

### 保持纹理正常的建议

1. **只替换 modeldata.mdl**（保留其他子文件不变）
2. **UV 可以自由修改** — 管线完整支持 UV 变更（Blender 编辑 → 导入 → 补丁 → 游戏）
3. **不要修改 materialID** — batch 中的 materialID 指向材质资源
4. **如果改动了 LOD 文件**：`LOD1.mdl`、`LOD2.mdl` 等也需要一起替换
5. **纹理替换**：如有需要，可以单独对 `Texture` 类型资产做补丁

### 材质查询示例

在资产数据库中搜索与模型关联的材质：

```
在 RONIN 中：
1. 展开 MaterialInstance 目录
2. 或按 UUID 搜索（从 materialmap.bin 的 UUID 查找）

程序方式：
foreach (var asset in library.All)
{
    if (asset.Type == AssetType.MaterialInstance)
        Console.WriteLine($"{asset.Path} [{asset.StringAssetID}]");
}
```

---

## 七、完整操作流程图

```
┌──────────────────────────────────────────────────────────┐
│  1. 打开 RONIN → Load AssetDatabase                      │
│     (加载到 107K 资产索引)                                │
└─────────────────────┬────────────────────────────────────┘
                      ▼
┌──────────────────────────────────────────────────────────┐
│  2. 浏览资产树 → 选中 SkeletalMesh 模型                   │
│     确认属性面板中的子文件清单                             │
└─────────────────────┬────────────────────────────────────┘
                      ▼
┌──────────────────────────────────────────────────────────┐
│  3. 导出模型数据                                          │
│     - 提取 modeldata.mdl / LOD1.mdl                       │
│     - MdlToFbxConverter → 生成 ASCII FBX                  │
└─────────────────────┬────────────────────────────────────┘
                      ▼
┌──────────────────────────────────────────────────────────┐
│  4. 在 Blender 中编辑模型                                  │
│     - 导入 FBX                                            │
│     - 编辑网格（保持 UV/蒙皮）                              │
│     - 导出 ASCII FBX                                       │
└─────────────────────┬────────────────────────────────────┘
                      ▼
┌──────────────────────────────────────────────────────────┐
│  5. 导入回游戏格式                                        │
│     - FbxToMdlConverter → MDL 二进制                       │
│     - MDLWriter 写入完整游戏格式                            │
└─────────────────────┬────────────────────────────────────┘
                      ▼
┌──────────────────────────────────────────────────────────┐
│  6. 生成补丁文件                                          │
│     - BackupManager 自动备份                               │
│     - PatchGenerator 创建 @patch_N.csv + .dat              │
│     - 不修改原始 @image*.dat                                │
└─────────────────────┬────────────────────────────────────┘
                      ▼
┌──────────────────────────────────────────────────────────┐
│  7. 启动游戏 ✅                                           │
│     - 引擎加载 @patch_N.csv → 覆盖原始资产                   │
│     - 从 @patch_N.dat 读取修改后的模型数据                   │
│     - 纹理通过原始 MaterialInstance 继续正常显示              │
└──────────────────────────────────────────────────────────┘
```

---

## 八、注意事项和限制

### 已知限制

| 限制 | 说明 |
|------|------|
| **GDeflate 压缩** | 当前使用未压缩补丁（`cmp=0`），游戏兼容但文件稍大 |
| **材质编辑** | 工具尚未支持 MaterialInstance 可视化编辑 |
| **LOD 同步** | 如果替换了高模，低 LOD 也需同步替换 |
| **蒙皮骨骼** | 骨骼权重保留从原始 MDL，FBX 导入暂不重建骨骼层级 |
| **纹理替换** | 纹理补丁需单独处理（替换 Texture 资产） |

### 文件安全

- ✅ **原始文件永不修改**：补丁系统只创建新文件
- ✅ **自动备份**：每个操作前在 `Backups/` 目录创建时间戳快照
- ✅ **恢复简单**：直接删除 `@patch_N.csv/.dat` 即可还原（或从备份恢复）
