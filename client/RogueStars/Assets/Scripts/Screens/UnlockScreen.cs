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
    /// The unlock moment and the feature intro (GDD §16), data-driven by FeatureDef.Intro: full screen, a kicker, the
    /// feature's name on a ribbon, then its 2–4 panels one at a time — an art slot (IntroArt) with a small looping animation
    /// (IntroAnimator: tap, pulse, slide, count, spin, bounce, fill) and one short line — with page dots and NEXT. The last
    /// panel adds the one-line description, the reward (unlock gift, starter items, the stash kept for it) and ONE guided
    /// action that claims the celebration (FeatureCelebrate for every member) and opens the feature, plus the next teaser.
    /// <see cref="Replay"/> shows the same panels from a feature screen's "?" button: no claim, the last button closes it.
    /// SKIP or Android back on an unlock moment dismisses it for good (Q7): the celebration is claimed (counts as seen, the
    /// gifts are granted) and the player returns to where they were, without opening the feature.
    /// </summary>
    public sealed class UnlockScreen : UiScreen
    {
        private readonly List<string> _ids;
        private readonly bool _replay;
        private readonly List<KeyValuePair<FeatureDef, FeatureIntroStep>> _pages = new List<KeyValuePair<FeatureDef, FeatureIntroStep>>();
        private int _page;
        private bool _claiming;

        public UnlockScreen(List<string> featureIds) : this(featureIds, false) { }

        private UnlockScreen(List<string> featureIds, bool replay) { _ids = featureIds; _replay = replay; }

        /// <summary>The intro of an unlocked feature again ("?" on its screen): the same panels, closed by GOT IT.</summary>
        public static UnlockScreen Replay(string featureId) => new UnlockScreen(new List<string> { featureId }, true);

        public IReadOnlyList<string> FeatureIds => _ids;
        public bool IsReplay => _replay;
        public int Page => _page;
        public int PageCount => _pages.Count;
        public bool OnLastPage => _page >= _pages.Count - 1;
        public override string Title => _replay ? "How it works" : "New unlock";
        public override float HeaderHeight => 0;
        public override bool ShowBack => false;
        public override float FooterHeight => 360;
        /// <summary>The single guided action of the last panel (null on earlier panels and in a replay).</summary>
        public Button Action { get; private set; }

        private FeatureDef Primary => Content.Feature(_ids[0]);

        private void CollectPages()
        {
            _pages.Clear();
            foreach (var id in _ids)
            {
                var f = Content.Feature(id);
                if (f == null) continue;
                if (f.Intro.Count == 0) _pages.Add(new KeyValuePair<FeatureDef, FeatureIntroStep>(f, new FeatureIntroStep(f.Description, f.Art, IntroAnim.Pulse)));
                foreach (var step in f.Intro) _pages.Add(new KeyValuePair<FeatureDef, FeatureIntroStep>(f, step));
            }
            _page = Mathf.Clamp(_page, 0, Mathf.Max(0, _pages.Count - 1));
        }

        /// <summary>Show panel <paramref name="page"/> (clamped).</summary>
        public void ShowPage(int page)
        {
            _page = Mathf.Clamp(page, 0, Mathf.Max(0, _pages.Count - 1));
            Rebuild();
        }

        /// <summary>NEXT: the following panel.</summary>
        public void Next() { if (!OnLastPage) ShowPage(_page + 1); }

        protected override void Build(RectTransform body)
        {
            CollectPages();
            var def = Primary;
            var names = new List<string>();
            foreach (var id in _ids) { var f = Content.Feature(id); if (f != null) names.Add(f.Name); }
            bool run = def != null && def.RunContent;
            string kicker = _replay ? "HOW IT WORKS" : run ? "NEW IN YOUR RUNS" : "NEW UNLOCK";
            var k = Ui.Label(body, kicker, 30, HubKit.Cyan, TextAnchor.MiddleCenter, "Kicker");
            k.fontStyle = FontStyle.Bold;
            Ui.Size(k, -1, 90);

            // the title ribbon
            var ribbonSlot = HubKit.Centered(body, 900, 130, 150, "Ribbon");
            var ribbon = HubKit.RoundPanel(ribbonSlot, run ? HubKit.Orange : HubKit.Cyan, "RibbonFace");
            Ui.Stretch(ribbon.rectTransform);
            var name = Ui.Label(ribbon.transform, string.Join(" + ", names), names.Count > 1 || string.Join(" + ", names).Length > 20 ? 54 : 66,
                run ? HubKit.OrangeInk : HubKit.CyanInk, TextAnchor.MiddleCenter, "FeatureName");
            name.fontStyle = FontStyle.Bold;
            Ui.Stretch(name.rectTransform, 20, 0, 20, 0);

            var page = _pages.Count > 0 ? _pages[_page].Value : null;
            BuildArt(body, page);

            var text = Ui.Label(body, page?.Text ?? "", 44, HubKit.Ink, TextAnchor.MiddleCenter, "StepText");
            text.font = Ui.FontFor(text.text); // the chunky display font (U2)
            Ui.Size(text, -1, 150);

            // page dots
            var dots = Ui.Row(body, 40, 18, null, 0, "Dots");
            dots.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < _pages.Count; i++)
            {
                var d = Ui.Panel(dots, i == _page ? HubKit.Gold : HubKit.Dashed, "Dot:" + i);
                d.sprite = HubKit.Disc;
                Ui.Size(d, i == _page ? 34 : 24, i == _page ? 34 : 24, 0);
            }

            if (OnLastPage) BuildLastPage(body);
            else
            {
                var row = Ui.Row(Footer, 150, 24, null, 0, "Buttons");
                Ui.Size(row, -1, 150, -1, 0); // never stretched by the buttons
                row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                // a light text button (U2); on an unlock moment it dismisses the moment for good (Q7), in a replay it jumps to the end
                var skip = HubKit.RoundButton(row, "SKIP", () => { if (_replay) ShowPage(_pages.Count - 1); else Dismiss(); }, new Color(1, 1, 1, 0.1f), HubKit.Ink, 260, 130, 40);
                skip.name = "IntroSkip";
                var next = HubKit.RoundButton(row, "NEXT", Next, HubKit.Gold, HubKit.OrangeInk, 520, 150, 52);
                next.name = "IntroNext";
            }
        }

        private void BuildArt(RectTransform body, FeatureIntroStep step)
        {
            var holder = HubKit.Centered(body, 600, 600, 660, "Art"); // a big art card (U2: the page was half empty)
            var glow = Ui.Panel(holder, new Color(HubKit.Cyan.r, HubKit.Cyan.g, HubKit.Cyan.b, 0.18f), "Glow");
            glow.sprite = HubKit.Circle; glow.raycastTarget = false;
            Ui.Stretch(glow.rectTransform, -70, -70, -70, -70);
            var frame = HubKit.RoundPanel(holder, HubKit.Cyan, "Frame");
            Ui.Stretch(frame.rectTransform);
            var face = HubKit.RoundPanel(frame.transform, Theme.Hex(0x16324A), "Face");
            Ui.Stretch(face.rectTransform, 9, 9, 9, 9);
            face.gameObject.AddComponent<RectMask2D>(); // slides stay inside the frame
            var art = Ui.Panel(face.transform, Color.white, "IntroArt");
            art.sprite = IntroArt.Sprite(step?.Art);
            art.preserveAspect = true;
            art.raycastTarget = false;
            var art_rt = art.rectTransform;
            art_rt.anchorMin = art_rt.anchorMax = art_rt.pivot = new Vector2(0.5f, 0.5f);
            art_rt.sizeDelta = new Vector2(450, 450);
            art.gameObject.AddComponent<CanvasGroup>();
            var anim = holder.gameObject.AddComponent<IntroAnimator>();
            anim.Kind = step?.Anim;
            anim.Art = art_rt;
            anim.Glow = glow;
            switch (step?.Anim)
            {
                case IntroAnim.Tap:
                {
                    var hand = Ui.Panel(holder, Color.white, "Hand");
                    hand.sprite = IntroArt.Hand(); hand.preserveAspect = true; hand.raycastTarget = false;
                    var h = hand.rectTransform;
                    h.anchorMin = h.anchorMax = new Vector2(0.5f, 0.5f);
                    h.pivot = new Vector2(0.45f, 0.95f);
                    h.sizeDelta = new Vector2(170, 170);
                    h.anchoredPosition = new Vector2(60, -20);
                    anim.Hand = h;
                    break;
                }
                case IntroAnim.Count:
                {
                    var c = Ui.Display(holder, "+100", 64, HubKit.Gold, TextAnchor.MiddleCenter, "Counter");
                    c.raycastTarget = false;
                    c.rectTransform.anchorMin = c.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    c.rectTransform.sizeDelta = new Vector2(300, 90);
                    c.rectTransform.anchoredPosition = new Vector2(0, 190);
                    c.gameObject.AddComponent<Outline>().effectColor = HubKit.OrangeInk;
                    anim.Counter = c;
                    break;
                }
                case IntroAnim.Fill:
                {
                    var bar = HubKit.RoundPanel(holder, HubKit.CardDeep, "FillBar");
                    var b = bar.rectTransform;
                    b.anchorMin = new Vector2(0.1f, 0); b.anchorMax = new Vector2(0.9f, 0);
                    b.pivot = new Vector2(0.5f, 1);
                    b.sizeDelta = new Vector2(0, 44); b.anchoredPosition = new Vector2(0, -14);
                    var fill = HubKit.RoundPanel(bar.transform, HubKit.Gold, "Fill");
                    fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0.7f, 1);
                    fill.rectTransform.offsetMin = new Vector2(5, 5); fill.rectTransform.offsetMax = new Vector2(-5, -5);
                    anim.Fill = fill.rectTransform;
                    break;
                }
            }
        }

        private void BuildLastPage(RectTransform body)
        {
            if (_replay)
            {
                var done = HubKit.RoundButton(Footer, "GOT IT", () => App.Router.Pop(), HubKit.Cyan, HubKit.CyanInk, -1, 150, 50);
                done.name = "IntroDone";
                return;
            }
            var def = Primary;
            var rewards = new List<Reward>();
            foreach (var id in _ids)
            {
                var f = Content.Feature(id);
                if (f == null) continue;
                rewards.AddRange(f.UnlockRewards);
                rewards.AddRange(f.UnlockGrants);
                // Rewards follow use: what was kept for this feature until now (GDD §13).
                rewards.AddRange(RewardGate.ReleasedAt(State, id));
            }
            var desc = Ui.Label(body, def?.Description ?? "", 32, HubKit.InkSoft, TextAnchor.MiddleCenter, "Description");
            Ui.Size(desc, -1, 110);
            if (rewards.Count > 0)
            {
                var row = Ui.Row(body, 70, 16, null, 0, "Rewards");
                row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                foreach (var chip in Fmt.RewardChips(rewards, Content))
                    HubKit.Chip(row, chip, HubKit.Ink, HubKit.Card, -1, 64, "Reward");
            }
            var action = HubLayout.UnlockAction(_ids[0]);
            Action = HubKit.RoundButton(Footer, action.Label, Claim, HubKit.Cyan, HubKit.CyanInk, -1, 170, 50);
            Action.name = "UnlockAction";
            var next = Features.Next(State, Content, Now);
            if (next != null)
                HubKit.Teaser(Footer, null, HubScreen.TeaserText(next), null, HubKit.CardDeep, "UnlockTeaser");
        }

        /// <summary>Claim every pending member of the group (unlock gifts), then open the feature in place of this screen.</summary>
        public void Claim() => ClaimAll(true);

        /// <summary>
        /// SKIP / back (Q7): the moment counts as seen — every pending member is claimed (gifts granted) — and the screen
        /// closes without opening the feature. A replay just closes.
        /// </summary>
        public void Dismiss()
        {
            if (_replay) { if (App.Router.Current == this) App.Router.Pop(); return; }
            ClaimAll(false);
        }

        public override bool HandleSystemBack() { Dismiss(); return true; }

        private void ClaimAll(bool open)
        {
            if (_claiming || _replay) return;
            _claiming = true;
            var pending = new List<string>();
            foreach (var id in _ids) if (!State.Onboarding.Celebrated.Contains(id) && State.Onboarding.Unlocked.ContainsKey(id)) pending.Add(id);
            ClaimNext(pending, 0, open);
        }

        private void ClaimNext(List<string> pending, int i, bool open)
        {
            if (i >= pending.Count) { if (open) Open(); else Close(); return; }
            Run(new FeatureCelebrate { FeatureId = pending[i] }, r =>
            {
                if (r != null && r.Ok) ClaimNext(pending, i + 1, open);
                else _claiming = false;
            });
        }

        private void Close()
        {
            if (App.Router.Current == this) App.Router.Pop();
        }

        private void Open()
        {
            if (App.Router.Current != this) return;
            var target = ScreenCatalog.Find(HubLayout.UnlockAction(_ids[0]).ScreenId);
            if (target != null) App.Router.Replace(target.Create());
            else App.Router.Pop();
        }
    }
}
