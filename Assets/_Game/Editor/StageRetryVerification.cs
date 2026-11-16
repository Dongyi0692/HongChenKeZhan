using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Integration checks against the existing scene and real UI callbacks. Damage is
// injected through ReceiveDamage to exercise progression, not animation accuracy.
public static class StageRetryVerification
{
    [Serializable] public sealed class Report
    {
        public string scope = "Existing InnDuel, live frames, real elite doorway entrance and dialogue camera, damage/progression and result-button callbacks. AI Update is temporarily suspended to isolate checkpoint behavior; entrance locomotion still runs normally. This is not an attack-animation or performance test. No scenes are created or saved. Returns to title on completion.";
        public bool active, completed, passed, framesAdvanced, gameTimeAdvanced;
        public string phase, outputPath, defeatScreenshot, entranceScreenshot;
        public bool entranceObservedOutside, entranceCrossedDoor, entranceCompleted, dialogueClearanceSatisfied;
        public float entranceDistanceTravelled;
        public int checks, frameStart, frameEnd, runtimeErrors, battlesVerified, retriesVerified, failureDialoguesVerified;
        public float gameTimeStart, gameTimeEnd, elapsedSeconds;
        public List<string> successes = new(), failures = new();
    }
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    static Report report = new() { phase = "NotStarted" };
    static InnInterface ui;
    static DuelEncounter duel;
    static PrototypeFighter fighter;
    static PrototypeInput input;
    static CombatActor player;
    static readonly Dictionary<EnemyCombatAI, bool> brainStates = new();
    static readonly Dictionary<int, Vector3> checkpointPositions = new();
    static readonly List<CombatActor> opponents = new();
    static Vector3 playerStart, entrancePreviousPosition;
    static bool entranceObserved, failureDialogueVerified;
    static int entranceMovingFrames;
    static int stageIndex, attempt, phaseFrame, sentryId, eliteId, allyHealth;
    static double startedAt, phaseAt;
    static float fightTimeAtStart;
    static bool doorAtStart;
    static DuelEncounter.EncounterStage ExpectedStage => stageIndex == 0 ? DuelEncounter.EncounterStage.Tutorial
        : stageIndex == 1 ? DuelEncounter.EncounterStage.EliteDuel : DuelEncounter.EncounterStage.Rescue;
    static string View => Field(ui, "view").ToString();
    static string Part => Field(ui, "part").ToString();

    [Unity.Pipeline.Commands.CliCommand("inncombat", "begin_retry_test", "Verify retries of all three actual InnDuel battles without saving a scene")]
    public static string Begin()
    {
        if (report.active) return Status();
        Detach();
        report = new Report { phase = "Preparing", frameStart = Time.frameCount, gameTimeStart = Time.time };
        startedAt = EditorApplication.timeSinceStartup;
        try
        {
            Require(EditorApplication.isPlaying && !EditorApplication.isPaused, "Start unpaused Play Mode first.");
            Require(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "InnDuel", "Expected existing InnDuel scene.");
            fighter = Object.FindFirstObjectByType<PrototypeFighter>();
            ui = Object.FindFirstObjectByType<InnInterface>();
            Require(fighter != null && ui != null, "Actual player and UI must be initialized.");
            duel = fighter.GetComponent<DuelEncounter>();
            input = fighter.GetComponent<PrototypeInput>();
            Call("Restart", true);
            player = duel.PlayerActor;
            Require(duel.HasRequiredBindings, duel.MissingBindingReason);
            sentryId = duel.TutorialActor.GetInstanceID();
            eliteId = duel.EliteActor.GetInstanceID();
            stageIndex = attempt = 0;
            entranceObserved = false;
            entranceMovingFrames = 0;
            report.active = true;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
            StartBattle();
        }
        catch (Exception error) { Fail(error.GetBaseException().ToString()); }
        return Status();
    }

    [Unity.Pipeline.Commands.CliCommand("inncombat", "status_retry_test", "Read three-battle checkpoint verification progress")]
    public static string Status()
    {
        if (report.active) Observe();
        return JsonUtility.ToJson(report, true);
    }

    static void StartBattle()
    {
        if (stageIndex == 0)
        {
            ShowPlay();
            PlacePlayerNear(duel.TutorialActor);
            // This retry fixture skips presentation but honors the new explicit story gates.
            Require(duel.RevealTutorialSentry() && duel.CompleteTutorialInstructions(), "Could not prepare completed tutorial prerequisite");
            PrepareWoundedBattleStart();
            Call("BeginTutorialFight");
        }
        else if (stageIndex == 2) { PrepareWoundedBattleStart(); Call("BeginRescue"); }
        else throw new InvalidOperationException("Elite battle must begin through the actual entrance and dialogue.");
        RecordBattleStart();
    }

    static void RecordBattleStart()
    {
        Require(duel.Stage == ExpectedStage && duel.IsCombatActive, "Could not start expected battle " + ExpectedStage);
        Check(player.Health == player.MaxHealth, ExpectedStage + " heals the wounded player to full on first entry");
        Check(Mathf.Approximately(fighter.Stamina, 37f) && fighter.SkillCooldownRemaining == 0f,
            ExpectedStage + " stage-entry healing preserves stamina and adds no skill cooldown");
        if (input != null) input.enabled = false;
        SuspendBrains();
        opponents.Clear();
        opponents.AddRange(duel.EnemyActors.Where(a => a != null && a.isActiveAndEnabled && a.Team == CombatTeam.Enemy && !a.IsDefeated));
        checkpointPositions.Clear();
        foreach (var actor in opponents) checkpointPositions.Add(actor.GetInstanceID(), actor.transform.position);
        playerStart = player.transform.position;
        fightTimeAtStart = (float)Field(ui, "battleStartFightLength");
        var door = Object.FindFirstObjectByType<InnEntranceDoors>();
        doorAtStart = door != null && door.OpenRequested;
        if (stageIndex == 2)
        {
            allyHealth = duel.EliteActor.Health;
            checkpointPositions.Add(duel.EliteActor.GetInstanceID(), duel.EliteActor.transform.position);
            Check(duel.EliteVictoryAchieved && duel.EliteActor.Team == CombatTeam.Player, "Rescue begins with victorious elite on player team");
        }
        Check(duel.CanRetryCurrentBattle && duel.RetryStage == ExpectedStage, ExpectedStage + " records its own retry checkpoint");
        Check(opponents.Count == (stageIndex == 2 ? Mathf.Max(1, duel.RescueEnemyCount) : 1), ExpectedStage + " starts with expected enemy count");
        SetPhase("Running");
    }

    static void PrepareWoundedBattleStart()
    {
        player.ResetActor(Mathf.Max(1, player.MaxHealth / 3));
        typeof(PrototypeFighter).GetProperty("Stamina").GetSetMethod(true).Invoke(fighter, new object[] { 37f });
        Check(player.Health < player.MaxHealth, ExpectedStage + " entry fixture begins wounded");
    }

    static void Tick()
    {
        if (!report.active) return;
        try
        {
            Observe();
            if (!EditorApplication.isPlaying) { Fail("Play Mode ended before checkpoint checks finished."); return; }
            if (report.elapsedSeconds > 75f) { Fail("Checkpoint verification exceeded 75 seconds in " + report.phase); return; }
            if (input != null) input.enabled = false;
            SuspendBrains();
            if (Time.frameCount <= phaseFrame) return;
            double elapsed = EditorApplication.timeSinceStartup - phaseAt;
            switch (report.phase)
            {
                case "Running":
                    if (Time.frameCount < phaseFrame + 3 || elapsed < .15d) return;
                    failureDialogueVerified = false;
                    DirtyAndDefeat();
                    SetPhase("AwaitDefeat");
                    break;
                case "AwaitDefeat":
                    if (View == "Dialogue" && !failureDialogueVerified) VerifyFailureDialogue();
                    FinishDialogue();
                    if (View != "Defeat")
                    {
                        if (elapsed > 4d) Fail("Expected failure result; actual UI " + View + ", stage " + duel.Stage);
                        return;
                    }
                    Check(failureDialogueVerified, ExpectedStage + " failure dialogue was inspected before continuing to the result");
                    Check(duel.Stage == DuelEncounter.EncounterStage.Failed && duel.RetryStage == ExpectedStage,
                        ExpectedStage + " failure retains correct checkpoint (attempt " + (attempt + 1) + ")");
                    if (stageIndex == 0 && attempt == 0)
                    {
                        report.defeatScreenshot = Path.Combine(LogFolder(), "StageRetryDefeat.png");
                        ScreenCapture.CaptureScreenshot(report.defeatScreenshot);
                    }
                    SetPhase("RetryDelay");
                    break;
                case "RetryDelay":
                    if (Time.frameCount < phaseFrame + 2 || elapsed < .12d) return;
                    if (attempt == 0)
                    {
                        var button = ui.GetComponentsInChildren<Button>(true).FirstOrDefault(b =>
                            b.gameObject.activeInHierarchy && b.GetComponentInChildren<Text>() != null
                            && b.GetComponentInChildren<Text>().text.StartsWith("重试本段", StringComparison.Ordinal));
                        Require(button != null, "Current-stage retry button was not present in actual failure UI.");
                        button.onClick.Invoke();
                    }
                    else Call("Restart", false); // Same route used by the R key.
                    if (input != null) input.enabled = false;
                    VerifyRetry();
                    report.retriesVerified++;
                    SetPhase("Settled");
                    break;
                case "Settled":
                    if (Time.frameCount < phaseFrame + 2 || elapsed < .12d) return;
                    Check(duel.Stage == ExpectedStage && View == "Play", ExpectedStage + " stays playable on subsequent frames");
                    attempt++;
                    if (attempt < 2) SetPhase("Running");
                    else
                    {
                        foreach (var actor in opponents) Kill(actor, player);
                        var expected = stageIndex == 0 ? DuelEncounter.EncounterStage.EliteReady
                            : stageIndex == 1 ? DuelEncounter.EncounterStage.EliteVictory : DuelEncounter.EncounterStage.Complete;
                        Check(duel.Stage == expected, ExpectedStage + " can still be won after repeated retry");
                        if (stageIndex == 0)
                            Check(!duel.EliteActor.gameObject.activeSelf && !duel.EliteEntranceInProgress && !duel.EliteEntranceComplete,
                                "Tutorial defeat does not instantly reveal or finish the elite entrance");
                        report.battlesVerified++;
                        SetPhase("Advance");
                    }
                    break;
                case "Advance":
                    if (Time.frameCount < phaseFrame + 2 || elapsed < .12d) return;
                    if (stageIndex == 0)
                    {
                        Check(duel.TutorialActor.IsDefeated, "Tutorial victory persists before the elite duel");
                        SetPhase("AwaitEliteEntrance");
                    }
                    else if (stageIndex == 1)
                    {
                        FinishDialogue();
                        if (View != "Play" || duel.EliteActor.Team != CombatTeam.Player)
                        {
                            if (elapsed > 4d) Fail("Elite victory did not complete its ally transition.");
                            return;
                        }
                        stageIndex++; attempt = 0; StartBattle();
                    }
                    else
                    {
                        FinishDialogue();
                        if (View != "Victory")
                        {
                            if (elapsed > 4d) Fail("Rescue victory result was not reached.");
                            return;
                        }
                        Check(!duel.CanRetryCurrentBattle, "Completed run does not retain a combat retry route");
                        Call("Restart", true);
                        Check(View == "Title" && Part == "Road" && duel.Stage == DuelEncounter.EncounterStage.Arrival,
                            "Return to title still resets the entire run");
                        Check(!duel.EliteVictoryAchieved && !duel.CanRetryCurrentBattle && duel.TutorialActor.Health == duel.TutorialActor.MaxHealth,
                            "New run clears old checkpoint and restores the initial sentry");
                        SetPhase("Finalize");
                    }
                    break;
                case "AwaitEliteEntrance":
                    ObserveEliteEntrance();
                    if (!duel.EliteEntranceComplete || View != "Dialogue")
                    {
                        if (elapsed > 15d) Fail("Elite did not complete its actual entrance and open dialogue. UI " + View
                            + ", entrance active " + duel.EliteEntranceInProgress + ", stopped " + duel.EliteEntranceStoppedAtDoor);
                        return;
                    }
                    Check(report.entranceObservedOutside, "Elite first becomes visible outside the actual doorway");
                    Check(report.entranceDistanceTravelled > 1.5f && entranceMovingFrames >= 3,
                        "Elite advances through live locomotion frames instead of appearing at the hall destination");
                    Check(report.entranceCrossedDoor && !duel.EliteEntranceStoppedAtDoor,
                        "Elite passes through the doorway without blocked-route fallback");
                    Check(Vector3.ProjectOnPlane(duel.EliteActor.transform.position - duel.eliteArrivalPosition, Vector3.up).magnitude < .3f,
                        "Elite reaches the hall dialogue stance");
                    Check(!duel.IsCombatActive && !duel.CanRetryCurrentBattle,
                        "Entrance and pre-fight dialogue do not capture a premature combat checkpoint");
                    report.entranceCompleted = true;
                    SetPhase("EliteDialogueCamera");
                    break;
                case "EliteDialogueCamera":
                    if (Time.frameCount < phaseFrame + 3 || elapsed < .65d) return;
                    Check(View == "Dialogue" && !duel.EliteEntranceInProgress, "Dialogue freezes gameplay only after the elite has settled");
                    Check(FacingOther(player.transform, duel.EliteActor.transform) && FacingOther(duel.EliteActor.transform, player.transform),
                        "Both living dialogue participants face each other horizontally");
                    var orbit = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
                    Check(orbit != null && orbit.enabled && orbit.IsFramingActors,
                        "Dialogue uses the enabled shared camera framing mode");
                    report.dialogueClearanceSatisfied = orbit.ClearanceSatisfied;
                    Check(report.dialogueClearanceSatisfied, "Dialogue camera has obstruction clearance after its transition");
                    report.entranceScreenshot = Path.Combine(LogFolder(), "EliteEntranceDialogue.png");
                    ScreenCapture.CaptureScreenshot(report.entranceScreenshot);
                    SetPhase("EliteDialogueCaptured");
                    break;
                case "EliteDialogueCaptured":
                    if (Time.frameCount < phaseFrame + 2) return;
                    stageIndex = 1; attempt = 0;
                    PrepareWoundedBattleStart();
                    FinishDialogue();
                    RecordBattleStart();
                    Check(duel.EliteEntranceComplete, "Second-stage checkpoint is captured after the actual entrance");
                    break;
                case "Finalize":
                    if (Time.frameCount > phaseFrame + 2) Finish();
                    break;
            }
        }
        catch (Exception error) { Fail(error.GetBaseException().ToString()); }
    }

    static void ObserveEliteEntrance()
    {
        if (!duel.EliteActor.gameObject.activeInHierarchy) return;
        var door = Object.FindFirstObjectByType<InnEntranceDoors>();
        Require(door != null, "Actual doorway is required to verify the entrance route.");
        Vector3 position = duel.EliteActor.transform.position;
        float inward = duel.eliteArrivalPosition.z >= door.transform.position.z ? 1f : -1f;
        float doorDepth = (position.z - door.transform.position.z) * inward;
        if (!entranceObserved)
        {
            entranceObserved = true;
            report.entranceObservedOutside = doorDepth < -.5f;
            entrancePreviousPosition = position;
            Check(door.OpenRequested && duel.EliteEntranceInProgress, "Arrival opens the real door before elite movement");
        }
        float moved = Vector3.ProjectOnPlane(position - entrancePreviousPosition, Vector3.up).magnitude;
        if (moved > .001f) entranceMovingFrames++;
        report.entranceDistanceTravelled += moved;
        entrancePreviousPosition = position;
        if (doorDepth > .25f) report.entranceCrossedDoor = true;
    }

    static bool FacingOther(Transform actor, Transform other)
    {
        Vector3 toward = Vector3.ProjectOnPlane(other.position - actor.position, Vector3.up);
        Vector3 forward = Vector3.ProjectOnPlane(actor.forward, Vector3.up);
        return toward.sqrMagnitude > .01f && Vector3.Angle(forward, toward) < 5f;
    }

    static void DirtyAndDefeat()
    {
        Require(duel.IsCombatActive && View == "Play", "Failure injection requires a running battle.");
        fighter.ClearInput();
        typeof(PrototypeFighter).GetProperty("Stamina").GetSetMethod(true).Invoke(fighter, new object[] { 7f });
        if (stageIndex == 2)
        {
            Require(duel.EliteActor.ReceiveDamage(opponents[opponents.Count - 1], duel.EliteActor.MaxHealth + 1, 0f, 0f, true),
                "Could not apply lethal grunt pressure to the protected ally");
            Check(duel.Stage == DuelEncounter.EncounterStage.Rescue && duel.EliteActor.Health == 1 && !duel.EliteActor.IsDefeated,
                "Rescue ally survives lethal grunt damage at one health");
            if (opponents.Count > 1) Kill(opponents[0], player);
        }
        else Require(opponents[0].ReceiveDamage(player, 7, 0f, 0f, true), "Could not damage current opponent before failure.");
        Kill(player, opponents[opponents.Count - 1]);
        Check(duel.Stage == DuelEncounter.EncounterStage.Failed && player.IsDefeated, ExpectedStage + " uses the real player-defeat transition");
    }

    static void VerifyFailureDialogue()
    {
        string prefix = ExpectedStage + " failure dialogue " + (attempt + 1) + ": ";
        CombatActor expected = stageIndex == 0 ? duel.TutorialActor : stageIndex == 1 ? duel.EliteActor
            : opponents.Where(a => a != null && a.gameObject.activeInHierarchy && a.Team == CombatTeam.Enemy && !a.IsDefeated)
                .OrderBy(a => (a.transform.position - player.transform.position).sqrMagnitude).FirstOrDefault();
        var participant = (CombatActor)typeof(InnInterface).GetMethod("ResolveDialogueParticipant", Hidden).Invoke(ui, null);
        Check(expected != null && participant == expected, prefix + "selects the surviving opponent from the current battle");
        Check(participant.gameObject.activeInHierarchy && participant.Team == CombatTeam.Enemy && !participant.IsDefeated
            && (stageIndex != 2 || participant != duel.EliteActor),
            prefix + "excludes inactive actors, defeated enemies and the fallen allied elite");
        var dialogueOrbit = (OrbitCamera)Field(ui, "orbit");
        Check(dialogueOrbit != null && dialogueOrbit.enabled && dialogueOrbit.IsFramingActors
            && (Transform)Field(dialogueOrbit, "frameA") == player.transform
            && (Transform)Field(dialogueOrbit, "frameB") == participant.transform,
            prefix + "actual camera frames the player and that same surviving opponent");
        failureDialogueVerified = true;
        report.failureDialoguesVerified++;
    }

    static void VerifyRetry()
    {
        string prefix = ExpectedStage + " retry " + (attempt + 1) + ": ";
        Check(duel.Stage == ExpectedStage && duel.IsCombatActive && View == "Play", prefix + "returns directly to current battle without pre-fight dialogue");
        Check(player.Health == player.MaxHealth && Mathf.Abs(fighter.Stamina - fighter.MaxStamina) < .01f,
            prefix + "restores full player health and stamina");
        Check(!player.ControlLocked && player.GetComponent<CharacterController>().enabled && !player.IsAttacking
            && fighter.State == PrototypeFighter.ActionState.Ready && fighter.MoveInput == Vector2.zero
            && !fighter.GuardRequested && fighter.SkillCooldownRemaining <= 0f && input.StoryAllowsCombat,
            prefix + "restores controls and clears attack, movement and cooldown state");
        Check(Vector3.Distance(player.transform.position, playerStart) < .35f, prefix + "returns player to current-segment start position");
        var living = duel.EnemyActors.Where(a => a != null && a.isActiveAndEnabled && a.Team == CombatTeam.Enemy && !a.IsDefeated).ToList();
        Check(living.Count == opponents.Count && living.All(a => opponents.Contains(a)), prefix + "restores same enemy instances without duplicate waves");
        foreach (var actor in opponents)
            Check(actor.Health == actor.MaxHealth && actor.Team == CombatTeam.Enemy && !actor.ControlLocked
                && actor.GetComponent<CharacterController>().enabled && actor.GetComponent<EnemyCombatAI>().CombatEnabled
                && !actor.IsAttacking && Vector3.Distance(actor.transform.position, checkpointPositions[actor.GetInstanceID()]) < .35f,
                prefix + actor.DisplayName + " restores health, hostile team, AI and start position");
        Check(duel.TutorialActor.GetInstanceID() == sentryId && duel.EliteActor.GetInstanceID() == eliteId,
            prefix + "keeps existing actors and earlier progression");
        if (stageIndex > 0) Check(duel.TutorialActor.IsDefeated, prefix + "does not revive completed tutorial opponent");
        if (stageIndex == 2)
        {
            var ally = duel.EliteActor;
            Check(duel.EliteVictoryAchieved && ally.Team == CombatTeam.Player && ally.Health == allyHealth
                && !ally.IsDefeated && !ally.ControlLocked && ally.GetComponent<EnemyCombatAI>().CombatEnabled
                && Vector3.Distance(ally.transform.position, checkpointPositions[eliteId]) < .35f,
                prefix + "restores defeated ally to rescue checkpoint while retaining elite victory");
        }
        else Check(!duel.EliteVictoryAchieved && duel.EliteActor.Team == CombatTeam.Enemy, prefix + "does not grant premature elite victory or ally team");
        Check(Mathf.Abs((float)Field(ui, "fightLength") - fightTimeAtStart) < .01f, prefix + "discards only failed attempt time");
        var door = Object.FindFirstObjectByType<InnEntranceDoors>();
        Check(door == null || door.OpenRequested == doorAtStart, prefix + "restores current-segment door state");
    }

    static void Kill(CombatActor victim, CombatActor source)
    {
        victim.Invulnerable = false;
        Require(victim.ReceiveDamage(source, victim.MaxHealth + 1, 0f, 0f, true), "Could not defeat " + victim.DisplayName);
    }
    static void SuspendBrains()
    {
        foreach (var brain in Object.FindObjectsByType<EnemyCombatAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!brainStates.ContainsKey(brain)) brainStates.Add(brain, brain.enabled);
            brain.enabled = false;
        }
    }
    static void PlacePlayerNear(CombatActor target)
    {
        var placement = typeof(CombatActor).Assembly.GetType("FengChenInn.ActorPlacement").GetMethod("TryPlace", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (Vector3 offset in new[] { Vector3.right * 1.8f, Vector3.left * 1.8f, Vector3.back * 1.8f })
            if ((bool)placement.Invoke(null, new object[] { fighter.GetComponent<CharacterController>(), target.transform.position + offset, Quaternion.identity, float.NaN }))
            {
                fighter.PlaceForStory(fighter.transform.position, Quaternion.identity);
                return;
            }
        throw new InvalidOperationException("No clear current-scene test stance near " + target.DisplayName);
    }
    static void FinishDialogue()
    {
        for (int count = 0; count < 64 && View == "Dialogue"; count++) Call("AdvanceDialogue");
    }
    static object Field(object value, string name) => value.GetType().GetField(name, Hidden).GetValue(value);
    static void Call(string method, params object[] values) => typeof(InnInterface).GetMethod(method, Hidden).Invoke(ui, values);
    static void ShowPlay() => Call("Show", Enum.Parse(typeof(InnInterface).GetNestedType("View", BindingFlags.NonPublic), "Play"));
    static void SetPhase(string phase) { report.phase = phase; phaseAt = EditorApplication.timeSinceStartup; phaseFrame = Time.frameCount; }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static void Check(bool value, string message)
    {
        report.checks++;
        if (!value) throw new InvalidOperationException(message);
        report.successes.Add(message);
    }
    static void Observe()
    {
        report.frameEnd = Time.frameCount;
        report.gameTimeEnd = Time.time;
        report.elapsedSeconds = (float)(EditorApplication.timeSinceStartup - startedAt);
        report.framesAdvanced = report.frameEnd > report.frameStart + 5;
        report.gameTimeAdvanced = report.gameTimeEnd > report.gameTimeStart + .1f;
    }
    static void OnLog(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.runtimeErrors++; }
    static void Fail(string reason) { report.failures.Add(reason); Finish(); }
    static void Finish()
    {
        Observe();
        if (!report.framesAdvanced || !report.gameTimeAdvanced) report.failures.Add("Live frames and game time did not advance sufficiently.");
        if (report.battlesVerified != 3 || report.retriesVerified != 6) report.failures.Add("Expected all three battles and six retries.");
        if (report.runtimeErrors > 0) report.failures.Add("Runtime errors: " + report.runtimeErrors);
        report.active = false; report.completed = true; report.passed = report.failures.Count == 0;
        report.phase = report.passed ? "Completed" : "Failed";
        Detach();
        foreach (var pair in brainStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        brainStates.Clear();
        if (fighter != null) fighter.ClearInput();
        // Existing title state is intentional; stopping Play restores all authored data.
        report.outputPath = Path.Combine(LogFolder(), "StageRetryVerification.json");
        File.WriteAllText(report.outputPath, JsonUtility.ToJson(report, true));
    }
    static string LogFolder()
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
        Directory.CreateDirectory(path);
        return path;
    }
    static void Detach()
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
    }
}
