using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FengChenInn;

// Read-only geometry audit for indoor/outdoor rain detection.
// Command: write "audit" into Temp/RainZone.command
[InitializeOnLoad]
public static class InnRainZoneAudit
{
    static InnRainZoneAudit()
    {
        EditorApplication.update += () =>
        {
            const string command = "Temp/RainZone.command";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(command)) return;
            string text = File.ReadAllText(command).Trim();
            File.Delete(command);
            if (text == "audit") Run();
            else if (text == "refresh") AssetDatabase.Refresh();
        };
    }

    public static void Run()
    {
        var lines = new List<string>();
        lines.Add("Scene: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
        Physics.SyncTransforms();
        float[] xs = { -6f, -3f, 0f, 3f, 6f };
        for (float z = -22f; z <= 12f; z += 1f)
        {
            foreach (float x in xs)
            {
                lines.Add("== x=" + x + " z=" + z);
                Sample(lines, new Vector3(x, 3.4f, z), 8f, "lower");
                Sample(lines, new Vector3(x, 7.6f, z), 4f, "upper");
            }
        }
        Directory.CreateDirectory("Logs");
        File.WriteAllLines("Logs/InnRainZoneAudit.txt", lines);
    }

    static void Sample(List<string> lines, Vector3 start, float drop, string label)
    {
        var down = Physics.RaycastAll(start, Vector3.down, drop, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            .Where(h => h.collider != null && !h.collider.isTrigger && h.normal.y > .45f)
            .OrderBy(h => h.distance).ToArray();
        if (down.Length == 0) return;
        var ground = down[0];
        string surface = FootstepSurface.Resolve(ground.collider).ToString();
        lines.Add("  [" + label + "] ground y=" + ground.point.y.ToString("F2") + " " + ground.collider.name
            + " surface=" + surface);
        var up = Physics.RaycastAll(ground.point + Vector3.up * 1.4f, Vector3.up, 9,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            .Where(h => h.collider != null && !h.collider.isTrigger && h.normal.y < -.35f)
            .OrderBy(h => h.distance).Take(3).ToArray();
        if (up.Length == 0) { lines.Add("    roof: none"); return; }
        foreach (var h in up)
            lines.Add("    roof d=" + h.distance.ToString("F2") + " " + h.collider.name
                + " ny=" + h.normal.y.ToString("F2"));
    }
}
