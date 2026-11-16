using System;
using System.IO;
using FengChenInn;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DuelDemoSetup
{
    private const string Scene = "Assets/_Game/Scenes/InnDuel.unity";
    public const string BuildPath = "Builds/ReportDemo/FengChenInn.exe";

    [MenuItem("FengChenInn/Install Duel Demo")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        if (EditorSceneManager.GetActiveScene().path != Scene)
        {
            if (EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the open scene before installing the duel.");
            EditorSceneManager.OpenScene(Scene);
        }
        var fighter = UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>();
        if (fighter == null || fighter.skinnedVisual == null) throw new InvalidOperationException("Duel scene player rig missing.");
        if (fighter.GetComponent<DuelEncounter>() == null) fighter.gameObject.AddComponent<DuelEncounter>();
        var controls = GameObject.Find("Controls")?.GetComponent<TMP_Text>();
        if (controls != null)
        {
            controls.text = "F  Start duel   WASD  Move   Mouse  Look   LMB / RMB  Light / Heavy\nQ  Guard   L-SHIFT  Dodge   SPACE  Jump   L-CTRL  Crouch   R  Reset   ESC  Mouse";
            EditorUtility.SetDirty(controls);
        }
        EditorSceneManager.MarkSceneDirty(fighter.gameObject.scene);
        if (!EditorSceneManager.SaveScene(fighter.gameObject.scene)) throw new IOException("Could not save the duel scene.");
    }

    [MenuItem("FengChenInn/Build Windows Duel Demo")]
    public static void BuildWindows()
    {
        Install();
        Directory.CreateDirectory(Path.GetDirectoryName(BuildPath));
        var report = BuildPipeline.BuildPlayer(new[] { Scene }, BuildPath, BuildTarget.StandaloneWindows64, BuildOptions.None);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Windows build failed: " + report.summary.result);
        Debug.Log("DUEL_WINDOWS_BUILD_OK " + Path.GetFullPath(BuildPath));
    }
}
