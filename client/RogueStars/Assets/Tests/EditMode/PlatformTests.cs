using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Services;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Tests
{
    /// <summary>Store / ad gateways and the purchase + rewarded-ad flows of GameSession (fake platform, fake transport).</summary>
    public sealed class PlatformTests
    {
        [Test]
        public void StoreCatalog_MatchesServerPlayCatalog()
        {
            var products = StoreCatalog.Products(ContentDb.Default).ToDictionary(p => p.ProductId);
            Assert.That(products["diamonds_60"].Consumable, Is.True);
            Assert.That(products["diamonds_60"].StoreProductId, Is.EqualTo("rs.diamonds_60"));
            Assert.That(products[ContentDb.Default.Shop.MonthlyPassProductId].Consumable, Is.True, "monthly pass = 30-day consumable");
            Assert.That(products["perm_no_ads"].Consumable, Is.False);
            Assert.That(products["starter_worm"].Consumable, Is.False);
            Assert.That(products.Values.All(p => !string.IsNullOrEmpty(p.StoreProductId)), Is.True);
            // Same vector as the server's GooglePlayReceiptValidator.ObfuscatedAccountId test.
            Assert.That(StoreCatalog.ObfuscatedAccountId("dev_buyer"), Is.EqualTo("88ce147471988b7897c8a00627a8fcdae46419e856b351d1b7dba7f3e40bce49"));
        }

        [Test]
        public void PlatformServices_UseFakesInTheEditor()
        {
            var cfg = UnityEngine.ScriptableObject.CreateInstance<PlatformConfig>();
            cfg.MaxRewardedAdUnitId = "abc123";
            Assert.That(PlatformServices.CreateStore(cfg), Is.TypeOf<FakeStoreGateway>());
            Assert.That(PlatformServices.CreateAds(cfg), Is.TypeOf<FakeAdGateway>());
            Assert.That(PlatformConfig.LoadDefault().HasMaxAdUnit, Is.False, "committed config keeps placeholders");
        }

        private sealed class Offline : System.IDisposable
        {
            public readonly string Dir = TestUtil.TempDir();
            public readonly GameSession Session = new GameSession();
            public readonly FakeStoreGateway Store = new FakeStoreGateway();
            public readonly LocalGameService Service;

            public Offline()
            {
                Service = new LocalGameService(Dir, "local_platform", "Tester", new ManualClock(TestUtil.T0), 7);
                Session.Use(Service);
                Session.Store = Store;
                Service.Connect(_ => { });
                Session.InitPlatform();
            }

            public long Diamonds => Session.State.Wallet.Get(Currencies.Diamonds);
            public void Dispose() => TestUtil.DeleteDir(Dir);
        }

        [Test]
        public void Purchase_GrantsThenFinishes()
        {
            using (var o = new Offline())
            {
                long before = o.Diamonds;
                CommandResult result = null;
                o.Session.Purchase("diamonds_60", r => result = r);
                Assert.That(result.Ok, Is.True, result.Error);
                Assert.That(o.Diamonds, Is.EqualTo(before + 120));
                Assert.That(o.Store.Finished.Count, Is.EqualTo(1));
                Assert.That(o.Store.Unfinished, Is.Empty);
            }
        }

        [Test]
        public void DeferredPurchase_IsGrantedWhenTheStoreDeliversIt()
        {
            using (var o = new Offline())
            {
                long before = o.Diamonds;
                var infos = new List<string>();
                o.Session.InfoRaised += infos.Add;
                o.Store.DeferNext = true;
                CommandResult result = null;
                o.Session.Purchase("diamonds_60", r => result = r);
                Assert.That(result.Ok, Is.False);
                Assert.That(result.Error, Is.EqualTo(StoreErrors.Deferred));
                Assert.That(o.Diamonds, Is.EqualTo(before));
                Assert.That(infos.Any(i => i.Contains("pending")), Is.True);

                // Payment approved later (or the app restarted): the store delivers, the session verifies and finishes.
                o.Store.Deliver(o.Store.Unfinished[0]);
                Assert.That(o.Diamonds, Is.EqualTo(before + 120));
                Assert.That(o.Store.Finished.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void CancelledPurchase_ShowsNoErrorAndGrantsNothing()
        {
            using (var o = new Offline())
            {
                var errors = new List<string>();
                o.Session.ErrorRaised += errors.Add;
                o.Store.FailNext = true;
                long before = o.Diamonds;
                o.Session.Purchase("diamonds_60");
                Assert.That(o.Diamonds, Is.EqualTo(before));
                Assert.That(errors, Is.Empty);
            }
        }

        // ---- online: verification failures and server-verified ads ------------------------------------------------

        private sealed class Online
        {
            public readonly FakeTransport Http = new FakeTransport();
            public readonly GameSession Session = new GameSession();
            public readonly FakeStoreGateway Store = new FakeStoreGateway();
            public readonly RecordingAds Ads = new RecordingAds();
            public readonly RemoteGameService Service;
            public readonly GameState ServerState;
            public System.Func<HttpRequest, HttpResponse> Extra = _ => null;
            public string Wire => JsonConvert.SerializeObject(ServerState, ClientJson.Wire);

            public Online()
            {
                ServerState = new GameEngine().NewPlayer("dev_buyer", TestUtil.T0, 99);
                Http.Handler = req =>
                {
                    var r = Extra(req);
                    if (r != null) return r;
                    if (req.Url.EndsWith("/v1/auth/dev")) return FakeTransport.Json(200, "{\"token\":\"t\",\"playerId\":\"dev_buyer\"}");
                    if (req.Url.EndsWith("/v1/session") || req.Url.EndsWith("/v1/state"))
                        return FakeTransport.Json(200, "{\"ok\":true,\"playerId\":\"dev_buyer\",\"serverTimeMs\":" + TestUtil.T0 + ",\"version\":1,\"state\":" + Wire + "}");
                    if (req.Url.EndsWith("/v1/command"))
                        return FakeTransport.Json(200, "{\"ok\":true,\"events\":[],\"version\":2,\"state\":" + Wire + "}");
                    return FakeTransport.Json(404, "{\"ok\":false,\"error\":\"not_found\"}");
                };
                Service = new RemoteGameService(new RemoteConfig { BaseUrl = "http://test", DeviceId = "buyer" }, Http, new ManualClock(TestUtil.T0));
                Session.Use(Service);
                Session.Store = Store;
                Session.PlatformAds = Ads;
                Service.Connect(_ => { });
            }
        }

        /// <summary>A platform ad SDK that is not the fake (so the session takes the real, server-verified path).</summary>
        private sealed class RecordingAds : IAdGateway
        {
            public readonly List<string> Shown = new List<string>();
            public string Name => "max";
            public void Show(string placementId, System.Action<ServiceResult<string>> done)
            {
                Shown.Add(placementId);
                done(ServiceResult<string>.Success("local-token"));
            }
        }

        [Test]
        public void Online_RejectedReceipt_IsNotConfirmedAtTheStore()
        {
            var o = new Online();
            o.Extra = req => req.Url.EndsWith("/v1/iap/verify") ? FakeTransport.Json(402, "{\"ok\":false,\"error\":\"account_mismatch\"}") : null;
            var errors = new List<string>();
            o.Session.ErrorRaised += errors.Add;
            o.Session.Purchase("diamonds_60");
            Assert.That(o.Store.Finished, Is.Empty);
            Assert.That(o.Store.Unfinished.Count, Is.EqualTo(1));
            Assert.That(errors.Count, Is.EqualTo(1));

            o.Extra = req => req.Url.EndsWith("/v1/iap/verify") ? FakeTransport.Json(402, "{\"ok\":false,\"error\":\"purchase_pending\"}") : null;
            var infos = new List<string>();
            o.Session.InfoRaised += infos.Add;
            o.Store.Restore();
            Assert.That(o.Store.Finished, Is.Empty);
            Assert.That(infos.Any(i => i.Contains("pending")), Is.True);

            o.Extra = req => req.Url.EndsWith("/v1/iap/verify") ? FakeTransport.Json(200, "{\"ok\":true,\"alreadyCompleted\":true,\"orderId\":\"GPA.1\"}") : null;
            o.Store.Restore();
            Assert.That(o.Store.Finished.Count, Is.EqualTo(1));
            var verify = o.Http.Requests.Last(r => r.Url.EndsWith("/v1/iap/verify"));
            Assert.That((string)JObject.Parse(verify.Body)["purchaseToken"], Does.StartWith("fake-"));
        }

        [Test]
        public void Online_SystemPlacement_WaitsForTheVerifiedEvent_AndReturnsItsId()
        {
            var o = new Online();
            int polls = 0;
            o.Extra = req =>
            {
                if (!req.Url.EndsWith("/v1/ads/credits")) return null;
                polls++;
                return FakeTransport.Json(200, polls < 3
                    ? "{\"ok\":true,\"credits\":[]}"
                    : "{\"ok\":true,\"credits\":[{\"eventId\":\"other\",\"placement\":\"revive\",\"atMs\":1},{\"eventId\":\"ev-42\",\"placement\":\"stamina_refill\",\"atMs\":2}]}");
            };
            ServiceResult<string> token = null;
            o.Session.Ads.Show("stamina_refill", r => token = r);
            Assert.That(o.Ads.Shown, Is.EqualTo(new[] { "stamina_refill" }));
            Assert.That(token, Is.Null, "pending until the S2S callback reached the server");
            for (int i = 0; i < 5 && token == null; i++) o.Session.Tick(o.Session.AdVerifyPollSeconds);
            Assert.That(token.Ok, Is.True);
            Assert.That(token.Value, Is.EqualTo("ev-42"));
        }

        [Test]
        public void Online_SystemPlacement_TimesOut()
        {
            var o = new Online();
            o.Session.AdVerifyTimeoutSeconds = 3;
            o.Extra = req => req.Url.EndsWith("/v1/ads/credits") ? FakeTransport.Json(200, "{\"ok\":true,\"credits\":[]}") : null;
            ServiceResult<string> token = null;
            o.Session.Ads.Show("revive", r => token = r);
            for (int i = 0; i < 10 && token == null; i++) o.Session.Tick(o.Session.AdVerifyPollSeconds);
            Assert.That(token.Ok, Is.False);
            Assert.That(token.Error, Is.EqualTo("ad_not_verified_yet"));
        }

        [Test]
        public void Online_DirectRewardPlacement_ShowsPending_ThenSeesTheServerGrant()
        {
            var o = new Online();
            var infos = new List<string>();
            o.Session.InfoRaised += infos.Add;
            CommandResult done = null;
            o.Session.WatchAd("daily_diamonds_ad", r => done = r);
            Assert.That(infos.Any(i => i.Contains("pending")), Is.True);
            Assert.That(o.Http.Requests.Any(r => r.Url.Contains("/v1/command")), Is.False, "the client never grants ad rewards online");
            // The S2S callback lands on the server; the next refresh shows the consumed placement.
            o.ServerState.Shop.AdViews["daily_diamonds_ad"] = 1;
            for (int i = 0; i < 5 && done == null; i++) o.Session.Tick(o.Session.AdVerifyPollSeconds);
            Assert.That(done.Ok, Is.True);
            Assert.That(infos.Any(i => i.Contains("received")), Is.True);
        }

        [Test]
        public void Online_NoAds_SkipsTheAdAndSendsAnEmptyToken()
        {
            var o = new Online();
            o.ServerState.Flags.Add(ContentDb.Default.Shop.NoAdsFlag);
            o.Service.Refresh(_ => { });
            ServiceResult<string> token = null;
            o.Session.Ads.Show("revive", r => token = r);
            Assert.That(o.Ads.Shown, Is.Empty);
            Assert.That(token.Ok, Is.True);
            Assert.That(token.Value, Is.EqualTo(""));
        }

        [Test]
        public void Online_FakeAds_PlayTheCallbackAgainstTheDevServer()
        {
            var o = new Online();
            o.Session.PlatformAds = new FakeAdGateway();
            o.Extra = req => req.Url.Contains("/v1/ads/ssv?") ? FakeTransport.Json(200, "{\"ok\":true,\"outcome\":\"credited\"}") : null;
            ServiceResult<string> token = null;
            o.Session.Ads.Show("revive", r => token = r);
            var cb = o.Http.Requests.Single(r => r.Url.Contains("/v1/ads/ssv?"));
            Assert.That(cb.Url, Does.Contain("user_id=dev_buyer"));
            Assert.That(cb.Url, Does.Contain("custom_data=revive"));
            Assert.That(cb.Headers.ContainsKey("Authorization"), Is.False);
            Assert.That(token.Ok, Is.True);
        }
    }
}
