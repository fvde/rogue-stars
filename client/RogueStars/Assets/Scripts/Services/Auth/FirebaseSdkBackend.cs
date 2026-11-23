using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using UnityEngine;

namespace RogueStars.Client.Services
{
    /// <summary>
    /// <see cref="IFirebaseAuthBackend"/> on the Firebase Unity SDK (com.google.firebase.auth 14.2.0, Auth module only). The app
    /// configuration comes from google-services.json (Firebase generates Android resources from it at build time). Every
    /// continuation runs on the Unity main thread (ContinueWithOnMainThread).
    /// </summary>
    public sealed class FirebaseSdkBackend : IFirebaseAuthBackend
    {
        private FirebaseAuth _auth;

        public void Initialize(Action<ServiceResult> done)
        {
            if (_auth != null) { done(ServiceResult.Success()); return; }
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(t =>
            {
                if (t.IsFaulted || t.IsCanceled) { done(ServiceResult.Fail(Describe(t.Exception))); return; }
                if (t.Result != DependencyStatus.Available) { done(ServiceResult.Fail("firebase_dependencies_" + t.Result)); return; }
                try
                {
                    _auth = FirebaseAuth.DefaultInstance;
                    done(ServiceResult.Success());
                }
                catch (Exception ex)
                {
                    // Typically a missing google-services.json at build time.
                    Debug.LogError("[Auth] Firebase init failed: " + ex.Message);
                    done(ServiceResult.Fail("firebase_init_failed"));
                }
            });
        }

        public FirebaseUserInfo CurrentUser
        {
            get
            {
                var u = _auth?.CurrentUser;
                if (u == null) return null;
                bool pgs = false;
                foreach (var p in u.ProviderData) if (p.ProviderId == PlayGamesAuthProvider.ProviderId) pgs = true;
                return new FirebaseUserInfo { Uid = u.UserId, IsAnonymous = u.IsAnonymous, HasPlayGames = pgs };
            }
        }

        public void SignInAnonymously(Action<ServiceResult> done) =>
            _auth.SignInAnonymouslyAsync().ContinueWithOnMainThread(t => done(t.IsFaulted || t.IsCanceled ? ServiceResult.Fail(Describe(t.Exception)) : ServiceResult.Success()));

        public void SignInWithPlayGames(string serverAuthCode, Action<ServiceResult> done) =>
            _auth.SignInWithCredentialAsync(PlayGamesAuthProvider.GetCredential(serverAuthCode))
                .ContinueWithOnMainThread(t => done(t.IsFaulted || t.IsCanceled ? ServiceResult.Fail(Describe(t.Exception)) : ServiceResult.Success()));

        public void LinkPlayGames(string serverAuthCode, Action<ServiceResult<bool>> done)
        {
            var user = _auth.CurrentUser;
            if (user == null) { done(ServiceResult<bool>.Fail("no_user")); return; }
            user.LinkWithCredentialAsync(PlayGamesAuthProvider.GetCredential(serverAuthCode)).ContinueWithOnMainThread(t =>
            {
                if (!t.IsFaulted && !t.IsCanceled) { done(ServiceResult<bool>.Success(true)); return; }
                // The Play Games account already belongs to another Firebase user (e.g. after a reinstall): switch to that
                // user with the credential Firebase hands back; the anonymous account stays behind.
                var linkEx = Find<FirebaseAccountLinkException>(t.Exception);
                var updated = linkEx?.UserInfo?.UpdatedCredential;
                if (updated != null && updated.IsValid())
                {
                    _auth.SignInWithCredentialAsync(updated).ContinueWithOnMainThread(s =>
                        done(s.IsFaulted || s.IsCanceled ? ServiceResult<bool>.Fail(Describe(s.Exception)) : ServiceResult<bool>.Success(false)));
                    return;
                }
                done(ServiceResult<bool>.Fail(linkEx != null ? AuthErrors.CredentialInUse : Describe(t.Exception)));
            });
        }

        public void GetIdToken(bool forceRefresh, Action<ServiceResult<string>> done)
        {
            var user = _auth?.CurrentUser;
            if (user == null) { done(ServiceResult<string>.Fail("no_user")); return; }
            user.TokenAsync(forceRefresh).ContinueWithOnMainThread(t =>
                done(t.IsFaulted || t.IsCanceled ? ServiceResult<string>.Fail(Describe(t.Exception)) : ServiceResult<string>.Success(t.Result)));
        }

        public void SignOut() => _auth?.SignOut();

        private static T Find<T>(AggregateException ex) where T : Exception
        {
            if (ex == null) return null;
            foreach (var inner in ex.Flatten().InnerExceptions)
                if (inner is T match) return match;
            return null;
        }

        private static string Describe(AggregateException ex)
        {
            var fb = Find<FirebaseException>(ex);
            if (fb != null) return "firebase_" + (AuthError)fb.ErrorCode;
            return ex?.Flatten().InnerException?.Message ?? "cancelled";
        }
    }
}
