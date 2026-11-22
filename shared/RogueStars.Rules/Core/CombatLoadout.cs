using System;
using System.Collections.Generic;

namespace RogueStars.Rules.Core
{
    /// <summary>Weapon style of an arm part; decides the visual and base multipliers of Basic/Combo/Counter/Rage attacks.</summary>
    public enum AttackStyle { Slam, Claw, Blaster, Drill, Whip, Cannon }

    [Serializable]
    public sealed class CombatAlien
    {
        public string AlienTypeId;
        public string Name;
        public StatBlock Stats = new StatBlock();
        public List<string> SkillIds = new List<string>();
    }

    /// <summary>
    /// Everything combat needs about the player side, assembled by loadout contributors
    /// (worm/parts/hero: progression; mount; aliens; city building boons). Also the Arena snapshot format.
    /// </summary>
    [Serializable]
    public sealed class CombatLoadout
    {
        public string PlayerId;
        public string DisplayName;
        public StatBlock WormStats = new StatBlock();
        public List<AttackStyle> ArmStyles = new List<AttackStyle>();
        public string HeroId;
        public string MountId;
        public StatBlock MountStats = new StatBlock();
        public List<CombatAlien> Aliens = new List<CombatAlien>();
        /// <summary>Non-draft skills active for the worm in runs/battles: hero traits, mount weapons, part effects, building boons.</summary>
        public List<string> MetaSkillIds = new List<string>();
        /// <summary>Laboratory-researched skills that are toggled on for the run draft pool.</summary>
        public List<string> DraftPoolSkillIds = new List<string>();
        /// <summary>Extra draft rerolls per run from talents/buildings.</summary>
        public int DraftRerolls;
        /// <summary>Power score for display and matchmaking.</summary>
        public long Power;
    }
}
