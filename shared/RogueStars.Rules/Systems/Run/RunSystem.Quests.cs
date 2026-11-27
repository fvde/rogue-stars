using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    // Run Quests (docs/design/run-quests/recipe.md): a rare character on the board (QuestTuning.ChancePct: a plain 20% roll per
    // eligible run, at most one per run) who tells a ~30-encounter story. Scheduled at run start (ScheduleQuest), placed by
    // RunBoard on a trunk tile (forced stop), resolved with RunQuestChoose. Battles and sacrifices that are declined or not
    // affordable keep the story on the same node; a lost quest battle is the normal defeat (revive or the run ends; the story
    // waits). Boons add account-wide run coins on every coin gain (GainCoins). Finished chains keep visiting with friendly
    // epilogue scenes.
    public static partial class RunSystem
    {
        private static void RegisterQuests(GameEngine e)
        {
            e.Register<RunQuestChoose>((c, ctx) => { var r = Need(ctx); QuestChoose(ctx, r, c.Index); return Ok(ctx); });
        }

        // ---- queries (client, server, simulator) ----------------------------------------------------------------

        /// <summary>A chain's progress (null before the first encounter).</summary>
        public static QuestProgress QuestProgressOf(GameState s, string questId) =>
            questId != null && s.RunQuests.Quests.TryGetValue(questId, out var p) ? p : null;

        /// <summary>The node a chain waits on (its start node before the first encounter, null once completed).</summary>
        public static QuestNodeDef QuestCurrentNode(ContentDb c, GameState s, RunQuestDef q)
        {
            var p = QuestProgressOf(s, q.Id);
            if (p != null && p.Completed) return null;
            return q.Node(p != null && p.Node != null ? p.Node : q.StartNode);
        }

        /// <summary>Account-wide bonus: + this many run coins on every run-coin gain (the sum of every chain's boons).</summary>
        public static int QuestCoinBonus(GameState s)
        {
            int sum = 0;
            foreach (var p in s.RunQuests.Quests.Values) sum += p.CoinBonus;
            return sum;
        }

        /// <summary>Display name of the account coin bonus ("Starglint"; "Quest bonus" when several chains add to it).</summary>
        public static string QuestBonusName(ContentDb c, GameState s)
        {
            string name = null;
            foreach (var q in c.RunQuestOrder)
            {
                var p = QuestProgressOf(s, q.Id);
                if (p == null || p.CoinBonus <= 0) continue;
                if (name != null) return "Quest bonus";
                name = q.BonusName;
            }
            return name ?? (c.RunQuestOrder.Count > 0 ? c.RunQuestOrder[0].BonusName : "Quest bonus");
        }

        /// <summary>
        /// The run may meet a quest character: a chain exists, not the Burrow and not the tutorial run (main level 1, its own
        /// small tile pool). No level gate: from the second run of the game on.
        /// </summary>
        public static bool QuestEligible(ContentDb c, RunMode mode, LevelDef level) =>
            mode != RunMode.Burrow && c.RunQuestOrder.Count > 0 && level != null && level.TilePoolId != c.Campaign.TutorialPool;

        /// <summary>The chain the next encounter belongs to: the first unfinished one, else (epilogues) the one seen least.</summary>
        public static RunQuestDef QuestNext(ContentDb c, GameState s)
        {
            RunQuestDef best = null; int bestSeen = int.MaxValue;
            foreach (var q in c.RunQuestOrder)
            {
                var p = QuestProgressOf(s, q.Id);
                if (p == null || !p.Completed) return q;
                if (q.Epilogues.Count == 0) continue;
                if (p.EpiloguesSeen < bestSeen) { best = q; bestSeen = p.EpiloguesSeen; }
            }
            return best;
        }

        /// <summary>The chapter the chain is at (1-based; Chapters + 1 once completed).</summary>
        public static int QuestChapter(ContentDb c, GameState s, RunQuestDef q)
        {
            var n = QuestCurrentNode(c, s, q);
            return n != null ? n.Chapter : q.Chapters + 1;
        }

        /// <summary>The open quest node of the run (null if the phase is not Quest or it is an epilogue scene).</summary>
        public static QuestNodeDef QuestOpenNode(ContentDb c, GameState s, ActiveRun run)
        {
            if (run == null || run.Phase != RunPhase.Quest || run.Quest.Epilogue) return null;
            var q = c.RunQuest(run.Quest.QuestId);
            return q != null ? QuestCurrentNode(c, s, q) : null;
        }

        /// <summary>The open epilogue scene of the run, or null.</summary>
        public static QuestEpilogueDef QuestOpenEpilogue(ContentDb c, ActiveRun run)
        {
            if (run == null || run.Phase != RunPhase.Quest || !run.Quest.Epilogue) return null;
            var q = c.RunQuest(run.Quest.QuestId);
            return q?.Epilogues.Find(e => e.Id == run.Quest.EpilogueId);
        }

        /// <summary>The owned run skills a skill sacrifice may take at random (its tier; signatures never), in the order gained.</summary>
        public static List<string> QuestSkillCandidates(ContentDb c, ActiveRun run, QuestCostKind kind)
        {
            var list = new List<string>();
            if (run == null) return list;
            Rarity tier = kind == QuestCostKind.MythicSkill ? Rarity.Mythic : Rarity.Legendary;
            var sigs = Signatures(run);
            foreach (var id in run.Skills)
            {
                var s = c.Skill(id);
                if (s == null || s.Signature || sigs.Contains(id)) continue;
                if (RunTuning.RunTier(s.Rarity) == tier) list.Add(id);
            }
            return list;
        }

        /// <summary>The run skills "forget all" removes (every run skill but the signatures).</summary>
        public static List<string> QuestForgettable(ContentDb c, ActiveRun run)
        {
            var sigs = Signatures(run);
            return run.Skills.FindAll(id => !sigs.Contains(id) && (c.Skill(id) == null || !c.Skill(id).Signature));
        }

        /// <summary>Whether the run can pay a sacrifice now (coins, an owned skill of the tier, a skill to forget...).</summary>
        public static bool QuestCanPay(ContentDb c, ActiveRun run, QuestCostDef cost)
        {
            if (run == null) return false;
            if (cost == null) return true;
            switch (cost.Kind)
            {
                case QuestCostKind.None: return true;
                case QuestCostKind.Coins: return run.Coins >= cost.Amount;
                case QuestCostKind.LegendarySkill:
                case QuestCostKind.MythicSkill: return QuestSkillCandidates(c, run, cost.Kind).Count > 0;
                case QuestCostKind.CurrentHpPct: return run.WormHp > 1;
                case QuestCostKind.AllRunSkills: return QuestForgettable(c, run).Count > 0;
                default: return false;
            }
        }

        /// <summary>The option of the open quest node or scene a farming auto step takes: advance on story / boon, wait on battles and prices.</summary>
        public static int QuestFarmPick(ContentDb c, GameState s, ActiveRun run)
        {
            var node = QuestOpenNode(c, s, run);
            if (node == null) return 0;
            for (int i = 0; i < node.Options.Count; i++)
            {
                var a = node.Options[i].Action;
                if (node.Kind == QuestNodeKind.Story || node.Kind == QuestNodeKind.Boon ? a == QuestOptionAction.Advance : a == QuestOptionAction.Wait) return i;
            }
            return 0;
        }

        // ---- run start: scheduling --------------------------------------------------------------------------------

        /// <summary>
        /// At run start (before the board): decides whether this run meets a quest character: a plain, independent
        /// QuestTuning.ChancePct roll (no pity) from a fork of the run stream, so the rest of the run is unchanged. Met: RunBoard
        /// places the tile.
        /// </summary>
        private static void ScheduleQuest(CommandContext ctx, ActiveRun run, LevelDef level)
        {
            var s = ctx.State; var c = ctx.Content;
            run.Quest = new RunQuestRun { CoinBonus = QuestCoinBonus(s), BonusName = QuestBonusName(c, s) };
            if (!QuestEligible(c, run.Mode, level)) return;
            var q = QuestNext(c, s);
            if (q == null) return;
            double pct = c.QuestTuning.ChancePct;
            var rng = run.Rng.Fork("quest");
            if (!(pct >= 100 || (pct > 0 && rng.NextDouble() * 100 < pct))) return;
            var p = QuestProgressOf(s, q.Id);
            run.Quest.QuestId = q.Id;
            if (p != null && p.Completed)
            {
                run.Quest.Epilogue = true;
                run.Quest.EpilogueId = q.Epilogues[p.EpiloguesSeen % q.Epilogues.Count].Id;
            }
            ctx.Emit("run_quest_scheduled", q.Id, run.Quest.Epilogue ? "epilogue" : "story");
        }

        /// <summary>
        /// RunBoard.AppendSegment (after the pool fill): on a scheduled run, the quest tile replaces a pool tile on the trunk of segment
        /// QuestTuning.Segment (every path passes it; it forces a stop), as close to SegmentAt of the segment's path as possible.
        /// Replacing (not reserving) keeps every random pick of the board as it would be without the quest.
        /// </summary>
        public static void PlaceQuestTile(ContentDb c, ActiveRun run, int segment, List<RunTile> path, HashSet<int> onTrunk)
        {
            if (run == null || run.Quest == null || run.Quest.QuestId == null || run.Quest.Placed || segment < c.QuestTuning.Segment) return;
            int last = path.Count - 1;
            if (last < 0) return;
            int want = Math.Max(0, Math.Min(last, (int)Math.Round(c.QuestTuning.SegmentAt * last)));
            for (int d = 0; d <= last; d++)
                foreach (int idx in new[] { want + d, want - d })
                    if (idx >= 0 && idx <= last && onTrunk.Contains(path[idx].Id) && !path[idx].Fixed && path[idx].TileId != null)
                    {
                        path[idx].TileId = ContentDb.QuestTileId;
                        path[idx].Fixed = true;
                        run.Quest.Placed = true;
                        run.Quest.Tile = path[idx].Id;
                        return;
                    }
        }

        // ---- the encounter ----------------------------------------------------------------------------------------

        /// <summary>Landing on (or stopping at) the quest tile: the encounter opens (phase Quest; auto mode pauses).</summary>
        private static void OpenQuest(CommandContext ctx, ActiveRun run, RunTile tile)
        {
            var s = ctx.State; var c = ctx.Content;
            var q = c.RunQuest(run.Quest.QuestId);
            if (q == null || run.Quest.Met) { Consume(c, run, tile, TileDefOf(c, tile)); return; }
            run.Quest.Met = true;
            run.Quest.Tile = tile.Id;
            if (!s.RunQuests.Quests.TryGetValue(q.Id, out var p)) { p = new QuestProgress { Node = q.StartNode }; s.RunQuests.Quests[q.Id] = p; }
            p.Encounters++;
            s.RunQuests.Encounters++;
            s.Count("run_quest_encounters");
            run.Phase = RunPhase.Quest;
            ctx.Emit("run_quest", q.Id, run.Quest.Epilogue ? "epilogue:" + run.Quest.EpilogueId : p.Node ?? "");
        }

        private static void QuestChoose(CommandContext ctx, ActiveRun run, int index)
        {
            var s = ctx.State; var c = ctx.Content;
            Require(run.Phase == RunPhase.Quest, "no quest encounter open");
            var q = c.RunQuest(run.Quest.QuestId);
            Require(q != null, "unknown quest");
            var p = QuestProgressOf(s, q.Id);
            Require(p != null, "quest not met");
            var before = Snap.Of(run);
            var tile = run.Quest.Tile >= 0 && run.Quest.Tile < run.Tiles.Count ? run.Tiles[run.Quest.Tile] : null;
            var outcome = new RunOutcome { Source = "quest", EventId = q.Id, Tile = run.Quest.Tile, TileId = ContentDb.QuestTileId };

            if (run.Quest.Epilogue)
            {
                var ep = QuestOpenEpilogue(c, run);
                Require(ep != null, "unknown scene");
                Require(index == 0, "invalid option");
                outcome.Title = ep.Title; outcome.Choice = ep.Option; outcome.Text = q.Character + " dropped by to say hello.";
                p.EpiloguesSeen++;
                s.Count("run_quest_epilogues");
                run.Phase = RunPhase.AwaitRoll;
                if (tile != null) Consume(c, run, tile, TileDefOf(c, tile));
                switch (ep.Kind)
                {
                    case QuestEpilogueKind.Coins: GainCoins(run, (long)ep.Amount); break;
                    case QuestEpilogueKind.HealPct: HealWorm(c, run, ep.Amount); break;
                    case QuestEpilogueKind.SkillPick: run.PendingDrafts += Math.Max(1, (int)ep.Amount); break; // a free level-up draft (Continue opens it)
                    case QuestEpilogueKind.MaxHpPct:
                        run.RunStats.Add(StatMod.Bonus(StatId.Hp, ep.Amount));
                        run.Gains.Add(new RunGain { Source = "event", Stat = StatMod.Bonus(StatId.Hp, ep.Amount) });
                        RefreshMaxHp(c, run);
                        break;
                }
                ctx.Emit("run_quest_epilogue", q.Id, ep.Id);
                before.Diff(c, run, outcome.Lines);
                if (ep.Kind == QuestEpilogueKind.SkillPick) outcome.Lines.Insert(0, new OutcomeLine(OutcomeKind.SkillRoll, "Common", c.RunTuning.DraftChoices, true));
                PublishOutcome(ctx, run, outcome);
                Continue(ctx, run);
                return;
            }

            var node = q.Node(p.Node ?? q.StartNode);
            Require(node != null, "quest node missing");
            Require(index >= 0 && index < node.Options.Count, "invalid option");
            var opt = node.Options[index];
            outcome.Title = node.Title;
            outcome.Choice = opt.Text;
            bool advance;
            switch (opt.Action)
            {
                case QuestOptionAction.Pay:
                {
                    Require(QuestCanPay(c, run, node.Cost), "cannot pay this price");
                    QuestPay(ctx, run, node.Cost);
                    advance = true;
                    break;
                }
                case QuestOptionAction.Fight:
                {
                    var enemies = QuestEnemies(c, run, node.Battle ?? new QuestBattleDef());
                    RewardsFor(c, enemies, 3, out int xp, out long coins);
                    run.Phase = RunPhase.AwaitRoll; // a win continues the run (drafts from the level-up open next)
                    advance = Fight(ctx, run, enemies, "quest", xp, coins, null);
                    if (!advance)
                    {
                        // lost: the normal defeat (Fight set phase Defeated: revive or the run ends); the story waits on this node.
                        // A revive retries the battle and, if won, moves the story on (QuestBattleWonAfterRevive).
                        s.Count("run_quest_battles_lost");
                        p.Waits++;
                        outcome.Text = "Beaten! " + q.Character + " will wait for you. The story goes on next time you meet.";
                        outcome.Lines.Add(new OutcomeLine(OutcomeKind.QuestStep, "waits", node.Chapter, false));
                        ctx.Emit("run_quest_choice", q.Id, node.Id, index.ToString(), "lost");
                        before.Diff(c, run, outcome.Lines);
                        PublishOutcome(ctx, run, outcome);
                        return;
                    }
                    s.Count("run_quest_battles_won");
                    break;
                }
                case QuestOptionAction.Wait: advance = false; break;
                default: advance = true; break;
            }

            if (advance) QuestAdvance(ctx, run, q, p, node, opt, outcome);
            else
            {
                p.Waits++;
                outcome.Text = q.Character + " will wait for you. The story goes on next time you meet.";
                outcome.Lines.Add(new OutcomeLine(OutcomeKind.QuestStep, "waits", node.Chapter, false));
            }
            ctx.Emit("run_quest_choice", q.Id, node.Id, index.ToString(), advance ? "advanced" : "waits");
            if (tile != null) Consume(c, run, tile, TileDefOf(c, tile));
            before.Diff(c, run, outcome.Lines);
            run.Phase = RunPhase.AwaitRoll;
            PublishOutcome(ctx, run, outcome);
            Continue(ctx, run);
        }

        /// <summary>The story moves on with <paramref name="opt"/>: boon, journal line, next node or the end of the chain.</summary>
        private static void QuestAdvance(CommandContext ctx, ActiveRun run, RunQuestDef q, QuestProgress p, QuestNodeDef node, QuestOptionDef opt, RunOutcome outcome)
        {
            var s = ctx.State; var c = ctx.Content;
            if (node.Kind == QuestNodeKind.Boon && node.Boon != null && node.Boon.CoinsPerGain > 0)
            {
                p.CoinBonus += node.Boon.CoinsPerGain;
                run.Quest.CoinBonus += node.Boon.CoinsPerGain; // applies from now on, this run included
                run.Quest.BonusName = QuestBonusName(c, s);
                outcome.Lines.Add(new OutcomeLine(OutcomeKind.QuestBoon, q.BonusName, QuestCoinBonus(s), true));
            }
            p.Journal.Add(new QuestJournalEntry { Chapter = node.Chapter, NodeId = node.Id, Choice = opt.Text, Line = opt.Journal, AtMs = ctx.NowMs });
            p.Node = opt.Next;
            s.Count("run_quest_chapters");
            if (opt.Next == null)
            {
                p.Completed = true;
                s.Count("run_quests_completed");
                ctx.Emit("run_quest_completed", q.Id);
            }
            outcome.Text = opt.Journal;
            outcome.Success = true;
            outcome.Lines.Add(new OutcomeLine(OutcomeKind.QuestStep, p.Completed ? "complete" : "advanced", Math.Min(node.Chapter + 1, q.Chapters), true));
        }

        /// <summary>
        /// A revive after a lost quest battle retried it and won (RunSystem.Revive → AfterWin "quest"): the story moves on as
        /// if the first try had been won (the Fight option), the tile is used up and the STORY result is published.
        /// </summary>
        private static void QuestBattleWonAfterRevive(CommandContext ctx, ActiveRun run)
        {
            var s = ctx.State; var c = ctx.Content;
            var q = c.RunQuest(run.Quest.QuestId);
            var p = q != null ? QuestProgressOf(s, q.Id) : null;
            var node = q != null ? QuestCurrentNode(c, s, q) : null;
            if (node == null || node.Kind != QuestNodeKind.Battle) return;
            int index = node.Options.FindIndex(o => o.Action == QuestOptionAction.Fight);
            if (index < 0) return;
            var opt = node.Options[index];
            s.Count("run_quest_battles_won");
            p.Waits = Math.Max(0, p.Waits - 1); // the loss before the revive did not make the story wait after all
            var outcome = new RunOutcome { Source = "quest", EventId = q.Id, Tile = run.Quest.Tile, TileId = ContentDb.QuestTileId, Title = node.Title, Choice = opt.Text };
            QuestAdvance(ctx, run, q, p, node, opt, outcome);
            ctx.Emit("run_quest_choice", q.Id, node.Id, index.ToString(), "advanced");
            var tile = run.Quest.Tile >= 0 && run.Quest.Tile < run.Tiles.Count ? run.Tiles[run.Quest.Tile] : null;
            if (tile != null) Consume(c, run, tile, TileDefOf(c, tile));
            PublishOutcome(ctx, run, outcome);
        }

        /// <summary>Pays a sacrifice (validated by QuestCanPay). A skill price takes a random owned skill of its tier (the run's stream).</summary>
        private static void QuestPay(CommandContext ctx, ActiveRun run, QuestCostDef cost)
        {
            var c = ctx.Content;
            if (cost == null) return;
            switch (cost.Kind)
            {
                case QuestCostKind.Coins: run.Coins = Math.Max(0, run.Coins - cost.Amount); break;
                case QuestCostKind.LegendarySkill:
                case QuestCostKind.MythicSkill:
                {
                    var candidates = QuestSkillCandidates(c, run, cost.Kind);
                    if (candidates.Count > 0) ForgetSkill(ctx, run, candidates[run.Rng.Range(0, candidates.Count)]);
                    break;
                }
                case QuestCostKind.CurrentHpPct:
                    run.WormHp = Math.Max(1, Math.Round(run.WormHp * (1 - cost.Amount / 100.0)));
                    break;
                case QuestCostKind.AllRunSkills:
                    foreach (var id in QuestForgettable(c, run)) ForgetSkill(ctx, run, id);
                    break;
            }
            RefreshMaxHp(c, run);
            ctx.Emit("run_quest_paid", cost.Kind.ToString(), cost.Amount.ToString());
        }

        /// <summary>Removes an owned run skill (a sacrifice): its level goes too; the signature is re-read.</summary>
        private static void ForgetSkill(CommandContext ctx, ActiveRun run, string id)
        {
            if (id == null || !run.Skills.Remove(id)) return;
            run.SkillLevels.Remove(id);
            UpdateSignature(ctx, run);
            ctx.Emit("run_skill_lost", id);
        }

        /// <summary>
        /// A quest battle (difficult, above the level's elites): led by the level's miniboss at the board's usual strength
        /// (threat, danger, BoardEnemyPct, +EnemyPctPerRoll per roll) + QuestTuning.BattleBasePct + the node's BonusPct, plus the
        /// node's escorts from the level's elite pool at elite strength (+EliteBonusPct).
        /// </summary>
        public static List<CombatantSpec> QuestEnemies(ContentDb c, ActiveRun run, QuestBattleDef battle)
        {
            var t = c.RunTuning;
            var level = Level(c, run);
            var mods = level.Modifiers;
            double scale = c.ThreatMultiplier(run.Threat);
            double common = DangerPct(c, run) + t.BoardEnemyPct + t.EnemyPctPerRoll * run.Rolls;
            double lead = common + c.QuestTuning.BattleBasePct + battle.BonusPct;
            string leader = level.MinibossId ?? (level.ElitePool.Count > 0 ? level.ElitePool[0] : level.EnemyPool[0]);
            var list = new List<CombatantSpec> { BattleSimulator.BuildEnemy(c, leader, scale, lead + mods.EnemyAtkPct, lead + mods.EnemyHpPct, mods.EnemyDefPct) };
            var pool = level.ElitePool.Count > 0 ? level.ElitePool : level.EnemyPool;
            double elite = common + t.EliteBonusPct;
            for (int i = 0; i < battle.Escorts; i++)
                list.Add(BattleSimulator.BuildEnemy(c, pool[run.Rng.Range(0, pool.Count)], scale, elite + mods.EnemyAtkPct, elite + mods.EnemyHpPct, mods.EnemyDefPct));
            return list;
        }

        // ---- run coins ----------------------------------------------------------------------------------------------

        /// <summary>
        /// Every run-coin gain goes through here: the account's quest bonus (QuestCoinBonus, e.g. Starglint +3) is added to each
        /// gain, once per gain. Costs and losses do not get it (<see cref="AddCoins"/> for a signed change).
        /// </summary>
        private static void GainCoins(ActiveRun run, long amount)
        {
            if (amount <= 0) return;
            run.Coins += amount;
            int bonus = run.Quest != null ? run.Quest.CoinBonus : 0;
            if (bonus <= 0) return;
            run.Coins += bonus;
            run.Quest.CoinBonusPaid += bonus;
        }

        /// <summary>A signed run-coin change: gains through <see cref="GainCoins"/> (quest bonus), losses never below 0.</summary>
        private static void AddCoins(ActiveRun run, long amount)
        {
            if (amount > 0) GainCoins(run, amount);
            else if (amount < 0) run.Coins = Math.Max(0, run.Coins + amount);
        }
    }
}
