# NG4MOD 迁移技能

下载或复制完整的 [`ng4mod-authoring/`](ng4mod-authoring/SKILL.md) 目录交给代理，即可使用 NG4MOD v2 格式、DLL 迁移、Runtime UI 控制 ABI 和游戏资源替换打包流程。目录包含完整公开协议、ModControl.h、native-only Python 打包工具及测试，不需要 NG4 Tool 私有仓库。

支持本地技能的代理，可将目录放入自己的 skills 路径。Codex 用户可放在 `~/.codex/skills/ng4mod-authoring/`，随后在新会话中使用：

```text
使用 $ng4mod-authoring，把我的现有 Mod 转为 NG4MOD v2。
Mod 源码/资源在 <路径>，目标游戏版本是 <版本>。
DLL 有配置项时接入 Runtime UI，输出包和验证记录。
```

只有技能目录也能使用：让代理先读取 `SKILL.md`。Python 工具使用 Python 3.8+ 标准库，负责原生包；资源转换还需要用户持有的原始 parent、CSV/数据库身份与对应游戏格式转换器。技能不能替代缺失的 Mod 源码或游戏资产，不附带游戏文件，也不保证任意版本的 Hook 可用。

本目录是当前 NG4 Tool 公开协议的独立分发，不表示本仓库历史 RONIN 代码已经实现该协议。按技能随附的规范判断兼容性。

本机维护可以使用同卷硬链接；Git 和 GitHub 只保存普通文件内容，下载后各副本独立。发布内容包括技能说明、格式与 ABI 资料、打包与验证脚本。
