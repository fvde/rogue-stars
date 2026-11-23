using System;
using UnityEngine;

namespace RogueStars.Client.Services
{
    /// <summary>Per-device client settings persisted in PlayerPrefs.</summary>
    [Serializable]
    public sealed class ClientSettings
    {
        private const string KeyOnline = "rs.online";
        private const string KeyUrl = "rs.serverUrl";
        private const string KeyDevice = "rs.deviceId";
        private const string KeyName = "rs.displayName";
        private const string KeyAutoPick = "rs.autoPickDrafts";

        public bool Online;
        public string ServerUrl;
        public string DeviceId;
        public string DisplayName;
        /// <summary>
        /// Run auto mode "Auto-pick drafts (farming)": off by default. On, auto mode also resolves drafts, events, chests,
        /// campfires, branches, revive (give up) and Burrow (cash out) with their defaults and leaves shops without buying
        /// (RunAutoStep.Farming). Off, auto stops at every decision.
        /// </summary>
        public bool AutoPickDrafts;
        /// <summary>From the build's ServerConfig (not a player setting): "dev" or "firebase".</summary>
        public string AuthMode = AuthModes.Dev;

        public static ClientSettings Load()
        {
            var defaults = ServerConfig.LoadDefault();
            var s = new ClientSettings
            {
                AuthMode = string.IsNullOrEmpty(defaults.AuthMode) ? AuthModes.Dev : defaults.AuthMode,
                Online = PlayerPrefs.GetInt(KeyOnline, defaults.OnlineByDefault ? 1 : 0) == 1,
                ServerUrl = PlayerPrefs.GetString(KeyUrl, defaults.BaseUrl),
                DeviceId = PlayerPrefs.GetString(KeyDevice, ""),
                DisplayName = PlayerPrefs.GetString(KeyName, "Worm"),
                AutoPickDrafts = PlayerPrefs.GetInt(KeyAutoPick, 0) == 1,
            };
            if (string.IsNullOrEmpty(s.DeviceId))
            {
                s.DeviceId = Guid.NewGuid().ToString("N").Substring(0, 12);
                PlayerPrefs.SetString(KeyDevice, s.DeviceId);
                PlayerPrefs.Save();
            }
            return s;
        }

        public void Save()
        {
            PlayerPrefs.SetInt(KeyOnline, Online ? 1 : 0);
            PlayerPrefs.SetString(KeyUrl, ServerUrl ?? "");
            PlayerPrefs.SetString(KeyDevice, DeviceId ?? "");
            PlayerPrefs.SetString(KeyName, DisplayName ?? "Worm");
            PlayerPrefs.SetInt(KeyAutoPick, AutoPickDrafts ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Set and persist only the auto-pick (farming) toggle.</summary>
        public void SetAutoPickDrafts(bool on)
        {
            AutoPickDrafts = on;
            PlayerPrefs.SetInt(KeyAutoPick, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        public RemoteConfig ToRemoteConfig() => new RemoteConfig { BaseUrl = ServerUrl, DeviceId = DeviceId, DisplayName = DisplayName, AuthMode = AuthMode };
    }
}
