using UnityEngine;
using UnityEngine.InputSystem;

namespace FengChenInn
{
    // CombatActor applies attack root travel in LateUpdate. Follow only after
    // that movement and the fighter's final pose have completed this frame.
    [DefaultExecutionOrder(10000)]
    public sealed class OrbitCamera : MonoBehaviour
    {
        public Transform target;
        public float distance = 3f;
        public float sensitivity = .12f;
        public bool invertY;
        [Header("Indoor framing")]
        [Min(.8f)] public float indoorDistanceLimit = 3f;
        [Min(.8f)] public float framingDistanceLimit = 3.2f;
        [Min(.05f)] public float transitionTime = .24f;
        [Min(.1f)] public float distanceReturnSpeed = 3.2f;
        [Min(.05f)] public float minimumProbeRadius = .18f;
        [Min(0f)] public float wallPadding = .04f;
        [Min(.5f)] public float preferredMinimumDistance = 1.15f;
        [Min(30f)] public float obstructionTurnSpeed = 180f;
        [Header("Combat target lock")]
        [Min(1f)] public float lockAcquireDistance = 8f;
        [Min(1f)] public float lockReleaseDistance = 10f;
        [Min(30f)] public float lockTurnSpeed = 240f;
        public LayerMask obstructionMask = ~0;
        private float yaw, pitch = 24f;
        private Camera view;
        private Transform frameA, frameB;
        private Vector3 frameDirection, currentFocus;
        private float followDistance, transitionRemaining;
        private bool initialized;
        private bool holdingObstructionSide;
        private float obstructionSideAngle;
        private CombatActor lockedTarget;
        private float lockSightLostAt = -1f;
        private readonly RaycastHit[] castHits = new RaycastHit[96];
        private readonly Collider[] overlaps = new Collider[64];
        private static readonly float[] FallbackAngles = { -20f, 20f, -45f, 45f, -75f, 75f, -110f, 110f, 180f };
        public bool IsFramingActors => frameA != null && frameB != null;
        public float CurrentDistance { get; private set; }
        public float ObstructionRadius { get; private set; }
        public bool ClearanceSatisfied { get; private set; }
        public CombatActor LockedTarget => lockedTarget;
        public bool IsTargetLocked => lockedTarget != null;

        private void Awake() => view = GetComponent<Camera>();

        private CombatActor LockOwner => target != null ? target.GetComponentInParent<CombatActor>() : null;
        private bool CanUseTargetLock()
        {
            CombatActor owner = LockOwner;
            if (owner == null || owner.IsDefeated || owner.ControlLocked || IsFramingActors) return false;
            var encounter = owner.GetComponent<DuelEncounter>();
            return encounter == null || encounter.IsCombatActive;
        }

        public void ClearTargetLock()
        {
            if (lockedTarget != null)
            {
                // Continue free look from the view the player was just seeing.
                yaw = transform.eulerAngles.y;
                pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, transform.eulerAngles.x), -8f, 60f);
                holdingObstructionSide = false;
                transitionRemaining = transitionTime;
            }
            lockedTarget = null;
            lockSightLostAt = -1f;
        }

        public bool ToggleTargetLock()
        {
            if (lockedTarget != null) { ClearTargetLock(); return false; }
            return TryLockTarget(ChooseLockTarget(null));
        }

        public bool CycleTargetLock()
        {
            CombatActor next = ChooseLockTarget(lockedTarget);
            return next != null && TryLockTarget(next);
        }

        public bool TryLockTarget(CombatActor candidate)
        {
            if (!CanUseTargetLock() || !LockCandidate(candidate, lockAcquireDistance, true)) return false;
            EnsureInitialized();
            lockedTarget = candidate;
            lockSightLostAt = -1f;
            holdingObstructionSide = false;
            transitionRemaining = transitionTime;
            return true;
        }

        private CombatActor ChooseLockTarget(CombatActor excluded)
        {
            if (!CanUseTargetLock()) return null;
            CombatActor best = null;
            float score = float.PositiveInfinity;
            foreach (CombatActor candidate in CombatActor.Actors)
            {
                if (candidate == excluded || !LockCandidate(candidate, lockAcquireDistance, true)) continue;
                Vector3 viewport = view.WorldToViewportPoint(CombatTargeting.AimPoint(candidate));
                float candidateScore = new Vector2(viewport.x - .5f, viewport.y - .5f).sqrMagnitude * 4f
                    + Vector3.Distance(target.position, candidate.transform.position) / lockAcquireDistance * .15f;
                if (candidateScore < score) { score = candidateScore; best = candidate; }
            }
            return best;
        }

        private bool LockCandidate(CombatActor candidate, float maxDistance, bool requireVisible)
        {
            CombatActor owner = LockOwner;
            if (!CombatTargeting.IsValid(owner, candidate)) return false;
            Vector3 offset = candidate.transform.position - owner.transform.position;
            if (offset.sqrMagnitude > maxDistance * maxDistance || Mathf.Abs(offset.y) > 2.5f) return false;
            if (!requireVisible) return true;
            if (view == null) view = GetComponent<Camera>();
            Vector3 point = CombatTargeting.AimPoint(candidate);
            Vector3 viewport = view.WorldToViewportPoint(point);
            return viewport.z > view.nearClipPlane && viewport.x >= -.05f && viewport.x <= 1.05f
                && viewport.y >= -.05f && viewport.y <= 1.05f && owner.HasLineOfSight(candidate)
                && SightClear(transform.position, point);
        }

        private void UpdateTargetLock(float dt)
        {
            if (lockedTarget == null) return;
            if (!CanUseTargetLock() || !LockCandidate(lockedTarget, Mathf.Max(lockAcquireDistance, lockReleaseDistance), false))
            { ClearTargetLock(); return; }
            if (LockOwner.HasLineOfSight(lockedTarget)) lockSightLostAt = -1f;
            else
            {
                if (lockSightLostAt < 0f) lockSightLostAt = Time.time;
                if (Time.time - lockSightLostAt >= .45f) { ClearTargetLock(); return; }
            }
            Vector3 toward = Vector3.ProjectOnPlane(lockedTarget.transform.position - target.position, Vector3.up);
            if (toward.sqrMagnitude > .01f)
                yaw = Mathf.MoveTowardsAngle(yaw, Quaternion.LookRotation(toward).eulerAngles.y, lockTurnSpeed * dt);
        }

        public void FrameActors(Transform a, Transform b, bool immediate = false)
        {
            if (a == null || b == null) return;
            ClearTargetLock();
            if (frameA == a && frameB == b) return;
            EnsureInitialized();
            frameA = a; frameB = b;
            holdingObstructionSide = false;
            Vector3 center = RecoverFocus(FrameFocus(), true);
            Vector3 across = Vector3.ProjectOnPlane(b.position - a.position, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, across).normalized;
            if (side.sqrMagnitude < .01f) side = transform.right;
            float requested = FrameDistance();
            Vector3 best = side;
            float score = float.NegativeInfinity;
            foreach (Vector3 candidate in new[] { side, -side, (side + across * .45f).normalized, (-side + across * .45f).normalized })
            {
                Vector3 direction = (candidate + Vector3.up * .20f).normalized;
                Vector3 position = ResolvePosition(center, direction, requested);
                float value = Vector3.Distance(center, position);
                if (!SightClear(a.position + Vector3.up * 1.2f, position)) value -= 10f;
                if (!SightClear(b.position + Vector3.up * 1.2f, position)) value -= 10f;
                value -= Vector3.Distance(transform.position, position) * .05f;
                if (value > score) { score = value; best = direction; }
            }
            frameDirection = best;
            transitionRemaining = immediate ? 0f : transitionTime;
            if (immediate) ApplyFrame(0f, true);
        }

        public void ResumeFollow(bool immediate = false)
        {
            EnsureInitialized();
            frameA = frameB = null;
            holdingObstructionSide = false;
            Vector3 angles = transform.eulerAngles;
            yaw = angles.y;
            pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, angles.x), -8f, 60f);
            followDistance = Mathf.Min(Vector3.Distance(transform.position, FollowFocus()), FollowLimit());
            transitionRemaining = immediate ? 0f : transitionTime;
            if (immediate) ApplyFrame(0f, true);
        }

        private void Update()
        {
            if (target == null || IsFramingActors || Time.timeScale <= 0f
                || Cursor.lockState != CursorLockMode.Locked || Mouse.current == null) return;
            Vector2 delta = Mouse.current.delta.ReadValue();
            // Lock-on owns the horizontal orbit until the player unlocks or
            // cycles targets. Mouse movement may still adjust camera elevation.
            if (IsTargetLocked) delta.x = 0f;
            else yaw += delta.x * sensitivity;
            pitch = Mathf.Clamp(pitch + delta.y * sensitivity * (invertY ? 1f : -1f), -8f, 60f);
            // Free-look input ends a transition, but must not erase the safe
            // shoulder every frame near a wall. ResolvePosition releases that
            // shoulder when the requested direction has regained clearance.
            if (delta.sqrMagnitude > 1f)
                transitionRemaining = 0f;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            EnsureInitialized();
            if (Time.timeScale > 0f) UpdateTargetLock(Mathf.Min(Time.deltaTime, .1f));
            ApplyFrame(Mathf.Min(Time.unscaledDeltaTime, .1f), false);
        }

        private void EnsureInitialized()
        {
            if (view == null) view = GetComponent<Camera>();
            if (initialized) return;
            initialized = true;
            currentFocus = FollowFocus();
            followDistance = FollowLimit();
            ObstructionRadius = ProbeRadius();
        }
        private Vector3 FollowFocus() => target != null ? target.position + Vector3.up * 1.25f : transform.position + transform.forward;
        private Vector3 LockFocus()
        {
            Vector3 playerFocus = FollowFocus();
            if (lockedTarget == null) return playerFocus;
            Vector3 enemyFocus = CombatTargeting.AimPoint(lockedTarget) + Vector3.up * .18f;
            // Keep the enemy near the centre while retaining the player's body.
            return playerFocus + Vector3.ClampMagnitude((enemyFocus - playerFocus) * .52f, 1.8f);
        }
        private Vector3 FrameFocus() => (frameA.position + frameB.position) * .5f + Vector3.up * 1.22f;
        private float FollowLimit() => Mathf.Max(.8f, Mathf.Min(distance, indoorDistanceLimit));
        private float FrameDistance() => Mathf.Clamp(Vector3.Distance(frameA.position, frameB.position) * .75f + 1f, 1.8f, Mathf.Max(1.8f, framingDistanceLimit));

        private void ApplyFrame(float dt, bool immediate)
        {
            ObstructionRadius = ProbeRadius();
            bool framing = IsFramingActors;
            // The orbit/collision pivot stays on the player's shoulders. Moving
            // this pivot to the two-person midpoint consumed the camera's rear
            // clearance and pushed the player below the frame at normal range.
            // Lock-on changes where we look, not the player's camera boom origin.
            Vector3 wantedFocus = RecoverFocus(framing ? FrameFocus() : FollowFocus(), framing);
            Vector3 wantedLookFocus = !framing && lockedTarget != null
                ? RecoverFocus(LockFocus(), false) : wantedFocus;
            Vector3 direction = framing ? frameDirection : Quaternion.Euler(pitch, yaw, 0f) * Vector3.back;
            float requested = framing ? FrameDistance() : FollowLimit();
            Vector3 safePosition = ResolvePosition(wantedFocus, direction, requested, !framing);
            float safeDistance = Vector3.Distance(wantedFocus, safePosition);
            if (!framing)
            {
                // Obstructions retract immediately; release back to full distance gently.
                followDistance = immediate || safeDistance < followDistance ? safeDistance
                    : Mathf.MoveTowards(followDistance, safeDistance, distanceReturnSpeed * dt);
                if (safeDistance > .001f && followDistance < safeDistance - .001f)
                {
                    // The shoulder was already selected above. Contract on that
                    // ray instead of performing a second side search that can
                    // disagree with the first one and make the camera swap sides.
                    if (TryDirection(wantedFocus, (safePosition - wantedFocus) / safeDistance,
                        followDistance, out Vector3 contracted))
                        safePosition = contracted;
                    else followDistance = safeDistance; // Keep the verified safe position.
                }
            }
            if (!framing && !immediate && holdingObstructionSide)
            {
                Vector3 previous = transform.position - wantedFocus;
                Vector3 next = safePosition - wantedFocus;
                if (previous.sqrMagnitude > .01f && next.sqrMagnitude > .01f)
                {
                    Vector3 turn = Vector3.RotateTowards(previous.normalized, next.normalized,
                        obstructionTurnSpeed * Mathf.Deg2Rad * dt, 0f);
                    // Smooth only while the intermediate route still fits outside
                    // the character and has actual clearance. Never lerp through walls.
                    if (TryDirection(wantedFocus, turn, next.magnitude, out Vector3 intermediate)
                        && Vector3.Distance(wantedFocus, intermediate) >= preferredMinimumDistance * .85f)
                        safePosition = intermediate;
                }
            }
            if (!immediate && transitionRemaining > 0f)
            {
                float blend = 1f - Mathf.Exp(-dt / Mathf.Max(.02f, transitionTime * .32f));
                currentFocus = Vector3.Lerp(currentFocus, wantedLookFocus, blend);
                Vector3 blended = Vector3.Lerp(transform.position, safePosition, blend);
                Vector3 offset = blended - wantedFocus;
                // Blending camera positions must never tunnel across an intervening wall.
                if (offset.sqrMagnitude > .0001f
                    && TryDirection(wantedFocus, offset.normalized, offset.magnitude, out Vector3 transitionPosition))
                    safePosition = transitionPosition;
                transitionRemaining = Mathf.Max(0f, transitionRemaining - dt);
            }
            else currentFocus = wantedLookFocus;
            Vector3 look = currentFocus - safePosition;
            if (look.sqrMagnitude < .001f) look = wantedLookFocus - safePosition;
            if (look.sqrMagnitude < .001f) look = transform.forward;
            transform.SetPositionAndRotation(safePosition, Quaternion.LookRotation(look, Vector3.up));
            CurrentDistance = Vector3.Distance(safePosition, wantedFocus);
            ClearanceSatisfied = PointClear(safePosition) && SightClear(wantedFocus, safePosition);
        }

        private float ProbeRadius()
        {
            if (view == null) return minimumProbeRadius;
            // Enclose the near clip rectangle, including its corners, not just the lens point.
            float halfHeight = view.nearClipPlane * Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * view.aspect;
            return Mathf.Max(minimumProbeRadius,
                Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight + view.nearClipPlane * view.nearClipPlane) + wallPadding);
        }

        private Vector3 RecoverFocus(Vector3 wanted, bool framing)
        {
            if (PointClear(wanted, .035f)) return wanted;
            // The midpoint of two people can sit inside a pillar even though both
            // people are correctly placed. Anchor on a visible participant instead.
            if (framing)
            {
                Vector3 first = frameA.position + Vector3.up * 1.22f;
                if (PointClear(first, .035f)) return first;
                Vector3 second = frameB.position + Vector3.up * 1.22f;
                if (PointClear(second, .035f)) return second;
            }
            foreach (Vector3 offset in new[] { Vector3.up * .35f, Vector3.up * .7f,
                transform.right * .45f, -transform.right * .45f })
                if (PointClear(wanted + offset, .035f)) return wanted + offset;
            return wanted;
        }

        private Vector3 ResolvePosition(Vector3 focus, Vector3 direction, float requested, bool preserveSide = false)
        {
            direction.Normalize();
            requested = Mathf.Max(.12f, requested);
            bool directValid = TryDirection(focus, direction, requested, out Vector3 direct);
            // Seek a side or raised third-person view before the camera enters
            // the character. Releasing the side uses a separate threshold.
            float releaseDistance = preferredMinimumDistance + (preserveSide && holdingObstructionSide ? .3f : 0f);
            if (directValid && Vector3.Distance(focus, direct) >= Mathf.Min(releaseDistance, requested))
            {
                if (preserveSide) holdingObstructionSide = false;
                return direct;
            }
            Vector3 best = direct;
            bool found = directValid;
            float bestScore = found ? CameraCandidateScore(focus, best, 0f, preserveSide) : float.NegativeInfinity;
            if (preserveSide && holdingObstructionSide)
            {
                Vector3 heldDirection = Quaternion.AngleAxis(obstructionSideAngle, Vector3.up) * direction;
                if (TryDirection(focus, heldDirection, requested, out Vector3 held))
                {
                    // Keep the current safe shoulder unless another candidate
                    // improves clearance substantially, instead of swapping L/R
                    // for centimetre-scale differences on every rendered frame.
                    float heldScore = CameraCandidateScore(focus, held, obstructionSideAngle, preserveSide) + .4f;
                    if (heldScore > bestScore) { bestScore = heldScore; best = held; found = true; }
                }
            }
            // Used only when the requested orbit cannot fit a safe close camera.
            foreach (float angle in FallbackAngles)
            {
                Vector3 alternative = Quaternion.AngleAxis(angle, Vector3.up) * direction;
                if (!TryDirection(focus, alternative, requested, out Vector3 position)) continue;
                float score = CameraCandidateScore(focus, position, angle, preserveSide);
                if (score > bestScore) { bestScore = score; best = position; found = true; }
            }
            // A low table/rail may obstruct the shoulder view but leave a safe
            // slightly higher camera. Prefer it before extreme inward retraction.
            if (!found || Vector3.Distance(focus, best) < preferredMinimumDistance)
            {
                Vector3 raisedDirection = (direction + Vector3.up * .65f).normalized;
                if (TryDirection(focus, raisedDirection, requested, out Vector3 raisedPosition)
                    && Vector3.Distance(focus, raisedPosition) > (found ? Vector3.Distance(focus, best) : 0f))
                { best = raisedPosition; found = true; }
            }
            if (found)
            {
                if (preserveSide)
                {
                    obstructionSideAngle = Vector3.SignedAngle(Vector3.ProjectOnPlane(direction, Vector3.up),
                        Vector3.ProjectOnPlane(best - focus, Vector3.up), Vector3.up);
                    holdingObstructionSide = Mathf.Abs(obstructionSideAngle) > 1f;
                }
                return best;
            }
            if (TryDirection(focus, (direction + Vector3.up).normalized, requested, out Vector3 raised)) return raised;
            if (PointClear(transform.position) && SightClear(focus, transform.position)) return transform.position;
            // A physically enclosed focus has no valid solution; expose this to validation.
            return focus;
        }

        private float CameraCandidateScore(Vector3 focus, Vector3 position, float angle, bool preserveSide)
        {
            float length = Vector3.Distance(focus, position);
            float score = length - Mathf.Abs(angle) * .012f - Mathf.Max(0f, preferredMinimumDistance - length) * 3f;
            if (preserveSide && holdingObstructionSide)
            {
                // When mouse input rotates the held ray into a wall, prefer a
                // nearby safe ray on the same side over an equally clear ray
                // across the wall. Relative orbit angle alone favoured +75
                // over -110, causing an instant 150-degree shoulder swap.
                Vector3 previous = Vector3.ProjectOnPlane(transform.position - focus, Vector3.up);
                Vector3 next = Vector3.ProjectOnPlane(position - focus, Vector3.up);
                if (previous.sqrMagnitude > .01f && next.sqrMagnitude > .01f)
                    score -= Vector3.Angle(previous, next) * .012f;
            }
            return score;
        }

        private bool TryDirection(Vector3 focus, Vector3 direction, float requested, out Vector3 position)
        {
            float available = requested;
            int count = Physics.SphereCastNonAlloc(focus, ObstructionRadius, direction, castHits,
                requested, obstructionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Blocks(castHits[i].collider) && castHits[i].distance > .001f)
                    available = Mathf.Min(available, Mathf.Max(0f, castHits[i].distance - wallPadding));
            // The small probe also handles a focus whose larger near-plane sphere
            // starts overlapped: do not trust a large cast alone in that situation.
            count = Physics.SphereCastNonAlloc(focus, .035f, direction, castHits,
                requested, obstructionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Blocks(castHits[i].collider) && castHits[i].distance > .001f)
                    available = Mathf.Min(available, Mathf.Max(0f, castHits[i].distance - ObstructionRadius - wallPadding));
            for (int sample = 0; sample < 7; sample++)
            {
                float length = available * (1f - sample / 7f);
                if (length < .10f) break;
                position = focus + direction * length;
                if (PointClear(position) && SightClear(focus, position)) return true;
            }
            position = focus;
            return false;
        }
        private bool PointClear(Vector3 point) => PointClear(point, ObstructionRadius);
        private bool PointClear(Vector3 point, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(point, radius, overlaps,
                obstructionMask, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++) if (Blocks(overlaps[i])) return false;
            return true;
        }
        private bool SightClear(Vector3 from, Vector3 to)
        {
            Vector3 offset = to - from;
            if (offset.sqrMagnitude < .0001f) return true;
            int count = Physics.SphereCastNonAlloc(from, .035f, offset.normalized, castHits,
                offset.magnitude, obstructionMask, QueryTriggerInteraction.Ignore);
            if (count == castHits.Length) return false;
            for (int i = 0; i < count; i++) if (Blocks(castHits[i].collider)) return false;
            return true;
        }
        private bool Blocks(Collider collider)
        {
            if (collider == null || collider.GetComponentInParent<CombatActor>() != null
                || target != null && collider.transform.IsChildOf(target)) return false;
            // Stage11WorldSetup's four invisible play-area limits block actors,
            // not sight. Do not ignore renderless colliders in general: the inn's
            // real walls and furniture also use separate collision proxies.
            for (Transform node = collider.transform; node != null; node = node.parent)
                if (node.name == "Invisible boundary") return false;
            return true;
        }
    }
}
