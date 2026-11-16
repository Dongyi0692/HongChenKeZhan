using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
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
    }
    [Serializable] public sealed class PracticeSample
    {
        public int step, startFrame, endFrame, requests, executedActions, mouseDeltaFrames;
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
        public int frame;
        public string cursor, cursorAfterRecovery, focusedWindow;
        public bool applicationFocused, orbitEnabled, framing, hasTarget, dialoguePoseLocked;
        public float yaw, renderedAngleSincePreviousFrame, currentCameraDistance;
        public Vector3 cameraPosition, cameraForward;
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
        public string scope = "Existing Stage12 scene. Ledger/E/result callbacks are real UI entry points; movement, queued mouse delta, public action requests and animation completion are observed over real frames. AI Updates are temporarily suspended; ReceiveDamage deliberately advances battle outcomes. Position fixtures precede observations. No tutorial counters or completion flags are forged, no scene is created/saved. Background pause recovery is harness-only and logged.";
        public string phase = "NotStarted", scene, outputFolder, resultText;
        public bool active, completed, passed, retryEachStage, framesAdvanced, gameTimeAdvanced, returnedToTitle;
        public int checks, frameStart, frameEnd, runtimeErrors, focusPauseResumes, battlesObserved, retriesObserved;
        public float timeStart, timeEnd, elapsedSeconds, eliteEntranceTravel;
        public List<string> successes = new(), failures = new();
        public List<StateSample> states = new();
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
    static InputSettings.BackgroundBehavior oldInputBackground;
    static double beganAt, phaseAt, lastDialogueAdvance;
    static int lastFrame, phaseFrame, practiceWarmupUntil;
    static string previousDialogueLine;
    static float nextBattleActionAt;
    static int nextLookFocusRecoveryFrame;
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
        Detach(); brains.Clear(); observedBattles.Clear(); retriedBattles.Clear();
        fighter = null; ui = null; player = null; dialogue = null; practice = null;
        entranceObserved = false; changingPhase = false;
        report = new Report { retryEachStage = retries, frameStart = Time.frameCount, timeStart = Time.time };
        beganAt = EditorApplication.timeSinceStartup;
        oldBackground = Application.runInBackground;
        oldInputBackground = InputSystem.settings.backgroundBehavior;
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
            input = fighter.GetComponent<PrototypeInput>(); duel = fighter.GetComponent<DuelEncounter>();
            Require(input != null && duel != null, "Actual input/encounter missing.");
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Type gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameView != null) EditorWindow.GetWindow(gameView).Focus();
            Call("Restart", true); player = duel.PlayerActor;
            Require(duel.HasRequiredBindings, duel.MissingBindingReason);
            Require(ui.ledgerAnchor != null && ui.rearDoorAnchor != null && ui.rearExitAnchor != null && ui.storyAnchors != null,
                "Stage12 ledger/rear-door/rear-exit anchors must be installed first.");
            Require(Mouse.current != null, "A real Input System mouse device is required for the camera practice step.");
            Check(!duel.TutorialActor.gameObject.activeInHierarchy && !duel.EliteActor.gameObject.activeInHierarchy && !ui.LedgerRead,
                "Title/reset begins with unread ledger and hidden opponents");
            report.active = true;
            fighter.ActionPerformed += OnAction;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
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
            if (report.elapsedSeconds > (report.retryEachStage ? 210f : 150f)) { Fail("Live workflow timeout."); return; }
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
                    Call("Restart", false); input.enabled = false;
                    Check(duel.Stage == battle && player.Health == player.MaxHealth && duel.IsCombatActive,
                        battle + " result retry restores only this battle at full player health");
                    if (battle == DuelEncounter.EncounterStage.Rescue)
                        Check(duel.EliteActor.Health == 600 && duel.EliteActor.GruntDamageHealthFloor == 1 && duel.RemainingEnemies == 5,
                            "Rescue retry restores five enemies and the protected 600-health ally");
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
                    Check(duel.EliteActor.Health == 600 && duel.EliteActor.MaxHealth == 600, "Won elite becomes a 600-health ally");
                    PlaceNear(ui.rearDoorAnchor.position, ui.storyAnchors.rearDoorDistance - .15f);
                    Call("UpdateStoryMovement", true);
                    Check(ui.storyAnchors.RearDoorOpen, "Rear-door E callback requests the authored door to open");
                    Capture("05_RearDoor"); SetPhase("RearDoorOpening");
                    break;
                case "RearDoorOpening":
                    if (age < 1d) return;
                    PlaceNear(ui.rearExitAnchor.position, ui.rearExitDistance - .15f);
                    Call("UpdateStoryMovement", false);
                    Require(duel.Stage == DuelEncounter.EncounterStage.Rescue, "Actual rear-exit threshold did not start rescue.");
                    Check(duel.RemainingEnemies == 5 && duel.EliteActor.Health == 600 && duel.EliteActor.GruntDamageHealthFloor == 1,
                        "Rear exit starts five hostile grunts against the protected ally");
                    Check(player.Health == player.MaxHealth, "Rescue entry restores full player health");
                    SetPhase("ReturnToHelp");
                    break;
                case "ReturnToHelp":
                    if (age < 2.3d) return;
                    PlaceNear(duel.EliteActor.transform.position, 2.8f);
                    Call("UpdateStoryMovement", false);
                    Require(Part == "JointFight", "Actual proximity after the skirmish did not enter the joint fight.");
                    var attacker = duel.EnemyActors.First(a => a != null && !a.IsDefeated);
                    Require(duel.EliteActor.ReceiveDamage(attacker, duel.EliteActor.MaxHealth * 2, 0f, 0f, true), "Ally protection probe rejected.");
                    Check(duel.EliteActor.Health == 1 && !duel.EliteActor.IsDefeated && duel.Stage == DuelEncounter.EncounterStage.Rescue,
                        "Lethal grunt damage cannot kill the rescue ally or terminate the joint fight");
                    Capture("06_JointFight"); EnterBattle();
                    break;
                case "FinalDialogue":
                    if (View == "Dialogue") { AdvanceSpokenLine(); return; }
                    if (View != "Victory") return;
                    CloseDialogueObservation();
                    report.resultText = ((UnityEngine.UI.Text)Get(ui, "victoryStats")).text;
                    Check(duel.Stage == DuelEncounter.EncounterStage.Complete && duel.EliteActor.Team == CombatTeam.Player
                        && !duel.EliteActor.IsDefeated, "Joint victory reaches the farewell/result with the ally alive");
                    Capture("07_Victory"); SetPhase("ResultCapture");
                    break;
                case "ResultCapture":
                    if (Time.frameCount >= phaseFrame + 4) Finish();
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
                // Feed the real Input System delta consumed by OrbitCamera and InnPracticeGuide.
                InputSystem.QueueDeltaStateEvent(Mouse.current.delta, new Vector2(16f, 0f));
                break;
            case 4:
                fighter.GuardRequested = true;
                break;
            default:
                fighter.GuardRequested = false;
                if (practice.requests != 0 || !fighter.IsGrounded || fighter.State != PrototypeFighter.ActionState.Ready
                    || Time.time < (float)Get(fighter, "landingUntil") || player.IsStunned) return;
                if (step == 5 && fighter.Stamina < fighter.dodgeStaminaCost || step == 6 && fighter.Stamina < fighter.jumpStaminaCost
                    || step == 7 && fighter.Stamina < fighter.skillStaminaCost) return;
                practice.requests++;
                practice.accepted = step == 2 ? fighter.Attack(false) : step == 3 ? fighter.Attack(true)
                    : step == 5 ? fighter.Dodge() : step == 6 ? fighter.Jump() : fighter.Skill();
                Require(practice.accepted, "Single actual practice request rejected at step " + step);
                break;
        }
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
            Check(practice.requests == 1 && practice.executedActions == 1, "Practice step " + practice.step + " uses exactly one successful real action");
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
            Check(player.Health == player.MaxHealth, battle + " begins with full player health");
        }
        SuspendBrains();
        nextBattleActionAt = Time.time + .45f;
        SetPhase("Battle");
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
                point.y = fighter.transform.position.y;
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
        foreach (var actor in CombatActor.Actors.ToArray())
        {
            if (actor == null) continue;
            var brain = actor.GetComponent<EnemyCombatAI>();
            if (brain == null) continue;
            if (!brains.ContainsKey(brain)) brains.Add(brain, brain.enabled);
            brain.enabled = false; actor.CancelAttack();
        }
    }
    static void Capture(string name)
    {
        string path = Path.Combine(report.outputFolder, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
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
        if (report.failures.Count < 20) report.failures.Add(message);
    }
    static void Fail(string reason) { report.failures.Add(reason); Finish(); }
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
        if (report.battlesObserved != 3 || report.practice.Count != 8 || !report.framesAdvanced || !report.gameTimeAdvanced)
            report.failures.Add("The complete three-battle/live-practice sequence did not finish.");
        if (report.retryEachStage && report.retriesObserved != 3) report.failures.Add("Expected one actual result retry per battle.");
        foreach (var screenshot in report.screenshots)
        {
            screenshot.written = File.Exists(screenshot.path) && new FileInfo(screenshot.path).Length > 0;
            if (!screenshot.written) report.failures.Add("Screenshot was not written: " + screenshot.path);
        }
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
        if (fighter != null) fighter.ActionPerformed -= OnAction;
    }
}
