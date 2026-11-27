using System.Text.RegularExpressions;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Run.BattleLab;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Parts v2 skills (docs/design/parts-v2/data.py): every Epic, Legendary, Mythic and awakened skill carries its approved text, its
/// numbers match the text, crowd control is Mythic S / SS once per enemy, and the new engine primitives behave (extra turns, sure
/// crits, forced misses, relative floors and ceilings, the shared blow-up caps).
/// </summary>
public class PartSkillTests
{
    private static readonly ContentDb C = ContentDb.Default;

    private static IEnumerable<SkillDef> PartSkills() => C.Skills.Values.Where(s => s.Source == SkillSource.Part).OrderBy(s => s.Id, StringComparer.Ordinal);

    private static string RepoFile(string rel)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, rel);
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException(rel);
    }

    // ---- content --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Counts_Epic1_Legendary2_Mythic5PlusAwakened_390Texts()
    {
        Assert.Equal(390, PartSkills().Count());
        foreach (var d in C.PartsConfig.Defs.Values)
        {
            int n = d.Rarity switch { PartRarity.Epic => 1, PartRarity.Legendary => 2, PartRarity.Mythic => 5, _ => 0 };
            Assert.Equal(n, d.SkillIds.Count);
            Assert.Equal(d.IsMythic ? 5 : 0, d.AwakenedSkillIds.Count);
            foreach (var id in d.SkillIds.Concat(d.AwakenedSkillIds))
            {
                var s = C.Skill(id)!;
                Assert.False(s.Draftable);
                Assert.Equal(SkillSource.Part, s.Source);
                Assert.Equal(s.Description, s.Text);
                Assert.Equal(s.Description, SkillText.Full(s)); // the card shows the approved text
            }
        }
    }

    /// <summary>Every text is the approved one, word for word: each appears in data.py (the page's source).</summary>
    [Fact]
    public void Texts_AreTheApprovedOnes_FromDataPy()
    {
        string data = File.ReadAllText(RepoFile("docs/design/parts-v2/data.py"));
        foreach (var s in PartSkills())
            Assert.True(data.Contains("\"" + s.Description + "\"", StringComparison.Ordinal), s.Id + ": " + s.Description);
        foreach (var m in ContentDbMythics())
            Assert.Contains("\"" + m.Name + "\"", data);
    }

    private static IEnumerable<PartDef> ContentDbMythics() => C.PartsConfig.Defs.Values.Where(d => d.IsMythic);

    /// <summary>
    /// The numbers in a skill's text are its effect numbers: every number of the text is a number of the skill (a value, chance,
    /// stacks, duration, every-Nth, per-battle, a Param / If number, ±100 for "+50% damage" multipliers, or a product such as a buff
    /// step × its stack cap for "max +20%"), or of its item's base skills (the Arc volley's "instead of every 3rd").
    /// </summary>
    [Fact]
    public void Texts_NumbersMatchTheEffects()
    {
        var failures = new List<string>();
        foreach (var d in C.PartsConfig.Defs.Values)
        {
            var itemNums = new HashSet<double>();
            foreach (var id in d.SkillIds) itemNums.UnionWith(Numbers(C.Skill(id)!));
            foreach (var id in d.SkillIds.Concat(d.AwakenedSkillIds))
            {
                var s = C.Skill(id)!;
                var nums = Numbers(s);
                var pool = new HashSet<double>(nums);
                foreach (var a in nums) foreach (var b in nums) pool.Add(a * b);
                foreach (var a in nums) { pool.Add(a - 100); pool.Add(100 - a); }
                foreach (Match m in Regex.Matches(s.Description, @"\d+"))
                {
                    double n = double.Parse(m.Value);
                    if (n == 1 || pool.Contains(n) || itemNums.Contains(n)) continue;
                    failures.Add(s.Id + ": " + n + " in \"" + s.Description + "\" (numbers: " + string.Join(",", nums.OrderBy(x => x)) + ")");
                }
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static HashSet<double> Numbers(SkillDef s)
    {
        var set = new HashSet<double>();
        foreach (var m in s.Stats) { set.Add(Math.Abs(m.Flat)); set.Add(Math.Abs(m.Percent)); }
        foreach (var t in s.Triggers)
        {
            foreach (var v in new[] { t.Value, t.Chance, t.Stacks, t.Duration, t.EveryN, t.MaxPerBattle })
                set.Add(Math.Abs(v));
            if (t.Stat != null) { set.Add(Math.Abs(t.Stat.Flat)); set.Add(Math.Abs(t.Stat.Percent)); }
            foreach (var text in new[] { t.Param, t.If })
                if (text != null)
                    foreach (Match m in Regex.Matches(text, @"\d+")) set.Add(double.Parse(m.Value));
        }
        set.Remove(0);
        return set;
    }

    [Fact]
    public void CrowdControl_OnlyMythicSAndSS_OncePerEnemy_BossesLoseRage()
    {
        var cc = new HashSet<StatusId> { StatusId.Stun, StatusId.Freeze, StatusId.Delay, StatusId.Charm };
        foreach (var d in C.PartsConfig.Defs.Values)
            foreach (var ids in new[] { d.SkillIds, d.AwakenedSkillIds })
                for (int i = 0; i < ids.Count; i++)
                {
                    var s = C.Skill(ids[i])!;
                    foreach (var t in s.Triggers.Where(t => t.Effect == EffectKind.ApplyStatus && cc.Contains(t.Status)))
                    {
                        Assert.True(d.IsMythic && i >= 3, s.Id + " applies crowd control below Mythic S");
                        Assert.Contains("per_target:1", t.Param);
                        Assert.Contains("boss_rage:20", t.Param);
                        Assert.Contains("once per enemy", s.Description);
                    }
                }
    }

    [Fact]
    public void StandardSkills_UseNoSynergyMechanic()
    {
        var syn = new HashSet<StatusId> { StatusId.Heat, StatusId.Chill, StatusId.Shock, StatusId.Poison, StatusId.Bleed, StatusId.Freeze };
        foreach (var s in PartSkills().Where(s => s.Rarity is Rarity.Epic or Rarity.Legendary))
        {
            Assert.DoesNotContain(s.Triggers, t => syn.Contains(t.Status));
            Assert.DoesNotContain(s.Triggers, t => t.Target == TargetKind.OtherEnemies && !(t.Param ?? "").Contains("noarc")); // no Arc jumps
            Assert.DoesNotContain(s.Triggers, t => t.Effect is EffectKind.Echo or EffectKind.Summon or EffectKind.AllyAction or EffectKind.ForceSupernova);
        }
    }

    // ---- engine primitives ---------------------------------------------------------------------------------------------

    private const TriggerKind Hit = TriggerKind.OnHit, Start = TriggerKind.OnBattleStart;

    private static int Count(BattleResult r, BattleAction a, int round, int actor) =>
        r.Log.Count(e => e.Action == a && e.Round == round && e.Actor == actor && !e.Dodge);

    [Fact]
    public void ExtraTurns_AtMostTwoPerRound_TheRestMoveOn()
    {
        var c = new ContentDb();
        var id = Skill(c, "et", On(Start, EffectKind.ExtraTurn, target: TargetKind.Self, stacks: 5));
        var r = Fight(c, Worm(id), 4);
        int w = Index(r, "worm");
        Assert.Equal(3, Count(r, BattleAction.Basic, 1, w)); // its own turn + 2 extra
        Assert.Equal(3, Count(r, BattleAction.Basic, 2, w)); // + 2 more
        Assert.Equal(2, Count(r, BattleAction.Basic, 3, w)); // the 5th
        Assert.Equal(1, Count(r, BattleAction.Basic, 4, w));
        // an extra turn never grants another: a turn-end extra turn gives one per own turn, not a chain
        var chain = Skill(c, "chain", On(TriggerKind.OnTurnEnd, EffectKind.ExtraTurn, target: TargetKind.Self));
        var rc = Fight(c, Worm(chain), 2);
        Assert.Equal(2, Count(rc, BattleAction.Basic, 1, Index(rc, "worm")));
    }

    [Fact]
    public void RageCost_RelativeCutsAddUp_FloorFifty()
    {
        var c = new ContentDb();
        var turbine = Skill(c, "turbine", Rule("rage_cost", value: 40), Rule("rage_cost", value: 25), On(TriggerKind.OnTurnStart, EffectKind.GainRage, 50, TargetKind.Self));
        var r = Fight(c, Worm(turbine), 3);
        // 100 − 65 = 35 → floor 50: a rage attack every turn on 50 rage (none without the cuts until turn 2)
        int[] WormRage(BattleResult x) => x.Log.Where(e => e.Action == BattleAction.Rage && e.Actor == Index(x, "worm")).Select(e => e.Round).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, WormRage(r));
        var plain = Skill(c, "plain", On(TriggerKind.OnTurnStart, EffectKind.GainRage, 50, TargetKind.Self));
        Assert.Equal(new[] { 2 }, WormRage(Fight(c, Worm(plain), 3)));
    }

    [Fact]
    public void Supernova_HeatSooner_AddsUp_FloorSix()
    {
        var c = new ContentDb();
        var id = Skill(c, "nova", Apply(Hit, StatusId.Heat), Rule("burst_delta", StatusId.Heat, 4), Rule("burst_delta", StatusId.Heat, 4));
        var r = Fight(c, Worm(id), 8);
        var first = r.Log.First(e => e.Action == BattleAction.Supernova && e.Amount == 0);
        Assert.Equal(6, first.Round); // 10 − 8 = 2 → floor 6
        Assert.Equal(6, first.Stacks);
    }

    [Fact]
    public void SidekickAction_RelativeCut_FloorFifty()
    {
        var c = new ContentDb();
        var id = Skill(c, "swarm", Rule("sidekick_act", value: 90));
        var alien = Unit("a0", CombatRole.Alien, 1, 0, 1);
        alien.SkillIds.Add("ask_blorp_1");
        var r = Fight(c, new[] { alien, Worm(id) }, 30, Bag());
        // starts at 50 charge, +1 per turn: acts at once (50), then needs 50 again (never 10)
        Assert.Single(r.Log, e => e.Action == BattleAction.SidekickAct);
    }

    [Fact]
    public void ForcedMisses_AtMostOneFullyAvoidedHitPerRound()
    {
        var c = new ContentDb();
        var id = Skill(c, "miss", On(Start, EffectKind.ForceMiss, target: TargetKind.Self, stacks: 3));
        var r = Fight(c, Worm(id), 5, Unit("e1", CombatRole.Enemy, 1_000_000, 10), Unit("e2", CombatRole.Enemy, 1_000_000, 10));
        int w = Index(r, "worm");
        int[] dodges = Enumerable.Range(1, 5).Select(rd => r.Log.Count(e => e.Round == rd && e.Target == w && e.Dodge)).ToArray();
        Assert.Equal(new[] { 1, 1, 1, 0, 0 }, dodges);
    }

    [Fact]
    public void DodgeCap_HigherVsAStatus_CeilingEightyFive()
    {
        var c = new ContentDb();
        var cap = Skill(c, "cap", Apply(Hit, StatusId.Chill), Rule("dodge_cap_vs", StatusId.Chill, 50));
        var plain = Skill(c, "plain", Apply(Hit, StatusId.Chill));
        double Rate(string skill)
        {
            var w = Worm(skill); w.Stats.Add(new StatMod(StatId.Dodge, 5000));
            var r = Fight(c, w, 400, Unit("e", CombatRole.Enemy, 1_000_000_000, 1));
            int wi = Index(r, "worm");
            var atk = r.Log.Where(e => e.Target == wi && e.Action == BattleAction.Basic).ToList();
            return atk.Count(e => e.Dodge) / (double)atk.Count;
        }
        // dodge decays after consecutive dodges, so the rate sits below the cap; the higher cap shows, but never past 85%
        double withCap = Rate(cap), without = Rate(plain);
        Assert.True(withCap > without, withCap + " vs " + without);
        Assert.True(withCap < 0.86, withCap.ToString());
    }

    [Fact]
    public void FreeRageAttacks_AtMostTwoPerTurn()
    {
        var c = new ContentDb();
        var id = Skill(c, "free", On(Hit, EffectKind.FreeRageAttack));
        var r = Fight(c, Worm(id), 3);
        int w = Index(r, "worm");
        for (int rd = 1; rd <= 3; rd++) Assert.Equal(2, Count(r, BattleAction.Rage, rd, w));
    }

    [Fact]
    public void Ticks_AtMostThreePerEnemyPerRound_FromEverySource()
    {
        var c = new ContentDb();
        var ids = new List<string> { Skill(c, "rad", Apply(Hit, StatusId.Poison, 10)) };
        for (int k = 0; k < 4; k++) ids.Add(Skill(c, "tick" + k, On(TriggerKind.OnTurnEnd, EffectKind.TickStatus, 100, status: StatusId.Poison)));
        var r = Fight(c, Worm(ids.ToArray()), 4);
        var perRound = Enumerable.Range(1, 4).Select(rd => r.Log.Count(e => e.Round == rd && e.Action == BattleAction.StatusTick)).ToArray();
        Assert.All(perRound, n => Assert.True(n <= 3, string.Join(",", perRound)));
        Assert.Contains(3, perRound);
    }

    [Fact]
    public void Echoes_ChainAtMostTwoDeep()
    {
        var c = new ContentDb();
        var id = Skill(c, "echo", On(TriggerKind.OnTurnEnd, EffectKind.Echo), On(TriggerKind.OnEcho, EffectKind.Echo));
        var r = Fight(c, Worm(id), 2);
        Assert.Equal(new[] { 1, 1, 2, 2 }, Rounds(r, BattleAction.Echo));
    }

    [Fact]
    public void Drones_AtMostFiveOnASide()
    {
        var c = new ContentDb();
        var id = Skill(c, "hive", On(Start, EffectKind.Summon, 25, TargetKind.Self, stacks: 9, param: "sk_drone"));
        var r = Fight(c, Worm(id), 1);
        Assert.Equal(5, r.Log.Count(e => e.Action == BattleAction.Summon));
    }

    [Fact]
    public void CrowdControl_OncePerEnemy_BossesLoseTwentyRage()
    {
        var c = new ContentDb();
        var id = Skill(c, "stun", On(Hit, EffectKind.ApplyStatus, status: StatusId.Stun, duration: 1, param: "per_target:1;boss_rage:20"));
        var r = Fight(c, Worm(id), 8, Bag("b1"));
        Assert.Single(r.Log, e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Stun);
        var boss = Unit("boss", CombatRole.Boss, 1_000_000, 0); boss.StartRage = 60;
        var rb = Fight(c, Worm(id), 8, boss);
        Assert.DoesNotContain(rb.Log, e => e.Status == StatusId.Stun);
        Assert.Single(rb.Log, e => e.Action == BattleAction.GainRage && e.Amount == -20 && e.SkillId == id);
    }

    [Fact]
    public void SureCrits_AndEmpower_OnTheNextAttacks()
    {
        var c = new ContentDb();
        var id = Skill(c, "keen", On(Start, EffectKind.SureCrit, target: TargetKind.Self, stacks: 2), On(Start, EffectKind.Empower, 100, TargetKind.Self));
        var r = Fight(c, Worm(id), 4);
        var basics = r.Log.Where(e => e.Action == BattleAction.Basic && e.Actor == Index(r, "worm")).ToList();
        Assert.Equal(new[] { true, true, false, false }, basics.Select(e => e.Crit));
        Assert.Equal(300, basics[0].Amount); // a crit (+50%) at double damage
        Assert.Equal(100, basics[3].Amount);
    }

    [Fact]
    public void ShieldBreak_Undo_AndOncePerEnemyTriggersFire()
    {
        var c = new ContentDb();
        var id = Skill(c, "guard", On(Start, EffectKind.Shield, 1, TargetKind.Self), On(TriggerKind.OnShieldBreak, EffectKind.GainRage, 7, TargetKind.Self),
            On(TriggerKind.OnDamaged, EffectKind.Rewind, 0, TargetKind.Self, maxPerBattle: 1), On(TriggerKind.OnUndo, EffectKind.GainRage, 11, TargetKind.Self));
        var w = Worm(id);
        var r = Fight(c, w, 3, Unit("e", CombatRole.Enemy, 1_000_000, 500));
        Assert.Single(r.Log, e => e.Action == BattleAction.GainRage && e.Amount == 7);
        Assert.Single(r.Log, e => e.Action == BattleAction.GainRage && e.Amount == 11);
    }

    [Fact]
    public void TheWormAttacksWithTheNeutralStyle_ArmsCarryNone()
    {
        var lo = new CombatLoadout();
        var side = BattleSimulator.BuildPlayerSide(lo, C);
        Assert.Equal(AttackStyle.Slam, side.Single(s => s.Role == CombatRole.Worm).Style);
    }

    // ---- Symbiote rework and part drops --------------------------------------------------------------------------------

    [Fact]
    public void Symbiote_AliensKeepTheirTurns_JoiningCostsFiftyCharge()
    {
        var sym = C.Skill("mythic_symbiote")!;
        Assert.DoesNotContain(sym.Triggers, t => t.Effect == EffectKind.Rule);
        Assert.Contains("Your rage attack also fires every alien's ability.", SkillText.Full(sym));
        Assert.Contains("Each alien that joins loses 50 charge.", SkillText.Full(sym));
        var t = sym.Triggers.Single();
        Assert.Equal((TriggerKind.OnRageAttack, EffectKind.AllyAction), (t.When, t.Effect));
        Assert.Contains("charge:-50", t.Param);
    }

    [Fact]
    public void BossFirstClear_GivesOneRareOrEpicPart_RepeatClearsDoNot()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e, seed: 3);
        RunTestKit.AutoToEnd(e, ref s);
        var parts = s.Run.LastSummary!.Rewards.Where(l => l.Reward.Kind == RewardKinds.Part).ToList();
        Assert.Single(parts);
        Assert.Contains(parts[0].Reward.Rarity, new[] { (int)Rarity.Rare, (int)Rarity.Epic });
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1" }).Ok);
        RunTestKit.AutoToEnd(e, ref s);
        Assert.DoesNotContain(s.Run.LastSummary!.Rewards, l => l.Reward.Kind == RewardKinds.Part);
    }
}
