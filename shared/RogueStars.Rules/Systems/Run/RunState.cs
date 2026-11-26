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
    public enum RunPhase { AwaitRoll, ChooseBranch, Event, Draft, Shop, Defeated, BurrowChoice, Completed, Failed, Encounter, Minigame, Quest }

    /// <summary>
    /// The decision the run waits for (RunSystem.Decision). Roll and GiveUp (defeated with the revive used: no other
    /// option) are clear steps that auto mode performs; every other kind needs the player unless farming.
    /// </summary>
    public enum RunDecision { None, Roll, Branch, Event, Treasure, Campfire, Draft, Shop, Revive, GiveUp, BurrowChoice, Encounter, Minigame, Quest }

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
        /// <summary>Seed of the run-start signature offers (set once for a new player from its seed; 0 for older saves).</summary>
        public ulong SignatureSeed;
        /// <summary>Daily Biomass surge: reset-day of the counter and won portal runs that used the bonus that day.</summary>
        public long BiomassBonusDay = -1;
        public int BiomassBonusRuns;
        /// <summary>
        /// "Longest survived": the most rolls of any finished run (won or lost) per level, for the levels played most recently
        /// (RunSystem.LongestRolls; at most RunSystem.LongestRollsKept entries, the newest last).
        /// </summary>
        public List<LevelRolls> LongestRolls = new List<LevelRolls>();
    }

    [Serializable]
    public sealed class LevelRolls
    {
        public string LevelId;
        public int Rolls;
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
        /// <summary>Topology hint (column along the path, lane: 0 trunk / first fork arm, 1 second arm, −1 boss arena).</summary>
        public int X, Y;
        /// <summary>
        /// Iso grid cell (RunBoard grid layout; whole numbers): +PosX runs up-right on screen, +PosY up-left, so the path climbs
        /// as PosX + PosY grows and PosX − PosY is its screen-horizontal place (kept to ±RunTuning.GridBand). Neighbouring path
        /// tiles are side neighbours on the grid, no two tiles share a cell. Display only: no rule reads it.
        /// </summary>
        public double PosX, PosY;
        /// <summary>Grid heading the path enters this tile with, in degrees: 0 = +Y, 90 = +X, 180 = −Y, −90 = −X.</summary>
        public double Dir;
        /// <summary>Platform height of the tile's block (0 ground, 1 raised section); neighbours differ by at most 1 (a step).</summary>
        public int Height;
        /// <summary>Board segment the tile belongs to (the start tile: 0, the boss arena: the worm's); −1 = no 2D layout (hand-built boards).</summary>
        public int Segment = -1;
        /// <summary>Extra enemy strength % on this tile (campfire light, boss actions).</summary>
        public double BonusPct;
        public bool Fixed;
        public int TimesResolved;
        /// <summary>Resolved (fight won, pickup taken, event decided): kept on the board as it was, drawn cleared.</summary>
        public bool Cleared;
    }

    /// <summary>
    /// A set-piece slot the board layout keeps free inside a loop (fork rectangle) or a hairpin: grid cells X..X+W−1 ×
    /// Y..Y+H−1 that no tile ever takes (display only: the client stands a large prop there).
    /// </summary>
    [Serializable]
    public sealed class RunSetPiece
    {
        public int X, Y, W, H;
        public int Height;
        public int Segment;
        /// <summary>"loop" (inside a fork) or "hairpin" (inside a U-turn).</summary>
        public string Shape;
    }

    [Serializable]
    public sealed class ShopOffer
    {
        public ShopOfferKind Kind;
        public string SkillId;
        public StatMod Stat;
        public double Value;
        public long Price;
        /// <summary>The price before a SALE (0 = not on sale; Price is what it costs).</summary>
        public long ListPrice;
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
        /// <summary>The boss arena tile once it opened ahead of the worm (the meter is full), else -1.</summary>
        public int BossGateTile = -1;
        /// <summary>The open end of the path and the segments generated so far (RunBoard: the board grows ahead of the worm).</summary>
        public int BoardEnd;
        public int BoardSegments;
        /// <summary>Set-piece slots of the board's grid layout (RunBoard.Board.SetPieces; display only).</summary>
        public List<RunSetPiece> SetPieces = new List<RunSetPiece>();
        /// <summary>Steps the next roll loses (Gravity Vortex).</summary>
        public int StepPenaltyNext;
        /// <summary>Phase Encounter: "ambush" (a miniboss of the awakening clock) or "boss" (the arena opened ahead).</summary>
        public string EncounterKind;
        public RunPhase Phase = RunPhase.AwaitRoll;
        public int StepsLeft;
        public List<int> BranchOptions = new List<int>();
        public int ChosenNext = -1;
        public int Rolls;
        /// <summary>Hard roll limit: the boss comes at the latest after this roll (the awakening meter is the usual clock).</summary>
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
        /// <summary>All run XP earned so far (level-ups included).</summary>
        public int XpTotal;
        public int PendingDrafts;
        /// <summary>Mythic choices waiting (one per miniboss beaten, RunTuning.MythicChoices skills of Mythic rarity).</summary>
        public int PendingMythicDrafts;
        /// <summary>The open draft is a mythic choice (from a miniboss), not a level-up draft.</summary>
        public bool MythicDraft;
        public List<string> Skills = new List<string>();
        /// <summary>The run's signature skill (also in Skills; null for runs saved before the signatures pass).</summary>
        public string SignatureId;
        /// <summary>
        /// Every signature the run holds, in the order found (skill redesign: the start pick, then minibosses and Resonance events;
        /// at most RunTuning.MaxSignatures). Empty for runs saved before: <see cref="RunSystem.Signatures"/> falls back to SignatureId.
        /// </summary>
        public List<string> SignatureIds = new List<string>();
        /// <summary>The unowned signatures a Resonance event offers (its options "first" / "second" pick from this list).</summary>
        public List<string> SignatureChoice = new List<string>();
        /// <summary>Rage the next battle starts with (Ambush Instinct, set when landing on an enemy tile; cleared by the battle).</summary>
        public double PendingStartRage;
        /// <summary>Plague (lab Mythic), run-scoped: stacks of each status (name) inflicted while the run owned Plague.</summary>
        public Dictionary<string, int> PlagueStacks = new Dictionary<string, int>();
        /// <summary>Level of an owned run skill when above 1 (a skill drafted / bought again levels up, RunTuning.MaxSkillLevel).</summary>
        public Dictionary<string, int> SkillLevels = new Dictionary<string, int>();
        /// <summary>Every skill, skill level and stat perk gained in this run, in order (result screen, HUD build size).</summary>
        public List<RunGain> Gains = new List<RunGain>();
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
        public int SkipNegativeEvents;

        // decisions
        public List<string> DraftOffer = new List<string>();
        public int DraftRerolls;
        public int DraftsWithoutLegendary;
        public List<ShopOffer> Shop = new List<ShopOffer>();
        /// <summary>Rerolls in the open shop (reset on every new shop visit: the reroll price starts at the base again).</summary>
        public int ShopRerolls;
        /// <summary>Shops opened this run (the first one always offers a skill at the cheapest price, RunSystem.StartCoins).</summary>
        public int ShopsVisited;
        public string EventId;
        public int EventTile = -1;
        public string LastEventText;
        /// <summary>What the last event / chest / campfire choice or shop visit did (result screen); null before the first.</summary>
        public RunOutcome LastOutcome;
        /// <summary>Increases with every new <see cref="LastOutcome"/> (the client shows each outcome once).</summary>
        public int OutcomeSeq;
        /// <summary>
        /// What every tile did since the last roll, in order (GDD §3.2 "every tile shows what it did"): one outcome per tile
        /// landed on (source "tile", or "battle" for a fight) and per pickup / skill effect while passing (source "pass"), plus
        /// the encounter fights (ambush miniboss) and a fight retried after a revive ("battle"). Each has RunOutcome.Tile and a
        /// RunOutcome.Seq from <see cref="TileOutcomeSeq"/> (the client animates those it has not shown yet). Decisions
        /// (events, chests, campfires, mini-games) report through <see cref="LastOutcome"/>; shops report nothing.
        /// </summary>
        public List<RunOutcome> TileOutcomes = new List<RunOutcome>();
        public int TileOutcomeSeq;

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
        /// <summary>Minibosses spawned by the awakening clock so far (RunTuning.MinibossAtMeter) and waiting to attack.</summary>
        public int MinibossesSpawned;
        public int PendingAmbushes;
        public int MinibossesDefeated;
        public int ShopBuys;

        // burrow
        public int Floor = 1;
    }

    /// <summary>Kind of one line of a <see cref="RunOutcome"/>.</summary>
    /// <summary>
    /// Kind of one line of a <see cref="RunOutcome"/>. Empty: an empty tile (Id = tile id). Nothing: the tile / choice had no
    /// effect (Id = tile id for tiles, e.g. a toll with no coins left). Avoided: a boon kept a negative tile away. StepPenalty:
    /// the next roll moves Amount steps less (Gravity Vortex).
    /// </summary>
    public enum OutcomeKind { Coins, Hp, Stat, SkillGained, SkillLost, Xp, LevelUp, Shards, AccountLoot, Awakening, AwakenRate, BossStat, Danger, Battle, Nothing, SkillLevel, SkillRoll, Empty, Avoided, StepPenalty,
        /// <summary>Run coins the account's quest bonus added (Id = the bonus name, e.g. Starglint; the Coins line shows the rest).</summary>
        CoinBonus,
        /// <summary>A quest boon: the account coin bonus is now Amount coins per gain (Id = the bonus name).</summary>
        QuestBoon,
        /// <summary>A quest encounter's step: Id "advanced" / "waits" / "complete", Amount = the chapter the story is at now.</summary>
        QuestStep }

    /// <summary>
    /// One addition to the run's build: a new skill (Level 1), a skill level-up (Level 2+) or a stat perk (Stat set).
    /// Source: "draft" (level-up draft), "mythic" (miniboss choice), "shop", "event" (events, chests, campfires), "minigame" (a mini-game's skill roll) or "boon"
    /// (granted at run start by meta skills).
    /// </summary>
    [Serializable]
    public sealed class RunGain
    {
        public string Source;
        public string SkillId;
        public int Level = 1;
        public StatMod Stat;
        public RunGain() { }
        public RunGain(string source, string skillId, int level) { Source = source; SkillId = skillId; Level = level; }
    }

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
        /// <summary>"event", "treasure", "campfire", "minigame" or "quest" (decision results, LastOutcome); tile outcomes: "tile", "pass", "battle" (TileOutcomes).</summary>
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
        /// <summary>Tile outcomes (ActiveRun.TileOutcomes): the run tile index, its tile def id, the sequence number; −1 / null otherwise.</summary>
        public int Tile = -1;
        public string TileId;
        public int Seq;
    }

    /// <summary>
    /// One granted run reward with its source. Doublable by the result-screen ad: run loot (pending loot found in the run,
    /// Burrow floor rewards included), the level's completion rewards and the completion loot rolls, as granted (Biomass surge
    /// and run-reward modifiers included). Not doublable: first-clear milestone chests / portal bonuses, feat Diamonds and rare
    /// drops (one-time or rare by design). Rewards stashed by RewardGate (no use yet) are neither listed nor doubled.
    /// </summary>
    [Serializable]
    public sealed class RunRewardLine
    {
        public const string RunLoot = "run_loot", Completion = "completion", CompletionRoll = "completion_roll",
            Milestone = "milestone", Feat = "feat", RareDrop = "rare_drop";
        public Reward Reward;
        public string Source;
        public bool Doublable;
        /// <summary>True once RunDoubleRewards granted this line a second time.</summary>
        public bool Doubled;

        public static bool IsDoublable(string source) => source == RunLoot || source == Completion || source == CompletionRoll;
    }

    [Serializable]
    public sealed class RunSummary
    {
        public string RunId;
        public string LevelId;
        public RunMode Mode;
        public bool Won;
        /// <summary>
        /// Everything granted for this run, in total (result screen): after RunDoubleRewards the doubled currencies show their
        /// doubled amounts and the second copies of item drops are listed too.
        /// </summary>
        public List<Reward> Granted = new List<Reward>();
        /// <summary>The single amounts the "double run rewards" ad pays again (see RunRewardLine.IsDoublable).</summary>
        public List<Reward> Doublable = new List<Reward>();
        /// <summary>One line per granted reward with its source and whether the ad doubles it / doubled it (result screen badges).</summary>
        public List<RunRewardLine> Rewards = new List<RunRewardLine>();
        /// <summary>What RunDoubleRewards granted (empty before).</summary>
        public List<Reward> DoubledGranted = new List<Reward>();
        /// <summary>True if this run used one of the day's Biomass surge runs (RunTuning.DailyBiomassBonusRuns).</summary>
        public bool BiomassBonus;
        /// <summary>True once RunDoubleRewards was used for this run.</summary>
        public bool Doubled;
        public List<int> NewFeats = new List<int>();
        public int Rolls;
        public int Level;
        public int Floors;
        public int XpTotal;
        /// <summary>Account-loot opportunities offered in the run (Burrow: of its last floor).</summary>
        public int AccountLootOffers;
        /// <summary>Account-loot items found during the run (before completion rolls; Burrow: floor rewards included).</summary>
        public int AccountLootDrops;
        /// <summary>The run's build gains (skills, skill levels, stat perks) with their sources (result screen).</summary>
        public List<RunGain> Gains = new List<RunGain>();
        public int Minibosses;
        public int ShopBuys;
        /// <summary>The level's longest survived run in rolls, this one included ("Longest Journey").</summary>
        public int LongestRolls;
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
