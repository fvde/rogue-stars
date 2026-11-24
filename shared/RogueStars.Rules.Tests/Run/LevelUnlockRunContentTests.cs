using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// GDD §16 level unlocks, run side: level 1 is the tutorial run (roll, fight, level up and pick skills, beat the boss), and
/// shops, minibosses (Mythics), two dice and forks, campfires, the signature choice and the Alien Arcade join the board level
/// by level, each right after its feature unlocks (the level before its first board).
/// </summary>
public class LevelUnlockRunContentTests
{
    private static readonly ContentDb C = ContentDb.Default;

    private static int First(string feature) => C.RunContentLevel(feature);

    [Fact]
    public void FirstBoards_GetEachElement_TheLevelAfterItsFeatureUnlocks()
    {
        Assert.Equal(3, First(Features.RunShop));
        Assert.Equal(5, First(Features.RunMiniboss));
        Assert.Equal(6, First(Features.RunTwoDice));
        Assert.Equal(10, First(Features.RunCampfire));
        Assert.Equal(12, First(Features.RunSignatures));
        Assert.Equal(15, First(Features.RunMinigames));
        Assert.Equal(C.Campaign.SignatureChoiceFromLevel, First(Features.RunSignatures));
        Assert.Equal(C.MinigameTuning.MinMainLevel, First(Features.RunMinigames));
        Assert.Equal(C.Campaign.CampfiresFromLevel, C.BoardTiles["campfire"].MinMainLevel);
        Assert.All(C.FeatureOrder.Where(f => f.RunContent), f => Assert.Equal(2, f.Since));
        for (int n = 1; n <= 25; n++)
        {
            var l = C.MainLevel(n)!;
            bool Has(string tile) => l.FixedTiles.Any(f => f.TileId == tile);
            Assert.Equal(n >= First(Features.RunShop), Has("shop"));
            Assert.Equal(n >= First(Features.RunMiniboss), Has("miniboss"));
            Assert.Equal(n >= First(Features.RunMiniboss), l.ClockMinibosses);
            Assert.Equal(n >= First(Features.RunCampfire), Has("campfire"));
            Assert.Equal(n >= First(Features.RunTwoDice), l.BranchChance > 0);
            if (n <= C.Campaign.LevelsPerPortal) Assert.Equal(n >= First(Features.RunTwoDice) ? 2 : 1, l.Dice); // portal 1's two dice
            Assert.Equal(n == 1, l.TilePoolId == C.Campaign.TutorialPool);
        }
        // the Dark Side, the Burrow and later portals keep every element
        foreach (var l in new[] { C.DarkSideLevel(1), C.RunLevel("the_burrow_1")!, C.MainLevel(41)! })
        {
            Assert.True(l.ClockMinibosses);
            Assert.Contains(l.FixedTiles, f => f.TileId == "shop");
        }
    }

    [Fact]
    public void TutorialBoard_HasOnlyRollFightLevelUpAndTheBoss()
    {
        var pool = C.TilePools[C.Campaign.TutorialPool].Select(w => C.BoardTiles[w.Id].Category).ToHashSet();
        Assert.Subset(new HashSet<TileCategory> { TileCategory.Empty, TileCategory.Enemy, TileCategory.StatBuff, TileCategory.Pickup, TileCategory.Loot, TileCategory.Shard, TileCategory.Minigame }, pool); // GAME tiles: in every pool, gated by level
        var l1 = C.MainLevel(1)!;
        Assert.Empty(l1.FixedTiles);
        Assert.Equal(0, l1.BranchChance);
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var b = RunBoard.Generate(C, l1, new Rng(seed));
            foreach (var t in b.Tiles.Where(t => t.TileId != null))
            {
                var cat = C.BoardTiles[t.TileId].Category;
                Assert.DoesNotContain(cat, new[] { TileCategory.Event, TileCategory.Elite, TileCategory.Hazard, TileCategory.Shop, TileCategory.Campfire,
                    TileCategory.Miniboss, TileCategory.Treasure, TileCategory.StatDebuff, TileCategory.Minigame });
            }
            Assert.DoesNotContain(b.Tiles, t => t.Next.Count > 1); // no forks
        }
    }

    [Fact]
    public void TutorialRun_LevelsUpAndPicksSkills_NoMinibosses_AndIsWonByAWormWithoutParts()
    {
        var l1 = C.MainLevel(1)!;
        int wins = 0, drafts = 0;
        double rolls = 0;
        for (int seed = 1; seed <= 60; seed++)
        {
            var e = new GameEngine(C);
            var s = e.NewPlayer("p", TestHelpers.T0, seed);
            Assert.Empty(s.Parts.Equipped);
            var r = e.Execute(ref s, new RunStart { PortalId = l1.PortalId, LevelId = l1.Id, Mode = RunMode.Normal }, TestHelpers.T0);
            Assert.True(r.Ok, r.Error);
            Assert.Single(RunSystem.Signatures(s.Run.Active)); // the first offered signature, no pick screen yet
            for (int i = 0; i < 500 && s.Run.Active != null; i++)
            {
                var step = e.Execute(ref s, new RunAutoStep { Farming = true, FarmShops = true }, TestHelpers.T0);
                Assert.True(step.Ok, step.Error);
                drafts += step.Events.Count(ev => ev.Type == "run_draft");
            }
            Assert.Equal(0, s.Run.LastSummary.Minibosses);
            rolls += s.Run.LastSummary.Rolls;
            if (s.Run.LastSummary.Won) wins++;
        }
        Assert.InRange(wins, 58, 60);
        Assert.True(drafts / 60.0 >= 3, "level-up skill picks per tutorial run: " + drafts / 60.0);
        Assert.InRange(rolls / 60, 14, 28); // short: about 20 rolls
    }

    [Fact]
    public void SignatureChoice_IsOneOffer_UntilItsUnlock_ThenThree()
    {
        var e = new GameEngine(C);
        var s = e.NewPlayer("p", TestHelpers.T0, 3);
        Assert.Single(RunSystem.SignatureOffer(e, s, TestHelpers.T0));
        s.Onboarding.Unlocked[Features.RunSignatures] = TestHelpers.T0;
        var three = RunSystem.SignatureOffer(e, s, TestHelpers.T0);
        Assert.Equal(C.RunTuning.SignatureChoices, three.Count);
        s.Onboarding.Unlocked.Remove(Features.RunSignatures);
        Assert.Equal(three[0], RunSystem.SignatureOffer(e, s, TestHelpers.T0)[0]); // the single offer is the first of the three
    }

    [Fact]
    public void PoolTiles_CampfiresAndGames_OnlyFromTheirLevels()
    {
        int Count(int level, string tile)
        {
            var e = new GameEngine(C);
            int n = 0;
            for (int seed = 1; seed <= 30; seed++)
            {
                var s = e.NewPlayer("p", TestHelpers.T0, seed);
                s.Flags.Add(Features.DevUnlockAllFlag);
                s.Counters[Requirements.MainLevelCounter] = level - 1;
                var l = C.MainLevel(level)!;
                s.Run.Levels[C.MainLevelId(level - 1)] = new LevelProgress { Cleared = true, Clears = 1 };
                s.Wallet.Add(Currencies.Stamina, 100);
                Assert.True(e.Execute(ref s, new RunStart { PortalId = l.PortalId, LevelId = l.Id }, TestHelpers.T0).Ok);
                n += s.Run.Active!.Tiles.Count(t => t.TileId == tile && !t.Fixed);
            }
            return n;
        }
        Assert.Equal(0, Count(9, "campfire"));
        Assert.True(Count(10, "campfire") > 0);
        Assert.Equal(0, Count(14, ContentDb.MinigameTileId));
        Assert.True(Count(15, ContentDb.MinigameTileId) > 0);
    }
}
