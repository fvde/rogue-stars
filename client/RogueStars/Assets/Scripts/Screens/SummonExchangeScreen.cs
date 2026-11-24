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
    /// Summon event exchange shop: the type's exchange tokens (earned per draw, kept between events) buy featured shards,
    /// tickets and supplies, each limited per event (docs/design/summon-events.md).
    /// </summary>
    public sealed class SummonExchangeScreen : UiScreen
    {
        public SummonEventType Type;

        public SummonExchangeScreen() { }
        public SummonExchangeScreen(SummonEventType type) { Type = type; }

        public override string Title => "Exchange Shop";
        public override float HeaderHeight => 0;
        public override bool CoversResourceBar => true;
        public override float FooterHeight => 150;

        protected override void Build(RectTransform body)
        {
            var def = Content.SummonType(Type);
            var ev = SummonEventSystem.CurrentEvent(State, Content, Type, Now);
            var prog = SummonEventSystem.Progress(State, Content, Type, Now);
            if (Footer != null)
            {
                var fi = Footer.GetComponent<Image>(); if (fi != null) fi.color = SummonKit.Night;
                SummonKit.Background(Root);
                var row = Ui.Row(Footer, 110, 0, null, 0, "BackRow");
                row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                Ui.Button(row, "<", OnBack, SummonKit.Plum, 130, 110, true, 64).name = "BackButton";
            }
            if (def == null || ev == null) { Note(body, "No summon event running."); return; }
            SummonKit.TopBar(body, SummonView.FeaturedName(Content, Type, ev.FeaturedId), null,
                ("diamond", State.Wallet.Get(Currencies.Diamonds), "diamonds"),
                (SummonArt.IconOfCurrency(def.TokenCurrency), State.Wallet.Get(def.TokenCurrency), "tokens"),
                (SummonArt.IconOfCurrency(def.TicketCurrency), State.Wallet.Get(def.TicketCurrency), "tickets"));
            SummonKit.Banner(body, "Exchange Shop", "exchange", 260);
            Note(body, Fmt.CurrencyName(def.TokenCurrency, Content) + ": +" + def.TokensPerPull + " per draw, kept between events. Limits reset with every event.");
            float cell = Mathf.Floor((Mathf.Max(640, BodyFrame.rect.width - 48) - 2 * 16) / 3);
            var grid = Ui.Grid(body, new Vector2(cell, 450), 3, 16);
            foreach (var x in def.Exchange)
            {
                var item = x;
                var reward = SummonEventSystem.Resolve(item.Reward, ev.FeaturedId);
                int bought = SummonEventSystem.ExchangeBought(prog, item.Id);
                bool soldOut = item.EventLimit > 0 && bought >= item.EventLimit;
                bool afford = State.Wallet.Get(def.TokenCurrency) >= item.Price;
                var card = Ui.RoundPanel(grid, SummonKit.Night, 28, "Item:" + item.Id);
                UiShapes.Outline(card.rectTransform, SummonKit.PlumLight, 28, 5);
                var title = SummonKit.Chunky(card.transform, ItemTitle(item, reward), item.Name.Length > 16 ? 28 : 34, Color.white, TextAnchor.MiddleCenter, "Name");
                SummonKit.Place(title, new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(cell - 16, 76));
                var tile = SummonKit.RewardTile(card.transform, reward, 160, null, soldOut, null);
                SummonKit.Place(tile, new Vector2(0.5f, 1), new Vector2(0, -88), new Vector2(160, 160));
                var limit = Ui.Label(card.transform, SummonView.LimitText(prog, item), 26, Theme.Hex(0xC9B8FF), TextAnchor.MiddleCenter, "Limit");
                limit.fontStyle = FontStyle.Bold;
                SummonKit.Place(limit, new Vector2(0.5f, 1), new Vector2(0, -256), new Vector2(cell - 10, 40));
                var buy = SummonKit.ActionButton(card.transform, "", SummonArt.IconOfCurrency(def.TokenCurrency), Fmt.Num(item.Price),
                    () => Run(new SummonExchangeBuy { EventType = Type, ItemId = item.Id }), SummonKit.Lime, -1, 110, !soldOut && afford, "Buy:" + item.Id);
                SummonKit.Place(buy, new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(cell - 36, 110));
                if (soldOut) { var c = buy.GetComponentsInChildren<Text>(); if (c.Length > 0) c[c.Length - 1].text = "Sold out"; }
            }
        }

        private string ItemTitle(SummonExchangeItemDef item, Reward reward) =>
            reward.Kind == RewardKinds.HeroShards || (reward.Kind == RewardKinds.Currency && reward.Id.StartsWith("shards_"))
                ? Fmt.CurrencyName(reward.Kind == RewardKinds.HeroShards ? Currencies.HeroShards(reward.Id) : reward.Id, Content)
                : item.Name;
    }
}
