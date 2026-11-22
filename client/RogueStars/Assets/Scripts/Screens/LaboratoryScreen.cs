using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>Laboratory: research lines (cost, timer with speed-ups, collect) and draft-pool toggles.</summary>
    public sealed class LaboratoryScreen : UiScreen
    {
        public override string Title => "Laboratory";

        protected override void Build(RectTransform body)
        {
            var ids = new List<string>(Content.LabResearch.Keys);
            ids.Sort(string.CompareOrdinal);
            int running = 0;
            foreach (var e in State.Lab.Entries.Values) if (e.ResearchingLevel > 0) running++;
            Note(body, "Research slots " + running + "/" + Mathf.Max(1, Content.RunTuning.LabSlots) + " · active draft skills " + LaboratorySystem.ActiveSkillIds(State, Content).Count
                       + " · speed +" + Fmt.Pct(CitySystem.ResearchSpeedPct(State, Content)));
            foreach (var id in ids) Line(body, id, running);
        }

        private void Line(RectTransform body, string id, int running)
        {
            var def = Content.LabResearch[id];
            var entry = LaboratorySystem.Entry(State, id);
            int level = entry?.Level ?? 0;
            var cur = level > 0 ? Content.Skill(def.Levels[Mathf.Min(level, def.Levels.Count) - 1].SkillId) : null;
            var nextLevel = level < def.Levels.Count ? def.Levels[level] : null;
            var nextSkill = nextLevel != null ? Content.Skill(nextLevel.SkillId) : null;
            var family = (cur ?? nextSkill)?.Family ?? SkillFamily.None;
            var card = Card(body, Color.Lerp(Theme.FamilyColor(family), Theme.Panel, 0.82f));
            var head = Ui.Row(card, 56, 10);
            Ui.Label(head, def.Name + "  <size=24>Lv " + level + "/" + def.Levels.Count + "</size>", Theme.FontNormal, Theme.FamilyColor(family));
            if (level > 0)
            {
                bool on = entry.Enabled;
                Ui.Button(head, on ? "In pool" : "Off", () => Run(new LabToggle { ResearchId = id, Enabled = !on }), on ? Theme.Good : Theme.Locked, 180, 56, true, 26);
            }
            var shown = cur ?? nextSkill;
            if (shown != null) Note(card, shown.Name + " (" + shown.Rarity + "): " + shown.Description);

            if (entry != null && entry.ResearchingLevel > 0)
            {
                bool ready = Now >= LaboratorySystem.ReadyAtMs(State, id);
                if (!ready && entry.TimerId != null) TimerControls(card, entry.TimerId, "Researching Lv " + entry.ResearchingLevel);
                Ui.Button(card, ready ? "Collect" : "Researching…", () => Run(new LabCollect { ResearchId = id }), Theme.Good, -1, 80, ready);
            }
            else if (nextLevel != null)
            {
                bool afford = State.Wallet.CanAfford(nextLevel.Costs);
                Ui.Button(card, "Research Lv " + (level + 1) + "\n<size=22>" + Fmt.Costs(nextLevel.Costs, Content) + " · " + Fmt.Duration(LaboratorySystem.DurationMs(State, Content, nextLevel.DurationMs)) + "</size>",
                    () => Run(new LabResearch { ResearchId = id }), Theme.Accent, -1, 90, afford && running < Mathf.Max(1, Content.RunTuning.LabSlots), 28);
            }
            else Note(card, "Fully researched.");
        }
    }
}
