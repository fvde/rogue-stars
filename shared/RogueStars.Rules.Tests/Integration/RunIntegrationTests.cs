using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Integration;

/// <summary>Run workstream wired into the rest: real worm stats, wildcard drops, portal progress, lab timers, early balance.</summary>
public class RunIntegrationTests
{
    private const long T0 = TestHelpers.T0, Min = 60_000;

    private static GameEngine Engine() => new GameEngine(new ContentDb()) { Cloner = TestHelpers.Clone };

    /// <summary>Plays one run with RunAutoStep (auto mode). Returns the summary.</summary>
    public static RunSummary AutoRun(GameEngine e, ref GameState s, string levelId, long now)
    {
        var r = e.Execute(ref s, new RunStart { LevelId = levelId }, now);
        Assert.True(r.Ok, r.Error);
        for (int i = 0; i < 1500 && s.Run.Active != null; i++)
        {
            var step = e.Execute(ref s, new RunAutoStep { Farming = true, FarmShops = true }, now);
            Assert.True(step.Ok, step.Error);
        }
        Assert.Null(s.Run.Active);
        return s.Run.LastSummary;
    }

    /// <summary>Equip the starter parts (head, body, arm).</summary>
    public static void EquipStarterParts(GameEngine e, ref GameState s, long now)
    {
        foreach (var p in s.Parts.Items.Values.OrderBy(x => x.Uid, StringComparer.Ordinal).ToList())
        {
            var kind = e.Content.PartsConfig.Defs[p.DefId].Kind;
            var slot = kind switch { PartKind.Head => PartSlot.Head, PartKind.Body => PartSlot.Body, PartKind.Tail => PartSlot.Tail, PartKind.Legs => PartSlot.Legs, _ => PartSlot.RightArm };
            if (s.Parts.Equipped.ContainsKey(slot)) continue;
            Assert.True(e.Execute(ref s, new PartEquip { Uid = p.Uid, Slot = slot }, now).Ok);
        }
    }

    /// <summary>Spend run rewards on upgrades: worm levels with Biomass, then levels of equipped parts.</summary>
    public static void SpendOnUpgrades(GameEngine e, ref GameState s, long now)
    {
        while (s.Wallet.CanAfford(Currencies.Biomass, WormSystem.NextLevelCost(s, e.Content)) && e.Execute(ref s, new WormLevelUp { Levels = 1 }, now).Ok) { }
        foreach (var uid in s.Parts.Equipped.Values.OrderBy(x => x, StringComparer.Ordinal).ToList())
            while (e.Execute(ref s, new PartLevelUp { Uid = uid, Levels = 1 }, now).Ok) { }
    }

    [Fact]
    public void Runs_UseRealProgressionWormStats()
    {
        var e = Engine();
        var s = e.NewPlayer("p", T0, 3);
        var expected = e.BuildLoadout(s, T0).WormStats.Value(StatId.Hp);
        Assert.True(expected > 0);
        Assert.True(e.Execute(ref s, new RunStart { LevelId = "main_1" }, T0).Ok);
        Assert.Equal(expected, s.Run.Active!.Loadout.WormStats.Value(StatId.Hp), 6); // no FallbackWormStats added
        Assert.Equal(e.Content.EconomyConfig.StartingWallet[Currencies.Stamina] - 5, s.Wallet.Get(Currencies.Stamina));
        Assert.Equal(5, s.Counter("stamina_spent"));
    }

    [Fact]
    public void WildcardDrops_ResolveToRealItemsOfTheRarity_Deterministically()
    {
        string Grant(long seed)
        {
            var e = Engine();
            var s = e.NewPlayer("p", T0, seed);
            s.Flags.Add(Features.DevUnlockAllFlag); // every item kind has a use (not about the reward gate)
            var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = T0 };
            ctx.Grant(new Reward(RewardKinds.Part, RewardWildcard.Any, 3, (int)Rarity.Epic));
            ctx.Grant(new Reward(RewardKinds.Rune, RewardWildcard.Any, 2, 1));
            ctx.Grant(new Reward(RewardKinds.Blueprint, RewardWildcard.Any, 2, (int)Rarity.Rare));
            ctx.Grant(new Reward(RewardKinds.Alien, RewardWildcard.Any, 1, (int)Rarity.Legendary));
            var newParts = s.Parts.Items.Values.Where(p => p.Rarity == (int)PartRarity.Epic).ToList();
            Assert.Equal(3, newParts.Count);
            Assert.All(newParts, p => Assert.True(e.Content.PartsConfig.Defs.ContainsKey(p.DefId)));
            Assert.Equal(2, s.Parts.Runes.Count);
            Assert.All(s.Parts.Runes.Values, r => Assert.True(e.Content.PartsConfig.Runes.ContainsKey(r.DefId)));
            var rareBps = s.City.Blueprints.Keys.Where(id => e.Content.Blueprint(id)!.Rarity == Rarity.Rare).ToList();
            Assert.Equal(2, rareBps.Count + s.Counter("blueprint_duplicates"));
            var alien = Assert.Single(s.Aliens.Owned.Values);
            Assert.Equal(Rarity.Legendary, e.Content.AlienType(alien.TypeId)!.Rarity);
            return string.Join(",", newParts.Select(p => p.DefId)) + "|" + string.Join(",", rareBps) + "|" + alien.TypeId;
        }
        Assert.Equal(Grant(11), Grant(11));
    }

    [Fact]
    public void ClearingLevels_SetsMainLevelMax_AndUnlocksFeatures()
    {
        var e = Engine();
        var s = e.NewPlayer("p", T0, 4);
        Assert.False(Features.IsUnlocked(s, e.Content, Features.Parts));
        Assert.Equal(1, RunSystem.MainLevelNumber(e.Content, "main_1"));
        Assert.Equal(11, RunSystem.MainLevelNumber(e.Content, "main_11"));
        Assert.Equal(0, RunSystem.MainLevelNumber(e.Content, "the_burrow_1"));
        s.Worm.Level = 30; // strong enough to win
        var sum = AutoRun(e, ref s, "main_1", T0);
        Assert.True(sum.Won);
        Assert.Equal(1, s.Counter(Requirements.MainLevelCounter));
        Assert.True(s.Onboarding.Unlocked.ContainsKey(Features.Parts)); // recorded by the run's own command
        Assert.DoesNotContain(s.Parts.Items.Values, p => p.DefId == RewardWildcard.Any);
        AutoRun(e, ref s, "main_1", T0 + Min); // replay does not lower the max
        Assert.Equal(1, s.Counter(Requirements.MainLevelCounter));
    }

    [Fact]
    public void LabResearch_UsesCityTimers_AndResearchSpeedBoon()
    {
        var e = Engine();
        var s = e.NewPlayer("p", T0, 5);
        s.Flags.Add(Features.DevUnlockAllFlag);
        s.Wallet.Add(Currencies.Data, 1_000_000);
        s.Wallet.Add(Currencies.Ore, 1_000); s.Wallet.Add(Currencies.Biomass, 1_000); // the starting wallet no longer has them
        var def = e.Content.LabResearch["res_burning_soul"];
        Assert.Equal("build the Laboratory in the City", e.Execute(ref s, new LabResearch { ResearchId = def.Id }, T0).Error);
        string lab = TestHelpers.WithLaboratory(e, s);
        long nominal = LaboratorySystem.NextNominalMs(s, e.Content);
        Assert.True(e.Execute(ref s, new LabResearch { ResearchId = def.Id }, T0).Ok);
        var t1 = TimerSystem.Find(s, LaboratorySystem.TimerKind, def.Id)!;
        Assert.Equal((long)Math.Ceiling(nominal / 1.1), t1.DurationMs); // Laboratory level 1: +10% speed
        Assert.True(e.Execute(ref s, new TimerSpeedUp { TimerId = t1.Id, Method = SpeedUpMethod.Free }, T0).Ok);
        long ready = LaboratorySystem.ReadyAtMs(s, def.Id);
        Assert.True(ready < t1.StartMs + t1.DurationMs);
        Assert.True(e.Execute(ref s, new LabCollect { ResearchId = def.Id }, ready).Ok);

        // Laboratory levels speed research up (+20% at level 10).
        CitySystem.Building(s, lab).Level = 10;
        double pct = CitySystem.ResearchSpeedPct(s, e.Content);
        Assert.Equal(20, pct, 6);
        long nominal2 = LaboratorySystem.NextNominalMs(s, e.Content);
        Assert.True(nominal2 > nominal, "every research makes the next one longer");
        Assert.True(e.Execute(ref s, new LabResearch { ResearchId = def.Id }, ready + 10 * Min).Ok);
        var t2 = TimerSystem.Find(s, LaboratorySystem.TimerKind, def.Id)!;
        Assert.Equal((long)Math.Ceiling(nominal2 / (1 + pct / 100.0)), t2.DurationMs);
    }

    /// <summary>
    /// Early balance: a brand-new player (starter kit, no cheats) auto-plays Ember Wastes (the first run without parts:
    /// they arrive with the Parts unlock), spends what the runs pay
    /// on worm and part levels between runs, and clears level 3 within a bounded number of runs (stamina regen
    /// between sessions), while level 1 is won right away.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NewPlayer_AutoPlays_ToLevel3_WithUpgradesBetweenRuns(long seed)
    {
        const int MaxRuns = 25;
        var e = Engine();
        var s = e.NewPlayer("p", T0, seed);
        long now = T0;
        EquipStarterParts(e, ref s, now);
        int runs = 0, wins = 0;
        bool firstWon = false;
        while (runs < MaxRuns && s.Counter(Requirements.MainLevelCounter) < 3)
        {
            int next = (int)s.Counter(Requirements.MainLevelCounter) + 1;
            if (s.Wallet.Get(Currencies.Stamina) < 5) now += 30 * Min; // a break: stamina regenerates
            var sum = AutoRun(e, ref s, "main_" + next, now);
            runs++;
            if (sum.Won) wins++;
            if (runs == 1) firstWon = sum.Won;
            now += Min;
            EquipStarterParts(e, ref s, now); // the starter parts arrive with the Parts unlock (level 1), not at creation
            SpendOnUpgrades(e, ref s, now);
            foreach (var kv in s.Wallet.Balances) Assert.True(kv.Value >= 0, kv.Key);
        }
        Assert.True(firstWon, "a fresh worm should win Ember Wastes 1");
        Assert.True(s.Counter(Requirements.MainLevelCounter) >= 3, $"stuck at {s.Counter(Requirements.MainLevelCounter)} after {runs} runs");
        // Worm Level unlocks with level 3 (GDD §16 level unlocks): the first upgrades are part levels, then worm levels.
        Assert.True(Features.IsUnlocked(s, e.Content, Features.WormLevel));
        Assert.True(s.Counter("part_levels") > 0, "runs paid for part levels");
    }
}
