using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FengChenInn.EditorTools
{
    // Deterministic attack-phase inspector: re-simulates each attack from t=0 in
    // fixed 1/60 steps, calls RocketboxVisual.Apply at every step, then snapshots
    // the main camera. Play-mode updates do not interleave because the whole run
    // happens inside one synchronous menu invocation.
    public static class AttackPhaseCapture
    {
        private static readonly (string label, bool heavy, float time)[] Samples =
        {
            ("light_w06", false, 0.06f),
            ("light_w17", false, 0.17f),
            ("light_s22", false, 0.22f),
            ("light_s28", false, 0.28f),
            ("light_s34", false, 0.34f),
            ("light_f42", false, 0.42f),
            ("light_r55", false, 0.55f),
            ("light_end", false, 0.64f),
            ("heavy_w15", true, 0.15f),
            ("heavy_w42", true, 0.42f),
            ("heavy_s50", true, 0.50f),
            ("heavy_s55", true, 0.55f),
            ("heavy_s63", true, 0.63f),
            ("heavy_f75", true, 0.75f),
            ("heavy_r95", true, 0.95f),
            ("heavy_end", true, 1.18f),
            ("idle_ref", false, -1f),
        };

        [MenuItem("FengChenInn/Capture Attack Phases")]
        public static void Capture()
        {
            PrototypeFighter fighter = Object.FindFirstObjectByType<PrototypeFighter>();
            if (fighter == null)
            {
                Debug.LogError("[AttackPhaseCapture] No PrototypeFighter in open scenes.");
                return;
            }
            RocketboxVisual visual = fighter.skinnedVisual != null
                ? fighter.skinnedVisual
                : Object.FindFirstObjectByType<RocketboxVisual>();
            if (visual == null)
            {
                Debug.LogError("[AttackPhaseCapture] No RocketboxVisual found.");
                return;
            }
            Camera cam = new GameObject("AttackPhaseCaptureCam").AddComponent<Camera>();
            cam.CopyFrom(Camera.main != null ? Camera.main : cam);
            cam.transform.SetParent(fighter.transform, true);

            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Verification", "attack_frames");
            Directory.CreateDirectory(outDir);

            var stateProp = typeof(PrototypeFighter).GetProperty("State");
            var timeField = typeof(PrototypeFighter).GetField("actionTime", BindingFlags.NonPublic | BindingFlags.Instance);
            var drawField = typeof(RocketboxVisual).GetField("drawWeight", BindingFlags.NonPublic | BindingFlags.Instance);
            var holdField = typeof(RocketboxVisual).GetField("holdOverride", BindingFlags.NonPublic | BindingFlags.Instance);
            if (stateProp == null || timeField == null || drawField == null || holdField == null)
            {
                Debug.LogError("[AttackPhaseCapture] Reflection targets missing.");
                return;
            }

            const float dt = 1f / 60f;
            var lowerField = typeof(RocketboxVisual).GetField("lowerAmount", BindingFlags.NonPublic | BindingFlags.Instance);
            var diag = new System.Text.StringBuilder();
            diag.AppendLine("label\tstate\tisCrouch\tlower\tdrawWeight\thandWorld\tweaponWorld\tgripDist\tfootL\tfootR");
            foreach ((string label, bool heavy, float time) in Samples)
            {
                fighter.ResetPose();
                // controller.isGrounded only refreshes inside Move(); nudge it so
                // the re-simulation doesn't fall into the airborne pose branch.
                fighter.GetComponent<CharacterController>().Move(Vector3.down * 0.05f);
                fighter.MoveInput = Vector2.zero;
                fighter.CrouchRequested = false;
                fighter.GuardRequested = false;
                stateProp.SetValue(fighter, PrototypeFighter.ActionState.Ready);
                timeField.SetValue(fighter, 0f);
                holdField.SetValue(visual, true);
                drawField.SetValue(visual, 1f);

                if (time >= 0f)
                {
                    stateProp.SetValue(fighter, heavy ? PrototypeFighter.ActionState.HeavyAttack : PrototypeFighter.ActionState.LightAttack);
                    for (float t = dt; t <= time + 0.0001f; t += dt)
                    {
                        float step = Mathf.Min(t, time) - (t - dt);
                        float now = Mathf.Min(t, time);
                        timeField.SetValue(fighter, now);
                        visual.Apply(fighter, Vector3.zero, now, 0f, step);
                    }
                }
                else
                {
                    // Idle reference: hold a stable sheathed-adjacent carried pose.
                    for (int i = 0; i < 90; i++)
                        visual.Apply(fighter, Vector3.zero, 0f, 0f, dt);
                }

                Snap(cam, Path.Combine(outDir, label + ".png"), fighter.transform);
                Vector3 hand = visual.rightArm.end.position;
                Vector3 grip = visual.weapon.position + visual.weapon.forward * 0.09f;
                diag.AppendLine(string.Format("{0}\t{1}\t{2}\t{3:F3}\t{4:F2}\t{5}\t{6}\t{7:F3}\t{8:F2}\t{9:F2}\tpelvisY={10:F3}\tgrounded={11}",
                    label, fighter.State, fighter.IsCrouching,
                    lowerField != null ? (float)lowerField.GetValue(visual) : -1f,
                    (float)drawField.GetValue(visual),
                    hand.ToString("F3"), visual.weapon.position.ToString("F3"),
                    Vector3.Distance(hand, grip),
                    visual.leftLeg.end.position.ToString("F2"), visual.rightLeg.end.position.ToString("F2"),
                    visual.pelvis.position.y, fighter.IsGrounded));
                Debug.Log("[AttackPhaseCapture] saved " + label);
            }
            Debug.Log("[AttackPhaseCapture] done -> " + outDir);
            File.WriteAllText(Path.Combine(outDir, "diag.txt"), diag.ToString());
            Object.DestroyImmediate(cam.gameObject);
        }

        // Front-right 3/4 close view: chest height, far enough to keep the full
        // blade in frame, close enough to judge wrist and grip alignment.
        private static void Snap(Camera cam, string path, Transform fighter)
        {
            Transform t = cam.transform;
            Vector3 focus = fighter.position + Vector3.up * 1.15f;
            Vector3 offset = new Vector3(1.9f, 0.55f, 1.9f);
            t.position = focus + offset;
            t.rotation = Quaternion.LookRotation((focus - t.position).normalized, Vector3.up);
            const int w = 960, h = 540;
            RenderTexture rt = new RenderTexture(w, h, 24);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
            tex.Apply();
            cam.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
        }
    }
}
