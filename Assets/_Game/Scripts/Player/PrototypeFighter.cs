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
        [Tooltip("Limit a single rendered turn during a slow frame; input movement remains immediate.")]
        [Range(15f, 60f)] public float maximumLocomotionTurnStep = 35f;
        [Range(0f, 1f)] public float guardMoveMultiplier = .35f;
        [Range(0f, .3f)] public float coyoteTime = .08f;
        [Range(0f, .3f)] public float inputBufferTime = .16f;
        [Range(0f, .4f)] public float landingBlendTime = .16f;
        [Header("Uneven ground")]
        [Range(0f, .4f)] public float groundSnapDistance = .28f;
        [Range(.05f, .4f)] public float minimumFallTime = .18f;
        [Min(.1f)] public float minimumLandingDrop = .35f;
        [Tooltip("Use the shallow contact part of the existing landing clip instead of its deep crouch.")]
        public bool useLightLanding = true;
        [Range(.06f, .2f)] public float lightLandingClipSeconds = .10f;
        [Header("Running sword carry")]
        public bool stabilizeRunningSword = true;
        [Range(0f, .8f)] public float runningHandCorrectionWeight = .55f;
        [Range(.1f, .4f)] public float runningPoseBlendTime = .20f;
        [Range(.5f, 1.1f)] public float fullSpeedRunPlayback = .82f;
        public Vector3 runningHandPosition = new Vector3(.38f, .98f, .15f);
        public Vector3 runningBladeDirection = new Vector3(.30f, -.75f, .58f);
        public Vector3 runningOffhandPosition = new Vector3(-.32f, .90f, .10f);
        [Range(0f, .65f)] public float runningOffhandWeight = .65f;
        [Range(0f, .15f)] public float runningOffhandSway = .08f;
        [Header("Stamina")]
        [Min(1f)] public float maxStamina = 100f;
        // Retained for serialized compatibility with older scenes; running is free.
        [HideInInspector] public float runStaminaPerSecond = 16f;
        [Min(0f)] public float jumpStaminaCost = 18f;
        [Min(0f)] public float dodgeStaminaCost = 22f;
        [Min(0f)] public float skillStaminaCost = 30f;
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
        [Header("Optional sequential heavy attacks — disable only this to restore the full combo")]
        public bool useSequentialHeavy = true;
        [Range(.5f, 5f)] public float heavySequenceMemory = 2.2f;
        [Range(1f, 1.4f)] public float playerAttackTempo = 1.12f;
        [Range(.3f, 1f)] public float responsiveRecoveryScale = .65f;
        [Range(.2f, 1f)] public float maximumActionBuffer = .75f;
        public int LastHeavyStep { get; private set; }
        public int NextHeavyStep => heavySequence.NextIndex + 1;
        readonly HeavyStrikeSequence heavySequence = new HeavyStrikeSequence();
        CombatActor previousLockedTarget;
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
        // Legacy serialized field; existing scenes may retain a value, but the skill has no cooldown.
        [HideInInspector] public float skillCooldown;
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
        public bool IsDialoguePoseActive => dialoguePoseLocked;
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
        public float SkillCooldownRemaining => 0f;
        public float HitRecoil => Mathf.Max(impact.Strength, actor != null && actor.IsStunned ? 1f : 0f);
        public bool CanAnimateJump => IsMotionAvailable(CombatMotion.JumpStart)
            && IsMotionAvailable(CombatMotion.JumpLoop) && IsMotionAvailable(CombatMotion.JumpLand);
        public string Status => actor != null && actor.IsDefeated ? "Defeated" : dialoguePoseLocked ? "Idle" : MovementLocked ? "Stagger"
            : State != ActionState.Ready ? State.ToString() : !IsGrounded ? "Jump" : IsGuarding ? "Guard"
            : IsRunning ? "Run" : "Idle";
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
        private float landingUntil, lastStaminaUseAt = -10f;
        private bool wasGrounded, wasGuarding, guardExhausted, groundedFromMovement;
        private bool intentionalJump;
        private float airborneTime, airbornePeak;
        private readonly RaycastHit[] groundHits = new RaycastHit[24];
        private bool dialoguePoseLocked;
        private BufferedAction queuedAction;
        private float queuedUntil;
        private ImpactSlide impact;
        private bool MovementLocked => dialoguePoseLocked || impact.Active || actor != null && (actor.IsDefeated || actor.IsStunned || actor.ControlLocked);

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
            RestoreRunningSwordPose();
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

        private Transform carryUpper, carryLower, carryHand, carryChest;
        private Vector3 carryChestReference;
        private Transform offhandUpper, offhandLower, offhandHand;
        private Animator carryAnimator;
        private Quaternion carryUpperBase, carryLowerBase, carryHandBase;
        private Quaternion offhandUpperBase, offhandLowerBase;
        private bool carryPoseApplied;
        private bool offhandPoseApplied;
        private float carryWeight;

        private void RestoreRunningSwordPose()
        {
            if (carryPoseApplied)
            {
                if (carryUpper != null) carryUpper.localRotation = carryUpperBase;
                if (carryLower != null) carryLower.localRotation = carryLowerBase;
                if (carryHand != null) carryHand.localRotation = carryHandBase;
                carryPoseApplied = false;
            }
            if (offhandPoseApplied)
            {
                if (offhandUpper != null) offhandUpper.localRotation = offhandUpperBase;
                if (offhandLower != null) offhandLower.localRotation = offhandLowerBase;
                offhandPoseApplied = false;
            }
        }

        // Preserve the source run's shoulder/elbow counter-motion. The armed
        // hand softly follows the torso rather than being pinned to the root.
        // The socket remains attached to the hand, and attacks use untouched clips.
        private void ApplyRunningSwordPose()
        {
            bool canCorrect = stabilizeRunningSword && !dialoguePoseLocked && !MovementLocked
                && State == ActionState.Ready && animationDriver != null && animationDriver.Ready
                && actor != null && !actor.IsAttacking && actor.WeaponRoot != null && actor.BladeBase != null && actor.BladeTip != null;
            bool running = canCorrect && animationDriver.CurrentMotion == CombatMotion.Run && IsRunning;
            // Do not modify combat, guard or dodge poses. Ordinary stopping and
            // jumping retain a short fade instead of dropping the correction.
            bool ordinaryExit = canCorrect && (animationDriver.CurrentMotion == CombatMotion.ExplorationIdle
                || animationDriver.CurrentMotion == CombatMotion.Idle || animationDriver.CurrentMotion == CombatMotion.JumpStart
                || animationDriver.CurrentMotion == CombatMotion.JumpLoop || animationDriver.CurrentMotion == CombatMotion.JumpLand);
            if (!running && !ordinaryExit) { carryWeight = 0f; return; }
            carryWeight = Mathf.MoveTowards(carryWeight, running ? 1f : 0f,
                Mathf.Min(Time.deltaTime / Mathf.Max(.1f, runningPoseBlendTime), .25f));
            if (carryWeight <= 0f) return;
            Animator animator = animationDriver.targetAnimator;
            if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid) return;
            if (carryAnimator != animator || carryUpper == null || carryLower == null || carryHand == null)
            {
                carryAnimator = animator;
                carryUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                carryLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                carryHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                offhandUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                offhandLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                offhandHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                carryChest = animator.GetBoneTransform(HumanBodyBones.UpperChest)
                    ?? animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
                carryChestReference = carryChest != null ? transform.InverseTransformPoint(carryChest.position) : Vector3.zero;
            }
            if (carryUpper == null || carryLower == null || carryHand == null || !actor.WeaponRoot.IsChildOf(carryHand)) return;
            float upperLength = Vector3.Distance(carryUpper.position, carryLower.position);
            float lowerLength = Vector3.Distance(carryLower.position, carryHand.position);
            if (upperLength < .01f || lowerLength < .01f) return;
            carryUpperBase = carryUpper.localRotation;
            carryLowerBase = carryLower.localRotation;
            carryHandBase = carryHand.localRotation;
            carryPoseApplied = true;
            Vector3 shoulder = carryUpper.position;
            Vector3 chestDelta = carryChest != null ? carryChest.position - transform.TransformPoint(carryChestReference) : Vector3.zero;
            Vector3 anchor = transform.TransformPoint(runningHandPosition) + chestDelta;
            Vector3 target = Vector3.Lerp(carryHand.position, anchor, .65f);
            Vector3 toward = target - shoulder;
            float distance = Mathf.Clamp(toward.magnitude, Mathf.Abs(upperLength - lowerLength) + .01f,
                upperLength + lowerLength - .005f);
            Vector3 axis = toward.normalized;
            target = shoulder + axis * distance;
            Vector3 bend = Vector3.ProjectOnPlane(carryLower.position - shoulder, axis).normalized;
            if (bend.sqrMagnitude < .0001f) bend = Vector3.ProjectOnPlane(transform.right, axis).normalized;
            float along = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) / (2f * distance);
            Vector3 elbow = shoulder + axis * along + bend * Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            carryUpper.rotation = Quaternion.FromToRotation(carryLower.position - shoulder, elbow - shoulder) * carryUpper.rotation;
            carryLower.rotation = Quaternion.FromToRotation(carryHand.position - carryLower.position, target - carryLower.position) * carryLower.rotation;
            float armBlend = Mathf.Clamp01(runningHandCorrectionWeight) * carryWeight;
            Quaternion softUpper = Quaternion.RotateTowards(carryUpperBase, carryUpper.localRotation, 35f);
            Quaternion softLower = Quaternion.RotateTowards(carryLowerBase, carryLower.localRotation, 45f);
            carryUpper.localRotation = Quaternion.Slerp(carryUpperBase, softUpper, armBlend);
            carryLower.localRotation = Quaternion.Slerp(carryLowerBase, softLower, armBlend);
            Vector3 blade = actor.BladeTip.position - actor.BladeBase.position;
            if (blade.sqrMagnitude > .0001f && runningBladeDirection.sqrMagnitude > .0001f)
                carryHand.rotation = Quaternion.FromToRotation(blade, transform.TransformDirection(runningBladeDirection)) * carryHand.rotation;
            carryHand.localRotation = Quaternion.Slerp(carryHandBase, carryHand.localRotation, .85f * carryWeight);
            ApplyRunningOffhandPose();
        }

        private void ApplyRunningOffhandPose()
        {
            // The free arm keeps most of the source counter-swing. A small
            // correction is enough to avoid the old wide raised-elbow posture.
            float blend = Mathf.Clamp(runningOffhandWeight, 0f, .22f) * carryWeight;
            if (blend <= 0f || offhandUpper == null || offhandLower == null || offhandHand == null) return;
            Vector3 shoulder = offhandUpper.position;
            float upperLength = Vector3.Distance(shoulder, offhandLower.position);
            float lowerLength = Vector3.Distance(offhandLower.position, offhandHand.position);
            if (upperLength < .01f || lowerLength < .01f) return;
            // Follow the actual run cycle, leaving some of its natural counter-swing.
            // This changes rotations only; bone lengths, torso and attack clips stay intact.
            Vector3 localTarget = runningOffhandPosition;
            localTarget.z += Mathf.Sin(animationDriver.NormalizedTime * Mathf.PI * 2f) * runningOffhandSway;
            Vector3 chestDelta = carryChest != null ? carryChest.position - transform.TransformPoint(carryChestReference) : Vector3.zero;
            Vector3 toward = Vector3.Lerp(offhandHand.position, transform.TransformPoint(localTarget) + chestDelta, .65f) - shoulder;
            if (toward.sqrMagnitude < .0001f) return;
            float distance = Mathf.Clamp(toward.magnitude, Mathf.Abs(upperLength - lowerLength) + .005f,
                upperLength + lowerLength - .005f);
            Vector3 axis = toward.normalized;
            Vector3 target = shoulder + axis * distance;
            Vector3 bend = Vector3.ProjectOnPlane(offhandLower.position - shoulder, axis).normalized;
            if (bend.sqrMagnitude < .0001f) bend = Vector3.ProjectOnPlane(-transform.right, axis).normalized;
            if (bend.sqrMagnitude < .0001f) return;
            float along = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) / (2f * distance);
            Vector3 elbow = shoulder + axis * along + bend * Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            offhandUpperBase = offhandUpper.localRotation;
            offhandLowerBase = offhandLower.localRotation;
            offhandPoseApplied = true;
            offhandUpper.rotation = Quaternion.FromToRotation(offhandLower.position - shoulder, elbow - shoulder) * offhandUpper.rotation;
            offhandLower.rotation = Quaternion.FromToRotation(offhandHand.position - offhandLower.position, target - offhandLower.position) * offhandLower.rotation;
            offhandUpper.localRotation = Quaternion.Slerp(offhandUpperBase, offhandUpper.localRotation, blend);
            offhandLower.localRotation = Quaternion.Slerp(offhandLowerBase, offhandLower.localRotation, blend);
        }

        private void LateUpdate()
        {
            ApplyRunningSwordPose();
            if (dialoguePoseLocked || State != ActionState.Dodge || animationDriver == null || animationDriver.HasMotion(CombatMotion.Dodge)
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

        private void OnDisable()
        {
            RestoreRunningSwordPose();
            RestoreLegacyDodgePose();
            SetDialoguePose(false);
        }
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
        private float RecoveryDelay(CombatMotion motion) => (motion == CombatMotion.Heavy ? heavyRecoveryDelay
            : motion == CombatMotion.Skill ? skillRecoveryDelay : lightRecoveryDelay) * responsiveRecoveryScale;

        private float AssistReach(CombatAttack attack)
        {
            float ordinary = Mathf.Min(targetAssistDistance, attack.range + .35f);
            return attack.motion == CombatMotion.Skill ? Mathf.Max(ordinary, 4.1f) : ordinary;
        }

        private void RefreshTarget(float range, bool advancing = false)
        {
            if (!enableTargetAssist || actor == null || actor.IsDefeated || actor.ControlLocked
                || encounter != null && !encounter.IsCombatActive)
            { selectedTarget = null; return; }
            Vector3 intent = GuardRequested ? transform.forward : CameraMovement();
            if (intent.sqrMagnitude < .01f) intent = transform.forward;
            var orbit = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
            var locked = orbit != null ? orbit.LockedTarget : null;
            if (locked != previousLockedTarget) { heavySequence.Reset(); previousLockedTarget = locked; }
            float reach = advancing ? Mathf.Max(4.1f, range + .35f) : Mathf.Min(targetAssistDistance, range + .35f);
            if (CombatTargeting.IsValid(actor, locked) && actor.HasLineOfSight(locked)
                && Vector3.Distance(transform.position, locked.transform.position) <= reach)
            { selectedTarget = locked; return; }
            selectedTarget = CombatTargeting.Select(actor, Camera.main, intent,
                reach, targetAssistHalfAngle, selectedTarget);
        }

        private void UpdateAttackFacing(float dt)
        {
            if (actor == null || !actor.IsAttacking) return;
            if (!enableTargetAssist || !CombatTargeting.IsValid(actor, attackTarget))
            { attackTarget = null; return; }
            Vector3 toward = Vector3.ProjectOnPlane(attackTarget.transform.position - transform.position, Vector3.up);
            var orbit = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
            bool explicitlyLocked = orbit != null && orbit.LockedTarget == attackTarget;
            if (toward.sqrMagnitude < .001f || toward.magnitude > AssistReach(actor.CurrentAttack)
                || !explicitlyLocked && Vector3.Angle(attackIntent, toward) > targetAssistHalfAngle || !actor.HasLineOfSight(attackTarget))
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
            if (dialoguePoseLocked) return false;
            if (queuedAction != BufferedAction.None && (Time.time > queuedUntil || !CanAfford(queuedAction))) queuedAction = BufferedAction.None;
            // A rejected, unaffordable defence must not reserve the input slot
            // and swallow a subsequent free attack.
            if (!CanAfford(requested)) return false;
            if (queuedAction == BufferedAction.Dodge && requested != BufferedAction.Dodge) return false;
            if (TryExecute(requested)) { queuedAction = BufferedAction.None; return true; }
            bool available = requested == BufferedAction.Jump ? CanAnimateJump : IsMotionAvailable(MotionFor(requested));
            if (CanBufferInput() && available)
            {
                // Defence takes priority over a subsequently mashed attack.
                if (queuedAction == BufferedAction.Dodge && requested != BufferedAction.Dodge) return false;
                queuedAction = requested;
                float retain = inputBufferTime;
                if (actor != null && actor.IsAttacking)
                {
                    float eligible = actor.LastHitTime + RecoveryDelay(actor.CurrentAttack.motion);
                    if (requested == BufferedAction.Dodge && allowHeavyDefensiveExit && actor.CurrentAttack.hitWindows != null)
                        foreach (var window in actor.CurrentAttack.hitWindows)
                            if (window.end + betweenStrikeRecovery > actor.AttackElapsed)
                            { eligible = Mathf.Min(eligible, window.end + betweenStrikeRecovery); break; }
                    float wait = Mathf.Max(0f, eligible - actor.AttackElapsed);
                    if (wait <= maximumActionBuffer) retain = Mathf.Max(retain, wait + inputBufferTime);
                }
                queuedUntil = Time.time + Mathf.Min(maximumActionBuffer, retain);
            }
            return false;
        }
        private bool CanBufferInput()
        {
            if (dialoguePoseLocked || actor != null && (actor.IsDefeated || actor.ControlLocked)) return false;
            // Only the final short part of hitstun accepts an early input. A
            // long stun must never release a stale attack several seconds later.
            return !MovementLocked || !impact.Active && actor != null && actor.IsStunned
                && actor.StunRemaining <= inputBufferTime;
        }
        private bool CanAfford(BufferedAction action) => Stamina >= (action == BufferedAction.Dodge ? dodgeStaminaCost
            : action == BufferedAction.Jump ? jumpStaminaCost : action == BufferedAction.Skill ? skillStaminaCost : 0f);
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
            if (dialoguePoseLocked) return false;
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
                        && (requested != BufferedAction.Skill || Stamina >= skillStaminaCost)))
                ExitAttackRecovery();
            if (MovementLocked || State != ActionState.Ready || Time.time < landingUntil) return false;
            if (requested == BufferedAction.Jump)
            {
                if (!CanAnimateJump || IsGuarding || verticalVelocity > .01f
                    || (!IsGrounded && Time.time - lastGroundedAt > coyoteTime) || !SpendStamina(jumpStaminaCost)) return false;
                verticalVelocity = Mathf.Sqrt(Mathf.Max(0f, -2f * gravity * jumpHeight));
                jumpedAt = Time.time;
                intentionalJump = true;
                airborneTime = 0f;
                airbornePeak = transform.position.y;
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
            if (requested == BufferedAction.Skill && Stamina < skillStaminaCost) return false;
            CombatAttack sourceAttack = requested == BufferedAction.Heavy ? heavyAttack
                : requested == BufferedAction.Skill ? skillAttack : lightAttack;
            if (sourceAttack == null) return false;
            RefreshTarget(sourceAttack.range, requested == BufferedAction.Skill);
            bool sequential = useSequentialHeavy && requested == BufferedAction.Heavy
                && heavyAttack.hitWindows != null && heavyAttack.hitWindows.Length > 1;
            int heavyStep = sequential ? heavySequence.Select(selectedTarget) : -1;
            CombatAttack attack = HeavyStrikeSequence.Prepare(sourceAttack,
                animationDriver.MotionLength(motion, sourceAttack.duration * sourceAttack.speed), playerAttackTempo, heavyStep);
            attack.motion = motion;
            // Explicit camera lock should also align the start of the attack.
            // Only turn at launch; never steer a damaging sweep through new targets.
            var cameraOrbit = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
            if (selectedTarget != null && cameraOrbit != null && cameraOrbit.LockedTarget == selectedTarget)
            {
                Vector3 toward = Vector3.ProjectOnPlane(selectedTarget.transform.position - transform.position, Vector3.up);
                if (toward.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(toward);
            }
            if (!actor.TryAttack(attack)) return false;
            heavySequence.ObserveTarget(selectedTarget);
            if (sequential)
            {
                LastHeavyStep = heavyStep + 1;
                heavySequence.Accepted(heavyStep, heavyAttack.hitWindows.Length, selectedTarget, attack.duration, heavySequenceMemory);
            }
            else if (!useSequentialHeavy) { LastHeavyStep = 0; heavySequence.Reset(); }
            // Failed requests and buffered inputs cost nothing. Pay exactly once
            // only after the actual skill starts, using the shared recovery delay.
            if (requested == BufferedAction.Skill) SpendStamina(skillStaminaCost);
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
            RestoreRunningSwordPose();
            RestoreLegacyDodgePose();
            if (dialoguePoseLocked)
            {
                animationDriver?.SetDialoguePose(true);
                return;
            }
            float dt = Time.deltaTime;
            if (dt <= 0f || controller == null || !controller.enabled) return;
            if (actor == null || animationDriver == null) RefreshBindings();
            var orbitNow = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
            var lockNow = orbitNow != null ? orbitNow.LockedTarget : null;
            if (lockNow != previousLockedTarget) { heavySequence.Reset(); previousLockedTarget = lockNow; }
            if (useSequentialHeavy && actor != null && actor.IsAttacking
                && (actor.CurrentAttack.motion == CombatMotion.Light || actor.CurrentAttack.motion == CombatMotion.Heavy))
                heavySequence.Sustain(heavySequenceMemory);
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
                if (Time.time > queuedUntil || !CanAfford(queuedAction)) queuedAction = BufferedAction.None;
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
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(movement),
                    Mathf.Min(turnSpeed * dt, maximumLocomotionTurnStep));
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
            float beforeGravity = verticalVelocity;
            verticalVelocity += gravity * dt;
            // Integrate the whole interval, including the upward half of a slow
            // frame. End-velocity-only integration could erase an entire jump
            // when gravity crossed zero in the same frame as the Space input.
            float verticalStep = (beforeGravity + verticalVelocity) * .5f * dt;
            Vector3 beforeMove = transform.position;
            CollisionFlags flags = MoveWithStepFallback(displacement + Vector3.up * verticalStep + impact.Advance(dt));
            // A wall may reject movement while the input velocity remains high.
            // Only count progress along the requested path, excluding floor snap.
            float travel = Mathf.Clamp(Vector3.Dot(transform.position - beforeMove, planarVelocity.normalized),
                0f, planarVelocity.magnitude * dt);
            float travelSpeed = travel / dt;
            if ((flags & CollisionFlags.Below) == 0 && !intentionalJump && verticalVelocity <= 0f
                && (wasGrounded || Time.time - lastGroundedAt <= coyoteTime
                    || airborneTime < minimumFallTime + .08f && airbornePeak - transform.position.y < minimumLandingDrop))
                flags |= FollowNearbyGround();
            groundedFromMovement = (flags & CollisionFlags.Below) != 0;
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
            bool groundedNow = IsGrounded;
            bool substantialLanding = intentionalJump || airborneTime >= minimumFallTime
                && airbornePeak - transform.position.y >= minimumLandingDrop;
            if (groundedNow && !wasGrounded && substantialLanding && Time.time - jumpedAt > .05f
                && !MovementLocked && State == ActionState.Ready && (actor == null || !actor.IsAttacking))
            {
                float contact = useLightLanding ? Mathf.Min(landingBlendTime, lightLandingClipSeconds) : landingBlendTime;
                landingUntil = Time.time + contact;
                if (IsMotionAvailable(CombatMotion.JumpLand))
                {
                    if (useLightLanding)
                        animationDriver.RestartRange(CombatMotion.JumpLand, 0f,
                            Mathf.Min(lightLandingClipSeconds, animationDriver.MotionLength(CombatMotion.JumpLand)), .06f, 1f);
                    else animationDriver.Play(CombatMotion.JumpLand, .07f);
                }
            }
            if (groundedNow)
            {
                intentionalJump = false;
                airborneTime = 0f;
                airbornePeak = transform.position.y;
            }
            else
            {
                airborneTime += dt;
                airbornePeak = Mathf.Max(airbornePeak, transform.position.y);
            }
            wasGrounded = groundedNow;
            if (finishingDodge) State = ActionState.Ready;
            IsRunning = canMove && groundedNow && !guarding && animationDriver != null && animationDriver.HasTravelSpeed(travelSpeed);
            UpdateAnimation(groundedNow, guarding, travelSpeed);
            if (transform.position.y < -8f) ResetPose();
        }
        private CollisionFlags FollowNearbyGround()
        {
            if (groundSnapDistance <= 0f) return CollisionFlags.None;
            float lift = controller.skinWidth + .06f;
            float radius = Mathf.Max(.05f, controller.radius * .68f);
            float nearest = float.PositiveInfinity;
            float minimumNormal = Mathf.Cos(Mathf.Min(80f, controller.slopeLimit) * Mathf.Deg2Rad);
            // Probe the support under the capsule's feet, not one point at its
            // center. At a tread edge the center ray can miss a still-supported
            // foot and falsely enter the airborne pose.
            int count = Physics.SphereCastNonAlloc(transform.position + Vector3.up * (lift + radius), radius, Vector3.down,
                groundHits, lift + groundSnapDistance, ~0, QueryTriggerInteraction.Ignore);
            // A full buffer cannot establish the nearest safe support.
            if (count == groundHits.Length) return CollisionFlags.None;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i];
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<CombatActor>() != null
                    || hit.normal.y < minimumNormal) continue;
                nearest = Mathf.Min(nearest, hit.distance);
            }
            if (float.IsPositiveInfinity(nearest)) return CollisionFlags.None;
            float down = Mathf.Clamp(nearest - lift + controller.skinWidth, .01f, groundSnapDistance);
            return controller.Move(Vector3.down * down);
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
        private void UpdateAnimation(bool grounded, bool guarding, float travelSpeed)
        {
            if (animationDriver == null || !animationDriver.Ready || MovementLocked
                || State != ActionState.Ready || actor != null && actor.IsAttacking) return;
            bool substantialFall = airborneTime >= minimumFallTime && airbornePeak - transform.position.y >= minimumLandingDrop;
            if (!grounded && (intentionalJump || substantialFall))
            {
                float fraction = animationDriver.motionSet != null ? animationDriver.motionSet.jumpStartToLoopNormalized : .65f;
                float startup = Mathf.Clamp(animationDriver.MotionLength(CombatMotion.JumpStart, .25f) * fraction, .08f, .22f);
                float takeoffSpeed = Mathf.Sqrt(Mathf.Max(0f, -2f * gravity * jumpHeight));
                if (!intentionalJump || Time.time - jumpedAt >= startup && verticalVelocity <= takeoffSpeed * .7f)
                    if (IsMotionAvailable(CombatMotion.JumpLoop)) animationDriver.Play(CombatMotion.JumpLoop, .1f);
                return;
            }
            if (Time.time < landingUntil) return;
            if (guarding) animationDriver.Play(CombatMotion.Guard, .1f);
            else
            {
                // At rest, finish the real block clip after releasing Q. Walking,
                // jumping or starting an attack may interrupt this recovery.
                if (travelSpeed < .1f && animationDriver.ReleaseGuard()) return;
                if (animationDriver.HasTravelSpeed(travelSpeed) && IsMotionAvailable(CombatMotion.Run))
                    animationDriver.Play(CombatMotion.Run, animationDriver.motionSet.locomotionFade, CalculateRunPlaybackRate(travelSpeed));
                else animationDriver.SetLocomotion(0f, encounter != null && encounter.IsCombatActive);
            }
        }

        public float CalculateRunPlaybackRate(float travelSpeed)
        {
            float move = EffectiveMoveSpeed;
            float reference = animationDriver != null && animationDriver.motionSet != null
                ? animationDriver.motionSet.runReferenceSpeed : move;
            // Keep the imported sprint's cadence at full speed and ease it at lower speeds.
            float fullRate = Mathf.Max(move / Mathf.Max(.1f, reference), fullSpeedRunPlayback);
            return Mathf.Clamp(travelSpeed / Mathf.Max(.1f, move) * fullRate, .35f, 1.5f);
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
            if (actor != null && actor.LastBlockWasPerfect) multiplier *= .35f;
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
            heavySequence.Reset();
            MoveInput = Vector2.zero;
            RunRequested = GuardRequested = CrouchRequested = false;
            queuedAction = BufferedAction.None;
            selectedTarget = attackTarget = guardTarget = null;
            if (actor != null) actor.Guarding = false;
        }
        public void ResetPose()
        {
            SetDialoguePose(false);
            PlaceForStory(spawnPosition, spawnRotation);
            Stamina = maxStamina;
            lastStaminaUseAt = -10f;
            guardExhausted = false;
            LightAttackCount = HeavyAttackCount = JumpCount = DodgeCount = SkillCount = GuardCount = 0;
        }
        public void PlaceForStory(Vector3 position, Quaternion rotation)
        {
            RestoreRunningSwordPose();
            RestoreLegacyDodgePose();
            carryWeight = 0f;
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
            intentionalJump = false;
            airborneTime = 0f;
            airbornePeak = position.y;
            wasGrounded = wasGuarding = groundedFromMovement = false;
            State = ActionState.Ready;
            ClearInput();
            if (actor != null) { actor.CancelAttack(); actor.Invulnerable = false; }
            if (animationDriver != null) animationDriver.ClearImpactPause();
            if (dialoguePoseLocked)
            {
                SettleDialogueFeet();
                animationDriver?.SetDialoguePose(true);
                return;
            }
            CombatMotion rest = encounter != null && encounter.IsCombatActive ? CombatMotion.Idle : CombatMotion.ExplorationIdle;
            if (animationDriver != null && animationDriver.Ready && animationDriver.HasMotion(rest))
                animationDriver.Play(rest, .1f);
        }

        // Dialogue owns presentation until the UI returns control. Clearing a
        // one-frame input and pausing time alone cannot guarantee this ordering.
        public void SetDialoguePose(bool active)
        {
            if (dialoguePoseLocked == active) return;
            dialoguePoseLocked = active;
            if (!active)
            {
                animationDriver?.SetDialoguePose(false);
                ClearInput();
                return;
            }
            RefreshBindings();
            RestoreRunningSwordPose();
            RestoreLegacyDodgePose();
            ClearInput();
            planarVelocity = Vector3.zero;
            verticalVelocity = 0f;
            impact = default;
            actionTime = landingUntil = 0f;
            jumpedAt = -10f;
            intentionalJump = false;
            airborneTime = 0f;
            airbornePeak = transform.position.y;
            wasGuarding = IsRunning = false;
            State = ActionState.Ready;
            if (actor != null) { actor.CancelAttack(); actor.Invulnerable = false; }
            SettleDialogueFeet();
            animationDriver?.SetDialoguePose(true);
        }

        private void SettleDialogueFeet()
        {
            if (controller == null) controller = GetComponent<CharacterController>();
            if (controller == null || !controller.enabled) return;
            // Keep X/Z and facing. Find a walkable floor within one jump below,
            // then let the controller resolve the downward movement against it.
            // Ignore actors so a nearby opponent is never mistaken for the floor.
            float lift = controller.skinWidth + .05f;
            float nearest = float.PositiveInfinity;
            float reach = Mathf.Max(.3f, jumpHeight + controller.stepOffset + .5f) + lift;
            float minimumNormal = Mathf.Cos(Mathf.Min(80f, controller.slopeLimit) * Mathf.Deg2Rad);
            foreach (RaycastHit hit in Physics.RaycastAll(transform.position + Vector3.up * lift, Vector3.down,
                reach, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<CombatActor>() != null
                    || hit.normal.y < minimumNormal) continue;
                nearest = Mathf.Min(nearest, hit.distance);
            }
            if (!float.IsPositiveInfinity(nearest))
            {
                float down = Mathf.Max(.02f, nearest - lift + controller.skinWidth + .02f);
                CollisionFlags contact = controller.Move(Vector3.down * down);
                groundedFromMovement = (contact & CollisionFlags.Below) != 0;
            }
            else groundedFromMovement = controller.isGrounded;
            wasGrounded = groundedFromMovement;
            landingUntil = 0f;
            verticalVelocity = groundedFromMovement ? -2f : 0f;
            lastGroundedAt = groundedFromMovement ? Time.time : -10f;
        }
    }
}
