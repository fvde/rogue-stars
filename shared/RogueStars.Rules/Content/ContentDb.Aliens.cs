using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>The alien's ability from Stars on (replaces the previous step). Aliens pass: one step per star (1★ … 10★).</summary>
    [Serializable]
    public sealed class AlienSkillStep
    {
        public int Stars;
        public string SkillId;
        public AlienSkillStep() { }
        public AlienSkillStep(int stars, string skillId) { Stars = stars; SkillId = skillId; }
    }

    /// <summary>
    /// An alien species (aliens pass, owner-approved review rev 4.3): a sidekick with NO stats of its own. Its one ability works in % of
    /// the worm's ATK (hits, statuses) or max HP (heals, shields); Speed sets how often it acts; stars (1★–10★) are its only progression.
    /// </summary>
    [Serializable]
    public sealed class AlienTypeDef
    {
        public string Id;
        public string Name;
        public string Description;
        public Rarity Rarity;
        /// <summary>Its main synergy (the first of <see cref="Synergies"/>).</summary>
        public SkillFamily Family;
        /// <summary>The synergies its ability serves (squad screen badges): Pebblehorn Counter + Alien, Nebulisk four.</summary>
        public List<SkillFamily> Synergies = new List<SkillFamily>();
        /// <summary>The ability's name ("Thorn Lash"); every star step carries it.</summary>
        public string AbilityName;
        /// <summary>The ability per star: 10 steps (1★ … 10★), each its own skill with the star's numbers.</summary>
        public List<AlienSkillStep> ArchetypeSkills = new List<AlienSkillStep>();
        /// <summary>Charge per round (Combat.SidekickActAt = 100): 34 acts about every 3 rounds, 50 every 2.</summary>
        public double BaseSpeed;
        /// <summary>Work power: percent production bonus at 1★ (stars raise it, AlienTuning.StarWorkPct).</summary>
        public double BaseWork;
        /// <summary>Traits this species rolls (pool: Swift or Eager, its effect trait(s), one work trait). Mutations convert into it.</summary>
        public List<string> PossibleTraits = new List<string>();
    }

    /// <summary>
    /// A summoned sidekick (drones): exists for one battle, never attacked, charges by Speed and uses its one ability. Its hits are a %
    /// of the worm's ATK (the Summon trigger's Value) × the side's alien power.
    /// </summary>
    [Serializable]
    public sealed class SidekickDef
    {
        public string Id;
        public string Name;
        public string Description;
        public double Speed;
        public string AbilitySkillId;
    }

    /// <summary>What a trait does (aliens pass): city work, the alien's tempo, its ability's effect, or an old id kept for saves.</summary>
    public enum AlienTraitKind { Work, Tempo, Effect, Legacy }

    [Serializable]
    public sealed class AlienTraitDef
    {
        public string Id;
        public string Name;
        public string Description;
        public AlienTraitKind Kind;
        /// <summary>Roll weight for initial rolls and mutations (0: never rolled, e.g. a legacy trait).</summary>
        public double Weight = 10;
        /// <summary>Combat: stat mods on the sidekick (Swift: +15% Speed; Vampiric: lifesteal that heals the worm).</summary>
        public List<StatMod> CombatStats = new List<StatMod>();
        /// <summary>Work: + percent production when working in a building producing one of WorkCurrencies (empty = any).</summary>
        public double WorkPct;
        public List<string> WorkCurrencies = new List<string>();
        /// <summary>Legacy traits (pre aliens pass): what they become, the first one in the species' pool wins (save migration, breeding).</summary>
        public List<string> RemapTo = new List<string>();
    }

    [Serializable]
    public sealed class AlienTuningDef
    {
        /// <summary>Work power growth in percent per star above 1 (stars are the only progression).</summary>
        public double StarWorkPct = 40;
        /// <summary>Initial gene roll: uniform in [−InitialGeneSpread, +InitialGeneSpread]. Genes always clamp to [GeneMin, GeneMax]. Genes are inactive (coming soon).</summary>
        public double InitialGeneSpread = 15, GeneMin = -30, GeneMax = 60;
        /// <summary>Chance for the 1st, 2nd, 3rd trait when a new alien is rolled.</summary>
        public double[] TraitChances = { 1.0, 0.35, 0.1 };
        public int MaxTraits = 3;
        /// <summary>Tier stars: an ability's effect (stacks, targets, chances) changes only here; between them only its main % grows.</summary>
        public int[] SkillStars = { 1, 3, 5, 7, 10 };
        /// <summary>Effect traits: Twin +1 stack on every TwinEvery-th action; Wide's extra target at WidePct %; Lingering +LingeringRounds;
        /// Bulwark +BulwarkPct % on guards and shields; guards are capped at GuardCapPct.</summary>
        public int TwinEvery = 2, LingeringRounds = 1;
        public double WidePct = 50, BulwarkPct = 20, GuardCapPct = 75;
    }

    public sealed partial class ContentDb
    {
        public readonly Dictionary<string, AlienTypeDef> AlienTypes = new Dictionary<string, AlienTypeDef>();
        public readonly Dictionary<string, AlienTraitDef> AlienTraits = new Dictionary<string, AlienTraitDef>();
        /// <summary>Trait ids in definition order (deterministic rolls).</summary>
        public readonly List<string> AlienTraitOrder = new List<string>();
        public readonly AlienTuningDef AlienTuning = new AlienTuningDef();

        public AlienTypeDef AlienType(string id) => id != null && AlienTypes.TryGetValue(id, out var a) ? a : null;
        /// <summary>Battle-scoped sidekicks (drones) by id (EffectKind.Summon on the player side).</summary>
        public readonly Dictionary<string, SidekickDef> Sidekicks = new Dictionary<string, SidekickDef>();
        public SidekickDef Sidekick(string id) => id != null && Sidekicks.TryGetValue(SummonId(id), out var d) ? d : null;
        public AlienTraitDef AlienTrait(string id) => id != null && AlienTraits.TryGetValue(id, out var t) ? t : null;

        /// <summary>The sidekick id of a Summon Param ("sk_drone;max:2" → "sk_drone").</summary>
        public static string SummonId(string param)
        {
            if (string.IsNullOrEmpty(param)) return param;
            int i = param.IndexOf(';');
            return i < 0 ? param : param.Substring(0, i);
        }

        // ---- trait ids (aliens pass) ----
        public const string TraitSwift = "trait_swift", TraitEager = "trait_eager", TraitTwin = "trait_twin", TraitWide = "trait_wide",
            TraitLingering = "trait_lingering", TraitBulwark = "trait_bulwark", TraitVampiric = "trait_vampiric";

        partial void BuildAliens()
        {
            // ---- traits: work ----
            AlienWorkTrait("trait_green_thumb", "Green Thumb", 20, 12, Currencies.Biomass, Currencies.Grain);
            AlienWorkTrait("trait_deep_digger", "Deep Digger", 20, 12, Currencies.Ore, Currencies.Diamonds, Currencies.AdvancementPlates);
            AlienWorkTrait("trait_tinkerer", "Tinkerer", 20, 10, Currencies.Fuel, Currencies.GearCores);
            AlienWorkTrait("trait_scholar", "Scholar", 15, 8, Currencies.HeroXp, Currencies.BlueprintTokens, Currencies.AlienTokens);
            AlienWorkTrait("trait_tireless", "Tireless", 10, 10);
            AlienWorkTrait("trait_prodigy", "Prodigy", 18, 3);
            // ---- traits: combat (aliens pass) shape tempo or the effect, never stats ----
            AlienCombatTrait(TraitSwift, "Swift", AlienTraitKind.Tempo, 10, "+15% Speed: acts more often.", new StatMod(StatId.Speed, 0, 15));
            AlienCombatTrait(TraitEager, "Eager", AlienTraitKind.Tempo, 10, "Its first action comes at battle start.");
            AlienCombatTrait(TraitTwin, "Twin", AlienTraitKind.Effect, 8, "+1 stack on every 2nd action.");
            AlienCombatTrait(TraitWide, "Wide", AlienTraitKind.Effect, 8, "Its hit also strikes 1 random other enemy at half.");
            AlienCombatTrait(TraitLingering, "Lingering", AlienTraitKind.Effect, 8, "Its buff, Chill, Shock or guard lasts longer (+1 round, a guard +1 hit).");
            AlienCombatTrait(TraitBulwark, "Bulwark", AlienTraitKind.Effect, 8, "Its guards and shields +20%.");
            AlienCombatTrait(TraitVampiric, "Vampiric", AlienTraitKind.Effect, 5, "Its damage heals the worm 10% of it.", new StatMod(StatId.LifeSteal, 10));
            // ---- legacy combat traits (ids kept for saves): migrated by species (AlienSystem.ConvertTrait) ----
            AlienLegacyTrait("trait_tough", "Tough", TraitBulwark, TraitLingering);
            AlienLegacyTrait("trait_armored", "Armored", TraitBulwark, TraitLingering);
            AlienLegacyTrait("trait_fierce", "Fierce", TraitTwin, TraitWide);
            AlienLegacyTrait("trait_lucky", "Lucky", TraitSwift);
            AlienLegacyTrait("trait_pyromaniac", "Pyromaniac", TraitTwin);
            AlienLegacyTrait("trait_stormborn", "Stormborn", TraitTwin);

            // ---- species: the approved per-alien table (review rev 4.3), every number explicit per star ----
            BuildAlienSpecies();

            // ---- battle-scoped sidekicks (Hive Carrier drones, Mothership Link, Swarm Beacon, Swarmheart): the zap is 100% of the
            // drone's power = the summon's Value % of the worm's ATK ----
            AddSidekick("sk_drone", "Drone", 40, "Zaps the front enemy.",
                new SkillTrigger { When = TriggerKind.OnAct, Effect = EffectKind.Damage, Target = TargetKind.Enemy, Value = 100 });
        }

        // ---- content helpers (aliens) --------------------------------------------------------

        private void AddSidekick(string id, string name, double speed, string description, params SkillTrigger[] ability)
        {
            var skill = new SkillDef
            {
                Id = "skab_" + id.Replace("sk_", ""), Name = name + " Zap", Description = description, Family = SkillFamily.AlienSynergy,
                Rarity = Rarity.Common, Source = SkillSource.Alien, Draftable = false, Text = "Zap the front enemy for its power.",
            };
            skill.Triggers.AddRange(ability);
            AddSkill(skill);
            Sidekicks.Add(id, new SidekickDef { Id = id, Name = name, Speed = speed, Description = description, AbilitySkillId = skill.Id });
        }

        private void AlienWorkTrait(string id, string name, double pct, double weight, params string[] currencies)
        {
            var t = new AlienTraitDef { Id = id, Name = name, WorkPct = pct, Weight = weight, Kind = AlienTraitKind.Work };
            t.WorkCurrencies.AddRange(currencies);
            t.Description = "+" + pct + "% production" + (currencies.Length > 0 ? " of " + string.Join("/", currencies) : "") + " when working.";
            AlienTraits.Add(id, t);
            AlienTraitOrder.Add(id);
        }

        private void AlienCombatTrait(string id, string name, AlienTraitKind kind, double weight, string description, params StatMod[] mods)
        {
            var t = new AlienTraitDef { Id = id, Name = name, Weight = weight, Kind = kind, Description = description };
            t.CombatStats.AddRange(mods);
            AlienTraits.Add(id, t);
            AlienTraitOrder.Add(id);
        }

        private void AlienLegacyTrait(string id, string name, params string[] remap)
        {
            var t = new AlienTraitDef { Id = id, Name = name, Weight = 0, Kind = AlienTraitKind.Legacy, Description = "Old trait: becomes " + string.Join(" or ", remap) + "." };
            t.RemapTo.AddRange(remap);
            AlienTraits.Add(id, t);
            AlienTraitOrder.Add(id);
        }

        // ---- the per-alien table (aliens pass) ---------------------------------------------------------------------------------
        // Whole numbers only, one value per star (1★ … 10★). Owner ranges are exact at 1★ and 10★; wide ranges are interpolated
        // between the tier-star values (rounded half up), small ranges (and every count or chance) move only at tier stars 1/3/5/7/10.

        /// <summary>Hit 15% → 25% (tier stars 15 / 17 / 19 / 22 / 25).</summary>
        public static readonly int[] AlienHit15 = { 15, 16, 17, 18, 19, 21, 22, 23, 24, 25 };
        /// <summary>Hit 20% → 30% (tier stars 20 / 22 / 24 / 27 / 30).</summary>
        public static readonly int[] AlienHit20 = { 20, 21, 22, 23, 24, 26, 27, 28, 29, 30 };
        /// <summary>Sporeling's all-enemy hit 8% → 12% (small: tier stars only).</summary>
        public static readonly int[] AlienHit8 = { 8, 8, 9, 9, 10, 10, 11, 11, 11, 12 };
        public static readonly int[] AlienStacks1To3 = { 1, 1, 1, 1, 2, 2, 2, 2, 2, 3 };
        public static readonly int[] AlienStacks2To4 = { 2, 2, 2, 2, 3, 3, 3, 3, 3, 4 };
        public static readonly int[] AlienStacks1To2 = { 1, 1, 1, 1, 1, 1, 2, 2, 2, 2 };
        /// <summary>Clackclaw / Whirrbit chance points 5 → 9 (small: tier stars only).</summary>
        public static readonly int[] AlienBuff5To9 = { 5, 5, 6, 6, 7, 7, 8, 8, 8, 9 };
        /// <summary>Pebblehorn's guard and Voidmaw's Echo 20% → 50% (owner range; tier stars 20 / 25 / 35 / 40 / 50).</summary>
        public static readonly int[] AlienPct20To50 = { 20, 23, 25, 30, 35, 38, 40, 43, 47, 50 };
        /// <summary>Thornqueen's heal per stack 1% → 5% of ATK (owner range, small: tier stars only).</summary>
        public static readonly int[] AlienHeal1To5 = { 1, 1, 2, 2, 3, 3, 4, 4, 4, 5 };
        /// <summary>Gravitoad's shield 5% → 20% of max HP (owner range; tier stars 5 / 10 / 12 / 15 / 20).</summary>
        public static readonly int[] AlienShield5To20 = { 5, 8, 10, 11, 12, 14, 15, 17, 18, 20 };
        /// <summary>Frostmite's Freeze / Chronoslug's Delay chance 5 → 20% (owner-set; tier stars only).</summary>
        public static readonly int[] AlienCc5To20 = { 5, 5, 9, 9, 13, 13, 16, 16, 16, 20 };
        /// <summary>Solarch's Supernova bonus +10% → +50% (owner range; tier stars 10 / 20 / 30 / 35 / 50).</summary>
        public static readonly int[] AlienNova10To50 = { 10, 15, 20, 25, 30, 33, 35, 40, 45, 50 };
        public static readonly int[] AlienHeat2To3 = { 2, 2, 2, 2, 2, 2, 3, 3, 3, 3 };
        /// <summary>Tidecaller's heal 5% → 10% of max HP (owner range, small: tier stars only).</summary>
        public static readonly int[] AlienHeal5To10 = { 5, 5, 6, 6, 7, 7, 8, 8, 8, 10 };
        /// <summary>Nebulisk per Starlight stack: crit chance 3 → 7, crit damage 10 → 20 (owner range, small: tier stars only).</summary>
        public static readonly int[] AlienCrit3To7 = { 3, 3, 4, 4, 5, 5, 6, 6, 6, 7 };
        public static readonly int[] AlienCritDmg10To20 = { 10, 10, 12, 12, 14, 14, 17, 17, 17, 20 };
        /// <summary>Swarmheart's drone cap 1 → 2 (from 7★).</summary>
        public static readonly int[] AlienDrones1To2 = { 1, 1, 1, 1, 1, 1, 2, 2, 2, 2 };

        public const int AlienBuffRounds = 3;
        /// <summary>Chronoslug's Delay re-apply cooldown (rounds), like Freeze's.</summary>
        public const int AlienDelayCooldown = 3;
        /// <summary>Solarch: Heat stacks from which its Supernova goes off now.</summary>
        public const int SolarchNovaAt = 8;

        private static SkillTrigger Act(EffectKind e, TargetKind target = TargetKind.Enemy, double value = 0) =>
            new SkillTrigger { When = TriggerKind.OnAct, Effect = e, Target = target, Value = value };

        private static SkillTrigger ActStatus(StatusId status, int stacks, TargetKind target = TargetKind.Enemy) =>
            new SkillTrigger { When = TriggerKind.OnAct, Effect = EffectKind.ApplyStatus, Target = target, Status = status, Stacks = stacks };

        /// <summary>"Hit the front enemy for X% ATK and apply N Status." (the Common / Rare appliers).</summary>
        private static Func<int, AbilityStep> HitApply(string verb, int[] hit, StatusId status, int[] stacks, bool every = false) => s => new AbilityStep(
            (every ? verb + " every enemy" : verb + " the front enemy") + " for {v0}% ATK and apply {s1} " + SkillText.StatusName(status) + ".",
            Act(EffectKind.Damage, every ? TargetKind.AllEnemies : TargetKind.Enemy, hit[s]),
            ActStatus(status, stacks[s], every ? TargetKind.AllEnemies : TargetKind.Enemy));

        /// <summary>One star's ability: its card template and triggers.</summary>
        private sealed class AbilityStep
        {
            public readonly string Text;
            public readonly SkillTrigger[] Triggers;
            public AbilityStep(string text, params SkillTrigger[] triggers) { Text = text; Triggers = triggers; }
        }

        private void BuildAlienSpecies()
        {
            const string G = "trait_green_thumb", D = "trait_deep_digger", K = "trait_tinkerer", S = "trait_scholar", P = "trait_prodigy";
            // ---- Common ----
            AlienSpecies("alien_blorp", "Blorp", Rarity.Common, 34, 1, "Thorn Lash", "Starter Bleed applier.", new[] { SkillFamily.Bleed },
                new[] { TraitSwift, TraitEager, TraitTwin, TraitLingering, G }, HitApply("Hit", AlienHit15, StatusId.Bleed, AlienStacks1To3));
            AlienSpecies("alien_mossling", "Mossling", Rarity.Common, 34, 1.4, "Spore Puff", "Starter Radiation applier and a good worker.", new[] { SkillFamily.Poison },
                new[] { TraitSwift, TraitEager, TraitTwin, G }, HitApply("Hit", AlienHit15, StatusId.Poison, AlienStacks2To4));
            AlienSpecies("alien_pebblehorn", "Pebblehorn", Rarity.Common, 34, 1, "Stone Guard", "Softens one hit every few rounds; counters still fire.",
                new[] { SkillFamily.Counter, SkillFamily.AlienSynergy }, new[] { TraitSwift, TraitEager, TraitBulwark, TraitLingering, D },
                s => new AbilityStep("Guard the worm: the next hit it takes deals {v0}% less.", Act(EffectKind.Guard, TargetKind.Worm, AlienPct20To50[s])));
            AlienSpecies("alien_zib", "Zib", Rarity.Common, 37, 1, "Zip Strike", "A quick striker that rides the worm's combo chance.", new[] { SkillFamily.Combo },
                new[] { TraitSwift, TraitEager, TraitWide, TraitVampiric, K },
                s => { var t = Act(EffectKind.Damage, TargetKind.Enemy, AlienHit20[s]); t.Param = "combo"; return new AbilityStep("Strike the front enemy for {v0}% ATK; uses your combo chance.", t); });
            AlienSpecies("alien_glimmerfly", "Glimmerfly", Rarity.Common, 37, 1, "Static Wings", "Shock builder.", new[] { SkillFamily.Lightning },
                new[] { TraitSwift, TraitEager, TraitTwin, TraitLingering, S }, HitApply("Zap", AlienHit15, StatusId.Shock, AlienStacks1To3));
            AlienSpecies("alien_shiverpup", "Shiverpup", Rarity.Common, 34, 1, "Frost Bite", "Chill builder (no crowd control).", new[] { SkillFamily.Ice },
                new[] { TraitSwift, TraitEager, TraitTwin, TraitLingering, D }, HitApply("Bite", AlienHit15, StatusId.Chill, AlienStacks1To3));
            AlienSpecies("alien_emberkit", "Emberkit", Rarity.Common, 34, 1, "Kindle", "Starter Fire applier.", new[] { SkillFamily.Fire },
                new[] { TraitSwift, TraitEager, TraitTwin, TraitWide, K }, HitApply("Hit", AlienHit15, StatusId.Heat, AlienStacks1To3));
            // ---- Rare ----
            AlienSpecies("alien_frostmite", "Frostmite", Rarity.Rare, 37, 1, "Rime Bite", "Freeze source (a Freeze resets Chill).", new[] { SkillFamily.Ice },
                new[] { TraitSwift, TraitEager, TraitWide, D },
                s =>
                {
                    var f = ActStatus(StatusId.Freeze, 1); f.Chance = AlienCc5To20[s]; f.Duration = 1;
                    return new AbilityStep("Bite the front enemy for {v0}% ATK. {c1}% chance to Freeze it.", Act(EffectKind.Damage, TargetKind.Enemy, AlienHit20[s]), f);
                });
            AlienSpecies("alien_sporeling", "Sporeling", Rarity.Rare, 37, 1.4, "Spore Cloud", "Radiation on every enemy.", new[] { SkillFamily.Poison },
                new[] { TraitSwift, TraitEager, TraitTwin, G }, HitApply("Hit", AlienHit8, StatusId.Poison, AlienStacks1To2, true));
            AlienSpecies("alien_clackclaw", "Clackclaw", Rarity.Rare, 34, 1, "Snap Back", "Counter uptime.", new[] { SkillFamily.Counter },
                new[] { TraitSwift, TraitEager, TraitLingering, D }, s => WormBuffStep(StatId.CounterChance, AlienBuff5To9[s], "counter chance"));
            AlienSpecies("alien_whirrbit", "Whirrbit", Rarity.Rare, 37, 1.4, "Whirl", "Combo uptime.", new[] { SkillFamily.Combo },
                new[] { TraitSwift, TraitEager, TraitLingering, K }, s => WormBuffStep(StatId.ComboChance, AlienBuff5To9[s], "combo chance"));
            AlienSpecies("alien_arcwhisk", "Arcwhisk", Rarity.Rare, 41, 1, "Arc Whip", "Arc source: its jumps count as yours.", new[] { SkillFamily.Lightning },
                new[] { TraitSwift, TraitEager, TraitWide, S },
                s =>
                {
                    int n = AlienStacks1To3[s];
                    var t = Act(EffectKind.Damage, TargetKind.OtherEnemies, AlienHit20[s]); t.Stacks = n;
                    return new AbilityStep("Arc to " + (n == 1 ? "1 enemy" : "{s0} enemies") + " for {v0}% ATK. Counts as your Arc jump.", t);
                });
            // ---- Epic ----
            AlienSpecies("alien_voidmaw", "Voidmaw", Rarity.Epic, 40, 1, "Void Echo", "Echoes the worm's last attack.", new[] { SkillFamily.Time },
                new[] { TraitSwift, TraitEager, TraitVampiric, S },
                s => new AbilityStep("Repeat the worm's last attack at {v0}% damage (an Echo).", Act(EffectKind.Echo, TargetKind.Self, AlienPct20To50[s])));
            AlienSpecies("alien_thornqueen", "Thornqueen", Rarity.Epic, 36, 1, "Thorn Harvest", "Cashes Bleed in as healing.", new[] { SkillFamily.Bleed },
                new[] { TraitSwift, TraitEager, TraitLingering, G },
                s =>
                {
                    var t = Act(EffectKind.ConsumeStatus, TargetKind.Enemy, AlienHeal1To5[s]); t.Status = StatusId.Bleed; t.Param = "dmg:0;heal_atk:$";
                    return new AbilityStep("Consume the front enemy's Bleed. Heal the worm {v0}% of ATK per stack consumed.", t);
                });
            AlienSpecies("alien_gravitoad", "Gravitoad", Rarity.Epic, 36, 1, "Rock Ward", "Shields the worm.", new[] { SkillFamily.Counter },
                new[] { TraitSwift, TraitEager, TraitBulwark, D },
                s => new AbilityStep("Shield the worm for {v0}% of max HP.", Act(EffectKind.Shield, TargetKind.Worm, AlienShield5To20[s])));
            AlienSpecies("alien_chronoslug", "Chronoslug", Rarity.Epic, 40, 1, "Slow Ooze", "Time control: may delay the front enemy.", new[] { SkillFamily.Time },
                new[] { TraitSwift, TraitEager, TraitWide, S },
                s =>
                {
                    var d = ActStatus(StatusId.Delay, 1); d.Chance = AlienCc5To20[s]; d.Param = "cd:" + AlienDelayCooldown;
                    return new AbilityStep("Hit the front enemy for {v0}% ATK. {c1}% chance to Delay it (loses its next turn).", Act(EffectKind.Damage, TargetKind.Enemy, AlienHit20[s]), d);
                });
            // ---- Legendary ----
            AlienSpecies("alien_solarch", "Solarch", Rarity.Legendary, 44, 1, "Solar Flare", "Fire payoff: detonates Heat early.", new[] { SkillFamily.Fire },
                new[] { TraitSwift, TraitEager, TraitTwin, P },
                s =>
                {
                    var nova = Act(EffectKind.ForceSupernova, TargetKind.Enemy, AlienNova10To50[s]); nova.Param = "at:" + SolarchNovaAt;
                    return new AbilityStep("Apply {s0} Heat to the front enemy. At " + SolarchNovaAt + "+ Heat it goes Supernova now, +{v1}% damage.",
                        ActStatus(StatusId.Heat, AlienHeat2To3[s]), nova);
                });
            AlienSpecies("alien_tidecaller", "Tidecaller", Rarity.Legendary, 44, 1.4, "Tidal Call", "Alien engine: heals and makes another sidekick act.", new[] { SkillFamily.AlienSynergy },
                new[] { TraitSwift, TraitEager, TraitLingering, P },
                s =>
                {
                    var more = Act(EffectKind.AllyAction, TargetKind.Self); more.Param = "others";
                    return new AbilityStep("Heal the worm {v0}% of max HP. Another sidekick acts.", Act(EffectKind.Heal, TargetKind.Worm, AlienHeal5To10[s]), more);
                });
            // ---- Mythic ----
            AlienSpecies("alien_nebulisk", "Nebulisk", Rarity.Mythic, 50, 1, "Starlight Eye", "Battle-long crit stacks for every crit build.",
                new[] { SkillFamily.Bleed, SkillFamily.Fire, SkillFamily.Poison, SkillFamily.Combo }, new[] { TraitSwift, TraitEager, P },
                s =>
                {
                    SkillTrigger Stack(StatId stat, int v) => new SkillTrigger { When = TriggerKind.OnAct, Effect = EffectKind.BuffStat, Target = TargetKind.Worm, Stat = new StatMod(stat, v), Param = "stack:3" };
                    return new AbilityStep("Give the worm a Starlight stack: +{b0}% crit chance, +{b1}% crit damage. Max 3, lasts the battle.",
                        Stack(StatId.CritChance, AlienCrit3To7[s]), Stack(StatId.CritDamage, AlienCritDmg10To20[s]));
                });
            AlienSpecies("alien_swarmheart", "Swarmheart", Rarity.Mythic, 50, 1, "Hatch", "Grows the squad mid-battle with drones.", new[] { SkillFamily.AlienSynergy },
                new[] { TraitSwift, TraitEager, TraitWide, P },
                s =>
                {
                    int max = AlienDrones1To2[s];
                    var t = Act(EffectKind.Summon, TargetKind.Self, AlienHit15[s]); t.Param = "sk_drone;max:" + max;
                    return new AbilityStep("Hatch a Drone that zaps for {v0}% ATK. Max " + max + (max == 1 ? " Drone." : " Drones."), t);
                });
        }

        private static AbilityStep WormBuffStep(StatId stat, int points, string what)
        {
            var t = new SkillTrigger { When = TriggerKind.OnAct, Effect = EffectKind.BuffStat, Target = TargetKind.Worm, Stat = new StatMod(stat, points), Duration = AlienBuffRounds };
            return new AbilityStep("The worm gets +{b0}% " + what + " for {d0} rounds.", t);
        }

        private void AlienSpecies(string id, string name, Rarity rarity, double speed, double workFactor, string ability, string description,
            SkillFamily[] synergies, string[] traits, Func<int, AbilityStep> step)
        {
            var a = new AlienTypeDef
            {
                Id = id, Name = name, Rarity = rarity, Family = synergies[0], AbilityName = ability, Description = description,
                BaseSpeed = speed, BaseWork = AlienBaseWork[(int)rarity] * workFactor,
            };
            a.Synergies.AddRange(synergies);
            a.PossibleTraits.AddRange(traits);
            string prev = null;
            for (int star = 1; star <= 10; star++)
            {
                var st = step(star - 1);
                var s = new SkillDef
                {
                    Id = "ask_" + id.Replace("alien_", "") + "_" + star, Name = ability, Description = description, Family = synergies[0], Rarity = rarity,
                    Source = SkillSource.Alien, Draftable = false, UpgradesFrom = prev, Text = st.Text,
                };
                s.Triggers.AddRange(st.Triggers);
                AddSkill(s);
                a.ArchetypeSkills.Add(new AlienSkillStep(star, s.Id));
                prev = s.Id;
            }
            AlienTypes.Add(id, a);
        }

        private static readonly double[] AlienBaseWork = { 10, 13, 17, 22, 30 };
    }
}
