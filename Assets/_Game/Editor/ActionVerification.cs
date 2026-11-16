using System;
using System.Collections.Generic;
using System.IO;
using FengChenInn;
using UnityEditor;
using UnityEngine;

public static class ActionVerification
{
    private static IEnumerator<float> routine;
    private static float nextTime;
    private static readonly List<string> results = new List<string>();
    private static PrototypeFighter fighter;
    public static bool Completed { get; private set; }
    public static int FailureCount { get; private set; }
    [MenuItem("FengChenInn/Verify Actions (Play Mode)")]
    public static void Start()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        fighter = UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>();
        if (fighter == null) throw new InvalidOperationException("Open ActionLab first.");
        fighter.GetComponent<PrototypeInput>().enabled = false;
        Application.runInBackground = true;
        // Batch mode renders thousands of frames per second; CharacterController.isGrounded
        // flaps when the per-frame gravity step drops below the skin width. Capping the
        // frame rate keeps contact detection (and every check that depends on it) deterministic.
        Application.targetFrameRate = 60;
        Completed = false;
        FailureCount = 0;
        results.Clear(); routine = Verify(); nextTime = Time.time;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        if (Time.time < nextTime) return;
        try
        {
            if (routine.MoveNext()) { nextTime = Time.time + routine.Current; return; }
            Directory.CreateDirectory("Verification");
            File.WriteAllLines("Verification/actions.txt", results);
            foreach (string line in results) if (line.StartsWith("FAIL")) FailureCount++;
            fighter.GetComponent<PrototypeInput>().enabled = true;
            Debug.Log("ACTION_VERIFICATION_COMPLETE " + string.Join(" | ", results));
            EditorApplication.update -= Tick;
            Completed = true;
        }
        catch (Exception error)
        {
            EditorApplication.update -= Tick;
            fighter.GetComponent<PrototypeInput>().enabled = true;
            Debug.LogException(error);
            FailureCount = Mathf.Max(1, FailureCount);
            Completed = true;
        }
    }
    private static void Check(bool condition, string message)
    {
        results.Add((condition ? "PASS " : "FAIL ") + message);
    }
    private static void CheckGrip(RocketboxVisual rig, string label)
    {
        if (rig == null || rig.weapon == null || rig.rightArm.end == null) { Check(false, label + " grip exists"); return; }
        Transform hand = rig.rightArm.end;
        Transform index = hand.Find("Bip01 R Finger1"), middle = hand.Find("Bip01 R Finger2"), pinky = hand.Find("Bip01 R Finger4");
        if (index == null || middle == null || pinky == null) { Check(false, label + " finger bones exist"); return; }
        Vector3 center = Vector3.Lerp(hand.position, (index.position + middle.position + pinky.position) / 3f, 0.65f);
        Vector3 actual = rig.weapon.TransformPoint(Vector3.forward * 0.09f);
        Check(Vector3.Distance(actual, center) < 0.01f, label + " grip is centered in hand");
        Check(Vector3.Dot((index.position - pinky.position).normalized, rig.weapon.forward) > 0.95f, label + " blade follows hand grip axis");
    }
    private static IEnumerator<float> Verify()
    {
        fighter.ResetPose(); yield return 0.4f;
        RocketboxVisual rig = fighter.skinnedVisual;
        float standingPelvis = rig != null ? rig.pelvis.position.y - fighter.transform.position.y : 0f;
        bool walksReady = rig != null && rig.walkMotions != null && rig.walkMotions.Length == 8 && Array.TrueForAll(rig.walkMotions, x => x != null);
        Check(walksReady && rig.walkMotions[0].name == "WalkForward", "Original forward walk clip restored");
        if (walksReady)
        {
            rig.walkMotions[0].SampleAnimation(rig.gameObject, 0f);
            Quaternion firstWalkPose = rig.leftLeg.upper.localRotation;
            rig.walkMotions[0].SampleAnimation(rig.gameObject, 0.5f);
            Check(Quaternion.Angle(firstWalkPose, rig.leftLeg.upper.localRotation) > 5f, "Female walk animates leg bones");
        }
        Check(rig != null && rig.GetComponentInChildren<SkinnedMeshRenderer>() != null, "Skinned woman is active");
        int frame = Time.frameCount; float time = Time.time; yield return 0.4f;
        Check(Time.frameCount > frame && Time.time > time, "Play mode advances");
        yield return 0.8f;
        Check(rig != null && rig.IsSwordSheathed, "Idle keeps sword sheathed");
        Check(rig != null && Vector3.Distance(rig.weapon.position, rig.SheathPivotWorld) < 0.02f, "Sheathed sword rests in scabbard");
        Capture("SheathedIdle", true);
        fighter.MoveInput = Vector2.up; yield return 0.35f;
        Check(rig != null && rig.IsSwordHeld, "Movement draws the sword");
        CheckGrip(rig, "Moving");
        Capture("MoveDrawn", true);
        fighter.MoveInput = Vector2.zero; yield return 1.3f;
        Check(rig != null && rig.IsSwordSheathed, "Standing still sheathes the sword");
        Vector3 start = fighter.transform.position;
        Quaternion facing = fighter.transform.rotation;
        fighter.MoveInput = Vector2.up; yield return 0.4f;
        fighter.MoveInput = Vector2.zero;
        Check(Vector3.Distance(start,fighter.transform.position) > 0.8f,"WASD forward moves controller");
        start = fighter.transform.position; fighter.MoveInput = Vector2.right; yield return 0.3f;
        fighter.MoveInput = Vector2.zero;
        Check(Vector3.Distance(start,fighter.transform.position)>0.6f,"D moves controller");
        fighter.MoveInput = Vector2.down; yield return 0.3f;
        fighter.MoveInput = Vector2.left; yield return 0.3f;
        fighter.MoveInput = Vector2.zero;
        Check(Vector3.Dot(fighter.transform.forward, -Camera.main.transform.right) > 0.9f, "WASD turns the fighter toward movement");
        fighter.ResetPose(); yield return 0.3f;
        Check(fighter.Jump(),"Grounded jump starts"); yield return 0.2f;
        Check(fighter.transform.position.y > 0.4f,"Jump lifts body");
        Check(!fighter.Jump(),"Air jump rejected"); Capture("Jump");
        fighter.CrouchRequested = true; yield return 0.8f;
        Check(fighter.IsGrounded && fighter.IsCrouching, "Holding crouch in air crouches after landing");
        Check(fighter.IsCrouching && fighter.GetComponent<CharacterController>().height < 1.1f,"Crouch lowers capsule");
        Check(rig != null && standingPelvis - (rig.pelvis.position.y - fighter.transform.position.y) > 0.35f, "Crouch bends the skinned rig");
        Capture("Crouch", true); fighter.CrouchRequested = false; yield return 0.2f;
        Check(!fighter.IsCrouching,"Crouch releases");
        fighter.GuardRequested = true; yield return 0.15f;
        Check(fighter.IsGuarding,"Held guard enters pose"); Capture("Guard", true);
        Check(rig != null && rig.weapon != null, "Guard keeps sword visible"); CheckGrip(rig, "Guard");
        Quaternion guardBlade = rig != null ? rig.weapon.rotation : Quaternion.identity;
        fighter.GuardRequested = false;
        Check(!fighter.IsGuarding, "Released guard exits pose");
        Check(fighter.Attack(false),"Light attack starts");
        float lightDeadline = Time.time + 0.48f;
        while (!fighter.IsAttackActive && Time.time < lightDeadline) yield return 0.01f;
        Check(fighter.IsAttackActive,"Light strike window opens");
        Check(rig != null && rig.weapon != null, "Light strike drives sword"); CheckGrip(rig, "Light");
        Check(rig != null && Quaternion.Angle(guardBlade, rig.weapon.rotation) > 15f, "Light strike changes sword orientation");
        Check(!fighter.Attack(true),"Attacks cannot stack"); Capture("Light"); yield return 0.5f;
        float attackTimeout = Time.realtimeSinceStartup + 3f;
        while (fighter.State != PrototypeFighter.ActionState.Ready && Time.realtimeSinceStartup < attackTimeout) yield return 0.1f;
        Check(fighter.Attack(true),"Heavy attack starts");
        float heavyDeadline = Time.time + 0.86f;
        while (!fighter.IsAttackActive && Time.time < heavyDeadline) yield return 0.01f;
        Check(fighter.IsAttackActive,"Heavy strike window opens");
        Check(rig != null && rig.weapon != null, "Heavy strike drives sword"); CheckGrip(rig, "Heavy");
        Check(rig != null && Vector3.Distance(rig.weapon.TransformPoint(Vector3.forward * 0.09f),
            rig.rightArm.end.position) < 0.10f, "Sword grip stays at the right hand");
        Capture("Heavy", true); yield return 0.8f;
        start = fighter.transform.position; Check(fighter.Dodge(),"Dodge starts"); yield return 0.12f;
        Vector3 backward = -fighter.transform.forward;
        Check(fighter.IsInvulnerable,"Dodge invulnerability window opens"); Capture("Dodge"); yield return 0.5f;
        Check(Vector3.Distance(start,fighter.transform.position) > 0.8f && fighter.State == PrototypeFighter.ActionState.Ready,"Dodge moves and recovers");
        Check(Vector3.Dot(fighter.transform.position - start, backward) > 0.8f, "Neutral dodge backsteps");
        fighter.ResetPose(); yield return 0.3f;
        start = fighter.transform.position;
        facing = fighter.transform.rotation;
        fighter.MoveInput = Vector2.right;
        Check(fighter.Dodge(), "Directional dodge starts");
        fighter.MoveInput = Vector2.zero;
        yield return 0.55f;
        Vector3 cameraRight = Camera.main.transform.right;
        Check(Vector3.Dot(fighter.transform.position - start, cameraRight) > 0.8f
            && Quaternion.Angle(facing, fighter.transform.rotation) < 0.1f, "Directional dodge sidesteps without turning");
        fighter.ResetPose(); yield return 1.4f;
        Check(rig != null && rig.IsSwordSheathed, "Reset returns sword to sheath");
        Capture("Idle", true);

        var duel = fighter.GetComponent<DuelEncounter>();
        Check(duel != null && duel.Opponent != null, "Duel opponent spawned in ActionLab");
        if (duel == null || duel.Opponent == null) yield break;
        var opponentRig = duel.Opponent.GetComponentInChildren<RocketboxVisual>();
        Check(opponentRig != null && opponentRig.IsSwordSheathed &&
            Vector3.Distance(opponentRig.weapon.position, opponentRig.Scabbard.position) < 0.02f,
            "Waiting opponent keeps the sword in the scabbard");
        PrepareDuel(duel);
        yield return 0.15f;
        fighter.GuardRequested = true;
        duel.Begin();
        float timeout = Time.time + 4f;
        while (duel.Feedback != "BLOCKED" && Time.time < timeout) yield return 0.02f;
        Check(duel.Feedback == "BLOCKED" && duel.PlayerHealth == DuelEncounter.PlayerMax, "Facing guard prevents ordinary slash damage");
        fighter.GuardRequested = false;

        PrepareDuel(duel);
        yield return 0.15f;
        duel.Begin();
        timeout = Time.time + 4f;
        while (duel.State != DuelEncounter.Phase.Slash && Time.time < timeout) yield return 0.01f;
        bool dodged = fighter.Dodge();
        timeout = Time.time + 0.4f;
        while (duel.Feedback != "DODGED" && duel.Feedback != "MISS" && Time.time < timeout) yield return 0.01f;
        Check(dodged && (duel.Feedback == "DODGED" || duel.Feedback == "MISS") && duel.PlayerHealth == DuelEncounter.PlayerMax, "Timed dodge avoids the slash");

        PrepareDuel(duel);
        yield return 0.15f;
        duel.Begin();
        timeout = Time.time + 3f;
        Vector3 beforeHit = fighter.transform.position;
        while (duel.PlayerHealth == DuelEncounter.PlayerMax && Time.time < timeout) yield return 0.02f;
        yield return 0.25f;
        Check(duel.PlayerHealth == 80, "One slash deals damage only once");
        Check(fighter.transform.position.z < beforeHit.z - 0.12f, "Player recoils away from the hit");
        timeout = Time.time + 12f;
        while (duel.State != DuelEncounter.Phase.Defeat && Time.time < timeout) yield return 0.05f;
        Check(duel.State == DuelEncounter.Phase.Defeat && duel.PlayerHealth == 0, "Unguarded player can lose the duel");

        PrepareDuel(duel);
        yield return 0.15f;
        duel.Begin();
        for (int i = 0; i < 3; i++)
        {
            PutPlayerInSwordRange(duel);
            fighter.GuardRequested = true;
            timeout = Time.time + 4f;
            bool sawSlash = duel.State == DuelEncounter.Phase.Slash;
            while (!(sawSlash && duel.State == DuelEncounter.Phase.Recover) && Time.time < timeout)
            {
                sawSlash |= duel.State == DuelEncounter.Phase.Slash;
                yield return 0.02f;
            }
            Check(duel.State == DuelEncounter.Phase.Recover && duel.Feedback == "BLOCKED", "Counter starts from a successful block");
            float startZ = duel.Opponent.position.z;
            Check(fighter.Attack(true), "Heavy counter starts");
            timeout = Time.time + 2f;
            while (fighter.State != PrototypeFighter.ActionState.Ready && Time.time < timeout) yield return 0.02f;
            Check(duel.OpponentHealth == Mathf.Max(0, DuelEncounter.OpponentMax - 32 * (i + 1)), "One player hit per attack");
            if (i == 2)
                Check(duel.Opponent.position.z > startZ + 0.12f, "Heavy counter pushes the opponent back");
        }
        Check(duel.State == DuelEncounter.Phase.Victory, "Defeating the opponent ends the duel");

        PrepareDuel(duel);
        yield return 0.15f;
        fighter.GuardRequested = true;
        duel.Begin();
        timeout = Time.time + 4f;
        while (duel.State != DuelEncounter.Phase.Recover && Time.time < timeout) yield return 0.02f;
        float beforeLight = duel.Opponent.position.z;
        Check(fighter.Attack(false), "Light counter starts after a block");
        timeout = Time.time + 0.75f;
        while (fighter.State != PrototypeFighter.ActionState.Ready && Time.time < timeout) yield return 0.02f;
        Check(duel.OpponentHealth == 70 && duel.Opponent.position.z > beforeLight + 0.09f,
            "Light contact damages the opponent and gives a short recoil");
        duel.ResetEncounter();
        fighter.ResetPose();
        Check(duel.State == DuelEncounter.Phase.Waiting && duel.PlayerHealth == DuelEncounter.PlayerMax &&
            duel.OpponentHealth == DuelEncounter.OpponentMax, "Reset restores both fighters");

        // A narrow solid wall crosses the sword path but leaves the fighter room to stand.
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Verification wall";
        wall.transform.SetPositionAndRotation(fighter.transform.position + new Vector3(0f, 1.3f, 0.84f), Quaternion.identity);
        wall.transform.localScale = new Vector3(2.6f, 2.6f, 0.12f);
        yield return 0.1f;
        Check(fighter.Attack(false), "Attack into a wall starts");
        timeout = Time.time + 0.65f;
        bool sawObstruction = false, penetrated = false;
        Bounds wallBounds = wall.GetComponent<Collider>().bounds;
        while (fighter.State == PrototypeFighter.ActionState.LightAttack && Time.time < timeout)
        {
            sawObstruction |= rig.BladeBlocker == wall.GetComponent<Collider>();
            for (float z = 0.23f; z <= 1.131f; z += 0.03f)
                penetrated |= wallBounds.Contains(rig.weapon.TransformPoint(0f, 0f, z));
            yield return 0.015f;
        }
        Check(sawObstruction, "The solid wall obstructs the sword swing");
        Check(!penetrated, "The blocked blade stays outside the wall");
        UnityEngine.Object.Destroy(wall);
    }
    private static void PrepareDuel(DuelEncounter duel)
    {
        fighter.ResetPose();
        duel.ResetEncounter();
        PutPlayerInSwordRange(duel);
    }

    private static void PutPlayerInSwordRange(DuelEncounter duel)
    {
        var controller = fighter.GetComponent<CharacterController>();
        controller.enabled = false;
        fighter.transform.SetPositionAndRotation(duel.Opponent.position + Vector3.back * 1.25f, Quaternion.identity);
        controller.enabled = true;
        fighter.skinnedVisual.ResetSwordSweep();
    }

    private static void Capture(string name, bool threeQuarter = false)
    {
        Directory.CreateDirectory("Verification");
        Camera camera = Camera.main;
        var texture = new RenderTexture(1280,720,24);
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        Vector3 previousPosition = camera.transform.position;
        Quaternion previousRotation = camera.transform.rotation;
        if (threeQuarter)
        {
            Vector3 focus = fighter.transform.position + Vector3.up * 0.9f;
            Vector3 offset = fighter.transform.TransformDirection(new Vector3(2.3f, 0.55f, 3.0f));
            camera.transform.SetPositionAndRotation(focus + offset, Quaternion.LookRotation(-offset));
        }
        camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
        var png = new Texture2D(1280,720,TextureFormat.RGB24,false);
        png.ReadPixels(new Rect(0,0,1280,720),0,0); png.Apply();
        File.WriteAllBytes("Verification/" + name + ".png",png.EncodeToPNG());
        camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
        camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
        UnityEngine.Object.DestroyImmediate(png); texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
    }
}















