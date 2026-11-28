using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Client.World.Anim;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Animates a BattleResult log on a small stage: the player's side (worm, aliens, mount) on the left, enemies on the
    /// right. Each entry drives code tweens (no packages): the attacker lunges toward its target, the target flashes and
    /// shakes, floating damage numbers rise (crits bigger with "CRIT"), dodge / heal / shield / status / resist / stun
    /// popups, HP bars tween down, status chips (BURN 2), knocked-out fighters fade, a turn-order strip shows who acts
    /// next. One speed button cycling ×0.5/×1/×2, ×3 with the monthly pass (skipped otherwise). Sidekicks (aliens, drones) stand along the left edge,
    /// are never hit and act on their own turns: a hop and flash, their ability's name floating up, then its effect; a blocking
    /// sidekick raises a guard on the worm (shield chip with the count) and dashes in front of it when it intercepts a hit.
    /// Bar values come straight from the log entries (TargetHp/Shield/Rage, ActorShield/Rage). <see cref="PlayAll"/> /
    /// <see cref="PlaySteps"/> apply entries without animation; <see cref="Instant"/> makes the playback skip every
    /// animation (tests, scripted playthroughs); in EditMode every tween completes at once anyway.
    /// </summary>
    public sealed class BattlePlaybackScreen : UiScreen
    {
        private sealed class FighterView
        {
            public BattleFighterInfo Info;
            public RectTransform Root;
            public RectTransform Sprite;
            public Image SpriteImage;
            /// <summary>The creature's code animation (null: the lettered box, a mount, reduced animation off-screen).</summary>
            public UiCreature Anim;
            public Color SpriteColor;
            public Color FlashColor = Color.white;
            public Vector2 Home;
            public UiBar Hp, Shield, Rage;
            public RectTransform Chips;
            public CanvasGroup Group;
            public double HpNow, ShieldNow, RageNow;
            public readonly Dictionary<StatusId, int> Statuses = new Dictionary<StatusId, int>();
            public string ChipKey = "";
            public bool Visible = true;
            public bool Dead;
            /// <summary>Hits this fighter's guards still hold (Block abilities of its sidekicks, an alien's Guard).</summary>
            public int Guards;
            /// <summary>An alien Guard's % (aliens pass): the next hit taken deals that much less (0 = a Block's full intercept).</summary>
            public long GuardPct;
            /// <summary>Damage numbers / words floating over this fighter right now (they stack upwards).</summary>
            public int LivePopups;
            /// <summary>The worm's art faces left: it is mirrored while it looks right (<see cref="WormFacing"/>).</summary>
            public bool MirrorsWorm, Flip;
            public bool Sidekick => Info.Side == BattleSide.Player && (Info.Role == CombatRole.Alien || Info.Role == CombatRole.Summon);
        }

        /// <summary>The shared play speed (PlaySpeed.Current) as allowed for this player (×3 with the pass), read at build.</summary>
        private float _speed = PlaySpeed.Default;

        private readonly BattleResult _battle;
        private readonly List<FighterView> _views = new List<FighterView>();
        private int _next;
        private float _timer;
        private Text _round;
        /// <summary>Hero and mount kits: the hero's Ability HUD ("Starfall 2/4", "Feast in 2") and the mount's ("Scan Pulse in 2").</summary>
        private Text _kitHero, _kitMount;
        private string _mountShot;
        private int _mountNext = -1, _mountReload;
        /// <summary>What the kit HUD showed (tests, screenshots).</summary>
        public readonly List<string> KitHud = new List<string>();
        public int Volleys { get; private set; }
        public int Abilities { get; private set; }
        private Text _banner;
        private RectTransform _endPanel;
        private RectTransform _stage;
        /// <summary>Damage numbers, words and ability banners: a layer over every fighter (BT5), cleared when the battle ends.</summary>
        private RectTransform _fx;
        private RectTransform _order;
        private GameObject _orderPlate;
        private bool _built;
        private RectTransform _feed;
        private readonly Queue<GameObject> _feedLines = new Queue<GameObject>();
        private FighterView _hero;
        private int _popupSeq;

        /// <summary>Stage height (board px, centred on <see cref="StageCenterY"/>) and a fighter card's size at scale 1.</summary>
        public const float StageH = 1060;
        public static readonly Vector2 ViewSize = new Vector2(480, 660);
        /// <summary>
        /// Height of a fighter's feet over its card's bottom: the board's stack under each fighter sits there (the worm: HP
        /// number, HP, shield and rage bars, statuses; an enemy: HP number, HP bar, statuses).
        /// </summary>
        public const float SpriteBase = 200;

        /// <summary>Skip mode: no animations, the whole log is applied on the next frame.</summary>
        public static bool Instant;
        /// <summary>Extra playback multiplier for scripted playthroughs/tests (1 in the game).</summary>
        public static float TimeScale = 1f;

        // ---- design A: top strip + skill strip ----
        private BattleHudContext _ctx;
        /// <summary>What the HUD shows (tests).</summary>
        public BattleHudContext Context => _ctx;
        private readonly SkillFireQueue _fires = new SkillFireQueue();
        private readonly Dictionary<string, RectTransform> _skillTiles = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, int> _fireCounts = new Dictionary<string, int>();
        private RectTransform _fireLayer;
        /// <summary>Key of a "+N" tile: fires of skills without a tile of their own pulse it (every skill has its tile today).</summary>
        public const string MoreKey = "+more";
        /// <summary>Every skill firing played so far, in order ("Magma Tail · Burn ×2"; tests).</summary>
        public readonly List<string> SkillFires = new List<string>();

        /// <summary>A fight's playback; a won run battle returns to the board by itself, a lost one waits for Continue.</summary>
        public BattlePlaybackScreen(BattleResult battle, BattleHudContext context = null)
        {
            _battle = battle;
            _ctx = context;
        }

        public override string Title => "Battle";
        public override bool AutoRebuild => false;
        /// <summary>No app header: the battle's own top HUD (<see cref="BattleHud"/>) is the header.</summary>
        public override float HeaderHeight => 0;
        public override bool CoversResourceBar => true;
        /// <summary>How often a skill fired so far (tests).</summary>
        public int FireCount(string skillId) => _fireCounts.TryGetValue(skillId, out var n) ? n : 0;
        /// <summary>The skill-fire queue of the strip (tests: what plays / waits on a tile).</summary>
        public SkillFireQueue FireQueue => _fires;
        /// <summary>Seconds one skill fire plays on its tile at the current speed.</summary>
        public float FireSeconds => Mathf.Max(0.3f, 0.9f / _speed / Mathf.Max(0.01f, TimeScale));
        /// <summary>The top strip as text (tests).</summary>
        public string HudText => _hud?.Summary;
        public bool Finished => _battle == null || _next >= _battle.Log.Count;

        /// <summary>
        /// The round pill: "Round n" only. The battle is simulated before it is played back, but its length is never shown
        /// (owner: knowing "Round 1/12" up front gave the ending away); battles have no per-level round limit to show.
        /// </summary>
        public static string RoundLabel(int round) => "Round " + round;
        public float Speed => _speed;
        /// <summary>The speed the battle plays at, as the board's speed button shows it (tests).</summary>
        public string SpeedLabel => PlaySpeed.Label(_speed);
        /// <summary>The next speed of the shared setting (the board's speed button; tests): the battle follows at once.</summary>
        public void CycleSpeed() => _speed = PlaySpeed.Cycle(ShopSystem.HasBattleSpeed3(State, Content, Now));
        public float StepSeconds => 0.55f / _speed / Mathf.Max(0.01f, TimeScale);
        /// <summary>Animation time for one beat of an entry (lunge, hit, bar) at the current speed.</summary>
        private float Beat => Instant ? 0 : Mathf.Min(0.42f / Mathf.Min(1f, _speed), StepSeconds * 0.8f); // the cap grows at ×0.5 so a beat still fills its step

        // ---- build -------------------------------------------------------------------------------

        /// <summary>
        /// Board "New · battle" (UI kit v2, docs/design/ui-kit-v2.md): everything is placed in board px (1080 wide, y down) in a
        /// design container scaled to the screen's width; its height is the screen's height in board px (2400 on 9:20 phones).
        /// The arena art covers the top down to the dimmed lower area (<see cref="DimTop"/>, its top edge cut at an angle); the
        /// top HUD (<see cref="BattleHud"/>) and the stage sit on top; the skill tiles, speed / Continue / Log keep to the bottom.
        /// </summary>
        protected override void Build(RectTransform body)
        {
            if (_built) { _next = 0; _views.Clear(); _feedLines.Clear(); SkillFires.Clear(); _fireCounts.Clear(); _heroBuffs.Clear(); _roundNow = 0; }
            _built = true;
            if (_battle == null) { Note(body, "No battle to show."); return; }
            var bl = body.GetComponent<VerticalLayoutGroup>();
            if (bl != null) { bl.padding = new RectOffset(0, 0, 0, 0); bl.spacing = 0; }
            _ctx = _ctx ?? BattleHudContext.FromRun(State?.Run.Active, Content);
            // BT3 / Q2: a battle without a run behind it (hero / mount kit fights, a run that already ended) shows only the
            // worm's account max HP, ATK and DEF, bound before the first frame — no level pill, never "–"
            if (!_ctx.HasStats && State != null && Session?.Engine != null) _ctx.FillFromAccount(State, Session.Engine.BuildLoadout(State, Now));
            float frameH = BodyFrame != null && BodyFrame.rect.height > 100 ? BodyFrame.rect.height : 1920;
            float frameW = BodyFrame != null && BodyFrame.rect.width > 100 ? BodyFrame.rect.width : 1080;
            float s = frameW / KitV2.DesignWidth;
            _scale = s;
            var holder = Ui.Rect(body, "BattleHolder");
            Ui.Size(holder, -1, frameH);
            float H = Mathf.Max(MinDesignHeight, frameH / s);
            var d = Ui.Rect(holder, "Design");
            d.anchorMin = d.anchorMax = d.pivot = new Vector2(0, 1);
            d.sizeDelta = new Vector2(KitV2.DesignWidth, H);
            d.anchoredPosition = Vector2.zero;
            d.localScale = new Vector3(s, s, 1);
            float dimTop = DimTop(H);

            // the arena: the battle art covers the board down into the dimmed area (cover fit, centred)
            var arena = KitV2.Place(d, "Arena", 0, 0, KitV2.DesignWidth, dimTop + ArenaOverlap);
            arena.gameObject.AddComponent<RectMask2D>();
            var bg = ArtCatalog.BattleBackground(null) ?? HudArt.Arena();
            float k = Mathf.Max(KitV2.DesignWidth / bg.rect.width, (dimTop + ArenaOverlap) / bg.rect.height);
            float bw = bg.rect.width * k, bh = bg.rect.height * k;
            KitV2.Icon(arena, bg, (KitV2.DesignWidth - bw) / 2, 0, bw, bh, "Art").preserveAspect = false;
            // the lower area: navy, dimmed over the arena's foot, its top edge cut at an angle (code-drawn on purpose)
            var panel = KitV2.Place(d, "SkillPanel", 0, dimTop, KitV2.DesignWidth, H - dimTop);
            if (panel.GetComponent<CanvasRenderer>() == null) panel.gameObject.AddComponent<CanvasRenderer>();
            var slant = panel.gameObject.AddComponent<KitSlant>();
            slant.raycastTarget = false;
            slant.LeftDrop = DimCut / (H - dimTop); slant.RightDrop = 0; slant.SolidAt = Mathf.Min(0.6f, 240 / (H - dimTop));

            // the stage: fighters placed from its centre (board y StageCenterY)
            _stage = KitV2.Place(d, "Fighters", 0, StageCenterY - StageH / 2, KitV2.DesignWidth, StageH);
            var players = _battle.Fighters.FindAll(f => f.Side == BattleSide.Player);
            var enemies = _battle.Fighters.FindAll(f => f.Side != BattleSide.Player);
            foreach (var f in _battle.Fighters)
            {
                var side = f.Side == BattleSide.Player ? players : enemies;
                _views.Add(Fighter(f, side.IndexOf(f), side.Count));
            }
            _hero = _views.Find(v => v.Info.Role == CombatRole.Worm) ?? _views.Find(v => v.Info.Side == BattleSide.Player);
            _fx = KitV2.Place(d, "BattleFx", 0, StageCenterY - StageH / 2, KitV2.DesignWidth, StageH);
            var mount = _views.Find(v => v.Info.Role == CombatRole.Mount);
            if (mount != null && _hero != null && _hero.Info.Role == CombatRole.Worm) SeatOnMount(_hero, mount.Info.Id);

            // the top HUD: level, the worm's live HP, ATK / DEF as the board showed them, settings; coins, "Round n", skip
            _hud = BattleHud.Build(d, _ctx.Coins >= 0, ShowMenu, Close, PulseSkip); // T49: a tap on the stats pulses the skip button
            if (_hud.Coins != null) _hud.Coins.text = Fmt.Num(_ctx.Coins);
            _round = _hud.Round;
            _skipButton = _hud.SkipButton;
            // the kit HUD (hero and mount kits) under the coins: the hero's Ability counter / timer, the mount's next shot
            if (_battle.Log.Exists(x => x.Action == BattleAction.KitCounter && x.Round == 0))
            {
                var kp = KitV2.HudPill(d, 21, KitRowY, 400, 64, "KitHeroPill", 0.85f).rectTransform;
                _kitHero = KitV2.Text(kp, "", 18, 6, 364, 52, 30, Theme.Gold, TextAnchor.MiddleCenter, "KitHero");
                KitFit.On(_kitHero, 18);
            }
            if (_views.Exists(v => v.Info.Role == CombatRole.Mount) && _battle.Log.Exists(x => x.Action == BattleAction.Volley))
            {
                var mp = KitV2.HudPill(d, 21, KitRowY + (_kitHero != null ? 72 : 0), 400, 56, "KitMountPill", 0.85f).rectTransform;
                _kitMount = KitV2.Text(mp, "", 18, 4, 364, 48, 26, Theme.Hex(0x7FB2FF), TextAnchor.MiddleCenter, "KitMount");
                KitFit.On(_kitMount, 16);
                var first = _battle.Log.Find(x => x.Action == BattleAction.Volley);
                _mountShot = Content.Skill(first.SkillId)?.Name ?? "Shot";
                _mountNext = first.Round;
                SetMountHud(0);
            }

            // turn order: who acts next (portraits) on a solid kit plate under the round, right of the kit HUD when it shows
            float orderX = _kitHero != null || _kitMount != null ? 559 : 290;
            var plate = Kit.Plate(d, 26, "TurnOrderPlate");
            _orderPlate = plate.gameObject;
            KitV2.At(plate.rectTransform, orderX, KitRowY, 500, 78);
            _order = Ui.Row(d, 62, 8, null, 0, "TurnOrder");
            KitV2.At(_order, orderX + 10, KitRowY + 8, 480, 62);
            _order.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            _order.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

            // the bottom row: speed (left), Continue (centre, after a lost fight), battle log (right)
            _speed = PlaySpeed.Allowed(ShopSystem.HasBattleSpeed3(State, Content, Now));
            float rowY = H - BottomRowFromBottom;
            var speed = KitV2.SpeedButton(d, 27, rowY, HudArt.Icon("speed"), SpeedLabel, () => { CycleSpeed(); RefreshSpeed(); });
            _speedText = speed.transform.Find("SpeedText")?.GetComponent<Text>();
            _endPanel = KitV2.Place(d, "EndPanel", 370, rowY, 340, 112);
            var cont = KitV2.SmallButton(_endPanel, "button_small_yellow", 0, 0, 340, 112, KitV2.Yellow, "Continue", Close, "ContinueButton");
            cont.GetComponentInChildren<Text>().fontSize = 48;
            _endPanel.gameObject.SetActive(false);
            var logImg = KitV2.Box(d, "speed_button", 893, rowY, 160, 112, KitV2.Navy, 26.7f, 6.7f, KitV2.Ink, null, "Button:Battle log");
            KitV2.Text(logImg.rectTransform, "Log", 0, 22, 160, 68, 44, Theme.Hex(0xD8D4EE), TextAnchor.MiddleCenter, "Label");
            KitV2.Tap(logImg.rectTransform, ToggleLog);

            SkillGrid(d, dimTop, rowY);
            _feed = Ui.Card(d, Theme.Panel, null, 30, 20, 4, 0, "Feed");
            KitV2.At(_feed, 40, Mathf.Max(StageCenterY - 300, rowY - 940), 1000, 900);
            Ui.Caption(_feed, "Battle log", Theme.Gold);
            _feed.gameObject.SetActive(false);

            _fx.SetAsLastSibling(); // #31: hit numbers and ability chips draw over the HUD and the NEXT plate
            _banner = KitV2.Text(d, "", 40, StageCenterY - 90, 1000, 180, 130, Theme.Gold, TextAnchor.MiddleCenter, "Banner");
            _banner.gameObject.SetActive(false);
            foreach (var v in _views) Refresh(v, Content.Combat.RageMax, false);
            RefreshHero();
            TurnOrder();
        }

        /// <summary>Board px: the stage's centre line, the kit HUD / NEXT row, the bottom row (speed, Continue, Log) from the bottom.</summary>
        public const float StageCenterY = 850, KitRowY = 336, BottomRowFromBottom = 171;
        /// <summary>The dimmed lower area: 800 px on the 2400 board, its left edge <see cref="DimCut"/> lower (the angled cut).</summary>
        public const float DimHeight = 800, DimCut = 80, ArenaOverlap = 107;
        /// <summary>Shortest design height laid out (16:9 phones are 1920).</summary>
        public const float MinDesignHeight = 1700;

        /// <summary>Board y of the dimmed area's top (right edge) for a design height: 1600 on the 2400 board, never above the stage's floor.</summary>
        public static float DimTop(float designHeight) => Mathf.Max(1330, designHeight - DimHeight);

        private RectTransform _skillGrid;
        private Text _speedText;
        private UiModal _menu;

        private void RefreshSpeed()
        {
            if (_speedText != null) _speedText.text = SpeedLabel;
        }

        private void ToggleLog()
        {
            if (_feed == null) return;
            _feed.gameObject.SetActive(!_feed.gameObject.activeSelf);
            _feed.SetAsLastSibling();
        }

        /// <summary>The settings button: the battle pauses under a small menu (resume, battle log, skip).</summary>
        public void ShowMenu()
        {
            if (App == null || (_menu != null && _menu.IsOpen)) return;
            _menu = Ui.Modal(App.ModalLayer, "Paused", b => Ui.Label(b, "The battle waits for you.", Theme.FontNormal, Theme.TextDim, TextAnchor.MiddleCenter),
                new ModalButton("Resume", null, Theme.Accent),
                new ModalButton("Log", ToggleLog),
                new ModalButton("Skip", Close));
        }

        /// <summary>The settings menu is open: the playback waits (tests).</summary>
        public bool Paused => _menu != null && _menu.IsOpen;

        private RectTransform _skipButton;
        /// <summary>Times the skip button pulsed for a HUD tap (tests, T49).</summary>
        public int SkipPulses { get; private set; }

        /// <summary>A tap on the HUD's stats: the skip button pulses to show where to skip (G1 T49).</summary>
        public void PulseSkip()
        {
            if (_skipButton == null) return;
            SkipPulses++;
            App.FeedbackPop(_skipButton);
        }

        private float _scale = 1;
        private BattleHud _hud;

        // ---- the skill tiles (board: light silver tiles centred in rows, the firing one green) ----------------------------

        /// <summary>Tiles per row and their size / step (board px): four of 181 up to 8 skills, five of 160 above; rows centred.</summary>
        public const int GridColumns = 4, WideColumns = 5;
        public const float GridTile = 181, GridStep = 208, WideTile = 160, WideStep = 190;
        /// <summary>Board px from the dimmed area's top to the first tile row (more room on tall screens).</summary>
        public const float SkillGap = 185, CompactSkillGap = 110;

        /// <summary>The skill area's scroll view (tests).</summary>
        public ScrollRect SkillScroll { get; private set; }

        private void SkillGrid(RectTransform d, float dimTop, float rowY)
        {
            _skillTiles.Clear();
            var order = BattleSkillFx.StripOrder(_ctx.Skills, _battle, Content);
            var levels = _ctx.Levels;
            var tray = RunPresentation.Tray(Content, order, id => levels != null && levels.TryGetValue(id, out var lv) ? lv : 1, int.MaxValue);
            int n = tray.Tiles.Count;
            bool wide = n > 2 * GridColumns;
            int cols = wide ? WideColumns : GridColumns;
            float tile = wide ? WideTile : GridTile, step = wide ? WideStep : GridStep;
            float top = dimTop + (rowY - dimTop >= SkillGap + 2 * step ? SkillGap : CompactSkillGap) - 10;
            float bottom = rowY - 24;
            var grid = KitV2.Place(d, "SkillStrip", 0, top, KitV2.DesignWidth, bottom - top);
            _skillGrid = grid;
            var view = Ui.Rect(grid, "SkillViewport");
            Ui.Stretch(view);
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0); // drag target between tiles
            var content = Ui.Rect(view, "SkillContent");
            int rows = Mathf.Max(1, (n + cols - 1) / cols);
            const float pad = 12; // room for the firing tile's glow
            float contentH = Mathf.Max(bottom - top, rows * step + 2 * pad);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, contentH); content.anchoredPosition = Vector2.zero;
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.content = content; sr.viewport = view; sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 40;
            SkillScroll = sr;
            for (int i = 0; i < n; i++)
            {
                var t = tray.Tiles[i];
                int row = i / cols, inRow = Mathf.Min(cols, n - row * cols);
                float cx = KitV2.DesignWidth / 2 - (inRow - 1) * step / 2 + (i % cols) * step;
                float cy = pad + row * step + step / 2;
                var family = Content.Skill(t.SkillId)?.Family ?? SkillFamily.None;
                var tileRt = KitV2.SkillTile(content, tile, ArtCatalog.SkillIcon(t.SkillId), family == SkillFamily.None ? "★" : SkillText.FamilyName(family).Substring(0, 1),
                    family == SkillFamily.None ? Theme.Hex(0xFFE05A) : Theme.FamilyColor(family), "Slot:" + t.SkillId);
                tileRt.anchorMin = tileRt.anchorMax = new Vector2(0, 1);
                tileRt.anchoredPosition = new Vector2(cx, -cy);
                var tip = SkillTip.Of(t.SkillId, t.Level);
                SkillTileView.Tappable(tileRt, Content, () => tip); // tap = the full text (owner playtest 0.1.224)
                _skillTiles[t.SkillId] = tileRt;
            }
            if (rows * step + 2 * pad > bottom - top + 1)
            {
                // a fade at the bottom edge: more tiles below
                var fade = Ui.Panel(grid, Color.white, "SkillScrollHint");
                fade.sprite = HudArt.Gradient(new Color32(0x1D, 0x1B, 0x33, 0), new Color32(0x1D, 0x1B, 0x33, 255));
                fade.raycastTarget = false;
                fade.rectTransform.anchorMin = new Vector2(0, 0); fade.rectTransform.anchorMax = new Vector2(1, 0); fade.rectTransform.pivot = new Vector2(0.5f, 0);
                fade.rectTransform.sizeDelta = new Vector2(0, 40); fade.rectTransform.anchoredPosition = Vector2.zero;
            }
            if (n == 0) KitV2.Text(grid, "No skills yet — draft some on the board.", 40, 20, 1000, 80, 34, Theme.TextDim, TextAnchor.MiddleCenter, "NoSkills");
            // labels rise from the tiles over the stage: their own layer on top of the screen
            _fireLayer = Ui.Rect(Root, "SkillFireLayer");
            Ui.Stretch(_fireLayer);
            _fireLayer.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            _fires.Started -= PlayFire;
            _fires.Started += PlayFire;
        }

        /// <summary>A skill of the player's side fired (log entry with its SkillId): count it, queue its tile's glow + label.</summary>
        private void OnSkillFire(BattleLogEntry e, bool animate)
        {
            var fire = BattleSkillFx.FireOf(e, _battle, Content);
            if (!fire.HasValue) return;
            var f = fire.Value;
            SkillFires.Add(f.Label);
            _fireCounts[f.SkillId] = FireCount(f.SkillId) + 1;
            if (!animate || Instant) return; // skip mode: no animation, the count / log above is all
            string key = _skillTiles.ContainsKey(f.SkillId) ? f.SkillId : _skillTiles.ContainsKey(MoreKey) ? MoreKey : null;
            if (key == null) return;
            _fires.Duration = FireSeconds;
            _fires.Push(key, f.Label);
        }

        /// <summary>One fire on its tile: the tile turns green with its glow (the board's active tile) and pops, the label rises.</summary>
        private void PlayFire(string key, string label)
        {
            if (!_skillTiles.TryGetValue(key, out var tile) || tile == null) return;
            float secs = FireSeconds;
            KitV2.SetSkillActive(tile, true);
            var flash = Ui.RoundPanel(tile, new Color(1, 1, 1, 0), 26.7f, "FireFlash");
            flash.raycastTarget = false;
            flash.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Stretch(flash.rectTransform, 4, 4, 4, 4);
            UiTweens.Play(tile.gameObject, "fire", secs, t =>
            {
                // 181 → 197 (the board's active tile), a small overshoot as it lights up
                float pop = 1 + 0.09f * Mathf.Min(1, t * 6) + 0.06f * Mathf.Sin(Mathf.Clamp01(t * 3f) * Mathf.PI);
                if (tile != null) tile.localScale = Vector3.one * pop;
                if (flash != null) flash.color = new Color(1, 1, 1, 0.6f * Mathf.Max(0, 1 - t * 2.5f));
            }, () =>
            {
                if (tile != null) { tile.localScale = Vector3.one; KitV2.SetSkillActive(tile, false); }
                if (flash != null) Ui.DestroyGo(flash.gameObject);
            });
            if (_fireLayer == null || !Application.isPlaying) return;
            // one skill hitting several targets fires once per target: show its label once
            if (!_liveLabelTexts.Add(label)) return;
            // a pill sized to its text, rising from the tile; labels of tiles firing at once stack in up to three rows
            var holder = Ui.Chip(_fireLayer, label, new Color(0.08f, 0.05f, 0.09f, 0.94f), Theme.Gold, 30, 60, Theme.Shade(Theme.Gold, 0.8f), "SkillFireLabel");
            holder.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
            holder.pivot = new Vector2(0.5f, 0.5f);
            int lane = _liveLabels++ % 3;
            // in the free band between the arena and the tile rows, above the firing tile's column (labels of fires at once
            // stack upwards into the arena's foot), never over the skill tiles or the fighters
            var grid = _skillGrid != null ? _skillGrid : tile;
            var band = grid.TransformPoint(new Vector3(0, grid.rect.yMax + 62, 0));
            var world = new Vector3(tile.position.x, band.y, band.z);
            Vector2 start = (Vector2)_fireLayer.InverseTransformPoint(world) + new Vector2(0, lane * 74);
            float half = Mathf.Max(0, _fireLayer.rect.width / 2 - (label.Length * 9 + 40));
            start.x = Mathf.Clamp(start.x, -half, half);
            holder.anchoredPosition = start;
            var group = holder.gameObject.AddComponent<CanvasGroup>();
            float life = Mathf.Max(0.7f, secs * 1.5f);
            UiTweens.Play(holder.gameObject, "float", life, t =>
            {
                holder.anchoredPosition = start + new Vector2(0, 36 * RunFx.Ease(t));
                holder.localScale = Vector3.one * (t < 0.12f ? Mathf.Lerp(0.7f, 1.05f, t / 0.12f) : 1);
                group.alpha = t < 0.75f ? 1 : 1 - (t - 0.75f) / 0.25f;
            }, () => { _liveLabels = Math.Max(0, _liveLabels - 1); _liveLabelTexts.Remove(label); Ui.DestroyGo(holder.gameObject); });
        }

        private int _liveLabels;
        private readonly HashSet<string> _liveLabelTexts = new HashSet<string>();

        /// <summary>
        /// Stage position (a fighter card's centre, from the stage centre; board px, y up) of the n-th of
        /// <paramref name="count"/> fighters of a side. The player's last fighter is the worm (BattleSimulator.BuildPlayerSide
        /// adds it last): big, left of centre (<see cref="WormSpot"/>); the companions (aliens, summons) stand along the left
        /// edge (<see cref="CompanionPosition"/>); one enemy stands right as on the board, two or more stack in one column down
        /// the right side (#30).
        /// </summary>
        public static Vector2 StagePosition(BattleSide side, int index, int count)
        {
            count = Math.Max(1, count);
            if (side == BattleSide.Player)
            {
                if (index == count - 1) return WormSpot;
                return CompanionPosition(index);
            }
            if (count == 1) return EnemySpot;
            // one column down the right side: card tops from under the NEXT plate down to the dimmed area
            float k = EnemyScale(count), visible = EnemyVisibleH * k;
            float step = (EnemyColumnTop - EnemyColumnBottom - visible) / (count - 1);
            float bottom = EnemyColumnTop - visible - step * index; // the card's bottom edge
            return new Vector2(EnemySpot.x, bottom + ViewSize.y / 2 * k);
        }

        /// <summary>The board's spots (stage px): the worm's card and a lone enemy's card.</summary>
        public static readonly Vector2 WormSpot = new Vector2(-134, 0), EnemySpot = new Vector2(287, -30);

        /// <summary>
        /// The enemy column for 2+ enemies (stage px from the stage centre): card tops from just under the NEXT plate (board y
        /// 430) to the dimmed area (board y 1610), and the drawn height of one enemy card at scale 1 (stack, sprite).
        /// </summary>
        public const float EnemyColumnTop = StageCenterY - 430, EnemyColumnBottom = StageCenterY - 1610, EnemyVisibleH = SpriteBase + EnemySpriteSize;

        /// <summary>Scale of each of <paramref name="count"/> enemies (1 for one or two; 3+ shrink to fit the column, never below 0.5).</summary>
        public static float EnemyScale(int count)
        {
            if (count < 2) return 1;
            return Mathf.Clamp((EnemyColumnTop - EnemyColumnBottom - 16 * (count - 1)) / (count * EnemyVisibleH), 0.5f, 1f);
        }

        /// <summary>A shrunk fighter's bars, HP number and status chips keep at least this effective scale (readable, #30).</summary>
        public const float MinBarScale = 0.92f;

        /// <summary>
        /// The k-th companion spot (sidekicks: aliens, then drones): the board's column along the left edge (sprite centres at
        /// board y 760, 944, 1128), then above it, then below it; every sprite keeps ≥ 16 px to the screen edge.
        /// </summary>
        public static Vector2 CompanionPosition(int k)
        {
            k %= 6;
            float[] ys = { 760, 944, 1128, 576, 1312, 1360 };
            float x = k == 5 ? 290 : 16 + AlienSpriteSize * SidekickScale / 2 + 2;
            // the card's centre from the sprite's centre (feet at SpriteBase over the card's bottom, scaled)
            float centre = ys[k] + AlienSpriteSize * SidekickScale / 2 + SpriteBase * SidekickScale - ViewSize.y * SidekickScale / 2;
            return new Vector2(x - KitV2.DesignWidth / 2, StageCenterY - centre);
        }

        /// <summary>Scale of a sidekick on the stage (the worm and the enemies are 1): 185 px aliens as on the board.</summary>
        public const float SidekickScale = 0.84f;

        /// <summary>
        /// Seats the worm on its mount: the mount's art (ArtCatalog <c>Art/Slice/Mounts/&lt;mount id&gt;</c>, else the drawn
        /// hover saucer, HudArt.Mount) under the worm's sprite, its rim in front of the worm's tail; the worm's stack (HP number,
        /// bars, statuses) moves down under the mount.
        /// </summary>
        private void SeatOnMount(FighterView worm, string mountId)
        {
            var art = ArtCatalog.SliceMount(mountId);
            var img = Ui.Panel(worm.Sprite, Color.white, "Mount");
            img.sprite = art ?? HudArt.Mount();
            img.preserveAspect = true;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            float w = worm.Sprite.sizeDelta.x;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w * 1.1f, w * 0.55f);
            rt.anchoredPosition = new Vector2(-w * 0.03f, w * 0.1f);
            float drop = MountDrop * w;
            foreach (var name in new[] { "HpText", "HpHolder", "ShieldHolder", "RageHolder" })
            {
                var part = (RectTransform)worm.Root.Find(name);
                if (part != null) part.anchoredPosition -= new Vector2(0, drop);
            }
            if (worm.Chips != null) worm.Chips.anchoredPosition -= new Vector2(0, drop);
        }

        /// <summary>How far (share of the worm sprite's width) the worm's stack moves down under its mount.</summary>
        public const float MountDrop = 0.28f;

        // ---- the stack under a fighter (card px from the card's bottom, at scale 1; board "New · battle") --------------------

        /// <summary>Size of a status icon under a fighter (board 53) and the gap between icons.</summary>
        public const float StatusChipSize = 56, StatusGap = 14;

        private FighterView Fighter(BattleFighterInfo f, int index, int count)
        {
            var v = new FighterView { Info = f, HpNow = f.StartHp, ShieldNow = f.StartShield, RageNow = f.StartRage };
            var root = Ui.Rect(_stage, "Fighter:" + f.Index);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = ViewSize;
            // the worm always takes the hero spot; anything else that ends up last on the player side (a summon) a companion spot
            bool playerSide = f.Side == BattleSide.Player;
            bool sidekick = playerSide && (f.Role == CombatRole.Alien || f.Role == CombatRole.Summon);
            bool worm = f.Role == CombatRole.Worm;
            v.Home = worm ? StagePosition(BattleSide.Player, 0, 1)
                : sidekick ? CompanionPosition(SidekickOrdinal(f))
                : playerSide && index == count - 1 ? CompanionPosition(index) : StagePosition(f.Side, index, count);
            root.anchoredPosition = v.Home;
            if (sidekick) root.localScale = Vector3.one * SidekickScale;
            else if (playerSide && !worm) root.localScale = Vector3.one * 0.62f;
            else if (f.Side != BattleSide.Player) root.localScale = Vector3.one * EnemyScale(count);
            v.Root = root;
            v.Group = root.gameObject.AddComponent<CanvasGroup>();
            bool player = f.Side == BattleSide.Player;
            bool boss = f.Role == CombatRole.Boss;

            // the board's stack, centred under the fighter's art: the worm's HP number over HP, shield and rage bars; an
            // enemy's big HP number over its HP bar (its shield shows only while it has one; no rage bar on the board)
            float barW = worm ? BarWidth : EnemyBarWidth;
            var hpText = KitV2.Text(root, Fmt.Num(f.StartHp), 0, 0, ViewSize.x, 10, worm ? 30 : 48, Color.white, TextAnchor.MiddleCenter, "HpText");
            hpText.supportRichText = true;
            PinBottom(hpText.rectTransform, worm ? 170 : 116, worm ? 32 : 56, 1f);
            var hpBg = Ui.Rect(root, "HpHolder");
            PinBottom(hpBg, worm ? 132 : 66, worm ? 35 : 40, barW);
            var hpFill = KitV2.Bar(hpBg, "hp_red", KitV2.HpRed, 13.3f, 5, 5);
            v.Hp = new UiBar(hpFill, hpText);
            if (boss)
            {
                // the boss frame: a gold rim round its HP bar and a BOSS tag over its number
                UiShapes.Outline(hpBg, KitV2.DiamondGold, 15, 5, "BossFrame");
                var tag = KitV2.Box(root, null, 0, 0, 112, 40, Theme.Hex(0xD93B3B), 12, 4, KitV2.Ink, null, "BossTag").rectTransform;
                PinBottom(tag, 176, 40, 112 / ViewSize.x);
                KitV2.Text(tag, "BOSS", 0, 0, 112, 40, 28, KitV2.DiamondGold, TextAnchor.MiddleCenter, "Label");
            }
            var shieldBg = Ui.Rect(root, "ShieldHolder");
            PinBottom(shieldBg, worm ? 98 : 100, worm ? 29 : 14, barW); // an enemy's shield: a strip over its HP frame's top edge
            v.Shield = new UiBar(KitV2.Bar(shieldBg, "hp_shield_blue", KitV2.ShieldBlue, 13.3f, 5, 5), null);
            var rageBg = Ui.Rect(root, "RageHolder");
            PinBottom(rageBg, 68, 24, barW);
            v.Rage = new UiBar(KitV2.Bar(rageBg, "hp_rage_yellow", KitV2.RageYellow, 13.3f, 5, 5), null);
            if (!worm) rageBg.gameObject.SetActive(false);
            if (!f.Targetable) { hpText.gameObject.SetActive(false); hpBg.gameObject.SetActive(false); shieldBg.gameObject.SetActive(false); rageBg.gameObject.SetActive(false); }

            Vector2 size;
            float radius;
            switch (f.Role)
            {
                case CombatRole.Worm: size = new Vector2(250, 108); radius = 54; break;
                case CombatRole.Boss: size = new Vector2(196, 172); radius = 44; break;
                case CombatRole.Elite: size = new Vector2(164, 156); radius = 40; break;
                case CombatRole.Alien: case CombatRole.Mount: case CombatRole.Summon: size = new Vector2(124, 124); radius = 34; break;
                default: size = new Vector2(146, 146); radius = 38; break;
            }
            var color = RoleColor(f.Role);
            // the fighter's art (ArtCatalog): the hero worm, an alien by type id, an enemy by def id (bosses / elites without
            // their own art: a family sprite, scaled up; playtest 0.1.325: no coloured glow under it); without art a rounded
            // token with its letter
            var hero = FighterSprite(f, Content, out bool family, out float spriteSize);
            Image body;
            if (hero != null)
            {
                body = Ui.Panel(root, Color.white, "Sprite");
                body.sprite = hero;
                body.preserveAspect = true;
                size = new Vector2(spriteSize, spriteSize);
            }
            else if (f.Role == CombatRole.Mount) body = Ui.Panel(root, new Color(1, 1, 1, 0), "Sprite"); // drawn under the worm (SeatOnMount)
            else body = Ui.RoundPanel(root, worm ? Theme.Worm : Theme.Shade(color, 0.5f), radius, "Sprite");
            body.raycastTarget = false;
            var srt = body.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(SpriteAnchorX(player), 0);
            srt.pivot = new Vector2(0.5f, 0);
            srt.sizeDelta = size;
            srt.anchoredPosition = new Vector2(0, SpriteBase);
            if (hero == null && f.Role != CombatRole.Mount) UiShapes.Outline(srt, worm ? Theme.WormLight : color, radius, 6);
            v.Sprite = srt;
            v.SpriteImage = body;
            // the worm's art faces left but the enemies stand on the right: it starts turned to them and follows its target (Face)
            v.MirrorsWorm = worm && hero != null;
            if (v.MirrorsWorm) SetFlip(v, WormFacing.Flip(StagePosition(BattleSide.Player, 0, 1).x, StagePosition(BattleSide.Enemy, 0, 1).x, false));
            v.SpriteColor = body.color;
            if (hero != null && f.Role != CombatRole.Mount) { v.Anim = UiCreature.Attach(body); if (v.Anim != null) v.Anim.AimScreen = player ? 1 : -1; }
            v.FlashColor = hero != null ? new Color(1f, 0.45f, 0.42f) : Color.white; // a sprite cannot get whiter: it flashes red
            if (!worm && f.Role != CombatRole.Mount && hero == null)
            {
                var letter = Ui.Display(srt, RoleLetter(f.Role), 44, Color.Lerp(color, Color.white, 0.6f), TextAnchor.MiddleCenter);
                Ui.Stretch(letter.rectTransform);
            }
            else if (hero == null && worm)
            {
                var eye = Ui.Dot(srt, Theme.OnAccent, 16, "Eye");
                eye.rectTransform.anchorMin = eye.rectTransform.anchorMax = new Vector2(0.82f, 0.62f);
                eye.rectTransform.sizeDelta = new Vector2(16, 16);
            }
            // the status icons under the bars (board: icon + its count), centred like the bars
            var chips = Ui.Rect(root, "Status");
            chips.anchorMin = new Vector2(0, 0); chips.anchorMax = new Vector2(1, 0); chips.pivot = new Vector2(0.5f, 1);
            chips.sizeDelta = new Vector2(0, StatusChipSize); chips.anchoredPosition = new Vector2(0, StatusChipSize + 2);
            var h = chips.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = StatusGap; h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            v.Chips = chips;
            // #30: a shrunk fighter keeps a readable stack (each part grows about its own bottom centre)
            float shrink = root.localScale.x;
            if (shrink > 0 && shrink < MinBarScale)
                foreach (var part in new[] { (RectTransform)hpText.transform, hpBg, shieldBg, rageBg, chips }) part.localScale = Vector3.one * (MinBarScale / shrink);
            if (f.SummonedBy >= 0) { v.Visible = false; root.gameObject.SetActive(false); }
            return v;
        }

        /// <summary>The fighter's place among its side's sidekicks (aliens first, then drones in summon order).</summary>
        private int SidekickOrdinal(BattleFighterInfo f)
        {
            int k = 0;
            foreach (var o in _battle.Fighters)
            {
                if (o == f) return k;
                if (o.Side == f.Side && (o.Role == CombatRole.Alien || o.Role == CombatRole.Summon)) k++;
            }
            return k;
        }

        /// <summary>Drawn size of a regular enemy / alien sprite on the stage (bosses and elites: × the ArtCatalog role scales).</summary>
        public const float EnemySpriteSize = 350, AlienSpriteSize = 220, WormSpriteSize = 450;

        /// <summary>
        /// The art of a fighter (pure lookup through ArtCatalog; tests): the worm → the hero sprite, an alien → its sprite by
        /// type id, an enemy-side fighter → its own sprite by def id, else (bosses, elites, summons) a sprite of its family
        /// (<paramref name="family"/>). Bosses and elites are drawn bigger. Null = the lettered box.
        /// </summary>
        public static Sprite FighterSprite(BattleFighterInfo f, ContentDb content, out bool family, out float size)
        {
            family = false;
            size = EnemySpriteSize;
            if (f == null) return null;
            switch (f.Role)
            {
                case CombatRole.Worm: size = WormSpriteSize; return ArtCatalog.Hero();
                case CombatRole.Alien:
                {
                    // its art, else a drawn critter in its synergy's colour (never the lettered box: sidekicks act on screen)
                    size = AlienSpriteSize;
                    var fam = content?.AlienType(f.Id)?.Family ?? SkillFamily.AlienSynergy;
                    return ArtCatalog.Alien(f.Id) ?? (f.Side == BattleSide.Player ? HudArt.AlienCritter(Theme.FamilyColor(fam)) : null);
                }
                case CombatRole.Mount: return null;
                case CombatRole.Summon when f.Side == BattleSide.Player:
                    // a battle-scoped sidekick (drone): its art, else the drawn drone
                    size = AlienSpriteSize * 0.8f;
                    return ArtCatalog.Sidekick(f.Id) ?? HudArt.Drone();
            }
            if (f.Side != BattleSide.Enemy) return null;
            var s = ArtCatalog.EnemyOrFamily(f.Id, content, out family);
            size = EnemySpriteSize * (f.Role == CombatRole.Boss ? ArtCatalog.BossFamilyScale : f.Role == CombatRole.Elite ? ArtCatalog.EliteFamilyScale : 1);
            if (f.Role == CombatRole.Summon) size = AlienSpriteSize;
            return s;
        }

        /// <summary>
        /// Horizontal anchor (fraction of the fighter card) of the fighter's art: the card's centre on both sides (the board's
        /// stack sits centred under each fighter). Its bars are centred on the same line.
        /// </summary>
        public static float SpriteAnchorX(bool player) => 0.5f;

        /// <summary>Width of the worm's bars (board 453 px) and an enemy's (board 341 px) as fractions of the fighter card.</summary>
        public const float BarWidth = 453 / 480f, EnemyBarWidth = 341 / 480f;

        /// <summary>A part of the stack under a fighter: <paramref name="width"/> of the card wide, centred, <paramref name="y"/> over the card's bottom.</summary>
        private static void PinBottom(RectTransform rt, float y, float height, float width)
        {
            rt.anchorMin = new Vector2(0.5f - width / 2, 0); rt.anchorMax = new Vector2(0.5f + width / 2, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(0, height); rt.anchoredPosition = new Vector2(0, y);
        }

        private static float Frac(double a, double b) => b > 0 ? (float)(a / b) : 0;

        public static Color RoleColor(CombatRole r)
        {
            switch (r)
            {
                case CombatRole.Worm: return Theme.Worm;
                case CombatRole.Alien: return Theme.Hex(0x7BE08A);
                case CombatRole.Mount: return Theme.Hex(0x7FB2FF);
                case CombatRole.Summon: return Theme.Hex(0x8BE04E);
                case CombatRole.Elite: return Theme.ValenceColor(Valence.Elite);
                case CombatRole.Boss: return Theme.ValenceColor(Valence.Boss);
                default: return Theme.Bad;
            }
        }

        private static string RoleLetter(CombatRole r)
        {
            switch (r)
            {
                case CombatRole.Worm: return "W";
                case CombatRole.Alien: return "A";
                case CombatRole.Mount: return "M";
                case CombatRole.Summon: return "S";
                case CombatRole.Elite: return "EL";
                case CombatRole.Boss: return "B";
                default: return "E";
            }
        }

        /// <summary>The letter on a status icon: H(eat), R(adiation), B(leed), C(hill), F(reeze), S(hock / stun) …</summary>
        public static string StatusGlyph(StatusId s) => SkillText.StatusName(s).Substring(0, 1);

        public static Color StatusColor(StatusId s)
        {
            switch (s)
            {
                case StatusId.Burn:
                case StatusId.Heat: return Theme.Hex(0xFF8A2A);
                case StatusId.Chill: return Theme.Hex(0x8FD8FF);
                case StatusId.Freeze: return Theme.Premium;
                case StatusId.Poison: return Theme.Hex(0x8BE04E);
                case StatusId.Bleed: return Theme.Bad;
                case StatusId.Stun: return Theme.TextDim;
                case StatusId.Shock: return Theme.Hex(0xFFE03D);
                case StatusId.Charm: return Theme.Hex(0xFF9FD2);
                case StatusId.Exposed: return Theme.Hex(0xE0506E);
                case StatusId.Stagger: return Theme.Hex(0xD9A066);
                case StatusId.Delay: return Theme.Hex(0x7FE0D0);
                default: return Theme.Purple;
            }
        }

        // ---- playback ----------------------------------------------------------------------------

        public override void Frame(float dt)
        {
            if (_battle == null || Root == null || Paused) return;
            _fires.Tick(dt); // skill tiles play their queued fires, also after the last entry
            if (_autoClose >= 0)
            {
                // a won run battle goes straight back to the board after its VICTORY beat (playtest 0.1.308: no Continue gate)
                _autoClose -= dt;
                if (_autoClose <= 0) { _autoClose = -1; Close(); }
                return;
            }
            if (Finished) return;
            if (Instant) { PlayAll(); return; }
            _timer += dt;
            while (_timer >= StepSeconds && !Finished)
            {
                _timer -= StepSeconds;
                Step(true);
            }
        }

        /// <summary>Apply every remaining log entry without animation (tests, skip-to-end after the fight).</summary>
        public void PlayAll()
        {
            while (!Finished) Step(false);
        }

        /// <summary>Index of the next log entry to play (screenshots).</summary>
        public int NextIndex => _next;

        /// <summary>Apply the next N entries (screenshots mid-fight).</summary>
        public void PlaySteps(int n)
        {
            for (int i = 0; i < n && !Finished; i++) Step(false);
        }

        private static bool IsAttack(BattleAction a) =>
            a == BattleAction.Basic || a == BattleAction.Combo || a == BattleAction.Counter || a == BattleAction.Skill || a == BattleAction.Rage;

        private void Step(bool animate)
        {
            var e = _battle.Log[_next++];
            if (_round != null) _round.text = RoundLabel(Math.Max(1, e.Round));
            if (e.Round != _roundNow) SetMountHud(e.Round);
            _roundNow = e.Round;
            var actor = View(e.Actor);
            var target = View(e.Target) ?? actor;
            double rageMax = Content.Combat.RageMax;
            bool attack = IsAttack(e.Action);
            if (attack && actor != null && target != null && target != actor) Face(actor, target);
            if (animate && attack && actor != null && target != null && target != actor) Lunge(actor, target, e.Action == BattleAction.Rage);
            switch (e.Action)
            {
                case BattleAction.Basic:
                case BattleAction.Combo:
                case BattleAction.Counter:
                case BattleAction.Skill:
                case BattleAction.Rage:
                case BattleAction.Execute:
                case BattleAction.StatusTick:
                    if (target == null) break;
                    if (e.Dodge) { Popup(target, "DODGE", Theme.Premium, animate, 40); if (animate) Dodge(target); break; }
                    if (animate) Hit(target, e.Crit);
                    // the board's big outlined hit numbers over the target (crits bigger, gold, with "CRIT")
                    if (e.Crit) Popup(target, Fmt.Num(e.Amount) + "!", Theme.Gold, animate, CritNumberSize, "CRIT");
                    else Popup(target, "-" + Fmt.Num(e.Amount), e.Action == BattleAction.StatusTick ? StatusColor(e.Status) : target.Info.Side == BattleSide.Player ? Theme.Hex(0xFF9B3D) : KitV2.HitBlue, animate,
                        e.Action == BattleAction.StatusTick ? HitNumberSize * 2 / 3 : HitNumberSize);
                    break;
                case BattleAction.Heal:
                case BattleAction.Regen:
                case BattleAction.LifeSteal:
                    if (target == null) break;
                    Popup(target, "+" + Fmt.Num(e.Amount), Theme.Good, animate, 46);
                    break;
                case BattleAction.Shield:
                    if (target == null) break;
                    Popup(target, "+" + Fmt.Num(e.Amount) + " SHIELD", Theme.Premium, animate, 36);
                    break;
                case BattleAction.StatusApplied:
                    if (target == null) break;
                    target.Statuses[e.Status] = Math.Max(1, e.Stacks);
                    // a conversion (Fallout Wounds: consumed Bleed becomes Radiation) reads as one
                    if (e.SkillId == "mythic_fallout_wounds") Popup(target, "BLEED → RADIATION", StatusColor(e.Status), animate, 36);
                    else Popup(target, SkillText.StatusName(e.Status).ToUpperInvariant(), StatusColor(e.Status), animate, 34);
                    break;
                case BattleAction.StatusBlocked:
                    if (target != null) Popup(target, SkillText.StatusName(e.Status).ToUpperInvariant() + " LOCKED", StatusColor(e.Status), animate, 32, e.Stacks + " turns");
                    break;
                case BattleAction.CheatDeath:
                    if (target != null) Popup(target, "LAST WORD", Theme.Gold, animate, 52, "1 HP");
                    break;
                case BattleAction.Echo:
                    if (actor != null) Popup(actor, "ECHO", Theme.Hex(0xB89CFF), animate, 40);
                    break;
                case BattleAction.Staggered:
                    if (actor != null) Popup(actor, "STAGGERED", StatusColor(StatusId.Stagger), animate, 40, "rage attack lost");
                    break;
                case BattleAction.Sacrifice:
                    if (actor != null) { Die(actor, animate); Popup(actor, "SACRIFICE", Theme.Gold, animate, 40); }
                    break;
                case BattleAction.Loop:
                    if (target == null) break;
                    if (e.Actor == e.Target) { Popup(target, "LOOP", Theme.Hex(0xB89CFF), animate, 56, "rewind to round " + e.Amount); break; }
                    if (e.Stacks > 0 && !target.Visible) { target.Visible = true; target.Root.gameObject.SetActive(true); target.Group.alpha = 1; }
                    target.Statuses.Clear();
                    Popup(target, "REWIND", Theme.Hex(0xB89CFF), animate, 30);
                    break;
                case BattleAction.StatusRemoved:
                    if (target == null) break;
                    if (e.Stacks <= 0) target.Statuses.Remove(e.Status); else target.Statuses[e.Status] = e.Stacks;
                    if (e.Status == StatusId.Heat) Popup(target, "COOLED", StatusColor(StatusId.Chill), animate, 32);
                    break;
                case BattleAction.Supernova:
                    if (target == null) break;
                    if (e.Amount == 0 && e.Actor == e.Target)
                    {
                        // the marker: the heated fighter blows; its Heat is gone
                        target.Statuses.Remove(StatusId.Heat);
                        Nova(target, animate);
                        SupernovaCount++;
                        break;
                    }
                    if (animate) Hit(target, true);
                    Popup(target, "-" + Fmt.Num(e.Amount), target.Info.Side == BattleSide.Player ? Theme.Hex(0xFF5A3A) : Theme.Hex(0xFFB03A), animate, 66, target.Info.Side == BattleSide.Player ? "RECOIL" : "NOVA");
                    break;
                case BattleAction.StatusExpired:
                    target?.Statuses.Remove(e.Status);
                    break;
                case BattleAction.StatusResisted:
                    if (target != null) Popup(target, "RESIST", Theme.TextDim, animate, 32);
                    break;
                case BattleAction.Buff:
                    // an extra sidekick action (Command) is credited with a 0-buff on the sidekick: its own act entry shows it
                    if (target != null && target.Sidekick && e.Amount == 0 && actor != target) break;
                    if (target != null) Popup(target, "BUFF", Theme.Good, animate, 32);
                    if (target != null && target == _hero) TrackBuff(e);
                    break;
                case BattleAction.SidekickAct:
                    if (actor == null) break;
                    SidekickActs++;
                    if (animate) Hop(actor);
                    AbilityFloat(actor, AbilityName(e.SkillId, actor.Info.Name), e.Stacks > 0, animate);
                    break;
                case BattleAction.Guard:
                    if (target == null) break;
                    target.Guards = e.Stacks;
                    target.GuardPct = e.Amount; // an alien's Guard carries its %, a Block 0
                    Popup(target, "GUARD", Theme.Premium, animate, 34, e.Amount > 0 ? "next hit −" + e.Amount + "%" : null);
                    if (animate && actor != null) Hop(actor);
                    break;
                case BattleAction.Guarded:
                    // aliens pass: the guard softens the hit, which still lands (the hit's own entry follows)
                    if (target == null) break;
                    target.Guards = e.Stacks;
                    if (e.Stacks <= 0) target.GuardPct = 0;
                    Guardeds++;
                    if (animate && actor != null && actor != target) Intercept(actor, target);
                    Popup(target, "GUARDED", Theme.Premium, animate, 44, "−" + Fmt.Num(e.Amount) + " softened");
                    break;
                case BattleAction.Block:
                    if (target == null) break;
                    target.Guards = e.Stacks;
                    Blocks++;
                    if (animate && actor != null) Intercept(actor, target);
                    Popup(target, "BLOCKED", Theme.Premium, animate, 56, Fmt.Num(e.Amount) + " stopped");
                    break;
                case BattleAction.SkipTurn:
                    if (actor != null)
                        Popup(actor, actor.Statuses.ContainsKey(StatusId.Delay) ? "DELAYED" : actor.Statuses.ContainsKey(StatusId.Freeze) ? "FROZEN" : "STUNNED", Theme.TextDim, animate, 32);
                    break;
                case BattleAction.Summon:
                    if (target != null && !target.Visible)
                    {
                        target.Visible = true;
                        target.Root.gameObject.SetActive(true);
                        var g = target.Group;
                        if (animate) UiTweens.Play(target.Root.gameObject, "fade", Beat, t => g.alpha = t);
                    }
                    break;
                case BattleAction.Death:
                    if (target != null) { Die(target, animate); Popup(target, "KO", Theme.TextDim, animate, 52); }
                    break;
                // ---- hero and mount kits ----
                case BattleAction.Volley:
                {
                    // the mount fires on its reload: the shot's name over the worm it carries, a hop of the seated worm
                    Volleys++;
                    var name = Content.Skill(e.SkillId)?.Name ?? "Shot";
                    var seat = _hero ?? actor;
                    if (animate && seat != null) Hop(seat);
                    if (seat != null) AbilityFloat(seat, name.ToUpperInvariant(), e.Amount > 0, animate);
                    if (e.Amount == 0) { _mountReload = Math.Max(1, e.Stacks); _mountNext = e.Round + _mountReload; _mountShot = name; }
                    SetMountHud(e.Round);
                    break;
                }
                case BattleAction.Ability:
                {
                    Abilities++;
                    var name = Content.Skill(e.SkillId)?.Name ?? "Ability";
                    if (target != null) { Popup(target, name.ToUpperInvariant() + "!", Theme.Gold, animate, 52, "hero ability"); if (animate) Hop(target); }
                    if (_kitHero != null) { _kitHero.text = name + "!"; KitHud.Add(_kitHero.text); }
                    break;
                }
                case BattleAction.KitCounter:
                    if (actor != null && actor.Info.Role == CombatRole.Mount) { SetMountHud(e.Round, e); break; }
                    if (_kitHero != null) { _kitHero.text = KitCounterText(e); KitHud.Add(_kitHero.text); }
                    break;
                case BattleAction.Mark:
                    if (target != null) Popup(target, "MARKED", Theme.Hex(0x7FB2FF), animate, 34, "Spotlight");
                    break;
            }
            Sync(e, actor, View(e.Target));
            Refresh(actor, rageMax, animate);
            if (target != actor) Refresh(target, rageMax, animate);
            RefreshHero();
            Feed(e, actor, target);
            OnSkillFire(e, animate);
            if (animate || Finished) TurnOrder();
            if (Finished) { End(); return; }
            // a Supernova hits every fighter at once: its hit entries play in the same beat as the burst
            if (e.Action == BattleAction.Supernova && _battle.Log[_next].Action == BattleAction.Supernova && _battle.Log[_next].Amount > 0) Step(animate);
        }

        // ---- animations (play mode; complete at once in EditMode / Instant) -------------------------------

        /// <summary>The worm's mirror state (tests): true = turned right, towards the enemies.</summary>
        public bool WormFlipped => _hero != null && _hero.Flip;

        private static void SetFlip(FighterView v, bool flip)
        {
            v.Flip = flip;
            if (v.Sprite != null) v.Sprite.localScale = new Vector3(WormFacing.ScaleX(flip), 1, 1); // about the sprite's own pivot: ground contact stays
        }

        /// <summary>The worm turns to its target (left or right of it on the stage); the mount seated under it turns with it.</summary>
        private static void Face(FighterView actor, FighterView target)
        {
            if (!actor.MirrorsWorm || target == null) return;
            SetFlip(actor, WormFacing.Flip(actor.Root.anchoredPosition.x, target.Root.anchoredPosition.x, actor.Flip));
        }

        private void Lunge(FighterView actor, FighterView target, bool rage)
        {
            Clip(actor, CreatureClip.Attack, target);
            var from = actor.Home;
            var delta = target.Root.anchoredPosition - actor.Root.anchoredPosition;
            var reach = Vector2.ClampMagnitude(delta * (rage ? 0.55f : 0.4f), rage ? 300 : 200);
            var rt = actor.Root;
            float beat = Beat;
            actor.Root.SetAsLastSibling();
            UiTweens.Play(rt.gameObject, "move", beat, t =>
            {
                // quick out, slower back
                float k = t < 0.35f ? t / 0.35f : 1 - (t - 0.35f) / 0.65f;
                k = Mathf.Sin(k * Mathf.PI / 2);
                rt.anchoredPosition = from + reach * k;
            }, () => rt.anchoredPosition = from);
        }

        private void Hit(FighterView v, bool crit)
        {
            Clip(v, CreatureClip.Hit);
            var sprite = v.Sprite;
            var img = v.SpriteImage;
            var baseColor = v.SpriteColor;
            var home = new Vector2(0, SpriteBase);
            float amp = crit ? 18 : 10;
            UiTweens.Play(sprite.gameObject, "hit", Beat * 1.1f, t =>
            {
                float wait = 0.3f; // the blow lands mid-lunge
                float k = t < wait ? 0 : (t - wait) / (1 - wait);
                sprite.anchoredPosition = home + new Vector2(amp * Mathf.Sin(k * Mathf.PI * 6) * (1 - k), 0);
                if (!v.Dead) img.color = Color.Lerp(baseColor, v.FlashColor, k <= 0 ? 0 : (1 - k) * 0.85f);
            }, () => { sprite.anchoredPosition = home; if (!v.Dead) img.color = baseColor; });
        }

        private void Dodge(FighterView v)
        {
            Clip(v, CreatureClip.Move, null, CreatureEvents.HopSeconds * 0.6f);
            var sprite = v.Sprite;
            var home = new Vector2(0, SpriteBase);
            float dir = v.Info.Side == BattleSide.Player ? -1 : 1;
            UiTweens.Play(sprite.gameObject, "hit", Beat, t => sprite.anchoredPosition = home + new Vector2(dir * 50 * Mathf.Sin(t * Mathf.PI), 0),
                () => sprite.anchoredPosition = home);
        }

        /// <summary>A sidekick's turn: a small hop and a bright flash on its sprite.</summary>
        private void Hop(FighterView v)
        {
            Clip(v, CreatureClip.Move, null, CreatureEvents.HopSeconds);
            var sprite = v.Sprite;
            var img = v.SpriteImage;
            var baseColor = v.SpriteColor;
            var home = new Vector2(0, SpriteBase);
            UiTweens.Play(sprite.gameObject, "hop", Beat * 1.2f, t =>
            {
                sprite.anchoredPosition = home + new Vector2(0, 46 * Mathf.Sin(t * Mathf.PI));
                if (!v.Dead) img.color = Color.Lerp(baseColor, new Color(1f, 1f, 0.75f), 0.7f * Mathf.Sin(t * Mathf.PI));
            }, () => { sprite.anchoredPosition = home; if (!v.Dead) img.color = baseColor; });
        }

        /// <summary>A blocking sidekick dashes in front of the guarded worm (towards the enemies) and back.</summary>
        private void Intercept(FighterView blocker, FighterView guarded)
        {
            var rt = blocker.Root;
            var from = blocker.Home;
            var to = guarded.Root.anchoredPosition + new Vector2(170, -10);
            float beat = Beat * 1.6f;
            rt.SetAsLastSibling();
            UiTweens.Play(rt.gameObject, "move", beat, t =>
            {
                float k = t < 0.3f ? t / 0.3f : t < 0.7f ? 1 : 1 - (t - 0.7f) / 0.3f;
                rt.anchoredPosition = Vector2.Lerp(from, to, Mathf.SmoothStep(0, 1, k));
            }, () => rt.anchoredPosition = from);
            Hit(blocker, false);
        }

        /// <summary>The ability's name floating up from a sidekick ("Block", "Kindle"); extra actions (Command) say so.</summary>
        private void AbilityFloat(FighterView v, string name, bool extra, bool animate)
        {
            AbilityFloats.Add(name);
            if (!animate || Instant || !Application.isPlaying || _stage == null || string.IsNullOrEmpty(name)) return;
            var holder = Ui.Chip(_fx ?? _stage, name, new Color(0.06f, 0.16f, 0.1f, 0.92f), Color.Lerp(RoleColor(v.Info.Role), Color.white, 0.35f), 30, 56,
                RoleColor(v.Info.Role), "AbilityFloat");
            holder.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
            holder.pivot = new Vector2(0, 0.5f);
            // BT6: in a band along the arena's top edge (lanes for banners at once), above the units, never over one; #31: the
            // band starts below the NEXT plate (it floated up under it)
            var start = new Vector2(Mathf.Clamp(v.Root.anchoredPosition.x - 60, -520, 120), PopupCeiling - 20 - (_abilityLane++ % 3) * 64);
            holder.anchoredPosition = start;
            Text cap = null;
            if (extra)
            {
                cap = Ui.Label(holder, "EXTRA", 20, Theme.Gold, TextAnchor.LowerLeft, "Extra");
                cap.fontStyle = FontStyle.Bold;
                cap.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                cap.rectTransform.anchorMin = new Vector2(0, 1); cap.rectTransform.anchorMax = new Vector2(1, 1); cap.rectTransform.pivot = new Vector2(0, 0);
                cap.rectTransform.sizeDelta = new Vector2(0, 26); cap.rectTransform.anchoredPosition = new Vector2(14, 0);
            }
            var group = holder.gameObject.AddComponent<CanvasGroup>();
            float life = Mathf.Max(0.8f, StepSeconds * 2.2f);
            UiTweens.Play(holder.gameObject, "float", life, t =>
            {
                holder.localScale = Vector3.one * (t < 0.12f ? Mathf.Lerp(0.6f, 1.08f, t / 0.12f) : t < 0.2f ? Mathf.Lerp(1.08f, 1f, (t - 0.12f) / 0.08f) : 1);
                holder.anchoredPosition = start + new Vector2(24 * RunFx.Ease(t), 34 * RunFx.Ease(t));
                group.alpha = t < 0.75f ? 1 : 1 - (t - 0.75f) / 0.25f;
            }, () => { _abilityLane = Math.Max(0, _abilityLane - 1); Ui.DestroyGo(holder.gameObject); });
        }

        private int _abilityLane;

        /// <summary>
        /// A sidekick ability's short name: "Pebblehorn Block 2" → "Block" (the species and the star tier are on the sidekick
        /// already), a drone's "Drone Zap" stays.
        /// </summary>
        public static string AbilityName(string skillId, string sidekickName, ContentDb content = null)
        {
            var def = (content ?? ContentDb.Default).Skill(skillId);
            string n = def?.Name ?? skillId ?? "";
            if (!string.IsNullOrEmpty(sidekickName) && n.StartsWith(sidekickName + " ", StringComparison.Ordinal) && n.Length > sidekickName.Length + 1 && !n.EndsWith(" Zap", StringComparison.Ordinal))
                n = n.Substring(sidekickName.Length + 1);
            int sp = n.LastIndexOf(' ');
            if (sp > 0 && int.TryParse(n.Substring(sp + 1), out _)) n = n.Substring(0, sp);
            return n;
        }

        /// <summary>Sidekick actions / intercepted hits played so far, and the ability names floated (tests, screenshots).</summary>
        public int SidekickActs { get; private set; }
        public int Blocks { get; private set; }
        /// <summary>Hits an alien's Guard softened so far (aliens pass; tests, screenshots).</summary>
        public int Guardeds { get; private set; }
        /// <summary>The % of the guard on fighter <paramref name="index"/> (0 = none or a Block) (tests).</summary>
        public long GuardPctOn(int index) => View(index)?.GuardPct ?? 0;
        public readonly List<string> AbilityFloats = new List<string>();
        /// <summary>Actors of the turn-order pips (fighter indices; tests).</summary>
        public readonly List<int> NextActors = new List<int>();
        /// <summary>Guards the fighter's sidekicks still hold (tests).</summary>
        public int GuardsOn(int index) => View(index)?.Guards ?? 0;

        private void Die(FighterView v, bool animate)
        {
            if (animate && !Instant && Application.isPlaying && v.Info.Side != BattleSide.Player && _fireLayer != null && _hud != null)
            {
                // EXP gems and coins burst out of the beaten enemy and fly to the HUD
                var from = LayerPoint(v.Sprite);
                RunFx.Burst(_fireLayer, from, LayerPoint(_hud.XpFill.rectTransform), new Color32(0x5C, 0xD0, 0x6A, 255), 8, 30 * _scale);
                if (_hud.CoinsChip != null) RunFx.Burst(_fireLayer, from, LayerPoint(_hud.CoinsChip), new Color32(0xB8, 0xD8, 0xF6, 255), 6, 30 * _scale, 0.15f);
            }
            v.Dead = true;
            Clip(v, CreatureClip.Death);
            v.SpriteImage.color = Theme.Locked;
            var g = v.Group;
            if (!animate) { g.alpha = 0.35f; return; }
            UiTweens.Play(v.Root.gameObject, "fade", Beat * 1.6f, t => g.alpha = Mathf.Lerp(1, 0.35f, t));
        }

        /// <summary>A rect's centre in the fire layer's top-left anchored coordinates (particles flying to the HUD).</summary>
        private Vector2 LayerPoint(RectTransform target)
        {
            if (_fireLayer == null || target == null) return Vector2.zero;
            var world = target.TransformPoint(target.rect.center);
            var local = (Vector2)_fireLayer.InverseTransformPoint(world);
            return local - new Vector2(_fireLayer.rect.xMin, _fireLayer.rect.yMax);
        }

        private void Popup(FighterView v, string text, Color color, bool animate, int size, string caption = null)
        {
            if (!animate || v == null || Instant || !Application.isPlaying) return;
            var holder = Ui.Rect(_fx ?? _stage, "Popup");
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
            size = (int)(size * 1.45f);
            holder.sizeDelta = new Vector2(700, size + (caption != null ? 40 : 0));
            // #31: over the target's own sprite (upper body; it spawned in a lane near the top, under the NEXT strip and on the
            // speed button, far from the fighter hit) with a small random offset; numbers at once stack upwards, never above
            // the NEXT plate
            float jitter = UnityEngine.Random.Range(-30f, 30f);
            int lane = v.LivePopups++;
            var start = PopupSpot(v.Root.anchoredPosition, v.Root.localScale.x, v.Sprite.anchorMin.x, v.Sprite.anchoredPosition.y, v.Sprite.sizeDelta.y, size, lane);
            start.x += jitter;
            _popupSeq++;
            // wide words ("SUPERNOVA!") stay on the stage
            float half = Mathf.Min(350, text.Length * size * 0.33f);
            start.x = Mathf.Clamp(start.x, -540 + half, 540 - half);
            holder.anchoredPosition = start;
            var label = SliceKit.Chunky(holder, text, size, color, TextAnchor.MiddleCenter);
            Ui.Stretch(label.rectTransform, 0, caption != null ? 36 : 0, 0, 0);
            var outline = label.GetComponent<Outline>();
            Text cap = null;
            if (caption != null)
            {
                cap = Ui.Label(holder, caption, 26, color, TextAnchor.UpperCenter);
                cap.fontStyle = FontStyle.Bold;
                cap.rectTransform.anchorMin = new Vector2(0, 1); cap.rectTransform.anchorMax = Vector2.one; cap.rectTransform.pivot = new Vector2(0.5f, 1);
                cap.rectTransform.sizeDelta = new Vector2(0, 36); cap.rectTransform.anchoredPosition = Vector2.zero;
            }
            float life = Mathf.Clamp(StepSeconds * 1.5f, 0.4f, 0.8f); // gone within 0.8 s
            UiTweens.Play(holder.gameObject, "float", life, t =>
            {
                float pop = t < 0.15f ? Mathf.Lerp(0.6f, 1.25f, t / 0.15f) : t < 0.25f ? Mathf.Lerp(1.25f, 1f, (t - 0.15f) / 0.1f) : 1f;
                holder.localScale = Vector3.one * pop;
                holder.anchoredPosition = start + new Vector2(0, 70 * t);
                float a = t < 0.7f ? 1 : 1 - (t - 0.7f) / 0.3f;
                label.color = new Color(color.r, color.g, color.b, a);
                if (cap != null) cap.color = new Color(color.r, color.g, color.b, a);
                if (outline != null) outline.effectColor = new Color(SliceKit.Ink.r, SliceKit.Ink.g, SliceKit.Ink.b, a);
            }, () => { v.LivePopups = Math.Max(0, v.LivePopups - 1); Ui.DestroyGo(holder.gameObject); });
        }

        /// <summary>Stage y (from the stage centre) of the NEXT plate's lower edge minus a margin: no combat text floats above it.</summary>
        public const float PopupCeiling = StageCenterY - KitRowY - 78 - 16;
        /// <summary>Popup sizes of a hit and a crit (× 1.45 drawn: the board's 144 px "-1,2K").</summary>
        public const int HitNumberSize = 98, CritNumberSize = 112;

        /// <summary>
        /// Where a hit number over a fighter starts (stage px, pure; tested): over the sprite's head, lane
        /// <paramref name="lane"/> stacked upwards, kept under <see cref="PopupCeiling"/> including its 70 px rise.
        /// </summary>
        public static Vector2 PopupSpot(Vector2 card, float scale, float anchorX, float spriteBase, float spriteH, int size, int lane)
        {
            float sx = (anchorX - 0.5f) * ViewSize.x * scale;
            float sy = (-ViewSize.y / 2 + spriteBase + spriteH * 0.92f) * scale; // the board: the number over the target's head
            // over the head, stacked upwards; a tall fighter right under the NEXT plate has no room above: its numbers
            // stack downwards over its body instead
            float top = PopupCeiling - 70 - size * 0.5f, step = size + 8;
            float y0 = Mathf.Min(card.y + sy, top);
            float y = y0 + lane * step <= top ? y0 + lane * step : y0 - lane * step * 0.75f;
            return new Vector2(card.x + sx, y);
        }

        /// <summary>Supernovas played so far (tests).</summary>
        public int SupernovaCount { get; private set; }

        /// <summary>Heat at or above this reads as dangerous (the chip turns red and pulses): 80% of the Supernova threshold.</summary>
        public static int HeatDangerAt(ContentDb c) =>
            c.Combat.Statuses.TryGetValue(StatusId.Heat, out var d) && d.BurstAtStacks > 0 ? Mathf.Max(1, Mathf.CeilToInt(d.BurstAtStacks * 0.8f)) : int.MaxValue;

        /// <summary>The Supernova burst: a flash over the stage and an expanding fire ring with a white core on the exploding fighter.</summary>
        private void Nova(FighterView v, bool animate)
        {
            if (!animate || v == null || Instant || !Application.isPlaying || _stage == null) return;
            var s = v.Sprite;
            float sx = (s.anchorMin.x - 0.5f) * ViewSize.x * v.Root.localScale.x;
            float sy = (-ViewSize.y / 2 + s.anchoredPosition.y + s.sizeDelta.y / 2) * v.Root.localScale.y;
            var at = v.Root.anchoredPosition + new Vector2(sx, sy);
            var flash = Ui.Panel(_stage, new Color(1f, 0.6f, 0.2f, 0.4f), "SupernovaFlash");
            flash.raycastTarget = false;
            Ui.Stretch(flash.rectTransform, -400, -400, -400, -400);
            var ring = Ui.Panel(_stage, new Color(1f, 0.5f, 0.12f, 0.95f), "SupernovaFx");
            ring.sprite = UiShapes.Circle;
            ring.raycastTarget = false;
            var rt = ring.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(240, 240);
            rt.anchoredPosition = at;
            var core = Ui.Panel(rt, new Color(1f, 0.95f, 0.75f, 1f), "Core");
            core.sprite = UiShapes.Circle;
            core.raycastTarget = false;
            Ui.Stretch(core.rectTransform, 50, 50, 50, 50);
            // the word beside the burst, towards the middle of the stage (the hit numbers stay readable on the fighters)
            var word = SliceKit.Chunky(_fx ?? _stage, "SUPERNOVA!", 92, Theme.Hex(0xFFB03A), TextAnchor.MiddleCenter, "SupernovaWord");
            word.raycastTarget = false;
            var wrt = word.rectTransform;
            wrt.anchorMin = wrt.anchorMax = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(700, 130);
            float wx = at.x > 0 ? Mathf.Clamp(at.x - 420, -190, 190) : Mathf.Clamp(at.x + 420, -190, 190);
            wrt.anchoredPosition = new Vector2(wx, at.y - 90);
            float life = Mathf.Max(0.7f, StepSeconds * 2.2f);
            UiTweens.Play(word.gameObject, "word", life * 1.4f, t =>
            {
                float pop = t < 0.12f ? Mathf.Lerp(0.5f, 1.2f, t / 0.12f) : t < 0.22f ? Mathf.Lerp(1.2f, 1f, (t - 0.12f) / 0.1f) : 1f;
                wrt.localScale = Vector3.one * pop;
                word.color = new Color(1f, 0.69f, 0.23f, t < 0.75f ? 1 : 1 - (t - 0.75f) / 0.25f);
            }, () => Ui.DestroyGo(word.gameObject));
            UiTweens.Play(ring.gameObject, "nova", life, t =>
            {
                float e = 1 - (1 - t) * (1 - t);
                rt.localScale = Vector3.one * Mathf.Lerp(0.25f, 5.2f, e);
                ring.color = new Color(1f, Mathf.Lerp(0.75f, 0.3f, t), 0.12f, 0.95f * (1 - t));
                core.color = new Color(1f, 0.95f, 0.75f, Mathf.Clamp01(1 - t * 1.6f));
                flash.color = new Color(1f, 0.6f, 0.2f, 0.45f * Mathf.Clamp01(1 - t * 2.2f));
            }, () => { Ui.DestroyGo(ring.gameObject); Ui.DestroyGo(flash.gameObject); });
        }

        /// <summary>Upcoming actors (next attack-like entries), the first one outlined.</summary>
        private void TurnOrder()
        {
            if (_order == null) return;
            Ui.Clear(_order);
            NextActors.Clear();
            // #34: nobody acts after the last action: no empty "NEXT" plate
            bool any = false;
            for (int i = _next; i < _battle.Log.Count && !any; i++)
            {
                var a = _battle.Log[i].Action;
                any = IsAttack(a) || a == BattleAction.SkipTurn || a == BattleAction.SidekickAct;
            }
            _order.gameObject.SetActive(any);
            if (_orderPlate != null) _orderPlate.SetActive(any);
            if (!any) return;
            var caption = Kit.Legible(Ui.Label(_order, "NEXT", 24, Theme.Cream));
            caption.fontStyle = FontStyle.Bold;
            caption.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Size(caption, 84, -1, 0);
            int last = -2, shown = 0;
            for (int i = _next; i < _battle.Log.Count && shown < 6; i++)
            {
                var e = _battle.Log[i];
                // attacks, skipped turns and sidekick actions (a sidekick's ability effects follow its act: not separate turns)
                if (!IsAttack(e.Action) && e.Action != BattleAction.SkipTurn && e.Action != BattleAction.SidekickAct) continue;
                if (e.Actor == last) continue;
                var v = View(e.Actor);
                if (v == null) continue;
                if (v.Sidekick && e.Action != BattleAction.SidekickAct) continue;
                last = e.Actor;
                bool kick = v.Sidekick;
                var sq = Ui.RoundPanel(_order, v.Info.Role == CombatRole.Worm ? Theme.Worm : RoleColor(v.Info.Role), kick ? 18 : 12, kick ? "Turn:Sidekick" : "Turn");
                Ui.Size(sq, kick ? 44 : 54, kick ? 44 : 54, 0);
                // a mini portrait (BT4): the fighter's own sprite in its role-coloured frame, never a letter
                var face = v.SpriteImage != null ? v.SpriteImage.sprite : null;
                if (face != null)
                {
                    var pic = Ui.Panel(sq.transform, Color.white, "Portrait");
                    pic.sprite = face;
                    pic.preserveAspect = true;
                    pic.raycastTarget = false;
                    Ui.Stretch(pic.rectTransform, 3, 3, 3, 3);
                }
                UiShapes.Outline(sq.rectTransform, shown == 0 ? Color.white : SliceKit.Ink, kick ? 22 : 12, shown == 0 ? 4 : 2);
                NextActors.Add(e.Actor);
                shown++;
            }
            Ui.Spacer(_order);
        }

        private void Feed(BattleLogEntry e, FighterView actor, FighterView target)
        {
            if (_feed == null) return;
            string who = actor?.Info.Name ?? "";
            string tgt = target != null && target != actor ? target.Info.Name : "";
            string what;
            switch (e.Action)
            {
                case BattleAction.Death: what = tgt.Length > 0 ? tgt + " is knocked out" : who + " is knocked out"; break;
                case BattleAction.StatusApplied: what = who + " applies " + SkillText.StatusName(e.Status) + (e.Stacks > 1 ? " ×" + e.Stacks : "") + " to " + tgt; break;
                case BattleAction.StatusTick: what = SkillText.StatusName(e.Status) + " deals " + Fmt.Num(e.Amount) + " to " + (target?.Info.Name ?? ""); break;
                case BattleAction.StatusRemoved: what = SkillText.StatusName(e.Status) + " −" + e.Amount + " on " + (target?.Info.Name ?? ""); break;
                case BattleAction.Supernova:
                    what = e.Amount == 0 && e.Actor == e.Target ? (target?.Info.Name ?? "") + " goes SUPERNOVA" + (e.SkillId != null ? " [" + (Content.Skill(e.SkillId)?.Name ?? e.SkillId) + "]" : "") : "Supernova hits " + (target?.Info.Name ?? "") + " for " + Fmt.Num(e.Amount);
                    break;
                case BattleAction.StatusBlocked: what = (target?.Info.Name ?? "") + " can't gain " + SkillText.StatusName(e.Status) + " for " + e.Stacks + " turns"; break;
                case BattleAction.Execute: what = who + " executes " + tgt; break;
                case BattleAction.CheatDeath: what = who + " survives a fatal hit at 1 HP"; break;
                case BattleAction.Echo: what = who + " echoes its attack"; break;
                case BattleAction.Staggered: what = who + " is staggered and loses its rage attack"; break;
                case BattleAction.Sacrifice: what = who + " takes the fatal hit for " + tgt; break;
                case BattleAction.Loop: if (e.Actor != e.Target) return; what = who + " loops time back to round " + e.Amount; break;
                case BattleAction.Heal: case BattleAction.Regen: case BattleAction.LifeSteal: what = (target ?? actor)?.Info.Name + " heals " + Fmt.Num(e.Amount); break;
                case BattleAction.Shield: what = (target ?? actor)?.Info.Name + " gains " + Fmt.Num(e.Amount) + " shield"; break;
                case BattleAction.GainRage: case BattleAction.StatusExpired: return;
                case BattleAction.SidekickAct: what = who + " uses " + AbilityName(e.SkillId, who) + (e.Stacks > 0 ? " (extra action)" : ""); break;
                case BattleAction.Guard:
                    what = e.Amount > 0 ? who + " guards " + tgt + ": the next hit deals " + e.Amount + "% less" : who + " guards " + tgt + " (" + e.Stacks + (e.Stacks == 1 ? " hit)" : " hits)");
                    break;
                case BattleAction.Guarded: what = who + "'s guard softens a hit on " + tgt + " (" + Fmt.Num(e.Amount) + " less)"; break;
                case BattleAction.Block: what = who + " blocks a hit on " + tgt + " (" + Fmt.Num(e.Amount) + " stopped)"; break;
                case BattleAction.Volley: what = who + " fires " + (Content.Skill(e.SkillId)?.Name ?? "its shot") + (e.Amount > 0 ? " (extra shot)" : "") + (tgt.Length > 0 ? " at " + tgt : ""); break;
                case BattleAction.Ability: what = who + " uses " + (Content.Skill(e.SkillId)?.Name ?? "its Ability"); break;
                case BattleAction.Mark: what = who + " marks " + tgt; break;
                case BattleAction.KitCounter: return;
                default:
                    what = who + " " + e.Action.ToString().ToLowerInvariant() + (tgt.Length > 0 ? " → " + tgt : "")
                           + (e.Dodge ? " (dodged)" : e.Amount > 0 ? " " + Fmt.Num(e.Amount) : "") + (e.Crit ? " CRIT" : "")
                           + (e.SkillId != null ? " [" + (Content.Skill(e.SkillId)?.Name ?? e.SkillId) + "]" : "");
                    break;
            }
            var line = Ui.Label(_feed, "R" + e.Round + "  " + what, 22, Theme.TextDim);
            _feedLines.Enqueue(line.gameObject);
            while (_feedLines.Count > 12) Ui.DestroyGo(_feedLines.Dequeue());
        }

        /// <summary>
        /// The hero's kit HUD: a counter "Starfall 2/4" / "Stillness 1/5" / "Bank 60/100", a timer "Feast in 2" (the skill's
        /// ability_every / ability_turns rule says it is a timer).
        /// </summary>
        public string KitCounterText(BattleLogEntry e)
        {
            var sk = Content.Skill(e.SkillId);
            string name = sk?.Name ?? "Ability";
            bool timer = false, bank = false, still = false;
            if (sk != null)
                foreach (var t in sk.Triggers)
                {
                    if (t.Effect != EffectKind.Rule || t.Param == null) continue;
                    if (t.Param.StartsWith("ability_every") || t.Param.StartsWith("ability_turns")) timer = true;
                    if (t.Param.StartsWith("rage_bank")) bank = true;
                }
            if (!timer && !bank && sk != null && sk.Triggers.Exists(t => t.Effect == EffectKind.SpreadStatus)) still = true;
            if (timer) return name + (e.Amount <= 1 ? " next turn" : " in " + e.Amount);
            if (bank) return "Bank " + e.Amount + "/" + e.Stacks;
            return (still ? "Stillness" : name) + " " + e.Amount + "/" + e.Stacks;
        }

        /// <summary>The mount's HUD: "Scan Pulse in 2" (its reload), or its charge / coil "Coil 120/300".</summary>
        private void SetMountHud(int round, BattleLogEntry counter = null)
        {
            if (_kitMount == null) return;
            if (counter != null)
            {
                var sk = Content.Skill(counter.SkillId);
                bool coil = sk != null && sk.Triggers.Exists(t => t.Effect == EffectKind.Rule && t.Param == "coil");
                _kitMount.text = (coil ? "Coil " : "Charge +") + Fmt.Num(counter.Amount) + (coil ? "/" + Fmt.Num(counter.Stacks) : "%") + " · " + _mountShot;
            }
            else
            {
                int left = _mountNext - round;
                _kitMount.text = _mountShot + (left <= 0 ? " ready" : left == 1 ? " next round" : " in " + left);
            }
            KitHud.Add(_kitMount.text);
        }

        /// <summary>HP / shield / rage straight from the log entry (values right after it, see BattleLogEntry).</summary>
        private static void Sync(BattleLogEntry e, FighterView actor, FighterView target)
        {
            if (actor != null) { actor.ShieldNow = e.ActorShield; actor.RageNow = e.ActorRage; }
            if (target != null) { target.HpNow = e.TargetHp; target.ShieldNow = e.TargetShield; target.RageNow = e.TargetRage; }
        }

        /// <summary>The guard chip: the guard icon with the hits a Block holds, or an alien Guard's % ("-20%").</summary>
        private static void GuardChip(RectTransform chips, int guards, long pct)
        {
            var chip = StatusIcon(chips, ArtCatalog.StatusIcon("guard") ?? HudArt.Icon("shield"), pct > 0 ? "-" + pct + "%" : guards.ToString(), Color.white, "Guard");
            chip.Find("Glyph").name = "Icon";
            chip.Find("Stacks").name = "Count";
        }

        /// <summary>
        /// One status under a fighter as on the board: its icon (<see cref="StatusChipSize"/>) with the count beside its foot in
        /// white outlined digits ("Glyph", "Stacks").
        /// </summary>
        private static RectTransform StatusIcon(RectTransform chips, Sprite icon, string count, Color countColor, string name)
        {
            float tw = 16 + 15 * count.Length;
            var chip = Ui.Rect(chips, name);
            Ui.Size(chip, StatusChipSize + tw - 4, StatusChipSize, 0);
            var g = KitV2.Icon(chip, icon, 0, 0, StatusChipSize, StatusChipSize, "Glyph");
            g.raycastTarget = false;
            var n = KitV2.Text(chip, count, StatusChipSize - 4, StatusChipSize - 30, tw, 30, 26.7f, countColor, TextAnchor.MiddleLeft, "Stacks");
            foreach (var sh in n.GetComponents<Shadow>()) if (!(sh is Outline)) UnityEngine.Object.DestroyImmediate(sh);
            return chip;
        }

        /// <summary>Current bar values of fighter <paramref name="index"/> (tests).</summary>
        public bool TryGetBars(int index, out double hp, out double shield, out double rage)
        {
            var v = View(index);
            hp = v?.HpNow ?? 0; shield = v?.ShieldNow ?? 0; rage = v?.RageNow ?? 0;
            return v != null;
        }

        private FighterView View(int index) => index >= 0 && index < _views.Count ? _views[index] : null;

        /// <summary>
        /// The HP number under a fighter: the HP ("12K"), for the worm with its shield in blue ("12K(1,4K)", board); shown
        /// over the HP bar.
        /// </summary>
        public static string HpLabel(double hp, double shield, bool worm) =>
            Fmt.Num(Math.Max(0, hp)) + (worm && shield > 0 ? "<color=#7FD3FF>(" + Fmt.Num(shield) + ")</color>" : "");

        private void Refresh(FighterView v, double rageMax, bool animate)
        {
            if (v == null) return;
            bool worm = v.Info.Role == CombatRole.Worm;
            TweenBar(v.Hp, Frac(v.HpNow, v.Info.MaxHp), HpLabel(v.HpNow, v.ShieldNow, worm), animate);
            TweenBar(v.Shield, Frac(v.ShieldNow, v.Info.MaxHp), null, animate);
            v.Rage.Set(Frac(v.RageNow, rageMax));
            // an enemy's shield strip shows only while it has one (the board shows its HP bar alone)
            if (!worm && v.Info.Targetable)
            {
                var holder = v.Shield.Fill.parent.parent.gameObject;
                if (holder.activeSelf != v.ShieldNow > 0) holder.SetActive(v.ShieldNow > 0);
            }
            // status icons: rebuilt only when they change
            var parts = new List<string>();
            foreach (var kv in v.Statuses) parts.Add(kv.Key + ":" + kv.Value);
            parts.Add("guard:" + v.Guards + ":" + v.GuardPct);
            string key = string.Join(",", parts);
            if (key == v.ChipKey) return;
            v.ChipKey = key;
            Ui.Clear(v.Chips);
            if (v.Guards > 0) GuardChip(v.Chips, v.Guards, v.GuardPct);
            foreach (var kv in v.Statuses)
            {
                // Heat close to its Supernova reads as dangerous: a hot red glow behind the icon, a yellow count, a pulse
                bool danger = kv.Key == StatusId.Heat && kv.Value >= HeatDangerAt(Content);
                var chip = StatusIcon(v.Chips, StatusArt.Icon(kv.Key), kv.Value.ToString(), danger ? Theme.Hex(0xFFE04A) : Color.white,
                    "Status:" + kv.Key + (danger ? ":Danger" : ""));
                if (!danger) continue;
                var glow = KitV2.Place(chip, "DangerGlow", -6, -6, StatusChipSize + 12, StatusChipSize + 12);
                var gi = glow.gameObject.AddComponent<Image>();
                gi.sprite = UiShapes.Circle; gi.color = new Color(1f, 0.18f, 0.18f, 0.55f); gi.raycastTarget = false;
                glow.SetAsFirstSibling();
                if (Application.isPlaying) UiPulse.On(chip.Find("Glyph").GetComponent<Image>(), 0.12f, 0.6f, 0.75f);
            }
        }

        private void TweenBar(UiBar bar, float to, string label, bool animate)
        {
            float from = bar.Value;
            if (!animate || Mathf.Approximately(from, to) || Instant) { bar.Set(to, label); return; }
            if (label != null && bar.Label != null) bar.Label.text = label;
            UiTweens.Play(bar.Fill.gameObject.transform.parent.gameObject, "bar", Beat, t => bar.Set(Mathf.Lerp(from, to, 1 - (1 - t) * (1 - t))), () => bar.Set(to));
        }

        /// <summary>A stat buff on the worm (from a Buff log entry): which stat, how much, until which round.</summary>
        private struct LiveBuff { public StatId Stat; public double Flat, Pct; public int Until; }
        private readonly List<LiveBuff> _heroBuffs = new List<LiveBuff>();
        private int _roundNow;

        /// <summary>
        /// Records a buff on the worm: the log carries the skill and the amount; the skill's BuffStat effect with that amount
        /// names the stat and its duration (BattleSimulator: a buff applied in round r ends after round r + duration − 1).
        /// </summary>
        private void TrackBuff(BattleLogEntry e)
        {
            var skill = e.SkillId != null ? Content.Skill(e.SkillId) : null;
            if (skill == null) return;
            SkillTrigger best = null;
            foreach (var t in skill.Triggers)
            {
                if (t.Effect != EffectKind.BuffStat || t.Stat == null) continue;
                if (best == null) best = t;
                if (Math.Round(t.Stat.Percent != 0 ? t.Stat.Percent : t.Stat.Flat) == e.Amount) { best = t; break; }
            }
            if (best == null) return;
            _heroBuffs.Add(new LiveBuff { Stat = best.Stat.Stat, Flat = best.Stat.Flat, Pct = best.Stat.Percent, Until = best.Duration > 0 ? e.Round + best.Duration - 1 : int.MaxValue });
        }

        /// <summary>A HUD stat with the worm's live buffs: (shown + flat) × (1 + percent / 100).</summary>
        private double LiveStat(double shown, StatId stat)
        {
            double flat = 0, pct = 0;
            foreach (var b in _heroBuffs)
                if (b.Stat == stat && _roundNow <= b.Until) { flat += b.Flat; pct += b.Pct; }
            return (shown + flat) * (1 + pct / 100.0);
        }

        private void RefreshHero()
        {
            if (_hero == null) return;
            if (_hud != null)
            {
                // the top HUD: the worm's live HP from the log, its run ATK / DEF / level as the board showed them
                var s = _ctx.Hud;
                s.Hp = Math.Max(0, _hero.HpNow);
                s.MaxHp = _hero.Info.MaxHp;
                s.Atk = LiveStat(s.Atk, StatId.Atk);
                s.Def = LiveStat(s.Def, StatId.Def);
                _hud.Show(s, _ctx.HasStats ? s.Level : 0, s.XpNeed > 0 ? (float)s.Xp / s.XpNeed : 0, _ctx.SkillCount, _ctx.HasStats, _ctx.InRun);
            }
        }

        private void End()
        {
            bool won = _battle.PlayerWon();
            ClearFx();
            if (_banner != null)
            {
                _banner.text = won ? "VICTORY" : _battle.EndReason == BattleEndReason.RoundCap ? "TIME UP" : "DEFEAT";
                _banner.color = won ? Theme.Gold : Theme.Bad;
                _banner.gameObject.SetActive(true);
                if (Application.isPlaying && !Instant) UiTween.PopIn(_banner, 0.3f);
            }
            // playtest 0.1.308: a won run battle returns to the board by itself after a short VICTORY beat; a lost one (and a
            // fight outside a run: arena, kit trials) keeps the Continue button (the defeat / revive flow follows it)
            if (won && _ctx != null && _ctx.InRun) _autoClose = Instant ? 0 : WinBeatSeconds / Mathf.Max(0.5f, _speed * TimeScale);
            else if (_endPanel != null) _endPanel.gameObject.SetActive(true);
            foreach (var v in _views)
                if (!v.Dead && v.Visible && CreatureEvents.End(won, v.Info.Side == BattleSide.Player) is CreatureClip c) Clip(v, c);
        }

        /// <summary>Plays a creature clip on a fighter (tests read <see cref="ClipCount"/>); <paramref name="toward"/> sets the direction of attacks.</summary>
        private void Clip(FighterView v, CreatureClip clip, FighterView toward = null, float seconds = 0)
        {
            if (v?.Anim == null) return;
            v.Anim.SetTimeScale(Mathf.Max(0.5f, _speed * TimeScale) * 1.5f);
            if (toward != null) v.Anim.AimScreen = toward.Root.anchoredPosition.x >= v.Root.anchoredPosition.x ? 1 : -1;
            v.Anim.Play(clip, seconds);
            ClipCounts[(int)clip]++;
        }

        /// <summary>Creature clips started so far per <see cref="CreatureClip"/> (tests, screenshots).</summary>
        public readonly int[] ClipCounts = new int[6];

        /// <summary>The code animation of fighter <paramref name="index"/> (tests).</summary>
        public UiCreature AnimOf(int index) => View(index)?.Anim;

        /// <summary>BT5: no stale numbers or banners after the battle ends.</summary>
        private void ClearFx()
        {
            if (_fx == null) return;
            for (int i = _fx.childCount - 1; i >= 0; i--) Ui.DestroyGo(_fx.GetChild(i).gameObject);
            foreach (var v in _views) v.LivePopups = 0;
            _abilityLane = 0;
        }

        /// <summary>Seconds the VICTORY banner shows at ×1 before a won run battle returns to the board on its own.</summary>
        public const float WinBeatSeconds = 0.9f;
        private float _autoClose = -1;
        /// <summary>A won run battle is counting down to return to the board by itself (tests).</summary>
        public bool AutoClosing => _autoClose >= 0;

        /// <summary>The end panel's Continue button: back to the run.</summary>
        public void Continue() => Close();

        private void Close()
        {
            if (App.Router.Current == this) App.Router.Pop();
        }
    }

    /// <summary>
    /// The worm as the run's HUD showed it when the fight started (top strip numbers) and the run's skills with their
    /// levels (skill strip). The run screen passes it; without one the playback reads the active run (or shows the log's
    /// fired skills only).
    /// </summary>
    public sealed class BattleHudContext
    {
        public RunHudStats Hud;
        /// <summary>False: ATK / DEF / level unknown (no run and no account) — the strip hides those pills (never "–").</summary>
        public bool HasStats;
        /// <summary>True: a run is behind this battle (its level badge and XP bar show). False (Q2): account stats only.</summary>
        public bool InRun;
        /// <summary>Run coins for the chip; &lt; 0 = no coins chip.</summary>
        public long Coins = -1;
        public int SkillCount;
        public List<string> Skills = new List<string>();
        public Dictionary<string, int> Levels = new Dictionary<string, int>();
        /// <summary>The run's "Roll n/N" (null outside a run).</summary>
        public string Rolls;

        /// <summary>No run stats yet: the worm's account ATK / DEF / HP and worm level (keeps coins, skills, rolls).</summary>
        public void FillFromAccount(GameState state, CombatLoadout loadout)
        {
            if (state == null || loadout == null) return;
            double hp = loadout.WormStats.Value(StatId.Hp);
            Hud = new RunHudStats
            {
                Hp = hp, MaxHp = hp, Atk = loadout.WormStats.Value(StatId.Atk), Def = loadout.WormStats.Value(StatId.Def),
                Level = Math.Max(1, state.Worm.Level),
            };
            HasStats = true;
        }

        public static BattleHudContext FromAccount(GameState state, CombatLoadout loadout)
        {
            var ctx = new BattleHudContext();
            ctx.FillFromAccount(state, loadout);
            return ctx;
        }

        public static BattleHudContext FromRun(ActiveRun run, ContentDb c)
        {
            var ctx = new BattleHudContext();
            if (run == null) return ctx;
            ctx.Hud = RunPresentation.Hud(c, run);
            ctx.HasStats = true;
            ctx.InRun = true;
            ctx.Coins = run.Coins;
            ctx.SkillCount = RunSystem.BuildSize(run);
            ctx.Rolls = RunScreen.RollText(c, run);
            ctx.Skills = RunSystem.ActiveSkillIds(run);
            foreach (var id in ctx.Skills) ctx.Levels[id] = RunDraft.Level(run, id);
            return ctx;
        }
    }
}
