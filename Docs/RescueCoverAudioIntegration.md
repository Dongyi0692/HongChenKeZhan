# 后门外听见的屋内交手声

当红拂在第二战后从后门走到屋外、五名追兵从前门实际入堂时，`InnInterface.BeginRescue()` 启动 `RescueCoverAudio`。首名追兵真正进入大堂后，先播三次项目已有的 `WoodFloor` 单步音效；至少约 1.35 秒且真实战斗已有首次挥剑、命中或格挡后，才淡入屋内刀剑交手声。红拂回屋时这层远处录音衰减；正式加入第三战、重开或重试时停止。现有 `CombatAudioSystem` 继续按真实挥剑、命中和格挡事件播放 3D 音效，战斗判定未改。

原始素材 `Assets/_Game/AudioSources/RescueCover/SwordBattle2.wav` 是用户选定的 Freesound [SwordBattle2.wav](https://freesound.org/people/freefire66/sounds/175951/)，作者 freefire66，CC0。原件保留为 88 秒、96 kHz、24 位立体声 WAV，SHA-256 `297e00cc05592aa97feb6d1e04d61350b78c133c77dea4fc1f34668cdbc1ae15`。

游戏用 `Assets/_Game/Resources/RescueCoverAudio/SwordBattle2_Loop.wav` 另存为 60 秒、48 kHz、16 位单声道 WAV。使用原件前 62 秒，双声道合成单声道，经 9 抽头低通后降采样，两端 2 秒交叉拼接，增益 2 倍，峰值约 0.60；原件未覆盖。Unity 的独立 3D 声源以约 0.19 音量上限和约 1.45 kHz 低通放在斗笠客附近，室外听感偏远，玩家回屋时淡出。木地板脚步复用 `Assets/_Game/Resources/Footsteps/WoodFloor/`，以单独 3D 声源播放，不循环整段录音。

检查要点：第二战后出后门 → 追兵入堂 → 木地板三步 → 录音淡入；站在门外听雨与屋内交手；返回屋内时远处录音衰减，靠近斗笠客进入第三战后停止。暂停时两路声源暂停。若追兵一直未进入大堂，录音不凭空启动。

`Docs/AudioPreviews/RescueCover_SequencePreview.wav` 是 8 秒干声顺序预览，含三步与交手声淡入，不模拟 Unity 里的空间距离和雨声。Unity 6.3.25f1 已导入资源并启动正式场景主菜单，Console 无新错误；第二战后至回援的完整人工游玩与现场听感仍待检查。
