using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// GDD §16 level unlocks, run side: level 1 is the tutorial run (roll, fight, level up and pick skills, beat the boss); shops
/// are on the board from the first run; the signature choice (the second unlock), minibosses (Mythics), two dice and forks,
/// campfires and the Alien Arcade join level by level, each right after its feature unlocks (the level before its first board).
/// </summary>
public class LevelUnlockRunContentTests
{
    private static readonly ContentDb C = ContentDb.Default;

    private static int First(string feature) => C.RunContentLevel(feature);

    [Fact]
    public void FirstBoards_GetEachElement_TheLevelAfterItsFeatureUnlocks()
    {
        Assert.Null(C.Feature("run_shop")); // shops: always there, no unlock
        Assert.Equal(3, First(Features.RunSignatures));
        Assert.Equal(5, First(Features.RunMiniboss));
        Assert.Equal(6, First(Features.RunTwoDice));
        Assert.Equal(10, First(Features.RunCampfire));
        Assert.Equal(15, First(Features.RunMinigames));
        Assert.Equal(C.Campaign.SignatureChoiceFromLevel, First(Features.RunSignatures));
        Assert.Equal(C.MinigameTuning.MinMainLevel, First(Features.RunMinigames));
        Assert.Equal(C.Campaign.CampfiresFromLevel, C.BoardTiles["campfire"].MinMainLevel);
        Assert.All(C.FeatureOrder.Where(f => f.RunContent), f => Assert.Equal(2, f.Since));
        for (int n = 1; n <= 25; n++)
        {
            var l = C.MainLevel(n)!;
            bool Has(string tile) => l.FixedTiles.Any(f => f.TileId == tile);
            Assert.True(Has("shop"), "shops from the first run (level " + n + ")");
            Assert.Equal(n >= First(Features.RunSignatures), l.SignatureChoice);
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
            Assert.True(l.SignatureChoice);
            Assert.Contains(l.FixedTiles, f => f.TileId == "shop");
        }
    }

    [Fact]
    public void TutorialBoard_HasOnlyRollFightLevelUpAndTheBoss()
    {
        var pool = C.TilePools[C.Campaign.TutorialPool].Select(w => C.BoardTiles[w.Id].Category).ToHashSet();
        Assert.Subset(new HashSet<TileCategory> { TileCategory.Empty, TileCategory.Enemy, TileCategory.StatBuff, TileCategory.Pickup, TileCategory.Loot, TileCategory.Shard, TileCategory.Minigame }, pool); // GAME tiles: in every pool, gated by level
        var l1 = C.MainLevel(1)!;
        Assert.All(l1.FixedTiles, f => Assert.Equal("shop", f.TileId)); // shops only (always unlocked)
        Assert.Equal(0, l1.BranchChance);
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var b = RunBoard.Generate(C, l1, new Rng(seed));
            foreach (var t in b.Tiles.Where(t => t.TileId != null))
            {
                var cat = C.BoardTiles[t.TileId].Category;
                Assert.DoesNotContain(cat, new[] { TileCategory.Event, TileCategory.Elite, TileCategory.Hazard, TileCategory.Campfire,
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
            Assert.Empty(RunSystem.Signatures(s.Run.Active)); // no signature before its unlock (the second unlock, level 3)
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
    public void SignatureChoice_IsAnUnlock_NoneOnTheFirstLevels_ThenEveryRunStartsWithThePick()
    {
        // owner, 2026-11-27: the second unlock (the level-2 slot); before it runs have no signature, no pick, no Resonance
        int from = C.Campaign.SignatureChoiceFromLevel;
        Assert.Equal(3, from);
        Assert.Equal(from - 1, (int)Features.UnlockLevel(C.Feature(Features.RunSignatures)!));
        for (int n = 1; n < from; n++)
        {
            var e = new GameEngine(C);
            var s = e.NewPlayer("p", TestHelpers.T0, n);
            s.Counters[Requirements.MainLevelCounter] = n - 1;
            if (n > 1) s.Run.Levels[C.MainLevelId(n - 1)] = new LevelProgress { Cleared = true, Clears = 1 };
            var l = C.MainLevel(n)!;
            Assert.True(e.Execute(ref s, new RunStart { PortalId = l.PortalId, LevelId = l.Id, PickOnBoard = true }, TestHelpers.T0).Ok);
            Assert.NotEqual(RunPhase.Signature, s.Run.Active!.Phase);
            Assert.Empty(RunSystem.Signatures(s.Run.Active));
            Assert.Empty(s.Run.Active.StartSignatures);
        }
        // from the unlock on: the pick on the board before the first roll
        {
            var e = new GameEngine(C);
            var s = e.NewPlayer("p", TestHelpers.T0, 9);
            s.Counters[Requirements.MainLevelCounter] = from - 1;
            for (int n = 1; n < from; n++) s.Run.Levels[C.MainLevelId(n)] = new LevelProgress { Cleared = true, Clears = 1 };
            s.Wallet.Add(Currencies.Stamina, 100);
            var l = C.MainLevel(from)!;
            var r = e.Execute(ref s, new RunStart { PortalId = l.PortalId, LevelId = l.Id, PickOnBoard = true }, TestHelpers.T0);
            Assert.True(r.Ok, r.Error);
            Assert.Equal(RunPhase.Signature, s.Run.Active!.Phase);
            Assert.Equal(C.RunTuning.SignatureChoices, s.Run.Active.StartSignatures.Count);
        }
    }

    [Fact]
    public void SignatureChoice_IsThreeDistinctSignatures()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            var e = new GameEngine(C);
            var s = e.NewPlayer("p", TestHelpers.T0, seed);
            var offer = RunSystem.SignatureOffer(e, s, TestHelpers.T0);
            Assert.Equal(C.RunTuning.SignatureChoices, offer.Count);
            Assert.Equal(offer.Count, offer.Distinct().Count());
            Assert.All(offer, id => Assert.True(C.Skill(id)!.Signature));
            Assert.Equal(offer, RunSystem.SignatureOffer(e, s, TestHelpers.T0)); // deterministic: what the client shows is what the server validates
            var l3 = C.MainLevel(C.Campaign.SignatureChoiceFromLevel)!;
            s.Counters[Requirements.MainLevelCounter] = C.Campaign.SignatureChoiceFromLevel - 1;
            for (int n = 1; n < C.Campaign.SignatureChoiceFromLevel; n++) s.Run.Levels[C.MainLevelId(n)] = new LevelProgress { Cleared = true, Clears = 1 };
            s.Wallet.Add(Currencies.Stamina, 100);
            var pick = offer[seed % offer.Count];
            var r = e.Execute(ref s, new RunStart { PortalId = l3.PortalId, LevelId = l3.Id, SignatureId = pick }, TestHelpers.T0);
            Assert.True(r.Ok, r.Error);
            Assert.Equal(new[] { pick }, RunSystem.Signatures(s.Run.Active));
        }
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
