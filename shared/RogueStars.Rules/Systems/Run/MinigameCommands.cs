using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>How well a mini-game went (decided by the client game, GDD §3.6).</summary>
    public enum MinigameTier { Miss = 0, Bronze = 1, Silver = 2, Gold = 3 }

    /// <summary>What a GAME tile pays: run coins or a skill roll (1-of-3 draft, rarity odds by tier).</summary>
    public enum MinigameReward { Coins = 0, SkillRoll = 1 }

    /// <summary>
    /// On a GAME tile (RunPhase.Minigame): the rules pick the game (never one already played this run), its seed and the
    /// reward type. Once per GAME tile.
    /// </summary>
    [Serializable] public sealed class RunMinigameStart : GameCommand { }

    /// <summary>
    /// Ends the started mini-game with the tier the client game decided (validated as a tier value only). Exactly one
    /// finish per start: the reward is paid once. A Miss right after the start is the "skip".
    /// </summary>
    [Serializable] public sealed class RunMinigameFinish : GameCommand { public MinigameTier Tier; }

    /// <summary>Mini-games of the active run.</summary>
    [Serializable]
    public sealed class MinigameRunState
    {
        /// <summary>Games started this run (never offered twice).</summary>
        public List<string> Played = new List<string>();
        /// <summary>The GAME tile waiting to be played (-1 = none).</summary>
        public int Tile = -1;
        /// <summary>The started game (null until RunMinigameStart).</summary>
        public string GameId;
        /// <summary>Seed for the client game (layouts, sequences, timings).</summary>
        public int Seed;
        public MinigameReward Reward;
        /// <summary>True between RunMinigameStart and RunMinigameFinish.</summary>
        public bool Started;
        /// <summary>Last finished game's tier and reward (result screen).</summary>
        public MinigameTier LastTier;
        public int Finished;
    }

    public sealed partial class ActiveRun
    {
        public MinigameRunState Minigame = new MinigameRunState();
    }
}
