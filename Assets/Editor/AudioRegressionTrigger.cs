using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Small command relay so the existing Play Mode verifications can be re-run from the shell
// without touching their code: write "steps", "menu" or "ui" into Temp/AudioRegression.command.
// If the editor is already playing it first leaves Play Mode, so each run starts from a clean
// scene load instead of inheriting the previous test's state.
[InitializeOnLoad]
public static class AudioRegressionTrigger
{
    const string Command = "Temp/AudioRegression.command";
    static string pending;
    static IEnumerator<double> probe;
    static double nextProbe;

    static AudioRegressionTrigger()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("UiProbe", false))
            {
                SessionState.SetBool("UiProbe", false);
                probe = ProbeUi(); nextProbe = EditorApplication.timeSinceStartup + 4;
            }
        };
    }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists(Command))
        {
            pending = File.ReadAllText(Command).Trim();
            File.Delete(Command);
            if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
        }
        if (probe != null)
        {
            if (EditorApplication.timeSinceStartup < nextProbe) return;
            if (probe.MoveNext()) nextProbe = EditorApplication.timeSinceStartup + probe.Current;
            else probe = null;
            return;
        }
        if (pending == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string action = pending;
        pending = null;
        if (action == "steps") FootstepVerification.StartTest();
        else if (action == "menu") MenuAudioVerification.StartTest();
        else if (action == "ui") { SessionState.SetBool("UiProbe", true); EditorApplication.isPlaying = true; }
    }

    // Dumps the settings panel slider inventory so the count reported by the menu audio
    // verification can be attributed to the actual UI hierarchy.
    static IEnumerator<double> ProbeUi()
    {
        yield return 6;
        var ui = UnityEngine.Object.FindFirstObjectByType<InnInterface>();
        var lines = new List<string>();
        if (!ui) { File.WriteAllLines("Logs/UiSliderProbe.txt", new[] { "no InnInterface" }); yield break; }
        typeof(InnInterface).GetMethod("StartGame", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(ui, null);
        yield return .5;
        typeof(InnInterface).GetMethod("OpenSettings", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(ui, null);
        yield return 1f;
        var sliders = ui.GetComponentsInChildren<Slider>(true);
        lines.Add("active sliders=" + ui.GetComponentsInChildren<Slider>().Length + " all sliders=" + sliders.Length);
        foreach (var s in sliders)
            lines.Add("  " + Path(s.transform) + " active=" + s.gameObject.activeInHierarchy
                      + " value=" + s.value.ToString("F2"));
        File.WriteAllLines("Logs/UiSliderProbe.txt", lines);
        EditorApplication.isPlaying = false;
    }

    static string Path(Transform t)
    {
        var chain = new List<string>();
        for (var c = t; c; c = c.parent) chain.Add(c.name);
        chain.Reverse();
        return string.Join("/", chain.ToArray());
    }
}
