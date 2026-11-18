using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CombatAudioVerification
{
    static IEnumerator<double> run;static double next;static bool background;static int errors;
    static InnInterface ui;static PrototypeFighter fighter;static PrototypeInput input;static CombatActor actor,target;
    static GameObject dummy,dropObject;static CombatAudioSystem audio;static float maxSum,maxSignal;
    static readonly float[] samples=new float[256];static readonly List<string> results=new();
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static CombatAudioVerification()
    {
        EditorApplication.update+=Tick;
        EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("CombatAudioTest",false)){SessionState.SetBool("CombatAudioTest",false);Begin();}};
    }
    [MenuItem("Tools/红尘客栈/音频/验证战斗与序章 (Play Mode)")]
    public static void StartTest(){if(EditorApplication.isPlaying)Begin();else{SessionState.SetBool("CombatAudioTest",true);EditorApplication.isPlaying=true;}}
    static void Begin(){results.Clear();errors=0;maxSum=maxSignal=0;Application.logMessageReceived+=Log;run=Verify();next=EditorApplication.timeSinceStartup+3;}
    static void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors++;}
    static void Tick()
    {
        if(run==null||EditorApplication.isCompiling)return;
        if(audio) {
            var voices=audio.GetComponentsInChildren<AudioSource>();maxSum=Mathf.Max(maxSum,voices.Where(s=>s.isPlaying).Sum(s=>s.volume));
            foreach(var source in voices.Where(s=>s.isPlaying)){source.GetOutputData(samples,0);foreach(float v in samples)maxSignal=Mathf.Max(maxSignal,Mathf.Abs(v));}
        }
        if(EditorApplication.timeSinceStartup<next)return;
        try{if(run.MoveNext())next=EditorApplication.timeSinceStartup+run.Current;else{run=null;Cleanup();}}
        catch(Exception e){results.Add("FAIL: "+e);Save();run=null;Cleanup();}
    }
    static void Save(){Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/CombatAudioVerification.txt",results);}
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);results.Add("PASS: "+label);Save();}
    static void Sound(string kind,AudioClip clip){results.Add(" SOUND "+kind+" "+clip.name+" attackTime="+(actor?actor.AttackElapsed.ToString("F3"):"-")+" t="+Time.time.ToString("F3"));}
    static void Cleanup()
    {
        Application.logMessageReceived-=Log;if(audio)audio.SoundPlayed-=Sound;
        if(dummy)UnityEngine.Object.DestroyImmediate(dummy);if(dropObject)UnityEngine.Object.DestroyImmediate(dropObject);
        if(input)input.enabled=true;Time.timeScale=1;
        if(ui){ui.enabled=true;typeof(InnInterface).GetMethod("Restart",Private).Invoke(ui,new object[]{true});}
        Application.runInBackground=background;
    }
    static void Place()
    {
        actor.ResetActor();fighter.ResetPose();fighter.PlaceForStory(new Vector3(0,.24f,-2),Quaternion.identity);
        if(target){target.ResetActor();target.transform.SetPositionAndRotation(new Vector3(0,.24f,-.7f),Quaternion.Euler(0,180,0));}
        Physics.SyncTransforms();
    }
    static IEnumerable<double> StartAttack(bool heavy)
    {
        double deadline=EditorApplication.timeSinceStartup+3;
        while(!actor.IsAttacking&&EditorApplication.timeSinceStartup<deadline){fighter.Attack(heavy);yield return .025;}
        Check(actor.IsAttacking,"Real controller started "+(heavy?"heavy combo":"light attack"));
    }
    static IEnumerable<double> EndAttack()
    {
        double deadline=EditorApplication.timeSinceStartup+8;
        while(actor.IsAttacking&&EditorApplication.timeSinceStartup<deadline)yield return .05;
        Check(!actor.IsAttacking,"Attack animation completed");yield return .3;
    }
    static IEnumerator<double> Verify()
    {
        background=Application.runInBackground;Application.runInBackground=true;EditorApplication.isPaused=false;
        ui=UnityEngine.Object.FindFirstObjectByType<InnInterface>();fighter=UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>();
        input=fighter.GetComponent<PrototypeInput>();actor=fighter.GetComponent<CombatActor>();audio=CombatAudioSystem.Instance;
        Check(audio&&ui&&fighter,"Combat pool attached to existing Stage12");audio.SoundPlayed+=Sound;
        Check(Resources.LoadAll<AudioClip>("CombatAudio").Length==15,"15 processed clips from all seven original recordings");
        foreach(var motion in new[]{CombatMotion.Light,CombatMotion.Heavy,CombatMotion.Skill}){
            var c=fighter.animationDriver.motionSet.GetClip(motion);results.Add("AUDIT "+motion+" length="+c.length+" events="+c.events.Length);
        }
        var menu=UnityEngine.Object.FindFirstObjectByType<MenuAudio>();
        typeof(InnInterface).GetMethod("BeginStoryIntro",Private).Invoke(ui,null);yield return 2.3;
        var music=menu.GetComponentsInChildren<AudioSource>().Where(s=>s.name.StartsWith("Menu music")).ToArray();
        var rain=menu.GetComponentsInChildren<AudioSource>().Single(s=>s.name=="Rain night ambience");
        Check(music.Any(s=>s.isPlaying&&s.volume>.01f)&&rain.isPlaying,"Prologue retains existing background music together with rain");
        int rainSample=rain.timeSamples;yield return .3;Check(rain.timeSamples!=rainSample,"Rain continues through prologue without restarting");
        typeof(InnInterface).GetMethod("StartGame",Private).Invoke(ui,null);ui.enabled=false;input.enabled=false;Time.timeScale=1;yield return 2.3;
        Check(music.All(s=>!s.isPlaying||s.volume<.001f)&&rain.isPlaying,"Leaving prologue fades music in about two seconds and keeps rain");
        dummy=new GameObject("Temporary combat audio target");var collider=dummy.AddComponent<CapsuleCollider>();collider.center=Vector3.up*.9f;collider.height=1.8f;collider.radius=.38f;
        target=dummy.AddComponent<CombatActor>();target.Configure(CombatRole.Elite,CombatTeam.Enemy,1000);
        Place();target.transform.position=new Vector3(20,.24f,0);yield return .5;
        int swings=audio.SwingCount,hits=audio.HitCount,blocks=audio.BlockCount;
        foreach(var wait in StartAttack(false))yield return wait;
        foreach(var wait in EndAttack())yield return wait;
        Check(audio.SwingCount==swings+1&&audio.HitCount==hits&&audio.BlockCount==blocks,"Missed light attack plays one swing and no contact sound");
        Place();yield return .5;int hp=target.Health;hits=audio.HitCount;
        foreach(var wait in StartAttack(false))yield return wait;
        foreach(var wait in EndAttack())yield return wait;
        Check(target.Health<hp&&audio.HitCount==hits+1,"Actual blade collision causes damage and exactly one impact");
        Place();yield return .5;target.Guarding=true;blocks=audio.BlockCount;hits=audio.HitCount;hp=target.Health;yield return .2;
        Check(audio.BlockCount==blocks,"Holding guard without contact stays silent");
        foreach(var wait in StartAttack(false))yield return wait;
        foreach(var wait in EndAttack())yield return wait;
        Check(target.Health==hp&&audio.BlockCount==blocks+1&&audio.HitCount==hits,"Successful frontal block plays block only, with no damage impact");
        Place();target.transform.position=new Vector3(20,.24f,0);yield return .5;swings=audio.SwingCount;hits=audio.HitCount;
        foreach(var wait in StartAttack(true))yield return wait;
        foreach(var wait in EndAttack())yield return wait;
        Check(audio.SwingCount==swings+fighter.heavyAttack.hitWindows.Length&&audio.HitCount==hits,"Four-strike heavy combo plays four separate synchronized swings on a miss");
        Place();target.transform.position=new Vector3(20,.24f,0);yield return .5;
        int dodge=audio.DodgeCount;double deadline=EditorApplication.timeSinceStartup+3;
        while(fighter.State!=PrototypeFighter.ActionState.Dodge&&EditorApplication.timeSinceStartup<deadline){fighter.Dodge();yield return .025;}
        Check(audio.DodgeCount==dodge+1,"Accepted dodge action triggers one boosted, trimmed sound");yield return .7;
        Time.timeScale=0;swings=audio.SwingCount;yield return .2;
        Check(audio.GetComponentsInChildren<AudioSource>().All(s=>!s.isPlaying)&&audio.SwingCount==swings,"Pause stops combat voices and cannot advance swing cues");Time.timeScale=1;
        int drops=audio.DropCount;var selected=new HashSet<string>();
        Action<string,AudioClip> collect=(kind,clip)=>{if(kind=="Drop")selected.Add(clip.name);};audio.SoundPlayed+=collect;
        for(int i=0;i<3;i++){
            dropObject=GameObject.CreatePrimitive(PrimitiveType.Cube);dropObject.name="Temporary falling weapon audio probe";
            dropObject.transform.position=new Vector3(-3,1.5f,-2);dropObject.transform.localScale=new Vector3(.05f,.04f,.6f);
            dropObject.AddComponent<Rigidbody>();dropObject.AddComponent<WeaponDropAudio>();yield return 1.4;
            UnityEngine.Object.DestroyImmediate(dropObject);
        }
        audio.SoundPlayed-=collect;
        Check(audio.DropCount==drops+3&&selected.Count>=2,"Real physics floor contacts choose complete drop variations once per object");
        Check(maxSum<=audio.VolumeBudget+.001f,"Concurrent combat source volume sum stays within budget: "+maxSum.ToString("F3"));
        Check(maxSignal>.00001f,"AudioSource output contains rendered audio samples: peak="+maxSignal.ToString("F5"));
        Check(errors==0,"No runtime errors during combat/prologue verification");results.Add("COMPLETE");Save();
    }
}
