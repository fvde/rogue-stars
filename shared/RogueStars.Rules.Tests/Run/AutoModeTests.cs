using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Auto mode (GDD §3.1): RunAutoStep only performs clear, choice-free steps and stops at every real decision
/// (RunSystem.RequiresPlayer); Farming resolves decisions with their default but never buys in shops.
/// </summary>
public class AutoModeTests
{
    private static readonly RunAutoStep Auto = new();
    private static readonly RunAutoStep Farm = new() { Farming = true };

    /// <summary>Plain auto is refused at a player decision and leaves the state untouched.</summary>
    private static void AssertStops(GameEngine e, ref GameState s, RunDecision expected)
    {
        var run = s.Run.Active!;
        Assert.Equal(expected, RunSystem.Decision(e.Content, run));
        Assert.True(RunSystem.RequiresPlayer(e.Content, run));
        Assert.False(RunSystem.RequiresPlayer(e.Content, run, farming: true));
        long version = s.Version;
        var phase = run.Phase;
        var r = RunTestKit.Exec(e, ref s, Auto);
        Assert.False(r.Ok);
        Assert.Contains("needs a player decision", r.Error);
        Assert.Equal(version, s.Version);
        Assert.Equal(phase, s.Run.Active!.Phase);
    }

    /// <summary>A line board of one tile kind; the first roll always lands on (or stops at) such a tile.</summary>
    private static GameState OnLine(GameEngine e, string tile, long seed = 42)
    {
        var s = RunTestKit.StartRun(e, seed: seed);
        RunTestKit.LineBoard(s.Run.Active!, Enumerable.Repeat(tile, 30).ToArray());
        Assert.True(RunTestKit.Exec(e, ref s, Auto).Ok); // roll: a clear step
        return s;
    }

    [Fact]
    public void Auto_ContinuesThroughClearTiles_AndBattles_UntilADecision()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        var clear = new[] { "loot", "buff_atk", "enemy_1", "hazard_spikes", "coin_pickup", "debuff_def", "shard", "enemy_2", "loot_rich", "hazard_toll" };
        RunTestKit.LineBoard(run, Enumerable.Range(0, 60).Select(i => clear[i % clear.Length]).ToArray());
        int steps = 0;
        while (s.Run.Active != null && !RunSystem.RequiresPlayer(e.Content, s.Run.Active))
        {
            Assert.True(RunTestKit.Exec(e, ref s, Auto).Ok);
            Assert.True(++steps < 200);
        }
        // only battles' level-ups (drafts) can stop auto on this board; otherwise it reaches the boss and ends
        if (s.Run.Active != null) AssertStops(e, ref s, RunDecision.Draft);
        Assert.True(s.Counter("run_rolls") > 0);
        Assert.True(s.Counter("run_battles_won") > 0 || s.Run.Active != null); // an early level-up (loot XP) may stop it first
    }

    [Fact]
    public void Auto_StopsAtShop_PlayerBuysSkill_ThenAutoContinues()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, new[] { "shop" }.Concat(Enumerable.Repeat("empty", 20)).ToArray());
        run.ExtraDicePermanent = 5;
        run.Coins = 1000;
        Assert.True(RunTestKit.Exec(e, ref s, Auto).Ok);          // passes into the shop: forced stop
        Assert.Equal(RunPhase.Shop, s.Run.Active!.Phase);
        AssertStops(e, ref s, RunDecision.Shop);
        Assert.Equal(1000, s.Run.Active!.Coins);
        int skill = s.Run.Active.Shop.FindIndex(o => o.Kind == ShopOfferKind.Skill);
        Assert.True(skill >= 0);
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopBuy { Index = skill }).Ok);
        AssertStops(e, ref s, RunDecision.Shop);                  // still shopping after a purchase
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopLeave()).Ok);
        Assert.False(RunSystem.RequiresPlayer(e.Content, s.Run.Active!));
        Assert.True(RunTestKit.Exec(e, ref s, Auto).Ok);          // auto resumes
        Assert.Equal(2, s.Counter("run_rolls"));
    }

    [Fact]
    public void Farming_BuysSensibly_HealFirstThenSkillsAndPerks_ThenLeaves()
    {
        GameState AtShop(GameEngine e)
        {
            var s = RunTestKit.StartRun(e);
            var run = s.Run.Active!;
            RunTestKit.LineBoard(run, new[] { "shop" }.Concat(Enumerable.Repeat("empty", 20)).ToArray());
            run.ExtraDicePermanent = 5;
            run.Coins = 1000;
            run.WormHp = 1;                                        // low HP: the shop offers the heal, the bot heuristic takes it
            Assert.True(RunTestKit.Exec(e, ref s, Auto).Ok);
            return s;
        }
        var e = RunTestKit.Engine();
        var a = AtShop(e);
        var heal = a.Run.Active!.Shop.FindIndex(o => o.Kind == ShopOfferKind.Heal);
        Assert.Equal(heal, RunSystem.FarmShopPick(e.Content, a.Run.Active));   // low HP: the heal first
        Assert.True(RunTestKit.Exec(e, ref a, Farm).Ok);
        Assert.Equal(RunPhase.Shop, a.Run.Active!.Phase);                       // one purchase per step
        Assert.True(a.Run.Active.Shop[heal].Sold);
        Assert.True(a.Run.Active.WormHp > 1);
        int guard = 0;
        while (a.Run.Active!.Phase == RunPhase.Shop && guard++ < 20)
        {
            int pick = RunSystem.FarmShopPick(e.Content, a.Run.Active);
            if (pick >= 0) Assert.NotEqual(ShopOfferKind.Heal, a.Run.Active.Shop[pick].Kind);
            Assert.True(RunTestKit.Exec(e, ref a, Farm).Ok);
        }
        Assert.Equal(RunPhase.AwaitRoll, a.Run.Active!.Phase);                  // left once nothing more is worth buying
        Assert.True(a.Run.Active.ShopBuys >= 4, "rich: bought the heal and the skills on offer (3 skills + heal + stat perk)");
        Assert.Contains(a.Run.Active.Gains, g => g.Source == "shop" && g.SkillId != null);

        var b = AtShop(e);                                                     // broke: leaves without buying
        b.Run.Active!.Coins = 0;
        Assert.Equal(-1, RunSystem.FarmShopPick(e.Content, b.Run.Active));
        Assert.True(RunTestKit.Exec(e, ref b, Farm).Ok);
        Assert.Equal(RunPhase.AwaitRoll, b.Run.Active!.Phase);

        var c = AtShop(e);                                                     // FarmShops alone is not farming
        Assert.False(RunTestKit.Exec(e, ref c, new RunAutoStep { FarmShops = true }).Ok);
    }

    [Fact]
    public void Auto_StopsAtDraft_FarmingPicksBest()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.Phase = RunPhase.Draft;
        run.PendingDrafts = 1;
        run.DraftOffer.AddRange(new[] { "gen_guard", "crit_keen", "gen_swift" });
        AssertStops(e, ref s, RunDecision.Draft);
        string best = RunDraft.BestPick(e.Content, s.Run.Active!);
        Assert.True(RunTestKit.Exec(e, ref s, Farm).Ok);
        Assert.Contains(best, s.Run.Active!.Skills);
        Assert.NotEqual(RunPhase.Draft, s.Run.Active.Phase);
    }

    [Theory]
    [InlineData("event", RunDecision.Event)]
    [InlineData("treasure", RunDecision.Treasure)]
    [InlineData("campfire", RunDecision.Campfire)]
    public void Auto_StopsAtEventsWithOptions_FarmingTakesDefault(string tile, RunDecision decision)
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = OnLine(e, tile);
        Assert.Equal(RunPhase.Event, s.Run.Active!.Phase);
        AssertStops(e, ref s, decision);
        long resolved = s.Counter("run_events_resolved");
        Assert.True(RunTestKit.Exec(e, ref s, Farm).Ok);
        Assert.Equal(resolved + 1, s.Counter("run_events_resolved"));
    }

    [Fact]
    public void Auto_StopsAtBranch_FarmingTakesFirstLeg()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 20).ToArray());
        RunTestKit.SideArm(run, 1, 3, 2); // a longer side leg: the ways land on different tiles
        run.ExtraDicePermanent = 2;
        Assert.True(RunTestKit.Exec(e, ref s, Auto).Ok);
        Assert.Equal(RunPhase.ChooseBranch, s.Run.Active!.Phase);
        AssertStops(e, ref s, RunDecision.Branch);
        Assert.True(RunTestKit.Exec(e, ref s, Farm).Ok);
        Assert.Contains(2, s.Run.Active!.Trail);
    }

    [Fact]
    public void Auto_StopsAtRevive_GivesUpOnlyWithoutARevive()
    {
        var e = RunTestKit.Engine(RunTestKit.Weak());
        var s = OnLine(e, "enemy_1");
        Assert.Equal(RunPhase.Defeated, s.Run.Active!.Phase);
        AssertStops(e, ref s, RunDecision.Revive);

        var farmed = s;
        Assert.True(RunTestKit.Exec(e, ref farmed, Farm).Ok);      // farming: give up (no hidden spending)
        Assert.Null(farmed.Run.Active);
        Assert.False(farmed.Run.LastSummary.Won);

        var used = OnLine(e, "enemy_1");
        used.Run.Active!.ReviveUsed = true;                        // only "give up" left: a clear step
        Assert.Equal(RunDecision.GiveUp, RunSystem.Decision(e.Content, used.Run.Active));
        Assert.False(RunSystem.RequiresPlayer(e.Content, used.Run.Active));
        Assert.True(RunTestKit.Exec(e, ref used, Auto).Ok);
        Assert.Null(used.Run.Active);
    }

    [Fact]
    public void Auto_StopsAtBurrowChoice_FarmingCashesOut()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e, "the_burrow_1", mode: RunMode.Burrow);
        for (int i = 0; i < 400 && s.Run.Active != null && s.Run.Active.Phase != RunPhase.BurrowChoice; i++)
            Assert.True(RunTestKit.Exec(e, ref s, Farm).Ok);
        Assert.Equal(RunPhase.BurrowChoice, s.Run.Active!.Phase);
        AssertStops(e, ref s, RunDecision.BurrowChoice);
        Assert.True(RunTestKit.Exec(e, ref s, Farm).Ok);
        Assert.Null(s.Run.Active);
        Assert.True(s.Run.LastSummary.Won);
    }

    [Fact]
    public void FinishedRun_HasNoDecision()
    {
        var e = RunTestKit.Engine();
        Assert.Equal(RunDecision.None, RunSystem.Decision(e.Content, null!));
        Assert.False(RunSystem.RequiresPlayer(e.Content, null!));
    }
}
