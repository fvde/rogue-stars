using System.Collections.Generic;
using RogueStars.Client.World.Anim;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Home on the owner-approved board "New · home" (UI kit v2, board px scaled to the width; tools/art/kit_layouts.json
    /// screen "home"): the navy top bar (the app's <see cref="ResourceBar"/> hosted in the kit bar: avatar in its gold frame,
    /// name, power, stamina / Diamonds / Ore pills), the season bar (level hex, progress or "Completed!", chest), the
    /// translucent side rails (left: offer, pass, live events, Growth Fund; right: quests, summon event, mail, invasions,
    /// More) with badges and timer chips, the next level's title and "Longest survived: N rolls", the worm on the home planet
    /// (art by growth stage) with the crash site and the squad, the guide card, the quick band (Portal Map, Chapter rewards,
    /// Patrol, Dark Side), the Guild edge tab, the big START (starts the next level directly, <see cref="StartNextLevel"/>)
    /// with its stamina cost, the Modes edge tab and the board nav. The sky by state gets the calm treatment
    /// (<see cref="HubBackdrop"/>). Everything appears as features unlock (HubLayout, Features); locked features are never
    /// shown. Pending unlock celebrations open the full-screen <see cref="UnlockScreen"/>. Updates in place.
    /// </summary>
    public sealed partial class HubScreen : UiScreen
    {
        public override string Title => "Planet";
        public override bool ShowBack => false;
        public override float HeaderHeight => KitV2.TopBarDesign;
        public override bool CoversResourceBar => true;
        protected override bool KitTopBar => true;
        protected override string ActiveMainTab => HubLayout.PlanetTab;

        /// <summary>The app's resource bar is built into the kit top bar (<see cref="ResourceBar"/>).</summary>
        public override RectTransform ResourceBarHost => KitTopDesign;

        /// <summary>The frame everything is placed in (frame px = board px × scale), its planet layer and the 1080-wide design.</summary>
        private RectTransform _frame, _centre, _design;
        private float _s, _h;
        /// <summary>The body frame size the layout was built for (zero: built on the fallback size, before the first layout).</summary>
        private Vector2 _builtFrame;
        private readonly WalkerSim _squadSim = new WalkerSim(5);
        /// <summary>The squad walking around the worm (C4), null without a squad.</summary>
        public HubSquad Squad { get; private set; }
        /// <summary>The worm's tap target (celebrate + hearts, 1.5 s cooldown).</summary>
        public HubCreatureTap WormTap { get; private set; }

        /// <summary>The home planet's growth stage 1–5 by the main levels cleared (planet_stage1 … 5).</summary>
        public static int PlanetStage(GameState s)
        {
            long lv = s.Counter(Requirements.MainLevelCounter);
            return lv < 5 ? 1 : lv < 20 ? 2 : lv < 50 ? 3 : lv < 100 ? 4 : 5;
        }

        /// <summary>The sky state the hub shows (ArtCatalog.SkyStates), set on every build.</summary>
        public string Sky { get; private set; }

        /// <summary>Forces the sky state (tests, screenshots); null = <see cref="SkyState"/> from the live events and the local hour.</summary>
        public static string SkyOverride;

        /// <summary>Night from 19:00 to 06:00 local time.</summary>
        public const int NightFrom = 19, NightUntil = 6;

        /// <summary>
        /// The home sky: an invasion running → "invasion", another live event → "event", else "night" (19–6 h local) or "day".
        /// </summary>
        public static string SkyState(IList<ActiveLiveEvent> live, int localHour)
        {
            if (live != null)
            {
                foreach (var e in live) if (e.Kind == LiveOpsKind.Invasion) return ArtCatalog.SkyInvasion;
                if (live.Count > 0) return ArtCatalog.SkyEvent;
            }
            return localHour >= NightFrom || localHour < NightUntil ? ArtCatalog.SkyNight : ArtCatalog.SkyDay;
        }

        private string CurrentSky()
        {
            if (SkyOverride != null) return SkyOverride;
            if (State == null) return ArtCatalog.SkyDay;
            int hour = System.DateTimeOffset.FromUnixTimeMilliseconds(Now).ToLocalTime().Hour;
            return SkyState(HubLayout.LiveEvents(State, Content, Now), hour);
        }

        // ---- board layout (px at 1080 wide; body y = board y − the 237 px top bar) ----------------------------------

        /// <summary>Season label and bar (board y 267 / 323), the rails (448), the title (496) and "Longest survived" (579).</summary>
        public const float SeasonLabelY = 30, SeasonBarY = 86, RailY = 211, TitleY = 259, LongestY = 342;
        /// <summary>Everything above the planet moves up by this while the season pass is locked (no season bar).</summary>
        public const float NoSeasonShift = 170;
        /// <summary>Distance from the body's bottom edge (the nav's top, board y 2163) of the quick band (1659) and the START row (1867).</summary>
        public const float QuickFromBottom = 504, StartFromBottom = 296, EdgeFromBottom = 270;
        /// <summary>The worm-on-planet group of the board: worm 325,629 427², planet 165,880 747² (bbox 998 tall from body y 392).</summary>
        public const float GroupHeight = 998, PlanetSize = 747, WormSize = 427, WormDrop = 251;

        /// <summary>The shortest body (board px) the layout gets: shorter frames scale the board down by their height.</summary>
        public const float MinBodyHeight = 1300;

        /// <summary>Width of a guide card (between the rails).</summary>
        public const float GuideWidth = 660;

        protected override void Build(RectTransform body)
        {
            var bl = body.GetComponent<VerticalLayoutGroup>();
            if (bl != null) { bl.padding = new RectOffset(0, 0, 0, 0); bl.spacing = 0; }
            var scroll = Root.GetComponentInChildren<ScrollRect>(true);
            if (scroll != null) scroll.vertical = false;
            App.Bar.Rebuild(); // the bar lives in this screen's top bar
            bool laidOut = BodyFrame != null && BodyFrame.rect.height > 100 && BodyFrame.rect.width > 100;
            float frameH = laidOut ? BodyFrame.rect.height : 1926;
            float frameW = laidOut ? BodyFrame.rect.width : 1080;
            // the layout is absolute in frame units: Frame rebuilds it when the frame size changes (canvas resize, safe area)
            _builtFrame = laidOut ? new Vector2(frameW, frameH) : Vector2.zero;
            // board px scale with the width; a frame shorter than MinBodyHeight board px (tablet / landscape) scales by its
            // height instead and centres the 1080-wide layout
            _s = Mathf.Min(frameW / 1080f, frameH / MinBodyHeight);
            _h = frameH / _s;
            float ox = (frameW - 1080 * _s) / 2, s = _s, dh = _h;
            Sky = CurrentSky();
            int stage = PlanetStage(State);
            bool calm = HubBackdrop.Enabled;
            // the holder (sky + the frame everything is placed in) and each block in it are keyed parts: a state change
            // rebuilds only the blocks whose content changed (in place)
            var holder = Part(body, "holder", K(frameW, frameH, Sky, stage, calm), b =>
            {
                var h = Ui.Rect(b, "HomeHolder");
                Ui.Size(h, -1, frameH);
                // the space backdrop by state (day / night / event / invasion), covering the frame at its own aspect (clipped)
                h.gameObject.AddComponent<RectMask2D>();
                var sky = Ui.Panel(h, Color.white, "Sky");
                sky.sprite = ScreenArt.Sky(Sky, stage); sky.raycastTarget = false;
                Ui.Stretch(sky.rectTransform);
                if (sky.sprite != null && sky.sprite.rect.height > 0)
                {
                    var fit = sky.gameObject.AddComponent<AspectRatioFitter>();
                    fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                    fit.aspectRatio = sky.sprite.rect.width / sky.sprite.rect.height;
                }
                // the calm treatment: graded sky + vignette and rail bands (the sky layer only)
                if (calm) { HubBackdrop.Grade(sky); HubBackdrop.Overlay(h); }
                var home = Ui.Stretch(Ui.Rect(h, "Home"));
                Ui.Stretch(Ui.Rect(home, "Centre"), ox, 0, ox, 0);
                var design = Ui.Rect(home, "Design");
                design.anchorMin = design.anchorMax = design.pivot = new Vector2(0, 1);
                design.anchoredPosition = new Vector2(ox, 0);
                design.sizeDelta = new Vector2(1080, dh);
                design.localScale = new Vector3(s, s, 1);
            });
            _frame = (RectTransform)holder.Find("Home");
            _centre = (RectTransform)_frame.Find("Centre");
            _design = (RectTransform)_frame.Find("Design");
            Part(_centre, "centre", CentreKey(), f => BuildCentre());
            Part(_design, "top", TopKey(), f => BuildTop());
            Part(_design, "rails", RailsKey(), f => BuildRails());
            Part(_design, "title", TitleKey(), f => BuildTitle());
            var quick = QuickEntries();
            var quickIds = new System.Text.StringBuilder();
            foreach (var q in quick) quickIds.Append(q.Name).Append(',');
            Part(_design, "quick", K(quickIds.ToString(), _h), f => BuildQuick(quick));
            Part(_design, "guide", GuideKey(), f => BuildGuide());
            var run = State.Run.Active;
            var nextLevel = RunSystem.NextMainLevel(State, Content);
            Part(_design, "start", K(run?.LevelId, nextLevel?.Id, nextLevel != null ? RunSystem.EntryCost(Content, nextLevel.Id, RunMode.Normal).Amount : 0, _h,
                ModesUnlocked(), Session.IsUnlocked(Features.Guilds)), f => BuildStart());
        }

        /// <summary>In place: sky, season bar, rails, title, planet, quick band, guide and START are keyed parts.</summary>
        protected override bool InPlace => true;

        private float Shift => Session.IsUnlocked(Features.SeasonPass) ? 0 : NoSeasonShift;

        private string TopKey() => !Session.IsUnlocked(Features.SeasonPass) ? "locked"
            : K(ShopSystem.SeasonLevel(State, Content), ShopSystem.SeasonXp(State, Content), ShopSystem.SeasonIndex(Content, Now));

        private string TitleKey()
        {
            var next = RunSystem.NextMainLevel(State, Content);
            return K(Shift, Session.IsUnlocked(Features.Parts), next?.Id, next != null ? RunSystem.LongestRolls(State, next.Id) : 0);
        }

        // ---- top: season bar ---------------------------------------------------------------------------------------

        private void BuildTop()
        {
            if (!Session.IsUnlocked(Features.SeasonPass)) return;
            int tier = ShopSystem.SeasonLevel(State, Content);
            long xp = ShopSystem.SeasonXp(State, Content);
            long per = System.Math.Max(1, Content.SeasonPass.XpPerLevel);
            bool done = tier >= Content.SeasonPass.Levels.Count;
            KitV2.Text(_design, "Season " + (ShopSystem.SeasonIndex(Content, Now) + 1), 340, SeasonLabelY, 400, 51, 42.7f, Color.white, TextAnchor.MiddleCenter, "SeasonLabel");
            var bar = KitV2.SeasonBar(_design, 219, SeasonBarY, done ? 1 : (xp % per) / (float)per, tier.ToString(), done ? "Completed!" : (xp % per) + " / " + per,
                KitArt.Get("icon_season_chest") ?? ScreenArt.Prop("chest_mythic"));
            KitV2.Tap(bar, () => App.Open("passes"));
        }

        // ---- the side rails ------------------------------------------------------------------------------------------

        /// <summary>Everything the rails show except their live timers.</summary>
        private string RailsKey()
        {
            var k = new System.Text.StringBuilder().Append(_h).Append('|').Append(Shift).Append('|');
            foreach (var e in HubLayout.LeftRail(State, Content, Now)) k.Append(e.Id).Append(e.Label).Append(',');
            foreach (var e in HubLayout.RightRail(State, Content, Now)) k.Append(e.Id).Append(e.Label).Append(',');
            k.Append(QuestCount()).Append('|').Append(Session.IsUnlocked(Features.Heroes) ? SummonView.HubBadge(State, Content, Now) : 0).Append('|');
            foreach (var ev in HubLayout.LiveEvents(State, Content, Now)) k.Append(ev.OccurrenceId).Append(ev.Kind).Append(ev.Name).Append(ev.EndMs).Append(',');
            k.Append(Session.IsUnlocked(Features.Invasions)).Append('|').Append(OfferTimer().Length > 0).Append('|');
            k.Append(ShopSystem.PassClaimables(State, Content, Now).Count).Append('|').Append(MailSystem.UnreadCount(State)).Append('|').Append(SummonLabel());
            return k.ToString();
        }

        /// <summary>The rail entries (left, right) in board order with their art, badges and timers.</summary>
        public (List<KitV2.RailEntry> left, List<KitV2.RailEntry> right) RailEntries()
        {
            var left = new List<KitV2.RailEntry>();
            var right = new List<KitV2.RailEntry>();
            var events = HubLayout.LiveEvents(State, Content, Now);
            var shown = new List<ActiveLiveEvent>();
            for (int i = 0; i < events.Count && i < HubLayout.MaxLiveIcons; i++) shown.Add(events[i]);
            foreach (var entry in HubLayout.LeftRail(State, Content, Now))
            {
                var e = entry;
                if (e.Id == "fund") AddEvents(left, shown, false); // live events sit above the Growth Fund
                var r = Rail(e);
                if (e.Id == "offer") { string t = OfferTimer(); if (t.Length > 0) r.Timer = OfferTimer; }
                else if (e.Id == "pass") { int n = ShopSystem.PassClaimables(State, Content, Now).Count; r.Badge = n > 0 ? n.ToString() : null; }
                left.Add(r);
            }
            if (!HubLayout.LeftRail(State, Content, Now).Exists(x => x.Id == "fund")) AddEvents(left, shown, false);
            foreach (var entry in HubLayout.RightRail(State, Content, Now))
            {
                var e = entry;
                if (e.Id == "more")
                {
                    AddEvents(right, shown, true); // invasions above "More"
                    if (Session.IsUnlocked(Features.Invasions) && (events.Count == 0 || events.Count > HubLayout.MaxLiveIcons))
                        right.Add(new KitV2.RailEntry { Name = "Side:observatory", Label = "Events", Icon = KitArt.Get("icon_invasion") ?? HudArt.Icon("skull"), OnClick = () => App.Open("observatory") });
                }
                var r = Rail(e);
                if (e.Id == "quests") r.Badge = QuestCount();
                else if (e.Id == "summon") { int n = SummonView.HubBadge(State, Content, Now); r.Badge = n > 0 ? n.ToString() : null; r.Label = SummonLabel(); }
                else if (e.Id == "mail") { int n = MailSystem.UnreadCount(State); r.Badge = n > 0 ? n.ToString() : null; }
                right.Add(r);
            }
            if (!HubLayout.RightRail(State, Content, Now).Exists(x => x.Id == "more"))
            {
                AddEvents(right, shown, true);
                if (Session.IsUnlocked(Features.Invasions) && (events.Count == 0 || events.Count > HubLayout.MaxLiveIcons))
                    right.Add(new KitV2.RailEntry { Name = "Side:observatory", Label = "Events", Icon = KitArt.Get("icon_invasion") ?? HudArt.Icon("skull"), OnClick = () => App.Open("observatory") });
            }
            return (left, right);
        }

        /// <summary>The live events of one kind as rail entries: other events on the left, invasions ("!") on the right; each with its timer.</summary>
        private void AddEvents(List<KitV2.RailEntry> rail, List<ActiveLiveEvent> events, bool invasions)
        {
            foreach (var live in events)
            {
                var ev = live;
                bool invasion = ev.Kind == LiveOpsKind.Invasion;
                if (invasion != invasions) continue;
                rail.Add(new KitV2.RailEntry
                {
                    Name = "Live:" + ev.OccurrenceId, Label = invasion ? "Invasion" : Short(ev.Name), Badge = invasion ? "!" : null,
                    Icon = invasion ? KitArt.Get("icon_invasion") ?? HudArt.Icon("skull") : KitArt.Get("icon_event") ?? ScreenArt.HubIcon("event"),
                    Timer = () => Fmt.Duration(System.Math.Max(0, ev.EndMs - Now)), OnClick = () => App.Open("observatory"),
                });
            }
        }

        private KitV2.RailEntry Rail(HubLayout.Entry e) => new KitV2.RailEntry
        {
            Name = "Side:" + e.Id, Label = e.Label, Icon = RailIcon(e.Id), OnClick = () => OpenEntry(e),
        };

        /// <summary>A rail icon: the kit's <c>icon_*</c> when present, else today's hub art (or its stand-in).</summary>
        private Sprite RailIcon(string id)
        {
            switch (id)
            {
                case "offer": return KitArt.Get("icon_starter_pack") ?? ScreenArt.HubIcon("offer");
                case "pass": return KitArt.Get("icon_pass") ?? ScreenArt.HubIcon("pass");
                case "fund": return KitArt.Get("icon_growth_fund") ?? HudArt.Icon("coin");
                case "quests": return KitArt.Get("icon_quests") ?? ScreenArt.HubIcon("quests");
                case "summon": return KitArt.Get("icon_hero_event") ?? SummonArt.Featured(SummonEventType.Hero, "");
                case "mail": return KitArt.Get("icon_mail") ?? ScreenArt.HubIcon("mail");
                default: return KitArt.Get("icon_" + id) ?? ScreenArt.HubIcon(id);
            }
        }

        /// <summary>"Hero Event" / "Mount Event" by the first summon event on now (the board: "Hero Event").</summary>
        private string SummonLabel()
        {
            if (!Session.IsUnlocked(Features.Heroes)) return "";
            var types = SummonView.Available(State, Content);
            return types.Count > 0 && types[0] == SummonEventType.Mount ? "Mount Event" : "Hero Event";
        }

        private void BuildRails()
        {
            var (left, right) = RailEntries();
            // the rails end above the quick band (scaled down when a long rail would reach it)
            float top = RailY - Shift, bottom = _h - QuickFromBottom - 24;
            Rail(left, "LeftColumn", 16, top, bottom);
            Rail(right, "RightColumn", 883, top, bottom);
        }

        private void Rail(List<KitV2.RailEntry> entries, string name, float x, float top, float bottom)
        {
            if (entries.Count == 0) return;
            var rail = KitV2.SideRail(_design, name, x, top, entries, (t, f) => Live(t, f), x > 540);
            float h = KitV2.RailHeight(entries);
            float k = Mathf.Min(1, (bottom - top) / Mathf.Max(1, h));
            if (k < 1)
            {
                rail.localScale = new Vector3(k, k, 1);
                if (x > 540) rail.anchoredPosition = new Vector2(x + KitV2.RailWidth * (1 - k), -top);
            }
        }

        /// <summary>Claimable daily/weekly quests as a badge count ("2"), empty when none.</summary>
        private string QuestCount()
        {
            int n = 0;
            foreach (var q in Content.Quests.Values) if (QuestSystem.CanClaim(State, Content, q, Now)) n++;
            return n > 0 ? n.ToString() : "";
        }

        private static string Short(string name) => string.IsNullOrEmpty(name) ? "Event" : name.Length <= 14 ? name : name.Substring(0, 13) + "…";

        private string OfferTimer()
        {
            var offer = HubLayout.LimitedOffer(State, Content, Now);
            if (offer?.Schedule != null && offer.Schedule.TryActive(Now, out _, out long end)) return Fmt.Duration(System.Math.Max(0, end - Now));
            return "";
        }

        private void OpenEntry(HubLayout.Entry e)
        {
            if (e.Id == "more") { ShowMore(); return; }
            if (e.Id == "offer") { App.Router.Push(new ShopScreen(ShopScreen.OffersTab)); return; } // the Shop's Offers section
            if (e.ScreenId != null) App.Open(e.ScreenId);
        }

        /// <summary>Every other unlocked screen (laboratory, aliens, heroes, mounts, breeding ...).</summary>
        public void ShowMore()
        {
            Modal("More", b =>
            {
                var grid = Ui.Grid(b, new Vector2(280, 130), 3, 14);
                foreach (var entry in HubLayout.MoreScreens(State, Content))
                {
                    var e = entry;
                    HubKit.RoundButton(grid, e.Title, () => { Ui.Clear(App.ModalLayer); App.Router.Push(e.Create()); }, HubKit.Card, HubKit.Ink, -1, 130).name = "More:" + e.Id;
                }
            }, new ModalButton("Close", null));
        }

        // ---- title ---------------------------------------------------------------------------------------------------

        private void BuildTitle()
        {
            float top = TitleY - Shift;
            var next = RunSystem.NextMainLevel(State, Content);
            if (!Session.IsUnlocked(Features.Parts))
                KitV2.Text(_design, "AN EMPTY PLANET", 207, top - 56, 666, 50, 40, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleCenter, "Kicker");
            if (next == null) return;
            var title = KitV2.Text(_design, PortalsScreen.LevelNum(next.Number) + ". " + next.Name, 207, top, 666, 83, 72, Color.white, TextAnchor.MiddleCenter, "LevelTitle");
            KitFit.On(title, 40);
            int longest = RunSystem.LongestRolls(State, next.Id);
            if (longest > 0)
                KitV2.Text(_design, "Longest survived: <color=#FFE14D>" + longest + "</color> rolls", 207, LongestY - Shift, 666, 51, 42.7f, Color.white, TextAnchor.MiddleCenter, "Longest");
        }

        // ---- centre: the worm on the planet ----------------------------------------------------------------------------

        /// <summary>Everything the planet block shows (planet, worm, crash site, squad, story).</summary>
        private string CentreKey()
        {
            var squad = new System.Text.StringBuilder();
            foreach (var id in AlienSystem.ActiveSquad(State, Content)) squad.Append(id).Append(',');
            var pod = CrashSiteVisible(State) ? CitySystem.LaunchPod(State) : null;
            return K(_s, _h, Shift, Session.IsUnlocked(Features.Parts), PlanetStage(State), GuideHeight(), squad.ToString(), pod?.BlueprintId);
        }

        /// <summary>Places by centre and size in frame px (board px at 1080).</summary>
        private RectTransform At(RectTransform rt, float x, float y, float w, float h) => SliceKit.Place(rt, _s, x, y, w, h);

        /// <summary>The worm-on-planet group's scale and top (body px): the board's group fitted between the title and the guide / quick band.</summary>
        public (float scale, float top) PlanetGroup()
        {
            float zoneTop = LongestY - Shift + 51, zoneBottom = _h - QuickFromBottom - GuideHeight() - 12;
            float k = Mathf.Clamp((zoneBottom - zoneTop) / GroupHeight, 0.05f, 1f); // short frames (tablets, landscape) shrink it to fit
            float top = zoneTop + Mathf.Max(0, (zoneBottom - zoneTop - GroupHeight * k) / 2);
            return (k, top);
        }

        private void BuildCentre()
        {
            bool parts = Session.IsUnlocked(Features.Parts);
            var (k, top) = PlanetGroup();
            float size = PlanetSize * k;
            float cy = top + (WormDrop + PlanetSize / 2) * k;
            var planet = Ui.Panel(_centre, Color.white, "Planet");
            planet.sprite = ScreenArt.Planet(PlanetStage(State)); planet.preserveAspect = true; planet.raycastTarget = false;
            At(planet.rectTransform, 540, cy, size, size);
            BuildCrashSite(cy, size * 0.33f); // the fireplace, the saucer and the Launch Pod (from the Aliens unlock)
            var worm = Ui.Panel(_centre, Color.white, "Worm");
            worm.sprite = ArtCatalog.Hero() ?? HudArt.AlienCritter(HubKit.Pink); worm.preserveAspect = true; worm.raycastTarget = false;
            At(worm.rectTransform, 538.5f, top + WormSize * k / 2, WormSize * k, WormSize * k);
            UiBob.On(worm.rectTransform, 6 * _s, 1.8f);
            var wormAnim = UiCreature.Attach(worm); // the worm breathes and sways (code spine)
            WormTap = HubCreatureTap.Add(worm, wormAnim, _frame, _s, 0.4f); // tap: celebrate + hearts (a body-sized target, clear of the crash-site props)
            // C4: the squad wanders around the worm (the City's walkers and social events)
            Squad = HubSquad.Build(_centre, _squadSim, AlienSystem.ActiveSquad(State, Content), Content, new Vector2(540, cy), size * 0.33f, _s);
            if (!parts)
            {
                var story = SliceKit.Chunky(_centre, "Just you, a worm with no arms. A portal hums nearby…", (int)(38 * _s), Color.white, TextAnchor.MiddleCenter, "Story");
                At(story.rectTransform, 540, cy + size / 2 + 20, 900, 60);
                story.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
        }

        // ---- quick band ------------------------------------------------------------------------------------------------

        /// <summary>
        /// The quick band (board: Portal Map, Chapter rewards, Patrol, Dark Side) as ours exist: the Portal Map opens the
        /// level / portal selection, chapter rewards the map's milestone chests, patrol the City's production, Dark Side the
        /// portals' other modes.
        /// </summary>
        private List<KitV2.QuickEntry> QuickEntries()
        {
            var list = new List<KitV2.QuickEntry>();
            if (!Session.IsUnlocked(Features.Parts)) return list;
            list.Add(new KitV2.QuickEntry { Name = "Feature:map", Label = "Portal Map", Icon = KitArt.Get("icon_portal_map") ?? ScreenArt.HubIcon("map"), OnClick = () => App.Open("portals") });
            list.Add(new KitV2.QuickEntry { Name = "Feature:chapter_rewards", Label = "Chapter", Icon = KitArt.Get("icon_chapter_rewards") ?? ScreenArt.HubIcon("chapter_rewards"), OnClick = () => App.Open("portals") });
            if (Session.IsUnlocked(Features.BiomassFarm))
                list.Add(new KitV2.QuickEntry { Name = "Feature:patrol", Label = "Patrol", Icon = KitArt.Get("icon_patrol") ?? ScreenArt.HubIcon("patrol"), OnClick = () => App.Open("city") });
            if (Session.IsUnlocked(Features.DarkSide))
                list.Add(new KitV2.QuickEntry { Name = "Feature:challenges", Label = "Dark Side", Icon = KitArt.Get("icon_dark_side") ?? HudArt.Icon("skull"), OnClick = () => App.Open("portals") });
            return list;
        }

        private void BuildQuick(List<KitV2.QuickEntry> entries)
        {
            if (entries.Count == 0) return;
            KitV2.QuickBand(_design, _h - QuickFromBottom, entries);
        }

        // ---- guide -----------------------------------------------------------------------------------------------------

        private string GuideKey()
        {
            var goal = Tutorial.Next(State, Content, Now);
            var next = Features.Next(State, Content, Now);
            return K(_h, GuideExpanded, goal?.StepId, goal?.Text, goal?.ScreenId, next != null ? TeaserText(next) : null, QuickEntries().Count);
        }

        private int GuideItems() => GuideExpanded ? GuideCount() : System.Math.Min(1, GuideCount());

        /// <summary>Height (board px) of the guide stack above the quick band (0 without cards).</summary>
        private float GuideHeight() => GuideItems() == 0 ? 0 : GuideItems() * 132;

        /// <summary>Guide cards available (goal, next unlock); only the first shows unless expanded (H3).</summary>
        private int GuideCount()
        {
            int n = 0;
            var goal = Tutorial.Next(State, Content, Now);
            if (goal != null && goal.ScreenId != "portals" && goal.ScreenId != null) n++;
            if (Features.Next(State, Content, Now) != null) n++;
            return n;
        }

        /// <summary>The guide shows all its cards (the "+N" chip was tapped; tap "–" to collapse).</summary>
        public bool GuideExpanded;

        /// <summary>The guide cards (goal with "Go", the next-unlock teaser), stacked above the quick band between the rails.</summary>
        private void BuildGuide()
        {
            int total = GuideCount();
            if (total == 0) return;
            float bottom = _h - (QuickEntries().Count > 0 ? QuickFromBottom : StartFromBottom) - 12;
            var col = Ui.Column(_design, 12, 0, null, "Guide");
            col.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            col.anchorMin = col.anchorMax = new Vector2(0, 1);
            col.pivot = new Vector2(0.5f, 0);
            col.sizeDelta = new Vector2(GuideWidth, GuideItems() * 132 - 12);
            col.anchoredPosition = new Vector2(540, -bottom);
            var vl = col.GetComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.LowerCenter; vl.childForceExpandHeight = false;
            var cards = new List<GameObject>();
            var goal = Tutorial.Next(State, Content, Now);
            if (goal != null && goal.ScreenId != "portals" && goal.ScreenId != null)
            {
                var g = HubKit.RoundPanel(col, new Color(0.11f, 0.1f, 0.2f, 0.88f), "Goal");
                Ui.Size(g, -1, 120);
                var row = Ui.Row(g.transform, -1, 16, null, 0, "Row");
                Ui.Stretch(row, 30, 12, 16, 12);
                var label = Ui.Label(row, (goal.StepId != null ? "GOAL  " : "") + goal.Text, 30, HubKit.Gold, TextAnchor.MiddleLeft, "GoalText");
                label.fontStyle = FontStyle.Bold;
                HubKit.RoundButton(row, "Go", () => App.Open(goal.ScreenId), HubKit.EventGreen, HubKit.EventGreenBg, 150, 90).name = "GoalGo";
                cards.Add(g.gameObject);
            }
            var next = Features.Next(State, Content, Now);
            if (next != null)
            {
                var teaser = HubKit.Teaser(col, null, TeaserText(next), null, new Color(0.11f, 0.1f, 0.2f, 0.88f), "Teaser"); // no names or art: what unlocks next is a surprise
                Ui.Size(teaser, -1, 120, 1);
                cards.Add(teaser.gameObject);
            }
            // one card at a time (highest priority first); the rest behind a "+N" chip that expands the stack (H3)
            for (int i = 1; i < cards.Count; i++) cards[i].SetActive(GuideExpanded);
            if (total > 1 && cards.Count > 0)
            {
                var chipBtn = Ui.Button(cards[0].transform, GuideExpanded ? "–" : "+" + (total - 1), () => { GuideExpanded = !GuideExpanded; Refresh(); },
                    HubKit.Orange, 76, 52, true, 26);
                chipBtn.name = "GuideMore";
                var crt = (RectTransform)chipBtn.transform;
                crt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1, 1);
                crt.sizeDelta = new Vector2(76, 52); crt.anchoredPosition = new Vector2(10, 22);
            }
        }

        /// <summary>"3 more levels to the next feature": the hub never names what unlocks next (a surprise).</summary>
        public static string TeaserText(NextUnlock n)
        {
            if (n.LevelsToGo <= 0) return "A new feature is unlocking";
            return n.LevelsToGo == 1 ? "1 more level to the next feature" : n.LevelsToGo + " more levels to the next feature";
        }

        // ---- START row: Guild, START, Modes --------------------------------------------------------------------------------

        private bool ModesUnlocked() => Session.IsUnlocked(Features.Overcharged) || Session.IsUnlocked(Features.Burrow) || Session.IsUnlocked(Features.DarkSide);

        /// <summary>
        /// The big yellow START (CONTINUE during a run) with the stamina cost of the next level, the Guild edge tab (once guilds
        /// unlock) and the Modes edge tab (the portal map's other modes, once one unlocks).
        /// </summary>
        private void BuildStart()
        {
            var run = State.Run.Active;
            var next = RunSystem.NextMainLevel(State, Content);
            float y = _h - StartFromBottom;
            var b = KitV2.StartButton(_design, 272, y, 533, 208, run != null ? "ContinueButton" : "PlayButton");
            var face = (RectTransform)b.Find("Face");
            if (run != null)
                KitV2.Text(face, "CONTINUE", 20, 40, 493, 101, 84, Color.white, TextAnchor.MiddleCenter, "Title");
            else
            {
                KitV2.Text(face, "START", 20, next != null ? 26 : 50, 493, 101, 101.3f, Color.white, TextAnchor.MiddleCenter, "Title");
                if (next != null)
                {
                    var cost = RunSystem.EntryCost(Content, next.Id, RunMode.Normal);
                    KitV2.Icon(face, ResourceBar.KitCurrencyIcon(cost.Currency), 203, 127, 53, 54, "Stamina");
                    KitV2.Text(face, "x" + cost.Amount, 262, 128, 120, 51, 42.7f, Color.white, TextAnchor.MiddleLeft, "PillText");
                }
            }
            KitV2.Tap(b, StartNextLevel);
            if (Session.IsUnlocked(Features.Guilds))
                KitV2.EdgeTab(_design, true, _h - EdgeFromBottom, KitArt.Get("icon_guild") ?? ScreenArt.HubIcon("guild"), "Guild", "GuildButton", () => App.Open("guild"));
            if (ModesUnlocked())
                KitV2.EdgeTab(_design, false, _h - EdgeFromBottom, KitArt.Get("icon_modes") ?? HudArt.Icon("skull"), "Modes", "ModesButton", () => App.Open("portals"));
        }

        /// <summary>
        /// START: continue the run in progress, else start the next main level directly (the usual RunStart with the on-board
        /// signature pick, <see cref="SignaturePick.StartRun"/>); not enough stamina opens the refill; no next level (all
        /// cleared) opens the portal map. The one place the hub starts a run (the campaign rework slots in here).
        /// </summary>
        public void StartNextLevel()
        {
            if (State.Run.Active != null) { App.Router.Push(new RunScreen()); return; }
            var next = RunSystem.NextMainLevel(State, Content);
            if (next == null || !RunSystem.IsLevelUnlocked(State, Content, next.Id, RunMode.Normal)) { App.Open("portals"); return; }
            var cost = RunSystem.EntryCost(Content, next.Id, RunMode.Normal);
            if (!State.Wallet.CanAfford(cost.Currency, cost.Amount))
            {
                if (cost.Currency == Currencies.Stamina) StaminaModal.Show(this, App);
                else App.Bar.ShowCurrency(cost.Currency);
                return;
            }
            SignaturePick.StartRun(App, new RunStart { PortalId = next.PortalId, LevelId = next.Id, Mode = RunMode.Normal }, r => { if (r.Ok) App.Router.Push(new RunScreen()); });
        }

        // ---- nav badges ----------------------------------------------------------------------------------------------------

        /// <summary>Badge of a bottom tab (<see cref="HubLayout.TabBadge"/>: "▲ UP", a claim count, "NEW"; null = none).</summary>
        public string TabBadge(string tabId) => HubLayout.TabBadge(State, Content, Now, tabId);

        public const string UpgradeBadge = HubLayout.UpgradeBadge;

        // ---- unlock moment ----------------------------------------------------------------------------

        /// <summary>Every frame while the hub is on top (never from a rebuild): open a pending unlock moment.</summary>
        public override void Frame(float dt)
        {
            if (FrameSizeChanged()) Rebuild();
            ShowPendingUnlock();
        }

        /// <summary>
        /// The body frame no longer has the size the layout was built for: the hub was built before the canvas took its size
        /// (first frame, a resolution or safe-area change), so the bottom block would sit below the screen edge or float above it.
        /// </summary>
        public bool FrameSizeChanged()
        {
            if (BodyFrame == null) return false;
            var r = BodyFrame.rect;
            if (r.height <= 100 || r.width <= 100) return false;
            return Mathf.Abs(r.width - _builtFrame.x) > 1 || Mathf.Abs(r.height - _builtFrame.y) > 1;
        }

        /// <summary>
        /// Opens the full-screen unlock moment for the first pending celebration group while the hub is on top and no modal
        /// is open (never during a run). Returns true when it opened one. Backing out of / skipping the moment claims it
        /// (Q7: counts as seen), so it never comes back; the "?" on the feature's screen replays the intro.
        /// </summary>
        public bool ShowPendingUnlock()
        {
            if (State == null || App.Router.Current != this || App.ModalLayer.childCount > 0) return false;
            var group = HubLayout.PendingGroup(State, Content);
            if (group == null) return false;
            App.Router.Push(new UnlockScreen(group));
            return true;
        }
    }

    /// <summary>Clean placeholder for screens whose rules workstream is not merged yet.</summary>
    public sealed class PlaceholderScreen : UiScreen
    {
        private readonly string _title;
        private readonly string _workstream;
        private readonly string _description;

        public PlaceholderScreen(string title, string workstream, string description)
        {
            _title = title; _workstream = workstream; _description = description;
        }

        public override string Title => _title;

        protected override void Build(RectTransform body)
        {
            var card = Card(body, Theme.PanelAlt);
            Ui.Label(card, _description, Theme.FontNormal);
            Note(card, "TODO(merge " + _workstream + "): this screen is wired into navigation; its rules system is still a stub on this branch.");
        }
    }
}
