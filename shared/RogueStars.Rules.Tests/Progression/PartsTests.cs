using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Progression.ProgressionKit;

namespace RogueStars.Rules.Tests.Progression;

public class PartsTests
{
    private const string Arm = "arm_plasma_blaster";
    private const string Arm2 = "arm_tesla_whip";
    private const string Head = "head_scanner_visor";
    private const string Head2 = "head_ember_crown";

    private static PartItem Add(GameState s, string def, PartRarity r) => PartsSystem.AddPart(s, C, def, (int)r);

    /// <summary>Materials for a merge to step t on the given target, per content recipe.</summary>
    private static List<string> MaterialsFor(GameState s, PartItem target, PartRarity t)
    {
        var recipe = C.PartsConfig.Rarities[(int)t].Merge!;
        var mats = new List<string>();
        var otherDef = target.DefId == Head ? Head2 : Arm2;
        for (int i = 0; i < recipe.SameDefCount; i++) mats.Add(Add(s, target.DefId, recipe.SameDefRarity).Uid);
        for (int i = 0; i < recipe.FodderCount; i++) mats.Add(Add(s, otherDef, recipe.FodderRarity).Uid);
        return mats;
    }

    [Fact]
    public void Ladder_Has15Steps_InSpecOrder()
    {
        var names = C.PartsConfig.Rarities.Select(r => r.Name).ToArray();
        Assert.Equal(new[] { "Common", "Uncommon", "Rare", "Rare+", "Epic", "Epic+1", "Epic+2", "Legendary", "Legendary+1", "Legendary+2", "Legendary+3", "Mythic", "Mythic+1", "Mythic+2", "Mythic+3" }, names);
        for (int i = 1; i < 15; i++) Assert.NotNull(C.PartsConfig.Rarities[i].Merge);
    }

    [Fact]
    public void Merge_EveryStepOfTheLadder_CommonToMythic3()
    {
        var e = Engine(); var s = Player(e);
        var target = Add(s, Arm, PartRarity.Common);
        for (int t = 1; t < 15; t++)
        {
            var mats = MaterialsFor(s, target, (PartRarity)t);
            var r = Ok(e, ref s, new PartMerge { TargetUid = target.Uid, MaterialUids = mats });
            Assert.Equal(t, (int)r.Payload);
            target = s.Parts.Items[target.Uid];
            Assert.Equal(t, target.Rarity);
            foreach (var m in mats) Assert.False(s.Parts.Items.ContainsKey(m));
        }
        Assert.Contains("max rarity", Fail(e, ref s, new PartMerge { TargetUid = target.Uid, MaterialUids = new List<string>() }));
        Assert.Equal(14, s.Counter("part_rarity_max"));
    }

    [Fact]
    public void Merge_RejectsWrongMaterials()
    {
        var e = Engine(); var s = Player(e);
        var t = Add(s, Arm, PartRarity.Common);
        var wrongRarity = Add(s, Arm, PartRarity.Uncommon);
        var other = Add(s, Arm2, PartRarity.Common);   // Common->Uncommon needs same def
        var ok = Add(s, Arm, PartRarity.Common);
        Fail(e, ref s, new PartMerge { TargetUid = t.Uid, MaterialUids = new List<string> { ok.Uid } }); // too few
        Fail(e, ref s, new PartMerge { TargetUid = t.Uid, MaterialUids = new List<string> { ok.Uid, wrongRarity.Uid } });
        Fail(e, ref s, new PartMerge { TargetUid = t.Uid, MaterialUids = new List<string> { ok.Uid, other.Uid } });
        Fail(e, ref s, new PartMerge { TargetUid = t.Uid, MaterialUids = new List<string> { ok.Uid, ok.Uid } });
        var ok2 = Add(s, Arm, PartRarity.Common);
        Ok(e, ref s, new PartEquip { Uid = ok2.Uid, Slot = PartSlot.LeftArm });
        Assert.Contains("equipped", Fail(e, ref s, new PartMerge { TargetUid = t.Uid, MaterialUids = new List<string> { ok.Uid, ok2.Uid } }));
        // A head can never be fodder for an arm.
        var t2 = Add(s, Arm, PartRarity.Rare);
        var same = Add(s, Arm, PartRarity.Rare);
        var head = Add(s, Head, PartRarity.Rare);
        Fail(e, ref s, new PartMerge { TargetUid = t2.Uid, MaterialUids = new List<string> { same.Uid, head.Uid } });
        var armFodder = Add(s, Arm2, PartRarity.Rare);
        Ok(e, ref s, new PartMerge { TargetUid = t2.Uid, MaterialUids = new List<string> { same.Uid, armFodder.Uid } });
    }

    [Fact]
    public void Merge_KeepsTargetLevelAndRunes_RefundsMaterialLevels()
    {
        var e = Engine(); var s = Player(e);
        SetBalance(s, Currencies.Ore, 1_000_000);
        var t = Add(s, Arm, PartRarity.Common);
        var m1 = Add(s, Arm, PartRarity.Common);
        var m2 = Add(s, Arm, PartRarity.Common);
        Ok(e, ref s, new PartLevelUp { Uid = t.Uid, Levels = 5 });
        Ok(e, ref s, new PartLevelUp { Uid = m1.Uid, Levels = 4 });
        long oreBefore = s.Wallet.Get(Currencies.Ore);
        long m1Spent = s.Parts.Items[m1.Uid].SpentOre;
        Ok(e, ref s, new PartMerge { TargetUid = t.Uid, MaterialUids = new List<string> { m1.Uid, m2.Uid } });
        Assert.Equal(oreBefore + m1Spent, s.Wallet.Get(Currencies.Ore));
        Assert.Equal(6, s.Parts.Items[t.Uid].Level);
    }

    [Fact]
    public void QuickMerge_NineCommonsBecomeOneRare_AndRespectsCap()
    {
        var e = Engine(); var s = Player(e);
        for (int i = 0; i < 9; i++) Add(s, Head, PartRarity.Common);
        var r = Ok(e, ref s, new PartQuickMerge());
        Assert.Equal(4, (int)r.Payload);
        Assert.Single(s.Parts.Items);
        Assert.Equal((int)PartRarity.Rare, s.Parts.Items.Values.First().Rarity);

        // Never above QuickMergeMaxTarget.
        var e2 = Engine(); var s2 = Player(e2);
        for (int i = 0; i < 3; i++) Add(s2, Head, PartRarity.Epic2);
        Ok(e2, ref s2, new PartQuickMerge());
        Assert.Equal((int)PartRarity.Legendary, s2.Parts.Items.Values.Max(p => p.Rarity));
        for (int i = 0; i < 3; i++) Add(s2, Head, PartRarity.Legendary);
        Assert.Equal(0, (int)Ok(e2, ref s2, new PartQuickMerge()).Payload);
    }

    [Fact]
    public void LevelUp_RespectsCap_RaiseCapWithPlates_LevelDownRefunds100Percent()
    {
        var e = Engine(); var s = Player(e);
        SetBalance(s, Currencies.Ore, 10_000_000); SetBalance(s, Currencies.GearCores, 100_000); SetBalance(s, Currencies.AdvancementPlates, 1000);
        var p = Add(s, Head, PartRarity.Rare);
        int cap = PartsSystem.LevelCap(C, p);
        Assert.Equal(20, cap);
        Ok(e, ref s, new PartLevelUp { Uid = p.Uid, Levels = cap - 1 });
        Assert.Contains("cap", Fail(e, ref s, new PartLevelUp { Uid = p.Uid }));
        Ok(e, ref s, new PartRaiseCap { Uid = p.Uid });
        Assert.Equal(25, PartsSystem.LevelCap(C, s.Parts.Items[p.Uid]));
        Assert.Contains("cap raises", Fail(e, ref s, new PartRaiseCap { Uid = p.Uid })); // Rare allows one raise
        Ok(e, ref s, new PartLevelUp { Uid = p.Uid, Levels = 5 });
        Assert.True(s.Wallet.Get(Currencies.GearCores) < 100_000, "levels >= 20 cost gear cores");

        Ok(e, ref s, new PartLevelDown { Uid = p.Uid });
        Assert.Equal(1, s.Parts.Items[p.Uid].Level);
        Assert.Equal(10_000_000, s.Wallet.Get(Currencies.Ore));
        Assert.Equal(100_000, s.Wallet.Get(Currencies.GearCores));
    }

    [Fact]
    public void Dismantle_RefundsAllMaterials_PlusDismantleOre_AndFreesRunes()
    {
        var e = Engine(); var s = Player(e);
        SetBalance(s, Currencies.Ore, 1_000_000); SetBalance(s, Currencies.GearCores, 0); SetBalance(s, Currencies.AdvancementPlates, 100);
        var p = Add(s, Head, PartRarity.RarePlus);
        var rune = PartsSystem.AddRune(s, C, "rune_vigor", 0);
        Ok(e, ref s, new PartLevelUp { Uid = p.Uid, Levels = 10 });
        Ok(e, ref s, new PartRaiseCap { Uid = p.Uid });
        Ok(e, ref s, new RuneSocket { RuneUid = rune.Uid, PartUid = p.Uid, SlotIndex = 0 });
        Ok(e, ref s, new PartEquip { Uid = p.Uid, Slot = PartSlot.Head });
        Assert.Contains("unequip", Fail(e, ref s, new PartDismantle { Uids = new List<string> { p.Uid } }));
        Ok(e, ref s, new PartUnequip { Slot = PartSlot.Head });
        Ok(e, ref s, new PartDismantle { Uids = new List<string> { p.Uid } });
        Assert.Equal(1_000_000 + C.PartsConfig.Rarities[(int)PartRarity.RarePlus].DismantleOre, s.Wallet.Get(Currencies.Ore));
        Assert.Equal(100, s.Wallet.Get(Currencies.AdvancementPlates));
        Assert.Equal("", s.Parts.Runes[rune.Uid].SocketedIn);
        Assert.Empty(s.Parts.Items);
    }

    [Fact]
    public void Equip_ArmsFitEitherArm_OthersOnlyTheirSlot()
    {
        var e = Engine(); var s = Player(e);
        var a1 = Add(s, Arm, PartRarity.Common); var a2 = Add(s, "arm_rail_cannon", PartRarity.Common); var h = Add(s, Head, PartRarity.Common);
        Ok(e, ref s, new PartEquip { Uid = a1.Uid, Slot = PartSlot.LeftArm });
        Ok(e, ref s, new PartEquip { Uid = a2.Uid, Slot = PartSlot.RightArm });
        Fail(e, ref s, new PartEquip { Uid = h.Uid, Slot = PartSlot.LeftArm });
        Fail(e, ref s, new PartEquip { Uid = a1.Uid, Slot = PartSlot.Head });
        Ok(e, ref s, new PartEquip { Uid = h.Uid, Slot = PartSlot.Head });
        var l = e.BuildLoadout(s, TestHelpers.T0);
        Assert.Equal(new[] { AttackStyle.Blaster, AttackStyle.Cannon }, l.ArmStyles);
        // Moving an arm part to the other arm empties the first.
        Ok(e, ref s, new PartEquip { Uid = a1.Uid, Slot = PartSlot.RightArm });
        Assert.False(s.Parts.Equipped.ContainsKey(PartSlot.LeftArm));
    }

    [Fact]
    public void Runes_Merge3To1_LockAndSocketRules()
    {
        var e = Engine(); var s = Player(e);
        var r = Enumerable.Range(0, 4).Select(_ => PartsSystem.AddRune(s, C, "rune_fury", 1)).ToList();
        var other = PartsSystem.AddRune(s, C, "rune_vigor", 1);
        Fail(e, ref s, new RuneMerge { RuneUids = new List<string> { r[0].Uid, r[1].Uid, other.Uid } });
        Ok(e, ref s, new RuneLock { RuneUid = r[0].Uid, Locked = true });
        Assert.Contains("locked", Fail(e, ref s, new RuneMerge { RuneUids = new List<string> { r[0].Uid, r[1].Uid, r[2].Uid } }));
        var res = Ok(e, ref s, new RuneMerge { RuneUids = new List<string> { r[1].Uid, r[2].Uid, r[3].Uid } });
        var merged = s.Parts.Runes[(string)res.Payload];
        Assert.Equal(2, merged.Rarity);
        Assert.Equal(3, s.Parts.Runes.Count); // r0, other, merged

        var mythic = PartsSystem.AddRune(s, C, "rune_fury", (int)Rarity.Mythic);
        var m2 = PartsSystem.AddRune(s, C, "rune_fury", (int)Rarity.Mythic);
        var m3 = PartsSystem.AddRune(s, C, "rune_fury", (int)Rarity.Mythic);
        Assert.Contains("max", Fail(e, ref s, new RuneMerge { RuneUids = new List<string> { mythic.Uid, m2.Uid, m3.Uid } }));

        // Sockets: Common part has 0 slots; Rare+ has 1; no duplicate rune type on one part.
        var common = Add(s, Head, PartRarity.Common);
        Assert.Contains("locked", Fail(e, ref s, new RuneSocket { RuneUid = merged.Uid, PartUid = common.Uid, SlotIndex = 0 }));
        var leg = Add(s, Head, PartRarity.Legendary);
        Ok(e, ref s, new RuneSocket { RuneUid = merged.Uid, PartUid = leg.Uid, SlotIndex = 0 });
        Assert.Contains("type", Fail(e, ref s, new RuneSocket { RuneUid = r[0].Uid, PartUid = leg.Uid, SlotIndex = 1 }));
        Ok(e, ref s, new RuneSocket { RuneUid = other.Uid, PartUid = leg.Uid, SlotIndex = 1 });
        Assert.Contains("unsocket", Fail(e, ref s, new RuneMerge { RuneUids = new List<string> { merged.Uid, r[0].Uid, other.Uid } }));
        // Rune stat applies: +ATK% from the fury rune.
        var stats = PartsSystem.PartStats(s, C, s.Parts.Items[leg.Uid]);
        Assert.Equal(2 * C.PartsConfig.RuneRarityMultipliers[2], stats.Percent[StatId.Atk], 6);
        Ok(e, ref s, new RuneUnsocket { PartUid = leg.Uid, SlotIndex = 0 });
        Assert.Equal("", s.Parts.Runes[merged.Uid].SocketedIn);
    }

    [Fact]
    public void Forge_AddsRuneSlot_AndPercentToBaseStatsOnly()
    {
        var e = Engine(); var s = Player(e);
        SetBalance(s, Currencies.Diamonds, 10_000); SetBalance(s, Currencies.GearCores, 10_000); SetBalance(s, Currencies.Ore, 1_000_000);
        var low = Add(s, Head, PartRarity.RarePlus);
        Assert.Contains("rarity", Fail(e, ref s, new PartForgeUnlock { Uid = low.Uid }));
        var p = Add(s, Head, PartRarity.Epic);
        Ok(e, ref s, new PartLevelUp { Uid = p.Uid, Levels = 10 });
        var cfg = C.PartsConfig;
        double mult = cfg.Rarities[(int)PartRarity.Epic].StatMultiplier;
        double before = PartsSystem.PartStats(s, C, s.Parts.Items[p.Uid]).Value(StatId.Hp);
        Assert.Equal(60 * mult + 8 * 10 * mult, before, 6);
        Assert.Equal(1, PartsSystem.RuneSlotCount(C, s.Parts.Items[p.Uid]));
        Ok(e, ref s, new PartForgeUnlock { Uid = p.Uid });
        Assert.Equal(2, PartsSystem.RuneSlotCount(C, s.Parts.Items[p.Uid]));
        double after = PartsSystem.PartStats(s, C, s.Parts.Items[p.Uid]).Value(StatId.Hp);
        Assert.Equal(60 * mult * (1 + cfg.Forge[0].BonusPercent / 100) + 8 * 10 * mult, after, 6); // level part unchanged
        Assert.Equal(10_000 - cfg.Forge[0].Diamonds, s.Wallet.Get(Currencies.Diamonds));
        Ok(e, ref s, new PartForgeUnlock { Uid = p.Uid });
        Assert.Equal(3, PartsSystem.RuneSlotCount(C, s.Parts.Items[p.Uid]));
        Fail(e, ref s, new PartForgeUnlock { Uid = p.Uid });
        // Mythic (3 slots) + 2 forges is capped at 4 slots.
        var m = Add(s, Head2, PartRarity.Mythic); m.ForgedSlots = 2;
        Assert.Equal(4, PartsSystem.RuneSlotCount(C, m));
    }

    [Fact]
    public void Awakening_SAtLegendary_SSAtMythic_AddsSignatureSkillsAndStats()
    {
        var e = Engine(); var s = Player(e);
        SetBalance(s, ProgressionCurrencies.PartAwakenCores, 1000);
        var epic = Add(s, Arm, PartRarity.Epic2);
        Assert.Contains("rarity", Fail(e, ref s, new PartAwaken { Uid = epic.Uid }));
        var p = Add(s, Arm, PartRarity.Legendary);
        double atk0 = PartsSystem.PartStats(s, C, p).Value(StatId.Atk);
        Ok(e, ref s, new PartAwaken { Uid = p.Uid });
        p = s.Parts.Items[p.Uid];
        Assert.Equal(1, p.Awaken);
        Assert.Equal(atk0 * 1.15, PartsSystem.PartStats(s, C, p).Value(StatId.Atk), 6);
        Assert.Equal(new[] { Arm + "_s" }, PartsSystem.PartSkills(C, p));
        Assert.Contains("rarity", Fail(e, ref s, new PartAwaken { Uid = p.Uid })); // SS needs Mythic
        p.Rarity = (int)PartRarity.Mythic;
        Ok(e, ref s, new PartAwaken { Uid = p.Uid });
        p = s.Parts.Items[p.Uid];
        Assert.Equal(new[] { Arm + "_sig", Arm + "_ss" }, PartsSystem.PartSkills(C, p));
        Assert.Contains("fully", Fail(e, ref s, new PartAwaken { Uid = p.Uid }));
        Assert.Equal(1000 - 20 - 60, s.Wallet.Get(ProgressionCurrencies.PartAwakenCores));
        var sk = C.Skill(Arm + "_ss")!;
        Assert.Equal(SkillSource.Part, sk.Source);
        Assert.False(sk.Draftable);
        p.Rarity = (int)PartRarity.Mythic3;
        Assert.Contains(Arm + "_sig2", PartsSystem.PartSkills(C, p));
        Assert.Contains("awakened", Fail(e, ref s, new PartDismantle { Uids = new List<string> { p.Uid } }));
    }

    [Fact]
    public void Content_HasEnoughPartsAndRunes()
    {
        var defs = C.PartsConfig.Defs.Values.ToList();
        Assert.True(defs.Count >= 30);
        foreach (PartKind k in Enum.GetValues(typeof(PartKind))) Assert.True(defs.Count(d => d.Kind == k) >= 4);
        Assert.True(defs.Where(d => d.Kind == PartKind.Arm).Select(d => d.Style).Distinct().Count() >= 5);
        Assert.True(C.PartsConfig.Runes.Count >= 12);
        foreach (var d in defs) foreach (var id in new[] { d.SignatureSkillId, d.Signature2SkillId, d.AwakenSSkillId, d.AwakenSSSkillId }) Assert.NotNull(C.Skill(id));
    }

    [Fact]
    public void Granters_PartAndRune()
    {
        var e = Engine(); var s = Player(e);
        Grant(e, s, new Reward(RewardKinds.Part, Head, 2, (int)PartRarity.Epic));
        Grant(e, s, new Reward(RewardKinds.Rune, "rune_mist", 1, 3));
        Assert.Equal(2, s.Parts.Items.Values.Count(p => p.DefId == Head && p.Rarity == (int)PartRarity.Epic));
        Assert.Equal(3, s.Parts.Runes.Values.Single().Rarity);
        Assert.Equal(2, s.Counter("parts_obtained"));
    }
}
