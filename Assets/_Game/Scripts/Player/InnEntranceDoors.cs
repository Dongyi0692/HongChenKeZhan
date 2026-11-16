using UnityEngine;

namespace FengChenInn
{
    public sealed class InnEntranceDoors : MonoBehaviour
    {
        public Transform player;
        public Transform leftHinge;
        public Transform rightHinge;
        [Min(0f)] public float activationDistance = 3f;
        [Min(0f)] public float rotationSpeed = 90f;
        [Range(0f, 180f)] public float openAngle = 135f;
        public bool ManualControl { get; set; }
        public bool OpenRequested { get; set; }

        private Quaternion leftClosed;
        private Quaternion rightClosed;
        private float angle;

        private void Awake()
        {
            AttachDoorVisual("Existing_wood_door_left_root", leftHinge);
            AttachDoorVisual("Existing_wood_door_right_root", rightHinge);
            leftClosed = leftHinge.localRotation;
            rightClosed = rightHinge.localRotation;
        }

        private void AttachDoorVisual(string namePrefix, Transform hinge)
        {
            if (hinge == null) return;
            foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!candidate.name.StartsWith(namePrefix) || candidate.IsChildOf(hinge)) continue;
                // The door is nested inside the imported environment prefab. A runtime
                // copy avoids prefab parenting restrictions and static-batched geometry.
                GameObject visual = Instantiate(candidate.gameObject, hinge, true);
                visual.name = candidate.name + " (animated)";
                foreach (Transform child in visual.GetComponentsInChildren<Transform>(true)) child.gameObject.isStatic = false;
                foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                visual.SetActive(true);
                candidate.gameObject.SetActive(false);
                return;
            }
        }

        private void Update()
        {
            Vector3 offset = player.position - transform.position;
            offset.y = 0f;
            bool open = ManualControl ? OpenRequested : offset.sqrMagnitude <= activationDistance * activationDistance;
            float target = open ? openAngle : 0f;
            angle = Mathf.MoveTowards(angle, target, rotationSpeed * Time.deltaTime);
            leftHinge.localRotation = leftClosed * Quaternion.Euler(0f, -angle, 0f);
            rightHinge.localRotation = rightClosed * Quaternion.Euler(0f, angle, 0f);
        }
    }
}
