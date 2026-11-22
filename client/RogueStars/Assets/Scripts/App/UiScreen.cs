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
        private bool _builtOnce;

        public ClientApp App { get; private set; }
        public RectTransform Root { get; private set; }
        public bool IsCreated => Root != null;
        public abstract string Title { get; }
        /// <summary>Show the back button (false for the root hub).</summary>
        public virtual bool ShowBack => true;
        /// <summary>Rebuild automatically on every state change (false for screens with text input).</summary>
        public virtual bool AutoRebuild => true;

        protected GameSession Session => App.Session;
        protected GameState State => App.Session.State;
        protected ContentDb Content => App.Session.Content;
        protected long Now => App.Session.NowMs;

        internal void Create(ClientApp app, RectTransform parent)
        {
            App = app;
            Root = Ui.Stretch(Ui.Rect(parent, "Screen:" + GetType().Name));
            var header = Ui.Row(Root, 110, 16, Theme.Header, 16, "Header");
            header.anchorMin = new Vector2(0, 1); header.anchorMax = new Vector2(1, 1); header.pivot = new Vector2(0.5f, 1);
            header.sizeDelta = new Vector2(0, 110); header.anchoredPosition = Vector2.zero;
            if (ShowBack) Ui.Button(header, "< Back", () => app.Router.Pop(), Theme.Button, 200, 84);
            _title = Ui.Label(header, Title, Theme.FontTitle, Theme.Text, ShowBack ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, "Title");
            BuildHeaderExtras(header);
            var bodyFrame = Ui.Stretch(Ui.Rect(Root, "BodyFrame"), 0, 110, 0, 0);
            _body = Ui.Scroll(bodyFrame, out _scroll);
            Rebuild();
        }

        /// <summary>Rebuild the body from the current state (keeps the scroll position).</summary>
        public void Rebuild()
        {
            if (Root == null) return;
            float pos = _scroll != null && _builtOnce ? _scroll.verticalNormalizedPosition : 1; // new screens start at the top
            _builtOnce = true;
            Ui.Clear(_body);
            _live.Clear();
            if (_title != null) _title.text = Title;
            if (App.Session.State == null) Ui.Label(_body, "Connecting…", Theme.FontLarge, Theme.TextDim, TextAnchor.MiddleCenter);
            else Build(_body);
            if (_scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                _scroll.verticalNormalizedPosition = pos;
            }
            Tick();
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
            Ui.Column(parent, spacing, 18, color ?? Theme.Panel, "Card");

        protected static Text Section(Transform parent, string text) =>
            Ui.Label(parent, text, Theme.FontLarge, Theme.Accent, TextAnchor.MiddleLeft, "Section");

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
