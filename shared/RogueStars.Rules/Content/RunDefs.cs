using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    // Content definition classes for the Run workstream (board, battle tuning, enemies, bosses, events,
    // shops, loot, laboratory). Data only; filled in ContentDb.Run.cs / ContentDb.Skills.cs.

    public enum PortalKind { Main, Burrow, DarkSide }

    /// <summary>Tile category: decides how a tile resolves when landed on / passed.</summary>
    public enum TileCategory
    {
        Start, Empty, Enemy, Elite, Miniboss, StatBuff, StatDebuff, Loot, Treasure, Event, Campfire, Shop,
        Hazard, Shard, Pickup, BossTile, BossGate, Minigame,
        /// <summary>A Run Quest encounter (placed on scheduled runs only, RunSystem.Quests; forces a stop).</summary>
        Quest,
    }

    /// <summary>Tile valence colour for the client (and for "skip negative events" boons).</summary>
    public enum TileValence { Neutral, Positive, Negative, Enemy, Elite, Boss, Special }

    /// <summary>Tunable numbers of the auto-battle (GDD §4). Read by BattleSimulator.</summary>
    [Serializable]
    public sealed class BattleTuning
    {
        public int RoundCap = 30;
        public int BossRoundCap = 40;
        public int ArenaRoundCap = 30;
        /// <summary>DEF floor: damage is at least this % of the bonus-adjusted attack.</summary>
        public double DefFloorPct = 10;
        public double DrKnee = 10, DodgeKnee = 10, CritKnee = 15, StatusResistKnee = 10;
        public double MaxDamageReduction = 90, MaxDodge = 75, MaxCrit = 100;
        /// <summary>Consecutive dodges multiply the dodge odds by this per previous dodge.</summary>
        public double DodgeDecay = 0.8;
        /// <summary>Combo and counter chains multiply their chance by this per link.</summary>
        public double ChainDecay = 0.5;
        public int MaxChain = 6;
        public double BaseCritDamage = 50;
        public double RageMax = 100, RageOverfillMax = 125, RagePerAction = 25, RagePerHitTaken = 10;
        /// <summary>OnLowHp fires once per battle when HP drops below this %.</summary>
        public double LowHpTriggerPct = 30;
        /// <summary>Conditional bonus thresholds: "vs &gt;70% HP" and "vs &lt;40% HP".</summary>
        public double HighHpCondPct = 70, LowHpCondPct = 40;
        public int LogCap = 800;
        public int MaxFightersPerSide = 8;
        /// <summary>
        /// Sidekicks (aliens, drones): each of their turns adds Speed × (1 + the side worm's SidekickHaste %) charge; at SidekickActAt
        /// they act once (their ability) and spend it. A Common alien (Speed 34) acts about every 3 rounds; battles start at
        /// SidekickStartCharge. At most one own action per turn (extra actions come from Command and other AllyAction effects).
        /// </summary>
        public double SidekickActAt = 100, SidekickStartCharge = 50;
        /// <summary>
        /// Aliens pass: sidekicks have no stats. Their hits are a % of the worm's ATK, heals and shields a % of its max HP, both
        /// × (1 + the side's alien power %). At most MaxExtraSidekickActions extra sidekick actions per round on a side (Command,
        /// Tidecaller, Swarm Link, Hivemind chains).
        /// </summary>
        public int MaxExtraSidekickActions = 6;
        /// <summary>Hivemind / a drone's signature trigger: a Strike signature (Flurry, Riposte, Echo) strikes for HiveStrikePct % of
        /// the worm's ATK, Arc jumps for HiveArcPct %.</summary>
        public double HiveStrikePct = 30, HiveArcPct = 25;
        /// <summary>Hits a worm's guards (Block abilities) can hold at once.</summary>
        public int MaxGuards = 3;
        /// <summary>Attack multipliers (% of ATK) per attack style and attack kind.</summary>
        public Dictionary<AttackStyle, AttackStyleDef> Styles = new Dictionary<AttackStyle, AttackStyleDef>();
        public Dictionary<StatusId, StatusDef> Statuses = new Dictionary<StatusId, StatusDef>();
    }

    [Serializable]
    public sealed class AttackStyleDef
    {
        public double Basic = 100, Combo = 60, Counter = 60, Rage = 200;
        public AttackStyleDef() { }
        public AttackStyleDef(double basic, double combo, double counter, double rage) { Basic = basic; Combo = combo; Counter = counter; Rage = rage; }
    }

    [Serializable]
    public sealed class StatusDef
    {
        public StatusId Id;
        public int MaxStacks = 1;
        /// <summary>Default duration in the affected fighter's turns.</summary>
        public int DefaultDuration = 2;
        /// <summary>Damage per stack per turn as % of the applier's ATK (DoT). Ignores DEF, respects DR.</summary>
        public double DotAtkPct;
        public bool IgnoresShield;
        public bool SkipsTurn;
        /// <summary>Extra damage taken per stack (Shock, Freeze).</summary>
        public double DamageTakenPctPerStack;
        /// <summary>Healing received reduction (Poison).</summary>
        public double HealReductionPct;
        /// <summary>Per-target cooldown (rounds) before the status can be applied again.</summary>
        public int ApplyCooldownRounds;
        /// <summary>Charm: the fighter attacks its own side instead.</summary>
        public bool Charms;
        /// <summary>Element stat that scales the DoT power (FireDamage for Heat...).</summary>
        public StatId ElementStat = StatId.Atk;
        public bool HasElement;
        /// <summary>Never expires by turns (Heat, Radiation, Bleed): only death, a Supernova or a remove / consume effect clears it.</summary>
        public bool Permanent;
        /// <summary>
        /// Supernova (Heat): when the stacks reach this, the fighter explodes for <see cref="BurstAtkPct"/>% of the heater's ATK
        /// (× its element and Supernova damage) to every targetable fighter of its own side (itself included), the heater's
        /// side's worm takes <see cref="BurstRecoilPct"/>% of its max HP (less its SupernovaGuard), and the stacks reset. 0 = no burst.
        /// </summary>
        public int BurstAtStacks;
        public double BurstAtkPct, BurstRecoilPct;
        /// <summary>Damage dealt by the affected fighter per stack (negative = less: Chill).</summary>
        public double DamageDealtPctPerStack;
        /// <summary>Each stack applied removes one stack of this status (Chill cools Heat).</summary>
        public StatusId Cools;
        /// <summary>Landing this status removes every stack of that status from the target (CC pass: any Freeze resets Chill to 0).</summary>
        public StatusId Clears;
    }

    /// <summary>Enemy combatant definition at threat 0. HP/ATK/DEF scale with ContentDb.ThreatMultiplier.</summary>
    [Serializable]
    public sealed class EnemyDef
    {
        public string Id;
        public string Name;
        public bool Elite;
        public bool Boss;
        public AttackStyle Style = AttackStyle.Claw;
        public List<StatMod> Stats = new List<StatMod>();
        public List<string> SkillIds = new List<string>();
        /// <summary>Run coins of a beaten enemy: 8 (was 6: the 20% shorter runs of 2026-11-27 keep their shop buys).</summary>
        public int Xp = 4;
        public int Coins = 8;
        /// <summary>
        /// The sprite the client draws for an enemy without art of its own (an enemy id of its biome, drawn at the elite scale);
        /// null = its own id / its family's sprite. Elites without art of their own use it until they get theirs.
        /// </summary>
        public string ArtId;
        /// <summary>
        /// Def exists but its client art is not imported yet: biome rosters skip it (ContentDb.BiomeRoster) until the flag is cleared
        /// in ContentDb.Run (BiomeArtPending), so nothing without art reaches a live pool. Flip it when the sprite lands.
        /// </summary>
        public bool ArtPending;
    }

    /// <summary>Boss board action at an awakening threshold.</summary>
    public enum BossActionKind { BuffEnemyTiles, TransformTiles, Summon, BuffBoss }

    [Serializable]
    public sealed class BossAwakenAction
    {
        /// <summary>Meter value (0..100) at which the action fires once.</summary>
        public double AtMeter;
        public BossActionKind Kind;
        /// <summary>% bonus (BuffEnemyTiles/BuffBoss/Summon tile strength).</summary>
        public double Value;
        public int Count;
        /// <summary>Tile id for TransformTiles/Summon.</summary>
        public string Param;
        public string Text;
    }

    [Serializable]
    public sealed class BossDef
    {
        public string Id;
        public string Name;
        public string EnemyId;
        /// <summary>Dedicated boss tile ids placed on the board.</summary>
        public List<string> BossTileIds = new List<string>();
        public int BossTileCount = 3;
        /// <summary>
        /// Meter points per roll (meter 0..100). The meter is the run's clock: the boss comes when it is full (or at the level's
        /// roll limit), so 100 / AwakenPerRoll is the base run length in rolls before boss tiles, events and skills shift it:
        /// 4.5 = ~22 rolls (owner 2026-11-27: "runs are too long, shorten required boss rage by 20%"; was 3.6 = ~28 rolls).
        /// </summary>
        public double AwakenPerRoll = 4.5;
        public List<BossAwakenAction> Actions = new List<BossAwakenAction>();
        /// <summary>Extra boss stat % when the meter is full.</summary>
        public double FullyAwakeStatPct = 15;
        /// <summary>Enemy ids that join the boss fight.</summary>
        public List<string> Escorts = new List<string>();
    }

    [Serializable]
    public sealed class TileDef
    {
        public string Id;
        public string Name;
        public string Text;
        public TileCategory Category;
        public TileValence Valence;
        public int EnemyLevel;
        public StatId Stat;
        /// <summary>Stat % for buff/debuff tiles, HP % for hazards, meter delta for boss tiles...</summary>
        public double Value;
        public long Coins;
        public int PushBack;
        public double MeterDelta;
        public double BossStatPct;
        public string EventId;
        public string LootTableId;
        public bool TriggersOnPass;
        public bool ForceStop;
        /// <summary>Appears at most once per board (never chosen as a replacement).</summary>
        public bool Once;
        /// <summary>Replaced from the pool after it resolved.</summary>
        public bool Consumable = true;
        /// <summary>At most this many per run from the tile pools (0 = no limit; RunTileLimits).</summary>
        public int MaxPerRun;
        /// <summary>
        /// Level-unlocked run content: never picked from a pool on a main level (Normal mode) below this number (0 = always;
        /// RunTileLimits). Fixed tiles are per level (LevelDef.FixedTiles).
        /// </summary>
        public int MinMainLevel;
    }

    [Serializable]
    public sealed class WeightedId
    {
        public string Id;
        public double Weight;
        public WeightedId() { }
        public WeightedId(string id, double weight) { Id = id; Weight = weight; }
    }

    [Serializable]
    public sealed class FixedTile
    {
        /// <summary>Position along the main path (0..1).</summary>
        public double At;
        public string TileId;
        public FixedTile() { }
        public FixedTile(double at, string tileId) { At = at; TileId = tileId; }
    }

    [Serializable]
    public sealed class LootEntry
    {
        public double Weight;
        public string Kind = RewardKinds.Currency;
        public string Id;
        public long Min = 1, Max = 1;
        public int Rarity;
        public LootEntry() { }
        public LootEntry(double weight, string kind, string id, long min, long max, int rarity = 0)
        { Weight = weight; Kind = kind; Id = id; Min = min; Max = max; Rarity = rarity; }
    }

    [Serializable]
    public sealed class LootTableDef
    {
        public string Id;
        public List<LootEntry> Entries = new List<LootEntry>();
    }

    /// <summary>Extreme or mode modifiers on a level (Dark Side).</summary>
    [Serializable]
    public sealed class LevelModifiers
    {
        public double EnemyAtkPct, EnemyHpPct, EnemyDefPct;
        /// <summary>Change to all healing in the run (e.g. -50).</summary>
        public double HealPct;
        public double AwakeningPct;
        public double DangerPct;
        public string Text;
    }

    [Serializable]
    public sealed class LevelDef
    {
        public string Id;
        public string Name;
        public string PortalId;
        /// <summary>1-based index in its portal (main levels: 1, every main level is its own portal; see <see cref="Number"/>).</summary>
        public int Index;
        /// <summary>Position on the global threat curve (see ContentDb.ThreatMultiplier).</summary>
        public int Threat;
        /// <summary>Spine tiles per board segment (the board is generated ahead of the worm segment by segment, RunBoard).</summary>
        public int BoardLength = 28;
        public double BranchChance = 0.18;
        public int Dice = 1;
        /// <summary>Hard roll limit (the awakening meter normally brings the boss first, ~23 rolls; was 45 with ~30-roll runs).</summary>
        public int MaxRolls = 36;
        public string TilePoolId;
        public List<FixedTile> FixedTiles = new List<FixedTile>();
        public string BossId;
        public List<string> EnemyPool = new List<string>();
        /// <summary>
        /// The level's elites (its biome's three): the elite fights draw from it, and so do the lairs' minibosses (playtest
        /// 0.1.359: it was always the same one): each run shuffles the pool with its own seed (RunSystem.MinibossOrder), so the
        /// run's two lairs hold two different elites and runs differ.
        /// </summary>
        public List<string> ElitePool = new List<string>();
        /// <summary>The level's headline miniboss (the first of <see cref="ElitePool"/>; run quests lead their battles with it).</summary>
        public string MinibossId;
        public string LootTableId;
        public string ShopTableId = "shop_standard";
        public List<Reward> CompletionRewards = new List<Reward>();
        /// <summary>
        /// Extra loot-table rolls on completion: the main account-loot payout of a run (3: the scattered tile loot of the old
        /// short runs moved here; ordinary tiles pay in-run resources only).
        /// </summary>
        public int CompletionLootRolls = 3;
        /// <summary>
        /// Rare drops rolled once when the level (Burrow: floor) is beaten (run RNG); pending like loot, paid unscaled on a
        /// won run (no run-reward modifiers; the double-rewards ad doubles them like every listed reward). E.g. Awaken Cores on deep Burrow floors.
        /// </summary>
        public List<RareDrop> RareDrops = new List<RareDrop>();
        /// <summary>Parts v2: the first Normal clear (the boss beaten for the first time) gives one random Rare or Epic part.</summary>
        public bool FirstClearPart;
        /// <summary>
        /// Main campaign: one-time rewards of the first Normal clear (milestone chest every 10 levels, bonus chest
        /// every 50; <see cref="CampaignDef"/>). Paid unscaled (the double-rewards ad doubles them too), never again on later clears.
        /// </summary>
        public List<Reward> FirstClearRewards = new List<Reward>();
        /// <summary>Main levels: global level number (1, 2, 3, … endless). Dark Side: its level number. 0 elsewhere.</summary>
        public int Number;
        /// <summary>Main levels: the campaign biome id (CampaignBiomeDef.Id; the client's board art). Null elsewhere.</summary>
        public string Biome;
        /// <summary>Main levels: the biome reward currency (paid ×CampaignDef.BiomeRewardMult on a won run). Null elsewhere.</summary>
        public string BiomeResource;
        /// <summary>
        /// Main levels whose biome resource has no completion line (Grain, Fuel, Hero XP, Gear Cores, Data): one guaranteed roll of
        /// it (Min–Max, before the ×2) is added to the completion. 0 = none.
        /// </summary>
        public long BiomeRollMin, BiomeRollMax;
        /// <summary>Main levels: the biome's first visit (the level of its unlock slot; the client's "New biome" banner).</summary>
        public bool BiomeDebut;
        public long StaminaCost = 5;
        public double RewardMultiplier = 1;
        public LevelModifiers Modifiers = new LevelModifiers();
        /// <summary>Feat 2: worm HP ≥ this % at the end. Feat 3: boss beaten within this many rounds.</summary>
        public double FeatHpPct = 50;
        public int FeatBossRounds = 15;
        /// <summary>The awakening clock sends minibosses (RunTuning.MinibossAtMeter); false on the first main levels (GDD §16).</summary>
        public bool ClockMinibosses = true;
        /// <summary>Runs start with the signature pick (and may find signatures); false below CampaignDef.SignatureChoiceFromLevel (GDD §16).</summary>
        public bool SignatureChoice = true;
    }

    [Serializable]
    public sealed class PortalDef
    {
        public string Id;
        public string Name;
        public PortalKind Kind;
        public int Order;
        public List<string> LevelIds = new List<string>();
        public string EntryCurrency = Currencies.Stamina;
        /// <summary>Overcharged variant: threat bonus, reward and stamina multipliers.</summary>
        public int OverchargedThreatBonus = 8;
        public double OverchargedRewardMultiplier = 1.8;
        public double OverchargedStaminaMultiplier = 2;
        /// <summary>Burrow: entry cost per attempt (in EntryCurrency).</summary>
        public long EntryCost;
        /// <summary>
        /// Endless portal (Dark Side): LevelIds holds only the materialized first levels; level i beyond them is generated on
        /// demand (ContentDb.PortalLevel). Main portals are endless as a sequence (ContentDb.MainPortal), each holds one level.
        /// </summary>
        public bool Endless;
    }

    /// <remarks>
    /// Params of existing kinds: <c>BossStatPct</c> "atk" / "hp" = that boss stat only (null: both); <c>StrengthenEnemies</c>
    /// with a negative value weakens the enemies ahead; <c>PendingReward</c> "biome_chest" = a level-scaled roll of the
    /// reward's currency (RunTuning.BiomeChestRange); <c>UpgradeSkill</c> "random" = a random trainable skill (no picker).
    /// </remarks>
    public enum RunEffectKind
    {
        Coins, HealPct, DamagePct, StatPercent, GainSkill, GainSpecificSkill, UpgradeSkill, LoseRandomSkill,
        Loot, PendingReward, BossStatPct, Meter, Battle, Xp, Shards, Danger, StrengthenEnemies,
        /// <summary>The boss awakens Value % faster for the rest of the run (negative: slower).</summary>
        AwakenRatePct,
        /// <summary>Gain the Resonance event's offered signature number Param ("0" / "1", ActiveRun.SignatureChoice).</summary>
        GainSignature,
    }

    /// <summary>One effect of an event outcome. Param meaning depends on Kind (see skill-conventions.md).</summary>
    [Serializable]
    public sealed class RunEffect
    {
        public RunEffectKind Kind;
        public double Value;
        public StatId Stat;
        public string Param;
        public Rarity MinRarity;
        public Reward Reward;
        public RunEffect() { }
        public RunEffect(RunEffectKind kind, double value, string param = null) { Kind = kind; Value = value; Param = param; }
    }

    [Serializable]
    public sealed class EventOutcome
    {
        public double Weight = 1;
        public string Text;
        public List<RunEffect> Effects = new List<RunEffect>();
    }

    [Serializable]
    public sealed class EventOption
    {
        public string Text;
        /// <summary>Visible risk/reward text (hidden outcomes are weighted).</summary>
        public string RiskText;
        public long CoinCost;
        public int ShardCost;
        public List<EventOutcome> Outcomes = new List<EventOutcome>();
        /// <summary>
        /// Rite Offering: the player gives up one owned run skill of <see cref="OfferFamily"/> (SkillFamily.None = a skill with no
        /// synergy) before the outcome applies; signatures can't be offered. Shown only while the run owns such a skill
        /// (RunSystem.OfferChoices); the pick goes in RunChooseEventOption.SkillId.
        /// </summary>
        public bool Offering;
        public SkillFamily OfferFamily;
    }

    [Serializable]
    public sealed class EventDef
    {
        public string Id;
        public string Name;
        public string Text;
        public List<EventOption> Options = new List<EventOption>();
        /// <summary>Option chosen by auto mode / RunAutoStep (must be always affordable).</summary>
        public int DefaultOption;
        /// <summary>Counts as a negative event for "skip negative events" boons.</summary>
        public bool Negative;
        /// <summary>
        /// The event's art: "npc:&lt;id&gt;" (a character bust, Art/Run/Characters) or "prop:&lt;id&gt;" (Art/Run/Props). The client
        /// draws its placeholder until the file is imported. Null: the client's own mapping (RunScenes.Look).
        /// </summary>
        public string Art;
        /// <summary>Biome events: the campaign biome id (CampaignBiomeDef.Id) whose event tiles may draw it; null = shared / tile-bound.</summary>
        public string Biome;
        /// <summary>The biome's Rite (its 5th event: RunTuning.RiteWeight, only in the first half of the run, RunSystem.RiteOpen).</summary>
        public bool Rite;

        /// <summary>Some option can pay account loot (pending meta rewards): an account-loot opportunity (capped per run).</summary>
        public bool GrantsAccountLoot()
        {
            foreach (var o in Options)
                foreach (var oc in o.Outcomes)
                    foreach (var e in oc.Effects)
                        if (e.Kind == RunEffectKind.Loot || e.Kind == RunEffectKind.PendingReward) return true;
            return false;
        }
    }

    /// <summary>
    /// The tile mix of one board segment (RunBoard.AppendSegment): pool weights × the multiplier of the tile's kind, so every
    /// stretch of the path feels a little different (hunting grounds, ruins, badlands, quiet valleys).
    /// </summary>
    [Serializable]
    public sealed class SegmentTheme
    {
        public string Name;
        public double Enemy = 1, Reward = 1, Event = 1, Hazard = 1, Buff = 1;

        public double Weight(TileDef d)
        {
            switch (d.Category)
            {
                case TileCategory.Enemy: case TileCategory.Elite: return Enemy;
                case TileCategory.Loot: case TileCategory.Pickup: case TileCategory.Shard: return Reward;
                case TileCategory.Event: case TileCategory.Treasure: return Event;
                case TileCategory.Hazard: case TileCategory.StatDebuff: return Hazard;
                case TileCategory.StatBuff: return Buff;
                default: return 1;
            }
        }
    }

    /// <summary>How a board segment is laid out in 2D (RunBoard layout pass): the path's heading program and the shape of its forks.</summary>
    public enum BoardLayoutKind
    {
        /// <summary>Long straight with gentle drift; forks are side loops (one arm runs straight on, the other swings out and back).</summary>
        Straight,
        /// <summary>Switchbacks: the heading alternates left / right every few tiles (rounded corners, turn rate limited).</summary>
        ZigZag,
        /// <summary>One slow sweep: the heading follows a long sine, big S-bends across the whole width.</summary>
        WideCurve,
        /// <summary>The path leans hard to one side and curls (up to the max heading) before hooking back: hooks / half-spirals.</summary>
        Hook,
        /// <summary>A calm trunk with more forks, each a symmetric diamond (both arms bulge out and meet again).</summary>
        Diamonds,
    }

    /// <summary>One segment layout style (RunTuning.BoardLayouts): picked per segment by weight.</summary>
    [Serializable]
    public sealed class BoardLayoutDef
    {
        public string Name;
        public BoardLayoutKind Kind;
        public double Weight = 1;
        /// <summary>Multiplier on the level's fork chance in this segment.</summary>
        public double BranchMult = 1;
        /// <summary>Forks are side loops (one straight arm) instead of diamonds.</summary>
        public bool SideLoops;
        /// <summary>Heading amplitude (degrees off "forward") and period (tiles) of the layout's heading program.</summary>
        public double Amplitude = 45;
        public double Period = 12;
    }

    [Serializable]
    public sealed class ShopTableDef
    {
        public string Id;
        /// <summary>Skill offers per shop fill (playtest 0.1.325: 3 skills, the support offers come on top).</summary>
        public int SkillOffers = 3;
        public Dictionary<Rarity, double> RarityWeights = new Dictionary<Rarity, double>();
        public Dictionary<Rarity, long> SkillPrices = new Dictionary<Rarity, long>();
        public double HealPct = 35;
        public long HealPrice = 30;
        public List<StatMod> StatOffers = new List<StatMod>();
        public long StatPrice = 40;
        public long RerollBase = 10, RerollStep = 10;
        /// <summary>
        /// Support offers per shop fill on top of the skills (playtest 0.1.325): this many distinct stat perks (from StatOffers)
        /// always, and the heal as well while the worm is below <see cref="HealOfferBelowPct"/> of its max HP.
        /// </summary>
        public int StatPerkOffers = 1;
        /// <summary>The heal is offered (next to the stat perks) only while the worm's HP is below this % of its max.</summary>
        public double HealOfferBelowPct = 60;
        /// <summary>Chance (%) that one skill offer of a shop fill is on SALE at <see cref="SalePct"/> off (its list price is shown struck through).</summary>
        public double SaleChancePct = 20;
        public double SalePct = 50;
    }

    /// <summary>Run-wide tunables (dice, xp, draft, danger, revive...).</summary>
    [Serializable]
    public sealed class RunTuning
    {
        /// <summary>
        /// Run XP needed for the next level: XpBase + XpPerLevel × (level − 1) (RunSystem.XpNeeded). One XP grant (a battle, a
        /// pickup, an event outcome) gives at most one level-up; the overflow carries (up to XpBankLevels more levels' worth)
        /// and pays one level per roll, see RunSystem.AddXp. 2 + 1 per level (was 3 + 1): the 20% shorter runs of 2026-11-27
        /// (~23 rolls, ~20% fewer fights) still give ~10 drafts.
        /// </summary>
        public int XpBase = 2, XpPerLevel = 1;
        /// <summary>Synergies every player has from the start (the others unlock by campaign level: CampaignDef.SignatureUnlocks). Poison = Radiation.</summary>
        public List<SkillFamily> StarterSynergies = new List<SkillFamily> { SkillFamily.Fire, SkillFamily.Poison, SkillFamily.Bleed, SkillFamily.Combo };
        /// <summary>Owned skills the campfire's Train offers (run UI glitches #16): the lowest levels first (RunSystem.TrainChoices).</summary>
        public int TrainChoices = 3;
        /// <summary>Signatures offered at run start (1 of N).</summary>
        public int SignatureChoices = 3;
        /// <summary>Owned skills of the signature's synergy (the signature itself not counted) at which it awakens to level 2, 3.</summary>
        public int[] SignatureAwakenAt = { 3, 6 };
        /// <summary>Offer weight × this for skills of the run's signature synergy (drafts, Mythic choices, shops).</summary>
        public double SignatureFamilyWeight = 1.4;
        /// <summary>Signatures a run can hold (the start pick plus ones found at minibosses and Resonance events).</summary>
        public int MaxSignatures = 3;
        /// <summary>% chance that a miniboss's Mythic choice offers an unowned signature in place of one Mythic.</summary>
        public double SignatureMythicChancePct = 25;
        /// <summary>Run coins the Resonance event pays instead of a signature.</summary>
        public long ResonanceCoins = 120;
        public int XpBankLevels = 2;
        /// <summary>Run coins at run start: the level's cheapest shop skill price + this margin (RunSystem.StartCoins), so the first shop always allows a purchase.</summary>
        public long StartCoinsMargin = 30;
        /// <summary>
        /// At most this many account-loot opportunities (events / guarded caches whose options pay meta loot) are offered per run
        /// (per floor in the Burrow); later ones are replaced by other events. Ordinary tiles never pay account loot.
        /// </summary>
        public int MaxAccountLootOffersPerRun = 3;
        /// <summary>
        /// Every event / Treasure Pod reward chest also pays this % of the level's completion amount of each bulk resource in
        /// <see cref="RewardChestCurrencies"/> (playtest 0.1.325: a 1-chest event ≈ 15% of the run's completion payout, a 2-chest
        /// event ≈ 30%, at every level), rounded (RunSystem.NiceAmount), on top of its loot roll.
        /// </summary>
        public double RewardChestCompletionPct = 10;
        /// <summary>The bulk resources a reward chest scales with the level's completion rewards (never Diamonds or tokens).</summary>
        public List<string> RewardChestCurrencies = new List<string> { Currencies.Biomass, Currencies.Ore, Currencies.Data, Currencies.Fuel };
        /// <summary>
        /// Biome events (docs/design/biomes): on a main campaign level every Strange Signal event tile draws one of the level
        /// biome's events with this % chance (else a shared event by weight). No event repeats within a run; once a biome's
        /// events are used up the tile draws from the shared pool.
        /// </summary>
        public double BiomeEventPct = 40;
        /// <summary>The biome's Rite is its 5th event with this weight (the four biome events weigh 1 each).</summary>
        public double RiteWeight = 2;
        /// <summary>
        /// A biome chest (event "Ore chest", "Fuel chest", …): one roll of the resource at chapter 1 (already doubled: the biome's
        /// ×2), times the chapter's reward scale like all loot, rounded (RunSystem.NiceAmount). Min / max per currency.
        /// </summary>
        public Dictionary<string, long[]> BiomeChestRange = new Dictionary<string, long[]>
        {
            { Currencies.Biomass, new long[] { 90, 180 } }, { Currencies.Ore, new long[] { 40, 80 } }, { Currencies.Grain, new long[] { 40, 80 } },
            { Currencies.Fuel, new long[] { 20, 40 } }, { Currencies.HeroXp, new long[] { 30, 60 } }, { Currencies.GearCores, new long[] { 2, 4 } },
            { Currencies.Data, new long[] { 20, 40 } },
        };
        public int DraftChoices = 3;
        public int DraftRerollsPerRun = 1;
        /// <summary>Hidden pity: after this many draft picks without a Legendary+, an offer contains one.</summary>
        public int LegendaryPity = 7;
        public Dictionary<Rarity, double> DraftRarityWeights = new Dictionary<Rarity, double>();
        /// <summary>Weight multiplier per owned skill of the same family in the synergy slot.</summary>
        public double SynergyWeightPerOwned = 1.5;
        public double DangerPctPerLoot = 5;
        public double ReviveHpPct = 60;
        public long ReviveDiamondCost = 50;
        public long FeatDiamonds = 10;
        /// <summary>Enemy tile strength by tile level (1..3): % bonus and enemy count.</summary>
        public List<double> EnemyLevelBonusPct = new List<double> { 0, 0, 15, 30 };
        public List<int> EnemyLevelCount = new List<int> { 1, 1, 2, 3 };
        public double EliteBonusPct = 20;
        /// <summary>
        /// HP/ATK % of every board fight (enemy, elite, miniboss and event fights; not the boss): a ~23-roll run has ~8 fights,
        /// so each one is a little softer than the threat curve alone (GDD §3.3).
        /// </summary>
        public double BoardEnemyPct = -15;
        public double MinibossBonusPct = 0;
        public double CampfireEnemyBonusPct = 12;
        public int CampfireRadius = 4;
        /// <summary>Run coins per tile enemy level of a won board fight (12, was 10: shorter runs keep their coin income, GDD §3.3).</summary>
        public long CoinsPerEnemyLevel = 12;
        public long DraftEmptyCoins = 40;
        /// <summary>Global scale on enemy base HP/ATK (threat 0 balance knob for a fresh worm).</summary>
        public double EnemyHpScale = 0.7, EnemyAtkScale = 0.6;
        /// <summary>Worm heals this % of max HP after every won battle.</summary>
        public double HealAfterBattlePct = 15;
        public int MaxStepsGuard = 400;
        /// <summary>Fallback worm stats if no progression contributor provided HP (prototype safety net).</summary>
        public List<StatMod> FallbackWormStats = new List<StatMod>();
        public int LabSlots = 1;
        /// <summary>
        /// Daily Biomass surge: the first N won portal runs (Normal / Overcharged) of each reset-day pay their Biomass ×Mult,
        /// so Biomass comes mainly from runs even for short sessions (docs/design/economy-model.md).
        /// </summary>
        public int DailyBiomassBonusRuns = 3;
        public double DailyBiomassBonusMult = 2;

        // ---- build depth (GDD §3.3 "Build"): skill levels, minibosses and mythic choices --------------------------------
        // ---- board (a long path generated ahead in segments, RunBoard) ---------------------------------------------------
        /// <summary>Segments generated at the start; more are appended while the worm gets within BoardLookAhead steps of the end.</summary>
        public int InitialSegments = 2;
        /// <summary>Steps of board kept ahead of the worm beyond its largest possible roll.</summary>
        public int BoardLookAhead = 16;
        /// <summary>Tile mixes of later segments (the first segment uses the plain pool).</summary>
        public List<SegmentTheme> SegmentThemes = new List<SegmentTheme>();
        /// <summary>Fork arms: every arm has this many tiles at least / at most before it rejoins (playtest: no short cut-throughs).</summary>
        public int BranchMinArm = 8, BranchMaxArm = 11;
        /// <summary>Segment layout styles (2D path shape and fork shape), picked per segment by weight.</summary>
        public List<BoardLayoutDef> BoardLayouts = new List<BoardLayoutDef>();
        /// <summary>Grid layout: the path keeps its screen-horizontal place (PosX − PosY) within ±GridBand cells.</summary>
        public int GridBand = 8;
        /// <summary>Steps ahead of the worm in which boss awakening actions transform / summon tiles.</summary>
        public int BossActionReach = 30;

        /// <summary>
        /// Run skills have exactly three tiers, fixed per skill (owner decision: no upgrading into a better rarity): Common,
        /// Legendary, Mythic. An owned skill offered again levels up to its tier's cap: Common Lv3 (two upgrades), Legendary
        /// Lv2 (one), Mythic Lv1 (never; Mythics are standalone, build-defining skills).
        /// </summary>
        public int MaxLevelCommon = 3, MaxLevelLegendary = 2, MaxLevelMythic = 1;

        /// <summary>Level cap of a run skill of tier <paramref name="tier"/> (<see cref="RunTier"/>).</summary>
        public int MaxSkillLevelOf(Rarity tier)
        {
            var t = RunTier(tier);
            return t == Rarity.Mythic ? MaxLevelMythic : t == Rarity.Legendary ? MaxLevelLegendary : MaxLevelCommon;
        }

        /// <summary>The run tier of a rarity: Common and Rare → Common, Epic and Legendary → Legendary, Mythic → Mythic.</summary>
        public static Rarity RunTier(Rarity r) => r >= Rarity.Mythic ? Rarity.Mythic : r >= Rarity.Epic ? Rarity.Legendary : Rarity.Common;
        /// <summary>
        /// Skill levels improve the skill's OWN effect along this ladder (SkillLeveling; owner: "+15% at level one, 25% at level 2,
        /// 40% at level 3: the upgrades deepen the commitment but yield less than the first pick"). Each rung is the sum of the two
        /// below (15, 25, 40, 65, …). A Common skill climbs from rung 1 (×1, ×5/3, ×8/3 of its Lv1 numbers: +15% → +25% → +40%),
        /// a Legendary from rung 2 (×1, ×8/5: +25% → +40%), a Mythic from rung 3.
        /// </summary>
        public double[] SkillLevelLadder = { 15, 25, 40 };
        /// <summary>Trigger chances never grow past this (%); beyond it the trigger's stacks (or duration / count) grow instead.</summary>
        public double SkillLevelChanceCap = 100;
        /// <summary>Weight factor of a level-up offer (an owned skill) against a new skill in drafts and shops.</summary>
        public double LevelUpOfferWeight = 0.6;
        /// <summary>
        /// Awakening meter points at which a miniboss (a lieutenant of the boss) attacks after the move. Empty since playtest
        /// 0.1.308: every miniboss stands on the path (its lair tile, <see cref="MinibossesPerRun"/>) so the player sees it coming.
        /// </summary>
        public List<double> MinibossAtMeter = new List<double>();
        /// <summary>
        /// Miniboss lairs per run (playtest 0.1.308: 2). The level's lair (LevelDef.FixedTiles "miniboss") is placed in the first
        /// segments of the board only, one per segment, until the run has this many.
        /// </summary>
        public int MinibossesPerRun = 2;
        /// <summary>Run coins for a beaten miniboss on top of its enemy coins (it pays no XP: its reward is the mythic choice).</summary>
        public long MinibossCoins = 30;
        /// <summary>
        /// Board fights (enemies, elites, minibosses, event fights) get +this HP/ATK % per roll of the run so far: the boss's
        /// forces grow with the run while the worm's build grows from ~0 to 20–40 skills and perks. 1.0 (was 0.8 with ~28-roll
        /// runs): the ~22-roll run reaches the boss with the same ~22% growth.
        /// </summary>
        public double EnemyPctPerRoll = 1.0;
        /// <summary>Boss HP/ATK % for the bigger end-of-run build (20–40 skills and perks instead of ~9 drafts).</summary>
        public double BossBuildPct = 60;
        /// <summary>Extra HP/ATK % of each later miniboss of the run (per miniboss already beaten).</summary>
        public double MinibossStepPct = 5;
        /// <summary>Skills offered by the mythic choice after a beaten miniboss (Mythic rarity).</summary>
        public int MythicChoices = 3;
        /// <summary>Run coins instead of a mythic choice when no mythic skill is left.</summary>
        public long MythicEmptyCoins = 60;
    }

    /// <summary>
    /// A campaign biome (docs/design/biomes): its own roster (enemies, elites, 1–2 bosses alternating per visit), its biome reward
    /// (×2 of one resource) and its synergy weather. No enemy families: a shared enemy is simply listed in several rosters.
    /// </summary>
    [Serializable]
    public sealed class CampaignBiomeDef
    {
        /// <summary>Id (LevelDef.Biome; the client draws the board / backdrop art of the same id).</summary>
        public string Id;
        public string Name;
        /// <summary>Unlock slot: the main level from which it is in the rotation (1 = a start biome). The level is its first visit.</summary>
        public int From = 1;
        /// <summary>
        /// Its ground / backdrop art is in (client Art/Boards/&lt;id&gt;/background). A planned biome stays out of the rotation until then:
        /// its unlock slot waits and the next ready biome fills it (ContentDb.Rotation). Flip it when the art lands.
        /// </summary>
        public bool Ready = true;
        /// <summary>Its own roster (def ids; defs with EnemyDef.ArtPending are skipped until their art is in).</summary>
        public List<string> Enemies = new List<string>(), Elites = new List<string>(), Bosses = new List<string>();
        /// <summary>
        /// Stand-ins while its own roster's art is pending: added (in order, art-ready only) until it has CampaignDef.MinBiomeEnemies
        /// enemies / MinBiomeElites elites; StandInBoss when none of its bosses has art yet.
        /// </summary>
        public List<string> StandInEnemies = new List<string>(), StandInElites = new List<string>();
        public string StandInBoss;
        /// <summary>Biome reward: this currency pays ×CampaignDef.BiomeRewardMult on its levels.</summary>
        public string Resource;
        /// <summary>Synergy weather: Boost (None = General) +CampaignDef.WeatherBoostPct %, Weak −WeakPct % (player only).</summary>
        public SkillFamily Boost, Weak;
        public double WeakPct = 25;
        public CampaignBiomeDef() { }
        public CampaignBiomeDef(string id, string name) { Id = id; Name = name; }
    }

    /// <summary>A signature that unlocks by campaign level (no Laboratory packages): its synergy from main level <see cref="Level"/> on.</summary>
    [Serializable]
    public sealed class SignatureUnlockDef
    {
        public SkillFamily Family;
        public int Level;
        /// <summary>The onboarding feature that announces it (unlocks when level − 1 is cleared).</summary>
        public string FeatureId;
        public SignatureUnlockDef() { }
        public SignatureUnlockDef(SkillFamily family, int level, string featureId) { Family = family; Level = level; FeatureId = featureId; }
    }

    /// <summary>Synergy weather of one battle (main campaign levels only): final damage multipliers by synergy (BattleSimulator).</summary>
    [Serializable]
    public sealed class BattleWeather
    {
        /// <summary>Boosted synergy (None = General: damage of untagged skills); both sides, enemies only for tagged damage.</summary>
        public SkillFamily Boost;
        public double BoostPct;
        /// <summary>Weakened synergy: the player side only.</summary>
        public SkillFamily Weak;
        public double WeakPct;
    }

    /// <summary>
    /// Main campaign layout (content): one endless, linear sequence of levels, every level its own portal ("portal after
    /// portal") with its own biome (ContentDb.Campaign.cs). The biomes come from a deterministic seeded shuffle, the same for
    /// every player, that never repeats a biome twice in a row and brings each newly unlocked biome first (ContentDb.CampaignBiomeIndex). There
    /// are no boss levels: every level ends with its biome's boss. The first <see cref="MaterializedLevels"/> levels are
    /// materialized at start-up (ContentDb.Portals / RunLevels), every later one is generated on demand from its number.
    /// Main level n (1-based, global) sits at threat position <see cref="ThreatAt"/>(n) on the threat curve; the integer part
    /// is LevelDef.Threat, the fraction becomes a small enemy HP/ATK bonus, so late levels can climb more gently than one
    /// curve step per level. Sized with tools/RogueStars.Sim (docs/balance/progression-report.md, "Endless campaign").
    /// </summary>
    [Serializable]
    public sealed class CampaignDef
    {
        /// <summary>Main levels (each with its portal) materialized at start-up (ContentDb.Portals / RunLevels); later ones are generated on demand.</summary>
        public int MaterializedLevels = 500;
        /// <summary>
        /// Reward / difficulty rhythm only (no biome, no portal, no boss): levels are grouped in chapters of this many for the
        /// reward scale and loot table, stamina, board growth, the dice rhythm (chapter steps 6–10 roll two dice), the threat
        /// slope bands and the endless anchors (EndlessFromChapter, …). Chapter c (0-based) = levels 10c+1 … 10c+10.
        /// </summary>
        public int ChapterLevels = 10;
        /// <summary>
        /// The campaign biomes in unlock order (docs/design/biomes, built in ContentDb.CampaignBuildBiomes): 4 start biomes, then one
        /// per unlock slot (<see cref="CampaignBiomeDef.From"/>). Only <see cref="CampaignBiomeDef.Ready"/> biomes rotate; level n's
        /// biome: ContentDb.CampaignBiomeIndex. The order only changes when this list, the Ready flags or <see cref="BiomeOrderSeed"/>
        /// change. The first biome is level 1's (the tutorial run).
        /// </summary>
        public List<CampaignBiomeDef> Biomes = new List<CampaignBiomeDef>();
        /// <summary>Signatures beyond the start set (RunTuning.StarterSynergies) unlock by campaign level, one with each new biome.</summary>
        public List<SignatureUnlockDef> SignatureUnlocks = new List<SignatureUnlockDef>();
        /// <summary>A biome roster tops up with stand-ins to at least this many enemies / elites while its own art is pending.</summary>
        public int MinBiomeEnemies = 3, MinBiomeElites = 2;
        /// <summary>Biome reward: its resource pays ×this on its levels (completion, completion rolls, reward chests).</summary>
        public double BiomeRewardMult = 2;
        /// <summary>Biome reward roll for Data biomes (main levels pay no Data otherwise): DataRollMin–Max × the chapter's reward scale.</summary>
        public long BiomeDataRollMin = 10, BiomeDataRollMax = 20;
        /// <summary>Synergy weather: the boosted synergy's damage ×(1 + this %); the weakened one −CampaignBiomeDef.WeakPct %.</summary>
        public double WeatherBoostPct = 25;
        /// <summary>
        /// Main levels from this number on have weather: the tutorial levels before the signature pick have none (the level-start
        /// weather banner comes with the pick, so weather never acts unseen).
        /// </summary>
        public int WeatherFromLevel = 3;
        /// <summary>Seed of the biome order (the same for every player).</summary>
        public ulong BiomeOrderSeed = 0x5EEDB10EUL;
        /// <summary>Levels 1..LinearUntil advance one threat step per level.</summary>
        public int LinearUntil = 30;
        /// <summary>Threat steps per level after LinearUntil, for each later band of chapters (last entry repeats forever).</summary>
        public List<double> LateSlopes = new List<double> { 0.4, 0.3, 0.12, 0.12, 0.1, 0.08, 0.07, 0.06 };
        /// <summary>Chapters per entry of LateSlopes.</summary>
        public int SlopeBandChapters = 3;
        /// <summary>Stamina per main run: 5 + chapter index, capped.</summary>
        public long StaminaBase = 5, StaminaMax = 10;
        /// <summary>
        /// Gentle ramp for new players: enemy HP % and ATK % of main levels 1, 2, 3, … (on top of the threat curve; levels
        /// past the list are unchanged). Level 1 (the tutorial run) is played without parts, shops or
        /// Mythics, level 2 without shops, and worm levels unlock with level 3 (GDD §16 level unlocks), so the first levels are
        /// eased the most; levels 1–3 should almost never be lost (EarlyLevelBalanceTests).
        /// </summary>
        public List<double> EarlyLevelEnemyHpPct = new List<double> { -70, -60, -50, -48, -46, -42, -38, -34, -28, -22, -14, -7 };
        public List<double> EarlyLevelEnemyAtkPct = new List<double> { -50, -42, -35, -35, -32, -30, -27, -24, -18, -12, -8, -4 };

        public double EarlyHpPct(int level) => level >= 1 && level <= EarlyLevelEnemyHpPct.Count ? EarlyLevelEnemyHpPct[level - 1] : 0;
        public double EarlyAtkPct(int level) => level >= 1 && level <= EarlyLevelEnemyAtkPct.Count ? EarlyLevelEnemyAtkPct[level - 1] : 0;

        /// <summary>
        /// Run content introduced level by level (GDD §16 level unlocks): main levels below these numbers do not have it, and
        /// its feature (Features.RunSignatures, …) unlocks when the level before is cleared. Main levels only, every level past
        /// the first chapter has everything (numbers ≤ ChapterLevels). Two dice keep their chapter rhythm (chapter steps 6–10);
        /// forks come with them (BranchChance 0 below <see cref="ForksFromLevel"/>). Mini-games: MinigameTuning.MinMainLevel.
        /// Shops are on the board from the first run (owner, 2026-11-27); the signature pick is the second unlock (LevelDef.SignatureChoice).
        /// </summary>
        public int SignatureChoiceFromLevel = 3, MinibossesFromLevel = 5, ForksFromLevel = 6, CampfiresFromLevel = 10;
        /// <summary>
        /// The tutorial run (main level 1): its own tile pool (enemies, pickups, buffs and loot only: no events, elites, hazards
        /// or debuffs), and a faster awakening so the first boss comes after about 18 rolls (was ~22 before the shorter runs).
        /// Level 1's biome is always the first campaign biome (Lava Fields).
        /// </summary>
        public string TutorialPool = "pool_main_tutorial";
        /// <summary>The tile pool of every main level (no biome is harder or easier: difficulty comes only from the level scaling).</summary>
        public string MainPool = "pool_main";
        public double TutorialAwakeningPct = 25;

        /// <summary>First Dark Side level (the feature itself unlocks at main level 53, ContentDb.Onboarding).</summary>
        public int DarkSideFromLevel = 40;
        /// <summary>
        /// Dark Side: endless procedural levels along the campaign curve. Dark level i sits at main level
        /// DarkSideFromLevel + DarkSideSpacing × (i − 1) (minus DarkSideThreatOffset threat steps; its extreme modifiers
        /// make up the difference). The first DarkSideLevels are materialized (PortalDef.LevelIds), later ones are generated.
        /// </summary>
        public int DarkSideLevels = 150;
        public double DarkSideSpacing = 3;
        public int DarkSideThreatOffset = 5;
        /// <summary>Data per Dark Side clear (before the level's ×2.5 reward multiplier): DataBase × DataGrowth^(i − 1) up to the anchor.</summary>
        public double DarkSideDataBase = 24, DarkSideDataGrowth = 1.025;
        /// <summary>Burrow / Dark Side portal order (after every main portal).</summary>
        public int SidePortalOrder = 1000;
        /// <summary>Main board lap length: 24 + chapter step + 2 per chapter (the chapter part capped here), ×1.5 with two dice.</summary>
        public int BoardGrowthChapters = 6;
        /// <summary>Hard roll limit of main and Dark Side runs (the awakening meter normally brings the boss at ~23 rolls; was 45).</summary>
        public int MaxRolls = 36;

        /// <summary>Reward scale of chapter index i (0-based) up to the endless anchor: (i+1) × RewardGrowth^max(0, i−2).</summary>
        public double RewardGrowth = 1.0;

        // ---- endless campaign (docs/design/economy-model.md, "Endless campaign rewards") ----------------------------------
        /// <summary>
        /// Chapter index from which rewards follow the endless formula: chapter i ≥ EndlessFromChapter pays
        /// RewardScale(EndlessFromChapter − 1) × (M(i) / M(EndlessFromChapter − 1))^EndlessRewardExponent, where M is the enemy
        /// threat multiplier at the chapter's first level. Same anchor for the item-rarity shift and the boss variants.
        /// </summary>
        public int EndlessFromChapter = 50;
        /// <summary>α: per-run currencies grow as threat^α (α &lt; 1, sublinear to threat and upgrade costs).</summary>
        public double EndlessRewardExponent = 0.7;
        /// <summary>Dark Side level from which Data follows threat^α (anchor = this level's legacy Data).</summary>
        public int DarkSideEndlessFrom = 150;
        /// <summary>Run item drops (parts, runes, blueprints, aliens) of chapter i ≥ EndlessFromChapter gain
        /// 1 + (i − EndlessFromChapter) / RarityShiftChapters rarity tiers, never above <see cref="MaxDropRarity"/>.</summary>
        public int RarityShiftChapters = 25;
        public int MaxDropRarity = 3; // Legendary: Mythic only from pulls and merges
        /// <summary>From EndlessFromChapter on, a level's boss changes its variant (title, boss tiles, escort, board hazard) every N chapters.</summary>
        public int BossVariantEveryChapters = 5;
        public int BossVariants = 6;

        /// <summary>One-time milestone chest on the first clear of every MilestoneEveryLevels-th main level.</summary>
        public int MilestoneEveryLevels = 10;
        /// <summary>Parts v2: a boss first clear's part is Epic with this chance, else Rare (the Parts pull ratio 9.5 : 28).</summary>
        public double FirstClearEpicPartPct = 25;
        /// <summary>Parts v2: chance of a random Legendary part on every Dark Side clear.</summary>
        public double DarkSideLegendaryPartPct = 5;
        /// <summary>Chest Diamonds: MilestoneDiamonds × (1 + n / MilestoneGrowthLevels), rounded to 5.</summary>
        public long MilestoneDiamonds = 50;
        public int MilestoneGrowthLevels = 500;
        /// <summary>Chest tokens of each pull type (Part, Blueprint, Alien): MilestoneTokens + n / MilestoneGrowthLevels.</summary>
        public long MilestoneTokens = 1;
        /// <summary>Every BonusChestEveryLevels-th level pays the bonus chest instead: the milestone chest × BonusChestMult.</summary>
        public int BonusChestEveryLevels = 50;
        public long BonusChestMult = 4;
        /// <summary>
        /// State size: main / Dark Side progress more than this many levels behind the player's best clear is folded into the
        /// compact archives (RunState.MainArchive / DarkArchive) once the per-level records exceed twice the window.
        /// </summary>
        public int ProgressWindowLevels = 100;

        public double ThreatAt(int n)
        {
            if (n <= LinearUntil) return n - 1;
            double t = LinearUntil - 1;
            int per = Math.Max(1, ChapterLevels * SlopeBandChapters);
            int rest = n - LinearUntil;
            for (int band = 0; rest > 0; band++)
            {
                int take = band < LateSlopes.Count - 1 ? Math.Min(rest, per) : rest;
                t += take * LateSlopes[Math.Min(band, LateSlopes.Count - 1)];
                rest -= take;
            }
            return t;
        }

        public double RewardScale(int chapter) => (chapter + 1) * Math.Pow(RewardGrowth, Math.Max(0, chapter - 2));

        /// <summary>0-based chapter of main level n (reward / difficulty rhythm only).</summary>
        public int ChapterOf(int n) => Math.Max(0, n - 1) / Math.Max(1, ChapterLevels);

        /// <summary>1-based step of main level n in its chapter (1 … ChapterLevels).</summary>
        public int ChapterStep(int n) => Math.Max(0, n - 1) % Math.Max(1, ChapterLevels) + 1;

        /// <summary>Main level number at which Dark Side level i sits.</summary>
        public int DarkSideMainLevel(int i) => (int)Math.Round(DarkSideFromLevel + DarkSideSpacing * (i - 1));

        /// <summary>One-time rewards of main level n's first clear (milestone chest / bonus chest); empty otherwise.</summary>
        public List<Reward> MilestoneRewards(int n)
        {
            var list = new List<Reward>();
            if (n <= 0 || MilestoneEveryLevels <= 0 || n % MilestoneEveryLevels != 0) return list;
            long mult = BonusChestEveryLevels > 0 && n % BonusChestEveryLevels == 0 ? BonusChestMult : 1;
            double grow = 1 + (double)n / Math.Max(1, MilestoneGrowthLevels);
            long diamonds = (long)Math.Round(MilestoneDiamonds * grow / 5.0) * 5 * mult;
            long tokens = (MilestoneTokens + n / Math.Max(1, MilestoneGrowthLevels)) * mult;
            list.Add(Reward.Cur(Currencies.Diamonds, diamonds));
            list.Add(Reward.Cur(Currencies.PartTokens, tokens));
            list.Add(Reward.Cur(Currencies.BlueprintTokens, tokens));
            list.Add(Reward.Cur(Currencies.AlienTokens, tokens));
            return list;
        }
    }

    [Serializable]
    public sealed class ThreatCurve
    {
        /// <summary>mult(t) = Growth^t · (1 + Linear·t). t=0 → 1; t=30 ≈ 22; t=100 ≈ 8000 (years of content).</summary>
        public double Growth = 1.075;
        public double Linear = 0.05;
    }

    [Serializable]
    public sealed class RareDrop
    {
        public Reward Reward;
        public double ChancePct;
        public RareDrop() { }
        public RareDrop(Reward reward, double chancePct) { Reward = reward; ChancePct = chancePct; }
    }

    public enum LabNodeKind { Skill, Stat }

    /// <summary>One level of a laboratory node: a draft-pool skill (skill nodes) or a stat bonus (stat nodes, total at that level).</summary>
    [Serializable]
    public sealed class LabLevelDef
    {
        public string SkillId;
        /// <summary>Stat nodes: the node's whole bonus at this level (replaces the previous level's).</summary>
        public List<StatMod> Stats = new List<StatMod>();
    }

    /// <summary>Prerequisite of a laboratory node: another node at a minimum level.</summary>
    [Serializable]
    public sealed class LabPrereq
    {
        public string ResearchId;
        public int Level = 1;
        public LabPrereq() { }
        public LabPrereq(string researchId, int level) { ResearchId = researchId; Level = level; }
    }

    /// <summary>
    /// Laboratory node (one tree, four branches; docs/design/GDD.md §5). Skill nodes: level N unlocks/upgrades the draft-pool
    /// skill Levels[N-1].SkillId. Stat nodes: level N gives Levels[N-1].Stats to the loadout. Level 1 needs the prerequisites
    /// (any or all of <see cref="Requires"/>); higher levels need the previous level. Time and Data cost do not depend on the
    /// node but on how many levels the player has researched so far (<see cref="LabTuningDef"/>).
    /// </summary>
    [Serializable]
    public sealed class LabResearchDef
    {
        public string Id;
        public string Name;
        public string Description;
        public LabNodeKind Kind;
        /// <summary>UI grouping: "offense", "defense", "elements", "command".</summary>
        public string Branch;
        /// <summary>Depth in the branch (1 = root row).</summary>
        public int Tier = 1;
        public List<LabLevelDef> Levels = new List<LabLevelDef>();
        public bool DefaultResearched;
        public List<LabPrereq> Requires = new List<LabPrereq>();
        /// <summary>True: every prerequisite is needed; false: any one of them.</summary>
        public bool RequireAll;
        /// <summary>Feature that must be unlocked before level 1 can start (a synergy's skill nodes need its signature unlock); null = none.</summary>
        public string RequiresFeature;
    }

    /// <summary>
    /// Laboratory pacing. The k-th research level a player starts (k = levels researched or running, beyond the default
    /// ones) takes <c>BaseHours × Growth^k</c> nominal hours and costs <c>ceil(DataBase × hours^DataExponent)</c> Data,
    /// whatever the node: every research makes the next one longer and dearer. Durations are shortened by the Laboratory building boon
    /// (CitySystem.ResearchSpeedPct, Laboratory levels).
    /// Sizing (docs/design/GDD.md §5, docs/balance/progression-report.md): ~28.7k nominal hours and ~93k Data in total.
    /// </summary>
    [Serializable]
    public sealed class LabTuningDef
    {
        public double BaseHours = 1.5;
        public double Growth = 1.0355;
        /// <summary>Data cost of the k-th research: DataBase × Hours(k)^DataExponent (early research relatively dearer).</summary>
        public double DataBase = 14, DataExponent = 0.75;
        /// <summary>At most this many research slots (the Laboratory building: 1, +1 at 5★ and 10★).</summary>
        public int MaxSlots = 3;
        /// <summary>Research started while the first slot is busy takes this many times longer (1: every Laboratory slot is equal).</summary>
        public double AnnexDurationMult = 1;

        public double Hours(int k) => BaseHours * Math.Pow(Growth, Math.Max(0, k));
        public long DataCost(int k) => (long)Math.Ceiling(DataBase * Math.Pow(Hours(k), DataExponent) - 1e-9);
    }
}
