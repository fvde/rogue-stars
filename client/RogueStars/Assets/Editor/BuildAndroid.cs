using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace RogueStars.Client.Editor
{
    /// <summary>
    /// IL2CPP / ARM64 builds: Builds/RogueStars.aab (Play) and Builds/RogueStars.apk (sideloading), see <see cref="Build"/>.
    /// Batchmode: Unity -batchmode -nographics -quit -buildTarget Android -projectPath client/RogueStars
    ///            -executeMethod RogueStars.Client.Editor.BuildAndroid.Build -logFile /abs/build.log
    /// Exits with code 1 on failure.
    /// Build-time configuration (environment variables; the committed assets keep placeholders and are restored after the
    /// build, see <see cref="BuildSecrets"/> and docs/setup.md): ROGUESTARS_SERVER_URL, ROGUESTARS_AUTH (dev|firebase),
    /// ROGUESTARS_STORE (fake|unity_iap), ROGUESTARS_ADS (disabled|fake|max), ROGUESTARS_PRODUCT_SET (initial|all),
    /// ROGUESTARS_MAX_SDK_KEY, ROGUESTARS_MAX_REWARDED_AD_UNIT, ROGUESTARS_PLAY_GAMES_APP_ID, ROGUESTARS_PLAY_GAMES_WEB_CLIENT_ID,
    /// ROGUESTARS_KEYSTORE, ROGUESTARS_KEYSTORE_PASS, ROGUESTARS_KEY_ALIAS, ROGUESTARS_KEY_PASS, ROGUESTARS_OUTPUT (apk|aab|both).
    /// </summary>
    public static class BuildAndroid
    {
        public const string OutputPath = "Builds/RogueStars.apk";
        public const string BundlePath = "Builds/RogueStars.aab";
        public const string FallbackVersion = "0.1.0";
        public const string BillingPermission = "com.android.vending.BILLING";

        /// <summary>
        /// Builds the app bundle for Play (Builds/RogueStars.aab) and the sideloadable APK (Builds/RogueStars.apk) with the
        /// same configuration and version. ROGUESTARS_OUTPUT=apk|aab|both (default both; ROGUESTARS_AAB=1 = aab only).
        /// Signed with the upload key when ROGUESTARS_KEYSTORE is set, otherwise debug-signed (fine locally, refused by Play).
        /// Every artefact must declare the Play Billing permission, else the build fails.
        /// </summary>
        [MenuItem("Rogue Stars/Build Android (AAB + APK)")]
        public static void Build()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                ProjectSetup.Apply();
                if (!File.Exists(Bootstrap.ScenePath)) Bootstrap.CreateScene();

                string mode = Env("ROGUESTARS_OUTPUT") ?? (Env("ROGUESTARS_AAB") == "1" ? "aab" : "both");
                if (mode != "apk" && mode != "aab" && mode != "both") throw new InvalidOperationException("ROGUESTARS_OUTPUT must be apk, aab or both");
                bool buildAab = mode != "apk", buildApk = mode != "aab";

                using (var injected = BuildSecrets.Inject())
                {
                    ResolveAndroidDependencies();

                    var (version, code) = GitVersion();
                    PlayerSettings.bundleVersion = version;
                    PlayerSettings.Android.bundleVersionCode = code;
                    Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
                    bool ok = true;
                    try
                    {
                        if (buildAab) ok = BuildOne(BundlePath, true, injected, version, code);
                        if (ok && buildApk) ok = BuildOne(OutputPath, false, injected, version, code);
                    }
                    finally { EditorUserBuildSettings.buildAppBundle = false; }
                    sw.Stop();
                    if (!ok)
                    {
                        if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(1);
                        return;
                    }
                    Debug.Log("[BuildAndroid] done (" + mode + ") in " + sw.Elapsed.TotalSeconds.ToString("0") + "s");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[BuildAndroid] exception: " + ex);
                if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static bool BuildOne(string output, bool appBundle, BuildSecrets injected, string version, int code)
        {
            var sw = Stopwatch.StartNew();
            EditorUserBuildSettings.buildAppBundle = appBundle;
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
            if (s.result != BuildResult.Succeeded)
            {
                Debug.LogError("[BuildAndroid] FAILED " + output + ": " + s.result + " errors=" + s.totalErrors + " after " + sw.Elapsed.TotalSeconds.ToString("0") + "s");
                return false;
            }
            if (!ArchiveMentions(output, BillingPermission))
            {
                Debug.LogError("[BuildAndroid] FAILED " + output + ": manifest lacks " + BillingPermission + " (Play Console would not allow in-app products)");
                return false;
            }
            long bytes = new FileInfo(output).Length;
            bool adId = ArchiveMentions(output, LauncherManifestPatch.AdPermissions[0]);
            Debug.Log("[BuildAndroid] OK " + output + " (store " + injected.Store + ", ads " + injected.Ads + ", auth " + injected.Auth +
                      ", playGames " + injected.PlayGames + ", signed " + injected.Signed + ", billing permission yes, AD_ID " + (adId ? "declared" : "absent") + ") version " + version +
                      " (" + code + ") size " + (bytes / 1048576.0).ToString("0.0") + " MB in " + sw.Elapsed.TotalSeconds.ToString("0") + "s");
            return true;
        }

        /// <summary>
        /// True when the manifest of an APK (binary XML, UTF-16 or UTF-8 string pool) or AAB (base/manifest, protobuf UTF-8)
        /// contains <paramref name="text"/>.
        /// </summary>
        public static bool ArchiveMentions(string archivePath, string text)
        {
            using (var zip = System.IO.Compression.ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName != "AndroidManifest.xml" && entry.FullName != "base/manifest/AndroidManifest.xml") continue;
                    byte[] data;
                    using (var s = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        data = ms.ToArray();
                    }
                    if (Contains(data, System.Text.Encoding.UTF8.GetBytes(text)) || Contains(data, System.Text.Encoding.Unicode.GetBytes(text))) return true;
                }
            }
            return false;
        }

        private static bool Contains(byte[] data, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= data.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && data[i + j] == needle[j]) j++;
                if (j == needle.Length) return true;
            }
            return false;
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
