using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RogueStars.Client.Services;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Tests
{
    public sealed class RemoteGameServiceTests
    {
        private FakeTransport _http;
        private ManualClock _clock;
        private RemoteGameService _svc;
        private GameEngine _serverEngine;
        private GameState _serverState;
        private int _authCalls;

        /// <summary>Server-shaped wire JSON of a state (camelCase members, keys untouched) like StateJson.Wire.</summary>
        private string WireState() => JsonConvert.SerializeObject(_serverState, ClientJson.Wire);

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock(TestUtil.T0);
            _serverEngine = new GameEngine();
            _serverState = _serverEngine.NewPlayer("dev_device1", TestUtil.T0, 123456789);
            _serverState.DisplayName = "Server Worm";
            _http = new FakeTransport();
            _authCalls = 0;
            _http.Handler = Route;
            _svc = new RemoteGameService(new RemoteConfig { BaseUrl = "http://test:8080/", DeviceId = "device1", DisplayName = "Wormy" }, _http, _clock);
        }

        private HttpResponse Route(HttpRequest req)
        {
            if (req.Url.EndsWith("/v1/auth/dev"))
            {
                _authCalls++;
                return FakeTransport.Json(200, "{\"token\":\"tok" + _authCalls + "\",\"playerId\":\"dev_device1\"}");
            }
            if (req.Url.EndsWith("/v1/session"))
                return FakeTransport.Json(200, "{\"ok\":true,\"playerId\":\"dev_device1\",\"created\":true,\"serverTimeMs\":" + (TestUtil.T0 + 5000) +
                                               ",\"version\":3,\"state\":" + WireState() + ",\"commands\":[\"CityBuild\",\"LoginClaim\"]}");
            return FakeTransport.Json(404, "{\"ok\":false,\"error\":\"not_found\"}");
        }

        private void Connect()
        {
            ServiceResult res = null;
            _svc.Connect(r => res = r);
            Assert.That(res.Ok, Is.True, res.Error);
        }

        [Test]
        public void Connect_DevAuthThenSession_MapsState()
        {
            Connect();
            Assert.That(_http.Requests.Count, Is.EqualTo(2));
            Assert.That(_http.Requests[0].Url, Is.EqualTo("http://test:8080/v1/auth/dev"));
            Assert.That(JObject.Parse(_http.Requests[0].Body)["deviceId"].ToString(), Is.EqualTo("device1"));
            Assert.That(_http.Requests[0].Headers.ContainsKey("Authorization"), Is.False);
            Assert.That(_http.Requests[1].Method, Is.EqualTo("POST"));
            Assert.That(_http.Requests[1].Headers["Authorization"], Is.EqualTo("Bearer tok1"));
            Assert.That(JObject.Parse(_http.Requests[1].Body)["displayName"].ToString(), Is.EqualTo("Wormy"));

            Assert.That(_svc.IsConnected, Is.True);
            Assert.That(_svc.PlayerId, Is.EqualTo("dev_device1"));
            Assert.That(_svc.Version, Is.EqualTo(3));
            Assert.That(_svc.AllowedCommands, Is.EquivalentTo(new[] { "CityBuild", "LoginClaim" }));
            Assert.That(_svc.State.DisplayName, Is.EqualTo("Server Worm"));
            Assert.That(_svc.State.Wallet.Get(Currencies.ArenaTickets), Is.EqualTo(_serverState.Wallet.Get(Currencies.ArenaTickets)), "currency keys are not re-cased");
            Assert.That(_svc.State.Rng.S1, Is.EqualTo(_serverState.Rng.S1));
            Assert.That(_svc.NowMs, Is.EqualTo(TestUtil.T0 + 5000), "clock follows server time");
        }

        [Test]
        public void Execute_SendsTypeRequestIdAndCamelCaseArgs_AndAppliesResponse()
        {
            Connect();
            var newState = _serverEngine.NewPlayer("dev_device1", TestUtil.T0, 1);
            newState.Wallet.Add(Currencies.Diamonds, 777);
            _http.Handler = req => FakeTransport.Json(200,
                "{\"ok\":true,\"error\":null,\"events\":[{\"type\":\"reward\",\"args\":[\"currency\",\"diamonds\",\"10\"]}],\"payload\":{\"diamonds\":10}," +
                "\"version\":4,\"replayed\":false,\"state\":" + JsonConvert.SerializeObject(newState, ClientJson.Wire) + "}");
            int changed = 0;
            _svc.StateChanged += () => changed++;

            CommandResult result = null;
            _svc.Execute(new CityCollect { BlueprintId = "biomass_farm" }, r => result = r);

            var body = JObject.Parse(_http.Requests[2].Body);
            Assert.That(_http.Requests[2].Url, Is.EqualTo("http://test:8080/v1/command"));
            Assert.That((string)body["type"], Is.EqualTo("CityCollect"));
            Assert.That((string)body["requestId"], Is.Not.Empty);
            Assert.That((string)body["args"]["blueprintId"], Is.EqualTo("biomass_farm"));
            Assert.That(body["args"]["type"], Is.Null);
            Assert.That(body["args"]["requestId"], Is.Null);

            Assert.That(result.Ok, Is.True);
            Assert.That(result.Events[0].Type, Is.EqualTo("reward"));
            Assert.That(result.Events[0].Args[1], Is.EqualTo("diamonds"));
            Assert.That(((JToken)result.Payload)["diamonds"].Value<int>(), Is.EqualTo(10));
            Assert.That(_svc.Version, Is.EqualTo(4));
            Assert.That(_svc.State.Wallet.Get(Currencies.Diamonds), Is.EqualTo(newState.Wallet.Get(Currencies.Diamonds)));
            Assert.That(changed, Is.EqualTo(1));
        }

        [Test]
        public void Execute_EnumsAreSentAsNumbers()
        {
            Connect();
            _http.Handler = req => FakeTransport.Json(200, "{\"ok\":false,\"error\":\"unknown timer\"}");
            _svc.Execute(new TimerSpeedUp { TimerId = "t1", Method = SpeedUpMethod.Ad }, _ => { });
            var args = JObject.Parse(_http.Requests[2].Body)["args"];
            Assert.That(args["method"].Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(args["method"].Value<int>(), Is.EqualTo((int)SpeedUpMethod.Ad));
            Assert.That((string)args["timerId"], Is.EqualTo("t1"));
        }

        [Test]
        public void Execute_RuleRejection_KeepsState()
        {
            Connect();
            var before = _svc.State;
            _http.Handler = req => FakeTransport.Json(200, "{\"ok\":false,\"error\":\"not enough resources\"}");
            CommandResult result = null;
            _svc.Execute(new CityUpgrade { BlueprintId = "x" }, r => result = r);
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Is.EqualTo("not enough resources"));
            Assert.That(_svc.State, Is.SameAs(before));
        }

        [Test]
        public void Execute_HttpError_MapsErrorCode()
        {
            Connect();
            _http.Handler = req => FakeTransport.Json(409, "{\"ok\":false,\"error\":\"version_conflict\",\"details\":{\"version\":9}}");
            CommandResult result = null;
            _svc.Execute(new LoginClaim(), r => result = r);
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Is.EqualTo("version_conflict"));
        }

        [Test]
        public void Execute_NetworkError()
        {
            Connect();
            _http.Handler = req => new HttpResponse { Status = 0, NetworkError = "Cannot connect" };
            CommandResult result = null;
            _svc.Execute(new LoginClaim(), r => result = r);
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Does.StartWith("network_error"));
        }

        [Test]
        public void ServerOnlyCommands_AreRejectedLocally()
        {
            Connect();
            int sent = _http.Requests.Count;
            CommandResult result = null;
            _svc.Execute(new PurchaseComplete { ProductId = "diamonds_60", ReceiptId = "dev:x" }, r => result = r);
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Is.EqualTo(ServiceErrors.ServerOnlyCommand));
            Assert.That(_http.Requests.Count, Is.EqualTo(sent));
        }

        [Test]
        public void Unauthorized_ReauthenticatesOnceAndRetries()
        {
            Connect();
            int commandCalls = 0;
            _http.Handler = req =>
            {
                if (req.Url.EndsWith("/v1/command"))
                {
                    commandCalls++;
                    return commandCalls == 1
                        ? FakeTransport.Json(401, "{\"ok\":false,\"error\":\"unauthorized\"}")
                        : FakeTransport.Json(200, "{\"ok\":true,\"events\":[],\"version\":5}");
                }
                return Route(req);
            };
            CommandResult result = null;
            _svc.Execute(new LoginClaim(), r => result = r);
            Assert.That(result.Ok, Is.True, result.Error);
            Assert.That(commandCalls, Is.EqualTo(2));
            Assert.That(_authCalls, Is.EqualTo(2));
            Assert.That(_http.Requests[_http.Requests.Count - 1].Headers["Authorization"], Is.EqualTo("Bearer tok2"));
        }

        [Test]
        public void VerifyPurchase_PostsProductAndToken()
        {
            Connect();
            _http.Handler = req => FakeTransport.Json(200, "{\"ok\":true,\"events\":[],\"version\":6,\"state\":" + WireState() + "}");
            CommandResult result = null;
            _svc.VerifyPurchase("diamonds_60", "fake-abc", r => result = r);
            var req0 = _http.Requests[2];
            Assert.That(req0.Url, Does.EndWith("/v1/iap/verify"));
            var body = JObject.Parse(req0.Body);
            Assert.That((string)body["productId"], Is.EqualTo("diamonds_60"));
            Assert.That((string)body["purchaseToken"], Is.EqualTo("fake-abc"));
            Assert.That(result.Ok, Is.True);
            Assert.That(_svc.Version, Is.EqualTo(6));
        }

        [Test]
        public void Arena_Endpoints_Map()
        {
            Connect();
            _http.Handler = req =>
            {
                if (req.Url.EndsWith("/v1/arena/opponents"))
                    return FakeTransport.Json(200, "{\"ok\":true,\"joined\":true,\"opponents\":[{\"playerId\":\"p2\",\"displayName\":\"Glorp\",\"points\":1010,\"power\":321}]}");
                if (req.Url.EndsWith("/v1/arena/leaderboard"))
                    return FakeTransport.Json(200, "{\"ok\":true,\"week\":\"2026-W10\",\"joined\":true,\"league\":1,\"groupId\":\"g\",\"groupSize\":2,\"myRank\":2," +
                                                   "\"entries\":[{\"rank\":1,\"playerId\":\"p2\",\"displayName\":\"Glorp\",\"points\":1010,\"power\":321,\"wins\":3,\"losses\":1}]}");
                if (req.Url.EndsWith("/v1/arena/fight"))
                    return FakeTransport.Json(200, "{\"ok\":true,\"fightId\":\"f1\",\"won\":true,\"pointsDelta\":30,\"points\":1030,\"seed\":5," +
                                                   "\"opponent\":{\"playerId\":\"p2\",\"displayName\":\"Glorp\",\"power\":321},\"log\":null,\"state\":" + WireState() + "}");
                return Route(req);
            };
            List<ArenaOpponent> opps = null;
            _svc.ArenaOpponents(r => opps = r.Value);
            Assert.That(opps[0].DisplayName, Is.EqualTo("Glorp"));
            Assert.That(opps[0].Power, Is.EqualTo(321));

            ArenaLeaderboard board = null;
            _svc.ArenaLeaderboard(r => board = r.Value);
            Assert.That(board.MyRank, Is.EqualTo(2));
            Assert.That(board.Entries[0].Wins, Is.EqualTo(3));

            ArenaFightOutcome fight = null;
            _svc.ArenaFight("p2", r => fight = r.Value);
            Assert.That(fight.Won, Is.True);
            Assert.That(fight.PointsDelta, Is.EqualTo(30));
            Assert.That(JObject.Parse(_http.Requests[_http.Requests.Count - 1].Body)["opponentId"].ToString(), Is.EqualTo("p2"));
        }

        [Test]
        public void Guilds_MineAndChat_Map()
        {
            Connect();
            _http.Handler = req =>
            {
                if (req.Url.EndsWith("/v1/guilds/mine"))
                    return FakeTransport.Json(200, "{\"ok\":true,\"guild\":{\"id\":\"g1\",\"name\":\"Wigglers\",\"level\":2,\"members\":5},\"level\":2," +
                                                   "\"contributions\":{\"week\":\"w\",\"total\":10,\"members\":[{\"playerId\":\"a\",\"displayName\":\"A\",\"damage\":10}]}}");
                if (req.Url.Contains("/v1/guilds/chat?since=0"))
                    return FakeTransport.Json(200, "{\"ok\":true,\"messages\":[{\"id\":\"m1\",\"playerId\":\"a\",\"displayName\":\"A\",\"text\":\"hi\",\"atMs\":42}]}");
                return Route(req);
            };
            MyGuild mine = null;
            _svc.Guilds.Mine(r => mine = r.Value);
            Assert.That(mine.Guild.Name, Is.EqualTo("Wigglers"));
            Assert.That(mine.Members[0].Damage, Is.EqualTo(10));
            List<ChatMessage> chat = null;
            _svc.Guilds.Chat(0, r => chat = r.Value);
            Assert.That(chat[0].Text, Is.EqualTo("hi"));
            Assert.That(chat[0].AtMs, Is.EqualTo(42));
        }
    }
}
