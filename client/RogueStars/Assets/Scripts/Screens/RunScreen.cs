using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// The run (vertical slice, reference 1): ONE composed world with a thin HUD floating over it. The board is a real 2D
    /// world (<see cref="WorldBoard"/>: tile blocks on the rules' iso grid, markers standing on tiles, set pieces in the
    /// loops, scatter, the worm) rendered under the uGUI HUD (<see cref="SliceHud"/>: level / HP / ATK / DEF pills, cog,
    /// coins, the boss rail) and the bottom controls (the big dice button on its pedestal — tap to roll, hold for auto —,
    /// the speed chip, auto-pick, the skills book). Decision popups (branch on the board, event cards, skill draft, shop,
    /// revive, Burrow), battle playback and auto mode as before.
    /// Auto mode (hold the dice): sends RunAutoStep for the clear steps (roll, move, tiles, battles with fast playback); a
    /// tap anywhere stops it. At a real decision (RunSystem.RequiresPlayer) auto pauses and the decision UI opens; auto
    /// resumes by itself after it while still on. "Auto-pick: On" (farming, ClientSettings.AutoPickDrafts, off by default)
    /// takes the default choices instead.
    /// </summary>
    public sealed class RunScreen : UiScreen
    {
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
        private int _outcomeSeen;
        private UiModal _outcome;
        private float _outcomeTimer;
        private bool _outcomeRevealing;

        // ---- presentation: the world board, follow camera, HUD shown values, landing feedback ----
        private WorldBoard _world;
        private RectTransform _holder, _overlay, _fx;
        private Vector2 _holderSize;
        /// <summary>Follow camera centre (board-local world units); NaN = not placed yet.</summary>
        private Vector2 _cam = new Vector2(float.NaN, float.NaN);
        /// <summary>View width now (world units; NaN = the default framing, <see cref="DefaultViewWidth"/>).</summary>
        private float _camWidth = float.NaN;
        /// <summary>The player moved / zoomed the view: it stays there until the next roll or move.</summary>
        private bool _camManual;
        /// <summary>Progress (0..1) of the ease back to the follow framing; negative = not returning.</summary>
        private float _camReturn = -1;
        private Vector2 _camReturnFrom;
        private float _camReturnFromWidth;
        private BoardGestureState _gestures;
        /// <summary>The worm's board-local position as drawn now (the camera follows it).</summary>
        private Vector2 _tokenPos;
        private RunHudStats? _hudShown;
        private SliceHud _hud;
        private int _skillCount;
        private DiceView _footerDice;
        private readonly List<KeyValuePair<RectTransform, Vector2>> _pins = new List<KeyValuePair<RectTransform, Vector2>>();
        /// <summary>The board's art (ArtCatalog biome of the level).</summary>
        private BiomeArt _biome = ArtCatalog.Default;
        /// <summary>The run's skills and levels as last drawn (the final boss playback has no active run left).</summary>
        private List<string> _lastSkills = new List<string>();
        private Dictionary<string, int> _lastSkillLevels = new Dictionary<string, int>();
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
        /// <summary>Board animation speed (the speed chip: ×1, ×2, ×3 for dice, hops and landings).</summary>
        public static int BoardSpeed = 1;
        /// <summary>Hop arc height in world units.</summary>
        public const float HopArc = 0.55f;
        /// <summary>The worm's visual middle above its feet (world units): what the follow camera frames.</summary>
        public const float WormFocus = 0.9f;
        /// <summary>
        /// Where the worm's middle sits in the open band between the top HUD and the dice cap (0 = at the HUD, 1 = at the
        /// cap): a little below the band's middle, so more of the path ahead (up the screen) shows.
        /// </summary>
        public const float FocalShare = 0.55f;
        /// <summary>Bottom of the top HUD (the coin chip) from the top, in reference px (× width / 1080).</summary>
        public const float HudTopRef = 340;
        /// <summary>The dice stand's frame reaches this far below the screen's bottom edge (reference px): the column runs off-screen.</summary>
        public const float StandDrop = 30;
        /// <summary>Top of the dice cap from the bottom, in reference px (× <see cref="ControlsScale"/>).</summary>
        public static float ControlsTopRef => DiceStand.CapTop - StandDrop;
        /// <summary>The open band between the HUD and the cap is at least this many world units tall (short screens widen the view).</summary>
        public const float MinBandUnits = 6.5f;
        /// <summary>Widest default view (world units) on the shortest screens (tablets).</summary>
        public const float MaxDefaultWidth = 14f;
        /// <summary>Pinch / wheel zoom: view width between these multiples of the default.</summary>
        public const float MinZoom = 0.8f, MaxZoom = 1.9f;
        /// <summary>Seconds of the eased pan and zoom back to the follow framing (a roll / a move).</summary>
        public const float ReturnSeconds = 0.4f;

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
        /// <summary>No app header and no footer: the HUD floats over the world (reference 1).</summary>
        public override float HeaderHeight => 0;
        public override bool CoversResourceBar => true;
        /// <summary>Toasts float above the dice cap.</summary>
        public override float ToastBottomInset => ControlsScale(_holderSize.x > 0 ? _holderSize : new Vector2(972, 2160)) * (ControlsTopRef + 20);
        /// <summary>Do not rebuild under a running animation (periodic refreshes would cut it short).</summary>
        public override bool AutoRebuild => !Animating;
        public bool Auto => _auto;
        /// <summary>Auto-pick drafts / farming (player setting, off by default).</summary>
        public bool Farming => App.Settings != null && App.Settings.AutoPickDrafts;
        /// <summary>Auto mode is on but waits for the player's decision.</summary>
        public bool AutoPaused => _auto && State.Run.Active != null && RunSystem.RequiresPlayer(Content, State.Run.Active, Farming);
        public bool Animating => _anim != null && _anim.Busy;
        /// <summary>The board animations' speed now (the speed chip; 4 while a tap fast-forwards).</summary>
        private float AnimSpeed => _anim != null ? Mathf.Max(1, _anim.Speed) : Mathf.Max(1, BoardSpeed);
        /// <summary>The world board (tests, captures).</summary>
        public WorldBoard World => _world;

        // ---- pure helpers (tested) ----------------------------------------------------------------

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

        /// <summary>How one board tile is drawn (pure; tested).</summary>
        public struct TileLook
        {
            public string Label;
            public Color Accent;
            /// <summary>Already walked over (in the trail) and not the token's tile. Passed tiles keep their look (board game).</summary>
            public bool Visited;
            /// <summary>The worm landed (stopped) here: only landed tiles are coloured.</summary>
            public bool Landed;
            /// <summary>The worm stands here.</summary>
            public bool Current;
            /// <summary>The path splits after this tile.</summary>
            public bool Fork;
            /// <summary>Resolved (fight won, pickup taken, event decided): its marker leaves the board.</summary>
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

        /// <summary>
        /// The dice button's caption: "Hold for Auto" when a roll is next; what the next tap does otherwise (fight, enter
        /// the arena, pick a path on the board, your decision); auto mode's state.
        /// </summary>
        public static string DiceCaption(ActiveRun run, bool auto, bool autoPaused)
        {
            if (run == null) return "";
            if (auto) return autoPaused ? "Your choice" : "Auto…";
            switch (run.Phase)
            {
                case RunPhase.AwaitRoll: return "Hold for Auto";
                case RunPhase.Encounter: return run.EncounterKind == "boss" ? "Enter Arena" : "Fight!";
                case RunPhase.ChooseBranch: return "Pick a path";
                case RunPhase.Completed: case RunPhase.Failed: return "";
                default: return "Choose";
            }
        }

        // ---- build -------------------------------------------------------------------------------

        public override void Tick()
        {
            base.Tick();
            var run = State?.Run.Active;
            if (_coins != null) _coins.text = run != null ? Fmt.Num(run.Coins) : "-";
        }

        protected override void Build(RectTransform body)
        {
            var run = State.Run.Active;
            // the run fills the screen edge to edge; the finished note keeps the normal padding
            var bodyLayout = body.GetComponent<VerticalLayoutGroup>();
            if (bodyLayout != null)
            {
                int pad = run == null ? 24 : 0;
                bodyLayout.padding = new RectOffset(pad, pad, pad, pad);
                bodyLayout.spacing = run == null ? 18 : 0;
            }
            if (run == null) { BuildFinished(body); return; }
            var level = Content.RunLevel(run.LevelId);
            _biome = ArtCatalog.BiomeFor(level);
            _lastSkills = RunSystem.ActiveSkillIds(run);
            _lastSkillLevels = new Dictionary<string, int>();
            foreach (var id in _lastSkills) _lastSkillLevels[id] = RunDraft.Level(run, id);
            bool newRun = run.RunId != _runId;
            if (newRun)
            {
                _runId = run.RunId; _trailSeen = run.Trail.Count; _rollsSeen = run.Rolls; _battlesSeen = run.BattleCount; _outcomeSeen = run.OutcomeSeq;
                _cam = new Vector2(float.NaN, float.NaN); _camWidth = float.NaN; _camManual = false; _camReturn = -1; _hudShown = null;
            }

            // What changed since the last build → dice, token hops, landing, then battle and decision.
            var path = new List<int>();
            if (run.Trail.Count > _trailSeen) path.AddRange(run.Trail.GetRange(Math.Max(0, _trailSeen - 1), run.Trail.Count - Math.Max(0, _trailSeen - 1)));
            bool rolled = run.Rolls > _rollsSeen;
            bool battle = run.BattleCount > _battlesSeen && run.LastBattle != null;
            var landTile = path.Count > 1 ? run.Tiles[path[path.Count - 1]] : null;
            if (landTile != null && landTile.TimesResolved == 0 && landTile.Id != run.BossGateTile) landTile = null; // stopped at a fork
            var newOutcome = run.OutcomeSeq > _outcomeSeen ? run.LastOutcome : null;
            int tokenStart = path.Count > 1 ? path[0] : run.Position;

            var hudNow = RunPresentation.Hud(Content, run);
            Board(body, run, landTile != null ? landTile.Id : -1, tokenStart);
            Hud(run, _hudShown ?? hudNow);
            Controls(run);
            _trailSeen = run.Trail.Count; _rollsSeen = run.Rolls; _battlesSeen = run.BattleCount;

            _anim = UiSequence.On(Root.gameObject);
            _anim.Clear();
            _anim.Speed = Mathf.Max(1, BoardSpeed);
            bool moving = path.Count > 1 && _world?.Token != null;
            if (moving) LastPopups.Clear();
            if (moving || rolled) ReturnCamera(); // a roll / a new move: the view eases back to the worm
            if (rolled && run.LastDice.Count > 0) RollDice(run.LastDice);
            if (moving)
            {
                float hop = RunFx.HopSeconds(_auto);
                for (int i = 1; i < path.Count; i++)
                {
                    var fromTile = run.Tiles[path[i - 1]];
                    var toTile = run.Tiles[path[i]];
                    var from = WorldBoard.TopOf(fromTile);
                    var to = WorldBoard.TopOf(toTile);
                    Vector2 cFrom = WorldBoard.CellOf(fromTile), cTo = WorldBoard.CellOf(toTile);
                    bool last = i == path.Count - 1;
                    _anim.Then(hop, t =>
                    {
                        // board-game hop: eased step, arc, squash at take-off and landing, stretched in the air
                        float e = RunFx.Ease(t);
                        _tokenPos = Vector2.Lerp(from, to, e);
                        _world?.PlaceToken(_tokenPos, Vector2.Lerp(cFrom, cTo, t < 0.5f ? 0 : 1), RunFx.HopScale(t));
                        _world?.LiftToken(HopArc * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI));
                    }, () =>
                    {
                        // a passed pickup (shard, coin cluster) pays as the worm hops over it; the tile keeps its look
                        if (!last) { var pass = RunPresentation.Pass(Content, run, toTile); if (pass.HasValue) SpawnPopups(new List<LandingPopup> { pass.Value }, to, false); }
                    });
                }
            }
            if (landTile != null)
            {
                var land = landTile;
                var popups = RunPresentation.Landing(Content, run, land, battle, newOutcome);
                _anim.Call(() => Land(land, popups));
                if (!battle) _anim.Call(() => AnimateHud(hudNow));
                // a moment to read the popups before a fight or a decision covers the board
                bool next = battle || RunSystem.RequiresPlayer(Content, run, false) || newOutcome != null && RunPresentation.IsSheetSource(newOutcome.Source);
                if (next && !RunFx.Skip) _anim.Wait(_auto ? 0.45f : 0.85f);
            }
            else if (!battle) _anim.Call(() => AnimateHud(hudNow));
            if (battle)
            {
                var result = run.LastBattle;
                var ctx = BattleContext();
                _anim.Call(() => App.Router.Push(new BattlePlaybackScreen(result, _auto, ctx)));
            }
            _anim.Call(() => ShowOutcomeOrDecision(run));
        }

        // ---- motion: dice, landing, popups, HUD -------------------------------------------------

        /// <summary>Tumbling dice over the board (two when the level rolls two), landing on the rolled values; the button's dice follow.</summary>
        private void RollDice(List<int> values)
        {
            var vals = new List<int>(values);
            float secs = RunFx.DiceSeconds(_auto);
            if (secs <= 0 || _holder == null) { _footerDice?.Pose(1, PrintedDice(vals)); return; }
            var stage = Ui.Rect(_holder, "DiceStage");
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
                _footerDice?.Pose(Mathf.Min(1, t * 1.05f), PrintedDice(vals));
            });
            // the dice rest for a beat, then fade while the worm starts hopping
            _anim.Then(_auto ? 0.18f : 0.4f, null, () =>
            {
                if (stage == null) return;
                UiTweens.Play(stage.gameObject, "fade", 0.38f / AnimSpeed, k => { if (group != null) group.alpha = 1 - k; }, () => { if (stage != null) UnityEngine.Object.Destroy(stage.gameObject); });
            });
        }

        /// <summary>The landing: the tile pops and takes its landed colour (a beaten marker leaves), then the feedback popups.</summary>
        private void Land(RunTile tile, List<LandingPopup> popups)
        {
            LastLandedTile = tile.Id;
            var top = WorldBoard.TopOf(tile);
            if (_world != null && _world.Tiles.TryGetValue(tile.Id, out var view))
            {
                view.ApplyLanded(tile.Cleared);
                var go = view.Go;
                if (!RunFx.Skip && go != null)
                    UiTweens.Play(go, "land", RunFx.LandPopSeconds / AnimSpeed, k => { if (go != null) go.transform.localScale = Vector3.one * RunFx.LandPopScale(k); });
                if (!RunFx.Skip && _overlay != null)
                {
                    var at = Overlay(top);
                    var ring = RunFx.Ring(_overlay, at, new Vector2(190, 130) * (_hud?.Scale ?? 1), Color.Lerp(view.Accent, Color.white, 0.3f));
                    if (ring != null) Pin(ring, top);
                }
            }
            SpawnPopups(popups, top, true);
        }

        /// <summary>Floating texts over a tile (board-local point), plus particles by kind (coins fly to the coin chip).</summary>
        private void SpawnPopups(List<LandingPopup> popups, Vector2 at, bool landing)
        {
            if (_overlay == null) return;
            var above = at + new Vector2(0, 1.15f);
            var pos = Overlay(above);
            for (int i = 0; i < popups.Count; i++)
            {
                var p = popups[i];
                LastPopups.Add(p.Text);
                PopupsSpawned++;
                var ppos = pos + new Vector2(0, i * (RunFx.Skip ? 64 : 0));
                RunFx.FloatText(_overlay, ppos, p.Text, p.Color, landing ? (p.Kind == PopupKind.Fight || p.Kind == PopupKind.LevelUp ? 44 : 36) : 28, i * 0.28f, "Popup:" + p.Kind);
                switch (p.Kind)
                {
                    case PopupKind.Coins:
                        RunFx.Burst(_fx, FxPoint(_overlay, pos), _coins != null ? FxPoint(_coins.rectTransform) : (Vector2?)null, Theme.Gold, 7, 22, i * 0.28f);
                        break;
                    case PopupKind.Heal:
                        RunFx.Burst(_overlay, pos, null, Theme.Good, 8, 18, i * 0.28f);
                        break;
                    case PopupKind.Stat:
                    case PopupKind.Xp:
                    case PopupKind.LevelUp:
                    case PopupKind.Shard:
                        RunFx.Burst(_overlay, pos, null, p.Color, 6, 16, i * 0.28f);
                        break;
                    case PopupKind.Loot:
                        RunFx.Burst(_overlay, pos, null, Theme.Gold, 9, 18, i * 0.28f);
                        break;
                    case PopupKind.Hurt:
                        var body = _world?.TokenBody;
                        if (body != null && !RunFx.Skip)
                            UiTweens.Play(body.gameObject, "hurt", 0.35f, k => { if (body != null) body.localRotation = Quaternion.Euler(0, 0, 14 * Mathf.Sin(k * Mathf.PI * 5) * (1 - k)); });
                        break;
                }
            }
        }

        /// <summary>A point of the overlay (top-left anchored) in the screen's effects layer.</summary>
        private Vector2 FxPoint(RectTransform space, Vector2 anchored)
        {
            if (_fx == null || space == null) return anchored;
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

        /// <summary>HUD numbers move to <paramref name="to"/>: bars tween, changed stat pills pop (instant outside play mode).</summary>
        private void AnimateHud(RunHudStats to)
        {
            var from = _hudShown ?? to;
            _hudShown = to;
            if (_hud == null) return;
            bool atkUp = Math.Abs(to.Atk - from.Atk) >= 0.5, defUp = Math.Abs(to.Def - from.Def) >= 0.5;
            UiTweens.Play(_hud.Root.gameObject, "hud", RunFx.Skip ? 0 : 0.6f, k =>
            {
                float e = RunFx.Ease(k);
                ShowHud(Lerp(from, to, e), to.Level > from.Level ? (k < 0.5f ? from.Level : to.Level) : to.Level, XpFraction(from, to, k));
                float pop = 1 + 0.25f * Mathf.Sin(Mathf.Clamp01(k * 1.6f) * Mathf.PI);
                if (atkUp && _hud?.AtkPill != null) _hud.AtkPill.localScale = Vector3.one * pop;
                if (defUp && _hud?.DefPill != null) _hud.DefPill.localScale = Vector3.one * pop;
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

        private void ShowHud(RunHudStats s, int level, float xpFrac) => _hud?.Show(s, level, xpFrac, _skillCount);

        /// <summary>The HUD as shown now (tests: "HP a / b | ATK n | DEF n | Lv n | XP x / y | SKILLS n").</summary>
        public string HudText => _hud?.Summary;

        /// <summary>What the battle playback shows in its top HUD and skill grid (the worm as the run HUD shows it).</summary>
        private BattleHudContext BattleContext()
        {
            var run = State?.Run.Active;
            var hud = _hudShown ?? (run != null ? RunPresentation.Hud(Content, run) : default(RunHudStats));
            return new BattleHudContext
            {
                Hud = hud, HasStats = _hudShown.HasValue || run != null, Coins = run?.Coins ?? -1, SkillCount = run != null ? RunSystem.BuildSize(run) : _skillCount,
                Skills = new List<string>(_lastSkills), Levels = new Dictionary<string, int>(_lastSkillLevels),
                Rolls = run != null ? RollText(Content, run) : null,
            };
        }

        // ---- follow camera ------------------------------------------------------------------------

        /// <summary>Camera centre in board-local world units (tests).</summary>
        public Vector2 CameraCentre => _cam;
        /// <summary>Camera height in board pixels (100 per world unit; tests of the follow camera).</summary>
        public float CameraY => _cam.y * 100;

        /// <summary>Scale of the top HUD (reference px → holder px): the width over 1080.</summary>
        public static float HudScale(Vector2 holder) => holder.x / 1080f;

        /// <summary>
        /// Scale of the bottom controls: the width over 1080, but never more than the height over 1920 (tablets: the dice
        /// stand does not eat half the screen; it stays centred).
        /// </summary>
        public static float ControlsScale(Vector2 holder) => Mathf.Min(holder.x / 1080f, holder.y / 1920f);

        /// <summary>The open band between the top HUD and the dice cap (holder px from the top: x = top, y = bottom).</summary>
        public static Vector2 OpenBand(Vector2 holder) => new Vector2(HudScale(holder) * HudTopRef, holder.y - ControlsScale(holder) * ControlsTopRef);

        /// <summary>Where the worm's middle sits on screen (holder px from the top-left): centred, in the open band.</summary>
        public static Vector2 FocalPoint(Vector2 holder)
        {
            var band = OpenBand(holder);
            return new Vector2(holder.x / 2, band.x + (band.y - band.x) * FocalShare);
        }

        /// <summary>
        /// The default view width (world units): <see cref="WorldBoard.ViewWidth"/>, wider on short screens so the open band
        /// shows at least <see cref="MinBandUnits"/> (up to <see cref="MaxDefaultWidth"/>).
        /// </summary>
        public static float DefaultViewWidth(Vector2 holder)
        {
            if (holder.x <= 0 || holder.y <= 0) return WorldBoard.ViewWidth;
            var band = OpenBand(holder);
            float units = (band.y - band.x) / (holder.x / WorldBoard.ViewWidth);
            if (units >= MinBandUnits) return WorldBoard.ViewWidth;
            return Mathf.Min(MaxDefaultWidth, WorldBoard.ViewWidth * MinBandUnits / Mathf.Max(0.5f, units));
        }

        /// <summary>
        /// The follow framing (pure; tested at several aspect ratios): the camera centre that puts the worm standing at
        /// <paramref name="feet"/> (board-local) with its middle on <see cref="FocalPoint"/> of a holder of
        /// <paramref name="holder"/> px showing <paramref name="viewWidth"/> world units across.
        /// </summary>
        public static Vector2 FollowCentre(Vector2 feet, Vector2 holder, float viewWidth)
        {
            if (holder.x <= 0 || holder.y <= 0) return feet + new Vector2(0, WormFocus);
            float perUnit = holder.x / viewWidth;
            var focal = FocalPoint(holder);
            return new Vector2(feet.x - (focal.x - holder.x / 2) / perUnit, feet.y + WormFocus + (focal.y - holder.y / 2) / perUnit);
        }

        /// <summary>Where the camera wants to be: the worm's middle on the focal point at the default width.</summary>
        private Vector2 CameraTarget() => FollowCentre(_tokenPos, _holderSize, DefaultViewWidth(_holderSize));

        /// <summary>View width now (world units).</summary>
        public float CameraWidth => float.IsNaN(_camWidth) ? DefaultViewWidth(_holderSize) : _camWidth;
        /// <summary>The player has moved or zoomed the view (it stays until the next roll / move).</summary>
        public bool CameraManual => _camManual;
        /// <summary>The view is easing back to the follow framing.</summary>
        public bool CameraReturning => _camReturn >= 0;

        private void ApplyCamera()
        {
            if (_world == null || float.IsNaN(_cam.x)) return;
            _world.SetView(_cam, CameraWidth);
            if (_holder != null)
                foreach (var kv in _pins)
                    if (kv.Key != null) kv.Key.anchoredPosition = Overlay(kv.Value) + PinOffset(kv.Key);
        }

        private readonly Dictionary<RectTransform, Vector2> _pinOffsets = new Dictionary<RectTransform, Vector2>();
        private Vector2 PinOffset(RectTransform rt) => _pinOffsets.TryGetValue(rt, out var o) ? o : Vector2.zero;

        /// <summary>Keeps an overlay element over a board-local point while the camera moves (tap targets, rings).</summary>
        private void Pin(RectTransform rt, Vector2 local, Vector2 offset = default)
        {
            _pins.Add(new KeyValuePair<RectTransform, Vector2>(rt, local));
            if (offset != default) _pinOffsets[rt] = offset;
            rt.anchoredPosition = Overlay(local) + offset;
        }

        /// <summary>A board-local world point in the board's overlay (top-left anchored; tests).</summary>
        public Vector2 OverlayPoint(Vector2 local) => Overlay(local);

        /// <summary>A board-local world point in the overlay (top-left anchored).</summary>
        private Vector2 Overlay(Vector2 local) => _world != null ? _world.ToOverlay(local, _holderSize) : Vector2.zero;

        /// <summary>
        /// One camera frame: the player's view stays put; a return eases (smoothstep, <see cref="ReturnSeconds"/>) from where
        /// the player left it to the follow framing, then the camera follows the worm (exponential ease, never a snap).
        /// </summary>
        public void StepCamera(float dt)
        {
            if (_world == null || float.IsNaN(_cam.x)) return;
            if (_camManual) { ApplyCamera(); return; }
            var target = CameraTarget();
            float width = DefaultViewWidth(_holderSize);
            if (_camReturn >= 0)
            {
                _camReturn = Mathf.Min(1, _camReturn + dt / ReturnSeconds);
                float e = RunFx.Ease(_camReturn);
                _cam = Vector2.Lerp(_camReturnFrom, target, e);
                _camWidth = Mathf.Lerp(_camReturnFromWidth, width, e);
                if (_camReturn >= 1) { _camReturn = -1; _camWidth = float.NaN; }
                ApplyCamera();
                return;
            }
            _camWidth = float.NaN;
            // RunFx.CameraStep works in board pixels: 100 per world unit; it keeps pace with the board speed while the worm moves
            float cdt = Animating ? dt * AnimSpeed : dt;
            _cam = new Vector2(RunFx.CameraStep(_cam.x * 100, target.x * 100, cdt) / 100, RunFx.CameraStep(_cam.y * 100, target.y * 100, cdt) / 100);
            ApplyCamera();
        }

        /// <summary>Back to the follow framing: eased from wherever the player left the view (no-op when already following).</summary>
        public void ReturnCamera()
        {
            if (!_camManual && float.IsNaN(_camWidth)) return;
            _camManual = false;
            if (float.IsNaN(_cam.x)) { _camWidth = float.NaN; return; }
            _camReturnFrom = _cam;
            _camReturnFromWidth = CameraWidth;
            _camReturn = 0;
        }

        /// <summary>A board-local world point under a holder point (px from the top-left) for a view centre and width.</summary>
        private Vector2 WorldAt(Vector2 holderPx, Vector2 centre, float width)
        {
            float perUnit = _holderSize.x / width;
            return centre + new Vector2((holderPx.x - _holderSize.x / 2) / perUnit, -(holderPx.y - _holderSize.y / 2) / perUnit);
        }

        /// <summary>
        /// The player moves the view (the gestures; tests call it directly): the world point under <paramref name="from"/>
        /// (holder px from the top-left) ends up under <paramref name="to"/>, the view <paramref name="zoom"/> times closer
        /// (pinch: &gt; 1 = fingers apart). The width stays within <see cref="MinZoom"/>..<see cref="MaxZoom"/> of the
        /// default, the centre within the drawn path (plus a margin), so the board is never lost.
        /// </summary>
        public void MoveView(Vector2 from, Vector2 to, float zoom)
        {
            if (_world == null || float.IsNaN(_cam.x) || _holderSize.x <= 0) return;
            float baseW = DefaultViewWidth(_holderSize), width = CameraWidth;
            float newW = Mathf.Clamp(width / Mathf.Max(0.01f, zoom), baseW * MinZoom, baseW * MaxZoom);
            var anchor = WorldAt(from, _cam, width);
            float perUnit = _holderSize.x / newW;
            var centre = anchor - new Vector2((to.x - _holderSize.x / 2) / perUnit, -(to.y - _holderSize.y / 2) / perUnit);
            // bounds: the drawn path and the follow framing, plus a margin
            var b = _world.PathBounds;
            var follow = CameraTarget();
            float xMin = Mathf.Min(b.xMin, follow.x) - 1.5f, xMax = Mathf.Max(b.xMax, follow.x) + 1.5f;
            float yMin = Mathf.Min(b.yMin, follow.y) - 1.5f, yMax = Mathf.Max(b.yMax, follow.y) + 1.5f;
            _cam = new Vector2(Mathf.Clamp(centre.x, xMin, xMax), Mathf.Clamp(centre.y, yMin, yMax));
            _camWidth = newW;
            _camManual = true;
            _camReturn = -1;
            ApplyCamera();
        }

        // ---- HUD ----------------------------------------------------------------------------------

        /// <summary>The top HUD (level, HP, ATK, DEF, cog, coins) and the boss rail over the board.</summary>
        private void Hud(ActiveRun run, RunHudStats shown)
        {
            _skillCount = RunSystem.BuildSize(run);
            _hud = SliceHud.Build(_holder, _holderSize.x, PauseModal, () => Session.Info("Run level " + _hud.Level.text + " · " + _hud.XpText), true);
            _coins = _hud.Coins;
            SliceHud.BossRail(_holder, RunPresentation.BossClock(Content, run), _hud.Scale);
            ShowHud(shown, shown.Level, shown.XpNeed > 0 ? (float)shown.Xp / shown.XpNeed : 0);
        }

        // ---- board ----------------------------------------------------------------------------------

        /// <summary>The board's art (ArtCatalog biome of the level) as drawn now (tests).</summary>
        public BiomeArt Biome => _biome;

        private void Board(RectTransform body, ActiveRun run, int landingId, int tokenTile)
        {
            float frameH = BodyFrame != null ? BodyFrame.rect.height : 1920;
            float frameW = BodyFrame != null ? BodyFrame.rect.width : 1080;
            if (frameH < 100) frameH = 1920;
            if (frameW < 100) frameW = 1080;
            _holderSize = new Vector2(frameW, frameH);
            _pins.Clear();
            _pinOffsets.Clear();
            var holder = Ui.Rect(body, "BoardHolder");
            Ui.Size(holder, -1, frameH);
            _holder = holder;

            if (_world == null || _world.Disposed)
            {
                _world = new WorldBoard("RunBoard");
                var life = Root.gameObject.GetComponent<WorldLifetime>() ?? Root.gameObject.AddComponent<WorldLifetime>();
                var w = _world;
                life.OnGone = () => w.Dispose();
            }
            var canvas = Root.GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.rootCanvas != null ? canvas.rootCanvas.scaleFactor : 1;
            if (scale <= 0) scale = 1;
            var rt = _world.EnsureTarget(Mathf.RoundToInt(frameW * scale), Mathf.RoundToInt(frameH * scale));
            var view = Ui.Rect(holder, "BoardView");
            Ui.Stretch(view);
            var raw = view.gameObject.AddComponent<RawImage>();
            raw.texture = rt;
            raw.color = Color.white;
            // one-finger drag pans, pinch / the mouse wheel zoom; the board view and the tap-target overlay share the
            // gesture (a drag that starts on a fork target pans, a tap on it still picks)
            _gestures = new BoardGestureState { Space = holder, OnMove = MoveView };
            view.gameObject.AddComponent<BoardGestures>().State = _gestures;

            // fork: the path each option walks with the steps left
            var onPath = new Dictionary<int, int>();
            if (run.Phase == RunPhase.ChooseBranch)
                for (int i = 0; i < run.BranchOptions.Count; i++)
                    foreach (var id in BranchPath(run, i, Content)) if (!onPath.ContainsKey(id)) onPath[id] = i;
            _world.Build(run, Content, landingId, tokenTile, onPath, BoardSeed(run.RunId));
            _tokenPos = WorldBoard.TopOf(run.Tiles[Mathf.Clamp(tokenTile, 0, run.Tiles.Count - 1)]);

            _overlay = Ui.Rect(holder, "BoardOverlay");
            Ui.Stretch(_overlay);
            _overlay.gameObject.AddComponent<BoardGestures>().State = _gestures;
            var og = _overlay.gameObject.AddComponent<CanvasGroup>();
            og.blocksRaycasts = true;
            og.interactable = true;

            // place the camera: first build centres on the worm; later builds keep it where it was (it then eases)
            if (float.IsNaN(_cam.x)) _cam = CameraTarget();
            ApplyCamera();

            // fork: tap targets over the tile each option lands on (A / B) and the option tile itself
            if (run.Phase == RunPhase.ChooseBranch)
                for (int i = 0; i < run.BranchOptions.Count; i++)
                {
                    int index = i;
                    string letter = i == 0 ? "A" : "B";
                    var col = BranchColor(i);
                    int landId = BranchLanding(run, i, Content);
                    if (landId < 0) continue;
                    float s = frameW / 1080f;
                    var pick = Ui.Button(_overlay, "", () => PickBranch(index), new Color(col.r, col.g, col.b, 0.25f), 190 * s, 150 * s, true, 20);
                    pick.name = "BranchPick:" + letter;
                    var prt = (RectTransform)pick.transform;
                    prt.anchorMin = prt.anchorMax = new Vector2(0, 1);
                    prt.pivot = new Vector2(0.5f, 0.5f);
                    prt.sizeDelta = new Vector2(190 * s, 150 * s);
                    Pin(prt, WorldBoard.TopOf(run.Tiles[landId]) + new Vector2(0, 0.2f));
                    UiShapes.Outline(prt, col, 30 * s, 6);
                    var badge = Ui.RoundPanel(prt, col, 30 * s, "LandingBadge");
                    badge.raycastTarget = false;
                    var brt = badge.rectTransform;
                    brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1); brt.pivot = new Vector2(0.5f, 0.5f);
                    brt.sizeDelta = new Vector2(150 * s, 56 * s); brt.anchoredPosition = new Vector2(0, 12 * s);
                    var bl = SliceKit.Chunky(badge.transform, letter + " · LAND", (int)(28 * s), Color.white, TextAnchor.MiddleCenter);
                    Ui.Stretch(bl.rectTransform);
                    UiPulse.On(badge, 0.06f, 1.2f, 0.6f);
                    var opt = Ui.Button(_overlay, "", () => PickBranch(index), new Color(0, 0, 0, 0.01f), 160 * s, 110 * s, true, 20);
                    opt.name = "BranchOption:" + letter;
                    var ort = (RectTransform)opt.transform;
                    ort.anchorMin = ort.anchorMax = new Vector2(0, 1);
                    ort.pivot = new Vector2(0.5f, 0.5f);
                    ort.sizeDelta = new Vector2(160 * s, 110 * s);
                    Pin(ort, WorldBoard.TopOf(run.Tiles[run.BranchOptions[i]]));
                }

            // effects layer over the whole screen (coins flying to the coin chip)
            if (_fx == null || _fx.parent != Root)
            {
                _fx = Ui.Rect(Root, "Fx");
                Ui.Stretch(_fx);
                _fx.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            }
            _fx.SetAsLastSibling();
        }

        /// <summary>Seed of the board's cosmetic scatter (FNV-1a of the run id).</summary>
        public static int BoardSeed(string runId)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char ch in runId ?? "") h = (h ^ ch) * 16777619;
                return (int)h;
            }
        }

        /// <summary>
        /// The dice printed on the roll button: always (at least) two, like a pair of dice — a one-die roll shows its die and
        /// the face opposite it beside it.
        /// </summary>
        public static List<int> PrintedDice(IList<int> rolled)
        {
            var list = new List<int>(rolled);
            if (list.Count == 1) list.Add(7 - Mathf.Clamp(list[0], 1, 6));
            if (list.Count == 0) { list.Add(3); list.Add(5); }
            return list;
        }

        // ---- bottom controls -------------------------------------------------------------------------

        /// <summary>
        /// The bottom controls (reference 1): the big dice button on its stone pedestal (tap = roll / fight / reopen the
        /// decision, hold = auto; its caption says what the tap does), the last roll's dice printed on it, the speed chip
        /// and the auto-pick chip bottom-left, the skills book bottom-right.
        /// </summary>
        private void Controls(ActiveRun run)
        {
            // the controls live in a 1080 × 2400 reference frame anchored bottom-centre (ControlsScale: tablets keep them compact)
            float s = ControlsScale(_holderSize);
            var root = Ui.Rect(_holder, "Controls");
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0);
            root.pivot = new Vector2(0.5f, 0);
            root.sizeDelta = new Vector2(1080 * s, 2400 * s);
            root.anchoredPosition = Vector2.zero;

            // the dice stand: pedestal and cap drawn by one camera into one frame (DiceStand), laid over each other
            float standY = 2400 + StandDrop - DiceStand.FrameH / 2f;
            var pedestal = Ui.Panel(root, Color.white, "Pedestal");
            pedestal.sprite = HudArt.Pedestal();
            pedestal.raycastTarget = false;
            SliceKit.Place(pedestal.rectTransform, s, 540, standY, DiceStand.FrameW, DiceStand.FrameH, true);

            bool encounter = run.Phase == RunPhase.Encounter;
            bool canRoll = run.Phase == RunPhase.AwaitRoll || encounter;
            bool deciding = !canRoll && run.Phase != RunPhase.Completed && run.Phase != RunPhase.Failed;
            bool live = canRoll || _auto || deciding;
            var btnRt = Ui.Rect(root, "RollButton");
            SliceKit.Place(btnRt, s, 540, standY, DiceStand.FrameW, DiceStand.FrameH, true);
            // the cap pulses / presses about its base on the slab
            var face = Ui.Panel(btnRt, Color.white, "Cap");
            face.sprite = HudArt.DiceButton(live);
            var frt = face.rectTransform;
            frt.anchorMin = frt.anchorMax = frt.pivot = DiceStand.Base01;
            frt.sizeDelta = new Vector2(DiceStand.FrameW * s, DiceStand.FrameH * s);
            frt.anchoredPosition = Vector2.zero;
            var btn = btnRt.gameObject.AddComponent<Button>();
            btn.targetGraphic = face;
            btn.transition = Selectable.Transition.ColorTint;
            btn.interactable = live;
            // two dice printed on the cap's top (white on red, laid into the top plane: the plane squashes by the camera's
            // pitch, the dice turn a little in it); after a roll they show the last roll's dice, which the tumbling dice land on
            var shown = PrintedDice(run.LastDice.Count > 0 ? run.LastDice : new List<int> { 3, 5 });
            int diceN = shown.Count;
            float dieSize = (diceN == 1 ? 150 : diceN == 2 ? 146 : Mathf.Max(70, 300f / diceN)) * s;
            DiceStand.TopPlane(out var topAt, out float squash);
            var plane = Ui.Rect(face.transform, "DicePrint");
            plane.anchorMin = plane.anchorMax = topAt;
            plane.sizeDelta = new Vector2(10, 10);
            plane.anchoredPosition = Vector2.zero;
            plane.localScale = new Vector3(1, squash, 1);
            var dice = new DiceView(plane, diceN, dieSize, "FooterDice", new Color(1, 0.96f, 0.94f, 0.95f), new Color32(0xD8, 0x3A, 0x34, 255));
            dice.Root.anchorMin = dice.Root.anchorMax = new Vector2(0.5f, 0.5f);
            dice.Root.sizeDelta = new Vector2(diceN * dieSize * 1.3f, dieSize * 1.2f);
            dice.Root.anchoredPosition = Vector2.zero;
            dice.Root.localRotation = Quaternion.Euler(0, 0, -8);
            dice.Pose(1, shown);
            _footerDice = dice;
            // the caption is engraved into the cap's front wall (dark red, a light lip under it)
            var caption = Ui.Display(face.transform, DiceCaption(run, _auto, AutoPaused), (int)(Mathf.Min(50, DiceStand.FrontWallHeight * 0.62f) * s), live ? (Color)new Color32(0x9A, 0x1C, 0x26, 255) : (Color)new Color32(0x4E, 0x48, 0x5A, 255), TextAnchor.MiddleCenter, "Label");
            caption.raycastTarget = false;
            caption.horizontalOverflow = HorizontalWrapMode.Overflow;
            var lip = caption.gameObject.AddComponent<Shadow>();
            lip.effectColor = new Color(1, 0.72f, 0.66f, 0.75f);
            lip.effectDistance = new Vector2(1.5f, -3f);
            caption.rectTransform.anchorMin = caption.rectTransform.anchorMax = DiceStand.FrontWall01;
            caption.rectTransform.sizeDelta = new Vector2(420 * s, 70 * s);
            var hold = btnRt.gameObject.AddComponent<HoldButton>();
            hold.OnTap = () =>
            {
                if (_auto) StopAuto();
                else if (Animating) _anim.Speed = 4; // tap while the dice roll / the worm hops: fast-forward
                else
                {
                    var r = State.Run.Active;
                    if (r != null && r.Phase == RunPhase.AwaitRoll) Run(new RunRoll());
                    else if (r != null && r.Phase == RunPhase.Encounter) Run(new RunEncounter());
                    else if (r != null && r.Phase == RunPhase.ChooseBranch) { ReturnCamera(); Rebuild(); } // the camera eases back to the fork
                    else if (r != null) { _decisionKey = null; ShowOutcomeOrDecision(r); }
                }
            };
            hold.OnHold = StartAuto;
            if (live && !_auto && canRoll) UiPulse.On(face, 0.025f, 1.6f, 1f);

            // speed chip (×1 / ×2 / ×3 board animations) and the auto-pick chip above it
            var speed = Ui.Button(root, "", CycleSpeed, new Color32(0x3A, 0x2E, 0x58, 245), 210 * s, 110 * s, true, 20);
            speed.name = "SpeedChip";
            SliceKit.Place((RectTransform)speed.transform, s, 142, 2284, 214, 112, true);
            UiShapes.Outline((RectTransform)speed.transform, SliceKit.PillEdge, 28 * s, 5);
            var sIcon = Ui.Panel(speed.transform, Color.white, "SpeedIcon");
            sIcon.sprite = HudArt.Icon("speed");
            sIcon.preserveAspect = true;
            sIcon.raycastTarget = false;
            sIcon.rectTransform.anchorMin = sIcon.rectTransform.anchorMax = new Vector2(0.3f, 0.5f);
            sIcon.rectTransform.sizeDelta = new Vector2(64 * s, 64 * s);
            sIcon.rectTransform.anchoredPosition = Vector2.zero;
            var sText = SliceKit.Chunky(speed.transform, "x" + Mathf.Max(1, BoardSpeed), (int)(48 * s), Color.white, TextAnchor.MiddleCenter, "SpeedText");
            sText.rectTransform.anchorMin = sText.rectTransform.anchorMax = new Vector2(0.7f, 0.5f);
            sText.rectTransform.sizeDelta = new Vector2(100 * s, 60 * s);

            var pick = Ui.Button(root, "", ToggleFarming, Farming ? (Color)new Color32(0xF6, 0xC8, 0x3C, 255) : (Color)new Color32(0x5A, 0x4E, 0x6E, 240), 200 * s, 74 * s, true, 20);
            pick.name = "AutoPickToggle";
            SliceKit.Place((RectTransform)pick.transform, s, 126, 2164, 184, 72, true);
            UiShapes.Outline((RectTransform)pick.transform, SliceKit.PillEdge, 22 * s, 4);
            var pt = SliceKit.Chunky(pick.transform, "Auto-pick\n<size=" + (int)(22 * s) + ">" + (Farming ? "ON" : "OFF") + "</size>", (int)(26 * s), Color.white, TextAnchor.MiddleCenter, "AutoPickText");
            Ui.Stretch(pt.rectTransform);

            // the skills book (count badge) bottom-right
            var book = Ui.Button(root, "", () => SkillsModal(run), new Color(0, 0, 0, 0), 150 * s, 160 * s, true, 20);
            book.name = "SkillsButton";
            SliceKit.Place((RectTransform)book.transform, s, 980, 2262, 140, 150, true);
            var bimg = Ui.Panel(book.transform, Color.white, "BookIcon");
            bimg.sprite = HudArt.Icon("book");
            bimg.preserveAspect = true;
            bimg.raycastTarget = false;
            bimg.rectTransform.anchorMin = bimg.rectTransform.anchorMax = new Vector2(0.5f, 0.55f);
            bimg.rectTransform.sizeDelta = new Vector2(124 * s, 124 * s);
            bimg.rectTransform.anchoredPosition = Vector2.zero;
            var bl = SliceKit.Chunky(root, "Skills", (int)(46 * s), Color.white, TextAnchor.MiddleCenter, "SkillsLabel");
            SliceKit.Place(bl.rectTransform, s, 982, 2352, 200, 60, true);
            var badge = Ui.Dot(book.transform, new Color32(0xEE, 0x3E, 0x4E, 255), 46 * s, "SkillBadge");
            var le = badge.GetComponent<LayoutElement>(); if (le != null) le.ignoreLayout = true;
            badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(0.88f, 0.9f);
            badge.rectTransform.sizeDelta = new Vector2(46 * s, 46 * s);
            badge.rectTransform.anchoredPosition = Vector2.zero;
            UiShapes.Outline(badge.rectTransform, SliceKit.Ink, 23 * s, 3);
            var count = SliceKit.Chunky(badge.transform, _skillCount.ToString(), (int)(28 * s), Color.white, TextAnchor.MiddleCenter, "SkillCount");
            Ui.Stretch(count.rectTransform);
            if (_hud != null) _hud.Skills = count;
        }

        /// <summary>The speed chip: board animations ×1 → ×2 → ×3 → ×1.</summary>
        public void CycleSpeed()
        {
            BoardSpeed = BoardSpeed >= 3 ? 1 : BoardSpeed + 1;
            if (_anim != null) _anim.Speed = BoardSpeed;
            Rebuild();
        }

        private void PauseModal()
        {
            var run = State?.Run.Active;
            if (run == null) { App.Router.Pop(); return; }
            Modal("Paused", b =>
            {
                // the HUD has no title: the level and its boss are named here
                var boss = run.BossId != null && Content.RunBosses.TryGetValue(run.BossId, out var bd) ? bd : null;
                Ui.Display(b, Title, 36, Theme.Cream, TextAnchor.MiddleCenter, "PauseLevel");
                Ui.Label(b, Subtitle + (boss != null ? "\nBoss: " + boss.Name : ""), Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter);
                Ui.Label(b, "Pending loot: " + run.PendingLoot.Count + " items · " + run.Coins + " coins", Theme.FontNormal, Theme.TextDim, TextAnchor.MiddleCenter, "PendingLoot");
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
                var ctx = BattleContext();
                _anim.Call(() => App.Router.Push(new BattlePlaybackScreen(battle, _auto, ctx)));
            }
            _toResult = true;
            CloseDecision();
            Ui.Button(body, "See results", () => App.Router.Replace(new RunResultScreen()), Theme.Accent, -1, 120, true, Theme.FontLarge);
        }

        // ---- frame: auto mode and result transition -------------------------------------------

        public override void Frame(float dt)
        {
            // the screen's size changed under a built board (rotation, a late safe area, a resized window): rebuild, so the
            // board's render target, the HUD and the framing match the real aspect again
            if (_world != null && BodyFrame != null && !Animating && State?.Run.Active != null
                && (Mathf.Abs(BodyFrame.rect.width - _holderSize.x) > 1 || Mathf.Abs(BodyFrame.rect.height - _holderSize.y) > 1)
                && BodyFrame.rect.width > 100 && BodyFrame.rect.height > 100)
                Rebuild();
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

        /// <summary>"Fire 2 · Radiation 1": the run's skills by family.</summary>
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
            foreach (var f in order) parts.Add(SkillText.FamilyName(f) + " " + counts[f]);
            return parts.Count == 0 ? "no skills yet" : string.Join(" · ", parts);
        }

        /// <summary>Scale of the modal layer against the 1080-px reference width.</summary>
        private float ModalScale()
        {
            float w = App.ModalLayer is RectTransform rt ? rt.rect.width : 0;
            return (w > 100 ? w : 1080) / 1080f;
        }

        /// <summary>
        /// The level-up draft (reference 2): the board dimmed behind, the hero large at the top, the gold "LEVEL UP!" ribbon
        /// (red "MYTHIC!" after a miniboss), "Select a Skill to learn!", one card per offered skill (tier tag, framed icon,
        /// name ribbon, cream panel with coloured keywords, "?" for the full text) and the purple reroll button.
        /// </summary>
        private UiModal DraftModal(ActiveRun run)
        {
            var owned = new HashSet<SkillFamily>();
            foreach (var id in RunSystem.ActiveSkillIds(run)) owned.Add(Content.Skill(id)?.Family ?? SkillFamily.None);
            bool mythic = run.MythicDraft;
            float s = ModalScale();
            var root = SliceCards.Overlay(App.ModalLayer, mythic ? "Mythic choice" : "Choose a skill", out var modal);
            SliceCards.Header(root, s, mythic ? SliceCards.RedRibbon : SliceCards.GoldRibbon, mythic ? "MYTHIC!" : "LEVEL UP!", mythic ? "MythicTitle" : "Title",
                mythic ? "Miniboss defeated! Take a Mythic skill" : "Select a Skill to learn!");
            if (mythic) UiPulse.On(root.Find("Ribbon").GetComponent<Image>(), 0.03f, 1.4f, 1f);
            int n = Math.Max(1, run.DraftOffer.Count);
            float step = Mathf.Min(287, 900f / n), y = 1050;
            foreach (var id in run.DraftOffer)
            {
                string sid = id;
                var sk = Content.Skill(sid);
                var fam = sk?.Family ?? SkillFamily.None;
                var rarity = sk?.Rarity ?? Rarity.Common;
                int ownedLevel = RunDraft.Level(run, sid);
                string full = ownedLevel > 0 && sk != null ? SkillText.UpgradeFull(Content, sk, ownedLevel, ownedLevel + 1) : Fmt.SkillFull(sk);
                if (sk?.UpgradesFrom != null && ownedLevel == 0) full += " Upgrades " + (Content.Skill(sk.UpgradesFrom)?.Name ?? sk.UpgradesFrom) + ".";
                var spec = new SkillCardSpec
                {
                    Id = sid, Tier = RunTuning.RunTier(rarity).ToString(), TierColor = RunPresentation.SkillColor(sk), Family = fam,
                    IconArt = ArtCatalog.SkillIcon(sid),
                    Title = ownedLevel > 0 && sk != null ? SkillText.Title(sk, ownedLevel + 1) : sk?.Name ?? sid,
                    Body = full,
                };
                if (ownedLevel > 0) { spec.Chip = "LEVEL " + ownedLevel + " → " + (ownedLevel + 1); spec.ChipName = "LevelUpChip"; spec.ChipColor = new Color32(0x9C, 0xF0, 0xB5, 255); }
                else if (sk?.UpgradesFrom != null) { spec.Chip = "UPGRADE"; spec.ChipColor = new Color32(0xD6, 0xC2, 0xFF, 255); }
                else if (fam != SkillFamily.None && owned.Contains(fam)) { spec.Chip = "SYNERGY"; spec.ChipName = "SynergyChip"; spec.ChipColor = Theme.Gold; }
                string info = (sk?.Name ?? sid) + " (" + rarity + (fam != SkillFamily.None ? " · " + SkillText.FamilyName(fam) : "") + "): " + full + (ownedLevel > 0 ? " " + LevelUpText(Content) : "");
                SliceCards.Card(root, s, y, spec, () => { modal.Close(); CloseDecision(); Run(new RunDraftPick { SkillId = sid }); }, () => Session.Info(info));
                y += step;
            }
            SliceCards.Reroll(root, s, "Reroll (" + run.DraftRerolls + ")", () => Run(new RunDraftReroll()), run.DraftRerolls > 0);
            UiTween.PopIn(root, 0.15f);
            return modal;
        }

        /// <summary>
        /// The shop in the same card family: blue "SHOP" ribbon, the coins line, one compact card per offer (skills by tier,
        /// heals and stat perks green) with its price button, reroll and leave. Buying keeps it open (<see cref="RefreshShop"/>).
        /// </summary>
        private UiModal ShopModal(ActiveRun run)
        {
            var table = Content.RunShopTables.TryGetValue(Content.RunLevel(run.LevelId)?.ShopTableId ?? "", out var st) ? st : null;
            long reroll = table != null ? table.RerollBase + table.RerollStep * run.ShopRerolls : 0;
            _shopButtons.Clear();
            float s = ModalScale();
            var root = SliceCards.Overlay(App.ModalLayer, "Shop", out var modal);
            SliceCards.Header(root, s, SliceCards.BlueRibbon, "SHOP", "Title", null, 0, 300);
            _shopCoins = SliceKit.Chunky(root, run.Coins + " coins · " + run.ShopBuys + " bought this run", (int)(42 * s), new Color32(0xFF, 0xE0, 0x6A, 255), TextAnchor.MiddleCenter, "ShopCoins");
            SliceKit.Place(_shopCoins.rectTransform, s, 540, 405, 1000, 60);
            int n = Math.Max(1, run.Shop.Count);
            float h = Mathf.Min(200, 1460f / n - 12), y = 470 + h / 2;
            for (int i = 0; i < run.Shop.Count; i++)
            {
                int index = i;
                var o = run.Shop[i];
                bool skill = o.Kind == ShopOfferKind.Skill;
                var sk = skill ? Content.Skill(o.SkillId) : null;
                string what = skill ? (sk?.Name ?? o.SkillId)
                    : o.Kind == ShopOfferKind.Heal ? "Heal " + Fmt.Pct(o.Value)
                    : o.Stat != null ? SkillText.Stat(o.Stat) ?? "+stat" : "+stat";
                int ownedLv = skill ? RunDraft.Level(run, o.SkillId) : 0;
                string detail = skill ? (ownedLv > 0 && sk != null ? SkillText.UpgradeFull(Content, sk, ownedLv, ownedLv + 1) : Fmt.SkillFull(sk))
                    : o.Kind == ShopOfferKind.Heal ? "Restores " + Fmt.Pct(o.Value) + " of your max HP now."
                    : "Raises the stat for the rest of this run.";
                var spec = new SkillCardSpec
                {
                    Id = o.SkillId ?? ("perk" + i), Tier = skill ? RunTuning.RunTier(sk?.Rarity ?? Rarity.Common).ToString() : o.Kind == ShopOfferKind.Heal ? "Heal" : "Perk",
                    TierColor = skill ? RunPresentation.SkillColor(sk) : Theme.Good, Family = sk?.Family ?? SkillFamily.None,
                    IconArt = skill ? ArtCatalog.SkillIcon(o.SkillId) : HudArt.Icon(o.Kind == ShopOfferKind.Heal ? "heart" : "sword"),
                    Title = ownedLv > 0 && sk != null ? SkillText.Title(sk, ownedLv + 1) : what, Body = detail, Dim = o.Sold,
                };
                if (ownedLv > 0) { spec.Chip = "LEVEL " + ownedLv + " → " + (ownedLv + 1); spec.ChipName = "LevelUpChip"; spec.ChipColor = new Color32(0x9C, 0xF0, 0xB5, 255); }
                var card = SliceCards.Card(root, s, y, spec, null, null, "Offer", h, "Words");
                // the offer's name is its own text (the tests look the buy button up by the name next to it)
                var nameText = card.Find("Words/SkillName")?.GetComponent<Text>();
                if (nameText != null) nameText.text = spec.Title;
                var words = card.Find("Words") as RectTransform;
                var body = words != null ? words.Find("SkillText") as RectTransform : null;
                if (body != null) body.offsetMax = new Vector2(-250 * s, body.offsetMax.y);
                var buy = Ui.Button(card, o.Sold ? "Sold" : o.Price + " coins", () => Run(new RunShopBuy { Index = index }, r => { if (r.Ok) RefreshShop(); }), Theme.Gold, 210 * s, 84 * s, !o.Sold && run.Coins >= o.Price, (int)(32 * s));
                var bt = (RectTransform)buy.transform;
                bt.anchorMin = bt.anchorMax = new Vector2(1, 0);
                bt.pivot = new Vector2(1, 0);
                bt.sizeDelta = new Vector2(210 * s, 84 * s);
                bt.anchoredPosition = new Vector2(-24 * s, 20 * s);
                UiShapes.Outline(bt, IsoArt.Ink, Theme.RadiusButton, 4);
                _shopButtons.Add(new KeyValuePair<Button, int>(buy, index));
                y += h + 12;
            }
            var rr = SliceCards.Reroll(root, s, "Reroll (" + reroll + ")", () => Run(new RunShopReroll()), true, 2086, "Button:Reroll (" + reroll + ")");
            ((RectTransform)rr.transform).anchoredPosition = new Vector2(300 * s, ((RectTransform)rr.transform).anchoredPosition.y);
            var leave = Ui.Button(root, "Leave", () => { modal.Close(); Run(new RunShopLeave()); }, Theme.Accent, 320 * s, 170 * s, true, (int)(58 * s));
            SliceKit.Place((RectTransform)leave.transform, s, 780, 2086, 320, 170, true);
            UiShapes.Outline((RectTransform)leave.transform, IsoArt.Ink, Theme.RadiusButton, 6);
            UiTween.PopIn(root, 0.15f);
            return modal;
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
                // the signature first: the run's identity, its awakening and how far the next one is
                var sig = run.SignatureId != null ? Content.Skill(run.SignatureId) : null;
                if (sig != null && ids.Remove(sig.Id))
                {
                    int slv = RunDraft.Level(run, sig.Id);
                    var scol = Theme.FamilyColor(sig.Family);
                    var card = Card(b, Theme.Tint(scol, 0.3f), 6);
                    card.name = "Signature:" + sig.Id;
                    UiShapes.Outline(card, SignaturePick.SignatureColor, 16, 4);
                    Ui.Label(card, "SIGNATURE · " + SkillText.FamilyName(sig.Family).ToUpperInvariant(), 20, SignaturePick.SignatureColor);
                    Ui.Label(card, SkillText.Title(Content, sig, slv), Theme.FontNormal, Color.Lerp(scol, Color.white, 0.6f));
                    Ui.Label(card, Fmt.SkillFull(SkillLeveling.At(Content, sig, slv)), Theme.FontSmall, Theme.Text);
                    int next = RunSystem.NextAwakeningAt(Content, run);
                    Ui.Label(card, next > 0 ? "Awakens at " + next + " " + SkillText.FamilyName(sig.Family) + " skills (have " + RunDraft.FamilyCount(Content, run, sig.Family) + ")." : "Fully awakened.",
                        20, Theme.TextDim);
                }
                foreach (var id in ids)
                {
                    var s = Content.Skill(id);
                    var col = RunPresentation.SkillColor(s);
                    var row = Ui.Row(b, 72, 12, Theme.Tint(col, 0.22f), 16, "Skill:" + id);
                    UiShapes.Outline(row, Theme.Shade(col, 0.9f), 16, 3);
                    Ui.Dot(row, col, 28, "Rarity");
                    int lv = RunDraft.Level(run, id);
                    Ui.Label(row, (s == null ? id : lv > 0 ? SkillText.Title(Content, s, lv) : s.Name) + " <size=20>(" + (s?.Rarity ?? Rarity.Common) + (s != null && s.Family != SkillFamily.None ? " · " + SkillText.FamilyName(s.Family) : "") + ")</size>", Theme.FontSmall, Color.Lerp(col, Color.white, 0.5f));
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
                var go = Ui.Button(body, caption, () => SignaturePick.StartRun(App, new RunStart { PortalId = target.PortalId, LevelId = target.Id, Mode = mode }, r => { if (r.Ok) App.Router.Replace(new RunScreen()); }),
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
