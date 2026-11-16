using System;
using System.IO;
using FengChenInn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FemaleWalkMotionBaker
{
    const string Folder = "Assets/_Game/Art/Animations/KevinIglesias/";
    [Serializable] class Pack { public Motion[] clips; }
    [Serializable] class Motion { public string name; public float length; public Pose[] frames; }
    [Serializable] class Pose { public Vector3 lt, ls, lf, rt, rs, rf; }
    [MenuItem("FengChenInn/Bake Female Walk Motions")]
    public static void Bake()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit play mode first.");
        if (UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>() == null)
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/ActionLab.unity");
        var fighter = UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>();
        if (fighter == null || fighter.skinnedVisual == null) throw new InvalidOperationException("Female placeholder missing.");
        var input = JsonUtility.FromJson<Pack>(File.ReadAllText(Folder + "WalkSamples.json"));
        if (input?.clips == null || input.clips.Length != 8) throw new InvalidDataException("Expected eight walks.");
        var instance = UnityEngine.Object.Instantiate(fighter.skinnedVisual.gameObject);
        try {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var target = instance.GetComponent<RocketboxVisual>();
            var result = new AnimationClip[8];
            for (int i=0;i<8;i++) result[i]=BakeClip(input.clips[i], target);
            fighter.skinnedVisual.walkMotions=result;
        } finally { UnityEngine.Object.DestroyImmediate(instance); }
        EditorUtility.SetDirty(fighter.skinnedVisual);
        EditorSceneManager.MarkSceneDirty(fighter.gameObject.scene);
        EditorSceneManager.SaveScene(fighter.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("FEMALE_WALKS_BAKED 8");
    }
    [MenuItem("FengChenInn/Install Female Combat Strafes")]
    public static void InstallStrafes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit play mode first.");
        if (UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>() == null)
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/ActionLab.unity");
        var fighter = UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>();
        if (fighter == null || fighter.skinnedVisual == null) throw new InvalidOperationException("Open ActionLab first.");
        var rig = fighter.skinnedVisual;
        if (rig.walkMotions == null || rig.walkMotions.Length != 8) throw new InvalidOperationException("Existing walks missing.");
        var data = JsonUtility.FromJson<Pack>(File.ReadAllText(Folder + "StrafeSamples.json"));
        if (data?.clips == null || data.clips.Length != 2) throw new InvalidDataException("Expected left and right strafe.");
        var preview = UnityEngine.Object.Instantiate(rig.gameObject);
        try
        {
            preview.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var target = preview.GetComponent<RocketboxVisual>();
            foreach (var motion in data.clips)
            {
                int index = motion.name == "Right" ? 2 : motion.name == "Left" ? 6 : -1;
                if (index < 0) throw new InvalidDataException("Unknown strafe direction.");
                motion.name = "CombatStrafe" + motion.name;
                rig.walkMotions[index] = BakeClip(motion, target);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(preview); }
        EditorUtility.SetDirty(rig);
        EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        EditorSceneManager.SaveScene(rig.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("FEMALE_COMBAT_STRAFES_INSTALLED");
    }
    [MenuItem("FengChenInn/Restore Original Forward Walk")]
    public static void RestoreOriginalForwardWalk()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        if (UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>() == null)
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/ActionLab.unity");
        var fighter = UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>();
        if (fighter == null || fighter.skinnedVisual == null) throw new InvalidOperationException("Character missing.");
        var rig = fighter.skinnedVisual;
        if (rig.walkMotions == null || rig.walkMotions.Length == 0) throw new InvalidOperationException("Forward walk slot missing.");
        var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "WalkForward.anim");
        if (walk == null) throw new FileNotFoundException("Original forward walk missing.");
        rig.walkMotions[0] = walk;
        EditorUtility.SetDirty(rig);
        EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        EditorSceneManager.SaveScene(rig.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("ORIGINAL_FORWARD_WALK_RESTORED");
    }
    [MenuItem("FengChenInn/Install Reference Forward Run")]
    public static void InstallReferenceRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit play mode first.");
        if (UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>() == null)
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/ActionLab.unity");
        var fighter = UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>();
        if (fighter == null || fighter.skinnedVisual == null) throw new InvalidOperationException("Character missing.");
        var rig = fighter.skinnedVisual;
        if (rig.walkMotions == null || rig.walkMotions.Length != 8) throw new InvalidOperationException("Walk array missing.");
        const string folder = "Assets/_Game/Art/Animations/Quaternius/";
        var pack = JsonUtility.FromJson<Pack>(File.ReadAllText(folder + "ReferenceRunSamples.json"));
        if (pack?.clips == null || pack.clips.Length != 1) throw new InvalidDataException("Run source missing.");
        var instance = UnityEngine.Object.Instantiate(rig.gameObject);
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rig.walkMotions[0] = BakeClip(pack.clips[0], instance.GetComponent<RocketboxVisual>(), folder);
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
        EditorUtility.SetDirty(rig);
        EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        EditorSceneManager.SaveScene(rig.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("REFERENCE_FORWARD_RUN_INSTALLED");
    }
    static AnimationClip BakeClip(Motion motion, RocketboxVisual target, string folder = Folder)
    {
        Transform[] b=target.GetWalkBones();
        Transform lt=b[2].Find("Bip01 L Toe0"), rt=b[5].Find("Bip01 R Toe0");
        if (lt == null || rt == null) throw new InvalidOperationException("Foot toes missing.");
        Quaternion[] initial=new Quaternion[6], previous=new Quaternion[6];
        AnimationCurve[,] curves=new AnimationCurve[6,4];
        for(int j=0;j<6;j++){ initial[j]=b[j].localRotation; for(int k=0;k<4;k++) curves[j,k]=new AnimationCurve(); }
        for(int i=0;i<motion.frames.Length;i++){
            for(int j=0;j<6;j++) b[j].localRotation=initial[j];
            Pose p=motion.frames[i];
            Aim(b[0],b[1],p.lt); Aim(b[1],b[2],p.ls); Aim(b[2],lt,p.lf);
            Aim(b[3],b[4],p.rt); Aim(b[4],b[5],p.rs); Aim(b[5],rt,p.rf);
            float t=motion.length*i/(motion.frames.Length-1);
            for(int j=0;j<6;j++){
                Quaternion q=b[j].localRotation;
                if(i>0 && Quaternion.Dot(q,previous[j])<0) q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
                previous[j]=q;
                curves[j,0].AddKey(t,q.x); curves[j,1].AddKey(t,q.y); curves[j,2].AddKey(t,q.z); curves[j,3].AddKey(t,q.w);
            }
        }
        for(int j=0;j<6;j++) b[j].localRotation=initial[j];
        string path=folder+"Walk"+motion.name+".anim";
        AnimationClip clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,path);}
        clip.ClearCurves(); clip.frameRate=48;
        string[] component={"x","y","z","w"};
        for(int j=0;j<6;j++) for(int k=0;k<4;k++)
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(b[j],target.transform),typeof(Transform),"m_LocalRotation."+component[k]),curves[j,k]);
        clip.EnsureQuaternionContinuity(); EditorUtility.SetDirty(clip); return clip;
    }
    static void Aim(Transform parent, Transform child, Vector3 direction){ parent.rotation=Quaternion.FromToRotation(child.position-parent.position,direction)*parent.rotation; }
}




