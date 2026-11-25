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
    /// <summary>One revealed item (a pull result, a reactor output, a chest reward).</summary>
    public sealed class RevealItem
    {
        public string Kind;
        public string Id;
        /// <summary>The item's name, once ("Spider Legs" — never "Common Spider Legs / Common").</summary>
        public string Name;
        public Rarity Tier;
        /// <summary>Amount caption under the art ("×2K", "×50"), or empty.</summary>
        public string Amount = "";
        /// <summary>The first copy: a "NEW" ribbon across the card's corner.</summary>
        public bool IsNew;
        public bool Featured;
        /// <summary>A small badge at the card's top ("+50 shards", "+50 SHARDS"), or empty.</summary>
        public string Badge = "";
        /// <summary>A small note under the name ("kept for later"), or empty.</summary>
        public string Note = "";
        /// <summary>Draws the art into the card's art box (alien sprite, hero, currency icon); null = a rarity-coloured placeholder glyph.</summary>
        public Action<RectTransform> Art;
    }

    /// <summary>A button at the end of the reveal (Pull ×1 / ×10 again).</summary>
    public sealed class RevealAction
    {
        public string Caption;
        public string Icon;
        public string Cost;
        public bool Can = true;
        public Color Face = Theme.Accent;
        public string Name;
        public Action OnClick;
    }

    public sealed class RevealOptions
    {
        public string Title;
        /// <summary>Skip the animation (Skip Animation setting): open in the final state.</summary>
        public bool Skip;
        /// <summary>A line under the cards (the summon Luck line), named <see cref="InfoName"/>.</summary>
        public string Info;
        public string InfoName = "Info";
        public List<RevealAction> Again = new List<RevealAction>();
        public string CloseCaption = "OK";
        /// <summary>The modal root is named "Modal:" + Name.</summary>
        public string Name = "Reveal";
        public Color Accent = Theme.Purple;
    }

    /// <summary>
    /// The one reveal ceremony for every pull (SG1): summons, Signals, the Shard Reactor and chests. Charge (a portal glowing up
    /// to the best rarity) → crack (Legendary: short; Mythic: long, with shake and flash) → cards deal in face down → Common to
    /// Epic flip in rarity order → Legendary / Mythic hold (pulse) and flip last → "MYTHIC!" / "LEGENDARY!" banner → the
    /// end buttons (OK, Pull ×1 / ×10 again). A tap anywhere skips to the end state; the banner still shows after a skip.
    /// Outside play mode (and with <see cref="RevealOptions.Skip"/>) it opens in the end state.
    /// </summary>
    public static class PullReveal
    {
        /// <summary>Tests: play the animation in edit mode (driven by <see cref="PullRevealPlayer.Advance"/>).</summary>
        public static bool AnimateInEditMode;

        public const float CardW = 168, CardH = 236, SingleW = 340, SingleH = 470;

        /// <summary>Legendary and Mythic cards stay face down (pulsing) until every other card is open.</summary>
        public static bool Held(Rarity r) => r >= Rarity.Legendary;

        public static Rarity Best(IList<RevealItem> items)
        {
            var best = Rarity.Common;
            if (items != null) foreach (var it in items) if (it.Tier > best) best = it.Tier;
            return best;
        }

        /// <summary>0 = no crack, 1 = Legendary (short), 2 = Mythic (long, shake and flash).</summary>
        public static int CrackStrength(Rarity best) => best >= Rarity.Mythic ? 2 : best >= Rarity.Legendary ? 1 : 0;

        /// <summary>"MYTHIC!", "LEGENDARY!" or null.</summary>
        public static string BannerText(Rarity best) => best >= Rarity.Mythic ? "MYTHIC!" : best >= Rarity.Legendary ? "LEGENDARY!" : null;

        /// <summary>
        /// The order cards turn face up: the non-held cards by rarity (lowest first, then deal order), then the held cards by
        /// rarity — the best card is always last.
        /// </summary>
        public static List<int> FlipOrder(IList<RevealItem> items)
        {
            var low = new List<int>();
            var held = new List<int>();
            for (int i = 0; i < items.Count; i++) (Held(items[i].Tier) ? held : low).Add(i);
            Comparison<int> byTier = (a, b) => items[a].Tier != items[b].Tier ? items[a].Tier.CompareTo(items[b].Tier) : a.CompareTo(b);
            low.Sort(byTier);
            held.Sort(byTier);
            low.AddRange(held);
            return low;
        }

        /// <summary>The best item (last flipped): its name goes under the banner.</summary>
        public static RevealItem BestItem(IList<RevealItem> items)
        {
            var order = FlipOrder(items);
            return order.Count == 0 ? null : items[order[order.Count - 1]];
        }

        // ---- items from results -------------------------------------------------------------------------

        /// <summary>The display name of a revealed reward, once and without its rarity.</summary>
        public static string ItemName(Reward r, ContentDb c)
        {
            if (r == null) return "";
            switch (r.Kind)
            {
                case RewardKinds.Part:
                    return r.Id == "*" ? "Random part" : c != null && c.PartsConfig.Defs.TryGetValue(r.Id, out var pd) ? pd.Name : r.Id;
                case RewardKinds.Rune:
                    return r.Id == "*" ? "Random rune" : c != null && c.PartsConfig.Runes.TryGetValue(r.Id, out var rd) ? rd.Name : r.Id;
                case RewardKinds.Alien: return c?.AlienType(r.Id)?.Name ?? r.Id;
                case RewardKinds.Blueprint: return c?.Blueprint(r.Id)?.Name ?? r.Id;
                default: return SummonView.RewardName(r, c);
            }
        }

        /// <summary>The art of a reward: alien sprite (named "AlienSprite"), hero / mount art, currency icon; parts, runes and blueprints have none yet.</summary>
        public static Action<RectTransform> ArtFor(Reward r)
        {
            if (r == null) return null;
            switch (r.Kind)
            {
                case RewardKinds.Alien:
                {
                    var art = ArtCatalog.Alien(r.Id);
                    if (art == null) return null;
                    return box => Ui.Stretch(SummonKit.Img(box, art, "AlienSprite").rectTransform);
                }
                case RewardKinds.Hero:
                case RewardKinds.HeroShards:
                    return box => Ui.Stretch(SummonKit.FeaturedArt(box, SummonEventType.Hero, r.Id, "Icon"));
                case RewardKinds.Mount:
                    return box => Ui.Stretch(SummonKit.FeaturedArt(box, SummonEventType.Mount, r.Id, "Icon"));
                case RewardKinds.Currency:
                    return box => Ui.Stretch(SummonKit.Img(box, SummonArt.CurrencyIcon(r.Id), "Icon").rectTransform, 14, 14, 14, 14);
                default: return null; // parts (W2 on hold), runes, blueprints: placeholder glyph
            }
        }

        public static List<RevealItem> FromGacha(GachaPullResult result, ContentDb c, GameState s, long now)
        {
            var list = new List<RevealItem>();
            if (result == null) return list;
            foreach (var it in result.Items)
            {
                var r = new Reward(it.Kind, it.Id, it.Amount, it.Rarity);
                list.Add(new RevealItem
                {
                    Kind = it.Kind, Id = it.Id, Tier = it.Tier, Featured = it.Featured, Name = ItemName(r, c), Art = ArtFor(r),
                    IsNew = it.Note == "new",
                    // a blueprint / alien is a batch of 50 shards: NEW when it unlocked the item, else "+50 shards"
                    Badge = it.Shards > 0 && it.Note != "new" ? "+" + Fmt.Num(it.Shards) + " shards" : "",
                    Amount = it.Amount > 1 ? "×" + SummonView.Compact(it.Amount) : "",
                    // a pull result is always shown (published odds); one without a use yet waits in the reward stash
                    Note = s != null && !RewardGate.HasUse(s, c, r, now) ? "kept for later" : "",
                });
            }
            return list;
        }

        public static List<RevealItem> FromSummon(SummonPullResult result, ContentDb c)
        {
            var list = new List<RevealItem>();
            if (result == null) return list;
            foreach (var it in result.Items)
            {
                var r = new Reward(it.Kind, it.Id, it.Amount);
                string badge = SummonView.CopyBadge(it, c);
                list.Add(new RevealItem
                {
                    Kind = it.Kind, Id = it.Id, Tier = it.Tier, Featured = it.Featured, Name = SummonView.RewardName(r, c), Art = ArtFor(r),
                    IsNew = badge == "NEW", Badge = badge == "NEW" ? "" : badge, Amount = SummonView.Amount(r),
                });
            }
            return list;
        }

        /// <summary>Rewards (chests, reactor output): items keep their rarity, currencies are Common.</summary>
        public static List<RevealItem> FromRewards(IEnumerable<Reward> rewards, ContentDb c)
        {
            var list = new List<RevealItem>();
            if (rewards == null) return list;
            foreach (var r in rewards)
            {
                list.Add(new RevealItem
                {
                    Kind = r.Kind, Id = r.Id, Tier = TierOf(r, c), Name = ItemName(r, c), Art = ArtFor(r),
                    Amount = r.Kind == RewardKinds.Currency || r.Amount > 1 ? "×" + SummonView.Compact(r.Amount) : "",
                });
            }
            return list;
        }

        public static Rarity TierOf(Reward r, ContentDb c)
        {
            if (r == null || c == null) return Rarity.Common;
            switch (r.Kind)
            {
                case RewardKinds.Alien: return c.AlienType(r.Id)?.Rarity ?? Rarity.Common;
                case RewardKinds.Blueprint: return c.Blueprint(r.Id)?.Rarity ?? Rarity.Common;
                case RewardKinds.Part: return RunTierOfPart(r.Rarity);
                case RewardKinds.Rune: return (Rarity)Mathf.Clamp(r.Rarity, 0, 4);
                default: return Rarity.Common;
            }
        }

        /// <summary>A part's ladder rarity (14 steps) on the five-step reveal scale.</summary>
        private static Rarity RunTierOfPart(int partRarity)
        {
            // ladder: 0 Common, 1 Uncommon, 2 Rare, 3 Epic, 4 Legendary, 5+ Mythic and up
            if (partRarity >= 5) return Rarity.Mythic;
            if (partRarity == 4) return Rarity.Legendary;
            if (partRarity == 3) return Rarity.Epic;
            if (partRarity >= 1) return Rarity.Rare;
            return Rarity.Common;
        }

        // ---- the ceremony -------------------------------------------------------------------------------

        /// <summary>Opens the reveal on <paramref name="layer"/>. The modal's <see cref="UiModal.Body"/> is the card grid.</summary>
        public static UiModal Show(Transform layer, IList<RevealItem> items, RevealOptions o)
        {
            o = o ?? new RevealOptions();
            var dim = Ui.Panel(layer, new Color(0.03f, 0.02f, 0.07f, 0.94f), "Modal:" + o.Name);
            Ui.Stretch(dim.rectTransform);
            var modal = new UiModal(dim.gameObject);
            var player = dim.gameObject.AddComponent<PullRevealPlayer>();
            player.Build(modal, dim.rectTransform, items ?? new List<RevealItem>(), o);
            bool animate = !o.Skip && (Application.isPlaying || AnimateInEditMode);
            if (animate) player.Play(); else player.Finish();
            return modal;
        }
    }

    /// <summary>Drives one reveal: its own step queue (so tests can advance it in edit mode) and the skip-to-end.</summary>
    public sealed class PullRevealPlayer : MonoBehaviour
    {
        private sealed class Step { public float Duration; public Action<float> Update; public Action Done; }

        private readonly Queue<Step> _steps = new Queue<Step>();
        private Step _current;
        private float _t;

        private UiModal _modal;
        private RectTransform _root, _stage, _grid, _portal, _end, _hint;
        private Image _portalGlow, _flash;
        private readonly List<RectTransform> _cards = new List<RectTransform>();
        private IList<RevealItem> _items;
        private RevealOptions _o;
        private readonly HashSet<int> _flipped = new HashSet<int>();

        /// <summary>Card indices in the order they turned face up (tests).</summary>
        public readonly List<int> FlipLog = new List<int>();
        public bool Finished { get; private set; }
        public bool BannerShown { get; private set; }
        public string Phase { get; private set; } = "";
        public IReadOnlyList<RectTransform> Cards => _cards;

        public void Build(UiModal modal, RectTransform root, IList<RevealItem> items, RevealOptions o)
        {
            _modal = modal; _root = root; _items = items; _o = o;
            // a tap anywhere skips to the end state
            var tap = root.gameObject.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(() => { if (!Finished) Skip(); });

            if (!string.IsNullOrEmpty(o.Title))
            {
                var title = SummonKit.Chunky(root, o.Title, 56, Color.white, TextAnchor.MiddleCenter, "Title");
                SummonKit.Place(title, new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(900, 80));
            }

            _stage = Ui.Rect(root, "Stage");
            Ui.Stretch(_stage);

            // the portal: a glowing disc that charges toward the best rarity's colour
            _portal = Ui.Rect(_stage, "Portal");
            SummonKit.Place(_portal, new Vector2(0.5f, 0.55f), Vector2.zero, new Vector2(420, 420));
            _portalGlow = Ui.Panel(_portal, Theme.RarityColor(Rarity.Rare), "PortalGlow");
            _portalGlow.sprite = UiShapes.Circle; _portalGlow.raycastTarget = false;
            Ui.Stretch(_portalGlow.rectTransform);
            var core = Ui.Panel(_portal, new Color(1, 1, 1, 0.85f), "PortalCore");
            core.sprite = UiShapes.Circle; core.raycastTarget = false;
            SummonKit.Place(core, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150, 150));
            _portal.gameObject.SetActive(false);

            // the cards: 5 per row (one big card for a single pull)
            bool single = items.Count == 1;
            _grid = Ui.Rect(root, "Cards");
            _grid.anchorMin = _grid.anchorMax = new Vector2(0.5f, 0.56f);
            _grid.pivot = new Vector2(0.5f, 0.5f);
            int cols = single ? 1 : Mathf.Min(5, items.Count);
            int rows = single ? 1 : (items.Count + 4) / 5;
            float w = single ? PullReveal.SingleW : PullReveal.CardW, h = single ? PullReveal.SingleH : PullReveal.CardH, gap = 14;
            _grid.sizeDelta = new Vector2(cols * w + (cols - 1) * gap, rows * h + (rows - 1) * gap);
            var gl = _grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(w, h); gl.spacing = new Vector2(gap, gap);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = cols;
            gl.childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < items.Count; i++) _cards.Add(Card(_grid, items[i], w, h));
            modal.Body = _grid;

            _flash = Ui.Panel(root, Color.white, "Flash");
            Ui.Stretch(_flash.rectTransform);
            _flash.raycastTarget = false;
            _flash.gameObject.SetActive(false);

            _hint = Ui.Rect(root, "TapHint");
            SummonKit.Place(SummonKit.Chunky(_hint, "Tap to skip", 30, new Color(0.8f, 0.77f, 1f, 0.85f), TextAnchor.MiddleCenter, "Text"), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 50));
            SummonKit.Place(_hint, new Vector2(0.5f, 0), new Vector2(0, 120), new Vector2(600, 50));
            _hint.gameObject.SetActive(false);
        }

        private RectTransform Card(Transform grid, RevealItem it, float w, float h)
        {
            var color = Theme.RarityColor(it.Tier);
            var root = Ui.Rect(grid, "Card:" + it.Kind + ":" + it.Id);

            // the back: dark plum with a ring; Legendary / Mythic pulse in their colour while held
            var back = Ui.RoundPanel(root, SummonKit.Plum, 24, "Back");
            Ui.Stretch(back.rectTransform);
            UiShapes.Outline(back.rectTransform, SummonKit.PlumLight, 24, 4);
            var ring = Ui.Panel(back.transform, Color.white, "Ring");
            ring.sprite = UiShapes.Ring(6); ring.color = SummonKit.PlumLight; ring.raycastTarget = false;
            SummonKit.Place(ring, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w * 0.45f, w * 0.45f));

            // the front: rarity frame, art, name once, amount, badges, NEW ribbon
            var front = Ui.RoundPanel(root, Color.Lerp(color, SummonKit.Night, 0.3f), 24, "Front");
            Ui.Stretch(front.rectTransform);
            UiShapes.Outline(front.rectTransform, Color.Lerp(color, Color.white, 0.25f), 24, 5, "Frame");
            var glow = Ui.Panel(front.transform, new Color(color.r, color.g, color.b, 0.45f), "ArtGlow");
            glow.sprite = UiShapes.Circle; glow.raycastTarget = false;
            float art = w * 0.72f;
            SummonKit.Place(glow, new Vector2(0.5f, 1), new Vector2(0, -h * 0.08f), new Vector2(art * 1.1f, art * 1.1f));
            var box = Ui.Rect(front.transform, "ArtBox");
            SummonKit.Place(box, new Vector2(0.5f, 1), new Vector2(0, -h * 0.1f), new Vector2(art, art));
            if (it.Art != null) it.Art(box); else Glyph(box, it.Kind, color);

            float s = w / PullReveal.CardW;
            if (!string.IsNullOrEmpty(it.Amount))
            {
                var a = SummonKit.Chunky(front.transform, it.Amount, (int)(28 * s), Color.white, TextAnchor.MiddleCenter, "Amount");
                a.resizeTextForBestFit = true; a.resizeTextMinSize = 12; a.resizeTextMaxSize = (int)(28 * s);
                SummonKit.Place(a, new Vector2(0.5f, 1), new Vector2(0, -h * 0.1f - art + 4 * s), new Vector2(w - 16, 34 * s));
            }
            var name = SummonKit.Chunky(front.transform, it.Name ?? "", (int)(21 * s), Color.white, TextAnchor.MiddleCenter, "Name");
            name.resizeTextForBestFit = true; name.resizeTextMinSize = 12; name.resizeTextMaxSize = (int)(21 * s);
            SummonKit.Place(name, new Vector2(0.5f, 0), new Vector2(0, (string.IsNullOrEmpty(it.Note) ? 8 : 26) * s), new Vector2(w - 14, 58 * s));
            if (!string.IsNullOrEmpty(it.Note))
            {
                var n = Ui.Label(front.transform, it.Note, (int)(15 * s), Theme.Cream, TextAnchor.MiddleCenter, "Note");
                SummonKit.Place(n, new Vector2(0.5f, 0), new Vector2(0, 4 * s), new Vector2(w - 10, 22 * s));
            }
            string badge = !string.IsNullOrEmpty(it.Badge) ? it.Badge : it.Featured ? "FEATURED" : "";
            if (badge.Length > 0)
            {
                var b = Ui.RoundPanel(front.transform, it.Featured && string.IsNullOrEmpty(it.Badge) ? Theme.Hex(0x8A4CE0) : SummonKit.BadgeRed, 14, "Badge");
                SummonKit.Place(b, new Vector2(0.5f, 1), new Vector2(0, 12 * s), new Vector2(w - 18, 34 * s));
                var bt = SummonKit.Chunky(b.transform, badge, (int)((badge.Length > 12 ? 15 : 20) * s), Color.white, TextAnchor.MiddleCenter, "BadgeText");
                bt.resizeTextForBestFit = true; bt.resizeTextMinSize = 10; bt.resizeTextMaxSize = (int)(20 * s);
                Ui.Stretch(bt.rectTransform, 4, 0, 4, 0);
            }
            if (it.IsNew) NewRibbon(front.transform, s);
            front.gameObject.SetActive(false);
            return root;
        }

        /// <summary>The NEW ribbon: a red band across the card's top-right corner.</summary>
        private static void NewRibbon(Transform card, float s)
        {
            var clip = Ui.Rect(card, "NewRibbon");
            clip.anchorMin = clip.anchorMax = new Vector2(1, 1); clip.pivot = new Vector2(1, 1);
            clip.sizeDelta = new Vector2(96 * s, 96 * s); clip.anchoredPosition = Vector2.zero;
            clip.gameObject.AddComponent<RectMask2D>();
            var band = Ui.Panel(clip, SummonKit.BadgeRed, "Band");
            band.raycastTarget = false;
            var brt = band.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(150 * s, 30 * s); brt.anchoredPosition = new Vector2(-34 * s, -34 * s);
            brt.localRotation = Quaternion.Euler(0, 0, -45);
            var t = SummonKit.Chunky(band.transform, "NEW", (int)(20 * s), Color.white, TextAnchor.MiddleCenter, "NewText");
            Ui.Stretch(t.rectTransform);
        }

        /// <summary>Placeholder art (no generated art yet): a white shape on a rarity disc — part = diamond, blueprint = square, rune = disc.</summary>
        private static void Glyph(RectTransform box, string kind, Color color)
        {
            var disc = Ui.Panel(box, Color.Lerp(color, Color.white, 0.2f), "Glyph");
            disc.sprite = UiShapes.Circle; disc.raycastTarget = false;
            SummonKit.Place(disc, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(box.sizeDelta.x * 0.8f, box.sizeDelta.y * 0.8f));
            var shape = Ui.RoundPanel(disc.transform, new Color(1, 1, 1, 0.9f), kind == RewardKinds.Rune ? 999 : 8, "Shape");
            shape.raycastTarget = false;
            float g = box.sizeDelta.x * 0.36f;
            SummonKit.Place(shape, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(g, g));
            if (kind == RewardKinds.Part) shape.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        }

        // ---- sequence -------------------------------------------------------------------------------

        private void Then(float duration, Action<float> update, Action done = null) =>
            _steps.Enqueue(new Step { Duration = Mathf.Max(0.0001f, duration), Update = update, Done = done });

        /// <summary>Queues the whole ceremony.</summary>
        public void Play()
        {
            var best = PullReveal.Best(_items);
            int crack = PullReveal.CrackStrength(best);
            bool single = _items.Count == 1;
            foreach (var c in _cards) c.gameObject.SetActive(false);
            _hint.gameObject.SetActive(true);

            // 1 charge: the portal swells and its colour climbs the rarities up to the best one
            Phase = "charge";
            _portal.gameObject.SetActive(true);
            float charge = crack > 0 ? 0.9f : 0.55f;
            Then(charge, k =>
            {
                float lvl = Mathf.Lerp(0, (float)best, k);
                int lo = Mathf.FloorToInt(lvl);
                var a = Theme.RarityColor((Rarity)Mathf.Clamp(lo, 1, 4));
                var b = Theme.RarityColor((Rarity)Mathf.Clamp(lo + 1, 1, 4));
                _portalGlow.color = Color.Lerp(a, b, lvl - lo);
                _portal.localScale = Vector3.one * (0.4f + 0.6f * k);
                _portal.localRotation = Quaternion.Euler(0, 0, k * 90);
            });

            // 2 crack (Legendary / Mythic): shake and light cracks, then a flash
            if (crack > 0)
            {
                Then(0.0001f, null, () => { Phase = "crack"; Cracks(crack == 2 ? 9 : 5, Theme.RarityColor(best)); });
                float dur = crack == 2 ? 0.9f : 0.45f;
                float amp = crack == 2 ? 16 : 7;
                Then(dur, k =>
                {
                    _portal.anchoredPosition = new Vector2(Mathf.Sin(k * 90) * amp * k, Mathf.Cos(k * 77) * amp * k);
                    var cr = _portal.Find("Cracks");
                    if (cr != null) cr.localScale = Vector3.one * k;
                });
            }
            Then(0.0001f, null, () =>
            {
                Phase = "deal";
                _portal.gameObject.SetActive(false);
                if (crack > 0) Flash(crack == 2 ? 0.95f : 0.6f);
            });

            // 3 deal: cards fly out face down
            for (int i = 0; i < _cards.Count; i++)
            {
                int idx = i;
                Then(single ? 0.15f : 0.07f, null, () =>
                {
                    var c = _cards[idx];
                    c.gameObject.SetActive(true);
                    UiTweens.Play(c.gameObject, "deal", 0.25f, k => { if (c != null) c.localScale = Vector3.one * Mathf.Lerp(0.5f, 1f, 1 - (1 - k) * (1 - k)); });
                    if (PullReveal.Held(_items[idx].Tier)) Hold(idx);
                });
            }

            // 4 flips: low rarities first, held cards last (each after a pulse), the best card ends with the banner
            Then(0.0001f, null, () => Phase = "flip");
            foreach (var idx in PullReveal.FlipOrder(_items))
            {
                int i = idx;
                var tier = _items[i].Tier;
                if (PullReveal.Held(tier)) Then(tier >= Rarity.Mythic ? 0.9f : 0.5f, null, () => Phase = "hold");
                Then(single ? 0.25f : 0.12f, null, () => Flip(i));
            }
            Then(0.3f, null, Finish);
        }

        /// <summary>Advances the queued steps by <paramref name="dt"/> seconds (tests drive it in edit mode).</summary>
        public void Advance(float dt)
        {
            while (dt > 0 && !Finished)
            {
                if (_current == null)
                {
                    if (_steps.Count == 0) return;
                    _current = _steps.Dequeue();
                    _t = 0;
                }
                float use = Mathf.Min(dt, _current.Duration - _t);
                _t += use; dt -= use;
                _current.Update?.Invoke(Mathf.Clamp01(_t / _current.Duration));
                if (_t >= _current.Duration - 1e-6f)
                {
                    var s = _current; _current = null;
                    s.Done?.Invoke();
                }
            }
        }

        private void Update()
        {
            if (Application.isPlaying && !Finished) Advance(Time.unscaledDeltaTime);
        }

        /// <summary>Tap: jump to the end state (every card face up, the banner, the buttons).</summary>
        public void Skip()
        {
            _steps.Clear();
            _current = null;
            Finish();
        }

        /// <summary>The end state: every card face up, the banner for a Legendary / Mythic, the end buttons.</summary>
        public void Finish()
        {
            if (Finished) return;
            _steps.Clear(); _current = null;
            Phase = "done";
            if (_portal != null) _portal.gameObject.SetActive(false);
            if (_flash != null) _flash.gameObject.SetActive(false);
            if (_hint != null) _hint.gameObject.SetActive(false);
            foreach (var i in PullReveal.FlipOrder(_items)) Flip(i, false);
            foreach (var c in _cards) { c.gameObject.SetActive(true); c.localScale = Vector3.one; }
            if (!BannerShown) Banner();
            Finished = true;
            EndButtons();
        }

        private void Hold(int i)
        {
            var back = _cards[i].Find("Back");
            if (back == null) return;
            var color = Theme.RarityColor(_items[i].Tier);
            var glow = Ui.Panel(back, new Color(color.r, color.g, color.b, 0.6f), "HoldGlow");
            glow.raycastTarget = false;
            UiShapes.Round(glow, 24);
            Ui.Stretch(glow.rectTransform, -10, -10, -10, -10);
            glow.transform.SetAsFirstSibling();
            UiPulse.On(glow, 0.06f, 0.8f, 0.3f);
            var ring = back.Find("Ring")?.GetComponent<Image>();
            if (ring != null) ring.color = color;
        }

        private void Flip(int i, bool animate = true)
        {
            if (_flipped.Contains(i)) return;
            _flipped.Add(i);
            FlipLog.Add(i);
            var card = _cards[i];
            card.gameObject.SetActive(true);
            var back = card.Find("Back");
            var front = card.Find("Front");
            if (back != null) back.gameObject.SetActive(false);
            if (front != null) front.gameObject.SetActive(true);
            if (animate && front != null)
            {
                front.localScale = new Vector3(0, 1, 1);
                UiTweens.Play(front.gameObject, "flip", 0.18f, k => { if (front != null) front.localScale = new Vector3(k, 1 + 0.08f * Mathf.Sin(k * Mathf.PI), 1); },
                    () => { if (front != null) front.localScale = Vector3.one; });
                var tier = _items[i].Tier;
                if (tier >= Rarity.Epic) Sparks(card, Theme.RarityColor(tier), tier >= Rarity.Mythic ? 14 : tier >= Rarity.Legendary ? 9 : 5);
                if (tier >= Rarity.Mythic) Flash(0.7f);
                // the best card ends the flips: the banner arrives with it
                var order = PullReveal.FlipOrder(_items);
                if (order.Count > 0 && order[order.Count - 1] == i) Banner();
            }
        }

        private void Banner()
        {
            var best = PullReveal.Best(_items);
            string text = PullReveal.BannerText(best);
            if (text == null || BannerShown) return;
            BannerShown = true;
            RevealBanner.Show(_root, text, Theme.RarityColor(best), PullReveal.BestItem(_items)?.Name, 0.84f);
        }

        private void Cracks(int n, Color color)
        {
            var cr = Ui.Rect(_portal, "Cracks");
            Ui.Stretch(cr);
            for (int k = 0; k < n; k++)
            {
                var line = Ui.Panel(cr, Color.Lerp(color, Color.white, 0.7f), "Crack");
                line.raycastTarget = false;
                var rt = line.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0, 0.5f);
                rt.sizeDelta = new Vector2(150 + 40 * (k % 3), 6);
                rt.localRotation = Quaternion.Euler(0, 0, k * 360f / n + 17 * (k % 2));
            }
            cr.localScale = Vector3.zero;
        }

        private void Flash(float alpha)
        {
            if (_flash == null) return;
            _flash.gameObject.SetActive(true);
            _flash.color = new Color(1, 1, 1, alpha);
            UiTweens.Play(_flash.gameObject, "flash", 0.5f, k => { if (_flash != null) _flash.color = new Color(1, 1, 1, alpha * (1 - k)); },
                () => { if (_flash != null) _flash.gameObject.SetActive(false); });
        }

        private static void Sparks(RectTransform card, Color color, int n)
        {
            if (!Application.isPlaying) return;
            for (int k = 0; k < n; k++)
            {
                var dot = Ui.Dot(card, Color.Lerp(color, Color.white, 0.4f), 14, "Spark");
                dot.raycastTarget = false;
                var rt = dot.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                float a = k * Mathf.PI * 2 / n;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                UiTweens.Play(dot.gameObject, "spark", 0.5f, t =>
                {
                    if (rt == null) return;
                    rt.anchoredPosition = dir * (40 + 140 * t);
                    dot.color = new Color(dot.color.r, dot.color.g, dot.color.b, 1 - t);
                }, () => { if (rt != null) Ui.DestroyGo(rt.gameObject); });
            }
        }

        private void EndButtons()
        {
            if (_end != null) return;
            _end = Ui.Rect(_root, "Buttons");
            SummonKit.Place(_end, new Vector2(0.5f, 0), new Vector2(0, 110), new Vector2(940, 260));
            var col = _end.gameObject.AddComponent<VerticalLayoutGroup>();
            col.spacing = 16; col.childAlignment = TextAnchor.LowerCenter;
            col.childControlWidth = col.childControlHeight = true; col.childForceExpandWidth = true; col.childForceExpandHeight = false;
            if (!string.IsNullOrEmpty(_o.Info))
            {
                var info = SummonKit.Chunky(_end, _o.Info, 30, Color.white, TextAnchor.MiddleCenter, _o.InfoName);
                Ui.Size(info, -1, 56);
            }
            var row = Ui.Row(_end, 120, 20, null, 0, "Row");
            var modal = _modal;
            Ui.Button(row, _o.CloseCaption, () => modal.Close(), SummonKit.Plum, -1, 120, true, 40).name = "Close";
            foreach (var a in _o.Again)
            {
                var act = a;
                var b = SummonKit.ActionButton(row, act.Caption, act.Icon, act.Cost, () => { modal.Close(); act.OnClick?.Invoke(); }, act.Face, -1, 120, act.Can, act.Name);
                if (!act.Can) foreach (var t in b.GetComponentsInChildren<Text>(true)) if (t.name == "Count") t.color = Theme.Bad;
            }
        }
    }
}
