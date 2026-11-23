using System;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RogueStars.Client.Services
{
    /// <summary>A bearer token for the game server.</summary>
    public sealed class AuthToken
    {
        public string Token;
        /// <summary>Player id the server will derive (Firebase uid); informational, the session response is authoritative.</summary>
        public string PlayerId;
        /// <summary>Token expiry (Unix ms; 0 = unknown). RemoteGameService refreshes a minute before.</summary>
        public long ExpiresAtMs;
    }

    /// <summary>
    /// Produces bearer tokens for <see cref="RemoteGameService"/>. Without a provider the service uses dev auth
    /// (<c>POST /v1/auth/dev</c>: editor, local server, tests). Production: <see cref="FirebaseAuthProvider"/>.
    /// </summary>
    public interface IAuthProvider
    {
        string Name { get; }
        /// <summary>Signs in if needed and returns a current ID token; <paramref name="forceRefresh"/> after the server answered 401.</summary>
        void GetToken(bool forceRefresh, Action<ServiceResult<AuthToken>> done);
    }

    public static class AuthModes
    {
        /// <summary>POST /v1/auth/dev (dev server only; refused by production).</summary>
        public const string Dev = "dev";
        /// <summary>Firebase Auth ID token (Play Games sign-in, anonymous fallback).</summary>
        public const string Firebase = "firebase";
    }

    public static class AuthErrors
    {
        public const string CredentialInUse = "credential_in_use";
        public const string Unavailable = "auth_unavailable";
        public const string SignInRequired = "sign_in_required";
    }

    /// <summary>Firebase user as the auth flow needs it.</summary>
    public sealed class FirebaseUserInfo
    {
        public string Uid;
        public bool IsAnonymous;
        /// <summary>A Play Games credential is linked to this user (provider id "playgames.google.com").</summary>
        public bool HasPlayGames;
    }

    /// <summary>The Firebase Auth SDK operations used by <see cref="FirebaseAuthProvider"/> (real: FirebaseSdkBackend; tests: a fake).</summary>
    public interface IFirebaseAuthBackend
    {
        /// <summary>FirebaseApp.CheckAndFixDependenciesAsync + default app from google-services.json.</summary>
        void Initialize(Action<ServiceResult> done);
        FirebaseUserInfo CurrentUser { get; }
        void SignInAnonymously(Action<ServiceResult> done);
        /// <summary>SignInWithCredentialAsync(PlayGamesAuthProvider.GetCredential(serverAuthCode)).</summary>
        void SignInWithPlayGames(string serverAuthCode, Action<ServiceResult> done);
        /// <summary>
        /// Links the Play Games credential to the current (anonymous) user — same uid, progress kept. If the Play Games
        /// account already belongs to another Firebase user, signs in as that user instead and fails with
        /// <see cref="AuthErrors.CredentialInUse"/> only when that is impossible.
        /// </summary>
        void LinkPlayGames(string serverAuthCode, Action<ServiceResult<bool>> done);
        /// <summary>user.TokenAsync(forceRefresh): cached until shortly before expiry, then refreshed by the SDK.</summary>
        void GetIdToken(bool forceRefresh, Action<ServiceResult<string>> done);
        void SignOut();
    }

    /// <summary>Google Play Games Services v2 sign-in (real: GooglePlayGamesSignIn on Android; elsewhere unavailable).</summary>
    public interface IPlayGamesSignIn
    {
        /// <summary>Plugin present and configured (Play Games app id + web client id in PlayGamesSettings).</summary>
        bool IsAvailable { get; }
        /// <summary>PlayGamesPlatform.Instance.Authenticate (automatic, silent) or ManuallyAuthenticate (interactive).</summary>
        void Authenticate(bool interactive, Action<bool> done);
        /// <summary>PlayGamesPlatform.Instance.RequestServerSideAccess(false, code): one-time server auth code, null on failure.</summary>
        void RequestServerAuthCode(Action<string> done);
    }

    public sealed class NoPlayGames : IPlayGamesSignIn
    {
        public bool IsAvailable => false;
        public void Authenticate(bool interactive, Action<bool> done) => done(false);
        public void RequestServerAuthCode(Action<string> done) => done(null);
    }

    /// <summary>
    /// Firebase Auth for the production server (<c>Auth:Mode=firebase</c>): the server verifies the Firebase ID token and uses
    /// the uid as player id.
    /// <list type="number">
    /// <item>No user yet: Play Games (v2 automatic sign-in) → <c>RequestServerSideAccess</c> → server auth code →
    /// <c>PlayGamesAuthProvider.GetCredential(code)</c> → <c>SignInWithCredentialAsync</c>. If Play Games is unavailable or
    /// declined: anonymous sign-in (testing, devices without Play Games).</item>
    /// <item>Anonymous user and Play Games available: once per app session the anonymous account is linked to Play Games
    /// (same uid, progress kept); if that Play Games account already has a player, the client switches to it.</item>
    /// <item>The ID token is cached by the SDK and refreshed on expiry; after a 401 it is force-refreshed.</item>
    /// </list>
    /// </summary>
    public sealed class FirebaseAuthProvider : IAuthProvider
    {
        private readonly IFirebaseAuthBackend _fb;
        private readonly IPlayGamesSignIn _pgs;
        private bool _initialized;
        private bool _linkTried;

        /// <summary>Fall back to an anonymous account when Play Games sign-in is unavailable or fails.</summary>
        public bool AllowAnonymous = true;

        public FirebaseAuthProvider(IFirebaseAuthBackend backend, IPlayGamesSignIn playGames = null)
        {
            _fb = backend ?? throw new ArgumentNullException(nameof(backend));
            _pgs = playGames ?? new NoPlayGames();
        }

        public string Name => AuthModes.Firebase;
        public FirebaseUserInfo User => _initialized ? _fb.CurrentUser : null;
        public bool PlayGamesAvailable => _pgs.IsAvailable;
        /// <summary>Last sign-in step, for diagnostics and the Settings screen.</summary>
        public string LastEvent { get; private set; } = "";

        public void GetToken(bool forceRefresh, Action<ServiceResult<AuthToken>> done)
        {
            Initialize(init =>
            {
                if (!init.Ok) { done(ServiceResult<AuthToken>.Fail(init.Error)); return; }
                EnsureUser(user =>
                {
                    if (!user.Ok) { done(ServiceResult<AuthToken>.Fail(user.Error)); return; }
                    _fb.GetIdToken(forceRefresh, t =>
                    {
                        if (!t.Ok || string.IsNullOrEmpty(t.Value)) { done(ServiceResult<AuthToken>.Fail(t.Error ?? "no_id_token")); return; }
                        done(ServiceResult<AuthToken>.Success(new AuthToken { Token = t.Value, PlayerId = _fb.CurrentUser?.Uid, ExpiresAtMs = Jwt.ExpiresAtMs(t.Value) }));
                    });
                });
            });
        }

        /// <summary>Settings "Link Play Games": interactive Play Games sign-in, then link (or switch to the existing account).</summary>
        public void LinkPlayGames(Action<ServiceResult> done)
        {
            Initialize(init =>
            {
                if (!init.Ok) { done(init); return; }
                if (!_pgs.IsAvailable) { done(ServiceResult.Fail(AuthErrors.Unavailable)); return; }
                PlayGamesCode(true, code =>
                {
                    if (code == null) { done(ServiceResult.Fail("play_games_sign_in_failed")); return; }
                    var u = _fb.CurrentUser;
                    if (u == null) { _fb.SignInWithPlayGames(code, r => { Note(r.Ok ? "signed in with Play Games" : "Play Games sign-in failed: " + r.Error); done(r); }); return; }
                    _fb.LinkPlayGames(code, r => { Note(LinkNote(r)); done(r.Ok ? ServiceResult.Success() : ServiceResult.Fail(r.Error)); });
                });
            });
        }

        private void Initialize(Action<ServiceResult> done)
        {
            if (_initialized) { done(ServiceResult.Success()); return; }
            _fb.Initialize(r =>
            {
                _initialized = r.Ok;
                done(r.Ok ? r : ServiceResult.Fail(AuthErrors.Unavailable + ": " + r.Error));
            });
        }

        private void EnsureUser(Action<ServiceResult> done)
        {
            var user = _fb.CurrentUser;
            if (user != null && !user.IsAnonymous) { done(ServiceResult.Success()); return; }
            if (user != null)
            {
                // Anonymous: try to upgrade to Play Games once per app session; stay anonymous otherwise.
                if (_linkTried || !_pgs.IsAvailable) { done(ServiceResult.Success()); return; }
                _linkTried = true;
                PlayGamesCode(false, code =>
                {
                    if (code == null) { done(ServiceResult.Success()); return; }
                    _fb.LinkPlayGames(code, r => { Note(LinkNote(r)); done(ServiceResult.Success()); });
                });
                return;
            }
            if (!_pgs.IsAvailable) { Anonymous(done); return; }
            _linkTried = true;
            PlayGamesCode(false, code =>
            {
                if (code == null) { Anonymous(done); return; }
                _fb.SignInWithPlayGames(code, r =>
                {
                    if (r.Ok) { Note("signed in with Play Games"); done(r); return; }
                    Note("Play Games sign-in failed: " + r.Error);
                    Anonymous(done);
                });
            });
        }

        private void Anonymous(Action<ServiceResult> done)
        {
            if (!AllowAnonymous) { done(ServiceResult.Fail(AuthErrors.SignInRequired)); return; }
            _fb.SignInAnonymously(r => { Note(r.Ok ? "signed in anonymously" : "anonymous sign-in failed: " + r.Error); done(r); });
        }

        private void PlayGamesCode(bool interactive, Action<string> done)
        {
            _pgs.Authenticate(interactive, ok =>
            {
                if (!ok) { Note("Play Games not signed in"); done(null); return; }
                _pgs.RequestServerAuthCode(code => done(string.IsNullOrEmpty(code) ? null : code));
            });
        }

        private static string LinkNote(ServiceResult<bool> r) =>
            !r.Ok ? "Play Games link failed: " + r.Error : r.Value ? "linked Play Games" : "switched to the existing Play Games account";

        private void Note(string message)
        {
            LastEvent = message;
            Debug.Log("[Auth] " + message);
        }
    }

    /// <summary>Reads the <c>exp</c> claim of a JWT (no verification; the server verifies).</summary>
    public static class Jwt
    {
        public static long ExpiresAtMs(string token)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length < 2) return 0;
                string b64 = parts[1].Replace('-', '+').Replace('_', '/');
                b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
                var payload = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
                long? exp = payload.Value<long?>("exp");
                return exp.HasValue ? exp.Value * 1000 : 0;
            }
            catch (Exception) { return 0; }
        }
    }
}
