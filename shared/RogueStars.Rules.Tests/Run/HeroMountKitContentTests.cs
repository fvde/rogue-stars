using System.Text.RegularExpressions;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Hero and mount kits (owner-approved Hero and Mount Mechanics page, rev 2): the 9 hero and 9 mount kits as data. Every kit
/// element at every step has the page's numbers, the page's text (at most 20 words, no decimals, different at every step).
/// </summary>
public class HeroMountKitContentTests
{
    private static readonly ContentDb C = ContentDb.Default;

    public static readonly string[] Heroes =
    {
        "hero_star_knight", "hero_chef", "hero_void_pirate", "hero_noodle_ninja", "hero_alchemist", "hero_sparkplug", "hero_beast_tamer",
        "hero_glacier_monk", "hero_hourglass_hermit",
    };

    public static readonly string[] Mounts =
    {
        "mount_scout_saucer", "mount_magma_barge", "mount_toxic_hauler", "mount_star_lancer", "mount_frost_frigate", "mount_rift_racer",
        "mount_aegis_walker", "mount_hive_carrier", "mount_chrono_skiff",
    };

    [Fact]
    public void NineHeroes_NineMounts_OldIdsKept_NamesFromThePage()
    {
        Assert.Equal(Heroes.OrderBy(x => x, StringComparer.Ordinal), C.HeroConfig.Defs.Keys.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(Mounts.OrderBy(x => x, StringComparer.Ordinal), C.MountConfig.Defs.Keys.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal("Prism Alchemist Worm", C.HeroConfig.Defs["hero_alchemist"].Name);  // was Toxic / Atomic Alchemist
        Assert.Equal("Nebula Monk Worm", C.HeroConfig.Defs["hero_glacier_monk"].Name);   // was Glacier Monk
        Assert.Equal("Siphon Hauler", C.MountConfig.Defs["mount_toxic_hauler"].Name);    // was Toxic / Reactor Hauler
        Assert.Equal("Hourglass Hermit Worm", C.HeroConfig.Defs["hero_hourglass_hermit"].Name);
        // not tied to a synergy: no family on any hero or mount, every kit skill is Family None and not draftable
        Assert.All(C.HeroConfig.Defs.Values, d => Assert.Equal(SkillFamily.None, d.Family));
        Assert.All(C.MountConfig.Defs.Values, d => Assert.Equal(SkillFamily.None, d.Family));
        foreach (var s in KitSkills())
        {
            Assert.Equal(SkillFamily.None, s.Family);
            Assert.False(s.Draftable);
            Assert.Empty(s.Stats); // no stat sticks: every element is a mechanic
        }
        // the old ProgFamilySkill steps are gone
        Assert.DoesNotContain(C.Skills.Keys, id => id.StartsWith("hero_", StringComparison.Ordinal) && Regex.IsMatch(id, "_(a1|a2|a3|b1|b2)$"));
        Assert.DoesNotContain(C.Skills.Keys, id => id.StartsWith("mount_", StringComparison.Ordinal) && Regex.IsMatch(id, "_(w1|w2|w3|w4|s1|s2)$"));
    }

    internal static IEnumerable<SkillDef> KitSkills() =>
        C.HeroConfig.Defs.Values.SelectMany(d => d.Traits).Concat(C.MountConfig.Defs.Values.SelectMany(d => d.Weapons)).Select(st => C.Skill(st.SkillId)!);

    private static readonly Regex Decimal = new(@"\d[.,]\d");

    /// <summary>Page rules: every step text at most 20 words, no decimals; every shown text reads differently per step.</summary>
    [Fact]
    public void KitTexts_AreShort_Whole_AndDifferAtEveryStep()
    {
        var bad = new List<string>();
        void Check(string where, string text)
        {
            if (SkillText.WordCount(text) > 20) bad.Add(where + " (" + SkillText.WordCount(text) + " words): " + text);
            if (Decimal.IsMatch(text)) bad.Add(where + " has a decimal: " + text);
        }
        foreach (var d in C.HeroConfig.Defs.Values)
        {
            Assert.Equal(new[] { "Trait", "Ability", "Number", "Clause", "Clause", "Capstone" }, d.Kit.Select(k => k.Slot));
            Assert.Equal(new[] { 1, 1, 3, 5, 7, 10 }, d.Kit.Select(k => k.Star));
            Assert.Equal(d.Kit.Count, d.Kit.Select(k => k.Text).Distinct().Count());
            foreach (var k in d.Kit) Check(d.Id + " " + k.StepLabel + " " + k.Name, k.Text);
            Check(d.Id + " tagline", d.Tagline.Substring(d.Tagline.IndexOf(':') + 1)); // the line under the name
        }
        foreach (var d in C.MountConfig.Defs.Values)
        {
            foreach (var slot in new[] { "Passive", "Active" })
            {
                var lines = d.Kit.Where(k => k.Slot == slot).ToList();
                Assert.Equal(new[] { "1★", "3★", "5★", "A3", "A5" }, lines.Select(k => k.StepLabel));
                Assert.Single(lines.Select(k => k.Name).Distinct());
                for (int i = 1; i < lines.Count; i++) Assert.NotEqual(lines[i - 1].Text, lines[i].Text);
            }
            foreach (var k in d.Kit) Check(d.Id + " " + k.Slot + " " + k.StepLabel, k.Text);
        }
        foreach (var s in KitSkills()) Check(s.Id, SkillText.Full(s));
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    /// <summary>Numbers are whole everywhere in the kits (values, stacks, chances, rule arguments).</summary>
    [Fact]
    public void KitNumbers_AreWhole()
    {
        foreach (var s in KitSkills())
            foreach (var t in s.Triggers)
            {
                Assert.Equal(Math.Round(t.Value), t.Value);
                Assert.Equal(Math.Round(t.Chance), t.Chance);
                foreach (Match m in Regex.Matches(t.Param ?? "", @"[\d.]+")) Assert.DoesNotContain(".", m.Value);
                if (t.Stat != null) { Assert.Equal(Math.Round(t.Stat.Flat), t.Stat.Flat); Assert.Equal(Math.Round(t.Stat.Percent), t.Stat.Percent); }
            }
    }

    // ---- helpers to read the numbers -----------------------------------------------------------------------------------

    private static SkillDef S(string id) => C.Skill(id) ?? throw new Xunit.Sdk.XunitException("no skill " + id);
    private static SkillTrigger T(string id, EffectKind e, int nth = 0) => S(id).Triggers.Where(t => t.Effect == e).ElementAt(nth);
    private static double Rule(string id, string name) => S(id).Triggers.First(t => t.Effect == EffectKind.Rule && (t.Param == name || (t.Param ?? "").StartsWith(name + ":"))).Value;
    private static int RuleArg(string id, string name) =>
        int.Parse(S(id).Triggers.First(t => t.Effect == EffectKind.Rule && (t.Param ?? "").StartsWith(name + ":")).Param!.Split(':')[1]);
    private static bool HasRule(string id, string name) => S(id).Triggers.Any(t => t.Effect == EffectKind.Rule && ((t.Param ?? "") == name || (t.Param ?? "").StartsWith(name + ":")));
    private static double P(string id, EffectKind e, string token, int nth = 0) => BattleSimTokens(T(id, e, nth).Param, token);
    private static double BattleSimTokens(string? p, string token)
    {
        foreach (var raw in (p ?? "").Split(';')) { var a = raw.Split(':'); if (a.Length == 2 && a[0] == token) return double.Parse(a[1]); }
        return 0;
    }

    /// <summary>The mount's skill of a slot at each kit step (1★, 3★, 5★, A3, A5).</summary>
    private static string[] Steps(string mount, string slot) => Enumerable.Range(1, 5).Select(k => mount + "_" + slot + "_" + k).ToArray();
    private static double[] Each(string[] ids, Func<string, double> f) => ids.Select(f).ToArray();

    // ---- heroes: every element at every star step ------------------------------------------------------------------------

    [Fact]
    public void StarKnight_Starguard_Starfall_Starlight_GuidingStar_LastConstellation()
    {
        const string h = "hero_star_knight";
        Assert.Equal(12, T(h + "_trait", EffectKind.Shield).Value);           // battle start shield 12% max HP
        Assert.Equal(TriggerKind.OnShieldHit, T(h + "_trait", EffectKind.GainRage).When);
        Assert.Equal(5, T(h + "_trait", EffectKind.GainRage).Value);          // 5 rage per absorbed hit
        Assert.Equal(4, RuleArg(h + "_ability", "ability_shield_hits"));      // every 4th absorbed hit
        Assert.Equal(60, T(h + "_ability", EffectKind.Damage).Value);         // 1★ 60% ATK to every enemy
        Assert.Equal(TargetKind.AllEnemies, T(h + "_ability", EffectKind.Damage).Target);
        Assert.Equal(6, T(h + "_ability", EffectKind.Shield).Value);          // shields you 6%
        Assert.Equal(90, T(h + "_ability_3", EffectKind.Damage).Value);       // 3★ 90%
        Assert.Equal(TargetKind.AllEnemies, T(h + "_c5", EffectKind.CarrySignatures).Target); // 5★ carries on-hit to every enemy hit
        Assert.Equal(10, RuleArg(h + "_c7", "heals_count"));                  // 7★ every 10% max HP healed = 1 hit
        var c10 = T(h + "_c10", EffectKind.CheatDeath);                     // 10★ once per battle, Starfall at double damage
        Assert.Equal(1, c10.MaxPerBattle);
        Assert.Equal(200, BattleSimTokens(c10.Param, "ability"));
    }

    [Fact]
    public void Chef_TasteTest_Feast_Seasoning_SecondCourse_Banquet()
    {
        const string h = "hero_chef";
        var taste = T(h + "_trait", EffectKind.Heal);
        Assert.Equal(TriggerKind.OnStatusLost, taste.When);
        Assert.Equal(1, taste.Value);                                       // 1% max HP per stack lost
        Assert.Equal(8, BattleSimTokens(taste.Param, "round_cap"));         // max 8% per round
        Assert.Equal(4, RuleArg(h + "_ability", "ability_every"));          // every 4th round
        Assert.Equal(6, P(h + "_ability", EffectKind.ConsumeStatus, "take")); // up to 6 stacks
        Assert.Equal(25, P(h + "_ability", EffectKind.ConsumeStatus, "dmg")); // 25% ATK per stack
        Assert.Equal(3, RuleArg(h + "_ability_3", "ability_every"));        // 3★ every 3rd round
        Assert.Equal(3, T(h + "_c5", EffectKind.CarrySignatures).Stacks);    // 5★ next hit ×3
        Assert.Equal(TargetKind.SecondEnemy, T(h + "_c7", EffectKind.ConsumeStatus).Target);
        Assert.Equal(3, P(h + "_c7", EffectKind.ConsumeStatus, "take"));     // 7★ up to 3 from the enemy behind
        Assert.Equal(50, P(h + "_ability_10", EffectKind.ConsumeStatus, "splash")); // 10★ Banquet: every enemy takes half
        Assert.Equal(6, P(h + "_ability_10", EffectKind.ConsumeStatus, "take"));
    }

    [Fact]
    public void VoidPirate_DeadReckoning_Broadside_BlackFlag_Plunder_VoidGalleon()
    {
        const string h = "hero_void_pirate";
        Assert.True(HasRule(h + "_trait", "status_crit"));
        Assert.Equal(4, RuleArg(h + "_ability", "ability_every"));
        Assert.Equal(50, T(h + "_ability", EffectKind.Damage).Value);
        Assert.Contains("crit", T(h + "_ability", EffectKind.Damage).Param!.Split(';'));
        Assert.Equal(75, T(h + "_ability_3", EffectKind.Damage).Value);
        Assert.Equal(TriggerKind.OnCrit, T(h + "_c5", EffectKind.CarrySignatures).When);
        var plunder = T(h + "_c7", EffectKind.GainRunCoins);
        Assert.Equal(2, plunder.Value);
        Assert.Equal(20, plunder.Value * plunder.MaxPerBattle);             // max 20 per battle
        Assert.Equal(TriggerKind.OnBattleStart, T(h + "_c10", EffectKind.FireAbility).When);
    }

    [Fact]
    public void ShadowNoodle_DoubleTap_NoodleKnot_SharpNoodle_PileOn_ThousandNoodles()
    {
        const string h = "hero_noodle_ninja";
        Assert.True(HasRule(h + "_trait", "double_tap"));
        Assert.Equal(3, RuleArg(h + "_ability", "ability_turns"));
        Assert.Equal(new double[] { 30, 30 }, S(h + "_ability").Triggers.Where(t => t.Effect == EffectKind.BuffStat).Select(t => t.Stat!.Flat));
        Assert.Equal(new double[] { 45, 45 }, S(h + "_ability_3").Triggers.Where(t => t.Effect == EffectKind.BuffStat).Select(t => t.Stat!.Flat));
        Assert.Equal(20, Rule(h + "_c5", "extra_crit"));
        Assert.Equal(6, Rule(h + "_c7", "pile_on"));
        Assert.Equal(48, RuleArg(h + "_c7", "pile_on"));
        var fin = T(h + "_c10", EffectKind.Damage);
        Assert.Equal((150, "extra_hits:5", 1), (fin.Value, fin.If, fin.MaxPerBattle));
    }

    [Fact]
    public void PrismAlchemist_Prism_Flask_VolatileFlask_Conduit_DeepWells()
    {
        const string h = "hero_alchemist";
        Assert.Equal((5.0, "cond:status_count"), (S(h + "_trait").Triggers[0].Value, S(h + "_trait").Triggers[0].Param));
        Assert.Equal(3, RuleArg(h + "_ability", "ability_every"));
        Assert.Equal(80, T(h + "_ability", EffectKind.Damage).Value);
        Assert.Equal((2, "owned"), (T(h + "_ability", EffectKind.ApplyStatus).Stacks, T(h + "_ability", EffectKind.ApplyStatus).Param));
        Assert.Equal(3, T(h + "_ability_3", EffectKind.ApplyStatus).Stacks);
        Assert.Equal(TriggerKind.OnAbility, T(h + "_c5", EffectKind.CarrySignatures).When);
        Assert.Equal(50, Rule(h + "_c7", "element_pool"));
        Assert.Equal(new[] { StatusId.Bleed, StatusId.Chill, StatusId.Shock }, S(h + "_c10").Triggers.Select(t => t.Status));
        Assert.All(S(h + "_c10").Triggers, t => Assert.Equal("deep_wells:2", t.Param));
    }

    [Fact]
    public void Sparkplug_Ignition_Overcharge_SparkPlug_ShortCircuit_LiveWire()
    {
        const string h = "hero_sparkplug";
        Assert.Equal(new double[] { 5, 5 }, S(h + "_trait").Triggers.Select(t => t.Value));
        Assert.Equal(new[] { TriggerKind.OnDamaged, TriggerKind.OnExtraHit }, S(h + "_trait").Triggers.Select(t => t.When));
        Assert.All(S(h + "_trait").Triggers, t => Assert.Equal(20, BattleSimTokens(t.Param, "round_cap")));
        Assert.Equal(new double[] { 8, 8 }, S(h + "_trait_3").Triggers.Select(t => t.Value));
        Assert.Equal(100, RuleArg(h + "_ability", "rage_bank"));
        Assert.Equal((TriggerKind.OnRageAttack, TargetKind.AllEnemies), (T(h + "_c5", EffectKind.CarrySignatures).When, T(h + "_c5", EffectKind.CarrySignatures).Target));
        Assert.Equal(TriggerKind.OnRageAttack, T(h + "_c7", EffectKind.MountFire).When);
        Assert.Equal((20.0, "self_hp_below:30"), (T(h + "_c10", EffectKind.GainRage).Value, T(h + "_c10", EffectKind.GainRage).If));
        Assert.Equal((10.0, "self_hp_below:30"), (T(h + "_c10", EffectKind.Heal).Value, T(h + "_c10", EffectKind.Heal).If));
    }

    [Fact]
    public void BeastTamer_PackSpirit_Whistle_Tamed_PackGuard_AlphaHowl()
    {
        const string h = "hero_beast_tamer";
        Assert.Equal(new[] { TriggerKind.OnSidekickAct, TriggerKind.OnMountFire }, S(h + "_trait").Triggers.Select(t => t.When));
        Assert.All(S(h + "_trait").Triggers, t => Assert.Equal((2.0, "stack:15"), (t.Stat!.Percent, t.Param))); // +2% ATK, max +30%
        Assert.Equal(4, RuleArg(h + "_ability", "ability_every"));
        Assert.Equal(3, RuleArg(h + "_ability_3", "ability_every"));
        Assert.Equal(TriggerKind.OnSidekickHit, T(h + "_c5", EffectKind.CarrySignatures).When);
        Assert.Equal(3, T(h + "_ability_7", EffectKind.Whistle).Value);
        Assert.Equal(30, P(h + "_ability_10", EffectKind.Whistle, "follow"));
        Assert.Equal(3, T(h + "_ability_10", EffectKind.Whistle).Value);
    }

    [Fact]
    public void NebulaMonk_Stillness_MirrorPalm_OpenPalm_InnerCalm_NebulaMind()
    {
        const string h = "hero_glacier_monk";
        Assert.Equal(3, RuleArg(h + "_trait", "ability_stillness"));
        Assert.Equal(50, T(h + "_ability", EffectKind.SpreadStatus).Value);  // half its largest status
        Assert.Equal(20, P(h + "_ability", EffectKind.SpreadStatus, "cap"));
        Assert.Equal(30, P(h + "_ability_3", EffectKind.SpreadStatus, "cap"));
        Assert.Equal(TargetKind.AllEnemies, T(h + "_c5", EffectKind.CarrySignatures).Target);
        Assert.Equal(10, T(h + "_c7", EffectKind.Heal).Value);
        Assert.Equal((8.0, 200.0), (P(h + "_ability_10", EffectKind.SpreadStatus, "dmg_per"), P(h + "_ability_10", EffectKind.SpreadStatus, "dmg_cap")));
    }

    [Fact]
    public void HourglassHermit_Patience_Rewind_TurningSand_BorrowedSand_HourOfNeed()
    {
        const string h = "hero_hourglass_hermit";
        var pat = T(h + "_trait", EffectKind.AdvanceTimer);
        Assert.Equal(("ability;mount", 1), (pat.Param, pat.CooldownRounds)); // once per round
        Assert.Equal(4, RuleArg(h + "_ability", "ability_every"));
        Assert.Equal(2, T(h + "_ability", EffectKind.Rewind).Stacks);
        Assert.Equal(3, T(h + "_ability_3", EffectKind.Rewind).Stacks);
        Assert.Equal(2, T(h + "_c5", EffectKind.CarrySignatures).Stacks);
        Assert.Equal(("undo", 1), (T(h + "_c7", EffectKind.CheatDeath).Param, T(h + "_c7", EffectKind.CheatDeath).MaxPerBattle));
        Assert.Equal(50, P(h + "_ability_10", EffectKind.Rewind, "reflect"));
    }

    // ---- mounts: the passive and the active at 1★ / 3★ / 5★ / A3 / A5 ----------------------------------------------------

    [Fact]
    public void Mounts_ReloadPerStep()
    {
        foreach (var id in Mounts.Where(m => m != "mount_chrono_skiff"))
            Assert.Equal(new[] { 3, 3, 3, 2, 2 }, C.MountConfig.Defs[id].Reloads);
        Assert.Equal(new[] { 4, 4, 3, 3, 3 }, C.MountConfig.Defs["mount_chrono_skiff"].Reloads); // 3 from 5★
    }

    [Fact]
    public void ScoutSaucer_Spotlight_ScanPulse()
    {
        var p = Steps("mount_scout_saucer", "passive"); var a = Steps("mount_scout_saucer", "active");
        Assert.Equal(new double[] { 10, 15, 20, 25, 30 }, Each(p, id => Rule(id, "spotlight")));
        Assert.Equal(new double[] { 0, 0, 50, 50, 100 }, Each(p, id => HasRule(id, "contagion") ? Rule(id, "contagion") : 0));
        Assert.Equal(new double[] { 80, 100, 120, 140, 160 }, Each(a, id => T(id, EffectKind.Damage).Value));
        Assert.Equal(new double[] { 1, 2, 3, 4, 5 }, Each(a, id => T(id, EffectKind.ApplyStatus).Stacks));
        Assert.Equal(new[] { false, false, true, true, true }, a.Select(id => (T(id, EffectKind.Damage).Param ?? "") == "crit"));
        Assert.Equal(40, T(a[4], EffectKind.Damage, 1).Value);               // A5: others take 40%
        Assert.Equal(TargetKind.AllOtherEnemies, T(a[4], EffectKind.Damage, 1).Target);
    }

    [Fact]
    public void MagmaBarge_PressureValve_Eruption()
    {
        var p = Steps("mount_magma_barge", "passive"); var a = Steps("mount_magma_barge", "active");
        Assert.All(p, id => Assert.Equal((10, true, 30.0), (T(id, EffectKind.GainRage).EveryN, T(id, EffectKind.GainRage).Rider, P(id, EffectKind.GainRage, "round_cap"))));
        Assert.Equal(new double[] { 10, 15, 20, 25, 30 }, Each(p, id => T(id, EffectKind.GainRage).Value));
        Assert.Equal(new double[] { 0, 0, 10, 15, 20 }, Each(p, id => S(id).Triggers.Count(t => t.Effect == EffectKind.GainRage) > 1 ? T(id, EffectKind.GainRage, 1).Value : 0));
        Assert.Equal(new double[] { 120, 140, 160, 180, 200 }, Each(a, id => T(id, EffectKind.Damage).Value));
        Assert.Equal(new[] { "", "", "worm_rage:70", "worm_rage:60", "worm_rage:60" }, a.Select(id => S(id).Triggers.FirstOrDefault(t => t.Effect == EffectKind.GainRage)?.If ?? ""));
        Assert.Equal(new[] { false, false, false, false, true }, a.Select(id => S(id).Triggers.Any(t => t.Effect == EffectKind.FreeRageAttack && t.If == "worm_rage:100")));
    }

    [Fact]
    public void SiphonHauler_SiphonLine_DrainHose()
    {
        var p = Steps("mount_toxic_hauler", "passive"); var a = Steps("mount_toxic_hauler", "active");
        Assert.Equal(new double[] { 5, 8, 10, 12, 15 }, Each(p, id => T(id, EffectKind.Heal).Value));
        Assert.All(p, id => Assert.Equal(TriggerKind.OnStatusDamage, T(id, EffectKind.Heal).When));
        Assert.Equal(new double[] { 0, 0, 10, 15, 20 }, Each(p, id => HasRule(id, "siphon_cap") ? Rule(id, "siphon_cap") : 0));
        Assert.Equal(new double[] { 60, 80, 100, 120, 140 }, Each(a, id => T(id, EffectKind.Damage).Value));
        Assert.Equal(new double[] { 100, 100, 150, 175, 200 }, Each(a, id => T(id, EffectKind.TickStatus).Value));
        Assert.All(a, id => Assert.Equal(new[] { StatusId.Poison, StatusId.Bleed }, S(id).Triggers.Where(t => t.Effect == EffectKind.TickStatus).Select(t => t.Status)));
        Assert.Equal("wide_siphon", T(a[4], EffectKind.Damage).Param);
    }

    [Fact]
    public void StarLancer_LanceSight_Impale()
    {
        var p = Steps("mount_star_lancer", "passive"); var a = Steps("mount_star_lancer", "active");
        Assert.Equal(new double[] { 20, 25, 30, 35, 40 }, Each(p, id => T(id, EffectKind.Charge).Value));
        Assert.Equal(new double[] { 100, 125, 150, 175, 200 }, Each(p, id => P(id, EffectKind.Charge, "cap")));
        Assert.Equal(new double[] { 0, 0, 30, 30, 30 }, Each(p, id => P(id, EffectKind.Charge, "double_below")));
        Assert.Equal(new[] { false, false, false, false, true }, p.Select(id => S(id).Triggers.Any(t => t.Effect == EffectKind.AdvanceTimer && t.Param == "reload")));
        Assert.Equal(new double[] { 120, 140, 160, 180, 200 }, Each(a, id => T(id, EffectKind.Damage).Value));
        Assert.All(a, id => Assert.All(S(id).Triggers.Where(t => t.Effect == EffectKind.Damage), t => Assert.Equal("crit", t.Param)));
        Assert.Equal(new double[] { 0, 0, 80, 90, 100 }, Each(a, id => S(id).Triggers.Count(t => t.Effect == EffectKind.Damage) > 1 ? T(id, EffectKind.Damage, 1).Value : 0));
        Assert.Equal(TargetKind.SecondEnemy, T(a[2], EffectKind.Damage, 1).Target);
        Assert.Equal(TargetKind.AllOtherEnemies, T(a[4], EffectKind.Damage, 1).Target);
    }

    [Fact]
    public void FrostFrigate_CoolantLoop_IceWall()
    {
        var p = Steps("mount_frost_frigate", "passive"); var a = Steps("mount_frost_frigate", "active");
        Assert.All(p, id => Assert.Equal(1, T(id, EffectKind.RemoveStatus).Stacks));
        Assert.Equal(new double[] { 2, 3, 4, 5, 6 }, Each(p, id => T(id, EffectKind.Shield).Value));
        Assert.Equal(new double[] { 0, 0, 25, 30, 40 }, Each(p, id => S(id).Triggers.FirstOrDefault(t => t.Effect == EffectKind.CheatDeath)?.Value ?? 0));
        Assert.Equal("largest;to_front", T(p[4], EffectKind.RemoveStatus).Param);
        Assert.Equal(new double[] { 60, 70, 80, 90, 100 }, Each(a, id => T(id, EffectKind.Damage).Value));
        Assert.Equal(new double[] { 6, 8, 10, 12, 15 }, Each(a, id => T(id, EffectKind.Shield).Value));
        Assert.Equal(new double[] { 0, 0, 100, 125, 150 }, Each(a, id => P(id, EffectKind.Shield, "wall")));
        Assert.Contains("wall_all", T(a[4], EffectKind.Shield).Param!);
        // it never applies Chill (no Fire conflict)
        Assert.All(p.Concat(a), id => Assert.DoesNotContain(S(id).Triggers, t => t.Effect == EffectKind.ApplyStatus));
    }

    [Fact]
    public void RiftRacer_Slipstream_RiftDash()
    {
        var p = Steps("mount_rift_racer", "passive"); var a = Steps("mount_rift_racer", "active");
        Assert.Equal(new double[] { 0, 30, 40, 50, 60 }, Each(p, id => Rule(id, "slipstream")));
        Assert.Equal(new[] { "", "", "mount", "mount", "all" }, p.Select(id => S(id).Triggers.FirstOrDefault(t => t.Effect == EffectKind.AdvanceTimer)?.Param ?? ""));
        Assert.Equal(new double[] { 60, 70, 80, 90, 100 }, Each(a, id => T(id, EffectKind.RiderAttack).Value));
        Assert.Equal(new double[] { 1, 1, 1, 1, 2 }, Each(a, id => T(id, EffectKind.RiderAttack).Stacks));
        Assert.Equal(new[] { false, false, true, true, true }, a.Select(id => T(id, EffectKind.RiderAttack).Param == "combo"));
    }

    [Fact]
    public void AegisWalker_PaybackCoil_PaybackCannon()
    {
        var p = Steps("mount_aegis_walker", "passive"); var a = Steps("mount_aegis_walker", "active");
        Assert.Equal(new double[] { 20, 25, 30, 35, 40 }, Each(p, id => Rule(id, "coil")));
        Assert.Equal(new double[] { 300, 300, 400, 450, 500 }, Each(p, id => Rule(id, "coil_cap")));
        Assert.Equal(new[] { false, false, true, true, true }, p.Select(id => HasRule(id, "coil_recoil") && Rule(id, "coil_counter") == 50));
        Assert.Equal(new[] { false, false, false, false, true }, p.Select(id => HasRule(id, "coil_window")));
        Assert.Equal(new double[] { 60, 80, 100, 120, 140 }, Each(a, id => T(id, EffectKind.Damage).Value));
        Assert.All(a, id => Assert.Equal("coil", T(id, EffectKind.Damage).Param));
        Assert.Equal(new double[] { 0, 0, 2, 2, 2 }, Each(a, id => S(id).Triggers.FirstOrDefault(t => t.Effect == EffectKind.CarrySignatures)?.Stacks ?? 0));
        Assert.Equal(TargetKind.LastAttacker, T(a[0], EffectKind.Damage).Target);
        Assert.Equal(TargetKind.Attackers, T(a[4], EffectKind.Damage).Target);
    }

    [Fact]
    public void HiveCarrier_Wingman_LaunchBay()
    {
        var p = Steps("mount_hive_carrier", "passive"); var a = Steps("mount_hive_carrier", "active");
        Assert.Equal(new double[] { 30, 35, 40, 45, 50 }, Each(p, id => T(id, EffectKind.Damage).Value));
        Assert.Equal(new double[] { 1, 1, 1, 2, 2 }, Each(p, id => P(id, EffectKind.Damage, "turn_cap")));
        Assert.Equal(new[] { false, false, true, true, true }, p.Select(id => S(id).Triggers.Any(t => t.Effect == EffectKind.CarrySignatures)));
        Assert.Equal(10, T(p[4], EffectKind.ChargeAllies).Value);
        Assert.Equal(new double[] { 20, 25, 30, 35, 40 }, Each(a, id => T(id, EffectKind.Summon).Value));
        Assert.Equal(new double[] { 2, 2, 3, 3, 3 }, Each(a, id => P(id, EffectKind.Summon, "max")));
        Assert.Equal(new double[] { 0, 0, 50, 50, 50 }, Each(a, id => P(id, EffectKind.Summon, "guard")));
        Assert.Equal(new[] { false, false, false, false, true }, a.Select(id => T(id, EffectKind.Summon).Param!.Contains("carry")));
    }

    [Fact]
    public void ChronoSkiff_TimeDebt_EchoShot()
    {
        var p = Steps("mount_chrono_skiff", "passive"); var a = Steps("mount_chrono_skiff", "active");
        Assert.All(p, id => Assert.Equal((StatusId.Delay, TargetKind.FastestNonBoss), (T(id, EffectKind.ApplyStatus).Status, T(id, EffectKind.ApplyStatus).Target)));
        Assert.Equal(new double[] { -20, -25, -30, -35, -40 }, Each(p, id => T(id, EffectKind.GainRage).Value)); // bosses lose rage instead
        Assert.Equal(new double[] { 0, 0, 15, 20, 25 }, Each(p, id => S(id).Triggers.FirstOrDefault(t => t.When == TriggerKind.OnSkipTurn)?.Value ?? 0));
        var low = T(p[4], EffectKind.ApplyStatus, 1);
        Assert.Equal(("self_hp_below:50", 1, true), (low.If, low.MaxPerBattle, low.Rider));
        Assert.Equal(new double[] { 60, 70, 80, 90, 100 }, Each(a, id => T(id, EffectKind.RiderAttack).Value));
        Assert.Equal(new double[] { 1, 1, 1, 1, 2 }, Each(a, id => T(id, EffectKind.RiderAttack).Stacks));
        Assert.Equal(new[] { false, false, true, true, true }, a.Select(id => T(id, EffectKind.RiderAttack).Param!.Contains("as_echo")));
    }

    /// <summary>The steps between the kit steps give +5% mount damage; Fuel levels +1%; the collection +1% per step of every mount.</summary>
    [Fact]
    public void MountDamage_InBetweenSteps_Levels_Collection()
    {
        var cfg = C.MountConfig;
        double Pct(int stars, int awaken, int level) => MountSystem.MountStats(C, "mount_scout_saucer", new MountEntry { Stars = stars, Awaken = awaken, Level = level }).Value(StatId.MountPower);
        Assert.Equal(0, Pct(1, 0, 1));
        Assert.Equal(5, Pct(2, 0, 1));   // 2★
        Assert.Equal(5, Pct(3, 0, 1));   // 3★ is a kit step: no extra %
        Assert.Equal(10, Pct(4, 0, 1));  // 4★
        Assert.Equal(15, Pct(5, 1, 1));  // A1
        Assert.Equal(20, Pct(5, 2, 1));  // A2
        Assert.Equal(25, Pct(5, 4, 1));  // A4
        Assert.Equal(25 + 29, Pct(5, 5, 30)); // +1% per level
        Assert.Equal(new[] { new StatMod(StatId.MountPower, 1) }.Select(m => (m.Stat, m.Flat)), cfg.CollectionPerStep.Select(m => (m.Stat, m.Flat)));
    }
}
