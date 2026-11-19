using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Actual Stage12 UI callbacks, practice observation and live animation frames.
// Battle outcomes alone are advanced by ReceiveDamage, never claimed as combat-feel validation.
public static class Stage12StoryPlaythrough
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] public sealed class StateSample
    {
        public string phase, stage, view, part;
        public int frame, playerHealth, playerMax, allyHealth, allyMax, enemies, practiceStep;
        public float time;
        public bool ledgerRead, sentryVisible, instructionsComplete, allyProtected, playerPoseLocked;
        public bool rearDoorOpen;
        public Vector3 playerPosition;
    }
    [Serializable] public sealed class BattleStartSample
    {
        public string stage, source = "DuelEncounter.StageChanged synchronous callback";
        public int frame, occurrence, playerHealth, playerMax, allyHealth, allyMax;
        public float time;
        public bool combatActive, fullHealth, rescueEntranceInProgress;
    }
    [Serializable] public sealed class PracticeSample
    {
        public int step, startFrame, endFrame, requests, executedActions, mouseDeltaFrames, injectedDynamicFrames;
        public float startTime, endTime, measuredMovement, measuredLookAngle, maximumJumpRise;
        public bool accepted, groundedAtEnd;
        public string motion;
        public Vector3 startPosition;
        public float initialOrbitYaw, lastOrbitYaw, measuredOrbitYaw;
        public int unlockedCursorFrames, framingFrames, lookFocusRecoveries;
        public string initialCursor, lastCursor, cameraName, guideCameraName;
    }
    [Serializable] public sealed class LookSample
    {
        public int frame, lastInjectedDynamicFrame;
        public string cursor, cursorAfterRecovery, focusedWindow;
        public bool applicationFocused, orbitEnabled, framing, hasTarget, dialoguePoseLocked;
        public float yaw, renderedAngleSincePreviousFrame, currentCameraDistance;
        public Vector3 cameraPosition, cameraForward;
        public Vector2 observedMouseDelta;
    }
    [Serializable] public sealed class DialogueSample
    {
        public string phase, view, firstText, playerMotion, participant;
        public int startFrame, endFrame;
        public bool playerAlive, poseLockedAtStart, poseLockedAtEnd;
        public float maximumBoneDrift;
        public Vector3 playerFeet, leftFoot, rightFoot;
        public List<string> lines = new();
    }
    [Serializable] public sealed class Screenshot
    {
        public string path;
        public int requestedFrame;
        public bool written;
    }
    [Serializable] public sealed class Report
    {
        public string scope = "Existing Stage12 scene. Ledger/E/result callbacks are real UI entry points; movement, synthetic mouse delta supplied through InputState.Change in InputSystem Dynamic onAfterUpdate, public action requests and animation completion are observed over real frames. Mouse input is injected in practice step 1 and as a fresh click after 18 seconds on the epilogue. AI Updates are temporarily suspended except during the outside cover phase and real front-door reinforcement entrance, when normal AI must clear the doorway and continue the cover battle. Full-health entry checks sample the synchronous formal battle StageChanged callback, excluding RescueCover. ReceiveDamage deliberately advances battle outcomes and explicitly tests the ally health floor. Position fixtures precede observations, including legal locations on both sides of the existing rear door and near the ally; E/anchor callbacks and actual door rotation time are still used. These fixtures are not a manual traversal/pathfinding acceptance. No tutorial counters or completion flags are forged, no scene is created/saved. The Editor-only SuppressFocusPauseForVerification switch is temporarily enabled and restored to isolate external focus loss; actual UI initialized/update remains enabled. This is not a verification of normal focus-pause behavior. Final rear departure must reach an independent Epilogue view before Victory.";
        public string phase = "NotStarted", scene, outputFolder, resultText;
        public bool active, completed, passed, retryEachStage, framesAdvanced, gameTimeAdvanced, returnedToTitle;
        public bool departedThroughRear, coverBeganOutside, returnedThroughRear, joinedAfterReturn, finalDeparture, epilogueSeen;
        public bool focusPauseSuppressionBefore, focusPauseSuppressionDuring, focusPauseSuppressionRestored;
        public bool keyboardEnabledBefore, mouseEnabledBefore, keyboardEnabledDuring, mouseEnabledDuring, diagnosticDevicesRestored;
        public string editorInputRoutingBefore, editorInputRoutingDuring, editorInputRoutingAfter;
        public bool editorInputRoutingRestored;
        public float epilogueSeconds;
        public int checks, frameStart, frameEnd, runtimeErrors, focusPauseResumes, battlesObserved, retriesObserved;
        public float timeStart, timeEnd, elapsedSeconds, eliteEntranceTravel;
        public List<string> successes = new(), failures = new();
        public List<StateSample> states = new();
        public List<BattleStartSample> battleStarts = new();
        public List<PracticeSample> practice = new();
        public List<LookSample> lookSamples = new();
        public List<DialogueSample> dialogues = new();
        public List<Screenshot> screenshots = new();
    }
    static Report report = new();
    static InnInterface ui;
    static PrototypeFighter fighter;
    static PrototypeInput input;
    static DuelEncounter duel;
    static CombatActor player;
    static readonly Dictionary<EnemyCombatAI, bool> brains = new();
    static readonly HashSet<DuelEncounter.EncounterStage> observedBattles = new(), retriedBattles = new();
    static DuelEncounter.EncounterStage battle;
    static PracticeSample practice;
    static DialogueSample dialogue;
    static Vector3 previousPracticePosition, previousViewForward, entrancePrevious, practiceMoveWorld;
    static Vector3[] dialogueBones;
    static bool entranceObserved, oldBackground, changingPhase;
    static bool oldFocusPauseSuppression, focusPauseSuppressionSaved;
    static bool protectingEntrancePlayer, entrancePlayerWasInvulnerable;
    static InputSettings.BackgroundBehavior oldInputBackground;
    static InputSettings.EditorInputBehaviorInPlayMode oldEditorInputRouting;
    static Keyboard savedKeyboard;
    static Mouse savedMouse;
    static bool diagnosticDevicesSaved, oldKeyboardEnabled, oldMouseEnabled;
    static double beganAt, phaseAt, lastDialogueAdvance;
    static int lastFrame, phaseFrame, practiceWarmupUntil;
    static string previousDialogueLine;
    static float nextBattleActionAt;
    static double epilogueBegan;
    static int epilogueClickPhase;
    static bool epilogueLoopChecked;
    static bool epilogueGuardChecked;
    static bool victoryScreenshotRequested;
    static Button resultClickTarget;
    static int resultClickPhase;
    static int victoryCountAtStart;
    static int nextLookFocusRecoveryFrame;
    static int lastLookInputFrame = -1;
    static string View => Get(ui, "view").ToString();
    static string Part => Get(ui, "part").ToString();

    [Unity.Pipeline.Commands.CliCommand("innstage12", "begin_story_test", "Verify ledger, real practice, three-stage story and result in the existing Stage12")]
    public static string Begin() => BeginInternal(false);
    [Unity.Pipeline.Commands.CliCommand("innstage12", "begin_story_retry_test", "Verify the Stage12 flow with one actual failure/result retry per battle")]
    public static string BeginWithRetries() => BeginInternal(true);
    [Unity.Pipeline.Commands.CliCommand("innstage12", "story_test_status", "Read actual Stage12 story/practice verification progress")]
    public static string Status() { if (report.active) Observe(); return JsonUtility.ToJson(report, true); }
    [Unity.Pipeline.Commands.CliCommand("innstage12", "cancel_story_test", "Cancel the live flow fixture and return to title")]
    public static string Cancel() { if (report.active) Fail("Cancelled by operator."); return Status(); }

    static string BeginInternal(bool retries)
    {
        if (report.active) return Status();
        Detach(); RestoreDiagnosticDevices(); brains.Clear(); observedBattles.Clear(); retriedBattles.Clear();
        fighter = null; ui = null; player = null; dialogue = null; practice = null;
        entranceObserved = false; changingPhase = false; protectingEntrancePlayer = false; lastLookInputFrame = -1;
        epilogueClickPhase = 0;
        epilogueLoopChecked = false;
        epilogueGuardChecked = false;
        victoryScreenshotRequested = false;
        resultClickTarget = null;
        resultClickPhase = 0;
        report = new Report { retryEachStage = retries, frameStart = Time.frameCount, timeStart = Time.time };
        report.scope += " Keyboard/mouse enabled states are saved; disabled devices are explicitly enabled only after the test selects IgnoreFocus and checked before proceeding. Original device enabled states are independently restored on every exit. Synthetic mouse events do not verify hardware input or normal focus-loss handling.";
        report.scope += " Editor input routing is temporarily AllDeviceInputAlwaysGoesToGameView and independently restored; the InputSettings asset is never saved by this fixture.";
        beganAt = EditorApplication.timeSinceStartup;
        oldBackground = Application.runInBackground;
        oldInputBackground = InputSystem.settings.backgroundBehavior;
        oldEditorInputRouting = InputSystem.settings.editorInputBehaviorInPlayMode;
        report.editorInputRoutingBefore = oldEditorInputRouting.ToString();
        try
        {
            Require(EditorApplication.isPlaying && !EditorApplication.isPaused, "Start unpaused Play Mode in the existing Stage12 first.");
            report.scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Require(report.scene.IndexOf("Stage12", StringComparison.OrdinalIgnoreCase) >= 0, "Expected existing Stage12, found " + report.scene);
            report.outputFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Stage12Story_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + (retries ? "_Retry" : "")));
            Directory.CreateDirectory(report.outputFolder);
            fighter = Object.FindFirstObjectByType<PrototypeFighter>();
            ui = Object.FindFirstObjectByType<InnInterface>();
            Require(fighter != null && ui != null, "Actual player/UI missing.");
            oldFocusPauseSuppression = ui.SuppressFocusPauseForVerification;
            focusPauseSuppressionSaved = true;
            report.focusPauseSuppressionBefore = oldFocusPauseSuppression;
            ui.SuppressFocusPauseForVerification = true;
            report.focusPauseSuppressionDuring = ui.SuppressFocusPauseForVerification;
            input = fighter.GetComponent<PrototypeInput>(); duel = fighter.GetComponent<DuelEncounter>();
            Require(input != null && duel != null, "Actual input/encounter missing.");
            Require(Keyboard.current != null && Mouse.current != null, "Actual Input System keyboard and mouse are required.");
            savedKeyboard = Keyboard.current; savedMouse = Mouse.current;
            oldKeyboardEnabled = report.keyboardEnabledBefore = savedKeyboard.enabled;
            oldMouseEnabled = report.mouseEnabledBefore = savedMouse.enabled;
            diagnosticDevicesSaved = true;
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            report.editorInputRoutingDuring = InputSystem.settings.editorInputBehaviorInPlayMode.ToString();
            Require(InputSystem.settings.editorInputBehaviorInPlayMode == InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView,
                "Diagnostic Editor routing did not enable GameView delivery; refusing a no-input verification.");
            if (!savedKeyboard.enabled) InputSystem.EnableDevice(savedKeyboard);
            if (!savedMouse.enabled) InputSystem.EnableDevice(savedMouse);
            report.keyboardEnabledDuring = savedKeyboard.enabled; report.mouseEnabledDuring = savedMouse.enabled;
            Require(report.keyboardEnabledDuring && report.mouseEnabledDuring,
                "Diagnostic keyboard/mouse remain disabled after explicit enabling; refusing a no-input verification.");
            Type gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameView != null) EditorWindow.GetWindow(gameView).Focus();
            Call("Restart", true); player = duel.PlayerActor;
            victoryCountAtStart = UiVictoryAudio.Instance != null ? UiVictoryAudio.Instance.VictoryCount : 0;
            Require(duel.HasRequiredBindings, duel.MissingBindingReason);
            Require(ui.ledgerAnchor != null && ui.rearDoorAnchor != null && ui.rearExitAnchor != null && ui.storyAnchors != null,
                "Stage12 ledger/rear-door/rear-exit anchors must be installed first.");
            Require(Mouse.current != null, "A real Input System mouse device is required for the camera practice step.");
            Check(!duel.TutorialActor.gameObject.activeInHierarchy && !duel.EliteActor.gameObject.activeInHierarchy && !ui.LedgerRead,
                "Title/reset begins with unread ledger and hidden opponents");
            report.active = true;
            duel.StageChanged += OnBattleStageChanged;
            fighter.ActionPerformed += OnAction;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
            InputSystem.onAfterUpdate += FeedPracticeMouse;
            InputSystem.onAfterUpdate += FeedEpilogueClick;
            InputSystem.onAfterUpdate += FeedResultClick;
            EditorApplication.playModeStateChanged += VerificationPlayState;
            AssemblyReloadEvents.beforeAssemblyReload += VerificationReload;
            Call("StartGame");
            input.enabled = false;
            PlaceNear(ui.ledgerAnchor.position, ui.ledgerInteractionDistance - .15f);
            Call("UpdateStoryMovement", false); // Actual Road -> Hall threshold, never assign the story enum.
            Require(Part == "Hall", "Ledger fixture did not lie on the actual hall side of the entrance: " + Part);
            SetPhase("OpenLedger");
        }
        catch (Exception e) { Fail(e.GetBaseException().ToString()); }
        return Status();
    }

    static void Tick()
    {
        if (!report.active) return;
        try
        {
            Observe();
            if (!EditorApplication.isPlaying) { Fail("Play Mode ended early."); return; }
            if (report.elapsedSeconds > (report.retryEachStage ? 300f : 240f)) { Fail("Live workflow timeout."); return; }
            if (EditorApplication.isPaused) { Fail("Editor was paused externally."); return; }
            // Product focus handling stays unchanged; this autonomous fixture resumes only its own live run.
            if (View == "Pause")
            {
                ShowPlay(); report.focusPauseResumes++;
                if (report.focusPauseResumes > 25) { Fail("Repeated external focus loss prevents stable live verification."); return; }
            }
            input.enabled = false; // Commands below traverse the same public fighter requests as PrototypeInput.
            SuspendBrains();
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            double age = EditorApplication.timeSinceStartup - phaseAt;
            if (age > 30d) { Fail("Phase stalled: " + report.phase + ", view " + View + ", part " + Part); return; }
            if (View == "Dialogue" || View == "Ledger") ObserveDialogue();
            switch (report.phase)
            {
                case "OpenLedger":
                    if (Time.frameCount < phaseFrame + 3 || !fighter.IsGrounded) return;
                    Call("UpdateStoryMovement", true);
                    Require(View == "Ledger", "Actual E callback did not open ledger view.");
                    Check(!duel.TutorialActor.gameObject.activeInHierarchy && !ui.LedgerRead, "Opening ledger does not reveal the sentry before reading it");
                    Capture("01_Ledger"); SetPhase("ReadLedger");
                    break;
                case "ReadLedger":
                    if (Time.frameCount < phaseFrame + 4) return;
                    CloseDialogueObservation();
                    Call("FinishReadingLedger");
                    Require(ui.LedgerRead && duel.TutorialSentryRevealed && View == "Dialogue", "Read/E callback did not reveal sentry and begin dialogue.");
                    Check(!duel.IsCombatActive && duel.TutorialActor.Invulnerable, "Revealed sentry remains passive while dialogue/practice begin");
                    Capture("02_SentryDialogue"); SetPhase("SentryDialogue");
                    break;
                case "SentryDialogue":
                    if (View == "Dialogue") { AdvanceSpokenLine(); return; }
                    Require(ui.IsPracticing && ui.PracticeStep == 0, "Sentry dialogue must enter the real practice guide.");
                    PreparePracticePosition(); SetPhase("Practice");
                    break;
                case "Practice":
                    DrivePractice();
                    if (duel.Stage != DuelEncounter.EncounterStage.Tutorial) return;
                    CompletePracticeSample();
                    Check(ui.PracticeStep == 8 && duel.TutorialInstructionsCompleted && report.practice.Count == 8,
                        "All eight real practice steps complete before the first battle");
                    Check(!player.IsAttacking && fighter.IsGrounded, "Final skill has finished and landed before combat starts");
                    Capture("03_TutorialBattle"); EnterBattle();
                    break;
                case "Battle":
                    if (battle == DuelEncounter.EncounterStage.Rescue && duel.RescueEntranceInProgress) return;
                    if (Time.time < nextBattleActionAt) return;
                    if (report.retryEachStage && !retriedBattles.Contains(battle))
                    {
                        retriedBattles.Add(battle);
                        var source = duel.EnemyActors.First(a => a != null && !a.IsDefeated && a.Team == CombatTeam.Enemy);
                        player.Invulnerable = false;
                        Require(player.ReceiveDamage(source, player.MaxHealth + 1, 0f, 0f, true), "Failure injection rejected.");
                        Check(duel.Stage == DuelEncounter.EncounterStage.Failed, battle + " player defeat stops the current battle");
                        SetPhase("FailureDialogue");
                        return;
                    }
                    foreach (var enemy in duel.EnemyActors.ToArray())
                        if (enemy != null && enemy.Team == CombatTeam.Enemy && !enemy.IsDefeated)
                            Require(enemy.ReceiveDamage(player, enemy.MaxHealth + 1, 0f, 0f, true), "Progression damage rejected by " + enemy.name);
                    if (battle == DuelEncounter.EncounterStage.Tutorial)
                    {
                        Check(duel.Stage == DuelEncounter.EncounterStage.EliteReady, "Tutorial victory advances to elite arrival");
                        PlaceNear(duel.eliteArrivalPosition, 2.8f); SetPhase("EliteArrival");
                    }
                    else if (battle == DuelEncounter.EncounterStage.EliteDuel) SetPhase("EliteVictoryDialogue");
                    else SetPhase("FinalDialogue");
                    break;
                case "FailureDialogue":
                    if (View == "Dialogue") { AdvanceSpokenLine(); return; }
                    if (View != "Defeat") return;
                    CloseDialogueObservation(); Capture("Failure_" + battle);
                    SetPhase("FailureCapture");
                    break;
                case "FailureCapture":
                    if (Time.frameCount < phaseFrame + 4) return;
                    Call("Restart", false); input.enabled = false;
                    Check(duel.Stage == battle && player.Health == player.MaxHealth && duel.IsCombatActive,
                        battle + " result retry restores only this battle at full player health");
                    if (battle == DuelEncounter.EncounterStage.Rescue)
                    {
                        var originalJoin = report.battleStarts.FirstOrDefault(s => s.stage == DuelEncounter.EncounterStage.Rescue.ToString());
                        Check(originalJoin != null && duel.EliteActor.Health == originalJoin.allyHealth
                            && duel.EliteActor.MaxHealth == originalJoin.allyMax
                            && duel.EliteActor.GruntDamageHealthFloor == 1 && duel.RemainingEnemies == 5,
                            "Rescue retry restores five enemies and the ally health recorded at the actual Join checkpoint");
                    }
                    report.retriesObserved++; EnterBattle();
                    break;
                case "EliteArrival":
                    ObserveEntrance();
                    if (View != "Dialogue") return;
                    Check(duel.EliteEntranceComplete && !duel.EliteEntranceStoppedAtDoor && report.eliteEntranceTravel > .5f,
                        "Elite completes the actual doorway locomotion before dialogue");
                    Capture("04_EliteDialogue"); SetPhase("EliteDialogue");
                    break;
                case "EliteDialogue":
                    if (View == "Dialogue") { AdvanceSpokenLine(); return; }
                    Require(duel.Stage == DuelEncounter.EncounterStage.EliteDuel, "Elite dialogue did not start the elite battle.");
                    EnterBattle();
                    break;
                case "EliteVictoryDialogue":
                    if (View == "Dialogue") { AdvanceSpokenLine(); return; }
                    if (Part != "Departure" || duel.EliteActor.Team != CombatTeam.Player) return;
                    CloseDialogueObservation();
                    Check(duel.EliteActor.Health == 900 && duel.EliteActor.MaxHealth == 900, "Won elite becomes a protected 900-health ally");
                    Check(!duel.IsCombatActive && !duel.RescueCoverActive && !(bool)Get(ui, "hasHelped"),
                        "After the elite dialogue the player must leave first; cover battle has not begun");
                    Capture("05_RearDeparture");
                    PlaceNear(ui.rearDoorAnchor.position - RearOutward() * .65f, .1f);
                    Call("UpdateStoryMovement", true);
                    Check(ui.storyAnchors.RearDoorOpen, "Actual E callback opens rear door for first departure");
                    SetPhase("FirstRearDoorOpening");
                    break;
                case "FirstRearDoorOpening":
                    if (age < 1.6d) return;
                    PlaceNear(ui.rearExitAnchor.position, .1f);
                    Call("UpdateStoryMovement", false);
                    Require(Part == "ReturnToHelp" && duel.RescueCoverActive, "Actual outside rear anchor did not start the cover phase");
                    report.departedThroughRear = !(bool)Call("IsInsideRearDoor");
                    report.coverBeganOutside = report.departedThroughRear && !duel.IsCombatActive && !(bool)Get(ui, "hasHelped");
                    Check(report.coverBeganOutside, "Cover starts while the player is outside and has not joined the third battle");
                    Check(!ui.storyAnchors.RearDoorOpen, "Rear door closes after first departure");
                    Capture("06_CoverBeginsOutside"); SetPhase("FrontDoorOpening");
                    break;
                case "FrontDoorOpening":
                    if (!duel.RescueCoverActive || duel.RescueEntranceInProgress) return;
                    Check(duel.RescueEnteredCount == 5 && duel.EnemyActors.Count(a => a != null && a.Team == CombatTeam.Enemy && !a.IsDefeated) == 5,
                        "Five living reinforcements complete their actual front-door entrance during cover");
                    Check(duel.EliteActor.GruntDamageHealthFloor == 1 && !duel.EliteActor.IsDefeated && !duel.IsCombatActive,
                        "Protected ally holds off the reinforcements before the player joins");
                    SetPhase("ObserveCover");
                    break;
                case "ObserveCover":
                    if (age < .7d) return;
                    Check(Part == "ReturnToHelp" && !duel.IsCombatActive && !(bool)Get(ui, "hasHelped"),
                        "Waiting outside does not silently count as joining the rescue battle");
                    // Stay clear of the shut door leaf/capsule overlap while
                    // remaining inside the real 2m interaction radius.
                    PlaceNear(ui.rearDoorAnchor.position + RearOutward() * 1.35f, .1f);
                    Call("UpdateStoryMovement", true);
                    Check(ui.storyAnchors.RearDoorOpen, "Actual E callback reopens rear door from outside for the return");
                    SetPhase("ReturnRearDoorOpening");
                    break;
                case "ReturnRearDoorOpening":
                    if (age < 1.6d) return;
                    PlaceNear(duel.EliteActor.transform.position, 1.6f);
                    report.returnedThroughRear = (bool)Call("IsInsideRearDoor");
                    Require(report.returnedThroughRear, "Legal return fixture is not inside the rear door plane");
                    Call("UpdateStoryMovement", false);
                    Require(View == "Dialogue", "Returning inside near the ally did not begin the actual support dialogue");
                    Check(!duel.IsCombatActive && !(bool)Get(ui, "hasHelped"), "Support dialogue precedes formal player participation");
                    Capture("07_ReturnSupportDialogue"); SetPhase("ReturnSupportDialogue");
                    break;
                case "ReturnSupportDialogue":
                    if (View == "Dialogue") { AdvanceSpokenLine(); return; }
                    Require(duel.Stage == DuelEncounter.EncounterStage.Rescue && Part == "JointFight", "Support dialogue did not start the formal rescue battle");
                    report.joinedAfterReturn = report.returnedThroughRear && (bool)Get(ui, "hasHelped");
                    Check(report.joinedAfterReturn, "Formal rescue begins only after returning and finishing the support dialogue");
                    CheckBattleStartHealth(DuelEncounter.EncounterStage.Rescue, "Joining rescue restores full player health at the synchronous stage event");
                    Check(!ui.storyAnchors.RearDoorOpen, "Rear door closes at the formal rescue checkpoint");
                    var attacker = duel.EnemyActors.First(a => a != null && !a.IsDefeated);
                    Require(duel.EliteActor.ReceiveDamage(attacker, duel.EliteActor.MaxHealth * 2, 0f, 0f, true), "Ally protection probe rejected.");
                    Check(duel.EliteActor.Health == 1 && !duel.EliteActor.IsDefeated && duel.Stage == DuelEncounter.EncounterStage.Rescue,
                        "Lethal grunt damage cannot kill the rescue ally or terminate the joint fight");
                    Capture("08_JointFight"); EnterBattle();
                    break;
                case "FinalDialogue":
                    if (View == "Dialogue") { AdvanceSpokenLine(); return; }
                    if (Part != "FinalDeparture" || View != "Play") return;
                    CloseDialogueObservation();
                    Check(duel.Stage == DuelEncounter.EncounterStage.Complete && duel.EliteActor.Team == CombatTeam.Player
                        && !duel.EliteActor.IsDefeated, "Joint victory reaches farewell and a playable final departure with the ally alive");
                    Check(report.dialogues.All(d => d.lines.All(l => !l.Contains("风尘未歇"))), "Epilogue text is not a page in the dialogue array");
                    PlaceNear(ui.rearDoorAnchor.position - RearOutward() * .65f, .1f);
                    Call("UpdateStoryMovement", true);
                    Check(ui.storyAnchors.RearDoorOpen, "Actual E callback opens the rear door for the final departure");
                    Capture("09_FinalRearDeparture"); SetPhase("FinalRearDoorOpening");
                    break;
                case "FinalRearDoorOpening":
                    if (age < 1.6d) return;
                    PlaceNear(ui.rearExitAnchor.position, .1f); Call("UpdateStoryMovement", false);
                    report.finalDeparture = !(bool)Call("IsInsideRearDoor");
                    Require(report.finalDeparture && View == "Epilogue", "Leaving by the rear anchor did not begin the independent epilogue");
                    report.epilogueSeen = true; epilogueBegan = EditorApplication.timeSinceStartup;
                    Capture("10_IndependentEpilogue"); SetPhase("Epilogue");
                    break;
                case "Epilogue":
                    if (View == "Epilogue" && EditorApplication.timeSinceStartup - epilogueBegan >= 1.5d
                        && !epilogueGuardChecked)
                    {
                        epilogueGuardChecked = true;
                        Check(epilogueClickPhase >= 2, "An early held mouse press cannot skip the first-second input guard");
                    }
                    if (View == "Epilogue" && EditorApplication.timeSinceStartup - epilogueBegan >= 1.6d
                        && !report.screenshots.Any(s => s.path.EndsWith("10b_EpilogueVisible.png", StringComparison.Ordinal)))
                        Capture("10b_EpilogueVisible");
                    if (View == "Epilogue" && EditorApplication.timeSinceStartup - epilogueBegan >= 8d
                        && !report.screenshots.Any(s => s.path.EndsWith("10c_EpilogueHeldTitle.png", StringComparison.Ordinal)))
                    {
                        var epilogueLayer = ui.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r => r.name == "Epilogue");
                        Require(epilogueLayer != null, "Epilogue view missing");
                        Check(((UnityEngine.UI.Text)Get(ui, "epilogueTitle")).color.a >= .99f,
                            "Epilogue title remains visible while waiting for the click");
                        Check(Mathf.Abs(((UnityEngine.UI.Text)Get(ui, "epilogueTitle")).rectTransform.anchoredPosition.x) < .01f,
                            "Epilogue title is horizontally centered");
                        Check(!epilogueLayer.GetComponentsInChildren<UnityEngine.UI.Text>(true)
                            .Any(t => t.text.Contains("AI 辅助制作")),
                            "Epilogue has no AI assistance credit");
                        Capture("10c_EpilogueHeldTitle");
                    }
                    if (View == "Epilogue" && EditorApplication.timeSinceStartup - epilogueBegan >= 17d
                        && !epilogueLoopChecked)
                    {
                        epilogueLoopChecked = true;
                        Check(UiVictoryAudio.Instance != null && UiVictoryAudio.Instance.VictoryCount == victoryCountAtStart + 1
                            && UiVictoryAudio.Instance.VictoryPlaying,
                            "Epilogue remains open beyond the 16-second Victory B clip with one looping music cue");
                    }
                    if (View != "Victory") return;
                    report.epilogueSeconds = (float)(EditorApplication.timeSinceStartup - epilogueBegan);
                    Check(epilogueGuardChecked && epilogueClickPhase >= 6 && report.epilogueSeconds >= 18f,
                        "Independent epilogue waits for a new click after the music loop and ignores a double-click");
                    report.resultText = ((UnityEngine.UI.Text)Get(ui, "victoryStats")).text;
                    Check(report.resultText.Contains("战斗用时  " + Mathf.RoundToInt((float)Get(ui, "completedFightLength")) + " 秒"),
                        "Victory statistics use the battle duration frozen at final departure");
                    SetPhase("ResultCapture");
                    break;
                case "ResultCapture":
                    if (age < .75d) return;
                    if (!victoryScreenshotRequested)
                    {
                        victoryScreenshotRequested = true;
                        Capture("11_Victory");
                        phaseFrame = Time.frameCount;
                        return;
                    }
                    if (Time.frameCount < phaseFrame + 4) return;
                    var victoryFade = (UnityEngine.CanvasGroup)Get(ui, "victoryFade");
                    Check(victoryFade.alpha >= .99f && victoryFade.blocksRaycasts
                        && !((UnityEngine.UI.Image)Get(ui, "resultCurtain")).gameObject.activeSelf,
                        "Victory result finishes its fade-in with clickable buttons");
                    Check(UiVictoryAudio.Instance != null && !UiVictoryAudio.Instance.VictoryPlaying,
                        "Victory music has fully faded before result interaction");
                    var resultLayer = ui.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r => r.name == "Victory");
                    Require(resultLayer != null, "Victory view missing");
                    Check(!resultLayer.GetComponentsInChildren<UnityEngine.UI.Text>(true)
                        .Any(t => t.text.Contains("AI 辅助制作")),
                        "Victory result has no AI assistance credit");
                    var defeatLayer = ui.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r => r.name == "Defeat");
                    Check(defeatLayer != null && !defeatLayer.GetComponentsInChildren<UnityEngine.UI.Text>(true)
                        .Any(t => t.text.Contains("AI 辅助制作")),
                        "Defeat result has no AI assistance credit");
                    var buttons = resultLayer.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                    var replay = buttons.FirstOrDefault(b => b.name.Contains("再玩一次"));
                    var title = buttons.FirstOrDefault(b => b.name.Contains("回到标题"));
                    Require(replay != null && title != null, "Victory result buttons missing");
                    Check(PointerHits(replay) && PointerHits(title), "Victory buttons receive the top UI raycast");
                    resultClickTarget = replay;
                    resultClickPhase = 0;
                    SetPhase("ResultReplayPointer");
                    break;
                case "ResultReplayPointer":
                    if (View != "Play") return;
                    Check(resultClickPhase >= 2 && Part == "Road" && duel.Stage == DuelEncounter.EncounterStage.Arrival,
                        "Mouse click on Replay resets the story and encounter");
                    Call("ShowVictory");
                    resultClickTarget = ui.GetComponentsInChildren<UnityEngine.UI.Button>(true)
                        .First(b => b.name.Contains("回到标题") && b.transform.parent.name == "Victory");
                    resultClickPhase = 0;
                    SetPhase("ResultTitlePointer");
                    break;
                case "ResultTitlePointer":
                    if (View != "Title") return;
                    Check(resultClickPhase >= 2, "Mouse click on Return to Title opens the formal main menu");
                    var titleLayer = ui.GetComponentsInChildren<RectTransform>(true).First(r => r.name == "Title");
                    Check(!titleLayer.GetComponentsInChildren<UnityEngine.UI.Text>(true)
                        .Any(t => t.text.Contains("AI 辅助制作"))
                        && titleLayer.GetComponentsInChildren<UnityEngine.UI.RawImage>(true)
                            .Any(i => i.name == "Art 5" && i.texture != null),
                        "Title keeps only the lower-left illustrated AI credit");
                    Capture("12_TitleSingleCredit");
                    Finish();
                    break;
            }
        }
        catch (Exception e) { Fail(e.GetBaseException().ToString()); }
    }

    static void PreparePracticePosition()
    {
        fighter.ClearInput();
        // Move the fixture before measurement, then let the guide observe three stationary frames.
        var motor = fighter.GetComponent<CharacterController>();
        var placement = typeof(CombatActor).Assembly.GetType("FengChenInn.ActorPlacement").GetMethod("TryPlace", BindingFlags.Static | BindingFlags.NonPublic);
        bool found = false;
        foreach (float radius in new[] { 2f, 3f, 4f })
        {
            for (int pointIndex = 0; pointIndex < 8 && !found; pointIndex++)
            {
                Vector3 point = duel.tutorialPosition + Quaternion.Euler(0f, pointIndex * 45f, 0f) * Vector3.forward * radius;
                point.y = fighter.transform.position.y;
                if (!(bool)placement.Invoke(null, new object[] { motor, point, Quaternion.identity, float.NaN })) continue;
                for (int directionIndex = 0; directionIndex < 8 && !found; directionIndex++)
                {
                    Vector3 direction = Quaternion.Euler(0f, directionIndex * 45f, 0f) * Vector3.forward;
                    Vector3 center = motor.transform.TransformPoint(motor.center);
                    float testRadius = motor.radius + .015f;
                    Vector3 ends = Vector3.up * Mathf.Max(0f, motor.height * .5f - testRadius);
                    bool clear = true;
                    foreach (var hit in Physics.CapsuleCastAll(center - ends, center + ends, testRadius, direction, 1.7f, ~0, QueryTriggerInteraction.Ignore))
                        if (!hit.transform.IsChildOf(fighter.transform)) { clear = false; break; }
                    if (!clear) continue;
                    bool supported = false;
                    Vector3 destination = fighter.transform.position + direction * 1.7f;
                    foreach (var hit in Physics.RaycastAll(destination + Vector3.up * .25f, Vector3.down, .65f, ~0, QueryTriggerInteraction.Ignore))
                        if (hit.collider.GetComponentInParent<CombatActor>() == null && hit.normal.y > .7f) { supported = true; break; }
                    if (!supported) continue;
                    practiceMoveWorld = direction; found = true;
                    fighter.PlaceForStory(fighter.transform.position, Quaternion.LookRotation(direction));
                }
            }
            if (found) break;
        }
        Require(found, "No existing clear 1.7m practice corridor; the fixture did not move environment geometry.");
        practiceWarmupUntil = Time.frameCount + 3;
        previousPracticePosition = fighter.transform.position;
        previousViewForward = Camera.main.transform.forward;
        practice = null;
    }
    static Vector3 RearOutward() => Vector3.ProjectOnPlane(ui.rearExitAnchor.position - ui.rearDoorAnchor.position, Vector3.up).normalized;
    static void DrivePractice()
    {
        if (!ui.IsPracticing) return;
        if (Time.frameCount <= practiceWarmupUntil) { fighter.ClearInput(); return; }
        int step = ui.PracticeStep;
        if (step >= 8) return;
        if (practice == null || practice.step != step)
        {
            CompletePracticeSample(); fighter.ClearInput();
            practice = new PracticeSample { step = step, startFrame = Time.frameCount, startTime = Time.time, startPosition = fighter.transform.position };
            report.practice.Add(practice);
            previousPracticePosition = fighter.transform.position; previousViewForward = Camera.main.transform.forward;
            if (step == 1)
            {
                var orbit = Camera.main.GetComponent<OrbitCamera>();
                Require(orbit != null, "Practice camera has no OrbitCamera component.");
                practice.initialOrbitYaw = practice.lastOrbitYaw = (float)Get(orbit, "yaw");
                practice.initialCursor = Cursor.lockState.ToString();
                practice.cameraName = Camera.main.name + "#" + Camera.main.GetInstanceID();
                var guideCamera = Get(Get(ui, "practiceGuide"), "view") as Camera;
                practice.guideCameraName = guideCamera != null ? guideCamera.name + "#" + guideCamera.GetInstanceID() : "Missing";
                nextLookFocusRecoveryFrame = 0;
                RecoverLookInput();
            }
        }
        Vector3 position = fighter.transform.position, viewForward = Camera.main.transform.forward;
        if (step == 0 && fighter.MoveInput.sqrMagnitude > .01f)
            practice.measuredMovement += Vector3.ProjectOnPlane(position - previousPracticePosition, Vector3.up).magnitude;
        if (step == 1)
        {
            // EditorApplication.update may observe a different Input System update
            // than OrbitCamera/InnPracticeGuide. Keep input sightings separate from
            // actual rendered angular movement; never infer rotation from queued delta.
            if (Mouse.current.delta.ReadValue().sqrMagnitude > .04f) practice.mouseDeltaFrames++;
            float renderedAngle = Vector3.Angle(previousViewForward, viewForward);
            practice.measuredLookAngle += renderedAngle;
            var orbit = Camera.main.GetComponent<OrbitCamera>();
            float yaw = (float)Get(orbit, "yaw");
            practice.measuredOrbitYaw += Mathf.Abs(Mathf.DeltaAngle(practice.lastOrbitYaw, yaw));
            practice.lastOrbitYaw = yaw;
            practice.lastCursor = Cursor.lockState.ToString();
            if (Cursor.lockState != CursorLockMode.Locked) practice.unlockedCursorFrames++;
            if (orbit.IsFramingActors) practice.framingFrames++;
            var sample = new LookSample { frame = Time.frameCount, cursor = Cursor.lockState.ToString(),
                lastInjectedDynamicFrame = lastLookInputFrame, observedMouseDelta = Mouse.current.delta.ReadValue(),
                focusedWindow = EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.GetType().Name : "None",
                applicationFocused = Application.isFocused, orbitEnabled = orbit.enabled, framing = orbit.IsFramingActors,
                hasTarget = orbit.target != null, dialoguePoseLocked = fighter.IsDialoguePoseActive,
                yaw = yaw, renderedAngleSincePreviousFrame = renderedAngle, currentCameraDistance = orbit.CurrentDistance,
                cameraPosition = Camera.main.transform.position, cameraForward = viewForward };
            bool gameViewFocused = EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.GetType().Name == "GameView";
            if ((Cursor.lockState != CursorLockMode.Locked || !gameViewFocused)
                && Time.frameCount >= nextLookFocusRecoveryFrame) RecoverLookInput();
            sample.cursorAfterRecovery = Cursor.lockState.ToString();
            if (report.lookSamples.Count < 180) report.lookSamples.Add(sample);
        }
        if (step == 6) practice.maximumJumpRise = Mathf.Max(practice.maximumJumpRise, position.y - practice.startPosition.y);
        previousPracticePosition = position; previousViewForward = viewForward;
        switch (step)
        {
            case 0:
                // The same camera-relative MoveInput used by WASD. No transform edits while measured.
                Vector3 forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                fighter.MoveInput = new Vector2(Vector3.Dot(practiceMoveWorld, right), Vector3.Dot(practiceMoveWorld, forward));
                fighter.RunRequested = true;
                break;
            case 1:
                fighter.MoveInput = Vector2.zero;
                // Injection is synchronized with the actual Dynamic input update
                // in FeedPracticeMouse, never with this Editor update callback.
                break;
            case 4:
                fighter.GuardRequested = true;
                break;
            default:
                fighter.GuardRequested = false;
                int requiredRequests = step == 3 && fighter.useSequentialHeavy ? 4 : 1;
                if (practice.requests >= requiredRequests || !fighter.IsGrounded || fighter.State != PrototypeFighter.ActionState.Ready
                    || Time.time < (float)Get(fighter, "landingUntil") || player.IsStunned) return;
                if (step == 5 && fighter.Stamina < fighter.dodgeStaminaCost || step == 6 && fighter.Stamina < fighter.jumpStaminaCost
                    || step == 7 && fighter.Stamina < fighter.skillStaminaCost) return;
                practice.requests++;
                practice.accepted = step == 2 ? fighter.Attack(false) : step == 3 ? fighter.Attack(true)
                    : step == 5 ? fighter.Dodge() : step == 6 ? fighter.Jump() : fighter.Skill();
                Require(practice.accepted, "Actual practice request rejected at step " + step);
                break;
        }
    }
    static void FeedPracticeMouse()
    {
        if (!report.active || InputState.currentUpdateType != InputUpdateType.Dynamic
            || lastLookInputFrame == Time.frameCount || ui == null || !ui.IsPracticing || ui.PracticeStep != 1
            || practice == null || practice.step != 1 || View != "Play" || Time.timeScale <= 0f || Mouse.current == null) return;
        // A background native pointer event can replace a queued mouse delta.
        // Supply the synthetic device state after the Dynamic update, before
        // ordinary OrbitCamera and guide callbacks. Never set camera angles or
        // tutorial progress; those still advance through normal runtime code.
        InputState.Change(Mouse.current.delta, new Vector2(16f, 0f));
        lastLookInputFrame = Time.frameCount;
        practice.injectedDynamicFrames++;
    }
    static void FeedEpilogueClick()
    {
        if (!report.active || InputState.currentUpdateType != InputUpdateType.Dynamic || Mouse.current == null
            || report.phase != "Epilogue" || View != "Epilogue" || epilogueClickPhase >= 7) return;
        double age = EditorApplication.timeSinceStartup - epilogueBegan;
        if (epilogueClickPhase == 0 && age < .2d || epilogueClickPhase == 1 && age < 1.2d
            || epilogueClickPhase == 2 && age < 18d) return;
        var state = new MouseState { position = Mouse.current.position.ReadValue() }
            .WithButton(MouseButton.Left, epilogueClickPhase == 0 || epilogueClickPhase == 3 || epilogueClickPhase == 5);
        InputState.Change(Mouse.current, state);
        epilogueClickPhase++;
    }
    static bool PointerHits(Button button)
    {
        if (EventSystem.current == null) return false;
        Canvas.ForceUpdateCanvases();
        var pointer = new PointerEventData(EventSystem.current)
        {
            position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position)
        };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        return hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(button.transform);
    }
    static void FeedResultClick()
    {
        if (!report.active || InputState.currentUpdateType != InputUpdateType.Dynamic || Mouse.current == null
            || resultClickTarget == null || resultClickPhase >= 2
            || report.phase != "ResultReplayPointer" && report.phase != "ResultTitlePointer") return;
        if (report.phase == "ResultTitlePointer" && EditorApplication.timeSinceStartup - phaseAt < .85d) return;
        Vector2 position = RectTransformUtility.WorldToScreenPoint(null, resultClickTarget.transform.position);
        var state = new MouseState { position = position }.WithButton(MouseButton.Left, resultClickPhase == 0);
        InputState.Change(Mouse.current, state);
        resultClickPhase++;
    }
    static void RecoverLookInput()
    {
        // Harness recovery only: normal Show(Play) already resumes follow. Do not
        // write camera yaw/pitch, call its Update, or change tutorial measurements.
        Type gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gameView != null) EditorWindow.GetWindow(gameView).Focus();
        PrototypeInput.SetCursor(true);
        practice.lookFocusRecoveries++;
        nextLookFocusRecoveryFrame = Time.frameCount + 6;
    }
    static void CompletePracticeSample()
    {
        if (practice == null || practice.endFrame != 0) return;
        practice.endFrame = Time.frameCount; practice.endTime = Time.time; practice.groundedAtEnd = fighter.IsGrounded;
        if (practice.step >= 2 && practice.step != 4)
            {
                int expected = practice.step == 3 && fighter.useSequentialHeavy ? 4 : 1;
                Check(practice.requests == expected && practice.executedActions == expected,
                    "Practice step " + practice.step + " uses " + expected + " successful real actions");
            }
    }
    static void OnAction(CombatMotion motion)
    {
        if (!report.active || practice == null || !ui.IsPracticing || practice.step != ui.PracticeStep) return;
        CombatMotion expected = practice.step == 2 ? CombatMotion.Light : practice.step == 3 ? CombatMotion.Heavy
            : practice.step == 4 ? CombatMotion.Guard : practice.step == 5 ? CombatMotion.Dodge
            : practice.step == 6 ? CombatMotion.JumpStart : practice.step == 7 ? CombatMotion.Skill : CombatMotion.Idle;
        if (motion == expected) { practice.executedActions++; practice.motion = motion.ToString(); }
    }
    static void EnterBattle()
    {
        CloseDialogueObservation(); fighter.ClearInput(); input.enabled = false;
        battle = duel.Stage;
        Require(duel.IsCombatActive, "Expected an actual running battle.");
        if (observedBattles.Add(battle))
        {
            report.battlesObserved++;
            // Rescue reaches this fixture phase only after the live entrance,
            // which can include several seconds of legitimate enemy damage.
            CheckBattleStartHealth(battle, battle + " begins with full player health");
        }
        SuspendBrains();
        nextBattleActionAt = Time.time + .45f;
        SetPhase("Battle");
    }
    static void OnBattleStageChanged(DuelEncounter.EncounterStage stage)
    {
        if (!report.active || (stage != DuelEncounter.EncounterStage.Tutorial
            && stage != DuelEncounter.EncounterStage.EliteDuel && stage != DuelEncounter.EncounterStage.Rescue)) return;
        CombatActor actor = duel.PlayerActor;
        string stageName = stage.ToString();
        var sample = new BattleStartSample { stage = stageName, frame = Time.frameCount, time = Time.time,
            occurrence = report.battleStarts.Count(s => s.stage == stageName) + 1,
            playerHealth = actor != null ? actor.Health : 0, playerMax = actor != null ? actor.MaxHealth : 0,
            allyHealth = duel.EliteActor != null ? duel.EliteActor.Health : 0, allyMax = duel.EliteActor != null ? duel.EliteActor.MaxHealth : 0,
            fullHealth = actor != null && actor.MaxHealth > 0 && actor.Health == actor.MaxHealth,
            combatActive = duel.IsCombatActive, rescueEntranceInProgress = duel.RescueEntranceInProgress };
        report.battleStarts.Add(sample);
        Check(sample.combatActive && sample.fullHealth,
            stage + " start callback " + sample.occurrence + " has full player health");
    }
    static void CheckBattleStartHealth(DuelEncounter.EncounterStage stage, string message)
    {
        var sample = report.battleStarts.LastOrDefault(s => s.stage == stage.ToString());
        Check(sample != null && sample.combatActive && sample.fullHealth
            && sample.playerMax > 0 && sample.playerHealth == sample.playerMax, message);
    }
    static void ObserveEntrance()
    {
        if (duel.EliteActor == null || !duel.EliteActor.gameObject.activeInHierarchy) return;
        Vector3 position = duel.EliteActor.transform.position;
        if (entranceObserved) report.eliteEntranceTravel += Vector3.ProjectOnPlane(position - entrancePrevious, Vector3.up).magnitude;
        entrancePrevious = position; entranceObserved = true;
    }

    static void ObserveDialogue()
    {
        if (dialogue == null)
        {
            dialogue = new DialogueSample { phase = report.phase, view = View, startFrame = Time.frameCount,
                playerAlive = !player.IsDefeated, poseLockedAtStart = player.Animation.IsDialoguePoseActive,
                playerMotion = player.Animation.CurrentMotion.ToString(), playerFeet = player.transform.position };
            dialogueBones = BonePositions();
            dialogue.leftFoot = dialogueBones[1]; dialogue.rightFoot = dialogueBones[2];
            var participant = Call("ResolveDialogueParticipant") as CombatActor;
            dialogue.participant = participant != null ? participant.name : "None";
            report.dialogues.Add(dialogue); previousDialogueLine = "";
        }
        Vector3[] current = BonePositions();
        for (int i = 0; i < current.Length; i++) dialogue.maximumBoneDrift = Mathf.Max(dialogue.maximumBoneDrift, Vector3.Distance(current[i], dialogueBones[i]));
        dialogue.poseLockedAtEnd = player.Animation.IsDialoguePoseActive; dialogue.endFrame = Time.frameCount;
        if (View == "Dialogue")
        {
            string line = ((UnityEngine.UI.Text)Get(ui, "speaker")).text + ": " + ((UnityEngine.UI.Text)Get(ui, "dialogue")).text;
            if (line != previousDialogueLine)
            {
                dialogue.lines.Add(line); previousDialogueLine = line;
                if (dialogue.firstText == null) dialogue.firstText = line;
            }
        }
    }
    static Vector3[] BonePositions()
    {
        Animator animator = player.Animation.targetAnimator;
        Vector3 Position(HumanBodyBones bone)
        { Transform target = animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null; return target != null ? target.position : player.transform.position; }
        return new[] { Position(HumanBodyBones.Hips), Position(HumanBodyBones.LeftFoot), Position(HumanBodyBones.RightFoot) };
    }
    static void AdvanceSpokenLine()
    {
        if (dialogue == null || Time.frameCount < dialogue.startFrame + 4 || EditorApplication.timeSinceStartup - lastDialogueAdvance < .12d) return;
        lastDialogueAdvance = EditorApplication.timeSinceStartup;
        Call("AdvanceDialogue"); input.enabled = false;
        if (View != "Dialogue") CloseDialogueObservation();
    }
    static void CloseDialogueObservation()
    {
        if (dialogue == null) return;
        Check(dialogue.poseLockedAtStart && dialogue.poseLockedAtEnd && dialogue.maximumBoneDrift < .005f,
            dialogue.phase + " holds the actual player dialogue stance across rendered frames");
        dialogue = null; dialogueBones = null;
    }
    static void PlaceNear(Vector3 anchor, float radius)
    {
        fighter.ClearInput();
        var motor = fighter.GetComponent<CharacterController>();
        var placement = typeof(CombatActor).Assembly.GetType("FengChenInn.ActorPlacement").GetMethod("TryPlace", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (float distance in new[] { radius, Mathf.Min(radius, .9f) })
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = anchor + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * distance;
                // Test setup must use the destination floor level. The rear
                // yard is lower than the hall; carrying its current height to
                // a hall candidate makes the floor probe start underground.
                point.y = anchor.y;
                Vector3 toward = Vector3.ProjectOnPlane(anchor - point, Vector3.up);
                Quaternion facing = toward.sqrMagnitude > .001f ? Quaternion.LookRotation(toward) : fighter.transform.rotation;
                if (!(bool)placement.Invoke(null, new object[] { motor, point, facing, float.NaN })) continue;
                if (Vector3.ProjectOnPlane(fighter.transform.position - anchor, Vector3.up).magnitude > radius + .05f) continue;
                fighter.PlaceForStory(fighter.transform.position, facing);
                Physics.SyncTransforms(); return;
            }
        throw new InvalidOperationException("No legal existing-floor fixture near anchor " + anchor);
    }
    static void SuspendBrains()
    {
        bool entering = duel != null && (duel.RescueCoverActive || duel.Stage == DuelEncounter.EncounterStage.Rescue && duel.RescueEntranceInProgress);
        if (entering && !protectingEntrancePlayer)
        {
            entrancePlayerWasInvulnerable = player.Invulnerable;
            protectingEntrancePlayer = true;
            player.Invulnerable = true;
        }
        else if (!entering && protectingEntrancePlayer)
        {
            player.Invulnerable = entrancePlayerWasInvulnerable;
            protectingEntrancePlayer = false;
        }
        foreach (var actor in CombatActor.Actors.ToArray())
        {
            if (actor == null) continue;
            var brain = actor.GetComponent<EnemyCombatAI>();
            if (brain == null) continue;
            if (!brains.ContainsKey(brain)) brains.Add(brain, brain.enabled);
            // The first arrival must be free to approach its target and vacate
            // the threshold. Disabling it here blocks the real queue behind it.
            // Encounter logic still controls which outside actors may fight.
            if (entering) { brain.enabled = brains[brain]; continue; }
            brain.enabled = false; actor.CancelAttack();
        }
    }
    static void Capture(string name)
    {
        string path = Path.Combine(report.outputFolder, name + ".png");
        var pixels = ScreenCapture.CaptureScreenshotAsTexture();
        if (pixels != null)
        {
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(pixels);
        }
        report.screenshots.Add(new Screenshot { path = path, requestedFrame = Time.frameCount });
    }
    static void SetPhase(string phase)
    {
        report.phase = phase; phaseAt = EditorApplication.timeSinceStartup; phaseFrame = lastFrame = Time.frameCount;
        State();
    }
    static void State()
    {
        report.states.Add(new StateSample { phase = report.phase, stage = duel.Stage.ToString(), view = View, part = Part,
            frame = Time.frameCount, time = Time.time, playerHealth = player.Health, playerMax = player.MaxHealth,
            allyHealth = duel.EliteActor != null ? duel.EliteActor.Health : 0, allyMax = duel.EliteActor != null ? duel.EliteActor.MaxHealth : 0,
            enemies = duel.RemainingEnemies, practiceStep = ui.PracticeStep, ledgerRead = ui.LedgerRead,
            playerPosition = player.transform.position, rearDoorOpen = ui.storyAnchors != null && ui.storyAnchors.RearDoorOpen,
            sentryVisible = duel.TutorialActor.gameObject.activeInHierarchy, instructionsComplete = duel.TutorialInstructionsCompleted,
            allyProtected = duel.EliteActor != null && duel.EliteActor.GruntDamageHealthFloor > 0,
            playerPoseLocked = player.Animation.IsDialoguePoseActive });
    }
    static void Observe()
    {
        report.frameEnd = Time.frameCount; report.timeEnd = Time.time;
        report.framesAdvanced = report.frameEnd > report.frameStart + 5; report.gameTimeAdvanced = report.timeEnd > report.timeStart + .1f;
        report.elapsedSeconds = (float)(EditorApplication.timeSinceStartup - beganAt);
    }
    static void Check(bool value, string description)
    { report.checks++; (value ? report.successes : report.failures).Add(description); }
    static void Require(bool value, string description) { if (!value) throw new InvalidOperationException(description); }
    static object Get(object target, string field) => target.GetType().GetField(field, Hidden).GetValue(target);
    static object Call(string method, params object[] args) => typeof(InnInterface).GetMethod(method, Hidden).Invoke(ui, args);
    static void ShowPlay()
    {
        var view = typeof(InnInterface).GetNestedType("View", BindingFlags.NonPublic);
        Call("Show", Enum.Parse(view, "Play")); input.enabled = false;
    }
    static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        report.runtimeErrors++;
        if (report.failures.Count < 20) report.failures.Add(message + "\n" + stack);
    }
    static void Fail(string reason) { report.failures.Add(reason); Finish(); }
    static void VerificationPlayState(PlayModeStateChange state)
    { if (state == PlayModeStateChange.ExitingPlayMode && report.active) Fail("Play Mode ended during story verification."); }
    static void VerificationReload()
    { if (report.active) Fail("Assembly reload interrupted story verification."); else { RestoreFocusPauseGuard(); RestoreDiagnosticDevices(); } }
    static void Finish()
    {
        if (changingPhase) return;
        changingPhase = true;
        Observe(); report.active = false; report.completed = true;
        Detach();
        try
        {
            if (fighter != null) fighter.ClearInput();
            foreach (var pair in brains) if (pair.Key != null) pair.Key.enabled = pair.Value;
            brains.Clear();
            if (EditorApplication.isPlaying && ui != null)
            { Call("Restart", true); report.returnedToTitle = View == "Title"; }
        }
        catch (Exception e) { report.failures.Add("Restore title: " + e.GetBaseException().Message); }
        Application.runInBackground = oldBackground;
        InputSystem.settings.backgroundBehavior = oldInputBackground;
        try
        {
            InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInputRouting;
            report.editorInputRoutingAfter = InputSystem.settings.editorInputBehaviorInPlayMode.ToString();
            report.editorInputRoutingRestored = InputSystem.settings.editorInputBehaviorInPlayMode == oldEditorInputRouting;
        }
        catch (Exception ex) { report.failures.Add("Restore Editor input routing: " + ex.GetBaseException().Message); }
        RestoreDiagnosticDevices();
        if (report.battlesObserved != 3 || report.practice.Count != 8 || !report.framesAdvanced || !report.gameTimeAdvanced)
            report.failures.Add("The complete three-battle/live-practice sequence did not finish.");
        if (report.retryEachStage && report.retriesObserved != 3) report.failures.Add("Expected one actual result retry per battle.");
        if (!report.departedThroughRear || !report.coverBeganOutside || !report.returnedThroughRear || !report.joinedAfterReturn
            || !report.finalDeparture || !report.epilogueSeen)
            report.failures.Add("Rear departure, outside cover, voluntary return, formal rescue and final epilogue sequence did not all complete.");
        foreach (var screenshot in report.screenshots)
        {
            screenshot.written = File.Exists(screenshot.path) && new FileInfo(screenshot.path).Length > 0;
            if (!screenshot.written) report.failures.Add("Screenshot was not written: " + screenshot.path);
        }
        if (!report.focusPauseSuppressionRestored) report.failures.Add("Editor focus-pause verification guard did not restore its prior value.");
        if (!report.diagnosticDevicesRestored) report.failures.Add("Diagnostic keyboard/mouse enabled states did not restore their original values.");
        if (!report.editorInputRoutingRestored) report.failures.Add("Diagnostic Editor input routing did not restore its original value.");
        report.passed = report.failures.Count == 0 && report.runtimeErrors == 0 && report.returnedToTitle;
        report.phase = report.passed ? "Completed" : "Failed";
        if (string.IsNullOrEmpty(report.outputFolder)) report.outputFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Stage12Story_PreconditionFailure"));
        Directory.CreateDirectory(report.outputFolder);
        File.WriteAllText(Path.Combine(report.outputFolder, "report.json"), JsonUtility.ToJson(report, true));
        changingPhase = false;
    }
    static void Detach()
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        InputSystem.onAfterUpdate -= FeedPracticeMouse;
        InputSystem.onAfterUpdate -= FeedEpilogueClick;
        InputSystem.onAfterUpdate -= FeedResultClick;
        EditorApplication.playModeStateChanged -= VerificationPlayState;
        AssemblyReloadEvents.beforeAssemblyReload -= VerificationReload;
        if (duel != null) duel.StageChanged -= OnBattleStageChanged;
        if (fighter != null) fighter.ActionPerformed -= OnAction;
        RestoreFocusPauseGuard();
    }
    static void RestoreFocusPauseGuard()
    {
        if (!focusPauseSuppressionSaved) return;
        if (ui != null) ui.SuppressFocusPauseForVerification = oldFocusPauseSuppression;
        report.focusPauseSuppressionRestored = ui == null || ui.SuppressFocusPauseForVerification == oldFocusPauseSuppression;
        focusPauseSuppressionSaved = false;
    }
    static void RestoreDiagnosticDevices()
    {
        if (!diagnosticDevicesSaved) return;
        try
        {
            if (savedKeyboard != null && savedKeyboard.added) { if (oldKeyboardEnabled) InputSystem.EnableDevice(savedKeyboard); else InputSystem.DisableDevice(savedKeyboard); }
        }
        catch (Exception ex) { report.failures.Add("Restore keyboard enabled state: " + ex.GetBaseException().Message); }
        try
        {
            if (savedMouse != null && savedMouse.added) { if (oldMouseEnabled) InputSystem.EnableDevice(savedMouse); else InputSystem.DisableDevice(savedMouse); }
        }
        catch (Exception ex) { report.failures.Add("Restore mouse enabled state: " + ex.GetBaseException().Message); }
        report.diagnosticDevicesRestored = (savedKeyboard == null || !savedKeyboard.added || savedKeyboard.enabled == oldKeyboardEnabled)
            && (savedMouse == null || !savedMouse.added || savedMouse.enabled == oldMouseEnabled);
        diagnosticDevicesSaved = false;
    }
}
