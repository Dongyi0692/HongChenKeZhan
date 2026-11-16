using System;
using System.Linq;
using FengChenInn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class RocketboxSetup
{
    private const string Art = "Assets/_Game/Art/Characters/Rocketbox/";

    [MenuItem("FengChenInn/Install Female Placeholder")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var fighter = UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>();
        if (fighter == null || fighter.visualRoot == null) throw new InvalidOperationException("Open ActionLab first.");
        if (fighter.skinnedVisual != null) return;

        string modelPath = Art + "Female_Adult_07.fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        importer.importAnimation = false;
        importer.optimizeGameObjects = false;
        importer.SaveAndReimport();

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var wrapper = new GameObject("FemalePlaceholder");
        wrapper.transform.SetParent(fighter.transform, false);
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, wrapper.transform);
            Transform Bone(string suffix)
            {
                string name = "Bip01 " + suffix;
                return model.GetComponentsInChildren<Transform>(true).SingleOrDefault(bone => bone.name == name)
                    ?? throw new InvalidOperationException("Missing bone: " + name);
            }

            var renderer = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (renderer == null || renderer.bones.Length < 20)
                throw new InvalidOperationException("The FBX did not import as a skinned model.");
            Transform foot = Bone("R Foot");
            Transform toe = Bone("R Toe0");
            Vector3 forward = Vector3.ProjectOnPlane(wrapper.transform.InverseTransformDirection(toe.position - foot.position), Vector3.up);
            if (forward.sqrMagnitude > 0.00001f)
                model.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(forward.normalized, Vector3.up))
                    * model.transform.localRotation;
            Bounds originalBounds = renderer.bounds;
            if (originalBounds.size.y < 0.001f) throw new InvalidOperationException("Model bounds are invalid.");
            model.transform.localScale *= 1.72f / originalBounds.size.y;
            model.transform.position += wrapper.transform.up * (wrapper.transform.position.y - renderer.bounds.min.y);

            var body = MakeMaterial("JacketAndJeans", "f007_body_color", "f007_body_normal");
            var head = MakeMaterial("Face", "f007_head_color", "f007_head_normal");
            var hair = MakeMaterial("HairAndLashes", "f007_opacity_color", null, true);
            renderer.sharedMaterials = renderer.sharedMaterials.Select(material => material.name.Contains("opacity") ? hair :
                material.name.Contains("head") ? head : body).ToArray();
            renderer.updateWhenOffscreen = true;

            var visual = wrapper.AddComponent<RocketboxVisual>();
            visual.pelvis = Bone("Pelvis");
            visual.chest = Bone("Spine2");
            visual.leftArm = MakeLimb(Bone("L UpperArm"), Bone("L Forearm"), Bone("L Hand"));
            visual.rightArm = MakeLimb(Bone("R UpperArm"), Bone("R Forearm"), Bone("R Hand"));
            visual.leftLeg = MakeLimb(Bone("L Thigh"), Bone("L Calf"), Bone("L Foot"));
            visual.rightLeg = MakeLimb(Bone("R Thigh"), Bone("R Calf"), foot);
            var previousSword = fighter.rightArm.Find("SwordSocket");
            if (previousSword == null) throw new InvalidOperationException("Missing existing placeholder sword.");
            var sword = UnityEngine.Object.Instantiate(previousSword.gameObject, wrapper.transform, false);
            sword.name = "PracticeSword";
            visual.weapon = sword.transform;
            visual.weapon.SetPositionAndRotation(visual.rightArm.end.position, wrapper.transform.rotation);

            fighter.skinnedVisual = visual;
            fighter.visualRoot.gameObject.SetActive(false);
            EditorUtility.SetDirty(fighter);
            EditorSceneManager.MarkSceneDirty(fighter.gameObject.scene);
            EditorSceneManager.SaveScene(fighter.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("FEMALE_PLACEHOLDER_INSTALLED | Bones: " + renderer.bones.Length);
        }
        catch
        {
            fighter.skinnedVisual = null;
            fighter.visualRoot.gameObject.SetActive(true);
            UnityEngine.Object.DestroyImmediate(wrapper);
            throw;
        }
    }

    private static RocketboxVisual.Limb MakeLimb(Transform upper, Transform lower, Transform end)
    {
        return new RocketboxVisual.Limb { upper = upper, lower = lower, end = end };
    }

    private static Material MakeMaterial(string name, string color, string normal, bool cutout = false)
    {
        string path = Art + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(GraphicsSettings.currentRenderPipeline.defaultMaterial);
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = Color.white;
        material.SetFloat("_Smoothness", 0.26f);
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + color + ".tga"));
        if (normal != null)
        {
            string normalPath = Art + normal + ".tga";
            var importer = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            if (importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            material.EnableKeyword("_NORMALMAP");
        }
        if (cutout)
        {
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.45f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }
        EditorUtility.SetDirty(material);
        return material;
    }
}
