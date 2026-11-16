using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Component rules only: dummy clips and temporary entities in the existing Play Mode scene.
// This does not claim to validate the real ledger, tutorial presentation, entrance animation or scene anchors.
public static class InnProgressionRules
{
    [Serializable] public sealed class Report
    {
        public string scope = "Isolated live-component rules with dummy animation clips; no scene/assets saved. Elite entrance completion is explicitly seeded as a fixture prerequisite, not tested here.";
        public bool passed;
        public int checks, remainingFixtures;
        public List<string> successes = new(), failures = new();
    }
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Vector3 Origin = new(16000f, 150f, 16000f);

    [Unity.Pipeline.Commands.CliCommand("inncombat", "progression_rules", "Check ledger/tutorial gates, five-enemy rescue, protected ally and reset contracts")]
    public static string Run()
    {
        var report = new Report();
        var before = new HashSet<CombatActor>(Object.FindObjectsByType<CombatActor>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        GameObject fixture = null;
        AnimationClip clip = null;
        AholoMotionSet motions = null;
        void Check(bool value, string label)
        {
            report.checks++;
            (value ? report.successes : report.failures).Add(label);
        }
        try
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Run in existing Play Mode; this tool does not open or create a scene.");
            fixture = new GameObject("INN_PROGRESSION_RULE_FIXTURE");
            var floor = new GameObject("Temporary rule floor");
            floor.transform.SetParent(fixture.transform);
            floor.transform.position = Origin - Vector3.up * .25f;
            floor.AddComponent<BoxCollider>().size = new Vector3(48f, .5f, 48f);
            clip = new AnimationClip { name = "Progression rule dummy pose" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Constant(0f, 2f, 0f));
            motions = ScriptableObject.CreateInstance<AholoMotionSet>();
            motions.idle = motions.explorationIdle = motions.walk = motions.light = motions.heavy = motions.skill = clip;
            motions.hit = motions.knockback = motions.death = clip;
            var player = CreateActor(fixture.transform, motions, "Rule player", CombatRole.Player, CombatTeam.Player, Origin + Vector3.up * .05f);
            var elitePrefab = CreateActor(fixture.transform, motions, "Rule elite template", CombatRole.Elite, CombatTeam.Enemy, Origin + Vector3.right * 15f);
            var gruntPrefab = CreateActor(fixture.transform, motions, "Rule grunt template", CombatRole.Grunt, CombatTeam.Enemy, Origin + Vector3.left * 15f);
            elitePrefab.gameObject.SetActive(false); gruntPrefab.gameObject.SetActive(false);
            var duel = player.gameObject.AddComponent<DuelEncounter>();
            duel.eliteActorPrefab = elitePrefab; duel.gruntActorPrefab = gruntPrefab;
            duel.eliteMotions = duel.gruntMotions = motions;
            duel.tutorialPosition = Origin + new Vector3(0f, .05f, 2f);
            duel.eliteArrivalPosition = Origin + new Vector3(.7f, .05f, 5f);
            duel.eliteGuardPosition = Origin + new Vector3(0f, .05f, 7f);
            duel.rescueArrivalPosition = Origin + new Vector3(0f, .05f, 10f);
            duel.ResetEncounter();
            Physics.SyncTransforms();

            Check(duel.Stage == DuelEncounter.EncounterStage.Arrival && !duel.TutorialActor.gameObject.activeSelf
                && !duel.EliteActor.gameObject.activeSelf, "Arrival keeps both opponents hidden");
            Check(!duel.TutorialSentryRevealed && !duel.TutorialInstructionsCompleted, "Restart clears ledger/tutorial completion markers");
            Check(!duel.BeginTutorial() && !duel.CompleteTutorialInstructions(), "Neither battle nor instruction completion bypasses sentry reveal");
            Check(duel.RevealTutorialSentry() && duel.TutorialActor.gameObject.activeInHierarchy,
                "Ledger callback reveals the actual sentry");
            Check(duel.Stage == DuelEncounter.EncounterStage.Arrival && !duel.IsCombatActive
                && duel.TutorialActor.Invulnerable && duel.TutorialActor.ControlLocked,
                "Revealed sentry stays passive and invulnerable for dialogue and practice");
            Check(!duel.TutorialActor.ReceiveDamage(player, 1000, 0f, 0f) && !duel.BeginTutorial(),
                "Practice cannot damage the sentry or start combat before instruction completion");
            Check(duel.CompleteTutorialInstructions() && duel.TutorialInstructionsCompleted && !duel.TutorialActor.Invulnerable,
                "Instruction completion opens the explicit battle gate");
            player.ResetActor(17);
            Check(duel.BeginTutorial() && duel.IsCombatActive && player.Health == player.MaxHealth,
                "Tutorial starts after practice and heals the player");
            player.ReceiveDamage(duel.TutorialActor, 1000, 0f, 0f);
            Check(duel.Stage == DuelEncounter.EncounterStage.Failed && !duel.BeginEliteDuel() && !duel.BeginRescueBattle(),
                "Player defeat still stops later stages");
            Check(duel.RetryCurrentBattle() && duel.Stage == DuelEncounter.EncounterStage.Tutorial
                && duel.TutorialInstructionsCompleted && player.Health == player.MaxHealth,
                "Retry restores this fight at full health without replaying ledger/practice");
            duel.TutorialActor.ReceiveDamage(player, 1000, 0f, 0f);
            Check(duel.Stage == DuelEncounter.EncounterStage.EliteReady && !duel.EliteActor.gameObject.activeSelf,
                "Tutorial victory does not prematurely display the arriving elite");
            Check(!duel.MakeEliteAlly() && !duel.BeginRescueBattle(), "Only a won elite duel can unlock the ally/rescue");

            // Dedicated entrance playthrough owns locomotion. Seed its completed prerequisite here.
            typeof(DuelEncounter).GetProperty("EliteEntranceComplete").GetSetMethod(true).Invoke(duel, new object[] { true });
            typeof(DuelEncounter).GetField("eliteEntranceReadyAt", Hidden).SetValue(duel, Time.time);
            player.ResetActor(19);
            Check(duel.BeginEliteDuel() && player.Health == player.MaxHealth, "Elite battle also begins at full player health");
            duel.EliteActor.ReceiveDamage(player, 1000, 0f, 0f);
            Check(duel.EliteVictoryAchieved && duel.MakeEliteAlly() && duel.EliteActor.Team == CombatTeam.Player,
                "Actual elite defeat permits the allied transition");
            Check(duel.EliteActor.Health == 600 && duel.EliteActor.MaxHealth == 600
                && duel.EliteActor.GruntDamageHealthFloor == 0, "Ally has 600 health but rescue-only protection is not yet enabled");
            Check(duel.MakeEliteAlly() && duel.EliteActor.Health == 600, "Repeated ally conversion is stable");
            player.ResetActor(23);
            Check(duel.BeginRescueBattle() && duel.RemainingEnemies == 5 && player.Health == player.MaxHealth,
                "Rescue starts five grunts and heals the player");
            Check(duel.EnemyActors.All(actor => actor.Role == CombatRole.Grunt && actor.Team == CombatTeam.Enemy),
                "All five reinforcements remain hostile grunts");
            Check(duel.EliteActor.GruntDamageHealthFloor == 1, "Only active rescue enables the allied elite's one-health floor");
            var attacker = duel.EnemyActors[0];
            int allyDeaths = 0;
            duel.EliteActor.Defeated += _ => allyDeaths++;
            duel.EliteActor.ReceiveDamage(attacker, 100000, 0f, 0f);
            duel.EliteActor.ReceiveDamage(attacker, 100000, 0f, 0f);
            Check(duel.EliteActor.Health == 1 && !duel.EliteActor.IsDefeated && allyDeaths == 0,
                "Repeated lethal grunt damage leaves the ally alive at one health");
            player.ReceiveDamage(attacker, 100000, 0f, 0f);
            Check(duel.Stage == DuelEncounter.EncounterStage.Failed && duel.EliteActor.GruntDamageHealthFloor == 0,
                "Player defeat fails rescue and disables its temporary protection");
            Check(duel.RetryCurrentBattle() && duel.Stage == DuelEncounter.EncounterStage.Rescue
                && player.Health == player.MaxHealth && duel.EliteActor.Health == 600 && duel.EliteActor.GruntDamageHealthFloor == 1,
                "Rescue retry restores player/ally and re-enables the scoped protection");
            foreach (var grunt in duel.EnemyActors.ToArray()) grunt.ReceiveDamage(player, 1000, 0f, 0f);
            Check(duel.Stage == DuelEncounter.EncounterStage.Complete && duel.EliteActor.GruntDamageHealthFloor == 0,
                "Defeating all five completes rescue and removes protection");
            duel.ResetEncounter();
            Check(!duel.TutorialActor.gameObject.activeSelf && !duel.TutorialSentryRevealed && !duel.TutorialInstructionsCompleted
                && !duel.EliteVictoryAchieved && duel.EliteActor.Team == CombatTeam.Enemy
                && duel.EliteActor.MaxHealth == duel.eliteMaxHealth && duel.EliteActor.GruntDamageHealthFloor == 0,
                "Full restart restores hidden tutorial, enemy elite health and no protection");

            var probe = CreateActor(fixture.transform, motions, "Protection probe", CombatRole.Elite, CombatTeam.Player, Origin + Vector3.right * 10f);
            var hostileElite = CreateActor(fixture.transform, motions, "Hostile elite probe", CombatRole.Elite, CombatTeam.Enemy, Origin + Vector3.right * 12f);
            probe.SetGruntDamageHealthFloor(1);
            probe.ReceiveDamage(hostileElite, 1000, 0f, 0f);
            Check(probe.IsDefeated, "Grunt protection does not silently grant invulnerability to other enemy roles");
            player.SetGruntDamageHealthFloor(1);
            duel.TutorialActor.SetGruntDamageHealthFloor(1);
            Check(player.GruntDamageHealthFloor == 0 && duel.TutorialActor.GruntDamageHealthFloor == 0,
                "The player and grunts cannot acquire allied-elite story protection");
        }
        catch (Exception e) { report.failures.Add(e.GetBaseException().ToString()); }
        finally
        {
            // Coordinator destruction schedules its spawns; explicitly remove only new fixtures now.
            if (fixture != null) Object.DestroyImmediate(fixture);
            foreach (var actor in Object.FindObjectsByType<CombatActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (actor != null && !before.Contains(actor)) Object.DestroyImmediate(actor.gameObject);
            if (motions != null) Object.DestroyImmediate(motions);
            if (clip != null) Object.DestroyImmediate(clip);
            Physics.SyncTransforms();
            report.remainingFixtures = Object.FindObjectsByType<CombatActor>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(actor => !before.Contains(actor));
            Check(report.remainingFixtures == 0, "All temporary rule actors removed");
        }
        report.passed = report.failures.Count == 0;
        string json = JsonUtility.ToJson(report, true);
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "InnProgressionRules.json"), json);
        return json;
    }

    static CombatActor CreateActor(Transform parent, AholoMotionSet motions, string name, CombatRole role, CombatTeam team, Vector3 position)
    {
        var body = new GameObject(name);
        body.transform.SetParent(parent);
        body.transform.position = position;
        var controller = body.AddComponent<CharacterController>();
        controller.height = 1.8f; controller.center = Vector3.up * .9f; controller.radius = .3f;
        var visual = new GameObject("Dummy visual"); visual.transform.SetParent(body.transform, false);
        var animator = visual.AddComponent<Animator>();
        var driver = body.AddComponent<AholoAnimationDriver>(); driver.Configure(motions, animator);
        var blade = new GameObject("Dummy blade"); blade.transform.SetParent(body.transform, false);
        blade.transform.localPosition = Vector3.up * .9f;
        var actor = body.AddComponent<CombatActor>();
        actor.Configure(role, team, 100); actor.WeaponRoot = blade.transform; actor.RefreshBindings();
        return actor;
    }
}
