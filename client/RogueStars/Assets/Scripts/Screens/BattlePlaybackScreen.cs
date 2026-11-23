using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
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
        private UiBar _heroHp, _heroRage;
        private Text _heroHpText, _heroRageText;
        private RectTransform _heroChips;
        private int _popupSeq;

        /// <summary>Stage height and fighter card size (canvas units).</summary>
        public const float StageH = 760;
        public static readonly Vector2 ViewSize = new Vector2(320, 280);

        /// <summary>Skip mode: no animations, the whole log is applied on the next frame and auto playbacks close at once.</summary>
        public static bool Instant;
        /// <summary>Extra playback multiplier for scripted playthroughs/tests (1 in the game).</summary>
        public static float TimeScale = 1f;

        public BattlePlaybackScreen(BattleResult battle, bool autoClose = false)
        {
            _battle = battle;
            _autoClose = autoClose;
        }

        public override string Title => "Battle";
        public override bool AutoRebuild => false;
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
            if (_built) { _next = 0; _views.Clear(); _feedLines.Clear(); }
            _speedButtons.Clear();
            _built = true;
            if (_battle == null) { Note(body, "No battle to show."); return; }
            var top = Ui.Row(body, 84, 10);
            _round = Ui.Display(top, "Round 1 / " + _battle.Rounds, Theme.FontLarge, Theme.Text);
            bool x3 = ShopSystem.HasBattleSpeed3(State, Content, Now);
            for (int s = 1; s <= 3; s++)
            {
                int sp = s;
                bool allowed = s < 3 || x3;
                var b = Ui.Button(top, "×" + s + (allowed ? "" : "\n<size=18>PASS</size>"), () => { _speed = sp; RecolorSpeed(); }, _speed == s ? Theme.Accent : Theme.Track, 120, 76, allowed, 30);
                b.name = "Speed" + s;
                if (!allowed) UiShapes.Outline((RectTransform)b.transform, Theme.Border, Theme.RadiusButton, 3);
                _speedButtons.Add(b);
            }

            // turn order: who acts next
            _order = Ui.Row(body, 56, 10, null, 0, "TurnOrder");
            _order.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;

            // the stage
            var frame = Ui.RoundPanel(body, Theme.Hex(0x26121A), 40, "Stage");
            Ui.Size(frame, -1, StageH);
            var ground = Ui.RoundPanel(frame.transform, Theme.Hex(0x1A0E0A), 40, "Ground");
            ground.raycastTarget = false;
            var grt = ground.rectTransform;
            grt.anchorMin = Vector2.zero; grt.anchorMax = new Vector2(1, 0.3f);
            grt.offsetMin = grt.offsetMax = Vector2.zero;
            var horizon = Ui.Panel(frame.transform, Theme.Hex(0x3A1A14), "Horizon");
            horizon.raycastTarget = false;
            horizon.rectTransform.anchorMin = new Vector2(0, 0.3f); horizon.rectTransform.anchorMax = new Vector2(1, 0.3f);
            horizon.rectTransform.sizeDelta = new Vector2(-40, 6);
            _stage = Ui.Stretch(Ui.Rect(frame.transform, "Fighters"));
            var players = _battle.Fighters.FindAll(f => f.Side == BattleSide.Player);
            var enemies = _battle.Fighters.FindAll(f => f.Side != BattleSide.Player);
            foreach (var f in _battle.Fighters)
            {
                var side = f.Side == BattleSide.Player ? players : enemies;
                _views.Add(Fighter(f, side.IndexOf(f), side.Count));
            }

            // the worm's panel: big HP (with shield) and rage
            _hero = _views.Find(v => v.Info.Role == CombatRole.Worm) ?? _views.Find(v => v.Info.Side == BattleSide.Player);
            if (_hero != null)
            {
                var panel = Ui.Card(body, Theme.PanelAlt, null, 40, 24, 12);
                var hpRow = Ui.Row(panel, 40, 14);
                Ui.Size(Ui.Label(hpRow, "HP", 24, Theme.Text), 80, -1, 0);
                _heroHp = Ui.Bar(hpRow, Frac(_hero.HpNow, _hero.Info.MaxHp), Theme.Good, null, 32);
                _heroHpText = Ui.Display(hpRow, "", 28, Theme.Text, TextAnchor.MiddleRight);
                Ui.Size(_heroHpText, 220, -1, 0);
                var rageRow = Ui.Row(panel, 30, 14);
                Ui.Size(Ui.Label(rageRow, "RAGE", 24, Theme.Text), 80, -1, 0);
                _heroRage = Ui.Bar(rageRow, Frac(_hero.RageNow, Content.Combat.RageMax), Theme.Accent, null, 20);
                _heroRageText = Ui.Label(rageRow, "", 24, Theme.AccentLight, TextAnchor.MiddleRight);
                Ui.Size(_heroRageText, 220, -1, 0);
                _heroChips = Ui.ChipRow(panel, 44, 8);
            }

            _banner = Ui.Display(body, "", 110, Theme.Text, TextAnchor.MiddleCenter, "Banner");
            _banner.gameObject.SetActive(false);
            _endPanel = Ui.Row(body, 120, 12);
            Ui.Button(_endPanel, "Continue", Close, Theme.Accent, -1, 120, true, Theme.FontTitle);
            _endPanel.gameObject.SetActive(false);
            var logButton = Ui.Button(body, "Battle log", null, new Color(0, 0, 0, 0), -1, 84, true, 28);
            logButton.GetComponentInChildren<Text>().color = Theme.TextDim;
            UiShapes.Outline((RectTransform)logButton.transform, Theme.Border, Theme.RadiusButton, 3);
            _feed = Ui.Card(body, Theme.Panel, null, 30, 20, 4, 0, "Feed");
            Ui.Caption(_feed, "Battle log", Theme.Gold);
            _feed.gameObject.SetActive(false);
            logButton.onClick.AddListener(() => _feed.gameObject.SetActive(!_feed.gameObject.activeSelf));
            if (_speed == 3 && !x3) _speed = 2;
            RecolorSpeed();
            foreach (var v in _views) Refresh(v, Content.Combat.RageMax, false);
            RefreshHero();
            TurnOrder();
        }

        /// <summary>Stage position (anchored, from the stage centre) of the n-th of <paramref name="count"/> fighters of a side.</summary>
        public static Vector2 StagePosition(BattleSide side, int index, int count)
        {
            count = Math.Max(1, count);
            float lane = (StageH - 60) / count;
            float y = StageH / 2 - 30 - lane * (index + 0.5f);
            float x = (side == BattleSide.Player ? -1 : 1) * 235;
            return new Vector2(x, y);
        }

        private FighterView Fighter(BattleFighterInfo f, int index, int count)
        {
            var v = new FighterView { Info = f, HpNow = f.StartHp, ShieldNow = f.StartShield, RageNow = f.StartRage };
            var root = Ui.Rect(_stage, "Fighter:" + f.Index);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = ViewSize;
            v.Home = StagePosition(f.Side, index, count);
            root.anchoredPosition = v.Home;
            float lane = (StageH - 60) / Math.Max(1, count);
            if (lane < ViewSize.y) root.localScale = Vector3.one * Mathf.Max(0.55f, lane / ViewSize.y);
            v.Root = root;
            v.Group = root.gameObject.AddComponent<CanvasGroup>();
            bool player = f.Side == BattleSide.Player;
            var align = player ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;

            var name = Ui.Label(root, f.Name, 24, Theme.Text, align, "Name");
            name.fontStyle = FontStyle.Bold;
            Pin(name.rectTransform, 0, 34);
            var hpBg = Ui.Rect(root, "HpHolder");
            Pin(hpBg, -36, 26);
            v.Hp = Ui.Bar(hpBg, Frac(f.StartHp, f.MaxHp), player ? Theme.Good : Theme.Bad, Fmt.Num(f.StartHp), 26);
            Ui.Stretch((RectTransform)v.Hp.Fill.parent);
            var shieldBg = Ui.Rect(root, "ShieldHolder");
            Pin(shieldBg, -64, 8);
            v.Shield = Ui.Bar(shieldBg, Frac(f.StartShield, f.MaxHp), Theme.Premium, null, 8);
            Ui.Stretch((RectTransform)v.Shield.Fill.parent);
            var rageBg = Ui.Rect(root, "RageHolder");
            Pin(rageBg, -76, 8);
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
            var body = Ui.RoundPanel(root, f.Role == CombatRole.Worm ? Theme.Worm : Theme.Shade(color, 0.5f), radius, "Sprite");
            body.raycastTarget = false;
            var srt = body.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(player ? 0.3f : 0.7f, 0);
            srt.pivot = new Vector2(0.5f, 0);
            srt.sizeDelta = size;
            srt.anchoredPosition = new Vector2(0, 46);
            UiShapes.Outline(srt, f.Role == CombatRole.Worm ? Theme.WormLight : color, radius, 6);
            v.Sprite = srt;
            v.SpriteImage = body;
            v.SpriteColor = body.color;
            if (f.Role != CombatRole.Worm)
            {
                var letter = Ui.Display(srt, RoleLetter(f.Role), 44, Color.Lerp(color, Color.white, 0.6f), TextAnchor.MiddleCenter);
                Ui.Stretch(letter.rectTransform);
            }
            else
            {
                var eye = Ui.Dot(srt, Theme.OnAccent, 16, "Eye");
                eye.rectTransform.anchorMin = eye.rectTransform.anchorMax = new Vector2(0.82f, 0.62f);
                eye.rectTransform.sizeDelta = new Vector2(16, 16);
            }
            var chips = Ui.Rect(root, "Status");
            chips.anchorMin = new Vector2(0, 0); chips.anchorMax = new Vector2(1, 0); chips.pivot = new Vector2(0.5f, 0);
            chips.sizeDelta = new Vector2(0, 40); chips.anchoredPosition = Vector2.zero;
            var h = chips.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.childAlignment = player ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            v.Chips = chips;
            if (f.SummonedBy >= 0) { v.Visible = false; root.gameObject.SetActive(false); }
            return v;
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

        public static Color StatusColor(StatusId s)
        {
            switch (s)
            {
                case StatusId.Burn: return Theme.Accent;
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

        public override void Frame(float dt)
        {
            if (_battle == null || Root == null) return;
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
            if (_round != null) _round.text = "Round " + e.Round + " / " + _battle.Rounds;
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
                    else Popup(target, "−" + Fmt.Num(e.Amount), e.Action == BattleAction.StatusTick ? StatusColor(e.Status) : target.Info.Side == BattleSide.Player ? Theme.Hex(0xFF9B3D) : Theme.Cream, animate, 48);
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
                    Popup(target, e.Status.ToString().ToUpperInvariant(), StatusColor(e.Status), animate, 34);
                    break;
                case BattleAction.StatusExpired:
                    target?.Statuses.Remove(e.Status);
                    break;
                case BattleAction.StatusResisted:
                    if (target != null) Popup(target, "RESIST", Theme.TextDim, animate, 32);
                    break;
                case BattleAction.Buff:
                    if (target != null) Popup(target, "BUFF", Theme.Good, animate, 32);
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
            if (animate || Finished) TurnOrder();
            if (Finished) End();
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
            var home = new Vector2(0, 46);
            float amp = crit ? 18 : 10;
            UiTweens.Play(sprite.gameObject, "hit", Beat * 1.1f, t =>
            {
                float wait = 0.3f; // the blow lands mid-lunge
                float k = t < wait ? 0 : (t - wait) / (1 - wait);
                sprite.anchoredPosition = home + new Vector2(amp * Mathf.Sin(k * Mathf.PI * 6) * (1 - k), 0);
                if (!v.Dead) img.color = Color.Lerp(baseColor, Color.white, k <= 0 ? 0 : (1 - k) * 0.85f);
            }, () => { sprite.anchoredPosition = home; if (!v.Dead) img.color = baseColor; });
        }

        private void Dodge(FighterView v)
        {
            var sprite = v.Sprite;
            var home = new Vector2(0, 46);
            float dir = v.Info.Side == BattleSide.Player ? -1 : 1;
            UiTweens.Play(sprite.gameObject, "hit", Beat, t => sprite.anchoredPosition = home + new Vector2(dir * 50 * Mathf.Sin(t * Mathf.PI), 0),
                () => sprite.anchoredPosition = home);
        }

        private void Die(FighterView v, bool animate)
        {
            v.Dead = true;
            v.SpriteImage.color = Theme.Locked;
            var g = v.Group;
            if (!animate) { g.alpha = 0.35f; return; }
            UiTweens.Play(v.Root.gameObject, "fade", Beat * 1.6f, t => g.alpha = Mathf.Lerp(1, 0.35f, t));
        }

        private void Popup(FighterView v, string text, Color color, bool animate, int size, string caption = null)
        {
            if (!animate || v == null || Instant || !Application.isPlaying) return;
            var holder = Ui.Rect(_stage, "Popup");
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
            holder.sizeDelta = new Vector2(420, size + (caption != null ? 40 : 0));
            float jitter = ((_popupSeq++ % 3) - 1) * 46;
            // over the sprite's centre (the sprite sits on the card's bottom, 30% / 70% across)
            var s = v.Sprite;
            float sx = (s.anchorMin.x - 0.5f) * ViewSize.x * v.Root.localScale.x;
            float sy = (-ViewSize.y / 2 + s.anchoredPosition.y + s.sizeDelta.y / 2) * v.Root.localScale.y;
            var start = v.Root.anchoredPosition + new Vector2(sx + jitter, sy + (_popupSeq % 2) * 24);
            holder.anchoredPosition = start;
            var label = Ui.Display(holder, text, size, color, TextAnchor.MiddleCenter);
            Ui.Stretch(label.rectTransform, 0, caption != null ? 36 : 0, 0, 0);
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.1f, 0.03f, 0.02f, 0.9f);
            outline.effectDistance = new Vector2(3, -3);
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
                outline.effectColor = new Color(0.1f, 0.03f, 0.02f, 0.9f * a);
            }, () => Ui.DestroyGo(holder.gameObject));
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
                case BattleAction.StatusApplied: what = who + " applies " + e.Status + (e.Stacks > 1 ? " ×" + e.Stacks : "") + " to " + tgt; break;
                case BattleAction.StatusTick: what = e.Status + " deals " + Fmt.Num(e.Amount) + " to " + (target?.Info.Name ?? ""); break;
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
                var c = StatusColor(kv.Key);
                Ui.Chip(v.Chips, kv.Key.ToString().ToUpperInvariant() + (kv.Value > 1 ? " " + kv.Value : ""), c, Theme.TextOn(c), 18, 34);
            }
        }

        private void TweenBar(UiBar bar, float to, string label, bool animate)
        {
            float from = bar.Value;
            if (!animate || Mathf.Approximately(from, to) || Instant) { bar.Set(to, label); return; }
            if (label != null && bar.Label != null) bar.Label.text = label;
            UiTweens.Play(bar.Fill.gameObject.transform.parent.gameObject, "bar", Beat, t => bar.Set(Mathf.Lerp(from, to, 1 - (1 - t) * (1 - t))), () => bar.Set(to));
        }

        private void RefreshHero()
        {
            if (_hero == null || _heroHp == null) return;
            _heroHp.Set(Frac(_hero.HpNow, _hero.Info.MaxHp));
            _heroHpText.text = Fmt.Num(Math.Max(0, _hero.HpNow)) + " / " + Fmt.Num(_hero.Info.MaxHp);
            double rageMax = Content.Combat.RageMax;
            _heroRage.Set(Frac(_hero.RageNow, rageMax));
            _heroRageText.text = (rageMax > 0 ? (100 * _hero.RageNow / rageMax).ToString("0") : "0") + "%";
            Ui.Clear(_heroChips);
            if (_hero.ShieldNow > 0) Ui.Chip(_heroChips, "SHIELD " + Fmt.Num(_hero.ShieldNow), Theme.Hex(0x12303A), Theme.Hex(0xB8F0FF), 20, 40);
            foreach (var kv in _hero.Statuses)
                Ui.Chip(_heroChips, kv.Key.ToString().ToUpperInvariant() + (kv.Value > 1 ? " " + kv.Value : ""), Theme.Tint(StatusColor(kv.Key), 0.4f), StatusColor(kv.Key), 20, 40);
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
                UiSequence.On(Root.gameObject).Wait(Instant ? 0.05f : 0.8f, Close);
        }

        /// <summary>The end panel's Continue button: back to the run.</summary>
        public void Continue() => Close();

        private void Close()
        {
            if (App.Router.Current == this) App.Router.Pop();
        }
    }
}
