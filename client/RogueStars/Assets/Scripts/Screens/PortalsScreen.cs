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
    /// Portal / level select: main portals with feats, Overcharged, Burrow and Dark Side; stamina cost + refill. The main
    /// campaign and the Dark Side are endless: both are shown as a bounded window around the frontier (RunSystem.NextMainLevel,
    /// the Dark Side max counter) built with ContentDb.MainPortal / DarkSideLevel; nothing iterates the whole campaign.
    /// </summary>
    public sealed class PortalsScreen : UiScreen
    {
        private static readonly string[] Tabs = { "Main", "Overcharged", "Burrow", "Dark Side" };
        private int _tab;
        /// <summary>Dark Side page (-1 = the page with the next level to clear).</summary>
        private int _page = -1;
        /// <summary>First main portal index shown (-1 = the window around the frontier).</summary>
        private int _mainStart = -1;
        public const int DarkSidePageSize = 25;
        /// <summary>Dark Side pages browsable past the one holding the next level.</summary>
        public const int DarkSidePagesAhead = 1;
        /// <summary>Main portals per page: cleared ones before the frontier portal, the frontier, upcoming ones after it.</summary>
        public const int MainPageSize = 5;
        public const int MainPortalsBehind = 2;
        public const int MainPortalsAhead = 2;

        /// <summary>Daily Biomass surge: the first won portal runs of the day pay Biomass ×N.</summary>
        public static Text SurgeLabel(Transform parent, GameState s, ContentDb c, long now)
        {
            int left = RunSystem.BiomassBonusRunsLeft(s, c, now);
            var t = c.RunTuning;
            var label = Ui.Label(parent, "Biomass surge: " + left + "/" + t.DailyBiomassBonusRuns + " left today — won Main/Overcharged runs pay Biomass ×" + t.DailyBiomassBonusMult.ToString("0.#"),
                Theme.FontSmall, left > 0 ? Theme.CurrencyColor(Currencies.Biomass) : Theme.TextDim);
            label.name = "BiomassSurge";
            return label;
        }

        // ---- endless windows (pure, O(1); tested) -----------------------------------------------------------

        /// <summary>0-based index of the main portal holding the next level to clear.</summary>
        public static int FrontierPortalIndex(GameState s, ContentDb c)
        {
            long next = s.Counter(Requirements.MainLevelCounter) + 1;
            return (int)System.Math.Min(ContentDb.MaxGeneratedIndex - 1, (next - 1) / System.Math.Max(1, c.Campaign.LevelsPerPortal));
        }

        /// <summary>
        /// The main portals to show: <paramref name="requestedStart"/> (-1 = around the frontier) clamped so the window
        /// never starts before portal 0 and never goes past <see cref="MainPortalsAhead"/> portals beyond the frontier.
        /// Returns the first index and the number of portals (≤ <see cref="MainPageSize"/>).
        /// </summary>
        public static (int start, int count) MainWindow(int frontier, int requestedStart)
        {
            int last = frontier + MainPortalsAhead;
            int maxStart = System.Math.Max(0, last - MainPageSize + 1);
            int start = requestedStart < 0 ? System.Math.Max(0, frontier - MainPortalsBehind) : requestedStart;
            start = System.Math.Max(0, System.Math.Min(maxStart, start));
            return (start, System.Math.Min(MainPageSize, last - start + 1));
        }

        /// <summary>Next Dark Side level to clear (1-based).</summary>
        public static int DarkSideFrontier(GameState s) => (int)System.Math.Min(ContentDb.MaxGeneratedIndex - 1, s.Counter(RunSystem.DarkSideMaxCounter) + 1);

        /// <summary>Dark Side page to show (-1 = the frontier's page), clamped to [0, frontier page + DarkSidePagesAhead].</summary>
        public static int DarkSidePage(int frontier, int requested)
        {
            int frontierPage = (frontier - 1) / DarkSidePageSize;
            return requested < 0 ? frontierPage : Mathf.Clamp(requested, 0, frontierPage + DarkSidePagesAhead);
        }

        /// <summary>"BONUS" on every portal-bonus level (every 50th), "CHEST" on every other milestone level (every 10th), else null.</summary>
        public static string MilestoneMarker(ContentDb c, int levelNumber)
        {
            var k = c.Campaign;
            if (levelNumber <= 0) return null;
            if (k.PortalBonusEveryLevels > 0 && levelNumber % k.PortalBonusEveryLevels == 0) return "BONUS";
            if (k.MilestoneEveryLevels > 0 && levelNumber % k.MilestoneEveryLevels == 0) return "CHEST";
            return null;
        }

        /// <summary>Level numbers in full (no K/M abbreviation: "level 12345").</summary>
        public static string LevelNum(long n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
            SurgeLabel(body, State, Content, Now);
            Ui.TabBar(body, Tabs, _tab, i => { _tab = i; _page = -1; _mainStart = -1; Rebuild(); });

            var mode = _tab == 1 ? RunMode.Overcharged : _tab == 2 ? RunMode.Burrow : _tab == 3 ? RunMode.DarkSide : RunMode.Normal;
            string gate = mode == RunMode.Overcharged ? Features.Overcharged : mode == RunMode.Burrow ? Features.Burrow : mode == RunMode.DarkSide ? Features.DarkSide : null;
            if (gate != null && !Session.IsUnlocked(gate))
            {
                var f = Content.Feature(gate);
                Note(body, (f?.Name ?? gate) + " is locked: " + Requirements.Describe(Content, f?.Requires ?? new RequirementSet()));
            }
            if (mode == RunMode.DarkSide)
            {
                KeyValue(body, "Data", Fmt.Num(State.Wallet.Get(Currencies.Data)) + " (Laboratory research)", Theme.CurrencyColor(Currencies.Data));
                var dark = Content.Portal(ContentDb.DarkSidePortalId);
                if (dark != null) DarkSideCard(body, dark);
                return;
            }
            if (mode == RunMode.Burrow)
            {
                // The Burrow is a single small portal.
                foreach (var p in Content.Portals.Values) if (p.Kind == PortalKind.Burrow) PortalCard(body, p, mode, -1);
                return;
            }
            MainPortals(body, mode);
        }

        /// <summary>Main / Overcharged: a page of main portals around the frontier, with paging back to cleared ones.</summary>
        private void MainPortals(RectTransform body, RunMode mode)
        {
            int frontier = FrontierPortalIndex(State, Content);
            var (start, count) = MainWindow(frontier, _mainStart);
            int per = System.Math.Max(1, Content.Campaign.LevelsPerPortal);
            var nav = Ui.Row(body, 70, 8);
            Ui.Button(nav, "< Prev", () => { _mainStart = System.Math.Max(0, start - MainPageSize); Rebuild(); }, Theme.Button, 180, 70, start > 0).name = "MainPrev";
            var range = Ui.Label(nav, "Levels " + LevelNum(start * per + 1) + "–" + LevelNum((start + count) * per) + " · next " + LevelNum((long)State.Counter(Requirements.MainLevelCounter) + 1),
                Theme.FontSmall, Theme.Text, TextAnchor.MiddleCenter);
            range.name = "MainRange";
            Ui.Button(nav, "Next >", () => { _mainStart = start + MainPageSize; Rebuild(); }, Theme.Button, 180, 70, start + count - 1 < frontier + MainPortalsAhead).name = "MainNext";
            if (_mainStart >= 0 && (frontier < start || frontier >= start + count))
                Ui.Button(body, "Jump to level " + LevelNum((long)State.Counter(Requirements.MainLevelCounter) + 1), () => { _mainStart = -1; Rebuild(); }, Theme.Warn).name = "MainFrontier";
            for (int i = start; i < start + count; i++)
            {
                var portal = Content.MainPortal(i);
                if (portal != null) PortalCard(body, portal, mode, i == frontier ? 0 : i < frontier ? -1 : 1);
            }
        }

        /// <summary>The endless Dark Side: pages of levels around the next level to clear, each level showing its Data.</summary>
        private void DarkSideCard(RectTransform body, PortalDef portal)
        {
            int next = DarkSideFrontier(State);
            int page = DarkSidePage(next, _page);
            int lastPage = DarkSidePage(next, int.MaxValue);
            var card = Card(body, Theme.PanelAlt);
            Ui.Label(card, portal.Name + " — endless procedural levels", Theme.FontLarge, Theme.Premium);
            Note(card, "Cleared " + LevelNum(next - 1) + " · next level " + LevelNum(next) + " · Data per clear grows with the level.");
            var nav = Ui.Row(card, 70, 8);
            Ui.Button(nav, "< Prev", () => { _page = Mathf.Max(0, page - 1); Rebuild(); }, Theme.Button, 180, 70, page > 0).name = "DarkPrev";
            Ui.Label(nav, "Levels " + LevelNum(page * DarkSidePageSize + 1) + "–" + LevelNum((page + 1) * DarkSidePageSize), Theme.FontSmall, Theme.Text, TextAnchor.MiddleCenter);
            Ui.Button(nav, "Next >", () => { _page = page + 1; Rebuild(); }, Theme.Button, 180, 70, page < lastPage).name = "DarkNext";
            var grid = Ui.Grid(card, new Vector2(186, 150), 5, 10);
            for (int i = page * DarkSidePageSize + 1; i <= (page + 1) * DarkSidePageSize; i++)
            {
                var lv = Content.DarkSideLevel(i);
                if (lv == null) break;
                bool unlocked = RunSystem.IsLevelUnlocked(State, Content, lv.Id, RunMode.DarkSide);
                var prog = RunSystem.Progress(State, Content, lv.Id);
                long data = 0;
                foreach (var r in lv.CompletionRewards) if (r.Id == Currencies.Data) data += (long)System.Math.Round(r.Amount * lv.RewardMultiplier);
                string label = lv.Index + "\n<size=22>" + (prog.Cleared ? "cleared" : "+" + Fmt.Num(data) + " Data") + "</size>";
                var color = !unlocked ? Theme.Locked : prog.Cleared ? Color.Lerp(Theme.Good, Theme.Panel, 0.45f) : i == next ? Theme.Warn : Theme.Accent;
                Ui.Button(grid, label, () => LevelModal(portal, lv, RunMode.DarkSide), color, -1, 150, unlocked, 30).name = "Level:" + lv.Id;
            }
        }

        /// <summary>One portal's level grid. <paramref name="frontier"/>: -1 before the frontier portal, 0 = it, 1 = after it.</summary>
        private void PortalCard(RectTransform body, PortalDef portal, RunMode mode, int frontier)
        {
            var card = Card(body, Theme.PanelAlt);
            card.name = "Portal:" + portal.Id;
            int first = int.MaxValue, last = 0;
            var levels = new List<LevelDef>(portal.LevelIds.Count);
            foreach (var levelId in portal.LevelIds)
            {
                var level = Content.RunLevel(levelId);
                if (level == null) continue;
                levels.Add(level);
                if (level.Number > 0) { first = System.Math.Min(first, level.Number); last = System.Math.Max(last, level.Number); }
            }
            string header = portal.Name + (mode == RunMode.Overcharged ? " (Overcharged)" : "");
            if (portal.Kind == PortalKind.Main && last > 0) header += "\n<size=24>Levels " + LevelNum(first) + "–" + LevelNum(last) + (frontier == 0 ? " · FRONTIER" : "") + "</size>";
            Ui.Label(card, header, Theme.FontLarge, mode == RunMode.Overcharged ? Theme.Warn : frontier == 0 ? Theme.Good : Theme.Premium);
            var grid = Ui.Grid(card, new Vector2(186, 150), 5, 10);
            foreach (var level in levels)
            {
                var lv = level;
                bool unlocked = RunSystem.IsLevelUnlocked(State, Content, lv.Id, mode);
                if (mode == RunMode.Burrow && lv.Index != 1) continue;
                var prog = RunSystem.Progress(State, Content, lv.Id);
                bool cleared = mode == RunMode.Overcharged ? prog.OverchargedCleared : prog.Cleared;
                string feats = "";
                for (int i = 0; i < 3; i++) feats += i < prog.Feats.Count && prog.Feats[i] ? "★" : "☆";
                string marker = mode == RunMode.Normal && portal.Kind == PortalKind.Main ? MilestoneMarker(Content, lv.Number) : null;
                string label = (mode == RunMode.Burrow ? "Dive" : lv.Index.ToString())
                               + (marker != null ? " <size=20><color=" + (marker == "BONUS" ? "#FF9F43" : "#FFD86B") + ">" + marker + (prog.Cleared ? " ✓" : "") + "</color></size>" : "")
                               + "\n<size=24>" + (mode == RunMode.Burrow ? "best floor " + prog.BestFloor : feats) + "</size>";
                var color = !unlocked ? Theme.Locked : cleared ? Color.Lerp(Theme.Good, Theme.Panel, 0.45f) : Theme.Accent;
                Ui.Button(grid, label, () => LevelModal(portal, lv, mode), color, -1, 150, unlocked, Theme.FontLarge).name = "Level:" + lv.Id;
            }
        }

        private void LevelModal(PortalDef portal, LevelDef level, RunMode mode)
        {
            var cost = RunSystem.EntryCost(Content, level.Id, mode);
            var prog = RunSystem.Progress(State, Content, level.Id);
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
                string marker = mode == RunMode.Normal && portal.Kind == PortalKind.Main ? MilestoneMarker(Content, level.Number) : null;
                if (marker != null && level.FirstClearRewards.Count > 0)
                    Ui.Label(b, (marker == "BONUS" ? "Portal bonus" : "Milestone chest") + " (first clear" + (prog.Cleared ? ", claimed" : "") + "): "
                                + Fmt.Rewards(level.FirstClearRewards, Content), Theme.FontSmall, prog.Cleared ? Theme.TextDim : Theme.Premium);
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
