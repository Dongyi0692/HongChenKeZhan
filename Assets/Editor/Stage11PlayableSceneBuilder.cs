using FengChenInn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class Stage11PlayableSceneBuilder
{
    private const string SourceScene = "Assets/_Game/Scenes/InnDuel.unity";
    private const string PlayableScene = "Assets/_Game/Scenes/InnDuel_Stage11.unity";
    private const string ModelPath = "Assets/_Game/Art/Stage11Final/Stage11_Final.glb";

    [MenuItem("Tools/风尘客栈/生成 Stage11 可试玩场景")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PlayableScene) != null)
        {
            Debug.LogWarning("Stage11 可试玩场景已存在，未覆盖：" + PlayableScene);
            return;
        }

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError("Stage11 模型尚未导入完成：" + ModelPath);
            return;
        }

        var source = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
        if (!EditorSceneManager.SaveScene(source, PlayableScene, true))
        {
            Debug.LogError("无法复制可试玩场景：" + PlayableScene);
            return;
        }

        var scene = EditorSceneManager.OpenScene(PlayableScene, OpenSceneMode.Single);
        GameObject environment = GameObject.Find("FengChen Inn - Stage 5");
        Transform oldVisual = environment != null ? environment.transform.Find("FengChenInn_Stage5") : null;
        if (oldVisual == null)
        {
            Debug.LogError("找不到旧客栈模型，未修改场景。");
            return;
        }

        var newVisual = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
        newVisual.name = "Stage11 Final - Visuals";
        newVisual.transform.SetParent(environment.transform, false);
        newVisual.transform.localPosition = oldVisual.localPosition;
        newVisual.transform.localRotation = oldVisual.localRotation;
        newVisual.transform.localScale = oldVisual.localScale;

        // The old model owns three mesh colliders. Keep it alive for the original
        // movement and placement rules, but show only the final Stage11 model.
        int hiddenRenderers = 0;
        foreach (Renderer renderer in oldVisual.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            hiddenRenderers++;
        }

        InnEntranceDoors doors = Object.FindFirstObjectByType<InnEntranceDoors>();
        if (doors == null)
        {
            Debug.LogError("找不到入口门脚本；已生成的场景尚未保存。");
            return;
        }
        doors.visualSourceRoot = newVisual.transform;
        EditorUtility.SetDirty(doors);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Stage11 可试玩场景已生成：{PlayableScene}。隐藏旧模型 Renderer {hiddenRenderers} 个，保留旧碰撞与玩法。");
    }
}
