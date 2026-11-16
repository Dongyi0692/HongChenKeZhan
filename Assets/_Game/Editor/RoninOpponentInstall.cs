using System;
using System.IO;
using System.Linq;
using FengChenInn;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RoninOpponentInstall
{
    const string Folder = "Assets/_Game/Art/Characters/Ronin/";
    const string ScenePath = "Assets/_Game/Scenes/InnDuel.unity";

    public static void InstallAndBuild()
    {
        AssetDatabase.Refresh();
        string characterPath = Folder + "Ronin.fbx";
        string swordPath = Folder + "RoninSword.fbx";
        var importer = AssetImporter.GetAtPath(characterPath) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Ronin FBX failed to import.");
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = false;
        importer.optimizeGameObjects = false;
        importer.SaveAndReimport();
        var character = AssetDatabase.LoadAssetAtPath<GameObject>(characterPath);
        var swordAsset = AssetDatabase.LoadAssetAtPath<GameObject>(swordPath);
        if (character == null || swordAsset == null) throw new InvalidOperationException("Missing separate character or sword asset.");
        var wrapper = new GameObject("RoninOpponent");
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(character, wrapper.transform);
            Transform Bone(string suffix) => model.GetComponentsInChildren<Transform>(true)
                .SingleOrDefault(t => t.name == "Bip01 " + suffix)
                ?? throw new InvalidOperationException("Missing rig bone: " + suffix);
            var mesh = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (mesh == null || mesh.bones.Length < 15) throw new InvalidOperationException("FBX has no usable skin.");
            int boneCount = mesh.bones.Length;
            var foot = Bone("R Foot");
            var toe = Bone("R Toe0");
            Vector3 facing = Vector3.ProjectOnPlane(wrapper.transform.InverseTransformDirection(toe.position - foot.position), Vector3.up);
            if (facing.sqrMagnitude > 0.00001f)
                model.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(facing.normalized, Vector3.up)) * model.transform.localRotation;
            float height = mesh.bounds.size.y;
            if (height < 0.001f) throw new InvalidOperationException("Invalid character size.");
            float scale = 1.72f / height;
            model.transform.localScale *= scale;
            model.transform.position += wrapper.transform.up * (wrapper.transform.position.y - mesh.bounds.min.y);
            mesh.updateWhenOffscreen = true;
            var sword = (GameObject)PrefabUtility.InstantiatePrefab(swordAsset, wrapper.transform);
            sword.name = "IndependentSword";
            sword.transform.localScale *= scale;
            var visual = wrapper.AddComponent<RocketboxVisual>();
            visual.pelvis = Bone("Pelvis"); visual.chest = Bone("Spine2");
            visual.leftArm = Limb(Bone("L UpperArm"), Bone("L Forearm"), Bone("L Hand"));
            visual.rightArm = Limb(Bone("R UpperArm"), Bone("R Forearm"), Bone("R Hand"));
            visual.leftLeg = Limb(Bone("L Thigh"), Bone("L Calf"), Bone("L Foot"));
            visual.rightLeg = Limb(Bone("R Thigh"), Bone("R Calf"), foot);
            visual.weapon = sword.transform; visual.keepSwordDrawn = true;
            var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, Folder + "RoninOpponent.prefab");
            if (prefab == null) throw new InvalidOperationException("Failed to save opponent prefab.");
            EditorSceneManager.OpenScene(ScenePath);
            var encounter = UnityEngine.Object.FindFirstObjectByType<DuelEncounter>();
            if (encounter == null) throw new InvalidOperationException("DuelEncounter missing from scene.");
            encounter.opponentVisualPrefab = prefab.GetComponent<RocketboxVisual>();
            EditorUtility.SetDirty(encounter);
            EditorSceneManager.MarkSceneDirty(encounter.gameObject.scene);
            if (!EditorSceneManager.SaveOpenScenes()) throw new IOException("Could not save scene.");
            AssetDatabase.SaveAssets();
            var target = "Builds/RoninEnemy/FengChenInn.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            BuildReport report = BuildPipeline.BuildPlayer(new[] { ScenePath }, target, BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Build failed: " + report.summary.result);
            Debug.Log("RONIN_INSTALL_BUILD_OK bones=" + boneCount + " bodyHeight=" + height + " scale=" + scale);
        }
        finally { UnityEngine.Object.DestroyImmediate(wrapper); }
    }

    static RocketboxVisual.Limb Limb(Transform upper, Transform lower, Transform end) =>
        new RocketboxVisual.Limb { upper = upper, lower = lower, end = end };
}
