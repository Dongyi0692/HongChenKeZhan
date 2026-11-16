using UnityEngine;

namespace FengChenInn
{
    // Only used when story placement bypasses CharacterController.Move.
    internal static class ActorPlacement
    {
        private static readonly Vector3[] Directions =
        {
            Vector3.left, Vector3.right, Vector3.back, Vector3.forward,
            new Vector3(-1f, 0f, -1f).normalized, new Vector3(1f, 0f, -1f).normalized,
            new Vector3(-1f, 0f, 1f).normalized, new Vector3(1f, 0f, 1f).normalized
        };

        internal static bool TryPlace(CharacterController motor, Vector3 wanted, Quaternion rotation, float entranceZ)
        {
            if (motor == null) return false;
            Transform actor = motor.transform;
            Vector3 scale = actor.lossyScale;
            float radius = motor.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) + .025f;
            float halfHeight = Mathf.Max(radius, motor.height * Mathf.Abs(scale.y) * .5f);
            Vector3 center = rotation * Vector3.Scale(motor.center, scale);
            float minY = center.y - halfHeight;
            float wantedFeet = wanted.y + minY;
            float slope = Mathf.Cos(motor.slopeLimit * Mathf.Deg2Rad);
            Physics.SyncTransforms();

            float originalGround = GroundHeight(wanted);
            if (TryCandidate(wanted, originalGround)) return true;
            if (float.IsNegativeInfinity(originalGround)) return false;
            float step = Mathf.Max(.38f, radius + .08f);
            for (int ring = 1; ring <= 3; ring++)
                foreach (Vector3 direction in Directions)
                {
                    Vector3 point = wanted + direction * (step * ring);
                    if (!float.IsNaN(entranceZ) && (point.z < entranceZ) != (wanted.z < entranceZ)) continue;
                    if (!ClearRoute(wanted + center, point + center)) continue;
                    float ground = GroundHeight(point);
                    if (Mathf.Abs(ground - originalGround) <= .12f && TryCandidate(point, ground)) return true;
                }
            return false;

            bool ClearRoute(Vector3 start, Vector3 end)
            {
                Vector3 delta = end - start;
                foreach (RaycastHit hit in Physics.RaycastAll(end, -delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!hit.transform.IsChildOf(actor) && hit.collider.GetComponentInParent<CombatActor>() == null)
                        return false;
                }
                return true;
            }

            float GroundHeight(Vector3 point)
            {
                // Restrict footing to the intended level. In particular, don't
                // choose a tabletop as an alternative floor for a hall spawn.
                Vector3 ray = new Vector3(point.x, wantedFeet + .22f, point.z);
                float groundY = float.NegativeInfinity;
                foreach (RaycastHit hit in Physics.RaycastAll(ray, Vector3.down, .66f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.normal.y < slope || hit.transform.IsChildOf(actor)
                        || hit.collider.GetComponentInParent<CombatActor>() != null) continue;
                    groundY = Mathf.Max(groundY, hit.point.y);
                }
                return groundY;
            }

            bool TryCandidate(Vector3 point, float groundY)
            {
                float clearance = wantedFeet - groundY;
                if (!float.IsNegativeInfinity(groundY) && (clearance < .018f || clearance > .16f))
                    point.y = groundY - minY + .018f;
                Vector3 mid = point + center;
                Vector3 ends = Vector3.up * (halfHeight - radius);
                foreach (Collider hit in Physics.OverlapCapsule(mid - ends, mid + ends, radius, ~0, QueryTriggerInteraction.Ignore))
                    if (!hit.transform.IsChildOf(actor)) return false;

                bool enabled = motor.enabled;
                motor.enabled = false;
                actor.SetPositionAndRotation(point, rotation);
                motor.enabled = enabled;
                return true;
            }
        }
    }
}
