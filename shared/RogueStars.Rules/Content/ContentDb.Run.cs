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
        /// Main portals use t = portalIndex·10 + level-1 (0..29), Overcharged +8, Burrow 4..22, Dark Side 30..48,
        /// leaving room for many more portals (t=60 ≈ 260×, t=100 ≈ 8000×) as worm/parts/buildings scale for years.
        /// </summary>
        public double ThreatMultiplier(int threat)
        {
            if (threat < 0) threat = 0;
            return Math.Pow(RunThreat.Growth, threat) * (1 + RunThreat.Linear * threat);
        }

        /// <summary>Multiplier of a fractional threat position relative to its integer part (≥ 1).</summary>
        public double ThreatFractionMultiplier(double threat)
        {
            int whole = (int)Math.Floor(threat + 1e-9);
            double frac = threat - whole;
            if (frac <= 1e-9) return 1;
            return Math.Pow(ThreatMultiplier(whole + 1) / ThreatMultiplier(whole), frac);
        }

        /// <summary>Any run level by id: materialized (RunLevels) or generated on demand (endless main portals, Dark Side).</summary>
        public LevelDef RunLevel(string id)
        {
            if (id == null) return null;
            return RunLevels.TryGetValue(id, out var l) ? l : GeneratedLevel(id);
        }

        /// <summary>Any portal by id: materialized (Portals) or a generated endless main portal.</summary>
        public PortalDef Portal(string id)
        {
            if (id == null) return null;
            return Portals.TryGetValue(id, out var p) ? p : GeneratedPortal(id);
        }

        /// <summary>Run loot table by id (materialized or generated with its endless portal).</summary>
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
            RunBuildBosses();
            RunBuildTiles();
            RunBuildEvents();
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

            b.Statuses[StatusId.Burn] = new StatusDef { Id = StatusId.Burn, MaxStacks = 10, DefaultDuration = 3, DotAtkPct = 4, HasElement = true, ElementStat = StatId.FireDamage };
            b.Statuses[StatusId.Poison] = new StatusDef { Id = StatusId.Poison, MaxStacks = 20, DefaultDuration = 4, DotAtkPct = 2.5, HealReductionPct = 30, HasElement = true, ElementStat = StatId.PoisonDamage };
            b.Statuses[StatusId.Bleed] = new StatusDef { Id = StatusId.Bleed, MaxStacks = 5, DefaultDuration = 3, DotAtkPct = 6, IgnoresShield = true, HasElement = true, ElementStat = StatId.BleedDamage };
            b.Statuses[StatusId.Freeze] = new StatusDef { Id = StatusId.Freeze, MaxStacks = 1, DefaultDuration = 1, SkipsTurn = true, DamageTakenPctPerStack = 20, ApplyCooldownRounds = 3, HasElement = true, ElementStat = StatId.IceDamage };
            b.Statuses[StatusId.Stun] = new StatusDef { Id = StatusId.Stun, MaxStacks = 1, DefaultDuration = 1, SkipsTurn = true, ApplyCooldownRounds = 3 };
            b.Statuses[StatusId.Shock] = new StatusDef { Id = StatusId.Shock, MaxStacks = 5, DefaultDuration = 2, DamageTakenPctPerStack = 6, HasElement = true, ElementStat = StatId.LightningDamage };
            b.Statuses[StatusId.Charm] = new StatusDef { Id = StatusId.Charm, MaxStacks = 1, DefaultDuration = 1, Charms = true, ApplyCooldownRounds = 4 };
        }

        private void RunBuildRunTuning()
        {
            var t = RunTuning;
            t.DraftRarityWeights[Rarity.Common] = 55;
            t.DraftRarityWeights[Rarity.Rare] = 28;
            t.DraftRarityWeights[Rarity.Epic] = 12;
            t.DraftRarityWeights[Rarity.Legendary] = 4;
            t.DraftRarityWeights[Rarity.Mythic] = 1;
            t.SegmentThemes.Add(new SegmentTheme { Name = "Hunting Grounds", Enemy = 1.45, Reward = 0.9 });
            t.SegmentThemes.Add(new SegmentTheme { Name = "Old Ruins", Event = 1.7, Reward = 1.3, Enemy = 0.85 });
            t.SegmentThemes.Add(new SegmentTheme { Name = "Badlands", Hazard = 1.7, Reward = 1.5, Buff = 0.7 });
            t.SegmentThemes.Add(new SegmentTheme { Name = "Quiet Valley", Buff = 1.8, Enemy = 0.8, Hazard = 0.6 });
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
            RunEnemySkill("enemy_burning_touch", "Burning Touch", new SkillTrigger { When = TriggerKind.OnHit, Chance = 30, Effect = EffectKind.ApplyStatus, Status = StatusId.Burn });
            RunEnemySkill("enemy_chill", "Chilling Grip", new SkillTrigger { When = TriggerKind.OnHit, Chance = 12, Effect = EffectKind.ApplyStatus, Status = StatusId.Freeze });
            RunEnemySkill("enemy_static", "Static Skin", new SkillTrigger { When = TriggerKind.OnHit, Chance = 35, Effect = EffectKind.ApplyStatus, Status = StatusId.Shock });
            RunEnemySkill("enemy_toxic", "Toxic Bite", new SkillTrigger { When = TriggerKind.OnHit, Chance = 40, Effect = EffectKind.ApplyStatus, Status = StatusId.Poison, Stacks = 2 });
            RunEnemySkill("enemy_rend", "Rend", new SkillTrigger { When = TriggerKind.OnHit, Chance = 25, Effect = EffectKind.ApplyStatus, Status = StatusId.Bleed });
            RunEnemySkill("enemy_harden", "Harden", new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.Shield, Target = TargetKind.Self, Value = 15 });
            RunEnemySkill("enemy_enrage", "Enrage", new SkillTrigger { When = TriggerKind.OnLowHp, Effect = EffectKind.BuffStat, Target = TargetKind.Self, Stat = new StatMod(StatId.Atk, 0, 40) });
            RunEnemySkill("enemy_stun_slam", "Stunning Slam", new SkillTrigger { When = TriggerKind.OnRageAttack, Chance = 50, Effect = EffectKind.ApplyStatus, Status = StatusId.Stun });
            RunEnemySkill("boss_magma_eruption", "Eruption", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 4, Effect = EffectKind.Damage, Value = 80, Target = TargetKind.AllEnemies },
                new SkillTrigger { When = TriggerKind.OnHit, Chance = 40, Effect = EffectKind.ApplyStatus, Status = StatusId.Burn, Stacks = 2 });
            RunEnemySkill("boss_frost_tide", "Frost Tide", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 5, Effect = EffectKind.ApplyStatus, Status = StatusId.Freeze, Target = TargetKind.AllEnemies },
                new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.Shield, Target = TargetKind.Self, Value = 20 });
            RunEnemySkill("boss_hive_swarm", "Swarm Call", new SkillTrigger { When = TriggerKind.OnRoundStart, CooldownRounds = 5, Effect = EffectKind.Summon, Param = "spark_drone" },
                new SkillTrigger { When = TriggerKind.OnHit, Chance = 50, Effect = EffectKind.ApplyStatus, Status = StatusId.Shock, Stacks = 2 });
            RunEnemySkill("boss_void_hunger", "Void Hunger", new SkillTrigger { When = TriggerKind.OnRoundEnd, CooldownRounds = 3, Effect = EffectKind.TrueDamage, Value = 60, Target = TargetKind.Enemy });
            // passive stat packages on enemy skills
            Skills["boss_void_hunger"].Stats.Add(new StatMod(StatId.LifeSteal, 20));

            // Portal 1: Ember Wastes
            RunEnemy("ember_imp", "Ember Imp", 300, 45, 8, 95, AttackStyle.Claw, "enemy_burning_touch");
            RunEnemy("magma_slug", "Magma Slug", 420, 38, 16, 70, AttackStyle.Slam, "enemy_harden");
            RunEnemy("cinder_hound", "Cinder Hound", 260, 52, 6, 115, AttackStyle.Claw).Stats.Add(new StatMod(StatId.ComboChance, 15));
            RunEnemy("ash_wraith", "Ash Wraith", 240, 48, 4, 105, AttackStyle.Whip).Stats.Add(new StatMod(StatId.Dodge, 15));
            // Portal 2: Glacier Rift
            RunEnemy("frost_mite", "Frost Mite", 280, 44, 10, 100, AttackStyle.Claw, "enemy_chill");
            RunEnemy("ice_crawler", "Ice Crawler", 380, 40, 18, 80, AttackStyle.Drill, "enemy_harden");
            RunEnemy("glacier_golem", "Glacier Golem", 520, 42, 24, 60, AttackStyle.Slam).Stats.Add(new StatMod(StatId.CounterChance, 12));
            RunEnemy("snow_lurker", "Snow Lurker", 260, 55, 6, 120, AttackStyle.Claw).Stats.Add(new StatMod(StatId.CritChance, 15));
            // Portal 3: Static Jungle
            RunEnemy("volt_beetle", "Volt Beetle", 340, 46, 14, 90, AttackStyle.Drill, "enemy_static");
            RunEnemy("static_eel", "Static Eel", 280, 50, 8, 110, AttackStyle.Whip, "enemy_static");
            RunEnemy("spark_drone", "Spark Drone", 180, 40, 4, 125, AttackStyle.Blaster);
            RunEnemy("thunder_moth", "Thunder Moth", 250, 52, 6, 115, AttackStyle.Whip).Stats.Add(new StatMod(StatId.Dodge, 20));
            // Burrow / Dark Side
            RunEnemy("burrow_grub", "Burrow Grub", 360, 46, 12, 85, AttackStyle.Drill, "enemy_toxic");
            RunEnemy("tunnel_stalker", "Tunnel Stalker", 300, 55, 8, 110, AttackStyle.Claw, "enemy_rend");
            RunEnemy("void_spawn", "Void Spawn", 320, 56, 10, 100, AttackStyle.Whip, "enemy_toxic");
            RunEnemy("null_shade", "Null Shade", 280, 60, 6, 120, AttackStyle.Claw, "enemy_rend").Stats.Add(new StatMod(StatId.Dodge, 18));

            // Elites
            RunMakeElite(RunEnemy("elite_lava_brute", "Lava Brute", 950, 72, 22, 80, AttackStyle.Slam, "enemy_burning_touch", "enemy_enrage"));
            RunMakeElite(RunEnemy("elite_frost_knight", "Frost Knight", 900, 66, 30, 85, AttackStyle.Drill, "enemy_chill", "enemy_harden"));
            RunMakeElite(RunEnemy("elite_storm_caller", "Storm Caller", 820, 74, 18, 105, AttackStyle.Blaster, "enemy_static", "enemy_stun_slam"));
            RunMakeElite(RunEnemy("elite_void_reaver", "Void Reaver", 980, 80, 20, 100, AttackStyle.Claw, "enemy_rend", "enemy_enrage"));
            RunMakeElite(RunEnemy("elite_rift_echo", "Rift Echo", 1000, 78, 24, 100, AttackStyle.Cannon, "enemy_stun_slam"));
            RunMakeElite(RunEnemy("elite_burrow_horror", "Burrow Horror", 1100, 70, 26, 75, AttackStyle.Slam, "enemy_toxic", "enemy_enrage"));

            // RunBosses
            RunMakeBoss(RunEnemy("boss_magma_tyrant", "Magma Tyrant", 1900, 70, 22, 95, AttackStyle.Slam, "boss_magma_eruption"));
            RunMakeBoss(RunEnemy("boss_frost_leviathan", "Frost Leviathan", 2100, 66, 28, 90, AttackStyle.Whip, "boss_frost_tide"));
            RunMakeBoss(RunEnemy("boss_storm_hive", "Storm Hive Queen", 1800, 72, 20, 110, AttackStyle.Blaster, "boss_hive_swarm"));
            RunMakeBoss(RunEnemy("boss_void_maw", "Void Maw", 2300, 78, 26, 100, AttackStyle.Cannon, "boss_void_hunger"));

            // Invaders (LiveOps InvasionDef.BossId): fought by the server with BattleSimulator.PlayerVsBoss; the
            // server scales them by invader power and gives them the group's remaining HP pool.
            RunMakeBoss(RunEnemy("boss_gloop_matriarch", "Gloop Matriarch", 2400, 64, 24, 85, AttackStyle.Slam, "enemy_toxic"));
            RunMakeBoss(RunEnemy("boss_rust_queen", "Rust Queen", 2200, 74, 30, 95, AttackStyle.Claw, "enemy_rend"));
            RunMakeBoss(RunEnemy("boss_hollow_cantor", "Hollow Cantor", 2000, 80, 20, 105, AttackStyle.Blaster, "boss_void_hunger"));
            RunMakeBoss(RunEnemy("boss_prism_tyrant", "Prism Tyrant", 2600, 70, 32, 90, AttackStyle.Cannon, "enemy_stun_slam"));
        }

        private static void RunMakeElite(EnemyDef e)
        {
            e.Elite = true; e.Xp = 14; e.Coins = 25;
            e.Stats.Add(new StatMod(StatId.CritChance, 10));
            e.Stats.Add(new StatMod(StatId.RageGain, 0, 20));
        }

        private static void RunMakeBoss(EnemyDef e)
        {
            e.Boss = true; e.Xp = 40; e.Coins = 40;
            e.Stats.Add(new StatMod(StatId.CritChance, 10));
            e.Stats.Add(new StatMod(StatId.StatusResist, 25));
            e.Stats.Add(new StatMod(StatId.CritResist, 20));
        }

        private void RunBuildBosses()
        {
            RunBoss("boss_magma_tyrant", "Magma Tyrant", "boss_magma_tyrant", new[] { "bt_magma_vent", "bt_cooling_pool", "bt_tyrant_shrine" }, "ember_imp");
            RunBoss("boss_frost_leviathan", "Frost Leviathan", "boss_frost_leviathan", new[] { "bt_frozen_heart", "bt_thaw_beacon", "bt_leviathan_tribute" }, "frost_mite");
            RunBoss("boss_storm_hive", "Storm Hive Queen", "boss_storm_hive", new[] { "bt_hive_node", "bt_grounding_rod", "bt_hive_tribute" }, "spark_drone");
            RunBoss("boss_void_maw", "Void Maw", "boss_void_maw", new[] { "bt_void_rift", "bt_seal_stone", "bt_void_tribute" }, "void_spawn");
            CampaignBuildBossVariants();
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
            var sup = RunTile("loot", "Supply Cache", TileCategory.Pickup, TileValence.Positive); sup.Coins = 25; sup.Value = 3;
            sup.Text = "Run coins and XP.";
            var rich = RunTile("loot_rich", "Rich Supply Cache", TileCategory.Pickup, TileValence.Positive); rich.Coins = 50; rich.Value = 6;
            rich.Text = "Lots of run coins and XP.";
            var pod = RunTile("treasure", "Treasure Pod", TileCategory.Treasure, TileValence.Special); pod.EventId = "treasure_pod";
            pod.Text = "Guarded account loot.";
            RunTile("event", "Strange Signal", TileCategory.Event, TileValence.Special);
            RunTile("campfire", "Campfire", TileCategory.Campfire, TileValence.Positive).EventId = "campfire";
            var shop = RunTile("shop", "Wandering Shop", TileCategory.Shop, TileValence.Special); shop.ForceStop = true; shop.Consumable = false;
            var h1 = RunTile("hazard_spikes", "Crystal Spikes", TileCategory.Hazard, TileValence.Negative); h1.Value = 10;
            var h2 = RunTile("hazard_toll", "Toll Drones", TileCategory.Hazard, TileValence.Negative); h2.Coins = 30;
            var h3 = RunTile("hazard_vortex", "Gravity Vortex", TileCategory.Hazard, TileValence.Negative); h3.PushBack = 2; h3.Value = 4; h3.Text = "Lose 4% HP; your next roll moves 2 steps less.";
            var sh = RunTile("shard", "Portal Shard", TileCategory.Shard, TileValence.Positive); sh.TriggersOnPass = true;
            var cp = RunTile("coin_pickup", "Coin Cluster", TileCategory.Pickup, TileValence.Positive); cp.TriggersOnPass = true; cp.Coins = 12;
            var gate = RunTile("boss_gate", "Boss Arena", TileCategory.BossGate, TileValence.Boss); gate.Consumable = false; gate.Once = true;

            // Dedicated boss tiles: meter +/- and boss buffs (risk/reward: tributes pay coins but feed the boss).
            RunBossTile("bt_magma_vent", "Magma Vent", 10, 4, 0);
            RunBossTile("bt_cooling_pool", "Cooling Pool", -10, 0, 0);
            RunBossTile("bt_tyrant_shrine", "Tyrant Shrine", 5, 6, 30);
            RunBossTile("bt_frozen_heart", "Frozen Heart", 10, 4, 0);
            RunBossTile("bt_thaw_beacon", "Thaw Beacon", -10, 0, 0);
            RunBossTile("bt_leviathan_tribute", "Leviathan Tribute", 5, 6, 30);
            RunBossTile("bt_hive_node", "Hive Node", 10, 4, 0);
            RunBossTile("bt_grounding_rod", "Grounding Rod", -10, 0, 0);
            RunBossTile("bt_hive_tribute", "Hive Tribute", 5, 6, 30);
            RunBossTile("bt_void_rift", "Void Rift", 12, 5, 0);
            RunBossTile("bt_seal_stone", "Seal Stone", -10, 0, 0);
            RunBossTile("bt_void_tribute", "Void Tribute", 6, 8, 40);

            TilePools["pool_standard"] = RunStandardPool(1.0, 1.0);
            TilePools["pool_ember"] = RunStandardPool(1.0, 1.0);
            TilePools["pool_glacier"] = RunStandardPool(1.1, 1.0);
            TilePools["pool_static"] = RunStandardPool(1.2, 1.1);
            TilePools["pool_burrow"] = RunStandardPool(1.3, 1.4);
            TilePools["pool_dark"] = RunStandardPool(1.5, 1.5);
            TilePools["pool_void"] = RunStandardPool(1.25, 1.2);
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
                new WeightedId("loot", 7), new WeightedId("loot_rich", 2), new WeightedId("treasure", 2),
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
            RunEvent("unstable_reactor", "Unstable Reactor", "A humming reactor offers raw power, but the boss feeds on the surge.", 1, 3,
                RunOpt("Absorb the surge", "Gain a Rare+ skill. The boss gains +15% ATK.", RunOut(1, "Power floods in.", RunSkillFx(Rarity.Rare), RunFx(RunEffectKind.BossStatPct, 15))),
                RunOpt("Walk away", "Nothing happens.", RunNothing));
            RunEvent("smuggler", "Smuggler", "A shady alien smuggler trades rare techniques for a piece of your vitality.", 1, 3,
                RunOpt("Trade vitality", "-10% max HP for an Epic+ skill.", RunOut(1, "A painful bargain.", RunStatFx(StatId.Hp, -10), RunSkillFx(Rarity.Epic))),
                RunOpt("Decline", "Nothing happens.", RunNothing));
            var echo = RunOpt("Fight the echo", "Battle a strong elite (+30%). Win: a part (account loot, paid on clear).",
                RunOut(1, "The echo fades, leaving a part behind.", RunFx(RunEffectKind.Battle, 30, "elite_rift_echo"), RunRewardFx(new Reward(RewardKinds.Part, "*", 1, 2))));
            RunEvent("rift_echo", "Rift Echo", "An echo of a fearsome foe flickers in a rift.", 1, 2.5, echo, RunOpt("Leave it", "Nothing happens.", RunNothing));
            RunEvent("abandoned_cache", "Abandoned Cache", "A half-buried supply cache. Something ticks inside.", 1, 3,
                RunOpt("Search it", "65%: 1 account loot item (paid on clear, +Danger) and the alarm wakes the boss (+8). 35%: trapped — lose 20% HP and 5% max HP for the run.",
                    RunOut(65, "Loot! An alarm wails in the distance.", RunFx(RunEffectKind.Loot, 1), RunFx(RunEffectKind.Meter, 8)),
                    RunOut(35, "Boom! It was trapped.", RunFx(RunEffectKind.DamagePct, 20), RunStatFx(StatId.Hp, -5))),
                RunOpt("Leave it", "Nothing happens.", RunNothing));
            RunEvent("strange_spores", "Strange Spores", "Glowing spores drift by. They smell... edible.", 1, 3,
                RunOpt("Eat them", "50%: +8% ATK. 50%: lose 10% HP and -5% DEF.",
                    RunOut(50, "Delicious power.", RunStatFx(StatId.Atk, 8)), RunOut(50, "Bad spores.", RunFx(RunEffectKind.DamagePct, 10), RunStatFx(StatId.Def, -5))),
                RunOpt("Ignore them", "Nothing happens.", RunNothing));
            var merchant = RunOpt("Buy a tonic (40 coins)", "Heal 30% HP.", RunOut(1, "Refreshing.", RunFx(RunEffectKind.HealPct, 30)));
            merchant.CoinCost = 40;
            RunEvent("alien_medic", "Alien Medic", "A friendly alien medic offers a tonic.", 1, 3, merchant, RunOpt("No thanks", "Nothing happens.", RunNothing));
            var altar = RunOpt("Offer 2 portal shards", "Gain an Epic+ skill.", RunOut(1, "The altar glows.", RunSkillFx(Rarity.Epic)));
            altar.ShardCost = 2;
            RunEvent("shard_altar", "Shard Altar", "An altar hums with portal energy.", 2, 3, altar,
                RunOpt("Pray", "Calm the boss: awakening -15.", RunOut(1, "A calm settles.", RunFx(RunEffectKind.Meter, -15))),
                RunOpt("Leave", "Nothing happens.", RunNothing));
            RunEvent("meteor_shower", "Meteor Shower", "Meteors streak across the sky!", 0, 2,
                RunOpt("Take shelter", "Lose 30 coins of time.", RunOut(1, "You wait it out.", RunFx(RunEffectKind.Coins, -30))),
                RunOpt("Run through it", "60%: unharmed with a shard. 40%: lose 20% HP.",
                    RunOut(60, "You dodge every rock and grab a shard.", RunFx(RunEffectKind.Shards, 1)), RunOut(40, "Ouch!", RunFx(RunEffectKind.DamagePct, 20))));
            RunEvent("lost_explorer", "Lost Explorer", "A lost explorer worm asks for help.", 1, 3,
                RunOpt("Help them", "Lose 10% HP; gain XP and coins.", RunOut(1, "Grateful, they share their supplies.", RunFx(RunEffectKind.DamagePct, 10), RunFx(RunEffectKind.Xp, 20), RunFx(RunEffectKind.Coins, 60))),
                RunOpt("Ignore them", "Nothing happens.", RunNothing));
            RunEvent("gravity_well", "Gravity Well", "A swirling gravity well pulls loot from deep space.", 1, 2,
                RunOpt("Dive in", "2 account loot items (paid on clear, +Danger). The boss awakens 15% faster for the rest of the run.",
                    RunOut(1, "You surface with treasure — and the boss stirs.", RunFx(RunEffectKind.Loot, 2), RunFx(RunEffectKind.AwakenRatePct, 15))),
                RunOpt("Steer away", "Nothing happens.", RunNothing));
            RunEvent("echoing_shrine", "Echoing Shrine", "Ancient echoes can sharpen a technique.", 1, 2.5,
                RunOpt("Meditate", "Upgrade a skill; awakening +10.", RunOut(1, "Your technique evolves.", RunFx(RunEffectKind.UpgradeSkill, 1), RunFx(RunEffectKind.Meter, 10))),
                RunOpt("Leave", "Nothing happens.", RunNothing));
            var patch = RunOpt("Patch the leak (40 coins)", "Pay to stay safe.", RunOut(1, "Fixed.", RunFx(RunEffectKind.Coins, 0)));
            patch.CoinCost = 40;
            var fuel = new EventDef { Id = "fuel_leak", Name = "Fuel Leak", Text = "Your mount is leaking fuel.", DefaultOption = 1, Negative = true };
            fuel.Options.Add(patch);
            fuel.Options.Add(RunOpt("Ignore it", "-10% Speed for the rest of the run.", RunOut(1, "You sputter on.", RunStatFx(StatId.Speed, -10))));
            RunEvents[fuel.Id] = fuel;
            RandomRunEvents.Add(new WeightedId(fuel.Id, 2));
            RunEvent("wild_beast", "Wild Beast", "A wild beast eyes you curiously.", 1, 2.5,
                RunOpt("Try to tame it", "50%: +5% ATK and 1 account loot item (paid on clear). 50%: it attacks (elite fight).",
                    RunOut(50, "It follows you loyally.", RunStatFx(StatId.Atk, 5), RunFx(RunEffectKind.Loot, 1)),
                    RunOut(50, "It attacks!", RunFx(RunEffectKind.Battle, 10, "elite"))),
                RunOpt("Back off", "Nothing happens.", RunNothing));
            var bm = RunOpt("Buy a signal beacon (100 coins)", "A blueprint token (account loot, paid on clear). The smugglers' toxins cost 8% max HP for the run.",
                RunOut(1, "Sold. You feel a little weaker.", RunRewardFx(Reward.Cur(Currencies.BlueprintTokens, 1)), RunStatFx(StatId.Hp, -8)));
            bm.CoinCost = 100;
            RunEvent("black_market", "Black Market", "A hidden market trades in rare signals.", 1, 2, bm, RunOpt("Leave", "Nothing happens.", RunNothing));
            RunEvent("cursed_relic", "Cursed Relic", "A relic radiates power and malice.", 1, 2,
                RunOpt("Claim it", "Gain a Legendary+ skill. -15% max HP and awakening +20.", RunOut(1, "Power at a price.", RunSkillFx(Rarity.Legendary), RunStatFx(StatId.Hp, -15), RunFx(RunEffectKind.Meter, 20))),
                RunOpt("Leave it", "Nothing happens.", RunNothing));
            RunEvent("training_dummy", "Training Grounds", "Old training machines still work.", 0, 2.5,
                RunOpt("Train", "Gain 25 XP.", RunOut(1, "You feel sharper.", RunFx(RunEffectKind.Xp, 25))),
                RunOpt("Spar the champion", "Battle an elite for +6% ATK.", RunOut(1, "Champion beaten!", RunFx(RunEffectKind.Battle, 0, "elite"), RunStatFx(StatId.Atk, 6))));
            RunEvent("boss_scout", "Boss Scout", "One of the boss's scouts is watching you.", 0, 2,
                RunOpt("Chase it down", "Battle; awakening -20 on win.", RunOut(1, "The scout is silenced.", RunFx(RunEffectKind.Battle, 0, "enemy"), RunFx(RunEffectKind.Meter, -20))),
                RunOpt("Let it go", "Awakening +10.", RunOut(1, "The scout reports back.", RunFx(RunEffectKind.Meter, 10))));

            // Tile-bound events
            RunEvent("treasure_pod", "Treasure Pod", "A sealed treasure pod, guarded by an elite. Opening it is loud.", 1, 0,
                RunOpt("Fight the guardian", "Elite fight. Win: 2 account loot items (paid on clear, each +Danger); nearby enemies grow stronger.",
                    RunOut(1, "The guardian falls. Loot spills out.", RunFx(RunEffectKind.Battle, 10, "elite"), RunFx(RunEffectKind.Loot, 2), RunFx(RunEffectKind.StrengthenEnemies, 10))),
                RunOpt("Leave", "Nothing happens.", RunNothing));
            RunEvent("campfire", "Campfire", "A warm campfire. Its light draws nearby enemies.", 0, 0,
                RunOpt("Rest", "Heal 35% HP. Nearby enemies grow stronger.", RunOut(1, "You rest.", RunFx(RunEffectKind.HealPct, 35), RunFx(RunEffectKind.StrengthenEnemies, 12))),
                RunOpt("Forge", "Upgrade a skill. Nearby enemies grow stronger.", RunOut(1, "You hone a technique.", RunFx(RunEffectKind.UpgradeSkill, 1), RunFx(RunEffectKind.StrengthenEnemies, 12))));
        }

        // ---- shops & loot ---------------------------------------------------------------------

        private void RunBuildShopsAndLoot()
        {
            var std = new ShopTableDef { Id = "shop_standard" };
            std.RarityWeights[Rarity.Common] = 40; std.RarityWeights[Rarity.Rare] = 35; std.RarityWeights[Rarity.Epic] = 18;
            std.RarityWeights[Rarity.Legendary] = 6; std.RarityWeights[Rarity.Mythic] = 1;
            std.SkillPrices[Rarity.Common] = 25; std.SkillPrices[Rarity.Rare] = 45; std.SkillPrices[Rarity.Epic] = 80;
            std.SkillPrices[Rarity.Legendary] = 140; std.SkillPrices[Rarity.Mythic] = 220;
            std.SkillOffers = 4; std.StatOfferCount = 2;
            std.StatOffers.Add(new StatMod(StatId.Atk, 0, 8));
            std.StatOffers.Add(new StatMod(StatId.Hp, 0, 10));
            std.StatOffers.Add(new StatMod(StatId.Def, 0, 10));
            std.StatOffers.Add(new StatMod(StatId.CritChance, 5));
            std.StatOffers.Add(new StatMod(StatId.Speed, 0, 8));
            std.StatOffers.Add(new StatMod(StatId.LifeSteal, 4));
            std.StatOffers.Add(new StatMod(StatId.DamageReduction, 4));
            RunShopTables[std.Id] = std;

            var dark = new ShopTableDef { Id = "shop_dark", HealPct = 25, HealPrice = 45, StatPrice = 60, RerollBase = 20, RerollStep = 15, SkillOffers = std.SkillOffers, StatOfferCount = std.StatOfferCount };
            foreach (var kv in std.RarityWeights) dark.RarityWeights[kv.Key] = kv.Value;
            dark.RarityWeights[Rarity.Legendary] = 10; dark.RarityWeights[Rarity.Mythic] = 3;
            foreach (var kv in std.SkillPrices) dark.SkillPrices[kv.Key] = kv.Value * 3 / 2;
            dark.StatOffers.AddRange(std.StatOffers);
            RunShopTables[dark.Id] = dark;

            // Loot tables: every system is fed by runs (currencies, parts/runes, rare blueprint/alien drops).
            // Item drops use Id="*" = "random def of the given Rarity" (see skill-conventions.md).
            for (int p = 1; p <= Campaign.Portals; p++)
            {
                var t = CampaignPortalLoot(p - 1);
                RunLootTables[t.Id] = t;
            }
            var burrow = new LootTableDef { Id = "loot_burrow" };
            burrow.Entries.AddRange(RunLootTables["loot_p2"].Entries);
            burrow.Entries.Add(new LootEntry(2, RewardKinds.Currency, Currencies.AdvancementPlates, 1, 2));
            burrow.Entries.Add(new LootEntry(2, RewardKinds.Part, "*", 1, 1, 2));
            RunLootTables[burrow.Id] = burrow;
            var darkLoot = new LootTableDef { Id = "loot_dark" };
            darkLoot.Entries.AddRange(RunLootTables["loot_p3"].Entries);
            darkLoot.Entries.Add(new LootEntry(5, RewardKinds.Currency, Currencies.Diamonds, 10, 30));
            darkLoot.Entries.Add(new LootEntry(3, RewardKinds.Part, "*", 1, 1, 3)); // Legendary: the Mythic+ fodder
            darkLoot.Entries.Add(new LootEntry(1, RewardKinds.Blueprint, "*", 1, 1, 2));
            darkLoot.Entries.Add(new LootEntry(1, RewardKinds.Alien, "*", 1, 1, 2));
            RunLootTables[darkLoot.Id] = darkLoot;
        }

        // ---- portals & levels -----------------------------------------------------------------

        private void RunBuildPortals()
        {
            // Main campaign: biome templates cycle through the four enemy families; the first Campaign.Portals portals are
            // materialized here, every later one is generated on demand by the same code (ContentDb.Campaign.cs).
            for (int i = 0; i < Campaign.Portals; i++)
            {
                var levels = new List<LevelDef>();
                var portal = CampaignBuildMainPortal(i, levels);
                Portals[portal.Id] = portal;
                foreach (var l in levels) RunLevels[l.Id] = l;
            }

            // Burrow: multi-floor dive (floors = levels), entry costs a Burrow Key, cash out between floors.
            var burrow = new PortalDef { Id = "the_burrow", Name = "The Burrow", Kind = PortalKind.Burrow, Order = Campaign.SidePortalOrder, EntryCurrency = Currencies.BurrowKeys, EntryCost = 1 };
            for (int f = 1; f <= 10; f++)
            {
                var l = RunStoreLevel("the_burrow_" + f, "Burrow Floor " + f, burrow.Id, f, 4 + (f - 1) * 2, "pool_burrow", "boss_void_maw", "loot_burrow",
                    new[] { "burrow_grub", "tunnel_stalker", "void_spawn" }, new[] { "elite_burrow_horror" });
                l.FixedTiles.RemoveAll(ft => ft.TileId == "shop" && ft.At > 0.8); // short floors: one shop each
                l.BoardLength = 18; l.MaxRolls = 6; // short floors: the boss comes at the roll limit l.StaminaCost = 0; l.CompletionLootRolls = 1;
                l.CompletionRewards.Add(Reward.Cur(Currencies.GearCores, 3 + f));
                l.CompletionRewards.Add(Reward.Cur(Currencies.AdvancementPlates, f >= 5 ? 1 : 0));
                // Deep floors feed the endgame sinks: Mount Cores (mount awakening); the two deepest floors have a small
                // chance of an Awaken Core (rare drop, paid unscaled on cash-out).
                l.CompletionRewards.Add(Reward.Cur(Currencies.MountCores, f >= 6 ? 1 : 0));
                l.CompletionRewards.RemoveAll(r => r.Amount <= 0);
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
