using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Progression.ProgressionKit;

namespace RogueStars.Rules.Tests.Progression;

/// <summary>
/// Parts v2 (docs/design/parts.md, docs/design/parts-v2/): four standard item sets and 30 Mythics, stats and growth, level caps,
/// the Mythic ladder (copies + other Mythics of the slot, Metal, Ore), feeding for Metal, the Replicator, awakening, interchangeable
/// arms, runes and the Forge, and the reset of a save from before parts v2.
/// </summary>
public class PartsTests
{
    private const string Blaster = "myth_firebrand_blaster";   // arm, Fire
    private const string Cannon = "myth_shatter_cannon";       // arm, Ice
    private const string Whip = "myth_chainlash_whip";         // arm, Combo
    private const string Crown = "myth_cinder_crown";          // head
    private const string Visor = "myth_isotope_visor";         // head

    private static PartItem Add(GameState s, string def, PartRarity r = PartRarity.Mythic) => PartsSystem.AddPart(s, C, def, (int)r);

    private static GameState Rich(GameEngine e)
    {
        var s = Player(e);
        SetBalance(s, Currencies.Ore, 100_000_000); SetBalance(s, Currencies.GearCores, 1_000_000); SetBalance(s, Currencies.Metal, 100_000);
        return s;
    }

    // ---- item sets ----------------------------------------------------------------------------------------------------

    [Fact]
    public void ItemSets_FourStandardRaritiesOf30_And30Mythics_SixPerSlot()
    {
        var defs = C.PartsConfig.Defs.Values.ToList();
        Assert.Equal(150, defs.Count);
        foreach (var r in new[] { PartRarity.Common, PartRarity.Rare, PartRarity.Epic, PartRarity.Legendary, PartRarity.Mythic })
        {
            var set = defs.Where(d => d.Rarity == r).ToList();
            Assert.Equal(30, set.Count);
            foreach (var k in ContentDb.PartKinds) Assert.Equal(6, set.Count(d => d.Kind == k));
            if (r != PartRarity.Mythic)
                foreach (var k in ContentDb.PartKinds)
                    Assert.Equal(ContentDb.PartRoles.OrderBy(x => x), set.Where(d => d.Kind == k).Select(d => d.Role).OrderBy(x => x)); // one per role
        }
        // tier role slot names; weapon nouns are cosmetic
        Assert.Equal("Scrap Guard Helm", C.PartsConfig.Defs["c_head_guard"].Name);
        Assert.Equal("Quantum Fury Blaster", C.PartsConfig.Defs["l_arm_fury"].Name);
        Assert.Equal("Plasma Keen Claw", C.PartsConfig.Defs["e_arm_keen"].Name);
        Assert.Equal("Firebrand Blaster", C.PartsConfig.Defs[Blaster].Name);
        Assert.Equal("Starburst Blaster", C.PartsConfig.Defs[Blaster].AwakenedName);
        // the ladder: no Uncommon, no SS+1 / SS+2
        Assert.Equal(new[] { "Common", "Rare", "Epic", "Legendary", "Mythic", "Mythic+1", "Mythic+2", "Mythic S", "Mythic S+1", "Mythic S+2", "Mythic SS" },
            C.PartsConfig.Rarities.Select(r => r.Name));
    }

    // ---- stats, growth, caps ------------------------------------------------------------------------------------------

    [Fact]
    public void Stats_SlotCoreTimesRarity_PlusRoleStat_PageExamples()
    {
        // Firebrand Blaster: +85 ATK, +13 per level → +1 372 ATK at 100; +10% fire damage → +30% at 100; awakened ×1.4: 120 / 18, +14% → +42%
        var f = C.PartsConfig.Defs[Blaster];
        Assert.Equal(85, PartsSystem.DefStats(C, f, 1).Value(StatId.Atk), 6);
        Assert.Equal(13, f.StatsPerLevel.Single(m => m.Stat == StatId.Atk).Flat, 6);
        Assert.Equal(1372, PartsSystem.DefStats(C, f, 100).Value(StatId.Atk), 6);
        Assert.Equal(10, PartsSystem.DefStats(C, f, 1).Value(StatId.FireDamage), 6);
        Assert.Equal(30, PartsSystem.DefStats(C, f, 100).Value(StatId.FireDamage), 6);
        Assert.Equal(120, PartsSystem.DefStats(C, f, 1, true).Value(StatId.Atk), 6);
        Assert.Equal(14, PartsSystem.DefStats(C, f, 1, true).Value(StatId.FireDamage), 6);
        Assert.Equal(42, PartsSystem.DefStats(C, f, 100, true).Value(StatId.FireDamage), 6);
        // a Common +3% crit chance → +9% at its cap 10 (Scrap Keen Claw: ATK 15 + 3 crit chance)
        var claw = C.PartsConfig.Defs["c_arm_keen"];
        Assert.Equal(15, PartsSystem.DefStats(C, claw, 1).Value(StatId.Atk), 6);
        Assert.Equal(9, PartsSystem.DefStats(C, claw, 10).Value(StatId.CritChance), 6);
        // Quantum Guard Shell: HP 100 × 3.6 = 360, DEF (6 + 6) × 3.6 = 43.2 → 45 (multiples of 5 from 20 up)
        var shell = C.PartsConfig.Defs["l_body_guard"];
        Assert.Equal(360, PartsSystem.DefStats(C, shell, 1).Value(StatId.Hp), 6);
        Assert.Equal(45, PartsSystem.DefStats(C, shell, 1).Value(StatId.Def), 6);
        // arms add a second role stat: Guard counter chance, Swift combo chance
        Assert.Equal(3, PartsSystem.DefStats(C, C.PartsConfig.Defs["c_arm_guard"], 1).Value(StatId.CounterChance), 6);
        Assert.Equal(5, PartsSystem.DefStats(C, C.PartsConfig.Defs["r_arm_swift"], 1).Value(StatId.ComboChance), 6);
        // a Mythic's synergy stat as a percentage of HP (Last Stand Plate +10% HP)
        Assert.Equal(10, PartsSystem.DefStats(C, C.PartsConfig.Defs["myth_last_stand_plate"], 1).Percent[StatId.Hp], 6);
    }

    [Fact]
    public void Growth_FlatPlus15PercentPerLevel_PercentLinearTo3xAtTheMaxLevel_ForEveryDef()
    {
        foreach (var d in C.PartsConfig.Defs.Values)
            foreach (var aw in d.IsMythic ? new[] { false, true } : new[] { false })
            {
                int cap = d.IsMythic ? 100 : C.PartsConfig.Rarities[(int)d.Rarity].LevelCap;
                var bs = aw ? d.AwakenedBaseStats : d.BaseStats;
                var pl = aw ? d.AwakenedStatsPerLevel : d.StatsPerLevel;
                Assert.Equal(bs.Count, pl.Count);
                for (int i = 0; i < bs.Count; i++)
                {
                    bool pct = bs[i].Percent != 0 || StatMod.IsPointStat(bs[i].Stat);
                    double b = bs[i].Flat + bs[i].Percent, step = pl[i].Flat + pl[i].Percent;
                    Assert.True(b > 0, d.Id);
                    if (pct) Assert.Equal(3 * b, b + (cap - 1) * step, 6);
                    else { Assert.Equal(Math.Floor(b), b); Assert.Equal(ContentDb.PartRound(b * 0.15), step, 6); }
                }
            }
    }

    [Fact]
    public void LevelCaps_PerRarity_AndPerMythicStep_UpTo100AtSS()
    {
        var e = Engine(); var s = Rich(e);
        int[] caps = { 10, 20, 30, 50, 70, 75, 80, 85, 90, 95, 100 };
        for (int r = 0; r < caps.Length; r++) Assert.Equal(caps[r], C.PartsConfig.Rarities[r].LevelCap);
        Assert.Equal(100, C.PartsConfig.MaxLevel);
        var common = Add(s, "c_head_mend", PartRarity.Common);
        Ok(e, ref s, new PartLevelUp { Uid = common.Uid, Levels = 9 });
        Assert.Contains("cap", Fail(e, ref s, new PartLevelUp { Uid = common.Uid }));
        var ss = Add(s, Crown, PartRarity.MythicSS);
        Ok(e, ref s, new PartLevelUp { Uid = ss.Uid, Levels = 99 });
        Assert.Equal(100, s.Parts.Items[ss.Uid].Level);
        // Ore 30·L^1.4 per level, Gear Cores from level 10 (today's curves)
        Assert.Equal(30, C.PartsConfig.LevelOreCosts[1]);
        Assert.Equal(0, C.PartsConfig.LevelGearCoreCosts[9]);
        Assert.True(C.PartsConfig.LevelGearCoreCosts[10] > 0);
    }

    [Fact]
    public void Skills_ActivePerRarity_Epic1_Legendary2_Mythic3_S4_SS5()
    {
        int[] expected = { 0, 0, 1, 2, 3, 3, 3, 4, 4, 4, 5 };
        for (int r = 0; r < expected.Length; r++) Assert.Equal(expected[r], PartsSystem.SkillCount(C, r));
        var p = new PartItem { Uid = "x", DefId = Blaster, Rarity = (int)PartRarity.MythicS };
        Assert.Equal(C.PartsConfig.Defs[Blaster].SkillIds.Take(4), PartsSystem.PartSkills(C, p));
        Assert.Single(PartsSystem.PartSkills(C, new PartItem { DefId = "e_tail_keen", Rarity = (int)PartRarity.Epic }));
        Assert.Equal(2, PartsSystem.PartSkills(C, new PartItem { DefId = "l_tail_keen", Rarity = (int)PartRarity.Legendary }).Count);
        Assert.Empty(PartsSystem.PartSkills(C, new PartItem { DefId = "r_tail_keen", Rarity = (int)PartRarity.Rare }));
    }

    // ---- equipping ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Arms_AreInterchangeable_AnyArmFitsEitherArm()
    {
        var e = Engine(); var s = Rich(e);
        var a1 = Add(s, Blaster); var a2 = Add(s, "c_arm_guard", PartRarity.Common); var head = Add(s, Crown);
        Ok(e, ref s, new PartEquip { Uid = a1.Uid, Slot = PartSlot.LeftArm });
        Ok(e, ref s, new PartEquip { Uid = a2.Uid, Slot = PartSlot.RightArm });
        Assert.Contains("arm", Fail(e, ref s, new PartEquip { Uid = a1.Uid, Slot = PartSlot.Head }));
        Assert.Contains("fit", Fail(e, ref s, new PartEquip { Uid = head.Uid, Slot = PartSlot.LeftArm }));
        // moving an arm to the other arm leaves the first arm empty
        Ok(e, ref s, new PartEquip { Uid = a1.Uid, Slot = PartSlot.RightArm });
        Assert.False(s.Parts.Equipped.ContainsKey(PartSlot.LeftArm));
        var l = e.BuildLoadout(s, TestHelpers.T0);
        Assert.Contains(Blaster + "_sk1", l.MetaSkillIds);
    }

    // ---- Mythic ladder -------------------------------------------------------------------------------------------------

    [Fact]
    public void Ladder_CopiesOtherMythicsMetalAndOre_PerTheTable()
    {
        int[] copies = { 1, 1, 1, 2, 2, 5 }, others = { 0, 0, 0, 2, 2, 5 };
        long[] metal = { 100, 150, 200, 300, 450, 600 }, ore = { 2200, 3400, 5100, 7600, 11400, 17100 };
        for (int i = 0; i < 6; i++)
        {
            var step = C.PartsConfig.Rarities[(int)PartRarity.Mythic1 + i].Climb!;
            Assert.Equal((copies[i], others[i], metal[i], ore[i]), (step.Copies, step.Others, step.Metal, step.Ore));
        }
        Assert.Equal(12, copies.Sum()); Assert.Equal(9, others.Sum()); Assert.Equal(1800, metal.Sum());
        for (int r = 0; r <= (int)PartRarity.Mythic; r++) Assert.Null(C.PartsConfig.Rarities[r].Climb); // nothing merges into a rarity
    }

    [Fact]
    public void Climb_MythicToSS_WithExactMaterials_ArmsShareOnePool()
    {
        var e = Engine(); var s = Rich(e);
        var t = Add(s, Blaster);
        long metal0 = s.Wallet.Get(Currencies.Metal), ore0 = s.Wallet.Get(Currencies.Ore);
        for (int step = 1; step <= 6; step++)
        {
            var c = C.PartsConfig.Rarities[(int)PartRarity.Mythic + step].Climb!;
            var mats = new List<string>();
            for (int i = 0; i < c.Copies; i++) mats.Add(Add(s, Blaster).Uid);
            for (int i = 0; i < c.Others; i++) mats.Add(Add(s, i % 2 == 0 ? Cannon : Whip).Uid); // any other arm Mythic
            Ok(e, ref s, new PartClimb { TargetUid = t.Uid, MaterialUids = mats });
            Assert.Equal((int)PartRarity.Mythic + step, s.Parts.Items[t.Uid].Rarity);
            Assert.All(mats, u => Assert.False(s.Parts.Items.ContainsKey(u)));
        }
        Assert.Equal((int)PartRarity.MythicSS, s.Parts.Items[t.Uid].Rarity);
        Assert.Equal(metal0 - 1800, s.Wallet.Get(Currencies.Metal));
        Assert.Equal(ore0 - (2200 + 3400 + 5100 + 7600 + 11400 + 17100), s.Wallet.Get(Currencies.Ore));
        Assert.Contains("SS", Fail(e, ref s, new PartClimb { TargetUid = t.Uid, MaterialUids = { Add(s, Blaster).Uid } }));
        Assert.Equal(5, PartsSystem.PartSkills(C, s.Parts.Items[t.Uid]).Count);
    }

    [Fact]
    public void Climb_RejectsWrongMaterials()
    {
        var e = Engine(); var s = Rich(e);
        var t = Add(s, Blaster, PartRarity.MythicS);
        var copy1 = Add(s, Blaster); var copy2 = Add(s, Blaster);
        var otherArm = Add(s, Cannon); var otherArm2 = Add(s, Whip);
        var head = Add(s, Crown);
        // a copy of the climbed item counts only as a copy, never as an other Mythic
        Assert.Contains("copies", Fail(e, ref s, new PartClimb { TargetUid = t.Uid, MaterialUids = { copy1.Uid, copy2.Uid, Add(s, Blaster).Uid, otherArm.Uid } }));
        // other Mythics must be of the same slot
        Assert.Contains("slot", Fail(e, ref s, new PartClimb { TargetUid = t.Uid, MaterialUids = { copy1.Uid, copy2.Uid, otherArm.Uid, head.Uid } }));
        // climbed or awakened Mythics are never material; neither are equipped or locked ones
        var climbed = Add(s, Whip, PartRarity.Mythic1);
        Assert.Contains("base-step", Fail(e, ref s, new PartClimb { TargetUid = t.Uid, MaterialUids = { copy1.Uid, copy2.Uid, otherArm.Uid, climbed.Uid } }));
        Ok(e, ref s, new PartLock { Uid = otherArm2.Uid });
        Assert.Contains("base-step", Fail(e, ref s, new PartClimb { TargetUid = t.Uid, MaterialUids = { copy1.Uid, copy2.Uid, otherArm.Uid, otherArm2.Uid } }));
        Ok(e, ref s, new PartLock { Uid = otherArm2.Uid, Locked = false });
        // no Metal → no step
        SetBalance(s, Currencies.Metal, 299);
        Assert.Contains("metal", Fail(e, ref s, new PartClimb { TargetUid = t.Uid, MaterialUids = { copy1.Uid, copy2.Uid, otherArm.Uid, otherArm2.Uid } }));
        SetBalance(s, Currencies.Metal, 300);
        Ok(e, ref s, new PartClimb { TargetUid = t.Uid, MaterialUids = { copy1.Uid, copy2.Uid, otherArm.Uid, otherArm2.Uid } });
        Assert.Equal((int)PartRarity.MythicS1, s.Parts.Items[t.Uid].Rarity);
        // standard parts never climb
        var leg = Add(s, "l_arm_fury", PartRarity.Legendary);
        Assert.Contains("Mythic", Fail(e, ref s, new PartClimb { TargetUid = leg.Uid }));
    }

    [Fact]
    public void Climb_MaterialsRefundTheirLevelOreAndRunes()
    {
        var e = Engine(); var s = Rich(e);
        var t = Add(s, Visor);
        var copy = Add(s, Visor);
        Ok(e, ref s, new PartLevelUp { Uid = copy.Uid, Levels = 5 });
        var rune = PartsSystem.AddRune(s, C, "rune_fury", 1);
        Ok(e, ref s, new RuneSocket { RuneUid = rune.Uid, PartUid = copy.Uid, SlotIndex = 0 });
        long ore = s.Wallet.Get(Currencies.Ore), spent = s.Parts.Items[copy.Uid].SpentOre;
        Ok(e, ref s, new PartClimb { TargetUid = t.Uid, MaterialUids = { copy.Uid } });
        Assert.Equal(ore + spent - 2200, s.Wallet.Get(Currencies.Ore));
        Assert.Equal("", s.Parts.Runes[rune.Uid].SocketedIn);
    }

    // ---- Metal ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Feed_StandardPartsBecomeMetal_1_2_6_20_NeverMythicsEquippedLockedOrForged()
    {
        var e = Engine(); var s = Rich(e); SetBalance(s, Currencies.Metal, 0);
        var c = Add(s, "c_legs_swift", PartRarity.Common); var r = Add(s, "r_legs_swift", PartRarity.Rare);
        var ep = Add(s, "e_legs_swift", PartRarity.Epic); var l = Add(s, "l_legs_swift", PartRarity.Legendary);
        Ok(e, ref s, new PartLevelUp { Uid = l.Uid, Levels = 12 });
        long ore = s.Wallet.Get(Currencies.Ore), cores = s.Wallet.Get(Currencies.GearCores);
        long spentOre = s.Parts.Items[l.Uid].SpentOre, spentCores = s.Parts.Items[l.Uid].SpentGearCores;
        var res = Ok(e, ref s, new PartFeed { Uids = { c.Uid, r.Uid, ep.Uid, l.Uid } });
        Assert.Equal(29L, res.Payload);
        Assert.Equal(29, s.Wallet.Get(Currencies.Metal));
        Assert.Equal(ore + spentOre, s.Wallet.Get(Currencies.Ore));
        Assert.Equal(cores + spentCores, s.Wallet.Get(Currencies.GearCores));
        Assert.Empty(s.Parts.Items.Values.Where(p => p.DefId.EndsWith("legs_swift", StringComparison.Ordinal)));

        var m = Add(s, Crown);
        Assert.Contains("Mythic", Fail(e, ref s, new PartFeed { Uids = { m.Uid } }));
        var eq = Add(s, "c_head_keen", PartRarity.Common);
        Ok(e, ref s, new PartEquip { Uid = eq.Uid, Slot = PartSlot.Head });
        Assert.Contains("unequip", Fail(e, ref s, new PartFeed { Uids = { eq.Uid } }));
        var locked = Add(s, "c_tail_keen", PartRarity.Common);
        Ok(e, ref s, new PartLock { Uid = locked.Uid });
        Assert.Contains("locked", Fail(e, ref s, new PartFeed { Uids = { locked.Uid } }));
        var forged = Add(s, "e_tail_keen", PartRarity.Epic); forged.ForgedSlots = 1;
        Assert.Contains("forged", Fail(e, ref s, new PartFeed { Uids = { forged.Uid } }));
    }

    // ---- Replicator ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Replicator_ThreeSpareMythicsAnySlot_Plus5000Ore_GiveACopyOfAnOwnedMythic()
    {
        var e = Engine(); var s = Rich(e);
        var target = Add(s, Blaster, PartRarity.MythicS);
        var a = Add(s, Crown); var b = Add(s, "myth_rimeguard_plate"); var c = Add(s, "myth_storm_coil");
        long ore = s.Wallet.Get(Currencies.Ore);
        var res = Ok(e, ref s, new PartReplicate { TargetUid = target.Uid, MaterialUids = { a.Uid, b.Uid, c.Uid } });
        var copy = s.Parts.Items[(string)res.Payload!];
        Assert.Equal(Blaster, copy.DefId);
        Assert.Equal((int)PartRarity.Mythic, copy.Rarity);
        Assert.Equal(ore - 5000, s.Wallet.Get(Currencies.Ore));
        Assert.False(s.Parts.Items.ContainsKey(a.Uid));
        // exactly three, base step, not awakened; Mythics only
        var x = Add(s, Crown); var y = Add(s, Visor);
        Assert.Contains("3", Fail(e, ref s, new PartReplicate { TargetUid = target.Uid, MaterialUids = { x.Uid, y.Uid } }));
        var z = Add(s, Visor); z.Awaken = 1;
        Assert.Contains("awakened", Fail(e, ref s, new PartReplicate { TargetUid = target.Uid, MaterialUids = { x.Uid, y.Uid, z.Uid } }));
        var std = Add(s, "l_head_fury", PartRarity.Legendary);
        Assert.Contains("Mythic", Fail(e, ref s, new PartReplicate { TargetUid = std.Uid, MaterialUids = { x.Uid, y.Uid, Add(s, Visor).Uid } }));
    }

    // ---- awakening -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Awakening_MythicOnce_50Cores_NewNameStatsAndSkills()
    {
        var e = Engine(); var s = Rich(e);
        SetBalance(s, ProgressionCurrencies.PartAwakenCores, 120);
        var leg = Add(s, "l_arm_fury", PartRarity.Legendary);
        Assert.Contains("rarity", Fail(e, ref s, new PartAwaken { Uid = leg.Uid }));
        var p = Add(s, Blaster, PartRarity.MythicS);
        Ok(e, ref s, new PartLevelUp { Uid = p.Uid, Levels = 10 });
        double atk = PartsSystem.PartStats(s, C, s.Parts.Items[p.Uid]).Value(StatId.Atk);
        Ok(e, ref s, new PartAwaken { Uid = p.Uid });
        var a = s.Parts.Items[p.Uid];
        Assert.Equal(1, a.Awaken);
        Assert.Equal(70, s.Wallet.Get(ProgressionCurrencies.PartAwakenCores));
        Assert.Equal("Starburst Blaster", PartsSystem.DisplayName(C, a));
        Assert.Equal(120 + 10 * 18, PartsSystem.PartStats(s, C, a).Value(StatId.Atk), 6);
        Assert.True(PartsSystem.PartStats(s, C, a).Value(StatId.Atk) > atk);
        Assert.Equal(C.PartsConfig.Defs[Blaster].AwakenedSkillIds.Take(4), PartsSystem.PartSkills(C, a)); // the rung still decides how many
        Assert.Contains("already", Fail(e, ref s, new PartAwaken { Uid = p.Uid }));
        // an awakened Mythic still climbs (as the target)
        var mats = new List<string> { Add(s, Blaster).Uid, Add(s, Blaster).Uid, Add(s, Cannon).Uid, Add(s, Whip).Uid };
        Ok(e, ref s, new PartClimb { TargetUid = p.Uid, MaterialUids = mats });
    }

    // ---- runes and Forge -------------------------------------------------------------------------------------------------

    [Fact]
    public void Runes_SlotsByRarity_Merge3To1_LockAndSocketRules()
    {
        var e = Engine(); var s = Player(e);
        int[] slots = { 0, 1, 1, 2, 3, 3, 3, 4, 4, 4, 5 };
        for (int r = 0; r < slots.Length; r++) Assert.Equal(slots[r], C.PartsConfig.Rarities[r].RuneSlots);
        var r1 = Enumerable.Range(0, 4).Select(_ => PartsSystem.AddRune(s, C, "rune_fury", 1)).ToList();
        var other = PartsSystem.AddRune(s, C, "rune_vigor", 1);
        Ok(e, ref s, new RuneLock { RuneUid = r1[0].Uid, Locked = true });
        Assert.Contains("locked", Fail(e, ref s, new RuneMerge { RuneUids = { r1[0].Uid, r1[1].Uid, r1[2].Uid } }));
        var mergedUid = (string)Ok(e, ref s, new RuneMerge { RuneUids = { r1[1].Uid, r1[2].Uid, r1[3].Uid } }).Payload!;
        var merged = s.Parts.Runes[mergedUid];
        Assert.Equal(2, merged.Rarity);
        var common = Add(s, "c_head_guard", PartRarity.Common);
        Assert.Contains("locked", Fail(e, ref s, new RuneSocket { RuneUid = merged.Uid, PartUid = common.Uid, SlotIndex = 0 }));
        var leg = Add(s, "l_head_guard", PartRarity.Legendary);
        Ok(e, ref s, new RuneSocket { RuneUid = merged.Uid, PartUid = leg.Uid, SlotIndex = 0 });
        Assert.Contains("type", Fail(e, ref s, new RuneSocket { RuneUid = r1[0].Uid, PartUid = leg.Uid, SlotIndex = 1 }));
        Ok(e, ref s, new RuneSocket { RuneUid = other.Uid, PartUid = leg.Uid, SlotIndex = 1 });
        Assert.Equal(2 * C.PartsConfig.RuneRarityMultipliers[2], PartsSystem.PartStats(s, C, s.Parts.Items[leg.Uid]).Percent[StatId.Atk], 6);
    }

    [Fact]
    public void Forge_EpicPlus_AddsRuneSlotAndPercentToLevelOneStats()
    {
        var e = Engine(); var s = Rich(e); SetBalance(s, Currencies.Diamonds, 10_000);
        Assert.Contains("rarity", Fail(e, ref s, new PartForgeUnlock { Uid = Add(s, "r_head_mend", PartRarity.Rare).Uid }));
        var p = Add(s, "e_head_mend", PartRarity.Epic);
        Ok(e, ref s, new PartLevelUp { Uid = p.Uid, Levels = 10 });
        var def = C.PartsConfig.Defs["e_head_mend"];
        double b = def.BaseStats.Single(m => m.Stat == StatId.Hp).Flat, step = def.StatsPerLevel.Single(m => m.Stat == StatId.Hp).Flat;
        Ok(e, ref s, new PartForgeUnlock { Uid = p.Uid });
        Assert.Equal(b * 1.1 + 10 * step, PartsSystem.PartStats(s, C, s.Parts.Items[p.Uid]).Value(StatId.Hp), 6);
        Assert.Equal(2, PartsSystem.RuneSlotCount(C, s.Parts.Items[p.Uid]));
    }

    // ---- old saves -------------------------------------------------------------------------------------------------------

    [Fact]
    public void OldSave_IsResetToTheStarterSet_OneCommonPerSlotEquipped_RunesKept()
    {
        var e = Engine(); var s = Player(e);
        var rune = PartsSystem.AddRune(s, C, "rune_fury", 1);
        // a save from before parts v2: an old item and model 0
        s.Parts.Items["p_old"] = new PartItem { Uid = "p_old", DefId = "arm_plasma_blaster", Rarity = 7, Runes = { rune.Uid } };
        rune.SocketedIn = "p_old";
        s.Parts.Equipped[PartSlot.RightArm] = "p_old";
        s.Parts.Model = 0;
        Ok(e, ref s, new PartUnequip { Slot = PartSlot.Head });
        Assert.Equal(PartsSystem.ModelVersion, s.Parts.Model);
        Assert.False(s.Parts.Items.ContainsKey("p_old"));
        Assert.Equal(6, s.Parts.Items.Count);
        Assert.All(s.Parts.Items.Values, p => Assert.Equal(PartRarity.Common, C.PartsConfig.Defs[p.DefId].Rarity));
        Assert.Equal(5, s.Parts.Equipped.Count); // the head was just unequipped by the command after the reset
        Assert.Equal("", s.Parts.Runes[rune.Uid].SocketedIn);
        // a new save starts on v2 (nothing reset)
        var fresh = Player(e);
        Assert.Equal(PartsSystem.ModelVersion, fresh.Parts.Model);
    }

    // ---- sources -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void PartPulls_DrawFromTheRaritysItemSet_TenBannersOfThreeMythics()
    {
        var type = C.GachaConfig.Types[GachaType.Part];
        Assert.Equal(new[] { 58.3, 28, 9.5, 3.0, 1.2 }, type.Tiers.Select(t => t.Weight));
        Assert.Equal((100, 40, true, Rarity.Epic), (type.MythicPity, type.LegendaryPity, type.TenPullGuarantee, type.TenPullMinTier));
        for (int t = 0; t < 5; t++)
        {
            var pool = type.Tiers[t].Pool;
            Assert.Equal(30, pool.Count);
            Assert.All(pool, x => Assert.Equal(RewardKinds.Part, x.Kind));
            Assert.All(pool, x => Assert.Equal(t, (int)C.PartsConfig.Defs[x.Id].Rarity));
            Assert.Single(pool.Select(x => x.Weight).Distinct()); // uniform within the rarity
        }
        var rot = C.GachaConfig.Rotations.Single(r => r.Type == GachaType.Part);
        Assert.Equal(7L * 86_400_000, rot.DurationMs);
        Assert.Equal(10, rot.Themes.Count);
        var all = new HashSet<string>();
        foreach (var th in rot.Themes)
        {
            var feats = th.FeaturedIn(Rarity.Mythic);
            Assert.Equal(3, feats.Count);
            Assert.Equal(0.8, th.FeaturedShare);
            var defs = feats.Select(f => C.PartsConfig.Defs[f.Id]).ToList();
            Assert.Single(defs.Select(d => d.Synergy).Distinct());     // one synergy per banner
            Assert.Equal(3, defs.Select(d => d.Kind).Distinct().Count()); // in 3 different slots
            foreach (var f in feats) Assert.True(all.Add(f.Id));
        }
        Assert.Equal(30, all.Count); // all 30 Mythics at launch
        Assert.Equal(new[] { "Inferno Arsenal", "Glacier Vault", "Storm Works", "Fallout Foundry", "Shadow Forge", "Whirlwind Workshop", "Bulwark Armory",
            "Hive Hangar", "Hourglass Depot", "Proving Grounds" }, rot.Themes.Select(t => t.Name));
    }

    [Fact]
    public void Drops_NoPartsFromRunsOrChests_BossFirstClearsAndDarkSide()
    {
        foreach (var t in C.RunLootTables.Values) Assert.DoesNotContain(t.Entries, x => x.Kind == RewardKinds.Part);
        foreach (var ev in C.RunEvents.Values)
            foreach (var o in ev.Options)
                foreach (var oc in o.Outcomes)
                    foreach (var fx in oc.Effects)
                        Assert.False(fx.Reward != null && fx.Reward.Kind == RewardKinds.Part, ev.Id);
        var main = C.RunLevels.Values.Where(l => C.Portals[l.PortalId].Kind == PortalKind.Main).ToList();
        Assert.NotEmpty(main);
        Assert.All(main, l => Assert.True(l.FirstClearPart, l.Id));
        var dark = C.RunLevels.Values.Where(l => C.Portals.TryGetValue(l.PortalId, out var p) && p.Kind == PortalKind.DarkSide).ToList();
        Assert.NotEmpty(dark);
        Assert.All(dark, l => Assert.Contains(l.RareDrops, d => d.Reward.Kind == RewardKinds.Part && d.Reward.Rarity == (int)Rarity.Legendary && d.ChancePct == 5));
        Assert.Equal(25, C.Campaign.FirstClearEpicPartPct);
    }

    [Fact]
    public void Granter_WildcardPart_RollsTheRaritysItemSet()
    {
        var e = Engine(); var s = Player(e);
        int before = s.Parts.Items.Count;
        for (int i = 0; i < 20; i++) Grant(e, s, new Reward(RewardKinds.Part, "*", 1, (int)Rarity.Epic));
        var got = s.Parts.Items.Values.Skip(before).ToList();
        Assert.Equal(20, got.Count);
        Assert.All(got, p => Assert.Equal(PartRarity.Epic, C.PartsConfig.Defs[p.DefId].Rarity));
        Assert.True(got.Select(p => p.DefId).Distinct().Count() > 5);
        Grant(e, s, new Reward(RewardKinds.Part, "*", 1, (int)Rarity.Mythic));
        Assert.Contains(s.Parts.Items.Values, p => C.PartsConfig.Defs[p.DefId].IsMythic && p.Rarity == (int)PartRarity.Mythic);
    }
}
