using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;

[DefaultExecutionOrder(90)]
public sealed class FootstepTestDrive : MonoBehaviour
{
    public PrototypeFighter fighter;
    public Vector3 direction;
    public float strength;
    void Update()
    {
        var forward=Vector3.ProjectOnPlane(Camera.main.transform.forward,Vector3.up).normalized;
        var right=Vector3.Cross(Vector3.up,forward);
        fighter.MoveInput=new Vector2(Vector3.Dot(direction,right),Vector3.Dot(direction,forward))*strength;
        fighter.RunRequested=strength>.7f;
    }
}

[InitializeOnLoad]
public static class FootstepVerification
{
    static IEnumerator<double> run;
    static double next;
    static int errors;
    static bool background;
    static InnInterface ui;
    static PrototypeFighter fighter;
    static PrototypeInput input;
    static FootstepTestDrive drive;
    static HongfuFootsteps steps;
    static readonly List<string> results=new();
    static readonly List<float> phases=new();
    static readonly List<string> variations=new();
    static readonly List<FootstepGround> grounds=new();
    static readonly List<string> trace=new();
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static FootstepVerification()
    {
        EditorApplication.update+=Tick;
        EditorApplication.playModeStateChanged+=s=> {
            if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("FootstepTest",false)) {
                SessionState.SetBool("FootstepTest",false);Begin();
            }
        };
    }
    [MenuItem("Tools/红尘客栈/音频/验证红拂女脚步 (Play Mode)")]
    public static void StartTest()
    {
        if(EditorApplication.isPlaying)Begin();
        else {SessionState.SetBool("FootstepTest",true);EditorApplication.isPlaying=true;}
    }
    static void Begin() {results.Clear();trace.Clear();phases.Clear();variations.Clear();grounds.Clear();errors=0;Application.logMessageReceived+=Log;run=Verify();next=EditorApplication.timeSinceStartup+3;}
    static void Log(string s,string stack,LogType type) {if(type==LogType.Error||type==LogType.Exception)errors++;}
    static void Tick()
    {
        if(run!=null&&drive&&drive.strength>0&&steps&&trace.Count<300) {
            var t=typeof(HongfuFootsteps);trace.Add(Time.time.ToString("F3")+" y="+fighter.transform.position.y.ToString("F3")+" grounded="+fighter.IsGrounded+" gap="+t.GetField("groundGap",Private).GetValue(steps)+" d="+t.GetField("distance",Private).GetValue(steps)+" next="+t.GetField("nextStepDistance",Private).GetValue(steps)+" moving="+t.GetField("moving",Private).GetValue(steps)+" motion="+fighter.animationDriver.CurrentMotion);
        }
        if(run==null||EditorApplication.isCompiling||EditorApplication.timeSinceStartup<next)return;
        try {if(run.MoveNext())next=EditorApplication.timeSinceStartup+run.Current;else{run=null;Cleanup();}}
        catch(Exception e){results.Add("FAIL: "+e);Save();File.WriteAllLines("Logs/FootstepTrace.txt",trace);run=null;Cleanup();}
    }
    static void Save(){Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/FootstepVerification.txt",results);}
    static void Check(bool condition,string label){if(!condition)throw new Exception(label);results.Add("PASS: "+label);Save();}
    static void Cleanup()
    {
        Application.logMessageReceived-=Log;
        if(steps)steps.StepPlayed-=OnStep;
        if(drive)UnityEngine.Object.DestroyImmediate(drive);
        if(input)input.enabled=true;
        Time.timeScale=1;
        if(ui){ui.enabled=true;typeof(InnInterface).GetMethod("Restart",Private).Invoke(ui,new object[]{true});}
        Application.runInBackground=background;
    }
    static void OnStep(FootstepGround ground,AudioClip clip)
    {
        phases.Add(fighter.animationDriver.NormalizedTime);variations.Add(clip.name+":"+steps.LastPitch.ToString("F4"));grounds.Add(ground);
        results.Add(" STEP t="+Time.time.ToString("F3")+" surface="+ground+" clip="+clip.name+" speed="+steps.ActualSpeed.ToString("F2")+" phase="+fighter.animationDriver.NormalizedTime.ToString("F3")+" position="+fighter.transform.position.ToString("F2"));
    }
    static void Place(Vector3 position)
    {
        drive.strength=0;fighter.ClearInput();var cc=fighter.GetComponent<CharacterController>();
        cc.enabled=false;fighter.transform.position=position;cc.enabled=true;Physics.SyncTransforms();
    }
    static bool Silent()=>steps.GetComponentsInChildren<AudioSource>().Where(s=>s.name.StartsWith("Hongfu footsteps")).All(s=>!s.isPlaying);
    static IEnumerator<double> Verify()
    {
        background=Application.runInBackground;Application.runInBackground=true;EditorApplication.isPaused=false;
        ui=UnityEngine.Object.FindFirstObjectByType<InnInterface>();fighter=UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>();
        steps=fighter.GetComponent<HongfuFootsteps>();input=fighter.GetComponent<PrototypeInput>();
        Check(ui&&fighter&&steps,"Automatic attachment on existing Stage12 player");
        Check(Resources.LoadAll<AudioClip>("Footsteps/WetGround").Length==8&&Resources.LoadAll<AudioClip>("Footsteps/WoodFloor").Length==8,"8 wet and 8 wooden single steps imported");
        foreach(var c in Resources.LoadAll<AudioClip>("Footsteps"))
            Check(c.channels==1&&c.length>.25f&&c.length<.7f&&c.loadType==AudioClipLoadType.DecompressOnLoad,c.name+" is short mono decompressed PCM");
        var voices=steps.GetComponentsInChildren<AudioSource>().Where(s=>s.name.StartsWith("Hongfu footsteps")).ToArray();
        Check(voices.Length==3&&voices.All(s=>s.spatialBlend==1&&!s.loop&&s.dopplerLevel==0&&s.maxDistance==18),"Independent 3D one-shot voices; no recording loop");
        int count=steps.TotalSteps;yield return .35;
        Check(steps.TotalSteps==count&&Silent(),"Main menu/pause produces no footsteps");
        typeof(InnInterface).GetMethod("StartGame",Private).Invoke(ui,null);ui.enabled=false;input.enabled=false;Time.timeScale=1;
        fighter.SetDialoguePose(false);fighter.GetComponent<CombatActor>().ControlLocked=false;
        drive=fighter.gameObject.AddComponent<FootstepTestDrive>();drive.fighter=fighter;steps.StepPlayed+=OnStep;
        Place(new Vector3(-4,0,-20));yield return .4;
        count=steps.TotalSteps;drive.direction=Vector3.right;drive.strength=.5f;double started=Time.time;yield return 4;
        float walkRate=(steps.TotalSteps-count)/(float)(Time.time-started);
        results.Add("Slow diagnostic: dt="+(Time.time-started)+" scale="+Time.timeScale+" position="+fighter.transform.position+" input="+fighter.MoveInput+" speed="+steps.ActualSpeed+" grounded="+fighter.IsGrounded+" state="+fighter.State+" motion="+fighter.animationDriver.CurrentMotion+" enabled="+fighter.enabled+" driver="+drive.enabled+" ground="+steps.GroundCollider);Save();
        Check(steps.TotalSteps-count>=2&&steps.CurrentGround==FootstepGround.WetGround,"Slow movement uses wet footsteps on outdoor ground");
        Check(!steps.UsesAnimationEvents,"Existing locomotion clips have no events: calibrated distance fallback active");
        drive.strength=0;yield return .15;count=steps.TotalSteps;yield return .35;
        Check(steps.TotalSteps==count&&Silent(),"Stopping clears accumulated steps and fades existing tails");
        Place(new Vector3(-6,0,-20));yield return .4;
        count=steps.TotalSteps;drive.strength=1;started=Time.time;yield return 3.2;
        float runRate=(steps.TotalSteps-count)/(float)(Time.time-started);
        Check(runRate>walkRate,"Running cadence exceeds walking including existing landing transitions: "+walkRate.ToString("F2")+" / "+runRate.ToString("F2")+" steps/s");
        Check(variations.Distinct().Count()>4&&steps.LastPitch>=.955f&&steps.LastPitch<=1.045f,"Multiple clips and slight pitch variation");
        Time.timeScale=0;count=steps.TotalSteps;yield return .3;
        Check(steps.TotalSteps==count&&Silent(),"Pause immediately prevents new footsteps and silences tails");
        Time.timeScale=1;yield return 1.1;
        Check(steps.TotalSteps>count,"Resume restarts cadence without a burst of queued steps");
        drive.strength=0;Place(new Vector3(0,0,-12));yield return .4;
        drive.direction=Vector3.forward;drive.strength=.5f;yield return .2;
        Check(fighter.Jump(),"Existing controller accepts jump without changing parameters");
        yield return .15;count=steps.TotalSteps;
        Check(!fighter.IsGrounded,"Player is airborne during jump");yield return .25;
        Check(steps.TotalSteps==count&&Silent(),"Airborne movement does not produce footsteps");
        drive.strength=0;yield return .8;
        var entrance=UnityEngine.Object.FindFirstObjectByType<InnEntranceDoors>();entrance.ManualControl=true;entrance.OpenRequested=true;yield return 1.8;
        Place(new Vector3(0,0,-9));yield return .4;grounds.Clear();
        drive.direction=Vector3.forward;drive.strength=.5f;yield return 3.6;
        Check(grounds.Contains(FootstepGround.WetGround)&&grounds.Contains(FootstepGround.WoodFloor),"Actual movement crosses exterior porch into indoor wooden floor");
        Check(steps.CurrentGround==FootstepGround.WoodFloor&&steps.LastClip.name.StartsWith("WoodFloor"),"Indoor floor selects wooden steps from actual collider");
        drive.strength=0;yield return .2;drive.direction=Vector3.back;drive.strength=.5f;yield return 3.6;
        Check(steps.CurrentGround==FootstepGround.WetGround,"Walking back outside switches to wet ground");
        drive.strength=0;Place(new Vector3(4.7f,.25f,-2.8f));yield return .5;
        drive.direction=Vector3.forward;drive.strength=.5f;yield return 1.8;
        Check(fighter.transform.position.y>.6f&&steps.CurrentGround==FootstepGround.WoodFloor,"Existing smooth stair collider uses wood footsteps");
        drive.strength=0;Place(new Vector3(4.7f,4.1f,3.9f));yield return .4;
        drive.direction=Vector3.left;drive.strength=.5f;yield return 1;
        Check(steps.CurrentGround==FootstepGround.WoodFloor,"Upper wooden walkway resolves WoodFloor");
        drive.strength=0;Place(new Vector3(0,.25f,-2));yield return .4;
        count=steps.TotalSteps;fighter.SetDialoguePose(true);drive.direction=Vector3.forward;drive.strength=1;yield return .4;
        Check(steps.TotalSteps==count&&Silent(),"Dialogue lock suppresses steps");fighter.SetDialoguePose(false);
        drive.strength=0;yield return .2;count=steps.TotalSteps;
        var cc=fighter.GetComponent<CharacterController>();cc.enabled=false;fighter.transform.position+=Vector3.right*2;cc.enabled=true;yield return .2;
        Check(steps.TotalSteps==count,"Teleport does not generate a phantom step");
        Check(phases.Count>=10,"Recorded actual playable phases for gait synchronization review");
        Check(phases.All(p=>Mathf.Abs(Mathf.Repeat(p+.25f,.5f)-.25f)<.08f),"All steps align within 8% of the actual left/right contact cycle");
        Check(errors==0,"No runtime errors during footstep verification");
        results.Add("COMPLETE");Save();
    }
}

