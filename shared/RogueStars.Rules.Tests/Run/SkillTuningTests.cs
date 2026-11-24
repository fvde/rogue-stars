using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Skill tuning pass (owner): the level ladder (+15% → +25% → +40%), every level reads differently with the upgrade inline,
/// and every draftable run / laboratory skill provably changes a battle or a run (dead skills were removed or fixed).
/// </summary>
public class SkillTuningTests
{
    private static readonly ContentDb C = ContentDb.Default;

    /// <summary>Every skill a run can draft: run skills and every research level of every laboratory skill node.</summary>
    private static List<SkillDef> Pool(ContentDb c) =>
        c.Skills.Values.Where(s => s.Draftable && (s.Source == SkillSource.Run || s.Source == SkillSource.Laboratory))
            .OrderBy(s => s.Id, StringComparer.Ordinal).ToList();

    // ---- 1. the level ladder ----------------------------------------------------------------------------------------

    [Fact]
    public void TypicalSkills_Climb15_25_40_AndLegendaries25_40()
    {
        Assert.Equal(new[] { 15.0, 25, 40 }, Enumerable.Range(1, 3).Select(l => SkillLeveling.At(C, C.Skill("crit_keen"), l).Stats[0].Flat));
        Assert.Equal(new[] { 15.0, 25, 40 }, Enumerable.Range(1, 3).Select(l => SkillLeveling.At(C, C.Skill("gen_power"), l).Stats[0].Percent));
        Assert.Equal(new[] { 15.0, 25, 40 }, Enumerable.Range(1, 3).Select(l => SkillLeveling.At(C, C.Skill("gen_vitality"), l).Stats[0].Percent));
        Assert.Equal(new[] { 25.0, 40 }, Enumerable.Range(1, 2).Select(l => SkillLeveling.At(C, C.Skill("gen_overclocked"), l).Stats[0].Percent));
        // a trade-off's cost never scales (Overclocked Core: −15% max HP at every level)
        Assert.Equal(new[] { -15.0, -15 }, Enumerable.Range(1, 2).Select(l => SkillLeveling.At(C, C.Skill("gen_overclocked"), l).Stats[1].Percent));
        // tier caps and the tier rule are unchanged: Common Lv3, Legendary Lv2, Mythic Lv1
        Assert.Equal(3, C.RunTuning.MaxSkillLevelOf(Rarity.Common));
        Assert.Equal(2, C.RunTuning.MaxSkillLevelOf(Rarity.Legendary));
        Assert.Equal(1, C.RunTuning.MaxSkillLevelOf(Rarity.Mythic));
        Assert.All(Pool(C), s => Assert.True(s.Rarity is Rarity.Common or Rarity.Legendary or Rarity.Mythic, s.Id));
    }

    [Fact]
    public void UpgradeText_ShowsTheChangedNumbersInline_AndTheTargetLevel()
    {
        var keen = C.Skill("crit_keen")!;
        Assert.Equal("+15% → +25% crit chance.", SkillText.UpgradeFull(C, keen, 1, 2));
        Assert.Equal("+25% → +40% crit chance.", SkillText.UpgradeFull(C, keen, 2, 3));
        Assert.Equal("Keen Eye Lv 2", SkillText.Title(keen, 2));
        Assert.Equal("On crit: 30% → 50% chance to apply Heat ×3.", SkillText.UpgradeFull(C, C.Skill("fire_ember"), 1, 2));
        Assert.Equal("When hit: apply Radiation ×2 → ×3 to the attacker.", SkillText.UpgradeFull(C, C.Skill("poison_spit"), 1, 2));
        Assert.Equal("Rage attack consumes the target's Bleed: 25% → 40% ATK per stack, heal 50% of it. At max stacks, refund 50 rage.", SkillText.UpgradeFull(C, C.Skill("bleed_frenzy"), 1, 2));
        Assert.Equal("Supernova needs 12 Heat, but never hurts you and deals +25% → +40% damage.", SkillText.UpgradeFull(C, C.Skill("fire_controlled"), 1, 2));
        Assert.Equal("On rage attack: apply Freeze for 1 round → 2 rounds to all enemies.", SkillText.UpgradeFull(C, C.Skill("lab_frost_nova_5"), 1, 2));
        Assert.Equal("Run start: the boss awakens 9% → 15% slower.", SkillText.UpgradeFull(C, C.Skill("gen_calm_mind"), 1, 2));
        Assert.StartsWith("Level up: its numbers climb +15% → +25% → +40% (Legendary +25% → +40%)", SkillText.LevelUpRule(C));
    }

    /// <summary>Owner: "All upgrades need to read within text": every level of every draftable skill reads differently.</summary>
    [Fact]
    public void EveryLevel_OfEveryDraftableSkill_ReadsDifferently_WithArrowsOnTheChangedNumbers()
    {
        int upgrades = 0;
        foreach (var s in Pool(C))
        {
            Assert.False(string.IsNullOrWhiteSpace(SkillText.Full(s)), s.Id + " has no text");
            int max = C.RunTuning.MaxSkillLevelOf(s.Rarity);
            for (int lv = 2; lv <= max; lv++)
            {
                string a = SkillText.Full(C, s, lv - 1), b = SkillText.Full(C, s, lv);
                Assert.True(a != b, $"{s.Id} Lv{lv - 1} and Lv{lv} read the same: {a}");
                var lines = SkillText.Upgrade(C, s, lv - 1, lv);
                Assert.Contains(lines, l => l.Contains(SkillText.Arrow));
                // the arrow always sits between two numbers (never a whole line → another line)
                foreach (var l in lines.Where(l => l.Contains(SkillText.Arrow)))
                    foreach (var part in l.Split(SkillText.Arrow).Skip(1))
                        Assert.True(part.Length > 0 && (char.IsDigit(part[0]) || part[0] is '+' or '-' or '×'), $"{s.Id} Lv{lv}: {l}");
                upgrades++;
            }
        }
        Assert.True(upgrades > 100, upgrades.ToString());
    }

    [Fact]
    public void StatusStacks_GrowVisibly_WhenTheChanceIsCapped()
    {
        foreach (var s in Pool(C))
        {
            int max = C.RunTuning.MaxSkillLevelOf(s.Rarity);
            for (int lv = 2; lv <= max; lv++)
            {
                SkillDef a = SkillLeveling.At(C, s, lv - 1), b = SkillLeveling.At(C, s, lv);
                for (int i = 0; i < a.Triggers.Count; i++)
                {
                    SkillTrigger x = a.Triggers[i], y = b.Triggers[i];
                    Assert.True(y.Chance <= 100, s.Id);
                    if (!SkillLeveling.IsCountEffect(x.Effect) || x.Grow == GrowKind.Fixed) continue; // a Fixed part stays put by design
                    // a count trigger changes its chance, its stacks or its duration at every level
                    Assert.True(x.Chance != y.Chance || x.Stacks != y.Stacks || x.Duration != y.Duration || x.Value != y.Value, $"{s.Id} Lv{lv} trigger {i}");
                }
            }
        }
    }

    // ---- 2. removed / fixed skills ----------------------------------------------------------------------------------

    public static readonly string[] Removed =
    {
        "alien_mothership", "lab_drone_bay", "lab_drone_bay_5", "gen_hoarder", "lab_treasure_sense", "lab_treasure_sense_3",
    };

    [Fact]
    public void RemovedSkills_AreGone_AndNoDraftableSkillSummons()
    {
        foreach (var id in Removed) Assert.Null(C.Skill(id));
        Assert.Null(C.LabNode("res_drone_bay"));
        Assert.Null(C.LabNode("res_treasure_sense"));
        // (sidekick pass) a draftable summon launches a battle-scoped sidekick (Swarm Beacon's drones), never an enemy
        foreach (var s in Pool(C))
            Assert.All(s.Triggers.Where(t => t.Effect == EffectKind.Summon), t => Assert.NotNull(C.Sidekick(t.Param)));
    }

    /// <summary>
    /// Owner (signatures pass + skill redesign): one Mythic pool, no gated capstones. Every synergy (starters + packages) has its
    /// signature, 3 Common + 2 Legendary run skills and 3 themed Mythics (Alien: 4 Commons and 4 Mythics, owner decision), none
    /// gated by family counts or prerequisites.
    /// </summary>
    [Fact]
    public void EverySynergy_HasASignature_ThreeCommonsTwoLegendaries_ThreeMythics()
    {
        var synergies = C.RunTuning.StarterSynergies.Concat(C.LabResearch.Values.Where(d => d.Kind == LabNodeKind.Package).Select(d => d.Family)).ToList();
        Assert.Equal(8, synergies.Distinct().Count());
        Assert.DoesNotContain(SkillFamily.Crit, synergies);
        foreach (var f in synergies)
        {
            var run = Pool(C).Where(s => s.Family == f && s.Source == SkillSource.Run).ToList();
            bool alien = f == SkillFamily.AlienSynergy;
            Assert.Equal(alien ? 4 : 3, run.Count(s => s.Rarity == Rarity.Common));
            Assert.Equal(2, run.Count(s => s.Rarity == Rarity.Legendary));
            Assert.Equal(alien ? 4 : 3, run.Count(s => s.Rarity == Rarity.Mythic));
            Assert.Single(C.Skills.Values, s => s.Signature && s.Family == f);
        }
        Assert.All(Pool(C).Where(s => s.Rarity == Rarity.Mythic), s => Assert.True(s.RequiresFamilyCount == 0 && s.Requires.Count == 0, s.Id));
    }

    /// <summary>
    /// A % bonus on a point stat (crit, element damage, alien power …) multiplies a zero base: it did nothing (Kindling Heart's
    /// "+20% fire damage", the element and alien lab nodes). Point stats are flat in every run and lab skill and lab stat node.
    /// </summary>
    [Fact]
    public void PointStats_AreFlat_InRunAndLabSkills_AndLabStatNodes()
    {
        IEnumerable<StatMod> Mods(SkillDef s) => s.Stats.Concat(s.Triggers.Where(t => t.Stat != null).Select(t => t.Stat));
        foreach (var s in Pool(C))
            foreach (var m in Mods(s))
                Assert.True(!SkillText.IsPointStat(m.Stat) || m.Percent == 0, $"{s.Id}: {m.Stat} {m.Percent}%");
        foreach (var n in C.LabResearch.Values.Where(n => n.Kind == LabNodeKind.Stat))
            foreach (var lv in n.Levels)
                foreach (var m in lv.Stats)
                    Assert.True(!SkillText.IsPointStat(m.Stat) || m.Percent == 0, $"{n.Id}: {m.Stat} {m.Percent}%");
    }

    [Fact]
    public void SavedRuns_AndLabs_WithRemovedIds_KeepWorking()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        foreach (var id in new[] { "alien_mothership", "lab_drone_bay_3", "gen_hoarder", "lab_treasure_sense_2" })
        {
            run.Skills.Add(id);
            run.SkillLevels[id] = 2;
            run.Gains.Add(new RunGain("draft", id, 1));
        }
        run.Loadout.DraftPoolSkillIds.Add("lab_drone_bay_3");
        Assert.NotNull(RunSystem.WormStats(e.Content, run));
        Assert.Equal(0, RunDraft.FamilyCount(e.Content, run, SkillFamily.AlienSynergy));
        RunTestKit.AutoToEnd(e, ref s);
        Assert.True(s.Run.LastSummary!.Won);

        // a research still running on a removed node is stopped (it would block the slot for ever)
        var e2 = RunTestKit.Engine();
        var s2 = TestHelpers.NewPlayer(e2, 7);
        s2.Lab.Entries["res_drone_bay"] = new LabEntry { Level = 2, ResearchingLevel = 3, TimerId = "t-x", ReadyAtMs = TestHelpers.T0 + 99_000_000 };
        s2.Lab.Entries["res_treasure_sense"] = new LabEntry { Level = 5, Enabled = true };
        RunTestKit.Exec(e2, ref s2, new LabToggle { ResearchId = "res_eagle_eye", Enabled = true });
        Assert.Equal(0, s2.Lab.Entries["res_drone_bay"].ResearchingLevel);
        Assert.Equal(0, LaboratorySystem.Running(s2));
        Assert.DoesNotContain(LaboratorySystem.ActiveSkillIds(s2, e2.Content), id => e2.Content.Skill(id) == null);
    }

    // ---- 3. every skill does something --------------------------------------------------------------------------

    /// <summary>Board skills: proven by their own run tests below; every other draftable skill acts in battle.</summary>
    private static readonly HashSet<string> BoardSkills = new() { "gen_wealth", "gen_pathfinder", "gen_camper", "gen_growth", "gen_calm_mind", "gen_scholar", "gen_ambush" };

    internal static bool IsBoardSkill(SkillDef s) => BoardSkills.Contains(s.Id) || s.Id.StartsWith("lab_lucky_dice", StringComparison.Ordinal);

    /// <summary>
    /// A test kit on the worm so that element / status stats have something to boost: one family-tagged skill per element
    /// (its statuses carry the element's DoT; Ice and Lightning also hit with element damage).
    /// </summary>
    internal static ContentDb KitContent()
    {
        var c = new ContentDb();
        void Kit(string id, SkillFamily fam, params SkillTrigger[] t)
        {
            var s = new SkillDef { Id = id, Name = id, Family = fam, Source = SkillSource.Building, Draftable = false };
            s.Triggers.AddRange(t);
            c.AddSkill(s);
        }
        SkillTrigger St(StatusId st, double chance) => new() { When = TriggerKind.OnHit, Effect = EffectKind.ApplyStatus, Status = st, Chance = chance };
        SkillTrigger Dmg(double chance) => new() { When = TriggerKind.OnAttack, Effect = EffectKind.Damage, Value = 20, Chance = chance };
        Kit("kit_fire", SkillFamily.Fire, St(StatusId.Heat, 100)); // Ignite-like: Supernovas happen, so Heat / Supernova skills have something to change
        Kit("kit_ice", SkillFamily.Ice, St(StatusId.Freeze, 10), Dmg(25));
        Kit("kit_light", SkillFamily.Lightning, St(StatusId.Shock, 30), Dmg(25));
        Kit("kit_poison", SkillFamily.Poison, St(StatusId.Poison, 30));
        Kit("kit_bleed", SkillFamily.Bleed, St(StatusId.Bleed, 30));
        // a control: the old Kindling Heart's "+20% fire damage" (a % of a zero base) must read as doing nothing
        var dead = new SkillDef { Id = "dead_probe", Name = "Dead", Family = SkillFamily.Fire, Source = SkillSource.Building, Draftable = false };
        dead.Stats.Add(new StatMod(StatId.FireDamage, 0, 20));
        c.AddSkill(dead);
        // a frail worm: rescues (Last Word, Sacrifice Protocol, Rewind Shell, Loop) and low-HP rules need a fight it can lose
        c.AddSkill(new SkillDef { Id = "kit_frail", Name = "Frail", Source = SkillSource.Building, Draftable = false, Stats = { new StatMod(StatId.Hp, 0, -70) } });
        // a squad that guards every round: block-hooked skills (Decoy) are judged where blocks happen
        c.AddSkill(new SkillDef { Id = "kit_guards", Name = "Guards", Source = SkillSource.Building, Draftable = false,
            Triggers = { new SkillTrigger { When = TriggerKind.OnRoundStart, Effect = EffectKind.Block, Stacks = 1, Target = TargetKind.Self } } });
        // heavy stackers: skills gated on many stacks (20 Radiation, max Bleed / Shock) are judged where those stacks happen
        foreach (var st in new[] { StatusId.Poison, StatusId.Bleed, StatusId.Shock })
            c.AddSkill(new SkillDef { Id = "kit_heavy_" + st, Name = "Heavy " + st, Source = SkillSource.Building, Draftable = false,
                Triggers = { new SkillTrigger { When = TriggerKind.OnHit, Effect = EffectKind.ApplyStatus, Status = st, Stacks = 3, Chance = 100 } } });
        return c;
    }

    /// <summary>
    /// The build a skill is judged in (skill redesign): a run skill sits next to its synergy's signature (every run has one, and
    /// Arc / Chill / Flurry skills build on it) and the source its offer gate needs (SkillDef.Needs); rescues get a fight they
    /// can lose. The baseline is the same build without the skill.
    /// </summary>
    internal static List<string> Context(ContentDb c, SkillDef s)
    {
        var ctx = new List<string>();
        void Add(string id) { if (id != s.Id && c.Skill(id) != null && !ctx.Contains(id)) ctx.Add(id); }
        if (s.Source != SkillSource.Run && s.Source != SkillSource.Laboratory) return ctx;
        if (s.Family != SkillFamily.None)
        {
            var sig = c.Skills.Values.Where(x => x.Signature && x.Family == s.Family).OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (sig != null && !s.Signature) Add(sig.Id);
        }
        if (s.Family == SkillFamily.Lightning) Add("light_spark");  // Shock for Overclock / Thunderclap / Capacitor
        if (s.Family == SkillFamily.Combo) Add("combo_flurry");     // combo chains for Whirlwind / Fury Chain
        switch (s.Needs)
        {
            case "arc": Add("sig_lightning"); break;
            case "applies:Shock": Add("sig_lightning"); Add("light_spark"); break;
            case "consume:Bleed": Add("sig_bleed"); Add("bleed_frenzy"); break;
            case "blocks": Add("kit_guards"); break;
            case "echo": Add("sig_time"); break;
        }
        foreach (var st in new[] { StatusId.Poison, StatusId.Bleed, StatusId.Shock })
        {
            string n = st.ToString();
            bool many = s.Triggers.Any(t => (t.If ?? "").Contains("max:" + n) || (t.When == TriggerKind.OnStackThreshold && (t.Param ?? "").StartsWith(n + ":"))
                || (t.Effect == EffectKind.Rule && t.Status == st && t.Param == "max"));
            if (many) Add("kit_heavy_" + n);
        }
        bool rescue = s.Triggers.Any(t => t.When == TriggerKind.OnFatal || t.Effect == EffectKind.Rewind || t.Param == "last_light:30" || t.Effect == EffectKind.LoseHp);
        if (rescue) Add("kit_frail");
        return ctx;
    }

    internal static readonly string[] KitIds = { "kit_fire", "kit_ice", "kit_light", "kit_poison", "kit_bleed" };

    /// <summary>A worm with a squad and a mount, base crit / combo / counter / parry / dodge, against a shielded slug, a dodging wraith and a boss.</summary>
    internal static BattleResult Fight(ContentDb c, string? skillId, int seed, bool brawl, int level = 1, IEnumerable<string>? extra = null)
    {
        var lo = new CombatLoadout { DisplayName = "Worm", MountId = "test_mount" };
        lo.WormStats.AddAll(new[]
        {
            new StatMod(StatId.Hp, brawl ? 1100 : 2600), new StatMod(StatId.Atk, 100), new StatMod(StatId.Def, 20), new StatMod(StatId.Speed, 100),
            new StatMod(StatId.CritChance, 10), new StatMod(StatId.CritDamage, 50), new StatMod(StatId.ComboChance, 15),
            new StatMod(StatId.CounterChance, 15), new StatMod(StatId.ParryChance, 20), new StatMod(StatId.Dodge, 10),
        });
        lo.MountStats.Add(new StatMod(StatId.Atk, 25)).Add(new StatMod(StatId.Hp, 80));
        for (int i = 0; i < 2; i++)
        {
            var a = new CombatAlien { AlienTypeId = "alien_" + i, Name = "Alien " + i };
            a.Stats.Add(new StatMod(StatId.Hp, 300)).Add(new StatMod(StatId.Atk, 30)).Add(new StatMod(StatId.Def, 8)).Add(new StatMod(StatId.Speed, 45));
            a.SkillIds.Add(i == 0 ? "ask_voidmaw_1" : "ask_blorp_1"); // sidekicks: a striker and a bleeder (sidekick pass)
            lo.Aliens.Add(a);
        }
        var skills = new List<string>(KitIds);
        if (extra != null) skills.AddRange(extra);
        if (skillId != null) skills.Add(skillId);
        var setup = new BattleSetup();
        var levels = new Dictionary<string, int>();
        if (skillId != null) levels[skillId] = level;
        setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, skills, null, levels));
        setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "magma_slug", 1.5));
        setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "ash_wraith", 1.5));
        setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "boss_frost_leviathan", brawl ? 1.2 : 1.0));
        return BattleSimulator.Simulate(setup, c, new Rng((ulong)(1000 + seed)));
    }

    internal static string Signature(BattleResult r) =>
        r.Winner + "|" + r.Rounds + "|" + r.PlayerDamageDealt + "|" + r.PlayerDamageTaken + "|" + Math.Round(r.WormHpLeft)
        + "|" + string.Join(",", r.Fighters.Select(f => f.DamageDealt + ":" + f.EndHp + ":" + f.EndRage));

    internal static bool LandsEffect(BattleResult r, string skillId, SkillTrigger t)
    {
        foreach (var e in r.Log)
        {
            if (e.SkillId != skillId) continue;
            switch (t.Effect)
            {
                case EffectKind.Damage:
                case EffectKind.TrueDamage: if (e.Action == BattleAction.Skill && e.Amount > 0) return true; break;
                case EffectKind.ApplyStatus: if (e.Action == BattleAction.StatusApplied && e.Status == t.Status) return true; break;
                case EffectKind.Heal: if (e.Action == BattleAction.Heal && e.Amount > 0) return true; break;
                case EffectKind.Shield: if (e.Action == BattleAction.Shield && e.Amount > 0) return true; break;
                case EffectKind.GainRage: if (e.Action == BattleAction.GainRage && e.Amount > 0) return true; break;
                case EffectKind.BuffStat:
                case EffectKind.Strike:
                case EffectKind.AllyAction: if (e.Action == BattleAction.Buff) return true; break;
                case EffectKind.Summon: if (e.Action == BattleAction.Summon) return true; break;
                case EffectKind.Block: if (e.Action == BattleAction.Guard) return true; break;
                case EffectKind.RemoveStatus:
                case EffectKind.ConsumeStatus: if (e.Action == BattleAction.StatusRemoved && e.Status == t.Status) return true; break;
                // skill redesign effects
                case EffectKind.BlockStatus: if (e.Action == BattleAction.StatusBlocked && e.Status == t.Status) return true; break;
                case EffectKind.ForceSupernova: if (e.Action == BattleAction.Supernova) return true; break;
                case EffectKind.MultiplyStatus:
                case EffectKind.TransferStatus: if (e.Action == BattleAction.StatusApplied && e.Status == t.Status) return true; break;
                case EffectKind.StackAll: if (e.Action == BattleAction.StatusApplied) return true; break;
                case EffectKind.TickStatus: if (e.Action == BattleAction.StatusTick && e.Amount > 0) return true; break;
                case EffectKind.Execute: if (e.Action == BattleAction.Execute) return true; break;
                case EffectKind.Reflect:
                case EffectKind.LoseHp: if (e.Action == BattleAction.Skill && e.Amount > 0) return true; break;
                case EffectKind.CheatDeath: if (e.Action == BattleAction.CheatDeath) return true; break;
                case EffectKind.FreeRageAttack:
                case EffectKind.Charge: if (e.Action == BattleAction.Buff) return true; break;
                case EffectKind.Sacrifice: if (e.Action == BattleAction.Sacrifice) return true; break;
                case EffectKind.Echo: if (e.Action == BattleAction.Echo) return true; break;
                case EffectKind.Rewind: if (e.Action == BattleAction.Heal && e.Amount > 0) return true; break;
                case EffectKind.Loop: if (e.Action == BattleAction.Loop) return true; break;
            }
        }
        return false;
    }

    /// <summary>
    /// Owner: "remove skills that don't work / do anything". Every battle skill (each at Lv1, run and lab) changes the battle's
    /// outcome on some seed, and every one of its battle triggers lands its effect (logged with the skill's id).
    /// </summary>
    [Fact]
    public void EveryBattleSkill_ChangesTheBattle_AndEveryTriggerLandsItsEffect()
    {
        var c = KitContent();
        var baseline = new Dictionary<(int, bool), string>();
        var failures = new List<string>();
        for (int seed = 0; seed < 8; seed++)
            foreach (bool brawl in new[] { false, true })
            {
                baseline[(seed, brawl)] = Signature(Fight(c, null, seed, brawl));
                Assert.Equal(baseline[(seed, brawl)], Signature(Fight(c, "dead_probe", seed, brawl))); // the check catches dead skills
            }
        var ctxBase = new Dictionary<(int, bool, string), string>();
        foreach (var s in Pool(c).Where(s => !IsBoardSkill(s)))
        {
            bool changed = false;
            var landed = new bool[s.Triggers.Count];
            var ctx = Context(c, s);
            string key = string.Join(",", ctx);
            foreach (bool brawl in new[] { false, true })
                for (int seed = 0; seed < 8; seed++)
                {
                    if (!ctxBase.TryGetValue((seed, brawl, key), out var b)) ctxBase[(seed, brawl, key)] = b = Signature(Fight(c, null, seed, brawl, 1, ctx));
                    var r = Fight(c, s.Id, seed, brawl, 1, ctx);
                    if (Signature(r) != b) changed = true;
                    for (int i = 0; i < s.Triggers.Count; i++) if (!landed[i] && LandsEffect(r, s.Id, s.Triggers[i])) landed[i] = true;
                }
            if (!changed) failures.Add(s.Id + ": no battle changed");
            for (int i = 0; i < s.Triggers.Count; i++)
            {
                var t = s.Triggers[i];
                bool cond = t.When == TriggerKind.Passive && (t.Effect == EffectKind.Rule || t.Param != null && t.Param.StartsWith("cond:", StringComparison.Ordinal));
                if (!cond && !landed[i]) failures.Add(s.Id + ": trigger " + t.When + " " + t.Effect + " never landed");
            }
            // every level-up changes a battle too (no dead upgrades)
            for (int lv = 2; lv <= C.RunTuning.MaxSkillLevelOf(s.Rarity); lv++)
            {
                bool moved = false;
                foreach (bool brawl in new[] { false, true })
                    for (int seed = 0; seed < 40 && !moved; seed++) // chance-on-crit and first-turn upgrades need more seeds
                        moved = Signature(Fight(c, s.Id, seed, brawl, lv, ctx)) != Signature(Fight(c, s.Id, seed, brawl, lv - 1, ctx));
                if (!moved) failures.Add(s.Id + ": Lv" + lv + " changes no battle");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>The statuses the skills apply each change a fight: skipped turns, charm (an enemy hits its own side), damage taken, DoTs.</summary>
    [Theory]
    [InlineData(StatusId.Heat)]
    [InlineData(StatusId.Chill)]
    [InlineData(StatusId.Poison)]
    [InlineData(StatusId.Bleed)]
    [InlineData(StatusId.Shock)]
    [InlineData(StatusId.Freeze)]
    [InlineData(StatusId.Stun)]
    [InlineData(StatusId.Charm)]
    public void EveryStatus_ChangesTheFight(StatusId status)
    {
        var c = new ContentDb();
        var s = new SkillDef { Id = "apply_it", Name = "Apply", Source = SkillSource.Building, Draftable = false };
        s.Triggers.Add(new SkillTrigger { When = TriggerKind.OnHit, Effect = EffectKind.ApplyStatus, Status = status, Chance = 100, Stacks = status == StatusId.Heat ? 4 : 1 });
        c.AddSkill(s);
        BattleResult Run(bool with)
        {
            var lo = new CombatLoadout { DisplayName = "Worm" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 3000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Def, 10)).Add(new StatMod(StatId.Speed, 200));
            var setup = new BattleSetup { RoundCap = 6 };
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, with ? new[] { "apply_it" } : null));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "glacier_golem", 3));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "magma_slug", 3));
            return BattleSimulator.Simulate(setup, c, new Rng(5UL));
        }
        BattleResult without = Run(false), with = Run(true);
        Assert.Contains(with.Log, e => e.Action == BattleAction.StatusApplied && e.Status == status);
        Assert.NotEqual(Signature(without), Signature(with));
        var def = c.Combat.Statuses[status];
        if (def.SkipsTurn || def.Charms || def.DamageDealtPctPerStack < 0) Assert.True(with.PlayerDamageTaken < without.PlayerDamageTaken, $"{with.PlayerDamageTaken} vs {without.PlayerDamageTaken}");
        else Assert.True(with.PlayerDamageDealt > without.PlayerDamageDealt, $"{with.PlayerDamageDealt} vs {without.PlayerDamageDealt}");
        if (def.Charms) // a charmed enemy attacks its own side
            Assert.Contains(with.Log, e => e.Action == BattleAction.Basic && e.Actor >= 0 && e.Target >= 0
                && with.Fighters[e.Actor].Side == BattleSide.Enemy && with.Fighters[e.Target].Side == BattleSide.Enemy);
    }

    // ---- board skills ----

    /// <summary>One roll on a line board of <paramref name="tile"/> tiles, with or without the skill (granted like a draft pick).</summary>
    private static ActiveRun Roll(string? skillId, string tile, Action<ActiveRun>? setup = null, int rolls = 1, long seed = 42, Action<ActiveRun>? after = null)
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e, seed: seed);
        var run = s.Run.Active!;
        if (skillId != null)
        {
            if (C.Skill(skillId)!.Source == SkillSource.Laboratory) run.Loadout.DraftPoolSkillIds.Add(skillId);
            run.Phase = RunPhase.Draft; run.MythicDraft = false; run.DraftOffer.Clear(); run.DraftOffer.Add(skillId); run.PendingDrafts = 1;
            Assert.True(RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = skillId }).Ok);
            run = s.Run.Active!;
        }
        RunTestKit.LineBoard(run, Enumerable.Repeat(tile, 60).ToArray());
        setup?.Invoke(run);
        for (int i = 0; i < rolls; i++)
        {
            run = s.Run.Active!;
            run.Phase = RunPhase.AwaitRoll; run.EventId = null; run.PendingDrafts = 0; run.PendingMythicDrafts = 0;
            var r = RunTestKit.Exec(e, ref s, new RunRoll());
            Assert.True(r.Ok, r.Error);
            after?.Invoke(s.Run.Active!);
        }
        return s.Run.Active!;
    }

    [Fact]
    public void Prospector_WonBattlesPayCoins()
    {
        var without = Roll(null, "enemy_1");
        var with = Roll("gen_wealth", "enemy_1");
        Assert.Equal(1, with.BattlesWon);
        Assert.Equal(12, with.Coins - without.Coins);
    }

    [Fact]
    public void Pathfinder_PassedTilesPayCoins()
    {
        int passed = 0;
        var without = Roll(null, "empty", rolls: 3);
        var with = Roll("gen_pathfinder", "empty", rolls: 3, after: r => passed += r.LastDice.Sum() - 1); // every step but the landing one
        Assert.True(passed > 0);
        Assert.Equal(3 * passed, with.Coins - without.Coins);
    }

    [Fact]
    public void Camper_CampfiresHealMore()
    {
        void Hurt(ActiveRun r) => r.WormHp = r.WormMaxHp * 0.3;
        var without = Roll(null, "campfire", Hurt);
        var with = Roll("gen_camper", "campfire", Hurt);
        Assert.True(with.WormHp - without.WormHp >= with.WormMaxHp * 0.15 - 1e-6, $"{with.WormHp} vs {without.WormHp}");
    }

    [Fact]
    public void GrowthSpurt_LevelUpsGrowTheRunStats()
    {
        void NearLevel(ActiveRun r) => r.Xp = RunSystem.XpNeeded(C, r.Level) - 1;
        var without = Roll(null, "enemy_1", NearLevel);
        var with = Roll("gen_growth", "enemy_1", NearLevel);
        Assert.Equal(2, with.Level);
        Assert.Equal(without.RunStats.Count + 2, with.RunStats.Count);
        Assert.True(RunSystem.WormStats(C, with).Atk > RunSystem.WormStats(C, without).Atk);
    }

    [Fact]
    public void CalmMind_SlowsTheBoss()
    {
        var without = Roll(null, "empty", rolls: 3);
        var with = Roll("gen_calm_mind", "empty", rolls: 3);
        Assert.True(without.Awakening > 0);
        Assert.True(with.Awakening < without.Awakening * 0.95, $"{with.Awakening} vs {without.Awakening}");
    }

    /// <summary>Ambush Instinct (R14): landing on an enemy tile starts that battle with rage (15 → 25 → 40).</summary>
    [Fact]
    public void AmbushInstinct_EnemyTileBattles_StartWithRage()
    {
        long Start(ActiveRun r) => r.LastBattle!.Fighters.First(f => f.Role == CombatRole.Worm).StartRage;
        Assert.Equal(0, Start(Roll(null, "enemy_1")));
        Assert.Equal(15, Start(Roll("gen_ambush", "enemy_1")));
        Assert.Equal(new[] { 15.0, 25, 40 }, Enumerable.Range(1, 3).Select(l => SkillLeveling.At(C, C.Skill("gen_ambush"), l).Triggers[0].Value));
    }

    [Fact]
    public void Scholar_BattlesGiveMoreXp()
    {
        var without = Roll(null, "enemy_1");
        var with = Roll("gen_scholar", "enemy_1");
        Assert.True(with.XpTotal > without.XpTotal, $"{with.XpTotal} vs {without.XpTotal}");
    }

    [Fact]
    public void LuckyDice_RollsExtraDice()
    {
        int maxDice(string? skill) { int m = 0; Roll(skill, "empty", rolls: 12, after: r => m = Math.Max(m, r.LastDice.Count)); return m; }
        Assert.Equal(1, maxDice(null));
        Assert.True(maxDice("lab_lucky_dice_5") > 1); // 60% per roll: an extra die on the next roll
        Assert.True(maxDice("lab_lucky_dice") > 1);   // 20%
    }

    [Fact]
    public void EveryDraftableSkill_IsCoveredByAnOutcomeTest()
    {
        // battle skills run through EveryBattleSkill_…; the board skills have their own tests above
        foreach (var s in Pool(C).Where(IsBoardSkill).Where(s => s.Id != "gen_scholar"))
            Assert.Contains(s.Triggers, t => t.When is TriggerKind.OnBattleWon or TriggerKind.OnTilePass or TriggerKind.OnTileLand
                or TriggerKind.OnLevelUp or TriggerKind.OnRunStart or TriggerKind.OnRoll);
        Assert.Equal(new[] { "gen_scholar" }, Pool(C).Where(IsBoardSkill).Where(s => s.Triggers.Count == 0).Select(s => s.Id));
    }
}
