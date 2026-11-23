using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// The unlock moment (design canvas "Unlock"): full screen, the feature's name, one line of what it does, the reward,
    /// ONE guided action that claims the celebration (FeatureCelebrate for every member of the unlock group) and opens the
    /// feature, and the next teaser. Shown by the hub for <see cref="OnboardingSystem.PendingCelebrations"/>.
    /// </summary>
    public sealed class UnlockScreen : UiScreen
    {
        private readonly List<string> _ids;
        private bool _claiming;

        public UnlockScreen(List<string> featureIds) { _ids = featureIds; }

        public IReadOnlyList<string> FeatureIds => _ids;
        public override string Title => "New unlock";
        public override float HeaderHeight => 0;
        public override bool ShowBack => false;
        public override float FooterHeight => 360;
        /// <summary>The single guided action (tests).</summary>
        public Button Action { get; private set; }

        private FeatureDef Primary => Content.Feature(_ids[0]);

        protected override void Build(RectTransform body)
        {
            var def = Primary;
            var names = new List<string>();
            var rewards = new List<Reward>();
            foreach (var id in _ids)
            {
                var f = Content.Feature(id);
                if (f == null) continue;
                names.Add(f.Name);
                rewards.AddRange(f.UnlockRewards);
                rewards.AddRange(f.UnlockGrants);
            }
            long day = Requirements.AccountAgeDays(State, Content, Now) + 1;
            var kicker = Ui.Label(body, "DAY " + day + " · NEW ON YOUR PLANET", 30, HubKit.Cyan, TextAnchor.MiddleCenter, "Kicker");
            kicker.fontStyle = FontStyle.Bold;
            Ui.Size(kicker, -1, 120);

            var icon = HubKit.Centered(body, 340, 340, 420, "Icon");
            var glow = Ui.Panel(icon, new Color(HubKit.Cyan.r, HubKit.Cyan.g, HubKit.Cyan.b, 0.12f), "Glow");
            glow.sprite = HubKit.Circle; glow.raycastTarget = false;
            Ui.Stretch(glow.rectTransform, -60, -60, -60, -60);
            var frame = HubKit.RoundPanel(icon, HubKit.Cyan, "Frame");
            Ui.Stretch(frame.rectTransform);
            var face = HubKit.RoundPanel(frame.transform, Theme.Hex(0x16324A), "Face");
            Ui.Stretch(face.rectTransform, 9, 9, 9, 9);
            var glyph = Ui.Label(face.transform, HubLayout.FeatureGlyph(_ids[0]), 120, Theme.Hex(0xB8F0FF), TextAnchor.MiddleCenter, "Glyph");
            glyph.fontStyle = FontStyle.Bold;
            Ui.Stretch(glyph.rectTransform);

            var name = Ui.Label(body, string.Join(" + ", names), 76, HubKit.Ink, TextAnchor.MiddleCenter, "FeatureName");
            name.fontStyle = FontStyle.Bold;
            var desc = Ui.Label(body, def?.Description ?? "", 34, HubKit.InkSoft, TextAnchor.MiddleCenter, "Description");
            Ui.Size(desc, -1, 140);

            if (rewards.Count > 0)
            {
                var row = Ui.Row(body, 70, 16, null, 0, "Rewards");
                row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                foreach (var r in rewards)
                    HubKit.Chip(row, (r.Kind == RewardKinds.Currency ? "+" : "") + Fmt.Reward(r, Content), HubKit.Ink, HubKit.Card, -1, 64, "Reward");
            }

            var action = HubLayout.UnlockAction(_ids[0]);
            Action = HubKit.RoundButton(Footer, action.Label, Claim, HubKit.Cyan, HubKit.CyanInk, -1, 170, 50);
            Action.name = "UnlockAction";
            var next = Features.Next(State, Content, Now);
            if (next != null)
                HubKit.Teaser(Footer, null, TeaserLine(next), null, HubKit.CardDeep, "UnlockTeaser");
        }

        /// <summary>"Tomorrow at level 16: Grain Farm" style line for the bottom of the moment.</summary>
        public static string TeaserLine(NextUnlock n)
        {
            string when = n.DaysToWait == 1 ? "Tomorrow" : n.DaysToWait > 1 ? "In " + n.DaysToWait + " days" : "Next";
            string at = n.LevelMet || n.Level <= 0 ? "" : " at level " + n.Level;
            return when + at + ": " + n.Name;
        }

        /// <summary>Claim every pending member of the group (unlock gifts), then open the feature in place of this screen.</summary>
        public void Claim()
        {
            if (_claiming) return;
            _claiming = true;
            var pending = new List<string>();
            foreach (var id in _ids) if (!State.Onboarding.Celebrated.Contains(id) && State.Onboarding.Unlocked.ContainsKey(id)) pending.Add(id);
            ClaimNext(pending, 0);
        }

        private void ClaimNext(List<string> pending, int i)
        {
            if (i >= pending.Count) { Open(); return; }
            Run(new FeatureCelebrate { FeatureId = pending[i] }, r =>
            {
                if (r != null && r.Ok) ClaimNext(pending, i + 1);
                else _claiming = false;
            });
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
