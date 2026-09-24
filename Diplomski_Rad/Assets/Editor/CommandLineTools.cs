// CommandLineTools.cs
// Batch-mode entry points, so the scene and the Windows build can be reproduced
// from a terminal without touching the editor UI (see README.md):
//
//   Unity.exe -batchmode -quit -projectPath <project> -executeMethod CommandLineTools.RebuildScene [-seed N]
//   Unity.exe -batchmode -quit -projectPath <project> -executeMethod CommandLineTools.BuildWindows

using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CommandLineTools
{
    // Seed of the level shipped in the committed scene and the release build.
    public const int DEFAULT_SEED = 2026;

    private const string SCENE_PATH = "Assets/Scenes/PlatformerScene.unity";
    private const string BUILD_PATH = "Builds/Windows/Diplomski_Rad.exe";

    /// <summary>Tools/Build Platformer Scene + seeded procedural level, then save.</summary>
    public static void RebuildScene()
    {
        int seed = ReadIntArg("-seed", DEFAULT_SEED);
        BuildGameScene.Build();
        ProceduralLevelGenerator.GenerateLevel(seed);
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), SCENE_PATH);
        Debug.Log($"[CommandLineTools] Scene rebuilt with seed {seed} -> {SCENE_PATH}");
    }

    [MenuItem("Tools/Build Windows Player")]
    public static void BuildWindows()
    {
        // The project template's SampleScene used to be the only scene in
        // Build Settings, so a plain File > Build opened an empty scene.
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(SCENE_PATH, true) };

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes           = new[] { SCENE_PATH },
            locationPathName = BUILD_PATH,
            target           = BuildTarget.StandaloneWindows64,
            options          = BuildOptions.None,
        });

        var s = report.summary;
        Debug.Log($"[CommandLineTools] Build {s.result}: {BUILD_PATH}, {s.totalSize / (1024 * 1024)} MB, " +
                  $"{s.totalErrors} errors, {s.totalTime.TotalSeconds:F0} s");
        if (Application.isBatchMode && s.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }

    private static int ReadIntArg(string name, int fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v) ? v : fallback;
    }
}
