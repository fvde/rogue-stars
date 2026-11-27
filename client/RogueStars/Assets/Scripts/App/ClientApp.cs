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
    /// modal and feedback layers), owns the session, router and feedback, and drives periodic refresh.
    /// Plain C# so tests can build it without the bootstrap MonoBehaviour.
    /// </summary>
    public sealed class ClientApp
    {
        public const float TopBarHeight = 170;

        public GameSession Session { get; } = new GameSession();
        public ScreenRouter Router { get; private set; }
        /// <summary>Last dev tool status ("Progress reset · day 1", errors): shown inside the dev panel (T58).</summary>
        public string DevStatus;

        /// <summary>A quick pop on a UI element (feedback hooks of screens).</summary>
        public void FeedbackPop(RectTransform target) => FeedbackFx.Pop(target, 0.25f, 0.35f);

        /// <summary>Animations, hurray banners, the error bubble and the reconnect strip (G1: there are no toasts).</summary>
        public GameFeedback Feedback { get; private set; }
        public RectTransform Root { get; private set; }
        public RectTransform ModalLayer { get; private set; }
        /// <summary>Tutorial coach marks (CoachMarks): above the modals and the feedback layer, never blocking input.</summary>
        public RectTransform CoachLayer { get; private set; }
        public ResourceBar Bar { get; private set; }
        /// <summary>Pressed / busy look of buttons (UiPress, on the canvas root).</summary>
        public UiPress Press { get; private set; }
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
            var events = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (events == null) events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)).GetComponent<EventSystem>();
            events.pixelDragThreshold = DragThreshold(Screen.dpi);
            return (RectTransform)go.transform;
        }

        /// <summary>
        /// Finger travel (px) before a press becomes a drag. Unity's default of 10 px is ~0.6 mm on a 420 dpi phone: a tap that
        /// wobbles that much on a button inside a scroll view (every screen body) starts a scroll and the click is dropped.
        /// 0.1 inch (~2.5 mm, about Android's touch slop) keeps taps taps; desktop / unknown dpi keeps the default.
        /// </summary>
        public static int DragThreshold(float dpi) => Mathf.Max(10, Mathf.RoundToInt(dpi * 0.1f));

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
            var feedbackLayer = Ui.Stretch(Ui.Rect(safe, "FeedbackLayer"));
            app.Feedback = new GameFeedback(app, feedbackLayer);
            app.CoachLayer = Ui.Stretch(Ui.Rect(safe, "CoachLayer"));
            // every button presses on pointer down and shows busy while its command is in flight
            app.Press = canvasRoot.gameObject.AddComponent<UiPress>();
            app.Press.InFlight = () => app.Session.InFlight;

            var platform = PlatformConfig.LoadDefault();
            app.Platform = platform;
            app.Session.ProductSet = platform.ProductSet;
            app.Session.Store = PlatformServices.CreateStore(platform);
            app.Session.PlatformAds = PlatformServices.CreateAds(platform);
            if (app.Session.PlatformAds is MaxAdGateway max) max.UserId = () => app.Session.State?.PlayerId;
            app.Session.Changed += app.OnStateChanged;
            app.Session.ResultEvents += app.Feedback.OnEvents;
            app.Session.Failed += app.Feedback.Error;
            app.Session.DevNotice += m => app.Feedback.Banner("DEV: BATTLE MISMATCH", Theme.Warn, m); // dev servers / owner only
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
            ConnectionStatus = "connecting";
            Router.Reset(new HubScreen());
            Bar.Rebuild();
            service.Connect(r =>
            {
                if (Session.Service != service) return; // superseded by a later Start
                ConnectionStatus = r.Ok ? "connected" : "error: " + r.Error;
                if (r.Ok)
                {
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
            if (cur != null && cur.AutoRebuild) cur.StateChanged(); // in-place screens update only what changed
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
            if (Router.Current != null && Router.Current.HandleSystemBack()) return true;
            return Router.Pop();
        }

        public void Update(float dt)
        {
            // The HUD clock (regen timers, the periodic state refresh) must never depend on a screen behaving: a screen that
            // throws in Frame/Tick used to abort this method before Bar.Tick and the refresh, freezing stamina at 0 for good.
            Guard(() => Feedback.Tick(dt));
            Guard(() => Session.Tick(dt));
            Guard(() => Router.Current?.Frame(dt));
            _tick += dt;
            if (_tick >= 0.5f)
            {
                _tick = 0;
                Guard(() => Router.Tick());
                Guard(() => Bar.Tick());
            }
            _sinceRefresh += dt;
            float interval = Session.IsOnline ? 30f : 5f;
            if (_sinceRefresh >= interval && Session.IsReady && Session.InFlight == 0)
            {
                _sinceRefresh = 0;
                Guard(() => Session.Service.Refresh(_ => { })); // its change event rebuilds the current screen: a throwing screen must not escape
            }
        }

        private static void Guard(Action step)
        {
            try { step(); }
            catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
        }

        /// <summary>
        /// Main tab bar: back to the hub (kept alive, not rebuilt for nothing) and open the tab's screen on it (null = the hub).
        /// </summary>
        public void OpenTab(string screenId)
        {
            if (Router.Depth == 0 || !(Router.Stack[0] is HubScreen)) Router.Reset(new HubScreen());
            var entry = screenId != null ? ScreenCatalog.Find(screenId) : null;
            Router.ToRoot(entry?.Create());
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
        /// <summary>
        /// Bring the bar up to date: when its structure is unchanged (same screen, chips, name, level, power) only the live
        /// numbers update in place; otherwise it is built anew (<see cref="Build"/>).
        /// </summary>
        public void Rebuild()
        {
            string key = StructureKey();
            if (key == _builtKey && _root.childCount > 0) { Tick(); return; }
            Build();
            _builtKey = key;
        }

        private string _builtKey;

        /// <summary>Full builds of the bar so far (tests: a balance change updates in place).</summary>
        public int Builds { get; private set; }

        /// <summary>Everything the bar shows except the live numbers (the chips' balances and regen timers).</summary>
        private string StructureKey()
        {
            var s = _app.Session;
            var sb = new System.Text.StringBuilder(ScreenKey());
            if (s.State == null) return sb.Append("|null").ToString();
            sb.Append('|').Append(s.State.Worm.Level).Append('|').Append(s.State.DisplayName ?? s.State.PlayerId).Append('|').Append(s.IsOnline ? s.Service.ModeName : "");
            sb.Append('|').Append(s.Engine != null ? Power() : 0);
            foreach (var id in ChipsFor(s.State, s.Content, ScreenKey())) sb.Append('|').Append(id);
            sb.Append('|').Append(HubLayout.VisibleCurrencies(s.State, s.Content).Count > 0);
            return sb.ToString();
        }

        /// <summary>Build the bar from scratch.</summary>
        public void Build()
        {
            Builds++;
            Ui.Clear(_root);
            _live.Clear();
            var bar = Ui.Stretch(Ui.Row(_root, -1, 12, BarBg, 0, "Bar"));
            var bl = bar.GetComponent<HorizontalLayoutGroup>();
            bl.childForceExpandHeight = false; bl.childAlignment = TextAnchor.MiddleLeft;
            bl.padding = new RectOffset(12, 12, 10, 10);
            var edge = Ui.Panel(bar, new Color32(0x14, 0x10, 0x22, 255), "Edge");
            edge.raycastTarget = false;
            edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            edge.rectTransform.anchorMin = Vector2.zero; edge.rectTransform.anchorMax = new Vector2(1, 0); edge.rectTransform.pivot = new Vector2(0.5f, 0);
            edge.rectTransform.sizeDelta = new Vector2(0, 6); edge.rectTransform.anchoredPosition = Vector2.zero;
            var s = _app.Session;
            // the profile (reference home): the worm's portrait in a gold frame, the name and the power
            var profile = Ui.Panel(bar, new Color(0, 0, 0, 0), "ProfileChip");
            Ui.Size(profile, ProfileWidth, 140, 0);
            var pb = profile.gameObject.AddComponent<Button>();
            pb.targetGraphic = profile;
            pb.onClick.AddListener(ShowProfile);
            var frame = Ui.RoundPanel(profile.transform, Screens.SliceKit.Ink, 22, "AvatarFrame");
            var frt = frame.rectTransform;
            frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0, 0.5f);
            frt.sizeDelta = new Vector2(132, 132); frt.anchoredPosition = new Vector2(0, 0);
            var gold = Ui.RoundPanel(frame.transform, new Color32(0xF2, 0xC2, 0x3C, 255), 18, "Gold");
            Ui.Stretch(gold.rectTransform, 5, 5, 5, 5);
            var avatar = Ui.RoundPanel(gold.transform, new Color32(0x3C, 0x5C, 0x9C, 255), 14, "Avatar");
            Ui.Stretch(avatar.rectTransform, 8, 8, 8, 8);
            var face = Ui.Panel(avatar.transform, Color.white, "Portrait");
            face.sprite = ArtCatalog.WormIcon(); face.preserveAspect = true; face.raycastTarget = false;
            face.enabled = face.sprite != null;
            Ui.Stretch(face.rectTransform, 4, 4, 4, 0);
            // the level badge: the star badge art (Art/Hub/Icons/profile_level), else a green pill
            var star = ArtCatalog.HubIcon("profile_level");
            var lvBg = star != null ? Ui.Panel(frame.transform, Color.white, "LevelBadge") : Ui.RoundPanel(frame.transform, new Color32(0x5C, 0xC8, 0x4C, 255), 16, "LevelBadge");
            if (star != null) { lvBg.sprite = star; lvBg.preserveAspect = true; lvBg.raycastTarget = false; }
            var lrt = lvBg.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(1, 0); lrt.pivot = new Vector2(1, 0);
            lrt.sizeDelta = star != null ? new Vector2(72, 72) : new Vector2(56, 40); lrt.anchoredPosition = star != null ? new Vector2(16, -14) : new Vector2(6, -4);
            var lv = Screens.SliceKit.Chunky(lvBg.transform, s.State != null ? s.State.Worm.Level.ToString() : "", 28, Color.white, TextAnchor.MiddleCenter, "WormLevel");
            Ui.Stretch(lv.rectTransform);
            string who = s.State != null ? (s.State.DisplayName ?? s.State.PlayerId) : "…";
            var name = Screens.SliceKit.Chunky(profile.transform, who, 38, Color.white, TextAnchor.UpperLeft, "Name");
            Ui.Stretch(name.rectTransform, 150, 14, 6, 70);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            string online = s.IsOnline ? "  ·  " + s.Service.ModeName.ToUpperInvariant() : "";
            var power = Screens.SliceKit.Chunky(profile.transform, s.State != null && s.Engine != null ? Fmt.Num(Power()) + online : "", 34, new Color32(0xFF, 0xD0, 0x48, 255), TextAnchor.LowerLeft, "Power");
            Ui.Stretch(power.rectTransform, 196, 70, 6, 16);
            var sword = Ui.Panel(profile.transform, Color.white, "PowerIcon");
            sword.sprite = World.HudArt.Icon("sword"); sword.preserveAspect = true; sword.raycastTarget = false;
            sword.rectTransform.anchorMin = sword.rectTransform.anchorMax = new Vector2(0, 0);
            sword.rectTransform.sizeDelta = new Vector2(40, 40); sword.rectTransform.anchoredPosition = new Vector2(170, 38);
            _builtFor = ScreenKey();
            if (s.State == null) return;
            // G2: up to four currency chips relevant to the screen (icon + number), zero balances hidden, then the wallet.
            var shownIds = ChipsFor(s.State, s.Content, _builtFor);
            var row = Ui.Row(bar, 140, 6, null, 0, "Currencies");
            var rl = row.GetComponent<HorizontalLayoutGroup>();
            rl.childAlignment = TextAnchor.MiddleRight; rl.childForceExpandWidth = false; rl.childForceExpandHeight = false;
            Ui.Size(row, -1, 140, 1);
            foreach (var cid in shownIds)
            {
                string id = cid;
                var cell = Ui.Column(row, 2, 0, null, "Cell:" + id);
                cell.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                Ui.Size(cell, ChipWidth, 140, 0);
                var chip = HubKit.RoundPanel(cell, new Color32(0x16, 0x12, 0x2A, 255), "ChipBg:" + id);
                Ui.Size(chip, ChipWidth, 56, 0);
                var cb = chip.gameObject.AddComponent<Button>(); // tap: what it is for and where to spend it
                cb.targetGraphic = chip;
                cb.onClick.AddListener(() => ShowCurrency(id));
                var icon = Ui.Panel(chip.transform, Color.white, "Icon");
                icon.sprite = World.SummonArt.CurrencyIcon(id); icon.preserveAspect = true; icon.raycastTarget = false;
                var irt = icon.rectTransform;
                irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0, 0.5f);
                irt.sizeDelta = new Vector2(44, 44); irt.anchoredPosition = new Vector2(2, 0);
                var t = Screens.SliceKit.Chunky(chip.transform, "", 28, Color.white, TextAnchor.MiddleCenter, "Chip:" + id);
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 16; t.resizeTextMaxSize = t.fontSize;
                Ui.Stretch(t.rectTransform, 46, 0, 6, 0);
                _live.Add(new KeyValuePair<Text, Func<string>>(t, () => ChipText(id)));
                if (s.Content.Regen(id) != null)
                {
                    // the regen timer sits under its pill (it ran into the next chip)
                    var timer = Screens.SliceKit.Chunky(cell, "", 22, new Color32(0xC9, 0xCF, 0xF5, 255), TextAnchor.UpperCenter, "Timer:" + id);
                    Ui.Size(timer, ChipWidth, 28, 0);
                    _live.Add(new KeyValuePair<Text, Func<string>>(timer, () => RegenTimer(id)));
                }
            }
            if (HubLayout.VisibleCurrencies(s.State, s.Content).Count > 0)
            {
                // the wallet: every balance and what it is for
                var wallet = Ui.Panel(row, new Color(0, 0, 0, 0), "CurrencyDetails");
                Ui.Size(wallet, 64, 64, 0);
                var wb = wallet.gameObject.AddComponent<Button>();
                wb.targetGraphic = wallet;
                wb.onClick.AddListener(ShowDetails);
                var wi = Ui.Panel(wallet.transform, Color.white, "Icon");
                wi.sprite = World.SummonArt.Icon("wallet"); wi.preserveAspect = true; wi.raycastTarget = false;
                Ui.Stretch(wi.rectTransform);
            }
            Tick();
        }

        /// <summary>The chip of a currency in the bar (fly-to target, G1), null when it is not shown on this screen.</summary>
        public RectTransform ChipOf(string currency)
        {
            if (_root == null || currency == null) return null;
            foreach (var rt in _root.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == "ChipBg:" + currency) return rt;
            return null;
        }

        /// <summary>The wallet button (fly-to target for items and currencies without a chip).</summary>
        public RectTransform Wallet
        {
            get
            {
                if (_root == null) return null;
                foreach (var rt in _root.GetComponentsInChildren<RectTransform>(true))
                    if (rt.name == "CurrencyDetails") return rt;
                foreach (var rt in _root.GetComponentsInChildren<RectTransform>(true))
                    if (rt.name == "ProfileChip") return rt;
                return null;
            }
        }

        /// <summary>Currency chips in the bar at most (G2: 3–4 per screen).</summary>
        public const int MaxChips = 4;

        /// <summary>Profile chip and currency chip widths: 12 + 400 + 6 + (4 × 112 + 4 × 6 + 64) + 12 ≤ 978.</summary>
        public const float ProfileWidth = 400, ChipWidth = 112;

        private string _builtFor;

        /// <summary>The screen the bar was built for (the router's top screen type).</summary>
        private string ScreenKey() => _app.Router?.Current?.GetType().Name ?? "";

        /// <summary>
        /// The currencies a screen's bar shows, most relevant first (G2). Stamina and Diamonds are the defaults; feature
        /// screens show what is spent there.
        /// </summary>
        public static string[] RelevantCurrencies(string screen)
        {
            switch (screen)
            {
                case "WormScreen": case "PartsScreen": case "GearScreen":
                    return new[] { Currencies.Ore, Currencies.GearCores, RogueStars.Rules.Systems.ProgressionCurrencies.PartAwakenCores, Currencies.Diamonds };
                case "CityScreen": return new[] { Currencies.Biomass, Currencies.Grain, Currencies.Ore, Currencies.Diamonds };
                case "HeroesScreen": return new[] { Currencies.HeroXp, Currencies.HeroSigils, Currencies.Diamonds };
                case "MountsScreen": return new[] { Currencies.Fuel, Currencies.MountBolts, Currencies.Diamonds };
                case "SummonEventScreen": case "SummonExchangeScreen": return new[] { Currencies.HeroTickets, Currencies.MountTickets, Currencies.Diamonds };
                case "LaboratoryScreen": return new[] { Currencies.Data, Currencies.Biomass, Currencies.Diamonds };
                case "AliensScreen": case "BreedingScreen": return new[] { Currencies.AlienTokens, Currencies.Grain, Currencies.Diamonds };
                case "ArenaScreen": return new[] { Currencies.ArenaTickets, Currencies.ArenaMedals, Currencies.Diamonds };
                case "ObservatoryScreen": return new[] { Currencies.InvasionAttempts, Currencies.Diamonds };
                case "GuildScreen": return new[] { Currencies.GuildMarks, Currencies.Diamonds };
                case "ReactorScreen": return new[] { Currencies.Fuel, Currencies.Diamonds };
                case "ShopScreen": return new[] { Currencies.AlienTokens, Currencies.BlueprintTokens, Currencies.PartTokens, Currencies.Diamonds, Currencies.Stamina };
                case "HubScreen": return new[] { Currencies.Stamina, Currencies.Diamonds, Currencies.Ore, Currencies.Biomass };
                default: return new[] { Currencies.Stamina, Currencies.Diamonds };
            }
        }

        /// <summary>
        /// The chips for a screen: its relevant currencies the player has a use for, at most <see cref="MaxChips"/>; zero
        /// balances are hidden (stamina and Diamonds always show: refills and the shop's entry).
        /// </summary>
        public static List<string> ChipsFor(GameState s, RogueStars.Rules.Content.ContentDb c, string screen)
        {
            var visible = HubLayout.VisibleCurrencies(s, c);
            var list = new List<string>();
            foreach (var id in RelevantCurrencies(screen))
            {
                if (list.Count >= MaxChips) break;
                if (!visible.Contains(id) || list.Contains(id)) continue;
                if (s.Wallet.Get(id) <= 0 && id != Currencies.Stamina && id != Currencies.Diamonds) continue;
                list.Add(id);
            }
            return list;
        }

        /// <summary>The top bar's band (reference home: a dark purple band under the portrait and the currencies).</summary>
        public static readonly Color BarBg = new Color32(0x2A, 0x22, 0x46, 255);

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
                    var sw = Ui.Panel(row, Color.white, "Swatch");
                    sw.sprite = World.SummonArt.CurrencyIcon(id); sw.preserveAspect = true;
                    Ui.Size(sw, 64, 64, 0);
                    var col = Ui.Column(row, 2, 0, null, "Names");
                    col.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                    Ui.Size(col, -1, -1, 1);
                    Ui.Label(col, info.Name, Theme.FontSmall, Theme.Text);
                    Ui.Label(col, info.Use, 20, Theme.TextDim, TextAnchor.MiddleLeft, "Use");
                    var v = Ui.Label(row, ChipText(id), Theme.FontSmall, Theme.Text, TextAnchor.MiddleRight);
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
        /// <summary>Is <paramref name="screenId"/> the screen on top (the Worm and Parts count as one gear screen)?</summary>
        private bool IsCurrentScreen(string screenId)
        {
            var cur = _app.Router.Current;
            if (cur == null) return false;
            if ((screenId == "worm" || screenId == "parts") && cur is GearScreen) return true;
            var entry = ScreenCatalog.Find(screenId);
            var made = entry?.Create?.Invoke();
            return made != null && made.GetType() == cur.GetType();
        }

        public UiModal ShowCurrency(string id)
        {
            var s = _app.Session;
            if (s.State == null) return null;
            Ui.Clear(_app.ModalLayer);
            var info = CurrencyGuide.For(s.State, s.Content, id);
            var buttons = new List<ModalButton>();
            // W6: no "Go to X" while X is the current screen (the Worm and Parts share the gear screen)
            if (info.ScreenId != null && !IsCurrentScreen(info.ScreenId))
            {
                string screen = info.ScreenId;
                buttons.Add(new ModalButton(info.GoLabel, () => _app.Open(screen), Theme.Accent));
            }
            buttons.Add(new ModalButton("Close", null));
            var modal = Ui.Modal(_app.ModalLayer, info.Name, b =>
            {
                Ui.Display(b, "You have " + ChipText(id), Theme.FontLarge, Theme.CurrencyColor(id), TextAnchor.MiddleCenter, "Balance");
                Ui.Caption(b, "What it's for");
                Ui.Label(b, info.Use, Theme.FontNormal, Theme.Text, TextAnchor.MiddleLeft, "Use");
                if (!string.IsNullOrEmpty(info.Hint)) Ui.Label(b, info.Hint, Theme.FontSmall, Theme.Good, TextAnchor.MiddleLeft, "Hint");
                if (info.ScreenId == null) Ui.Label(b, "Nothing to spend it on yet.", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleLeft, "Hint");
            }, buttons.ToArray());
            modal.Root.name = "Modal:Currency:" + id;
            return modal;
        }

        /// <summary>The chip's number: the balance, "/cap" while a regen currency is below its cap (G2: numbers only).</summary>
        public string ChipText(string id)
        {
            var s = _app.Session;
            if (s.State == null) return "";
            string text = Fmt.Num(s.State.Wallet.Get(id));
            var rule = s.Content.Regen(id);
            if (rule != null && s.State.Wallet.Regen.ContainsKey(id))
            {
                long cap = s.State.Wallet.Cap(rule);
                if (s.State.Wallet.Get(id) < cap) text += "/" + cap;
            }
            return text;
        }

        /// <summary>Time to the next regen tick ("6m 00s"), empty at the cap: shown under the pill.</summary>
        public string RegenTimer(string id)
        {
            var s = _app.Session;
            if (s.State == null) return "";
            var rule = s.Content.Regen(id);
            if (rule == null || !s.State.Wallet.Regen.TryGetValue(id, out var rs)) return "";
            long cap = s.State.Wallet.Cap(rule);
            if (s.State.Wallet.Get(id) >= cap) return "";
            long tick = Math.Max(1000, (long)(rule.TickMs * (1 - Math.Min(90, rs.TickSpeedPercent) / 100.0)));
            long next = rs.LastTickMs + tick - s.NowMs;
            return Fmt.Duration(Math.Max(0, next));
        }

        public void Tick()
        {
            if (_builtFor != null && _builtFor != ScreenKey()) { Rebuild(); return; } // another screen: its currencies
            foreach (var kv in _live)
            {
                if (kv.Key == null) continue;
                string v = kv.Value();
                if (kv.Key.text != v) kv.Key.text = v; // unchanged: no mesh rebuild
            }
        }
    }
}
