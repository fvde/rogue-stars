using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    // Structured outcomes of event / chest / campfire choices and shop visits (ActiveRun.LastOutcome): the client's result
    // screen lists exactly what happened. Built by diffing the run before and after the effects, so every effect kind —
    // battles and level-ups included — is covered without bookkeeping in each effect.
    public static partial class RunSystem
    {
        private static void PublishOutcome(CommandContext ctx, ActiveRun run, RunOutcome outcome)
        {
            if (outcome.Lines.Count == 0) outcome.Lines.Add(new OutcomeLine(OutcomeKind.Nothing, null, 0, true));
            run.LastOutcome = outcome;
            run.OutcomeSeq++;
            ctx.Emit("run_outcome", outcome.Source, outcome.EventId ?? "", outcome.Lines.Count.ToString());
        }

        /// <summary>Most tile outcomes kept per move (older ones are dropped; the client reads them after every command).</summary>
        private const int MaxTileOutcomes = 64;

        /// <summary>Tiles whose landing opens a decision (its own result) or a shop (no summary): no tile outcome.</summary>
        private static bool OpensDecision(TileDef def)
        {
            switch (def.Category)
            {
                case TileCategory.Event: case TileCategory.Treasure: case TileCategory.Campfire: case TileCategory.Shop: case TileCategory.Minigame: case TileCategory.Quest:
                case TileCategory.BossGate:
                    return true;
                default: return false;
            }
        }

        /// <summary>Records what a tile did (landing, passing, a fight) as a structured outcome in ActiveRun.TileOutcomes.</summary>
        private static void TileOutcome(CommandContext ctx, ActiveRun run, RunTile tile, TileDef def, Snap before, string source, bool onlyIfChanged)
        {
            var o = new RunOutcome { Source = source, Title = def != null ? def.Name : null, Tile = tile != null ? tile.Id : -1, TileId = tile?.TileId };
            before.Diff(ctx.Content, run, o.Lines);
            if (o.Lines.Count == 0)
            {
                if (onlyIfChanged) return;
                bool empty = def != null && (def.Category == TileCategory.Empty || def.Category == TileCategory.Start);
                o.Lines.Add(new OutcomeLine(empty ? OutcomeKind.Empty : OutcomeKind.Nothing, tile?.TileId, 0, true));
            }
            o.Seq = ++run.TileOutcomeSeq;
            run.TileOutcomes.Add(o);
            if (run.TileOutcomes.Count > MaxTileOutcomes) run.TileOutcomes.RemoveAt(0);
            ctx.Emit("run_tile_outcome", source, o.TileId ?? "", o.Lines.Count.ToString());
        }

        /// <summary>What an outcome line is worth to show first: losses and fights after gains, in a stable order.</summary>
        private sealed class Snap
        {
            private long _coins, _bonus;
            private double _hp, _awakening, _bossPct, _awakenRed;
            private int _xp, _level, _shards, _danger, _stats, _loot, _rare, _battles, _penalty, _skip;
            private List<string> _skills;
            private Dictionary<string, int> _levels;

            public static Snap Of(ActiveRun r) => new Snap
            {
                _coins = r.Coins, _bonus = r.Quest != null ? r.Quest.CoinBonusPaid : 0, _hp = r.WormHp, _awakening = r.Awakening, _bossPct = r.BossStatPct,
                _awakenRed = r.AwakeningReductionPct, _xp = r.XpTotal, _level = r.Level, _shards = r.Shards, _danger = r.Danger,
                _stats = r.RunStats.Count, _loot = r.PendingLoot.Count, _rare = r.PendingRare.Count, _battles = r.BattleCount,
                _penalty = r.StepPenaltyNext, _skip = r.SkipNegativeEvents,
                _skills = new List<string>(r.Skills), _levels = new Dictionary<string, int>(r.SkillLevels),
            };

            public void Diff(ContentDb c, ActiveRun r, List<OutcomeLine> lines)
            {
                void Add(OutcomeKind k, string id, double amount, bool good) => lines.Add(new OutcomeLine(k, id, amount, good));
                if (r.BattleCount > _battles)
                {
                    bool won = r.Phase != RunPhase.Defeated && r.LastBattle != null && r.LastBattle.PlayerWon();
                    Add(OutcomeKind.Battle, won ? "won" : "lost", r.BattleCount - _battles, won);
                }
                foreach (var id in r.Skills) if (!_skills.Contains(id)) Add(OutcomeKind.SkillGained, id, 1, true);
                foreach (var id in _skills) if (!r.Skills.Contains(id)) Add(OutcomeKind.SkillLost, id, 1, false);
                foreach (var id in r.Skills)
                {
                    int now = RunDraft.Level(r, id), was = _skills.Contains(id) ? (_levels.TryGetValue(id, out var l) && l > 1 ? l : 1) : now;
                    if (now > was) Add(OutcomeKind.SkillLevel, id, now, true);
                }
                for (int i = _loot; i < r.PendingLoot.Count; i++)
                    lines.Add(new OutcomeLine(OutcomeKind.AccountLoot, r.PendingLoot[i].Id, r.PendingLoot[i].Amount, true) { Reward = r.PendingLoot[i] });
                for (int i = _rare; i < r.PendingRare.Count; i++)
                    lines.Add(new OutcomeLine(OutcomeKind.AccountLoot, r.PendingRare[i].Id, r.PendingRare[i].Amount, true) { Reward = r.PendingRare[i] });
                for (int i = _stats; i < r.RunStats.Count; i++)
                {
                    var m = r.RunStats[i];
                    double v = m.Percent != 0 ? m.Percent : m.Flat;
                    Add(OutcomeKind.Stat, m.Stat.ToString(), v, v >= 0);
                }
                if (Math.Abs(r.WormHp - _hp) >= 0.5) Add(OutcomeKind.Hp, null, Math.Round(r.WormHp - _hp), r.WormHp > _hp);
                long bonus = (r.Quest != null ? r.Quest.CoinBonusPaid : 0) - _bonus; // the account's quest bonus, shown apart
                long coins = r.Coins - _coins - Math.Max(0, bonus);
                if (coins != 0) Add(OutcomeKind.Coins, null, coins, coins > 0);
                if (bonus > 0) Add(OutcomeKind.CoinBonus, r.Quest.BonusName, bonus, true);
                if (r.XpTotal > _xp) Add(OutcomeKind.Xp, null, r.XpTotal - _xp, true);
                if (r.Level > _level) Add(OutcomeKind.LevelUp, null, r.Level - _level, true);
                if (r.Shards != _shards) Add(OutcomeKind.Shards, null, r.Shards - _shards, r.Shards > _shards);
                if (Math.Abs(r.Awakening - _awakening) >= 0.05) Add(OutcomeKind.Awakening, null, Math.Round(r.Awakening - _awakening, 1), r.Awakening < _awakening);
                if (Math.Abs(r.AwakeningReductionPct - _awakenRed) >= 0.05) Add(OutcomeKind.AwakenRate, null, Math.Round(_awakenRed - r.AwakeningReductionPct, 1), r.AwakeningReductionPct > _awakenRed);
                if (Math.Abs(r.BossStatPct - _bossPct) >= 0.05) Add(OutcomeKind.BossStat, null, r.BossStatPct - _bossPct, r.BossStatPct < _bossPct);
                if (r.Danger != _danger) Add(OutcomeKind.Danger, null, r.Danger - _danger, r.Danger < _danger);
                if (r.StepPenaltyNext > _penalty) Add(OutcomeKind.StepPenalty, null, r.StepPenaltyNext - _penalty, false);
                if (r.SkipNegativeEvents < _skip) Add(OutcomeKind.Avoided, null, _skip - r.SkipNegativeEvents, true);
            }
        }
    }
}
