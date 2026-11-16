using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Reproduce the recorded actor coordinates in the existing hall. Only setup
// uses placement; the measured escape uses normal input and Controller.Move.
public static class CornerEscapeVerification
{
    [Serializable] public class Probe { public string direction, state; public float distance, intendedSpeed; public Vector2 input; public Vector3 end; }
    [Serializable] public class Report
    {
        public bool active, passed, completed, enemyYielded, escaped, wallBlocked, wallSlide;
        public string phase, output;
        public int frames, runtimeErrors;
        public float enemyRetreat, escapeDistance, wallSlideDistance;
        public List<Probe> probes = new();
        public List<string> failures = new();
    }
    static Report report = new();
    static readonly Vector3 PlayerSpot = new(3.602263f,.03f,-.439734f);
    static readonly Vector3 EnemySpot = new(2.857561f,.271061f,-.520221f);
    static readonly Vector3[] Directions = { Vector3.forward,Vector3.back,Vector3.left,Vector3.right };
    static readonly string[] Names = { "hall-side", "bench-side", "enemy-side", "stair-side" };
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static PrototypeFighter fighter;
    static CombatActor player, enemy;
    static EnemyCombatAI brain;
    static PrototypeInput input;
    static InnInterface ui;
    static int directionIndex, initialFrame, originalHealth;
    static float phaseAt;
    static double began;
    static Vector3 retreatStart;
    static Collider wall;
    static Vector3 wallStart;

    [Unity.Pipeline.Commands.CliCommand("inncombat","begin_corner_test","Reproduce the recorded stair/bench/enemy pin and observe a real escape")]
    public static string Begin()
    {
        if (report.active) return Status();
        report = new Report();
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) throw new Exception("Enter unpaused Play Mode first.");
            fighter = Object.FindFirstObjectByType<PrototypeFighter>();
            ui = Object.FindFirstObjectByType<InnInterface>();
            Call("Restart",true);
            Call("BeginTutorialFight");
            var encounter = fighter.GetComponent<DuelEncounter>();
            player = encounter.PlayerActor; enemy = encounter.TutorialActor;
            brain = enemy.GetComponent<EnemyCombatAI>();
            input = fighter.GetComponent<PrototypeInput>(); input.enabled = false;
            originalHealth = player.MaxHealth;
            player.Configure(CombatRole.Player,CombatTeam.Player,10000);
            brain.enabled = false;
            directionIndex = 0;
            initialFrame = Time.frameCount; began = EditorApplication.timeSinceStartup;
            ResetStance();
            var camera = Camera.main.GetComponent<OrbitCamera>();
            camera.FrameActors(player.transform,enemy.transform,true);
            report.active = true;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
            SetPhase("Probe");
        }
        catch(Exception error) { Fail(error.GetBaseException().ToString()); }
        return Status();
    }
    [Unity.Pipeline.Commands.CliCommand("inncombat","status_corner_test","Read recorded corner escape checks")]
    public static string Status() => JsonUtility.ToJson(report,true);

    static void Tick()
    {
        if (!report.active) return;
        try
        {
            report.frames = Time.frameCount-initialFrame;
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup-began > 25d) throw new Exception("Corner test timed out.");
            input.enabled = false;
            float elapsed = Time.time-phaseAt;
            if (report.phase == "Probe")
            {
                Move(Directions[directionIndex]);
                if(elapsed < .5f) return;
                report.probes.Add(new Probe { direction=Names[directionIndex], distance=Vector3.ProjectOnPlane(player.transform.position-PlayerSpot,Vector3.up).magnitude,end=player.transform.position,
                    input=fighter.MoveInput,state=fighter.State.ToString(),intendedSpeed=((Vector3)typeof(PrototypeFighter).GetField("planarVelocity",Hidden).GetValue(fighter)).magnitude });
                fighter.ClearInput(); directionIndex++;
                ResetStance();
                if(directionIndex < Directions.Length) SetPhase("Probe");
                else
                {
                    brain.enabled = true; brain.ResetBrain(); brain.SetCombatEnabled(true); brain.PreferOpeningTarget(player);
                    retreatStart=enemy.transform.position;
                    SetPhase("Yield");
                }
            }
            else if(report.phase == "Yield")
            {
                fighter.ClearInput();
                report.enemyRetreat = Mathf.Max(report.enemyRetreat,Vector3.ProjectOnPlane(enemy.transform.position-retreatStart,Vector3.up).magnitude);
                float gap=Vector3.ProjectOnPlane(enemy.transform.position-player.transform.position,Vector3.up).magnitude;
                if(gap >= 1.0f)
                {
                    report.enemyYielded=true;
                    SetPhase("Escape");
                }
                else if(elapsed>2f) throw new Exception("Enemy did not yield space at the recorded stair/bench corner. gap="+gap);
            }
            else if(report.phase == "Escape")
            {
                Move(new Vector3(-1f,0f,1f).normalized); // Back into the hall, past the yielded space.
                report.escapeDistance=Vector3.ProjectOnPlane(player.transform.position-PlayerSpot,Vector3.up).magnitude;
                if(report.escapeDistance>.85f)
                {
                    report.escaped=true;
                    brain.enabled=false;
                    wall=Array.Find(Object.FindObjectsByType<Collider>(FindObjectsSortMode.None),c=>c.name=="ExteriorWalls_west_window_sill_wall.003");
                    if(wall==null||!wall.enabled) throw new Exception("Existing west wall required for collision regression.");
                    fighter.PlaceForStory(new Vector3(wall.bounds.max.x+.6f,.03f,wall.bounds.center.z),Quaternion.LookRotation(Vector3.left));
                    SetPhase("WallPressure");
                }
                else if(elapsed>3f) throw new Exception("Normal movement could not escape after enemy yielding: "+player.transform.position+", action="+fighter.State);
            }
            else if(report.phase=="WallPressure")
            {
                Move(Vector3.left);
                if(elapsed<.8f)return;
                float clearance=player.transform.position.x-wall.bounds.max.x;
                report.wallBlocked=clearance>=.30f&&clearance<.55f;
                if(!report.wallBlocked)throw new Exception("Wall must block the full body: clearance="+clearance);
                wallStart=player.transform.position;
                SetPhase("WallSlide");
            }
            else if(report.phase=="WallSlide")
            {
                Move(Vector3.forward);
                report.wallSlideDistance=player.transform.position.z-wallStart.z;
                if(report.wallSlideDistance>.85f){report.wallSlide=true;Finish();}
                else if(elapsed>1.5f)throw new Exception("Cannot move along a blocking wall.");
            }
        }
        catch(Exception error){Fail(error.GetBaseException().ToString());}
    }
    static void ResetStance()
    {
        fighter.ClearInput();
        player.ResetActor(); enemy.ResetActor();
        fighter.PlaceForStory(PlayerSpot,Quaternion.LookRotation(Vector3.left));
        var motor=enemy.GetComponent<CharacterController>();
        motor.enabled=false; enemy.transform.SetPositionAndRotation(EnemySpot,Quaternion.LookRotation(Vector3.right)); motor.enabled=true;
        enemy.ControlLocked=false;
        Physics.SyncTransforms();
    }
    static void Move(Vector3 direction)
    {
        Vector3 forward=Vector3.ProjectOnPlane(Camera.main.transform.forward,Vector3.up).normalized;
        Vector3 right=Vector3.Cross(Vector3.up,forward);
        fighter.MoveInput=new Vector2(Vector3.Dot(direction,right),Vector3.Dot(direction,forward));
    }
    static void SetPhase(string value){report.phase=value;phaseAt=Time.time;}
    static void Call(string method, params object[] args)=>typeof(InnInterface).GetMethod(method,Hidden).Invoke(ui,args);
    static void OnLog(string value,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)report.runtimeErrors++;}
    static void Fail(string error){report.failures.Add(error);Finish();}
    static void Finish()
    {
        report.active=false;report.completed=true;
        EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;
        if(!report.enemyYielded||!report.escaped||!report.wallBlocked||!report.wallSlide||report.runtimeErrors>0) report.failures.Add("Escape/collision checks incomplete or runtime error.");
        if(report.probes.Count!=4||report.probes[0].distance<.85f)report.failures.Add("The open hall direction must allow normal movement.");
        if(report.probes.Count==4&&report.probes[1].end.z<PlayerSpot.z-.08f)report.failures.Add("Movement into the bench must stay blocked.");
        report.passed=report.failures.Count==0;
        report.phase=report.passed?"Completed":"Failed";
        if(fighter!=null)fighter.ClearInput();
        if(player!=null&&originalHealth>0)player.Configure(CombatRole.Player,CombatTeam.Player,originalHealth);
        if(report.passed)Call("Restart",true);
        report.output=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Logs","CornerEscapeVerification.json"));
        File.WriteAllText(report.output,JsonUtility.ToJson(report,true));
    }
}
