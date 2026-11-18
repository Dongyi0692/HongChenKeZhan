using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using FengChenInn;

[InitializeOnLoad]
public static class FootstepSceneAudit
{
    static FootstepSceneAudit() { EditorApplication.update+=()=> {
        const string command="Temp/Footsteps.command";
        if(EditorApplication.isCompiling||!File.Exists(command))return;
        var text=File.ReadAllText(command).Trim();File.Delete(command);
        if(text=="audit")Run();
        else if(text=="refresh")AssetDatabase.Refresh();
        else if(text=="test")typeof(FootstepSceneAudit).Assembly.GetType("FootstepVerification").GetMethod("StartTest").Invoke(null,null);
        else if(text=="combat")typeof(FootstepSceneAudit).Assembly.GetType("CombatAudioVerification").GetMethod("StartTest").Invoke(null,null);
    }; }
    public static void Run()
    {
        var lines=new List<string>();
        lines.Add("Scene: "+UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
        foreach(var f in Object.FindObjectsByType<PrototypeFighter>(FindObjectsInactive.Include,FindObjectsSortMode.None)) {
            lines.Add("Fighter "+f.name+" at "+f.transform.position);
            var driver=f.animationDriver ? f.animationDriver : f.GetComponentInChildren<AholoAnimationDriver>(true);
            if(!driver||!driver.motionSet) continue;
            foreach(var motion in new[]{CombatMotion.Walk,CombatMotion.Run}) {
                var c=driver.motionSet.GetClip(motion);if(!c)continue;
                lines.Add(motion+" "+AssetDatabase.GetAssetPath(c)+" length="+c.length+" events="+c.events.Length);
                foreach(var e in c.events)lines.Add(" Event "+e.time+" "+e.functionName);
                if(driver.targetAnimator) {
                    var clone=Object.Instantiate(driver.targetAnimator.gameObject);clone.hideFlags=HideFlags.HideAndDontSave;
                    foreach(var component in clone.GetComponentsInChildren<Behaviour>())component.enabled=false;
                    var feet=clone.GetComponentsInChildren<Transform>().Where(t=>t.name.ToLower().Contains("foot")&&!t.name.ToLower().Contains("toe")).ToArray();
                    for(int i=0;i<20;i++) { c.SampleAnimation(clone,c.length*i/20f);lines.Add(" Phase "+(i/20f)+" "+string.Join(";",feet.Select(t=>t.name+"="+clone.transform.InverseTransformPoint(t.position).ToString("F3")))); }
                    Object.DestroyImmediate(clone);
                }
            }
            lines.Add("Animator "+(driver.targetAnimator?driver.targetAnimator.name:"NULL"));
        }
        Physics.SyncTransforms();
        foreach(var p in new[]{new Vector3(0,2,-9),new Vector3(0,2,-7),new Vector3(0,2,-5.5f),new Vector3(0,2,-4),new Vector3(0,2,-2),new Vector3(-3,2,1),new Vector3(4.7f,6,1),new Vector3(4.7f,6,4),new Vector3(-4,6,3)}) {
            lines.Add("Probe "+p);
            foreach(var h in Physics.RaycastAll(p,Vector3.down,8).OrderBy(h=>h.distance).Take(5)) {
                var r=h.collider.GetComponent<Renderer>();
                lines.Add(" "+h.point+" "+Path(h.collider.transform)+" trigger="+h.collider.isTrigger+" material="+(r?string.Join(",",r.sharedMaterials.Select(m=>m?m.name:"null")):"none"));
            }
        }
        Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/FootstepSceneAudit.txt",lines);
    }
    static string Path(Transform t)=>t.parent?Path(t.parent)+"/"+t.name:t.name;
}
