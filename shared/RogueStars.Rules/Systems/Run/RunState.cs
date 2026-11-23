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

    /// <summary>What the run is waiting for. The client shows the matching UI (RunSystem.Decision names the decision).</summary>
    public enum RunPhase { AwaitRoll, ChooseBranch, Event, Draft, Shop, Defeated, BurrowChoice, Completed, Failed, Minigame }

    /// <summary>
    /// The decision the run waits for (RunSystem.Decision). Roll and GiveUp (defeated with the revive used: no other
    /// option) are clear steps that auto mode performs; every other kind needs the player unless farming.
    /// </summary>
    public enum RunDecision { None, Roll, Branch, Event, Treasure, Campfire, Draft, Shop, Revive, GiveUp, BurrowChoice, Minigame }

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
        /// <summary>
        /// Endless campaign: progress of main / Dark Side levels far behind the frontier, folded out of <see cref="Levels"/>
        /// (RunSystem.CompactProgress) so the state stays small however far a player gets. Read through RunSystem.Progress.
        /// </summary>
        public ProgressArchive MainArchive = new ProgressArchive();
        public ProgressArchive DarkArchive = new ProgressArchive();
        public long RunsStarted;
        /// <summary>Daily Biomass surge: reset-day of the counter and won portal runs that used the bonus that day.</summary>
        public long BiomassBonusDay = -1;
        public int BiomassBonusRuns;
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

    /// <summary>
    /// Compact progress of levels 1 … Full + Flags.Length of one endless sequence (main campaign or Dark Side): levels 1 … Full
    /// are fully done (cleared, Overcharged cleared, all 3 feats); level Full + k is encoded in Flags[k − 1] as one base-32 digit
    /// of bits 1 cleared, 2 Overcharged cleared, 4/8/16 feats 1–3. One byte per archived level at most, nothing once complete.
    /// </summary>
    [Serializable]
    public sealed class ProgressArchive
    {
        public int Full;
        public string Flags = "";
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
    public sealed partial class ActiveRun
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
        /// <summary>Hard roll limit: the boss comes at the latest after this roll (the awakening meter is the usual clock).</summary>
        public int MaxRolls;
        /// <summary>Laps around the circuit board (passing the boss gate starts the next lap).</summary>
        public int Laps;
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
        /// <summary>All run XP earned so far (level-ups included).</summary>
        public int XpTotal;
        public int PendingDrafts;
        public List<string> Skills = new List<string>();
        public List<string> ReplacedSkills = new List<string>();
        public List<StatMod> RunStats = new List<StatMod>();
        public long Coins;
        public int Shards;

        // loot
        public List<Reward> PendingLoot = new List<Reward>();
        /// <summary>Rare drops won so far (LevelDef.RareDrops): paid unscaled and not doubled on a won run, lost otherwise.</summary>
        public List<Reward> PendingRare = new List<Reward>();
        public int Danger;
        /// <summary>Account-loot opportunities offered so far (events / guarded caches that pay meta loot; RunTuning.MaxAccountLootOffersPerRun).</summary>
        public int AccountLootOffers;
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
        /// <summary>What the last event / chest / campfire choice or shop visit did (result screen); null before the first.</summary>
        public RunOutcome LastOutcome;
        /// <summary>Increases with every new <see cref="LastOutcome"/> (the client shows each outcome once).</summary>
        public int OutcomeSeq;
        /// <summary>Purchases of the open shop, published as <see cref="LastOutcome"/> when the shop is left.</summary>
        public RunOutcome ShopOutcome;

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

    /// <summary>Kind of one line of a <see cref="RunOutcome"/>.</summary>
    public enum OutcomeKind { Coins, Hp, Stat, SkillGained, SkillLost, Xp, LevelUp, Shards, AccountLoot, Awakening, AwakenRate, BossStat, Danger, Battle, Nothing, SkillRoll }

    /// <summary>One thing that happened (gained / lost), with its number; Good = in the player's favour.</summary>
    [Serializable]
    public sealed class OutcomeLine
    {
        public OutcomeKind Kind;
        /// <summary>Skill id, stat name, reward id or battle kind.</summary>
        public string Id;
        public double Amount;
        /// <summary>AccountLoot: the pending reward (paid when the boss is beaten).</summary>
        public Reward Reward;
        public bool Good;
        public OutcomeLine() { }
        public OutcomeLine(OutcomeKind kind, string id, double amount, bool good) { Kind = kind; Id = id; Amount = amount; Good = good; }
    }

    /// <summary>
    /// Structured result of an event / chest / campfire option or a shop visit: what was chosen, whether chance was involved
    /// (the chance of the lucky outcome and whether it hit) and every gain or loss as a line (the client's result screen).
    /// </summary>
    [Serializable]
    public sealed class RunOutcome
    {
        /// <summary>"event", "treasure", "campfire" or "shop".</summary>
        public string Source;
        public string EventId;
        public string Title;
        public string Choice;
        public string Text;
        /// <summary>The option had more than one weighted outcome.</summary>
        public bool Chance;
        /// <summary>Chance of the option's first (lucky) outcome, 0..100.</summary>
        public double SuccessChancePct;
        public bool Success;
        public List<OutcomeLine> Lines = new List<OutcomeLine>();
    }

    [Serializable]
    public sealed class RunSummary
    {
        public string RunId;
        public string LevelId;
        public RunMode Mode;
        public bool Won;
        public List<Reward> Granted = new List<Reward>();
        /// <summary>Granted run rewards that the "double run rewards" ad pays again (everything but feat Diamonds).</summary>
        public List<Reward> Doublable = new List<Reward>();
        /// <summary>True if this run used one of the day's Biomass surge runs (RunTuning.DailyBiomassBonusRuns).</summary>
        public bool BiomassBonus;
        /// <summary>True once RunDoubleRewards was used for this run.</summary>
        public bool Doubled;
        public List<int> NewFeats = new List<int>();
        public int Rolls;
        public int Level;
        public int Floors;
        public int Laps;
        public int XpTotal;
        /// <summary>Account-loot opportunities offered in the run (Burrow: of its last floor).</summary>
        public int AccountLootOffers;
        /// <summary>Account-loot items found during the run (before completion rolls; Burrow: floor rewards included).</summary>
        public int AccountLootDrops;
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
        /// <summary>End time when the research started (the live value is the timer's end: LaboratorySystem.ReadyAtMs).</summary>
        public long ReadyAtMs;
        /// <summary>City TimerSystem timer of the running research (kind "lab_research").</summary>
        public string TimerId;
        /// <summary>True while the running research uses the Research Annex slot (longer timer).</summary>
        public bool InAnnex;
    }
}
