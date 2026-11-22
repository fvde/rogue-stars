using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace RogueStars.Client.Editor
{
    /// <summary>
    /// IL2CPP / ARM64 APK build to Builds/RogueStars.apk.
    /// Batchmode: Unity -batchmode -nographics -quit -buildTarget Android -projectPath client/RogueStars
    ///            -executeMethod RogueStars.Client.Editor.BuildAndroid.Build -logFile /abs/build.log
    /// Exits with code 1 on failure.
    /// </summary>
    public static class BuildAndroid
    {
        public const string OutputPath = "Builds/RogueStars.apk";
        public const string FallbackVersion = "0.1.0";

        [MenuItem("Rogue Stars/Build Android APK")]
        public static void Build()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                ProjectSetup.Apply();
                if (!File.Exists(Bootstrap.ScenePath)) Bootstrap.CreateScene();

                var (version, code) = GitVersion();
                PlayerSettings.bundleVersion = version;
                PlayerSettings.Android.bundleVersionCode = code;
                EditorUserBuildSettings.buildAppBundle = false;

                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { Bootstrap.ScenePath },
                    locationPathName = OutputPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                var s = report.summary;
                sw.Stop();
                if (s.result != BuildResult.Succeeded)
                {
                    Debug.LogError("[BuildAndroid] FAILED: " + s.result + " errors=" + s.totalErrors + " after " + sw.Elapsed.TotalSeconds.ToString("0") + "s");
                    if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(1);
                    return;
                }
                long bytes = new FileInfo(OutputPath).Length;
                Debug.Log("[BuildAndroid] OK " + OutputPath + " version " + version + " (" + code + ") size " + (bytes / 1048576.0).ToString("0.0") + " MB in " + sw.Elapsed.TotalSeconds.ToString("0") + "s");
            }
            catch (Exception ex)
            {
                Debug.LogError("[BuildAndroid] exception: " + ex);
                if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        /// <summary>"0.1.&lt;commit count&gt;" and the commit count as versionCode; constant fallback without git.</summary>
        public static (string version, int code) GitVersion()
        {
            try
            {
                var psi = new ProcessStartInfo("git", "rev-list --count HEAD")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(5000);
                    if (p.ExitCode == 0 && int.TryParse(output, out int count) && count > 0) return ("0.1." + count, count);
                }
            }
            catch (Exception) { /* no git on PATH */ }
            return (FallbackVersion, 1);
        }
    }
}
