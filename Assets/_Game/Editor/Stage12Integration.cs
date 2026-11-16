using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FengChenInn;

public static class Stage12Integration
{
    const string ScenePath = "Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity";
    [Unity.Pipeline.Commands.CliCommand("innstage12", "package", "Install teammate scene importer without replacing project packages")]
    public static string Package()
    {
        var request = UnityEditor.PackageManager.Client.Add("com.unity.cloud.gltfast@6.20.0");
        void Poll()
        {
            if (!request.IsCompleted) return;
            EditorApplication.update -= Poll;
            Directory.CreateDirectory("Logs/Stage12Integration");
            File.WriteAllText("Logs/Stage12Integration/package.txt", request.Status + " " + request.Error?.message);
        }
        EditorApplication.update += Poll;
        return "Package installation started";
    }
    [Serializable] public class Item { public string name, path; public Vector3 position, min, max; public string[] components; }
    [Serializable] public class Report { public string scene; public Item[] items; public string[] roots; }
    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    [Unity.Pipeline.Commands.CliCommand("innstage12", "align_ground", "Align outdoor story anchors to the authored lower courtyard floor")]
    public static string AlignGround()
    {
        if(EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=ScenePath)throw new Exception("Stop in Stage12 first");
        var anchors=UnityEngine.Object.FindFirstObjectByType<InnStoryAnchors>();var duel=UnityEngine.Object.FindFirstObjectByType<DuelEncounter>();
        Physics.SyncTransforms();
        Vector3 Ground(Vector3 p){var hit=Physics.RaycastAll(new Vector3(p.x,1f,p.z),Vector3.down,3f,~0,QueryTriggerInteraction.Ignore).Where(h=>h.normal.y>.7f&&h.collider.GetComponentInParent<CombatActor>()==null).OrderByDescending(h=>h.point.y).First();return new Vector3(p.x,hit.point.y+.04f,p.z);}
        anchors.rearExit.position=Ground(anchors.rearExit.position);
        foreach(var spawn in duel.rescueSpawnAnchors)spawn.position=Ground(spawn.position);
        duel.eliteGuardPosition=Ground(duel.eliteGuardPosition);duel.rescueArrivalPosition=Ground(duel.rescueArrivalPosition);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        return "Outdoor anchors aligned to authored floor; hall height unchanged.";
    }
    [Unity.Pipeline.Commands.CliCommand("innstage12", "finish_art", "Add rear-combat fill light and opened placeholder ledger leaves")]
    public static string FinishArt()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlaying||scene.path!=ScenePath)throw new Exception("Stop in Stage12 first");
        if(!GameObject.Find("Rear passage combat lighting")){
            var lights=new GameObject("Rear passage combat lighting");
            void LightAt(string name,Vector3 pos,Color color,float intensity,float range){var go=new GameObject(name);go.transform.SetParent(lights.transform);go.transform.position=pos;var l=go.AddComponent<Light>();l.type=LightType.Point;l.color=color;l.intensity=intensity;l.range=range;l.shadows=LightShadows.None;}
            LightAt("Rear doorway warm spill",new Vector3(2.1f,2.9f,5.8f),new Color(1f,.72f,.42f),4.5f,9f);
            LightAt("Rear courtyard moon fill",new Vector3(0,4.8f,9f),new Color(.58f,.69f,1f),3.2f,12f);
        }
        if(!GameObject.Find("Opened ledger placeholder")){
            var book=GameObject.Find("Stage11 Final - Visuals").GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Counter_Ledger_Part00").GetComponent<Renderer>().bounds;
            var root=new GameObject("Opened ledger placeholder");root.transform.position=new Vector3(book.center.x,book.max.y+.013f,book.center.z);
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=new Color(.83f,.72f,.49f);material.SetFloat("_Smoothness",.1f);
            const string materialPath="Assets/_Game/Art/Stage11Final/LedgerPlaceholder.mat";
            AssetDatabase.CreateAsset(material,materialPath);
            foreach(int side in new[]{-1,1}){var page=GameObject.CreatePrimitive(PrimitiveType.Cube);page.name=side<0?"Left open page":"Right open page";page.transform.SetParent(root.transform,false);page.transform.localPosition=new Vector3(side*.085f,0,0);page.transform.localScale=new Vector3(.17f,.006f,.25f);page.transform.localRotation=Quaternion.Euler(0,0,-side*5f);UnityEngine.Object.DestroyImmediate(page.GetComponent<Collider>());page.GetComponent<Renderer>().sharedMaterial=material;}
        }
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        return "Authored scene preserved; lit rear combat and opened temporary ledger leaves.";
    }
    [Unity.Pipeline.Commands.CliCommand("innstage12", "apply", "Bind existing Stage12 art to current characters and new story")]
    public static string Apply()
    {
        if (EditorApplication.isPlaying) throw new Exception("Stop play first");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new Exception("Open Stage12 first");
        if (GameObject.Find("Stage12 Story Anchors")) throw new Exception("Already integrated; edit existing bindings");
        Directory.CreateDirectory("Backups/Stage12Integration");
        File.Copy(ScenePath,"Backups/Stage12Integration/Stage12_before_story.unity",false);
        var oldScene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/InnDuel.unity",OpenSceneMode.Additive);
        var original = oldScene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<PrototypeFighter>(true)).Single();
        var oldOrbit = oldScene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<OrbitCamera>(true)).Single();
        var destination = scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<PrototypeFighter>(true)).Single();
        Vector3 spawn=destination.transform.position;
        Quaternion facing=destination.transform.rotation;
        var player=UnityEngine.Object.Instantiate(original.gameObject);
        player.name="Player";
        SceneManager.MoveGameObjectToScene(player,scene);
        player.transform.SetPositionAndRotation(spawn,facing);
        var newFighter=player.GetComponent<PrototypeFighter>();
        newFighter.skillStaminaCost=30f;
        var controller=player.GetComponent<CharacterController>();
        controller.height=1.8f;controller.center=Vector3.up*.9f;controller.radius=.36f;controller.stepOffset=.3f;controller.skinWidth=.04f;controller.slopeLimit=50f;
        var camera=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<OrbitCamera>(true)).Single();
        EditorUtility.CopySerialized(oldOrbit,camera);camera.target=player.transform;
        foreach(var hud in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ActionHud>(true)))hud.fighter=newFighter;
        var entrance=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<InnEntranceDoors>(true)).Single();
        entrance.player=player.transform;
        UnityEngine.Object.DestroyImmediate(destination.gameObject);
        EditorSceneManager.CloseScene(oldScene,true);SceneManager.SetActiveScene(scene);
        foreach(var stairs in UnityEngine.Object.FindObjectsByType<InnStairTraversal>(FindObjectsInactive.Include,FindObjectsSortMode.None))stairs.enabled=false;
        var visuals=GameObject.Find("Stage11 Final - Visuals");
        if(!visuals)throw new Exception("Missing authored Stage11 visual root");
        Transform Find(string name)=>visuals.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
        var holder=new GameObject("Stage12 Story Anchors");
        var anchors=holder.AddComponent<InnStoryAnchors>();
        Transform Anchor(string name, Vector3 pos){var go=new GameObject(name);go.transform.SetParent(holder.transform);go.transform.position=pos;return go.transform;}
        var book=Find("Counter_Ledger_Part00").GetComponent<Renderer>().bounds.center;
        anchors.ledger=Anchor("Ledger interaction",new Vector3(book.x,.25f,book.z));
        anchors.rearDoor=Anchor("Rear door interaction",new Vector3(2.125f,.25f,4.2f));
        anchors.rearExit=Anchor("Rear exit discovery",new Vector3(2.125f,.25f,6.5f));
        anchors.rearExitDistance=.75f;
        var rear=holder.AddComponent<InnRearExitDoor>();rear.hinge=Find("HiddenDoor_Hinge");rear.openAngle=-110f;
        anchors.rearDoorController=rear;
        var duel=player.GetComponent<DuelEncounter>();
        duel.tutorialPosition=new Vector3(2.7f,.25f,2.8f);
        duel.eliteArrivalPosition=new Vector3(.65f,.25f,-3.4f);
        duel.eliteGuardPosition=new Vector3(.3f,.25f,7.4f);
        duel.rescueArrivalPosition=new Vector3(0,.25f,8.5f);
        duel.RescueEnemyCount=5;duel.eliteAllyMaxHealth=600;
        var positions=new[]{new Vector3(-1.4f,.25f,7),new Vector3(-1.2f,.25f,8.7f),new Vector3(.3f,.25f,9.2f),new Vector3(1.6f,.25f,8.8f),new Vector3(2.4f,.25f,7.8f)};
        duel.rescueSpawnAnchors=positions.Select((p,i)=>Anchor("Rescue soldier "+(i+1),p)).ToArray();
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
        return "Integrated current player and combat prefabs, teammate environment/menu, ledger/rear door and five rescue anchors. Requires runtime checks.";
    }
    [Unity.Pipeline.Commands.CliCommand("innstage12", "inspect", "Open existing Stage12 and inspect authored story objects")]
    public static string Inspect()
    {
        if (EditorApplication.isPlaying) throw new Exception("Stop play before scene inspection");
        var current = SceneManager.GetActiveScene();
        if (current.path != ScenePath && current.isDirty) throw new Exception("Current scene has unsaved edits");
        var scene = current.path == ScenePath ? current : EditorSceneManager.OpenScene(ScenePath);
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true));
        var report = new Report { scene = scene.path, roots = scene.GetRootGameObjects().Select(r=>r.name).ToArray(),
            items = all.Where(t => t.name.Contains("HiddenDoor") || t.name.Contains("Ledger") || t.name.Contains("Counter") || t.name.Contains("Yard") || t.name.Contains("Courtyard") || t.GetComponent<PrototypeFighter>() || t.GetComponent<OrbitCamera>() || t.GetComponent<ActionHud>()).Select(t => { var rr = t.GetComponent<Renderer>(); return new Item {name=t.name,path=PathOf(t),position=t.position,min=rr?rr.bounds.min:Vector3.zero,max=rr?rr.bounds.max:Vector3.zero,components=t.GetComponents<Component>().Where(c=>c).Select(c=>c.GetType().Name).ToArray()}; }).ToArray() };
        Directory.CreateDirectory("Logs/Stage12Integration");
        File.WriteAllText("Logs/Stage12Integration/scene.json",JsonUtility.ToJson(report,true));
        return JsonUtility.ToJson(report);
    }
}
