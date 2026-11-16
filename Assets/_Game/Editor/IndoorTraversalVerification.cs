using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class IndoorTraversalVerification
{
    [Serializable] public class Report
    {
        public bool active, passed, completed;
        public string phase, output;
        public int frames, traversals, cameraCases, errors;
        public float gameSeconds, peakHeight;
        public List<string> failures = new();
    }
    static Report report = new();
    static PrototypeFighter fighter;
    static PrototypeInput input;
    static InnInterface ui;
    static OrbitCamera orbit;
    static InnStairTraversal stairs;
    static Vector3 bottom, top, destination;
    static double startedAt, phaseAt;
    static int phaseFrame, frameStart, cameraIndex;
    static float timeStart;
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [Unity.Pipeline.Commands.CliCommand("innpolish", "begin_traversal", "Verify real movement on existing stairs and camera wall clearance")]
    public static string Begin()
    {
        if (report.active) return Status();
        report = new Report { active = true };
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) throw new Exception("Start unpaused Play Mode first.");
            fighter = Object.FindFirstObjectByType<PrototypeFighter>();
            input = fighter.GetComponent<PrototypeInput>();
            ui = Object.FindFirstObjectByType<InnInterface>();
            orbit = Camera.main.GetComponent<OrbitCamera>();
            stairs = Object.FindFirstObjectByType<InnStairTraversal>();
            if (stairs == null || !stairs.Ready) throw new Exception("Existing staircase traversal component was not installed.");
            Call("Restart", true);
            Call("Show", Enum.Parse(typeof(InnInterface).GetNestedType("View", BindingFlags.NonPublic), "Play"));
            input.enabled = false;
            Vector3 forward = Vector3.ProjectOnPlane(stairs.Top - stairs.Bottom, Vector3.up).normalized;
            bottom = stairs.Bottom - forward * .55f; bottom.y = .04f;
            top = stairs.Top + forward * .35f; top.y += .03f;
            fighter.PlaceForStory(bottom, Quaternion.LookRotation(forward));
            orbit.ResumeFollow(true);
            destination = top;
            frameStart = Time.frameCount; timeStart = Time.time;
            startedAt = EditorApplication.timeSinceStartup;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
            SetPhase("Stairs");
        }
        catch (Exception e) { Fail(e.GetBaseException().Message); }
        return Status();
    }

    [Unity.Pipeline.Commands.CliCommand("innpolish", "traversal_status", "Read stair movement and camera checks")]
    public static string Status() => JsonUtility.ToJson(report, true);

    static void Tick()
    {
        if (!report.active) return;
        try
        {
            report.frames = Time.frameCount - frameStart;
            report.gameSeconds = Time.time - timeStart;
            report.peakHeight = Mathf.Max(report.peakHeight, fighter.transform.position.y);
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - startedAt > 60d)
                throw new Exception("Live traversal timed out or Play Mode stopped.");
            if (Time.frameCount <= phaseFrame) return;
            input.enabled = false;
            if (report.phase == "Stairs")
            {
                Vector3 delta = Vector3.ProjectOnPlane(destination - fighter.transform.position, Vector3.up);
                if (delta.magnitude < .23f && Mathf.Abs(fighter.transform.position.y - destination.y) < .3f)
                {
                    report.traversals++;
                    fighter.MoveInput = Vector2.zero;
                    if (report.traversals == 1) ScreenCapture.CaptureScreenshot(LogPath("StairTop.png"));
                    if (report.traversals >= 4) { cameraIndex = 0; PrepareCameraCase(); return; }
                    destination = report.traversals % 2 == 0 ? top : bottom;
                    SetPhase("Stairs");
                }
                else
                {
                    if (EditorApplication.timeSinceStartup - phaseAt > 12d)
                        throw new Exception("Stair passage blocked at " + fighter.transform.position + ", target " + destination);
                    Vector3 forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                    Vector3 right = Vector3.Cross(Vector3.up, forward);
                    fighter.MoveInput = new Vector2(Vector3.Dot(delta.normalized, right), Vector3.Dot(delta.normalized, forward));
                    fighter.RunRequested = true;
                }
            }
            else if (report.phase == "Camera")
            {
                fighter.ClearInput();
                if (EditorApplication.timeSinceStartup - phaseAt < .25d) return;
                if (!orbit.ClearanceSatisfied || orbit.CurrentDistance > orbit.indoorDistanceLimit + .1f)
                    throw new Exception("Camera obstructed or too far in case " + cameraIndex + ", position " + orbit.transform.position);
                report.cameraCases++;
                cameraIndex++;
                if (cameraIndex >= 24) Finish(); else PrepareCameraCase();
            }
        }
        catch (Exception e) { Fail(e.GetBaseException().Message); }
    }
    static void PrepareCameraCase()
    {
        Vector3[] places = { new(-5.1f,.04f,0f), new(0f,.04f,-4.1f), new(0f,.04f,4.1f) };
        fighter.PlaceForStory(places[cameraIndex / 8], Quaternion.identity);
        orbit.ResumeFollow(true);
        typeof(OrbitCamera).GetField("yaw", Hidden).SetValue(orbit, (cameraIndex % 8) * 45f);
        typeof(OrbitCamera).GetField("pitch", Hidden).SetValue(orbit, 24f);
        SetPhase("Camera");
    }
    static void SetPhase(string phase) { report.phase = phase; phaseAt = EditorApplication.timeSinceStartup; phaseFrame = Time.frameCount; }
    static void OnLog(string text, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors++; }
    static void Fail(string error) { report.failures.Add(error); Finish(); }
    static void Finish()
    {
        report.active = false; report.completed = true;
        if (report.frames < 10 || report.gameSeconds < .2f) report.failures.Add("No sustained live movement observed.");
        if (report.traversals != 4 || report.cameraCases != 24 || report.errors > 0) report.failures.Add("Incomplete traversal/camera checks or runtime errors.");
        report.passed = report.failures.Count == 0;
        report.phase = report.passed ? "Completed" : "Failed";
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        if (fighter != null) fighter.ClearInput();
        if (report.passed) Call("Restart", true);
        report.output = LogPath("IndoorTraversalVerification.json");
        File.WriteAllText(report.output, JsonUtility.ToJson(report,true));
    }
    static void Call(string method, params object[] args) => typeof(InnInterface).GetMethod(method, Hidden).Invoke(ui,args);
    static string LogPath(string name)
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath,"..","Logs"));
        Directory.CreateDirectory(root); return Path.Combine(root,name);
    }
}
