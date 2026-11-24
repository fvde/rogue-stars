using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>Fluent helper to define skills compactly in content (Run workstream).</summary>
    public sealed class RunSkillBuilder
    {
        public readonly SkillDef Def;
        public RunSkillBuilder(SkillDef def) { Def = def; }

        public RunSkillBuilder Desc(string text) { Def.Description = text; return this; }
        public RunSkillBuilder Stat(StatId stat, double flat, double percent = 0) { Def.Stats.Add(new StatMod(stat, flat, percent)); return this; }
        public RunSkillBuilder Upgrades(string fromId) { Def.UpgradesFrom = fromId; return this; }
        public RunSkillBuilder FamilyGate(int count) { Def.RequiresFamilyCount = count; return this; }
        public RunSkillBuilder Requires(params string[] ids) { Def.Requires.AddRange(ids); return this; }
        public RunSkillBuilder NotDraftable() { Def.Draftable = false; return this; }
        /// <summary>A count effect on sidekicks: AllyAction (Stacks sidekick actions), Summon (Stacks sidekicks of Param at Value % ATK), Block.</summary>
        public RunSkillBuilder Kick(TriggerKind when, EffectKind effect, int stacks, double value = 0, string param = null, TargetKind target = TargetKind.Self)
        {
            Def.Triggers.Add(new SkillTrigger { When = when, Effect = effect, Stacks = stacks, Value = value, Param = param, Target = target });
            return this;
        }

        public RunSkillBuilder On(TriggerKind when, EffectKind effect, double value, TargetKind target = TargetKind.Enemy, double chance = 100, int cooldown = 0, string param = null)
        {
            Def.Triggers.Add(new SkillTrigger { When = when, Effect = effect, Value = value, Target = target, Chance = chance, CooldownRounds = cooldown, Param = param });
            return this;
        }

        public RunSkillBuilder Status(TriggerKind when, double chance, StatusId status, int stacks = 1, TargetKind target = TargetKind.Enemy, int duration = 0, int cooldown = 0)
        {
            Def.Triggers.Add(new SkillTrigger { When = when, Effect = EffectKind.ApplyStatus, Chance = chance, Status = status, Stacks = stacks, Target = target, Duration = duration, CooldownRounds = cooldown });
            return this;
        }

        public RunSkillBuilder Buff(TriggerKind when, StatId stat, double percent, TargetKind target = TargetKind.Self, int duration = 0, double chance = 100, double flat = 0)
        {
            Def.Triggers.Add(new SkillTrigger { When = when, Effect = EffectKind.BuffStat, Stat = new StatMod(stat, flat, percent), Target = target, Duration = duration, Chance = chance });
            return this;
        }

        /// <summary>Conditional damage bonus (Passive, Param "cond:&lt;condition&gt;", Value = +%).</summary>
        public RunSkillBuilder Cond(string condition, double percent)
        {
            Def.Triggers.Add(new SkillTrigger { When = TriggerKind.Passive, Effect = EffectKind.BuffStat, Param = "cond:" + condition, Value = percent });
            return this;
        }

        /// <summary>Run-level (board) trigger, see docs/design/skill-conventions.md.</summary>
        public RunSkillBuilder Board(TriggerKind when, EffectKind effect, double value, string param = null, double chance = 100, StatMod stat = null)
        {
            Def.Triggers.Add(new SkillTrigger { When = when, Effect = effect, Value = value, Param = param, Chance = chance, Stat = stat, Target = TargetKind.Self });
            return this;
        }
    }

    /// <summary>
    /// Run skills (three tiers: Common, Legendary, Mythic; 9 families + generic, ~30 Mythic); the Laboratory tree is in
    /// ContentDb.Laboratory.cs.
    /// </summary>
    public sealed partial class ContentDb
    {
        /// <summary>
        /// A run-pool skill (run or laboratory). Its rarity is mapped to one of the three run tiers (RunTuning.RunTier: Common and
        /// Rare → Common, Epic and Legendary → Legendary, Mythic → Mythic), fixed for the skill: it never upgrades into a tier.
        /// </summary>
        private RunSkillBuilder RunSkill(string id, string name, SkillFamily family, Rarity rarity, SkillSource source = SkillSource.Run)
        {
            var s = new SkillDef { Id = id, Name = name, Family = family, Rarity = RunTuning.RunTier(rarity), Source = source };
            AddSkill(s);
            return new RunSkillBuilder(s);
        }

        /// <summary>
        /// A former upgrade chain (e.g. Spark → Arc → Thunderclap), folded into ONE skill (owner decision: no upgrading into a
        /// better rarity): its first member's name and tier; its levels (SkillLeveling's ladder) replace the chain's growth. The
        /// later members' names / rarities are kept in the call for the record only.
        /// </summary>
        private void RunChain(string id, SkillFamily family, string[] names, Rarity[] rarities, Action<RunSkillBuilder, int> fill)
        {
            var b = RunSkill(id, names[0], family, rarities[0]);
            fill(b, 1);
        }

        private static readonly Rarity[] RunChainCRE = { Rarity.Common, Rarity.Rare, Rarity.Epic };

        partial void BuildSkills()
        {
            RunBuildFamilySkills();
            RunBuildGenericSkills();
            RunBuildMythicSkills();
            RunBuildSignatures();
            RunBuildLabSkills();
        }

        // Numbers (owner tuning pass): a typical Common skill gives +15% at Lv1 and climbs the level ladder (SkillLeveling:
        // ×1, ×5/3, ×8/3 → +15% → +25% → +40%); Lv1 numbers are multiples of 3 so every level reads as whole numbers. A
        // Legendary gives ~+25% per pick (×1, ×8/5 → +25% → +40%), a Mythic one strong standalone package (it never levels).
        // Proc chances sit on the doubled ladder (30% → 50% → 80%); element damage stats are points (flat), like crit chance.
        private void RunBuildFamilySkills()
        {
            const TriggerKind Hit = TriggerKind.OnHit;

            // Signatures pass: Fire, Radiation (internal id Poison) and Bleed are the starting synergies; Ice, Lightning, Alien
            // Synergy, Counter and Combo enter the run pool when their Laboratory package is researched (RunDraft family lock).
            // Every synergy: 2 Common + 2 Legendary here and 3 Mythics (RunBuildMythicSkills, one pool for miniboss choices).
            // Crit is no synergy any more: its standalone picks are generic skills (RunBuildGenericSkills).

            // ---- Fire: Heat stacks; at 10 the enemy goes Supernova (all enemies hit, the worm takes recoil) ----
            RunChain("fire_ember", SkillFamily.Fire, new[] { "Ember Strikes", "Blazing Strikes", "Inferno Strikes" }, RunChainCRE, (b, t) =>
                b.Desc("Hits may add Heat.").Status(Hit, 30, StatusId.Heat, 2));
            RunSkill("fire_kindle", "Kindling Heart", SkillFamily.Fire, Rarity.Rare).Desc("More damage against heated enemies.")
                .Cond("vs_status:Heat", 15);
            RunSkill("fire_flare", "Solar Flare", SkillFamily.Fire, Rarity.Epic).FamilyGate(2).Desc("Rage attacks heat all enemies.")
                .Status(TriggerKind.OnRageAttack, 100, StatusId.Heat, 3, TargetKind.AllEnemies);
            RunSkill("fire_inferno", "Wildfire", SkillFamily.Fire, Rarity.Legendary).FamilyGate(3).Desc("Bigger Supernovas, less recoil.")
                .Stat(StatId.SupernovaDamage, 25).Stat(StatId.SupernovaGuard, 25);

            // ---- Ice (package) ----------------------------------------------------------------
            RunChain("ice_shard", SkillFamily.Ice, new[] { "Ice Shard", "Ice Lance", "Glacial Spear" }, RunChainCRE, (b, t) =>
                b.Desc("Attacks fire an ice shard that may Freeze.").On(TriggerKind.OnAttack, EffectKind.Damage, 36, TargetKind.Enemy, 35)
                 .Status(TriggerKind.OnAttack, 6, StatusId.Freeze));
            RunSkill("ice_armor", "Frost Armor", SkillFamily.Ice, Rarity.Rare).Desc("+15% DEF; start battles with a shield.")
                .Stat(StatId.Def, 0, 15).On(TriggerKind.OnBattleStart, EffectKind.Shield, 6, TargetKind.Self);
            RunSkill("ice_frostbite", "Frostbite", SkillFamily.Ice, Rarity.Epic).FamilyGate(2).Desc("+40% damage vs frozen enemies, +20% ice damage.")
                .Cond("vs_status:Freeze", 40).Stat(StatId.IceDamage, 20);
            RunSkill("ice_glacier", "Glacier Skin", SkillFamily.Ice, Rarity.Legendary).FamilyGate(3).Desc("When damaged, may freeze the attacker.")
                .Status(TriggerKind.OnDamaged, 20, StatusId.Freeze).Stat(StatId.DamageReduction, 10);

            // ---- Lightning (package) ----------------------------------------------------------
            RunChain("light_spark", SkillFamily.Lightning, new[] { "Spark", "Arc", "Thunderclap" }, RunChainCRE, (b, t) =>
                b.Desc("Hits may Shock.").Status(Hit, 30, StatusId.Shock));
            RunSkill("light_conductor", "Conductor Coil", SkillFamily.Lightning, Rarity.Rare).Desc("+12 Speed, +15% lightning damage.")
                .Stat(StatId.Speed, 12).Stat(StatId.LightningDamage, 15);
            RunSkill("light_chain", "Chain Lightning", SkillFamily.Lightning, Rarity.Epic).FamilyGate(2).Desc("Attacks may jump to a random enemy.")
                .On(TriggerKind.OnAttack, EffectKind.Damage, 50, TargetKind.RandomEnemy, 35);
            RunSkill("light_storm", "Static Storm", SkillFamily.Lightning, Rarity.Legendary).FamilyGate(3).Desc("Every round, lightning strikes all enemies.")
                .On(TriggerKind.OnRoundStart, EffectKind.Damage, 35, TargetKind.AllEnemies).Status(TriggerKind.OnRoundStart, 50, StatusId.Shock, 1, TargetKind.AllEnemies);

            // ---- Radiation (internal id Poison): unlimited stacks that never fade, each deals damage every round ----
            RunChain("poison_spit", SkillFamily.Poison, new[] { "Isotope Spit", "Isotope Spray", "Isotope Torrent" }, RunChainCRE, (b, t) =>
                b.Desc("Hits may irradiate.").Status(Hit, 30, StatusId.Poison, 2));
            RunSkill("poison_glands", "Reactor Glands", SkillFamily.Poison, Rarity.Rare).Desc("+15% radiation damage, +9 status chance.")
                .Stat(StatId.PoisonDamage, 15).Stat(StatId.StatusChance, 9);
            RunSkill("poison_miasma", "Fallout", SkillFamily.Poison, Rarity.Epic).FamilyGate(2).Desc("Irradiate all enemies at the end of each round.")
                .Status(TriggerKind.OnRoundEnd, 100, StatusId.Poison, 2, TargetKind.AllEnemies);
            RunSkill("poison_plague", "Contagion", SkillFamily.Poison, Rarity.Legendary).FamilyGate(3).Desc("Kills spread Radiation; +25% vs irradiated.")
                .Status(TriggerKind.OnKill, 100, StatusId.Poison, 4, TargetKind.AllEnemies).Cond("vs_status:Poison", 25);

            // ---- Bleed: up to 10 stacks that bleed every round; Bleed skills consume them for burst and lifesteal ----
            RunChain("bleed_serrated", SkillFamily.Bleed, new[] { "Serrated Edge", "Jagged Edge", "Butcher Edge" }, RunChainCRE, (b, t) =>
                b.Desc("Hits may cause Bleed.").Status(Hit, 30, StatusId.Bleed, 2));
            RunSkill("bleed_razor", "Razor Scales", SkillFamily.Bleed, Rarity.Rare).Desc("+15% bleed damage, +6% ATK.")
                .Stat(StatId.BleedDamage, 15).Stat(StatId.Atk, 0, 6);
            RunSkill("bleed_hemorrhage", "Hemorrhage", SkillFamily.Bleed, Rarity.Epic).FamilyGate(2).Desc("+30% damage vs bleeding enemies.")
                .Cond("vs_status:Bleed", 30);
            RunSkill("bleed_frenzy", "Blood Frenzy", SkillFamily.Bleed, Rarity.Legendary).FamilyGate(3).Desc("Rage attacks drink the enemy's Bleed.")
                .On(TriggerKind.OnRageAttack, EffectKind.ConsumeStatus, 25, TargetKind.Enemy, 100, 0, "heal:50");
            Skills["bleed_frenzy"].Triggers[0].Status = StatusId.Bleed;

            // ---- Alien Synergy (package) ------------------------------------------------------
            // Sidekick pass: aliens (and drones) are sidekicks that act with one ability; Alien skills make them stronger, act more
            // often, or hook the worm onto their actions.
            RunChain("alien_bond", SkillFamily.AlienSynergy, new[] { "Squad Bond", "Squad Unity", "Squad Soul" }, RunChainCRE, (b, t) =>
                b.Desc("Sidekick abilities are stronger.").Stat(StatId.AlienPower, 45));
            RunSkill("alien_rally", "Rally Cry", SkillFamily.AlienSynergy, Rarity.Rare).Desc("Battle start: your sidekicks act at once.")
                .Kick(TriggerKind.OnBattleStart, EffectKind.AllyAction, 2);
            RunSkill("alien_charmer", "Alien Charm", SkillFamily.AlienSynergy, Rarity.Epic).FamilyGate(2).Desc("Sidekick actions may charm the enemy.")
                .Status(TriggerKind.OnSidekickAct, 45, StatusId.Charm);
            RunSkill("alien_hivemind", "Hivemind", SkillFamily.AlienSynergy, Rarity.Legendary).FamilyGate(3).Desc("Sidekicks +40 power; each sidekick action heals the worm.")
                .Stat(StatId.AlienPower, 40).On(TriggerKind.OnSidekickAct, EffectKind.Heal, 6, TargetKind.Worm);

            // ---- Counter (package) ------------------------------------------------------------
            // was "Riposte" (the Counter signature is Riposte now)
            RunChain("counter_riposte", SkillFamily.Counter, new[] { "Counter Stance", "Swift Stance", "Master Stance" }, RunChainCRE, (b, t) =>
                b.Desc("Chance to counter-attack.").Stat(StatId.CounterChance, 15));
            RunSkill("counter_spiked", "Spiked Hide", SkillFamily.Counter, Rarity.Rare).Desc("Taking damage hurts the attacker.")
                .On(TriggerKind.OnDamaged, EffectKind.Damage, 24, TargetKind.Enemy);
            RunSkill("counter_parry", "Parry Stance", SkillFamily.Counter, Rarity.Epic).FamilyGate(2).Desc("+15 parry, parries block more.")
                .Stat(StatId.ParryChance, 15).Stat(StatId.ParryReduction, 20);
            RunSkill("counter_vengeance", "Vengeance", SkillFamily.Counter, Rarity.Legendary).FamilyGate(3).Desc("Counters deal +50% and grant rage.")
                .Cond("counter", 50).On(TriggerKind.OnCounter, EffectKind.GainRage, 20, TargetKind.Self);

            // ---- Combo (package) --------------------------------------------------------------
            // was "Flurry" (the Combo signature is Flurry now)
            RunChain("combo_flurry", SkillFamily.Combo, new[] { "Quick Hands", "Quicker Hands", "Blur Hands" }, RunChainCRE, (b, t) =>
                b.Desc("Chance to strike again.").Stat(StatId.ComboChance, 15));
            RunSkill("combo_momentum", "Momentum", SkillFamily.Combo, Rarity.Rare).Desc("Combos build rage.")
                .On(TriggerKind.OnCombo, EffectKind.GainRage, 12, TargetKind.Self);
            RunSkill("combo_rhythm", "Battle Rhythm", SkillFamily.Combo, Rarity.Epic).FamilyGate(2).Desc("Each combo: +3% ATK for the battle.")
                .Buff(TriggerKind.OnCombo, StatId.Atk, 3);
            RunSkill("combo_whirlwind", "Whirlwind", SkillFamily.Combo, Rarity.Legendary).FamilyGate(3).Desc("Combos hit all enemies.")
                .On(TriggerKind.OnCombo, EffectKind.Damage, 35, TargetKind.AllEnemies);
        }

        /// <summary>
        /// Signature skills (owner design): one per synergy, each does ONE simple thing; the run starts with one of three
        /// (RunSystem.SignatureOffer) and it awakens at 3 / 6 owned skills of its synergy (SignatureSystem: the same effect, a bigger
        /// number). Never drafted, bought or levelled by drafts.
        /// </summary>
        private void RunBuildSignatures()
        {
            RunSkillBuilder Sig(string id, string name, SkillFamily family, string desc)
            {
                var b = RunSkill(id, name, family, Rarity.Common).Desc(desc).NotDraftable();
                b.Def.Signature = true;
                return b;
            }
            Sig("sig_fire", "Ignite", SkillFamily.Fire, "Every hit heats the enemy.").Status(TriggerKind.OnHit, 100, StatusId.Heat);
            Sig("sig_radiation", "Irradiate", SkillFamily.Poison, "Every hit irradiates the enemy.").Status(TriggerKind.OnHit, 100, StatusId.Poison);
            Sig("sig_bleed", "Lacerate", SkillFamily.Bleed, "Every hit makes the enemy bleed.").Status(TriggerKind.OnHit, 100, StatusId.Bleed);
            Sig("sig_ice", "Chill", SkillFamily.Ice, "Every hit chills the enemy (and cools its Heat).").Status(TriggerKind.OnHit, 100, StatusId.Chill);
            Sig("sig_lightning", "Arc", SkillFamily.Lightning, "Every hit jumps to another enemy.")
                .Def.Triggers.Add(new SkillTrigger { When = TriggerKind.OnHit, Effect = EffectKind.Damage, Value = 50, Target = TargetKind.OtherEnemies, Stacks = 1 });
            Sig("sig_alien", "Command", SkillFamily.AlienSynergy, "Each round a sidekick takes an extra action.")
                .Def.Triggers.Add(new SkillTrigger { When = TriggerKind.OnRoundStart, Effect = EffectKind.AllyAction, Target = TargetKind.Self, Stacks = 1 });
            Sig("sig_counter", "Riposte", SkillFamily.Counter, "Hits against you may be answered.")
                .On(TriggerKind.OnDamaged, EffectKind.Strike, 0, TargetKind.Enemy, 30, 0, "counter");
            Sig("sig_combo", "Flurry", SkillFamily.Combo, "Hits may strike again.")
                .On(TriggerKind.OnHit, EffectKind.Strike, 0, TargetKind.Enemy, 30);
        }

        private void RunBuildGenericSkills()
        {
            RunChain("gen_vitality", SkillFamily.None, new[] { "Vitality", "Greater Vitality" }, new[] { Rarity.Common, Rarity.Rare }, (b, t) =>
                b.Desc("+max HP.").Stat(StatId.Hp, 0, 15));
            RunChain("gen_power", SkillFamily.None, new[] { "Power", "Greater Power" }, new[] { Rarity.Common, Rarity.Rare }, (b, t) =>
                b.Desc("+ATK.").Stat(StatId.Atk, 0, 15));
            RunSkill("gen_guard", "Guard", SkillFamily.None, Rarity.Common).Desc("+15% DEF.").Stat(StatId.Def, 0, 15);
            RunSkill("gen_swift", "Swift", SkillFamily.None, Rarity.Common).Desc("+12 Speed, +6 dodge.").Stat(StatId.Speed, 12).Stat(StatId.Dodge, 6);
            RunSkill("gen_regen", "Regrowth", SkillFamily.None, Rarity.Rare).Desc("Regenerate 2% HP per turn.").Stat(StatId.Regen, 2);
            RunSkill("gen_vampire", "Vampiric", SkillFamily.None, Rarity.Rare).Desc("+6% lifesteal.").Stat(StatId.LifeSteal, 6);
            RunSkill("gen_giant_slayer", "Giant Slayer", SkillFamily.None, Rarity.Rare).Desc("+18% damage vs enemies above 70% HP.").Cond("vs_high_hp", 18);
            RunSkill("gen_boss_hunter", "Boss Hunter", SkillFamily.None, Rarity.Epic).Desc("+25% boss damage.").Stat(StatId.BossDamage, 25);
            RunSkill("gen_shield_breaker", "Shield Breaker", SkillFamily.None, Rarity.Rare).Desc("+30% damage vs shielded enemies.").Cond("vs_shielded", 30);
            RunSkill("gen_wealth", "Prospector", SkillFamily.None, Rarity.Common).Desc("Won battles give 12 run coins.")
                .Board(TriggerKind.OnBattleWon, EffectKind.GainRunCoins, 12);
            RunSkill("gen_scholar", "Scholar", SkillFamily.None, Rarity.Common).Desc("+18% XP.").Stat(StatId.XpGain, 18);
            RunSkill("gen_pathfinder", "Pathfinder", SkillFamily.None, Rarity.Rare).Desc("Passing tiles gives 3 run coins.")
                .Board(TriggerKind.OnTilePass, EffectKind.GainRunCoins, 3);
            RunSkill("gen_camper", "Camper", SkillFamily.None, Rarity.Common).Desc("Landing on a campfire heals 15% more.")
                .Board(TriggerKind.OnTileLand, EffectKind.HealOnTile, 15, "tile:Campfire");
            RunSkill("gen_growth", "Growth Spurt", SkillFamily.None, Rarity.Epic).Desc("Every level-up: +2% ATK and HP for the run.")
                .Board(TriggerKind.OnLevelUp, EffectKind.RunStatGrowth, 0, null, 100, new StatMod(StatId.Atk, 0, 2))
                .Board(TriggerKind.OnLevelUp, EffectKind.RunStatGrowth, 0, null, 100, new StatMod(StatId.Hp, 0, 2));
            // was "each roll drains 1 awakening" (its levels could not grow: the meter drain is a whole pacing number)
            // the former Crit synergy's standalone picks (Crit is no synergy any more; Precision was folded away)
            RunChain("crit_keen", SkillFamily.None, new[] { "Keen Eye", "Hawk Eye", "Star Eye" }, RunChainCRE, (b, t) =>
                b.Desc("+crit chance.").Stat(StatId.CritChance, 15));
            RunSkill("crit_brutal", "Brutal Force", SkillFamily.None, Rarity.Rare).Desc("+24% crit damage.").Stat(StatId.CritDamage, 24);
            RunSkill("crit_executioner", "Executioner", SkillFamily.None, Rarity.Legendary).Desc("+40% damage vs enemies under 40% HP.")
                .Cond("vs_low_hp", 40);
            RunSkill("gen_calm_mind", "Calm Mind", SkillFamily.None, Rarity.Rare).Desc("The boss awakens slower: a longer run.")
                .Board(TriggerKind.OnRunStart, EffectKind.SlowAwakening, 9);
        }

        /// <summary>
        /// The Mythic pool (owner decision, signatures pass): one pool drawn at every miniboss choice, never gated by family counts
        /// or prerequisites and never in the normal draft or the shop. Every synergy brings ~3 themed Mythics (into the pool once
        /// the synergy is unlocked), plus generic ones (the former Crit Mythics among them); the choice favours owned synergies.
        /// </summary>
        private void RunBuildMythicSkills()
        {
            const TriggerKind Hit = TriggerKind.OnHit;
            // Fire
            RunSkill("fire_phoenix", "Phoenix Coil", SkillFamily.Fire, Rarity.Mythic).Desc("Supernovas heal you instead of hurting you.")
                .Stat(StatId.SupernovaGuard, 100).On(TriggerKind.OnSupernova, EffectKind.Heal, 6, TargetKind.Self);
            RunSkill("mythic_sunheart", "Sunheart Core", SkillFamily.Fire, Rarity.Mythic).Desc("A star burns in your core.")
                .Stat(StatId.SupernovaDamage, 40).Status(TriggerKind.OnBattleStart, 100, StatusId.Heat, 5, TargetKind.AllEnemies);
            RunSkill("mythic_cinder_storm", "Cinder Storm", SkillFamily.Fire, Rarity.Mythic).Desc("Ash rains on every foe; Supernovas spread it.")
                .Status(TriggerKind.OnRoundStart, 100, StatusId.Heat, 2, TargetKind.AllEnemies).Status(TriggerKind.OnSupernova, 100, StatusId.Heat, 2, TargetKind.AllEnemies);
            // Ice
            RunSkill("ice_absolute_zero", "Absolute Zero", SkillFamily.Ice, Rarity.Mythic).Desc("Freeze all enemies at battle start.")
                .Status(TriggerKind.OnBattleStart, 100, StatusId.Freeze, 1, TargetKind.AllEnemies).Stat(StatId.IceDamage, 30);
            RunSkill("mythic_winter_crown", "Winter Crown", SkillFamily.Ice, Rarity.Mythic).Desc("A crown of ice guards you.")
                .Stat(StatId.Def, 0, 30).Stat(StatId.DamageReduction, 10).On(TriggerKind.OnBattleStart, EffectKind.Shield, 25, TargetKind.Self);
            RunSkill("mythic_shatterpoint", "Shatterpoint", SkillFamily.Ice, Rarity.Mythic).Desc("Frozen foes break.")
                .Cond("vs_status:Freeze", 60).Status(TriggerKind.OnAttack, 25, StatusId.Freeze).Stat(StatId.IceDamage, 30);
            // Lightning
            RunSkill("light_thunder_god", "Thunder God", SkillFamily.Lightning, Rarity.Mythic).Desc("Rounds may stun a random enemy.")
                .Status(TriggerKind.OnRoundStart, 30, StatusId.Stun, 1, TargetKind.RandomEnemy).Stat(StatId.LightningDamage, 30);
            RunSkill("mythic_overcharge", "Overcharge", SkillFamily.Lightning, Rarity.Mythic).Desc("Lightning in your veins.")
                .Stat(StatId.Speed, 20).Stat(StatId.LightningDamage, 40).Status(Hit, 60, StatusId.Shock, 2);
            RunSkill("mythic_ball_lightning", "Ball Lightning", SkillFamily.Lightning, Rarity.Mythic).Desc("A living storm orbits you.")
                .On(TriggerKind.OnRoundStart, EffectKind.Damage, 60, TargetKind.RandomEnemy).Status(TriggerKind.OnRoundStart, 100, StatusId.Shock, 2, TargetKind.RandomEnemy);
            // Radiation
            RunSkill("poison_venom_heart", "Reactor Heart", SkillFamily.Poison, Rarity.Mythic).Desc("Regenerate; irradiate everything each round.")
                .Stat(StatId.Regen, 3).Status(TriggerKind.OnRoundStart, 100, StatusId.Poison, 3, TargetKind.AllEnemies);
            RunSkill("mythic_blightbloom", "Meltdown", SkillFamily.Poison, Rarity.Mythic).Desc("Your core runs hot.")
                .Stat(StatId.PoisonDamage, 60).Status(Hit, 100, StatusId.Poison, 2);
            RunSkill("mythic_toxic_aura", "Gamma Aura", SkillFamily.Poison, Rarity.Mythic).Desc("The air around you glows.")
                .Status(TriggerKind.OnRoundEnd, 100, StatusId.Poison, 2, TargetKind.AllEnemies).Cond("vs_status:Poison", 25);
            // Bleed
            RunSkill("bleed_crimson_tide", "Crimson Tide", SkillFamily.Bleed, Rarity.Mythic).Desc("Crits make all enemies bleed.")
                .Status(TriggerKind.OnCrit, 100, StatusId.Bleed, 2, TargetKind.AllEnemies).Stat(StatId.LifeSteal, 10);
            RunSkill("mythic_thousand_cuts", "Thousand Cuts", SkillFamily.Bleed, Rarity.Mythic).Desc("Every strike opens a wound.")
                .Stat(StatId.BleedDamage, 40).Status(Hit, 60, StatusId.Bleed, 2);
            RunSkill("mythic_bloodlust", "Bloodlust", SkillFamily.Bleed, Rarity.Mythic).Desc("Every few rounds you drink every wound.")
                .On(TriggerKind.OnRoundEnd, EffectKind.ConsumeStatus, 20, TargetKind.AllEnemies, 100, 3, "heal:60");
            Skills["mythic_bloodlust"].Triggers[0].Status = StatusId.Bleed;
            // Alien Synergy
            RunSkill("alien_overmind", "Overmind", SkillFamily.AlienSynergy, Rarity.Mythic).Desc("The squad fights as one mind: sidekicks act far more often.")
                .Stat(StatId.AlienPower, 50).Stat(StatId.SidekickHaste, 50);
            RunSkill("mythic_ark_command", "Ark Command", SkillFamily.AlienSynergy, Rarity.Mythic).Desc("Your squad opens every battle.")
                .Stat(StatId.AlienPower, 60).Kick(TriggerKind.OnBattleStart, EffectKind.AllyAction, 3);
            RunSkill("mythic_swarm_call", "Swarm Beacon", SkillFamily.AlienSynergy, Rarity.Mythic).Desc("Drones answer your call.")
                .Kick(TriggerKind.OnBattleStart, EffectKind.Summon, 3, 40, "sk_drone");
            // Counter
            RunSkill("counter_mirror", "Mirror Carapace", SkillFamily.Counter, Rarity.Mythic).Desc("Parries reflect true damage.")
                .On(TriggerKind.OnParry, EffectKind.TrueDamage, 100, TargetKind.Enemy).Stat(StatId.ParryChance, 10);
            RunSkill("mythic_thorn_king", "Thorn King", SkillFamily.Counter, Rarity.Mythic).Desc("Touching you hurts.")
                .Stat(StatId.CounterChance, 20).On(TriggerKind.OnDamaged, EffectKind.Damage, 50, TargetKind.Enemy);
            RunSkill("mythic_bastion", "Bastion", SkillFamily.Counter, Rarity.Mythic).Desc("Nothing gets through.")
                .Stat(StatId.ParryChance, 20).Stat(StatId.ParryReduction, 30).On(TriggerKind.OnParry, EffectKind.GainRage, 30, TargetKind.Self);
            // Combo
            RunSkill("combo_endless", "Endless Dance", SkillFamily.Combo, Rarity.Mythic).Desc("+25 combo; combos heal.")
                .Stat(StatId.ComboChance, 25).On(TriggerKind.OnCombo, EffectKind.Heal, 2, TargetKind.Self);
            RunSkill("mythic_blade_storm", "Blade Storm", SkillFamily.Combo, Rarity.Mythic).Desc("Faster and faster.")
                .Stat(StatId.ComboChance, 30).Buff(TriggerKind.OnCombo, StatId.Atk, 5);
            RunSkill("mythic_cascade", "Cascade", SkillFamily.Combo, Rarity.Mythic).Desc("Strikes spill onto the next foe.")
                .On(TriggerKind.OnCombo, EffectKind.Damage, 50, TargetKind.RandomEnemy).Stat(StatId.RageGain, 20);
            // generic (the former Crit Mythics included)
            RunSkill("crit_assassin", "Assassin's Mark", SkillFamily.None, Rarity.Mythic).Desc("Crits add a true-damage strike.")
                .On(TriggerKind.OnCrit, EffectKind.TrueDamage, 50, TargetKind.Enemy).Stat(StatId.CritDamage, 30);
            RunSkill("mythic_deadeye", "Deadeye", SkillFamily.None, Rarity.Mythic).Desc("Every shot finds the gap.")
                .Stat(StatId.CritChance, 25).Stat(StatId.CritDamage, 50);
            RunSkill("mythic_reaper", "Reaper", SkillFamily.None, Rarity.Mythic).Desc("Finish what you start.")
                .Cond("vs_low_hp", 60).On(TriggerKind.OnKill, EffectKind.GainRage, 40, TargetKind.Self);
            RunSkill("mythic_titan_blood", "Titan Blood", SkillFamily.None, Rarity.Mythic).Desc("Giant's blood.")
                .Stat(StatId.Hp, 0, 40).Stat(StatId.Atk, 0, 25).Stat(StatId.Regen, 3);
            RunSkill("mythic_warlord", "Warlord", SkillFamily.None, Rarity.Mythic).Desc("Born to topple bosses.")
                .Stat(StatId.BossDamage, 40).Stat(StatId.Atk, 0, 25);
            RunSkill("mythic_chrono_shell", "Chrono Shell", SkillFamily.None, Rarity.Mythic).Desc("Time bends around you.")
                .On(TriggerKind.OnBattleStart, EffectKind.Shield, 30, TargetKind.Self).Stat(StatId.DamageReduction, 10);
        }
    }
}
