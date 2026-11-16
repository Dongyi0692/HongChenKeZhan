using UnityEngine;

namespace FengChenInn
{
    /// <summary>Selects a nearby visible opponent; it never changes facing, movement or damage.</summary>
    public static class CombatTargeting
    {
        const float MaximumHeightDifference = 1.25f;
        const float TargetSwitchAdvantage = .08f;

        public static bool IsValid(CombatActor owner, CombatActor target)
        {
            return owner != null && owner.isActiveAndEnabled && !owner.IsDefeated && !owner.ControlLocked
                && target != null && target.isActiveAndEnabled && !target.IsDefeated && !target.ControlLocked
                && owner.IsHostileTo(target);
        }

        public static Vector3 AimPoint(CombatActor target)
        {
            if (target == null) return Vector3.zero;
            var controller = target.GetComponent<CharacterController>();
            return controller != null
                ? controller.transform.TransformPoint(controller.center)
                : target.transform.position + Vector3.up * .9f;
        }

        public static CombatActor Select(CombatActor owner, Camera view, Vector3 desiredDirection,
            float maxDistance, float maxAngle, CombatActor previous = null)
        {
            if (owner == null || !owner.isActiveAndEnabled || owner.IsDefeated || owner.ControlLocked
                || maxDistance <= .01f) return null;

            Vector3 forward = Vector3.ProjectOnPlane(desiredDirection, Vector3.up);
            if (forward.sqrMagnitude < .001f)
                forward = Vector3.ProjectOnPlane(owner.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < .001f) return null;
            forward.Normalize();
            // This is forward attack assistance, never automatic selection behind the requested direction.
            float angleLimit = Mathf.Clamp(maxAngle, 0f, 89f);
            Vector3 origin = AimPoint(owner);
            CombatActor best = null;
            float bestScore = float.PositiveInfinity;
            float previousScore = float.PositiveInfinity;
            foreach (CombatActor candidate in CombatActor.Actors)
            {
                if (!TryScore(owner, candidate, view, origin, forward, maxDistance, angleLimit, out float score))
                    continue;
                if (candidate == previous) previousScore = score;
                if (score < bestScore || Mathf.Approximately(score, bestScore)
                    && best != null && candidate.GetInstanceID() < best.GetInstanceID())
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            // Hysteresis is applied only after every eligibility check. Leaving the cone/range,
            // becoming friendly, falling or moving behind cover releases the old target immediately.
            return previous != null && previousScore < float.PositiveInfinity
                && previousScore <= bestScore + TargetSwitchAdvantage ? previous : best;
        }

        static bool TryScore(CombatActor owner, CombatActor candidate, Camera view, Vector3 origin,
            Vector3 forward, float maxDistance, float angleLimit, out float score)
        {
            score = float.PositiveInfinity;
            if (!IsValid(owner, candidate)) return false;
            Vector3 point = AimPoint(candidate);
            Vector3 delta = point - origin;
            if (Mathf.Abs(delta.y) > MaximumHeightDifference || delta.sqrMagnitude > maxDistance * maxDistance)
                return false;
            Vector3 flat = Vector3.ProjectOnPlane(delta, Vector3.up);
            // Overlapping actor centres do not provide an intentional facing direction.
            if (flat.sqrMagnitude < .0001f) return false;
            float angle = Vector3.Angle(forward, flat);
            // A reversed input can change intent before the body has turned. Do not
            // promise a target behind the body that the short windup cannot reach.
            if (angle > angleLimit || Vector3.Angle(owner.transform.forward, flat) >= 89f) return false;

            float screenDistance = 0f;
            if (view != null)
            {
                Vector3 viewport = view.WorldToViewportPoint(point);
                if (viewport.z <= view.nearClipPlane || viewport.z > view.farClipPlane
                    || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                    return false;
                screenDistance = Mathf.Clamp01(new Vector2(viewport.x - .5f, viewport.y - .5f).magnitude / .7071f);
                Vector3 sight = point - view.transform.position;
                foreach (var obstruction in Physics.RaycastAll(view.transform.position, sight.normalized,
                    sight.magnitude, owner.collisionMask, QueryTriggerInteraction.Ignore))
                    if (obstruction.collider.GetComponentInParent<CombatActor>() == null) return false;
            }
            if (!owner.HasLineOfSight(candidate)) return false;

            // Input direction matters most; closeness and screen position break otherwise similar choices.
            score = .62f * angle / Mathf.Max(1f, angleLimit)
                + .23f * delta.magnitude / maxDistance + .15f * screenDistance;
            return true;
        }
    }
}
