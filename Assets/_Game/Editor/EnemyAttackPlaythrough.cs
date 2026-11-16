using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Real tutorial grunt AI, clips and sword contacts over live frames. No clock
// mutation, injected damage, synthetic floor, scene creation or scene saving.
public static class EnemyAttackPlaythrough
{
    [Serializable] public sealed class Swing
    {
        public string motion;
        public float duration, sampledThrough;
        public bool endedNaturally;
    }
    [Serializable] public sealed class Report
    {
        public string scope = "Existing InnDuel tutorial grunt: live AI selects both attacks, real clips finish, actual sword damage reaches an idle player. Does not cover elite skills, group combat or full story.";
        public string phase = "NotStarted", outputPath;
        public bool active, completed, passed, restoredTitle, restoredHealth;
        public int firstFrame, lastFrame, actualHits, actualDamage, runtimeErrors;
        public float elapsedSeconds, firstGameTime, lastGameTime;
        public Vector3 playerPosition, gruntPosition;
        public List<Swing> swings = new();
        public List<string> failures = new();
    }
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static Report report = new();
    static InnInterface ui;
    static PrototypeFighter fighter;
    static PrototypeInput input;
    static CombatActor player, grunt;
    static EnemyCombatAI brain;
    static Swing swing;
    static int originalMax, originalHealth;
    static bool healthChanged;
    static double startedAt;

    [Unity.Pipeline.Commands.CliCommand("inncombat", "begin_enemy_test", "Observe real tutorial grunt attacks for up to 20 seconds, then return to title")]
    public static string Begin()
    {
        if (report.active) return Status();
        Detach();
        player = grunt = null; fighter = null; ui = null; input = null; brain = null;
        swing = null; healthChanged = false;
        report = new Report { phase = "Preparing", firstFrame = Time.frameCount, firstGameTime = Time.time };
        startedAt = EditorApplication.timeSinceStartup;
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                throw new InvalidOperationException("Start unpaused Play Mode in existing InnDuel first.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "InnDuel")
                throw new InvalidOperationException("Expected existing InnDuel; no scene was opened.");
            fighter = Object.FindFirstObjectByType<PrototypeFighter>();
            ui = Object.FindFirstObjectByType<InnInterface>();
            if (fighter == null || ui == null) throw new InvalidOperationException("Player/UI not initialized.");
            // Start and finish at the same full-reset title baseline. Existing
            // temporary actors are reset through the game's own coordinator.
            InvokeUi("Restart", true);
            var duel = fighter.GetComponent<DuelEncounter>();
            if (duel == null || !duel.HasRequiredBindings)
                throw new InvalidOperationException("Actual actor/model/weapon bindings are incomplete.");
            player = duel.PlayerActor; grunt = duel.TutorialActor;
            brain = grunt.GetComponent<EnemyCombatAI>();
            input = fighter.GetComponent<PrototypeInput>();
            if (brain == null || input == null) throw new InvalidOperationException("Actual AI/input missing.");
            originalMax = player.MaxHealth; originalHealth = player.Health;
            healthChanged = true;
            player.Configure(CombatRole.Player, CombatTeam.Player, 10000);
            fighter.ClearInput(); fighter.ResetPose();
            player.Invulnerable = player.Guarding = false;
            InvokeUi("BeginTutorialFight");
            if (duel.Stage != DuelEncounter.EncounterStage.Tutorial)
                throw new InvalidOperationException("Tutorial did not enter combat.");
            input.enabled = false;
            brain.enabled = true;
            PositionPair(duel.tutorialPosition);
            brain.ResetBrain(); brain.SetCombatEnabled(true); brain.PreferOpeningTarget(player);
            grunt.AttackStarted += OnAttack;
            grunt.AttackPoseSampled += OnPose;
            player.Damaged += OnDamaged;
            Application.logMessageReceived += OnLog;
            report.phase = "ObservingAI"; report.active = true;
            EditorApplication.update += Tick;
        }
        catch (Exception e) { report.failures.Add(e.GetBaseException().Message); Finish(); }
        return Status();
    }

    [Unity.Pipeline.Commands.CliCommand("inncombat", "status_enemy_test", "Read live enemy-attack test progress and saved result")]
    public static string Status()
    {
        if (report.active) ObserveTime();
        return JsonUtility.ToJson(report, true);
    }

    static void PositionPair(Vector3 authoredPoint)
    {
        var place = typeof(CombatActor).Assembly.GetType("FengChenInn.ActorPlacement")
            .GetMethod("TryPlace", BindingFlags.NonPublic | BindingFlags.Static);
        var playerMotor = player.GetComponent<CharacterController>();
        var enemyMotor = grunt.GetComponent<CharacterController>();
        bool placed = false;
        foreach (Vector3 anchor in new[] { authoredPoint, new Vector3(0f, .05f, 0f), new Vector3(-2f, .05f, 0f), new Vector3(2f, .05f, 0f) })
        {
            if (!(bool)place.Invoke(null, new object[] { enemyMotor, anchor, Quaternion.identity, float.NaN })) continue;
            foreach (float angle in new[] { 25f, -25f, 90f, -90f, 180f })
            {
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 point = grunt.transform.position - direction * 1.3f;
                if (!(bool)place.Invoke(null, new object[] { playerMotor, point, Quaternion.LookRotation(direction), float.NaN })) continue;
                if (!player.HasLineOfSight(grunt)) continue;
                // Require space around both bodies for sword travel, ignoring
                // the floor and actor colliders. Never move scene furniture.
                bool obstructed = false;
                Vector3 center = (player.transform.position + grunt.transform.position) * .5f + Vector3.up;
                foreach (var hit in Physics.OverlapBox(center, new Vector3(1.35f, .55f, 1.35f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.GetComponentInParent<CombatActor>() == null) { obstructed = true; break; }
                if (obstructed) continue;
                Vector3 toPlayer = Vector3.ProjectOnPlane(player.transform.position - grunt.transform.position, Vector3.up);
                if (toPlayer.magnitude > brain.engageDistance || toPlayer.magnitude < .7f) continue;
                fighter.PlaceForStory(player.transform.position, Quaternion.LookRotation(-toPlayer));
                grunt.transform.rotation = Quaternion.LookRotation(toPlayer);
                placed = true;
                break;
            }
            if (placed) break;
        }
        if (!placed) throw new InvalidOperationException("No clear existing hall stance found; scene was not changed.");
        var orbit = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
        if (orbit == null) throw new InvalidOperationException("Actual orbit camera missing.");
        orbit.enabled = true;
        orbit.FrameActors(player.transform, grunt.transform, true);
        Physics.SyncTransforms();
        report.playerPosition = player.transform.position; report.gruntPosition = grunt.transform.position;
    }

    static void Tick()
    {
        if (!report.active) return;
        try
        {
            ObserveTime();
            if (!EditorApplication.isPlaying || player == null || grunt == null)
                throw new InvalidOperationException("Play Mode/actors ended before the test completed.");
            fighter.ClearInput(); // No player actions; ordinary hit reaction/knockback remain enabled.
            fighter.GuardRequested = false;
            if (swing != null && !grunt.IsAttacking)
            {
                swing.endedNaturally = swing.sampledThrough >= swing.duration - .001f;
                if (!swing.endedNaturally) report.failures.Add("Attack stopped before its real duration: " + swing.motion);
                swing = null;
            }
            bool light = report.swings.Exists(s => s.motion == CombatMotion.Light.ToString() && s.endedNaturally);
            bool heavy = report.swings.Exists(s => s.motion == CombatMotion.Heavy.ToString() && s.endedNaturally);
            if (light && heavy && report.actualHits > 0) { Finish(); return; }
            if (report.elapsedSeconds >= 20f)
            { report.failures.Add("20-second timeout: expected both AI-selected attacks to finish and actual sword damage."); Finish(); }
        }
        catch (Exception e) { report.failures.Add(e.GetBaseException().Message); Finish(); }
    }

    static void OnAttack(CombatAttack attack)
    {
        swing = new Swing { motion = attack.motion.ToString(), duration = attack.duration };
        report.swings.Add(swing);
    }
    static void OnPose(CombatMotion motion, float before, float clock, Vector3 bladeBase, Vector3 bladeTip)
    {
        if (swing != null && swing.motion == motion.ToString()) swing.sampledThrough = Mathf.Max(swing.sampledThrough, clock);
    }
    static void OnDamaged(CombatActor source, int damage)
    {
        if (source == grunt && damage > 0) { report.actualHits++; report.actualDamage += damage; }
    }
    static void OnLog(string message, string trace, LogType kind)
    {
        if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert)
        { report.runtimeErrors++; if (report.failures.Count < 12) report.failures.Add(message); }
    }
    static void ObserveTime()
    {
        report.elapsedSeconds = (float)(EditorApplication.timeSinceStartup - startedAt);
        report.lastFrame = Time.frameCount; report.lastGameTime = Time.time;
    }
    static void InvokeUi(string method, params object[] args) => typeof(InnInterface).GetMethod(method, Hidden).Invoke(ui, args);
    static void Finish()
    {
        ObserveTime();
        report.active = false; Detach();
        if (!report.swings.Exists(s => s.motion == "Light" && s.endedNaturally)) report.failures.Add("No complete real light swing observed.");
        if (!report.swings.Exists(s => s.motion == "Heavy" && s.endedNaturally)) report.failures.Add("No complete real heavy swing observed.");
        if (report.actualHits < 1) report.failures.Add("No real sword damage observed.");
        if (report.lastFrame <= report.firstFrame + 5 || report.lastGameTime <= report.firstGameTime + .1f)
            report.failures.Add("Insufficient real frame/game-time advancement.");
        try
        {
            if (EditorApplication.isPlaying)
            {
                if (brain != null) brain.SetCombatEnabled(false);
                if (player != null && healthChanged) player.Configure(CombatRole.Player, CombatTeam.Player, originalMax);
                if (ui != null)
                {
                    InvokeUi("Restart", true);
                    report.restoredTitle = typeof(InnInterface).GetField("view", Hidden).GetValue(ui).ToString() == "Title";
                }
                if (player != null && healthChanged)
                {
                    player.ResetActor(originalHealth);
                    report.restoredHealth = player.MaxHealth == originalMax && player.Health == originalHealth;
                }
            }
            else { report.restoredTitle = report.restoredHealth = true; } // Play Mode teardown restores authored state.
        }
        catch (Exception e) { report.failures.Add("Cleanup: " + e.GetBaseException().Message); }
        if (healthChanged && (!report.restoredTitle || !report.restoredHealth)) report.failures.Add("Title/health restoration was not confirmed.");
        report.completed = true; report.passed = report.failures.Count == 0;
        report.phase = report.passed ? "Completed" : "Failed";
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
        Directory.CreateDirectory(folder);
        report.outputPath = Path.Combine(folder, "EnemyAttackPlaythrough.json");
        File.WriteAllText(report.outputPath, JsonUtility.ToJson(report, true));
    }
    static void Detach()
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        if (grunt != null) { grunt.AttackStarted -= OnAttack; grunt.AttackPoseSampled -= OnPose; }
        if (player != null) player.Damaged -= OnDamaged;
    }
}
