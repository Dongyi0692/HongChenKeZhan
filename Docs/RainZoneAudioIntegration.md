# 室内外雨声分区与湿地脚步提升

本轮只做两件事：区分室内屋顶雨声与室外开阔雨声，并让室外湿地脚步在雨声下清楚可闻。Codex 已完成的主菜单配乐、序章、木门、战斗、UI 与结算音频保持原样，未重写 `AudioManager`，未改角色移动、动作状态机与战斗参数。

## 雨声：两条独立环境层

| 用途 | 资源 | 说明 |
| --- | --- | --- |
| 室内屋顶 | `Resources/MenuAudio/RainNight.wav` | 原素材，未改动（48 kHz 立体声，52.48 s 无缝循环） |
| 室外开阔 | `Resources/MenuAudio/OutdoorRainNight.wav` | 由 `end rain-sound.mp3` 派生的循环版本（48 kHz 立体声，81.5 s） |
| 原始素材 | `Assets/_Game/AudioSources/Rain/end rain-sound.mp3` | 逐字节保留，未覆盖 |

室外循环由 `D:\下载\红尘客栈\.task_work\rain_zone\normalize.py` 的同目录脚本链生成：裁掉 2 s 首端与 86 s 之后的尾部淡出，取 2.5 s 尾段与 0–2.5 s 首段做等功率交叉淡化后接回主体，重采样到 48 kHz，整体 +5 dB 后限幅到 -1 dBFS。生成结果与原始素材电平匹配（RMS -22.30 dB vs 室内 -22.04 dB），分窗电平在 81.5 s 内波动不超过 2.2 dB，无淡出残留；接缝处的样本跳变（-26.5 dBFS）远小于雨声自身相邻样本跳变（-0.4 dBFS），首尾无静音段。

两条层各由一个 `AudioSource` 播放，`loop = true`、`spatialBlend = 0`、常驻播放。进入/离开客栈只改音量，不 `Stop`、不重播，因此不会出现接缝、卡顿或播放位置丢失。

## 室内外判定

新增 `Assets/_Game/Scripts/Player/InnRainZone.cs`，运行时自动挂到现有 `PrototypeFighter` 上，不需要改场景。判定复用工程已有的地面分类：客栈下方的可行走碰撞体（大堂、楼梯、二楼、上层木廊）解析为 `WoodFloor`，门廊、驿路、后院为 `WetGround`。因此雨声分区与脚步换挡使用同一条边界，不需要新增触发器或修改碰撞体。

- 首次采样直接决定分区，场景开始时的雨声与角色实际位置一致。
- 进入 0.25 s、离开 0.45 s 的稳定时间，避免在门口的踏板上来回切换。
- 状态通过静态量 `InnRainZone.Outdoor`（0 = 屋顶雨，1 = 室外雨）暴露给 `MenuAudio`。

## 交叉淡化

`MenuAudio` 新增第二条雨声层，与屋顶层共用同一个总音量 `RainVolume`：等功率淡化 `cos/sin`，`roof² + outdoor²` 恒定，过渡中两层同时发声也不会让总响度变大。默认过渡 2 s（`rainTransition` 可在 Inspector 调整）。

- 标题、设置、序章保持原来的纯屋顶雨床，`MenuAudio.SetMenu(true)` 时不参与分区混合。
- 进门时室外层线性降到 0、屋顶层升到满，反之亦然；两层始终在播放。
- 门的开合仍通过 `InnDoorAudio.RainExposure` 轻微抬高雨床，逻辑未变。

## 湿地脚步

- 新素材集：`Resources/Footsteps/WetGroundLoud/`（8 个）。原始 `Footsteps/WetGround/` 逐字节保留，`HongfuFootsteps.wetFolder` 指向新集，缺失时自动回退到原集。
- 处理链：90 Hz 一阶高通 → 2 kHz 以上 +4 dB 存在感 → 按 RMS 统一到 -26.3 dB（限制在 -1 dBFS 峰值内）。8 段处理后 RMS 落在 -25.97…-26.80 dB（原始散布 -31.8…-28.9 dB），峰值统一在 -1 dBFS，无削波（flat factor 0）。响度整体比木地板高约 3.5 dB。
- `AudioSource`：`volume` .75 → .8，`minDistance` 2.5 → 4 m（跟随相机在 3.0–3.2 m，此前会被 3D 衰减压掉 1–3 dB），`maxDistance` 保持 18 m。三段独立单步发声源、非循环、无 Doppler 不变。
- 落地时单步音量 ×1.14，仅在真实下落后 0.5 s 内的第一步生效；腾空仍不触发脚步。
- 触发时机沿用原有逻辑（动画事件优先，无事件时按实际位移累计距离并与动画落脚相位对齐）。本轮核查确认：Walk/Run 均为距离触发，室外半幅输入约 1 步/秒、全速跑步频率更高，停止/腾空/对白/战斗动作均不触发。

## 验证

编辑器菜单与命令入口：

- `Assets/Editor/RainZoneVerification.cs`：写 `Temp/RainZoneVerify.command`（内容 `run`）或运行菜单项，自动进入 Play Mode，覆盖室外站立/行走、进门交叉淡化、室内行走、后院返回、室内外战斗，以及 6 次反复进出。
- `Assets/Editor/AudioRegressionTrigger.cs`：写 `Temp/AudioRegression.command`（`steps` / `menu` / `ui`）复用已有的两套验证脚本。

结果：`Logs/RainZoneVerification.txt` 40 项全过，`Logs/FootstepVerification.txt` 48 项全过，`Logs/MenuAudioVerification.txt` 19 项全过。记录归档在 `Docs/AudioVerification/rain-zone-2026-11-18.txt`。

已核实的量化结果：室外 `open = 0.120 / roof = 0.000`，室内 `roof = 0.120 / open = 0.000`，过渡峰值与最小值均为 0.120（等功率无起伏）；穿越门廊时 41 帧内 25 帧两层同时可闻；战斗前后雨床变化 0.0000；6 次进出后室外层仍只有一个实例且播放位置连续。

## 尚未覆盖

- 石阶、门槛与后院泥地仍共用同一套湿地脚步录音，没有按材质细分。
- 雨声没有随室内体积、遮蔽程度做 3D 化或混响处理，仍是双声道 2D 层。
- 若将来给走路动画加 Animation Events，脚步会优先走事件分支，雨声分区逻辑不受影响。
