using System.IO;
using RogueStars.Client.Services;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;

namespace RogueStars.Client.Editor
{
    /// <summary>
    /// Patches the generated launcher (app) manifest, the highest manifest-merger priority: <c>tools:node="remove"</c> only
    /// wins over library manifests from there. While ads are disabled (PlatformConfig.AdsMode != max) the advertising-id
    /// permissions that the AppLovin and Firebase (measurement) libraries merge in are removed, so the app declares no
    /// advertising id use. The Play Billing permission is declared here as well (also by Unity IAP's billing library and
    /// RogueStarsConfig.androidlib); BuildAndroid checks every artefact for it.
    /// </summary>
    public sealed class LauncherManifestPatch : IPostGenerateGradleAndroidProject
    {
        public const string Marker = "<!-- RogueStars LauncherManifestPatch -->";
        public static readonly string[] AdPermissions =
        {
            "com.google.android.gms.permission.AD_ID",
            "android.permission.ACCESS_ADSERVICES_AD_ID",
            "android.permission.ACCESS_ADSERVICES_ATTRIBUTION",
            "android.permission.ACCESS_ADSERVICES_TOPICS",
        };

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            string path = Path.Combine(unityLibraryPath, "..", "launcher", "src", "main", "AndroidManifest.xml");
            if (!File.Exists(path)) { Debug.LogWarning("[LauncherManifestPatch] launcher manifest not found: " + path); return; }
            var platform = AssetDatabase.LoadAssetAtPath<PlatformConfig>(Bootstrap.PlatformConfigPath);
            bool adsEnabled = platform != null && platform.AdsMode == PlatformConfig.AdsMax;
            File.WriteAllText(path, Patch(File.ReadAllText(path), adsEnabled));
            Debug.Log("[LauncherManifestPatch] billing permission declared, advertising id " + (adsEnabled ? "kept (ads max)" : "removed (ads disabled)"));
        }

        public static string Patch(string manifest, bool adsEnabled)
        {
            if (manifest.Contains(Marker)) return manifest;
            if (!manifest.Contains("xmlns:tools="))
                manifest = manifest.Replace("<manifest ", "<manifest xmlns:tools=\"http://schemas.android.com/tools\" ");
            var add = new System.Text.StringBuilder();
            add.Append("  ").Append(Marker).Append('\n');
            add.Append("  <uses-permission android:name=\"com.android.vending.BILLING\" />\n");
            if (!adsEnabled)
                foreach (var p in AdPermissions) add.Append("  <uses-permission android:name=\"").Append(p).Append("\" tools:node=\"remove\" />\n");
            int at = manifest.IndexOf("<application", System.StringComparison.Ordinal);
            return at < 0 ? manifest : manifest.Substring(0, at) + add + "  " + manifest.Substring(at);
        }
    }
}
