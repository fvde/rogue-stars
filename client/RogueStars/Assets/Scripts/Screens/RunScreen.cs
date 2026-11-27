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
    /// the speed chip, the skills book). Decision popups (branch on the board, event cards, skill draft, shop,
    /// revive, Burrow), battle playback and auto mode as before.
    /// Auto mode (hold the dice; a level unlock, Features.AutoRoll) rolls the dice and starts the fights it meets (a clock
    /// miniboss, the boss arena: RunEncounter): while it is on and nothing is open, it sends RunRoll / RunEncounter; a tap
    /// anywhere stops it. Everything else — every decision, result, chest, battle playback — waits for the player's tap
    /// (owner playtest 0.1.200); auto pauses meanwhile and resumes after it while still on (<see cref="AutoPaused"/>).
    /// The tutorial run (main level 1) shows coach marks: roll, pick a skill on level-up, beat the boss (CoachMarks).
    /// </summary>
    public sealed partial class RunScreen : UiScreen
    {
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
        private Text _coins;
        private int _outcomeSeen;
        private UiModal _outcome;
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
        private RectTransform _rollButton;
        private GameObject _coach;
        private string _coachMark;
        private bool _coachOff;
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
        /// <summary>Duration of the spinning chance indicator.</summary>
        public const float ChanceSpinSeconds = 1.3f;
        /// <summary>Board animation speed = THE play speed (PlaySpeed.Current: the speed chip, ×0.5 … ×3 for dice, hops, landings and battles).</summary>
        public static float BoardSpeed { get => PlaySpeed.Current; set => PlaySpeed.Current = value; }
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
        public const float StandDrop = 46;
        /// <summary>
        /// The dice stand is drawn at this share of its model frame (playtest: at full size its slab and column ran into the speed
        /// chip and the skills book and the cap reached up to the revive buttons): still the screen's big thumb target.
        /// </summary>
        public const float StandScale = 0.84f;
        /// <summary>Top of the dice cap from the bottom, in reference px (× <see cref="ControlsScale"/>).</summary>
        public static float ControlsTopRef => DiceStand.CapTop * StandScale - StandDrop;
        /// <summary>The open band between the HUD and the cap is at least this many world units tall (short screens widen the view).</summary>
        public const float MinBandUnits = 6.5f;
        /// <summary>Widest default view (world units) on the shortest screens (tablets).</summary>
        public const float MaxDefaultWidth = 14f;
        /// <summary>Pinch / wheel zoom: view width between these multiples of the default.</summary>
        public const float MinZoom = 0.8f, MaxZoom = 1.9f;
        /// <summary>Seconds of the eased pan and zoom back to the follow framing (a roll / a move).</summary>
        public const float ReturnSeconds = 0.4f;

        /// <summary>
        /// G1 board feedback: a floor title card sweeps across the board (T44), a looted cache puffs and shows an empty chest
        /// (T47), the boss icon on the track shakes and glows (T48).
        /// </summary>
        public override bool OnFeedback(GameEvent e)
        {
            if (_holder == null) return false;
            switch (e.Type)
            {
                case "run_burrow_descend":
                {
                    var banner = RevealBanner.Show(_holder, "FLOOR " + (e.Args.Count > 0 ? e.Args[0] : ""), new Color32(0x6A, 0x4A, 0x2A, 255), null, 0.6f, 170);
                    banner.name = "FloorCard";
                    var go = banner.gameObject;
                    UiTweens.Play(go, "life", 1.6f, null, () => Ui.DestroyGo(go));
                    LastBoardFeedback = e.Type;
                    return true;
                }
                case "run_cache_empty":
                    App.Feedback.FloatNumber(_holder, "Empty", Theme.TextDim);
                    LastBoardFeedback = e.Type;
                    return true;
                case "boss_awaken":
                {
                    RectTransform clock = null;
                    foreach (var rt in _holder.GetComponentsInChildren<RectTransform>(true)) if (rt.name == "ClockTrack") { clock = rt; break; }
                    if (clock == null) return false;
                    FeedbackFx.Shake(clock, 10f, 0.5f);
                    FeedbackFx.Flash(clock, Theme.Bad, 0.7f);
                    LastBoardFeedback = e.Type;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The last board feedback played (tests).</summary>
        public string LastBoardFeedback { get; private set; }

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
                return RollText(Content, run) + " · Boss " + Fmt.Whole(run.Awakening) + "% awake · Threat " + run.Threat
                       + (run.Mode != RunMode.Normal ? " · " + run.Mode : "") + (run.Mode == RunMode.Burrow ? " floor " + run.Floor : "");
            }
        }

        protected override string BackLabel => "II";
        protected override void OnBack() => PauseModal();
        /// <summary>No app header and no footer: the HUD floats over the world (reference 1).</summary>
        public override float HeaderHeight => 0;
        public override bool CoversResourceBar => true;
        /// <summary>Do not rebuild under a running animation (periodic refreshes would cut it short).</summary>
        public override bool AutoRebuild => !Animating;
        public bool Auto => _auto;
        /// <summary>Auto mode is on but waits for the player (anything but a roll is next, or a screen is open).</summary>
        public bool AutoPaused => _auto && State.Run.Active != null && WaitsForPlayer(State.Run.Active);
        public bool Animating => _anim != null && _anim.Busy;
        /// <summary>The board animations' speed now (the speed chip; 4 while a tap fast-forwards).</summary>
        private float AnimSpeed => _anim != null ? Mathf.Max(0.25f, _anim.Speed) : Mathf.Max(0.25f, BoardSpeed);
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
                Accent = def != null ? Theme.TileAccent(def) : Theme.Border,
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
                        case RunEffectKind.Loot: case RunEffectKind.PendingReward: Add(true, "REWARD CHEST (paid on clear)"); break;
                        case RunEffectKind.AwakenRatePct: Add(fx.Value < 0, fx.Value < 0 ? "Boss wakes slower" : "RISK boss wakes " + Fmt.Pct(fx.Value) + " faster"); break;
                        case RunEffectKind.Coins: Add(fx.Value >= 0, (fx.Value >= 0 ? "+" : "−") + Fmt.Num(Math.Abs(fx.Value)) + " coins"); break;
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
        public static string DiceCaption(ActiveRun run, bool auto, bool autoPaused, bool autoUnlocked = true)
        {
            if (run == null) return "";
            if (auto && !autoPaused) return "Auto…";
            switch (run.Phase)
            {
                case RunPhase.AwaitRoll: return autoUnlocked ? "Hold for Auto" : "Tap to roll"; // Auto Roll: a level unlock (GDD §16)
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
            _anim.Speed = Mathf.Max(0.25f, BoardSpeed);
            bool moving = path.Count > 1 && _world?.Token != null;
            if (moving) LastPopups.Clear();
            if (moving || rolled) ReturnCamera(); // a roll / a new move: the view eases back to the worm
            if (!(rolled && run.LastDice.Count > 0)) DropDiceStage(); // a stage of an earlier roll / a pre-roll without a roll
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
                    _anim.Call(() => _world?.FaceToken(from.x, to.x)); // the worm looks where it is going
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
                if (next && !RunFx.Skip) _anim.Wait(_auto ? 0.3f : PreDecisionSeconds);
            }
            else if (!battle) _anim.Call(() => AnimateHud(hudNow));
            _anim.Mark(SkipMark); // a tap on the dice finishes everything before this at once
            if (battle)
            {
                var result = run.LastBattle;
                var ctx = BattleContext();
                _anim.Call(() => App.Router.Push(new BattlePlaybackScreen(result, ctx)));
            }
            _anim.Call(() => ShowOutcomeOrDecision(run));
        }

        // ---- motion: dice, landing, popups, HUD -------------------------------------------------

        /// <summary>Tumbling dice over the board (two when the level rolls two), landing on the rolled values; the button's dice follow.</summary>
        private void RollDice(List<int> values)
        {
            var vals = new List<int>(values);
            float secs = RunFx.DiceSeconds(_auto);
            if (secs <= 0 || _holder == null) { DropDiceStage(); _footerDice?.Pose(1, PrintedDice(vals)); return; }
            // the dice already tumble since the tap (pre-roll): they land from where they are, the whole roll staying ~secs
            float t0 = 0;
            if (_preRolling && _diceView != null && _diceView.Dice.Count == vals.Count)
            {
                t0 = _preRollT;
                secs = Mathf.Max(PreRollLandSeconds, secs - _preRollWaited);
            }
            else { DropDiceStage(); MakeDiceStage(vals.Count); }
            _preRolling = false;
            var view = _diceView;
            var stage = _diceStage;
            var group = _diceGroup;
            _anim.Then(secs, t =>
            {
                float k = Mathf.Lerp(t0, 1, t);
                view.Pose(k, vals);
                _footerDice?.Pose(Mathf.Min(1, k * 1.05f), PrintedDice(vals));
            });
            // the dice rest for a beat, then fade while the worm starts hopping
            _anim.Then(_auto ? 0.18f : DiceRestSeconds, null, () =>
            {
                if (stage == null) return;
                UiTweens.Play(stage.gameObject, "fade", 0.3f / AnimSpeed, k => { if (group != null) group.alpha = 1 - k; }, () =>
                {
                    if (stage != null) UnityEngine.Object.Destroy(stage.gameObject);
                    if (_diceStage == stage) { _diceStage = null; _diceView = null; _diceGroup = null; }
                });
            });
        }

        // ---- the dice stage (over the board; under BodyFrame, so a rebuild on the server's answer keeps it) ----

        /// <summary>Manual pause after the landing before a fight / decision covers the board (×1 speed).</summary>
        public const float PreDecisionSeconds = 0.4f;
        /// <summary>The dice rest on their values before they fade (manual).</summary>
        public const float DiceRestSeconds = 0.2f;
        /// <summary>Shortest landing of dice that already tumbled while the roll was on its way.</summary>
        public const float PreRollLandSeconds = 0.45f;
        /// <summary>Waiting dice loop through the first bounce of the roll (pose time 0..this).</summary>
        public const float PreRollLoop = 0.26f;
        /// <summary>The animation mark a tap on the dice skips to (everything before it finishes at once).</summary>
        public const string SkipMark = "decide";

        private RectTransform _diceStage;
        private DiceView _diceView;
        private CanvasGroup _diceGroup;
        private bool _preRolling;
        private float _preRollT, _preRollWaited;

        /// <summary>The dice tumble from the tap on, before the roll's answer (tests).</summary>
        public bool PreRolling => _preRolling;

        private void MakeDiceStage(int count)
        {
            var parent = BodyFrame != null ? BodyFrame : _holder;
            var stage = Ui.Rect(parent, "DiceStage");
            Ui.Stretch(stage);
            var shade = Ui.Panel(stage, new Color(0, 0, 0, 0.25f), "Shade");
            shade.raycastTarget = false;
            Ui.Stretch(shade.rectTransform);
            float dieSize = count <= 2 ? 150 : Mathf.Max(70, 520f / count);
            var view = new DiceView(stage, count, dieSize);
            view.Root.sizeDelta = new Vector2(count * dieSize * 1.35f, dieSize * 1.4f);
            // B4: the dice pop above the worm's head (offset by its sprite height), never on top of it
            view.Root.anchorMin = view.Root.anchorMax = new Vector2(0, 1);
            view.Root.anchoredPosition = DiceAboveWorm(view.Root.sizeDelta);
            var group = stage.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            _diceStage = stage; _diceView = view; _diceGroup = group;
        }

        private void DropDiceStage()
        {
            if (_diceStage != null) Ui.DestroyGo(_diceStage.gameObject);
            _diceStage = null; _diceView = null; _diceGroup = null;
            _preRolling = false;
        }

        /// <summary>Tap on ROLL: the dice start tumbling now, then the roll goes out; its answer lands them (RollDice).</summary>
        private void RollNow(ActiveRun r)
        {
            if (!RunFx.Skip && _holder != null)
            {
                DropDiceStage();
                MakeDiceStage(Math.Max(1, r.LastDice.Count > 0 ? r.LastDice.Count : _footerDice != null ? _footerDice.Dice.Count : 1));
                _preRolling = true;
                _preRollT = 0; _preRollWaited = 0;
            }
            Run(new RunRoll(), res => { if (!res.Ok) DropDiceStage(); });
        }

        private void StepPreRoll(float dt)
        {
            if (!_preRolling || _diceView == null) return;
            _preRollWaited += dt;
            _preRollT = (_preRollT + dt / RunFx.DiceRollSeconds) % PreRollLoop;
            _diceView.Pose(_preRollT, _preRollFaces);
        }

        private static readonly List<int> _preRollFaces = new List<int> { 6, 6, 6, 6, 6, 6 };

        /// <summary>Centre of the rolling dice (top-left-anchored holder coordinates): above the worm's head, kept on screen.</summary>
        private Vector2 DiceAboveWorm(Vector2 size)
        {
            float w = _holderSize.x > 10 ? _holderSize.x : 1080, h = _holderSize.y > 10 ? _holderSize.y : 1920;
            if (_world == null) return new Vector2(w / 2, -h / 2 + 60);
            var head = Overlay(_tokenPos + new Vector2(0, WorldBoard.WormHeight + 0.3f));
            return DicePlacement(head, size, w, h);
        }

        /// <summary>The dice centre: just above <paramref name="head"/>, clamped inside the holder below the HUD strip (pure; tested).</summary>
        public static Vector2 DicePlacement(Vector2 head, Vector2 size, float w, float h)
        {
            float x = Mathf.Clamp(head.x, size.x / 2 + 20, w - size.x / 2 - 20);
            float y = head.y + size.y / 2 + 10;
            y = Mathf.Clamp(y, -h + size.y / 2 + 20, -h * 0.14f - size.y / 2);
            return new Vector2(x, y);
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

        /// <summary>D3: when the draft opens the HUD's Lv chip already shows the new level (with a pop), never the old one.</summary>
        private void BumpHudLevel(ActiveRun run)
        {
            if (_hud == null || run == null) return;
            var now = RunPresentation.Hud(Content, run);
            int shown = _hudShown?.Level ?? 0;
            if (_hudShown.HasValue && shown >= now.Level) return;
            _hudShown = now;
            ShowHud(now, now.Level, now.XpNeed > 0 ? (float)now.Xp / now.XpNeed : 0);
            var pill = _hud.Root != null ? _hud.Root.Find("RunLevel") as RectTransform : null;
            if (pill != null && Application.isPlaying)
                UiTweens.Play(pill.gameObject, "lvpop", 0.45f, k => pill.localScale = Vector3.one * (1 + 0.35f * Mathf.Sin(k * Mathf.PI)), () => { if (pill != null) pill.localScale = Vector3.one; });
        }

        /// <summary>The HUD as shown now (tests: "HP a / b | ATK n | DEF n | Lv n | XP x / y | SKILLS n").</summary>
        public string HudText => _hud?.Summary;

        /// <summary>What the battle playback shows in its top HUD and skill grid (the worm as the run HUD shows it).</summary>
        private BattleHudContext BattleContext()
        {
            var run = State?.Run.Active;
            var hud = _hudShown ?? (run != null ? RunPresentation.Hud(Content, run) : default(RunHudStats));
            return new BattleHudContext
            {
                // Q2: battles with no run behind them show the worm's account stats (no level badge / XP bar);
                // the run's own final battle is marked InRun again in BuildFinished
                Hud = hud, HasStats = run != null, InRun = run != null, Coins = run?.Coins ?? -1, SkillCount = run != null ? RunSystem.BuildSize(run) : _skillCount,
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
        /// default and the whole view inside the drawn tile window (ClampView), so the player never sees where it ends.
        /// </summary>
        public void MoveView(Vector2 from, Vector2 to, float zoom)
        {
            if (_world == null || float.IsNaN(_cam.x) || _holderSize.x <= 0) return;
            float baseW = DefaultViewWidth(_holderSize), width = CameraWidth;
            float newW = Mathf.Clamp(width / Mathf.Max(0.01f, zoom), baseW * MinZoom, baseW * MaxZoom);
            var anchor = WorldAt(from, _cam, width);
            float perUnit = _holderSize.x / newW;
            var centre = anchor - new Vector2((to.x - _holderSize.x / 2) / perUnit, -(to.y - _holderSize.y / 2) / perUnit);
            // bounds: the whole view stays inside the drawn tile window (only tiles near the worm are drawn), so the
            // player never sees where the drawn board ends; zooming out stops where the view would outgrow it
            var follow = CameraTarget();
            _cam = ClampView(_world.PathBounds, follow, centre, ref newW, _holderSize.y / _holderSize.x, baseW);
            _camWidth = newW;
            _camManual = true;
            _camReturn = -1;
            ApplyCamera();
        }

        /// <summary>Inset (world units) of the drawn path's vertical extent the view must stay inside.</summary>
        public const float ViewInset = 0.5f;

        /// <summary>
        /// Clamps a looked-around view (centre, width). Only tiles within a depth window around the worm are drawn, and on the
        /// iso board depth runs up/down the screen: so the view's top and bottom edges stay inside the drawn path's vertical
        /// extent (inset by <see cref="ViewInset"/>) and zooming out stops where the view would get taller than it (never
        /// below the default <paramref name="baseW"/>). Sideways the island and its dark surround frame the board, so the
        /// centre only stays within the path plus a margin. The follow framing is always allowed (pure; tested).
        /// </summary>
        public static Vector2 ClampView(Rect drawn, Vector2 follow, Vector2 centre, ref float width, float aspect, float baseW)
        {
            aspect = Mathf.Max(0.01f, aspect);
            float yLo = Mathf.Min(drawn.yMin + ViewInset, follow.y - baseW * aspect / 2);
            float yHi = Mathf.Max(drawn.yMax - ViewInset, follow.y + baseW * aspect / 2);
            width = Mathf.Min(width, Mathf.Max(baseW, (yHi - yLo) / aspect));
            float hh = width * aspect / 2;
            float y = yHi - yLo >= 2 * hh ? Mathf.Clamp(centre.y, yLo + hh, yHi - hh) : follow.y;
            float x = Mathf.Clamp(centre.x, Mathf.Min(drawn.xMin, follow.x) - 1.5f, Mathf.Max(drawn.xMax, follow.x) + 1.5f);
            return new Vector2(x, y);
        }

        // ---- HUD ----------------------------------------------------------------------------------

        /// <summary>The top HUD (level, HP, ATK, DEF, cog, coins) and the boss rail over the board.</summary>
        private void Hud(ActiveRun run, RunHudStats shown)
        {
            _skillCount = RunSystem.BuildSize(run);
            _hud = SliceHud.Build(_holder, _holderSize.x, PauseModal, () => App.Feedback.Tip("Run level " + _hud.Level.text + " · " + _hud.XpText, _hud.Level.rectTransform, true), true); // T50
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
                var life = Root.gameObject.GetComponent<WorldLifetime>(); if (life == null) life = Root.gameObject.AddComponent<WorldLifetime>();
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

            // fork (run UI glitches #10): a floating arrow bobs over the tile each option lands on, tinted like its "Go left" /
            // "Go right" button in the controls; the landing tile and the option tile stay tap targets (A / B)
            if (run.Phase == RunPhase.ChooseBranch)
                for (int i = 0; i < run.BranchOptions.Count; i++)
                {
                    int index = i;
                    string letter = i == 0 ? "A" : "B";
                    var col = BranchColor(i);
                    int landId = BranchLanding(run, i, Content);
                    if (landId < 0) continue;
                    float s = frameW / 1080f;
                    var pick = Ui.Button(_overlay, "", () => PickBranch(index), new Color(1, 1, 1, 0.001f), 190 * s, 150 * s, true, 20);
                    pick.name = "BranchPick:" + letter;
                    var prt = (RectTransform)pick.transform;
                    prt.anchorMin = prt.anchorMax = new Vector2(0, 1);
                    prt.pivot = new Vector2(0.5f, 0.5f);
                    prt.sizeDelta = new Vector2(190 * s, 150 * s);
                    Pin(prt, WorldBoard.TopOf(run.Tiles[landId]) + new Vector2(0, 0.2f));
                    var arrow = Ui.Panel(prt, Color.white, "BranchArrow");
                    arrow.sprite = ForkArrow(col);
                    arrow.preserveAspect = true;
                    arrow.raycastTarget = false;
                    var art = arrow.rectTransform;
                    art.anchorMin = art.anchorMax = new Vector2(0.5f, 1); art.pivot = new Vector2(0.5f, 0);
                    art.sizeDelta = new Vector2(120 * s, 150 * s); art.anchoredPosition = new Vector2(0, 10 * s);
                    UiBob.On(art, 16 * s, 0.9f);
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

        private static readonly Dictionary<Color, Sprite> ForkArrows = new Dictionary<Color, Sprite>();

        /// <summary>The fork's floating arrow (#10): a chunky down arrow in the branch colour with the ink outline (drawn).</summary>
        public static Sprite ForkArrow(Color col)
        {
            if (ForkArrows.TryGetValue(col, out var sp) && sp != null) return sp;
            const int w = 160, h = 200;
            var p = new IsoPaint(w, h);
            var pts = new List<Vector2> { IsoPaint.V(52, 196), IsoPaint.V(108, 196), IsoPaint.V(108, 96), IsoPaint.V(150, 96), IsoPaint.V(80, 8), IsoPaint.V(10, 96), IsoPaint.V(52, 96) };
            p.Shape(pts, IsoPaint.VGrad(IsoPaint.Shade(col, -0.25f), IsoPaint.Shade(col, 0.25f), 8, 196), IsoArt.Ink, 9);
            p.Line(IsoPaint.V(66, 184), IsoPaint.V(66, 108), 7, new Color(1, 1, 1, 0.45f));
            sp = p.ToSprite("rs_fork_arrow", IsoPaint.V(w / 2f, 0));
            ForkArrows[col] = sp;
            return sp;
        }

        /// <summary>
        /// The fork's two options on screen (#10): the option whose landing tile is further left is "Go left" (index into
        /// ActiveRun.BranchOptions), the other "Go right"; -1 when there is no fork.
        /// </summary>
        public static void ForkSides(ActiveRun run, ContentDb content, out int left, out int right)
        {
            left = right = -1;
            if (run == null || run.Phase != RunPhase.ChooseBranch || run.BranchOptions.Count < 2) return;
            int a = BranchLanding(run, 0, content), b = BranchLanding(run, 1, content);
            float ax = a >= 0 ? WorldBoard.TopOf(run.Tiles[a]).x : 0, bx = b >= 0 ? WorldBoard.TopOf(run.Tiles[b]).x : 1;
            left = ax <= bx ? 0 : 1;
            right = 1 - left;
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
        /// bottom-left, the skills book bottom-right.
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
            float k = StandScale, standW = DiceStand.FrameW * k, standH = DiceStand.FrameH * k;
            float standY = 2400 + StandDrop - standH / 2f;
            var pedestal = Ui.Panel(root, Color.white, "Pedestal");
            pedestal.sprite = HudArt.Pedestal();
            pedestal.raycastTarget = false;
            SliceKit.Place(pedestal.rectTransform, s, 540, standY, standW, standH, true);

            bool encounter = run.Phase == RunPhase.Encounter;
            bool canRoll = run.Phase == RunPhase.AwaitRoll || encounter;
            bool deciding = !canRoll && run.Phase != RunPhase.Completed && run.Phase != RunPhase.Failed;
            bool live = canRoll || _auto || deciding;
            var btnRt = Ui.Rect(root, "RollButton");
            _rollButton = btnRt;
            SliceKit.Place(btnRt, s, 540, standY, standW, standH, true);
            // the cap pulses / presses about its base on the slab
            var face = Ui.Panel(btnRt, Color.white, "Cap");
            face.sprite = HudArt.DiceButton(live);
            var frt = face.rectTransform;
            frt.anchorMin = frt.anchorMax = frt.pivot = DiceStand.Base01;
            frt.sizeDelta = new Vector2(standW * s, standH * s);
            frt.anchoredPosition = Vector2.zero;
            var btn = btnRt.gameObject.AddComponent<Button>();
            btn.targetGraphic = face;
            btn.transition = Selectable.Transition.ColorTint;
            btn.interactable = live;
            // two dice printed on the cap's top (white on red, laid into the top plane: the plane squashes by the camera's
            // pitch, the dice turn a little in it); after a roll they show the last roll's dice, which the tumbling dice land on
            var shown = PrintedDice(run.LastDice.Count > 0 ? run.LastDice : new List<int> { 3, 5 });
            int diceN = shown.Count;
            float dieSize = (diceN == 1 ? 150 : diceN == 2 ? 146 : Mathf.Max(70, 300f / diceN)) * k * s;
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
            var caption = Ui.Display(face.transform, DiceCaption(run, _auto, AutoPaused, Session.IsUnlocked(Features.AutoRoll)), (int)(Mathf.Min(50, DiceStand.FrontWallHeight * 0.62f) * k * s), live ? (Color)new Color32(0x9A, 0x1C, 0x26, 255) : (Color)new Color32(0x4E, 0x48, 0x5A, 255), TextAnchor.MiddleCenter, "Label");
            caption.raycastTarget = false;
            caption.horizontalOverflow = HorizontalWrapMode.Overflow;
            var lip = caption.gameObject.AddComponent<Shadow>();
            lip.effectColor = new Color(1, 0.72f, 0.66f, 0.75f);
            lip.effectDistance = new Vector2(1.5f, -3f);
            caption.rectTransform.anchorMin = caption.rectTransform.anchorMax = DiceStand.FrontWall01;
            caption.rectTransform.sizeDelta = new Vector2(420 * k * s, 70 * k * s);
            _diceAnchor = btnRt;
            var hold = btnRt.gameObject.AddComponent<HoldButton>();
            hold.OnTap = () =>
            {
                if (_auto && !AutoPaused) StopAuto();
                else if (Animating) { if (!_anim.FlushUntil(SkipMark)) _anim.Speed = 4; } // tap while the dice roll / the worm hops: there at once
                else if (_preRolling || Session.InFlight > 0) { } // the roll is on its way: a second tap does nothing
                else
                {
                    var r = State.Run.Active;
                    if (r != null && r.Phase == RunPhase.AwaitRoll) RollNow(r);
                    else if (r != null && r.Phase == RunPhase.Encounter) Run(new RunEncounter());
                    else if (r != null && r.Phase == RunPhase.ChooseBranch) { ReturnCamera(); Rebuild(); } // the camera eases back to the fork
                    else if (r != null) { _decisionKey = null; ShowOutcomeOrDecision(r); }
                }
            };
            hold.OnHold = HoldDice; // Auto Roll unlocks with a level (GDD §16); before it a hold says when
            if (live && !_auto && canRoll) UiPulse.On(face, 0.025f, 1.6f, 1f);

            // the fork (#10): "Go left" / "Go right" just above the dice stand, each tinted like its arrow on the board
            ForkSides(run, Content, out int goLeft, out int goRight);
            if (goLeft >= 0)
                foreach (var (index, text, x) in new[] { (goLeft, "Go left", 300f), (goRight, "Go right", 780f) })
                {
                    int ix = index;
                    var col = BranchColor(ix);
                    var go = Ui.Button(root, "", () => PickBranch(ix), col, 400 * s, 140 * s, true, 20);
                    go.name = "BranchGo:" + (ix == 0 ? "A" : "B");
                    SliceKit.Place((RectTransform)go.transform, s, x, 2400 - ControlsTopRef - 100, 400, 140, true); // just above the dice cap
                    UiShapes.Outline((RectTransform)go.transform, SliceKit.Ink, Theme.RadiusButton, 6);
                    var gt = SliceKit.Chunky(go.transform, text, (int)(60 * s), Color.white, TextAnchor.MiddleCenter, "GoText");
                    Ui.Stretch(gt.rectTransform, 0, 0, 0, 8 * s);
                }

            // speed chip: THE play speed (board and battles, ×0.5 / ×1 / ×2 / ×3 with the pass), one button cycling through them
            BoardSpeed = PlaySpeed.Allowed(ShopSystem.HasBattleSpeed3(State, Content, Now));
            PlaySpeed.BuildChip(root, s, 142, 2284, 214, 112, CycleSpeed, BoardSpeed);

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
            _boardBook = new[] { book.gameObject, bl.gameObject };
        }

        /// <summary>The board's skills book and its label: hidden while a decision / result covers the board (each has its own book).</summary>
        private GameObject[] _boardBook;

        private void SyncBoardBook()
        {
            if (_boardBook == null) return;
            bool covered = (_decision != null && _decision.IsOpen) || OutcomeOpen;
            foreach (var go in _boardBook) if (go != null && go.activeSelf == covered) go.SetActive(!covered);
        }

        /// <summary>The speed chip: the shared play speed (board and battles) ×0.5 → ×1 → ×2 → ×3 (with the pass) → ×0.5.</summary>
        public void CycleSpeed()
        {
            PlaySpeed.Cycle(ShopSystem.HasBattleSpeed3(State, Content, Now));
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
                // the run's final battle (boss kill or defeat) is still a run battle: the server settled the run
                // before playback, but the HUD keeps the run's level, XP and stats as last shown
                if (_hudShown.HasValue) { ctx.InRun = true; ctx.HasStats = true; }
                _anim.Call(() => App.Router.Push(new BattlePlaybackScreen(battle, ctx)));
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
            StepPreRoll(dt);
            SyncBoardBook();
            if (_toResult && !Animating && App.Router.Current == this)
            {
                _toResult = false;
                App.Router.Replace(new RunResultScreen());
                return;
            }
            UpdateCoach();
            if (!_auto) return;
            var run = State.Run.Active;
            if (run == null) { StopAuto(); return; }
            if (WaitsForPlayer(run))
            {
                // paused: a decision, result, chest, playback or encounter waits for the player's tap. No tap-to-stop
                // overlay (that screen must be usable); auto rolls again once the player closed it, while still on.
                RemoveAutoOverlay();
                _autoTimer = 0;
                return;
            }
            EnsureAutoOverlay();
            if (Animating || Session.InFlight > 0) { _autoTimer = 0; return; }
            _autoTimer += dt;
            if (_autoTimer < 0.6f) return;
            _autoTimer = 0;
            // Auto starts the fights it meets too (owner: "actually start battle automatically"): a miniboss of the clock
            // and the boss arena. Manual play keeps the Fight! / Enter Arena tap; every decision still waits for the player.
            if (run.Phase == RunPhase.Encounter) Run(new RunEncounter(), r => { if (!r.Ok) StopAuto(); });
            else Run(new RunRoll(), r => { if (!r.Ok) StopAuto(); });
        }

        /// <summary>
        /// Auto mode rolls and starts the fights it meets (a clock miniboss, the boss arena); it waits while anything else
        /// is next (a decision, a revive…), or a screen is open over the board (a result sheet, a decision sheet, a battle
        /// playback, a chest).
        /// </summary>
        private bool WaitsForPlayer(ActiveRun run) =>
            run.Phase != RunPhase.AwaitRoll && run.Phase != RunPhase.Encounter || OutcomeOpen || _decision != null && _decision.IsOpen
            || App.Router.Current != this;

        // ---- tutorial coach marks (level 1) -----------------------------------------------------------------------

        /// <summary>The coach mark showing now (CoachMarks.Roll / Pick / Boss), or null.</summary>
        public string CoachMark => _coach != null ? _coachMark : null;

        /// <summary>"Skip tips": no more coach marks in this run.</summary>
        public void SkipCoach() { _coachOff = true; HideCoach(); }

        private void HideCoach()
        {
            if (_coach != null) Ui.DestroyGo(_coach);
            _coach = null;
            _coachMark = null;
        }

        /// <summary>Every frame: the mark for the tutorial run's state (roll, pick a skill, beat the boss), never during animations.</summary>
        public void UpdateCoach()
        {
            var run = State?.Run.Active;
            bool decision = _decision != null && _decision.IsOpen;
            string mark = _coachOff || App.Router.Current != this || OutcomeOpen ? null
                : CoachMarks.For(State, Content, decision, Animating || Session.InFlight > 0 || _auto && !AutoPaused);
            if (mark == _coachMark && (_coach != null) == (mark != null)) return;
            HideCoach();
            if (mark == null || run == null) return;
            RectTransform target = (_rollButton != null ? _rollButton.Find("Cap/Label") as RectTransform : null) ?? _rollButton; // the dice: the hand at the caption's corner
            if (mark == CoachMarks.Pick)
                foreach (var t in App.ModalLayer.GetComponentsInChildren<Button>(true))
                    if (t.name.StartsWith("Button:Pick")) { target = (RectTransform)t.transform; break; }
            _coach = CoachMarks.Show(App.CoachLayer, target, mark, SkipCoach);
            _coachMark = mark;
        }

        private RectTransform _diceAnchor;

        /// <summary>
        /// A hold on the dice: auto mode once Auto Roll is unlocked; before that a short hint says when it unlocks
        /// (owner playtest 0.1.224: a silent hold felt broken).
        /// </summary>
        public void HoldDice()
        {
            if (Session.IsUnlocked(Features.AutoRoll)) StartAuto();
            else App.Feedback.Tip(AutoRollHint(Content), _diceAnchor); // anchored above the dice
        }

        /// <summary>"Auto Roll unlocks at level 7" (the feature's unlock level from content).</summary>
        public static string AutoRollHint(ContentDb content)
        {
            var f = content?.Feature(Features.AutoRoll);
            return "Auto Roll unlocks at level " + (f != null ? Features.UnlockLevel(f) : 7);
        }

        public void StartAuto()
        {
            if (_auto) return;
            _auto = true;
            _autoTimer = 0;
            Rebuild();
        }

        public void StopAuto()
        {
            if (!_auto) return;
            _auto = false;
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
            HideCoach();
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
        /// What makes an open decision UI stale. A purchase or a reroll in the shop does not: the shop sheet stays open and
        /// updates in place (<see cref="RefreshShop"/>), so buying 2–4 perks per visit or rerolling never closes and reopens
        /// it (owner playtest 0.1.224: a reroll rebuilt the whole sheet — a flash).
        /// </summary>
        public static string DecisionKey(ActiveRun r) =>
            r.RunId + "|" + r.Phase + "|" + r.Rolls + "|" + r.EventId + "|" + string.Join(",", r.DraftOffer) + "|" + r.DraftRerolls + "|" + r.MythicDraft
            + (r.Phase == RunPhase.Shop ? "" : "|" + r.ShopRerolls + "|" + r.Coins);

        /// <summary>The skill level-up rule (exact numbers from RunTuning; a level-up improves the skill's own numbers).</summary>
        public static string LevelUpText(ContentDb c) => RogueStars.Rules.Core.SkillText.LevelUpRule(c);

        private readonly List<KeyValuePair<Button, int>> _shopButtons = new List<KeyValuePair<Button, int>>();
        private Text _shopCoins;

        /// <summary>
        /// Updates the open shop after a purchase or a reroll: sold offers, affordability, coins, the reroll price; a reroll
        /// flips the new offers in place (<see cref="ReplaceShopOffers"/>). Never closes and reopens the sheet.
        /// </summary>
        private void RefreshShop(bool rerolled = false)
        {
            var run = State.Run.Active;
            if (run == null || run.Phase != RunPhase.Shop) return;
            if (rerolled) ReplaceShopOffers(run);
            if (_shopReroll != null)
            {
                long price = ShopRerollPrice(run);
                _shopReroll.name = "Button:Reroll (" + price + ")";
                _shopReroll.interactable = run.Coins >= price;
                var label = _shopReroll.transform.Find("RerollPrice")?.GetComponent<Text>();
                if (label != null) label.text = price > 0 ? Fmt.Num(price) : "FREE";
            }
            if (_shopCoins != null) _shopCoins.text = ShopBoughtLine(run);
            if (_shopHud != null) { var st = RunPresentation.Hud(Content, run); _shopHud.Show(st, st.Level, st.XpNeed > 0 ? (float)st.Xp / st.XpNeed : 0, RunSystem.BuildSize(run)); }
            if (_shopHudCoins != null) _shopHudCoins.text = Fmt.Num(run.Coins);
            foreach (var kv in _shopButtons)
            {
                if (kv.Key == null || kv.Value >= run.Shop.Count) continue;
                var o = run.Shop[kv.Value];
                kv.Key.interactable = !o.Sold && run.Coins >= o.Price;
                ShopPriceLabel(kv.Key, o);
            }
        }

        private void ShowDecision(ActiveRun stale)
        {
            if (Root == null || App.Router.Current != this) return;
            if (_outcome != null && _outcome.IsOpen) return; // the outcome result first; its Continue opens the decision
            var run = State.Run.Active;
            if (run == null || run.RunId != stale.RunId) return;
            string key = DecisionKey(run);
            if (key == _decisionKey && _decision != null && _decision.IsOpen)
            {
                // the open shop follows every state change in place (HUD stats, coins, prices), not only its own buy callback
                if (run.Phase == RunPhase.Shop) RefreshShop();
                return;
            }
            CloseDecision();
            _decisionKey = key;
            switch (run.Phase)
            {
                case RunPhase.ChooseBranch: _decision = null; break; // picked on the board (A / B landing tiles)
                case RunPhase.Event: _decision = EventModal(run); break;
                case RunPhase.Draft: _decision = run.MythicDraft && ShowChestIntro ? ChestIntro("chest_mythic", "You found a Chest!", () => _decision = DraftModal(run)) : DraftModal(run); break;
                case RunPhase.Shop: _decision = ShopModal(run); break;
                case RunPhase.Defeated: _decision = ReviveModal(run); break;
                case RunPhase.BurrowChoice: _decision = BurrowModal(run); break;
                case RunPhase.Minigame: _decision = Minigames.MinigameHost.Open(App.ModalLayer, Session); break;
                case RunPhase.Quest: _decision = QuestModal(run); break;
                case RunPhase.Signature: _decision = SignatureBoardPick(run); break;
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
            source == "treasure" ? "CHEST" : source == "campfire" ? "CAMPFIRE" : source == "shop" ? "SHOP" : source == "minigame" ? "MINI-GAME" : source == "quest" ? "STORY" : "EVENT";

        /// <summary>One result line as text, e.g. "+30 coins", "−5% max HP", "New skill: Ember Kindle" (pure; tested).</summary>
        public static string OutcomeLineText(OutcomeLine l, ContentDb content)
        {
            string sign = l.Amount >= 0 ? "+" : "−";
            double a = Math.Abs(l.Amount);
            switch (l.Kind)
            {
                case OutcomeKind.Coins: return sign + Fmt.Num(a) + " coins";
                case OutcomeKind.Hp: return sign + Fmt.Num(a) + " HP";
                case OutcomeKind.Stat:
                    return sign + Fmt.Pct(a) + " " + (l.Id == "Hp" ? "max HP" : l.Id == "Atk" ? "ATK" : l.Id == "Def" ? "DEF" : l.Id) + " for the run";
                case OutcomeKind.SkillGained: return "New skill: " + (content.Skill(l.Id)?.Name ?? l.Id);
                case OutcomeKind.SkillLost: return "Lost skill: " + (content.Skill(l.Id)?.Name ?? l.Id);
                case OutcomeKind.SkillLevel: return (content.Skill(l.Id)?.Name ?? l.Id) + " → level " + Fmt.Whole(a);
                case OutcomeKind.Xp: return "+" + Fmt.Num(a) + " XP";
                case OutcomeKind.LevelUp: return "Level up! +" + Fmt.Whole(a) + " skill draft" + (a > 1 ? "s" : "");
                case OutcomeKind.Shards: return sign + Fmt.Num(a) + " portal shard" + (a == 1 ? "" : "s");
                case OutcomeKind.AccountLoot:
                    return "+" + (l.Reward != null ? Fmt.Reward(l.Reward, content) : l.Id) + " — reward chest, paid when you beat the boss";
                case OutcomeKind.Awakening: return "Boss wakes " + Fmt.Whole(a) + "% " + (l.Amount >= 0 ? "sooner" : "later");
                case OutcomeKind.AwakenRate: return "Boss awakens " + Fmt.Pct(a) + (l.Amount >= 0 ? " faster" : " slower") + " for the rest of the run";
                case OutcomeKind.BossStat: return "Boss " + sign + Fmt.Pct(a) + " stronger";
                case OutcomeKind.Danger: return "Danger " + sign + Fmt.Whole(a);
                case OutcomeKind.Battle: return l.Id == "won" ? "Fight won" : "Fight lost";
                case OutcomeKind.SkillRoll: return "Skill roll: pick 1 of " + Fmt.Whole(a) + " (" + l.Id + " odds)";
                case OutcomeKind.CoinBonus: return "+" + Fmt.Num(a) + " coins from " + (l.Id ?? "your quest bonus");
                case OutcomeKind.QuestBoon: return (l.Id ?? "Quest bonus") + ": +" + Fmt.Num(a) + " coins on every coin gain, forever";
                case OutcomeKind.QuestStep:
                    return l.Id == "complete" ? "The story is complete!" : l.Id == "waits" ? "The story waits at chapter " + Fmt.Whole(a) : "The story moves on: chapter " + Fmt.Whole(a);
                default: return "Nothing happened.";
            }
        }

        /// <summary>The id of a result line's icon (HUD icons: heart, sword, shield, coin, exp, skull; "chest" for loot), or null = a glyph (pure; tested).</summary>
        public static string OutcomeIconId(OutcomeLine l)
        {
            switch (l.Kind)
            {
                case OutcomeKind.Coins: case OutcomeKind.CoinBonus: case OutcomeKind.QuestBoon: return "coin";
                case OutcomeKind.Hp: return "heart";
                case OutcomeKind.Stat: return l.Id == "Atk" ? "sword" : l.Id == "Def" ? "shield" : l.Id == "Hp" ? "heart" : null;
                case OutcomeKind.Xp: case OutcomeKind.LevelUp: return "exp";
                case OutcomeKind.Battle: return "sword";
                case OutcomeKind.AccountLoot: return "chest";
                case OutcomeKind.Awakening: case OutcomeKind.AwakenRate: case OutcomeKind.BossStat: return "skull";
                default: return null;
            }
        }

        private static Sprite OutcomeSprite(OutcomeLine l)
        {
            var id = OutcomeIconId(l);
            if (id == null) return null;
            return id == "chest" ? ArtCatalog.SliceMarker("chest") ?? ScreenArt.Prop("chest_common") : HudArt.Icon(id);
        }

        /// <summary>Icon glyph and colour of a result line.</summary>
        private static KeyValuePair<string, Color> OutcomeIcon(OutcomeLine l, ContentDb content)
        {
            switch (l.Kind)
            {
                case OutcomeKind.Coins: case OutcomeKind.CoinBonus: case OutcomeKind.QuestBoon: return new KeyValuePair<string, Color>("C", Theme.Gold);
                case OutcomeKind.QuestStep: return new KeyValuePair<string, Color>("?!", l.Good ? Theme.Good : Theme.TextFaint);
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
            bool mythic = run.MythicDraft;
            BumpHudLevel(run);
            float s = ModalScale();
            var root = SliceCards.Overlay(App.ModalLayer, mythic ? "Mythic choice" : "Choose a skill", out var modal);
            Canvas.ForceUpdateCanvases();
            // D1: the cards fit above the reroll button on any aspect (16:9 moves the header up / shrinks the cards)
            var lay = SliceCards.Layout(SliceCards.RefHeight(root, s), run.DraftOffer.Count, SliceCards.RerollReserve);
            SliceCards.Header(root, s, mythic ? SliceCards.RedRibbon : SliceCards.GoldRibbon, mythic ? "MYTHIC!" : "LEVEL UP!", mythic ? "MythicTitle" : "Title",
                mythic ? "Miniboss defeated! Take a Mythic skill" : "Select a Skill to learn!", lay.HeroY, lay.RibbonY);
            if (mythic) UiPulse.On(root.Find("Ribbon").GetComponent<Image>(), 0.03f, 1.4f, 1f);
            float step = lay.Step, y = lay.FirstY;
            foreach (var id in run.DraftOffer)
            {
                string sid = id;
                var sk = Content.Skill(sid);
                var fam = sk?.Family ?? SkillFamily.None;
                var rarity = sk?.Rarity ?? Rarity.Common;
                int ownedLevel = RunDraft.Level(run, sid);
                string full = Fmt.SkillCard(Content, sk, ownedLevel);
                if (sk?.UpgradesFrom != null && ownedLevel == 0) full += " Upgrades " + (Content.Skill(sk.UpgradesFrom)?.Name ?? sk.UpgradesFrom) + ".";
                bool signature = sk != null && sk.Signature;
                if (signature) full = SignaturePick.Body(Content, sk);
                var spec = new SkillCardSpec
                {
                    Id = sid, Tier = signature ? "Signature" : RunTuning.RunTier(rarity).ToString(), TierColor = signature ? SignaturePick.SignatureColor : RunPresentation.SkillColor(sk), Family = fam,
                    IconArt = ArtCatalog.SkillIcon(sid),
                    Title = ownedLevel > 0 && sk != null ? SkillText.Title(sk, ownedLevel + 1) : sk?.Name ?? sid,
                    Body = full, Tip = OfferTip(sid, ownedLevel, signature), TipContent = Content,
                };
                if (signature)
                {
                    // a miniboss offers a new signature in a Mythic's place (skill redesign: several signatures per run)
                    int have = RunDraft.FamilyCount(Content, run, fam);
                    spec.Chip = "NEW SIGNATURE" + (have >= Content.RunTuning.SignatureAwakenAt[0] ? " · AWAKENS NOW" : "");
                    spec.ChipName = "SignatureChip"; spec.ChipColor = SignaturePick.SignatureColor;
                }
                // C3: an owned skill reads "Name Lv 2" with the upgrade inline (no "LEVEL 1 → 2" chip); D4: no synergy tag
                else if (ownedLevel == 0 && sk?.UpgradesFrom != null) { spec.Chip = "UPGRADE"; spec.ChipColor = new Color32(0xD6, 0xC2, 0xFF, 255); }
                string info = (sk?.Name ?? sid) + " (" + rarity + (fam != SkillFamily.None ? " · " + SkillText.FamilyName(fam) : "") + "): " + SkillText.StripMarks(full) + (ownedLevel > 0 ? " " + LevelUpText(Content) : "");
                SliceCards.Card(root, s, y, spec, () => { modal.Close(); CloseDecision(); Run(new RunDraftPick { SkillId = sid }); }, () => App.Feedback.Tip(info), "Button:Pick", lay.CardH);
                y += step;
            }
            SliceCards.Reroll(root, s, "Reroll (" + run.DraftRerolls + ")", () => Run(new RunDraftReroll()), run.DraftRerolls > 0);
            DecisionSkillsBook(root, s, 975, 2262, true); // the run's skills while picking (opens on top, the offer stays)
            UiTween.PopIn(root, 0.15f);
            return modal;
        }

        /// <summary>The tooltip of an offered skill: a new one at Lv 1, an owned one at its next level with the upgrade line.</summary>
        public static SkillTip OfferTip(string skillId, int ownedLevel, bool signature = false) => new SkillTip
        {
            SkillId = skillId, Level = ownedLevel > 0 ? ownedLevel + 1 : 1,
            UpgradeFrom = ownedLevel, UpgradeTo = ownedLevel > 0 ? ownedLevel + 1 : 0, TierOverride = signature ? "Signature" : null,
        };

        /// <summary>
        /// The run-start signature pick on the run board (run UI glitches #24): the board stays visible behind a light dim, the
        /// violet ribbon and one card per offered signature (ActiveRun.StartSignatures); a pick sends RunPickSignature and the
        /// first ROLL follows. Named "Modal:Choose a signature"; cards "Button:Signature:&lt;id&gt;".
        /// </summary>
        private UiModal SignatureBoardPick(ActiveRun run)
        {
            float s = ModalScale();
            var root = SliceCards.Overlay(App.ModalLayer, "Choose a signature", out var modal, 0.62f);
            Canvas.ForceUpdateCanvases();
            int n = Math.Max(1, run.StartSignatures.Count);
            // the hero stays on the board (no big hero at the top): the ribbon sits under the HUD, the cards under it
            var lay = SliceCards.Layout(SliceCards.RefHeight(root, s), n, SliceCards.RerollReserve - 170);
            float ribbonY = 470;
            SliceCards.Header(root, s, SignaturePick.VioletRibbon, "CHOOSE YOUR SIGNATURE", "Title", "It gives this run its identity!", 0, ribbonY);
            var title = root.Find("Ribbon/Title")?.GetComponent<Text>();
            if (title != null) title.fontSize = (int)(62 * s);
            float h = Mathf.Min(300, lay.CardH + 20), step = h + 18, y = ribbonY + 160 + h / 2;
            foreach (var id in run.StartSignatures)
            {
                string sid = id;
                var sk = Content.Skill(sid);
                if (sk == null) continue;
                SliceCards.Card(root, s, y, SignaturePick.Spec(Content, sk), () => { modal.Close(); CloseDecision(); Run(new RunPickSignature { SignatureId = sid }); }, null, "Button:Signature:" + sid, h);
                y += step;
            }
            UiTween.PopIn(root, 0.15f);
            return modal;
        }

        /// <summary>
        /// The revive (run UI glitches #19) in the Slice style of the outcome sheet: the dimmed board, the red "DEFEATED!" ribbon,
        /// a tombstone as the centrepiece (<see cref="ArtCatalog.RunIcon"/> "tombstone", drawn until the art exists), the rule on
        /// the paper and chunky buttons: Ad revive (green), Diamonds (cyan), Give up (red). Named "Modal:Defeated!"; buttons
        /// "Button:Ad revive (n)", "Button:n Diamonds", "Button:Give up".
        /// </summary>
        private UiModal ReviveModal(ActiveRun run)
        {
            long cost = Content.RunTuning.ReviveDiamondCost;
            int adsLeft = ShopSystem.AdViewsLeft(State, Content, RunSystem.ReviveAdPlacement, Now);
            var root = SliceCards.Overlay(App.ModalLayer, "Defeated!", out var modal, 0.88f);
            Canvas.ForceUpdateCanvases();
            var frame = RunScenes.Frame(root, out float s);
            var cap = SliceKit.Chunky(frame, "BATTLE LOST", (int)(44 * s), new Color32(0xFF, 0x9A, 0x8E, 255), TextAnchor.MiddleCenter, "Caption");
            RunScenes.At(cap.rectTransform, s, 540, 290, 1000, 60);
            var rib = Ui.Panel(frame, Color.white, "Ribbon");
            rib.sprite = SliceCards.RedRibbon; rib.preserveAspect = true; rib.raycastTarget = false;
            RunScenes.At(rib.rectTransform, s, 540, 410, 1010, 172);
            var title = SliceKit.Chunky(rib.transform, "DEFEATED!", (int)(80 * s), Color.white, TextAnchor.MiddleCenter, "ReviveTitle");
            Ui.Stretch(title.rectTransform, 40 * s, 0, 40 * s, 14 * s);
            // the tombstone in a cold glow
            var glow = Ui.Panel(frame, new Color(0.55f, 0.6f, 0.95f, 0.35f), "TombGlow");
            glow.sprite = ScreenArt.Glow(); glow.raycastTarget = false;
            RunScenes.At(glow.rectTransform, s, 540, 720, 760, 760);
            UiPulse.On(glow, 0.05f, 2f, 0.7f);
            var tomb = Ui.Panel(frame, Color.white, "Tombstone");
            tomb.sprite = TombstoneSprite(); tomb.preserveAspect = true; tomb.raycastTarget = false;
            RunScenes.At(tomb.rectTransform, s, 540, 720, 420, 420);
            UiBob.On(tomb.rectTransform, 6 * s, 2.4f);
            // the rule on the paper
            var paper = Ui.RoundPanel(frame, SliceKit.Ink, 40 * s, "Paper");
            paper.raycastTarget = false;
            RunScenes.At(paper.rectTransform, s, 540, 1080, 980, 260);
            var face = Ui.RoundPanel(paper.transform, RunScenes.Paper, 34 * s, "Face");
            face.raycastTarget = false;
            Ui.Stretch(face.rectTransform, 7 * s, 7 * s, 7 * s, 7 * s);
            string rule = run.ReviveUsed ? "Your revive for this run is used." : "Revive once with " + RunScenes.Good(Fmt.Pct(Content.RunTuning.ReviveHpPct) + " HP") + " and retry the fight.";
            var rt = RunScenes.Text(face.transform, rule, s, 48, RunScenes.Ink, TextAnchor.MiddleCenter, "ReviveText", 32);
            Ui.Stretch(rt.rectTransform, 30 * s, 20 * s, 30 * s, 100 * s);
            var note = RunScenes.Text(face.transform, "Giving up loses " + RunScenes.Bad(run.PendingLoot.Count + " pending loot items") + ".", s, 38, RunScenes.Ink, TextAnchor.MiddleCenter, "ReviveNote", 26);
            Ui.Stretch(note.rectTransform, 30 * s, 168 * s, 30 * s, 18 * s);
            // chunky buttons, stacked
            float y = 1350;
            if (!run.ReviveUsed)
            {
                ReviveButton(frame, s, y, "Ad revive (" + adsLeft + ")", "Ad revive", null, new Color32(0x5C, 0xC0, 0x48, 255), () => Session.Ads.Show(RunSystem.ReviveAdPlacement, ad =>
                {
                    if (ad.Ok) Run(new RunRevive { Method = ReviveMethod.Ad, AdToken = ad.Value });
                }));
                y += 185;
                ReviveButton(frame, s, y, cost + " Diamonds", Fmt.Num(cost), SummonArt.CurrencyIcon(Currencies.Diamonds), new Color32(0x4C, 0xB8, 0xE8, 255), () => Run(new RunRevive { Method = ReviveMethod.Diamonds }));
                y += 185;
            }
            ReviveButton(frame, s, y, "Give up", "Give up", null, new Color32(0xE0, 0x46, 0x50, 255), () => Run(new RunAbandon()), run.ReviveUsed ? 620 : 420);
            UiTween.PopIn(frame, 0.15f);
            return modal;
        }

        /// <summary>One chunky revive button (outline, white chunky caption, optional icon left of it), named "Button:" + <paramref name="name"/>.</summary>
        private static void ReviveButton(RectTransform frame, float s, float y, string name, string caption, Sprite icon, Color col, Action onClick, float w = 620)
        {
            var b = Ui.Button(frame, "", onClick, col, w * s, 160 * s, true, 20);
            b.name = "Button:" + name;
            RunScenes.At((RectTransform)b.transform, s, 540, y, w, 160);
            UiShapes.Outline((RectTransform)b.transform, SliceKit.Ink, Theme.RadiusButton, 7);
            var t = SliceKit.Chunky(b.transform, caption, (int)(68 * s), Color.white, TextAnchor.MiddleCenter, "Caption");
            Ui.Stretch(t.rectTransform, icon != null ? 110 * s : 0, 0, 0, 8 * s);
            if (icon == null) return;
            var img = Ui.Panel(b.transform, Color.white, "Icon");
            img.sprite = icon; img.preserveAspect = true; img.raycastTarget = false;
            var irt = img.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(96 * s, 96 * s);
            irt.anchoredPosition = new Vector2(-150 * s, 4 * s);
        }

        private static Sprite _tombstone;

        /// <summary>
        /// The revive's centrepiece: <c>Art/Run/Icons/tombstone</c> (512 × 512, transparent; the art package
        /// run-icons-tombstone) once imported, else a drawn grey tombstone with the ink outline.
        /// </summary>
        public static Sprite TombstoneSprite()
        {
            var art = ArtCatalog.RunIcon("tombstone");
            if (art != null) return art;
            if (_tombstone != null) return _tombstone;
            const int w = 320, h = 360;
            var p = new IsoPaint(w, h);
            var stone = (Color)new Color32(0x9A, 0x9E, 0xAE, 255);
            // the slab: straight sides, a round top
            var pts = new List<Vector2> { IsoPaint.V(64, 56), IsoPaint.V(256, 56) };
            for (int i = 0; i <= 24; i++)
            {
                float a = Mathf.PI * i / 24f;
                pts.Add(IsoPaint.V(160 + 96 * Mathf.Cos(a), 240 + 96 * Mathf.Sin(a)));
            }
            p.Shape(pts, IsoPaint.VGrad(IsoPaint.Shade(stone, -0.3f), IsoPaint.Shade(stone, 0.12f), 56, 336), IsoArt.Ink, 10);
            // cel shading on the right, a highlight on the left, a crack
            p.Poly(new List<Vector2> { IsoPaint.V(206, 62), IsoPaint.V(250, 62), IsoPaint.V(250, 236), IsoPaint.V(226, 300), IsoPaint.V(206, 300) }, new Color(0, 0, 0, 0.16f));
            p.Line(IsoPaint.V(96, 120), IsoPaint.V(96, 250), 10, new Color(1, 1, 1, 0.35f));
            p.Line(IsoPaint.V(150, 300), IsoPaint.V(170, 262), 5, IsoArt.Ink);
            p.Line(IsoPaint.V(170, 262), IsoPaint.V(156, 232), 5, IsoArt.Ink);
            // the base slab
            p.Shape(IsoPaint.RoundRect(30, 14, 260, 52, 14), IsoPaint.VGrad(IsoPaint.Shade(stone, -0.45f), IsoPaint.Shade(stone, -0.15f), 14, 66), IsoArt.Ink, 9);
            _tombstone = p.ToSprite("rs_tombstone", IsoPaint.V(w / 2f, h / 2f));
            return _tombstone;
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

        /// <summary>
        /// The run's skills book: two tabs, "Skills" (the signatures, then every other skill as its tile) and "Stats" (every
        /// battle stat of the worm right now, <see cref="RunStatSheet"/>); each tab scrolls once it is taller than the screen.
        /// </summary>
        private void SkillsModal(ActiveRun run)
        {
            RectTransform pane = null;
            Modal("Skills", b =>
            {
                Kit.TabChips(b, new[] { "Skills", "Stats" }, 0, i => FillSkillsBook(pane, run, i), 84);
                pane = Ui.Column(b, 0, 0, null, "BookPane");
                FillSkillsBook(pane, run, 0);
            });
        }

        private void FillSkillsBook(RectTransform pane, ActiveRun run, int tab)
        {
            if (pane == null) return;
            Ui.Clear(pane);
            // the modal's title, tabs, Close button and padding take about 560 px; the rest scrolls
            float layer = App.ModalLayer != null ? App.ModalLayer.rect.height : 0;
            float max = Mathf.Max(640, layer - 560);
            Ui.ScrollingColumn(pane, max, b => { if (tab == 1) SkillsBookStats(b, run); else SkillsBookSkills(b, run); });
            // the frame hugs a short tab (ScrollingColumn caps only the preferred height; its min stays the cap)
            var le = pane.GetChild(pane.childCount - 1).GetComponent<LayoutElement>();
            if (le != null) le.minHeight = le.preferredHeight;
        }

        /// <summary>The Stats tab: one gold heading per group, a line per stat (name, dim note, value on the right).</summary>
        private void SkillsBookStats(RectTransform b, ActiveRun run)
        {
            foreach (var g in RunStatSheet.Build(Content, run))
            {
                var head = SliceKit.Chunky(b, g.Title, 40, Theme.Gold, TextAnchor.MiddleLeft, "StatGroup:" + g.Title);
                Ui.Size(head, -1, 64);
                int i = 0;
                foreach (var r in g.Rows)
                {
                    var row = Ui.Row(b, r.Note != null ? 92 : 68, 12, i++ % 2 == 0 ? new Color(1, 1, 1, 0.07f) : new Color(0, 0, 0, 0.18f), 18, "Stat:" + r.Stat);
                    var names = Ui.Column(row, 0, 0, null, "Name");
                    names.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                    Ui.Size(names, -1, -1, 1);
                    Ui.Label(names, r.Label, Theme.FontNormal, Theme.Text, TextAnchor.MiddleLeft, "StatName");
                    if (r.Note != null) Ui.Label(names, r.Note, 22, Theme.TextDim, TextAnchor.MiddleLeft, "StatNote");
                    var v = SliceKit.Chunky(row, r.Value, 40, Color.white, TextAnchor.MiddleRight, "StatValue");
                    Ui.Size(v, 320, -1, 0);
                }
            }
        }

        private void SkillsBookSkills(RectTransform b, ActiveRun run)
        {
            var ids = RunSystem.ActiveSkillIds(run);
            if (ids.Count == 0) Note(b, "No skills yet: level up on the board to draft some.");
            // the signatures first (the run's identity; several can be found): each with its awakening and how far the next one is
            foreach (var sigId in RunSystem.Signatures(run))
            {
                var sig = Content.Skill(sigId);
                if (sig == null || !ids.Remove(sig.Id)) continue;
                int slv = RunDraft.Level(run, sig.Id);
                var scol = Theme.FamilyColor(sig.Family);
                var sigRow = Ui.Row(b, -1, 16, Theme.Tint(scol, 0.3f), 14, "Signature:" + sig.Id);
                var sigLayout = sigRow.GetComponent<HorizontalLayoutGroup>();
                sigLayout.childAlignment = TextAnchor.UpperLeft;
                sigLayout.childForceExpandHeight = false;
                UiShapes.Outline(sigRow, SignaturePick.SignatureColor, 16, 4);
                var sigTile = SkillTileView.Build(sigRow, sig.Id, SignaturePick.SignatureColor, sig.Family, 132);
                Ui.Size(sigTile, 132, 132, 0);
                var sigTip = SkillTip.Of(sig.Id, slv); sigTip.TierOverride = "Signature";
                SkillTileView.Tappable(sigTile, Content, () => sigTip);
                var card = Ui.Column(sigRow, 6, 0, null, "SignatureText");
                Ui.Size(card, -1, -1, 1);
                Ui.Label(card, "SIGNATURE · " + SkillText.FamilyName(sig.Family).ToUpperInvariant(), 20, SignaturePick.SignatureColor);
                Ui.Label(card, SkillText.Title(Content, sig, slv), Theme.FontNormal, Color.Lerp(scol, Color.white, 0.6f));
                Ui.Label(card, Fmt.SkillFull(SkillLeveling.At(Content, sig, slv)), Theme.FontSmall, Theme.Text);
                int next = RunSystem.NextAwakeningAt(Content, run, sig.Id);
                Ui.Label(card, next > 0 ? "Awakens at " + next + " " + SkillText.FamilyName(sig.Family) + " skills (have " + RunDraft.FamilyCount(Content, run, sig.Family) + ")." : "Fully awakened.",
                    20, Theme.TextDim);
            }
            // every other skill as the shared tile (tier frame, art filling it) with its name and level; a tap = the full text
            if (ids.Count == 0) return;
            const float cellW = 196, cellH = 262, tileSize = 168;
            var grid = Ui.Rect(b, "SkillGrid");
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(cellW, cellH);
            gl.spacing = new Vector2(14, 14);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 4;
            gl.childAlignment = TextAnchor.UpperCenter;
            Ui.Size(grid, -1, ((ids.Count + 3) / 4) * (cellH + 14), 1);
            foreach (var id in ids)
            {
                var s = Content.Skill(id);
                int lv = Math.Max(1, RunDraft.Level(run, id));
                var cell = Ui.Rect(grid, "Skill:" + id);
                var tile = SkillTileView.Build(cell, Content, id, tileSize);
                tile.anchorMin = tile.anchorMax = tile.pivot = new Vector2(0.5f, 1);
                tile.anchoredPosition = Vector2.zero;
                var lvl = SliceKit.Chunky(tile, "Lv " + lv, 26, Color.white, TextAnchor.LowerRight, "SkillLevel");
                Ui.Stretch(lvl.rectTransform, 0, 0, 16, 12);
                var name = Ui.Label(cell, s?.Name ?? id, 22, Theme.Text, TextAnchor.UpperCenter, "SkillName");
                name.rectTransform.anchorMin = new Vector2(0, 0); name.rectTransform.anchorMax = new Vector2(1, 0);
                name.rectTransform.pivot = new Vector2(0.5f, 0);
                name.rectTransform.sizeDelta = new Vector2(0, cellH - tileSize - 6);
                name.rectTransform.anchoredPosition = Vector2.zero;
                name.horizontalOverflow = HorizontalWrapMode.Wrap;
                name.resizeTextForBestFit = true; name.resizeTextMinSize = 14; name.resizeTextMaxSize = name.fontSize;
                var tip = SkillTip.Of(id, lv);
                SkillTileView.Tappable(tile, Content, () => tip);
            }
        }
    }
}
