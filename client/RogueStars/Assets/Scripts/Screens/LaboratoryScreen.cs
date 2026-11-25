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
    /// The whole laboratory as one research tree (L1). Rows = dependency depth (a node sits one row below its deepest
    /// prerequisite, across branches); columns = one lane per branch (synergy packages, offense, defense, elements, command),
    /// each lane as wide as its fullest row. Nodes snap to a grid, so the gaps between grid columns and rows are free
    /// channels for the connecting lines. Inside a lane, rows are ordered by the barycentre of their prerequisites and
    /// dependants (a few sweeps) to cut crossings. Coordinates: top-left origin, y down, in UI units.
    /// </summary>
    public sealed class LabTreeLayout
    {
        public const float NodeWidth = 224, NodeHeight = 164, ColGap = 30, RowGap = 104, LaneGap = 48, HeaderHeight = 84, Margin = 28;
        public float Width, Height;
        public readonly Dictionary<string, Rect> Nodes = new Dictionary<string, Rect>();
        /// <summary>Dependency depth per node (0 = no prerequisites).</summary>
        public readonly Dictionary<string, int> Depth = new Dictionary<string, int>();
        /// <summary>Lane per branch: x range in the tree (header + background).</summary>
        public readonly Dictionary<string, Rect> Lanes = new Dictionary<string, Rect>();
        /// <summary>Every prerequisite edge (from prerequisite to node), across branches too.</summary>
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

        private static int DepthOf(ContentDb c, string id, Dictionary<string, int> memo, HashSet<string> visiting)
        {
            if (memo.TryGetValue(id, out int d)) return d;
            var def = c.LabNode(id);
            int depth = 0;
            if (def != null && visiting.Add(id))
            {
                foreach (var r in def.Requires)
                    if (c.LabNode(r.ResearchId) != null) depth = Mathf.Max(depth, DepthOf(c, r.ResearchId, memo, visiting) + 1);
                visiting.Remove(id);
            }
            memo[id] = depth;
            return depth;
        }

        public static LabTreeLayout Build(ContentDb c)
        {
            var layout = new LabTreeLayout();
            var defs = SortedDefs(c);
            var visiting = new HashSet<string>();
            foreach (var d in defs) DepthOf(c, d.Id, layout.Depth, visiting);
            foreach (var d in defs)
                foreach (var r in d.Requires)
                    if (c.LabNode(r.ResearchId) != null) layout.Edges.Add(new KeyValuePair<string, string>(r.ResearchId, d.Id));

            // cells: branch × depth
            var branches = Branches(c);
            int maxDepth = 0;
            var cells = new Dictionary<string, Dictionary<int, List<string>>>();
            foreach (var b in branches) cells[b] = new Dictionary<int, List<string>>();
            foreach (var d in defs)
            {
                int dep = layout.Depth[d.Id];
                maxDepth = Mathf.Max(maxDepth, dep);
                var lane = cells[d.Branch];
                if (!lane.TryGetValue(dep, out var l)) lane[dep] = l = new List<string>();
                l.Add(d.Id);
            }

            // lanes: as wide as their fullest cell
            var laneX = new Dictionary<string, float>();
            var laneCols = new Dictionary<string, int>();
            float x = Margin;
            foreach (var b in branches)
            {
                int cols = 1;
                foreach (var l in cells[b].Values) cols = Mathf.Max(cols, l.Count);
                laneCols[b] = cols;
                laneX[b] = x;
                float w = cols * NodeWidth + (cols - 1) * ColGap;
                layout.Lanes[b] = new Rect(x - LaneGap / 2 + 4, 0, w + LaneGap - 8, 0);
                x += w + LaneGap;
            }
            layout.Width = x - LaneGap + Margin;
            layout.Height = HeaderHeight + (maxDepth + 1) * NodeHeight + maxDepth * RowGap + Margin;
            foreach (var b in branches) { var r = layout.Lanes[b]; r.height = layout.Height; layout.Lanes[b] = r; }

            // place: cells centred on the lane's grid columns, then barycentre sweeps (down by prerequisites, up by dependants)
            var parents = new Dictionary<string, List<string>>();
            var children = new Dictionary<string, List<string>>();
            foreach (var d in defs) { parents[d.Id] = new List<string>(); children[d.Id] = new List<string>(); }
            foreach (var e in layout.Edges) { parents[e.Value].Add(e.Key); children[e.Key].Add(e.Value); }
            void Place()
            {
                foreach (var b in branches)
                    foreach (var kv in cells[b])
                    {
                        int offset = (laneCols[b] - kv.Value.Count) / 2;
                        for (int i = 0; i < kv.Value.Count; i++)
                            layout.Nodes[kv.Value[i]] = new Rect(laneX[b] + (offset + i) * (NodeWidth + ColGap),
                                HeaderHeight + kv.Key * (NodeHeight + RowGap), NodeWidth, NodeHeight);
                    }
            }
            Place();
            for (int sweep = 0; sweep < 4; sweep++)
            {
                var by = sweep % 2 == 0 ? parents : children;
                foreach (var b in branches)
                    foreach (var kv in cells[b])
                    {
                        var key = new Dictionary<string, float>();
                        foreach (var id in kv.Value)
                        {
                            float sum = 0; int n = 0;
                            foreach (var o in by[id]) { sum += layout.Nodes[o].center.x; n++; }
                            key[id] = n > 0 ? sum / n : layout.Nodes[id].center.x;
                        }
                        kv.Value.Sort((p, q) => key[p] != key[q] ? key[p].CompareTo(key[q]) : string.CompareOrdinal(p, q));
                    }
                Place();
            }
            return layout;
        }

        /// <summary>
        /// The connecting line from <paramref name="from"/> to <paramref name="to"/> as points: straight down through the row gap
        /// for neighbouring rows; else down into the prerequisite's row gap, across to the free column channel left of the
        /// node, down that channel to the node's row gap, across and down into the node. <paramref name="lane"/> shifts
        /// parallel lines apart a little.
        /// </summary>
        public List<Vector2> Route(string from, string to, float lane = 0)
        {
            var a = Nodes[from]; var b = Nodes[to];
            var pts = new List<Vector2>();
            var start = new Vector2(a.center.x + lane, a.yMax);
            var end = new Vector2(b.center.x + lane, b.yMin);
            float gapBelowA = a.yMax + RowGap / 2 + lane;
            float gapAboveB = b.yMin - RowGap / 2 + lane;
            pts.Add(start);
            if (b.yMin - a.yMax <= RowGap + 1)
            {
                pts.Add(new Vector2(start.x, gapBelowA));
                pts.Add(new Vector2(end.x, gapBelowA));
            }
            else
            {
                float channel = b.xMin - ColGap / 2 + lane * 0.5f;
                pts.Add(new Vector2(start.x, gapBelowA));
                pts.Add(new Vector2(channel, gapBelowA));
                pts.Add(new Vector2(channel, gapAboveB));
                pts.Add(new Vector2(end.x, gapAboveB));
            }
            pts.Add(end);
            return pts;
        }
    }

    /// <summary>Research state of one laboratory node, as the tree draws it.</summary>
    public enum LabNodeState { Locked, Available, Researching, Researched, Maxed }

    /// <summary>
    /// Laboratory (L1): header (Data, next cost/time, slots, Research Annex), active research slots (timers, speed-ups,
    /// collect) and the whole research tree in a pannable view (both directions). Nodes show their state (locked: dark
    /// with a lock; available: orange outline; researching: pulsing cyan outline + progress; researched: bright with a
    /// check; maxed: gold), their level and their next effect in words. Lines connect every prerequisite (gold = all
    /// needed, thin = any one). Tapping a node opens its detail (effect now/next, requirement rows, Research with a
    /// disabled reason, timer controls, draft-pool toggle).
    /// </summary>
    public sealed class LaboratoryScreen : UiScreen
    {
        public const float ViewportHeight = 1240;
        private static readonly Color StatColor = Theme.Hex(0x3E5C76);
        private Vector2 _pan;
        private bool _panSet;
        private string _pendingDone;

        public override string Title => "Laboratory";

        /// <summary>G1 T35: a collected research pops its node in the tree.</summary>
        public override bool OnFeedback(GameEvent e)
        {
            if (e.Type != "lab_collected" || e.Args.Count < 1) return false;
            PlayResearchDone(e.Args[0]);
            return true;
        }
        public override string[] IntroFeatures => new[] { Features.Laboratory, Features.PackageIce, Features.PackageLightning, Features.PackageCounter, Features.PackageAlien, Features.PackageTime };

        /// <summary>Last built layout (tests).</summary>
        public LabTreeLayout Layout { get; private set; }
        /// <summary>The tree's pan view (tests).</summary>
        public ScrollRect TreeScroll { get; private set; }

        private readonly Dictionary<string, RectTransform> _nodes = new Dictionary<string, RectTransform>();

        protected override void Build(RectTransform body)
        {
            Header(body);
            ActiveSlots(body);
            if (Content.LabResearch.Count == 0) { Note(body, "No research available."); return; }
            Legend(body);
            Tree(body);
            if (_pendingDone != null) { var id = _pendingDone; _pendingDone = null; PlayResearchDone(id); }
        }

        /// <summary>Pans the tree to a branch's lane ("packages", "offense" …; tests and deep links).</summary>
        public void ShowBranch(string branch)
        {
            var layout = Layout ?? LabTreeLayout.Build(Content);
            if (!layout.Lanes.TryGetValue(branch, out var lane)) return;
            _pan = new Vector2(-Mathf.Max(0, lane.x - LabTreeLayout.Margin), 0);
            _panSet = true;
            Rebuild();
        }

        /// <summary>The node's state for the tree.</summary>
        public static LabNodeState NodeState(GameState s, ContentDb c, LabResearchDef def)
        {
            var e = LaboratorySystem.Entry(s, def.Id);
            if (e.ResearchingLevel > 0) return LabNodeState.Researching;
            if (e.Level >= def.Levels.Count) return LabNodeState.Maxed;
            if (e.Level > 0) return LabNodeState.Researched;
            return LaboratorySystem.PrerequisitesMet(s, c, def) ? LabNodeState.Available : LabNodeState.Locked;
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
            Ui.Label(card, st, Theme.FontSmall, active ? Theme.Good : Theme.Warn).name = "AnnexStatus";
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
                    if (b == null) return def.Name + " (" + def.Rarity + " blueprint, needs " + need + "): not owned — second research slot.";
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
                Ui.Button(card, ready ? "Collect" : "Researching…", () => Collect(id), Theme.Good, -1, 76, ready).name = "Collect:" + id;
            }
        }

        private void Collect(string id) => Run(new LabCollect { ResearchId = id }, r => { if (r == null || r.Ok) { _pendingDone = id; Rebuild(); } });

        // ---- tree ----------------------------------------------------------------------------------

        private Color NodeColor(LabResearchDef def)
        {
            if (def == null) return Theme.Panel;
            if (def.Kind == LabNodeKind.Stat) return StatColor;
            if (def.Kind == LabNodeKind.Package) return Theme.FamilyColor(def.Family);
            var sk = def.Levels.Count > 0 ? Content.Skill(def.Levels[0].SkillId) : null;
            return Theme.FamilyColor(sk?.Family ?? SkillFamily.None);
        }

        private static string LaneName(string branch) => branch == "packages" ? "Synergy packages" : Fmt.Humanize(branch);

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

        private void Tree(RectTransform body)
        {
            var layout = LabTreeLayout.Build(Content);
            Layout = layout;
            _nodes.Clear();

            // the pan view: a fixed-height window, the tree scrolls both ways inside it
            var view = Ui.RoundPanel(body, Theme.PanelAlt, Theme.RadiusCard, "LabTree");
            Ui.Size(view, -1, ViewportHeight);
            view.gameObject.AddComponent<RectMask2D>();
            var canvas = Ui.Rect(view.transform, "TreeCanvas");
            canvas.anchorMin = canvas.anchorMax = new Vector2(0, 1);
            canvas.pivot = new Vector2(0, 1);
            canvas.sizeDelta = new Vector2(layout.Width, layout.Height);
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.content = canvas; sr.viewport = view.rectTransform;
            sr.horizontal = true; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.inertia = true; sr.scrollSensitivity = 40;
            TreeScroll = sr;
            canvas.anchoredPosition = _panSet ? _pan : Vector2.zero;
            sr.onValueChanged.AddListener(_ => { _pan = canvas.anchoredPosition; _panSet = true; });

            // lanes: faint stripe + header
            int li = 0;
            foreach (var kv in layout.Lanes)
            {
                var stripe = Ui.Panel(canvas, new Color(1, 1, 1, li++ % 2 == 0 ? 0.035f : 0.015f), "Lane:" + kv.Key);
                stripe.raycastTarget = false;
                Place(stripe.rectTransform, kv.Value);
                var head = Ui.Display(stripe.transform, LaneName(kv.Key) + "  <size=24>" + BranchProgress(kv.Key) + "</size>", 32, Theme.Gold, TextAnchor.MiddleCenter, "LaneHeader");
                var hrt = head.rectTransform;
                hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = Vector2.one; hrt.pivot = new Vector2(0.5f, 1);
                hrt.sizeDelta = new Vector2(-12, LabTreeLayout.HeaderHeight - 16); hrt.anchoredPosition = new Vector2(0, -8);
            }

            // lines first (under the nodes)
            var lines = Ui.Rect(canvas, "Edges");
            lines.anchorMin = lines.anchorMax = new Vector2(0, 1); lines.pivot = new Vector2(0, 1);
            lines.sizeDelta = new Vector2(layout.Width, layout.Height);
            var fanIn = new Dictionary<string, int>();
            foreach (var e in layout.Edges)
            {
                var def = Content.LabNode(e.Value);
                fanIn.TryGetValue(e.Value, out int k);
                fanIn[e.Value] = k + 1;
                int n = def.Requires.Count;
                float lane = n <= 1 ? 0 : (k - (n - 1) / 2f) * 7f;
                bool met = LaboratorySystem.Entry(State, e.Key).Level >= RequiredLevel(def, e.Key);
                var col = def.RequireAll ? Theme.Gold : Theme.Cream;
                col = met ? col : Color.Lerp(col, Theme.Background, 0.62f);
                var edge = Ui.Rect(lines, "Edge:" + e.Key + ">" + e.Value);
                edge.anchorMin = edge.anchorMax = new Vector2(0, 1); edge.pivot = new Vector2(0, 1);
                edge.sizeDelta = Vector2.zero;
                Polyline(edge, layout.Route(e.Key, e.Value, lane), col, def.RequireAll ? 6 : 3);
                var tip = layout.Nodes[e.Value];
                var dot = Ui.Dot(edge, col, def.RequireAll ? 16 : 12, "Tip");
                dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0, 1);
                dot.rectTransform.sizeDelta = Vector2.one * (def.RequireAll ? 16 : 12);
                dot.rectTransform.anchoredPosition = new Vector2(tip.center.x + lane, -tip.yMin);
            }
            foreach (var kv in layout.Nodes) Node(canvas, Content.LabNode(kv.Key), kv.Value);
        }

        private static void Place(RectTransform rt, Rect r)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(r.x, -r.y);
            rt.sizeDelta = new Vector2(r.width, r.height);
        }

        private static void Polyline(RectTransform parent, List<Vector2> pts, Color color, float thickness)
        {
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                var img = Ui.Panel(parent, color, "Segment");
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0.5f, 0.5f);
                // axis-aligned segments, extended by half the thickness so corners join
                float x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x), y0 = Mathf.Min(a.y, b.y), y1 = Mathf.Max(a.y, b.y);
                rt.sizeDelta = new Vector2(x1 - x0 + thickness, y1 - y0 + thickness);
                rt.anchoredPosition = new Vector2((x0 + x1) / 2, -(y0 + y1) / 2);
            }
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

        /// <summary>The short effect on a node's face: the next level's (or, when maxed, the current) effect in words.</summary>
        public string FaceLine(LabResearchDef def)
        {
            if (def.Kind == LabNodeKind.Package) return PackageLine(def);
            var e = LaboratorySystem.Entry(State, def.Id);
            int lv = Mathf.Clamp(e.Level + 1, 1, def.Levels.Count);
            if (def.Levels.Count == 0) return "";
            var level = def.Levels[lv - 1];
            if (def.Kind == LabNodeKind.Skill) return "Draft skill";
            if (level.Stats.Count == 0) return "";
            var first = StatLine(level.Stats[0]);
            return level.Stats.Count <= 2 ? StatsText(level.Stats) : first + " +" + (level.Stats.Count - 1) + " more";
        }

        private static string StatLine(StatMod m) =>
            Fmt.StatName(m.Stat) + " +" + (m.Percent != 0 ? Fmt.Pct(m.Percent) : WormScreen.StatText(m.Stat, m.Flat));

        private static string StatsText(List<StatMod> stats)
        {
            var parts = new List<string>();
            foreach (var m in stats) parts.Add(StatLine(m));
            return string.Join(", ", parts);
        }

        private void Node(RectTransform canvas, LabResearchDef def, Rect r)
        {
            var e = LaboratorySystem.Entry(State, def.Id);
            var state = NodeState(State, Content, def);
            var baseCol = NodeColor(def);
            Color face;
            switch (state)
            {
                case LabNodeState.Locked: face = Color.Lerp(baseCol, Theme.Locked, 0.82f); break;
                case LabNodeState.Available: face = Color.Lerp(baseCol, Theme.Panel, 0.5f); break;
                default: face = Color.Lerp(baseCol, Theme.Panel, 0.12f); break;
            }
            string kind = def.Kind == LabNodeKind.Skill ? "SKILL" + (e.Level > 0 ? (e.Enabled ? " · ON" : " · OFF") : "") : def.Kind == LabNodeKind.Package ? "PACKAGE" : "STAT";
            string lvText = def.Kind == LabNodeKind.Package ? (e.Level > 0 ? "unlocked" : "") : "Lv " + e.Level + "/" + def.Levels.Count;
            string label = "<size=18>" + kind + (lvText.Length > 0 ? " · " + lvText : "") + "</size>\n" + def.Name + "\n<size=19>" + FaceLine(def) + "</size>";
            var btn = Ui.Button(canvas, label, () => NodeModal(def.Id), face, -1, -1, true, 23);
            btn.name = "LabNode:" + def.Id;
            var rt = (RectTransform)btn.transform;
            Object.DestroyImmediate(btn.GetComponent<LayoutElement>());
            Place(rt, r);
            _nodes[def.Id] = rt;
            var text = btn.GetComponentInChildren<Text>();
            if (state == LabNodeState.Locked && text != null) text.color = Theme.TextDim;
            Ui.Rect(rt, "State:" + state).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            // kind accent: a coloured band on top (skills, packages) or a steel edge (stats)
            var band = Ui.Panel(rt, def.Kind == LabNodeKind.Stat ? Theme.Hex(0x9FB3C8) : baseCol, def.Kind == LabNodeKind.Stat ? "StatFrame" : "KindBand");
            band.raycastTarget = false;
            var brt = band.rectTransform;
            brt.anchorMin = new Vector2(0.15f, 1); brt.anchorMax = new Vector2(0.85f, 1); brt.pivot = new Vector2(0.5f, 1);
            brt.sizeDelta = new Vector2(0, def.Kind == LabNodeKind.Package ? 14 : 8); brt.anchoredPosition = new Vector2(0, -4);
            if (state == LabNodeState.Locked) band.color = Color.Lerp(band.color, Theme.Locked, 0.6f);

            // level pips along the bottom (stat / skill nodes)
            if (def.Kind != LabNodeKind.Package && def.Levels.Count > 1)
            {
                var pips = Ui.Rect(rt, "Pips");
                pips.anchorMin = new Vector2(0.12f, 0); pips.anchorMax = new Vector2(0.88f, 0); pips.pivot = new Vector2(0.5f, 0);
                pips.sizeDelta = new Vector2(0, 8); pips.anchoredPosition = new Vector2(0, 16);
                int n = def.Levels.Count;
                for (int i = 0; i < n; i++)
                {
                    var pip = Ui.RoundPanel(pips, i < e.Level ? Theme.Gold : i < e.Level + (e.ResearchingLevel > 0 ? 1 : 0) ? Theme.Premium : new Color(0, 0, 0, 0.35f), 4, "Pip");
                    pip.raycastTarget = false;
                    var prt = pip.rectTransform;
                    prt.anchorMin = new Vector2((float)i / n, 0); prt.anchorMax = new Vector2((float)(i + 1) / n, 1);
                    prt.offsetMin = new Vector2(2, 0); prt.offsetMax = new Vector2(-2, 0);
                }
            }

            switch (state)
            {
                case LabNodeState.Locked:
                    LockIcon(rt);
                    break;
                case LabNodeState.Available:
                    UiShapes.Outline(rt, Theme.Accent, Theme.RadiusButton, 5, "AvailableRing");
                    break;
                case LabNodeState.Researching:
                {
                    var ring = UiShapes.Outline(rt, Theme.Premium, Theme.RadiusButton, 6, "ResearchingRing");
                    UiPulse.On(ring, 0.03f, 1.2f, 0.4f);
                    ResearchProgress(rt, def.Id);
                    break;
                }
                case LabNodeState.Researched:
                    CheckBadge(rt, Theme.Good);
                    break;
                case LabNodeState.Maxed:
                    UiShapes.Outline(rt, Theme.Gold, Theme.RadiusButton, 6, "MaxedRing");
                    CheckBadge(rt, Theme.Gold);
                    break;
            }
        }

        private static RectTransform Corner(RectTransform node, string name, float size)
        {
            var c = Ui.Rect(node, name);
            c.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            c.anchorMin = c.anchorMax = new Vector2(1, 1); c.pivot = new Vector2(0.5f, 0.5f);
            c.sizeDelta = new Vector2(size, size); c.anchoredPosition = new Vector2(-8, -8);
            return c;
        }

        /// <summary>A small padlock (shackle ring over a body) at the node's top-right corner.</summary>
        private static void LockIcon(RectTransform node)
        {
            var c = Corner(node, "LockIcon", 48);
            var bg = Ui.Dot(c, Theme.Background, 48, "LockBack");
            bg.rectTransform.anchoredPosition = Vector2.zero; bg.rectTransform.sizeDelta = new Vector2(48, 48);
            var shackle = Ui.Panel(c, Theme.TextDim, "Shackle");
            shackle.sprite = UiShapes.CircleRing; shackle.raycastTarget = false;
            shackle.rectTransform.sizeDelta = new Vector2(22, 24); shackle.rectTransform.anchoredPosition = new Vector2(0, 7);
            var body = Ui.RoundPanel(c, Theme.TextDim, 4, "LockBody");
            body.raycastTarget = false;
            body.rectTransform.sizeDelta = new Vector2(28, 20); body.rectTransform.anchoredPosition = new Vector2(0, -6);
        }

        private static void CheckBadge(RectTransform node, Color color)
        {
            var c = Corner(node, "CheckBadge", 44);
            var dot = Ui.Dot(c, color, 44, "CheckDot");
            dot.rectTransform.anchoredPosition = Vector2.zero; dot.rectTransform.sizeDelta = new Vector2(44, 44);
            var t = Ui.Label(c, "✓", 30, Theme.OnAccent, TextAnchor.MiddleCenter, "Check");
            t.fontStyle = FontStyle.Bold;
            Ui.Stretch(t.rectTransform);
        }

        /// <summary>A thin progress bar plus the time left on a researching node (live).</summary>
        private void ResearchProgress(RectTransform node, string id)
        {
            var track = Ui.RoundPanel(node, new Color(0, 0, 0, 0.45f), 6, "ResearchBar");
            track.raycastTarget = false;
            var trt = track.rectTransform;
            trt.anchorMin = new Vector2(0.08f, 0); trt.anchorMax = new Vector2(0.92f, 0); trt.pivot = new Vector2(0.5f, 0);
            trt.sizeDelta = new Vector2(0, 12); trt.anchoredPosition = new Vector2(0, 30);
            var fill = Ui.RoundPanel(track.transform, Theme.Premium, 6, "Fill");
            fill.raycastTarget = false;
            var frt = fill.rectTransform;
            frt.anchorMin = Vector2.zero; frt.offsetMin = frt.offsetMax = Vector2.zero;
            var left = Ui.Label(node, "", 18, Theme.Premium, TextAnchor.MiddleCenter, "TimeLeft");
            var lrt = left.rectTransform;
            lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 0); lrt.pivot = new Vector2(0.5f, 0);
            lrt.sizeDelta = new Vector2(0, 24); lrt.anchoredPosition = new Vector2(0, 42);
            System.Func<string> update = () =>
            {
                var e = LaboratorySystem.Entry(State, id);
                var t = e.TimerId != null ? TimerSystem.Get(State, e.TimerId) : null;
                long ready = LaboratorySystem.ReadyAtMs(State, id);
                long rem = System.Math.Max(0, ready - Now);
                float p = t != null && t.DurationMs > 0 ? 1f - (float)rem / t.DurationMs : rem <= 0 ? 1f : 0f;
                if (frt != null) frt.anchorMax = new Vector2(Mathf.Clamp01(p), 1);
                return rem > 0 ? Fmt.Duration(rem) : "Ready!";
            };
            left.text = update();
            Live(left, update);
        }

        /// <summary>Research done (collected): the node pops and flashes a gold ring (inline, no toast). Block 4 / G1 T35 hook.</summary>
        public void PlayResearchDone(string nodeId)
        {
            if (nodeId == null || !_nodes.TryGetValue(nodeId, out var node) || node == null) return;
            var flash = UiShapes.Outline(node, Theme.Gold, Theme.RadiusButton, 10, "DoneFlash");
            var go = node.gameObject;
            UiTweens.Play(go, "lab_done", 0.6f, t =>
            {
                if (node == null) return;
                float k = t < 0.35f ? Mathf.Lerp(1f, 1.18f, t / 0.35f) : Mathf.Lerp(1.18f, 1f, (t - 0.35f) / 0.65f);
                node.localScale = new Vector3(k, k, 1);
                if (flash != null) { var c = Theme.Gold; c.a = 1f - t; flash.color = c; }
            }, () =>
            {
                if (node != null) node.localScale = Vector3.one;
                if (flash != null) Ui.DestroyGo(flash.gameObject);
            });
        }

        private void Legend(RectTransform body)
        {
            var row = Ui.ChipRow(body, 44, 8);
            row.name = "LabLegend";
            Ui.Chip(row, "Researched", Theme.Good, Theme.OnAccent, 20, 40);
            Ui.Chip(row, "Available", null, Theme.Accent, 20, 40);
            Ui.Chip(row, "Researching", null, Theme.Premium, 20, 40);
            Ui.Chip(row, "Locked", Theme.Locked, Theme.TextDim, 20, 40);
            Note(body, "Drag to explore. Gold lines: every link needed · thin lines: any one. Tap a node for details.");
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
                Ui.Label(b, (def.Kind == LabNodeKind.Skill ? "Skill node" : def.Kind == LabNodeKind.Package ? "Synergy package" : "Stat node") + " · " + LaneName(def.Branch) + " · Lv " + e.Level + "/" + def.Levels.Count,
                    Theme.FontSmall, NodeColor(def));
                if (!string.IsNullOrEmpty(def.Description)) Note(b, def.Description);
                if (def.Kind == LabNodeKind.Package)
                {
                    var unlocks = new List<string>();
                    foreach (var sk in Content.Skills.Values)
                        if (sk.Family == def.Family && sk.Source == SkillSource.Run && sk.Draftable) unlocks.Add(sk.Name + " (" + sk.Rarity + ")");
                    unlocks.Sort(string.CompareOrdinal);
                    Ui.Label(b, "Run skills: " + string.Join(", ", unlocks), 22, Theme.TextDim).name = "PackageSkills";
                }
                if (e.Level > 0) Ui.Label(b, "Now: " + LevelText(def, e.Level), 24, Theme.Good).name = "EffectNow";
                if (e.Level < def.Levels.Count) Ui.Label(b, "Next: " + LevelText(def, e.Level + 1), 24, Theme.Text).name = "EffectNext";
                // requirements, each on its own row
                if (def.Requires.Count > 0 && e.Level == 0)
                {
                    Ui.Label(b, def.Requires.Count == 1 ? "Requires:" : def.RequireAll ? "Requires ALL of:" : "Requires ANY one of:", Theme.FontSmall, def.RequireAll ? Theme.Warn : Theme.Text).name = "Prereqs";
                    foreach (var r in def.Requires)
                    {
                        var rd = Content.LabNode(r.ResearchId);
                        bool met = LaboratorySystem.Entry(State, r.ResearchId).Level >= r.Level;
                        Ui.Label(b, (met ? "✓ " : "✗ ") + (rd?.Name ?? r.ResearchId) + " Lv " + r.Level, 22, met ? Theme.Good : Theme.Bad).name = "Requirement";
                    }
                }
                if (def.RequiresFeature != null && !Session.IsUnlocked(def.RequiresFeature))
                    Ui.Label(b, "✗ Needs " + (Content.Feature(def.RequiresFeature)?.Name ?? def.RequiresFeature) + " unlocked", 22, Theme.Bad).name = "Requirement";
                if (e.ResearchingLevel > 0)
                {
                    bool ready = Now >= LaboratorySystem.ReadyAtMs(State, id);
                    if (!ready && e.TimerId != null) TimerControls(b, e.TimerId, "Researching Lv " + e.ResearchingLevel);
                    Ui.Button(b, ready ? "Collect" : "Researching…", () => Run(new LabCollect { ResearchId = id }, r => { Ui.Clear(App.ModalLayer); if (r == null || r.Ok) { _pendingDone = id; Rebuild(); } }), Theme.Good, -1, 80, ready).name = "Collect";
                }
                else if (e.Level < def.Levels.Count)
                {
                    string when = Fmt.Duration(LaboratorySystem.NextDurationMs(State, Content)) + (block == null && LaboratorySystem.NextInAnnex(State, Content) ? " in the Annex" : "");
                    string reason = block != null ? BlockText(block, def) : !afford ? "Needs " + Fmt.Num(cost - State.Wallet.Get(Currencies.Data)) + " more Data" : null;
                    var btn = Ui.ButtonWithReason(b, "Research Lv " + (e.Level + 1) + "\n<size=22>" + Fmt.Num(cost) + " Data · " + when + "</size>",
                        () => Run(new LabResearch { ResearchId = id }, _ => Reopen(id)), Theme.Accent, -1, 90, reason == null, reason, 28);
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

        /// <summary>Player words for the rules' block reasons.</summary>
        private string BlockText(string block, LabResearchDef def)
        {
            switch (block)
            {
                case "prerequisites missing": return "Research the linked nodes first";
                case "laboratory busy": return "All research slots are busy";
                case "already researching": return "Already researching";
                case "research maxed": return "Maxed";
                default: return Fmt.Humanize(block);
            }
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
                return sk == null ? "" : sk.Name + " (" + sk.Rarity + ") — " + Fmt.SkillFull(sk);
            }
            if (def.Kind == LabNodeKind.Package)
            {
                var sig = Content.Skill(lv.SkillId);
                return sig == null ? "" : "Signature " + sig.Name + " — " + Fmt.SkillFull(sig);
            }
            return StatsText(lv.Stats);
        }
    }
}
