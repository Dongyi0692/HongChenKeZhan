# 红尘客栈：Codex 接手入口

先读 `Docs/Delivery/2026-11-18/HANDOFF.md`、`README_开始试玩.md`、`ProjectSettings/ProjectVersion.txt`，再检查当前 Git 分支、未提交改动和 Unity 打开的场景。2026-11-16 交付文件为历史归档，当前代码与新交接文件优先。

- 正式名称是“红尘客栈”。历史命名空间 `FengChenInn`、资源名和 GUID 仍用于兼容，勿批量重命名。
- 使用 Unity 6000.3.25f1；正式场景为 `Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity`。不要把旧 `InnDuel.unity` 当成当前版本。
- 用户的本版场景优化优先。旧 `Entrance exterior ground` 入口平台已移除，不要从旧分支、备份或恢复场景加回。场景冲突逐项核对，不要整场覆盖；保留 `.meta` GUID。
- `Stage12Integration.apply` 已执行，勿重复；勿重新生成 Stage12 或启用旧 `InnStairTraversal`。保留平滑楼梯碰撞及现有角色、战斗、门和剧情交互。
- 主菜单、序章、雨区、脚步、战斗、UI 和胜利 B 的现有接入及原始素材路径见新交接文件。不要用旧验证报告代替修改后的 Play Mode 验证。
- Unity 模型、图片和音频使用 Git LFS；先拉取真实二进制再诊断导入。不要提交 `Library`、`Temp`、`Builds`、`Backups` 或 `Assets/_Recovery`。
- 当前交付是 Unity 工程，不包含 Blender 源工程；不要把 Unity 验证描述成 Blender 源文件验证。