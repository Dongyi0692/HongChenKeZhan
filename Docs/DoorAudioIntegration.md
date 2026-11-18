# 木门音效与雨声联动

原始素材：`Assets/_Game/AudioSources/Door/door-open-close-heavy-wooden-medium-creak-seal-theatre.wav`。保留用户原文件的逐字节副本，6.8275 秒、48 kHz、浮点立体声；不作为完整动作音效直接播放。

游戏片段位于 `Assets/_Game/Resources/DoorAudio/`，由原素材分段转为适合空间音效的单声道 PCM：

| 片段 | 原文件时间 | 用途 |
|---|---|---|
| WoodOpenHinge | 1.45–2.80 秒 | 开门转动，80ms 首尾交叠循环 |
| WoodCloseHinge | 4.70–6.12 秒 | 关门转动，80ms 首尾交叠循环 |
| WoodCloseImpact | 6.215–6.80 秒 | 完全闭合，低通柔化高频、保留沉闷撞击 |

## 同步绑定

当前门动画没有 Animator Animation Events：

- 前门 `InnEntranceDoors.Update()` 用 `Mathf.MoveTowards` 转动双门轴。实际配置 135 度、90 度/秒，完整单程约 1.5 秒。
- 后门 `InnRearExitDoor.Update()` 用 `Quaternion.RotateTowards` 转动。实际配置 -110 度、85 度/秒，完整单程约 1.294 秒。

两者的 `Awake()` 自动获取/添加 `InnDoorAudio`，**每次门轴旋转赋值之后**调用 `SyncMotion(previousAngle, currentAngle, fullAngle)`，传入距闭合位置的角度绝对值。前门共享一个门轴音源，避免双门重复响两遍。

转动期间使用对应方向的门轴循环片段；停止时约 80ms 收尾。角度由非零实际抵达零时只播放一次闭合撞击，不在点击交互、起始闭门状态或持续闭门帧播放。中途反向重新选择对应门轴片段，不提前触发撞击。暂停游戏时门轴和撞击音效暂停，雨声继续。

若以后替换为 Animator，应在实际门轴角度更新之后（例如 LateUpdate）调用上述同步入口；不要再同时添加另一套播放同音效的 Animation Event，以免重复触发。闭合撞击的绑定点应是门扇真正接触门框的那一帧。

## 雨声

门音效独立于背景音乐与雨声 AudioSource。门音效使用 3D 距离衰减，最小距离 3m、最大距离 22m，无多普勒变调。

`InnDoorAudio.RainExposure` 取当前启用木门的最大开度。`MenuAudio` 对原有持续雨声施加 1.0–1.55 倍的开度增益，开门响应约 0.65 秒，关门衰减响应约 1.4 秒；不重启雨声、不修改用户保存的雨声音量，雨声滑块为零时仍保持静音。主菜单使用原基准音量。

背景音乐素材与调度逻辑、雨声素材、场景资产、角色和战斗代码均保持原状。测试工具仅在运行态操作门及暂停状态，不保存场景。

验证：`DoorAudioVerification.StartTest`；报告：`Logs/DoorAudioVerification.txt`。覆盖前后门、停止转动、中途反向、暂停恢复、闭合撞击时机及雨声增减。
