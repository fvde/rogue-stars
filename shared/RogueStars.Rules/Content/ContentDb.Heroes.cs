using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    [Serializable]
    public sealed class HeroDef
    {
        public string Id;
        public string Name;
        /// <summary>One line: what the hero is about ("Guardian: a shield from the first second …").</summary>
        public string Tagline;
        /// <summary>What drives its Ability ("Counter: hits on your shields", "Timer: every 4th round").</summary>
        public string Meter;
        /// <summary>Heroes are not tied to a synergy (hero &amp; mount kits): None. Kept for saves and old screens.</summary>
        public SkillFamily Family;
        public SkillFamily SecondaryFamily;
        /// <summary>Worm stats at 1★ level 1 (scaled by HeroConfig.StarStatPercent); one shared budget for every hero.</summary>
        public List<StatMod> BaseStats = new List<StatMod>();
        public List<StatMod> StatsPerLevel = new List<StatMod>();
        /// <summary>Kit skills by star (Star field); a step can replace an earlier skill (the 3★ number, an ability clause).</summary>
        public List<SkillStep> Traits = new List<SkillStep>();
        /// <summary>The kit as the screens show it: 1★ Trait + Ability, 3★ Number, 5★ / 7★ Clause, 10★ Capstone.</summary>
        public List<KitText> Kit = new List<KitText>();
    }

    [Serializable]
    public sealed class HeroConfig
    {
        public Dictionary<string, HeroDef> Defs = new Dictionary<string, HeroDef>();
        public int MaxStars = 10;
        /// <summary>StarShardCosts[s] = shards to reach star s (index 1 = unlock; index 0 unused).</summary>
        public List<long> StarShardCosts = new List<long>();
        /// <summary>StarStatPercent[s] = % bonus to the hero's base stats at star s.</summary>
        public List<double> StarStatPercent = new List<double>();
        public int MaxLevelBase = 10;
        public int MaxLevelPerStar = 10;
        /// <summary>LevelXpCosts[L] = Hero XP from level L to L+1 (index 0 unused).</summary>
        public List<long> LevelXpCosts = new List<long>();
        /// <summary>Permanent worm stats per star of every unlocked hero (collection bonus).</summary>
        public List<StatMod> CollectionPerStar = new List<StatMod>();
        /// <summary>The kit steps (stars where a hero's kit changes).</summary>
        public int[] KitStars = { 1, 3, 5, 7, 10 };

        public int MaxLevel(int stars) => Math.Min(LevelXpCosts.Count, MaxLevelBase + MaxLevelPerStar * stars);
    }

    public sealed partial class ContentDb
    {
        /// <summary>Hero personas, star costs, hand-written kits (Progression; hero &amp; mount kits).</summary>
        public readonly HeroConfig HeroConfig = new HeroConfig();

        partial void BuildHeroes()
        {
            var h = HeroConfig;
            h.MaxStars = 10;
            h.StarShardCosts.AddRange(new long[] { 0, 40, 30, 60, 100, 160, 250, 380, 550, 800, 1100 });
            h.StarStatPercent.AddRange(new double[] { 0, 0, 10, 22, 36, 52, 70, 92, 118, 148, 185 });
            h.MaxLevelBase = 10; h.MaxLevelPerStar = 10;
            h.LevelXpCosts.Add(0);
            for (int l = 1; l < h.MaxLevelBase + h.MaxLevelPerStar * h.MaxStars; l++) h.LevelXpCosts.Add((long)Math.Round(40 * Math.Pow(l, 1.6)));
            h.CollectionPerStar.Add(new StatMod(StatId.Hp, 0, 0.3));
            h.CollectionPerStar.Add(new StatMod(StatId.Atk, 0, 0.3));

            const TriggerKind Ab = TriggerKind.OnAbility;
            const TargetKind Self = TargetKind.Self, All = TargetKind.AllEnemies, Front = TargetKind.Enemy;

            // ---- Star Knight (starter; counter: hits on your shields) -------------------------------------------------------
            var d = NewHero("hero_star_knight", "Star Knight Worm", "Guardian: a shield from the first second; every 4th hit it absorbs calls down Starfall.",
                "Counter: hits on your shields");
            HeroSkill(d, 1, "Trait", "Starguard", "trait", "Battle start: shield of 12% max HP. Each hit your shield absorbs gives 5 rage.",
                K(TriggerKind.OnBattleStart, EffectKind.Shield, 12, Self), K(TriggerKind.OnShieldHit, EffectKind.GainRage, 5, Self));
            HeroSkill(d, 1, "Ability", "Starfall", "ability", "Every 4th hit your shields absorb: Starfall hits every enemy for 60% ATK and shields you 6% max HP.",
                KRule("ability_shield_hits:4"), K(Ab, EffectKind.Damage, 60, All), K(Ab, EffectKind.Shield, 6, Self));
            HeroUpgrade(d, 3, "Number", "Starfall", "Starfall: 60% → 90% ATK.", "ability_3", "ability",
                "Every 4th hit your shields absorb: Starfall hits every enemy for 90% ATK and shields you 6% max HP.",
                KRule("ability_shield_hits:4"), K(Ab, EffectKind.Damage, 90, All), K(Ab, EffectKind.Shield, 6, Self));
            HeroSkill(d, 5, "Clause", "Starlight", "c5", "Starfall carries each of your signatures' on-hit to every enemy it hits.",
                K(Ab, EffectKind.CarrySignatures, 0, All));
            HeroSkill(d, 7, "Clause", "Guiding Star", "c7", "Healing counts too: every 10% max HP you heal counts as one absorbed hit.",
                KRule("heals_count:10"));
            HeroSkill(d, 10, "Capstone", "Last Constellation", "c10", "Once per battle, a fatal hit leaves you at 1 HP and fires Starfall at double damage.",
                K(TriggerKind.OnFatal, EffectKind.CheatDeath, 0, Self, "ability:200", maxPerBattle: 1));

            // ---- Chef (timer: every 4th round) --------------------------------------------------------------------------------
            d = NewHero("hero_chef", "Chef Worm", "Status gourmet: heals whenever enemies lose stacks, and eats the biggest status on a timer.",
                "Timer: every 4th round");
            HeroSkill(d, 1, "Trait", "Taste Test", "trait", "When an enemy loses status stacks, heal 1% max HP per stack (max 8% per round).",
                K(TriggerKind.OnStatusLost, EffectKind.Heal, 1, Self, "per_unit;round_cap:8"));
            HeroSkill(d, 1, "Ability", "Feast", "ability", "Every 4th round: eat up to 6 stacks of the front enemy's largest status, 25% ATK per stack.",
                KRule("ability_every:4"), K(Ab, EffectKind.ConsumeStatus, 25, Front, "largest;take:6;dmg:25"));
            HeroUpgrade(d, 3, "Number", "Feast", "Feast: every 4th → 3rd round.", "ability_3", "ability",
                "Every 3rd round: eat up to 6 stacks of the front enemy's largest status, 25% ATK per stack.",
                KRule("ability_every:3"), K(Ab, EffectKind.ConsumeStatus, 25, Front, "largest;take:6;dmg:25"));
            HeroSkill(d, 5, "Clause", "Seasoning", "c5", "After a Feast, your next hit applies your signatures' on-hit three times.",
                K(Ab, EffectKind.CarrySignatures, 0, Self, "next_hit", stacks: 3));
            HeroSkill(d, 7, "Clause", "Second Course", "c7", "Feast also eats up to 3 stacks from the enemy behind.",
                K(Ab, EffectKind.ConsumeStatus, 25, TargetKind.SecondEnemy, "largest;take:3;dmg:25"));
            HeroUpgrade(d, 10, "Capstone", "Banquet", "A full Feast (6 stacks) becomes a Banquet: every enemy takes half its damage.", "ability_10", "ability_3",
                "Every 3rd round: eat up to 6 of the largest status, 25% ATK each. Six eaten: all enemies take half.",
                KRule("ability_every:3"), K(Ab, EffectKind.ConsumeStatus, 25, Front, "largest;take:6;dmg:25;splash:50"));

            // ---- Void Pirate (timer: every 4th round) ------------------------------------------------------------------------
            d = NewHero("hero_void_pirate", "Void Pirate Worm", "Crit raider: status damage crits, an always-crit broadside, crits carry the signature, crit kills pay coins.",
                "Timer: every 4th round");
            HeroSkill(d, 1, "Trait", "Dead Reckoning", "trait", "Your status damage can crit: ticks, Supernovas, consumes and discharges.",
                KRule("status_crit"));
            HeroSkill(d, 1, "Ability", "Broadside", "ability", "Every 4th round: Broadside hits every enemy for 50% ATK. These hits always crit.",
                KRule("ability_every:4"), K(Ab, EffectKind.Damage, 50, All, "crit;oncrit"));
            HeroUpgrade(d, 3, "Number", "Broadside", "Broadside: 50% → 75% ATK.", "ability_3", "ability",
                "Every 4th round: Broadside hits every enemy for 75% ATK. These hits always crit.",
                KRule("ability_every:4"), K(Ab, EffectKind.Damage, 75, All, "crit;oncrit"));
            HeroSkill(d, 5, "Clause", "Black Flag", "c5", "Your crits apply your signatures' on-hit once more.",
                K(TriggerKind.OnCrit, EffectKind.CarrySignatures, 0, Front));
            HeroSkill(d, 7, "Clause", "Plunder", "c7", "Crit kills pay 2 run coins (max 20 per battle).",
                K(TriggerKind.OnKill, EffectKind.GainRunCoins, 2, Self, @if: "crit_kill", maxPerBattle: 10));
            HeroSkill(d, 10, "Capstone", "Void Galleon", "c10", "Broadside also fires at the start of every battle.",
                K(TriggerKind.OnBattleStart, EffectKind.FireAbility, 0, Self));

            // ---- Shadow Noodle (timer: every 3rd turn) -------------------------------------------------------------------------
            d = NewHero("hero_noodle_ninja", "Shadow Noodle Worm", "Extra-hit engine: every extra hit doubles its statuses, and a knot turn makes extra hits from nothing.",
                "Timer: every 3rd turn");
            HeroSkill(d, 1, "Trait", "Double Tap", "trait", "Your extra hits (combo strikes, counters, Echoes) apply their statuses twice.",
                KRule("double_tap"));
            HeroSkill(d, 1, "Ability", "Noodle Knot", "ability", "Every 3rd turn: +30% combo chance and +30% counter chance until your next turn.",
                KRule("ability_turns:3"),
                K(Ab, EffectKind.BuffStat, 0, Self, "until_turn", stat: new StatMod(StatId.ComboChance, 30)),
                K(Ab, EffectKind.BuffStat, 0, Self, "until_turn", stat: new StatMod(StatId.CounterChance, 30)));
            HeroUpgrade(d, 3, "Number", "Noodle Knot", "Noodle Knot: +30% → +45% combo and counter chance.", "ability_3", "ability",
                "Every 3rd turn: +45% combo chance and +45% counter chance until your next turn.",
                KRule("ability_turns:3"),
                K(Ab, EffectKind.BuffStat, 0, Self, "until_turn", stat: new StatMod(StatId.ComboChance, 45)),
                K(Ab, EffectKind.BuffStat, 0, Self, "until_turn", stat: new StatMod(StatId.CounterChance, 45)));
            HeroSkill(d, 5, "Clause", "Sharp Noodle", "c5", "Your extra hits get +20% crit chance.",
                KRule("extra_crit", 20));
            HeroSkill(d, 7, "Clause", "Pile-On", "c7", "Each hit on the same enemy in one turn deals +6% more than the last (max +48%).",
                KRule("pile_on:48", 6));
            HeroSkill(d, 10, "Capstone", "Thousand Noodles", "c10", "Once per battle, a turn with 5+ extra hits ends with a strike on every enemy for 150% ATK.",
                K(TriggerKind.OnTurnEnd, EffectKind.Damage, 150, All, @if: "extra_hits:5", maxPerBattle: 1));

            // ---- Prism Alchemist (was Atomic / Toxic Alchemist; timer: every 3rd round) ------------------------------------------
            d = NewHero("hero_alchemist", "Prism Alchemist Worm", "Status mixer: rewards every different colour on an enemy, throws flasks of what you own, deepens the wells.",
                "Timer: every 3rd round");
            HeroSkill(d, 1, "Trait", "Prism", "trait", "Your hits deal +5% damage per different status on the target.",
                K(TriggerKind.Passive, EffectKind.Damage, 5, Front, "cond:status_count"));
            HeroSkill(d, 1, "Ability", "Flask", "ability", "Every 3rd round: a flask hits the front enemy for 80% ATK and 2 stacks of a random owned status.",
                KRule("ability_every:3"), K(Ab, EffectKind.Damage, 80, Front), K(Ab, EffectKind.ApplyStatus, 0, Front, "owned", stacks: 2));
            HeroUpgrade(d, 3, "Number", "Flask", "Flask: 2 → 3 stacks.", "ability_3", "ability",
                "Every 3rd round: a flask hits the front enemy for 80% ATK and 3 stacks of a random owned status.",
                KRule("ability_every:3"), K(Ab, EffectKind.Damage, 80, Front), K(Ab, EffectKind.ApplyStatus, 0, Front, "owned", stacks: 3));
            HeroSkill(d, 5, "Clause", "Volatile Flask", "c5", "Flasks also trigger your signatures' on-hit.",
                K(Ab, EffectKind.CarrySignatures, 0, Front));
            HeroSkill(d, 7, "Clause", "Conduit", "c7", "Your highest element damage bonus also counts for your other elements, at half.",
                KRule("element_pool", 50));
            HeroSkill(d, 10, "Capstone", "Deep Wells", "c10", "Bleed, Chill and Shock can stack twice as high on enemies (20, 20, 10).",
                KRule("deep_wells:2", 0, StatusId.Bleed), KRule("deep_wells:2", 0, StatusId.Chill), KRule("deep_wells:2", 0, StatusId.Shock));

            // ---- Sparkplug (bank: rage above 100) ------------------------------------------------------------------------------
            d = NewHero("hero_sparkplug", "Sparkplug Worm", "Living ignition: hits and extra hits make rage; overflow rage doubles the next rage attack.",
                "Bank: rage above 100");
            HeroSkill(d, 1, "Trait", "Ignition", "trait", "Gain 5 rage when hit and for each extra hit you land (max 20 per round).",
                K(TriggerKind.OnDamaged, EffectKind.GainRage, 5, Self, "round_cap:20"), K(TriggerKind.OnExtraHit, EffectKind.GainRage, 5, Self, "round_cap:20"));
            HeroSkill(d, 1, "Ability", "Overcharge", "ability", "Rage past 100 is banked (max 100). A rage attack with a full bank fires twice.",
                KRule("rage_bank:100"));
            HeroUpgrade(d, 3, "Number", "Ignition", "Ignition: 5 → 8 rage.", "trait_3", "trait",
                "Gain 8 rage when hit and for each extra hit you land (max 20 per round).",
                K(TriggerKind.OnDamaged, EffectKind.GainRage, 8, Self, "round_cap:20"), K(TriggerKind.OnExtraHit, EffectKind.GainRage, 8, Self, "round_cap:20"));
            HeroSkill(d, 5, "Clause", "Spark Plug", "c5", "Your rage attacks apply your signatures' on-hit to every enemy.",
                K(TriggerKind.OnRageAttack, EffectKind.CarrySignatures, 0, All));
            HeroSkill(d, 7, "Clause", "Short Circuit", "c7", "Each rage attack makes your mount fire now (once per round).",
                K(TriggerKind.OnRageAttack, EffectKind.MountFire, 0, Self));
            HeroSkill(d, 10, "Capstone", "Live Wire", "c10", "Below 30% HP: hits on you give 20 rage, and rage attacks heal 10% max HP.",
                K(TriggerKind.OnDamaged, EffectKind.GainRage, 20, Self, @if: "self_hp_below:30"),
                K(TriggerKind.OnRageAttack, EffectKind.Heal, 10, Self, @if: "self_hp_below:30"));

            // ---- Beast Tamer (timer: every 4th round) ---------------------------------------------------------------------------
            d = NewHero("hero_beast_tamer", "Beast Tamer Worm", "Pack leader: sidekicks and the mount buff the worm, and a whistle sends everyone at once.",
                "Timer: every 4th round");
            HeroSkill(d, 1, "Trait", "Pack Spirit", "trait", "Each time a sidekick acts or your mount fires: +2% ATK this battle (max +30%).",
                K(TriggerKind.OnSidekickAct, EffectKind.BuffStat, 0, Self, "stack:15", stat: new StatMod(StatId.Atk, 0, 2)),
                K(TriggerKind.OnMountFire, EffectKind.BuffStat, 0, Self, "stack:15", stat: new StatMod(StatId.Atk, 0, 2)));
            HeroSkill(d, 1, "Ability", "Whistle", "ability", "Every 4th round: every sidekick acts, fastest first, then your mount fires.",
                KRule("ability_every:4"), K(Ab, EffectKind.Whistle, 0, Self));
            HeroUpgrade(d, 3, "Number", "Whistle", "Whistle: every 4th → 3rd round.", "ability_3", "ability",
                "Every 3rd round: every sidekick acts, fastest first, then your mount fires.",
                KRule("ability_every:3"), K(Ab, EffectKind.Whistle, 0, Self));
            HeroSkill(d, 5, "Clause", "Tamed", "c5", "Sidekick hits apply your signatures' on-hit (once per action).",
                K(TriggerKind.OnSidekickHit, EffectKind.CarrySignatures, 0, Front));
            HeroUpgrade(d, 7, "Clause", "Pack Guard", "Whistle shields the worm for 3% max HP per ally that acted.", "ability_7", "ability_3",
                "Every 3rd round: every sidekick acts, fastest first, then your mount fires. Shield 3% max HP per ally.",
                KRule("ability_every:3"), K(Ab, EffectKind.Whistle, 3, Self));
            HeroUpgrade(d, 10, "Capstone", "Alpha Howl", "During Whistle, the worm follows each ally with a 30% ATK extra hit.", "ability_10", "ability_7",
                "Every 3rd round: all sidekicks act, then your mount fires; shield 3% per ally; you follow each with 30% ATK.",
                KRule("ability_every:3"), K(Ab, EffectKind.Whistle, 3, Self, "follow:30"));

            // ---- Nebula Monk (was Glacier Monk; counter: rounds without HP loss) -----------------------------------------------------
            d = NewHero("hero_glacier_monk", "Nebula Monk Worm", "Calm centre: rounds without HP loss build Stillness; Mirror Palm spreads the biggest status down the line.",
                "Counter: rounds without HP loss");
            HeroSkill(d, 1, "Trait", "Stillness", "trait", "End of a round without HP loss: +1 Stillness (max 3). Shields, dodges and guards count.",
                KRule("ability_stillness:3"));
            HeroSkill(d, 1, "Ability", "Mirror Palm", "ability", "At 3 Stillness: strike the front enemy; every other enemy gets half its largest status (max 20 stacks).",
                K(Ab, EffectKind.Damage, 100, Front), K(Ab, EffectKind.SpreadStatus, 50, Front, "cap:20"));
            HeroUpgrade(d, 3, "Number", "Mirror Palm", "Mirror Palm: max 20 → 30 stacks.", "ability_3", "ability",
                "At 3 Stillness: strike the front enemy; every other enemy gets half its largest status (max 30 stacks).",
                K(Ab, EffectKind.Damage, 100, Front), K(Ab, EffectKind.SpreadStatus, 50, Front, "cap:30"));
            HeroSkill(d, 5, "Clause", "Open Palm", "c5", "Mirror Palm also applies your signatures' on-hit to every enemy.",
                K(Ab, EffectKind.CarrySignatures, 0, All));
            HeroSkill(d, 7, "Clause", "Inner Calm", "c7", "Mirror Palm heals 10% max HP.",
                K(Ab, EffectKind.Heal, 10, Self));
            HeroUpgrade(d, 10, "Capstone", "Nebula Mind", "Mirror Palm also hits every enemy for 8% ATK per stack it copied (max 200%).", "ability_10", "ability_3",
                "At 3 Stillness: strike; others get half the largest status (max 30). Then 8% ATK per stack copied hits all.",
                K(Ab, EffectKind.Damage, 100, Front), K(Ab, EffectKind.SpreadStatus, 50, Front, "cap:30;dmg_per:8;dmg_cap:200"));

            // ---- Hourglass Hermit (new; timer: every 4th round) -----------------------------------------------------------------
            d = NewHero("hero_hourglass_hermit", "Hourglass Hermit Worm", "Time keeper: kills wind the clock forward, and the hourglass rewinds the last hits taken.",
                "Timer: every 4th round");
            HeroSkill(d, 1, "Trait", "Patience", "trait", "Each kill brings your Ability and your mount's next shot 1 round sooner (once per round).",
                K(TriggerKind.OnEnemyDeath, EffectKind.AdvanceTimer, 0, Self, "ability;mount", cooldown: 1));
            HeroSkill(d, 1, "Ability", "Rewind", "ability", "Every 4th round: undo the damage of the last 2 hits you took.",
                KRule("ability_every:4"), K(Ab, EffectKind.Rewind, 0, Self, stacks: 2));
            HeroUpgrade(d, 3, "Number", "Rewind", "Rewind: 2 → 3 hits.", "ability_3", "ability",
                "Every 4th round: undo the damage of the last 3 hits you took.",
                KRule("ability_every:4"), K(Ab, EffectKind.Rewind, 0, Self, stacks: 3));
            HeroSkill(d, 5, "Clause", "Turning Sand", "c5", "After a Rewind, your next hit applies your signatures' on-hit twice.",
                K(Ab, EffectKind.CarrySignatures, 0, Self, "next_hit", stacks: 2));
            HeroSkill(d, 7, "Clause", "Borrowed Sand", "c7", "Once per battle, a fatal hit is undone.",
                K(TriggerKind.OnFatal, EffectKind.CheatDeath, 0, Self, "undo", maxPerBattle: 1));
            HeroUpgrade(d, 10, "Capstone", "Hour of Need", "Each hit Rewind undoes strikes its attacker back for 50% ATK.", "ability_10", "ability_3",
                "Every 4th round: undo the damage of the last 3 hits you took; each strikes its attacker for 50% ATK.",
                KRule("ability_every:4"), K(Ab, EffectKind.Rewind, 0, Self, "reflect:50", stacks: 3));
        }

        private HeroDef NewHero(string id, string name, string tagline, string meter)
        {
            var d = new HeroDef { Id = id, Name = name, Tagline = tagline, Meter = meter, Family = SkillFamily.None, SecondaryFamily = SkillFamily.None };
            // one shared stat budget: hero level only raises worm stats
            d.BaseStats.Add(new StatMod(StatId.Hp, 100, 5));
            d.BaseStats.Add(new StatMod(StatId.Atk, 10, 5));
            d.StatsPerLevel.Add(new StatMod(StatId.Hp, 10));
            d.StatsPerLevel.Add(new StatMod(StatId.Atk, 2));
            HeroConfig.Defs[id] = d;
            return d;
        }

        /// <summary>A new kit element (its own skill <c>heroId_suffix</c>) at a star, with its kit line.</summary>
        private void HeroSkill(HeroDef d, int star, string slot, string name, string suffix, string text, params SkillTrigger[] triggers)
        {
            var s = KitSkill(d.Id + "_" + suffix, name, SkillSource.Hero, text, triggers);
            d.Traits.Add(new SkillStep(star, 0, s.Id));
            d.Kit.Add(new KitText(star, 0, slot, name, text));
        }

        /// <summary>A step that changes an earlier element: its own skill replaces that element's skill; the kit line says what changed.</summary>
        private void HeroUpgrade(HeroDef d, int star, string slot, string name, string kitText, string suffix, string replacesSuffix, string skillText,
            params SkillTrigger[] triggers)
        {
            var s = KitSkill(d.Id + "_" + suffix, name, SkillSource.Hero, skillText, triggers);
            d.Traits.Add(new SkillStep(star, 0, s.Id, d.Id + "_" + replacesSuffix));
            d.Kit.Add(new KitText(star, 0, slot, name, kitText));
        }
    }
}
