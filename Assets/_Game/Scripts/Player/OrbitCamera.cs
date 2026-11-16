using UnityEngine;
using UnityEngine.InputSystem;

namespace FengChenInn
{
    [DefaultExecutionOrder(-20)]
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
        public LayerMask obstructionMask = ~0;
        private float yaw, pitch = 24f;
        private Camera view;
        private Transform frameA, frameB;
        private Vector3 frameDirection, currentFocus;
        private float followDistance, transitionRemaining;
        private bool initialized;
        private readonly RaycastHit[] castHits = new RaycastHit[96];
        private readonly Collider[] overlaps = new Collider[64];
        private static readonly float[] FallbackAngles = { -20f, 20f, -45f, 45f, -75f, 75f, -110f, 110f, 180f };
        public bool IsFramingActors => frameA != null && frameB != null;
        public float CurrentDistance { get; private set; }
        public float ObstructionRadius { get; private set; }
        public bool ClearanceSatisfied { get; private set; }

        private void Awake() => view = GetComponent<Camera>();

        public void FrameActors(Transform a, Transform b, bool immediate = false)
        {
            if (a == null || b == null) return;
            if (frameA == a && frameB == b) return;
            EnsureInitialized();
            frameA = a; frameB = b;
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
            yaw += delta.x * sensitivity;
            pitch = Mathf.Clamp(pitch + delta.y * sensitivity * (invertY ? 1f : -1f), -8f, 60f);
            // A deliberate mouse movement immediately owns the orbit direction.
            if (delta.sqrMagnitude > 1f) transitionRemaining = 0f;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            EnsureInitialized();
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
        private Vector3 FrameFocus() => (frameA.position + frameB.position) * .5f + Vector3.up * 1.22f;
        private float FollowLimit() => Mathf.Max(.8f, Mathf.Min(distance, indoorDistanceLimit));
        private float FrameDistance() => Mathf.Clamp(Vector3.Distance(frameA.position, frameB.position) * .75f + 1f, 1.8f, Mathf.Max(1.8f, framingDistanceLimit));

        private void ApplyFrame(float dt, bool immediate)
        {
            ObstructionRadius = ProbeRadius();
            bool framing = IsFramingActors;
            Vector3 wantedFocus = RecoverFocus(framing ? FrameFocus() : FollowFocus(), framing);
            Vector3 direction = framing ? frameDirection : Quaternion.Euler(pitch, yaw, 0f) * Vector3.back;
            float requested = framing ? FrameDistance() : FollowLimit();
            Vector3 safePosition = ResolvePosition(wantedFocus, direction, requested);
            float safeDistance = Vector3.Distance(wantedFocus, safePosition);
            if (!framing)
            {
                // Obstructions retract immediately; release back to full distance gently.
                followDistance = immediate || safeDistance < followDistance ? safeDistance
                    : Mathf.MoveTowards(followDistance, safeDistance, distanceReturnSpeed * dt);
                if (safeDistance > .001f)
                    safePosition = ResolvePosition(wantedFocus, (safePosition - wantedFocus) / safeDistance, followDistance);
            }
            if (!immediate && transitionRemaining > 0f)
            {
                float blend = 1f - Mathf.Exp(-dt / Mathf.Max(.02f, transitionTime * .32f));
                currentFocus = Vector3.Lerp(currentFocus, wantedFocus, blend);
                Vector3 blended = Vector3.Lerp(transform.position, safePosition, blend);
                Vector3 offset = blended - wantedFocus;
                // Blending camera positions must never tunnel across an intervening wall.
                if (offset.sqrMagnitude > .0001f)
                    safePosition = ResolvePosition(wantedFocus, offset.normalized, offset.magnitude);
                transitionRemaining = Mathf.Max(0f, transitionRemaining - dt);
            }
            else currentFocus = wantedFocus;
            Vector3 look = currentFocus - safePosition;
            if (look.sqrMagnitude < .001f) look = wantedFocus - safePosition;
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

        private Vector3 ResolvePosition(Vector3 focus, Vector3 direction, float requested)
        {
            direction.Normalize();
            requested = Mathf.Max(.12f, requested);
            bool directValid = TryDirection(focus, direction, requested, out Vector3 direct);
            if (directValid
                && Vector3.Distance(focus, direct) >= Mathf.Min(.65f, requested)) return direct;
            Vector3 best = direct;
            bool found = directValid;
            float bestScore = found ? Vector3.Distance(focus, best) : float.NegativeInfinity;
            // Used only when the requested orbit cannot fit a safe close camera.
            foreach (float angle in FallbackAngles)
            {
                Vector3 alternative = Quaternion.AngleAxis(angle, Vector3.up) * direction;
                if (!TryDirection(focus, alternative, requested, out Vector3 position)) continue;
                float score = Vector3.Distance(focus, position) - Mathf.Abs(angle) * .012f;
                if (score > bestScore) { bestScore = score; best = position; found = true; }
            }
            if (found) return best;
            if (TryDirection(focus, (direction + Vector3.up).normalized, requested, out Vector3 raised)) return raised;
            if (PointClear(transform.position) && SightClear(focus, transform.position)) return transform.position;
            // A physically enclosed focus has no valid solution; expose this to validation.
            return focus;
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
        private bool Blocks(Collider collider) => collider != null && collider.GetComponentInParent<CombatActor>() == null
            && (target == null || !collider.transform.IsChildOf(target));
    }
}
