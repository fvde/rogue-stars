using System;
using System.IO;
using GooglePlayGames;
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
    /// <item><c>ROGUESTARS_SERVER_URL</c> (default ServerConfig.ReleaseBaseUrl = production; "local" = keep the dev defaults) → ServerConfig.BaseUrl, online by default, auth "firebase" (unless <c>ROGUESTARS_AUTH=dev</c>).</item>
    /// <item><c>ROGUESTARS_AUTH</c> (dev|firebase) → ServerConfig.AuthMode.</item>
    /// <item><c>ROGUESTARS_STORE</c> (disabled|fake|unity_iap), <c>ROGUESTARS_ADS</c> (disabled|fake|max), <c>ROGUESTARS_MAX_REWARDED_AD_UNIT</c>,
    /// <c>ROGUESTARS_PRODUCT_SET</c> (initial|all) → PlatformConfig.</item>
    /// <item><c>ROGUESTARS_MAX_SDK_KEY</c> → AppLovinSettings.SdkKey (merged into the Android manifest by the MAX plugin);
    /// the AppLovin Quality Service Gradle plugin is only enabled with a real key (it calls AppLovin with it). Only read with
    /// ads "max": with ads disabled the build needs no AppLovin key at all.</item>
    /// <item><c>ROGUESTARS_PLAY_GAMES_APP_ID</c>, <c>ROGUESTARS_PLAY_GAMES_WEB_CLIENT_ID</c> → the Play Games plugin's
    /// PlayGamesSettings + GooglePlayGamesManifest.androidlib (same files as its setup window writes).</item>
    /// <item><c>ROGUESTARS_KEYSTORE</c>, <c>ROGUESTARS_KEYSTORE_PASS</c>, <c>ROGUESTARS_KEY_ALIAS</c>, <c>ROGUESTARS_KEY_PASS</c> → release signing (upload key).</item>
    /// </list>
    /// </summary>
    public sealed class BuildSecrets : IDisposable
    {
        public const string PlayGamesSettingsPath = "Assets/GooglePlayGames/Resources/PlayGamesSettings.asset";
        public const string PlayGamesManifestDir = "Assets/Plugins/Android/GooglePlayGamesManifest.androidlib";
        public const string GoogleServicesJson = "Assets/google-services.json";

        private string _platformJson, _serverJson, _sdkKey;
        private bool _quality;
        private bool _customKeystore;
        private string _keystoreName, _keystorePass, _aliasName, _aliasPass;
        private string _pgsSettingsJson;
        private bool _pgsSettingsCreated, _pgsManifestCreated;

        public string Store { get; private set; }
        public string Ads { get; private set; }
        public string Auth { get; private set; }
        public bool PlayGames { get; private set; }
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
            if (Env("ROGUESTARS_PRODUCT_SET") is string set) platform.ProductSet = set;
            EditorUtility.SetDirty(platform);
            Store = platform.StoreMode;
            bool max = platform.AdsMode == PlatformConfig.AdsMax;
            Ads = max && !platform.HasMaxAdUnit ? "fake (no MAX ad unit id)" : platform.AdsMode;

            if (server != null)
            {
                // Default: the production server (ServerConfig.ReleaseBaseUrl); "local" keeps the dev defaults (offline, 10.0.2.2).
                string url = Env("ROGUESTARS_SERVER_URL") ?? server.ReleaseBaseUrl;
                if (url == "local") url = null;
                if (!string.IsNullOrEmpty(url))
                {
                    server.BaseUrl = url;
                    server.OnlineByDefault = true;
                    server.AuthMode = AuthModes.Firebase; // the production server only accepts Firebase ID tokens
                }
                if (Env("ROGUESTARS_AUTH") is string auth) server.AuthMode = auth;
                EditorUtility.SetDirty(server);
                Auth = server.AuthMode;
            }
            if (Auth == AuthModes.Firebase && !File.Exists(GoogleServicesJson))
                Debug.LogWarning("[BuildSecrets] auth=firebase but " + GoogleServicesJson + " is missing: Firebase cannot initialize in this build");

            var maxSettings = AppLovinSettings.Instance;
            _sdkKey = maxSettings.SdkKey;
            _quality = maxSettings.QualityServiceEnabled;
            string key = max ? Env("ROGUESTARS_MAX_SDK_KEY") : null;
            maxSettings.SdkKey = key ?? "";
            maxSettings.QualityServiceEnabled = key != null;
            EditorUtility.SetDirty(maxSettings);

            ApplyPlayGames(Env("ROGUESTARS_PLAY_GAMES_APP_ID"), Env("ROGUESTARS_PLAY_GAMES_WEB_CLIENT_ID"));

            _customKeystore = PlayerSettings.Android.useCustomKeystore;
            _keystoreName = PlayerSettings.Android.keystoreName;
            _keystorePass = PlayerSettings.Android.keystorePass;
            _aliasName = PlayerSettings.Android.keyaliasName;
            _aliasPass = PlayerSettings.Android.keyaliasPass;
            if (Env("ROGUESTARS_KEYSTORE") is string keystore)
            {
                if (!File.Exists(keystore)) throw new InvalidOperationException("ROGUESTARS_KEYSTORE not found: " + keystore);
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystore;
                PlayerSettings.Android.keystorePass = Env("ROGUESTARS_KEYSTORE_PASS") ?? throw new InvalidOperationException("ROGUESTARS_KEYSTORE_PASS missing");
                PlayerSettings.Android.keyaliasName = Env("ROGUESTARS_KEY_ALIAS") ?? throw new InvalidOperationException("ROGUESTARS_KEY_ALIAS missing");
                PlayerSettings.Android.keyaliasPass = Env("ROGUESTARS_KEY_PASS") ?? Env("ROGUESTARS_KEYSTORE_PASS");
                Signed = true;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BuildSecrets] store=" + Store + " ads=" + Ads + " products=" + platform.ProductSet + " auth=" + Auth + " playGames=" + PlayGames +
                      " maxKey=" + (key != null ? "set" : "none") + " signed=" + Signed + " server=" + (server != null ? server.BaseUrl : "-"));
        }

        /// <summary>Writes the Play Games plugin configuration exactly like its Android setup window (GPGSUtil) does.</summary>
        private void ApplyPlayGames(string appId, string webClientId)
        {
            // The plugin's upgrader writes a manifest with an empty APP_ID on first import; never ship that (the Play Games
            // SDK aborts on it if anything initializes it).
            string manifestPath = PlayGamesManifestDir + "/AndroidManifest.xml";
            if (File.Exists(manifestPath) && File.ReadAllText(manifestPath).Contains("android:value=\"\\u003\""))
            {
                Debug.LogWarning("[BuildSecrets] removing the Play Games manifest with an empty APP_ID (plugin upgrader output)");
                AssetDatabase.DeleteAsset(PlayGamesManifestDir);
            }
            var existing = AssetDatabase.LoadAssetAtPath<PlayGamesSettings>(PlayGamesSettingsPath);
            if (appId == null || webClientId == null)
            {
                PlayGames = existing != null && !string.IsNullOrEmpty(existing.AppId) && !string.IsNullOrEmpty(existing.WebClientId) &&
                            File.Exists(PlayGamesManifestDir + "/AndroidManifest.xml");
                if (appId != null || webClientId != null)
                    Debug.LogWarning("[BuildSecrets] set both ROGUESTARS_PLAY_GAMES_APP_ID and ROGUESTARS_PLAY_GAMES_WEB_CLIENT_ID");
                return;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(appId, @"^\d{6,}$")) throw new InvalidOperationException("ROGUESTARS_PLAY_GAMES_APP_ID must be the numeric Play Games project id");
            if (!webClientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)) throw new InvalidOperationException("ROGUESTARS_PLAY_GAMES_WEB_CLIENT_ID must be an OAuth client id (…apps.googleusercontent.com)");

            if (existing == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PlayGamesSettingsPath));
                existing = ScriptableObject.CreateInstance<PlayGamesSettings>();
                AssetDatabase.CreateAsset(existing, PlayGamesSettingsPath);
                _pgsSettingsCreated = true;
            }
            else _pgsSettingsJson = EditorJsonUtility.ToJson(existing);
            existing.AppId = appId;
            existing.WebClientId = webClientId;
            EditorUtility.SetDirty(existing);

            if (!File.Exists(PlayGamesManifestDir + "/AndroidManifest.xml"))
            {
                Directory.CreateDirectory(PlayGamesManifestDir);
                File.WriteAllText(PlayGamesManifestDir + "/project.properties", "target=android-16\nandroid.library=true\n");
                File.WriteAllText(PlayGamesManifestDir + "/AndroidManifest.xml", PlayGamesManifest(appId));
                _pgsManifestCreated = true;
            }
            PlayGames = true;
        }

        /// <summary>The plugin's template-AndroidManifest.txt with the app id filled in (no Nearby).</summary>
        public static string PlayGamesManifest(string appId) =>
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<!-- Written for one build by RogueStars BuildSecrets (same content as the Google Play Games plugin setup). -->\n" +
            "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\"\n" +
            "    package=\"com.google.example.games.mainlibproj\"\n" +
            "    android:versionCode=\"1\"\n" +
            "    android:versionName=\"1.0\" >\n" +
            "    <application>\n" +
            "        <!-- The space in these forces it to be interpreted as a string vs. int -->\n" +
            "        <meta-data android:name=\"com.google.android.gms.games.APP_ID\"\n" +
            "            android:value=\"\\u003" + appId + "\" />\n" +
            "        <meta-data android:name=\"com.google.android.gms.games.unityVersion\"\n" +
            "            android:value=\"\\u0032.3.0\" />\n" +
            "        <activity android:name=\"com.google.games.bridge.NativeBridgeActivity\"\n" +
            "            android:theme=\"@android:style/Theme.Translucent.NoTitleBar.Fullscreen\" />\n" +
            "    </application>\n" +
            "</manifest>\n";

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
            if (_pgsSettingsCreated) AssetDatabase.DeleteAsset(PlayGamesSettingsPath);
            else if (_pgsSettingsJson != null)
            {
                var pgs = AssetDatabase.LoadAssetAtPath<PlayGamesSettings>(PlayGamesSettingsPath);
                if (pgs != null) { EditorJsonUtility.FromJsonOverwrite(_pgsSettingsJson, pgs); EditorUtility.SetDirty(pgs); }
            }
            if (_pgsManifestCreated) AssetDatabase.DeleteAsset(PlayGamesManifestDir);
            if (Signed)
            {
                PlayerSettings.Android.useCustomKeystore = _customKeystore;
                PlayerSettings.Android.keystoreName = _keystoreName;
                PlayerSettings.Android.keystorePass = _keystorePass;
                PlayerSettings.Android.keyaliasName = _aliasName;
                PlayerSettings.Android.keyaliasPass = _aliasPass;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
