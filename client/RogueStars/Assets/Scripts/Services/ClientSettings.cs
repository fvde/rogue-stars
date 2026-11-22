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

        public bool Online;
        public string ServerUrl;
        public string DeviceId;
        public string DisplayName;

        public static ClientSettings Load()
        {
            var defaults = ServerConfig.LoadDefault();
            var s = new ClientSettings
            {
                Online = PlayerPrefs.GetInt(KeyOnline, defaults.OnlineByDefault ? 1 : 0) == 1,
                ServerUrl = PlayerPrefs.GetString(KeyUrl, defaults.BaseUrl),
                DeviceId = PlayerPrefs.GetString(KeyDevice, ""),
                DisplayName = PlayerPrefs.GetString(KeyName, "Worm"),
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
            PlayerPrefs.Save();
        }

        public RemoteConfig ToRemoteConfig() => new RemoteConfig { BaseUrl = ServerUrl, DeviceId = DeviceId, DisplayName = DisplayName };
    }
}
