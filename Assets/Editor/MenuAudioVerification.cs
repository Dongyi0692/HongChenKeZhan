using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class MenuAudioVerification
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Report = "Logs/MenuAudioVerification.txt";
    static IEnumerator<double> run;
    static double next;
    static int errors;
    static float originalVolume;
    static bool hadPreference, wasBackground, hadRainPreference;
    static float originalRainVolume;
    static MenuAudio audio;
    static InnInterface ui;
    static readonly List<string> results = new();
    static MenuAudioVerification()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("MenuAudioVerify", false)) {
                SessionState.SetBool("MenuAudioVerify", false);
                Begin();
            }
        };
    }
    public static void StartTest()
    {
        if (EditorApplication.isPlaying) Begin();
        else { SessionState.SetBool("MenuAudioVerify", true); EditorApplication.isPlaying = true; }
    }
    static void Begin()
    {
        results.Clear(); errors = 0;
        Application.logMessageReceived += Log;
        run = Verify(); next = EditorApplication.timeSinceStartup + 3;
    }
    static void Update()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            const string command = "Temp/MenuAudioVerification.command";
            if (File.Exists(command))
            {
                string value = File.ReadAllText(command).Trim(); File.Delete(command);
                if (value == "run")
                {
                    StartTest();
                }
            }
            if (run == null || EditorApplication.timeSinceStartup < next) return;
            if (run.MoveNext()) next = EditorApplication.timeSinceStartup + run.Current;
            else { run = null; Cleanup(); }
        }
        catch (Exception ex)
        {
            results.Add("FAIL: " + ex); Save(); run = null; Cleanup();
        }
    }
    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }
    static void Cleanup()
    {
        Application.logMessageReceived -= Log; Application.runInBackground = wasBackground;
        if (audio) audio.SetMusicVolume(originalVolume);
        if (audio) audio.SetRainVolume(originalRainVolume);
        if (!hadPreference) PlayerPrefs.DeleteKey("FengChenInn.UI.musicVolume");
        if (!hadRainPreference) PlayerPrefs.DeleteKey("FengChenInn.UI.rainVolume");
        PlayerPrefs.Save();
    }
    static void Save() { Directory.CreateDirectory("Logs"); File.WriteAllLines(Report, results); }
    static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); results.Add("PASS: " + message); Save(); }
    static object Field(object obj, string name) => obj.GetType().GetField(name, Private).GetValue(obj);
    static void Call(string name, params object[] args) => typeof(InnInterface).GetMethod(name, Private).Invoke(ui, args);
    static string View() => Field(ui, "view").ToString();
    static AudioSource[] Music() => audio.GetComponentsInChildren<AudioSource>().Where(s => s.gameObject.name.StartsWith("Menu music")).ToArray();
    static AudioSource Rain() => audio.GetComponentsInChildren<AudioSource>().Single(s => s.gameObject.name == "Rain night ambience");
    static IEnumerator<double> Verify()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; yield return 8; }
        wasBackground = Application.runInBackground; Application.runInBackground = true; EditorApplication.isPaused = false;
        ui = UnityEngine.Object.FindFirstObjectByType<InnInterface>();
        audio = UnityEngine.Object.FindFirstObjectByType<MenuAudio>();
        Check(ui && audio, "Actual Stage12 interface and audio component loaded");
        hadPreference = PlayerPrefs.HasKey("FengChenInn.UI.musicVolume");
        originalVolume = audio.MusicVolume;
        originalRainVolume = audio.RainVolume;
        hadRainPreference = PlayerPrefs.HasKey("FengChenInn.UI.rainVolume");
        audio.SetRainVolume(.12f);
        audio.SetMenu(false); yield return 2.3;
        Call("Restart", true);
        audio.SetMusicVolume(.65f);
        yield return .5;
        Check((float)Field(audio,"envelope") > 0 && (float)Field(audio,"envelope") < 1, "Menu music fades in while timeScale=0; envelope="+Field(audio,"envelope")+", dsp="+AudioSettings.dspTime);
        yield return 2;
        Check(Music().Length == 2 && Music().Any(s=>s.isPlaying) && Rain().isPlaying, "Music and independent rain playing");
        Check(Music()[0].clip.samples == 2646000 && Music()[0].clip.frequency == 44100, "Original 60 second clip imported as PCM");
        double initialStart = ((double[])Field(audio,"starts"))[0];
        Call("OpenSettings"); yield return .3;
        Check(View()=="Settings" && (bool)Field(audio,"menu"), "Settings keeps menu music running");
        var sliders=ui.GetComponentsInChildren<Slider>();
        // The panel gained one slider per UI bus after this check was written, so the bus order
        // (master, music, rain, sensitivity) is what matters, not the total count.
        results.Add("INFO: settings exposes " + sliders.Length + " sliders; bus order master/music/rain/sensitivity first");
        Check(sliders.Length>=4, "Settings has separate master, music, rain and sensitivity sliders");
        sliders[1].value=0; yield return .3;
        Check(Music().All(s=>s.volume==0) && Rain().volume>0 && Rain().isPlaying, "Music slider mutes only music");
        sliders[1].value=.65f; yield return .3;
        Check(Mathf.Approximately(PlayerPrefs.GetFloat("FengChenInn.UI.musicVolume"),.65f), "Music preference stored");
        int rainCursor = Rain().timeSamples;
        sliders[2].value = 0; yield return .5;
        Check(Rain().volume == 0 && Rain().isPlaying && Music().Any(s=>s.volume>0), "Rain slider mutes only rain without stopping the loop");
        sliders[2].value = .12f; yield return .5;
        Check(Rain().volume > 0 && Rain().timeSamples != rainCursor && Mathf.Approximately(PlayerPrefs.GetFloat("FengChenInn.UI.rainVolume"),.12f), "Rain preference stored and unmute preserves playback position");
        Check(Rain().clip.frequency==48000 && Rain().clip.channels==2 && Mathf.Abs(Rain().clip.length-52.48454f)<.01f, "Roof rain stereo seamless loop imported");
        Call("CloseSettings"); yield return .3;
        Check(((double[])Field(audio,"starts"))[0]==initialStart, "Returning from settings does not restart music");
        double until=AudioSettings.dspTime+120;
        float minimum=1; int overlaps=0; bool overlap=false;
        while(AudioSettings.dspTime<until)
        {
            var sources=Music();
            CheckSilent(sources.Any(s=>s.isPlaying), "Playback stopped during loop");
            minimum=Mathf.Min(minimum,sources.Sum(s=>s.volume));
            bool both=sources.All(s=>s.volume>.01f);
            if(both&&!overlap)overlaps++;
            overlap=both;
            yield return .05;
        }
        Check(overlaps>=2 && minimum>.3f, "Two real-time loop joins without volume gap; overlaps="+overlaps+", minimumGain="+minimum);
        var rain=Rain(); int rainSample=rain.timeSamples;
        UnityEngine.Object.FindFirstObjectByType<MainMenuV2>().GetComponentsInChildren<Button>().First().onClick.Invoke();
        yield return 1;
        // The prologue layer was added after this verification was written and it is treated as a
        // title view, so the theme and the rain both keep running instead of releasing the music.
        Check(View()=="Prologue" && (float)Field(audio,"envelope")>.9f, "Start button enters prologue with the title theme still up");
        yield return 1.3;
        Check(Music().Any(s=>s.isPlaying && s.volume>.05f), "Theme keeps playing through the prologue");
        Check(rain.isPlaying && rain.timeSamples!=rainSample, "Rain continues uninterrupted through prologue");
        UnityEngine.Object.FindFirstObjectByType<StoryIntroPanel>().SkipButton.onClick.Invoke();
        // The release is exactly two seconds long, so sampling at two seconds is a frame race.
        yield return 2.6;
        Check(View()!="Prologue" && Rain().isPlaying && Music().All(s=>!s.isPlaying), "Existing prologue completion enters gameplay with rain retained");
        Call("Restart",true); yield return 2.5;
        Check(View()=="Title" && Music().Length==2 && audio.GetComponentsInChildren<AudioSource>().Count(s=>s.gameObject.name=="Rain night ambience")==1 && Music().Any(s=>s.isPlaying), "Return to title fades music back in without duplicate sources");
        Check(errors==0,"No runtime errors during audio verification");
        Call("OpenSettings"); yield return .5; ScreenCapture.CaptureScreenshot("Logs/MenuAudioSettings.png"); yield return .5; Call("CloseSettings");
        results.Add("COMPLETE"); Save();
    }
    static void CheckSilent(bool ok,string message) { if(!ok)throw new Exception(message); }
}




