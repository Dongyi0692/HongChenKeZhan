using System;
using System.IO;
using System.Linq;
using FengChenInn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RoninEnemyVerification
{
    const string Flag = "FengChenInn.RoninVerification";
    static double start;
    static bool began;
    static int errors;

    public static void Run()
    {
        SessionState.SetBool(Flag, true);
        EditorSceneManager.OpenScene("Assets/_Game/Scenes/InnDuel.unity");
        EditorApplication.playModeStateChanged -= Changed;
        EditorApplication.playModeStateChanged += Changed;
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Bootstrap()
    {
        if (!SessionState.GetBool(Flag, false)) return;
        EditorApplication.playModeStateChanged -= Changed;
        EditorApplication.playModeStateChanged += Changed;
    }

    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        start = EditorApplication.timeSinceStartup;
        Application.logMessageReceived += OnLog;
        EditorApplication.update += Poll;
    }

    static void OnLog(string condition, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error) errors++;
    }

    static void Poll()
    {
        double elapsed = EditorApplication.timeSinceStartup - start;
        var duel = UnityEngine.Object.FindFirstObjectByType<DuelEncounter>();
        if (!began && elapsed > 1.5 && duel != null) { duel.Begin(); began = true; }
        if (elapsed < 4) return;
        EditorApplication.update -= Poll;
        try
        {
            if (duel == null || duel.Opponent == null) throw new Exception("Opponent was not created.");
            var visual = duel.Opponent.GetComponentInChildren<RocketboxVisual>();
            if (visual == null || visual.weapon == null || !visual.keepSwordDrawn) throw new Exception("Independent sword or opponent pose is missing.");
            var skin = visual.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin == null || skin.bones.Length < 15 || skin.bones.Any(b => b == null)) throw new Exception("Skin or bones are missing.");
            var renderer = visual.weapon.GetComponentInChildren<Renderer>();
            if (renderer == null) throw new Exception("Sword has no renderer.");
            foreach (var other in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (!other.transform.IsChildOf(duel.Opponent)) other.enabled = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white * 0.65f;
            var cam = new GameObject("RoninCheckCamera").AddComponent<Camera>();
            cam.transform.position = duel.Opponent.position + duel.Opponent.forward * 2.8f + Vector3.up * 1.35f;
            cam.transform.LookAt(duel.Opponent.position + Vector3.up * 0.9f);
            cam.fieldOfView = 38;
            var rt = new RenderTexture(800, 800, 24); cam.targetTexture = rt; cam.Render();
            var previous = RenderTexture.active; RenderTexture.active = rt;
            var image = new Texture2D(800, 800, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 800, 800), 0, 0); image.Apply();
            File.WriteAllBytes("Builds/RoninEnemy/ronin_check.png", image.EncodeToPNG());
            RenderTexture.active = previous; cam.targetTexture = null; rt.Release();
            Debug.Log("RONIN_PLAY_CHECK bones=" + skin.bones.Length + " sword=" + visual.weapon.name + " errors=" + errors + " state=" + duel.State);
        }
        catch (Exception e) { errors++; Debug.LogException(e); }
        SessionState.SetBool(Flag, false);
        EditorApplication.Exit(errors == 0 ? 0 : 2);
    }
}
