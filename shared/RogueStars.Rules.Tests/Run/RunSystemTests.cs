using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

public class RunSystemTests
{
    [Fact]
    public void Start_CostsStamina_AndChecksUnlocks()
    {
        var e = RunTestKit.Engine();
        var s = TestHelpers.NewPlayer(e);
        long stamina = s.Wallet.Get(Currencies.Stamina);
        Assert.Equal(e.Content.EconomyConfig.StartingWallet[Currencies.Stamina], stamina);
        Assert.False(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "main_2" }).Ok);                       // locked
        Assert.False(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "main_1", Mode = RunMode.Overcharged }).Ok); // not cleared yet
        Assert.False(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "main_11" }).Ok);                       // portal locked
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "main_1", PortalId = "main_1" }).Ok);
        Assert.Equal(stamina - 5, s.Wallet.Get(Currencies.Stamina));
        Assert.False(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "main_1" }).Ok);                       // already running
        Assert.Equal(1, s.Counter("runs_started"));

        Assert.Equal(new Cost(Currencies.Stamina, 10).Amount, RunSystem.EntryCost(e.Content, "main_1", RunMode.Overcharged).Amount);
        Assert.Equal(Currencies.BurrowKeys, RunSystem.EntryCost(e.Content, "the_burrow_1", RunMode.Burrow).Currency);

        var poor = TestHelpers.NewPlayer(e, 3);
        poor.Wallet.TrySpend(Currencies.Stamina, poor.Wallet.Get(Currencies.Stamina));
        var r = RunTestKit.Exec(e, ref poor, new RunStart { LevelId = "main_1" });
        Assert.False(r.Ok);
        Assert.Contains("stamina", r.Error);
        // regen refills over time
        Assert.True(RunTestKit.Exec(e, ref poor, new RunStart { LevelId = "main_1" }, TestHelpers.T0 + 5 * 6 * 60 * 1000).Ok);
    }

    [Fact]
    public void Overcharged_AfterClear_CostsDouble()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        RunTestKit.AutoToEnd(e, ref s);
        Assert.True(s.Run.LastSummary.Won);
        Assert.True(RunSystem.IsLevelUnlocked(s, e.Content, "main_2", RunMode.Normal));
        long before = s.Wallet.Get(Currencies.Stamina);
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "main_1", Mode = RunMode.Overcharged }).Ok);
        Assert.Equal(before - 10, s.Wallet.Get(Currencies.Stamina));
        Assert.Equal(e.Content.RunLevel("main_1")!.Threat + 8, s.Run.Active!.Threat);
    }

    [Fact]
    public void Roll_MovesByDiceSum_OnPlainBoard()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        RunTestKit.LineBoard(s.Run.Active!, Enumerable.Repeat("empty", 30).ToArray());
        s.Run.Active!.ExtraDicePermanent = 1;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        var run = s.Run.Active!;
        Assert.Equal(2, run.LastDice.Count);
        Assert.All(run.LastDice, d => Assert.InRange(d, 1, 6));
        Assert.Equal(run.LastDice.Sum(), run.Position);
        Assert.Equal(1, run.Rolls);
        Assert.Equal(RunPhase.AwaitRoll, run.Phase);
    }

    [Fact]
    public void Branch_RequiresChoice_AndFollowsIt()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 20).ToArray());
        // tile 1 branches to 2 (main) or a longer side leg s → s2 → 3 (different landings: a real choice)
        int side = RunTestKit.SideArm(run, 1, 3, 2);
        run.ExtraDicePermanent = 2; // ≥3 steps: always passes the branch
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.Equal(RunPhase.ChooseBranch, s.Run.Active!.Phase);
        Assert.Equal(new List<int> { 2, side }, s.Run.Active.BranchOptions);
        Assert.False(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.True(RunTestKit.Exec(e, ref s, new RunChooseBranch { Index = 1 }).Ok);
        Assert.Contains(side, s.Run.Active!.Trail);
        Assert.DoesNotContain(2, s.Run.Active.Trail);
    }

    [Fact]
    public void Branch_BothWaysLandOnTheSameTile_TheWormJustMoves()
    {
        // playtest 0.1.325: equal arms 1 → 2 → 3 and 1 → s → 3; a roll of ≥ 3 steps walks past the join on either way
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 30).ToArray());
        int side = RunTestKit.SideArm(run, 1, 3, 1, "loot"); // a pickup on the side arm does not make it a choice
        run.ExtraDicePermanent = 2;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        run = s.Run.Active!;
        Assert.NotEqual(RunPhase.ChooseBranch, run.Phase);
        Assert.Empty(run.BranchOptions);
        Assert.Contains(2, run.Trail);                   // the default (main) arm
        Assert.DoesNotContain(side, run.Trail);
        Assert.Equal(run.LastDice.Sum(), run.Position);  // landed where either way would have
    }

    [Fact]
    public void Branch_SameLanding_OnlyWhenEveryWayEndsTheMoveOnOneTile()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        var c = e.Content;
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 30).ToArray());
        int side = RunTestKit.SideArm(run, 1, 3, 1);
        var fork = run.Tiles[1];
        run.StepsLeft = 1; // lands on the arm itself: 2 or s
        Assert.False(RunSystem.SameLanding(c, run, fork));
        run.StepsLeft = 2; // both reach the join
        Assert.True(RunSystem.SameLanding(c, run, fork));
        Assert.Equal(3, RunSystem.LandingVia(c, run, side, 2));
        run.StepsLeft = 6;
        Assert.True(RunSystem.SameLanding(c, run, fork));

        // a forced stop on one arm ends that way early: different landings, a real choice
        run.Tiles[side].TileId = "shop";
        Assert.Equal(side, RunSystem.LandingVia(c, run, side, 6));
        Assert.False(RunSystem.SameLanding(c, run, fork));
        run.Tiles[side].TileId = "empty";

        // arms of different length never land together
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 30).ToArray());
        RunTestKit.SideArm(run, 1, 3, 3);
        run.StepsLeft = 8;
        Assert.False(RunSystem.SameLanding(c, run, run.Tiles[1]));

        // another fork before the landing: not known yet, the player chooses
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 30).ToArray());
        RunTestKit.SideArm(run, 1, 3, 1);
        RunTestKit.SideArm(run, 4, 6, 1);
        run.StepsLeft = 8;
        Assert.False(RunSystem.SameLanding(c, run, run.Tiles[1]));
        run.StepsLeft = 3; // the move ends on tile 4 (the next fork's tile) before choosing there
        Assert.True(RunSystem.SameLanding(c, run, run.Tiles[1]));
    }

    [Fact]
    public void Shop_ForcesStop_WhenPassed()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, new[] { "shop" }.Concat(Enumerable.Repeat("empty", 20)).ToArray());
        run.ExtraDicePermanent = 5;
        run.Coins = 1000;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.Equal(1, s.Run.Active!.Position);
        Assert.Equal(RunPhase.Shop, s.Run.Active.Phase);
        Assert.NotEmpty(s.Run.Active.Shop);
        int skillOffer = s.Run.Active.Shop.FindIndex(o => o.Kind == ShopOfferKind.Skill);
        string skill = s.Run.Active.Shop[skillOffer].SkillId;
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopBuy { Index = skillOffer }).Ok);
        Assert.Contains(skill, s.Run.Active!.Skills);
        Assert.False(RunTestKit.Exec(e, ref s, new RunShopBuy { Index = skillOffer }).Ok); // sold
        long coins = s.Run.Active!.Coins;
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopReroll()).Ok);
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopReroll()).Ok);
        Assert.Equal(coins - 10 - 20, s.Run.Active!.Coins);                       // rising reroll price
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopLeave()).Ok);
        Assert.Equal(RunPhase.AwaitRoll, s.Run.Active!.Phase);
    }

    [Fact]
    public void SupplyCaches_PayRunResources_NotAccountLoot()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("loot", 30).ToArray());
        long coins = run.Coins;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        run = s.Run.Active!;
        Assert.Empty(run.PendingLoot);                                             // ordinary tiles never pay account loot
        Assert.Equal(0, run.Danger);
        Assert.Equal(coins + e.Content.BoardTiles["loot"].Coins, run.Coins);
        Assert.True(run.XpTotal >= (int)e.Content.BoardTiles["loot"].Value);
        Assert.True(run.Tiles[run.Position].TileId != null);                       // consumed tile replaced from the pool
    }

    [Fact]
    public void AccountLoot_FromRiskyEvents_IsPending_AndRaisesDanger()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 30).ToArray());
        run.Phase = RunPhase.Event; run.EventId = "gravity_well"; run.EventTile = 1;
        long biomass = s.Wallet.Get(Currencies.Biomass);
        Assert.True(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = 0 }).Ok);
        run = s.Run.Active!;
        foreach (var b in RunSystem.RewardChestBundle(e.Content, e.Content.RunLevel(run.LevelId)!)) // 2 chests: twice the level's bundle + 2 rolls
            Assert.True(run.PendingLoot.Where(r => r.Id == b.Id).Sum(r => r.Amount) >= 2 * b.Amount, b.Id);
        Assert.Equal(2, run.Danger);
        Assert.Equal(-15, run.AwakeningReductionPct, 6);                           // lasting cost: the boss awakens 15% faster
        Assert.Equal(biomass, s.Wallet.Get(Currencies.Biomass));                    // nothing granted yet
        run.Danger = 4;
        Assert.Equal(4 * e.Content.RunTuning.DangerPctPerLoot, RunSystem.DangerPct(e.Content, run), 6);
    }

    [Fact]
    public void AwakeningThresholds_TriggerBossActions()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        var ctx = RunTestKit.Ctx(e, s);
        var boss = e.Content.RunBosses[run.BossId];
        double enemyBonusBefore = run.Tiles.Where(t => t.TileId.StartsWith("enemy_")).Sum(t => t.BonusPct);
        int enemyTiles = run.Tiles.Count(t => t.TileId.StartsWith("enemy_"));
        RunSystem.AddAwakening(ctx, run, 24);
        Assert.Equal(0, run.ActionsTriggered);
        RunSystem.AddAwakening(ctx, run, 1);
        Assert.Equal(1, run.ActionsTriggered);
        Assert.Equal(enemyBonusBefore + enemyTiles * boss.Actions[0].Value, run.Tiles.Where(t => t.TileId.StartsWith("enemy_")).Sum(t => t.BonusPct), 6);
        RunSystem.AddAwakening(ctx, run, 500);
        Assert.Equal(100, run.Awakening);
        Assert.Equal(boss.Actions.Count, run.ActionsTriggered);
        Assert.Equal(boss.FullyAwakeStatPct, run.BossStatPct, 6);
        Assert.Contains(ctx.Events, ev => ev.Type == "boss_awaken");
        RunSystem.AddAwakening(ctx, run, -30);
        Assert.Equal(70, run.Awakening);

        // rolling fills the meter
        var s2 = RunTestKit.StartRun(e, seed: 5);
        RunTestKit.LineBoard(s2.Run.Active!, Enumerable.Repeat("empty", 30).ToArray());
        Assert.True(RunTestKit.Exec(e, ref s2, new RunRoll()).Ok);
        Assert.Equal(boss.AwakenPerRoll, s2.Run.Active!.Awakening, 6);
    }

    [Fact]
    public void RunLevelBoons_FromMetaSkills()
    {
        var c = new ContentDb();
        c.AddSkill(RunTestKit.Skill("b_dice", new SkillTrigger { When = TriggerKind.OnRunStart, Effect = EffectKind.ExtraDice, Value = 1 }));
        c.AddSkill(RunTestKit.Skill("b_roll_dice", new SkillTrigger { When = TriggerKind.OnRoll, Effect = EffectKind.ExtraDice, Value = 1 }));
        c.AddSkill(RunTestKit.Skill("b_slow", new SkillTrigger { When = TriggerKind.OnRunStart, Effect = EffectKind.SlowAwakening, Value = 50 }));
        c.AddSkill(RunTestKit.Skill("b_coins", new SkillTrigger { When = TriggerKind.OnRunStart, Effect = EffectKind.GainRunCoins, Value = 40 }));
        c.AddSkill(RunTestKit.Skill("b_start_skill", new SkillTrigger { When = TriggerKind.OnRunStart, Effect = EffectKind.GainRunCoins, Param = "fire_kindle" }));
        c.AddSkill(RunTestKit.Skill("b_start_skill2", new SkillTrigger { When = TriggerKind.OnRunStart, Param = "skill:crit_keen" }));
        c.AddSkill(RunTestKit.Skill("b_random_skill", new SkillTrigger { When = TriggerKind.OnRunStart, Param = "random_skill:Epic" }));
        c.AddSkill(RunTestKit.Skill("b_skip", new SkillTrigger { When = TriggerKind.OnRunStart, Effect = EffectKind.SkipNegativeEvent, Value = 2 }));
        c.AddSkill(RunTestKit.Skill("b_skip_param", new SkillTrigger { When = TriggerKind.OnRunStart, Param = "skip_negative_events", Value = 1 }));
        c.AddSkill(RunTestKit.Skill("b_growth", new SkillTrigger { When = TriggerKind.OnRunStart, Effect = EffectKind.RunStatGrowth, Stat = new StatMod(StatId.Hp, 0, 10) }));
        var worm = new RunTestKit.WormSetup();
        worm.Meta.AddRange(new[] { "b_dice", "b_roll_dice", "b_slow", "b_coins", "b_start_skill", "b_start_skill2", "b_random_skill", "b_skip", "b_skip_param", "b_growth" });
        var e = RunTestKit.Engine(worm, c);
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        Assert.Equal(1, run.ExtraDicePermanent);
        Assert.Equal(50 + (run.Skills.Contains("gen_calm_mind") ? 9 : 0), run.AwakeningReductionPct); // the random start skill may be Calm Mind
        Assert.Equal(40 + RunSystem.StartCoins(c, c.RunLevel(run.LevelId)), run.Coins); // the boon's 40 on top of the start coins
        Assert.Contains("fire_kindle", run.Skills);
        Assert.Contains("crit_keen", run.Skills);
        Assert.Contains(run.Skills, id => c.Skill(id)!.Rarity >= Rarity.Epic && id != "fire_kindle" && id != "crit_keen");
        Assert.Equal(3, run.SkipNegativeEvents);
        Assert.Equal(1100, run.WormMaxHp, 6);
        Assert.Equal(run.WormMaxHp, run.WormHp, 6);

        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 40).ToArray());
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        run = s.Run.Active!;
        Assert.Equal(2, run.LastDice.Count);                         // base 1 + permanent 1
        Assert.Equal(1, run.ExtraDiceNext);                          // OnRoll extra die for the next roll
        Assert.Equal(c.RunBosses[run.BossId].AwakenPerRoll * 0.5, run.Awakening, 6);
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.Equal(3, s.Run.Active!.LastDice.Count);

        // skip negative tiles: a hazard is skipped while the boon has charges
        run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("hazard_spikes", 40).ToArray());
        run.ExtraDicePermanent = 0; run.ExtraDiceNext = 0;
        double hp = run.WormHp;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.Equal(hp, s.Run.Active!.WormHp, 6);
        Assert.Equal(2, s.Run.Active.SkipNegativeEvents);
    }

    [Fact]
    public void SkipNegative_SkipsHarmfulEventOutcomes()
    {
        var o = new EventOutcome();
        o.Effects.Add(new RunEffect(RunEffectKind.DamagePct, 10));
        Assert.True(RunSystem.IsNegativeOutcome(o));
        o.Effects.Add(new RunEffect(RunEffectKind.Loot, 1));
        Assert.False(RunSystem.IsNegativeOutcome(o));               // trades are not skipped

        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.SkipNegativeEvents = 1;
        run.Phase = RunPhase.Event;
        run.EventId = "boss_scout";
        double meter = run.Awakening;
        Assert.True(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = 1 }).Ok); // "let it go": meter +10 (negative)
        Assert.Equal(meter, s.Run.Active!.Awakening, 6);
        Assert.Equal(0, s.Run.Active.SkipNegativeEvents);
    }

    [Fact]
    public void Events_ApplyOutcomes_AndRespectCosts()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.Phase = RunPhase.Event;
        run.EventId = "alien_medic";
        run.Coins = 0;
        Assert.False(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = 0 }).Ok); // 40 coins needed
        s.Run.Active!.Coins = 50;
        s.Run.Active.WormHp = 10;
        Assert.True(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = 0 }).Ok);
        Assert.Equal(10, s.Run.Active!.Coins);
        Assert.True(s.Run.Active.WormHp > 10);

        // Rift echo: battle, then a Parts Crate added to pending loot (parts v2: runs drop no parts)
        s.Run.Active.Phase = RunPhase.Event;
        s.Run.Active.EventId = "rift_echo";
        Assert.True(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = 0 }).Ok);
        Assert.Contains(s.Run.Active!.PendingLoot, r => r.Kind == RewardKinds.Currency && r.Id == Currencies.PartTokens);
        Assert.NotNull(s.Run.Active.LastBattle);
    }

    [Fact]
    public void Rewards_OnlyOnCompletion_WithModifiers()
    {
        long Gained(bool withModifier)
        {
            var e = RunTestKit.Engine(RunTestKit.Strong());
            var s = TestHelpers.NewPlayer(e, 11);
            if (withModifier) s.Modifiers.Add(new Modifier { Id = "m", Target = "run_reward:biomass", Multiplier = 2 });
            Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "main_1" }).Ok);
            long before = s.Wallet.Get(Currencies.Biomass);
            for (int i = 0; i < 1000 && s.Run.Active != null; i++)
            {
                Assert.Equal(before, s.Wallet.Get(Currencies.Biomass)); // nothing paid out while running
                Assert.True(RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true, FarmShops = true }).Ok);
            }
            Assert.Null(s.Run.Active);
            Assert.True(s.Run.LastSummary.Won);
            Assert.Equal(1, s.Counter("runs_completed"));
            Assert.True(RunSystem.Progress(s, "main_1").Feats[0]);
            return s.Wallet.Get(Currencies.Biomass) - before;
        }
        long normal = Gained(false), doubled = Gained(true);
        var level = ContentDb.Default.RunLevel("main_1")!;
        Assert.True(normal >= level.CompletionRewards.First(r => r.Id == Currencies.Biomass).Amount);
        Assert.InRange(doubled, normal * 2 - 5, normal * 2 + 5);
    }

    [Fact]
    public void Failure_LosesPendingLoot_AndReviveOnlyOnce()
    {
        var e = RunTestKit.Engine(RunTestKit.Weak());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("enemy_1", 40).ToArray());
        run.PendingLoot.Add(Reward.Cur(Currencies.Biomass, 500));
        s.Wallet.Balances[Currencies.Diamonds] = 100;
        long biomass = s.Wallet.Get(Currencies.Biomass);

        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.Equal(RunPhase.Defeated, s.Run.Active!.Phase);
        Assert.NotNull(s.Run.Active.LastBattle);
        Assert.False(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.True(RunTestKit.Exec(e, ref s, new RunRevive { Method = ReviveMethod.Diamonds }).Ok);
        Assert.Equal(100 - e.Content.RunTuning.ReviveDiamondCost, s.Wallet.Get(Currencies.Diamonds));
        Assert.True(s.Run.Active!.ReviveUsed);
        Assert.Equal(RunPhase.Defeated, s.Run.Active.Phase);               // the weak worm loses the rematch
        Assert.False(RunTestKit.Exec(e, ref s, new RunRevive { Method = ReviveMethod.Ad }).Ok);
        Assert.True(RunTestKit.Exec(e, ref s, new RunAbandon()).Ok);
        Assert.Null(s.Run.Active);
        Assert.False(s.Run.LastSummary.Won);
        Assert.Equal(biomass, s.Wallet.Get(Currencies.Biomass));
        Assert.Equal(1, s.Counter("runs_failed"));
    }

    [Fact]
    public void Revive_ResumesFight_WithEnemiesKeepingDamage()
    {
        var worm = new RunTestKit.WormSetup { Hp = 120, Atk = 40, Def = 0 }; // loses to a Horde (the revive is what is tested)
        var e = RunTestKit.Engine(worm);
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("enemy_3", 40).ToArray());
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.Equal(RunPhase.Defeated, s.Run.Active!.Phase);
        var hurt = s.Run.Active.DefeatEnemies;
        Assert.NotEmpty(hurt);
        Assert.Contains(hurt, h => h.StartHp >= 0);
        Assert.True(RunTestKit.Exec(e, ref s, new RunRevive { Method = ReviveMethod.Ad, AdToken = "ad" }).Ok);
        Assert.Equal(1, s.Counter("run_revives"));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(3L)]
    [InlineData(4L)]
    [InlineData(5L)]
    public void AutoRun_FinishesWithinStepBudget(long seed)
    {
        foreach (var worm in new[] { new RunTestKit.WormSetup(), RunTestKit.Strong() })
        {
            var e = RunTestKit.Engine(worm);
            var s = RunTestKit.StartRun(e, seed: seed);
            int steps = RunTestKit.AutoToEnd(e, ref s, 400);
            Assert.True(steps > 0);
            Assert.NotNull(s.Run.LastSummary);
        }
    }

    [Fact]
    public void Run_IsReproducibleFromSeed()
    {
        string Play()
        {
            var e = RunTestKit.Engine();
            var s = RunTestKit.StartRun(e, seed: 77);
            RunTestKit.AutoToEnd(e, ref s);
            return System.Text.Json.JsonSerializer.Serialize(s, TestHelpers.Json);
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void OtherModes_Run()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = TestHelpers.NewPlayer(e);
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "the_burrow_1", Mode = RunMode.Burrow }).Ok);
        Assert.Equal(1, s.Wallet.Get(Currencies.BurrowKeys));
        // play floor 1, descend once, then cash out
        for (int i = 0; i < 400 && s.Run.Active != null && s.Run.Active.Phase != RunPhase.BurrowChoice; i++)
            Assert.True(RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true, FarmShops = true }).Ok);
        Assert.Equal(RunPhase.BurrowChoice, s.Run.Active!.Phase);
        Assert.NotEmpty(s.Run.Active.PendingLoot);
        Assert.True(RunTestKit.Exec(e, ref s, new RunBurrowDescend()).Ok);
        Assert.Equal(2, s.Run.Active!.Floor);
        Assert.Equal("the_burrow_2", s.Run.Active.LevelId);
        long gear = s.Wallet.Get(Currencies.GearCores);
        for (int i = 0; i < 400 && s.Run.Active != null && s.Run.Active.Phase != RunPhase.BurrowChoice; i++)
            Assert.True(RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true, FarmShops = true }).Ok);
        Assert.True(RunTestKit.Exec(e, ref s, new RunBurrowCashOut()).Ok);
        Assert.True(s.Run.LastSummary.Won);
        Assert.True(s.Wallet.Get(Currencies.GearCores) > gear);
        Assert.Equal(2, RunSystem.Progress(s, "the_burrow_1").BestFloor);

        var s2 = TestHelpers.NewPlayer(e, 9);
        s2.Wallet.Add(Currencies.Stamina, 100);
        Assert.True(RunTestKit.Exec(e, ref s2, new RunStart { LevelId = "dark_side_1", Mode = RunMode.DarkSide }).Ok);
        Assert.Equal(2, s2.Run.Active!.Dice);
        RunTestKit.AutoToEnd(e, ref s2);
        Assert.NotNull(s2.Run.LastSummary);
    }
}

public class AwakenCoreSourceTests
{
    [Fact]
    public void Content_CoresOnlyAsRareDropsOnDeepBurrowFloors_NeverRoutineRewards()
    {
        var c = ContentDb.Default;
        string core = ProgressionCurrencies.PartAwakenCores;
        foreach (var l in c.RunLevels.Values)
        {
            Assert.DoesNotContain(l.CompletionRewards, r => r.Id == core);
            // parts v2: Dark Side clears carry their 5% Legendary part, nothing else
            if (l.PortalId != "the_burrow") Assert.All(l.RareDrops, d => Assert.Equal(RewardKinds.Part, d.Reward.Kind));
        }
        foreach (var t in c.RunLootTables.Values) Assert.DoesNotContain(t.Entries, x => x.Id == core);
        var burrow = c.Portal("the_burrow")!;
        for (int i = 0; i < burrow.LevelIds.Count; i++)
        {
            var drops = c.RunLevel(burrow.LevelIds[i])!.RareDrops;
            var cores = drops.FindAll(d => d.Reward.Id == core);
            if (i < burrow.LevelIds.Count - 2) Assert.Empty(cores);
            else Assert.All(cores, d => Assert.InRange(d.ChancePct, 0.1, 5));
            // the other Burrow rare drop: a Crate on floors 5+ (was 1 Advancement Plate per floor; 75 plates = 1 Crate)
            var crates = drops.FindAll(d => d.Reward.Id == Currencies.PartTokens);
            if (i < 4) Assert.Empty(crates);
            else Assert.Equal(1.5, Assert.Single(crates).ChancePct);
            Assert.Equal(drops.Count, cores.Count + crates.Count);
        }
    }

    [Fact]
    public void RareDrop_PaidUnscaled_OnlyOnCompletion_AndDoubledByTheAd()
    {
        var content = new ContentDb();
        content.RunLevel("main_1")!.RareDrops.Add(new RareDrop(Reward.Cur(ProgressionCurrencies.PartAwakenCores, 1), 100));
        var e = RunTestKit.Engine(RunTestKit.Strong(), content);
        var s = TestHelpers.NewPlayer(e, 11);
        s.Modifiers.Add(new Modifier { Id = "m", Target = "run_reward:*", Multiplier = 2 });
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "main_1" }).Ok);
        RunTestKit.AutoToEnd(e, ref s);
        Assert.True(s.Run.LastSummary.Won);
        Assert.Equal(1, s.Wallet.Get(ProgressionCurrencies.PartAwakenCores));
        Assert.Contains(s.Run.LastSummary.Doublable, r => r.Id == ProgressionCurrencies.PartAwakenCores); // playtest 0.1.359
        Assert.True(RunTestKit.Exec(e, ref s, new RunDoubleRewards { AdToken = "ad" }).Ok);
        Assert.Equal(2, s.Wallet.Get(ProgressionCurrencies.PartAwakenCores));
        Assert.Contains(s.Run.LastSummary.Granted, r => r.Id == ProgressionCurrencies.PartAwakenCores && r.Amount == 2);
    }
}

public class BiomassSurgeTests
{
    [Fact]
    public void FirstWonPortalRunsOfTheDay_PayTripleBiomass()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = TestHelpers.NewPlayer(e, 11);
        s.Wallet.Add(Currencies.Stamina, 1000);
        var t = e.Content.RunTuning;
        long completion = e.Content.RunLevel("main_1")!.CompletionRewards.First(r => r.Id == Currencies.Biomass).Amount;
        var flags = new List<bool>();
        for (int i = 0; i < t.DailyBiomassBonusRuns + 1; i++)
        {
            Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "main_1" }).Ok);
            RunTestKit.AutoToEnd(e, ref s);
            Assert.True(s.Run.LastSummary.Won);
            flags.Add(s.Run.LastSummary.BiomassBonus);
            long paid = s.Run.LastSummary.Granted.Where(r => r.Id == Currencies.Biomass).Sum(r => r.Amount);
            Assert.True(paid >= completion * (s.Run.LastSummary.BiomassBonus ? t.DailyBiomassBonusMult : 1));
        }
        Assert.Equal(Enumerable.Repeat(true, t.DailyBiomassBonusRuns).Append(false), flags);
        Assert.Equal(0, RunSystem.BiomassBonusRunsLeft(s, e.Content, TestHelpers.T0));
        Assert.Equal(t.DailyBiomassBonusRuns, RunSystem.BiomassBonusRunsLeft(s, e.Content, TestHelpers.T0 + GameClock.Day));
    }
}

public class DarkSideTests
{
    [Fact]
    public void DarkSide_IsProceduralAlongTheCampaign_DataGrowsWithLevel()
    {
        var c = ContentDb.Default;
        var dark = c.Portal("dark_side")!;
        Assert.Equal(c.Campaign.DarkSideLevels, dark.LevelIds.Count);
        Assert.True(dark.LevelIds.Count >= 100);
        long prevData = 0; int prevThreat = int.MinValue;
        foreach (var id in dark.LevelIds)
        {
            var l = c.RunLevel(id)!;
            long data = l.CompletionRewards.Single(r => r.Id == Currencies.Data).Amount;
            Assert.True(data >= prevData);
            Assert.True(l.Threat >= prevThreat);
            prevData = data; prevThreat = l.Threat;
        }
        // The last Dark Side level sits near the end of the 500-level campaign and pays many times the first one's Data.
        var last = c.RunLevel(dark.LevelIds[^1])!;
        Assert.True(last.CompletionRewards.Single(r => r.Id == Currencies.Data).Amount >= 10 * c.RunLevel(dark.LevelIds[0])!.CompletionRewards.Single(r => r.Id == Currencies.Data).Amount);
        Assert.True(last.Threat > c.Campaign.ThreatAt(350));
        // Main portals pay no Data (the quest chests none either since 2026-11-26).
        Assert.All(c.RunLevels.Values.Where(l => l.PortalId != "dark_side"), l => Assert.DoesNotContain(l.CompletionRewards, r => r.Id == Currencies.Data));
    }
}

public class LaboratoryTests
{
    [Fact]
    public void NewPlayer_HasDefaultResearch_InDraftPool()
    {
        var e = RunTestKit.Engine();
        var s = TestHelpers.NewPlayer(e);
        Assert.Equal(1, LaboratorySystem.Entry(s, "res_second_wind").Level);
        var lo = e.BuildLoadout(s, TestHelpers.T0);
        Assert.Contains("lab_second_wind", lo.DraftPoolSkillIds);
        Assert.DoesNotContain("lab_burning_soul", lo.DraftPoolSkillIds);
    }

    [Fact]
    public void Research_CostsDataAndTime_Collect_Toggle_Upgrade()
    {
        var e = RunTestKit.Engine();
        var s = TestHelpers.NewPlayer(e);
        var tuning = e.Content.LabTuning;
        TestHelpers.WithLaboratory(e, s);
        s.Wallet.Balances.Remove(Currencies.Data);
        Assert.False(RunTestKit.Exec(e, ref s, new LabResearch { ResearchId = "res_burning_soul" }).Ok); // cannot afford
        s.Wallet.Add(Currencies.Data, 5000);
        Assert.Equal(0, LaboratorySystem.ResearchIndex(s, e.Content));
        Assert.True(RunTestKit.Exec(e, ref s, new LabResearch { ResearchId = "res_burning_soul" }).Ok);
        Assert.Equal(5000 - tuning.DataCost(0), s.Wallet.Get(Currencies.Data));
        Assert.Equal(1, LaboratorySystem.ResearchIndex(s, e.Content));
        Assert.Contains("busy", RunTestKit.Exec(e, ref s, new LabResearch { ResearchId = "lab_atk_1" }).Error); // one slot
        Assert.False(RunTestKit.Exec(e, ref s, new LabCollect { ResearchId = "res_burning_soul" }).Ok); // not ready
        long ready = LaboratorySystem.ReadyAtMs(s, "res_burning_soul");
        Assert.Equal(TestHelpers.T0 + LaboratorySystem.DurationMs(s, e.Content, (long)Math.Round(tuning.Hours(0) * 3_600_000)), ready);
        Assert.True(RunTestKit.Exec(e, ref s, new LabCollect { ResearchId = "res_burning_soul" }, ready).Ok);
        Assert.Contains("lab_burning_soul", e.BuildLoadout(s, ready).DraftPoolSkillIds);

        Assert.True(RunTestKit.Exec(e, ref s, new LabToggle { ResearchId = "res_burning_soul", Enabled = false }, ready).Ok);
        Assert.DoesNotContain("lab_burning_soul", e.BuildLoadout(s, ready).DraftPoolSkillIds);
        Assert.False(RunTestKit.Exec(e, ref s, new LabToggle { ResearchId = "res_overload", Enabled = true }, ready).Ok);
        Assert.True(RunTestKit.Exec(e, ref s, new LabToggle { ResearchId = "res_burning_soul", Enabled = true }, ready).Ok);

        // upgrade to level 2, finished with a City timer speed-up (Diamonds); the 2nd research is longer and pricier
        s.Wallet.Add(Currencies.Diamonds, 100_000);
        long data = s.Wallet.Get(Currencies.Data);
        Assert.True(RunTestKit.Exec(e, ref s, new LabResearch { ResearchId = "res_burning_soul" }, ready).Ok);
        Assert.Equal(data - tuning.DataCost(1), s.Wallet.Get(Currencies.Data));
        Assert.True(tuning.Hours(1) > tuning.Hours(0));
        var timer = TimerSystem.Find(s, LaboratorySystem.TimerKind, "res_burning_soul")!;
        Assert.True(RunTestKit.Exec(e, ref s, new TimerSpeedUp { TimerId = timer.Id, Method = SpeedUpMethod.Diamonds }, ready).Ok);
        Assert.True(RunTestKit.Exec(e, ref s, new LabCollect { ResearchId = "res_burning_soul" }, ready).Ok);
        Assert.Null(TimerSystem.Get(s, timer.Id));
        var pool = e.BuildLoadout(s, ready).DraftPoolSkillIds;
        Assert.Contains("lab_burning_soul_2", pool);
        Assert.DoesNotContain("lab_burning_soul", pool);
        Assert.Equal(2, s.Counter("lab_research_completed"));
    }

    [Fact]
    public void Tree_PrerequisitesAnyAndAll_StatNodesFeedTheLoadout()
    {
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        TestHelpers.WithLaboratory(e, s);
        s.Wallet.Add(Currencies.Data, 10_000_000);
        long t = TestHelpers.T0;
        void Research(string id, int levels)
        {
            for (int i = 0; i < levels; i++)
            {
                Assert.True(RunTestKit.Exec(e, ref s, new LabResearch { ResearchId = id }, t).Ok, id);
                t = LaboratorySystem.ReadyAtMs(s, id);
                Assert.True(RunTestKit.Exec(e, ref s, new LabCollect { ResearchId = id }, t).Ok);
            }
        }
        // Row 2 needs any row-1 node of the branch at level 2.
        Assert.Contains("prerequisites", RunTestKit.Exec(e, ref s, new LabResearch { ResearchId = "lab_def_1" }, t).Error);
        Research("lab_hp_1", 1);
        Assert.Contains("prerequisites", RunTestKit.Exec(e, ref s, new LabResearch { ResearchId = "lab_def_1" }, t).Error);
        double hpBefore = e.BuildLoadout(s, t).WormStats.Percent.TryGetValue(StatId.Hp, out var hb) ? hb : 0;
        Research("lab_hp_1", 1);
        Assert.Equal(hpBefore + 1, e.BuildLoadout(s, t).WormStats.Percent[StatId.Hp], 6); // +1% per level
        Research("lab_def_1", 1);
        // Stat nodes cannot be toggled.
        Assert.Contains("only skill", RunTestKit.Exec(e, ref s, new LabToggle { ResearchId = "lab_hp_1", Enabled = false }, t).Error);
        // Capstones need all prerequisites.
        var cap = e.Content.LabResearch["lab_damage_reduction"];
        Assert.True(cap.RequireAll);
        Assert.False(LaboratorySystem.PrerequisitesMet(s, e.Content, cap));
        Assert.Equal(e.Content.LabTuning.DataCost(LaboratorySystem.ResearchIndex(s, e.Content)), LaboratorySystem.NextDataCost(s, e.Content));
    }

    [Fact]
    public void Laboratory_LockedUntilPlaced_SlotsByStars_SpeedByLevels()
    {
        var e = RunTestKit.Engine();
        var s = TestHelpers.NewPlayer(e);
        s.Wallet.Add(Currencies.Data, 1_000_000);
        long t = TestHelpers.T0;
        Assert.Equal(0, LaboratorySystem.Slots(s, e.Content));
        Assert.Equal("build the Laboratory in the City", RunTestKit.Exec(e, ref s, new LabResearch { ResearchId = "lab_hp_1" }, t).Error);
        string lab = TestHelpers.WithLaboratory(e, s);
        Assert.Equal(1, LaboratorySystem.Slots(s, e.Content));
        Assert.Equal(10, CitySystem.ResearchSpeedPct(s, e.Content), 6);
        AlienSystem.Owned(s, "alien_zib").WorkBuildingId = null; // unstaffed: no slots
        Assert.Equal("the Laboratory needs an alien", RunTestKit.Exec(e, ref s, new LabResearch { ResearchId = "lab_hp_1" }, t).Error);
        AlienSystem.Owned(s, "alien_zib").WorkBuildingId = lab;
        CitySystem.Owned(s, ContentDb.LaboratoryId).Stars = 5;
        Assert.Equal(2, LaboratorySystem.Slots(s, e.Content));
        CitySystem.Owned(s, ContentDb.LaboratoryId).Stars = 10;
        Assert.Equal(3, LaboratorySystem.Slots(s, e.Content));
        Assert.Equal(10, CitySystem.ResearchSpeedPct(s, e.Content), 6); // stars give slots, not speed
        CitySystem.Building(s, lab).Level = 10;
        Assert.Equal(20, CitySystem.ResearchSpeedPct(s, e.Content), 6);
        for (int i = 0; i < 3; i++)
            Assert.True(RunTestKit.Exec(e, ref s, new LabResearch { ResearchId = new[] { "lab_hp_1", "lab_atk_1", "lab_alien_power_1" }[i] }, t).Ok);
        Assert.Contains("busy", RunTestKit.Exec(e, ref s, new LabResearch { ResearchId = "res_burning_soul" }, t).Error);
    }

    /// <summary>
    /// The laboratory's size (docs/design/GDD.md §5): even perfect play with one slot (all 3 ad speed-ups at the start of
    /// every timer = ×0.85³, the daily free speed-up every day, the strongest Laboratory building boon) cannot finish the
    /// tree in 365 days. (With the Research Annex from day 1 it now could: the skill tuning pass removed Treasure Sense and
    /// Drone Bay, 10 research levels; the owner adds lab skills later, which restores that guard.)
    /// </summary>
    [Fact]
    public void Tree_CannotBeFinishedInYearOne_EvenWithPerfectPlay()
    {
        var c = ContentDb.Default;
        int levels = c.LabResearch.Values.Sum(d => d.Levels.Count - (d.DefaultResearched ? 1 : 0));
        double total = 0;
        for (int k = 0; k < levels; k++) total += c.LabTuning.Hours(k);
        // ≈ 1.5–2 years of perfect play with one slot and no building boon (36 nodes after the skill tuning pass removed Treasure
        // Sense and Drone Bay; the owner adds lab skills later)
        Assert.InRange(total, 20_000, 32_000);

        var lab = c.Blueprints["bp_laboratory"];
        var boon = lab.Boons.Single(b => b.Kind == BoonKind.ResearchSpeedPct);
        double maxBoon = boon.Base * CitySystem.Growth(c, boon, c.CityTuning.MaxLevel, 10); // levels only: +20% at level 10
        double ads = Math.Pow(1 - c.TimerTuning.AdReductionPct / 100.0, c.TimerTuning.MaxAdsPerTimer);
        double freeHoursPerDay = c.TimerTuning.FreeSpeedUpsPerDay * c.TimerTuning.FreeSpeedUpMs / 3_600_000.0;
        // Nominal research hours one slot clears in a year at best.
        double oneSlot = 365 * (24 + freeHoursPerDay) * (1 + maxBoon / 100.0) / ads;
        Assert.True(total > oneSlot, $"tree {total:0} h vs one slot {oneSlot:0} h");
        // A second slot needs a 5★ Laboratory (a single Mythic: months of pulls, City design "Mythic pace").
        // Without the building boon one slot needs about two years.
        double plain = 365 * (24 + freeHoursPerDay) / ads;
        Assert.InRange(total / plain, 1.35, 2.3); // ≈ 1.4 years with 36 nodes (≈ 2 with 38)
    }

    [Fact]
    public void Tree_Has38NodesInFourBranches_MixingStatsAndSkills_NoPackages()
    {
        var c = ContentDb.Default;
        Assert.Equal(38, c.LabResearch.Count); // 36 + the Mythic nodes Plague and Shatter (owner, 2026-11-26); the six packages went (biome design)
        Assert.DoesNotContain(c.LabResearch.Values, d => d.Branch == "packages");
        // the synergy skill nodes hang off the stat nodes and wait for their signature's level unlock (Combo is a start synergy)
        Assert.Null(c.LabNode("res_chain_strikes")!.RequiresFeature);
        Assert.Equal(Features.SignatureIce, c.LabNode("res_frost_nova")!.RequiresFeature);
        Assert.Equal(Features.SignatureLightning, c.LabNode("res_overload")!.RequiresFeature);
        Assert.Equal(Features.SignatureCounter, c.LabNode("res_retaliation")!.RequiresFeature);
        Assert.Equal(Features.SignatureCounter, c.LabNode("res_bulwark")!.RequiresFeature);
        Assert.Equal(Features.SignatureAlien, c.LabNode("res_pack_tactics")!.RequiresFeature);
        Assert.All(c.LabResearch.Values, d => Assert.All(d.Requires, r => Assert.NotNull(c.LabNode(r.ResearchId))));
        foreach (var branch in new[] { "offense", "defense", "elements", "command" })
        {
            var nodes = c.LabResearch.Values.Where(d => d.Branch == branch).ToList();
            Assert.Contains(nodes, d => d.Kind == LabNodeKind.Stat);
            Assert.Contains(nodes, d => d.Kind == LabNodeKind.Skill);
        }
        foreach (var d in c.LabResearch.Values)
        {
            foreach (var r in d.Requires) Assert.NotNull(c.LabNode(r.ResearchId));
            if (d.Kind == LabNodeKind.Stat)
                foreach (var lv in d.Levels) Assert.All(lv.Stats, m => Assert.InRange(m.Flat + m.Percent, 1, 10));
            else foreach (var lv in d.Levels) Assert.NotNull(c.Skill(lv.SkillId));
            if (d.Tier > 1) Assert.NotEmpty(d.Requires);
        }
    }

    [Fact]
    public void DoubleRunRewards_PaysRunRewardsAgain_OncePerRun_WithDailyCap()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        s.Run.Active!.PendingLoot.Add(Reward.Cur(Currencies.Grain, 77));
        // No won run yet → rejected.
        Assert.False(RunTestKit.Exec(e, ref s, new RunDoubleRewards { AdToken = "ad" }).Ok);
        RunTestKit.AutoToEnd(e, ref s);
        var sum = s.Run.LastSummary;
        Assert.True(sum.Won);
        Assert.NotEmpty(sum.NewFeats); // first clear: feat Diamonds are granted, and doubled like every listed reward (playtest 0.1.359)
        Assert.All(sum.Rewards.Where(l => l.Source == RunRewardLine.Feat), l => Assert.True(l.Doublable));
        Assert.Equal(sum.NewFeats.Count, sum.Rewards.Count(l => l.Source == RunRewardLine.Feat));

        var before = new Dictionary<string, long>(s.Wallet.Balances);
        Assert.False(RunTestKit.Exec(e, ref s, new RunDoubleRewards()).Ok); // ad token required without No Ads
        var r = RunTestKit.Exec(e, ref s, new RunDoubleRewards { AdToken = "ad" });
        Assert.True(r.Ok, r.Error);
        foreach (var g in s.Run.LastSummary.Doublable.Where(x => x.Kind == RewardKinds.Currency).GroupBy(x => x.Id))
            Assert.Equal(before.GetValueOrDefault(g.Key) + g.Sum(x => x.Amount), s.Wallet.Get(g.Key));
        Assert.True(s.Wallet.Get(Currencies.Grain) >= before.GetValueOrDefault(Currencies.Grain) + 77);
        Assert.Equal(1, s.Counter("run_rewards_doubled"));
        Assert.Equal(1, s.Counter("ads_watched"));
        Assert.False(RunTestKit.Exec(e, ref s, new RunDoubleRewards { AdToken = "ad" }).Ok); // once per run

        // Daily cap: the placement allows DailyCap doubles per day (No Ads = instant, no token).
        s.Flags.Add(e.Content.Shop.NoAdsFlag);
        int cap = e.Content.AdPlacement(RunSystem.DoubleRewardsAdPlacement)!.DailyCap;
        for (int i = 1; i < cap; i++)
        {
            Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "main_1" }).Ok);
            RunTestKit.AutoToEnd(e, ref s);
            Assert.True(RunTestKit.Exec(e, ref s, new RunDoubleRewards()).Ok);
        }
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "main_1" }).Ok);
        RunTestKit.AutoToEnd(e, ref s);
        var capped = RunTestKit.Exec(e, ref s, new RunDoubleRewards());
        Assert.False(capped.Ok);
        Assert.Equal("ad daily cap reached", capped.Error);
        Assert.True(RunTestKit.Exec(e, ref s, new RunDoubleRewards(), TestHelpers.T0 + GameClock.Day).Ok); // next day
    }

    [Fact]
    public void DoubleRunRewards_RejectedAfterFailedRun()
    {
        var e = RunTestKit.Engine(RunTestKit.Weak());
        var s = RunTestKit.StartRun(e);
        RunTestKit.AutoToEnd(e, ref s);
        Assert.False(s.Run.LastSummary.Won);
        Assert.Equal("no won run to double", RunTestKit.Exec(e, ref s, new RunDoubleRewards { AdToken = "ad" }).Error);
    }
}
