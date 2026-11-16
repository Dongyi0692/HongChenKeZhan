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

// Prep alone skips prior fights through public damage/progression interfaces.
// The observed rescue uses unmodified AI, animation clocks, sword sweeps and damage.
public static class Stage12RescueAIPlaythrough
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] public sealed class Swing
    {
        public string motion, endReason;
        public int startFrame, endFrame;
        public float startTime, duration, sampledThrough;
        public bool endedNaturally;
    }
    [Serializable] public sealed class ActorResult
    {
        public string name, role, team;
        public int id, startingHealth, finalHealth, attacks, naturalEnds, interruptions, hits, damageDealt, damageReceived;
        public int groundSamples, cachedGroundedSamples, rawGroundedSamples, groundedProbeSamples;
        public bool startedEnabled, startedCombatEnabled, defeated;
        public float minimumY, maximumY, minimumFloorGap = -1f, maximumFloorGap = -1f;
        public Vector3 startPosition, endPosition;
        public List<Swing> swings = new();
    }
    [Serializable] public sealed class Sample
    {
        public string actor, aiState, target, motion;
        public int frame, health;
        public float time, floorGap;
        public bool cachedGrounded, rawGrounded, attacking, givingSpace, spacePathBlocked;
        public Vector3 position;
    }
    [Serializable] public sealed class Report
    {
        public string scope = "Actual Stage12 rear-yard rescue, authored five spawn anchors and ally position. Preparation skips tutorial prerequisites/two prior fights and allows actual elite entrance. During the 20-second AI observation: no injected damage, AI suspension, attack forcing or clock/hit-volume changes; player stays distant, invulnerable and excluded as a target. A separate post-observation progression check verifies distant victory waits for approach. If enemies remain then, explicitly counted postObservationDamageCalls clear them and never contribute to observed AI hits/damage. No scene created/saved.";
        public string phase = "NotStarted", scene, outputFolder, finalStage, endReason;
        public bool active, completed, passed, restoredTitle, framesAdvanced, gameTimeAdvanced, allAIEnabled;
        public bool distantCompletionStayedPlayable, distantCompletionHadNoFarewell, approachTriggeredFarewell, reachedVictory;
        public string distantView, distantStoryPart, distantPrompt, postObservationStage;
        public int setupDamageCalls, observedDamageInjectionCalls, firstFrame, lastFrame, runtimeErrors, focusPauseResumes;
        public int postObservationDamageCalls, approachFrame, farewellFrame;
        public int gruntAttacks, allyAttacks, gruntActualHits, allyActualHits, gruntDamage, allyDamage;
        public float elapsedSeconds, observedSeconds, firstGameTime, lastGameTime;
        public Vector3 isolatedPlayerPosition, approachFixturePosition, allyPositionAtApproach;
        public float approachFixtureGroundY;
        public List<ActorResult> actors = new();
        public List<Sample> samples = new();
        public List<string> screenshots = new(), failures = new();
    }
    sealed class Watch
    {
        public CombatActor actor;
        public EnemyCombatAI brain;
        public CharacterController motor;
        public ActorResult result;
        public Swing current;
        public Action<CombatAttack> attack;
        public Action<CombatMotion, float, float, Vector3, Vector3> pose;
        public Action<CombatActor, int> hit, damage;
    }
    static Report report = new();
    static readonly List<Watch> watches = new();
    static InnInterface ui;
    static PrototypeFighter fighter;
    static PrototypeInput input;
    static DuelEncounter duel;
    static CombatActor player;
    static CombatActor framedTarget;
    static OrbitCamera orbit;
    static double beganAt, phaseAt;
    static float observeStartedAt, nextSampleAt;
    static int lastFrame, captureFrame, lastDialogueAdvanceFrame;
    static bool observing, oldBackground, shot5, shot15;
    static InputSettings.BackgroundBehavior oldInputBackground;
    static string View => Get(ui, "view").ToString();

    [Unity.Pipeline.Commands.CliCommand("innstage12", "begin_rescue_ai_test", "Observe actual five-grunt versus ally AI/sword damage for 20 seconds in Stage12")]
    public static string Begin()
    {
        if (report.active) return Status();
        Detach(); watches.Clear(); observing = shot5 = shot15 = false;
        ui = null; fighter = null; player = null; input = null; duel = null;
        report = new Report(); beganAt = EditorApplication.timeSinceStartup;
        oldBackground = Application.runInBackground; oldInputBackground = InputSystem.settings.backgroundBehavior;
        try
        {
            Require(EditorApplication.isPlaying && !EditorApplication.isPaused, "Start unpaused Play Mode in the existing Stage12.");
            report.scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Require(report.scene.IndexOf("Stage12", StringComparison.OrdinalIgnoreCase) >= 0, "Expected Stage12; no scene was opened.");
            report.outputFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Stage12RescueAI_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")));
            Directory.CreateDirectory(report.outputFolder);
            fighter = Object.FindFirstObjectByType<PrototypeFighter>(); ui = Object.FindFirstObjectByType<InnInterface>();
            Require(fighter != null && ui != null, "Actual player/UI missing.");
            input = fighter.GetComponent<PrototypeInput>(); duel = fighter.GetComponent<DuelEncounter>();
            Require(input != null && duel != null, "Actual input/encounter missing.");
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Type gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameView != null) EditorWindow.GetWindow(gameView).Focus();
            Call("Restart", true); player = duel.PlayerActor;
            Require(duel.HasRequiredBindings, duel.MissingBindingReason);
            report.isolatedPlayerPosition = player.transform.position;
            Call("StartGame"); input.enabled = false;
            Require(duel.RevealTutorialSentry() && duel.CompleteTutorialInstructions(), "Could not prepare the explicitly skipped tutorial prerequisite.");
            Call("BeginTutorialFight");
            Require(duel.Stage == DuelEncounter.EncounterStage.Tutorial, "Tutorial preparation could not start.");
            Require(duel.TutorialActor.ReceiveDamage(player, duel.TutorialActor.MaxHealth + 1, 0f, 0f, true), "Tutorial setup damage rejected.");
            report.setupDamageCalls++;
            // The normal UI starts the actual doorway entrance after its brief delay.
            report.active = true; report.phase = "AwaitEliteEntrance"; phaseAt = EditorApplication.timeSinceStartup;
            Application.logMessageReceived += OnLog; EditorApplication.update += Tick;
        }
        catch (Exception e) { Fail(e.GetBaseException().ToString()); }
        return Status();
    }
    [Unity.Pipeline.Commands.CliCommand("innstage12", "rescue_ai_test_status", "Read the actual rescue AI observation and saved evidence")]
    public static string Status() { if (report.active) Observe(); return JsonUtility.ToJson(report, true); }
    [Unity.Pipeline.Commands.CliCommand("innstage12", "cancel_rescue_ai_test", "Stop the rescue AI fixture and restore title")]
    public static string Cancel() { if (report.active) Fail("Cancelled by operator."); return Status(); }

    static void Tick()
    {
        if (!report.active) return;
        try
        {
            Observe();
            Require(EditorApplication.isPlaying && !EditorApplication.isPaused, "Play Mode was stopped or paused.");
            if (report.elapsedSeconds > 60f) { Fail("60-second total setup/observation timeout."); return; }
            if (View == "Pause")
            {
                ShowPlay(); report.focusPauseResumes++;
                Require(report.focusPauseResumes <= 20, "Repeated focus loss prevents an advancing observation.");
            }
            input.enabled = false; fighter.ClearInput();
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            if (report.phase == "AwaitEliteEntrance")
            {
                if (!duel.EliteEntranceReadyForDialogue)
                {
                    Require(EditorApplication.timeSinceStartup - phaseAt < 20d, "Actual elite entrance did not complete during setup.");
                    return;
                }
                Call("BeginEliteFight");
                Require(duel.Stage == DuelEncounter.EncounterStage.EliteDuel, "Elite setup battle could not begin.");
                Require(duel.EliteActor.ReceiveDamage(player, duel.EliteActor.MaxHealth + 1, 0f, 0f, true), "Elite setup damage rejected.");
                report.setupDamageCalls++;
                Require(duel.MakeEliteAlly(), "Won elite could not become allied.");
                Call("BeginRescue");
                Require(duel.Stage == DuelEncounter.EncounterStage.Rescue && duel.RemainingEnemies == 5, "Actual rescue did not spawn five grunts.");
                // Keep the real ReturnToHelp state; being distant must not count as having helped.
                fighter.PlaceForStory(report.isolatedPlayerPosition, fighter.transform.rotation);
                player.Invulnerable = true; player.ControlLocked = true; input.enabled = false;
                Require(Vector3.ProjectOnPlane(player.transform.position - duel.EliteActor.transform.position, Vector3.up).magnitude > 5f,
                    "Initial safe player spawn is not distant enough to isolate the actual rescue group.");
                foreach (var actor in duel.EnemyActors) AddWatch(actor);
                AddWatch(duel.EliteActor);
                Require(watches.Count == 6 && watches.All(w => w.brain != null && w.brain.enabled && w.brain.CombatEnabled),
                    "Expected all five real grunt AIs and the allied elite AI enabled by the encounter.");
                Require(duel.EliteActor.Health == 600 && duel.EliteActor.GruntDamageHealthFloor == 1, "Authored ally health/protection missing.");
                orbit = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
                Require(orbit != null, "Actual orbit camera missing.");
                orbit.enabled = true;
                framedTarget = duel.EnemyActors[0];
                orbit.FrameActors(duel.EliteActor.transform, framedTarget.transform, true);
                report.firstFrame = Time.frameCount; report.firstGameTime = observeStartedAt = Time.time;
                nextSampleAt = Time.time; observing = true;
                report.allAIEnabled = true; report.phase = "ObservingActualAI";
                Capture("01_AuthoredRescueStart");
                return;
            }
            if (report.phase == "CaptureEnd")
            {
                if (Time.frameCount < captureFrame + 3) return;
                foreach (var watch in watches) EndSwing(watch, "ObservationBoundary");
                // All observation delegates are already gated off. This explicit
                // fallback exists only to exercise the separate approach/result gate.
                player.ControlLocked = false;
                if (duel.Stage == DuelEncounter.EncounterStage.Rescue)
                    foreach (var actor in duel.EnemyActors.ToArray())
                        if (actor != null && !actor.IsDefeated && actor.Team == CombatTeam.Enemy)
                        {
                            Require(actor.ReceiveDamage(player, actor.MaxHealth + 1, 0f, 0f, true), "Post-observation progression damage rejected.");
                            report.postObservationDamageCalls++;
                        }
                Require(duel.Stage == DuelEncounter.EncounterStage.Complete, "Post-observation progression did not reach Complete.");
                report.postObservationStage = duel.Stage.ToString();
                report.phase = "CheckDistantCompletion"; captureFrame = Time.frameCount;
                return;
            }
            if (report.phase == "CheckDistantCompletion")
            {
                if (Time.frameCount < captureFrame + 3) return;
                report.distantView = View;
                report.distantStoryPart = Get(ui, "part").ToString();
                report.distantPrompt = ((UnityEngine.UI.Text)Get(ui, "hintText")).text;
                report.distantCompletionStayedPlayable = View == "Play" && report.distantStoryPart == "ReturnToHelp"
                    && Time.timeScale > 0f && !player.ControlLocked;
                report.distantCompletionHadNoFarewell = View != "Dialogue" && View != "Victory" && !(bool)Get(ui, "hasHelped");
                Require(report.distantCompletionStayedPlayable && report.distantCompletionHadNoFarewell,
                    "Distant player received farewell/result before approaching the ally.");
                Capture("05_DistantClear_WaitsForApproach");
                report.phase = "CaptureDistantCompletion"; captureFrame = Time.frameCount;
                return;
            }
            if (report.phase == "CaptureDistantCompletion")
            {
                if (Time.frameCount < captureFrame + 3) return;
                PlacePlayerNearAlly();
                report.approachFrame = Time.frameCount;
                report.phase = "AwaitApproachFarewell";
                return;
            }
            if (report.phase == "AwaitApproachFarewell")
            {
                if (View == "Dialogue")
                {
                    report.farewellFrame = Time.frameCount;
                    report.approachTriggeredFarewell = (bool)Get(ui, "hasHelped") && Get(ui, "part").ToString() == "Finished";
                    Require(report.approachTriggeredFarewell, "Approach opened an unexpected dialogue.");
                    Capture("06_Approach_Farewell"); captureFrame = lastDialogueAdvanceFrame = Time.frameCount;
                    report.phase = "AdvanceFarewell";
                }
                else Require(Time.frameCount - report.approachFrame <= 6, "Nearby player did not reach farewell through normal UI updates.");
                return;
            }
            if (report.phase == "AdvanceFarewell")
            {
                if (View == "Victory")
                {
                    report.reachedVictory = true;
                    Capture("07_Approach_Victory"); captureFrame = Time.frameCount; report.phase = "CaptureVictory";
                }
                else
                {
                    Require(View == "Dialogue", "Farewell left dialogue without reaching Victory.");
                    if (Time.frameCount >= captureFrame + 4 && Time.frameCount >= lastDialogueAdvanceFrame + 2)
                    { Call("AdvanceDialogue"); lastDialogueAdvanceFrame = Time.frameCount; }
                }
                return;
            }
            if (report.phase == "CaptureVictory")
            {
                if (Time.frameCount >= captureFrame + 3) Finish();
                return;
            }
            if (!observing) return;
            player.Invulnerable = true; player.ControlLocked = true;
            foreach (var watch in watches)
            {
                if (watch.actor == null) { Fail("Observed actor was destroyed during the rescue."); return; }
                if (watch.current != null && !watch.actor.IsAttacking)
                    EndSwing(watch, watch.actor.IsDefeated ? "Defeated" : watch.actor.IsStunned ? "HitReaction"
                        : !duel.IsCombatActive ? "EncounterEnded" : "AttackEnded");
                if (!watch.actor.IsDefeated && (!watch.brain.enabled || !watch.brain.CombatEnabled))
                {
                    // The encounter legitimately disables all AIs after the last enemy is defeated.
                    if (duel.Stage == DuelEncounter.EncounterStage.Rescue) report.allAIEnabled = false;
                }
            }
            if (duel.Stage == DuelEncounter.EncounterStage.Rescue && (framedTarget == null || framedTarget.IsDefeated || !orbit.IsFramingActors))
            {
                framedTarget = duel.EnemyActors.Where(a => a != null && !a.IsDefeated)
                    .OrderBy(a => (a.transform.position - duel.EliteActor.transform.position).sqrMagnitude).FirstOrDefault();
                if (framedTarget != null) orbit.FrameActors(duel.EliteActor.transform, framedTarget.transform);
            }
            if (Time.time >= nextSampleAt) { SampleActors(); nextSampleAt = Time.time + .25f; }
            if (!shot5 && report.observedSeconds >= 5f) { shot5 = true; Capture("02_ActualCombat_5s"); }
            if (!shot15 && report.observedSeconds >= 15f) { shot15 = true; Capture("03_ActualCombat_15s"); }
            if (duel.Stage == DuelEncounter.EncounterStage.Complete || report.observedSeconds >= 20f)
            {
                report.endReason = duel.Stage == DuelEncounter.EncounterStage.Complete ? "NaturalRescueVictory" : "TwentySecondsObserved";
                SampleActors();
                observing = false; report.finalStage = duel.Stage.ToString();
                Capture("04_ObservationEnd"); captureFrame = Time.frameCount; report.phase = "CaptureEnd";
            }
            else Require(duel.Stage == DuelEncounter.EncounterStage.Rescue, "Unexpected rescue stage: " + duel.Stage);
        }
        catch (Exception e) { Fail(e.GetBaseException().ToString()); }
    }

    static void PlacePlayerNearAlly()
    {
        CombatActor ally = duel.EliteActor;
        Require(ally != null && !ally.IsDefeated, "Living ally missing for approach regression.");
        var motor = player.GetComponent<CharacterController>();
        var place = typeof(CombatActor).Assembly.GetType("FengChenInn.ActorPlacement").GetMethod("TryPlace", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (float distance in new[] { 2.2f, 2.8f, 1.5f })
            for (int direction = 0; direction < 8; direction++)
            {
                // Use the ally's actual rear-yard floor level, not the higher indoor player level.
                Vector3 wanted = ally.transform.position + Quaternion.Euler(0f, direction * 45f, 0f) * Vector3.forward * distance;
                Vector3 toward = Vector3.ProjectOnPlane(ally.transform.position - wanted, Vector3.up);
                Quaternion facing = Quaternion.LookRotation(toward);
                if (!(bool)place.Invoke(null, new object[] { motor, wanted, facing, float.NaN })) continue;
                if (Vector3.ProjectOnPlane(player.transform.position - ally.transform.position, Vector3.up).magnitude > 3.2f) continue;
                RaycastHit[] ground = Physics.RaycastAll(player.transform.position + Vector3.up * .25f, Vector3.down, .7f, ~0, QueryTriggerInteraction.Ignore);
                var supported = ground.Where(hit => hit.collider.GetComponentInParent<CombatActor>() == null && hit.normal.y > .7f).OrderBy(hit => hit.distance).ToArray();
                if (supported.Length == 0) continue;
                fighter.PlaceForStory(player.transform.position, facing);
                player.ControlLocked = false;
                report.approachFixturePosition = player.transform.position;
                report.allyPositionAtApproach = ally.transform.position;
                report.approachFixtureGroundY = supported[0].point.y;
                Physics.SyncTransforms();
                return;
            }
        throw new InvalidOperationException("No valid existing rear-yard ground beside the ally for approach regression.");
    }

    static void AddWatch(CombatActor actor)
    {
        Require(actor != null, "Observed actor missing.");
        var watch = new Watch { actor = actor, brain = actor.GetComponent<EnemyCombatAI>(), motor = actor.GetComponent<CharacterController>() };
        watch.result = new ActorResult { name = actor.name, id = actor.GetInstanceID(), role = actor.Role.ToString(), team = actor.Team.ToString(),
            startingHealth = actor.Health, startPosition = actor.transform.position, minimumY = actor.transform.position.y, maximumY = actor.transform.position.y,
            startedEnabled = watch.brain != null && watch.brain.enabled, startedCombatEnabled = watch.brain != null && watch.brain.CombatEnabled };
        watch.attack = attack =>
        {
            if (!observing) return;
            if (watch.current != null) EndSwing(watch, "NextAttackStarted");
            watch.current = new Swing { motion = attack.motion.ToString(), duration = attack.duration, startFrame = Time.frameCount, startTime = Time.time };
            watch.result.swings.Add(watch.current); watch.result.attacks++;
            if (actor.Role == CombatRole.Grunt) report.gruntAttacks++; else report.allyAttacks++;
        };
        watch.pose = (motion, before, clock, from, to) =>
        { if (observing && watch.current != null && watch.current.motion == motion.ToString()) watch.current.sampledThrough = Mathf.Max(watch.current.sampledThrough, clock); };
        watch.hit = (target, amount) =>
        {
            if (!observing || amount <= 0) return;
            watch.result.hits++; watch.result.damageDealt += amount;
            if (actor.Role == CombatRole.Grunt) { report.gruntActualHits++; report.gruntDamage += amount; }
            else { report.allyActualHits++; report.allyDamage += amount; }
        };
        watch.damage = (source, amount) => { if (observing && amount > 0) watch.result.damageReceived += amount; };
        actor.AttackStarted += watch.attack; actor.AttackPoseSampled += watch.pose;
        actor.HitLanded += watch.hit; actor.Damaged += watch.damage;
        watches.Add(watch); report.actors.Add(watch.result);
    }
    static void EndSwing(Watch watch, string reason)
    {
        var swing = watch.current;
        if (swing == null) return;
        swing.endFrame = Time.frameCount; swing.endReason = reason;
        swing.endedNaturally = swing.sampledThrough >= swing.duration - .002f;
        if (swing.endedNaturally) watch.result.naturalEnds++;
        else if (reason != "ObservationBoundary") watch.result.interruptions++;
        watch.current = null;
    }
    static void SampleActors()
    {
        foreach (var watch in watches)
        {
            if (watch.actor == null) continue;
            Vector3 position = watch.actor.transform.position;
            bool cached = (bool)typeof(EnemyCombatAI).GetField("groundedFromGravity", Hidden).GetValue(watch.brain);
            bool raw = watch.motor != null && watch.motor.enabled && watch.motor.isGrounded;
            float nearestFloor = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(position + Vector3.up * .3f, Vector3.down, 1.5f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<CombatActor>() == null && hit.normal.y > .7f)
                    nearestFloor = Mathf.Min(nearestFloor, hit.distance - .3f);
            float gap = float.IsPositiveInfinity(nearestFloor) ? -1f : nearestFloor;
            var row = watch.result;
            row.finalHealth = watch.actor.Health; row.defeated = watch.actor.IsDefeated; row.endPosition = position;
            row.minimumY = Mathf.Min(row.minimumY, position.y); row.maximumY = Mathf.Max(row.maximumY, position.y);
            if (!watch.actor.IsDefeated)
            {
                row.groundSamples++; if (cached) row.cachedGroundedSamples++; if (raw) row.rawGroundedSamples++;
                if (gap >= -.05f && gap < .18f && gap != -1f) row.groundedProbeSamples++;
                if (gap != -1f)
                {
                    row.minimumFloorGap = row.minimumFloorGap == -1f ? gap : Mathf.Min(row.minimumFloorGap, gap);
                    row.maximumFloorGap = row.maximumFloorGap == -1f ? gap : Mathf.Max(row.maximumFloorGap, gap);
                }
            }
            report.samples.Add(new Sample { actor = row.name, frame = Time.frameCount, time = Time.time, position = position,
                health = watch.actor.Health, aiState = watch.brain.State.ToString(), target = watch.brain.Target != null ? watch.brain.Target.name : "None",
                motion = watch.actor.Animation != null ? watch.actor.Animation.CurrentMotion.ToString() : "Missing",
                cachedGrounded = cached, rawGrounded = raw, floorGap = gap, attacking = watch.actor.IsAttacking,
                givingSpace = watch.brain.GivingSpace, spacePathBlocked = watch.brain.SpacePathBlocked });
        }
    }
    static void Capture(string name)
    { string path = Path.Combine(report.outputFolder, name + ".png"); ScreenCapture.CaptureScreenshot(path); report.screenshots.Add(path); }
    static void Observe()
    {
        report.elapsedSeconds = (float)(EditorApplication.timeSinceStartup - beganAt);
        report.lastFrame = Time.frameCount; report.lastGameTime = Time.time;
        if (observing) report.observedSeconds = Time.time - observeStartedAt;
        report.framesAdvanced = report.firstFrame > 0 && report.lastFrame > report.firstFrame + 5;
        report.gameTimeAdvanced = report.firstFrame > 0 && report.lastGameTime > report.firstGameTime + .1f;
    }
    static void OnLog(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { report.runtimeErrors++; if (report.failures.Count < 15) report.failures.Add(message); } }
    static object Get(object target, string field) => target.GetType().GetField(field, Hidden).GetValue(target);
    static void Call(string method, params object[] args) => typeof(InnInterface).GetMethod(method, Hidden).Invoke(ui, args);
    static void SetEnum(object target, string field, string value)
    { var info = target.GetType().GetField(field, Hidden); info.SetValue(target, Enum.Parse(info.FieldType, value)); }
    static void ShowPlay()
    { var type = typeof(InnInterface).GetNestedType("View", BindingFlags.NonPublic); Call("Show", Enum.Parse(type, "Play")); }
    static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    static void Fail(string reason) { report.failures.Add(reason); Finish(); }
    static void Finish()
    {
        Observe(); observing = false; report.active = false; report.completed = true;
        foreach (var watch in watches) EndSwing(watch, "ObservationBoundary");
        Detach();
        if (!report.framesAdvanced || !report.gameTimeAdvanced) report.failures.Add("Insufficient actual rescue frame/time advancement.");
        if (report.gruntAttacks == 0 || report.allyAttacks == 0) report.failures.Add("Both sides did not produce actual AI-selected attacks.");
        if (report.gruntActualHits == 0 || report.allyActualHits == 0) report.failures.Add("Actual sword damage was not observed from both sides.");
        if (!report.actors.Any(a => a.role == "Grunt" && a.naturalEnds > 0) || !report.actors.Any(a => a.role == "Elite" && a.naturalEnds > 0))
            report.failures.Add("Both sides did not finish at least one genuine full attack clip; inspect interruption evidence.");
        if (!report.allAIEnabled) report.failures.Add("An active combatant's AI was not enabled for the observation.");
        if (!report.distantCompletionStayedPlayable || !report.distantCompletionHadNoFarewell
            || !report.approachTriggeredFarewell || !report.reachedVictory)
            report.failures.Add("Distant-clear/approach/farewell/Victory regression did not complete.");
        foreach (var row in report.actors)
            if (!row.defeated && row.minimumY < row.startPosition.y - 2f) report.failures.Add(row.name + " fell more than two meters below its authored starting level.");
        try
        {
            if (EditorApplication.isPlaying && ui != null)
            { fighter.ClearInput(); Call("Restart", true); report.restoredTitle = View == "Title"; }
        }
        catch (Exception e) { report.failures.Add("Restore title: " + e.GetBaseException().Message); }
        Application.runInBackground = oldBackground; InputSystem.settings.backgroundBehavior = oldInputBackground;
        if (!report.restoredTitle) report.failures.Add("Title restoration was not confirmed.");
        foreach (string path in report.screenshots) if (!File.Exists(path)) report.failures.Add("Missing screenshot: " + path);
        report.passed = report.failures.Count == 0 && report.runtimeErrors == 0;
        report.phase = report.passed ? "Completed" : "Failed";
        if (string.IsNullOrEmpty(report.outputFolder)) report.outputFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Stage12RescueAI_PreconditionFailure"));
        Directory.CreateDirectory(report.outputFolder);
        File.WriteAllText(Path.Combine(report.outputFolder, "report.json"), JsonUtility.ToJson(report, true));
    }
    static void Detach()
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        foreach (var watch in watches)
            if (watch.actor != null)
            { watch.actor.AttackStarted -= watch.attack; watch.actor.AttackPoseSampled -= watch.pose; watch.actor.HitLanded -= watch.hit; watch.actor.Damaged -= watch.damage; }
    }
}
