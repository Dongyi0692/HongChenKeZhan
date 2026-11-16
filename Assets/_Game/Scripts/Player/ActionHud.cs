using TMPro;
using UnityEngine;

namespace FengChenInn
{
    public sealed class ActionHud : MonoBehaviour
    {
        public PrototypeFighter fighter;
        public TMP_Text statusText;
        private DuelEncounter duel;
        private void Start()
        {
            if (fighter == null) return;
            duel = fighter.GetComponent<DuelEncounter>();
            if (duel == null || gameObject.scene.name != "InnDuel") return;
            foreach (Transform child in transform) child.gameObject.SetActive(false);
            gameObject.AddComponent<InnInterface>().Initialize(fighter, duel);
            enabled = false;
        }
        private void Update()
        {
            if (fighter == null || statusText == null) return;
            if (duel != null)
            {
                string state = duel.State switch
                {
                    DuelEncounter.Phase.Waiting => "F: START DUEL",
                    DuelEncounter.Phase.Windup => "OPPONENT: WINDUP",
                    DuelEncounter.Phase.Slash => "OPPONENT: SLASH",
                    DuelEncounter.Phase.Recover => "OPPONENT: OPENING",
                    DuelEncounter.Phase.Victory => "VICTORY  /  R: RESTART",
                    DuelEncounter.Phase.Defeat => "DEFEAT  /  R: RESTART",
                    _ => "DUEL IN PROGRESS"
                };
                statusText.text = $"YOU {duel.PlayerHealth}/{DuelEncounter.PlayerMax}    RIVAL {duel.OpponentHealth}/{DuelEncounter.OpponentMax}\n{state}"
                    + (string.IsNullOrEmpty(duel.Feedback) ? "" : $"    {duel.Feedback}")
                    + (Cursor.lockState == CursorLockMode.Locked ? "" : "\nClick the game to capture mouse");
                return;
            }
            statusText.text = "ACTION LAB  /  " + fighter.Status.ToUpperInvariant()
                + (fighter.IsAttackActive ? "  [STRIKE]" : "")
                + (fighter.IsInvulnerable ? "  [EVADE]" : "")
                + (fighter.skinnedVisual != null && fighter.skinnedVisual.IsSwordSheathed ? "  [SHEATHED]" : "")
                + "\n" + (Cursor.lockState == CursorLockMode.Locked ? "ESC releases mouse" : "Click the game to capture mouse");
        }
    }
}

