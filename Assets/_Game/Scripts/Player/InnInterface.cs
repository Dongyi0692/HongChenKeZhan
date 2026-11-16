using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FengChenInn
{
    public sealed class InnInterface : MonoBehaviour
    {
        private enum View { Title, Play, Tutorial, Dialogue, Pause, Settings, Victory, Defeat, Ledger }
        private enum StoryPart { Road, Hall, TutorialFight, EliteArriving, EliteFight, Departure, ReturnToHelp, JointFight, Finished, AwaitSentry, Practice }
        private enum PendingBattle { None, Tutorial, Elite, Rescue }

        [Header("Story interaction anchors")]
        public InnStoryAnchors storyAnchors;
        public Transform ledgerAnchor, rearDoorAnchor, rearExitAnchor;
        [Min(.5f)] public float ledgerInteractionDistance = 1.8f;
        [Min(.5f)] public float rearExitDistance = 1.3f;
        public bool LedgerRead => ledgerRead;
        public int PracticeStep => practiceGuide.StepIndex;
        public bool IsPracticing => part == StoryPart.Practice;

        private static readonly Color Paper = new Color32(245, 233, 213, 255);
        private static readonly Color Muted = new Color32(191, 178, 166, 255);
        private static readonly Color Amber = new Color32(237, 186, 110, 255);
        private static readonly Color Plum = new Color32(175, 133, 187, 255);
        private static readonly Color Red = new Color32(216, 120, 102, 255);
        private static readonly Color Wood = new Color32(35, 27, 28, 250);
        private static readonly Color Line = new Color32(105, 81, 66, 255);
        private static readonly Color Ink = new Color32(16, 17, 22, 235);
        private static readonly Vector2 TopLeft = new(0, 1), Top = new(.5f, 1), TopRight = new(1, 1);
        private static readonly Vector2 Center = new(.5f, .5f), Bottom = new(.5f, 0), BottomLeft = Vector2.zero;
        private const string Pref = "FengChenInn.UI.";

        private static readonly (int width, int height)[] ResolutionOptions =
            { (1280, 720), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160) };
        private static readonly FullScreenMode[] FullscreenOptions =
            { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
        private static readonly string[] FullscreenLabels = { "全屏", "无边框", "窗口" };

        private readonly Dictionary<View, RectTransform> views = new();
        private readonly HashSet<CombatActor> watchedEnemies = new();
        private PrototypeFighter fighter;
        private PrototypeInput input;
        private DuelEncounter duel;
        private OrbitCamera orbit;
        private Camera sceneCamera;
        private Font bodyFont, titleFont;
        private Texture2D titleShade, panelGrain, ledgerInk;
        private RectTransform root, bars, warning, hint, rivalPanel, allyPanel;
        private UnityEngine.UI.Image playerFill, rivalFill, allyFill, staminaFill, tellFill;
        private UnityEngine.UI.Text playerValue, rivalValue, rivalName, allyValue, staminaValue, enemyCount, objective;
        private UnityEngine.UI.Text warningTitle, warningAction, warningDirection, hintText, speaker, dialogue, pageCount, qualityText, resolutionText, fullscreenText;
        private UnityEngine.UI.Text shortLine, victoryStats, defeatStats, tutorialControls;
        private View view, settingsReturn;
        private bool initialized, firstTutorial, resultStarted, hasHelped, heardAllyDown;
        private (string speaker, string line)[] lines;
        private int lineIndex;
        private Action afterDialogue;
        private StoryPart part;
        private PendingBattle pendingBattle;
        private InnEntranceDoors entrance;
        private CombatActor watchedPlayer;
        private float shortLineUntil, fightLength, arrivalAt, rescueAt, hintUntil;
        private float battleStartFightLength;
        private bool battleStartDoorOpen;
        private bool battleStartRearDoorOpen, ledgerRead;
        private readonly InnPracticeGuide practiceGuide = new();
        private RectTransform practicePanel;
        private UnityEngine.UI.Text practiceProgress, practiceInstruction;
        private bool watchingSkirmish;
        private bool lightHit, heavyHit, defended;
        private RectTransform targetMarker;
        private readonly List<UnityEngine.UI.Image> targetStrokes = new();
        private UnityEngine.UI.Text attackFeedback, defenseFeedback;
        private float attackFeedbackUntil, defenseFeedbackUntil, targetFlashUntil, nextFeedbackSoundAt;
        private CombatActor lastHitTarget;
        private AudioSource feedbackAudio;
        private AudioClip strikeSound, blockSound, hurtSound;
        private readonly RaycastHit[] markerObstructions = new RaycastHit[32];

        public void Initialize(PrototypeFighter player, DuelEncounter encounter)
        {
            fighter = player;
            input = player.GetComponent<PrototypeInput>();
            input.InterfaceControlsDuel = true;
            input.StoryAllowsCombat = false;
            duel = encounter;
            ResolveStoryAnchors();
            entrance = FindFirstObjectByType<InnEntranceDoors>();
            // Keep the current story's manual door interaction. No new scene or door is created.
            if (entrance != null) entrance.ManualControl = true;
            sceneCamera = Camera.main;
            orbit = sceneCamera != null ? sceneCamera.GetComponent<OrbitCamera>() : null;
            bodyFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "SimHei" }, 32);
            titleFont = Font.CreateDynamicFontFromOSFont(new[] { "KaiTi", "SimSun", "Microsoft YaHei" }, 96);
            root = Fill(transform, "红尘客栈界面");
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            if (EventSystem.current == null)
                new GameObject("Inn UI Events", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var scaler = GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.referenceResolution = new Vector2(1600, 900);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }
            titleShade = MakeTexture(true);
            panelGrain = MakeTexture(false);
            AudioListener.volume = PlayerPrefs.GetFloat(Pref + "volume", AudioListener.volume);
            QualitySettings.SetQualityLevel(Mathf.Clamp(PlayerPrefs.GetInt(Pref + "quality", QualitySettings.GetQualityLevel()), 0, QualitySettings.names.Length - 1), true);
            RestoreDisplaySettings();
            if (orbit != null)
            {
                orbit.sensitivity = PlayerPrefs.GetFloat(Pref + "sensitivity", orbit.sensitivity);
                orbit.invertY = PlayerPrefs.GetInt(Pref + "invert") != 0;
            }
            BuildPlay();
            BuildTitle();
            BuildTutorial();
            BuildDialogue();
            BuildLedger();
            BuildPause();
            BuildSettings();
            BuildResult(true);
            BuildResult(false);
            initialized = true;
            Show(View.Title);
        }

        private void Update()
        {
            if (!initialized) return;
            WatchCombatEvents();
            var keys = Keyboard.current;
            if (keys != null && keys.escapeKey.wasPressedThisFrame)
            {
                if (view == View.Play) Show(View.Pause);
                else if (view == View.Pause) Show(View.Play);
                else if (view == View.Settings) CloseSettings();
                else if (view == View.Tutorial) CloseTutorial();
                else if (view == View.Ledger) Show(View.Play);
                return;
            }
            if (view == View.Dialogue)
            {
                if (keys != null && (keys.eKey.wasPressedThisFrame || keys.spaceKey.wasPressedThisFrame)) AdvanceDialogue();
                return;
            }
            if (view == View.Ledger)
            {
                if (keys != null && keys.eKey.wasPressedThisFrame) FinishReadingLedger();
                return;
            }
            if (view == View.Victory || view == View.Defeat)
            {
                if (keys != null && keys.rKey.wasPressedThisFrame) Restart(false);
                return;
            }
            if (view != View.Play) return;
            if (keys != null && keys.f1Key.wasPressedThisFrame) { Show(View.Tutorial); return; }
            if (keys != null && keys.rKey.wasPressedThisFrame) { Restart(false); return; }
            UpdatePlay(keys != null && keys.eKey.wasPressedThisFrame);
        }

        // Project after the camera and fighter Update, so the marker and health
        // bar both describe the target selected for this frame.
        private void LateUpdate()
        {
            if (!initialized) return;
            bool battle = view == View.Play && IsCombatPart && duel.IsCombatActive;
            CombatActor enemy = battle ? fighter.SelectedTarget : null;
            bool hasTarget = enemy != null && enemy.isActiveAndEnabled && !enemy.IsDefeated
                && !enemy.ControlLocked && duel.PlayerActor != null && duel.PlayerActor.IsHostileTo(enemy);
            rivalPanel.gameObject.SetActive(hasTarget);
            if (hasTarget)
            {
                rivalFill.fillAmount = (float)enemy.Health / Mathf.Max(1, enemy.MaxHealth);
                rivalValue.text = $"{enemy.Health} / {enemy.MaxHealth}";
                rivalName.text = enemy.DisplayName;
            }
            Vector2 markerPosition = default;
            bool showMarker = hasTarget && TryProjectTarget(enemy, out markerPosition);
            targetMarker.gameObject.SetActive(showMarker);
            if (showMarker)
            {
                targetMarker.anchoredPosition = markerPosition;
                Color color = enemy == lastHitTarget && Time.unscaledTime < targetFlashUntil ? Amber : Paper;
                foreach (var stroke in targetStrokes) stroke.color = color;
            }
            UpdateFeedbackLabel(attackFeedback, battle, attackFeedbackUntil);
            UpdateFeedbackLabel(defenseFeedback, battle, defenseFeedbackUntil);
        }

        private bool TryProjectTarget(CombatActor enemy, out Vector2 position)
        {
            position = default;
            if (sceneCamera == null) sceneCamera = Camera.main;
            if (sceneCamera == null || !duel.PlayerActor.HasLineOfSight(enemy)) return false;
            Vector3 point = enemy.transform.position + Vector3.up * 1.25f;
            Vector3 viewport = sceneCamera.WorldToViewportPoint(point);
            if (viewport.z <= 0f || viewport.x < .03f || viewport.x > .97f || viewport.y < .03f || viewport.y > .97f)
                return false;
            Vector3 fromCamera = point - sceneCamera.transform.position;
            int count = Physics.RaycastNonAlloc(sceneCamera.transform.position, fromCamera.normalized,
                markerObstructions, fromCamera.magnitude, ~0, QueryTriggerInteraction.Ignore);
            // A full buffer cannot prove visibility; avoid drawing through a wall.
            if (count == markerObstructions.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Transform obstacle = markerObstructions[i].transform;
                if (!obstacle.IsChildOf(enemy.transform) && !obstacle.IsChildOf(fighter.transform)) return false;
            }
            var canvas = GetComponent<Canvas>();
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(views[View.Play],
                sceneCamera.WorldToScreenPoint(point), uiCamera, out position);
        }

        private static void UpdateFeedbackLabel(UnityEngine.UI.Text label, bool battle, float until)
        {
            bool visible = battle && Time.unscaledTime < until;
            label.transform.parent.gameObject.SetActive(visible);
            if (!visible) return;
            Color color = label.color;
            color.a = Mathf.Clamp01((until - Time.unscaledTime) / .18f);
            label.color = color;
        }

        private void WatchCombatEvents()
        {
            if (duel.PlayerActor != watchedPlayer)
            {
                UnwatchPlayerEvents();
                watchedPlayer = duel.PlayerActor;
                if (watchedPlayer != null)
                {
                    watchedPlayer.Blocked += OnPlayerBlocked;
                    watchedPlayer.HitLanded += OnPlayerHitLanded;
                    watchedPlayer.AttackBlocked += OnPlayerAttackBlocked;
                    watchedPlayer.Damaged += OnPlayerDamaged;
                }
            }
            foreach (var actor in duel.EnemyActors)
                if (actor != null && watchedEnemies.Add(actor)) actor.Damaged += OnEnemyDamaged;
        }

        private void OnPlayerBlocked(CombatActor source)
        {
            if (part == StoryPart.TutorialFight) defended = true;
            ShowCombatFeedback(false, "格挡成功", Amber, blockSound);
        }

        private void OnPlayerHitLanded(CombatActor target, int damage)
        {
            if (target == null || damage <= 0 || !duel.PlayerActor.IsHostileTo(target)) return;
            lastHitTarget = target;
            targetFlashUntil = Time.unscaledTime + .14f;
            ShowCombatFeedback(true, $"命中 · {damage}", Paper, strikeSound);
        }

        private void OnPlayerAttackBlocked(CombatActor target)
        {
            if (target != null && duel.PlayerActor.IsHostileTo(target))
                ShowCombatFeedback(true, "被格挡", Amber, blockSound);
        }

        private void OnPlayerDamaged(CombatActor source, int damage)
        {
            if (damage > 0) ShowCombatFeedback(false, $"受击 · −{damage}", Red, hurtSound);
        }

        private void ShowCombatFeedback(bool offense, string text, Color color, AudioClip sound)
        {
            if (!isActiveAndEnabled || view != View.Play || !IsCombatPart || !duel.IsCombatActive) return;
            var label = offense ? attackFeedback : defenseFeedback;
            label.text = text;
            label.color = color;
            if (offense) attackFeedbackUntil = Time.unscaledTime + .7f;
            else defenseFeedbackUntil = Time.unscaledTime + .7f;
            if (feedbackAudio != null && sound != null && Time.unscaledTime >= nextFeedbackSoundAt)
            {
                feedbackAudio.PlayOneShot(sound);
                nextFeedbackSoundAt = Time.unscaledTime + .045f;
            }
        }

        private void UnwatchPlayerEvents()
        {
            if (watchedPlayer == null) return;
            watchedPlayer.Blocked -= OnPlayerBlocked;
            watchedPlayer.HitLanded -= OnPlayerHitLanded;
            watchedPlayer.AttackBlocked -= OnPlayerAttackBlocked;
            watchedPlayer.Damaged -= OnPlayerDamaged;
        }

        private void OnEnemyDamaged(CombatActor source, int amount)
        {
            if (part != StoryPart.TutorialFight || source != duel.PlayerActor || amount <= 0) return;
            if (source.CurrentAttack != null && source.CurrentAttack.motion == CombatMotion.Light) lightHit = true;
            if (source.CurrentAttack != null && source.CurrentAttack.motion == CombatMotion.Heavy) heavyHit = true;
        }

        private void UnwatchCombatEvents()
        {
            UnwatchPlayerEvents();
            watchedPlayer = null;
            foreach (var actor in watchedEnemies) if (actor != null) actor.Damaged -= OnEnemyDamaged;
            watchedEnemies.Clear();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && initialized && view == View.Play) Show(View.Pause);
        }

        private void OnDisable() => ClearCombatFeedback();

        private void OnDestroy()
        {
            if (!initialized) return;
            practiceGuide.Dispose();
            if (fighter != null) fighter.SetDialoguePose(false);
            UnwatchCombatEvents();
            Time.timeScale = 1f;
            PrototypeInput.SetCursor(false);
            Destroy(titleShade);
            Destroy(panelGrain);
            Destroy(ledgerInk);
            Destroy(bodyFont);
            Destroy(titleFont);
            Destroy(strikeSound);
            Destroy(blockSound);
            Destroy(hurtSound);
        }

        private void Show(View next)
        {
            ClearCombatFeedback();
            view = next;
            foreach (var pair in views) pair.Value.gameObject.SetActive(pair.Key == next);
            bool play = next == View.Play;
            fighter.SetDialoguePose(next == View.Dialogue || next == View.Ledger);
            Time.timeScale = play ? 1f : 0f;
            input.enabled = play;
            fighter.ClearInput();
            PrototypeInput.SetCursor(play);
            bool titleView = next == View.Title || (next == View.Settings && settingsReturn == View.Title);
            if (orbit != null)
            {
                orbit.enabled = !titleView;
                if (play) orbit.ResumeFollow();
            }
            watchingSkirmish = false;
            if (titleView && sceneCamera != null)
            {
                var position = new Vector3(4.9f, 2.9f, -11.8f);
                sceneCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(new Vector3(0f, 1.9f, -4.96f) - position));
            }
            if (next == View.Tutorial) UpdateTutorialControls();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (play) hintUntil = Time.unscaledTime + 6f;
        }

        private void ClearCombatFeedback()
        {
            attackFeedbackUntil = defenseFeedbackUntil = targetFlashUntil = nextFeedbackSoundAt = 0f;
            lastHitTarget = null;
            if (targetMarker != null) targetMarker.gameObject.SetActive(false);
            if (attackFeedback != null) attackFeedback.transform.parent.gameObject.SetActive(false);
            if (defenseFeedback != null) defenseFeedback.transform.parent.gameObject.SetActive(false);
            if (feedbackAudio != null) feedbackAudio.Stop();
        }

        private void StartGame()
        {
            firstTutorial = false;
            Show(View.Play);
        }

        private void CloseTutorial()
        {
            if (firstTutorial) PlayerPrefs.SetInt(Pref + "tutorialV2", 1);
            firstTutorial = false;
            PlayerPrefs.Save();
            Show(View.Play);
        }

        private void Restart(bool toTitle)
        {
            if (!toTitle && (view == View.Defeat || duel.CanRetryCurrentBattle))
            {
                RetryCurrentBattle();
                return;
            }
            UnwatchCombatEvents();
            practiceGuide.Dispose();
            afterDialogue = null;
            lines = null;
            fighter.ResetPose();
            duel.ResetEncounter();
            input.StoryAllowsCombat = false;
            part = StoryPart.Road;
            pendingBattle = PendingBattle.None;
            ledgerRead = false;
            if (entrance != null) entrance.OpenRequested = false;
            if (storyAnchors != null) storyAnchors.SetRearDoorOpen(false);
            shortLineUntil = fightLength = arrivalAt = rescueAt = 0f;
            battleStartFightLength = 0f;
            battleStartDoorOpen = false;
            battleStartRearDoorOpen = false;
            lightHit = heavyHit = defended = hasHelped = heardAllyDown = resultStarted = false;
            Show(toTitle ? View.Title : View.Play);
        }

        private void RememberBattleStart()
        {
            battleStartFightLength = fightLength;
            battleStartDoorOpen = entrance != null && entrance.OpenRequested;
            battleStartRearDoorOpen = storyAnchors != null && storyAnchors.RearDoorOpen;
        }

        private void RetryCurrentBattle()
        {
            if (!duel.RetryCurrentBattle())
            {
                // Never discard completed fights when a checkpoint cannot be restored.
                defeatStats.text = "暂时无法恢复本段战斗。\n请检查角色站位与动作资源后再次重试。";
                input.StoryAllowsCombat = false;
                Show(View.Defeat);
                return;
            }
            UnwatchCombatEvents();
            practiceGuide.Dispose();
            afterDialogue = null;
            lines = null;
            pendingBattle = PendingBattle.None;
            resultStarted = hasHelped = heardAllyDown = false;
            shortLineUntil = arrivalAt = rescueAt = 0f;
            fightLength = battleStartFightLength;
            part = duel.Stage == DuelEncounter.EncounterStage.Tutorial ? StoryPart.TutorialFight
                : duel.Stage == DuelEncounter.EncounterStage.EliteDuel ? StoryPart.EliteFight : StoryPart.ReturnToHelp;
            if (part == StoryPart.TutorialFight) lightHit = heavyHit = defended = false;
            if (entrance != null) entrance.OpenRequested = battleStartDoorOpen;
            if (storyAnchors != null) storyAnchors.SetRearDoorOpen(battleStartRearDoorOpen);
            if (part == StoryPart.ReturnToHelp) rescueAt = Time.time;
            input.StoryAllowsCombat = true;
            WatchCombatEvents();
            Show(View.Play);
            Say("已从本段战斗重新开始。", 2.5f);
        }

        private bool IsCombatPart => part == StoryPart.TutorialFight || part == StoryPart.EliteFight
            || part == StoryPart.ReturnToHelp || part == StoryPart.JointFight;

        private void UpdatePlay(bool interact)
        {
            // Briefly frame the actual ally/enemy skirmish; the player can still move.
            bool watchSkirmish = part == StoryPart.ReturnToHelp && Time.time < rescueAt + 2f;
            if (watchSkirmish && !watchingSkirmish)
                FrameActors(duel.EliteActor != null ? duel.EliteActor.transform : null,
                    duel.ActiveEnemy != null ? duel.ActiveEnemy.transform : null);
            else if (!watchSkirmish && watchingSkirmish && orbit != null) orbit.ResumeFollow();
            watchingSkirmish = watchSkirmish;
            bool battle = IsCombatPart && duel.IsCombatActive;
            input.StoryAllowsCombat = battle || part == StoryPart.Practice;
            UpdateMeters(battle);
            if (battle) fightLength += Time.deltaTime;
            shortLine.gameObject.SetActive(Time.time < shortLineUntil);
            if (duel.Stage == DuelEncounter.EncounterStage.Failed && !resultStarted)
            {
                FailCurrentStage();
                return;
            }
            if (part == StoryPart.TutorialFight && duel.Stage == DuelEncounter.EncounterStage.EliteReady)
            {
                part = StoryPart.EliteArriving;
                input.StoryAllowsCombat = false;
                arrivalAt = Time.time + 1.1f;
                Say("红拂女 · 借宿的路，也要拿剑来换。\n门外又响起了脚步声。", 4.5f);
            }
            if (part == StoryPart.EliteFight && duel.Stage == DuelEncounter.EncounterStage.EliteVictory)
            {
                input.StoryAllowsCombat = false;
                part = StoryPart.Departure;
                OpenDialogue(() =>
                {
                    duel.MakeEliteAlly();
                    Show(View.Play);
                    Say("从后门离开客栈。", 4.5f);
                }, InnStoryScript.Victory);
                return;
            }
            if ((part == StoryPart.ReturnToHelp || part == StoryPart.JointFight)
                && duel.Stage == DuelEncounter.EncounterStage.Complete && hasHelped && !resultStarted)
            {
                resultStarted = true;
                part = StoryPart.Finished;
                input.StoryAllowsCombat = false;
                duel.StopCombat();
                OpenDialogue(ShowVictory, duel.EliteActor != null && duel.EliteActor.IsDefeated
                    ? InnStoryScript.FarewellWhileAllyDown : InnStoryScript.Farewell);
                return;
            }
            string cue = UpdateStoryMovement(interact);
            if (view != View.Play) return;
            practicePanel.gameObject.SetActive(part == StoryPart.Practice);
            if (part == StoryPart.Practice)
            {
                practiceProgress.text = practiceGuide.Progress;
                practiceInstruction.text = practiceGuide.Instruction;
            }
            if (part == StoryPart.TutorialFight) cue = TutorialHint();
            if (battle && fighter.Stamina < 1f) cue = "体力不足 · 松开格挡，等待体力恢复";
            hint.gameObject.SetActive(cue.Length != 0 || Time.unscaledTime < hintUntil);
            hintText.text = cue.Length != 0 ? cue : "F1 操作   ·   Esc 暂停";
        }

        private void UpdateMeters(bool battle)
        {
            bars.gameObject.SetActive(true);
            CombatActor threat = IncomingThreat();
            CombatActor player = duel.PlayerActor, ally = duel.EliteActor;
            playerFill.fillAmount = player != null ? (float)player.Health / Mathf.Max(1, player.MaxHealth) : 1f;
            playerValue.text = player != null ? $"{player.Health} / {player.MaxHealth}" : "—";
            staminaFill.fillAmount = fighter.Stamina01;
            staminaValue.text = $"体力 {Mathf.CeilToInt(fighter.Stamina)} / {Mathf.CeilToInt(fighter.MaxStamina)}";
            staminaFill.color = fighter.Stamina01 < .2f ? Red : Amber;
            bool showAlly = ally != null && (part == StoryPart.Departure || part == StoryPart.ReturnToHelp || part == StoryPart.JointFight);
            allyPanel.gameObject.SetActive(showAlly);
            if (showAlly)
            {
                allyFill.fillAmount = (float)ally.Health / Mathf.Max(1, ally.MaxHealth);
                allyValue.text = ally.IsDefeated ? "倒地 · 失战" : $"{ally.Health} / {ally.MaxHealth}";
            }
            bool rescue = part == StoryPart.ReturnToHelp || part == StoryPart.JointFight;
            enemyCount.gameObject.SetActive(rescue);
            enemyCount.text = $"敌对来兵  {duel.RemainingEnemies}";
            objective.text = part switch
            {
                StoryPart.Road => "入栈  ·  沿驿路走向客栈",
                StoryPart.Hall => "空栈  ·  查看账台上的账本",
                StoryPart.AwaitSentry => "异动  ·  留意账台附近",
                StoryPart.Practice => "初试剑  ·  按提示完成实操",
                StoryPart.TutorialFight => "初试剑  ·  击退驻守小兵",
                StoryPart.EliteArriving => "追兵已至  ·  留意客栈门口",
                StoryPart.EliteFight => "赴约  ·  击败追兵",
                StoryPart.Departure => "承诺  ·  从后门离开客栈",
                StoryPart.ReturnToHelp => "回头  ·  返回帮助追兵",
                StoryPart.JointFight => "并肩  ·  击退所有来兵",
                _ => ""
            };
            if (pendingBattle != PendingBattle.None) objective.text = "暂缓交手  ·  可自由探索";
            bool danger = battle && threat != null;
            warning.gameObject.SetActive(danger);
            if (danger)
            {
                CombatAttack attack = threat.CurrentAttack;
                warningTitle.text = string.IsNullOrWhiteSpace(attack.label) ? "来招 · 留意起手" : attack.label;
                warningTitle.color = attack.motion == CombatMotion.Skill ? Red : Amber;
                string response = fighter.IsMotionAvailable(CombatMotion.Guard) ? "Q 面向来招格挡" : "拉开距离";
                if (attack.unblockable) response = "避开攻击范围";
                if (fighter.IsMotionAvailable(CombatMotion.Dodge)) response += " / Shift 闪避";
                warningAction.text = response;
                warningDirection.text = DirectionLabel(threat.transform);
                float firstHit = attack.activeStart;
                if (attack.hitWindows != null && attack.hitWindows.Length > 0)
                    foreach (var window in attack.hitWindows) if (window != null) firstHit = Mathf.Min(firstHit, window.start);
                tellFill.fillAmount = Mathf.Clamp01(threat.AttackElapsed / Mathf.Max(.01f, firstHit));
                tellFill.color = warningTitle.color;
            }
        }

        private CombatActor IncomingThreat()
        {
            CombatActor result = null;
            float nearest = float.PositiveInfinity;
            foreach (var enemy in duel.EnemyActors)
            {
                if (enemy == null || enemy.IsDefeated || enemy.CurrentAttack == null || enemy.Team != CombatTeam.Enemy) continue;
                var brain = enemy.GetComponent<EnemyCombatAI>();
                if (brain == null || brain.Target != duel.PlayerActor) continue;
                CombatAttack attack = enemy.CurrentAttack;
                float lastHit = attack.activeEnd;
                if (attack.hitWindows != null)
                    foreach (var window in attack.hitWindows) if (window != null) lastHit = Mathf.Max(lastHit, window.end);
                if (enemy.AttackElapsed > lastHit) continue;
                float distance = (enemy.transform.position - fighter.transform.position).sqrMagnitude;
                if (distance < nearest) { nearest = distance; result = enemy; }
            }
            return result;
        }

        private bool Near(Vector3 spot, float range = 1.18f)
        {
            Vector3 delta = fighter.transform.position - spot;
            return Mathf.Abs(delta.y) < 1.2f && delta.x * delta.x + delta.z * delta.z <= range * range;
        }

        private void Say(string text, float seconds = 4.8f)
        {
            shortLine.text = text;
            shortLineUntil = Time.time + seconds;
            shortLine.gameObject.SetActive(true);
        }

        private string UpdateStoryMovement(bool interact)
        {
            float doorZ = entrance != null ? entrance.transform.position.z : -4.96f;
            Vector3 door = new(entrance != null ? entrance.transform.position.x : 0f, .05f, doorZ);
            if (pendingBattle != PendingBattle.None)
            {
                // Retry only on a fresh interaction. Missing imports must not replay dialogue every frame.
                if (interact)
                {
                    switch (pendingBattle)
                    {
                        case PendingBattle.Tutorial: BeginTutorialFight(); break;
                        case PendingBattle.Elite: BeginEliteFight(); break;
                        case PendingBattle.Rescue: BeginRescue(); break;
                    }
                }
                return pendingBattle == PendingBattle.None ? "" : "交手暂未就绪 · 可继续探索，E 重试，R 重置";
            }
            switch (part)
            {
                case StoryPart.Road:
                    if (fighter.transform.position.z > doorZ + .65f)
                    {
                        if (entrance != null) entrance.OpenRequested = false;
                        part = StoryPart.Hall;
                        Say("红拂女 · " + InnStoryScript.Arrival);
                        return "店内无人 · 去账台看看";
                    }
                    if (interact && Near(door, 2f) && entrance != null) entrance.OpenRequested = true;
                    return Near(door, 2f) ? "E   推门　「逆旅」" : "WASD 移动并转身 · 鼠标转动视角";
                case StoryPart.Hall:
                    if (ledgerAnchor == null) return "账本交互点尚未配置";
                    if (Near(ledgerAnchor.position, ledgerInteractionDistance))
                    {
                        if (interact) Show(View.Ledger);
                        return "E   翻阅账本";
                    }
                    return "店内无人 · 查看账台上的账本";
                case StoryPart.AwaitSentry:
                    if (interact) RevealSentryAndTalk();
                    return "小兵现身的位置被挡住 · 留出空地，E 重试";
                case StoryPart.Practice:
                    practiceGuide.Tick(Time.deltaTime);
                    if (practiceGuide.Complete)
                    {
                        if (duel.CompleteTutorialInstructions()) BeginTutorialFight();
                        else return "动作尚未收势 · 稍候开始交手";
                    }
                    return practiceGuide.Instruction;
                case StoryPart.EliteArriving:
                    if (!duel.EliteEntranceInProgress && !duel.EliteEntranceComplete && Time.time >= arrivalAt)
                    {
                        if (!duel.BeginEliteEntrance())
                        {
                            arrivalAt = float.PositiveInfinity;
                            Say("追兵尚未进门，请留出门口通道，按 E 重试。", 6f);
                        }
                    }
                    if (interact && !duel.EliteEntranceInProgress && !duel.EliteEntranceComplete)
                        arrivalAt = Time.time;
                    if (duel.EliteEntranceReadyForDialogue && duel.EliteActor != null && Near(duel.EliteActor.transform.position, 4f))
                        OpenDialogue(BeginEliteFight, InnStoryScript.BeforeFight);
                    return duel.EliteEntranceComplete ? "追兵已在门边 · 靠近交谈"
                        : duel.EliteEntranceInProgress ? "追兵正在入栈 · 留出门口通道" : "门外传来脚步声";
                case StoryPart.EliteFight:
                    return "看清起手，等对手收势再反击";
                case StoryPart.Departure:
                    if (rearDoorAnchor == null || rearExitAnchor == null || storyAnchors == null)
                        return "后门撤离引导点尚未配置";
                    if (Near(rearExitAnchor.position, rearExitDistance) && storyAnchors.RearDoorOpen)
                    {
                        BeginRescue();
                        return "门外传来兵刃声 · 回头看看";
                    }
                    if (Near(rearDoorAnchor.position, storyAnchors.rearDoorDistance))
                    {
                        if (interact && !storyAnchors.OpenRearDoor()) return "后门尚未配置开门组件";
                        return storyAnchors.RearDoorOpen ? "穿过后门，离开客栈" : "E   推开后门";
                    }
                    return "走向后门 · E 推门离开";
                case StoryPart.ReturnToHelp:
                    if (!hasHelped && Time.time >= rescueAt + 2f && duel.EliteActor != null && Near(duel.EliteActor.transform.position, 3.5f))
                    {
                        hasHelped = true;
                        part = StoryPart.JointFight;
                        Say("追兵 · 不是叫你走么？\n红拂女 · 欠你一句话，总得还上。", 5.5f);
                    }
                    if (duel.Stage == DuelEncounter.EncounterStage.Complete && !hasHelped)
                        return "来兵已退 · 靠近追兵交谈";
                    return "返回帮助追兵 · 同伴之间不会互相伤害";
                case StoryPart.JointFight:
                    if (!heardAllyDown && duel.EliteActor != null && duel.EliteActor.IsDefeated)
                    {
                        heardAllyDown = true;
                        Say("追兵暂时失去战斗能力。\n击退剩余来兵。", 4f);
                    }
                    return "与追兵并肩 · 击退所有来兵";
                default:
                    return "";
            }
        }

        private string TutorialHint()
        {
            if (!lightHit && fighter.IsMotionAvailable(CombatMotion.Light)) return "靠近对手 · 鼠标左键轻击，命中一次";
            if (!defended && fighter.IsMotionAvailable(CombatMotion.Guard)) return "看他抬剑 · 面向来招，按住 Q 格挡";
            if (!heavyHit && fighter.IsMotionAvailable(CombatMotion.Heavy)) return "等他挥空 · 鼠标右键重击，命中一次";
            if (fighter.CanAnimateJump && fighter.JumpCount == 0) return "Space 跳跃 · 跳跃会消耗体力";
            if (fighter.IsMotionAvailable(CombatMotion.Skill) && fighter.SkillCount == 0) return $"看准方向 · F 地面突进斩（{fighter.skillStaminaCost:0} 体力）";
            return "击退驻守小兵 · 看清起手，等收势再反击";
        }

        private void ResolveStoryAnchors()
        {
            if (storyAnchors == null) storyAnchors = FindFirstObjectByType<InnStoryAnchors>();
            if (storyAnchors == null) return;
            if (ledgerAnchor == null) ledgerAnchor = storyAnchors.ledger;
            if (rearDoorAnchor == null) rearDoorAnchor = storyAnchors.rearDoor;
            if (rearExitAnchor == null) rearExitAnchor = storyAnchors.rearExit;
            ledgerInteractionDistance = storyAnchors.ledgerInteractionDistance;
            rearExitDistance = storyAnchors.rearExitDistance;
        }

        private void FinishReadingLedger()
        {
            if (view != View.Ledger) return;
            ledgerRead = true;
            part = StoryPart.AwaitSentry;
            Show(View.Play);
            RevealSentryAndTalk();
        }

        private void RevealSentryAndTalk()
        {
            if (!ledgerRead) return;
            if (!duel.RevealTutorialSentry())
            {
                part = StoryPart.AwaitSentry;
                Say("屏风后有人影，却被杂物挡住了。留出空地，按 E 继续。", 5f);
                return;
            }
            OpenDialogue(BeginPractice, InnStoryScript.Sentry);
        }

        private void BeginPractice()
        {
            if (duel.TutorialInstructionsCompleted) { BeginTutorialFight(); return; }
            practiceGuide.Begin(fighter, sceneCamera);
            part = StoryPart.Practice;
            input.StoryAllowsCombat = true;
            Show(View.Play);
            Say("系统提示 · 按顺序完成实际操作。\n练习期间，小兵不会主动攻击。", 5f);
        }

        private string DirectionLabel(Transform threat)
        {
            if (sceneCamera == null || threat == null) return "前方";
            Vector3 delta = threat.position - fighter.transform.position;
            Vector3 forward = Vector3.ProjectOnPlane(sceneCamera.transform.forward, Vector3.up).normalized;
            float angle = Vector3.SignedAngle(forward, Vector3.ProjectOnPlane(delta, Vector3.up), Vector3.up);
            if (Mathf.Abs(angle) <= 45f) return "前方";
            if (Mathf.Abs(angle) >= 135f) return "后方";
            return angle > 0 ? "右侧" : "左侧";
        }

        private void BeginTutorialFight()
        {
            if (!duel.BeginTutorial()) { WaitForBattle(PendingBattle.Tutorial); return; }
            practiceGuide.Dispose();
            RememberBattleStart();
            pendingBattle = PendingBattle.None;
            part = StoryPart.TutorialFight;
            input.StoryAllowsCombat = true;
            Show(View.Play);
        }

        private void BeginEliteFight()
        {
            if (!duel.BeginEliteDuel()) { WaitForBattle(PendingBattle.Elite); return; }
            RememberBattleStart();
            pendingBattle = PendingBattle.None;
            part = StoryPart.EliteFight;
            input.StoryAllowsCombat = true;
            Show(View.Play);
            Say(InnStoryScript.CombatLesson, 5f);
        }

        private void BeginRescue()
        {
            if (!duel.BeginRescueBattle()) { WaitForBattle(PendingBattle.Rescue); return; }
            pendingBattle = PendingBattle.None;
            part = StoryPart.ReturnToHelp;
            input.StoryAllowsCombat = true;
            rescueAt = Time.time;
            RememberBattleStart();
            Show(View.Play);
            FrameActors(duel.EliteActor != null ? duel.EliteActor.transform : null,
                duel.ActiveEnemy != null ? duel.ActiveEnemy.transform : null);
            watchingSkirmish = true;
            Say(InnStoryScript.RescueDiscovery, 6f);
        }

        private void WaitForBattle(PendingBattle waiting)
        {
            pendingBattle = waiting;
            part = waiting == PendingBattle.Tutorial ? StoryPart.Hall
                : waiting == PendingBattle.Elite ? StoryPart.EliteArriving : StoryPart.Departure;
            arrivalAt = float.PositiveInfinity;
            afterDialogue = null;
            lines = null;
            input.StoryAllowsCombat = false;
            duel.StopCombat();
            if (duel.PlayerActor != null) duel.PlayerActor.ControlLocked = false;
            if (waiting == PendingBattle.Rescue && entrance != null) entrance.OpenRequested = true;
            Show(View.Play);
            string reason = duel.MissingBindingReason;
            Say(string.IsNullOrWhiteSpace(reason)
                ? "暂时无法开始交手。可继续探索，按 E 重试或 R 重置。"
                : "角色动作尚未就绪：" + reason + "\n可继续探索，完成资源接入后按 E 重试。", 9f);
        }

        private void FailCurrentStage()
        {
            resultStarted = true;
            duel.StopCombat();
            input.StoryAllowsCombat = false;
            var failedPart = part;
            part = StoryPart.Finished;
            defeatStats.text = failedPart == StoryPart.TutorialFight ? "入栈受阻" : failedPart == StoryPart.EliteFight ? "交手落败" : "回援失利";
            defeatStats.text += "\n从本段重新开始，已完成的战斗保留。";
            var script = failedPart == StoryPart.TutorialFight ? InnStoryScript.TutorialDefeat
                : failedPart == StoryPart.EliteFight ? InnStoryScript.Defeat : InnStoryScript.RescueDefeat;
            OpenDialogue(() => Show(View.Defeat), script);
        }

        private void ShowVictory()
        {
            victoryStats.text = $"雨夜脱身\n战斗用时  {Mathf.RoundToInt(fightLength)} 秒";
            Show(View.Victory);
        }

        private void OpenDialogue(Action after, (string speaker, string line)[] script, bool frameActors = true)
        {
            lines = script;
            lineIndex = 0;
            afterDialogue = after;
            input.StoryAllowsCombat = false;
            Show(View.Dialogue);
            if (frameActors)
            {
                CombatActor participant = ResolveDialogueParticipant();
                Transform target = participant != null ? participant.transform : null;
                FaceDialogueParticipants(target);
                FrameActors(fighter.transform, target);
            }
            RenderLine();
        }

        private CombatActor ResolveDialogueParticipant()
        {
            if (duel.Stage == DuelEncounter.EncounterStage.Failed)
            {
                // Finished is shared by failure and the final farewell. It does
                // not mean the elite has appeared, nor that he is still hostile.
                CombatActor opponent = duel.RetryStage == DuelEncounter.EncounterStage.Tutorial ? duel.TutorialActor
                    : duel.RetryStage == DuelEncounter.EncounterStage.EliteDuel ? duel.EliteActor : null;
                if (IsLivingDialogueEnemy(opponent)) return opponent;
                if (IsLivingDialogueEnemy(duel.ActiveEnemy)) return duel.ActiveEnemy;
                CombatActor nearest = null;
                float nearestDistance = float.PositiveInfinity;
                foreach (var candidate in duel.EnemyActors)
                {
                    if (!IsLivingDialogueEnemy(candidate)) continue;
                    float distance = (candidate.transform.position - fighter.transform.position).sqrMagnitude;
                    if (distance < nearestDistance) { nearest = candidate; nearestDistance = distance; }
                }
                return nearest;
            }
            bool speaksWithElite = part == StoryPart.Departure || part == StoryPart.ReturnToHelp || part == StoryPart.Finished;
            if (speaksWithElite && duel.EliteActor != null && duel.EliteActor.gameObject.activeInHierarchy)
                return duel.EliteActor;
            CombatActor active = duel.ActiveEnemy;
            return active != null && active.gameObject.activeInHierarchy ? active : null;
        }

        private static bool IsLivingDialogueEnemy(CombatActor actor)
            => actor != null && actor.gameObject.activeInHierarchy && actor.Team == CombatTeam.Enemy && !actor.IsDefeated;

        private void FrameActors(Transform a, Transform b)
        {
            if (orbit == null || a == null || b == null) return;
            orbit.enabled = true;
            orbit.FrameActors(a, b);
        }

        private void FaceDialogueParticipants(Transform other)
        {
            if (other == null || fighter == null) return;
            Vector3 toward = Vector3.ProjectOnPlane(other.position - fighter.transform.position, Vector3.up);
            if (toward.sqrMagnitude < .001f) return;
            if (duel.PlayerActor != null && !duel.PlayerActor.IsDefeated)
            {
                fighter.PlaceForStory(fighter.transform.position, Quaternion.LookRotation(toward));
                // PlaceForStory reapplies the player's dedicated held dialogue
                // pose; do not replace it with the ordinary exploration idle.
            }
            var actor = other.GetComponent<CombatActor>();
            if (actor != null && !actor.IsDefeated)
            {
                actor.CancelAttack();
                other.rotation = Quaternion.LookRotation(-toward);
                actor.Animation?.SettleForDialogue(duel.IsCombatActive);
            }
        }

        private void AdvanceDialogue()
        {
            if (view != View.Dialogue || lines == null || afterDialogue == null) return;
            if (++lineIndex >= lines.Length)
            {
                var completed = afterDialogue;
                afterDialogue = null;
                completed();
            }
            else RenderLine();
        }

        private void RenderLine()
        {
            if (lines == null || lines.Length == 0) return;
            var line = lines[lineIndex];
            speaker.text = line.speaker;
            speaker.color = line.speaker == "红拂女" ? Plum : line.speaker == "追兵" ? Amber : Muted;
            dialogue.text = line.line;
            pageCount.text = $"{lineIndex + 1:00} / {lines.Length:00}";
        }

        private void OpenSettings()
        {
            settingsReturn = view;
            UpdateQualityLabel();
            Show(View.Settings);
        }

        private void CloseSettings() { PlayerPrefs.Save(); Show(settingsReturn); }

        private void UpdateQualityLabel()
        {
            int index = QualitySettings.GetQualityLevel();
            qualityText.text = index switch { 0 when QualitySettings.names.Length > 1 => "流畅", _ when index == QualitySettings.names.Length - 1 => "高质量", _ => "均衡" };
        }

        private RectTransform CreateView(View mode)
        {
            var layer = Fill(root, mode.ToString());
            views.Add(mode, layer);
            return layer;
        }

        private void BuildPlay()
        {
            var layer = CreateView(View.Play);
            bars = Fill(layer, "生命状态");
            (playerFill, playerValue, _, _) = Health(bars, "红拂女", Plum, TopLeft, new Vector2(36, -34));
            (rivalFill, rivalValue, rivalName, rivalPanel) = Health(bars, "敌人", Red, TopRight, new Vector2(-36, -34));
            (allyFill, allyValue, _, allyPanel) = Health(bars, "追兵 · 同伴", Amber, TopLeft, new Vector2(36, -164));
            staminaFill = Meter(bars, "体力", TopLeft, new Vector2(50, -140), new Vector2(304, 5), Amber);
            staminaValue = Label(bars, "体力", 17, Amber, TopLeft, new Vector2(50, -112), new Vector2(304, 25));
            enemyCount = Label(bars, "", 19, Paper, TopRight, new Vector2(-42, -118), new Vector2(290, 30), TextAnchor.MiddleRight);
            objective = Label(bars, "", 18, Paper, TopLeft, new Vector2(42, -250), new Vector2(390, 52));
            objective.gameObject.AddComponent<Shadow>().effectColor = Color.black;
            warning = Place(layer, "招式预警", Top, Top, new Vector2(0, -40), new Vector2(354, 96));
            Paint(warning, Ink);
            warningTitle = Label(warning, "", 25, Amber, TopLeft, new Vector2(14, -9), new Vector2(240, 36));
            warningDirection = Label(warning, "", 18, Paper, TopRight, new Vector2(-14, -12), new Vector2(66, 30), TextAnchor.MiddleRight);
            warningAction = Label(warning, "", 17, Paper, TopLeft, new Vector2(14, -48), new Vector2(326, 30));
            tellFill = Meter(warning, "起手进度", TopLeft, new Vector2(14, -83), new Vector2(326, 3), Amber);
            hint = Place(layer, "短提示", Bottom, Bottom, new Vector2(0, 34), new Vector2(700, 46));
            Paint(hint, Ink);
            hintText = Label(hint, "", 20, Paper, Center, Vector2.zero, new Vector2(680, 42), TextAnchor.MiddleCenter);
            shortLine = Label(layer, "", 21, Paper, Bottom, new Vector2(0, 98), new Vector2(980, 72), TextAnchor.MiddleCenter);
            shortLine.gameObject.AddComponent<Shadow>().effectColor = Color.black;
            Label(layer, "F1 操作   ·   Esc 暂停", 15, Paper, BottomLeft, new Vector2(30, 22), new Vector2(300, 26));
            practicePanel = Place(layer, "逐项实操", TopRight, TopRight, new Vector2(-40, -156), new Vector2(410, 120));
            Paint(practicePanel, Ink);
            practiceProgress = Label(practicePanel, "", 22, Amber, TopLeft, new Vector2(16, -12), new Vector2(378, 34));
            practiceInstruction = Label(practicePanel, "", 21, Paper, TopLeft, new Vector2(16, -51), new Vector2(378, 60));
            practicePanel.gameObject.SetActive(false);
            BuildCombatFeedback(layer);
        }

        private void BuildCombatFeedback(RectTransform layer)
        {
            // Small brackets identify a person, never imply a screen-centre sword ray.
            targetMarker = Place(layer, "当前攻击目标", Center, Center, Vector2.zero, new Vector2(30, 30));
            foreach (float x in new[] { -1f, 1f })
            foreach (float y in new[] { -1f, 1f })
            {
                AddTargetStroke(new Vector2(x * 11f, y * 15f), new Vector2(8, 2));
                AddTargetStroke(new Vector2(x * 15f, y * 11f), new Vector2(2, 8));
            }
            var offense = Place(layer, "攻击结果", TopRight, TopRight, new Vector2(-42, -158), new Vector2(286, 34));
            Paint(offense, Ink);
            attackFeedback = Label(offense, "", 21, Paper, Center, Vector2.zero,
                new Vector2(264, 32), TextAnchor.MiddleRight);
            var defense = Place(layer, "防御结果", TopLeft, TopLeft, new Vector2(42, -316), new Vector2(286, 34));
            Paint(defense, Ink);
            defenseFeedback = Label(defense, "", 21, Paper, Center, Vector2.zero,
                new Vector2(264, 32), TextAnchor.MiddleLeft);
            feedbackAudio = gameObject.AddComponent<AudioSource>();
            feedbackAudio.playOnAwake = false;
            feedbackAudio.spatialBlend = 0f;
            feedbackAudio.volume = .22f;
            // Original, short procedural cues; no externally licensed sound files.
            strikeSound = MakeCombatSound("Sword contact", 0);
            blockSound = MakeCombatSound("Sword deflected", 1);
            hurtSound = MakeCombatSound("Player struck", 2);
            ClearCombatFeedback();
        }

        private void AddTargetStroke(Vector2 position, Vector2 size)
        {
            var stroke = Paint(Place(targetMarker, "目标线", Center, Center, position, size), Paper);
            var outline = stroke.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, .85f);
            outline.effectDistance = new Vector2(1, -1);
            targetStrokes.Add(stroke);
        }

        private static AudioClip MakeCombatSound(string name, int kind)
        {
            const int sampleRate = 22050;
            float duration = kind == 1 ? .15f : .10f;
            var samples = new float[Mathf.CeilToInt(sampleRate * duration)];
            var random = new System.Random(51 + kind);
            float filtered = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / sampleRate;
                float noise = (float)random.NextDouble() * 2f - 1f;
                filtered = Mathf.Lerp(filtered, noise, kind == 1 ? .8f : .22f);
                float envelope = Mathf.Min(1f, t / .002f) * Mathf.Pow(1f - t / duration, 3f);
                float tone = kind == 1
                    ? .42f * Mathf.Sin(2f * Mathf.PI * 1470f * t) + .22f * Mathf.Sin(2f * Mathf.PI * 2210f * t)
                    : .35f * Mathf.Sin(2f * Mathf.PI * (kind == 2 ? 95f : 190f) * t);
                samples[i] = (tone + filtered * .6f) * envelope;
            }
            var clip = AudioClip.Create(name, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private (UnityEngine.UI.Image fill, UnityEngine.UI.Text value, UnityEngine.UI.Text name, RectTransform panel) Health(
            RectTransform parent, string name, Color color, Vector2 anchor, Vector2 position)
        {
            var panel = Place(parent, name, anchor, anchor, position, new Vector2(332, 72));
            Paint(panel, Ink);
            var label = Label(panel, name, 22, Paper, TopLeft, new Vector2(14, -6), new Vector2(166, 34));
            var value = Label(panel, "", 17, Muted, TopRight, new Vector2(-14, -9), new Vector2(136, 30), TextAnchor.MiddleRight);
            var fill = Meter(panel, "生命条", TopLeft, new Vector2(14, -47), new Vector2(304, 8), color);
            fill.fillOrigin = anchor == TopRight ? 1 : 0;
            return (fill, value, label, panel);
        }

        private void BuildTitle()
        {
            var layer = CreateView(View.Title);
            layer.gameObject.AddComponent<MainMenuV2>().Build(StartGame, OpenSettings, Quit);
            Label(layer, InnStoryScript.AICredit, 15, Muted, new Vector2(1, 0), new Vector2(-28, 14),
                new Vector2(460, 24), TextAnchor.MiddleRight);
        }

        private void BuildTutorial()
        {
            var frame = Panel(CreateView(View.Tutorial), "入门", "操作指引", 760, 674);
            tutorialControls = Label(frame, "", 21, Paper, TopLeft, new Vector2(39, -140), new Vector2(682, 422));
            tutorialControls.lineSpacing = 1.18f;
            Button(frame, "继续", Bottom, new Vector2(0, 30), new Vector2(246, 52), CloseTutorial, true);
        }

        private void UpdateTutorialControls()
        {
            if (tutorialControls == null) return;
            var controls = new List<string> { "WASD   跑步与转身", "鼠标   转动视角" };
            if (fighter.IsMotionAvailable(CombatMotion.Light)) controls.Add("鼠标左键   轻击");
            if (fighter.IsMotionAvailable(CombatMotion.Heavy)) controls.Add("鼠标右键   四式重击；式间可用格挡或闪避退出");
            if (fighter.IsMotionAvailable(CombatMotion.Guard)) controls.Add("Q（按住）   面敌格挡与缓步移动，消耗体力");
            if (fighter.CanAnimateJump) controls.Add("Space   跳跃，消耗体力");
            if (fighter.IsMotionAvailable(CombatMotion.Dodge)) controls.Add("Shift   闪避，消耗体力");
            if (fighter.IsMotionAvailable(CombatMotion.Skill)) controls.Add($"F   地面突进斩 · {fighter.skillStaminaCost:0} 体力 · 无冷却");
            controls.Add("E   交谈 / 推门    F1   查看操作");
            controls.Add("Esc   暂停    R   战斗中重试本段 / 探索时重新开始");
            controls.Add("敌人身上的方框表示攻击目标；右侧提示命中或被格挡。");
            controls.Add("停止消耗体力的动作，等待恢复。留意对手起手，等收势再反击。");
            tutorialControls.text = string.Join("\n", controls);
        }

        private void BuildDialogue()
        {
            var layer = CreateView(View.Dialogue);
            var frame = Place(layer, "对话框", Bottom, Bottom, new Vector2(0, 42), new Vector2(940, 205));
            Grain(frame);
            Rule(frame, TopLeft, new Vector2(0, 0), new Vector2(940, 2));
            speaker = Label(frame, "", 23, Plum, TopLeft, new Vector2(30, -14), new Vector2(330, 38));
            dialogue = Label(frame, "", 25, Paper, TopLeft, new Vector2(30, -60), new Vector2(878, 89));
            pageCount = Label(frame, "", 16, Muted, BottomLeft, new Vector2(30, 16), new Vector2(110, 30));
            Button(frame, "E  继续", new Vector2(1, 0), new Vector2(-28, 15), new Vector2(140, 36), AdvanceDialogue);
            Label(layer, InnStoryScript.AICredit, 16, Muted, BottomLeft, new Vector2(30, 10), new Vector2(520, 26));
        }

        private void BuildLedger()
        {
            var layer = CreateView(View.Ledger);
            Paint(Fill(layer, "阅读暗场"), new Color(.025f, .02f, .018f, .88f), true);
            var book = Place(layer, "展开的账本", Center, Center, new Vector2(0, 32), new Vector2(1120, 646));
            Paint(book, new Color32(87, 61, 39, 255));
            var left = Place(book, "墨污旧页", TopLeft, TopLeft, new Vector2(12, -12), new Vector2(536, 622));
            var right = Place(book, "清晰账页", TopRight, TopRight, new Vector2(-12, -12), new Vector2(536, 622));
            Paint(left, new Color32(222, 204, 166, 255));
            Paint(right, new Color32(238, 223, 190, 255));
            Color writing = new Color32(54, 42, 32, 255);
            Label(left, "逆旅流水账", 44, writing, TopLeft, new Vector2(34, -30), new Vector2(470, 60), display: true);
            Label(left, "房钱、饭食、酒钱，按日记入。\n米柴、菜盐，另列支出。", 23,
                writing, TopLeft, new Vector2(36, -128), new Vector2(466, 82), TextAnchor.UpperLeft);
            ledgerInk = MakeLedgerInk();
            // Reserve the middle of the page for the blot; neither text block
            // intersects this rectangle, even before the texture's transparent rim.
            var stain = Place(left, "空白处的墨迹", TopLeft, TopLeft, new Vector2(52, -240), new Vector2(430, 268))
                .gameObject.AddComponent<UnityEngine.UI.RawImage>();
            stain.texture = ledgerInk;
            stain.raycastTarget = false;
            Label(left, "掌柜手记：日清日结，不赊旧账。", 20, writing, BottomLeft,
                new Vector2(35, 30), new Vector2(466, 34));
            Label(right, "十月 · 日清册", 34, writing, TopLeft, new Vector2(35, -32), new Vector2(466, 50), display: true);
            Paint(Place(right, "账页横线", TopLeft, TopLeft, new Vector2(35, -99), new Vector2(466, 1)), new Color32(138, 108, 75, 255));
            string[] days = InnStoryScript.LedgerEntries.Split(new[] { "\n\n" }, StringSplitOptions.None);
            for (int i = 0; i < days.Length; i++)
            {
                // Give the interrupted latest entry its own guaranteed space;
                // a single long text box previously clipped its final sentence.
                float top = i < 3 ? 118f + i * 108f : 450f;
                var entries = Label(right, days[i], 21, writing, TopLeft,
                    new Vector2(35, -top), new Vector2(466, i < 3 ? 94 : 124), TextAnchor.UpperLeft);
                entries.lineSpacing = 1f;
            }
            var lastDrop = Place(right, "末句旁的小墨痕", TopLeft, TopLeft,
                new Vector2(215, -508), new Vector2(64, 22)).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            lastDrop.texture = ledgerInk;
            lastDrop.raycastTarget = false;
            Label(layer, "最新一笔停在半句，旁边留着一小点墨。", 21, Paper, Bottom,
                new Vector2(0, 97), new Vector2(1050, 35), TextAnchor.MiddleCenter);
            Button(layer, "读完，合上  E", Bottom, new Vector2(0, 32), new Vector2(272, 48), FinishReadingLedger, true);
        }

        private static Texture2D MakeLedgerInk()
        {
            const int size = 256;
            var pixels = new Color32[size * size];
            var blots = new[] { new Vector4(.38f, .62f, .27f, .25f), new Vector4(.67f, .44f, .24f, .19f),
                new Vector4(.36f, .33f, .21f, .18f), new Vector4(.58f, .72f, .17f, .13f), new Vector4(.73f, .68f, .08f, .07f) };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = (float)x / size, py = (float)y / size, density = -1f;
                foreach (Vector4 blot in blots)
                {
                    float dx = (px - blot.x) / blot.z, dy = (py - blot.y) / blot.w;
                    density = Mathf.Max(density, 1f - dx * dx - dy * dy);
                }
                float grain = Mathf.PerlinNoise(px * 37f + 4.1f, py * 37f + 2.4f);
                byte alpha = (byte)(Mathf.Clamp01(density * 8f + grain * .45f) * 235f);
                pixels[y * size + x] = new Color32(29, 26, 23, alpha);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Ledger ink", wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private void BuildPause()
        {
            var frame = Panel(CreateView(View.Pause), "雨歇片刻", "游戏暂停", 488, 398);
            Button(frame, "继续游戏   Esc", Top, new Vector2(0, -142), new Vector2(354, 54), () => Show(View.Play), true);
            Button(frame, "设置", Top, new Vector2(0, -211), new Vector2(354, 54), OpenSettings);
            Button(frame, "回到标题", Top, new Vector2(0, -280), new Vector2(354, 54), () => Restart(true));
        }

        private void RestoreDisplaySettings()
        {
            int modeIndex = -1;
            if (PlayerPrefs.HasKey(Pref + "fullscreenMode")) modeIndex = PlayerPrefs.GetInt(Pref + "fullscreenMode");
            else if (PlayerPrefs.HasKey(Pref + "fullscreen")) modeIndex = PlayerPrefs.GetInt(Pref + "fullscreen") != 0 ? 0 : 2;
            if (modeIndex >= 0 && modeIndex < FullscreenOptions.Length) Screen.fullScreenMode = FullscreenOptions[modeIndex];

            if (PlayerPrefs.HasKey(Pref + "resW") && PlayerPrefs.HasKey(Pref + "resH"))
            {
                int w = PlayerPrefs.GetInt(Pref + "resW");
                int h = PlayerPrefs.GetInt(Pref + "resH");
                if (w >= 640 && h >= 480) Screen.SetResolution(w, h, Screen.fullScreenMode);
            }
        }

        private static string ResolutionLabel(int index)
        {
            var r = ResolutionOptions[Mathf.Clamp(index, 0, ResolutionOptions.Length - 1)];
            return r.width + " × " + r.height;
        }

        private int ResolutionIndex()
        {
            for (int i = 0; i < ResolutionOptions.Length; i++)
                if (ResolutionOptions[i].width == Screen.width && ResolutionOptions[i].height == Screen.height) return i;
            return 2;
        }

        private int FullscreenIndex()
        {
            for (int i = 0; i < FullscreenOptions.Length; i++)
                if (FullscreenOptions[i] == Screen.fullScreenMode) return i;
            return 0;
        }

        private void PrevResolution() { ApplyDisplay(ResolutionIndex() - 1, FullscreenIndex()); UpdateResolutionLabel(); }
        private void NextResolution() { ApplyDisplay(ResolutionIndex() + 1, FullscreenIndex()); UpdateResolutionLabel(); }

        private void ApplyDisplay(int resIndex, int modeIndex)
        {
            resIndex = (resIndex + ResolutionOptions.Length) % ResolutionOptions.Length;
            modeIndex = (modeIndex + FullscreenOptions.Length) % FullscreenOptions.Length;
            var r = ResolutionOptions[resIndex];
            Screen.SetResolution(r.width, r.height, FullscreenOptions[modeIndex]);
            PlayerPrefs.SetInt(Pref + "resW", r.width);
            PlayerPrefs.SetInt(Pref + "resH", r.height);
            PlayerPrefs.SetInt(Pref + "fullscreenMode", modeIndex);
        }

        private void UpdateResolutionLabel()
        {
            if (resolutionText != null) resolutionText.text = ResolutionLabel(ResolutionIndex());
        }

        private void UpdateFullscreenLabel()
        {
            if (fullscreenText != null) fullscreenText.text = FullscreenLabels[FullscreenIndex()];
        }

        private Text StepperRow(RectTransform frame, float y, string value, Action onPrev, Action onNext)
        {
            Button(frame, "<", TopLeft, new Vector2(274, y), new Vector2(46, 38), onPrev);
            var label = Label(frame, value, 19, Paper, TopLeft, new Vector2(326, y), new Vector2(180, 38), TextAnchor.MiddleCenter);
            Button(frame, ">", TopLeft, new Vector2(512, y), new Vector2(46, 38), onNext);
            return label;
        }

        private void BuildSettings()
        {
            var frame = Panel(CreateView(View.Settings), "灯下整装", "设置", 640, 540);
            SettingLabel(frame, "总音量", -137);
            SliderRow(frame, -138, 0, 1, AudioListener.volume, value =>
            { AudioListener.volume = value; PlayerPrefs.SetFloat(Pref + "volume", value); });
            SettingLabel(frame, "画质", -190);
            var quality = Button(frame, "", TopLeft, new Vector2(274, -190), new Vector2(284, 38), () =>
            {
                QualitySettings.SetQualityLevel((QualitySettings.GetQualityLevel() + 1) % QualitySettings.names.Length, true);
                PlayerPrefs.SetInt(Pref + "quality", QualitySettings.GetQualityLevel());
                UpdateQualityLabel();
            });
            qualityText = quality.GetComponentInChildren<Text>();
            SettingLabel(frame, "分辨率", -243);
            resolutionText = StepperRow(frame, -243, ResolutionLabel(ResolutionIndex()), PrevResolution, NextResolution);
            SettingLabel(frame, "显示模式", -296);
            var fullscreen = Button(frame, "", TopLeft, new Vector2(274, -296), new Vector2(284, 38), () =>
            {
                ApplyDisplay(ResolutionIndex(), (FullscreenIndex() + 1) % FullscreenOptions.Length);
                UpdateFullscreenLabel();
            });
            fullscreenText = fullscreen.GetComponentInChildren<Text>();
            fullscreenText.text = FullscreenLabels[FullscreenIndex()];
            SettingLabel(frame, "视角灵敏度", -349);
            SliderRow(frame, -350, .04f, .32f, orbit != null ? orbit.sensitivity : .12f, value =>
            { if (orbit != null) orbit.sensitivity = value; PlayerPrefs.SetFloat(Pref + "sensitivity", value); });
            SettingLabel(frame, "垂直视角", -402);
            ToggleRow(frame, -402, orbit != null && orbit.invertY, "反转", "正常", value =>
            { if (orbit != null) orbit.invertY = value; PlayerPrefs.SetInt(Pref + "invert", value ? 1 : 0); });
            Button(frame, "返回", Bottom, new Vector2(0, 25), new Vector2(240, 44), CloseSettings, true);
        }

        private void BuildResult(bool won)
        {
            var layer = CreateView(won ? View.Victory : View.Defeat);
            var frame = Panel(layer, won ? "雨夜余声" : "客栈折剑", won ? "风尘未歇" : "落败", 600, 408);
            var result = Label(frame, won ? "交手结果  胜" : "交手结果  败\n重试本段，已完成的战斗保留。", 22, Paper,
                TopLeft, new Vector2(39, -141), new Vector2(524, 73));
            if (won) victoryStats = result;
            else defeatStats = result;
            Button(frame, won ? "再玩一次   R" : "重试本段   R", Top, new Vector2(0, -235), new Vector2(456, 52), () => Restart(false), true);
            Button(frame, won ? "回到标题" : "退出游戏", Top, new Vector2(0, -303), new Vector2(456, 48),
                won ? () => Restart(true) : Quit);
            Label(layer, InnStoryScript.AICredit, 16, Muted, BottomLeft, new Vector2(84, 37), new Vector2(520, 28));
        }

        private void TitleBackdrop(RectTransform layer)
        {
            var shade = Fill(layer, "夜色与暖灯").gameObject.AddComponent<RawImage>();
            shade.texture = titleShade;
            shade.raycastTarget = false;
            var rain = Fill(layer, "雨帘").gameObject.AddComponent<InnRain>();
            rain.raycastTarget = false;
            Rule(layer, Top, new Vector2(0, -26), new Vector2(1500, 1));
            Rule(layer, Bottom, new Vector2(0, 24), new Vector2(1500, 1));
        }

        private RectTransform Panel(RectTransform layer, string chapter, string title, float width, float height)
        {
            Paint(Fill(layer, "暗场"), new Color(0.03f, .03f, .05f, .70f), true);
            var panel = Place(layer, title, Center, Center, Vector2.zero, new Vector2(width, height));
            Grain(panel);
            Rule(panel, TopLeft, Vector2.zero, new Vector2(width, 2));
            Label(panel, chapter, 17, Amber, TopLeft, new Vector2(39, -23), new Vector2(width - 78, 30));
            Label(panel, title, 50, Paper, TopLeft, new Vector2(36, -53), new Vector2(width - 72, 65), display: true);
            Rule(panel, TopLeft, new Vector2(39, -121), new Vector2(width - 78, 1));
            return panel;
        }

        private void Grain(RectTransform frame)
        {
            Paint(frame, Wood);
            var grain = Fill(frame, "纸纹").gameObject.AddComponent<RawImage>();
            grain.texture = panelGrain;
            grain.raycastTarget = false;
        }

        private void SettingLabel(RectTransform frame, string label, float y) =>
            Label(frame, label, 22, Paper, TopLeft, new Vector2(39, y), new Vector2(196, 38));

        private void ToggleRow(RectTransform frame, float y, bool initial, string on, string off, Action<bool> changed)
        {
            bool value = initial;
            Text text = null;
            var button = Button(frame, value ? on : off, TopLeft, new Vector2(274, y), new Vector2(284, 38), () =>
            { value = !value; changed(value); text.text = value ? on : off; });
            text = button.GetComponentInChildren<Text>();
        }

        private void SliderRow(RectTransform frame, float y, float min, float max, float initial, Action<float> changed)
        {
            var holder = Place(frame, "滑杆", TopLeft, TopLeft, new Vector2(274, y), new Vector2(222, 38));
            var slider = holder.gameObject.AddComponent<Slider>();
            var track = Place(holder, "底轨", Center, Center, Vector2.zero, new Vector2(218, 6));
            Paint(track, Line, true);
            var slide = Fill(holder, "滑动区域");
            slide.offsetMin = new Vector2(9, 0);
            slide.offsetMax = new Vector2(-9, 0);
            var handle = Place(slide, "滑钮", Center, Center, Vector2.zero, new Vector2(18, 26));
            slider.targetGraphic = Paint(handle, Amber, true);
            slider.handleRect = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = initial;
            var valueText = Label(frame, SliderLabel(initial, min, max), 18, Muted, TopLeft, new Vector2(508, y), new Vector2(61, 38), TextAnchor.MiddleRight);
            slider.onValueChanged.AddListener(value => { changed(value); valueText.text = SliderLabel(value, min, max); });
        }

        private static string SliderLabel(float value, float min, float max) => $"{Mathf.RoundToInt(Mathf.InverseLerp(min, max, value) * 100)}%";

        private Button Button(RectTransform parent, string title, Vector2 anchor, Vector2 pos, Vector2 size, Action onClick, bool primary = false)
        {
            var rect = Place(parent, title, anchor, anchor, pos, size);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = Paint(rect, Color.white, true);
            button.colors = new ColorBlock
            {
                normalColor = primary ? new Color32(112, 70, 61, 255) : new Color32(48, 37, 37, 255),
                highlightedColor = new Color32(129, 87, 70, 255),
                selectedColor = new Color32(129, 87, 70, 255),
                pressedColor = new Color32(89, 53, 53, 255),
                disabledColor = new Color32(48, 43, 43, 255),
                colorMultiplier = 1f, fadeDuration = .12f
            };
            Rule(rect, TopLeft, Vector2.zero, new Vector2(size.x, 1));
            Label(rect, title, size.y < 42 ? 19 : 22, Paper, Center, Vector2.zero, size - new Vector2(8, 2), TextAnchor.MiddleCenter);
            button.onClick.AddListener(() => onClick());
            return button;
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private Text Label(Transform parent, string text, int size, Color color, Vector2 anchor, Vector2 pos, Vector2 dimensions,
            TextAnchor alignment = TextAnchor.MiddleLeft, bool display = false)
        {
            var item = Place(parent, text.Length == 0 ? "文字" : text, anchor, anchor, pos, dimensions).gameObject.AddComponent<Text>();
            item.font = display && titleFont != null ? titleFont : bodyFont;
            item.fontSize = size;
            item.text = text;
            item.color = color;
            item.alignment = alignment;
            item.raycastTarget = false;
            return item;
        }

        private static Image Meter(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var track = Place(parent, name, anchor, anchor, pos, size);
            Paint(track, new Color32(78, 63, 62, 255));
            var fill = Paint(Fill(track, "进度"), color);
            fill.sprite = WhiteSprite();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            return fill;
        }

        private static Sprite whiteSprite;
        private static Sprite WhiteSprite() => whiteSprite != null ? whiteSprite : whiteSprite = Sprite.Create(Texture2D.whiteTexture,
            new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), Center);

        private static void Rule(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size) =>
            Paint(Place(parent, "细线", anchor, anchor, pos, size), Line);

        private static Image Paint(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static RectTransform Place(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform Fill(Transform parent, string name)
        {
            var rect = Place(parent, name, Vector2.zero, Center, Vector2.zero, Vector2.zero);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Texture2D MakeTexture(bool title)
        {
            const int width = 256, height = 128;
            var pixels = new Color32[width * height];
            var random = new System.Random(29);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (float)x / (width - 1), v = (float)y / (height - 1);
                float grain = (float)random.NextDouble() * .055f;
                if (title)
                {
                    float edge = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.24f, .82f, u));
                    float glow = Mathf.Exp(-15f * ((u - .74f) * (u - .74f) + (v - .53f) * (v - .53f)));
                    pixels[y * width + x] = new Color(.052f + .16f * glow + grain, .057f + .067f * glow + grain,
                        .082f + grain, Mathf.Clamp01(.96f - edge * .64f + .22f * Mathf.Abs(v - .5f)));
                }
                else pixels[y * width + x] = new Color(.64f, .47f, .33f, grain + .028f * Mathf.PerlinNoise(u * 7, v * 42));
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply(false, true);
            return texture;
        }
    }
}


