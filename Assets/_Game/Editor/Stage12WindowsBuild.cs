using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Builds the delivered scene only. Does not run any historical scene installer.
public static class Stage12WindowsBuild
{
    public const string ScenePath = "Assets/_Game/Scenes/InnDuel_Stage12_CollisionLighting.unity";

    [Serializable]
    private sealed class Result
    {
        public string result, scene, output, unityVersion, version;
        public int errors, warnings;
        public ulong bytes;
        public double seconds;
        public string[] messages;
    }

    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before building.");
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-buildOutput");
        if (index < 0 || index + 1 >= args.Length)
            throw new ArgumentException("Supply -buildOutput through unity build --output.");
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string output = Path.GetFullPath(args[index + 1]);
        string buildRoot = Path.Combine(project, "Builds") + Path.DirectorySeparatorChar;
        if (!output.StartsWith(buildRoot, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(output), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output must be an exe inside this project's Builds directory.");
        string folder = Path.GetDirectoryName(output);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new IOException("Refusing to overwrite an existing build: " + folder);
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            throw new FileNotFoundException("The delivered Stage12 scene is missing.");
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            throw new InvalidOperationException("Windows build support is not installed.");

        PlayerSettings.productName = "红尘客栈";
        PlayerSettings.bundleVersion = "0.3.3";
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(folder);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.CompressWithLz4HC
        });
        var summary = report.summary;
        var result = new Result
        {
            result = summary.result.ToString(), scene = ScenePath, output = output,
            unityVersion = Application.unityVersion, version = PlayerSettings.bundleVersion,
            errors = (int)summary.totalErrors, warnings = (int)summary.totalWarnings,
            bytes = summary.totalSize, seconds = summary.totalTime.TotalSeconds,
            messages = report.steps.SelectMany(step => step.messages)
                .Where(message => message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Warning)
                .Select(message => message.content).ToArray()
        };
        Directory.CreateDirectory(Path.Combine(project, "Logs"));
        File.WriteAllText(Path.Combine(project, "Logs", "Stage12WindowsBuild.json"), JsonUtility.ToJson(result, true));
        if (summary.result != BuildResult.Succeeded || summary.totalErrors != 0 || !File.Exists(output))
            throw new InvalidOperationException("Stage12 Windows build failed; read Logs/Stage12WindowsBuild.json.");
        Debug.Log("STAGE12_WINDOWS_BUILD_OK " + output);
    }
}
