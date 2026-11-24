using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    /// <summary>
    /// One line of a hero or mount kit as the screens show it (hero &amp; mount kits, docs/design/heroes-mounts.md): the step it
    /// unlocks at, its slot (hero: Trait / Ability / Number / Clause / Capstone; mount: Passive / Active), its name and its text.
    /// </summary>
    [Serializable]
    public sealed class KitText
    {
        public int Star;
        /// <summary>Mount awakening of the step (A3, A5); 0 = a star step.</summary>
        public int Awaken;
        public string Slot;
        public string Name;
        public string Text;

        public KitText() { }
        public KitText(int star, int awaken, string slot, string name, string text) { Star = star; Awaken = awaken; Slot = slot; Name = name; Text = text; }

        /// <summary>"1★", "3★", "A3".</summary>
        public string StepLabel => Awaken > 0 ? "A" + Awaken : Star + "★";
    }

    public sealed partial class ContentDb
    {
        /// <summary>A hand-written kit skill (hero trait / ability / clause, mount passive / active step): no tiers, never drafted.</summary>
        private SkillDef KitSkill(string id, string name, SkillSource source, string text, params SkillTrigger[] triggers)
        {
            var s = new SkillDef { Id = id, Name = name, Source = source, Draftable = false, Family = SkillFamily.None, Rarity = Rarity.Common, Text = text, Description = text };
            s.Triggers.AddRange(triggers);
            AddSkill(s);
            return s;
        }

        private static SkillTrigger K(TriggerKind when, EffectKind effect, double value = 0, TargetKind target = TargetKind.Enemy, string param = null,
            StatusId status = StatusId.None, int stacks = 1, string @if = null, int everyN = 0, int maxPerBattle = 0, int cooldown = 0, StatMod stat = null,
            bool rider = false) =>
            new SkillTrigger
            {
                When = when, Effect = effect, Value = value, Target = target, Param = param, Status = status, Stacks = stacks, If = @if,
                EveryN = everyN, MaxPerBattle = maxPerBattle, CooldownRounds = cooldown, Stat = stat, Rider = rider, Grow = GrowKind.Fixed,
            };

        /// <summary>A Passive rule ("name[:arg]", Value = its number).</summary>
        private static SkillTrigger KRule(string param, double value = 0, StatusId status = StatusId.None) =>
            new SkillTrigger { When = TriggerKind.Passive, Effect = EffectKind.Rule, Target = TargetKind.Self, Param = param, Value = value, Status = status, Grow = GrowKind.Fixed };
    }
}
