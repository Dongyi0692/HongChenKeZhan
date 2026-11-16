using UnityEngine;
using UnityEngine.InputSystem;

namespace FengChenInn
{
    [DefaultExecutionOrder(-20)]
    public sealed class OrbitCamera : MonoBehaviour
    {
        public Transform target;
        public float distance = 5f;
        public float sensitivity = 0.12f;
        public bool invertY;
        private float yaw, pitch = 24f;




        private void Update()
        {
            if (target == null || Cursor.lockState != CursorLockMode.Locked || Mouse.current == null) return;
            Vector2 delta = Mouse.current.delta.ReadValue();
            yaw += delta.x * sensitivity;
            pitch = Mathf.Clamp(pitch + delta.y * sensitivity * (invertY ? 1f : -1f), -12f, 65f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        }

        private void LateUpdate()
        {
            if (target == null) return;
            Vector3 focus = target.position + Vector3.up * 1.2f;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 direction = rotation * Vector3.back;
            float safeDistance = distance;
            foreach (RaycastHit hit in Physics.SphereCastAll(focus, 0.16f, direction, distance, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(target)) safeDistance = Mathf.Min(safeDistance, Mathf.Max(0.45f, hit.distance - 0.12f));
            transform.SetPositionAndRotation(focus + direction * safeDistance, rotation);
        }
    }
}

