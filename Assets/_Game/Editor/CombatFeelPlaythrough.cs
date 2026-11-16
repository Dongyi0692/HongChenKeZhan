using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Live-frame smoke test of the real InnDuel actors, clips and weapon sweep.
// Never creates/saves a scene, replaces models, reflects attack clocks or injects damage.
public static class CombatFeelPlaythrough
{
    [Serializable] public sealed class Report
    {
        public string scope = "Existing InnDuel scene, actual actors/animation/weapon collision; input driven over real frames. Temporary test health and placement remain in Play Mode until stopped.";
        public bool active, completed, passed, framesAdvanced, gameTimeAdvanced;
        public string phase, scene, outputPath;
        public int frameStart, frameEnd, lightHitCount, heavyHitCount, runtimeErrors;
        public int lightRequests, heavyRequests, dodgeRequests;
        public float gameTimeStart, gameTimeEnd, elapsedSeconds, initialAimAngle, turnDegrees;
        public bool lightStarted, heavyStarted, guardFromRecovery, dodgeFromRecovery;
        public float lightRecoveryAt, lightFullDuration, heavyRecoveryAt, heavyFullDuration;
        public List<string> failures = new();
    }
    [Serializable] public sealed class ActorState
    {
        public string name, role, team, model, motionSet, motion, weapon;
        public Vector3 position;
        public int health;
        public bool active, bound, humanoid, attacking, controlLocked;
        public float attackElapsed;
    }
    [Serializable] public sealed class SceneState
    {
        public bool playing, paused;
        public int frame;
        public float gameTime, timeScale;
        public string scene, view, phase;
        public List<ActorState> actors = new();
    }
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static Report report = new() { phase = "NotStarted" };
    static PrototypeFighter fighter;
    static CombatActor player, enemy;
    static PrototypeInput input;
    static InnInterface ui;
    static double startedAt, phaseAt;
    static int phaseFrame;
    static Quaternion lightStartFacing;
    static float dodgeRequestedGameTime;

    [Unity.Pipeline.Commands.CliCommand("inncombat", "begin_playtest", "Drive real InnDuel combat over live frames without saving the scene")]
    public static string Begin()
    {
        if (report.active) return Status();
        Detach();
        report = new Report { phase = "Preparing", frameStart = Time.frameCount, gameTimeStart = Time.time };
        startedAt = EditorApplication.timeSinceStartup;
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                throw new InvalidOperationException("Start unpaused Play Mode in the existing InnDuel scene first.");
            report.scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (report.scene != "InnDuel") throw new InvalidOperationException("Expected existing InnDuel; no scene was opened or created by this command.");
            fighter = Object.FindFirstObjectByType<PrototypeFighter>();
            ui = Object.FindFirstObjectByType<InnInterface>();
            if (fighter == null || ui == null) throw new InvalidOperationException("Actual player/UI have not initialized.");
            var duel = fighter.GetComponent<DuelEncounter>();
            if (duel == null) throw new InvalidOperationException("Actual encounter coordinator missing.");
            SetEnum(ui, "part", "TutorialFight");
            var viewType = typeof(InnInterface).GetNestedType("View", BindingFlags.NonPublic);
            typeof(InnInterface).GetMethod("Show", Hidden).Invoke(ui, new[] { Enum.Parse(viewType, "Play") });
            input = fighter.GetComponent<PrototypeInput>();
            if (input != null) input.enabled = false; // No hardware input overwrites the driven commands.
            if (duel.Stage != DuelEncounter.EncounterStage.Arrival) duel.ResetEncounter();
            if (!duel.BeginTutorial()) throw new InvalidOperationException("Tutorial could not start: " + duel.MissingBindingReason);
            player = duel.PlayerActor;
            enemy = duel.TutorialActor;
            if (player == null || enemy == null || !duel.HasRequiredBindings)
                throw new InvalidOperationException("Real model/motion/weapon bindings are incomplete.");
            var brain = enemy.GetComponent<EnemyCombatAI>();
            if (brain != null) { brain.SetCombatEnabled(false); brain.enabled = false; }
            enemy.Configure(CombatRole.Grunt, CombatTeam.Enemy, 1000);
            fighter.ClearInput();
            player.CancelAttack();
            player.HitLanded -= OnHit;
            player.HitLanded += OnHit;
            fighter.ActionPerformed -= OnActionPerformed;
            fighter.ActionPerformed += OnActionPerformed;
            Application.logMessageReceived += OnLog;
            report.active = true;
            EditorApplication.update += Tick;
            PositionPlayer();
            SetPhase("SettleLight");
        }
        catch (Exception e) { Fail(e.GetBaseException().Message); }
        return Status();
    }

    [Unity.Pipeline.Commands.CliCommand("inncombat", "status", "Read live combat playthrough progress and result")]
    public static string Status()
    {
        if (report.active) ObserveTime();
        return JsonUtility.ToJson(report, true);
    }

    [Unity.Pipeline.Commands.CliCommand("inncombat", "state", "Inspect actual scene actors and live frame state")]
    public static string State()
    {
        var currentUi = ui != null ? ui : Object.FindFirstObjectByType<InnInterface>();
        var state = new SceneState
        {
            playing = EditorApplication.isPlaying, paused = EditorApplication.isPaused,
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            frame = Time.frameCount, gameTime = Time.time, timeScale = Time.timeScale,
            phase = report.phase, view = currentUi != null ? typeof(InnInterface).GetField("view", Hidden).GetValue(currentUi).ToString() : "Unknown"
        };
        foreach (var actor in Object.FindObjectsByType<CombatActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var driver = actor.Animation;
            var animator = driver != null ? driver.targetAnimator : null;
            state.actors.Add(new ActorState
            {
                name = actor.name, role = actor.Role.ToString(), team = actor.Team.ToString(),
                position = actor.transform.position, health = actor.Health, active = actor.gameObject.activeInHierarchy,
                bound = driver != null && driver.Ready && actor.HasWeapon, humanoid = animator != null && animator.isHuman,
                model = animator != null ? animator.name : "Missing", motionSet = driver != null && driver.motionSet != null ? driver.motionSet.name : "Missing",
                motion = driver != null ? driver.CurrentMotion.ToString() : "Missing", weapon = actor.WeaponRoot != null ? actor.WeaponRoot.name : "Missing",
                attacking = actor.IsAttacking, controlLocked = actor.ControlLocked, attackElapsed = actor.AttackElapsed
            });
        }
        return JsonUtility.ToJson(state, true);
    }

    static void Tick()
    {
        if (!report.active) return;
        try
        {
            ObserveTime();
            if (!EditorApplication.isPlaying) { Fail("Play Mode ended before verification completed."); return; }
            if (report.elapsedSeconds > 30f) { Fail("30-second live-frame timeout; inspect timeScale/UI pause and player grounding."); return; }
            if (EditorApplication.isPaused || Time.timeScale <= 0f)
            {
                if (EditorApplication.timeSinceStartup - phaseAt > 3d) Fail("Editor/game is paused; live playback could not advance.");
                return;
            }
            if (Time.frameCount <= phaseFrame) return;
            double phaseTime = EditorApplication.timeSinceStartup - phaseAt;
            if (phaseTime > 9d) { Fail("Phase timed out: " + report.phase); return; }
            switch (report.phase)
            {
                case "SettleLight":
                    if (!fighter.IsGrounded || phaseTime < .3d) return;
                    lightStartFacing = fighter.transform.rotation;
                    report.initialAimAngle = Vector3.Angle(fighter.transform.forward, Vector3.ProjectOnPlane(enemy.transform.position - fighter.transform.position, Vector3.up));
                    // Request may be queued. Observe ActionPerformed instead of resending it.
                    SetPhase("AwaitLightStart");
                    report.lightRequests++;
                    fighter.Attack(false);
                    break;
                case "AwaitLightStart":
                    if (phaseTime > 1d) Fail("The single light request did not start before its buffer expired.");
                    break;
                case "Light":
                    report.turnDegrees = Mathf.Max(report.turnDegrees, Quaternion.Angle(lightStartFacing, fighter.transform.rotation));
                    if (!fighter.CanExitAttackRecovery) return;
                    report.lightRecoveryAt = player.AttackElapsed;
                    fighter.GuardRequested = true;
                    SetPhase("GuardRecovery");
                    break;
                case "GuardRecovery":
                    if (!fighter.IsGuarding || player.IsAttacking) return;
                    report.guardFromRecovery = report.lightRecoveryAt < report.lightFullDuration - .05f;
                    fighter.GuardRequested = false;
                    enemy.ResetActor();
                    PositionPlayer();
                    SetPhase("SettleHeavy");
                    break;
                case "SettleHeavy":
                    if (!fighter.IsGrounded || phaseTime < .3d) return;
                    SetPhase("AwaitHeavyStart");
                    report.heavyRequests++;
                    fighter.Attack(true);
                    break;
                case "AwaitHeavyStart":
                    if (phaseTime > 1d) Fail("The single heavy request did not start before its buffer expired.");
                    break;
                case "Heavy":
                    if (!fighter.CanExitAttackRecovery) return;
                    report.heavyRecoveryAt = player.AttackElapsed;
                    dodgeRequestedGameTime = Time.time;
                    SetPhase("AwaitDodgeStart");
                    report.dodgeRequests++;
                    fighter.Dodge();
                    break;
                case "AwaitDodgeStart":
                    if (phaseTime > 1d) Fail("The single recovery dodge request did not start before its buffer expired.");
                    break;
                case "Dodge":
                    if (fighter.State != PrototypeFighter.ActionState.Ready || phaseTime < .5d) return;
                    Finish();
                    break;
            }
        }
        catch (Exception e) { Fail(e.GetBaseException().ToString()); }
    }

    static void PositionPlayer()
    {
        fighter.ClearInput();
        var controller = fighter.GetComponent<CharacterController>();
        var placement = typeof(CombatActor).Assembly.GetType("FengChenInn.ActorPlacement").GetMethod("TryPlace", BindingFlags.Static | BindingFlags.NonPublic);
        bool placed = false;
        foreach (float side in new[] { 25f, -25f })
        {
            Vector3 approach = Quaternion.Euler(0f, side, 0f) * Vector3.forward;
            Vector3 point = enemy.transform.position - approach * 1.35f;
            if (!(bool)placement.Invoke(null, new object[] { controller, point, Quaternion.identity, float.NaN })) continue;
            if (!player.HasLineOfSight(enemy)) continue;
            fighter.PlaceForStory(fighter.transform.position, Quaternion.identity);
            placed = true;
            break;
        }
        if (!placed) throw new InvalidOperationException("No clear nearby test stance in existing hall; scene was not changed.");
        enemy.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(fighter.transform.position - enemy.transform.position, Vector3.up));
        var camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("Main camera missing.");
        var orbit = camera.GetComponent<OrbitCamera>();
        if (orbit != null)
        {
            typeof(OrbitCamera).GetField("yaw", Hidden).SetValue(orbit, 0f);
            typeof(OrbitCamera).GetField("pitch", Hidden).SetValue(orbit, 24f);
            orbit.enabled = true;
            typeof(OrbitCamera).GetMethod("LateUpdate", Hidden).Invoke(orbit, null);
        }
        Physics.SyncTransforms();
    }

    static void OnHit(CombatActor target, int damage)
    {
        if (!report.active || target != enemy || damage <= 0 || player.CurrentAttack == null) return;
        if (player.CurrentAttack.motion == CombatMotion.Light) report.lightHitCount++;
        if (player.CurrentAttack.motion == CombatMotion.Heavy) report.heavyHitCount++;
    }
    static void OnActionPerformed(CombatMotion motion)
    {
        if (!report.active) return;
        // This event is raised after the real fighter has changed state, including
        // actions executed from the input buffer on a later frame.
        if (motion == CombatMotion.Light && report.phase == "AwaitLightStart"
            && fighter.State == PrototypeFighter.ActionState.LightAttack && player.IsAttacking)
        {
            report.lightStarted = true;
            report.lightFullDuration = player.AttackDuration;
            SetPhase("Light");
        }
        else if (motion == CombatMotion.Heavy && report.phase == "AwaitHeavyStart"
            && fighter.State == PrototypeFighter.ActionState.HeavyAttack && player.IsAttacking)
        {
            report.heavyStarted = true;
            report.heavyFullDuration = player.AttackDuration;
            SetPhase("Heavy");
        }
        else if (motion == CombatMotion.Dodge && report.phase == "AwaitDodgeStart"
            && fighter.State == PrototypeFighter.ActionState.Dodge && !player.IsAttacking)
        {
            report.heavyRecoveryAt += Mathf.Max(0f, Time.time - dodgeRequestedGameTime);
            report.dodgeFromRecovery = report.heavyRecoveryAt < report.heavyFullDuration - .05f;
            SetPhase("Dodge");
        }
    }
    static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.runtimeErrors++;
    }
    static void SetPhase(string name) { report.phase = name; phaseAt = EditorApplication.timeSinceStartup; phaseFrame = Time.frameCount; }
    static void SetEnum(object instance, string fieldName, string value)
    {
        var field = instance.GetType().GetField(fieldName, Hidden);
        field.SetValue(instance, Enum.Parse(field.FieldType, value));
    }
    static void ObserveTime()
    {
        report.frameEnd = Time.frameCount; report.gameTimeEnd = Time.time;
        report.framesAdvanced = report.frameEnd > report.frameStart + 5;
        report.gameTimeAdvanced = report.gameTimeEnd > report.gameTimeStart + .1f;
        if (report.active) report.elapsedSeconds = (float)(EditorApplication.timeSinceStartup - startedAt);
    }
    static void Fail(string reason) { report.failures.Add(reason); Finish(); }
    static void Finish()
    {
        ObserveTime();
        if (!report.lightStarted || report.lightHitCount < 1) report.failures.Add("No actual light-attack weapon hit observed.");
        if (!report.heavyStarted || report.heavyHitCount < 1) report.failures.Add("No actual heavy-attack weapon hit observed.");
        if (report.turnDegrees < 5f) report.failures.Add("Expected visible early attack-facing correction was not observed.");
        if (!report.guardFromRecovery || !report.dodgeFromRecovery) report.failures.Add("Early recovery guard/dodge was not confirmed.");
        if (!report.framesAdvanced || !report.gameTimeAdvanced) report.failures.Add("Live frames and game time did not advance sufficiently.");
        if (report.lightRequests != 1 || report.heavyRequests != 1 || report.dodgeRequests != 1)
            report.failures.Add("Expected exactly one request for each driven attack/dodge.");
        if (report.runtimeErrors > 0) report.failures.Add("Runtime errors during playthrough: " + report.runtimeErrors);
        report.active = false; report.completed = true; report.passed = report.failures.Count == 0;
        report.phase = report.passed ? "Completed" : "Failed";
        Detach();
        if (fighter != null) fighter.ClearInput();
        // Leave the real game visible for capture. Stopping Play Mode restores the authored scene.
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
        Directory.CreateDirectory(folder);
        report.outputPath = Path.Combine(folder, "CombatFeelPlaythrough.json");
        File.WriteAllText(report.outputPath, JsonUtility.ToJson(report, true));
    }
    static void Detach()
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        if (player != null) player.HitLanded -= OnHit;
        if (fighter != null) fighter.ActionPerformed -= OnActionPerformed;
    }
}
