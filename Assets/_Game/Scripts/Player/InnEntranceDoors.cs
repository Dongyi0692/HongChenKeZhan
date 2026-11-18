using UnityEngine;

namespace FengChenInn
{
    public sealed class InnEntranceDoors : MonoBehaviour
    {
        public Transform player;
        public Transform leftHinge;
        public Transform rightHinge;
        public Transform visualSourceRoot;
        public bool rebuildImportedDoorCollision;
        [Min(0f)] public float activationDistance = 3f;
        [Min(0f)] public float releaseDistanceMargin = .45f;
        [Min(0f)] public float closeDelay = .45f;
        [Min(0f)] public float rotationSpeed = 90f;
        [Range(0f, 180f)] public float openAngle = 135f;
        public bool ManualControl { get; set; }
        public bool OpenRequested { get; set; }
        internal bool HoldOpenForEncounter { get; set; }
        public bool PlayerInRange { get; private set; }
        public float CurrentAngle => angle;

        private InnDoorAudio doorAudio;
        private Quaternion leftClosed;
        private Quaternion rightClosed;
        private float angle;
        private float lastPlayerNearAt = -10f;

        private void Awake()
        {
            doorAudio = GetComponent<InnDoorAudio>() ?? gameObject.AddComponent<InnDoorAudio>();
            AttachDoorVisual("Existing_wood_door_left_root", leftHinge);
            AttachDoorVisual("Existing_wood_door_right_root", rightHinge);
            if (leftHinge != null) leftClosed = leftHinge.localRotation;
            if (rightHinge != null) rightClosed = rightHinge.localRotation;
        }

        private void AttachDoorVisual(string namePrefix, Transform hinge)
        {
            if (hinge == null) return;
            Transform[] candidates = visualSourceRoot != null
                ? visualSourceRoot.GetComponentsInChildren<Transform>(true)
                : FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Transform candidate in candidates)
            {
                if (!candidate.name.StartsWith(namePrefix) || candidate.IsChildOf(hinge)) continue;
                // The door is nested inside the imported environment prefab. A runtime
                // copy avoids prefab parenting restrictions and static-batched geometry.
                GameObject visual = Instantiate(candidate.gameObject, hinge, true);
                visual.name = candidate.name + " (animated)";
                foreach (Transform child in visual.GetComponentsInChildren<Transform>(true)) child.gameObject.isStatic = false;
                foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                if (rebuildImportedDoorCollision)
                {
                    foreach (Collider old in hinge.GetComponentsInChildren<Collider>(true)) old.enabled = false;
                    var mf = visual.GetComponentInChildren<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var b = mf.sharedMesh.bounds;
                        Vector3[] axes = { mf.transform.TransformVector(Vector3.right*b.size.x), mf.transform.TransformVector(Vector3.up*b.size.y), mf.transform.TransformVector(Vector3.forward*b.size.z) };
                        Vector3 width = Vector3.zero;
                        foreach(var axis in axes) if(Mathf.Abs(axis.y)<axis.magnitude*.3f && axis.magnitude>width.magnitude) width=axis;
                        float side = hinge==leftHinge?1f:-1f;
                        if(width.sqrMagnitude>.01f) visual.transform.rotation=Quaternion.FromToRotation(width.normalized,Vector3.right*side)*visual.transform.rotation;
                        var center=mf.transform.TransformPoint(b.center);
                        var target=new Vector3(hinge.position.x+side*width.magnitude*.5f,center.y,hinge.position.z);
                        visual.transform.position+=target-center;
                        var box=mf.gameObject.AddComponent<BoxCollider>();box.center=b.center;box.size=b.size;
                    }
                }
                visual.SetActive(true);
                candidate.gameObject.SetActive(false);
                return;
            }
        }

        private void Update()
        {
            if (player != null && !ManualControl)
            {
                Vector3 offset = player.position - transform.position;
                offset.y = 0f;
                float radius = activationDistance + (PlayerInRange ? releaseDistanceMargin : 0f);
                PlayerInRange = offset.sqrMagnitude <= radius * radius;
                if (PlayerInRange) lastPlayerNearAt = Time.time;
            }
            else PlayerInRange = false;
            // Completing an NPC entrance cannot close a door held by the encounter.
            bool open = HoldOpenForEncounter || OpenRequested
                || !ManualControl && (PlayerInRange || Time.time - lastPlayerNearAt < closeDelay);
            float target = open ? openAngle : 0f;
            float previousAngle = angle;
            angle = Mathf.MoveTowards(angle, target, rotationSpeed * Time.deltaTime);
            if (leftHinge != null) leftHinge.localRotation = leftClosed * Quaternion.Euler(0f, -angle, 0f);
            if (rightHinge != null) rightHinge.localRotation = rightClosed * Quaternion.Euler(0f, angle, 0f);
            if (doorAudio != null && (leftHinge != null || rightHinge != null))
                doorAudio.SyncMotion(previousAngle, angle, openAngle);
        }
    }
}
