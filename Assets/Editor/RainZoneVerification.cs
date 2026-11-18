using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;

// Play Mode verification for the indoor/outdoor rain split and the promoted wet footsteps.
// Command: write "run" into Temp/RainZoneVerify.command (or use the menu item).
[InitializeOnLoad]
public static class RainZoneVerification
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Report = "Logs/RainZoneVerification.txt";
    const string Command = "Temp/RainZoneVerify.command";

    static IEnumerator<double> run;
    static bool pendingRun;
    static double next;
    static int errors;
    static bool background, hadRainPreference;
    static float originalRainVolume;
    static InnInterface ui;
    static PrototypeFighter fighter;
    static PrototypeInput input;
    static HongfuFootsteps steps;
    static MenuAudio audio;
    static InnRainZone zone;
    static FootstepTestDrive drive;
    static readonly List<string> results = new List<string>();
    static readonly List<string> stepLog = new List<string>();
    static AudioSource[] voices;

    static RainZoneVerification()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("RainZoneVerify", false)) {
                SessionState.SetBool("RainZoneVerify", false);
                Begin();
            }
        };
    }

    public static void StartTest()
    {
        if (EditorApplication.isPlaying) Begin();
        else { SessionState.SetBool("RainZoneVerify", true); EditorApplication.isPlaying = true; }
    }

    static void Begin()
    {
        results.Clear(); stepLog.Clear(); errors = 0;
        Application.logMessageReceived += Log;
        run = Verify(); next = EditorApplication.timeSinceStartup + 3;
    }

    static void Update()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (File.Exists(Command))
            {
                string value = File.ReadAllText(Command).Trim(); File.Delete(Command);
                if (value == "run")
                {
                    // A play session that is still running keeps the assemblies it started with,
                    // so a rebuild needs a fresh domain before the new logic can be tested.
                    if (EditorApplication.isPlaying) { pendingRun = true; EditorApplication.isPlaying = false; }
                    else StartTest();
                }
            }
            if (pendingRun && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                pendingRun = false;
                StartTest();
            }
            if (run == null || EditorApplication.timeSinceStartup < next) return;
            if (run.MoveNext()) next = EditorApplication.timeSinceStartup + run.Current;
            else { run = null; Cleanup(); }
        }
        catch (Exception ex)
        {
            results.Add("FAIL: " + ex.Message);
            results.Add(ex.StackTrace);
            Save(); run = null; Cleanup();
        }
    }

    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; results.Add(" LOGERROR: " + message); } }

    static void Save() { Directory.CreateDirectory("Logs"); File.WriteAllLines(Report, results); }

    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        results.Add("PASS: " + message); Save();
    }

    static void Cleanup()
    {
        Application.logMessageReceived -= Log;
        if (steps) steps.StepPlayed -= OnStep;
        if (drive) UnityEngine.Object.DestroyImmediate(drive);
        if (input) input.enabled = true;
        Time.timeScale = 1;
        if (audio) audio.SetRainVolume(originalRainVolume);
        if (!hadRainPreference) PlayerPrefs.DeleteKey("FengChenInn.UI.rainVolume");
        PlayerPrefs.Save();
        if (ui) ui.enabled = true;
        Application.runInBackground = background;
        File.WriteAllLines("Logs/RainZoneFootsteps.txt", stepLog);
        results.Add("COMPLETE"); Save();
    }

    static float Roof() => audio && audio.RoofRain ? audio.RoofRain.volume : -1f;
    static float Open() => audio && audio.OutdoorRain ? audio.OutdoorRain.volume : -1f;
    static float Power() { float r = Roof(), o = Open(); return Mathf.Sqrt(Mathf.Max(0, r * r + o * o)); }

    static void Place(Vector3 position)
    {
        if (drive) drive.strength = 0;
        fighter.ClearInput();
        var cc = fighter.GetComponent<CharacterController>();
        cc.enabled = false; fighter.transform.position = position; cc.enabled = true;
        Physics.SyncTransforms();
    }

    static object Field(object target, string name)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    static void OnStep(FootstepGround ground, AudioClip clip)
    {
        float level = 0;
        foreach (var v in voices) if (v && v.isPlaying && v.volume > level) level = v.volume;
        stepLog.Add(Time.time.ToString("F3") + " surface=" + ground + " clip=" + clip.name
            + " voice=" + level.ToString("F3") + " speed=" + steps.ActualSpeed.ToString("F2")
            + " position=" + fighter.transform.position.ToString("F2"));
    }

    static IEnumerator<double> Verify()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; yield return 8; }
        background = Application.runInBackground; Application.runInBackground = true; EditorApplication.isPaused = false;

        ui = UnityEngine.Object.FindFirstObjectByType<InnInterface>();
        audio = UnityEngine.Object.FindFirstObjectByType<MenuAudio>();
        fighter = UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>();
        Check(ui && audio && fighter, "Existing Stage12 interface, audio manager and player loaded");

        steps = fighter.GetComponent<HongfuFootsteps>();
        input = fighter.GetComponent<PrototypeInput>();
        zone = fighter.GetComponent<InnRainZone>();
        Check(steps && zone, "Existing footstep probe and new shelter zone both attached to the player");
        voices = steps.GetComponentsInChildren<AudioSource>()
            .Where(s => s.name.StartsWith("Hongfu footsteps")).ToArray();

        hadRainPreference = PlayerPrefs.HasKey("FengChenInn.UI.rainVolume");
        originalRainVolume = audio.RainVolume;
        audio.SetRainVolume(.12f);

        Check(audio.OutdoorRain && audio.OutdoorRain.clip, "Outdoor rain loop resolved from Resources");
        Check(audio.OutdoorRain.clip.frequency == 48000 && audio.OutdoorRain.clip.channels == 2,
            "Outdoor loop is 48 kHz stereo; " + audio.OutdoorRain.clip.frequency + " Hz / " + audio.OutdoorRain.clip.channels + " ch");
        Check(audio.OutdoorRain.loop && audio.RoofRain.loop, "Both rain layers loop instead of one-shot playback");
        Check(audio.OutdoorRain.clip.length > 70f,
            "Outdoor loop keeps a natural length; " + audio.OutdoorRain.clip.length.ToString("F1") + " s");
        Check(audio.GetComponentsInChildren<AudioSource>().Count(s => s.gameObject.name == "Rain outdoor ambience") == 1,
            "Exactly one outdoor source; nothing duplicated at start");

        typeof(InnInterface).GetMethod("StartGame", Private).Invoke(ui, null);
        ui.enabled = false; input.enabled = false; Time.timeScale = 1;
        fighter.SetDialoguePose(false);
        fighter.GetComponent<CombatActor>().ControlLocked = false;
        audio.SetMenu(false);
        drive = fighter.gameObject.AddComponent<FootstepTestDrive>();
        drive.fighter = fighter;
        steps.StepPlayed += OnStep;
        yield return .8;
        CombatAudioSystem combat = CombatAudioSystem.Instance;

        // ---- A: on the open road -------------------------------------------------
        Place(new Vector3(-4, 0, -20)); yield return 3f;
        Check(InnRainZone.Active, "Shelter state active once gameplay starts");
        Check(InnRainZone.Outdoor > .98f, "Open road resolves outdoors; mix=" + InnRainZone.Outdoor.ToString("F2"));
        float bed = Power();
        Check(Roof() < .015f && Open() > .09f,
            "Outdoor rain carries the bed outside; roof=" + Roof().ToString("F3") + " open=" + Open().ToString("F3"));
        Check(audio.OutdoorRain.isPlaying && audio.RoofRain.isPlaying,
            "Both loops keep playing; a zone change never restarts a source");
        Check(steps.CurrentGround == FootstepGround.WetGround && zone.GroundName != null,
            "Ground under the player is WetGround; " + zone.GroundName);

        voices = steps.GetComponentsInChildren<AudioSource>().Where(s => s.name.StartsWith("Hongfu footsteps")).ToArray();
        Check(voices.All(v => Mathf.Approximately(v.minDistance, 4f) && Mathf.Approximately(v.maxDistance, 18f)),
            "Wet footsteps use 4 m near field so the follow camera never attenuates them");
        Check(voices.All(v => v.spatialBlend == 1 && !v.loop && v.priority == 100),
            "Existing 3D one-shot voices unchanged apart from the near field");

        // ---- B: walking in -------------------------------------------------------
        int before = steps.TotalSteps;
        // A walk-strength drive keeps the existing locomotion clip in sync; at full run the
        // distance fallback waits for a whole stride, which needs a longer window.
        drive.direction = Vector3.forward; drive.strength = .5f;
        var wetClips = (AudioClip[])Field(steps, "wet");
        var woodClips = (AudioClip[])Field(steps, "wood");
        results.Add("INFO: wet=" + (wetClips == null ? "null" : wetClips.Length.ToString())
            + " wood=" + (woodClips == null ? "null" : woodClips.Length.ToString())
            + " ground=" + steps.CurrentGround + " firstWet=" + (wetClips != null && wetClips.Length > 0 ? wetClips[0].name : "-"));
        float walkUntil = Time.time + 4f;
        while (Time.time < walkUntil) yield return 0;
        float walkSpeed = steps.ActualSpeed;
        drive.strength = 0; yield return .2;
        var outdoor = stepLog.Where(l => l.Contains("surface=WetGround")).ToList();
        Check(steps.TotalSteps - before >= 2 && outdoor.Count >= 2,
            "Walking outdoors triggers wet steps; count=" + (steps.TotalSteps - before)
            + " speed=" + walkSpeed.ToString("F2"));
        Check(stepLog.All(l => l.Contains("clip=WetGround")),
            "Every outdoor step uses the promoted wet set; " + string.Join(", ", stepLog.Select(l => l.Split(' ')[2]).Distinct()));
        float voicePeak = stepLog.Select(l => float.Parse(l.Split(' ')[3].Replace("voice=", ""))).DefaultIfEmpty(0).Max();
        Check(voicePeak > .55f, "Wet step source level raised; peak voice=" + voicePeak.ToString("F3"));
        Check(steps.LastClip != null && !steps.LastClip.name.StartsWith("WoodFloor"), "Wet set legitimately distinct from wood");

        // ---- C: crossing the porch into the hall --------------------------------
        Place(new Vector3(-4, 0, -20)); yield return 1.2f;
        float outsidePower = Power();
        var mid = new List<float>();
        int overlapFrames = 0, frames = 0;
        Place(new Vector3(0, .25f, -2));
        float start = Time.time;
        while (Time.time - start < 3.2f)
        {
            float p = Power();
            if (Roof() > .03f && Open() > .03f) overlapFrames++;
            if (frames > 0 && p > 0) mid.Add(p);
            frames++;
            if (Roof() > .09f && Open() < .01f) break;
            yield return .05;
        }
        yield return .4;
        Check(frames > 18 && overlapFrames > 8,
            "Indoor and outdoor layers overlap through the doorway; frames=" + frames + ", overlap=" + overlapFrames);
        Check(mid.Count > 0 && mid.Max() < outsidePower * 1.25f,
            "Crossfade never stacks the two loops louder than one bed; peak=" + mid.DefaultIfEmpty(0).Max().ToString("F3") + " vs bed=" + outsidePower.ToString("F3"));
        Check(mid.Count > 0 && mid.Min() > outsidePower * .5f,
            "Crossfade never cuts the bed to silence; minimum=" + mid.DefaultIfEmpty(0).Min().ToString("F3"));
        Check(InnRainZone.Outdoor < .02f && zone.Changes >= 1,
            "Hall resolves indoors after the debounce; changes=" + zone.Changes);
        Check(Open() < .01f && Roof() > .085f,
            "Roof rain takes over inside; roof=" + Roof().ToString("F3") + " open=" + Open().ToString("F3"));
        Check(zone.GroundName != null && zone.GroundName.Contains("GroundFloor"),
            "Hall floor recognised from the existing surface map; " + zone.GroundName);

        // ---- D: indoor walking stays on wood ------------------------------------
        stepLog.Clear();
        before = steps.TotalSteps;
        Place(new Vector3(-2.5f, .25f, 1.5f)); yield return .3;
        drive.direction = Vector3.right; drive.strength = .5f; yield return 2.4;
        drive.strength = 0; yield return .3;
        Check(stepLog.Count >= 2 && stepLog.All(l => l.Contains("surface=WoodFloor")),
            "Indoor movement keeps wooden steps and roof rain; steps=" + (steps.TotalSteps - before));
        Check(InnRainZone.Outdoor < .02f && Open() < .01f, "Walking inside never flickers back to outdoor rain");

        // ---- E: back out through the door ---------------------------------------
        stepLog.Clear();
        Place(new Vector3(0, 0, 8)); yield return 3f;
        Check(InnRainZone.Outdoor > .98f, "Backyard resolves outdoors again");
        Check(Open() > .09f && Roof() < .015f,
            "Outdoor rain restored on the backyard side; open=" + Open().ToString("F3"));
        drive.direction = Vector3.right; drive.strength = .55f; yield return 2f;
        drive.strength = 0; yield return .2;
        Check(stepLog.Count >= 2 && stepLog.All(l => l.Contains("surface=WetGround")),
            "Wet steps resume as soon as the player is outside");

        // ---- F: combat against the new bed --------------------------------------
        float bedBeforeCombat = Roof();
        combat = CombatAudioSystem.Instance;
        Check(combat != null, "Existing combat audio system present");
        int hits = combat.HitCount, blocks = combat.BlockCount;
        combat.Hit(fighter.transform.position + Vector3.up);
        combat.Block(fighter.transform.position + Vector3.up);
        yield return .5;
        Check(combat.HitCount > hits && combat.BlockCount > blocks, "Hit and block feedback still fire");
        Check(Mathf.Abs(Roof() - bedBeforeCombat) < .006f,
            "Combat does not duck or restart the rain bed; delta=" + (Roof() - bedBeforeCombat).ToString("F4"));
        Check(audio.RoofRain.isPlaying && audio.OutdoorRain.isPlaying, "Rain keeps running through combat");

        // ---- G: repeated in/out --------------------------------------------------
        int changes = zone.Changes;
        int sourceCount = audio.GetComponentsInChildren<AudioSource>().Count(s => s.gameObject.name == "Rain outdoor ambience");
        int samplesStart = audio.OutdoorRain.timeSamples;
        for (int cycle = 0; cycle < 4; cycle++)
        {
            Place(new Vector3(0, .25f, -2)); yield return 1.2f;
            Place(new Vector3(-4, 0, -20)); yield return 1.2f;
        }
        Place(new Vector3(0, .25f, -2)); yield return 2.8f;
        Check(zone.Changes - changes >= 6, "Repeated crossings keep committing; new changes=" + (zone.Changes - changes));
        Check(audio.GetComponentsInChildren<AudioSource>().Count(s => s.gameObject.name == "Rain outdoor ambience") == sourceCount && sourceCount == 1,
            "Repeated crossings never duplicate the outdoor source");
        Check(audio.OutdoorRain.isPlaying && audio.OutdoorRain.timeSamples != samplesStart,
            "Outdoor loop keeps its playback position across crossings (no restart)");
        Check(InnRainZone.Outdoor < .02f && Open() < .01f, "Final state indoors is stable, no stuck crossfade");

        // ---- indoor combat, now that the duel audio exists ----------------------
        float indoorBed = Roof();
        int indoorHits = combat.HitCount;
        combat.Hit(fighter.transform.position + Vector3.up);
        combat.Block(fighter.transform.position + Vector3.up);
        yield return .5;
        Check(combat.HitCount > indoorHits, "Hit feedback still fires under the roof");
        Check(Mathf.Abs(Roof() - indoorBed) < .006f,
            "Indoor combat leaves the roof rain bed untouched; delta=" + (Roof() - indoorBed).ToString("F4"));
        Check(errors == 0, "No runtime errors during rain zone verification");

        // ---- reference numbers --------------------------------------------------
        results.Add("REPORT: bed=" + Power().ToString("F3") + " roofSample=" + audio.RoofRain.timeSamples
            + " outdoorSample=" + audio.OutdoorRain.timeSamples);
        results.Add("REPORT: wetSet=" + string.Join(",", Resources.LoadAll<AudioClip>("Footsteps/WetGroundLoud").Select(c => c.name)));
        results.Add("REPORT: wetVolume=" + steps.volume + " minDistance=" + steps.minDistance + " transition=" + audio.rainTransition);
        Save();
    }
}
