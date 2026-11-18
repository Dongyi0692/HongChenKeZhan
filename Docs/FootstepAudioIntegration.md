# 红拂女脚步音效

入口仍为 `Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity`。运行时自动为现有 `PrototypeFighter` 添加独立 `HongfuFootsteps`，不用重新保存/生成场景。角色控制器、输入、动画资源、战斗和背景音乐文件不做修改。

## 素材

- 原录音：`Assets/_Game/AudioSources/Footsteps/`，逐字节保留用户的 WAV 与 AIFF。
- 单步：`Assets/_Game/Resources/Footsteps/WetGround/` 和 `WoodFloor/`，各 8 个；另有提升版 `WetGroundLoud/` 8 个，供室外使用（见 `Docs/RainZoneAudioIntegration.md`）。
- 24-bit PCM WAV，湿地维持 44100 Hz，木地板维持 48000 Hz；转单声道用于 3D 定位。Unity 预解压 PCM，不循环，不对原文件做转码覆盖。
- 从连续录音的清晰单步段提取，保留约 8 ms 起音余量和完整可用尾音；去直流、45 Hz 一阶高通减轻低频底噪，按响亮的 50 ms 窗口匹配增益并限制峰值到 -3.35 dBFS。仅边界使用 2 ms / 35 ms 防点击渐变，无硬噪声门。
- 逐段源时间、增益、峰值、原文件 SHA256 见 `Docs/AudioVerification/footstep-segments.json`。

## 地面和声音

`FootstepSurface` 将材质分为 `WetGround` / `WoodFloor`。在角色脚下向下检测真实碰撞体，排除自身、触发器和陡壁；复用控制器的 `IsGrounded`，不依赖可能被战斗水平位移覆盖的 `CharacterController.isGrounded`。

已核实 Stage12 的 `EnvGround_*`、室外石阶和露天门廊使用湿地；`GroundFloor_slab/plank`、二楼地板和楼梯的可见/平滑碰撞面使用木板。新关卡可以在碰撞体或其父物体上添加 `FootstepSurface` 明确指定；未标记且不在现有木板名单内的表面默认湿地。石阶目前没有专用录音。

独立 3 个单发声源保留相邻步的自然尾音，全部为 3D、非循环、无 Doppler。默认音量 .8，距离 4–18 m 线性衰减（跟随相机 3–3.2 m 始终落在近场内，本地脚步不再被 3D 衰减压掉）；音高 ±4.5%，同一地面不连续选择同一个片段。组件 Inspector 可调音量、音高变化和衰减距离；不改变雨声和音乐音量。

## 时机与 Animation Events

现有 Walk / Run clips 均无 Animation Events。实际红拂女控制器在地面移动时播放 Run（0.6667 秒），其播放速率随实际移动速度调整。Walk 为 1.3333 秒，但现有键盘没有慢走切换键。此次保留该行为，不新增按键或修改移动速度。

无事件时，按实际水平位移累计距离；每步距离根据实际速度 × 动画长度 ÷ (2 × 动画播放速率) 校准，并复用控制器已有的 0.35–1.5 播放速率限制。采样左右脚后，首次触发对齐到当前动画的半周期落脚相位。当前 Run 参考速度为 7.7123 m/s，全速 4.2 m/s 对应约 1.63 步/秒，半幅输入 2.1 m/s 对应约 1.05 步/秒（不含碰撞或落地过渡造成的停步）。速度变化保留当前步的累计比例；现有角色在障碍/门槛处切换落地动画或重置动画时钟后，重新对齐实际落脚相位，不在落地动作中追加普通走路声。不会因为仅按住方向键而在碰墙时持续播放。

停止、失去有效位移、跳跃腾空、暂停、对白锁定或进入战斗动作时不再触发，残余单步在 40 ms 内淡出。传送不累计成脚步，不积压补播。

将来如给移动动画添加事件，在 Animator 所在的 `Hongfu_Aholo_Model` 上由自动添加的 `FootstepAnimationEvents` 接收：左右脚实际接地帧分别绑定 `FootstepLeft` / `FootstepRight`（也支持 `Footstep` / `OnFootstep`）。检测到这类事件时优先使用事件并关闭该动画的距离触发，避免双播。只绑定移动动画，勿绑定攻击片段。修改事件后重新进入 Play Mode。

## 验证与范围

编辑器菜单：`Tools/红尘客栈/音频/验证红拂女脚步 (Play Mode)`。测试只在临时 Play Mode 状态下注入方向/输入强度，不保存场景或改写角色参数；结束恢复主菜单。慢走采用半幅已有 `MoveInput`，不是验证一个不存在的键盘慢走键。

最终 Play Mode 结果见 `Docs/AudioVerification/footsteps-2026-11-18.txt`。动画/碰撞采样记录见 `footstep-scene-audit.txt`。

当前两类普通移动音效齐全；跑步复用同类单步并提高触发频率。尚无专用跳跃落地、石阶/石板、其他地面，以及独立的重跑/拖步录音。这些未以整段循环或不匹配的音效冒充；空中保持静音。


