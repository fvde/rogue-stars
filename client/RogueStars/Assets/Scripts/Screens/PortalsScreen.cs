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
    /// Portal level map: Main / Overcharged / Burrow / Dark Side as a zig-zag path of level nodes running upwards
    /// (cleared = stars, next = big glowing node, later = dimmed, the portal's last level a boss node, milestone chest /
    /// portal bonus chips), a header with the portal's name and progress plus stamina, and a fixed bottom level card
    /// (star goals, START RUN with the entry cost, details). Tapping a node selects it for the card. The main campaign and
    /// the Dark Side are endless: both are shown as a bounded window around the frontier (RunSystem.NextMainLevel, the
    /// Dark Side max counter) built with ContentDb.MainPortal / DarkSideLevel; nothing iterates the whole campaign.
    /// </summary>
    public sealed class PortalsScreen : UiScreen
    {
        private static readonly string[] Tabs = { "Main", "Overcharged", "Burrow", "Dark Side" };
        private int _tab;
        /// <summary>Dark Side page (-1 = the page with the next level to clear).</summary>
        private int _page = -1;
        /// <summary>First main portal index shown (-1 = the window around the frontier).</summary>
        private int _mainStart = -1;
        /// <summary>Level shown in the bottom card (null = the next level to play on this tab).</summary>
        private string _selected;
        /// <summary>Scroll the map to the focused node on the next build (opening, tab or page change).</summary>
        private bool _focus = true;
        private Text _stamina;
        public const int DarkSidePageSize = 25;
        /// <summary>Dark Side pages browsable past the one holding the next level.</summary>
        public const int DarkSidePagesAhead = 1;
        /// <summary>Main portals per page: cleared ones before the frontier portal, the frontier, upcoming ones after it.</summary>
        public const int MainPageSize = 5;
        public const int MainPortalsBehind = 2;
        public const int MainPortalsAhead = 2;

        /// <summary>Map geometry: vertical distance between two nodes, boss row, portal banner, node sizes.</summary>
        public const float NodeStep = 200, BossStep = 250, BannerH = 110, NodeSize = 150, NextSize = 196, LockedSize = 136;
        public static readonly Vector2 BossSize = new Vector2(300, 170);
        /// <summary>Bottom level card rows.</summary>
        public const float CardTitleH = 60, GoalH = 46, StartH = 124;

        /// <summary>Visual state of a level node on the map.</summary>
        public enum MapNode { Locked, Open, Next, Cleared }

        /// <summary>Node state: the next level to clear glows, cleared ones show stars, unlocked others are open, the rest locked.</summary>
        public static MapNode NodeStateOf(bool unlocked, bool cleared, bool next)
        {
            if (next && unlocked) return MapNode.Next;
            if (cleared) return MapNode.Cleared;
            return unlocked ? MapNode.Open : MapNode.Locked;
        }

        /// <summary>Horizontal position (0..1 of the map width) of the n-th node along the path: a smooth zig-zag.</summary>
        public static float MapX(int pathIndex) => ZigZag[((pathIndex % ZigZag.Length) + ZigZag.Length) % ZigZag.Length];

        private static readonly float[] ZigZag = { 0.24f, 0.76f, 0.5f, 0.24f, 0.64f, 0.3f, 0.74f, 0.42f };

        /// <summary>
        /// Daily Biomass surge: the first won portal runs of the day pay Biomass ×N. Null (nothing shown) until Biomass has a
        /// use (RunSystem.BiomassSurgeActive): no surge counter or text before the City / worm levels.
        /// </summary>
        public static Text SurgeLabel(Transform parent, GameState s, ContentDb c, long now)
        {
            if (!RunSystem.BiomassSurgeActive(s, c, now)) return null;
            int left = RunSystem.BiomassBonusRunsLeft(s, c, now);
            var t = c.RunTuning;
            var label = Ui.Label(parent, "Biomass surge: " + left + "/" + t.DailyBiomassBonusRuns + " left today — won Main/Overcharged runs pay Biomass ×" + Fmt.OneDecimal(t.DailyBiomassBonusMult),
                Theme.FontSmall, left > 0 ? Theme.CurrencyColor(Currencies.Biomass) : Theme.TextDim);
            label.name = "BiomassSurge";
            return label;
        }

        // ---- endless windows (pure, O(1); tested) -----------------------------------------------------------

        /// <summary>0-based index of the main portal holding the next level to clear.</summary>
        public static int FrontierPortalIndex(GameState s, ContentDb c)
        {
            long next = s.Counter(Requirements.MainLevelCounter) + 1;
            return (int)System.Math.Min(ContentDb.MaxGeneratedIndex - 1, (next - 1) / System.Math.Max(1, c.Campaign.LevelsPerPortal));
        }

        /// <summary>
        /// The main portals to show: <paramref name="requestedStart"/> (-1 = around the frontier) clamped so the window
        /// never starts before portal 0 and never goes past <see cref="MainPortalsAhead"/> portals beyond the frontier.
        /// Returns the first index and the number of portals (≤ <see cref="MainPageSize"/>).
        /// </summary>
        public static (int start, int count) MainWindow(int frontier, int requestedStart)
        {
            int last = frontier + MainPortalsAhead;
            int maxStart = System.Math.Max(0, last - MainPageSize + 1);
            int start = requestedStart < 0 ? System.Math.Max(0, frontier - MainPortalsBehind) : requestedStart;
            start = System.Math.Max(0, System.Math.Min(maxStart, start));
            return (start, System.Math.Min(MainPageSize, last - start + 1));
        }

        /// <summary>Next Dark Side level to clear (1-based).</summary>
        public static int DarkSideFrontier(GameState s) => (int)System.Math.Min(ContentDb.MaxGeneratedIndex - 1, s.Counter(RunSystem.DarkSideMaxCounter) + 1);

        /// <summary>Dark Side page to show (-1 = the frontier's page), clamped to [0, frontier page + DarkSidePagesAhead].</summary>
        public static int DarkSidePage(int frontier, int requested)
        {
            int frontierPage = (frontier - 1) / DarkSidePageSize;
            return requested < 0 ? frontierPage : Mathf.Clamp(requested, 0, frontierPage + DarkSidePagesAhead);
        }

        /// <summary>"BONUS" on every portal-bonus level (every 50th), "CHEST" on every other milestone level (every 10th), else null.</summary>
        public static string MilestoneMarker(ContentDb c, int levelNumber)
        {
            var k = c.Campaign;
            if (levelNumber <= 0) return null;
            if (k.PortalBonusEveryLevels > 0 && levelNumber % k.PortalBonusEveryLevels == 0) return "BONUS";
            if (k.MilestoneEveryLevels > 0 && levelNumber % k.MilestoneEveryLevels == 0) return "CHEST";
            return null;
        }

        /// <summary>Level numbers in full (no K/M abbreviation: "level 12345").</summary>
        public static string LevelNum(long n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private RunMode Mode => _tab == 1 ? RunMode.Overcharged : _tab == 2 ? RunMode.Burrow : _tab == 3 ? RunMode.DarkSide : RunMode.Normal;

        /// <summary>The level of the bottom card (selected node, else the next one to play on this tab).</summary>
        public LevelDef SelectedLevel
        {
            get
            {
                if (State == null) return null;
                if (_selected != null) { var l = Content.RunLevel(_selected); if (l != null) return l; }
                return DefaultLevel();
            }
        }

        private LevelDef DefaultLevel()
        {
            switch (Mode)
            {
                case RunMode.DarkSide: return Content.DarkSideLevel(DarkSideFrontier(State));
                case RunMode.Burrow:
                    foreach (var p in Content.Portals.Values)
                        if (p.Kind == PortalKind.Burrow && p.LevelIds.Count > 0) return Content.RunLevel(p.LevelIds[0]);
                    return null;
                default:
                    var next = RunSystem.NextMainLevel(State, Content);
                    if (Mode == RunMode.Overcharged)
                    {
                        // first unlocked, not yet overcharged-cleared level of the frontier window
                        int frontier = FrontierPortalIndex(State, Content);
                        var (start, count) = MainWindow(frontier, -1);
                        for (int i = start; i < start + count; i++)
                        {
                            var portal = Content.MainPortal(i);
                            if (portal == null) continue;
                            foreach (var id in portal.LevelIds)
                                if (RunSystem.IsLevelUnlocked(State, Content, id, RunMode.Overcharged) && !RunSystem.Progress(State, Content, id).OverchargedCleared)
                                    return Content.RunLevel(id);
                        }
                    }
                    return next;
            }
        }

        /// <summary>Select a level for the bottom card (map node tap).</summary>
        public void Select(string levelId)
        {
            _selected = levelId;
            Rebuild();
        }

        /// <summary>"?": the run content and the modes introduced level by level (GDD §16).</summary>
        public override string[] IntroFeatures => new[]
        {
            Features.RunShop, Features.RunMiniboss, Features.RunTwoDice, Features.AutoRoll, Features.RunCampfire, Features.RunSignatures,
            Features.RunMinigames, Features.Overcharged, Features.Burrow, Features.DarkSide,
        };

        public override string Title
        {
            get
            {
                if (State == null) return "Portals";
                if (Mode == RunMode.DarkSide) return Content.Portal(ContentDb.DarkSidePortalId)?.Name ?? "Dark Side";
                var lv = SelectedLevel;
                var portal = lv != null ? Content.Portal(lv.PortalId) : null;
                return portal?.Name ?? "Portals";
            }
        }

        public override string Subtitle
        {
            get
            {
                if (State == null) return null;
                if (Mode == RunMode.DarkSide)
                {
                    int next = DarkSideFrontier(State);
                    return "Endless · cleared " + LevelNum(next - 1) + " · next level " + LevelNum(next);
                }
                var lv = SelectedLevel;
                var portal = lv != null ? Content.Portal(lv.PortalId) : null;
                if (portal == null) return null;
                if (portal.Kind == PortalKind.Burrow) return "Dive floor after floor · cash out any time";
                int cleared = 0, stars = 0;
                foreach (var id in portal.LevelIds)
                {
                    var p = RunSystem.Progress(State, Content, id);
                    if (Mode == RunMode.Overcharged ? p.OverchargedCleared : p.Cleared) cleared++;
                    foreach (var f in p.Feats) if (f) stars++;
                }
                int index = portal.Kind == PortalKind.Main ? (lv.Number - 1) / System.Math.Max(1, Content.Campaign.LevelsPerPortal) + 1 : 0;
                return (index > 0 ? "Portal " + LevelNum(index) + " · " : "") + cleared + " of " + portal.LevelIds.Count + " cleared · "
                       + stars + " of " + 3 * portal.LevelIds.Count + " stars" + (Mode == RunMode.Overcharged ? " · Overcharged" : "");
            }
        }

        public override float FooterHeight => FooterPadTop + FooterPadBottom + CardTitleH + FooterSpacing + 3 * GoalH + 8 + FooterSpacing + StartH;

        protected override void BuildHeaderExtras(RectTransform header)
        {
            var chip = Ui.Chip(header, "", Theme.Panel, Theme.Text, 28, 64, null, "StaminaChip");
            _stamina = chip.GetComponentInChildren<Text>();
            var dot = Ui.Panel(chip, Color.white, "Icon");
            dot.sprite = World.SummonArt.CurrencyIcon(Currencies.Stamina); dot.preserveAspect = true; dot.raycastTarget = false;
            Ui.Size(dot, 40, 40, 0);
            dot.transform.SetAsFirstSibling();
            chip.GetComponent<HorizontalLayoutGroup>().spacing = 10;
        }

        public override void Tick()
        {
            base.Tick();
            if (_stamina != null && State != null)
                _stamina.text = Fmt.Num(State.Wallet.Get(Currencies.Stamina)) + "/" + EconomySystem.RegenCap(State, Content, Currencies.Stamina);
        }

        protected override void Build(RectTransform body)
        {
            var active = State.Run.Active;
            if (active != null)
            {
                var card = Ui.Card(body, Theme.Tint(Theme.Accent, 0.18f), Theme.Accent, Theme.RadiusCard, 24, 10);
                Ui.Display(card, "Run in progress: " + (Content.RunLevel(active.LevelId)?.Name ?? active.LevelId) + " (" + active.Mode + ")", Theme.FontNormal, Theme.Cream);
                Ui.Button(card, "Continue run", () => App.Router.Push(new RunScreen()), Theme.Accent);
            }
            var top = Ui.Row(body, 64, 12);
            var surge = SurgeLabel(top, State, Content, Now);
            if (surge != null) surge.fontSize = 24;
            else Ui.Label(top, "", 24, Theme.TextDim).name = "SurgeSpacer";
            Ui.Chip(top, "Burrow Keys " + State.Wallet.Get(Currencies.BurrowKeys), Theme.Panel, Theme.TextDim, 22, 52);
            Ui.Button(top, "Refill", () => StaminaModal.Show(this, App), Theme.Good, 170, 64, true, 26);
            Ui.TabBar(body, Tabs, _tab, i => { _tab = i; _page = -1; _mainStart = -1; _selected = null; _focus = true; Rebuild(); });

            var mode = Mode;
            string gate = mode == RunMode.Overcharged ? Features.Overcharged : mode == RunMode.Burrow ? Features.Burrow : mode == RunMode.DarkSide ? Features.DarkSide : null;
            if (gate != null && !Session.IsUnlocked(gate))
            {
                var f = Content.Feature(gate);
                Note(body, (f?.Name ?? gate) + " is locked: " + Requirements.Describe(Content, f?.Requires ?? new RequirementSet()));
            }
            var map = new MapBuilder(this, mode);
            if (mode == RunMode.DarkSide)
            {
                KeyValue(body, "Data", Fmt.Num(State.Wallet.Get(Currencies.Data)) + " (Laboratory research)", Theme.CurrencyColor(Currencies.Data));
                var dark = Content.Portal(ContentDb.DarkSidePortalId);
                if (dark != null) DarkSide(body, dark, map);
            }
            else if (mode == RunMode.Burrow)
            {
                // The Burrow is a single small portal: one dive node.
                foreach (var p in Content.Portals.Values)
                    if (p.Kind == PortalKind.Burrow)
                    {
                        var lv = p.LevelIds.Count > 0 ? Content.RunLevel(p.LevelIds[0]) : null;
                        if (lv != null) map.Section(p, new List<LevelDef> { lv }, false);
                    }
                map.Draw(body);
            }
            else MainPortals(body, mode, map);
            LevelCard(SelectedLevel, mode);
            if (_focus && map.Focus != null) ScrollToAfterBuild(map.Focus);
            _focus = false;
        }

        /// <summary>Main / Overcharged: a page of main portals around the frontier, with paging back to cleared ones.</summary>
        private void MainPortals(RectTransform body, RunMode mode, MapBuilder map)
        {
            int frontier = FrontierPortalIndex(State, Content);
            var (start, count) = MainWindow(frontier, _mainStart);
            int per = System.Math.Max(1, Content.Campaign.LevelsPerPortal);
            var nav = Ui.Row(body, 76, 10);
            Ui.Button(nav, "< Prev", () => { _mainStart = System.Math.Max(0, start - MainPageSize); _focus = true; Rebuild(); }, Theme.Button, 190, 76, start > 0, 28).name = "MainPrev";
            var range = Ui.Label(nav, "Levels " + LevelNum(start * per + 1) + "–" + LevelNum((start + count) * per) + " · next " + LevelNum((long)State.Counter(Requirements.MainLevelCounter) + 1),
                Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter);
            range.name = "MainRange";
            Ui.Button(nav, "Next >", () => { _mainStart = start + MainPageSize; _focus = true; Rebuild(); }, Theme.Button, 190, 76, start + count - 1 < frontier + MainPortalsAhead, 28).name = "MainNext";
            if (_mainStart >= 0 && (frontier < start || frontier >= start + count))
                Ui.Button(body, "Jump to level " + LevelNum((long)State.Counter(Requirements.MainLevelCounter) + 1), () => { _mainStart = -1; _focus = true; _selected = null; Rebuild(); }, Theme.Warn).name = "MainFrontier";
            // newest portal at the top: the path runs upwards from the oldest level at the bottom
            for (int i = start + count - 1; i >= start; i--)
            {
                var portal = Content.MainPortal(i);
                if (portal == null) continue;
                var levels = new List<LevelDef>(portal.LevelIds.Count);
                foreach (var id in portal.LevelIds) { var l = Content.RunLevel(id); if (l != null) levels.Add(l); }
                map.Section(portal, levels, true, "Portal " + LevelNum(i + 1) + (i == frontier ? " · FRONTIER" : ""));
            }
            map.Draw(body);
        }

        /// <summary>The endless Dark Side: pages of levels around the next level to clear, each level showing its Data.</summary>
        private void DarkSide(RectTransform body, PortalDef portal, MapBuilder map)
        {
            int next = DarkSideFrontier(State);
            int page = DarkSidePage(next, _page);
            int lastPage = DarkSidePage(next, int.MaxValue);
            var nav = Ui.Row(body, 76, 10);
            Ui.Button(nav, "< Prev", () => { _page = Mathf.Max(0, page - 1); _focus = true; Rebuild(); }, Theme.Button, 190, 76, page > 0, 28).name = "DarkPrev";
            Ui.Label(nav, "Levels " + LevelNum(page * DarkSidePageSize + 1) + "–" + LevelNum((page + 1) * DarkSidePageSize), Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter);
            Ui.Button(nav, "Next >", () => { _page = page + 1; _focus = true; Rebuild(); }, Theme.Button, 190, 76, page < lastPage, 28).name = "DarkNext";
            var levels = new List<LevelDef>();
            for (int i = page * DarkSidePageSize + 1; i <= (page + 1) * DarkSidePageSize; i++)
            {
                var lv = Content.DarkSideLevel(i);
                if (lv == null) break;
                levels.Add(lv);
            }
            map.Section(portal, levels, false);
            map.Draw(body);
        }

        // ---- the map ------------------------------------------------------------------------------------------

        /// <summary>Collects the sections of the page (top to bottom) and draws nodes, connector dots and banners.</summary>
        private sealed class MapBuilder
        {
            private sealed class Node
            {
                public PortalDef Portal;
                public LevelDef Level;
                public bool Boss;
                public MapNode State;
                public float Y;
                public float X;
            }

            private readonly PortalsScreen _s;
            private readonly RunMode _mode;
            private readonly List<Node> _nodes = new List<Node>();
            private readonly List<KeyValuePair<float, string>> _banners = new List<KeyValuePair<float, string>>();
            private float _y = 40;
            /// <summary>Each section's vertical range and a level of it (its biome backdrop, P1).</summary>
            private readonly List<KeyValuePair<Vector2, LevelDef>> _sections = new List<KeyValuePair<Vector2, LevelDef>>();
            /// <summary>Node to scroll to (selected / next level).</summary>
            public RectTransform Focus;

            private readonly string _nextId;

            public MapBuilder(PortalsScreen s, RunMode mode) { _s = s; _mode = mode; _nextId = s.DefaultLevel()?.Id; }

            /// <summary>Add a portal's levels; the list is in play order, drawn top-down from the last level.</summary>
            public void Section(PortalDef portal, List<LevelDef> levels, bool bossAtEnd, string banner = null)
            {
                float sectionTop = _y;
                if (banner != null) { _banners.Add(new KeyValuePair<float, string>(_y + BannerH / 2 - 10, portal.Name + " · " + banner)); _y += BannerH; }
                var content = _s.Content;
                for (int i = levels.Count - 1; i >= 0; i--)
                {
                    var lv = levels[i];
                    bool boss = bossAtEnd && i == levels.Count - 1 && levels.Count > 1;
                    bool unlocked = RunSystem.IsLevelUnlocked(_s.State, content, lv.Id, _mode);
                    var prog = RunSystem.Progress(_s.State, content, lv.Id);
                    bool cleared = _mode == RunMode.Overcharged ? prog.OverchargedCleared : prog.Cleared;
                    bool next = lv.Id == _nextId;
                    var node = new Node { Portal = portal, Level = lv, Boss = boss, State = NodeStateOf(unlocked, cleared, next) };
                    float step = boss ? BossStep : NodeStep;
                    node.Y = _y + step / 2;
                    _y += step;
                    _nodes.Add(node);
                }
                if (levels.Count > 0) _sections.Add(new KeyValuePair<Vector2, LevelDef>(new Vector2(sectionTop, _y), levels[0]));
            }

            public void Draw(RectTransform body)
            {
                var holder = Ui.Rect(body, "Map");
                Ui.Size(holder, -1, _y + 20);
                int count = _nodes.Count;
                for (int k = 0; k < count; k++)
                {
                    var n = _nodes[k];
                    n.X = n.Boss ? 0.5f : MapX(count - 1 - k); // index along the path, from the bottom
                }
                // P1: each portal's section sits on its biome's ground art (dimmed), the levels joined by a road
                foreach (var sec in _sections)
                {
                    var bg = Ui.Panel(holder, Color.white, "Backdrop");
                    bg.raycastTarget = false;
                    var art = ArtCatalog.Background(ArtCatalog.BiomeFor(sec.Value));
                    if (art != null) { bg.sprite = art; bg.type = Image.Type.Tiled; bg.pixelsPerUnitMultiplier = 0.8f; }
                    else bg.color = Theme.Hex(0x2A2030);
                    var brt = bg.rectTransform;
                    brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(0.5f, 1);
                    brt.sizeDelta = new Vector2(0, sec.Key.y - sec.Key.x); brt.anchoredPosition = new Vector2(0, -sec.Key.x);
                    var dim = Ui.Panel(bg.transform, new Color(0.06f, 0.03f, 0.05f, 0.45f), "Dim");
                    dim.raycastTarget = false;
                    Ui.Stretch(dim.rectTransform);
                }
                float w = _s.BodyFrame != null && _s.BodyFrame.rect.width > 100 ? _s.BodyFrame.rect.width - 48 : 930;
                for (int k = 0; k + 1 < count; k++)
                {
                    var a = _nodes[k];
                    var b = _nodes[k + 1];
                    bool lit = b.State == MapNode.Cleared || b.State == MapNode.Next && a.State != MapNode.Locked;
                    Road(holder, a, b, w, lit);
                }
                // connector dots between consecutive nodes (on the road, behind the nodes)
                for (int k = 0; k + 1 < count; k++)
                {
                    var a = _nodes[k];
                    var b = _nodes[k + 1];
                    bool lit = b.State == MapNode.Cleared || b.State == MapNode.Next && a.State != MapNode.Locked;
                    var color = b.State == MapNode.Cleared ? Theme.AccentLight : lit ? Theme.Accent : Theme.Border;
                    for (int d = 1; d <= 3; d++)
                    {
                        float t = 0.3f + 0.2f * (d - 1);
                        var dot = Ui.Dot(holder, color, 16, "Connector");
                        Place(dot.rectTransform, Mathf.Lerp(b.X, a.X, t), Mathf.Lerp(b.Y, a.Y, t), new Vector2(16, 16));
                    }
                }
                foreach (var kv in _banners)
                {
                    var chip = Ui.Chip(holder, kv.Value, Theme.PanelAlt, Theme.TextDim, 24, 56, Theme.Border, "PortalBanner");
                    chip.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                    Place(chip, 0.5f, kv.Key, new Vector2(0, 56));
                }
                string selected = _s.SelectedLevel?.Id;
                foreach (var n in _nodes)
                {
                    var rt = NodeButton(holder, n, n.Level.Id == selected);
                    if (n.Level.Id == selected || Focus == null && n.State == MapNode.Next) Focus = rt;
                }
            }

            /// <summary>A road segment between two nodes: a dark-edged sandy band, brighter where the path is open (P1).</summary>
            private static void Road(RectTransform holder, Node a, Node b, float width, bool lit)
            {
                float dx = (b.X - a.X) * width, dy = -(b.Y - a.Y);
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                var edge = Ui.RoundPanel(holder, new Color(0.1f, 0.05f, 0.04f, 0.85f), 23, "Road");
                edge.raycastTarget = false;
                Place(edge.rectTransform, (a.X + b.X) / 2, (a.Y + b.Y) / 2, new Vector2(len + 46, 46));
                edge.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
                var fill = Ui.RoundPanel(edge.transform, lit ? Theme.Hex(0xC89A5C) : Theme.Hex(0x6A5446), 16, "RoadFill");
                fill.raycastTarget = false;
                Ui.Stretch(fill.rectTransform, 7, 7, 7, 7);
            }

            private static void Place(RectTransform rt, float x, float y, Vector2 size)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(x, 1);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = size;
                rt.anchoredPosition = new Vector2(0, -y);
            }

            private RectTransform NodeButton(RectTransform holder, Node n, bool selected)
            {
                var content = _s.Content;
                var lv = n.Level;
                var prog = RunSystem.Progress(_s.State, content, lv.Id);
                bool burrow = _mode == RunMode.Burrow;
                bool dark = _mode == RunMode.DarkSide;
                Vector2 size = n.Boss ? BossSize : n.State == MapNode.Next ? new Vector2(NextSize, NextSize) : n.State == MapNode.Locked ? new Vector2(LockedSize, LockedSize) : new Vector2(NodeSize, NodeSize);
                float radius = n.Boss ? 48 : size.x / 2;
                Color fill, ring, text;
                switch (n.State)
                {
                    case MapNode.Next: fill = Theme.Accent; ring = Theme.Cream; text = Theme.OnAccent; break;
                    case MapNode.Cleared: fill = Theme.Hex(0x3A2414); ring = Theme.AccentLight; text = Theme.Cream; break;
                    case MapNode.Open: fill = Theme.Hex(0x3A2414); ring = Theme.Accent; text = Theme.Cream; break;
                    default: fill = Theme.Panel; ring = Theme.Border; text = Theme.TextFaint; break;
                }
                if (n.Boss && n.State != MapNode.Next)
                {
                    fill = n.State == MapNode.Locked ? Theme.Hex(0x2A1416) : Theme.Hex(0x3A1414);
                    ring = n.State == MapNode.Locked ? Theme.Shade(Theme.Bad, 0.55f) : Theme.Bad;
                    text = n.State == MapNode.Locked ? Theme.Hex(0xA06A6C) : Theme.Hex(0xFF9FA2);
                }

                var root = Ui.Rect(holder, "Level:" + lv.Id);
                Place(root, n.X, n.Y, size);
                if (n.State == MapNode.Next)
                {
                    var glow = Ui.RoundPanel(root, new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.35f), radius + 26, "Glow");
                    glow.raycastTarget = false;
                    Ui.Stretch(glow.rectTransform, -26, -26, -26, -26);
                    UiPulse.On(glow, 0.06f, 1.4f, 0.4f);
                }
                // a medallion: a rim plate behind the face (gold once cleared / next, bronze when open, dark when locked) (P1)
                var rim = n.State == MapNode.Locked ? Theme.Hex(0x3A2A28) : n.State == MapNode.Open ? Theme.Hex(0xB07A3C) : Theme.Gold;
                var plate = Ui.RoundPanel(root, rim, radius + 10, "Medal");
                plate.raycastTarget = false;
                Ui.Stretch(plate.rectTransform, -10, -10, -10, -10);
                UiShapes.Outline(plate.rectTransform, SliceKit.Ink, radius + 10, 4);
                var face = Ui.RoundPanel(root, fill, radius, "Face");
                Ui.Stretch(face.rectTransform);
                UiShapes.Outline(face.rectTransform, ring, radius, n.State == MapNode.Next ? 8 : 5);
                if (selected) UiShapes.Outline(Ui.Stretch(Ui.Rect(root, "Selected"), -12, -12, -12, -12), Theme.Text, radius + 12, 4, "SelectedRing");
                var btn = root.gameObject.AddComponent<Button>();
                btn.targetGraphic = face;
                var colors = btn.colors;
                colors.disabledColor = Color.white; // locked nodes are already drawn dimmed
                colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
                btn.colors = colors;
                btn.interactable = RunSystem.IsLevelUnlocked(_s.State, content, lv.Id, _mode);
                string id = lv.Id;
                btn.onClick.AddListener(() => _s.Select(id));

                var col = Ui.Column(face.transform, 0, 0, null, "Text");
                Ui.Stretch(col, 6, 8, 6, 8);
                if (n.Boss)
                {
                    // the boss's portrait on the boss node (P1)
                    var portrait = Ui.Panel(face.transform, n.State == MapNode.Locked ? new Color(0.35f, 0.3f, 0.35f, 1f) : Color.white, "BossPortrait");
                    portrait.sprite = World.IsoArt.BossMarker(content, lv.BossId); portrait.preserveAspect = true; portrait.raycastTarget = false;
                    var prt = portrait.rectTransform;
                    prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0, 0.5f);
                    prt.sizeDelta = new Vector2(150, 150); prt.anchoredPosition = new Vector2(4, 4);
                    Ui.Stretch(col, 140, 8, 6, 8);
                }
                col.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                string number = burrow ? "Dive" : LevelNum(lv.Number > 0 ? lv.Number : lv.Index);
                if (n.Boss)
                {
                    Ui.Display(col, "BOSS · " + number, 34, text, TextAnchor.MiddleCenter, "Number");
                    var bossDef = lv.BossId != null && content.RunBosses.TryGetValue(lv.BossId, out var bd) ? bd : null;
                    Ui.Label(col, bossDef?.Name ?? lv.Name, 24, n.State == MapNode.Locked ? Theme.TextFaint : Theme.Hex(0xFFD0D2), TextAnchor.MiddleCenter, "BossName");
                }
                else Ui.Display(col, number, n.State == MapNode.Next ? 64 : 48, text, TextAnchor.MiddleCenter, "Number");
                if (n.State == MapNode.Next) Ui.Label(col, burrow ? "best floor " + prog.BestFloor : "NEXT", 22, text, TextAnchor.MiddleCenter, "Tag").fontStyle = FontStyle.Bold;
                else if (burrow) Ui.Label(col, "best floor " + prog.BestFloor, 20, text, TextAnchor.MiddleCenter, "Tag");
                if (dark)
                {
                    long data = 0;
                    foreach (var r in lv.CompletionRewards) if (r.Id == Currencies.Data) data += (long)System.Math.Round(r.Amount * lv.RewardMultiplier);
                    Ui.Label(col, prog.Cleared ? "cleared" : "+" + Fmt.Num(data) + " Data", 20, n.State == MapNode.Locked ? Theme.TextFaint : Theme.Premium, TextAnchor.MiddleCenter, "Data");
                }
                else if (!burrow && n.State != MapNode.Locked)
                {
                    string stars = "";
                    for (int i = 0; i < 3; i++) stars += i < prog.Feats.Count && prog.Feats[i] ? "★" : "☆";
                    var st = Ui.Label(col, stars, 26, Theme.Gold, TextAnchor.MiddleCenter, "Stars");
                    st.gameObject.SetActive(n.State == MapNode.Cleared || prog.Feats.Exists(f => f));
                }
                // milestone chest / portal bonus chip beside the node
                string marker = _mode == RunMode.Normal && n.Portal.Kind == PortalKind.Main ? MilestoneMarker(content, lv.Number) : null;
                if (marker != null)
                {
                    bool bonus = marker == "BONUS";
                    var chip = Ui.Chip(root, marker + (prog.Cleared ? " ✓" : ""), prog.Cleared ? Theme.PanelAlt : Theme.Hex(0x3B2A10),
                        prog.Cleared ? Theme.TextFaint : bonus ? Theme.Hex(0xFF9F43) : Theme.Cream, 22, 50, prog.Cleared ? Theme.Border : bonus ? Theme.Hex(0xFF9F43) : Theme.Gold, "Marker");
                    chip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    chip.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                    bool left = n.X > 0.5f || n.Boss;
                    chip.anchorMin = chip.anchorMax = new Vector2(left ? 0 : 1, 0.5f);
                    chip.pivot = new Vector2(left ? 1 : 0, 0.5f);
                    chip.sizeDelta = new Vector2(0, 50);
                    chip.anchoredPosition = new Vector2(left ? -18 : 18, 0);
                }
                return root;
            }
        }

        // ---- bottom level card ---------------------------------------------------------------------------------

        private void LevelCard(LevelDef level, RunMode mode)
        {
            if (Footer == null) return;
            var active = State.Run.Active;
            if (level == null) { Note(Footer, "No level available."); return; }
            var portal = Content.Portal(level.PortalId);
            var prog = RunSystem.Progress(State, Content, level.Id);
            var head = Ui.Row(Footer, CardTitleH, 12);
            string name = mode == RunMode.Burrow ? level.Name : "Level " + LevelNum(level.Number > 0 ? level.Number : level.Index) + " · " + level.Name;
            Ui.Display(head, name, 44, Theme.Text, TextAnchor.MiddleLeft, "LevelName").horizontalOverflow = HorizontalWrapMode.Overflow;
            var threat = Ui.Label(head, "Threat " + level.Threat + (mode == RunMode.Overcharged && portal != null ? " + " + portal.OverchargedThreatBonus : ""), Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight);
            Ui.Size(threat, 220, -1, 0);

            var goals = Ui.Column(Footer, 4, 0, null, "Goals");
            Ui.Size(goals, -1, 3 * GoalH + 8);
            if (mode == RunMode.Burrow)
            {
                Goal(goals, "Best floor " + prog.BestFloor, prog.BestFloor > 0);
                Goal(goals, "Each floor raises the loot — and the danger", false);
                Goal(goals, "Cash out between floors to keep your haul", false);
            }
            else
            {
                string[] feats = { "Clear the level", "Finish with ≥ " + level.FeatHpPct + "% HP", "Beat the boss within " + level.FeatBossRounds + " rounds" };
                for (int i = 0; i < 3; i++) Goal(goals, feats[i], i < prog.Feats.Count && prog.Feats[i]);
            }

            var row = Ui.Row(Footer, StartH, 14);
            Ui.Button(row, "i", () => LevelModal(portal, level, mode), Theme.Button, 124, StartH, portal != null, Theme.FontTitle).name = "LevelDetails";
            if (active != null)
            {
                Ui.Button(row, "CONTINUE RUN", () => App.Router.Push(new RunScreen()), Theme.Accent, -1, StartH, true, 52).name = "StartRun";
                return;
            }
            var cost = RunSystem.EntryCost(Content, level.Id, mode);
            bool unlocked = RunSystem.IsLevelUnlocked(State, Content, level.Id, mode);
            bool gateOpen = mode == RunMode.Normal || Session.IsUnlocked(mode == RunMode.Overcharged ? Features.Overcharged : mode == RunMode.Burrow ? Features.Burrow : Features.DarkSide);
            var start = Ui.Button(row, unlocked ? (mode == RunMode.Burrow ? "DIVE" : "START RUN") : "LOCKED", () => Start(portal, level, mode, cost),
                unlocked ? Theme.Accent : Theme.Button, -1, StartH, unlocked && gateOpen && portal != null, 52);
            start.name = "StartRun";
            var label = start.GetComponentInChildren<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.offsetMin = new Vector2(40, label.rectTransform.offsetMin.y);
            bool afford = State.Wallet.CanAfford(cost.Currency, cost.Amount);
            var costChip = Ui.Chip(label.transform.parent, cost.Amount + " " + Fmt.CurrencyName(cost.Currency).ToLowerInvariant(),
                new Color(0.16f, 0.07f, 0f, 0.16f), afford ? Theme.OnAccent : Theme.Bad, 28, 80, null, "Cost");
            costChip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            costChip.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            costChip.anchorMin = costChip.anchorMax = new Vector2(1, 0.5f);
            costChip.pivot = new Vector2(1, 0.5f);
            costChip.sizeDelta = new Vector2(0, 80);
            costChip.anchoredPosition = new Vector2(-16, 0);
            if (!unlocked) costChip.gameObject.SetActive(false);
        }

        private static void Goal(Transform parent, string text, bool done)
        {
            var row = Ui.Row(parent, GoalH - 4, 14);
            var pip = Ui.Label(row, done ? "★" : "○", 34, done ? Theme.Gold : Theme.TextFaint, TextAnchor.MiddleCenter, "Pip");
            Ui.Size(pip, 44, -1, 0);
            Ui.Label(row, text, Theme.FontSmall, done ? Theme.Text : Theme.TextDim, TextAnchor.MiddleLeft, "Goal");
        }

        /// <summary>Full level details (threat, boss, dice, modifiers, feats, rewards, milestone chest) with a start button.</summary>
        public void LevelModal(PortalDef portal, LevelDef level, RunMode mode)
        {
            var cost = RunSystem.EntryCost(Content, level.Id, mode);
            var prog = RunSystem.Progress(State, Content, level.Id);
            var boss = level.BossId != null && Content.RunBosses.TryGetValue(level.BossId, out var bd) ? bd : null;
            bool unlocked = RunSystem.IsLevelUnlocked(State, Content, level.Id, mode);
            // P3: "Level 10 · Ember Wastes 10" (the portal name repeated the level's)
            string title = mode == RunMode.Burrow ? level.Name : "Level " + LevelNum(level.Number > 0 ? level.Number : level.Index) + " · " + level.Name;
            Modal(title, b =>
            {
                KeyValue(b, "Threat", level.Threat + (mode == RunMode.Overcharged ? " + " + portal.OverchargedThreatBonus : ""));
                KeyValue(b, "Boss", boss?.Name ?? "—", Theme.ValenceColor(Valence.Boss));
                KeyValue(b, "Dice", DiceText(level));
                KeyValue(b, "Entry", cost.Amount + " " + Fmt.CurrencyName(cost.Currency), State.Wallet.CanAfford(cost.Currency, cost.Amount) ? Theme.Text : Theme.Bad);
                if (!string.IsNullOrEmpty(level.Modifiers?.Text)) Ui.Label(b, level.Modifiers.Text, Theme.FontSmall, Theme.Warn);
                if (mode != RunMode.Burrow)
                {
                    string[] feats = { "Clear the level", "Finish with ≥ " + level.FeatHpPct + "% HP", "Beat the boss within " + level.FeatBossRounds + " rounds" };
                    for (int i = 0; i < 3; i++)
                        Ui.Label(b, (i < prog.Feats.Count && prog.Feats[i] ? "★ " : "☆ ") + feats[i], Theme.FontSmall, i < prog.Feats.Count && prog.Feats[i] ? Theme.Good : Theme.TextDim);
                }
                // completion rewards once the level has been played (P3: "Completion: —" before)
                string completion = level.CompletionRewards.Count > 0 && (prog.Cleared || prog.Feats.Exists(f => f)) ? Fmt.Rewards(level.CompletionRewards, Content, State, Now) : null;
                if (!string.IsNullOrEmpty(completion) && completion != "—") Note(b, "Completion: " + completion);
                if (!unlocked) Ui.Label(b, LockedReason(level, mode), Theme.FontSmall, Theme.Bad, TextAnchor.MiddleCenter, "LockedReason");
                string marker = mode == RunMode.Normal && portal.Kind == PortalKind.Main ? MilestoneMarker(Content, level.Number) : null;
                if (marker != null && level.FirstClearRewards.Count > 0)
                    Ui.Label(b, (marker == "BONUS" ? "Portal bonus" : "Milestone chest") + " (first clear" + (prog.Cleared ? ", claimed" : "") + "): "
                                + Fmt.Rewards(level.FirstClearRewards, Content, State, Now), Theme.FontSmall, prog.Cleared ? Theme.TextDim : Theme.Premium);
                if (State.Run.Active != null) Ui.Label(b, "Finish or abandon your current run first.", Theme.FontSmall, Theme.Bad);
            },
            unlocked ? new[] { new ModalButton("Cancel", null),
                new ModalButton("Start (" + cost.Amount + " " + Fmt.CurrencyName(cost.Currency) + ")", () => Start(portal, level, mode, cost), Theme.Accent) } : new[] { new ModalButton("Close", null) });
        }

        /// <summary>"2 dice · 45 rolls" (P3: was "2d6").</summary>
        public static string DiceText(LevelDef level) => level.Dice + (level.Dice == 1 ? " die" : " dice") + " · " + level.MaxRolls + " rolls";

        /// <summary>Why a level cannot be started yet: "Clear level 9 first".</summary>
        public string LockedReason(LevelDef level, RunMode mode)
        {
            long n = level.Number > 0 ? level.Number : level.Index;
            if (mode == RunMode.Normal && n > 1) return "Clear level " + LevelNum(n - 1) + " first";
            if (mode == RunMode.Overcharged) return "Clear it normally first";
            return "Locked";
        }

        private void Start(PortalDef portal, LevelDef level, RunMode mode, Cost cost)
        {
            if (!State.Wallet.CanAfford(cost.Currency, cost.Amount) && cost.Currency == Currencies.Stamina)
            {
                StaminaModal.Show(this, App);
                return;
            }
            SignaturePick.StartRun(App, new RunStart { PortalId = portal.Id, LevelId = level.Id, Mode = mode }, r => { if (r.Ok) App.Router.Push(new RunScreen()); });
        }
    }

    /// <summary>Stamina refill options (daily free, ad, Diamonds with a rising price).</summary>
    public static class StaminaModal
    {
        public static void Show(UiScreen owner, ClientApp app)
        {
            var s = app.Session;
            var info = EconomySystem.GetStaminaRefillInfo(s.State, s.Content, s.NowMs);
            Ui.Modal(app.ModalLayer, "Refill stamina", b =>
            {
                Ui.Display(b, "+" + info.Amount + " Stamina per refill", Theme.FontLarge, Theme.CurrencyColor(Currencies.Stamina), TextAnchor.MiddleCenter);
                Ui.Button(b, "Daily free (" + info.FreeRefillsLeft + " left)", () => s.Run(new BuyStamina { Method = StaminaRefillMethod.DailyFree }), Theme.Good, -1, Theme.ButtonHeight, info.FreeRefillsLeft > 0);
                Ui.Button(b, (info.AdsInstant ? "Instant (No Ads)" : "Watch ad") + " (" + info.AdRefillsLeft + " left)", () => s.Ads.Show("stamina_refill", ad =>
                {
                    if (ad.Ok) s.Run(new BuyStamina { Method = StaminaRefillMethod.Ad, AdToken = ad.Value });
                }), Theme.Button, -1, Theme.ButtonHeight, info.AdRefillsLeft > 0);
                Ui.Button(b, info.NextDiamondPrice < 0 ? "Diamond refills used up" : info.NextDiamondPrice + " Diamonds (" + info.DiamondRefillsLeft + " left)",
                    () => s.Run(new BuyStamina { Method = StaminaRefillMethod.Diamonds }), Theme.Premium, -1, Theme.ButtonHeight,
                    info.NextDiamondPrice >= 0 && s.State.Wallet.CanAfford(Currencies.Diamonds, info.NextDiamondPrice));
            }, new ModalButton("Close", null));
        }
    }
}
