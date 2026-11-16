using FengChenInn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

internal static class Stage11WorldSetup
{
    private const string ScenePath = "Assets/_Game/Scenes/InnDuel_Stage11.unity";
    private const string RainMaterialPath = "Assets/_Game/Art/Stage11Final/Stage11_Rain.mat";
    private const string SetupName = "Stage11 - World, Rain and Night";

    private static readonly string[] GroundNames =
    {
        "EnvGround_North", "EnvGround_South", "EnvGround_East", "EnvGround_West"
    };

    [MenuItem("Tools/风尘客栈/设置 Stage11 全场景移动与雨夜")]
    private static void ApplyFromMenu()
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            Debug.LogError("请先打开 " + ScenePath + "，然后再运行雨夜场景设置。");
            return;
        }

        if (Apply())
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("Stage11 室外地面、空气墙、雨和夜景已设置并保存。");
        }
    }

    internal static bool Apply()
    {
        GameObject model = GameObject.Find("Stage11 Final - Visuals");
        if (model == null)
        {
            Debug.LogError("找不到 Stage11 Final - Visuals；未修改场景。");
            return false;
        }

        var grounds = new BoxCollider[GroundNames.Length];
        var children = model.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < GroundNames.Length; i++)
        {
            Transform ground = null;
            foreach (Transform child in children)
                if (child.name == GroundNames[i]) { ground = child; break; }
            if (ground == null || !ground.TryGetComponent(out MeshFilter mesh) || mesh.sharedMesh == null)
            {
                Debug.LogError("终版模型缺少地面：" + GroundNames[i]);
                return false;
            }

            var collider = ground.GetComponent<BoxCollider>();
            if (collider == null) collider = ground.gameObject.AddComponent<BoxCollider>();
            collider.center = mesh.sharedMesh.bounds.center;
            collider.size = mesh.sharedMesh.bounds.size;
            collider.isTrigger = false;
            collider.enabled = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            grounds[i] = collider;
            Debug.Log($"{GroundNames[i]}: {collider.bounds.min} to {collider.bounds.max}");
        }

        var allGround = grounds[0].bounds;
        for (int i = 1; i < grounds.Length; i++) allGround.Encapsulate(grounds[i].bounds);

        var setup = GetOrCreate(null, SetupName);
        var walls = GetOrCreate(setup.transform, "Invisible boundary");
        const float thickness = 1.2f;
        float centerY = allGround.center.y + 4f;
        float height = 12f;
        SetWall(walls.transform, "West", new Vector3(allGround.min.x, centerY, allGround.center.z),
            new Vector3(thickness, height, allGround.size.z + thickness * 2f));
        SetWall(walls.transform, "East", new Vector3(allGround.max.x, centerY, allGround.center.z),
            new Vector3(thickness, height, allGround.size.z + thickness * 2f));
        SetWall(walls.transform, "South", new Vector3(allGround.center.x, centerY, allGround.min.z),
            new Vector3(allGround.size.x + thickness * 2f, height, thickness));
        SetWall(walls.transform, "North", new Vector3(allGround.center.x, centerY, allGround.max.z),
            new Vector3(allGround.size.x + thickness * 2f, height, thickness));

        ApplyNight(model, setup.transform, children);
        ApplyRain(setup.transform, grounds);
        Debug.Log($"Stage11 ground bounds: {allGround.min} to {allGround.max}");
        return true;
    }

    private static GameObject GetOrCreate(Transform parent, string name)
    {
        Transform existing = parent != null ? parent.Find(name) : GameObject.Find(name)?.transform;
        if (existing != null) return existing.gameObject;
        var created = new GameObject(name);
        if (parent != null) created.transform.SetParent(parent, false);
        return created;
    }

    private static void SetWall(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var wall = GetOrCreate(parent, name);
        wall.transform.position = center;
        wall.transform.rotation = Quaternion.identity;
        var collider = wall.GetComponent<BoxCollider>();
        if (collider == null) collider = wall.AddComponent<BoxCollider>();
        collider.center = Vector3.zero;
        collider.size = size;
        collider.isTrigger = false;
        collider.enabled = true;
    }

    private static void ApplyNight(GameObject model, Transform setup, Transform[] modelChildren)
    {
        var camera = Camera.main;
        if (camera != null)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.012f, .023f, .052f);
            EditorUtility.SetDirty(camera);
        }

        var sunObject = GameObject.Find("Warm Sun") ?? GameObject.Find("Moonlight - Cool Blue");
        var moon = sunObject != null ? sunObject.GetComponent<Light>() : null;
        if (moon != null)
        {
            sunObject.name = "Moonlight - Cool Blue";
            moon.color = new Color(.48f, .61f, .9f);
            moon.intensity = .58f;
            moon.shadows = LightShadows.Soft;
            EditorUtility.SetDirty(moon);
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.12f, .17f, .28f);
        RenderSettings.ambientEquatorColor = new Color(.055f, .08f, .15f);
        RenderSettings.ambientGroundColor = new Color(.023f, .033f, .066f);
        RenderSettings.ambientIntensity = .38f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(.025f, .04f, .083f);
        RenderSettings.fogDensity = .012f;
        RenderSettings.reflectionIntensity = .45f;

        var lanternLights = GetOrCreate(setup, "Front lantern lights");
        foreach (Transform child in modelChildren)
        {
            if (!child.name.StartsWith("Existing_lantern_front_") || !child.name.Contains("_root")) continue;
            var lamp = GetOrCreate(lanternLights.transform, child.name + " light");
            lamp.transform.position = child.position + Vector3.down * .25f;
            var light = lamp.GetComponent<Light>();
            if (light == null) light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, .47f, .16f);
            light.intensity = 2.4f;
            light.range = 6f;
            light.shadows = LightShadows.None;
        }
    }

    private static void ApplyRain(Transform setup, BoxCollider[] grounds)
    {
        var rainRoot = GetOrCreate(setup, "Outdoor rain");
        var material = AssetDatabase.LoadAssetAtPath<Material>(RainMaterialPath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogError("找不到 URP 粒子 Shader，雨粒材质未创建。");
                return;
            }
            material = new Material(shader) { name = "Stage11 Rain" };
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(.69f, .79f, 1f, .46f));
            AssetDatabase.CreateAsset(material, RainMaterialPath);
        }

        for (int i = 0; i < grounds.Length; i++)
        {
            Bounds bounds = grounds[i].bounds;
            var zone = GetOrCreate(rainRoot.transform, "Rain - " + GroundNames[i]);
            zone.transform.position = new Vector3(bounds.center.x, 8.5f, bounds.center.z);
            zone.transform.rotation = Quaternion.identity;
            var particles = zone.GetComponent<ParticleSystem>();
            if (particles == null) particles = zone.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 2f;
            main.startLifetime = .68f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(.012f, .020f);
            main.startColor = Color.white;
            main.maxParticles = Mathf.CeilToInt(bounds.size.x * bounds.size.z * .55f) + 100;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = bounds.size.x * bounds.size.z * .55f;

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(bounds.size.x, .05f, bounds.size.z);

            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-.7f);
            velocity.y = new ParticleSystem.MinMaxCurve(-13f);
            velocity.z = new ParticleSystem.MinMaxCurve(-.35f);

            var renderer = zone.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = .35f;
            renderer.lengthScale = .9f;
            renderer.material = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
