using System.Collections.Generic;
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
    /// Home (reference home, with our planet as the centrepiece): the season bar on top (once the pass unlocks), feature
    /// columns left (daily goals, live events) and right (shop, offer, pass, mail, more) with badges and timers, the frontier
    /// portal, the next level's title and "Longest survived: N rolls", the home planet (art by growth stage, else drawn)
    /// with the worm on it, the feature row (map, chapter rewards, patrol, challenges as they exist), the guide (goal,
    /// next-unlock teaser), the big START button with its stamina cost and the auto-pick chip, and the bottom tab bar with
    /// the raised active tab. Everything appears as features unlock (HubLayout, Features); locked features are never shown.
    /// Pending unlock celebrations open the full-screen <see cref="UnlockScreen"/>.
    /// </summary>
    public sealed class HubScreen : UiScreen
    {
        public override string Title => "Planet";
        public override bool ShowBack => false;
        public override float HeaderHeight => 0;

        /// <summary>Toasts float above START and the tab bar.</summary>
        public override float ToastBottomInset => 640 * (BodyFrame != null && BodyFrame.rect.width > 100 ? BodyFrame.rect.width / 1080f : 1);

        private static readonly Color Panel = new Color(0.08f, 0.06f, 0.16f, 0.55f);
        private static readonly Color BadgeRed = new Color32(0xE8, 0x3A, 0x48, 255);
        private static readonly Color StartYellow = new Color32(0xF8, 0xE0, 0x6A, 255);
        private static readonly Color TabDark = new Color32(0x2A, 0x26, 0x3E, 255);
        private static readonly Color TabActive = new Color32(0x7C, 0x5C, 0xE0, 255);

        private RectTransform _frame;
        private float _s, _h;

        /// <summary>The home planet's growth stage 1–5 by the main levels cleared (planet_stage1 … 5).</summary>
        public static int PlanetStage(GameState s)
        {
            long lv = s.Counter(Requirements.MainLevelCounter);
            return lv < 5 ? 1 : lv < 20 ? 2 : lv < 50 ? 3 : lv < 100 ? 4 : 5;
        }

        protected override void Build(RectTransform body)
        {
            var bl = body.GetComponent<VerticalLayoutGroup>();
            if (bl != null) { bl.padding = new RectOffset(0, 0, 0, 0); bl.spacing = 0; }
            float frameH = BodyFrame != null && BodyFrame.rect.height > 100 ? BodyFrame.rect.height : 1750;
            float frameW = BodyFrame != null && BodyFrame.rect.width > 100 ? BodyFrame.rect.width : 1080;
            var holder = Ui.Rect(body, "HomeHolder");
            Ui.Size(holder, -1, frameH);
            _s = frameW / 1080f;
            _h = frameH / _s;
            var sky = Ui.Panel(holder, Color.white, "Sky");
            sky.sprite = ScreenArt.Sky(PlanetStage(State)); sky.raycastTarget = false;
            Ui.Stretch(sky.rectTransform);
            _frame = Ui.Rect(holder, "Home");
            Ui.Stretch(_frame);
            BuildTop();
            BuildColumns();
            BuildCentre();
            BuildBottom();
        }

        /// <summary>Places by centre and size in reference px; <paramref name="fromBottom"/>: y is the distance from the bottom edge.</summary>
        private RectTransform At(RectTransform rt, float x, float y, float w, float h, bool fromBottom = false) => SliceKit.Place(rt, _s, x, fromBottom ? _h - y : y, w, h);

        // ---- top: season bar, title ---------------------------------------------------------------------------

        private void BuildTop()
        {
            if (!Session.IsUnlocked(Features.SeasonPass)) return;
            int tier = ShopSystem.SeasonLevel(State, Content);
            long xp = ShopSystem.SeasonXp(State, Content);
            long per = System.Math.Max(1, Content.SeasonPass.XpPerLevel);
            bool done = tier >= Content.SeasonPass.Levels.Count;
            var label = SliceKit.Chunky(_frame, "Season " + (ShopSystem.SeasonIndex(Content, Now) + 1), (int)(44 * _s), Color.white, TextAnchor.MiddleCenter, "SeasonLabel");
            At(label.rectTransform, 540, 36, 500, 56);
            var bar = Ui.Button(_frame, "", () => App.Open("shop"), new Color(0, 0, 0, 0), 620 * _s, 110 * _s, true, 20);
            bar.name = "SeasonBar";
            At((RectTransform)bar.transform, 540, 122, 620, 110);
            var edge = Ui.RoundPanel(bar.transform, SliceKit.Ink, 24 * _s, "Edge");
            edge.raycastTarget = false;
            Ui.Stretch(edge.rectTransform);
            var face = Ui.RoundPanel(edge.transform, new Color32(0xF6, 0xD0, 0x4C, 255), 20 * _s, "Face");
            face.raycastTarget = false;
            Ui.Stretch(face.rectTransform, 6 * _s, 6 * _s, 6 * _s, 6 * _s);
            var track = Ui.RoundPanel(face.transform, new Color32(0xC8, 0x8E, 0x22, 255), 14 * _s, "Track");
            track.raycastTarget = false;
            Ui.Stretch(track.rectTransform, 96 * _s, 26 * _s, 150 * _s, 26 * _s);
            var fill = Ui.RoundPanel(track.transform, new Color32(0xFF, 0xF0, 0x9C, 255), 14 * _s, "Fill");
            fill.raycastTarget = false;
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(done ? 1 : Mathf.Clamp01((xp % per) / (float)per), 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var progress = SliceKit.Chunky(track.transform, done ? "Completed!" : (xp % per) + " / " + per, (int)(34 * _s), Color.white, TextAnchor.MiddleCenter, "SeasonProgress");
            Ui.Stretch(progress.rectTransform);
            var hex = Ui.Panel(face.transform, Color.white, "Tier");
            hex.sprite = HudArt.Icon("exp"); hex.preserveAspect = true; hex.raycastTarget = false;
            hex.rectTransform.anchorMin = hex.rectTransform.anchorMax = new Vector2(0, 0.5f);
            hex.rectTransform.sizeDelta = new Vector2(96 * _s, 96 * _s); hex.rectTransform.anchoredPosition = new Vector2(52 * _s, 0);
            var tt = SliceKit.Chunky(hex.transform, tier.ToString(), (int)(40 * _s), Color.white, TextAnchor.MiddleCenter, "TierText");
            Ui.Stretch(tt.rectTransform);
            var reward = Ui.Panel(face.transform, Color.white, "Reward");
            reward.sprite = ScreenArt.Prop("chest_common"); reward.preserveAspect = true; reward.raycastTarget = false;
            reward.rectTransform.anchorMin = reward.rectTransform.anchorMax = new Vector2(1, 0.5f);
            reward.rectTransform.sizeDelta = new Vector2(110 * _s, 90 * _s); reward.rectTransform.anchoredPosition = new Vector2(-75 * _s, 0);
        }

        // ---- the side columns ---------------------------------------------------------------------------------

        private void BuildColumns()
        {
            var left = new List<System.Action<float>>();
            foreach (var entry in HubLayout.LeftColumn(State, Content))
            {
                var e = entry;
                left.Add(y =>
                {
                    var b = SideIcon(85, y, e.Id, e.Label, () => OpenEntry(e), "Side:" + e.Id, out var badge, out _);
                    if (e.Id == "quests") badge.text = QuestCount();
                });
            }
            var events = HubLayout.LiveEvents(State, Content, Now);
            for (int i = 0; i < events.Count && i < HubLayout.MaxLiveIcons; i++)
            {
                var ev = events[i];
                bool invasion = ev.Kind == LiveOpsKind.Invasion;
                left.Add(y =>
                {
                    SideIcon(85, y, invasion ? "invasion" : "event", Short(ev.Name), () => App.Open("observatory"), "Live:" + ev.OccurrenceId, out var badge, out var timer);
                    badge.text = invasion ? "!" : "";
                    Live(timer, () => Fmt.Duration(System.Math.Max(0, ev.EndMs - Now)));
                });
            }
            if (Session.IsUnlocked(Features.Invasions) && (events.Count == 0 || events.Count > HubLayout.MaxLiveIcons))
                left.Add(y => SideIcon(85, y, "observatory", "Events", () => App.Open("observatory"), "Side:observatory", out _, out _));
            var right = new List<System.Action<float>>();
            foreach (var entry in HubLayout.RightColumn(State, Content, Now))
            {
                var e = entry;
                right.Add(y =>
                {
                    SideIcon(995, y, e.Id, e.Label, () => OpenEntry(e), "Side:" + e.Id, out var badge, out var timer);
                    if (e.Id == "mail") { int unread = MailSystem.UnreadCount(State); badge.text = unread > 0 ? unread.ToString() : ""; }
                    else if (e.Id == "offer") Live(timer, OfferTimer);
                    else if (e.Id == "pass") timer.text = "Tier " + ShopSystem.SeasonLevel(State, Content);
                });
            }
            Column(left, 85, "LeftColumn");
            Column(right, 995, "RightColumn");
        }

        /// <summary>A translucent rounded column behind the icons, then the icons top to bottom (160 apart).</summary>
        private void Column(List<System.Action<float>> icons, float x, string name)
        {
            if (icons.Count == 0) return;
            float top = Session.IsUnlocked(Features.SeasonPass) ? 230 : 40, step = 168;
            var panel = Ui.RoundPanel(_frame, Panel, 30 * _s, name);
            panel.raycastTarget = false;
            At(panel.rectTransform, x, top + icons.Count * step / 2, 150, icons.Count * step + 16);
            for (int i = 0; i < icons.Count; i++) icons[i](top + step / 2 + i * step);
        }

        /// <summary>A side icon: art (HubIcon), a chunky label under it, a red badge (count / "!") and a timer pill.</summary>
        private Button SideIcon(float x, float y, string icon, string label, System.Action onClick, string name, out Text badge, out Text timer)
        {
            var b = Ui.Button(_frame, "", onClick, new Color(1, 1, 1, 0.001f), 150 * _s, 160 * _s, true, 20);
            b.name = name;
            At((RectTransform)b.transform, x, y, 150, 160);
            var img = Ui.Panel(b.transform, Color.white, "Icon");
            img.sprite = ScreenArt.HubIcon(icon); img.preserveAspect = true; img.raycastTarget = false;
            Ui.Stretch(img.rectTransform, 22 * _s, 4 * _s, 22 * _s, 50 * _s);
            var l = SliceKit.Chunky(b.transform, label, (int)(32 * _s), Color.white, TextAnchor.LowerCenter, "Label");
            Ui.Stretch(l.rectTransform, -10 * _s, 0, -10 * _s, 6 * _s);
            var bd = Ui.Panel(b.transform, BadgeRed, "Badge");
            bd.sprite = UiShapes.Circle; bd.raycastTarget = false;
            bd.rectTransform.anchorMin = bd.rectTransform.anchorMax = new Vector2(0.86f, 0.9f);
            bd.rectTransform.sizeDelta = new Vector2(48 * _s, 48 * _s);
            badge = SliceKit.Chunky(bd.transform, "", (int)(28 * _s), Color.white, TextAnchor.MiddleCenter, "CountText");
            Ui.Stretch(badge.rectTransform);
            bd.gameObject.AddComponent<HideWhenEmpty>().Text = badge;
            var tp = Ui.RoundPanel(b.transform, new Color32(0x22, 0x1C, 0x3A, 235), 16 * _s, "Timer");
            tp.raycastTarget = false;
            tp.rectTransform.anchorMin = tp.rectTransform.anchorMax = new Vector2(0.5f, 0.36f);
            tp.rectTransform.sizeDelta = new Vector2(130 * _s, 36 * _s); tp.rectTransform.anchoredPosition = Vector2.zero;
            timer = SliceKit.Chunky(tp.transform, "", (int)(24 * _s), Color.white, TextAnchor.MiddleCenter, "TimerText");
            Ui.Stretch(timer.rectTransform);
            tp.gameObject.AddComponent<HideWhenEmpty>().Text = timer;
            return b;
        }

        /// <summary>Claimable daily/weekly goals as a badge count ("2"), empty when none.</summary>
        private string QuestCount()
        {
            int n = 0;
            foreach (var q in Content.Quests.Values) if (QuestSystem.CanClaim(State, Content, q, Now)) n++;
            return n > 0 ? n.ToString() : "";
        }

        private static string Short(string name) => string.IsNullOrEmpty(name) ? "Event" : name.Length <= 10 ? name : name.Substring(0, 9) + "…";

        private string OfferTimer()
        {
            var offer = HubLayout.LimitedOffer(State, Content, Now);
            if (offer?.Schedule != null && offer.Schedule.TryActive(Now, out _, out long end)) return Fmt.Duration(System.Math.Max(0, end - Now));
            return "";
        }

        private void OpenEntry(HubLayout.Entry e)
        {
            if (e.Id == "more") { ShowMore(); return; }
            if (e.ScreenId != null) App.Open(e.ScreenId);
        }

        /// <summary>Every other unlocked screen (laboratory, aliens, heroes, mounts, breeding, guild...).</summary>
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

        // ---- centre: portal, level title, longest survived, the planet ---------------------------------------

        private void BuildCentre()
        {
            bool parts = Session.IsUnlocked(Features.Parts);
            float top = Session.IsUnlocked(Features.SeasonPass) ? 250 : 70;
            var next = RunSystem.NextMainLevel(State, Content);
            var portal = Content.MainPortal(PortalsScreen.FrontierPortalIndex(State, Content));
            if (portal != null && parts)
            {
                var chip = Ui.RoundPanel(_frame, new Color(0.1f, 0.06f, 0.2f, 0.6f), 26 * _s, "PortalChip");
                chip.raycastTarget = false;
                At(chip.rectTransform, 540, top, 560, 54);
                var ct = SliceKit.Chunky(chip.transform, "Portal " + (portal.Order + 1) + " · " + portal.Name, (int)(30 * _s), new Color32(0xFF, 0xD0, 0x7A, 255), TextAnchor.MiddleCenter, "Text");
                Ui.Stretch(ct.rectTransform, 12 * _s, 0, 12 * _s, 0);
            }
            if (!parts)
            {
                var k = SliceKit.Chunky(_frame, "AN EMPTY PLANET", (int)(40 * _s), new Color(1, 1, 1, 0.7f), TextAnchor.MiddleCenter, "Kicker");
                At(k.rectTransform, 540, top, 800, 60);
            }
            if (next != null)
            {
                var title = SliceKit.Chunky(_frame, PortalsScreen.LevelNum(next.Number) + ". " + next.Name, (int)(70 * _s), Color.white, TextAnchor.MiddleCenter, "LevelTitle");
                At(title.rectTransform, 540, top + 90, 760, 96);
                title.horizontalOverflow = HorizontalWrapMode.Wrap; title.resizeTextForBestFit = true; title.resizeTextMaxSize = title.fontSize; title.resizeTextMinSize = (int)(40 * _s);
                int longest = RunSystem.LongestRolls(State, next.Id);
                if (longest > 0)
                {
                    var l = SliceKit.Chunky(_frame, "Longest survived: <color=#FFD84A>" + longest + "</color> rolls", (int)(48 * _s), Color.white, TextAnchor.MiddleCenter, "Longest");
                    At(l.rectTransform, 540, top + 175, 760, 66);
                }
            }
            // the planet fills the space between the title and the bottom block
            float zoneTop = top + 220, zoneBottom = _h - BottomBlock();
            float size = Mathf.Clamp(Mathf.Min(zoneBottom - zoneTop, 760), 300, 760);
            float cy = (zoneTop + zoneBottom) / 2 + 20;
            var glow = Ui.Panel(_frame, new Color(0.5f, 0.75f, 1f, 0.35f), "PlanetGlow");
            glow.sprite = ScreenArt.Glow(); glow.raycastTarget = false;
            At(glow.rectTransform, 540, cy, size * 1.35f, size * 1.35f);
            var planet = Ui.Panel(_frame, Color.white, "Planet");
            planet.sprite = ScreenArt.Planet(PlanetStage(State)); planet.preserveAspect = true; planet.raycastTarget = false;
            At(planet.rectTransform, 540, cy, size, size);
            var worm = Ui.Panel(_frame, Color.white, "Worm");
            worm.sprite = ArtCatalog.Hero() ?? HudArt.AlienCritter(HubKit.Pink); worm.preserveAspect = true; worm.raycastTarget = false;
            At(worm.rectTransform, 540, cy - size * 0.36f - size * 0.12f, size * 0.34f, size * 0.34f);
            UiBob.On(worm.rectTransform, 6 * _s, 1.8f);
            if (!parts)
            {
                var story = SliceKit.Chunky(_frame, "Just you, a worm with no arms. A portal hums nearby…", (int)(38 * _s), Color.white, TextAnchor.MiddleCenter, "Story");
                At(story.rectTransform, 540, cy + size / 2 + 20, 900, 60);
                story.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
        }

        /// <summary>Height (reference px from the bottom) of the tabs, START, the feature row and the guide.</summary>
        private float BottomBlock()
        {
            float h = 210 + 250;
            if (FeatureRow().Count > 0) h += 200;
            h += GuideItems() * 132;
            return h + 20;
        }

        // ---- bottom: guide, feature row, START, tabs ------------------------------------------------------------

        private struct Feature { public string Id, Label, Icon; public System.Action Open; }

        /// <summary>The feature row as ours exist: the map (portals), chapter rewards (milestone chests on the map), patrol (City production) and challenges (Dark Side).</summary>
        private List<Feature> FeatureRow()
        {
            var list = new List<Feature>();
            if (!Session.IsUnlocked(Features.Parts)) return list;
            list.Add(new Feature { Id = "map", Label = "Portal\nMap", Icon = "map", Open = () => App.Open("portals") });
            list.Add(new Feature { Id = "chapter_rewards", Label = "Chapter\nRewards", Icon = "chapter_rewards", Open = () => App.Open("portals") });
            if (Session.IsUnlocked(Features.BiomassFarm)) list.Add(new Feature { Id = "patrol", Label = "Farm\nPatrol", Icon = "patrol", Open = () => App.Open("city") });
            if (Session.IsUnlocked(Features.DarkSide)) list.Add(new Feature { Id = "challenges", Label = "Dark\nSide", Icon = "challenges", Open = () => App.Open("portals") });
            return list;
        }

        private int GuideItems()
        {
            int n = 0;
            var snoozed = HubLayout.PendingGroup(State, Content);
            if (snoozed != null && App.SnoozedUnlocks.Contains(snoozed[0])) n++;
            var goal = Tutorial.Next(State, Content, Now);
            if (goal != null && goal.ScreenId != "portals" && goal.ScreenId != null) n++;
            if (Features.Next(State, Content, Now) != null) n++;
            return n;
        }

        private void BuildBottom()
        {
            // tabs (bottom), START row above them, the feature row, the guide on top
            BuildTabs();
            BuildStart();
            var features = FeatureRow();
            float y = 210 + 250;
            if (features.Count > 0)
            {
                var panel = Ui.RoundPanel(_frame, Panel, 40 * _s, "FeatureRow");
                panel.raycastTarget = false;
                float w = features.Count * 200 + 40;
                At(panel.rectTransform, 540, y + 95, w, 170, true);
                for (int i = 0; i < features.Count; i++)
                {
                    var f = features[i];
                    var b = Ui.Button(_frame, "", f.Open, new Color(1, 1, 1, 0.001f), 190 * _s, 190 * _s, true, 20);
                    b.name = "Feature:" + f.Id;
                    At((RectTransform)b.transform, 540 - (features.Count - 1) * 100 + i * 200, y + 100, 190, 190, true);
                    var img = Ui.Panel(b.transform, Color.white, "Icon");
                    img.sprite = ScreenArt.HubIcon(f.Icon); img.preserveAspect = true; img.raycastTarget = false;
                    Ui.Stretch(img.rectTransform, 36 * _s, 0, 36 * _s, 70 * _s);
                    var l = SliceKit.Chunky(b.transform, f.Label, (int)(34 * _s), Color.white, TextAnchor.LowerCenter, "Label");
                    Ui.Stretch(l.rectTransform, -10 * _s, 0, -10 * _s, 8 * _s);
                    l.lineSpacing = 0.85f;
                }
                y += 200;
            }
            BuildGuide(y);
        }

        private void BuildGuide(float fromBottom)
        {
            var col = Ui.Column(_frame, 12, 0, null, "Guide");
            col.anchorMin = col.anchorMax = new Vector2(0.5f, 0);
            col.pivot = new Vector2(0.5f, 0);
            col.sizeDelta = new Vector2(940, GuideItems() * 132);
            col.localScale = Vector3.one * _s;
            col.anchoredPosition = new Vector2(0, fromBottom * _s);
            var vl = col.GetComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.LowerCenter; vl.childForceExpandHeight = false;
            var snoozed = HubLayout.PendingGroup(State, Content);
            if (snoozed != null && App.SnoozedUnlocks.Contains(snoozed[0]))
            {
                var row = HubKit.RoundPanel(col, HubKit.TabActive, "NewUnlock");
                Ui.Size(row, -1, 120);
                var btn = row.gameObject.AddComponent<Button>();
                btn.targetGraphic = row;
                btn.onClick.AddListener(() => { App.SnoozedUnlocks.Clear(); ShowPendingUnlock(); });
                var t = Ui.Label(row.transform, "NEW: " + Content.Feature(snoozed[0])?.Name + " — tap to open", 32, HubKit.Cyan, TextAnchor.MiddleCenter, "Text");
                t.fontStyle = FontStyle.Bold;
                Ui.Stretch(t.rectTransform);
            }
            var goal = Tutorial.Next(State, Content, Now);
            if (goal != null && goal.ScreenId != "portals" && goal.ScreenId != null)
            {
                var g = HubKit.RoundPanel(col, new Color(0.11f, 0.1f, 0.26f, 0.92f), "Goal");
                Ui.Size(g, -1, 120);
                var row = Ui.Row(g.transform, -1, 16, null, 0, "Row");
                Ui.Stretch(row, 30, 12, 16, 12);
                var label = Ui.Label(row, (goal.StepId != null ? "GOAL  " : "") + goal.Text, 30, HubKit.Gold, TextAnchor.MiddleLeft, "GoalText");
                label.fontStyle = FontStyle.Bold;
                HubKit.RoundButton(row, "Go", () => App.Open(goal.ScreenId), HubKit.EventGreen, HubKit.EventGreenBg, 170, 90).name = "GoalGo";
            }
            var next = Features.Next(State, Content, Now);
            if (next != null)
            {
                var teaser = HubKit.Teaser(col, "NEXT", TeaserText(next), TeaserFigure(next), new Color(0.11f, 0.1f, 0.26f, 0.92f), "Teaser");
                Ui.Size(teaser, -1, 120, 1);
            }
        }

        /// <summary>"Laboratory — clear level 14", "City: Biomass Farm — unlocks tomorrow".</summary>
        public static string TeaserText(NextUnlock n) => n.Name + " — " + n.Text;

        /// <summary>Level progress (2/5) while the level gate is open, else the day it unlocks.</summary>
        public static string TeaserFigure(NextUnlock n)
        {
            if (!n.LevelMet && n.Level > 0) return n.CurrentLevel + "/" + n.Level;
            if (n.DaysToWait > 0) return "Day " + (n.CurrentDay + n.DaysToWait);
            return null;
        }

        /// <summary>
        /// The big yellow START (CONTINUE during a run) with the next level and its stamina cost, the auto-pick chip at its
        /// right and "Modes" (the portal map's other modes) once one unlocks.
        /// </summary>
        private void BuildStart()
        {
            var run = State.Run.Active;
            string name = run != null ? "ContinueButton" : "PlayButton";
            var next = RunSystem.NextMainLevel(State, Content);
            var b = Ui.Button(_frame, "", run != null ? (System.Action)(() => App.Router.Push(new RunScreen())) : () => App.Open("portals"), new Color(0, 0, 0, 0), 400 * _s, 190 * _s, true, 20);
            b.name = name;
            At((RectTransform)b.transform, 500, 210 + 112, 400, 190, true);
            var edge = Ui.RoundPanel(b.transform, SliceKit.Ink, 34 * _s, "Edge");
            edge.raycastTarget = false;
            Ui.Stretch(edge.rectTransform);
            var lip = Ui.RoundPanel(edge.transform, new Color32(0xE0, 0x9A, 0x2C, 255), 30 * _s, "Lip");
            lip.raycastTarget = false;
            Ui.Stretch(lip.rectTransform, 7 * _s, 7 * _s, 7 * _s, 7 * _s);
            var face = Ui.RoundPanel(lip.transform, StartYellow, 26 * _s, "Face");
            face.raycastTarget = false;
            Ui.Stretch(face.rectTransform, 0, 0, 0, 14 * _s);
            var inner = Ui.RoundPanel(face.transform, new Color32(0xFF, 0xF2, 0xA8, 255), 22 * _s, "Inner");
            inner.raycastTarget = false;
            Ui.Stretch(inner.rectTransform, 12 * _s, 10 * _s, 12 * _s, 12 * _s);
            var level = run != null ? Content.RunLevel(run.LevelId) : null;
            string kicker = run != null ? "RUN IN PROGRESS" + (level != null ? " · " + level.Name.ToUpperInvariant() : "")
                : next == null ? "PORTALS" : "LEVEL " + PortalsScreen.LevelNum(next.Number);
            var k = SliceKit.Chunky(b.transform, kicker, (int)(30 * _s), Color.white, TextAnchor.LowerCenter, "HubNextLevel");
            k.rectTransform.anchorMin = new Vector2(0, 1); k.rectTransform.anchorMax = new Vector2(1, 1); k.rectTransform.pivot = new Vector2(0.5f, 0);
            k.rectTransform.sizeDelta = new Vector2(80 * _s, 44 * _s); k.rectTransform.anchoredPosition = new Vector2(0, 4 * _s);
            var t = SliceKit.Chunky(face.transform, run != null ? "CONTINUE" : "START", (int)((run != null ? 66 : 88) * _s), Color.white, TextAnchor.MiddleCenter, "Title");
            Ui.Stretch(t.rectTransform, 0, 8 * _s, 0, run != null ? 10 * _s : 64 * _s);
            if (run == null && next != null)
            {
                var cost = RunSystem.EntryCost(Content, next.Id, RunMode.Normal).Amount;
                var c = SliceKit.Chunky(face.transform, "x" + cost, (int)(42 * _s), Color.white, TextAnchor.LowerCenter, "PillText");
                Ui.Stretch(c.rectTransform, 40 * _s, 0, 0, 14 * _s);
                var bolt = Ui.Panel(face.transform, Color.white, "Stamina");
                bolt.sprite = ScreenArt.Icon("stamina"); bolt.preserveAspect = true; bolt.raycastTarget = false;
                bolt.rectTransform.anchorMin = bolt.rectTransform.anchorMax = new Vector2(0.5f, 0);
                bolt.rectTransform.sizeDelta = new Vector2(54 * _s, 54 * _s); bolt.rectTransform.anchoredPosition = new Vector2(-30 * _s, 40 * _s);
            }
            if (!Session.IsUnlocked(Features.Parts)) return;
            // the auto-pick chip (farming: the run's choices are made for you)
            bool on = App.Settings != null && App.Settings.AutoPickDrafts;
            var chip = Ui.Button(_frame, "", () => { App.Settings?.SetAutoPickDrafts(!on); Rebuild(); }, on ? (Color)new Color32(0x5C, 0xC8, 0x4C, 255) : new Color32(0x3A, 0x30, 0x5A, 255), 110 * _s, 150 * _s, true, 20);
            chip.name = "AutoPickChip";
            At((RectTransform)chip.transform, 770, 210 + 112, 110, 150, true);
            UiShapes.Outline((RectTransform)chip.transform, SliceKit.Ink, 20 * _s, 5);
            var ot = SliceKit.Chunky(chip.transform, on ? "ON" : "OFF", (int)(36 * _s), Color.white, TextAnchor.UpperCenter, "State");
            Ui.Stretch(ot.rectTransform, 0, 8 * _s, 0, 0);
            var ic = Ui.Panel(chip.transform, Color.white, "Icon");
            ic.sprite = HudArt.Icon("dice"); ic.preserveAspect = true; ic.raycastTarget = false;
            Ui.Stretch(ic.rectTransform, 14 * _s, 56 * _s, 14 * _s, 14 * _s);
            if (Session.IsUnlocked(Features.Overcharged) || Session.IsUnlocked(Features.Burrow) || Session.IsUnlocked(Features.DarkSide))
            {
                var modes = Ui.Button(_frame, "", () => App.Open("portals"), new Color32(0x4A, 0x2A, 0x40, 255), 170 * _s, 150 * _s, true, 20);
                modes.name = "ModesButton";
                At((RectTransform)modes.transform, 985, 210 + 112, 170, 150, true);
                UiShapes.Outline((RectTransform)modes.transform, SliceKit.Ink, 20 * _s, 5);
                var mi = Ui.Panel(modes.transform, Color.white, "Icon");
                mi.sprite = HudArt.Icon("skull"); mi.preserveAspect = true; mi.raycastTarget = false;
                Ui.Stretch(mi.rectTransform, 40 * _s, 10 * _s, 40 * _s, 54 * _s);
                var ml = SliceKit.Chunky(modes.transform, "Modes", (int)(38 * _s), Color.white, TextAnchor.LowerCenter, "Label");
                Ui.Stretch(ml.rectTransform, 0, 0, 0, 8 * _s);
            }
        }

        /// <summary>The bottom tab bar: dark tabs with their icon, the Planet raised in purple with its label; badges.</summary>
        private void BuildTabs()
        {
            var list = HubLayout.Tabs(State, Content);
            var bar = Ui.Panel(_frame, new Color32(0x1A, 0x16, 0x2A, 255), "Tabs");
            bar.raycastTarget = false;
            At(bar.rectTransform, 540, 105, 1080, 210, true);
            float w = 1080f / Mathf.Max(list.Count, 3);
            float x0 = 540 - w * (list.Count - 1) / 2;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                bool active = e.Id == HubLayout.PlanetTab;
                float h = active ? 250 : 200;
                var b = Ui.Button(_frame, "", () => { if (e.ScreenId != null) App.Open(e.ScreenId); }, new Color(0, 0, 0, 0), (w - 12) * _s, h * _s, true, 20);
                b.name = "Tab:" + e.Id;
                At((RectTransform)b.transform, x0 + i * w, h / 2 + 4, w - 12, h, true);
                var edge = Ui.RoundPanel(b.transform, SliceKit.Ink, 26 * _s, "Edge");
                edge.raycastTarget = false;
                Ui.Stretch(edge.rectTransform);
                var face = Ui.RoundPanel(edge.transform, active ? TabActive : TabDark, 22 * _s, "Face");
                face.raycastTarget = false;
                Ui.Stretch(face.rectTransform, 5 * _s, 5 * _s, 5 * _s, 5 * _s);
                var img = Ui.Panel(face.transform, Color.white, "Icon");
                img.sprite = ScreenArt.HubIcon("tab_" + e.Id); img.preserveAspect = true; img.raycastTarget = false;
                Ui.Stretch(img.rectTransform, 30 * _s, active ? 14 * _s : 30 * _s, 30 * _s, active ? 80 * _s : 30 * _s);
                if (active)
                {
                    var l = SliceKit.Chunky(face.transform, e.Label, (int)(40 * _s), Color.white, TextAnchor.LowerCenter, "Label");
                    Ui.Stretch(l.rectTransform, 0, 0, 0, 16 * _s);
                }
                string badge = TabBadge(e.Id);
                if (!string.IsNullOrEmpty(badge))
                {
                    var bd = Ui.RoundPanel(b.transform, BadgeRed, 20 * _s, "Badge");
                    bd.raycastTarget = false;
                    bd.rectTransform.anchorMin = bd.rectTransform.anchorMax = new Vector2(1, 1);
                    bd.rectTransform.pivot = new Vector2(1, 0.5f);
                    bd.rectTransform.sizeDelta = new Vector2(100 * _s, 44 * _s); bd.rectTransform.anchoredPosition = new Vector2(-4 * _s, 0);
                    UiShapes.Outline(bd.rectTransform, Color.white, 20 * _s, 3);
                    var bt = SliceKit.Chunky(bd.transform, badge, (int)(26 * _s), Color.white, TextAnchor.MiddleCenter, "BadgeText");
                    Ui.Stretch(bt.rectTransform);
                }
            }
        }

        /// <summary>
        /// Badge of a bottom tab: "▲ UP" on the Worm when an equipped part can be levelled with the Ore the player has
        /// (<see cref="GearGuide.HasAffordableUpgrade"/>), else "NEW" on the day its feature unlocked.
        /// </summary>
        public string TabBadge(string tabId)
        {
            if (tabId == "worm" && GearGuide.HasAffordableUpgrade(State, Content)) return UpgradeBadge;
            return IsNewToday(tabId) ? "NEW" : null;
        }

        public const string UpgradeBadge = "▲ UP";

        /// <summary>A tab's feature was unlocked today (NEW badge).</summary>
        private bool IsNewToday(string tabId)
        {
            string feature = tabId == "worm" ? Features.Parts : tabId == "city" ? Features.BiomassFarm : tabId == "signals" ? Features.PartPulls : tabId == "arena" ? Features.Arena : null;
            if (feature == null || !State.Onboarding.Unlocked.TryGetValue(feature, out long at)) return false;
            return GameClock.DayIndex(Content, at) == GameClock.DayIndex(Content, Now);
        }

        // ---- unlock moment ----------------------------------------------------------------------------

        /// <summary>Every frame while the hub is on top (never from a rebuild): open a pending unlock moment.</summary>
        public override void Frame(float dt) => ShowPendingUnlock();

        /// <summary>
        /// Opens the full-screen unlock moment for the first pending celebration group while the hub is on top and no modal
        /// is open (never during a run). Returns true when it opened one. Snoozed groups (the player backed out) wait for a tap.
        /// </summary>
        public bool ShowPendingUnlock()
        {
            if (State == null || App.Router.Current != this || App.ModalLayer.childCount > 0) return false;
            var group = HubLayout.PendingGroup(State, Content);
            if (group == null || App.SnoozedUnlocks.Contains(group[0])) return false;
            App.SnoozedUnlocks.Add(group[0]); // backing out of the moment does not reopen it on every tick
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
