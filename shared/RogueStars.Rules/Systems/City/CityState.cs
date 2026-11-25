using System;
using System.Collections.Generic;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        /// <summary>City grid, owned blueprints/buildings, production priority (City workstream).</summary>
        public CityState City = new CityState();
        /// <summary>Owned aliens (one per type) and the combat squad (City workstream).</summary>
        public AlienState Aliens = new AlienState();
        /// <summary>Breeding Pen jobs and born children awaiting a keep/discard choice (City workstream).</summary>
        public BreedingState Breeding = new BreedingState();
        /// <summary>Generic timers (construction, upgrades, breeding, research...).</summary>
        public TimerState Timers = new TimerState();
    }
}

namespace RogueStars.Rules.Systems
{
    [Serializable]
    public sealed class CityState
    {
        /// <summary>City layout version: 0 = a save from before the City redesign (reset once by CitySystem), 1 = current.</summary>
        public int Version;
        /// <summary>Owned blueprints keyed by blueprint id: stars (shared by every copy). Buildings live in <see cref="Buildings"/>.</summary>
        public Dictionary<string, OwnedBlueprint> Blueprints = new Dictionary<string, OwnedBlueprint>();
        /// <summary>Built copies (placed or stored), in build order.</summary>
        public List<CityBuilding> Buildings = new List<CityBuilding>();
        /// <summary>Dug patch ids, in dig order.</summary>
        public List<string> DugPatches = new List<string>();
        /// <summary>Street cells (y × GridWidth + x).</summary>
        public List<int> Streets = new List<int>();
        /// <summary>Patch being dug (its timer: DigTimerId), or null.</summary>
        public string DigPatchId;
        public string DigTimerId;
        /// <summary>Digs that cost nothing and finish at once (granted by the redesign reset: one per old area unlocked).</summary>
        public int FreeDigs;
        /// <summary>Bumped on every layout change (placement-bonus cache key).</summary>
        public int LayoutRev;
        /// <summary>Currency that gets the boosted production share; null/empty = no priority.</summary>
        public string PriorityCurrency;
        /// <summary>Legacy (before the City redesign): unlocked area ids; read once by the reset (free digs).</summary>
        public List<string> UnlockedAreas = new List<string>();

        [NonSerialized] internal object LayoutCache;
    }

    [Serializable]
    public sealed class OwnedBlueprint
    {
        public string Id;
        /// <summary>1..10, raised with blueprint shards; shared by every copy.</summary>
        public int Stars = 1;
        /// <summary>Legacy (before the City redesign): the one building's level; read once by the reset.</summary>
        public int Level;
    }

    /// <summary>One built copy of a blueprint.</summary>
    [Serializable]
    public sealed class CityBuilding
    {
        /// <summary>"&lt;blueprintId&gt;#&lt;n&gt;" (n = 1..copies).</summary>
        public string Uid;
        public string BlueprintId;
        public int X;
        public int Y;
        /// <summary>Footprint swapped (2×1 ↔ 1×2, 3×2 ↔ 2×3).</summary>
        public bool Rotated;
        /// <summary>True while on the land; false = in storage (level kept).</summary>
        public bool Placed;
        /// <summary>1..10; 0 = first construction still running.</summary>
        public int Level;
        /// <summary>Uncollected production (fractional remainder kept across collects).</summary>
        public double Stored;
        /// <summary>Pull buildings: chances piled up (fraction = progress to the next).</summary>
        public double Chances;
        public long LastSettleMs;
        /// <summary>Running construction/upgrade timer id, or null.</summary>
        public string TimerId;
    }

    /// <summary>
    /// Genes (aliens pass: "coming soon"): stored on every alien and still rolled and bred, but read nowhere — they have no effect
    /// yet. HP / ATK / DEF are kept for old saves.
    /// </summary>
    [Serializable]
    public sealed class AlienGenes
    {
        /// <summary>Quality in percent relative to the species base (e.g. 12 = +12%).</summary>
        public double Hp, Atk, Def, Speed, Work;

        public AlienGenes Copy() => new AlienGenes { Hp = Hp, Atk = Atk, Def = Def, Speed = Speed, Work = Work };
    }

    [Serializable]
    public sealed class OwnedAlien
    {
        public string TypeId;
        /// <summary>Removed (aliens pass): alien levels are gone; an old save's level is refunded in Grain once and reset to 1.</summary>
        public int Level = 1;
        /// <summary>1..10, raised with alien shards: the only progression.</summary>
        public int Stars = 1;
        public AlienGenes Genes = new AlienGenes();
        /// <summary>Up to 3 trait ids.</summary>
        public List<string> Traits = new List<string>();
        /// <summary>Uid of the building this alien works in (CityBuilding.Uid), or null. The Launch Pod's aliens are the squad.</summary>
        public string WorkBuildingId;
        /// <summary>0 = pulled; +1 per bred replacement.</summary>
        public int Generation;
    }

    [Serializable]
    public sealed class AlienState
    {
        /// <summary>One alien per alien type, keyed by type id.</summary>
        public Dictionary<string, OwnedAlien> Owned = new Dictionary<string, OwnedAlien>();
        /// <summary>Squad order (alien type ids); members are the aliens working in the Launch Pod (AlienSystem.ActiveSquad).</summary>
        public List<string> Squad = new List<string>();
    }

    [Serializable]
    public sealed class BreedingJob
    {
        public string Id;
        public string ParentA;
        public string ParentB;
        public string TimerId;
        /// <summary>Gene quality bonus captured at start (from buildings).</summary>
        public double QualityBonus;
        public long StartMs;
    }

    [Serializable]
    public sealed class PendingChild
    {
        public string Id;
        public string TypeId;
        public string ParentA;
        public string ParentB;
        public AlienGenes Genes = new AlienGenes();
        public List<string> Traits = new List<string>();
        public bool Mutated;
    }

    [Serializable]
    public sealed class BreedingState
    {
        public List<BreedingJob> Jobs = new List<BreedingJob>();
        public List<PendingChild> Pending = new List<PendingChild>();
        public int NextId;
    }

    [Serializable]
    public sealed class GameTimer
    {
        public string Id;
        /// <summary>Owner-defined kind, e.g. "city_build", "city_upgrade", "city_dig", "breeding", "lab_research".</summary>
        public string Kind;
        /// <summary>Owner-defined reference (building uid, patch id, breeding job id, skill id...).</summary>
        public string RefId;
        public long StartMs;
        public long EndsMs;
        public long DurationMs;
        public int AdsUsed;
        public int SpeedUps;
    }

    [Serializable]
    public sealed class TimerState
    {
        public List<GameTimer> Active = new List<GameTimer>();
        public int NextId;
        /// <summary>UTC day index (ms / 86400000) of the last free speed-up and how many were used that day.</summary>
        public long FreeDay = -1;
        public int FreeUsed;
    }
}
