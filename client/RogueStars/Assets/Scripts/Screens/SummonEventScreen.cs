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
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Summon event hub (docs/design/summon-events.md): the featured hero / mount centre stage under a spotlight, time left,
    /// currencies, the Luck meter with its rule, the pull-count milestone track, Skip Animation, 1× / 10× Draw (tickets, or
    /// Diamonds when tickets are short), and the side column: Exchange Shop, Packs, Pass, Tasks with badges. Hero and mount
    /// events switch with the tabs under the top bar. Opened from the hub's left column ("Summon").
    /// </summary>
    public sealed class SummonEventScreen : UiScreen
    {
        /// <summary>Skip the card-by-card reveal of draws (kept for the app session).</summary>
        public static bool SkipAnimation;

        public SummonEventType Type = SummonEventType.Hero;

        public SummonEventScreen() { }
        public SummonEventScreen(SummonEventType type) { Type = type; }

        public override string Title => "Summon";
        public override float HeaderHeight => 0;
        public override bool CoversResourceBar => true;
        public override float FooterHeight => 400;

        private SummonEventTypeDef Def => Content.SummonType(Type);

        public override void OnShow()
        {
            var avail = SummonView.Available(State, Content);
            if (avail.Count > 0 && !avail.Contains(Type)) { Type = avail[0]; Rebuild(); }
        }

        protected override void Build(RectTransform body)
        {
            var def = Def;
            var ev = SummonEventSystem.CurrentEvent(State, Content, Type, Now);
            var prog = SummonEventSystem.Progress(State, Content, Type, Now);
            if (Footer != null) { var fi = Footer.GetComponent<Image>(); if (fi != null) fi.color = SummonKit.Night; }
            SummonKit.Background(Root);
            if (def == null || ev == null)
            {
                SummonKit.TopBar(body, "Summon", OnBack);
                Note(body, "Summon events start with the Heroes unlock.");
                BuildFooter(null, null);
                return;
            }
            string featuredName = SummonView.FeaturedName(Content, Type, ev.FeaturedId);
            SummonKit.TopBar(body, featuredName, null,
                ("diamond", State.Wallet.Get(Currencies.Diamonds), "diamonds"),
                (SummonArt.IconOfCurrency(def.TokenCurrency), State.Wallet.Get(def.TokenCurrency), "tokens"),
                (SummonArt.IconOfCurrency(def.TicketCurrency), State.Wallet.Get(def.TicketCurrency), "tickets"));
            var avail = SummonView.Available(State, Content);
            if (avail.Count > 1) TypeTabs(body, avail);
            float stageH = Mathf.Max(1150, BodyFrame.rect.height - 112 - (avail.Count > 1 ? 76 + 18 : 0) - 18 - 48);
            float stageW = Mathf.Max(640, BodyFrame.rect.width - 48);
            Stage(body, def, ev, prog, stageW, stageH);
            BuildFooter(def, ev);
        }

        private void TypeTabs(RectTransform body, List<SummonEventType> avail)
        {
            var row = Ui.Row(body, 76, 10, null, 0, "TypeTabs");
            foreach (var t in avail)
            {
                var tt = t;
                var face = tt == Type ? SummonArt.Accent(tt) : SummonKit.Plum;
                Ui.Button(row, Content.SummonType(tt).Name, () => { Type = tt; Rebuild(); }, face, -1, 76, true, 30).name = "Tab:" + tt;
            }
        }

        // ---- stage ---------------------------------------------------------------------------------------------

        private void Stage(RectTransform body, SummonEventTypeDef def, SummonEventInfo ev, SummonEventProgress prog, float width, float height)
        {
            var stage = Ui.RoundPanel(body, SummonKit.Night, 36, "Stage");
            Ui.Size(stage, -1, height, 1, 0);
            var mask = stage.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;
            var back = SummonKit.Img(stage.transform, SummonArt.Backdrop(Type), "Backdrop", false);
            Ui.Stretch(back.rectTransform);
            var spot = SummonKit.Img(stage.transform, SummonArt.Icon("spotlight"), "Spotlight", false);
            SummonKit.Place(spot, new Vector2(0.5f, 1), Vector2.zero, new Vector2(820, height * 0.82f));

            // the featured one, big, centre stage
            float art = Mathf.Min(Type == SummonEventType.Hero ? 820 : 640, Mathf.Min(width - 260, height * 0.56f) + (Type == SummonEventType.Hero ? 120 : 0));
            var featured = SummonKit.Img(stage.transform, SummonArt.Featured(Type, ev.FeaturedId), "Featured");
            SummonKit.Place(featured, new Vector2(0.5f, 1), new Vector2(-90, Type == SummonEventType.Hero ? -150 : -260), new Vector2(art, art));
            UiBob.On(featured.rectTransform, 10, 2.6f);

            // time left + what the player owns of it
            var time = SummonKit.Chunky(stage.transform, "", 34, Color.white, TextAnchor.MiddleLeft, "TimeLeft");
            SummonKit.Place(time, new Vector2(0, 1), new Vector2(30, -24), new Vector2(620, 50));
            Live(time, () => SummonView.TimeLeft(ev.EndMs, Now));
            var owned = SummonKit.Chunky(stage.transform, OwnedLine(ev.FeaturedId), 28, Theme.Hex(0xE8DAFF), TextAnchor.MiddleLeft, "Owned");
            SummonKit.Place(owned, new Vector2(0, 1), new Vector2(30, -74), new Vector2(620, 44));

            // side column
            var side = Ui.Column(stage.transform, 22, 12, new Color(0.1f, 0.06f, 0.2f, 0.72f), "SideColumn");
            SummonKit.Place(side, new Vector2(1, 1), new Vector2(-22, -190), new Vector2(190, 4 * 206 + 24));
            side.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            side.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            SummonKit.SideButton(side, "exchange", "Exchange", 0, () => App.Router.Push(new SummonExchangeScreen(Type)), 160);
            SummonKit.SideButton(side, "packs", "Packs", SummonView.PacksBadge(State, Content, Type, Now), () => PacksSheet(), 160);
            SummonKit.SideButton(side, "pass", "Pass", SummonView.PassBadge(State, Content, Type, Now), () => App.Router.Push(new SummonPassScreen(Type)), 160);
            SummonKit.SideButton(side, "tasks", "Tasks", SummonView.TaskBadge(State, Content, Type, Now), () => TasksSheet(), 160);

            // Luck gem, meter and rule
            float luckY = 330;
            var gem = SummonKit.Img(stage.transform, SummonArt.Icon("luck_gem"), "LuckGem");
            SummonKit.Place(gem, new Vector2(0.5f, 0), new Vector2(0, luckY + 120), new Vector2(170, 200));
            UiPulse.On(gem, 0.05f, 1.6f, 1);
            var luckRow = Ui.Row(stage.transform, 64, 14, null, 0, "LuckRow");
            SummonKit.Place(luckRow, new Vector2(0.5f, 0), new Vector2(0, luckY + 50), new Vector2(700, 64));
            var lh = luckRow.GetComponent<HorizontalLayoutGroup>();
            lh.childAlignment = TextAnchor.MiddleCenter; lh.childForceExpandWidth = false;
            Ui.Button(luckRow, "?", LuckInfo, Theme.Hex(0xD8D2F0), 60, 60, true, 36).name = "LuckInfo";
            int luck = SummonEventSystem.Luck(State, Type), max = SummonEventSystem.LuckMax(Content, Type);
            var luckText = SummonKit.Chunky(luckRow, "Luck <color=#FFD24A>" + luck + "</color>/" + max, 50, Color.white, TextAnchor.MiddleLeft, "Luck");
            luckText.supportRichText = true;
            luckText.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Size(luckText, -1, 64, 0);
            var rule = SummonKit.Chunky(stage.transform, SummonView.LuckRule(Type), 30, Color.white, TextAnchor.MiddleCenter, "LuckRule");
            SummonKit.Place(rule, new Vector2(0.5f, 0), new Vector2(0, luckY - 50), new Vector2(width - 60, 96));

            // milestone track
            Milestones(stage.transform, def, prog, width);
        }

        private string OwnedLine(string id)
        {
            if (Type == SummonEventType.Hero)
            {
                var h = HeroSystem.Get(State, id);
                return h == null ? "Not in your crew yet" : "Yours · " + Fmt.Stars(h.Stars);
            }
            var m = MountSystem.Get(State, id);
            return m == null ? "Not in your hangar yet" : "Yours · " + Fmt.Stars(m.Stars) + (m.Awaken > 0 ? " · Awakened " + m.Awaken : "");
        }

        private void Milestones(Transform stage, SummonEventTypeDef def, SummonEventProgress prog, float width)
        {
            var row = Ui.Row(stage, 170, 0, null, 0, "Milestones");
            SummonKit.Place(row, new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(width - 40, 170));
            float tileSize = Mathf.Min(110, (width - 40) / Mathf.Max(1, def.Milestones.Count) - 50);
            var hl = row.GetComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleCenter; hl.childForceExpandWidth = true; hl.childControlWidth = false;
            int pulls = prog?.Pulls ?? 0;
            for (int i = 0; i < def.Milestones.Count; i++)
            {
                var m = def.Milestones[i];
                var reward = SummonEventSystem.Resolve(m.Rewards[0], prog?.FeaturedId);
                bool claimed = prog != null && prog.Claimed.Contains(SummonEventSystem.MilestoneKey(m.Pulls));
                bool ready = SummonEventSystem.MilestoneClaimable(prog, m);
                var tile = SummonKit.RewardTile(row, reward, tileSize, System.Math.Min(pulls, m.Pulls) + "/" + m.Pulls, claimed, claimed ? "claimed" : null,
                    ready ? Theme.Hex(0x7BE08A) : (Color?)null);
                tile.name = "Milestone:" + m.Pulls;
                if (ready)
                {
                    var b = tile.gameObject.AddComponent<Button>();
                    var mm = m;
                    b.onClick.AddListener(() => Run(new SummonMilestoneClaim { EventType = Type, Pulls = mm.Pulls }));
                    UiPulse.On(tile, 0.06f, 1.2f, 1);
                }
            }
        }

        // ---- footer: skip animation, draws, back ------------------------------------------------------------------

        private void BuildFooter(SummonEventTypeDef def, SummonEventInfo ev)
        {
            if (Footer == null) return;
            var skip = Ui.Row(Footer, 60, 14, null, 0, "SkipRow");
            var sh = skip.GetComponent<HorizontalLayoutGroup>();
            sh.childAlignment = TextAnchor.MiddleCenter; sh.childForceExpandWidth = false;
            var box = Ui.Button(skip, "", () => { SkipAnimation = !SkipAnimation; Rebuild(); }, Theme.Hex(0xF4F0FF), 56, 56, true);
            box.name = "SkipAnimation";
            if (SkipAnimation) SummonKit.Place(SummonKit.Img(box.targetGraphic.transform, SummonArt.Icon("check"), "Check"), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 56));
            var st = SummonKit.Chunky(skip, "Skip Animation", 40, Color.white, TextAnchor.MiddleLeft, "SkipLabel");
            st.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Size(st, -1, 60, 0);

            var draws = Ui.Row(Footer, 150, 30, null, 0, "Draws");
            draws.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(60, 60, 0, 0);
            if (def != null && ev != null)
            {
                DrawButton(draws, def, ev, 1);
                DrawButton(draws, def, ev, 10);
            }

            var bottom = Ui.Row(Footer, 110, 20, null, 0, "BackRow");
            bottom.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            Ui.Button(bottom, "<", OnBack, SummonKit.Plum, 130, 110, true, 64).name = "BackButton";
            if (ev != null)
            {
                var next = SummonEventSystem.NextEvent(State, Content, Type, Now);
                var nt = Ui.Label(bottom, next == null ? "" : "Next: " + SummonView.FeaturedName(Content, Type, next.FeaturedId), Theme.FontSmall, Theme.Hex(0xB8A8E0), TextAnchor.MiddleRight, "Next");
                Ui.Size(nt, -1, 110, 1);
            }
        }

        private void DrawButton(Transform row, SummonEventTypeDef def, SummonEventInfo ev, int count)
        {
            bool tickets = SummonView.PaysWithTickets(State, def, count);
            long need = count == 10 ? def.Gacha.TokenCost10 : def.Gacha.TokenCost1;
            long dia = SummonView.DiamondCost(def, count);
            bool can = tickets || State.Wallet.Get(Currencies.Diamonds) >= dia;
            string caption = count + "× Draw";
            var face = tickets ? SummonKit.Lime : Theme.Premium;
            SummonKit.ActionButton(row, caption, tickets ? SummonArt.IconOfCurrency(def.TicketCurrency) : "diamond",
                tickets ? State.Wallet.Get(def.TicketCurrency) + "/" + need : Fmt.Num(dia),
                () => Pull(count, tickets ? SummonPayment.Tickets : SummonPayment.Diamonds, ev.Id), face, -1, 150, can, "Draw:" + count);
        }

        public void Pull(int count, SummonPayment pay, string eventId)
        {
            Run(new SummonPull { EventType = Type, Count = count, Payment = pay, EventId = eventId }, r =>
            {
                if (!r.Ok) return;
                var result = r.Payload as SummonPullResult ?? (r.Payload as JToken)?.ToObject<SummonPullResult>(ClientJson.WireSerializer);
                if (result != null) Reveal(result);
            });
        }

        // ---- draw result ------------------------------------------------------------------------------------------

        /// <summary>The draw result: one card per pull (rarity frame, icon, amount, NEW / ★n on copies), Luck and tokens.</summary>
        public UiModal Reveal(SummonPullResult result)
        {
            var def = Def;
            var cards = new List<RectTransform>();
            var modal = Ui.Sheet(App.ModalLayer, "SummonResult", SummonKit.Night, SummonArt.Accent(Type), (box, m) =>
            {
                SummonKit.Chunky(box, result.Items.Count > 1 ? "Summon ×" + result.Items.Count : "Summon", 56, Color.white, TextAnchor.MiddleCenter, "Title");
                var grid = Ui.Grid(box, new Vector2(164, 250), 5, 12);
                foreach (var it in result.Items)
                {
                    var card = Card(grid, it);
                    cards.Add(card);
                    if (Application.isPlaying && !SkipAnimation) card.gameObject.SetActive(false);
                }
                var info = Ui.Row(box, 60, 10, null, 0, "Info");
                info.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                var luck = SummonKit.Chunky(info, (result.LuckTriggered ? "Luck full — guaranteed! · " : "") + "Luck " + result.Luck + "/" + result.LuckMax
                    + "   +" + Fmt.Num(result.Tokens) + " " + Fmt.CurrencyName(def?.TokenCurrency, Content), 32, Color.white, TextAnchor.MiddleCenter, "LuckAfter");
                luck.horizontalOverflow = HorizontalWrapMode.Overflow;
                var buttons = Ui.Row(box, 110, 20, null, 0, "Buttons");
                Ui.Button(buttons, "OK", m.Close, SummonKit.Plum, -1, 110, true, 40);
                if (def != null && result.Items.Count > 0)
                {
                    int count = result.Items.Count;
                    bool tickets = SummonView.PaysWithTickets(State, def, count);
                    bool can = tickets || State.Wallet.Get(Currencies.Diamonds) >= SummonView.DiamondCost(def, count);
                    SummonKit.ActionButton(buttons, "Again ×" + count, tickets ? SummonArt.IconOfCurrency(def.TicketCurrency) : "diamond",
                        tickets ? State.Wallet.Get(def.TicketCurrency) + "/" + (count == 10 ? def.Gacha.TokenCost10 : def.Gacha.TokenCost1) : Fmt.Num(SummonView.DiamondCost(def, count)),
                        () => { m.Close(); Pull(count, tickets ? SummonPayment.Tickets : SummonPayment.Diamonds, result.EventId); }, tickets ? SummonKit.Lime : Theme.Premium, -1, 110, can, "Again");
                }
            }, 960);
            if (Application.isPlaying && !SkipAnimation)
            {
                var seq = UiSequence.On(modal.Root);
                foreach (var c in cards)
                {
                    var card = c;
                    seq.Wait(0.1f, () => { if (card != null) { card.gameObject.SetActive(true); UiTween.PopIn(card, 0.2f); } });
                }
            }
            return modal;
        }

        private RectTransform Card(Transform grid, SummonPullItem it)
        {
            var color = Theme.RarityColor(it.Tier);
            var card = Ui.RoundPanel(grid, Color.Lerp(color, SummonKit.Night, 0.25f), 26, "Card:" + it.Kind + ":" + it.Id);
            UiShapes.Outline(card.rectTransform, Color.Lerp(color, Color.white, 0.3f), 26, 4);
            var reward = new Reward(it.Kind, it.Id, it.Amount);
            var icon = SummonKit.Img(card.transform, SummonArt.RewardIcon(reward), "Icon");
            SummonKit.Place(icon, new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(130, 130));
            string amount = SummonView.Amount(reward);
            if (amount.Length > 0) SummonKit.Place(SummonKit.Chunky(card.transform, amount, 30, Color.white, TextAnchor.MiddleRight, "Amount"), new Vector2(1, 1), new Vector2(-8, -108), new Vector2(150, 40));
            var name = SummonKit.Chunky(card.transform, SummonView.RewardName(reward, Content), 22, Color.white, TextAnchor.UpperCenter, "Name");
            SummonKit.Place(name, new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(154, 96));
            string badge = SummonView.CopyBadge(it, Content);
            if (badge.Length > 0 || it.Featured)
            {
                var b = Ui.RoundPanel(card.transform, badge.Length > 0 ? SummonKit.BadgeRed : Theme.Hex(0x8A4CE0), 14, "Badge");
                SummonKit.Place(b, new Vector2(0.5f, 1), new Vector2(0, 10), new Vector2(150, 38));
                var bt = SummonKit.Chunky(b.transform, badge.Length > 0 ? badge : "FEATURED", badge.Length > 12 ? 16 : 22, Color.white, TextAnchor.MiddleCenter, "BadgeText");
                Ui.Stretch(bt.rectTransform);
            }
            return card.rectTransform;
        }

        // ---- luck info ------------------------------------------------------------------------------------------------

        private void LuckInfo()
        {
            var def = Def;
            Modal("Luck", b =>
            {
                int max = SummonEventSystem.LuckMax(Content, Type);
                Ui.Label(b, "Every draw adds 1 Luck. The featured " + SummonView.TypeNoun(Type) + " drops at a flat 1.2% per draw; when Luck reaches "
                    + max + ", that draw is the featured " + SummonView.TypeNoun(Type) + ". Luck is fixed: nothing else changes the odds. It is kept until the next "
                    + SummonView.TypeNoun(Type) + " summon event, so you can save up across events.", Theme.FontSmall, Theme.Text).name = "LuckRuleText";
                long batch = Content.ShardStars.BatchShards;
                Ui.Label(b, "A drop is " + batch + " shards of the featured " + SummonView.TypeNoun(Type) + ": the first unlocks it, then shards raise its stars (up to "
                    + Content.ShardStars.MaxStars + "★, " + Fmt.Num(Content.ShardStars.TotalTo(Content.ShardStars.MaxStars)) + " shards in all).", Theme.FontSmall, Theme.Text);
                if (def == null) return;
                double total = 0;
                foreach (var t in def.Gacha.Tiers) total += t.Weight;
                foreach (var t in def.Gacha.Tiers)
                {
                    var names = new List<string>();
                    foreach (var e in t.Pool)
                    {
                        if (names.Count >= 3) { names.Add("…"); break; }
                        var ev = SummonEventSystem.CurrentEvent(State, Content, Type, Now);
                        names.Add(SummonView.RewardName(SummonEventSystem.Resolve(new Reward(e.Kind, e.Id, e.Amount), ev?.FeaturedId), Content));
                    }
                    Ui.Label(b, t.Tier + " " + Fmt.Odds(t.Weight / total * 100) + ": " + string.Join(", ", names), 22, Theme.RarityColor(t.Tier));
                }
                if (def.Gacha.TenPullGuarantee) Note(b, "10× Draw: at least one " + def.Gacha.TenPullMinTier + " or better.");
            });
        }

        // ---- packs --------------------------------------------------------------------------------------------------

        public UiModal PacksSheet()
        {
            var def = Def;
            var ev = SummonEventSystem.CurrentEvent(State, Content, Type, Now);
            return Ui.Sheet(App.ModalLayer, "Packs", SummonKit.Plum, Theme.Hex(0x4A3A78), (box, m) =>
            {
                SummonKit.Banner(box, "Event Packs", "packs");
                var t = SummonKit.Chunky(box, "", 34, Color.white, TextAnchor.MiddleCenter, "TimeLeft");
                if (ev != null) Live(t, () => SummonView.TimeLeft(ev.EndMs, Now));
                SummonKit.CreamList(box, 1150, list =>
                {
                    if (def == null) return;
                    // the daily ad ticket (instant while ads are disabled or with No Ads)
                    var ad = Content.AdPlacement(def.AdPlacementId);
                    if (ad != null)
                    {
                        SummonKit.CreamCard(list, ad.Name, out var b);
                        int left = ShopSystem.AdViewsLeft(State, Content, ad.Id, Now);
                        PackRow(b, ad.Rewards, "Daily limit " + (ad.DailyCap - left) + "/" + ad.DailyCap, row =>
                        {
                            var btn = SummonKit.ActionButton(row, Session.NoAds ? "Free" : "", Session.NoAds ? null : "ad", null,
                                () => Session.WatchAd(ad.Id, _ => { m.Close(); PacksSheet(); }), Theme.Hex(0xF2C23C), 240, 96, left > 0, "Pack:" + ad.Id);
                            SummonKit.Badge(btn.transform, left);
                        });
                    }
                    foreach (var id in def.PackProductIds)
                    {
                        var p = Content.Product(id);
                        if (p == null) continue;
                        SummonKit.CreamCard(list, p.Name, out var b);
                        string block = ShopSystem.PurchaseBlockReason(State, Content, p, Now);
                        PackRow(b, p.Contents, "Daily limit " + ShopSystem.DailyPurchaseCount(State, Content, p.Id, Now) + "/" + p.DailyLimit, row =>
                        {
                            if (!Session.IsOffered(p.Id)) Ui.Button(row, "Coming soon", null, Theme.Button, 240, 96, false, 30).name = "ComingSoon:" + p.Id;
                            else SummonKit.ActionButton(row, Fmt.Price(p.PriceUsdCents), null, null, () => Session.Purchase(p.Id, _ => { m.Close(); PacksSheet(); }), SummonKit.Lime, 240, 96, block == null, "Pack:" + p.Id);
                        });
                    }
                });
                Ui.Button(box, "X", m.Close, SummonKit.PlumLight, -1, 100, true, 48).name = "Close";
            }, 980);
        }

        private void PackRow(RectTransform body, List<Reward> rewards, string limit, System.Action<RectTransform> button)
        {
            var row = Ui.Row(body, 150, 14, null, 0, "PackRow");
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            foreach (var r in rewards) SummonKit.RewardTile(row, r, 120);
            Ui.Size(Ui.Rect(row, "Spacer"), -1, 10, 1);
            var col = Ui.Column(row, 4, 0, null, "Buy");
            Ui.Size(col, 260, 150, 0);
            Ui.Label(col, limit, 24, SummonKit.Brown, TextAnchor.MiddleCenter, "Limit");
            button(col);
        }

        // ---- tasks --------------------------------------------------------------------------------------------------

        public UiModal TasksSheet()
        {
            var def = Def;
            return Ui.Sheet(App.ModalLayer, "Tasks", SummonKit.Plum, Theme.Hex(0x4A3A78), (box, m) =>
            {
                SummonKit.Banner(box, "Tasks", "tasks");
                SummonKit.CreamList(box, 1150, list =>
                {
                    if (def == null) return;
                    foreach (var st in SummonEventSystem.Tasks(State, Content, Type, Now))
                    {
                        var s = st;
                        var line = def.Tasks.Find(l => l.Id == s.LineId);
                        SummonKit.CreamCard(list, s.Text, out var b);
                        Ui.Bar(b, s.Target > 0 ? (float)System.Math.Min(s.Progress, s.Target) / s.Target : 1, SummonKit.Lime,
                            Fmt.Num(System.Math.Min(s.Progress, s.Target)) + "/" + Fmt.Num(s.Target), 44);
                        var row = Ui.Row(b, 130, 14, null, 0, "TaskRow");
                        row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                        SummonKit.RewardTile(row, s.Reward, 112);
                        Ui.Size(Ui.Rect(row, "Spacer"), -1, 10, 1);
                        if (s.Done) Ui.Label(row, "Done", Theme.FontNormal, SummonKit.Brown, TextAnchor.MiddleRight, "Done");
                        else if (s.Claimable)
                            SummonKit.ActionButton(row, "CLAIM", null, null, () => Run(new SummonTaskClaim { EventType = Type, LineId = s.LineId }, _ => { m.Close(); TasksSheet(); }), SummonKit.Lime, 220, 96, true, "Claim:" + s.LineId);
                        else
                            SummonKit.ActionButton(row, "GO", null, null, () => { m.Close(); App.Open(SummonView.GoScreen(line)); }, Theme.Hex(0xF2C23C), 220, 96, true, "Go:" + s.LineId);
                    }
                });
                var bottom = Ui.Row(box, 100, 20, null, 0, "TaskButtons");
                Ui.Button(bottom, "X", m.Close, SummonKit.PlumLight, -1, 100, true, 48).name = "Close";
                Ui.Button(bottom, "Claim all", () => Run(new SummonTaskClaimAll { EventType = Type }, _ => { m.Close(); TasksSheet(); }), SummonKit.Lime, -1, 100,
                    SummonEventSystem.ClaimableTasks(State, Content, Type, Now) > 0, 36).name = "ClaimAll";
            }, 980);
        }
    }
}
