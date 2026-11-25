using System.Collections.Generic;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.App
{
    /// <summary>How a game event is shown now that toasts are gone (G1, owner-approved toast map T01–T58).</summary>
    public enum FeedbackKind { Silent, Animation, Banner }

    /// <summary>
    /// Replaces the toast layer (G1): game events become animations (rewards fly into the currency bar, star pops, screen
    /// hooks via <see cref="UiScreen.OnFeedback"/>), hurray banners (one at a time, queued) or nothing (the screen already
    /// shows it); command failures become an error bubble anchored above the tapped button (T57); "not connected" shows a
    /// persistent "Reconnecting…" strip (T56). Its layer sits above the modals and never takes input.
    /// </summary>
    public sealed class GameFeedback
    {
        public const float BubbleLifetime = 2f;
        public const float BannerLifetime = 2.2f;

        private readonly ClientApp _app;
        private readonly RectTransform _layer;
        private readonly Queue<(string text, Color color, string sub)> _banners = new Queue<(string, Color, string)>();
        private GameObject _banner;
        private float _bannerLeft;
        private GameObject _bubble;
        private float _bubbleLeft;
        private GameObject _strip;

        /// <summary>Last banner text shown (tests).</summary>
        public string LastBanner { get; private set; }
        /// <summary>Last error bubble text (tests).</summary>
        public string LastBubble { get; private set; }
        /// <summary>Banners showing + queued.</summary>
        public int BannerCount => (_banner != null ? 1 : 0) + _banners.Count;
        public bool BubbleVisible => _bubble != null;
        public bool Reconnecting => _strip != null;
        /// <summary>The feedback layer (tests).</summary>
        public RectTransform Layer => _layer;

        public GameFeedback(ClientApp app, RectTransform layer)
        {
            _app = app;
            _layer = layer;
            var cg = layer.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false; cg.interactable = false;
        }

        // ---- the event map --------------------------------------------------------------------------

        /// <summary>
        /// How an event type is shown (T01–T58). Pure; tested. Unknown types are silent (T43: dev log only).
        /// </summary>
        public static FeedbackKind KindOf(string type)
        {
            switch (type)
            {
                case "reward": case "city_collected": case "stamina_refilled": case "purchase":            // T01–T04
                case "building_built": case "building_upgraded": case "building_stored":                 // T06, T07, T09 (City screen hooks)
                case "building_demolished":
                case "blueprint_star_up": case "alien_star_up": case "hero_star": case "mount_star":     // T12, T17, T23
                case "part_equipped": case "part_merged": case "worm_talent_tier": case "lab_collected": // T26, T27, T31, T35
                case "run_burrow_descend": case "run_cache_empty": case "boss_awaken":                   // T44, T47, T48
                    return FeedbackKind.Animation;
                case "area_unlocked": case "city_dug":                                                     // T10 (the City crumbles the rubble)
                case "breed_born": case "part_awakened": case "part_replicated":                         // T20, T28, T29
                case "worm_level": case "returning_player": case "arena_week_end": case "planetary_event": // T30, T37, T40, T41
                case "invasion_outcome": case "run_signature_awakened": case "run_encounter":            // T42, T45, T46
                    return FeedbackKind.Banner;
                default:
                    return FeedbackKind.Silent;
            }
        }

        /// <summary>
        /// The events of one result: a run's end (or doubled rewards) shows its rewards on the result screen, so its
        /// "reward" events do not fly. Pure; tested.
        /// </summary>
        public static List<GameEvent> Shown(IList<GameEvent> events)
        {
            var list = new List<GameEvent>();
            if (events == null) return list;
            bool resultScreen = false;
            foreach (var e in events)
                if (e.Type == "run_completed" || e.Type == "run_failed" || e.Type == "run_rewards_doubled") resultScreen = true;
            foreach (var e in events)
                if (!(resultScreen && e.Type == "reward") && KindOf(e.Type) != FeedbackKind.Silent) list.Add(e);
            return list;
        }

        /// <summary>The hurray banner for a banner event: a short shout and a subtitle (player words, display names only).</summary>
        public static string BannerText(GameEvent e, ContentDb c, GameState state, out string sub, out Color color)
        {
            var a = e.Args;
            string A(int i) => i < a.Count ? a[i] : "";
            color = Theme.Gold;
            sub = null;
            switch (e.Type)
            {
                case "area_unlocked": color = Theme.Good; sub = "City area unlocked"; return "NEW LAND!";
                case "city_dug": color = Theme.Good; sub = c?.CityPatch(A(0))?.Name; return "NEW LAND!";
                case "breed_born": color = Theme.Good; sub = "A baby " + (c?.AlienType(A(1))?.Name ?? "alien"); return "HATCHED!";
                case "part_awakened": color = Theme.Warn; sub = Fmt.PartName(c, state, A(0)); return "AWAKENED!";
                case "part_replicated":
                    color = Theme.RarityColor(Rarity.Legendary);
                    sub = c != null && c.PartsConfig.Defs.TryGetValue(A(1), out var rd) ? rd.Name : null;
                    return "REPLICATED!";
                case "worm_level": sub = "Worm level " + A(0); return "LEVEL UP!";
                case "returning_player": color = Theme.Premium; return "WELCOME BACK!";
                case "arena_week_end": sub = "Arena rank " + A(0); return "WEEK OVER!";
                case "planetary_event": color = Theme.Purple; sub = c?.PlanetaryEvent(A(0))?.Name; return "NEW EVENT!";
                case "invasion_outcome":
                    bool won = A(2) == "repelled";
                    color = won ? Theme.Good : Theme.Bad;
                    sub = c?.Invasion(A(0))?.Name;
                    return won ? "REPELLED!" : "OVERRUN!";
                case "run_signature_awakened": color = Theme.RarityColor(Rarity.Mythic); sub = c?.Skill(A(0))?.Name; return "AWAKENED!";
                case "run_encounter":
                    color = Theme.Bad;
                    sub = A(0) == "boss" ? "The boss arena opens" : "A miniboss attacks";
                    return A(0) == "boss" ? "BOSS!" : "MINIBOSS!";
                default: return null;
            }
        }

        // ---- feeds ----------------------------------------------------------------------------------

        /// <summary>The events of one successful command (GameSession.ResultEvents).</summary>
        public void OnEvents(IList<GameEvent> events)
        {
            var c = _app.Session.Content;
            foreach (var e in Shown(events))
            {
                switch (KindOf(e.Type))
                {
                    case FeedbackKind.Banner:
                        var text = BannerText(e, c, _app.Session.State, out var sub, out var color);
                        Banner(text, color, sub);
                        break;
                    case FeedbackKind.Animation:
                        Animate(e);
                        break;
                }
            }
        }

        private void Animate(GameEvent e)
        {
            // the screen first: a card's star row, a slot, the lab node, the board
            var screen = _app.Router?.Current;
            if (screen != null && screen.OnFeedback(e)) return;
            string A(int i) => i < e.Args.Count ? e.Args[i] : "";
            switch (e.Type)
            {
                case "reward":
                    if (A(0) == RewardKinds.Currency) FlyCurrency(A(1), A(2));
                    else FlyToWallet(SummonArt.RewardIcon(new Reward(A(0), A(1), 1)));
                    break;
                case "city_collected": FlyCurrency(A(1), A(2)); break;
                case "stamina_refilled":
                {
                    var chip = _app.Bar.ChipOf(Currencies.Stamina);
                    if (chip != null) FeedbackFx.GlowSweep(chip, Theme.CurrencyColor(Currencies.Stamina));
                    break;
                }
                case "purchase": FlyToWallet(SummonArt.Icon("diamond")); break;
                case "blueprint_star_up": case "alien_star_up": case "hero_star": case "mount_star":
                    StarBurst(); // T12/T17/T23: the new star pops at the card's star-up button
                    break;
                default:
                    // the button that caused it flashes (merge T27, talent tier T31, city buildings T06/T07/T09)
                    var pressed = Ui.LastPressed;
                    if (pressed != null && pressed.gameObject.activeInHierarchy) FeedbackFx.Flash((RectTransform)pressed.transform, Theme.Gold);
                    break;
            }
        }

        /// <summary>Star pops (tests).</summary>
        public int StarPops { get; private set; }

        private void StarBurst()
        {
            StarPops++;
            var star = Ui.Panel(_layer, Color.white, "StarPop");
            star.raycastTarget = false;
            star.sprite = SummonArt.Icon("star"); star.preserveAspect = true;
            var rt = star.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(120, 120);
            rt.position = Source();
            var go = star.gameObject;
            FeedbackFx.StarPop(rt);
            UiTweens.Play(go, "life", 0.9f, null, () => Ui.DestroyGo(go));
        }

        private Vector3 Source()
        {
            var pressed = Ui.LastPressed;
            if (pressed != null && pressed.gameObject.activeInHierarchy) return FeedbackFx.Centre((RectTransform)pressed.transform);
            return FeedbackFx.Centre(_layer);
        }

        /// <summary>Currency icons fly from the source (the tapped button) into the bar chip, which pops (T01/T02).</summary>
        public void FlyCurrency(string currency, string amountText)
        {
            long.TryParse(amountText, out var amount);
            var target = _app.Bar.ChipOf(currency) ?? _app.Bar.Wallet;
            if (target == null) return;
            int n = amount >= 1000 ? 6 : amount >= 100 ? 5 : amount >= 10 ? 4 : 3;
            FeedbackFx.FlyTo(_layer, SummonArt.CurrencyIcon(currency), Source(), target, n);
        }

        private void FlyToWallet(Sprite icon)
        {
            var target = _app.Bar.Wallet;
            if (target == null) return;
            FeedbackFx.FlyTo(_layer, icon, Source(), target, 3, 80);
        }

        // ---- banners --------------------------------------------------------------------------------

        /// <summary>Queue a hurray banner (one at a time, <see cref="BannerLifetime"/> each).</summary>
        public void Banner(string text, Color color, string sub = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            _banners.Enqueue((text, color, sub));
            if (_banner == null) NextBanner();
        }

        private void NextBanner()
        {
            if (_banners.Count == 0) return;
            var (text, color, sub) = _banners.Dequeue();
            LastBanner = text;
            _banner = RevealBanner.Show(_layer, text, color, sub, 0.62f).gameObject;
            _bannerLeft = BannerLifetime;
        }

        // ---- errors / connection --------------------------------------------------------------------

        /// <summary>
        /// A command failed (T57): the tapped button shakes and a small bubble with the reason shows above it for
        /// <see cref="BubbleLifetime"/> s (above the screen's middle when nothing was tapped).
        /// </summary>
        public void Error(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return;
            if (reason == "Not connected") { ShowReconnecting(true); return; }
            LastBubble = reason;
            var pressed = Ui.LastPressed;
            RectTransform anchor = pressed != null && pressed.gameObject.activeInHierarchy ? (RectTransform)pressed.transform : null;
            if (anchor != null) FeedbackFx.Shake(anchor);
            Bubble(reason, anchor, Theme.Bad, "ErrorBubble", false);
        }

        /// <summary>
        /// A small info bubble anchored to <paramref name="anchor"/> (above it, or under it with <paramref name="below"/>;
        /// the last pressed button when null) for <see cref="BubbleLifetime"/> s: the run level tooltip (T50), a card's
        /// short text, the Auto Roll hint.
        /// </summary>
        public void Tip(string text, RectTransform anchor = null, bool below = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            LastTip = text;
            if (anchor == null)
            {
                var pressed = Ui.LastPressed;
                if (pressed != null && pressed.gameObject.activeInHierarchy) anchor = (RectTransform)pressed.transform;
            }
            Bubble(text, anchor, Theme.Accent, "TipBubble", below);
        }

        /// <summary>Last floating number (tests).</summary>
        public string LastFloat { get; private set; }

        /// <summary>A number pops over <paramref name="anchor"/>, rises and fades (the Observatory hit, T55).</summary>
        public void FloatNumber(RectTransform anchor, string text, Color color)
        {
            LastFloat = text;
            var t = Screens.SliceKit.Chunky(_layer, text, 64, color, TextAnchor.MiddleCenter, "FloatNumber");
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, 0.8f); o.effectDistance = new Vector2(3, -3);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(400, 90);
            Vector3 start = anchor != null ? FeedbackFx.Centre(anchor) : FeedbackFx.Centre(_layer);
            rt.position = start;
            float rise = 140 * _layer.lossyScale.y;
            var go = t.gameObject;
            UiTweens.Play(go, "float", 0.9f, k =>
            {
                if (rt == null) return;
                rt.position = start + new Vector3(0, rise * k, 0);
                rt.localScale = Vector3.one * (k < 0.15f ? Mathf.Lerp(0.6f, 1.25f, k / 0.15f) : Mathf.Lerp(1.25f, 1f, (k - 0.15f) / 0.85f));
                t.color = new Color(color.r, color.g, color.b, k < 0.6f ? 1 : 1 - (k - 0.6f) / 0.4f);
            }, () => Ui.DestroyGo(go));
        }

        /// <summary>Last tip text (tests).</summary>
        public string LastTip { get; private set; }

        private void Bubble(string text, RectTransform anchor, Color accent, string name, bool below)
        {
            if (_bubble != null) Ui.DestroyGo(_bubble);
            var bubble = Ui.RoundPanel(_layer, new Color(accent.r * 0.18f, accent.g * 0.18f, accent.b * 0.18f, 0.97f), 22, name);
            bubble.raycastTarget = false;
            UiShapes.Outline(bubble.rectTransform, accent, 22, 3);
            var h = bubble.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(22, 22, 10, 10);
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;
            var fit = bubble.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var label = Ui.Label(bubble.transform, text, Theme.FontSmall, Color.Lerp(accent, Color.white, 0.65f), TextAnchor.MiddleCenter, "Text");
            bool longText = text.Length > 48;
            label.horizontalOverflow = longText ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            if (longText) { Ui.Size(label, 760, -1, 0); fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; bubble.rectTransform.sizeDelta = new Vector2(804, 0); }
            else Ui.Size(label, -1, -1, 0);
            var rt = bubble.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, below ? 1 : 0);
            if (anchor != null)
            {
                var c = new Vector3[4];
                anchor.GetWorldCorners(c);
                float gap = 8 * _layer.lossyScale.y;
                rt.position = new Vector3((c[1].x + c[2].x) * 0.5f, below ? c[0].y - gap : c[1].y + gap, 0);
                // keep it on screen horizontally
                var lc = new Vector3[4];
                _layer.GetWorldCorners(lc);
                float half = (longText ? 804 : 400) * 0.5f * _layer.lossyScale.x;
                rt.position = new Vector3(Mathf.Clamp(rt.position.x, lc[0].x + half, lc[2].x - half), rt.position.y, 0);
            }
            else rt.anchoredPosition = Vector2.zero;
            UiTween.PopIn(bubble, 0.15f);
            _bubble = bubble.gameObject;
            _bubbleLeft = BubbleLifetime;
        }

        /// <summary>The persistent "Reconnecting…" strip at the top (T56), until a command succeeds again.</summary>
        public void ShowReconnecting(bool on)
        {
            if (!on) { if (_strip != null) Ui.DestroyGo(_strip); _strip = null; return; }
            if (_strip != null) return;
            var strip = Ui.Panel(_layer, new Color(0.35f, 0.22f, 0.05f, 0.95f), "Reconnecting");
            strip.raycastTarget = false;
            var rt = strip.rectTransform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 52);
            var t = Ui.Label(strip.transform, "Reconnecting…", Theme.FontSmall, Theme.Warn, TextAnchor.MiddleCenter, "Text");
            Ui.Stretch(t.rectTransform);
            UiPulse.On(t, 0.02f, 1.6f, 0.6f);
            _strip = strip.gameObject;
        }

        public void Tick(float dt)
        {
            if (_bubble != null)
            {
                _bubbleLeft -= dt;
                if (_bubbleLeft <= 0) { Ui.DestroyGo(_bubble); _bubble = null; }
            }
            if (_banner != null)
            {
                _bannerLeft -= dt;
                if (_bannerLeft <= 0) { Ui.DestroyGo(_banner); _banner = null; NextBanner(); }
            }
            if (_strip != null && _app.Session.IsReady && _app.Session.LastResult != null && _app.Session.LastResult.Ok) ShowReconnecting(false);
        }
    }
}
