using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
[InitializeOnLoad] public static class HongchenSceneRepair {
 const string Dir="Temp/HongchenSceneRepair";
 static HongchenSceneRepair(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;string f=Dir+"/command.txt";if(!File.Exists(f))return;try{string cmd=File.ReadAllText(f).Trim();File.Delete(f);if(cmd=="audit")Audit();if(cmd=="apply")Apply();if(cmd=="verify")Verify();if(cmd=="extended")ExtendedVerify();if(cmd=="finish")Finish();if(cmd=="play")EditorApplication.isPlaying=true;if(cmd=="stop")EditorApplication.isPlaying=false;}catch(IOException){/* Retry a command that is still being written. */}catch(Exception e){File.WriteAllText(Dir+"/error.txt",e.ToString());}}
 static void Finish(){
 if(EditorApplication.isPlaying)throw new Exception("Exit play mode before saving");
 var scene=SceneManager.GetActiveScene();if(!scene.path.EndsWith("InnDuel_Stage12_CollisionLighting.unity"))throw new Exception("Unexpected scene");
 EditorSceneManager.SaveScene(scene);Audit();Capture("after_hall");File.WriteAllText(Dir+"/saved.txt",scene.path+"\n"+DateTime.Now.ToString("s"));
 }
 static void ExtendedVerify(){
 if(!EditorApplication.isPlaying)throw new Exception("Extended verification requires Play Mode");
 var lines=new List<string>();var fighter=UnityEngine.Object.FindFirstObjectByType<FengChenInn.PrototypeFighter>();var original=fighter.GetComponent<CharacterController>();
 var go=new GameObject("TEMP runtime movement probe");go.layer=fighter.gameObject.layer;var cc=go.AddComponent<CharacterController>();cc.height=original.height;cc.radius=original.radius;cc.center=original.center;cc.skinWidth=original.skinWidth;cc.stepOffset=original.stepOffset;cc.slopeLimit=original.slopeLimit;
 lines.Add($"Player profile: height={cc.height} radius={cc.radius} step={cc.stepOffset} slope={cc.slopeLimit} layer={go.layer} gravity={fighter.gravity}");
 int pass=0,fail=0;
 Action<string,Vector3,Vector3,float,int,Func<Vector3,bool>> test=(name,start,dir,speed,frames,accept)=>{cc.enabled=false;go.transform.position=start;cc.enabled=true;Physics.SyncTransforms();float vy=-2;for(int i=0;i<frames;i++){if(cc.isGrounded&&vy<0)vy=-2;vy+=fighter.gravity/60f;cc.Move((dir*speed+Vector3.up*vy)/60f);}var end=go.transform.position;bool ok=accept(end);if(ok)pass++;else fail++;lines.Add((ok?"PASS ":"FAIL ")+name+" end="+end.ToString("F3"));};
 try{
 foreach(float x in new[]{4.5f,4.7f,4.9f}){
 test("Stair up x="+x,new Vector3(x,.23f,-2.8f),Vector3.forward,3.5f,120,v=>v.z>3.8f&&v.y>3.9f&&v.y<4.2f);
 test("Stair down x="+x,new Vector3(x,4.05f,3.8f),Vector3.back,3.5f,130,v=>v.z< -3.1f&&v.y>.1f&&v.y<.4f);
 }
 test("Counter running",new Vector3(-2,.23f,1.7f),Vector3.forward,6,100,v=>v.z<2.8f&&v.z>2);
 test("Table running",new Vector3(-2.8f,.23f,-1.9f),Vector3.left,6,100,v=>v.x> -3.6f&&v.x< -3);
 test("East wall",new Vector3(3,.23f,-3.4f),Vector3.right,6,100,v=>v.x<5.7f&&v.x>3);
 test("Rear door",new Vector3(2.12f,.23f,3.7f),Vector3.forward,6,100,v=>v.z<4.6f&&v.z>4);
 var model=GameObject.Find("Stage11 Final - Visuals");var excluded=new HashSet<string>(File.ReadAllLines(Dir+"/visual_only_exclusions.txt"));int covered=0,visual=0,missing=0;
 foreach(var mf in model.GetComponentsInChildren<MeshFilter>()){var rr=mf.GetComponent<Renderer>();if(!rr||!rr.enabled||!mf.sharedMesh)continue;if(excluded.Contains(PathOf(mf.transform))){visual++;continue;}bool ok=mf.GetComponents<Collider>().Any(c=>c.enabled&&!c.isTrigger&&!Physics.GetIgnoreLayerCollision(go.layer,c.gameObject.layer));if(ok)covered++;else{missing++;lines.Add("MISSING "+PathOf(mf.transform));}}
 lines.Add($"Visible mesh coverage: solid={covered}, visual-only={visual}, missing={missing}");
 lines.Add($"RESULT: passed={pass} failed={fail} missing={missing}");
 }finally{UnityEngine.Object.DestroyImmediate(go);File.WriteAllLines(Dir+"/extended_collision_tests.txt",lines);}
 }
 static string PathOf(Transform t){return t.parent?PathOf(t.parent)+"/"+t.name:t.name;}
 static void Capture(string name){var go=new GameObject("Temporary inspection camera");var cam=go.AddComponent<Camera>();cam.transform.position=new Vector3(0,2.1f,-4.15f);cam.transform.LookAt(new Vector3(0,2.4f,2));cam.fieldOfView=80;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.02f,.03f,.06f);var rt=new RenderTexture(1400,900,24);cam.targetTexture=rt;cam.Render();var prev=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(1400,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1400,900),0,0);tex.Apply();File.WriteAllBytes(Dir+"/"+name+".png",tex.EncodeToPNG());RenderTexture.active=prev;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(tex);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
 static void Apply(){
 if(EditorApplication.isPlaying)throw new Exception("Exit play mode first");var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Game/Scenes/InnDuel_Stage11.unity")throw new Exception("Unexpected source scene");
 EditorSceneManager.SaveScene(scene,"Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity",false);
 Capture("before_hall");var model=GameObject.Find("Stage11 Final - Visuals");if(!model)throw new Exception("Missing model");
 PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
 foreach(var c in model.transform.parent.GetComponentsInChildren<Collider>(true))if(!c.transform.IsChildOf(model.transform))c.enabled=false;
 int added=0,skipped=0;var ignored=new List<string>();
 foreach(var mf in model.GetComponentsInChildren<MeshFilter>(true)){
 var rr=mf.GetComponent<Renderer>();string p=PathOf(mf.transform).ToLowerInvariant();
 if(!rr||!rr.enabled||!mf.gameObject.activeInHierarchy||!mf.sharedMesh)continue;
 if(p.Contains("curtain")||p.Contains("banner")||p.Contains("lantern")||p.Contains("bamboo_leaf")||p.Contains("foliage")||p.Contains("node_0")||p.Contains("tripo_node")||p.Contains("lash_")||p.Contains("sword_attached")||p.Contains("rootnode")||p.Contains("立方体")){ignored.Add(PathOf(mf.transform));skipped++;continue;}
 if(mf.GetComponents<Collider>().Any(c=>c.enabled&&!c.isTrigger))continue;
 if(mf.sharedMesh.vertexCount>10000 && mf.name!="Stair_New_ClosedRiser" && !p.Contains("wood_door")){var c=mf.gameObject.AddComponent<BoxCollider>();c.center=mf.sharedMesh.bounds.center;c.size=mf.sharedMesh.bounds.size;}
 else {var c=mf.gameObject.AddComponent<MeshCollider>();c.sharedMesh=mf.sharedMesh;}
 added++;
 }
 Physics.SyncTransforms();var stair=model.GetComponentsInChildren<MeshFilter>().First(x=>x.name=="Stair_New_ClosedRiser");var sc=stair.GetComponent<Collider>();var sb=stair.GetComponent<Renderer>().bounds;
 var samples=new List<Vector3>();for(float z=sb.min.z+.05f;z<sb.max.z-.03f;z+=.05f){if(sc.Raycast(new Ray(new Vector3(sb.center.x,sb.max.y+1,z),Vector3.down),out var hit,10)&&hit.normal.y>.6f)samples.Add(hit.point);}
 if(samples.Count<10)throw new Exception("Cannot measure staircase");var lo=samples.OrderBy(v=>v.y).First();var hi=samples.OrderBy(v=>v.y).Last();lo.z=sb.min.z;lo.y=sb.min.y;hi.z=sb.max.z;var delta=hi-lo;
 var ramp=new GameObject("Stage12 Stair smooth walking surface");ramp.transform.SetParent(model.transform,true);ramp.transform.rotation=Quaternion.LookRotation(delta.normalized,Vector3.up);ramp.transform.position=(lo+hi)*.5f-ramp.transform.up*.045f+Vector3.up*.045f;var rampC=ramp.AddComponent<BoxCollider>();rampC.size=new Vector3(1.40f,.09f,delta.magnitude+.10f);
 var doors=UnityEngine.Object.FindFirstObjectByType<FengChenInn.InnEntranceDoors>();doors.rebuildImportedDoorCollision=true;
 doors.leftHinge.position=new Vector3(-1.2f,doors.leftHinge.position.y,-4.96f);doors.rightHinge.position=new Vector3(1.2f,doors.rightHinge.position.y,-4.96f);
 foreach(var cc in UnityEngine.Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None)){cc.detectCollisions=true;cc.slopeLimit=50;cc.stepOffset=.3f;cc.skinWidth=.04f;}
 var lightRoot=new GameObject("Stage12 Interior warm lighting");
 foreach(var l in model.transform.parent.GetComponentsInChildren<Light>())if(PathOf(l.transform).Contains("Interior Lighting"))l.enabled=false;
 AddLight(lightRoot.transform,"Hall soft warm fill",new Vector3(0,3,-.8f),5f,11f);
 AddLight(lightRoot.transform,"Counter warm fill",new Vector3(-2,2.8f,2.4f),3.5f,7f);
 AddLight(lightRoot.transform,"Stair warm fill",new Vector3(3.5f,4.4f,.4f),3.5f,8f);
 AddLight(lightRoot.transform,"Gallery warm fill",new Vector3(-3,5.6f,3.2f),3f,8f);
 foreach(var rr in model.GetComponentsInChildren<Renderer>())if(rr.name=="Gallery_Lantern_Rear_Mesh"||rr.name=="Gallery_Lantern_West_Mesh"||rr.name=="Hall_Lantern_Front_Mesh")AddLight(lightRoot.transform,rr.name+" warm emission light",rr.bounds.center+Vector3.down*.25f,2.5f,5.5f);
 Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Capture("after_hall");File.WriteAllText(Dir+"/apply.txt",$"scene={scene.path}\nadded colliders={added}\nexcluded visual meshes={skipped}\nstair samples={samples.Count}\nramp bottom={lo} top={hi}\n");File.WriteAllLines(Dir+"/visual_only_exclusions.txt",ignored);Audit();Verify();
 }
 static void AddLight(Transform root,string name,Vector3 pos,float intensity,float range){var go=new GameObject(name);go.transform.SetParent(root);go.transform.position=pos;var l=go.AddComponent<Light>();l.type=LightType.Point;l.color=new Color(1,.76f,.43f);l.intensity=intensity;l.range=range;l.shadows=LightShadows.None;}
 static void Verify(){var lines=new List<string>();Physics.SyncTransforms();var go=new GameObject("TEMP collision test capsule");var cc=go.AddComponent<CharacterController>();cc.height=1.8f;cc.radius=.36f;cc.center=Vector3.up*.9f;cc.skinWidth=.04f;cc.stepOffset=.3f;cc.slopeLimit=50;
 Action<string,Vector3,Vector3,int> walk=(name,start,direction,steps)=>{cc.enabled=false;go.transform.position=start;cc.enabled=true;Physics.SyncTransforms();for(int i=0;i<steps;i++)cc.Move(direction*.035f+Vector3.down*.05f);lines.Add(name+" start="+start.ToString("F3")+" end="+go.transform.position.ToString("F3"));};
 walk("Counter front blocked",new Vector3(-2,.23f,1.7f),Vector3.forward,100);
 walk("West table blocked",new Vector3(-2.8f,.23f,-1.9f),Vector3.left,100);
 walk("East wall blocked",new Vector3(2,.23f,-1.6f),Vector3.right,180);
 walk("Stair ascent",new Vector3(4.7f,.23f,-2.8f),Vector3.forward,200);
 walk("Stair descent",new Vector3(4.7f,4.05f,3.8f),Vector3.back,200);
 walk("Rear hidden door blocked",new Vector3(2.12f,.23f,3.7f),Vector3.forward,90);
 if(EditorApplication.isPlaying){var door=UnityEngine.Object.FindFirstObjectByType<FengChenInn.InnEntranceDoors>();var left=door.leftHinge.localRotation;var right=door.rightHinge.localRotation;door.leftHinge.localRotation=Quaternion.identity;door.rightHinge.localRotation=Quaternion.identity;Physics.SyncTransforms();walk("Entrance CLOSED blocks",new Vector3(0,.23f,-6.1f),Vector3.forward,100);door.leftHinge.localRotation=Quaternion.Euler(0,-135,0);door.rightHinge.localRotation=Quaternion.Euler(0,135,0);Physics.SyncTransforms();walk("Entrance OPEN passes",new Vector3(0,.23f,-6.1f),Vector3.forward,100);door.leftHinge.localRotation=left;door.rightHinge.localRotation=right;Physics.SyncTransforms();}
 UnityEngine.Object.DestroyImmediate(go);File.WriteAllLines(Dir+(EditorApplication.isPlaying?"/play_collision_tests.txt":"/edit_collision_tests.txt"),lines);if(EditorApplication.isPlaying)Capture("play_hall");}
 [Serializable] public class Item {public string path,kind;public Vector3 position,min,max;public int vertices,colliders;public bool active;public float intensity,range;}
 [Serializable] public class Report {public string scene;public bool playing;public List<Item> items=new List<Item>();}
 static void Audit(){var r=new Report{scene=SceneManager.GetActiveScene().path,playing=EditorApplication.isPlaying};foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true)){var mf=t.GetComponent<MeshFilter>();var rr=t.GetComponent<Renderer>();var c=t.GetComponent<Collider>();var l=t.GetComponent<Light>();if(mf||c||l||!t.parent){var a=new Item{path=PathOf(t),position=t.position,kind=mf?"mesh":l?"light":c?c.GetType().Name:"root",active=t.gameObject.activeInHierarchy,colliders=t.GetComponents<Collider>().Count(x=>x.enabled&&!x.isTrigger)};if(rr){a.min=rr.bounds.min;a.max=rr.bounds.max;a.active&=rr.enabled;}if(mf&&mf.sharedMesh)a.vertices=mf.sharedMesh.vertexCount;if(l){a.intensity=l.intensity;a.range=l.range;}r.items.Add(a);}}File.WriteAllText(Dir+"/audit.json",JsonUtility.ToJson(r,true));}
}
