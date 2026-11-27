using System.Collections;
using System.Reflection;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using RogueStars.Rules.Tests.Run;
using static RogueStars.Rules.Tests.Meta.MetaTestKit;

namespace RogueStars.Rules.Tests.Meta;

/// <summary>
/// Rewards follow use (GDD §13): the content mapping (every currency and item kind → what gives it a use) and the grant
/// gate per source: a reward without a use never lands in the wallet, it waits in the stash (capped for free sources,
/// in full for paid/explicit ones) and is released when its use unlocks.
/// </summary>
public class RewardGateTests
{

    // ---- mapping ------------------------------------------------------------------------------------------------

    private static IEnumerable<string> AllCurrencyConstants() =>
        typeof(Currencies).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)
            .Concat(typeof(ProgressionCurrencies).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!));

    [Fact]
    public void EveryCurrencyConstant_AndShardFamily_HasAUseEntry()
    {
        var c = ContentDb.Default;
        foreach (var id in AllCurrencyConstants()) Assert.True(c.CurrencyUse(id) != null, "no use entry for currency " + id);
        foreach (var id in new[] { Currencies.HeroShards("hero_star_knight"), Currencies.AlienShards("alien_blorp"), Currencies.BlueprintShards("bp_ore_mine"), Currencies.MountShards("mount_scout_saucer") })
            Assert.True(c.CurrencyUse(id) != null, "no use entry for " + id);
        foreach (var kind in typeof(RewardKinds).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!))
            if (kind != RewardKinds.Currency) Assert.True(c.ItemUse(kind) != null, "no use entry for item kind " + kind);
    }

    [Fact]
    public void EveryCurrencyAndItemKind_InTheContent_HasAUseEntry()
    {
        // Crawls the whole content graph: rewards, loot entries, pull pools (Kind + Id), costs and every "...Currency" field.
        var c = new ContentDb();
        var currencies = new HashSet<string>(); var kinds = new HashSet<string>();
        Crawl(c, new HashSet<object>(ReferenceEqualityComparer.Instance), currencies, kinds);
        foreach (var k in c.EconomyConfig.StartingWallet.Keys) currencies.Add(k);
        Assert.Contains(Currencies.Grain, currencies);   // the crawl reaches loot tables
        Assert.Contains(RewardKinds.Rune, kinds);
        foreach (var id in currencies) Assert.True(c.CurrencyUse(id) != null, "content uses currency " + id + " without a use entry");
        foreach (var k in kinds) Assert.True(c.ItemUse(k) != null, "content uses item kind " + k + " without a use entry");
    }

    private static void Crawl(object? o, HashSet<object> seen, HashSet<string> currencies, HashSet<string> kinds)
    {
        if (o == null || o is string || o.GetType().IsPrimitive || o.GetType().IsEnum || !seen.Add(o)) return;
        if (o is IDictionary dict) { foreach (DictionaryEntry kv in dict) Crawl(kv.Value, seen, currencies, kinds); return; }
        if (o is IEnumerable list) { foreach (var x in list) Crawl(x, seen, currencies, kinds); return; }
        var t = o.GetType();
        if (t.Namespace == null || !t.Namespace.StartsWith("RogueStars.Rules")) return;
        var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
        var kindF = fields.FirstOrDefault(f => f.Name == "Kind" && f.FieldType == typeof(string));
        var idF = fields.FirstOrDefault(f => f.Name == "Id" && f.FieldType == typeof(string));
        if (kindF != null && idF != null && kindF.GetValue(o) is string kind && idF.GetValue(o) is string id)
        {
            if (kind == RewardKinds.Currency) currencies.Add(id); else kinds.Add(kind);
        }
        foreach (var f in fields)
        {
            var v = f.GetValue(o);
            if (f.FieldType == typeof(string)) { if ((f.Name == "Currency" || f.Name.EndsWith("Currency")) && v is string cur && cur.Length > 0) currencies.Add(cur); continue; }
            Crawl(v, seen, currencies, kinds);
        }
    }

    [Fact]
    public void Uses_NameRealFeatures_AndKeepAllCommandsExist()
    {
        var e = new GameEngine();
        foreach (var u in e.Content.RewardUses)
            foreach (var r in u.UsedWhen.Items)
                if (r.Kind == RequirementKind.Feature) Assert.True(e.Content.Feature(r.Key) != null, "unknown feature " + r.Key);
        foreach (var cmd in e.Content.GateConfig.KeepAllCommands) Assert.True(e.CommandType(cmd) != null, "unknown command " + cmd);
    }

    [Fact]
    public void StartingWallet_HoldsOnlyWhatDay1Uses()
    {
        var e = new GameEngine();
        var s = e.NewPlayer("p1", TestHelpers.T0, 1);
        foreach (var k in e.Content.EconomyConfig.StartingWallet.Keys)
            Assert.True(RewardGate.CurrencyHasUse(s, e.Content, k, TestHelpers.T0), k + " has no use on day 1");
        // A brand-new account holds stamina and Diamonds only: no tickets, keys, attempts, tokens or farm currencies.
        Assert.Equal(new[] { Currencies.Diamonds, Currencies.Stamina }, s.Wallet.Balances.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Empty(s.Parts.Items);
        Assert.Empty(s.City.Blueprints);
        // The starter parts / farm blueprint, arena tickets and burrow keys wait in the stash for their features.
        Assert.Equal(6, s.Stash.Items.Count(r => r.Kind == RewardKinds.Part)); // parts v2: one Common per slot
        Assert.Contains(s.Stash.Items, r => r.Kind == RewardKinds.Blueprint && r.Id == "bp_biomass_farm");
        Assert.Equal(e.Content.Arena.StartTickets, RewardGate.Stashed(s, Currencies.ArenaTickets));
        Assert.Equal(e.Content.Regen(Currencies.BurrowKeys)!.BaseCap, RewardGate.Stashed(s, Currencies.BurrowKeys));
    }

    // ---- per source -----------------------------------------------------------------------------------------------

    /// <summary>Locked new player (no starter kit) whose main level is <paramref name="cleared"/>, Parts + Daily Goals unlocked.</summary>
    private static (GameEngine e, GameState s) LockedRunner(int cleared)
    {
        var e = RunTestKit.Engine(RunTestKit.Strong(), TestHelpers.NoStarterKit);
        var s = e.NewPlayer("p1", TestHelpers.T0, 7);
        s.Wallet.Add(Currencies.Stamina, 1000);
        if (cleared > 0)
        {
            var prev = e.Content.MainLevel(cleared);
            s.Run.Levels[prev.Id] = new LevelProgress { Cleared = true, Clears = 1 };
            s.Counters[Requirements.MainLevelCounter] = cleared;
        }
        return (e, s);
    }

    [Fact]
    public void RunLoot_And_Completion_LockedCurrencies_AreStashed_NotShownAsGranted()
    {
        var (e, s) = LockedRunner(0);
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1" }).Ok);
        RunTestKit.AutoToEnd(e, ref s);
        Assert.True(s.Run.LastSummary.Won);
        // Level 1 pays Biomass and Ore: Biomass has no use on day 1 (City day 2, worm levels day 3), Ore has one once Parts
        // unlock with this very clear (the release lands in the same command).
        Assert.Equal(0, s.Wallet.Get(Currencies.Biomass));
        Assert.True(RewardGate.Stashed(s, Currencies.Biomass) > 0);
        Assert.DoesNotContain(s.Run.LastSummary.Granted, r => r.Id == Currencies.Biomass);
        Assert.DoesNotContain(s.Run.LastSummary.Doublable, r => r.Id == Currencies.Biomass);
        Assert.True(Features.IsUnlocked(s, e.Content, Features.Parts));
        Assert.True(s.Wallet.Get(Currencies.Ore) > 0);
        foreach (var kv in s.Wallet.Balances.Where(kv => kv.Value > 0))
            Assert.True(RewardGate.CurrencyHasUse(s, e.Content, kv.Key, TestHelpers.T0), kv.Key + " landed in the wallet without a use");
    }

    [Fact]
    public void MilestoneChest_PaysDiamonds_TokensWaitForTheirSignals()
    {
        var (e, s) = LockedRunner(9);
        long diamonds = s.Wallet.Get(Currencies.Diamonds);
        var chest = e.Content.MainLevel(10).FirstClearRewards;
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = e.Content.MainLevelId(10) }).Ok);
        RunTestKit.AutoToEnd(e, ref s);
        Assert.True(s.Run.LastSummary.Won);
        Assert.True(s.Wallet.Get(Currencies.Diamonds) >= diamonds + chest.Single(r => r.Id == Currencies.Diamonds).Amount);
        // The Part Signal (Parts, level 1) and the Alien Signal (Aliens, level 6) are open: their tokens land at once.
        Assert.True(Features.IsUnlocked(s, e.Content, Features.PartPulls));
        Assert.True(s.Wallet.Get(Currencies.PartTokens) >= chest.Single(r => r.Id == Currencies.PartTokens).Amount);
        Assert.True(s.Wallet.Get(Currencies.AlienTokens) >= chest.Single(r => r.Id == Currencies.AlienTokens).Amount);
        foreach (var t in new[] { Currencies.BlueprintTokens }) // the Blueprint Signal opens with the City (15)
        {
            Assert.Equal(0, s.Wallet.Get(t));
            Assert.True(RewardGate.Stashed(s, t) >= chest.Single(r => r.Id == t).Amount, t);
            Assert.DoesNotContain(s.Run.LastSummary.Granted, r => r.Id == t);
        }
    }

    [Fact]
    public void NewPlayerCalendar_And_Login_LockedRewardsAreStashed()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        var r = Ok(e, ref s, new LoginClaim(), t); // day 1 of the cycle: Diamonds (always usable)
        Assert.Contains(r.Events, ev => ev.Type == "reward" && ev.Args[1] == Currencies.Diamonds);
        Assert.DoesNotContain(r.Events, ev => ev.Type == "reward_stashed");
        r = Ok(e, ref s, new NewPlayerClaim { Day = 2 }, t + D); // 5 Part Signal tokens: the Signal opens on day 4
        Assert.Contains(r.Events, ev => ev.Type == "reward_stashed" && ev.Args[1] == Currencies.PartTokens);
        Assert.DoesNotContain(r.Events, ev => ev.Type == "reward");
        Assert.Equal(0, Bal(s, Currencies.PartTokens));
        Assert.Equal(5, RewardGate.Stashed(s, Currencies.PartTokens));
    }

    [Fact]
    public void QuestChest_GearCoresBeforeAPartNeedsThem_AreStashedUpToTheCap_TheRestDropped()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        var use = e.Content.CurrencyUse(Currencies.GearCores)!; // no part needs them yet
        var chest = e.Content.PointChests.First(c => c.Rewards.Any(r => r.Id == Currencies.GearCores));
        long perChest = chest.Rewards.Single(r => r.Id == Currencies.GearCores).Amount;
        long got = 0;
        e.Settle(s, t); // daily/weekly point reset happens here, not between the claims below
        while (got <= use.StashCap)
        {
            if (chest.Track == PointTrack.Daily) s.Quests.DailyPoints = chest.Points; else s.Quests.WeeklyPoints = chest.Points;
            s.Quests.ChestsClaimed.Clear();
            Ok(e, ref s, new QuestClaimChest { Track = chest.Track, Points = chest.Points }, t);
            got += perChest;
        }
        Assert.Equal(0, Bal(s, Currencies.GearCores));
        Assert.Equal(use.StashCap, RewardGate.Stashed(s, Currencies.GearCores));
        Assert.Equal(got - use.StashCap, s.Stash.Dropped[Currencies.GearCores]);
    }

    [Fact]
    public void Mail_And_PaidPurchases_KeepEverything_BeyondTheCap()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        long cap = e.Content.CurrencyUse(Currencies.Fuel)!.StashCap;
        MailSystem.Push(s, e.Content, t, "Sorry", "Compensation", new[] { Reward.Cur(Currencies.Fuel, cap * 3) });
        Ok(e, ref s, new MailClaimAll(), t);
        Assert.Equal(0, Bal(s, Currencies.Fuel));
        Assert.Equal(cap * 3, RewardGate.Stashed(s, Currencies.Fuel)); // explicit gift: kept in full
        // A free source on top is capped against what the stash already holds: nothing more is kept.
        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = t };
        Assert.False(ctx.Grant(Reward.Cur(Currencies.Fuel, 10)));
        Assert.Equal(cap * 3, RewardGate.Stashed(s, Currencies.Fuel));
        Assert.Equal(10, s.Stash.Dropped[Currencies.Fuel]);
    }

    [Fact]
    public void FeatureUnlock_ReleasesTheStash_RecordedForTheUnlockMoment()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        Ok(e, ref s, new NewPlayerClaim { Day = 2 }, t + D);
        Assert.Equal(5, RewardGate.Stashed(s, Currencies.PartTokens));
        // Part Signal (with Parts, level 1): the unlock grants its free 10× pull and releases the 5 stashed tokens.
        s.Counters[Requirements.MainLevelCounter] = 12;
        for (int day = 2; day <= 4; day++) Ok(e, ref s, new LobbyPopupShown(), t + day * D); // one group per day: City, Worm Level, Part Signal
        Assert.True(Features.IsUnlocked(s, e.Content, Features.PartPulls));
        Assert.Equal(5, Bal(s, Currencies.PartTokens));
        Assert.Equal(1, s.Gacha.FreePulls(GachaType.Part));
        Assert.Equal(0, RewardGate.Stashed(s, Currencies.PartTokens));
        var released = RewardGate.ReleasedAt(s, Features.PartPulls);
        Assert.Contains(released, r => r.Id == Currencies.PartTokens && r.Amount == 5);
    }

    [Fact]
    public void ItemStash_KeepsTheBestRarities_UpToTheCap()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        int cap = (int)e.Content.ItemUse(RewardKinds.Rune)!.StashCap;
        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = t };
        for (int i = 0; i < cap; i++) Assert.False(ctx.Grant(new Reward(RewardKinds.Rune, RewardWildcard.Any, 1, 0)));
        Assert.False(ctx.Grant(new Reward(RewardKinds.Rune, RewardWildcard.Any, 1, 0))); // full: dropped
        Assert.False(ctx.Grant(new Reward(RewardKinds.Rune, RewardWildcard.Any, 1, 2))); // better: replaces a common
        var runes = s.Stash.Items.Where(r => r.Kind == RewardKinds.Rune).ToList();
        Assert.Equal(cap, runes.Count);
        Assert.Equal(1, runes.Count(r => r.Rarity == 2));
        Assert.Equal(2, s.Stash.Dropped[RewardKinds.Rune]);
        Assert.Empty(s.Parts.Runes);
        Unlock(s, Features.Runes);
        Ok(e, ref s, new LobbyPopupShown(), t);
        Assert.Equal(cap, s.Parts.Runes.Count);
        Assert.DoesNotContain(s.Stash.Items, r => r.Kind == RewardKinds.Rune);
    }

    [Fact]
    public void NonFeatureUse_GearCores_ReleaseWithTheFirstRarePart()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        Unlock(s, Features.Parts);
        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = t };
        Assert.False(ctx.Grant(Reward.Cur(Currencies.GearCores, 50))); // only Commons (cap 10): no Gear Core cost yet
        Assert.True(ctx.Grant(new Reward(RewardKinds.Part, "r_arm_guard", 1, (int)PartRarity.Rare)));
        Ok(e, ref s, new LobbyPopupShown(), t);
        Assert.Equal(50, Bal(s, Currencies.GearCores));
    }

    [Fact]
    public void RegenCurrencies_DoNotRegenerateBeforeTheirUse_UnlockGivesExactlyTheCap()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        e.Settle(s, t + 30 * D);
        Assert.Equal(0, Bal(s, Currencies.ArenaTickets));
        Assert.Equal(0, Bal(s, Currencies.BurrowKeys));
        Assert.Equal(0, Bal(s, Currencies.InvasionAttempts));
        Unlock(s, Features.Arena, Features.Burrow, Features.Invasions);
        e.Settle(s, t + 30 * D + 1);
        Assert.Equal(e.Content.Arena.StartTickets, Bal(s, Currencies.ArenaTickets)); // no 30-day backlog
        Assert.Equal(e.Content.Regen(Currencies.BurrowKeys)!.BaseCap, Bal(s, Currencies.BurrowKeys));
        Assert.Equal(e.Content.LiveOps.InvasionAttemptsPerDay, Bal(s, Currencies.InvasionAttempts));
    }

    [Fact]
    public void CityProduction_WithoutAUse_StaysInTheBuilding()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        Unlock(s, Features.BiomassFarm, Features.GrainFarm);
        s.Wallet.Add(Currencies.Ore, 10_000); s.Wallet.Add(Currencies.Biomass, 10_000);
        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = t };
        Assert.True(ctx.Grant(new Reward(RewardKinds.Blueprint, "bp_grain_farm", 1)));
        string farm = (string)Ok(e, ref s, new CityPlace { BlueprintId = "bp_grain_farm", X = 11, Y = 9 }, t).Payload!;
        Ok(e, ref s, new TimerSpeedUp { TimerId = CitySystem.Building(s, farm)!.TimerId, Method = SpeedUpMethod.Diamonds }, t);
        s.Aliens.Owned["alien_zib"] = new OwnedAlien { TypeId = "alien_zib", WorkBuildingId = farm }; // a worker runs the farm
        Ok(e, ref s, new CityCollect(), t + 5 * 60 * 60 * 1000L);
        Assert.Equal(0, Bal(s, Currencies.Grain)); // Grain is for aliens (day 7): the farm keeps it
        double stored = CitySystem.Building(s, farm)!.Stored;
        Assert.True(stored >= 1);
        Unlock(s, Features.Aliens);
        Ok(e, ref s, new CityCollect(), t + 5 * 60 * 60 * 1000L + 1);
        Assert.True(Bal(s, Currencies.Grain) >= (long)Math.Floor(stored));
    }

    [Fact]
    public void Previews_ListOnlyUsableRewards()
    {
        var e = Engine(); var s = Player(e);
        var list = new List<Reward> { Reward.Cur(Currencies.Diamonds, 5), Reward.Cur(Currencies.Fuel, 5), new Reward(RewardKinds.Mount, "mount_scout_saucer", 1) };
        Assert.Equal(new[] { Currencies.Diamonds }, RewardGate.Usable(s, e.Content, list).Select(r => r.Id));
        Assert.True(RewardGate.AnyUsable(s, e.Content, list));
        Assert.False(RewardGate.AnyUsable(s, e.Content, list.Skip(1)));
        UnlockAll(s);
        Assert.Equal(3, RewardGate.Usable(s, e.Content, list).Count);
    }
}
