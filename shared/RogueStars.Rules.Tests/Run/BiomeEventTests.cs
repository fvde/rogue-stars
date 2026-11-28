using System.Text.RegularExpressions;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Biome identity part B (docs/design/biomes, owner-approved 2026-11-28): 4 events + a Rite per biome, the 40% mix on
/// Strange Signal tiles of main levels, no repeats, Rites only in the first half of the run, the Offering (give up an owned
/// skill of one synergy, never a signature), the 13 new shared events and the one-gain-one-price texts.
/// </summary>
public class BiomeEventTests
{
    private static readonly ContentDb C = ContentDb.Default;

    /// <summary>All 17 biomes of the design (8 live, 9 planned by part A).</summary>
    public static readonly string[] AllBiomes =
    {
        "lava", "gas_giant", "seabed", "cloud_islands", "space_station", "alien_jungle", "shipwreck", "toxic_swamp",
        "desert_ruins", "ice", "coral_reef", "neon_city", "mushroom_forest", "candy_planet", "crystal_cave", "clockwork_city", "frozen_future",
    };

    private static readonly string[] NewShared =
    {
        "wandering_trader", "sparring_master", "lost_alien_pup", "comet_fragment", "storm_front", "cold_snap", "reactor_leak",
        "blood_moon", "time_rift", "bounty_board", "wishing_well", "strange_mirror", "supply_drop",
    };

    private static IEnumerable<EventDef> Drawn() =>
        C.RandomRunEvents.Select(w => C.RunEvents[w.Id]).Concat(C.BiomeEvents.Values.SelectMany(l => l).Concat(C.BiomeRites.Values).Select(id => C.RunEvents[id]));

    // ---- content ---------------------------------------------------------------------------------------------------

    [Fact]
    public void EveryBiome_HasFourEvents_AndOneRite_KeyedByItsId()
    {
        Assert.Equal(AllBiomes.OrderBy(x => x), C.BiomeEvents.Keys.OrderBy(x => x));
        Assert.Equal(AllBiomes.OrderBy(x => x), C.BiomeRites.Keys.OrderBy(x => x));
        foreach (var b in AllBiomes)
        {
            Assert.Equal(4, C.BiomeEvents[b].Count);
            Assert.All(C.BiomeEvents[b], id => { Assert.Equal(b, C.RunEvents[id].Biome); Assert.False(C.RunEvents[id].Rite); });
            var rite = C.RunEvents[C.BiomeRites[b]];
            Assert.True(rite.Rite);
            Assert.Equal(b, rite.Biome);
            Assert.Single(rite.Options, o => o.Offering);
            Assert.Equal("Leave", rite.Options[rite.DefaultOption].Text);
        }
        // the live campaign biomes (and any part A adds) all have their events
        Assert.All(C.Campaign.Biomes, bd => Assert.True(C.BiomeEvents.ContainsKey(bd.Id), bd.Id));
    }

    [Fact]
    public void EventIds_AreUnique_BiomeEventsAreNotInTheSharedPool()
    {
        var biomeIds = C.BiomeEvents.Values.SelectMany(l => l).Concat(C.BiomeRites.Values).ToList();
        Assert.Equal(biomeIds.Count, biomeIds.Distinct().Count());
        Assert.Equal(17 * 5, biomeIds.Count);
        Assert.Equal(C.RandomRunEvents.Count, C.RandomRunEvents.Select(w => w.Id).Distinct().Count());
        Assert.DoesNotContain(C.RandomRunEvents, w => biomeIds.Contains(w.Id));
        Assert.All(biomeIds, id => Assert.True(C.RunEvents.ContainsKey(id), id));
        // Strange Spores moved to the Mushroom Forest
        Assert.Contains("strange_spores", C.BiomeEvents["mushroom_forest"]);
        Assert.DoesNotContain(C.RandomRunEvents, w => w.Id == "strange_spores");
        // the 13 new shared events with their weights; Treasure Pod and Campfire stay tile-bound
        Assert.All(NewShared, id => Assert.Contains(C.RandomRunEvents, w => w.Id == id));
        Assert.Equal(3, C.RandomRunEvents.Single(w => w.Id == "supply_drop").Weight);
        Assert.Equal(2.5, C.RandomRunEvents.Single(w => w.Id == "bounty_board").Weight);
        Assert.DoesNotContain(C.RandomRunEvents, w => w.Id == "treasure_pod" || w.Id == "campfire");
    }

    private static readonly HashSet<RunEffectKind> Simple = new()
    {
        RunEffectKind.Coins, RunEffectKind.HealPct, RunEffectKind.DamagePct, RunEffectKind.StatPercent, RunEffectKind.GainSkill,
        RunEffectKind.Battle, RunEffectKind.PendingReward, RunEffectKind.BossStatPct, RunEffectKind.Meter, RunEffectKind.AwakenRatePct,
        RunEffectKind.StrengthenEnemies, RunEffectKind.Shards, RunEffectKind.Xp, RunEffectKind.LoseRandomSkill, RunEffectKind.UpgradeSkill,
        RunEffectKind.Loot,
    };

    [Fact]
    public void BiomeAndNewSharedEvents_UseOnlyExistingSimpleEffects_AndRecordTheirArt()
    {
        var ids = C.BiomeEvents.Values.SelectMany(l => l).Concat(C.BiomeRites.Values).Concat(NewShared);
        foreach (var id in ids)
        {
            var ev = C.RunEvents[id];
            Assert.Matches("^(npc|prop):[a-z_]+$", ev.Art ?? "");
            Assert.InRange(ev.Options.Count, 2, 3);
            foreach (var o in ev.Options)
                foreach (var oc in o.Outcomes)
                    foreach (var fx in oc.Effects)
                        Assert.True(Simple.Contains(fx.Kind), id + ": " + fx.Kind);
            Assert.All(ev.Options.Where(o => o.Offering), o => Assert.True(ev.Rite, id + ": only Rites offer skills"));
        }
    }

    private static bool IsPrice(RunEffect fx) => fx.Kind switch
    {
        RunEffectKind.DamagePct or RunEffectKind.Battle or RunEffectKind.LoseRandomSkill => true,
        RunEffectKind.StrengthenEnemies or RunEffectKind.Meter or RunEffectKind.AwakenRatePct or RunEffectKind.BossStatPct or RunEffectKind.Danger => fx.Value > 0,
        RunEffectKind.StatPercent or RunEffectKind.Coins => fx.Value < 0,
        _ => false,
    };

    [Fact]
    public void EveryDrawnEvent_IsOneGain_AndAtMostOnePrice()
    {
        foreach (var ev in Drawn())
            foreach (var o in ev.Options)
                foreach (var oc in o.Outcomes)
                {
                    // a gamble's coin cost is its stake: each outcome is then one gain or one price ("20 coins: 50% +8% ATK, 50% lose 10% HP")
                    int optionPrice = o.Outcomes.Count > 1 ? 0 : (o.CoinCost > 0 ? 1 : 0) + (o.ShardCost > 0 ? 1 : 0) + (o.Offering ? 1 : 0);
                    int prices = optionPrice + oc.Effects.Count(IsPrice);
                    int gains = oc.Effects.Count(fx => !IsPrice(fx) && !(fx.Kind == RunEffectKind.Coins && fx.Value == 0));
                    Assert.True(gains <= 1, ev.Id + " / " + o.Text + ": " + gains + " gains");
                    Assert.True(prices <= 1, ev.Id + " / " + o.Text + ": " + prices + " prices");
                }
    }

    private static readonly Regex Num = new(@"\d+(?:[.,]\d+)?");

    [Fact]
    public void Texts_FollowTheRoundNumberRule()
    {
        foreach (var ev in Drawn())
            foreach (var o in ev.Options)
                foreach (var text in new[] { o.Text, o.RiskText })
                    foreach (Match m in Num.Matches(text ?? ""))
                    {
                        Assert.DoesNotContain(".", m.Value);
                        Assert.DoesNotContain(",", m.Value);
                        long n = long.Parse(m.Value);
                        Assert.True(n < 20 || n % 5 == 0, ev.Id + ": " + text);
                    }
        // the biome chest ranges are round at every chapter
        foreach (int level in new[] { 1, 30, 200, 900 })
            foreach (var cur in C.RunTuning.BiomeChestRange.Keys)
                Assert.All(RunSystem.BiomeChestRange(C, C.MainLevel(level), cur), v => Assert.Equal(RunSystem.NiceAmount(v), v));
    }

    // ---- the mix rule ---------------------------------------------------------------------------------------------------

    private static ActiveRun FreshRun(long seed = 42, string level = "main_1")
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e, level, seed);
        return s.Run.Active!;
    }

    [Fact]
    public void EventTiles_OnMainLevels_DrawABiomeEvent_FortyPercentOfTheTime()
    {
        var run = FreshRun();
        Assert.Equal("lava", C.RunLevel(run.LevelId).Biome);
        var lava = C.BiomeEvents["lava"].Append(C.BiomeRites["lava"]).ToHashSet();
        int biome = 0, n = 4000;
        for (int i = 0; i < n; i++)
        {
            run.EventsSeen.Clear();
            run.AccountLootOffers = 0;
            if (lava.Contains(RunSystem.PickRandomEvent(C, run)!)) biome++;
        }
        Assert.InRange((double)biome / n, 0.37, 0.43);
    }

    [Fact]
    public void OtherModes_KeepTheSharedPool()
    {
        var run = FreshRun();
        run.LevelId = "the_burrow_1";
        Assert.Null(C.RunLevel(run.LevelId).Biome);
        var biomeIds = C.BiomeEvents.Values.SelectMany(l => l).Concat(C.BiomeRites.Values).ToHashSet();
        for (int i = 0; i < 500; i++)
        {
            run.EventsSeen.Clear();
            Assert.DoesNotContain(RunSystem.PickRandomEvent(C, run)!, biomeIds);
        }
    }

    [Fact]
    public void NoEventRepeats_AndAUsedUpBiome_DrawsShared()
    {
        var run = FreshRun();
        run.AccountLootOffers = -100; // no loot cap in the way
        var seen = new List<string>();
        for (int i = 0; i < 200; i++)
        {
            var id = RunSystem.PickRandomEvent(C, run);
            if (id == null) break; // every event of the pool was drawn
            Assert.DoesNotContain(id, seen);
            seen.Add(id);
            run.EventsSeen.Add(id); // what landing on the tile records
            if (C.BiomeEvents["lava"].Append("rite_lava").All(seen.Contains) && RunSystem.BiomeEventCandidates(C, run, "lava", out _).Count == 0)
            {
                // the five lava events are used up: the biome draws nothing, every tile now draws a shared event
                Assert.Null(RunSystem.PickBiomeEvent(C, run, "lava"));
                for (int k = 0; k < 5; k++)
                {
                    var next = RunSystem.PickRandomEvent(C, run);
                    if (next == null) break;
                    Assert.Contains(C.RandomRunEvents, w => w.Id == next);
                    Assert.DoesNotContain(next, seen);
                    seen.Add(next); run.EventsSeen.Add(next);
                }
            }
        }
        Assert.All(C.BiomeEvents["lava"].Append("rite_lava"), b => Assert.Contains(b, seen));
        Assert.True(seen.Count > 20);
    }

    [Fact]
    public void LandingOnAnEventTile_RecordsTheEvent()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        RunTestKit.LineBoard(s.Run.Active!, Enumerable.Repeat("event", 30).ToArray());
        var r = RunTestKit.Exec(e, ref s, new RunRoll());
        Assert.True(r.Ok, r.Error);
        var run = s.Run.Active!;
        Assert.Equal(RunPhase.Event, run.Phase);
        Assert.Equal(new[] { run.EventId }, run.EventsSeen);
    }

    [Fact]
    public void Rite_WeighsDouble_AndOnlyInTheFirstHalfOfTheRun()
    {
        var run = FreshRun();
        Assert.True(RunSystem.RiteOpen(C, run));
        var ids = RunSystem.BiomeEventCandidates(C, run, "lava", out var w);
        Assert.Equal(5, ids.Count);
        Assert.Equal(C.RunTuning.RiteWeight, w[ids.IndexOf("rite_lava")]);
        Assert.Equal(2, C.RunTuning.RiteWeight);
        Assert.All(C.BiomeEvents["lava"], id => Assert.Equal(1, w[ids.IndexOf(id)]));

        // past half of the expected rolls (HUD "Roll n/N"): by the roll limit …
        run.Rolls = run.MaxRolls / 2 + 1;
        Assert.False(RunSystem.RiteOpen(C, run));
        // … or by the boss meter (the boss is closer than the rolls already made)
        run.Rolls = 4;
        run.Awakening = 90;
        Assert.True(RunSystem.BossRollEstimate(C, run) < 8);
        Assert.False(RunSystem.RiteOpen(C, run));
        ids = RunSystem.BiomeEventCandidates(C, run, "lava", out _);
        Assert.DoesNotContain("rite_lava", ids);
        Assert.Equal(4, ids.Count);
        for (int i = 0; i < 300; i++) { run.EventsSeen.Clear(); Assert.NotEqual("rite_lava", RunSystem.PickRandomEvent(C, run)); }
    }

    // ---- the Offering ---------------------------------------------------------------------------------------------------

    private static string SkillOf(SkillFamily f, bool signature) =>
        RunDraft.SortedSkillIds(C).First(id => C.Skills[id].Family == f && C.Skills[id].Signature == signature
            && (signature || C.Skills[id].Source == SkillSource.Run && C.Skills[id].Draftable));

    private static (GameEngine e, GameState s) AtRite(string biome)
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 20).ToArray());
        run.Position = 1;
        run.Phase = RunPhase.Event;
        run.EventId = C.BiomeRites[biome];
        run.EventTile = 1;
        return (e, s);
    }

    [Fact]
    public void Offering_NeedsAnOwnedSkillOfTheSynergy()
    {
        var (e, s) = AtRite("lava");
        var run = s.Run.Active!;
        run.Skills.RemoveAll(id => C.Skill(id)?.Family == SkillFamily.Ice && !C.Skill(id)!.Signature);
        var rite = C.RunEvents["rite_lava"];
        int offer = rite.Options.FindIndex(o => o.Offering);
        Assert.Equal(SkillFamily.Ice, rite.Options[offer].OfferFamily);
        Assert.False(RunSystem.OptionAvailable(C, run, rite.Options[offer]));
        Assert.False(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = offer }).Ok);

        string ice = SkillOf(SkillFamily.Ice, false);
        s.Run.Active!.Skills.Add(ice);
        Assert.True(RunSystem.OptionAvailable(C, s.Run.Active, rite.Options[offer]));
        Assert.Equal(new[] { ice }, RunSystem.OfferChoices(C, s.Run.Active, SkillFamily.Ice));
        var r = RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = offer, SkillId = ice });
        Assert.True(r.Ok, r.Error);
        run = s.Run.Active!;
        Assert.DoesNotContain(ice, run.Skills);
        Assert.Equal(-20, run.BossAtkPct);
        Assert.Equal(0, run.BossHpPct);
        Assert.Equal(0, run.BossStatPct);
        var o = run.LastOutcome!;
        Assert.Equal(OutcomeShow.Popups, o.Show); // deterministic: flies into the HUD
        Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.SkillLost && l.Id == ice);
        Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.BossStat && l.Id == RunSystem.BossAtkParam && l.Amount == -20 && l.Good);
    }

    [Fact]
    public void Offering_NeverTakesASignature()
    {
        var (e, s) = AtRite("seabed"); // offers a Heat (Fire) skill
        var run = s.Run.Active!;
        string sig = SkillOf(SkillFamily.Fire, true);
        run.Skills.RemoveAll(id => C.Skill(id)?.Family == SkillFamily.Fire);
        run.Skills.Add(sig);
        run.SignatureIds.Add(sig);
        int offer = C.RunEvents["rite_seabed"].Options.FindIndex(o => o.Offering);
        Assert.Empty(RunSystem.OfferChoices(C, run, SkillFamily.Fire));
        Assert.False(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = offer, SkillId = sig }).Ok);
        Assert.Contains(sig, s.Run.Active!.Skills);

        // with a plain Fire skill too, only that one can go, and it weakens the enemies ahead
        string fire = SkillOf(SkillFamily.Fire, false);
        s.Run.Active.Skills.Add(fire);
        Assert.Equal(new[] { fire }, RunSystem.OfferChoices(C, s.Run.Active, SkillFamily.Fire));
        Assert.False(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = offer, SkillId = sig }).Ok);
        RunTestKit.LineBoard(s.Run.Active, Enumerable.Repeat("enemy_1", 20).ToArray());
        s.Run.Active.Position = 1; s.Run.Active.EventTile = 1;
        Assert.True(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = offer, SkillId = fire }).Ok);
        run = s.Run.Active!;
        Assert.Contains(sig, run.Skills);
        Assert.DoesNotContain(fire, run.Skills);
        Assert.Contains(run.Tiles, t => t.BonusPct == -25);
        Assert.Contains(run.LastOutcome!.Lines, l => l.Kind == OutcomeKind.EnemiesStronger && l.Amount == -25 && l.Good);
    }

    [Fact]
    public void AlienJungle_OffersASkillWithNoSynergy()
    {
        var opt = C.RunEvents["rite_alien_jungle"].Options.Single(o => o.Offering);
        Assert.Equal(SkillFamily.None, opt.OfferFamily);
    }

    [Fact]
    public void WeakenedEnemies_AndWeakerBoss_AreNotBadOutcomes()
    {
        Assert.False(RunSystem.IsNegativeOutcome(new EventOutcome { Effects = { new RunEffect(RunEffectKind.StrengthenEnemies, -25) } }));
        Assert.True(RunSystem.IsNegativeOutcome(new EventOutcome { Effects = { new RunEffect(RunEffectKind.StrengthenEnemies, 10) } }));
        Assert.False(RunSystem.IsNegativeOutcome(new EventOutcome { Effects = { new RunEffect(RunEffectKind.BossStatPct, -20, RunSystem.BossHpParam) } }));
    }

    [Fact]
    public void BiomeChest_PaysOneRoundRollOfItsResource_ScaledWithTheChapter()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 20).ToArray());
        run.Position = 1; run.Phase = RunPhase.Event; run.EventId = "gas_refinery_overflow"; run.EventTile = 1;
        int loot = run.PendingLoot.Count, danger = run.Danger;
        Assert.True(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = 0 }).Ok);
        run = s.Run.Active!;
        var chest = run.PendingLoot.Skip(loot).Single();
        Assert.Equal(Currencies.Fuel, chest.Id);
        var range = RunSystem.BiomeChestRange(C, C.RunLevel(run.LevelId), Currencies.Fuel);
        Assert.InRange(chest.Amount, range[0], range[1]);
        Assert.Equal(RunSystem.NiceAmount(chest.Amount), chest.Amount);
        Assert.Equal(danger + 1, run.Danger);
        Assert.Equal(new long[] { 20, 40 }, RunSystem.BiomeChestRange(C, C.MainLevel(1), Currencies.Fuel)); // chapter 1: doubled 10–20
        var late = RunSystem.BiomeChestRange(C, C.MainLevel(400), Currencies.Fuel);
        Assert.True(late[0] > 20, "scales with the chapter");
        Assert.Equal(OutcomeShow.Popups, run.LastOutcome!.Show); // a known resource: flies into the HUD
    }

    [Fact]
    public void RandomImprovement_HasNoPicker_AndShowsTheSheet()
    {
        var well = C.RunEvents["wishing_well"].Options[0];
        Assert.False(RunSystem.ImprovesSkill(well));
        Assert.True(RunSystem.HasRandomPick(well.Outcomes[0]));
        Assert.True(RunSystem.ImprovesSkill(C.RunEvents["crystal_singer"].Options[0])); // "of your choice": the picker
    }
}
