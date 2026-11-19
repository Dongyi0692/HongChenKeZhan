# 红尘客栈：2026-11-18 共创交接

## 接手顺序

1. 阅读仓库根目录 `AGENTS.md`、本文件、`README_开始试玩.md`、`ProjectSettings/ProjectVersion.txt`。
2. 检查当前分支、未提交改动、Unity 实际打开的场景，再编辑。
3. 本文件记录当前可编辑工程；2026-11-16 的交付文件和各音频测试记录是历史与验证依据。发生描述冲突时，以当前提交的场景、脚本和本文件为准。

| 项目 | 当前交付 |
| --- | --- |
| 仓库 | https://github.com/Dongyi0692/HongChenKeZhan |
| 默认交付分支 | `main`；`integrate-stage12-story` 同步交付 |
| Unity | 6000.3.25f1（Unity 6.3 LTS） |
| 正式入口场景 | `Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity` |
| 范围 | 可编辑 Unity 项目、音频原始素材和处理版本、脚本、.meta、项目设置和验证记录 |
| 非交付物 | `Builds/`、`Library/`、`Temp/`、`Backups/`、`Assets/_Recovery/` 与机器生成的解决方案文件 |

## 当前版本优先的合并规则

当前提交是用户亲自优化后的版本。正式入口场景中旧的 `Entrance exterior ground`（入口室外地面平台）已移除；不要从旧 `main`、`combat-improvements`、`delivery/stage12-main-menu-v2`、备份或恢复场景把它还原。涉及出生点平台、入口碰撞、场景物体、当前镜头、门、剧情和音频的冲突，先保留本版本的实际场景和脚本，再逐项移植共创者的独立改进，并在 Unity 里验证。不要用整个旧场景覆盖当前场景。保留所有现有 `.meta` GUID。

旧交付分支的文字仍有“玩法分支未合入”等历史表述，不适用于当前整合工程。不要再次运行一次性 `Stage12Integration.apply`、重新生成 Stage12 场景，或启用旧 `InnStairTraversal`；Stage12 已有连续楼梯碰撞。历史命名空间 `FengChenInn` 仍供引用，勿为统一名称批量改名。角色战斗参数与玩法以当前脚本为准。

## 功能与素材位置

- 菜单、序章和雨声：`Assets/_Game/Scripts/Player/MenuAudio.cs`，处理后资源在 `Assets/_Game/Resources/MenuAudio/`。当前音乐在菜单和序章均播放；室外和室内雨声过渡还见 `InnRainZone.cs` 与 `Docs/RainZoneAudioIntegration.md`。
- 木门：`InnEntranceDoors.cs`、`InnRearExitDoor.cs`、`InnDoorAudio.cs`；动作和音效的对应说明见 `Docs/DoorAudioIntegration.md`。
- 脚步：`HongfuFootsteps.cs`、`FootstepSurface.cs`、`FootstepAnimationEvents.cs`；`WetGround` 和 `WoodFloor` 的处理片段在 `Assets/_Game/Resources/Footsteps/`。
- 战斗音效：`CombatAudioSystem.cs`、`CombatAudioEmitter.cs`、`WeaponDropAudio.cs` 和 `Resources/CombatAudio/`。武器落地片段已制备；如新增真正的脱手/落地动作，应接在实际事件上，不要凭固定计时器或无画面的归鞘动作播放。
- 后门外屋内掩护声：`RescueCoverAudio.cs` 复用木地板单步素材，随后在真实交手开始时淡入用户选定的 `SwordBattle2.wav` 远处录音。原件、处理版和 CC0 来源见 `Docs/RescueCoverAudioIntegration.md`；不替代真实攻击、命中、格挡事件。
- UI、序章翻页和胜利 B：`UiVictoryAudio.cs`、`StoryIntroPanel.cs`、`Assets/Audio/UI/`、`Resources/UiVictory/`。不要把早期测试报告当成对所有后续代码改动的复测。
- 用户提供的连续原始录音及 UI、胜利原素材保存在 `Assets/_Game/AudioSources/`；处理后版本另存，不要覆盖原文件。对应说明见 `Docs/*AudioIntegration.md`，测试记录见 `Docs/AudioVerification/`。

## 获取和运行

```powershell
git lfs install
git clone https://github.com/Dongyi0692/HongChenKeZhan.git
cd HongChenKeZhan
git lfs pull
git lfs fsck
git status --short
```

在 Unity Hub 添加克隆后的仓库根目录（含 `Assets`、`Packages`、`ProjectSettings`），用 6000.3.25f1 打开正式入口场景并等待资源导入。先确认 LFS 取回实际音频、模型和图片，再检查脚本编译、缺失引用、主菜单 → 序章 → 客栈 → 战斗 → 胜利 → 后院的 Play Mode 流程。详细操作见 `README_开始试玩.md`。

## 本轮已有验证和后续检查

2026-11-18 的 `Docs/AudioVerification/` 中有菜单、雨区、脚步、战斗、UI 胜利和结算的 Play Mode 检查记录，记录显示各自测试完成且无运行错误。它们是当时版本的证据；后续修改场景或脚本后要重新测受影响流程。交付前状态记录显示 Unity 6000.3.25f1、正式场景、缺失脚本 0、菜单贴图 16/16、非 Play Mode。当前源码上传不等于重新制作 Windows 试玩包；需要新版 exe 时单独构建和验证。
