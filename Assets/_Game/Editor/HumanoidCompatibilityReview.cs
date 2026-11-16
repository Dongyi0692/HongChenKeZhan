using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit, isolated import experiment. Never edits a scene, existing prefab, or original importer.
public static class HumanoidCompatibilityReview
{
    const string Folder = "Assets/_Game/Art/Aholo20261116/Compatibility";
    const string Source = "C:/Users/東壹/Documents/Codex/2026-10-29/read-https-raw-githubusercontent-com-manycore/work/AholoAnimations_20261116";
    const string ReportPath = "Verification/HumanoidCompatibility_20261116.json";
    static string ProjectPath => Path.GetDirectoryName(Application.dataPath);
    static readonly string[] Roles = { "Hongfu", "Elite", "Grunt" };
    static readonly string[] Bases = { "Hongfu_Base_Walk.fbx", "Elite_Base_Jog.fbx", "Grunt_Base_SwordA.fbx" };
    static readonly HumanBodyBones[] MeasuredBones = {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
        HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes
    };
    static readonly (HumanBodyBones a, HumanBodyBones b)[] Segments = {
        (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm), (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
        (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm), (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
        (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg), (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
        (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg), (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot)
    };

    [Serializable] public sealed class Review {
        public string sourceMotion = "Grunt Sword A; genuine Aholo export, root X/Z extracted before this trial";
        public bool wroteScene, alteredExistingPrefabs, allAvatarsValid;
        public string note = "Avatar validity is a structural check only. Compare native Generic source to its own Humanoid conversion first; cross-character shape/limb differences remain subject to visual and weapon-grip review.";
        public string sourceHumanClip, sourceGenericClip;
        public bool sourceClipIsHumanMotion;
        public float clipLength;
        public List<ModelReview> models = new();
        public ModelReview nativeReference;
        public List<PoseComparison> comparisons = new();
        public List<string> warnings = new();
        public string error;
    }
    [Serializable] public sealed class ModelReview {
        public string role, path, error;
        public bool avatarValid, avatarHuman, poseChanges;
        public float humanScale, restMeshHeight, maxPoseChangeNormalized;
        public int mappedBoneCount;
        public List<string> missingRequired = new();
        public List<BoneMapping> mapping = new();
        public List<PoseSample> samples = new();
    }
    [Serializable] public sealed class BoneMapping { public string human, bone; }
    [Serializable] public sealed class PoseSample {
        public float seconds;
        public List<BoneSample> bones = new();
        public float[] muscles;
        public float maximumAbsoluteMuscle;
        public Vector3 humanBodyPosition;
        public Quaternion humanBodyRotation;
    }
    [Serializable] public sealed class BoneSample { public string name; public Vector3 positionRelativeHips, normalizedPosition; }
    [Serializable] public sealed class PoseComparison {
        public string reference, target;
        public float seconds, maxNormalizedJointDistance, meanNormalizedJointDistance, maxLimbDirectionDifference;
        public string largestJointDifference, largestSegmentDifference;
        public float rightHandNormalizedDistance, leftHandNormalizedDistance;
    }

    [MenuItem("FengChenInn/Aholo 20261116/Review Humanoid Compatibility (Isolated)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit play mode before isolated import review.");
        var report = new Review();
        Application.LogCallback collect = (message, stack, type) => {
            if ((type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                && !report.warnings.Contains(message)) report.warnings.Add(message);
        };
        Application.logMessageReceived += collect;
        try {
            Directory.CreateDirectory(Path.Combine(ProjectPath, Folder));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (int i = 0; i < Roles.Length; i++) {
                string path = CopyForTrial(Bases[i], Roles[i] + "_Humanoid.fbx");
                Configure(path, true, null);
                report.models.Add(InspectAvatar(Roles[i], path));
            }
            report.allAvatarsValid = report.models.All(m => m.avatarValid && m.avatarHuman && m.missingRequired.Count == 0);
            if (!report.allAvatarsValid) throw new InvalidOperationException("At least one automatic Human avatar is invalid or missing required bones; no production conversion was attempted.");

            Avatar sourceAvatar = LoadAvatar(Folder + "/Grunt_Humanoid.fbx");
            string humanClipPath = CopyForTrial("Grunt_SwordA_InPlace.fbx", "Grunt_SwordA_Humanoid.fbx");
            Configure(humanClipPath, true, sourceAvatar);
            string nativeModelPath = CopyForTrial("Grunt_Base_SwordA.fbx", "Grunt_GenericReference.fbx");
            Configure(nativeModelPath, false, null);
            string nativeClipPath = CopyForTrial("Grunt_SwordA_InPlace.fbx", "Grunt_SwordA_Generic.fbx");
            Configure(nativeClipPath, false, null);
            AnimationClip humanClip = LoadClip(humanClipPath), nativeClip = LoadClip(nativeClipPath);
            report.sourceHumanClip = humanClipPath + " :: " + humanClip.name;
            report.sourceGenericClip = nativeClipPath + " :: " + nativeClip.name;
            report.sourceClipIsHumanMotion = humanClip.humanMotion;
            report.clipLength = humanClip.length;
            if (!humanClip.humanMotion || nativeClip.humanMotion) throw new InvalidOperationException("Human/Generic trial clip types do not match expectations.");
            if (Mathf.Abs(humanClip.length - nativeClip.length) > .02f) throw new InvalidOperationException("Trial clip lengths differ.");
            // Two real motion instants: preparation at 30%, active upward sweep at 75%.
            float[] times = { humanClip.length * .30f, humanClip.length * .75f };
            foreach (var model in report.models) SampleHumanoid(model, humanClip, times);
            report.nativeReference = new ModelReview { role = "Grunt native Generic", path = nativeModelPath };
            SampleNative(report.nativeReference, nativeClip, times, report.models.Single(m => m.role == "Grunt").mapping);
            var sourceHuman = report.models.Single(m => m.role == "Grunt");
            for (int i = 0; i < times.Length; i++) {
                report.comparisons.Add(Compare(report.nativeReference, sourceHuman, i));
                foreach (var target in report.models.Where(m => m.role != "Grunt")) report.comparisons.Add(Compare(sourceHuman, target, i));
            }
            if (report.models.Any(m => !m.poseChanges)) report.warnings.Add("At least one target did not show measurable movement between the two real clip instants.");
        }
        catch (Exception ex) { report.error = ex.ToString(); Debug.LogError("HUMANOID_COMPATIBILITY_REVIEW: " + ex.Message); }
        finally {
            Application.logMessageReceived -= collect;
            string output = Path.Combine(ProjectPath, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("HUMANOID_COMPATIBILITY_REVIEW_REPORT " + output);
        }
    }

    static string CopyForTrial(string sourceName, string targetName)
    {
        string source = Path.Combine(Source, sourceName), target = Folder + "/" + targetName;
        if (!File.Exists(source)) throw new FileNotFoundException("Verified source missing", source);
        // Trial names are owned solely by this experiment; originals are never changed.
        File.Copy(source, Path.Combine(ProjectPath, target), true);
        AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        return target;
    }
    static void Configure(string path, bool human, Avatar sourceAvatar)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("No FBX importer: " + path);
        importer.importAnimation = true;
        importer.animationType = human ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
        importer.avatarSetup = sourceAvatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
        importer.sourceAvatar = sourceAvatar;
        importer.autoGenerateAvatarMappingIfUnspecified = true;
        importer.optimizeGameObjects = false;
        importer.preserveHierarchy = true;
        importer.isReadable = true;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport();
        var settings = importer.defaultClipAnimations;
        if (settings == null || settings.Length != 1) throw new InvalidOperationException("Expected one real take: " + path);
        var clip = settings[0];
        clip.name = Path.GetFileNameWithoutExtension(path);
        clip.loopTime = false;
        clip.loopPose = false;
        clip.keepOriginalOrientation = true;
        clip.keepOriginalPositionXZ = true;
        clip.keepOriginalPositionY = true;
        clip.lockRootRotation = true;
        clip.lockRootPositionXZ = true;
        clip.lockRootHeightY = true;
        importer.clipAnimations = new[] { clip };
        importer.SaveAndReimport();
    }
    static Avatar LoadAvatar(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
    static AnimationClip LoadClip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));

    static ModelReview InspectAvatar(string role, string path)
    {
        var result = new ModelReview { role = role, path = path };
        var avatar = LoadAvatar(path);
        result.avatarValid = avatar != null && avatar.isValid;
        result.avatarHuman = avatar != null && avatar.isHuman;
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        foreach (var human in importer.humanDescription.human ?? Array.Empty<HumanBone>())
            result.mapping.Add(new BoneMapping { human = human.humanName, bone = human.boneName });
        if (result.avatarValid && result.avatarHuman) {
            Scene scene = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            try {
                instance = InstanceInPreview(path, scene);
                var animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null) throw new InvalidOperationException("Valid avatar has no imported Animator");
                animator.avatar = avatar;
                animator.Rebind();
                result.mapping.Clear();
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++) {
                    var bone = animator.GetBoneTransform((HumanBodyBones)i);
                    if (bone != null) result.mapping.Add(new BoneMapping { human = HumanTrait.BoneName[i], bone = bone.name });
                }
            }
            finally {
                if (instance != null) Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        result.mappedBoneCount = result.mapping.Count;
        for (int i = 0; i < HumanTrait.BoneCount; i++)
            if (HumanTrait.RequiredBone(i) && !result.mapping.Any(m => m.human == HumanTrait.BoneName[i])) result.missingRequired.Add(HumanTrait.BoneName[i]);
        return result;
    }

    static GameObject InstanceInPreview(string path, Scene scene)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new InvalidOperationException("Imported model missing: " + path);
        var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
        if (instance == null) throw new InvalidOperationException("Unable to instantiate isolated model: " + path);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.hideFlags = HideFlags.HideAndDontSave;
        return instance;
    }

    static float RestHeight(GameObject instance)
    {
        var points = new List<Vector3>();
        foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
            Bounds b = renderer.sharedMesh.bounds;
            for (int i = 0; i < 8; i++) points.Add(renderer.transform.TransformPoint(b.center + Vector3.Scale(b.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        }
        return points.Count == 0 ? 1f : Mathf.Max(.001f, points.Max(p => p.y) - points.Min(p => p.y));
    }

    static void SampleHumanoid(ModelReview model, AnimationClip clip, float[] times)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        PlayableGraph graph = default;
        GameObject instance = null;
        try {
            instance = InstanceInPreview(model.path, scene);
            var animator = instance.GetComponentInChildren<Animator>(true);
            if (animator == null) throw new InvalidOperationException("Imported Humanoid animator missing");
            animator.avatar = LoadAvatar(model.path);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            model.humanScale = animator.humanScale;
            model.restMeshHeight = RestHeight(instance);
            graph = PlayableGraph.Create("IsolatedHumanoidReview");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            var output = AnimationPlayableOutput.Create(graph, "Motion", animator);
            output.SetSourcePlayable(playable);
            graph.Play();
            foreach (float time in times) {
                playable.SetTime(time);
                playable.SetSpeed(0);
                graph.Evaluate(0f);
                var sample = Measure(time, model.restMeshHeight, bone => animator.GetBoneTransform(bone));
                using (var poseHandler = new HumanPoseHandler(animator.avatar, animator.transform)) {
                    var pose = new HumanPose();
                    poseHandler.GetHumanPose(ref pose);
                    sample.muscles = pose.muscles;
                    sample.maximumAbsoluteMuscle = pose.muscles == null ? 0 : pose.muscles.Max(v => Mathf.Abs(v));
                    sample.humanBodyPosition = pose.bodyPosition;
                    sample.humanBodyRotation = pose.bodyRotation;
                }
                model.samples.Add(sample);
            }
            RecordMovement(model);
        }
        catch (Exception ex) { model.error = ex.ToString(); }
        finally {
            if (graph.IsValid()) graph.Destroy();
            if (instance != null) Object.DestroyImmediate(instance);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static void SampleNative(ModelReview model, AnimationClip clip, float[] times, List<BoneMapping> mapping)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        GameObject instance = null;
        try {
            instance = InstanceInPreview(model.path, scene);
            model.restMeshHeight = RestHeight(instance);
            var transforms = instance.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, t => t);
            foreach (float time in times) {
                clip.SampleAnimation(instance, time);
                model.samples.Add(Measure(time, model.restMeshHeight, bone => {
                    string human = HumanTrait.BoneName[(int)bone];
                    string boneName = mapping.FirstOrDefault(m => m.human == human)?.bone;
                    return boneName != null && transforms.TryGetValue(boneName, out var transform) ? transform : null;
                }));
            }
            RecordMovement(model);
        }
        catch (Exception ex) { model.error = ex.ToString(); }
        finally {
            if (instance != null) Object.DestroyImmediate(instance);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static PoseSample Measure(float time, float height, Func<HumanBodyBones, Transform> find)
    {
        var sample = new PoseSample { seconds = time };
        var hips = find(HumanBodyBones.Hips);
        if (hips == null) throw new InvalidOperationException("No mapped hips at sample time");
        foreach (var bone in MeasuredBones) {
            var t = find(bone);
            if (t == null) continue;
            var position = t.position - hips.position;
            sample.bones.Add(new BoneSample { name = bone.ToString(), positionRelativeHips = position, normalizedPosition = position / height });
        }
        return sample;
    }
    static void RecordMovement(ModelReview model)
    {
        if (model.samples.Count < 2) return;
        var start = model.samples[0].bones.ToDictionary(b => b.name, b => b.normalizedPosition);
        model.maxPoseChangeNormalized = model.samples[1].bones.Where(b => start.ContainsKey(b.name)).Max(b => Vector3.Distance(start[b.name], b.normalizedPosition));
        model.poseChanges = model.maxPoseChangeNormalized > .01f;
    }
    static PoseComparison Compare(ModelReview source, ModelReview target, int index)
    {
        if (source.samples.Count <= index || target.samples.Count <= index) throw new InvalidOperationException("Actual pose sampling failed: " + source.role + " / " + target.role);
        var result = new PoseComparison { reference = source.role, target = target.role, seconds = source.samples[index].seconds };
        var a = source.samples[index].bones.ToDictionary(b => b.name, b => b.normalizedPosition);
        var b = target.samples[index].bones.ToDictionary(bone => bone.name, bone => bone.normalizedPosition);
        int count = 0;
        foreach (var pair in a) {
            if (!b.ContainsKey(pair.Key)) continue;
            float difference = Vector3.Distance(pair.Value, b[pair.Key]);
            result.meanNormalizedJointDistance += difference; count++;
            if (difference > result.maxNormalizedJointDistance) { result.maxNormalizedJointDistance = difference; result.largestJointDifference = pair.Key; }
            if (pair.Key == "RightHand") result.rightHandNormalizedDistance = difference;
            if (pair.Key == "LeftHand") result.leftHandNormalizedDistance = difference;
        }
        result.meanNormalizedJointDistance /= Mathf.Max(1, count);
        foreach (var segment in Segments) {
            string from = segment.a.ToString(), to = segment.b.ToString();
            if (!a.ContainsKey(from) || !a.ContainsKey(to) || !b.ContainsKey(from) || !b.ContainsKey(to)) continue;
            float angle = Vector3.Angle(a[to] - a[from], b[to] - b[from]);
            if (angle > result.maxLimbDirectionDifference) { result.maxLimbDirectionDifference = angle; result.largestSegmentDifference = from + " → " + to; }
        }
        return result;
    }
}
