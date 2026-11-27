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
    /// Run result (reference victory; defeat is our own variant): the dimmed arena, the crest (a gold crown in laurels, or a
    /// cracked grey one), the ribbon "VICTORY!" / "DEFEATED", a hanging pennant with the level tag, "You Survived · N Rolls ·
    /// Longest Journey", the level's three stars, a full-width reward strip (tiles: icon + amount, identical rewards summed
    /// into one tile, <see cref="Aggregate"/>), the run's chips, double rewards (rewarded ad), Continue (back to the portals) and the next level / try again.
    /// Rewards with a Legendary or Mythic item open the shared pull reveal over the screen once (Q4); others only the strip.
    /// </summary>
    public sealed class RunResultScreen : UiScreen
    {
        public override string Title => "Run result";
        public override float HeaderHeight => 0;
        public override bool CoversResourceBar => true;
        public override bool ShowBack => false;

        private static readonly Color Gold = new Color32(0xF6, 0xCC, 0x40, 255);
        private static readonly Color StripBg = new Color32(0x2E, 0x24, 0x48, 255);
        private bool _revealed;
        /// <summary>The pull reveal this screen opened (null: the rewards had no Legendary / Mythic item).</summary>
        public UiModal Reveal { get; private set; }

        /// <summary>Reward tile size and gap on the strip (reference px); the row scrolls sideways when the tiles do not fit (#39).</summary>
        public const float RewardTileSize = 165, RewardTileGap = 28;

        private bool _doubleShown, _surgeShown;

        /// <summary>
        /// The reward strip's order (run UI glitches #39): the highest rarity first, ties in the summed order (pure; tested).
        /// </summary>
        public static List<Reward> ByRarity(List<Reward> rewards)
        {
            var list = new List<Reward>(rewards ?? new List<Reward>());
            var order = new Dictionary<Reward, int>();
            for (int i = 0; i < list.Count; i++) order[list[i]] = i;
            list.Sort((a, b) => a.Rarity != b.Rarity ? b.Rarity.CompareTo(a.Rarity) : order[a].CompareTo(order[b]));
            return list;
        }

        /// <summary>
        /// The reward strip's tiles: identical rewards (same kind, id and rarity) summed into one, in first-seen order
        /// (owner playtest 0.1.200: "30 Diamonds", not 10, 10, 10). Pure; tested.
        /// </summary>
        public static List<Reward> Aggregate(IEnumerable<Reward> rewards)
        {
            var list = new List<Reward>();
            var at = new Dictionary<string, int>();
            if (rewards == null) return list;
            foreach (var r in rewards)
            {
                if (r == null) continue;
                string key = r.Kind + "|" + r.Id + "|" + r.Rarity;
                if (at.TryGetValue(key, out int i)) list[i].Amount += r.Amount;
                else { at[key] = list.Count; list.Add(new Reward(r.Kind, r.Id, r.Amount, r.Rarity)); }
            }
            return list;
        }

        protected override void Build(RectTransform body)
        {
            var s0 = State.Run.LastSummary;
            if (s0 == null) { Note(body, "No run finished yet."); return; }
            var bl = body.GetComponent<VerticalLayoutGroup>();
            if (bl != null) { bl.padding = new RectOffset(0, 0, 0, 0); bl.spacing = 0; }
            float frameH = BodyFrame != null && BodyFrame.rect.height > 100 ? BodyFrame.rect.height : 1920;
            var holder = Ui.Rect(body, "ResultHolder");
            Ui.Size(holder, -1, frameH);
            Canvas.ForceUpdateCanvases();
            var sum = s0;
            bool won = sum.Won;
            var level = Content.RunLevel(sum.LevelId);
            var portal = level != null ? Content.Portal(level.PortalId) : null;

            // the dimmed arena behind everything
            var bgBox = SliceKit.Backdrop(holder, ArtCatalog.BattleBackground(null), frameH, "Backdrop");
            Ui.Stretch(bgBox.transform.parent as RectTransform);
            var dim = Ui.Panel(holder, won ? new Color(0.03f, 0.03f, 0.06f, 0.86f) : new Color(0.06f, 0.02f, 0.04f, 0.9f), "Dim");
            dim.raycastTarget = false;
            Ui.Stretch(dim.rectTransform);
            var frame = RunScenes.Frame(holder, out float s);

            // crest with a halo
            var halo = Ui.Panel(frame, won ? new Color(1f, 0.86f, 0.35f, 0.5f) : new Color(0.6f, 0.6f, 0.75f, 0.25f), "Halo");
            halo.sprite = ScreenArt.Glow(); halo.raycastTarget = false;
            RunScenes.At(halo.rectTransform, s, 540, 330, 900, 700);
            var crest = Ui.Panel(frame, Color.white, "Crest");
            crest.sprite = ScreenArt.Result(won ? "crown" : "crest_broken"); crest.preserveAspect = true; crest.raycastTarget = false;
            RunScenes.At(crest.rectTransform, s, 540, 320, 520, 380);
            if (won) UiBob.On(crest.rectTransform, 8 * s, 2f);

            // the pennant hanging from the ribbon
            var pennant = Ui.Panel(frame, Color.white, "Pennant");
            pennant.sprite = ScreenArt.Result(won ? "banner" : "banner_defeat"); pennant.preserveAspect = false; pennant.raycastTarget = false;
            RunScenes.At(pennant.rectTransform, s, 540, 1000, 600, 640);
            var rib = Ui.Panel(frame, Color.white, "Ribbon");
            rib.sprite = SliceCards.Ribbon(won ? (Color)new Color32(0xE4, 0x50, 0x64, 255) : new Color32(0x6A, 0x6E, 0x8A, 255)); rib.preserveAspect = true; rib.raycastTarget = false;
            RunScenes.At(rib.rectTransform, s, 540, 640, 1000, 170);
            var banner = SliceKit.Chunky(frame, won ? "VICTORY!" : "DEFEATED", (int)(132 * s), won ? Gold : (Color)new Color32(0xD8, 0xDC, 0xE8, 255), TextAnchor.MiddleCenter, "Banner");
            RunScenes.At(banner.rectTransform, s, 540, 610, 1000, 170);
            UiTween.PopIn(banner, 0.35f);
            string tag = level != null && level.Number > 0 ? "Level " + PortalsScreen.LevelNum(level.Number) : level?.Name ?? sum.LevelId;
            if (sum.Mode != RunMode.Normal) tag += " · " + sum.Mode;
            var tagPill = Ui.RoundPanel(frame, new Color32(0xF2, 0xC8, 0x4A, 255), 34 * s, "LevelTag");
            tagPill.raycastTarget = false;
            RunScenes.At(tagPill.rectTransform, s, 540, 735, 300, 72);
            UiShapes.Outline(tagPill.rectTransform, SliceKit.Ink, 34 * s, 5);
            var tt = SliceKit.Chunky(tagPill.transform, tag, (int)(44 * s), Color.white, TextAnchor.MiddleCenter, "LevelTagText");
            Ui.Stretch(tt.rectTransform, 10 * s, 0, 10 * s, 4 * s);
            tt.resizeTextForBestFit = true; tt.resizeTextMaxSize = tt.fontSize; tt.resizeTextMinSize = (int)(24 * s); tt.horizontalOverflow = HorizontalWrapMode.Wrap;
            var survived = SliceKit.Chunky(frame, won ? "You Survived" : "You Lasted", (int)(52 * s), Color.white, TextAnchor.MiddleCenter, "Survived");
            RunScenes.At(survived.rectTransform, s, 540, 838, 560, 70);
            var rolls = SliceKit.Chunky(frame, sum.Rolls + " Rolls", (int)(124 * s), Color.white, TextAnchor.MiddleCenter, "Rolls");
            RunScenes.At(rolls.rectTransform, s, 540, 935, 600, 140);
            int longest = System.Math.Max(sum.LongestRolls, sum.Rolls);
            var journey = SliceKit.Chunky(frame, "Longest Journey:\n" + longest + " Rolls", (int)(50 * s), Gold, TextAnchor.MiddleCenter, "Longest");
            RunScenes.At(journey.rectTransform, s, 540, 1063, 560, 120);
            if (level != null && sum.Mode != RunMode.Burrow)
            {
                var prog = RunSystem.Progress(State, Content, sum.LevelId);
                for (int i = 0; i < 3; i++)
                {
                    bool got = i < prog.Feats.Count && prog.Feats[i];
                    bool isNew = sum.NewFeats.Contains(i);
                    var star = SliceKit.Chunky(frame, "★", (int)((i == 1 ? 76 : 62) * s), got ? Gold : new Color(0.35f, 0.3f, 0.4f, 1), TextAnchor.MiddleCenter, "StarGlyph");
                    RunScenes.At(star.rectTransform, s, 470 + i * 70, i == 1 ? 1166 : 1174, 90, 90);
                    if (isNew) UiTween.PopIn(star, 0.5f);
                }
            }

            // the reward strip
            var strip = Ui.Panel(holder, StripBg, "RewardStrip");
            strip.raycastTarget = false;
            var srt = strip.rectTransform;
            srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1); srt.pivot = new Vector2(0.5f, 1);
            srt.sizeDelta = new Vector2(0, 290 * s); srt.anchoredPosition = new Vector2(0, -1362 * s);
            foreach (var (y, c) in new[] { (0f, (Color)Gold), (282f, (Color)new Color32(0xC8, 0x82, 0x2E, 255)) })
            {
                var line = Ui.Panel(strip.transform, c, "Edge");
                line.raycastTarget = false;
                line.rectTransform.anchorMin = new Vector2(0, 1); line.rectTransform.anchorMax = new Vector2(1, 1); line.rectTransform.pivot = new Vector2(0.5f, 1);
                line.rectTransform.sizeDelta = new Vector2(0, 10 * s); line.rectTransform.anchoredPosition = new Vector2(0, -y * s);
            }
            frame.SetAsLastSibling();
            var shown = ByRarity(Aggregate(sum.Granted));
            if (shown.Count == 0)
            {
                var none = SliceKit.Chunky(frame, won ? "No rewards this time." : "Pending loot lost.", (int)(46 * s), new Color(1, 1, 1, 0.75f), TextAnchor.MiddleCenter, "NoRewards");
                RunScenes.At(none.rectTransform, s, 540, 1507, 900, 80);
            }
            // #39: every reward on one row that scrolls sideways (the highest rarity first); a row that fits is centred
            // #40: the Biomass surge and the doubling play on the tiles (count up, pop, a ×N badge) instead of a text line
            bool surge = sum.BiomassBonus && RewardGate.CurrencyHasUse(State, Content, Currencies.Biomass, Now);
            bool animateSurge = surge && !_surgeShown, animateDouble = sum.Doubled && !_doubleShown;
            _surgeShown |= surge; _doubleShown |= sum.Doubled;
            double surgeMult = Content.RunTuning.DailyBiomassBonusMult;
            var row = RewardRow(frame, s, shown.Count);
            float step = RewardTileSize + RewardTileGap;
            for (int i = 0; i < shown.Count; i++)
            {
                var r = shown[i];
                bool doubled = sum.Doubled && sum.Doublable.Exists(d => d.Kind == r.Kind && d.Id == r.Id);
                bool surged = surge && r.Kind == RewardKinds.Currency && r.Id == Currencies.Biomass;
                double mult = (doubled ? 2 : 1) * (surged ? surgeMult : 1);
                bool play = (animateDouble && doubled) || (animateSurge && surged);
                double from = play && mult > 1 ? r.Amount / (animateDouble && !animateSurge && surged ? 2 : mult) : r.Amount;
                RewardTile(row, s, 40 + RewardTileSize / 2 + i * step, 110, RewardTileSize, r, mult, from, play ? 0.35f + 0.12f * i : -1);
            }
            if (!_revealed)
            {
                _revealed = true; // once per screen (rebuilds do not replay it)
                Reveal = PullReveal.ShowIfEarned(App.ModalLayer, shown, Content,
                    new RevealOptions { Title = won ? "Run rewards" : "Kept rewards", Name = "RunReveal", Skip = SummonEventScreen.SkipAnimation });
            }

            // the run's numbers (the skills gained are not listed: owner playtest 0.1.200)
            var chips = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Run level " + sum.Level, "LevelChip"),
                new KeyValuePair<string, string>("Skills " + sum.Gains.Count, "SkillsChip"),
            };
            if (sum.Mode == RunMode.Burrow) chips.Add(new KeyValuePair<string, string>("Floors " + sum.Floors, "FloorsChip"));
            if (sum.Minibosses > 0) chips.Add(new KeyValuePair<string, string>("Minibosses " + sum.Minibosses, "MinibossChip"));
            if (sum.ShopBuys > 0) chips.Add(new KeyValuePair<string, string>("Shop buys " + sum.ShopBuys, "ShopChip"));
            float cw = 1000f / chips.Count;
            for (int i = 0; i < chips.Count; i++)
            {
                var pill = SliceKit.Pill(frame, s, 40 + cw * (i + 0.5f), 1712, cw - 16, 64, SliceKit.PillDark, chips[i].Value);
                var t = SliceKit.Chunky(pill.transform, chips[i].Key, (int)(30 * s), Color.white, TextAnchor.MiddleCenter, "Text");
                Ui.Stretch(t.rectTransform, 8 * s, 0, 8 * s, 2 * s);
                t.horizontalOverflow = HorizontalWrapMode.Wrap; t.resizeTextForBestFit = true; t.resizeTextMaxSize = t.fontSize; t.resizeTextMinSize = (int)(18 * s);
            }
            DoubleRewards(frame, s, sum);

            // Continue (back to the portals) and the next level / try again
            var cont = Ui.Button(frame, "", () => App.Router.Pop(), new Color32(0x3E, 0x3A, 0x78, 255), 430 * s, 170 * s, true, 20);
            cont.name = "Button:Portals";
            RunScenes.At((RectTransform)cont.transform, s, 540, 1990, 430, 170);
            UiShapes.Outline((RectTransform)cont.transform, SliceKit.Ink, Theme.RadiusButton, 7);
            var ct = SliceKit.Chunky(cont.transform, "Continue", (int)(76 * s), Color.white, TextAnchor.MiddleCenter, "ContinueText");
            Ui.Stretch(ct.rectTransform, 0, 0, 0, 8 * s);
            LevelDef next = null;
            if (won && sum.Mode == RunMode.Normal && level != null && level.Number > 0)
            {
                var candidate = RunSystem.NextMainLevel(State, Content);
                if (candidate != null && RunSystem.IsLevelUnlocked(State, Content, candidate.Id, RunMode.Normal)) next = candidate;
            }
            var target = next ?? level;
            if (target != null)
            {
                var mode = next != null ? RunMode.Normal : sum.Mode;
                string caption = next != null ? "Next: Level " + PortalsScreen.LevelNum(next.Number) : won ? "Play again" : "Try again";
                var go = Ui.Button(frame, "", () => SignaturePick.StartRun(App, new RunStart { PortalId = target.PortalId, LevelId = target.Id, Mode = mode }, r => { if (r.Ok) App.Router.Replace(new RunScreen()); }),
                    new Color32(0xF2, 0x8A, 0x3A, 255), 520 * s, 100 * s, true, 20);
                go.name = "Again";
                RunScenes.At((RectTransform)go.transform, s, 540, 2150, 520, 100);
                UiShapes.Outline((RectTransform)go.transform, SliceKit.Ink, Theme.RadiusButton, 5);
                var gt = SliceKit.Chunky(go.transform, caption, (int)(46 * s), Color.white, TextAnchor.MiddleCenter, "AgainText");
                Ui.Stretch(gt.rectTransform, 0, 0, 0, 6 * s);
            }
        }

        /// <summary>
        /// The reward row (#39): a viewport over the strip (named "RewardScroll", RectMask2D + horizontal ScrollRect) and its
        /// content ("RewardRow", 300 reference px high, tiles placed from its top-left); centred when the tiles fit the width.
        /// </summary>
        private static RectTransform RewardRow(RectTransform frame, float s, int n)
        {
            var view = Ui.Rect(frame, "RewardScroll");
            RunScenes.At(view, s, 540, 1380 + 150, 1080, 300);
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            view.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect(view, "RewardRow");
            float w = 80 + n * RewardTileSize + Mathf.Max(0, n - 1) * RewardTileGap;
            content.anchorMin = content.anchorMax = new Vector2(0, 1); content.pivot = new Vector2(0, 1);
            content.sizeDelta = new Vector2(w * s, 300 * s);
            content.anchoredPosition = new Vector2(Mathf.Max(0, (1080 - w) / 2) * s, 0);
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view; scroll.content = content; scroll.horizontal = w > 1080; scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Elastic; scroll.scrollSensitivity = 30;
            return content;
        }

        /// <summary>
        /// A reward tile on the strip: a tinted square with the reward's mark and its amount ("x1250"), its name under it (two
        /// lines, ≥ 20 px at 1080: #42). <paramref name="mult"/> &gt; 1: a "×N" badge (doubled, Biomass surge); with
        /// <paramref name="delay"/> ≥ 0 the amount counts up from <paramref name="from"/> and the tile pops when it lands (#40).
        /// </summary>
        private void RewardTile(RectTransform frame, float s, float x, float y, float size, Reward r, double mult = 1, double from = 0, float delay = -1)
        {
            Color c = r.Kind == RewardKinds.Currency ? Theme.CurrencyColor(r.Id) : r.Rarity > 0 ? Theme.RarityColor((Rarity)Mathf.Clamp(r.Rarity, 0, (int)Rarity.Mythic)) : Theme.Premium;
            var edge = Ui.RoundPanel(frame, SliceKit.Ink, 28 * s, "Reward");
            edge.raycastTarget = false;
            RunScenes.At(edge.rectTransform, s, x, y, size, size);
            var rim = Ui.RoundPanel(edge.transform, Color.Lerp(c, Color.white, 0.45f), 24 * s, "Rim");
            rim.raycastTarget = false;
            Ui.Stretch(rim.rectTransform, 6 * s, 6 * s, 6 * s, 6 * s);
            var face = Ui.RoundPanel(rim.transform, Theme.Shade(c, 0.75f), 20 * s, "Face");
            face.raycastTarget = false;
            Ui.Stretch(face.rectTransform, 7 * s, 7 * s, 7 * s, 7 * s);
            var orb = Ui.Panel(face.transform, Color.white, "Icon");
            orb.raycastTarget = false;
            // R1: one icon per currency everywhere (the gem for Diamonds), the same as the top bar
            orb.sprite = r.Kind == RewardKinds.Currency ? SummonArt.CurrencyIcon(r.Id) : r.Kind == RewardKinds.Part ? HudArt.Icon("sword") : ScreenArt.Icon("blessing");
            orb.color = Color.white;
            orb.preserveAspect = true;
            Ui.Stretch(orb.rectTransform, size * 0.16f * s, size * 0.08f * s, size * 0.16f * s, size * 0.26f * s);
            var a = SliceKit.Chunky(edge.transform, "x" + Fmt.Num(r.Amount), (int)(44 * s * Mathf.Min(1, size / 165f)), Color.white, TextAnchor.LowerCenter, "Amount");
            Ui.Stretch(a.rectTransform, 0, 0, 0, -6 * s);
            // the caption is only the name (R1: the amount was shown twice); #42: two lines, never below 20 px at 1080
            string caption = r.Kind == RewardKinds.Currency ? Fmt.CurrencyName(r.Id, Content) : Fmt.Reward(new Reward(r.Kind, r.Id, 1, r.Rarity), Content);
            var name = SliceKit.Chunky(frame, caption, (int)(26 * s), new Color(1, 1, 1, 0.85f), TextAnchor.UpperCenter, "RewardName");
            RunScenes.At(name.rectTransform, s, x, y + size / 2 + 40, size + 26, 66);
            name.horizontalOverflow = HorizontalWrapMode.Wrap; name.verticalOverflow = VerticalWrapMode.Truncate;
            name.resizeTextForBestFit = true; name.resizeTextMaxSize = name.fontSize; name.resizeTextMinSize = Mathf.Max(1, (int)(20 * s));
            name.lineSpacing = 0.85f;
            if (mult <= 1.0001) return;
            // the multiplier badge on the tile's corner
            var badge = Ui.RoundPanel(edge.transform, new Color32(0xF2, 0x8A, 0x3A, 255), 22 * s, "MultBadge");
            badge.raycastTarget = false;
            var brt = badge.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(76 * s, 48 * s); brt.anchoredPosition = new Vector2(-10 * s, -6 * s);
            UiShapes.Outline(brt, SliceKit.Ink, 22 * s, 4);
            var bt = SliceKit.Chunky(brt, "×" + Fmt.OneDecimal(mult), (int)(32 * s), Color.white, TextAnchor.MiddleCenter, "MultText");
            Ui.Stretch(bt.rectTransform, 0, 0, 0, 2 * s);
            if (delay < 0 || !Application.isPlaying) return;
            // count up from the amount before the multiplier, then the tile and the badge pop
            badge.gameObject.SetActive(false);
            double to = r.Amount;
            a.text = "x" + Fmt.Num(from);
            var tileRt = edge.rectTransform;
            var seq = UiSequence.On(edge.gameObject);
            seq.Wait(delay);
            seq.Call(() => { badge.gameObject.SetActive(true); UiTween.PopIn(badge, 0.25f); });
            seq.Then(0.8f, k =>
            {
                float e = 1 - (1 - k) * (1 - k);
                a.text = "x" + Fmt.Num(from + (to - from) * e);
                a.rectTransform.localScale = Vector3.one * (1 + 0.12f * Mathf.Sin(k * Mathf.PI * 6) * (1 - k));
            }, () =>
            {
                a.text = "x" + Fmt.Num(to);
                a.rectTransform.localScale = Vector3.one;
                UiTweens.Play(tileRt.gameObject, "pop", 0.3f, k => tileRt.localScale = Vector3.one * (1 + 0.18f * Mathf.Sin(k * Mathf.PI)), () => tileRt.localScale = Vector3.one);
            });
        }

        /// <summary>Rewarded ad (instant with No Ads): the won run's rewards once more (<see cref="RunDoubleRewards"/>).</summary>
        private void DoubleRewards(RectTransform frame, float s, RunSummary sum)
        {
            if (!sum.Won || sum.Doublable.Count == 0) return;
            if (sum.Doubled)
            {
                var d = SliceKit.Chunky(frame, "Rewards doubled!", (int)(50 * s), Theme.Good, TextAnchor.MiddleCenter, "Doubled");
                RunScenes.At(d.rectTransform, s, 540, 1830, 800, 70);
                return;
            }
            int left = ShopSystem.AdViewsLeft(State, Content, RunSystem.DoubleRewardsAdPlacement, Now);
            bool noAds = ShopSystem.HasNoAds(State, Content);
            string label = (noAds ? "Double these rewards" : "Watch ad: double rewards") + "  <size=" + (int)(26 * s) + ">(" + (left > 0 ? left + " left today" : "daily limit reached") + ")</size>";
            var b = Ui.Button(frame, "", () =>
            {
                if (noAds) { Run(new RunDoubleRewards()); return; }
                Session.Ads.Show(RunSystem.DoubleRewardsAdPlacement, ad =>
                {
                    if (ad.Ok) Run(new RunDoubleRewards { AdToken = ad.Value });
                    else Session.Error(Fmt.Humanize(ad.Error));
                });
            }, new Color32(0x1E, 0x18, 0x30, 230), 760 * s, 96 * s, left > 0, 20);
            b.name = "DoubleRewards";
            RunScenes.At((RectTransform)b.transform, s, 540, 1830, 760, 96);
            UiShapes.Outline((RectTransform)b.transform, Gold, Theme.RadiusButton, 5);
            var t = SliceKit.Chunky(b.transform, label, (int)(38 * s), Gold, TextAnchor.MiddleCenter, "DoubleText");
            Ui.Stretch(t.rectTransform, 12 * s, 0, 12 * s, 4 * s);
        }
    }
}
