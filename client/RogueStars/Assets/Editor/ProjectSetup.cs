using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace RogueStars.Client.Editor
{
    /// <summary>Player settings for the Android prototype. Idempotent; run via -executeMethod RogueStars.Client.Editor.ProjectSetup.Apply.</summary>
    public static class ProjectSetup
    {
        public const string PackageName = "com.roguestars.game";

        [MenuItem("Rogue Stars/Apply Project Settings")]
        public static void Apply()
        {
            PlayerSettings.companyName = "Rogue Stars";
            PlayerSettings.productName = "Rogue Stars";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.runInBackground = false;
            // Dev servers run on plain http (localhost / 10.0.2.2); production uses https on Cloud Run.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;

            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, PackageName);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Minimal);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = true;

            Bootstrap.EnsureServerConfig();
            Bootstrap.EnsurePlatformConfig();
            // AppLovin MAX: the SDK key is injected per build (BuildSecrets). Without a key the Quality Service Gradle
            // plugin must stay off (it would call AppLovin with the key).
            var max = AppLovinSettings.Instance;
            if (string.IsNullOrEmpty(max.SdkKey) && max.QualityServiceEnabled) { max.QualityServiceEnabled = false; EditorUtility.SetDirty(max); }
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] Applied player settings for " + PackageName);
        }
    }
}
