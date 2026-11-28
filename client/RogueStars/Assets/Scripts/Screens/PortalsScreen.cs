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
    /// The level selection: the campaign as a chain of portals, one per level ("portal after portal"), each a round window
    /// with its biome peeking through (the biome's board art, ArtCatalog.BiomeFor), running upwards along a zig-zag road:
    /// cleared portals show their stars, the next level glows, locked ones are dimmed. Main / Overcharged / Burrow / Dark Side
    /// tabs, a header with the next level and the stamina, and a fixed bottom level card (star goals, START RUN with the entry
    /// cost, details). Tapping a portal selects it for the card. There are no boss levels: every level ends with its biome's
    /// boss. The campaign and the Dark Side are endless: both are shown as a bounded window around the frontier
    /// (RunSystem.NextMainLevel, the Dark Side max counter) built with ContentDb.MainLevel / DarkSideLevel; nothing iterates
    /// the whole campaign.
    /// </summary>
    public sealed class PortalsScreen : UiScreen
    {
        private static readonly string[] Tabs = { "Main", "Overcharged", "Burrow", "Dark Side" };
        private int _tab;
        /// <summary>Dark Side page (-1 = the page with the next level to clear).</summary>
        private int _page = -1;
        /// <summary>First main level shown (-1 = the window around the frontier).</summary>
        private int _mainStart = -1;
        /// <summary>Level shown in the bottom card (null = the next level to play on this tab).</summary>
        private string _selected;
        /// <summary>Scroll the map to the focused node on the next build (opening, tab or page change).</summary>
        private bool _focus = true;
        private Text _stamina;
        public const int DarkSidePageSize = 25;
        /// <summary>Dark Side pages browsable past the one holding the next level.</summary>
        public const int DarkSidePagesAhead = 1;
        /// <summary>Main levels per page: cleared ones before the next level, the next level, upcoming ones after it.</summary>
        public const int MainPageSize = 30;
        public const int MainLevelsBehind = 20;
        public const int MainLevelsAhead = 9;

        /// <summary>Map geometry: vertical distance between two portals and the portal sizes (next is bigger, locked smaller).</summary>
        public const float NodeStep = 236, NodeSize = 176, NextSize = 220, LockedSize = 156;
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

        private static readonly float[] ZigZag = { 0.26f, 0.7f, 0.42f, 0.24f, 0.62f, 0.3f, 0.74f, 0.46f };

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

        /// <summary>Number of the next main level to clear (the frontier), clamped to the generated range.</summary>
        public static int MainFrontier(GameState s) =>
            (int)System.Math.Min(ContentDb.MaxGeneratedIndex - MainLevelsAhead, s.Counter(Requirements.MainLevelCounter) + 1);

        /// <summary>0-based index of the main portal holding the next level to clear (every level is its own portal: level − 1).</summary>
        public static int FrontierPortalIndex(GameState s, ContentDb c) => MainFrontier(s) - 1;

        /// <summary>
        /// The main levels to show: <paramref name="requestedStart"/> (-1 = around the frontier) clamped so the window never
        /// starts before level 1 and never goes past <see cref="MainLevelsAhead"/> levels beyond the next one. Returns the
        /// first level number and the number of levels (≤ <see cref="MainPageSize"/>).
        /// </summary>
        public static (int start, int count) MainWindow(int next, int requestedStart)
        {
            int last = next + MainLevelsAhead;
            int maxStart = System.Math.Max(1, last - MainPageSize + 1);
            int start = requestedStart < 1 ? System.Math.Max(1, next - MainLevelsBehind) : requestedStart;
            start = System.Math.Max(1, System.Math.Min(maxStart, start));
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

        /// <summary>"BONUS" on every bonus-chest level (every 50th), "CHEST" on every other milestone level (every 10th), else null.</summary>
        public static string MilestoneMarker(ContentDb c, int levelNumber)
        {
            var k = c.Campaign;
            if (levelNumber <= 0) return null;
            if (k.BonusChestEveryLevels > 0 && levelNumber % k.BonusChestEveryLevels == 0) return "BONUS";
            if (k.MilestoneEveryLevels > 0 && levelNumber % k.MilestoneEveryLevels == 0) return "CHEST";
            return null;
        }

        /// <summary>Level numbers in full (no K/M abbreviation: "level 12345").</summary>
        public static string LevelNum(long n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A level's title: main levels "55. Gas Giant" (number and biome), Dark Side "Dark Side 7", Burrow its name.</summary>
        public static string LevelTitle(LevelDef level)
        {
            if (level == null) return "";
            if (level.Number > 0 && level.PortalId != ContentDb.DarkSidePortalId) return LevelNum(level.Number) + ". " + level.Name;
            return level.Name;
        }

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
                        var (start, count) = MainWindow(MainFrontier(State), -1);
                        for (int n = start; n < start + count; n++)
                        {
                            var id = Content.MainLevelId(n);
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
            Refresh(); // in place: the two nodes whose ring changes and the level card
        }

        /// <summary>"?": the run content and the modes introduced level by level (GDD §16).</summary>
        public override string[] IntroFeatures => new[]
        {
            Features.RunSignatures, Features.RunMiniboss, Features.RunTwoDice, Features.AutoRoll, Features.RunCampfire,
            Features.RunMinigames, Features.Overcharged, Features.Burrow, Features.DarkSide,
        };

        public override string Title
        {
            get
            {
                if (State == null) return "Portals";
                if (Mode == RunMode.DarkSide) return Content.Portal(ContentDb.DarkSidePortalId)?.Name ?? "Dark Side";
                if (Mode == RunMode.Burrow) return "The Burrow";
                return "Portals";
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
                if (Mode == RunMode.Burrow) return "Dive floor after floor · cash out any time";
                var lv = RunSystem.NextMainLevel(State, Content);
                long cleared = State.Counter(Requirements.MainLevelCounter);
                return (Mode == RunMode.Overcharged ? "Overcharged · " : "") + "Next: " + LevelTitle(lv) + " · " + LevelNum(cleared) + " cleared";
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

        /// <summary>In place: the map's nodes, its road / decoration layer, the rows and the level card are keyed parts.</summary>
        protected override bool InPlace => true;

        protected override void Build(RectTransform body)
        {
            var active = State.Run.Active;
            if (active != null)
                Part(body, "active", K(active.LevelId, active.Mode), b =>
                {
                    var card = Ui.Card(b, Theme.Tint(Theme.Accent, 0.18f), Theme.Accent, Theme.RadiusCard, 24, 10);
                    Ui.Display(card, "Run in progress: " + (Content.RunLevel(active.LevelId) != null ? LevelTitle(Content.RunLevel(active.LevelId)) : active.LevelId) + " (" + active.Mode + ")", Theme.FontNormal, Theme.Cream);
                    Ui.Button(card, "Continue run", () => App.Router.Push(new RunScreen()), Theme.Accent);
                });
            bool surgeOn = RunSystem.BiomassSurgeActive(State, Content, Now);
            Part(body, "top", K(surgeOn, surgeOn ? RunSystem.BiomassBonusRunsLeft(State, Content, Now) : 0, State.Wallet.Get(Currencies.BurrowKeys)), b =>
            {
                var top = Ui.Row(b, 64, 12);
                var surge = SurgeLabel(top, State, Content, Now);
                if (surge != null) surge.fontSize = 24;
                else Ui.Label(top, "", 24, Theme.TextDim).name = "SurgeSpacer";
                Ui.Chip(top, "Burrow Keys " + State.Wallet.Get(Currencies.BurrowKeys), Theme.Panel, Theme.TextDim, 22, 52);
                Ui.Button(top, "Refill", () => StaminaModal.Show(this, App), Theme.Good, 170, 64, true, 26);
            });
            Part(body, "tabs", K(_tab), b => Ui.TabBar(b, Tabs, _tab, i => { _tab = i; _page = -1; _mainStart = -1; _selected = null; _focus = true; Refresh(); }));

            var mode = Mode;
            string gate = mode == RunMode.Overcharged ? Features.Overcharged : mode == RunMode.Burrow ? Features.Burrow : mode == RunMode.DarkSide ? Features.DarkSide : null;
            if (gate != null && !Session.IsUnlocked(gate))
            {
                var f = Content.Feature(gate);
                string locked = (f?.Name ?? gate) + " is locked: " + Requirements.Describe(Content, f?.Requires ?? new RequirementSet());
                Part(body, "gate", locked, b => Note(b, locked));
            }
            var map = new MapBuilder(this, mode);
            if (mode == RunMode.DarkSide)
            {
                string data = Fmt.Num(State.Wallet.Get(Currencies.Data));
                Part(body, "data", data, b => KeyValue(b, "Data", data + " (Laboratory research)", Theme.CurrencyColor(Currencies.Data)));
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
                        if (lv != null) map.Add(lv);
                    }
                map.Draw(body);
            }
            else MainPortals(body, map);
            LevelCard(SelectedLevel, mode);
            if (_focus && map.Focus != null) ScrollToAfterBuild(map.Focus);
            _focus = false;
        }

        /// <summary>Main / Overcharged: a page of the portal chain around the next level, with paging back to cleared ones.</summary>
        private void MainPortals(RectTransform body, MapBuilder map)
        {
            int next = MainFrontier(State);
            var (start, count) = MainWindow(next, _mainStart);
            Part(body, "mainNav", K(start, count, next), b =>
            {
                var nav = Ui.Row(b, 76, 10);
                Ui.Button(nav, "< Prev", () => { _mainStart = System.Math.Max(1, start - MainPageSize); _focus = true; Refresh(); }, Theme.Button, 190, 76, start > 1, 28).name = "MainPrev";
                var range = Ui.Label(nav, "Levels " + LevelNum(start) + "–" + LevelNum(start + count - 1) + " · next " + LevelNum(next),
                    Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter);
                range.name = "MainRange";
                Ui.Button(nav, "Next >", () => { _mainStart = start + MainPageSize; _focus = true; Refresh(); }, Theme.Button, 190, 76, start + count - 1 < next + MainLevelsAhead, 28).name = "MainNext";
            });
            if (_mainStart >= 1 && (next < start || next >= start + count))
                Part(body, "mainJump", K(next), b =>
                    Ui.Button(b, "Jump to level " + LevelNum(next), () => { _mainStart = -1; _focus = true; _selected = null; Refresh(); }, Theme.Warn).name = "MainFrontier");
            // one portal per level, the newest at the top: the chain climbs from the oldest level at the bottom
            for (int n = start + count - 1; n >= start; n--)
            {
                var l = Content.MainLevel(n);
                if (l != null) map.Add(l);
            }
            map.Draw(body);
        }

        /// <summary>The endless Dark Side: pages of levels around the next level to clear, each level showing its Data.</summary>
        private void DarkSide(RectTransform body, PortalDef portal, MapBuilder map)
        {
            int next = DarkSideFrontier(State);
            int page = DarkSidePage(next, _page);
            int lastPage = DarkSidePage(next, int.MaxValue);
            Part(body, "darkNav", K(page, lastPage), b =>
            {
                var nav = Ui.Row(b, 76, 10);
                Ui.Button(nav, "< Prev", () => { _page = Mathf.Max(0, page - 1); _focus = true; Refresh(); }, Theme.Button, 190, 76, page > 0, 28).name = "DarkPrev";
                Ui.Label(nav, "Levels " + LevelNum(page * DarkSidePageSize + 1) + "–" + LevelNum((page + 1) * DarkSidePageSize), Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter);
                Ui.Button(nav, "Next >", () => { _page = page + 1; _focus = true; Refresh(); }, Theme.Button, 190, 76, page < lastPage, 28).name = "DarkNext";
            });
            for (int i = (page + 1) * DarkSidePageSize; i >= page * DarkSidePageSize + 1; i--)
            {
                var lv = Content.DarkSideLevel(i);
                if (lv != null) map.Add(lv);
            }
            map.Draw(body);
        }

        // ---- the map ------------------------------------------------------------------------------------------

        /// <summary>Collects the portals of the page (top to bottom) and draws the road, the connector dots and the portals.</summary>
        private sealed class MapBuilder
        {
            private sealed class Node
            {
                public LevelDef Level;
                public MapNode State;
                public float Y;
                public float X;
            }

            private readonly PortalsScreen _s;
            private readonly RunMode _mode;
            private readonly List<Node> _nodes = new List<Node>();
            private float _y = 60;
            /// <summary>Node to scroll to (selected / next level).</summary>
            public RectTransform Focus;

            private readonly string _nextId;

            public MapBuilder(PortalsScreen s, RunMode mode) { _s = s; _mode = mode; _nextId = s.DefaultLevel()?.Id; }

            /// <summary>Add the next portal down the page (callers add from the newest level to the oldest).</summary>
            public void Add(LevelDef lv)
            {
                var content = _s.Content;
                bool unlocked = RunSystem.IsLevelUnlocked(_s.State, content, lv.Id, _mode);
                var prog = RunSystem.Progress(_s.State, content, lv.Id);
                bool cleared = _mode == RunMode.Overcharged ? prog.OverchargedCleared : prog.Cleared;
                var node = new Node { Level = lv, State = NodeStateOf(unlocked, cleared, lv.Id == _nextId) };
                node.Y = _y + NodeStep / 2;
                _y += NodeStep;
                _nodes.Add(node);
            }

            public void Draw(RectTransform body)
            {
                int count = _nodes.Count;
                for (int k = 0; k < count; k++)
                {
                    var n = _nodes[k];
                    // index along the path from the bottom; the level number keeps a portal's place stable across pages
                    int pathIndex = n.Level.Number > 0 ? n.Level.Number - 1 : count - 1 - k;
                    n.X = count == 1 ? 0.5f : MapX(pathIndex);
                }
                float w = _s.BodyFrame != null && _s.BodyFrame.rect.width > 100 ? _s.BodyFrame.rect.width - 48 : 930;
                // the map's frame (its height and the node list) is a part; the road layer and every portal are parts in it
                var layout = new System.Text.StringBuilder();
                var states = new System.Text.StringBuilder().Append(w).Append('|');
                foreach (var n in _nodes) { layout.Append(n.Level.Id).Append(','); states.Append((int)n.State); }
                var holder = _s.Part(body, "map", K(_y, layout.ToString()), b =>
                {
                    var h = Ui.Rect(b, "Map");
                    Ui.Size(h, -1, _y + 40);
                });
                _s.Part(holder, "deco", states.ToString(), h => Decoration(h, w));
                string selected = _s.SelectedLevel?.Id;
                foreach (var n in _nodes)
                {
                    var node = n;
                    bool sel = n.Level.Id == selected;
                    var rt = _s.Part(holder, "node:" + n.Level.Id, NodeKey(n, sel), h => NodeButton(h, node, sel));
                    if (sel || Focus == null && n.State == MapNode.Next) Focus = rt;
                }
            }

            /// <summary>Everything a node shows (its part key).</summary>
            private string NodeKey(Node n, bool selected)
            {
                var prog = RunSystem.Progress(_s.State, _s.Content, n.Level.Id);
                var feats = new System.Text.StringBuilder();
                foreach (var f in prog.Feats) feats.Append(f ? '1' : '0');
                return K(n.State, n.X, n.Y, selected, prog.Cleared, prog.BestFloor, feats.ToString(), RunSystem.IsLevelUnlocked(_s.State, _s.Content, n.Level.Id, _mode),
                    RunSystem.LongestRolls(_s.State, n.Level.Id));
            }

            /// <summary>The night-sky backdrop, the road between consecutive portals and its connector dots (under the portals).</summary>
            private void Decoration(RectTransform holder, float w)
            {
                var sky = Ui.Panel(holder, Theme.Hex(0x170E22), "Backdrop");
                sky.raycastTarget = false;
                Ui.Stretch(sky.rectTransform, -24, 0, -24, 0);
                // a few faint stars (fixed pattern: the layer is rebuilt only when the nodes change)
                for (int i = 0; i < _nodes.Count * 3; i++)
                {
                    float x = Mathf.Repeat(i * 0.6180339f + 0.13f, 1f), y = 30 + Mathf.Repeat(i * 0.4142136f, 1f) * (_y - 20);
                    var star = Ui.Dot(holder, new Color(1, 1, 1, 0.08f + 0.1f * Mathf.Repeat(i * 0.37f, 1f)), 6 + (i % 3) * 3, "Star");
                    star.raycastTarget = false;
                    Place(star.rectTransform, x, y, new Vector2(6 + (i % 3) * 3, 6 + (i % 3) * 3));
                }
                int count = _nodes.Count;
                for (int k = 0; k + 1 < count; k++)
                {
                    var a = _nodes[k];
                    var b = _nodes[k + 1];
                    bool lit = b.State == MapNode.Cleared || b.State == MapNode.Next && a.State != MapNode.Locked;
                    Road(holder, a, b, w, lit);
                }
                // connector dots between consecutive portals (on the road, behind the portals)
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
            }

            /// <summary>A road segment between two portals: a dark-edged sandy band, brighter where the path is open.</summary>
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

            /// <summary>
            /// One portal: a round window into the level's biome (its board art behind a circular mask), an energy ring and a
            /// metal rim (gold once cleared / next, bronze when open, dark when locked: the biome dimmed), the level number on
            /// a badge at its foot and, beside it, the biome name, the stars (cleared) or NEXT / best roll count, and the
            /// milestone chest chip.
            /// </summary>
            private RectTransform NodeButton(RectTransform holder, Node n, bool selected)
            {
                var content = _s.Content;
                var lv = n.Level;
                var prog = RunSystem.Progress(_s.State, content, lv.Id);
                bool burrow = _mode == RunMode.Burrow;
                bool dark = _mode == RunMode.DarkSide;
                bool locked = n.State == MapNode.Locked;
                float d = n.State == MapNode.Next ? NextSize : locked ? LockedSize : NodeSize;
                float radius = d / 2;

                var root = Ui.Rect(holder, "Level:" + lv.Id);
                Place(root, n.X, n.Y, new Vector2(d, d));
                if (n.State == MapNode.Next)
                {
                    var glow = Ui.RoundPanel(root, new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.38f), radius + 30, "Glow");
                    glow.raycastTarget = false;
                    Ui.Stretch(glow.rectTransform, -30, -30, -30, -30);
                    UiPulse.On(glow, 0.06f, 1.4f, 0.4f);
                }
                var rimColor = locked ? Theme.Hex(0x3A2A38) : n.State == MapNode.Open ? Theme.Hex(0xB07A3C) : Theme.Gold;
                var rim = Ui.RoundPanel(root, rimColor, radius + 12, "Medal");
                rim.raycastTarget = false;
                Ui.Stretch(rim.rectTransform, -12, -12, -12, -12);
                UiShapes.Outline(rim.rectTransform, SliceKit.Ink, radius + 12, 4);

                // the window: the biome's board art behind a circular mask
                var face = Ui.Panel(root, Color.white, "Face");
                face.sprite = UiShapes.Circle;
                Ui.Stretch(face.rectTransform);
                face.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                face.color = Theme.Hex(0x2A1E3A);
                var art = ArtCatalog.Background(ArtCatalog.BiomeFor(lv));
                if (art != null)
                {
                    var biome = Ui.Panel(face.transform, locked ? new Color(0.32f, 0.3f, 0.38f, 1) : Color.white, "Biome");
                    biome.sprite = art; biome.raycastTarget = false;
                    var brt = biome.rectTransform;
                    float aspect = art.rect.height / Mathf.Max(1, art.rect.width);
                    float bw = aspect >= 1 ? d : d / aspect, bh = aspect >= 1 ? d * aspect : d;
                    brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
                    brt.sizeDelta = new Vector2(bw * 1.15f, bh * 1.15f); // a close crop: the biome's middle fills the window
                    brt.anchoredPosition = Vector2.zero;
                }
                // the portal's energy: a swirl ring inside the rim, a soft dark edge so the window reads as depth
                var swirl = Ui.Panel(face.transform, locked ? new Color(0.25f, 0.2f, 0.3f, 0.9f) : new Color(0.62f, 0.42f, 1f, 0.85f), "Swirl");
                swirl.sprite = UiShapes.CircleRing; swirl.raycastTarget = false;
                Ui.Stretch(swirl.rectTransform);
                var inner = Ui.Panel(face.transform, new Color(0.05f, 0.02f, 0.1f, 0.45f), "Edge");
                inner.sprite = UiShapes.CircleRing; inner.raycastTarget = false;
                Ui.Stretch(inner.rectTransform, d * 0.1f, d * 0.1f, d * 0.1f, d * 0.1f);
                if (selected) UiShapes.Outline(Ui.Stretch(Ui.Rect(root, "Selected"), -20, -20, -20, -20), Theme.Text, radius + 20, 4, "SelectedRing");

                var btn = root.gameObject.AddComponent<Button>();
                btn.targetGraphic = face;
                var colors = btn.colors;
                colors.disabledColor = Color.white; // locked portals are already drawn dimmed
                colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
                btn.colors = colors;
                btn.interactable = RunSystem.IsLevelUnlocked(_s.State, content, lv.Id, _mode);
                string id = lv.Id;
                btn.onClick.AddListener(() => _s.Select(id));

                // the level number on a badge at the portal's foot
                string number = burrow ? "Dive" : LevelNum(lv.Number > 0 ? lv.Number : lv.Index);
                var badge = Ui.RoundPanel(root, n.State == MapNode.Next ? Theme.Accent : locked ? Theme.Hex(0x2A2232) : Theme.Hex(0x3A2414), 28, "Badge");
                badge.raycastTarget = false;
                var bdr = badge.rectTransform;
                bdr.anchorMin = bdr.anchorMax = new Vector2(0.5f, 0);
                bdr.pivot = new Vector2(0.5f, 0.5f);
                bdr.sizeDelta = new Vector2(Mathf.Max(96, 30 + 28 * number.Length), 58);
                bdr.anchoredPosition = new Vector2(0, 2);
                UiShapes.Outline(bdr, locked ? Theme.Border : Theme.Gold, 28, 4);
                var num = SliceKit.Chunky(badge.transform, number, n.State == MapNode.Next ? 40 : 36, locked ? Theme.TextFaint : Color.white, TextAnchor.MiddleCenter, "Number");
                Ui.Stretch(num.rectTransform);

                // beside the portal, towards the middle of the map: the biome, the stars / NEXT, the milestone chip
                bool right = n.X <= 0.5f;
                var side = Ui.Rect(root, "Side");
                side.anchorMin = side.anchorMax = new Vector2(right ? 1 : 0, 0.5f);
                side.pivot = new Vector2(right ? 0 : 1, 0.5f);
                side.sizeDelta = new Vector2(360, d);
                side.anchoredPosition = new Vector2(right ? 26 : -26, 0);
                var align = right ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                float y = 0;
                Text SideText(string text, int size, Color color, string name, float h)
                {
                    var t = SliceKit.Chunky(side, text, size, color, align, name);
                    var r = t.rectTransform;
                    r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
                    r.sizeDelta = new Vector2(0, h); r.anchoredPosition = new Vector2(0, -y);
                    y += h + 4;
                    return t;
                }
                string biomeName = burrow ? lv.Name : dark ? "Dark Side" : lv.Name;
                SideText(biomeName, 30, locked ? Theme.TextFaint : Theme.Cream, "BiomeName", 38);
                if (n.State == MapNode.Next)
                {
                    SideText(burrow ? "best floor " + prog.BestFloor : "NEXT", 26, Theme.AccentLight, "Tag", 32);
                    int longest = RunSystem.LongestRolls(_s.State, lv.Id);
                    if (!burrow && longest > 0) SideText("best " + longest + " rolls", 22, Theme.TextDim, "Best", 28);
                }
                else if (burrow) SideText("best floor " + prog.BestFloor, 22, Theme.TextDim, "Tag", 28);
                if (dark)
                {
                    long data = 0;
                    foreach (var r in lv.CompletionRewards) if (r.Id == Currencies.Data) data += (long)System.Math.Round(r.Amount * lv.RewardMultiplier);
                    SideText(prog.Cleared ? "cleared" : "+" + Fmt.Num(data) + " Data", 22, locked ? Theme.TextFaint : Theme.Premium, "Data", 28);
                }
                if (!burrow && !locked && (n.State == MapNode.Cleared || prog.Feats.Exists(f => f)))
                {
                    string stars = "";
                    for (int i = 0; i < 3; i++) stars += i < prog.Feats.Count && prog.Feats[i] ? "★" : "☆";
                    SideText(stars, 30, Theme.Gold, "Stars", 34);
                }
                // milestone chest / bonus chest chip
                string marker = _mode == RunMode.Normal && content.Portal(lv.PortalId)?.Kind == PortalKind.Main ? MilestoneMarker(content, lv.Number) : null;
                if (marker != null)
                {
                    bool bonus = marker == "BONUS";
                    var chip = Ui.Chip(side, marker + (prog.Cleared ? " ✓" : ""), prog.Cleared ? Theme.PanelAlt : Theme.Hex(0x3B2A10),
                        prog.Cleared ? Theme.TextFaint : bonus ? Theme.Hex(0xFF9F43) : Theme.Cream, 22, 46, prog.Cleared ? Theme.Border : bonus ? Theme.Hex(0xFF9F43) : Theme.Gold, "Marker");
                    chip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    chip.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                    chip.anchorMin = chip.anchorMax = new Vector2(right ? 0 : 1, 1);
                    chip.pivot = new Vector2(right ? 0 : 1, 1);
                    chip.sizeDelta = new Vector2(0, 46);
                    chip.anchoredPosition = new Vector2(0, -y - 2);
                    y += 50;
                }
                // centre the side block's content vertically on the portal
                foreach (RectTransform c in side) c.anchoredPosition += new Vector2(0, -(d - y) / 2);
                return root;
            }
        }

        // ---- bottom level card ---------------------------------------------------------------------------------

        private void LevelCard(LevelDef level, RunMode mode)
        {
            if (Footer == null) return;
            if (level == null) { Part(Footer, "none", "", f => Note(f, "No level available.")); return; }
            var prog = RunSystem.Progress(State, Content, level.Id);
            var feats = new System.Text.StringBuilder();
            foreach (var f in prog.Feats) feats.Append(f ? '1' : '0');
            var cost = RunSystem.EntryCost(Content, level.Id, mode);
            string key = K(level.Id, mode, State.Run.Active != null, prog.BestFloor, feats.ToString(), RunSystem.IsLevelUnlocked(State, Content, level.Id, mode),
                mode == RunMode.Normal || Session.IsUnlocked(mode == RunMode.Overcharged ? Features.Overcharged : mode == RunMode.Burrow ? Features.Burrow : Features.DarkSide),
                cost.Amount, cost.Currency, State.Wallet.CanAfford(cost.Currency, cost.Amount));
            Part(Footer, "card", key, f => LevelCardBody(f, level, mode));
        }

        private void LevelCardBody(RectTransform footer, LevelDef level, RunMode mode)
        {
            var Footer = footer;
            var active = State.Run.Active;
            var portal = Content.Portal(level.PortalId);
            var prog = RunSystem.Progress(State, Content, level.Id);
            var head = Ui.Row(Footer, CardTitleH, 12);
            string name = LevelTitle(level);
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
            string title = LevelTitle(level); // "55. Gas Giant"
            Modal(title, b =>
            {
                KeyValue(b, "Threat", level.Threat + (mode == RunMode.Overcharged ? " + " + portal.OverchargedThreatBonus : ""));
                if (!string.IsNullOrEmpty(level.Biome)) KeyValue(b, "Biome", level.Name);
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
                    Ui.Label(b, (marker == "BONUS" ? "Bonus chest" : "Milestone chest") + " (first clear" + (prog.Cleared ? ", claimed" : "") + "): "
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
