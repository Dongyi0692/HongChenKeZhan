# 给 WorkBuddy 的《红尘客栈》GitHub 上传交接

## 用户委托

请将这台电脑上已经优化的《红尘客栈》Unity 工程上传到 <https://github.com/Dongyi0692/HongChenKeZhan.git>，并让共创者的 Codex 能从仓库读到这份交接。用户明确要求：与远端旧版本发生冲突时，出生点平台、正式场景和其他由用户优化过的部分以**这台电脑上的当前版本**为准。不要把已移除的入口平台从旧分支带回来。用户已授权上传；这份文件是给 WorkBuddy 的执行说明，**截至写作时尚未提交或上传**。

## 当前工程状态（2026-11-19）

- 工程根目录：`D:\下载\红尘客栈\HongChenKeZhan-v6`，包含 `Assets`、`Packages`、`ProjectSettings`。
- Unity 版本：`6000.3.25f1`。正式场景：`Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity`。不要改用旧 `InnDuel.unity`、`InnDuel_Stage11.unity` 或 `ActionLab.unity`。
- `origin` 的 fetch/push 地址均为上方 GitHub 仓库。当前分支 `integrate/stage12-audio-v6`，本地 HEAD `7ddcd9f`，当前工作树有**尚未提交**的修改和新增文件。需要先检查实时远端状态；本地 `main` 当前显示落后 `origin/main` 8 个提交，不能直接用本地旧 `main` 覆盖远端。
- `.gitattributes` 已为 WAV、PNG、MP3、FBX 等二进制素材配置 Git LFS。上传时必须同时上传 LFS 对象和对应 `.meta`。
- 旧交接见 `AGENTS.md`、`Docs/Delivery/2026-11-18/HANDOFF.md`、`README_开始试玩.md`。关于本轮终幕与胜利页，以 `Docs/终幕演出_2026-11-19/交付报告.md` 和当前源码为准；旧文件中的“终幕自动继续”描述已经过时。

## 应纳入本轮交付的内容

工作树中已有但尚未提交的改动需要逐项检查并纳入同一次交付，不能只提交最后一次 UI 修复：

1. `Assets/_Game/Scripts/Player/InnInterface.cs`、`UiVictoryAudio.cs`、`InnStoryScript.cs`：终幕点击转场、胜利 B 循环、胜利结算按钮修复、AI 标注清理。终幕标题居中并常驻，点击后随背景淡黑。胜利页“再玩一次”“回到标题”在入场鼠标松开后保持可点击。
2. `Assets/_Game/Resources/UIArt/EpilogueBackdrop.png` 和 `.meta`：用户提供的终幕无字原图。保留原图，不替换为旧的带字图。
3. `Assets/_Game/Editor/Stage12StoryPlaythrough.cs`、`PrototypeSetup.cs`、`Assets/Editor/UiArtCapture.cs` 及其 `.meta`：编辑器验证和旧原型标注清理。
4. `Assets/_Game/Scripts/Player/CombatAudioSystem.cs`、`RescueCoverAudio.cs` 及 `.meta`、`Assets/_Game/AudioSources/RescueCover/`、`Assets/_Game/Resources/RescueCoverAudio/`：先前已完成的战斗声和后门外掩护声，原始与处理素材都要保留。说明见 `Docs/CombatAudioIntegration.md`、`Docs/RescueCoverAudioIntegration.md`。
5. `Docs/终幕演出_2026-11-19/`、`Docs/UI正式版优化_2026-11-18/`、`Docs/UI界面截图_2026-11-18/`、`Docs/UI界面清单_2026-11-18.md`、`Docs/AudioPreviews/`、已有音频验证与交接文档；以及当前工作树中已修改的 `AGENTS.md`、`ProjectSettings` 文件。对每项先看差异，保留当前工程所需设置和引用，不把看起来“无关”的已有工作静默丢掉。
6. **本文件本身也必须提交**，以便共创者的 Codex 在 GitHub 上读取。

工作树以执行时 `git status --short` 为最终清单；上面是写作时的分类，不是排他白名单。每个新 Unity 资源都检查对应 `.meta`，不要重新生成 GUID。

## 已完成的验证与限制

- 正式 Stage12 场景的 Unity Play Mode 自动流程在 `Logs/Stage12Story_20261119_120250/report.json`：**56 项通过、0 个运行错误**；终幕停留约 19.02 秒，标题仍在；“再玩一次”和“回到标题”均通过 Input System 鼠标按下/松开事件及实际 UI 射线触发。此流程通过测试夹具推进部分战斗结果，不等同玩家手动通关。
- 可随仓库交付的 Game 视图截图在 `Docs/终幕演出_2026-11-19/After/`：`10_终幕常驻标题.png`、`11_胜利结算.png`、`12_标题唯一AI标注.png`。主菜单只保留左下角那一处 AI 标注；终幕、胜利、失败页及旧原型 UI 的其他标注已清理。
- `Logs/`、`Backups/` 被 `.gitignore` 排除。日志和本地备份留在用户电脑，不要强制提交；远端共创者请读上面的报告、截图和本交接中的验证摘要。音频循环的主观听感及玩家手动完整通关仍应在交付后复核。

## 建议的上传顺序

1. 进入**上述工程根目录**，先记录 `git status -sb`、`git diff --check`、`git remote -v`、`git lfs status`。检查尚未跟踪的目录及大小。不要在存在未提交修改时执行 `reset --hard`、`clean -fd`、强制推送或整场景覆盖。
2. 将当前工作树作为用户版本保存为提交，包含该交接文件及所有应交付的源码、资源、`.meta` 和文档。先检查暂存清单，确保没有 `Library/`、`Temp/`、`Logs/`、`Backups/`、`Builds/`、`Assets/_Recovery/` 或机器生成的解决方案。运行 `git lfs install`，确认大文件走 LFS；提交后检查 `git status` 和 `git lfs status`。
3. `git fetch origin` 后比较当前分支与远端同名分支、`origin/main` 的提交差异。若远端有共创者新提交，先在本地合并或变基并解决冲突；保留本机优化过的正式 Stage12 场景、入口/出生点、门、镜头、UI 和音频，逐项吸收共创者独立改进。不要用 `ours` 或 `theirs` 对整个工程一键覆盖。特别核对旧 `Entrance exterior ground` 平台**仍不存在**。
4. 用 Unity `6000.3.25f1` 打开正式场景，至少确认脚本编译、资源引用、主菜单→序章→游戏→终幕→胜利页，以及两个结算按钮能响应。冲突若涉及玩法或场景，扩大 Play Mode 复测。`git lfs fsck` 确认素材对象完整。
5. 先把集成提交推送到远端 `integrate/stage12-audio-v6`；再按仓库权限将其合入 `main`（可用 PR），确保 `main` 含当前交付且没有覆盖共创者新内容。不要强推。若权限、LFS 上传或冲突阻止合入，请保留已推送的集成分支并明确报告尚未完成 `main` 更新，不能声称已全部上传。
6. 回报用户：实际推送的分支和提交哈希、是否已进入 `main`、GitHub/PR 链接、LFS 校验、Unity 验证结果、任何未解决冲突。

此文件记录的是用户的交付意图和当前工程事实。WorkBuddy 应以执行时的真实 Git/Unity 状态为准，不把本文件当作替代验证的凭据。
