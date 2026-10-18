# 第三方 Mod 接入 Runtime 模组 GUI

本文面向已有 Ninja Gaiden 4 原生 DLL 的作者，说明如何让 NG4 Tool Runtime 自动展示配置并接受修改。当前合同是 **Windows x64、Mod 控制 ABI v1**；它与 [NG4MOD v2 容器格式](README.md)独立版本化。

本目录附带可直接引用的 [ModControl.h](ModControl.h)，包括完整结构定义、枚举、调用约定和可选 C++ 辅助函数。将本目录整体分享即可获得格式说明、控制接口和接入指南。本文按 2026-10-18 的当前实现核对；游戏相关 Hook 仍由作者针对自己的游戏基线验证。

## 1. 接入后的工作分工

作者无需链接 Runtime DLL、引入 ImGui、注册 GUI 绘制回调或操作窗口。入口 DLL 导出 `Ng4ModQueryControl`，Runtime 根据返回的参数描述生成模组页。

| 部件 | 负责什么 |
|---|---|
| NG4MOD 包与管理器 | 校验包、安装 DLL、处理独占 claim、生成加载登记 |
| 游戏中的原生加载器 | 加载入口 DLL，在 loader lock 外调用可选初始化函数 |
| 作者的 Mod DLL | 安装玩法 Hook、读取与保存配置、描述参数、校验修改、在安全时机应用 |
| Runtime | 发现已加载 DLL、查询控制接口、生成 GUI、转交修改请求、显示真实结果 |

Runtime 不负责安装或主动加载 Mod，也不提供 DLL 代码热替换。GUI 的“启用”应是作者提供的行为开关；不能把关闭开关实现成在回调内卸载自身 DLL。

## 2. 让 Runtime 发现你的 DLL

推荐把入口放进 NG4MOD 的 `native_libraries`，设置 `load: true`；同目录依赖设置 `load: false`。纯 DLL 包使用 `assets: []`、`files: []`。完整打包规则及可选 `Ng4ModInitialize` 初始化合同见[格式文档的 Native libraries](README.md#45-native-libraries)。

管理器安装后，Runtime 读取游戏根目录的 `ng4-native-plugins.ini`，将登记路径与当前进程实际加载的 DLL 完整路径匹配。仅导入包、尚未启动游戏加载 DLL 时，GUI 会显示未加载。**作者不要让 DLL 自行改写这份管理器注册表**，也不要把 Runtime 的 `dinput8.dll` 塞进自己的 Mod 包。

如果使用自己的加载器，已加载、未登记、且提供有效控制 ABI 的 DLL 也会被发现，显示为“当前进程已加载但未登记”。Runtime 不会为了探测文件而调用 `LoadLibrary`。

当前模组管理专用配置在菜单打开时约每秒查询一次，关闭后停止周期查询；已排队的修改仍可完成。GUI 刷新不是玩法 tick，Mod 必须能在 Runtime 不存在或菜单关闭时独立运行。

## 3. 导出控制接口

使用未修饰的 C 导出名 **`Ng4ModQueryControl`**。调用约定是 `NG4_MOD_CALL`（`__cdecl`）；不要与 `Ng4ModInitialize` 的 `WINAPI` 声明混写。

下面是接入代码骨架。`GetInfo`、`GetParameter`、`SetParameter` 是作者需要实现的三个回调，后文说明各自合同；这段代码本身不是完整玩法 Mod。

```cpp
#include "ModControl.h"

uint32_t NG4_MOD_CALL GetInfo(Ng4ModInfo* output);
uint32_t NG4_MOD_CALL GetParameter(uint32_t index, Ng4ModParameter* output);
uint32_t NG4_MOD_CALL SetParameter(const char* id, double value,
                                  Ng4ModApplyResult* output);

extern "C" __declspec(dllexport)
uint32_t NG4_MOD_CALL Ng4ModQueryControl(uint32_t version,
                                        Ng4ModControlApi* output)
{
    return ng4tool::mod_control::Api(
        version, output, GetInfo, GetParameter, SetParameter);
}
```

有可写 `NG4_MOD_UINT64` 参数时，还要实现并将 `SetIntegerParameter` 作为 `Api` 的最后一个实参传入：

```cpp
uint32_t NG4_MOD_CALL SetIntegerParameter(const char* id, uint64_t value,
                                         Ng4ModApplyResult* output);
```

即使全部参数只读，`set_parameter` 也必须非空，可以统一返回 `NG4_MOD_READ_ONLY`。不用精确整数写入时，`set_integer_parameter` 可以为空。

### 二进制布局

- 不要修改头文件结构、数组长度、成员顺序或添加 `#pragma pack(1)`；使用 Windows x64 默认布局。
- 每个输出结构由调用方先设置 `struct_size`，回调先核对，再填写全部字段。`Output(output)` 辅助函数完成核对、清零和恢复尺寸。
- `QueryControl` 必须检查请求版本和 API 结构尺寸。不支持时返回 `NG4_MOD_INVALID`，不能返回部分兼容的指针表。
- API 回调指针在 DLL 存续期间保持有效；结构中不能传 STL 对象、临时字符串指针、游戏对象指针或需另一侧释放的内存。
- 字符数组使用 UTF-8，并在容量内以 NUL 结尾。容量按字节计算，预留终止字节，避免截断中文多字节字符。
- 所有回调必须在内部处理异常，不允许 C++ 异常越过 DLL 边界。

| 类型 | 当前 x64 字节数 |
|---|---:|
| `Ng4ModInfo` | 656 |
| `Ng4ModChoice` | 72 |
| `Ng4ModParameter` | 2280 |
| `Ng4ModApplyResult` | 272 |
| `Ng4ModControlApi` | 40 |

C++17 及以上可使用头文件辅助函数；纯 C 或其他语言可按相同布局实现，尺寸与调用约定必须一致。

## 4. 提供 Mod 信息和参数

### `get_info`

返回 `NG4_MOD_OK`，并填写：

| 字段 | 写法 |
|---|---|
| `id` | 稳定内部 ID，建议与 manifest 的 `mod_id` 一致 |
| `name`、`description` | 中文显示名称与说明 |
| `version` | 实际 DLL 版本 |
| `state` | `NOT_INITIALIZED`、`READY` 或 `FAILED`，反映初始化／Hook 可用状态 |
| `flags` | 普通模块用 `0`；实验模块可标 `EXPERIMENTAL`，独立入口可标 `STANDALONE` |
| `parameter_count` | 当前参数数量，当前 Runtime 上限为 1024 |

**禁用不等于未初始化。** Hook 已准备好、只是用户关闭玩法开关时，仍保持 `READY`，让 `enabled` 参数显示关闭值，用户才能再次启用。初始化尚未完成时不要假报 `READY`。

### `get_parameter(index)`

索引范围为 `0..parameter_count-1`；越界返回 `NG4_MOD_INVALID`。参数 `id` 在同一 DLL 内唯一且长期稳定，setter 按 `id` 接收请求。保持参数数量、顺序及 ID 稳定，不能随着鼠标悬浮或瞬时玩法状态反复增删条目。

以下展示参数描述的构造方式；`enabled` 和 `multiplier` 应取自 Mod 的同步配置快照，不是固定示例值：

```cpp
using namespace ng4tool::mod_control;

auto enabled_parameter = Parameter(
    "enabled", "启用", NG4_MOD_BOOL, enabled ? 1.0 : 0.0);

auto damage_parameter = Parameter(
    "damage_multiplier", "伤害倍率", NG4_MOD_FLOAT,
    multiplier, 0.1, 10.0, 0.05, NG4_MOD_PARAM_NEXT_EVENT,
    "下一次生成敌人时应用，已有敌人保持原值。");

// 在对应的 get_parameter 分支中：
// if (!Output(output)) return NG4_MOD_INVALID;
// *output = damage_parameter;
// return NG4_MOD_OK;
```

| `kind` | GUI | 参数要求 |
|---|---|---|
| `NG4_MOD_BOOL` | 复选框 | 值为 `0` 或 `1`，范围覆盖当前值 |
| `NG4_MOD_INTEGER` | 整数数值输入 | `value` 是整数，实际走 `double`，不要存超出其精确整数范围的数据 |
| `NG4_MOD_FLOAT` | 小数输入 | 当前值、范围和步长必须有限；步长非负 |
| `NG4_MOD_ENUM` | 下拉选择 | `choice_count` 为 `1..24`，每项有整数值和中文名称，当前值必须在选项中 |
| `NG4_MOD_KEY` | 按键名称与“捕获按键”按钮 | 使用 Windows 虚拟键码；具体支持范围由 Mod 校验 |
| `NG4_MOD_UINT64` | 精确无符号 64 位输入 | 用 `integer_value/minimum/maximum` 和专用整数 setter，全程不经过 `double` |

非 UINT64 类型填写 `value/minimum/maximum/step`，须满足 `minimum <= value <= maximum`。字段非法时该参数会被 Runtime 隐藏并显示描述错误。

BOOL／ENUM 更改后提交；数值输入在编辑结束、失去焦点后提交。键盘捕获由 Runtime 接收按键并转交虚拟键码。当前 ABI 没有独立的手柄绑定控件类型；手柄按钮可用 ENUM 描述，组合键可拆成多个参数，解释和玩法应用由 Mod 负责。

### 默认折叠的细节配置

当前 GUI 根据参数 ID 前缀分组：

| ID 示例 | 显示位置 |
|---|---|
| `enabled`、`damage_multiplier` | Mod 展开后的常用配置区 |
| `enemy.123.normal`、`enemy.123.madness` | 默认折叠的“分怪物伤害倍率” |
| `rule.stage1.enabled`、`rule.stage1.spacing` | 默认折叠的“分关卡／生成规则” |
| `accessory.general.*`、`accessory.yakumo.*`、`accessory.ryu.*`、`accessory.special.*` | 对应的饰品配置组 |
| `abyss_*` | “深渊批次与挑战”组 |

只在语义对应时使用这些前缀。ABI v1 没有自定义分类、布局或任意 ImGui 绘制扩展；不能通过添加未定义结构字段接入新控件。显示文字用 `label`，解释和生效边界用 `description`，不要把全部使用说明放进控件名称。

## 5. 接收修改并返回真实结果

`set_parameter` 接收参数 ID 和 `double`；UINT64 使用专用 setter。即使 GUI 已有限制，DLL 仍需检查 ID、类型、范围、枚举成员、只读状态及当前可用性。

每次 setter 填写 `Ng4ModApplyResult`，函数返回码与 `output.code` 保持一致；`message` 用中文。普通参数的 `output.value` 写接受值。UINT64 没有精确的结果值字段，`output.value` 可写有限占位值 `0`，精确值由随后 `get_parameter.integer_value` 回读。

| 返回码 | 应用场景 |
|---|---|
| `NG4_MOD_OK` | 配置已保存，声明的即时行为已应用 |
| `NG4_MOD_PENDING` | 配置已接受并保存，等待下一次生成／动作退场等安全事件 |
| `NG4_MOD_RESTART_REQUIRED` | 配置已保存，本次进程确实无法应用，需重启 |
| `NG4_MOD_INVALID` | ID、值、范围或请求结构无效 |
| `NG4_MOD_UNAVAILABLE` | 当前运行状态不能接受修改 |
| `NG4_MOD_READ_ONLY` | 参数没有写入实现或本身是只读能力 |
| `NG4_MOD_IO_ERROR` | 配置持久化失败 |
| `NG4_MOD_INTERNAL_ERROR` | 其他内部错误 |

参数的静态说明与返回值配合使用：`NG4_MOD_PARAM_READ_ONLY` 禁止编辑；`NG4_MOD_PARAM_NEXT_EVENT` 标注下一事件生效；`NG4_MOD_PARAM_RESTART` 标注重启后生效。没有这些标志时用 `0`。Mod 尚未 `READY` 时，当前 GUI 只允许非只读的 RESTART 参数编辑，其余禁用。

推荐写入流程：

```text
验证请求及状态
  -> 在 Mod 内串行化配置事务，生成候选配置
  -> 通过本 Mod 持有的 INI 句柄保存候选配置
  -> 保存失败：返回 IO_ERROR，保留原已发布配置
  -> 保存成功：发布配置快照／提交安全事件请求
  -> 返回 OK、PENDING 或真正的 RESTART_REQUIRED
```

不要只保存文件却返回“已实时应用”。配置快照应描述 Mod 已接受的值；延迟生效的参数通过标志和返回消息明确提示。Runtime 在 setter 后重新查询参数，不应依赖 `output.value` 单独更新 GUI。

## 6. 线程、Hook 与初始化

控制回调在 Runtime 的后台服务线程调用，**不是游戏线程，也不是 ImGui 绘制线程**。Runtime 在回调期间持有 DLL 引用，但不会为回调提供游戏对象生命周期保证，也没有强制中止阻塞回调的超时机制。

- getter 读取短时同步快照；避免扫内存、重建大表、等待下一帧或进行耗时初始化。
- setter 可校验和保存配置；游戏对象修改交给本 Mod 已有的游戏线程 Hook／任务队列，并返回 `PENDING`。
- 锁保护 Mod 自己的配置与待办状态，不要持锁等待游戏线程回调同一把锁。
- 玩法 Hook 自行检查启用开关；重新启用必须有真实执行路径，不能只改 UI 值。
- 初始化签名验证、配置加锁或必需 Hook 失败时，通过状态和返回码反映失败。
- `DllMain` 中不等待线程、不做耗时 Hook 初始化；使用格式文档中的初始化入口或已验证的异步初始化机制。

## 7. 中文 INI 与运行中独占写入

面向用户的参数名称、INI 节名／键名／注释采用中文和 UTF-8；内部参数 ID 和 ABI 符号保持稳定机器标识。管理器的注册表协议字段保持既有格式，与 Mod 自己的中文配置区分。

示例：

```ini
; 修改此文件前请退出游戏；运行中可通过模组界面设置。
[通用]
启用=是

[伤害]
倍率=1.0
```

启动顺序是：仅在文件不存在时创建默认配置，随后取得独占写入句柄，再从该句柄进行最终读取并校验，最后启用玩法。这样退出游戏后手工修改的值会成为下次启动和 GUI 的初值。不添加旧英文节／键兼容层，也不在升级时覆盖已有中文配置。

Windows 实现可使用 `CreateFileW`，访问权限为 `GENERIC_READ | GENERIC_WRITE`，共享权限仅 `FILE_SHARE_READ`，已存在文件用 `OPEN_EXISTING`。整个运行期保留句柄，保存走该句柄并刷新；不要另开写句柄，也不要用临时文件 rename 覆盖它。其他进程的写入、删除和替换因此被拒绝，退出游戏后释放。

所有写入入口，包括自动补充条目和 GUI setter，都必须服从同一配置事务。加锁或读取失败时停止依赖该配置的功能并报告失败，不以默认值偷偷继续运行。配置默认放在本 Mod 的私有目录，不改写其他作者的文件。NG4MOD v2 的 `files` 不能用来安装任意 INI；让 DLL 首次运行创建自己的配置。

## 8. 作者验收

1. 对最终 x64 DLL 检查导出表，确认存在未修饰的 `Ng4ModQueryControl`；版本／尺寸不匹配请求被拒绝。
2. 检查每个参数的 ID 唯一性、中文 UTF-8、NUL 终止、范围和枚举；UINT64 用大于 `2^53` 的值及上限验证精确回读。
3. 导入含最新 DLL 的 NG4MOD，启动游戏后按 P 打开模组页，确认登记、加载、控制接口和 Mod 状态。
4. 修改 BOOL、数值、枚举和键盘绑定，核对实际玩法及 INI；下一事件设置要等指定事件再验收。
5. 关闭再打开菜单、刷新快照，确认配置与折叠状态稳定；退出游戏手工改中文 INI，重启确认读取。
6. 验证运行中外部写入／删除／替换被拒绝；注入保存失败时 setter 返回错误，内存配置不假报更新。
7. 不加载 Runtime 时，Mod 仍能根据 INI 独立运行。

## 9. 常见接入问题

| 现象 | 检查位置 |
|---|---|
| 已安装但未加载 | 管理器启用状态、`load`、初始化失败、游戏是否已重启；GUI 不会代为加载 |
| 旧版／不兼容 | 导出名、请求版本、结构尺寸、三个必需回调是否完整 |
| 某些参数消失 | 参数描述非法、枚举不含当前值、数量超限；未就绪时返回 UNAVAILABLE 的参数暂不显示 |
| 开关灰色无法恢复 | 是否把“用户禁用”误写成 `NOT_INITIALIZED`，或错误设置 READ_ONLY |
| 修改后值回跳 | getter 是否仍读旧快照、保存是否失败、setter 是否真的发布了接受值 |
| GUI 成功但玩法无变化 | Hook 是否读取新配置、是否有安全事件应用路径、返回码是否如实标注延迟 |
| 大整数精度丢失 | 是否误用 INTEGER／FLOAT 或把 UINT64 转成了 double |
| 打开菜单后卡住 | 回调是否阻塞、与游戏线程互等，或在 getter 内重复初始化／扫描 |

## 维护说明

本目录的 `ModControl.h` 是公开分发副本，与仓库 `native/common/ModControl.h` 保持逐字节一致。内部实现以该源文件为唯一归属；修改 ABI 时同步公开副本和本文，不能只修改一份头文件。GUI 分组和回调行为以当前 Runtime 实现为准，不从容器 schema 推断控制 API 版本。
