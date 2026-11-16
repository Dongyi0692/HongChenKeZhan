using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Run in Play Mode in the existing scene: AholoRulesVerification.Run().
// Dummy clips prove component rules only, never the quality/binding of exported Aholo animation.
public static class AholoRulesVerification
{
    [Serializable]
    public sealed class Result
    {
        public string scope = "Synchronous real-component rules with explicit physics queries; unaffected by timeScale; no frame-advance/playability claim. In-memory dummy clips are not Aholo animation validation.";
        public bool passed;
        public int checks;
        public int fixtureRemainders;
        public int frameStart, frameEnd, runtimeErrors;
        public bool framesAdvanced, gameTimeAdvanced;
        public GroundingObservation groundingProbe;
        public List<string> failures = new();
        public List<string> successes = new();
    }
    [Serializable]
    public sealed class GroundingObservation
    {
        public bool ran, afterDown, afterHorizontal, nextFrame;
        public bool fighterBeforeHorizontal, fighterControllerAfterHorizontal, fighterAfterHorizontal;
        public int frameMoved, frameObserved;
        public string downFlags, horizontalFlags, error;
    }
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Vector3 TestOrigin = new(10000f, 100f, 10000f);
    static bool menuRunning;
    static int initialFrame, observedErrors;
    static float originalTimeScale, initialGameTime;
    static double probeStarted;
    static GameObject groundingFixture;
    static CharacterController groundingController;
    static PrototypeFighter groundingFighter;
    static GroundingObservation groundingObservation;

    [MenuItem("FengChenInn/Aholo/Verify Combat Rules")]
    public static void RunMenu()
    {
        if (menuRunning) return;
        if (!EditorApplication.isPlaying) { Debug.LogWarning("Enter Play Mode in InnDuel before running combat rule verification."); return; }
        menuRunning = true;
        initialFrame = Time.frameCount;
        initialGameTime = Time.time;
        originalTimeScale = Time.timeScale;
        Time.timeScale = 1f;
        observedErrors = 0;
        probeStarted = EditorApplication.timeSinceStartup;
        StartGroundingProbe();
        Application.logMessageReceived += CountError;
        EditorApplication.update += FinishMenuAfterFrames;
    }

    static void CountError(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) observedErrors++;
    }

    static void FinishMenuAfterFrames()
    {
        if (EditorApplication.isPlaying && Time.frameCount - initialFrame < 5 && EditorApplication.timeSinceStartup - probeStarted < 8d) return;
        EditorApplication.update -= FinishMenuAfterFrames;
        try
        {
            int finalFrame = Time.frameCount;
            float finalGameTime = Time.time;
            Result result = JsonUtility.FromJson<Result>(Run());
            result.frameStart = initialFrame;
            result.frameEnd = finalFrame;
            result.framesAdvanced = finalFrame > initialFrame;
            result.gameTimeAdvanced = finalGameTime > initialGameTime;
            result.runtimeErrors = observedErrors;
            groundingObservation.frameObserved = Time.frameCount;
            groundingObservation.nextFrame = groundingController != null && groundingController.isGrounded;
            groundingObservation.fighterBeforeHorizontal = groundingFighter != null && groundingFighter.IsGrounded;
            if (groundingFighter != null)
            {
                var fighterController = groundingFighter.GetComponent<CharacterController>();
                fighterController.Move(Vector3.forward * .02f);
                groundingObservation.fighterControllerAfterHorizontal = fighterController.isGrounded;
                groundingObservation.fighterAfterHorizontal = groundingFighter.IsGrounded;
            }
            result.groundingProbe = groundingObservation;
            if (!groundingObservation.ran || !groundingObservation.afterDown)
                result.failures.Add("Grounding probe did not establish its grounded precondition: " + groundingObservation.error);
            result.checks++;
            if (!groundingObservation.fighterBeforeHorizontal || !groundingObservation.fighterAfterHorizontal)
                result.failures.Add("Real PrototypeFighter did not retain its gravity-move ground contact after a horizontal move.");
            else result.successes.Add("Real PrototypeFighter retains grounded state when a later horizontal Move overwrites controller.isGrounded");
            if (!result.framesAdvanced || !result.gameTimeAdvanced)
                result.failures.Add("The separate live-frame probe did not observe advancing frames and game time.");
            if (observedErrors > 0) result.failures.Add("Console errors/exceptions observed during verification: " + observedErrors);
            result.passed = result.failures.Count == 0;
            Directory.CreateDirectory("Verification");
            string path = "Verification/AholoRules_20261116.json";
            File.WriteAllText(path, JsonUtility.ToJson(result, true));
            Debug.Log("AHOLO_RULES_VERIFICATION_COMPLETE passed=" + result.passed + " checks=" + result.checks + " path=" + Path.GetFullPath(path));
        }
        finally
        {
            Application.logMessageReceived -= CountError;
            Time.timeScale = originalTimeScale;
            if (groundingFixture != null) Object.DestroyImmediate(groundingFixture);
            groundingController = null;
            groundingFighter = null;
            groundingFixture = null;
            menuRunning = false;
        }
    }

    // Isolated real PhysX observation of the Update gravity Move + LateUpdate XZ Move pattern.
    // A false afterHorizontal is recorded as evidence, not assumed to be an engine defect.
    static void StartGroundingProbe()
    {
        groundingObservation = new GroundingObservation();
        try
        {
            groundingFixture = new GameObject("RULE_TEST_ONLY_grounding_probe");
            groundingFixture.transform.position = TestOrigin + Vector3.right * 20f;
            var floor = new GameObject("ProbeFloor");
            floor.transform.SetParent(groundingFixture.transform, false);
            floor.transform.localPosition = new Vector3(0f, -.25f, 0f);
            floor.AddComponent<BoxCollider>().size = new Vector3(10f, .5f, 10f);
            var mover = new GameObject("ProbeController");
            mover.transform.SetParent(groundingFixture.transform, false);
            mover.transform.localPosition = Vector3.up * .05f;
            groundingController = mover.AddComponent<CharacterController>();
            groundingController.height = 1.8f;
            groundingController.center = Vector3.up * .9f;
            groundingController.radius = .3f;
            groundingController.skinWidth = .03f;
            groundingController.minMoveDistance = 0f;
            Physics.SyncTransforms();
            groundingObservation.downFlags = groundingController.Move(Vector3.down * .2f).ToString();
            groundingObservation.afterDown = groundingController.isGrounded;
            groundingObservation.horizontalFlags = groundingController.Move(Vector3.forward * .02f).ToString();
            groundingObservation.afterHorizontal = groundingController.isGrounded;
            groundingObservation.frameMoved = Time.frameCount;
            groundingObservation.ran = true;
            // Separate real player component: its own Update must establish ground
            // contact over the live frames before the final horizontal overwrite.
            var playerProbe = new GameObject("ProbeActualFighter");
            playerProbe.transform.SetParent(groundingFixture.transform, false);
            playerProbe.transform.localPosition = new Vector3(2f, .05f, 0f);
            var playerController = playerProbe.AddComponent<CharacterController>();
            playerController.height = 1.8f; playerController.center = Vector3.up * .9f;
            playerController.radius = .36f; playerController.skinWidth = .03f; playerController.minMoveDistance = 0f;
            Physics.SyncTransforms();
            playerController.Move(Vector3.down * .2f);
            groundingFighter = playerProbe.AddComponent<PrototypeFighter>();
        }
        catch (Exception e) { groundingObservation.error = e.Message; }
    }

    public static string Run()
    {
        var result = new Result();
        if (!EditorApplication.isPlaying)
        {
            result.failures.Add("Enter Play Mode in the existing scene first; no scene is created by this verifier.");
            return JsonUtility.ToJson(result, true);
        }
        var before = new HashSet<CombatActor>(Object.FindObjectsByType<CombatActor>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        var cleanup = new List<Object>();
        Action<bool, string> check = (ok, name) =>
        {
            result.checks++;
            if (ok) result.successes.Add(name); else result.failures.Add(name);
        };
        try
        {
            AnimationClip dummy = new() { name = "RULE_TEST_ONLY_dummy_pose" };
            AnimationUtility.SetEditorCurve(dummy, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Constant(0f, 1f, 0f));
            cleanup.Add(dummy);
            AholoMotionSet motions = ScriptableObject.CreateInstance<AholoMotionSet>();
            motions.name = "RULE_TEST_ONLY_motion_set";
            motions.idle = motions.walk = motions.light = motions.heavy = motions.skill = motions.guard = motions.death = dummy;
            cleanup.Add(motions);

            CombatActor player = CreateActor("RuleTest player", CombatRole.Player, CombatTeam.Player, TestOrigin, motions);
            CombatActor enemy = CreateActor("RuleTest enemy", CombatRole.Grunt, CombatTeam.Enemy, TestOrigin + Vector3.forward, motions);
            CombatActor ally = CreateActor("RuleTest ally", CombatRole.Elite, CombatTeam.Player, TestOrigin + new Vector3(.08f, 0f, 1.2f), motions);
            enemy.SetTeam(CombatTeam.Player);
            check(enemy.Team == CombatTeam.Enemy, "Grunt SetTeam cannot make it an ally");
            enemy.Configure(CombatRole.Grunt, CombatTeam.Player, 100);
            check(enemy.Team == CombatTeam.Enemy, "Grunt Configure cannot bypass enemy allegiance");
            check(!enemy.TryAttack(new CombatAttack { motion = CombatMotion.Skill }), "Grunt cannot use skill even when a skill clip exists");
            check(!player.IsHostileTo(ally) && player.IsHostileTo(enemy), "Allegiance classifies allies and enemies");
            int health = ally.Health;
            check(!ally.ReceiveDamage(player, 20) && ally.Health == health, "Direct damage rejects friendly fire");

            int deaths = 0;
            enemy.Defeated += _ => deaths++;
            check(enemy.ReceiveDamage(player, 1000), "Lethal hostile damage accepted");
            check(!enemy.ReceiveDamage(player, 1000) && deaths == 1, "Death event fires once");
            check(!enemy.GetComponent<CharacterController>().enabled, "Defeat disables standing collision capsule");
            enemy.ResetActor();
            check(enemy.Health == enemy.MaxHealth && !enemy.IsDefeated && enemy.GetComponent<CharacterController>().enabled, "Reset restores health and collision");

            CombatAttack attack = new()
            {
                damage = 10, motion = CombatMotion.Light, duration = 1f, range = 2f,
                hitWindows = new[]
                {
                    new CombatHitWindow { start = .1f, end = .3f },
                    new CombatHitWindow { start = .5f, end = .7f }
                }
            };
            check(player.TryAttack(attack), "Real actor accepts a configured test attack");
            var hitSets = (HashSet<CombatActor>[])typeof(CombatActor).GetField("hitTargets", Hidden).GetValue(player);
            var sweep = typeof(CombatActor).GetMethod("SweepSegment", Hidden);
            Physics.SyncTransforms();
            Vector3 bladeStart = TestOrigin + Vector3.up * .9f;
            Vector3 bladeEnd = bladeStart + Vector3.forward * 1.35f;
            sweep.Invoke(player, new object[] { bladeStart, bladeEnd, attack, attack.hitWindows[0], hitSets[0] });
            sweep.Invoke(player, new object[] { bladeStart, bladeEnd, attack, attack.hitWindows[0], hitSets[0] });
            check(enemy.Health == 90, "Repeated blade samples hit each target once in a window");
            check(ally.Health == health, "Blade sweep filters same-team actors");
            sweep.Invoke(player, new object[] { bladeStart, bladeEnd, attack, attack.hitWindows[1], hitSets[1] });
            check(enemy.Health == 80, "A distinct configured combo window can hit the same target once again");
            player.CancelAttack();
            check(!player.IsAttacking, "CancelAttack closes damage windows");

            enemy.ResetActor();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "RULE_TEST_ONLY_wall";
            wall.transform.position = TestOrigin + new Vector3(0f, .9f, .5f);
            wall.transform.localScale = new Vector3(2f, 2f, .2f);
            cleanup.Add(wall);
            Physics.SyncTransforms();
            check(!player.HasLineOfSight(enemy), "Environment raycast blocks target behind wall");
            sweep.Invoke(player, new object[] { bladeStart, bladeEnd, attack, attack.hitWindows[0], new HashSet<CombatActor>() });
            check(enemy.Health == enemy.MaxHealth, "Blade sweep cannot damage through a wall");
            Object.DestroyImmediate(wall);
            Physics.SyncTransforms();
            check(player.HasLineOfSight(enemy), "Removing obstruction restores line of sight");

            // A second isolated coordinator proves actual story gates and reset logic.
            var hostActor = CreateActor("RULE_TEST_ONLY_encounter", CombatRole.Player, CombatTeam.Player, TestOrigin + Vector3.right * 20f, motions);
            var host = hostActor.gameObject;
            var duel = host.AddComponent<DuelEncounter>();
            duel.eliteActorPrefab = ally;
            duel.gruntActorPrefab = enemy;
            duel.eliteMotions = duel.gruntMotions = motions;
            duel.tutorialPosition = host.transform.position + Vector3.forward * 3f;
            duel.eliteArrivalPosition = host.transform.position + Vector3.forward * 5f;
            // Use the same clearance above the fixture floor as the authored inn position.
            duel.eliteGuardPosition = host.transform.position + Vector3.forward * 4f + Vector3.up * .05f;
            duel.rescueArrivalPosition = host.transform.position + Vector3.forward * 7f;
            duel.ResetEncounter();
            check(duel.Stage == DuelEncounter.EncounterStage.Arrival && !duel.EliteActor.gameObject.activeSelf
                && !duel.TutorialActor.gameObject.activeSelf, "Reset begins with both story opponents hidden");
            check(duel.HasRequiredBindings, "Configured inactive elite passes structural binding checks");
            Transform savedWeapon = duel.PlayerActor.WeaponRoot;
            duel.PlayerActor.WeaponRoot = null;
            check(!duel.HasRequiredBindings && !duel.BeginTutorial() && duel.Stage == DuelEncounter.EncounterStage.Arrival,
                "Missing weapon bindings cannot start an invisible or ineffective battle");
            duel.PlayerActor.WeaponRoot = savedWeapon;
            check(!duel.MakeEliteAlly() && !duel.BeginRescueBattle(), "Cannot convert elite or start rescue before elite victory");
            check(!duel.BeginTutorial() && duel.RevealTutorialSentry() && !duel.BeginTutorial()
                && duel.CompleteTutorialInstructions() && duel.BeginTutorial(), "Tutorial requires reveal and instruction completion before battle");
            duel.TutorialActor.ReceiveDamage(duel.PlayerActor, 1000, 0f, 0f);
            check(duel.Stage == DuelEncounter.EncounterStage.EliteReady && !duel.EliteActor.gameObject.activeSelf, "Tutorial victory waits for the explicit elite arrival");
            check(!duel.MakeEliteAlly() && !duel.BeginRescueBattle(), "Tutorial victory alone cannot unlock ally conversion");
            // This is a component-rule fixture; the separate live entrance test owns locomotion.
            typeof(DuelEncounter).GetProperty("EliteEntranceComplete").GetSetMethod(true).Invoke(duel, new object[] { true });
            typeof(DuelEncounter).GetField("eliteEntranceReadyAt", Hidden).SetValue(duel, Time.time);
            check(duel.BeginEliteDuel(), "Elite fight begins only after tutorial victory");
            duel.PlayerActor.ReceiveDamage(duel.EliteActor, 1000, 0f, 0f);
            check(duel.Stage == DuelEncounter.EncounterStage.Failed && !duel.IsCombatActive, "Player defeat ends current combat");
            check(!duel.BeginRescueBattle() && !duel.MakeEliteAlly() && !duel.BeginEliteDuel(), "Defeat prevents all subsequent fight transitions");
            duel.ResetEncounter();
            check(duel.Stage == DuelEncounter.EncounterStage.Arrival && duel.PlayerActor.Health == duel.PlayerActor.MaxHealth
                && !duel.EliteVictoryAchieved && duel.EliteActor.Team == CombatTeam.Enemy, "Full reset restores stage, health, allegiance and victory gate");
            duel.RevealTutorialSentry(); duel.CompleteTutorialInstructions(); duel.BeginTutorial();
            duel.TutorialActor.ReceiveDamage(duel.PlayerActor, 1000, 0f, 0f);
            typeof(DuelEncounter).GetProperty("EliteEntranceComplete").GetSetMethod(true).Invoke(duel, new object[] { true });
            typeof(DuelEncounter).GetField("eliteEntranceReadyAt", Hidden).SetValue(duel, Time.time);
            duel.BeginEliteDuel();
            duel.EliteActor.ReceiveDamage(duel.PlayerActor, 1000, 0f, 0f);
            check(duel.EliteVictoryAchieved && duel.Stage == DuelEncounter.EncounterStage.EliteVictory, "Only elite defeat awards elite-victory gate");
            check(duel.MakeEliteAlly() && duel.EliteActor.Team == CombatTeam.Player && !duel.EliteActor.IsDefeated, "Won elite can recover and become allied");
            check(Vector3.Distance(duel.EliteActor.transform.position, duel.eliteGuardPosition) < .01f, "Allied elite takes existing door guard position");
            int restored = duel.EliteActor.Health;
            duel.MakeEliteAlly();
            check(duel.EliteActor.Health == restored, "Repeated conversion does not repeatedly restore health");
            Vector3 playerBefore = duel.PlayerActor.transform.position;
            check(duel.BeginRescueBattle() && duel.Stage == DuelEncounter.EncounterStage.Rescue
                && duel.RemainingEnemies == duel.RescueEnemyCount, "Rescue spawns configured enemy count");
            check(duel.PlayerActor.transform.position == playerBefore, "Rescue never teleports the player");
            check(duel.EnemyActors.All(a => a.Team == CombatTeam.Enemy && a.Role == CombatRole.Grunt), "All reinforcements remain enemy grunts");
            check(duel.EnemyActors.All(a => a.GetComponent<EnemyCombatAI>().Target == duel.EliteActor), "Reinforcements first target the guarding elite");
            CombatActor reinforcement = duel.EnemyActors[0];
            duel.EliteActor.ReceiveDamage(reinforcement, 1000, 0f, 0f);
            check(duel.Stage == DuelEncounter.EncounterStage.Rescue && duel.EliteActor.Health == 1 && !duel.EliteActor.IsDefeated,
                "Allied elite survives lethal grunt damage at one health during rescue");
            foreach (CombatActor grunt in duel.EnemyActors.ToArray()) grunt.ReceiveDamage(duel.PlayerActor, 1000, 0f, 0f);
            check(duel.Stage == DuelEncounter.EncounterStage.Complete && !duel.IsCombatActive, "Defeating every reinforcement completes rescue");
            duel.StopCombat();
            check(duel.Stage == DuelEncounter.EncounterStage.Complete, "StopCombat never rewrites the story result");
            Object.DestroyImmediate(host);
        }
        catch (Exception exception)
        {
            result.failures.Add(exception.ToString());
        }
        finally
        {
            // Only newly created fixtures are removed; existing scene actors are untouched.
            foreach (var actor in Object.FindObjectsByType<CombatActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (actor != null && !before.Contains(actor)) Object.DestroyImmediate(actor.gameObject);
            foreach (Object item in cleanup) if (item != null) Object.DestroyImmediate(item);
            Physics.SyncTransforms();
            result.fixtureRemainders = Object.FindObjectsByType<CombatActor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Count(actor => actor != null && !before.Contains(actor));
            check(result.fixtureRemainders == 0, "All temporary combat actors were cleaned up");
        }
        result.passed = result.failures.Count == 0;
        return JsonUtility.ToJson(result, true);
    }

    static CombatActor CreateActor(string name, CombatRole role, CombatTeam team, Vector3 position, AholoMotionSet motions)
    {
        var body = new GameObject(name);
        body.transform.position = position;
        var controller = body.AddComponent<CharacterController>();
        controller.height = 1.8f; controller.radius = .3f; controller.center = Vector3.up * .9f;
        var visual = new GameObject("Dummy visual");
        visual.transform.SetParent(body.transform, false);
        var animator = visual.AddComponent<Animator>();
        var driver = body.AddComponent<AholoAnimationDriver>();
        driver.Configure(motions, animator);
        var weapon = new GameObject("Dummy blade anchor");
        weapon.transform.SetParent(body.transform, false);
        weapon.transform.localPosition = Vector3.up * .9f;
        var actor = body.AddComponent<CombatActor>();
        actor.Configure(role, team, 100);
        actor.WeaponRoot = weapon.transform;
        actor.BladeEnd = 1.35f;
        actor.RefreshBindings();
        return actor;
    }
}
