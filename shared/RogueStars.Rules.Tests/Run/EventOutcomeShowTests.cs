using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Event outcome rules (owner 2026-11, docs/design event-outcomes analysis): a result sheet only for news (a gamble, a random
/// pick, a milestone), deterministic results fly in as popups, results without effect are a one-line toast, a lost fight goes
/// straight to the revive. Plus the three bugs found while mapping: lost event fights showed the win text and applied their
/// follow-ups silently after a revive, "nearby enemies grow stronger" was never listed, boss-wake numbers drifted.
/// </summary>
public class EventOutcomeShowTests
{
    private static (GameEngine e, GameState s) AtEvent(string eventId, RunTestKit.WormSetup? worm = null, params string[] ahead)
    {
        var e = RunTestKit.Engine(worm ?? RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        var tiles = new List<string> { "event" };
        tiles.AddRange(ahead.Length > 0 ? ahead : Enumerable.Repeat("empty", 12));
        RunTestKit.LineBoard(run, tiles.ToArray());
        run.Position = 1;
        run.Phase = RunPhase.Event;
        run.EventId = eventId;
        run.EventTile = 1;
        run.Coins = 500;
        run.Shards = 5;
        run.WormHp = Math.Max(1, run.WormMaxHp / 2);
        return (e, s);
    }

    private static RunOutcome Choose(GameEngine e, ref GameState s, int option)
    {
        int seq = s.Run.Active!.OutcomeSeq;
        var r = RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = option });
        Assert.True(r.Ok, r.Error);
        Assert.True(s.Run.Active!.OutcomeSeq > seq, "an outcome is published");
        return s.Run.Active.LastOutcome!;
    }

    private static int Opt(string eventId, string text) => ContentDb.Default.RunEvents[eventId].Options.FindIndex(o => o.Text.StartsWith(text));

    [Theory]
    [InlineData("abandoned_cache", "Search it")]
    [InlineData("strange_spores", "Eat them")]
    [InlineData("meteor_shower", "Run through it")]
    [InlineData("wild_beast", "Try to tame it")]
    public void Gambles_KeepTheSheet_WithTheChanceMeter(string ev, string option)
    {
        var (e, s) = AtEvent(ev);
        var o = Choose(e, ref s, Opt(ev, option));
        Assert.Equal(OutcomeShow.Sheet, o.Show);
        Assert.True(o.Chance);
    }

    [Theory]
    [InlineData("unstable_reactor", "Absorb the surge")]
    [InlineData("smuggler", "Trade vitality")]
    [InlineData("shard_altar", "Offer 2 portal shards")]
    [InlineData("cursed_relic", "Claim it")]
    public void RandomSkill_KeepsTheSheet_LedByTheSkill_WithEveryCost(string ev, string option)
    {
        var (e, s) = AtEvent(ev);
        var o = Choose(e, ref s, Opt(ev, option));
        Assert.Equal(OutcomeShow.Sheet, o.Show);
        Assert.Equal(OutcomeKind.SkillGained, o.Lines[0].Kind); // the news first
        var opt = ContentDb.Default.RunEvents[ev].Options[Opt(ev, option)];
        foreach (var fx in opt.Outcomes[0].Effects)
        {
            if (fx.Kind == RunEffectKind.StatPercent) Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Stat && l.Id == fx.Stat.ToString());
            if (fx.Kind == RunEffectKind.BossStatPct) Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.BossStat);
            if (fx.Kind == RunEffectKind.Meter) Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Awakening);
        }
        if (opt.ShardCost > 0) Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Shards && l.Amount == -opt.ShardCost);
    }

    [Fact]
    public void RandomLoot_GravityWell_KeepsTheSheet_LootFirst()
    {
        var (e, s) = AtEvent("gravity_well");
        var o = Choose(e, ref s, Opt("gravity_well", "Dive in"));
        Assert.Equal(OutcomeShow.Sheet, o.Show);
        Assert.Equal(OutcomeKind.AccountLoot, o.Lines[0].Kind);
        Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.AwakenRate);
    }

    [Fact]
    public void TreasurePod_WonFight_SheetForTheLoot_ListsEnemiesStronger()
    {
        var (e, s) = AtEvent("treasure_pod", null, "enemy_1", "empty", "enemy_1", "empty", "empty", "empty", "empty", "empty");
        var o = Choose(e, ref s, Opt("treasure_pod", "Fight the guardian"));
        Assert.Equal(OutcomeShow.Sheet, o.Show);
        Assert.Equal(OutcomeKind.AccountLoot, o.Lines[0].Kind); // the chest contents lead, the fight follows
        Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Battle && l.Id == "won");
        var stronger = o.Lines.Single(l => l.Kind == OutcomeKind.EnemiesStronger);
        Assert.Equal(10, stronger.Amount);
        Assert.Equal("2", stronger.Id);
        Assert.False(stronger.Good);
    }

    [Fact]
    public void Campfire_Rest_FliesIn_AndListsEnemiesStronger()
    {
        var (e, s) = AtEvent("campfire", null, "enemy_1", "empty", "empty", "empty", "empty", "empty");
        var o = Choose(e, ref s, Opt("campfire", "Rest"));
        Assert.Equal(OutcomeShow.Popups, o.Show);
        Assert.Null(o.Toast);
        Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Hp && l.Good);
        Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.EnemiesStronger && l.Amount == 12);
    }

    [Theory]
    [InlineData("lost_explorer", "Help them")]
    [InlineData("alien_medic", "Buy a tonic")]
    [InlineData("shard_altar", "Pray")]
    [InlineData("meteor_shower", "Take shelter")]
    [InlineData("echoing_shrine", "Meditate")]
    [InlineData("fuel_leak", "Patch the leak")]
    [InlineData("fuel_leak", "Ignore it")]
    [InlineData("black_market", "Buy a signal beacon")]
    [InlineData("training_dummy", "Train")]
    [InlineData("boss_scout", "Let it go")]
    [InlineData("campfire", "Train")]
    public void Deterministic_FliesIn_WithoutSheetOrToast(string ev, string option)
    {
        var (e, s) = AtEvent(ev);
        s.Run.Active!.Awakening = 40; // Pray has something to calm
        var o = Choose(e, ref s, Opt(ev, option));
        Assert.Equal(OutcomeShow.Popups, o.Show);
        if (o.Lines.Exists(l => l.Kind != OutcomeKind.Nothing)) Assert.Null(o.Toast);
    }

    [Fact]
    public void EveryDeclineOption_IsAToast_YouMoveOn()
    {
        int n = 0;
        foreach (var ev in ContentDb.Default.RunEvents.Values)
            for (int i = 0; i < ev.Options.Count; i++)
            {
                var opt = ev.Options[i];
                if (opt.CoinCost > 0 || opt.ShardCost > 0 || !opt.Outcomes.TrueForAll(oc => oc.Effects.Count == 0)) continue;
                var (e, s) = AtEvent(ev.Id);
                var o = Choose(e, ref s, i);
                Assert.Equal(OutcomeShow.Popups, o.Show);
                Assert.Equal("You move on.", o.Toast);
                n++;
            }
        Assert.True(n >= 14, "every Leave / Decline option (" + n + ")");
    }

    [Fact]
    public void ClampedEffect_IsAToast()
    {
        var (e, s) = AtEvent("shard_altar");
        s.Run.Active!.Awakening = 0;
        var o = Choose(e, ref s, Opt("shard_altar", "Pray"));
        Assert.Equal(OutcomeShow.Popups, o.Show);
        Assert.Equal("Nothing changed.", o.Toast);
    }

    [Fact]
    public void BoonAvoidedEvent_IsAToast()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 12).ToArray());
        run.SkipNegativeEvents = 1;
        var ctx = RunTestKit.Ctx(e, s);
        typeof(RunSystem).GetMethod("OpenEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { ctx, run, "fuel_leak", run.Tiles[1] });
        Assert.Equal(OutcomeShow.Popups, run.LastOutcome!.Show);
        Assert.Equal("Boon: Fuel Leak avoided", run.LastOutcome.Toast);
    }

    [Theory]
    [InlineData("training_dummy", "Spar with an elite")]
    [InlineData("boss_scout", "Chase it down")]
    [InlineData("rift_echo", "Fight the echo")]
    public void WonEventFight_FliesIn_WithTheRewards(string ev, string option)
    {
        var (e, s) = AtEvent(ev);
        s.Run.Active!.Awakening = 50;
        var o = Choose(e, ref s, Opt(ev, option));
        Assert.Equal(OutcomeShow.Popups, o.Show);
        Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Battle && l.Id == "won");
        Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Xp);
    }

    /// <summary>Bug: a lost Spar / Scout chase / Rift Echo showed the win text, and after a revive its follow-ups applied silently.</summary>
    [Theory]
    [InlineData("training_dummy", "Spar with an elite", "Elite beaten!", OutcomeKind.Stat)]
    [InlineData("boss_scout", "Chase it down", "The scout is silenced.", OutcomeKind.Awakening)]
    [InlineData("rift_echo", "Fight the echo", "The echo fades, leaving a crate behind.", OutcomeKind.AccountLoot)]
    public void LostEventFight_GoesToTheRevive_WithLossText_AndTheRetryWinPublishesTheFollowUps(string ev, string option, string winText, OutcomeKind followUp)
    {
        var (e, s) = AtEvent(ev, RunTestKit.Weak());
        s.Run.Active!.Awakening = 50;
        var lost = Choose(e, ref s, Opt(ev, option));
        Assert.Equal(RunPhase.Defeated, s.Run.Active!.Phase);
        Assert.Equal(OutcomeShow.Revive, lost.Show);
        Assert.NotEqual(winText, lost.Text);
        Assert.StartsWith("Beaten!", lost.Text);
        Assert.DoesNotContain(lost.Lines, l => l.Kind == followUp && l.Kind != OutcomeKind.Awakening);

        // the revived worm is much stronger (test only): the retry is won
        var strong = RunTestKit.Strong();
        var lo = s.Run.Active.Loadout;
        lo.WormStats = new StatBlock();
        lo.WormStats.Add(new StatMod(StatId.Hp, strong.Hp)); lo.WormStats.Add(new StatMod(StatId.Atk, strong.Atk));
        lo.WormStats.Add(new StatMod(StatId.Def, strong.Def)); lo.WormStats.Add(new StatMod(StatId.Speed, strong.Speed));
        s.Run.Active.DefeatEnemies.ForEach(x => x.StartHp = 1);
        s.Run.Active.WormMaxHp = strong.Hp;
        s.Wallet.Add(Currencies.Diamonds, 10_000);
        int seq = s.Run.Active.OutcomeSeq;
        var r = RunTestKit.Exec(e, ref s, new RunRevive { Method = ReviveMethod.Diamonds });
        Assert.True(r.Ok, r.Error);
        var run = s.Run.Active!;
        Assert.NotEqual(RunPhase.Defeated, run.Phase);
        Assert.True(run.OutcomeSeq > seq, "the retry's result is published");
        var won = run.LastOutcome!;
        Assert.Equal(winText, won.Text);
        Assert.Equal(OutcomeShow.Popups, won.Show);
        Assert.Contains(won.Lines, l => l.Kind == OutcomeKind.Battle && l.Id == "won");
        Assert.Contains(won.Lines, l => l.Kind == followUp);
        Assert.Null(run.DefeatOutcome);
    }

    /// <summary>Bug: a positive awakening is scaled; the button now shows the scaled number, so button and result agree.</summary>
    [Fact]
    public void BossWake_ButtonNumber_EqualsTheResult()
    {
        var (e, s) = AtEvent("boss_scout");
        var run = s.Run.Active!;
        run.Awakening = 20;
        run.AwakeningReductionPct = 30;
        double shown = RunSystem.EventAwakening(e.Content, run, 10);
        Assert.NotEqual(10, shown, 3);
        var o = Choose(e, ref s, Opt("boss_scout", "Let it go"));
        var line = o.Lines.Single(l => l.Kind == OutcomeKind.Awakening);
        Assert.Equal(Math.Round(shown), Math.Round(line.Amount));
        Assert.Equal(-15, RunSystem.EventAwakening(e.Content, run, -15)); // drains are not scaled
    }

    [Fact]
    public void FullHp_RestAndTonic_SayHpAlreadyFull()
    {
        var c = ContentDb.Default;
        var (_, s) = AtEvent("campfire");
        var run = s.Run.Active!;
        var rest = c.RunEvents["campfire"].Options[Opt("campfire", "Rest")];
        var tonic = c.RunEvents["alien_medic"].Options[Opt("alien_medic", "Buy a tonic")];
        var train = c.RunEvents["campfire"].Options[Opt("campfire", "Train")];
        Assert.Null(RunSystem.OptionUselessReason(run, rest));
        run.WormHp = run.WormMaxHp;
        Assert.Equal("HP already full", RunSystem.OptionUselessReason(run, rest));
        Assert.Equal("HP already full", RunSystem.OptionUselessReason(run, tonic));
        Assert.Null(RunSystem.OptionUselessReason(run, train));
    }
}
