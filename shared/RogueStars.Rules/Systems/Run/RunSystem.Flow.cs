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
                if (run.PendingMythicDrafts > 0) { OpenMythicDraft(ctx, run); continue; }
                if (run.PendingDrafts > 0) { OpenDraft(ctx, run); continue; }
                if (run.StepsLeft > 0)
                {
                    var tile = run.Tiles[run.Position];
                    if (tile.Next.Count == 0) EnsureBoardAhead(c, run, run.StepsLeft);
                    if (tile.Next.Count == 0) { run.StepsLeft = 0; continue; }
                    int next;
                    if (tile.Next.Count > 1)
                    {
                        // both ways end the move on the same tile (the roll walks past the join): no choice, the worm takes
                        // the default arm (playtest 0.1.325)
                        if (run.ChosenNext < 0 && SameLanding(c, run, tile)) run.ChosenNext = tile.Next[0];
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
                    if (run.StepsLeft > 0)
                    {
                        var passed = Snap.Of(run);
                        PassTile(ctx, run, nt, def);
                        TileOutcome(ctx, run, nt, def, passed, "pass", !def.TriggersOnPass);
                        if (!def.ForceStop) continue;
                        run.StepsLeft = 0;
                    }
                    var landed = Snap.Of(run);
                    int seq = run.OutcomeSeq;
                    LandTile(ctx, run, nt, def);
                    // Every landing shows what it did: a decision tile reports through its decision (or its LastOutcome when it
                    // resolved at once, e.g. a boon kept a bad event away); anything else gets a tile outcome.
                    bool reported = OpensDecision(def) && (run.Phase != RunPhase.AwaitRoll || run.OutcomeSeq != seq);
                    if (!reported) TileOutcome(ctx, run, nt, def, landed, IsFightTile(def) ? "battle" : "tile", false);
                    continue;
                }
                // Minibosses come with the clock too (RunTuning.MinibossAtMeter): a lieutenant of the boss attacks after the move.
                if (run.PendingAmbushes > 0 && !run.BossFought) { OpenEncounter(ctx, run, "ambush"); return; }
                // The awakening meter is the run's clock: once it is full (or at the roll limit) the boss's arena opens ahead
                // of the worm after the move; the next step walks in and fights.
                if (!run.BossFought && (run.Rolls >= run.MaxRolls || BossAwake(c, run))) { OpenEncounter(ctx, run, "boss"); return; }
                return;
            }
        }

        /// <summary>
        /// Where a move of <paramref name="steps"/> steps that enters <paramref name="first"/> ends: the tile it lands on, or a
        /// forced stop it meets on the way (shop, miniboss, arcade). -1 when that is not known yet: another fork comes first, or
        /// the board ends.
        /// </summary>
        public static int LandingVia(ContentDb c, ActiveRun run, int first, int steps)
        {
            int pos = first;
            for (int left = steps - 1; left > 0; left--)
            {
                var t = run.Tiles[pos];
                if (TileDefOf(c, t).ForceStop) return pos;
                if (t.Next.Count != 1) return -1;
                pos = t.Next[0];
            }
            return pos;
        }

        /// <summary>
        /// A fork whose ways all end the current move on the same tile (the split is shorter than the roll and both arms are
        /// equally long): the worm just moves, no choice to make. Pickups passed on the arms differ, but the landing — what the
        /// move is about — is the same, so the default arm is taken.
        /// </summary>
        public static bool SameLanding(ContentDb c, ActiveRun run, RunTile fork)
        {
            if (fork.Next.Count < 2 || run.StepsLeft <= 0) return false; // pure: a way into the unbuilt board is "not known" (a choice)
            int land = LandingVia(c, run, fork.Next[0], run.StepsLeft);
            if (land < 0) return false;
            for (int i = 1; i < fork.Next.Count; i++)
                if (LandingVia(c, run, fork.Next[i], run.StepsLeft) != land) return false;
            return true;
        }

        /// <summary>
        /// The board grows ahead of the worm: segments are appended until the open end is more than
        /// <paramref name="steps"/> + RunTuning.BoardLookAhead steps away (nothing behind the worm ever changes).
        /// </summary>
        private static void EnsureBoardAhead(ContentDb c, ActiveRun run, int steps)
        {
            var level = Level(c, run);
            int need = steps + c.RunTuning.BoardLookAhead;
            for (int guard = 0; guard < 20 && RunBoard.StepsToEnd(run.Tiles, run.Position, run.BoardEnd) < need; guard++)
            {
                var b = RunBoard.Wrap(run.Tiles, run.BoardEnd, run.BoardSegments, run.SetPieces);
                RunBoard.AppendSegment(c, level, b, run.Rng, run);
                run.BoardEnd = b.EndTile;
                run.BoardSegments = b.Segments;
                if (!AccountLootOfferLeft(c, run)) RetireTreasures(c, run);
            }
        }

        /// <summary>A fight comes after the move (phase Encounter; the next clear step resolves it with its own battle).</summary>
        private static void OpenEncounter(CommandContext ctx, ActiveRun run, string kind)
        {
            run.Phase = RunPhase.Encounter;
            run.EncounterKind = kind;
            if (kind == "boss")
            {
                // the arena opens right ahead of the worm: the path ends there
                var here = run.Tiles[run.Position];
                var arena = RunBoard.Add(run.Tiles, "boss_gate", here.X + 1, -1); // its own lane: never on top of a path tile
                RunBoard.PlaceAhead(here, arena, run.Tiles);
                here.Next.Clear();
                here.Next.Add(arena.Id);
                run.BossGateTile = arena.Id;
                run.BoardEnd = arena.Id;
            }
            ctx.Emit("run_encounter", kind);
        }

        /// <summary>Resolves the open encounter: the miniboss attacks, or the worm walks into the arena and fights the boss.</summary>
        private static void Encounter(CommandContext ctx, ActiveRun run)
        {
            Require(run.Phase == RunPhase.Encounter, "no encounter");
            string kind = run.EncounterKind;
            run.Phase = RunPhase.AwaitRoll;
            run.EncounterKind = null;
            if (kind == "ambush")
            {
                run.PendingAmbushes = Math.Max(0, run.PendingAmbushes - 1);
                var before = Snap.Of(run);
                StartAmbush(ctx, run);
                if (ctx.State.Run.Active == run)
                {
                    var here = run.Tiles[run.Position];
                    TileOutcome(ctx, run, here, null, before, "battle", false);
                    run.TileOutcomes[run.TileOutcomes.Count - 1].Title = "Miniboss ambush";
                }
            }
            else
            {
                if (run.BossGateTile >= 0 && run.Position != run.BossGateTile)
                {
                    run.Position = run.BossGateTile;
                    run.Trail.Add(run.BossGateTile);
                }
                StartBoss(ctx, run);
            }
            Continue(ctx, run);
        }

        /// <summary>Once the run's account-loot opportunities are used up, chests ahead become supply caches (no chest that does nothing).</summary>
        private static void RetireTreasures(ContentDb c, ActiveRun run)
        {
            foreach (var t in run.Tiles)
                if (!t.Cleared && t.TileId == "treasure" && !run.Trail.Contains(t.Id)) t.TileId = "loot";
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
                AddCoins(run, def.Coins);
                if (def.Value > 0) AddXp(ctx, run, (int)def.Value); // supply caches: run XP
            }
            Consume(ctx.Content, run, tile, def);
        }

        private static bool IsNegative(TileDef def) => def.Valence == TileValence.Negative;

        private static bool IsFightTile(TileDef def) => def.Category == TileCategory.Enemy || def.Category == TileCategory.Elite || def.Category == TileCategory.Miniboss;

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
            run.PendingStartRage = 0;
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
                    run.RunStats.Add(StatMod.Bonus(def.Stat, def.Value)); // Sharp Stone's +15% crit is 15 points, not 15% of 5
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
                case TileCategory.Quest:
                    OpenQuest(ctx, run, tile);
                    break;
                case TileCategory.Hazard:
                    if (def.Value > 0) DamageWorm(run, def.Value);
                    if (def.Coins > 0) run.Coins = Math.Max(0, run.Coins - def.Coins);
                    if (def.PushBack > 0) run.StepPenaltyNext += def.PushBack; // the path never goes back: the next roll is shorter
                    Consume(c, run, tile, def);
                    break;
                case TileCategory.Shard:
                case TileCategory.Pickup:
                    ResolvePickup(ctx, run, tile, def);
                    break;
                case TileCategory.BossTile:
                    AddAwakening(ctx, run, def.MeterDelta);
                    run.BossStatPct += def.BossStatPct;
                    if (def.Coins > 0) GainCoins(run, def.Coins); else run.Coins += def.Coins; // cost (a tribute) or a gain
                    Consume(c, run, tile, def);
                    break;
            }
        }

        /// <summary>
        /// A resolved tile stays what it was and is marked cleared (the path never comes back to it; the client draws it
        /// cleared, so a beaten fight never turns into a new-looking tile under the worm).
        /// </summary>
        private static void Consume(ContentDb c, ActiveRun run, RunTile tile, TileDef def)
        {
            tile.Cleared = true;
            tile.BonusPct = 0;
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

        /// <summary>
        /// The awakening points an event effect of <paramref name="amount"/> adds now: gains are scaled like the boss's pace
        /// (<see cref="AddAwakening"/>), drains are not. The option button shows this number, so button and result agree.
        /// </summary>
        public static double EventAwakening(ContentDb c, ActiveRun run, double amount) =>
            amount > 0 && run != null && Boss(c, run) != null ? AwakeningScaled(c, run, amount) : amount;

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
            var at = c.RunTuning.MinibossAtMeter;
            var levelDef = c.RunLevel(run.LevelId);
            bool clock = levelDef == null || levelDef.ClockMinibosses; // the first main levels have none yet (GDD §16)
            while (clock && run.Mode != RunMode.Burrow && run.MinibossesSpawned < at.Count && run.Awakening >= at[run.MinibossesSpawned] - 1e-9)
            {
                run.MinibossesSpawned++;
                run.PendingAmbushes++;
                ctx.Emit("run_miniboss_coming", run.MinibossesSpawned.ToString());
            }
        }

        /// <summary>A miniboss of the awakening clock attacks (after the move). Win: a mythic choice.</summary>
        private static void StartAmbush(CommandContext ctx, ActiveRun run)
        {
            var c = ctx.Content;
            var enemies = EnemiesFor(c, run, "ambush", null, 3, 0);
            RewardsFor(c, enemies, 3, out int xp, out long coins);
            xp = 0; // the reward is the mythic choice (keeps level-up drafts at ~8–10 per run)
            coins += c.RunTuning.MinibossCoins;
            ctx.Emit("run_ambush", enemies.Count > 0 ? enemies[0].Id : "");
            if (Fight(ctx, run, enemies, "ambush", xp, coins, null)) AfterWin(ctx, run, "ambush", null);
        }

        /// <summary>True for the two miniboss fights: the lair tile ("miniboss") and the awakening clock's lieutenant ("ambush").</summary>
        public static bool IsMinibossFight(string kind) => kind == "miniboss" || kind == "ambush";

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
                    foreach (var id in RunBoard.Ahead(run.Tiles, run.Position, c.RunTuning.BossActionReach))
                    {
                        var t = run.Tiles[id];
                        if (t.Fixed || t.Cleared) continue;
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

        /// <summary>
        /// The run's miniboss order for a level (pure): its <see cref="LevelDef.ElitePool"/> (else its MinibossId) shuffled by a
        /// fork of the run's stream, so every run has its own order and the run's lairs meet different minibosses.
        /// </summary>
        public static List<string> MinibossOrder(LevelDef level, Rng runRng)
        {
            var pool = new List<string>(level.ElitePool);
            if (pool.Count == 0 && level.MinibossId != null) pool.Add(level.MinibossId);
            var rng = runRng.Fork("minibosses:" + level.Id);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int k = rng.Range(0, i + 1);
                (pool[i], pool[k]) = (pool[k], pool[i]);
            }
            return pool;
        }

        /// <summary>The miniboss the run's next miniboss fight meets: entry <c>MinibossesDefeated</c> of its order (wrapping).</summary>
        public static string NextMiniboss(LevelDef level, ActiveRun run)
        {
            var order = run.MinibossOrder;
            if (order == null || order.Count == 0) return level.MinibossId;
            return order[run.MinibossesDefeated % order.Count];
        }

        /// <summary>Enemy specs for a fight: kind = enemy | elite | miniboss | boss | an enemy id.</summary>
        private static List<CombatantSpec> EnemiesFor(ContentDb c, ActiveRun run, string kind, string enemyId, int tileLevel, double bonusPct)
        {
            var t = c.RunTuning;
            var level = Level(c, run);
            var mods = level.Modifiers;
            double scale = c.ThreatMultiplier(run.Threat);
            // The boss's forces grow while the run (and the worm's build) grows: +EnemyPctPerRoll per roll so far.
            double common = DangerPct(c, run) + bonusPct + t.BoardEnemyPct + t.EnemyPctPerRoll * run.Rolls;
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
                case "ambush":
                {
                    double b = common + t.MinibossBonusPct + t.MinibossStepPct * run.MinibossesDefeated;
                    list.Add(BattleSimulator.BuildEnemy(c, NextMiniboss(level, run), scale, b + mods.EnemyAtkPct, b + mods.EnemyHpPct, mods.EnemyDefPct));
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

        /// <summary>Plague (lab Mythic): its threshold (stacks per opening stack) if the run owns it, else 0.</summary>
        public static int PlagueEvery(ContentDb c, ActiveRun run)
        {
            foreach (var id in run.Skills)
            {
                var s = c.Skill(id);
                if (s == null) continue;
                foreach (var t in s.Triggers)
                    if (t.Effect == EffectKind.Rule && t.Param == "plague" && t.Value >= 1) return (int)t.Value;
            }
            return 0;
        }

        /// <summary>Plague's opening stacks for the run's next battle: per status, one stack for every PlagueEvery inflicted this run.</summary>
        public static Dictionary<string, int> PlagueOpening(ContentDb c, ActiveRun run)
        {
            var res = new Dictionary<string, int>();
            int every = PlagueEvery(c, run);
            if (every <= 0) return res;
            foreach (var kv in run.PlagueStacks)
                if (kv.Value >= every) res[kv.Key] = kv.Value / every;
            return res;
        }

        /// <summary>Runs a battle with the run's worm. On loss the run enters Defeated (revive or give up).</summary>
        private static bool Fight(CommandContext ctx, ActiveRun run, List<CombatantSpec> enemies, string kind, int xp, long coins, List<RunEffect> followUp)
        {
            var c = ctx.Content;
            var setup = new BattleSetup { RoundCap = kind == "boss" ? c.Combat.BossRoundCap : c.Combat.RoundCap };
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(run.Loadout, c, run.Skills, WormExtraStats(c, run), run.SkillLevels));
            setup.Player[setup.Player.Count - 1].StartHp = run.WormHp;
            setup.Player[setup.Player.Count - 1].StartRage = run.PendingStartRage;
            run.PendingStartRage = 0;
            var opening = PlagueOpening(c, run);
            if (opening.Count > 0) setup.Player[setup.Player.Count - 1].OpeningStatuses = opening;
            setup.Enemy.AddRange(enemies);
            var result = BattleSimulator.Simulate(setup, c, run.Rng.Fork("battle:" + run.BattleCount));
            run.BattleCount++;
            run.LastBattle = result;
            if (result.StacksInflicted != null)
                foreach (var kv in result.StacksInflicted)
                    run.PlagueStacks[kv.Key] = (run.PlagueStacks.TryGetValue(kv.Key, out var had) ? had : 0) + kv.Value;
            if (result.RunCoins > 0) GainCoins(run, result.RunCoins); // Plunder (Void Pirate): crit kills pay run coins
            run.WormHp = result.WormHpLeft;
            run.WormMaxHp = result.WormMaxHp;
            if (kind == "boss") run.BossRounds = result.Rounds;
            int felled = 0; // enemies knocked out (summons excluded): summon event tasks
            foreach (var f in result.Fighters) if (f.Side == BattleSide.Enemy && f.SummonedBy < 0 && !f.Alive) felled++;
            if (felled > 0) ctx.State.Count("run_enemies_defeated", felled);
            int novas = 0;
            foreach (var le in result.Log) if (le.Action == BattleAction.Supernova && le.Amount == 0 && le.Actor == le.Target) novas++;
            ctx.Emit("run_battle", kind, result.Winner.ToString(), result.Rounds.ToString(), result.Log.Count.ToString(),
                result.PlayerDamageDealt.ToString(), result.PlayerDamageTaken.ToString(), novas.ToString());
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
            if (kind == "elite" || IsMinibossFight(kind)) ctx.State.Count("run_elites_killed");
            if (IsMinibossFight(kind))
            {
                run.MinibossesDefeated++;
                run.PendingMythicDrafts++;
                ctx.State.Count("run_minibosses_killed");
            }
            GainCoins(run, coins);
            HealWorm(c, run, c.RunTuning.HealAfterBattlePct);
            RunTriggers(ctx, run, TriggerKind.OnBattleWon, null);
            double xpGain = BattleSimulator.FinalStats(WormSpec(c, run), c).Value(StatId.XpGain);
            AddXp(ctx, run, (int)Math.Round(xp * (1 + xpGain / 100.0)));
        }

        /// <summary>Post-win continuation: consume the battle tile, finish the boss, or resume event effects.</summary>
        private static void AfterWin(CommandContext ctx, ActiveRun run, string kind, List<RunEffect> followUp, Snap before = null)
        {
            if (kind == "boss") { BossDefeated(ctx, run); return; }
            if (kind == "quest") { QuestBattleWonAfterRevive(ctx, run, before); return; } // a revive retried a lost quest battle
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
            double bossPct = common + run.BossStatPct + c.RunTuning.BossBuildPct;
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
            run.PendingMythicDrafts = 0;
            run.PendingAmbushes = 0;
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

        /// <summary>
        /// Adds run XP. One grant (a battle, a pickup, an event outcome) gives at most ONE level-up (playtest: a big battle gave
        /// four level-ups / drafts at once). The overflow carries (at most RunTuning.XpBankLevels further levels' worth, the rest
        /// is lost) and is paid out one level per roll (<see cref="LevelUpFromBank"/>) or by the next grant.
        /// </summary>
        private static void AddXp(CommandContext ctx, ActiveRun run, int amount)
        {
            if (amount <= 0) return;
            run.Xp += amount;
            run.XpTotal += amount;
            LevelUpFromBank(ctx, run);
        }

        /// <summary>At most one level-up from the XP the run holds; the carry is capped at RunTuning.XpBankLevels levels.</summary>
        private static void LevelUpFromBank(CommandContext ctx, ActiveRun run)
        {
            var c = ctx.Content;
            int need = XpNeeded(c, run.Level);
            if (run.Xp < need) return;
            int cap = -1;
            for (int i = 1; i <= Math.Max(0, c.RunTuning.XpBankLevels); i++) cap += XpNeeded(c, run.Level + i);
            run.Xp = Math.Max(0, Math.Min(run.Xp - need, cap));
            run.Level++;
            run.PendingDrafts++;
            ctx.Emit("run_level_up", run.Level.ToString());
            RunTriggers(ctx, run, TriggerKind.OnLevelUp, null);
        }

        /// <summary>Run XP needed to go from <paramref name="level"/> to the next level (HUD "XP n/needed").</summary>
        public static int XpNeeded(ContentDb c, int level) => Math.Max(1, c.RunTuning.XpBase + c.RunTuning.XpPerLevel * (Math.Max(1, level) - 1));

        /// <summary>
        /// Daily Biomass surge (RunTuning.DailyBiomassBonusRuns / Mult): the first N won Normal/Overcharged runs of the
        /// reset-day pay their Biomass ×Mult. Returns the Biomass multiplier for this (won) run.
        /// </summary>
        private static double BiomassSurge(CommandContext ctx, ActiveRun run, RunSummary summary)
        {
            var t = ctx.Content.RunTuning; var rs = ctx.State.Run;
            if (run.Mode != RunMode.Normal && run.Mode != RunMode.Overcharged) return 1;
            if (!BiomassSurgeActive(ctx.State, ctx.Content, ctx.NowMs)) return 1; // no use for Biomass yet: no surge, no counter
            long day = GameClock.DayIndex(ctx.Content, ctx.NowMs);
            if (rs.BiomassBonusDay != day) { rs.BiomassBonusDay = day; rs.BiomassBonusRuns = 0; }
            if (rs.BiomassBonusRuns >= t.DailyBiomassBonusRuns) return 1;
            rs.BiomassBonusRuns++;
            summary.BiomassBonus = true;
            return t.DailyBiomassBonusMult;
        }

        /// <summary>
        /// The daily Biomass surge exists only once Biomass has a use (RewardGate.CurrencyHasUse: City or Worm Level unlocked);
        /// before that no surge run is consumed and no surge counter or text is shown anywhere.
        /// </summary>
        public static bool BiomassSurgeActive(GameState s, ContentDb c, long nowMs) =>
            c.RunTuning.DailyBiomassBonusRuns > 0 && RewardGate.CurrencyHasUse(s, c, Currencies.Biomass, nowMs);

        /// <summary>Daily Biomass surge runs left today (0 while the surge is not active, <see cref="BiomassSurgeActive"/>).</summary>
        public static int BiomassBonusRunsLeft(GameState s, ContentDb c, long nowMs)
        {
            if (!BiomassSurgeActive(s, c, nowMs)) return 0;
            return s.Run.BiomassBonusDay == GameClock.DayIndex(c, nowMs) ? Math.Max(0, c.RunTuning.DailyBiomassBonusRuns - s.Run.BiomassBonusRuns) : c.RunTuning.DailyBiomassBonusRuns;
        }

        /// <summary>Rolls the level's rare drops once (when the level / Burrow floor is beaten) into PendingRare.</summary>
        private static void RollRareDrops(LevelDef level, ActiveRun run)
        {
            foreach (var d in level.RareDrops)
                if (d.Reward != null && run.Rng.NextDouble() * 100 < d.ChancePct)
                    run.PendingRare.Add(new Reward(d.Reward.Kind, d.Reward.Id, d.Reward.Amount, d.Reward.Rarity));
        }

        /// <summary>
        /// One loot roll (tile loot, event loot, completion rolls). Loot Luck (the worm's <see cref="StatId.LootLuck"/>
        /// points: lab Salvage Algorithms, Salvage Yard, the Salvage Yard / Blueprint Relay collection) is the % chance of a
        /// bonus roll from the same table (every full 100 points = a sure bonus roll); bonus items never raise Danger.
        /// </summary>
        private static void RollLoot(ContentDb c, ActiveRun run, bool raisesDanger)
        {
            var level = Level(c, run);
            var table = c.RunLootTable(level.LootTableId);
            if (table == null || table.Entries.Count == 0) return;
            if (!RollLootEntry(run, table)) return;
            if (raisesDanger) run.Danger++;
            double luck = LootLuck(c, run);
            if (luck <= 0) return; // no RNG draw without luck: unlucky runs keep their streams
            int bonus = (int)(luck / 100);
            if (run.Rng.NextDouble() * 100 < luck - bonus * 100) bonus++;
            for (int i = 0; i < bonus; i++) RollLootEntry(run, table);
        }

        private static bool RollLootEntry(ActiveRun run, LootTableDef table)
        {
            var weights = new double[table.Entries.Count];
            for (int i = 0; i < weights.Length; i++) weights[i] = table.Entries[i].Weight;
            int idx = run.Rng.PickWeighted(weights);
            if (idx < 0) return false;
            var e = table.Entries[idx];
            long amount = e.Max > e.Min ? e.Min + run.Rng.Range(0, (int)(e.Max - e.Min + 1)) : e.Min;
            run.PendingLoot.Add(new Reward(e.Kind, e.Id, amount, e.Rarity));
            return true;
        }

        /// <summary>The worm's Loot Luck points in this run (% chance of a bonus item per loot roll).</summary>
        public static double LootLuck(ContentDb c, ActiveRun run) => BattleSimulator.FinalStats(WormSpec(c, run), c).Value(StatId.LootLuck);

        /// <summary>
        /// Adds reward chests (events, Treasure Pods) to the pending loot; each raises Danger. A chest is one loot roll plus
        /// RunTuning.RewardChestCompletionPct of the level's completion rewards in bulk resources (playtest 0.1.325: an event's
        /// payout scales with the level like the run's own, so a good event is a notable share of the run).
        /// </summary>
        private static void AddLoot(ContentDb c, ActiveRun run, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int start = run.PendingLoot.Count;
                RollLoot(c, run, true);
                foreach (var r in RewardChestBundle(c, Level(c, run)))
                {
                    // one line per resource per chest: the bundle joins the roll's own line of that resource
                    var same = run.PendingLoot.FindIndex(start, p => p.Kind == RewardKinds.Currency && p.Id == r.Id);
                    if (same >= 0) run.PendingLoot[same].Amount += r.Amount; else run.PendingLoot.Add(r);
                }
            }
        }

        /// <summary>The bulk resources one reward chest adds on the level (its share of the level's completion rewards, rounded).</summary>
        public static List<Reward> RewardChestBundle(ContentDb c, LevelDef level)
        {
            var list = new List<Reward>();
            if (level == null) return list;
            var t = c.RunTuning;
            foreach (var r in level.CompletionRewards)
            {
                if (r.Kind != RewardKinds.Currency || !t.RewardChestCurrencies.Contains(r.Id)) continue;
                long amount = NiceAmount(r.Amount * t.RewardChestCompletionPct / 100.0);
                if (amount > 0) list.Add(Reward.Cur(r.Id, amount));
            }
            return list;
        }

        /// <summary>Round numbers for shown amounts: whole below 10, steps of 5 below 100, 10 below 1 000, 50 below 10 000, then 500.</summary>
        public static long NiceAmount(double x)
        {
            if (x < 0.5) return 0;
            double step = x < 10 ? 1 : x < 100 ? 5 : x < 1000 ? 10 : x < 10000 ? 50 : 500;
            return (long)(Math.Round(x / step) * step);
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

        /// <summary>
        /// Adds a skill to the run (replacing its UpgradesFrom) and applies its OnRunStart effects; an owned skill levels up
        /// instead (up to RunTuning.MaxSkillLevel, its own numbers grow: <see cref="SkillLeveling"/>; its OnRunStart effects add the
        /// difference). Source: see RunGain.
        /// </summary>
        private static bool GrantSkill(CommandContext ctx, ActiveRun run, string skillId, int depth, string source)
        {
            var c = ctx.Content;
            var s = c.Skill(skillId);
            if (s == null || depth > 3) return false;
            if (s.Signature) return false; // signatures come only from the run-start pick and level only by awakening
            if (run.Skills.Contains(skillId))
            {
                int lv = RunDraft.Level(run, skillId);
                if (lv >= c.RunTuning.MaxSkillLevelOf(s.Rarity)) return false;
                run.SkillLevels[skillId] = lv + 1;
                run.Gains.Add(new RunGain(source, skillId, lv + 1));
                ApplyRunStartLevelDelta(ctx, run, s, lv, lv + 1);
                ctx.Emit("run_skill_level", skillId, (lv + 1).ToString());
                RefreshMaxHp(c, run);
                return true;
            }
            if (s.UpgradesFrom != null && run.Skills.Remove(s.UpgradesFrom))
            {
                run.ReplacedSkills.Add(s.UpgradesFrom);
                run.SkillLevels.Remove(s.UpgradesFrom);
            }
            run.Skills.Add(skillId);
            run.Gains.Add(new RunGain(source, skillId, 1));
            ctx.Emit("run_skill_gained", skillId);
            foreach (var t in s.Triggers)
                if (t.When == TriggerKind.OnRunStart) ApplyRunTrigger(ctx, run, t, null, depth + 1);
            UpdateSignature(ctx, run);
            RefreshMaxHp(c, run);
            return true;
        }

        /// <summary>Fires run-level triggers of all active worm skills (see docs/design/skill-conventions.md).</summary>
        private static void RunTriggers(CommandContext ctx, ActiveRun run, TriggerKind when, TileDef tile)
        {
            var c = ctx.Content;
            foreach (var id in ActiveSkillIds(run))
            {
                var s = RunSkill(c, run, id);
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
                    GrantRandomSkill(ctx, run, min, depth, "boon");
                    return;
                }
                string sid = p.StartsWith("skill:", StringComparison.Ordinal) ? p.Substring(6) : p;
                if (c.Skill(sid) != null) { GrantSkill(ctx, run, sid, depth, "boon"); return; }
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
                    AddCoins(run, (long)t.Value);
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
                case EffectKind.SkipNegativeEvent:
                    run.SkipNegativeEvents += (int)t.Value;
                    break;
                case EffectKind.StartRage: // Ambush Instinct: the battle this tile starts begins with rage
                    run.PendingStartRage = Math.Max(run.PendingStartRage, t.Value);
                    break;
            }
        }

        /// <summary>A skill as the run uses it: run skills at their level (SkillLeveling), meta skills as defined.</summary>
        public static SkillDef RunSkill(ContentDb c, ActiveRun run, string skillId)
        {
            var def = c.Skill(skillId);
            int lv = run != null && run.SkillLevels.TryGetValue(skillId, out var l) ? l : 1;
            return lv > 1 && run.Skills.Contains(skillId) ? SkillLeveling.At(c, def, lv) : def;
        }

        /// <summary>
        /// A level-up of a skill with OnRunStart effects (one-time run effects: awakening slow, loot %, coins, run stats) adds the
        /// difference between the two levels, so a leveled boon is worth exactly its leveled numbers.
        /// </summary>
        private static void ApplyRunStartLevelDelta(CommandContext ctx, ActiveRun run, SkillDef def, int from, int to)
        {
            var c = ctx.Content;
            var a = SkillLeveling.At(c, def, from);
            var b = SkillLeveling.At(c, def, to);
            for (int i = 0; i < def.Triggers.Count; i++)
            {
                var ta = a.Triggers[i]; var tb = b.Triggers[i];
                if (ta.When != TriggerKind.OnRunStart || ta.Param != null) continue;
                switch (ta.Effect)
                {
                    case EffectKind.SlowAwakening: run.AwakeningReductionPct += tb.Value - ta.Value; break;
                    case EffectKind.GainRunCoins: AddCoins(run, (long)Math.Round(tb.Value - ta.Value)); break;
                    case EffectKind.RunStatGrowth:
                        if (ta.Stat != null) run.RunStats.Add(new StatMod(ta.Stat.Stat, tb.Stat.Flat - ta.Stat.Flat, tb.Stat.Percent - ta.Stat.Percent));
                        break;
                }
            }
        }

        private static void GrantRandomSkill(CommandContext ctx, ActiveRun run, Rarity min, int depth, string source)
        {
            var pool = RunDraft.Eligible(ctx.Content, run);
            // a new skill when one is left (owned skills would only level up)
            var fresh = pool.FindAll(s => !run.Skills.Contains(s.Id));
            if (fresh.Count > 0) pool = fresh;
            var cands = pool.FindAll(s => s.Rarity >= min);
            if (cands.Count == 0) cands = pool;
            if (cands.Count == 0) { GainCoins(run, ctx.Content.RunTuning.DraftEmptyCoins); return; }
            GrantSkill(ctx, run, cands[run.Rng.Range(0, cands.Count)].Id, depth, source);
        }

        /// <summary>Skills and perks gained this run (every new skill, skill level and stat perk; HUD "Skills n").</summary>
        public static int BuildSize(ActiveRun run) => run == null ? 0 : run.Gains.Count;

        /// <summary>Mythic choice after a beaten miniboss (RunTuning.MythicChoices Mythic skills; coins if none is left).</summary>
        private static void OpenMythicDraft(CommandContext ctx, ActiveRun run)
        {
            var offer = RunDraft.GenerateMythic(ctx.Content, run, run.Rng);
            MaybeOfferSignature(ctx, run, offer);
            if (offer.Count == 0)
            {
                run.PendingMythicDrafts--;
                GainCoins(run, ctx.Content.RunTuning.MythicEmptyCoins);
                return;
            }
            run.DraftOffer.Clear();
            run.DraftOffer.AddRange(offer);
            run.MythicDraft = true;
            run.Phase = RunPhase.Draft;
            ctx.Emit("run_mythic_draft", string.Join(",", offer));
        }

        private static void OpenDraft(CommandContext ctx, ActiveRun run)
        {
            var offer = RunDraft.Generate(ctx.Content, run, run.Rng);
            if (offer.Count == 0)
            {
                run.PendingDrafts--;
                GainCoins(run, ctx.Content.RunTuning.DraftEmptyCoins);
                return;
            }
            run.DraftOffer.Clear();
            run.DraftOffer.AddRange(offer);
            run.MythicDraft = false;
            run.Phase = RunPhase.Draft;
            ctx.Emit("run_draft", string.Join(",", offer));
        }

        // ---- events ---------------------------------------------------------------------------

        /// <summary>The run may still offer an account-loot opportunity (RunTuning.MaxAccountLootOffersPerRun).</summary>
        public static bool AccountLootOfferLeft(ContentDb c, ActiveRun run) => run.AccountLootOffers < c.RunTuning.MaxAccountLootOffersPerRun;

        private static bool IsAccountLootEvent(ContentDb c, string eventId) =>
            eventId != null && c.RunEvents.TryGetValue(eventId, out var ev) && ev.GrantsAccountLoot();

        /// <summary>The rare Resonance event: pick 1 of 2 unowned signatures, or take coins (skill redesign, several signatures).</summary>
        public const string ResonanceEvent = "resonance";

        private static string PickRandomEvent(ContentDb c, ActiveRun run)
        {
            bool lootLeft = AccountLootOfferLeft(c, run);
            var w = new double[c.RandomRunEvents.Count];
            bool sigLeft = FindableSignatures(c, run).Count > 0;
            for (int i = 0; i < w.Length; i++)
                w[i] = !lootLeft && IsAccountLootEvent(c, c.RandomRunEvents[i].Id) ? 0
                    : !sigLeft && c.RandomRunEvents[i].Id == ResonanceEvent ? 0 : c.RandomRunEvents[i].Weight;
            int idx = run.Rng.PickWeighted(w);
            return idx < 0 ? null : c.RandomRunEvents[idx].Id;
        }

        private static void OpenEvent(CommandContext ctx, ActiveRun run, string eventId, RunTile tile)
        {
            var c = ctx.Content;
            if (eventId == null || !c.RunEvents.TryGetValue(eventId, out var ev)) { Consume(c, run, tile, TileDefOf(c, tile)); return; }
            if (ev.Negative && TrySkipNegative(ctx, run))
            {
                // shown, not silently skipped: the result screen says the boon kept it away
                Consume(c, run, tile, TileDefOf(c, tile));
                var shielded = new RunOutcome { Source = OutcomeSource(c, ev.Id, TileDefOf(c, tile)), EventId = ev.Id, Title = ev.Name, Choice = "Avoided", Text = "Your boon shields you from misfortune.",
                    Show = OutcomeShow.Popups, Toast = "Boon: " + ev.Name + " avoided" };
                PublishOutcome(ctx, run, shielded);
                return;
            }
            if (ev.GrantsAccountLoot())
            {
                // Account loot is rare: a few risky opportunities per run. Chests ahead become supply caches once they are
                // used up (RetireTreasures); an account-loot event drawn after that shows that it is empty.
                if (!AccountLootOfferLeft(c, run))
                {
                    ctx.Emit("run_cache_empty", eventId);
                    Consume(c, run, tile, TileDefOf(c, tile));
                    var empty = new RunOutcome { Source = OutcomeSource(c, ev.Id, TileDefOf(c, tile)), EventId = ev.Id, Title = ev.Name, Choice = "Empty",
                        Text = "Already looted — this run's " + c.RunTuning.MaxAccountLootOffersPerRun + " account-loot chances are used up.",
                        Show = OutcomeShow.Popups, Toast = "Already looted" };
                    PublishOutcome(ctx, run, empty);
                    return;
                }
                run.AccountLootOffers++;
                if (!AccountLootOfferLeft(c, run)) RetireTreasures(c, run);
            }
            if (eventId == ResonanceEvent)
            {
                // Resonance: two unowned signatures (or coins); without any to find it is an empty site
                run.SignatureChoice.Clear();
                run.SignatureChoice.AddRange(DrawSignatures(c, run, 2));
                if (run.SignatureChoice.Count == 0) { Consume(c, run, tile, TileDefOf(c, tile)); return; }
            }
            run.Phase = RunPhase.Event;
            run.EventId = eventId;
            run.EventTile = tile.Id;
            ctx.Emit("run_event", eventId);
        }

        private static bool CanAffordOption(ActiveRun run, EventOption o) => run.Coins >= o.CoinCost && run.Shards >= o.ShardCost;

        /// <summary>
        /// Why an option would do nothing now, else null: "HP already full" for a pure heal (campfire Rest, the medic's tonic)
        /// at full HP. The client shows it on the button instead of resolving to nothing (event outcome rules).
        /// </summary>
        public static string OptionUselessReason(ActiveRun run, EventOption o)
        {
            if (run == null || o == null || o.Outcomes.Count != 1) return null;
            bool heal = false;
            foreach (var fx in o.Outcomes[0].Effects)
            {
                if (fx.Kind == RunEffectKind.HealPct) heal = true;
                else if (fx.Kind != RunEffectKind.StrengthenEnemies) return null;
            }
            return heal && run.WormMaxHp > 0 && run.WormHp >= run.WormMaxHp - 0.5 ? "HP already full" : null;
        }

        private static void ChooseEventOption(CommandContext ctx, ActiveRun run, int index, string trainSkill = null)
        {
            var c = ctx.Content;
            Require(run.Phase == RunPhase.Event && run.EventId != null, "no event open");
            var ev = c.RunEvents[run.EventId];
            Require(index >= 0 && index < ev.Options.Count, "invalid option");
            var opt = ev.Options[index];
            Require(CanAffordOption(run, opt), "cannot afford this option");
            foreach (var oc in opt.Outcomes)
                foreach (var fx in oc.Effects)
                    if (fx.Kind == RunEffectKind.GainSignature)
                        Require(int.TryParse(fx.Param, out var si) && si >= 0 && si < run.SignatureChoice.Count, "no such signature offered");
            if (!string.IsNullOrEmpty(trainSkill))
            {
                Require(ImprovesSkill(opt), "this option improves no skill");
                Require(TrainChoices(c, run).Contains(trainSkill), "skill cannot be improved");
            }
            var before = Snap.WithTiles(run);
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
            bool shielded = false;
            if (outcome != null)
            {
                if (IsNegativeOutcome(outcome) && TrySkipNegative(ctx, run)) { run.LastEventText = "Your boon shields you from misfortune."; shielded = true; }
                else
                {
                    ApplyEffects(ctx, run, outcome.Effects, eventTileId, trainSkill);
                }
            }
            result.Text = run.LastEventText;
            before.Diff(ctx.Content, run, result.Lines);
            PresentEvent(run, result, outcome, shielded);
            if (result.Show == OutcomeShow.Revive)
            {
                // a lost fight: not the win text; the revive retries it and publishes the result then (with the follow-ups)
                run.DefeatOutcome = new RunOutcome
                {
                    Source = result.Source, EventId = result.EventId, Title = result.Title, Choice = result.Choice, Text = result.Text,
                    Chance = result.Chance, SuccessChancePct = result.SuccessChancePct, Success = result.Success,
                };
                result.Text = LostFightText(outcome);
            }
            PublishOutcome(ctx, run, result);
            Continue(ctx, run);
        }

        /// <summary>The text of a lost event fight (the revive screen shows it; never the win text).</summary>
        public static string LostFightText(EventOutcome oc)
        {
            int i = oc != null ? oc.Effects.FindIndex(fx => fx.Kind == RunEffectKind.Battle) : -1;
            bool reward = i >= 0 && i < oc.Effects.Count - 1;
            return reward ? "Beaten! Win the retry to claim the reward." : "Beaten! Win the retry to go on.";
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

        private static void ApplyEffects(CommandContext ctx, ActiveRun run, List<RunEffect> effects, int originTile, string trainSkill = null)
        {
            var c = ctx.Content;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                switch (e.Kind)
                {
                    case RunEffectKind.Coins: AddCoins(run, (long)e.Value); break;
                    case RunEffectKind.HealPct: HealWorm(c, run, e.Value); break;
                    case RunEffectKind.DamagePct: DamageWorm(run, e.Value); break;
                    case RunEffectKind.StatPercent: run.RunStats.Add(StatMod.Bonus(e.Stat, e.Value)); RefreshMaxHp(c, run); break;
                    case RunEffectKind.GainSkill: GrantRandomSkill(ctx, run, e.MinRarity, 0, "event"); break;
                    case RunEffectKind.GainSpecificSkill: GrantSkill(ctx, run, e.Param, 0, "event"); break;
                    case RunEffectKind.GainSignature:
                    {
                        int.TryParse(e.Param, out var si);
                        if (si >= 0 && si < run.SignatureChoice.Count && !run.Skills.Contains(run.SignatureChoice[si])
                            && Signatures(run).Count < c.RunTuning.MaxSignatures) AddSignature(ctx, run, run.SignatureChoice[si], "event");
                        run.SignatureChoice.Clear();
                        break;
                    }
                    case RunEffectKind.UpgradeSkill: TrainSkill(ctx, run, trainSkill); trainSkill = null; break;
                    case RunEffectKind.LoseRandomSkill:
                    {
                        var sigs = Signatures(run);
                        var losable = run.Skills.FindAll(id => !sigs.Contains(id) && (c.Skill(id) == null || !c.Skill(id).Signature)); // signatures are never lost
                        if (losable.Count > 0)
                        {
                            string lost = losable[run.Rng.Range(0, losable.Count)];
                            run.SkillLevels.Remove(lost);
                            run.Skills.Remove(lost);
                            UpdateSignature(ctx, run);
                        }
                        RefreshMaxHp(c, run);
                    }
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

        /// <summary>True if one of the option's outcomes improves a skill (campfire Train, shrine): the player picks which.</summary>
        public static bool ImprovesSkill(EventOption o)
        {
            if (o == null) return false;
            foreach (var oc in o.Outcomes)
                foreach (var fx in oc.Effects)
                    if (fx.Kind == RunEffectKind.UpgradeSkill) return true;
            return false;
        }

        /// <summary>
        /// The owned skills a campfire Train (or a shrine) offers (at most RunTuning.TrainChoices, the lowest levels first, the
        /// earliest gained on ties): a skill below its max level
        /// levels up; a chain's lower tier turns into its upgrade (<see cref="TrainTarget"/>). Signatures (they level by
        /// awakening only) and maxed skills are not listed.
        /// </summary>
        public static List<string> TrainChoices(ContentDb c, ActiveRun run)
        {
            var all = new List<string>();
            foreach (var id in run.Skills)
                if (TrainTarget(c, run, id) != null) all.Add(id);
            // at most RunTuning.TrainChoices (run UI glitches #16: 3 cards): the lowest levels first, the earliest gained on ties
            var list = new List<string>();
            int n = Math.Max(1, c.RunTuning.TrainChoices);
            while (list.Count < n && all.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < all.Count; i++) if (RunDraft.Level(run, all[i]) < RunDraft.Level(run, all[best])) best = i;
                list.Add(all[best]);
                all.RemoveAt(best);
            }
            return list;
        }

        /// <summary>What training an owned skill gives: the skill itself one level up, or its chain upgrade; null = cannot be improved.</summary>
        public static string TrainTarget(ContentDb c, ActiveRun run, string ownedId)
        {
            var s = c.Skill(ownedId);
            if (s == null || s.Signature || !run.Skills.Contains(ownedId)) return null;
            if (RunDraft.CanLevelUp(c, run, s)) return ownedId;
            foreach (var id in RunDraft.SortedSkillIds(c))
            {
                var u = c.Skills[id];
                if (u.UpgradesFrom == ownedId && RunDraft.IsEligible(c, run, u)) return id;
            }
            return null;
        }

        /// <summary>The default Train pick (auto mode, bots, a command without a skill): the lowest-level choice, the earliest gained on ties.</summary>
        public static string TrainAutoPick(ContentDb c, ActiveRun run)
        {
            string best = null;
            int bestLv = int.MaxValue;
            foreach (var id in TrainChoices(c, run))
            {
                int lv = RunDraft.Level(run, id);
                if (lv < bestLv) { best = id; bestLv = lv; }
            }
            return best;
        }

        /// <summary>Campfire Train / shrine: improves the chosen owned skill (default: <see cref="TrainAutoPick"/>); nothing to improve = coins.</summary>
        private static void TrainSkill(CommandContext ctx, ActiveRun run, string chosen)
        {
            var c = ctx.Content;
            string owned = !string.IsNullOrEmpty(chosen) ? chosen : TrainAutoPick(c, run);
            string target = owned != null ? TrainTarget(c, run, owned) : null;
            if (target == null) { GainCoins(run, c.RunTuning.DraftEmptyCoins); return; }
            GrantSkill(ctx, run, target, 0, "event");
        }

        // ---- shops ----------------------------------------------------------------------------

        private static ShopTableDef ShopTable(ContentDb c, ActiveRun run)
        {
            var level = Level(c, run);
            return level.ShopTableId != null && c.RunShopTables.TryGetValue(level.ShopTableId, out var t) ? t : c.RunShopTables["shop_standard"];
        }

        private static void OpenShop(CommandContext ctx, ActiveRun run)
        {
            run.ShopRerolls = 0; // rerolls count per visit: each new shop's reroll starts at the base price
            FillShop(ctx.Content, run);
            run.ShopsVisited++;
            run.Phase = RunPhase.Shop;
            ctx.Emit("run_shop");
        }

        /// <summary>
        /// Fills the shop: <see cref="ShopTableDef.SkillOffers"/> skills, then the support offers on top (the heal while hurt, the
        /// stat perks). A reroll (<paramref name="reroll"/>) draws new skills and new stat perks (playtest 0.1.359: the perk stayed
        /// the same; an unsold perk is redrawn, another stat than before when one is left). The heal stays as it is (it is one
        /// fixed offer, a reroll could only draw it again), and a bought heal or perk stays sold.
        /// </summary>
        private static void FillShop(ContentDb c, ActiveRun run, bool reroll = false)
        {
            var table = ShopTable(c, run);
            var kept = reroll ? run.Shop.FindAll(o => o.Kind == ShopOfferKind.Heal || o.Kind == ShopOfferKind.Stat && o.Sold) : null;
            var oldPerks = reroll ? run.Shop.FindAll(o => o.Kind == ShopOfferKind.Stat && !o.Sold) : null;
            run.Shop.Clear();
            var pool = RunDraft.Eligible(c, run);
            long cheapest = CheapestSkillPrice(table);
            for (int i = 0; i < table.SkillOffers && pool.Count > 0; i++)
            {
                // The run's first shop (before any reroll) leads with a skill at the cheapest price: the start coins buy it.
                bool cheapSlot = i == 0 && run.ShopsVisited == 0 && run.ShopRerolls == 0;
                var w = new double[pool.Count];
                for (int k = 0; k < pool.Count; k++)
                {
                    w[k] = RunDraft.OfferWeight(c, run, pool[k], table.RarityWeights.TryGetValue(pool[k].Rarity, out var x) ? x : 1);
                    if (cheapSlot && (table.SkillPrices.TryGetValue(pool[k].Rarity, out var pr) ? pr : 50) > cheapest) w[k] = 0;
                }
                if (cheapSlot && Array.TrueForAll(w, v => v <= 0)) // no cheap skill left in the pool: any skill
                    for (int k = 0; k < pool.Count; k++) w[k] = RunDraft.OfferWeight(c, run, pool[k], table.RarityWeights.TryGetValue(pool[k].Rarity, out var x) ? x : 1);
                int idx = run.Rng.PickWeighted(w);
                if (idx < 0) break;
                var s = pool[idx];
                pool.RemoveAt(idx);
                run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Skill, SkillId = s.Id, Price = table.SkillPrices.TryGetValue(s.Rarity, out var p) ? p : 50 });
            }
            ApplySale(table, run);
            if (kept != null)
            {
                // a reroll: the heal and the sold perks as they were (in their order), the unsold perks drawn again
                foreach (var o in kept) if (o.Kind == ShopOfferKind.Heal) run.Shop.Add(o);
                foreach (var o in kept) if (o.Kind == ShopOfferKind.Stat) run.Shop.Add(o);
                AddStatPerks(table, run, oldPerks.Count, oldPerks);
                return;
            }
            // support offers on top of the skills (playtest 0.1.325): the heal while the worm is hurt, and always the stat perks
            if (run.WormHp < run.WormMaxHp * table.HealOfferBelowPct / 100.0)
                run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Heal, Value = table.HealPct, Price = table.HealPrice });
            AddStatPerks(table, run, table.StatPerkOffers, null);
        }

        /// <summary>
        /// Draws <paramref name="count"/> stat perks from the table, never one the shop already shows; a reroll leaves out the
        /// perks it replaces (<paramref name="replaced"/>) while other stats are left, so the reroll visibly changes the perk.
        /// </summary>
        private static void AddStatPerks(ShopTableDef table, ActiveRun run, int count, List<ShopOffer> replaced)
        {
            bool Same(StatMod a, StatMod b) => a.Stat == b.Stat && a.Flat == b.Flat && a.Percent == b.Percent;
            var stats = new List<StatMod>(table.StatOffers);
            stats.RemoveAll(m => run.Shop.Exists(o => o.Kind == ShopOfferKind.Stat && o.Stat != null && Same(o.Stat, m)));
            if (replaced != null && stats.Count > count)
            {
                var fresh = stats.FindAll(m => !replaced.Exists(o => o.Stat != null && Same(o.Stat, m)));
                if (fresh.Count >= count) stats = fresh;
            }
            for (int i = 0; i < count && stats.Count > 0; i++)
            {
                int k = run.Rng.Range(0, stats.Count);
                var m = stats[k];
                stats.RemoveAt(k);
                run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Stat, Stat = new StatMod(m.Stat, m.Flat, m.Percent), Value = m.Percent != 0 ? m.Percent : m.Flat, Price = table.StatPrice });
            }
        }

        /// <summary>
        /// SALE: with <see cref="ShopTableDef.SaleChancePct"/> one of the fill's skill offers costs <see cref="ShopTableDef.SalePct"/>
        /// less (ListPrice keeps the old price). Drawn from a forked stream, so the run's own random stream is untouched.
        /// </summary>
        private static void ApplySale(ShopTableDef table, ActiveRun run)
        {
            if (table.SaleChancePct <= 0 || run.Shop.Count == 0) return;
            var rng = run.Rng.Fork("shop_sale:" + run.ShopsVisited + ":" + run.ShopRerolls);
            if (rng.NextDouble() * 100 >= table.SaleChancePct) return;
            var o = run.Shop[rng.Range(0, run.Shop.Count)];
            o.ListPrice = o.Price;
            o.Price = Math.Max(1, (long)Math.Round(o.Price * (1 - table.SalePct / 100)));
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
                AccountLootOffers = run.AccountLootOffers, AccountLootDrops = run.PendingLoot.Count, XpTotal = run.XpTotal,
                Gains = new List<RunGain>(run.Gains), Minibosses = run.MinibossesDefeated, ShopBuys = run.ShopBuys };
            if (won)
            {
                double modeMult = level.RewardMultiplier * (run.Mode == RunMode.Overcharged ? portal.OverchargedRewardMultiplier : 1);
                double biomassMult = BiomassSurge(ctx, run, summary);
                var rewards = new List<Reward>();
                var sources = new List<string>();
                foreach (var r in run.PendingLoot) { rewards.Add(Scaled(r, modeMult * (r.Id == Currencies.Biomass ? biomassMult : 1))); sources.Add(RunRewardLine.RunLoot); }
                if (run.Mode != RunMode.Burrow)
                {
                    RollRareDrops(level, run);
                    foreach (var r in level.CompletionRewards) { rewards.Add(Scaled(r, modeMult * (r.Id == Currencies.Biomass ? biomassMult : 1))); sources.Add(RunRewardLine.Completion); }
                    var before = run.PendingLoot.Count;
                    for (int i = 0; i < level.CompletionLootRolls; i++) RollLoot(c, run, false);
                    for (int i = before; i < run.PendingLoot.Count; i++)
                    {
                        rewards.Add(Scaled(run.PendingLoot[i], modeMult * (run.PendingLoot[i].Id == Currencies.Biomass ? biomassMult : 1)));
                        sources.Add(RunRewardLine.CompletionRoll);
                    }
                }

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
                        foreach (var r in level.FirstClearRewards) { rewards.Add(new Reward(r.Kind, r.Id, r.Amount, r.Rarity)); sources.Add(RunRewardLine.Milestone); }
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
                        sources.Add(RunRewardLine.Feat);
                    }
                }
                for (int i = 0; i < rewards.Count; i++) GrantRunReward(ctx, rewards[i], summary, sources[i]);
                foreach (var r in run.PendingRare)
                {
                    if (!ctx.Grant(r)) continue; // no use yet: stashed (RewardGate), not shown as granted
                    summary.Granted.Add(new Reward(r.Kind, r.Id, r.Amount, r.Rarity));
                    summary.Rewards.Add(new RunRewardLine { Reward = new Reward(r.Kind, r.Id, r.Amount, r.Rarity), Source = RunRewardLine.RareDrop, Doublable = RunRewardLine.IsDoublable(RunRewardLine.RareDrop) });
                    if (RunRewardLine.IsDoublable(RunRewardLine.RareDrop)) summary.Doublable.Add(new Reward(r.Kind, r.Id, r.Amount, r.Rarity));
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
            summary.LongestRolls = RecordRolls(s, run.LevelId, run.Rolls);
            run.Phase = won ? RunPhase.Completed : RunPhase.Failed;
            s.Run.LastSummary = summary;
            s.Run.Active = null;
        }

        /// <summary>Levels whose longest run is kept (the most recently finished ones).</summary>
        public const int LongestRollsKept = 20;

        /// <summary>The most rolls any finished run on this level lasted (0 = none kept).</summary>
        public static int LongestRolls(GameState s, string levelId)
        {
            var e = s.Run.LongestRolls.Find(x => x.LevelId == levelId);
            return e != null ? e.Rolls : 0;
        }

        private static int RecordRolls(GameState s, string levelId, int rolls)
        {
            var list = s.Run.LongestRolls;
            int i = list.FindIndex(x => x.LevelId == levelId);
            var e = i >= 0 ? list[i] : new LevelRolls { LevelId = levelId };
            if (i >= 0) list.RemoveAt(i);
            e.Rolls = Math.Max(e.Rolls, rolls);
            list.Add(e);
            while (list.Count > LongestRollsKept) list.RemoveAt(0);
            return e.Rolls;
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
        private static void GrantRunReward(CommandContext ctx, Reward r, RunSummary summary, string source)
        {
            bool doublable = RunRewardLine.IsDoublable(source);
            var reward = new Reward(r.Kind, r.Id, r.Amount, r.Rarity);
            if (reward.Kind == RewardKinds.Currency)
            {
                double m = ModifierMath.Multiplier(ctx.State.Modifiers, "run_reward:" + reward.Id, ctx.NowMs);
                reward.Amount = Math.Max(0, (long)Math.Round(reward.Amount * m));
            }
            if (reward.Amount <= 0) return;
            if (!ctx.Grant(reward)) return; // no use yet: stashed (RewardGate), not shown as granted (nor doubled)
            // separate copies: Granted totals grow when the rewards are doubled, Doublable / Rewards keep the single amounts
            summary.Granted.Add(new Reward(reward.Kind, reward.Id, reward.Amount, reward.Rarity));
            summary.Rewards.Add(new RunRewardLine { Reward = new Reward(reward.Kind, reward.Id, reward.Amount, reward.Rarity), Source = source, Doublable = doublable });
            if (doublable) summary.Doublable.Add(new Reward(reward.Kind, reward.Id, reward.Amount, reward.Rarity));
        }
    }
}
