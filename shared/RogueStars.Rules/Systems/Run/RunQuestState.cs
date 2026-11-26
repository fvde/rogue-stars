using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        /// <summary>Run Quests: progress of every quest chain and the journal (RunSystem.Quests).</summary>
        public RunQuestState RunQuests = new RunQuestState();
    }
}

namespace RogueStars.Rules.Systems
{
    /// <summary>Account state of the Run Quests (docs/design/run-quests/recipe.md).</summary>
    [Serializable]
    public sealed class RunQuestState
    {
        /// <summary>Progress per quest id (created at the first encounter).</summary>
        public Dictionary<string, QuestProgress> Quests = new Dictionary<string, QuestProgress>();
        /// <summary>Encounters met so far, all chains and epilogues (0 = the player has not met anyone yet).</summary>
        public int Encounters;
    }

    /// <summary>One quest chain's progress.</summary>
    [Serializable]
    public sealed class QuestProgress
    {
        /// <summary>The node the story waits on (null before the first encounter = the start node; null after completion).</summary>
        public string Node;
        public bool Completed;
        /// <summary>Encounters with this character (story, retries and epilogues).</summary>
        public int Encounters;
        /// <summary>Times the story waited (a battle declined, or lost and the run ended; a price refused or not affordable).</summary>
        public int Waits;
        /// <summary>Epilogue scenes given after completion (the next one is Epilogues[EpiloguesSeen % count]).</summary>
        public int EpiloguesSeen;
        /// <summary>Account-wide +run coins on every run-coin gain, from this chain's boons.</summary>
        public int CoinBonus;
        /// <summary>The story so far: one entry per chapter passed, in order.</summary>
        public List<QuestJournalEntry> Journal = new List<QuestJournalEntry>();
    }

    [Serializable]
    public sealed class QuestJournalEntry
    {
        public int Chapter;
        public string NodeId;
        /// <summary>The option picked.</summary>
        public string Choice;
        /// <summary>The option's journal line.</summary>
        public string Line;
        public long AtMs;
    }

    /// <summary>The quest encounter of the active run.</summary>
    [Serializable]
    public sealed class RunQuestRun
    {
        /// <summary>The chain this run meets (scheduled at run start), or null.</summary>
        public string QuestId;
        /// <summary>The meeting is an epilogue scene (the chain is complete): EpilogueId is the scene.</summary>
        public bool Epilogue;
        public string EpilogueId;
        /// <summary>The encounter tile is on the board (RunBoard) and its index (−1 = not placed yet).</summary>
        public bool Placed;
        public int Tile = -1;
        /// <summary>The worm reached the tile (the encounter is counted even if the run ends right after).</summary>
        public bool Met;
        /// <summary>Account bonus in this run: +CoinBonus run coins on every coin gain (boons met mid-run apply at once).</summary>
        public int CoinBonus;
        /// <summary>Run coins paid by the bonus so far this run (outcome lines show it apart from the base gain).</summary>
        public long CoinBonusPaid;
        /// <summary>Name of the bonus for display ("Starglint").</summary>
        public string BonusName;
    }

    public sealed partial class ActiveRun
    {
        public RunQuestRun Quest = new RunQuestRun();
    }

    /// <summary>
    /// Phase Quest: pick option Index of the quest node (Story / Boon: advance; Battle: Fight or Wait; Sacrifice: Pay or Wait;
    /// an epilogue scene has one option, 0). A Legendary / Mythic skill sacrifice takes a random owned skill of the tier.
    /// </summary>
    [Serializable] public sealed class RunQuestChoose : GameCommand { public int Index; }
}
