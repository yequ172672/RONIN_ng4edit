# NG4MOD v2 互操作格式

本文是 `.ng4mod` v2 的公开交换合同，供 Mod 制作工具、格式检查器和 Mod 管理器在不依赖 NG4 Tool 源码的情况下实现兼容读写。

DLL 作者接入游戏内模组配置界面，请阅读同目录的 [Runtime GUI 接入指南](RUNTIME_INTEGRATION.md)；[ModControl.h](ModControl.h) 提供完整可选控制 ABI，可与本目录一起独立分享。

成品 `.ng4mod` 使用 **YequModManager** 安装：从 [YequModManager 下载地址](https://github.com/yequ172672/YequModManager-Release) 的 Releases 获取安装包，按 [管理器使用教程](https://www.caimogu.cc/post/2493721.html) 配置游戏目录、导入并启用 Mod，应用后启动或重启游戏。

本文中的 MUST、MUST NOT、SHOULD 和 MAY 分别表示必须、禁止、建议和可选。实现只有在完成容器、manifest、payload 和安装边界校验后，才应声明支持 NG4MOD v2。

## 1. 格式概览

`.ng4mod` v2 是一个带 EOF trailer 的原始二进制容器，不是 ZIP：

```text
offset 0
+--------------------------------------+  --+
| game-compatible DAT payload prefix   |    | payload_length bytes
+--------------------------------------+  --+
| optional PNG cover                   |    | cover.length bytes
+--------------------------------------+  --+
| UTF-8 metadata JSON                  |    | metadata_length bytes
+--------------------------------------+  --+
| fixed EOF trailer                    |    | 160 bytes
+--------------------------------------+  --+
EOF
```

DAT prefix 可以依次包含三类 payload：

1. `assets`：可直接写入游戏 Patch DAT 的完整 parent asset；
2. `files`：当前只允许受管理器独占发布的 `Assets/AssetDatabase.dat`；
3. `native_libraries`：Mod 私有 DLL，包含入口 DLL 和它的同目录依赖。

三类数组都必须存在且不能为 `null`，但允许为空；三者合计必须至少声明一个 payload。因此纯 DLL 包可以使用 `assets: []` 和 `files: []`。

## 2. DAT payload prefix

每个 payload 都是文件开头 DAT prefix 内的一个连续 slice：

- slice 起点是非负 Int64，必须按 16 字节对齐；
- 第一项的规范偏移是 `0`，后续项从前一项结束位置向上对齐到 16 字节；
- 对齐空洞必须全部写零，并计入完整 prefix SHA-256；
- slice 的 `payload_size` 必须大于零，范围不能重叠或越过 `payload_length`；
- 每项 `payload_sha256` 只覆盖自身 slice；
- trailer 中的 `payload_sha256` 覆盖完整 `[0, payload_length)`；
- 最后一个 slice 必须恰好结束于 `payload_length`，不能有未声明的尾随 DAT 字节。

物理类别顺序 MUST 为 `assets`、`files`、`native_libraries`。同一类别内按 `payload_offset` 升序解释。规范 writer SHOULD 同时让数组声明顺序与物理顺序一致；`native_libraries` 的数组顺序还是同一 Mod 内多个入口 DLL 的加载顺序。

`assets` 中每个 slice 是完整 parent payload，不是只包含被替换 child 的差分数据。v2 当前只支持未压缩游戏 payload：`game_compression` 必须为 `none`，所有 `compressed_size` 必须为 `0`。

## 3. 160 字节 EOF trailer

reader 必须从 EOF 前 160 字节识别 v2，不能只检查扩展名或文件开头。所有多字节整数均为 little-endian；offset 和 length 均为有符号 Int64。

| 偏移 | 长度 | 类型 | 字段 | 约束 |
|---:|---:|---|---|---|
| 0 | 8 | byte[8] | `magic` | ASCII `NG4MOD2\0`，十六进制 `4e 47 34 4d 4f 44 32 00` |
| 8 | 4 | UInt32 LE | `trailer_size` | 必须为 `160` |
| 12 | 4 | UInt32 LE | `schema_version` | 必须为 `2` |
| 16 | 8 | reserved | 保留 | 必须全零 |
| 24 | 8 | Int64 LE | `payload_length` | DAT prefix 长度，必须非负 |
| 32 | 8 | Int64 LE | `metadata_offset` | metadata 的绝对文件偏移 |
| 40 | 8 | Int64 LE | `metadata_length` | `1..16777216` |
| 48 | 16 | reserved | 保留 | 必须全零 |
| 64 | 32 | byte[32] | `payload_sha256` | SHA-256(`[0, payload_length)`) 的原始 32 字节摘要 |
| 96 | 32 | byte[32] | `metadata_sha256` | metadata 原始字节的 SHA-256 |
| 128 | 32 | byte[32] | `trailer_sha256` | 下述规则计算的 trailer SHA-256 |

文件长度必须满足：

```text
file_length = metadata_offset + metadata_length + 160
metadata_offset >= payload_length
```

无封面时 `metadata_offset == payload_length`；有封面时两者之差必须恰好等于 `cover.length`。

`trailer_sha256` 的计算规则：

1. 填写 trailer 的 `0..127` 字节；
2. 将 `128..159` 保持为零；
3. 对完整 160 字节计算 SHA-256；
4. 将原始 32 字节摘要写入 `128..159`。

reader 校验时必须复制 trailer、清零副本的 `128..159`，再计算摘要。所有长度加法都要使用 checked Int64；负数、溢出和越界必须拒绝。v2 的 reserved 字节不表达 flags，扩展这些字节需要提升 schema version。

## 4. Metadata JSON

metadata 是无 BOM 的 UTF-8 JSON，最大 16 MiB。hash 针对文件中实际存在的原始 JSON 字节，不能先解析或重新格式化再校验。

规范 writer SHOULD 使用 snake_case 属性名、两空格缩进和 LF 换行，以获得确定性输出。reader MAY 在 `schema_version == 2` 时忽略未知 JSON 字段，但 MUST 识别本文定义的所有 payload 数组；忽略 `native_libraries` 会漏装 DLL，不能视为完整 v2 支持。

### 4.1 顶层字段

| 字段 | 类型 | 要求 |
|---|---|---|
| `format` | string | 精确为 `ng4mod` |
| `schema_version` | number | 精确整数 `2` |
| `mod_id` | string | 非空、稳定的 Mod ID；不得未经清理直接用作路径 |
| `name` | string | 非空显示名称 |
| `version` | string | 非空版本字符串 |
| `author` | string | 无值时写 `""` |
| `description` | string | 无值时写 `""` |
| `icon` | string | 兼容字段，v2 通常写 `""`；不能指向容器内虚构路径 |
| `cover` | object/省略 | 可选 PNG 描述，见 4.2 |
| `dependencies` | string[] | 非 `null`，元素不得为 `null`、空串或纯空白 |
| `game` | object | 必须存在 |
| `game.id` | string | 精确为 `ninja-gaiden-4` |
| `game.asset_database_sha256` | string | `""` 或 64 位十六进制 SHA-256 |
| `assets` | object[] | 非 `null`；纯原生 Mod 可以为空 |
| `files` | object[] | 非 `null`；最多一项 |
| `native_libraries` | object[] | 非 `null`；最多 64 项 |

SHA-256 的 JSON 表示统一为 64 个十六进制字符；比较时 SHOULD 忽略大小写。trailer 内的 SHA-256 则是原始 32 字节，不是十六进制文本。

### 4.2 Cover

`cover` 存在时结构如下；示例数值和 hash 必须由实际 PNG 重新计算：

```json
{
  "media_type": "image/png",
  "offset": 123456,
  "length": 45678,
  "sha256": "0000000000000000000000000000000000000000000000000000000000000000"
}
```

约束：

- `offset == payload_length`；
- `offset + length == metadata_offset`；
- `length` 必须为 `1..8388608`；
- `sha256` 覆盖 PNG 原始字节；
- PNG 必须具有合法 signature、首个 `IHDR`、至少一个 `IDAT` 和最终唯一 `IEND`，所有 chunk CRC 必须正确；
- 宽高均不能超过 4096，像素总数不能超过 16,777,216；
- APNG 的 `acTL`、`fcTL`、`fdAT` chunk 不受支持。

封面不属于 DAT prefix，不得复制到游戏 Patch DAT。

### 4.3 Assets

每个 `assets` 元素包含：

| 字段 | 要求 |
|---|---|
| `asset_id` | 四组八位十六进制：`xxxxxxxx-xxxxxxxx-xxxxxxxx-xxxxxxxx`；包内忽略大小写唯一 |
| `logical_path` | 非空安全相对路径，使用 `/` |
| `asset_type` | 非空资产类型名称 |
| `storage_path` | `Assets/Files/.../<asset_id>/asset.bin`；包内忽略大小写唯一 |
| `csv_unknown` | 不透明 CSV 字段，不得包含逗号、CR、LF |
| `csv_metadata` | 不透明 CSV 字段，不得包含逗号、CR、LF |
| `payload_path` | 精确为 `payload/<asset_id>.bin`；它是逻辑标识，不是 ZIP entry |
| `payload_size` | 正 Int64，等于 parent slice 长度 |
| `payload_sha256` | parent slice 的 SHA-256 |
| `payload_offset` | 16 字节对齐的 DAT 绝对偏移 |
| `game_compression` | 精确为 `none` |
| `sub_entries` | 非 `null` child 数组 |
| `intent` | 非 `null` 替换意图对象 |

路径不得包含 `\`、盘符、绝对路径、空 component、`.` 或 `..`。

每个 `sub_entries` 元素包含：

| 字段 | 要求 |
|---|---|
| `name` | 非空 child 文件名，不得包含 `/`；parent 内忽略大小写唯一 |
| `addressing` | 精确为 `local` 或 `global` |
| `offset` | 相对于当前 parent payload 起点的非负 Int64 |
| `size` | 非负 Int64；`offset + size` 必须位于 parent slice 内 |
| `compressed_size` | 当前必须为 `0` |

`addressing` 必须保留源 CSV 的真实语义，不能仅按 `asset_type` 推断。非纹理资产也可能合法包含 global child。纹理资产还应进行纹理专用一致性校验，包括 `Image.img`、`Metadata.bin`、连续 global mip、DDS header 和 descriptor 范围。

`intent` 结构：

```json
{
  "replaced_sub_entries": ["modeldata.mdl"],
  "source_archive": "@image4",
  "source_fingerprint": ""
}
```

`replaced_sub_entries` 必须非 `null`、无重复，并且每一项都存在于当前 `sub_entries`。`source_archive` 和 `source_fingerprint` 是来源追踪字段，不改变寻址；无值时写 `""`。

### 4.4 Files

当前 v2 只允许一个受管理器独占发布的文件。下例 hash 只是字段占位，实际包必须重新计算：

```json
{
  "target_path": "Assets/AssetDatabase.dat",
  "kind": "asset_database",
  "payload_offset": 123456,
  "payload_size": 7890,
  "payload_sha256": "0000000000000000000000000000000000000000000000000000000000000000"
}
```

`files` 最多一项，目标和 kind 必须与上例精确一致。其 slice 遵守相同的 16 字节对齐、范围和 SHA-256 规则；规范 producer SHOULD 将单项大小限制为 512 MiB。安装器必须保证同一时刻最多一个启用 Mod 拥有这个目标，并以可恢复事务发布或还原它。

不能用 `files` 任意写入游戏根目录、代理 DLL、INI 或其他路径。

### 4.5 Native libraries

`native_libraries` 是 v2 的 DLL payload 注册表。每项包含：

| 字段 | 要求 |
|---|---|
| `name` | 纯 `.dll` 文件名；不得包含目录、盘符或路径分隔符，整项不得为 `.` 或 `..`；包内忽略大小写唯一 |
| `load` | `true` 为游戏进程入口 DLL；`false` 为随入口提取的同目录依赖 |
| `exclusive_claims` | 可选 token 数组；省略等价于 `[]`，仅 `load=true` 可声明 |
| `payload_offset` | 16 字节对齐的 DAT 绝对偏移 |
| `payload_size` | 正 Int64；规范 producer 最大 256 MiB |
| `payload_sha256` | DLL slice 的 SHA-256 |

包级约束：

- 最多 64 个 DLL；
- `native_libraries` 非空时至少一个 `load=true`；
- 数组顺序定义同一包多个入口 DLL 的加载顺序；
- 所有 DLL 都提取到该 Mod 的同一私有目录，`load=false` 依赖不得单独注册为入口；
- 包不能要求把任何入口 DLL 发布为游戏根的 `winmm.dll`、`dinput8.dll` 等代理；根代理和注册表属于管理器，不属于 Mod 包。

`exclusive_claims` 用于声明不能由多个启用 Mod 同时占用的 Hook 或运行时所有权：

- 每个入口最多 16 项；
- token 长度为 `1..128`；
- 只允许 ASCII 字母、数字、`.`、`-`、`_`；
- 大小写敏感，单个入口内不能重复；
- `load=false` 依赖 MUST NOT 声明 claim；
- 安装器必须在写入游戏目录前汇总所有已启用包并拒绝重复 claim。

例如，独占敌人生成根句柄 Hook 的入口可声明：

```json
"exclusive_claims": ["ng4.enemy-spawn-owner.v1"]
```

入口 DLL MAY 导出以下可选初始化函数：

```text
DWORD WINAPI Ng4ModInitialize(DWORD timeout_ms)
```

导出名必须为未修饰的 `Ng4ModInitialize`。公共加载器应在 loader lock 外、`LoadLibrary` 成功后调用它，并只在返回 `ERROR_SUCCESS`（0）时把入口视为初始化成功。函数必须在传入时限内返回；耗时 Hook 初始化不能阻塞在 `DllMain`。没有此导出的 DLL 保持传统的 DllMain-only 行为。

Mod DLL 还可提供[可选 Mod 控制 ABI 与 GUI 接入](RUNTIME_INTEGRATION.md)，供游戏内 Runtime
读取启用状态和参数、提交配置修改。此接口独立于容器版本及初始化合同，不要求其他加载器实现，
也不授权动态替换／卸载 DLL。

## 5. Asset CSV 重建

安装器只把 `[0, payload_length)` 写为游戏 Patch DAT，绝不能把 cover、metadata 或 trailer 写入 DAT。包含 asset 时，以 UTF-8 无 BOM、CRLF 生成配套 CSV，首行固定为：

```text
header,2,0,
```

每个 asset 按 `payload_offset` 升序写一行，八个字段依次为：

```text
storage_path,payload_offset,csv_unknown,payload_size,0,local_file_count,csv_metadata,sub_entry_blob
```

`local_file_count` 只统计 `addressing == "local"` 的 child。`sub_entry_blob` 按 manifest 中的 child 顺序拼接，每项格式为：

```text
name/global_offset/local_offset/size/0
```

寻址转换：

| `addressing` | `global_offset` | `local_offset` |
|---|---:|---:|
| `local` | `0` | `sub_entry.offset` |
| `global` | `asset.payload_offset + sub_entry.offset` | `0` |

所有数值使用 invariant decimal Int64。生成后应重新读取 CSV/DAT，验证 parent/child 范围、local/global 转换、slice hash 和 DAT 总长度。

纯 DLL 包没有 asset，不应伪造空 DAT/CSV pair。管理器只需保留已验证的包快照并发布原生库注册状态。

## 6. 第三方适配方法

### 6.1 Reader / 校验器

推荐顺序如下，任何一步失败都不得暴露安装计划：

1. 检查文件至少 160 字节，从 EOF 读取 trailer 并验证 magic、版本和 reserved 字节；
2. 使用 checked Int64 验证 `payload_length`、`metadata_offset`、`metadata_length` 与总文件长度；
3. 清零 trailer hash 区后校验 trailer SHA-256；
4. 流式校验完整 DAT prefix SHA-256；
5. 读取 metadata 原始字节，先校验 hash，再按严格 UTF-8 解析 JSON；
6. 校验顶层 discriminator、必填字段、路径、UUID、数组数量和唯一性；
7. 按 `assets -> files -> native_libraries` 收集 slice，校验 16 字节对齐、零填充、无重叠、无越界、无尾随字节和逐项 SHA-256；
8. 如有 cover，校验它恰好填满 prefix 与 metadata 之间的区域，并校验 PNG 与 hash；
9. 仅在全部通过后生成只读安装计划。

最小 reader 伪代码：

```text
size = file.length
require size >= 160
trailer = read_exact(size - 160, 160)
require trailer[0:8] == "NG4MOD2\0"
require read_u32_le(trailer, 8) == 160
require read_u32_le(trailer, 12) == 2

payload_length = read_i64_le(trailer, 24)
metadata_offset = read_i64_le(trailer, 32)
metadata_length = read_i64_le(trailer, 40)
require checked(metadata_offset + metadata_length + 160) == size

require sha256(trailer with bytes 128..159 zeroed) == trailer[128:160]
require sha256(file[0:payload_length]) == trailer[64:96]
metadata = read_exact(metadata_offset, metadata_length)
require sha256(metadata) == trailer[96:128]

manifest = parse_strict_utf8_json(metadata)
validate_manifest_and_all_slices(manifest, payload_length)
validate_optional_cover(manifest.cover, payload_length, metadata_offset)
```

### 6.2 Writer

1. 先准备完整 parent payload、受管文件和 DLL 原始字节；
2. 按类别顺序排列，每项起点向上对齐到 16 字节并用零填充；
3. 记录每项 offset、size 和 SHA-256；
4. 记录 `payload_length`，可选地直接追加 PNG cover；
5. 生成无 BOM UTF-8 metadata，并记录实际 byte length 与 SHA-256；
6. 构建零初始化的 160 字节 trailer，最后计算 trailer SHA-256；
7. 原子写入目标 `.ng4mod`，完成后重新以 reader 路径验证一次。

修改任何 payload、对齐、JSON 空白、属性顺序或换行后，都必须重新计算受影响的 slice、prefix、metadata 和 trailer hash。

### 6.3 Mod 管理器 / 安装器

安装器还必须实现格式之外的生命周期边界：

- 所有校验和 claim 冲突检查必须发生在首次游戏目录写入之前；
- 保留完整源包或内容寻址快照，用 staging + 原子替换发布 manager-owned 文件；
- asset Patch DAT 只复制 `payload_length` 字节，CSV 从 metadata 重建；
- 每个 Mod 的全部 DLL 提取到独立私有目录，跨 Mod 同名 DLL 不能互相覆盖；
- 只把 `load=true` 注册为入口，并在游戏进程中加载，不能把不可信 DLL 加载进管理器进程；
- 多个启用 Mod 的入口按管理器优先级、安装顺序、`mod_id` 和包内声明顺序形成稳定加载次序；
- 禁用或卸载一个 Mod 时重建注册状态，不影响其他 Mod；删除或还原前核对所有权与已发布 hash；
- 未知外部改写、路径逃逸、符号链接或 reparse point 必须失败关闭。

## 7. Native-only 最小样本

同目录的 [NG4MOD_V2_NATIVE_ONLY_SAMPLE.json](NG4MOD_V2_NATIVE_ONLY_SAMPLE.json) 是最小 manifest 样本。它描述一个仅含入口 DLL 的包：

- `assets` 和 `files` 均为空；
- `native_libraries` 只有一个 `load=true` 入口；
- DLL slice 位于 offset `0`；
- 声明一个示例独占 claim。

样本假定 payload 是四个测试字节：

```text
4d 5a 90 00
```

它的 SHA-256 是：

```text
9f2981a7cc4d40a2a409dc895de64253acd819d7c0011c8e80b86fe899464e31
```

因此样本的 `payload_length` 为 `4`、`metadata_offset` 为 `4`。将 sample JSON 以无 BOM UTF-8 序列化后，使用其实际字节数和 hash 填写 trailer，即可构造一个能验证容器结构的最小 fixture。

这四个字节不是可执行 PE DLL，样本只用于 parser/writer 互操作测试，不能启用或加载。生产包必须替换为真实 x64 DLL，并重算 `payload_size`、`payload_sha256`、完整 prefix hash、metadata hash 和 trailer hash。

## 8. 兼容性与版本策略

- 旧的 asset-only v2 包继续有效，写 `files: []`、`native_libraries: []` 即可；
- `exclusive_claims` 是可选字段，缺失时按空数组处理；
- 更新后的 v2 reader 必须接受 `assets: []` 的纯 native 包，不能再把“至少一个 asset”作为格式条件；
- ZIP、7z、RAR 只能作为外层传输包。raw v2 的识别标志位于 EOF trailer；
- legacy v1 是包含 `ng4mod.json` 和 `payload/...` entry 的 ZIP 容器。支持迁移时必须先完整验证 v1，再重建 raw v2，不能只改扩展名；
- `payload_path` 在 raw v2 中仍是必需逻辑标识，不能据此查找 ZIP entry；
- SHA-256 只提供完整性，不提供作者身份认证或代码签名；
- trailer 布局、寻址语义或现有必填字段含义发生不兼容变化时，必须提升 `schema_version`。

第三方适配完成的最低验收是：能够读取 asset-only 包、asset + DLL 混合包和 native-only 包；能够拒绝 hash 损坏、非零 padding、越界 slice、重复 DLL 名、无入口的 DLL 集合以及重复 exclusive claim；安装后游戏 Patch DAT 不包含 metadata 或 trailer。
