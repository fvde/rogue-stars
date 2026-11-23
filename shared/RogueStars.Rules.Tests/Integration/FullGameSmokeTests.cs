using System.Text.Json;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Integration;

/// <summary>
/// A new player plays through every system with the full content (starter kit, gates, real granters) in offline
/// mode. Every command must keep balances non-negative and bump the version by exactly one.
/// </summary>
public class FullGameSmokeTests
{
    private const long Min = 60_000, Hour = 60 * Min;

    private sealed class Player
    {
        public readonly GameEngine E;
        public GameState S;
        public long Now;

        public Player(long seed)
        {
            var content = new ContentDb();
            content.Shop.AllowDevReceipts = true; // offline mode: dev receipts complete purchases locally
            E = new GameEngine(content) { Cloner = TestHelpers.Clone };
            Now = TestHelpers.T0;
            S = E.NewPlayer("smoke", Now, seed);
            S.DisplayName = "Smoky";
            AssertInvariants();
        }

        public CommandResult Do(GameCommand cmd, long advanceMs = 0)
        {
            Now += advanceMs;
            long v = S.Version;
            var r = E.Execute(ref S, cmd, Now);
            Assert.True(r.Ok, cmd.Type + " failed: " + r.Error);
            Assert.Equal(v + 1, S.Version);
            Assert.Equal(Now, S.LastCommandMs);
            AssertInvariants();
            return r;
        }

        /// <summary>
        /// Plays one command (CityCollect) per account day until <paramref name="day"/> (1 = creation day): feature
        /// unlocks are gated by account day and limited to one group per day after day 1. Returns the unlocked ids.
        /// </summary>
        public List<string> PlayUntilDay(int day)
        {
            var unlocked = new List<string>();
            long created = GameClock.DayIndex(E.Content, S.CreatedMs);
            while (GameClock.DayIndex(E.Content, Now) - created + 1 < day)
            {
                long next = GameClock.DayStartMs(E.Content, GameClock.DayIndex(E.Content, Now) + 1) + Hour;
                unlocked.AddRange(Do(new CityCollect(), next - Now).Events.Where(ev => ev.Type == "feature_unlocked").Select(ev => ev.Args[0]));
            }
            return unlocked;
        }

        public void AssertInvariants()
        {
            foreach (var kv in S.Wallet.Balances) Assert.True(kv.Value >= 0, "negative balance " + kv.Key);
            foreach (var kv in S.Counters) Assert.True(kv.Value >= 0, "negative counter " + kv.Key);
            foreach (var b in S.City.Blueprints.Values) Assert.True(b.Stored >= 0);
        }

        /// <summary>Stand-in for the rewards of many further runs (one real run is played in the smoke test).</summary>
        public void RunRewards(long biomass, long ore, long grain)
        {
            S.Wallet.Add(Currencies.Biomass, biomass); S.Wallet.Add(Currencies.Ore, ore); S.Wallet.Add(Currencies.Grain, grain);
        }

        /// <summary>Finish a running timer with Diamonds (speed-up command).</summary>
        public void Rush(string timerId)
        {
            if (TimerSystem.Get(S, timerId) == null) return;
            Do(new TimerSpeedUp { TimerId = timerId, Method = SpeedUpMethod.Diamonds });
        }

        public void Build(string bp, int x, int y)
        {
            Do(new CityBuild { BlueprintId = bp, X = x, Y = y });
            Rush(CitySystem.Owned(S, bp).TimerId);
            Assert.True(CitySystem.IsActive(CitySystem.Owned(S, bp)), bp);
        }
    }

    [Fact]
    public void NewPlayer_PlaysThroughEverySystem()
    {
        var p = new Player(2027);
        var s = p.S;
        Assert.Equal(3, p.S.Parts.Items.Count);

        // Day 1: login, starter Biomass Farm, first collection.
        p.Do(new LoginClaim());
        p.Build("bp_biomass_farm", 4, 4);
        var collected = (Dictionary<string, long>)p.Do(new CityCollect(), 2 * Hour).Payload!;
        Assert.True(collected[Currencies.Biomass] > 0);

        // Equip the starter parts.
        foreach (var part in p.S.Parts.Items.Values.OrderBy(x => x.Uid).ToList())
        {
            var def = p.E.Content.PartsConfig.Defs[part.DefId];
            var slot = def.Kind switch { PartKind.Head => PartSlot.Head, PartKind.Body => PartSlot.Body, _ => PartSlot.RightArm };
            p.Do(new PartEquip { Uid = part.Uid, Slot = slot });
        }

        // A complete auto-played run (RunAutoStep) with real worm stats; completion pays out and sets portal progress.
        long biomassBefore = p.S.Wallet.Get(Currencies.Biomass);
        p.Do(new RunStart { LevelId = "ember_wastes_1" });
        for (int i = 0; i < 1500 && p.S.Run.Active != null; i++) p.Do(new RunAutoStep { Farming = true, FarmShops = true });
        Assert.Null(p.S.Run.Active);
        Assert.True(p.S.Run.LastSummary.Won);
        Assert.Equal(1, p.S.Counter("runs_completed"));
        Assert.Equal(1, p.S.Counter(Requirements.MainLevelCounter));
        Assert.True(p.S.Wallet.Get(Currencies.Biomass) > biomassBefore);
        Assert.True(Features.IsUnlocked(p.S, p.E.Content, Features.Parts));

        // Later portal progress (stand-in for many more runs) over 14 days: one unlock per day (pulls, aliens, housing,
        // heroes, mounts, breeding...).
        p.S.Counters[Requirements.MainLevelCounter] = 30;
        p.RunRewards(200_000, 200_000, 100_000);
        var unlockEvents = p.PlayUntilDay(14);
        Assert.Contains(Features.Aliens, unlockEvents);
        Assert.Contains(Features.Mounts, unlockEvents);
        Assert.NotNull(AlienSystem.Owned(p.S, "alien_blorp"));

        // Offline IAP with a dev receipt: Diamonds for pulls.
        p.Do(new PurchaseStart { ProductId = "diamonds_8000" });
        p.Do(new PurchaseComplete { ProductId = "diamonds_8000", ReceiptId = "dev:smoke-1" });
        Assert.True(p.S.Wallet.Get(Currencies.Diamonds) >= 16_000);

        // Every pull type, tokens and Diamonds.
        p.Do(new GachaPull { PullType = GachaType.Part, Count = 10, Payment = GachaPayment.Tokens });
        p.Do(new GachaPull { PullType = GachaType.Blueprint, Count = 1, Payment = GachaPayment.Tokens });
        p.Do(new GachaPull { PullType = GachaType.Alien, Count = 1, Payment = GachaPayment.Tokens });
        while (p.S.Aliens.Owned.Count < 2) p.Do(new GachaPull { PullType = GachaType.Alien, Count = 10, Payment = GachaPayment.Diamonds });
        p.Do(new GachaPull { PullType = GachaType.Blueprint, Count = 10, Payment = GachaPayment.Diamonds });
        Assert.True(p.S.Counter("pulls_part") >= 10 && p.S.Counter("pulls_blueprint") >= 11 && p.S.Counter("pulls_alien") >= 1);

        // Worm and parts.
        p.Do(new WormLevelUp { Levels = 5 });
        Assert.Equal(6, p.S.Worm.Level);
        var arm = p.S.Parts.Equipped[PartSlot.RightArm];
        p.Do(new PartLevelUp { Uid = arm, Levels = 3 });

        // City: housing (squad slot), grain farm with an alien worker, squad, breeding.
        p.Build("bp_housing_pod", 5, 4);
        p.Build("bp_grain_farm", 4, 5);
        p.Do(new AlienAssign { AlienTypeId = "alien_blorp", BlueprintId = "bp_grain_farm" });
        var aliens = p.S.Aliens.Owned.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        p.Do(new AlienSetSquad { AlienTypeIds = { aliens[0] } });
        p.Do(new AlienLevelUp { AlienTypeId = aliens[0] });
        p.Build("bp_breeding_pen", 6, 4);
        string job = (string)p.Do(new BreedStart { ParentA = aliens[0], ParentB = aliens[1] }).Payload!;
        p.Rush(BreedingSystem.Job(p.S, job).TimerId);
        string child = (string)p.Do(new BreedCollect { JobId = job }).Payload!;
        p.Do(new BreedResolve { ChildId = child, Keep = BreedKeep.Replace });

        // Hero and mount from the unlock grants.
        p.Do(new HeroUnlock { HeroId = "hero_star_knight" });
        p.Do(new MountEquip { MountId = "mount_scout_saucer" });

        // Quests: login, part pull, ad (No-token ad fails, a real token works).
        p.Do(new LoginClaim()); // a new day since the unlock days above
        p.Do(new BuyStamina { Method = StaminaRefillMethod.Ad, AdToken = "ad-1" });
        p.Do(new QuestClaimAll());
        Assert.True(p.S.Counter("daily_quests_claimed") >= 3);

        // Soft-currency shop and a timed collection later the same day.
        p.Do(new ShopBuyItem { ItemId = "daily_free_gift" });
        p.Do(new CityCollect(), 3 * Hour);

        // Combat loadout: hero, mount, squad aliens, parts, building boons; power counts everything.
        var loadout = p.E.BuildLoadout(p.S, p.Now);
        Assert.Equal("hero_star_knight", loadout.HeroId);
        Assert.Equal("mount_scout_saucer", loadout.MountId);
        Assert.Single(loadout.Aliens);
        Assert.Equal(aliens[0], loadout.Aliens[0].AlienTypeId);
        Assert.Contains(AttackStyle.Slam, loadout.ArmStyles);
        Assert.True(loadout.Power > 0);
        Assert.Equal(WormSystem.ComputePower(loadout, p.E.Content), loadout.Power);
        var arena = ArenaSystem.CreateSnapshot(p.E, p.S, p.Now);
        Assert.Equal(loadout.Power, arena.Power);

        // Determinism: replaying the whole state through JSON and rebuilding gives the same loadout.
        var copy = TestHelpers.Clone(p.S);
        Assert.Equal(JsonSerializer.Serialize(loadout, TestHelpers.Json), JsonSerializer.Serialize(p.E.BuildLoadout(copy, p.Now), TestHelpers.Json));
        Assert.True(p.S.Version >= 30);
    }

    [Fact]
    public void FullyPopulatedState_RoundTripsThroughJson_WithoutLoss()
    {
        var p = new Player(99);
        p.S.Counters[Requirements.MainLevelCounter] = 40;
        p.PlayUntilDay(19); // day-gated unlocks, one per day: everything up to the Burrow
        p.RunRewards(500_000, 500_000, 200_000);
        p.Do(new PurchaseStart { ProductId = "diamonds_8000" });
        p.Do(new PurchaseComplete { ProductId = "diamonds_8000", ReceiptId = "dev:rt-1" });
        p.Do(new PurchaseStart { ProductId = p.E.Content.Shop.MonthlyPassProductId });
        p.Do(new PurchaseComplete { ProductId = p.E.Content.Shop.MonthlyPassProductId, ReceiptId = "dev:rt-2" });
        foreach (var t in new[] { GachaType.Part, GachaType.Blueprint, GachaType.Alien })
            p.Do(new GachaPull { PullType = t, Count = 10, Payment = GachaPayment.Diamonds });
        p.Build("bp_biomass_farm", 4, 4);
        p.Build("bp_housing_pod", 5, 4);
        p.Do(new AlienSetSquad { AlienTypeIds = { "alien_blorp" } });
        p.Do(new HeroUnlock { HeroId = "hero_star_knight" });
        p.Do(new MountEquip { MountId = "mount_scout_saucer" });
        p.Do(new LoginClaim());
        p.Do(new FeatureCelebrate { FeatureId = Features.Aliens });
        p.Do(new LobbyPopupShown { PopupId = "news" });
        p.Do(new ShopBuyItem { ItemId = "daily_free_gift" });
        p.Do(new CityCollect(), 5 * Hour); // settles planetary events/modifiers too
        p.Do(new CityBuild { BlueprintId = "bp_grain_farm", X = 4, Y = 5 }); // timer left running
        p.Do(new LabResearch { ResearchId = "res_burning_soul" });          // research timer running
        p.Do(new RunStart { LevelId = "ember_wastes_1" });                   // a run in progress (board, RNG, loadout)
        for (int i = 0; i < 5 && p.S.Run.Active != null; i++) p.Do(new RunAutoStep { Farming = true, FarmShops = true });

        // Every top-level part of the state is populated.
        var s = p.S;
        Assert.NotEmpty(s.Wallet.Balances); Assert.NotEmpty(s.Wallet.Regen); Assert.NotEmpty(s.Modifiers);
        Assert.NotEmpty(s.Counters); Assert.NotEmpty(s.Flags); Assert.NotEmpty(s.Parts.Items); Assert.NotEmpty(s.Parts.Runes);
        Assert.NotEmpty(s.City.Blueprints); Assert.NotEmpty(s.Aliens.Owned); Assert.NotEmpty(s.Timers.Active);
        Assert.NotEmpty(s.Heroes.Owned); Assert.NotEmpty(s.Mounts.Owned); Assert.NotEmpty(s.Onboarding.Unlocked);
        Assert.NotEmpty(s.Shop.PurchaseCounts); Assert.NotEmpty(s.Shop.DailyGrants); Assert.NotEmpty(s.Mail.Inbox);
        Assert.NotNull(s.Run.Active); Assert.NotEmpty(s.Run.Active!.Tiles); Assert.NotEmpty(s.Lab.Entries);

        string json = JsonSerializer.Serialize(s, TestHelpers.Json);
        var back = JsonSerializer.Deserialize<GameState>(json, TestHelpers.Json)!;
        Assert.Equal(json, JsonSerializer.Serialize(back, TestHelpers.Json));
        Assert.Equal(s.Rng.NextULong(), back.Rng.NextULong()); // RNG stream survives too

        // The deserialized state keeps playing identically.
        var a = s; var b = back;
        var cmd = new GachaPull { PullType = GachaType.Alien, Count = 10, Payment = GachaPayment.Diamonds };
        var ra = p.E.Execute(ref a, cmd, p.Now + Min);
        var rb = p.E.Execute(ref b, cmd, p.Now + Min);
        Assert.True(ra.Ok && rb.Ok);
        for (int i = 0; i < 20; i++)
        {
            if (a.Run.Active == null) break;
            Assert.True(p.E.Execute(ref a, new RunAutoStep { Farming = true, FarmShops = true }, p.Now + Min).Ok);
            Assert.True(p.E.Execute(ref b, new RunAutoStep { Farming = true, FarmShops = true }, p.Now + Min).Ok);
        }
        Assert.Equal(JsonSerializer.Serialize(a, TestHelpers.Json), JsonSerializer.Serialize(b, TestHelpers.Json));
    }
}
