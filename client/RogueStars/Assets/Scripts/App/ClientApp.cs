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
        /// <summary>Unlock groups (first feature id) whose full-screen moment was already opened this session (no reopen loop).</summary>
        public readonly HashSet<string> SnoozedUnlocks = new HashSet<string>();

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
            // Payments on hold on a device: online no store at all; offline the dev-receipt store keeps purchases testable.
            if (Platform != null && Platform.StoreMode == PlatformConfig.StoreDisabled && !Application.isEditor)
                Session.Store = service.IsOnline ? (IStoreGateway)new DisabledStoreGateway() : new FakeStoreGateway();
            Session.Use(service);
            SnoozedUnlocks.Clear();
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

    /// <summary>Top bar: profile chip and the currencies the player has a use for (with the stamina regen timer).</summary>
    public sealed class ResourceBar
    {
        private readonly ClientApp _app;
        private readonly RectTransform _root;
        private readonly List<KeyValuePair<Text, Func<string>>> _live = new List<KeyValuePair<Text, Func<string>>>();

        public ResourceBar(ClientApp app, RectTransform root) { _app = app; _root = root; }

        /// <summary>
        /// Profile chip (worm level, name, power; opens the profile) and the currency chips the player has a use for yet
        /// (<see cref="HubLayout.VisibleCurrencies"/>: stamina always, the rest as their features unlock).
        /// </summary>
        public void Rebuild()
        {
            Ui.Clear(_root);
            _live.Clear();
            var bar = Ui.Stretch(Ui.Row(_root, -1, 12, HubKit.Bg, 12, "Bar"));
            var bl = bar.GetComponent<HorizontalLayoutGroup>();
            bl.childForceExpandHeight = false; bl.childAlignment = TextAnchor.MiddleLeft;
            bl.padding = new RectOffset(12, 12, 10, 10);
            var s = _app.Session;
            var profile = HubKit.RoundPanel(bar, HubKit.Card, "ProfileChip");
            Ui.Size(profile, ProfileWidth, 84, 0);
            var pb = profile.gameObject.AddComponent<Button>();
            pb.targetGraphic = profile;
            pb.onClick.AddListener(ShowProfile);
            var avatar = HubKit.RoundPanel(profile.transform, HubKit.Pink, "Avatar");
            var art = avatar.rectTransform;
            art.anchorMin = new Vector2(0, 0.5f); art.anchorMax = new Vector2(0, 0.5f); art.pivot = new Vector2(0, 0.5f);
            art.sizeDelta = new Vector2(68, 68); art.anchoredPosition = new Vector2(8, 0);
            var lv = Ui.Label(avatar.transform, s.State != null ? s.State.Worm.Level.ToString() : "", 28, HubKit.PinkInk, TextAnchor.MiddleCenter, "WormLevel");
            lv.fontStyle = FontStyle.Bold;
            Ui.Stretch(lv.rectTransform);
            string who = s.State != null ? (s.State.DisplayName ?? s.State.PlayerId) : "…";
            var name = Ui.Label(profile.transform, who, 26, HubKit.Ink, TextAnchor.UpperLeft, "Name");
            name.fontStyle = FontStyle.Bold;
            Ui.Stretch(name.rectTransform, 90, 10, 10, 40);
            string online = s.IsOnline ? "  ·  " + s.Service.ModeName.ToUpperInvariant() : "";
            var power = Ui.Label(profile.transform, s.State != null && s.Engine != null ? "Power " + Fmt.Num(Power()) + online : "", 22, HubKit.InkDim, TextAnchor.LowerLeft, "Power");
            Ui.Stretch(power.rectTransform, 90, 40, 10, 10);
            if (s.State == null) return;
            // Up to five currency chips (stamina first) and "All" in a 2-column block to the right of the profile.
            var visible = HubLayout.VisibleCurrencies(s.State, s.Content);
            // Two chip columns in what the profile leaves free of the 978-unit canvas (282-wide cells overflowed it and
            // clipped the right column: Diamonds, "All").
            var grid = Ui.Grid(bar, new Vector2(ChipWidth, 44), 2, 6);
            var gl = grid.GetComponent<GridLayoutGroup>();
            gl.childAlignment = TextAnchor.MiddleRight;
            Ui.Size(grid, -1, 146, 1);
            int shown = visible.Count > 1 ? Math.Min(visible.Count, MaxChips) : visible.Count;
            for (int i = 0; i < shown; i++)
            {
                string id = visible[i];
                var chip = HubKit.RoundPanel(grid, HubKit.Card, "ChipBg:" + id);
                var cb = chip.gameObject.AddComponent<Button>(); // tap: what it is for and where to spend it
                cb.targetGraphic = chip;
                cb.onClick.AddListener(() => ShowCurrency(id));
                var dot = Ui.Panel(chip.transform, Theme.CurrencyColor(id), "Dot");
                dot.sprite = HubKit.Circle;
                var drt = dot.rectTransform;
                drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0, 0.5f);
                drt.sizeDelta = new Vector2(20, 20); drt.anchoredPosition = new Vector2(12, 0);
                var t = Ui.Label(chip.transform, "", 22, HubKit.Ink, TextAnchor.MiddleLeft, "Chip:" + id);
                t.fontStyle = FontStyle.Bold;
                Ui.Stretch(t.rectTransform, 40, 0, 6, 0);
                _live.Add(new KeyValuePair<Text, Func<string>>(t, () => ChipText(id)));
            }
            if (visible.Count > 1)
                HubKit.RoundButton(grid, visible.Count > shown ? "All (" + visible.Count + ")" : "All", ShowDetails, HubKit.TabActive, HubKit.Cyan, -1, 44, 22).name = "CurrencyDetails";
            Tick();
        }

        /// <summary>Currency chips in the bar before "All".</summary>
        public const int MaxChips = 5;

        /// <summary>Profile chip and currency chip widths: 12 + 400 + 12 + (2 × 265 + 6) + 12 ≤ 978.</summary>
        public const float ProfileWidth = 400, ChipWidth = 265;

        private long _power;
        private long _powerVersion = -1;

        /// <summary>Combat power for the chip; not rebuilt during a run (every run step changes the state, power does not).</summary>
        private long Power()
        {
            var s = _app.Session;
            if (_powerVersion < 0 || s.State.Run.Active == null && _powerVersion != s.State.Version)
            {
                _power = s.Engine.BuildLoadout(s.State, s.NowMs).Power;
                _powerVersion = s.State.Version;
            }
            return _power;
        }

        /// <summary>Profile: name, worm level, power; Worm (once unlocked) and Settings.</summary>
        public void ShowProfile()
        {
            var s = _app.Session;
            if (s.State == null) return;
            var buttons = new List<ModalButton>();
            if (s.IsUnlocked(RogueStars.Rules.Systems.Features.Parts)) buttons.Add(new ModalButton("Worm", () => _app.Open("worm"), Theme.Accent));
            buttons.Add(new ModalButton("Settings", () => _app.Open("settings")));
            buttons.Add(new ModalButton("Close", null));
            Ui.Modal(_app.ModalLayer, s.State.DisplayName ?? s.State.PlayerId, b =>
            {
                Ui.Label(b, "Worm level " + s.State.Worm.Level + "  ·  Power " + Fmt.Num(s.Engine.BuildLoadout(s.State, s.NowMs).Power), Theme.FontNormal, Theme.Text, TextAnchor.MiddleCenter);
                Ui.Label(b, "Day " + (RogueStars.Rules.Systems.Requirements.AccountAgeDays(s.State, s.Content, s.NowMs) + 1) + " on the planet  ·  " +
                    (s.Service == null ? "" : s.Service.ModeName), Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter);
            }, buttons.ToArray());
        }

        /// <summary>
        /// Every currency the player has a use for (<see cref="HubLayout.VisibleCurrencies"/>, in display order): name, balance
        /// (and the regen cap where there is one) and what it is for (<see cref="CurrencyGuide"/>); a row opens its explainer.
        /// Rewards without a use never reach the wallet.
        /// </summary>
        public void ShowDetails()
        {
            var s = _app.Session;
            if (s.State == null) return;
            Ui.Modal(_app.ModalLayer, "Resources", b =>
            {
                var ids = HubLayout.VisibleCurrencies(s.State, s.Content);
                Ui.Label(b, "Tap a resource to see where to spend it.", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter, "Hint");
                // Sized to its rows (a fixed 1100-unit list was a mostly empty box for a new player).
                var list = Ui.List(b, Mathf.Min(1150, ids.Count * 118 + 28));
                foreach (var id in ids)
                {
                    var cur = id;
                    var info = CurrencyGuide.For(s.State, s.Content, id);
                    var row = Ui.Row(list, 108, 14, Theme.Panel, 14, "Currency:" + id);
                    row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false; // a round swatch, not a stretched one
                    var rb = row.gameObject.AddComponent<Button>();
                    rb.targetGraphic = row.GetComponent<Image>();
                    rb.onClick.AddListener(() => ShowCurrency(cur));
                    var sw = Ui.Panel(row, Theme.CurrencyColor(id), "Swatch");
                    sw.sprite = HubKit.Circle;
                    Ui.Size(sw, 28, 28, 0);
                    var col = Ui.Column(row, 2, 0, null, "Names");
                    col.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                    Ui.Size(col, -1, -1, 1);
                    Ui.Label(col, info.Name, Theme.FontSmall, Theme.Text);
                    Ui.Label(col, info.Use, 20, Theme.TextDim, TextAnchor.MiddleLeft, "Use");
                    var v = Ui.Label(row, ChipText(id).Replace(Fmt.CurrencyShort(id) + " ", ""), Theme.FontSmall, Theme.Text, TextAnchor.MiddleRight);
                    Ui.Size(v, 230, -1, 0);
                    var go = Ui.Label(row, "›", Theme.FontLarge, Theme.Accent, TextAnchor.MiddleCenter, "More");
                    Ui.Size(go, 30, -1, 0);
                }
            }, new ModalButton("Close", null));
        }

        /// <summary>
        /// One currency's explainer: balance, what it is for (the rules' reward-use text), what the player can do with it
        /// right now (Ore: equipped parts to level up) and a Go button to where it is spent (<see cref="CurrencyGuide"/>).
        /// </summary>
        public UiModal ShowCurrency(string id)
        {
            var s = _app.Session;
            if (s.State == null) return null;
            Ui.Clear(_app.ModalLayer);
            var info = CurrencyGuide.For(s.State, s.Content, id);
            var buttons = new List<ModalButton>();
            if (info.ScreenId != null)
            {
                string screen = info.ScreenId;
                buttons.Add(new ModalButton(info.GoLabel, () =>
                {
                    // already on the gear screen: just bring the inventory into view
                    if ((screen == "worm" || screen == "parts") && _app.Router.Current is GearScreen gear) gear.FocusInventory();
                    else _app.Open(screen);
                }, Theme.Accent));
            }
            buttons.Add(new ModalButton("Close", null));
            var modal = Ui.Modal(_app.ModalLayer, info.Name, b =>
            {
                Ui.Display(b, "You have " + ChipText(id).Replace(Fmt.CurrencyShort(id) + " ", ""), Theme.FontLarge, Theme.CurrencyColor(id), TextAnchor.MiddleCenter, "Balance");
                Ui.Caption(b, "What it's for");
                Ui.Label(b, info.Use, Theme.FontNormal, Theme.Text, TextAnchor.MiddleLeft, "Use");
                if (!string.IsNullOrEmpty(info.Hint)) Ui.Label(b, info.Hint, Theme.FontSmall, Theme.Good, TextAnchor.MiddleLeft, "Hint");
                if (info.ScreenId == null) Ui.Label(b, "Nothing to spend it on yet.", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleLeft, "Hint");
            }, buttons.ToArray());
            modal.Root.name = "Modal:Currency:" + id;
            return modal;
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
