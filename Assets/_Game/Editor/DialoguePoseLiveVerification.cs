using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.LowLevel;
using Object = UnityEngine.Object;

// Runtime fixtures only: never saves a scene or changes an animation asset.
// The before-Update probe reproduces the original same-frame pause race.
public static class DialoguePoseLiveVerification
{
    [Serializable] public sealed class BonePose
    {
        public string name;
        public Vector3 position, worldPosition;
        public Quaternion rotation;
    }
    [Serializable] public sealed class Snapshot
    {
        public string point, view, motion, action;
        public int frame, executedActions;
        public float gameTime, deltaTime, timeScale, clipTime, maxBoneAngle, maxBoneDistance;
        public bool fighterLocked, driverLocked, grounded, controllerGrounded;
        public Vector3 position;
        public List<BonePose> bones = new();
    }
    [Serializable] public sealed class Case
    {
        public string name, result = "Pending";
        public bool ledgerObserved, dialogueObserved, enteredWhileMoving, enteredWhileAirborne, resumedMovement;
        public int ledgerFrames, dialogueFrames;
        public float resumeDistance;
        public List<Snapshot> snapshots = new();
        public List<string> checks = new(), failures = new(), screenshots = new();
    }
    [Serializable] public sealed class Report
    {
        public string scope = "Existing Stage12 scene and live PlayerLoop frames. Actual Hall->Ledger->FinishReadingLedger->Sentry callbacks are used. Moving/jumping dialogue cases replay the existing RevealSentryAndTalk callback after closing its initial dialogue; this is fixture setup, not an added gameplay trigger. No animation is forced, no scene is saved. Reports locked relaxed standing idle (ExplorationIdle when available, otherwise Idle), ground contact, blocked requests/counters and restored movement; waist appearance still needs visual review. Returns to title and restores background settings.";
        public string scene, phase = "NotStarted", outputPath;
        public bool active, completed, passed, returnedToTitle;
        public int passedCases, focusPauseResumes;
        public float elapsedSeconds;
        public List<Case> cases = new();
        public List<string> errors = new();
    }
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly HumanBodyBones[] TrackedBones = { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
        HumanBodyBones.Head, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
    static Report report = new();
    static InnInterface ui;
    static PrototypeFighter fighter;
    static PrototypeInput input;
    static DuelEncounter duel;
    static PlayerLoopSystem originalLoop;
    static bool loopInstalled, oldBackground, finishing;
    static InputSettings.BackgroundBehavior oldInputBackground;
    static int caseIndex, openedFrame, lastRecordedFrame;
    static double beganAt, phaseAt;
    static float phaseGameTime;
    static string folder, observedView;
    static Snapshot openedPose;
    static Vector3 movementStart, travelDirection;
    static Case Current => report.cases[caseIndex];
    static string View => Get(ui, "view").ToString();

    [Unity.Pipeline.Commands.CliCommand("inndialoguepose", "begin", "Verify real-frame dialogue/ledger pose lock and return to title")]
    public static string Begin()
    {
        if (report.active) return Status();
        report = new Report(); finishing = false; ui = null; fighter = null; folder = null;
        beganAt = EditorApplication.timeSinceStartup;
        oldBackground = Application.runInBackground;
        oldInputBackground = InputSystem.settings.backgroundBehavior;
        try
        {
            Require(EditorApplication.isPlaying && !EditorApplication.isPaused, "Start unpaused Play Mode in the existing Stage12 first.");
            report.scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Require(report.scene.IndexOf("Stage12", StringComparison.OrdinalIgnoreCase) >= 0, "Expected existing Stage12; found " + report.scene);
            fighter = Object.FindFirstObjectByType<PrototypeFighter>(); ui = Object.FindFirstObjectByType<InnInterface>();
            Require(fighter != null && ui != null, "Player/UI not initialized.");
            input = fighter.GetComponent<PrototypeInput>(); duel = fighter.GetComponent<DuelEncounter>();
            Require(input != null && duel != null && ui.ledgerAnchor != null, "Input, encounter or authored ledger anchor missing.");
            Require(fighter.animationDriver != null && fighter.animationDriver.Ready
                && (fighter.animationDriver.HasMotion(CombatMotion.ExplorationIdle) || fighter.animationDriver.HasMotion(CombatMotion.Idle)), "Existing standing idle is unavailable.");
            folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "DialoguePose_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));
            Directory.CreateDirectory(folder);
            report.outputPath = Path.Combine(folder, "DialoguePoseLiveVerification.json");
            foreach (string name in new[] { "Standing", "Moving", "Jumping" }) report.cases.Add(new Case { name = name });
            caseIndex = 0;
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            originalLoop = PlayerLoop.GetCurrentPlayerLoop();
            var loop = CloneLoop(originalLoop);
            Require(Insert(ref loop, typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate), BeforeUpdate, false), "Normal Update loop missing.");
            Require(Insert(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate), AfterLateUpdate, true), "Normal LateUpdate loop missing.");
            report.active = true; loopInstalled = true; PlayerLoop.SetPlayerLoop(loop);
            EditorApplication.update += Watchdog;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            Application.logMessageReceived += OnLog;
            SetPhase("Prepare");
        }
        catch (Exception error) { Abort(error.GetBaseException().ToString()); }
        return Status();
    }
    [Unity.Pipeline.Commands.CliCommand("inndialoguepose", "status", "Read pose verification, frame evidence and output paths")]
    public static string Status()
    {
        if (report.active) report.elapsedSeconds = (float)(EditorApplication.timeSinceStartup - beganAt);
        return JsonUtility.ToJson(report, true);
    }
    [Unity.Pipeline.Commands.CliCommand("inndialoguepose", "cancel", "Stop the pose fixture, restore loop/settings and return to title")]
    public static string Cancel() { if (report.active) Abort("Cancelled by operator."); return Status(); }

    static void BeforeUpdate()
    {
        if (!report.active) return;
        try
        {
            if (input != null) input.enabled = false;
            if (View == "Pause" && report.phase != "ObserveLocked")
            {
                Call("Show", ParseView("Play")); report.focusPauseResumes++;
                input.enabled = false;
            }
            double elapsed = EditorApplication.timeSinceStartup - phaseAt;
            switch (report.phase)
            {
                case "Prepare":
                    Call("Restart", true); Call("StartGame");
                    Set("part", Enum.Parse(typeof(InnInterface).GetNestedType("StoryPart", BindingFlags.NonPublic), "Hall"));
                    input.enabled = false; PlaceNear(ui.ledgerAnchor.position, Mathf.Min(1.2f, ui.ledgerInteractionDistance - .2f));
                    SetPhase("WarmupLedger"); break;
                case "WarmupLedger":
                    Require(View == "Play" && Time.timeScale > 0f, "Ledger warmup unexpectedly paused.");
                    if (Time.time - phaseGameTime >= .35f && fighter.IsGrounded)
                    {
                        Call("UpdateStoryMovement", true);
                        Require(View == "Ledger", "Real Hall proximity interaction did not open Ledger.");
                        BeginLockedObservation("Ledger");
                    }
                    else if (elapsed > 5d) throw new InvalidOperationException("Ledger fixture did not reach grounded stance.");
                    break;
                case "AfterLedger":
                    Call("FinishReadingLedger");
                    Require(ui.LedgerRead && duel.TutorialSentryRevealed && View == "Dialogue", "Real ledger completion did not reveal sentry/dialogue.");
                    if (caseIndex == 0) BeginLockedObservation("Dialogue");
                    else
                    {
                        CompleteDialogue();
                        PlaceNear(duel.TutorialActor.transform.position, 1.35f);
                        SetPhase("WarmupMotion");
                    }
                    break;
                case "WarmupMotion":
                    Require(View == "Play" && Time.timeScale > 0f, "Motion warmup unexpectedly paused.");
                    if (Time.time - phaseGameTime >= .35f && fighter.IsGrounded)
                    {
                        movementStart = fighter.transform.position;
                        if (caseIndex == 1)
                        {
                            travelDirection = FreeDirection(); fighter.MoveInput = InputFor(travelDirection); fighter.RunRequested = true;
                        }
                        else Require(fighter.Jump(), "Existing grounded jump did not execute.");
                        SetPhase("MotionLeadIn");
                    }
                    else if (elapsed > 5d) throw new InvalidOperationException("Motion fixture did not reach grounded stance.");
                    break;
                case "MotionLeadIn":
                    if (caseIndex == 1) fighter.MoveInput = InputFor(travelDirection);
                    if (Time.time - phaseGameTime >= .12f)
                    {
                        Current.enteredWhileMoving = caseIndex == 1 && Vector3.ProjectOnPlane(fighter.transform.position - movementStart, Vector3.up).magnitude > .1f;
                        Current.enteredWhileAirborne = caseIndex == 2 && !fighter.IsGrounded && fighter.transform.position.y > movementStart.y + .15f;
                        Check(caseIndex == 1 ? Current.enteredWhileMoving : Current.enteredWhileAirborne,
                            "Requested lead-in actually occurred before dialogue");
                        Current.snapshots.Add(Capture("Immediately before replaying real sentry callback"));
                        Call("RevealSentryAndTalk");
                        Require(View == "Dialogue", "Existing sentry callback did not reopen dialogue.");
                        BeginLockedObservation("Dialogue");
                    }
                    break;
                case "CloseDialogue":
                    CompleteDialogue();
                    Check(!fighter.IsDialoguePoseActive && !fighter.animationDriver.IsDialoguePoseActive && Time.timeScale > 0f,
                        "Closing all lines releases both locks and resumes game time");
                    movementStart = fighter.transform.position;
                    travelDirection = FreeDirection(); fighter.MoveInput = InputFor(travelDirection);
                    SetPhase("ResumeMovement"); break;
                case "ResumeMovement":
                    fighter.MoveInput = InputFor(travelDirection);
                    if (Time.time - phaseGameTime >= .35f)
                    {
                        Current.resumeDistance = Vector3.ProjectOnPlane(fighter.transform.position - movementStart, Vector3.up).magnitude;
                        Current.resumedMovement = Current.resumeDistance > .2f;
                        Check(Current.resumedMovement, "WASD movement resumes after dialogue; meters=" + Current.resumeDistance);
                        Check(!fighter.IsDialoguePoseActive && !fighter.animationDriver.IsDialoguePoseActive, "Pose remains released during movement");
                        Current.snapshots.Add(Capture("After resumed real movement")); fighter.ClearInput();
                        SetPhase("NextCase");
                    }
                    break;
                case "NextCase":
                    foreach (string screenshot in Current.screenshots)
                        Check(File.Exists(screenshot), "Screenshot written: " + Path.GetFileName(screenshot));
                    Check(Current.ledgerFrames >= 5 && Current.dialogueFrames >= 5, "Both paused views observed across at least five real frame intervals");
                    Current.result = Current.failures.Count == 0 ? "Passed live-frame checks; visual review pending" : "Failed checks";
                    if (Current.failures.Count == 0) report.passedCases++;
                    if (++caseIndex == report.cases.Count) Finish(); else SetPhase("Prepare");
                    break;
            }
        }
        catch (Exception error) { Abort(error.GetBaseException().ToString()); }
    }

    static void BeginLockedObservation(string view)
    {
        observedView = view; openedFrame = Time.frameCount; lastRecordedFrame = -1;
        openedPose = Capture("Immediately after " + view + " opens"); Current.snapshots.Add(openedPose);
        Check(openedPose.fighterLocked && openedPose.driverLocked && openedPose.motion == ExpectedDialogueMotion(),
            view + " immediately samples the locked standing pose: " + ExpectedDialogueMotion());
        // Exercise rejected public requests once. A lock must reject them without
        // queuing actions, spending stamina or incrementing execution counters.
        int count = CountActions(); float stamina = fighter.Stamina;
        bool accepted = fighter.Jump() | fighter.Attack(false) | fighter.Attack(true) | fighter.Dodge() | fighter.Skill();
        Check(!accepted && CountActions() == count && Mathf.Abs(fighter.Stamina - stamina) < .001f,
            view + " rejects actions without counters/stamina changing");
        fighter.MoveInput = Vector2.one; fighter.GuardRequested = true;
        SetPhase("ObserveLocked");
    }
    static void AfterLateUpdate()
    {
        if (!report.active || report.phase != "ObserveLocked" || lastRecordedFrame == Time.frameCount) return;
        try
        {
            lastRecordedFrame = Time.frameCount;
            Snapshot sample = Capture("After all LateUpdate; frame +" + (Time.frameCount - openedFrame));
            for (int i = 0; i < Mathf.Min(openedPose.bones.Count, sample.bones.Count); i++)
            {
                sample.maxBoneAngle = Mathf.Max(sample.maxBoneAngle, Quaternion.Angle(openedPose.bones[i].rotation, sample.bones[i].rotation));
                sample.maxBoneDistance = Mathf.Max(sample.maxBoneDistance, Vector3.Distance(openedPose.bones[i].position, sample.bones[i].position));
            }
            Current.snapshots.Add(sample);
            Check(sample.view == observedView && sample.fighterLocked && sample.driverLocked && sample.motion == ExpectedDialogueMotion(),
                observedView + " retains locked " + ExpectedDialogueMotion() + " at frame +" + (Time.frameCount - openedFrame));
            Check(sample.grounded, observedView + " feet retain controller ground contact");
            Check(sample.executedActions == openedPose.executedActions, observedView + " execution counters do not advance");
            Check(sample.maxBoneAngle < .25f && sample.maxBoneDistance < .001f
                && Vector3.Distance(sample.position, openedPose.position) < .01f, observedView + " pose/root remain stable");
            if (Time.frameCount == openedFrame + 2)
            {
                string path = Path.Combine(folder, Current.name + "_" + observedView + ".png");
                ScreenCapture.CaptureScreenshot(path); Current.screenshots.Add(path);
            }
            if (Time.frameCount < openedFrame + 5) return;
            if (observedView == "Ledger")
            { Current.ledgerObserved = true; Current.ledgerFrames = Time.frameCount - openedFrame; SetPhase("AfterLedger"); }
            else
            { Current.dialogueObserved = true; Current.dialogueFrames = Time.frameCount - openedFrame; SetPhase("CloseDialogue"); }
        }
        catch (Exception error) { Abort(error.GetBaseException().ToString()); }
    }
    static Snapshot Capture(string label)
    {
        var driver = fighter.animationDriver; var motor = fighter.GetComponent<CharacterController>();
        var sample = new Snapshot { point = label, view = View, frame = Time.frameCount, executedActions = CountActions(),
            gameTime = Time.time, deltaTime = Time.deltaTime, timeScale = Time.timeScale, clipTime = driver.NormalizedTime,
            motion = driver.CurrentMotion.ToString(), action = fighter.State.ToString(), fighterLocked = fighter.IsDialoguePoseActive,
            driverLocked = driver.IsDialoguePoseActive, grounded = fighter.IsGrounded, controllerGrounded = motor.isGrounded, position = fighter.transform.position };
        var animator = driver.targetAnimator;
        if (animator != null && animator.isHuman && animator.avatar != null && animator.avatar.isValid)
            foreach (var id in TrackedBones)
            {
                Transform bone = animator.GetBoneTransform(id);
                if (bone != null) sample.bones.Add(new BonePose { name = id.ToString(), position = bone.localPosition, rotation = bone.localRotation, worldPosition = bone.position });
            }
        return sample;
    }
    static int CountActions() => fighter.LightAttackCount + fighter.HeavyAttackCount + fighter.JumpCount + fighter.DodgeCount + fighter.SkillCount + fighter.GuardCount;
    static string ExpectedDialogueMotion() => fighter.animationDriver.HasMotion(CombatMotion.ExplorationIdle)
        ? nameof(CombatMotion.ExplorationIdle) : nameof(CombatMotion.Idle);
    static void CompleteDialogue()
    {
        int safety = 0;
        while (View == "Dialogue" && ++safety <= 40) Call("AdvanceDialogue");
        Require(View == "Play", "Closing real sentry lines did not return to practice/play."); input.enabled = false;
    }
    static void PlaceNear(Vector3 anchor, float radius)
    {
        var place = typeof(CombatActor).Assembly.GetType("FengChenInn.ActorPlacement")?.GetMethod("TryPlace", BindingFlags.Static | BindingFlags.NonPublic);
        Require(place != null, "Existing ActorPlacement.TryPlace unavailable.");
        foreach (Vector3 direction in new[] { Vector3.back, Vector3.right, Vector3.left, Vector3.forward,
            new Vector3(1,0,-1).normalized, new Vector3(-1,0,-1).normalized })
        {
            Vector3 candidate = anchor + direction * radius; candidate.y = 0f;
            Quaternion facing = Quaternion.LookRotation(-direction);
            if (!(bool)place.Invoke(null, new object[] { fighter.GetComponent<CharacterController>(), candidate, facing, float.NaN })) continue;
            fighter.PlaceForStory(fighter.transform.position, facing); return;
        }
        throw new InvalidOperationException("No open fixture stance near actual story anchor.");
    }
    static Vector2 InputFor(Vector3 direction)
    {
        Vector3 forward = Vector3.ProjectOnPlane(Camera.main != null ? Camera.main.transform.forward : fighter.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .01f) forward = fighter.transform.forward;
        return new Vector2(Vector3.Dot(direction, Vector3.Cross(Vector3.up, forward)), Vector3.Dot(direction, forward));
    }
    static Vector3 FreeDirection()
    {
        var motor = fighter.GetComponent<CharacterController>();
        Vector3 center = fighter.transform.TransformPoint(motor.center);
        float half = Mathf.Max(0f, motor.height * .5f - motor.radius);
        float best = -1f; Vector3 chosen = Vector3.back;
        for (int i = 0; i < 8; i++)
        {
            Vector3 direction = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward; float clear = 1.6f;
            foreach (var hit in Physics.CapsuleCastAll(center + Vector3.up * half, center - Vector3.up * half,
                motor.radius * .92f, direction, 1.6f, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(fighter.transform)) clear = Mathf.Min(clear, hit.distance);
            if (clear > best) { best = clear; chosen = direction; }
        }
        Require(best > .3f, "No short open movement direction for the resume check."); return chosen;
    }
    static void Check(bool okay, string label) { if (okay) Current.checks.Add(label); else Current.failures.Add(label); }
    static void Watchdog()
    {
        if (!report.active) return;
        if (!EditorApplication.isPlaying) Abort("Play Mode ended during verification.");
        else if (EditorApplication.timeSinceStartup - beganAt > 65d) Abort("Live verification exceeded its 65-second safety limit.");
    }
    static void OnLog(string message, string stack, LogType type)
    { if (report.active && (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)) report.errors.Add(message); }
    static void BeforeReload() { if (report.active) Abort("Assembly reload interrupted verification."); }
    static void Abort(string error) { report.errors.Add(error); Finish(); }
    static void Finish()
    {
        if (finishing) return; finishing = true; report.active = false;
        if (loopInstalled) { PlayerLoop.SetPlayerLoop(originalLoop); loopInstalled = false; }
        EditorApplication.update -= Watchdog; AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload; Application.logMessageReceived -= OnLog;
        try
        {
            if (EditorApplication.isPlaying && ui != null) { fighter?.ClearInput(); Call("Restart", true); report.returnedToTitle = View == "Title"; }
        }
        catch (Exception error) { report.errors.Add("Title restoration: " + error.GetBaseException().Message); }
        finally { Application.runInBackground = oldBackground; InputSystem.settings.backgroundBehavior = oldInputBackground; }
        report.elapsedSeconds = (float)(EditorApplication.timeSinceStartup - beganAt);
        report.completed = true; report.passed = report.passedCases == 3 && report.errors.Count == 0 && report.returnedToTitle;
        report.phase = report.passed ? "Passed" : "FailedOrInterrupted";
        if (!string.IsNullOrEmpty(report.outputPath)) File.WriteAllText(report.outputPath, JsonUtility.ToJson(report, true));
    }
    static void SetPhase(string phase) { report.phase = phase; phaseAt = EditorApplication.timeSinceStartup; phaseGameTime = Time.time; }
    static object ParseView(string value) => Enum.Parse(typeof(InnInterface).GetNestedType("View", BindingFlags.NonPublic), value);
    static object Get(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
    static void Set(string name, object value) => typeof(InnInterface).GetField(name, Hidden).SetValue(ui, value);
    static void Call(string method, params object[] args) => typeof(InnInterface).GetMethod(method, Hidden).Invoke(ui, args);
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static PlayerLoopSystem CloneLoop(PlayerLoopSystem source)
    {
        if (source.subSystemList == null) return source;
        var children = new PlayerLoopSystem[source.subSystemList.Length];
        for (int i = 0; i < children.Length; i++) children[i] = CloneLoop(source.subSystemList[i]);
        source.subSystemList = children; return source;
    }
    static bool Insert(ref PlayerLoopSystem parent, Type target, PlayerLoopSystem.UpdateFunction callback, bool after)
    {
        if (parent.subSystemList == null) return false;
        for (int i = 0; i < parent.subSystemList.Length; i++)
        {
            if (parent.subSystemList[i].type == target)
            {
                var children = new List<PlayerLoopSystem>(parent.subSystemList);
                children.Insert(i + (after ? 1 : 0), new PlayerLoopSystem { type = typeof(DialoguePoseLiveVerification), updateDelegate = callback });
                parent.subSystemList = children.ToArray(); return true;
            }
            var child = parent.subSystemList[i];
            if (Insert(ref child, target, callback, after)) { parent.subSystemList[i] = child; return true; }
        }
        return false;
    }
}
