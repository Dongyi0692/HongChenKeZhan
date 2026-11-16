using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using FengChenInn;

[InitializeOnLoad]
public static class MainMenuV2Tools {
    const string Output="Temp/MainMenuV2Verification";
    static MainMenuV2Tools(){EditorApplication.update+=Poll;}
    static void Poll(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        const string file="Temp/MenuV2.command";
        if(!File.Exists(file))return;
        string cmd;
        try {cmd=File.ReadAllText(file).Trim();File.Delete(file);}catch(IOException){return;}
        Directory.CreateDirectory(Output);
        try {
            if(cmd=="play")EditorApplication.isPlaying=true;
            else if(cmd=="stop")EditorApplication.isPlaying=false;
            else if(cmd=="import"){
                foreach(var path in Directory.GetFiles("Assets/_Game/Resources/MainMenuV2","*.png")){
                    var imp=(TextureImporter)AssetImporter.GetAtPath(path.Replace('\\','/'));
                    imp.mipmapEnabled=false;imp.alphaIsTransparency=true;imp.npotScale=TextureImporterNPOTScale.None;imp.textureCompression=TextureImporterCompression.Uncompressed;imp.maxTextureSize=2048;imp.SaveAndReimport();
                }
            }
            else if(cmd.StartsWith("capture")){ScreenCapture.CaptureScreenshot(Output+"/"+cmd+".png");}
            else if(cmd.StartsWith("button")){
                var menu=UnityEngine.Object.FindFirstObjectByType<MainMenuV2>();
                menu.GetComponentsInChildren<Button>()[int.Parse(cmd.Substring(6))].onClick.Invoke();
            }
            else if(cmd=="back"||cmd=="tutorial"||cmd=="title"){
                var ui=UnityEngine.Object.FindFirstObjectByType<InnInterface>();
                var method=cmd=="back"?"CloseSettings":cmd=="tutorial"?"CloseTutorial":"Restart";
                typeof(InnInterface).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,cmd=="title"?new object[]{true}:null);
            }
            else if(cmd=="status"){
                var ui=UnityEngine.Object.FindFirstObjectByType<InnInterface>();
                string v=ui?typeof(InnInterface).GetField("view",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui).ToString():"missing";
                File.WriteAllText(Output+"/status.txt",$"scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}\nplaying={EditorApplication.isPlaying}\nview={v}\ntimeScale={Time.timeScale}\nmenu={UnityEngine.Object.FindFirstObjectByType<MainMenuV2>()!=null}\nscreen={Screen.width}x{Screen.height}\n");
            }
            File.WriteAllText(Output+"/last_command.txt",cmd+" OK "+DateTime.Now);
        }catch(Exception e){File.WriteAllText(Output+"/error.txt",e.ToString());Debug.LogException(e);}
    }
}
