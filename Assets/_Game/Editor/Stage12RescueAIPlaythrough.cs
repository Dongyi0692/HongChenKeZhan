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
        public string scope = "Actual Stage12 front-door reinforcements and indoor allied battle. Preparation skips tutorial prerequisites/two prior fights and allows actual elite entrance. Five grunts must start outside and physically cross the front door before a 20-second actual AI observation. Player is safely parked indoors, invulnerable and excluded as a target. During observation: no injected damage, AI suspension, attack forcing or clock/hit-volume changes. Separate, explicitly counted post-observation damage clears survivors only to verify the normal farewell and victory flow; it never contributes to observed AI damage. No scene created/saved.";
        public string phase = "NotStarted", scene, outputFolder, finalStage, endReason;
        public bool active, completed, passed, restoredTitle, framesAdvanced, gameTimeAdvanced, allAIEnabled;
        public bool allFiveEnteredFromOutside, automaticFarewell, reachedVictory;
        public bool observationOnly, uiStateRestored;
        public string postObservationStage;
        public int setupDamageCalls, observedDamageInjectionCalls, firstFrame, lastFrame, runtimeErrors, focusPauseResumes;
        public int postObservationDamageCalls, farewellFrame;
        public int gruntAttacks, allyAttacks, gruntActualHits, allyActualHits, gruntDamage, allyDamage;
        public float elapsedSeconds, observedSeconds, firstGameTime, lastGameTime;
        public Vector3 isolatedPlayerPosition, frontDoorPosition, frontDoorInward;
        public List<DoorCrossing> entrance = new();
        public List<ActorResult> actors = new();
        public List<Sample> samples = new();
        public List<string> screenshots = new(), failures = new();
    }
    [Serializable] public sealed class DoorCrossing
    {
        public string actor;
        public Vector3 outsideStart, latestPosition;
        public bool startedOutside, crossedThreshold, reachedHall;
        public int thresholdFrame, hallFrame;
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
    static bool oldUiEnabled, oldFocusSuppression, savedUiState;
    static InputSettings.BackgroundBehavior oldInputBackground;
    static string View => Get(ui, "view").ToString();

    [Unity.Pipeline.Commands.CliCommand("innstage12", "begin_rescue_ai_test", "Observe actual five-grunt versus ally AI/sword damage for 20 seconds in Stage12")]
    public static string Begin() => BeginInternal(false);
    [Unity.Pipeline.Commands.CliCommand("innstage12", "begin_rescue_ai_only", "Observe only natural indoor NPC attacks for twenty seconds, then restore Title; separate from the rear-departure story test")]
    public static string BeginOnly() => BeginInternal(true);
    static string BeginInternal(bool observationOnly)
    {
        if (report.active) return Status();
        Detach(); watches.Clear(); observing = shot5 = shot15 = false;
        ui = null; fighter = null; player = null; input = null; duel = null;
        report = new Report { observationOnly = observationOnly }; beganAt = EditorApplication.timeSinceStartup;
        savedUiState = false;
        if (observationOnly)
            report.scope = "Actual Stage12 five-grunt front-door entrance and natural indoor NPC combat. Preparation explicitly skips tutorial/two prior fights through public progression/damage interfaces, then invokes the existing direct BeginRescueBattle API; this does not test the rear-door return storyline. During observation no injected damage, AI suspension, forced attacks, animation clock changes, health changes, hit-volume changes or time acceleration occur. Player is safely parked and ControlLocked so it is excluded from normal AI targeting. Actual NPC roots, attacks, interruptions, sword hit events and damage are recorded for twenty seconds or an earlier natural victory. UI Update is temporarily disabled only after formal rescue starts to isolate NPC combat from later story transitions; its original enabled state and Editor-only focus-pause suppression are restored. Screenshots/HUD are not UI-flow or performance acceptance. No post-observation survivor damage is injected, no farewell/Victory claim is made. FinalDeparture/Epilogue is verified by the separate Story fixture. No scene created or saved.";
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
            oldUiEnabled = ui.enabled; oldFocusSuppression = ui.SuppressFocusPauseForVerification; savedUiState = true;
            ui.SuppressFocusPauseForVerification = true;
            input = fighter.GetComponent<PrototypeInput>(); duel = fighter.GetComponent<DuelEncounter>();
            Require(input != null && duel != null, "Actual input/encounter missing.");
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Type gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameView != null) EditorWindow.GetWindow(gameView).Focus();
            Call("Restart", true); player = duel.PlayerActor;
            Require(duel.HasRequiredBindings, duel.MissingBindingReason);
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
            if (report.elapsedSeconds > 90f) { Fail("90-second total setup/entrance/observation timeout."); return; }
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
                if (report.observationOnly)
                {
                    Require(duel.BeginRescueBattle(), "Direct formal rescue preparation API rejected.");
                    ShowPlay(); ui.enabled = false;
                }
                else Call("BeginRescue");
                Require(duel.Stage == DuelEncounter.EncounterStage.Rescue && duel.RemainingEnemies == 5, "Actual rescue did not spawn five grunts.");
                PlacePlayerInHall();
                player.Invulnerable = true; player.ControlLocked = true; input.enabled = false;
                var door = Object.FindFirstObjectByType<InnEntranceDoors>();
                Require(door != null, "Actual front entrance missing.");
                report.frontDoorPosition = door.transform.position;
                report.frontDoorInward = duel.eliteArrivalPosition.z >= door.transform.position.z ? Vector3.forward : Vector3.back;
                foreach (var actor in duel.EnemyActors)
                    report.entrance.Add(new DoorCrossing { actor = actor.name, outsideStart = actor.transform.position,
                        latestPosition = actor.transform.position,
                        startedOutside = Vector3.Dot(actor.transform.position - report.frontDoorPosition, report.frontDoorInward) < -.5f });
                Require(report.entrance.Count == 5 && report.entrance.All(row => row.startedOutside), "Five reinforcements did not start outside the front door.");
                Require(player.MaxHealth == 150 && duel.EnemyActors.All(actor => actor.MaxHealth == 83)
                    && duel.EliteActor.MaxHealth == 900 && duel.EliteActor.GruntDamageHealthFloor == 1, "Scaled health/protected ally values missing.");
                orbit = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
                Require(orbit != null, "Actual orbit camera missing.");
                orbit.enabled = true;
                framedTarget = duel.EnemyActors[0];
                orbit.FrameActors(duel.EliteActor.transform, framedTarget.transform, true);
                report.phase = "AwaitReinforcementEntrance"; phaseAt = EditorApplication.timeSinceStartup;
                Capture("00_FiveOutsideFrontDoor");
                return;
            }
            if (report.phase == "AwaitReinforcementEntrance")
            {
                for (int i = 0; i < report.entrance.Count; i++)
                {
                    var row = report.entrance[i];
                    row.latestPosition = duel.EnemyActors[i].transform.position;
                    float inside = Vector3.Dot(row.latestPosition - report.frontDoorPosition, report.frontDoorInward);
                    if (!row.crossedThreshold && inside > .2f) { row.crossedThreshold = true; row.thresholdFrame = Time.frameCount; }
                    if (!row.reachedHall && inside >= 2.15f) { row.reachedHall = true; row.hallFrame = Time.frameCount; }
                }
                if (duel.RescueEntranceInProgress)
                {
                    Require(EditorApplication.timeSinceStartup - phaseAt < 30d, "Five real grunts did not complete the front-door walk within 30 seconds.");
                    return;
                }
                report.allFiveEnteredFromOutside = duel.RescueEnteredCount == 5 && report.entrance.All(row => row.startedOutside && row.crossedThreshold && row.reachedHall);
                Require(report.allFiveEnteredFromOutside, "Not every reinforcement physically crossed from outside into the hall.");
                foreach (var actor in duel.EnemyActors) AddWatch(actor);
                AddWatch(duel.EliteActor);
                Require(watches.Count == 6 && watches.All(w => w.actor.IsDefeated || (w.brain != null && w.brain.enabled && w.brain.CombatEnabled)),
                    "Expected all five real grunt AIs and the allied elite AI enabled after entrance.");
                report.firstFrame = Time.frameCount; report.firstGameTime = observeStartedAt = Time.time;
                nextSampleAt = Time.time; observing = true;
                report.allAIEnabled = true; report.phase = "ObservingActualAI";
                Capture("01_AllFiveInsideHall");
                return;
            }
            if (report.phase == "CaptureEnd")
            {
                if (Time.frameCount < captureFrame + 3) return;
                foreach (var watch in watches) EndSwing(watch, "ObservationBoundary");
                if (report.observationOnly) { Finish(); return; }
                // All observation delegates are already gated off. This explicit
                // fallback exists only to exercise the separate farewell/result flow.
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
                report.phase = "AwaitFarewell"; captureFrame = Time.frameCount;
                return;
            }
            if (report.phase == "AwaitFarewell")
            {
                if (View == "Dialogue")
                {
                    report.farewellFrame = Time.frameCount;
                    report.automaticFarewell = (bool)Get(ui, "hasHelped") && Get(ui, "part").ToString() == "Finished";
                    Require(report.automaticFarewell, "Indoor victory opened an unexpected dialogue.");
                    foreach (var defeated in duel.EnemyActors)
                    {
                        if (defeated == null || defeated.Role != CombatRole.Grunt || !defeated.IsDefeated) continue;
                        foreach (var renderer in defeated.GetComponentsInChildren<Renderer>(true))
                            Require(!renderer.enabled, "Defeated grunt remained visible in paused farewell: " + defeated.name);
                    }
                    Capture("05_IndoorVictory_Farewell"); captureFrame = lastDialogueAdvanceFrame = Time.frameCount;
                    report.phase = "AdvanceFarewell";
                }
                else Require(Time.frameCount - captureFrame <= 10, "Indoor victory did not reach farewell through normal UI updates.");
                return;
            }
            if (report.phase == "AdvanceFarewell")
            {
                if (View == "Victory")
                {
                    report.reachedVictory = true;
                    Capture("06_IndoorVictory_Result"); captureFrame = Time.frameCount; report.phase = "CaptureVictory";
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

    static void PlacePlayerInHall()
    {
        CombatActor ally = duel.EliteActor;
        Require(ally != null && !ally.IsDefeated, "Living ally missing for indoor observation.");
        var door = Object.FindFirstObjectByType<InnEntranceDoors>();
        Require(door != null, "Front entrance missing for indoor observation.");
        Vector3 inward = duel.eliteArrivalPosition.z >= door.transform.position.z ? Vector3.forward : Vector3.back;
        Vector3 right = Vector3.Cross(Vector3.up, inward);
        var motor = player.GetComponent<CharacterController>();
        var place = typeof(CombatActor).Assembly.GetType("FengChenInn.ActorPlacement").GetMethod("TryPlace", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (float depth in new[] { 5.5f, 4.5f, 6.5f })
            foreach (float side in new[] { -2.5f, 2.5f, -1.8f, 1.8f })
            {
                Vector3 wanted = door.transform.position + inward * depth + right * side;
                wanted.y = duel.eliteArrivalPosition.y;
                Vector3 toward = Vector3.ProjectOnPlane(ally.transform.position - wanted, Vector3.up);
                Quaternion facing = Quaternion.LookRotation(toward);
                if (!(bool)place.Invoke(null, new object[] { motor, wanted, facing, door.transform.position.z })) continue;
                if (Vector3.Dot(player.transform.position - door.transform.position, inward) < 3f) continue;
                RaycastHit[] ground = Physics.RaycastAll(player.transform.position + Vector3.up * .25f, Vector3.down, .7f, ~0, QueryTriggerInteraction.Ignore);
                var supported = ground.Where(hit => hit.collider.GetComponentInParent<CombatActor>() == null && hit.normal.y > .7f).OrderBy(hit => hit.distance).ToArray();
                if (supported.Length == 0) continue;
                fighter.PlaceForStory(player.transform.position, facing);
                report.isolatedPlayerPosition = player.transform.position;
                Physics.SyncTransforms();
                return;
            }
        throw new InvalidOperationException("No safe indoor observation position clear of the front-door route.");
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
        if (!report.allFiveEnteredFromOutside || !report.observationOnly && (!report.automaticFarewell || !report.reachedVictory))
            report.failures.Add("Front-door entrance/indoor battle/farewell/Victory regression did not complete.");
        foreach (var row in report.actors)
            if (!row.defeated && row.minimumY < row.startPosition.y - 2f) report.failures.Add(row.name + " fell more than two meters below its authored starting level.");
        try
        {
            if (savedUiState && ui != null) ui.enabled = oldUiEnabled;
            if (EditorApplication.isPlaying && ui != null)
            { fighter.ClearInput(); Call("Restart", true); report.restoredTitle = View == "Title"; }
        }
        catch (Exception e) { report.failures.Add("Restore title: " + e.GetBaseException().Message); }
        Application.runInBackground = oldBackground; InputSystem.settings.backgroundBehavior = oldInputBackground;
        if (savedUiState && ui != null)
        {
            ui.enabled = oldUiEnabled; ui.SuppressFocusPauseForVerification = oldFocusSuppression;
            report.uiStateRestored = ui.enabled == oldUiEnabled && ui.SuppressFocusPauseForVerification == oldFocusSuppression;
        }
        else report.uiStateRestored = !savedUiState || ui == null;
        savedUiState = false;
        if (!report.uiStateRestored) report.failures.Add("Test UI enabled/focus-suppression state did not restore.");
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
