using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// The run: snake-layout board (tiles coloured by valence with letter icons), token movement and dice
    /// animations, HUD (HP, XP, coins, danger, awakening meter, boss), decision modals (branch, event, draft,
    /// shop, revive, burrow), battle playback, and auto mode (hold Roll; any tap interrupts).
    /// </summary>
    public sealed class RunScreen : UiScreen
    {
        public const int Columns = 8;
        public const float Cell = 124, LaneH = 76, BandGap = 26, TileSize = 64;

        private string _runId;
        private int _trailSeen;
        private int _rollsSeen;
        private int _battlesSeen;
        private bool _auto;
        private float _autoTimer;
        private string _decisionKey;
        private UiModal _decision;
        private GameObject _autoOverlay;
        private UiSequence _anim;
        private bool _toResult;
        private bool _summaryBattleShown;

        public override string Title => "Run";
        /// <summary>Do not rebuild under a running animation (periodic refreshes would cut it short).</summary>
        public override bool AutoRebuild => !Animating;
        public bool Auto => _auto;
        public bool Animating => _anim != null && _anim.Busy;

        // ---- layout ------------------------------------------------------------------------------

        /// <summary>Board position (top-left origin) of a tile: snake rows of <see cref="Columns"/>, lanes by Y.</summary>
        public static Vector2 TileCenter(RunTile t)
        {
            int band = t.X / Columns;
            int c = t.X % Columns;
            if (band % 2 == 1) c = Columns - 1 - c;
            float x = 30 + c * Cell + Cell / 2;
            float y = band * (3 * LaneH + BandGap) + (t.Y + 1) * LaneH + LaneH / 2;
            return new Vector2(x, y);
        }

        public static float BoardHeight(ActiveRun run)
        {
            int maxX = 0;
            foreach (var t in run.Tiles) maxX = Math.Max(maxX, t.X);
            int bands = maxX / Columns + 1;
            return bands * (3 * LaneH + BandGap) + 10;
        }

        // ---- build -------------------------------------------------------------------------------

        protected override void Build(RectTransform body)
        {
            var run = State.Run.Active;
            if (run == null) { BuildFinished(body); return; }
            var level = Content.RunLevel(run.LevelId);
            bool newRun = run.RunId != _runId;
            if (newRun) { _runId = run.RunId; _trailSeen = run.Trail.Count; _rollsSeen = run.Rolls; _battlesSeen = run.BattleCount; }

            Hud(body, run, level);
            var token = Board(body, run, out var dice);
            Controls(body, run, dice);

            // What changed since the last build → animate dice, token path, then battle and decision.
            var path = new List<int>();
            if (run.Trail.Count > _trailSeen) path.AddRange(run.Trail.GetRange(Math.Max(0, _trailSeen - 1), run.Trail.Count - Math.Max(0, _trailSeen - 1)));
            bool rolled = run.Rolls > _rollsSeen;
            bool battle = run.BattleCount > _battlesSeen && run.LastBattle != null;
            _trailSeen = run.Trail.Count; _rollsSeen = run.Rolls; _battlesSeen = run.BattleCount;

            _anim = UiSequence.On(Root.gameObject);
            _anim.Clear();
            if (rolled && dice != null)
            {
                var finalText = DiceText(run);
                _anim.Then(0.45f, t => dice.text = t >= 1 ? finalText : Random.Range(1, 7).ToString());
            }
            if (path.Count > 1 && token != null)
            {
                for (int i = 1; i < path.Count; i++)
                {
                    var from = TileCenter(run.Tiles[path[i - 1]]);
                    var to = TileCenter(run.Tiles[path[i]]);
                    _anim.Then(0.16f, t => token.anchoredPosition = TokenPos(Vector2.Lerp(from, to, t)));
                }
            }
            if (battle)
            {
                var result = run.LastBattle;
                _anim.Call(() => App.Router.Push(new BattlePlaybackScreen(result, _auto)));
            }
            _anim.Call(() => ShowDecision(run));
        }

        private static Vector2 TokenPos(Vector2 center) => new Vector2(center.x, -center.y + 30);

        private static string DiceText(ActiveRun run) => run.LastDice.Count == 0 ? "-" : string.Join(" + ", run.LastDice.ConvertAll(d => d.ToString()));

        private void Hud(RectTransform body, ActiveRun run, LevelDef level)
        {
            var hud = Card(body, Theme.Header, 6);
            var row = Ui.Row(hud, 50, 10);
            Ui.Label(row, (level?.Name ?? run.LevelId) + (run.Mode != RunMode.Normal ? " · " + run.Mode : "") + (run.Mode == RunMode.Burrow ? " · floor " + run.Floor : ""), Theme.FontNormal);
            Ui.Label(row, "Roll " + run.Rolls + "/" + run.MaxRolls, Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight);
            Ui.Bar(hud, run.WormMaxHp > 0 ? (float)(run.WormHp / run.WormMaxHp) : 1, Theme.Good, "HP " + Fmt.Num(Math.Max(0, run.WormHp)) + " / " + Fmt.Num(run.WormMaxHp), 40);
            int need = Content.RunTuning.XpBase + Content.RunTuning.XpPerLevel * (run.Level - 1);
            Ui.Bar(hud, need > 0 ? (float)run.Xp / need : 0, Theme.Accent, "Level " + run.Level + " · XP " + run.Xp + "/" + need, 30);
            var boss = run.BossId != null && Content.RunBosses.TryGetValue(run.BossId, out var bd) ? bd : null;
            Ui.Bar(hud, (float)(run.Awakening / 100.0), Theme.ValenceColor(Valence.Boss), (boss?.Name ?? "Boss") + " awakening " + run.Awakening.ToString("0") + "%", 30);
            var stats = Ui.Row(hud, 64, 8);
            Ui.Label(stats, "Coins " + run.Coins + "\nShards " + run.Shards, Theme.FontSmall, Theme.Warn);
            Ui.Label(stats, "Danger " + run.Danger + "\n<size=20>+" + Fmt.Pct(run.Danger * Content.RunTuning.DangerPctPerLoot) + " enemy stats</size>", Theme.FontSmall, run.Danger > 0 ? Theme.Bad : Theme.TextDim);
            Ui.Button(stats, "Loot " + run.PendingLoot.Count, () => LootModal(run), Theme.Button, 170, 60, true, 24);
            Ui.Button(stats, "Skills " + run.Skills.Count, () => SkillsModal(run), Theme.Button, 170, 60, true, 24);
        }

        private RectTransform Board(RectTransform body, ActiveRun run, out Text dice)
        {
            float h = BoardHeight(run);
            var holder = Ui.Rect(body, "BoardHolder");
            Ui.Size(holder, -1, h);
            var board = Ui.Panel(holder, Theme.Background, "Board").rectTransform;
            board.anchorMin = board.anchorMax = new Vector2(0.5f, 1);
            board.pivot = new Vector2(0.5f, 1);
            board.sizeDelta = new Vector2(Columns * Cell + 60, h);
            board.anchoredPosition = Vector2.zero;

            // edges first (behind tiles)
            foreach (var t in run.Tiles)
                foreach (var n in t.Next)
                    if (n >= 0 && n < run.Tiles.Count) Edge(board, TileCenter(t), TileCenter(run.Tiles[n]), n < t.Id ? Theme.Warn * 0.6f : Theme.Locked);

            var options = run.Phase == RunPhase.ChooseBranch ? run.BranchOptions : null;
            foreach (var t in run.Tiles)
            {
                var def = Content.BoardTiles.TryGetValue(t.TileId ?? "", out var d) ? d : null;
                var center = TileCenter(t);
                bool option = options != null && options.Contains(t.Id);
                if (option)
                {
                    var glow = Ui.Panel(board, Theme.Warn, "Option").rectTransform;
                    Place(glow, center, TileSize + 16);
                }
                bool gate = def != null && def.Category == TileCategory.BossGate;
                var color = def != null ? Theme.TileColor(def.Valence) : Theme.Locked;
                if (t.BonusPct > 0) color = Color.Lerp(color, Theme.Bad, 0.3f);
                var img = Ui.Panel(board, color, "Tile:" + t.Id);
                Place(img.rectTransform, center, gate ? TileSize + 30 : TileSize);
                var lab = Ui.Label(img.transform, def != null ? Theme.TileLetter(def.Category) : "", gate ? 22 : 28, Color.white, TextAnchor.MiddleCenter);
                lab.fontStyle = FontStyle.Bold;
                Ui.Stretch(lab.rectTransform);
            }
            // boss on the board, next to its gate
            var gateTile = run.BossGateTile >= 0 && run.BossGateTile < run.Tiles.Count ? run.Tiles[run.BossGateTile] : null;
            if (gateTile != null)
            {
                var c = TileCenter(gateTile);
                var bossImg = Ui.Panel(board, Color.Lerp(Theme.ValenceColor(Valence.Boss), Color.black, 0.3f), "Boss");
                var p = new Vector2(Mathf.Clamp(c.x + (c.x > board.sizeDelta.x / 2 ? -110 : 110), 60, board.sizeDelta.x - 60), c.y - 50);
                Place(bossImg.rectTransform, p, 96);
                var bl = Ui.Label(bossImg.transform, "BOSS\n" + run.Awakening.ToString("0") + "%", 22, Color.white, TextAnchor.MiddleCenter);
                Ui.Stretch(bl.rectTransform);
            }

            // token (worm)
            var cur = run.Position >= 0 && run.Position < run.Tiles.Count ? run.Tiles[run.Position] : null;
            var start = cur;
            int prevIndex = Math.Max(0, Math.Min(_trailSeen, run.Trail.Count) - 1);
            if (run.Trail.Count > _trailSeen && _trailSeen > 0) start = run.Tiles[run.Trail[prevIndex]];
            RectTransform token = null;
            if (start != null)
            {
                var tok = Ui.Panel(board, Theme.Hex(0xFF8FB1), "Token");
                token = tok.rectTransform;
                token.anchorMin = token.anchorMax = new Vector2(0, 1);
                token.pivot = new Vector2(0.5f, 0.5f);
                token.sizeDelta = new Vector2(44, 44);
                token.anchoredPosition = TokenPos(TileCenter(start));
                var tl = Ui.Label(tok.transform, "W", 26, Color.black, TextAnchor.MiddleCenter);
                tl.fontStyle = FontStyle.Bold;
                Ui.Stretch(tl.rectTransform);
            }

            // dice + last effect text under the board
            var info = Ui.Row(body, 70, 10, Theme.Panel, 12);
            dice = Ui.Label(info, DiceText(run), Theme.FontLarge, Theme.Text, TextAnchor.MiddleCenter);
            Ui.Size(dice, 200, -1, 0);
            var land = cur != null && Content.BoardTiles.TryGetValue(cur.TileId ?? "", out var ld) ? ld : null;
            Ui.Label(info, run.LastEventText ?? (land != null ? land.Name + (string.IsNullOrEmpty(land.Text) ? "" : ": " + land.Text) : ""), Theme.FontSmall, Theme.TextDim);
            return token;
        }

        private static void Place(RectTransform rt, Vector2 center, float size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(center.x, -center.y);
        }

        private static void Edge(RectTransform board, Vector2 a, Vector2 b, Color color)
        {
            var img = Ui.Panel(board, color, "Edge");
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var pa = new Vector2(a.x, -a.y);
            var pb = new Vector2(b.x, -b.y);
            rt.anchoredPosition = (pa + pb) / 2;
            rt.sizeDelta = new Vector2(Vector2.Distance(pa, pb), 6);
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(pb.y - pa.y, pb.x - pa.x) * Mathf.Rad2Deg);
        }

        private void Controls(RectTransform body, ActiveRun run, Text dice)
        {
            var row = Ui.Row(body, 120, 12);
            bool canRoll = run.Phase == RunPhase.AwaitRoll;
            var roll = Ui.Button(row, _auto ? "AUTO…" : "ROLL\n<size=22>hold for auto</size>", null, canRoll ? Theme.Accent : Theme.Locked, -1, 120, canRoll || _auto, Theme.FontLarge);
            roll.name = "RollButton";
            var hold = roll.gameObject.AddComponent<HoldButton>();
            hold.OnTap = () => { if (_auto) StopAuto(); else if (!Animating) Run(new RunRoll()); };
            hold.OnHold = StartAuto;
            Ui.Button(row, _auto ? "Stop auto" : "Auto", () => { if (_auto) StopAuto(); else StartAuto(); }, _auto ? Theme.Warn : Theme.Button, 220, 120);
            var row2 = Ui.Row(body, 80, 12);
            Ui.Button(row2, "Decision", () => { _decisionKey = null; ShowDecision(run); }, Theme.Button, -1, 80, run.Phase != RunPhase.AwaitRoll);
            Ui.Button(row2, "Abandon", () => Modal("Abandon the run?", b => Note(b, "All pending loot (" + run.PendingLoot.Count + " items) is lost."),
                new ModalButton("Keep going", null), new ModalButton("Abandon", () => Run(new RunAbandon()), Theme.Bad)), Theme.Bad, 260, 80);
        }

        private void BuildFinished(RectTransform body)
        {
            var summary = State.Run.LastSummary;
            Ui.Label(body, "Run finished", Theme.FontLarge, Theme.Text, TextAnchor.MiddleCenter);
            if (summary != null && summary.RunId == _runId && summary.LastBattle != null && !_summaryBattleShown)
            {
                _summaryBattleShown = true;
                var battle = summary.LastBattle;
                _anim = UiSequence.On(Root.gameObject);
                _anim.Clear();
                _anim.Call(() => App.Router.Push(new BattlePlaybackScreen(battle, _auto)));
            }
            _toResult = true;
            CloseDecision();
            Ui.Button(body, "See results", () => App.Router.Replace(new RunResultScreen()), Theme.Accent);
        }

        // ---- frame: auto mode and result transition -------------------------------------------

        public override void Frame(float dt)
        {
            if (_toResult && !Animating && App.Router.Current == this)
            {
                _toResult = false;
                App.Router.Replace(new RunResultScreen());
                return;
            }
            if (!_auto) return;
            EnsureAutoOverlay();
            if (Animating || Session.InFlight > 0 || App.Router.Current != this) { _autoTimer = 0; return; }
            _autoTimer += dt;
            if (_autoTimer < 0.6f) return;
            _autoTimer = 0;
            if (State.Run.Active == null) { StopAuto(); return; }
            CloseDecision();
            Run(new RunAutoStep(), r => { if (!r.Ok) StopAuto(); });
        }

        public void StartAuto()
        {
            if (_auto) return;
            _auto = true;
            _autoTimer = 0;
            Session.Info("Auto mode — tap anywhere to stop");
            Rebuild();
        }

        public void StopAuto()
        {
            if (!_auto) return;
            _auto = false;
            if (_autoOverlay != null) Ui.DestroyGo(_autoOverlay);
            _autoOverlay = null;
            if (Root != null) Rebuild();
        }

        private void EnsureAutoOverlay()
        {
            if (_autoOverlay != null) { _autoOverlay.transform.SetAsLastSibling(); return; }
            var overlay = Ui.Button(App.ModalLayer, "", StopAuto, new Color(0, 0, 0, 0.01f));
            Ui.Stretch((RectTransform)overlay.transform);
            _autoOverlay = overlay.gameObject;
        }

        public override void OnHide()
        {
            CloseDecision();
            if (_autoOverlay != null) Ui.DestroyGo(_autoOverlay);
            _autoOverlay = null;
        }

        public override void OnShow() { _decisionKey = null; }

        // ---- decisions ---------------------------------------------------------------------------

        private void CloseDecision()
        {
            if (_decision != null && _decision.IsOpen) _decision.Close();
            _decision = null;
        }

        private static string DecisionKey(ActiveRun r) =>
            r.RunId + "|" + r.Phase + "|" + r.Rolls + "|" + r.EventId + "|" + string.Join(",", r.DraftOffer) + "|" + r.DraftRerolls + "|" + r.ShopRerolls + "|" + r.Shop.FindAll(o => o.Sold).Count + "|" + r.Coins;

        private void ShowDecision(ActiveRun stale)
        {
            if (Root == null || App.Router.Current != this) return;
            var run = State.Run.Active;
            if (run == null || run.RunId != stale.RunId) return;
            string key = DecisionKey(run);
            if (key == _decisionKey && _decision != null && _decision.IsOpen) return;
            CloseDecision();
            _decisionKey = key;
            switch (run.Phase)
            {
                case RunPhase.ChooseBranch: _decision = BranchModal(run); break;
                case RunPhase.Event: _decision = EventModal(run); break;
                case RunPhase.Draft: _decision = DraftModal(run); break;
                case RunPhase.Shop: _decision = ShopModal(run); break;
                case RunPhase.Defeated: _decision = ReviveModal(run); break;
                case RunPhase.BurrowChoice: _decision = BurrowModal(run); break;
            }
        }

        private UiModal BranchModal(ActiveRun run)
        {
            var buttons = new List<ModalButton>();
            for (int i = 0; i < run.BranchOptions.Count; i++)
            {
                int index = i;
                var tile = run.Tiles[run.BranchOptions[i]];
                var ahead = RunBoard.Ahead(run.Tiles, tile.Id, 3);
                var names = new List<string>();
                foreach (var id in ahead) names.Add(TileName(run.Tiles[id]));
                buttons.Add(new ModalButton((i == 0 ? "Path A" : "Path B") + ": " + string.Join(", ", names), () => Run(new RunChooseBranch { Index = index }), Theme.Accent));
            }
            return Modal("Choose a path", b => Note(b, "Highlighted tiles on the board are your options. " + run.StepsLeft + " steps left."), buttons.ToArray());
        }

        private string TileName(RunTile t) => Content.BoardTiles.TryGetValue(t.TileId ?? "", out var d) ? d.Name : t.TileId;

        private UiModal EventModal(ActiveRun run)
        {
            Content.RunEvents.TryGetValue(run.EventId ?? "", out var ev);
            var buttons = new List<ModalButton>();
            if (ev != null)
                for (int i = 0; i < ev.Options.Count; i++)
                {
                    int index = i;
                    var o = ev.Options[i];
                    string cost = (o.CoinCost > 0 ? " [" + o.CoinCost + " coins]" : "") + (o.ShardCost > 0 ? " [" + o.ShardCost + " shards]" : "");
                    buttons.Add(new ModalButton(o.Text + cost, () => Run(new RunChooseEventOption { Index = index }), i == ev.DefaultOption ? Theme.Accent : Theme.Button));
                }
            return Modal(ev?.Name ?? "Event", b =>
            {
                Ui.Label(b, ev?.Text ?? run.EventId, Theme.FontNormal);
                if (ev != null)
                    foreach (var o in ev.Options)
                        if (!string.IsNullOrEmpty(o.RiskText)) Ui.Label(b, "• " + o.Text + ": " + o.RiskText, Theme.FontSmall, Theme.Warn);
            }, buttons.Count > 0 ? buttons.ToArray() : new[] { new ModalButton("Continue", () => Run(new RunAutoStep())) });
        }

        private UiModal DraftModal(ActiveRun run)
        {
            return Modal("Choose a skill", b =>
            {
                foreach (var id in run.DraftOffer)
                {
                    string sid = id;
                    var s = Content.Skill(sid);
                    var card = Ui.Column(b, 4, 14, Color.Lerp(Theme.FamilyColor(s?.Family ?? SkillFamily.None), Theme.Panel, 0.65f), "Draft:" + sid);
                    var head = Ui.Row(card, 48, 8);
                    Ui.Label(head, s?.Name ?? sid, Theme.FontNormal, Theme.RarityColor(s?.Rarity ?? Rarity.Common));
                    Ui.Label(head, (s?.Family ?? SkillFamily.None) + " · " + (s?.Rarity ?? Rarity.Common), Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight);
                    if (!string.IsNullOrEmpty(s?.Description)) Ui.Label(card, s.Description, Theme.FontSmall);
                    if (s?.UpgradesFrom != null) Ui.Label(card, "Upgrades " + (Content.Skill(s.UpgradesFrom)?.Name ?? s.UpgradesFrom), Theme.FontSmall, Theme.Good);
                    Ui.Button(card, "Pick", () => { CloseDecision(); Run(new RunDraftPick { SkillId = sid }); }, Theme.Accent, -1, 70);
                }
            }, new ModalButton("Reroll (" + run.DraftRerolls + ")", () => Run(new RunDraftReroll()), Theme.Button));
        }

        private UiModal ShopModal(ActiveRun run)
        {
            var table = Content.RunShopTables.TryGetValue(Content.RunLevel(run.LevelId)?.ShopTableId ?? "", out var st) ? st : null;
            long reroll = table != null ? table.RerollBase + table.RerollStep * run.ShopRerolls : 0;
            return Modal("Shop — " + run.Coins + " coins", b =>
            {
                for (int i = 0; i < run.Shop.Count; i++)
                {
                    int index = i;
                    var o = run.Shop[i];
                    string what = o.Kind == ShopOfferKind.Skill ? (Content.Skill(o.SkillId)?.Name ?? o.SkillId)
                        : o.Kind == ShopOfferKind.Heal ? "Heal " + Fmt.Pct(o.Value)
                        : "+" + (o.Stat != null ? (o.Stat.Percent != 0 ? Fmt.Pct(o.Stat.Percent) : o.Stat.Flat.ToString("0")) + " " + o.Stat.Stat : "stat");
                    var row = Ui.Row(b, 76, 8);
                    var color = o.Kind == ShopOfferKind.Skill ? Theme.FamilyColor(Content.Skill(o.SkillId)?.Family ?? SkillFamily.None) : Theme.Good;
                    Ui.Label(row, what, Theme.FontSmall, color);
                    Ui.Button(row, o.Sold ? "Sold" : o.Price + " coins", () => Run(new RunShopBuy { Index = index }), Theme.Premium, 220, 70, !o.Sold && run.Coins >= o.Price, 26);
                }
            },
            new ModalButton("Reroll (" + reroll + ")", () => Run(new RunShopReroll()), Theme.Button),
            new ModalButton("Leave", () => Run(new RunShopLeave()), Theme.Accent));
        }

        private UiModal ReviveModal(ActiveRun run)
        {
            long cost = Content.RunTuning.ReviveDiamondCost;
            int adsLeft = ShopSystem.AdViewsLeft(State, Content, RunSystem.ReviveAdPlacement, Now);
            var buttons = new List<ModalButton>();
            if (!run.ReviveUsed)
            {
                buttons.Add(new ModalButton("Ad revive (" + adsLeft + ")", () => Session.Ads.Show(RunSystem.ReviveAdPlacement, ad =>
                {
                    if (ad.Ok) Run(new RunRevive { Method = ReviveMethod.Ad, AdToken = ad.Value });
                }), Theme.Good));
                buttons.Add(new ModalButton(cost + " Diamonds", () => Run(new RunRevive { Method = ReviveMethod.Diamonds }), Theme.Premium));
            }
            buttons.Add(new ModalButton("Give up", () => Run(new RunAbandon()), Theme.Bad));
            return Modal("Defeated!", b =>
            {
                Ui.Label(b, run.ReviveUsed ? "Your revive for this run is used." : "Revive once with " + Fmt.Pct(Content.RunTuning.ReviveHpPct) + " HP and retry the fight.", Theme.FontNormal, Theme.Text, TextAnchor.MiddleCenter);
                Note(b, "Giving up loses " + run.PendingLoot.Count + " pending loot items.");
            }, buttons.ToArray());
        }

        private UiModal BurrowModal(ActiveRun run) =>
            Modal("Floor " + run.Floor + " cleared", b =>
            {
                Ui.Label(b, "Dive deeper for better loot, or cash out now.", Theme.FontNormal);
                Note(b, "Pending: " + Fmt.Rewards(run.PendingLoot, Content));
            },
            new ModalButton("Cash out", () => Run(new RunBurrowCashOut()), Theme.Good),
            new ModalButton("Descend", () => Run(new RunBurrowDescend()), Theme.Bad));

        private void LootModal(ActiveRun run) =>
            Modal("Pending loot", b =>
            {
                Ui.Label(b, run.PendingLoot.Count == 0 ? "Nothing yet." : Fmt.Rewards(run.PendingLoot, Content), Theme.FontSmall);
                Note(b, "Paid out only when you beat the boss. Each item raises the danger level.");
            });

        private void SkillsModal(ActiveRun run) =>
            Modal("Skills", b =>
            {
                foreach (var id in RunSystem.ActiveSkillIds(run))
                {
                    var s = Content.Skill(id);
                    Ui.Label(b, (s?.Name ?? id) + " <size=20>(" + (s?.Family ?? SkillFamily.None) + ")</size>", Theme.FontSmall, Theme.FamilyColor(s?.Family ?? SkillFamily.None));
                }
            });
    }

    /// <summary>Run result: win/loss, granted rewards, new feats; replay or leave.</summary>
    public sealed class RunResultScreen : UiScreen
    {
        public override string Title => "Run result";

        protected override void Build(RectTransform body)
        {
            var s = State.Run.LastSummary;
            if (s == null) { Note(body, "No run finished yet."); return; }
            var card = Card(body, s.Won ? Color.Lerp(Theme.Good, Theme.Panel, 0.7f) : Color.Lerp(Theme.Bad, Theme.Panel, 0.7f));
            Ui.Label(card, s.Won ? "VICTORY" : "DEFEAT", Theme.FontTitle, s.Won ? Theme.Good : Theme.Bad, TextAnchor.MiddleCenter);
            Ui.Label(card, (Content.RunLevel(s.LevelId)?.Name ?? s.LevelId) + " · " + s.Mode, Theme.FontNormal, Theme.Text, TextAnchor.MiddleCenter);
            KeyValue(card, "Rolls", s.Rolls.ToString());
            KeyValue(card, "Run level", s.Level.ToString());
            if (s.Mode == RunMode.Burrow) KeyValue(card, "Floors", s.Floors.ToString());
            foreach (var f in s.NewFeats) Ui.Label(card, "New feat ★ " + (f + 1), Theme.FontNormal, Theme.Warn);
            Section(body, s.Won ? "Rewards" : "Pending loot lost");
            if (s.Granted.Count == 0) Note(body, s.Won ? "No rewards." : "Better luck next time.");
            foreach (var r in s.Granted) Ui.Label(body, "+ " + Fmt.Reward(r, Content), Theme.FontNormal, Theme.Good);
            // TODO(rules): double-rewards ad — no command exists yet (Meta placement "double_run_rewards").
            var row = Ui.Row(body, Theme.ButtonHeight, 12);
            var level = Content.RunLevel(s.LevelId);
            if (level != null)
                Ui.Button(row, "Again", () => Run(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = s.Mode }, r => { if (r.Ok) App.Router.Replace(new RunScreen()); }), Theme.Accent);
            Ui.Button(row, "Portals", () => App.Router.Pop(), Theme.Button);
        }
    }
}
