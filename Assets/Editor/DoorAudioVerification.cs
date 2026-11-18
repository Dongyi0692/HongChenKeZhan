using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class DoorAudioVerification
{
    static IEnumerator<double> run;
    static double next;
    static int errors;
    static InnInterface ui;
    static float rainVolume;
    static bool background;
    static readonly List<string> results = new();
    const BindingFlags Private = BindingFlags.Instance|BindingFlags.NonPublic;
    static DoorAudioVerification()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += s => {
            if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("DoorAudioTest",false)) {
                SessionState.SetBool("DoorAudioTest",false); Begin();
            }
        };
    }
    public static void StartTest()
    {
        if(EditorApplication.isPlaying) Begin();
        else { SessionState.SetBool("DoorAudioTest",true); EditorApplication.isPlaying=true; }
    }
    static void Begin() { results.Clear();errors=0;Application.logMessageReceived+=Log;run=Verify();next=EditorApplication.timeSinceStartup+3; }
    static void Log(string s,string stack,LogType type) { if(type==LogType.Error||type==LogType.Exception)errors++; }
    static void Tick()
    {
        if(run==null||EditorApplication.isCompiling||EditorApplication.timeSinceStartup<next)return;
        try { if(run.MoveNext())next=EditorApplication.timeSinceStartup+run.Current;else{run=null;Cleanup();} }
        catch(Exception e){results.Add("FAIL: "+e);Save();run=null;Cleanup();}
    }
    static void Cleanup()
    {
        Application.logMessageReceived-=Log;
        var audio=UnityEngine.Object.FindFirstObjectByType<MenuAudio>();if(audio)audio.SetRainVolume(rainVolume);
        if(ui) { ui.enabled=true;typeof(InnInterface).GetMethod("Restart",Private).Invoke(ui,new object[]{true}); }
        Application.runInBackground=background;
    }
    static void Save(){Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/DoorAudioVerification.txt",results);}
    static void Check(bool value,string label){if(!value)throw new Exception(label);results.Add("PASS: "+label);Save();}
    static AudioSource Source(Component door,string label)=>door.GetComponentsInChildren<AudioSource>().Single(s=>s.name==label);
    static float Angle(InnEntranceDoors door)=>(float)typeof(InnEntranceDoors).GetField("angle",Private).GetValue(door);
    static IEnumerator<double> Verify()
    {
        background=Application.runInBackground;Application.runInBackground=true;EditorApplication.isPaused=false;
        ui=UnityEngine.Object.FindFirstObjectByType<InnInterface>();
        var front=UnityEngine.Object.FindFirstObjectByType<InnEntranceDoors>();
        var rear=UnityEngine.Object.FindFirstObjectByType<InnRearExitDoor>();
        var audio=UnityEngine.Object.FindFirstObjectByType<MenuAudio>();
        Check(ui&&front&&rear&&audio,"Existing Stage12 front door, rear door and ambience loaded");
        rainVolume=audio.RainVolume;audio.SetRainVolume(.12f);
        typeof(InnInterface).GetMethod("StartGame",Private).Invoke(ui,null);ui.enabled=false;Time.timeScale=1;
        audio.SetMenu(false);front.ManualControl=true;front.OpenRequested=false;rear.OpenRequested=false;
        yield return 2;
        var hinge=Source(front,"Wood hinge");var hit=Source(front,"Wood door closure");
        var rain=audio.GetComponentsInChildren<AudioSource>().Single(s=>s.name=="Rain night ambience");
        Check(!hinge.isPlaying&&!hit.isPlaying,"Initially closed doors do not play spurious sound");
        double started=AudioSettings.dspTime;front.OpenRequested=true;yield return .25;
        Check(Angle(front)>0&&hinge.isPlaying&&hinge.clip.name=="WoodOpenHinge","Front hinge creak starts with actual rotation");
        Check(rain.isPlaying&&rain.volume>.12f,"Opening door smoothly increases existing rain source");
        Time.timeScale=0;float before=Angle(front);yield return .2;
        Check(Mathf.Approximately(Angle(front),before)&&!hinge.isPlaying&&rain.isPlaying,"Pause freezes door and hinge sound while rain continues");
        Time.timeScale=1;yield return .2;
        Check(Angle(front)>before&&hinge.isPlaying,"Resume continues door and hinge sound");
        yield return 1.7;
        Check(Mathf.Abs(Angle(front)-front.openAngle)<.01f&&!hinge.isPlaying&&!hit.isPlaying,"Full open stops creak without closure impact");
        front.OpenRequested=false;yield return .2;
        Check(hinge.isPlaying&&hinge.clip.name=="WoodCloseHinge"&&!hit.isPlaying,"Closing uses separate creak with no premature impact");
        while(Angle(front)>.001f){CheckNoImpact(hit);yield return .01;}
        Check(hit.isPlaying,"Impact starts on the actual fully-closed frame");
        yield return .8;
        Check(!hit.isPlaying&&!hinge.isPlaying,"Closed stationary door does not repeat impact or creak");
        yield return 3.5;
        Check(rain.volume>=.119f&&rain.volume<.125f&&rain.isPlaying,"Closed door gradually restores indoor rain without restart");
        front.OpenRequested=true;yield return .3;front.OpenRequested=false;yield return .1;front.OpenRequested=true;yield return .2;
        Check(!hit.isPlaying&&hinge.clip.name=="WoodOpenHinge","Mid-motion reversal switches creak without false closure hit");
        front.OpenRequested=false;yield return 2;
        rear.OpenRequested=true;yield return .3;
        Check(Source(rear,"Wood hinge").isPlaying&&InnDoorAudio.RainExposure>0,"Negative-angle rear door also drives creak and rain exposure");
        yield return 1.5;rear.OpenRequested=false;
        yield return Mathf.Abs(rear.openAngle)/rear.rotationSpeed+.1f;
        Check(Source(rear,"Wood door closure").isPlaying,"Rear door impact follows its own close duration");
        yield return .8;
        Check(!Source(rear,"Wood hinge").isPlaying&&!Source(rear,"Wood door closure").isPlaying,"Rear door audio finishes cleanly");
        Check(errors==0,"No runtime errors during door audio verification");
        results.Add("COMPLETE");Save();
    }
    static void CheckNoImpact(AudioSource hit){if(hit.isPlaying)throw new Exception("Impact before actual closure");}
}
