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

        // ---- how a decision's result is shown (event outcome rules, owner 2026-11) ---------------------------------------

        /// <summary>
        /// True if the outcome holds a random pick the button could not name: a random skill gained (GainSkill), random reward
        /// chests (Loot) or a random skill lost.
        /// </summary>
        public static bool HasRandomPick(EventOutcome oc)
        {
            if (oc == null) return false;
            foreach (var fx in oc.Effects)
                if (fx.Kind == RunEffectKind.GainSkill || fx.Kind == RunEffectKind.Loot || fx.Kind == RunEffectKind.LoseRandomSkill) return true;
            return false;
        }

        /// <summary>
        /// Sets <see cref="RunOutcome.Show"/> of an event / chest / campfire result: a lost fight → the revive; a gamble or a
        /// random pick → the sheet (news first); anything else → popups, with a toast when nothing changed.
        /// </summary>
        private static void PresentEvent(ActiveRun run, RunOutcome r, EventOutcome oc, bool shielded)
        {
            if (run.Phase == RunPhase.Defeated) { r.Show = OutcomeShow.Revive; return; }
            if (r.Chance || !shielded && HasRandomPick(oc)) { r.Show = OutcomeShow.Sheet; NewsFirst(r); return; }
            r.Show = OutcomeShow.Popups;
            if (shielded) r.Toast = "Boon: misfortune avoided";
            else if (r.Lines.Count == 0) r.Toast = oc == null || oc.Effects.Count == 0 ? oc?.Text ?? "You move on." : "Nothing changed.";
        }

        /// <summary>A sheet leads with the news (the random skill, the chest contents, a boon's shield); costs and fights follow.</summary>
        private static void NewsFirst(RunOutcome o)
        {
            int Rank(OutcomeLine l) => l.Kind == OutcomeKind.SkillGained || l.Kind == OutcomeKind.SkillLost || l.Kind == OutcomeKind.AccountLoot
                || l.Kind == OutcomeKind.SkillRoll || l.Kind == OutcomeKind.Avoided ? 0 : l.Kind == OutcomeKind.Battle ? 2 : 1;
            var sorted = new List<OutcomeLine>(o.Lines);
            // stable: the diff's order within a rank
            var keyed = new List<KeyValuePair<int, OutcomeLine>>();
            for (int i = 0; i < sorted.Count; i++) keyed.Add(new KeyValuePair<int, OutcomeLine>(Rank(sorted[i]) * 1000 + i, sorted[i]));
            keyed.Sort((a, b) => a.Key.CompareTo(b.Key));
            o.Lines.Clear();
            foreach (var kv in keyed) o.Lines.Add(kv.Value);
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
            private double[] _tileBonus;

            /// <summary>Also tracks the board's enemy bonuses ("nearby enemies grow stronger": campfire, treasure pod).</summary>
            public static Snap WithTiles(ActiveRun r)
            {
                var s = Of(r);
                s._tileBonus = new double[r.Tiles.Count];
                for (int i = 0; i < r.Tiles.Count; i++) s._tileBonus[i] = r.Tiles[i].BonusPct;
                return s;
            }

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
                {
                    var p = r.PendingLoot[i];
                    // one line per currency (two chests of an event: "+90 Biomass", not two lines)
                    var same = p.Kind == RewardKinds.Currency ? lines.Find(l => l.Kind == OutcomeKind.AccountLoot && l.Reward != null && l.Reward.Kind == RewardKinds.Currency && l.Id == p.Id) : null;
                    if (same != null) { same.Amount += p.Amount; same.Reward = new Reward(p.Kind, p.Id, (long)same.Amount, p.Rarity); }
                    else lines.Add(new OutcomeLine(OutcomeKind.AccountLoot, p.Id, p.Amount, true) { Reward = new Reward(p.Kind, p.Id, p.Amount, p.Rarity) });
                }
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
                if (_tileBonus != null)
                {
                    int n = 0; double most = 0;
                    for (int i = 0; i < _tileBonus.Length && i < r.Tiles.Count; i++)
                    {
                        double d = r.Tiles[i].BonusPct - _tileBonus[i];
                        if (d > 0.05 && !r.Tiles[i].Cleared) { n++; most = Math.Max(most, d); }
                    }
                    if (n > 0) Add(OutcomeKind.EnemiesStronger, n.ToString(), Math.Round(most, 1), false);
                }
            }
        }
    }
}
