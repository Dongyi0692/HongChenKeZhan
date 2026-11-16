# 红尘客栈：Codex 接手入口

当前本机整合分支为 `integrate-stage12-story`：已在现有 Stage12 上接入玩法分支、账本和新版三段流程。以根目录 `README.md` 与 `README_开始试玩.md` 的当前说明为准；下面及交付文档中“未合入玩法分支”描述的是原作者交付时状态。此整合使用官方交付归档及经哈希核验的 LFS 资源，未执行远端分支的 Git 历史合并，也未推送。

`Stage12Integration.apply` 是一次性迁移工具，已完成，不要重复执行。不要重新生成场景或启用旧 `InnStairTraversal`，Stage12 已有连续楼梯碰撞。人物/战斗动画资源沿用原玩法分支；对白单独锁定现有自然站姿。

开始工作先读 `Docs/Delivery/2026-11-16/HANDOFF.md` 和 `ProjectSettings/ProjectVersion.txt`，再检查当前 Git 分支、未提交改动以及 Unity 实际打开的场景。

- 正式名称是“红尘客栈”。历史命名空间 `FengChenInn`、资源名和 GUID 仍用于兼容，不要批量重命名。
- 本次演示入口是 `Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity`。不要把旧 `InnDuel.unity` 当成已完成的新版。
- 第二版主菜单已经接入；保留 `MainMenuV2.cs`、`InnInterface.BuildTitle()` 接入和 `Resources/MainMenuV2/asset_00..15.png` 及其 `.meta`。
- 模型、贴图、动画通过 Git LFS 保存；先确认拉取到真实二进制，再诊断 Unity 导入问题。
- 编辑前保留已有修改。场景和二进制资源不要用单边覆盖来解决合并冲突；保留 `.meta` 的 GUID。
- `combat-improvements` 是伙伴的独立玩法分支；本交付未合入它。整合时结合交付文档保留双方功能，并重新实测 Stage12。
- `Assets/Editor` 的检查工具使用项目相对 `Temp` 目录；无需 Blender MCP，也无需原作者电脑上的 D 盘目录。
- 当前交付只含 Unity 工程，不包含 Blender 源工程。不要把 Unity 验证结果描述成 Blender 源文件验证。
