# 红尘客栈

Unity 可编辑工程。当前交付分支为 **`delivery/stage12-main-menu-v2`**，包含第二版主菜单、Stage12 雨夜客栈、碰撞及室内照明修复。

使用 Unity Hub 添加仓库根文件夹，编辑器版本为 **Unity 6.3 LTS（6000.3.25f1）**。打开 **`Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity`**，点击 Play，出现第二版主菜单后点击“开始游戏”。旧 `InnDuel.unity` 是历史版本，不是本次交付入口。

**下载、导入、改动清单及给伙伴 Codex 的交接提示： [交付文档](Docs/Delivery/2026-11-16/HANDOFF.md)。**

```powershell
git lfs install
git clone --branch delivery/stage12-main-menu-v2 https://github.com/Dongyi0692/HongChenKeZhan.git
cd HongChenKeZhan
git lfs pull
git lfs fsck
```

请保留完整 `Assets`（含 `.meta`）、`Packages` 和 `ProjectSettings`，不要只复制 GLB 或场景文件。首次打开会重新生成 Library 并导入模型，请等待编译完成。

项目中的大文件通过 Git LFS 保存。请安装 Git LFS 后再克隆，或在已有克隆内执行 `git lfs pull`。

当前版本包含主角、小兵、精英及客栈场景。WASD 跑步，鼠标调整视角，Space 跳跃，Shift 闪避，Q 格挡，鼠标左右键轻击与重击，F 技能，E 交互。

历史 Kevin Iglesias 动画资源受原许可约束，本仓库未包含该旧资源目录。当前 InnDuel 所用的 Aholo 动画已包含在工程中。旧场景及旧动画制作工具可能需要另行获取并导入原资产。Rocketbox、Quaternius 等第三方素材适用其随附许可，项目内素材的权利归各自权利人所有。
