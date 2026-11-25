using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RogueStars.Client.App;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
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

            // SG2: the banner card — featured art, light text on the rarity red, pity as two bars
            var mythicRed = Theme.RarityColor(Rarity.Mythic);
            var card = Card(body, useBanner ? Color.Lerp(mythicRed, Theme.Panel, 0.45f) : Theme.PanelAlt);
            card.name = "BannerCard";
            if (useBanner)
            {
                var th = banner.Theme;
                Ui.Display(card, th.Name, Theme.FontLarge, Theme.Text, TextAnchor.MiddleCenter, "BannerName");
                FeaturedArt(card, th.FeaturedKind, th.FeaturedId);
                Ui.Display(card, "Featured: " + FeaturedName(th.FeaturedKind, th.FeaturedId), Theme.FontNormal, Theme.Cream, TextAnchor.MiddleCenter, "Featured");
                Live(Ui.Label(card, "", Theme.FontSmall, Theme.Cream, TextAnchor.MiddleCenter, "EndsIn"), () => "Ends in " + Fmt.Duration(banner.EndMs - Now));
            }
            else
            {
                Ui.Display(card, def.Name + " — standard pool", Theme.FontLarge, Theme.Text, TextAnchor.MiddleCenter, "BannerName");
                FeaturedArt(card, null, null);
            }
            PityRow(card, type, def);
            Ui.Button(card, "Published odds", () => OddsModal(type, useBanner ? banner.Id : null), Theme.Button, -1, 70);

            string bannerId = useBanner ? banner.Id : null;
            long tokens = State.Wallet.Get(def.TokenCurrency);
            long dia = State.Wallet.Get(Currencies.Diamonds);
            var row = Ui.Row(body, 130, 14, null, 0, "TokenPulls");
            PullButton(row, "1×", SummonArt.IconOfCurrency(def.TokenCurrency), TokenCost(def.TokenCost1), tokens >= def.TokenCost1, () => Pull(type, 1, GachaPayment.Tokens, bannerId), Theme.Accent, "Pull:1:tokens");
            PullButton(row, "10×", SummonArt.IconOfCurrency(def.TokenCurrency), TokenCost(def.TokenCost10), tokens >= def.TokenCost10, () => Pull(type, 10, GachaPayment.Tokens, bannerId), Theme.Accent, "Pull:10:tokens");
            var row2 = Ui.Row(body, 130, 14, null, 0, "DiamondPulls");
            PullButton(row2, "1×", "diamond", Fmt.Num(def.DiamondCost1), dia >= def.DiamondCost1, () => Pull(type, 1, GachaPayment.Diamonds, bannerId), Theme.Premium, "Pull:1:diamonds");
            PullButton(row2, "10×", "diamond", Fmt.Num(def.DiamondCost10), dia >= def.DiamondCost10, () => Pull(type, 10, GachaPayment.Diamonds, bannerId), Theme.Premium, "Pull:10:diamonds");
            if (!string.IsNullOrEmpty(def.AdPlacementId))
            {
                int left = ShopSystem.AdViewsLeft(State, Content, def.AdPlacementId, Now);
                Ui.Button(body, "Free pull (ad) — " + left + " left today", () => Session.Ads.Show(def.AdPlacementId, ad =>
                {
                    if (ad.Ok) Pull(type, 1, GachaPayment.Ad, bannerId, ad.Value);
                }), Theme.Good, -1, Theme.ButtonHeight, left > 0);
            }
        }

        /// <summary>"1 token" / "10 tokens".</summary>
        public static string TokenCost(long n) => Fmt.Num(n) + (n == 1 ? " token" : " tokens");

        private static void PullButton(Transform row, string caption, string icon, string cost, bool can, System.Action onClick, Color face, string name)
        {
            var b = SummonKit.ActionButton(row, caption, icon, cost, onClick, face, -1, 130, can, name);
            if (!can)
            {
                // the cost turns red when the balance is short (SM2/SG2: show the price, not have/need)
                foreach (var t in b.GetComponentsInChildren<UnityEngine.UI.Text>(true)) if (t.name == "Count") t.color = Theme.Bad;
            }
        }

        /// <summary>The featured item's name, without its rarity ("Plasma Blaster").</summary>
        private string FeaturedName(string kind, string id)
        {
            if (kind == RewardKinds.Part && id != null && Content.PartsConfig.Defs.TryGetValue(id, out var pd)) return pd.Name;
            if (kind == RewardKinds.Alien) return Content.AlienType(id)?.Name ?? id;
            if (kind == RewardKinds.Blueprint) return Content.Blueprint(id)?.Name ?? id;
            return Fmt.Reward(new Reward(kind, id, 1), Content);
        }

        /// <summary>The featured art on the banner: the alien's sprite when it has one, else a glowing mythic emblem (placeholder).</summary>
        private void FeaturedArt(Transform card, string kind, string id)
        {
            var holder = Ui.Rect(card, "FeaturedArt");
            Ui.Size(holder, -1, 300, 1, 0);
            var glow = Ui.Panel(holder, new Color(1f, 0.55f, 0.55f, kind != null ? 0.35f : 0.15f), "Glow");
            glow.sprite = UiShapes.Circle;
            glow.raycastTarget = false;
            SummonKit.Place(glow, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(290, 290));
            UiPulse.On(glow, 0.06f, 1.8f, 1);
            var art = kind == RewardKinds.Alien ? ArtCatalog.Alien(id) : null;
            var img = SummonKit.Img(holder, art != null ? art : SummonArt.Icon(kind == null ? "token" : "star"), "FeaturedSprite");
            SummonKit.Place(img, new Vector2(0.5f, 0.5f), Vector2.zero, art != null ? new Vector2(280, 280) : new Vector2(200, 200));
            UiBob.On(img.rectTransform, 8, 2.4f);
        }

        /// <summary>
        /// The visible, fixed pity of the pull type: "Mythic 37/100" and "Legendary 12/40" (rarity colours) as two progress bars,
        /// and the 10-pull rule. Nothing else changes the odds (docs/design/pull-system.md).
        /// </summary>
        public static RectTransform PityRow(Transform parent, GachaType type, GachaTypeDef def, GameState state, ContentDb content)
        {
            var pity = GachaSystem.Pity(state, content, type);
            var col = Ui.Column(parent, 6, 14, new Color(0.08f, 0.04f, 0.06f, 0.75f), "Pity");
            if (pity.MythicPity > 0) PityBar(col, "PityMythic", "Mythic " + pity.Mythic + "/" + pity.MythicPity, (float)pity.Mythic / pity.MythicPity, Rarity.Mythic);
            if (pity.LegendaryPity > 0) PityBar(col, "PityLegendary", "Legendary " + pity.Legendary + "/" + pity.LegendaryPity, (float)pity.Legendary / pity.LegendaryPity, Rarity.Legendary);
            var rule = Ui.Label(parent, "Guaranteed Mythic by pull " + pity.MythicPity + ", Legendary by pull " + pity.LegendaryPity + " (fixed, never changes)."
                + (def.TenPullGuarantee ? " Every 10× pull has at least one " + def.TenPullMinTier + "." : ""), Theme.FontSmall, Theme.Cream, TextAnchor.MiddleCenter);
            rule.name = "PityRule";
            return col;
        }

        private static void PityBar(Transform parent, string name, string text, float value, Rarity r)
        {
            var row = Ui.Row(parent, 44, 12, null, 0, name + "Row");
            var label = Ui.Display(row, text, Theme.FontSmall, Theme.RarityColor(r), TextAnchor.MiddleLeft, name);
            Ui.Size(label, 270, 44, 0);
            var bar = Ui.Bar(row, value, Theme.RarityColor(r), null, 30);
            bar.Fill.parent.name = name + "Bar";
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
