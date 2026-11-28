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
    /// The run's decision screens of the screens pass (references rest, event, event-vendor, chest, shop): events on a
    /// stage with option tiles or at a vendor's counter, the campfire's Train picker, the chest reveal (Mythic choice,
    /// signature offers, guarded loot), the restyled result sheet and the shop.
    /// </summary>
    public sealed partial class RunScreen
    {
        /// <summary>The chest intro plays before a Mythic choice / signature offer / loot (play mode, not in skip mode).</summary>
        public static bool ShowChestIntro => ForceChestIntro || Application.isPlaying && !InstantOutcomes;
        /// <summary>Tests: build the chest intro outside play mode too.</summary>
        public static bool ForceChestIntro;

        // ---- the skills book on every decision ------------------------------------------------------------------

        /// <summary>
        /// Opens the run's skills book on top of whatever is open (owner playtest: "the player must be able to see their
        /// skills at any time"): the decision under it (draft, shop, event, chest, …) stays exactly as it is — nothing is sent,
        /// rerolled or rebuilt — and closing the book returns to it.
        /// </summary>
        public void OpenSkills()
        {
            var run = State.Run.Active;
            if (run != null) SkillsModal(run);
        }

        /// <summary>
        /// The skills book button of a skill selection screen (bottom-right unless placed otherwise; named "SkillsBook"). Run UI
        /// glitches #9 (owner): only the screens that pick or improve a skill show it (level-up draft, Mythic choice, signature
        /// choices, campfire Train, shop); events, chests, results and the revive do not.
        /// </summary>
        private Button DecisionSkillsBook(RectTransform parent, float s, float x = 975, float y = 2272, bool fromBottom = false) =>
            RunScenes.SkillsBook(parent, s, x, y, OpenSkills, fromBottom);

        // ---- events ----------------------------------------------------------------------------------------------

        private UiModal EventModal(ActiveRun run)
        {
            Content.RunEvents.TryGetValue(run.EventId ?? "", out var ev);
            if (ev == null)
                return Modal(run.EventId ?? "Event", b => Ui.Label(b, run.EventId ?? "", Theme.FontNormal), new ModalButton("Continue", () => Run(new RunChooseEventOption { Index = 0 }), Theme.Accent));
            if (ev.Id == "resonance") return ShowChestIntro ? ChestIntro("chest_signature", "You found a Signal!", () => _decision = SignatureReveal(run, ev)) : SignatureReveal(run, ev);
            var look = RunScenes.Look(ev.Id, ev);
            var visible = VisibleOptions(run, ev);
            return look.Kind == EventLayoutKind.Vendor && visible.Count == 2 ? VendorModal(run, ev, look, visible) : SceneModal(run, ev, look, visible);
        }

        /// <summary>
        /// The options shown: a Resonance option whose signature is not offered is hidden, and so is a Rite Offering while the
        /// run owns no skill of its synergy (RunSystem.OptionAvailable).
        /// </summary>
        private List<int> VisibleOptions(ActiveRun run, EventDef ev)
        {
            var list = new List<int>();
            for (int i = 0; i < ev.Options.Count; i++)
            {
                var o = ev.Options[i];
                var sigFx = o.Outcomes.Count > 0 ? o.Outcomes[0].Effects.Find(fx => fx.Kind == RunEffectKind.GainSignature) : null;
                if (sigFx != null && (!int.TryParse(sigFx.Param, out var si) || si < 0 || si >= run.SignatureChoice.Count)) continue;
                if (!RunSystem.OptionAvailable(Content, run, o)) continue;
                list.Add(i);
            }
            return list;
        }

        private static string OptionName(EventOption o) =>
            "Button:" + o.Text + (o.CoinCost > 0 ? " [" + o.CoinCost + " coins]" : "") + (o.ShardCost > 0 ? " [" + o.ShardCost + " shards]" : "");

        private static bool Affordable(ActiveRun run, EventOption o) => run.Coins >= o.CoinCost && run.Shards >= o.ShardCost;

        /// <summary>Picks an option: one that improves a skill opens the Train picker first (when there is a choice).</summary>
        private void ChooseOption(ActiveRun run, EventDef ev, int index, UiModal modal)
        {
            var o = ev.Options[index];
            if (o.Offering) { OfferingPicker(run, ev, index, modal); return; }
            if (RunSystem.ImprovesSkill(o) && RunSystem.TrainChoices(Content, run).Count > 0) { TrainPicker(run, index, modal); return; }
            modal.Close();
            Run(new RunChooseEventOption { Index = index });
        }

        /// <summary>The scene layout (references rest / event): stage on top, title band, flavour line, option tiles.</summary>
        private UiModal SceneModal(ActiveRun run, EventDef ev, EventLook look, List<int> visible)
        {
            var root = RunScenes.Overlay(App.ModalLayer, ev.Name, out var modal, out var frame, out float s);
            var hud = RunScenes.Stage(root, frame, s, Content, run, RunScenes.StageArt(look), null, PauseModal);
            bool campfire = ev.Id == "campfire";
            string flavour = look.Line ?? ev.Text;
            RunScenes.Panel(root, frame, s, look.Title ?? ev.Name, flavour, campfire ? 60 : 46);
            int n = visible.Count;
            var xs = RunScenes.Columns(n);
            float size = n <= 2 ? 320 : 262;
            for (int k = 0; k < n; k++)
            {
                int index = visible[k];
                var o = ev.Options[index];
                string useless = RunSystem.OptionUselessReason(run, o);
                bool ok = Affordable(run, o) && useless == null;
                string outcome = RunScenes.OutcomeText(o, Content, run);
                // #17: the campfire's results in the shared choice palette (they were pale olive on the cream paper)
                if (campfire && RunSystem.ImprovesSkill(o))
                    outcome = RunSystem.TrainChoices(Content, run).Count > 0 ? RunScenes.Good("Improve 1 existing skill")
                        : "Nothing to train: " + RunScenes.Good("+" + Content.RunTuning.DraftEmptyCoins) + " coins";
                else if (campfire) outcome = RunScenes.Good((RunScenes.EffectLines(o, Content).Find(l => l.Key).Value ?? "Rest").Replace("+", ""));
                if (!Affordable(run, o)) outcome = RunScenes.Bad("Not enough " + (run.Coins < o.CoinCost ? "coins" : "shards"));
                else if (useless != null) outcome = RunScenes.Bad(useless); // Rest at full HP: said on the button, not resolved to nothing
                var tile = RunScenes.OptionTile(frame, s, xs[k], 1915, size, o.Text, RunScenes.IconSprite(RunScenes.OptionIcon(o)), outcome, ok,
                    () => ChooseOption(run, ev, index, modal), OptionName(o), n <= 2 ? (campfire ? 62 : 46) : 38, n <= 2 ? (campfire ? 56 : 46) : 36, n <= 2 ? 400 : 330);
                if (campfire) AddAlias(tile, RunSystem.ImprovesSkill(o) ? "Option:Train" : "Option:Rest");
            }
            if (_auto)
            {
                var hint = RunScenes.Text(frame, "Auto mode waits for your choice.", s, 32, RunScenes.Ink, TextAnchor.MiddleCenter, "AutoHint");
                RunScenes.At(hint.rectTransform, s, 540, 2350, 900, 50);
            }
            hud.Root.SetAsLastSibling();
            UiTween.PopIn(frame, 0.12f);
            return modal;
        }

        /// <summary>A second, invisible button over a tile under another name (the showcase and tests find options by role).</summary>
        private static void AddAlias(Button tile, string name)
        {
            var alias = Ui.Button(tile.transform, "", () => tile.onClick.Invoke(), new Color(1, 1, 1, 0.001f), 10, 10, tile.interactable, 10);
            alias.name = name;
            Ui.Stretch((RectTransform)alias.transform);
            var le = alias.GetComponent<LayoutElement>(); if (le != null) le.ignoreLayout = true;
        }

        /// <summary>The vendor layout (reference event-vendor): NPC, speech bubble, two blessing / curse cards with Sign.</summary>
        private UiModal VendorModal(ActiveRun run, EventDef ev, EventLook look, List<int> visible)
        {
            var root = RunScenes.Overlay(App.ModalLayer, ev.Name, out var modal, out var frame, out float s);
            RunScenes.VendorStage(root, frame, s, look.Title ?? ev.Name, look.Art, look.Line ?? ev.Text);
            // E1: the run HUD (HP, coins) stays visible while deciding to pay coins or max HP (#2: the board's strip, in place)
            RunScenes.Hud(root, Content, run, PauseModal);
            float[] xs = { 268, 812 };
            for (int k = 0; k < 2; k++)
            {
                int index = visible[k];
                var o = ev.Options[index];
                string useless = RunSystem.OptionUselessReason(run, o);
                bool ok = Affordable(run, o) && useless == null;
                RunScenes.Halves(o, Content, out var blessing, out var curse, run);
                bool nothing = o.Outcomes.TrueForAll(oc => oc.Effects.Count == 0) && o.CoinCost == 0 && o.ShardCost == 0;
                string sign = nothing ? "Leave" : o.CoinCost > 0 || o.ShardCost > 0 ? "Pay" : "Sign";
                if (!Affordable(run, o)) curse += "\n" + RunScenes.Bad("Not enough " + (run.Coins < o.CoinCost ? "coins" : "shards"));
                else if (useless != null) curse += "\n" + RunScenes.Bad(useless); // the tonic at full HP
                RunScenes.VendorCard(frame, s, xs[k], blessing, curse, sign, ok, () => ChooseOption(run, ev, index, modal), OptionName(o));
            }
            var or = SliceKit.Chunky(frame, "OR", (int)(84 * s), Color.white, TextAnchor.MiddleCenter, "Or");
            RunScenes.At(or.rectTransform, s, 540, 2282, 160, 120);
            UiTween.PopIn(frame, 0.12f);
            return modal;
        }

        /// <summary>
        /// The campfire's Train picker (owner rule): the run's owned skills that can improve, each as a card with its upgrade
        /// inline ("+15% → +25% crit chance"); signatures and maxed skills are not offered. Picking sends the option with the skill.
        /// </summary>
        private void TrainPicker(ActiveRun run, int index, UiModal parent)
        {
            var choices = RunSystem.TrainChoices(Content, run);
            float s = ModalScale();
            // C2: the rest scene (its panel and stage worm) is hidden while the picker is open, the overlay is opaque
            if (parent?.Root != null) parent.Root.SetActive(false);
            var root = SliceCards.Overlay(App.ModalLayer, "Train", out var modal, 1f);
            modal.Closed += () => { if (parent?.Root != null) parent.Root.SetActive(true); };
            Canvas.ForceUpdateCanvases();
            int n = Math.Max(1, choices.Count);
            // C1: the cards live in a scroll area between the ribbon and the footer (note + Back), on any aspect
            const float footer = 2400 - 2120 + 25 + 24;
            var lay = SliceCards.Layout(SliceCards.RefHeight(root, s), Math.Min(n, 3), footer);
            SliceCards.Header(root, s, SliceCards.GoldRibbon, "TRAIN", "TrainTitle", "Pick a skill to improve!", lay.HeroY, lay.RibbonY);
            float top = lay.RibbonY + 150, h = lay.CardH, step = h + 10;
            var view = Ui.Rect(root, "TrainList");
            view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(0, footer * s); view.offsetMax = new Vector2(0, -top * s);
            var viewImg = view.gameObject.AddComponent<Image>(); viewImg.color = new Color(0, 0, 0, 0);
            view.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, (n * step + 10) * s);
            content.anchoredPosition = Vector2.zero;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view; scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            float y = h / 2 + 5;
            foreach (var owned in choices)
            {
                string id = owned;
                string target = RunSystem.TrainTarget(Content, run, id);
                var sk = Content.Skill(target);
                int lv = RunDraft.Level(run, id);
                bool chain = target != id;
                // C3: "Name Lv 2" with the upgrade inline; only a chain upgrade (another skill) keeps its UPGRADE chip
                var spec = new SkillCardSpec
                {
                    Id = id, Tier = RunTuning.RunTier(sk?.Rarity ?? Rarity.Common).ToString(), TierColor = RunPresentation.SkillColor(sk), Family = sk?.Family ?? SkillFamily.None,
                    IconArt = ArtCatalog.SkillIcon(target),
                    Title = chain ? sk?.Name ?? target : SkillText.Title(sk, lv + 1),
                    Body = chain ? Fmt.SkillCard(Content, sk, 0) + " Replaces " + (Content.Skill(id)?.Name ?? id) + "." : Fmt.SkillCard(Content, sk, lv),
                    Chip = chain ? "UPGRADE" : null, ChipName = "UpgradeChip", ChipColor = new Color32(0xD6, 0xC2, 0xFF, 255),
                    Tip = chain ? SkillTip.Of(target, 1) : OfferTip(id, lv), TipContent = Content,
                };
                SliceCards.Card(content, s, y, spec, () => { modal.Close(); parent.Close(); CloseDecision(); Run(new RunChooseEventOption { Index = index, SkillId = id }); }, null, "Train:" + id, h);
                y += step;
            }
            var note = SliceKit.Chunky(root, "Signatures and max-level skills can't train.", (int)(34 * s), new Color(1, 1, 1, 0.8f), TextAnchor.MiddleCenter, "TrainNote");
            SliceKit.Place(note.rectTransform, s, 540, 2120, 1000, 50, true);
            var back = Ui.Button(root, "", modal.Close, SliceCards.Purple, 320 * s, 150 * s, true, 20);
            back.name = "Button:Back";
            SliceKit.Place((RectTransform)back.transform, s, 540, 2260, 320, 150, true);
            UiShapes.Outline((RectTransform)back.transform, IsoArt.Ink, Theme.RadiusButton, 6);
            var bt = SliceKit.Chunky(back.transform, "Back", (int)(64 * s), Color.white, TextAnchor.MiddleCenter, "BackText");
            Ui.Stretch(bt.rectTransform, 0, 0, 0, 8 * s);
            DecisionSkillsBook(root, s, 975, 2262, true);
            UiTween.PopIn(root, 0.15f);
        }

        /// <summary>
        /// A Rite's Offering picker (the Train picker's layout): the run's owned skills of the Rite's synergy as cards, each at
        /// its level; signatures are never listed (RunSystem.OfferChoices). Picking sends the option with the skill to give up.
        /// </summary>
        private void OfferingPicker(ActiveRun run, EventDef ev, int index, UiModal parent)
        {
            var o = ev.Options[index];
            var choices = RunSystem.OfferChoices(Content, run, o.OfferFamily);
            float s = ModalScale();
            if (parent?.Root != null) parent.Root.SetActive(false);
            var root = SliceCards.Overlay(App.ModalLayer, "Offering", out var modal, 1f);
            modal.Closed += () => { if (parent?.Root != null) parent.Root.SetActive(true); };
            Canvas.ForceUpdateCanvases();
            int n = Math.Max(1, choices.Count);
            const float footer = 2400 - 2120 + 25 + 24;
            var lay = SliceCards.Layout(SliceCards.RefHeight(root, s), Math.Min(n, 3), footer);
            var gain = RunScenes.EffectLines(o, Content, run).Find(l => l.Key).Value;
            SliceCards.Header(root, s, SliceCards.GoldRibbon, "OFFERING", "OfferingTitle",
                "Give up 1 " + RunScenes.OfferingName(o.OfferFamily) + (gain != null ? ": " + gain : ""), lay.HeroY, lay.RibbonY);
            float top = lay.RibbonY + 150, h = lay.CardH, step = h + 10;
            var view = Ui.Rect(root, "OfferingList");
            view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(0, footer * s); view.offsetMax = new Vector2(0, -top * s);
            var viewImg = view.gameObject.AddComponent<Image>(); viewImg.color = new Color(0, 0, 0, 0);
            view.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, (n * step + 10) * s);
            content.anchoredPosition = Vector2.zero;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view; scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            float y = h / 2 + 5;
            foreach (var owned in choices)
            {
                string id = owned;
                var sk = Content.Skill(id);
                int lv = RunDraft.Level(run, id);
                var spec = new SkillCardSpec
                {
                    Id = id, Tier = RunTuning.RunTier(sk?.Rarity ?? Rarity.Common).ToString(), TierColor = RunPresentation.SkillColor(sk), Family = sk?.Family ?? SkillFamily.None,
                    IconArt = ArtCatalog.SkillIcon(id), Title = sk != null ? SkillText.Title(sk, lv) : id,
                    Body = Fmt.SkillCard(Content, sk, lv),
                    Chip = "GIVE UP", ChipName = "OfferChip", ChipColor = new Color32(0xFF, 0xC2, 0xC2, 255),
                    Tip = OfferTip(id, lv), TipContent = Content,
                };
                SliceCards.Card(content, s, y, spec, () => { modal.Close(); parent.Close(); CloseDecision(); Run(new RunChooseEventOption { Index = index, SkillId = id }); }, null, "Offer:" + id, h);
                y += step;
            }
            var note = SliceKit.Chunky(root, "The skill and its levels are gone. Signatures can't be offered.", (int)(34 * s), new Color(1, 1, 1, 0.8f), TextAnchor.MiddleCenter, "OfferingNote");
            SliceKit.Place(note.rectTransform, s, 540, 2120, 1000, 50, true);
            var back = Ui.Button(root, "", modal.Close, SliceCards.Purple, 320 * s, 150 * s, true, 20);
            back.name = "Button:Back";
            SliceKit.Place((RectTransform)back.transform, s, 540, 2260, 320, 150, true);
            UiShapes.Outline((RectTransform)back.transform, IsoArt.Ink, Theme.RadiusButton, 6);
            var bt = SliceKit.Chunky(back.transform, "Back", (int)(64 * s), Color.white, TextAnchor.MiddleCenter, "BackText");
            Ui.Stretch(bt.rectTransform, 0, 0, 0, 8 * s);
            DecisionSkillsBook(root, s, 975, 2262, true);
            UiTween.PopIn(root, 0.15f);
        }

        // ---- chest ----------------------------------------------------------------------------------------------

        /// <summary>
        /// The chest moment (reference chest): the board dimmed, a gold ribbon, the big glowing chest, "Tap anywhere to
        /// open!"; a tap bursts it open and <paramref name="reveal"/> builds what was inside (in the card family). It always
        /// waits for the player's tap (auto mode only rolls).
        /// </summary>
        private UiModal ChestIntro(string prop, string title, Action reveal)
        {
            var root = SliceCards.Overlay(App.ModalLayer, "Chest", out var modal, 0.9f);
            BuildChest(root, prop, title, modal.Close, reveal, out _);
            return modal;
        }

        /// <summary>The chest stage inside <paramref name="root"/>; a tap runs <paramref name="close"/> then <paramref name="reveal"/>.</summary>
        private RectTransform BuildChest(RectTransform root, string prop, string title, Action close, Action reveal, out Button tap)
        {
            Canvas.ForceUpdateCanvases();
            var holder = Ui.Rect(root, "ChestStage");
            Ui.Stretch(holder);
            var frame = RunScenes.Frame(holder, out float s);
            // K1: the chunky 3/4 chest art (the prop's own art, else the board's treasure chest) large in a glow in the
            // chest's colour; reward ribbons are gold
            var glow = Ui.Panel(frame, ChestGlowColor(prop), "ChestGlow");
            glow.sprite = ScreenArt.Glow(); glow.raycastTarget = false;
            RunScenes.At(glow.rectTransform, s, 540, 1260, 1040, 1040);
            UiPulse.On(glow, 0.06f, 1.6f, 0.6f);
            var rib = Ui.Panel(frame, Color.white, "Ribbon");
            rib.sprite = SliceCards.GoldRibbon;
            rib.preserveAspect = true; rib.raycastTarget = false;
            RunScenes.At(rib.rectTransform, s, 540, 395, 1010, 172);
            var t = SliceKit.Chunky(rib.transform, title, (int)(80 * s), Color.white, TextAnchor.MiddleCenter, "ChestTitle");
            Ui.Stretch(t.rectTransform, 0, 0, 0, 14 * s);
            var chest = Ui.Panel(frame, Color.white, "Chest");
            chest.sprite = ChestSprite(prop); chest.preserveAspect = true; chest.raycastTarget = false;
            RunScenes.At(chest.rectTransform, s, 540, 1270, 700, 640);
            UiBob.On(chest.rectTransform, 10 * s, 1.8f);
            // K2: the hint sits just under the chest; the dice tower's caption is hidden while the chest is up
            var hint = SliceKit.Chunky(frame, "Tap anywhere to open!", (int)(60 * s), new Color(1, 1, 1, 0.78f), TextAnchor.MiddleCenter, "ChestHint");
            RunScenes.At(hint.rectTransform, s, 540, 1670, 1000, 90);
            UiPulse.On(hint, 0.04f, 1.4f, 0.55f);
            var diceLabel = _rollButton != null ? _rollButton.Find("Cap/Label") : null;
            if (diceLabel != null) diceLabel.gameObject.SetActive(false);
            var b = Ui.Button(holder, "", null, new Color(1, 1, 1, 0.001f), 10, 10, true, 10);
            b.name = "ChestTap";
            Ui.Stretch((RectTransform)b.transform);
            var le = b.GetComponent<LayoutElement>(); if (le != null) le.ignoreLayout = true;
            bool opened = false;
            void Open()
            {
                if (opened) return;
                opened = true;
                var seq = UiSequence.On(holder.gameObject);
                seq.Then(0.32f, k =>
                {
                    float sc = 1 + 0.25f * Mathf.Sin(k * Mathf.PI) + 0.04f * Mathf.Sin(k * 40);
                    chest.rectTransform.localScale = Vector3.one * sc;
                    glow.color = new Color(1f, 0.9f, 0.5f, 0.55f + 0.45f * k);
                }, () => { if (diceLabel != null) diceLabel.gameObject.SetActive(true); close?.Invoke(); reveal?.Invoke(); });
            }
            b.onClick.AddListener(Open);
            tap = b;
            return holder;
        }

        /// <summary>The chest moment's art: the prop's generated art, else the board's treasure chest marker, else the drawn prop.</summary>
        public static Sprite ChestSprite(string prop) => ArtCatalog.RunProp(prop) ?? ArtCatalog.SliceMarker("chest") ?? ScreenArt.Prop(prop);

        private static Color ChestGlowColor(string prop) =>
            prop == "chest_mythic" ? new Color(1f, 0.32f, 0.3f, 0.6f) : prop == "chest_signature" ? new Color(0.45f, 0.95f, 0.9f, 0.55f) : new Color(1f, 0.86f, 0.42f, 0.55f);

        /// <summary>The signature offer (Resonance) revealed from its chest: one card per offered signature, and the coins.</summary>
        private UiModal SignatureReveal(ActiveRun run, EventDef ev)
        {
            float s = ModalScale();
            var root = SliceCards.Overlay(App.ModalLayer, "Signature", out var modal);
            SliceCards.Header(root, s, SliceCards.Ribbon(SignaturePick.SignatureColor), "SIGNATURE!", "SignatureTitle", "Attune to a signal!");
            var visible = VisibleOptions(run, ev);
            float step = Mathf.Min(287, 900f / Math.Max(1, visible.Count)), y = 1050;
            foreach (int i in visible)
            {
                int index = i;
                var o = ev.Options[i];
                var sigFx = o.Outcomes.Count > 0 ? o.Outcomes[0].Effects.Find(fx => fx.Kind == RunEffectKind.GainSignature) : null;
                SkillCardSpec spec;
                if (sigFx != null)
                {
                    int.TryParse(sigFx.Param, out var si);
                    var sig = Content.Skill(run.SignatureChoice[si]);
                    spec = new SkillCardSpec
                    {
                        Id = sig?.Id ?? "sig", Tier = "Signature", TierColor = SignaturePick.SignatureColor, Family = sig?.Family ?? SkillFamily.None,
                        IconArt = ArtCatalog.SkillIcon(sig?.Id), Title = sig?.Name ?? o.Text, Body = SignaturePick.Body(Content, sig),
                        Chip = "SIGNATURE · " + SkillText.FamilyName(sig?.Family ?? SkillFamily.None).ToUpperInvariant(), ChipName = "SignatureChip", ChipColor = SignaturePick.SignatureColor,
                        Tip = sig != null ? OfferTip(sig.Id, 0, true) : (SkillTip?)null, TipContent = Content,
                    };
                }
                else
                {
                    spec = new SkillCardSpec { Id = "coins", Tier = "Coins", TierColor = Theme.Gold, IconArt = ScreenArt.Icon("coins_big"), Title = o.Text, Body = SkillText.StripMarks(o.RiskText ?? "") };
                }
                var card = SliceCards.Card(root, s, y, spec, () => { modal.Close(); CloseDecision(); Run(new RunChooseEventOption { Index = index }); }, null, OptionName(o));
                if (sigFx != null) card.Find("Draft:" + spec.Id)?.gameObject.SetActive(true);
                y += step;
            }
            DecisionSkillsBook(root, s, 975, 2262, true);
            UiTween.PopIn(root, 0.15f);
            return modal;
        }

        // ---- outcome result (restyled) -------------------------------------------------------------------------

        private UiModal OutcomeSheet(RunOutcome o)
        {
            var root = SliceCards.Overlay(App.ModalLayer, "Outcome", out var modal, 0.88f);
            Canvas.ForceUpdateCanvases();
            var content = Ui.Rect(root, "Content");
            Ui.Stretch(content);
            var group = content.gameObject.AddComponent<CanvasGroup>();
            var frame = RunScenes.Frame(content, out float s);
            bool good = o.Lines.Count == 0 || o.Lines.Exists(l => l.Good);
            var cap = SliceKit.Chunky(frame, SourceCaption(o.Source) + " RESULT", (int)(44 * s), new Color32(0xFF, 0xE0, 0x6A, 255), TextAnchor.MiddleCenter, "Caption");
            RunScenes.At(cap.rectTransform, s, 540, 300, 1000, 60);
            var rib = Ui.Panel(frame, Color.white, "Ribbon");
            rib.sprite = good ? SliceCards.GoldRibbon : SliceCards.RedRibbon; rib.preserveAspect = true; rib.raycastTarget = false;
            RunScenes.At(rib.rectTransform, s, 540, 430, 1010, 172);
            var title = SliceKit.Chunky(rib.transform, o.Title ?? "Result", (int)(76 * s), Color.white, TextAnchor.MiddleCenter, "ResultTitle");
            Ui.Stretch(title.rectTransform, 40 * s, 0, 40 * s, 14 * s);
            title.horizontalOverflow = HorizontalWrapMode.Wrap; title.resizeTextForBestFit = true; title.resizeTextMaxSize = title.fontSize; title.resizeTextMinSize = (int)(44 * s);
            if (!string.IsNullOrEmpty(o.Choice) && o.Source != "shop")
            {
                var ch = SliceKit.Chunky(frame, (o.Source == "minigame" ? "Medal: " : "You chose: ") + o.Choice, (int)(46 * s), Color.white, TextAnchor.MiddleCenter, "Choice");
                RunScenes.At(ch.rectTransform, s, 540, 575, 1000, 70);
            }

            // the paper panel with the chance roll, the outcome text and one row per gain / loss
            float rows = o.Lines.Count * 122 + (o.Chance ? 330 : 0) + (string.IsNullOrEmpty(o.Text) ? 0 : 110) + 70;
            float top = 650, height = Mathf.Clamp(rows, 260, 1320);
            var paper = Ui.RoundPanel(frame, SliceKit.Ink, 40 * s, "Paper");
            paper.raycastTarget = false;
            RunScenes.At(paper.rectTransform, s, 540, top + height / 2, 980, height);
            var face = Ui.RoundPanel(paper.transform, RunScenes.Paper, 34 * s, "Face");
            face.raycastTarget = false;
            Ui.Stretch(face.rectTransform, 7 * s, 7 * s, 7 * s, 7 * s);
            var col = Ui.Column(face.transform, 14 * s, (int)(30 * s), null, "Body");
            Ui.Stretch(col);
            col.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;

            RectTransform meter = null, marker = null;
            Text stamp = null;
            float landing = 0.5f;
            if (o.Chance)
            {
                float pct = Mathf.Clamp01((float)(o.SuccessChancePct / 100.0));
                var panel = Ui.Column(col, 14 * s, 0, null, "ChanceMeter");
                var cl = Ui.Label(panel, Fmt.Pct(Math.Round(o.SuccessChancePct)) + " chance", 50, RunScenes.ChanceInk, TextAnchor.MiddleCenter, "ChanceLabel");
                cl.font = Ui.DisplayFont; cl.fontSize = (int)(56 * s);
                Ui.Size(cl, -1, 76 * s);
                var trackEdge = Ui.RoundPanel(panel, SliceKit.Ink, 26 * s, "TrackEdge");
                Ui.Size(trackEdge, -1, 92 * s);
                var track = Ui.Rect(trackEdge.transform, "Track");
                Ui.Stretch(track, 6 * s, 6 * s, 6 * s, 6 * s);
                var lucky = Ui.RoundPanel(track, new Color32(0x5C, 0xC0, 0x48, 255), 20 * s, "Lucky");
                lucky.rectTransform.anchorMin = new Vector2(0, 0); lucky.rectTransform.anchorMax = new Vector2(pct, 1);
                lucky.rectTransform.offsetMin = lucky.rectTransform.offsetMax = Vector2.zero;
                var bad = Ui.RoundPanel(track, new Color32(0xD8, 0x44, 0x4C, 255), 20 * s, "Unlucky");
                bad.rectTransform.anchorMin = new Vector2(pct, 0); bad.rectTransform.anchorMax = new Vector2(1, 1);
                bad.rectTransform.offsetMin = bad.rectTransform.offsetMax = Vector2.zero;
                var m = Ui.RoundPanel(track, Color.white, 10 * s, "Marker");
                UiShapes.Outline(m.rectTransform, SliceKit.Ink, 10 * s, 5);
                marker = m.rectTransform;
                marker.anchorMin = marker.anchorMax = new Vector2(0, 0.5f);
                marker.pivot = new Vector2(0.5f, 0.5f);
                marker.sizeDelta = new Vector2(30 * s, 130 * s);
                landing = o.Success ? pct * 0.5f : pct + (1 - pct) * 0.5f;
                stamp = SliceKit.Chunky(panel, "", (int)(84 * s), Color.white, TextAnchor.MiddleCenter, "ChanceResult");
                Ui.Size(stamp, -1, 110 * s);
                meter = track;
            }
            var details = Ui.Column(col, 12 * s, 0, null, "Lines");
            if (!string.IsNullOrEmpty(o.Text))
            {
                var ot = Ui.Label(details, o.Text, 40, RunScenes.Ink, TextAnchor.MiddleCenter, "OutcomeText");
                ot.font = Ui.DisplayFont; ot.fontSize = (int)(46 * s);
            }
            foreach (var l in o.Lines)
            {
                var icon = OutcomeIcon(l, Content);
                var row = Ui.Row(details, 110 * s, 18 * s, l.Good ? new Color32(0xDC, 0xEE, 0xC4, 255) : new Color32(0xF6, 0xD2, 0xCC, 255), (int)(14 * s), "Line:" + l.Kind);
                bool skillLine = (l.Kind == OutcomeKind.SkillGained || l.Kind == OutcomeKind.SkillLost || l.Kind == OutcomeKind.SkillLevel) && Content.Skill(l.Id ?? "") != null;
                if (skillLine)
                {
                    // a skill gained / lost / levelled: the shared skill tile, a tap shows its full text
                    row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false; // the tile stays square
                    var tile = SkillTileView.Build(row, Content, l.Id, 92 * s, "Icon");
                    Ui.Size(tile, 92 * s, 92 * s, 0);
                    var r = State.Run.Active;
                    var tip = SkillTip.Of(l.Id, r != null ? RunDraft.Level(r, l.Id) : 1);
                    SkillTileView.Tappable(tile, Content, () => tip);
                }
                else
                {
                    var dot = Ui.RoundPanel(row, icon.Value, 40 * s, "Icon");
                    Ui.Size(dot, 82 * s, 82 * s, 0);
                    UiShapes.Outline(dot.rectTransform, SliceKit.Ink, 40 * s, 4);
                    var art = OutcomeSprite(l);
                    if (art != null)
                    {
                        // E3: the HUD's stat icons (heart, sword, shield, coin …) instead of letter chips
                        var img = Ui.Panel(dot.transform, Color.white, "StatIcon");
                        img.sprite = art; img.preserveAspect = true; img.raycastTarget = false;
                        Ui.Stretch(img.rectTransform, 10 * s, 10 * s, 10 * s, 10 * s);
                    }
                    else
                    {
                        var glyph = SliceKit.Chunky(dot.transform, icon.Key, (int)((icon.Key.Length > 1 ? 30 : 42) * s), Color.white, TextAnchor.MiddleCenter, "Glyph");
                        Ui.Stretch(glyph.rectTransform);
                    }
                }
                var t = Ui.Label(row, OutcomeLineText(l, Content), 40, l.Good ? RunScenes.Gain : RunScenes.Loss);
                t.font = Ui.DisplayFont; t.fontSize = (int)(44 * s);
                t.resizeTextForBestFit = true; t.resizeTextMaxSize = t.fontSize; t.resizeTextMinSize = (int)(28 * s);
                Ui.Size(t, -1, -1, 1);
            }
            var cont = Ui.Button(frame, "", CloseOutcome, new Color32(0x3E, 0x3A, 0x78, 255), 430 * s, 180 * s, true, 20);
            cont.name = "OutcomeContinue";
            RunScenes.At((RectTransform)cont.transform, s, 540, 2085, 430, 180);
            UiShapes.Outline((RectTransform)cont.transform, SliceKit.Ink, Theme.RadiusButton, 7);
            var ct = SliceKit.Chunky(cont.transform, "Continue", (int)(76 * s), Color.white, TextAnchor.MiddleCenter, "ContinueText");
            Ui.Stretch(ct.rectTransform, 0, 0, 0, 8 * s);

            Action startReveal = () =>
            {
                if (marker == null) { group.alpha = 1; return; }
                void Land()
                {
                    float w = meter.rect.width;
                    marker.anchoredPosition = new Vector2(landing * w, 0);
                    stamp.text = o.Success ? "SUCCESS!" : "NO LUCK";
                    stamp.color = o.Success ? new Color32(0x6C, 0xE0, 0x4C, 255) : new Color32(0xF0, 0x50, 0x58, 255);
                    details.GetComponent<CanvasGroup>().alpha = 1;
                    _outcomeRevealing = false;
                }
                var dg = details.gameObject.AddComponent<CanvasGroup>();
                if (InstantOutcomes || !Application.isPlaying) { Land(); return; }
                dg.alpha = 0;
                _outcomeRevealing = true;
                var seq = UiSequence.On(modal.Root);
                seq.Then(ChanceSpinSeconds, k =>
                {
                    float w = meter.rect.width;
                    float decay = (1 - k) * (1 - k);
                    float x = landing + 0.5f * decay * Mathf.Sin(k * Mathf.PI * 9);
                    marker.anchoredPosition = new Vector2(Mathf.Clamp01(x) * w, 0);
                    stamp.text = k < 1 ? "rolling…" : "";
                }, Land);
            };
            // guarded loot: the chest bursts open first, then the result
            bool chest = o.Source == "treasure" && o.Lines.Exists(l => l.Kind == OutcomeKind.AccountLoot) && ShowChestIntro;
            if (chest)
            {
                group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;
                _outcomeRevealing = true;
                RectTransform stage = null;
                stage = BuildChest(root, "chest_common", "You found a Chest!", () => Ui.DestroyGo(stage.gameObject), () =>
                {
                    group.alpha = 1; group.interactable = true; group.blocksRaycasts = true;
                    _outcomeRevealing = false;
                    startReveal();
                }, out _);
            }
            else startReveal();
            return modal;
        }

        // ---- shop ----------------------------------------------------------------------------------------------

        private static readonly string[] ShopLines =
        {
            "I adore worms. They buy things.",
            "Everything here fell off a comet. Honest.",
            "Coins in, power out. Simple as orbit.",
            "Back again? My favourite customer this light-year!",
        };

        /// <summary>The shop's price label: a coin and the price, the struck-through list price under SALE, or "Sold".</summary>
        private static void ShopPriceLabel(Button b, ShopOffer o)
        {
            var label = b.transform.Find("Price")?.GetComponent<Text>();
            if (label != null)
            {
                label.text = o.Sold ? "Sold" : Fmt.Num(o.Price);
                if (o.Sold) Ui.Stretch(label.rectTransform); // #27: "Sold" in the middle of the button (the price leaves room for the coin)
            }
            var coin = b.transform.Find("Coin");
            if (coin != null) coin.gameObject.SetActive(!o.Sold);
            var sale = b.transform.Find("Was");
            if (sale != null) sale.gameObject.SetActive(!o.Sold);
            var tag = b.transform.parent != null ? b.transform.parent.Find("Sale") : null; // #27: no SALE! tag on a sold offer
            if (tag != null) tag.gameObject.SetActive(!o.Sold);
        }

        /// <summary>A yellow price button (coin icon + price; "Was" with the struck list price on SALE), named "Button:n coins".</summary>
        private Button PriceButton(RectTransform frame, float s, float x, float y, float w, float h, ShopOffer o, Action buy, bool enabled)
        {
            var b = Ui.Button(frame, "", buy, RunScenes.SignYellow, w * s, h * s, enabled, 20);
            b.name = "Button:" + o.Price + " coins";
            RunScenes.At((RectTransform)b.transform, s, x, y, w, h);
            UiShapes.Outline((RectTransform)b.transform, SliceKit.Ink, Theme.RadiusButton, 6);
            bool sale = o.ListPrice > o.Price;
            float py = sale ? -h * 0.18f : 0;
            var coin = Ui.Panel(b.transform, Color.white, "Coin");
            coin.sprite = HudArt.Icon("coin"); coin.preserveAspect = true; coin.raycastTarget = false;
            coin.rectTransform.anchorMin = coin.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            coin.rectTransform.sizeDelta = new Vector2(h * 0.5f * s, h * 0.5f * s);
            coin.rectTransform.anchoredPosition = new Vector2(-w * 0.24f * s, py * s);
            var t = SliceKit.Chunky(b.transform, o.Sold ? "Sold" : Fmt.Num(o.Price), (int)(Mathf.Min(58, h * 0.45f) * s), Color.white, TextAnchor.MiddleCenter, "Price");
            Ui.Stretch(t.rectTransform, w * 0.3f * s, 0, 8 * s, 6 * s);
            t.rectTransform.anchoredPosition = new Vector2(t.rectTransform.anchoredPosition.x, py * s);
            if (sale)
            {
                var was = SliceKit.Chunky(b.transform, Fmt.Num(o.ListPrice), (int)(34 * s), new Color32(0xE8, 0xE4, 0xF0, 255), TextAnchor.MiddleCenter, "Was");
                was.rectTransform.anchorMin = was.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                was.rectTransform.sizeDelta = new Vector2(w * 0.6f * s, 44 * s);
                was.rectTransform.anchoredPosition = new Vector2(10 * s, h * 0.25f * s);
                var strike = Ui.Panel(was.transform, new Color32(0x2A, 0x20, 0x34, 255), "Strike");
                strike.raycastTarget = false;
                strike.rectTransform.anchorMin = strike.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                strike.rectTransform.sizeDelta = new Vector2(80 * s, 6 * s);
                strike.rectTransform.localRotation = Quaternion.Euler(0, 0, 12);
                var tag = SliceKit.Chunky(frame, "SALE!", (int)(48 * s), Color.white, TextAnchor.MiddleCenter, "Sale");
                RunScenes.At(tag.rectTransform, s, x - 30, y - h / 2 - 26, w, 60);
            }
            ShopPriceLabel(b, o);
            return b;
        }

        /// <summary>
        /// The shop (reference shop): the HUD, a wooden SHOP sign, the shopkeeper with a speech bubble, the utility offers
        /// (heal, stat perks) as small priced tiles, the skill offers as cards (tier tag, framed icon, name + level, coloured
        /// keywords, price; SALE with the struck list price), Exit, Reroll with its price and the skills book. Buying keeps
        /// it open (<see cref="RefreshShop"/>).
        /// </summary>
        private UiModal ShopModal(ActiveRun run)
        {
            long reroll = ShopRerollPrice(run);
            _shopButtons.Clear();
            var root = RunScenes.Overlay(App.ModalLayer, "Shop", out var modal, out var frame, out float s, new Color32(0x3E, 0x22, 0x16, 255));
            var wood = Ui.Panel(root, Color.white, "Wood");
            wood.sprite = HudArt.Gradient(new Color32(0x4A, 0x2A, 0x1A, 255), new Color32(0x7A, 0x46, 0x2A, 255));
            wood.raycastTarget = false;
            Ui.Stretch(wood.rectTransform);
            frame.SetAsLastSibling();
            for (int i = 0; i < 7; i++)
            {
                var plank = Ui.Panel(frame, new Color(0, 0, 0, 0.12f), "Plank");
                plank.raycastTarget = false;
                RunScenes.At(plank.rectTransform, s, 80 + i * 160, 600, 6, 1200);
            }
            var arch = Ui.Panel(frame, new Color(0.25f, 0.4f, 0.7f, 0.55f), "Window");
            arch.sprite = ScreenArt.Glow(); arch.raycastTarget = false;
            RunScenes.At(arch.rectTransform, s, 600, 620, 760, 760);
            var shelf = Ui.Panel(frame, new Color32(0x5A, 0x32, 0x1C, 255), "Shelf");
            shelf.raycastTarget = false;
            RunScenes.At(shelf.rectTransform, s, 540, 1070, 1200, 40);
            UiShapes.Outline(shelf.rectTransform, SliceKit.Ink, 4, 4);

            var hud = RunScenes.Hud(root, Content, run, PauseModal); // #2: the board's strip, in place
            _shopHudCoins = hud.Coins; _shopHud = hud; _shopFrame = frame;

            var sign = Ui.RoundPanel(frame, new Color32(0x8A, 0x52, 0x2E, 255), 24 * s, "ShopSign");
            sign.raycastTarget = false;
            RunScenes.At(sign.rectTransform, s, 540, 395, 600, 200);
            UiShapes.Outline(sign.rectTransform, SliceKit.Ink, 24 * s, 7);
            var signText = Ui.Display(sign.transform, "SHOP", 150, new Color32(0xE6, 0xC4, 0xA8, 255), TextAnchor.MiddleCenter, "Title");
            signText.fontSize = (int)(150 * s);
            var so = signText.gameObject.AddComponent<Shadow>(); so.effectColor = new Color(0, 0, 0, 0.45f); so.effectDistance = new Vector2(0, -6 * s);
            Ui.Stretch(signText.rectTransform);
            _shopCoins = SliceKit.Chunky(frame, ShopBoughtLine(run), (int)(30 * s), new Color32(0xFF, 0xE0, 0x9A, 255), TextAnchor.MiddleCenter, "ShopCoins");
            RunScenes.At(_shopCoins.rectTransform, s, 540, 1036, 600, 40);

            var npc = Ui.Panel(frame, Color.white, "Npc:shopkeeper");
            npc.sprite = ScreenArt.Character("shopkeeper"); npc.preserveAspect = true; npc.raycastTarget = false;
            RunScenes.At(npc.rectTransform, s, 235, 810, 500, 500);
            RunScenes.Bubble(frame, s, 665, 590, 700, 170, ShopLines[Math.Abs(run.ShopsVisited) % ShopLines.Length], false);

            // the offers live in their own layer: a reroll replaces only them, in place (RefreshShop)
            _shopOffers = Ui.Rect(frame, "Offers");
            Ui.Stretch(_shopOffers);
            _shopScale = s;
            BuildShopOffers(run, 0);

            // Exit (green), Reroll (purple, price), the skills book
            var exit = Ui.Button(frame, "", () => { modal.Close(); Run(new RunShopLeave()); }, new Color32(0xC2, 0xE0, 0x5A, 255), 370 * s, 175 * s, true, 20);
            exit.name = "Button:Leave";
            RunScenes.At((RectTransform)exit.transform, s, 265, 2272, 370, 175);
            UiShapes.Outline((RectTransform)exit.transform, SliceKit.Ink, Theme.RadiusButton, 7);
            var et = SliceKit.Chunky(exit.transform, "Exit", (int)(84 * s), Color.white, TextAnchor.MiddleCenter, "ExitText");
            Ui.Stretch(et.rectTransform, 0, 0, 0, 8 * s);
            var rr = Ui.Button(frame, "", () => Run(new RunShopReroll(), r => { if (r.Ok) RefreshShop(true); }), SliceCards.Purple, 370 * s, 175 * s, run.Coins >= reroll, 20);
            rr.name = "Button:Reroll (" + reroll + ")";
            _shopReroll = rr;
            RunScenes.At((RectTransform)rr.transform, s, 665, 2272, 370, 175);
            UiShapes.Outline((RectTransform)rr.transform, SliceKit.Ink, Theme.RadiusButton, 7);
            var rt = SliceKit.Chunky(rr.transform, "Reroll", (int)(66 * s), Color.white, TextAnchor.MiddleCenter, "RerollText");
            Ui.Stretch(rt.rectTransform, 0, 0, 0, 70 * s);
            var rc = Ui.Panel(rr.transform, Color.white, "Coin");
            rc.sprite = HudArt.Icon("coin"); rc.preserveAspect = true; rc.raycastTarget = false;
            rc.rectTransform.anchorMin = rc.rectTransform.anchorMax = new Vector2(0.5f, 0.28f);
            rc.rectTransform.sizeDelta = new Vector2(56 * s, 56 * s);
            rc.rectTransform.anchoredPosition = new Vector2(-62 * s, 0);
            var rp = SliceKit.Chunky(rr.transform, reroll > 0 ? Fmt.Num(reroll) : "FREE", (int)(50 * s), Color.white, TextAnchor.MiddleLeft, "RerollPrice");
            rp.rectTransform.anchorMin = rp.rectTransform.anchorMax = new Vector2(0.5f, 0.28f);
            rp.rectTransform.pivot = new Vector2(0, 0.5f);
            rp.rectTransform.sizeDelta = new Vector2(170 * s, 60 * s);
            rp.rectTransform.anchoredPosition = new Vector2(-28 * s, 0);
            DecisionSkillsBook(frame, s);
            hud.Root.SetAsLastSibling();
            UiTween.PopIn(frame, 0.12f);
            return modal;
        }

        /// <summary>The reroll's price now: the rules' price (base + step × rerolls in this visit; every shop starts at the base).</summary>
        private long ShopRerollPrice(ActiveRun run) => RunSystem.ShopRerollCost(Content, run);

        private RectTransform _shopOffers;
        private float _shopScale = 1;
        private Button _shopReroll;

        /// <summary>
        /// One offer's layer (all of its parts: tile / card, price button, SALE tag), pivoted at the offer's centre so a
        /// reroll flips it about its own middle.
        /// </summary>
        private RectTransform OfferGroup(int index, float x, float y)
        {
            var g = Ui.Rect(_shopOffers, "Offer:" + index);
            Ui.Stretch(g);
            g.pivot = new Vector2(x / RunScenes.RefW, 1 - y / RunScenes.RefH);
            g.anchoredPosition = Vector2.zero;
            return g;
        }

        /// <summary>
        /// The utility tiles and skill cards of the current offers into <see cref="_shopOffers"/>; <paramref name="flipDelay"/>
        /// &gt; 0 = a reroll: each new offer flips in after the old one flipped out (staggered).
        /// </summary>
        private void BuildShopOffers(ActiveRun run, float flipDelay)
        {
            float s = _shopScale;
            _shopButtons.Clear();
            int order = 0;
            // utility offers (heal, stat perks): small priced tiles right of the shopkeeper
            var utility = new List<int>();
            var skills = new List<int>();
            for (int i = 0; i < run.Shop.Count; i++) (run.Shop[i].Kind == ShopOfferKind.Skill ? skills : utility).Add(i);
            int un = Math.Min(3, utility.Count);
            for (int k = 0; k < un; k++)
            {
                int index = utility[k];
                var o = run.Shop[index];
                ShopTileSlot(un, k, out float x, out float size);
                string label = o.Kind == ShopOfferKind.Heal ? "Heal " + Fmt.Pct(o.Value) : SkillText.Stat(o.Stat) ?? "+stat";
                var group = OfferGroup(index, x, 880);
                var tileEdge = Ui.RoundPanel(group, SliceKit.Ink, 26 * s, "Utility:" + label);
                tileEdge.raycastTarget = false;
                RunScenes.At(tileEdge.rectTransform, s, x, 820, size, size);
                var tileFace = Ui.RoundPanel(tileEdge.transform, o.Sold ? (Color)new Color32(0x6A, 0x66, 0x74, 255) : new Color32(0x74, 0x78, 0xA8, 255), 22 * s, "Face");
                tileFace.raycastTarget = false;
                Ui.Stretch(tileFace.rectTransform, 6 * s, 6 * s, 6 * s, 6 * s);
                var icon = Ui.Panel(tileFace.transform, Color.white, "Icon");
                icon.sprite = o.Kind == ShopOfferKind.Heal ? ScreenArt.Icon("heal") : o.Stat != null && o.Stat.Stat == StatId.Hp ? ScreenArt.Icon("max_hp")
                    : o.Stat != null && o.Stat.Stat == StatId.Def ? HudArt.Icon("shield") : o.Stat != null && o.Stat.Stat == StatId.Atk ? HudArt.Icon("sword") : ScreenArt.Icon("upgrade");
                icon.preserveAspect = true; icon.raycastTarget = false;
                Ui.Stretch(icon.rectTransform, size * 0.16f * s, size * 0.08f * s, size * 0.16f * s, size * 0.3f * s);
                var lt = SliceKit.Chunky(tileFace.transform, label, (int)(34 * s), Color.white, TextAnchor.LowerCenter, "Label");
                Ui.Stretch(lt.rectTransform, 4 * s, 0, 4 * s, 10 * s);
                lt.horizontalOverflow = HorizontalWrapMode.Wrap; lt.resizeTextForBestFit = true; lt.resizeTextMaxSize = lt.fontSize; lt.resizeTextMinSize = (int)(22 * s);
                var slot = Ui.Rect(group, "OfferSlot:" + index);
                Ui.Stretch(slot);
                Button buy = null;
                buy = PriceButton(slot, s, x, 975, size, 92, o, () => BuyOffer(index, buy), !o.Sold && run.Coins >= o.Price);
                if (o.Sold) SoldLook(group, tileEdge.rectTransform, s, false);
                AddOfferName(buy, label);
                _shopButtons.Add(new KeyValuePair<Button, int>(buy, index));
                if (flipDelay > 0) UiTween.FlipIn(group, flipDelay + 0.04f * order++);
            }

            // skill offers as cards
            int n = Math.Max(1, skills.Count);
            float h = Mathf.Min(285, 1060f / n - 14), y = 1110 + h / 2;
            foreach (int index in skills)
            {
                var o = run.Shop[index];
                var sk = Content.Skill(o.SkillId);
                int ownedLv = RunDraft.Level(run, o.SkillId);
                var spec = new SkillCardSpec
                {
                    Id = o.SkillId, Tier = RunTuning.RunTier(sk?.Rarity ?? Rarity.Common).ToString(), TierColor = RunPresentation.SkillColor(sk), Family = sk?.Family ?? SkillFamily.None,
                    IconArt = ArtCatalog.SkillIcon(o.SkillId), Title = ownedLv > 0 && sk != null ? SkillText.Title(sk, ownedLv + 1) : sk?.Name ?? o.SkillId,
                    Body = Fmt.SkillCard(Content, sk, ownedLv), Dim = o.Sold, Tip = OfferTip(o.SkillId, ownedLv), TipContent = Content,
                };
                string info = (sk?.Name ?? o.SkillId) + " (" + (sk?.Rarity ?? Rarity.Common) + "): " + SkillText.StripMarks(spec.Body) + (ownedLv > 0 ? " " + LevelUpText(Content) : "");
                var group = OfferGroup(index, 540, y);
                var card = SliceCards.Card(group, s, y, spec, null, () => App.Feedback.Tip(info), "Offer", h, "Words");
                var body = card.Find("Words/SkillText") as RectTransform;
                if (body != null) body.offsetMax = new Vector2(-250 * s, body.offsetMax.y);
                var slot = Ui.Rect(group, "OfferSlot:" + index);
                Ui.Stretch(slot);
                Button buy = null;
                buy = PriceButton(slot, s, 925, y + 22 * h / 280, 200, h * 0.48f, o, () => BuyOffer(index, buy), !o.Sold && run.Coins >= o.Price);
                if (o.Sold) SoldLook(group, card, s, false);
                AddOfferName(buy, spec.Title);
                _shopButtons.Add(new KeyValuePair<Button, int>(buy, index));
                if (flipDelay > 0) UiTween.FlipIn(group, flipDelay + 0.04f * order++);
                y += h + 14;
            }
        }

        /// <summary>Reference px right of the shopkeeper where the utility tiles go, and the screen margin kept on the right.</summary>
        public const float ShopTilesLeft = 490, ShopTilesMargin = 32, ShopTileGap = 18;

        /// <summary>
        /// S2: centre and size of utility tile <paramref name="k"/> of <paramref name="n"/>: the tiles and their gaps fit the
        /// width right of the shopkeeper minus a 32 px margin, so the last one never touches the screen edge (pure; tested).
        /// </summary>
        public static void ShopTileSlot(int n, int k, out float x, out float size)
        {
            n = Math.Max(1, n);
            float width = RunScenes.RefW - ShopTilesMargin - ShopTilesLeft;
            size = Mathf.Min(210, (width - (n - 1) * ShopTileGap) / n);
            float used = n * size + (n - 1) * ShopTileGap;
            float left = ShopTilesLeft + (width - used) / 2;
            x = left + size / 2 + k * (size + ShopTileGap);
        }

        private SliceHud _shopHud;
        private RectTransform _shopFrame;

        /// <summary>Buys an offer; on success S4's feedback plays (SOLD stamp, coins to the counter, the changed HUD stat pops).</summary>
        private void BuyOffer(int index, Button buy)
        {
            var run = State.Run.Active;
            if (run == null) return;
            var before = RunPresentation.Hud(Content, run);
            long coinsBefore = run.Coins;
            Vector2 from = buy != null ? InShopFrame((RectTransform)buy.transform) : Vector2.zero;
            Run(new RunShopBuy { Index = index }, r =>
            {
                if (!r.Ok) return;
                RefreshShop();
                ShopBoughtFx(index, before, coinsBefore, from);
            });
        }

        /// <summary>A rect's centre in the shop frame's top-left-anchored coordinates (RunFx particles).</summary>
        private Vector2 InShopFrame(RectTransform rt)
        {
            if (_shopFrame == null || rt == null) return Vector2.zero;
            Vector2 local = _shopFrame.InverseTransformPoint(rt.TransformPoint(rt.rect.center));
            return new Vector2(local.x - _shopFrame.rect.xMin, local.y - _shopFrame.rect.yMax);
        }

        private void ShopBoughtFx(int index, RunHudStats before, long coinsBefore, Vector2 from)
        {
            var run = State.Run.Active;
            if (run == null || _shopOffers == null) return;
            var group = _shopOffers.Find("Offer:" + index) as RectTransform;
            if (group != null)
            {
                RectTransform target = null;
                foreach (Transform t in group) if (t.name.StartsWith("Utility:") || t.name == "Offer") { target = (RectTransform)t; break; }
                SoldLook(group, target, _shopScale, true);
            }
            // coins fly from the price to the coin counter
            if (_shopHud != null && _shopHud.CoinsChip != null && run.Coins < coinsBefore)
            {
                RunFx.Burst(_shopFrame, from, InShopFrame(_shopHud.CoinsChip), Theme.Gold, 8, 26 * _shopScale);
                Pop(_shopHud.CoinsChip);
            }
            // the HUD stat the purchase changed pops
            var after = RunPresentation.Hud(Content, run);
            if (_shopHud != null)
            {
                if (Math.Round(after.Atk) != Math.Round(before.Atk)) Pop(_shopHud.AtkPill);
                if (Math.Round(after.Def) != Math.Round(before.Def)) Pop(_shopHud.DefPill);
                if ((Math.Round(after.Hp) != Math.Round(before.Hp) || Math.Round(after.MaxHp) != Math.Round(before.MaxHp)) && _shopHud.HpFill != null)
                    Pop(_shopHud.HpFill.transform.parent as RectTransform);
            }
        }

        /// <summary>A short scale pop (HUD stat changed).</summary>
        private static void Pop(RectTransform rt)
        {
            if (rt == null || !Application.isPlaying) return;
            UiTweens.Play(rt.gameObject, "pop", 0.35f, k => rt.localScale = Vector3.one * (1 + 0.28f * Mathf.Sin(k * Mathf.PI)), () => { if (rt != null) rt.localScale = Vector3.one; });
        }

        /// <summary>S4: a bought offer's tile / card dims and a red SOLD stamp lies across it (named "SoldStamp").</summary>
        private static void SoldLook(RectTransform group, RectTransform target, float s, bool animate)
        {
            if (group == null || target == null || group.Find("SoldStamp") != null) return;
            var cg = target.GetComponent<CanvasGroup>(); if (cg == null) cg = target.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0.45f;
            var stamp = Ui.RoundPanel(group, new Color(0, 0, 0, 0), 14 * s, "SoldStamp");
            stamp.raycastTarget = false;
            var srt = stamp.rectTransform;
            srt.anchorMin = target.anchorMin; srt.anchorMax = target.anchorMax; srt.pivot = new Vector2(0.5f, 0.5f);
            float w = Mathf.Min(target.sizeDelta.x * 0.8f, 360 * s);
            srt.sizeDelta = new Vector2(w, 96 * s);
            srt.anchoredPosition = target.anchoredPosition + new Vector2(target.sizeDelta.x * (0.5f - target.pivot.x), target.sizeDelta.y * (0.5f - target.pivot.y));
            srt.localRotation = Quaternion.Euler(0, 0, 12);
            UiShapes.Outline(srt, new Color32(0xE0, 0x30, 0x3C, 255), 14 * s, 7);
            var t = SliceKit.Chunky(srt, "SOLD", (int)(62 * s), new Color32(0xF0, 0x40, 0x4A, 255), TextAnchor.MiddleCenter, "SoldText");
            Ui.Stretch(t.rectTransform);
            if (animate && Application.isPlaying)
                UiTweens.Play(stamp.gameObject, "stamp", 0.22f, k => srt.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, k * k));
        }

        /// <summary>Seconds of a reroll's flip-out of the old offers (the new ones flip in after it).</summary>
        public const float ShopFlipSeconds = 0.12f;

        /// <summary>
        /// A reroll in place (owner playtest 0.1.224: the whole sheet used to close and reopen — a flash): the old offers flip
        /// out about their own middle while the new ones are built underneath and flip in after them, staggered. The rest
        /// of the sheet (background, sign, shopkeeper, HUD, buttons) stays as it is.
        /// </summary>
        private void ReplaceShopOffers(ActiveRun run)
        {
            if (_shopOffers == null) return;
            var old = new List<Transform>();
            foreach (Transform t in _shopOffers) old.Add(t);
            float stagger = 0;
            foreach (var t in old)
            {
                t.name = "Old" + t.name; // tests and lookups see only the live offers
                foreach (var b in t.GetComponentsInChildren<Button>(true)) b.interactable = false;
                UiTween.FlipOut(t, ShopFlipSeconds, stagger);
                stagger += 0.04f;
            }
            BuildShopOffers(run, Application.isPlaying ? ShopFlipSeconds : 0);
            foreach (var t in old) if (t != null) t.SetAsLastSibling(); // the old cards flip out on top of the new ones
        }

        /// <summary>"3 bought this run" under the counter (empty before the first purchase).</summary>
        private static string ShopBoughtLine(ActiveRun run) => run.ShopBuys > 0 ? run.ShopBuys + " bought this run" : "";

        private Text _shopHudCoins;

        /// <summary>The offer's name under its buy button (invisible; tests and the playthrough find the button by the name next to it).</summary>
        private static void AddOfferName(Button buy, string name)
        {
            var t = Ui.Label(buy.transform, name, 10, new Color(0, 0, 0, 0), TextAnchor.MiddleCenter, "OfferName");
            var le = t.GetComponent<LayoutElement>(); if (le != null) le.ignoreLayout = true;
            t.rectTransform.sizeDelta = Vector2.zero;
        }
    }
}
