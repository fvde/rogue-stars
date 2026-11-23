using System;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Vector2 = UnityEngine.Vector2;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// Plays a run mini-game on a GAME tile (GDD §3.6), one full screen over the run: sends RunMinigameStart (the rules pick
    /// the game, seed and reward), shows the title, the one-line instruction, the reward and an animated hint (a finger
    /// tapping / dragging / swiping on the highlighted target), PLAY → 3-2-1 → the game (timer bar, status line) → the tier
    /// medal → RunMinigameFinish {tier}; the run screen then shows the result (and the skill draft for a skill roll).
    /// The client decides the tier (thresholds in each game's logic class). Skip = finish as Miss.
    /// </summary>
    public sealed class MinigameHost : MonoBehaviour
    {
        /// <summary>Tests: no waits (intro, countdown and medal pass at once; the game still needs steps or a tier).</summary>
        public static bool Instant;
        /// <summary>Skip flag for tests: right after the start the game finishes with this tier (no play).</summary>
        public static MinigameTier? AutoTier;
        /// <summary>Demo / playthrough: the game's scripted player plays aiming this tier (PLAY is pressed by itself).</summary>
        public static MinigameTier? Autoplay;
        /// <summary>Speed-up for scripted play mode runs.</summary>
        public static float TimeScale = 1;

        public enum Stage { Starting, Intro, Countdown, Playing, Medal, Finishing, Done }

        public const float CountdownSeconds = 1.5f, MedalSeconds = 1.6f;

        public Stage Phase { get; private set; } = Stage.Starting;
        public MinigameLogic Game { get; private set; }
        public string GameId { get; private set; }
        public MinigameReward Reward { get; private set; }
        public UiModal Modal { get; private set; }
        public MinigameTier? SentTier { get; private set; }
        public MinigameRenderer Renderer { get; private set; }

        private GameSession _session;
        private RectTransform _root, _arena, _overlay, _hint;
        private Text _title, _instruction, _status, _big;
        private UiBar _timer;
        private Button _play, _skip;
        private float _t, _acc;

        /// <summary>Opens the host over <paramref name="layer"/> for the active run's GAME tile.</summary>
        public static UiModal Open(RectTransform layer, GameSession session)
        {
            // the start command rebuilds the run screen, which asks again: one host per GAME tile
            foreach (var open in layer.GetComponentsInChildren<MinigameHost>())
                if (open.Phase != Stage.Done && open.Modal != null && open.Modal.IsOpen) return open.Modal;
            var bg = Ui.Panel(layer, new Color(0.035f, 0.04f, 0.09f, 0.985f), "Modal:Minigame");
            Ui.Stretch(bg.rectTransform);
            var host = bg.gameObject.AddComponent<MinigameHost>();
            host._session = session;
            host._root = bg.rectTransform;
            host.Modal = new UiModal(bg.gameObject);
            host.Begin();
            return host.Modal;
        }

        private ContentDb Content => _session.Content;

        private void Begin()
        {
            var run = _session.State?.Run.Active;
            if (run == null || run.Phase != RunPhase.Minigame) { Close(); return; }
            BuildChrome();
            if (run.Minigame.Started) { Setup(); return; } // reopened mid-game: same game and seed, from the start
            _status.text = "Opening the arcade…";
            _session.Run(new RunMinigameStart(), r =>
            {
                if (this == null || Phase == Stage.Done) return;
                if (!r.Ok) { Close(); return; }
                Setup();
            });
        }

        private void Setup()
        {
            var m = _session.State?.Run.Active?.Minigame;
            if (m == null || !m.Started) { Close(); return; }
            GameId = m.GameId;
            Reward = m.Reward;
            Game = MinigameCatalog.Create(m.GameId, m.Seed);
            var def = Content.Minigame(m.GameId);
            _title.text = def != null ? def.Name : m.GameId;
            _title.font = Ui.FontFor(_title.text);
            _instruction.text = def != null ? def.Instruction : "";
            _status.text = Reward == MinigameReward.SkillRoll ? "Prize: a SKILL ROLL — better medal, rarer skills" : "Prize: RUN COINS — better medal, more coins";
            Renderer = new MinigameRenderer(_arena);
            Renderer.Render(Game);
            Phase = Stage.Intro;
            _t = 0;
            _play.gameObject.SetActive(true);
            _skip.gameObject.SetActive(true);
            UiTween.PopIn(_arena, 0.25f);
            if (AutoTier.HasValue) { Send(AutoTier.Value); return; }
            if (Autoplay.HasValue || Instant) StartCountdown();
        }

        // ---- chrome -----------------------------------------------------------------------------

        private void BuildChrome()
        {
            var col = Ui.Rect(_root, "Column");
            col.anchorMin = new Vector2(0.5f, 0); col.anchorMax = new Vector2(0.5f, 1); col.pivot = new Vector2(0.5f, 1);
            col.sizeDelta = new Vector2(960, 0); col.anchoredPosition = Vector2.zero;
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 10; v.padding = new RectOffset(0, 0, 24, 0);
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            var top = Ui.Row(col, 60, 12);
            Ui.Chip(top, "MINI-GAME", Theme.Hex(0x2E3570), Theme.Premium, 26, 54);
            Ui.Spacer(top);
            _skip = Ui.Button(top, "Skip", () => Send(MinigameTier.Miss), Theme.Button, 170, 60, true, 26);
            _skip.gameObject.SetActive(false);
            _title = Ui.Display(col, "Mini-game", 64, Theme.Cream, TextAnchor.MiddleCenter, "Title");
            Ui.Size(_title, height: 76);
            _instruction = Ui.Label(col, "", 34, Theme.Text, TextAnchor.MiddleCenter, "Instruction");
            _instruction.fontStyle = FontStyle.Bold;
            Ui.Size(_instruction, height: 84);
            _timer = Ui.Bar(col, 1, Theme.Good, null, 22);
            _status = Ui.Label(col, "", 28, Theme.TextDim, TextAnchor.MiddleCenter, "Status");
            Ui.Size(_status, height: 40);

            var frame = Ui.RoundPanel(col, Theme.Hex(0x0E1124), 48, "ArenaFrame");
            Ui.Size(frame, height: MinigameLogic.Height + 20);
            UiShapes.Outline(frame.rectTransform, Theme.Hex(0x3E4690), 48, 5);
            var mask = Ui.Stretch(Ui.Rect(frame.transform, "Mask"), 10, 10, 10, 10);
            mask.gameObject.AddComponent<RectMask2D>();
            _arena = Ui.Rect(mask, "Arena");
            _arena.anchorMin = _arena.anchorMax = new Vector2(0.5f, 0.5f);
            _arena.pivot = Vector2.zero;
            _arena.sizeDelta = new Vector2(MinigameLogic.Width, MinigameLogic.Height);
            _arena.anchoredPosition = new Vector2(-MinigameLogic.Width / 2, -MinigameLogic.Height / 2);
            var catcher = _arena.gameObject.AddComponent<Image>();
            catcher.color = new Color(0, 0, 0, 0);
            _arena.gameObject.AddComponent<MinigamePointer>().Host = this;

            _overlay = Ui.Rect(frame.transform, "Overlay");
            Ui.Stretch(_overlay);
            _overlay.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            _hint = Ui.Rect(_overlay, "HintFinger");
            _hint.anchorMin = _hint.anchorMax = new Vector2(0.5f, 0.5f);
            _hint.sizeDelta = new Vector2(110, 110);
            var finger = _hint.gameObject.AddComponent<Image>();
            finger.sprite = UiShapes.Circle; finger.color = new Color(1, 1, 1, 0.55f); finger.raycastTarget = false;
            UiShapes.Outline(_hint, Theme.Gold, 55, 8, "FingerRing").raycastTarget = false;
            _hint.gameObject.SetActive(false);
            _big = Ui.Display(_overlay, "", 180, Theme.Gold, TextAnchor.MiddleCenter, "Big");
            Ui.Stretch(_big.rectTransform);
            _big.raycastTarget = false;
            _big.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.7f);
            _play = Ui.Button(_overlay, "PLAY", StartCountdown, Theme.Accent, 420, 140, true, 64);
            var prt = (RectTransform)_play.transform;
            prt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0); prt.pivot = new Vector2(0.5f, 0);
            prt.sizeDelta = new Vector2(420, 140); prt.anchoredPosition = new Vector2(0, 60);
            _play.gameObject.SetActive(false);
        }

        // ---- flow -------------------------------------------------------------------------------

        public void StartCountdown()
        {
            if (Phase != Stage.Intro) return;
            Phase = Stage.Countdown;
            _t = 0;
            _play.gameObject.SetActive(false);
            _hint.gameObject.SetActive(false);
            if (Instant) Begin_Play();
        }

        private void Begin_Play()
        {
            Phase = Stage.Playing;
            _skip.gameObject.SetActive(false);
            _big.text = Instant ? "" : "GO!";
            _t = 0;
            _acc = 0;
        }

        private void Update()
        {
            if (Application.isPlaying) Advance(Time.unscaledDeltaTime * TimeScale);
        }

        /// <summary>Advance the host by <paramref name="dt"/> seconds (EditMode tests call this; play mode every frame).</summary>
        public void Advance(float dt)
        {
            if (Game == null) return;
            _t += dt;
            switch (Phase)
            {
                case Stage.Intro:
                    AnimateHint();
                    Renderer.Render(Game);
                    break;
                case Stage.Countdown:
                {
                    int n = 3 - (int)(_t / (CountdownSeconds / 3));
                    _big.text = n > 0 ? n.ToString() : "";
                    _big.transform.localScale = Vector3.one * (1.3f - 0.3f * ((_t / (CountdownSeconds / 3)) % 1));
                    if (_t >= CountdownSeconds) Begin_Play();
                    break;
                }
                case Stage.Playing:
                {
                    if (_t > 0.5f && _big.text == "GO!") _big.text = "";
                    _acc += dt;
                    int steps = 0;
                    while (_acc >= MinigameLogic.Dt && !Game.Over && steps++ < 12)
                    {
                        _acc -= MinigameLogic.Dt;
                        if (Autoplay.HasValue) Game.Bot(Autoplay.Value);
                        Game.Step();
                    }
                    if (steps >= 12) _acc = 0;
                    _timer.Set(1 - Game.Time / Game.TimeLimit);
                    _timer.Fill.GetComponent<Image>().color = Game.Time / Game.TimeLimit < 0.7f ? Theme.Good : Theme.Warn;
                    _status.text = Game.Status;
                    Renderer.Render(Game);
                    if (Game.Over) ShowMedal();
                    break;
                }
                case Stage.Medal:
                    if (_t >= MedalSeconds || Instant) Send(Game.Tier);
                    break;
            }
        }

        /// <summary>Steps the game with its scripted player until it ends (tests); returns the tier.</summary>
        public MinigameTier PlayToEnd(MinigameTier aim, float maxSeconds = 40)
        {
            var keep = Autoplay;
            Autoplay = aim;
            if (Phase == Stage.Intro) StartCountdown();
            for (float t = 0; t < maxSeconds && Phase != Stage.Medal && Phase != Stage.Finishing && Phase != Stage.Done; t += MinigameLogic.Dt)
                Advance(MinigameLogic.Dt);
            Autoplay = keep;
            return Game.Tier;
        }

        private void AnimateHint()
        {
            // a finger shows the move on the highlighted target: tap (press), drag / swipe (slide to HintTo)
            if (Instant) return;
            _hint.gameObject.SetActive(true);
            var def = Content.Minigame(GameId);
            float cycle = 1.6f, k = (_t % cycle) / cycle;
            var a = Game.HintTarget; var b = Game.HintTo;
            System.Numerics.Vector2 p = def != null && def.Input != MinigameInput.Tap ? System.Numerics.Vector2.Lerp(a, b, Mathf.SmoothStep(0, 1, Mathf.Clamp01((k - 0.2f) / 0.6f))) : a;
            float press = def != null && def.Input == MinigameInput.Tap ? (k > 0.4f && k < 0.6f ? 0.75f : 1) : (k > 0.2f && k < 0.8f ? 0.8f : 1);
            _hint.anchoredPosition = new Vector2(p.X - MinigameLogic.Width / 2, p.Y - MinigameLogic.Height / 2);
            _hint.localScale = Vector3.one * press;
        }

        private void ShowMedal()
        {
            Phase = Stage.Medal;
            _t = 0;
            var tier = Game.Tier;
            var medal = Ui.Rect(_overlay, "Medal");
            medal.anchorMin = medal.anchorMax = new Vector2(0.5f, 0.5f);
            medal.sizeDelta = new Vector2(420, 420);
            var disc = medal.gameObject.AddComponent<Image>();
            disc.sprite = UiShapes.Circle;
            disc.color = MedalColor(tier);
            disc.raycastTarget = false;
            UiShapes.Outline(medal, Theme.Cream, 210, 12, "Rim").raycastTarget = false;
            var label = Ui.Display(medal, tier == MinigameTier.Miss ? "MISS" : tier.ToString().ToUpperInvariant(), 72, Theme.TextOn(MedalColor(tier)), TextAnchor.MiddleCenter, "MedalTier");
            Ui.Stretch(label.rectTransform, 0, 0, 0, 60);
            var score = Ui.Label(medal, Game.Status, 30, Theme.TextOn(MedalColor(tier)), TextAnchor.MiddleCenter, "MedalScore");
            Ui.Stretch(score.rectTransform, 20, 250, 20, 40);
            UiTween.PopIn(medal, 0.35f);
            _big.text = "";
            _status.text = Game.Status;
        }

        public static Color MedalColor(MinigameTier tier) =>
            tier == MinigameTier.Gold ? Theme.Gold : tier == MinigameTier.Silver ? Theme.Hex(0xC9CFF5) : tier == MinigameTier.Bronze ? Theme.Hex(0xD08A4E) : Theme.Hex(0x4A4258);

        /// <summary>RunMinigameFinish with the tier; closes the host (the run screen shows the result).</summary>
        private void Send(MinigameTier tier)
        {
            if (Phase == Stage.Finishing || Phase == Stage.Done) return;
            Phase = Stage.Finishing;
            SentTier = tier;
            _session.Run(new RunMinigameFinish { Tier = tier }, r => Close());
        }

        private void Close()
        {
            Phase = Stage.Done;
            if (Modal != null && Modal.IsOpen) Modal.Close();
        }

        // ---- input ------------------------------------------------------------------------------

        internal void Pointer(int kind, Vector2 local)
        {
            if (Phase == Stage.Intro && kind == 0) { StartCountdown(); return; } // a tap on the arena also starts
            if (Phase != Stage.Playing || Game == null || Game.Over) return;
            var p = new System.Numerics.Vector2(local.x, local.y);
            if (kind == 0) Game.Down(p);
            else if (kind == 1) Game.Move(p);
            else Game.Up(p);
        }
    }

    /// <summary>Pointer events on the arena (touch and mouse) in arena coordinates.</summary>
    public sealed class MinigamePointer : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public MinigameHost Host;

        private void Send(int kind, PointerEventData e)
        {
            if (Host == null) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out var local))
                Host.Pointer(kind, local);
        }

        public void OnPointerDown(PointerEventData e) => Send(0, e);
        public void OnDrag(PointerEventData e) => Send(1, e);
        public void OnPointerUp(PointerEventData e) => Send(2, e);
    }
}
