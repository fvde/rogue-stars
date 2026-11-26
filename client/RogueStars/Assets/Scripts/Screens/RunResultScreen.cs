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
            var shown = Aggregate(sum.Granted);
            if (shown.Count == 0)
            {
                var none = SliceKit.Chunky(frame, won ? "No rewards this time." : "Pending loot lost.", (int)(46 * s), new Color(1, 1, 1, 0.75f), TextAnchor.MiddleCenter, "NoRewards");
                RunScenes.At(none.rectTransform, s, 540, 1507, 900, 80);
            }
            int n = Mathf.Min(5, shown.Count);
            float tile = n <= 3 ? 180 : 165, gap = 28, x0 = 540 - (n * tile + (n - 1) * gap) / 2 + tile / 2;
            for (int i = 0; i < n; i++) RewardTile(frame, s, x0 + i * (tile + gap), 1490, tile, shown[i]);
            if (!_revealed)
            {
                _revealed = true; // once per screen (rebuilds do not replay it)
                Reveal = PullReveal.ShowIfEarned(App.ModalLayer, shown, Content,
                    new RevealOptions { Title = won ? "Run rewards" : "Kept rewards", Name = "RunReveal", Skip = SummonEventScreen.SkipAnimation });
            }
            if (shown.Count > n)
            {
                var more = SliceKit.Chunky(frame, "+" + (shown.Count - n) + " more", (int)(34 * s), Color.white, TextAnchor.MiddleCenter, "MoreRewards");
                RunScenes.At(more.rectTransform, s, 540, 1622, 400, 44);
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
            if (sum.BiomassBonus && RewardGate.CurrencyHasUse(State, Content, Currencies.Biomass, Now))
            {
                var surge = SliceKit.Chunky(frame, "Biomass surge ×" + Fmt.OneDecimal(Content.RunTuning.DailyBiomassBonusMult) + " applied", (int)(32 * s), Theme.CurrencyColor(Currencies.Biomass), TextAnchor.MiddleCenter, "SurgeApplied");
                RunScenes.At(surge.rectTransform, s, 540, 1660, 900, 44);
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

        /// <summary>A reward tile on the strip: a tinted square with the reward's mark and its amount ("x1,250"), its name under it.</summary>
        private void RewardTile(RectTransform frame, float s, float x, float y, float size, Reward r)
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
            string amount = r.Kind == RewardKinds.Currency ? "x" + Fmt.Num(r.Amount) : "x" + r.Amount;
            var a = SliceKit.Chunky(edge.transform, amount, (int)(44 * s), Color.white, TextAnchor.LowerCenter, "Amount");
            Ui.Stretch(a.rectTransform, 0, 0, 0, -6 * s);
            // the caption is only the name (R1: the amount was shown twice)
            string caption = r.Kind == RewardKinds.Currency ? Fmt.CurrencyName(r.Id, Content) : Fmt.Reward(new Reward(r.Kind, r.Id, 1, r.Rarity), Content);
            var name = SliceKit.Chunky(frame, caption, (int)(24 * s), new Color(1, 1, 1, 0.8f), TextAnchor.UpperCenter, "RewardName");
            RunScenes.At(name.rectTransform, s, x, y + size / 2 + 30, size + 24, 40);
            name.horizontalOverflow = HorizontalWrapMode.Wrap; name.resizeTextForBestFit = true; name.resizeTextMaxSize = name.fontSize; name.resizeTextMinSize = (int)(16 * s);
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
