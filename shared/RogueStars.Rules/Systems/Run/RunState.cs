using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        /// <summary>Runs: the active run (if any), level progress and feats.</summary>
        public RunState Run = new RunState();
        /// <summary>Laboratory research levels and draft-pool toggles.</summary>
        public LabState Lab = new LabState();
    }
}

namespace RogueStars.Rules.Systems
{
    public enum RunMode { Normal, Overcharged, Burrow, DarkSide }

    /// <summary>What the run is waiting for. The client shows the matching UI; RunAutoStep performs the default.</summary>
    public enum RunPhase { AwaitRoll, ChooseBranch, Event, Draft, Shop, Defeated, BurrowChoice, Completed, Failed }

    public enum ReviveMethod { Ad, Diamonds }

    public enum ShopOfferKind { Skill, Heal, Stat }

    [Serializable]
    public sealed class RunState
    {
        /// <summary>The run in progress, or null.</summary>
        public ActiveRun Active;
        /// <summary>Summary of the last finished run (for the result screen).</summary>
        public RunSummary LastSummary;
        /// <summary>Progress per level id (normal mode; Overcharged tracked via OverchargedCleared).</summary>
        public Dictionary<string, LevelProgress> Levels = new Dictionary<string, LevelProgress>();
        public long RunsStarted;
    }

    [Serializable]
    public sealed class LevelProgress
    {
        public bool Cleared;
        public bool OverchargedCleared;
        public int Clears;
        /// <summary>The 3 feats: [0] clear, [1] HP left ≥ x%, [2] boss beaten within N rounds.</summary>
        public List<bool> Feats = new List<bool> { false, false, false };
        /// <summary>Burrow: deepest floor cleared.</summary>
        public int BestFloor;
    }

    [Serializable]
    public sealed class RunTile
    {
        public int Id;
        public string TileId;
        public List<int> Next = new List<int>();
        /// <summary>Layout hint for the client (column along the path, lane).</summary>
        public int X, Y;
        /// <summary>Extra enemy strength % on this tile (campfire light, boss actions).</summary>
        public double BonusPct;
        public bool Fixed;
        public int TimesResolved;
    }

    [Serializable]
    public sealed class ShopOffer
    {
        public ShopOfferKind Kind;
        public string SkillId;
        public StatMod Stat;
        public double Value;
        public long Price;
        public bool Sold;
    }

    [Serializable]
    public sealed class ActiveRun
    {
        public string RunId;
        public string PortalId;
        public string LevelId;
        public RunMode Mode;
        public long StartedMs;
        /// <summary>Run RNG stream (forked from the player stream at start): a run is reproducible from it.</summary>
        public Rng Rng = new Rng();
        /// <summary>Loadout snapshot taken at run start (worm, parts, hero, mount, aliens, lab pool).</summary>
        public CombatLoadout Loadout = new CombatLoadout();
        public int Threat;

        // board
        public List<RunTile> Tiles = new List<RunTile>();
        public int Position;
        public List<int> Trail = new List<int>();
        public int BossGateTile;
        public RunPhase Phase = RunPhase.AwaitRoll;
        public int StepsLeft;
        public List<int> BranchOptions = new List<int>();
        public int ChosenNext = -1;
        public int Rolls;
        public int MaxRolls;
        public int Dice = 1;
        public int ExtraDicePermanent;
        public int ExtraDiceNext;
        public List<int> LastDice = new List<int>();

        // boss awakening
        public string BossId;
        public double Awakening;
        public int ActionsTriggered;
        public double AwakeningReductionPct;
        public double BossStatPct;
        public bool BossFought;

        // worm
        public double WormHp = -1;
        public double WormMaxHp;
        public int Level = 1;
        public int Xp;
        public int PendingDrafts;
        public List<string> Skills = new List<string>();
        public List<string> ReplacedSkills = new List<string>();
        public List<StatMod> RunStats = new List<StatMod>();
        public long Coins;
        public int Shards;

        // loot
        public List<Reward> PendingLoot = new List<Reward>();
        public int Danger;
        public double ExtraLootPct;
        public int SkipNegativeEvents;

        // decisions
        public List<string> DraftOffer = new List<string>();
        public int DraftRerolls;
        public int DraftsWithoutLegendary;
        public List<ShopOffer> Shop = new List<ShopOffer>();
        public int ShopRerolls;
        public string EventId;
        public int EventTile = -1;
        public string LastEventText;

        // battles
        public int BattleCount;
        public BattleResult LastBattle;
        public bool ReviveUsed;
        public List<CombatantSpec> DefeatEnemies = new List<CombatantSpec>();
        public string DefeatKind;
        public int DefeatXp;
        public long DefeatCoins;
        public List<RunEffect> DefeatFollowUp = new List<RunEffect>();
        public int BossRounds;
        public int BattlesWon;

        // burrow
        public int Floor = 1;
    }

    [Serializable]
    public sealed class RunSummary
    {
        public string RunId;
        public string LevelId;
        public RunMode Mode;
        public bool Won;
        public List<Reward> Granted = new List<Reward>();
        public List<int> NewFeats = new List<int>();
        public int Rolls;
        public int Level;
        public int Floors;
        public BattleResult LastBattle;
    }

    // ---- laboratory -------------------------------------------------------------------------

    [Serializable]
    public sealed class LabState
    {
        public Dictionary<string, LabEntry> Entries = new Dictionary<string, LabEntry>();
    }

    [Serializable]
    public sealed class LabEntry
    {
        /// <summary>Researched level (0 = not researched).</summary>
        public int Level;
        /// <summary>Toggled on for the run draft pool.</summary>
        public bool Enabled;
        /// <summary>Level being researched (0 = idle).</summary>
        public int ResearchingLevel;
        /// <summary>Research completes at this time; City's timer hook may lower it (speed-ups).</summary>
        public long ReadyAtMs;
    }
}
