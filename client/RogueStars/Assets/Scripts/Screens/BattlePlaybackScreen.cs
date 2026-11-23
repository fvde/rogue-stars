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
    /// Animates a BattleResult log: placeholder fighters (worm, aliens, mount, enemies) with HP / shield / rage bars,
    /// status badges, floating numbers and crit/dodge/parry popups. Speed ×1/×2, ×3 with the monthly pass.
    /// </summary>
    public sealed class BattlePlaybackScreen : UiScreen
    {
        private sealed class FighterView
        {
            public BattleFighterInfo Info;
            public RectTransform Card;
            public Image Sprite;
            public UiBar Hp, Shield, Rage;
            public Text Status;
            public double HpNow, ShieldNow, RageNow;
            public readonly Dictionary<StatusId, int> Statuses = new Dictionary<StatusId, int>();
            public bool Visible = true;
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
        private bool _built;
        private RectTransform _feed;
        private readonly Queue<GameObject> _feedLines = new Queue<GameObject>();
        private readonly List<Button> _speedButtons = new List<Button>();

        private void RecolorSpeed()
        {
            for (int i = 0; i < _speedButtons.Count; i++) ((Image)_speedButtons[i].targetGraphic).color = _speed == i + 1 ? Theme.Accent : Theme.Button;
        }

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
        /// <summary>Extra playback multiplier for scripted playthroughs/tests (1 in the game).</summary>
        public static float TimeScale = 1f;

        protected override void Build(RectTransform body)
        {
            if (_built) { _next = 0; _views.Clear(); _feedLines.Clear(); }
            _speedButtons.Clear();
            _built = true;
            if (_battle == null) { Note(body, "No battle to show."); return; }
            var top = Ui.Row(body, 80, 10, Theme.Header, 12);
            _round = Ui.Label(top, "Round 1", Theme.FontLarge);
            bool x3 = ShopSystem.HasBattleSpeed3(State, Content, Now);
            for (int s = 1; s <= 3; s++)
            {
                int sp = s;
                bool allowed = s < 3 || x3;
                _speedButtons.Add(Ui.Button(top, "×" + s + (allowed ? "" : "\n<size=18>pass</size>"), () => { _speed = sp; RecolorSpeed(); }, _speed == s ? Theme.Accent : Theme.Button, 110, 70, allowed, 28));
            }

            var arena = Ui.Row(body, -1, 20);
            arena.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            arena.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            var left = Ui.Column(arena, 10, 0, null, "Player");
            var right = Ui.Column(arena, 10, 0, null, "Enemy");
            Ui.Size(left, -1, -1, 1); Ui.Size(right, -1, -1, 1);
            foreach (var f in _battle.Fighters) _views.Add(Fighter(f.Side == BattleSide.Player ? left : right, f));

            _banner = Ui.Label(body, "", Theme.FontTitle, Theme.Text, TextAnchor.MiddleCenter, "Banner");
            _feed = Ui.Column(body, 4, 16, Theme.Panel, "Feed");
            Ui.Label(_feed, "Battle log", Theme.FontSmall, Theme.Accent);
            _endPanel = Ui.Row(body, Theme.ButtonHeight, 12);
            Ui.Button(_endPanel, "Continue", Close, Theme.Accent);
            _endPanel.gameObject.SetActive(false);
            if (_speed == 3 && !x3) _speed = 2;
        }

        private FighterView Fighter(RectTransform parent, BattleFighterInfo f)
        {
            var v = new FighterView { Info = f, HpNow = f.StartHp, ShieldNow = f.StartShield, RageNow = f.StartRage };
            var card = Ui.Column(parent, 4, 10, Theme.Panel, "Fighter:" + f.Index);
            v.Card = card;
            var head = Ui.Row(card, 110, 10);
            Ui.Size(head, flexHeight: 0); // cards stretch to the taller side; the sprite row keeps its size
            v.Sprite = Ui.Panel(head, RoleColor(f.Role), "Sprite");
            Ui.Size(v.Sprite, 110, 110, 0);
            var letter = Ui.Label(v.Sprite.transform, RoleLetter(f.Role), 40, Color.white, TextAnchor.MiddleCenter);
            letter.fontStyle = FontStyle.Bold;
            Ui.Stretch(letter.rectTransform);
            Ui.Label(head, f.Name + "\n<size=20>" + f.Role + "</size>", Theme.FontSmall);
            v.Hp = Ui.Bar(card, Frac(f.StartHp, f.MaxHp), f.Side == BattleSide.Player ? Theme.Good : Theme.Bad, Fmt.Num(f.StartHp), 36);
            v.Shield = Ui.Bar(card, Frac(f.StartShield, f.MaxHp), Theme.Premium, null, 12);
            v.Rage = Ui.Bar(card, Frac(f.StartRage, Content.Combat.RageMax), Theme.Warn, null, 12);
            v.Status = Ui.Label(card, "", 22, Theme.Warn);
            if (f.SummonedBy >= 0) { v.Visible = false; card.gameObject.SetActive(false); }
            return v;
        }

        private static float Frac(double a, double b) => b > 0 ? (float)(a / b) : 0;

        public static Color RoleColor(CombatRole r)
        {
            switch (r)
            {
                case CombatRole.Worm: return Theme.Hex(0xFF8FB1);
                case CombatRole.Alien: return Theme.Hex(0xC77DFF);
                case CombatRole.Mount: return Theme.Hex(0x6FD3FF);
                case CombatRole.Summon: return Theme.Hex(0x8BE04E);
                case CombatRole.Elite: return Theme.ValenceColor(Valence.Elite);
                case CombatRole.Boss: return Theme.ValenceColor(Valence.Boss);
                default: return Theme.ValenceColor(Valence.Enemy);
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

        // ---- playback ----------------------------------------------------------------------------

        public override void Frame(float dt)
        {
            if (_battle == null || Root == null) return;
            if (Finished) return;
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

        private void Step(bool animate)
        {
            var e = _battle.Log[_next++];
            if (_round != null) _round.text = "Round " + e.Round + " / " + _battle.Rounds;
            var actor = View(e.Actor);
            var target = View(e.Target) ?? actor;
            if (actor != null && animate) Pulse(actor.Sprite.rectTransform);
            double rageMax = Content.Combat.RageMax;
            switch (e.Action)
            {
                case BattleAction.Basic:
                case BattleAction.Combo:
                case BattleAction.Counter:
                case BattleAction.Skill:
                case BattleAction.Rage:
                case BattleAction.StatusTick:
                    if (target == null) break;
                    if (e.Dodge) { Popup(target, "DODGE", Theme.Premium, animate); break; }
                    Popup(target, (e.Crit ? "CRIT " : "") + "-" + Fmt.Num(e.Amount) + (e.Parry ? " PARRY" : ""), e.Crit ? Theme.Warn : Theme.Bad, animate, e.Crit ? 44 : 34);
                    break;
                case BattleAction.Heal:
                case BattleAction.Regen:
                case BattleAction.LifeSteal:
                    if (target == null) break;
                    Popup(target, "+" + Fmt.Num(e.Amount), Theme.Good, animate);
                    break;
                case BattleAction.Shield:
                    if (target == null) break;
                    Popup(target, "+" + Fmt.Num(e.Amount) + " shield", Theme.Premium, animate);
                    break;
                case BattleAction.StatusApplied:
                    if (target == null) break;
                    target.Statuses[e.Status] = Math.Max(1, e.Stacks);
                    Popup(target, e.Status.ToString().ToUpperInvariant(), Theme.Warn, animate, 28);
                    break;
                case BattleAction.StatusExpired:
                    target?.Statuses.Remove(e.Status);
                    break;
                case BattleAction.StatusResisted:
                    if (target != null) Popup(target, "RESIST", Theme.TextDim, animate, 28);
                    break;
                case BattleAction.Buff:
                    if (target != null) Popup(target, "BUFF", Theme.Good, animate, 28);
                    break;
                case BattleAction.SkipTurn:
                    if (actor != null) Popup(actor, "STUNNED", Theme.TextDim, animate, 28);
                    break;
                case BattleAction.Summon:
                    if (target != null && !target.Visible) { target.Visible = true; target.Card.gameObject.SetActive(true); }
                    break;
                case BattleAction.Death:
                    if (target != null) { target.Sprite.color = Theme.Locked; Popup(target, "KO", Theme.TextDim, animate, 40); }
                    break;
            }
            Sync(e, actor, View(e.Target));
            Refresh(actor, rageMax);
            if (target != actor) Refresh(target, rageMax);
            Feed(e, actor, target);
            if (Finished) End();
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
            while (_feedLines.Count > 10) Ui.DestroyGo(_feedLines.Dequeue());
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

        private static void Refresh(FighterView v, double rageMax)
        {
            if (v == null) return;
            v.Hp.Set(Frac(v.HpNow, v.Info.MaxHp), Fmt.Num(Math.Max(0, v.HpNow)) + " / " + Fmt.Num(v.Info.MaxHp));
            v.Shield.Set(Frac(v.ShieldNow, v.Info.MaxHp));
            v.Rage.Set(Frac(v.RageNow, rageMax));
            var parts = new List<string>();
            foreach (var kv in v.Statuses) parts.Add(kv.Key + (kv.Value > 1 ? "×" + kv.Value : ""));
            v.Status.text = string.Join(" ", parts);
        }

        private static void Pulse(RectTransform rt)
        {
            if (!Application.isPlaying) return;
            UiSequence.On(rt.gameObject).Then(0.18f, t => rt.localScale = Vector3.one * (1f + 0.18f * Mathf.Sin(t * Mathf.PI)));
        }

        private void Popup(FighterView v, string text, Color color, bool animate, int size = 34)
        {
            if (!animate || v == null) return;
            var label = Ui.Label(v.Card, text, size, color, TextAnchor.MiddleCenter, "Popup");
            label.fontStyle = FontStyle.Bold;
            var le = label.GetComponent<LayoutElement>();
            le.ignoreLayout = true;
            var rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(400, 60);
            // Rise over the sprite row, inside the card (never into the round header above).
            rt.anchoredPosition = new Vector2(0, -110);
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            UiSequence.On(label.gameObject).Then(0.7f, t =>
            {
                rt.anchoredPosition = new Vector2(0, -110 + 60 * t);
                label.color = new Color(color.r, color.g, color.b, 1 - t * t);
            }, () => Ui.DestroyGo(label.gameObject));
        }

        private void End()
        {
            bool won = _battle.PlayerWon();
            if (_banner != null)
            {
                _banner.text = won ? "VICTORY" : _battle.EndReason == BattleEndReason.RoundCap ? "TIME UP" : "DEFEAT";
                _banner.color = won ? Theme.Good : Theme.Bad;
            }
            if (_endPanel != null) _endPanel.gameObject.SetActive(true);
            if (_autoClose && Application.isPlaying)
                UiSequence.On(Root.gameObject).Wait(0.8f, Close);
        }

        /// <summary>The end panel's Continue button: back to the run.</summary>
        public void Continue() => Close();

        private void Close()
        {
            if (App.Router.Current == this) App.Router.Pop();
        }
    }
}
