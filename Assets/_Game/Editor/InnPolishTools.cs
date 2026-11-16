using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InnPolishTools
{
    [Serializable] public class Item
    {
        public string path, type;
        public Vector3 center, size, position, rotation;
        public bool enabled, trigger;
    }
    [Serializable] public class Inspection
    {
        public string scene;
        public bool playing, dirty;
        public List<Item> colliders = new();
    }
    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

    [Unity.Pipeline.Commands.CliCommand("innpolish", "prepare_play", "Enter live exploration for isolated component checks")]
    public static string PreparePlay()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        var ui = UnityEngine.Object.FindFirstObjectByType<FengChenInn.InnInterface>();
        const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(FengChenInn.InnInterface).GetMethod("Restart", hidden).Invoke(ui, new object[] { true });
        var play = Enum.Parse(typeof(FengChenInn.InnInterface).GetNestedType("View", System.Reflection.BindingFlags.NonPublic), "Play");
        typeof(FengChenInn.InnInterface).GetMethod("Show", hidden).Invoke(ui, new[] { play });
        return "Exploration running; no scene saved.";
    }

    [Unity.Pipeline.Commands.CliCommand("innpolish", "inspect", "Inspect existing stairs and nearby collision; no scene changes")]
    public static string Inspect()
    {
        var scene = SceneManager.GetActiveScene();
        var report = new Inspection { scene = scene.path, playing = Application.isPlaying, dirty = scene.isDirty };
        foreach (var c in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string path = PathOf(c.transform);
            if (path.IndexOf("Stair", StringComparison.OrdinalIgnoreCase) < 0 &&
                path.IndexOf("Collision", StringComparison.OrdinalIgnoreCase) < 0 &&
                path.IndexOf("Ramp", StringComparison.OrdinalIgnoreCase) < 0) continue;
            report.colliders.Add(new Item { path = path, type = c.GetType().Name,
                center = c.bounds.center, size = c.bounds.size, position = c.transform.position,
                rotation = c.transform.eulerAngles, enabled = c.enabled, trigger = c.isTrigger });
        }
        return JsonUtility.ToJson(report);
    }

    [Unity.Pipeline.Commands.CliCommand("innpolish", "install_stair_surface", "Attach reversible stair walking surface to the existing InnDuel scene")]
    public static string InstallStairSurface()
    {
        var scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/_Game/Scenes/InnDuel.unity")
            throw new InvalidOperationException("Open existing InnDuel in edit mode first.");
        if (scene.isDirty) throw new InvalidOperationException("Scene has unsaved changes; preserve them before installing.");
        var root = GameObject.Find("FengChen Inn - Stage 5/Architecture Collision");
        if (root == null) throw new InvalidOperationException("Existing architecture collision root missing.");
        if (root.GetComponent<FengChenInn.InnStairTraversal>() == null)
            Undo.AddComponent<FengChenInn.InnStairTraversal>(root);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Attached stair traversal to existing architecture; visual steps and furniture unchanged.";
    }
}
