using System;
using UnityEngine;

namespace FengChenInn
{
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(100)]
    public sealed class PrototypeFighter : MonoBehaviour
    {
        // First four values and legacy fields keep existing scene/editor references intact.
        public enum ActionState { Ready, LightAttack, HeavyAttack, Dodge, Skill }
        [Header("Camera-relative movement")]
        [Min(.1f)] public float moveSpeed = 4.2f;
        [Min(.1f)] public float walkSpeed = 2.2f;
        [Min(.1f)] public float acceleration = 16f;
        [Min(.1f)] public float deceleration = 22f;
        [Header("Indoor movement")]
        public bool useIndoorMovementTuning = true;
        [Min(.1f)] public float indoorMoveSpeed = 4.2f;
        [Min(.1f)] public float indoorAcceleration = 40f;
        [Min(.1f)] public float indoorBraking = 60f;
        [Min(0f)] public float guardAdjustmentSpeed = .65f;
        public float jumpHeight = 1.1f;
        public float gravity = -22f;
        public float turnSpeed = 900f;
        [Range(0f, 1f)] public float guardMoveMultiplier = .35f;
        [Range(0f, .3f)] public float coyoteTime = .08f;
        [Range(0f, .3f)] public float inputBufferTime = .16f;
        [Range(0f, .4f)] public float landingBlendTime = .16f;
        [Header("Stamina")]
        [Min(1f)] public float maxStamina = 100f;
        // Retained for serialized compatibility with older scenes; running is free.
        [HideInInspector] public float runStaminaPerSecond = 16f;
        [Min(0f)] public float jumpStaminaCost = 18f;
        [Min(0f)] public float dodgeStaminaCost = 22f;
        [Min(0f)] public float guardStaminaPerSecond = 12f;
        public bool useImpactGuardCost = true;
        [Min(0f)] public float guardHoldStaminaPerSecond = 2f;
        [Min(0f)] public float guardHitStaminaCost = 9f;
        [Min(0f)] public float staminaRecoveryPerSecond = 24f;
        [Min(0f)] public float staminaRecoveryDelay = .8f;
        [Header("Animation and weapon combat")]
        public AholoAnimationDriver animationDriver;
        [Tooltip("Keep the whole clip. Mark damage windows in seconds after previewing its sword path.")]
        public bool useFullClipDurations = true;
        [Header("Attack direction assistance")]
        public bool enableTargetAssist = true;
        [Min(.1f)] public float targetAssistDistance = 2.5f;
        [Range(5f, 85f)] public float targetAssistHalfAngle = 65f;
        [Min(0f)] public float attackTurnSpeed = 720f;
        [Range(0f, .3f)] public float attackTurnTime = .14f;
        [Header("Recovery control")]
        [Tooltip("Allow a new input after the final damage window; the full animation still finishes at rest.")]
        [Range(0f, .5f)] public float lightRecoveryDelay = .12f;
        [Range(0f, .5f)] public float heavyRecoveryDelay = .18f;
        [Range(0f, .5f)] public float skillRecoveryDelay = .16f;
        [Tooltip("Heavy still plays all strikes by default; guard or dodge can withdraw between strikes.")]
        public bool allowHeavyDefensiveExit = true;
        [Range(0f, .3f)] public float betweenStrikeRecovery = .10f;
        public CombatAttack lightAttack = new CombatAttack
        {
            motion = CombatMotion.Light, damage = 18, duration = .9f,
            activeStart = .25f, activeEnd = .48f, range = 1.8f, knockback = .14f, stagger = .22f
        };
        public CombatAttack heavyAttack = new CombatAttack
        {
            motion = CombatMotion.Heavy, damage = 32, duration = 2.2f,
            activeStart = .6f, activeEnd = .88f, range = 2f, knockback = .35f, stagger = .42f
        };
        public CombatAttack skillAttack = new CombatAttack
        {
            motion = CombatMotion.Skill, damage = 26, duration = 1.2f,
            activeStart = .35f, activeEnd = .62f, range = 2f, knockback = .27f, stagger = .32f
        };
        [Min(0f)] public float skillCooldown = 6f;
        [Min(0f)] public float skillTravelDistance = 2.4f;
        [Range(0f, 1f)] public float skillTravelStart = .05f;
        [Range(0f, 1f)] public float skillTravelEnd = .38f;
        [Min(.05f)] public float dodgeDuration = .45f;
        [Min(0f)] public float dodgeDistance = 1.7f;
        public Vector2 dodgeInvulnerability = new Vector2(.06f, .25f);
        [Header("Legacy references - no longer animate the character")]
        public Transform visualRoot, leftArm, rightArm, leftLeg, rightLeg;
        public RocketboxVisual skinnedVisual;
        public Vector2 MoveInput { get; set; }
        public bool RunRequested { get; set; }
        public bool CrouchRequested { get; set; }
        public bool GuardRequested { get; set; }
        public ActionState State { get; private set; }
        public bool IsCrouching => false;
        // CharacterController.isGrounded describes its LAST Move. CombatActor may
        // issue a horizontal Move in LateUpdate, so keep our gravity-move result.
        public bool IsGrounded => controller != null && controller.enabled && groundedFromMovement && verticalVelocity <= .01f;
        public bool IsGuarding => GuardRequested && IsGrounded && State == ActionState.Ready
            && !guardExhausted && (GuardHoldCost <= 0f && !useImpactGuardCost || Stamina > .001f)
            && !MovementLocked && IsMotionAvailable(CombatMotion.Guard);
        public bool IsRunning { get; private set; }
        public bool IsInvulnerable => State == ActionState.Dodge && actionTime >= dodgeInvulnerability.x
            && actionTime < dodgeInvulnerability.y && !MovementLocked;
        public bool IsAttackActive
        {
            get
            {
                if (actor == null || !actor.IsAttacking || actor.CurrentAttack == null) return false;
                CombatAttack attack = actor.CurrentAttack;
                if (attack.hitWindows != null && attack.hitWindows.Length > 0)
                {
                    foreach (CombatHitWindow window in attack.hitWindows)
                        if (window != null && actor.AttackElapsed >= window.start && actor.AttackElapsed <= window.end) return true;
                    return false;
                }
                return actor.AttackElapsed >= attack.activeStart && actor.AttackElapsed <= attack.activeEnd;
            }
        }
        public float Stamina { get; private set; }
        public float MaxStamina => maxStamina;
        public float Stamina01 => Stamina / Mathf.Max(1f, maxStamina);
        public float SkillCooldownRemaining => Mathf.Max(0f, skillReadyAt - Time.time);
        public float HitRecoil => Mathf.Max(impact.Strength, actor != null && actor.IsStunned ? 1f : 0f);
        public bool CanAnimateJump => IsMotionAvailable(CombatMotion.JumpStart)
            && IsMotionAvailable(CombatMotion.JumpLoop) && IsMotionAvailable(CombatMotion.JumpLand);
        public string Status => actor != null && actor.IsDefeated ? "Defeated" : MovementLocked ? "Stagger"
            : State != ActionState.Ready ? State.ToString() : !IsGrounded ? "Jump" : IsGuarding ? "Guard"
            : IsRunning ? "Run" : planarVelocity.sqrMagnitude > .02f ? "Walk" : "Idle";
        public int LightAttackCount { get; private set; }
        public int HeavyAttackCount { get; private set; }
        public int JumpCount { get; private set; }
        public int DodgeCount { get; private set; }
        public int SkillCount { get; private set; }
        public int GuardCount { get; private set; }
        public event Action<CombatMotion> ActionPerformed;
        public CombatActor SelectedTarget => CombatTargeting.IsValid(actor, actor != null && actor.IsAttacking ? attackTarget : selectedTarget)
            ? (actor.IsAttacking ? attackTarget : selectedTarget) : null;
        public bool CanExitAttackRecovery => actor != null && actor.IsAttacking && !MovementLocked
            && actor.AttackElapsed >= actor.LastHitTime + RecoveryDelay(actor.CurrentAttack.motion);
        public bool CanDefendCancelAttack => CanExitAttackRecovery || allowHeavyDefensiveExit
            && actor != null && actor.IsAttacking && !MovementLocked
            && actor.CurrentAttack.motion == CombatMotion.Heavy && actor.IsBetweenStrikes(betweenStrikeRecovery, .08f);
        public float EffectiveMoveSpeed => useIndoorMovementTuning ? indoorMoveSpeed : moveSpeed;
        private float GuardHoldCost => useImpactGuardCost ? guardHoldStaminaPerSecond : guardStaminaPerSecond;
        private enum BufferedAction { None, Jump, Light, Heavy, Dodge, Skill }
        private CharacterController controller;
        private CombatActor actor, subscribedActor;
        private CombatActor selectedTarget, attackTarget, guardTarget;
        private Vector3 attackIntent;
        private DuelEncounter encounter;
        private Vector3 spawnPosition, planarVelocity, dodgeDirection, skillDirection;
        private Quaternion spawnRotation;
        private float verticalVelocity, actionTime, lastGroundedAt = -10f, jumpedAt = -10f;
        private float landingUntil, lastStaminaUseAt = -10f, skillReadyAt;
        private bool wasGrounded, wasGuarding, guardExhausted, groundedFromMovement;
        private BufferedAction queuedAction;
        private float queuedUntil;
        private ImpactSlide impact;
        private bool MovementLocked => impact.Active || actor != null && (actor.IsDefeated || actor.IsStunned || actor.ControlLocked);

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            controller.radius = Mathf.Max(controller.radius, .36f);
            controller.height = 1.8f;
            controller.center = Vector3.up * .9f;
            Stamina = maxStamina;
            RefreshBindings();
        }
        public void RefreshBindings()
        {
            if (animationDriver == null) animationDriver = GetComponentInChildren<AholoAnimationDriver>(true);
            actor = GetComponent<CombatActor>();
            encounter = GetComponent<DuelEncounter>();
            if (subscribedActor == actor) return;
            if (subscribedActor != null) { subscribedActor.Damaged -= OnDamaged; subscribedActor.Blocked -= OnBlocked; }
            subscribedActor = actor;
            if (subscribedActor != null) { subscribedActor.Damaged += OnDamaged; subscribedActor.Blocked += OnBlocked; }
        }
        private void OnDestroy()
        {
            RestoreLegacyDodgePose();
            if (subscribedActor != null) { subscribedActor.Damaged -= OnDamaged; subscribedActor.Blocked -= OnBlocked; }
        }
        public bool IsMotionAvailable(CombatMotion motion) => animationDriver != null
            && animationDriver.Ready && (animationDriver.HasMotion(motion)
                || motion == CombatMotion.Dodge && ResolveLegacyDodgeBones());

        // The previous dodge was procedural, not an animation clip. Reapply its
        // crouch/lean and modest steps to the new humanoid after Animator updates.
        private Animator legacyDodgeAnimator;
        private Transform dodgeHips, dodgeSpine, dodgeLeftUpper, dodgeRightUpper, dodgeLeftLower, dodgeRightLower;
        private readonly Transform[] dodgeBones = new Transform[6];
        private readonly Quaternion[] dodgeBaseRotations = new Quaternion[6];
        private Vector3 dodgeBaseHipsPosition;
        private bool dodgePoseApplied;

        private bool ResolveLegacyDodgeBones()
        {
            Animator animator = animationDriver != null ? animationDriver.targetAnimator : null;
            if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid) return false;
            if (legacyDodgeAnimator == animator && dodgeHips != null && dodgeSpine != null
                && dodgeLeftUpper != null && dodgeRightUpper != null && dodgeLeftLower != null && dodgeRightLower != null) return true;
            legacyDodgeAnimator = animator;
            dodgeHips = animator.GetBoneTransform(HumanBodyBones.Hips);
            dodgeSpine = animator.GetBoneTransform(HumanBodyBones.Spine);
            dodgeLeftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            dodgeRightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            dodgeLeftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            dodgeRightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            dodgeBones[0] = dodgeHips; dodgeBones[1] = dodgeSpine;
            dodgeBones[2] = dodgeLeftUpper; dodgeBones[3] = dodgeRightUpper;
            dodgeBones[4] = dodgeLeftLower; dodgeBones[5] = dodgeRightLower;
            return dodgeHips != null && dodgeSpine != null && dodgeLeftUpper != null
                && dodgeRightUpper != null && dodgeLeftLower != null && dodgeRightLower != null;
        }

        private void RestoreLegacyDodgePose()
        {
            if (!dodgePoseApplied) return;
            for (int i = 0; i < dodgeBones.Length; i++)
                if (dodgeBones[i] != null) dodgeBones[i].localRotation = dodgeBaseRotations[i];
            if (dodgeHips != null) dodgeHips.localPosition = dodgeBaseHipsPosition;
            dodgePoseApplied = false;
        }

        private void LateUpdate()
        {
            if (State != ActionState.Dodge || animationDriver == null || animationDriver.HasMotion(CombatMotion.Dodge)
                || !ResolveLegacyDodgeBones()) return;
            float dive = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .10f, actionTime));
            float recover = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.27f, .45f, actionTime));
            float weight = dive * (1f - recover);
            if (weight <= 0f) return;
            for (int i = 0; i < dodgeBones.Length; i++) dodgeBaseRotations[i] = dodgeBones[i].localRotation;
            dodgeBaseHipsPosition = dodgeHips.localPosition;
            dodgePoseApplied = true;
            // Convert the world-space lowering through the bone parent so model
            // scale does not multiply the old motion's 0.34 m body drop.
            dodgeHips.localPosition -= dodgeHips.parent.InverseTransformVector(transform.up * (.315f * weight));
            dodgeSpine.rotation = Quaternion.AngleAxis(36f * weight, transform.right) * dodgeSpine.rotation;
            float step = Mathf.Sin(actionTime * 18f) * weight;
            dodgeLeftUpper.rotation = Quaternion.AngleAxis((24f + 12f * step) * weight, transform.right) * dodgeLeftUpper.rotation;
            dodgeRightUpper.rotation = Quaternion.AngleAxis((24f - 12f * step) * weight, transform.right) * dodgeRightUpper.rotation;
            dodgeLeftLower.rotation = Quaternion.AngleAxis(-35f * weight, transform.right) * dodgeLeftLower.rotation;
            dodgeRightLower.rotation = Quaternion.AngleAxis(-35f * weight, transform.right) * dodgeRightLower.rotation;
        }

        private void OnDisable() => RestoreLegacyDodgePose();
        private Vector3 CameraMovement()
        {
            Transform view = Camera.main != null ? Camera.main.transform : transform;
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .001f) forward = transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return Vector3.ClampMagnitude(forward * MoveInput.y + right * MoveInput.x, 1f);
        }
        public bool Jump() => Request(BufferedAction.Jump);
        public bool Attack(bool heavy) => Request(heavy ? BufferedAction.Heavy : BufferedAction.Light);
        public bool Dodge() => Request(BufferedAction.Dodge);
        public bool Skill() => Request(BufferedAction.Skill);
        private float RecoveryDelay(CombatMotion motion) => motion == CombatMotion.Heavy ? heavyRecoveryDelay
            : motion == CombatMotion.Skill ? skillRecoveryDelay : lightRecoveryDelay;

        private void RefreshTarget(float range)
        {
            if (!enableTargetAssist || actor == null || actor.IsDefeated || actor.ControlLocked
                || encounter != null && !encounter.IsCombatActive)
            { selectedTarget = null; return; }
            Vector3 intent = GuardRequested ? transform.forward : CameraMovement();
            if (intent.sqrMagnitude < .01f) intent = transform.forward;
            selectedTarget = CombatTargeting.Select(actor, Camera.main, intent,
                Mathf.Min(targetAssistDistance, range + .35f), targetAssistHalfAngle, selectedTarget);
        }

        private void UpdateAttackFacing(float dt)
        {
            if (actor == null || !actor.IsAttacking) return;
            if (!enableTargetAssist || !CombatTargeting.IsValid(actor, attackTarget))
            { attackTarget = null; return; }
            Vector3 toward = Vector3.ProjectOnPlane(attackTarget.transform.position - transform.position, Vector3.up);
            if (toward.sqrMagnitude < .001f || toward.magnitude > Mathf.Min(targetAssistDistance, actor.CurrentAttack.range + .35f)
                || Vector3.Angle(attackIntent, toward) > targetAssistHalfAngle || !actor.HasLineOfSight(attackTarget))
            { attackTarget = null; return; }
            bool comboWindup = actor.CurrentAttack.motion == CombatMotion.Heavy && actor.IsComboWindup(attackTurnTime);
            if (MovementLocked || !comboWindup && actor.AttackElapsed >= Mathf.Min(attackTurnTime, actor.FirstHitTime * .75f)) return;
            actor.SetAttackFacing(Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toward), attackTurnSpeed * dt), comboWindup, attackTurnTime);
            skillDirection = transform.forward;
        }

        private void ExitAttackRecovery()
        {
            actor.CancelAttack();
            attackTarget = null;
            State = ActionState.Ready;
            actionTime = 0f;
        }
        private bool Request(BufferedAction requested)
        {
            if (TryExecute(requested)) { queuedAction = BufferedAction.None; return true; }
            bool available = requested == BufferedAction.Jump ? CanAnimateJump : IsMotionAvailable(MotionFor(requested));
            if (CanBufferInput() && available)
            {
                queuedAction = requested;
                queuedUntil = Time.time + inputBufferTime;
            }
            return false;
        }
        private bool CanBufferInput()
        {
            if (actor != null && (actor.IsDefeated || actor.ControlLocked)) return false;
            // Only the final short part of hitstun accepts an early input. A
            // long stun must never release a stale attack several seconds later.
            return !MovementLocked || !impact.Active && actor != null && actor.IsStunned
                && actor.StunRemaining <= inputBufferTime;
        }
        private static CombatMotion MotionFor(BufferedAction action) => action switch
        {
            BufferedAction.Jump => CombatMotion.JumpStart,
            BufferedAction.Heavy => CombatMotion.Heavy,
            BufferedAction.Skill => CombatMotion.Skill,
            BufferedAction.Dodge => CombatMotion.Dodge,
            _ => CombatMotion.Light
        };
        private bool TryExecute(BufferedAction requested)
        {
            CombatAttack requestedAttack = requested == BufferedAction.Heavy ? heavyAttack
                : requested == BufferedAction.Skill ? skillAttack : requested == BufferedAction.Light ? lightAttack : null;
            if (actor != null && actor.IsHitPaused) return false;
            // Only defensive actions may abandon the remaining heavy strikes.
            // Unavailable/costly inputs cannot cancel an action for free.
            bool mayExit = CanExitAttackRecovery || requested == BufferedAction.Dodge && CanDefendCancelAttack;
            if (mayExit && IsGrounded && Time.time >= landingUntil
                && (requested == BufferedAction.Jump ? CanAnimateJump && !IsGuarding && Stamina >= jumpStaminaCost
                    : IsMotionAvailable(MotionFor(requested))
                        && (requested == BufferedAction.Dodge || requestedAttack != null && actor.HasWeapon)
                        && (requested != BufferedAction.Dodge || Stamina >= dodgeStaminaCost)
                        && (requested != BufferedAction.Skill || Time.time >= skillReadyAt)))
                ExitAttackRecovery();
            if (MovementLocked || State != ActionState.Ready || Time.time < landingUntil) return false;
            if (requested == BufferedAction.Jump)
            {
                if (!CanAnimateJump || IsGuarding || verticalVelocity > .01f
                    || (!IsGrounded && Time.time - lastGroundedAt > coyoteTime) || !SpendStamina(jumpStaminaCost)) return false;
                verticalVelocity = Mathf.Sqrt(Mathf.Max(0f, -2f * gravity * jumpHeight));
                jumpedAt = Time.time;
                wasGrounded = false;
                groundedFromMovement = false;
                animationDriver.Play(CombatMotion.JumpStart, .07f);
                JumpCount++;
                ActionPerformed?.Invoke(CombatMotion.JumpStart);
                return true;
            }
            if (!IsGrounded || actor == null || actor.IsAttacking) return false;
            CombatMotion motion = MotionFor(requested);
            if (!IsMotionAvailable(motion)) return false;
            if (requested == BufferedAction.Dodge)
            {
                if (!SpendStamina(dodgeStaminaCost)) return false;
                dodgeDirection = CameraMovement();
                if (dodgeDirection.sqrMagnitude < .01f) dodgeDirection = -transform.forward;
                State = ActionState.Dodge;
                actionTime = 0f;
                GuardRequested = false;
                if (animationDriver.HasMotion(CombatMotion.Dodge))
                    animationDriver.Play(CombatMotion.Dodge, .06f,
                        animationDriver.MotionLength(CombatMotion.Dodge) / Mathf.Max(.05f, dodgeDuration));
                else
                    animationDriver.Play(CombatMotion.Idle, .06f);
                DodgeCount++;
                ActionPerformed?.Invoke(motion);
                return true;
            }
            if (requested == BufferedAction.Skill && Time.time < skillReadyAt) return false;
            CombatAttack attack = requested == BufferedAction.Heavy ? heavyAttack
                : requested == BufferedAction.Skill ? skillAttack : lightAttack;
            if (attack == null) return false;
            attack.motion = motion;
            if (useFullClipDurations)
                attack.duration = animationDriver.MotionLength(motion, attack.duration) / Mathf.Max(.05f, attack.speed);
            RefreshTarget(attack.range);
            if (!actor.TryAttack(attack)) return false;
            attackTarget = selectedTarget;
            attackIntent = CameraMovement();
            if (attackIntent.sqrMagnitude < .01f) attackIntent = transform.forward;
            GuardRequested = false;
            actor.Guarding = false;
            State = requested == BufferedAction.Heavy ? ActionState.HeavyAttack
                : requested == BufferedAction.Skill ? ActionState.Skill : ActionState.LightAttack;
            actionTime = 0f;
            planarVelocity = Vector3.zero;
            if (requested == BufferedAction.Heavy) HeavyAttackCount++;
            else if (requested == BufferedAction.Skill)
            {
                SkillCount++;
                skillReadyAt = Time.time + skillCooldown;
                skillDirection = transform.forward;
            }
            else LightAttackCount++;
            ActionPerformed?.Invoke(motion);
            return true;
        }
        private bool SpendStamina(float cost)
        {
            if (Stamina + .001f < cost) return false;
            Stamina = Mathf.Max(0f, Stamina - cost);
            lastStaminaUseAt = Time.time;
            return true;
        }
        private void Update()
        {
            RestoreLegacyDodgePose();
            float dt = Time.deltaTime;
            if (dt <= 0f || controller == null || !controller.enabled) return;
            if (actor == null || animationDriver == null) RefreshBindings();
            if (actor != null && actor.IsHitPaused)
            {
                // A few contact-stop frames must not consume the player's short
                // pre-input window while the recovery clock itself is frozen.
                if (queuedAction != BufferedAction.None) queuedUntil += dt;
                return;
            }
            if (actor == null || !actor.IsAttacking)
                RefreshTarget(Mathf.Max(lightAttack != null ? lightAttack.range : 0f, heavyAttack != null ? heavyAttack.range : 0f));
            UpdateAttackFacing(dt);
            CrouchRequested = false;
            if (IsGrounded) lastGroundedAt = Time.time;
            float previousActionTime = actionTime;
            actionTime += dt;
            if ((State == ActionState.LightAttack || State == ActionState.HeavyAttack || State == ActionState.Skill)
                && (actor == null || !actor.IsAttacking)) State = ActionState.Ready;
            bool finishingDodge = State == ActionState.Dodge && actionTime >= dodgeDuration;
            if (MovementLocked)
            {
                State = ActionState.Ready;
                if (!CanBufferInput()) queuedAction = BufferedAction.None;
            }
            bool recoverToGuard = CanDefendCancelAttack && GuardRequested && IsGrounded && !guardExhausted
                && (GuardHoldCost <= 0f && !useImpactGuardCost || Stamina > .001f) && IsMotionAvailable(CombatMotion.Guard);
            // A defensive hold supersedes a previously queued attack, but not a newer dodge.
            if (recoverToGuard && (queuedAction == BufferedAction.Light || queuedAction == BufferedAction.Heavy || queuedAction == BufferedAction.Skill))
                queuedAction = BufferedAction.None;
            if (recoverToGuard || CanExitAttackRecovery && MoveInput.sqrMagnitude > .01f)
                ExitAttackRecovery();
            if (queuedAction != BufferedAction.None)
            {
                if (Time.time > queuedUntil) queuedAction = BufferedAction.None;
                else if (TryExecute(queuedAction))
                {
                    queuedAction = BufferedAction.None;
                    previousActionTime = 0f;
                }
            }
            if (!GuardRequested) guardExhausted = false;
            bool guarding = IsGuarding;
            if (guarding && GuardHoldCost > 0f)
            {
                Stamina = Mathf.Max(0f, Stamina - GuardHoldCost * dt);
                lastStaminaUseAt = Time.time;
                if (Stamina <= 0f) { guardExhausted = true; guarding = false; }
            }
            else if (Time.time - lastStaminaUseAt >= staminaRecoveryDelay && (actor == null || !actor.IsDefeated))
                Stamina = Mathf.Min(maxStamina, Stamina + staminaRecoveryPerSecond * dt);
            if (actor != null) { actor.Guarding = guarding; actor.Invulnerable = IsInvulnerable; }
            if (guarding && !wasGuarding)
            {
                guardTarget = SelectedTarget;
                GuardCount++;
                ActionPerformed?.Invoke(CombatMotion.Guard);
            }
            if (!guarding) guardTarget = null;
            else if (CombatTargeting.IsValid(actor, guardTarget)
                && Vector3.Distance(transform.position, guardTarget.transform.position) <= targetAssistDistance
                && actor.HasLineOfSight(guardTarget))
            {
                Vector3 toward = Vector3.ProjectOnPlane(guardTarget.transform.position - transform.position, Vector3.up);
                if (toward.sqrMagnitude > .01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toward), turnSpeed * dt);
            }
            wasGuarding = guarding;
            Vector3 movement = CameraMovement();
            bool canMove = !MovementLocked && State == ActionState.Ready;
            // Guard remains directed where the player raised it. In particular,
            // S must step away from the enemy rather than turn the guard backward.
            if (canMove && !guarding && movement.sqrMagnitude > .001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(movement), turnSpeed * dt);
            IsRunning = canMove && IsGrounded && !guarding && movement.sqrMagnitude > .01f;
            float speed = EffectiveMoveSpeed;
            if (guarding) speed = Mathf.Min(speed * guardMoveMultiplier, guardAdjustmentSpeed);
            Vector3 desiredVelocity = canMove ? movement * speed : Vector3.zero;
            float startRate = useIndoorMovementTuning ? indoorAcceleration : acceleration;
            float stopRate = useIndoorMovementTuning ? indoorBraking : deceleration;
            float smoothRate = desiredVelocity.sqrMagnitude > planarVelocity.sqrMagnitude ? startRate : stopRate;
            planarVelocity = Vector3.MoveTowards(planarVelocity, desiredVelocity, smoothRate * dt);
            if (!canMove) planarVelocity = Vector3.zero;
            Vector3 displacement = planarVelocity * dt;
            if (State == ActionState.Dodge)
            {
                float before = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(previousActionTime / Mathf.Max(.05f, dodgeDuration)));
                float after = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(actionTime / Mathf.Max(.05f, dodgeDuration)));
                displacement += dodgeDirection * (dodgeDistance * (after - before));
            }
            else if (State == ActionState.Skill && actor != null
                && (animationDriver == null || animationDriver.motionSet == null
                    || !animationDriver.motionSet.HasRootMotion(CombatMotion.Skill)))
            {
                float duration = Mathf.Max(.05f, actor.AttackDuration);
                float start = skillTravelStart * duration;
                float end = Mathf.Max(start + .01f, skillTravelEnd * duration);
                float before = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, previousActionTime));
                float after = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, actionTime));
                displacement += skillDirection * (skillTravelDistance * (after - before));
            }
            if (IsGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * dt;
            CollisionFlags flags = MoveWithStepFallback(displacement + Vector3.up * (verticalVelocity * dt) + impact.Advance(dt));
            groundedFromMovement = (flags & CollisionFlags.Below) != 0;
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
            bool groundedNow = IsGrounded;
            if (groundedNow && !wasGrounded && Time.time - jumpedAt > .05f
                && !MovementLocked && State == ActionState.Ready && (actor == null || !actor.IsAttacking))
            {
                landingUntil = Time.time + landingBlendTime;
                if (IsMotionAvailable(CombatMotion.JumpLand)) animationDriver.Play(CombatMotion.JumpLand, .07f);
            }
            wasGrounded = groundedNow;
            if (finishingDodge) State = ActionState.Ready;
            UpdateAnimation(groundedNow, guarding);
            if (transform.position.y < -8f) ResetPose();
        }
        private CollisionFlags MoveWithStepFallback(Vector3 displacement)
        {
            Vector3 before = transform.position;
            CollisionFlags flags = controller.Move(displacement);
            Vector3 horizontal = Vector3.ProjectOnPlane(displacement, Vector3.up);
            Vector3 travelled = Vector3.ProjectOnPlane(transform.position - before, Vector3.up);
            // PhysX's auto-step can wedge the capsule against a sloping handrail:
            // even movement away from it is then rejected. Retry only the missing
            // horizontal movement without stepping; keep every collision enabled.
            // Gravity has already been applied, and an airborne actor never retries.
            if (controller.stepOffset <= 0f || horizontal.sqrMagnitude < .000001f
                || (flags & (CollisionFlags.Sides | CollisionFlags.Below)) != (CollisionFlags.Sides | CollisionFlags.Below)
                || travelled.sqrMagnitude >= horizontal.sqrMagnitude * .04f) return flags;
            float step = controller.stepOffset;
            try
            {
                controller.stepOffset = 0f;
                flags |= controller.Move(horizontal - travelled);
            }
            finally { controller.stepOffset = step; }
            return flags;
        }
        private void UpdateAnimation(bool grounded, bool guarding)
        {
            if (animationDriver == null || !animationDriver.Ready || MovementLocked
                || State != ActionState.Ready || actor != null && actor.IsAttacking) return;
            if (!grounded)
            {
                float fraction = animationDriver.motionSet != null ? animationDriver.motionSet.jumpStartToLoopNormalized : .65f;
                if (Time.time - jumpedAt >= animationDriver.MotionLength(CombatMotion.JumpStart, .25f) * fraction)
                    if (IsMotionAvailable(CombatMotion.JumpLoop)) animationDriver.Play(CombatMotion.JumpLoop, .1f);
                return;
            }
            if (Time.time < landingUntil) return;
            if (guarding) animationDriver.Play(CombatMotion.Guard, .1f);
            else
            {
                // At rest, finish the real block clip after releasing Q. Walking,
                // jumping or starting an attack may interrupt this recovery.
                if (planarVelocity.sqrMagnitude < .01f && animationDriver.ReleaseGuard()) return;
                if (planarVelocity.magnitude >= .08f && IsMotionAvailable(CombatMotion.Run))
                {
                    float reference = animationDriver.motionSet.runReferenceSpeed;
                    float rate = Mathf.Clamp(planarVelocity.magnitude / Mathf.Max(.1f, reference), .35f, 1.5f);
                    animationDriver.Play(CombatMotion.Run, animationDriver.motionSet.locomotionFade, rate);
                }
                else animationDriver.SetLocomotion(0f, encounter != null && encounter.IsCombatActive);
            }
        }
        private void OnDamaged(CombatActor source, int amount)
        {
            State = ActionState.Ready;
            actionTime = 0f;
            ClearInput();
            planarVelocity = Vector3.zero;
            // CombatActor owns the hit pose and controller-based knockback.
        }
        private void OnBlocked(CombatActor source)
        {
            if (!useImpactGuardCost) return;
            float multiplier = source != null && source.CurrentAttack != null
                && source.CurrentAttack.motion == CombatMotion.Heavy ? 1.5f : 1f;
            Stamina = Mathf.Max(0f, Stamina - guardHitStaminaCost * multiplier);
            lastStaminaUseAt = Time.time;
            if (Stamina <= 0f)
            {
                guardExhausted = true;
                if (actor != null) actor.Guarding = false;
            }
        }
        public void ReceiveHit(Vector3 away, float distance)
        {
            impact.Start(away, distance);
            State = ActionState.Ready;
            actionTime = 0f;
            ClearInput();
        }
        public void ClearInput()
        {
            MoveInput = Vector2.zero;
            RunRequested = GuardRequested = CrouchRequested = false;
            queuedAction = BufferedAction.None;
            selectedTarget = attackTarget = guardTarget = null;
            if (actor != null) actor.Guarding = false;
        }
        public void ResetPose()
        {
            PlaceForStory(spawnPosition, spawnRotation);
            Stamina = maxStamina;
            lastStaminaUseAt = -10f;
            skillReadyAt = 0f;
            guardExhausted = false;
            LightAttackCount = HeavyAttackCount = JumpCount = DodgeCount = SkillCount = GuardCount = 0;
        }
        public void PlaceForStory(Vector3 position, Quaternion rotation)
        {
            if (controller == null) controller = GetComponent<CharacterController>();
            bool controllerWasEnabled = controller.enabled;
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = controllerWasEnabled;
            verticalVelocity = 0f;
            planarVelocity = Vector3.zero;
            impact = default;
            actionTime = 0f;
            landingUntil = 0f;
            jumpedAt = lastGroundedAt = -10f;
            wasGrounded = wasGuarding = groundedFromMovement = false;
            State = ActionState.Ready;
            ClearInput();
            if (actor != null) { actor.CancelAttack(); actor.Invulnerable = false; }
            if (animationDriver != null) animationDriver.ClearImpactPause();
            CombatMotion rest = encounter != null && encounter.IsCombatActive ? CombatMotion.Idle : CombatMotion.ExplorationIdle;
            if (animationDriver != null && animationDriver.Ready && animationDriver.HasMotion(rest))
                animationDriver.Play(rest, .1f);
        }
    }
}
