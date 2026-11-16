红拂女 Aholo 行走与剑术攻击样本 — 独立审阅资产

当前交付的是网页实际下载的 Hongfu_WalkA.glb 与 Hongfu_SwordAttack.glb 对应模型、真实行走及剑术攻击动画。
该目录不会自动替换 InnDuel 中的角色，也不会添加或保存任何 Unity Scene 文件。

生成入口（由主任务统一调用）
Unity 菜单：FengChenInn > Aholo 20261116 > Prepare Hongfu Walk Review
C# 入口：AholoReviewImport20261116.PrepareHongfu()
入口仅生成材料、独立 prefab 与 MotionSet；实例检查位于临时预览空间，结束即清除。
成功后生成：
- Hongfu_Aholo_WalkReview.prefab
- Hongfu_WalkReview_Motions.asset
- Materials/ 下的 13 个 URP Lit 材料
- 工程 Verification/Aholo20261116/Hongfu_WalkReview.json
生成入口已在 Unity 6000.3.25f1 后台运行成功，检查报告与独立 prefab 已生成；主场景未替换。

已核对的源数据
- 18 个网格、52 根骨骼、1,314,423 个三角面；没有减面。
- 绑定站姿高度约 2.010232 米，行走首帧约 1.979328 米；二者是不同姿态，勿按行走姿态误缩放。
- 原贴图 7 张（2048/4096），字节与 GLB 内嵌图片一致。
- 额外 2 张 Unity 金属/光滑图仅做通道打包，原图仍保留。
- 原始行走时长 1.333333 秒，前进 1.758042 米，参考移动速度约 1.318531 米/秒。
- Hongfu_Walk_InPlace.fbx 去除髋骨水平净位移，保留上下起伏和侧向摆动；回读首尾水平漂移为 0。
- base 模型与独立动作的全部骨名、层级和绑定矩阵一致。

动作配置
生成入口只将 Walk 与 Light 填入真实动画。Idle、Run、重击、技能、格挡、跳跃三段、闪避、受击、击退、死亡仍留空。
不要把已有片段填到其他用途槽位。该样本仍不是完整可战斗主角。
工作区原始 base FBX 中带前移的 Walk 不用于角色控制；工程 base 保留 Animation 导入以建立 Generic Avatar，MotionSet 只引用独立 InPlace 动作。
当前仍缺 Idle，不会在进入运行时用 Walk 假冒待机；Driver 会明确提示 Idle 尚未配置。

后续绑定
1. 使用生成的 Generic 骨架，不强行转换 Humanoid。Animator 在 Hongfu_Aholo_Model 上；不要重命名或重排 AholoRig 和 mixamorig:* 骨骼。
2. 动画驱动在 prefab 根节点，targetAnimator 指向上述 Animator，motionSet 指向 Hongfu_WalkReview_Motions。
3. 接入现有角色时，由原 CharacterController 决定世界位移；Animator.applyRootMotion 保持 false。
4. 先按参考速度 1.318531 米/秒检查脚底，再调 walkSpeed/动作播放倍率；旧玩家 2.2 米/秒需配合提高播放速度或调低走速，否则有滑步。
5. 佩剑尚未安装。右手挂点为 AholoRig 层级下的 mixamorig:RightHand；需将独立剑的握点、长轴、刃口方向校准后再配置 CombatActor 武器扫掠。
6. 生成入口根据左右脚尖平均方向校正视觉朝向至 +Z，只在视觉父层施加旋转，不改骨骼曲线；结果角度记录在 Unity 检查报告。
7. 原文件及转换校验保留在本次 work/AholoAnimations_20261116。Hongfu_SampleImport.json 记录源 SHA256、材料对应及贴图通道。
8. 所有 FBX 必须启用 Preserve Hierarchy，避免 Unity 剥离仅骨架文件的 AholoRig 顶层。动画路径应为 AholoRig/mixamorig:Hips/...，与完整模型完全一致。

验收范围
已完成 FBX 往返、文件哈希、Unity 编译与导入入口检查；真实行走采样最大关节变化49.719度，首尾水平漂移约0.00000075米，轻击根位移曲线约2.029米。握剑与完整游戏操作仍待后续资源齐全后人工验收。
原网格和贴图分辨率保留，资源成本较高；此批不擅自做性能或美术优化。

轻击补充（2026-11-16）
SwordAttack 完整1.533333秒，原始髋骨前进2.029194米。Hongfu_Light_SwordAttack.fbx保留原生运动；Hongfu_Light_InPlace.fbx仅把全部水平根运动移到独立曲线，Y方向身体起伏保留。
PrepareHongfu 会在最终Unity朝向模型上采样原始片段，生成MotionSet.lightRootMotion的x/z随时间曲线；Light使用根部固定的片段。CombatActor通过CharacterController.Move执行同一曲线并处理碰撞，不让模型离开碰撞体。
已有审阅prefab时可调用 AholoReviewImport20261116.AddHongfuLight() 添加新片段与曲线，不替换主场景。
Hongfu_Light_SourceRootMotion.json保存源glTF坐标下曲线作追溯；直接游戏运行使用MotionSet中Unity朝向空间的曲线。

