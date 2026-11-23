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
        /// <summary>Store / ads / product-set configuration of this build (Resources/PlatformConfig).</summary>
        public PlatformConfig Platform { get; private set; }
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

            var platform = PlatformConfig.LoadDefault();
            app.Platform = platform;
            app.Session.ProductSet = platform.ProductSet;
            app.Session.Store = PlatformServices.CreateStore(platform);
            app.Session.PlatformAds = PlatformServices.CreateAds(platform);
            if (app.Session.PlatformAds is MaxAdGateway max) max.UserId = () => app.Session.State?.PlayerId;
            app.Session.Changed += app.OnStateChanged;
            app.Session.EventRaised += e => app.Toasts.Event(e, app.Session.Content, app.Session.State);
            app.Session.ErrorRaised += m => app.Toasts.Error(m);
            app.Session.InfoRaised += m => app.Toasts.Info(m);
            return app;
        }

        public IGameService CreateService()
        {
            if (ServiceFactory != null) return ServiceFactory(Settings);
            // Online: auth provider from the build's AuthMode (Firebase on devices, dev auth in the editor); ads mode = the
            // server's session flag.
            if (Settings.Online) return new RemoteGameService(Settings.ToRemoteConfig(), auth: AuthFactory.For(Settings.AuthMode));
            return new LocalGameService(SaveDirectory, "local_" + Settings.DeviceId, Settings.DisplayName)
            {
                AdsDisabled = Platform != null && Platform.IsAdsDisabled,
            };
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
                    if (service.IsOnline) Toasts.Info("Connected to server");
                    Session.InitPlatform();
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
            Tutorial.Check(Session);
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
            Toasts.SetBottomInset(Router.Current?.ToastBottomInset ?? ToastCenter.DefaultBottomInset);
            Toasts.Tick(dt);
            Session.Tick(dt);
            Router.Current?.Frame(dt);
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
            var head = Ui.Row(col, 46, 8);
            string who = s.State != null ? (s.State.DisplayName ?? s.State.PlayerId) : "…";
            Ui.Label(head, who, Theme.FontNormal, Theme.Text);
            if (s.State != null)
                foreach (var cur in Secondary)
                {
                    string id = cur;
                    var t = Ui.Label(head, "", 22, Theme.CurrencyColor(id), TextAnchor.MiddleRight, "Chip:" + id);
                    Ui.Size(t, 170, -1, 0);
                    _live.Add(new KeyValuePair<Text, Func<string>>(t, () => Fmt.CurrencyShort(id) + " " + Fmt.Num(_app.Session.State?.Wallet.Get(id) ?? 0)));
                }
            var mode = Ui.Label(head, s.Service == null ? "" : s.Service.ModeName.ToUpperInvariant(), 22, s.IsOnline ? Theme.Good : Theme.Warn, TextAnchor.MiddleRight);
            Ui.Size(mode, 110, -1, 0);
            Ui.Button(head, "All", ShowDetails, Theme.Button, 80, 44, s.State != null, 22).name = "CurrencyDetails";
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

        /// <summary>Endgame/meta currencies shown compactly in the bar's first row.</summary>
        public static readonly string[] Secondary = { Currencies.Data, Currencies.ArenaMedals, RogueStars.Rules.Systems.ProgressionCurrencies.PartAwakenCores };

        /// <summary>Every non-zero currency of the wallet with its full name (and the regen cap where there is one).</summary>
        public void ShowDetails()
        {
            var s = _app.Session;
            if (s.State == null) return;
            Ui.Modal(_app.ModalLayer, "Resources", b =>
            {
                var ids = new List<string>(Shown);
                foreach (var id in Secondary) ids.Add(id);
                var rest = new List<string>();
                foreach (var kv in s.State.Wallet.Balances) if (!ids.Contains(kv.Key) && kv.Value != 0) rest.Add(kv.Key);
                rest.Sort(string.CompareOrdinal);
                ids.AddRange(rest);
                var list = Ui.List(b, 1100);
                foreach (var id in ids)
                {
                    var row = Ui.Row(list, 52, 10, null, 0, "Currency:" + id);
                    var sw = Ui.Panel(row, Theme.CurrencyColor(id), "Swatch");
                    Ui.Size(sw, 12, 40, 0);
                    Ui.Label(row, Fmt.CurrencyName(id, s.Content), Theme.FontSmall, Theme.Text);
                    var v = Ui.Label(row, ChipText(id).Replace(Fmt.CurrencyShort(id) + " ", ""), Theme.FontSmall, Theme.Text, TextAnchor.MiddleRight);
                    Ui.Size(v, 320, -1, 0);
                }
            }, new ModalButton("Close", null));
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
