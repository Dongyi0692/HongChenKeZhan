using System;
using System.Collections.Generic;
using UnityEngine;

namespace FengChenInn
{
    public enum CombatTeam { Neutral, Player, Enemy }
    public enum CombatRole { Player, Elite, Grunt }

    [Serializable]
    public sealed class CombatHitWindow
    {
        public float start = .25f, end = .45f;
        public float damageMultiplier = 1f;
    }

    [Serializable]
    public sealed class CombatAttack
    {
        public string label = "挥砍";
        public CombatMotion motion = CombatMotion.Light;
        public int damage = 16;
        public float duration = 1f, speed = 1f, activeStart = .25f, activeEnd = .45f;
        public float range = 1.9f, knockback = .16f, stagger = .24f;
        public bool unblockable;
        // Empty means one hit for the complete attack. Populate only after checking the clip.
        public CombatHitWindow[] hitWindows = Array.Empty<CombatHitWindow>();
    }

    [DisallowMultipleComponent]
    public sealed class CombatActor : MonoBehaviour
    {
        static readonly HashSet<CombatActor> actors = new();
        public static IEnumerable<CombatActor> Actors => actors;
        public string DisplayName = "角色";
        [SerializeField] CombatRole role;
        [SerializeField] CombatTeam team;
        [SerializeField, Min(1)] int maxHealth = 100;
        public Transform WeaponRoot, BladeBase, BladeTip;
        public Vector3 BladeAxis = Vector3.forward;
        public float BladeStart = .12f, BladeEnd = 1.1f, BladeRadius = .065f;
        public LayerMask collisionMask = ~0;
        [Tooltip("Only defeated grunts are hidden. Story elites are never removed.")]
        [Min(0f)] public float defeatedGruntHideDelay = 4f;
        public bool Guarding { get; set; }
        public bool Invulnerable { get; set; }
        public bool ControlLocked { get; set; }
        public CombatRole Role => role;
        public CombatTeam Team => team;
        public int MaxHealth => maxHealth;
        public int Health { get; private set; }
        public bool IsDefeated => Health <= 0;
        public bool IsStunned => stunTime > 0f;
        public float StunRemaining => stunTime;
        public bool IsHitPaused => Animation != null && Animation.IsImpactPaused;
        public bool IsAttacking => attack != null;
        public float AttackElapsed => attackClock;
        public float AttackDuration => attack != null ? attack.duration : 0f;
        public CombatAttack CurrentAttack => attack;
        public AholoAnimationDriver Animation { get; private set; }
        public bool HasWeapon => (BladeBase != null && BladeTip != null) || WeaponRoot != null;
        public event Action<CombatActor, int> Damaged;
        public event Action<CombatActor> Defeated;
        public event Action<CombatActor> Blocked;
        public event Action<CombatAttack> AttackStarted;
        public event Action<CombatActor, int> HitLanded;
        // Raised on the attacker, distinct from a successful damaging hit.
        public event Action<CombatActor> AttackBlocked;
        // Optional live diagnostics; no allocation or geometry queries without a subscriber.
        public event Action<CombatMotion, float, float, Vector3, Vector3> AttackPoseSampled;

        public float FirstHitTime
        {
            get
            {
                if (attack == null) return 0f;
                float first = float.PositiveInfinity;
                if (windows != null)
                    foreach (var window in windows)
                        if (window != null && window.end >= window.start) first = Mathf.Min(first, window.start);
                return float.IsPositiveInfinity(first) ? attack.activeStart : first;
            }
        }
        public float LastHitTime
        {
            get
            {
                if (attack == null) return 0f;
                float last = 0f;
                if (windows != null)
                    foreach (var window in windows)
                        if (window != null && window.end >= window.start) last = Mathf.Max(last, window.end);
                return last;
            }
        }

        public bool IsBetweenStrikes(float recovery = .10f, float nextStrikeMargin = .08f)
        {
            if (attack == null || windows == null || windows.Length < 2) return false;
            float previousEnd = float.NegativeInfinity, nextStart = float.PositiveInfinity;
            foreach (var window in windows)
            {
                if (window == null || window.end < window.start) continue;
                if (attackClock >= window.start && attackClock <= window.end) return false;
                if (window.end < attackClock) previousEnd = Mathf.Max(previousEnd, window.end);
                if (window.start > attackClock) nextStart = Mathf.Min(nextStart, window.start);
            }
            return !float.IsInfinity(previousEnd) && !float.IsInfinity(nextStart)
                && attackClock >= previousEnd + recovery && attackClock < nextStart - nextStrikeMargin;
        }

        public bool IsComboWindup(float anticipation = .14f)
        {
            if (attack == null || windows == null || windows.Length < 2) return false;
            float previousEnd = float.NegativeInfinity, nextStart = float.PositiveInfinity;
            foreach (var window in windows)
            {
                if (window == null || window.end < window.start) continue;
                if (attackClock >= window.start && attackClock <= window.end) return false;
                if (window.end < attackClock) previousEnd = Mathf.Max(previousEnd, window.end);
                if (window.start > attackClock) nextStart = Mathf.Min(nextStart, window.start);
            }
            return !float.IsInfinity(previousEnd) && !float.IsInfinity(nextStart)
                && attackClock >= previousEnd + .08f && attackClock >= nextStart - anticipation;
        }

        // Root travel and body share a heading; never redirect inside a hit window.
        public void SetAttackFacing(Quaternion facing, bool allowComboWindup = false, float anticipation = .14f)
        {
            if (attack == null || attackClock >= FirstHitTime && !(allowComboWindup && IsComboWindup(anticipation))) return;
            // Keep animation-path history through windup. Rotate that history
            // with the body so the turn itself cannot manufacture a sword hit.
            if (hasPreviousBlade)
            {
                Quaternion change = facing * Quaternion.Inverse(transform.rotation);
                previousBase = transform.position + change * (previousBase - transform.position);
                previousTip = transform.position + change * (previousTip - transform.position);
            }
            transform.rotation = attackFacing = facing;
        }

        CharacterController motor;
        CombatAttack attack;
        CombatHitWindow[] windows;
        HashSet<CombatActor>[] hitTargets;
        readonly Collider[] contacts = new Collider[64];
        float attackClock, stunTime;
        Vector3 previousBase, previousTip;
        Quaternion attackFacing;
        bool hasPreviousBlade;
        ImpactSlide impact;
        float defeatedFor;
        Renderer[] hiddenDefeatRenderers;
        bool[] defeatRendererStates;

        void Awake() { Health = maxHealth; RefreshBindings(); }
        void OnEnable() => actors.Add(this);
        void OnDisable()
        {
            actors.Remove(this);
            CancelAttack();
            if (Animation != null) Animation.ClearImpactPause();
        }

        public void RefreshBindings()
        {
            motor = GetComponent<CharacterController>();
            Animation = GetComponentInChildren<AholoAnimationDriver>(true);
        }

        public void Configure(CombatRole actorRole, CombatTeam actorTeam, int health)
        {
            role = actorRole;
            team = actorRole == CombatRole.Grunt ? CombatTeam.Enemy : actorTeam;
            maxHealth = Mathf.Max(1, health);
            ResetActor();
        }

        public void ResetActor(int health = -1)
        {
            if (Animation != null) Animation.ClearImpactPause();
            RestoreDefeatVisuals();
            defeatedFor = 0f;
            CancelAttack();
            Health = health < 0 ? maxHealth : Mathf.Clamp(health, 1, maxHealth);
            stunTime = 0f;
            impact = default;
            Guarding = Invulnerable = ControlLocked = false;
            RefreshBindings();
            if (motor != null) motor.enabled = true;
        }

        // Stage entry heals only; it must not reset stamina, cooldowns, pose or allegiance.
        public void RestoreHealthToFull()
        {
            if (!IsDefeated) Health = maxHealth;
        }

        void RestoreDefeatVisuals()
        {
            if (hiddenDefeatRenderers != null)
                for (int i = 0; i < hiddenDefeatRenderers.Length; i++)
                    if (hiddenDefeatRenderers[i] != null) hiddenDefeatRenderers[i].enabled = defeatRendererStates[i];
            hiddenDefeatRenderers = null;
            defeatRendererStates = null;
        }

        void UpdateDefeatVisuals(float dt)
        {
            if (role != CombatRole.Grunt || !IsDefeated || hiddenDefeatRenderers != null) return;
            defeatedFor += dt;
            if (defeatedFor < defeatedGruntHideDelay) return;
            hiddenDefeatRenderers = GetComponentsInChildren<Renderer>(true);
            defeatRendererStates = new bool[hiddenDefeatRenderers.Length];
            for (int i = 0; i < hiddenDefeatRenderers.Length; i++)
            {
                defeatRendererStates[i] = hiddenDefeatRenderers[i].enabled;
                hiddenDefeatRenderers[i].enabled = false;
            }
            if (motor != null) motor.enabled = false;
        }

        public void SetTeam(CombatTeam value)
        {
            if (role == CombatRole.Grunt && value != CombatTeam.Enemy) return;
            CancelAttack();
            team = value;
            Guarding = Invulnerable = false;
        }

        public bool IsHostileTo(CombatActor other) => other != null && other != this &&
            team != CombatTeam.Neutral && other.team != CombatTeam.Neutral && team != other.team;

        public bool TryAttack(CombatAttack definition)
        {
            if (definition == null || IsDefeated || IsStunned || IsAttacking || IsHitPaused || ControlLocked ||
                (role == CombatRole.Grunt && definition.motion == CombatMotion.Skill)) return false;
            RefreshBindings();
            // A missing clip or weapon is a missing action, never a procedural replacement.
            if (Animation == null || !Animation.Ready || !Animation.HasMotion(definition.motion) || !HasWeapon) return false;
            attack = definition;
            float playableDuration = Animation.MotionLength(definition.motion) / Mathf.Max(.05f, definition.speed);
            attack.duration = Mathf.Min(Mathf.Max(.05f, definition.duration), playableDuration);
            attackClock = 0f;
            attackFacing = transform.rotation;
            hasPreviousBlade = false;
            windows = definition.hitWindows != null && definition.hitWindows.Length > 0
                ? definition.hitWindows
                : new[] { new CombatHitWindow { start = definition.activeStart, end = definition.activeEnd } };
            hitTargets = new HashSet<CombatActor>[windows.Length];
            for (int i = 0; i < windows.Length; i++) hitTargets[i] = new HashSet<CombatActor>();
            Guarding = false;
            Animation.Restart(definition.motion, .10f, Mathf.Max(.05f, definition.speed));
            AttackStarted?.Invoke(definition);
            return true;
        }

        public void CancelAttack()
        {
            attack = null;
            windows = null;
            hitTargets = null;
            attackClock = 0f;
            hasPreviousBlade = false;
        }

        public bool ReceiveDamage(CombatActor source, int amount, float knockback = .15f,
            float stagger = .22f, bool unblockable = false)
        {
            if (IsDefeated || ControlLocked || Invulnerable || amount <= 0 ||
                (source != null && (source.IsDefeated || source.ControlLocked || !IsHostileTo(source)))) return false;
            Vector3 toward = source != null ? Vector3.ProjectOnPlane(source.transform.position - transform.position, Vector3.up) : transform.forward;
            if (!unblockable && Guarding && (toward.sqrMagnitude < .001f || Vector3.Angle(transform.forward, toward) <= 75f))
            {
                impact.Start(-toward, Mathf.Min(.04f, knockback));
                Blocked?.Invoke(source);
                source?.AttackBlocked?.Invoke(this);
                PauseContact(source, .025f);
                return true;
            }
            int dealt = Mathf.Min(Health, amount);
            Health -= dealt;
            CancelAttack();
            Guarding = false;
            stunTime = Mathf.Max(stunTime, stagger);
            impact.Start(-toward, Mathf.Max(0f, knockback));
            bool fallsOnDefeat = IsDefeated && Animation != null && Animation.Ready
                && Animation.HasMotion(CombatMotion.Death);
            if (Animation != null && Animation.Ready)
            {
                CombatMotion motion = IsDefeated ? (fallsOnDefeat ? CombatMotion.Death : CombatMotion.Idle)
                    : knockback >= .3f ? CombatMotion.Knockback : CombatMotion.Hit;
                if (Animation.HasMotion(motion)) Animation.Restart(motion, .06f);
            }
            Damaged?.Invoke(source, dealt);
            source?.HitLanded?.Invoke(this, dealt);
            if (!IsDefeated) PauseContact(source, source != null && source.CurrentAttack != null
                && source.CurrentAttack.motion == CombatMotion.Heavy ? .055f : .035f);
            if (IsDefeated)
            {
                // Only a character that falls to the floor releases its standing capsule.
                if (fallsOnDefeat && motor != null) motor.enabled = false;
                impact = default;
                Defeated?.Invoke(this);
            }
            return true;
        }

        void PauseContact(CombatActor source, float duration)
        {
            // Presentation pause is actor-local: UI, camera, other opponents and
            // global game time keep running. Actor and graph use the same timer.
            if (Animation != null) Animation.PauseImpact(duration);
            if (source != null && source.Animation != null) source.Animation.PauseImpact(duration);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt > 0f) UpdateDefeatVisuals(dt);
            if (dt <= 0f || ControlLocked) return;
            if (IsHitPaused) return;
            stunTime = Mathf.Max(0f, stunTime - dt);
            if (impact.Active && motor != null && motor.enabled) motor.Move(impact.Advance(dt));
            if (attack == null) return;
            float before = attackClock;
            CombatAttack current = attack;
            float speed = Mathf.Max(.05f, current.speed);
            // The graph may begin evaluating one frame after an input request.
            // Independent += deltaTime used to lead the visible sword by that
            // entire first frame (71 ms in the live trace). Share its real clock.
            if (Animation == null || !Animation.TryGetMotionTime(current.motion, out float clipTime))
            {
                // A landing/story/disabled-graph override is an interruption,
                // never an attack that can remain armed without a playing clip.
                CancelAttack();
                return;
            }
            float finishAt = Mathf.Min(current.duration, Animation.MotionLength(current.motion) / speed);
            float end = Mathf.Min(finishAt, Mathf.Max(before, clipTime / speed));
            if (end <= before)
            {
                if (before >= finishAt) CancelAttack();
                return;
            }
            bool activeInterval = false;
            foreach (var window in windows)
                if (window != null && window.end >= window.start && end >= window.start && before <= window.end)
                { activeInterval = true; break; }
            // At low fps a rotating sword follows an arc, not the chord between
            // two rendered endpoints. Sample actual bones only near damage
            // windows, bounded at 24 poses for a hitch and ~60 poses/second.
            int poses = activeInterval ? Mathf.Clamp(Mathf.CeilToInt((end - before) * 60f), 1, 24) : 1;
            for (int pose = 1; pose <= poses && attack == current; pose++)
            {
                float previousClock = attackClock;
                attackClock = Mathf.Lerp(before, end, (float)pose / poses);
                if (motor != null && motor.enabled && Animation.motionSet != null &&
                    Animation.motionSet.TryGetRootDelta(current.motion, previousClock * speed, attackClock * speed, out Vector3 travel))
                    motor.Move(attackFacing * new Vector3(travel.x, 0f, travel.z));
                if (activeInterval) Animation.SampleMotionPose(current.motion, attackClock * speed);
                BladeSegment(out Vector3 from, out Vector3 to);
                AttackPoseSampled?.Invoke(current.motion, previousClock, attackClock, from, to);
                int samples = hasPreviousBlade ? Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(
                    Vector3.Distance(previousBase, from), Vector3.Distance(previousTip, to)) / Mathf.Max(.025f, BladeRadius)), 1, 24) : 1;
                for (int windowIndex = 0; attack == current && windowIndex < windows.Length; windowIndex++)
                {
                    CombatHitWindow window = windows[windowIndex];
                    if (window == null || attackClock < window.start || previousClock > window.end || window.end < window.start) continue;
                    float interval = Mathf.Max(.00001f, attackClock - previousClock);
                    float firstSample = Mathf.Clamp01((window.start - previousClock) / interval);
                    float lastSample = Mathf.Clamp01((window.end - previousClock) / interval);
                    for (int sample = 0; sample <= samples && attack == current; sample++)
                    {
                        float t = Mathf.Lerp(firstSample, lastSample, (float)sample / samples);
                        Vector3 start = hasPreviousBlade ? Vector3.Lerp(previousBase, from, t) : from;
                        Vector3 tip = hasPreviousBlade ? Vector3.Lerp(previousTip, to, t) : to;
                        SweepSegment(start, tip, current, window, hitTargets[windowIndex]);
                    }
                }
                if (attack != current) return; // A story/hit event owns its new pose.
                previousBase = from;
                previousTip = to;
                hasPreviousBlade = true;
                // Keep the exact contact pose/root time during local hitstop;
                // resume from it instead of jumping to the pre-evaluated end.
                if (IsHitPaused) return;
            }
            if (attack == current && attackClock >= finishAt) CancelAttack();
        }

        void BladeSegment(out Vector3 start, out Vector3 end)
        {
            if (BladeBase != null && BladeTip != null) { start = BladeBase.position; end = BladeTip.position; return; }
            if (WeaponRoot == null) { start = end = transform.position; return; }
            Vector3 axis = WeaponRoot.TransformDirection(BladeAxis.normalized);
            start = WeaponRoot.position + axis * BladeStart;
            end = WeaponRoot.position + axis * BladeEnd;
        }

        void SweepSegment(Vector3 start, Vector3 end, CombatAttack current, CombatHitWindow window, HashSet<CombatActor> hits)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length < .001f) return;
            // Solid environment clips the blade query, so damage does not pass through walls.
            foreach (RaycastHit hit in Physics.RaycastAll(start, delta / length, length, collisionMask, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<CombatActor>() == null)
                    length = Mathf.Min(length, hit.distance);
            end = start + delta.normalized * length;
            int count = Physics.OverlapCapsuleNonAlloc(start, end, Mathf.Max(.01f, BladeRadius), contacts, collisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                CombatActor victim = contacts[i].GetComponentInParent<CombatActor>();
                if (!IsHostileTo(victim) || victim.IsDefeated || hits.Contains(victim)) continue;
                if (Vector3.Distance(transform.position, victim.transform.position) > current.range + .5f || !HasLineOfSight(victim)) continue;
                // Evading also consumes this attack segment: it cannot hit again later in the same sweep.
                hits.Add(victim);
                victim.ReceiveDamage(this, Mathf.Max(1, Mathf.RoundToInt(current.damage * window.damageMultiplier)), current.knockback, current.stagger, current.unblockable);
            }
        }

        public bool HasLineOfSight(CombatActor target)
        {
            if (target == null) return false;
            Vector3 start = transform.position + Vector3.up * .9f;
            Vector3 delta = target.transform.position + Vector3.up * .9f - start;
            if (delta.sqrMagnitude < .001f) return true;
            foreach (RaycastHit hit in Physics.RaycastAll(start, delta.normalized, delta.magnitude, collisionMask, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(target.transform) &&
                    hit.collider.GetComponentInParent<CombatActor>() == null) return false;
            return true;
        }
    }
}
