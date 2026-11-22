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
        /// <summary>Unlocked city area ids (content order).</summary>
        public List<string> UnlockedAreas = new List<string>();
        /// <summary>
        /// Owned blueprints keyed by blueprint id. A blueprint is also its building: each blueprint can be
        /// built once; level, placement and production storage live here.
        /// </summary>
        public Dictionary<string, OwnedBlueprint> Blueprints = new Dictionary<string, OwnedBlueprint>();
        /// <summary>Currency that gets the boosted production share; null/empty = no priority.</summary>
        public string PriorityCurrency;
    }

    [Serializable]
    public sealed class OwnedBlueprint
    {
        public string Id;
        /// <summary>1..10, raised with blueprint shards.</summary>
        public int Stars = 1;
        /// <summary>Building level; 0 = never constructed (construction pending or not started).</summary>
        public int Level;
        /// <summary>True while the building occupies cells on the grid. Demolish = put back into storage (level kept).</summary>
        public bool Placed;
        public int X;
        public int Y;
        /// <summary>Uncollected production (fractional remainder kept across collects).</summary>
        public double Stored;
        public long LastSettleMs;
        /// <summary>Running construction/upgrade timer id, or null.</summary>
        public string TimerId;
    }

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
        public int Level = 1;
        /// <summary>1..10, raised with alien shards.</summary>
        public int Stars = 1;
        public AlienGenes Genes = new AlienGenes();
        /// <summary>Up to 3 trait ids.</summary>
        public List<string> Traits = new List<string>();
        /// <summary>Blueprint id of the building this alien works in, or null.</summary>
        public string WorkBuildingId;
        /// <summary>0 = pulled; +1 per bred replacement.</summary>
        public int Generation;
    }

    [Serializable]
    public sealed class AlienState
    {
        /// <summary>One alien per alien type, keyed by type id.</summary>
        public Dictionary<string, OwnedAlien> Owned = new Dictionary<string, OwnedAlien>();
        /// <summary>Squad order (alien type ids). Only the first CitySystem.SquadSlots entries fight.</summary>
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
        /// <summary>Owner-defined kind, e.g. "city_build", "city_upgrade", "breeding", "lab_research".</summary>
        public string Kind;
        /// <summary>Owner-defined reference (blueprint id, breeding job id, skill id...).</summary>
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
