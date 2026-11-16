using System.Collections.Generic;
using UnityEngine;

namespace FengChenInn
{
    [RequireComponent(typeof(CombatActor), typeof(CharacterController))]
    public sealed class EnemyCombatAI : MonoBehaviour
    {
        public enum Behaviour { Waiting, Approach, Windup, Strike, Recovery, Stagger, Defeated }
        public float moveSpeed = 2.1f, turnSpeed = 420f, engageDistance = 1.45f;
        public float attackInterval = .65f, detectionRadius = 30f;
        [Header("Readable group combat")]
        public bool coordinateGruntAttacks = true;
        [Min(0f)] public float sharedAttackGap = .28f;
        public CombatAttack[] attacks = System.Array.Empty<CombatAttack>();
        public bool CombatEnabled { get; private set; }
        public CombatActor Target { get; private set; }
        public Behaviour State { get; private set; }
        public CombatActor Actor { get; private set; }
        CharacterController motor;
        float cooldown, verticalVelocity, retargetClock;
        bool groundedFromGravity;
        int nextAttack;
        CombatActor openingTarget;
        CombatActor reservedTarget;
        static readonly Dictionary<CombatActor, EnemyCombatAI> attackers = new();
        static readonly Dictionary<CombatActor, float> nextOpening = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCoordination() { attackers.Clear(); nextOpening.Clear(); }

        void Awake() { Actor = GetComponent<CombatActor>(); motor = GetComponent<CharacterController>(); }

        public void SetCombatEnabled(bool value)
        {
            ReleaseAttackTurn();
            if (Actor == null) Actor = GetComponent<CombatActor>();
            CombatEnabled = value;
            Target = null;
            openingTarget = null;
            Actor.CancelAttack();
            Actor.Guarding = false;
            cooldown = value ? .3f : 0f;
            State = value ? Behaviour.Approach : Behaviour.Waiting;
        }

        public void ClearTarget()
        {
            ReleaseAttackTurn();
            Target = null;
            openingTarget = null;
            Actor.CancelAttack();
            cooldown = .35f;
        }

        public void PreferOpeningTarget(CombatActor actor)
        {
            openingTarget = actor != null && Actor.IsHostileTo(actor) && !actor.IsDefeated ? actor : null;
            Target = openingTarget;
            retargetClock = .75f;
        }

        public void ResetBrain()
        {
            nextAttack = 0;
            verticalVelocity = 0f;
            groundedFromGravity = false;
            SetCombatEnabled(false);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || Actor == null) return;
            if (!Actor.IsAttacking) ReleaseAttackTurn();
            if (Actor.IsHitPaused) return;
            if (Actor.IsDefeated) { State = Behaviour.Defeated; return; }
            if (!motor.enabled) return;
            if (Actor.ControlLocked) return;
            // Later approach/attack root moves can overwrite motor.isGrounded.
            if (groundedFromGravity && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += -22f * dt;
            groundedFromGravity = (motor.Move(Vector3.up * (verticalVelocity * dt)) & CollisionFlags.Below) != 0;
            if (!CombatEnabled) { State = Behaviour.Waiting; SetLocomotion(0f); return; }
            if (Actor.IsStunned) { State = Behaviour.Stagger; return; }
            if (Actor.IsAttacking)
            {
                CombatAttack attack = Actor.CurrentAttack;
                float firstHit = attack.activeStart, lastHit = attack.activeEnd;
                if (attack.hitWindows != null && attack.hitWindows.Length > 0)
                {
                    firstHit = float.PositiveInfinity;
                    lastHit = 0f;
                    foreach (var window in attack.hitWindows)
                    {
                        if (window == null) continue;
                        firstHit = Mathf.Min(firstHit, window.start);
                        lastHit = Mathf.Max(lastHit, window.end);
                    }
                }
                State = Actor.AttackElapsed < firstHit ? Behaviour.Windup : Actor.AttackElapsed <= lastHit ? Behaviour.Strike : Behaviour.Recovery;
                // Lock facing before the active frames so a late dodge remains useful.
                if (Target != null && Actor.AttackElapsed < firstHit * .55f) Face(Target.transform.position - transform.position, dt);
                cooldown = attackInterval;
                return;
            }
            cooldown = Mathf.Max(0f, cooldown - dt);
            retargetClock -= dt;
            if (openingTarget != null && (!openingTarget.isActiveAndEnabled || openingTarget.IsDefeated || !Actor.IsHostileTo(openingTarget)))
                openingTarget = null;
            if (openingTarget != null) Target = openingTarget;
            else if (Target == null || !Target.isActiveAndEnabled || Target.IsDefeated || !Actor.IsHostileTo(Target) || retargetClock <= 0f)
            { SelectTarget(); retargetClock = .75f; }
            if (Target == null) { State = Behaviour.Waiting; SetLocomotion(0f); return; }
            Vector3 toward = Vector3.ProjectOnPlane(Target.transform.position - transform.position, Vector3.up);
            Face(toward, dt);
            float distance = toward.magnitude;
            if (distance > engageDistance)
            {
                State = Behaviour.Approach;
                if (Actor.Animation == null || !Actor.Animation.Ready || !Actor.Animation.HasMotion(CombatMotion.Walk))
                { SetLocomotion(0f); return; }
                Vector3 before = transform.position;
                Vector3 direction = AvoidObstruction(toward.normalized);
                motor.Move(direction * (Mathf.Min(moveSpeed, (distance - engageDistance) / Mathf.Max(.001f, dt)) * dt));
                SetLocomotion(Vector3.ProjectOnPlane(transform.position - before, Vector3.up).magnitude / Mathf.Max(.001f, dt));
            }
            else
            {
                State = cooldown > 0f ? Behaviour.Recovery : Behaviour.Approach;
                SetLocomotion(0f);
                if (cooldown <= 0f && Vector3.Angle(transform.forward, toward) < 25f && Actor.HasLineOfSight(Target)
                    && CanStartAttackTurn())
                {
                    CombatAttack chosen = NextAvailableAttack();
                    if (chosen != null && Actor.TryAttack(chosen))
                    {
                        ClaimAttackTurn();
                        nextAttack++;
                        openingTarget = null;
                    }
                    else cooldown = .5f;
                }
            }
        }

        bool CanStartAttackTurn()
        {
            if (!coordinateGruntAttacks || Actor.Role != CombatRole.Grunt || Target == null) return true;
            // A grunt outside the player's view may approach, but must not begin
            // a fresh blindside swing. Already telegraphed attacks still finish.
            if (Target.Role == CombatRole.Player && Camera.main != null)
            {
                Vector3 viewport = Camera.main.WorldToViewportPoint(transform.position + Vector3.up);
                if (viewport.z <= Camera.main.nearClipPlane || viewport.x < -.05f || viewport.x > 1.05f
                    || viewport.y < -.1f || viewport.y > 1.1f) return false;
            }
            if (attackers.TryGetValue(Target, out var other))
            {
                bool stillAttacking = other != null && other.isActiveAndEnabled && other.CombatEnabled
                    && other.Actor != null && !other.Actor.IsDefeated && !other.Actor.ControlLocked
                    && other.Actor.IsAttacking;
                if (stillAttacking && other != this) return false;
                if (!stillAttacking)
                {
                    // The previous actor may finish in LateUpdate, before its AI
                    // gets another Update. Release it BEFORE checking the gap so
                    // a different AI's earlier Update cannot steal the turn.
                    if (other != null) other.ReleaseAttackTurn();
                    if (attackers.ContainsKey(Target))
                    {
                        attackers.Remove(Target);
                        nextOpening[Target] = Time.time + sharedAttackGap;
                    }
                }
            }
            if (nextOpening.TryGetValue(Target, out float opensAt))
            {
                if (Time.time < opensAt) return false;
                nextOpening.Remove(Target);
            }
            return true;
        }

        void ClaimAttackTurn()
        {
            if (!coordinateGruntAttacks || Actor.Role != CombatRole.Grunt || Target == null) return;
            attackers[Target] = this;
            reservedTarget = Target;
        }

        void ReleaseAttackTurn()
        {
            if (object.ReferenceEquals(reservedTarget, null)) return;
            if (attackers.TryGetValue(reservedTarget, out var owner) && owner == this)
            {
                attackers.Remove(reservedTarget);
                if (reservedTarget != null) nextOpening[reservedTarget] = Time.time + sharedAttackGap;
            }
            reservedTarget = null;
        }

        void OnDisable() => ReleaseAttackTurn();
        void OnDestroy()
        {
            ReleaseAttackTurn();
            if (object.ReferenceEquals(Actor, null)) return;
            attackers.Remove(Actor);
            nextOpening.Remove(Actor);
        }

        void SelectTarget()
        {
            float nearest = detectionRadius * detectionRadius;
            Target = null;
            foreach (CombatActor other in CombatActor.Actors)
            {
                if (!Actor.IsHostileTo(other) || other.IsDefeated || !other.isActiveAndEnabled || other.ControlLocked) continue;
                float distance = (other.transform.position - transform.position).sqrMagnitude;
                if (distance < nearest) { nearest = distance; Target = other; }
            }
        }

        CombatAttack NextAvailableAttack()
        {
            if (attacks == null || attacks.Length == 0 || Actor.Animation == null || !Actor.Animation.Ready) return null;
            for (int i = 0; i < attacks.Length; i++)
            {
                CombatAttack attack = attacks[(nextAttack + i) % attacks.Length];
                if (attack != null && Actor.Animation.HasMotion(attack.motion)) return attack;
            }
            return null;
        }

        void SetLocomotion(float speed)
        {
            if (Actor.Animation != null && Actor.Animation.Ready && !Actor.IsAttacking && !Actor.IsStunned && !Actor.IsDefeated)
                Actor.Animation.SetLocomotion(speed, CombatEnabled);
        }

        void Face(Vector3 toward, float dt)
        {
            toward = Vector3.ProjectOnPlane(toward, Vector3.up);
            if (toward.sqrMagnitude > .001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toward), turnSpeed * dt);
        }

        Vector3 AvoidObstruction(Vector3 direction)
        {
            Vector3 origin = transform.position + Vector3.up * .65f;
            foreach (RaycastHit hit in Physics.SphereCastAll(origin, motor.radius * .8f, direction, .8f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<CombatActor>() != null) continue;
                Vector3 tangent = Vector3.ProjectOnPlane(direction, hit.normal);
                tangent.y = 0f;
                if (tangent.sqrMagnitude < .01f) tangent = Vector3.Cross(Vector3.up, hit.normal);
                return tangent.normalized;
            }
            return direction;
        }
    }
}
