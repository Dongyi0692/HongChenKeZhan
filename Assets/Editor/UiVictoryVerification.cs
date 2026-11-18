using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Play Mode audit for the UI cues and, above all, for WHEN Victory B is allowed to
// sound: never on the mid-story elite win, exactly once on the true ending card.
[InitializeOnLoad]
public static class UiVictoryVerification
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator<double> run;
    static double next;
    static readonly List<string> lines = new();
    static int errors;
    static InnInterface ui;
    static UiVictoryAudio audio;
    static bool background;
    static bool pendingRestart;
    static UiVictoryVerification()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("UiVictoryTest", false))
            { SessionState.SetBool("UiVictoryTest", false); Begin(); }
        };
    }
    [MenuItem("Tools/红尘客栈/音频/验证界面与胜利 (Play Mode)")]
    public static void StartTest()
    {
        // A running play session keeps the assemblies it started with, so a script edit needs a
        // fresh domain before the new behaviour can be measured.
        if (EditorApplication.isPlaying) { pendingRestart = true; EditorApplication.isPlaying = false; return; }
        SessionState.SetBool("UiVictoryTest", true);
        EditorApplication.isPlaying = true;
    }
    static void Begin()
    {
        lines.Clear(); errors = 0; Application.logMessageReceived += Log;
        run = Verify(); next = EditorApplication.timeSinceStartup + 2;
    }
    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception) errors++; }
    static void Tick()
    {
        const string command = "Temp/UiVictory.command";
        if (!EditorApplication.isCompiling && File.Exists(command))
        {
            string action = File.ReadAllText(command).Trim(); File.Delete(command);
            if (action == "test") StartTest();
        }
        if (pendingRestart && !EditorApplication.isPlaying && !EditorApplication.isCompiling)
        {
            pendingRestart = false;
            SessionState.SetBool("UiVictoryTest", true);
            EditorApplication.isPlaying = true;
            return;
        }
        // The editor throttles the player loop while the Game view is unfocused; keep it
        // stepping so unscaled fades and audio time really advance during the audit.
        if (run != null && EditorApplication.isPlaying)
        {
            if (EditorApplication.isPaused) EditorApplication.isPaused = false;
            EditorApplication.QueuePlayerLoopUpdate();
        }
        if (run == null || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < next) return;
        try { if (run.MoveNext()) next = EditorApplication.timeSinceStartup + run.Current; else { run = null; Cleanup(); } }
        catch (Exception ex) { lines.Add("FAIL: " + ex); Save(); run = null; Cleanup(); }
    }
    static void Save() { Directory.CreateDirectory("Logs"); File.WriteAllLines("Logs/UiVictoryVerification.txt", lines); }
    static void Check(bool ok, string label)
    { if (!ok) throw new Exception(label); lines.Add("PASS: " + label); Save(); }
    static void Cleanup()
    {
        Application.logMessageReceived -= Log;
        if (ui) { typeof(InnInterface).GetMethod("Restart", Private).Invoke(ui, new object[] { true }); }
        Time.timeScale = 1;
        Application.runInBackground = background;
    }
    static object Field(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    static object Part(string name)
    {
        var field = typeof(InnInterface).GetField("part", Private);
        return Enum.Parse(field.FieldType, name);
    }
    static void SetPart(string name) => typeof(InnInterface).GetField("part", Private).SetValue(ui, Part(name));
    static void SetStage(DuelEncounter duel, DuelEncounter.EncounterStage stage)
        => typeof(DuelEncounter).GetMethod("SetStage", Private).Invoke(duel, new object[] { stage });
    static void Advance(int times)
    {
        var advance = typeof(InnInterface).GetMethod("AdvanceDialogue", Private);
        for (int i = 0; i < times; i++) advance.Invoke(ui, null);
    }

    static IEnumerator<double> Verify()
    {
        background = Application.runInBackground; Application.runInBackground = true;
        ui = UnityEngine.Object.FindFirstObjectByType<InnInterface>();
        audio = UnityEngine.Object.FindFirstObjectByType<UiVictoryAudio>();
        var menu = UnityEngine.Object.FindFirstObjectByType<MenuAudio>();
        Check(ui && audio && menu, "Interface, victory and existing rain controllers loaded");
        var clip = Resources.Load<AudioClip>("UiVictory/Victory_B_Softer_100-116s");
        Check(clip && Mathf.Abs(clip.length - 16) < .01f && clip.channels == 2 && clip.loadType == AudioClipLoadType.DecompressOnLoad,
            "User-selected Victory B imported as original-length stereo PCM");
        Check(audio.VictoryClip == clip, "Victory B is the clip the player actually buffers");
        var buttons = UnityEngine.Object.FindObjectsByType<UiButtonSound>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Check(buttons.Length >= 10, "Main menu, settings and prologue buttons have UI triggers");
        Check(audio.HoverVolume >= 0 && audio.ConfirmVolume >= 0 && audio.PageVolume >= 0 && audio.VictoryVolume >= 0,
            "Four independent saved volume controls loaded");
        string[] required = { "UI_Hover_Brush", "UI_Confirm_Wood", "UI_Story_PageTurn", "UI_Ledger_PageTurn" };
        foreach (string name in required)
            Check(Resources.Load<AudioClip>("UiVictory/" + name), name + " imported from supplied recording");
        var brush = Resources.Load<AudioClip>("UiVictory/UI_Hover_Brush");
        Check(brush && brush.channels == 2 && Mathf.Abs(brush.length - .064f) < .01f,
            "Supplied brush-past recording replaces the old hover blip");
        var ledgerClip = Resources.Load<AudioClip>("UiVictory/UI_Ledger_PageTurn");
        Check(ledgerClip && ledgerClip.channels == 2 && ledgerClip.frequency == 48000
            && Mathf.Abs(ledgerClip.length - .589f) < .02f,
            "Supplied ledger page turn keeps its full 0.59 s tail and matches the dialogue cue's level");
        var dialogueLayer = ui.transform.Find("红尘客栈界面/Dialogue");
        var dialogueSound = dialogueLayer ? dialogueLayer.GetComponentInChildren<UiButtonSound>(true) : null;
        var hoverClip = (AudioClip)typeof(UiVictoryAudio).GetField("hover", Private).GetValue(audio);
        Check(hoverClip && hoverClip.name == "UI_Hover_Brush",
            "Hover cue plays the supplied brush-past recording");
        Check(dialogueSound && dialogueSound.hoverEnabled == false,
            "Dialogue continue button stays silent when the pointer passes over it");
        var dialoguePlate = dialogueLayer ? dialogueLayer.Find("对话框") as RectTransform : null;
        Check(dialoguePlate && Mathf.Abs(dialoguePlate.sizeDelta.x - 940f) < 1f
            && Mathf.Abs(dialoguePlate.sizeDelta.y - 205f) < 1f
            && dialogueLayer.GetComponentsInChildren<Component>(true).All(c => c.GetType().Name != "TextMeshProUGUI"),
            "Dialogue box is back to the original 940x205 UGUI plate");
        var menuButtons = buttons.Where(b => b.GetComponent<MenuButtonV2>()).ToArray();
        Check(menuButtons.Length == 3, "Three main menu buttons bound");
        Check(menuButtons.All(b => b.hoverEnabled), "Menu buttons keep the hover cue");
        yield return 2;
        int h = audio.HoverCount;
        ExecuteEvents.Execute(menuButtons[0].gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
        ExecuteEvents.Execute(menuButtons[1].gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
        Check(audio.HoverCount == h + 1, "Hover cooldown prevents rapid retrigger");
        yield return .16;
        ExecuteEvents.Execute(menuButtons[1].gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
        Check(audio.HoverCount == h + 2, "Hover resumes after short cooldown");
        int c = audio.ConfirmCount;
        menuButtons[1].GetComponent<Button>().onClick.Invoke();
        Check(audio.ConfirmCount == c + 1, "Main menu settings click plays wooden confirmation");
        var sample = new float[256];
        var confirmClip = Resources.Load<AudioClip>("UiVictory/UI_Confirm_Wood");
        Check(confirmClip.GetData(sample, 1000) && sample.Any(value => Mathf.Abs(value) > .00001f),
            "Wooden confirmation contains audible PCM samples");
        typeof(InnInterface).GetMethod("CloseSettings", Private).Invoke(ui, null);
        typeof(InnInterface).GetMethod("BeginStoryIntro", Private).Invoke(ui, null);
        yield return 6.5;
        var intro = UnityEngine.Object.FindFirstObjectByType<StoryIntroPanel>(FindObjectsInactive.Include);
        Check(intro && intro.FullyRevealed, "Prologue advances through all six paragraphs in Play Mode");
        Check(audio.PageCount == 5, "Five later prologue paragraphs each trigger one page turn");
        typeof(InnInterface).GetMethod("StartGame", Private).Invoke(ui, null);
        yield return .2;

        // ---------- Ledger: the supplied page turn on open and on close ----------
        var fighter = UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>();
        var duel = UnityEngine.Object.FindFirstObjectByType<DuelEncounter>();
        Check(fighter && duel && ui.ledgerAnchor, "Player, encounter and ledger anchor present");
        var rainBeds = menu.GetComponentsInChildren<AudioSource>()
            .Where(s => s.name == "Rain night ambience" || s.name == "Rain outdoor ambience").ToArray();
        Check(rainBeds.Length == 2, "Roof and outdoor rain beds are both present");

        SetPart("Hall");
        fighter.PlaceForStory(ui.ledgerAnchor.position, fighter.transform.rotation);
        Physics.SyncTransforms();
        int ledgerBefore = audio.LedgerPageCount;
        var movement = typeof(InnInterface).GetMethod("UpdateStoryMovement", Private);
        movement.Invoke(ui, new object[] { true });
        Check(Field(ui, "view").ToString() == "Ledger", "E at the ledger opens the spread; view=" + Field(ui, "view"));
        Check(audio.LedgerPageCount == ledgerBefore + 1, "Opening the ledger plays exactly one page turn");
        Check(audio.LedgerClip && audio.LedgerClip.name == "UI_Ledger_PageTurn",
            "Ledger uses the supplied page-turn recording");
        yield return .3;
        Check(!audio.VictoryPlaying, "Reading the ledger never starts the victory cue");
        typeof(InnInterface).GetMethod("FinishReadingLedger", Private).Invoke(ui, null);
        Check(audio.LedgerPageCount == ledgerBefore + 2, "Closing the ledger plays the second half of the gesture");
        Check(Field(ui, "view").ToString() != "Ledger", "Ledger closes back into the game; view=" + Field(ui, "view"));
        Type viewType = typeof(InnInterface).GetNestedType("View", BindingFlags.NonPublic);
        typeof(InnInterface).GetMethod("Show", Private).Invoke(ui, new object[] { Enum.Parse(viewType, "Play") });
        yield return .2;

        // ---------- Mid-story elite win must stay silent ----------
        var source = audio.GetComponentsInChildren<AudioSource>().Single(s => s.name == "Victory B once");
        Check(!audio.VictoryStarted && audio.VictoryCount == 0, "Fresh run starts with Victory B unarmed");
        SetPart("EliteFight");
        SetStage(duel, DuelEncounter.EncounterStage.EliteVictory);
        yield return .4;
        Check(Field(ui, "view").ToString() == "Dialogue", "Elite win still runs its departure dialogue");
        Check(audio.VictoryCount == 0 && !source.isPlaying,
            "Elite win stays silent: the story is not over yet");
        Advance(5);
        yield return .4;
        Check(audio.VictoryCount == 0 && !source.isPlaying, "Leaving the inn keeps Victory B silent");

        // ---------- True ending: last pursuer down, farewell, then one cue ----------
        float rainBeforeVictory = rainBeds.Sum(s => s.volume);
        SetPart("JointFight");
        typeof(InnInterface).GetField("hasHelped", Private).SetValue(ui, true);
        SetStage(duel, DuelEncounter.EncounterStage.Complete);
        yield return .4;
        Check(Field(ui, "view").ToString() == "Dialogue", "Clearing the last wave opens the farewell");
        Check(audio.VictoryCount == 0, "Farewell dialogue is not the trigger; the result card is");
        Advance(6);
        yield return .2;
        Check(Field(ui, "view").ToString() == "Victory", "Farewell ends on the result card; view=" + Field(ui, "view"));
        Check(audio.VictoryCount == 1 && audio.VictoryStarted, "True ending starts Victory B exactly once");
        Check(source.isPlaying && !source.loop, "Victory B is playing once and is not looping");
        yield return 1.8;
        Check(source.volume > .1f, "Victory B is audible on the result card; volume=" + source.volume.ToString("F2"));
        Check(clip.GetData(sample, clip.frequency * 2) && sample.Any(value => Mathf.Abs(value) > .00001f),
            "Victory B contains audible PCM at the played position");
        Check(rainBeds.All(s => s.isPlaying) && rainBeds.Sum(s => s.volume) > rainBeforeVictory * 1.15f,
            "Rain keeps playing under the score and grows clearer; sum=" + rainBeds.Sum(s => s.volume).ToString("F3"));
        int before = audio.VictoryCount;
        audio.StartVictory();
        Check(audio.VictoryCount == before, "Repeated victory signal cannot restart the cue");
        int samplePosition = source.timeSamples;
        yield return .5;
        Check(source.timeSamples > samplePosition, "Victory B advances instead of looping or restarting");
        Check(Mathf.Approximately(Time.timeScale, 0f), "Result card freezes the world while the cue plays");

        // ---------- Restart clears the cue so the next run can earn it again ----------
        typeof(InnInterface).GetMethod("Restart", Private).Invoke(ui, new object[] { true });
        yield return .3;
        Check(!source.isPlaying && source.volume < .001f, "Restart stops Victory B");
        Check(Field(ui, "view").ToString() == "Title", "Restart returns to the title");
        Check(!audio.VictoryStarted, "Next run re-arms Victory B");
        Check(errors == 0, "No runtime errors during UI, ledger and ending test");
        lines.Add("COMPLETE (instrumented ending; combat hits covered by CombatAudioVerification)"); Save();
    }
}
