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
        // Zoomed in: 5 big tiles per row (readable labels, thumb-sized tap targets at forks); the view follows the worm.
        public const int Columns = 5;
        public const float Cell = 186, LaneH = 112, BandGap = 24, Pad = 20;
        public const float TileW = 164, TileH = 94, GateGrow = 22;
        /// <summary>Kept for callers of the old square tiles: the tile height.</summary>
        public const float TileSize = TileH;
        public const float BoardWidth = Columns * Cell + 2 * Pad;
        /// <summary>Boss marker ("BOSS 42%") size; placed in a free cell next to the gate (<see cref="BossMarkerCenter"/>).</summary>
        public static readonly Vector2 BossMarkerSize = new Vector2(150, 64);

        // ---- bottom bar ----
        public const float InfoRowH = 52, SlotRowH = 80, RollRowH = 150, HintRowH = 34;
        public const int SkillSlots = 4;

        private string _runId;
        private int _trailSeen;
        /// <summary>First drawn column of the long board (<see cref="WindowStart"/>).</summary>
        private int _x0;
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
        private int _outcomeSeen;
        private UiModal _outcome;
        private float _outcomeTimer;
        private bool _outcomeRevealing;

        // ---- presentation: follow camera, token, HUD shown values, landing feedback ----
        private RectTransform _viewport, _board, _token, _tokenBody, _tokenShadow, _fx;
        private float _boardH, _viewH;
        /// <summary>Follow camera: the board's absolute rise (layout y with x0 = 0) at the viewport centre; NaN = not placed yet.</summary>
        private float _cam = float.NaN;
        /// <summary>Seconds the camera stays where the player dragged it before following again.</summary>
        private float _camHold;
        private RunHudStats? _hudShown;
        private RectTransform _hudRoot;
        private UiBar _hpBar, _xpBar;
        private Text _hpText, _atkText, _defText, _lvText;
        private DiceView _footerDice;
        /// <summary>Texts of the popups of the latest landing (tests; also passed-tile pickups).</summary>
        public readonly List<string> LastPopups = new List<string>();
        /// <summary>Feedback popups drawn so far (tests).</summary>
        public int PopupsSpawned { get; private set; }
        /// <summary>Tile the worm landed on in the latest move (-1 = none), and whether its landing animation played.</summary>
        public int LastLandedTile { get; private set; } = -1;

        /// <summary>Skip mode for tests: the chance roll of an outcome lands at once (no spinning animation).</summary>
        public static bool InstantOutcomes;
        /// <summary>Auto mode closes an outcome result after this many seconds (after its chance roll).</summary>
        public const float AutoOutcomeSeconds = 1.4f;
        /// <summary>Duration of the spinning chance indicator.</summary>
        public const float ChanceSpinSeconds = 1.3f;

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
                return RollText(Content, run) + " · Boss " + run.Awakening.ToString("0") + "% awake · Threat " + run.Threat
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

        /// <summary>"Roll 12/30": rolls so far and the roll the boss is expected after (its awakening meter is the clock).</summary>
        public static string RollText(ContentDb content, ActiveRun run) => "Roll " + run.Rolls + "/" + RunSystem.BossRollEstimate(content, run);

        /// <summary>
        /// Where the worm lands if it takes branch option <paramref name="option"/> (index into BranchOptions) with the steps
        /// it has left: along the path, stopping early at a forced stop (shop) or at the next fork. Pure; tested.
        /// </summary>
        public static int BranchLanding(ActiveRun run, int option, ContentDb content)
        {
            if (option < 0 || option >= run.BranchOptions.Count) return -1;
            int cur = run.BranchOptions[option];
            int left = Math.Max(0, run.StepsLeft - 1);
            for (int guard = 0; left > 0 && guard < 200; guard++)
            {
                var t = run.Tiles[cur];
                var def = content.BoardTiles.TryGetValue(t.TileId ?? "", out var d) ? d : null;
                if (def != null && def.ForceStop) break;
                if (t.Next.Count != 1) break;
                cur = t.Next[0];
                left--;
            }
            return cur;
        }

        /// <summary>Tiles between a branch option and its landing tile (both included), for the highlighted path.</summary>
        public static List<int> BranchPath(ActiveRun run, int option, ContentDb content)
        {
            var path = new List<int>();
            int land = BranchLanding(run, option, content);
            if (land < 0) return path;
            int cur = run.BranchOptions[option];
            path.Add(cur);
            for (int guard = 0; cur != land && guard < 200; guard++)
            {
                cur = run.Tiles[cur].Next[0];
                path.Add(cur);
            }
            return path;
        }

        /// <summary>
        /// The board is a long path (generated ahead in segments): only a window of it is drawn, from a little behind the
        /// worm (or the start of its current move) to <see cref="WindowColumns"/> columns ahead. The origin is a multiple of two
        /// snake bands, so tiles keep their place while the window moves.
        /// </summary>
        public const int WindowColumns = 14 * Columns;

        public static int WindowStart(ActiveRun run, int fromTile)
        {
            if (run == null || run.Tiles.Count == 0) return 0;
            int x = run.Tiles[Math.Max(0, Math.Min(run.Tiles.Count - 1, fromTile))].X - 2 - Columns; // room behind the worm for the follow camera
            return Math.Max(0, x / (2 * Columns) * (2 * Columns));
        }

        /// <summary>True if the tile is drawn: inside the window, and once the boss arena opened only the walked path and the arena.</summary>
        public static bool InWindow(ActiveRun run, RunTile t, int x0)
        {
            if (t.X < x0 || t.X >= x0 + WindowColumns) return false;
            if (run.BossGateTile >= 0 && t.Id != run.BossGateTile && !run.Trail.Contains(t.Id)) return false;
            return true;
        }

        /// <summary>
        /// Board position (top-left origin, y down) of a tile: snake rows of <see cref="Columns"/>, lanes by Y. This is the ONE
        /// board layout function (tiles, edges, token, camera and tap targets all go through it): when the rules provide
        /// per-tile 2D layout coordinates, only this changes.
        /// </summary>
        public static Vector2 TileCenter(RunTile t) => TileCenter(t, 0);

        public static Vector2 TileCenter(RunTile t, int x0)
        {
            int lx = t.X - x0;
            int band = lx / Columns;
            int c = lx % Columns;
            if (band % 2 == 1) c = Columns - 1 - c;
            float x = Pad + c * Cell + Cell / 2;
            float y = band * (3 * LaneH + BandGap) + (t.Y + 1) * LaneH + LaneH / 2;
            return new Vector2(x, y);
        }

        public static float BoardHeight(ActiveRun run) => BoardHeight(run, WindowStart(run, run.Position));

        public static float BoardHeight(ActiveRun run, int x0)
        {
            int maxX = 0;
            foreach (var t in run.Tiles) if (InWindow(run, t, x0)) maxX = Math.Max(maxX, t.X - x0);
            return (maxX / Columns + 1) * (3 * LaneH + BandGap) + 10;
        }

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
        public static Rect TileRect(RunTile t, bool gate, int x0 = 0)
        {
            float w = gate ? TileW + GateGrow : TileW, h = gate ? TileH + GateGrow : TileH;
            var c = TileCenter(t, x0);
            return new Rect(c.x - w / 2, c.y - h / 2, w, h);
        }

        /// <summary>
        /// Center of the boss marker: the first free cell around the boss gate (beside it, then the lanes above/below,
        /// then two cells away) that overlaps no tile and stays on the board; falls back to the band gap above the gate.
        /// </summary>
        public static Vector2 BossMarkerCenter(List<RunTile> tiles, int gateIndex, Vector2 boardSize, int x0 = 0)
        {
            var gate = tiles.Find(t => t.Id == gateIndex);
            var c = TileCenter(gate, x0);
            var rects = new List<Rect>(tiles.Count);
            foreach (var t in tiles) rects.Add(TileRect(t, t.Id == gateIndex, x0));
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
            /// <summary>Already walked over (in the trail) and not the token's tile. Passed tiles keep their look (board game).</summary>
            public bool Visited;
            /// <summary>The worm landed (stopped) here: only landed tiles are coloured / marked.</summary>
            public bool Landed;
            /// <summary>The worm stands here.</summary>
            public bool Current;
            /// <summary>The path splits after this tile.</summary>
            public bool Fork;
            /// <summary>Resolved (fight won, pickup taken, event decided): drawn with a check mark, never as a new tile.</summary>
            public bool Cleared;
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
                Landed = t.TimesResolved > 0,
                Fork = t.Next.Count > 1,
                Cleared = t.Cleared,
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
                        case RunEffectKind.Loot: case RunEffectKind.PendingReward: Add(true, "ACCOUNT LOOT (paid on clear)"); break;
                        case RunEffectKind.AwakenRatePct: Add(fx.Value < 0, fx.Value < 0 ? "Boss wakes slower" : "RISK boss wakes " + Fmt.Pct(fx.Value) + " faster"); break;
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
            if (newRun)
            {
                _runId = run.RunId; _trailSeen = run.Trail.Count; _rollsSeen = run.Rolls; _battlesSeen = run.BattleCount; _outcomeSeen = run.OutcomeSeq;
                _cam = float.NaN; _camHold = 0; _hudShown = null;
            }

            int moveStart = run.Trail.Count > _trailSeen && _trailSeen > 0 ? run.Trail[Math.Max(0, Math.Min(_trailSeen, run.Trail.Count) - 1)] : run.Position;
            _x0 = WindowStart(run, moveStart);

            // What changed since the last build → dice, token hops, landing, then battle and decision.
            var path = new List<int>();
            if (run.Trail.Count > _trailSeen) path.AddRange(run.Trail.GetRange(Math.Max(0, _trailSeen - 1), run.Trail.Count - Math.Max(0, _trailSeen - 1)));
            bool rolled = run.Rolls > _rollsSeen;
            bool battle = run.BattleCount > _battlesSeen && run.LastBattle != null;
            var landTile = path.Count > 1 ? run.Tiles[path[path.Count - 1]] : null;
            if (landTile != null && landTile.TimesResolved == 0 && landTile.Id != run.BossGateTile) landTile = null; // stopped at a fork
            var newOutcome = run.OutcomeSeq > _outcomeSeen ? run.LastOutcome : null;

            var hudNow = RunPresentation.Hud(Content, run);
            Hud(body, run, level, _hudShown ?? hudNow);
            var dice = Board(body, run, landTile != null ? landTile.Id : -1, out var landLook);
            Controls(run, dice);
            _trailSeen = run.Trail.Count; _rollsSeen = run.Rolls; _battlesSeen = run.BattleCount;

            _anim = UiSequence.On(Root.gameObject);
            _anim.Clear();
            _anim.Speed = 1;
            bool moving = path.Count > 1 && _token != null;
            if (moving) { _camHold = 0; LastPopups.Clear(); } // a new move: the camera follows the worm again
            if (rolled && run.LastDice.Count > 0) RollDice(run.LastDice);
            if (moving)
            {
                float hop = RunFx.HopSeconds(_auto);
                var lift = TokenLift;
                for (int i = 1; i < path.Count; i++)
                {
                    var fromTile = run.Tiles[path[i - 1]];
                    var toTile = run.Tiles[path[i]];
                    var from = DrawPoint(TileCenter(fromTile, _x0), _boardH);
                    var to = DrawPoint(TileCenter(toTile, _x0), _boardH);
                    bool last = i == path.Count - 1;
                    _anim.Then(hop, t =>
                    {
                        // board-game hop: eased step, arc, squash at take-off and landing, stretched in the air
                        _token.anchoredPosition = RunFx.HopPosition(from, to, t) + lift;
                        if (_tokenShadow != null)
                        {
                            _tokenShadow.anchoredPosition = Vector2.Lerp(from, to, RunFx.Ease(t)) + ShadowOffset;
                            float air = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                            _tokenShadow.localScale = Vector3.one * (1 - 0.35f * air);
                        }
                        var sc = RunFx.HopScale(t);
                        if (_tokenBody != null) _tokenBody.localScale = new Vector3(sc.x, sc.y, 1);
                    }, () =>
                    {
                        // a passed pickup (shard, coin cluster) pays as the worm hops over it; the tile keeps its look
                        if (!last) { var pass = RunPresentation.Pass(Content, toTile); if (pass.HasValue) SpawnPopups(new List<LandingPopup> { pass.Value }, to, false); }
                    });
                }
            }
            if (landTile != null)
            {
                var land = landTile;
                var popups = RunPresentation.Landing(Content, run, land, battle, newOutcome);
                _anim.Call(() => Land(land, landLook, popups));
                if (!battle) _anim.Call(() => AnimateHud(hudNow));
                // a moment to read the popups before a fight or a decision covers the board
                bool next = battle || RunSystem.RequiresPlayer(Content, run, false) || newOutcome != null && RunPresentation.IsSheetSource(newOutcome.Source);
                if (next && !RunFx.Skip) _anim.Wait(_auto ? 0.3f : 0.55f);
            }
            else if (!battle) _anim.Call(() => AnimateHud(hudNow));
            if (battle)
            {
                var result = run.LastBattle;
                _anim.Call(() => App.Router.Push(new BattlePlaybackScreen(result, _auto)));
            }
            _anim.Call(() => ShowOutcomeOrDecision(run));
        }

        // ---- motion: dice, landing, popups, HUD -------------------------------------------------

        /// <summary>The worm token stands a little above its tile's centre (the tile label stays readable).</summary>
        private static readonly Vector2 TokenLift = new Vector2(0, 30);
        private static readonly Vector2 ShadowOffset = new Vector2(0, -8);

        /// <summary>Tumbling dice over the board (two when the level rolls two), landing on the rolled values; the footer dice follow.</summary>
        private void RollDice(List<int> values)
        {
            var vals = new List<int>(values);
            float secs = RunFx.DiceSeconds(_auto);
            if (secs <= 0 || _viewport == null) { _footerDice?.Pose(1, vals); return; }
            var stage = Ui.Rect(_viewport, "DiceStage");
            Ui.Stretch(stage);
            var shade = Ui.Panel(stage, new Color(0, 0, 0, 0.25f), "Shade");
            shade.raycastTarget = false;
            Ui.Stretch(shade.rectTransform);
            float dieSize = vals.Count <= 2 ? 150 : Mathf.Max(70, 520f / vals.Count);
            var view = new DiceView(stage, vals.Count, dieSize);
            view.Root.anchorMin = view.Root.anchorMax = new Vector2(0.5f, 0.5f);
            view.Root.sizeDelta = new Vector2(vals.Count * dieSize * 1.35f, dieSize * 1.4f);
            view.Root.anchoredPosition = new Vector2(0, 60);
            var group = stage.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            _anim.Then(secs, t =>
            {
                view.Pose(t, vals);
                _footerDice?.Pose(Mathf.Min(1, t * 1.05f), vals);
            });
            // the dice rest for a beat, then fade while the worm starts hopping
            _anim.Then(_auto ? 0.12f : 0.25f, null, () =>
            {
                if (stage == null) return;
                UiTweens.Play(stage.gameObject, "fade", 0.25f, k => { if (group != null) group.alpha = 1 - k; }, () => { if (stage != null) UnityEngine.Object.Destroy(stage.gameObject); });
            });
        }

        /// <summary>The landing: the tile pops and takes its landed colour with a ring and a glow, then the feedback popups.</summary>
        private void Land(RunTile tile, TileVisual look, List<LandingPopup> popups)
        {
            LastLandedTile = tile.Id;
            var center = DrawPoint(TileCenter(tile, _x0), _boardH);
            if (look.Root != null)
            {
                look.ApplyLanded();
                var rt = look.Root;
                var accent = look.Accent;
                if (!RunFx.Skip)
                {
                    UiTweens.Play(rt.gameObject, "land", 0.45f, k => { if (rt != null) rt.localScale = Vector3.one * RunFx.LandPopScale(k); });
                    // rings and glow go under the token (the worm stays on top of its tile)
                    int under = _tokenShadow != null ? _tokenShadow.GetSiblingIndex() : _board.childCount;
                    RunFx.Ring(_board, center, new Vector2(TileW + 10, TileH + 10), Color.Lerp(accent, Color.white, 0.3f))?.SetSiblingIndex(under);
                    RunFx.Ring(_board, center, new Vector2(TileW - 20, TileH - 20), accent, 0.12f)?.SetSiblingIndex(under);
                    var glow = Ui.RoundPanel(_board, new Color(accent.r, accent.g, accent.b, 0.55f), 26, "LandGlow");
                    glow.raycastTarget = false;
                    PlaceAt(glow.rectTransform, center, new Vector2(TileW + 34, TileH + 34));
                    glow.transform.SetSiblingIndex(rt.GetSiblingIndex());
                    UiTweens.Play(glow.gameObject, "glow", 0.7f, k => { if (glow != null) { var c = glow.color; c.a = 0.55f * (1 - k); glow.color = c; } },
                        () => { if (glow != null) UnityEngine.Object.Destroy(glow.gameObject); });
                }
            }
            SpawnPopups(popups, center, true);
        }

        /// <summary>Floating texts over a tile, plus particles by kind (coins fly to the coin chip, heals rise, hurts flash red).</summary>
        private void SpawnPopups(List<LandingPopup> popups, Vector2 at, bool landing)
        {
            if (_board == null) return;
            for (int i = 0; i < popups.Count; i++)
            {
                var p = popups[i];
                LastPopups.Add(p.Text);
                PopupsSpawned++;
                var pos = at + new Vector2(0, 40 + i * (RunFx.Skip ? 64 : 0));
                RunFx.FloatText(_board, pos, p.Text, p.Color, landing ? (p.Kind == PopupKind.Fight || p.Kind == PopupKind.LevelUp ? 44 : 36) : 28, i * 0.28f, "Popup:" + p.Kind);
                switch (p.Kind)
                {
                    case PopupKind.Coins:
                        RunFx.Burst(_fx, FxPoint(_board, at), _coins != null ? FxPoint(_coins.rectTransform) : (Vector2?)null, Theme.Gold, 7, 22, i * 0.28f);
                        break;
                    case PopupKind.Heal:
                        RunFx.Burst(_board, at, null, Theme.Good, 8, 18, i * 0.28f);
                        break;
                    case PopupKind.Stat:
                    case PopupKind.Xp:
                    case PopupKind.LevelUp:
                    case PopupKind.Shard:
                        RunFx.Burst(_board, at, null, p.Color, 6, 16, i * 0.28f);
                        break;
                    case PopupKind.Loot:
                        RunFx.Burst(_board, at, null, Theme.Gold, 9, 18, i * 0.28f);
                        break;
                    case PopupKind.Hurt:
                        if (_token != null && !RunFx.Skip)
                        {
                            var body = _tokenBody;
                            UiTweens.Play(body.gameObject, "hurt", 0.35f, k => { if (body != null) body.localRotation = Quaternion.Euler(0, 0, 14 * Mathf.Sin(k * Mathf.PI * 5) * (1 - k)); });
                        }
                        break;
                }
            }
        }

        /// <summary>A board point in the screen's effects layer (top-left anchored coordinates).</summary>
        private Vector2 FxPoint(RectTransform space, Vector2 anchored)
        {
            if (_fx == null || space == null) return anchored;
            // anchored (top-left of space) → world → fx local → fx top-left anchored
            var local = new Vector3(space.rect.xMin + anchored.x, space.rect.yMax + anchored.y, 0);
            var world = space.TransformPoint(local);
            var inFx = (Vector2)_fx.InverseTransformPoint(world);
            return inFx - new Vector2(_fx.rect.xMin, _fx.rect.yMax);
        }

        private Vector2 FxPoint(RectTransform target)
        {
            if (_fx == null || target == null) return Vector2.zero;
            var world = target.TransformPoint(target.rect.center);
            var inFx = (Vector2)_fx.InverseTransformPoint(world);
            return inFx - new Vector2(_fx.rect.xMin, _fx.rect.yMax);
        }

        /// <summary>HUD numbers move to <paramref name="to"/>: bars tween, changed stat chips pop (instant outside play mode).</summary>
        private void AnimateHud(RunHudStats to)
        {
            var from = _hudShown ?? to;
            _hudShown = to;
            if (_hudRoot == null) return;
            bool atkUp = Math.Abs(to.Atk - from.Atk) >= 0.5, defUp = Math.Abs(to.Def - from.Def) >= 0.5;
            UiTweens.Play(_hudRoot.gameObject, "hud", RunFx.Skip ? 0 : 0.6f, k =>
            {
                float e = RunFx.Ease(k);
                ShowHud(Lerp(from, to, e), to.Level > from.Level ? (k < 0.5f ? from.Level : to.Level) : to.Level,
                    XpFraction(from, to, k));
                float pop = 1 + 0.25f * Mathf.Sin(Mathf.Clamp01(k * 1.6f) * Mathf.PI);
                if (atkUp && _atkText != null) _atkText.transform.parent.localScale = Vector3.one * pop;
                if (defUp && _defText != null) _defText.transform.parent.localScale = Vector3.one * pop;
            });
        }

        private static RunHudStats Lerp(RunHudStats a, RunHudStats b, float k) => new RunHudStats
        {
            Hp = a.Hp + (b.Hp - a.Hp) * k, MaxHp = a.MaxHp + (b.MaxHp - a.MaxHp) * k,
            Atk = a.Atk + (b.Atk - a.Atk) * k, Def = a.Def + (b.Def - a.Def) * k,
            Level = b.Level, Xp = b.Xp, XpNeed = b.XpNeed,
        };

        /// <summary>XP bar fill while it animates: a level-up fills the bar first, then restarts from empty.</summary>
        private static float XpFraction(RunHudStats from, RunHudStats to, float k)
        {
            float a = from.XpNeed > 0 ? (float)from.Xp / from.XpNeed : 0, b = to.XpNeed > 0 ? (float)to.Xp / to.XpNeed : 0;
            if (to.Level > from.Level) return k < 0.5f ? Mathf.Lerp(a, 1, k * 2) : Mathf.Lerp(0, b, (k - 0.5f) * 2);
            return Mathf.Lerp(a, b, RunFx.Ease(k));
        }

        private void ShowHud(RunHudStats s, int level, float xpFrac)
        {
            double hpFrac = s.MaxHp > 0 ? s.Hp / s.MaxHp : 1;
            var hpColor = hpFrac > 0.5 ? Theme.Good : hpFrac > 0.25 ? Theme.Warn : Theme.Bad;
            if (_hpBar != null)
            {
                _hpBar.Set((float)hpFrac, Fmt.Num(Math.Round(s.Hp)) + " / " + Fmt.Num(Math.Round(s.MaxHp)));
                _hpBar.Fill.GetComponent<Image>().color = hpColor;
            }
            if (_atkText != null) _atkText.text = "ATK " + Fmt.Num(Math.Round(s.Atk));
            if (_defText != null) _defText.text = "DEF " + Fmt.Num(Math.Round(s.Def));
            if (_lvText != null) _lvText.text = "Lv " + level;
            if (_xpBar != null) _xpBar.Set(xpFrac, "XP " + s.Xp + " / " + s.XpNeed);
        }

        /// <summary>The HUD as shown now (tests: HP / ATK / DEF / level / XP texts).</summary>
        public string HudText => string.Join(" | ", new[] { _hpBar?.Label?.text, _atkText?.text, _defText?.text, _lvText?.text, _xpBar?.Label?.text });

        // ---- follow camera ------------------------------------------------------------------------

        /// <summary>Height of one snake band (3 lanes + the gap).</summary>
        public const float BandH = 3 * LaneH + BandGap;

        /// <summary>Camera position (board rise at the viewport centre; tests).</summary>
        public float CameraY => _cam;

        /// <summary>Absolute rise of a point drawn at <paramref name="drawY"/> on the current board (independent of the window).</summary>
        private float AbsRise(float drawY) => drawY + (_x0 / Columns) * BandH + _boardH;

        /// <summary>Where the camera wants to be: the token, a little ahead (the path climbs), never below the path's start.</summary>
        private float CameraTarget()
        {
            if (_token == null) return float.IsNaN(_cam) ? _viewH / 2 : _cam;
            return Mathf.Max(_viewH / 2 - 40, AbsRise(_token.anchoredPosition.y) + _viewH * 0.12f);
        }

        private void ApplyCamera()
        {
            if (_board == null || float.IsNaN(_cam)) return;
            float camDraw = _cam - (_x0 / Columns) * BandH - _boardH;
            _board.anchoredPosition = new Vector2(0, -_viewH / 2 - camDraw);
        }

        /// <summary>One camera frame: eases towards the worm (never snaps), unless the player is looking around.</summary>
        public void StepCamera(float dt)
        {
            if (_board == null) return;
            if (_camHold > 0) { _camHold -= dt; ApplyCamera(); return; }
            _cam = RunFx.CameraStep(_cam, CameraTarget(), dt);
            ApplyCamera();
        }

        private void DragBoard(float dy)
        {
            if (float.IsNaN(_cam)) return;
            _cam -= dy;
            _camHold = 3f;
            ApplyCamera();
        }

        /// <summary>Hud: HP bar, ATK, DEF (run modifications applied), run level + XP bar, roll clock, skills, boss banner.</summary>
        private void Hud(RectTransform body, ActiveRun run, LevelDef level, RunHudStats shown)
        {
            var hud = Ui.Column(body, 10, 0, null, "Hud");
            _hudRoot = hud;
            var top = Ui.Row(hud, HudStatsH, 12, null, 0, "HudStats");
            var hpLabel = Ui.Display(top, "HP", 30, Theme.Text, TextAnchor.MiddleCenter);
            Ui.Size(hpLabel, 56, -1, 0);
            _hpBar = Ui.Bar(top, 1, Theme.Good, "", 40);
            _hpBar.Fill.parent.name = "HpBar";
            _hpText = _hpBar.Label;
            _atkText = StatChip(top, "AtkChip", Theme.Hex(0xFF8A6B));
            _defText = StatChip(top, "DefChip", Theme.Hex(0x7FC8FF));

            var mid = Ui.Row(hud, HudLevelH, 10, null, 0, "HudLevel");
            var lv = Ui.Chip(mid, "Lv 1", Theme.Tint(Theme.Premium, 0.35f), Theme.Premium, 26, 48, Theme.Premium, "RunLevel");
            _lvText = lv.GetComponentInChildren<Text>();
            _xpBar = Ui.Bar(mid, 0, Theme.Purple, "", 34);
            _xpBar.Fill.parent.name = "XpBar";
            Ui.Chip(mid, RollText(Content, run), Theme.Tint(Theme.Bad, 0.25f), Theme.Hex(0xFFD2C2), 24, 48, null, "RollCounter");
            Ui.Chip(mid, "Skills " + RunSystem.BuildSize(run), Theme.Tint(Theme.Premium, 0.25f), Theme.Hex(0xD6C2FF), 22, 48, null, "SkillCount");
            Ui.Button(mid, "Auto-pick: " + (Farming ? "On" : "Off"), ToggleFarming, Farming ? Theme.Premium : Theme.Panel, 210, 48, true, 22).name = "AutoPickToggle";

            // the boss waiting at the top of the board, with its awakening
            var boss = run.BossId != null && Content.RunBosses.TryGetValue(run.BossId, out var bd) ? bd : null;
            var banner = Ui.Row(hud, HudBannerH, 12, Theme.Hex(0x3A1414), 18, "BossBanner");
            UiShapes.Outline(banner, Theme.Bad, 26, 5);
            banner.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            var bossName = Ui.Display(banner, "BOSS · " + (boss?.Name ?? "Boss"), 28, Theme.Hex(0xFF9FA2), TextAnchor.MiddleLeft);
            bossName.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Size(bossName, -1, -1, 0);
            int left = Math.Max(0, RunSystem.BossRollEstimate(Content, run) - run.Rolls);
            string clock = run.Awakening.ToString("0") + "% awake · " + (left <= 0 ? "comes now" : left == 1 ? "1 roll left" : left + " rolls left");
            Ui.Bar(banner, (float)(run.Awakening / 100.0), Theme.Bad, clock, 30);
            if (run.Danger > 0) Ui.Chip(banner, "Danger " + run.Danger, Theme.Tint(Theme.Bad, 0.35f), Theme.Hex(0xFFB3B5), 20, 40);
            if (run.Shards > 0) Ui.Chip(banner, "Shards " + run.Shards, Theme.Tint(Theme.Premium, 0.3f), Theme.Premium, 20, 40);

            ShowHud(shown, shown.Level, shown.XpNeed > 0 ? (float)shown.Xp / shown.XpNeed : 0);
        }

        private static Text StatChip(RectTransform row, string name, Color color)
        {
            var chip = Ui.Chip(row, "-", Theme.Tint(color, 0.3f), Color.Lerp(color, Color.white, 0.4f), 26, HudStatsH - 6, color, name);
            Ui.Size(chip, 150, HudStatsH - 6, 0);
            return chip.GetComponentInChildren<Text>();
        }

        public const float HudStatsH = 58, HudLevelH = 50, HudBannerH = 66;

        /// <summary>The colours of one board tile (unlanded and landed look), applied at the landing.</summary>
        private struct TileVisual
        {
            public RectTransform Root;
            public Image Fill, Border;
            public Text Label;
            public string LandedLabel;
            public Color Accent;
            public Color LandedFill, LandedBorder, LandedText;
            public GameObject Mark;

            public void ApplyLanded()
            {
                if (Fill != null) Fill.color = LandedFill;
                if (Border != null) Border.color = LandedBorder;
                if (Label != null) { Label.color = LandedText; if (LandedLabel != null) { Label.text = LandedLabel; Label.font = Ui.FontFor(LandedLabel); } }
                if (Mark != null) Mark.SetActive(true);
            }
        }

        /// <summary>Fill / border / text of a tile: unwalked and passed tiles look the same; landed tiles are filled with their colour.</summary>
        private static void TileColors(TileLook look, bool landed, out Color fill, out Color border, out Color text)
        {
            if (look.Gate) { fill = Theme.Hex(0x3A1414); border = Theme.Bad; text = Theme.Hex(0xFF9FA2); return; }
            if (look.Empty)
            {
                fill = landed ? Theme.Hex(0x3A2A2A) : Theme.PanelAlt;
                border = landed ? Theme.TextFaint : Theme.Border;
                text = Theme.TextFaint;
                return;
            }
            if (landed)
            {
                fill = Color.Lerp(Theme.Panel, look.Accent, 0.62f);
                border = Color.Lerp(look.Accent, Color.white, 0.35f);
                text = Theme.TextOn(fill) == Theme.OnAccent ? Theme.OnAccent : Theme.Text;
                return;
            }
            fill = Theme.Panel; border = look.Accent; text = Color.Lerp(look.Accent, Color.white, 0.55f);
        }

        private DiceView Board(RectTransform body, ActiveRun run, int landingId, out TileVisual landing)
        {
            landing = default;
            int x0 = _x0;
            float h = BoardHeight(run, x0);
            _boardH = h;
            float boardH = h;
            // the board lives in its own viewport (the HUD above stays put); the follow camera moves the board inside it
            float frame = BodyFrame != null ? BodyFrame.rect.height : 0;
            _viewH = Mathf.Max(560, frame - 2 * 24 - (HudStatsH + HudLevelH + HudBannerH + 2 * 10) - 18 - 2);
            var holder = Ui.Rect(body, "BoardHolder");
            Ui.Size(holder, -1, _viewH);
            holder.gameObject.AddComponent<RectMask2D>();
            holder.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0); // raycast target for drags
            holder.gameObject.AddComponent<BoardDrag>().OnDragY = DragBoard;
            _viewport = holder;
            var board = Ui.Rect(holder, "Board");
            board.anchorMin = board.anchorMax = new Vector2(0.5f, 1);
            board.pivot = new Vector2(0.5f, 1);
            board.sizeDelta = new Vector2(BoardWidth, h);
            board.anchoredPosition = Vector2.zero;
            _board = board;
            Vector2 P(RunTile t) => DrawPoint(TileCenter(t, x0), boardH);
            var visible = run.Tiles.FindAll(t => InWindow(run, t, x0));

            // edges first (behind tiles): walked edges lit, the rest dark (the path only goes forward)
            var walked = new HashSet<long>();
            for (int i = 1; i < run.Trail.Count; i++) walked.Add(((long)run.Trail[i - 1] << 32) | (uint)run.Trail[i]);
            foreach (var t in visible)
                foreach (var n in t.Next)
                    if (n >= 0 && n < run.Tiles.Count && InWindow(run, run.Tiles[n], x0))
                    {
                        bool lit = walked.Contains(((long)t.Id << 32) | (uint)n);
                        Edge(board, P(t), P(run.Tiles[n]), lit ? Theme.Shade(Theme.AccentLight, 0.8f) : Theme.Border, lit ? 12 : 10);
                    }

            // At a fork: the path each option walks with the steps left and the tile it lands on (A / B on the board).
            var onPath = new Dictionary<int, int>();
            if (run.Phase == RunPhase.ChooseBranch)
                for (int i = 0; i < run.BranchOptions.Count; i++)
                    foreach (var id in BranchPath(run, i, Content)) if (!onPath.ContainsKey(id)) onPath[id] = i;
            foreach (var t in visible)
            {
                var look = LookOf(run, t, Content);
                var center = P(t);
                if (onPath.TryGetValue(t.Id, out int pathOpt) && !look.Option)
                {
                    var pc = BranchColor(pathOpt);
                    var trail = Ui.RoundPanel(board, new Color(pc.r, pc.g, pc.b, 0.28f), 22, "BranchPath");
                    trail.raycastTarget = false;
                    PlaceAt(trail.rectTransform, center, new Vector2(TileW + 16, TileH + 16));
                }
                if (look.Option)
                {
                    var glow = Ui.RoundPanel(board, new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0.45f), 26, "Option");
                    glow.raycastTarget = false;
                    PlaceAt(glow.rectTransform, center, new Vector2(TileW + 26, TileH + 26));
                    UiPulse.On(glow, 0.05f, 1.1f, 0.5f);
                }
                var size = look.Gate ? new Vector2(TileW + GateGrow, TileH + GateGrow) : new Vector2(TileW, TileH);
                // only the tiles the worm LANDED on are coloured; passed tiles keep their look. The tile landed on in this
                // move is drawn as before and takes its colour when the token lands (Land).
                bool landsNow = t.Id == landingId;
                TileColors(look, look.Landed && !landsNow, out var fill, out var border, out var text);
                if (look.Fork && !look.Landed) fill = Theme.Hex(0x2B2414);
                if (t.BonusPct > 0 && !look.Landed) fill = Color.Lerp(fill, Theme.Bad, 0.25f);
                var img = Ui.RoundPanel(board, fill, 16, "Tile:" + t.Id);
                img.raycastTarget = false;
                PlaceAt(img.rectTransform, center, size);
                var outline = UiShapes.Outline(img.rectTransform, border, 16, look.Gate ? 5 : look.Landed ? 5 : 4);
                string label = look.Label;
                if (look.Fork && !look.Visited && !look.Landed) label = string.IsNullOrEmpty(label) ? "FORK" : label + "\n<size=20>fork</size>";
                if (look.Option) label = (run.BranchOptions.IndexOf(t.Id) == 0 ? "A · " : "B · ") + label;
                if (look.Gate) label = "BOSS\n<size=18>arena</size>";
                bool check = look.Cleared && look.Landed && !string.IsNullOrEmpty(label) && !look.Gate;
                string landedLabel = check ? label + " ✓" : label;
                if (check && !landsNow) label = landedLabel; // the tile landed on now gets its check mark at the landing
                Text lab = null;
                if (!string.IsNullOrEmpty(label))
                {
                    lab = Ui.Display(img.transform, label, look.Gate ? 32 : label.Length > 6 ? 24 : 30, text, TextAnchor.MiddleCenter);
                    lab.lineSpacing = 0.8f;
                    Ui.Stretch(lab.rectTransform, 2, 2, 2, 2);
                }
                // landed marker: a footprint dot in the tile's corner
                GameObject mark = null;
                if (look.Landed && !look.Gate)
                {
                    var dot = Ui.Dot(img.transform, Theme.Cream, 18, "LandMark");
                    dot.GetComponent<LayoutElement>().ignoreLayout = true;
                    var drt = dot.rectTransform;
                    drt.anchorMin = drt.anchorMax = new Vector2(0, 1);
                    drt.sizeDelta = new Vector2(18, 18);
                    drt.anchoredPosition = new Vector2(16, -16);
                    mark = dot.gameObject;
                    if (landsNow) mark.SetActive(false);
                }
                if (landsNow)
                {
                    TileColors(look, true, out var lf, out var lb, out var lt);
                    landing = new TileVisual { Root = img.rectTransform, Fill = img, Border = outline, Label = lab, LandedLabel = landedLabel, Accent = look.Gate ? Theme.Bad : look.Empty ? Theme.TextFaint : look.Accent, LandedFill = lf, LandedBorder = lb, LandedText = lt, Mark = mark };
                }
                if (t.BonusPct > 0 && !look.Landed)
                {
                    var pip = Ui.Chip(img.transform, "+" + t.BonusPct.ToString("0") + "%", Theme.Bad, Theme.Text, 20, 32, null, "Bonus");
                    pip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    pip.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                    pip.anchorMin = pip.anchorMax = new Vector2(1, 1); pip.pivot = new Vector2(0.7f, 0.6f);
                    pip.sizeDelta = new Vector2(0, 32); pip.anchoredPosition = Vector2.zero;
                }
            }
            // fork: tap targets on the board — the tile the worm would land on (marked A / B) and the option tile itself
            if (run.Phase == RunPhase.ChooseBranch)
                for (int i = 0; i < run.BranchOptions.Count; i++)
                {
                    int index = i;
                    string letter = i == 0 ? "A" : "B";
                    var col = BranchColor(i);
                    int landId = BranchLanding(run, i, Content);
                    if (landId < 0) continue;
                    if (!InWindow(run, run.Tiles[landId], x0) || !InWindow(run, run.Tiles[run.BranchOptions[i]], x0)) continue;
                    var pick = Ui.Button(board, "", () => PickBranch(index), new Color(col.r, col.g, col.b, 0.18f), TileW + 30, TileH + 30, true, 20);
                    pick.name = "BranchPick:" + letter;
                    var prt = (RectTransform)pick.transform;
                    PlaceAt(prt, P(run.Tiles[landId]), new Vector2(TileW + 30, TileH + 30));
                    UiShapes.Outline(prt, col, 24, 6);
                    var badge = Ui.RoundPanel(prt, col, 30, "LandingBadge");
                    badge.raycastTarget = false;
                    var brt = badge.rectTransform;
                    brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1); brt.pivot = new Vector2(0.5f, 0.5f);
                    brt.sizeDelta = new Vector2(150, 56); brt.anchoredPosition = new Vector2(0, 8);
                    var bl = Ui.Display(badge.transform, letter + " · LAND", 26, Theme.TextOn(col), TextAnchor.MiddleCenter);
                    Ui.Stretch(bl.rectTransform);
                    UiPulse.On(badge, 0.06f, 1.2f, 0.6f);
                    var opt = Ui.Button(board, "", () => PickBranch(index), new Color(0, 0, 0, 0.01f), TileW, TileH, true, 20);
                    opt.name = "BranchOption:" + letter;
                    PlaceAt((RectTransform)opt.transform, P(run.Tiles[run.BranchOptions[i]]), new Vector2(TileW, TileH));
                }
            // boss on the board, next to its gate
            var gateTile = run.BossGateTile >= 0 && run.BossGateTile < run.Tiles.Count ? run.Tiles[run.BossGateTile] : null;
            if (gateTile != null)
            {
                var p = DrawPoint(BossMarkerCenter(visible, run.BossGateTile, new Vector2(BoardWidth, h), x0), h);
                var bossImg = Ui.RoundPanel(board, Theme.Shade(Theme.ValenceColor(Valence.Boss), 0.55f), 25, "Boss");
                bossImg.raycastTarget = false;
                PlaceAt(bossImg.rectTransform, p, BossMarkerSize);
                var bl = Ui.Display(bossImg.transform, "BOSS " + run.Awakening.ToString("0") + "%", 20, Theme.Text, TextAnchor.MiddleCenter);
                Ui.Stretch(bl.rectTransform);
            }

            // token (worm): a board-game piece — a glowing round base with the worm bobbing on it and a shadow on the
            // ground; it hops tile by tile along the path (Build) and is smaller than a tile, so the landed tile shows.
            var cur = run.Position >= 0 && run.Position < run.Tiles.Count ? run.Tiles[run.Position] : null;
            var start = cur;
            int prevIndex = Math.Max(0, Math.Min(_trailSeen, run.Trail.Count) - 1);
            if (run.Trail.Count > _trailSeen && _trailSeen > 0) start = run.Tiles[run.Trail[prevIndex]];
            _token = _tokenBody = _tokenShadow = null;
            if (start != null)
            {
                var shadow = Ui.Panel(board, new Color(0, 0, 0, 0.45f), "TokenShadow");
                shadow.sprite = UiShapes.Circle;
                shadow.raycastTarget = false;
                _tokenShadow = shadow.rectTransform;
                PlaceAt(_tokenShadow, P(start) + ShadowOffset, new Vector2(96, 34));

                var tokenRt = Ui.Rect(board, "Token");
                PlaceAt(tokenRt, P(start) + TokenLift, new Vector2(92, 92));
                _token = tokenRt;
                var glow = Ui.Panel(tokenRt, new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f), "Glow");
                glow.sprite = UiShapes.Circle;
                glow.raycastTarget = false;
                Ui.Stretch(glow.rectTransform, -18, -18, -18, -18);
                UiPulse.On(glow, 0.06f, 1.6f, 0.5f);
                var bodyRt = Ui.Rect(tokenRt, "Body");
                Ui.Stretch(bodyRt);
                bodyRt.pivot = new Vector2(0.5f, 0.1f); // squash from the ground
                _tokenBody = bodyRt;
                var plate = Ui.Panel(bodyRt, Theme.Accent, "Plate");
                plate.sprite = UiShapes.Circle;
                plate.raycastTarget = false;
                Ui.Stretch(plate.rectTransform, 6, 6, 6, 6);
                var ring = Ui.Panel(bodyRt, Theme.Cream, "Rim");
                ring.sprite = UiShapes.Circle;
                ring.raycastTarget = false;
                Ui.Stretch(ring.rectTransform, 0, 0, 0, 0);
                ring.transform.SetAsFirstSibling();
                var worm = Ui.RoundPanel(bodyRt, Theme.Worm, 20, "Worm");
                worm.raycastTarget = false;
                var wrt = worm.rectTransform;
                wrt.anchorMin = wrt.anchorMax = new Vector2(0.5f, 0.5f);
                wrt.sizeDelta = new Vector2(62, 32);
                wrt.anchoredPosition = new Vector2(0, 4);
                UiShapes.Outline(wrt, Theme.WormLight, 18, 4);
                var eye = Ui.Dot(wrt, Theme.OnAccent, 9, "Eye");
                eye.rectTransform.anchorMin = eye.rectTransform.anchorMax = new Vector2(0.8f, 0.62f);
                eye.rectTransform.sizeDelta = new Vector2(9, 9);
                UiBob.On(wrt, 5, 1.6f);
            }

            // effects layer over the whole screen (coins flying to the coin chip)
            if (_fx == null || _fx.parent != Root)
            {
                _fx = Ui.Rect(Root, "Fx");
                Ui.Stretch(_fx);
                _fx.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            }
            _fx.SetAsLastSibling();

            // place the camera: first build centres on the worm; later builds keep the camera where it was (it then eases)
            if (float.IsNaN(_cam)) _cam = CameraTarget();
            ApplyCamera();

            // dice + last effect text: first row of the fixed bottom bar
            var info = Ui.Row(Footer ?? body, InfoRowH, 12, null, 0, "Info");
            var diceChip = Ui.RoundPanel(info, Theme.PanelAlt, 16, "Dice");
            Ui.Size(diceChip, 150, InfoRowH, 0);
            int diceN = Math.Max(1, run.LastDice.Count);
            var dice = new DiceView(diceChip.rectTransform, diceN, Mathf.Min(InfoRowH - 10, 128f / diceN - 6), "FooterDice");
            Ui.Stretch(dice.Root);
            if (run.LastDice.Count > 0) dice.Pose(1, run.LastDice);
            else dice.Root.gameObject.SetActive(run.Rolls > 0);
            _footerDice = dice;
            var land = cur != null && Content.BoardTiles.TryGetValue(cur.TileId ?? "", out var ld) ? ld : null;
            var what = Ui.Label(info, run.LastEventText ?? (land != null ? land.Name + (string.IsNullOrEmpty(land.Text) ? "" : ": " + land.Text) : ""), 24, Theme.TextDim);
            what.verticalOverflow = VerticalWrapMode.Truncate;
            return dice;
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

        private void Controls(ActiveRun run, DiceView dice)
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
                    var col = RunPresentation.SkillColor(s); // rarity colour (family = the small tag)
                    var b = Ui.Button(slots, s?.Name ?? skills[i], () => SkillsModal(run), Theme.Tint(col, 0.32f), -1, SlotRowH, true, 22);
                    b.name = "Slot:" + skills[i];
                    b.GetComponentInChildren<Text>().color = Color.Lerp(col, Color.white, 0.5f);
                    UiShapes.Outline((RectTransform)b.transform, col, Theme.RadiusButton, 3, "RarityBorder");
                    FamilyTag((RectTransform)b.transform, s?.Family ?? SkillFamily.None);
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
            bool encounter = run.Phase == RunPhase.Encounter;
            bool canRoll = run.Phase == RunPhase.AwaitRoll || encounter;
            bool deciding = !canRoll && run.Phase != RunPhase.Completed && run.Phase != RunPhase.Failed;
            bool fork = run.Phase == RunPhase.ChooseBranch;
            string rollText = encounter && !_auto ? (run.EncounterKind == "boss" ? "ENTER ARENA\n<size=24>fight the boss</size>" : "FIGHT!\n<size=24>a miniboss attacks</size>")
                : fork ? "PICK A PATH\n<size=24>tap A or B on the board</size>"
                : !_auto
                ? canRoll ? "ROLL\n<size=24>hold for auto</size>" : "CHOOSE\n<size=24>your decision</size>"
                : AutoPaused ? "AUTO PAUSED\n<size=24>your choice</size>" : "AUTO…";
            var roll = Ui.Button(row, rollText, null, canRoll || _auto && !AutoPaused ? Theme.Accent : deciding ? Theme.Gold : Theme.Locked, -1, RollRowH, canRoll || _auto || deciding, 56);
            roll.name = "RollButton";
            var hold = roll.gameObject.AddComponent<HoldButton>();
            hold.OnTap = () =>
            {
                if (_auto) StopAuto();
                else if (Animating) _anim.Speed = 4; // tap while the dice roll / the worm hops: fast-forward
                else
                {
                    var r = State.Run.Active;
                    if (r != null && r.Phase == RunPhase.AwaitRoll) Run(new RunRoll());
                    else if (r != null && r.Phase == RunPhase.Encounter) Run(new RunEncounter());
                    else if (r != null && r.Phase == RunPhase.ChooseBranch) { _camHold = 0; Rebuild(); } // the camera eases back to the fork
                    else if (r != null) { _decisionKey = null; ShowOutcomeOrDecision(r); }
                }
            };
            hold.OnHold = StartAuto;
            var auto = Ui.Button(row, "AUTO\n<size=24>" + (_auto ? "on" : "off") + "</size>", () => { if (_auto) StopAuto(); else StartAuto(); },
                _auto ? Theme.Tint(Theme.Premium, 0.35f) : Theme.Panel, 150, RollRowH, true, 30);
            auto.name = "AutoButton";
            auto.GetComponentInChildren<Text>().color = _auto ? Theme.Premium : Theme.TextDim;
            if (_auto) UiShapes.Outline((RectTransform)auto.transform, Theme.Premium, Theme.RadiusButton, 4);

            var hint = Ui.Label(bar, fork ? "Fork! A and B mark where each path lands you with the steps you have left."
                    : _auto && AutoPaused ? "AUTO PAUSED — your choice. Auto continues after you decide."
                    : "Auto rolls and fights for you, and stops at shops, events, chests, forks and drafts.",
                22, fork || _auto && AutoPaused ? Theme.Gold : Theme.TextDim, TextAnchor.MiddleCenter, "Hint");
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
            StepCamera(dt);
            if (_toResult && !Animating && App.Router.Current == this)
            {
                _toResult = false;
                App.Router.Replace(new RunResultScreen());
                return;
            }
            if (_outcome != null && _outcome.IsOpen)
            {
                // the result of an event / chest / campfire / shop: auto passes it after a short look
                if (!_auto || _outcomeRevealing) { _outcomeTimer = 0; RemoveAutoOverlay(); return; }
                _outcomeTimer += dt;
                if (_outcomeTimer >= AutoOutcomeSeconds) CloseOutcome();
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
            if (_outcome != null && _outcome.IsOpen) { _outcome.Close(); _outcomeSeen = Math.Max(0, _outcomeSeen - 1); } // shown again on return
            if (_autoOverlay != null) Ui.DestroyGo(_autoOverlay);
            _autoOverlay = null;
        }

        /// <summary>Back on screen: reopen a decision that OnHide closed (an open one stays as it is, no close / reopen flicker).</summary>
        public override void OnShow() { if (_decision == null || !_decision.IsOpen) _decisionKey = null; }

        // ---- decisions ---------------------------------------------------------------------------

        private void CloseDecision()
        {
            if (_decision != null && _decision.IsOpen) _decision.Close();
            _decision = null;
        }

        /// <summary>
        /// What makes an open decision UI stale. A purchase does not: the shop sheet stays open and updates in place
        /// (<see cref="RefreshShop"/>), so buying 2–4 perks per visit does not close and reopen it.
        /// </summary>
        private static string DecisionKey(ActiveRun r) =>
            r.RunId + "|" + r.Phase + "|" + r.Rolls + "|" + r.EventId + "|" + string.Join(",", r.DraftOffer) + "|" + r.DraftRerolls + "|" + r.ShopRerolls + "|" + r.MythicDraft
            + (r.Phase == RunPhase.Shop ? "" : "|" + r.Coins);

        /// <summary>The skill level-up rule (exact numbers from RunTuning; a level-up improves the skill's own numbers).</summary>
        public static string LevelUpText(ContentDb c) => RogueStars.Rules.Core.SkillText.LevelUpRule(c);

        private readonly List<KeyValuePair<Button, int>> _shopButtons = new List<KeyValuePair<Button, int>>();
        private Text _shopCoins;

        /// <summary>Updates the open shop after a purchase: sold offers, affordability, coins (no close / reopen).</summary>
        private void RefreshShop()
        {
            var run = State.Run.Active;
            if (run == null || run.Phase != RunPhase.Shop) return;
            if (_shopCoins != null) _shopCoins.text = run.Coins + " coins · " + run.ShopBuys + " bought this run";
            foreach (var kv in _shopButtons)
            {
                if (kv.Key == null || kv.Value >= run.Shop.Count) continue;
                var o = run.Shop[kv.Value];
                kv.Key.interactable = !o.Sold && run.Coins >= o.Price;
                var label = kv.Key.GetComponentInChildren<Text>();
                if (label != null) label.text = o.Sold ? "Sold" : o.Price + " coins";
            }
        }

        private void ShowDecision(ActiveRun stale)
        {
            if (Root == null || App.Router.Current != this) return;
            if (_outcome != null && _outcome.IsOpen) return; // the outcome result first; its Continue opens the decision
            var run = State.Run.Active;
            if (run == null || run.RunId != stale.RunId) return;
            string key = DecisionKey(run);
            if (key == _decisionKey && _decision != null && _decision.IsOpen) return;
            CloseDecision();
            _decisionKey = key;
            switch (run.Phase)
            {
                case RunPhase.ChooseBranch: _decision = null; break; // picked on the board (A / B landing tiles)
                case RunPhase.Event: _decision = EventModal(run); break;
                case RunPhase.Draft: _decision = DraftModal(run); break;
                case RunPhase.Shop: _decision = ShopModal(run); break;
                case RunPhase.Defeated: _decision = ReviveModal(run); break;
                case RunPhase.BurrowChoice: _decision = BurrowModal(run); break;
                case RunPhase.Minigame: _decision = Minigames.MinigameHost.Open(App.ModalLayer, Session); break;
            }
        }

        /// <summary>
        /// The skill's family as a small tag in the corner of a rarity-coloured slot / card / icon (initial in the family
        /// colour; skills are coloured by rarity everywhere in the run).
        /// </summary>
        public static void FamilyTag(RectTransform target, SkillFamily family)
        {
            if (target == null || family == SkillFamily.None) return;
            var col = Theme.FamilyColor(family);
            var tag = Ui.Dot(target, Theme.Shade(col, 0.55f), 30, "FamilyTag");
            tag.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = tag.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.75f, 0.75f);
            rt.sizeDelta = new Vector2(30, 30);
            rt.anchoredPosition = Vector2.zero;
            UiShapes.Outline(rt, col, 15, 3);
            var l = Ui.Display(rt, family.ToString().Substring(0, 1), 18, Color.Lerp(col, Color.white, 0.4f), TextAnchor.MiddleCenter);
            Ui.Stretch(l.rectTransform);
        }

        /// <summary>Branch colours: A orange, B gold.</summary>
        public static Color BranchColor(int option) => option == 0 ? Theme.Accent : Theme.Gold;

        private void PickBranch(int index)
        {
            var r = State.Run.Active;
            if (r == null || r.Phase != RunPhase.ChooseBranch || Animating) return;
            Run(new RunChooseBranch { Index = index });
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
                    Ui.Button(box, "Continue", () => { modal.Close(); Run(new RunChooseEventOption { Index = 0 }); }, Theme.Accent);
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

        // ---- outcome results (event / chest / campfire / shop) ----------------------------------

        /// <summary>Show the newest outcome (ActiveRun.LastOutcome) once, else the pending decision.</summary>
        private void ShowOutcomeOrDecision(ActiveRun stale)
        {
            if (Root == null || App.Router.Current != this) return;
            var run = State.Run.Active;
            if (run == null || run.RunId != stale.RunId) return;
            if (_outcome != null && _outcome.IsOpen) return;
            if (run.LastOutcome != null && run.OutcomeSeq > _outcomeSeen && !RunPresentation.IsSheetSource(run.LastOutcome.Source))
                _outcomeSeen = run.OutcomeSeq; // a shop visit (bought items were seen in the shop) or a tile's outcome (shown as landing popups): no result sheet
            if (run.LastOutcome != null && run.OutcomeSeq > _outcomeSeen)
            {
                _outcomeSeen = run.OutcomeSeq;
                CloseDecision();
                _outcome = OutcomeSheet(run.LastOutcome);
                _outcomeTimer = 0;
                return;
            }
            ShowDecision(run);
        }

        private void CloseOutcome()
        {
            if (_outcome != null && _outcome.IsOpen) _outcome.Close();
            _outcome = null;
            _outcomeRevealing = false;
            var run = State.Run.Active;
            if (run != null) { _decisionKey = null; ShowDecision(run); }
        }

        /// <summary>The result screen is open (tests).</summary>
        public bool OutcomeOpen => _outcome != null && _outcome.IsOpen;

        private static string SourceCaption(string source) =>
            source == "treasure" ? "CHEST" : source == "campfire" ? "CAMPFIRE" : source == "shop" ? "SHOP" : source == "minigame" ? "MINI-GAME" : "EVENT";

        /// <summary>One result line as text, e.g. "+30 coins", "−5% max HP", "New skill: Ember Kindle" (pure; tested).</summary>
        public static string OutcomeLineText(OutcomeLine l, ContentDb content)
        {
            string sign = l.Amount >= 0 ? "+" : "−";
            double a = Math.Abs(l.Amount);
            switch (l.Kind)
            {
                case OutcomeKind.Coins: return sign + a.ToString("0") + " coins";
                case OutcomeKind.Hp: return sign + Fmt.Num(a) + " HP";
                case OutcomeKind.Stat:
                    return sign + Fmt.Pct(a) + " " + (l.Id == "Hp" ? "max HP" : l.Id == "Atk" ? "ATK" : l.Id == "Def" ? "DEF" : l.Id) + " for the run";
                case OutcomeKind.SkillGained: return "New skill: " + (content.Skill(l.Id)?.Name ?? l.Id);
                case OutcomeKind.SkillLost: return "Lost skill: " + (content.Skill(l.Id)?.Name ?? l.Id);
                case OutcomeKind.SkillLevel: return (content.Skill(l.Id)?.Name ?? l.Id) + " → level " + a.ToString("0");
                case OutcomeKind.Xp: return "+" + a.ToString("0") + " XP";
                case OutcomeKind.LevelUp: return "Level up! +" + a.ToString("0") + " skill draft" + (a > 1 ? "s" : "");
                case OutcomeKind.Shards: return sign + a.ToString("0") + " portal shard" + (a == 1 ? "" : "s");
                case OutcomeKind.AccountLoot:
                    return "+" + (l.Reward != null ? Fmt.Reward(l.Reward, content) : l.Id) + " — account loot, paid when you beat the boss";
                case OutcomeKind.Awakening: return "Boss awakening " + sign + a.ToString("0.#");
                case OutcomeKind.AwakenRate: return "Boss awakens " + Fmt.Pct(a) + (l.Amount >= 0 ? " faster" : " slower") + " for the rest of the run";
                case OutcomeKind.BossStat: return "Boss " + sign + Fmt.Pct(a) + " stronger";
                case OutcomeKind.Danger: return "Danger " + sign + a.ToString("0");
                case OutcomeKind.Battle: return l.Id == "won" ? "Fight won" : "Fight lost";
                case OutcomeKind.SkillRoll: return "Skill roll: pick 1 of " + a.ToString("0") + " (" + l.Id + " odds)";
                default: return "Nothing happened.";
            }
        }

        /// <summary>Icon glyph and colour of a result line.</summary>
        private static KeyValuePair<string, Color> OutcomeIcon(OutcomeLine l, ContentDb content)
        {
            switch (l.Kind)
            {
                case OutcomeKind.Coins: return new KeyValuePair<string, Color>("C", Theme.Gold);
                case OutcomeKind.Hp: return new KeyValuePair<string, Color>("HP", l.Good ? Theme.Good : Theme.Bad);
                case OutcomeKind.Stat: return new KeyValuePair<string, Color>("%", l.Good ? Theme.Good : Theme.Bad);
                case OutcomeKind.SkillGained:
                case OutcomeKind.SkillLost:
                case OutcomeKind.SkillLevel:
                    return new KeyValuePair<string, Color>("S", RunPresentation.SkillColor(content, l.Id));
                case OutcomeKind.Xp: case OutcomeKind.LevelUp: return new KeyValuePair<string, Color>("XP", Theme.Premium);
                case OutcomeKind.Shards: return new KeyValuePair<string, Color>("SH", Theme.Premium);
                case OutcomeKind.AccountLoot:
                {
                    var r = l.Reward;
                    var c = r == null ? Theme.Gold : r.Kind == RewardKinds.Currency ? Theme.CurrencyColor(r.Id) : Theme.RarityColor((Rarity)Mathf.Clamp(r.Rarity, 0, (int)Rarity.Mythic));
                    return new KeyValuePair<string, Color>("L", c);
                }
                case OutcomeKind.Battle: return new KeyValuePair<string, Color>("VS", l.Good ? Theme.Good : Theme.Bad);
                case OutcomeKind.SkillRoll: return new KeyValuePair<string, Color>("S", Theme.Purple);
                case OutcomeKind.Nothing: return new KeyValuePair<string, Color>("-", Theme.TextFaint);
                default: return new KeyValuePair<string, Color>("!", Theme.Bad); // boss awakening, boss strength, danger
            }
        }

        private UiModal OutcomeSheet(RunOutcome o)
        {
            var deep = Theme.Hex(0x1C1F3A);
            return Ui.Sheet(App.ModalLayer, "Outcome", deep, Theme.Hex(0x3E4690), (box, modal) =>
            {
                Ui.Caption(box, SourceCaption(o.Source) + " RESULT", Theme.Premium);
                Ui.Display(box, o.Title ?? "Result", 50, Theme.Text);
                if (!string.IsNullOrEmpty(o.Choice) && o.Source != "shop") Ui.Label(box, (o.Source == "minigame" ? "Medal: " : "You chose: ") + o.Choice, Theme.FontNormal, Theme.Hex(0xD7DBFA));

                RectTransform meter = null, marker = null;
                Text stamp = null;
                float landing = 0.5f;
                if (o.Chance)
                {
                    // the chance roll: a bar split into the lucky zone (green, its % chance) and the rest (red); a marker
                    // sweeps back and forth and lands in the zone the rules picked
                    float pct = Mathf.Clamp01((float)(o.SuccessChancePct / 100.0));
                    var panel = Ui.Column(box, 10, 0, null, "ChanceMeter");
                    Ui.Label(panel, Fmt.Pct(Math.Round(o.SuccessChancePct)) + " chance", 34, Theme.Gold, TextAnchor.MiddleCenter, "ChanceLabel").fontStyle = FontStyle.Bold;
                    var track = Ui.Rect(panel, "Track");
                    Ui.Size(track, -1, 64);
                    var good = Ui.RoundPanel(track, Theme.Shade(Theme.Good, 0.8f), 18, "Lucky");
                    good.rectTransform.anchorMin = new Vector2(0, 0); good.rectTransform.anchorMax = new Vector2(pct, 1);
                    good.rectTransform.offsetMin = good.rectTransform.offsetMax = Vector2.zero;
                    var bad = Ui.RoundPanel(track, Theme.Shade(Theme.Bad, 0.8f), 18, "Unlucky");
                    bad.rectTransform.anchorMin = new Vector2(pct, 0); bad.rectTransform.anchorMax = new Vector2(1, 1);
                    bad.rectTransform.offsetMin = bad.rectTransform.offsetMax = Vector2.zero;
                    var m = Ui.RoundPanel(track, Theme.Cream, 8, "Marker");
                    marker = m.rectTransform;
                    marker.anchorMin = marker.anchorMax = new Vector2(0, 0.5f);
                    marker.pivot = new Vector2(0.5f, 0.5f);
                    marker.sizeDelta = new Vector2(18, 92);
                    landing = o.Success ? pct * 0.5f : pct + (1 - pct) * 0.5f;
                    stamp = Ui.Display(panel, "", 48, Theme.Text, TextAnchor.MiddleCenter, "ChanceResult");
                    meter = track;
                }

                var details = Ui.Column(box, 10, 0, null, "Lines");
                if (!string.IsNullOrEmpty(o.Text)) Ui.Label(details, o.Text, Theme.FontNormal, Theme.Hex(0xAEB6F2), TextAnchor.MiddleCenter, "OutcomeText");
                foreach (var l in o.Lines)
                {
                    var icon = OutcomeIcon(l, Content);
                    var row = Ui.Row(details, 70, 16, l.Good ? Theme.Hex(0x173D2A) : Theme.Hex(0x4A1A1D), 18, "Line:" + l.Kind);
                    var dot = Ui.RoundPanel(row, icon.Value, 30, "Icon");
                    Ui.Size(dot, 58, 58, 0);
                    var glyph = Ui.Display(dot.transform, icon.Key, icon.Key.Length > 1 ? 22 : 30, Theme.TextOn(icon.Value), TextAnchor.MiddleCenter);
                    Ui.Stretch(glyph.rectTransform);
                    var t = Ui.Label(row, OutcomeLineText(l, Content), 28, l.Good ? Theme.Hex(0x9CF0B5) : Theme.Hex(0xFFB3B5));
                    t.fontStyle = FontStyle.Bold;
                    Ui.Size(t, -1, -1, 1);
                }
                var cont = Ui.Button(box, "Continue", CloseOutcome, Theme.Accent, -1, 110, true, 40);
                cont.name = "OutcomeContinue";
                Ui.Label(box, _auto ? "Auto continues in a moment." : "Tap Continue.", 22, Theme.Hex(0x8E95C8), TextAnchor.MiddleCenter);

                if (marker != null)
                {
                    var group = details.gameObject.AddComponent<CanvasGroup>();
                    void Land()
                    {
                        float w = meter.rect.width;
                        marker.anchoredPosition = new Vector2(landing * w, 0);
                        stamp.text = o.Success ? "SUCCESS!" : "NO LUCK";
                        stamp.color = o.Success ? Theme.Good : Theme.Bad;
                        group.alpha = 1;
                        _outcomeRevealing = false;
                    }
                    if (InstantOutcomes || !Application.isPlaying) { Land(); return; }
                    group.alpha = 0;
                    _outcomeRevealing = true;
                    var seq = UiSequence.On(modal.Root);
                    seq.Then(ChanceSpinSeconds, t =>
                    {
                        // fast sweeps that slow down and settle on the landing spot
                        float w = meter.rect.width;
                        float decay = (1 - t) * (1 - t);
                        float x = landing + 0.5f * decay * Mathf.Sin(t * Mathf.PI * 9);
                        marker.anchoredPosition = new Vector2(Mathf.Clamp01(x) * w, 0);
                        stamp.text = t < 1 ? "rolling…" : "";
                    }, Land);
                }
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
            bool mythic = run.MythicDraft;
            var mythicColor = Theme.RarityColor(Rarity.Mythic);
            return Ui.Sheet(App.ModalLayer, mythic ? "Mythic choice" : "Choose a skill", mythic ? Theme.Hex(0x2A0E12) : Theme.Hex(0x16122E), mythic ? mythicColor : Theme.Hex(0x5A4FB0), (box, modal) =>
            {
                if (mythic)
                {
                    var banner = Ui.Display(box, "MYTHIC!", 84, mythicColor, TextAnchor.MiddleCenter, "MythicTitle");
                    UiPulse.On(banner, 0.04f, 1.4f, 0.4f);
                    Ui.Label(box, "Miniboss defeated — take 1 of " + run.DraftOffer.Count + " Mythic skills · your build: " + BuildSummary(run), Theme.FontSmall, Theme.Hex(0xFFC9C2), TextAnchor.MiddleCenter);
                }
                else
                {
                    Ui.Display(box, "NEW SKILL!", 76, Theme.Gold, TextAnchor.MiddleCenter, "Title");
                    Ui.Label(box, "Choose 1 of " + run.DraftOffer.Count + " · your build: " + BuildSummary(run), Theme.FontSmall, Theme.Hex(0xC9C2F5), TextAnchor.MiddleCenter);
                }
                foreach (var id in run.DraftOffer)
                {
                    string sid = id;
                    var s = Content.Skill(sid);
                    var fam = s?.Family ?? SkillFamily.None;
                    var rarity = s?.Rarity ?? Rarity.Common;
                    var rarColor = RunPresentation.SkillColor(s); // cards are coloured by rarity; the family is a small tag
                    var btn = Ui.CardButton(box, "Button:Pick", Theme.Tint(rarColor, 0.22f), rarColor,
                        () => { modal.Close(); CloseDecision(); Run(new RunDraftPick { SkillId = sid }); }, out var card, 40, 24, 8);
                    var row = Ui.Row(card, -1, 22, null, 0, "Draft:" + sid);
                    row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
                    row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
                    var icon = Ui.RoundPanel(row, rarColor, 30, "Icon");
                    Ui.Size(icon, 130, 130, 0);
                    var letter = Ui.Display(icon.transform, fam == SkillFamily.None ? "★" : fam.ToString().Substring(0, 1), 64, Theme.TextOn(rarColor), TextAnchor.MiddleCenter);
                    Ui.Stretch(letter.rectTransform);
                    FamilyTag(icon.rectTransform, fam);
                    var text = Ui.Column(row, 4, 0, null, "Text");
                    Ui.Size(text, -1, -1, 1);
                    var tags = Ui.ChipRow(text, 40, 8);
                    var cap = Ui.Label(tags, rarity.ToString().ToUpperInvariant() + (fam == SkillFamily.None ? "" : " · " + fam.ToString().ToUpperInvariant()), 22, Color.Lerp(rarColor, Color.white, 0.35f));
                    cap.fontStyle = FontStyle.Bold;
                    Ui.Size(cap, -1, -1, 0);
                    cap.horizontalOverflow = HorizontalWrapMode.Overflow;
                    int ownedLevel = RunDraft.Level(run, sid);
                    if (ownedLevel > 0) Ui.Chip(tags, "LEVEL " + ownedLevel + " → " + (ownedLevel + 1), Theme.Hex(0x9CF0B5), Theme.Hex(0x0B2A16), 18, 34).name = "LevelUpChip";
                    else if (s?.UpgradesFrom != null) Ui.Chip(tags, "UPGRADE", Theme.Hex(0xD6C2FF), Theme.Hex(0x1D0B33), 18, 34);
                    else if (fam != SkillFamily.None && owned.Contains(fam)) Ui.Chip(tags, "SYNERGY", Theme.Gold, Theme.OnAccent, 18, 34);
                    Ui.Chip(tags, rarity.ToString(), Theme.Tint(Theme.RarityColor(rarity), 0.4f), Theme.RarityColor(rarity), 18, 34, Theme.RarityColor(rarity));
                    Ui.Display(text, s?.Name ?? sid, 40, Theme.Text);
                    string full = Fmt.SkillFull(s);
                    if (!string.IsNullOrEmpty(full)) Ui.Label(text, full, 24, Color.Lerp(rarColor, Color.white, 0.7f)).name = "SkillText";
                    if (s?.UpgradesFrom != null && ownedLevel == 0) Ui.Label(text, "Upgrades " + (Content.Skill(s.UpgradesFrom)?.Name ?? s.UpgradesFrom), 22, Theme.Good);
                    if (ownedLevel > 0) Ui.Label(text, LevelUpText(Content), 22, Theme.Good).name = "LevelUpText";
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
            _shopButtons.Clear();
            return Modal("Shop", b =>
            {
                _shopCoins = Ui.Label(b, run.Coins + " coins · " + run.ShopBuys + " bought this run", Theme.FontNormal, Theme.Gold, TextAnchor.MiddleCenter, "ShopCoins");
                for (int i = 0; i < run.Shop.Count; i++)
                {
                    int index = i;
                    var o = run.Shop[i];
                    bool skill = o.Kind == ShopOfferKind.Skill;
                    var sk = skill ? Content.Skill(o.SkillId) : null;
                    string what = skill ? (sk?.Name ?? o.SkillId)
                        : o.Kind == ShopOfferKind.Heal ? "Heal " + Fmt.Pct(o.Value)
                        : o.Stat != null ? RogueStars.Rules.Core.SkillText.Stat(o.Stat) ?? "+stat" : "+stat";
                    var color = skill ? RunPresentation.SkillColor(sk) : Theme.Good; // skills by rarity
                    var row = Ui.Row(b, -1, 14, o.Sold ? Theme.PanelAlt : Theme.Tint(color, 0.16f), 20, "Offer");
                    var rl = row.GetComponent<HorizontalLayoutGroup>();
                    rl.childAlignment = TextAnchor.MiddleLeft; rl.childForceExpandHeight = false;
                    rl.padding = new RectOffset(20, 20, 16, 16);
                    UiShapes.Outline(row, o.Sold ? Theme.Border : Theme.Shade(color, 0.8f), Theme.RadiusButton, 3);
                    var icon = Ui.RoundPanel(row, o.Sold ? Theme.Locked : color, 18, "Icon");
                    Ui.Size(icon, 64, 64, 0);
                    if (skill && sk != null) FamilyTag(icon.rectTransform, sk.Family);
                    var words = Ui.Column(row, 0, 0, null, "Words");
                    Ui.Size(words, -1, -1, 1);
                    words.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                    // the offer's name is its own text (the tests look the buy button up by the name next to it)
                    Ui.Label(words, what, Theme.FontNormal, o.Sold ? Theme.TextFaint : Theme.Text).fontStyle = FontStyle.Bold;
                    if (skill && sk != null) Ui.Label(words, sk.Rarity + (sk.Family != SkillFamily.None ? " · " + sk.Family : ""), 22, Theme.RarityColor(sk.Rarity)).name = "OfferRarity";
                    // Full text: what the offer actually does (a skill name alone means nothing to a new player).
                    string detail = skill ? Fmt.SkillFull(sk)
                        : o.Kind == ShopOfferKind.Heal ? "Restores " + Fmt.Pct(o.Value) + " of your max HP now."
                        : "Raises the stat for the rest of this run.";
                    if (!string.IsNullOrEmpty(detail)) Ui.Label(words, detail, 24, o.Sold ? Theme.TextFaint : Color.Lerp(color, Color.white, 0.7f)).name = "OfferText";
                    int lv = skill ? RunDraft.Level(run, o.SkillId) : 0;
                    if (lv > 0) Ui.Label(words, "LEVEL " + lv + " → " + (lv + 1) + " · " + LevelUpText(Content), 22, Theme.Good).name = "LevelUpText";
                    else if (skill && sk?.UpgradesFrom != null) Ui.Label(words, "Upgrades " + (Content.Skill(sk.UpgradesFrom)?.Name ?? sk.UpgradesFrom), 22, Theme.Good);
                    var buy = Ui.Button(row, o.Sold ? "Sold" : o.Price + " coins", () => Run(new RunShopBuy { Index = index }, r => { if (r.Ok) RefreshShop(); }), Theme.Gold, 220, 76, !o.Sold && run.Coins >= o.Price, 28);
                    _shopButtons.Add(new KeyValuePair<Button, int>(buy, index));
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
                Note(b, "Pending: " + Fmt.Rewards(run.PendingLoot, Content, State, Now));
            },
            new ModalButton("Cash out", () => Run(new RunBurrowCashOut()), Theme.Good),
            new ModalButton("Descend", () => Run(new RunBurrowDescend()), Theme.Bad));

        private void LootModal(ActiveRun run) =>
            Modal("Pending loot", b =>
            {
                // Rewards follow use (GDD §13.1): loot without a use yet goes to the stash, so it is not listed here.
                var usable = RewardGate.Usable(State, Content, run.PendingLoot, Now);
                if (usable.Count == 0) Ui.Label(b, "Nothing yet.", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter);
                foreach (var r in usable) Ui.Label(b, "+ " + Fmt.Reward(r, Content), Theme.FontSmall, Theme.Gold);
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
                    var col = RunPresentation.SkillColor(s);
                    var row = Ui.Row(b, 72, 12, Theme.Tint(col, 0.22f), 16, "Skill:" + id);
                    UiShapes.Outline(row, Theme.Shade(col, 0.9f), 16, 3);
                    Ui.Dot(row, col, 28, "Rarity");
                    int lv = RunDraft.Level(run, id);
                    Ui.Label(row, (s?.Name ?? id) + (lv > 1 ? " Lv " + lv : "") + " <size=20>(" + (s?.Rarity ?? Rarity.Common) + (s != null && s.Family != SkillFamily.None ? " · " + s.Family : "") + ")</size>", Theme.FontSmall, Color.Lerp(col, Color.white, 0.5f));
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
            Ui.Chip(stats, "Skills " + s.Gains.Count, Theme.Panel, Theme.TextDim, 24, 48).name = "SkillsChip";
            if (s.Minibosses > 0) Ui.Chip(stats, "Minibosses " + s.Minibosses, Theme.Panel, Theme.TextDim, 24, 48);
            if (s.ShopBuys > 0) Ui.Chip(stats, "Shop buys " + s.ShopBuys, Theme.Panel, Theme.TextDim, 24, 48);
            BuildGained(body, s);

            Ui.Caption(body, s.Won ? "Loot secured" : "Pending loot lost", s.Won ? Theme.Hex(0xE8CFA8) : Theme.Bad);
            if (s.Granted.Count == 0) Note(body, s.Won ? "No rewards." : "Better luck next time.");
            var grid = Ui.Grid(body, new Vector2(300, 170), 3, 14);
            foreach (var r in s.Granted) RewardTile(grid, r, s);
            if (s.BiomassBonus && RewardGate.CurrencyHasUse(State, Content, Currencies.Biomass, Now)) Ui.Label(body, "Biomass surge ×" + Content.RunTuning.DailyBiomassBonusMult.ToString("0.#") + " applied", Theme.FontNormal, Theme.CurrencyColor(Currencies.Biomass), TextAnchor.MiddleCenter).name = "SurgeApplied";
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

        /// <summary>One line per build gain of the run: what, its level, and where it came from (draft, MYTHIC, shop, event).</summary>
        public static string GainText(RunGain g, ContentDb c)
        {
            string src = g.Source == "mythic" ? "MYTHIC" : g.Source == "draft" ? "draft" : g.Source == "shop" ? "shop" : g.Source == "boon" ? "boon" : "event";
            if (g.Stat != null) return (RogueStars.Rules.Core.SkillText.Stat(g.Stat) ?? "+stat") + " · " + src;
            var s = c.Skill(g.SkillId);
            return (s?.Name ?? g.SkillId) + (g.Level > 1 ? " Lv " + g.Level : "") + " · " + src;
        }

        private void BuildGained(RectTransform body, RunSummary s)
        {
            if (s.Gains.Count == 0) return;
            Ui.Caption(body, "Skills gained this run (" + s.Gains.Count + ")", Theme.Hex(0xE8CFA8));
            var list = Ui.Grid(body, new Vector2(320, 44), 3, 8); // wraps: 20–40 gains per run
            list.name = "GainedSkills";
            foreach (var g in s.Gains)
            {
                var sk = g.SkillId != null ? Content.Skill(g.SkillId) : null;
                var col = sk != null ? RunPresentation.SkillColor(sk) : g.Source == "mythic" ? Theme.RarityColor(Rarity.Mythic) : Theme.Good; // by rarity
                Ui.Chip(list, GainText(g, Content), Theme.Tint(col, 0.25f), Color.Lerp(col, Color.white, 0.5f), 20, 40).name = "Gain";
            }
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
