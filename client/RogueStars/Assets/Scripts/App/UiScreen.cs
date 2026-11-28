using System;
using System.Collections.Generic;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.App
{
    /// <summary>
    /// Base class of every screen. A screen is a plain object that owns a RectTransform: a header (back + title)
    /// and a scrolling body that is rebuilt from the current state on every state change. Countdown texts are
    /// registered with <see cref="Live"/> and refreshed by <see cref="Tick"/> without rebuilding.
    /// </summary>
    public abstract class UiScreen
    {
        private sealed class LiveText
        {
            public Text Text;
            public Func<string> Value;
            public PartState Part;
        }

        private readonly List<LiveText> _live = new List<LiveText>();
        private RectTransform _body;
        private ScrollRect _scroll;
        private Text _title;
        private Text _subtitle;
        private RectTransform _scrollTarget;
        private bool _builtOnce;
        private long _builtVersion = long.MinValue;
        private string _builtPlayer;

        public ClientApp App { get; private set; }
        public RectTransform Root { get; private set; }
        public bool IsCreated => Root != null;
        public abstract string Title { get; }
        /// <summary>Height of the header (back + title); 0 = no header (the hub and full-screen moments draw their own).</summary>
        public virtual float HeaderHeight => 128;
        /// <summary>
        /// Full-screen game screens (the run board, the battle) cover the app's resource bar: the screen gets the whole safe
        /// area, its opaque background covers the bar (they show their own top strip).
        /// </summary>
        public virtual bool CoversResourceBar => false;
        /// <summary>A kit v2 top bar the app's resource bar is built into while this screen is on top (the home); null = the app bar.</summary>
        public virtual RectTransform ResourceBarHost => null;
        /// <summary>Show the back button (false for the root hub).</summary>
        public virtual bool ShowBack => true;
        /// <summary>Rebuild automatically on every state change (false for screens with text input).</summary>
        public virtual bool AutoRebuild => true;
        /// <summary>
        /// Rebuild whenever the screen comes back on top (back from a screen above). Default false: it rebuilds only when the
        /// state changed while it was hidden (<see cref="IsStale"/>).
        /// </summary>
        public virtual bool RebuildOnReturn => false;
        /// <summary>The state changed (another version or player) since this screen was last built.</summary>
        public bool IsStale
        {
            get
            {
                var st = App?.Session.State;
                return st == null ? _builtVersion != long.MinValue : st.Version != _builtVersion || st.PlayerId != _builtPlayer;
            }
        }
        /// <summary>Full rebuilds of this screen so far (tests: back without a state change does not rebuild).</summary>
        public int BuildCount { get; private set; }
        /// <summary>Height of a fixed bottom action bar outside the scroll (<see cref="Footer"/>); 0 = none.</summary>
        public virtual float FooterHeight => 0;
        /// <summary>
        /// A game event this screen shows itself (G1: a card's star fills, a part flies into its slot, a lab node pops, the
        /// board's floor title); true = handled, else the app's default feedback runs (or nothing).
        /// </summary>
        public virtual bool OnFeedback(RogueStars.Rules.Core.GameEvent e) => false;
        /// <summary>The fixed bottom bar (cleared before every <see cref="Build"/>); null without <see cref="FooterHeight"/>.</summary>
        protected RectTransform Footer { get; private set; }
        /// <summary>The scroll viewport of the body (tests: layout checks).</summary>
        public RectTransform BodyFrame { get; private set; }

        protected GameSession Session => App.Session;
        protected GameState State => App.Session.State;
        protected ContentDb Content => App.Session.Content;
        protected long Now => App.Session.NowMs;

        /// <summary>Second header line under the title (progress, context); null = none.</summary>
        public virtual string Subtitle => null;
        /// <summary>Caption of the header's back button ("&lt;"; the run screen shows a pause sign).</summary>
        protected virtual string BackLabel => "<";
        /// <summary>The header's back button (default: pop this screen).</summary>
        protected virtual void OnBack() => App.Router.Pop();
        /// <summary>Android back on this screen: true = handled here (the router does not pop).</summary>
        public virtual bool HandleSystemBack() => false;

        internal void Create(ClientApp app, RectTransform parent)
        {
            App = app;
            // a full-screen game screen reaches up over the resource bar and covers it with an opaque background
            Root = Ui.Stretch(Ui.Rect(parent, "Screen:" + GetType().Name), 0, CoversResourceBar ? -ClientApp.TopBarHeight : 0, 0, 0);
            if (CoversResourceBar) Root.gameObject.AddComponent<Image>().color = Theme.Background; // nothing of the bar shows through
            else if (HeaderHeight > 0) Kit.Backdrop(Root, "backdrop_menu", Theme.Hex(0x33202A), Theme.Hex(0x120A0D)); // kit backdrop slot, gradient until art exists
            float headerHeight = HeaderHeight;
            if (headerHeight > 0 && KitTopBar)
            {
                // a kit v2 top bar: a plain strip the screen fills itself (no back button or title)
                var bar = Ui.Rect(Root, "Header");
                bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1); bar.pivot = new Vector2(0.5f, 1);
                bar.sizeDelta = new Vector2(0, headerHeight); bar.anchoredPosition = Vector2.zero;
                _kitHeader = bar;
                var img = bar.gameObject.AddComponent<Image>();
                img.color = Color.clear; img.raycastTarget = true; // taps on the bar never reach the body under it
                // the board's top bar: a 1080-wide design of HeaderHeight board px, scaled to the width (KitDesignFit)
                KitTopDesign = Ui.Rect(bar, "TopBar");
                KitTopDesign.anchorMin = KitTopDesign.anchorMax = KitTopDesign.pivot = new Vector2(0, 1);
                _headerFit = bar.gameObject.AddComponent<KitDesignFit>();
                _headerFit.Target = KitTopDesign; _headerFit.DesignHeight = headerHeight;
                _headerFit.Scaled = _ => SyncKitChrome();
                BuildHeaderExtras(bar);
            }
            else if (headerHeight > 0)
            {
                var header = Ui.Row(Root, headerHeight, 20, null, 0, "Header");
                var hl = header.GetComponent<HorizontalLayoutGroup>();
                // the "?" help button floats top-right: header extras (stamina chip ...) stay left of it (P2)
                hl.padding = new RectOffset(24, IntroFeatures != null && IntroFeatures.Length > 0 ? 24 + 96 + 12 : 24, 12, 8);
                hl.childForceExpandHeight = false;
                header.anchorMin = new Vector2(0, 1); header.anchorMax = new Vector2(1, 1); header.pivot = new Vector2(0.5f, 1);
                header.sizeDelta = new Vector2(0, headerHeight); header.anchoredPosition = Vector2.zero;
                if (ShowBack) Kit.BackButton(header, BackLabel, OnBack);
                var titles = Ui.Column(header, 0, 0, null, "Titles");
                titles.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                Ui.Size(titles, -1, -1, 1);
                _title = Kit.HeaderTitle(titles, Title, ShowBack ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
                _subtitle = Ui.Label(titles, "", Theme.FontSmall, Theme.TextDim, ShowBack ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, "Subtitle");
                _subtitle.horizontalOverflow = HorizontalWrapMode.Overflow;
                BuildHeaderExtras(header);
            }
            float bottom = 0;
            if (FooterHeight > 0)
            {
                Footer = Ui.Column(Root, FooterSpacing, 0, null, "Footer");
                UiShapes.Round(Footer.gameObject.AddComponent<Image>(), 52).color = Theme.PanelAlt;
                Footer.anchorMin = Vector2.zero; Footer.anchorMax = new Vector2(1, 0); Footer.pivot = new Vector2(0.5f, 0);
                Footer.sizeDelta = new Vector2(0, FooterHeight); Footer.anchoredPosition = Vector2.zero;
                var lg = Footer.GetComponent<VerticalLayoutGroup>();
                if (lg != null)
                {
                    lg.childControlHeight = true; lg.childForceExpandHeight = false; lg.childAlignment = TextAnchor.LowerCenter;
                    lg.padding = new RectOffset(24, 24, FooterPadTop, FooterPadBottom);
                }
                bottom = FooterHeight;
                if (ActiveMainTab != null) Footer.anchoredPosition = new Vector2(0, MainTabBarHeight);
            }
            if (ActiveMainTab != null) bottom += MainTabBarHeight;
            BodyFrame = Ui.Stretch(Ui.Rect(Root, "BodyFrame"), 0, headerHeight, 0, bottom);
            _body = Ui.Scroll(BodyFrame, out _scroll);
            if (_headerFit != null) _headerFit.Apply();
            SyncKitChrome();
            if (IntroFeatures != null && IntroFeatures.Length > 0)
            {
                // "?" top-right: replays the unlock intro of this screen's feature(s) (GDD §16)
                _help = Ui.Button(Root, "?", ShowIntro, new Color(0.12f, 0.14f, 0.32f, 0.92f), 96, 96, true, 52);
                _help.name = "HelpButton";
                var rt = (RectTransform)_help.transform;
                var le = _help.GetComponent<LayoutElement>(); if (le != null) le.ignoreLayout = true;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
                rt.sizeDelta = new Vector2(96, 96);
                rt.anchoredPosition = new Vector2(-24, -16);
            }
            Rebuild();
        }

        // ---- feature intro ("?") -------------------------------------------------------------------------------

        /// <summary>
        /// Features whose intro (FeatureDef.Intro) this screen's "?" button replays; only unlocked ones count, and the button
        /// shows once one is unlocked. Null = no "?" (GDD §16: every feature screen explains itself again on request).
        /// </summary>
        public virtual string[] IntroFeatures => null;

        private Button _help;

        /// <summary>The "?" button (null on screens without intro features).</summary>
        public Button HelpButton => _help;

        /// <summary>The unlocked intro features of this screen, in content order.</summary>
        public List<string> UnlockedIntroFeatures()
        {
            var list = new List<string>();
            if (IntroFeatures == null || App?.Session.State == null) return list;
            foreach (var f in Content.FeatureOrder)
                if (Array.IndexOf(IntroFeatures, f.Id) >= 0 && Features.IsUnlocked(State, Content, f.Id)) list.Add(f.Id);
            return list;
        }

        /// <summary>"?": one feature replays its intro at once; several open a short topic list.</summary>
        public void ShowIntro()
        {
            var ids = UnlockedIntroFeatures();
            if (ids.Count == 0) return;
            if (ids.Count == 1) { App.Router.Push(Screens.UnlockScreen.Replay(ids[0])); return; }
            UiModal modal = null;
            modal = Ui.Modal(App.ModalLayer, "How it works", body =>
            {
                foreach (var id in ids)
                {
                    string fid = id;
                    var b = Ui.Button(body, Content.Feature(fid)?.Name ?? fid, () => { modal?.Close(); App.Router.Push(Screens.UnlockScreen.Replay(fid)); },
                        Theme.Button, -1, Theme.ButtonHeight);
                    b.name = "Intro:" + fid;
                }
            }, new ModalButton("Close", null));
        }

        /// <summary>Footer (fixed bottom bar) layout: spacing between its rows and vertical padding.</summary>
        public const float FooterSpacing = 12;
        public const int FooterPadTop = 18, FooterPadBottom = 22;

        /// <summary>Scroll the body so <paramref name="target"/> is centred once this build is laid out (instead of keeping the old position).</summary>
        protected void ScrollToAfterBuild(RectTransform target) => _scrollTarget = target;

        /// <summary>Scroll the body now so <paramref name="target"/> is centred (follow a moving object, e.g. the run's worm).</summary>
        protected void CenterOn(RectTransform target)
        {
            if (_scroll == null || target == null) return;
            _scroll.verticalNormalizedPosition = CenteredPosition(target);
        }

        /// <summary>Rebuild the body from the current state (keeps the scroll position).</summary>
        public void Rebuild()
        {
            if (Root == null) return;
            BuildCount++;
            var st = App.Session.State;
            _builtVersion = st != null ? st.Version : long.MinValue;
            _builtPlayer = st?.PlayerId;
            _builtOnce = true;
            _scrollTarget = null;
            Ui.Clear(_body);
            if (Footer != null) Ui.Clear(Footer);
            _live.Clear();
            _parts.Clear();
            _cursor.Clear();
            _building = null;
            if (_title != null) { _title.text = Title; _title.font = Ui.FontFor(Title); }
            if (_subtitle != null)
            {
                string sub = App.Session.State == null ? null : Subtitle;
                _subtitle.text = sub ?? "";
                _subtitle.gameObject.SetActive(!string.IsNullOrEmpty(sub));
            }
            BuildMainTabs();
            if (App.Session.State == null) Ui.Label(_body, "Connecting…", Theme.FontLarge, Theme.TextDim, TextAnchor.MiddleCenter);
            else Build(_body);
            if (_help != null)
            {
                _help.gameObject.SetActive(UnlockedIntroFeatures().Count > 0);
                _help.transform.SetAsLastSibling();
            }
            // The body's content keeps its scroll offset across a rebuild (same RectTransform); only a scroll target needs
            // the new layout now (a forced canvas update costs ~10-20 ms on a big screen). Edit mode (tests) has no canvas
            // update loop: lay out at once there so sizes read right after a build.
            if (_scroll != null && _scrollTarget != null || !Application.isPlaying)
            {
                Canvas.ForceUpdateCanvases();
                if (_scroll != null && _scrollTarget != null) _scroll.verticalNormalizedPosition = CenteredPosition(_scrollTarget);
            }
            _scrollTarget = null;
            Tick();
        }

        /// <summary>The screen is on top again (back from a screen above): rebuild only when needed.</summary>
        internal void Returned()
        {
            if (RebuildOnReturn) Rebuild();
            else if (InPlace) Refresh(); // cheap: only what changed
            else if (IsStale) Rebuild();
            else Tick();
        }

        /// <summary>Normalized scroll position that centres <paramref name="target"/> in the viewport.</summary>
        private float CenteredPosition(RectTransform target)
        {
            var content = _scroll.content;
            var viewport = _scroll.viewport;
            float contentH = content.rect.height, viewH = viewport.rect.height;
            if (contentH <= viewH + 1) return 1;
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var mid = content.InverseTransformPoint((corners[0] + corners[2]) / 2);
            float fromTop = -mid.y; // content pivot is its top edge
            float top = Mathf.Clamp(fromTop - viewH / 2, 0, contentH - viewH);
            return 1 - top / (contentH - viewH);
        }

        /// <summary>
        /// The hub tab this screen belongs to ("worm", "city", "planet", "shop", "arena"): the screen then shows the board's nav
        /// (<see cref="KitV2.Nav"/>) along the bottom with that tab raised; null = no nav.
        /// </summary>
        protected virtual string ActiveMainTab => null;

        private RectTransform _mainTabs;
        private KitDesignFit _navFit;
        private string _mainTabsKey;

        /// <summary>The nav along the bottom (null without <see cref="ActiveMainTab"/>; tests).</summary>
        public RectTransform MainNav => _mainTabs;

        private void BuildMainTabs()
        {
            if (ActiveMainTab == null || App.Session.State == null)
            {
                if (_mainTabs != null) Ui.DestroyGo(_mainTabs.gameObject);
                _mainTabs = null; _mainTabsKey = null; _navFit = null;
                SyncKitChrome();
                return;
            }
            var entries = HubLayout.Tabs(State, Content);
            var badges = new List<string>();
            var key = new System.Text.StringBuilder(ActiveMainTab);
            foreach (var e in entries)
            {
                string badge = HubLayout.TabBadge(State, Content, Now, e.Id);
                badges.Add(badge);
                key.Append('|').Append(e.Id).Append(e.Label).Append(e.Icon).Append(badge);
            }
            if (_mainTabs != null && _mainTabsKey == key.ToString()) return; // unchanged: keep the bar
            if (_mainTabs != null) Ui.DestroyGo(_mainTabs.gameObject);
            _mainTabsKey = key.ToString();
            var tabs = new List<KitTab>();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                tabs.Add(new KitTab
                {
                    Id = entry.Id, Label = entry.Label, Badge = badges[i], Active = entry.Id == ActiveMainTab,
                    Icon = HubLayout.TabIcon(entry),
                    OnClick = () =>
                    {
                        if (entry.Id == ActiveMainTab) return;
                        App.OpenTab(entry.ScreenId);
                    },
                });
            }
            _mainTabs = KitV2.Nav(Root, tabs, out _navFit);
            _navFit.Scaled = _ => SyncKitChrome();
            SyncKitChrome();
        }

        /// <summary>Design height of the nav (board px at 1080 wide).</summary>
        protected float MainTabBarHeight => KitV2.NavDesign;

        private RectTransform _kitHeader;
        private KitDesignFit _headerFit;

        /// <summary>The 1080-wide design rect of a kit top bar (<see cref="KitTopBar"/>; board px, y down), null otherwise.</summary>
        protected RectTransform KitTopDesign { get; private set; }

        /// <summary>The kit top bar and the nav take their scaled board heights; the body frame (and the footer) sit between them.</summary>
        protected void SyncKitChrome()
        {
            if (BodyFrame == null) return;
            float top = HeaderHeight;
            if (_kitHeader != null && _headerFit != null)
            {
                top = HeaderHeight * _headerFit.Scale;
                if (!Mathf.Approximately(_kitHeader.sizeDelta.y, top)) _kitHeader.sizeDelta = new Vector2(0, top);
            }
            float nav = 0;
            if (_mainTabs != null)
            {
                nav = MainTabBarHeight * (_navFit != null ? _navFit.Scale : 1);
                if (!Mathf.Approximately(_mainTabs.sizeDelta.y, nav)) _mainTabs.sizeDelta = new Vector2(0, nav);
            }
            else if (ActiveMainTab != null && App?.Session.State == null) nav = MainTabBarHeight;
            if (Footer != null && !Mathf.Approximately(Footer.anchoredPosition.y, nav)) Footer.anchoredPosition = new Vector2(0, nav);
            float bottom = (FooterHeight > 0 ? FooterHeight : 0) + nav;
            if (!Mathf.Approximately(BodyFrame.offsetMax.y, -top) || !Mathf.Approximately(BodyFrame.offsetMin.y, bottom))
            {
                BodyFrame.offsetMax = new Vector2(BodyFrame.offsetMax.x, -top);
                BodyFrame.offsetMin = new Vector2(BodyFrame.offsetMin.x, bottom);
            }
        }

        /// <summary>
        /// The header is a plain strip of <see cref="HeaderHeight"/> that <see cref="BuildHeaderExtras"/> fills (kit v2 top bar
        /// with currencies) instead of the back button and title.
        /// </summary>
        protected virtual bool KitTopBar => false;

        /// <summary>Fill the scrolling body.</summary>
        protected abstract void Build(RectTransform body);

        protected virtual void BuildHeaderExtras(RectTransform header) { }

        public virtual void OnShow() { }
        /// <summary>Called every frame while this screen is on top (animations, auto mode).</summary>
        public virtual void Frame(float dt) { }
        public virtual void OnHide() { }
        /// <summary>Called by the app on a fixed cadence (default: Live texts).</summary>
        public virtual void Tick()
        {
            for (int i = 0; i < _live.Count; i++)
            {
                var t = _live[i].Text;
                if (t == null) continue;
                string v = _live[i].Value();
                if (t.text != v) t.text = v; // unchanged text: no mesh rebuild
            }
        }

        /// <summary>Register a text that is recomputed every tick (countdowns).</summary>
        protected Text Live(Text text, Func<string> value)
        {
            _live.Add(new LiveText { Text = text, Value = value, Part = _building });
            return text;
        }

        internal void Destroy()
        {
            if (Root != null) Ui.DestroyGo(Root.gameObject);
            Root = null;
            _live.Clear();
            _parts.Clear();
        }

        // ---- in-place updates (keyed parts) --------------------------------------------------------------------------

        /// <summary>
        /// Opt-in: on a state change (and on return) the screen is refreshed in place instead of rebuilt. <see cref="Build"/>
        /// then creates every direct child of the body and the footer through <see cref="Part"/>: Build re-runs, but a part
        /// whose key is unchanged keeps its GameObjects (and its Live texts); only parts whose key changed are rebuilt, parts
        /// no longer built are removed, the order follows the calls.
        /// </summary>
        protected virtual bool InPlace => false;

        private sealed class PartState
        {
            public (Transform, string) Id;
            public string Key;
            public Transform Parent;
            public readonly List<GameObject> Roots = new List<GameObject>();
            public readonly List<PartState> Children = new List<PartState>();
            public PartState Owner;
            public bool Seen;
        }

        private readonly Dictionary<(Transform, string), PartState> _parts = new Dictionary<(Transform, string), PartState>();
        private readonly Dictionary<Transform, int> _cursor = new Dictionary<Transform, int>();
        private PartState _building;
        private bool _refreshing;

        /// <summary>Parts built (created or re-created) so far, and in-place refreshes (tests, probe).</summary>
        public int PartBuilds { get; private set; }
        public int Refreshes { get; private set; }
        /// <summary>True while <see cref="Build"/> runs as an in-place refresh.</summary>
        protected bool Refreshing => _refreshing;

        /// <summary>A state change: refresh in place (<see cref="InPlace"/>) or rebuild.</summary>
        public void StateChanged()
        {
            if (InPlace) Refresh();
            else Rebuild();
        }

        /// <summary>
        /// Re-run <see cref="Build"/> in place: parts with an unchanged key stay as they are (see <see cref="InPlace"/>).
        /// Screens that are not in-place rebuild.
        /// </summary>
        public void Refresh()
        {
            if (Root == null) return;
            if (!InPlace || !_builtOnce || App.Session.State == null || _parts.Count == 0) { Rebuild(); return; }
            Refreshes++;
            var st = App.Session.State;
            _builtVersion = st.Version;
            _builtPlayer = st.PlayerId;
            _scrollTarget = null;
            _live.RemoveAll(l => l.Part == null); // re-registered by Build
            foreach (var p in _parts.Values) p.Seen = false;
            _cursor.Clear();
            if (_title != null && _title.text != Title) { _title.text = Title; _title.font = Ui.FontFor(Title); }
            if (_subtitle != null)
            {
                string sub = Subtitle ?? "";
                if (_subtitle.text != sub) _subtitle.text = sub;
                if (_subtitle.gameObject.activeSelf != (sub.Length > 0)) _subtitle.gameObject.SetActive(sub.Length > 0);
            }
            BuildMainTabs();
            _refreshing = true;
            try { Build(_body); }
            finally { _refreshing = false; _building = null; }
            var gone = new List<PartState>();
            foreach (var p in _parts.Values) if (!p.Seen) gone.Add(p);
            foreach (var p in gone) if (_parts.ContainsKey(p.Id)) DropPart(p);
            if (!OnlyParts(_body) || Footer != null && !OnlyParts(Footer))
            {
                Debug.LogError(GetType().Name + ": an in-place Build created body/footer content outside Part(); rebuilt instead");
                Rebuild();
                return;
            }
            if (_help != null) _help.gameObject.SetActive(UnlockedIntroFeatures().Count > 0);
            if (_scroll != null && _scrollTarget != null || !Application.isPlaying)
            {
                Canvas.ForceUpdateCanvases();
                if (_scroll != null && _scrollTarget != null) _scroll.verticalNormalizedPosition = CenteredPosition(_scrollTarget);
            }
            _scrollTarget = null;
            Tick();
        }

        /// <summary>Every child of <paramref name="t"/> belongs to a live part.</summary>
        private bool OnlyParts(Transform t)
        {
            int owned = 0;
            foreach (var p in _parts.Values)
                if (p.Parent == t) foreach (var r in p.Roots) if (r != null && r.transform.parent == t) owned++;
            return owned == t.childCount;
        }

        /// <summary>
        /// A keyed part of the screen under <paramref name="parent"/>: <paramref name="build"/> creates its GameObjects (one or
        /// more direct children of the parent). On a refresh a part with the same <paramref name="id"/> and
        /// <paramref name="key"/> is kept as it is; a new key rebuilds just this part. The key must cover everything the part
        /// shows from the state (numbers that change often can be <see cref="Live"/> texts instead). Returns the part's
        /// first GameObject (a container for child parts: parts under it are keyed on their own).
        /// </summary>
        protected RectTransform Part(Transform parent, string id, string key, Action<RectTransform> build)
        {
            var full = (parent, id);
            int at = _cursor.TryGetValue(parent, out var c) ? c : 0;
            if (_parts.TryGetValue(full, out var p))
            {
                if (p.Key == key && Alive(p))
                {
                    Keep(p);
                    foreach (var r in p.Roots) { if (r.transform.GetSiblingIndex() != at) r.transform.SetSiblingIndex(at); at++; }
                    _cursor[parent] = at;
                    return p.Roots.Count > 0 ? (RectTransform)p.Roots[0].transform : null;
                }
                DropPart(p);
            }
            p = new PartState { Id = full, Key = key, Parent = parent, Owner = _building, Seen = true };
            _parts[full] = p;
            _building?.Children.Add(p);
            int before = parent.childCount;
            var outer = _building;
            _building = p;
            try { build((RectTransform)parent); }
            finally { _building = outer; }
            for (int i = before; i < parent.childCount; i++) p.Roots.Add(parent.GetChild(i).gameObject);
            for (int i = 0; i < p.Roots.Count; i++) if (p.Roots[i].transform.GetSiblingIndex() != at + i) p.Roots[i].transform.SetSiblingIndex(at + i);
            _cursor[parent] = at + p.Roots.Count;
            PartBuilds++;
            return p.Roots.Count > 0 ? (RectTransform)p.Roots[0].transform : null;
        }

        private static bool Alive(PartState p)
        {
            foreach (var r in p.Roots) if (r == null) return false;
            return true;
        }

        private static void Keep(PartState p)
        {
            p.Seen = true;
            foreach (var c in p.Children) Keep(c);
        }

        private void DropPart(PartState p)
        {
            foreach (var c in p.Children.ToArray()) DropPart(c);
            p.Children.Clear();
            _parts.Remove(p.Id);
            p.Owner?.Children.Remove(p);
            _live.RemoveAll(l => l.Part == p);
            foreach (var r in p.Roots) if (r != null) Ui.DestroyGo(r);
            p.Roots.Clear();
        }

        /// <summary>
        /// Key of a part that shows a lot of state and changes rarely (sub-views): any command (a new state version) rebuilds
        /// it, a refresh or a NotifyChanged without one keeps it.
        /// </summary>
        protected string VersionKey(params object[] local) => K(State.Version, State.PlayerId, K(local));

        /// <summary>A currency chip (<see cref="Kit.CurrencyChip"/>) whose amount updates in place (Live), so its part's key can leave the balance out.</summary>
        protected RectTransform LiveCurrencyChip(Transform parent, string currency, Action onTap = null, string name = null, float height = 60)
        {
            var chip = Kit.CurrencyChip(parent, currency, Fmt.Num(State.Wallet.Get(currency)), onTap, name, height);
            var amount = chip.Find("Amount");
            var t = amount != null ? amount.GetComponent<Text>() : null;
            if (t != null) Live(t, () => State != null ? Fmt.Num(State.Wallet.Get(currency)) : "");
            return chip;
        }

        /// <summary>Key helper: the values joined with '|' (null as "~").</summary>
        protected static string K(params object[] values)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var v in values) { sb.Append(v ?? "~"); sb.Append('|'); }
            return sb.ToString();
        }

        // ---- helpers used by many screens ---------------------------------------------------------

        protected UiModal Modal(string title, Action<RectTransform> body, params ModalButton[] buttons) =>
            Ui.Modal(App.ModalLayer, title, body, buttons);

        protected void Run(GameCommand cmd, Action<CommandResult> then = null) => Session.Run(cmd, then);

        protected static RectTransform Card(Transform parent, Color? color = null, float spacing = 8) =>
            Ui.Card(parent, color ?? Theme.Panel, null, Theme.RadiusCard, 24, spacing);

        protected static Text Section(Transform parent, string text) =>
            Ui.Display(parent, text, Theme.FontLarge, Theme.Gold, TextAnchor.MiddleLeft, "Section");

        protected static Text Note(Transform parent, string text) =>
            Ui.Label(parent, text, Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleLeft, "Note");

        /// <summary>Countdown bar plus speed-up buttons (Diamonds, ad −15%, daily free) for any rules timer.</summary>
        protected void TimerControls(Transform parent, string timerId, string label = null)
        {
            var t = TimerSystem.Get(State, timerId);
            if (t == null) return;
            bool refreshed = false;
            var bar = Ui.Bar(parent, 0, Theme.Accent, "");
            Live(bar.Label, () =>
            {
                var cur = TimerSystem.Get(State, timerId);
                if (cur == null) return "Done";
                long left = TimerSystem.Remaining(cur, Now);
                bar.Set(cur.DurationMs > 0 ? 1f - (float)left / cur.DurationMs : 1f);
                if (left <= 0 && !refreshed) { refreshed = true; Session.Refresh(); }
                return (label ?? Fmt.Humanize(cur.Kind)) + ": " + (left > 0 ? Fmt.Duration(left) : "done");
            });
            var row = Ui.Row(parent, Theme.ButtonHeight, 10);
            var dia = Ui.Button(row, "", () => Run(new TimerSpeedUp { TimerId = timerId, Method = SpeedUpMethod.Diamonds }), Theme.Premium);
            Live(dia.GetComponentInChildren<Text>(), () =>
            {
                var cur = TimerSystem.Get(State, timerId);
                return "Finish " + Fmt.Num(cur == null ? 0 : TimerSystem.DiamondCost(Content, TimerSystem.Remaining(cur, Now))) + " DIA";
            });
            int adsLeft = TimerSystem.AdsLeft(t, Content);
            Ui.Button(row, "Ad −" + Fmt.Pct(Content.TimerTuning.AdReductionPct) + " (" + adsLeft + ")", () =>
                Session.Ads.Show("timer_speedup", ad => { if (ad.Ok) Run(new TimerSpeedUp { TimerId = timerId, Method = SpeedUpMethod.Ad }); }),
                Theme.Button, -1, Theme.ButtonHeight, adsLeft > 0);
            int free = TimerSystem.FreeSpeedUpsLeft(State, Content, Now);
            Ui.Button(row, "Free (" + free + ")", () => Run(new TimerSpeedUp { TimerId = timerId, Method = SpeedUpMethod.Free }), Theme.Good, -1, Theme.ButtonHeight, free > 0);
        }

        /// <summary>What <see cref="TimerControls"/> shows besides its live texts (part keys); "" without a timer.</summary>
        protected string TimerKey(string timerId)
        {
            var t = timerId == null ? null : TimerSystem.Get(State, timerId);
            return t == null ? "" : K(timerId, TimerSystem.AdsLeft(t, Content), TimerSystem.FreeSpeedUpsLeft(State, Content, Now));
        }

        /// <summary>One "label: value" line.</summary>
        protected static Text KeyValue(Transform parent, string key, string value, Color? valueColor = null)
        {
            var row = Ui.Row(parent, 48, 10);
            var k = Ui.Label(row, key, Theme.FontSmall, Theme.TextDim);
            Ui.Size(k, 330, -1, 0);
            return Ui.Label(row, value, Theme.FontSmall, valueColor ?? Theme.Text);
        }
    }

    /// <summary>Stack navigation for screens inside one container.</summary>
    public sealed class ScreenRouter
    {
        private readonly List<UiScreen> _stack = new List<UiScreen>();
        private readonly RectTransform _container;
        private readonly ClientApp _app;

        public ScreenRouter(ClientApp app, RectTransform container) { _app = app; _container = container; }

        public UiScreen Current => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
        public int Depth => _stack.Count;
        public IReadOnlyList<UiScreen> Stack => _stack;
        public event Action<UiScreen> Changed;

        public void Push(UiScreen screen)
        {
            var prev = Current;
            if (prev != null && prev.Root.gameObject.activeSelf) { prev.OnHide(); prev.Root.gameObject.SetActive(false); }
            _stack.Add(screen);
            screen.Create(_app, _container);
            screen.OnShow();
            UiTween.PopIn(screen.Root, 0.12f);
            Changed?.Invoke(screen);
        }

        /// <summary>Go back one screen; the root screen is never popped.</summary>
        public bool Pop()
        {
            if (_stack.Count <= 1) return false;
            var top = Current;
            top.OnHide();
            top.Destroy();
            _stack.RemoveAt(_stack.Count - 1);
            var cur = Current;
            cur.Root.gameObject.SetActive(true);
            cur.Returned();
            cur.OnShow();
            Changed?.Invoke(cur);
            return true;
        }

        /// <summary>
        /// Back to the root screen, kept alive (no new hub is built; it rebuilds only when the state changed meanwhile), then
        /// push <paramref name="screen"/> on it, if any. The main tab bar's switch.
        /// </summary>
        public void ToRoot(UiScreen screen = null)
        {
            if (_stack.Count == 0) { if (screen != null) Push(screen); return; }
            for (int i = _stack.Count - 1; i >= 1; i--)
            {
                var s = _stack[i];
                if (s.Root != null && s.Root.gameObject.activeSelf) s.OnHide();
                s.Destroy();
                _stack.RemoveAt(i);
            }
            if (screen != null) { Push(screen); return; }
            var root = Current;
            if (!root.Root.gameObject.activeSelf)
            {
                root.Root.gameObject.SetActive(true);
                root.Returned();
                root.OnShow();
            }
            Changed?.Invoke(root);
        }

        /// <summary>Clear the stack and show <paramref name="root"/>.</summary>
        public void Reset(UiScreen root)
        {
            for (int i = _stack.Count - 1; i >= 0; i--) { _stack[i].OnHide(); _stack[i].Destroy(); }
            _stack.Clear();
            if (root != null) Push(root);
        }

        public void RebuildCurrent() => Current?.Rebuild();
        public void Tick() => Current?.Tick();

        /// <summary>Replace the top screen (e.g. run → result) without returning to it.</summary>
        public void Replace(UiScreen screen)
        {
            if (_stack.Count == 0) { Push(screen); return; }
            var top = Current;
            top.OnHide();
            top.Destroy();
            _stack.RemoveAt(_stack.Count - 1);
            _stack.Add(screen);
            screen.Create(_app, _container);
            screen.OnShow();
            Changed?.Invoke(screen);
        }
    }
}
