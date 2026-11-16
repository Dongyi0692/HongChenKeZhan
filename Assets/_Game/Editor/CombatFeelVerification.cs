using System;
using System.Collections.Generic;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Run inside the existing scene in Play Mode. These are synchronous component/PhysX
// checks with explicitly set clocks, not evidence of frame rate or animation quality.
public static class CombatFeelVerification
{
    [Serializable]
    public sealed class Result
    {
        public string scope = "Real component and PhysX rule checks; dummy clips and explicitly positioned clocks. Guard-priority check invokes one Update and requires a nonpaused frame. No scene saved; no live animation or frame-rate claim.";
        public bool passed;
        public int checks;
        public List<string> successes = new();
        public List<string> failures = new();
    }

    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Vector3 Origin = new(12000f, 150f, 12000f);

    [Unity.Pipeline.Commands.CliCommand("inncombat", "rules", "Run in-memory combat rules in Play Mode")]
    public static string Run()
    {
        var result = new Result();
        if (!EditorApplication.isPlaying)
        {
            result.failures.Add("Enter Play Mode in the existing scene first.");
            return JsonUtility.ToJson(result, true);
        }
        GameObject fixture = null;
        AnimationClip clip = null;
        AholoMotionSet motions = null;
        Action<bool, string> check = (ok, label) =>
        {
            result.checks++;
            if (ok) result.successes.Add(label); else result.failures.Add(label);
        };
        try
        {
            fixture = new GameObject("COMBAT_FEEL_TEST_ONLY");
            clip = new AnimationClip { name = "COMBAT_FEEL_DUMMY_ONLY" };
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Constant(0f, 5f, 0f));
            motions = ScriptableObject.CreateInstance<AholoMotionSet>();
            motions.idle = motions.explorationIdle = motions.walk = motions.run = clip;
            motions.light = motions.heavy = motions.skill = motions.guard = motions.dodge = clip;
            motions.jumpStart = motions.jumpLoop = motions.jumpLand = motions.hit = motions.knockback = motions.death = clip;
            CombatActor player = CreateActor(fixture.transform, "Player", CombatRole.Player, CombatTeam.Player, Origin, motions);
            CombatActor first = CreateActor(fixture.transform, "Elite", CombatRole.Elite, CombatTeam.Enemy, Origin + new Vector3(.15f, 0f, 1.6f), motions);
            CombatActor second = CreateActor(fixture.transform, "Grunt", CombatRole.Grunt, CombatTeam.Enemy, Origin + Vector3.right * 10f, motions);
            CombatActor friend = CreateActor(fixture.transform, "Friend", CombatRole.Elite, CombatTeam.Player, Origin + Vector3.forward, motions);
            var cameraObject = new GameObject("Test camera");
            cameraObject.transform.SetParent(fixture.transform, false);
            cameraObject.transform.position = Origin + new Vector3(0f, .9f, -4f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.aspect = 16f / 9f;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 30f;
            Func<Vector3, CombatActor, CombatActor> select = (direction, previous) =>
            {
                Physics.SyncTransforms();
                return CombatTargeting.Select(player, camera, direction, 2.5f, 65f, previous);
            };

            check(select(Vector3.forward, null) == first, "Visible nearby enemy selected, closer friend ignored");
            check(!CombatTargeting.IsValid(player, friend), "Same-team actor cannot be a target");
            first.transform.position = Origin + Vector3.back * 1.6f;
            check(select(Vector3.forward, first) == null, "Enemy behind requested direction releases selection");
            first.transform.position = Origin + Vector3.forward * 3f;
            check(select(Vector3.forward, first) == null, "Previously selected enemy beyond range is released");
            first.transform.position = Origin + new Vector3(0f, 1.6f, 1.5f);
            check(select(Vector3.forward, null) == null, "Enemy on a different floor outside height allowance rejected");
            first.transform.position = Origin + Vector3.forward * 1.6f;
            camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            check(select(Vector3.forward, null) == null, "Geometrically forward enemy outside camera view rejected");
            camera.transform.rotation = Quaternion.identity;
            var wall = new GameObject("Temporary obstruction");
            wall.transform.SetParent(fixture.transform, false);
            wall.transform.position = Origin + new Vector3(0f, .9f, .75f);
            wall.AddComponent<BoxCollider>().size = new Vector3(2f, 2f, .2f);
            check(select(Vector3.forward, first) == null, "Solid wall releases an otherwise valid target");
            Object.DestroyImmediate(wall);
            first.ControlLocked = true;
            check(select(Vector3.forward, null) == null, "Story-locked enemy cannot be selected");
            first.ControlLocked = false;
            first.gameObject.SetActive(false);
            check(select(Vector3.forward, first) == null, "Inactive enemy cannot stay selected");
            first.gameObject.SetActive(true);
            first.SetTeam(CombatTeam.Player);
            check(select(Vector3.forward, first) == null, "Elite becoming an ally immediately drops out of targeting");
            first.SetTeam(CombatTeam.Enemy);
            first.ReceiveDamage(player, 1000, 0f, 0f);
            check(select(Vector3.forward, first) == null, "Defeated enemy cannot stay selected");
            first.ResetActor();
            check(select(Vector3.forward, null) == first && first.GetComponent<CharacterController>().enabled,
                "Reset restores living target and standing collision");

            first.transform.position = Origin + new Vector3(.05f, 0f, 1.6f);
            second.transform.position = Origin + new Vector3(0f, 0f, 1.6f);
            check(select(Vector3.forward, first) == first, "Small score difference does not flicker between nearby targets");
            first.transform.position = Origin + Vector3.forward * 3f;
            check(select(Vector3.forward, first) == second, "Retention cannot keep out-of-range target over eligible enemy");
            second.transform.position = Origin + Vector3.right * 1.6f;
            player.transform.rotation = Quaternion.LookRotation(Vector3.left);
            check(select(Vector3.right, null) == null, "Reverse input cannot acquire an enemy behind the player's body");
            player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            check(select(Vector3.right, first) == second, "Explicit input direction can select a side enemy");
            Physics.SyncTransforms();
            check(CombatTargeting.Select(player, null, Vector3.right, 2.5f, 65f) == second,
                "No-camera fallback still obeys geometric target selection");
            player.transform.rotation = Quaternion.identity;

            second.transform.position = Origin + Vector3.right * 10f;
            first.transform.position = Origin + Vector3.forward * 1.5f;
            first.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            first.ResetActor();
            int landed = 0, blocked = 0, damaged = 0;
            CombatActor blockedTarget = null;
            player.HitLanded += (_, amount) => { if (amount > 0) landed++; };
            player.AttackBlocked += target => { blocked++; blockedTarget = target; };
            first.Damaged += (_, amount) => { if (amount > 0) damaged++; };
            first.Guarding = true;
            int initialHealth = first.Health;
            first.ReceiveDamage(player, 20, 0f, 0f);
            check(blocked == 1 && blockedTarget == first && landed == 0 && damaged == 0 && first.Health == initialHealth,
                "Guard reports the defender to attacker without damage or false hit confirmation");
            first.Guarding = false;
            first.ReceiveDamage(player, 20, 0f, 0f);
            check(landed == 1 && damaged == 1 && blocked == 1 && first.Health == initialHealth - 20,
                "Real damage raises hit confirmation once with health loss");
            first.Invulnerable = true;
            first.ReceiveDamage(player, 20, 0f, 0f);
            check(landed == 1 && first.Health == initialHealth - 20, "Invulnerability never produces a damaging-hit confirmation");
            first.ResetActor();

            var fighter = player.gameObject.AddComponent<PrototypeFighter>();
            fighter.RefreshBindings();
            fighter.useFullClipDurations = false;
            SetField(fighter, "groundedFromMovement", true);
            var combo = new CombatAttack
            {
                motion = CombatMotion.Heavy, duration = 4.33f, damage = 64, range = 2f,
                hitWindows = new[]
                {
                    new CombatHitWindow { start = .36f, end = .56f },
                    new CombatHitWindow { start = 1.03f, end = 1.23f },
                    new CombatHitWindow { start = 1.73f, end = 1.93f },
                    new CombatHitWindow { start = 2.56f, end = 2.7f }
                }
            };
            check(player.TryAttack(combo), "Configured complete heavy combo starts");
            SetProperty(fighter, "State", PrototypeFighter.ActionState.HeavyAttack);
            check(Mathf.Abs(player.FirstHitTime - .36f) < .001f && Mathf.Abs(player.LastHitTime - 2.7f) < .001f,
                "Combo timing uses first and final damage windows");
            SetField(player, "attackClock", 1.4f);
            check(!fighter.CanExitAttackRecovery && !fighter.Attack(false) && player.CurrentAttack == combo,
                "Gap between heavy hits cannot cancel remaining combo strikes");
            SetField(player, "attackClock", 2.75f);
            check(!fighter.CanExitAttackRecovery, "Final hit retains the small configured recovery cost");
            SetField(player, "attackClock", 2.7f + fighter.heavyRecoveryDelay + .01f);
            check(fighter.CanExitAttackRecovery, "Recovery unlocks after final strike without waiting for entire pose");
            CombatAttack savedSkill = fighter.skillAttack;
            fighter.skillAttack = null;
            check(!fighter.Skill() && player.CurrentAttack == combo, "Missing skill definition cannot cancel recovery for free");
            fighter.skillAttack = savedSkill;
            Transform savedWeapon = player.WeaponRoot;
            player.WeaponRoot = null;
            check(!fighter.Attack(false) && player.CurrentAttack == combo, "Missing weapon cannot cancel recovery into an impossible attack");
            player.WeaponRoot = savedWeapon;
            SetProperty(fighter, "Stamina", 0f);
            check(!fighter.Dodge() && player.CurrentAttack == combo, "Unaffordable dodge does not cancel recovery for free");
            SetProperty(fighter, "Stamina", fighter.MaxStamina);
            check(fighter.Dodge() && !player.IsAttacking && fighter.State == PrototypeFighter.ActionState.Dodge,
                "Affordable dodge can leave final recovery and closes every damage window");
            check(Mathf.Abs(fighter.Stamina - (fighter.MaxStamina - fighter.dodgeStaminaCost)) < .001f,
                "Recovery dodge still spends stamina once");

            player.ResetActor();
            player.transform.rotation = Quaternion.identity;
            check(player.TryAttack(combo), "Second combo available after actor reset");
            SetField(player, "attackClock", .1f);
            Quaternion turn = Quaternion.Euler(0f, 30f, 0f);
            player.SetAttackFacing(turn);
            Quaternion rootFacing = (Quaternion)typeof(CombatActor).GetField("attackFacing", Hidden).GetValue(player);
            check(Quaternion.Angle(player.transform.rotation, turn) < .01f && Quaternion.Angle(rootFacing, turn) < .01f,
                "Windup assistance keeps body and root-travel heading aligned");
            SetField(player, "attackClock", .4f);
            player.SetAttackFacing(Quaternion.Euler(0f, 80f, 0f));
            check(Quaternion.Angle(player.transform.rotation, turn) < .01f, "Facing cannot home in after attack becomes active");
            player.CancelAttack();

            var floor = new GameObject("Guard-priority fixture floor");
            floor.transform.SetParent(fixture.transform, false);
            floor.transform.position = Origin + Vector3.down * .25f;
            floor.AddComponent<BoxCollider>().size = new Vector3(10f, .5f, 10f);
            Physics.SyncTransforms();
            player.ResetActor();
            fighter.ClearInput();
            SetProperty(fighter, "State", PrototypeFighter.ActionState.HeavyAttack);
            SetProperty(fighter, "Stamina", fighter.MaxStamina);
            SetField(fighter, "groundedFromMovement", true);
            SetField(fighter, "verticalVelocity", 0f);
            SetField(fighter, "landingUntil", 0f);
            player.TryAttack(combo);
            SetField(player, "attackClock", 2.7f + fighter.heavyRecoveryDelay + .01f);
            Type bufferedAction = typeof(PrototypeFighter).GetNestedType("BufferedAction", BindingFlags.NonPublic);
            SetField(fighter, "queuedAction", Enum.Parse(bufferedAction, "Light"));
            SetField(fighter, "queuedUntil", Time.time + 10f);
            fighter.GuardRequested = true;
            if (Time.deltaTime > 0f)
            {
                typeof(PrototypeFighter).GetMethod("Update", Hidden).Invoke(fighter, null);
                check(fighter.State == PrototypeFighter.ActionState.Ready && player.Guarding && !player.IsAttacking,
                    "Current guard hold supersedes an older queued attack when recovery opens");
            }
            else check(false, "Guard-priority verification requires an unpaused frame with Time.deltaTime > 0");
            fighter.ClearInput();
            player.CancelAttack();

            Renderer gruntVisible = second.GetComponentInChildren<MeshRenderer>();
            var hiddenAccessory = new GameObject("Initially hidden accessory");
            hiddenAccessory.transform.SetParent(second.transform, false);
            var hiddenRenderer = hiddenAccessory.AddComponent<MeshRenderer>();
            hiddenRenderer.enabled = false;
            second.ResetActor();
            second.ReceiveDamage(player, 1000, 0f, 0f);
            typeof(CombatActor).GetMethod("UpdateDefeatVisuals", Hidden).Invoke(second, new object[] { second.defeatedGruntHideDelay + .1f });
            check(!gruntVisible.enabled && !hiddenRenderer.enabled && !second.GetComponent<CharacterController>().enabled,
                "Defeated grunt visuals and standing collision are removed after delay");
            second.ResetActor();
            check(gruntVisible.enabled && !hiddenRenderer.enabled && second.GetComponent<CharacterController>().enabled,
                "Retry restores grunt and preserves originally hidden accessory state");
            Renderer eliteVisible = first.GetComponentInChildren<MeshRenderer>();
            first.ReceiveDamage(player, 1000, 0f, 0f);
            typeof(CombatActor).GetMethod("UpdateDefeatVisuals", Hidden).Invoke(first, new object[] { 30f });
            check(eliteVisible.enabled, "Defeated elite remains available for story and ally transition");
            first.ResetActor();
            first.SetTeam(CombatTeam.Player);
            check(!CombatTargeting.IsValid(player, first), "Recovered allied elite stays excluded from attacks");
        }
        catch (Exception e)
        {
            result.failures.Add(e.ToString());
        }
        finally
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            if (motions != null) Object.DestroyImmediate(motions);
            if (clip != null) Object.DestroyImmediate(clip);
            Physics.SyncTransforms();
            check(fixture == null, "All owned test fixtures cleaned up without touching scene actors");
        }
        result.passed = result.failures.Count == 0;
        return JsonUtility.ToJson(result, true);
    }

    static CombatActor CreateActor(Transform parent, string label, CombatRole role, CombatTeam team,
        Vector3 position, AholoMotionSet motions)
    {
        var body = new GameObject("COMBAT_FEEL_TEST_" + label);
        body.transform.SetParent(parent, false);
        body.transform.position = position;
        var controller = body.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = .3f;
        controller.center = Vector3.up * .9f;
        var visual = new GameObject("Dummy visual");
        visual.transform.SetParent(body.transform, false);
        visual.AddComponent<MeshRenderer>();
        var animator = visual.AddComponent<Animator>();
        var driver = body.AddComponent<AholoAnimationDriver>();
        driver.Configure(motions, animator);
        var anchor = new GameObject("Dummy blade");
        anchor.transform.SetParent(body.transform, false);
        anchor.transform.localPosition = Vector3.up * .9f;
        var actor = body.AddComponent<CombatActor>();
        actor.Configure(role, team, 100);
        actor.WeaponRoot = anchor.transform;
        actor.RefreshBindings();
        return actor;
    }

    static void SetField(object target, string name, object value)
        => target.GetType().GetField(name, Hidden).SetValue(target, value);
    static void SetProperty(object target, string name, object value)
        => target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance).SetValue(target, value);
}
