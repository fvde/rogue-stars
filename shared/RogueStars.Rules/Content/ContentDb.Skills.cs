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
        /// <summary>Adds a trigger as built (skill redesign: gates, counters, rules).</summary>
        public RunSkillBuilder Add(SkillTrigger t) { Def.Triggers.Add(t); return this; }
        /// <summary>Card text template (SkillDef.Text): '|' between lines, '!' marks a cost, {v0} {c0} {s0} {b0} {p0} the leveled numbers.</summary>
        public RunSkillBuilder Text(string template) { Def.Text = template; return this; }
        /// <summary>Offer gate (SkillDef.Needs): offered only once the run owns that source (RunDraft.HasSource).</summary>
        public RunSkillBuilder Needs(string source) { Def.Needs = source; return this; }
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

        /// <summary>A trigger for content (named arguments keep the redesign's skills readable).</summary>
        private static SkillTrigger Tg(TriggerKind when, EffectKind effect, double value = 0, TargetKind target = TargetKind.Enemy, string @if = null,
            StatusId status = StatusId.None, int stacks = 1, int duration = 0, string param = null, double chance = 100, int everyN = 0,
            int maxPerBattle = 0, int cooldown = 0, GrowKind grow = GrowKind.Auto, StatMod stat = null, double step = 0) =>
            new SkillTrigger
            {
                When = when, Effect = effect, Value = value, Target = target, If = @if, Status = status, Stacks = stacks, Duration = duration,
                Param = param, Chance = chance, EveryN = everyN, MaxPerBattle = maxPerBattle, CooldownRounds = cooldown, Grow = grow, Stat = stat,
                Step = step,
            };

        // Numbers (owner tuning pass): a typical Common skill gives +15% at Lv1 and climbs the level ladder (SkillLeveling:
        // ×1, ×5/3, ×8/3 → +15% → +25% → +40%); Lv1 numbers are multiples of 3 so every level reads as whole numbers. A
        // Legendary gives ~+25% per pick (×1, ×8/5 → +25% → +40%), a Mythic one strong standalone package (it never levels).
        // Proc chances sit on the doubled ladder (30% → 50% → 80%); element damage stats are points (flat), like crit chance.
        //
        // Skill redesign (owner-approved proposal, rev 2): per synergy 3 Common + 2 Legendary here and 3 Mythics in the shared pool.
        // Commons add procs, hooks, openers or a stat; no Legendary or Mythic is a plain "+X% stat": each carries a condition,
        // threshold, trade-off, conversion, consume, timing window or rule change. Reworks keep their ids (saves keep their skills),
        // replaced skills got new ids (removed ids load as skipped). Card texts are templates over the leveled numbers (SkillDef.Text).
        private void RunBuildFamilySkills()
        {
            const TriggerKind Hit = TriggerKind.OnHit, Rage = TriggerKind.OnRageAttack, Passive = TriggerKind.Passive;
            const EffectKind Rule = EffectKind.Rule;

            // ---- Fire: Heat stacks; at 10 the enemy goes Supernova (all enemies hit, the worm takes recoil) ----
            RunSkill("fire_ember", "Ember Strikes", SkillFamily.Fire, Rarity.Common).Desc("Crits may stoke Heat.")
                .Add(Tg(TriggerKind.OnCrit, EffectKind.ApplyStatus, status: StatusId.Heat, stacks: 3, chance: 30))
                .Text("On crit: {c0}% chance to apply Heat ×{s0}.");
            RunSkill("fire_kindle", "Kindling Heart", SkillFamily.Fire, Rarity.Common).Desc("More damage the hotter the enemy.")
                .Cond("per_stack:Heat", 1.5).Text("+{v0}% damage per Heat stack on the target.");
            RunSkill("fire_plating", "Thermal Plating", SkillFamily.Fire, Rarity.Common).Desc("Heated attackers build your shield.")
                .Add(Tg(TriggerKind.OnDamaged, EffectKind.Shield, 3, TargetKind.Self, "has:Heat"))
                .Text("When a heated enemy hits you: gain a shield of {v0}% max HP.");
            RunSkill("fire_flashpoint", "Flashpoint", SkillFamily.Fire, Rarity.Legendary).FamilyGate(2).Desc("Detonate a hot enemy at will.")
                .Add(Tg(Rage, EffectKind.ForceSupernova, 25, @if: "has:Heat:6"))
                .Text("Rage attack on an enemy with 6+ Heat: it goes Supernova now, +{v0}% damage.");
            RunSkill("fire_controlled", "Controlled Burn", SkillFamily.Fire, Rarity.Legendary).FamilyGate(2).Desc("Safer, bigger, later Supernovas.")
                .Add(Tg(Passive, Rule, status: StatusId.Heat, param: "burst_at:12"))
                .Add(Tg(Passive, Rule, status: StatusId.Heat, param: "no_recoil"))
                .Stat(StatId.SupernovaDamage, 25)
                .Text("Supernova needs 12 Heat, but never hurts you and deals +{p0}% damage.");

            // ---- Radiation (internal id Poison): unlimited stacks that never fade, each deals damage every round ----
            RunSkill("poison_spit", "Hot Spit", SkillFamily.Poison, Rarity.Common).Desc("Attackers get irradiated.")
                .Add(Tg(TriggerKind.OnDamaged, EffectKind.ApplyStatus, status: StatusId.Poison, stacks: 2))
                .Text("When hit: apply Radiation ×{s0} to the attacker.");
            RunSkill("poison_glands", "Reactor Glands", SkillFamily.Poison, Rarity.Common).Desc("Hotter radiation that can crit.")
                .Stat(StatId.PoisonDamage, 15).Add(Tg(Passive, Rule, status: StatusId.Poison, param: "dot_crit"))
                .Text("+{p0}% radiation damage. Radiation ticks can crit.");
            RunSkill("poison_geiger", "Geiger Counter", SkillFamily.Poison, Rarity.Common).Desc("Radiation adds up to a burst.")
                .Add(Tg(TriggerKind.OnStatusApplied, EffectKind.Damage, 60, param: "Poison", everyN: 10))
                .Text("Every 10th Radiation stack you apply: deal {v0}% ATK to that enemy.");
            RunSkill("poison_fission", "Fission", SkillFamily.Poison, Rarity.Legendary).FamilyGate(2).Desc("Irradiated deaths spread.")
                .Add(Tg(TriggerKind.OnEnemyDeath, EffectKind.TransferStatus, 50, TargetKind.AllOtherEnemies, "has:Poison", StatusId.Poison))
                .Text("When an irradiated enemy dies, {v0}% of its Radiation spreads to each other enemy.");
            RunSkill("poison_critical", "Critical Mass", SkillFamily.Poison, Rarity.Legendary).FamilyGate(2).Desc("Heavy radiation exposes.")
                .Add(Tg(TriggerKind.OnStackThreshold, EffectKind.ApplyStatus, status: StatusId.Exposed, stacks: 25, param: "Poison:20"))
                .Text("Enemies with 20+ Radiation are Exposed: they take +{s0}% damage from everything.");

            // ---- Bleed: up to 10 wounds that tick through shields; all Bleed healing comes from consuming stacks ----
            RunSkill("bleed_serrated", "Serrated Edge", SkillFamily.Bleed, Rarity.Common).Desc("Open every fight with a deep cut.")
                .Add(Tg(Hit, EffectKind.ApplyStatus, @if: "first_hit", status: StatusId.Bleed, stacks: 3))
                .Text("Your first hit each battle applies Bleed ×{s0}.");
            RunSkill("bleed_tearing", "Tearing Wounds", SkillFamily.Bleed, Rarity.Common).Desc("Bleeding attackers tear their wounds.")
                .Add(Tg(TriggerKind.OnDamaged, EffectKind.ApplyStatus, @if: "has:Bleed", status: StatusId.Bleed, chance: 30))
                .Text("When a bleeding enemy attacks: {c0}% chance its wounds tear, +1 Bleed.");
            RunSkill("bleed_scent", "Blood Scent", SkillFamily.Bleed, Rarity.Common).Desc("Crits cash in wounds.")
                .Add(Tg(TriggerKind.OnCrit, EffectKind.ConsumeStatus, 30, @if: "has:Bleed", status: StatusId.Bleed, param: "take:2;dmg:20;heal:$"))
                .Text("On crit: consume 2 Bleed for 20% ATK per stack, heal {v0}% of it.");
            RunSkill("bleed_frenzy", "Blood Frenzy", SkillFamily.Bleed, Rarity.Legendary).FamilyGate(2).Desc("Rage attacks drink the enemy's Bleed.")
                .Add(Tg(Rage, EffectKind.ConsumeStatus, 25, status: StatusId.Bleed, param: "dmg:$;heal:50;refund_max:50"))
                .Text("Rage attack consumes the target's Bleed: {v0}% ATK per stack, heal 50% of it. At max stacks, refund 50 rage.");
            RunSkill("bleed_hemorrhage", "Hemorrhage", SkillFamily.Bleed, Rarity.Legendary).FamilyGate(2).Desc("Deeper wounds, no longer through shields.")
                .Add(Tg(Passive, Rule, 5, status: StatusId.Bleed, param: "max"))
                .Add(Tg(Passive, Rule, status: StatusId.Bleed, param: "keeps_shields"))
                .Text("Bleed can stack {v0} higher.|!Bleed no longer ignores shields");

            // ---- Ice (package): Chill weakens and cools Heat; Freeze skips turns ----
            RunSkill("ice_armor", "Frost Armor", SkillFamily.Ice, Rarity.Common).Desc("A shield that chills whoever hits it.")
                .Add(Tg(TriggerKind.OnBattleStart, EffectKind.Shield, 6, TargetKind.Self))
                .Add(Tg(TriggerKind.OnShieldHit, EffectKind.ApplyStatus, status: StatusId.Chill, grow: GrowKind.Fixed))
                .Text("Battle start: gain a shield of {v0}% max HP. Hits on your shield chill the attacker.");
            RunSkill("ice_shard", "Ice Shard", SkillFamily.Ice, Rarity.Common).Desc("Chilled enemies draw ice shards.")
                .Add(Tg(Hit, EffectKind.Damage, 50, @if: "has:Chill", chance: 30, grow: GrowKind.Chance))
                .Text("Hitting a chilled enemy: {c0}% chance to fire a shard for {v0}% ATK.");
            RunSkill("ice_brittle", "Brittle", SkillFamily.Ice, Rarity.Common).Desc("Cold makes enemies fragile.")
                .Cond("per_stack:Chill", 1.5).Text("Enemies take +{v0}% damage per Chill stack.");
            RunSkill("ice_permafrost", "Permafrost", SkillFamily.Ice, Rarity.Legendary).FamilyGate(2).Desc("Deep cold stays.")
                .Add(Tg(Passive, Rule, status: StatusId.Chill, param: "no_expire:5")).Stat(StatId.IceDamage, 25)
                .Text("Chill no longer wears off on enemies with 5+ stacks. +{p0}% ice damage.");
            RunSkill("ice_cold_snap", "Cold Snap", SkillFamily.Ice, Rarity.Legendary).FamilyGate(2).Needs("freeze").Desc("A freeze chills the pack.")
                .Add(Tg(TriggerKind.OnStatusApplied, EffectKind.ApplyStatus, target: TargetKind.AllOtherEnemies, status: StatusId.Chill, stacks: 3, param: "Freeze"))
                .Text("When you Freeze an enemy, every other enemy gains Chill ×{s0}.");
            // CC pass (owner): Ice's own Freeze source. Any Freeze resets the target's Chill to 0 (StatusDef.Clears), so the enemy
            // has to be chilled up again; the levels raise the damage against frozen enemies, never the freeze.
            RunSkill("ice_deep_freeze", "Deep Freeze", SkillFamily.Ice, Rarity.Legendary).FamilyGate(2).Needs("applies:Chill").Desc("Deep cold locks an enemy in ice.")
                .Add(Tg(TriggerKind.OnStatusApplied, EffectKind.ApplyStatus, @if: "has:Chill:5 & unblocked:Freeze", status: StatusId.Freeze, param: "Chill", grow: GrowKind.Fixed))
                .Cond("vs_status:Freeze", 25)
                .Text("When an enemy reaches 5 Chill, freeze it; its Chill resets.|+{v1}% damage to frozen enemies.");

            // ---- Lightning (package): Arc spreads hits; Shock (max 5, +6% damage taken per stack, 2 turns) ----
            RunSkill("light_conductor", "Conductor Coil", SkillFamily.Lightning, Rarity.Common).Needs("arc").Desc("Stronger Arcs.")
                .Stat(StatId.ArcDamage, 15).Text("+{p0}% Arc damage.");
            RunSkill("light_spark", "Spark", SkillFamily.Lightning, Rarity.Common).Needs("arc").Desc("Arcs may shock.")
                .Add(Tg(TriggerKind.OnArcJump, EffectKind.ApplyStatus, status: StatusId.Shock, chance: 30))
                .Text("Arc jumps: {c0}% chance to apply Shock.");
            RunSkill("light_overclock", "Overclock", SkillFamily.Lightning, Rarity.Common).Needs("arc").Desc("Fully shocked enemies arc again.")
                .Add(Tg(Hit, EffectKind.Damage, 50, TargetKind.OtherEnemies, "max:Shock", chance: 30, grow: GrowKind.Chance))
                .Text("Hitting an enemy at max Shock: {c0}% chance to Arc one extra time.");
            RunSkill("light_ground_loop", "Ground Loop", SkillFamily.Lightning, Rarity.Legendary).Needs("arc").Desc("Arcs find the last enemy.")
                .Add(Tg(Passive, Rule, 50, TargetKind.Self, param: "arc_loop"))
                .Text("Last enemy standing: your Arc jumps strike it instead, with +{v0}% damage.");
            RunSkill("light_thunderclap", "Thunderclap", SkillFamily.Lightning, Rarity.Legendary).FamilyGate(2).Needs("arc").Desc("Arcs build a storm.")
                .Add(Tg(TriggerKind.OnArcJump, EffectKind.ApplyStatus, target: TargetKind.AllEnemies, status: StatusId.Shock, stacks: 2, everyN: 8, grow: GrowKind.Fixed))
                .Add(new SkillTrigger { When = Passive, Effect = EffectKind.BuffStat, Param = "cond:arc", If = "max:Shock", Value = 25 })
                .Text("Every 8th Arc jump: all enemies gain Shock ×2. Your Arcs deal +{v1}% to enemies at max Shock.");

            // ---- Combo (package): extra strikes in one turn, each re-rolls on-hit effects ----
            // was "Flurry" (the Combo signature is Flurry now)
            RunSkill("combo_flurry", "Quick Hands", SkillFamily.Combo, Rarity.Common).Desc("Chance to strike again.").Stat(StatId.ComboChance, 15);
            RunSkill("combo_momentum", "Momentum", SkillFamily.Combo, Rarity.Common).Desc("Combos build rage.")
                .On(TriggerKind.OnCombo, EffectKind.GainRage, 12, TargetKind.Self);
            RunSkill("combo_opening", "Opening Flurry", SkillFamily.Combo, Rarity.Common).Desc("Fast out of the gate.")
                .Add(Tg(TriggerKind.OnTurnStart, EffectKind.BuffStat, target: TargetKind.Self, @if: "first_turn", duration: 1, stat: new StatMod(StatId.ComboChance, 30)))
                .Text("Your first turn each battle: +{b0}% combo chance.");
            RunSkill("combo_rhythm", "Battle Rhythm", SkillFamily.Combo, Rarity.Legendary).FamilyGate(2).Desc("Keep the chain going.")
                .Add(Tg(TriggerKind.OnCombo, EffectKind.BuffStat, target: TargetKind.Self, param: "reset_no_combo", stat: new StatMod(StatId.Atk, 0, 2.5)))
                .Text("Each combo: +{b0}% ATK this battle. A turn without a combo resets it.");
            RunSkill("combo_whirlwind", "Whirlwind", SkillFamily.Combo, Rarity.Legendary).FamilyGate(2).Desc("Every few strikes sweep the field.")
                .Add(Tg(TriggerKind.OnCombo, EffectKind.Damage, 100, TargetKind.AllEnemies, everyN: 4))
                .Text("Every 4th combo strike sweeps all enemies for {v0}% ATK.");

            // ---- Counter (package): get hit, hit back (parry was removed) ----
            // was "Riposte" (the Counter signature is Riposte now)
            RunSkill("counter_riposte", "Counter Stance", SkillFamily.Counter, Rarity.Common).Desc("Chance to counter-attack.").Stat(StatId.CounterChance, 15);
            RunSkill("counter_spiked", "Spiked Hide", SkillFamily.Counter, Rarity.Common).Needs("shield").Desc("Shields bite back.")
                .Add(Tg(TriggerKind.OnShieldHit, EffectKind.Reflect, 30))
                .Text("When your shield absorbs a hit, reflect {v0}% of the absorbed damage.");
            RunSkill("counter_brace", "Brace", SkillFamily.Counter, Rarity.Common).Desc("Countering steadies you.")
                .Add(Tg(TriggerKind.OnCounter, EffectKind.BuffStat, target: TargetKind.Self, param: "until_turn", stat: new StatMod(StatId.DamageReduction, 12)))
                .Text("After you counter, take {b0}% less damage until your next turn.");
            RunSkill("counter_grudge", "Grudge", SkillFamily.Counter, Rarity.Legendary).FamilyGate(2).Desc("Every blow feeds the answer.")
                .Add(Tg(TriggerKind.OnDamaged, EffectKind.Charge, 40, TargetKind.Self))
                .Text("Each hit you take adds +{v0}% to your next counter. Stacks.");
            // parry removed (skill redesign): Parry Stance → Rebound Plating
            RunSkill("counter_rebound", "Rebound Plating", SkillFamily.Counter, Rarity.Legendary).FamilyGate(2).Desc("Counters build a shield.")
                .Add(Tg(TriggerKind.OnCounter, EffectKind.Shield, 40, TargetKind.Self, param: "of_amount"))
                .Text("Your counterattacks grant a shield of {v0}% of the damage they deal.");

            // ---- Alien Synergy (package): sidekicks act at their own speed with one ability; owner: all four Commons stay ----
            RunSkill("alien_bond", "Squad Bond", SkillFamily.AlienSynergy, Rarity.Common).Desc("Sidekick abilities are stronger.").Stat(StatId.AlienPower, 45);
            RunSkill("alien_rally", "Mothership Link", SkillFamily.AlienSynergy, Rarity.Common).Desc("Battle start: launch a drone sidekick.") // id kept (was Rally Cry)
                .Kick(TriggerKind.OnBattleStart, EffectKind.Summon, 1, 30, "sk_drone");
            RunSkill("alien_rally_cry", "Rally Cry", SkillFamily.AlienSynergy, Rarity.Common).Needs("aliens").Desc("Your aliens fire the worm up.")
                .Add(Tg(TriggerKind.OnSidekickAct, EffectKind.BuffStat, target: TargetKind.Worm, param: "next_turn", stat: new StatMod(StatId.Atk, 0, 6)))
                .Text("When an alien uses its ability: the worm gets +{b0}% ATK for its next turn.");
            RunSkill("alien_bodyguard", "Bodyguard", SkillFamily.AlienSynergy, Rarity.Common).Needs("aliens").Desc("Aliens throw themselves in front.")
                .Add(Tg(TriggerKind.OnBattleStart, EffectKind.Block, target: TargetKind.Self))
                .Text("Your aliens block hits on the worm: the first {s0} each battle.");
            RunSkill("alien_hivemind", "Hivemind", SkillFamily.AlienSynergy, Rarity.Legendary).FamilyGate(2).Needs("aliens").Desc("Aliens channel your signatures.")
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "hivemind")).Stat(StatId.AlienPower, 25)
                .Text("Each alien is tuned to one of your signatures; its ability also triggers it once. +{p0}% alien power.");
            // ---- Time (package, R13): Echo repeats attacks; Time bends turn order ----
            RunSkill("time_head_start", "Head Start", SkillFamily.Time, Rarity.Common).Desc("Strike before they move.")
                .Add(Tg(TriggerKind.OnBattleStart, EffectKind.ApplyStatus, target: TargetKind.AllEnemies, status: StatusId.Delay, chance: 30, step: 10))
                .Text("Battle start: {c0}% chance to delay every enemy's first turn.");
            RunSkill("time_afterimage", "Afterimage", SkillFamily.Time, Rarity.Common).Needs("echo").Desc("Stronger echoes.")
                .Stat(StatId.EchoDamage, 15).Text("Echoes deal +{p0}% damage.");
            RunSkill("time_rewind_shell", "Rewind Shell", SkillFamily.Time, Rarity.Common).Desc("Undo the worst of it.")
                .Add(Tg(TriggerKind.OnDamaged, EffectKind.Rewind, target: TargetKind.Self, @if: "self_hp_below:30", maxPerBattle: 1))
                .Text("Once per battle below 30% HP: undo the damage of your last {s0} hit(s) taken.");
            RunSkill("time_stasis", "Stasis Field", SkillFamily.Time, Rarity.Legendary).FamilyGate(2).Desc("Freeze time for everyone else.")
                .Add(Tg(TriggerKind.OnRoundStart, EffectKind.ApplyStatus, target: TargetKind.AllEnemies, status: StatusId.Delay, everyN: 4, grow: GrowKind.Fixed))
                .Add(Tg(TriggerKind.OnRoundStart, EffectKind.BuffStat, target: TargetKind.Self, everyN: 4, duration: 1, stat: new StatMod(StatId.EchoDamage, 25)))
                .Text("Every 4th round, all enemies lose their turn. Your Echo that round deals +{b1}%.");
            RunSkill("time_paradox", "Paradox", SkillFamily.Time, Rarity.Legendary).FamilyGate(2).Needs("echo").Desc("Echo your best.")
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "echo_strongest")).Stat(StatId.EchoDamage, 25)
                .Text("Echo repeats your strongest attack this battle instead of the last one, at +{p0}% damage.");

            RunSkill("alien_decoy", "Decoy", SkillFamily.AlienSynergy, Rarity.Legendary).FamilyGate(2).Needs("blocks").Desc("Blocked attackers lose their way.")
                .Add(Tg(TriggerKind.OnBlock, EffectKind.ApplyStatus, status: StatusId.Charm, chance: 20, step: 10))
                .Text("When an alien blocks or guards a hit: {c0}% chance to Charm the attacker.");
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
            // skill redesign (R13): the Time package's signature, kept simple: every 3rd turn, repeat your last attack
            Sig("sig_time", "Echo", SkillFamily.Time, "Your attacks echo.")
                .Add(Tg(TriggerKind.OnTurnEnd, EffectKind.Echo, target: TargetKind.Self, everyN: 3));
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
                .Board(TriggerKind.OnLevelUp, EffectKind.RunStatGrowth, 0, null, 100, new StatMod(StatId.Hp, 0, 2))
                .Text("Each run level up: +{b0}% ATK and +{b1}% HP for the rest of the run.");
            // the former Crit synergy's standalone picks (Crit is no synergy any more; Precision was folded away)
            RunChain("crit_keen", SkillFamily.None, new[] { "Keen Eye", "Hawk Eye", "Star Eye" }, RunChainCRE, (b, t) =>
                b.Desc("+crit chance.").Stat(StatId.CritChance, 15));
            RunSkill("crit_brutal", "Brutal Force", SkillFamily.None, Rarity.Rare).Desc("+24% crit damage.").Stat(StatId.CritDamage, 24);
            // OnRunStart effects of run skills apply when the skill is gained (RunSystem.GrantSkill), at any point of the run:
            // the rest of the clock runs slower (owner playtest 0.1.200: no "Run start:" text on a skill bought mid-run)
            RunSkill("gen_calm_mind", "Calm Mind", SkillFamily.None, Rarity.Rare).Desc("The boss awakens slower: a longer run.")
                .Board(TriggerKind.OnRunStart, EffectKind.SlowAwakening, 9)
                .Text("From now on, the boss awakens {v0}% slower.");
            // skill redesign
            RunSkill("gen_ambush", "Ambush Instinct", SkillFamily.None, Rarity.Common).Desc("Pick your fights: arrive angry.")
                .Board(TriggerKind.OnTileLand, EffectKind.StartRage, 15, "tile:Enemy")
                .Text("Landing on an enemy tile: start that battle with {v0} rage.");
            RunSkill("gen_cull", "Cull", SkillFamily.None, Rarity.Legendary).Desc("The weak fall at once.")
                .Add(Tg(TriggerKind.OnHit, EffectKind.Execute, 15))
                .Text("Hitting a non-boss enemy below {v0}% HP kills it outright.");
            RunSkill("gen_overclocked", "Overclocked Core", SkillFamily.None, Rarity.Legendary).Desc("Power at a price.")
                .Stat(StatId.Atk, 0, 25).Stat(StatId.Hp, 0, -15)
                .Text("+{p0}% ATK|!{p1}% max HP");
        }

        /// <summary>
        /// The Mythic pool (owner decision, signatures pass): one pool drawn at every miniboss choice, never gated by family counts
        /// or prerequisites and never in the normal draft or the shop. Every synergy brings 3 themed Mythics (Alien 4: Swarm Beacon
        /// stays, owner), plus generic ones; the choice favours owned synergies. Skill redesign: each rewrites a rule of combat.
        /// Mythics that need a source the run may lack (Arc, Freeze, Shock, aliens, consumed Bleed) are offered only with it (Needs).
        /// </summary>
        private void RunBuildMythicSkills()
        {
            const TriggerKind Hit = TriggerKind.OnHit, Rage = TriggerKind.OnRageAttack, Passive = TriggerKind.Passive;
            const EffectKind Rule = EffectKind.Rule;
            // Fire
            RunSkill("mythic_thermal_shock", "Thermal Shock", SkillFamily.Fire, Rarity.Mythic).Desc("Heat and shock lock an enemy down.")
                .Add(Tg(Hit, EffectKind.ApplyStatus, @if: "has:Heat & unblocked:Heat & not_boss", status: StatusId.Stun))
                .Add(Tg(Hit, EffectKind.ApplyStatus, @if: "has:Heat & unblocked:Heat & boss", status: StatusId.Stagger))
                .Add(Tg(Hit, EffectKind.BlockStatus, @if: "has:Heat & unblocked:Heat", status: StatusId.Heat, duration: 3))
                .Text("Hitting a heated enemy stuns it (bosses: stagger).|!It can't gain Heat for 3 turns");
            RunSkill("mythic_chain_reaction", "Chain Reaction", SkillFamily.Fire, Rarity.Mythic).Desc("Every blast seeds the next.")
                .Add(Tg(TriggerKind.OnSupernova, EffectKind.ApplyStatus, target: TargetKind.AllEnemies, status: StatusId.Heat, stacks: 5, cooldown: 1))
                .Text("Supernova leaves 5 Heat on every enemy it hits (once per round).|!Each blast still singes you");
            RunSkill("fire_heat_siphon", "Heat Siphon", SkillFamily.Fire, Rarity.Mythic).Desc("Drink the fire.")
                .Add(Tg(Rage, EffectKind.ConsumeStatus, 4, @if: "has:Heat", status: StatusId.Heat, param: "buff:Atk:$;cap:60"))
                .Text("Your rage attack drains the target's Heat: +{v0}% ATK per stack for the battle (max +60%).");
            // Radiation
            RunSkill("mythic_chain_fission", "Chain Fission", SkillFamily.Poison, Rarity.Mythic).Desc("Doubling cores.")
                .Add(Tg(Rage, EffectKind.MultiplyStatus, 200, status: StatusId.Poison, param: "cap:50")).Stat(StatId.HealReceived, -50)
                .Text("Rage attacks double the target's Radiation (at most +50).|!Your own healing is halved");
            RunSkill("mythic_gamma_burst", "Gamma Burst", SkillFamily.Poison, Rarity.Mythic).Desc("Crits flash the core.")
                .Add(Tg(TriggerKind.OnCrit, EffectKind.TickStatus, 200, @if: "has:Poison", status: StatusId.Poison))
                .Text("Crits on an irradiated enemy make its Radiation tick instantly, at double damage.");
            RunSkill("mythic_fallout_aura", "Fallout Aura", SkillFamily.Poison, Rarity.Mythic).Desc("Mixed afflictions breed radiation.")
                .Add(Tg(TriggerKind.OnRoundStart, EffectKind.ApplyStatus, target: TargetKind.AllEnemies, status: StatusId.Poison, stacks: 2, param: "per_status"))
                .Text("Each round: every enemy gains 2 Radiation per different status it has.");
            // Bleed
            RunSkill("mythic_exsanguinate", "Exsanguinate", SkillFamily.Bleed, Rarity.Mythic).Desc("Bled dry.")
                .Add(Tg(Hit, EffectKind.Execute, 0, @if: "max:Bleed & hp_below:25"))
                .Add(new SkillTrigger { When = Passive, Effect = EffectKind.BuffStat, Param = "cond:if", If = "max:Bleed & hp_below:25 & boss", Value = 100 })
                .Text("Hitting an enemy at max Bleed and below 25% HP kills it. Bosses take +100% instead.");
            RunSkill("mythic_crimson_pact", "Crimson Pact", SkillFamily.Bleed, Rarity.Mythic).Desc("Blood for blood.")
                .Add(Tg(TriggerKind.OnTurnStart, EffectKind.LoseHp, 3, TargetKind.Self))
                .Add(Tg(Passive, Rule, 100, status: StatusId.Bleed, param: "dot"))
                .Add(Tg(Passive, Rule, 100, status: StatusId.Bleed, param: "consume_heal"))
                .Text("!Lose 3% HP each turn|Bleed ticks deal double, and consuming Bleed heals twice as much");
            RunSkill("mythic_fallout_wounds", "Fallout Wounds", SkillFamily.Bleed, Rarity.Mythic).Needs("consume:Bleed").Desc("Spent blood glows.")
                .Add(Tg(TriggerKind.OnStatusConsumed, EffectKind.ApplyStatus, status: StatusId.Poison, stacks: 3, param: "Bleed;per_unit"))
                .Text("Whenever Bleed is consumed, the enemy gains 3 Radiation per stack.");
            // Ice
            // renamed from "Shatter" (2026-11-26): the name went to the Laboratory Mythic Shatter (lab_shatter); id and art kept
            RunSkill("mythic_shatter", "Thaw Blast", SkillFamily.Ice, Rarity.Mythic).Needs("freeze").Desc("Break the ice.")
                .Add(Tg(Hit, EffectKind.Damage, 150, TargetKind.AllOtherEnemies, "has:Freeze"))
                .Add(Tg(Hit, EffectKind.RemoveStatus, 0, @if: "has:Freeze", status: StatusId.Freeze, stacks: 0))
                .Text("Hitting a frozen enemy shatters the ice: it thaws, and all other enemies take {v0}% ATK.");
            RunSkill("mythic_winters_grip", "Winter's Grip", SkillFamily.Ice, Rarity.Mythic).Desc("Cold steals their tricks.")
                .Add(Tg(Passive, Rule, status: StatusId.Chill, param: "shutdown"))
                .Text("Chilled enemies can't crit, combo or counter.");
            RunSkill("mythic_cold_fusion", "Cold Fusion", SkillFamily.Ice, Rarity.Mythic).Desc("Fire and ice, together.")
                .Add(Tg(Passive, Rule, status: StatusId.Chill, param: "no_cool"))
                .Add(new SkillTrigger { When = Passive, Effect = EffectKind.BuffStat, Param = "cond:if", If = "has:Heat & has:Chill", Value = 40 })
                .Text("Chill stops cooling Heat. Enemies with both Heat and Chill take +{v1}% damage from you.");
            // Lightning
            RunSkill("mythic_thunderhead", "Thunderhead", SkillFamily.Lightning, Rarity.Mythic).Desc("The storm hits everyone.")
                .Add(Tg(Passive, Rule, 60, TargetKind.Self, param: "basic_aoe"))
                .Text("Your basic attack hits every enemy, but at {v0}% damage.");
            RunSkill("mythic_superconductor", "Superconductor", SkillFamily.Lightning, Rarity.Mythic).Needs("arc").Desc("Lightning loves the cold.")
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "arc_seek_chill"))
                .Add(Tg(Passive, Rule, status: StatusId.Shock, param: "double_vs_chill"))
                .Text("Arcs jump to chilled or frozen enemies first. Shock on them counts double.");
            RunSkill("mythic_capacitor", "Capacitor", SkillFamily.Lightning, Rarity.Mythic).Needs("applies:Shock").Desc("Store the charge, then release.")
                .Add(Tg(Passive, Rule, status: StatusId.Shock, param: "no_expire"))
                .Add(Tg(TriggerKind.OnStackThreshold, EffectKind.ConsumeStatus, 30, status: StatusId.Shock, param: "Shock:5"))
                .Text("Shock no longer expires. At 5 stacks it discharges for 150% ATK and resets.");
            // Combo
            RunSkill("mythic_endless_chain", "Endless Chain", SkillFamily.Combo, Rarity.Mythic).Desc("The chain never slows.")
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "combo_no_decay"))
                .Add(Tg(Passive, Rule, 15, TargetKind.Self, param: "combo_falloff"))
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "combo_cap:80"))
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "proc_cap:6"))
                .Text("Combo chance no longer drops within a turn.|!Each strike deals {v1}% less than the last; combo chance at most 80%");
            RunSkill("mythic_fury_chain", "Fury Chain", SkillFamily.Combo, Rarity.Mythic).Desc("Three strikes unleash fury.")
                .Add(Tg(TriggerKind.OnCombo, EffectKind.FreeRageAttack, @if: "combo_strikes:3"))
                .Text("3 combo strikes in one turn: unleash a free rage attack.");
            RunSkill("mythic_perfect_form", "Perfect Form", SkillFamily.Combo, Rarity.Mythic).Desc("Offense is defense.")
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "combo_as_counter"))
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "counter_combo"))
                .Text("Your combo chance also counts as counter chance, and counterattacks can combo.");
            // Counter (parry removed: Mirror Carapace and Bastion reworked, ids kept)
            RunSkill("counter_mirror", "Mirror Carapace", SkillFamily.Counter, Rarity.Mythic).Desc("The first hit each round bounces back to every enemy.")
                .Add(Tg(TriggerKind.OnDamaged, EffectKind.Reflect, 100, TargetKind.AllEnemies, "first_hit_taken")).Stat(StatId.DamageTaken, 20)
                .Text("The first hit you take each round is reflected in full to every enemy.|!You take +{p0}% damage");
            RunSkill("mythic_bastion", "Bastion", SkillFamily.Counter, Rarity.Mythic).Desc("Stand still; answer everything.")
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "no_turn_attack"))
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "counter_all")).Cond("counter", 50)
                .Text("!You no longer make basic or combo attacks on your turn|Instead, you counter every hit at +{v2}% damage");
            RunSkill("mythic_last_word", "Last Word", SkillFamily.Counter, Rarity.Mythic).Desc("Not yet.")
                .Add(Tg(TriggerKind.OnFatal, EffectKind.CheatDeath, target: TargetKind.Self, param: "counter_window", maxPerBattle: 1))
                .Text("Once per battle, a fatal hit leaves you at 1 HP. Counter every hit until your next turn.");
            // Alien Synergy (owner: Swarm Beacon stays as a 4th Alien Mythic)
            RunSkill("mythic_swarm_link", "Swarm Link", SkillFamily.AlienSynergy, Rarity.Mythic).Needs("aliens").Desc("A kill rallies the squad.")
                .Add(Tg(TriggerKind.OnKill, EffectKind.AllyAction, target: TargetKind.Self, param: "all"))
                .Text("When the worm kills an enemy, every alien acts immediately.");
            RunSkill("mythic_symbiote", "Symbiote", SkillFamily.AlienSynergy, Rarity.Mythic).Needs("aliens").Desc("The squad moves with the worm.")
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "symbiote"))
                .Add(Tg(Rage, EffectKind.AllyAction, target: TargetKind.Self, param: "all"))
                .Text("!Your aliens stop acting alone|Your rage attack also fires every alien's ability");
            RunSkill("mythic_sacrifice", "Sacrifice Protocol", SkillFamily.AlienSynergy, Rarity.Mythic).Needs("aliens").Desc("One falls so you stand.")
                .Add(Tg(TriggerKind.OnFatal, EffectKind.Sacrifice, 30, TargetKind.Self, "sidekicks", maxPerBattle: 1))
                .Text("Once per battle, an alien takes a fatal hit for you. It leaves the battle; you heal {v0}%.");
            RunSkill("mythic_swarm_call", "Swarm Beacon", SkillFamily.AlienSynergy, Rarity.Mythic).Desc("Drones answer your call.")
                .Kick(TriggerKind.OnBattleStart, EffectKind.Summon, 3, 40, "sk_drone");
            // generic
            RunSkill("mythic_overkill", "Overkill", SkillFamily.None, Rarity.Mythic).Desc("Nothing goes to waste.")
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "overkill"))
                .Text("Damage beyond an enemy's HP carries over to the next enemy.");
            RunSkill("mythic_last_light", "Last Light", SkillFamily.None, Rarity.Mythic).Desc("Brightest before the end.")
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "last_light:30"))
                .Text("Below 30% HP: every hit crits.|!You can't heal");
            RunSkill("mythic_catalyst", "Catalyst", SkillFamily.None, Rarity.Mythic).Desc("Afflictions feed each other.")
                .Add(Tg(Hit, EffectKind.StackAll, @if: "statuses:3"))
                .Text("Enemies with 3+ different statuses gain 1 stack of each whenever you hit them.");
            // owner (2026-11-26): two generic Mythics, open without any unlock or synergy
            RunSkill("mythic_horrifying_presence", "Horrifying Presence", SkillFamily.None, Rarity.Mythic).Desc("Terror holds them still.")
                .Add(Tg(TriggerKind.OnBattleStart, EffectKind.ApplyStatus, target: TargetKind.AllEnemies, status: StatusId.Stun, grow: GrowKind.Fixed))
                .Text("Every enemy is stunned in round 1.");
            // owner asked for a three-rate package: the one Mythic allowed to be stats only (SkillRedesignContentTests)
            RunSkill("mythic_beast", "Beast", SkillFamily.None, Rarity.Mythic).Desc("Unleash the beast.")
                .Stat(StatId.CounterChance, 30).Stat(StatId.ComboChance, 30).Stat(StatId.CritChance, 30)
                .Text("+{p0}% counter chance|+{p1}% combo chance|+{p2}% crit chance");
            // Time (R13; Loop replaces Chrono Shell)
            RunSkill("mythic_borrowed_time", "Borrowed Time", SkillFamily.Time, Rarity.Mythic).Desc("Live faster, pay later.")
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "extra_turn"))
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "lose_turn_every:4"))
                .Text("Take an extra turn each round.|!Every 4th round, you lose your turn");
            RunSkill("mythic_time_dilation", "Time Dilation", SkillFamily.Time, Rarity.Mythic).Desc("Wounds age twice as fast.")
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "dot_ticks:2"))
                .Add(Tg(Passive, Rule, target: TargetKind.Self, param: "decay:1"))
                .Text("Status damage ticks twice per round.|!Permanent stacks decay by 1 each round");
            RunSkill("mythic_loop", "Loop", SkillFamily.Time, Rarity.Mythic).Desc("Not like this. Again.")
                .Add(Tg(TriggerKind.OnFatal, EffectKind.Loop, target: TargetKind.Self, maxPerBattle: 1))
                .Text("Once per battle, when you'd die, rewind yourself and all enemies 2 rounds.");
        }
    }
}
