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

    /// <summary>Run skills (~76 draftable, 9 families + generic); the Laboratory tree is in ContentDb.Laboratory.cs.</summary>
    public sealed partial class ContentDb
    {
        private RunSkillBuilder RunSkill(string id, string name, SkillFamily family, Rarity rarity, SkillSource source = SkillSource.Run)
        {
            var s = new SkillDef { Id = id, Name = name, Family = family, Rarity = rarity, Source = source };
            AddSkill(s);
            return new RunSkillBuilder(s);
        }

        /// <summary>Creates id, id_2, id_3 as an upgrade chain (each replaces the previous when drafted).</summary>
        private void RunChain(string id, SkillFamily family, string[] names, Rarity[] rarities, Action<RunSkillBuilder, int> fill)
        {
            string prev = null;
            for (int tier = 1; tier <= names.Length; tier++)
            {
                string sid = tier == 1 ? id : id + "_" + tier;
                var b = RunSkill(sid, names[tier - 1], family, rarities[tier - 1]);
                if (prev != null) b.Upgrades(prev);
                fill(b, tier);
                prev = sid;
            }
        }

        private static readonly Rarity[] RunChainCRE = { Rarity.Common, Rarity.Rare, Rarity.Epic };

        partial void BuildSkills()
        {
            RunBuildFamilySkills();
            RunBuildGenericSkills();
            RunBuildLabSkills();
        }

        private void RunBuildFamilySkills()
        {
            const TriggerKind Hit = TriggerKind.OnHit;

            // ---- Fire -------------------------------------------------------------------------
            RunChain("fire_ember", SkillFamily.Fire, new[] { "Ember Strikes", "Blazing Strikes", "Inferno Strikes" }, RunChainCRE, (b, t) =>
                b.Desc("Hits may Burn.").Status(Hit, 20 + 12 * t, StatusId.Burn, t >= 2 ? 2 : 1).Stat(StatId.FireDamage, 0, t == 3 ? 15 : 0));
            RunSkill("fire_kindle", "Kindling Heart", SkillFamily.Fire, Rarity.Rare).Desc("+20% fire damage, +5% ATK.")
                .Stat(StatId.FireDamage, 0, 20).Stat(StatId.Atk, 0, 5);
            RunSkill("fire_flare", "Solar Flare", SkillFamily.Fire, Rarity.Epic).FamilyGate(2).Desc("Rage attacks scorch all enemies.")
                .On(TriggerKind.OnRageAttack, EffectKind.Damage, 70, TargetKind.AllEnemies).Status(TriggerKind.OnRageAttack, 100, StatusId.Burn, 2, TargetKind.AllEnemies);
            RunSkill("fire_inferno", "Wildfire", SkillFamily.Fire, Rarity.Legendary).FamilyGate(3).Desc("+25% damage vs burning; kills spread Burn.")
                .Cond("vs_status:Burn", 25).Status(TriggerKind.OnKill, 100, StatusId.Burn, 3, TargetKind.AllEnemies);
            RunSkill("fire_phoenix", "Phoenix Coil", SkillFamily.Fire, Rarity.Mythic).FamilyGate(4).Requires("fire_inferno").Desc("At low HP, rise from the ashes.")
                .On(TriggerKind.OnLowHp, EffectKind.Heal, 40, TargetKind.Self).On(TriggerKind.OnLowHp, EffectKind.Shield, 20, TargetKind.Self)
                .Status(TriggerKind.OnLowHp, 100, StatusId.Burn, 5, TargetKind.AllEnemies);

            // ---- Ice --------------------------------------------------------------------------
            RunChain("ice_shard", SkillFamily.Ice, new[] { "Ice Shard", "Ice Lance", "Glacial Spear" }, RunChainCRE, (b, t) =>
                b.Desc("Attacks fire an ice shard that may Freeze.").On(TriggerKind.OnAttack, EffectKind.Damage, 25 + 10 * t, TargetKind.Enemy, 35)
                 .Status(TriggerKind.OnAttack, 6 + 4 * t, StatusId.Freeze));
            RunSkill("ice_armor", "Frost Armor", SkillFamily.Ice, Rarity.Rare).Desc("+15% DEF; start battles with a shield.")
                .Stat(StatId.Def, 0, 15).On(TriggerKind.OnBattleStart, EffectKind.Shield, 10, TargetKind.Self);
            RunSkill("ice_frostbite", "Frostbite", SkillFamily.Ice, Rarity.Epic).FamilyGate(2).Desc("+40% damage vs frozen enemies, +20% ice damage.")
                .Cond("vs_status:Freeze", 40).Stat(StatId.IceDamage, 0, 20);
            RunSkill("ice_glacier", "Glacier Skin", SkillFamily.Ice, Rarity.Legendary).FamilyGate(3).Desc("When damaged, may freeze the attacker.")
                .Status(TriggerKind.OnDamaged, 20, StatusId.Freeze).Stat(StatId.DamageReduction, 8);
            RunSkill("ice_absolute_zero", "Absolute Zero", SkillFamily.Ice, Rarity.Mythic).FamilyGate(4).Requires("ice_glacier").Desc("Freeze all enemies at battle start.")
                .Status(TriggerKind.OnBattleStart, 100, StatusId.Freeze, 1, TargetKind.AllEnemies).Stat(StatId.IceDamage, 0, 30);

            // ---- Lightning --------------------------------------------------------------------
            RunChain("light_spark", SkillFamily.Lightning, new[] { "Spark", "Arc", "Thunderclap" }, RunChainCRE, (b, t) =>
                b.Desc("Hits may Shock.").Status(Hit, 20 + 12 * t, StatusId.Shock, t >= 3 ? 2 : 1));
            RunSkill("light_conductor", "Conductor Coil", SkillFamily.Lightning, Rarity.Rare).Desc("+10 Speed, +20% lightning damage.")
                .Stat(StatId.Speed, 10).Stat(StatId.LightningDamage, 0, 20);
            RunSkill("light_chain", "Chain Lightning", SkillFamily.Lightning, Rarity.Epic).FamilyGate(2).Desc("Attacks may jump to a random enemy.")
                .On(TriggerKind.OnAttack, EffectKind.Damage, 50, TargetKind.RandomEnemy, 35);
            RunSkill("light_storm", "Static Storm", SkillFamily.Lightning, Rarity.Legendary).FamilyGate(3).Desc("Every round, lightning strikes all enemies.")
                .On(TriggerKind.OnRoundStart, EffectKind.Damage, 35, TargetKind.AllEnemies).Status(TriggerKind.OnRoundStart, 50, StatusId.Shock, 1, TargetKind.AllEnemies);
            RunSkill("light_thunder_god", "Thunder God", SkillFamily.Lightning, Rarity.Mythic).FamilyGate(4).Requires("light_storm").Desc("Rounds may stun a random enemy.")
                .Status(TriggerKind.OnRoundStart, 30, StatusId.Stun, 1, TargetKind.RandomEnemy).Stat(StatId.LightningDamage, 0, 30);

            // ---- Poison -----------------------------------------------------------------------
            RunChain("poison_spit", SkillFamily.Poison, new[] { "Venom Spit", "Venom Spray", "Venom Torrent" }, RunChainCRE, (b, t) =>
                b.Desc("Hits may Poison.").Status(Hit, 25 + 10 * t, StatusId.Poison, 1 + t));
            RunSkill("poison_glands", "Toxic Glands", SkillFamily.Poison, Rarity.Rare).Desc("+25% poison damage, +10 status chance.")
                .Stat(StatId.PoisonDamage, 0, 25).Stat(StatId.StatusChance, 10);
            RunSkill("poison_miasma", "Miasma", SkillFamily.Poison, Rarity.Epic).FamilyGate(2).Desc("Poison all enemies at the end of each round.")
                .Status(TriggerKind.OnRoundEnd, 100, StatusId.Poison, 1, TargetKind.AllEnemies);
            RunSkill("poison_plague", "Plague Bearer", SkillFamily.Poison, Rarity.Legendary).FamilyGate(3).Desc("Kills spread heavy Poison; +20% vs poisoned.")
                .Status(TriggerKind.OnKill, 100, StatusId.Poison, 4, TargetKind.AllEnemies).Cond("vs_status:Poison", 20);
            RunSkill("poison_venom_heart", "Venom Heart", SkillFamily.Poison, Rarity.Mythic).FamilyGate(4).Requires("poison_plague").Desc("Regenerate; poison everything each round.")
                .Stat(StatId.Regen, 3).Status(TriggerKind.OnRoundStart, 100, StatusId.Poison, 3, TargetKind.AllEnemies);

            // ---- Bleed ------------------------------------------------------------------------
            RunChain("bleed_serrated", SkillFamily.Bleed, new[] { "Serrated Edge", "Jagged Edge", "Butcher Edge" }, RunChainCRE, (b, t) =>
                b.Desc("Hits may cause Bleed.").Status(Hit, 20 + 12 * t, StatusId.Bleed, t >= 3 ? 2 : 1));
            RunSkill("bleed_razor", "Razor Scales", SkillFamily.Bleed, Rarity.Rare).Desc("+8% ATK, +20% bleed damage.")
                .Stat(StatId.Atk, 0, 8).Stat(StatId.BleedDamage, 0, 20);
            RunSkill("bleed_hemorrhage", "Hemorrhage", SkillFamily.Bleed, Rarity.Epic).FamilyGate(2).Desc("+30% damage vs bleeding enemies.")
                .Cond("vs_status:Bleed", 30);
            RunSkill("bleed_frenzy", "Blood Frenzy", SkillFamily.Bleed, Rarity.Legendary).FamilyGate(3).Desc("+10% lifesteal; kills grant rage.")
                .Stat(StatId.LifeSteal, 10).On(TriggerKind.OnKill, EffectKind.GainRage, 50, TargetKind.Self);
            RunSkill("bleed_crimson_tide", "Crimson Tide", SkillFamily.Bleed, Rarity.Mythic).FamilyGate(4).Requires("bleed_frenzy").Desc("Crits make all enemies bleed.")
                .Status(TriggerKind.OnCrit, 100, StatusId.Bleed, 2, TargetKind.AllEnemies).Stat(StatId.LifeSteal, 10);

            // ---- Alien Synergy ----------------------------------------------------------------
            RunChain("alien_bond", SkillFamily.AlienSynergy, new[] { "Squad Bond", "Squad Unity", "Squad Soul" }, RunChainCRE, (b, t) =>
                b.Desc("Aliens hit harder.").Stat(StatId.AlienPower, 10 + 12 * t));
            RunSkill("alien_rally", "Rally Cry", SkillFamily.AlienSynergy, Rarity.Rare).Desc("Battle start: allies +12% ATK for 3 rounds.")
                .Buff(TriggerKind.OnBattleStart, StatId.Atk, 12, TargetKind.AllAllies, 3);
            RunSkill("alien_charmer", "Alien Charm", SkillFamily.AlienSynergy, Rarity.Epic).FamilyGate(2).Desc("Attacks may charm the target.")
                .Status(TriggerKind.OnAttack, 15, StatusId.Charm);
            RunSkill("alien_hivemind", "Hivemind", SkillFamily.AlienSynergy, Rarity.Legendary).FamilyGate(3).Desc("Aliens +40%; the squad heals the worm.")
                .Stat(StatId.AlienPower, 40).On(TriggerKind.OnRoundStart, EffectKind.Heal, 3, TargetKind.Worm);
            RunSkill("alien_mothership", "Mothership Link", SkillFamily.AlienSynergy, Rarity.Mythic).FamilyGate(4).Requires("alien_hivemind").Desc("Summon an attack drone at battle start.")
                .On(TriggerKind.OnBattleStart, EffectKind.Summon, 50, TargetKind.Self, 100, 0, "drone");

            // ---- Counter ----------------------------------------------------------------------
            RunChain("counter_riposte", SkillFamily.Counter, new[] { "Riposte", "Swift Riposte", "Master Riposte" }, RunChainCRE, (b, t) =>
                b.Desc("Chance to counter-attack.").Stat(StatId.CounterChance, 6 + 7 * t));
            RunSkill("counter_spiked", "Spiked Hide", SkillFamily.Counter, Rarity.Rare).Desc("Taking damage hurts the attacker.")
                .On(TriggerKind.OnDamaged, EffectKind.Damage, 25, TargetKind.Enemy);
            RunSkill("counter_parry", "Parry Stance", SkillFamily.Counter, Rarity.Epic).FamilyGate(2).Desc("+15 parry, parries block more.")
                .Stat(StatId.ParryChance, 15).Stat(StatId.ParryReduction, 20);
            RunSkill("counter_vengeance", "Vengeance", SkillFamily.Counter, Rarity.Legendary).FamilyGate(3).Desc("Counters deal +50% and grant rage.")
                .Cond("counter", 50).On(TriggerKind.OnCounter, EffectKind.GainRage, 20, TargetKind.Self);
            RunSkill("counter_mirror", "Mirror Carapace", SkillFamily.Counter, Rarity.Mythic).FamilyGate(4).Requires("counter_vengeance").Desc("Parries reflect true damage.")
                .On(TriggerKind.OnParry, EffectKind.TrueDamage, 100, TargetKind.Enemy).Stat(StatId.ParryChance, 10);

            // ---- Combo ------------------------------------------------------------------------
            RunChain("combo_flurry", SkillFamily.Combo, new[] { "Flurry", "Rapid Flurry", "Endless Flurry" }, RunChainCRE, (b, t) =>
                b.Desc("Chance to strike again.").Stat(StatId.ComboChance, 6 + 7 * t));
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
                b.Desc("+crit chance.").Stat(StatId.CritChance, 5 + 5 * t));
            RunSkill("crit_brutal", "Brutal Force", SkillFamily.Crit, Rarity.Rare).Desc("+25% crit damage.").Stat(StatId.CritDamage, 25);
            RunSkill("crit_precision", "Precision", SkillFamily.Crit, Rarity.Epic).FamilyGate(2).Desc("+15 dodge ignore, +10 crit.")
                .Stat(StatId.DodgeIgnore, 15).Stat(StatId.CritChance, 10);
            RunSkill("crit_executioner", "Executioner", SkillFamily.Crit, Rarity.Legendary).FamilyGate(3).Desc("+40% damage vs enemies under 40% HP.")
                .Cond("vs_low_hp", 40);
            RunSkill("crit_assassin", "Assassin's Mark", SkillFamily.Crit, Rarity.Mythic).FamilyGate(4).Requires("crit_executioner").Desc("Crits add a true-damage strike.")
                .On(TriggerKind.OnCrit, EffectKind.TrueDamage, 50, TargetKind.Enemy).Stat(StatId.CritDamage, 30);
        }

        private void RunBuildGenericSkills()
        {
            RunChain("gen_vitality", SkillFamily.None, new[] { "Vitality", "Greater Vitality" }, new[] { Rarity.Common, Rarity.Rare }, (b, t) =>
                b.Desc("+max HP.").Stat(StatId.Hp, 0, 8 * t + 2));
            RunChain("gen_power", SkillFamily.None, new[] { "Power", "Greater Power" }, new[] { Rarity.Common, Rarity.Rare }, (b, t) =>
                b.Desc("+ATK.").Stat(StatId.Atk, 0, 7 * t + 1));
            RunSkill("gen_guard", "Guard", SkillFamily.None, Rarity.Common).Desc("+12% DEF.").Stat(StatId.Def, 0, 12);
            RunSkill("gen_swift", "Swift", SkillFamily.None, Rarity.Common).Desc("+10 Speed, +5 dodge.").Stat(StatId.Speed, 10).Stat(StatId.Dodge, 5);
            RunSkill("gen_regen", "Regrowth", SkillFamily.None, Rarity.Rare).Desc("Regenerate 2% HP per turn.").Stat(StatId.Regen, 2);
            RunSkill("gen_vampire", "Vampiric", SkillFamily.None, Rarity.Rare).Desc("+6% lifesteal.").Stat(StatId.LifeSteal, 6);
            RunSkill("gen_giant_slayer", "Giant Slayer", SkillFamily.None, Rarity.Rare).Desc("+20% damage vs enemies above 70% HP.").Cond("vs_high_hp", 20);
            RunSkill("gen_boss_hunter", "Boss Hunter", SkillFamily.None, Rarity.Epic).Desc("+25% boss damage.").Stat(StatId.BossDamage, 25);
            RunSkill("gen_shield_breaker", "Shield Breaker", SkillFamily.None, Rarity.Rare).Desc("+30% damage vs shielded enemies.").Cond("vs_shielded", 30);
            RunSkill("gen_wealth", "Prospector", SkillFamily.None, Rarity.Common).Desc("Won battles give 10 run coins.")
                .Board(TriggerKind.OnBattleWon, EffectKind.GainRunCoins, 10);
            RunSkill("gen_scholar", "Scholar", SkillFamily.None, Rarity.Common).Desc("+20% XP.").Stat(StatId.XpGain, 20);
            RunSkill("gen_pathfinder", "Pathfinder", SkillFamily.None, Rarity.Rare).Desc("Passing tiles gives 2 run coins.")
                .Board(TriggerKind.OnTilePass, EffectKind.GainRunCoins, 2);
            RunSkill("gen_camper", "Camper", SkillFamily.None, Rarity.Common).Desc("Landing on a campfire heals 20% more.")
                .Board(TriggerKind.OnTileLand, EffectKind.HealOnTile, 20, "tile:Campfire");
            RunSkill("gen_growth", "Growth Spurt", SkillFamily.None, Rarity.Epic).Desc("Every level-up: +2% ATK and HP for the run.")
                .Board(TriggerKind.OnLevelUp, EffectKind.RunStatGrowth, 0, null, 100, new StatMod(StatId.Atk, 0, 2))
                .Board(TriggerKind.OnLevelUp, EffectKind.RunStatGrowth, 0, null, 100, new StatMod(StatId.Hp, 0, 2));
            RunSkill("gen_calm_mind", "Calm Mind", SkillFamily.None, Rarity.Rare).Desc("Each roll drains 1 awakening.")
                .Board(TriggerKind.OnRoll, EffectKind.SlowAwakening, 1);
            RunSkill("gen_hoarder", "Hoarder", SkillFamily.None, Rarity.Legendary).Desc("+20% pending loot on completion.")
                .Board(TriggerKind.OnRunStart, EffectKind.ExtraLoot, 20);
        }
    }
}
