using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using RogueStars.Client.Services;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Tests
{
    public sealed class LocalGameServiceTests
    {
        private string _dir;
        private ManualClock _clock;

        [SetUp] public void SetUp() { _dir = TestUtil.TempDir(); _clock = new ManualClock(TestUtil.T0); }
        [TearDown] public void TearDown() => TestUtil.DeleteDir(_dir);

        private LocalGameService Connected()
        {
            var s = new LocalGameService(_dir, "local_p1", "Wormy", _clock, 7);
            ServiceResult res = null;
            s.Connect(r => res = r);
            Assert.That(res, Is.Not.Null);
            Assert.That(res.Ok, Is.True, res.Error);
            return s;
        }

        [Test]
        public void NewPlayer_IsCreatedAndSaved()
        {
            var s = Connected();
            Assert.That(s.IsConnected, Is.True);
            Assert.That(s.IsOnline, Is.False);
            Assert.That(s.State.PlayerId, Is.EqualTo("local_p1"));
            Assert.That(s.State.DisplayName, Is.EqualTo("Wormy"));
            Assert.That(s.State.Wallet.Get(Currencies.ArenaTickets), Is.GreaterThan(0));
            Assert.That(s.State.City.UnlockedAreas, Is.Not.Empty);
            Assert.That(File.Exists(s.SavePath), Is.True);
            Assert.That(s.Content.Shop.AllowDevReceipts, Is.True, "offline content enables dev receipts");
        }

        [Test]
        public void Execute_Success_ChangesStateAndRaisesEvents()
        {
            var s = Connected();
            int changes = 0;
            s.StateChanged += () => changes++;
            long v = s.State.Version;
            CommandResult result = null;
            s.Execute(new LoginClaim(), r => result = r);
            Assert.That(result.Ok, Is.True, result.Error);
            Assert.That(s.State.Version, Is.EqualTo(v + 1));
            Assert.That(result.Events.Exists(e => e.Type == "reward"), Is.True);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(QuestSystem.CanClaimLogin(s.State, s.Content, s.NowMs), Is.False);
        }

        [Test]
        public void Execute_Failure_LeavesStateUntouched()
        {
            var s = Connected();
            string before = JsonConvert.SerializeObject(s.State, ClientJson.Storage);
            CommandResult result = null;
            s.Execute(new CityUpgrade { BlueprintId = "does_not_exist" }, r => result = r);
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Is.Not.Empty);
            Assert.That(JsonConvert.SerializeObject(s.State, ClientJson.Storage), Is.EqualTo(before));
        }

        [Test]
        public void OfflinePurchase_CompletesWithDevReceipt()
        {
            var s = Connected();
            long before = s.State.Wallet.Get(Currencies.Diamonds);
            Assert.That(s.ExecuteNow(new PurchaseStart { ProductId = "diamonds_60" }).Ok, Is.True);
            CommandResult result = null;
            s.VerifyPurchase("diamonds_60", "fake-token-1", r => result = r);
            Assert.That(result.Ok, Is.True, result.Error);
            Assert.That(s.State.Wallet.Get(Currencies.Diamonds), Is.GreaterThanOrEqualTo(before + 60));
            Assert.That(s.State.Shop.Receipts, Does.Contain("dev:fake-token-1"));
        }

        [Test]
        public void SaveLoad_RoundTripIsLossless()
        {
            var s = Connected();
            s.ExecuteNow(new LoginClaim());
            s.DevGrantStarterKit();
            Assert.That(s.State.City.Blueprints, Is.Not.Empty);
            Assert.That(s.State.Aliens.Owned, Is.Not.Empty);
            s.State.Flags.Add("roundtrip_flag");
            s.Save();
            string expected = JsonConvert.SerializeObject(s.State, ClientJson.Storage);

            var reloaded = Connected();
            Assert.That(JsonConvert.SerializeObject(reloaded.State, ClientJson.Storage), Is.EqualTo(expected));
            Assert.That(reloaded.State.Rng.S0, Is.EqualTo(s.State.Rng.S0), "ulong RNG state survives JSON");
            Assert.That(reloaded.State.Flags, Does.Contain("roundtrip_flag"));
        }

        [Test]
        public void ResetSave_StartsFreshPlayer()
        {
            var s = Connected();
            s.ExecuteNow(new LoginClaim());
            s.ResetSave(_ => { });
            Assert.That(s.State.Version, Is.EqualTo(0));
            Assert.That(QuestSystem.CanClaimLogin(s.State, s.Content, s.NowMs), Is.True);
        }

        [Test]
        public void TimersSettleWhenTheClockAdvances()
        {
            var s = Connected();
            s.DevGrantStarterKit();
            string bp = null;
            foreach (var b in CitySystem.SortedBlueprints(s.State))
            {
                var def = s.Content.Blueprint(b.Id);
                for (int y = 0; y < s.Content.CityTuning.GridHeight && bp == null; y++)
                    for (int x = 0; x < s.Content.CityTuning.GridWidth && bp == null; x++)
                        if (CitySystem.PlacementError(s.State, s.Content, def, x, y) == null)
                        {
                            Assert.That(s.ExecuteNow(new CityBuild { BlueprintId = b.Id, X = x, Y = y }).Ok, Is.True);
                            bp = b.Id;
                        }
                if (bp != null) break;
            }
            Assert.That(bp, Is.Not.Null, "some blueprint can be placed");
            Assert.That(CitySystem.Owned(s.State, bp).Level, Is.EqualTo(0).Or.EqualTo(1));
            _clock.Advance(30L * 24 * 3600 * 1000);
            s.Refresh(_ => { });
            Assert.That(CitySystem.Owned(s.State, bp).Level, Is.EqualTo(1));
            Assert.That(CitySystem.Owned(s.State, bp).TimerId, Is.Null);
        }

        [Test]
        public void LocalArena_FightsBots()
        {
            var s = Connected();
            s.DevSetFlag(Features.DevUnlockAllFlag, true);
            ServiceResult<System.Collections.Generic.List<ArenaOpponent>> opps = null;
            s.ArenaOpponents(r => opps = r);
            Assert.That(opps.Ok, Is.True);
            Assert.That(opps.Value.Count, Is.EqualTo(5));
            long tickets = s.State.Wallet.Get(Currencies.ArenaTickets);
            ServiceResult<RogueStars.Client.Services.ArenaFightOutcome> fight = null;
            s.ArenaFight(opps.Value[0].PlayerId, r => fight = r);
            Assert.That(fight.Ok, Is.True, fight.Error);
            Assert.That(s.State.Wallet.Get(Currencies.ArenaTickets), Is.EqualTo(tickets - 1));
            Assert.That(s.State.Arena.WeekWins + s.State.Arena.WeekLosses, Is.EqualTo(1));
            ServiceResult<ArenaLeaderboard> board = null;
            s.ArenaLeaderboard(r => board = r);
            Assert.That(board.Value.MyRank, Is.GreaterThan(0));
        }
    }
}
