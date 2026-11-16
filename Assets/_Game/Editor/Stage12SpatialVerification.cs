using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using FengChenInn;
using UnityEditor;
using UnityEngine;

public static class Stage12SpatialVerification
{
    [Serializable] public class Result { public int passed, failed; public List<string> checks=new(); }
    [Unity.Pipeline.Commands.CliCommand("innstage12","spatial_test","Check new rear door passage, stairs and rescue footing with current player capsule")]
    public static string Test()
    {
        if(!Application.isPlaying)throw new Exception("Play first");
        var r=new Result();var fighter=UnityEngine.Object.FindFirstObjectByType<PrototypeFighter>();var source=fighter.GetComponent<CharacterController>();
        var anchors=UnityEngine.Object.FindFirstObjectByType<InnStoryAnchors>();var door=anchors.rearDoorController;
        var rotation=door.hinge.localRotation;var wasOpen=door.OpenRequested;
        var go=new GameObject("Temporary Stage12 movement probe");go.layer=fighter.gameObject.layer;
        var cc=go.AddComponent<CharacterController>();cc.height=source.height;cc.radius=source.radius;cc.center=source.center;cc.stepOffset=source.stepOffset;cc.skinWidth=source.skinWidth;cc.slopeLimit=source.slopeLimit;
        void Check(bool ok,string msg){if(ok)r.passed++;else r.failed++;r.checks.Add((ok?"PASS ":"FAIL ")+msg);}
        Vector3 Walk(Vector3 start,Vector3 direction,int frames){cc.enabled=false;go.transform.position=start;cc.enabled=true;Physics.SyncTransforms();float vy=-2;for(int i=0;i<frames;i++){if(cc.isGrounded&&vy<0)vy=-2;vy+=fighter.gravity/60f;cc.Move((direction*3.5f+Vector3.up*vy)/60f);}return go.transform.position;}
        try{
            door.OpenRequested=false;door.hinge.localRotation=rotation;Physics.SyncTransforms();
            var end=Walk(new Vector3(2.125f,.25f,3.65f),Vector3.forward,70);Check(end.z<4.8f,"Rear closed blocks "+end);
            door.hinge.localRotation=rotation*Quaternion.Euler(0,door.openAngle,0);Physics.SyncTransforms();
            end=Walk(new Vector3(2.125f,.25f,3.65f),Vector3.forward,70);Check(end.z>6.5f&&cc.isGrounded&&Mathf.Abs(end.y-anchors.rearExit.position.y)<.15f,"Rear open exits onto authored lower courtyard floor "+end);
            end=Walk(new Vector3(2.125f,.25f,7f),Vector3.back,65);Check(end.z<4f&&end.y>0,"Rear open reentry "+end);
            foreach(float x in new[]{4.5f,4.7f,4.9f}){end=Walk(new Vector3(x,.25f,-2.8f),Vector3.forward,120);Check(end.z>3.8f&&end.y>3.9f,"Stair up "+x+" "+end);end=Walk(new Vector3(x,4.05f,3.8f),Vector3.back,130);Check(end.z< -3.1f&&end.y>.1f&&end.y<.4f,"Stair down "+x+" "+end);}
            var duel=fighter.GetComponent<DuelEncounter>();foreach(var t in duel.rescueSpawnAnchors){end=Walk(t.position,Vector3.zero,30);Check(Vector3.Distance(end,t.position)<.45f&&cc.isGrounded,"Rescue footing "+t.name+" "+end);}
        }finally{door.OpenRequested=wasOpen;door.hinge.localRotation=rotation;UnityEngine.Object.DestroyImmediate(go);Physics.SyncTransforms();}
        Directory.CreateDirectory("Logs/Stage12Integration");var json=JsonUtility.ToJson(r,true);File.WriteAllText("Logs/Stage12Integration/spatial.json",json);return json;
    }
    [Unity.Pipeline.Commands.CliCommand("innstage12","materials","Read current character materials and shader loading state")]
    public static string Materials()
    {
        var actors=UnityEngine.Object.FindObjectsByType<CombatActor>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        return string.Join("\n",actors.SelectMany(a=>a.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials.Where(m=>m).Select(m=>$"{a.name}/{r.name}: {m.name}, {m.shader.name}, supported={m.shader.isSupported}, color={m.color}, texture={m.mainTexture?.name}"))));
    }
}
