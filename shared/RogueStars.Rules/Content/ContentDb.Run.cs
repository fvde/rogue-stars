using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    /// <summary>Run workstream content: portals, levels, board tiles, enemies, bosses, events, shops, loot, battle tuning.</summary>
    public sealed partial class ContentDb
    {
        public readonly BattleTuning Combat = new BattleTuning();
        public readonly RunTuning RunTuning = new RunTuning();
        public readonly ThreatCurve RunThreat = new ThreatCurve();
        public readonly CampaignDef Campaign = new CampaignDef();
        public readonly Dictionary<string, PortalDef> Portals = new Dictionary<string, PortalDef>();
        public readonly Dictionary<string, LevelDef> RunLevels = new Dictionary<string, LevelDef>();
        public readonly Dictionary<string, EnemyDef> Enemies = new Dictionary<string, EnemyDef>();
        public readonly Dictionary<string, BossDef> RunBosses = new Dictionary<string, BossDef>();
        public readonly Dictionary<string, TileDef> BoardTiles = new Dictionary<string, TileDef>();
        public readonly Dictionary<string, List<WeightedId>> TilePools = new Dictionary<string, List<WeightedId>>();
        public readonly Dictionary<string, EventDef> RunEvents = new Dictionary<string, EventDef>();
        /// <summary>RunEvents drawn by generic "event" tiles (ordered list: deterministic).</summary>
        public readonly List<WeightedId> RandomRunEvents = new List<WeightedId>();
        public readonly Dictionary<string, ShopTableDef> RunShopTables = new Dictionary<string, ShopTableDef>();
        public readonly Dictionary<string, LootTableDef> RunLootTables = new Dictionary<string, LootTableDef>();

        /// <summary>
        /// Long threat curve for enemy HP/ATK/DEF: Growth^t · (1 + Linear·t).
        /// Main levels use t = ThreatAt(level number) (level − 1 up to level 30), Overcharged +8, Burrow 4..22, Dark Side 30..48,
        /// leaving room for many more levels (t=60 ≈ 260×, t=100 ≈ 8000×) as worm/parts/buildings scale for years.
        /// </summary>
        public double ThreatMultiplier(int threat)
        {
            if (threat < 0) threat = 0;
            return Systems.CombatMath.PowInt(RunThreat.Growth, threat) * (1 + RunThreat.Linear * threat); // deterministic (local-first battles)
        }

        /// <summary>Multiplier of a fractional threat position relative to its integer part (≥ 1).</summary>
        public double ThreatFractionMultiplier(double threat)
        {
            int whole = (int)Math.Floor(threat + 1e-9);
            double frac = threat - whole;
            if (frac <= 1e-9) return 1;
            return Math.Pow(ThreatMultiplier(whole + 1) / ThreatMultiplier(whole), frac);
        }

        /// <summary>Any run level by id: materialized (RunLevels) or generated on demand (endless main levels, Dark Side).</summary>
        public LevelDef RunLevel(string id)
        {
            if (id == null) return null;
            return RunLevels.TryGetValue(id, out var l) ? l : GeneratedLevel(id);
        }

        /// <summary>Any portal by id: materialized (Portals) or a generated main level's portal.</summary>
        public PortalDef Portal(string id)
        {
            if (id == null) return null;
            return Portals.TryGetValue(id, out var p) ? p : GeneratedPortal(id);
        }

        /// <summary>Run loot table by id (materialized or generated for its chapter).</summary>
        public LootTableDef RunLootTable(string id)
        {
            if (id == null) return null;
            return RunLootTables.TryGetValue(id, out var t) ? t : GeneratedLootTable(id);
        }

        partial void BuildRun()
        {
            RunBuildBattleTuning();
            RunBuildRunTuning();
            RunBuildEnemies();
            CampaignBuildBiomes(); // biome rosters (after the enemy defs: art-pending ones are skipped)
            RunBuildBosses();
            RunBuildTiles();
            RunBuildEvents();
            RunBuildBiomeEvents();
            RunBuildShopsAndLoot();
            RunBuildPortals();
            // Stamina's regen rule (cap 60, 1 per 6 min) is owned by Progression (ContentDb.Economy).
            RegenRules.Add(new RegenRule { Currency = Currencies.BurrowKeys, BaseCap = 2, AmountPerTick = 1, TickMs = 12 * 60 * 60 * 1000L });
        }

        // ---- battle ---------------------------------------------------------------------------

        private void RunBuildBattleTuning()
        {
            var b = Combat;
            b.Styles[AttackStyle.Slam] = new AttackStyleDef(100, 60, 70, 220);
            b.Styles[AttackStyle.Claw] = new AttackStyleDef(95, 70, 60, 200);
            b.Styles[AttackStyle.Blaster] = new AttackStyleDef(100, 55, 55, 230);
            b.Styles[AttackStyle.Drill] = new AttackStyleDef(105, 60, 60, 190);
            b.Styles[AttackStyle.Whip] = new AttackStyleDef(90, 80, 65, 190);
            b.Styles[AttackStyle.Cannon] = new AttackStyleDef(110, 45, 50, 250);

            // Signatures pass: Heat replaced Burn (Fire), Poison is shown as Radiation (unlimited, permanent), Bleed is capped at
            // 10 permanent stacks that Bleed skills consume for burst and lifesteal, Chill (Ice) weakens and cools Heat.
            b.Statuses[StatusId.Heat] = new StatusDef { Id = StatusId.Heat, MaxStacks = 10, Permanent = true, BurstAtStacks = 10, BurstAtkPct = 250,
                BurstRecoilPct = 12, HasElement = true, ElementStat = StatId.FireDamage };
            b.Statuses[StatusId.Poison] = new StatusDef { Id = StatusId.Poison, MaxStacks = 9999, Permanent = true, DotAtkPct = 4, HealReductionPct = 30, HasElement = true, ElementStat = StatId.PoisonDamage };
            b.Statuses[StatusId.Bleed] = new StatusDef { Id = StatusId.Bleed, MaxStacks = 10, Permanent = true, DotAtkPct = 6, IgnoresShield = true, HasElement = true, ElementStat = StatId.BleedDamage };
            b.Statuses[StatusId.Chill] = new StatusDef { Id = StatusId.Chill, MaxStacks = 10, DefaultDuration = 3, DamageDealtPctPerStack = -2, Cools = StatusId.Heat, HasElement = true, ElementStat = StatId.IceDamage };
            b.Statuses[StatusId.Freeze] = new StatusDef { Id = StatusId.Freeze, MaxStacks = 1, DefaultDuration = 1, SkipsTurn = true, DamageTakenPctPerStack = 20, ApplyCooldownRounds = 3, HasElement = true, ElementStat = StatId.IceDamage, Clears = StatusId.Chill };
            b.Statuses[StatusId.Stun] = new StatusDef { Id = StatusId.Stun, MaxStacks = 1, DefaultDuration = 1, SkipsTurn = true, ApplyCooldownRounds = 3 };
            b.Statuses[StatusId.Shock] = new StatusDef { Id = StatusId.Shock, MaxStacks = 5, DefaultDuration = 2, DamageTakenPctPerStack = 6, HasElement = true, ElementStat = StatId.LightningDamage };
            b.Statuses[StatusId.Charm] = new StatusDef { Id = StatusId.Charm, MaxStacks = 1, DefaultDuration = 1, Charms = true, ApplyCooldownRounds = 4 };
            // skill redesign: Exposed (+1% damage taken per stack, permanent; Critical Mass), Stagger (a boss loses its next rage
            // attack; Thermal Shock), Delay (loses its next turn, no re-apply cooldown; Time)
            b.Statuses[StatusId.Exposed] = new StatusDef { Id = StatusId.Exposed, MaxStacks = 100, Permanent = true, DamageTakenPctPerStack = 1 };
            b.Statuses[StatusId.Stagger] = new StatusDef { Id = StatusId.Stagger, MaxStacks = 1, DefaultDuration = 1 };
            b.Statuses[StatusId.Delay] = new StatusDef { Id = StatusId.Delay, MaxStacks = 1, DefaultDuration = 1, SkipsTurn = true };
        }

        private void RunBuildRunTuning()
        {
            var t = RunTuning;
            // three run tiers (the old Common + Rare and Epic + Legendary odds folded together)
            t.DraftRarityWeights[Rarity.Common] = 83;
            t.DraftRarityWeights[Rarity.Legendary] = 16;
            t.DraftRarityWeights[Rarity.Mythic] = 1;
            t.SegmentThemes.Add(new SegmentTheme { Name = "Hunting Grounds", Enemy = 1.45, Reward = 0.9 });
            t.SegmentThemes.Add(new SegmentTheme { Name = "Old Ruins", Event = 1.7, Reward = 1.3, Enemy = 0.85 });
            t.SegmentThemes.Add(new SegmentTheme { Name = "Badlands", Hazard = 1.7, Reward = 1.5, Buff = 0.7 });
            t.SegmentThemes.Add(new SegmentTheme { Name = "Quiet Valley", Buff = 1.8, Enemy = 0.8, Hazard = 0.6 });
            // Segment layouts (GDD §3.1): the path's iso-grid shape varies per segment (run lengths, turns, hairpins); forks are
            // rectangles around a set-piece slot. Amplitude / Period / SideLoops date from the free 2D layout (unused on the grid).
            t.BoardLayouts.Add(new BoardLayoutDef { Name = "Long Straight", Kind = BoardLayoutKind.Straight, BranchMult = 1.3, SideLoops = true, Amplitude = 12, Period = 10 });
            t.BoardLayouts.Add(new BoardLayoutDef { Name = "Zig-Zag", Kind = BoardLayoutKind.ZigZag, Amplitude = 55, Period = 4 });
            t.BoardLayouts.Add(new BoardLayoutDef { Name = "Wide Curve", Kind = BoardLayoutKind.WideCurve, Amplitude = 70, Period = 18 });
            t.BoardLayouts.Add(new BoardLayoutDef { Name = "Hook", Kind = BoardLayoutKind.Hook, SideLoops = true, Amplitude = 80, Period = 10 });
            t.BoardLayouts.Add(new BoardLayoutDef { Name = "Diamond Splits", Kind = BoardLayoutKind.Diamonds, BranchMult = 1.8, Amplitude = 20, Period = 14 });
            t.FallbackWormStats.Add(new StatMod(StatId.Hp, 1000));
            t.FallbackWormStats.Add(new StatMod(StatId.Atk, 100));
            t.FallbackWormStats.Add(new StatMod(StatId.Def, 20));
            t.FallbackWormStats.Add(new StatMod(StatId.Speed, 100));
            t.FallbackWormStats.Add(new StatMod(StatId.CritChance, 5));
        }

        // ---- enemies --------------------------------------------------------------------------

        private EnemyDef RunEnemy(string id, string name, double hp, double atk, double def, double speed, AttackStyle style, params string[] skills)
        {
            var e = new EnemyDef { Id = id, Name = name, Style = style };
            e.Stats.Add(new StatMod(StatId.Hp, hp * RunTuning.EnemyHpScale));
            e.Stats.Add(new StatMod(StatId.Atk, atk * RunTuning.EnemyAtkScale));
            e.Stats.Add(new StatMod(StatId.Def, def));
            e.Stats.Add(new StatMod(StatId.Speed, speed));
            if (skills != null) e.SkillIds.AddRange(skills);
            Enemies[id] = e;
            return e;
        }

        private void RunEnemySkill(string id, string name, params SkillTrigger[] triggers)
        {
            var s = new SkillDef { Id = id, Name = name, Source = SkillSource.Run, Draftable = false, Rarity = Rarity.Common };
            s.Triggers.AddRange(triggers);
            AddSkill(s);
        }

        private void RunBuildEnemies()
        {
            RunEnemySkill("enemy_burning_touch", "Burning Touch", new SkillTrigger { When = TriggerKind.OnHit, Chance = 30, Effect = EffectKind.ApplyStatus, Status = StatusId.Heat });
            RunEnemySkill("enemy_chill", "Chilling Grip", new SkillTrigger { When = TriggerKind.OnHit, Chance = 12, Effect = EffectKind.ApplyStatus, Status = StatusId.Freeze });
            RunEnemySkill("enemy_static", "Static Skin", new SkillTrigger { When = TriggerKind.OnHit, Chance = 35, Effect = EffectKind.ApplyStatus, Status = StatusId.Shock });
            RunEnemySkill("enemy_toxic", "Toxic Bite", new SkillTrigger { When = TriggerKind.OnHit, Chance = 40, Effect = EffectKind.ApplyStatus, Status = StatusId.Poison, Stacks = 2 });
            RunEnemySkill("enemy_rend", "Rend", new SkillTrigger { When = TriggerKind.OnHit, Chance = 25, Effect = EffectKind.ApplyStatus, Status = StatusId.Bleed });
            RunEnemySkill("enemy_harden", "Harden", new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.Shield, Target = TargetKind.Self, Value = 15 });
            RunEnemySkill("enemy_enrage", "Enrage", new SkillTrigger { When = TriggerKind.OnLowHp, Effect = EffectKind.BuffStat, Target = TargetKind.Self, Stat = new StatMod(StatId.Atk, 0, 40) });
            RunEnemySkill("enemy_stun_slam", "Stunning Slam", new SkillTrigger { When = TriggerKind.OnRageAttack, Chance = 50, Effect = EffectKind.ApplyStatus, Status = StatusId.Stun });
            RunEnemySkill("boss_magma_eruption", "Eruption", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 4, Effect = EffectKind.Damage, Value = 80, Target = TargetKind.AllEnemies },
                new SkillTrigger { When = TriggerKind.OnHit, Chance = 40, Effect = EffectKind.ApplyStatus, Status = StatusId.Heat, Stacks = 2 });
            RunEnemySkill("boss_frost_tide", "Frost Tide", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 5, Effect = EffectKind.ApplyStatus, Status = StatusId.Freeze, Target = TargetKind.AllEnemies },
                new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.Shield, Target = TargetKind.Self, Value = 20 });
            RunEnemySkill("boss_hive_swarm", "Swarm Call", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 5, Effect = EffectKind.Summon, Param = "spark_drone" },
                new SkillTrigger { When = TriggerKind.OnHit, Chance = 50, Effect = EffectKind.ApplyStatus, Status = StatusId.Shock, Stacks = 2 });
            RunEnemySkill("boss_void_hunger", "Void Hunger", new SkillTrigger { When = TriggerKind.OnRoundEnd, CooldownRounds = 3, Effect = EffectKind.TrueDamage, Value = 60, Target = TargetKind.Enemy });
            // biome boss and enemy kits (docs/design/biomes): one simple thing each, existing effects only
            RunEnemySkill("enemy_time_snap", "Time Snap", new SkillTrigger { When = TriggerKind.OnHit, Chance = 15, Effect = EffectKind.ApplyStatus, Status = StatusId.Delay });
            RunEnemySkill("boss_molten_core", "Molten Core", new SkillTrigger { When = TriggerKind.OnHit, Chance = 50, Effect = EffectKind.ApplyStatus, Status = StatusId.Heat, Stacks = 2 });
            RunEnemySkill("boss_ignition", "Ignition", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 3, Effect = EffectKind.ApplyStatus, Status = StatusId.Heat, Stacks = 3 });
            RunEnemySkill("boss_sun_glare", "Sun Glare", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 4, Effect = EffectKind.ApplyStatus, Status = StatusId.Stun });
            RunEnemySkill("boss_ice_armor", "Ice Armor", new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.Shield, Target = TargetKind.Self, Value = 30 });
            RunEnemySkill("boss_regrow", "Regrow", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 3, Effect = EffectKind.Heal, Target = TargetKind.Self, Value = 10 });
            RunEnemySkill("boss_lure", "Lure", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 4, Effect = EffectKind.ApplyStatus, Status = StatusId.Charm });
            RunEnemySkill("boss_feedback_song", "Feedback Song", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 3, Effect = EffectKind.ApplyStatus, Status = StatusId.Shock, Stacks = 2 });
            RunEnemySkill("boss_salvage", "Salvage", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 3, Effect = EffectKind.Shield, Target = TargetKind.Self, Value = 15 });
            RunEnemySkill("boss_toxic_flood", "Toxic Flood", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 3, Effect = EffectKind.ApplyStatus, Status = StatusId.Poison, Stacks = 3 });
            RunEnemySkill("boss_refraction", "Refraction", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 3, Effect = EffectKind.ApplyStatus, Status = StatusId.Delay });
            RunEnemySkill("boss_spore_cloud", "Spore Cloud", new SkillTrigger { When = TriggerKind.OnRoundStart, Effect = EffectKind.ApplyStatus, Status = StatusId.Poison, Stacks = 1 });
            RunEnemySkill("boss_sugar_rush", "Sugar Rush", new SkillTrigger { When = TriggerKind.OnRoundStart, Effect = EffectKind.BuffStat, Target = TargetKind.Self, Param = "stack:5", Stat = new StatMod(StatId.Speed, 0, 10) });
            RunEnemySkill("boss_rewind", "Rewind", new SkillTrigger { When = TriggerKind.OnLowHp, Effect = EffectKind.Heal, Target = TargetKind.Self, Value = 20, MaxPerBattle = 1 });
            RunEnemySkill("boss_time_stop", "Time Stop", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 4, Effect = EffectKind.Strike });
            // passive stat packages on enemy skills
            Skills["boss_void_hunger"].Stats.Add(new StatMod(StatId.LifeSteal, 20));

            // Enemies sit on the standard role budget (HP x ATK about 14 000 for a regular, 68 000 for an elite, 133 000 for a boss;
            // biome design 2026-11-28: no family offsets any more, so the old Void roster came down to it). Biome rosters: CampaignBuildBiomes.
            // P(...) = ArtPending: a def without imported art, kept out of every live pool until its sprite lands (EnemyDef.ArtPending).
            EnemyDef P(EnemyDef e) { e.ArtPending = true; return e; }
            EnemyDef St(EnemyDef e, StatId stat, double v) { e.Stats.Add(new StatMod(stat, v)); return e; }
            RunEnemy("ember_imp", "Ember Imp", 300, 45, 8, 95, AttackStyle.Claw, "enemy_burning_touch");
            RunEnemy("magma_slug", "Magma Slug", 420, 38, 16, 70, AttackStyle.Slam, "enemy_harden");
            St(RunEnemy("cinder_hound", "Cinder Hound", 260, 52, 6, 115, AttackStyle.Claw), StatId.ComboChance, 15);
            St(RunEnemy("ash_wraith", "Ash Wraith", 240, 48, 4, 105, AttackStyle.Whip), StatId.Dodge, 15);
            RunEnemy("frost_mite", "Frost Mite", 280, 44, 10, 100, AttackStyle.Claw, "enemy_chill");
            RunEnemy("ice_crawler", "Ice Crawler", 380, 40, 18, 80, AttackStyle.Drill, "enemy_harden");
            St(RunEnemy("glacier_golem", "Glacier Golem", 520, 42, 24, 60, AttackStyle.Slam), StatId.CounterChance, 12);
            P(St(RunEnemy("snow_lurker", "Snow Lurker", 260, 55, 6, 120, AttackStyle.Claw), StatId.CritChance, 15));
            RunEnemy("volt_beetle", "Volt Beetle", 340, 46, 14, 90, AttackStyle.Drill, "enemy_static");
            RunEnemy("static_eel", "Static Eel", 280, 50, 8, 110, AttackStyle.Whip, "enemy_static");
            RunEnemy("spark_drone", "Spark Drone", 180, 40, 4, 125, AttackStyle.Blaster);
            P(St(RunEnemy("thunder_moth", "Thunder Moth", 250, 52, 6, 115, AttackStyle.Whip), StatId.Dodge, 20));
            RunEnemy("burrow_grub", "Burrow Grub", 340, 42, 12, 85, AttackStyle.Drill, "enemy_toxic");
            RunEnemy("tunnel_stalker", "Tunnel Stalker", 290, 50, 8, 110, AttackStyle.Claw, "enemy_rend");
            RunEnemy("void_spawn", "Void Spawn", 300, 48, 10, 100, AttackStyle.Whip, "enemy_toxic");
            St(RunEnemy("null_shade", "Null Shade", 260, 54, 6, 120, AttackStyle.Claw, "enemy_rend"), StatId.Dodge, 18);
            // new biome enemies (art pending)
            P(St(RunEnemy("obsidian_crab", "Obsidian Crab", 400, 38, 18, 75, AttackStyle.Claw, "enemy_harden"), StatId.CounterChance, 10));
            P(RunEnemy("gas_bloater", "Gas Bloater", 460, 32, 8, 70, AttackStyle.Slam, "enemy_burning_touch"));
            P(St(RunEnemy("flare_ray", "Flare Ray", 250, 54, 6, 120, AttackStyle.Whip), StatId.CritChance, 15));
            P(St(RunEnemy("rig_raider", "Rig Raider", 270, 52, 8, 110, AttackStyle.Blaster), StatId.ComboChance, 15));
            P(St(RunEnemy("lantern_angler", "Lantern Angler", 270, 50, 8, 105, AttackStyle.Claw, "enemy_chill"), StatId.CritChance, 15));
            P(St(RunEnemy("kelp_strangler", "Kelp Strangler", 400, 38, 16, 75, AttackStyle.Whip, "enemy_harden"), StatId.CounterChance, 12));
            P(St(RunEnemy("hail_sprite", "Hail Sprite", 230, 50, 4, 115, AttackStyle.Blaster, "enemy_chill"), StatId.Dodge, 20));
            P(St(RunEnemy("sky_ray", "Sky Ray", 240, 54, 4, 130, AttackStyle.Whip), StatId.CritChance, 15));
            P(RunEnemy("security_bot", "Security Bot", 380, 40, 18, 80, AttackStyle.Blaster, "enemy_harden", "enemy_static"));
            P(St(RunEnemy("lab_rat", "Lab Rat", 260, 52, 6, 115, AttackStyle.Claw), StatId.ComboChance, 15));
            P(St(RunEnemy("snap_orchid", "Snap Orchid", 340, 42, 12, 80, AttackStyle.Whip, "enemy_static"), StatId.CounterChance, 15));
            P(St(RunEnemy("spark_monkey", "Spark Monkey", 250, 54, 6, 120, AttackStyle.Claw), StatId.ComboChance, 15));
            P(RunEnemy("rust_leech", "Rust Leech", 300, 48, 8, 100, AttackStyle.Whip, "enemy_toxic"));
            P(St(RunEnemy("scrap_crab", "Scrap Crab", 400, 38, 18, 75, AttackStyle.Claw, "enemy_harden"), StatId.CounterChance, 10));
            P(RunEnemy("bog_toad", "Bog Toad", 320, 44, 10, 90, AttackStyle.Slam, "enemy_toxic"));
            P(St(RunEnemy("mire_gator", "Mire Gator", 340, 42, 14, 85, AttackStyle.Slam), StatId.CritChance, 15));
            P(St(RunEnemy("sand_scarab", "Sand Scarab", 400, 38, 18, 75, AttackStyle.Drill, "enemy_harden"), StatId.CounterChance, 10));
            P(RunEnemy("dune_serpent", "Dune Serpent", 260, 52, 6, 120, AttackStyle.Whip, "enemy_rend"));
            P(RunEnemy("wrapped_husk", "Wrapped Husk", 320, 44, 10, 85, AttackStyle.Claw, "enemy_burning_touch", "enemy_enrage"));
            P(RunEnemy("tusk_seal", "Tusk Seal", 380, 38, 14, 80, AttackStyle.Slam, "enemy_enrage"));
            P(RunEnemy("shard_penguin", "Shard Penguin", 250, 52, 8, 120, AttackStyle.Claw, "enemy_chill"));
            P(St(RunEnemy("puffer_mine", "Puffer Mine", 460, 30, 16, 70, AttackStyle.Slam), StatId.CounterChance, 20));
            P(St(RunEnemy("spine_urchin", "Spine Urchin", 320, 44, 14, 80, AttackStyle.Drill, "enemy_rend"), StatId.CounterChance, 10));
            P(St(RunEnemy("reef_shark", "Reef Shark", 260, 54, 6, 120, AttackStyle.Claw), StatId.CritChance, 15));
            P(St(RunEnemy("neon_ganger", "Neon Ganger", 270, 52, 8, 110, AttackStyle.Claw), StatId.ComboChance, 15));
            P(St(RunEnemy("holo_hound", "Holo Hound", 250, 52, 6, 115, AttackStyle.Claw), StatId.Dodge, 20));
            P(RunEnemy("puffball", "Puffball", 220, 60, 6, 95, AttackStyle.Slam, "enemy_toxic"));
            P(RunEnemy("myco_beetle", "Myco Beetle", 380, 38, 18, 80, AttackStyle.Drill, "enemy_harden"));
            P(RunEnemy("shroom_walker", "Shroom Walker", 380, 38, 14, 80, AttackStyle.Slam, "enemy_enrage"));
            P(St(RunEnemy("gummy_worm", "Gummy Worm", 270, 52, 8, 110, AttackStyle.Whip), StatId.ComboChance, 15));
            P(RunEnemy("jelly_blob", "Jelly Blob", 380, 38, 16, 75, AttackStyle.Slam, "enemy_harden", "enemy_toxic"));
            P(RunEnemy("candy_wasp", "Candy Wasp", 230, 56, 4, 130, AttackStyle.Drill, "enemy_toxic"));
            P(RunEnemy("gingerbread_brute", "Gingerbread Brute", 380, 38, 14, 80, AttackStyle.Slam, "enemy_enrage"));
            P(RunEnemy("tick_mite", "Tick Mite", 280, 46, 10, 100, AttackStyle.Claw, "enemy_time_snap"));
            P(St(RunEnemy("crystal_bat", "Crystal Bat", 240, 54, 4, 120, AttackStyle.Whip), StatId.Dodge, 20));
            P(St(RunEnemy("geode_crawler", "Geode Crawler", 400, 38, 18, 75, AttackStyle.Drill, "enemy_harden"), StatId.CounterChance, 12));
            P(St(RunEnemy("cog_soldier", "Cog Soldier", 400, 38, 18, 75, AttackStyle.Blaster, "enemy_harden"), StatId.CounterChance, 12));
            P(St(RunEnemy("steam_hound", "Steam Hound", 270, 52, 8, 110, AttackStyle.Claw), StatId.ComboChance, 15));
            P(RunEnemy("spring_hopper", "Spring Hopper", 240, 56, 6, 135, AttackStyle.Slam));
            P(RunEnemy("stasis_drone", "Stasis Drone", 260, 50, 8, 105, AttackStyle.Blaster, "enemy_chill"));
            P(St(RunEnemy("chrome_sentry", "Chrome Sentry", 400, 38, 18, 75, AttackStyle.Blaster), StatId.CounterChance, 15));

            // Elites (about the elite budget; minibosses are drawn from a biome's elites, so each biome fields at least two)
            RunMakeElite(RunEnemy("elite_lava_brute", "Lava Brute", 950, 72, 22, 80, AttackStyle.Slam, "enemy_burning_touch", "enemy_enrage"));
            RunMakeElite(RunEnemy("elite_frost_knight", "Frost Knight", 900, 66, 30, 85, AttackStyle.Drill, "enemy_chill", "enemy_harden"));
            RunMakeElite(RunEnemy("elite_storm_caller", "Storm Caller", 820, 74, 18, 105, AttackStyle.Blaster, "enemy_static", "enemy_stun_slam"));
            RunMakeElite(RunEnemy("elite_void_reaver", "Void Reaver", 950, 72, 20, 100, AttackStyle.Claw, "enemy_rend", "enemy_enrage"));
            RunMakeElite(RunEnemy("elite_rift_echo", "Rift Echo", 950, 72, 24, 100, AttackStyle.Cannon, "enemy_stun_slam"));
            RunMakeElite(RunEnemy("elite_burrow_horror", "Burrow Horror", 1000, 68, 26, 75, AttackStyle.Slam, "enemy_toxic", "enemy_enrage"));
            St(RunMakeElite(RunEnemy("elite_cinder_alpha", "Cinder Alpha", 900, 76, 18, 95, AttackStyle.Claw, "enemy_burning_touch", "enemy_rend")), StatId.ComboChance, 15);
            St(RunMakeElite(RunEnemy("elite_ash_matriarch", "Ash Matriarch", 980, 70, 20, 85, AttackStyle.Whip, "enemy_burning_touch", "enemy_harden")), StatId.Dodge, 10);
            St(RunMakeElite(RunEnemy("elite_glacier_warden", "Glacier Warden", 960, 62, 32, 75, AttackStyle.Slam, "enemy_chill", "enemy_stun_slam")), StatId.CounterChance, 12);
            St(RunMakeElite(RunEnemy("elite_lurker_alpha", "Lurker Alpha", 840, 70, 24, 100, AttackStyle.Claw, "enemy_chill", "enemy_enrage")), StatId.CritChance, 10);
            St(RunMakeElite(RunEnemy("elite_volt_monarch", "Volt Monarch", 880, 70, 22, 95, AttackStyle.Drill, "enemy_static", "enemy_harden")), StatId.CounterChance, 10);
            P(St(RunMakeElite(RunEnemy("elite_eel_tyrant", "Eel Tyrant", 780, 78, 16, 110, AttackStyle.Whip, "enemy_static", "enemy_rend"), "static_eel"), StatId.Dodge, 10));
            St(RunMakeElite(RunEnemy("elite_null_wraith", "Null Wraith", 880, 78, 18, 110, AttackStyle.Claw, "enemy_rend", "enemy_toxic")), StatId.Dodge, 12);
            St(RunMakeElite(RunEnemy("elite_grub_mother", "Grub Mother", 1060, 64, 28, 70, AttackStyle.Slam, "enemy_toxic", "enemy_harden")), StatId.CounterChance, 10);
            St(RunMakeElite(RunEnemy("elite_stalker_prime", "Stalker Prime", 950, 72, 22, 95, AttackStyle.Claw, "enemy_rend", "enemy_enrage")), StatId.CritChance, 10);
            // new biome elites (art pending)
            P(RunMakeElite(RunEnemy("elite_bloat_captain", "Bloat Captain", 1000, 68, 20, 80, AttackStyle.Slam, "enemy_burning_touch", "enemy_enrage")));
            P(St(RunMakeElite(RunEnemy("elite_trench_crusher", "Trench Crusher", 1000, 66, 30, 75, AttackStyle.Slam, "enemy_stun_slam")), StatId.CounterChance, 12));
            P(St(RunMakeElite(RunEnemy("elite_canopy_stalker", "Canopy Stalker", 860, 78, 18, 110, AttackStyle.Claw, "enemy_static")), StatId.CritChance, 15));
            P(St(RunMakeElite(RunEnemy("elite_obelisk_guardian", "Obelisk Guardian", 1040, 64, 32, 65, AttackStyle.Slam, "enemy_stun_slam")), StatId.CounterChance, 12));
            P(RunMakeElite(RunEnemy("elite_walrus_warlord", "Walrus Warlord", 1000, 66, 28, 75, AttackStyle.Slam, "enemy_stun_slam", "enemy_harden")));
            P(St(RunMakeElite(RunEnemy("elite_reef_guardian", "Reef Guardian", 1080, 62, 32, 65, AttackStyle.Slam, "enemy_harden")), StatId.CounterChance, 15));
            P(RunMakeElite(RunEnemy("elite_chrome_enforcer", "Chrome Enforcer", 980, 68, 28, 80, AttackStyle.Blaster, "enemy_harden", "enemy_stun_slam")));
            P(RunMakeElite(RunEnemy("elite_mycelium_knight", "Mycelium Knight", 980, 68, 26, 80, AttackStyle.Drill, "enemy_toxic", "enemy_harden")));
            P(St(RunMakeElite(RunEnemy("elite_licorice_lasher", "Licorice Lasher", 880, 76, 18, 105, AttackStyle.Whip, "enemy_rend")), StatId.Dodge, 10));
            P(RunMakeElite(RunEnemy("elite_gumball_golem", "Gumball Golem", 1060, 64, 30, 65, AttackStyle.Slam, "enemy_harden", "enemy_stun_slam")));
            P(St(RunMakeElite(RunEnemy("elite_shard_golem", "Shard Golem", 1060, 64, 30, 65, AttackStyle.Slam, "enemy_harden")), StatId.CounterChance, 15));
            P(RunMakeElite(RunEnemy("elite_clockwork_knight", "Clockwork Knight", 980, 68, 26, 85, AttackStyle.Drill, "enemy_time_snap", "enemy_harden")));
            P(St(RunMakeElite(RunEnemy("elite_cryo_captain", "Cryo Captain", 900, 74, 20, 95, AttackStyle.Blaster, "enemy_time_snap")), StatId.CritChance, 10));

            // Bosses (about the boss budget). The four invasion bosses double as biome bosses; invasions keep them (LiveOps
            // InvasionDef.BossId, fought with BattleSimulator.PlayerVsBoss, scaled by invader power with the group's HP pool).
            RunMakeBoss(RunEnemy("boss_magma_tyrant", "Magma Tyrant", 1900, 70, 22, 95, AttackStyle.Slam, "boss_magma_eruption"));
            RunMakeBoss(RunEnemy("boss_frost_leviathan", "Frost Leviathan", 2100, 66, 28, 90, AttackStyle.Whip, "boss_frost_tide"));
            RunMakeBoss(RunEnemy("boss_storm_hive", "Storm Hive Queen", 1800, 72, 20, 110, AttackStyle.Blaster, "boss_hive_swarm"));
            RunMakeBoss(RunEnemy("boss_void_maw", "Void Maw", 1900, 70, 26, 100, AttackStyle.Cannon, "boss_void_hunger"));
            RunMakeBoss(RunEnemy("boss_gloop_matriarch", "Gloop Matriarch", 2100, 64, 24, 85, AttackStyle.Slam, "boss_toxic_flood"));
            RunMakeBoss(RunEnemy("boss_rust_queen", "Rust Queen", 1950, 70, 30, 95, AttackStyle.Claw, "boss_salvage"));
            RunMakeBoss(P(RunEnemy("boss_hollow_cantor", "Hollow Cantor", 1900, 70, 20, 105, AttackStyle.Blaster, "boss_feedback_song")));
            RunMakeBoss(RunEnemy("boss_prism_tyrant", "Prism Tyrant", 2000, 68, 32, 90, AttackStyle.Cannon, "boss_refraction"));
            RunMakeBoss(RunEnemy("boss_elder_magma", "Elder Magma", 1950, 70, 22, 95, AttackStyle.Slam, "boss_molten_core"));
            RunMakeBoss(RunEnemy("boss_chrono_wyrm", "Chrono Wyrm", 1950, 70, 24, 100, AttackStyle.Whip, "boss_rewind"));
            RunMakeBoss(P(RunEnemy("boss_flare_medusa", "Flare Medusa", 1850, 72, 18, 105, AttackStyle.Whip, "boss_ignition")));
            RunMakeBoss(P(RunEnemy("boss_scarab_sun_king", "Scarab Sun-King", 2000, 68, 28, 90, AttackStyle.Slam, "boss_sun_glare")));
            RunMakeBoss(P(St(RunEnemy("boss_hailwing_roc", "Hailwing Roc", 1800, 75, 18, 115, AttackStyle.Claw), StatId.Dodge, 25)));
            RunMakeBoss(P(RunEnemy("boss_tusk_colossus", "Tusk Colossus", 2100, 64, 30, 80, AttackStyle.Slam, "boss_ice_armor")));
            RunMakeBoss(P(RunEnemy("boss_tide_hydra", "Tide Hydra", 2000, 68, 24, 95, AttackStyle.Whip, "boss_regrow")));
            RunMakeBoss(P(RunEnemy("boss_volt_orchid", "Volt Orchid", 1900, 70, 20, 95, AttackStyle.Whip, "boss_lure")));
            RunMakeBoss(P(RunEnemy("boss_spore_monarch", "Spore Monarch", 2000, 68, 24, 90, AttackStyle.Slam, "boss_spore_cloud")));
            RunMakeBoss(P(RunEnemy("boss_bonbon_behemoth", "Bonbon Behemoth", 2100, 64, 26, 85, AttackStyle.Slam, "boss_sugar_rush")));
            RunMakeBoss(P(RunEnemy("boss_stasis_warden", "Stasis Warden", 2000, 68, 26, 95, AttackStyle.Cannon, "boss_time_stop")));
        }

        /// <summary>Makes an enemy an elite; <paramref name="artId"/> = the sprite it is drawn with until it has its own (EnemyDef.ArtId).</summary>
        private static EnemyDef RunMakeElite(EnemyDef e, string artId = null)
        {
            e.Elite = true; e.Xp = 14; e.Coins = 30; e.ArtId = artId;
            e.Stats.Add(new StatMod(StatId.CritChance, 10));
            e.Stats.Add(new StatMod(StatId.RageGain, 20)); // points: +20% rage gain (a % of the 0 base was nothing)
            return e;
        }

        private static EnemyDef RunMakeBoss(EnemyDef e)
        {
            e.Boss = true; e.Xp = 40; e.Coins = 40;
            e.Stats.Add(new StatMod(StatId.CritChance, 10));
            e.Stats.Add(new StatMod(StatId.StatusResist, 25));
            e.Stats.Add(new StatMod(StatId.CritResist, 20));
            return e;
        }

        private void RunBuildBosses()
        {
            CampaignBuildBosses(); // every campaign boss: its board kit, its own boss tiles, its endless variants
        }

        private BossDef RunBoss(string id, string name, string enemyId, string[] tiles, string escort, string hazard = "hazard_spikes")
        {
            var b = new BossDef { Id = id, Name = name, EnemyId = enemyId };
            b.BossTileIds.AddRange(tiles);
            b.Escorts.Add(escort);
            b.Actions.Add(new BossAwakenAction { AtMeter = 25, Kind = BossActionKind.BuffEnemyTiles, Value = 10, Text = name + " stirs: enemies grow stronger." });
            b.Actions.Add(new BossAwakenAction { AtMeter = 50, Kind = BossActionKind.TransformTiles, Count = 3, Param = hazard, Text = name + " scorches the path." });
            b.Actions.Add(new BossAwakenAction { AtMeter = 75, Kind = BossActionKind.Summon, Count = 2, Param = "elite", Value = 15, Text = name + " summons its guard." });
            b.Actions.Add(new BossAwakenAction { AtMeter = 100, Kind = BossActionKind.BuffBoss, Value = b.FullyAwakeStatPct, Text = name + " is fully awake!" });
            RunBosses[id] = b;
            return b;
        }

        // ---- tiles ----------------------------------------------------------------------------

        private TileDef RunTile(string id, string name, TileCategory cat, TileValence val)
        {
            var t = new TileDef { Id = id, Name = name, Category = cat, Valence = val };
            BoardTiles[id] = t;
            return t;
        }

        private void RunBuildTiles()
        {
            RunTile("start", "Landing Site", TileCategory.Start, TileValence.Neutral).Consumable = false;
            RunTile("empty", "Quiet Ground", TileCategory.Empty, TileValence.Neutral).Consumable = false;
            RunTile("enemy_1", "Prowlers", TileCategory.Enemy, TileValence.Enemy).EnemyLevel = 1;
            RunTile("enemy_2", "Pack", TileCategory.Enemy, TileValence.Enemy).EnemyLevel = 2;
            RunTile("enemy_3", "Horde", TileCategory.Enemy, TileValence.Enemy).EnemyLevel = 3;
            RunTile("elite", "Elite", TileCategory.Elite, TileValence.Elite).EnemyLevel = 3;
            var mb = RunTile("miniboss", "Miniboss Lair", TileCategory.Miniboss, TileValence.Elite); mb.Once = true; mb.EnemyLevel = 3;
            mb.ForceStop = true; // playtest 0.1.308: the run's two minibosses stand on the path and stop the worm (no clock ambushes)
            var b1 = RunTile("buff_atk", "Power Crystal", TileCategory.StatBuff, TileValence.Positive); b1.Stat = StatId.Atk; b1.Value = 6;
            var b2 = RunTile("buff_hp", "Life Bloom", TileCategory.StatBuff, TileValence.Positive); b2.Stat = StatId.Hp; b2.Value = 6;
            var b3 = RunTile("buff_def", "Iron Moss", TileCategory.StatBuff, TileValence.Positive); b3.Stat = StatId.Def; b3.Value = 8;
            var b4 = RunTile("buff_crit", "Sharp Stone", TileCategory.StatBuff, TileValence.Positive); b4.Stat = StatId.CritChance; b4.Value = 15;
            var b5 = RunTile("buff_speed", "Tailwind", TileCategory.StatBuff, TileValence.Positive); b5.Stat = StatId.Speed; b5.Value = 8;
            var d1 = RunTile("debuff_atk", "Rust Cloud", TileCategory.StatDebuff, TileValence.Negative); d1.Stat = StatId.Atk; d1.Value = -4;
            var d2 = RunTile("debuff_def", "Acid Pool", TileCategory.StatDebuff, TileValence.Negative); d2.Stat = StatId.Def; d2.Value = -6;
            var d3 = RunTile("debuff_hp", "Withering Field", TileCategory.StatDebuff, TileValence.Negative); d3.Stat = StatId.Hp; d3.Value = -4;
            // Ordinary tiles pay in-run resources only (coins, XP, HP, buffs). Account loot comes from the level's completion
            // rewards and a few risky opportunities per run (guarded Treasure Pods, events; RunTuning.MaxAccountLootOffersPerRun).
            var sup = RunTile("loot", "Supply Cache", TileCategory.Pickup, TileValence.Positive); sup.Coins = 30; sup.Value = 3;
            sup.Text = "Run coins and XP.";
            var rich = RunTile("loot_rich", "Rich Supply Cache", TileCategory.Pickup, TileValence.Positive); rich.Coins = 60; rich.Value = 6;
            rich.Text = "Lots of run coins and XP.";
            var pod = RunTile("treasure", "Treasure Pod", TileCategory.Treasure, TileValence.Special); pod.EventId = "treasure_pod";
            pod.Text = "A guarded reward chest.";
            RunTile("event", "Strange Signal", TileCategory.Event, TileValence.Special);
            var fire = RunTile("campfire", "Campfire", TileCategory.Campfire, TileValence.Positive); fire.EventId = "campfire";
            fire.MinMainLevel = Campaign.CampfiresFromLevel; // level-unlocked run content (GDD §16)
            var shop = RunTile("shop", "Wandering Shop", TileCategory.Shop, TileValence.Special); shop.ForceStop = true; shop.Consumable = false;
            var h1 = RunTile("hazard_spikes", "Crystal Spikes", TileCategory.Hazard, TileValence.Negative); h1.Value = 10;
            var h2 = RunTile("hazard_toll", "Toll Drones", TileCategory.Hazard, TileValence.Negative); h2.Coins = 30;
            var h3 = RunTile("hazard_vortex", "Gravity Vortex", TileCategory.Hazard, TileValence.Negative); h3.PushBack = 2; h3.Value = 4; h3.Text = "Lose 4% HP; your next roll moves 2 steps less.";
            var sh = RunTile("shard", "Portal Shard", TileCategory.Shard, TileValence.Positive); sh.TriggersOnPass = true;
            var cp = RunTile("coin_pickup", "Coin Cluster", TileCategory.Pickup, TileValence.Positive); cp.TriggersOnPass = true; cp.Coins = 15;
            var gate = RunTile("boss_gate", "Boss Arena", TileCategory.BossGate, TileValence.Boss); gate.Consumable = false; gate.Once = true;

            // Dedicated boss tiles (feed / relief / tribute, named for each campaign boss): ContentDb.CampaignBuildBosses.

            TilePools["pool_standard"] = RunStandardPool(1.0, 1.0);
            // every main level: one pool (no biome is harder or easier; the old family pools averaged 1.14 enemy / 1.08 negative weight)
            TilePools[Campaign.MainPool] = RunStandardPool(1.1, 1.05);
            TilePools["pool_burrow"] = RunStandardPool(1.3, 1.4);
            TilePools["pool_dark"] = RunStandardPool(1.5, 1.5);
            // The tutorial run (main level 1, GDD §16): roll, fight, level up, pick skills, beat the boss. Nothing else on
            // the board: no events, elites, hazards, debuffs, treasure or campfires.
            TilePools[Campaign.TutorialPool] = new List<WeightedId>
            {
                new WeightedId("empty", 5), new WeightedId("enemy_1", 16), new WeightedId("enemy_2", 8),
                new WeightedId("buff_atk", 3), new WeightedId("buff_hp", 3), new WeightedId("buff_def", 2),
                new WeightedId("loot", 7), new WeightedId("coin_pickup", 5), new WeightedId("shard", 3),
            };
        }

        private void RunBossTile(string id, string name, double meter, double bossPct, long coins)
        {
            var t = RunTile(id, name, TileCategory.BossTile, TileValence.Boss);
            t.MeterDelta = meter; t.BossStatPct = bossPct; t.Coins = coins; t.Consumable = false;
        }

        private static List<WeightedId> RunStandardPool(double enemyWeight, double negativeWeight)
        {
            return new List<WeightedId>
            {
                new WeightedId("empty", 6),
                new WeightedId("enemy_1", 14 * enemyWeight),
                new WeightedId("enemy_2", 9 * enemyWeight),
                new WeightedId("enemy_3", 4 * enemyWeight),
                new WeightedId("elite", 2.5 * enemyWeight),
                new WeightedId("buff_atk", 3), new WeightedId("buff_hp", 3), new WeightedId("buff_def", 2),
                new WeightedId("buff_crit", 1.5), new WeightedId("buff_speed", 1.5),
                new WeightedId("debuff_atk", 1.5 * negativeWeight), new WeightedId("debuff_def", 1.5 * negativeWeight),
                new WeightedId("debuff_hp", 1.5 * negativeWeight),
                // treasure 3 (was 2), with the loot events' weights (Abandoned Cache 4, Gravity Well 2.5, Rift Echo 3): the 20% shorter
                // runs (2026-11-27) keep ~0.8 account-loot opportunities per run (RunLengthTests)
                new WeightedId("loot", 7), new WeightedId("loot_rich", 2), new WeightedId("treasure", 3),
                new WeightedId("event", 8), new WeightedId("campfire", 2),
                new WeightedId("hazard_spikes", 2.5 * negativeWeight), new WeightedId("hazard_toll", 2 * negativeWeight),
                new WeightedId("hazard_vortex", 1.5 * negativeWeight),
                new WeightedId("shard", 3), new WeightedId("coin_pickup", 5),
            };
        }

        // ---- events ---------------------------------------------------------------------------

        private static RunEffect RunFx(RunEffectKind kind, double value, string param = null) => new RunEffect(kind, value, param);
        private static RunEffect RunStatFx(StatId stat, double pct) => new RunEffect(RunEffectKind.StatPercent, pct) { Stat = stat };
        private static RunEffect RunSkillFx(Rarity min) => new RunEffect(RunEffectKind.GainSkill, 1) { MinRarity = min };
        private static RunEffect RunRewardFx(Reward r) => new RunEffect(RunEffectKind.PendingReward, 1) { Reward = r };
        /// <summary>Boss ATK or HP % for the run (param RunSystem.BossAtkParam / BossHpParam; negative = weaker).</summary>
        private static RunEffect RunBossFx(string stat, double pct) => new RunEffect(RunEffectKind.BossStatPct, pct, stat);

        private static EventOutcome RunOut(double weight, string text, params RunEffect[] fx)
        {
            var o = new EventOutcome { Weight = weight, Text = text };
            o.Effects.AddRange(fx);
            return o;
        }

        private static EventOption RunOpt(string text, string risk, params EventOutcome[] outcomes)
        {
            var o = new EventOption { Text = text, RiskText = risk };
            o.Outcomes.AddRange(outcomes);
            return o;
        }

        private static readonly EventOutcome RunNothing = new EventOutcome { Weight = 1, Text = "You move on." };

        private void RunEvent(string id, string name, string text, int defaultOption, double randomWeight, params EventOption[] options)
        {
            var e = new EventDef { Id = id, Name = name, Text = text, DefaultOption = defaultOption };
            e.Options.AddRange(options);
            RunEvents[id] = e;
            if (randomWeight > 0) RandomRunEvents.Add(new WeightedId(id, randomWeight));
        }

        private void RunBuildEvents()
        {
            // skill redesign: a rare chance to find another signature (RunSystem.ResonanceEvent; never sold in shops)
            RunEvent("resonance", "Resonance", "Two signals hum in the dark. Attune to one and your worm learns its signature.", 0, 0.6,
                RunOpt("Attune to the first signal", "Gain the first signature.", RunOut(1, "A new signature awakens in you.", RunFx(RunEffectKind.GainSignature, 1, "0"))),
                RunOpt("Attune to the second signal", "Gain the second signature.", RunOut(1, "A new signature awakens in you.", RunFx(RunEffectKind.GainSignature, 1, "1"))),
                RunOpt("Harvest the signals", "Take " + RunTuning.ResonanceCoins + " coins.", RunOut(1, "The signals fade into coins.", RunFx(RunEffectKind.Coins, RunTuning.ResonanceCoins))));
            RunEvent("unstable_reactor", "Unstable Reactor", "A humming reactor offers raw power, but the boss feeds on the surge.", 1, 3,
                RunOpt("Absorb the surge", "Gain a skill. The boss gains +15% ATK.", RunOut(1, "Power floods in.", RunSkillFx(Rarity.Common), RunBossFx(Systems.RunSystem.BossAtkParam, 15))),
                RunOpt("Walk away", "Nothing happens.", RunNothing));
            RunEvent("smuggler", "Smuggler", "A shady alien smuggler trades rare techniques for a piece of your vitality.", 1, 3,
                RunOpt("Trade vitality", "-10% max HP for a Legendary+ skill.", RunOut(1, "A painful bargain.", RunStatFx(StatId.Hp, -10), RunSkillFx(Rarity.Legendary))),
                RunOpt("Decline", "Nothing happens.", RunNothing));
            // parts v2: runs drop no parts (Parts pulls are the source); the echo leaves a Parts Crate instead
            var echo = RunOpt("Fight the echo", "Battle a strong elite (+30%). Win: a Parts Crate (reward chest, paid on clear).",
                RunOut(1, "The echo fades, leaving a crate behind.", RunFx(RunEffectKind.Battle, 30, "elite_rift_echo"), RunRewardFx(Reward.Cur(Currencies.PartTokens, 1))));
            RunEvent("rift_echo", "Rift Echo", "An echo of a fearsome foe flickers in a rift.", 1, 3, echo, RunOpt("Leave it", "Nothing happens.", RunNothing));
            RunEvent("abandoned_cache", "Abandoned Cache", "A half-buried supply cache. Something ticks inside.", 1, 4,
                // biomes pass: one gain or one price per outcome (was: chest + alarm / HP + max HP)
                RunOpt("Search it", "60%: 1 reward chest (paid on clear, +Danger). 40%: trapped: lose 20% HP.",
                    RunOut(60, "Loot! Nobody heard a thing.", RunFx(RunEffectKind.Loot, 1)),
                    RunOut(40, "Boom! It was trapped.", RunFx(RunEffectKind.DamagePct, 20))),
                RunOpt("Leave it", "Nothing happens.", RunNothing));
            // Strange Spores moved to the Mushroom Forest (ContentDb.BiomeEvents.cs)
            var merchant = RunOpt("Buy a tonic (40 coins)", "Heal 30% HP.", RunOut(1, "Refreshing.", RunFx(RunEffectKind.HealPct, 30)));
            merchant.CoinCost = 40;
            RunEvent("alien_medic", "Alien Medic", "A friendly alien medic offers a tonic.", 1, 3, merchant, RunOpt("No thanks", "Nothing happens.", RunNothing));
            var altar = RunOpt("Offer 2 portal shards", "Gain a Legendary+ skill.", RunOut(1, "The altar glows.", RunSkillFx(Rarity.Legendary)));
            altar.ShardCost = 2;
            RunEvent("shard_altar", "Shard Altar", "An altar hums with portal energy.", 2, 3, altar,
                RunOpt("Pray", "Calm the boss: awakening -15.", RunOut(1, "A calm settles.", RunFx(RunEffectKind.Meter, -15))),
                RunOpt("Leave", "Nothing happens.", RunNothing));
            RunEvent("meteor_shower", "Meteor Shower", "Meteors streak across the sky!", 0, 2,
                RunOpt("Take shelter", "Lose 30 coins of time.", RunOut(1, "You wait it out.", RunFx(RunEffectKind.Coins, -30))),
                RunOpt("Run through it", "60%: unharmed with a shard. 40%: lose 20% HP.",
                    RunOut(60, "You dodge every rock and grab a shard.", RunFx(RunEffectKind.Shards, 1)), RunOut(40, "Ouch!", RunFx(RunEffectKind.DamagePct, 20))));
            RunEvent("lost_explorer", "Lost Explorer", "A lost explorer worm asks for help.", 1, 3,
                RunOpt("Help them (lose 10% HP)", "+60 coins.", RunOut(1, "Grateful, they share their coins.", RunFx(RunEffectKind.DamagePct, 10), RunFx(RunEffectKind.Coins, 60))),
                RunOpt("Ignore them", "Nothing happens.", RunNothing));
            RunEvent("gravity_well", "Gravity Well", "A swirling gravity well pulls loot from deep space.", 1, 2.5,
                RunOpt("Dive in", "2 reward chests (paid on clear, +Danger). The boss awakens 15% faster for the rest of the run.",
                    RunOut(1, "You surface with treasure — and the boss stirs.", RunFx(RunEffectKind.Loot, 2), RunFx(RunEffectKind.AwakenRatePct, 15))),
                RunOpt("Steer away", "Nothing happens.", RunNothing));
            RunEvent("echoing_shrine", "Echoing Shrine", "Ancient echoes can sharpen a technique.", 1, 2.5,
                RunOpt("Meditate", "Improve 1 skill of your choice; awakening +10.", RunOut(1, "Your technique evolves.", RunFx(RunEffectKind.UpgradeSkill, 1), RunFx(RunEffectKind.Meter, 10))),
                RunOpt("Leave", "Nothing happens.", RunNothing));
            var patch = RunOpt("Patch the leak (40 coins)", "Pay to stay safe.", RunOut(1, "Fixed.", RunFx(RunEffectKind.Coins, 0)));
            patch.CoinCost = 40;
            var fuel = new EventDef { Id = "fuel_leak", Name = "Fuel Leak", Text = "Your mount is leaking fuel.", DefaultOption = 1, Negative = true };
            fuel.Options.Add(patch);
            fuel.Options.Add(RunOpt("Ignore it", "-10% Speed for the rest of the run.", RunOut(1, "You sputter on.", RunStatFx(StatId.Speed, -10))));
            RunEvents[fuel.Id] = fuel;
            RandomRunEvents.Add(new WeightedId(fuel.Id, 2));
            RunEvent("wild_beast", "Wild Beast", "A wild beast eyes you curiously.", 1, 2.5,
                RunOpt("Try to tame it", "50%: it digs up 1 reward chest (paid on clear, +Danger). 50%: it attacks (elite fight).",
                    RunOut(50, "It digs up a chest for you.", RunFx(RunEffectKind.Loot, 1)),
                    RunOut(50, "It attacks!", RunFx(RunEffectKind.Battle, 10, "elite"))),
                RunOpt("Back off", "Nothing happens.", RunNothing));
            var bm = RunOpt("Buy a signal beacon (100 coins)", "A Schematic (reward chest, paid on clear, +Danger).",
                RunOut(1, "Sold. No receipts.", RunRewardFx(Reward.Cur(Currencies.BlueprintTokens, 1))));
            bm.CoinCost = 100;
            RunEvent("black_market", "Black Market", "A hidden market trades in rare signals.", 1, 2, bm, RunOpt("Leave", "Nothing happens.", RunNothing));
            RunEvent("cursed_relic", "Cursed Relic", "A relic radiates power and malice.", 1, 2,
                RunOpt("Claim it", "Gain a Legendary+ skill. -15% max HP for the run.", RunOut(1, "Power at a price.", RunSkillFx(Rarity.Legendary), RunStatFx(StatId.Hp, -15))),
                RunOpt("Leave it", "Nothing happens.", RunNothing));
            RunEvent("training_dummy", "Training Grounds", "Old training machines still work.", 0, 2.5,
                RunOpt("Train", "Gain 25 XP.", RunOut(1, "You feel sharper.", RunFx(RunEffectKind.Xp, 25))),
                RunOpt("Spar with an elite", "Battle an elite for +6% ATK.", RunOut(1, "Elite beaten!", RunFx(RunEffectKind.Battle, 0, "elite"), RunStatFx(StatId.Atk, 6))));
            RunEvent("boss_scout", "Boss Scout", "One of the boss's scouts is watching you.", 0, 2,
                RunOpt("Chase it down", "Battle; awakening -20 on win.", RunOut(1, "The scout is silenced.", RunFx(RunEffectKind.Battle, 0, "enemy"), RunFx(RunEffectKind.Meter, -20))),
                RunOpt("Let it go", "Awakening +10.", RunOut(1, "The scout reports back.", RunFx(RunEffectKind.Meter, 10))));

            // Tile-bound events
            RunEvent("treasure_pod", "Treasure Pod", "A sealed treasure pod, guarded by an elite. Opening it is loud.", 1, 0,
                RunOpt("Fight the guardian", "Elite fight. Win: 2 reward chests (paid on clear, each +Danger); nearby enemies grow stronger.",
                    RunOut(1, "The guardian falls. Loot spills out.", RunFx(RunEffectKind.Battle, 10, "elite"), RunFx(RunEffectKind.Loot, 2), RunFx(RunEffectKind.StrengthenEnemies, 10))),
                RunOpt("Leave", "Nothing happens.", RunNothing));
            // Train: the player picks which owned skill improves (RunChooseEventOption.SkillId, RunSystem.TrainChoices)
            RunEvent("campfire", "Campfire", "A warm campfire. Its light draws nearby enemies.", 0, 0,
                RunOpt("Rest", "Restore 35% HP. Nearby enemies grow stronger.", RunOut(1, "You rest.", RunFx(RunEffectKind.HealPct, 35), RunFx(RunEffectKind.StrengthenEnemies, 12))),
                RunOpt("Train", "Improve 1 skill of your choice. Nearby enemies grow stronger.", RunOut(1, "You hone a technique.", RunFx(RunEffectKind.UpgradeSkill, 1), RunFx(RunEffectKind.StrengthenEnemies, 12))));
        }

        // ---- shops & loot ---------------------------------------------------------------------

        private void RunBuildShopsAndLoot()
        {
            var std = new ShopTableDef { Id = "shop_standard" };
            // three run tiers: Legendary = 2× Common, Mythic = 3× Common (the old odds folded: C+R, E+L, M)
            std.RarityWeights[Rarity.Common] = 75; std.RarityWeights[Rarity.Legendary] = 24; std.RarityWeights[Rarity.Mythic] = 1;
            std.SkillPrices[Rarity.Common] = 40; std.SkillPrices[Rarity.Legendary] = 80; std.SkillPrices[Rarity.Mythic] = 120;
            std.SkillOffers = 3; std.StatPerkOffers = 1; // 3 skills + a stat perk (+ the heal while hurt), every reroll too (playtest 0.1.325)
            std.StatOffers.Add(new StatMod(StatId.Atk, 0, 8));
            std.StatOffers.Add(new StatMod(StatId.Hp, 0, 10));
            std.StatOffers.Add(new StatMod(StatId.Def, 0, 10));
            std.StatOffers.Add(new StatMod(StatId.CritChance, 5));
            std.StatOffers.Add(new StatMod(StatId.Speed, 0, 8));
            std.StatOffers.Add(new StatMod(StatId.LifeSteal, 4));
            std.StatOffers.Add(new StatMod(StatId.DamageReduction, 4));
            RunShopTables[std.Id] = std;

            var dark = new ShopTableDef { Id = "shop_dark", HealPct = 25, HealPrice = 45, StatPrice = 60, RerollBase = 20, RerollStep = 15, SkillOffers = std.SkillOffers, StatPerkOffers = std.StatPerkOffers, HealOfferBelowPct = std.HealOfferBelowPct };
            foreach (var kv in std.RarityWeights) dark.RarityWeights[kv.Key] = kv.Value;
            dark.RarityWeights[Rarity.Legendary] = 28; dark.RarityWeights[Rarity.Mythic] = 3;
            dark.SkillPrices[Rarity.Common] = 60; dark.SkillPrices[Rarity.Legendary] = 120; dark.SkillPrices[Rarity.Mythic] = 180; // ×1.5
            dark.StatOffers.AddRange(std.StatOffers);
            RunShopTables[dark.Id] = dark;

            // Loot tables: every system is fed by runs (currencies, parts/runes, rare blueprint/alien drops).
            // Item drops use Id="*" = "random def of the given Rarity" (see skill-conventions.md).
            int chapters = (Campaign.MaterializedLevels + Campaign.ChapterLevels - 1) / Math.Max(1, Campaign.ChapterLevels);
            for (int p = 0; p < Math.Max(3, chapters); p++)
            {
                var t = CampaignChapterLoot(p);
                RunLootTables[t.Id] = t;
            }
            var burrow = new LootTableDef { Id = "loot_burrow" };
            burrow.Entries.AddRange(RunLootTables["loot_p2"].Entries);
            // Advancement Plates are gone (parts v2: fixed level caps); their Burrow share pays Crates at 75 plates = 1 Crate
            // (a plate ≈ 2 Diamonds: the Plate Foundry made 0.5/h where the Diamond Mine makes 1/h; a Crate = 1 pull = 150
            // Diamonds). The old entry (weight 2, 1–2 plates) becomes 1 Crate at weight 0.04 (same expected value).
            burrow.Entries.Add(new LootEntry(0.04, RewardKinds.Currency, Currencies.PartTokens, 1, 1));
            RunLootTables[burrow.Id] = burrow;
            var darkLoot = new LootTableDef { Id = "loot_dark" };
            darkLoot.Entries.AddRange(RunLootTables["loot_p3"].Entries);
            darkLoot.Entries.Add(new LootEntry(5, RewardKinds.Currency, Currencies.Diamonds, 10, 30));
            darkLoot.Entries.Add(new LootEntry(1, RewardKinds.Currency, Currencies.BlueprintTokens, 1, 2)); // pull resources, never shards
            darkLoot.Entries.Add(new LootEntry(1, RewardKinds.Currency, Currencies.AlienTokens, 1, 2));
            RunLootTables[darkLoot.Id] = darkLoot;
        }

        // ---- portals & levels -----------------------------------------------------------------

        private void RunBuildPortals()
        {
            // Main campaign: one portal per level, biomes in a seeded shuffle; the first Campaign.MaterializedLevels levels
            // are materialized here, every later one is generated on demand by the same code (ContentDb.Campaign.cs).
            for (int n = 1; n <= Campaign.MaterializedLevels; n++)
            {
                var l = CampaignBuildMainLevel(n, out var portal);
                Portals[portal.Id] = portal;
                RunLevels[l.Id] = l;
            }

            // Burrow: multi-floor dive (floors = levels), entry costs a Burrow Key, cash out between floors.
            var burrow = new PortalDef { Id = "the_burrow", Name = "The Burrow", Kind = PortalKind.Burrow, Order = Campaign.SidePortalOrder, EntryCurrency = Currencies.BurrowKeys, EntryCost = 1 };
            for (int f = 1; f <= 10; f++)
            {
                var l = RunStoreLevel("the_burrow_" + f, "Burrow Floor " + f, burrow.Id, f, 4 + (f - 1) * 2, "pool_burrow", "boss_void_maw", "loot_burrow",
                    new[] { "burrow_grub", "tunnel_stalker", "void_spawn" }, new[] { "elite_burrow_horror", "elite_grub_mother", "elite_stalker_prime" });
                l.FixedTiles.RemoveAll(ft => ft.TileId == "shop" && ft.At > 0.8); // short floors: one shop each
                l.BoardLength = 18; l.MaxRolls = 6; // short floors: the boss comes at the roll limit l.StaminaCost = 0; l.CompletionLootRolls = 1;
                l.CompletionRewards.Add(Reward.Cur(Currencies.GearCores, 3 + f));
                // Deep floors pay Fuel (mount levels; was 1 Mount Core = 300 Fuel); the two deepest floors have a small
                // chance of an Awaken Core (rare drop, paid unscaled on cash-out).
                l.CompletionRewards.Add(Reward.Cur(Currencies.Fuel, f >= 6 ? 300 : 0));
                l.CompletionRewards.RemoveAll(r => r.Amount <= 0);
                // Floors 5+ paid 1 Advancement Plate each; now a 1.5% chance of a Crate (75 plates = 1 Crate, see loot_burrow).
                if (f >= 5) l.RareDrops.Add(new RareDrop(Reward.Cur(Currencies.PartTokens, 1), 1.5));
                if (f >= 9) l.RareDrops.Add(new RareDrop(Reward.Cur(ProgressionCurrencies.PartAwakenCores, 1), f == 9 ? 1.0 : 1.5));
                burrow.LevelIds.Add(l.Id);
            }
            Portals[burrow.Id] = burrow;

            // Dark Side: procedural levels along the campaign curve, extreme modifiers, own rewards. The long-term Data source
            // for the Laboratory (Data grows with the level).
            var dark = new PortalDef { Id = DarkSidePortalId, Name = "Dark Side of the Planet", Kind = PortalKind.DarkSide, Order = Campaign.SidePortalOrder + 1, Endless = true };
            for (int i = 1; i <= Campaign.DarkSideLevels; i++)
            {
                var l = CampaignBuildDarkSideLevel(i);
                RunLevels[l.Id] = l;
                dark.LevelIds.Add(l.Id);
            }
            Portals[dark.Id] = dark;
        }

        private LevelDef RunStoreLevel(string id, string name, string portalId, int index, int threat, string pool, string bossId, string loot, string[] enemies, string[] elites)
        {
            var l = RunNewLevel(id, name, portalId, index, threat, pool, bossId, loot, enemies, elites);
            RunLevels[id] = l;
            return l;
        }

        private LevelDef RunNewLevel(string id, string name, string portalId, int index, int threat, string pool, string bossId, string loot, string[] enemies, string[] elites)
        {
            var l = new LevelDef
            {
                Id = id, Name = name, PortalId = portalId, Index = index, Threat = threat, TilePoolId = pool, BossId = bossId,
                LootTableId = loot, MinibossId = elites[0],
            };
            l.EnemyPool.AddRange(enemies);
            l.ElitePool.AddRange(elites);
            // One lap of the circuit: a campfire, two shops (forced stops every lap) and the miniboss lair (minibosses also come
            // with the awakening clock, RunTuning.MinibossAtMeter).
            l.FixedTiles.Add(new FixedTile(0.22, "campfire"));
            l.FixedTiles.Add(new FixedTile(0.42, "shop"));
            l.FixedTiles.Add(new FixedTile(0.68, "miniboss"));
            l.FixedTiles.Add(new FixedTile(0.88, "shop"));
            return l;
        }
    }
}
