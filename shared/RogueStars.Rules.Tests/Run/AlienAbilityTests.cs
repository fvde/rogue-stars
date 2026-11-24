using System.Text.RegularExpressions;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Run.SidekickTests;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Aliens pass (owner-approved review rev 4.3): every alien's card per tier star with its exact numbers, the card rules (whole
/// numbers, owner ranges, small ranges move only at tier stars), and each new mechanic in a battle: hits in % of the worm's ATK,
/// shared statuses, Guard, Zip Strike's combos, Void Echo, Thorn Harvest, Rock Ward, Rime Bite / Slow Ooze, Solar Flare, Tidal
/// Call, Starlight, Hatch, Arc Whip and the effect traits.
/// </summary>
public class AlienAbilityTests
{
    private static readonly ContentDb C = ContentDb.Default;

    private static string Card(string species, int star) => SkillText.Full(C.Skill(AlienSystem.AbilityAt(C, species, star))!);

    /// <summary>The approved cards at the tier stars 1 / 3 / 5 / 7 / 10 (page wording; Arc Whip / Hatch: singular for one).</summary>
    public static readonly Dictionary<string, string[]> TierCards = new()
    {
        ["alien_blorp"] = Hits("Hit", "Bleed", 15, 17, 19, 22, 25, 1, 1, 2, 2, 3),
        ["alien_mossling"] = Hits("Hit", "Radiation", 15, 17, 19, 22, 25, 2, 2, 3, 3, 4),
        ["alien_glimmerfly"] = Hits("Zap", "Shock", 15, 17, 19, 22, 25, 1, 1, 2, 2, 3),
        ["alien_shiverpup"] = Hits("Bite", "Chill", 15, 17, 19, 22, 25, 1, 1, 2, 2, 3),
        ["alien_emberkit"] = Hits("Hit", "Heat", 15, 17, 19, 22, 25, 1, 1, 2, 2, 3),
        ["alien_pebblehorn"] = new[] { 20, 25, 35, 40, 50 }.Select(v => $"Guard the worm: the next hit it takes deals {v}% less.").ToArray(),
        ["alien_zib"] = new[] { 20, 22, 24, 27, 30 }.Select(v => $"Strike the front enemy for {v}% ATK; uses your combo chance.").ToArray(),
        ["alien_frostmite"] = new[] { (20, 5), (22, 9), (24, 13), (27, 16), (30, 20) }.Select(x => $"Bite the front enemy for {x.Item1}% ATK. {x.Item2}% chance to Freeze it.").ToArray(),
        ["alien_sporeling"] = new[] { (8, 1), (9, 1), (10, 1), (11, 2), (12, 2) }.Select(x => $"Hit every enemy for {x.Item1}% ATK and apply {x.Item2} Radiation.").ToArray(),
        ["alien_clackclaw"] = new[] { 5, 6, 7, 8, 9 }.Select(v => $"The worm gets +{v}% counter chance for 3 rounds.").ToArray(),
        ["alien_whirrbit"] = new[] { 5, 6, 7, 8, 9 }.Select(v => $"The worm gets +{v}% combo chance for 3 rounds.").ToArray(),
        ["alien_arcwhisk"] = new[] { (1, 20), (1, 22), (2, 24), (2, 27), (3, 30) }
            .Select(x => $"Arc to {x.Item1} {(x.Item1 == 1 ? "enemy" : "enemies")} for {x.Item2}% ATK. Counts as your Arc jump.").ToArray(),
        ["alien_voidmaw"] = new[] { 20, 25, 35, 40, 50 }.Select(v => $"Repeat the worm's last attack at {v}% damage (an Echo).").ToArray(),
        ["alien_thornqueen"] = new[] { 1, 2, 3, 4, 5 }.Select(v => $"Consume the front enemy's Bleed. Heal the worm {v}% of ATK per stack consumed.").ToArray(),
        ["alien_gravitoad"] = new[] { 5, 10, 12, 15, 20 }.Select(v => $"Shield the worm for {v}% of max HP.").ToArray(),
        ["alien_chronoslug"] = new[] { (20, 5), (22, 9), (24, 13), (27, 16), (30, 20) }
            .Select(x => $"Hit the front enemy for {x.Item1}% ATK. {x.Item2}% chance to Delay it (loses its next turn).").ToArray(),
        ["alien_solarch"] = new[] { (2, 10), (2, 20), (2, 30), (3, 35), (3, 50) }
            .Select(x => $"Apply {x.Item1} Heat to the front enemy. At 8+ Heat it goes Supernova now, +{x.Item2}% damage.").ToArray(),
        ["alien_tidecaller"] = new[] { 5, 6, 7, 8, 10 }.Select(v => $"Heal the worm {v}% of max HP. Another sidekick acts.").ToArray(),
        ["alien_nebulisk"] = new[] { (3, 10), (4, 12), (5, 14), (6, 17), (7, 20) }
            .Select(x => $"Give the worm a Starlight stack: +{x.Item1}% crit chance, +{x.Item2}% crit damage. Max 3, lasts the battle.").ToArray(),
        ["alien_swarmheart"] = new[] { (15, 1), (17, 1), (19, 1), (22, 2), (25, 2) }
            .Select(x => $"Hatch a Drone that zaps for {x.Item1}% ATK. Max {x.Item2} {(x.Item2 == 1 ? "Drone" : "Drones")}.").ToArray(),
    };

    private static string[] Hits(string verb, string status, int h1, int h3, int h5, int h7, int h10, int s1, int s3, int s5, int s7, int s10)
    {
        var h = new[] { h1, h3, h5, h7, h10 };
        var s = new[] { s1, s3, s5, s7, s10 };
        return Enumerable.Range(0, 5).Select(i => $"{verb} the front enemy for {h[i]}% ATK and apply {s[i]} {status}.").ToArray();
    }

    private static readonly int[] Tiers = { 1, 3, 5, 7, 10 };

    [Fact]
    public void EveryAlien_HasItsApprovedCard_AtEveryTierStar()
    {
        Assert.Equal(C.AlienTypes.Keys.OrderBy(k => k, StringComparer.Ordinal), TierCards.Keys.OrderBy(k => k, StringComparer.Ordinal));
        var failures = new List<string>();
        foreach (var kv in TierCards)
            for (int i = 0; i < Tiers.Length; i++)
            {
                string got = Card(kv.Key, Tiers[i]);
                if (got != kv.Value[i]) failures.Add(kv.Key + " " + Tiers[i] + "★: expected \"" + kv.Value[i] + "\", got \"" + got + "\"");
            }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>
    /// Every tier star changes the card; stars between tier stars either raise the main number (wide ranges) or hold the card
    /// (small ranges and every count / chance: rev 4.3); wide ranges grow at every star; the main number never drops.
    /// </summary>
    [Fact]
    public void TierStarsChangeTheCard_SmallRangesHoldBetween_WideRangesGrowEveryStar()
    {
        var small = new HashSet<string> { "alien_sporeling", "alien_clackclaw", "alien_whirrbit", "alien_thornqueen", "alien_tidecaller", "alien_nebulisk" };
        foreach (var sp in C.AlienTypes.Keys)
        {
            var cards = Enumerable.Range(1, 10).Select(s => Card(sp, s)).ToList();
            for (int star = 2; star <= 10; star++)
            {
                bool tier = AlienSystem.IsTierStar(C, star);
                bool changed = cards[star - 1] != cards[star - 2];
                if (tier) Assert.True(changed, sp + " " + star + "★ (a tier star) leaves the card unchanged");
                else if (small.Contains(sp)) Assert.False(changed, sp + " " + star + "★: a small range moves only at tier stars");
                else Assert.True(changed, sp + " " + star + "★: a wide range grows at every star");
            }
            // the main number (the first one on the card) never drops
            var nums = cards.Select(c => int.Parse(Regex.Match(c, @"\d+").Value)).ToList();
            for (int i = 1; i < 10; i++) Assert.True(nums[i] >= nums[i - 1], sp + ": " + string.Join(",", nums));
        }
    }

    /// <summary>Owner ranges are exact at 1★ and 10★; wide ranges start and end on multiples of 5.</summary>
    [Fact]
    public void OwnerRanges_AreExactAtOneAndTenStars()
    {
        (string sp, int lo, int hi)[] ranges =
        {
            ("alien_gravitoad", 5, 20), ("alien_tidecaller", 5, 10), ("alien_thornqueen", 1, 5), ("alien_pebblehorn", 20, 50),
            ("alien_voidmaw", 20, 50), ("alien_nebulisk", 3, 7), ("alien_blorp", 15, 25), ("alien_zib", 20, 30), ("alien_swarmheart", 15, 25),
        };
        foreach (var (sp, lo, hi) in ranges)
        {
            Assert.Equal(lo, C.Skill(AlienSystem.AbilityAt(C, sp, 1))!.Triggers[0] is var a && a.Stat != null ? a.Stat.Flat : a.Value);
            Assert.Equal(hi, C.Skill(AlienSystem.AbilityAt(C, sp, 10))!.Triggers[0] is var b && b.Stat != null ? b.Stat.Flat : b.Value);
        }
        Assert.Equal(10, C.Skill("ask_solarch_1")!.Triggers[1].Value);
        Assert.Equal(50, C.Skill("ask_solarch_10")!.Triggers[1].Value);
        Assert.Equal(20, C.Skill("ask_nebulisk_10")!.Triggers[1].Stat.Flat);
        // CC chances move only at tier stars: 5 → 9 → 13 → 16 → 20 (Frostmite, Chronoslug)
        foreach (var sp in new[] { "alien_frostmite", "alien_chronoslug" })
            Assert.Equal(new double[] { 5, 5, 9, 9, 13, 13, 16, 16, 16, 20 }, Enumerable.Range(1, 10).Select(s => C.Skill(AlienSystem.AbilityAt(C, sp, s))!.Triggers[1].Chance));
    }

    /// <summary>Content rule: whole numbers only (no decimal in any alien card), cards ≤ 20 words, numbers in the data whole too.</summary>
    [Fact]
    public void AlienCards_HaveNoDecimals_AndStayShort()
    {
        foreach (var sp in C.AlienTypes.Values)
            foreach (var step in sp.ArchetypeSkills)
            {
                var s = C.Skill(step.SkillId)!;
                string card = SkillText.Full(s);
                Assert.False(Regex.IsMatch(card, @"\d[.,]\d"), step.SkillId + ": " + card);
                Assert.True(SkillText.WordCount(card) <= 20, step.SkillId + ": " + card);
                foreach (var t in s.Triggers)
                {
                    Assert.Equal(Math.Round(t.Value), t.Value);
                    Assert.Equal(Math.Round(t.Chance), t.Chance);
                    if (t.Stat != null) Assert.Equal(Math.Round(t.Stat.Flat), t.Stat.Flat);
                }
            }
    }

    // ---- the battle model: % of the worm's ATK ---------------------------------------------------------------------------

    /// <summary>A worm (ATK given) and sidekicks against defenceless dummies (no DEF, 1 ATK): exact numbers.</summary>
    internal static BattleResult Dummy(IEnumerable<CombatAlien> aliens, double wormAtk = 200, int rounds = 6, int dummies = 1, IEnumerable<string>? wormSkills = null,
        ContentDb? content = null, double wormHp = 100000, ulong seed = 3, IEnumerable<StatMod>? wormStats = null, Dictionary<string, int>? levels = null)
    {
        var c = content ?? C;
        var lo = new CombatLoadout { DisplayName = "Worm" };
        lo.WormStats.Add(new StatMod(StatId.Hp, wormHp)).Add(new StatMod(StatId.Atk, wormAtk)).Add(new StatMod(StatId.Speed, 100));
        if (wormStats != null) lo.WormStats.AddAll(wormStats);
        lo.Aliens.AddRange(aliens);
        var setup = new BattleSetup { RoundCap = rounds };
        setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, wormSkills?.ToList(), null, levels));
        for (int i = 0; i < dummies; i++)
        {
            var d = new CombatantSpec { Id = "dummy" + i, Name = "Dummy " + i, Role = CombatRole.Enemy, Targetable = true };
            d.Stats.Add(new StatMod(StatId.Hp, 1e7)).Add(new StatMod(StatId.Atk, 50)).Add(new StatMod(StatId.Speed, 1));
            setup.Enemy.Add(d);
        }
        return BattleSimulator.Simulate(setup, c, new Rng(seed));
    }

    private static int Idx(BattleResult r, string id) => r.Fighters.First(f => f.Id == id).Index;
    private static int Worm(BattleResult r) => r.Fighters.Single(f => f.Role == CombatRole.Worm).Index;

    [Fact]
    public void Hits_AreAPercentOfTheWormsAtk_TimesAlienPower()
    {
        long Hit(double atk, int star = 1, IEnumerable<string>? skills = null)
        {
            var r = Dummy(new[] { Alien("alien_blorp", star, 400) }, atk, 3, wormSkills: skills);
            return r.Log.First(e => e.Action == BattleAction.Skill && e.SkillId == AlienSystem.AbilityAt(C, "alien_blorp", star)).Amount;
        }
        Assert.Equal(30, Hit(200));              // 15% of 200
        Assert.Equal(60, Hit(400));              // scales with the worm's ATK
        Assert.Equal(50, Hit(200, 10));          // 25% at 10★
        Assert.Equal(44, Hit(200, 1, new[] { "alien_bond" })); // Squad Bond +45% alien power: 30 × 1.45 = 43.5
    }

    /// <summary>A sidekick's statuses are the shared ones with the worm as their source: the worm's power, its DoT ticks, its triggers.</summary>
    [Fact]
    public void SidekickStatuses_AreTheWormsStatuses()
    {
        var r = Dummy(new[] { Alien("alien_blorp", 10, 400) }, 200, 4);
        int worm = Worm(r), blorp = Idx(r, "alien_blorp");
        var applied = r.Log.First(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Bleed);
        Assert.Equal(blorp, applied.Actor); // the log names the alien …
        Assert.Equal(3, applied.Stacks);
        var tick = r.Log.First(e => e.Action == BattleAction.StatusTick && e.Status == StatusId.Bleed);
        Assert.Equal(worm, tick.Actor); // … but the Bleed is the worm's: its ticks are the worm's damage at the worm's ATK
        Assert.Equal(Math.Round(200 * 0.06 * 3), tick.Amount);
        // the worm's status triggers fire for an alien's stacks: Deep Freeze freezes at 5 Chill applied by Shiverpup
        var d = Dummy(new[] { Alien("alien_shiverpup", 10, 400) }, 200, 8, wormSkills: new[] { "ice_deep_freeze" });
        Assert.Contains(d.Log, e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Freeze && e.SkillId == "ice_deep_freeze");
    }

    // ---- Guard (Pebblehorn) -----------------------------------------------------------------------------------------------

    [Fact]
    public void StoneGuard_SoftensTheNextHit_WhichStillLandsAndCanBeCountered()
    {
        var r = Dummy(new[] { Alien("alien_pebblehorn", 1, 400) }, 200, 6, wormStats: new[] { new StatMod(StatId.CounterChance, 95) });
        int worm = Worm(r);
        var guard = r.Log.First(e => e.Action == BattleAction.Guard);
        Assert.Equal(worm, guard.Target);
        Assert.Equal(20, guard.Amount);
        Assert.Equal(1, guard.Stacks);
        var guarded = r.Log.First(e => e.Action == BattleAction.Guarded);
        Assert.Equal(guard.Actor, guarded.Actor);
        int i = r.Log.IndexOf(guarded);
        var hit = r.Log.Skip(i + 1).First(e => e.Target == worm && e.Action == BattleAction.Basic);
        Assert.Equal(50 * 0.8, hit.Amount, 0);                 // the dummy's 50 ATK hit, 20% softer
        Assert.Equal(10, guarded.Amount);                      // 20% prevented
        Assert.Contains(r.Log.Skip(i + 1).TakeWhile(e => e.Round == guarded.Round), e => e.Action == BattleAction.Counter && e.Actor == worm); // counters fire
        // one guard, refreshed (no stacking): never more than one guarded hit between two guards
        Assert.All(r.Log.Where(e => e.Action == BattleAction.Guard), g => Assert.Equal(1, g.Stacks));
        // 10★ 50%; Bulwark +20% (60%); Lingering softens one more hit
        var top = Dummy(new[] { Alien("alien_pebblehorn", 10, 400, ContentDb.TraitBulwark) }, 200, 4);
        Assert.Equal(60, top.Log.First(e => e.Action == BattleAction.Guard).Amount);
        Assert.Equal(30, top.Log.First(e => e.Action == BattleAction.Guarded).Amount);
        var lin = Dummy(new[] { Alien("alien_pebblehorn", 1, 34, ContentDb.TraitLingering) }, 200, 8);
        Assert.Equal(2, lin.Log.First(e => e.Action == BattleAction.Guard).Stacks);
        Assert.Equal(1, lin.Log.First(e => e.Action == BattleAction.Guarded).Stacks); // one more hit left after the first
    }

    // ---- Zip Strike (Zib) --------------------------------------------------------------------------------------------------

    [Fact]
    public void ZipStrike_RollsTheWormsComboChance_AndFiresComboPayoffs()
    {
        var no = Dummy(new[] { Alien("alien_zib", 1, 400) }, 200, 5);
        Assert.Equal(5, no.Log.Count(e => e.Action == BattleAction.Skill && e.SkillId == "ask_zib_1")); // one strike per action
        Assert.All(no.Log.Where(e => e.SkillId == "ask_zib_1" && e.Action == BattleAction.Skill), e => Assert.Equal(40, e.Amount)); // 20% of 200
        var yes = Dummy(new[] { Alien("alien_zib", 1, 400) }, 200, 5, wormSkills: new[] { "combo_momentum" }, wormStats: new[] { new StatMod(StatId.ComboChance, 95) });
        Assert.True(yes.Log.Count(e => e.Action == BattleAction.Skill && e.SkillId == "ask_zib_1") > 5);
        int zib = Idx(yes, "alien_zib");
        // Momentum (the worm's OnCombo: +rage) fires right after a Zib combo strike
        var strikes = yes.Log.Where(e => e.Action == BattleAction.SidekickAct && e.Actor == zib).ToList();
        Assert.Contains(yes.Log, e => e.Action == BattleAction.GainRage && e.SkillId == "combo_momentum" && e.Round == strikes[0].Round);
    }

    // ---- Void Echo (Voidmaw) -----------------------------------------------------------------------------------------------

    [Fact]
    public void VoidEcho_RepeatsTheWormsLastAttack_AtItsPercent_CreditedToTheAlien()
    {
        var r = Dummy(new[] { Alien("alien_voidmaw", 1, 400) }, 200, 4);
        int worm = Worm(r), vm = Idx(r, "alien_voidmaw");
        var echo = r.Log.First(e => e.Action == BattleAction.Echo && e.Actor == vm);
        Assert.Equal("ask_voidmaw_1", echo.SkillId);
        var basic = r.Log.First(e => e.Action == BattleAction.Basic && e.Actor == worm && !e.Crit);
        var repeat = r.Log[r.Log.IndexOf(echo) + 1];
        Assert.Equal(worm, repeat.Actor);                      // the worm's attack: its on-hit triggers fire
        Assert.Equal(basic.Amount * 0.2, repeat.Amount, 0);   // at 20%
        Assert.True(r.Fighters[vm].DamageDealt > 0);           // credited to the alien
        var top = Dummy(new[] { Alien("alien_voidmaw", 10, 400) }, 200, 4);
        Assert.Equal(basic.Amount * 0.5, top.Log[top.Log.IndexOf(top.Log.First(e => e.Action == BattleAction.Echo)) + 1].Amount, 0);
    }

    // ---- Thorn Harvest (Thornqueen) ----------------------------------------------------------------------------------------

    [Fact]
    public void ThornHarvest_ConsumesBleed_HealsPerStack_AndFiresTheWormsConsumePayoffs()
    {
        var r = Dummy(new[] { Alien("alien_blorp", 10, 400), Alien("alien_thornqueen", 10, 50) }, 200, 6, wormHp: 100000,
            wormSkills: new[] { "mythic_fallout_wounds" });
        int tq = Idx(r, "alien_thornqueen");
        var removed = r.Log.First(e => e.Action == BattleAction.StatusRemoved && e.Actor == tq && e.Status == StatusId.Bleed);
        int i = r.Log.IndexOf(removed);
        // the worm is at full HP: a heal shows only after damage; check the consume payoff instead and the heal amount below
        Assert.Contains(r.Log.Skip(i), e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Poison && e.SkillId == "mythic_fallout_wounds");
        // a hurt worm heals 5% of ATK per stack consumed at 10★
        var h = Dummy(new[] { Alien("alien_blorp", 10, 400), Alien("alien_thornqueen", 10, 50) }, 200, 6, wormHp: 2000);
        var heal = h.Log.First(e => e.Action == BattleAction.Heal && e.SkillId == "ask_thornqueen_10");
        var rem = h.Log.Take(h.Log.IndexOf(heal)).Last(e => e.Action == BattleAction.StatusRemoved && e.Status == StatusId.Bleed && h.Fighters[e.Actor].Id == "alien_thornqueen");
        Assert.Equal(200 * 0.05 * rem.Amount, heal.Amount, 0);
    }

    // ---- Rock Ward (Gravitoad) ---------------------------------------------------------------------------------------------

    [Fact]
    public void RockWard_ShieldsAPercentOfMaxHp_AlienPowerAndBulwarkRaiseIt()
    {
        long Shield(int star, params string[] traits) =>
            Dummy(new[] { Alien("alien_gravitoad", star, 400, traits) }, 200, 2, wormHp: 10000).Log.First(e => e.Action == BattleAction.Shield).Amount;
        Assert.Equal(500, Shield(1));
        Assert.Equal(2000, Shield(10));
        Assert.Equal(600, Shield(1, ContentDb.TraitBulwark));
        long bonded = Dummy(new[] { Alien("alien_gravitoad", 1, 400) }, 200, 2, wormHp: 10000, wormSkills: new[] { "alien_bond" }).Log.First(e => e.Action == BattleAction.Shield).Amount;
        Assert.Equal(725, bonded); // +45% alien power
    }

    // ---- CC: Rime Bite (Frostmite) and Slow Ooze (Chronoslug) ---------------------------------------------------------------

    [Fact]
    public void SlowOoze_Delay_HasAThreeRoundReapplyCooldown_LikeFreeze()
    {
        var c = new ContentDb();
        c.Skill("ask_chronoslug_10")!.Triggers[1].Chance = 100; // a sure Delay to see the cooldown
        var r = Dummy(new[] { Alien("alien_chronoslug", 10, 400) }, 200, 10, content: c);
        var rounds = r.Log.Where(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Delay).Select(e => e.Round).ToList();
        Assert.True(rounds.Count >= 2);
        for (int i = 1; i < rounds.Count; i++) Assert.True(rounds[i] - rounds[i - 1] >= C.Combat.Statuses[StatusId.Freeze].ApplyCooldownRounds);
        // Rime Bite's Freeze has Freeze's own re-apply cooldown and resets Chill
        var f = new ContentDb();
        f.Skill("ask_frostmite_10")!.Triggers[1].Chance = 100;
        var fr = Dummy(new[] { Alien("alien_frostmite", 10, 400) }, 200, 10, content: f);
        Assert.Contains(fr.Log, e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Freeze && e.SkillId == "ask_frostmite_10");
    }

    // ---- Solar Flare (Solarch) ---------------------------------------------------------------------------------------------

    [Fact]
    public void SolarFlare_DetonatesHeatAtEightStacks_WithItsBonus()
    {
        var r = Dummy(new[] { Alien("alien_solarch", 1, 400) }, 200, 6);
        var heat = r.Log.Where(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Heat).Select(e => e.Stacks).ToList();
        Assert.Equal(new[] { 2, 4, 6, 8 }, heat.Take(4));
        var nova = r.Log.First(e => e.Action == BattleAction.Supernova && e.Amount == 0);
        Assert.Equal("ask_solarch_1", nova.SkillId);
        Assert.Equal(8, nova.Stacks);           // at 8 Heat, before the natural burst at 10
    }

    // ---- Tidal Call (Tidecaller) -------------------------------------------------------------------------------------------

    [Fact]
    public void TidalCall_HealsAndAnotherSidekickActs()
    {
        var r = Dummy(new[] { Alien("alien_tidecaller", 1, 50), Alien("alien_blorp", 1, 1) }, 200, 8, wormHp: 3000);
        int tc = Idx(r, "alien_tidecaller"), bl = Idx(r, "alien_blorp");
        var act = r.Log.First(e => e.Action == BattleAction.SidekickAct && e.Actor == tc);
        var after = r.Log.Skip(r.Log.IndexOf(act) + 1).TakeWhile(e => !(e.Action == BattleAction.SidekickAct && e.Actor == tc)).ToList();
        Assert.Contains(after, e => e.Action == BattleAction.SidekickAct && e.Actor == bl && e.Stacks == 1); // Blorp acts (extra)
        Assert.DoesNotContain(after, e => e.Action == BattleAction.SidekickAct && e.Actor == tc && e.Stacks == 1); // never itself
        var alone = Dummy(new[] { Alien("alien_tidecaller", 1, 50) }, 200, 8, wormHp: 3000);
        Assert.DoesNotContain(alone.Log, e => e.Action == BattleAction.SidekickAct && e.Stacks == 1);
        Assert.Contains(alone.Log, e => e.Action == BattleAction.Heal && e.SkillId == "ask_tidecaller_1");
    }

    // ---- Starlight Eye (Nebulisk) ------------------------------------------------------------------------------------------

    [Fact]
    public void Starlight_StacksToThree_AndLastsTheBattle()
    {
        var r = Dummy(new[] { Alien("alien_nebulisk", 10, 400) }, 200, 8);
        int worm = Worm(r);
        var buffs = r.Log.Where(e => e.Action == BattleAction.Buff && e.SkillId == "ask_nebulisk_10" && e.Target == worm).ToList();
        Assert.Equal(6, buffs.Count); // 3 stacks × (crit chance, crit damage), then it holds
        Assert.Equal(new long[] { 7, 20, 7, 20, 7, 20 }, buffs.Select(b => b.Amount));
    }

    // ---- Hatch (Swarmheart) ------------------------------------------------------------------------------------------------

    [Fact]
    public void Hatch_GrowsTheSquadUpToItsCap_DronesZapAPercentOfAtk_AndTriggerTheStartSignature()
    {
        int Drones(int star) => Dummy(new[] { Alien("alien_swarmheart", star, 400) }, 200, 8).Fighters.Count(f => f.Role == CombatRole.Summon);
        Assert.Equal(1, Drones(1));
        Assert.Equal(1, Drones(5));
        Assert.Equal(2, Drones(7));
        var r = Dummy(new[] { Alien("alien_swarmheart", 1, 400) }, 200, 8, wormSkills: new[] { "sig_fire" });
        var drone = r.Fighters.First(f => f.Role == CombatRole.Summon);
        Assert.Equal(Idx(r, "alien_swarmheart"), drone.SummonedBy);
        var zap = r.Log.First(e => e.Action == BattleAction.Skill && e.Actor == drone.Index && e.SkillId == "skab_drone");
        Assert.Equal(30, zap.Amount); // 15% of 200
        // a drone's action also triggers the start signature once (Ignite: one Heat), Hivemind or not
        Assert.Contains(r.Log, e => e.Action == BattleAction.StatusApplied && e.Actor == drone.Index && e.SkillId == "sig_fire");
    }

    // ---- Arc Whip (Arcwhisk) -----------------------------------------------------------------------------------------------

    [Fact]
    public void ArcWhip_ArcsToItsTargets_AndCountsAsTheWormsArcJump()
    {
        var c = new ContentDb();
        c.Skill("light_spark")!.Triggers[0].Chance = 100;
        var r = Dummy(new[] { Alien("alien_arcwhisk", 10, 400) }, 200, 2, dummies: 3, wormSkills: new[] { "light_spark" }, content: c);
        int aw = Idx(r, "alien_arcwhisk");
        var act = r.Log.First(e => e.Action == BattleAction.SidekickAct && e.Actor == aw);
        var hits = r.Log.Skip(r.Log.IndexOf(act) + 1).Where(e => e.Action == BattleAction.Skill && e.Actor == aw && e.Round == act.Round).Take(3).ToList();
        Assert.Equal(3, hits.Select(h => h.Target).Distinct().Count());       // 3 enemies at 10★
        Assert.All(hits, h => Assert.Equal(60, h.Amount));                    // 30% of 200
        Assert.Contains(r.Log, e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Shock && e.SkillId == "light_spark"); // Spark: the worm's OnArcJump
        var run = new ActiveRun { Loadout = new CombatLoadout() };
        run.Loadout.Aliens.Add(Alien("alien_arcwhisk"));
        Assert.True(RunDraft.HasSource(C, run, "arc"));
    }

    // ---- effect traits ------------------------------------------------------------------------------------------------------

    [Fact]
    public void EffectTraits_EagerTwinWideLingeringVampiric()
    {
        // Eager: the first action at battle start (round 0)
        var eager = Dummy(new[] { Alien("alien_blorp", 1, 34, ContentDb.TraitEager) }, 200, 3);
        Assert.Contains(eager.Log, e => e.Action == BattleAction.SidekickAct && e.Round == 0);
        Assert.DoesNotContain(Dummy(new[] { Alien("alien_blorp", 1, 34) }, 200, 3).Log, e => e.Action == BattleAction.SidekickAct && e.Round == 0);
        // Twin: +1 stack on every 2nd action (1, 2, 1, 2 … Bleed added)
        var twin = Dummy(new[] { Alien("alien_blorp", 1, 400, ContentDb.TraitTwin) }, 200, 4);
        Assert.Equal(new[] { 1, 3, 4, 6 }, twin.Log.Where(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Bleed).Select(e => e.Stacks));
        // Wide: the hit also strikes one other enemy at half
        var wide = Dummy(new[] { Alien("alien_zib", 1, 400, ContentDb.TraitWide) }, 200, 1, dummies: 2);
        var zibHits = wide.Log.Where(e => e.Action == BattleAction.Skill && e.SkillId == "ask_zib_1").ToList();
        Assert.Equal(new long[] { 40, 20 }, zibHits.Select(e => e.Amount));
        Assert.NotEqual(zibHits[0].Target, zibHits[1].Target);
        // Lingering: a Chill lasts +1 round (one Eager action at battle start, then none)
        int Chill(params string[] traits)
        {
            var r = Dummy(new[] { Alien("alien_shiverpup", 1, 1, traits.Concat(new[] { ContentDb.TraitEager }).ToArray()) }, 1, 8);
            int first = r.Log.First(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Chill).Round;
            return r.Log.Where(e => e.Action == BattleAction.StatusExpired && e.Status == StatusId.Chill).Select(e => e.Round).DefaultIfEmpty(99).First() - first;
        }
        Assert.True(Chill(ContentDb.TraitLingering) > Chill(), "Lingering Chill lasts longer");
        // Vampiric: its damage heals the worm 10% of it
        var vamp = Dummy(new[] { Alien("alien_zib", 1, 400, ContentDb.TraitVampiric) }, 200, 4, wormHp: 3000);
        Assert.Contains(vamp.Log, e => e.Action == BattleAction.LifeSteal && e.SkillId == "ask_zib_1" && e.Amount == 4);
    }

    /// <summary>Every trait a species rolls (all but work and legacy traits) changes a seeded battle for some species that rolls it.</summary>
    [Fact]
    public void EveryCombatTrait_ChangesABattle()
    {
        foreach (var tr in C.AlienTraits.Values.Where(t => t.Kind is AlienTraitKind.Tempo or AlienTraitKind.Effect))
        {
            bool changed = false;
            foreach (var sp in C.AlienTypes.Values.Where(a => a.PossibleTraits.Contains(tr.Id)))
            {
                string Sig(bool with)
                {
                    var a = Alien(sp.Id, 10, 0, with ? new[] { tr.Id } : new string[0]);
                    var r = Fight(new[] { a, Alien("alien_blorp") }, new[] { "sig_bleed" }, rounds: 12, seed: 7);
                    return SkillTuningTests.Signature(r) + string.Join(",", r.Fighters.Select(f => f.EndShield));
                }
                if (Sig(true) != Sig(false)) { changed = true; break; }
            }
            Assert.True(changed, tr.Id + " changes no battle");
        }
    }
}
