using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Run mini-games (GDD §3.6): GAME tiles on the board, RunMinigameStart (rules pick game, seed, reward type) and
/// RunMinigameFinish {tier} (client-decided, paid once), rewards from ContentDb.MinigameTuning, auto / farming.
/// </summary>
public class MinigameTests
{
    /// <summary>Content with GAME tiles from level 1 on (the default keeps the first, tutorial level plain).</summary>
    private static ContentDb FromLevel1()
    {
        var c = new ContentDb();
        c.MinigameTuning.MinMainLevel = 1;
        return c;
    }

    /// <summary>A run whose first roll lands on a GAME tile (line board of GAME tiles).</summary>
    private static GameState OnGameTile(GameEngine e, long seed = 42)
    {
        var s = RunTestKit.StartRun(e, seed: seed);
        RunTestKit.LineBoard(s.Run.Active!, Enumerable.Repeat(ContentDb.MinigameTileId, 30).ToArray());
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.Equal(RunPhase.Minigame, s.Run.Active!.Phase);
        return s;
    }

    private static ContentDb ContentWithReward(MinigameReward forced)
    {
        var c = new ContentDb();
        c.MinigameTuning.SkillRollChancePct = forced == MinigameReward.SkillRoll ? 100 : 0;
        return c;
    }

    [Fact]
    public void Content_Has14Games_WithInstructions_AndAFullRewardTable()
    {
        var c = ContentDb.Default;
        Assert.Equal(14, c.Minigames.Count);
        Assert.Equal(14, c.Minigames.Select(m => m.Id).Distinct().Count());
        foreach (var m in c.Minigames)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Name));
            Assert.False(string.IsNullOrWhiteSpace(m.Instruction));
            Assert.True(m.Instruction.Length <= 70, m.Id + ": the instruction is one short line");
        }
        long prev = -1;
        foreach (MinigameTier tier in Enum.GetValues(typeof(MinigameTier)))
        {
            var r = c.MinigameTuning.For(tier);
            Assert.Equal(tier, r.Tier);
            Assert.True(r.Coins > prev, "coins grow with the tier");
            prev = r.Coins;
        }
        Assert.False(c.MinigameTuning.For(MinigameTier.Miss).Draft);
        // gold coins ≈ a good shop purchase or two
        var shop = c.RunShopTables["shop_standard"];
        Assert.InRange(c.MinigameTuning.For(MinigameTier.Gold).Coins, shop.SkillPrices[Rarity.Common], shop.SkillPrices[Rarity.Legendary] + shop.SkillPrices[Rarity.Mythic]);
        Assert.True(c.BoardTiles[ContentDb.MinigameTileId].Category == TileCategory.Minigame);
    }

    [Fact]
    public void GameTiles_ComeFromTheTilePools_AtMostMaxPerRun()
    {
        var e = RunTestKit.Engine(content: FromLevel1());
        var c = e.Content;
        Assert.All(c.TilePools.Values, pool => Assert.Contains(pool, w => w.Id == ContentDb.MinigameTileId));
        Assert.Equal(c.MinigameTuning.MaxPerRun, c.BoardTiles[ContentDb.MinigameTileId].MaxPerRun);
        int total = 0, none = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            var s = RunTestKit.StartRun(e, seed: seed);
            var run = s.Run.Active!;
            int n = run.Tiles.Count(t => t.TileId == ContentDb.MinigameTileId);
            Assert.InRange(n, 0, c.MinigameTuning.MaxPerRun);
            Assert.Equal(n, run.TilesPlaced.TryGetValue(ContentDb.MinigameTileId, out var placed) ? placed : 0);
            total += n;
            if (n == 0) none++;
        }
        Assert.True(total / 40.0 >= 1.0 && total / 40.0 <= 2.0, "GAME tiles per run: " + total / 40.0 + ", none: " + none); // ≈ 1–2 per run
        Assert.True(none <= 8, "boards without a GAME tile: " + none);
        // the first (tutorial) level stays plain
        for (long seed = 1; seed <= 10; seed++)
            Assert.DoesNotContain(RunTestKit.StartRun(RunTestKit.Engine(), seed: seed).Run.Active!.Tiles, t => t.TileId == ContentDb.MinigameTileId);
        // runless generation (previews) never places limited tiles
        var board = RunBoard.Generate(c, c.MainLevel(1), Rng.FromSeed(3));
        Assert.DoesNotContain(board.Tiles, t => t.TileId == ContentDb.MinigameTileId);
    }

    [Fact]
    public void WholeRuns_NeverExceedTheRunLimit()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong(), FromLevel1());
        for (long seed = 1; seed <= 10; seed++)
        {
            var s = RunTestKit.StartRun(e, seed: seed);
            RunTestKit.AutoToEnd(e, ref s);
            Assert.True(s.Counter("run_minigames_played") <= e.Content.MinigameTuning.MaxPerRun);
        }
    }

    [Fact]
    public void Landing_WaitsForThePlayer_AutoPauses_FarmingResolvesAsMiss()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = OnGameTile(e);
        var run = s.Run.Active!;
        Assert.Equal(RunDecision.Minigame, RunSystem.Decision(e.Content, run));
        Assert.True(RunSystem.RequiresPlayer(e.Content, run));
        Assert.False(RunSystem.RequiresPlayer(e.Content, run, farming: true));
        var auto = RunTestKit.Exec(e, ref s, new RunAutoStep());
        Assert.False(auto.Ok);
        Assert.Contains("needs a player decision", auto.Error);
        Assert.False(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);

        long coins = s.Run.Active!.Coins;
        var farm = RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true });
        Assert.True(farm.Ok, farm.Error);
        run = s.Run.Active!;
        Assert.Equal(RunPhase.AwaitRoll, run.Phase);
        Assert.Equal(MinigameTier.Miss, run.Minigame.LastTier);
        Assert.Equal(1, run.Minigame.Finished);
        Assert.Equal(coins + e.Content.MinigameTuning.For(MinigameTier.Miss).Coins, run.Coins);
        Assert.Equal("minigame", run.LastOutcome.Source);
        Assert.Single(run.Minigame.Played);
        Assert.Contains(farm.Events, ev => ev.Type == "run_minigame_finish" && ev.Args[1] == "Miss");
    }

    [Fact]
    public void Start_PicksGameSeedAndReward_FinishOncePerStart()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong(), ContentWithReward(MinigameReward.Coins));
        var s = OnGameTile(e);
        Assert.False(RunTestKit.Exec(e, ref s, new RunMinigameFinish { Tier = MinigameTier.Gold }).Ok); // finish before start
        var start = RunTestKit.Exec(e, ref s, new RunMinigameStart());
        Assert.True(start.Ok, start.Error);
        var m = s.Run.Active!.Minigame;
        Assert.NotNull(e.Content.Minigame(m.GameId));
        Assert.True(m.Seed > 0);
        Assert.Equal(MinigameReward.Coins, m.Reward);
        Assert.True(m.Started);
        Assert.False(RunTestKit.Exec(e, ref s, new RunMinigameStart()).Ok); // one start per tile

        long coins = s.Run.Active!.Coins;
        Assert.False(RunTestKit.Exec(e, ref s, new RunMinigameFinish { Tier = (MinigameTier)7 }).Ok); // not a tier
        Assert.True(RunTestKit.Exec(e, ref s, new RunMinigameFinish { Tier = MinigameTier.Gold }).Ok);
        var run = s.Run.Active!;
        Assert.Equal(coins + e.Content.MinigameTuning.For(MinigameTier.Gold).Coins, run.Coins);
        Assert.False(RunTestKit.Exec(e, ref s, new RunMinigameFinish { Tier = MinigameTier.Gold }).Ok); // pays once
        Assert.Equal(coins + e.Content.MinigameTuning.For(MinigameTier.Gold).Coins, s.Run.Active!.Coins);
        Assert.Equal(1, s.Counter("run_minigames_played"));
        Assert.Equal(1, s.Counter("run_minigames_gold"));
        // the result screen: a coins line, the game's name and the medal
        var o = run.LastOutcome;
        Assert.Equal("minigame", o.Source);
        Assert.Equal(e.Content.Minigame(m.GameId)!.Name, o.Title);
        Assert.Equal("Gold", o.Choice);
        Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Coins && l.Amount == e.Content.MinigameTuning.For(MinigameTier.Gold).Coins);
        // the tile is used up
        Assert.True(run.Tiles[run.Position].Cleared); // used up: cleared like every resolved tile
    }

    [Theory]
    [InlineData(MinigameTier.Miss)]
    [InlineData(MinigameTier.Bronze)]
    [InlineData(MinigameTier.Silver)]
    [InlineData(MinigameTier.Gold)]
    public void CoinRewards_FollowTheTable(MinigameTier tier)
    {
        var e = RunTestKit.Engine(RunTestKit.Strong(), ContentWithReward(MinigameReward.Coins));
        var s = OnGameTile(e);
        Assert.True(RunTestKit.Exec(e, ref s, new RunMinigameStart()).Ok);
        long coins = s.Run.Active!.Coins;
        Assert.True(RunTestKit.Exec(e, ref s, new RunMinigameFinish { Tier = tier }).Ok);
        Assert.Equal(coins + e.Content.MinigameTuning.For(tier).Coins, s.Run.Active!.Coins);
        Assert.Equal(RunPhase.AwaitRoll, s.Run.Active!.Phase);
    }

    [Fact]
    public void SkillRoll_OpensADraft_WithTierOdds_ThenTheRunGoesOn()
    {
        var c = ContentWithReward(MinigameReward.SkillRoll);
        var e = RunTestKit.Engine(RunTestKit.Strong(), c);
        var s = OnGameTile(e);
        Assert.True(RunTestKit.Exec(e, ref s, new RunMinigameStart()).Ok);
        Assert.Equal(MinigameReward.SkillRoll, s.Run.Active!.Minigame.Reward);
        int pending = s.Run.Active!.PendingDrafts;
        var fin = RunTestKit.Exec(e, ref s, new RunMinigameFinish { Tier = MinigameTier.Gold });
        Assert.True(fin.Ok, fin.Error);
        var run = s.Run.Active!;
        Assert.Equal(RunPhase.Draft, run.Phase);
        Assert.Equal(c.RunTuning.DraftChoices, run.DraftOffer.Count);
        Assert.Equal(run.DraftOffer.Count, run.DraftOffer.Distinct().Count());
        Assert.Contains(run.LastOutcome.Lines, l => l.Kind == OutcomeKind.SkillRoll);
        string pick = run.DraftOffer[0];
        Assert.True(RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = pick }).Ok);
        Assert.Contains(pick, s.Run.Active!.Skills);
        Assert.Equal("minigame", s.Run.Active!.Gains[^1].Source); // the build counts mini-game skill rolls
        Assert.False(s.Run.Active!.Minigame.DraftOpen);
        Assert.Equal(pending, s.Run.Active!.PendingDrafts); // the level-up drafts are untouched
        Assert.NotEqual(RunPhase.Minigame, s.Run.Active!.Phase);
    }

    [Fact]
    public void SkillRoll_Miss_PaysConsolationCoins_NoDraft()
    {
        var c = ContentWithReward(MinigameReward.SkillRoll);
        var e = RunTestKit.Engine(RunTestKit.Strong(), c);
        var s = OnGameTile(e);
        Assert.True(RunTestKit.Exec(e, ref s, new RunMinigameStart()).Ok);
        long coins = s.Run.Active!.Coins;
        Assert.True(RunTestKit.Exec(e, ref s, new RunMinigameFinish { Tier = MinigameTier.Miss }).Ok);
        Assert.Equal(RunPhase.AwaitRoll, s.Run.Active!.Phase);
        Assert.Equal(coins + c.MinigameTuning.For(MinigameTier.Miss).Coins, s.Run.Active!.Coins);
    }

    [Fact]
    public void GoldSkillRolls_OfferHigherRarities_ThanBronze()
    {
        double Avg(MinigameTier tier)
        {
            var c = ContentWithReward(MinigameReward.SkillRoll);
            var e = RunTestKit.Engine(RunTestKit.Strong(), c);
            double sum = 0; int n = 0;
            for (long seed = 1; seed <= 40; seed++)
            {
                var s = OnGameTile(e, seed);
                Assert.True(RunTestKit.Exec(e, ref s, new RunMinigameStart()).Ok);
                Assert.True(RunTestKit.Exec(e, ref s, new RunMinigameFinish { Tier = tier }).Ok);
                foreach (var id in s.Run.Active!.DraftOffer) { sum += (int)c.Skill(id)!.Rarity; n++; }
            }
            return sum / n;
        }
        double bronze = Avg(MinigameTier.Bronze), silver = Avg(MinigameTier.Silver), gold = Avg(MinigameTier.Gold);
        Assert.True(silver > bronze, $"silver {silver} > bronze {bronze}");
        Assert.True(gold > silver, $"gold {gold} > silver {silver}");
    }

    [Fact]
    public void NeverTheSameGameTwiceInARun()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = OnGameTile(e);
        var seen = new List<string>();
        string last = "";
        for (int i = 0; i < e.Content.Minigames.Count; i++)
        {
            if (s.Run.Active!.Phase != RunPhase.Minigame)
            {
                // next GAME tile: put the worm back in front of one
                while (s.Run.Active!.Phase != RunPhase.AwaitRoll) Assert.True(RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true }).Ok);
                RunTestKit.LineBoard(s.Run.Active!, Enumerable.Repeat(ContentDb.MinigameTileId, 30).ToArray());
                foreach (var t in s.Run.Active!.Tiles) t.Fixed = true; // boss actions leave them alone
                var rr = RunTestKit.Exec(e, ref s, new RunRoll());
                Assert.True(rr.Ok, rr.Error);
                last = string.Join(" ", rr.Events);
            }
            var st = RunTestKit.Exec(e, ref s, new RunMinigameStart());
            Assert.True(st.Ok, "game " + i + ": " + st.Error + " phase " + s.Run.Active?.Phase + " " + last);
            seen.Add(s.Run.Active!.Minigame.GameId);
            Assert.True(RunTestKit.Exec(e, ref s, new RunMinigameFinish { Tier = MinigameTier.Bronze }).Ok);
        }
        Assert.Equal(e.Content.Minigames.Count, seen.Distinct().Count());
    }

    [Fact]
    public void SameSeed_SameGame_SameSeed_SameReward()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var a = OnGameTile(e, 99);
        var b = OnGameTile(e, 99);
        Assert.True(RunTestKit.Exec(e, ref a, new RunMinigameStart()).Ok);
        Assert.True(RunTestKit.Exec(e, ref b, new RunMinigameStart()).Ok);
        Assert.Equal(a.Run.Active!.Minigame.GameId, b.Run.Active!.Minigame.GameId);
        Assert.Equal(a.Run.Active!.Minigame.Seed, b.Run.Active!.Minigame.Seed);
        Assert.Equal(a.Run.Active!.Minigame.Reward, b.Run.Active!.Minigame.Reward);
    }

    [Fact]
    public void DarkShops_PayMoreCoins()
    {
        var c = ContentDb.Default;
        var run = new ActiveRun { LevelId = c.DarkSideLevel(1).Id };
        var gold = c.MinigameTuning.For(MinigameTier.Gold);
        long dark = RunSystem.MinigameCoins(c, run, gold);
        double ratio = (double)c.RunShopTables["shop_dark"].SkillPrices[Rarity.Common] / c.RunShopTables["shop_standard"].SkillPrices[Rarity.Common];
        Assert.True(ratio > 1);
        Assert.Equal((long)Math.Round(gold.Coins * ratio), dark);
    }

    [Fact]
    public void FarmedRuns_PlayEveryGameTileTheyLandOn_AndFinish()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong(), FromLevel1());
        int played = 0;
        for (long seed = 1; seed <= 10; seed++)
        {
            var s = RunTestKit.StartRun(e, seed: seed);
            RunTestKit.AutoToEnd(e, ref s);
            played += (int)s.Counter("run_minigames_played");
            Assert.True(s.Counter("run_minigames_played") <= e.Content.MinigameTuning.MaxPerRun);
        }
        Assert.True(played > 0, "farmed runs land on GAME tiles");
    }
}
