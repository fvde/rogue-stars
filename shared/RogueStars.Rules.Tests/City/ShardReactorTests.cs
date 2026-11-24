using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.City.Kit;

namespace RogueStars.Rules.Tests.City;

/// <summary>The Shard Reactor (docs/design/pull-system.md "Shard Reactor"): jobs, worker slots, costs, targets of the same type and a higher rarity.</summary>
public class ShardReactorTests
{
    private const long T0 = TestHelpers.T0;
    private const long Min = 60_000;
    private const string R = ContentDb.ShardReactorId;

    /// <summary>A rich player with the Reactor built and <paramref name="workers"/> aliens working in it.</summary>
    private static (GameEngine e, GameState s, long t) Ready(int workers = 1, int stars = 1)
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        long t = BuildActive(e, ref s, R, 4, 4, T0);
        CitySystem.Owned(s, R).Stars = stars;
        string[] aliens = { "alien_blorp", "alien_mossling", "alien_pebblehorn" };
        for (int i = 0; i < workers; i++)
        {
            GrantAlien(e, s, aliens[i]);
            Ok(e, ref s, new AlienAssign { AlienTypeId = aliens[i], BlueprintId = R }, t);
        }
        return (e, s, t);
    }

    [Fact]
    public void Content_MythicBuilding_InTheBlueprintSignal_WithItsOwnStarsAndBanner()
    {
        var c = ContentDb.Default;
        var def = c.Blueprint(R)!;
        Assert.Equal(Rarity.Mythic, def.Rarity);
        Assert.Equal("Shard Reactor", def.Name);
        Assert.Contains(c.GachaConfig.Types[GachaType.Blueprint].Tiers.Single(t => t.Tier == Rarity.Mythic).Pool, en => en.Id == R);
        Assert.Contains(c.GachaConfig.Rotations.Single(r => r.Type == GachaType.Blueprint).Themes, th => th.FeaturedId == R);
        var b = new OwnedBlueprint { Id = R };
        int Slots(int stars) { b.Stars = stars; return CitySystem.WorkerSlots(c, def, b); }
        Assert.Equal((1, 1, 2, 2, 3, 3), (Slots(1), Slots(2), Slots(3), Slots(5), Slots(6), Slots(10)));
        // whole numbers on every cost, ratio and duration
        var d = c.ShardReactor;
        Assert.Equal(new long[] { 0, 4, 16, 64, 256 }, d.RatioByGap);
        Assert.Equal(10, d.OutputPerUnit);
    }

    [Fact]
    public void Quote_RatioByRarityGap_FuelAndTimeByTargetRarity()
    {
        var e = TestHelpers.Engine(); var s = Rich(e); var c = e.Content;
        // Common alien → Rare (gap 1): 4 per shard; Common → Mythic (gap 4): 256 per shard
        var q = ShardReactorSystem.Quote(s, c, ReactorKind.Alien, "alien_blorp", "alien_sporeling", 1);
        Assert.Null(q.Error);
        Assert.Equal(c.AlienType("alien_sporeling")!.Rarity - c.AlienType("alien_blorp")!.Rarity, 1);
        Assert.Equal((10L, 40L, Currencies.Grain, c.ShardReactor.AlienFuelPerUnit[1]), (q.OutputShards, q.SourceShards, q.FuelCurrency, q.Fuel));
        Assert.Equal(20 * Min, q.DurationMs);
        var big = ShardReactorSystem.Quote(s, c, ReactorKind.Alien, "alien_blorp", "alien_nebulisk", 3);
        Assert.Equal((30L, 30L * 256, 3 * c.ShardReactor.AlienFuelPerUnit[4], 3 * 180 * Min), (big.OutputShards, big.SourceShards, big.Fuel, big.DurationMs));
        var bp = ShardReactorSystem.Quote(s, c, ReactorKind.Blueprint, "bp_ore_mine", "bp_command_spire", 1);
        Assert.Equal((Currencies.Ore, c.ShardReactor.BlueprintFuelPerUnit[4], 2560L), (bp.FuelCurrency, bp.Fuel, bp.SourceShards));
        // same or lower rarity, other kind, bad units: refused
        Assert.NotNull(ShardReactorSystem.Quote(s, c, ReactorKind.Alien, "alien_nebulisk", "alien_blorp", 1).Error);
        Assert.NotNull(ShardReactorSystem.Quote(s, c, ReactorKind.Alien, "alien_blorp", "alien_mossling", 1).Error);
        Assert.NotNull(ShardReactorSystem.Quote(s, c, ReactorKind.Alien, "alien_blorp", "bp_command_spire", 1).Error);
        Assert.NotNull(ShardReactorSystem.Quote(s, c, ReactorKind.Alien, "alien_blorp", "alien_nebulisk", 0).Error);
        Assert.NotNull(ShardReactorSystem.Quote(s, c, ReactorKind.Alien, "alien_blorp", "alien_nebulisk", c.ShardReactor.MaxUnits + 1).Error);
    }

    [Fact]
    public void Targets_AreTheSameTypeAndAHigherRarityOnly()
    {
        var e = TestHelpers.Engine(); var s = Rich(e); var c = e.Content;
        foreach (var kind in new[] { ReactorKind.Alien, ReactorKind.Blueprint })
            foreach (var src in ShardReactorSystem.Items(c, kind))
            {
                var sr = ShardReactorSystem.RarityOf(c, kind, src)!.Value;
                var targets = ShardReactorSystem.Targets(s, c, kind, src);
                Assert.All(targets, t => Assert.True(ShardReactorSystem.RarityOf(c, kind, t)!.Value > sr));
                Assert.Equal(ShardReactorSystem.Items(c, kind).Count(t => ShardReactorSystem.RarityOf(c, kind, t)!.Value > sr), targets.Count);
                if (kind == ReactorKind.Alien) Assert.All(targets, t => Assert.NotNull(c.AlienType(t)));
                else Assert.All(targets, t => Assert.NotNull(c.Blueprint(t)));
            }
        Assert.Empty(ShardReactorSystem.Targets(s, c, ReactorKind.Alien, "alien_nebulisk")); // nothing above Mythic
    }

    [Fact]
    public void Job_PaysShardsAndFuel_TakesTime_CollectPaysTheTarget_AndUnlocksIt()
    {
        var (e, s, t) = Ready();
        var c = e.Content;
        s.Wallet.Add(Currencies.AlienShards("alien_blorp"), 2560);
        long grain = s.Wallet.Get(Currencies.Grain);
        Assert.Null(AlienSystem.Owned(s, "alien_nebulisk"));
        var id = (string)Ok(e, ref s, new ReactorStart { Kind = ReactorKind.Alien, SourceId = "alien_blorp", TargetId = "alien_nebulisk", Units = 1 }, t).Payload;
        Assert.Equal(0, s.Wallet.Get(Currencies.AlienShards("alien_blorp")));
        Assert.Equal(grain - c.ShardReactor.AlienFuelPerUnit[4], s.Wallet.Get(Currencies.Grain));
        var job = ShardReactorSystem.Job(s, id)!;
        Assert.Equal(TimerSystem.Get(s, job.TimerId)!.EndsMs, t + 180 * Min);
        Assert.Contains("not finished", Fail(e, ref s, new ReactorCollect { JobId = id }, t + 179 * Min));
        Assert.Contains("nothing", Fail(e, ref s, new ReactorCollect(), t + 179 * Min));
        Ok(e, ref s, new ReactorCollect { JobId = id }, t + 180 * Min);
        Assert.Equal(10, s.Wallet.Get(Currencies.AlienShards("alien_nebulisk")));
        Assert.Null(AlienSystem.Owned(s, "alien_nebulisk")); // 10 of the 50 to unlock
        Assert.Empty(s.Reactor.Jobs);
        Assert.Equal((1L, 2560L, 10L), (s.Counter("reactor_jobs_started"), s.Counter("reactor_shards_in"), s.Counter("reactor_shards_out")));

        // 4 more units → 50: the alien unlocks at 1★ with them
        s.Wallet.Add(Currencies.AlienShards("alien_blorp"), 4 * 2560);
        Ok(e, ref s, new ReactorStart { Kind = ReactorKind.Alien, SourceId = "alien_blorp", TargetId = "alien_nebulisk", Units = 4 }, t + 200 * Min);
        Ok(e, ref s, new ReactorCollect(), t + 200 * Min + 4 * 180 * Min);
        Assert.Equal(1, AlienSystem.Owned(s, "alien_nebulisk")!.Stars);
        Assert.Equal(0, s.Wallet.Get(Currencies.AlienShards("alien_nebulisk")));
    }

    [Fact]
    public void Jobs_RunInParallel_OnePerWorkingAlien_UpToThreeSlots()
    {
        var (e, s, t) = Ready(workers: 1, stars: 1);
        s.Wallet.Add(Currencies.BlueprintShards("bp_ore_mine"), 100_000);
        ReactorStart Job() => new ReactorStart { Kind = ReactorKind.Blueprint, SourceId = "bp_ore_mine", TargetId = "bp_spore_vats", Units = 1 };
        Ok(e, ref s, Job(), t);
        Assert.Contains("busy", Fail(e, ref s, Job(), t));
        Assert.Equal(1, ShardReactorSystem.JobSlots(s, e.Content));

        // 6★: three worker slots, three aliens → three parallel jobs
        var (e2, s2, t2) = Ready(workers: 3, stars: 6);
        s2.Wallet.Add(Currencies.BlueprintShards("bp_ore_mine"), 100_000);
        Assert.Equal(3, ShardReactorSystem.JobSlots(s2, e2.Content));
        for (int i = 0; i < 3; i++) Ok(e2, ref s2, Job(), t2);
        Assert.Contains("busy", Fail(e2, ref s2, Job(), t2));
        Assert.Equal(3, s2.Reactor.Jobs.Count);
        // speed-ups apply like any city timer
        s2.Wallet.Add(Currencies.Diamonds, 1000);
        Ok(e2, ref s2, new TimerSpeedUp { TimerId = s2.Reactor.Jobs[0].TimerId, Method = SpeedUpMethod.Diamonds }, t2 + Min);
        Assert.Equal(10, (long)Ok(e2, ref s2, new ReactorCollect(), t2 + Min).Payload);
        Assert.Equal(2, s2.Reactor.Jobs.Count);
    }

    [Fact]
    public void Start_IsValidated_BeforeAnythingIsPaid()
    {
        var e = TestHelpers.Engine(); var s = Rich(e);
        s.Wallet.Add(Currencies.AlienShards("alien_blorp"), 100_000);
        var ok = new ReactorStart { Kind = ReactorKind.Alien, SourceId = "alien_blorp", TargetId = "alien_sporeling", Units = 1 };
        Assert.Contains("Shard Reactor", Fail(e, ref s, ok, T0)); // not built
        var (e2, s2, t) = Ready(workers: 0);
        s2.Wallet.Add(Currencies.AlienShards("alien_blorp"), 100);
        Assert.Contains("no alien", Fail(e2, ref s2, ok, t)); // nobody works there
        GrantAlien(e2, s2, "alien_mossling");
        Ok(e2, ref s2, new AlienAssign { AlienTypeId = "alien_mossling", BlueprintId = R }, t);
        Assert.Contains("higher rarity", Fail(e2, ref s2, new ReactorStart { Kind = ReactorKind.Alien, SourceId = "alien_sporeling", TargetId = "alien_blorp" }, t));
        Assert.Contains("unknown", Fail(e2, ref s2, new ReactorStart { Kind = ReactorKind.Blueprint, SourceId = "alien_blorp", TargetId = "bp_command_spire" }, t));
        Assert.Contains("not enough", Fail(e2, ref s2, new ReactorStart { Kind = ReactorKind.Alien, SourceId = "alien_blorp", TargetId = "alien_sporeling", Units = 3 }, t));
        Assert.Equal(100, s2.Wallet.Get(Currencies.AlienShards("alien_blorp")));
        // a 10★ target is refused
        GrantAlien(e2, s2, "alien_sporeling");
        AlienSystem.Owned(s2, "alien_sporeling")!.Stars = 10;
        Assert.Contains("max stars", Fail(e2, ref s2, ok, t));
    }

    [Fact]
    public void ExcessShards_AreWhatIsLeftAfter10Stars()
    {
        var e = TestHelpers.Engine(); var s = Rich(e); var c = e.Content;
        GrantAlien(e, s, "alien_blorp");               // 1★: 950 more to 10★
        s.Wallet.Add(Currencies.AlienShards("alien_blorp"), 1000);
        Assert.Equal(50, ShardReactorSystem.ExcessShards(s, c, ReactorKind.Alien, "alien_blorp"));
        AlienSystem.Owned(s, "alien_blorp")!.Stars = 10;
        Assert.Equal(1000, ShardReactorSystem.ExcessShards(s, c, ReactorKind.Alien, "alien_blorp"));
    }
}
