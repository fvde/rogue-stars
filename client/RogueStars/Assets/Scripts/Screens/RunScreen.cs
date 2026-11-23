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
    /// The run: a board of tiles on a snake grid that climbs upwards (tiles labelled and outlined by type, visited tiles
    /// dimmed, forks marked, the worm token glowing and bobbing on its tile), a compact HUD (pause, level, tile x/y, HP,
    /// coins, run level, boss awakening, danger), a bottom bar (skill slots, Loot, big ROLL, AUTO), decision popups
    /// (branch, event cards with gain/risk chips, skill draft cards by family and rarity, shop, revive, Burrow), battle
    /// playback, and auto mode.
    /// Auto mode (hold ROLL or tap AUTO): sends RunAutoStep for the clear steps (roll, move, tiles, battles with fast
    /// playback); a tap anywhere stops it. At a real decision (RunSystem.RequiresPlayer: shop, draft, event, chest,
    /// campfire, branch, revive, Burrow) auto pauses: the tap-to-stop overlay goes away, the decision UI opens and the
    /// player decides; auto then resumes by itself while it is still on. "Auto-pick: On" (farming,
    /// ClientSettings.AutoPickDrafts, off by default) takes the default choices instead and leaves shops without buying.
    /// </summary>
    public sealed class RunScreen : UiScreen
    {
        // ---- board geometry (top-left origin, y down; drawn flipped so the path climbs) ----
        public const int Columns = 8;
        public const float Cell = 116, LaneH = 72, BandGap = 16, Pad = 20;
        public const float TileW = 100, TileH = 60, GateGrow = 20;
        /// <summary>Kept for callers of the old square tiles: the tile height.</summary>
        public const float TileSize = TileH;
        public const float BoardWidth = Columns * Cell + 2 * Pad;
        /// <summary>Boss marker ("BOSS 42%") size; placed in a free cell next to the gate (<see cref="BossMarkerCenter"/>).</summary>
        public static readonly Vector2 BossMarkerSize = new Vector2(104, 50);

        // ---- bottom bar ----
        public const float InfoRowH = 52, SlotRowH = 80, RollRowH = 150, HintRowH = 34;
        public const int SkillSlots = 4;

        private string _runId;
        private int _trailSeen;
        private int _rollsSeen;
        private int _battlesSeen;
        private bool _auto;
        private float _autoTimer;
        private string _decisionKey;
        private string _pausedKey;
        private UiModal _decision;
        private GameObject _autoOverlay;
        private UiSequence _anim;
        private bool _toResult;
        private bool _summaryBattleShown;
        private Text _coins;

        public override string Title
        {
            get
            {
                var run = State?.Run.Active;
                if (run == null) return "Run";
                var level = Content.RunLevel(run.LevelId);
                if (level == null) return run.LevelId;
                return (level.Number > 0 ? "Level " + PortalsScreen.LevelNum(level.Number) + " · " : "") + level.Name;
            }
        }

        public override string Subtitle
        {
            get
            {
                var run = State?.Run.Active;
                if (run == null) return null;
                var cur = run.Position >= 0 && run.Position < run.Tiles.Count ? run.Tiles[run.Position] : null;
                int last = run.BossGateTile >= 0 && run.BossGateTile < run.Tiles.Count ? run.Tiles[run.BossGateTile].X : 0;
                return "Tile " + ((cur?.X ?? 0) + 1) + " of " + (last + 1) + " · Roll " + run.Rolls + "/" + run.MaxRolls + " · Threat " + run.Threat
                       + (run.Mode != RunMode.Normal ? " · " + run.Mode : "") + (run.Mode == RunMode.Burrow ? " floor " + run.Floor : "");
            }
        }

        protected override string BackLabel => "II";
        protected override void OnBack() => PauseModal();

        public override float FooterHeight => FooterPadTop + FooterPadBottom + InfoRowH + SlotRowH + RollRowH + HintRowH + 3 * FooterSpacing;
        public override bool ReserveToastStrip => true;
        /// <summary>Do not rebuild under a running animation (periodic refreshes would cut it short).</summary>
        public override bool AutoRebuild => !Animating;
        public bool Auto => _auto;
        /// <summary>Auto-pick drafts / farming (player setting, off by default).</summary>
        public bool Farming => App.Settings != null && App.Settings.AutoPickDrafts;
        /// <summary>Auto mode is on but waits for the player's decision.</summary>
        public bool AutoPaused => _auto && State.Run.Active != null && RunSystem.RequiresPlayer(Content, State.Run.Active, Farming);
        public bool Animating => _anim != null && _anim.Busy;

        // ---- layout (pure; tested) -------------------------------------------------------------

        /// <summary>Board position (top-left origin, y down) of a tile: snake rows of <see cref="Columns"/>, lanes by Y.</summary>
        public static Vector2 TileCenter(RunTile t)
        {
            int band = t.X / Columns;
            int c = t.X % Columns;
            if (band % 2 == 1) c = Columns - 1 - c;
            float x = Pad + c * Cell + Cell / 2;
            float y = band * (3 * LaneH + BandGap) + (t.Y + 1) * LaneH + LaneH / 2;
            return new Vector2(x, y);
        }

        public static float BoardHeight(ActiveRun run) => BoardHeight(run.Tiles);

        public static float BoardHeight(List<RunTile> tiles)
        {
            int maxX = 0;
            foreach (var t in tiles) maxX = Math.Max(maxX, t.X);
            int bands = maxX / Columns + 1;
            return bands * (3 * LaneH + BandGap) + 10;
        }

        /// <summary>Where a layout point is drawn: the board is flipped vertically so the path starts at the bottom and climbs.</summary>
        public static Vector2 DrawPoint(Vector2 layout, float boardHeight) => new Vector2(layout.x, -(boardHeight - layout.y));

        /// <summary>Board rect (top-left origin, y down) a tile occupies; the boss gate is drawn larger.</summary>
        public static Rect TileRect(RunTile t, bool gate)
        {
            float w = gate ? TileW + GateGrow : TileW, h = gate ? TileH + GateGrow : TileH;
            var c = TileCenter(t);
            return new Rect(c.x - w / 2, c.y - h / 2, w, h);
        }

        /// <summary>
        /// Center of the boss marker: the first free cell around the boss gate (beside it, then the lanes above/below,
        /// then two cells away) that overlaps no tile and stays on the board; falls back to the band gap above the gate.
        /// </summary>
        public static Vector2 BossMarkerCenter(List<RunTile> tiles, int gateIndex, Vector2 boardSize)
        {
            var gate = tiles[gateIndex];
            var c = TileCenter(gate);
            var rects = new List<Rect>(tiles.Count);
            foreach (var t in tiles) rects.Add(TileRect(t, t.Id == gateIndex));
            var offsets = new[]
            {
                new Vector2(-Cell, 0), new Vector2(Cell, 0), new Vector2(0, -LaneH), new Vector2(0, LaneH),
                new Vector2(-Cell, -LaneH), new Vector2(Cell, -LaneH), new Vector2(-Cell, LaneH), new Vector2(Cell, LaneH),
                new Vector2(-2 * Cell, 0), new Vector2(2 * Cell, 0), new Vector2(0, -2 * LaneH), new Vector2(0, 2 * LaneH),
            };
            foreach (var o in offsets)
            {
                var p = c + o;
                var r = new Rect(p.x - BossMarkerSize.x / 2, p.y - BossMarkerSize.y / 2, BossMarkerSize.x, BossMarkerSize.y);
                if (r.xMin < 0 || r.yMin < 0 || r.xMax > boardSize.x || r.yMax > boardSize.y) continue;
                bool free = true;
                foreach (var tr in rects) if (tr.Overlaps(r)) { free = false; break; }
                if (free) return p;
            }
            return new Vector2(c.x, Mathf.Max(BossMarkerSize.y / 2, c.y - TileH / 2 - 30 - BossMarkerSize.y / 2));
        }

        /// <summary>How one board tile is drawn (pure; tested).</summary>
        public struct TileLook
        {
            public string Label;
            public Color Accent;
            /// <summary>Already walked over (in the trail) and not the token's tile: drawn dimmed.</summary>
            public bool Visited;
            /// <summary>The worm stands here.</summary>
            public bool Current;
            /// <summary>The path splits after this tile.</summary>
            public bool Fork;
            /// <summary>A branch the player can choose right now.</summary>
            public bool Option;
            public bool Gate;
            public bool Empty;
        }

        public static TileLook LookOf(ActiveRun run, RunTile t, ContentDb content)
        {
            var def = content.BoardTiles.TryGetValue(t.TileId ?? "", out var d) ? d : null;
            var cat = def?.Category ?? TileCategory.Empty;
            return new TileLook
            {
                Label = def != null ? Theme.TileLabel(cat) : "",
                Accent = def != null ? Theme.TileAccent(cat, def.Valence) : Theme.Border,
                Current = t.Id == run.Position,
                Visited = t.Id != run.Position && run.Trail.Contains(t.Id),
                Fork = t.Next.Count > 1,
                Option = run.Phase == RunPhase.ChooseBranch && run.BranchOptions.Contains(t.Id),
                Gate = cat == TileCategory.BossGate,
                Empty = cat == TileCategory.Empty,
            };
        }

        /// <summary>Gain / risk chips of an event option, from its cost and the effects of its outcomes (pure; tested).</summary>
        public static List<KeyValuePair<bool, string>> OptionTags(EventOption o)
        {
            var tags = new List<KeyValuePair<bool, string>>();
            void Add(bool good, string text) { if (!tags.Exists(k => k.Value == text)) tags.Add(new KeyValuePair<bool, string>(good, text)); }
            if (o.CoinCost > 0) Add(false, "COST " + o.CoinCost + " coins");
            if (o.ShardCost > 0) Add(false, "COST " + o.ShardCost + " shards");
            bool chance = o.Outcomes.Count > 1;
            foreach (var oc in o.Outcomes)
                foreach (var fx in oc.Effects)
                {
                    switch (fx.Kind)
                    {
                        case RunEffectKind.GainSkill: Add(true, "GAIN " + (fx.MinRarity > Rarity.Common ? fx.MinRarity + "+ " : "") + "skill"); break;
                        case RunEffectKind.GainSpecificSkill: Add(true, "GAIN skill"); break;
                        case RunEffectKind.UpgradeSkill: Add(true, "UPGRADE a skill"); break;
                        case RunEffectKind.LoseRandomSkill: Add(false, "RISK lose a skill"); break;
                        case RunEffectKind.HealPct: Add(true, "HEAL " + Fmt.Pct(fx.Value)); break;
                        case RunEffectKind.DamagePct: Add(false, "RISK −" + Fmt.Pct(fx.Value) + " HP"); break;
                        case RunEffectKind.StatPercent:
                            if (fx.Value >= 0) Add(true, "+" + Fmt.Pct(fx.Value) + " " + fx.Stat.ToString().ToUpperInvariant());
                            else Add(false, "RISK −" + Fmt.Pct(-fx.Value) + " " + fx.Stat.ToString().ToUpperInvariant());
                            break;
                        case RunEffectKind.Loot: case RunEffectKind.PendingReward: Add(true, "KEEP loot (paid on clear)"); break;
                        case RunEffectKind.Coins: Add(fx.Value >= 0, (fx.Value >= 0 ? "+" : "−") + Math.Abs(fx.Value).ToString("0") + " coins"); break;
                        case RunEffectKind.Xp: Add(true, "+XP"); break;
                        case RunEffectKind.Shards: Add(true, "+shards"); break;
                        case RunEffectKind.BossStatPct: Add(false, "RISK boss +" + Fmt.Pct(fx.Value)); break;
                        case RunEffectKind.Meter: Add(fx.Value < 0, fx.Value < 0 ? "Boss sleeps longer" : "RISK boss wakes"); break;
                        case RunEffectKind.Battle: Add(false, "RISK fight"); break;
                        case RunEffectKind.Danger: Add(false, "RISK +danger"); break;
                        case RunEffectKind.StrengthenEnemies: Add(false, "RISK stronger enemies"); break;
                    }
                }
            if (chance && tags.Count > 0) tags.Insert(0, new KeyValuePair<bool, string>(false, "CHANCE"));
            return tags;
        }

        // ---- build -------------------------------------------------------------------------------

        protected override void BuildHeaderExtras(RectTransform header)
        {
            var chip = Ui.Chip(header, "", Theme.Panel, Theme.Text, 28, 64, null, "CoinsChip");
            _coins = chip.GetComponentInChildren<Text>();
            var dot = Ui.Dot(chip, Theme.Gold, 26);
            dot.transform.SetAsFirstSibling();
            chip.GetComponent<HorizontalLayoutGroup>().spacing = 10;
        }

        public override void Tick()
        {
            base.Tick();
            var run = State?.Run.Active;
            if (_coins != null) _coins.text = run != null ? Fmt.Num(run.Coins) : "-";
        }

        protected override void Build(RectTransform body)
        {
            var run = State.Run.Active;
            if (run == null) { BuildFinished(body); return; }
            var level = Content.RunLevel(run.LevelId);
            bool newRun = run.RunId != _runId;
            if (newRun) { _runId = run.RunId; _trailSeen = run.Trail.Count; _rollsSeen = run.Rolls; _battlesSeen = run.BattleCount; }

            Hud(body, run, level);
            var token = Board(body, run, out var dice, out float boardH, out var focus);
            Controls(run, dice);
            if (focus != null) ScrollToAfterBuild(focus);

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
                    var from = DrawPoint(TileCenter(run.Tiles[path[i - 1]]), boardH);
                    var to = DrawPoint(TileCenter(run.Tiles[path[i]]), boardH);
                    _anim.Then(0.16f, t =>
                    {
                        // a little hop per step
                        token.anchoredPosition = Vector2.Lerp(from, to, t) + new Vector2(0, 18 * Mathf.Sin(t * Mathf.PI));
                    });
                }
            }
            if (battle)
            {
                var result = run.LastBattle;
                _anim.Call(() => App.Router.Push(new BattlePlaybackScreen(result, _auto)));
            }
            _anim.Call(() => ShowDecision(run));
        }

        private static string DiceText(ActiveRun run) => run.LastDice.Count == 0 ? "-" : string.Join(" + ", run.LastDice.ConvertAll(d => d.ToString()));

        private void Hud(RectTransform body, ActiveRun run, LevelDef level)
        {
            var hp = Ui.Row(body, 44, 14);
            var hpLabel = Ui.Label(hp, "HP", 24, Theme.Text);
            hpLabel.fontStyle = FontStyle.Bold;
            Ui.Size(hpLabel, 50, -1, 0);
            double frac = run.WormMaxHp > 0 ? run.WormHp / run.WormMaxHp : 1;
            Ui.Bar(hp, (float)frac, frac > 0.5 ? Theme.Good : frac > 0.25 ? Theme.Warn : Theme.Bad, null, 26);
            var hpText = Ui.Display(hp, Fmt.Num(Math.Max(0, run.WormHp)) + " / " + Fmt.Num(run.WormMaxHp), 28, Theme.Text, TextAnchor.MiddleRight);
            Ui.Size(hpText, 230, -1, 0);

            var chips = Ui.Row(body, 56, 10);
            int need = Content.RunTuning.XpBase + Content.RunTuning.XpPerLevel * (run.Level - 1);
            var lvl = Ui.Bar(chips, need > 0 ? (float)run.Xp / need : 0, Theme.Premium, "Lv " + run.Level + " · XP " + run.Xp + "/" + need, 44);
            Ui.Size(lvl.Fill.parent.GetComponent<Image>(), -1, 44, 1);
            Ui.Chip(chips, "Danger " + run.Danger, run.Danger > 0 ? Theme.Tint(Theme.Bad, 0.35f) : Theme.Panel, run.Danger > 0 ? Theme.Hex(0xFFB3B5) : Theme.TextFaint, 24, 48);
            if (run.Shards > 0) Ui.Chip(chips, "Shards " + run.Shards, Theme.Tint(Theme.Premium, 0.3f), Theme.Premium, 24, 48);
            Ui.Button(chips, "Auto-pick: " + (Farming ? "On" : "Off"), ToggleFarming, Farming ? Theme.Premium : Theme.Panel, 250, 52, true, 24).name = "AutoPickToggle";

            // the boss waiting at the top of the board, with its awakening
            var boss = run.BossId != null && Content.RunBosses.TryGetValue(run.BossId, out var bd) ? bd : null;
            var banner = Ui.Row(body, 76, 14, Theme.Hex(0x3A1414), 22, "BossBanner");
            UiShapes.Outline(banner, Theme.Bad, 26, 5);
            banner.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Ui.Display(banner, "BOSS · " + (boss?.Name ?? "Boss"), 32, Theme.Hex(0xFF9FA2), TextAnchor.MiddleLeft);
            var awake = Ui.Bar(banner, (float)(run.Awakening / 100.0), Theme.Bad, run.Awakening.ToString("0") + "% awake", 30);
            Ui.Size(awake.Fill.parent.GetComponent<Image>(), 300, 30, 0);
        }

        private RectTransform Board(RectTransform body, ActiveRun run, out Text dice, out float h, out RectTransform focus)
        {
            h = BoardHeight(run);
            float boardH = h;
            var holder = Ui.Rect(body, "BoardHolder");
            Ui.Size(holder, -1, h);
            var board = Ui.Rect(holder, "Board");
            board.anchorMin = board.anchorMax = new Vector2(0.5f, 1);
            board.pivot = new Vector2(0.5f, 1);
            board.sizeDelta = new Vector2(BoardWidth, h);
            board.anchoredPosition = Vector2.zero;
            Vector2 P(RunTile t) => DrawPoint(TileCenter(t), boardH);

            // edges first (behind tiles): walked edges lit, loop-backs gold, the rest dark
            var walked = new HashSet<long>();
            for (int i = 1; i < run.Trail.Count; i++) walked.Add(((long)run.Trail[i - 1] << 32) | (uint)run.Trail[i]);
            foreach (var t in run.Tiles)
                foreach (var n in t.Next)
                    if (n >= 0 && n < run.Tiles.Count)
                    {
                        bool lit = walked.Contains(((long)t.Id << 32) | (uint)n);
                        var color = lit ? Theme.Shade(Theme.AccentLight, 0.8f) : n < t.Id ? Theme.Shade(Theme.Gold, 0.55f) : Theme.Border;
                        Edge(board, P(t), P(run.Tiles[n]), color, lit ? 12 : 10);
                    }

            RectTransform currentTile = null;
            foreach (var t in run.Tiles)
            {
                var look = LookOf(run, t, Content);
                var center = P(t);
                if (look.Option)
                {
                    var glow = Ui.RoundPanel(board, new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0.45f), 26, "Option");
                    glow.raycastTarget = false;
                    PlaceAt(glow.rectTransform, center, new Vector2(TileW + 26, TileH + 26));
                    UiPulse.On(glow, 0.05f, 1.1f, 0.5f);
                }
                var size = look.Gate ? new Vector2(TileW + GateGrow, TileH + GateGrow) : new Vector2(TileW, TileH);
                Color fill, border, text;
                if (look.Visited) { fill = Theme.Hex(0x201416); border = Theme.Hex(0x4A3330); text = Theme.Hex(0x6E5550); }
                else if (look.Gate) { fill = Theme.Hex(0x3A1414); border = Theme.Bad; text = Theme.Hex(0xFF9FA2); }
                else if (look.Empty) { fill = Theme.PanelAlt; border = Theme.Border; text = Theme.TextFaint; }
                else { fill = Theme.Panel; border = look.Accent; text = Color.Lerp(look.Accent, Color.white, 0.55f); }
                if (look.Fork && !look.Visited) { fill = Theme.Hex(0x2B2414); }
                if (t.BonusPct > 0 && !look.Visited) fill = Color.Lerp(fill, Theme.Bad, 0.25f);
                var img = Ui.RoundPanel(board, fill, 16, "Tile:" + t.Id);
                img.raycastTarget = false;
                PlaceAt(img.rectTransform, center, size);
                UiShapes.Outline(img.rectTransform, border, 16, look.Gate ? 5 : 4);
                if (look.Current) currentTile = img.rectTransform;
                string label = look.Label;
                if (look.Fork && !look.Visited) label = string.IsNullOrEmpty(label) ? "FORK" : label + "\n<size=15>fork</size>";
                if (look.Option) label = (run.BranchOptions.IndexOf(t.Id) == 0 ? "A · " : "B · ") + label;
                if (!string.IsNullOrEmpty(label))
                {
                    var lab = Ui.Display(img.transform, label, look.Gate ? 26 : label.Length > 6 ? 17 : 20, text, TextAnchor.MiddleCenter);
                    lab.lineSpacing = 0.8f;
                    Ui.Stretch(lab.rectTransform, 2, 2, 2, 2);
                }
                if (t.BonusPct > 0 && !look.Visited)
                {
                    var pip = Ui.Chip(img.transform, "+" + t.BonusPct.ToString("0") + "%", Theme.Bad, Theme.Text, 16, 26, null, "Bonus");
                    pip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    pip.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                    pip.anchorMin = pip.anchorMax = new Vector2(1, 1); pip.pivot = new Vector2(0.7f, 0.6f);
                    pip.sizeDelta = new Vector2(0, 26); pip.anchoredPosition = Vector2.zero;
                }
            }
            // boss on the board, next to its gate
            var gateTile = run.BossGateTile >= 0 && run.BossGateTile < run.Tiles.Count ? run.Tiles[run.BossGateTile] : null;
            if (gateTile != null)
            {
                var p = DrawPoint(BossMarkerCenter(run.Tiles, run.BossGateTile, new Vector2(BoardWidth, h)), h);
                var bossImg = Ui.RoundPanel(board, Theme.Shade(Theme.ValenceColor(Valence.Boss), 0.55f), 25, "Boss");
                bossImg.raycastTarget = false;
                PlaceAt(bossImg.rectTransform, p, BossMarkerSize);
                var bl = Ui.Display(bossImg.transform, "BOSS " + run.Awakening.ToString("0") + "%", 20, Theme.Text, TextAnchor.MiddleCenter);
                Ui.Stretch(bl.rectTransform);
            }

            // token (worm): a glowing tile-sized plate with the worm bobbing on it; moved along the path by the animation
            var cur = run.Position >= 0 && run.Position < run.Tiles.Count ? run.Tiles[run.Position] : null;
            var start = cur;
            int prevIndex = Math.Max(0, Math.Min(_trailSeen, run.Trail.Count) - 1);
            if (run.Trail.Count > _trailSeen && _trailSeen > 0) start = run.Tiles[run.Trail[prevIndex]];
            RectTransform token = null;
            if (start != null)
            {
                var plate = Ui.RoundPanel(board, Theme.Accent, 18, "Token");
                plate.raycastTarget = false;
                token = plate.rectTransform;
                PlaceAt(token, P(start), new Vector2(TileW + 8, TileH + 8));
                UiShapes.Outline(token, Theme.Cream, 18, 5);
                var glow = Ui.RoundPanel(token, new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f), 30, "Glow");
                glow.raycastTarget = false;
                Ui.Stretch(glow.rectTransform, -16, -16, -16, -16);
                glow.transform.SetAsFirstSibling();
                UiPulse.On(glow, 0.04f, 1.6f, 0.5f);
                var worm = Ui.RoundPanel(token, Theme.Worm, 22, "Worm");
                worm.raycastTarget = false;
                var wrt = worm.rectTransform;
                wrt.anchorMin = wrt.anchorMax = new Vector2(0.5f, 0.5f);
                wrt.sizeDelta = new Vector2(72, 34);
                wrt.anchoredPosition = new Vector2(0, 2);
                UiShapes.Outline(wrt, Theme.WormLight, 17, 4);
                var eye = Ui.Dot(wrt, Theme.OnAccent, 9, "Eye");
                eye.rectTransform.anchorMin = eye.rectTransform.anchorMax = new Vector2(0.8f, 0.62f);
                eye.rectTransform.sizeDelta = new Vector2(9, 9);
                UiBob.On(wrt, 5, 1.6f);
            }
            focus = currentTile;

            // dice + last effect text: first row of the fixed bottom bar
            var info = Ui.Row(Footer ?? body, InfoRowH, 12, null, 0, "Info");
            var diceChip = Ui.RoundPanel(info, Theme.Cream, 16, "Dice");
            Ui.Size(diceChip, 150, InfoRowH, 0);
            dice = Ui.Display(diceChip.transform, DiceText(run), 30, Theme.OnAccent, TextAnchor.MiddleCenter);
            Ui.Stretch(dice.rectTransform);
            var land = cur != null && Content.BoardTiles.TryGetValue(cur.TileId ?? "", out var ld) ? ld : null;
            var what = Ui.Label(info, run.LastEventText ?? (land != null ? land.Name + (string.IsNullOrEmpty(land.Text) ? "" : ": " + land.Text) : ""), 24, Theme.TextDim);
            what.verticalOverflow = VerticalWrapMode.Truncate;
            return token;
        }

        private static void PlaceAt(RectTransform rt, Vector2 anchored, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchored;
        }

        private static void Edge(RectTransform board, Vector2 pa, Vector2 pb, Color color, float width)
        {
            var img = Ui.RoundPanel(board, color, width / 2, "Edge");
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = (pa + pb) / 2;
            rt.sizeDelta = new Vector2(Vector2.Distance(pa, pb) + width, width);
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(pb.y - pa.y, pb.x - pa.x) * Mathf.Rad2Deg);
        }

        private void Controls(ActiveRun run, Text dice)
        {
            var bar = Footer;
            if (bar == null) return;
            // skill slots
            var slots = Ui.Row(bar, SlotRowH, 10, null, 0, "Slots");
            var skills = RunSystem.ActiveSkillIds(run);
            for (int i = 0; i < SkillSlots; i++)
            {
                if (i < skills.Count && (i < SkillSlots - 1 || skills.Count == SkillSlots))
                {
                    var s = Content.Skill(skills[i]);
                    var fam = Theme.FamilyColor(s?.Family ?? SkillFamily.None);
                    var b = Ui.Button(slots, s?.Name ?? skills[i], () => SkillsModal(run), Theme.Tint(fam, 0.3f), -1, SlotRowH, true, 22);
                    b.GetComponentInChildren<Text>().color = Color.Lerp(fam, Color.white, 0.55f);
                }
                else if (i < skills.Count)
                    Ui.Button(slots, "+" + (skills.Count - i) + " more", () => SkillsModal(run), Theme.Panel, -1, SlotRowH, true, 22);
                else
                {
                    var empty = Ui.Button(slots, "empty", () => SkillsModal(run), new Color(0, 0, 0, 0), -1, SlotRowH, true, 22);
                    empty.GetComponentInChildren<Text>().color = Theme.TextFaint;
                    UiShapes.Outline((RectTransform)empty.transform, Theme.Border, Theme.RadiusButton, 3);
                }
            }

            var row = Ui.Row(bar, RollRowH, 14, null, 0, "RollRow");
            var loot = Ui.Button(row, "Loot\n<size=30>" + run.PendingLoot.Count + "</size>", () => LootModal(run), Theme.Panel, 150, RollRowH, true, 26);
            loot.GetComponentInChildren<Text>().color = Theme.Gold;
            bool canRoll = run.Phase == RunPhase.AwaitRoll;
            bool deciding = !canRoll && run.Phase != RunPhase.Completed && run.Phase != RunPhase.Failed;
            string rollText = !_auto
                ? canRoll ? "ROLL\n<size=24>hold for auto</size>" : "CHOOSE\n<size=24>your decision</size>"
                : AutoPaused ? "AUTO PAUSED\n<size=24>your choice</size>" : "AUTO…";
            var roll = Ui.Button(row, rollText, null, canRoll || _auto && !AutoPaused ? Theme.Accent : deciding ? Theme.Gold : Theme.Locked, -1, RollRowH, canRoll || _auto || deciding, 56);
            roll.name = "RollButton";
            var hold = roll.gameObject.AddComponent<HoldButton>();
            hold.OnTap = () =>
            {
                if (_auto) StopAuto();
                else if (!Animating)
                {
                    var r = State.Run.Active;
                    if (r != null && r.Phase == RunPhase.AwaitRoll) Run(new RunRoll());
                    else if (r != null) { _decisionKey = null; ShowDecision(r); }
                }
            };
            hold.OnHold = StartAuto;
            var auto = Ui.Button(row, "AUTO\n<size=24>" + (_auto ? "on" : "off") + "</size>", () => { if (_auto) StopAuto(); else StartAuto(); },
                _auto ? Theme.Tint(Theme.Premium, 0.35f) : Theme.Panel, 150, RollRowH, true, 30);
            auto.name = "AutoButton";
            auto.GetComponentInChildren<Text>().color = _auto ? Theme.Premium : Theme.TextDim;
            if (_auto) UiShapes.Outline((RectTransform)auto.transform, Theme.Premium, Theme.RadiusButton, 4);

            var hint = Ui.Label(bar, _auto && AutoPaused ? "AUTO PAUSED — your choice. Auto continues after you decide."
                    : "Auto rolls and fights for you, and stops at shops, events, chests, forks and drafts.",
                22, _auto && AutoPaused ? Theme.Gold : Theme.TextDim, TextAnchor.MiddleCenter, "Hint");
            Ui.Size(hint, -1, HintRowH);
        }

        private void PauseModal()
        {
            var run = State?.Run.Active;
            if (run == null) { App.Router.Pop(); return; }
            Modal("Paused", b =>
            {
                Ui.Label(b, "Pending loot: " + run.PendingLoot.Count + " items · " + run.Coins + " coins", Theme.FontNormal, Theme.TextDim, TextAnchor.MiddleCenter);
                Note(b, "Leaving keeps the run: continue it from the portals. Abandoning loses all pending loot.");
            },
            new ModalButton("Abandon", () => Modal("Abandon the run?", b => Note(b, "All pending loot (" + run.PendingLoot.Count + " items) is lost."),
                new ModalButton("Keep going", null), new ModalButton("Abandon", () => Run(new RunAbandon()), Theme.Bad)), Theme.Bad),
            new ModalButton("Leave", () => App.Router.Pop()),
            new ModalButton("Resume", null, Theme.Accent));
        }

        private void BuildFinished(RectTransform body)
        {
            var summary = State.Run.LastSummary;
            Ui.Display(body, "Run finished", Theme.FontTitle, Theme.Cream, TextAnchor.MiddleCenter);
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
            Ui.Button(body, "See results", () => App.Router.Replace(new RunResultScreen()), Theme.Accent, -1, 120, true, Theme.FontLarge);
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
            var run = State.Run.Active;
            if (run != null && RunSystem.RequiresPlayer(Content, run, Farming))
            {
                // paused at a player decision: no tap-to-stop overlay (the decision UI must be usable); resumes after
                RemoveAutoOverlay();
                _autoTimer = 0;
                string key = run.RunId + "|" + run.Phase + "|" + run.Rolls + "|" + run.EventId + "|" + run.BattleCount;
                if (key != _pausedKey)
                {
                    _pausedKey = key;
                    Session.Info("Auto paused — your choice");
                }
                return;
            }
            EnsureAutoOverlay();
            if (Animating || Session.InFlight > 0 || App.Router.Current != this) { _autoTimer = 0; return; }
            _autoTimer += dt;
            if (_autoTimer < 0.6f) return;
            _autoTimer = 0;
            if (run == null) { StopAuto(); return; }
            CloseDecision();
            Run(new RunAutoStep { Farming = Farming }, r => { if (!r.Ok) StopAuto(); });
        }

        /// <summary>Toggle "Auto-pick drafts (farming)" (persisted per device).</summary>
        public void ToggleFarming()
        {
            App.Settings?.SetAutoPickDrafts(!Farming);
            Rebuild();
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
            _pausedKey = null;
            RemoveAutoOverlay();
            if (Root != null) Rebuild();
        }

        private void RemoveAutoOverlay()
        {
            if (_autoOverlay != null) Ui.DestroyGo(_autoOverlay);
            _autoOverlay = null;
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
                buttons.Add(new ModalButton((i == 0 ? "Path A" : "Path B") + ": " + string.Join(", ", names), () => Run(new RunChooseBranch { Index = index }), i == 0 ? Theme.Accent : Theme.Gold));
            }
            return Modal("Fork in the path", b =>
            {
                Note(b, "The glowing tiles on the board are your options (A / B). " + run.StepsLeft + " steps left.");
                for (int i = 0; i < run.BranchOptions.Count; i++)
                {
                    var row = Ui.ChipRow(b, 52, 8);
                    Ui.Chip(row, i == 0 ? "A" : "B", i == 0 ? Theme.Accent : Theme.Gold, Theme.OnAccent, 24, 48);
                    foreach (var id in RunBoard.Ahead(run.Tiles, run.Tiles[run.BranchOptions[i]].Id, 4))
                    {
                        var look = LookOf(run, run.Tiles[id], Content);
                        if (string.IsNullOrEmpty(look.Label)) continue;
                        Ui.Chip(row, look.Label.Replace("\n", " "), Theme.Panel, Color.Lerp(look.Accent, Color.white, 0.5f), 20, 44, look.Accent);
                    }
                }
            }, buttons.ToArray());
        }

        private string TileName(RunTile t) => Content.BoardTiles.TryGetValue(t.TileId ?? "", out var d) ? d.Name : t.TileId;

        private UiModal EventModal(ActiveRun run)
        {
            Content.RunEvents.TryGetValue(run.EventId ?? "", out var ev);
            var deep = Theme.Hex(0x1C1F3A);
            return Ui.Sheet(App.ModalLayer, ev?.Name ?? "Event", deep, Theme.Hex(0x3E4690), (box, modal) =>
            {
                var head = Ui.Row(box, 130, 22);
                var portrait = Ui.RoundPanel(head, Theme.Hex(0x2E3570), 36, "Portrait");
                Ui.Size(portrait, 130, 130, 0);
                UiShapes.Outline(portrait.rectTransform, Theme.Hex(0x6E78C8), 36, 4);
                var glyph = Ui.Display(portrait.transform, "?", 72, Theme.Hex(0xAEB6F2), TextAnchor.MiddleCenter);
                Ui.Stretch(glyph.rectTransform);
                var titles = Ui.Column(head, 2, 0, null, "Titles");
                titles.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                Ui.Size(titles, -1, -1, 1);
                Ui.Caption(titles, "Event", Theme.Premium);
                Ui.Display(titles, ev?.Name ?? "Event", 50, Theme.Text);
                Ui.Label(box, ev?.Text ?? run.EventId, Theme.FontNormal, Theme.Hex(0xD7DBFA));
                if (ev == null)
                {
                    Ui.Button(box, "Continue", () => { modal.Close(); Run(new RunAutoStep { Farming = true }); }, Theme.Accent);
                    return;
                }
                for (int i = 0; i < ev.Options.Count; i++)
                {
                    int index = i;
                    var o = ev.Options[i];
                    string cost = (o.CoinCost > 0 ? " [" + o.CoinCost + " coins]" : "") + (o.ShardCost > 0 ? " [" + o.ShardCost + " shards]" : "");
                    bool affordable = run.Coins >= o.CoinCost && run.Shards >= o.ShardCost;
                    bool def = i == ev.DefaultOption;
                    var btn = Ui.CardButton(box, "Button:" + o.Text + cost, Theme.Hex(0x262C5C), def ? Theme.Premium : Theme.Gold,
                        () => { modal.Close(); Run(new RunChooseEventOption { Index = index }); }, out var card, 36, 24, 10);
                    btn.interactable = affordable;
                    Ui.Label(card, o.Text, Theme.FontNormal, Theme.Text).fontStyle = FontStyle.Bold; // the cost is a chip below
                    var tags = OptionTags(o);
                    if (tags.Count > 0)
                    {
                        var row = Ui.ChipRow(card, 48, 8);
                        int shown = 0;
                        foreach (var tag in tags)
                        {
                            if (shown++ >= 3) break;
                            Ui.Chip(row, tag.Value, tag.Key ? Theme.Hex(0x173D2A) : Theme.Hex(0x4A1A1D), tag.Key ? Theme.Hex(0x9CF0B5) : Theme.Hex(0xFFB3B5), 22, 44);
                        }
                    }
                    if (!string.IsNullOrEmpty(o.RiskText)) Ui.Label(card, o.RiskText, 24, Theme.Hex(0xAEB6F2));
                    if (!affordable) Ui.Label(card, "Not enough " + (run.Coins < o.CoinCost ? "coins" : "shards"), 22, Theme.Bad);
                }
                Ui.Label(box, _auto ? "Auto mode waits for your choice." : "Tap an option to decide.", 22, Theme.Hex(0x8E95C8), TextAnchor.MiddleCenter);
            });
        }

        /// <summary>"Fire 2 · Crit 1": the run's skills by family.</summary>
        private string BuildSummary(ActiveRun run)
        {
            var counts = new Dictionary<SkillFamily, int>();
            var order = new List<SkillFamily>();
            foreach (var id in RunSystem.ActiveSkillIds(run))
            {
                var f = Content.Skill(id)?.Family ?? SkillFamily.None;
                if (f == SkillFamily.None) continue;
                if (!counts.ContainsKey(f)) { counts[f] = 0; order.Add(f); }
                counts[f]++;
            }
            var parts = new List<string>();
            foreach (var f in order) parts.Add(f + " " + counts[f]);
            return parts.Count == 0 ? "no skills yet" : string.Join(" · ", parts);
        }

        private UiModal DraftModal(ActiveRun run)
        {
            var owned = new HashSet<SkillFamily>();
            foreach (var id in RunSystem.ActiveSkillIds(run)) owned.Add(Content.Skill(id)?.Family ?? SkillFamily.None);
            return Ui.Sheet(App.ModalLayer, "Choose a skill", Theme.Hex(0x16122E), Theme.Hex(0x5A4FB0), (box, modal) =>
            {
                Ui.Display(box, "NEW SKILL!", 76, Theme.Gold, TextAnchor.MiddleCenter, "Title");
                Ui.Label(box, "Choose 1 of " + run.DraftOffer.Count + " · your build: " + BuildSummary(run), Theme.FontSmall, Theme.Hex(0xC9C2F5), TextAnchor.MiddleCenter);
                foreach (var id in run.DraftOffer)
                {
                    string sid = id;
                    var s = Content.Skill(sid);
                    var fam = s?.Family ?? SkillFamily.None;
                    var famColor = Theme.FamilyColor(fam);
                    var rarity = s?.Rarity ?? Rarity.Common;
                    var btn = Ui.CardButton(box, "Button:Pick", Theme.Tint(famColor, 0.2f), famColor,
                        () => { modal.Close(); CloseDecision(); Run(new RunDraftPick { SkillId = sid }); }, out var card, 40, 24, 8);
                    var row = Ui.Row(card, -1, 22, null, 0, "Draft:" + sid);
                    row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
                    row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
                    var icon = Ui.RoundPanel(row, famColor, 30, "Icon");
                    Ui.Size(icon, 130, 130, 0);
                    var letter = Ui.Display(icon.transform, fam == SkillFamily.None ? "★" : fam.ToString().Substring(0, 1), 64, Theme.TextOn(famColor), TextAnchor.MiddleCenter);
                    Ui.Stretch(letter.rectTransform);
                    var text = Ui.Column(row, 4, 0, null, "Text");
                    Ui.Size(text, -1, -1, 1);
                    var tags = Ui.ChipRow(text, 40, 8);
                    var cap = Ui.Label(tags, (fam == SkillFamily.None ? "" : fam.ToString().ToUpperInvariant() + " · ") + rarity.ToString().ToUpperInvariant(), 22, Color.Lerp(famColor, Color.white, 0.35f));
                    cap.fontStyle = FontStyle.Bold;
                    Ui.Size(cap, -1, -1, 0);
                    cap.horizontalOverflow = HorizontalWrapMode.Overflow;
                    if (s?.UpgradesFrom != null) Ui.Chip(tags, "UPGRADE", Theme.Hex(0xD6C2FF), Theme.Hex(0x1D0B33), 18, 34);
                    else if (fam != SkillFamily.None && owned.Contains(fam)) Ui.Chip(tags, "SYNERGY", Theme.Gold, Theme.OnAccent, 18, 34);
                    Ui.Chip(tags, rarity.ToString(), Theme.Tint(Theme.RarityColor(rarity), 0.4f), Theme.RarityColor(rarity), 18, 34, Theme.RarityColor(rarity));
                    Ui.Display(text, s?.Name ?? sid, 40, Theme.Text);
                    string full = Fmt.SkillFull(s);
                    if (!string.IsNullOrEmpty(full)) Ui.Label(text, full, 24, Color.Lerp(famColor, Color.white, 0.7f)).name = "SkillText";
                    if (s?.UpgradesFrom != null) Ui.Label(text, "Upgrades " + (Content.Skill(s.UpgradesFrom)?.Name ?? s.UpgradesFrom), 22, Theme.Good);
                }
                var reroll = Ui.Button(box, "Reroll (" + run.DraftRerolls + " left)", () => Run(new RunDraftReroll()), new Color(0, 0, 0, 0), -1, 84, run.DraftRerolls > 0, 30);
                UiShapes.Outline((RectTransform)reroll.transform, Theme.Hex(0x5A4FB0), Theme.RadiusButton, 4);
                reroll.GetComponentInChildren<Text>().color = Theme.Hex(0xE6DBFF);
            });
        }

        private UiModal ShopModal(ActiveRun run)
        {
            var table = Content.RunShopTables.TryGetValue(Content.RunLevel(run.LevelId)?.ShopTableId ?? "", out var st) ? st : null;
            long reroll = table != null ? table.RerollBase + table.RerollStep * run.ShopRerolls : 0;
            return Modal("Shop · " + run.Coins + " coins", b =>
            {
                for (int i = 0; i < run.Shop.Count; i++)
                {
                    int index = i;
                    var o = run.Shop[i];
                    bool skill = o.Kind == ShopOfferKind.Skill;
                    var sk = skill ? Content.Skill(o.SkillId) : null;
                    string what = skill ? (sk?.Name ?? o.SkillId)
                        : o.Kind == ShopOfferKind.Heal ? "Heal " + Fmt.Pct(o.Value)
                        : o.Stat != null ? RogueStars.Rules.Core.SkillText.Stat(o.Stat) ?? "+stat" : "+stat";
                    var color = skill ? Theme.FamilyColor(sk?.Family ?? SkillFamily.None) : Theme.Good;
                    var row = Ui.Row(b, -1, 14, o.Sold ? Theme.PanelAlt : Theme.Tint(color, 0.16f), 20, "Offer");
                    var rl = row.GetComponent<HorizontalLayoutGroup>();
                    rl.childAlignment = TextAnchor.MiddleLeft; rl.childForceExpandHeight = false;
                    rl.padding = new RectOffset(20, 20, 16, 16);
                    UiShapes.Outline(row, o.Sold ? Theme.Border : Theme.Shade(color, 0.8f), Theme.RadiusButton, 3);
                    var icon = Ui.RoundPanel(row, o.Sold ? Theme.Locked : color, 18, "Icon");
                    Ui.Size(icon, 64, 64, 0);
                    var words = Ui.Column(row, 0, 0, null, "Words");
                    Ui.Size(words, -1, -1, 1);
                    words.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                    // the offer's name is its own text (the tests look the buy button up by the name next to it)
                    Ui.Label(words, what, Theme.FontNormal, o.Sold ? Theme.TextFaint : Theme.Text).fontStyle = FontStyle.Bold;
                    if (skill && sk != null) Ui.Label(words, (sk.Family != SkillFamily.None ? sk.Family + " · " : "") + sk.Rarity, 22, Theme.RarityColor(sk.Rarity));
                    // Full text: what the offer actually does (a skill name alone means nothing to a new player).
                    string detail = skill ? Fmt.SkillFull(sk)
                        : o.Kind == ShopOfferKind.Heal ? "Restores " + Fmt.Pct(o.Value) + " of your max HP now."
                        : "Raises the stat for the rest of this run.";
                    if (!string.IsNullOrEmpty(detail)) Ui.Label(words, detail, 24, o.Sold ? Theme.TextFaint : Color.Lerp(color, Color.white, 0.7f)).name = "OfferText";
                    if (skill && sk?.UpgradesFrom != null) Ui.Label(words, "Upgrades " + (Content.Skill(sk.UpgradesFrom)?.Name ?? sk.UpgradesFrom), 22, Theme.Good);
                    Ui.Button(row, o.Sold ? "Sold" : o.Price + " coins", () => Run(new RunShopBuy { Index = index }), Theme.Gold, 220, 76, !o.Sold && run.Coins >= o.Price, 28);
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
                if (run.PendingLoot.Count == 0) Ui.Label(b, "Nothing yet.", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter);
                foreach (var r in run.PendingLoot) Ui.Label(b, "+ " + Fmt.Reward(r, Content), Theme.FontSmall, Theme.Gold);
                Note(b, "Paid out only when you beat the boss. Each item raises the danger level (+" + Fmt.Pct(run.Danger * Content.RunTuning.DangerPctPerLoot) + " enemy stats now).");
            });

        private void SkillsModal(ActiveRun run) =>
            Modal("Skills", b =>
            {
                var ids = RunSystem.ActiveSkillIds(run);
                if (ids.Count == 0) Note(b, "No skills yet: level up on the board to draft some.");
                foreach (var id in ids)
                {
                    var s = Content.Skill(id);
                    var fam = Theme.FamilyColor(s?.Family ?? SkillFamily.None);
                    var row = Ui.Row(b, 72, 12, Theme.Tint(fam, 0.2f), 16);
                    var dot = Ui.Dot(row, fam, 28);
                    Ui.Label(row, (s?.Name ?? id) + " <size=20>(" + (s?.Family ?? SkillFamily.None) + " · " + (s?.Rarity ?? Rarity.Common) + ")</size>", Theme.FontSmall, Color.Lerp(fam, Color.white, 0.5f));
                }
            });
    }

    /// <summary>Run result: VICTORY/DEFEAT, the level's three stars, loot secured (or lost), double rewards, next level.</summary>
    public sealed class RunResultScreen : UiScreen
    {
        public override string Title => "Run result";

        /// <summary>Rewarded ad (instant with No Ads): the won run's rewards once more (<see cref="RunDoubleRewards"/>).</summary>
        private void DoubleRewards(RectTransform body, RunSummary s)
        {
            if (!s.Won || s.Doublable.Count == 0) return;
            if (s.Doubled) { Ui.Display(body, "Rewards doubled!", Theme.FontLarge, Theme.Good, TextAnchor.MiddleCenter); return; }
            int left = ShopSystem.AdViewsLeft(State, Content, RunSystem.DoubleRewardsAdPlacement, Now);
            bool noAds = ShopSystem.HasNoAds(State, Content);
            string label = (noAds ? "Double these rewards" : "Watch ad: double rewards") + " <size=24>(" + (left > 0 ? left + " left today" : "daily limit reached") + ")</size>";
            var b = Ui.Button(body, label, () =>
            {
                if (noAds) { Run(new RunDoubleRewards()); return; }
                Session.Ads.Show(RunSystem.DoubleRewardsAdPlacement, ad =>
                {
                    if (ad.Ok) Run(new RunDoubleRewards { AdToken = ad.Value });
                    else Session.Error(Fmt.Humanize(ad.Error));
                });
            }, new Color(0, 0, 0, 0), -1, 100, left > 0, 30);
            b.name = "DoubleRewards";
            b.GetComponentInChildren<Text>().color = Theme.Cream;
            UiShapes.Outline((RectTransform)b.transform, Theme.Gold, Theme.RadiusButton, 4);
        }

        protected override void Build(RectTransform body)
        {
            var s = State.Run.LastSummary;
            if (s == null) { Note(body, "No run finished yet."); return; }
            var level = Content.RunLevel(s.LevelId);
            var boss = level?.BossId != null && Content.RunBosses.TryGetValue(level.BossId, out var bd) ? bd : null;
            var portal = level != null ? Content.Portal(level.PortalId) : null;

            Ui.Spacer(body, 10);
            var banner = Ui.Display(body, s.Won ? "VICTORY" : "DEFEAT", 120, s.Won ? Theme.Gold : Theme.Bad, TextAnchor.MiddleCenter, "Banner");
            UiTween.PopIn(banner, 0.35f);
            string where = (portal != null ? portal.Name + " · " : "") + (level != null && level.Number > 0 ? "Level " + PortalsScreen.LevelNum(level.Number) : level?.Name ?? s.LevelId)
                           + (s.Won && boss != null ? " · " + boss.Name + " defeated" : "") + (s.Mode != RunMode.Normal ? " · " + s.Mode : "");
            Ui.Label(body, where, Theme.FontNormal, Theme.Hex(0xE8CFA8), TextAnchor.MiddleCenter);

            if (level != null && s.Mode != RunMode.Burrow)
            {
                var prog = RunSystem.Progress(State, Content, s.LevelId);
                string[] feats = { "Cleared", "HP ≥ " + level.FeatHpPct + "%", "Boss in " + level.FeatBossRounds + " rounds" };
                var stars = Ui.Row(body, 190, 30, null, 0, "Stars");
                stars.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                for (int i = 0; i < 3; i++)
                {
                    bool got = i < prog.Feats.Count && prog.Feats[i];
                    bool isNew = s.NewFeats.Contains(i);
                    var col = Ui.Column(stars, 2, 0, null, "Star" + i);
                    Ui.Size(col, 250, -1, 0);
                    var star = Ui.Label(col, got ? "★" : "☆", 120, got ? Theme.Gold : Theme.Hex(0x6B5634), TextAnchor.MiddleCenter, "StarGlyph");
                    Ui.Size(star, -1, 130);
                    if (isNew) UiTween.PopIn(star, 0.5f);
                    Ui.Label(col, feats[i] + (isNew ? "  NEW" : ""), 22, got ? Theme.Hex(0xE8CFA8) : Theme.Hex(0xA88E66), TextAnchor.MiddleCenter);
                }
            }
            var stats = Ui.ChipRow(body, 52, 10);
            stats.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Ui.Chip(stats, "Rolls " + s.Rolls, Theme.Panel, Theme.TextDim, 24, 48);
            Ui.Chip(stats, "Run level " + s.Level, Theme.Panel, Theme.TextDim, 24, 48);
            if (s.Mode == RunMode.Burrow) Ui.Chip(stats, "Floors " + s.Floors, Theme.Panel, Theme.TextDim, 24, 48);

            Ui.Caption(body, s.Won ? "Loot secured" : "Pending loot lost", s.Won ? Theme.Hex(0xE8CFA8) : Theme.Bad);
            if (s.Granted.Count == 0) Note(body, s.Won ? "No rewards." : "Better luck next time.");
            var grid = Ui.Grid(body, new Vector2(300, 170), 3, 14);
            foreach (var r in s.Granted) RewardTile(grid, r, s);
            if (s.BiomassBonus) Ui.Label(body, "Biomass surge ×" + Content.RunTuning.DailyBiomassBonusMult.ToString("0.#") + " applied", Theme.FontNormal, Theme.CurrencyColor(Currencies.Biomass), TextAnchor.MiddleCenter).name = "SurgeApplied";
            if (s.Mode == RunMode.Normal || s.Mode == RunMode.Overcharged) PortalsScreen.SurgeLabel(body, State, Content, Now);
            DoubleRewards(body, s);

            // next: the following main level after a won main run, else replay
            LevelDef next = null;
            if (s.Won && s.Mode == RunMode.Normal && level != null && level.Number > 0)
            {
                var candidate = RunSystem.NextMainLevel(State, Content);
                if (candidate != null && RunSystem.IsLevelUnlocked(State, Content, candidate.Id, RunMode.Normal)) next = candidate;
            }
            var target = next ?? level;
            if (target != null)
            {
                var mode = next != null ? RunMode.Normal : s.Mode;
                string caption = next != null ? "NEXT: LEVEL " + PortalsScreen.LevelNum(next.Number) : s.Won ? "PLAY AGAIN" : "TRY AGAIN";
                var go = Ui.Button(body, caption, () => Run(new RunStart { PortalId = target.PortalId, LevelId = target.Id, Mode = mode }, r => { if (r.Ok) App.Router.Replace(new RunScreen()); }),
                    Theme.Accent, -1, 140, true, 56);
                go.name = "Again";
            }
            var back = Ui.Button(body, "Back to portals", () => App.Router.Pop(), new Color(0, 0, 0, 0), -1, 80, true, 30);
            back.name = "Button:Portals";
            back.GetComponentInChildren<Text>().color = Theme.Hex(0xC9B08A);
        }

        private void RewardTile(Transform grid, Reward r, RunSummary s)
        {
            Color c = r.Kind == RewardKinds.Currency ? Theme.CurrencyColor(r.Id) : r.Rarity > 0 ? Theme.RarityColor((Rarity)Mathf.Clamp(r.Rarity, 0, (int)Rarity.Mythic)) : Theme.Premium;
            var tile = Ui.Card(grid, Theme.Hex(0x2A1D0C), r.Kind == RewardKinds.Currency ? (Color?)null : c, 32, 14, 4, 4, "Reward");
            var lg = tile.GetComponent<VerticalLayoutGroup>();
            lg.childAlignment = TextAnchor.MiddleCenter;
            lg.childForceExpandWidth = false;
            var icon = Ui.RoundPanel(tile, Theme.Shade(c, 0.6f), 18, "Icon");
            Ui.Size(icon, 60, 60, 0);
            icon.rectTransform.sizeDelta = new Vector2(60, 60);
            var line = Ui.Display(tile, "+" + Fmt.Reward(r, Content), 28, Theme.Text, TextAnchor.MiddleCenter);
            line.verticalOverflow = VerticalWrapMode.Truncate;
        }
    }
}
