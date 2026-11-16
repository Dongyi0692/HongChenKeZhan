using UnityEngine;

namespace FengChenInn
{
    // Authored scene references; the runtime-created interface reads these once.
    public sealed class InnStoryAnchors : MonoBehaviour
    {
        public Transform ledger;
        public Transform rearDoor;
        public Transform rearExit;
        public InnRearExitDoor rearDoorController;
        [Min(.5f)] public float ledgerInteractionDistance = 1.8f;
        [Min(.5f)] public float rearDoorDistance = 2f;
        [Min(.5f)] public float rearExitDistance = 1.3f;

        public bool RearDoorOpen => rearDoorController != null && rearDoorController.OpenRequested;
        public bool OpenRearDoor() => SetRearDoorOpen(true);
        public bool SetRearDoorOpen(bool open)
        {
            if (rearDoorController == null) return false;
            rearDoorController.OpenRequested = open;
            return true;
        }
    }
}
