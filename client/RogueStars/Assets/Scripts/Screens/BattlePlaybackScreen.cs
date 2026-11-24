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

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Animates a BattleResult log on a small stage: the player's side (worm, aliens, mount) on the left, enemies on the
    /// right. Each entry drives code tweens (no packages): the attacker lunges toward its target, the target flashes and
    /// shakes, floating damage numbers rise (crits bigger with "CRIT"), dodge / heal / shield / status / resist / stun
    /// popups, HP bars tween down, status chips (BURN 2), knocked-out fighters fade, a turn-order strip shows who acts
    /// next. Speed ×1/×2, ×3 with the monthly pass (marked PASS otherwise).
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
        }

        private static int _speed = 1;

        private readonly BattleResult _battle;
        private readonly bool _autoClose;
        private readonly List<FighterView> _views = new List<FighterView>();
        private int _next;
        private float _timer;
        private Text _round;
        private Text _banner;
        private RectTransform _endPanel;
        private RectTransform _stage;
        private RectTransform _order;
        private bool _built;
        private RectTransform _feed;
        private readonly Queue<GameObject> _feedLines = new Queue<GameObject>();
        private readonly List<Button> _speedButtons = new List<Button>();
        private FighterView _hero;
        private UiBar _heroRage;
        private Text _heroRageText;
        private RectTransform _heroChips;
        private int _popupSeq;

        /// <summary>Stage height and fighter card size (canvas units).</summary>
        public const float StageH = 1060;
        public static readonly Vector2 ViewSize = new Vector2(360, 500);
        /// <summary>Height of a fighter's feet over its card's bottom (the bars and statuses sit under it).</summary>
        public const float SpriteBase = 96;

        /// <summary>Skip mode: no animations, the whole log is applied on the next frame and auto playbacks close at once.</summary>
        public static bool Instant;
        /// <summary>Extra playback multiplier for scripted playthroughs/tests (1 in the game).</summary>
        public static float TimeScale = 1f;

        // ---- design A: top strip + skill strip ----
        private BattleHudContext _ctx;
        private readonly SkillFireQueue _fires = new SkillFireQueue();
        private readonly Dictionary<string, RectTransform> _skillTiles = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, int> _fireCounts = new Dictionary<string, int>();
        private RectTransform _fireLayer;
        /// <summary>Key of the "+N" tile: fires of skills that do not fit the strip pulse it.</summary>
        public const string MoreKey = "+more";
        /// <summary>Skill tiles in the battle's strip (the last one is "+N" when there are more).</summary>
        public const int StripSlots = 6;
        public const float StripTileSize = 146;
        /// <summary>Every skill firing played so far, in order ("Magma Tail · Burn ×2"; tests).</summary>
        public readonly List<string> SkillFires = new List<string>();

        public BattlePlaybackScreen(BattleResult battle, bool autoClose = false, BattleHudContext context = null)
        {
            _battle = battle;
            _autoClose = autoClose;
            _ctx = context;
        }

        public override string Title => "Battle";
        public override bool AutoRebuild => false;
        /// <summary>No app header: the run's top strip (skip button, worm HP / ATK / DEF / level) is the header (design A).</summary>
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
        /// <summary>Opened by auto mode: pops itself shortly after the fight; otherwise it waits for Continue.</summary>
        public bool AutoClose => _autoClose;
        public int Speed => _speed;
        public float StepSeconds => 0.55f / _speed / Mathf.Max(0.01f, TimeScale);
        /// <summary>Animation time for one beat of an entry (lunge, hit, bar) at the current speed.</summary>
        private float Beat => Instant ? 0 : Mathf.Min(0.42f, StepSeconds * 0.8f);

        private void RecolorSpeed()
        {
            for (int i = 0; i < _speedButtons.Count; i++) Ui.SetButtonColor(_speedButtons[i], _speed == i + 1 ? Theme.Accent : Theme.Track);
        }

        // ---- build -------------------------------------------------------------------------------

        protected override void Build(RectTransform body)
        {
            if (_built) { _next = 0; _views.Clear(); _feedLines.Clear(); SkillFires.Clear(); _fireCounts.Clear(); _heroBuffs.Clear(); _roundNow = 0; }
            _speedButtons.Clear();
            _built = true;
            if (_battle == null) { Note(body, "No battle to show."); return; }
            var bl = body.GetComponent<VerticalLayoutGroup>();
            if (bl != null) { bl.padding = new RectOffset(0, 0, 0, 0); bl.spacing = 0; }
            _ctx = _ctx ?? BattleHudContext.FromRun(State?.Run.Active, Content);
            float frameH = BodyFrame != null && BodyFrame.rect.height > 100 ? BodyFrame.rect.height : 1920;
            float frameW = BodyFrame != null && BodyFrame.rect.width > 100 ? BodyFrame.rect.width : 1080;
            float s = frameW / 1080f;
            _scale = s;
            var holder = Ui.Rect(body, "BattleHolder");
            Ui.Size(holder, -1, frameH);
            _holder = holder;

            // the arena (top ~62%): a 3/4 top-down biome floor, big fighters, enemies right, the worm and its companions left
            float arenaRefH = 1480;
            var arena = Ui.Panel(holder, Color.white, "Arena");
            arena.sprite = ArtCatalog.SliceBattle("battle_meadow") ?? HudArt.Arena();
            arena.raycastTarget = false;
            arena.rectTransform.anchorMin = new Vector2(0, 1); arena.rectTransform.anchorMax = new Vector2(1, 1);
            arena.rectTransform.pivot = new Vector2(0.5f, 1);
            arena.rectTransform.sizeDelta = new Vector2(0, arenaRefH * s); arena.rectTransform.anchoredPosition = Vector2.zero;
            _stage = Ui.Rect(holder, "Fighters");
            _stage.anchorMin = new Vector2(0.5f, 1); _stage.anchorMax = new Vector2(0.5f, 1);
            _stage.pivot = new Vector2(0.5f, 0.5f);
            _stage.sizeDelta = new Vector2(1080, StageH);
            _stage.localScale = Vector3.one * s;
            _stage.anchoredPosition = new Vector2(0, -(320 + StageH / 2) * s);
            var players = _battle.Fighters.FindAll(f => f.Side == BattleSide.Player);
            var enemies = _battle.Fighters.FindAll(f => f.Side != BattleSide.Player);
            foreach (var f in _battle.Fighters)
            {
                var side = f.Side == BattleSide.Player ? players : enemies;
                _views.Add(Fighter(f, side.IndexOf(f), side.Count));
            }
            _hero = _views.Find(v => v.Info.Role == CombatRole.Worm) ?? _views.Find(v => v.Info.Side == BattleSide.Player);
            var mount = _views.Find(v => v.Info.Role == CombatRole.Mount);
            if (mount != null && _hero != null && _hero.Info.Role == CombatRole.Worm) SeatOnMount(_hero, mount.Info.Id);

            // the bottom panel: the skill grid
            var panel = Ui.Panel(holder, new Color32(0x16, 0x1E, 0x28, 255), "SkillPanel");
            panel.raycastTarget = false;
            panel.rectTransform.anchorMin = Vector2.zero; panel.rectTransform.anchorMax = Vector2.one;
            panel.rectTransform.offsetMin = Vector2.zero; panel.rectTransform.offsetMax = new Vector2(0, -(arenaRefH - 8) * s);
            var edge = Ui.Panel(panel.transform, new Color32(0x0A, 0x0E, 0x14, 255), "Edge");
            edge.raycastTarget = false;
            edge.rectTransform.anchorMin = new Vector2(0, 1); edge.rectTransform.anchorMax = Vector2.one; edge.rectTransform.pivot = new Vector2(0.5f, 1);
            edge.rectTransform.sizeDelta = new Vector2(0, 10 * s); edge.rectTransform.anchoredPosition = Vector2.zero;
            // a soft dark gradient under the arena's edge and a darker floor (the panel is not a flat slab)
            var panelShade = Ui.Panel(panel.transform, Color.white, "Shade");
            panelShade.sprite = HudArt.Gradient(new Color32(0x0C, 0x10, 0x16, 255), new Color32(0x22, 0x2E, 0x38, 255));
            panelShade.raycastTarget = false;
            Ui.Stretch(panelShade.rectTransform, 0, 0, 0, 10 * s);

            // the HUD over the arena: the run's top HUD with the worm's live HP, "Round n/N", a skip chip
            _hud = SliceHud.Build(holder, frameW, () => Session.Info("Battle in progress — >> skips it"), null, _ctx.Coins >= 0);
            if (_hud.Coins != null) _hud.Coins.text = Fmt.Num(_ctx.Coins);
            var roundPill = SliceKit.Pill(holder, s, 540, 296, 300, 62, new Color32(0x14, 0x12, 0x20, 200), "RoundPill");
            _round = SliceKit.Chunky(roundPill.transform, "Round 1/" + _battle.Rounds, (int)(40 * s), Color.white, TextAnchor.MiddleCenter, "Round");
            Ui.Stretch(_round.rectTransform);
            var skip = Ui.Button(holder, "", Close, SliceKit.PillDark, 130 * s, 62 * s, true, 20);
            skip.name = "SkipButton";
            SliceKit.Place((RectTransform)skip.transform, s, 990, 296, 130, 62);
            UiShapes.Outline((RectTransform)skip.transform, SliceKit.PillEdge, 30 * s, 4);
            var skipIcon = Ui.Panel(skip.transform, Color.white, "SkipIcon");
            skipIcon.sprite = HudArt.Icon("speed");
            skipIcon.preserveAspect = true;
            skipIcon.raycastTarget = false;
            Ui.Stretch(skipIcon.rectTransform, 30 * s, 10 * s, 30 * s, 10 * s);

            // turn order: who acts next (small pips under the round)
            _order = Ui.Row(holder, 40 * s, 8 * s, null, 0, "TurnOrder");
            SliceKit.Place(_order, s, 540, 356, 400, 36);
            _order.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            _order.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

            _banner = SliceKit.Chunky(holder, "", (int)(130 * s), Theme.Gold, TextAnchor.MiddleCenter, "Banner");
            SliceKit.Place(_banner.rectTransform, s, 540, 860, 1000, 180);
            _banner.gameObject.SetActive(false);

            // bottom row: speed chips, Continue (at the end), battle log
            bool x3 = ShopSystem.HasBattleSpeed3(State, Content, Now);
            // one dark speed pill bottom-left (the board's speed chip look): the ">>" icon, then the x1 / x2 / x3 segments
            var speedPill = SliceKit.Pill(holder, s, 212, 2290, 404, 104, new Color32(0x3A, 0x2E, 0x58, 245), "SpeedPill", true);
            var spIcon = Ui.Panel(speedPill.transform, Color.white, "SpeedIcon");
            spIcon.sprite = HudArt.Icon("speed");
            spIcon.preserveAspect = true;
            spIcon.raycastTarget = false;
            spIcon.rectTransform.anchorMin = spIcon.rectTransform.anchorMax = new Vector2(0.13f, 0.5f);
            spIcon.rectTransform.sizeDelta = new Vector2(58 * s, 58 * s);
            spIcon.rectTransform.anchoredPosition = Vector2.zero;
            for (int sp = 1; sp <= 3; sp++)
            {
                int v = sp;
                bool allowed = sp < 3 || x3;
                var b = Ui.Button(holder, "", () => { _speed = v; RecolorSpeed(); }, _speed == sp ? Theme.Accent : (Color)new Color32(0x3A, 0x2E, 0x58, 245), 78 * s, 74 * s, allowed, 20);
                b.name = "Speed" + sp;
                SliceKit.Place((RectTransform)b.transform, s, 118 + sp * 82, 2290, 78, 74, true);
                UiShapes.Outline((RectTransform)b.transform, SliceKit.PillEdge, 20 * s, 3);
                var t = SliceKit.Chunky(b.transform, "x" + sp + (allowed ? "" : "\n<size=" + (int)(14 * s) + ">PASS</size>"), (int)(34 * s), Color.white, TextAnchor.MiddleCenter, "SpeedText");
                Ui.Stretch(t.rectTransform);
                _speedButtons.Add(b);
            }
            _endPanel = Ui.Rect(holder, "EndPanel");
            SliceKit.Place(_endPanel, s, 640, 2290, 340, 104, true);
            var cont = Ui.Button(_endPanel, "Continue", Close, Theme.Accent, 340 * s, 104 * s, true, (int)(50 * s));
            cont.name = "ContinueButton";
            Ui.Stretch((RectTransform)cont.transform);
            UiShapes.Outline((RectTransform)cont.transform, IsoArt.Ink, Theme.RadiusButton, 5);
            _endPanel.gameObject.SetActive(false);
            var logButton = Ui.Button(holder, "Log", null, SliceKit.PillDark, 130 * s, 76 * s, true, (int)(30 * s));
            logButton.name = "Button:Battle log";
            SliceKit.Place((RectTransform)logButton.transform, s, 984, 2290, 130, 76, true);
            logButton.GetComponentInChildren<Text>().color = Theme.TextDim;
            _feed = Ui.Card(holder, Theme.Panel, null, 30, 20, 4, 0, "Feed");
            SliceKit.Place(_feed, s, 540, 1600, 1000, 900);
            Ui.Caption(_feed, "Battle log", Theme.Gold);
            _feed.gameObject.SetActive(false);
            logButton.onClick.AddListener(() => { _feed.gameObject.SetActive(!_feed.gameObject.activeSelf); _feed.SetAsLastSibling(); });
            if (_speed == 3 && !x3) _speed = 2;
            RecolorSpeed();
            SkillGrid(holder, s, arenaRefH);
            foreach (var v in _views) Refresh(v, Content.Combat.RageMax, false);
            RefreshHero();
            TurnOrder();
        }

        private float _scale = 1;
        private RectTransform _holder;
        private SliceHud _hud;

        // ---- the skill grid (reference 3): 5 columns of tier-framed icons, each pops when its skill fires ----------------

        /// <summary>Skill tiles in the grid (the last one is "+N" when there are more).</summary>
        public const int GridColumns = 5, GridRows = 3;
        public const float GridTile = 172;

        private void SkillGrid(RectTransform holder, float s, float arenaRefH)
        {
            _skillTiles.Clear();
            var grid = Ui.Rect(holder, "SkillStrip");
            Ui.Stretch(grid);
            var order = BattleSkillFx.StripOrder(_ctx.Skills, _battle, Content);
            var levels = _ctx.Levels;
            int slots = GridColumns * GridRows;
            var tray = RunPresentation.Tray(Content, order, id => levels != null && levels.TryGetValue(id, out var lv) ? lv : 1, slots);
            int i = 0;
            float x0 = 165, step = 187.5f, y0 = arenaRefH + 138;
            foreach (var t in tray.Tiles)
            {
                var tile = SkillIconTile(grid, t, Content.Skill(t.SkillId)?.Family ?? SkillFamily.None, s);
                SliceKit.Place(tile, s, x0 + (i % GridColumns) * step, y0 + (i / GridColumns) * step, GridTile, GridTile);
                _skillTiles[t.SkillId] = tile;
                i++;
            }
            if (tray.Overflow > 0)
            {
                var more = Ui.Button(grid, "+" + tray.Overflow, null, new Color32(0x2A, 0x30, 0x48, 255), GridTile * s, GridTile * s, true, (int)(52 * s));
                more.name = "MoreSkills";
                SliceKit.Place((RectTransform)more.transform, s, x0 + (i % GridColumns) * step, y0 + (i / GridColumns) * step, GridTile, GridTile);
                UiShapes.Outline((RectTransform)more.transform, IsoArt.Ink, 26 * s, 5);
                more.GetComponentInChildren<Text>().color = Color.white;
                _skillTiles[MoreKey] = (RectTransform)more.transform;
            }
            if (tray.Tiles.Count == 0 && tray.Overflow == 0)
            {
                var none = SliceKit.Chunky(grid, "No skills yet — draft some on the board.", (int)(34 * s), Theme.TextDim, TextAnchor.MiddleCenter, "NoSkills");
                SliceKit.Place(none.rectTransform, s, 540, y0, 1000, 80);
            }
            // labels rise from the tiles over the stage: their own layer on top of the screen
            _fireLayer = Ui.Rect(Root, "SkillFireLayer");
            Ui.Stretch(_fireLayer);
            _fireLayer.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            _fires.Started -= PlayFire;
            _fires.Started += PlayFire;
        }

        /// <summary>
        /// One skill icon tile (no label): a thick frame in the skill's tier colour, a slate face, the skill's icon art
        /// (ArtCatalog Art/Skills/&lt;id&gt;) or its family glyph. Named "Slot:" + id, its frame "RarityBorder".
        /// </summary>
        public static RectTransform SkillIconTile(Transform parent, SkillTile t, SkillFamily family, float s)
        {
            var rt = Ui.Rect(parent, "Slot:" + t.SkillId);
            var border = Ui.RoundPanel(rt, t.Color, 28 * s, "RarityBorder");
            border.raycastTarget = false;
            Ui.Stretch(border.rectTransform);
            UiShapes.Outline(border.rectTransform, IsoArt.Ink, 28 * s, 4);
            var face = Ui.RoundPanel(rt, new Color32(0x8C, 0x90, 0xB6, 255), 20 * s, "Face");
            face.raycastTarget = false;
            Ui.Stretch(face.rectTransform, 12 * s, 12 * s, 12 * s, 14 * s);
            var shine = Ui.RoundPanel(face.transform, new Color(1, 1, 1, 0.12f), 16 * s, "Shine");
            shine.raycastTarget = false;
            Ui.Stretch(shine.rectTransform, 6 * s, 6 * s, 6 * s, 40 * s);
            var art = ArtCatalog.SkillIcon(t.SkillId);
            if (art != null)
            {
                var img = Ui.Panel(rt, Color.white, "IconArt");
                img.sprite = art;
                img.preserveAspect = true;
                img.raycastTarget = false;
                Ui.Stretch(img.rectTransform, 18 * s, 18 * s, 18 * s, 18 * s);
            }
            else
            {
                var col = family == SkillFamily.None ? (Color)new Color32(0xFF, 0xE0, 0x5A, 255) : Color.Lerp(Theme.FamilyColor(family), Color.white, 0.3f);
                var glyph = SliceKit.Chunky(rt, family == SkillFamily.None ? "★" : family.ToString().Substring(0, 1), (int)(84 * s), col, TextAnchor.MiddleCenter, "Glyph");
                Ui.Stretch(glyph.rectTransform);
            }
            return rt;
        }

        /// <summary>A skill of the player's side fired (log entry with its SkillId): count it, queue its tile's flash + label.</summary>
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

        /// <summary>One fire on its tile: the tile pops and flashes, the label ("Magma Tail · Burn ×2") rises above it.</summary>
        private void PlayFire(string key, string label)
        {
            if (!_skillTiles.TryGetValue(key, out var tile) || tile == null) return;
            float secs = FireSeconds;
            var flash = Ui.RoundPanel(tile, new Color(1, 1, 1, 0), Theme.RadiusButton, "FireFlash");
            flash.raycastTarget = false;
            flash.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Stretch(flash.rectTransform, -6, -6, -6, -6);
            UiTweens.Play(tile.gameObject, "fire", secs, t =>
            {
                float pop = 1 + 0.16f * Mathf.Sin(Mathf.Clamp01(t * 2.2f) * Mathf.PI);
                if (tile != null) tile.localScale = Vector3.one * pop;
                if (flash != null) flash.color = new Color(1, 1, 1, 0.75f * Mathf.Max(0, 1 - t * 1.8f));
            }, () =>
            {
                if (tile != null) tile.localScale = Vector3.one;
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
            // in the arena over the worm's head (labels of fires at once stack upwards), never over the skill grid
            var anchor = _hero != null ? _hero.Sprite : tile;
            var world = anchor.TransformPoint(anchor.rect.center + new Vector2(0, anchor.rect.height * 0.5f + 30));
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
        /// Stage position (anchored, from the stage centre) of the n-th of <paramref name="count"/> fighters of a side. The
        /// player's last fighter is the worm (BattleSimulator.BuildPlayerSide adds it last): the hero group (worm on its mount,
        /// about half the arena wide) stands left of centre; the companions (mount slot, aliens, summons) line up along the
        /// left edge; enemies stand right, the front rank further out.
        /// </summary>
        public static Vector2 StagePosition(BattleSide side, int index, int count)
        {
            count = Math.Max(1, count);
            if (side == BattleSide.Player)
            {
                if (index == count - 1) return new Vector2(-190, 30);
                return CompanionPosition(index);
            }
            float lane = (StageH - 60) / count;
            float y = count == 1 ? 30 : StageH / 2 - 60 - lane * (index + 0.5f);
            float x = count > 1 && index % 2 == 1 ? 210 : 300;
            return new Vector2(x, y);
        }

        /// <summary>The k-th companion spot (mount slot, aliens, summons): a column along the arena's left edge.</summary>
        public static Vector2 CompanionPosition(int k) => new Vector2(-430 + (k % 2) * 70, 330 - (k % 5) * 190);

        /// <summary>
        /// Seats the worm on its mount: the mount's art (ArtCatalog <c>Art/Slice/Mounts/&lt;mount id&gt;</c>, else the drawn
        /// hover saucer, HudArt.Mount) under the worm's sprite, its rim in front of the worm's tail; the worm's bars move under
        /// the mount.
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
            rt.sizeDelta = new Vector2(w * 1.4f, w * 0.7f);
            rt.anchoredPosition = new Vector2(-w * 0.03f, -w * 0.04f);
            foreach (var name in new[] { "HpHolder", "ShieldHolder", "RageHolder" })
            {
                var bar = (RectTransform)worm.Root.Find(name);
                if (bar != null) bar.anchoredPosition -= new Vector2(0, w * 0.36f);
            }
            var chips = worm.Chips;
            if (chips != null) chips.anchoredPosition -= new Vector2(0, w * 0.36f);
        }

        private FighterView Fighter(BattleFighterInfo f, int index, int count)
        {
            var v = new FighterView { Info = f, HpNow = f.StartHp, ShieldNow = f.StartShield, RageNow = f.StartRage };
            var root = Ui.Rect(_stage, "Fighter:" + f.Index);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = ViewSize;
            // the worm always takes the hero spot; anything else that ends up last on the player side (a summon) a companion spot
            bool playerSide = f.Side == BattleSide.Player;
            v.Home = f.Role == CombatRole.Worm ? StagePosition(BattleSide.Player, 0, 1)
                : playerSide && index == count - 1 ? CompanionPosition(index) : StagePosition(f.Side, index, count);
            root.anchoredPosition = v.Home;
            if (playerSide && f.Role != CombatRole.Worm) root.localScale = Vector3.one * 0.62f;
            else if (f.Side != BattleSide.Player)
            {
                float lane = (StageH - 60) / Math.Max(1, count);
                if (lane < ViewSize.y) root.localScale = Vector3.one * Mathf.Max(0.6f, lane / ViewSize.y);
            }
            v.Root = root;
            v.Group = root.gameObject.AddComponent<CanvasGroup>();
            bool player = f.Side == BattleSide.Player;
            var align = player ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;

            var name = Ui.Label(root, f.Name, 24, Theme.Text, align, "Name");
            name.fontStyle = FontStyle.Bold;
            Pin(name.rectTransform, 0, 34);
            name.gameObject.SetActive(false); // the arena shows the fighters, not their names
            var hpBg = Ui.Rect(root, "HpHolder");
            PinBottom(hpBg, 54, 34);
            v.Hp = Ui.Bar(hpBg, Frac(f.StartHp, f.MaxHp), player ? (Color)new Color32(0xF0, 0x4A, 0x52, 255) : (Color)new Color32(0xF0, 0x4A, 0x52, 255), Fmt.Num(f.StartHp), 34);
            if (v.Hp.Label != null) { v.Hp.Label.font = Ui.DisplayFont; v.Hp.Label.fontSize = 26; var o = v.Hp.Label.gameObject.AddComponent<Outline>(); o.effectColor = SliceKit.Ink; o.effectDistance = new Vector2(2, -2); }
            UiShapes.Outline(hpBg, SliceKit.Ink, 10, 3);
            Ui.Stretch((RectTransform)v.Hp.Fill.parent);
            var shieldBg = Ui.Rect(root, "ShieldHolder");
            PinBottom(shieldBg, 42, 10);
            v.Shield = Ui.Bar(shieldBg, Frac(f.StartShield, f.MaxHp), Theme.Premium, null, 8);
            Ui.Stretch((RectTransform)v.Shield.Fill.parent);
            var rageBg = Ui.Rect(root, "RageHolder");
            PinBottom(rageBg, 30, 10);
            v.Rage = Ui.Bar(rageBg, Frac(f.StartRage, Content.Combat.RageMax), Theme.Accent, null, 8);
            Ui.Stretch((RectTransform)v.Rage.Fill.parent);
            if (!f.Targetable) { hpBg.gameObject.SetActive(false); shieldBg.gameObject.SetActive(false); rageBg.gameObject.SetActive(false); }

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
            // their own art: a family sprite, scaled up, on a glow in the role's colour); without art a rounded token with its letter
            var hero = FighterSprite(f, Content, out bool family, out float spriteSize);
            Image body;
            if (hero != null)
            {
                if (family)
                {
                    var glow = Ui.Panel(root, new Color(color.r, color.g, color.b, 0.45f), "RoleGlow");
                    glow.sprite = UiShapes.Circle;
                    glow.raycastTarget = false;
                    var grt = glow.rectTransform;
                    grt.anchorMin = grt.anchorMax = new Vector2(player ? 0.3f : 0.7f, 0);
                    grt.pivot = new Vector2(0.5f, 0.5f);
                    grt.sizeDelta = new Vector2(spriteSize * 0.95f, spriteSize * 0.32f);
                    grt.anchoredPosition = new Vector2(0, 52);
                }
                body = Ui.Panel(root, Color.white, "Sprite");
                body.sprite = hero;
                body.preserveAspect = true;
                size = new Vector2(spriteSize, spriteSize);
            }
            else if (f.Role == CombatRole.Mount) body = Ui.Panel(root, new Color(1, 1, 1, 0), "Sprite"); // drawn under the worm (SeatOnMount)
            else body = Ui.RoundPanel(root, f.Role == CombatRole.Worm ? Theme.Worm : Theme.Shade(color, 0.5f), radius, "Sprite");
            body.raycastTarget = false;
            var srt = body.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(player ? 0.3f : 0.7f, 0);
            srt.pivot = new Vector2(0.5f, 0);
            srt.sizeDelta = size;
            srt.anchoredPosition = new Vector2(0, SpriteBase);
            if (hero == null && f.Role != CombatRole.Mount) UiShapes.Outline(srt, f.Role == CombatRole.Worm ? Theme.WormLight : color, radius, 6);
            v.Sprite = srt;
            v.SpriteImage = body;
            v.SpriteColor = body.color;
            v.FlashColor = hero != null ? new Color(1f, 0.45f, 0.42f) : Color.white; // a sprite cannot get whiter: it flashes red
            if (f.Role != CombatRole.Worm && f.Role != CombatRole.Mount && hero == null)
            {
                var letter = Ui.Display(srt, RoleLetter(f.Role), 44, Color.Lerp(color, Color.white, 0.6f), TextAnchor.MiddleCenter);
                Ui.Stretch(letter.rectTransform);
            }
            else if (hero == null && f.Role == CombatRole.Worm)
            {
                var eye = Ui.Dot(srt, Theme.OnAccent, 16, "Eye");
                eye.rectTransform.anchorMin = eye.rectTransform.anchorMax = new Vector2(0.82f, 0.62f);
                eye.rectTransform.sizeDelta = new Vector2(16, 16);
            }
            var chips = Ui.Rect(root, "Status");
            chips.anchorMin = new Vector2(0, 0); chips.anchorMax = new Vector2(1, 0); chips.pivot = new Vector2(0.5f, 1);
            chips.sizeDelta = new Vector2(0, 52); chips.anchoredPosition = new Vector2(0, 24);
            var h = chips.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.childAlignment = player ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            v.Chips = chips;
            if (f.SummonedBy >= 0) { v.Visible = false; root.gameObject.SetActive(false); }
            return v;
        }

        /// <summary>Drawn size of a regular enemy / alien sprite on the stage (bosses and elites: × the ArtCatalog role scales).</summary>
        public const float EnemySpriteSize = 330, AlienSpriteSize = 220, WormSpriteSize = 380;

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
                case CombatRole.Alien: size = AlienSpriteSize; return ArtCatalog.Alien(f.Id);
                case CombatRole.Mount: return null;
            }
            if (f.Side != BattleSide.Enemy) return null;
            var s = ArtCatalog.EnemyOrFamily(f.Id, content, out family);
            size = EnemySpriteSize * (f.Role == CombatRole.Boss ? ArtCatalog.BossFamilyScale : f.Role == CombatRole.Elite ? ArtCatalog.EliteFamilyScale : 1);
            if (f.Role == CombatRole.Summon) size = AlienSpriteSize;
            return s;
        }

        /// <summary>Stretch across the fighter card, a fixed height, <paramref name="y"/> over its bottom (the bars under a fighter).</summary>
        private static void PinBottom(RectTransform rt, float y, float height)
        {
            rt.anchorMin = new Vector2(0.08f, 0); rt.anchorMax = new Vector2(0.92f, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(0, height); rt.anchoredPosition = new Vector2(0, y);
        }

        /// <summary>Stretch horizontally across the fighter card, a fixed height, top offset <paramref name="y"/>.</summary>
        private static void Pin(RectTransform rt, float y, float height)
        {
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
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
                default: return Theme.Purple;
            }
        }

        // ---- playback ----------------------------------------------------------------------------

        /// <summary>
        /// Every fight is seen: even a trivially won one (a single blow) stays on screen at least this long before auto
        /// mode closes it (playtest 0.1.86: "landed on battle tiles that did nothing").
        /// </summary>
        public const float MinVisibleSeconds = 1.8f;
        private float _elapsed;

        /// <summary>Seconds auto mode keeps the finished fight on screen: 0.8 s, longer for very short fights.</summary>
        public static float AutoCloseHold(float elapsed) => Mathf.Max(0.8f, MinVisibleSeconds - elapsed);

        public override void Frame(float dt)
        {
            if (_battle == null || Root == null) return;
            _elapsed += dt;
            _fires.Tick(dt); // skill tiles play their queued fires, also after the last entry
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
            if (_round != null) _round.text = "Round " + e.Round + "/" + _battle.Rounds;
            _roundNow = e.Round;
            var actor = View(e.Actor);
            var target = View(e.Target) ?? actor;
            double rageMax = Content.Combat.RageMax;
            bool attack = IsAttack(e.Action);
            if (animate && attack && actor != null && target != null && target != actor) Lunge(actor, target, e.Action == BattleAction.Rage);
            switch (e.Action)
            {
                case BattleAction.Basic:
                case BattleAction.Combo:
                case BattleAction.Counter:
                case BattleAction.Skill:
                case BattleAction.Rage:
                case BattleAction.StatusTick:
                    if (target == null) break;
                    if (e.Dodge) { Popup(target, "DODGE", Theme.Premium, animate, 40); if (animate) Dodge(target); break; }
                    if (animate) Hit(target, e.Crit);
                    if (e.Crit) Popup(target, Fmt.Num(e.Amount) + "!", Theme.Gold, animate, 76, "CRIT");
                    else Popup(target, "-" + Fmt.Num(e.Amount), e.Action == BattleAction.StatusTick ? StatusColor(e.Status) : target.Info.Side == BattleSide.Player ? Theme.Hex(0xFF9B3D) : Theme.Hex(0x5EEAF2), animate, 60);
                    if (e.Parry) Popup(target, "PARRY", Theme.TextDim, animate, 30);
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
                    Popup(target, SkillText.StatusName(e.Status).ToUpperInvariant(), StatusColor(e.Status), animate, 34);
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
                    if (target != null) Popup(target, "BUFF", Theme.Good, animate, 32);
                    if (target != null && target == _hero) TrackBuff(e);
                    break;
                case BattleAction.SkipTurn:
                    if (actor != null) Popup(actor, "STUNNED", Theme.TextDim, animate, 32);
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

        private void Lunge(FighterView actor, FighterView target, bool rage)
        {
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
            var sprite = v.Sprite;
            var home = new Vector2(0, SpriteBase);
            float dir = v.Info.Side == BattleSide.Player ? -1 : 1;
            UiTweens.Play(sprite.gameObject, "hit", Beat, t => sprite.anchoredPosition = home + new Vector2(dir * 50 * Mathf.Sin(t * Mathf.PI), 0),
                () => sprite.anchoredPosition = home);
        }

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
            var holder = Ui.Rect(_stage, "Popup");
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
            size = (int)(size * 1.45f);
            holder.sizeDelta = new Vector2(700, size + (caption != null ? 40 : 0));
            float jitter = ((_popupSeq++ % 3) - 1) * 46;
            // over the sprite's centre (the sprite sits on the card's bottom, 30% / 70% across)
            var s = v.Sprite;
            float sx = (s.anchorMin.x - 0.5f) * ViewSize.x * v.Root.localScale.x;
            float sy = (-ViewSize.y / 2 + s.anchoredPosition.y + s.sizeDelta.y / 2) * v.Root.localScale.y;
            var start = v.Root.anchoredPosition + new Vector2(sx + jitter, sy + (_popupSeq % 2) * 24);
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
            float life = Mathf.Max(0.5f, StepSeconds * 1.5f);
            UiTweens.Play(holder.gameObject, "float", life, t =>
            {
                float pop = t < 0.15f ? Mathf.Lerp(0.6f, 1.25f, t / 0.15f) : t < 0.25f ? Mathf.Lerp(1.25f, 1f, (t - 0.15f) / 0.1f) : 1f;
                holder.localScale = Vector3.one * pop;
                holder.anchoredPosition = start + new Vector2(0, 110 * t);
                float a = t < 0.7f ? 1 : 1 - (t - 0.7f) / 0.3f;
                label.color = new Color(color.r, color.g, color.b, a);
                if (cap != null) cap.color = new Color(color.r, color.g, color.b, a);
                if (outline != null) outline.effectColor = new Color(SliceKit.Ink.r, SliceKit.Ink.g, SliceKit.Ink.b, a);
            }, () => Ui.DestroyGo(holder.gameObject));
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
            var word = SliceKit.Chunky(_stage, "SUPERNOVA!", 92, Theme.Hex(0xFFB03A), TextAnchor.MiddleCenter, "SupernovaWord");
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
            var caption = Ui.Label(_order, "NEXT", 22, Theme.TextDim);
            caption.fontStyle = FontStyle.Bold;
            Ui.Size(caption, 80, -1, 0);
            int last = -2, shown = 0;
            for (int i = _next; i < _battle.Log.Count && shown < 6; i++)
            {
                var e = _battle.Log[i];
                if (!IsAttack(e.Action) && e.Action != BattleAction.SkipTurn) continue;
                if (e.Actor == last) continue;
                last = e.Actor;
                var v = View(e.Actor);
                if (v == null) continue;
                var sq = Ui.RoundPanel(_order, v.Info.Role == CombatRole.Worm ? Theme.Worm : RoleColor(v.Info.Role), 12, "Turn");
                Ui.Size(sq, 48, 48, 0);
                if (shown == 0) UiShapes.Outline(sq.rectTransform, Color.white, 12, 4);
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
                    what = e.Amount == 0 && e.Actor == e.Target ? (target?.Info.Name ?? "") + " goes SUPERNOVA" : "Supernova hits " + (target?.Info.Name ?? "") + " for " + Fmt.Num(e.Amount);
                    break;
                case BattleAction.Heal: case BattleAction.Regen: case BattleAction.LifeSteal: what = (target ?? actor)?.Info.Name + " heals " + Fmt.Num(e.Amount); break;
                case BattleAction.Shield: what = (target ?? actor)?.Info.Name + " gains " + Fmt.Num(e.Amount) + " shield"; break;
                case BattleAction.GainRage: case BattleAction.StatusExpired: return;
                default:
                    what = who + " " + e.Action.ToString().ToLowerInvariant() + (tgt.Length > 0 ? " → " + tgt : "")
                           + (e.Dodge ? " (dodged)" : e.Amount > 0 ? " " + Fmt.Num(e.Amount) : "") + (e.Crit ? " CRIT" : "") + (e.Parry ? " parried" : "")
                           + (e.SkillId != null ? " [" + (Content.Skill(e.SkillId)?.Name ?? e.SkillId) + "]" : "");
                    break;
            }
            var line = Ui.Label(_feed, "R" + e.Round + "  " + what, 22, Theme.TextDim);
            _feedLines.Enqueue(line.gameObject);
            while (_feedLines.Count > 12) Ui.DestroyGo(_feedLines.Dequeue());
        }

        /// <summary>HP / shield / rage straight from the log entry (values right after it, see BattleLogEntry).</summary>
        private static void Sync(BattleLogEntry e, FighterView actor, FighterView target)
        {
            if (actor != null) { actor.ShieldNow = e.ActorShield; actor.RageNow = e.ActorRage; }
            if (target != null) { target.HpNow = e.TargetHp; target.ShieldNow = e.TargetShield; target.RageNow = e.TargetRage; }
        }

        /// <summary>Current bar values of fighter <paramref name="index"/> (tests).</summary>
        public bool TryGetBars(int index, out double hp, out double shield, out double rage)
        {
            var v = View(index);
            hp = v?.HpNow ?? 0; shield = v?.ShieldNow ?? 0; rage = v?.RageNow ?? 0;
            return v != null;
        }

        private FighterView View(int index) => index >= 0 && index < _views.Count ? _views[index] : null;

        private void Refresh(FighterView v, double rageMax, bool animate)
        {
            if (v == null) return;
            string hpText = Fmt.Num(Math.Max(0, v.HpNow)) + " / " + Fmt.Num(v.Info.MaxHp);
            TweenBar(v.Hp, Frac(v.HpNow, v.Info.MaxHp), hpText, animate);
            TweenBar(v.Shield, Frac(v.ShieldNow, v.Info.MaxHp), null, animate);
            v.Rage.Set(Frac(v.RageNow, rageMax));
            // status chips: rebuilt only when they change
            var parts = new List<string>();
            foreach (var kv in v.Statuses) parts.Add(kv.Key + ":" + kv.Value);
            string key = string.Join(",", parts);
            if (key == v.ChipKey) return;
            v.ChipKey = key;
            Ui.Clear(v.Chips);
            foreach (var kv in v.Statuses)
            {
                // a status icon with its stack count (reference 3)
                var c = StatusColor(kv.Key);
                // Heat close to its Supernova reads as dangerous: red, a hot outline and a pulse
                bool danger = kv.Key == StatusId.Heat && kv.Value >= HeatDangerAt(Content);
                if (danger) c = Theme.Hex(0xFF2E2E);
                var icon = Ui.RoundPanel(v.Chips, c, 12, "Status:" + kv.Key + (danger ? ":Danger" : ""));
                icon.raycastTarget = false;
                Ui.Size(icon, danger ? 76 : 62, danger ? 76 : 62, 0);
                UiShapes.Outline(icon.rectTransform, danger ? Theme.Hex(0xFFE04A) : SliceKit.Ink, 12, danger ? 5 : 3);
                if (danger && Application.isPlaying) UiPulse.On(icon, 0.12f, 0.6f, 0.75f);
                var g = SliceKit.Chunky(icon.transform, StatusGlyph(kv.Key), danger ? 46 : 38, Color.white, TextAnchor.MiddleCenter, "Glyph");
                Ui.Stretch(g.rectTransform);
                var n = SliceKit.Chunky(icon.transform, kv.Value.ToString(), danger ? 40 : 34, danger ? Theme.Hex(0xFFE04A) : Color.white, TextAnchor.LowerRight, "Stacks");
                Ui.Stretch(n.rectTransform, 0, 0, -12, -10);
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
                _hud.Show(s, _ctx.HasStats ? s.Level : 0, s.XpNeed > 0 ? (float)s.Xp / s.XpNeed : 0, _ctx.SkillCount, _ctx.HasStats);
            }
            if (_heroRage == null) return;
            double rageMax = Content.Combat.RageMax;
            _heroRage.Set(Frac(_hero.RageNow, rageMax));
            _heroRageText.text = (rageMax > 0 ? (100 * _hero.RageNow / rageMax).ToString("0") : "0") + "%";
            Ui.Clear(_heroChips);
            if (_hero.ShieldNow > 0) Ui.Chip(_heroChips, "SHIELD " + Fmt.Num(_hero.ShieldNow), Theme.Hex(0x12303A), Theme.Hex(0xB8F0FF), 20, 40);
            foreach (var kv in _hero.Statuses)
                Ui.Chip(_heroChips, SkillText.StatusName(kv.Key).ToUpperInvariant() + (kv.Value > 1 ? " " + kv.Value : ""), Theme.Tint(StatusColor(kv.Key), 0.4f), StatusColor(kv.Key), 20, 40);
        }

        private void End()
        {
            bool won = _battle.PlayerWon();
            if (_banner != null)
            {
                _banner.text = won ? "VICTORY" : _battle.EndReason == BattleEndReason.RoundCap ? "TIME UP" : "DEFEAT";
                _banner.color = won ? Theme.Gold : Theme.Bad;
                _banner.gameObject.SetActive(true);
                if (Application.isPlaying && !Instant) UiTween.PopIn(_banner, 0.3f);
            }
            if (_endPanel != null) _endPanel.gameObject.SetActive(true);
            if (_autoClose && Application.isPlaying)
                UiSequence.On(Root.gameObject).Wait(Instant ? 0.05f : AutoCloseHold(_elapsed), Close);
        }

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
        /// <summary>False: ATK / DEF / level unknown (a battle outside a run) — the strip shows "–".</summary>
        public bool HasStats;
        /// <summary>Run coins for the chip; &lt; 0 = no coins chip.</summary>
        public long Coins = -1;
        public int SkillCount;
        public List<string> Skills = new List<string>();
        public Dictionary<string, int> Levels = new Dictionary<string, int>();
        /// <summary>The run's "Roll n/N" (null outside a run).</summary>
        public string Rolls;

        public static BattleHudContext FromRun(ActiveRun run, ContentDb c)
        {
            var ctx = new BattleHudContext();
            if (run == null) return ctx;
            ctx.Hud = RunPresentation.Hud(c, run);
            ctx.HasStats = true;
            ctx.Coins = run.Coins;
            ctx.SkillCount = RunSystem.BuildSize(run);
            ctx.Rolls = RunScreen.RollText(c, run);
            ctx.Skills = RunSystem.ActiveSkillIds(run);
            foreach (var id in ctx.Skills) ctx.Levels[id] = RunDraft.Level(run, id);
            return ctx;
        }
    }
}
