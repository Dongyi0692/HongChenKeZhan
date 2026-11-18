# 菜单音乐与雨夜环境音

实现位于 `Assets/_Game/Scripts/Player/MenuAudio.cs`，挂在现有 InnInterface 根节点上，而非会被隐藏的菜单面板。菜单、序章和客栈使用同一场景及同一个雨声 AudioSource；切换界面不停止或重启雨声。战斗反馈音源保持独立。

## 音乐

- `MainMenuTheme.wav` 是用户提供的 `take-1-take-1 (4).wav` 的逐字节副本，SHA-256：`260f9c76f08b2fd82de7b1749d692ab9b714081bdbb949bdf312906bef871c47`。
- 保持 60 秒、44.1 kHz、立体声 PCM，无改编、裁剪或重新编码。
- 进入菜单约 1.8 秒淡入；两个音乐 AudioSource 通过 DSP 调度，在每轮最后 2.5 秒交叠下一轮。下一轮在 1.5 秒内升起，以覆盖原曲末尾的深度淡出。
- 点击开始立即启动 2 秒淡出，原有序章转场继续执行；淡出不受 Time.timeScale=0 影响。
- 设置里的音乐音量单独保存至 `FengChenInn.UI.musicVolume`，默认 65%。

## 屋顶雨声

- 来源：用户提供的 `669487__xkeril__rain-on-roofs.wav`，原长 54.48454 秒、48 kHz、24-bit 立体声。
- 原文件 SHA-256：`ccf637e58b37f844bd89502b8e04a3d63884db14a2559271248522aba87cc72d`。用户原文件保持不变。
- 游戏使用 `Resources/MenuAudio/RainNight.wav`，对雨声首尾做 2 秒等功率交叠，生成 52.48454 秒的连续循环副本；统一增益后保持 24-bit、48 kHz、立体声 PCM。
- 一个独立 AudioSource 使用原生 loop 连续播放，无重复实例；静音只降低音量，不停止播放。雨声贯穿主菜单、序章、室内和现有暂停/设置界面。
- 独立“雨声音量”保存至 `FengChenInn.UI.rainVolume`，默认 12%，调整时平滑变化。总音量仍共同控制所有声音。

## 验证入口

`Assets/Editor/MenuAudioVerification.cs` 提供 `MenuAudioVerification.StartTest`，也可向 `Temp/MenuAudioVerification.command` 写入 `run`。在 Stage12 中验证实际按钮、独立滑块、两次实时音乐循环、序章淡出和雨声保留，结果在 `Logs/MenuAudioVerification.txt`。

本次不修改场景、碰撞、人物或战斗逻辑，不更新旧的独立 exe 构建。雨声来源文件的许可信息沿用用户素材，本文不推断或授予额外许可。
