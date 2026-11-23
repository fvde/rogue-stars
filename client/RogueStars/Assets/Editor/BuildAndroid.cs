using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace RogueStars.Client.Editor
{
    /// <summary>
    /// IL2CPP / ARM64 APK build to Builds/RogueStars.apk (or Builds/RogueStars.aab for Play with ROGUESTARS_AAB=1).
    /// Batchmode: Unity -batchmode -nographics -quit -buildTarget Android -projectPath client/RogueStars
    ///            -executeMethod RogueStars.Client.Editor.BuildAndroid.Build -logFile /abs/build.log
    /// Exits with code 1 on failure.
    /// Build-time configuration (environment variables; the committed assets keep placeholders and are restored after the
    /// build, see <see cref="BuildSecrets"/> and docs/setup.md): ROGUESTARS_SERVER_URL, ROGUESTARS_STORE (fake|unity_iap),
    /// ROGUESTARS_ADS (fake|max), ROGUESTARS_MAX_SDK_KEY, ROGUESTARS_MAX_REWARDED_AD_UNIT, ROGUESTARS_KEYSTORE,
    /// ROGUESTARS_KEYSTORE_PASS, ROGUESTARS_KEY_ALIAS, ROGUESTARS_KEY_PASS, ROGUESTARS_AAB.
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

                using (var injected = BuildSecrets.Inject())
                {
                    ResolveAndroidDependencies();

                    var (version, code) = GitVersion();
                    PlayerSettings.bundleVersion = version;
                    PlayerSettings.Android.bundleVersionCode = code;
                    bool aab = Env("ROGUESTARS_AAB") == "1";
                    EditorUserBuildSettings.buildAppBundle = aab;
                    string output = aab ? Path.ChangeExtension(OutputPath, ".aab") : OutputPath;

                    Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
                    var options = new BuildPlayerOptions
                    {
                        scenes = new[] { Bootstrap.ScenePath },
                        locationPathName = output,
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
                    long bytes = new FileInfo(output).Length;
                    Debug.Log("[BuildAndroid] OK " + output + " (store " + injected.Store + ", ads " + injected.Ads + ", signed " + injected.Signed + ") version " + version +
                              " (" + code + ") size " + (bytes / 1048576.0).ToString("0.0") + " MB in " + sw.Elapsed.TotalSeconds.ToString("0") + "s");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[BuildAndroid] exception: " + ex);
                if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        internal static string Env(string name)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }

        /// <summary>
        /// External Dependency Manager (shipped with the MAX plugin) writes the AppLovin SDK Maven dependencies into
        /// Assets/Plugins/Android/mainTemplate.gradle; Gradle downloads them during the build. Invoked by reflection so the
        /// build does not depend on EDM's versioned assembly names.
        /// </summary>
        [MenuItem("Rogue Stars/Resolve Android Dependencies")]
        public static void ResolveAndroidDependencies()
        {
            Type resolver = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                resolver = resolver ?? asm.GetType("GooglePlayServices.PlayServicesResolver");
            var resolveSync = resolver?.GetMethod("ResolveSync", new[] { typeof(bool) });
            if (resolveSync == null) { Debug.LogWarning("[BuildAndroid] External Dependency Manager not loaded; skipping Android dependency resolution"); return; }
            bool ok = (bool)resolveSync.Invoke(null, new object[] { true });
            Debug.Log("[BuildAndroid] Android dependency resolution " + (ok ? "succeeded" : "FAILED"));
            if (!ok) throw new InvalidOperationException("Android dependency resolution failed");
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
