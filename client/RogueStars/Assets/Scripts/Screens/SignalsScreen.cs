using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RogueStars.Client.App;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Gacha: Blueprint / Part / Alien Signals, featured mythic banners with countdown, the visible fixed pity counters ("Mythic x/100",
    /// "Legendary x/40") and the 10-pull rule, published odds, 1×/10× pulls, ad pull, reveal (blueprints / aliens: NEW or +50 shards).
    /// </summary>
    public sealed class SignalsScreen : UiScreen
    {
        private static readonly GachaType[] Types = { GachaType.Part, GachaType.Blueprint, GachaType.Alien };
        private int _tab;
        private bool _standard;

        public override string Title => "Signals";
        public override string[] IntroFeatures => new[] { Features.PartPulls, Features.AlienPulls, Features.Blueprints };

        protected override void Build(RectTransform body)
        {
            var names = new List<string>();
            foreach (var t in Types) names.Add(Content.GachaConfig.Types.TryGetValue(t, out var d) ? d.Name : t.ToString());
            Ui.TabBar(body, names, _tab, i => { _tab = i; Rebuild(); });
            var type = Types[_tab];
            if (!Content.GachaConfig.Types.TryGetValue(type, out var def)) { Note(body, "Not available."); return; }
            string feature = GachaSystem.FeatureFor(type);
            if (!Session.IsUnlocked(feature))
            {
                var f = Content.Feature(feature);
                Note(body, (f?.Name ?? feature) + " unlocks later: " + Requirements.Describe(Content, f?.Requires ?? new RequirementSet()));
                return;
            }

            var banner = GachaSystem.ActiveBanner(Content, type, Now);
            var mode = Ui.Row(body, 76, 10);
            Ui.Button(mode, "Featured banner", () => { _standard = false; Rebuild(); }, !_standard ? Theme.Accent : Theme.Button, -1, 76, banner != null);
            Ui.Button(mode, "Standard", () => { _standard = true; Rebuild(); }, _standard || banner == null ? Theme.Accent : Theme.Button, -1, 76);
            bool useBanner = banner != null && !_standard;

            var card = Card(body, useBanner ? Color.Lerp(Theme.RarityColor(Rarity.Mythic), Theme.Panel, 0.7f) : Theme.PanelAlt);
            if (useBanner)
            {
                var th = banner.Theme;
                Ui.Label(card, th.Name, Theme.FontLarge, Theme.Text, TextAnchor.MiddleCenter);
                Ui.Label(card, "Featured Mythic: " + Fmt.Reward(new Reward(th.FeaturedKind, th.FeaturedId, th.FeaturedAmount, th.FeaturedRarity), Content), Theme.FontNormal, Theme.RarityColor(Rarity.Mythic), TextAnchor.MiddleCenter);
                Live(Ui.Label(card, "", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter), () => "Ends in " + Fmt.Duration(banner.EndMs - Now));
            }
            else Ui.Label(card, def.Name + " — standard pool", Theme.FontLarge, Theme.Text, TextAnchor.MiddleCenter);
            PityRow(card, type, def);
            Ui.Button(card, "Published odds", () => OddsModal(type, useBanner ? banner.Id : null), Theme.Button, -1, 70);

            string bannerId = useBanner ? banner.Id : null;
            long tokens = State.Wallet.Get(def.TokenCurrency);
            Note(body, Fmt.CurrencyName(def.TokenCurrency) + ": " + tokens + " · Diamonds: " + State.Wallet.Get(Currencies.Diamonds));
            var row = Ui.Row(body, 100, 10);
            Ui.Button(row, "1× \n<size=22>" + def.TokenCost1 + " tokens</size>", () => Pull(type, 1, GachaPayment.Tokens, bannerId), Theme.Accent, -1, 100, tokens >= def.TokenCost1, 30);
            Ui.Button(row, "10×\n<size=22>" + def.TokenCost10 + " tokens</size>", () => Pull(type, 10, GachaPayment.Tokens, bannerId), Theme.Accent, -1, 100, tokens >= def.TokenCost10, 30);
            var row2 = Ui.Row(body, 100, 10);
            long dia = State.Wallet.Get(Currencies.Diamonds);
            Ui.Button(row2, "1× \n<size=22>" + def.DiamondCost1 + " DIA</size>", () => Pull(type, 1, GachaPayment.Diamonds, bannerId), Theme.Premium, -1, 100, dia >= def.DiamondCost1, 30);
            Ui.Button(row2, "10×\n<size=22>" + def.DiamondCost10 + " DIA</size>", () => Pull(type, 10, GachaPayment.Diamonds, bannerId), Theme.Premium, -1, 100, dia >= def.DiamondCost10, 30);
            if (!string.IsNullOrEmpty(def.AdPlacementId))
            {
                int left = ShopSystem.AdViewsLeft(State, Content, def.AdPlacementId, Now);
                Ui.Button(body, "Free pull (ad) — " + left + " left today", () => Session.Ads.Show(def.AdPlacementId, ad =>
                {
                    if (ad.Ok) Pull(type, 1, GachaPayment.Ad, bannerId, ad.Value);
                }), Theme.Good, -1, Theme.ButtonHeight, left > 0);
            }
        }

        /// <summary>
        /// The visible, fixed pity of the pull type: "Mythic 37/100" and "Legendary 12/40" (rarity colours), and the 10-pull rule.
        /// Nothing else changes the odds (docs/design/pull-system.md).
        /// </summary>
        public static RectTransform PityRow(Transform parent, GachaType type, GachaTypeDef def, GameState state, ContentDb content)
        {
            var pity = GachaSystem.Pity(state, content, type);
            var row = Ui.Row(parent, 56, 12, null, 0, "Pity");
            row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            if (pity.MythicPity > 0)
                Ui.Label(row, "Mythic " + pity.Mythic + "/" + pity.MythicPity, Theme.FontNormal, Theme.RarityColor(Rarity.Mythic), TextAnchor.MiddleCenter).name = "PityMythic";
            if (pity.LegendaryPity > 0)
                Ui.Label(row, "Legendary " + pity.Legendary + "/" + pity.LegendaryPity, Theme.FontNormal, Theme.RarityColor(Rarity.Legendary), TextAnchor.MiddleCenter).name = "PityLegendary";
            var rule = Ui.Label(parent, "Guaranteed Mythic by pull " + pity.MythicPity + ", Legendary by pull " + pity.LegendaryPity + " (fixed, never changes)."
                + (def.TenPullGuarantee ? " Every 10× pull has at least one " + def.TenPullMinTier + "." : ""), Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter);
            rule.name = "PityRule";
            return row;
        }

        private void PityRow(Transform parent, GachaType type, GachaTypeDef def) => PityRow(parent, type, def, State, Content);

        private void Pull(GachaType type, int count, GachaPayment pay, string bannerId, string adToken = null)
        {
            Run(new GachaPull { PullType = type, Count = count, Payment = pay, BannerId = bannerId, AdToken = adToken }, r =>
            {
                if (!r.Ok) return;
                var result = r.Payload as GachaPullResult ?? (r.Payload as JToken)?.ToObject<GachaPullResult>(ClientJson.WireSerializer);
                if (result != null) Reveal(result);
            });
        }

        /// <summary>Reveal modal: cards pop in one by one (instant in edit mode).</summary>
        public UiModal Reveal(GachaPullResult result)
        {
            var cards = new List<RectTransform>();
            var modal = Modal(result.Items.Count > 1 ? "10× Signal" : "Signal", b =>
            {
                var grid = Ui.Grid(b, new Vector2(170, 170), 5, 8);
                foreach (var it in result.Items)
                {
                    var color = Theme.RarityColor(it.Tier);
                    var c = Ui.Panel(grid, Color.Lerp(color, Theme.Panel, 0.35f), "Card");
                    var pulled = new Reward(it.Kind, it.Id, it.Amount, it.Rarity);
                    // A pull result is always shown (published odds); one without a use yet waits in the reward stash.
                    string kept = RewardGate.HasUse(State, Content, pulled, Now) ? "" : "\n<size=16>kept for later</size>";
                    // an alien shows its sprite (ArtCatalog) over the name
                    var art = it.Kind == RewardKinds.Alien ? ArtCatalog.Alien(it.Id) : null;
                    if (art != null)
                    {
                        var img = Ui.Panel(c.transform, Color.white, "AlienSprite");
                        img.sprite = art;
                        img.preserveAspect = true;
                        img.raycastTarget = false;
                        var irt = img.rectTransform;
                        irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1);
                        irt.pivot = new Vector2(0.5f, 1);
                        irt.sizeDelta = new Vector2(96, 96);
                        irt.anchoredPosition = new Vector2(0, -4);
                    }
                    // a blueprint / alien is a batch of 50 shards: "NEW" when it unlocked the item, else "+50 shards"
                    string batch = it.Shards > 0 ? "\n<size=18>" + (it.Note == "new" ? "NEW" : "+" + Fmt.Num(it.Shards) + " shards") + "</size>" : "";
                    var t = Ui.Label(c.transform, (it.Featured ? "FEATURED\n" : "") + Fmt.Reward(pulled, Content) + batch + "\n<size=18>" + it.Tier + "</size>" + kept, art != null ? 17 : 20, Color.white, art != null ? TextAnchor.LowerCenter : TextAnchor.MiddleCenter);
                    Ui.Stretch(t.rectTransform, 6, art != null ? 96 : 6, 6, 6);
                    cards.Add(c.rectTransform);
                    if (Application.isPlaying) c.gameObject.SetActive(false);
                }
            });
            if (Application.isPlaying)
            {
                var seq = UiSequence.On(modal.Root);
                foreach (var c in cards)
                {
                    var card = c;
                    seq.Wait(0.12f, () => { if (card != null) { card.gameObject.SetActive(true); UiTween.PopIn(card, 0.2f); } });
                }
            }
            return modal;
        }

        private void OddsModal(GachaType type, string bannerId)
        {
            var odds = GachaSystem.GetOdds(Content, type, bannerId, Now);
            Modal("Published odds", b =>
            {
                foreach (var tier in odds.Tiers)
                {
                    Ui.Label(b, tier.Tier + ": " + Fmt.Odds(tier.Percent), Theme.FontNormal, Theme.RarityColor(tier.Tier));
                    var items = new List<string>();
                    foreach (var it in tier.Items)
                    {
                        if (items.Count >= 6) { items.Add("…"); break; }
                        items.Add(Fmt.Reward(new Reward(it.Kind, it.Id, it.Amount, it.Rarity), Content) + (it.Featured ? " (featured)" : "") + " " + Fmt.Odds(it.Percent));
                    }
                    Ui.Label(b, string.Join(", ", items), 20, Theme.TextDim);
                }
                Note(b, "Fixed pity: the " + odds.MythicPity + "th pull without a Mythic is a Mythic; the " + odds.LegendaryPity
                    + "th without a Legendary or better is at least Legendary. No other pity, nothing hidden.");
                if (odds.TenPullGuarantee) Note(b, "10× pull: at least one " + odds.TenPullMinTier + " or better.");
                if (odds.BatchShards > 0 && type != GachaType.Part) Note(b, "A blueprint or alien is " + odds.BatchShards + " shards: the first unlocks it.");
            });
        }
    }
}
