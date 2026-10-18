# 现有 DLL 的迁移

先读 [完整格式](ng4mod-v2/README.md)的 Native libraries 与生命周期合同。控制界面按 [Runtime 接入](ng4mod-v2/RUNTIME_INTEGRATION.md)实现。

## 分离加载器和玩法

1. 检查源码／PE 导出与依赖：普通插件可直接作为入口；`winmm.dll`、`dinput8.dll`、`version.dll` 等代理若混有系统转发与玩法，应从源码构建独立命名的玩法 DLL，删除其系统代理安装职责。不能假设仅改文件名就会初始化成功。
2. 保留现有 Hook 行为和配置，入口在 loader lock 外完成初始化。推荐导出 `extern "C" __declspec(dllexport) DWORD WINAPI Ng4ModInitialize(DWORD timeout_ms)`；全部必需初始化成功才返回 0。DllMain 不等待工作线程。
3. 核对宿主与已审查指令签名；早期启动代码页可能尚未解密，在 `timeout_ms` 内有界等待，超时失败。只验证有效指令，不把函数尾填充当签名；遇到已被其他 Mod 修改的入口，需证明兼容性，不能盲目覆盖。
4. 入口 `load=true`，同目录私有依赖 `load=false`。不打包 Windows 系统 DLL、管理器代理或 Runtime。依赖按实际 PE import／动态加载路径核对，不从开发机 PATH 推断用户一定存在。
5. 稳定 ID 区分不同产品；同一不可共存 Hook 的不同策略共享同一个已约定的 claim，不能通过换 token 绕过互斥。claim 区分大小写；只有入口可以声明。token 并不能自动检测未声明的 Hook 冲突。
6. 从 DLL 自身 HMODULE 定位私有目录，不依赖当前工作目录。UTF-8 中文 INI 由 DLL 首次创建，并在运行期独占写入；详情按 Runtime 指南。源 Mod 使用不同配置协议时，把迁移作为明确的一次性数据转换处理，保留原文件，不静默覆盖作者／用户配置。

## 随附 native-only 打包工具

`scripts/pack_native.py` 接收如下 JSON；`source` 相对于 spec 所在目录，包中的 `name` 必须是纯文件名：

```json
{
  "mod_id": "example.my-mod",
  "name": "我的模组",
  "version": "1.0.0",
  "author": "作者",
  "description": "功能与兼容游戏版本",
  "dependencies": [],
  "native_libraries": [
    {
      "source": "build/my-mod.dll",
      "name": "my-mod.dll",
      "load": true,
      "exclusive_claims": ["example.my-hook.v1"]
    },
    {
      "source": "build/my-helper.dll",
      "name": "my-helper.dll",
      "load": false
    }
  ]
}
```

没有私有依赖就移除第二项；没有独占所有权就省略 `exclusive_claims`，不要把示例 token 用于真实冲突检测。数组顺序就是同包入口的加载顺序。

工具只产出无封面的 native-only v2，检查基础 PE x64/DLL 标记、名字、数量、claims 与源文件哈希；不能证明 PE 的完整可加载性、导出、依赖可解析性或玩法正确性。输出必须是新路径，拒绝覆盖。它不是通用 v2 reader 或安装器；最终包仍须交给完整格式验证器／管理器验证，不加载未知 DLL 来测试。

## UI 与加载故障定位

- “已安装未加载”：检查启用、load、加载器日志、初始化返回值和是否重启游戏。
- “已加载未登记”：自己的加载器可能正常运行了 DLL，但持久安装应通过管理器导入包；不要手改 `ng4-native-plugins.ini`，管理器会重建它。
- “无控制界面”：检查 `Ng4ModQueryControl` 的未修饰导出、ABI 版本与尺寸、三个必需回调。单纯有 DLL 载荷不意味着有控制 ABI。
- “READY 但资源没生效”：检查初始化队列与首次读取时机；后序提供器可能错过首次解析，UI 状态不能证明命中。
- “GUI 改了又回跳”：检查保存结果、配置快照和 getter；实时对象修改需要安全事件，不用强制返回 OK 掩盖失败。

Runtime 当前通过稳定 ID 前缀做有限分组，具体前缀见接入指南；自定义布局或控件需要扩展 Runtime，ABI v1 不支持任意绘制回调。
