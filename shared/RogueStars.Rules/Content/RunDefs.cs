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
        Hazard, Shard, Pickup, BossTile, BossGate,
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
        public double DrKnee = 10, DodgeKnee = 10, CritKnee = 15, StatusResistKnee = 10, ParryKnee = 10;
        public double MaxDamageReduction = 90, MaxDodge = 75, MaxCrit = 100, MaxParry = 60;
        /// <summary>Consecutive dodges multiply the dodge odds by this per previous dodge.</summary>
        public double DodgeDecay = 0.8;
        /// <summary>Combo and counter chains multiply their chance by this per link.</summary>
        public double ChainDecay = 0.5;
        public int MaxChain = 6;
        public double BaseCritDamage = 50;
        public double BaseParryReduction = 40, MaxParryReduction = 90;
        public double RageMax = 100, RageOverfillMax = 125, RagePerAction = 25, RagePerHitTaken = 10;
        /// <summary>OnLowHp fires once per battle when HP drops below this %.</summary>
        public double LowHpTriggerPct = 30;
        /// <summary>Conditional bonus thresholds: "vs &gt;70% HP" and "vs &lt;40% HP".</summary>
        public double HighHpCondPct = 70, LowHpCondPct = 40;
        public int LogCap = 800;
        public int MaxFightersPerSide = 8;
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
        /// <summary>Element stat that scales the DoT power (FireDamage for Burn...).</summary>
        public StatId ElementStat = StatId.Atk;
        public bool HasElement;
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
        public int Xp = 4;
        public int Coins = 3;
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
        /// <summary>Meter points per roll (meter 0..100).</summary>
        public double AwakenPerRoll = 6;
        public List<BossAwakenAction> Actions = new List<BossAwakenAction>();
        /// <summary>Extra boss stat % when the meter is full.</summary>
        public double FullyAwakeStatPct = 30;
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
        /// <summary>1-based index in its portal.</summary>
        public int Index;
        /// <summary>Position on the global threat curve (see ContentDb.ThreatMultiplier).</summary>
        public int Threat;
        public int BoardLength = 28;
        public double BranchChance = 0.18;
        public double LoopChance = 0.5;
        public int Dice = 1;
        public int MaxRolls = 18;
        public string TilePoolId;
        public List<FixedTile> FixedTiles = new List<FixedTile>();
        public string BossId;
        public List<string> EnemyPool = new List<string>();
        public List<string> ElitePool = new List<string>();
        public string MinibossId;
        public string LootTableId;
        public string ShopTableId = "shop_standard";
        public List<Reward> CompletionRewards = new List<Reward>();
        /// <summary>Extra loot-table rolls on completion.</summary>
        public int CompletionLootRolls = 2;
        /// <summary>
        /// Rare drops rolled once when the level (Burrow: floor) is beaten (run RNG); pending like loot, paid unscaled on a
        /// won run (no run-reward modifiers, not doubled by the double-rewards ad). E.g. Awaken Cores on deep Burrow floors.
        /// </summary>
        public List<RareDrop> RareDrops = new List<RareDrop>();
        public long StaminaCost = 5;
        public double RewardMultiplier = 1;
        public LevelModifiers Modifiers = new LevelModifiers();
        /// <summary>Feat 2: worm HP ≥ this % at the end. Feat 3: boss beaten within this many rounds.</summary>
        public double FeatHpPct = 50;
        public int FeatBossRounds = 15;
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
    }

    public enum RunEffectKind
    {
        Coins, HealPct, DamagePct, StatPercent, GainSkill, GainSpecificSkill, UpgradeSkill, LoseRandomSkill,
        Loot, PendingReward, BossStatPct, Meter, Battle, Xp, Shards, Danger, StrengthenEnemies,
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
    }

    [Serializable]
    public sealed class ShopTableDef
    {
        public string Id;
        public int SkillOffers = 3;
        public Dictionary<Rarity, double> RarityWeights = new Dictionary<Rarity, double>();
        public Dictionary<Rarity, long> SkillPrices = new Dictionary<Rarity, long>();
        public double HealPct = 35;
        public long HealPrice = 30;
        public List<StatMod> StatOffers = new List<StatMod>();
        public long StatPrice = 40;
        public long RerollBase = 10, RerollStep = 10;
    }

    /// <summary>Run-wide tunables (dice, xp, draft, danger, revive...).</summary>
    [Serializable]
    public sealed class RunTuning
    {
        public int XpBase = 8, XpPerLevel = 6;
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
        public double MinibossBonusPct = 50;
        public double CampfireEnemyBonusPct = 12;
        public int CampfireRadius = 4;
        public long CoinsPerEnemyLevel = 4;
        public long DraftEmptyCoins = 20;
        /// <summary>Global scale on enemy base HP/ATK (threat 0 balance knob for a fresh worm).</summary>
        public double EnemyHpScale = 0.7, EnemyAtkScale = 0.6;
        /// <summary>Worm heals this % of max HP after every won battle.</summary>
        public double HealAfterBattlePct = 8;
        public int MaxStepsGuard = 400;
        /// <summary>Fallback worm stats if no progression contributor provided HP (prototype safety net).</summary>
        public List<StatMod> FallbackWormStats = new List<StatMod>();
        public int LabSlots = 1;
    }

    /// <summary>
    /// Main campaign layout (content): main portals of <see cref="LevelsPerPortal"/> levels, generated from biome
    /// templates. Main level n (1-based, global) sits at threat position <see cref="ThreatAt"/>(n) on the threat curve;
    /// the integer part is LevelDef.Threat, the fraction becomes a small enemy HP/ATK bonus, so late levels can climb
    /// more gently than one curve step per level. Sized with tools/RogueStars.Sim (docs/balance/progression-report.md).
    /// </summary>
    [Serializable]
    public sealed class CampaignDef
    {
        public int Portals = 50;
        public int LevelsPerPortal = 10;
        /// <summary>Levels 1..LinearUntil advance one threat step per level (the original 3 portals).</summary>
        public int LinearUntil = 30;
        /// <summary>Threat steps per level after LinearUntil, for each later portal band (last entry repeats).</summary>
        public List<double> LateSlopes = new List<double> { 0.4, 0.3, 0.12, 0.12, 0.1, 0.08, 0.07, 0.06 };
        /// <summary>Portals per entry of LateSlopes.</summary>
        public int SlopeBandPortals = 3;
        /// <summary>Stamina per main run: 5 + portal index, capped.</summary>
        public long StaminaBase = 5, StaminaMax = 10;
        /// <summary>
        /// Enemy HP/ATK % per biome family (ember, glacier, static, void) on top of the threat curve: the void family's
        /// roster (lifesteal boss, bleed/poison packs) is markedly stronger, so main portals even it out.
        /// </summary>
        public List<double> FamilyStrengthPct = new List<double> { 0, 0, 0, -25 };

        /// <summary>Main level that opens the Dark Side (feature gate, ContentDb.Onboarding); its levels start just below it.</summary>
        public int DarkSideFromLevel = 40;
        /// <summary>Burrow / Dark Side portal order (after every main portal).</summary>
        public int SidePortalOrder = 1000;
        /// <summary>Main board length: 24 + level index + 2 per portal, the portal part capped here.</summary>
        public int BoardGrowthPortals = 6;

        /// <summary>Reward scale of portal index i (0-based): (i+1) × RewardGrowth^max(0, i−2).</summary>
        public double RewardGrowth = 1.0;

        public double ThreatAt(int n)
        {
            if (n <= LinearUntil) return n - 1;
            double t = LinearUntil - 1;
            int per = Math.Max(1, LevelsPerPortal * SlopeBandPortals);
            for (int k = LinearUntil + 1; k <= n; k++)
            {
                int band = (k - LinearUntil - 1) / per;
                t += LateSlopes[Math.Min(band, LateSlopes.Count - 1)];
            }
            return t;
        }

        public double RewardScale(int portalIndex) => (portalIndex + 1) * Math.Pow(RewardGrowth, Math.Max(0, portalIndex - 2));
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

    [Serializable]
    public sealed class LabLevelDef
    {
        public string SkillId;
        public List<Cost> Costs = new List<Cost>();
        public long DurationMs;
    }

    /// <summary>Laboratory research line: level N unlocks/upgrades the draft-pool skill Levels[N-1].SkillId.</summary>
    [Serializable]
    public sealed class LabResearchDef
    {
        public string Id;
        public string Name;
        public List<LabLevelDef> Levels = new List<LabLevelDef>();
        public bool DefaultResearched;
    }
}
