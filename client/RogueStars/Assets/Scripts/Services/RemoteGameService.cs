using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Client.Services
{
    [Serializable]
    public sealed class RemoteConfig
    {
        /// <summary>e.g. http://10.0.2.2:8080 (Android emulator → host) or the Cloud Run URL.</summary>
        public string BaseUrl = "http://localhost:8080";
        /// <summary>Dev auth device id; the server derives the player id "dev_&lt;deviceId&gt;".</summary>
        public string DeviceId = "editor";
        public string DisplayName = "Worm";
        /// <summary>"dev" or "firebase" (see <see cref="AuthModes"/>); picks the auth provider in the composition root.</summary>
        public string AuthMode = AuthModes.Dev;
        /// <summary>
        /// Local-first commands (default): run locally, show at once, sync in the background (RemoteGameService.LocalFirst).
        /// False = every command waits for the server's state (the pre-local-first protocol; tests of the wire mapping).
        /// </summary>
        public bool LocalFirst = true;
    }

    /// <summary>
    /// Online mode: the server is the store of record (docs/server.md). Commands are local-first (see
    /// RemoteGameService.LocalFirst.cs): applied on the local engine at once, then synced to /v1/command in order; the
    /// server answers with an ack or, when it disagrees, its state, which replaces the local copy.
    /// Auth: an <see cref="IAuthProvider"/> (production: Firebase ID token, refreshed a minute before it expires and forced
    /// after a 401) or, without one, dev auth (POST /v1/auth/dev; local server, editor, tests).
    /// </summary>
    public sealed partial class RemoteGameService : IGameService, IAdRewardService
    {
        /// <summary>Tokens are renewed this long before their expiry.</summary>
        public const long TokenRefreshMarginMs = 60_000;

        private readonly RemoteConfig _config;
        private readonly IHttpTransport _http;
        private readonly IClock _clock;
        private readonly IAuthProvider _auth;
        private GameState _state;
        private string _token;
        private long _tokenExpiresMs;
        private long _serverOffsetMs;

        /// <summary>The auth provider (null = dev auth).</summary>
        public IAuthProvider Auth => _auth;

        public bool IsOnline => true;
        public string ModeName => "online";
        public bool IsConnected => _state != null && _token != null;
        public GameEngine Engine { get; }
        public ContentDb Content { get; }
        public GameState State => _state;
        public long NowMs => _clock.NowMs + _serverOffsetMs;
        public string PlayerId { get; private set; }
        public long Version { get; private set; }
        /// <summary>The server's session flag: Google Play payments are switched off (Iap:Mode=disabled).</summary>
        public bool PurchasesDisabled { get; private set; }
        /// <summary>Client-allowed command names reported by /v1/session.</summary>
        public readonly List<string> AllowedCommands = new List<string>();
        public IGuildApi Guilds { get; }
        public RemoteConfig Config => _config;
        /// <summary>The server's session flag: this player may use the dev tools (/v1/dev/*).</summary>
        public bool DevToolsAvailable { get; private set; }
        public IDevTools DevTools => DevToolsAvailable ? _devTools : null;
        private readonly IDevTools _devTools;

        public event Action StateChanged;

        public RemoteGameService(RemoteConfig config, IHttpTransport transport = null, IClock clock = null, ContentDb content = null,
            IAuthProvider auth = null)
        {
            _config = config ?? new RemoteConfig();
            _auth = auth;
            _http = transport ?? new UnityHttpTransport();
            _clock = clock ?? SystemClock.Instance;
            // Own content instance: the server's session flags (adsDisabled) are applied to it, never to ContentDb.Default.
            Content = content ?? new ContentDb();
            Engine = new GameEngine(Content) { Cloner = ClientJson.Clone };
            Guilds = new RemoteGuildApi(this);
            _devTools = new RemoteDevTools(this);
        }

        // ---- lifecycle --------------------------------------------------------------------------

        public void Connect(Action<ServiceResult> done)
        {
            Authenticate(auth =>
            {
                if (!auth.Ok) { done?.Invoke(auth); return; }
                Call("POST", "/v1/session", new JObject { ["displayName"] = _config.DisplayName }, (json, err) =>
                {
                    if (err != null) { done?.Invoke(ServiceResult.Fail(err)); return; }
                    PlayerId = (string)json["playerId"] ?? PlayerId;
                    AllowedCommands.Clear();
                    if (json["commands"] is JArray cmds) foreach (var c in cmds) AllowedCommands.Add((string)c);
                    // Global ads-disabled mode of the server (Ads:Mode=disabled): every placement is instant for everyone.
                    Content.Shop.AdsDisabled = json.Value<bool?>("adsDisabled") ?? false;
                    // Payments on hold (Iap:Mode=disabled): real-money products are shown as "coming soon".
                    PurchasesDisabled = json.Value<bool?>("purchasesDisabled") ?? false;
                    // Owner dev tools (server allowlist, docs/server.md): Settings shows the Developer card only then.
                    DevToolsAvailable = json.Value<bool?>("devTools") ?? false;
                    ResetQueue(); // a new session: the server's state is the starting point
                    ApplyState(json);
                    done?.Invoke(_state != null ? ServiceResult.Success() : ServiceResult.Fail("session_without_state"));
                });
            });
        }

        private bool TokenUsable => _token != null && (_tokenExpiresMs <= 0 || _clock.NowMs < _tokenExpiresMs - TokenRefreshMarginMs);

        private void Authenticate(Action<ServiceResult> done, bool forceRefresh = false)
        {
            if (!forceRefresh && TokenUsable) { done(ServiceResult.Success()); return; }
            if (_auth != null)
            {
                _auth.GetToken(forceRefresh, r =>
                {
                    if (!r.Ok) { done(ServiceResult.Fail(r.Error ?? "auth_failed")); return; }
                    _token = r.Value.Token;
                    _tokenExpiresMs = r.Value.ExpiresAtMs;
                    if (!string.IsNullOrEmpty(r.Value.PlayerId)) PlayerId = r.Value.PlayerId;
                    done(ServiceResult.Success());
                });
                return;
            }
            Call("POST", "/v1/auth/dev", new JObject { ["deviceId"] = _config.DeviceId }, (json, err) =>
            {
                if (err != null) { done(ServiceResult.Fail(err)); return; }
                _token = (string)json["token"];
                PlayerId = (string)json["playerId"];
                done(_token != null ? ServiceResult.Success() : ServiceResult.Fail("auth_failed"));
            }, auth: false);
        }

        public void Refresh(Action<ServiceResult> done)
        {
            if (_config.LocalFirst && _state != null)
            {
                // Settle time-based state as a local-first command: both copies settle at the same moment (an ack, not the
                // whole state); server-side changes (mail, ad rewards, guild) arrive as a version conflict with the state.
                var settle = new SettleTime();
                ExecuteLocalFirst(settle, r =>
                {
                    if (!r.Ok) { done?.Invoke(ServiceResult.Fail(r.Error)); return; }
                    var p = _queue.Find(x => x.Command == settle);
                    if (p == null) done?.Invoke(ServiceResult.Success());
                    else p.Synced = done ?? (_ => { });
                });
                return;
            }
            Call("GET", "/v1/state", null, (json, err) =>
            {
                if (err != null) { done?.Invoke(ServiceResult.Fail(err)); return; }
                ApplyState(json);
                done?.Invoke(ServiceResult.Success());
            });
        }

        // ---- commands ---------------------------------------------------------------------------

        /// <summary>Wire body of POST /v1/command for a command (args = the command's fields in camelCase).</summary>
        public static JObject CommandBody(GameCommand command, long? expectedVersion = null)
        {
            if (string.IsNullOrEmpty(command.RequestId)) command.RequestId = Guid.NewGuid().ToString("N");
            var args = JObject.FromObject(command, ClientJson.WireSerializer);
            args.Remove("type");
            args.Remove("requestId");
            var body = new JObject { ["type"] = command.Type, ["requestId"] = command.RequestId, ["args"] = args };
            if (expectedVersion.HasValue) body["expectedVersion"] = expectedVersion.Value;
            return body;
        }

        public void Execute(GameCommand command, Action<CommandResult> done)
        {
            if (command == null) { done?.Invoke(CommandResult.Fail("null command")); return; }
            if (ServerOnly.Is(command)) { done?.Invoke(CommandResult.Fail(ServiceErrors.ServerOnlyCommand)); return; }
            if (_config.LocalFirst && !IsServerFirst(command)) { ExecuteLocalFirst(command, done); return; }
            // Server-first: in queue order, busy until the server's state is back.
            Queued(next => Call("POST", "/v1/command", CommandBody(command), (json, err) => { var r = MapCommand(json, err); next(); done?.Invoke(r); }));
        }

        /// <summary>Server-first call: after the local-first queue drained (local-first mode), else at once.</summary>
        private void Queued(Action<Action> call)
        {
            if (_config.LocalFirst) ServerFirst(call);
            else call(() => { });
        }

        public void VerifyPurchase(string productId, string purchaseToken, Action<CommandResult> done)
        {
            var body = new JObject { ["productId"] = productId, ["purchaseToken"] = purchaseToken };
            Queued(next => Call("POST", "/v1/iap/verify", body, (json, err) =>
            {
                if (err == null && json != null && json.Value<bool?>("alreadyCompleted") == true)
                {
                    Call("GET", "/v1/state", null, (s, _) => { ApplyState(s); next(); done?.Invoke(CommandResult.Success()); });
                    return;
                }
                var r = MapCommand(json, err);
                next();
                done?.Invoke(r);
            }));
        }

        /// <summary>Maps a command-shaped response ({ ok, error, events, payload, version, state }).</summary>
        private CommandResult MapCommand(JObject json, string err)
        {
            if (json == null) return CommandResult.Fail(err ?? "empty_response");
            var r = new CommandResult
            {
                Ok = err == null && json.Value<bool?>("ok") == true,
                Error = err ?? (string)json["error"],
                Payload = json["payload"],
            };
            if (json["events"] is JArray evs) r.Events = evs.ToObject<List<GameEvent>>(ClientJson.WireSerializer);
            if (r.Ok) ApplyState(json);
            return r;
        }

        /// <summary>
        /// Adopts the server's state from a response (session, state, server-first calls, local-first disagreements) and
        /// re-runs the local-first commands not sent yet on top of it.
        /// </summary>
        private void ApplyState(JObject json)
        {
            if (json == null) return;
            var st = json["serverTimeMs"];
            if (st != null && st.Type == JTokenType.Integer) _serverOffsetMs = (long)st - _clock.NowMs;
            var v = json["version"];
            if (v != null && v.Type == JTokenType.Integer) Version = (long)v;
            if (json["state"] is JObject stateJson)
            {
                _state = stateJson.ToObject<GameState>(ClientJson.WireSerializer);
                if (_queue.Count > 0) Rebase();
                StateChanged?.Invoke();
            }
        }

        /// <summary>GET /v1/state and adopt it (inside a server-first call: the queue is drained, nothing to re-run).</summary>
        private void Resync(Action then) =>
            Call("GET", "/v1/state", null, (json, err) => { if (err == null) ApplyState(json); then(); });

        // ---- rewarded ads -----------------------------------------------------------------------

        public void AdCredits(Action<ServiceResult<List<AdCreditInfo>>> done) =>
            Get("/v1/ads/credits", json => ((JArray)json["credits"] ?? new JArray()).ToObject<List<AdCreditInfo>>(ClientJson.WireSerializer), done);

        public void InstantAd(string placementId, Action<CommandResult> done)
        {
            var body = new JObject { ["placementId"] = placementId, ["requestId"] = Guid.NewGuid().ToString("N") };
            Queued(next => Call("POST", "/v1/ads/instant", body, (json, err) => { var r = MapCommand(json, err); next(); done?.Invoke(r); }));
        }

        public void SimulateAdCallback(string placementId, string eventId, Action<ServiceResult> done)
        {
            string path = "/v1/ads/ssv?user_id=" + Uri.EscapeDataString(PlayerId ?? "") + "&event_id=" + Uri.EscapeDataString(eventId) +
                          "&custom_data=" + Uri.EscapeDataString(placementId ?? "");
            Call("GET", path, null, (json, err) => done?.Invoke(json != null && json["ok"] != null ? ServiceResult.Success() : ServiceResult.Fail(err ?? "bad_response")), auth: false);
        }

        // ---- arena ------------------------------------------------------------------------------

        public void ArenaOpponents(Action<ServiceResult<List<ArenaOpponent>>> done) =>
            Get("/v1/arena/opponents", json => ((JArray)json["opponents"] ?? new JArray()).ToObject<List<ArenaOpponent>>(ClientJson.WireSerializer), done);

        public void ArenaFight(string opponentId, Action<ServiceResult<ArenaFightOutcome>> done)
        {
            var body = new JObject { ["opponentId"] = opponentId, ["requestId"] = Guid.NewGuid().ToString("N") };
            Queued(next => Call("POST", "/v1/arena/fight", body, (json, err) =>
            {
                if (err != null) { next(); done(ServiceResult<ArenaFightOutcome>.Fail(err)); return; }
                var outcome = json.ToObject<ArenaFightOutcome>(ClientJson.WireSerializer);
                ApplyState(json);
                next();
                done(ServiceResult<ArenaFightOutcome>.Success(outcome));
            }));
        }

        public void ArenaLeaderboard(Action<ServiceResult<ArenaLeaderboard>> done) =>
            Get("/v1/arena/leaderboard", json => json.ToObject<ArenaLeaderboard>(ClientJson.WireSerializer), done);

        // ---- invasions --------------------------------------------------------------------------

        public void Invasions(Action<ServiceResult<List<InvasionInfo>>> done) =>
            Get("/v1/invasions", json => ((JArray)json["invasions"] ?? new JArray()).ToObject<List<InvasionInfo>>(ClientJson.WireSerializer), done);

        public void InvasionAttack(string invasionId, Action<ServiceResult<InvasionAttackOutcome>> done)
        {
            var body = new JObject { ["requestId"] = Guid.NewGuid().ToString("N") };
            Queued(next => Call("POST", "/v1/invasions/" + Uri.EscapeDataString(invasionId) + "/attack", body, (json, err) =>
            {
                if (err != null) { next(); done(ServiceResult<InvasionAttackOutcome>.Fail(err)); return; }
                var outcome = json.ToObject<InvasionAttackOutcome>(ClientJson.WireSerializer);
                ApplyState(json);
                next();
                done(ServiceResult<InvasionAttackOutcome>.Success(outcome));
            }));
        }

        // ---- HTTP plumbing ----------------------------------------------------------------------

        internal void Get<T>(string path, Func<JObject, T> map, Action<ServiceResult<T>> done)
        {
            Call("GET", path, null, (json, err) =>
            {
                if (err != null) { done(ServiceResult<T>.Fail(err)); return; }
                T value;
                try { value = map(json); }
                catch (Exception ex) { done(ServiceResult<T>.Fail("bad_response: " + ex.Message)); return; }
                done(ServiceResult<T>.Success(value));
            });
        }

        /// <summary>
        /// Sends a request and parses the JSON object body. err is non-null for network errors, HTTP ≥ 400
        /// (the body's "error" code) and 200 bodies with ok:false (rule rejections).
        /// A missing or (nearly) expired token is renewed first; a 401 drops the token, re-authenticates once (forcing a
        /// fresh ID token) and retries.
        /// </summary>
        internal void Call(string method, string path, JObject body, Action<JObject, string> done, bool auth = true, bool retried = false,
            int timeoutSeconds = 0)
        {
            var req = new HttpRequest
            {
                Method = method, Url = _config.BaseUrl.TrimEnd('/') + path, Body = body?.ToString(Formatting.None), TimeoutSeconds = timeoutSeconds,
            };
            if (auth)
            {
                if (!TokenUsable && !retried)
                {
                    Authenticate(a => { if (a.Ok) Call(method, path, body, done, true, true, timeoutSeconds); else done(null, a.Error); });
                    return;
                }
                req.Headers["Authorization"] = "Bearer " + _token;
            }
            _http.Send(req, resp =>
            {
                if (resp.NetworkError != null || resp.Status == 0) { done(null, "network_error: " + (resp.NetworkError ?? "no response")); return; }
                JObject json = null;
                if (!string.IsNullOrEmpty(resp.Body))
                {
                    try { json = JObject.Parse(resp.Body); }
                    catch (JsonException) { json = null; }
                }
                if (resp.Status == 401 && auth && !retried)
                {
                    _token = null;
                    Authenticate(a => { if (a.Ok) Call(method, path, body, done, true, true, timeoutSeconds); else done(json, a.Error); }, forceRefresh: true);
                    return;
                }
                if (resp.Status >= 400) { done(json, (json != null ? (string)json["error"] : null) ?? "http_" + resp.Status); return; }
                if (json == null) { done(null, "bad_response"); return; }
                if (json.Value<bool?>("ok") == false) { done(json, (string)json["error"] ?? "rejected"); return; }
                done(json, null);
            });
        }

        // ---- dev tools --------------------------------------------------------------------------

        /// <summary>A 60-day jump simulates a few seconds on the server (plus a cold start): longer than the default timeout.</summary>
        public const int DevToolsTimeoutSeconds = 120;

        private sealed class RemoteDevTools : IDevTools
        {
            private readonly RemoteGameService _s;
            public RemoteDevTools(RemoteGameService s) { _s = s; }

            public void Reset(Action<ServiceResult<DevToolsOutcome>> done) => Post("/v1/dev/reset", new JObject(), done);

            public void Jump(int day, string profile, Action<ServiceResult<DevToolsOutcome>> done) =>
                Post("/v1/dev/jump", new JObject { ["day"] = day, ["profile"] = profile }, done);

            private void Post(string path, JObject body, Action<ServiceResult<DevToolsOutcome>> done) =>
                _s.Queued(next => _s.Call("POST", path, body, (json, err) =>
                {
                    if (err != null) { next(); done(ServiceResult<DevToolsOutcome>.Fail(err)); return; }
                    DevToolsOutcome outcome;
                    try { outcome = json.ToObject<DevToolsOutcome>(ClientJson.WireSerializer); }
                    catch (Exception ex) { next(); done(ServiceResult<DevToolsOutcome>.Fail("bad_response: " + ex.Message)); return; }
                    _s.ResetQueue(); // the whole player was replaced: nothing queued applies any more
                    _s.ApplyState(json);
                    done(ServiceResult<DevToolsOutcome>.Success(outcome));
                }, timeoutSeconds: DevToolsTimeoutSeconds));
        }

        // ---- guilds -----------------------------------------------------------------------------

        private sealed class RemoteGuildApi : IGuildApi
        {
            private readonly RemoteGameService _s;
            public RemoteGuildApi(RemoteGameService s) { _s = s; }

            public void Search(string query, Action<ServiceResult<List<GuildInfo>>> done) =>
                _s.Get("/v1/guilds?query=" + Uri.EscapeDataString(query ?? ""),
                    json => ((JArray)json["guilds"] ?? new JArray()).ToObject<List<GuildInfo>>(ClientJson.WireSerializer), done);

            public void Mine(Action<ServiceResult<MyGuild>> done) =>
                _s.Get("/v1/guilds/mine", json =>
                {
                    var mine = new MyGuild();
                    if (json["guild"] is JObject g) mine.Guild = g.ToObject<GuildInfo>(ClientJson.WireSerializer);
                    mine.Level = json.Value<int?>("level") ?? mine.Guild?.Level ?? 0;
                    if (json["contributions"] is JObject c && c["members"] is JArray m)
                        mine.Members = m.ToObject<List<GuildMemberContribution>>(ClientJson.WireSerializer);
                    return mine;
                }, done);

            public void Create(string name, string description, Action<ServiceResult<GuildInfo>> done) =>
                PostGuild("/v1/guilds", new JObject { ["name"] = name, ["description"] = description ?? "" }, done);

            public void Join(string guildId, Action<ServiceResult<GuildInfo>> done) =>
                PostGuild("/v1/guilds/" + Uri.EscapeDataString(guildId) + "/join", new JObject(), done);

            public void Leave(Action<ServiceResult> done) =>
                _s.Queued(next => _s.Call("POST", "/v1/guilds/leave", new JObject(), (json, err) =>
                {
                    if (err != null) { next(); done(ServiceResult.Fail(err)); return; }
                    _s.Resync(() => { next(); done(ServiceResult.Success()); });
                }));

            public void Chat(long sinceMs, Action<ServiceResult<List<ChatMessage>>> done) =>
                _s.Get("/v1/guilds/chat?since=" + sinceMs,
                    json => ((JArray)json["messages"] ?? new JArray()).ToObject<List<ChatMessage>>(ClientJson.WireSerializer), done);

            public void Post(string text, Action<ServiceResult<ChatMessage>> done) =>
                _s.Call("POST", "/v1/guilds/chat", new JObject { ["text"] = text }, (json, err) =>
                {
                    if (err != null) { done(ServiceResult<ChatMessage>.Fail(err)); return; }
                    done(ServiceResult<ChatMessage>.Success(json["message"]?.ToObject<ChatMessage>(ClientJson.WireSerializer)));
                });

            private void PostGuild(string path, JObject body, Action<ServiceResult<GuildInfo>> done) =>
                _s.Queued(next => _s.Call("POST", path, body, (json, err) =>
                {
                    if (err != null) { next(); done(ServiceResult<GuildInfo>.Fail(err)); return; }
                    var g = json["guild"]?.ToObject<GuildInfo>(ClientJson.WireSerializer);
                    // Membership is applied to the player state by a server hook: re-sync.
                    _s.Resync(() => { next(); done(ServiceResult<GuildInfo>.Success(g)); });
                }));
        }
    }
}
