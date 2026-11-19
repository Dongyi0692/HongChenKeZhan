using System.IO;
using FengChenInn;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public static class PrototypeSetup
{
    private const string Root = "Assets/_Game";
    [MenuItem("FengChenInn/Import Fonts")]
    public static void ImportFonts() { TMP_PackageResourceImporter.ImportResources(true, false, false); }
    [MenuItem("FengChenInn/Create Action Lab")]
    public static void CreateScene()
    {
        Directory.CreateDirectory(Root + "/Scenes");
        Directory.CreateDirectory(Root + "/Art/Materials");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var jade = MakeMaterial("Jade", new Color(0.10f, 0.36f, 0.36f));
        var gold = MakeMaterial("Brass", new Color(0.72f, 0.46f, 0.20f));
        var wood = MakeMaterial("Wood", new Color(0.23f, 0.15f, 0.11f));
        var floor = MakeMaterial("Stone", new Color(0.38f, 0.38f, 0.34f));
        var skin = MakeMaterial("Mannequin", new Color(0.78f, 0.70f, 0.54f));
        var steel = MakeMaterial("Steel", new Color(0.74f, 0.82f, 0.83f));
        Part("Floor", null, PrimitiveType.Cube, new Vector3(0,-0.2f,0), new Vector3(18,0.4f,18), floor, true);
        for (int i = -8; i <= 8; i += 2)
        {
            Part("FloorInlayX", null, PrimitiveType.Cube, new Vector3(i,0.003f,0), new Vector3(0.025f,0.005f,18), wood);
            Part("FloorInlayZ", null, PrimitiveType.Cube, new Vector3(0,0.003f,i), new Vector3(18,0.005f,0.025f), wood);
        }
        Part("BackWall", null, PrimitiveType.Cube, new Vector3(0,1.5f,9), new Vector3(18,3,0.4f), wood, true);
        Part("LeftWall", null, PrimitiveType.Cube, new Vector3(-9,1.5f,0), new Vector3(0.4f,3,18), wood, true);
        Part("RightWall", null, PrimitiveType.Cube, new Vector3(9,1.5f,0), new Vector3(0.4f,3,18), wood, true);
        Part("FrontBoundary", null, PrimitiveType.Cube, new Vector3(0,0.4f,-9), new Vector3(18,0.8f,0.4f), wood, true);
        Part("JumpBlock", null, PrimitiveType.Cube, new Vector3(3,0.25f,1), new Vector3(1.6f,0.5f,1.2f), gold, true);
        Part("CrouchBeam", null, PrimitiveType.Cube, new Vector3(-3,1.35f,1), new Vector3(2.4f,0.3f,1.5f), wood, true);
        for (int side = -1; side <= 1; side += 2)
            Part("BeamPost", null, PrimitiveType.Cube, new Vector3(-3+side*1.4f,0.75f,1), new Vector3(0.25f,1.5f,1.5f), wood, true);
        var player = new GameObject("Player");
        player.transform.position = new Vector3(0,0.05f,-2);
        var controller = player.AddComponent<CharacterController>();
        controller.height = 1.8f; controller.center = Vector3.up*0.9f; controller.radius = 0.3f;
        controller.stepOffset = 0.25f; controller.skinWidth = 0.03f;
        var fighter = player.AddComponent<PrototypeFighter>();
        var visual = new GameObject("PlaceholderVisual").transform;
        visual.SetParent(player.transform, false); fighter.visualRoot = visual;
        Part("Torso", visual, PrimitiveType.Capsule, new Vector3(0,1.15f,0), new Vector3(0.55f,0.38f,0.34f), jade);
        Part("Belt", visual, PrimitiveType.Cube, new Vector3(0,0.92f,0), new Vector3(0.58f,0.12f,0.36f), gold);
        Part("Head", visual, PrimitiveType.Sphere, new Vector3(0,1.66f,0), new Vector3(0.35f,0.40f,0.35f), skin);
        Part("FaceMarker", visual, PrimitiveType.Cube, new Vector3(0,1.66f,0.17f), new Vector3(0.20f,0.06f,0.04f), wood);
        fighter.leftArm = Limb("LeftArm", visual, new Vector3(-0.36f,1.42f,0), new Vector3(0.17f,0.5f,0.17f), skin);
        fighter.rightArm = Limb("RightArm", visual, new Vector3(0.36f,1.42f,0), new Vector3(0.17f,0.5f,0.17f), skin);
        fighter.leftLeg = Limb("LeftLeg", visual, new Vector3(-0.17f,0.85f,0), new Vector3(0.21f,0.76f,0.23f), jade);
        fighter.rightLeg = Limb("RightLeg", visual, new Vector3(0.17f,0.85f,0), new Vector3(0.21f,0.76f,0.23f), jade);
        var sword = new GameObject("SwordSocket").transform;
        sword.SetParent(fighter.rightArm, false); sword.localPosition = new Vector3(0,-0.49f,0);
        Part("Grip", sword, PrimitiveType.Cube, new Vector3(0,0,0.09f), new Vector3(0.07f,0.07f,0.22f), wood);
        Part("Guard", sword, PrimitiveType.Cube, new Vector3(0,0,0.20f), new Vector3(0.30f,0.06f,0.06f), gold);
        Part("Blade", sword, PrimitiveType.Cube, new Vector3(0,0,0.68f), new Vector3(0.085f,0.035f,0.9f), steel);
        player.AddComponent<PrototypeInput>();
        var camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.tag = "MainCamera"; camera.fieldOfView = 55; camera.nearClipPlane = 0.08f;
        camera.backgroundColor = new Color(0.13f,0.18f,0.22f);
        camera.gameObject.AddComponent<AudioListener>();
        camera.gameObject.AddComponent<OrbitCamera>().target = player.transform;
        camera.transform.SetPositionAndRotation(new Vector3(0,3.23f,-6.56f), Quaternion.Euler(24,0,0));
        var light = new GameObject("Warm Sun").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.7f; light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(45,-30,0);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.50f,0.53f,0.59f);
        MakeHud(fighter);
        PlayerSettings.companyName = "FengChen Team"; PlayerSettings.productName = "FengChen Inn - Action Lab";
        PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        Application.runInBackground = true;
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), Root + "/Scenes/ActionLab.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Root + "/Scenes/ActionLab.unity", true) };
        AssetDatabase.SaveAssets();
        Debug.Log("ACTION_LAB_CREATED");
    }

    private static Transform Limb(string name, Transform parent, Vector3 joint, Vector3 size, Material material)
    {
        var pivot = new GameObject(name).transform; pivot.SetParent(parent,false); pivot.localPosition = joint;
        Part("Mesh", pivot, PrimitiveType.Capsule, Vector3.down*size.y*0.5f, new Vector3(size.x,size.y*0.5f,size.z), material);
        return pivot;
    }
    private static GameObject Part(string name, Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Material material, bool solid = false)
    {
        var obj = GameObject.CreatePrimitive(type); obj.name = name;
        obj.transform.SetParent(parent,false); obj.transform.localPosition = position; obj.transform.localScale = scale;
        obj.GetComponent<Renderer>().sharedMaterial = material;
        if (!solid) Object.DestroyImmediate(obj.GetComponent<Collider>());
        return obj;
    }
    private static Material MakeMaterial(string name, Color color)
    {
        string path = Root + "/Art/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(GraphicsSettings.currentRenderPipeline.defaultMaterial);
            AssetDatabase.CreateAsset(material,path);
        }
        material.color = color; EditorUtility.SetDirty(material); return material;
    }
    private static void MakeHud(PrototypeFighter fighter)
    {
        var canvas = new GameObject("Action HUD", typeof(Canvas), typeof(CanvasScaler));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280,720); scaler.matchWidthOrHeight = 0.5f;
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        if (font == null) throw new System.InvalidOperationException("Import TMP Essential Resources before creating Action Lab.");
        var state = HudText(canvas.transform,font,"State",new Vector2(0.02f,0.83f),new Vector2(0.6f,0.98f),22);
        var controls = HudText(canvas.transform,font,"Controls",new Vector2(0.02f,0.03f),new Vector2(0.98f,0.18f),19);
        controls.text = "WASD  Strafe    MOUSE  Turn    SPACE  Jump    L-CTRL  Hold to crouch\nLMB  Light    RMB  Heavy    Q  Hold to guard    L-SHIFT  Step / backstep    R  Reset";
        var hud = canvas.AddComponent<ActionHud>(); hud.fighter = fighter; hud.statusText = state;
    }
    private static TMP_Text HudText(Transform parent, TMP_FontAsset font, string name, Vector2 min, Vector2 max, int size)
    {
        var obj = new GameObject(name,typeof(RectTransform)); obj.transform.SetParent(parent,false);
        var rect = obj.GetComponent<RectTransform>(); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var text = obj.AddComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = size;
        text.color = new Color(1f,0.94f,0.77f); text.raycastTarget = false; return text;
    }

    public static void BuildWindows()
    {
        Directory.CreateDirectory("Builds/Windows");
        var report = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, "Builds/Windows/FengChenInn.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception("Windows build failed: " + report.summary.result);
        Debug.Log("ACTION_LAB_BUILD_OK");
    }
}
