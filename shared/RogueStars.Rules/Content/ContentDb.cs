using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>
    /// All static game content (definitions + tuning). Partial: each system defines its own content
    /// collections and fills them in its partial Build* method (Content/ContentDb.&lt;System&gt;.cs).
    /// Content is data, not logic: systems read numbers from here, never hard-code them.
    /// </summary>
    public sealed partial class ContentDb
    {
        private static ContentDb _default;
        public static ContentDb Default => _default ?? (_default = new ContentDb());

        /// <summary>Every skill from every source (run, lab, hero, mount, alien, building, part).</summary>
        public readonly Dictionary<string, SkillDef> Skills = new Dictionary<string, SkillDef>();
        /// <summary>Regenerating currencies (stamina, tickets...).</summary>
        public readonly List<RegenRule> RegenRules = new List<RegenRule>();

        public ContentDb()
        {
            // Order: skills used by other systems are plain ids. Gacha pools are built from the parts, heroes,
            // blueprints and alien types, so BuildGacha runs after those.
            BuildSkills();
            BuildRun();
            BuildWorm();
            BuildParts();
            BuildHeroes();
            BuildMounts();
            BuildEconomy();
            BuildCity();
            BuildAliens();
            BuildGacha();
            BuildBreeding();
            BuildOnboarding();
            BuildLiveOps();
            BuildQuests();
            BuildShop();
            BuildMail();
            BuildArena();
            BuildGuild();
            // Last: reads features, regen rules, arena and live-ops content (what gives each reward a use).
            BuildRewardGate();
        }

        public void AddSkill(SkillDef skill)
        {
            if (skill == null || string.IsNullOrEmpty(skill.Id)) throw new ArgumentException("skill id required");
            if (Skills.ContainsKey(skill.Id)) throw new InvalidOperationException("duplicate skill " + skill.Id);
            Skills[skill.Id] = skill;
        }

        public SkillDef Skill(string id) => id != null && Skills.TryGetValue(id, out var s) ? s : null;

        public RegenRule Regen(string currency)
        {
            foreach (var r in RegenRules) if (r.Currency == currency) return r;
            return null;
        }

        partial void BuildSkills();
        partial void BuildRun();
        partial void BuildWorm();
        partial void BuildParts();
        partial void BuildHeroes();
        partial void BuildMounts();
        partial void BuildGacha();
        partial void BuildEconomy();
        partial void BuildCity();
        partial void BuildAliens();
        partial void BuildBreeding();
        partial void BuildOnboarding();
        partial void BuildLiveOps();
        partial void BuildQuests();
        partial void BuildShop();
        partial void BuildMail();
        partial void BuildArena();
        partial void BuildGuild();
        partial void BuildRewardGate();
    }
}
