using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim
{
    // Run mini-games (GDD §3.6): the bot plays a GAME tile with a configurable skill. The client decides the tier on a
    // device; the bot draws it around its typical tier (MinigameSkill: 20% one tier below, 60% on it, 20% one above), so
    // the run economy (coins, skill rolls) stays measurable. The median player is Silver.
    public sealed partial class SimPlayer
    {
        /// <summary>Typical mini-game tier of this bot (median player: Silver).</summary>
        public MinigameTier MinigameSkill = MinigameTier.Silver;
        /// <summary>Player time per mini-game (instruction, 3-2-1, 15–30 s of play, medal).</summary>
        public const double MinigameSec = 28;
        /// <summary>Mini-games played per tier (index = tier).</summary>
        public readonly int[] MinigameTiers = new int[4];
        /// <summary>Run coins paid by mini-games (money rewards and Miss consolations) and skill rolls opened.</summary>
        public long MinigameCoins;
        public int MinigameSkillRolls;

        private Rng _minigameRng;

        /// <summary>A tier around <see cref="MinigameSkill"/> from the bot's own mini-game stream (deterministic per seed).</summary>
        public MinigameTier DrawMinigameTier()
        {
            if (_minigameRng == null) _minigameRng = Rng.FromSeed(Seed * 104729 + 3);
            double p = _minigameRng.NextDouble();
            int t = (int)MinigameSkill + (p < 0.2 ? -1 : p >= 0.8 ? 1 : 0);
            return (MinigameTier)System.Math.Max(0, System.Math.Min(3, t));
        }

        /// <summary>Start the GAME tile's mini-game and finish it with a drawn tier; the result carries both commands' events.</summary>
        private CommandResult PlayMinigame()
        {
            var start = Exec(new RunMinigameStart());
            if (!start.Ok) return start;
            var tier = DrawMinigameTier();
            long coins = S.Run.Active.Coins;
            var reward = S.Run.Active.Minigame.Reward;
            var fin = Exec(new RunMinigameFinish { Tier = tier });
            if (!fin.Ok) return fin;
            MinigameTiers[(int)tier]++;
            if (S.Run.Active != null)
            {
                if (S.Run.Active.Phase == RunPhase.Draft && reward == MinigameReward.SkillRoll) MinigameSkillRolls++;
                else MinigameCoins += System.Math.Max(0, S.Run.Active.Coins - coins);
            }
            fin.Events.InsertRange(0, start.Events);
            return fin;
        }
    }
}
