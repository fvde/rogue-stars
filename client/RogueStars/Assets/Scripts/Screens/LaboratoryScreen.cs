using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Node positions of one laboratory branch: one row per tier, nodes ordered by the average position of their in-branch
    /// prerequisites (fewer crossing edges), rows centred. Coordinates: top-left origin, y down, in UI units.
    /// </summary>
    public sealed class LabTreeLayout
    {
        public const float NodeHeight = 156, RowGap = 84, Gap = 18;
        public float Width, Height, NodeWidth;
        public readonly Dictionary<string, Rect> Nodes = new Dictionary<string, Rect>();
        /// <summary>In-branch prerequisite edges (from prerequisite to node).</summary>
        public readonly List<KeyValuePair<string, string>> Edges = new List<KeyValuePair<string, string>>();

        private static readonly string[] BranchOrder = { "packages", "offense", "defense", "elements", "command" };

        /// <summary>Branches: the synergy packages first, then the GDD order (offense, defense, elements, command), unknown ones after them.</summary>
        public static List<string> Branches(ContentDb c)
        {
            var list = new List<string>();
            foreach (var b in BranchOrder)
                foreach (var d in c.LabResearch.Values) if (d.Branch == b) { list.Add(b); break; }
            foreach (var d in SortedDefs(c)) if (!list.Contains(d.Branch)) list.Add(d.Branch);
            return list;
        }

        private static List<LabResearchDef> SortedDefs(ContentDb c)
        {
            var list = new List<LabResearchDef>(c.LabResearch.Values);
            list.Sort((a, b) => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        public static LabTreeLayout Build(ContentDb c, string branch, float width)
        {
            var layout = new LabTreeLayout { Width = width };
            var rows = new SortedDictionary<int, List<LabResearchDef>>();
            foreach (var d in SortedDefs(c))
            {
                if (d.Branch != branch) continue;
                if (!rows.TryGetValue(d.Tier, out var l)) rows[d.Tier] = l = new List<LabResearchDef>();
                l.Add(d);
            }
            int cols = 1;
            foreach (var r in rows.Values) cols = Mathf.Max(cols, r.Count);
            layout.NodeWidth = (width - Gap * (cols + 1)) / cols;
            float y = Gap;
            foreach (var kv in rows)
            {
                var row = kv.Value;
                // order by the mean x of in-branch prerequisites (already placed), then the content order
                var key = new Dictionary<string, float>();
                for (int i = 0; i < row.Count; i++)
                {
                    float sum = 0; int n = 0;
                    foreach (var req in row[i].Requires)
                        if (layout.Nodes.TryGetValue(req.ResearchId, out var pr)) { sum += pr.center.x; n++; }
                    key[row[i].Id] = n > 0 ? sum / n : float.MaxValue / 2 + i;
                }
                row.Sort((a, b) => key[a.Id] != key[b.Id] ? key[a.Id].CompareTo(key[b.Id]) : string.CompareOrdinal(a.Id, b.Id));
                float total = row.Count * layout.NodeWidth + (row.Count - 1) * Gap;
                float x = (width - total) / 2;
                foreach (var d in row)
                {
                    layout.Nodes[d.Id] = new Rect(x, y, layout.NodeWidth, NodeHeight);
                    x += layout.NodeWidth + Gap;
                }
                y += NodeHeight + RowGap;
            }
            layout.Height = y - RowGap + Gap;
            foreach (var kv in rows)
                foreach (var d in kv.Value)
                    foreach (var req in d.Requires)
                        if (layout.Nodes.ContainsKey(req.ResearchId)) layout.Edges.Add(new KeyValuePair<string, string>(req.ResearchId, d.Id));
            return layout;
        }
    }

    /// <summary>
    /// Laboratory tree: one tab per branch with stat nodes (square, steel) and skill nodes (family colour, SKILL tag),
    /// prerequisite edges (solid gold = all needed, thin = any one), node detail (effect now/next, any/all prerequisites,
    /// research with the next Data cost and duration, draft-pool toggle), active research slots (main + Research Annex
    /// when staffed) with speed-ups and collect.
    /// </summary>
    public sealed class LaboratoryScreen : UiScreen
    {
        public const float TreeWidth = 1000;
        private static readonly Color StatColor = Theme.Hex(0x3E5C76);
        private int _branch;

        public override string Title => "Laboratory";
        public override string[] IntroFeatures => new[] { Features.Laboratory, Features.PackageIce, Features.PackageLightning, Features.PackageCounter, Features.PackageAlien, Features.PackageTime };

        /// <summary>Last built layout (tests).</summary>
        public LabTreeLayout Layout { get; private set; }

        protected override void Build(RectTransform body)
        {
            Header(body);
            ActiveSlots(body);
            var branches = LabTreeLayout.Branches(Content);
            if (branches.Count == 0) { Note(body, "No research available."); return; }
            _branch = Mathf.Clamp(_branch, 0, branches.Count - 1);
            var labels = new List<string>();
            foreach (var b in branches) labels.Add(Fmt.Humanize(b) + " " + BranchProgress(b));
            Ui.TabBar(body, labels, _branch, i => { _branch = i; Rebuild(); }, 80);
            Tree(body, branches[_branch]);
            Legend(body);
        }

        /// <summary>Opens a branch tab by id ("packages", "offense" …; tests and deep links).</summary>
        public void ShowBranch(string branch)
        {
            int i = LabTreeLayout.Branches(Content).IndexOf(branch);
            if (i < 0) return;
            _branch = i;
            Rebuild();
        }

        private string BranchProgress(string branch)
        {
            int have = 0, max = 0;
            foreach (var d in Content.LabResearch.Values)
            {
                if (d.Branch != branch) continue;
                max += d.Levels.Count;
                have += LaboratorySystem.Entry(State, d.Id).Level;
            }
            return have + "/" + max;
        }

        // ---- header and slots ----------------------------------------------------------------------

        private void Header(RectTransform body)
        {
            var card = Card(body, Theme.Header, 4);
            int slots = LaboratorySystem.Slots(State, Content), running = LaboratorySystem.Running(State);
            KeyValue(card, "Data", Fmt.Num(State.Wallet.Get(Currencies.Data)), Theme.CurrencyColor(Currencies.Data));
            long cost = LaboratorySystem.NextDataCost(State, Content);
            bool annex = LaboratorySystem.NextInAnnex(State, Content);
            string where = running >= slots ? " · all slots busy" : annex ? " (Annex ×" + Fmt.OneDecimal(Content.LabTuning.AnnexDurationMult) + ")" : "";
            KeyValue(card, "Next research", Fmt.Num(cost) + " Data · " + Fmt.Duration(LaboratorySystem.NextDurationMs(State, Content)) + where,
                State.Wallet.CanAfford(Currencies.Data, cost) ? Theme.Text : Theme.Bad).name = "NextResearch";
            KeyValue(card, "Slots", running + "/" + slots + " busy · speed +" + Fmt.Pct(CitySystem.ResearchSpeedPct(State, Content)) + " · draft skills " + LaboratorySystem.ActiveSkillIds(State, Content).Count);
            var st = AnnexStatus(State, Content, out bool active);
            Ui.Label(card, st, 22, active ? Theme.Good : Theme.Warn).name = "AnnexStatus";
            Note(card, "Every research makes the next one longer and dearer, whatever the node.");
        }

        /// <summary>Research Annex requirement and staffing (second research slot), e.g. "Research Annex: staffed 1/2 (aliens 3★+)".</summary>
        public static string AnnexStatus(GameState s, ContentDb c, out bool active)
        {
            active = false;
            foreach (var def in c.Blueprints.Values)
                foreach (var boon in def.Boons)
                {
                    if (boon.Kind != BoonKind.ResearchSlots) continue;
                    string need = boon.RequiredWorkers > 0 ? boon.RequiredWorkers + " alien workers of " + boon.RequiredWorkerStars + "★+" : "no workers";
                    var b = CitySystem.Owned(s, def.Id);
                    if (b == null) return def.Name + " (" + def.Rarity + " blueprint, " + need + "): not owned — second research slot.";
                    if (!CitySystem.IsActive(b) || b.Level < boon.MinLevel) return def.Name + ": build it (needs " + need + ") for a second research slot.";
                    int staffed = 0, workers = 0;
                    foreach (var a in CitySystem.Workers(s, def.Id)) { workers++; if (a.Stars >= boon.RequiredWorkerStars) staffed++; }
                    if (staffed >= boon.RequiredWorkers) { active = true; return def.Name + ": staffed " + staffed + "/" + boon.RequiredWorkers + " — second slot active (research there takes ×" + Fmt.OneDecimal(c.LabTuning.AnnexDurationMult) + ")."; }
                    return def.Name + ": staffed " + staffed + "/" + boon.RequiredWorkers + " (needs " + need + (workers > staffed ? "; " + (workers - staffed) + " worker(s) below " + boon.RequiredWorkerStars + "★" : "") + ") — slot inactive.";
                }
            return "No second research slot in this content.";
        }

        private void ActiveSlots(RectTransform body)
        {
            var ids = new List<string>(State.Lab.Entries.Keys);
            ids.Sort(string.CompareOrdinal);
            foreach (var id in ids)
            {
                var e = State.Lab.Entries[id];
                if (e.ResearchingLevel <= 0) continue;
                var def = Content.LabNode(id);
                var card = Card(body, Color.Lerp(NodeColor(def), Theme.Panel, 0.78f), 6);
                card.name = "ActiveResearch:" + id;
                Ui.Label(card, (def?.Name ?? id) + " → Lv " + e.ResearchingLevel + "  <size=22>" + (e.InAnnex ? "Annex slot" : "Main slot") + "</size>", Theme.FontNormal);
                bool ready = Now >= LaboratorySystem.ReadyAtMs(State, id);
                if (!ready && e.TimerId != null) TimerControls(card, e.TimerId, "Researching");
                Ui.Button(card, ready ? "Collect" : "Researching…", () => Run(new LabCollect { ResearchId = id }), Theme.Good, -1, 76, ready).name = "Collect:" + id;
            }
        }

        // ---- tree ----------------------------------------------------------------------------------

        private Color NodeColor(LabResearchDef def)
        {
            if (def == null) return Theme.Panel;
            if (def.Kind == LabNodeKind.Stat) return StatColor;
            if (def.Kind == LabNodeKind.Package) return Theme.FamilyColor(def.Family);
            var sk = def.Levels.Count > 0 ? Content.Skill(def.Levels[0].SkillId) : null;
            return Theme.FamilyColor(sk?.Family ?? SkillFamily.None);
        }

        private void Tree(RectTransform body, string branch)
        {
            var layout = LabTreeLayout.Build(Content, branch, TreeWidth);
            Layout = layout;
            var holder = Ui.Rect(body, "LabTree");
            Ui.Size(holder, -1, layout.Height);
            var canvas = Ui.Rect(holder, "TreeCanvas");
            canvas.anchorMin = canvas.anchorMax = new Vector2(0.5f, 1);
            canvas.pivot = new Vector2(0.5f, 1);
            canvas.sizeDelta = new Vector2(layout.Width, layout.Height);
            canvas.anchoredPosition = Vector2.zero;

            foreach (var e in layout.Edges)
            {
                var def = Content.LabNode(e.Value);
                bool met = LaboratorySystem.Entry(State, e.Key).Level >= RequiredLevel(def, e.Key);
                var a = layout.Nodes[e.Key]; var b = layout.Nodes[e.Value];
                var col = def.RequireAll ? Theme.Warn : Theme.TextDim;
                Line(canvas, new Vector2(a.center.x, a.yMax), new Vector2(b.center.x, b.yMin), met ? col : Color.Lerp(col, Theme.Background, 0.6f), def.RequireAll ? 6 : 3);
            }
            foreach (var kv in layout.Nodes) Node(canvas, Content.LabNode(kv.Key), kv.Value);
        }

        /// <summary>"Chill + 7 skills": the package's signature and how many run skills it brings.</summary>
        public string PackageLine(LabResearchDef def)
        {
            var sig = def.Levels.Count > 0 ? Content.Skill(def.Levels[0].SkillId) : null;
            int n = 0;
            foreach (var sk in Content.Skills.Values) if (sk.Family == def.Family && sk.Source == SkillSource.Run && sk.Draftable) n++;
            return (sig?.Name ?? "?") + " + " + n + " skills";
        }

        private static int RequiredLevel(LabResearchDef def, string prereq)
        {
            foreach (var r in def.Requires) if (r.ResearchId == prereq) return r.Level;
            return 1;
        }

        private static void Line(RectTransform parent, Vector2 from, Vector2 to, Color color, float thickness)
        {
            var img = Ui.Panel(parent, color, "Edge");
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            var a = new Vector2(from.x, -from.y); var b = new Vector2(to.x, -to.y);
            var d = b - a;
            rt.anchoredPosition = a;
            rt.sizeDelta = new Vector2(d.magnitude, thickness);
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        private void Node(RectTransform canvas, LabResearchDef def, Rect r)
        {
            var e = LaboratorySystem.Entry(State, def.Id);
            bool researching = e.ResearchingLevel > 0;
            bool maxed = e.Level >= def.Levels.Count;
            bool prereq = e.Level > 0 || LaboratorySystem.PrerequisitesMet(State, Content, def);
            var baseCol = NodeColor(def);
            Color col = !prereq ? Color.Lerp(baseCol, Theme.Locked, 0.75f) : e.Level == 0 ? Color.Lerp(baseCol, Theme.Panel, 0.55f) : Color.Lerp(baseCol, Theme.Panel, 0.2f);
            string state = researching ? "researching" : maxed ? "max" : !prereq ? "locked" : e.Level == 0 ? "available" : "";
            string kind = def.Kind == LabNodeKind.Skill ? "SKILL" + (e.Level > 0 ? (e.Enabled ? " · on" : " · off") : "") : def.Kind == LabNodeKind.Package ? "SYNERGY PACKAGE" : "STAT";
            string req = def.Requires.Count > 1 ? (def.RequireAll ? " · ALL " : " · ANY ") + def.Requires.Count : "";
            string label = def.Kind == LabNodeKind.Package
                ? "<size=22>" + kind + "</size>\n" + def.Name + "\n<size=28>" + PackageLine(def) + (e.Level > 0 ? " · unlocked" : state.Length > 0 ? " · " + state : "") + "</size>"
                : "<size=18>" + kind + req + "</size>\n" + def.Name + "\n<size=20>Lv " + e.Level + "/" + def.Levels.Count + (state.Length > 0 ? " · " + state : "") + "</size>";
            var btn = Ui.Button(canvas, label, () => NodeModal(def.Id), col, -1, -1, true, def.Kind == LabNodeKind.Package ? 32 : 22);
            btn.name = "LabNode:" + def.Id;
            var rt = (RectTransform)btn.transform;
            Object.DestroyImmediate(btn.GetComponent<LayoutElement>());
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(r.x, -r.y);
            rt.sizeDelta = new Vector2(r.width, r.height);
            if (def.Kind == LabNodeKind.Package)
            {
                // package nodes: a thick synergy-coloured frame and the signature's icon (or its initial)
                UiShapes.Outline(rt, e.Level > 0 ? Theme.Gold : baseCol, 16, e.Level > 0 ? 6 : 4);
                var band = Ui.Panel(rt, baseCol, "PackageBand");
                band.raycastTarget = false;
                var brt = band.rectTransform;
                brt.anchorMin = new Vector2(0, 1); brt.anchorMax = Vector2.one; brt.pivot = new Vector2(0.5f, 1);
                brt.sizeDelta = new Vector2(0, 18); brt.anchoredPosition = Vector2.zero;
            }
            else if (def.Kind == LabNodeKind.Skill)
            {
                // skill nodes: family-coloured top band
                var band = Ui.Panel(rt, baseCol, "SkillBand");
                band.raycastTarget = false;
                var brt = band.rectTransform;
                brt.anchorMin = new Vector2(0, 1); brt.anchorMax = Vector2.one; brt.pivot = new Vector2(0.5f, 1);
                brt.sizeDelta = new Vector2(0, 10); brt.anchoredPosition = Vector2.zero;
            }
            else
            {
                // stat nodes: steel frame
                var frame = Ui.Panel(rt, Theme.Hex(0x9FB3C8), "StatFrame");
                frame.raycastTarget = false;
                var frt = frame.rectTransform;
                frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(0, 1); frt.pivot = new Vector2(0, 0.5f);
                frt.sizeDelta = new Vector2(8, 0); frt.anchoredPosition = Vector2.zero;
            }
        }

        private void Legend(RectTransform body)
        {
            Note(body, "Synergy packages unlock a whole synergy for every run: its signature (one of the three offered at run start) and its skills. "
                       + "Skill nodes (coloured band) add skills to the run draft pool and can be toggled; stat nodes (steel edge) give permanent bonuses. "
                       + "Gold lines: all prerequisites needed; thin lines: any one of them. Prerequisites in other branches are listed in the node.");
        }

        // ---- node detail ---------------------------------------------------------------------------

        public void NodeModal(string id)
        {
            var def = Content.LabNode(id);
            if (def == null) return;
            var e = LaboratorySystem.Entry(State, id);
            string block = LaboratorySystem.BlockReason(State, Content, def);
            long cost = LaboratorySystem.NextDataCost(State, Content);
            bool afford = State.Wallet.CanAfford(Currencies.Data, cost);
            Modal(def.Name, b =>
            {
                Ui.Label(b, (def.Kind == LabNodeKind.Skill ? "Skill node" : def.Kind == LabNodeKind.Package ? "Synergy package" : "Stat node") + " · " + Fmt.Humanize(def.Branch) + " row " + def.Tier + " · Lv " + e.Level + "/" + def.Levels.Count,
                    Theme.FontSmall, NodeColor(def));
                if (!string.IsNullOrEmpty(def.Description)) Note(b, def.Description);
                if (def.Kind == LabNodeKind.Package)
                {
                    var unlocks = new List<string>();
                    foreach (var sk in Content.Skills.Values)
                        if (sk.Family == def.Family && sk.Source == SkillSource.Run && sk.Draftable) unlocks.Add(sk.Name + " (" + sk.Rarity + ")");
                    unlocks.Sort(string.CompareOrdinal);
                    Ui.Label(b, "Run skills: " + string.Join(", ", unlocks), 22, Theme.TextDim).name = "PackageSkills";
                    if (def.RequiresFeature != null && !Session.IsUnlocked(def.RequiresFeature))
                        Ui.Label(b, "Needs " + (Content.Feature(def.RequiresFeature)?.Name ?? def.RequiresFeature) + " unlocked.", 22, Theme.Bad);
                }
                if (e.Level > 0) Ui.Label(b, "Now: " + LevelText(def, e.Level), 24, Theme.Good);
                if (e.Level < def.Levels.Count) Ui.Label(b, "Next: " + LevelText(def, e.Level + 1), 24, Theme.Text);
                if (def.Requires.Count > 0)
                {
                    Ui.Label(b, def.Requires.Count == 1 ? "Requires:" : def.RequireAll ? "Requires ALL of:" : "Requires ANY one of:", Theme.FontSmall, def.RequireAll ? Theme.Warn : Theme.Text).name = "Prereqs";
                    foreach (var r in def.Requires)
                    {
                        var rd = Content.LabNode(r.ResearchId);
                        bool met = LaboratorySystem.Entry(State, r.ResearchId).Level >= r.Level;
                        string other = rd != null && rd.Branch != def.Branch ? " (" + Fmt.Humanize(rd.Branch) + ")" : "";
                        Ui.Label(b, (met ? "✓ " : "✗ ") + (rd?.Name ?? r.ResearchId) + " Lv " + r.Level + other, 22, met ? Theme.Good : Theme.Bad);
                    }
                }
                if (e.ResearchingLevel > 0)
                {
                    bool ready = Now >= LaboratorySystem.ReadyAtMs(State, id);
                    if (!ready && e.TimerId != null) TimerControls(b, e.TimerId, "Researching Lv " + e.ResearchingLevel);
                    Ui.Button(b, ready ? "Collect" : "Researching…", () => Run(new LabCollect { ResearchId = id }, _ => Reopen(id)), Theme.Good, -1, 80, ready);
                }
                else if (e.Level < def.Levels.Count)
                {
                    string when = Fmt.Duration(LaboratorySystem.NextDurationMs(State, Content)) + (block == null && LaboratorySystem.NextInAnnex(State, Content) ? " in the Annex" : "");
                    var btn = Ui.Button(b, "Research Lv " + (e.Level + 1) + "\n<size=22>" + Fmt.Num(cost) + " Data · " + when + (block != null ? " · " + block : !afford ? " · not enough Data" : "") + "</size>",
                        () => Run(new LabResearch { ResearchId = id }, _ => Reopen(id)), Theme.Accent, -1, 90, block == null && afford, 28);
                    btn.name = "Research";
                }
                if (def.Kind == LabNodeKind.Skill && e.Level > 0)
                {
                    bool on = e.Enabled;
                    Ui.Button(b, on ? "In draft pool (tap to remove)" : "Not in draft pool (tap to add)", () => Run(new LabToggle { ResearchId = id, Enabled = !on }, _ => Reopen(id)),
                        on ? Theme.Good : Theme.Locked, -1, 76, true, 26).name = "Toggle";
                }
            }, new ModalButton("Close", null));
        }

        private void Reopen(string id)
        {
            Ui.Clear(App.ModalLayer);
            NodeModal(id);
        }

        private string LevelText(LabResearchDef def, int level)
        {
            var lv = def.Levels[Mathf.Clamp(level, 1, def.Levels.Count) - 1];
            if (def.Kind == LabNodeKind.Skill)
            {
                var sk = Content.Skill(lv.SkillId);
                return sk == null ? lv.SkillId : sk.Name + " (" + sk.Rarity + ") — " + Fmt.SkillFull(sk);
            }
            if (def.Kind == LabNodeKind.Package)
            {
                var sig = Content.Skill(lv.SkillId);
                return sig == null ? lv.SkillId : "Signature " + sig.Name + " — " + Fmt.SkillFull(sig);
            }
            var parts = new List<string>();
            foreach (var m in lv.Stats) parts.Add(m.Stat + " " + (m.Percent != 0 ? "+" + Fmt.Pct(m.Percent) : "+" + Fmt.OneDecimal(m.Flat)));
            return string.Join(", ", parts);
        }
    }
}
