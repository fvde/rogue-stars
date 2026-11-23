using System;
using RogueStars.Client.Services;
using UnityEditor;
using UnityEngine;

namespace RogueStars.Client.Editor
{
    /// <summary>
    /// Writes build-time configuration from environment variables into the assets for one build and restores the committed
    /// placeholders afterwards (Dispose), so real keys never end up in git. Nothing here is a secret by Google/AppLovin's
    /// definition except the keystore passwords, which only live in the environment (docs/setup.md, "Release signing").
    /// <list type="table">
    /// <item><c>ROGUESTARS_SERVER_URL</c> → ServerConfig.BaseUrl, online by default.</item>
    /// <item><c>ROGUESTARS_STORE</c> (fake|unity_iap), <c>ROGUESTARS_ADS</c> (fake|max), <c>ROGUESTARS_MAX_REWARDED_AD_UNIT</c> → PlatformConfig.</item>
    /// <item><c>ROGUESTARS_MAX_SDK_KEY</c> → AppLovinSettings.SdkKey (merged into the Android manifest by the MAX plugin);
    /// the AppLovin Quality Service Gradle plugin is only enabled with a real key (it calls AppLovin with it).</item>
    /// <item><c>ROGUESTARS_KEYSTORE</c>, <c>ROGUESTARS_KEYSTORE_PASS</c>, <c>ROGUESTARS_KEY_ALIAS</c>, <c>ROGUESTARS_KEY_PASS</c> → release signing (upload key).</item>
    /// </list>
    /// </summary>
    public sealed class BuildSecrets : IDisposable
    {
        private string _platformJson, _serverJson, _sdkKey;
        private bool _quality;
        private bool _customKeystore;
        private string _keystoreName, _keystorePass, _aliasName, _aliasPass;

        public string Store { get; private set; }
        public string Ads { get; private set; }
        public bool Signed { get; private set; }

        public static BuildSecrets Inject()
        {
            var b = new BuildSecrets();
            b.Apply();
            return b;
        }

        private static string Env(string name) => BuildAndroid.Env(name);

        private void Apply()
        {
            Bootstrap.EnsurePlatformConfig();
            var platform = AssetDatabase.LoadAssetAtPath<PlatformConfig>(Bootstrap.PlatformConfigPath);
            var server = AssetDatabase.LoadAssetAtPath<ServerConfig>(Bootstrap.ServerConfigPath);
            _platformJson = EditorJsonUtility.ToJson(platform);
            _serverJson = server != null ? EditorJsonUtility.ToJson(server) : null;

            if (Env("ROGUESTARS_STORE") is string store) platform.StoreMode = store;
            if (Env("ROGUESTARS_ADS") is string ads) platform.AdsMode = ads;
            if (Env("ROGUESTARS_MAX_REWARDED_AD_UNIT") is string unit) platform.MaxRewardedAdUnitId = unit;
            EditorUtility.SetDirty(platform);
            Store = platform.StoreMode;
            Ads = platform.AdsMode == PlatformConfig.AdsMax && !platform.HasMaxAdUnit ? "fake (no MAX ad unit id)" : platform.AdsMode;

            if (server != null && Env("ROGUESTARS_SERVER_URL") is string url)
            {
                server.BaseUrl = url;
                server.OnlineByDefault = true;
                EditorUtility.SetDirty(server);
            }

            var max = AppLovinSettings.Instance;
            _sdkKey = max.SdkKey;
            _quality = max.QualityServiceEnabled;
            string key = Env("ROGUESTARS_MAX_SDK_KEY");
            max.SdkKey = key ?? "";
            max.QualityServiceEnabled = key != null;
            EditorUtility.SetDirty(max);

            _customKeystore = PlayerSettings.Android.useCustomKeystore;
            _keystoreName = PlayerSettings.Android.keystoreName;
            _keystorePass = PlayerSettings.Android.keystorePass;
            _aliasName = PlayerSettings.Android.keyaliasName;
            _aliasPass = PlayerSettings.Android.keyaliasPass;
            if (Env("ROGUESTARS_KEYSTORE") is string keystore)
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystore;
                PlayerSettings.Android.keystorePass = Env("ROGUESTARS_KEYSTORE_PASS") ?? throw new InvalidOperationException("ROGUESTARS_KEYSTORE_PASS missing");
                PlayerSettings.Android.keyaliasName = Env("ROGUESTARS_KEY_ALIAS") ?? throw new InvalidOperationException("ROGUESTARS_KEY_ALIAS missing");
                PlayerSettings.Android.keyaliasPass = Env("ROGUESTARS_KEY_PASS") ?? Env("ROGUESTARS_KEYSTORE_PASS");
                Signed = true;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[BuildSecrets] store=" + Store + " ads=" + Ads + " maxKey=" + (key != null ? "set" : "none") + " signed=" + Signed +
                      " server=" + (server != null ? server.BaseUrl : "-"));
        }

        public void Dispose()
        {
            var platform = AssetDatabase.LoadAssetAtPath<PlatformConfig>(Bootstrap.PlatformConfigPath);
            if (platform != null) { EditorJsonUtility.FromJsonOverwrite(_platformJson, platform); EditorUtility.SetDirty(platform); }
            var server = AssetDatabase.LoadAssetAtPath<ServerConfig>(Bootstrap.ServerConfigPath);
            if (server != null && _serverJson != null) { EditorJsonUtility.FromJsonOverwrite(_serverJson, server); EditorUtility.SetDirty(server); }
            var max = AppLovinSettings.Instance;
            max.SdkKey = _sdkKey ?? "";
            max.QualityServiceEnabled = _quality;
            EditorUtility.SetDirty(max);
            if (Signed)
            {
                PlayerSettings.Android.useCustomKeystore = _customKeystore;
                PlayerSettings.Android.keystoreName = _keystoreName;
                PlayerSettings.Android.keystorePass = _keystorePass;
                PlayerSettings.Android.keyaliasName = _aliasName;
                PlayerSettings.Android.keyaliasPass = _aliasPass;
            }
            AssetDatabase.SaveAssets();
        }
    }
}
