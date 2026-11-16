using System;
using System.Collections.Generic;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

// Isolated component/PhysX tests, with explicit attack clocks and dummy clips.
// Run alongside the separate real-scene playthrough, not as an animation-quality test.
public static class CombatPolishRules
{
    [Serializable] public sealed class Result
    {
        public string scope = "In-memory combat rules with dummy clips and controlled clocks. Checks cancellation, input buffering, guarding and actor-local impact pause; no scene saved.";
        public bool passed;
        public int checks;
        public List<string> successes = new(), failures = new();
    }
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly Vector3 Origin = new(14000f, 120f, 14000f);

    [Unity.Pipeline.Commands.CliCommand("inncombat", "polish_rules", "Check indoor control, defensive combo exits and local impact pause")]
    public static string Run()
    {
        var result = new Result();
        GameObject root = null;
        AholoMotionSet motions = null;
        AnimationClip clip = null;
        void Check(bool ok, string text)
        {
            result.checks++;
            (ok ? result.successes : result.failures).Add(text);
        }
        try
        {
            if (!EditorApplication.isPlaying || Time.deltaTime <= 0f || Time.timeScale <= 0f)
                throw new InvalidOperationException("Run in advancing, unpaused Play Mode.");
            root = new GameObject("POLISH_RULES_TEST_ONLY");
            var floor = new GameObject("Temporary floor");
            floor.transform.SetParent(root.transform);
            floor.transform.position = Origin - Vector3.up * .25f;
            floor.AddComponent<BoxCollider>().size = new Vector3(20f, .5f, 20f);
            clip = new AnimationClip { name = "Polish dummy clip" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Constant(0f, 5f, 0f));
            motions = ScriptableObject.CreateInstance<AholoMotionSet>();
            motions.idle = motions.explorationIdle = motions.walk = motions.run = motions.guard = motions.dodge = clip;
            motions.light = motions.heavy = motions.skill = motions.hit = motions.knockback = motions.death = clip;
            motions.jumpStart = motions.jumpLoop = motions.jumpLand = clip;
            var player = CreateActor(root.transform, motions, "Player", CombatRole.Player, Origin);
            var enemy = CreateActor(root.transform, motions, "Enemy", CombatRole.Elite, Origin + Vector3.forward * 1.6f);
            var fighter = player.gameObject.AddComponent<PrototypeFighter>();
            fighter.useFullClipDurations = false;
            fighter.enableTargetAssist = false;
            Ground(fighter);
            var heavy = new CombatAttack
            {
                motion = CombatMotion.Heavy, duration = 4.33f, range = 2f,
                hitWindows = new[]
                {
                    new CombatHitWindow { start = .36f, end = .56f },
                    new CombatHitWindow { start = 1.03f, end = 1.23f },
                    new CombatHitWindow { start = 1.73f, end = 1.93f },
                    new CombatHitWindow { start = 2.56f, end = 2.7f }
                }
            };
            fighter.moveSpeed = 7.7123f;
            Check(Mathf.Abs(fighter.EffectiveMoveSpeed - 4.2f) < .001f, "Indoor speed supersedes the old 7.7 m/s scene setting");
            fighter.useIndoorMovementTuning = false;
            Check(Mathf.Abs(fighter.EffectiveMoveSpeed - fighter.moveSpeed) < .001f, "Disabling indoor tuning preserves serialized movement settings");
            fighter.useIndoorMovementTuning = true;
            Check(player.TryAttack(heavy), "Four-strike heavy starts");
            Set(fighter, "State", PrototypeFighter.ActionState.HeavyAttack);
            Clock(player, .4f);
            Check(!fighter.CanDefendCancelAttack, "Defense cannot cancel inside a damage window");
            Clock(player, .7f);
            Check(fighter.CanDefendCancelAttack && !fighter.CanExitAttackRecovery,
                "Between strikes permits only defensive exit, not free movement or fresh attacks");
            Clock(player, 1.01f);
            Check(!fighter.CanDefendCancelAttack, "Next strike retains a brief commitment before its damage window");
            Clock(player, .95f);
            Quaternion original = player.transform.rotation;
            player.SetAttackFacing(Quaternion.Euler(0f, 25f, 0f), true);
            Check(Quaternion.Angle(original, player.transform.rotation) > 20f, "Next strike windup can redirect toward the committed target");
            Clock(player, 1.1f);
            original = player.transform.rotation;
            player.SetAttackFacing(Quaternion.identity, true);
            Check(Quaternion.Angle(original, player.transform.rotation) < .01f, "Active strike cannot home toward a new direction");
            Clock(player, .7f);
            Set(fighter, "Stamina", 0f);
            Check(!fighter.Dodge() && player.IsAttacking, "Unaffordable dodge cannot erase the remaining heavy strikes");
            Set(fighter, "Stamina", 100f);
            Check(fighter.Dodge() && !player.IsAttacking && fighter.State == PrototypeFighter.ActionState.Dodge,
                "Affordable defensive dodge abandons the rest of the combo");
            Check(Mathf.Abs(fighter.Stamina - (100f - fighter.dodgeStaminaCost)) < .01f, "Defensive exit pays dodge cost exactly once");
            player.ResetActor(); fighter.ResetPose(); Ground(fighter);
            Check(player.TryAttack(heavy), "Heavy can restart after resetting the actor");
            Set(fighter, "State", PrototypeFighter.ActionState.HeavyAttack);
            Clock(player, .7f);
            fighter.GuardRequested = true;
            Tick(fighter);
            Check(fighter.IsGuarding && !player.IsAttacking, "Held guard exits a safe gap and disables remaining damage windows");
            Quaternion guarded = fighter.transform.rotation;
            fighter.MoveInput = Vector2.down;
            Ground(fighter);
            Tick(fighter);
            Check(Quaternion.Angle(guarded, fighter.transform.rotation) < .1f, "Backing away while guarding does not turn the body around");
            var speed = (Vector3)typeof(PrototypeFighter).GetField("planarVelocity", Hidden).GetValue(fighter);
            Check(speed.magnitude <= fighter.guardAdjustmentSpeed + .01f, "Guard adjustment remains a small controlled step");
            float stamina = fighter.Stamina;
            Check(player.ReceiveDamage(enemy, 5, 0f, 0f), "Forward attack can be guarded");
            Check(player.Health == player.MaxHealth && fighter.Stamina < stamina - 8f,
                "A blocked impact costs stamina rather than causing health damage");
            Check(player.IsHitPaused && enemy.IsHitPaused, "Guard contact pauses only the two participating actors");
            Set(fighter, "Stamina", 3f);
            player.ReceiveDamage(enemy, 5, 0f, 0f);
            Check(fighter.Stamina == 0f && !player.Guarding && !fighter.IsGuarding,
                "Depleting block exhausts stamina and immediately closes guard");
            player.ReceiveDamage(enemy, 5, 0f, 0f);
            Check(player.Health == player.MaxHealth - 5, "Exhausted guard cannot block the next impact for free");
            player.ResetActor(); enemy.ResetActor(); fighter.ResetPose(); Ground(fighter);
            Check(!player.IsHitPaused && !enemy.IsHitPaused, "Reset clears actor-local impact pause");
            Set(player, "stunTime", .4f);
            fighter.Dodge();
            Check(Field(fighter, "queuedAction").ToString() == "None", "Long hitstun does not store a stale defense command");
            Set(player, "stunTime", .08f);
            fighter.Dodge();
            Check(Field(fighter, "queuedAction").ToString() == "Dodge", "Final hitstun accepts a short defensive pre-input");
            Set(player, "stunTime", 0f);
            Tick(fighter);
            Check(fighter.State == PrototypeFighter.ActionState.Dodge, "Buffered dodge executes when short hitstun ends");
            player.ResetActor(); fighter.ResetPose(); Ground(fighter);
            Check(player.TryAttack(heavy), "Attack available for clock-pause check");
            Clock(player, .7f);
            player.Animation.PauseImpact(.055f);
            float before = player.AttackElapsed, scale = Time.timeScale;
            fighter.Dodge();
            float queuedUntil = (float)Field(fighter, "queuedUntil");
            Tick(fighter);
            Check((float)Field(fighter, "queuedUntil") > queuedUntil, "Local impact pause preserves the short pre-input lifetime");
            typeof(CombatActor).GetMethod("LateUpdate", Hidden).Invoke(player, null);
            Check(Mathf.Abs(player.AttackElapsed - before) < .0001f && Mathf.Approximately(scale, Time.timeScale),
                "Impact pause freezes attack/root clock without pausing global game time");
            var mixer = (AnimationMixerPlayable)typeof(AholoAnimationDriver).GetField("mixer", Hidden).GetValue(player.Animation);
            Check(Math.Abs(mixer.GetSpeed()) < .0001d, "Animation graph shares the local impact pause");
            player.Animation.ClearImpactPause();
            Check(!player.IsHitPaused && Math.Abs(mixer.GetSpeed() - 1d) < .0001d, "Animation speed resumes cleanly after impact pause");
            player.ResetActor(); fighter.ResetPose(); Ground(fighter);
            player.TryAttack(heavy);
            player.Animation.SampleMotionPose(CombatMotion.Heavy, .12f);
            typeof(CombatActor).GetMethod("LateUpdate", Hidden).Invoke(player, null);
            Check(Mathf.Abs(player.AttackElapsed - .12f) < .0001f,
                "Attack/root clock follows the evaluated clip rather than adding an extra first-frame delta");
            typeof(CombatActor).GetMethod("LateUpdate", Hidden).Invoke(player, null);
            Check(Mathf.Abs(player.AttackElapsed - .12f) < .0001f, "An unchanged pose cannot advance damage windows a second time");
            int poseSamples = 0;
            float greatestClockError = 0f;
            player.AttackPoseSampled += (motion, fromTime, toTime, fromPoint, toPoint) =>
            {
                poseSamples++;
                player.Animation.TryGetMotionTime(motion, out float sampledTime);
                greatestClockError = Mathf.Max(greatestClockError, Mathf.Abs(sampledTime - toTime));
            };
            player.Animation.SampleMotionPose(CombatMotion.Heavy, .56f);
            typeof(CombatActor).GetMethod("LateUpdate", Hidden).Invoke(player, null);
            player.Animation.TryGetMotionTime(CombatMotion.Heavy, out float finalPoseTime);
            Check(poseSamples > 1 && poseSamples <= 24 && greatestClockError < .0001f,
                "A long active frame samples bounded real bone poses with matching damage times");
            Check(Mathf.Abs(finalPoseTime - .56f) < .0001f && Mathf.Abs(player.AttackElapsed - .56f) < .0001f,
                "Subsampling restores the actual final pose and root clock when no contact interrupts it");
            player.ResetActor(); fighter.ResetPose(); Ground(fighter);
            Check(player.TryAttack(heavy), "Landing-interruption fixture starts a real attack");
            Set(fighter, "State", PrototypeFighter.ActionState.HeavyAttack);
            Set(fighter, "wasGrounded", false);
            Set(fighter, "jumpedAt", Time.time - 1f);
            Tick(fighter);
            Check(fighter.IsGrounded && (bool)Field(fighter, "wasGrounded"),
                "Landing-interruption fixture actually reacquires ground contact");
            Check(player.IsAttacking && player.Animation.CurrentMotion == CombatMotion.Heavy
                && (float)Field(fighter, "landingUntil") <= Time.time,
                "A one-frame ground contact change cannot replace an attack with landing or impose a landing lock");
            player.Animation.Play(CombatMotion.JumpLand, 0f);
            typeof(CombatActor).GetMethod("LateUpdate", Hidden).Invoke(player, null);
            Tick(fighter);
            Check(!player.IsAttacking && fighter.State == PrototypeFighter.ActionState.Ready
                && player.Animation.CurrentMotion != CombatMotion.Heavy,
                "An externally replaced attack clip cancels its damage and unlocks control without restarting the combo");
            player.ResetActor(); fighter.ResetPose(); Ground(fighter);
            Check(player.TryAttack(heavy), "Graph-loss fixture starts a real attack before disabling the graph");
            player.Animation.enabled = false;
            typeof(CombatActor).GetMethod("LateUpdate", Hidden).Invoke(player, null);
            Check(!player.IsAttacking, "Losing the animation graph cannot leave an invisible attack armed");
            player.Animation.enabled = true;
            player.ResetActor(); fighter.ResetPose(); Ground(fighter);
            Check(player.TryAttack(heavy), "Normal-completion fixture starts a real attack");
            Set(fighter, "State", PrototypeFighter.ActionState.HeavyAttack);
            player.Animation.SampleMotionPose(CombatMotion.Heavy, heavy.duration);
            typeof(CombatActor).GetMethod("LateUpdate", Hidden).Invoke(player, null);
            Tick(fighter);
            Check(!player.IsAttacking && fighter.State == PrototypeFighter.ActionState.Ready,
                "A normally completed attack releases both actor and player action state");
            player.ResetActor(); fighter.ResetPose(); Ground(fighter);
            var oversized = new CombatAttack { motion = CombatMotion.Heavy, duration = 20f };
            Check(player.TryAttack(oversized), "Overlong-duration fixture starts a real attack");
            player.Animation.SampleMotionPose(CombatMotion.Heavy, clip.length);
            typeof(CombatActor).GetMethod("LateUpdate", Hidden).Invoke(player, null);
            Check(!player.IsAttacking, "An overlong configured duration still releases at the actual clip end");

            player.ResetActor(); fighter.ResetPose(); Ground(fighter);
            fighter.skillCooldown = 6f; // Old scene data must no longer impose a cooldown.
            float skillCost = fighter.skillStaminaCost;
            Check(fighter.Skill() && player.IsAttacking, "Funded ground skill starts through the normal request path");
            Check(Mathf.Approximately(fighter.Stamina, 100f-skillCost) && fighter.SkillCooldownRemaining == 0f,
                "Skill pays its stamina once and ignores the legacy serialized cooldown");
            float paid = fighter.Stamina;
            Check(!fighter.Skill() && Mathf.Approximately(fighter.Stamina, paid),
                "A repeated request during the same skill does not spend more stamina");
            Clock(player, player.LastHitTime + fighter.skillRecoveryDelay + .01f);
            Check(fighter.Skill() && Mathf.Approximately(fighter.Stamina, 100f-2f*skillCost),
                "A second funded skill can start in recovery without waiting six seconds");
            Set(fighter, "Stamina", skillCost-1f);
            Clock(player, player.LastHitTime + fighter.skillRecoveryDelay + .01f);
            var previousSkill = player.CurrentAttack;
            Check(!fighter.Skill() && player.CurrentAttack == previousSkill && Mathf.Approximately(fighter.Stamina, skillCost-1f),
                "Insufficient stamina neither spends points nor cancels an existing recovery");
            player.ResetActor(); fighter.ResetPose(); Ground(fighter);
            Transform weapon = player.WeaponRoot; player.WeaponRoot = null;
            Check(!fighter.Skill() && Mathf.Approximately(fighter.Stamina, 100f),
                "A missing weapon rejects the skill without charging stamina");
            player.WeaponRoot = weapon;
            fighter.ClearInput();
            Set(fighter, "groundedFromMovement", false); Set(fighter, "verticalVelocity", 1f);
            Check(!fighter.Skill() && Mathf.Approximately(fighter.Stamina, 100f),
                "Airborne skill rejection remains unchanged and does not charge stamina");
        }
        catch (Exception error) { result.failures.Add(error.GetBaseException().ToString()); }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            if (motions != null) Object.DestroyImmediate(motions);
            if (clip != null) Object.DestroyImmediate(clip);
            Physics.SyncTransforms();
        }
        result.passed = result.failures.Count == 0;
        return JsonUtility.ToJson(result, true);
    }

    static CombatActor CreateActor(Transform parent, AholoMotionSet motions, string name, CombatRole role, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = position;
        var motor = go.AddComponent<CharacterController>();
        motor.height = 1.8f; motor.center = Vector3.up * .9f; motor.radius = .36f;
        var visual = new GameObject("Dummy visual"); visual.transform.SetParent(go.transform, false);
        var animator = visual.AddComponent<Animator>();
        var driver = go.AddComponent<AholoAnimationDriver>(); driver.Configure(motions, animator);
        var weapon = new GameObject("Dummy sword"); weapon.transform.SetParent(go.transform, false);
        var actor = go.AddComponent<CombatActor>();
        actor.Configure(role, role == CombatRole.Player ? CombatTeam.Player : CombatTeam.Enemy, 100);
        actor.WeaponRoot = weapon.transform;
        actor.RefreshBindings();
        return actor;
    }
    static object Field(object value, string name) => value.GetType().GetField(name, Hidden).GetValue(value);
    static void Set(object value, string name, object assigned)
    {
        var field = value.GetType().GetField(name, Hidden);
        if (field != null) field.SetValue(value, assigned);
        else value.GetType().GetProperty(name).SetValue(value, assigned);
    }
    static void Ground(PrototypeFighter fighter)
    {
        Set(fighter, "groundedFromMovement", true); Set(fighter, "verticalVelocity", 0f);
        Set(fighter, "landingUntil", 0f); Set(fighter, "wasGrounded", true);
        Physics.SyncTransforms();
    }
    static void Clock(CombatActor actor, float time) => Set(actor, "attackClock", time);
    static void Tick(PrototypeFighter fighter) => typeof(PrototypeFighter).GetMethod("Update", Hidden).Invoke(fighter, null);
}
