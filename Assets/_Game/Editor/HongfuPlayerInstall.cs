using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FengChenInn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Explicit entry points only. This file does not mutate assets or scenes on import.
public static class HongfuPlayerInstall
{
    const string Folder = "Assets/_Game/Art/Characters/Hongfu/";
    const string ScenePath = "Assets/_Game/Scenes/InnDuel.unity";
    const string VerifyFlag = "Hongfu.VerifyPending";
    const string ExitFlag = "Hongfu.VerifyExit";
    const string ReportDir = "Verification/Hongfu";
    [Serializable] class Pack { public Motion[] clips; }
    [Serializable] class Motion { public string name; public float length; public Pose[] frames; }
    [Serializable] class Pose { public Vector3 lt, ls, lf, rt, rs, rf; }
    [Serializable] class MaterialPack { public MaterialRow[] materials; }
    [Serializable] class MaterialRow
    {
        public string name, baseColor, normal, baseColorTexture, unityMetallicGlossTexture;
        public float[] color, baseColorFactor;
        public float metallic, roughness;
        public bool doubleSided;
    }

    [MenuItem("FengChenInn/Hongfu/Install Player")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before installing Hongfu.");
        OpenTargetScene();
        var fighter = Object.FindFirstObjectByType<PrototypeFighter>();
        if (fighter == null) throw new InvalidOperationException("InnDuel player missing.");
        AssetDatabase.Refresh();
        var importer = AssetImporter.GetAtPath(Folder + "Hongfu.fbx") as ModelImporter;
        if (importer == null) throw new FileNotFoundException("Hongfu.fbx not imported.");
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = false;
        importer.optimizeGameObjects = false;
        importer.isReadable = true;
        ApplyMaterials(importer, Folder + "materials.json");
        var swordImporter = AssetImporter.GetAtPath(Folder + "HongfuSword.fbx") as ModelImporter;
        if (swordImporter == null) throw new FileNotFoundException("HongfuSword.fbx not imported.");
        swordImporter.importAnimation = false;
        ApplyMaterials(swordImporter, Folder + "SwordImport.json");
        var character = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Hongfu.fbx");
        var swordSource = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "HongfuSword.fbx");
        if (character == null || swordSource == null) throw new FileNotFoundException("Character or sword asset missing.");
        var wrapper = new GameObject("HongfuPlayer");
        bool installed = false;
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(character, wrapper.transform);
            var transforms = model.GetComponentsInChildren<Transform>(true);
            Transform Bone(string suffix) => transforms.SingleOrDefault(t => t.name == "Bip01 " + suffix)
                ?? throw new InvalidOperationException("Missing Hongfu bone: Bip01 " + suffix);
            var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length == 0) throw new InvalidOperationException("Hongfu has no skinned mesh.");
            foreach (var skin in skins)
            {
                if (skin.sharedMesh == null || skin.bones.Length == 0 || skin.bones.Any(b => b == null))
                    throw new InvalidOperationException("Invalid skinned mesh: " + skin.name);
                skin.updateWhenOffscreen = true;
            }
            // The exported model is normalized; only correct its facing and ground contact.
            Vector3 facing = Vector3.ProjectOnPlane(Bone("R Toe0").position - Bone("R Foot").position, Vector3.up);
            if (facing.sqrMagnitude < 0.000001f) throw new InvalidOperationException("Cannot determine foot forward axis.");
            model.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(facing, Vector3.up)) * model.transform.rotation;
            Bounds bounds = RestMeshBounds(skins);
            if (bounds.size.y < 1.3f || bounds.size.y > 2.0f)
                throw new InvalidOperationException("Expected meter-scale Hongfu; height=" + bounds.size.y);
            model.transform.position -= Vector3.up * bounds.min.y;
            var weaponSocket = new GameObject("HongfuSwordSocket").transform;
            weaponSocket.SetParent(wrapper.transform, false);
            var sword = (GameObject)PrefabUtility.InstantiatePrefab(swordSource, weaponSocket);
            sword.name = "HongfuSword";
            NormalizeSword(sword.transform);
            var visual = wrapper.AddComponent<RocketboxVisual>();
            visual.pelvis = Bone("Pelvis"); visual.chest = Bone("Spine2");
            visual.leftArm = Limb(Bone("L UpperArm"), Bone("L Forearm"), Bone("L Hand"));
            visual.rightArm = Limb(Bone("R UpperArm"), Bone("R Forearm"), Bone("R Hand"));
            visual.leftLeg = Limb(Bone("L Thigh"), Bone("L Calf"), Bone("L Foot"));
            visual.rightLeg = Limb(Bone("R Thigh"), Bone("R Calf"), Bone("R Foot"));
            visual.weapon = weaponSocket;
            ConfigureSword(visual);
            visual.sheathMaterial = SheathMaterial("HongfuSheath", new Color(0.12f, 0.035f, 0.026f), 0f);
            visual.sheathTrimMaterial = SheathMaterial("HongfuSheathTrim", new Color(0.55f, 0.32f, 0.10f), 0.7f);
            visual.walkMotions = new[] { BakeForward(visual) };
            AuditSkinWeights(skins, true);
            var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, Folder + "HongfuPlayer.prefab");
            if (prefab == null) throw new IOException("Could not save Hongfu player prefab.");
            var previous = fighter.skinnedVisual;
            wrapper.transform.SetParent(fighter.transform, false);
            wrapper.transform.localPosition = Vector3.zero;
            wrapper.transform.localRotation = Quaternion.identity;
            fighter.skinnedVisual = visual;
            if (previous != null)
            {
                if (previous.gameObject.name == "HongfuPlayer") Object.DestroyImmediate(previous.gameObject);
                else
                {
                    previous.gameObject.name = "BackupPreviousPlayerVisual";
                    previous.gameObject.SetActive(false);
                }
            }
            if (fighter.visualRoot != null) fighter.visualRoot.gameObject.SetActive(false);
            EditorUtility.SetDirty(fighter);
            EditorSceneManager.MarkSceneDirty(fighter.gameObject.scene);
            if (!EditorSceneManager.SaveScene(fighter.gameObject.scene)) throw new IOException("Failed to save InnDuel.");
            AssetDatabase.SaveAssets();
            installed = true;
            Debug.Log("HONGFU_INSTALL_OK skins=" + skins.Length + " height=" + bounds.size.y + " player=" + fighter.transform.position);
        }
        finally { if (!installed && wrapper != null) Object.DestroyImmediate(wrapper); }
    }

    static RocketboxVisual.Limb Limb(Transform a, Transform b, Transform c) => new RocketboxVisual.Limb { upper = a, lower = b, end = c };

    static Bounds RestMeshBounds(IEnumerable<SkinnedMeshRenderer> skins)
    {
        // Imported renderer bounds include padding; use actual skin vertices for foot contact.
        Bounds bounds = default; bool first = true;
        foreach (var skin in skins)
        {
            // The newly instantiated FBX is in its bind pose. Source vertices avoid
            // BakeMesh's extra scale on FBX armatures exported in centimetres.
            foreach (var v in skin.sharedMesh.vertices)
                {
                    Vector3 p = skin.transform.TransformPoint(v);
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
        }
        return bounds;
    }

    static Material SheathMaterial(string name, Color color, float metallic)
    {
        string path = Folder + "Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color); material.SetFloat("_Metallic", metallic); material.SetFloat("_Smoothness", 0.28f);
        EditorUtility.SetDirty(material); return material;
    }

    static void ApplyMaterials(ModelImporter importer, string metadataPath)
    {
        if (!File.Exists(metadataPath)) throw new FileNotFoundException("Material metadata missing: " + metadataPath);
        var pack = JsonUtility.FromJson<MaterialPack>(File.ReadAllText(metadataPath));
        if (pack?.materials == null || pack.materials.Length == 0) throw new InvalidDataException("Material list empty: " + metadataPath);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("URP Lit shader missing.");
        string materialFolder = Folder + "Materials";
        if (!AssetDatabase.IsValidFolder(materialFolder)) AssetDatabase.CreateFolder(Folder.TrimEnd('/'), "Materials");
        string textureRoot = Path.GetDirectoryName(metadataPath).Replace('\\', '/') + "/";
        foreach (var row in pack.materials)
        {
            string path = materialFolder + "/" + row.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            string basePath = !string.IsNullOrEmpty(row.baseColor) ? row.baseColor : row.baseColorTexture;
            var colorMap = ImportTexture(textureRoot, basePath, false, true);
            var normalMap = ImportTexture(textureRoot, row.normal, true, false);
            var metallicGloss = ImportTexture(textureRoot, row.unityMetallicGlossTexture, false, false);
            float[] color = row.color ?? row.baseColorFactor;
            Color tint = color != null && color.Length >= 3 ? new Color(color[0], color[1], color[2], color.Length > 3 ? color[3] : 1f).gamma : Color.white;
            // Blender's default .8 factor is unused when an image supplies Base Color.
            material.SetColor("_BaseColor", colorMap != null ? Color.white : tint);
            material.SetTexture("_BaseMap", colorMap);
            material.SetFloat("_WorkflowMode", 1f);
            material.SetFloat("_Metallic", row.metallic);
            material.SetFloat("_Smoothness", metallicGloss != null ? 1f : 1f - Mathf.Clamp01(row.roughness));
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetTexture("_MetallicGlossMap", metallicGloss);
            if (metallicGloss != null) material.EnableKeyword("_METALLICSPECGLOSSMAP");
            else material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.SetTexture("_BumpMap", normalMap);
            material.SetFloat("_BumpScale", 1f);
            if (normalMap != null) material.EnableKeyword("_NORMALMAP");
            else material.DisableKeyword("_NORMALMAP");
            material.SetFloat("_Cull", row.doubleSided ? 0f : 2f);
            material.doubleSidedGI = row.doubleSided;
            EditorUtility.SetDirty(material);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), row.name), material);
        }
        importer.SaveAndReimport();
        AssetDatabase.SaveAssets();
    }

    static Texture2D ImportTexture(string root, string relative, bool normal, bool srgb)
    {
        if (string.IsNullOrEmpty(relative)) return null;
        string path = root + relative.Replace('\\', '/');
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new FileNotFoundException("Required texture missing: " + path);
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType != type || importer.sRGBTexture != srgb || importer.maxTextureSize != 2048)
        {
            importer.textureType = type; importer.sRGBTexture = srgb; importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void OpenTargetScene()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == ScenePath) return;
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the currently open scene before switching to InnDuel.");
        EditorSceneManager.OpenScene(ScenePath);
    }

    static void NormalizeSword(Transform sword)
    {
        var all = sword.GetComponentsInChildren<Transform>(true);
        Transform grip = all.SingleOrDefault(t => t.name == "GripCenter");
        Transform tip = all.SingleOrDefault(t => t.name == "BladeTip");
        Transform broad = all.SingleOrDefault(t => t.name == "BladeBroadAxis");
        if (grip == null || tip == null || broad == null)
            throw new InvalidOperationException("Sword coordinate markers missing.");
        Vector3 forward = (tip.position - grip.position).normalized;
        Vector3 broadRight = Vector3.ProjectOnPlane(broad.position - grip.position, forward).normalized;
        if (broadRight.sqrMagnitude < 0.5f) throw new InvalidOperationException("Invalid sword broad axis.");
        sword.rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, Vector3.Cross(forward, broadRight))) * sword.rotation;
        sword.position += new Vector3(0f, 0f, 0.09f) - grip.position;
    }

    static void ConfigureSword(RocketboxVisual visual)
    {
        // Distances are measured from the grip center, matching RocketboxVisual's sweep.
        var data = new SerializedObject(visual);
        var custom = data.FindProperty("useCustomBladeGeometry");
        if (custom != null) custom.boolValue = true;
        SetFloat(data, "customBladeStart", 0.17f);
        SetFloat(data, "customBladeEnd", 0.78f);
        SetFloat(data, "customBladeRadius", 0.035f);
        SetFloat(data, "sheathBladeLength", 0.64f);
        SetFloat(data, "sheathWidth", 0.085f);
        data.ApplyModifiedPropertiesWithoutUndo();
    }
    static void SetFloat(SerializedObject data, string name, float value)
    {
        var property = data.FindProperty(name);
        if (property != null) property.floatValue = value;
    }

    static AnimationClip BakeForward(RocketboxVisual target)
    {
        var pack = JsonUtility.FromJson<Pack>(File.ReadAllText("Assets/_Game/Art/Animations/KevinIglesias/WalkSamples.json"));
        var motion = pack.clips.Single(c => c.name == "Forward");
        var bones = target.GetWalkBones();
        Transform leftToe = bones[2].Find("Bip01 L Toe0"), rightToe = bones[5].Find("Bip01 R Toe0");
        if (leftToe == null || rightToe == null) throw new InvalidOperationException("Walk baking requires toe bones.");
        var bind = bones.Select(b => b.localRotation).ToArray();
        var previous = new Quaternion[6];
        var curves = new AnimationCurve[6, 4];
        for (int j = 0; j < 6; j++) for (int k = 0; k < 4; k++) curves[j, k] = new AnimationCurve();
        try
        {
            for (int i = 0; i < motion.frames.Length; i++)
            {
                for (int j = 0; j < 6; j++) bones[j].localRotation = bind[j];
                var p = motion.frames[i];
                Aim(bones[0], bones[1], p.lt); Aim(bones[1], bones[2], p.ls); Aim(bones[2], leftToe, p.lf);
                Aim(bones[3], bones[4], p.rt); Aim(bones[4], bones[5], p.rs); Aim(bones[5], rightToe, p.rf);
                float time = motion.length * i / (motion.frames.Length - 1);
                for (int j = 0; j < 6; j++)
                {
                    Quaternion q = bones[j].localRotation;
                    if (i > 0 && Quaternion.Dot(q, previous[j]) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    previous[j] = q;
                    curves[j, 0].AddKey(time, q.x); curves[j, 1].AddKey(time, q.y);
                    curves[j, 2].AddKey(time, q.z); curves[j, 3].AddKey(time, q.w);
                }
            }
        }
        finally { for (int j = 0; j < 6; j++) bones[j].localRotation = bind[j]; }
        string path = Folder + "WalkForward.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves(); clip.name = "WalkForward"; clip.frameRate = 48;
        string[] names = { "x", "y", "z", "w" };
        for (int j = 0; j < 6; j++) for (int k = 0; k < 4; k++)
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[j], target.transform),
                typeof(Transform), "m_LocalRotation." + names[k]), curves[j, k]);
        clip.EnsureQuaternionContinuity();
        var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }
    static void Aim(Transform parent, Transform child, Vector3 direction) =>
        parent.rotation = Quaternion.FromToRotation(child.position - parent.position, direction) * parent.rotation;

    static string AuditSkinWeights(IEnumerable<SkinnedMeshRenderer> skins, bool throwOnFailure)
    {
        int vertices = 0, invalid = 0, maxInfluences = 0;
        foreach (var skin in skins)
        {
            var mesh = skin.sharedMesh;
            var counts = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            int cursor = 0;
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                int count = i < counts.Length ? counts[i] : 0;
                float sum = 0f; bool bad = count == 0;
                maxInfluences = Mathf.Max(maxInfluences, count);
                for (int j = 0; j < count; j++)
                {
                    var w = weights[cursor++];
                    bad |= !Finite(w.weight) || w.weight < 0 || w.boneIndex < 0 || w.boneIndex >= skin.bones.Length;
                    sum += w.weight;
                }
                if (bad || Mathf.Abs(sum - 1f) > 0.005f) invalid++;
                vertices++;
            }
            counts.Dispose(); weights.Dispose();
        }
        string summary = "vertices=" + vertices + " invalid=" + invalid + " maxInfluences=" + maxInfluences;
        if (throwOnFailure && (invalid != 0 || vertices == 0)) throw new InvalidOperationException("Invalid skin weights: " + summary);
        return summary;
    }

    static readonly List<string> results = new List<string>();
    static IEnumerator<float> routine;
    static PrototypeFighter player;
    static float nextTime;
    static double deadline;
    static int failures;
    static Vector3[] idleMesh;
    static bool savedInput;
    static float savedMaxDelta;
    static int savedFrameRate;
    static bool savedBackground;

    [MenuItem("FengChenInn/Hongfu/Verify In Play Mode")]
    public static void Verify() => BeginVerification(false);
    public static void VerifyAndExit() => BeginVerification(true);
    static void BeginVerification(bool exit)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        OpenTargetScene();
        SessionState.SetBool(VerifyFlag, true); SessionState.SetBool(ExitFlag, exit);
        Bootstrap(); EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]
    static void Bootstrap()
    {
        if (!SessionState.GetBool(VerifyFlag, false)) return;
        EditorApplication.playModeStateChanged -= PlayChanged;
        EditorApplication.playModeStateChanged += PlayChanged;
    }
    static void PlayChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        EditorApplication.playModeStateChanged -= PlayChanged;
        try
        {
            savedBackground = Application.runInBackground; savedFrameRate = Application.targetFrameRate; savedMaxDelta = Time.maximumDeltaTime;
            player = Object.FindFirstObjectByType<PrototypeFighter>();
            if (player == null || player.skinnedVisual == null) throw new InvalidOperationException("Player missing.");
            savedInput = player.GetComponent<PrototypeInput>().enabled;
            player.GetComponent<PrototypeInput>().enabled = false;
            Application.runInBackground = true; Application.targetFrameRate = 60; Time.maximumDeltaTime = 0.04f;
            results.Clear(); failures = 0; Directory.CreateDirectory(ReportDir);
            Application.logMessageReceived += LogError;
            routine = Exercise(); nextTime = Time.time; deadline = EditorApplication.timeSinceStartup + 90;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }
        catch (Exception e) { failures++; results.Add("FAIL " + e); Finish(); }
    }
    static void LogError(string message, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
        { results.Add("FAIL runtime " + message); failures++; }
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Finish(); return; }
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Hongfu runtime verification timed out.");
            if (Time.time < nextTime) return;
            if (routine.MoveNext()) { nextTime = Time.time + routine.Current; return; }
            Finish();
        }
        catch (Exception e) { results.Add("FAIL " + e); failures++; Finish(); }
    }
    static void Finish()
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= LogError;
        SessionState.SetBool(VerifyFlag, false);
        Directory.CreateDirectory(ReportDir);
        File.WriteAllLines(ReportDir + "/results.txt", results);
        if (player != null)
        {
            player.MoveInput = Vector2.zero; player.CrouchRequested = player.GuardRequested = false;
            player.GetComponent<PrototypeInput>().enabled = savedInput;
        }
        Application.runInBackground = savedBackground; Application.targetFrameRate = savedFrameRate; Time.maximumDeltaTime = savedMaxDelta > 0 ? savedMaxDelta : 0.333f;
        Debug.Log("HONGFU_VERIFY_DONE failures=" + failures);
        bool exit = SessionState.GetBool(ExitFlag, false); SessionState.SetBool(ExitFlag, false);
        if (exit) EditorApplication.Exit(failures == 0 ? 0 : 2);
        else EditorApplication.ExitPlaymode();
    }
    static void Check(bool okay, string label)
    { results.Add((okay ? "PASS " : "FAIL ") + label); if (!okay) failures++; }

    static IEnumerator<float> Exercise()
    {
        var rig = player.skinnedVisual;
        Check(rig.name == "HongfuPlayer", "Formal Hongfu visual assigned");
        Check(player.GetComponent<DuelEncounter>().opponentVisualPrefab != null, "Independent opponent preserved");
        string weights = AuditSkinWeights(rig.GetComponentsInChildren<SkinnedMeshRenderer>(), true);
        Check(true, "Normalized skin weights " + weights);
        int frame = Time.frameCount; float time = Time.time;
        yield return 0.8f;
        Check(Time.frameCount > frame + 2 && Time.time > time, "Play mode advances");
        Check(player.IsGrounded, "Spawn has ground contact");
        var doors = Object.FindFirstObjectByType<InnEntranceDoors>();
        if (doors != null)
            Check(Mathf.Abs(Vector3.ProjectOnPlane(player.transform.position - doors.transform.position, Vector3.up).magnitude - 10f) < 0.15f,
                "Spawn remains ten meters outside entrance");
        Check(rig.walkMotions.Length > 0 && AssetDatabase.GetAssetPath(rig.walkMotions[0]) == Folder + "WalkForward.anim", "Independent forward clip assigned");
        var bones = rig.GetWalkBones(); var bind = bones.Select(b => b.localRotation).ToArray();
        rig.walkMotions[0].SampleAnimation(rig.gameObject, 0f);
        Quaternion first = bones[0].localRotation;
        rig.walkMotions[0].SampleAnimation(rig.gameObject, 0.4f);
        Check(Quaternion.Angle(first, bones[0].localRotation) > 5f, "Forward clip animates new leg hierarchy");
        for (int i = 0; i < bones.Length; i++) bones[i].localRotation = bind[i];
        yield return 0.5f;
        idleMesh = BakeAndCheck("idle"); Capture("01_idle_front", false); Capture("02_idle_back", true);
        Check(rig.IsSwordSheathed, "Idle sword sheathed");
        Vector3 start = player.transform.position;
        player.MoveInput = Vector2.up;
        yield return 0.45f;
        Check(Vector3.Distance(start, player.transform.position) > 0.5f, "W moves player");
        Check(rig.IsSwordHeld, "Movement draws formal sword");
        BakeAndCheck("move"); CheckGrip(rig, "move"); Capture("03_move_front", false); Capture("04_move_back", true);
        player.MoveInput = Vector2.zero; player.ResetPose();
        yield return 0.3f;
        float groundY = player.transform.position.y;
        Check(player.Jump(), "Jump starts");
        yield return 0.22f;
        Check(player.transform.position.y > groundY + 0.3f, "Jump lifts model");
        BakeAndCheck("jump"); Capture("05_jump", false);
        player.CrouchRequested = true;
        yield return 1f;
        Check(player.IsCrouching, "Holding crouch in air crouches after landing");
        var crouchMesh = BakeAndCheck("crouch");
        if (idleMesh.Length == crouchMesh.Length)
            Check(idleMesh.Where((v, i) => Vector3.Distance(v, crouchMesh[i]) > 0.03f).Count() > idleMesh.Length / 10, "Skinned geometry deforms under crouch");
        Capture("06_crouch", false);
        player.CrouchRequested = false;
        yield return 0.3f;
        player.GuardRequested = true;
        yield return 0.3f;
        Check(player.IsGuarding, "Guard starts"); BakeAndCheck("guard"); CheckGrip(rig, "guard"); Capture("07_guard", false);
        player.GuardRequested = false;
        Check(player.Attack(false), "Light attack starts");
        yield return 0.15f;
        BakeAndCheck("light_windup"); Capture("08_light_windup", false);
        float strikeDeadline = Time.time + 0.35f;
        while (!player.IsAttackActive && Time.time < strikeDeadline) yield return 0f;
        Check(player.IsAttackActive, "Light strike window active"); BakeAndCheck("light_strike"); CheckGrip(rig, "light"); Capture("09_light_strike", false);
        yield return 0.2f;
        Capture("10_light_recover", false);
        yield return 0.3f;
        Check(player.Attack(true), "Heavy attack starts");
        yield return 0.3f;
        BakeAndCheck("heavy_windup"); Capture("11_heavy_windup", false);
        strikeDeadline = Time.time + 0.5f;
        while (!player.IsAttackActive && Time.time < strikeDeadline) yield return 0f;
        Check(player.IsAttackActive, "Heavy strike window active"); BakeAndCheck("heavy_strike"); CheckGrip(rig, "heavy"); Capture("12_heavy_strike", false);
        yield return 0.27f;
        Capture("13_heavy_recover", false);
        yield return 0.5f;
        start = player.transform.position;
        Check(player.Dodge(), "Dodge starts");
        yield return 0.12f;
        Check(player.IsInvulnerable, "Dodge window active"); BakeAndCheck("dodge"); Capture("14_dodge", false);
        yield return 0.5f;
        Check(Vector3.Distance(start, player.transform.position) > 0.8f, "Dodge moves player");
        player.ResetPose();
        yield return 1.4f;
        Check(rig.IsSwordSheathed, "Reset sheathes sword"); BakeAndCheck("reset"); Capture("15_reset", true);
    }

    static Vector3[] BakeAndCheck(string label)
    {
        var vertices = new List<Vector3>();
        var rig = player.skinnedVisual;
        bool finite = true;
        foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var baked = new Mesh();
            try
            {
                skin.BakeMesh(baked);
                foreach (Vector3 p in baked.vertices)
                {
                    Vector3 local = rig.transform.InverseTransformPoint(skin.transform.TransformPoint(p));
                    finite &= Finite(local.x) && Finite(local.y) && Finite(local.z) && local.sqrMagnitude < 25f;
                    vertices.Add(local);
                }
            }
            finally { Object.DestroyImmediate(baked); }
        }
        Check(finite && vertices.Count > 0, label + " baked mesh finite and bounded");
        return vertices.ToArray();
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static void CheckGrip(RocketboxVisual rig, string label)
    {
        float distance = Vector3.Distance(rig.weapon.TransformPoint(new Vector3(0, 0, 0.09f)), rig.rightArm.end.position);
        Check(distance < 0.14f, label + " sword grip near hand distance=" + distance);
    }
    static void Capture(string name, bool back)
    {
        Camera source = Camera.main;
        var camera = new GameObject("Hongfu Verification Camera").AddComponent<Camera>();
        if (source != null) camera.CopyFrom(source);
        camera.enabled = false;
        camera.fieldOfView = 36;
        camera.nearClipPlane = 0.03f;
        Vector3 focus = player.transform.position + Vector3.up * 0.9f;
        Vector3 offset = player.transform.TransformDirection(back ? new Vector3(-1.9f, 0.35f, -3.0f) : new Vector3(1.9f, 0.35f, 3.0f));
        camera.transform.SetPositionAndRotation(focus + offset, Quaternion.LookRotation(-offset));
        var target = new RenderTexture(1280, 960, 24);
        var old = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image = new Texture2D(1280, 960, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0); image.Apply();
            File.WriteAllBytes(ReportDir + "/" + name + ".png", image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = old; camera.targetTexture = null;
            if (image != null) Object.DestroyImmediate(image);
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(camera.gameObject);
        }
    }
}
