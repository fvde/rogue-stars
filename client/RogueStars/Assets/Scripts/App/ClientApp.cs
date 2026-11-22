using System;
using System.Collections.Generic;
using RogueStars.Client.Screens;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RogueStars.Client.App
{
    /// <summary>
    /// Composition root: builds the canvas hierarchy (background, safe area, resource bar, screen container,
    /// modal and toast layers), owns the session, router and toasts, and drives periodic refresh.
    /// Plain C# so tests can build it without the bootstrap MonoBehaviour.
    /// </summary>
    public sealed class ClientApp
    {
        public const float TopBarHeight = 170;

        public GameSession Session { get; } = new GameSession();
        public ScreenRouter Router { get; private set; }
        public ToastCenter Toasts { get; private set; }
        public RectTransform Root { get; private set; }
        public RectTransform ModalLayer { get; private set; }
        public ResourceBar Bar { get; private set; }
        public ClientSettings Settings { get; private set; }
        /// <summary>Directory of the offline save (Application.persistentDataPath by default).</summary>
        public string SaveDirectory;
        /// <summary>Creates the service for the current settings (override in tests).</summary>
        public Func<ClientSettings, IGameService> ServiceFactory;
        public string ConnectionStatus { get; private set; } = "idle";

        private float _tick;
        private float _sinceRefresh;

        /// <summary>Create a portrait 1080×1920 overlay canvas (+ EventSystem) and return its root.</summary>
        public static RectTransform CreateCanvas(string name = "RogueStarsCanvas")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)) { layer = 5 };
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            return (RectTransform)go.transform;
        }

        public static ClientApp Build(RectTransform canvasRoot, ClientSettings settings, string saveDirectory = null)
        {
            var app = new ClientApp { Settings = settings ?? new ClientSettings { DeviceId = "editor", DisplayName = "Worm" } };
            app.SaveDirectory = saveDirectory ?? Application.persistentDataPath;
            var bg = Ui.Panel(canvasRoot, Theme.Background, "Background");
            Ui.Stretch(bg.rectTransform);
            var safe = Ui.Rect(canvasRoot, "SafeArea");
            Ui.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            app.Root = safe;

            var top = Ui.Rect(safe, "TopBar");
            top.anchorMin = new Vector2(0, 1); top.anchorMax = Vector2.one; top.pivot = new Vector2(0.5f, 1);
            top.sizeDelta = new Vector2(0, TopBarHeight);
            app.Bar = new ResourceBar(app, top);

            var container = Ui.Stretch(Ui.Rect(safe, "Screens"), 0, TopBarHeight, 0, 0);
            app.Router = new ScreenRouter(app, container);
            app.ModalLayer = Ui.Stretch(Ui.Rect(safe, "Modals"));
            var toastLayer = Ui.Stretch(Ui.Rect(safe, "ToastLayer"));
            app.Toasts = new ToastCenter(toastLayer);

            app.Session.Changed += app.OnStateChanged;
            app.Session.EventRaised += e => app.Toasts.Event(e, app.Session.Content);
            app.Session.ErrorRaised += m => app.Toasts.Error(m);
            app.Session.InfoRaised += m => app.Toasts.Info(m);
            return app;
        }

        public IGameService CreateService()
        {
            if (ServiceFactory != null) return ServiceFactory(Settings);
            return Settings.Online
                ? (IGameService)new RemoteGameService(Settings.ToRemoteConfig())
                : new LocalGameService(SaveDirectory, "local_" + Settings.DeviceId, Settings.DisplayName);
        }

        /// <summary>(Re)create the service for the current settings, connect and show the hub.</summary>
        public void Start(IGameService service = null)
        {
            service = service ?? CreateService();
            Session.Use(service);
            ConnectionStatus = "connecting";
            Router.Reset(new HubScreen());
            Bar.Rebuild();
            service.Connect(r =>
            {
                if (Session.Service != service) return; // superseded by a later Start
                ConnectionStatus = r.Ok ? "connected" : "error: " + r.Error;
                if (r.Ok)
                {
                    Toasts.Info(service.IsOnline ? "Connected to server" : "Offline mode");
                    OnStateChanged();
                    return;
                }
                ShowModal(Ui.Modal(ModalLayer, "Connection failed", b => Ui.Label(b, r.Error, Theme.FontNormal, Theme.TextDim, TextAnchor.MiddleCenter),
                    new ModalButton("Retry", () => Start()),
                    new ModalButton("Play offline", () => SwitchMode(false), Theme.Accent)));
            });
        }

        public void SwitchMode(bool online)
        {
            Settings.Online = online;
            Settings.Save();
            Start();
        }

        private void OnStateChanged()
        {
            Bar.Rebuild();
            var cur = Router.Current;
            if (cur != null && cur.AutoRebuild) cur.Rebuild();
        }

        public UiModal ShowModal(UiModal modal) => modal;

        /// <summary>Android back: close the top modal, else pop a screen.</summary>
        public bool HandleBack()
        {
            if (ModalLayer.childCount > 0)
            {
                Ui.DestroyGo(ModalLayer.GetChild(ModalLayer.childCount - 1).gameObject);
                return true;
            }
            return Router.Pop();
        }

        public void Update(float dt)
        {
            Toasts.Tick(dt);
            _tick += dt;
            if (_tick >= 0.5f)
            {
                _tick = 0;
                Router.Tick();
                Bar.Tick();
            }
            _sinceRefresh += dt;
            float interval = Session.IsOnline ? 30f : 5f;
            if (_sinceRefresh >= interval && Session.IsReady && Session.InFlight == 0)
            {
                _sinceRefresh = 0;
                Session.Service.Refresh(_ => { });
            }
        }

        /// <summary>Navigate to a catalog screen by id.</summary>
        public void Open(string screenId)
        {
            var entry = ScreenCatalog.Find(screenId);
            if (entry != null) Router.Push(entry.Create());
        }
    }

    /// <summary>Top bar: player, mode and the main currencies with the stamina regen timer.</summary>
    public sealed class ResourceBar
    {
        private static readonly string[] Shown =
        {
            Currencies.Biomass, Currencies.Grain, Currencies.Ore, Currencies.Diamonds,
            Currencies.Stamina, Currencies.Fuel, Currencies.GearCores, Currencies.ArenaTickets,
        };

        private readonly ClientApp _app;
        private readonly RectTransform _root;
        private readonly List<KeyValuePair<Text, Func<string>>> _live = new List<KeyValuePair<Text, Func<string>>>();

        public ResourceBar(ClientApp app, RectTransform root) { _app = app; _root = root; }

        public void Rebuild()
        {
            Ui.Clear(_root);
            _live.Clear();
            var col = Ui.Stretch(Ui.Column(_root, 6, 10, Theme.Panel, "Bar"));
            var s = _app.Session;
            var head = Ui.Row(col, 46, 10);
            string who = s.State != null ? (s.State.DisplayName ?? s.State.PlayerId) : "…";
            Ui.Label(head, who, Theme.FontNormal, Theme.Text);
            var mode = Ui.Label(head, s.Service == null ? "" : s.Service.ModeName.ToUpperInvariant(), Theme.FontSmall, s.IsOnline ? Theme.Good : Theme.Warn, TextAnchor.MiddleRight);
            Ui.Size(mode, 200);
            var grid = Ui.Grid(col, new Vector2(250, 46), 4, 8);
            if (s.State == null) return;
            foreach (var cur in Shown)
            {
                string id = cur;
                var chip = Ui.Panel(grid, Theme.PanelAlt, "Chip:" + id);
                var swatch = Ui.Panel(chip.transform, Theme.CurrencyColor(id), "Swatch");
                swatch.rectTransform.anchorMin = new Vector2(0, 0); swatch.rectTransform.anchorMax = new Vector2(0, 1);
                swatch.rectTransform.pivot = new Vector2(0, 0.5f); swatch.rectTransform.sizeDelta = new Vector2(10, 0);
                var t = Ui.Label(chip.transform, "", Theme.FontSmall, Theme.Text, TextAnchor.MiddleLeft);
                Ui.Stretch(t.rectTransform, 18, 0, 6, 0);
                _live.Add(new KeyValuePair<Text, Func<string>>(t, () => ChipText(id)));
            }
            Tick();
        }

        private string ChipText(string id)
        {
            var s = _app.Session;
            if (s.State == null) return "";
            string text = Fmt.CurrencyShort(id) + " " + Fmt.Num(s.State.Wallet.Get(id));
            var rule = s.Content.Regen(id);
            if (rule != null && s.State.Wallet.Regen.TryGetValue(id, out var rs))
            {
                long cap = s.State.Wallet.Cap(rule);
                if (s.State.Wallet.Get(id) < cap)
                {
                    long tick = Math.Max(1000, (long)(rule.TickMs * (1 - Math.Min(90, rs.TickSpeedPercent) / 100.0)));
                    long next = rs.LastTickMs + tick - s.NowMs;
                    text += "/" + cap + " " + Fmt.Duration(Math.Max(0, next));
                }
            }
            return text;
        }

        public void Tick()
        {
            foreach (var kv in _live) if (kv.Key != null) kv.Key.text = kv.Value();
        }
    }
}
