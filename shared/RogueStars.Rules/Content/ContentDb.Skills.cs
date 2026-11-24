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
            RunBuildLabSkills();
        }

        // Numbers (owner tuning pass): a typical Common skill gives +15% at Lv1 and climbs the level ladder (SkillLeveling:
        // ×1, ×5/3, ×8/3 → +15% → +25% → +40%); Lv1 numbers are multiples of 3 so every level reads as whole numbers. A
        // Legendary gives ~+25% per pick (×1, ×8/5 → +25% → +40%), a Mythic one strong standalone package (it never levels).
        // Proc chances sit on the doubled ladder (30% → 50% → 80%); element damage stats are points (flat), like crit chance.
        private void RunBuildFamilySkills()
        {
            const TriggerKind Hit = TriggerKind.OnHit;

            // ---- Fire -------------------------------------------------------------------------
            RunChain("fire_ember", SkillFamily.Fire, new[] { "Ember Strikes", "Blazing Strikes", "Inferno Strikes" }, RunChainCRE, (b, t) =>
                b.Desc("Hits may Burn.").Status(Hit, 30, StatusId.Burn));
            RunSkill("fire_kindle", "Kindling Heart", SkillFamily.Fire, Rarity.Rare).Desc("+15% fire damage, +6% ATK.")
                .Stat(StatId.FireDamage, 15).Stat(StatId.Atk, 0, 6);
            RunSkill("fire_flare", "Solar Flare", SkillFamily.Fire, Rarity.Epic).FamilyGate(2).Desc("Rage attacks scorch all enemies.")
                .On(TriggerKind.OnRageAttack, EffectKind.Damage, 75, TargetKind.AllEnemies).Status(TriggerKind.OnRageAttack, 100, StatusId.Burn, 2, TargetKind.AllEnemies);
            RunSkill("fire_inferno", "Wildfire", SkillFamily.Fire, Rarity.Legendary).FamilyGate(3).Desc("+25% damage vs burning; kills spread Burn.")
                .Cond("vs_status:Burn", 25).Status(TriggerKind.OnKill, 100, StatusId.Burn, 3, TargetKind.AllEnemies);
            RunSkill("fire_phoenix", "Phoenix Coil", SkillFamily.Fire, Rarity.Mythic).FamilyGate(4).Requires("fire_inferno").Desc("At low HP, rise from the ashes.")
                .On(TriggerKind.OnLowHp, EffectKind.Heal, 40, TargetKind.Self).On(TriggerKind.OnLowHp, EffectKind.Shield, 20, TargetKind.Self)
                .Status(TriggerKind.OnLowHp, 100, StatusId.Burn, 5, TargetKind.AllEnemies);

            // ---- Ice --------------------------------------------------------------------------
            RunChain("ice_shard", SkillFamily.Ice, new[] { "Ice Shard", "Ice Lance", "Glacial Spear" }, RunChainCRE, (b, t) =>
                b.Desc("Attacks fire an ice shard that may Freeze.").On(TriggerKind.OnAttack, EffectKind.Damage, 36, TargetKind.Enemy, 35)
                 .Status(TriggerKind.OnAttack, 6, StatusId.Freeze));
            RunSkill("ice_armor", "Frost Armor", SkillFamily.Ice, Rarity.Rare).Desc("+15% DEF; start battles with a shield.")
                .Stat(StatId.Def, 0, 15).On(TriggerKind.OnBattleStart, EffectKind.Shield, 6, TargetKind.Self);
            RunSkill("ice_frostbite", "Frostbite", SkillFamily.Ice, Rarity.Epic).FamilyGate(2).Desc("+40% damage vs frozen enemies, +20% ice damage.")
                .Cond("vs_status:Freeze", 40).Stat(StatId.IceDamage, 20);
            RunSkill("ice_glacier", "Glacier Skin", SkillFamily.Ice, Rarity.Legendary).FamilyGate(3).Desc("When damaged, may freeze the attacker.")
                .Status(TriggerKind.OnDamaged, 20, StatusId.Freeze).Stat(StatId.DamageReduction, 10);
            RunSkill("ice_absolute_zero", "Absolute Zero", SkillFamily.Ice, Rarity.Mythic).FamilyGate(4).Requires("ice_glacier").Desc("Freeze all enemies at battle start.")
                .Status(TriggerKind.OnBattleStart, 100, StatusId.Freeze, 1, TargetKind.AllEnemies).Stat(StatId.IceDamage, 30);

            // ---- Lightning --------------------------------------------------------------------
            RunChain("light_spark", SkillFamily.Lightning, new[] { "Spark", "Arc", "Thunderclap" }, RunChainCRE, (b, t) =>
                b.Desc("Hits may Shock.").Status(Hit, 30, StatusId.Shock));
            RunSkill("light_conductor", "Conductor Coil", SkillFamily.Lightning, Rarity.Rare).Desc("+12 Speed, +15% lightning damage.")
                .Stat(StatId.Speed, 12).Stat(StatId.LightningDamage, 15);
            RunSkill("light_chain", "Chain Lightning", SkillFamily.Lightning, Rarity.Epic).FamilyGate(2).Desc("Attacks may jump to a random enemy.")
                .On(TriggerKind.OnAttack, EffectKind.Damage, 50, TargetKind.RandomEnemy, 35);
            RunSkill("light_storm", "Static Storm", SkillFamily.Lightning, Rarity.Legendary).FamilyGate(3).Desc("Every round, lightning strikes all enemies.")
                .On(TriggerKind.OnRoundStart, EffectKind.Damage, 35, TargetKind.AllEnemies).Status(TriggerKind.OnRoundStart, 50, StatusId.Shock, 1, TargetKind.AllEnemies);
            RunSkill("light_thunder_god", "Thunder God", SkillFamily.Lightning, Rarity.Mythic).FamilyGate(4).Requires("light_storm").Desc("Rounds may stun a random enemy.")
                .Status(TriggerKind.OnRoundStart, 30, StatusId.Stun, 1, TargetKind.RandomEnemy).Stat(StatId.LightningDamage, 30);

            // ---- Poison -----------------------------------------------------------------------
            RunChain("poison_spit", SkillFamily.Poison, new[] { "Venom Spit", "Venom Spray", "Venom Torrent" }, RunChainCRE, (b, t) =>
                b.Desc("Hits may Poison.").Status(Hit, 30, StatusId.Poison, 2));
            RunSkill("poison_glands", "Toxic Glands", SkillFamily.Poison, Rarity.Rare).Desc("+15% poison damage, +9 status chance.")
                .Stat(StatId.PoisonDamage, 15).Stat(StatId.StatusChance, 9);
            RunSkill("poison_miasma", "Miasma", SkillFamily.Poison, Rarity.Epic).FamilyGate(2).Desc("Poison all enemies at the end of each round.")
                .Status(TriggerKind.OnRoundEnd, 100, StatusId.Poison, 2, TargetKind.AllEnemies);
            RunSkill("poison_plague", "Plague Bearer", SkillFamily.Poison, Rarity.Legendary).FamilyGate(3).Desc("Kills spread heavy Poison; +25% vs poisoned.")
                .Status(TriggerKind.OnKill, 100, StatusId.Poison, 4, TargetKind.AllEnemies).Cond("vs_status:Poison", 25);
            RunSkill("poison_venom_heart", "Venom Heart", SkillFamily.Poison, Rarity.Mythic).FamilyGate(4).Requires("poison_plague").Desc("Regenerate; poison everything each round.")
                .Stat(StatId.Regen, 3).Status(TriggerKind.OnRoundStart, 100, StatusId.Poison, 3, TargetKind.AllEnemies);

            // ---- Bleed ------------------------------------------------------------------------
            RunChain("bleed_serrated", SkillFamily.Bleed, new[] { "Serrated Edge", "Jagged Edge", "Butcher Edge" }, RunChainCRE, (b, t) =>
                b.Desc("Hits may cause Bleed.").Status(Hit, 30, StatusId.Bleed));
            RunSkill("bleed_razor", "Razor Scales", SkillFamily.Bleed, Rarity.Rare).Desc("+6% ATK, +15% bleed damage.")
                .Stat(StatId.Atk, 0, 6).Stat(StatId.BleedDamage, 15);
            RunSkill("bleed_hemorrhage", "Hemorrhage", SkillFamily.Bleed, Rarity.Epic).FamilyGate(2).Desc("+30% damage vs bleeding enemies.")
                .Cond("vs_status:Bleed", 30);
            RunSkill("bleed_frenzy", "Blood Frenzy", SkillFamily.Bleed, Rarity.Legendary).FamilyGate(3).Desc("+10% lifesteal; kills grant rage.")
                .Stat(StatId.LifeSteal, 10).On(TriggerKind.OnKill, EffectKind.GainRage, 50, TargetKind.Self);
            RunSkill("bleed_crimson_tide", "Crimson Tide", SkillFamily.Bleed, Rarity.Mythic).FamilyGate(4).Requires("bleed_frenzy").Desc("Crits make all enemies bleed.")
                .Status(TriggerKind.OnCrit, 100, StatusId.Bleed, 2, TargetKind.AllEnemies).Stat(StatId.LifeSteal, 10);

            // ---- Alien Synergy ----------------------------------------------------------------
            RunChain("alien_bond", SkillFamily.AlienSynergy, new[] { "Squad Bond", "Squad Unity", "Squad Soul" }, RunChainCRE, (b, t) =>
                b.Desc("Aliens hit harder.").Stat(StatId.AlienPower, 24));
            RunSkill("alien_rally", "Rally Cry", SkillFamily.AlienSynergy, Rarity.Rare).Desc("Battle start: allies +15% ATK for 3 rounds.")
                .Buff(TriggerKind.OnBattleStart, StatId.Atk, 15, TargetKind.AllAllies, 3);
            RunSkill("alien_charmer", "Alien Charm", SkillFamily.AlienSynergy, Rarity.Epic).FamilyGate(2).Desc("Attacks may charm the target.")
                .Status(TriggerKind.OnAttack, 15, StatusId.Charm);
            RunSkill("alien_hivemind", "Hivemind", SkillFamily.AlienSynergy, Rarity.Legendary).FamilyGate(3).Desc("Aliens +40%; the squad heals the worm.")
                .Stat(StatId.AlienPower, 40).On(TriggerKind.OnRoundStart, EffectKind.Heal, 3, TargetKind.Worm);
            // the family capstone (was Mothership Link, a drone summon; drones are gone): the squad's rage engine
            RunSkill("alien_overmind", "Overmind", SkillFamily.AlienSynergy, Rarity.Mythic).FamilyGate(4).Requires("alien_hivemind").Desc("The squad fights as one mind.")
                .Stat(StatId.AlienPower, 50).On(TriggerKind.OnRoundStart, EffectKind.GainRage, 25, TargetKind.AllAllies);

            // ---- Counter ----------------------------------------------------------------------
            RunChain("counter_riposte", SkillFamily.Counter, new[] { "Riposte", "Swift Riposte", "Master Riposte" }, RunChainCRE, (b, t) =>
                b.Desc("Chance to counter-attack.").Stat(StatId.CounterChance, 15));
            RunSkill("counter_spiked", "Spiked Hide", SkillFamily.Counter, Rarity.Rare).Desc("Taking damage hurts the attacker.")
                .On(TriggerKind.OnDamaged, EffectKind.Damage, 24, TargetKind.Enemy);
            RunSkill("counter_parry", "Parry Stance", SkillFamily.Counter, Rarity.Epic).FamilyGate(2).Desc("+15 parry, parries block more.")
                .Stat(StatId.ParryChance, 15).Stat(StatId.ParryReduction, 20);
            RunSkill("counter_vengeance", "Vengeance", SkillFamily.Counter, Rarity.Legendary).FamilyGate(3).Desc("Counters deal +50% and grant rage.")
                .Cond("counter", 50).On(TriggerKind.OnCounter, EffectKind.GainRage, 20, TargetKind.Self);
            RunSkill("counter_mirror", "Mirror Carapace", SkillFamily.Counter, Rarity.Mythic).FamilyGate(4).Requires("counter_vengeance").Desc("Parries reflect true damage.")
                .On(TriggerKind.OnParry, EffectKind.TrueDamage, 100, TargetKind.Enemy).Stat(StatId.ParryChance, 10);

            // ---- Combo ------------------------------------------------------------------------
            RunChain("combo_flurry", SkillFamily.Combo, new[] { "Flurry", "Rapid Flurry", "Endless Flurry" }, RunChainCRE, (b, t) =>
                b.Desc("Chance to strike again.").Stat(StatId.ComboChance, 15));
            RunSkill("combo_momentum", "Momentum", SkillFamily.Combo, Rarity.Rare).Desc("Combos build rage.")
                .On(TriggerKind.OnCombo, EffectKind.GainRage, 12, TargetKind.Self);
            RunSkill("combo_rhythm", "Battle Rhythm", SkillFamily.Combo, Rarity.Epic).FamilyGate(2).Desc("Each combo: +3% ATK for the battle.")
                .Buff(TriggerKind.OnCombo, StatId.Atk, 3);
            RunSkill("combo_whirlwind", "Whirlwind", SkillFamily.Combo, Rarity.Legendary).FamilyGate(3).Desc("Combos hit all enemies.")
                .On(TriggerKind.OnCombo, EffectKind.Damage, 35, TargetKind.AllEnemies);
            RunSkill("combo_endless", "Endless Dance", SkillFamily.Combo, Rarity.Mythic).FamilyGate(4).Requires("combo_whirlwind").Desc("+25 combo; combos heal.")
                .Stat(StatId.ComboChance, 25).On(TriggerKind.OnCombo, EffectKind.Heal, 2, TargetKind.Self);

            // ---- Crit -------------------------------------------------------------------------
            RunChain("crit_keen", SkillFamily.Crit, new[] { "Keen Eye", "Hawk Eye", "Star Eye" }, RunChainCRE, (b, t) =>
                b.Desc("+crit chance.").Stat(StatId.CritChance, 15));
            RunSkill("crit_brutal", "Brutal Force", SkillFamily.Crit, Rarity.Rare).Desc("+24% crit damage.").Stat(StatId.CritDamage, 24);
            RunSkill("crit_precision", "Precision", SkillFamily.Crit, Rarity.Epic).FamilyGate(2).Desc("+20 dodge ignore, +10 crit.")
                .Stat(StatId.DodgeIgnore, 20).Stat(StatId.CritChance, 10);
            RunSkill("crit_executioner", "Executioner", SkillFamily.Crit, Rarity.Legendary).FamilyGate(3).Desc("+40% damage vs enemies under 40% HP.")
                .Cond("vs_low_hp", 40);
            RunSkill("crit_assassin", "Assassin's Mark", SkillFamily.Crit, Rarity.Mythic).FamilyGate(4).Requires("crit_executioner").Desc("Crits add a true-damage strike.")
                .On(TriggerKind.OnCrit, EffectKind.TrueDamage, 50, TargetKind.Enemy).Stat(StatId.CritDamage, 30);
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
            RunSkill("gen_calm_mind", "Calm Mind", SkillFamily.None, Rarity.Rare).Desc("The boss awakens slower: a longer run.")
                .Board(TriggerKind.OnRunStart, EffectKind.SlowAwakening, 9);
        }

        /// <summary>
        /// Mythic skills for the miniboss choice (GDD §3.3 "Build"): two per family (one for Alien Synergy) plus three generic
        /// ones, no family gate, so every beaten miniboss offers 3 of the ~30 Mythics (the 9 gated family capstones above join
        /// that choice too).
        /// </summary>
        private void RunBuildMythicSkills()
        {
            const TriggerKind Hit = TriggerKind.OnHit;
            RunSkill("mythic_sunheart", "Sunheart Core", SkillFamily.Fire, Rarity.Mythic).Desc("A star burns in your core.")
                .Stat(StatId.FireDamage, 40).Stat(StatId.Atk, 0, 10).Status(TriggerKind.OnBattleStart, 100, StatusId.Burn, 3, TargetKind.AllEnemies);
            RunSkill("mythic_cinder_storm", "Cinder Storm", SkillFamily.Fire, Rarity.Mythic).Desc("Ash rains on every foe.")
                .On(TriggerKind.OnRoundStart, EffectKind.Damage, 30, TargetKind.AllEnemies).Status(Hit, 50, StatusId.Burn, 2);
            RunSkill("mythic_winter_crown", "Winter Crown", SkillFamily.Ice, Rarity.Mythic).Desc("A crown of ice guards you.")
                .Stat(StatId.Def, 0, 30).Stat(StatId.DamageReduction, 10).On(TriggerKind.OnBattleStart, EffectKind.Shield, 25, TargetKind.Self);
            RunSkill("mythic_shatterpoint", "Shatterpoint", SkillFamily.Ice, Rarity.Mythic).Desc("Frozen foes break.")
                .Cond("vs_status:Freeze", 60).Status(TriggerKind.OnAttack, 25, StatusId.Freeze).Stat(StatId.IceDamage, 30);
            RunSkill("mythic_overcharge", "Overcharge", SkillFamily.Lightning, Rarity.Mythic).Desc("Lightning in your veins.")
                .Stat(StatId.Speed, 20).Stat(StatId.LightningDamage, 40).Status(Hit, 60, StatusId.Shock, 2);
            RunSkill("mythic_ball_lightning", "Ball Lightning", SkillFamily.Lightning, Rarity.Mythic).Desc("A living storm orbits you.")
                .On(TriggerKind.OnRoundStart, EffectKind.Damage, 60, TargetKind.RandomEnemy).Status(TriggerKind.OnRoundStart, 100, StatusId.Shock, 2, TargetKind.RandomEnemy);
            RunSkill("mythic_blightbloom", "Blightbloom", SkillFamily.Poison, Rarity.Mythic).Desc("Your venom blossoms.")
                .Stat(StatId.PoisonDamage, 50).Stat(StatId.StatusChance, 15).Status(Hit, 60, StatusId.Poison, 3);
            RunSkill("mythic_toxic_aura", "Toxic Aura", SkillFamily.Poison, Rarity.Mythic).Desc("The air around you is poison.")
                .Status(TriggerKind.OnRoundEnd, 100, StatusId.Poison, 2, TargetKind.AllEnemies).Cond("vs_status:Poison", 25);
            RunSkill("mythic_thousand_cuts", "Thousand Cuts", SkillFamily.Bleed, Rarity.Mythic).Desc("Every strike opens a wound.")
                .Stat(StatId.BleedDamage, 40).Status(Hit, 60, StatusId.Bleed, 2);
            RunSkill("mythic_bloodlust", "Bloodlust", SkillFamily.Bleed, Rarity.Mythic).Desc("Blood makes you whole.")
                .Stat(StatId.LifeSteal, 15).On(TriggerKind.OnKill, EffectKind.Heal, 15, TargetKind.Self);
            RunSkill("mythic_ark_command", "Ark Command", SkillFamily.AlienSynergy, Rarity.Mythic).Desc("Your squad fights as one.")
                .Stat(StatId.AlienPower, 60).Buff(TriggerKind.OnBattleStart, StatId.Atk, 20, TargetKind.AllAllies, 3);
            RunSkill("mythic_thorn_king", "Thorn King", SkillFamily.Counter, Rarity.Mythic).Desc("Touching you hurts.")
                .Stat(StatId.CounterChance, 20).On(TriggerKind.OnDamaged, EffectKind.Damage, 50, TargetKind.Enemy);
            RunSkill("mythic_bastion", "Bastion", SkillFamily.Counter, Rarity.Mythic).Desc("Nothing gets through.")
                .Stat(StatId.ParryChance, 20).Stat(StatId.ParryReduction, 30).On(TriggerKind.OnParry, EffectKind.GainRage, 30, TargetKind.Self);
            RunSkill("mythic_blade_storm", "Blade Storm", SkillFamily.Combo, Rarity.Mythic).Desc("Faster and faster.")
                .Stat(StatId.ComboChance, 30).Buff(TriggerKind.OnCombo, StatId.Atk, 5);
            RunSkill("mythic_cascade", "Cascade", SkillFamily.Combo, Rarity.Mythic).Desc("Strikes spill onto the next foe.")
                .On(TriggerKind.OnCombo, EffectKind.Damage, 50, TargetKind.RandomEnemy).Stat(StatId.RageGain, 20);
            RunSkill("mythic_deadeye", "Deadeye", SkillFamily.Crit, Rarity.Mythic).Desc("Every shot finds the gap.")
                .Stat(StatId.CritChance, 25).Stat(StatId.CritDamage, 50);
            RunSkill("mythic_reaper", "Reaper", SkillFamily.Crit, Rarity.Mythic).Desc("Finish what you start.")
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
