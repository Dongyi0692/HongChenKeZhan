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
        public bool Guarding { get; set; }
        public bool Invulnerable { get; set; }
        public bool ControlLocked { get; set; }
        public CombatRole Role => role;
        public CombatTeam Team => team;
        public int MaxHealth => maxHealth;
        public int Health { get; private set; }
        public bool IsDefeated => Health <= 0;
        public bool IsStunned => stunTime > 0f;
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

        void Awake() { Health = maxHealth; RefreshBindings(); }
        void OnEnable() => actors.Add(this);
        void OnDisable() { actors.Remove(this); CancelAttack(); }

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
            CancelAttack();
            Health = health < 0 ? maxHealth : Mathf.Clamp(health, 1, maxHealth);
            stunTime = 0f;
            impact = default;
            Guarding = Invulnerable = ControlLocked = false;
            RefreshBindings();
            if (motor != null) motor.enabled = true;
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
            if (definition == null || IsDefeated || IsStunned || IsAttacking || ControlLocked ||
                (role == CombatRole.Grunt && definition.motion == CombatMotion.Skill)) return false;
            RefreshBindings();
            // A missing clip or weapon is a missing action, never a procedural replacement.
            if (Animation == null || !Animation.Ready || !Animation.HasMotion(definition.motion) || !HasWeapon) return false;
            attack = definition;
            attack.duration = Mathf.Max(.05f, definition.duration);
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
            if (IsDefeated)
            {
                // Only a character that falls to the floor releases its standing capsule.
                if (fallsOnDefeat && motor != null) motor.enabled = false;
                impact = default;
                Defeated?.Invoke(this);
            }
            return true;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || ControlLocked) return;
            stunTime = Mathf.Max(0f, stunTime - dt);
            if (impact.Active && motor != null && motor.enabled) motor.Move(impact.Advance(dt));
            if (attack == null) return;
            float before = attackClock;
            attackClock += dt;
            if (motor != null && motor.enabled && Animation != null && Animation.motionSet != null &&
                Animation.motionSet.TryGetRootDelta(attack.motion, before * Mathf.Max(.05f, attack.speed),
                    Mathf.Min(attackClock, attack.duration) * Mathf.Max(.05f, attack.speed), out Vector3 travel))
                motor.Move(attackFacing * new Vector3(travel.x, 0f, travel.z));
            BladeSegment(out Vector3 from, out Vector3 to);
            int samples = hasPreviousBlade ? Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(
                Vector3.Distance(previousBase, from), Vector3.Distance(previousTip, to)) / Mathf.Max(.025f, BladeRadius)), 1, 24) : 1;
            CombatAttack current = attack;
            for (int windowIndex = 0; attack == current && windowIndex < windows.Length; windowIndex++)
            {
                CombatHitWindow window = windows[windowIndex];
                if (window == null || attackClock < window.start || before > window.end || window.end < window.start) continue;
                for (int sample = 0; sample <= samples && attack == current; sample++)
                {
                    float t = samples == 0 ? 1f : (float)sample / samples;
                    Vector3 start = hasPreviousBlade ? Vector3.Lerp(previousBase, from, t) : from;
                    Vector3 end = hasPreviousBlade ? Vector3.Lerp(previousTip, to, t) : to;
                    SweepSegment(start, end, current, window, hitTargets[windowIndex]);
                }
            }
            previousBase = from;
            previousTip = to;
            hasPreviousBlade = true;
            if (attack == current && attackClock >= current.duration) CancelAttack();
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
