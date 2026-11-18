# UI 与胜利结算声音（2026-11-18）

## 来源和资源

- `Assets/_Game/AudioSources/UiVictory/` 保留用户原件：`vadim_makes_sound-soft-app-button-tap-sound-2-547872.mp3`、`chisper-ui-click-wooden-634791.mp3`、`page-turn-1.wav`、`Victory_B_Softer_100-116s.wav`、`button-brush-past.mp3`。原件未修改。
- `Assets/Audio/UI/` 放置可供编辑的处理版 WAV；运行时等价文件放置于 `Assets/_Game/Resources/UiVictory/`，以 PCM 预解码导入。
- UI 悬停改用用户后补的 `按键拂过音.mp3`（96 ms 单声道）：掐掉首尾约 18 ms 底噪，120 Hz 高通去闷，边界 3 ms / 8 ms 渐变，峰值归一到 -1.3 dBFS，输出 48 kHz 24-bit 立体声，`UI_Hover_Brush.wav`。电平与旧悬停基本对齐（旧峰值 0 dBFS、时长 130 ms），因此在同一“悬停”音量下听感不会突然变小。旧的 `UI_Hover_Soft.wav` 仍保留在工程内，代码里有回退分支，可随时换回。
- 木质确认取原 MP3 0–0.20 秒；翻页取原 WAV 0–0.38 秒并增加 14 dB（原峰值约 -23.9 dBFS）。处理版均使用 24-bit PCM 与边界短渐变。胜利音乐 B 为原始 16 秒立体声 WAV，逐字节复制，没有裁切、增益改写或循环。

## 触发和设置

- 现有主菜单、设置及序章按钮通过 `UiButtonSound` 监听真实指针进入和 `Button.onClick`。悬停全局冷却 0.11 秒，选中、按住或移动过程不会不断触发。确认使用木质声音。
- 对话框界面不做悬停声：对话框只有“E 继续”一个按钮，指针掠过正文/按钮不再发声（点击确认声保留）。实现方式是 `UiButtonSound.hoverEnabled` 开关 + `InnInterface.Button(..., hoverSound: false)`，其他按钮仍走 `Bind(button)` 默认值保持原行为。
- 序章六段文字自动逐段显示时，第二至第六段各触发一次翻页声；初始第一段不翻页。提前点“继续”直接揭开全部文字时不制造五次虚假的翻页声。
- 账本界面（`View.Ledger`）原本没有任何翻页声。现改为：按 E 翻开账本时触发一次、按 E / 「读完，合上」/ Esc 合上时再触发一次；只在真实交互时播放，指针悬停或走近不发声。账本使用用户本次提供的 `page-turn-1.wav` 完整 0.59 秒录音（+14 dB、48 kHz 24-bit、边界渐变），对白仍用它的 0.38 秒短剪版，二者电平一致（峰值 -9.9 dBFS），共享“翻页”滑杆。
- **胜利音乐 B 的触发点是通关结算面板，不是中段追兵战。** 早前实现把 `StartVictory()` 放在“击败追兵、进入 Departure 对话”处，于是 B 在中段放完、穿过后门时又被 `LeaveVictory()` 淡出，真正通关时 `started` 仍为真、`ShowVictory()` 又不再调用，结果通关后完全听不到音乐——这就是“通关没有胜利音乐”的直接原因。现改为：中段追兵胜利保持安静（只进入剧情对话），最后一波追兵全部被击败 → 告别对白 → 结算面板出现时 `ShowVictory()` 调一次 `StartVictory()`，1.3 秒淡入到 0.52 音量，`loop=false`、16 秒片段播完即止，同一轮不再重播；`Restart()` 会 `ResetVictory()` 停播并解锁，下一轮可再次获得。雨声全程不停，结算时只把雨的关注增益抬到 1.7 倍，不重启、不切断。之所以放在结算面板而不是最后一击的瞬间：B 只有 16 秒，若从最后一击起播，六句告别的阅读时间会把整段曲子耗掉，面板又会变回静音。
- 设置界面保留总音量、原菜单音乐和雨声音量，新增悬停、确认、翻页、胜利音乐 B 四个独立滑杆；各自通过 PlayerPrefs 保存。胜利 B 使用独立 AudioSource。
- 当前工程没有独立的战斗背景音乐 AudioSource。因此“战斗音乐渐弱”没有可控制的现成音轨；唯一常驻乐曲是主菜单主题，进入游戏时已用 2 秒淡出，胜利时不会再叠回来。战斗侧只有 `CombatAudioSystem` 的 8 路一次性拟音，`timeScale=0`（进入对话/结算）时统一停止，不会与 B 抢声道。

## 验证

Unity 6000.3.25f1，入口 `Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity`。菜单工具 `Tools/红尘客栈/音频/验证界面与胜利 (Play Mode)`；结果见 `Docs/AudioVerification/ui-victory-hover-brush-2026-11-18.txt`（28 项全过）与本轮 `Docs/AudioVerification/ending-ledger-2026-11-18.txt`（48 项全过）。该回归播放真实 UI 动作、序章递进、真实账本开合路径，并按剧情顺序走完“中段追兵胜利 → 离开客栈 → 最后一波清空 → 告别对白 → 结算面板 → 重开”；结局阶段通过注入 `DuelEncounter.EncounterStage.Complete` 复现“最后一波清空”这一信号，未改战斗判定，也未将其误报为人工通关。实际剑刃命中与战斗判定已由 `combat-2026-11-18.txt` 另行测试。当前项目没有完整战斗音乐音轨，不能验证其渐弱。

本轮同时修正了两处过期断言：雨声改为屋顶/室外两层后，“胜利时雨声变响”改为统计两层音量之和（只测屋顶层会因角色在室外而得到 0）；对话框按钮改为断言 `hoverEnabled == false`，并新增“悬停实际触发的是 `UI_Hover_Brush`”与“主菜单按钮仍保留悬停”两项。结局相关断言按新触发点重写：中段追兵胜利必须保持静音、结算面板必须恰好播一次、重复调用不得重播、`Restart` 必须停播并解锁。

同轮回归：`Logs/MenuAudioVerification.txt`（19 项 COMPLETE，含“无运行时报错”）、`Logs/DialogueUiVerification.txt`（COMPLETE），未受本次改动影响。
