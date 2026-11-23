using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    // Movement, tiles, battles, events, shops, awakening, completion.
    public static partial class RunSystem
    {
        /// <summary>Previous level of the same portal (endless portals included), or null for its first level.</summary>
        private static string PrevLevelId(ContentDb c, PortalDef portal, LevelDef level)
        {
            if (level.Index <= 1) return null;
            if (portal.Kind == PortalKind.Main) return c.MainLevelId(level.Number - 1);
            return c.PortalLevel(portal, level.Index - 1)?.Id;
        }

        private static LevelDef Level(ContentDb c, ActiveRun run) => c.RunLevel(run.LevelId);

        private static BossDef Boss(ContentDb c, ActiveRun run) =>
            run.BossId != null && c.RunBosses.TryGetValue(run.BossId, out var b) ? b : null;

        private static TileDef TileDefOf(ContentDb c, RunTile t) =>
            c.BoardTiles.TryGetValue(t.TileId, out var d) ? d : c.BoardTiles["empty"];

        // ---- movement -------------------------------------------------------------------------

        /// <summary>Advance the run until it needs a player decision (roll, branch, event, draft, shop, defeat...).</summary>
        private static void Continue(CommandContext ctx, ActiveRun run)
        {
            var c = ctx.Content;
            for (int guard = 0; guard < c.RunTuning.MaxStepsGuard; guard++)
            {
                if (ctx.State.Run.Active != run || run.Phase != RunPhase.AwaitRoll) return;
                if (run.PendingDrafts > 0) { OpenDraft(ctx, run); continue; }
                if (run.StepsLeft > 0)
                {
                    var tile = run.Tiles[run.Position];
                    if (tile.Next.Count == 0) { run.StepsLeft = 0; continue; }
                    int next;
                    if (tile.Next.Count > 1)
                    {
                        if (run.ChosenNext < 0)
                        {
                            run.Phase = RunPhase.ChooseBranch;
                            run.BranchOptions.Clear();
                            run.BranchOptions.AddRange(tile.Next);
                            return;
                        }
                        next = run.ChosenNext;
                        run.ChosenNext = -1;
                    }
                    else next = tile.Next[0];
                    run.Position = next;
                    run.Trail.Add(next);
                    run.StepsLeft--;
                    var nt = run.Tiles[next];
                    var def = TileDefOf(c, nt);
                    if (def.Category == TileCategory.BossGate)
                    {
                        // The board is a circuit: passing the gate starts the next lap (the boss is woken by its meter).
                        run.Laps++;
                        ctx.Emit("run_lap", run.Laps.ToString());
                    }
                    if (run.StepsLeft > 0)
                    {
                        PassTile(ctx, run, nt, def);
                        if (!def.ForceStop) continue;
                        run.StepsLeft = 0;
                    }
                    LandTile(ctx, run, nt, def);
                    continue;
                }
                // The awakening meter is the run's clock: once it is full (or at the roll limit) the boss comes after the move.
                if (!run.BossFought && (run.Rolls >= run.MaxRolls || BossAwake(c, run))) { StartBoss(ctx, run); continue; }
                return;
            }
        }

        private static void PassTile(CommandContext ctx, ActiveRun run, RunTile tile, TileDef def)
        {
            RunTriggers(ctx, run, TriggerKind.OnTilePass, def);
            if (!def.TriggersOnPass) return;
            ResolvePickup(ctx, run, tile, def);
        }

        private static void ResolvePickup(CommandContext ctx, ActiveRun run, RunTile tile, TileDef def)
        {
            if (def.Category == TileCategory.Shard) run.Shards++;
            if (def.Category == TileCategory.Pickup)
            {
                run.Coins += def.Coins;
                if (def.Value > 0) AddXp(ctx, run, (int)def.Value); // supply caches: run XP
            }
            Consume(ctx.Content, run, tile, def);
        }

        private static bool IsNegative(TileDef def) => def.Valence == TileValence.Negative;

        private static bool TrySkipNegative(CommandContext ctx, ActiveRun run)
        {
            if (run.SkipNegativeEvents <= 0) return false;
            run.SkipNegativeEvents--;
            ctx.Emit("run_negative_skipped", run.SkipNegativeEvents.ToString());
            return true;
        }

        private static void LandTile(CommandContext ctx, ActiveRun run, RunTile tile, TileDef def)
        {
            var c = ctx.Content;
            tile.TimesResolved++;
            ctx.Emit("run_land", tile.Id.ToString(), tile.TileId);
            RunTriggers(ctx, run, TriggerKind.OnTileLand, def);
            if (IsNegative(def) && TrySkipNegative(ctx, run)) { Consume(c, run, tile, def); return; }
            switch (def.Category)
            {
                case TileCategory.Enemy:
                case TileCategory.Elite:
                case TileCategory.Miniboss:
                {
                    string kind = def.Category == TileCategory.Enemy ? "enemy" : (def.Category == TileCategory.Elite ? "elite" : "miniboss");
                    var enemies = EnemiesFor(c, run, kind, null, Math.Max(1, def.EnemyLevel), tile.BonusPct);
                    RewardsFor(c, enemies, Math.Max(1, def.EnemyLevel), out int xp, out long coins);
                    if (Fight(ctx, run, enemies, kind, xp, coins, null)) AfterWin(ctx, run, kind, null);
                    break;
                }
                case TileCategory.StatBuff:
                case TileCategory.StatDebuff:
                    run.RunStats.Add(new StatMod(def.Stat, 0, def.Value));
                    RefreshMaxHp(c, run);
                    Consume(c, run, tile, def);
                    break;
                case TileCategory.Loot:
                    AddLoot(c, run, Math.Max(1, (int)def.Value));
                    Consume(c, run, tile, def);
                    break;
                case TileCategory.Treasure:
                case TileCategory.Campfire:
                case TileCategory.Event:
                    OpenEvent(ctx, run, def.EventId ?? PickRandomEvent(c, run), tile);
                    break;
                case TileCategory.Shop:
                    OpenShop(ctx, run);
                    break;
                case TileCategory.Minigame:
                    OpenMinigame(ctx, run, tile);
                    break;
                case TileCategory.Hazard:
                    if (def.Value > 0) DamageWorm(run, def.Value);
                    if (def.Coins > 0) run.Coins = Math.Max(0, run.Coins - def.Coins);
                    if (def.PushBack > 0)
                    {
                        int back = Math.Min(def.PushBack, run.Trail.Count - 1);
                        for (int i = 0; i < back; i++) run.Trail.RemoveAt(run.Trail.Count - 1);
                        run.Position = run.Trail[run.Trail.Count - 1];
                        run.StepsLeft = 0;
                    }
                    Consume(c, run, tile, def);
                    break;
                case TileCategory.Shard:
                case TileCategory.Pickup:
                    ResolvePickup(ctx, run, tile, def);
                    break;
                case TileCategory.BossTile:
                    AddAwakening(ctx, run, def.MeterDelta);
                    run.BossStatPct += def.BossStatPct;
                    run.Coins += def.Coins;
                    Consume(c, run, tile, def);
                    break;
            }
        }

        /// <summary>Consumed tiles are replaced from the pool ("once" tiles become empty).</summary>
        private static void Consume(ContentDb c, ActiveRun run, RunTile tile, TileDef def)
        {
            if (!def.Consumable) return;
            tile.Fixed = false;
            tile.BonusPct = 0;
            if (def.Once) { tile.TileId = "empty"; return; }
            var usedOnce = new HashSet<string>();
            foreach (var t in run.Tiles)
                if (c.BoardTiles.TryGetValue(t.TileId, out var d) && d.Once) usedOnce.Add(t.TileId);
            tile.TileId = RunBoard.PickPoolTile(c, Level(c, run).TilePoolId, run.Rng, usedOnce, run);
        }

        // ---- awakening ------------------------------------------------------------------------

        /// <summary>The boss's meter is full: it comes as soon as the current move has resolved.</summary>
        private static bool BossAwake(ContentDb c, ActiveRun run) => Boss(c, run) != null && run.Awakening >= 100 - 1e-9;

        /// <summary>Meter points the next roll adds (the boss's base pace, skills, buildings and level modifiers applied).</summary>
        public static double AwakeningPerRoll(ContentDb c, ActiveRun run)
        {
            var boss = Boss(c, run);
            return boss == null ? 0 : AwakeningScaled(c, run, boss.AwakenPerRoll);
        }

        private static double AwakeningScaled(ContentDb c, ActiveRun run, double amount)
        {
            var mods = Level(c, run).Modifiers;
            return amount * Math.Max(0.1, 1 - Math.Min(90, run.AwakeningReductionPct) / 100.0) * (1 + mods.AwakeningPct / 100.0);
        }

        /// <summary>
        /// The roll after which the boss is expected (HUD "Roll n/N"): the meter's remaining points at the current pace, at most
        /// the level's roll limit. Boss tiles, events and skills move it while the run goes on.
        /// </summary>
        public static int BossRollEstimate(ContentDb c, ActiveRun run)
        {
            if (run.BossFought || Boss(c, run) == null) return Math.Max(run.Rolls, Math.Min(run.MaxRolls, run.Rolls));
            double per = AwakeningPerRoll(c, run);
            double left = Math.Max(0, 100 - run.Awakening);
            int more = per <= 0 ? run.MaxRolls : (int)Math.Ceiling(left / per - 1e-9);
            return Math.Min(run.MaxRolls, run.Rolls + more);
        }

        /// <summary>Adds (or drains, if negative) boss awakening and fires threshold actions.</summary>
        public static void AddAwakening(CommandContext ctx, ActiveRun run, double amount)
        {
            var c = ctx.Content;
            var boss = Boss(c, run);
            if (boss == null || amount == 0) return;
            if (amount > 0) amount = AwakeningScaled(c, run, amount);
            run.Awakening = CombatMath.Clamp(run.Awakening + amount, 0, 100);
            while (run.ActionsTriggered < boss.Actions.Count && run.Awakening >= boss.Actions[run.ActionsTriggered].AtMeter)
            {
                var a = boss.Actions[run.ActionsTriggered];
                run.ActionsTriggered++;
                ApplyBossAction(c, run, a);
                ctx.Emit("boss_awaken", a.AtMeter.ToString(), a.Kind.ToString());
                ctx.State.Count("run_boss_awakenings");
                RunTriggers(ctx, run, TriggerKind.OnBossAwaken, null);
            }
        }

        private static void ApplyBossAction(ContentDb c, ActiveRun run, BossAwakenAction a)
        {
            switch (a.Kind)
            {
                case BossActionKind.BuffEnemyTiles:
                    foreach (var t in run.Tiles)
                    {
                        var d = TileDefOf(c, t);
                        if (d.Category == TileCategory.Enemy || d.Category == TileCategory.Elite || d.Category == TileCategory.Miniboss) t.BonusPct += a.Value;
                    }
                    break;
                case BossActionKind.TransformTiles:
                case BossActionKind.Summon:
                {
                    var cands = new List<RunTile>();
                    foreach (var t in run.Tiles)
                    {
                        if (t.Fixed || t.Id == run.Position) continue;
                        var cat = TileDefOf(c, t).Category;
                        if (cat == TileCategory.Start || cat == TileCategory.BossGate || cat == TileCategory.Shop || cat == TileCategory.BossTile) continue;
                        cands.Add(t);
                    }
                    for (int i = 0; i < a.Count && cands.Count > 0; i++)
                    {
                        int k = run.Rng.Range(0, cands.Count);
                        var t = cands[k];
                        cands.RemoveAt(k);
                        t.TileId = a.Param ?? "elite";
                        if (a.Kind == BossActionKind.Summon) t.BonusPct += a.Value;
                    }
                    break;
                }
                case BossActionKind.BuffBoss:
                    run.BossStatPct += a.Value;
                    break;
            }
        }

        // ---- battles --------------------------------------------------------------------------

        private static List<StatMod> WormExtraStats(ContentDb c, ActiveRun run)
        {
            var list = new List<StatMod>(run.RunStats);
            var level = Level(c, run);
            if (level != null && level.Modifiers.HealPct != 0) list.Add(new StatMod(StatId.HealPower, level.Modifiers.HealPct));
            return list;
        }

        /// <summary>Enemy stat bonus % from carried loot (Danger level).</summary>
        public static double DangerPct(ContentDb c, ActiveRun run) =>
            run.Danger * c.RunTuning.DangerPctPerLoot * (1 + Level(c, run).Modifiers.DangerPct / 100.0);

        /// <summary>Enemy specs for a fight: kind = enemy | elite | miniboss | boss | an enemy id.</summary>
        private static List<CombatantSpec> EnemiesFor(ContentDb c, ActiveRun run, string kind, string enemyId, int tileLevel, double bonusPct)
        {
            var t = c.RunTuning;
            var level = Level(c, run);
            var mods = level.Modifiers;
            double scale = c.ThreatMultiplier(run.Threat);
            double common = DangerPct(c, run) + bonusPct + t.BoardEnemyPct;
            var list = new List<CombatantSpec>();
            if (enemyId != null)
            {
                list.Add(BattleSimulator.BuildEnemy(c, enemyId, scale, common + mods.EnemyAtkPct, common + mods.EnemyHpPct, mods.EnemyDefPct));
                return list;
            }
            switch (kind)
            {
                case "elite":
                {
                    string id = level.ElitePool[run.Rng.Range(0, level.ElitePool.Count)];
                    double b = common + t.EliteBonusPct;
                    list.Add(BattleSimulator.BuildEnemy(c, id, scale, b + mods.EnemyAtkPct, b + mods.EnemyHpPct, mods.EnemyDefPct));
                    string add = level.EnemyPool[run.Rng.Range(0, level.EnemyPool.Count)];
                    list.Add(BattleSimulator.BuildEnemy(c, add, scale, common + mods.EnemyAtkPct, common + mods.EnemyHpPct, mods.EnemyDefPct));
                    break;
                }
                case "miniboss":
                {
                    double b = common + t.MinibossBonusPct;
                    list.Add(BattleSimulator.BuildEnemy(c, level.MinibossId, scale, b + mods.EnemyAtkPct, b + mods.EnemyHpPct, mods.EnemyDefPct));
                    break;
                }
                default:
                {
                    int lv = Math.Max(1, Math.Min(tileLevel, t.EnemyLevelCount.Count - 1));
                    double b = common + t.EnemyLevelBonusPct[lv];
                    for (int i = 0; i < t.EnemyLevelCount[lv]; i++)
                    {
                        string id = level.EnemyPool[run.Rng.Range(0, level.EnemyPool.Count)];
                        list.Add(BattleSimulator.BuildEnemy(c, id, scale, b + mods.EnemyAtkPct, b + mods.EnemyHpPct, mods.EnemyDefPct));
                    }
                    break;
                }
            }
            return list;
        }

        private static void RewardsFor(ContentDb c, List<CombatantSpec> enemies, int tileLevel, out int xp, out long coins)
        {
            xp = 0; coins = c.RunTuning.CoinsPerEnemyLevel * tileLevel;
            foreach (var e in enemies)
                if (c.Enemies.TryGetValue(e.Id, out var d)) { xp += d.Xp; coins += d.Coins; }
        }

        /// <summary>Runs a battle with the run's worm. On loss the run enters Defeated (revive or give up).</summary>
        private static bool Fight(CommandContext ctx, ActiveRun run, List<CombatantSpec> enemies, string kind, int xp, long coins, List<RunEffect> followUp)
        {
            var c = ctx.Content;
            var setup = new BattleSetup { RoundCap = kind == "boss" ? c.Combat.BossRoundCap : c.Combat.RoundCap };
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(run.Loadout, c, run.Skills, WormExtraStats(c, run)));
            setup.Player[setup.Player.Count - 1].StartHp = run.WormHp;
            setup.Enemy.AddRange(enemies);
            var result = BattleSimulator.Simulate(setup, c, run.Rng.Fork("battle:" + run.BattleCount));
            run.BattleCount++;
            run.LastBattle = result;
            run.WormHp = result.WormHpLeft;
            run.WormMaxHp = result.WormMaxHp;
            if (kind == "boss") run.BossRounds = result.Rounds;
            ctx.Emit("run_battle", kind, result.Winner.ToString(), result.Rounds.ToString());
            if (result.PlayerWon())
            {
                OnBattleWin(ctx, run, kind, xp, coins);
                return true;
            }
            run.Phase = RunPhase.Defeated;
            run.DefeatKind = kind;
            run.DefeatXp = xp;
            run.DefeatCoins = coins;
            run.DefeatEnemies.Clear();
            int ei = 0;
            foreach (var f in result.Fighters)
            {
                if (f.Side != BattleSide.Enemy || f.SummonedBy >= 0) continue;
                var spec = enemies[ei++];
                if (!f.Alive) continue;
                spec.StartHp = f.EndHp;
                spec.StartShield = 0;
                run.DefeatEnemies.Add(spec);
            }
            run.DefeatFollowUp.Clear();
            if (followUp != null) run.DefeatFollowUp.AddRange(followUp);
            ctx.Emit("run_defeated", kind);
            return false;
        }

        private static void OnBattleWin(CommandContext ctx, ActiveRun run, string kind, int xp, long coins)
        {
            var c = ctx.Content;
            run.BattlesWon++;
            ctx.State.Count("run_battles_won");
            if (kind == "elite" || kind == "miniboss") ctx.State.Count("run_elites_killed");
            run.Coins += coins;
            HealWorm(c, run, c.RunTuning.HealAfterBattlePct);
            RunTriggers(ctx, run, TriggerKind.OnBattleWon, null);
            double xpGain = BattleSimulator.FinalStats(WormSpec(c, run), c).Value(StatId.XpGain);
            AddXp(ctx, run, (int)Math.Round(xp * (1 + xpGain / 100.0)));
        }

        /// <summary>Post-win continuation: consume the battle tile, finish the boss, or resume event effects.</summary>
        private static void AfterWin(CommandContext ctx, ActiveRun run, string kind, List<RunEffect> followUp)
        {
            if (kind == "boss") { BossDefeated(ctx, run); return; }
            if (kind == "enemy" || kind == "elite" || kind == "miniboss")
            {
                var tile = run.Tiles[run.Position];
                Consume(ctx.Content, run, tile, TileDefOf(ctx.Content, tile));
            }
            if (followUp != null && followUp.Count > 0) ApplyEffects(ctx, run, followUp, run.Position);
        }

        private static void StartBoss(CommandContext ctx, ActiveRun run)
        {
            var c = ctx.Content;
            run.BossFought = true;
            var boss = Boss(c, run);
            if (boss == null) { BossDefeated(ctx, run); return; }
            var level = Level(c, run);
            var mods = level.Modifiers;
            double scale = c.ThreatMultiplier(run.Threat);
            double common = DangerPct(c, run);
            double bossPct = common + run.BossStatPct;
            var enemies = new List<CombatantSpec>
            {
                BattleSimulator.BuildEnemy(c, boss.EnemyId, scale, bossPct + mods.EnemyAtkPct, bossPct + mods.EnemyHpPct, mods.EnemyDefPct),
            };
            foreach (var esc in boss.Escorts)
                enemies.Add(BattleSimulator.BuildEnemy(c, esc, scale, common + mods.EnemyAtkPct, common + mods.EnemyHpPct, mods.EnemyDefPct));
            RewardsFor(c, enemies, 3, out int xp, out long coins);
            ctx.Emit("run_boss_fight", boss.Id);
            if (Fight(ctx, run, enemies, "boss", xp, coins, null)) AfterWin(ctx, run, "boss", null);
        }

        private static void BossDefeated(CommandContext ctx, ActiveRun run)
        {
            var c = ctx.Content;
            ctx.State.Count("run_bosses_killed");
            run.PendingDrafts = 0; // the run ends; no more drafts
            if (run.Mode == RunMode.Burrow)
            {
                var level = Level(c, run);
                foreach (var r in level.CompletionRewards) run.PendingLoot.Add(new Reward(r.Kind, r.Id, r.Amount, r.Rarity));
                for (int i = 0; i < level.CompletionLootRolls; i++) RollLoot(c, run, false);
                RollRareDrops(level, run);
                ctx.State.Count("burrow_floors_cleared");
                var p = GetProgress(c, ctx.State, run.LevelId);
                p.Cleared = true;
                var portal = c.Portal(run.PortalId);
                var first = GetProgress(c, ctx.State, portal.LevelIds[0]);
                first.BestFloor = Math.Max(first.BestFloor, run.Floor);
                int idx = portal.LevelIds.IndexOf(run.LevelId);
                if (idx + 1 < portal.LevelIds.Count) { run.Phase = RunPhase.BurrowChoice; return; }
            }
            Finish(ctx, run, true);
        }

        // ---- xp, loot, heal -------------------------------------------------------------------

        private static void AddXp(CommandContext ctx, ActiveRun run, int amount)
        {
            var t = ctx.Content.RunTuning;
            if (amount <= 0) return;
            run.Xp += amount;
            run.XpTotal += amount;
            for (int guard = 0; guard < 100; guard++)
            {
                int need = t.XpBase + t.XpPerLevel * (run.Level - 1);
                if (run.Xp < need) break;
                run.Xp -= need;
                run.Level++;
                run.PendingDrafts++;
                ctx.Emit("run_level_up", run.Level.ToString());
                RunTriggers(ctx, run, TriggerKind.OnLevelUp, null);
            }
        }

        /// <summary>
        /// Daily Biomass surge (RunTuning.DailyBiomassBonusRuns / Mult): the first N won Normal/Overcharged runs of the
        /// reset-day pay their Biomass ×Mult. Returns the Biomass multiplier for this (won) run.
        /// </summary>
        private static double BiomassSurge(CommandContext ctx, ActiveRun run, RunSummary summary)
        {
            var t = ctx.Content.RunTuning; var rs = ctx.State.Run;
            if (t.DailyBiomassBonusRuns <= 0 || (run.Mode != RunMode.Normal && run.Mode != RunMode.Overcharged)) return 1;
            long day = GameClock.DayIndex(ctx.Content, ctx.NowMs);
            if (rs.BiomassBonusDay != day) { rs.BiomassBonusDay = day; rs.BiomassBonusRuns = 0; }
            if (rs.BiomassBonusRuns >= t.DailyBiomassBonusRuns) return 1;
            rs.BiomassBonusRuns++;
            summary.BiomassBonus = true;
            return t.DailyBiomassBonusMult;
        }

        /// <summary>Daily Biomass surge runs left today.</summary>
        public static int BiomassBonusRunsLeft(GameState s, ContentDb c, long nowMs) =>
            s.Run.BiomassBonusDay == GameClock.DayIndex(c, nowMs) ? Math.Max(0, c.RunTuning.DailyBiomassBonusRuns - s.Run.BiomassBonusRuns) : c.RunTuning.DailyBiomassBonusRuns;

        /// <summary>Rolls the level's rare drops once (when the level / Burrow floor is beaten) into PendingRare.</summary>
        private static void RollRareDrops(LevelDef level, ActiveRun run)
        {
            foreach (var d in level.RareDrops)
                if (d.Reward != null && run.Rng.NextDouble() * 100 < d.ChancePct)
                    run.PendingRare.Add(new Reward(d.Reward.Kind, d.Reward.Id, d.Reward.Amount, d.Reward.Rarity));
        }

        private static void RollLoot(ContentDb c, ActiveRun run, bool raisesDanger)
        {
            var level = Level(c, run);
            var table = c.RunLootTable(level.LootTableId);
            if (table == null || table.Entries.Count == 0) return;
            var weights = new double[table.Entries.Count];
            for (int i = 0; i < weights.Length; i++) weights[i] = table.Entries[i].Weight;
            int idx = run.Rng.PickWeighted(weights);
            if (idx < 0) return;
            var e = table.Entries[idx];
            long amount = e.Max > e.Min ? e.Min + run.Rng.Range(0, (int)(e.Max - e.Min + 1)) : e.Min;
            run.PendingLoot.Add(new Reward(e.Kind, e.Id, amount, e.Rarity));
            if (raisesDanger) run.Danger++;
        }

        /// <summary>Adds loot items to the pending loot; each carried item raises Danger.</summary>
        private static void AddLoot(ContentDb c, ActiveRun run, int count)
        {
            for (int i = 0; i < count; i++) RollLoot(c, run, true);
        }

        private static void RefreshMaxHp(ContentDb c, ActiveRun run)
        {
            double max = Math.Max(1, BattleSimulator.FinalStats(WormSpec(c, run), c).Value(StatId.Hp));
            if (run.WormHp < 0) run.WormHp = max;
            else if (max > run.WormMaxHp && run.WormMaxHp > 0) run.WormHp += max - run.WormMaxHp;
            run.WormMaxHp = max;
            run.WormHp = Math.Min(run.WormHp, max);
        }

        private static void HealWorm(ContentDb c, ActiveRun run, double pct)
        {
            double mult = 1 + Level(c, run).Modifiers.HealPct / 100.0;
            run.WormHp = Math.Min(run.WormMaxHp, run.WormHp + Math.Max(0, run.WormMaxHp * pct / 100.0 * mult));
        }

        /// <summary>Board damage never kills: it leaves at least 1 HP.</summary>
        private static void DamageWorm(ActiveRun run, double pct)
        {
            run.WormHp = Math.Max(1, run.WormHp - run.WormMaxHp * pct / 100.0);
        }

        // ---- skills on the board --------------------------------------------------------------

        /// <summary>Adds a skill to the run (replacing its UpgradesFrom) and applies its OnRunStart effects.</summary>
        private static bool GrantSkill(CommandContext ctx, ActiveRun run, string skillId, int depth)
        {
            var c = ctx.Content;
            var s = c.Skill(skillId);
            if (s == null || run.Skills.Contains(skillId) || depth > 3) return false;
            if (s.UpgradesFrom != null && run.Skills.Remove(s.UpgradesFrom)) run.ReplacedSkills.Add(s.UpgradesFrom);
            run.Skills.Add(skillId);
            ctx.Emit("run_skill_gained", skillId);
            foreach (var t in s.Triggers)
                if (t.When == TriggerKind.OnRunStart) ApplyRunTrigger(ctx, run, t, null, depth + 1);
            RefreshMaxHp(c, run);
            return true;
        }

        /// <summary>Fires run-level triggers of all active worm skills (see docs/design/skill-conventions.md).</summary>
        private static void RunTriggers(CommandContext ctx, ActiveRun run, TriggerKind when, TileDef tile)
        {
            var c = ctx.Content;
            foreach (var id in ActiveSkillIds(run))
            {
                var s = c.Skill(id);
                if (s == null) continue;
                foreach (var t in s.Triggers)
                {
                    if (t.When != when) continue;
                    if (t.Param != null && t.Param.StartsWith("tile:", StringComparison.Ordinal)
                        && (tile == null || tile.Category.ToString() != t.Param.Substring(5))) continue;
                    if (t.Chance < 100 && !run.Rng.Chance(t.Chance / 100.0)) continue;
                    ApplyRunTrigger(ctx, run, t, tile, 0);
                }
            }
        }

        private static void ApplyRunTrigger(CommandContext ctx, ActiveRun run, SkillTrigger t, TileDef tile, int depth)
        {
            var c = ctx.Content;
            string p = t.Param;
            if (p == "skip_negative_events") { run.SkipNegativeEvents += (int)t.Value; return; }
            if (t.When == TriggerKind.OnRunStart && p != null)
            {
                if (p.StartsWith("random_skill:", StringComparison.Ordinal))
                {
                    Enum.TryParse(p.Substring(13), out Rarity min);
                    GrantRandomSkill(ctx, run, min, depth);
                    return;
                }
                string sid = p.StartsWith("skill:", StringComparison.Ordinal) ? p.Substring(6) : p;
                if (c.Skill(sid) != null) { GrantSkill(ctx, run, sid, depth); return; }
            }
            switch (t.Effect)
            {
                case EffectKind.ExtraDice:
                    if (t.When == TriggerKind.OnRunStart) run.ExtraDicePermanent += (int)t.Value;
                    else run.ExtraDiceNext += (int)t.Value;
                    break;
                case EffectKind.RunStatGrowth:
                    if (t.Stat != null) { run.RunStats.Add(new StatMod(t.Stat.Stat, t.Stat.Flat, t.Stat.Percent)); RefreshMaxHp(c, run); }
                    break;
                case EffectKind.GainRunCoins:
                    run.Coins += (long)t.Value;
                    break;
                case EffectKind.HealOnTile:
                    HealWorm(c, run, t.Value);
                    break;
                case EffectKind.Heal:
                    if (t.When == TriggerKind.OnTileLand || t.When == TriggerKind.OnTilePass || t.When == TriggerKind.OnLevelUp || t.When == TriggerKind.OnRoll)
                        HealWorm(c, run, t.Value);
                    break;
                case EffectKind.SlowAwakening:
                    if (t.When == TriggerKind.OnRunStart) run.AwakeningReductionPct += t.Value;
                    else run.Awakening = Math.Max(0, run.Awakening - t.Value);
                    break;
                case EffectKind.ExtraLoot:
                    run.ExtraLootPct += t.Value;
                    break;
                case EffectKind.SkipNegativeEvent:
                    run.SkipNegativeEvents += (int)t.Value;
                    break;
            }
        }

        private static void GrantRandomSkill(CommandContext ctx, ActiveRun run, Rarity min, int depth)
        {
            var pool = RunDraft.Eligible(ctx.Content, run);
            var cands = pool.FindAll(s => s.Rarity >= min);
            if (cands.Count == 0) cands = pool;
            if (cands.Count == 0) { run.Coins += ctx.Content.RunTuning.DraftEmptyCoins; return; }
            GrantSkill(ctx, run, cands[run.Rng.Range(0, cands.Count)].Id, depth);
        }

        private static void OpenDraft(CommandContext ctx, ActiveRun run)
        {
            var offer = RunDraft.Generate(ctx.Content, run, run.Rng);
            if (offer.Count == 0)
            {
                run.PendingDrafts--;
                run.Coins += ctx.Content.RunTuning.DraftEmptyCoins;
                return;
            }
            run.DraftOffer.Clear();
            run.DraftOffer.AddRange(offer);
            run.Phase = RunPhase.Draft;
            ctx.Emit("run_draft", string.Join(",", offer));
        }

        // ---- events ---------------------------------------------------------------------------

        /// <summary>The run may still offer an account-loot opportunity (RunTuning.MaxAccountLootOffersPerRun).</summary>
        public static bool AccountLootOfferLeft(ContentDb c, ActiveRun run) => run.AccountLootOffers < c.RunTuning.MaxAccountLootOffersPerRun;

        private static bool IsAccountLootEvent(ContentDb c, string eventId) =>
            eventId != null && c.RunEvents.TryGetValue(eventId, out var ev) && ev.GrantsAccountLoot();

        private static string PickRandomEvent(ContentDb c, ActiveRun run)
        {
            bool lootLeft = AccountLootOfferLeft(c, run);
            var w = new double[c.RandomRunEvents.Count];
            for (int i = 0; i < w.Length; i++)
                w[i] = !lootLeft && IsAccountLootEvent(c, c.RandomRunEvents[i].Id) ? 0 : c.RandomRunEvents[i].Weight;
            int idx = run.Rng.PickWeighted(w);
            return idx < 0 ? null : c.RandomRunEvents[idx].Id;
        }

        private static void OpenEvent(CommandContext ctx, ActiveRun run, string eventId, RunTile tile)
        {
            var c = ctx.Content;
            if (eventId == null || !c.RunEvents.TryGetValue(eventId, out var ev)) { Consume(c, run, tile, TileDefOf(c, tile)); return; }
            if (ev.Negative && TrySkipNegative(ctx, run)) { Consume(c, run, tile, TileDefOf(c, tile)); return; }
            if (ev.GrantsAccountLoot())
            {
                // Account loot is rare: a few risky opportunities per run, later caches are empty.
                if (!AccountLootOfferLeft(c, run)) { ctx.Emit("run_cache_empty", eventId); Consume(c, run, tile, TileDefOf(c, tile)); return; }
                run.AccountLootOffers++;
            }
            run.Phase = RunPhase.Event;
            run.EventId = eventId;
            run.EventTile = tile.Id;
            ctx.Emit("run_event", eventId);
        }

        private static bool CanAffordOption(ActiveRun run, EventOption o) => run.Coins >= o.CoinCost && run.Shards >= o.ShardCost;

        private static void ChooseEventOption(CommandContext ctx, ActiveRun run, int index)
        {
            var c = ctx.Content;
            Require(run.Phase == RunPhase.Event && run.EventId != null, "no event open");
            var ev = c.RunEvents[run.EventId];
            Require(index >= 0 && index < ev.Options.Count, "invalid option");
            var opt = ev.Options[index];
            Require(CanAffordOption(run, opt), "cannot afford this option");
            var before = Snap.Of(run);
            run.Coins -= opt.CoinCost;
            run.Shards -= opt.ShardCost;
            var weights = new double[opt.Outcomes.Count];
            double total = 0;
            for (int i = 0; i < weights.Length; i++) { weights[i] = opt.Outcomes[i].Weight; total += weights[i]; }
            int oi = run.Rng.PickWeighted(weights);
            var outcome = oi >= 0 ? opt.Outcomes[oi] : null;

            var tile = run.EventTile >= 0 ? run.Tiles[run.EventTile] : null;
            var tileDef = tile != null ? TileDefOf(c, tile) : null;
            var result = new RunOutcome
            {
                Source = OutcomeSource(c, ev.Id, tileDef),
                EventId = ev.Id, Title = ev.Name, Choice = opt.Text, Chance = opt.Outcomes.Count > 1,
                SuccessChancePct = total > 0 && weights.Length > 0 ? 100.0 * weights[0] / total : 100, Success = oi <= 0,
            };
            int eventTileId = run.EventTile;
            run.Phase = RunPhase.AwaitRoll;
            run.EventId = null;
            run.EventTile = -1;
            run.LastEventText = outcome != null ? outcome.Text : null;
            ctx.State.Count("run_events_resolved");
            ctx.Emit("run_event_outcome", ev.Id, index.ToString(), oi.ToString());
            if (tile != null) Consume(c, run, tile, TileDefOf(c, tile));
            if (outcome != null)
            {
                if (IsNegativeOutcome(outcome) && TrySkipNegative(ctx, run)) run.LastEventText = "Your boon shields you from misfortune.";
                else
                {
                    ApplyEffects(ctx, run, outcome.Effects, eventTileId);
                }
            }
            result.Text = run.LastEventText;
            before.Diff(ctx.Content, run, result.Lines);
            PublishOutcome(ctx, run, result);
            Continue(ctx, run);
        }

        /// <summary>"treasure" / "campfire" for tile-bound events (by the tile, else by the tile kind that hosts the event), else "event".</summary>
        private static string OutcomeSource(ContentDb c, string eventId, TileDef tile)
        {
            TileCategory? cat = tile != null && (tile.Category == TileCategory.Treasure || tile.Category == TileCategory.Campfire) ? tile.Category : (TileCategory?)null;
            if (cat == null)
                foreach (var d in c.BoardTiles.Values)
                    if (d.EventId == eventId) { cat = d.Category; break; }
            return cat == TileCategory.Treasure ? "treasure" : cat == TileCategory.Campfire ? "campfire" : "event";
        }

        /// <summary>An outcome whose effects are all harmful (skippable by "skip negative events" boons).</summary>
        public static bool IsNegativeOutcome(EventOutcome o)
        {
            if (o.Effects.Count == 0) return false;
            foreach (var e in o.Effects)
            {
                bool bad;
                switch (e.Kind)
                {
                    case RunEffectKind.DamagePct: case RunEffectKind.Battle: case RunEffectKind.LoseRandomSkill: case RunEffectKind.StrengthenEnemies: bad = true; break;
                    case RunEffectKind.StatPercent: bad = e.Value < 0; break;
                    case RunEffectKind.Coins: bad = e.Value < 0; break;
                    case RunEffectKind.Meter: case RunEffectKind.AwakenRatePct: bad = e.Value > 0; break;
                    case RunEffectKind.BossStatPct: case RunEffectKind.Danger: bad = e.Value > 0; break;
                    default: bad = false; break;
                }
                if (!bad) return false;
            }
            return true;
        }

        private static void ApplyEffects(CommandContext ctx, ActiveRun run, List<RunEffect> effects, int originTile)
        {
            var c = ctx.Content;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                switch (e.Kind)
                {
                    case RunEffectKind.Coins: run.Coins = Math.Max(0, run.Coins + (long)e.Value); break;
                    case RunEffectKind.HealPct: HealWorm(c, run, e.Value); break;
                    case RunEffectKind.DamagePct: DamageWorm(run, e.Value); break;
                    case RunEffectKind.StatPercent: run.RunStats.Add(new StatMod(e.Stat, 0, e.Value)); RefreshMaxHp(c, run); break;
                    case RunEffectKind.GainSkill: GrantRandomSkill(ctx, run, e.MinRarity, 0); break;
                    case RunEffectKind.GainSpecificSkill: GrantSkill(ctx, run, e.Param, 0); break;
                    case RunEffectKind.UpgradeSkill: UpgradeRandomSkill(ctx, run); break;
                    case RunEffectKind.LoseRandomSkill:
                        if (run.Skills.Count > 0) run.Skills.RemoveAt(run.Rng.Range(0, run.Skills.Count));
                        RefreshMaxHp(c, run);
                        break;
                    case RunEffectKind.Loot: AddLoot(c, run, Math.Max(1, (int)e.Value)); break;
                    case RunEffectKind.PendingReward:
                        if (e.Reward != null) { run.PendingLoot.Add(new Reward(e.Reward.Kind, e.Reward.Id, e.Reward.Amount, e.Reward.Rarity)); run.Danger++; }
                        break;
                    case RunEffectKind.BossStatPct: run.BossStatPct += e.Value; break;
                    case RunEffectKind.Meter: AddAwakening(ctx, run, e.Value); break;
                    case RunEffectKind.Xp: AddXp(ctx, run, (int)e.Value); break;
                    case RunEffectKind.Shards: run.Shards += (int)e.Value; break;
                    case RunEffectKind.Danger: run.Danger = Math.Max(0, run.Danger + (int)e.Value); break;
                    case RunEffectKind.AwakenRatePct: run.AwakeningReductionPct -= e.Value; break;
                    case RunEffectKind.StrengthenEnemies:
                    {
                        int origin = originTile >= 0 ? originTile : run.Position;
                        foreach (var id in RunBoard.Ahead(run.Tiles, origin, c.RunTuning.CampfireRadius))
                        {
                            var cat = TileDefOf(c, run.Tiles[id]).Category;
                            if (cat == TileCategory.Enemy || cat == TileCategory.Elite || cat == TileCategory.Miniboss) run.Tiles[id].BonusPct += e.Value;
                        }
                        break;
                    }
                    case RunEffectKind.Battle:
                    {
                        string p = e.Param ?? "enemy";
                        bool isId = c.Enemies.ContainsKey(p);
                        var enemies = EnemiesFor(c, run, isId ? "id" : p, isId ? p : null, 2, e.Value);
                        RewardsFor(c, enemies, 2, out int xp, out long coins);
                        var rest = effects.GetRange(i + 1, effects.Count - i - 1);
                        if (!Fight(ctx, run, enemies, "event", xp, coins, rest)) return;
                        break;
                    }
                }
            }
        }

        private static void UpgradeRandomSkill(CommandContext ctx, ActiveRun run)
        {
            var c = ctx.Content;
            var upgrades = new List<string>();
            foreach (var id in RunDraft.SortedSkillIds(c))
            {
                var s = c.Skills[id];
                if (s.UpgradesFrom != null && run.Skills.Contains(s.UpgradesFrom) && RunDraft.IsEligible(c, run, s)) upgrades.Add(id);
            }
            if (upgrades.Count == 0) { run.Coins += c.RunTuning.DraftEmptyCoins; return; }
            GrantSkill(ctx, run, upgrades[run.Rng.Range(0, upgrades.Count)], 0);
        }

        // ---- shops ----------------------------------------------------------------------------

        private static ShopTableDef ShopTable(ContentDb c, ActiveRun run)
        {
            var level = Level(c, run);
            return level.ShopTableId != null && c.RunShopTables.TryGetValue(level.ShopTableId, out var t) ? t : c.RunShopTables["shop_standard"];
        }

        private static void OpenShop(CommandContext ctx, ActiveRun run)
        {
            run.ShopOutcome = null;
            FillShop(ctx.Content, run);
            run.Phase = RunPhase.Shop;
            ctx.Emit("run_shop");
        }

        private static void FillShop(ContentDb c, ActiveRun run)
        {
            var table = ShopTable(c, run);
            run.Shop.Clear();
            var pool = RunDraft.Eligible(c, run);
            for (int i = 0; i < table.SkillOffers && pool.Count > 0; i++)
            {
                var w = new double[pool.Count];
                for (int k = 0; k < pool.Count; k++) w[k] = table.RarityWeights.TryGetValue(pool[k].Rarity, out var x) ? x : 1;
                int idx = run.Rng.PickWeighted(w);
                if (idx < 0) break;
                var s = pool[idx];
                pool.RemoveAt(idx);
                run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Skill, SkillId = s.Id, Price = table.SkillPrices.TryGetValue(s.Rarity, out var p) ? p : 50 });
            }
            run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Heal, Value = table.HealPct, Price = table.HealPrice });
            if (table.StatOffers.Count > 0)
            {
                var m = table.StatOffers[run.Rng.Range(0, table.StatOffers.Count)];
                run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Stat, Stat = new StatMod(m.Stat, m.Flat, m.Percent), Value = m.Percent, Price = table.StatPrice });
            }
        }

        // ---- completion -----------------------------------------------------------------------

        /// <summary>Mutable progress record of a level (an archived level is re-opened from its archive flags).</summary>
        private static LevelProgress GetProgress(ContentDb c, GameState s, string levelId)
        {
            if (!s.Run.Levels.TryGetValue(levelId, out var p)) { p = Archived(s, c, levelId) ?? new LevelProgress(); s.Run.Levels[levelId] = p; }
            while (p.Feats.Count < 3) p.Feats.Add(false);
            return p;
        }

        /// <summary>Ends the run. Won: pays pending loot + completion rewards (modifiers applied). Lost: everything pending is lost.</summary>
        private static void Finish(CommandContext ctx, ActiveRun run, bool won)
        {
            var c = ctx.Content; var s = ctx.State;
            var level = Level(c, run);
            var portal = c.Portal(run.PortalId);
            var summary = new RunSummary { RunId = run.RunId, LevelId = run.LevelId, Mode = run.Mode, Won = won, Rolls = run.Rolls, Level = run.Level, Floors = run.Floor, LastBattle = run.LastBattle,
                AccountLootOffers = run.AccountLootOffers, AccountLootDrops = run.PendingLoot.Count, Laps = run.Laps, XpTotal = run.XpTotal };
            if (won)
            {
                double modeMult = level.RewardMultiplier * (run.Mode == RunMode.Overcharged ? portal.OverchargedRewardMultiplier : 1);
                double lootMult = 1 + run.ExtraLootPct / 100.0;
                double biomassMult = BiomassSurge(ctx, run, summary);
                var rewards = new List<Reward>();
                int doublable;
                foreach (var r in run.PendingLoot) rewards.Add(Scaled(r, modeMult * lootMult * (r.Id == Currencies.Biomass ? biomassMult : 1)));
                if (run.Mode != RunMode.Burrow)
                {
                    RollRareDrops(level, run);
                    foreach (var r in level.CompletionRewards) rewards.Add(Scaled(r, modeMult * (r.Id == Currencies.Biomass ? biomassMult : 1)));
                    var before = run.PendingLoot.Count;
                    for (int i = 0; i < level.CompletionLootRolls; i++) RollLoot(c, run, false);
                    for (int i = before; i < run.PendingLoot.Count; i++)
                        rewards.Add(Scaled(run.PendingLoot[i], modeMult * (run.PendingLoot[i].Id == Currencies.Biomass ? biomassMult : 1)));
                }

                doublable = rewards.Count; // feat Diamonds below are not doubled by the ad
                if (run.Mode != RunMode.Burrow)
                {
                    var p = GetProgress(c, s, run.LevelId);
                    bool firstClear = run.Mode == RunMode.Overcharged ? !p.OverchargedCleared : !p.Cleared;
                    if (run.Mode == RunMode.Overcharged) p.OverchargedCleared = true; else p.Cleared = true;
                    p.Clears++;
                    if (firstClear) s.Count("levels_cleared");
                    // Global main level number (portal 1 = 1..10, ...): Meta's feature gates read main_level_max.
                    if (run.Mode == RunMode.Normal) ProgressionUtil.CountMax(s, Requirements.MainLevelCounter, MainLevelNumber(c, run.LevelId));
                    if (run.Mode == RunMode.DarkSide) ProgressionUtil.CountMax(s, DarkSideMaxCounter, level.Number);
                    // Endless campaign milestones (chest every 10 levels, portal bonus every 50): first Normal clear only.
                    if (firstClear && run.Mode == RunMode.Normal && level.FirstClearRewards.Count > 0)
                    {
                        foreach (var r in level.FirstClearRewards) rewards.Add(new Reward(r.Kind, r.Id, r.Amount, r.Rarity));
                        s.Count("milestone_chests");
                        ctx.Emit("run_milestone", run.LevelId, level.Number.ToString());
                    }
                    bool[] feats =
                    {
                        true,
                        run.WormMaxHp > 0 && run.WormHp / run.WormMaxHp * 100.0 >= level.FeatHpPct,
                        run.BossRounds > 0 && run.BossRounds <= level.FeatBossRounds,
                    };
                    for (int i = 0; i < 3; i++)
                    {
                        if (!feats[i] || p.Feats[i]) continue;
                        p.Feats[i] = true;
                        summary.NewFeats.Add(i);
                        s.Count("run_feats_earned");
                        rewards.Add(Reward.Cur(Currencies.Diamonds, c.RunTuning.FeatDiamonds));
                    }
                }
                for (int i = 0; i < rewards.Count; i++) GrantRunReward(ctx, rewards[i], summary, i < doublable);
                foreach (var r in run.PendingRare)
                {
                    if (!ctx.Grant(r)) continue; // no use yet: stashed (RewardGate), not shown as granted
                    summary.Granted.Add(r);
                    ctx.Emit("run_rare_drop", r.Kind, r.Id, r.Amount.ToString());
                }
                CompactProgress(s, c);
                s.Count("runs_completed");
                s.Count("runs_completed_" + run.Mode.ToString().ToLowerInvariant());
                ctx.Emit("run_completed", run.RunId, run.LevelId);
                OnboardingSystem.CheckUnlocks(ctx); // features unlocked by this clear show up in this command's events
            }
            else
            {
                s.Count("runs_failed");
                ctx.Emit("run_failed", run.RunId, run.PendingLoot.Count.ToString());
            }
            run.Phase = won ? RunPhase.Completed : RunPhase.Failed;
            s.Run.LastSummary = summary;
            s.Run.Active = null;
        }

        private static Reward Scaled(Reward r, double mult)
        {
            long amount = r.Kind == RewardKinds.Currency ? Math.Max(1, (long)Math.Round(r.Amount * mult)) : r.Amount;
            return new Reward(r.Kind, r.Id, amount, r.Rarity);
        }

        /// <summary>
        /// Applies run_reward modifiers to currencies and grants through the owners' granters (item drops with
        /// Id "*" are resolved to a random def of their rarity by the granter, see RewardWildcard).
        /// </summary>
        private static void GrantRunReward(CommandContext ctx, Reward r, RunSummary summary, bool doublable)
        {
            var reward = new Reward(r.Kind, r.Id, r.Amount, r.Rarity);
            if (reward.Kind == RewardKinds.Currency)
            {
                double m = ModifierMath.Multiplier(ctx.State.Modifiers, "run_reward:" + reward.Id, ctx.NowMs);
                reward.Amount = Math.Max(0, (long)Math.Round(reward.Amount * m));
            }
            if (reward.Amount <= 0) return;
            if (!ctx.Grant(reward)) return; // no use yet: stashed (RewardGate), not shown as granted
            summary.Granted.Add(reward);
            if (doublable) summary.Doublable.Add(reward);
        }
    }
}
