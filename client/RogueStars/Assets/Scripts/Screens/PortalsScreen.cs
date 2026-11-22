using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>Portal / level select: main portals with feats, Overcharged, Burrow and Dark Side; stamina cost + refill.</summary>
    public sealed class PortalsScreen : UiScreen
    {
        private static readonly string[] Tabs = { "Main", "Overcharged", "Burrow", "Dark Side" };
        private int _tab;

        public override string Title => "Portals";

        protected override void Build(RectTransform body)
        {
            var active = State.Run.Active;
            if (active != null)
            {
                var card = Card(body, Theme.Header);
                Ui.Label(card, "Run in progress: " + (Content.RunLevel(active.LevelId)?.Name ?? active.LevelId) + " (" + active.Mode + ")", Theme.FontNormal, Theme.Warn);
                Ui.Button(card, "Continue run", () => App.Router.Push(new RunScreen()), Theme.Accent);
            }
            var top = Ui.Row(body);
            Ui.Label(top, "Stamina " + State.Wallet.Get(Currencies.Stamina) + "/" + EconomySystem.RegenCap(State, Content, Currencies.Stamina)
                          + "   Burrow Keys " + State.Wallet.Get(Currencies.BurrowKeys), Theme.FontNormal, Theme.CurrencyColor(Currencies.Stamina));
            Ui.Button(top, "Refill", () => StaminaModal.Show(this, App), Theme.Good, 200);
            Ui.TabBar(body, Tabs, _tab, i => { _tab = i; Rebuild(); });

            var mode = _tab == 1 ? RunMode.Overcharged : _tab == 2 ? RunMode.Burrow : _tab == 3 ? RunMode.DarkSide : RunMode.Normal;
            var kind = _tab == 2 ? PortalKind.Burrow : _tab == 3 ? PortalKind.DarkSide : PortalKind.Main;
            string gate = mode == RunMode.Overcharged ? Features.Overcharged : mode == RunMode.Burrow ? Features.Burrow : mode == RunMode.DarkSide ? Features.DarkSide : null;
            if (gate != null && !Session.IsUnlocked(gate))
            {
                var f = Content.Feature(gate);
                Note(body, (f?.Name ?? gate) + " is locked: " + Requirements.Describe(Content, f?.Requires ?? new RequirementSet()));
            }
            var portals = new List<PortalDef>();
            foreach (var p in Content.Portals.Values) if (p.Kind == kind) portals.Add(p);
            portals.Sort((a, b) => a.Order.CompareTo(b.Order));
            foreach (var p in portals) PortalCard(body, p, mode);
        }

        private void PortalCard(RectTransform body, PortalDef portal, RunMode mode)
        {
            var card = Card(body, Theme.PanelAlt);
            Ui.Label(card, portal.Name + (mode == RunMode.Overcharged ? " (Overcharged)" : ""), Theme.FontLarge, mode == RunMode.Overcharged ? Theme.Warn : Theme.Premium);
            var grid = Ui.Grid(card, new Vector2(186, 150), 5, 10);
            foreach (var levelId in portal.LevelIds)
            {
                var level = Content.RunLevel(levelId);
                if (level == null) continue;
                var lv = level;
                bool unlocked = RunSystem.IsLevelUnlocked(State, Content, lv.Id, mode);
                if (mode == RunMode.Burrow && lv.Index != 1) continue;
                var prog = RunSystem.Progress(State, lv.Id);
                bool cleared = mode == RunMode.Overcharged ? prog.OverchargedCleared : prog.Cleared;
                string feats = "";
                for (int i = 0; i < 3; i++) feats += i < prog.Feats.Count && prog.Feats[i] ? "★" : "☆";
                string label = (mode == RunMode.Burrow ? "Dive" : lv.Index.ToString()) + "\n<size=24>" + (mode == RunMode.Burrow ? "best floor " + prog.BestFloor : feats) + "</size>";
                var color = !unlocked ? Theme.Locked : cleared ? Color.Lerp(Theme.Good, Theme.Panel, 0.45f) : Theme.Accent;
                Ui.Button(grid, label, () => LevelModal(portal, lv, mode), color, -1, 150, unlocked, Theme.FontLarge).name = "Level:" + lv.Id;
            }
        }

        private void LevelModal(PortalDef portal, LevelDef level, RunMode mode)
        {
            var cost = RunSystem.EntryCost(Content, level.Id, mode);
            var prog = RunSystem.Progress(State, level.Id);
            var boss = level.BossId != null && Content.RunBosses.TryGetValue(level.BossId, out var bd) ? bd : null;
            Modal(portal.Name + " — " + level.Name, b =>
            {
                KeyValue(b, "Threat", level.Threat + (mode == RunMode.Overcharged ? " + " + portal.OverchargedThreatBonus : ""));
                KeyValue(b, "Boss", boss?.Name ?? "—", Theme.ValenceColor(Valence.Boss));
                KeyValue(b, "Dice / rolls", level.Dice + "d6 · " + level.MaxRolls + " rolls");
                KeyValue(b, "Entry", cost.Amount + " " + Fmt.CurrencyName(cost.Currency), State.Wallet.CanAfford(cost.Currency, cost.Amount) ? Theme.Text : Theme.Bad);
                if (!string.IsNullOrEmpty(level.Modifiers?.Text)) Ui.Label(b, level.Modifiers.Text, Theme.FontSmall, Theme.Warn);
                if (mode != RunMode.Burrow)
                {
                    string[] feats = { "Clear the level", "Finish with ≥ " + level.FeatHpPct + "% HP", "Beat the boss within " + level.FeatBossRounds + " rounds" };
                    for (int i = 0; i < 3; i++)
                        Ui.Label(b, (i < prog.Feats.Count && prog.Feats[i] ? "★ " : "☆ ") + feats[i], Theme.FontSmall, i < prog.Feats.Count && prog.Feats[i] ? Theme.Good : Theme.TextDim);
                }
                if (level.CompletionRewards.Count > 0) Note(b, "Completion: " + Fmt.Rewards(level.CompletionRewards, Content));
                if (State.Run.Active != null) Ui.Label(b, "Finish or abandon your current run first.", Theme.FontSmall, Theme.Bad);
            },
            new ModalButton("Cancel", null),
            new ModalButton("Start (" + cost.Amount + " " + Fmt.CurrencyShort(cost.Currency) + ")", () => Start(portal, level, mode, cost), Theme.Accent));
        }

        private void Start(PortalDef portal, LevelDef level, RunMode mode, Cost cost)
        {
            if (!State.Wallet.CanAfford(cost.Currency, cost.Amount) && cost.Currency == Currencies.Stamina)
            {
                StaminaModal.Show(this, App);
                return;
            }
            Run(new RunStart { PortalId = portal.Id, LevelId = level.Id, Mode = mode }, r => { if (r.Ok) App.Router.Push(new RunScreen()); });
        }
    }

    /// <summary>Stamina refill options (daily free, ad, Diamonds with a rising price).</summary>
    public static class StaminaModal
    {
        public static void Show(UiScreen owner, ClientApp app)
        {
            var s = app.Session;
            var info = EconomySystem.GetStaminaRefillInfo(s.State, s.Content, s.NowMs);
            Ui.Modal(app.ModalLayer, "Refill stamina", b =>
            {
                Ui.Label(b, "+" + info.Amount + " Stamina per refill", Theme.FontNormal, Theme.CurrencyColor(Currencies.Stamina), TextAnchor.MiddleCenter);
                Ui.Button(b, "Daily free (" + info.FreeRefillsLeft + " left)", () => s.Run(new BuyStamina { Method = StaminaRefillMethod.DailyFree }), Theme.Good, -1, Theme.ButtonHeight, info.FreeRefillsLeft > 0);
                Ui.Button(b, (info.AdsInstant ? "Instant (No Ads)" : "Watch ad") + " (" + info.AdRefillsLeft + " left)", () => s.Ads.Show("stamina_refill", ad =>
                {
                    if (ad.Ok) s.Run(new BuyStamina { Method = StaminaRefillMethod.Ad, AdToken = ad.Value });
                }), Theme.Button, -1, Theme.ButtonHeight, info.AdRefillsLeft > 0);
                Ui.Button(b, info.NextDiamondPrice < 0 ? "Diamond refills used up" : info.NextDiamondPrice + " Diamonds (" + info.DiamondRefillsLeft + " left)",
                    () => s.Run(new BuyStamina { Method = StaminaRefillMethod.Diamonds }), Theme.Premium, -1, Theme.ButtonHeight,
                    info.NextDiamondPrice >= 0 && s.State.Wallet.CanAfford(Currencies.Diamonds, info.NextDiamondPrice));
            }, new ModalButton("Close", null));
        }
    }
}
