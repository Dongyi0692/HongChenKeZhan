# 红尘客栈 Unity 工程交付 · 2026-11-16

## 先看这几项

| 项目 | 当前交付 |
| --- | --- |
| 仓库 | https://github.com/Dongyi0692/HongChenKeZhan |
| 交付分支 | `delivery/stage12-main-menu-v2` |
| Unity | **6000.3.25f1 / Unity 6.3 LTS** |
| 演示与构建入口 | `Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity` |
| 渲染管线 | URP 17.3.0 |
| GLB 导入 | `com.unity.cloud.gltfast` 6.20.0，由 manifest 自动恢复 |
| 输入系统 | Input System 1.20.0 |
| 范围 | 完整 Unity 工程；按作者选择不上传 Blender 源文件、缓存、旧备份或源素材 ZIP |

本交付基于原 `main` 的 `77f4463`，未合入伙伴的 `combat-improvements`。下载时必须选交付分支；在本分支被合并之前，仅下载 `main` 看不到这些成果。

合并请求：[PR #1](https://github.com/Dongyi0692/HongChenKeZhan/pull/1)。已在独立目录从 GitHub 完整克隆并运行 Git LFS 校验，104 个 LFS 路径取回成功；本次新增的 20 个二进制资源与 `asset-checksums.json` 全部一致。详情见 `evidence/github-download-verification.txt`。独立克隆未再次启动 Unity 或构建可执行程序。

## 这次完成了什么

### 场景、模型与碰撞

- 导入 Stage11 最终客栈与 Stage9 暗门预览模型，并保留相关场景。Stage12 是实际可试玩的修复版本，包含室内、建筑、庭院、雨夜、角色及既有玩法。
- 新版客栈原来显示新模型，却沿用旧场景碰撞；Stage12 停用旧模型碰撞，依据当前可见实体补齐碰撞。
- 正门的门扇、门轴和碰撞同步开关；`InnEntranceDoors.rebuildImportedDoorCollision` 控制新修复行为，避免旧场景默认启用新对齐逻辑。
- 楼梯增加平滑行走面，保留可见踏步与栏杆。人物胶囊的实测配置为高 1.8 米、半径 0.36 米、跨步高度 0.3 米。
- 大堂、柜台、楼梯和回廊补充暖黄色照明，室外保留冷色雨夜。
- 布帘、旗帜和细绳等装饰不作为人物障碍。后方暗门有关闭状态阻挡，本次未新增暗门交互控制。

### 作者完成的第二版主菜单

- 16 张分层 PNG 已导入 `Assets/_Game/Resources/MainMenuV2/`，不需要再次导入原 ZIP。
- `MainMenuV2.cs` 实现背景与文字布局、分批淡入、雨丝、灯笼呼吸光、按钮悬停/按下反馈及点击淡出。
- `InnInterface.BuildTitle()` 接入新菜单，复用已有“开始游戏 / 设置 / 退出游戏”逻辑。
- `ActionHud.cs` 支持 Stage12 和 Stage11 场景进入完整界面。
- 实际实现使用 **RawImage + Texture2D**，并非必须将图片重新设置成 Sprite。保持现有导入配置：关闭 Mip Maps、保留 Alpha、不缩放非二次幂尺寸、无压缩、最大 2048。
- 菜单以 1672×941 为布局基准，等比缩放并用黑边适配不同比例。人物与发带保持静态，雨丝和灯光使用不受暂停影响的时间。
- 正式名称为“红尘客栈”；旧代码命名空间和部分历史资源名仍是 `FengChenInn`，不要仅为改名破坏引用。图片内已经烘焙的文字属于作者现有美术，本次上传未修改。

### 为跨电脑交付做的整理

- 构建入口改为 Stage12，避免 Build 后跑进旧 `InnDuel`。
- 两个编辑器检查工具移除作者本机绝对输出路径，改用项目内 `Temp`。
- 保留全部资源 `.meta`、Packages 依赖锁定和 ProjectSettings。大模型与图片通过已有 Git LFS 规则保存。
- `Library / Temp / Logs / UserSettings / .vs / .slnx` 等机器生成文件不进入仓库。

## 伙伴首次下载到电脑

先安装 Git、Git LFS、Unity Hub 和指定版本 Unity。Windows 构建时安装对应 Windows Build Support；仅编辑试玩不需要额外平台模块。

在准备存放工程的文件夹打开终端：

```powershell
git lfs install
git clone --branch delivery/stage12-main-menu-v2 https://github.com/Dongyi0692/HongChenKeZhan.git
cd HongChenKeZhan
git lfs pull
git lfs fsck
git status --short
```

不要依赖 GitHub “Download ZIP”来获取 LFS 模型：必须确认拿到完整二进制，而不是一百多字节的 LFS 指针文件。`git lfs pull` 成功后再启动 Unity。磁盘需要同时容纳 Git/LFS、本体资源和 Unity 生成的 Library，请留足空间。

在 Unity Hub 选择 **Add / 从磁盘添加**，选中包含 `Assets`、`Packages`、`ProjectSettings` 的仓库根目录。指定 **6000.3.25f1**，等待 Package Manager、GLB/贴图导入及脚本编译完成。

打开 `Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity`，点击 Play。正常顺序为第二版菜单 → 开始游戏 → 现有教学/剧情流程 → 客栈游戏场景。设置按钮复用原面板，编辑器中的退出按钮停止 Play Mode。

如果只是继续开发，不要把整个仓库目录再复制到另一个 Unity 工程的 Assets 下。添加现有工程即可。也不要只拷贝 `.unity`，否则其 GUID 引用的模型、脚本和材质会丢失。

## 已有旧克隆的伙伴

先保存 Unity 场景并停止 Play Mode，检查 `git status`。有自己的修改时先提交到自己的分支或另行备份，不要执行 `reset --hard` 或覆盖文件。

```powershell
git fetch origin
git switch --track origin/delivery/stage12-main-menu-v2
git lfs pull
git lfs fsck
```

如果本地已经有同名分支，改用 `git switch delivery/stage12-main-menu-v2`，然后 `git pull --ff-only`。如果需要保留当前工作目录不动，可以新建一个独立克隆。

## 给玩法分支的整合说明

检查时 `combat-improvements` 已包含战斗目标、反馈、重试检查点、楼梯防卡等改进；这些**不在本交付分支**。需要完整集成时，在自己的整合分支合并两个分支，不要整份覆盖 `InnInterface.cs`。

重点保留：

1. 本分支 `BuildTitle()` 中 `MainMenuV2` 的接入、Stage12 的 `ActionHud` 支持和名称修正。
2. 对方 `InnInterface` 的战斗反馈、重试与目标指示等改进。
3. 对方对 `InnDuel.unity` 的改动不会自动移植到 Stage12。检查玩家、敌人、相机及剧情组件参数，将需要的配置迁移到 Stage12 后再次测试。
4. 对方楼梯辅助脚本和本分支的斜坡/网格碰撞可能相互影响；不要仅以“无 Git 冲突”判断集成成功。
5. `.unity`、GLB 和 `.meta` 保持引用一致。场景冲突应在 Unity 中核对，二进制资源不要盲目选 ours/theirs。

## 资源与关键文件

| 路径 | 用途 |
| --- | --- |
| `Assets/_Game/Art/Stage11Final/Stage11_Final.glb` | 新版场景使用的客栈模型，约 166 MiB |
| `Assets/_Game/Art/Stage9HiddenDoor/Stage9_HiddenDoor.glb` | 历史暗门预览模型，约 186 MiB |
| `Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity` | 当前试玩和构建入口 |
| `Assets/_Game/Scenes/InnDuel_Stage11.unity` | 修复前版本，保留作对照 |
| `Assets/_Game/Scenes/Stage9_HiddenDoor_Preview.unity` | 历史预览，不作默认游戏入口 |
| `Assets/_Game/Resources/MainMenuV2/asset_00.png` 至 `asset_15.png` | 已接入的菜单背景、文字、装饰、普通/悬停按钮 |
| `Assets/_Game/Scripts/Player/MainMenuV2.cs` | 新菜单表现 |
| `Assets/_Game/Scripts/Player/InnInterface.cs` | 菜单、设置与既有玩法流程 |
| `Assets/_Game/Scripts/Player/InnEntranceDoors.cs` | 正门与门扇碰撞 |
| `Assets/Editor/HongchenDeliveryTools.cs` | 检查缺脚本、菜单资源数量和构建入口 |
| `Assets/Editor/HongchenSceneRepair.cs` | 本轮修复与碰撞验证工具，不是运行游戏的依赖 |
| `Assets/Editor/MainMenuV2Tools.cs` | 菜单检查与截图工具 |

`Stage11PlayableSceneBuilder` / `Stage11WorldSetup` 是历史生成工具；正常接手打开现成 Stage12 即可，不需要重新生成旧场景。GLB 中的坐标已由 Unity 导入处理，不要再手动做 Blender Z-up/Y-up 转换。

## 验证结果与边界

`evidence/` 包含实际 Unity 相机截图、作者主菜单运行截图和本轮碰撞测试日志。

- 正门关闭阻挡 / 打开通行通过。
- 读取当前人物胶囊及重力参数进行每秒 60 帧的移动模拟：楼梯左、中、右上下行 6 项，以及柜台、桌子、东墙、后门跑动阻挡 4 项，合计 **10 项通过**。
- 可见实体模型检查：1279 个实体网格有有效碰撞，18 个装饰网格有意排除，未发现其余遗漏。
- 菜单接入记录：曾实际检查设置/返回、开始游戏、退出 Play Mode及 1920×1080 画面；本次仅打包其现有实现，不重新设计菜单。
- 这些结果不是对所有跳跃、闪避、击退、相机角度和低配帧率的穷举验证。高面数角色与场景仍有性能优化空间。
- 本次没有交付 Windows 可执行安装包；这是可编辑 Unity 工程。跨操作系统的中文动态字体可能需要另行提供可分发的字体；Windows 开发机使用现有系统字体回退。
- 主菜单接入记录提到已有的停止 Play Mode 时 PlayableGraph 警告；不要混淆为本次菜单资源丢失。网络包下载/授权问题需要按实际报错处理。

验收时打开 `Tools → 红尘客栈 → 交付 → 检查当前场景`。报告在 `Temp/HongchenDelivery/status.txt`，预期场景是 Stage12、`missingScripts=0`、`menuTextures=16/16`、构建场景为 Stage12。这些报告属于本机临时输出，不提交。

## 常见问题

- **只有旧菜单/旧模型**：确认分支与打开的场景；Unity 不会因切换分支自动打开 Stage12。
- **GLB 导入失败或资源文件只有几行文本**：关闭 Unity，执行 `git lfs pull`，确认 LFS 真实数据下载完成后重新打开。
- **模型粉色**：检查 URP 包是否恢复、项目渲染配置是否完整，别只把 Assets 复制进空白 Built-in 工程。
- **中文设置面板方块字**：安装项目字体回退列表支持的系统字体，或团队后续提供可分发字体资源。
- **菜单文字或按钮重叠**：确认没有再次导入整张带 UI 的预览图叠加在 16 张分层图上。
- **构建仍进旧场景**：打开 Stage12，运行 `Tools → 红尘客栈 → 交付 → 将 Stage12 设为构建入口`，核对 Build Profiles 的 Scene List。

## 可以直接发给伙伴 Codex 的提示词

> 请先阅读仓库根目录 AGENTS.md 和 Docs/Delivery/2026-11-16/HANDOFF.md，检查当前分支、git status、Git LFS 文件以及 Unity 版本。当前交付分支是 delivery/stage12-main-menu-v2，入口为 Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity。保留已完成的第二版主菜单、Stage12 碰撞与暖色室内照明，保留所有 .meta 的 GUID，不覆盖我的未提交改动。如果整合 combat-improvements，请同时保留其战斗/重试/目标反馈，注意它修改的 InnDuel.unity 不是 Stage12；在新整合分支迁移配置，并实际运行检查菜单、设置、开始、门开关、楼梯上下行和战斗。不要把尚未运行的检查描述成已经通过。

原有第三方资源许可继续适用，详见仓库 README 与各资源随附许可。此次提交不改变资源许可，也未包含 Blender 可编辑源文件。
