using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    // Run mini-games (GDD §3.6): GAME tiles placed on the board, RunMinigameStart / RunMinigameFinish, rewards by tier.
    // The client plays the game and decides the tier; the rules pay ContentDb.MinigameTuning.Rewards for it, once.
    public static partial class RunSystem
    {
        private static void RegisterMinigames(GameEngine e)
        {
            e.Register<RunMinigameStart>((c, ctx) => { var r = Need(ctx); MinigameStart(ctx, r); return Ok(ctx); });
            e.Register<RunMinigameFinish>((c, ctx) => { var r = Need(ctx); MinigameFinish(ctx, r, c.Tier); return Ok(ctx); });
        }

        /// <summary>
        /// Turns MinigameTuning.TilesPerBoard free main-path tiles into GAME tiles (at TileFractions of the path). Uses a
        /// fork of the run RNG, so the rest of the run's random stream is untouched.
        /// </summary>
        private static void PlaceMinigameTiles(ContentDb c, ActiveRun run)
        {
            var t = c.MinigameTuning;
            if (!c.BoardTiles.ContainsKey(ContentDb.MinigameTileId) || c.Minigames.Count == 0) return;
            int count = run.Mode == RunMode.Burrow ? t.TilesPerBurrowFloor : t.TilesPerBoard;
            if (count <= 0 || t.TileFractions.Count == 0) return;
            var rng = run.Rng.Fork("minigames:" + run.Floor + ":" + run.LevelId);
            var cands = new List<RunTile>();
            foreach (var tile in run.Tiles)
            {
                if (tile.Y != 0 || tile.Fixed || tile.Id == 0 || tile.Id == run.BossGateTile) continue;
                var def = TileDefOf(c, tile);
                if (def.Category == TileCategory.Shop || def.Category == TileCategory.BossTile || def.Category == TileCategory.Miniboss) continue;
                cands.Add(tile);
            }
            if (cands.Count == 0) return;
            var placed = new List<int>();
            for (int i = 0; i < count; i++)
            {
                double frac = t.TileFractions[i % t.TileFractions.Count];
                int idx = (int)Math.Round(frac * (cands.Count - 1)) + rng.Range(-1, 2);
                idx = Math.Max(0, Math.Min(cands.Count - 1, idx));
                // nearest candidate that is not a neighbour of a GAME tile already placed
                for (int d = 0; d < cands.Count; d++)
                {
                    int k = idx + ((d & 1) == 0 ? d / 2 : -(d / 2 + 1));
                    if (k < 0 || k >= cands.Count) continue;
                    var tile = cands[k];
                    bool near = false;
                    foreach (var p in placed) if (Math.Abs(p - tile.Id) <= 2) near = true;
                    if (near) continue;
                    tile.TileId = ContentDb.MinigameTileId;
                    tile.Fixed = true; // boss actions never transform it
                    placed.Add(tile.Id);
                    break;
                }
            }
        }

        /// <summary>Landing on a GAME tile: the run waits for RunMinigameStart (auto mode pauses, farming plays it as FarmingTier).</summary>
        private static void OpenMinigame(CommandContext ctx, ActiveRun run, RunTile tile)
        {
            run.Phase = RunPhase.Minigame;
            run.Minigame.Tile = tile.Id;
            run.Minigame.GameId = null;
            run.Minigame.Started = false;
            ctx.Emit("run_minigame", tile.Id.ToString());
        }

        private static void MinigameStart(CommandContext ctx, ActiveRun run)
        {
            var c = ctx.Content;
            var m = run.Minigame;
            Require(run.Phase == RunPhase.Minigame, "no mini-game here");
            Require(!m.Started, "mini-game already started");
            var games = c.Minigames;
            Require(games.Count > 0, "no mini-games");
            var w = new double[games.Count];
            bool any = false;
            for (int i = 0; i < games.Count; i++) { w[i] = m.Played.Contains(games[i].Id) ? 0 : games[i].Weight; any |= w[i] > 0; }
            if (!any) for (int i = 0; i < games.Count; i++) w[i] = games[i].Weight; // every game played: repeats allowed
            int pick = run.Rng.PickWeighted(w);
            Require(pick >= 0, "no mini-games");
            m.GameId = games[pick].Id;
            m.Seed = run.Rng.Range(1, int.MaxValue);
            bool skill = run.Rng.NextDouble() * 100 < c.MinigameTuning.SkillRollChancePct && RunDraft.Eligible(c, run).Count > 0;
            m.Reward = skill ? MinigameReward.SkillRoll : MinigameReward.Coins;
            m.Started = true;
            m.Played.Add(m.GameId);
            ctx.State.Count("run_minigames_started");
            ctx.Emit("run_minigame_start", m.GameId, m.Seed.ToString(), m.Reward.ToString());
        }

        private static void MinigameFinish(CommandContext ctx, ActiveRun run, MinigameTier tier)
        {
            var c = ctx.Content;
            var m = run.Minigame;
            Require(run.Phase == RunPhase.Minigame && m.Started && m.GameId != null, "no mini-game running");
            Require(Enum.IsDefined(typeof(MinigameTier), tier), "invalid tier");
            var def = c.Minigame(m.GameId);
            var reward = c.MinigameTuning.For(tier);
            var before = Snap.Of(run);
            var outcome = new RunOutcome
            {
                Source = "minigame", EventId = m.GameId, Title = def != null ? def.Name : m.GameId,
                Choice = TierName(tier), Text = TierName(tier) + (tier == MinigameTier.Miss ? "" : " medal"),
            };

            bool draft = false;
            if (m.Reward == MinigameReward.SkillRoll && reward.Draft)
            {
                var offer = MinigameDraftOffer(c, run, reward);
                if (offer.Count > 0)
                {
                    run.DraftOffer.Clear();
                    run.DraftOffer.AddRange(offer);
                    run.PendingDrafts++; // DraftPick takes one pending draft: this one
                    draft = true;
                }
                else run.Coins += c.RunTuning.DraftEmptyCoins;
            }
            else run.Coins += MinigameCoins(c, run, reward);

            m.Started = false;
            m.LastTier = tier;
            m.Finished++;
            if (m.Tile >= 0 && m.Tile < run.Tiles.Count) Consume(c, run, run.Tiles[m.Tile], TileDefOf(c, run.Tiles[m.Tile]));
            m.Tile = -1;
            ctx.State.Count("run_minigames_played");
            if (tier == MinigameTier.Gold) ctx.State.Count("run_minigames_gold");
            ctx.Emit("run_minigame_finish", m.GameId, tier.ToString(), m.Reward.ToString());

            before.Diff(c, run, outcome.Lines);
            if (draft) outcome.Lines.Insert(0, new OutcomeLine(OutcomeKind.SkillRoll, BestOdds(reward).ToString(), run.DraftOffer.Count, true));
            PublishOutcome(ctx, run, outcome);
            if (draft)
            {
                run.Phase = RunPhase.Draft;
                ctx.Emit("run_draft", string.Join(",", run.DraftOffer));
                return;
            }
            run.Phase = RunPhase.AwaitRoll;
            Continue(ctx, run);
        }

        /// <summary>Farming auto step on a GAME tile: start it and finish it as MinigameTuning.FarmingTier.</summary>
        private static void MinigameAuto(CommandContext ctx, ActiveRun run)
        {
            if (!run.Minigame.Started) MinigameStart(ctx, run);
            MinigameFinish(ctx, run, ctx.Content.MinigameTuning.FarmingTier);
        }

        /// <summary>Run coins a tier pays (money reward and the Miss consolation), scaled with the level's shop prices.</summary>
        public static long MinigameCoins(ContentDb c, ActiveRun run, MinigameTierReward reward)
        {
            double scale = 1;
            if (c.MinigameTuning.ScaleCoinsWithShop && run != null && c.RunShopTables.TryGetValue("shop_standard", out var std)
                && std.SkillPrices.TryGetValue(Rarity.Rare, out var basePrice) && basePrice > 0
                && ShopTable(c, run).SkillPrices.TryGetValue(Rarity.Rare, out var price))
                scale = (double)price / basePrice;
            return (long)Math.Round(reward.Coins * scale);
        }

        /// <summary>A 1-of-N offer drawn with the tier's rarity odds from the run's draft pool (no duplicates).</summary>
        private static List<string> MinigameDraftOffer(ContentDb c, ActiveRun run, MinigameTierReward reward)
        {
            var pool = RunDraft.Eligible(c, run);
            var offer = new List<string>();
            while (offer.Count < c.RunTuning.DraftChoices && pool.Count > 0)
            {
                var w = new double[pool.Count];
                for (int i = 0; i < pool.Count; i++) w[i] = reward.DraftRarityWeights.TryGetValue(pool[i].Rarity, out var x) ? x : 0;
                int idx = run.Rng.PickWeighted(w);
                if (idx < 0) idx = run.Rng.Range(0, pool.Count); // only rarities without odds left: any
                offer.Add(pool[idx].Id);
                pool.RemoveAt(idx);
            }
            return offer;
        }

        /// <summary>The rarity with the highest odds of a tier's skill roll (result screen: "Epic odds").</summary>
        public static Rarity BestOdds(MinigameTierReward reward)
        {
            Rarity best = Rarity.Common; double bw = -1;
            foreach (var kv in reward.DraftRarityWeights) if (kv.Value > bw || (kv.Value == bw && kv.Key > best)) { bw = kv.Value; best = kv.Key; }
            return best;
        }

        public static string TierName(MinigameTier tier) =>
            tier == MinigameTier.Gold ? "Gold" : tier == MinigameTier.Silver ? "Silver" : tier == MinigameTier.Bronze ? "Bronze" : "Miss";
    }
}
