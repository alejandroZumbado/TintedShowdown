using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Builds de Windows y Android, desde el menú o sin abrir Unity:
//   Unity.exe -batchmode -quit -projectPath <repo> -buildTarget Win64   -executeMethod PlatformBuilds.BuildWindows
//   Unity.exe -batchmode -quit -projectPath <repo> -buildTarget Android -executeMethod PlatformBuilds.BuildAndroid
// En batchmode sale con código 1 si falla. Salida en E:\Users\Alejandro\Opal\Builds\
// (misma convención que WebGLBuildScript y que Cannons).
public static class PlatformBuilds
{
    private const string BuildsRoot = @"E:\Users\Alejandro\Opal\Builds";
    private static string WindowsExe => Path.Combine(BuildsRoot, "Tinted Showdown Windows", "Tinted Showdown.exe");
    private static string AndroidApk => Path.Combine(BuildsRoot, "Tinted Showdown Android", "TintedShowdown.apk");

    [MenuItem("Tinted Showdown/Build Windows")]
    public static void BuildWindows() => Finish(Build(BuildTarget.StandaloneWindows64, WindowsExe));

    // APK firmado con la llave debug de Unity: para instalar y probar, NO para
    // Play Store (eso requiere .aab + keystore propio).
    [MenuItem("Tinted Showdown/Build Android APK")]
    public static void BuildAndroid()
    {
        EditorUserBuildSettings.buildAppBundle = false; // .apk, no .aab
        Finish(Build(BuildTarget.Android, AndroidApk));
    }

    static bool Build(BuildTarget target, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = target,
            options = BuildOptions.None
        });

        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[Build {target}] {report.summary.result} — {report.summary.totalErrors} error(es). Ver log arriba.");
            return false;
        }
        Debug.Log($"[Build {target}] OK — {report.summary.totalSize / (1024 * 1024)} MB en {report.summary.totalTime}. Salida: {outputPath}");
        return true;
    }

    // en batchmode el resultado va al código de salida; en el Editor solo al log
    static void Finish(bool ok)
    {
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
}
