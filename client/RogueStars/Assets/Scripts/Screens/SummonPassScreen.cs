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
    /// Summon event pass: one row per event day, a free and a premium reward each; a day unlocks at every reset of the event.
    /// The premium track is a real-money product (shown as "Coming soon" while purchases are disabled); Claim All takes
    /// every unlocked reward (docs/design/summon-events.md).
    /// </summary>
    public sealed class SummonPassScreen : UiScreen
    {
        public SummonEventType Type;

        public SummonPassScreen() { }
        public SummonPassScreen(SummonEventType type) { Type = type; }

        public override string Title => "Summon Pass";
        public override float HeaderHeight => 0;
        public override bool CoversResourceBar => true;
        public override float FooterHeight => 300;

        protected override void Build(RectTransform body)
        {
            var def = Content.SummonType(Type);
            var ev = SummonEventSystem.CurrentEvent(State, Content, Type, Now);
            var prog = SummonEventSystem.Progress(State, Content, Type, Now);
            BuildFooter(def, prog);
            SummonKit.Background(Root);
            if (def == null || ev == null) { Note(body, "No summon event running."); return; }

            // header art band: the featured one, the title and the next-day countdown
            var head = Ui.RoundPanel(body, SummonKit.Night, 36, "PassHeader");
            Ui.Size(head, -1, 440, 1, 0);
            head.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var back = SummonKit.Img(head.transform, SummonArt.Backdrop(Type), "Backdrop", false);
            Ui.Stretch(back.rectTransform);
            SummonKit.Place(SummonKit.Img(head.transform, SummonArt.Icon("pass"), "Hourglass"), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(170, 210));
            SummonKit.Place(SummonKit.Img(head.transform, SummonArt.Featured(Type, ev.FeaturedId), "Featured"), new Vector2(1, 0), new Vector2(10, 0), new Vector2(330, 330));
            var title = SummonKit.Chunky(head.transform, def.Name + " Pass", 60, Theme.Hex(0xFFD24A), TextAnchor.MiddleCenter, "Title");
            SummonKit.Place(title, new Vector2(0.5f, 0.5f), new Vector2(-10, 60), new Vector2(600, 90));
            var count = SummonKit.Chunky(head.transform, "", 32, Theme.Hex(0xFFE7A0), TextAnchor.MiddleCenter, "Countdown");
            SummonKit.Place(count, new Vector2(0.5f, 0.5f), new Vector2(-10, -20), new Vector2(600, 50));
            Live(count, () => { string n = SummonView.NextPassDay(State, Content, Type, Now); return n.Length > 0 ? n : SummonView.TimeLeft(ev.EndMs, Now); });

            var strip = Ui.Row(body, 80, 0, Theme.Hex(0x5A2848), 0, "Strip");
            var st = SummonKit.Chunky(strip, "Rewards unlock every day of the event!", 34, Color.white, TextAnchor.MiddleCenter, "Rule");
            Ui.Size(st, -1, 80, 1);

            var cols = Ui.Row(body, 120, 0, null, 0, "Tracks");
            ColumnHead(cols, "Free", Theme.Hex(0xBCD8FF), Theme.Hex(0x5A2848));
            ColumnHead(cols, "Premium", Theme.Hex(0xFFD24A), Theme.Hex(0xC8862C));

            int unlocked = SummonEventSystem.PassDaysUnlocked(State, Content, Type, Now);
            bool premium = prog != null && prog.PassPremium;
            foreach (var d in def.Pass)
            {
                var day = d;
                var row = Ui.Row(body, 200, 0, null, 0, "Day:" + day.Day);
                Track(row, day, false, unlocked, prog, Theme.Hex(0x5A2848));
                var mid = Ui.Rect(row, "DayBadge");
                Ui.Size(mid, 90, 200, 0);
                var badge = Ui.Panel(mid, day.Day <= unlocked ? Theme.Hex(0xF2A93C) : Theme.Hex(0x7A5A3C), "Badge");
                badge.sprite = UiShapes.Circle;
                SummonKit.Place(badge, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(78, 78));
                var bt = SummonKit.Chunky(badge.transform, day.Day.ToString(), 40, Color.white, TextAnchor.MiddleCenter, "Number");
                Ui.Stretch(bt.rectTransform);
                Track(row, day, true, unlocked, prog, Theme.Hex(0xC8862C), premium);
            }
        }

        private static void ColumnHead(Transform cols, string text, Color face, Color bg)
        {
            var cell = Ui.Panel(cols, bg, "Head:" + text);
            Ui.Size(cell, -1, 120, 1);
            var pill = Ui.RoundPanel(cell.transform, face, 26, "Pill");
            SummonKit.Place(pill, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 96));
            var t = SummonKit.Chunky(pill.transform, text, 40, Color.white, TextAnchor.MiddleCenter, "Label");
            Ui.Stretch(t.rectTransform);
        }

        private void Track(Transform row, SummonPassDayDef day, bool premiumTrack, int unlocked, SummonEventProgress prog, Color bg, bool premiumOwned = true)
        {
            var cell = Ui.Panel(row, bg, (premiumTrack ? "Premium:" : "Free:") + day.Day);
            Ui.Size(cell, -1, 200, 1);
            var tiles = Ui.Row(cell.transform, 150, 10, null, 0, "Tiles");
            Ui.Stretch(tiles, 10, 25, 10, 25);
            var hl = tiles.GetComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleCenter; hl.childForceExpandWidth = false;
            bool claimed = prog != null && prog.Claimed.Contains(SummonEventSystem.PassKey(day.Day, premiumTrack));
            bool open = day.Day <= unlocked && premiumOwned;
            bool claimable = open && !claimed;
            foreach (var r in premiumTrack ? day.Premium : day.Free)
            {
                var tile = SummonKit.RewardTile(tiles, SummonEventSystem.Resolve(r, prog?.FeaturedId), 140, null, claimed || !open, claimed ? "claimed" : open ? null : "locked");
                if (claimable)
                {
                    var b = tile.gameObject.AddComponent<Button>();
                    int dd = day.Day;
                    b.onClick.AddListener(() => Run(new SummonPassClaim { EventType = Type, Day = dd, Premium = premiumTrack }));
                    UiPulse.On(tile, 0.05f, 1.2f, 1);
                }
            }
        }

        private void BuildFooter(SummonEventTypeDef def, SummonEventProgress prog)
        {
            if (Footer == null) return;
            var fi = Footer.GetComponent<Image>(); if (fi != null) fi.color = SummonKit.Night;
            if (def != null && prog != null && !prog.PassPremium)
            {
                var p = Content.Product(def.PassProductId);
                if (p != null)
                {
                    var buyRow = Ui.Row(Footer, 120, 0, null, 0, "BuyRow");
                    if (!Session.IsOffered(p.Id)) Ui.Button(buyRow, "Premium: coming soon", null, Theme.Button, -1, 110, false, 34).name = "ComingSoon:" + p.Id;
                    else
                    {
                        string block = ShopSystem.PurchaseBlockReason(State, Content, p, Now);
                        SummonKit.ActionButton(buyRow, "Premium " + Fmt.Price(p.PriceUsdCents), null, null, () => Session.Purchase(p.Id), Theme.Hex(0xF2C23C), -1, 110, block == null, "BuyPremium");
                    }
                }
            }
            var row = Ui.Row(Footer, 110, 20, null, 0, "BackRow");
            Ui.Button(row, "<", OnBack, SummonKit.Plum, 130, 110, true, 64).name = "BackButton";
            Ui.Size(Ui.Rect(row, "Spacer"), -1, 10, 1);
            bool any = def != null && SummonEventSystem.ClaimablePassDays(State, Content, Type, Now) > 0;
            SummonKit.ActionButton(row, "Claim All", null, null, () => Run(new SummonPassClaimAll { EventType = Type }), SummonKit.Lime, 320, 110, any, "ClaimAll");
        }
    }
}
