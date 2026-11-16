using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FengChenInn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit importer only: no InitializeOnLoad, scene opening, scene saving or player replacement.
public static class AholoReviewImport20261116
{
    public const string Folder = "Assets/_Game/Art/Aholo20261116";
    public const string PrefabPath = Folder + "/Hongfu_Aholo_WalkReview.prefab";
    public const string MotionSetPath = Folder + "/Hongfu_WalkReview_Motions.asset";
    const string ManifestPath = Folder + "/Hongfu_SampleImport.json";
    const string LightPath = Folder + "/Hongfu_Light_SwordAttack.fbx";
    const string LightInPlacePath = Folder + "/Hongfu_Light_InPlace.fbx";

    [Serializable] sealed class ImportManifest
    {
        public string sourceFile, sourceSha256, baseModel, walkAnimation, clipName;
        public float clipDuration, walkReferenceSpeed, sourceHeightMeters;
        public int boneCount, meshCount, triangleCount;
        public MaterialRow[] materials;
        public TextureRow[] textures;
        public string[] missingMotions;
    }
    [Serializable] sealed class TextureRow { public string file; public int width, height; }
    [Serializable] sealed class MaterialRow
    {
        public string name, sourceName, baseColorTexture, normalTexture, metallicGlossTexture, alphaMode;
        public float[] color;
        public float normalScale, metallic, roughness, alphaCutoff;
        public bool doubleSided;
    }
    [Serializable] sealed class ReviewReport
    {
        public string prefab, motionSet, source, sourceSha256, walkClip, rightHandPath;
        public string lightClip;
        public float lightSeconds;
        public Vector3 lightRootDisplacement;
        public float clipSeconds, expectedSeconds, heightMeters, sourceHeightMeters, forwardYawCorrectionDegrees;
        public int meshes, bones, triangles, materials;
        public float walkRootHorizontalDriftMeters;
        public float sampledPoseMaxRotationDegrees;
        public string[] missingMotions, warnings;
        public bool wroteScene;
    }

    [MenuItem("FengChenInn/Aholo 20261116/Prepare Hongfu Walk Review")]
    public static void PrepareHongfu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before preparing the review assets.");
        if (!File.Exists(ManifestPath)) throw new FileNotFoundException("Staged sample manifest missing.", ManifestPath);
        var manifest = JsonUtility.FromJson<ImportManifest>(File.ReadAllText(ManifestPath));
        if (manifest?.materials == null || manifest.materials.Length != 13)
            throw new InvalidDataException("Expected the verified 13-material Hongfu source mapping.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("AHOLO_REVIEW_STEP Import source materials");
        EnsureFolder(Folder + "/Materials");
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("URP Lit shader is missing.");

        string modelPath = Folder + "/" + manifest.baseModel;
        var modelImporter = RequiredModelImporter(modelPath);
        // Keep the source take imported so Unity builds the Generic avatar.
        // It is never assigned to the motion set; only the root-locked clips are used.
        ConfigureGeneric(modelImporter, true);
        modelImporter.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        foreach (var row in manifest.materials)
        {
            Material material = MaterialFor(row, manifest, shader);
            modelImporter.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), row.name), material);
        }
        modelImporter.SaveAndReimport();
        Debug.Log("AHOLO_REVIEW_STEP Model imported; preparing walk");

        string walkPath = Folder + "/" + manifest.walkAnimation;
        var walkImporter = RequiredModelImporter(walkPath);
        ConfigureGeneric(walkImporter, true);
        walkImporter.materialImportMode = ModelImporterMaterialImportMode.None;
        walkImporter.SaveAndReimport();
        var clips = walkImporter.defaultClipAnimations;
        if (clips == null || clips.Length != 1)
            throw new InvalidDataException("The walk file must contain exactly one take.");
        var settings = clips[0];
        settings.name = manifest.clipName;
        settings.loopTime = true;
        settings.loopPose = false;
        settings.wrapMode = WrapMode.Loop;
        settings.keepOriginalOrientation = true;
        settings.keepOriginalPositionXZ = true;
        settings.keepOriginalPositionY = true;
        settings.lockRootRotation = true;
        settings.lockRootPositionXZ = true;
        settings.lockRootHeightY = true;
        walkImporter.clipAnimations = new[] { settings };
        walkImporter.SaveAndReimport();
        var walk = AssetDatabase.LoadAllAssetsAtPath(walkPath).OfType<AnimationClip>()
            .SingleOrDefault(c => c.name == manifest.clipName);
        if (walk == null || Mathf.Abs(walk.length - manifest.clipDuration) > .04f)
            throw new InvalidDataException("Walk duration or clip name changed during import.");

        var motionSet = AssetDatabase.LoadAssetAtPath<AholoMotionSet>(MotionSetPath);
        if (motionSet == null)
        {
            motionSet = ScriptableObject.CreateInstance<AholoMotionSet>();
            AssetDatabase.CreateAsset(motionSet, MotionSetPath);
        }
        // This dedicated set deliberately contains only the real delivered walk.
        motionSet.idle = motionSet.run = motionSet.light = motionSet.heavy = motionSet.skill = null;
        motionSet.guard = motionSet.dodge = motionSet.jumpStart = motionSet.jumpLoop = motionSet.jumpLand = null;
        motionSet.hit = motionSet.knockback = motionSet.death = null;
        motionSet.walk = walk;
        AnimationClip rawLight = null;
        if (File.Exists(LightInPlacePath) && File.Exists(LightPath))
        {
            motionSet.light = ImportLightClip(true);
            rawLight = ImportLightClip(false);
        }
        motionSet.walkReferenceSpeed = manifest.walkReferenceSpeed;
        EditorUtility.SetDirty(motionSet);
        Debug.Log("AHOLO_REVIEW_STEP Building isolated review prefab");

        var previewScene = EditorSceneManager.NewPreviewScene();
        GameObject root = EditorUtility.CreateGameObjectWithHideFlags("Hongfu_Aholo_WalkReview", HideFlags.HideAndDontSave);
        SceneManager.MoveGameObjectToScene(root, previewScene);
        root.hideFlags = HideFlags.None;
        try
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (source == null) throw new FileNotFoundException("Imported Hongfu model missing.");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            model.name = "Hongfu_Aholo_Model";
            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = null;
            var transforms = model.GetComponentsInChildren<Transform>(true);
            Transform FindBone(string name) => transforms.SingleOrDefault(t => t.name == "mixamorig:" + name);
            var foot = FindBone("RightFoot");
            var toe = FindBone("RightToeBase");
            var hand = FindBone("RightHand");
            var hips = FindBone("Hips");
            if (foot == null || toe == null || hand == null || hips == null)
                throw new InvalidDataException("Required exported bones were not preserved.");
            Vector3 forward = Vector3.ProjectOnPlane(toe.position - foot.position, Vector3.up);
            var leftFoot = FindBone("LeftFoot");
            var leftToe = FindBone("LeftToeBase");
            if (leftFoot != null && leftToe != null)
                forward += Vector3.ProjectOnPlane(leftToe.position - leftFoot.position, Vector3.up);
            if (forward.sqrMagnitude < .000001f) throw new InvalidDataException("Cannot measure the source forward axis.");
            float yaw = Vector3.SignedAngle(forward, Vector3.forward, Vector3.up);
            // A visual wrapper correction preserves all animation bone paths and local curves.
            model.transform.localRotation = Quaternion.AngleAxis(yaw, Vector3.up) * model.transform.localRotation;

            var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length != manifest.meshCount)
                throw new InvalidDataException("Expected all 18 skinned mesh objects.");
            foreach (var skin in skins)
                if (skin.sharedMesh == null || skin.bones.Length == 0 || skin.bones.Any(b => b == null))
                    throw new InvalidDataException("A mesh lost skin bindings: " + skin.name);
            var bones = transforms.Where(t => t.name.StartsWith("mixamorig:", StringComparison.Ordinal)).ToArray();
            // Count all submeshes, without changing geometry or mesh optimization.
            int triangles = skins.Sum(s => Enumerable.Range(0, s.sharedMesh.subMeshCount)
                .Sum(i => (int)(s.sharedMesh.GetIndexCount(i) / 3)));
            if (triangles != manifest.triangleCount || bones.Length != manifest.boneCount)
                throw new InvalidDataException("Geometry or skeleton count differs from the verified FBX: triangles="
                    + triangles + "/" + manifest.triangleCount + ", bones=" + bones.Length + "/" + manifest.boneCount
                    + "; per-mesh=" + string.Join(";", skins.Select(s => s.name + "=" +
                        Enumerable.Range(0, s.sharedMesh.subMeshCount).Sum(i => (int)s.sharedMesh.GetIndexCount(i) / 3))));
            Bounds bounds = RestBounds(skins);
            if (Mathf.Abs(bounds.size.y - manifest.sourceHeightMeters) > .02f)
                throw new InvalidDataException("Model meter scale changed: height=" + bounds.size.y);
            model.transform.localPosition -= Vector3.up * bounds.min.y;
            if (rawLight != null)
            {
                motionSet.lightRootMotion = CaptureLightRoot(root.transform, model, hips, rawLight);
                CheckRootLockedLight(model, hips, motionSet.light);
            }

            // Sampling only this temporary prefab construction object cannot touch the user's scene.
            Vector3 beforePosition = model.transform.localPosition;
            Quaternion beforeRotation = model.transform.localRotation;
            walk.SampleAnimation(model, 0f);
            Vector3 firstHips = model.transform.InverseTransformPoint(hips.position);
            Quaternion[] firstRotations = bones.Select(b => b.localRotation).ToArray();
            walk.SampleAnimation(model, walk.length * .5f);
            float sampledRotation = bones.Select((b, i) => Quaternion.Angle(firstRotations[i], b.localRotation)).Max();
            if (sampledRotation < 1f)
                throw new InvalidDataException("The clip did not animate the sample skeleton; check Generic curve paths.");
            walk.SampleAnimation(model, Mathf.Max(0f, walk.length - .00001f));
            Vector3 finalHips = model.transform.InverseTransformPoint(hips.position);
            float drift = Vector3.ProjectOnPlane(finalHips - firstHips, Vector3.up).magnitude;
            walk.SampleAnimation(model, 0f);
            model.transform.localPosition = beforePosition;
            model.transform.localRotation = beforeRotation;
            if (drift > .01f) throw new InvalidDataException("In-place walk has unexpected horizontal drift: " + drift);

            var driver = root.AddComponent<AholoAnimationDriver>();
            driver.motionSet = motionSet;
            driver.targetAnimator = animator;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null) throw new IOException("Failed to create review prefab.");
            motionSet.visualPrefab = prefab;
            EditorUtility.SetDirty(motionSet);
            AssetDatabase.SaveAssets();
            var report = new ReviewReport
            {
                prefab = PrefabPath, motionSet = MotionSetPath, source = manifest.sourceFile,
                sourceSha256 = manifest.sourceSha256, walkClip = walk.name,
                lightClip = motionSet.light != null ? motionSet.light.name : null,
                lightSeconds = motionSet.light != null ? motionSet.light.length : 0f,
                lightRootDisplacement = motionSet.lightRootMotion.Ready
                    ? motionSet.lightRootMotion.Evaluate(motionSet.lightRootMotion.duration) : Vector3.zero,
                rightHandPath = AnimationUtility.CalculateTransformPath(hand, model.transform),
                clipSeconds = walk.length, expectedSeconds = manifest.clipDuration,
                heightMeters = bounds.size.y, sourceHeightMeters = manifest.sourceHeightMeters,
                forwardYawCorrectionDegrees = yaw, meshes = skins.Length, bones = bones.Length,
                triangles = triangles, materials = manifest.materials.Length,
                walkRootHorizontalDriftMeters = drift,
                missingMotions = manifest.missingMotions.Where(m => m != "Light" || motionSet.light == null).ToArray(),
                sampledPoseMaxRotationDegrees = sampledRotation,
                warnings = new[] { "Only delivered real clips are assigned. Other action slots are intentionally empty.",
                    "1.31 million triangles and original texture resolution retained; optimization is deferred.",
                    "No sword is attached. RightHand is the future independent sword attachment point.",
                    "Light uses a root-locked clip plus the original horizontal displacement curve for CharacterController playback." },
                wroteScene = false
            };
            Directory.CreateDirectory("Verification/Aholo20261116");
            File.WriteAllText("Verification/Aholo20261116/Hongfu_WalkReview.json", JsonUtility.ToJson(report, true));
            Debug.Log("AHOLO_HONGFU_REVIEW_READY " + PrefabPath + " | No scene or current player was modified.");
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(previewScene);
        }
    }

    [MenuItem("FengChenInn/Aholo 20261116/Add Real Hongfu Light Sample")]
    public static void AddHongfuLight()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before importing the review clip.");
        var set = AssetDatabase.LoadAssetAtPath<AholoMotionSet>(MotionSetPath);
        if (set == null) throw new InvalidOperationException("Prepare Hongfu review assets first.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException("Prepare the review prefab before adding light attack.");
        set.light = ImportLightClip(true);
        var raw = ImportLightClip(false);
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject instance = null;
        try
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
            var animator = instance.GetComponentInChildren<Animator>(true);
            var hips = instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == "mixamorig:Hips");
            set.lightRootMotion = CaptureLightRoot(instance.transform, animator.gameObject, hips, raw);
            CheckRootLockedLight(animator.gameObject, hips, set.light);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            if (instance != null) Object.DestroyImmediate(instance);
            EditorSceneManager.ClosePreviewScene(preview);
        }
        Debug.Log("AHOLO_LIGHT_SAMPLE_READY length=" + set.light.length
            + " | Original root travel transferred to CharacterController curve. No scene player was changed.");
    }

    static AnimationClip ImportLightClip(bool inPlace)
    {
        string path = inPlace ? LightInPlacePath : LightPath;
        if (!File.Exists(path)) throw new FileNotFoundException("Real light-attack sample missing.", path);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = RequiredModelImporter(path);
        ConfigureGeneric(importer, true);
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport();
        var takes = importer.defaultClipAnimations;
        if (takes.Length != 1) throw new InvalidDataException("Expected one real light-attack take.");
        var clip = takes[0];
        clip.name = inPlace ? "Hongfu_Light_InPlace" : "Hongfu_Light_SwordAttack";
        clip.loopTime = false;
        clip.loopPose = false;
        clip.keepOriginalOrientation = true;
        clip.keepOriginalPositionXZ = clip.keepOriginalPositionY = true;
        clip.lockRootRotation = clip.lockRootPositionXZ = clip.lockRootHeightY = true;
        importer.clipAnimations = new[] { clip };
        importer.SaveAndReimport();
        var animation = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .SingleOrDefault(c => c.name == clip.name);
        if (animation == null || Mathf.Abs(animation.length - 1.5333333f) > .04f)
            throw new InvalidDataException("Light attack imported with unexpected duration.");
        return animation;
    }

    static AholoRootMotion CaptureLightRoot(Transform wrapper, GameObject animatedModel, Transform hips, AnimationClip raw)
    {
        Directory.CreateDirectory("Verification/Aholo20261116");
        File.WriteAllLines("Verification/Aholo20261116/Hongfu_LightBindings.txt",
            new[] { "Animator root: " + animatedModel.name, "Hips path: " + AnimationUtility.CalculateTransformPath(hips, animatedModel.transform),
                "Graphics: " + SystemInfo.graphicsDeviceType }
            .Concat(AnimationUtility.GetCurveBindings(raw).Select(b => b.path + " | " + b.type.Name + " | " + b.propertyName)));
        // Sample the real imported clip in its final Unity-facing space so handedness
        // and the wrapper's yaw correction cannot invert the lateral travel curve.
        Vector3 modelPosition = animatedModel.transform.localPosition;
        Quaternion modelRotation = animatedModel.transform.localRotation;
        var x = new List<Keyframe>();
        var z = new List<Keyframe>();
        Vector3 origin = Vector3.zero;
        int frames = Mathf.RoundToInt(raw.length * 30f);
        for (int i = 0; i <= frames; i++)
        {
            float time = i == frames ? raw.length : i / 30f;
            raw.SampleAnimation(animatedModel, time);
            animatedModel.transform.localPosition = modelPosition;
            animatedModel.transform.localRotation = modelRotation;
            Vector3 position = wrapper.InverseTransformPoint(hips.position);
            if (i == 0) origin = position;
            Vector3 delta = position - origin;
            x.Add(new Keyframe(time, delta.x));
            z.Add(new Keyframe(time, delta.z));
        }
        var profile = new AholoRootMotion
        {
            enabled = true, duration = raw.length,
            x = new AnimationCurve(x.ToArray()), z = new AnimationCurve(z.ToArray())
        };
        foreach (AnimationCurve curve in new[] { profile.x, profile.z })
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
        Vector3 travel = profile.Evaluate(raw.length);
        if (travel.magnitude < 1.9f || travel.magnitude > 2.2f)
            throw new InvalidDataException("Expected about 2.029m native light-attack root travel; sampled " + travel);
        raw.SampleAnimation(animatedModel, 0f);
        animatedModel.transform.localPosition = modelPosition;
        animatedModel.transform.localRotation = modelRotation;
        return profile;
    }

    static void CheckRootLockedLight(GameObject model, Transform hips, AnimationClip clip)
    {
        Vector3 first = Vector3.zero;
        float maximum = 0f;
        for (int i = 0; i <= 46; i++)
        {
            clip.SampleAnimation(model, clip.length * i / 46f);
            Vector3 position = model.transform.InverseTransformPoint(hips.position);
            if (i == 0) first = position;
            maximum = Mathf.Max(maximum, Vector3.ProjectOnPlane(position - first, Vector3.up).magnitude);
        }
        clip.SampleAnimation(model, 0f);
        if (maximum > .001f)
            throw new InvalidDataException("Root-locked light still moves away from its controller: " + maximum);
    }

    static ModelImporter RequiredModelImporter(string path) => AssetImporter.GetAtPath(path) as ModelImporter
        ?? throw new FileNotFoundException("Required FBX is not imported: " + path);

    [MenuItem("FengChenInn/Aholo 20261116/Render Hongfu Review")]
    public static void RenderHongfuReview()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var motions = AssetDatabase.LoadAssetAtPath<AholoMotionSet>(MotionSetPath);
        if (prefab == null || motions == null) throw new FileNotFoundException("Prepare the real review prefab first.");
        Directory.CreateDirectory("Verification/Aholo20261116");
        foreach (var pair in new[] { ("Walk", motions.walk, .33f), ("Light", motions.light, .6f) })
        {
            if (pair.Item2 == null) continue;
            var preview = new PreviewRenderUtility();
            GameObject instance = null;
            Texture2D texture = null;
            try
            {
                instance = Object.Instantiate(prefab);
                preview.AddSingleGO(instance);
                var animator = instance.GetComponentInChildren<Animator>();
                pair.Item2.SampleAnimation(animator.gameObject, pair.Item2.length * pair.Item3);
                Bounds bounds = instance.GetComponentsInChildren<Renderer>().First().bounds;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.12f, .13f, .15f, 1f);
                preview.camera.fieldOfView = 30f;
                preview.camera.nearClipPlane = .01f;
                preview.camera.farClipPlane = 100f;
                Vector3 direction = new Vector3(.48f, .12f, 1f).normalized;
                preview.camera.transform.position = bounds.center + direction * (bounds.size.y * 2.6f);
                preview.camera.transform.LookAt(bounds.center);
                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35f, -40f, 0f);
                preview.lights[1].intensity = .7f;
                preview.lights[1].transform.rotation = Quaternion.Euler(25f, 140f, 0f);
                preview.ambientColor = new Color(.35f, .35f, .35f, 1f);
                preview.BeginStaticPreview(new Rect(0f, 0f, 1100f, 1100f));
                preview.Render(true);
                texture = preview.EndStaticPreview();
                File.WriteAllBytes("Verification/Aholo20261116/Hongfu_" + pair.Item1 + "_Preview.png", texture.EncodeToPNG());
            }
            finally
            {
                if (texture != null) Object.DestroyImmediate(texture);
                preview.Cleanup();
            }
        }
        Debug.Log("AHOLO_PREVIEW_READY: rendered real walk and light frames; no scene saved.");
    }

    static void ConfigureGeneric(ModelImporter importer, bool animation)
    {
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.motionNodeName = "AholoRig";
        importer.importAnimation = animation;
        importer.optimizeGameObjects = false;
        // Skeleton-only FBX must keep AholoRig, matching the full model's curve paths.
        importer.preserveHierarchy = true;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.weldVertices = false;
        importer.optimizeMeshPolygons = false;
        importer.optimizeMeshVertices = false;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.isReadable = true;
        importer.importBlendShapes = true;
        importer.importNormals = ModelImporterNormals.Import;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
    }
    static Material MaterialFor(MaterialRow row, ImportManifest manifest, Shader shader)
    {
        string path = Folder + "/Materials/" + row.name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.shader = shader;
        var colorMap = TextureFor(row.baseColorTexture, manifest, false, true);
        var normalMap = TextureFor(row.normalTexture, manifest, true, false);
        var metallicGloss = TextureFor(row.metallicGlossTexture, manifest, false, false);
        float[] c = row.color;
        Color linear = c != null && c.Length == 4 ? new Color(c[0], c[1], c[2], c[3]) : Color.white;
        material.SetColor("_BaseColor", linear.gamma);
        material.SetTexture("_BaseMap", colorMap);
        material.SetFloat("_WorkflowMode", 1f);
        material.SetFloat("_Metallic", row.metallic);
        material.SetFloat("_Smoothness", metallicGloss != null ? 1f : 1f - row.roughness);
        material.SetFloat("_SmoothnessTextureChannel", 0f);
        material.SetTexture("_MetallicGlossMap", metallicGloss);
        Keyword(material, "_METALLICSPECGLOSSMAP", metallicGloss != null);
        material.SetTexture("_BumpMap", normalMap);
        material.SetFloat("_BumpScale", row.normalScale);
        Keyword(material, "_NORMALMAP", normalMap != null);
        material.SetFloat("_Cull", row.doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
        material.doubleSidedGI = row.doubleSided;
        bool cutout = row.alphaMode == "MASK";
        bool transparent = row.alphaMode == "BLEND";
        material.SetFloat("_AlphaClip", cutout ? 1f : 0f);
        material.SetFloat("_Cutoff", row.alphaCutoff);
        material.SetFloat("_Surface", transparent ? 1f : 0f);
        material.SetFloat("_ZWrite", transparent ? 0f : 1f);
        material.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
        material.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
        Keyword(material, "_ALPHATEST_ON", cutout);
        Keyword(material, "_SURFACE_TYPE_TRANSPARENT", transparent);
        material.renderQueue = transparent ? (int)RenderQueue.Transparent : cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
        material.SetOverrideTag("RenderType", transparent ? "Transparent" : cutout ? "TransparentCutout" : "Opaque");
        EditorUtility.SetDirty(material);
        return material;
    }
    static void Keyword(Material material, string name, bool enabled)
    {
        if (enabled) material.EnableKeyword(name); else material.DisableKeyword(name);
    }
    static Texture2D TextureFor(string relative, ImportManifest manifest, bool normal, bool srgb)
    {
        if (string.IsNullOrEmpty(relative)) return null;
        string path = Folder + "/" + relative;
        var info = manifest.textures.Single(t => t.file == relative);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new FileNotFoundException("Missing mapped texture: " + path);
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        int size = Mathf.NextPowerOfTwo(Mathf.Max(info.width, info.height));
        bool changed = importer.textureType != type || importer.sRGBTexture != srgb
            || importer.maxTextureSize != size || importer.textureCompression != TextureImporterCompression.Uncompressed
            || importer.npotScale != TextureImporterNPOTScale.None || importer.alphaSource != TextureImporterAlphaSource.FromInput
            || importer.alphaIsTransparency || !importer.mipmapEnabled;
        if (changed)
        {
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Bounds RestBounds(IEnumerable<SkinnedMeshRenderer> skins)
    {
        Bounds bounds = default;
        bool first = true;
        foreach (var skin in skins)
            foreach (Vector3 vertex in skin.sharedMesh.vertices)
            {
                Vector3 point = skin.transform.TransformPoint(vertex);
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
        return bounds;
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
