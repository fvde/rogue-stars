using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>Archetype skill step: active from Stars on (replaces the previous step).</summary>
    [Serializable]
    public sealed class AlienSkillStep
    {
        public int Stars;
        public string SkillId;
        public AlienSkillStep() { }
        public AlienSkillStep(int stars, string skillId) { Stars = stars; SkillId = skillId; }
    }

    [Serializable]
    public sealed class AlienTypeDef
    {
        public string Id;
        public string Name;
        public string Description;
        public Rarity Rarity;
        public SkillFamily Family;
        /// <summary>Archetype skill upgrades: stars 1 / 3 / 5 / 7 / 10.</summary>
        public List<AlienSkillStep> ArchetypeSkills = new List<AlienSkillStep>();
        public double BaseHp, BaseAtk, BaseDef, BaseSpeed;
        /// <summary>Work power: percent production bonus at level 1 / star 1 / neutral genes.</summary>
        public double BaseWork;
        /// <summary>Traits this species can roll when pulled (mutations may add any trait).</summary>
        public List<string> PossibleTraits = new List<string>();
    }

    /// <summary>
    /// A summoned sidekick (sidekick pass): exists for one battle, never attacked, charges by Speed and uses its one ability.
    /// Its ability power is a % of the summoner's ATK (the Summon trigger's Value), x the side's alien power.
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

    [Serializable]
    public sealed class AlienTraitDef
    {
        public string Id;
        public string Name;
        public string Description;
        /// <summary>Roll weight for initial rolls and mutations.</summary>
        public double Weight = 10;
        /// <summary>Combat: stat mods applied to the alien's combat stats.</summary>
        public List<StatMod> CombatStats = new List<StatMod>();
        /// <summary>Work: + percent production when working in a building producing one of WorkCurrencies (empty = any).</summary>
        public double WorkPct;
        public List<string> WorkCurrencies = new List<string>();
    }

    [Serializable]
    public sealed class AlienTuningDef
    {
        /// <summary>Shards to go from star s to s+1 (index 0 = 1→2 ... 8 = 9→10).</summary>
        public long[] StarCosts = { 2, 4, 6, 10, 15, 22, 30, 42, 60 };
        /// <summary>Shards per duplicate alien copy, by rarity.</summary>
        public long[] ShardsPerDuplicate = { 1, 1, 1, 1, 1 };
        /// <summary>Grain to go from level L to L+1 = LevelGrainBase[rarity] × LevelGrainGrowth^(L−1).</summary>
        public long[] LevelGrainBase = { 80, 120, 180, 280, 400 };
        public double LevelGrainGrowth = 1.065;
        /// <summary>Max level = MaxLevelBase + MaxLevelPerStar × stars.</summary>
        public int MaxLevelBase = 20, MaxLevelPerStar = 8;
        /// <summary>Combat/work growth in percent per level above 1 and per star above 1.</summary>
        public double LevelCombatPct = 6, LevelWorkPct = 2, StarCombatPct = 12, StarWorkPct = 8;
        /// <summary>Initial gene roll: uniform in [−InitialGeneSpread, +InitialGeneSpread]. Genes always clamp to [GeneMin, GeneMax].</summary>
        public double InitialGeneSpread = 15, GeneMin = -30, GeneMax = 60;
        /// <summary>Chance for the 1st, 2nd, 3rd trait when a new alien is rolled.</summary>
        public double[] TraitChances = { 1.0, 0.35, 0.1 };
        public int MaxTraits = 3;
        /// <summary>Squad aliens can also work, at this fraction of their work bonus.</summary>
        public double SquadWorkFactor = 0.5;
        /// <summary>Stars at which archetype skills upgrade.</summary>
        public int[] SkillStars = { 1, 3, 5, 7, 10 };
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
        public SidekickDef Sidekick(string id) => id != null && Sidekicks.TryGetValue(id, out var d) ? d : null;
        public AlienTraitDef AlienTrait(string id) => id != null && AlienTraits.TryGetValue(id, out var t) ? t : null;

        partial void BuildAliens()
        {
            // ---- traits ----
            AlienWorkTrait("trait_green_thumb", "Green Thumb", 20, 12, Currencies.Biomass, Currencies.Grain);
            AlienWorkTrait("trait_deep_digger", "Deep Digger", 20, 12, Currencies.Ore, Currencies.Diamonds, Currencies.AdvancementPlates);
            AlienWorkTrait("trait_tinkerer", "Tinkerer", 20, 10, Currencies.Fuel, Currencies.GearCores);
            AlienWorkTrait("trait_scholar", "Scholar", 15, 8, Currencies.HeroXp, Currencies.BlueprintTokens, Currencies.AlienTokens);
            AlienWorkTrait("trait_tireless", "Tireless", 10, 10);
            AlienWorkTrait("trait_prodigy", "Prodigy", 18, 3);
            // Sidekicks (sidekick pass): aliens are never attacked; HP, ATK and DEF all fold into their ability power and Speed sets how
            // often they act. Combat traits raise one of those (ids kept for saved aliens: Tough / Armored were Brawler / Giant Slayer,
            // Swift was dodge ignore).
            AlienCombatTrait("trait_tough", "Tough", 12, new StatMod(StatId.Hp, 0, 15), "more ability power");
            AlienCombatTrait("trait_fierce", "Fierce", 12, new StatMod(StatId.Atk, 0, 15), "more ability power");
            AlienCombatTrait("trait_armored", "Armored", 10, new StatMod(StatId.Def, 0, 30), "more ability power");
            AlienCombatTrait("trait_swift", "Swift", 10, new StatMod(StatId.Speed, 0, 15), "acts more often");
            AlienCombatTrait("trait_lucky", "Lucky", 8, new StatMod(StatId.CritChance, 10), "damage abilities crit more");
            AlienCombatTrait("trait_vampiric", "Vampiric", 5, new StatMod(StatId.LifeSteal, 8), "damage abilities heal the worm");
            AlienCombatTrait("trait_pyromaniac", "Pyromaniac", 6, new StatMod(StatId.FireDamage, 15), "hotter Heat");
            AlienCombatTrait("trait_stormborn", "Stormborn", 6, new StatMod(StatId.LightningDamage, 15), "stronger Shock");

            // ---- species: id, name, rarity, family, archetype, role ----
            AlienSpecies("alien_blorp", "Blorp", Rarity.Common, SkillFamily.Bleed, "bleeder", "balanced", "trait_tough", "trait_green_thumb", "trait_tireless");
            AlienSpecies("alien_mossling", "Mossling", Rarity.Common, SkillFamily.AlienSynergy, "healer", "worker", "trait_green_thumb", "trait_tough", "trait_tireless");
            AlienSpecies("alien_pebblehorn", "Pebblehorn", Rarity.Common, SkillFamily.Counter, "blocker", "tank", "trait_armored", "trait_deep_digger", "trait_swift");
            AlienSpecies("alien_zib", "Zib", Rarity.Common, SkillFamily.Combo, "rager", "dps", "trait_swift", "trait_fierce", "trait_tinkerer");
            AlienSpecies("alien_glimmerfly", "Glimmerfly", Rarity.Common, SkillFamily.Lightning, "shocker", "balanced", "trait_stormborn", "trait_swift", "trait_scholar");
            AlienSpecies("alien_emberkit", "Emberkit", Rarity.Rare, SkillFamily.Fire, "burner", "dps", "trait_pyromaniac", "trait_fierce", "trait_tinkerer");
            AlienSpecies("alien_frostmite", "Frostmite", Rarity.Rare, SkillFamily.Ice, "freezer", "tank", "trait_armored", "trait_swift", "trait_deep_digger");
            AlienSpecies("alien_sporeling", "Sporeling", Rarity.Rare, SkillFamily.Poison, "poisoner", "worker", "trait_green_thumb", "trait_tireless", "trait_tough");
            AlienSpecies("alien_clackclaw", "Clackclaw", Rarity.Rare, SkillFamily.Counter, "counter", "tank", "trait_armored", "trait_deep_digger", "trait_tough");
            AlienSpecies("alien_whirrbit", "Whirrbit", Rarity.Rare, SkillFamily.Combo, "combo", "worker", "trait_tinkerer", "trait_swift", "trait_scholar");
            AlienSpecies("alien_voidmaw", "Voidmaw", Rarity.Epic, SkillFamily.Crit, "striker", "dps", "trait_fierce", "trait_lucky", "trait_vampiric");
            AlienSpecies("alien_thornqueen", "Thornqueen", Rarity.Epic, SkillFamily.Bleed, "bleeder", "balanced", "trait_fierce", "trait_tough", "trait_green_thumb");
            AlienSpecies("alien_gravitoad", "Gravitoad", Rarity.Epic, SkillFamily.Counter, "shielder", "tank", "trait_armored", "trait_tough", "trait_deep_digger");
            AlienSpecies("alien_solarch", "Solarch", Rarity.Legendary, SkillFamily.Fire, "burner", "dps", "trait_pyromaniac", "trait_fierce", "trait_prodigy");
            AlienSpecies("alien_tidecaller", "Tidecaller", Rarity.Legendary, SkillFamily.AlienSynergy, "healer", "worker", "trait_prodigy", "trait_scholar", "trait_tough");
            AlienSpecies("alien_nebulisk", "Nebulisk", Rarity.Mythic, SkillFamily.Crit, "crit", "dps", "trait_swift", "trait_fierce", "trait_prodigy");

            // ---- battle-scoped sidekicks (summoned for one battle: Hive Carrier drones, Mothership Link, Swarm Beacon) ----
            AddSidekick("sk_drone", "Drone", 40, "Zaps the front enemy.",
                new SkillTrigger { When = TriggerKind.OnAct, Effect = EffectKind.Damage, Target = TargetKind.Enemy, Value = 150 });
        }

        // ---- content helpers (aliens) --------------------------------------------------------

        private void AddSidekick(string id, string name, double speed, string description, params SkillTrigger[] ability)
        {
            var skill = new SkillDef
            {
                Id = "skab_" + id.Replace("sk_", ""), Name = name + " Zap", Description = description, Family = SkillFamily.AlienSynergy,
                Rarity = Rarity.Common, Source = SkillSource.Alien, Draftable = false,
            };
            skill.Triggers.AddRange(ability);
            AddSkill(skill);
            Sidekicks.Add(id, new SidekickDef { Id = id, Name = name, Speed = speed, Description = description, AbilitySkillId = skill.Id });
        }

        private void AlienWorkTrait(string id, string name, double pct, double weight, params string[] currencies)
        {
            var t = new AlienTraitDef { Id = id, Name = name, WorkPct = pct, Weight = weight };
            t.WorkCurrencies.AddRange(currencies);
            t.Description = "+" + pct + "% production" + (currencies.Length > 0 ? " of " + string.Join("/", currencies) : "") + " when working.";
            AlienTraits.Add(id, t);
            AlienTraitOrder.Add(id);
        }

        private void AlienCombatTrait(string id, string name, double weight, StatMod mod, string what)
        {
            var t = new AlienTraitDef { Id = id, Name = name, Weight = weight, Description = name + ": " + SkillText.Stat(mod) + " (" + what + ")." };
            t.CombatStats.Add(mod);
            AlienTraits.Add(id, t);
            AlienTraitOrder.Add(id);
        }

        private static readonly double[] AlienBaseHp = { 200, 260, 340, 450, 600 };
        private static readonly double[] AlienBaseAtk = { 20, 26, 34, 45, 60 };
        private static readonly double[] AlienBaseDef = { 5, 7, 9, 12, 16 };
        /// <summary>Sidekick charge per turn (Combat.SidekickActAt = 100): a Common acts about every 3 rounds, a Legendary about every 2.3, a Mythic every 2.</summary>
        private static readonly double[] AlienBaseSpeed = { 34, 37, 40, 44, 50 };
        private static readonly double[] AlienBaseWork = { 10, 13, 17, 22, 30 };

        private void AlienSpecies(string id, string name, Rarity rarity, SkillFamily family, string archetype, string role, params string[] traits)
        {
            int r = (int)rarity;
            double hp = 1, atk = 1, def = 1, work = 1;
            switch (role)
            {
                case "tank": hp = 1.3; atk = 0.8; def = 1.3; break;
                case "dps": hp = 0.85; atk = 1.25; break;
                case "worker": atk = 0.85; work = 1.4; break;
            }
            var a = new AlienTypeDef
            {
                Id = id, Name = name, Rarity = rarity, Family = family,
                BaseHp = AlienBaseHp[r] * hp, BaseAtk = AlienBaseAtk[r] * atk, BaseDef = AlienBaseDef[r] * def,
                BaseSpeed = AlienBaseSpeed[r], BaseWork = AlienBaseWork[r] * work,
                Description = name + " — " + archetype + " (" + role + ")",
            };
            a.PossibleTraits.AddRange(traits);
            int[] stars = AlienTuning.SkillStars;
            string prev = null;
            for (int tier = 0; tier < stars.Length; tier++)
            {
                var s = AlienArchetypeSkill(archetype, tier, r);
                s.Id = "ask_" + id.Replace("alien_", "") + "_" + (tier + 1);
                s.Name = name + " " + s.Name + " " + (tier + 1);
                s.Family = family;
                s.Rarity = rarity;
                s.Source = SkillSource.Alien;
                s.Draftable = false;
                s.UpgradesFrom = prev;
                AddSkill(s);
                a.ArchetypeSkills.Add(new AlienSkillStep(stars[tier], s.Id));
                prev = s.Id;
            }
            AlienTypes.Add(id, a);
        }

        /// <summary>
        /// Archetype abilities (sidekick pass): each alien has ONE ability it uses when it acts (TriggerKind.OnAct); tier 0..4 =
        /// stars 1/3/5/7/10 (later tiers replace earlier ones), rarity adds a small boost. Damage and status power come from the
        /// alien's ability power (its ATK in battle); heals, shields, rage and buffs also scale with it (Combat.SidekickSupport*).
        /// Only the Mythic (Nebulisk) has a two-part ability.
        /// </summary>
        private static SkillDef AlienArchetypeSkill(string archetype, int tier, int rarity)
        {
            double k = 1 + 0.1 * rarity;
            var s = new SkillDef();
            SkillTrigger Act(EffectKind e, TargetKind target)
            {
                var t = new SkillTrigger { When = TriggerKind.OnAct, Effect = e, Target = target };
                s.Triggers.Add(t);
                return t;
            }
            switch (archetype)
            {
                case "striker":
                    s.Name = "Void Strike"; s.Description = "Strikes the front enemy.";
                    Act(EffectKind.Damage, TargetKind.Enemy).Value = R1(AlienPick(tier, 150, 180, 210, 245, 290) * k);
                    break;
                case "healer":
                    s.Name = "Soothing Spores"; s.Description = "Heals the worm.";
                    Act(EffectKind.Heal, TargetKind.Worm).Value = R1(AlienPick(tier, 5, 6, 7, 8.5, 10) * k);
                    break;
                case "shielder":
                    s.Name = "Rock Ward"; s.Description = "Shields the worm.";
                    Act(EffectKind.Shield, TargetKind.Worm).Value = R1(AlienPick(tier, 8, 10, 12, 14.5, 17) * k);
                    break;
                case "blocker":
                    s.Name = "Block"; s.Description = "Intercepts the next hit on the worm.";
                    Act(EffectKind.Block, TargetKind.Worm).Stacks = tier >= 4 ? 3 : tier >= 2 ? 2 : 1;
                    break;
                case "rager":
                    s.Name = "War Chant"; s.Description = "Fills the worm's rage.";
                    Act(EffectKind.GainRage, TargetKind.Worm).Value = R1(AlienPick(tier, 20, 25, 30, 36, 45) * k);
                    break;
                case "burner": s.Name = "Kindle"; s.Description = "Heats the front enemy."; ActStatus(Act, StatusId.Heat, TargetKind.Enemy, AlienPickInt(tier, 1, 2, 2, 3, 3)); break;
                case "freezer":
                {
                    s.Name = "Rime Bite"; s.Description = "May freeze the front enemy.";
                    var t = ActStatus(Act, StatusId.Freeze, TargetKind.Enemy, 1);
                    t.Chance = Math.Min(100, Math.Round(AlienPick(tier, 50, 60, 70, 80, 95) * k)); t.Duration = 1;
                    break;
                }
                case "shocker": s.Name = "Static Wings"; s.Description = "Shocks the front enemy."; ActStatus(Act, StatusId.Shock, TargetKind.Enemy, AlienPickInt(tier, 1, 2, 2, 3, 3)); break;
                case "poisoner": s.Name = "Spore Cloud"; s.Description = "Irradiates every enemy."; ActStatus(Act, StatusId.Poison, TargetKind.AllEnemies, AlienPickInt(tier, 2, 2, 3, 3, 4)); break;
                case "bleeder": s.Name = "Thorn Lash"; s.Description = "Makes the front enemy bleed."; ActStatus(Act, StatusId.Bleed, TargetKind.Enemy, AlienPickInt(tier, 2, 2, 3, 3, 4)); break;
                case "counter":
                    s.Name = "Snap Back"; s.Description = "The worm counters more often for a while.";
                    WormBuff(Act, StatId.CounterChance, AlienPick(tier, 6, 8, 10, 12, 14) * k);
                    break;
                case "combo":
                    s.Name = "Whirl"; s.Description = "The worm combos more often for a while.";
                    WormBuff(Act, StatId.ComboChance, AlienPick(tier, 6, 8, 10, 12, 14) * k);
                    break;
                case "crit":
                    s.Name = "Starlight Eye"; s.Description = "The worm crits more often and harder for a while.";
                    WormBuff(Act, StatId.CritChance, AlienPick(tier, 4, 5, 6, 8, 10) * k);
                    WormBuff(Act, StatId.CritDamage, AlienPick(tier, 10, 13, 16, 21, 28) * k);
                    break;
                default: throw new InvalidOperationException("unknown archetype " + archetype);
            }
            return s;
        }

        private static SkillTrigger ActStatus(Func<EffectKind, TargetKind, SkillTrigger> act, StatusId status, TargetKind target, int stacks)
        {
            var t = act(EffectKind.ApplyStatus, target);
            t.Status = status; t.Stacks = stacks;
            return t;
        }

        /// <summary>Support abilities raise one of the worm's stats for <see cref="SidekickBuffRounds"/> rounds (about until the next action).</summary>
        private static void WormBuff(Func<EffectKind, TargetKind, SkillTrigger> act, StatId stat, double points)
        {
            var t = act(EffectKind.BuffStat, TargetKind.Worm);
            t.Stat = new StatMod(stat, R1(points)); t.Duration = SidekickBuffRounds;
        }

        public const int SidekickBuffRounds = 3;

        private static double R1(double v) => Math.Round(v, 1);
        private static int AlienPickInt(int tier, params int[] values) => values[Math.Min(tier, values.Length - 1)];

        private static double AlienPick(int tier, params double[] values) => values[Math.Min(tier, values.Length - 1)];
    }
}
