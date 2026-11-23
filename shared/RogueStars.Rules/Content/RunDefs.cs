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
        /// <summary>
        /// Meter points per roll (meter 0..100). The meter is the run's clock: the boss comes when it is full (or at the level's
        /// roll limit), so 100 / AwakenPerRoll is the base run length in rolls before boss tiles, events and skills shift it.
        /// </summary>
        public double AwakenPerRoll = 3.6;
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
        /// <summary>Hard roll limit (the awakening meter normally brings the boss first, ~30 rolls).</summary>
        public int MaxRolls = 45;
        public string TilePoolId;
        public List<FixedTile> FixedTiles = new List<FixedTile>();
        public string BossId;
        public List<string> EnemyPool = new List<string>();
        public List<string> ElitePool = new List<string>();
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
        /// won run (no run-reward modifiers, not doubled by the double-rewards ad). E.g. Awaken Cores on deep Burrow floors.
        /// </summary>
        public List<RareDrop> RareDrops = new List<RareDrop>();
        /// <summary>
        /// Main campaign: one-time rewards of the first Normal clear (milestone chest every 10 levels, portal-completion bonus
        /// every 50; <see cref="CampaignDef"/>). Paid unscaled, never doubled, never again on later clears.
        /// </summary>
        public List<Reward> FirstClearRewards = new List<Reward>();
        /// <summary>Main levels: global level number (portal 1 = 1..10, ...). Dark Side: its level number. 0 elsewhere.</summary>
        public int Number;
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
        /// <summary>
        /// Endless portal (Dark Side): LevelIds holds only the materialized first levels; level i beyond them is generated on
        /// demand (ContentDb.PortalLevel). Main portals are endless as a sequence (ContentDb.MainPortal), each has its 10 levels.
        /// </summary>
        public bool Endless;
    }

    public enum RunEffectKind
    {
        Coins, HealPct, DamagePct, StatPercent, GainSkill, GainSpecificSkill, UpgradeSkill, LoseRandomSkill,
        Loot, PendingReward, BossStatPct, Meter, Battle, Xp, Shards, Danger, StrengthenEnemies,
        /// <summary>The boss awakens Value % faster for the rest of the run (negative: slower).</summary>
        AwakenRatePct,
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
        public int XpBase = 4, XpPerLevel = 1;
        /// <summary>
        /// At most this many account-loot opportunities (events / guarded caches whose options pay meta loot) are offered per run
        /// (per floor in the Burrow); later ones are replaced by other events. Ordinary tiles never pay account loot.
        /// </summary>
        public int MaxAccountLootOffersPerRun = 3;
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
        /// HP/ATK % of every board fight (enemy, elite, miniboss and event fights; not the boss): a ~30-roll run has ~10 fights,
        /// so each one is a little softer than the threat curve alone (GDD §3.3).
        /// </summary>
        public double BoardEnemyPct = -15;
        public double MinibossBonusPct = 50;
        public double CampfireEnemyBonusPct = 12;
        public int CampfireRadius = 4;
        public long CoinsPerEnemyLevel = 4;
        public long DraftEmptyCoins = 20;
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
        public double DailyBiomassBonusMult = 4;
    }

    /// <summary>
    /// Main campaign layout (content): an endless sequence of main portals of <see cref="LevelsPerPortal"/> levels, generated
    /// from biome templates (ContentDb.Campaign.cs). The first <see cref="Portals"/> are materialized at start-up (named
    /// portals, listed in ContentDb.Portals); every later portal is generated on demand from its index and cached.
    /// Main level n (1-based, global) sits at threat position <see cref="ThreatAt"/>(n) on the threat curve; the integer part
    /// is LevelDef.Threat, the fraction becomes a small enemy HP/ATK bonus, so late levels can climb more gently than one
    /// curve step per level. Sized with tools/RogueStars.Sim (docs/balance/progression-report.md, "Endless campaign").
    /// </summary>
    [Serializable]
    public sealed class CampaignDef
    {
        /// <summary>Main portals materialized at start-up (ContentDb.Portals / RunLevels); later portals are generated on demand.</summary>
        public int Portals = 50;
        public int LevelsPerPortal = 10;
        /// <summary>Levels 1..LinearUntil advance one threat step per level (the original 3 portals).</summary>
        public int LinearUntil = 30;
        /// <summary>Threat steps per level after LinearUntil, for each later portal band (last entry repeats forever).</summary>
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
        /// <summary>
        /// Extra enemy HP/ATK % per biome family from portal <see cref="LateFamilyFromPortal"/> on (endless campaign): the glacier
        /// roster (freeze-all boss, shields, counters) stalls late-game worms for weeks every fourth portal without it.
        /// </summary>
        public List<double> LateFamilyStrengthPct = new List<double> { 0, -20, 0, 0 };
        public int LateFamilyFromPortal = 40;
        /// <summary>
        /// Gentle ramp for new players: enemy HP % and ATK % of main levels 1, 2, 3, … (on top of the threat curve; levels
        /// past the list are unchanged). Level 1 is played without parts (onboarding v2) and worm levels unlock on day 3,
        /// so day-1 levels are carried by the starter parts and part levels alone; levels 1–3 should almost never be lost
        /// (EarlyLevelBalanceTests).
        /// </summary>
        public List<double> EarlyLevelEnemyHpPct = new List<double> { -60, -55, -50, -48, -46, -42, -38, -34, -28, -22, -14, -7 };
        public List<double> EarlyLevelEnemyAtkPct = new List<double> { -40, -40, -35, -35, -32, -30, -27, -24, -18, -12, -8, -4 };

        public double EarlyHpPct(int level) => level >= 1 && level <= EarlyLevelEnemyHpPct.Count ? EarlyLevelEnemyHpPct[level - 1] : 0;
        public double EarlyAtkPct(int level) => level >= 1 && level <= EarlyLevelEnemyAtkPct.Count ? EarlyLevelEnemyAtkPct[level - 1] : 0;

        /// <summary>First Dark Side level (the feature itself unlocks at main level 45 on day 21, ContentDb.Onboarding).</summary>
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
        /// <summary>Main board lap length: 24 + level index + 2 per portal (the portal part capped here), ×1.5 with two dice.</summary>
        public int BoardGrowthPortals = 6;
        /// <summary>Hard roll limit of main and Dark Side runs (the awakening meter normally brings the boss at ~30 rolls).</summary>
        public int MaxRolls = 45;

        /// <summary>Reward scale of portal index i (0-based) up to the endless anchor: (i+1) × RewardGrowth^max(0, i−2).</summary>
        public double RewardGrowth = 1.0;

        // ---- endless campaign (docs/design/economy-model.md, "Endless campaign rewards") ----------------------------------
        /// <summary>
        /// Portal index from which rewards follow the endless formula: portal i ≥ EndlessFromPortal pays
        /// RewardScale(EndlessFromPortal − 1) × (M(i) / M(EndlessFromPortal − 1))^EndlessRewardExponent, where M is the enemy
        /// threat multiplier at the portal's first level. Same anchor for the item-rarity shift.
        /// </summary>
        public int EndlessFromPortal = 50;
        /// <summary>α: per-run currencies grow as threat^α (α &lt; 1, sublinear to threat and upgrade costs).</summary>
        public double EndlessRewardExponent = 0.7;
        /// <summary>Dark Side level from which Data follows threat^α (anchor = this level's legacy Data).</summary>
        public int DarkSideEndlessFrom = 150;
        /// <summary>Run item drops (parts, runes, blueprints, aliens) of portal i ≥ EndlessFromPortal gain
        /// 1 + (i − EndlessFromPortal) / RarityShiftPortals rarity tiers, never above <see cref="MaxDropRarity"/>.</summary>
        public int RarityShiftPortals = 25;
        public int MaxDropRarity = 3; // Legendary: Mythic only from pulls and merges
        /// <summary>A generated portal's boss changes its variant (title, boss tiles, escort, board hazard) every N portals.</summary>
        public int BossVariantEveryPortals = 5;
        public int BossVariants = 6;

        /// <summary>One-time milestone chest on the first clear of every MilestoneEveryLevels-th main level.</summary>
        public int MilestoneEveryLevels = 10;
        /// <summary>Chest Diamonds: MilestoneDiamonds × (1 + n / MilestoneGrowthLevels), rounded to 5.</summary>
        public long MilestoneDiamonds = 50;
        public int MilestoneGrowthLevels = 500;
        /// <summary>Chest tokens of each pull type (Part, Blueprint, Alien): MilestoneTokens + n / MilestoneGrowthLevels.</summary>
        public long MilestoneTokens = 1;
        /// <summary>Every PortalBonusEveryLevels-th level pays the portal-completion bonus instead: the chest × PortalBonusMult.</summary>
        public int PortalBonusEveryLevels = 50;
        public long PortalBonusMult = 4;
        /// <summary>
        /// State size: main / Dark Side progress more than this many levels behind the player's best clear is folded into the
        /// compact archives (RunState.MainArchive / DarkArchive) once the per-level records exceed twice the window.
        /// </summary>
        public int ProgressWindowLevels = 100;

        public double ThreatAt(int n)
        {
            if (n <= LinearUntil) return n - 1;
            double t = LinearUntil - 1;
            int per = Math.Max(1, LevelsPerPortal * SlopeBandPortals);
            int rest = n - LinearUntil;
            for (int band = 0; rest > 0; band++)
            {
                int take = band < LateSlopes.Count - 1 ? Math.Min(rest, per) : rest;
                t += take * LateSlopes[Math.Min(band, LateSlopes.Count - 1)];
                rest -= take;
            }
            return t;
        }

        public double RewardScale(int portalIndex) => (portalIndex + 1) * Math.Pow(RewardGrowth, Math.Max(0, portalIndex - 2));

        /// <summary>Main level number at which Dark Side level i sits.</summary>
        public int DarkSideMainLevel(int i) => (int)Math.Round(DarkSideFromLevel + DarkSideSpacing * (i - 1));

        /// <summary>One-time rewards of main level n's first clear (milestone chest / portal-completion bonus); empty otherwise.</summary>
        public List<Reward> MilestoneRewards(int n)
        {
            var list = new List<Reward>();
            if (n <= 0 || MilestoneEveryLevels <= 0 || n % MilestoneEveryLevels != 0) return list;
            long mult = PortalBonusEveryLevels > 0 && n % PortalBonusEveryLevels == 0 ? PortalBonusMult : 1;
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
    }

    /// <summary>
    /// Laboratory pacing. The k-th research level a player starts (k = levels researched or running, beyond the default
    /// ones) takes <c>BaseHours × Growth^k</c> nominal hours and costs <c>ceil(DataBase × hours^DataExponent)</c> Data,
    /// whatever the node: every research makes the next one longer and dearer. Durations are shortened by the Laboratory building boon
    /// (CitySystem.ResearchSpeedPct) and lengthened ×AnnexDurationMult in the Research Annex slot.
    /// Sizing (docs/design/GDD.md §5, docs/balance/progression-report.md): ~28.7k nominal hours and ~93k Data in total.
    /// </summary>
    [Serializable]
    public sealed class LabTuningDef
    {
        public double BaseHours = 1.5;
        public double Growth = 1.0355;
        /// <summary>Data cost of the k-th research: DataBase × Hours(k)^DataExponent (early research relatively dearer).</summary>
        public double DataBase = 14, DataExponent = 0.75;
        /// <summary>Main slot + at most this many Research Annex slots.</summary>
        public int MaxSlots = 2;
        /// <summary>Research started in the Annex slot (main slot busy) takes this many times longer.</summary>
        public double AnnexDurationMult = 2;

        public double Hours(int k) => BaseHours * Math.Pow(Growth, Math.Max(0, k));
        public long DataCost(int k) => (long)Math.Ceiling(DataBase * Math.Pow(Hours(k), DataExponent) - 1e-9);
    }
}
