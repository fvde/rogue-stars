using System;
using UnityEngine;
#if UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

namespace RogueStars.Client.Services
{
    /// <summary>
    /// Google Play Games Services v2 through the official Google Play Games plugin for Unity (Assets/GooglePlayGames,
    /// v2.3.0). Only used when the plugin is configured: PlayGamesSettings (Assets/GooglePlayGames/Resources) holds the Play
    /// Games app id and the OAuth <b>web</b> client id, and the GooglePlayGamesManifest.androidlib carries the app id — written
    /// by the plugin's setup window or by BuildSecrets from ROGUESTARS_PLAY_GAMES_APP_ID / ROGUESTARS_PLAY_GAMES_WEB_CLIENT_ID
    /// (docs/setup.md). Without them the Play Games SDK is never touched (it would abort the app on a missing app id).
    /// </summary>
    public sealed class GooglePlayGamesSignIn : IPlayGamesSignIn
    {
        public static IPlayGamesSignIn Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var settings = PlayGamesSettings.LoadInstance();
            if (settings != null && !string.IsNullOrEmpty(settings.AppId) && !string.IsNullOrEmpty(settings.WebClientId))
                return new GooglePlayGamesSignIn();
            Debug.Log("[Auth] Play Games not configured (PlayGamesSettings app id / web client id empty): anonymous sign-in only");
#endif
            return new NoPlayGames();
        }

        private GooglePlayGamesSignIn() { }

        public bool IsAvailable => true;

        public void Authenticate(bool interactive, Action<bool> done)
        {
#if UNITY_ANDROID
            if (PlayGamesPlatform.Instance.IsAuthenticated()) { done(true); return; }
            Action<SignInStatus> cb = status =>
            {
                Debug.Log("[Auth] Play Games sign-in: " + status);
                done(status == SignInStatus.Success);
            };
            if (interactive) PlayGamesPlatform.Instance.ManuallyAuthenticate(cb);
            else PlayGamesPlatform.Instance.Authenticate(cb);
#else
            done(false);
#endif
        }

        public void RequestServerAuthCode(Action<string> done)
        {
#if UNITY_ANDROID
            try
            {
                PlayGamesPlatform.Instance.RequestServerSideAccess(false, code => done(code));
            }
            catch (Exception ex)
            {
                Debug.LogError("[Auth] RequestServerSideAccess failed: " + ex.Message);
                done(null);
            }
#else
            done(null);
#endif
        }
    }
}
