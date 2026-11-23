using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using RogueStars.Client.Services;
using RogueStars.Rules.Core;

namespace RogueStars.Client.Tests
{
    /// <summary>In-memory stand-in for the Firebase Auth SDK: users by uid, Play Games accounts by player, JWT-shaped tokens.</summary>
    internal sealed class FakeFirebase : IFirebaseAuthBackend
    {
        public readonly List<string> Calls = new List<string>();
        /// <summary>Play Games player → Firebase uid of the user it is linked to (pre-existing accounts).</summary>
        public readonly Dictionary<string, string> PlayGamesOwners = new Dictionary<string, string>();
        public bool FailInit;
        public bool FailAnonymous;
        public long TokenLifetimeMs = 3_600_000;
        public Func<long> Now = () => TestUtil.T0;
        public int TokensIssued;
        private FirebaseUserInfo _user;
        private int _nextUid;

        public FirebaseUserInfo CurrentUser => _user;

        public void Initialize(Action<ServiceResult> done)
        {
            Calls.Add("init");
            done(FailInit ? ServiceResult.Fail("no google-services.json") : ServiceResult.Success());
        }

        public void SignInAnonymously(Action<ServiceResult> done)
        {
            Calls.Add("anonymous");
            if (FailAnonymous) { done(ServiceResult.Fail("firebase_OperationNotAllowed")); return; }
            _user = new FirebaseUserInfo { Uid = "anon" + (++_nextUid), IsAnonymous = true };
            done(ServiceResult.Success());
        }

        public void SignInWithPlayGames(string code, Action<ServiceResult> done)
        {
            Calls.Add("signin:" + code);
            string player = PlayerOf(code);
            if (player == null) { done(ServiceResult.Fail("firebase_InvalidCredential")); return; }
            if (!PlayGamesOwners.TryGetValue(player, out var uid)) PlayGamesOwners[player] = uid = "pgs" + (++_nextUid);
            _user = new FirebaseUserInfo { Uid = uid, IsAnonymous = false, HasPlayGames = true };
            done(ServiceResult.Success());
        }

        public void LinkPlayGames(string code, Action<ServiceResult<bool>> done)
        {
            Calls.Add("link:" + code);
            string player = PlayerOf(code);
            if (player == null) { done(ServiceResult<bool>.Fail("firebase_InvalidCredential")); return; }
            if (PlayGamesOwners.TryGetValue(player, out var owner) && owner != _user.Uid)
            {
                _user = new FirebaseUserInfo { Uid = owner, IsAnonymous = false, HasPlayGames = true }; // switched
                done(ServiceResult<bool>.Success(false));
                return;
            }
            PlayGamesOwners[player] = _user.Uid;
            _user = new FirebaseUserInfo { Uid = _user.Uid, IsAnonymous = false, HasPlayGames = true };
            done(ServiceResult<bool>.Success(true));
        }

        public void GetIdToken(bool forceRefresh, Action<ServiceResult<string>> done)
        {
            Calls.Add(forceRefresh ? "token!" : "token");
            if (_user == null) { done(ServiceResult<string>.Fail("no_user")); return; }
            TokensIssued++;
            done(ServiceResult<string>.Success(Token(_user.Uid, (Now() + TokenLifetimeMs) / 1000, TokensIssued)));
        }

        public void SignOut() { Calls.Add("signout"); _user = null; }

        public static string Token(string uid, long expSeconds, int n)
        {
            string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return B64("{\"alg\":\"RS256\"}") + "." + B64("{\"sub\":\"" + uid + "\",\"exp\":" + expSeconds + ",\"n\":" + n + "}") + ".sig";
        }

        private static string PlayerOf(string code) => code != null && code.StartsWith("code-") ? code.Substring(5).Split('#')[0] : null;
    }

    internal sealed class FakePlayGames : IPlayGamesSignIn
    {
        public bool IsAvailable { get; set; } = true;
        /// <summary>Play Games player signed in on the device (null = sign-in declined / no account).</summary>
        public string Player = "gamer1";
        public bool SilentSignIn = true;
        public readonly List<string> Calls = new List<string>();
        private int _codes;

        public void Authenticate(bool interactive, Action<bool> done)
        {
            Calls.Add(interactive ? "manual" : "auto");
            done(Player != null && (interactive || SilentSignIn));
        }

        public void RequestServerAuthCode(Action<string> done)
        {
            Calls.Add("code");
            done(Player == null ? null : "code-" + Player + "#" + (++_codes)); // one-time codes
        }
    }

    /// <summary>
    /// Firebase sign-in (Play Games, anonymous fallback, linking) and the online flow with Firebase ID tokens, against a fake
    /// Firebase SDK and a fake server transport.
    /// </summary>
    public sealed class FirebaseAuthTests
    {
        private FakeFirebase _fb;
        private FakePlayGames _pgs;
        private FirebaseAuthProvider _auth;

        [SetUp]
        public void SetUp()
        {
            _fb = new FakeFirebase();
            _pgs = new FakePlayGames();
            _auth = new FirebaseAuthProvider(_fb, _pgs);
        }

        private AuthToken Token(bool force = false)
        {
            ServiceResult<AuthToken> r = null;
            _auth.GetToken(force, x => r = x);
            Assert.That(r, Is.Not.Null, "synchronous fakes answer immediately");
            Assert.That(r.Ok, Is.True, r.Error);
            return r.Value;
        }

        [Test]
        public void PlayGames_ServerAuthCode_SignsInWithTheCredential()
        {
            var t = Token();
            Assert.That(_pgs.Calls, Is.EqualTo(new[] { "auto", "code" }));
            Assert.That(_fb.Calls, Is.EqualTo(new[] { "init", "signin:code-gamer1#1", "token" }));
            Assert.That(_auth.User.IsAnonymous, Is.False);
            Assert.That(_auth.User.HasPlayGames, Is.True);
            Assert.That(t.PlayerId, Is.EqualTo(_auth.User.Uid));
            Assert.That(t.ExpiresAtMs, Is.EqualTo(TestUtil.T0 + 3_600_000), "exp claim of the ID token");

            // Signed in: later tokens come straight from the SDK (cached/refreshed there), no new Play Games round trip.
            Token();
            Token(force: true);
            Assert.That(_pgs.Calls.Count, Is.EqualTo(2));
            Assert.That(_fb.Calls.Skip(3), Is.EqualTo(new[] { "token", "token!" }));
        }

        [Test]
        public void NoPlayGames_FallsBackToAnonymous()
        {
            _pgs.IsAvailable = false;
            Token();
            Assert.That(_fb.Calls, Is.EqualTo(new[] { "init", "anonymous", "token" }));
            Assert.That(_auth.User.IsAnonymous, Is.True);
            Assert.That(_pgs.Calls, Is.Empty, "an unconfigured Play Games plugin is never touched");
        }

        [Test]
        public void PlayGamesDeclined_FallsBackToAnonymous_AndAnonymousCanBeRequired()
        {
            _pgs.Player = null;
            Token();
            Assert.That(_auth.User.IsAnonymous, Is.True);

            var strict = new FirebaseAuthProvider(new FakeFirebase(), new FakePlayGames { Player = null }) { AllowAnonymous = false };
            ServiceResult<AuthToken> r = null;
            strict.GetToken(false, x => r = x);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Error, Is.EqualTo(AuthErrors.SignInRequired));
        }

        [Test]
        public void FirebaseUnavailable_FailsWithoutSigningIn()
        {
            _fb.FailInit = true;
            ServiceResult<AuthToken> r = null;
            _auth.GetToken(false, x => r = x);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Error, Does.StartWith(AuthErrors.Unavailable));
            Assert.That(_pgs.Calls, Is.Empty);
        }

        [Test]
        public void AnonymousUser_IsLinkedToPlayGames_KeepingTheUid()
        {
            _pgs.SilentSignIn = false; // first start: no silent Play Games sign-in
            Token();
            string anonUid = _auth.User.Uid;
            Assert.That(_auth.User.IsAnonymous, Is.True);

            // Next app session: Play Games signs in silently → the anonymous account is upgraded, same uid (= player id).
            _pgs.SilentSignIn = true;
            var next = new FirebaseAuthProvider(_fb, _pgs);
            ServiceResult<AuthToken> r = null;
            next.GetToken(false, x => r = x);
            Assert.That(r.Ok, Is.True, r.Error);
            Assert.That(_fb.Calls, Does.Contain("link:code-gamer1#1"));
            Assert.That(next.User.Uid, Is.EqualTo(anonUid));
            Assert.That(next.User.IsAnonymous, Is.False);
            Assert.That(r.Value.PlayerId, Is.EqualTo(anonUid));
            Assert.That(next.LastEvent, Is.EqualTo("linked Play Games"));
        }

        [Test]
        public void ManualLink_WhenThePlayGamesAccountAlreadyHasAPlayer_SwitchesToIt()
        {
            _pgs.SilentSignIn = false;
            _fb.PlayGamesOwners["gamer1"] = "existing-uid";
            Token();
            Assert.That(_auth.User.IsAnonymous, Is.True);
            ServiceResult done = null;
            _auth.LinkPlayGames(r => done = r);
            Assert.That(done.Ok, Is.True, done.Error);
            Assert.That(_pgs.Calls.Last(), Is.EqualTo("code"));
            Assert.That(_pgs.Calls, Does.Contain("manual"), "the Settings button signs in interactively");
            Assert.That(_auth.User.Uid, Is.EqualTo("existing-uid"));
            Assert.That(_auth.LastEvent, Does.Contain("switched"));
        }

        [Test]
        public void Jwt_ExpiryIsReadFromTheExpClaim()
        {
            Assert.That(Jwt.ExpiresAtMs(FakeFirebase.Token("u", 1_800_000_000, 1)), Is.EqualTo(1_800_000_000_000));
            Assert.That(Jwt.ExpiresAtMs("not-a-jwt"), Is.EqualTo(0));
        }

        // ---- RemoteGameService with Firebase ID tokens ----------------------------------------------------------------

        private sealed class Server
        {
            public readonly FakeTransport Http = new FakeTransport();
            public readonly ManualClock Clock = new ManualClock(TestUtil.T0);
            public readonly GameState State = new GameEngine().NewPlayer("uid", TestUtil.T0, 5);
            /// <summary>Tokens the server rejects (revoked/expired) with 401.</summary>
            public readonly HashSet<string> Rejected = new HashSet<string>();
            public readonly List<string> Bearers = new List<string>();

            public Server()
            {
                Http.Handler = req =>
                {
                    if (req.Url.Contains("/v1/auth/dev")) return FakeTransport.Json(404, "{\"ok\":false,\"error\":\"not_found\"}"); // production: dev auth off
                    req.Headers.TryGetValue("Authorization", out var bearer);
                    Bearers.Add(bearer);
                    if (bearer == null || Rejected.Contains(bearer.Substring(7))) return FakeTransport.Json(401, "{\"ok\":false,\"error\":\"invalid_token\"}");
                    string wire = JsonConvert.SerializeObject(State, ClientJson.Wire);
                    return FakeTransport.Json(200, "{\"ok\":true,\"playerId\":\"uid\",\"serverTimeMs\":" + TestUtil.T0 + ",\"version\":1,\"adsDisabled\":true,\"state\":" + wire + "}");
                };
            }
        }

        [Test]
        public void Online_UsesTheFirebaseIdToken_NeverDevAuth_AndRefreshesBeforeExpiry()
        {
            var server = new Server();
            _fb.Now = () => server.Clock.NowMs;
            var svc = new RemoteGameService(new RemoteConfig { BaseUrl = "https://rs.example", AuthMode = AuthModes.Firebase }, server.Http, server.Clock, auth: _auth);
            ServiceResult connected = null;
            svc.Connect(r => connected = r);
            Assert.That(connected.Ok, Is.True, connected.Error);
            Assert.That(server.Http.Requests.Any(r => r.Url.Contains("/v1/auth/dev")), Is.False);
            string first = server.Bearers.Last();
            Assert.That(first, Does.StartWith("Bearer "));
            Assert.That(Jwt.ExpiresAtMs(first.Substring(7)), Is.EqualTo(TestUtil.T0 + 3_600_000));

            // Within the token's lifetime: reused.
            server.Clock.Advance(30 * 60_000);
            svc.Refresh(_ => { });
            Assert.That(server.Bearers.Last(), Is.EqualTo(first));
            Assert.That(_fb.TokensIssued, Is.EqualTo(1));

            // A minute before expiry: a new token is fetched first (not forced: the SDK refreshes an expiring token itself).
            server.Clock.Advance(29 * 60_000 + 1);
            svc.Refresh(_ => { });
            Assert.That(_fb.TokensIssued, Is.EqualTo(2));
            Assert.That(_fb.Calls.Last(), Is.EqualTo("token"));
            Assert.That(server.Bearers.Last(), Is.Not.EqualTo(first));
        }

        [Test]
        public void Online_401_ForcesAFreshIdToken_AndRetriesOnce()
        {
            var server = new Server();
            _fb.Now = () => server.Clock.NowMs;
            var svc = new RemoteGameService(new RemoteConfig { BaseUrl = "https://rs.example" }, server.Http, server.Clock, auth: _auth);
            svc.Connect(_ => { });
            string revoked = server.Bearers.Last().Substring(7);
            server.Rejected.Add(revoked);
            ServiceResult r = null;
            svc.Refresh(x => r = x);
            Assert.That(r.Ok, Is.True, r.Error);
            Assert.That(_fb.Calls.Last(), Is.EqualTo("token!"), "forced refresh after the 401");
            Assert.That(server.Bearers.Last().Substring(7), Is.Not.EqualTo(revoked));

            // A token the server keeps rejecting: one retry, then the error surfaces (no loop).
            server.Rejected.Add(server.Bearers.Last().Substring(7));
            _fb.TokenLifetimeMs = 3_600_000;
            server.Http.Handler = req => FakeTransport.Json(401, "{\"ok\":false,\"error\":\"invalid_token\"}");
            int before = server.Http.Requests.Count;
            svc.Refresh(x => r = x);
            Assert.That(r.Ok, Is.False);
            Assert.That(server.Http.Requests.Count - before, Is.EqualTo(2));
        }

        [Test]
        public void Online_SignInFailure_IsAConnectionError()
        {
            var server = new Server();
            _fb.FailInit = true;
            var svc = new RemoteGameService(new RemoteConfig { BaseUrl = "https://rs.example" }, server.Http, server.Clock, auth: _auth);
            ServiceResult r = null;
            svc.Connect(x => r = x);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Error, Does.StartWith(AuthErrors.Unavailable));
            Assert.That(server.Http.Requests, Is.Empty);
        }

        [Test]
        public void AuthFactory_UsesDevAuthInTheEditor()
        {
            Assert.That(AuthFactory.For(AuthModes.Firebase), Is.Null, "editor: dev auth (no Firebase desktop SDK use)");
            Assert.That(AuthFactory.For(AuthModes.Dev), Is.Null);
            Assert.That(ServerConfig.LoadDefault().AuthMode, Is.EqualTo(AuthModes.Dev), "committed default: dev; release builds inject firebase");
        }
    }
}
