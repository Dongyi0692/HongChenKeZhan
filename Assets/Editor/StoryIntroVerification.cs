using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Exercises the actual Button callbacks in Play Mode; never saves runtime scene changes.
[InitializeOnLoad]
public static class StoryIntroVerification
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Command = "Temp/StoryIntroVerification.command";
    static IEnumerator<double> run;
    static double nextStep;
    static string output;
    static readonly List<string> results = new();
    static int callbacks, sceneLoads, errors;
    static StoryIntroPanel tracked;
    static Action original;
    static StoryIntroVerification() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (File.Exists(Command))
            {
                string command;
                try { command = File.ReadAllText(Command).Trim(); File.Delete(Command); }
                catch (IOException) { return; }
                if (command == "verify") Run();
                else if (command == "play") EditorApplication.isPlaying = true;
                else if (command == "stop") EditorApplication.isPlaying = false;
                else if (command == "status")
                {
                    Directory.CreateDirectory("Logs/StoryIntroVerification");
                    var ui = UnityEngine.Object.FindFirstObjectByType<InnInterface>();
                    File.WriteAllText("Logs/StoryIntroVerification/status.txt", $"playing={EditorApplication.isPlaying}\nview={(ui ? View(ui) : "none")}\nscreen={Screen.width}x{Screen.height}\nscene={SceneManager.GetActiveScene().path}\n");
                }
            }
            if (run == null || EditorApplication.timeSinceStartup < nextStep) return;
            if (!EditorApplication.isPlaying) throw new Exception("Play Mode ended before verification finished.");
            if (run.MoveNext()) nextStep = EditorApplication.timeSinceStartup + run.Current;
            else { run.Dispose(); run = null; Cleanup(); }
        }
        catch (Exception ex)
        {
            results.Add("FAIL: " + ex);
            if (output != null) File.WriteAllLines(output + "/report.txt", results);
            run?.Dispose(); run = null; Cleanup(); Debug.LogException(ex);
        }
    }
    [MenuItem("Tools/红尘客栈/序章/验证交互与1920布局（运行中）")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying || run != null) throw new InvalidOperationException("Enter Play Mode first; do not start a second verification.");
        output = "Logs/StoryIntroVerification/" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(output);
        results.Clear(); callbacks = sceneLoads = errors = 0;
        SceneManager.sceneLoaded += SceneLoaded;
        Application.logMessageReceived += Message;
        run = Verify(); nextStep = 0;
    }
    static void SceneLoaded(Scene scene, LoadSceneMode mode) { sceneLoads++; }
    static void Message(string text, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }
    static void Cleanup()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        Application.logMessageReceived -= Message;
        if (tracked && original != null) typeof(StoryIntroPanel).GetField("onComplete", Private).SetValue(tracked, original);
        tracked = null; original = null;
    }
    static string View(InnInterface ui) => typeof(InnInterface).GetField("view", Private).GetValue(ui).ToString();
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        results.Add("PASS: " + name);
        File.WriteAllLines(output + "/report.txt", results);
    }
    static void ResetTitle(InnInterface ui)
    { typeof(InnInterface).GetMethod("Restart", Private).Invoke(ui, new object[] { true }); }
    static void StartButton()
    {
        UnityEngine.Object.FindFirstObjectByType<MainMenuV2>().GetComponentsInChildren<Button>().First().onClick.Invoke();
    }
    static void Capture(string name) { ScreenCapture.CaptureScreenshot(output + "/" + name + ".png"); }
    static IEnumerator<double> Verify()
    {
        var ui = UnityEngine.Object.FindFirstObjectByType<InnInterface>();
        Check(ui != null, "Existing InnInterface present");
        Check(Screen.width == 1920 && Screen.height == 1080, "Game View is 1920x1080");
        string scene = SceneManager.GetActiveScene().path;
        Check(scene.EndsWith("InnDuel_Stage12_CollisionLighting.unity"), "Existing Stage12 entry scene");
        ResetTitle(ui); yield return .4;
        StartButton(); StartButton(); StartButton(); yield return .65;
        tracked = UnityEngine.Object.FindFirstObjectByType<StoryIntroPanel>();
        Check(tracked && View(ui) == "Prologue", "Start button opens prologue once");
        original = (Action)typeof(StoryIntroPanel).GetField("onComplete", Private).GetValue(tracked);
        typeof(StoryIntroPanel).GetField("onComplete", Private).SetValue(tracked, (Action)(() => { callbacks++; original(); }));
        Check(Time.timeScale == 0 && !UnityEngine.Object.FindFirstObjectByType<PrototypeInput>().enabled, "World and player input paused during prologue");
        var scaler = ui.GetComponent<CanvasScaler>();
        Check(scaler.referenceResolution == new Vector2(1920, 1080) && scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.MatchWidthOrHeight && Mathf.Approximately(scaler.matchWidthOrHeight, .5f), "1920x1080 canvas / match 0.5");
        var layout = JsonUtility.FromJson<StoryIntroPanel.Layout>(Resources.Load<TextAsset>("StoryIntro/布局清单").text);
        var images = tracked.GetComponentsInChildren<Image>();
        foreach (var item in layout.sprites)
        {
            var name = Path.GetFileNameWithoutExtension(item.file);
            var image = images.Single(i => i.name == name);
            Check(image.sprite && image.rectTransform.anchoredPosition == new Vector2(item.anchoredPosition_from_center_px[0], item.anchoredPosition_from_center_px[1]) && image.rectTransform.sizeDelta == new Vector2(item.rect_top_left_px[2], item.rect_top_left_px[3]) && image.sprite.rect.size == image.rectTransform.sizeDelta, "Layout and native sprite size: " + name);
        }
        Check(tracked.SourceText == Resources.Load<TextAsset>("StoryIntro/序章原文").text, "Canonical text source preserved");
        Check(!tracked.FullyRevealed, "Opening is still animating");
        tracked.ContinueButton.onClick.Invoke(); tracked.ContinueButton.onClick.Invoke(); tracked.SkipButton.onClick.Invoke();
        Check(tracked.FullyRevealed && View(ui) == "Prologue" && callbacks == 0, "Early Continue reveals all; same-frame repeats cannot dismiss");
        Check(tracked.GetComponentsInChildren<CanvasGroup>().All(g => g.alpha == 1), "Early reveal includes heading, all six paragraphs and signature");
        Capture("01_reveal_all"); yield return .5;
        tracked.ContinueButton.onClick.Invoke();
        for (int i = 0; i < 10; i++) { tracked.ContinueButton.onClick.Invoke(); tracked.SkipButton.onClick.Invoke(); }
        yield return .7;
        Check(callbacks == 1 && View(ui) == "Play" && Time.timeScale == 1, "Continue enters existing game exactly once");
        Check(scaler.referenceResolution == new Vector2(1600, 900) && scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand, "Original HUD scaling restored");
        Check(SceneManager.GetActiveScene().path == scene && sceneLoads == 0, "Existing scene reused without new loads");
        Capture("02_game_after_continue"); yield return .3;
        ResetTitle(ui); yield return .4; StartButton(); yield return .65;
        Check(!tracked.FullyRevealed && !tracked.IsLeaving, "Returning to title resets prologue");
        tracked.SkipButton.onClick.Invoke();
        for (int i = 0; i < 10; i++) { tracked.SkipButton.onClick.Invoke(); tracked.ContinueButton.onClick.Invoke(); }
        yield return .7;
        Check(callbacks == 2 && View(ui) == "Play" && Time.timeScale == 1, "Skip during animation enters same game once");
        ResetTitle(ui); yield return .4; StartButton(); yield return 2.2;
        var paragraphs = tracked.GetComponentsInChildren<CanvasGroup>().Where(g => g.name.StartsWith("Paragraph ")).ToArray();
        Check(paragraphs.Length == 6 && paragraphs[0].alpha > 0 && paragraphs[5].alpha == 0, "Six paragraphs fade in sequentially");
        Check(paragraphs.Zip(paragraphs.Skip(1), (a,b) => a.alpha >= b.alpha).All(x => x), "Earlier paragraphs remain visible");
        Capture("03_sequential_fade"); yield return 5.8;
        Check(tracked.FullyRevealed && paragraphs.All(p => p.alpha == 1), "Natural animation finishes with entire text visible");
        yield return 1;
        Check(View(ui) == "Prologue" && callbacks == 2, "Completed text stays for reading, no automatic transition");
        Check(errors == 0, "No runtime errors during verification");
        Check(sceneLoads == 0, "No duplicate scene load in any interaction path");
        Capture("04_prologue_1920x1080");
        results.Add("FINISHED: left prologue visible for visual review. Button callbacks exercised in actual Play Mode; no runtime changes saved.");
        File.WriteAllLines(output + "/report.txt", results);
        File.WriteAllText("Logs/StoryIntroVerification/latest.txt", output);
        yield return .5;
    }
}
