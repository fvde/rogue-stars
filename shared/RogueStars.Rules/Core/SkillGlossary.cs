using System;
using System.Collections.Generic;
using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    /// <summary>One mechanic / keyword a skill can touch, explained in player language (numbers from content).</summary>
    public sealed class GlossaryEntry
    {
        public string Id;
        /// <summary>Player-facing name ("Bleed", "Supernova").</summary>
        public string Name;
        /// <summary>One or two short sentences with the real numbers.</summary>
        public string Text;
        /// <summary>Lower-case words that name the mechanic in a skill's own text ("bleed", "bleeding"); tests use them.</summary>
        public string[] Words;

        public GlossaryEntry(string id, string name, string text, params string[] words) { Id = id; Name = name; Text = text; Words = words; }
    }

    /// <summary>
    /// The one glossary of combat and run mechanics (the skill "?" panel; owner playtest: "explain every mechanic the skill
    /// touches"). Definitions are built from content (status caps, damage per stack, durations, rage, crit, awakening thresholds),
    /// so they stay correct when tuning changes; <see cref="Keywords"/> maps a skill's family, stats, triggers, effects, statuses,
    /// gates and rules to the entries it involves — never from hand-written per-skill text. Numbers go through
    /// <see cref="NumFmt"/> (whole; one decimal only below 10).
    /// </summary>
    public static class SkillGlossary
    {
        public const string Heat = "heat", Supernova = "supernova", Radiation = "radiation", Bleed = "bleed", Chill = "chill", Freeze = "freeze",
            Shock = "shock", Arc = "arc", Stun = "stun", Charm = "charm", Exposed = "exposed", Stagger = "stagger", Delay = "delay",
            Statuses = "statuses", Combo = "combo", Counter = "counter", Crit = "crit", Rage = "rage", Shield = "shield", Dodge = "dodge",
            LifeSteal = "life_steal", Regen = "regen", Aliens = "aliens", Echo = "echo", Signature = "signature", BossClock = "boss_clock",
            RunXp = "run_xp", RunCoins = "run_coins";

        /// <summary>Every entry, in the order the panel lists them (pure; built from <paramref name="c"/>).</summary>
        public static List<GlossaryEntry> Entries(ContentDb c)
        {
            c = c ?? ContentDb.Default;
            var b = c.Combat;
            var t = c.RunTuning;
            var list = new List<GlossaryEntry>();
            StatusDef St(StatusId id) => b.Statuses.TryGetValue(id, out var d) ? d : new StatusDef { Id = id };
            string Rounds(int n) => N(n) + (n == 1 ? " round" : " rounds");

            var heat = St(StatusId.Heat);
            var chill = St(StatusId.Chill);
            var freeze = St(StatusId.Freeze);
            list.Add(new GlossaryEntry(Heat, "Heat",
                "Heat stacks up to " + N(heat.MaxStacks) + " on an enemy and never wears off. At " + N(heat.BurstAtStacks) + " stacks the enemy goes Supernova."
                + (chill.Cools == StatusId.Heat ? " Every Chill stack you apply cools away 1 Heat." : ""),
                "heat", "heated", "hotter"));
            list.Add(new GlossaryEntry(Supernova, "Supernova",
                "The heated enemy explodes: it and every other enemy take " + N(heat.BurstAtkPct) + "% of your ATK (fire damage adds to it). "
                + "The blast also hits you for " + N(heat.BurstRecoilPct) + "% of your max HP. The Heat is used up.",
                "supernova", "supernovas", "blast"));
            var rad = St(StatusId.Poison);
            list.Add(new GlossaryEntry(Radiation, "Radiation",
                "Radiation " + (rad.MaxStacks >= 1000 ? "stacks without limit" : "stacks up to " + N(rad.MaxStacks)) + (rad.Permanent ? " and never wears off" : "")
                + ". Each stack deals " + N(rad.DotAtkPct) + "% of your ATK at the start of the enemy's turn"
                + (rad.HealReductionPct > 0 ? ", and irradiated enemies heal " + N(rad.HealReductionPct) + "% less." : "."),
                "radiation", "irradiated", "irradiates"));
            var bleed = St(StatusId.Bleed);
            list.Add(new GlossaryEntry(Bleed, "Bleed",
                "Bleed stacks up to " + N(bleed.MaxStacks) + (bleed.Permanent ? " and never wears off" : "") + ". Each stack deals " + N(bleed.DotAtkPct)
                + "% of your ATK at the start of the enemy's turn" + (bleed.IgnoresShield ? ", straight through shields" : "")
                + ". Some Bleed skills consume the stacks for a burst of damage and healing.",
                "bleed", "bleeding", "wounds"));
            list.Add(new GlossaryEntry(Chill, "Chill",
                "Chill stacks up to " + N(chill.MaxStacks) + " and lasts " + Rounds(chill.DefaultDuration) + ". Each stack makes the enemy deal "
                + N(Math.Abs(chill.DamageDealtPctPerStack)) + "% less damage (ice damage adds to it)." + (freeze.Clears == StatusId.Chill ? " Any Freeze resets Chill to 0." : ""),
                "chill", "chilled", "cold"));
            list.Add(new GlossaryEntry(Freeze, "Freeze",
                "A frozen enemy loses its next turn and takes +" + N(freeze.DamageTakenPctPerStack) + "% damage while frozen. It can't be frozen again for "
                + Rounds(freeze.ApplyCooldownRounds) + "." + (freeze.Clears == StatusId.Chill ? " Freezing resets its Chill to 0." : ""),
                "freeze", "frozen", "freezes"));
            var shock = St(StatusId.Shock);
            list.Add(new GlossaryEntry(Shock, "Shock",
                "Shock stacks up to " + N(shock.MaxStacks) + " and lasts " + Rounds(shock.DefaultDuration) + ". Each stack makes the enemy take +"
                + N(shock.DamageTakenPctPerStack) + "% damage (lightning damage adds to it).",
                "shock", "shocked", "shocks"));
            list.Add(new GlossaryEntry(Arc, "Arc",
                "An Arc jump carries your hit on to another enemy for part of its damage. Arc damage makes every jump stronger.",
                "arc", "arcs"));
            var stun = St(StatusId.Stun);
            list.Add(new GlossaryEntry(Stun, "Stun",
                "A stunned enemy loses its next turn. It can't be stunned again for " + Rounds(stun.ApplyCooldownRounds) + ".",
                "stun", "stuns", "stunned"));
            var charm = St(StatusId.Charm);
            list.Add(new GlossaryEntry(Charm, "Charm",
                "A charmed enemy attacks its own side on its next turn. It can't be charmed again for " + Rounds(charm.ApplyCooldownRounds) + ".",
                "charm", "charmed"));
            var exposed = St(StatusId.Exposed);
            list.Add(new GlossaryEntry(Exposed, "Exposed",
                "Each Exposed stack makes the enemy take +" + N(exposed.DamageTakenPctPerStack) + "% damage from everything, up to "
                + N(exposed.MaxStacks) + " stacks. It never wears off.",
                "exposed", "expose"));
            list.Add(new GlossaryEntry(Stagger, "Stagger",
                "A staggered boss loses its next rage attack.",
                "stagger", "staggered"));
            list.Add(new GlossaryEntry(Delay, "Delay",
                "A delayed fighter loses its next turn.",
                "delay", "delayed"));
            list.Add(new GlossaryEntry(Statuses, "Statuses", StatusList(c) + " each count as a different status.",
                "statuses"));
            list.Add(new GlossaryEntry(Combo, "Combo",
                "After your attack, your combo chance rolls for an extra strike. Each further strike in the same turn is "
                + N((1 - b.ChainDecay) * 100) + "% less likely, at most " + N(b.MaxChain) + " in a row. Every strike can trigger your on-hit skills again.",
                "combo", "combos", "strike again"));
            list.Add(new GlossaryEntry(Counter, "Counter",
                "When an enemy hits you, your counter chance rolls to strike back at once with a counterattack.",
                "counter", "counters", "counterattack", "counterattacks"));
            list.Add(new GlossaryEntry(Crit, "Crit",
                "A critical hit deals +" + N(b.BaseCritDamage) + "% damage. Crit chance makes them more likely; crit damage adds to the bonus.",
                "crit", "crits", "critical"));
            list.Add(new GlossaryEntry(Rage, "Rage",
                "You gain " + N(b.RagePerAction) + " rage each turn and " + N(b.RagePerHitTaken) + " each time you are hit. At " + N(b.RageMax)
                + " rage you unleash a rage attack, a much stronger hit.",
                "rage"));
            list.Add(new GlossaryEntry(Shield, "Shield",
                "A shield absorbs damage before your HP does. Shield power makes your shields bigger.",
                "shield", "shields", "shielded"));
            list.Add(new GlossaryEntry(Dodge, "Dodge",
                "Dodge is the chance to avoid a hit completely, at most " + N(b.MaxDodge) + "%.",
                "dodge", "dodging"));
            list.Add(new GlossaryEntry(LifeSteal, "Life steal",
                "Life steal heals you for a share of the damage you deal.",
                "life steal", "lifesteal"));
            list.Add(new GlossaryEntry(Regen, "Regeneration",
                "Regeneration heals a share of your max HP at the start of each of your turns.",
                "regeneration", "regenerate"));
            double startShare = b.SidekickActAt > 0 ? b.SidekickStartCharge / b.SidekickActAt * 100 : 0;
            list.Add(new GlossaryEntry(Aliens, "Aliens",
                "Your aliens fight beside the worm as sidekicks. Each one charges with its speed and uses its one ability when full; they start "
                + N(startShare) + "% charged. Alien power makes their abilities stronger.",
                "alien", "aliens", "sidekick", "sidekicks", "drone", "drones", "squad"));
            list.Add(new GlossaryEntry(Echo, "Echo",
                "An Echo repeats one of your attacks. Echo damage makes the repeats stronger.",
                "echo", "echoes"));
            list.Add(new GlossaryEntry(Signature, "Signature",
                "The skill your run is built around. It awakens at " + JoinAnd(t.SignatureAwakenAt) + " owned skills of its synergy, and its effect grows each time.",
                "signature", "signatures"));
            list.Add(new GlossaryEntry(BossClock, "Boss awakening",
                "Every roll fills the boss's awakening meter. Minibosses attack at " + JoinAnd(t.MinibossAtMeter, "%")
                + ", and when the meter is full the boss fight begins. A slower meter means a longer run: more drafts, coins and fights.",
                "boss awakens", "awakening"));
            list.Add(new GlossaryEntry(RunXp, "Run XP",
                "Run XP fills your run level. Every run level up offers you a new skill.",
                "run xp", "xp", "level up"));
            list.Add(new GlossaryEntry(RunCoins, "Coins",
                "Coins are earned during the run and spent in its shops and at events.",
                "coins"));
            return list;
        }

        /// <summary>The entry by id (null when unknown).</summary>
        public static GlossaryEntry Entry(ContentDb c, string id)
        {
            foreach (var e in Entries(c)) if (e.Id == id) return e;
            return null;
        }

        /// <summary>The entries <paramref name="s"/> touches, in glossary order (pure).</summary>
        public static List<GlossaryEntry> Explain(ContentDb c, SkillDef s)
        {
            var ids = Keywords(c, s);
            var res = new List<GlossaryEntry>();
            foreach (var e in Entries(c)) if (ids.Contains(e.Id)) res.Add(e);
            return res;
        }

        /// <summary>
        /// The glossary ids a skill involves, from its data: family, passive stats, triggers (when / effect / status / target),
        /// gate conditions, parameters and rule changes, and its offer gate. Order = glossary order.
        /// </summary>
        public static List<string> Keywords(ContentDb c, SkillDef s)
        {
            c = c ?? ContentDb.Default;
            var set = new HashSet<string>();
            if (s == null) return new List<string>();
            if (s.Signature) set.Add(Signature);
            foreach (var m in s.Stats) if (m != null) AddStat(set, m.Stat);
            foreach (var t in s.Triggers)
            {
                if (t == null) continue;
                AddWhen(set, t.When);
                AddEffect(c, set, t);
                if (t.Status != StatusId.None && t.Effect != EffectKind.Rule) AddStatus(set, t.Status);
                if (t.Stat != null) AddStat(set, t.Stat.Stat);
                AddCondition(set, t.If);
                AddParam(set, t);
            }
            AddNeeds(set, s.Needs);
            // a status that makes its own mechanic: Heat goes Supernova, Freeze resets Chill
            if (set.Contains(Heat)) set.Add(Supernova);
            if (set.Contains(Freeze)) set.Add(Chill);
            var res = new List<string>();
            foreach (var e in Entries(c)) if (set.Contains(e.Id)) res.Add(e.Id);
            return res;
        }

        /// <summary>
        /// The synergy block of the "?" panel: what owning skills of its synergy does (more offers, awakening its signature),
        /// its offer gates and, for a signature, its awakenings; Mythics say where they come from. Empty lines are left out.
        /// </summary>
        public static List<string> SynergyLines(ContentDb c, SkillDef s)
        {
            c = c ?? ContentDb.Default;
            var t = c.RunTuning;
            var lines = new List<string>();
            if (s == null) return lines;
            string fam = SkillText.FamilyName(s.Family);
            if (s.Family != SkillFamily.None)
            {
                if (s.Signature)
                    lines.Add("Your " + fam + " skills awaken it: at " + JoinAnd(t.SignatureAwakenAt) + " " + fam + " skills its effect grows.");
                else
                    lines.Add(fam + " synergy: each " + fam + " skill you own makes more " + fam + " skills show up in your offers and counts toward awakening " + ("AEIOU".IndexOf(fam[0]) >= 0 ? "an " : "a ")
                        + fam + " signature (at " + JoinAnd(t.SignatureAwakenAt) + " " + fam + " skills).");
            }
            if (s.Signature) lines.AddRange(SkillText.Awakenings(c, s));
            if (s.RequiresFamilyCount > 0 && s.Family != SkillFamily.None)
                lines.Add("Offered once you own " + N(s.RequiresFamilyCount) + " " + fam + " skills.");
            string needs = NeedsText(s.Needs);
            if (needs != null) lines.Add("Offered once your run has " + needs + ".");
            if (RunTuning.RunTier(s.Rarity) == Rarity.Mythic)
                lines.Add("Mythic: a reward for beating a miniboss" + (s.Source == SkillSource.Laboratory ? ", once researched in the Laboratory." : ".")
                    + (t.MaxSkillLevelOf(Rarity.Mythic) <= 1 ? " It never levels up." : ""));
            return lines;
        }

        // ---- mapping ------------------------------------------------------------------------------------------

        private static void AddStatus(HashSet<string> set, StatusId st)
        {
            switch (st)
            {
                case StatusId.Heat: case StatusId.Burn: set.Add(Heat); break;
                case StatusId.Poison: set.Add(Radiation); break;
                case StatusId.Bleed: set.Add(Bleed); break;
                case StatusId.Chill: set.Add(Chill); break;
                case StatusId.Freeze: set.Add(Freeze); break;
                case StatusId.Shock: set.Add(Shock); break;
                case StatusId.Stun: set.Add(Stun); break;
                case StatusId.Charm: set.Add(Charm); break;
                case StatusId.Exposed: set.Add(Exposed); break;
                case StatusId.Stagger: set.Add(Stagger); break;
                case StatusId.Delay: set.Add(Delay); break;
            }
        }

        private static void AddStatusName(HashSet<string> set, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (Enum.TryParse(name, out StatusId st)) AddStatus(set, st);
        }

        private static void AddStat(HashSet<string> set, StatId stat)
        {
            switch (stat)
            {
                case StatId.CritChance: case StatId.CritDamage: set.Add(Crit); break;
                case StatId.ComboChance: set.Add(Combo); break;
                case StatId.CounterChance: case StatId.ParryChance: set.Add(Counter); break;
                case StatId.Dodge: set.Add(Dodge); break;
                case StatId.LifeSteal: set.Add(LifeSteal); break;
                case StatId.Regen: set.Add(Regen); break;
                case StatId.RageGain: set.Add(Rage); break;
                case StatId.ShieldPower: set.Add(Shield); break;
                case StatId.FireDamage: set.Add(Heat); break;
                case StatId.IceDamage: set.Add(Chill); set.Add(Freeze); break;
                case StatId.LightningDamage: set.Add(Shock); break;
                case StatId.PoisonDamage: set.Add(Radiation); break;
                case StatId.BleedDamage: set.Add(Bleed); break;
                case StatId.SupernovaDamage: case StatId.SupernovaGuard: set.Add(Supernova); break;
                case StatId.AlienPower: case StatId.SidekickHaste: set.Add(Aliens); break;
                case StatId.ArcDamage: set.Add(Arc); break;
                case StatId.EchoDamage: set.Add(Echo); break;
                case StatId.XpGain: set.Add(RunXp); break;
            }
        }

        private static void AddWhen(HashSet<string> set, TriggerKind w)
        {
            switch (w)
            {
                case TriggerKind.OnCrit: set.Add(Crit); break;
                case TriggerKind.OnCombo: set.Add(Combo); break;
                case TriggerKind.OnCounter: set.Add(Counter); break;
                case TriggerKind.OnDodge: set.Add(Dodge); break;
                case TriggerKind.OnRageAttack: set.Add(Rage); break;
                case TriggerKind.OnSupernova: set.Add(Supernova); break;
                case TriggerKind.OnArcJump: set.Add(Arc); break;
                case TriggerKind.OnShieldHit: set.Add(Shield); break;
                case TriggerKind.OnSidekickAct: case TriggerKind.OnBlock: case TriggerKind.OnSidekickHit: set.Add(Aliens); break;
                case TriggerKind.OnLevelUp: set.Add(RunXp); break;
                case TriggerKind.OnBossAwaken: set.Add(BossClock); break;
            }
        }

        private static void AddEffect(ContentDb c, HashSet<string> set, SkillTrigger t)
        {
            switch (t.Effect)
            {
                case EffectKind.Damage:
                    if (t.Target == TargetKind.OtherEnemies) set.Add(Arc);
                    break;
                case EffectKind.Shield: set.Add(Shield); break;
                case EffectKind.GainRage: case EffectKind.FreeRageAttack: case EffectKind.StartRage: set.Add(Rage); break;
                case EffectKind.ForceSupernova: set.Add(Supernova); set.Add(Heat); break;
                case EffectKind.Strike: set.Add(t.Param == "counter" ? Counter : Combo); break;
                case EffectKind.Charge: set.Add(Counter); break;
                case EffectKind.Summon: case EffectKind.AllyAction: case EffectKind.Block: case EffectKind.Sacrifice: case EffectKind.Guard:
                case EffectKind.ChargeAllies: case EffectKind.Whistle:
                    set.Add(Aliens); break;
                case EffectKind.Echo: set.Add(Echo); break;
                case EffectKind.StackAll: set.Add(Statuses); break;
                case EffectKind.SlowAwakening: set.Add(BossClock); break;
                case EffectKind.GainRunCoins: set.Add(RunCoins); break;
                case EffectKind.CarrySignatures: set.Add(Signature); break;
                case EffectKind.SpreadStatus: set.Add(Statuses); break;
                case EffectKind.CheatDeath: if (BattleSim.HasToken(t.Param, "counter_window")) set.Add(Counter); break;
                case EffectKind.Heal: if (t.Param == "overheal_shield") set.Add(Shield); break;
            }
        }

        /// <summary>Gate conditions (BattleConditions): "has:Heat:6", "max:Bleed &amp; hp_below:25", "rage_crit", "statuses:3" …</summary>
        private static void AddCondition(HashSet<string> set, string cond)
        {
            if (string.IsNullOrWhiteSpace(cond)) return;
            foreach (var raw in cond.Split('&', '|'))
            {
                var tok = raw.Trim().TrimStart('!');
                if (tok.StartsWith("not_", StringComparison.Ordinal)) tok = tok.Substring(4);
                var parts = tok.Split(':');
                switch (parts[0])
                {
                    case "has": case "max": case "unblocked": case "stacks":
                        if (parts.Length > 1) AddStatusName(set, parts[1]);
                        break;
                    case "statuses": set.Add(Statuses); break;
                    case "rage_crit": set.Add(Rage); set.Add(Crit); break;
                    case "combo_strikes": set.Add(Combo); break;
                    case "sidekicks": set.Add(Aliens); break;
                }
            }
        }

        /// <summary>Trigger parameters: status filters, conditional bonuses ("cond:per_stack:Heat") and rule changes.</summary>
        private static void AddParam(HashSet<string> set, SkillTrigger t)
        {
            string p = t.Param;
            if (string.IsNullOrEmpty(p)) return;
            if (t.When == TriggerKind.Passive && p.StartsWith("cond:", StringComparison.Ordinal))
            {
                string cnd = p.Substring(5);
                if (cnd.StartsWith("per_stack:", StringComparison.Ordinal)) AddStatusName(set, cnd.Substring(10));
                else if (cnd.StartsWith("vs_status:", StringComparison.Ordinal)) AddStatusName(set, cnd.Substring(10));
                else if (cnd == "status_count") set.Add(Statuses);
                else if (cnd == "vs_shielded") set.Add(Shield);
                else if (cnd == "combo") set.Add(Combo);
                else if (cnd == "counter") set.Add(Counter);
                else if (cnd == "rage") set.Add(Rage);
                else if (cnd == "arc") set.Add(Arc);
                return;
            }
            if (t.Effect == EffectKind.Rule)
            {
                if (t.Status != StatusId.None) AddStatus(set, t.Status);
                int i = p.IndexOf(':');
                switch (i < 0 ? p : p.Substring(0, i))
                {
                    case "burst_at": case "no_recoil": set.Add(Supernova); break;
                    case "keeps_shields": set.Add(Shield); break;
                    case "dot_crit": set.Add(Crit); break;
                    case "no_cool": set.Add(Chill); set.Add(Heat); break;
                    case "shutdown": set.Add(Chill); set.Add(Crit); set.Add(Combo); set.Add(Counter); break;
                    case "double_vs_chill": set.Add(Shock); set.Add(Chill); set.Add(Freeze); break;
                    case "arc_seek_chill": set.Add(Arc); set.Add(Chill); set.Add(Freeze); break;
                    case "arc_loop": set.Add(Arc); break;
                    case "combo_no_decay": case "combo_falloff": case "combo_cap": case "proc_cap": set.Add(Combo); break;
                    case "combo_as_counter": case "counter_combo": set.Add(Combo); set.Add(Counter); break;
                    case "no_turn_attack": set.Add(Combo); set.Add(Counter); break;
                    case "counter_all": set.Add(Counter); break;
                    case "hivemind": set.Add(Aliens); set.Add(Signature); break;
                    case "symbiote": set.Add(Aliens); set.Add(Rage); break;
                    case "last_light": set.Add(Crit); break;
                    case "echo_strongest": set.Add(Echo); break;
                    case "dot_ticks": set.Add(Radiation); set.Add(Bleed); break;
                    case "decay": set.Add(Heat); set.Add(Radiation); set.Add(Bleed); set.Add(Exposed); break;
                    case "plague": set.Add(Statuses); break;
                }
                return;
            }
            // status filters of status triggers ("Poison", "Bleed;per_unit", "Poison:20") and per-status bonuses
            if (t.When == TriggerKind.OnStatusApplied || t.When == TriggerKind.OnStackThreshold || t.When == TriggerKind.OnStatusConsumed || t.When == TriggerKind.OnStatusLost)
                AddStatusName(set, p.Split(';', ':')[0].Trim());
            if (BattleSim.HasToken(p, "per_status")) set.Add(Statuses);
            if (t.Effect == EffectKind.HealOnTile || p.StartsWith("tile:", StringComparison.Ordinal)) { /* board tiles explain themselves */ }
        }

        private static void AddNeeds(HashSet<string> set, string needs)
        {
            if (string.IsNullOrEmpty(needs)) return;
            int i = needs.IndexOf(':');
            string head = i < 0 ? needs : needs.Substring(0, i), arg = i < 0 ? null : needs.Substring(i + 1);
            switch (head)
            {
                case "arc": set.Add(Arc); break;
                case "freeze": set.Add(Freeze); break;
                case "aliens": case "blocks": set.Add(Aliens); break;
                case "echo": set.Add(Echo); break;
                case "shield": set.Add(Shield); break;
                case "applies": case "consume": AddStatusName(set, arg); break;
            }
        }

        /// <summary>An offer gate (SkillDef.Needs) in words ("an Arc skill"); null for none / unknown.</summary>
        public static string NeedsText(string needs)
        {
            if (string.IsNullOrEmpty(needs)) return null;
            int i = needs.IndexOf(':');
            string head = i < 0 ? needs : needs.Substring(0, i), arg = i < 0 ? null : needs.Substring(i + 1);
            string st = arg != null && Enum.TryParse(arg, out StatusId sid) ? SkillText.StatusName(sid) : null;
            switch (head)
            {
                case "arc": return "a skill that Arcs";
                case "freeze": return "a skill that freezes";
                case "aliens": return "aliens in battle";
                case "blocks": return "an alien that blocks hits";
                case "echo": return "a skill that echoes";
                case "shield": return "a skill that gives a shield";
                case "applies": return st != null ? "a skill that applies " + st : null;
                case "consume": return st != null ? "a skill that consumes " + st : null;
                default: return null;
            }
        }

        private static string StatusList(ContentDb c)
        {
            var names = new List<string>();
            foreach (StatusId id in Enum.GetValues(typeof(StatusId)))
            {
                if (id == StatusId.None || id == StatusId.Burn || !c.Combat.Statuses.ContainsKey(id)) continue;
                names.Add(SkillText.StatusName(id));
            }
            // a fixed, readable order: the synergy statuses first
            names.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            return JoinAnd(names);
        }

        private static int Rank(string name)
        {
            string[] order = { "Heat", "Radiation", "Bleed", "Chill", "Freeze", "Shock", "Stun", "Charm", "Exposed", "Stagger", "Delay" };
            int i = Array.IndexOf(order, name);
            return i < 0 ? order.Length : i;
        }

        private static string N(double v) => NumFmt.OneDecimal(v);

        private static string JoinAnd(int[] xs)
        {
            var l = new List<string>();
            foreach (var x in xs) l.Add(N(x));
            return JoinAnd(l);
        }

        private static string JoinAnd(List<double> xs, string suffix)
        {
            var l = new List<string>();
            foreach (var x in xs) l.Add(N(x) + suffix);
            return JoinAnd(l);
        }

        private static string JoinAnd(List<string> l)
        {
            if (l.Count == 0) return "";
            if (l.Count == 1) return l[0];
            var sb = new StringBuilder();
            for (int i = 0; i < l.Count; i++)
            {
                if (i > 0) sb.Append(i == l.Count - 1 ? " and " : ", ");
                sb.Append(l[i]);
            }
            return sb.ToString();
        }
    }
}
