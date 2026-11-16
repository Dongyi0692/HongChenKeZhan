using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Headless entry point: Unity -batchmode -projectPath <proj> -executeMethod BatchVerificationRun.Run
// Opens ActionLab, enters Play Mode, runs ActionVerification and exits 0 (all pass) or 2 (failures).
public static class BatchVerificationRun
{
    private const string RunFlag = "FengChenInn.BatchVerify";
    private const string Scene = "Assets/_Game/Scenes/ActionLab.unity";

    public static void Run()
    {
        SessionState.SetBool(RunFlag, true);
        EditorSceneManager.OpenScene(Scene);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void Bootstrap()
    {
        // Static state is lost on the play-mode domain reload; SessionState survives it.
        if (!SessionState.GetBool(RunFlag, false)) return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode) return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        ActionVerification.Start();
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (!ActionVerification.Completed) return;
        EditorApplication.update -= Poll;
        SessionState.SetBool(RunFlag, false);
        int failures = ActionVerification.FailureCount;
        Debug.Log("BATCH_VERIFICATION_DONE failures=" + failures);
        // Exit straight from Play Mode: routing through ExitPlaymode first hangs batch mode.
        EditorApplication.Exit(failures == 0 ? 0 : 2);
    }
}

