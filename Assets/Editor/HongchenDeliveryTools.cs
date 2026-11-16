using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Local, project-relative handoff diagnostics. No external service is required.
[InitializeOnLoad]
public static class HongchenDeliveryTools
{
    const string ScenePath = "Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity";
    const string Output = "Temp/HongchenDelivery";
    static HongchenDeliveryTools() { EditorApplication.update += Poll; }
    static void Poll()
    {
        const string commandPath = "Temp/HongchenDelivery.command";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(commandPath)) return;
        string command;
        try { command = File.ReadAllText(commandPath).Trim(); File.Delete(commandPath); }
        catch (IOException) { return; }
        Directory.CreateDirectory(Output);
        try
        {
            if (command == "stop") EditorApplication.isPlaying = false;
            else if (command == "configure") Configure();
            else if (command == "status") Status();
        }
        catch (Exception ex) { File.WriteAllText(Output + "/error.txt", ex.ToString()); Debug.LogException(ex); }
    }

    [MenuItem("Tools/红尘客栈/交付/检查当前场景")]
    public static void Status()
    {
        Directory.CreateDirectory(Output);
        var scene = SceneManager.GetActiveScene();
        int missing = scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<Transform>(true)
            .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)));
        var textures = Enumerable.Range(0,16).Select(i => Resources.Load<Texture2D>("MainMenuV2/asset_" + i.ToString("00"))).ToArray();
        File.WriteAllLines(Output + "/status.txt", new[] {
            "unity=" + Application.unityVersion, "scene=" + scene.path,
            "playing=" + EditorApplication.isPlaying, "sceneDirty=" + scene.isDirty,
            "missingScripts=" + missing, "menuTextures=" + textures.Count(t => t != null) + "/16",
            "buildScenes=" + string.Join(",", EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path))
        });
    }

    [MenuItem("Tools/红尘客栈/交付/将 Stage12 设为构建入口")]
    public static void Configure()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("先停止 Play Mode。");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) throw new FileNotFoundException(ScenePath);
        if (SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("先打开 Stage12 场景。");
        if (!EditorSceneManager.SaveOpenScenes()) throw new IOException("场景保存失败。");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Status();
    }
}
