// Run once: Tinted Showdown → Build WebGL
// Also invoked headless via: Unity.exe -batchmode -quit -executeMethod WebGLBuildScript.Build

using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebGLBuildScript
{
    private const string OutputPath = @"E:\Users\Alejandro\Opal\Builds\Tinten Showdown";

    [MenuItem("Tinted Showdown/Build WebGL")]
    public static void Build()
    {
        // Applies decompressionFallback + the custom template — same settings Setup All
        // sets, called directly here since batchmode has no one to click through the
        // interactive Setup All confirmation dialog.
        TintedShowdownSetup.SetupWebGLPlayerSettings();

        var scenes = new string[EditorBuildSettings.scenes.Length];
        for (int i = 0; i < scenes.Length; i++)
            scenes[i] = EditorBuildSettings.scenes[i].path;

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = OutputPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        });

        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[WebGLBuild] Build {report.summary.result} — {report.summary.totalErrors} error(es). Ver el log completo arriba.");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[WebGLBuild] OK — {report.summary.totalSize / (1024 * 1024)} MB en {report.summary.totalTime}. Salida: {OutputPath}");
    }
}
