using System;
using System.IO;
using FengChenInn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class OpenSwordMotionBaker
{
    private const string MotionFolder = "Assets/_Game/Art/Animations/Quaternius/";

    [Serializable]
    private sealed class SamplePack { public SampleClip[] clips; }

    [Serializable]
    private sealed class SampleClip
    {
        public string name;
        public float length;
        public SamplePose[] frames;
    }

    [Serializable]
    private sealed class SamplePose
    {
        public Vector3 chestRight, chestUp, leftUpper, leftForearm, rightUpper, rightForearm, blade;
    }

    [MenuItem("FengChenInn/Bake CC0 Sword Motions")]
    public static void Bake()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var fighter = UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>();
        if (fighter == null || fighter.skinnedVisual == null)
            throw new InvalidOperationException("Open ActionLab with the female placeholder installed.");

        var input = JsonUtility.FromJson<SamplePack>(File.ReadAllText(MotionFolder + "SwordSamples.json"));
        if (input?.clips == null || input.clips.Length != 3) throw new InvalidDataException("Expected three sword motions.");
        RocketboxVisual rig = fighter.skinnedVisual;
        var preview = UnityEngine.Object.Instantiate(rig.gameObject);
        try
        {
            preview.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var target = preview.GetComponent<RocketboxVisual>();
            rig.lightSwordMotion = BakeClip(input.clips[0], target);
            rig.heavySwordMotion = BakeClip(input.clips[1], target);
            rig.swordGuardMotion = BakeClip(input.clips[2], target);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(preview);
        }
        EditorUtility.SetDirty(rig);
        EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        EditorSceneManager.SaveScene(rig.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("CC0_SWORD_MOTIONS_BAKED");
    }

    private static AnimationClip BakeClip(SampleClip motion, RocketboxVisual target)
    {
        if (motion.frames.Length < 2 || motion.length <= 0f) throw new InvalidDataException("Incomplete motion " + motion.name);
        Transform[] bones = target.GetSwordMotionBones();
        var bindRotations = new Quaternion[bones.Length];
        var previous = new Quaternion[bones.Length];
        var curves = new AnimationCurve[bones.Length, 4];
        for (int bone = 0; bone < bones.Length; bone++)
        {
            bindRotations[bone] = bones[bone].localRotation;
            for (int axis = 0; axis < 4; axis++) curves[bone, axis] = new AnimationCurve();
        }
        Vector3 shoulders = (target.leftArm.upper.position + target.rightArm.upper.position) * 0.5f;
        Quaternion baseChest = target.chest.rotation;
        Quaternion restFrame = TorsoBasis(target.rightArm.upper.position - target.leftArm.upper.position, shoulders - target.chest.parent.parent.position);
        Quaternion chestFromFrame = Quaternion.Inverse(restFrame) * baseChest;

        for (int frame = 0; frame < motion.frames.Length; frame++)
        {
            for (int bone = 0; bone < bones.Length; bone++) bones[bone].localRotation = bindRotations[bone];
            SamplePose pose = motion.frames[frame];
            Quaternion authoredChest = TorsoBasis(pose.chestRight, pose.chestUp) * chestFromFrame;
            target.chest.rotation = Quaternion.RotateTowards(baseChest, authoredChest, 50f);
            Aim(target.leftArm.upper, target.leftArm.lower, pose.leftUpper);
            Aim(target.leftArm.lower, target.leftArm.end, pose.leftForearm);
            Aim(target.rightArm.upper, target.rightArm.lower, pose.rightUpper);
            Aim(target.rightArm.lower, target.rightArm.end, pose.rightForearm);
            target.weapon.rotation = Quaternion.LookRotation(pose.blade, Vector3.up);
            float time = motion.length * frame / (motion.frames.Length - 1);
            for (int bone = 0; bone < bones.Length; bone++)
            {
                Quaternion value = bones[bone].localRotation;
                if (frame > 0 && Quaternion.Dot(value, previous[bone]) < 0f)
                    value = new Quaternion(-value.x, -value.y, -value.z, -value.w);
                previous[bone] = value;
                curves[bone, 0].AddKey(time, value.x);
                curves[bone, 1].AddKey(time, value.y);
                curves[bone, 2].AddKey(time, value.z);
                curves[bone, 3].AddKey(time, value.w);
            }
        }
        for (int bone = 0; bone < bones.Length; bone++) bones[bone].localRotation = bindRotations[bone];

        string path = MotionFolder + motion.name + ".anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        clip.frameRate = 60f;
        string[] components = { "x", "y", "z", "w" };
        for (int bone = 0; bone < bones.Length; bone++)
        {
            string hierarchyPath = AnimationUtility.CalculateTransformPath(bones[bone], target.transform);
            for (int axis = 0; axis < 4; axis++)
            {
                var binding = EditorCurveBinding.FloatCurve(hierarchyPath, typeof(Transform), "m_LocalRotation." + components[axis]);
                AnimationUtility.SetEditorCurve(clip, binding, curves[bone, axis]);
            }
        }
        clip.EnsureQuaternionContinuity();
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static Quaternion TorsoBasis(Vector3 right, Vector3 up) => Quaternion.LookRotation(Vector3.Cross(right, up), up);

    private static void Aim(Transform joint, Transform child, Vector3 direction)
    {
        joint.rotation = Quaternion.FromToRotation(child.position - joint.position, direction) * joint.rotation;
    }
}
