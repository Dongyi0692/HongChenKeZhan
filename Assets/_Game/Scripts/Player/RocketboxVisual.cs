using System;
using UnityEngine;

namespace FengChenInn
{
    // Drives the skinned placeholder while the existing controller owns motion and attack timing.
    public sealed class RocketboxVisual : MonoBehaviour
    {
        [Serializable]
        public sealed class Limb
        {
            public Transform upper, lower, end;
            private Quaternion upperBind, lowerBind, endBind, endFacing;

            public void Initialize(Transform frame)
            {
                upperBind = upper.localRotation;
                lowerBind = lower.localRotation;
                endBind = end.localRotation;
                endFacing = Quaternion.Inverse(frame.rotation) * end.rotation;
            }

            public void Reset()
            {
                upper.localRotation = upperBind;
                lower.localRotation = lowerBind;
                end.localRotation = endBind;
            }

            public void Reach(Vector3 goal, Vector3 bend)
            {
                Vector3 origin = upper.position;
                float upperLength = Vector3.Distance(origin, lower.position);
                float lowerLength = Vector3.Distance(lower.position, end.position);
                Vector3 offset = goal - origin;
                if (offset.sqrMagnitude < 0.000001f) return;
                Vector3 direction = offset.normalized;
                float reach = Mathf.Clamp(offset.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.001f,
                    upperLength + lowerLength - 0.001f);
                float projected = (upperLength * upperLength - lowerLength * lowerLength + reach * reach) / (2f * reach);
                float bendHeight = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - projected * projected));
                Vector3 pole = Vector3.ProjectOnPlane(bend, direction).normalized;
                Vector3 elbow = origin + direction * projected + pole * bendHeight;
                Aim(upper, lower, elbow);
                Aim(lower, end, origin + direction * reach);
            }

            public void LevelEnd(Transform frame) => end.rotation = frame.rotation * endFacing;

            private static void Aim(Transform joint, Transform child, Vector3 point)
            {
                joint.rotation = Quaternion.FromToRotation(child.position - joint.position, point - joint.position) * joint.rotation;
            }
        }

        public Transform pelvis, chest, weapon;
        public bool keepSwordDrawn;
        public bool StorySheathed { get; set; }
        public enum StoryPose { Standing, Seated, Kneeling }
        [Header("Imported sword dimensions (metres from grip)")]
        public bool useCustomBladeGeometry;
        public float customBladeStart = 0.18f, customBladeEnd = 1f, customBladeRadius = 0.055f;
        public float sheathBladeLength = 0.95f, sheathWidth = 0.105f;
        public Material sheathMaterial, sheathTrimMaterial;
        public Limb leftArm = new Limb(), rightArm = new Limb(), leftLeg = new Limb(), rightLeg = new Limb();
        public AnimationClip lightSwordMotion, heavySwordMotion, swordGuardMotion;
        public AnimationClip[] walkMotions;
        public AnimationClip ActiveSwordMotion { get; private set; }
        // Sword carry: idle characters keep the blade in a hip scabbard; movement,
        // combat states or a story call to DrawSword() pull it into the right hand.
        public bool IsSwordHeld => drawWeight > 0.95f;
        public bool IsSwordSheathed => drawWeight < 0.05f;
        public float DrawWeight => drawWeight;
        public Transform Scabbard { get; private set; }
        public Vector3 SheathPivotWorld => pelvis.TransformPoint(sheathLocalPivot);
        public Vector3 SheathGripWorld => SheathPivotWorld + (pelvis.rotation * sheathLocalRot) * (Vector3.forward * 0.09f);
        public void DrawSword() => holdOverride = true;
        public void SheathSword() => holdOverride = false;
        private Vector3 sheathLocalPivot;
        private Quaternion sheathLocalRot;
        private Transform swordOwner;
        private CharacterController swordOwnerCollider;
        private readonly Collider[] bladeOverlaps = new Collider[16];
        private Vector3 lastBlade, lastGrip;
        private bool hasBladePose;
        private float bladeStart = 0.18f, bladeEnd = 1.00f, bladeRadius = 0.055f;
        public Collider BladeBlocker { get; private set; }
        public CharacterController BladeContact => BladeBlocker != swordOwnerCollider ? BladeBlocker as CharacterController : null;

        public void ResetSwordSweep()
        {
            hasBladePose = false;
            BladeBlocker = null;
        }
        private float drawWeight, sheathIdleTime, airborneTime;
        private bool holdOverride;
        private Vector3 pelvisBind, leftFoot, rightFoot, rightHandGoal, leftHandGoal, bladeDirection;
        private Quaternion chestBind, handFromGrip;
        private Vector3 gripOffsetInHand;
        private Transform middleKnuckle, indexKnuckle, pinkyKnuckle;
        private float lowerAmount, torsoPitch, torsoTwist, leftArmTuck;
        private bool wasAirborne;
        private float landingTime = 1f;
        private bool initialized;
        private float walkWeight;
        private Transform[] walkBones;
        private Quaternion[] walkBase;

        public Transform[] GetSwordMotionBones() => new[]
        {
            chest, leftArm.upper, leftArm.lower, rightArm.upper, rightArm.lower, weapon
        };

        public Transform[] GetWalkBones() => new[] { leftLeg.upper, leftLeg.lower, leftLeg.end, rightLeg.upper, rightLeg.lower, rightLeg.end };

        private void Awake()
        {
            pelvisBind = pelvis.localPosition;
            chestBind = chest.localRotation;
            leftArm.Initialize(transform);
            rightArm.Initialize(transform);
            leftLeg.Initialize(transform);
            rightLeg.Initialize(transform);
            leftFoot = transform.InverseTransformPoint(leftLeg.end.position);
            rightFoot = transform.InverseTransformPoint(rightLeg.end.position);
            rightHandGoal = new Vector3(0.29f, 0.95f, 0.16f);
            leftHandGoal = new Vector3(-0.28f, 0.95f, 0.12f);
            bladeDirection = Vector3.forward;
            var controller = GetComponentInParent<CharacterController>();
            swordOwnerCollider = controller;
            swordOwner = controller != null ? controller.transform : transform;
            Transform bladeMesh = weapon.Find("Blade");
            if (bladeMesh != null)
            {
                // Distances measured from the centre of the grip, with rounded blade edges.
                bladeRadius = Mathf.Max(bladeMesh.localScale.x, bladeMesh.localScale.y) * 0.5f + 0.012f;
                float length = bladeMesh.localScale.z;
                bladeStart = bladeMesh.localPosition.z - 0.09f - length * 0.5f + bladeRadius;
                bladeEnd = bladeMesh.localPosition.z - 0.09f + length * 0.5f - bladeRadius;
            }
            if (useCustomBladeGeometry)
            {
                bladeStart = Mathf.Max(0f, customBladeStart);
                bladeEnd = Mathf.Max(bladeStart + 0.01f, customBladeEnd);
                bladeRadius = Mathf.Max(0.005f, customBladeRadius);
            }
            InitializeGrip();
            InitializeSheath();
            if (keepSwordDrawn) { drawWeight = 1f; Scabbard.gameObject.SetActive(false); }
            walkBones = GetWalkBones();
            walkBase = new Quaternion[walkBones.Length];
            initialized = true;
        }

        private void InitializeGrip()
        {
            Transform hand = rightArm.end;
            Transform index = hand.Find("Bip01 R Finger1");
            indexKnuckle = index;
            middleKnuckle = hand.Find("Bip01 R Finger2");
            Transform pinky = hand.Find("Bip01 R Finger4");
            pinkyKnuckle = pinky;
            if (index == null || middleKnuckle == null || pinky == null)
            {
                middleKnuckle = null;
                return;
            }

            Vector3 knuckleCenter = (index.position + middleKnuckle.position + pinky.position) / 3f;
            gripOffsetInHand = hand.InverseTransformPoint(Vector3.Lerp(hand.position, knuckleCenter, 0.65f));
            Vector3 acrossPalm = (index.position - pinky.position).normalized;
            Vector3 fingers = middleKnuckle.position - hand.position;
            handFromGrip = Quaternion.Inverse(Quaternion.LookRotation(acrossPalm, fingers)) * hand.rotation;
            for (int digit = 1; digit <= 4; digit++)
            {
                Transform segment = hand.Find("Bip01 R Finger" + digit);
                for (int joint = 0; joint < 3 && segment != null; joint++)
                {
                    float bend = joint == 0 ? 46f : joint == 1 ? 72f : 43f;
                    segment.rotation = Quaternion.AngleAxis(bend, -acrossPalm) * segment.rotation;
                    segment = segment.childCount > 0 ? segment.GetChild(0) : null;
                }
            }
        }

        private void InitializeSheath()
        {
            // Keep the throat at the left hip and the sheath alongside the thigh.
            Vector3 gripWorld = transform.TransformPoint(new Vector3(-0.222f, 1.12f, 0.035f));
            Vector3 bladeWorld = transform.TransformDirection(new Vector3(0f, -0.99f, -0.12f).normalized);
            Vector3 upWorld = transform.forward;
            Quaternion sheathWorld = Quaternion.LookRotation(bladeWorld, upWorld);
            sheathLocalPivot = pelvis.InverseTransformPoint(gripWorld - bladeWorld * 0.09f);
            sheathLocalRot = Quaternion.Inverse(pelvis.rotation) * sheathWorld;
            Scabbard = new GameObject("Scabbard").transform;
            Scabbard.SetParent(pelvis, false);
            Scabbard.localPosition = sheathLocalPivot;
            Scabbard.localRotation = sheathLocalRot;
            Material wood = sheathMaterial, brass = sheathTrimMaterial;
            foreach (Transform part in weapon)
            {
                Renderer partRenderer = part.GetComponent<Renderer>();
                if (partRenderer == null) continue;
                if (wood == null && part.name.Contains("Grip")) wood = partRenderer.sharedMaterial;
                if (brass == null && part.name.Contains("Guard")) brass = partRenderer.sharedMaterial;
            }
            float sheathStart = useCustomBladeGeometry ? 0.09f + customBladeStart - 0.035f : 0.18f;
            ScabbardPart("Sheath", new Vector3(0f, 0f, sheathStart + sheathBladeLength * 0.5f), new Vector3(sheathWidth, 0.05f, sheathBladeLength), wood);
            ScabbardPart("Throat", new Vector3(0f, 0f, sheathStart + 0.005f), new Vector3(sheathWidth + 0.02f, 0.065f, 0.07f), brass);
            ScabbardPart("Chape", new Vector3(0f, 0f, sheathStart + sheathBladeLength - 0.01f), new Vector3(sheathWidth + 0.01f, 0.06f, 0.06f), brass);
            weapon.SetPositionAndRotation(SheathPivotWorld, pelvis.rotation * sheathLocalRot);
        }

        private void ScabbardPart(string partName, Vector3 center, Vector3 size, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            UnityEngine.Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(Scabbard, false);
            part.transform.localPosition = center;
            part.transform.localScale = size;
            if (material != null) part.GetComponent<Renderer>().sharedMaterial = material;
        }

        public void Apply(PrototypeFighter fighter, Vector3 worldVelocity, float actionTime, float gait, float dt)
        {
            if (!initialized) return;
            Vector3 horizontal = Vector3.ProjectOnPlane(worldVelocity, transform.up);
            float pace = Mathf.Clamp01(horizontal.magnitude / 4.2f);
            bool airborne = !fighter.IsGrounded && (wasAirborne || Mathf.Abs(worldVelocity.y) > 0.25f);
            if (wasAirborne && !airborne) landingTime = 0f;
            landingTime += dt;
            wasAirborne = airborne;

            float lower = 0.025f, pitch = 3f, twist = 0f;
            Vector3 tuckedHand = new Vector3(-0.22f, 1.08f, 0.23f);
            Vector3 leftHand = new Vector3(-0.28f, 1.02f, 0.06f);
            Vector3 rightHand = new Vector3(0.28f, 0.98f, 0.16f);
            Vector3 blade = Vector3.forward;
            Vector3 stepL = Vector3.zero, stepR = Vector3.zero;
            if (pace > 0.04f)
            {
                pitch = 11f * pace;
                leftHand = new Vector3(-0.19f, 0.93f, 0.13f + Mathf.Sin(gait) * 0.09f * pace);
                rightHand = new Vector3(0.20f, 0.99f, 0.24f - Mathf.Sin(gait) * 0.025f * pace);
                stepL = Step(Vector3.forward, gait, pace);
                stepR = Step(Vector3.forward, gait + Mathf.PI, pace);
            }

            if (fighter.IsCrouching)
            {
                lower = 0.46f;
                pitch = 27f;
                leftHand = new Vector3(-0.32f, 0.68f, 0.22f);
                rightHand = new Vector3(0.27f, 0.70f, 0.22f);
                stepL = Step(Vector3.forward, gait * 0.8f, pace * 0.55f);
                stepR = Step(Vector3.forward, gait * 0.8f + Mathf.PI, pace * 0.55f);
            }
            else if (airborne)
            {
                float falling = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, -3.5f, worldVelocity.y));
                lower = 0.08f;
                pitch = Mathf.Lerp(9f, 19f, falling);
                stepL = Vector3.Lerp(new Vector3(0f, 0.28f, 0.20f), new Vector3(0f, 0.17f, 0.12f), falling);
                stepR = Vector3.Lerp(new Vector3(0f, 0.17f, -0.18f), new Vector3(0f, 0.29f, -0.10f), falling);
                leftHand = Vector3.Lerp(new Vector3(-0.40f, 1.30f, 0.15f), new Vector3(-0.40f, 1.09f, 0.30f), falling);
                rightHand = Vector3.Lerp(new Vector3(0.37f, 1.25f, 0.08f), new Vector3(0.34f, 1.08f, 0.22f), falling);
                blade = new Vector3(0.8f, 0.35f, 0.5f);
            }
            else if (landingTime < 0.24f && fighter.State == PrototypeFighter.ActionState.Ready)
            {
                float settle = Mathf.Sin(Mathf.Clamp01(landingTime / 0.24f) * Mathf.PI);
                lower += 0.15f * settle;
                pitch += 20f * settle;
            }

            if (fighter.IsGuarding)
            {
                lower = 0.10f; pitch = 9f; twist = -12f;
                leftHand = new Vector3(-0.12f, 1.13f, 0.30f);
                rightHand = new Vector3(0.12f, 1.27f, 0.38f);
                blade = new Vector3(-0.72f, 0.85f, 0.12f);
                stepL += new Vector3(0f, 0f, 0.08f); stepR += new Vector3(0f, 0f, -0.08f);
            }
            if (fighter.State == PrototypeFighter.ActionState.LightAttack)
            {
                float wind = Ease(actionTime, 0f, 0.17f);
                float strike = Ease(actionTime, 0.18f, 0.34f);
                float recover = Ease(actionTime, 0.39f, 0.64f);
                Vector3 ready = new Vector3(0.28f, 0.98f, 0.16f);
                Vector3 windup = new Vector3(-0.22f, 1.24f, 0.37f);
                Vector3 follow = new Vector3(0.48f, 1.10f, 0.28f);
                rightHand = Vector3.Lerp(Vector3.Lerp(Vector3.Lerp(ready, windup, wind), follow, strike), ready, recover);
                // Arc forward from the left side of the chest before following through on the right.
                rightHand += Vector3.forward * (0.16f * Mathf.Sin(strike * Mathf.PI));
                float angle = Mathf.Lerp(-105f, 72f, strike);
                Vector3 cut = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0.08f, 1f);
                Vector3 carry = Vector3.forward;
                blade = Vector3.Slerp(Vector3.Slerp(carry, cut, wind), carry, recover);
                twist = Mathf.Lerp(Mathf.Lerp(0f, -23f, wind), 30f, strike) * (1f - recover);
                pitch = 8f + 8f * strike * (1f - recover);
                lower = 0.04f + 0.09f * strike * (1f - recover);
                stepL = new Vector3(0f, 0f, 0.15f * strike * (1f - recover));
                stepR = new Vector3(0f, 0f, -0.10f * strike * (1f - recover));
            }
            if (fighter.State == PrototypeFighter.ActionState.HeavyAttack)
            {
                float wind = Ease(actionTime, 0f, 0.42f);
                float strike = Ease(actionTime, 0.43f, 0.68f);
                float recover = Ease(actionTime, 0.72f, 1.18f);
                Vector3 ready = new Vector3(0.28f, 0.98f, 0.16f);
                Vector3 high = new Vector3(0.24f, 1.76f, -0.09f);
                Vector3 follow = new Vector3(0.04f, 0.77f, 0.49f);
                rightHand = Vector3.Lerp(Vector3.Lerp(Vector3.Lerp(ready, high, wind), follow, strike), ready, recover);
                // Bow the chop outward so the blade arcs in front of the body
                // instead of cutting across the face plane.
                rightHand += Vector3.forward * (0.18f * Mathf.Sin(strike * Mathf.PI));
                blade = Vector3.Slerp(Vector3.forward, new Vector3(0.12f, 0.75f, -0.66f), wind);
                blade = Vector3.Slerp(blade, new Vector3(0.04f, -0.5f, 0.9f), strike);
                blade = Vector3.Slerp(blade, Vector3.forward, recover);
                pitch = Mathf.Lerp(-12f * wind, 30f, strike) * (1f - recover);
                lower = Mathf.Lerp(0.03f, 0.20f, strike) * (1f - recover);
                stepL = new Vector3(0f, 0f, 0.22f * strike * (1f - recover));
                stepR = new Vector3(0f, 0f, -0.13f * strike * (1f - recover));
            }
            if (fighter.State == PrototypeFighter.ActionState.Dodge)
            {
                float dive = Ease(actionTime, 0f, 0.10f);
                float recover = Ease(actionTime, 0.27f, 0.48f);
                float weight = dive * (1f - recover);
                lower = Mathf.Lerp(0.025f, 0.34f, weight);
                pitch = Mathf.Lerp(3f, 39f, weight);
                tuckedHand += new Vector3(0f, 0.03f, 0.04f) * weight;
                rightHand = Vector3.Lerp(new Vector3(0.28f, 0.98f, 0.16f), new Vector3(0.38f, 0.86f, -0.05f), weight);
                blade = Vector3.Lerp(Vector3.forward, new Vector3(0.90f, 0.10f, -0.4f), weight);
                Vector3 localDodge = transform.InverseTransformDirection(horizontal).normalized;
                stepL = Step(localDodge, actionTime * 18f, weight * 0.65f);
                stepR = Step(localDodge, actionTime * 18f + Mathf.PI, weight * 0.65f);
            }

            if (fighter.HitRecoil > 0f)
            {
                float recoil = Mathf.Sin(fighter.HitRecoil * Mathf.PI * 0.65f);
                pitch -= 20f * recoil;
                lower += 0.08f * recoil;
                rightHand += new Vector3(0.08f, 0.02f, -0.16f) * recoil;
            }

            // Carry state: the sword only stays sheathed after a short idle delay;
            // any movement, combat state or DrawSword() call pulls it into the hand.
            // Ground contact needs a grace window: CharacterController.isGrounded
            // flaps for single frames at high frame rates (small dt vs skinWidth),
            // which must not reset the sheath timer or flick the sword out.
            airborneTime = fighter.IsGrounded ? 0f : airborneTime + dt;
            bool airborneBusy = airborneTime > 0.12f;
            bool busy = fighter.State != PrototypeFighter.ActionState.Ready || fighter.IsGuarding
                || fighter.IsCrouching || airborneBusy || holdOverride;
            bool moving = fighter.MoveInput.sqrMagnitude > 0.01f
                && fighter.State == PrototypeFighter.ActionState.Ready;
            bool resting = !busy && !moving;
            sheathIdleTime = resting ? sheathIdleTime + dt : 0f;
            bool sheatheNow = resting && sheathIdleTime > 0.7f;
            bool attacking = fighter.State == PrototypeFighter.ActionState.LightAttack
                || fighter.State == PrototypeFighter.ActionState.HeavyAttack;
            // Resting keeps the current carry pose until the idle delay expires.
            float carryTarget = drawWeight;
            if (sheatheNow || StorySheathed) carryTarget = 0f;
            else if (!resting) carryTarget = 1f;
            float carryRate = sheatheNow || StorySheathed ? 3.2f : attacking ? 9f : 5.5f;
            drawWeight = Mathf.MoveTowards(drawWeight, carryTarget, carryRate * dt);
            if (drawWeight > 0.001f && drawWeight < 0.999f)
            {
                // The right hand tracks the grip from the scabbard mouth to the combat
                // pose (and back), so the draw and sheath read as one motion.
                float drawEase = Mathf.SmoothStep(0f, 1f, drawWeight);
                rightHand = transform.InverseTransformPoint(
                    Vector3.Lerp(SheathGripWorld, transform.TransformPoint(rightHand), drawEase));
                Vector3 sheathBlade = (pelvis.rotation * sheathLocalRot) * Vector3.forward;
                blade = Vector3.Slerp(transform.InverseTransformDirection(sheathBlade), blade, drawEase).normalized;
            }

            float ease = 1f - Mathf.Exp(-22f * dt);
            lowerAmount = Mathf.Lerp(lowerAmount, lower, ease);
            torsoPitch = Mathf.Lerp(torsoPitch, pitch, ease);
            torsoTwist = Mathf.Lerp(torsoTwist, twist, ease);
            rightHandGoal = Vector3.Lerp(rightHandGoal, rightHand, ease);
            bladeDirection = Vector3.Slerp(bladeDirection, blade.normalized, ease);
            leftArm.Reset(); rightArm.Reset(); leftLeg.Reset(); rightLeg.Reset();
            pelvis.localPosition = pelvisBind - pelvis.parent.InverseTransformVector(transform.up * lowerAmount);
            chest.localRotation = chestBind;
            Quaternion torsoTurn = Quaternion.AngleAxis(torsoTwist, transform.up) * Quaternion.AngleAxis(torsoPitch, transform.right);
            chest.rotation = torsoTurn * chest.rotation;
            // Carry the off hand with the torso and keep the elbow folded below the shoulder.
            bool tuckLeftArm = attacking || fighter.State == PrototypeFighter.ActionState.Dodge;
            leftArmTuck = Mathf.Lerp(leftArmTuck, tuckLeftArm ? 1f : 0f, ease);
            if (tuckLeftArm)
            {
                Vector3 loweredHand = transform.TransformPoint(tuckedHand) - transform.up * lowerAmount;
                leftHand = transform.InverseTransformPoint(chest.position + torsoTurn * (loweredHand - chest.position));
            }
            leftHandGoal = Vector3.Lerp(leftHandGoal, leftHand, ease);
            Vector3 leftElbow = Vector3.Lerp(
                transform.TransformDirection(new Vector3(-0.38f, -0.95f, -0.22f)),
                torsoTurn * transform.TransformDirection(new Vector3(-0.12f, -0.95f, -0.50f)), leftArmTuck);
            leftLeg.Reach(transform.TransformPoint(leftFoot + stepL), transform.forward);
            rightLeg.Reach(transform.TransformPoint(rightFoot + stepR), transform.forward);
            leftLeg.LevelEnd(transform); rightLeg.LevelEnd(transform);
            ApplyForwardRun(fighter, pace, gait, dt);
            leftArm.Reach(transform.TransformPoint(leftHandGoal), leftElbow);
            rightArm.Reach(transform.TransformPoint(rightHandGoal), transform.right * 0.38f - transform.up * 0.95f - transform.forward * 0.22f);
            ActiveSwordMotion = null;
            if (drawWeight <= 0.001f)
                RestSwordInSheath();
            else
                PositionSword(transform.TransformDirection(bladeDirection), Mathf.SmoothStep(0f, 90f, drawWeight));
        }

        // Grounded, single horizontal cut. The feet stay planted during the tell and strike.
        public void ApplyStoryPose(StoryPose stance, bool swordDrawn)
        {
            if (!initialized) return;
            transform.localRotation = Quaternion.identity;
            leftArm.Reset(); rightArm.Reset(); leftLeg.Reset(); rightLeg.Reset();
            bool seated = stance == StoryPose.Seated;
            bool kneeling = stance == StoryPose.Kneeling;
            float dip = seated ? .47f : kneeling ? .48f : .02f;
            pelvis.localPosition = pelvisBind - pelvis.parent.InverseTransformVector(transform.up * dip);
            chest.localRotation = chestBind;
            chest.rotation = Quaternion.AngleAxis(seated || kneeling ? 14f : 3f, transform.right) * chest.rotation;
            leftLeg.Reach(transform.TransformPoint(leftFoot + Vector3.forward * (seated ? .36f : kneeling ? .35f : 0f)), transform.forward);
            rightLeg.Reach(transform.TransformPoint(rightFoot + Vector3.forward * (seated ? .36f : kneeling ? -.46f : 0f)), transform.forward);
            leftLeg.LevelEnd(transform);
            rightLeg.LevelEnd(transform);
            float handY = seated || kneeling ? .60f : .88f;
            leftArm.Reach(transform.TransformPoint(new Vector3(-.24f, handY, seated ? .30f : .13f)), -transform.right - transform.up);
            rightArm.Reach(transform.TransformPoint(new Vector3(.24f, handY, seated ? .35f : .17f)), transform.right - transform.up);
            drawWeight = swordDrawn ? 1f : 0f;
            ResetSwordSweep();
            if (swordDrawn)
                PositionSword(seated ? -transform.right : (-transform.up + transform.forward * .23f).normalized, respectCollision: false);
            else
                RestSwordInSheath();
        }

        public void ApplyOpponentPose(DuelEncounter.Phase phase, float time, float speed, float gait, float dt, bool lowSweep = false)
        {
            if (!initialized) return;
            float ready = Mathf.Sin(Time.time * 3f) * 0.015f;
            Vector3 hand = keepSwordDrawn ? new Vector3(0.25f, 0.83f + ready, 0.12f) : new Vector3(0.35f, 0.91f + ready, 0.22f);
            Vector3 offhand = new Vector3(-0.31f, 0.91f, 0.13f);
            Vector3 blade = keepSwordDrawn ? new Vector3(0f, -0.55f, 0.83f) : new Vector3(0.42f, -0.30f, 0.90f);
            Vector3 leftStep = Step(Vector3.forward, gait, speed / 3f);
            Vector3 rightStep = Step(Vector3.forward, gait + Mathf.PI, speed / 3f);
            float yaw = 0f, pitch = 5f, dip = 0.04f;
            Vector3 readyHand = hand;
            Vector3 raisedHand = new Vector3(0.54f, 1.30f, 0.06f);
            Vector3 endHand = new Vector3(-0.23f, 1.26f, 0.42f);
            float turn = 0f;
            if (lowSweep && (phase == DuelEncounter.Phase.Windup || phase == DuelEncounter.Phase.Slash || phase == DuelEncounter.Phase.Recover))
            {
                bool loading = phase == DuelEncounter.Phase.Windup;
                bool recovering = phase == DuelEncounter.Phase.Recover;
                float cut = loading ? 0f : recovering ? 1f : Ease(time, 0f, DuelEncounter.SlashTime);
                float weight = loading ? Ease(time, 0f, 0.56f) : recovering ? 1f - Ease(time, 0f, 0.46f) : 1f;
                rightStep = new Vector3(.12f, 0f, -.12f) * weight;
                leftStep = new Vector3(-.09f, 0f, .20f) * weight;
                Vector3 sweepHand = Vector3.Lerp(new Vector3(.44f, .36f, .11f), new Vector3(-.21f, .32f, .42f), cut);
                hand = Vector3.Lerp(hand, sweepHand, weight);
                Vector3 sweepBlade = Quaternion.Euler(0f, Mathf.Lerp(89f, -87f, cut), 0f) * new Vector3(0f, -.16f, 1f);
                blade = Vector3.Slerp(blade, sweepBlade, weight);
                offhand += new Vector3(-0.12f, 0.05f, 0.05f) * weight;
                yaw = Mathf.Lerp(27f, -30f, cut) * weight;
                pitch += 15f * weight;
                dip += 0.39f * weight;
            }
            else if (phase == DuelEncounter.Phase.Windup)
            {
                float load = Ease(time, 0f, DuelEncounter.WindupTime * 0.76f);
                hand = Vector3.Lerp(hand, raisedHand, load);
                blade = Vector3.Slerp(blade, new Vector3(0.98f, 0.17f, -0.18f), load);
                yaw = 30f * load;
                dip += 0.07f * load;
                turn = load;
            }
            else if (phase == DuelEncounter.Phase.Slash)
            {
                float cut = Ease(time, 0f, DuelEncounter.SlashTime * 0.82f);
                hand = Vector3.Lerp(raisedHand, endHand, cut) + Vector3.forward * (0.21f * Mathf.Sin(cut * Mathf.PI));
                blade = Quaternion.Euler(0f, Mathf.Lerp(100f, -100f, cut), 0f) * new Vector3(0f, 0.05f, 1f);
                yaw = Mathf.Lerp(30f, -34f, cut);
                pitch += 12f * cut;
                dip = 0.11f;
                turn = 1f;
            }
            else if (phase == DuelEncounter.Phase.Recover)
            {
                float settle = Ease(time, 0.08f, DuelEncounter.RecoveryTime * 0.8f);
                hand = Vector3.Lerp(endHand, readyHand, settle);
                blade = Vector3.Slerp(new Vector3(-0.98f, -0.23f, -0.18f), blade, settle);
                yaw = -34f * (1f - settle);
                pitch += 12f * (1f - settle);
                dip += 0.07f * (1f - settle);
                turn = 1f - settle;
            }
            else if (phase == DuelEncounter.Phase.Stagger || phase == DuelEncounter.Phase.Victory)
            {
                float recoil = phase == DuelEncounter.Phase.Victory
                    ? Ease(time, 0f, 0.25f) : 1f - Ease(time, 0.08f, 0.4f);
                pitch = -20f * recoil;
                hand += new Vector3(0.16f, -0.16f, -0.25f) * recoil;
                blade = Vector3.Slerp(blade, new Vector3(0.8f, -0.55f, 0.12f), recoil);
                dip += 0.12f * recoil;
                if (phase == DuelEncounter.Phase.Victory)
                    transform.localRotation = Quaternion.Euler(-75f * Ease(time, 0.12f, 0.52f), 0f, 0f);
            }

            leftStep += Vector3.forward * (0.14f * turn);
            rightStep -= Vector3.forward * (0.10f * turn);
            offhand += new Vector3(-0.10f, 0.09f, -0.22f) * turn;
            leftArm.Reset(); rightArm.Reset(); leftLeg.Reset(); rightLeg.Reset();
            pelvis.localPosition = pelvisBind - pelvis.parent.InverseTransformVector(transform.up * dip);
            chest.localRotation = chestBind;
            chest.rotation = Quaternion.AngleAxis(yaw, transform.up) * Quaternion.AngleAxis(pitch, transform.right) * chest.rotation;
            leftLeg.Reach(transform.TransformPoint(leftFoot + leftStep), transform.forward);
            rightLeg.Reach(transform.TransformPoint(rightFoot + rightStep), transform.forward);
            leftLeg.LevelEnd(transform); rightLeg.LevelEnd(transform);
            leftArm.Reach(transform.TransformPoint(offhand), -transform.right - transform.up * 0.65f);
            bool drawing = keepSwordDrawn || phase == DuelEncounter.Phase.Windup || phase == DuelEncounter.Phase.Slash ||
                (phase == DuelEncounter.Phase.Recover && time < 0.20f);
            drawWeight = Mathf.MoveTowards(drawWeight, drawing ? 1f : 0f, dt * (drawing ? 5.5f : 3.2f));
            if (drawWeight <= 0.001f)
            {
                rightArm.Reach(transform.TransformPoint(new Vector3(0.22f, 0.87f, 0.10f)), transform.right * 0.38f - transform.up);
                RestSwordInSheath();
                return;
            }
            if (drawWeight < 0.999f)
            {
                float draw = Mathf.SmoothStep(0f, 1f, drawWeight);
                hand = Vector3.Lerp(transform.InverseTransformPoint(SheathGripWorld), hand, draw);
                Vector3 sheathDirection = transform.InverseTransformDirection(Scabbard.forward);
                blade = Vector3.Slerp(sheathDirection, blade, draw);
            }
            rightArm.Reach(transform.TransformPoint(hand), transform.right - transform.up * 0.65f);
            PositionSword(transform.TransformDirection(blade));
        }
        private static float Ease(float t, float start, float end)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, t));
        }

        private void ApplyForwardRun(PrototypeFighter fighter, float pace, float gait, float dt)
        {
            bool usable = walkMotions != null && walkMotions.Length > 0 && walkMotions[0] != null
                && fighter.IsGrounded && !fighter.IsCrouching && !fighter.IsGuarding
                && fighter.State == PrototypeFighter.ActionState.Ready && pace > 0.05f;
            walkWeight = Mathf.MoveTowards(walkWeight, usable ? 1f : 0f, dt * 9f);
            if (walkWeight <= 0f || walkMotions == null || walkMotions.Length == 0 || walkMotions[0] == null) return;
            for (int i = 0; i < walkBones.Length; i++) walkBase[i] = walkBones[i].localRotation;
            AnimationClip clip = walkMotions[0];
            clip.SampleAnimation(gameObject, Mathf.Repeat(gait / (2f * Mathf.PI), 1f) * clip.length);
            for (int i = 0; i < walkBones.Length; i++)
                walkBones[i].localRotation = Quaternion.Slerp(walkBase[i], walkBones[i].localRotation, walkWeight);
        }
        private void RestSwordInSheath()
        {
            ResetSwordSweep();
            Quaternion rotation = pelvis.rotation * sheathLocalRot;
            Vector3 grip = SheathGripWorld;
            Vector3 direction = rotation * Vector3.forward;
            if (FindBladeBlocker(grip, direction) != null)
                direction = FindClearBladeDirection(grip, direction);
            rotation = Quaternion.FromToRotation(rotation * Vector3.forward, direction) * rotation;
            Vector3 pivot = grip - direction * 0.09f;
            Scabbard.SetPositionAndRotation(pivot, rotation);
            weapon.SetPositionAndRotation(pivot, rotation);
        }

        private Collider FindBladeBlocker(Vector3 grip, Vector3 direction)
        {
            // Ignore the owner's full locomotion capsule but give the blade
            // a smaller torso volume so it can pass alongside the hips and shoulders.
            if (swordOwnerCollider != null && HitsOwnTorso(grip, direction)) return swordOwnerCollider;
            int count = Physics.OverlapCapsuleNonAlloc(grip + direction * bladeStart, grip + direction * bladeEnd,
                bladeRadius, bladeOverlaps, ~0, QueryTriggerInteraction.Ignore);
            Collider fighter = null;
            for (int i = 0; i < count; i++)
            {
                Collider hit = bladeOverlaps[i];
                if (hit.transform.IsChildOf(swordOwner)) continue;
                if (hit is CharacterController) fighter = hit;
                else return hit;
            }
            return fighter;
        }

        private bool HitsOwnTorso(Vector3 grip, Vector3 direction)
        {
            Vector3 localGrip = transform.InverseTransformPoint(grip);
            Vector3 localDirection = transform.InverseTransformDirection(direction);
            float hips = transform.InverseTransformPoint(pelvis.position).y;
            float shoulders = transform.InverseTransformPoint(chest.position).y;
            Bounds torso = new Bounds(new Vector3(0f, (hips + shoulders) * 0.5f, 0f),
                new Vector3(0.34f, Mathf.Max(0.32f, shoulders - hips + 0.18f), 0.23f));
            torso.Expand(bladeRadius * 1.4f);
            Vector3 start = localGrip + localDirection * bladeStart;
            return torso.IntersectRay(new Ray(start, localDirection), out float distance) && distance <= bladeEnd - bladeStart;
        }
        private Vector3 FindClearBladeDirection(Vector3 grip, Vector3 preferred, Vector3? wristDirection = null)
        {
            // Keep the grip in the hand and fold the blade away from the obstruction.
            for (float angle = 12f; angle <= 168f; angle += 12f)
            {
                Vector3 up = Quaternion.AngleAxis(-angle, transform.right) * preferred;
                if (BladeFits(grip, up, wristDirection)) return up;
                Vector3 down = Quaternion.AngleAxis(angle, transform.right) * preferred;
                if (BladeFits(grip, down, wristDirection)) return down;
                Vector3 right = Quaternion.AngleAxis(angle, transform.up) * preferred;
                if (BladeFits(grip, right, wristDirection)) return right;
                Vector3 left = Quaternion.AngleAxis(-angle, transform.up) * preferred;
                if (BladeFits(grip, left, wristDirection)) return left;
            }
            return preferred;
        }

        private bool BladeFits(Vector3 grip, Vector3 blade, Vector3? wristDirection)
        {
            if (wristDirection.HasValue)
                grip = GripAt(blade, wristDirection.Value, out _);
            return FindBladeBlocker(grip, blade) == null;
        }

        private Vector3 ResolveBlade(Vector3 wristDirection, Vector3 desired)
        {
            BladeBlocker = null;
            Vector3 grip = GripAt(desired, wristDirection, out _);
            Vector3 safe = hasBladePose ? lastBlade : desired;
            Vector3 start = hasBladePose ? lastGrip : grip;
            int samples = hasBladePose
                ? Mathf.Clamp(Mathf.CeilToInt((Vector3.Angle(lastBlade, desired) * Mathf.Deg2Rad * bladeEnd +
                    Vector3.Distance(start, grip)) / bladeRadius), 1, 16)
                : 1;
            for (int i = 1; i <= samples; i++)
            {
                float t = (float)i / samples;
                Vector3 candidate = Vector3.Slerp(hasBladePose ? lastBlade : desired, desired, t).normalized;
                Vector3 candidateGrip = GripAt(candidate, wristDirection, out _);
                Collider block = FindBladeBlocker(Vector3.Lerp(start, candidateGrip, t), candidate);
                if (block != null)
                {
                    BladeBlocker = block;
                    break;
                }
                safe = candidate;
            }
            if (!BladeFits(grip, safe, wristDirection))
                safe = FindClearBladeDirection(grip, safe, wristDirection);
            lastGrip = GripAt(safe, wristDirection, out _);
            lastBlade = safe;
            hasBladePose = true;
            return safe;
        }

        private Vector3 GripAt(Vector3 blade, Vector3 wristDirection, out Quaternion frame)
        {
            Vector3 fingerDirection = Vector3.ProjectOnPlane(wristDirection, blade);
            if (fingerDirection.sqrMagnitude < 0.01f)
                fingerDirection = Vector3.ProjectOnPlane(transform.forward, blade);
            if (fingerDirection.sqrMagnitude < 0.01f)
                fingerDirection = Vector3.ProjectOnPlane(transform.right, blade);
            frame = Quaternion.LookRotation(blade, fingerDirection.normalized);
            Transform hand = rightArm.end;
            if (middleKnuckle == null)
                return hand.position + wristDirection * 0.055f;
            return hand.position + (frame * handFromGrip) * Vector3.Scale(gripOffsetInHand, hand.lossyScale);
        }

        private void PositionSword(Vector3 blade, float roll = 0f, bool respectCollision = true)
        {
            Transform hand = rightArm.end;
            Vector3 wristDirection = (hand.position - rightArm.lower.position).normalized;
            blade = respectCollision ? ResolveBlade(wristDirection, blade.normalized) : blade.normalized;
            Vector3 gripCenter = GripAt(blade, wristDirection, out Quaternion frame);
            if (middleKnuckle != null)
                hand.rotation = frame * handFromGrip;
            weapon.SetPositionAndRotation(gripCenter - blade * 0.09f, frame * Quaternion.AngleAxis(roll, Vector3.forward));
        }
        private static Vector3 Step(Vector3 direction, float phase, float pace)
        {
            return direction * (Mathf.Cos(phase) * 0.26f * pace)
                + Vector3.up * (Mathf.Max(0f, Mathf.Sin(phase)) * 0.15f * pace);
        }
    }
}











