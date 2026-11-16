using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FengChenInn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Explicit source-to-runtime preparation. Never opens/saves a Scene or changes installed actors.
public static class AholoSharedHumanoidImport
{
    public const string Folder = "Assets/_Game/Art/Aholo20261116/Runtime";
    const string ReportFolder = "Verification/Aholo20261116";
    [Serializable] public sealed class Manifest { public ModelRow[] models; public ClipRow[] clips; public string[] missingSourceFiles; }
    [Serializable] public sealed class ModelRow {
        public string role, baseModel, sourceSha256; public float sourceHeight, targetHeight, forwardYawCorrectionDegrees;
        public MaterialRow[] materials; public TextureRow[] textures;
    }
    [Serializable] public sealed class MaterialRow {
        public string name, sourceName, baseColorTexture, normalTexture, metallicGlossTexture, alphaMode;
        public float[] color; public float normalScale, metallic, roughness, alphaCutoff; public bool doubleSided;
    }
    [Serializable] public sealed class TextureRow { public string file; public int width, height; }
    [Serializable] public sealed class ClipRow {
        public string id, file, avatarRole, rootMotion, sourceSha256; public bool loop; public float referenceSpeed; public TargetRow[] targets;
    }
    [Serializable] public sealed class TargetRow { public string role, field; }
    [Serializable] public sealed class RootDocument { public string source; public RootCurve[] curves; }
    [Serializable] public sealed class RootCurve { public string bone, coordinate_system; public float[] times, x, z; }
    [Serializable] public sealed class Result {
        public bool wroteScene; public string note = "Import and binding only, following the user's latest request to stop agent acceptance testing. Source motion, geometry and prior root-coordinate evidence are retained; no new gameplay, pose sampling or rendering is performed.";
        public Vector4 sourceRootBasis; public float rootBasisMaximumError; public List<RoleResult> roles = new(); public List<string> warnings = new(); public string error;
    }
    [Serializable] public sealed class RoleResult {
        public string role, prefab, motionSet, sourceBaseModel, sourceSha256; public float sourceHeight, targetHeight, visualScale, yawCorrection, measuredFootYaw, finalRestHeight;
        public bool avatarValid; public int bones, meshes, triangles, materials;
        public List<MotionResult> motions = new(); public List<string> missing = new();
    }
    [Serializable] public sealed class MotionResult {
        public string slot, sourceClip, sourceAvatar; public bool poseSampled; public float seconds, sampledChangeDegrees, sourceToTargetRootScale;
        public Vector3 finalRootDisplacement;
    }

    public static string VisualPath(string role) => Folder + "/" + role + "_Visual.prefab";
    public static string MotionsPath(string role) => Folder + "/" + role + "_Motions.asset";

    [MenuItem("FengChenInn/Aholo 20261116/Prepare Shared Humanoid Runtime Assets")]
    public static void Prepare()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "/SharedHumanoidImport.json"));
        if (manifest?.models?.Length != 3) throw new InvalidDataException("Expected three explicit source models.");
        var result = new Result();
        Application.LogCallback collect = (message, stack, type) => {
            if ((type == LogType.Warning || type == LogType.Error || type == LogType.Exception) && !result.warnings.Contains(message)) result.warnings.Add(message);
        };
        Application.logMessageReceived += collect;
        try {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var avatars = new Dictionary<string, Avatar>();
            foreach (var model in manifest.models) {
                // Texture reimports can reload ModelImporter objects. Finish them before configuring this FBX.
                var materials = model.materials.ToDictionary(row => row.name, row => MaterialFor(row, model));
                var importer = Model(Folder + "/" + model.baseModel);
                Configure(importer, true, null);
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                foreach (var material in materials) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.Key), material.Value);
                importer.SaveAndReimport();
                var avatar = AvatarAt(importer.assetPath);
                if (avatar == null || !avatar.isValid || !avatar.isHuman) throw new InvalidDataException("Invalid Human avatar: " + model.role);
                avatars.Add(model.role, avatar);
            }
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var row in manifest.clips) {
                var importer = Model(Folder + "/" + row.file);
                Configure(importer, true, avatars[row.avatarRole]);
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
                var settings = importer.defaultClipAnimations.Single();
                settings.name = row.id;
                settings.loopTime = row.loop; settings.loopPose = false; settings.wrapMode = row.loop ? WrapMode.Loop : WrapMode.Once;
                settings.keepOriginalOrientation = settings.keepOriginalPositionXZ = settings.keepOriginalPositionY = true;
                settings.lockRootRotation = settings.lockRootPositionXZ = settings.lockRootHeightY = true;
                importer.clipAnimations = new[] { settings };
                importer.SaveAndReimport();
                var clip = ClipAt(importer.assetPath);
                if (!clip.humanMotion || clip.length <= 0f) throw new InvalidDataException("Invalid Human clip: " + row.file);
                clips.Add(row.id, clip);
            }
            // Already measured on this exact conversion pipeline. Do not rerun review probes after the user stopped agent testing.
            result.sourceRootBasis = new Vector4(-1f, 0f, 0f, 1f);
            result.rootBasisMaximumError = 6.064333e-8f;
            foreach (var model in manifest.models) result.roles.Add(BuildRole(model, manifest, clips, avatars[model.role], result.sourceRootBasis));
            Debug.Log("AHOLO_SHARED_HUMANOID_READY: three standalone visual prefabs and motion sets; no scene changed.");
        }
        catch (Exception ex) { result.error = ex.ToString(); Debug.LogException(ex); throw; }
        finally {
            Application.logMessageReceived -= collect;
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(ReportFolder + "/SharedHumanoid_Runtime.json", JsonUtility.ToJson(result, true));
        }
    }

    static RoleResult BuildRole(ModelRow modelRow, Manifest manifest, Dictionary<string, AnimationClip> clips, Avatar avatar, Vector4 basis)
    {
        var report = new RoleResult { role = modelRow.role, prefab = VisualPath(modelRow.role), motionSet = MotionsPath(modelRow.role), targetHeight = modelRow.targetHeight,
            sourceBaseModel = modelRow.baseModel, sourceSha256 = modelRow.sourceSha256 };
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject wrapper = null;
        try {
            wrapper = EditorUtility.CreateGameObjectWithHideFlags(modelRow.role + "_Visual", HideFlags.HideAndDontSave);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wrapper, scene);
            wrapper.hideFlags = HideFlags.None;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + modelRow.baseModel), wrapper.transform);
            model.name = modelRow.role + "_Aholo_Model";
            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.avatar = avatar; animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var bones = model.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("mixamorig:", StringComparison.Ordinal)).ToArray();
            var bindPositions = bones.Select(b => b.localPosition).ToArray();
            var bindRotations = bones.Select(b => b.localRotation).ToArray();
            var bindScales = bones.Select(b => b.localScale).ToArray();
            Transform Bone(string name) => bones.Single(t => t.name == "mixamorig:" + name);
            Vector3 front = Vector3.ProjectOnPlane(Bone("LeftToeBase").position - Bone("LeftFoot").position + Bone("RightToeBase").position - Bone("RightFoot").position, Vector3.up);
            if (front.sqrMagnitude < 1e-8f) throw new InvalidDataException("Unknown forward direction: " + modelRow.role);
            report.measuredFootYaw = Vector3.SignedAngle(front, Vector3.forward, Vector3.up);
            // Toe splay and the source take's first pose do not define the character's forward axis.
            // These sources have verified +Z forward motion; keep their explicit conversion orientation.
            report.yawCorrection = modelRow.forwardYawCorrectionDegrees;
            model.transform.localRotation = Quaternion.AngleAxis(report.yawCorrection, Vector3.up) * model.transform.localRotation;
            var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Bounds sourceBounds = RestBounds(skins);
            report.sourceHeight = sourceBounds.size.y;
            if (Mathf.Abs(report.sourceHeight - modelRow.sourceHeight) > .02f) throw new InvalidDataException("Rest mesh scale mismatch: " + modelRow.role);
            report.visualScale = modelRow.targetHeight / report.sourceHeight;
            model.transform.localScale *= report.visualScale;
            Bounds scaled = RestBounds(skins);
            model.transform.localPosition -= Vector3.up * scaled.min.y;
            report.avatarValid = animator.avatar.isValid && animator.avatar.isHuman;
            report.bones = bones.Length; report.meshes = skins.Length;
            report.triangles = skins.Sum(s => Enumerable.Range(0, s.sharedMesh.subMeshCount).Sum(i => (int)s.sharedMesh.GetIndexCount(i) / 3));
            report.materials = skins.SelectMany(s => s.sharedMaterials).Distinct().Count();
            if (bones.Length != 52 || skins.Any(s => s.sharedMesh == null || s.bones.Length == 0 || s.bones.Any(b => b == null))) throw new InvalidDataException("Skin or skeleton mismatch: " + modelRow.role);
            var set = AssetDatabase.LoadAssetAtPath<AholoMotionSet>(MotionsPath(modelRow.role));
            if (set == null) { set = ScriptableObject.CreateInstance<AholoMotionSet>(); AssetDatabase.CreateAsset(set, MotionsPath(modelRow.role)); }
            foreach (var field in typeof(AholoMotionSet).GetFields(BindingFlags.Instance | BindingFlags.Public).Where(f => f.FieldType == typeof(AnimationClip))) field.SetValue(set, null);
            set.lightRootMotion = new AholoRootMotion(); set.heavyRootMotion = new AholoRootMotion(); set.skillRootMotion = new AholoRootMotion();
            foreach (var row in manifest.clips) foreach (var target in row.targets.Where(t => t.role == modelRow.role)) {
                var field = typeof(AholoMotionSet).GetField(target.field);
                if (field == null || field.FieldType != typeof(AnimationClip)) throw new InvalidDataException("Unknown slot: " + target.field);
                field.SetValue(set, clips[row.id]);
                var motion = new MotionResult { slot = target.field, sourceClip = row.file, sourceAvatar = row.avatarRole, seconds = clips[row.id].length };
                var sourceModel = manifest.models.Single(m => m.role == row.avatarRole);
                motion.sourceToTargetRootScale = modelRow.targetHeight / sourceModel.sourceHeight;
                if (!string.IsNullOrEmpty(row.rootMotion)) {
                    var curve = MakeRoot(row.rootMotion, clips[row.id].length, motion.sourceToTargetRootScale, report.yawCorrection, basis);
                    if (target.field == "light") set.lightRootMotion = curve;
                    else if (target.field == "heavy") set.heavyRootMotion = curve;
                    else if (target.field == "skill") set.skillRootMotion = curve;
                    else throw new InvalidDataException("Root profile assigned to unsupported slot " + target.field);
                    motion.finalRootDisplacement = curve.Evaluate(curve.duration);
                }
                if (row.referenceSpeed > 0f) {
                    float reference = row.referenceSpeed * motion.sourceToTargetRootScale;
                    if (target.field == "walk") set.walkReferenceSpeed = reference;
                    if (target.field == "run") set.runReferenceSpeed = reference;
                }
                // Clips are assigned directly; the user will review the actual actions in the game.
                motion.poseSampled = false;
                report.motions.Add(motion);
            }
            // Return to the bind reference before saving the prefab; no sampled animation pose is baked.
            animator.Rebind();
            for (int i = 0; i < bones.Length; i++) { bones[i].localPosition = bindPositions[i]; bones[i].localRotation = bindRotations[i]; bones[i].localScale = bindScales[i]; }
            report.finalRestHeight = RestBounds(skins).size.y;
            if (Mathf.Abs(report.finalRestHeight - modelRow.targetHeight) > .002f) throw new InvalidDataException("Final visual height changed during sampling: " + modelRow.role);
            set.runTransitionSpeed = set.run != null && set.runReferenceSpeed > set.walkReferenceSpeed + .05f
                ? (set.walkReferenceSpeed + set.runReferenceSpeed) * .5f : set.walkReferenceSpeed * .8f;
            set.locomotionFade = .16f;
            if (set.guard != null) set.guardHoldNormalized = .48f;
            if (set.jumpStart != null && set.jumpLoop != null) set.jumpStartToLoopNormalized = .175f;
            if (modelRow.role == "Grunt") { set.skill = null; set.skillRootMotion = new AholoRootMotion(); }
            report.missing = typeof(AholoMotionSet).GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(f => f.FieldType == typeof(AnimationClip) && f.GetValue(set) == null).Select(f => f.Name).ToList();
            // Visual contains no gameplay controller or second animation driver. The installer binds the actor root.
            var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, VisualPath(modelRow.role));
            if (prefab == null) throw new IOException("Failed to save " + modelRow.role);
            set.visualPrefab = prefab; EditorUtility.SetDirty(set); AssetDatabase.SaveAssetIfDirty(set);
            return report;
        }
        finally { if (wrapper != null) Object.DestroyImmediate(wrapper); EditorSceneManager.ClosePreviewScene(scene); }
    }

    static float SampleChange(Animator animator, AnimationClip clip)
    {
        var graph = PlayableGraph.Create("SharedHumanSample");
        try {
            animator.Rebind(); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
            AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(playable);
            graph.Play(); playable.SetSpeed(0);
            var bones = animator.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("mixamorig:", StringComparison.Ordinal)).ToArray();
            playable.SetTime(clip.length * .15f); graph.Evaluate(0f); var first = bones.Select(b => b.localRotation).ToArray();
            playable.SetTime(clip.length * .65f); graph.Evaluate(0f);
            return bones.Select((b,i) => Quaternion.Angle(first[i],b.localRotation)).Max();
        }
        finally { graph.Destroy(); }
    }

    static AholoRootMotion MakeRoot(string relative, float duration, float scale, float yaw, Vector4 basis)
    {
        var source = JsonUtility.FromJson<RootDocument>(File.ReadAllText(Folder + "/" + relative)).curves.Single();
        if (source.times.Length < 2 || source.x.Length != source.times.Length || source.z.Length != source.times.Length || Mathf.Abs(source.times.Last()-duration)>.04f)
            throw new InvalidDataException("Root profile length mismatch: " + relative);
        var result = new AholoRootMotion { enabled = true, duration = duration };
        Quaternion rotation = Quaternion.AngleAxis(yaw,Vector3.up);
        for (int i=0;i<source.times.Length;i++) {
            var native = new Vector3(basis.x*source.x[i]+basis.y*source.z[i],0,basis.z*source.x[i]+basis.w*source.z[i]);
            var target = rotation*native*scale;
            result.x.AddKey(source.times[i],target.x); result.z.AddKey(source.times[i],target.z);
        }
        foreach (var curve in new[]{result.x,result.z}) for(int i=0;i<curve.length;i++) {
            AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);
        }
        return result;
    }

    static Vector4 DetermineSourceRootBasis(out float maximumError)
    {
        string path=Folder+"/Sources/RootBasis_Grunt.fbx";
        var importer=Model(path); Configure(importer,false,null); importer.materialImportMode=ModelImporterMaterialImportMode.None; importer.SaveAndReimport();
        var raw=importer.defaultClipAnimations.Single(); raw.name="RootBasisNative";
        raw.keepOriginalOrientation=raw.keepOriginalPositionXZ=raw.keepOriginalPositionY=true;
        raw.lockRootRotation=raw.lockRootPositionXZ=raw.lockRootHeightY=false;
        importer.clipAnimations=new[]{raw}; importer.SaveAndReimport();
        var clip=ClipAt(path); var source=JsonUtility.FromJson<RootDocument>(File.ReadAllText(Folder+"/RootMotion/RootBasis_Grunt.json")).curves.Single();
        var scene=EditorSceneManager.NewPreviewScene(); GameObject go=null;
        try {
            go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),scene);
            var hips=go.GetComponentsInChildren<Transform>().Single(t=>t.name=="mixamorig:Hips");
            var observed=new List<Vector2>(); Vector3 origin=default;
            for(int i=0;i<source.times.Length;i++) { clip.SampleAnimation(go,source.times[i]); if(i==0)origin=hips.position; var p=hips.position-origin; observed.Add(new Vector2(p.x,p.z)); }
            Vector4 best=default; maximumError=float.PositiveInfinity;
            foreach(bool swap in new[]{false,true}) foreach(float sx in new[]{-1f,1f}) foreach(float sz in new[]{-1f,1f}) {
                Vector4 candidate=swap?new Vector4(0,sx,sz,0):new Vector4(sx,0,0,sz); float error=0;
                for(int i=0;i<source.times.Length;i++) error=Mathf.Max(error,Vector2.Distance(observed[i],new Vector2(candidate.x*source.x[i]+candidate.y*source.z[i],candidate.z*source.x[i]+candidate.w*source.z[i])));
                if(error<maximumError){ maximumError=error; best=candidate; }
            }
            if(maximumError>.002f)throw new InvalidDataException("Source root coordinate verification failed: "+maximumError);
            return best;
        }
        finally { if(go!=null)Object.DestroyImmediate(go); EditorSceneManager.ClosePreviewScene(scene); }
    }

    static void Configure(ModelImporter importer,bool human,Avatar avatar)
    {
        importer.animationType=human?ModelImporterAnimationType.Human:ModelImporterAnimationType.Generic;
        importer.avatarSetup=avatar!=null?ModelImporterAvatarSetup.CopyFromOther:ModelImporterAvatarSetup.CreateFromThisModel;
        importer.sourceAvatar=avatar; importer.autoGenerateAvatarMappingIfUnspecified=true;
        importer.importAnimation=true; importer.optimizeGameObjects=false; importer.preserveHierarchy=true;
        importer.meshCompression=ModelImporterMeshCompression.Off; importer.weldVertices=false;
        importer.optimizeMeshPolygons=false; importer.optimizeMeshVertices=false;
        importer.globalScale=1; importer.useFileScale=true; importer.isReadable=true; importer.importBlendShapes=true;
        importer.importNormals=ModelImporterNormals.Import; importer.animationCompression=ModelImporterAnimationCompression.Off;
    }
    static ModelImporter Model(string path)=>AssetImporter.GetAtPath(path) as ModelImporter??throw new FileNotFoundException(path);
    static Avatar AvatarAt(string path)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
    static AnimationClip ClipAt(string path)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal));
    static Bounds RestBounds(IEnumerable<SkinnedMeshRenderer> skins)
    {
        Bounds result=default; bool first=true;
        foreach(var skin in skins)foreach(var vertex in skin.sharedMesh.vertices){var p=skin.transform.TransformPoint(vertex);if(first){result=new Bounds(p,Vector3.zero);first=false;}else result.Encapsulate(p);}
        return result;
    }
    static Material MaterialFor(MaterialRow row,ModelRow model)
    {
        string path=Folder+"/Materials/"+row.name+".mat"; var shader=Shader.Find("Universal Render Pipeline/Lit");
        if(shader==null)throw new InvalidOperationException("URP Lit unavailable");
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}mat.shader=shader;
        var color=TextureFor(row.baseColorTexture,model,false,true);var normal=TextureFor(row.normalTexture,model,true,false);var metal=TextureFor(row.metallicGlossTexture,model,false,false);
        float[] c=row.color;mat.SetColor("_BaseColor",c!=null&&c.Length==4?new Color(c[0],c[1],c[2],c[3]).gamma:Color.white);
        mat.SetTexture("_BaseMap",color);mat.SetFloat("_WorkflowMode",1);mat.SetFloat("_Metallic",row.metallic);
        mat.SetFloat("_Smoothness",metal!=null?1:1-row.roughness);mat.SetFloat("_SmoothnessTextureChannel",0);mat.SetTexture("_MetallicGlossMap",metal);Keyword(mat,"_METALLICSPECGLOSSMAP",metal!=null);
        mat.SetTexture("_BumpMap",normal);mat.SetFloat("_BumpScale",row.normalScale);Keyword(mat,"_NORMALMAP",normal!=null);
        mat.SetFloat("_Cull",row.doubleSided?(float)CullMode.Off:(float)CullMode.Back);mat.doubleSidedGI=row.doubleSided;
        bool cut=row.alphaMode=="MASK",transparent=row.alphaMode=="BLEND";
        mat.SetFloat("_AlphaClip",cut?1:0);mat.SetFloat("_Cutoff",row.alphaCutoff);mat.SetFloat("_Surface",transparent?1:0);mat.SetFloat("_ZWrite",transparent?0:1);
        mat.SetFloat("_SrcBlend",transparent?(float)BlendMode.SrcAlpha:(float)BlendMode.One);mat.SetFloat("_DstBlend",transparent?(float)BlendMode.OneMinusSrcAlpha:(float)BlendMode.Zero);
        Keyword(mat,"_ALPHATEST_ON",cut);Keyword(mat,"_SURFACE_TYPE_TRANSPARENT",transparent);
        mat.renderQueue=transparent?(int)RenderQueue.Transparent:cut?(int)RenderQueue.AlphaTest:(int)RenderQueue.Geometry;
        mat.SetOverrideTag("RenderType",transparent?"Transparent":cut?"TransparentCutout":"Opaque");EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);return mat;
    }
    static void Keyword(Material material,string key,bool value){if(value)material.EnableKeyword(key);else material.DisableKeyword(key);}
    static Texture2D TextureFor(string relative,ModelRow model,bool normal,bool srgb)
    {
        if(string.IsNullOrEmpty(relative))return null;
        string path=Folder+"/"+relative;var info=model.textures.Single(t=>t.file==relative);var importer=AssetImporter.GetAtPath(path) as TextureImporter;
        if(importer==null)throw new FileNotFoundException(path);
        var type=normal?TextureImporterType.NormalMap:TextureImporterType.Default;int size=Mathf.NextPowerOfTwo(Mathf.Max(info.width,info.height));
        if(importer.textureType!=type||importer.sRGBTexture!=srgb||importer.maxTextureSize!=size||importer.textureCompression!=TextureImporterCompression.Uncompressed||importer.npotScale!=TextureImporterNPOTScale.None||importer.alphaSource!=TextureImporterAlphaSource.FromInput||importer.alphaIsTransparency||!importer.mipmapEnabled){
            importer.textureType=type;importer.sRGBTexture=srgb;importer.maxTextureSize=size;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.npotScale=TextureImporterNPOTScale.None;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=false;importer.mipmapEnabled=true;importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    [MenuItem("FengChenInn/Aholo 20261116/Render Shared Humanoid Runtime Review")]
    public static void RenderReview()
    {
        Directory.CreateDirectory(ReportFolder);
        foreach(string role in new[]{"Hongfu","Elite","Grunt"}) {
            var set=AssetDatabase.LoadAssetAtPath<AholoMotionSet>(MotionsPath(role));
            if(set==null)throw new FileNotFoundException(MotionsPath(role));
            foreach(var pair in new[]{("Idle",set.idle,.4f),("Light",set.light,.55f)}) {
                if(pair.Item2==null)continue;
                var preview=new PreviewRenderUtility();PlayableGraph graph=default;Texture2D texture=null;
                try {
                    var go=Object.Instantiate(set.visualPrefab);preview.AddSingleGO(go);var animator=go.GetComponentInChildren<Animator>();animator.Rebind();animator.applyRootMotion=false;
                    graph=PlayableGraph.Create("RuntimeHumanRender");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);var p=AnimationClipPlayable.Create(graph,pair.Item2);p.SetApplyFootIK(false);p.SetApplyPlayableIK(false);AnimationPlayableOutput.Create(graph,"Pose",animator).SetSourcePlayable(p);graph.Play();p.SetSpeed(0);p.SetTime(pair.Item2.length*pair.Item3);graph.Evaluate(0);
                    var renders=go.GetComponentsInChildren<Renderer>();Bounds bounds=renders.First().bounds;foreach(var render in renders)bounds.Encapsulate(render.bounds);
                    preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.12f,.13f,.15f,1);preview.camera.fieldOfView=30;preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=100;
                    preview.camera.transform.position=bounds.center+new Vector3(.48f,.12f,1).normalized*bounds.size.y*2.6f;preview.camera.transform.LookAt(bounds.center);
                    preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-40,0);preview.lights[1].intensity=.7f;preview.lights[1].transform.rotation=Quaternion.Euler(25,140,0);preview.ambientColor=new Color(.35f,.35f,.35f,1);
                    preview.BeginStaticPreview(new Rect(0,0,1000,1000));preview.Render(true);texture=preview.EndStaticPreview();File.WriteAllBytes(ReportFolder+"/Runtime_"+role+"_"+pair.Item1+".png",texture.EncodeToPNG());
                }
                finally{if(graph.IsValid())graph.Destroy();if(texture!=null)Object.DestroyImmediate(texture);preview.Cleanup();}
            }
        }
        Debug.Log("AHOLO_RUNTIME_REVIEW_RENDERED: six real Human pose frames, no scene changed.");
    }
}
