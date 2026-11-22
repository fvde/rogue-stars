using System.Text.Json;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.City.Kit;

namespace RogueStars.Rules.Tests.City;

public class BreedingTests
{
    private const long T0 = TestHelpers.T0;

    private static (GameEngine e, GameState s, long t) WithPen(long seed = 42)
    {
        var e = TestHelpers.Engine();
        var s = Rich(e, seed);
        GrantAlien(e, s, "alien_blorp");
        GrantAlien(e, s, "alien_mossling");
        GrantAlien(e, s, "alien_emberkit");
        long t = BuildActive(e, ref s, "bp_breeding_pen", 4, 4, T0);
        return (e, s, t);
    }

    [Fact]
    public void Start_RequiresPenSlotsGrainAndTwoDifferentAliens()
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        GrantAlien(e, s, "alien_blorp");
        GrantAlien(e, s, "alien_mossling");
        Assert.Equal("no Breeding Pen", Fail(e, ref s, new BreedStart { ParentA = "alien_blorp", ParentB = "alien_mossling" }, T0));
        long t = BuildActive(e, ref s, "bp_breeding_pen", 4, 4, T0);
        Assert.Equal("parents must be two different aliens", Fail(e, ref s, new BreedStart { ParentA = "alien_blorp", ParentB = "alien_blorp" }, t));
        Assert.Equal("parent not owned", Fail(e, ref s, new BreedStart { ParentA = "alien_blorp", ParentB = "alien_solarch" }, t));
        long grain = s.Wallet.Get(Currencies.Grain);
        var r = Ok(e, ref s, new BreedStart { ParentA = "alien_blorp", ParentB = "alien_mossling" }, t);
        Assert.Equal(grain - 200, s.Wallet.Get(Currencies.Grain));
        var job = BreedingSystem.Job(s, (string)r.Payload!);
        Assert.Equal(2 * Hour, TimerSystem.Get(s, job.TimerId).DurationMs);
        Assert.Equal("all breeding slots busy", Fail(e, ref s, new BreedStart { ParentA = "alien_blorp", ParentB = "alien_mossling" }, t));
        Assert.Equal(1, s.Counter("breeds_started"));
    }

    [Fact]
    public void DurationAndCost_ByRarity_SpeedFromBuildingsAndModifiers()
    {
        var (e, s, t) = WithPen();
        Assert.Equal(500, BreedingSystem.GrainCost(e.Content, "alien_blorp", "alien_emberkit"));
        Assert.Equal(4 * Hour, BreedingSystem.DurationMs(s, e.Content, "alien_blorp", "alien_emberkit", t));
        CitySystem.Owned(s, "bp_breeding_pen").Level = 6; // +10% speed
        Assert.Equal((long)Math.Ceiling(4 * Hour / 1.1), BreedingSystem.DurationMs(s, e.Content, "alien_blorp", "alien_emberkit", t));
        s.Modifiers.Add(new Modifier { Target = "breeding_speed", Multiplier = 2 });
        Assert.Equal((long)Math.Ceiling(4 * Hour / 2.2), BreedingSystem.DurationMs(s, e.Content, "alien_blorp", "alien_emberkit", t));
        CitySystem.Owned(s, "bp_breeding_pen").Level = 10; // second slot
        Assert.Equal(2, BreedingSystem.Slots(s, e.Content));
    }

    [Fact]
    public void Collect_OnlyWhenDone_ThenChildPending_ParentsKept()
    {
        var (e, s, t) = WithPen();
        var r = Ok(e, ref s, new BreedStart { ParentA = "alien_blorp", ParentB = "alien_mossling" }, t);
        string job = (string)r.Payload!;
        Assert.Equal("breeding not finished", Fail(e, ref s, new BreedCollect { JobId = job }, t + Hour));
        string timer = BreedingSystem.Job(s, job).TimerId;
        Ok(e, ref s, new TimerSpeedUp { TimerId = timer, Method = SpeedUpMethod.Diamonds }, t + Hour);
        var c = Ok(e, ref s, new BreedCollect { JobId = job }, t + Hour);
        var child = BreedingSystem.Pending(s, (string)c.Payload!);
        Assert.Contains(child.TypeId, new[] { "alien_blorp", "alien_mossling" });
        Assert.Empty(s.Breeding.Jobs);
        Assert.Empty(s.Timers.Active);
        Assert.Equal(3, s.Aliens.Owned.Count);
        Assert.Equal("unknown breeding job", Fail(e, ref s, new BreedCollect { JobId = job }, t + Hour));
    }

    private static PendingChild BreedOnce(GameEngine e, ref GameState s, long t)
    {
        var r = Ok(e, ref s, new BreedStart { ParentA = "alien_blorp", ParentB = "alien_mossling" }, t);
        var c = Ok(e, ref s, new BreedCollect { JobId = (string)r.Payload! }, t + 2 * Hour);
        return BreedingSystem.Pending(s, (string)c.Payload!);
    }

    [Fact]
    public void Breeding_IsDeterministic()
    {
        var (e1, s1, t1) = WithPen(99);
        var (e2, s2, t2) = WithPen(99);
        var c1 = BreedOnce(e1, ref s1, t1);
        var c2 = BreedOnce(e2, ref s2, t2);
        Assert.Equal(JsonSerializer.Serialize(c1, TestHelpers.Json), JsonSerializer.Serialize(c2, TestHelpers.Json));
    }

    [Fact]
    public void Replace_SwapsGenesAndTraits_KeepsProgress_OnePerType()
    {
        var (e, s, t) = WithPen();
        var before = AlienSystem.Owned(s, "alien_blorp");
        before.Level = 7; before.Stars = 2;
        AlienSystem.Owned(s, "alien_mossling").Level = 7; AlienSystem.Owned(s, "alien_mossling").Stars = 2;
        var child = BreedOnce(e, ref s, t);
        Ok(e, ref s, new BreedResolve { ChildId = child.Id, Keep = BreedKeep.Replace }, t + 2 * Hour);
        var a = AlienSystem.Owned(s, child.TypeId);
        Assert.Equal(child.Genes.Hp, a.Genes.Hp);
        Assert.Equal(child.Traits, a.Traits);
        Assert.Equal(7, a.Level);
        Assert.Equal(2, a.Stars);
        Assert.Equal(1, a.Generation);
        Assert.Equal(3, s.Aliens.Owned.Count);
        Assert.Empty(s.Breeding.Pending);
        Assert.Equal("unknown child", Fail(e, ref s, new BreedResolve { ChildId = child.Id, Keep = BreedKeep.Replace }, t + 2 * Hour));
    }

    [Fact]
    public void Discard_GivesShardsAndGrainRefund()
    {
        var (e, s, t) = WithPen();
        var child = BreedOnce(e, ref s, t);
        long grain = s.Wallet.Get(Currencies.Grain);
        var genes = TestHelpers.Clone(AlienSystem.Owned(s, child.TypeId).Genes);
        Ok(e, ref s, new BreedResolve { ChildId = child.Id, Keep = BreedKeep.Discard }, t + 2 * Hour);
        Assert.Equal(1, s.Wallet.Get(Currencies.AlienShards(child.TypeId)));
        Assert.Equal(grain + 60, s.Wallet.Get(Currencies.Grain));
        Assert.Equal(genes.Atk, AlienSystem.Owned(s, child.TypeId).Genes.Atk);
        Assert.Equal(1, s.Counter("breeds_discarded"));
    }

    [Fact]
    public void PendingChildrenAreLimited()
    {
        var (e, s, t) = WithPen();
        for (int i = 0; i < 3; i++) { BreedOnce(e, ref s, t); t += 2 * Hour; }
        var r = Ok(e, ref s, new BreedStart { ParentA = "alien_blorp", ParentB = "alien_mossling" }, t);
        Assert.Equal("too many children waiting for a decision", Fail(e, ref s, new BreedCollect { JobId = (string)r.Payload! }, t + 2 * Hour));
    }

    private static OwnedAlien Parent(string type, double gene, params string[] traits)
    {
        var a = new OwnedAlien { TypeId = type, Genes = new AlienGenes { Hp = gene, Atk = gene, Def = gene, Speed = gene, Work = gene } };
        a.Traits.AddRange(traits);
        return a;
    }

    [Fact]
    public void Inheritance_GenesStayWithinBounds_TraitsFromParentsOrMutation()
    {
        var c = TestHelpers.Engine().Content;
        var a = Parent("alien_blorp", -10, "trait_tough", "trait_fierce");
        var b = Parent("alien_zib", 20, "trait_swift");
        var parentTraits = new[] { "trait_tough", "trait_fierce", "trait_swift" };
        var rng = new Rng(5);
        int mutations = 0, inherited = 0;
        const int N = 2000;
        foreach (double q in new[] { 0.0, 10.0 })
        {
            for (int i = 0; i < N; i++)
            {
                var child = BreedingSystem.RollChild(rng, c, a, b, q);
                foreach (var g in new[] { child.Genes.Hp, child.Genes.Atk, child.Genes.Def, child.Genes.Speed, child.Genes.Work })
                    Assert.InRange(g, -10 - 8 + q - 0.01, 20 + 8 + q + 0.01);
                Assert.InRange(child.Traits.Count, 0, 3);
                Assert.Equal(child.Traits.Count, child.Traits.Distinct().Count());
                int foreign = child.Traits.Count(t => !parentTraits.Contains(t));
                Assert.True(foreign <= (child.Mutated ? 1 : 0));
                if (child.Mutated) mutations++;
                inherited += child.Traits.Count(t => parentTraits.Contains(t));
            }
        }
        // ~8% (q=0) and ~13% (q=10) mutation rate; ~50% / ~60% inherit rate.
        Assert.InRange(mutations, (int)(N * 0.17), (int)(N * 0.25));
        Assert.InRange(inherited, (int)(3 * N * 1.0), (int)(3 * N * 1.2));
    }

    [Fact]
    public void Inheritance_ClampsToGeneBounds_AndQualityRaisesMean()
    {
        var c = TestHelpers.Engine().Content;
        var hi = Parent("alien_blorp", 58);
        var hi2 = Parent("alien_zib", 60);
        var rng = new Rng(11);
        for (int i = 0; i < 500; i++)
            Assert.InRange(BreedingSystem.RollChild(rng, c, hi, hi2, 20).Genes.Atk, -30, 60);

        var p1 = Parent("alien_blorp", 0);
        var p2 = Parent("alien_zib", 0);
        double sum0 = 0, sum5 = 0;
        var r0 = new Rng(3); var r5 = new Rng(3);
        for (int i = 0; i < 1000; i++)
        {
            sum0 += BreedingSystem.RollChild(r0, c, p1, p2, 0).Genes.Work;
            sum5 += BreedingSystem.RollChild(r5, c, p1, p2, 5).Genes.Work;
        }
        Assert.InRange(sum0 / 1000, -1, 1);
        Assert.InRange(sum5 / 1000, 4, 6);
    }

    [Fact]
    public void GeneLab_AddsQualityToNewJobs()
    {
        var (e, s, t) = WithPen();
        Assert.Equal(0, BreedingSystem.QualityBonus(s, e.Content));
        t = BuildActive(e, ref s, "bp_gene_lab", 6, 6, t);
        Assert.Equal(2, BreedingSystem.QualityBonus(s, e.Content), 6);
        var r = Ok(e, ref s, new BreedStart { ParentA = "alien_blorp", ParentB = "alien_mossling" }, t);
        Assert.Equal(2, BreedingSystem.Job(s, (string)r.Payload!).QualityBonus, 6);
        // Gene Lab also speeds breeding by 5%.
        Assert.Equal((long)Math.Ceiling(2 * Hour / 1.05), TimerSystem.Get(s, BreedingSystem.Job(s, (string)r.Payload!).TimerId).DurationMs);
    }
}
