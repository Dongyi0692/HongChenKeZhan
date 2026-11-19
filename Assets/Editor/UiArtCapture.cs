using System;
using System.IO;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;

public static class UiArtCapture
{
    [MenuItem("红尘客栈/记录当前 UI 截图")]
    public static void CaptureCurrent()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("UI_ART_CAPTURE: Enter Play Mode first.");
            return;
        }

        var ui = UnityEngine.Object.FindFirstObjectByType<InnInterface>();
        if (ui == null)
        {
            Debug.LogWarning("UI_ART_CAPTURE: InnInterface not found.");
            return;
        }

        var field = typeof(InnInterface).GetField("view", BindingFlags.Instance | BindingFlags.NonPublic);
        string view = field != null ? field.GetValue(ui).ToString() : "Unknown";
        string directory = Path.Combine(Application.dataPath, "..", "Docs", "UI正式版优化_2026-11-18", "After");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, DateTime.Now.ToString("HHmmss") + "_" + view + ".png");
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log("UI_ART_CAPTURE: " + path);
    }
}
