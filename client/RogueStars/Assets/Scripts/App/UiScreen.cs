using System;
using System.Collections.Generic;
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
        private readonly List<KeyValuePair<Text, Func<string>>> _live = new List<KeyValuePair<Text, Func<string>>>();
        private RectTransform _body;
        private ScrollRect _scroll;
        private Text _title;
        private Text _subtitle;
        private RectTransform _scrollTarget;
        private bool _builtOnce;

        public ClientApp App { get; private set; }
        public RectTransform Root { get; private set; }
        public bool IsCreated => Root != null;
        public abstract string Title { get; }
        /// <summary>Show the back button (false for the root hub).</summary>
        public virtual bool ShowBack => true;
        /// <summary>Rebuild automatically on every state change (false for screens with text input).</summary>
        public virtual bool AutoRebuild => true;
        /// <summary>Height of a fixed bottom action bar outside the scroll (<see cref="Footer"/>); 0 = none.</summary>
        public virtual float FooterHeight => 0;
        /// <summary>
        /// Keep a toast strip free between the body and the footer: the body never extends under it, so toasts never
        /// cover the screen's content (the run board).
        /// </summary>
        public virtual bool ReserveToastStrip => false;
        /// <summary>Distance of the toast strip from the bottom of the screen: just above the footer.</summary>
        public float ToastBottomInset => FooterHeight > 0 ? FooterHeight + 4 : ToastCenter.DefaultBottomInset;
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

        internal void Create(ClientApp app, RectTransform parent)
        {
            App = app;
            Root = Ui.Stretch(Ui.Rect(parent, "Screen:" + GetType().Name));
            var header = Ui.Row(Root, HeaderHeight, 20, null, 0, "Header");
            var hl = header.GetComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(24, 24, 12, 8);
            hl.childForceExpandHeight = false;
            header.anchorMin = new Vector2(0, 1); header.anchorMax = new Vector2(1, 1); header.pivot = new Vector2(0.5f, 1);
            header.sizeDelta = new Vector2(0, HeaderHeight); header.anchoredPosition = Vector2.zero;
            if (ShowBack) Ui.Button(header, BackLabel, OnBack, Theme.Panel, 104, 104, true, Theme.FontTitle).name = "BackButton";
            var titles = Ui.Column(header, 0, 0, null, "Titles");
            titles.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Size(titles, -1, -1, 1);
            _title = Ui.Display(titles, Title, Theme.FontTitle, Theme.Text, ShowBack ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, "Title");
            _subtitle = Ui.Label(titles, "", Theme.FontSmall, Theme.TextDim, ShowBack ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, "Subtitle");
            _subtitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            BuildHeaderExtras(header);
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
                bottom = FooterHeight + (ReserveToastStrip ? ToastCenter.StripHeight + 8 : 0);
            }
            BodyFrame = Ui.Stretch(Ui.Rect(Root, "BodyFrame"), 0, HeaderHeight, 0, bottom);
            _body = Ui.Scroll(BodyFrame, out _scroll);
            Rebuild();
        }

        /// <summary>Footer (fixed bottom bar) layout: spacing between its rows and vertical padding.</summary>
        public const float FooterSpacing = 12;
        public const int FooterPadTop = 18, FooterPadBottom = 22;

        /// <summary>Height of the header (back button, title, subtitle).</summary>
        public const float HeaderHeight = 128;

        /// <summary>Scroll the body so <paramref name="target"/> is centred once this build is laid out (instead of keeping the old position).</summary>
        protected void ScrollToAfterBuild(RectTransform target) => _scrollTarget = target;

        /// <summary>Rebuild the body from the current state (keeps the scroll position).</summary>
        public void Rebuild()
        {
            if (Root == null) return;
            float pos = _scroll != null && _builtOnce ? _scroll.verticalNormalizedPosition : 1; // new screens start at the top
            _builtOnce = true;
            _scrollTarget = null;
            Ui.Clear(_body);
            if (Footer != null) Ui.Clear(Footer);
            _live.Clear();
            if (_title != null) { _title.text = Title; _title.font = Ui.FontFor(Title); }
            if (_subtitle != null)
            {
                string sub = App.Session.State == null ? null : Subtitle;
                _subtitle.text = sub ?? "";
                _subtitle.gameObject.SetActive(!string.IsNullOrEmpty(sub));
            }
            if (App.Session.State == null) Ui.Label(_body, "Connecting…", Theme.FontLarge, Theme.TextDim, TextAnchor.MiddleCenter);
            else Build(_body);
            if (_scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                if (_scrollTarget != null) pos = CenteredPosition(_scrollTarget);
                _scroll.verticalNormalizedPosition = pos;
            }
            _scrollTarget = null;
            Tick();
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
                if (_live[i].Key != null) _live[i].Key.text = _live[i].Value();
        }

        /// <summary>Register a text that is recomputed every tick (countdowns).</summary>
        protected Text Live(Text text, Func<string> value)
        {
            _live.Add(new KeyValuePair<Text, Func<string>>(text, value));
            return text;
        }

        internal void Destroy()
        {
            if (Root != null) Ui.DestroyGo(Root.gameObject);
            Root = null;
            _live.Clear();
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
            if (prev != null) { prev.OnHide(); prev.Root.gameObject.SetActive(false); }
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
            cur.Rebuild();
            cur.OnShow();
            Changed?.Invoke(cur);
            return true;
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
