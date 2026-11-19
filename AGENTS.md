# 红尘客栈：Codex 接手入口

2026-11-18 后门外掩护阶段新增屋内脚步和用户选定的 `SwordBattle2.wav` 远处交手层；原件、处理版与触发说明见 `Docs/RescueCoverAudioIntegration.md`。后续修改此段须保留真实战斗音效的事件触发，不把远处录音当作命中判定。

当前合并工作树为 `C:/Users/東壹/Documents/Codex/HongChenKeZhan_merged`，分支 `integrate/stage12-audio-v6`，基于本地玩法快照 `709d2e9` 和队友交付 `ad92f44`。先读 `Docs/Delivery/2026-11-18/HANDOFF.md`、`README_开始试玩.md`、`ProjectSettings/ProjectVersion.txt`，再检查 Git 状态和 Unity 场景。交接文档所述的 GitHub 分支与音频检查是合并前记录。
前门行为更新：从第一段开战到第三段获胜，`DuelEncounter` 通过 `HoldOpenForEncounter` 保持前门开启，覆盖战间对白、回援及失败重试。结束或重开后恢复近距离感应。下文“离开后关闭”仅适用于感应模式。

2026-11-18 室内反馈修订：当前 Stage12 一楼使用 `Architecture Collision/Main hall floor` 连续碰撞，视觉木板保留，30 块板的独立 MeshCollider 已关闭。门槛、楼梯及后院碰撞保留。跑步姿态依据实际行进速度，主角和 NPC 的起停使用约 0.24 秒混合和起停阈值，慢帧也至少分六次推进。原场景和本轮两份动作脚本备份在 `Backups/2026-11-18_before_indoor_feedback`。验证详情见 README 的室内反馈记录。以下 v6 的“场景未改脏”描述的是上一轮测试结束时的状态。

2026-11-18 当前工作区为编辑版v6。运行时已修正换片首帧真实Mixer权重归一；跑步软手臂及有界退出，正常跑速节奏至少.82倍、起步随速度变化，急转身每帧35°上限；脚底球形接地、小落差过滤、默认浅落地原片段前.10秒。useLightLanding关闭可单独恢复完整落地，重击独立回退菜单保持不变。左轻/右逐次重/F突进30体力无冷却不变。备份Backups/2026-11-18_before_motion_flow。不要用整文件覆盖回退局部功能。

v6主线已接入：二战后E后门离栈→门外RescueCover（五兵前门入堂，双方保护、只打斗笠客，主角不传送）→E返屋且靠近Elite对白→JoinRescueBattle正式第三战满血/检查点→胜利结交→FinalDeparture后门正式离场→独立Epilogue2.8秒、AI说明→结算。第三战重试直接从回援交战开始。前门内外3米感应与NPC请求并集，离开后关闭，UI不再强制ManualControl=true。不要恢复旧‘二战后留大堂自动三战’或‘第三战后直接结算’逻辑。11-18替换剧本正文方向现已接入，文档旧简介/附录是历史；本轮不新增Word/场景/专用过场动作。

v6已验证：Logs/DoorSensor_20261118_031939.json八相位/0errors；Logs/GroundReview_20261118_031825_979/report.json四项浅20cm/真跳/1.2m跌落通过；最终Logs/RearTraverse_20261118_141217_886/report.json真实Fighter跑出/返回后门（仅初始合法摆位，之后无禁碰撞/新floor/传送）143帧通过、0error/restoredtrue，到门外anchor约.079m并在低地停.8秒再真实回室内，未改生产地形；首轮033254保留。最终动作Logs/MotionReview_20261118_041603_083/report.json为12项/107帧/0error、两公开Jump成功、环境和参数恢复，真实Mixer权重始终1；换片首帧最大头3.0cm/髋2.7cm、Land→Idle首帧0，四臂骨长稳定、最大转身35°、Run剑与近似躯干柱最小间距9.35cm。源落地自然压身仍在，最低头1.008m/髋.610m，不得称完全不矮或全网格不穿模。该动作模式走公开API，不是硬件输入验收，键位另有148项InputSystem报告。只交付editable，无exe/ZIP/GitHub上传。

v6最终流程Logs/Stage12Story_20261118_141636_Retry/report.json为55项通过/0runtimeErrors/8教学/3战斗流程/3次当前段重试/回标题，6次正式开战均150/150；departure→门外Cover→return→Join→FinalDeparture→Epilogue2.8336秒→Victory全true。工具使用Dynamic onAfterUpdate的InputState.Change合成鼠标由正常Orbit/guide消费，胜败ReceiveDamage明确推进，门内外/盟友旁合法起点是夹具，不是完整手控通关。真实后门穿越由独立RearTraverse报告补证。临时focusPause隔离、键鼠device状态、Editor输入路由均有恢复true；InputSettings资产未保存。首轮031735、033157/033528、Story032112/040343/041709/042341等工具问题历史保留，不覆盖最终结果；040343/042341是测试目标高度误用了门外低地高度而导致摆位错误，最终按各锚点自身高度处理，没有改生产地形。

v6最终收尾已完成：5份临时Editor验证源码及meta已归档Logs/V6VerificationSources，移出后重新编译completed/failedfalse/errors[]；Unity停止Play，Stage12 isDirtyfalse/rootCount12。新自动恢复副本Assets/_Recovery/0 (1).unity原样归档Backups/2026-11-18_before_motion_flow/EditorRecovery，老0.unity保留。回援对白及独立终幕截图已查看，黑底居中字和AI说明可见；不据截图宣称完整真人手感或用户审美验收。

v6补充回归：Logs/V5Combat_20261118_035800_241/report.json为148pass/0fail/0error/640帧/restoredtrue/fixtureRestoredtrue，合成InputSystem执行，临时Editor输入路由与设备启用状态保存恢复，非硬件输入/焦点验收。Logs/Stage12RescueAI_20261118_040151/report.json为passedtrue/0errors，五兵前门全入堂，19.9959秒观察无注入伤害，13/10次敌友攻击、9/13次实际命中，uiStateRestoredtrue/回Titletrue。后者AI-only准备跳过剧情，不核新结尾，不是主角手控通关。

以下v5及更早内容均为历史记录，涉及流程、动作参数和‘未做后门’的描述不可覆盖上面的v6现状。

最终收尾于2026-11-18凌晨：新临时V5检查源码已移出Assets并归档Logs/V5VerificationSources；再次编译0错误，Unity停止Play，现有Stage12场景isDirty=false、12个根物体。HeavyAttackModeMenu是交付功能，必须保留。

2026-11-17 最新工作区为编辑版 v5：左键单段轻击、右键逐次重击、F突进斩30体力无冷却。右键每按一次推进四式中的一式，可穿插轻击形成ABACADAE；连续轻重攻击保持段数，停手约2.2秒、受伤或锁定/实际攻击目标变化后重置。分段是独立开关 `PrototypeFighter.useSequentialHeavy`，停止Play后菜单“红尘客栈 > 战斗 > 恢复整套四连招”并保存场景即可单独回退，其他优化保留，教学文本自动跟随。说明见 `Docs/2026-11-17_逐次重击单独回退说明.txt`。不要整份覆盖旧文件关闭分段，不要重置前几轮未提交改动。

v5 战斗最终复测 `Logs/V5Combat_20261117_231923_121/report.json`：148通过、0失败、0运行错误、726帧、restoredtrue/fixtureRestoredtrue。通过真实Input System合成键鼠驱动现有图和控制器，含混合连招、超时/受击重置、三种提前输入、F体力、独立开关、文案及两种低体力队列边界。低体力无效Dodge输入前和Update均检查可支付，无法支付时不阻挡免费轻重击。换目标为单独helper夹具，受击为明确1HP调用，低体力用实际SpendStamina构造。测试临时隔离UI点击/焦点暂停守卫，并恢复背景运行、输入设置、UI/raycaster和原开关；不是硬件延迟、焦点处理或完整三战验收。历史报告仍保留：215841_219为136项通过，224340_355为WallTimeLimit/0项执行并恢复，后者不能算通过；当前以231923_121最终复测为准。

v5镜头 `Logs/CameraV5_20261117_223119/report.json`：22项通过、432帧、0错误、wallSideFlips0，锁定移动目标30/30帧双人可见，同帧LateUpdate位移跟随误差0。是临时外部几何/角色和InputSystem夹具，不是全场景人工墙角验收；墙边敌人并非每帧可见。`Logs/V5_OperationGuide.png` 已检查完整可读，Preferred411.25<Available490。

v5自动流程 `Logs/Stage12Story_20261117_235055_Retry/report.json`：42项通过、0运行错误、8教学/3战斗流程/3次当前段重试、最终回标题；StageChanged同步采样6次均150/150。实际动作/鼠标教学计数未伪造，流程中暂停部分AI并明确ReceiveDamage推进胜败，不是手控打通。旧232132满血失败来自入场后才采样被战斗伤害干扰，本轮仅修正夹具采样时间，不是玩法修改。

v5室内AI `Logs/Stage12RescueAI_20261117_232834/report.json`：passedtrue、0错误、5兵前门全入堂；20.024秒观察无注入伤害，小兵14攻击12命中、友军11攻击14命中。准备跳过前两战且玩家安全放置、保护并排除目标；观察后1次明确伤害清最后幸存者才验证结交/胜利/回标题，不是自然通关。视觉剩余：`Logs/V5Combat_20261117_231923_121/Heavy_4.png`中第四式贴闭门时头/上躯干有穿插，单帧不能确认胶囊穿透，不在148项覆盖中，不得声称彻底消除穿模。

性能固定编辑器机位 `Logs/PerformanceAB_20261117_213606_811/report.json`：PC约10.1FPS→Mobile管线约21.2FPS，PC复测10.3，结束恢复原管线；不是exe/群战或60FPS保证。无手选记录的Intel显卡默认流畅，已手选保留。Mobile四骨骼权重与Standalone允许已用SaveAssets保存并直接读磁盘确认；实际Play为Quality0/Mobile_RPAsset/FourBones/SavedChoiceFalse/Intel Iris Xe/SceneDirtyFalse。不要使用InternalEditorUtility.SaveToSerializedFileAndForget保存这个已持久化对象，Unity会拒绝。人物未减面或替换。

主线仍为账本→八项实操→小兵→斗笠客→五兵前门入堂联战→自动结交结算，不为剧本新增后门流程。当前剧本 `Docs/Story/红尘客栈_游戏实况修订剧本_2026-11-17.docx` 已按游戏修订并用Word渲染检查9页；原稿保留，人物统一红衣，详见同目录游戏实况修订说明。本轮仅可编辑工程，没有构建exe/ZIP或上传GitHub，旧v3程序不含v4/v5改动。

历史 v4：当时F重击、右键突进斩30体力无冷却，中键锁定/Tab换敌，精准格挡、受击还击、常驻按键和导航、跑步左臂收敛、新剧本文字。该版通过公开动作与文本25项、UI19项、格挡与还击16项、锁定镜头50/50双人可见检查，最终第三战5兵入堂、18.66秒自然AI胜利、对白清理与结算通过，运行错误0，报告 `Logs/Stage12RescueAI_20261117_183318/report.json`。这些历史结果不能当作v5再次通过。详见README。
历史基准：2026-11-17 早间 0.3.2（v3）更新，Windows 构建及完整 ZIP 已完成：跑步持剑局部修正、小落差接地、空气墙/真实墙镜头避障，第三战改为 5 名小兵从前门实际进入大堂并在屋内作战；所有角色生命按原基准乘 1.5 并向上取整，伤害保留。友军追兵为 900 HP、最低 1 HP 剧情保护。不要沿用旧“后门触发回援、盟友 600 HP”的游玩说明。

本轮跑步/接地、镜头物理夹具、前门入场及 20 秒室内实际 AI 检查已通过，证据为 `Logs/RunGroundCarryVerification.json`、`Logs/CameraBoundaryVerification_20261117.json`、`Logs/Stage12RescueAI_20261117_135756/report.json`。独立三段重试检查也通过 20 项（`Logs/EncounterRetry_20261117_143507/report.json`）：每段真实失败、对白、结算、重试；同一主角每段 150 满血且倍率不叠加；第三战初次与重试均为 5 兵实际从前门入场，运行错误 0。本轮完整自动教学因转视角输入未被可靠消费而未完成，不得称全流程已通过。Windows 构建成功（0 错误、1 条旧动画接口警告），后台启动日志未见异常；ZIP 174 个文件逐条解压校验通过。独立版完整键鼠流程未复测，详见根目录 README。历史 0.3.0/0.3.1 结果不能当作本轮通过依据。0.3.1（v2）全屏修复保留，已在该版实测显示切换、最大化和重启保存；本地 0.3.1/0.3.2 更新尚未上传 GitHub，远端 Release 仍为 v1。

当前整合分支为 `integrate-stage12-story`，此前整合版已上传 GitHub：在现有 Stage12 上接入玩法分支、账本和三段流程。以根目录 `README.md` 与 `README_开始试玩.md` 的当前说明为准；下面及交付文档中“未合入玩法分支”描述的是原作者交付时状态。整合使用官方交付归档及经哈希核验的 LFS 资源，未执行交付分支的 Git 历史合并。最新独立版为 `Builds/2026-11-17_v3/Windows/HongChenKeZhan.exe`，完整 ZIP 在同级 v3 目录；上一个独立版为 `Builds/2026-11-17_v2/Windows/HongChenKeZhan.exe`，不含本轮玩法修复。发布包放 Releases，不提交整个 Builds。

- 正式名称是“红尘客栈”。历史命名空间 `FengChenInn`、资源名和 GUID 仍用于兼容，勿批量重命名。
- 使用 Unity 6000.3.25f1；正式场景为 `Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity`。不要把旧 `InnDuel.unity` 当成当前版本。
- 用户的本版场景优化优先。旧 `Entrance exterior ground` 入口平台已移除，不要从旧分支、备份或恢复场景加回。场景冲突逐项核对，不要整场覆盖；保留 `.meta` GUID。
- `Stage12Integration.apply` 已执行，勿重复；勿重新生成 Stage12 或启用旧 `InnStairTraversal`。保留平滑楼梯碰撞及现有角色、战斗、门和剧情交互。
- 主菜单、序章、雨区、脚步、战斗、UI 和胜利 B 的现有接入及原始素材路径见新交接文件。不要用旧验证报告代替修改后的 Play Mode 验证。
- Unity 模型、图片和音频使用 Git LFS；先拉取真实二进制再诊断导入。不要提交 `Library`、`Temp`、`Builds`、`Backups` 或 `Assets/_Recovery`。
- 当前交付是 Unity 工程，不包含 Blender 源工程；不要把 Unity 验证描述成 Blender 源文件验证。
