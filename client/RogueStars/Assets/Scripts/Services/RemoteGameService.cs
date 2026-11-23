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
    }

    /// <summary>
    /// Online mode: the server is authoritative (docs/server.md). Every command is POSTed to /v1/command with a
    /// requestId and the returned state replaces the local copy. The local engine is only used for queries.
    /// Auth: an <see cref="IAuthProvider"/> (production: Firebase ID token, refreshed a minute before it expires and forced
    /// after a 401) or, without one, dev auth (POST /v1/auth/dev; local server, editor, tests).
    /// </summary>
    public sealed class RemoteGameService : IGameService, IAdRewardService
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
            Call("POST", "/v1/command", CommandBody(command), (json, err) => done?.Invoke(MapCommand(json, err)));
        }

        public void VerifyPurchase(string productId, string purchaseToken, Action<CommandResult> done)
        {
            var body = new JObject { ["productId"] = productId, ["purchaseToken"] = purchaseToken };
            Call("POST", "/v1/iap/verify", body, (json, err) =>
            {
                if (err == null && json != null && json.Value<bool?>("alreadyCompleted") == true)
                {
                    Refresh(_ => done?.Invoke(CommandResult.Success()));
                    return;
                }
                done?.Invoke(MapCommand(json, err));
            });
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
                StateChanged?.Invoke();
            }
        }

        // ---- rewarded ads -----------------------------------------------------------------------

        public void AdCredits(Action<ServiceResult<List<AdCreditInfo>>> done) =>
            Get("/v1/ads/credits", json => ((JArray)json["credits"] ?? new JArray()).ToObject<List<AdCreditInfo>>(ClientJson.WireSerializer), done);

        public void InstantAd(string placementId, Action<CommandResult> done)
        {
            var body = new JObject { ["placementId"] = placementId, ["requestId"] = Guid.NewGuid().ToString("N") };
            Call("POST", "/v1/ads/instant", body, (json, err) => done?.Invoke(MapCommand(json, err)));
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
            Call("POST", "/v1/arena/fight", body, (json, err) =>
            {
                if (err != null) { done(ServiceResult<ArenaFightOutcome>.Fail(err)); return; }
                var outcome = json.ToObject<ArenaFightOutcome>(ClientJson.WireSerializer);
                ApplyState(json);
                done(ServiceResult<ArenaFightOutcome>.Success(outcome));
            });
        }

        public void ArenaLeaderboard(Action<ServiceResult<ArenaLeaderboard>> done) =>
            Get("/v1/arena/leaderboard", json => json.ToObject<ArenaLeaderboard>(ClientJson.WireSerializer), done);

        // ---- invasions --------------------------------------------------------------------------

        public void Invasions(Action<ServiceResult<List<InvasionInfo>>> done) =>
            Get("/v1/invasions", json => ((JArray)json["invasions"] ?? new JArray()).ToObject<List<InvasionInfo>>(ClientJson.WireSerializer), done);

        public void InvasionAttack(string invasionId, Action<ServiceResult<InvasionAttackOutcome>> done)
        {
            var body = new JObject { ["requestId"] = Guid.NewGuid().ToString("N") };
            Call("POST", "/v1/invasions/" + Uri.EscapeDataString(invasionId) + "/attack", body, (json, err) =>
            {
                if (err != null) { done(ServiceResult<InvasionAttackOutcome>.Fail(err)); return; }
                var outcome = json.ToObject<InvasionAttackOutcome>(ClientJson.WireSerializer);
                ApplyState(json);
                done(ServiceResult<InvasionAttackOutcome>.Success(outcome));
            });
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
        internal void Call(string method, string path, JObject body, Action<JObject, string> done, bool auth = true, bool retried = false)
        {
            var req = new HttpRequest { Method = method, Url = _config.BaseUrl.TrimEnd('/') + path, Body = body?.ToString(Formatting.None) };
            if (auth)
            {
                if (!TokenUsable && !retried)
                {
                    Authenticate(a => { if (a.Ok) Call(method, path, body, done, true, true); else done(null, a.Error); });
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
                    Authenticate(a => { if (a.Ok) Call(method, path, body, done, true, true); else done(json, a.Error); }, forceRefresh: true);
                    return;
                }
                if (resp.Status >= 400) { done(json, (json != null ? (string)json["error"] : null) ?? "http_" + resp.Status); return; }
                if (json == null) { done(null, "bad_response"); return; }
                if (json.Value<bool?>("ok") == false) { done(json, (string)json["error"] ?? "rejected"); return; }
                done(json, null);
            });
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
                _s.Call("POST", "/v1/guilds/leave", new JObject(), (json, err) =>
                {
                    if (err != null) { done(ServiceResult.Fail(err)); return; }
                    _s.Refresh(_ => done(ServiceResult.Success()));
                });

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
                _s.Call("POST", path, body, (json, err) =>
                {
                    if (err != null) { done(ServiceResult<GuildInfo>.Fail(err)); return; }
                    var g = json["guild"]?.ToObject<GuildInfo>(ClientJson.WireSerializer);
                    // Membership is applied to the player state by a server hook: re-sync.
                    _s.Refresh(_ => done(ServiceResult<GuildInfo>.Success(g)));
                });
        }
    }
}
