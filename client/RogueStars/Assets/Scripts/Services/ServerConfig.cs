using UnityEngine;

namespace RogueStars.Client.Services
{
    /// <summary>
    /// Build-time defaults (Assets/Resources/ServerConfig.asset, created by Bootstrap.CreateScene).
    /// PlayerPrefs written by the Settings screen override them.
    /// </summary>
    [CreateAssetMenu(menuName = "Rogue Stars/Server Config", fileName = "ServerConfig")]
    public sealed class ServerConfig : ScriptableObject
    {
        /// <summary>10.0.2.2 is the host machine from the Android emulator.</summary>
        public string BaseUrl = "http://10.0.2.2:8080";
        public bool OnlineByDefault;
        /// <summary>
        /// Production server (Cloud Run, project rogue-stars-515610). BuildAndroid bakes it in (online, Firebase auth) unless
        /// ROGUESTARS_SERVER_URL names another server or is "local" (keeps BaseUrl, offline, dev auth).
        /// </summary>
        public string ReleaseBaseUrl = DefaultReleaseBaseUrl;
        public const string DefaultReleaseBaseUrl = "https://rogue-stars-server-t72a3eykvq-ew.a.run.app";
        /// <summary>
        /// "dev" (POST /v1/auth/dev; local server, editor) or "firebase" (Firebase ID token: Play Games sign-in with anonymous
        /// fallback; required by the production server). BuildAndroid sets "firebase" with ROGUESTARS_SERVER_URL unless
        /// ROGUESTARS_AUTH=dev. The editor always uses dev auth.
        /// </summary>
        public string AuthMode = "dev";

        public static ServerConfig LoadDefault()
        {
            var cfg = Resources.Load<ServerConfig>("ServerConfig");
            return cfg != null ? cfg : CreateInstance<ServerConfig>();
        }
    }
}
