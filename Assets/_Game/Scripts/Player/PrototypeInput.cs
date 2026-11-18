using UnityEngine;
using UnityEngine.InputSystem;

namespace FengChenInn
{
    [RequireComponent(typeof(PrototypeFighter))]
    [DefaultExecutionOrder(-10)]
    public sealed class PrototypeInput : MonoBehaviour
    {
        private PrototypeFighter fighter;
        private DuelEncounter duel;
        private OrbitCamera orbit;
        public bool InterfaceControlsDuel { get; set; }
        public bool StoryAllowsCombat { get; set; } = true;
        private void Awake()
        {
            fighter = GetComponent<PrototypeFighter>();
            duel = GetComponent<DuelEncounter>();
        }
        private void Update()
        {
            Keyboard keys = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keys == null || Time.timeScale <= 0f) { ClearInput(); return; }
            if (!InterfaceControlsDuel && keys.escapeKey.wasPressedThisFrame) SetCursor(false);
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                // Capturing the cursor must not also swing the sword.
                SetCursor(true);
                ClearInput();
                return;
            }
            if (Cursor.lockState != CursorLockMode.Locked) { ClearInput(); return; }
            fighter.MoveInput = new Vector2((keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0),
                (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0));
            fighter.RunRequested = fighter.MoveInput.sqrMagnitude > .001f;
            fighter.CrouchRequested = false;
            fighter.GuardRequested = StoryAllowsCombat && keys.qKey.isPressed;
            if (orbit == null && Camera.main != null) orbit = Camera.main.GetComponent<OrbitCamera>();
            if (StoryAllowsCombat && orbit != null)
            {
                if (mouse != null && mouse.middleButton.wasPressedThisFrame) orbit.ToggleTargetLock();
                else if (keys.tabKey.wasPressedThisFrame) orbit.CycleTargetLock();
            }
            if (!InterfaceControlsDuel && keys.rKey.wasPressedThisFrame)
            {
                fighter.ResetPose();
                if (duel != null) duel.ResetEncounter();
                return;
            }
            if (StoryAllowsCombat && keys.leftShiftKey.wasPressedThisFrame) fighter.Dodge();
            else if (keys.spaceKey.wasPressedThisFrame) fighter.Jump();
            else if (StoryAllowsCombat && keys.fKey.wasPressedThisFrame) fighter.Skill();
            else if (StoryAllowsCombat && mouse != null && mouse.rightButton.wasPressedThisFrame) fighter.Attack(true);
            else if (StoryAllowsCombat && mouse != null && mouse.leftButton.wasPressedThisFrame) fighter.Attack(false);
        }
        public void ClearInput() { if (fighter != null) fighter.ClearInput(); }
        public static void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
        private void OnDisable() => ClearInput();
        private void OnApplicationFocus(bool focus)
        {
            if (!focus) { ClearInput(); SetCursor(false); }
        }
    }
}
