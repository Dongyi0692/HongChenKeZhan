using UnityEngine;

namespace FengChenInn
{
    public sealed class InnRearExitDoor : MonoBehaviour
    {
        public Transform hinge;
        [Range(-170f, 170f)] public float openAngle = 110f;
        [Min(10f)] public float rotationSpeed = 85f;
        public bool OpenRequested;
        private Quaternion closedRotation;
        private void Awake() { if (hinge != null) closedRotation = hinge.localRotation; }
        private void Update()
        {
            if (hinge == null || Time.deltaTime <= 0f) return;
            Quaternion target = closedRotation * Quaternion.Euler(0, OpenRequested ? openAngle : 0f, 0);
            hinge.localRotation = Quaternion.RotateTowards(hinge.localRotation, target, rotationSpeed * Time.deltaTime);
        }
    }
}
