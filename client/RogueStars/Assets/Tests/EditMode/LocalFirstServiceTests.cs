using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RogueStars.Client.Screens;
using RogueStars.Client.Services;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Local-first commands (RemoteGameService.LocalFirst) against an in-test server that speaks the real protocol with the
    /// shared rules engine: ack on agreement, state on disagreement, version conflicts, claims, retries. Plus the client-runtime
    /// half of the battle determinism check and the round pill.
    /// </summary>
    public sealed class LocalFirstServiceTests
    {
        /// <summary>Holds responses until <see cref="Flush"/> (a real network is asynchronous).</summary>
        private sealed class DeferredTransport : IHttpTransport
        {
            public readonly List<HttpRequest> Requests = new List<HttpRequest>();
            public readonly List<(HttpRequest Req, Action<HttpResponse> Done)> Waiting = new List<(HttpRequest, Action<HttpResponse>)>();
            public Func<HttpRequest, HttpResponse> Handler;
            public bool Defer;

            public void Send(HttpRequest request, Action<HttpResponse> done)
            {
                Requests.Add(request);
                if (Defer) Waiting.Add((request, done));
                else done(Handler(request));
            }

            /// <summary>Answers the waiting requests (and the ones they trigger) until none is left.</summary>
            public int Flush()
            {
                int n = 0;
                while (Waiting.Count > 0)
                {
                    var w = Waiting[0];
                    Waiting.RemoveAt(0);
                    w.Done(Handler(w.Req));
                    n++;
                }
                return n;
            }
        }

        /// <summary>The server protocol of /v1/command with the rules engine (CommandService + BattleAuditService, sampling all).</summary>
        private sealed class ProtocolServer
        {
            public readonly GameEngine Engine = new GameEngine(new ContentDb()) { Cloner = ClientJson.Clone };
            public GameState State;
            public long NowMs = TestUtil.T0;
            public bool NetworkDown;
            public Func<GameState, GameState> Interfere; // a server-side change before the next command (mail, hook)
            public string RejectWith;
            public int Acks, FullStates, Commands;
            public readonly List<JObject> Bodies = new List<JObject>();

            public ProtocolServer() { State = Engine.NewPlayer("dev_lf", TestUtil.T0, 4242); State.Flags.Add(Features.DevUnlockAllFlag); }

            private string Wire(GameState s) => JsonConvert.SerializeObject(s, ClientJson.Wire);

            public HttpResponse Handle(HttpRequest req)
            {
                if (NetworkDown) return new HttpResponse { Status = 0, NetworkError = "Cannot connect" };
                if (req.Url.EndsWith("/v1/auth/dev")) return FakeTransport.Json(200, "{\"token\":\"tok\",\"playerId\":\"dev_lf\"}");
                if (req.Url.EndsWith("/v1/session") || req.Url.EndsWith("/v1/state"))
                    return FakeTransport.Json(200, "{\"ok\":true,\"playerId\":\"dev_lf\",\"serverTimeMs\":" + NowMs + ",\"version\":" + State.Version +
                                                   ",\"state\":" + Wire(State) + ",\"commands\":[]}");
                if (!req.Url.EndsWith("/v1/command")) return FakeTransport.Json(404, "{\"ok\":false,\"error\":\"not_found\"}");
                Commands++;
                var body = JObject.Parse(req.Body);
                Bodies.Add(body);
                if (Interfere != null) { State = Interfere(State); State.Version++; Interfere = null; }
                var type = Engine.CommandType((string)body["type"]);
                var cmd = (GameCommand)body["args"].ToObject(type, ClientJson.WireSerializer);
                cmd.RequestId = (string)body["requestId"];
                long expected = body.Value<long?>("expectedVersion") ?? State.Version;
                if (expected != State.Version) return Full(false, "version_conflict");
                if (RejectWith != null) { var e = RejectWith; RejectWith = null; return Full(false, e); }
                var claims = body["battles"] is JArray b ? b.ToObject<List<BattleResult>>(ClientJson.WireSerializer) : new List<BattleResult>();
                long at = body.Value<long?>("atMs") ?? NowMs;
                var st = State;
                // sampled: re-simulate every battle (the server's result wins)
                var r = Engine.Execute(ref st, cmd, at, (ctx, setup, rng, kind, i) => null);
                if (!r.Ok) return Full(false, r.Error);
                State = st;
                if (string.Equals(StateHash.Hex(State), (string)body["stateHash"], StringComparison.Ordinal))
                {
                    Acks++;
                    return FakeTransport.Json(200, "{\"ok\":true,\"events\":[],\"version\":" + State.Version + ",\"stateHash\":\"" + StateHash.Hex(State) + "\",\"match\":true}");
                }
                return Full(true, null);
            }

            private HttpResponse Full(bool ok, string error)
            {
                FullStates++;
                return FakeTransport.Json(200, "{\"ok\":" + (ok ? "true" : "false") + ",\"error\":" + (error == null ? "null" : "\"" + error + "\"") +
                                               ",\"events\":[],\"version\":" + State.Version + ",\"stateHash\":\"" + StateHash.Hex(State) + "\",\"match\":false,\"state\":" + Wire(State) + "}");
            }
        }

        private ProtocolServer _server;
        private DeferredTransport _http;
        private RemoteGameService _svc;

        [SetUp]
        public void SetUp()
        {
            _server = new ProtocolServer();
            _http = new DeferredTransport { Handler = r => _server.Handle(r) };
            _svc = new RemoteGameService(new RemoteConfig { BaseUrl = "http://lf", DeviceId = "lf" }, _http, new ManualClock(TestUtil.T0));
            ServiceResult res = null;
            _svc.Connect(r => res = r);
            Assert.That(res.Ok, Is.True, res.Error);
        }

        private CommandResult Run(GameCommand cmd)
        {
            CommandResult result = null;
            _svc.Execute(cmd, r => result = r);
            Assert.That(result, Is.Not.Null, "local-first commands answer at once");
            return result;
        }

        private string Level1 => _svc.Content.MainLevel(1).Id;

        [Test]
        public void LocalApply_ShowsAtOnce_ThenTheServerAgrees()
        {
            _http.Defer = true;
            int changed = 0;
            _svc.StateChanged += () => changed++;
            var r = Run(new RunStart { LevelId = Level1, Mode = RunMode.Normal });
            Assert.That(r.Ok, Is.True, r.Error);
            Assert.That(_svc.State.Run.Active, Is.Not.Null, "applied before any response");
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(_svc.PendingCount, Is.EqualTo(1));
            Assert.That(_server.Commands, Is.EqualTo(0));

            _http.Flush();
            Assert.That(_server.Acks, Is.EqualTo(1));
            Assert.That(_server.FullStates, Is.EqualTo(0));
            Assert.That(_svc.PendingCount, Is.EqualTo(0));
            Assert.That(StateHash.Hex(_svc.State), Is.EqualTo(StateHash.Hex(_server.State)));
            var body = _server.Bodies[0];
            Assert.That((long)body["expectedVersion"], Is.EqualTo(0));
            Assert.That((string)body["stateHash"], Has.Length.EqualTo(16));
            Assert.That(body["atMs"], Is.Not.Null);
        }

        [Test]
        public void Commands_AreSentInOrder_OneAtATime()
        {
            _http.Defer = true;
            Run(new RunStart { LevelId = Level1, Mode = RunMode.Normal });
            for (int i = 0; i < 3; i++) Run(new RunAutoStep { Farming = true, FarmShops = true });
            Assert.That(_svc.PendingCount, Is.EqualTo(4));
            Assert.That(_http.Waiting.Count, Is.EqualTo(1), "one in flight");
            _http.Flush();
            Assert.That(_server.Acks, Is.EqualTo(4));
            var versions = new List<long>();
            foreach (var b in _server.Bodies) versions.Add((long)b["expectedVersion"]);
            Assert.That(versions, Is.EqualTo(new List<long> { 0, 1, 2, 3 }));
            Assert.That(_svc.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Battles_AreSimulatedLocally_WithTheLogForPlayback_AndClaimed()
        {
            Run(new RunStart { LevelId = Level1, Mode = RunMode.Normal });
            CommandResult fought = null;
            for (int i = 0; i < 200 && fought == null && _svc.State.Run.Active != null; i++)
            {
                var r = Run(new RunAutoStep { Farming = true, FarmShops = true });
                if (r.Battles.Count > 0) fought = r;
            }
            Assert.That(fought, Is.Not.Null, "a battle happened");
            Assert.That(fought.Battles[0].Result.Log, Is.Not.Empty);
            var run = _svc.State.Run.Active;
            var summary = run != null ? run.LastBattle : _svc.State.Run.LastSummary.LastBattle;
            var playback = run != null ? run.LastBattleForPlayback() : _svc.State.Run.LastSummary.LastBattleForPlayback();
            Assert.That(summary.Log, Is.Empty, "the state keeps no log");
            Assert.That(playback.Log, Is.Not.Empty, "this device plays its own log");
            var claims = (JArray)_server.Bodies[_server.Bodies.Count - 1]["battles"];
            Assert.That(claims.Count, Is.EqualTo(fought.Battles.Count));
            Assert.That(((JArray)claims[0]["log"]).Count, Is.EqualTo(0), "claims carry no log");
            Assert.That(_server.FullStates, Is.EqualTo(0), "the server's re-simulation agreed");
            Assert.That(StateHash.Hex(_svc.State), Is.EqualTo(StateHash.Hex(_server.State)));
        }

        [Test]
        public void ServerDisagrees_ClientAdoptsItsState_AndReRunsThePendingCommands()
        {
            Run(new RunStart { LevelId = Level1, Mode = RunMode.Normal });
            _http.Defer = true;
            // something changed on the server (a mail, an ad reward): the next command meets a different state
            _server.Interfere = s => { s.Wallet.Add(Currencies.Diamonds, 500); return s; };
            long diamonds = _svc.State.Wallet.Get(Currencies.Diamonds);
            Run(new RunAutoStep { Farming = true, FarmShops = true });
            Run(new RunAutoStep { Farming = true, FarmShops = true });
            string diverged = null;
            _svc.Diverged += reason => diverged = reason;
            _http.Flush();
            Assert.That(diverged, Is.EqualTo("version_conflict"));
            Assert.That(_svc.State.Wallet.Get(Currencies.Diamonds), Is.EqualTo(diamonds + 500), "the server's state was adopted");
            Assert.That(_svc.PendingCount, Is.EqualTo(0));
            Assert.That(StateHash.Hex(_svc.State), Is.EqualTo(StateHash.Hex(_server.State)), "re-run and synced again");
        }

        [Test]
        public void ServerRejects_ClientAdoptsAndReports()
        {
            _server.RejectWith = "feature locked";
            string rejected = null;
            _svc.Rejected += e => rejected = e;
            Run(new RunStart { LevelId = Level1, Mode = RunMode.Normal });
            Assert.That(rejected, Is.EqualTo("feature locked"));
            Assert.That(_svc.State.Run.Active, Is.Null, "rolled back to the server's state");
            Assert.That(StateHash.Hex(_svc.State), Is.EqualTo(StateHash.Hex(_server.State)));
        }

        [Test]
        public void NetworkDown_KeepsPlaying_AndRetriesInOrder()
        {
            _server.NetworkDown = true;
            bool stalled = false;
            _svc.SyncStateChanged += s => stalled = s;
            Run(new RunStart { LevelId = Level1, Mode = RunMode.Normal });
            Run(new RunAutoStep { Farming = true, FarmShops = true });
            Assert.That(_svc.State.Run.Active, Is.Not.Null, "the player keeps playing");
            Assert.That(stalled, Is.True);
            Assert.That(_svc.PendingCount, Is.EqualTo(2));
            int sent = _http.Requests.Count;
            _svc.Tick(0.1f);
            Assert.That(_http.Requests.Count, Is.EqualTo(sent), "backoff");
            _server.NetworkDown = false;
            _svc.Tick(RemoteGameService.RetryBackoff[0]);
            Assert.That(stalled, Is.False);
            Assert.That(_svc.PendingCount, Is.EqualTo(0));
            Assert.That(_server.Acks, Is.EqualTo(2));
        }

        [Test]
        public void Refresh_IsALocalFirstSettle_NotAStateDownload()
        {
            ServiceResult done = null;
            _svc.Refresh(r => done = r);
            Assert.That(done.Ok, Is.True);
            Assert.That(_http.Requests[_http.Requests.Count - 1].Url, Does.EndWith("/v1/command"));
            Assert.That((string)_server.Bodies[0]["type"], Is.EqualTo("SettleTime"));
            Assert.That(_server.Acks, Is.EqualTo(1));
        }

        [Test]
        public void AdPaidAndPurchaseCommands_AreServerFirst_AfterTheQueue()
        {
            Assert.That(RemoteGameService.IsServerFirst(new TimerSpeedUp { TimerId = "t", Method = SpeedUpMethod.Ad, AdToken = "evt1" }), Is.True);
            Assert.That(RemoteGameService.IsServerFirst(new TimerSpeedUp { TimerId = "t", Method = SpeedUpMethod.Diamonds }), Is.False);
            Assert.That(RemoteGameService.IsServerFirst(new PurchaseStart { ProductId = "diamonds_60" }), Is.True);
            Assert.That(RemoteGameService.IsServerFirst(new RunStart()), Is.False);

            _http.Defer = true;
            Run(new RunStart { LevelId = Level1, Mode = RunMode.Normal });
            CommandResult verify = null;
            _svc.VerifyPurchase("diamonds_60", "fake-1", r => verify = r);
            Assert.That(verify, Is.Null, "busy until the server answers");
            Assert.That(_http.Waiting.Count, Is.EqualTo(1));
            Assert.That(_http.Waiting[0].Req.Url, Does.EndWith("/v1/command"), "the queued command goes first");
            _http.Flush();
            Assert.That(verify, Is.Not.Null);
            Assert.That(_http.Requests[_http.Requests.Count - 1].Url, Does.EndWith("/v1/iap/verify"));
        }

        // ---- battles: client runtime determinism and playback ------------------------------------

        private static string GoldenPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "shared", "RogueStars.Rules.Tests", "Golden", "battle-determinism.txt"));

        [Test]
        public void Battles_SimulateIdentically_OnTheClientRuntime_AsOnTheServer()
        {
            // The .NET suite (server runtime) checks the same probe against the same golden value.
            if (!File.Exists(GoldenPath)) Assert.Inconclusive("golden not found: " + GoldenPath);
            string golden = File.ReadAllText(GoldenPath).Trim();
            ulong fp = BattleDeterminism.Fingerprint(new ContentDb());
            if (fp.ToString("x16") != golden)
                Assert.Fail("client battle simulation differs from the server's: " + fp.ToString("x16") + " vs golden " + golden + "; per battle: " +
                            string.Join(",", BattleDeterminism.BattleHashes(new ContentDb())));
            // and a JSON round trip of the inputs (what the server receives) changes nothing
            var s = BattleDeterminism.Scenarios(ContentDb.Default, 4);
            foreach (var kv in s)
                Assert.That(StateHash.Hex(BattleSimulator.Simulate(ClientJson.Clone(kv.Key), ContentDb.Default, Rng.FromSeed(kv.Value))),
                    Is.EqualTo(StateHash.Hex(BattleSimulator.Simulate(kv.Key, ContentDb.Default, Rng.FromSeed(kv.Value)))));
        }

        [Test]
        public void StateHash_SurvivesTheWireRoundTrip()
        {
            var json = JsonConvert.SerializeObject(_server.State, ClientJson.Wire);
            var back = JsonConvert.DeserializeObject<GameState>(json, ClientJson.Wire);
            Assert.That(StateHash.Hex(back), Is.EqualTo(StateHash.Hex(_server.State)));
            Assert.That(StateHash.Hex(ClientJson.Clone(_server.State)), Is.EqualTo(StateHash.Hex(_server.State)));
        }

        [Test]
        public void BattlePlayback_ShowsTheRound_NeverTheBattlesLength()
        {
            Assert.That(BattlePlaybackScreen.RoundLabel(3), Is.EqualTo("Round 3"));
            using (var f = new AppFixture())
            {
                f.Service.ExecuteNow(new RunStart { LevelId = f.Service.Content.MainLevel(1).Id, Mode = RunMode.Normal });
                BattleResult battle = null;
                for (int i = 0; i < 3000 && battle == null && f.Service.State.Run.Active != null; i++)
                {
                    var r = f.Service.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = true });
                    if (r.Battles.Count > 0) battle = r.Battles[0].Result;
                }
                Assert.That(battle, Is.Not.Null, "a battle happened");
                f.App.Router.Reset(new HubScreen());
                var screen = new BattlePlaybackScreen(battle);
                f.App.Router.Push(screen);
                for (int k = 0; k < 4 && !screen.Finished; k++)
                {
                    AssertNoTotal(screen.Root, battle);
                    screen.PlaySteps(5);
                }
                screen.PlayAll();
                AssertNoTotal(screen.Root, battle);
            }
        }

        private static void AssertNoTotal(Transform root, BattleResult battle)
        {
            bool sawRound = false;
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                if (t.name != "Round") continue;
                sawRound = true;
                Assert.That(t.text, Does.Not.Contain("/"), "the round pill must not show the battle's length");
                Assert.That(t.text, Does.StartWith("Round "));
            }
            Assert.That(sawRound, Is.True);
        }
    }
}
