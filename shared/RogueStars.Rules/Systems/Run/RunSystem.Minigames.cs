using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    // Run mini-games (GDD §3.6): GAME tiles come from the tile pools (MinigameTuning.PoolWeight, at most TileDef.MaxPerRun per run), RunMinigameStart / RunMinigameFinish, rewards by tier.
    // The client plays the game and decides the tier; the rules pay ContentDb.MinigameTuning.Rewards for it, once.
    public static partial class RunSystem
    {
        private static void RegisterMinigames(GameEngine e)
        {
            e.Register<RunMinigameStart>((c, ctx) => { var r = Need(ctx); MinigameStart(ctx, r); return Ok(ctx); });
            e.Register<RunMinigameFinish>((c, ctx) => { var r = Need(ctx); MinigameFinish(ctx, r, c.Tier); return Ok(ctx); });
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
                    m.DraftOpen = true;
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
            if (tier >= MinigameTier.Bronze) ctx.State.Count("run_minigames_won"); // a medal (summon event tasks)
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
                && std.SkillPrices.TryGetValue(Rarity.Common, out var basePrice) && basePrice > 0
                && ShopTable(c, run).SkillPrices.TryGetValue(Rarity.Common, out var price))
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

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Per-run tile limits (TileDef.MaxPerRun): RunBoard.PickPoolTile asks <see cref="Allowed"/> and records each pick, so a
    /// tile kind such as the GAME tile appears at most N times per run however the board is generated or refilled.
    /// </summary>
    public static class RunTileLimits
    {
        public static bool Allowed(ContentDb c, ActiveRun run, string tileId)
        {
            if (!c.BoardTiles.TryGetValue(tileId, out var def) || def.MaxPerRun <= 0) return true;
            if (run == null) return false; // previews / runless generation: limited tiles are never placed
            if (tileId == ContentDb.MinigameTileId)
            {
                var level = c.RunLevel(run.LevelId);
                if (level != null && level.PortalId != ContentDb.DarkSidePortalId && level.Number > 0 && level.Number < c.MinigameTuning.MinMainLevel && run.Mode == RunMode.Normal) return false;
            }
            return (run.TilesPlaced.TryGetValue(tileId, out var n) ? n : 0) < def.MaxPerRun;
        }

        public static void Placed(ContentDb c, ActiveRun run, string tileId)
        {
            if (run == null || !c.BoardTiles.TryGetValue(tileId, out var def) || def.MaxPerRun <= 0) return;
            run.TilesPlaced[tileId] = (run.TilesPlaced.TryGetValue(tileId, out var n) ? n : 0) + 1;
        }
    }
}
