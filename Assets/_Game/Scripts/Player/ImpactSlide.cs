using UnityEngine;

namespace FengChenInn
{
    // Shares the same short, eased slide between both fighters. Callers move
    // their CharacterController, so the slide stops against world geometry.
    internal struct ImpactSlide
    {
        private const float Duration = 0.20f;
        private Vector3 offset;
        private float remaining;

        public bool Active => remaining > 0f;
        public float Strength => remaining / Duration;

        public void Start(Vector3 away, float distance)
        {
            offset = Vector3.ProjectOnPlane(away, Vector3.up).normalized * distance;
            remaining = Duration;
        }

        public Vector3 Advance(float deltaTime)
        {
            float before = Strength;
            remaining = Mathf.Max(0f, remaining - deltaTime);
            return offset * (before * before - Strength * Strength);
        }
    }
}
