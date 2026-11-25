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
    /// The crash site on the home planet ("the crash site comes first", owner 2026-11-25): from the Aliens unlock the fireplace, the
    /// crashed saucer and the Launch Pod stand on the planet's top arc next to the worm, the squad (C4 walkers) wanders around them.
    /// Tapping the pod opens its building card (<see cref="PodCard"/>): the squad seats (tap an alien to free its seat, tap one below to
    /// seat it), the boon, the level-up with its cost type and the star-up. Before the City (level 15) this is the only place the pod
    /// is managed; the City screen stays locked.
    /// </summary>
    public sealed partial class HubScreen
    {
        /// <summary>The pod's building card while open (null otherwise).</summary>
        public UiModal PodCard { get; private set; }
        /// <summary>The crash site's pod button on the planet (null without a pod).</summary>
        public Button PodButton { get; private set; }

        /// <summary>The crash site shows once the Launch Pod stands (the Aliens unlock places it).</summary>
        public static bool CrashSiteVisible(GameState s) => CitySystem.IsBuilt(CitySystem.LaunchPod(s));

        /// <summary>Fireplace, saucer and pod on the planet's top arc (<paramref name="r"/> = the walkers' arc radius).</summary>
        private void BuildCrashSite(float cy, float r)
        {
            PodButton = null;
            if (!CrashSiteVisible(State)) return;
            float Surface(float x) { float dx = Mathf.Clamp(x - 540, -r, r); return cy - Mathf.Sqrt(Mathf.Max(0, r * r - dx * dx)); }
            void Stand(Image img, float x, float w, float h, float sink, float tilt = 0)
            {
                img.preserveAspect = true;
                At(img.rectTransform, x, Surface(x) - h / 2 + sink, w, h);
                if (tilt != 0) img.rectTransform.localRotation = Quaternion.Euler(0, 0, tilt);
            }
            var site = Ui.Rect(_frame, "CrashSite");
            Ui.Stretch(site);
            var saucer = Ui.Panel(site, Color.white, "CrashSite:Saucer");
            saucer.sprite = CityArt.Saucer(); saucer.raycastTarget = false;
            Stand(saucer, 540 - r * 0.8f, r * 0.62f, r * 0.5f, r * 0.12f, 14);
            var glow = Ui.Panel(site, new Color(1f, 0.62f, 0.25f, 0.45f), "CrashSite:FireGlow");
            glow.sprite = ScreenArt.Glow(); glow.raycastTarget = false;
            At(glow.rectTransform, 540 - r * 0.42f, Surface(540 - r * 0.42f) - r * 0.1f, r * 0.55f, r * 0.4f);
            var fire = Ui.Panel(site, Color.white, "CrashSite:Fire");
            fire.sprite = CityArt.Fire(); fire.raycastTarget = false;
            Stand(fire, 540 - r * 0.42f, r * 0.3f, r * 0.3f, r * 0.05f);
            if (Application.isPlaying) UiPulse.On(glow, 0.08f, 1.6f, 0.5f);

            var pod = CitySystem.LaunchPod(State);
            var def = Content.Blueprint(pod.BlueprintId);
            float px = 540 + r * 0.6f, pw = r * 0.5f;
            // the pod's art is the button (a clear hit area, no chunky face)
            var hit = Ui.Panel(site, new Color(1, 1, 1, 0), "CrashSite:Pod");
            PodButton = hit.gameObject.AddComponent<Button>();
            PodButton.targetGraphic = hit;
            PodButton.onClick.AddListener(OpenPodCard);
            var art = Ui.Panel(PodButton.transform, Color.white, "PodArt");
            art.sprite = CityArt.Building(def); art.preserveAspect = true; art.raycastTarget = false;
            Ui.Stretch(art.rectTransform);
            At((RectTransform)PodButton.transform, px, Surface(px) - pw / 2 + r * 0.06f, pw, pw);
            // a small badge: seats used / seats, pulsing "+" while a seat is free and an alien could take it
            int slots = CitySystem.SquadSlots(State, Content), seated = AlienSystem.ActiveSquad(State, Content).Count;
            bool canSeat = seated < slots && State.Aliens.Owned.Count > seated;
            var badge = Ui.RoundPanel(site, canSeat ? new Color32(0xE8, 0x3A, 0x48, 255) : new Color(0.1f, 0.06f, 0.2f, 0.85f), 20 * _s, "PodBadge");
            badge.raycastTarget = false;
            At(badge.rectTransform, px, Surface(px) - pw - 14, 150, 44);
            var bt = SliceKit.Chunky(badge.transform, (canSeat ? "+ " : "") + "Squad " + seated + "/" + slots, (int)(26 * _s), Color.white, TextAnchor.MiddleCenter, "PodBadgeText");
            Ui.Stretch(bt.rectTransform);
            if (canSeat && Application.isPlaying) UiPulse.On(badge, 0.06f, 1.2f, 0.6f);
        }

        /// <summary>Opens the Launch Pod's card (the same card as in the City, small, over the hub).</summary>
        public void OpenPodCard()
        {
            if (!CrashSiteVisible(State)) return;
            if (PodCard == null || !PodCard.IsOpen)
            {
                PodCard = Ui.Sheet(App.ModalLayer, "LaunchPodCard", new Color(0.1f, 0.07f, 0.2f, 0.98f), Theme.RarityColor(Rarity.Common), (box, modal) => { }, 900);
                PodCard.Closed += () => PodCard = null;
            }
            RefreshPodCard();
        }

        private void RefreshPodCard()
        {
            if (PodCard == null || !PodCard.IsOpen || PodCard.Body == null) return;
            var pod = CitySystem.LaunchPod(State);
            if (!CitySystem.IsBuilt(pod)) { PodCard.Close(); return; }
            var box = PodCard.Body;
            Ui.Clear(box);
            BuildPodCard(box, pod);
        }

        private void BuildPodCard(RectTransform card, CityBuilding b)
        {
            var def = Content.Blueprint(b.BlueprintId);
            var owned = CitySystem.Owned(State, def.Id);
            int stars = owned?.Stars ?? 1;

            var head = Ui.Row(card, 64, 8, null, 0, "Head");
            var name = SliceKit.Chunky(head, def.Name, 42, Color.white, TextAnchor.MiddleLeft, "Name");
            Ui.Size(name, -1, 64, 1);
            Ui.Chip(head, def.Rarity.ToString(), Theme.RarityColor(def.Rarity), Color.white, 22, 44).name = "RarityTag";
            Ui.Button(head, "✕", () => PodCard?.Close(), new Color(0.25f, 0.2f, 0.4f, 1), 56, 56, true, 28).name = "CloseCard";
            Ui.Label(card, "Lv " + b.Level + "/" + CitySystem.MaxLevel(Content) + "   " + Fmt.Stars(stars) + "   Crash site", Theme.FontSmall, Theme.Gold, TextAnchor.MiddleLeft, "LevelLine");
            var about = Ui.Label(card, "Aliens in its seats join your runs.", 24, Theme.TextDim, TextAnchor.MiddleLeft, "About");
            about.horizontalOverflow = HorizontalWrapMode.Wrap;

            // the seats: an alien in a seat is a tap away from leaving it; an empty seat waits for one from the list below
            int slots = CitySystem.WorkerSlots(State, Content, b);
            var squad = CitySystem.Workers(State, b.Uid);
            var seats = Ui.Row(card, 96, 10, null, 0, "Seats");
            var sl = SliceKit.Chunky(seats, "Squad", 28, Theme.TextDim, TextAnchor.MiddleLeft, "Label");
            Ui.Size(sl, 120, 96, 0);
            for (int i = 0; i < slots; i++)
            {
                if (i < squad.Count)
                {
                    string id = squad[i].TypeId;
                    var sb = Ui.Button(seats, "", () => Run(new AlienAssign { AlienTypeId = id, BuildingUid = null }), new Color(0.22f, 0.4f, 0.3f, 1), 92, 92, true, 20);
                    sb.name = "Seat:" + id;
                    var img = SummonKit.Img(sb.transform, ArtCatalog.Alien(id) ?? HudArt.AlienCritter(Theme.Good), "Alien");
                    Ui.Stretch(img.rectTransform, 6, 6, 6, 6);
                }
                else
                {
                    var eb = Ui.Button(seats, "+", null, new Color(0.3f, 0.26f, 0.5f, 1), 92, 92, false, 40);
                    eb.name = "EmptySeat";
                }
            }
            bool full = squad.Count >= slots;

            // the other aliens: tap one to seat it (it leaves its building after the City)
            var others = new List<OwnedAlien>();
            foreach (var a in State.Aliens.Owned.Values) if (a.WorkBuildingId != b.Uid) others.Add(a);
            others.Sort((x, y) => string.CompareOrdinal(x.TypeId, y.TypeId));
            if (others.Count > 0)
            {
                Ui.Label(card, full ? "Seats full: tap a squad alien to free its seat." : "Tap an alien to seat it:", 22, full ? Theme.TextDim : Theme.Cream, TextAnchor.MiddleLeft, "PickHint");
                var row = Ui.Row(card, 150, 10, null, 0, "Aliens");
                row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                int shown = 0;
                foreach (var a in others)
                {
                    if (shown++ >= 6) break;
                    string id = a.TypeId;
                    var ab = Ui.Button(row, "", () => Run(new AlienAssign { AlienTypeId = id, BuildingUid = b.Uid }), new Color(0.2f, 0.17f, 0.32f, 1), 120, 146, !full, 18);
                    ab.name = "Pick:" + id;
                    var img = SummonKit.Img(ab.transform, ArtCatalog.Alien(id) ?? HudArt.AlienCritter(Theme.Good), "Art");
                    SummonKit.Place(img, new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(92, 92));
                    var work = CitySystem.Building(State, a.WorkBuildingId);
                    var t = Ui.Label(ab.transform, (Content.AlienType(id)?.Name ?? id) + (work != null ? "\n<size=16>" + Content.Blueprint(work.BlueprintId)?.Name + "</size>" : ""), 18, Color.white, TextAnchor.MiddleCenter, "Name");
                    SummonKit.Place(t, new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(116, 48));
                }
            }
            else if (squad.Count == 0) Ui.Label(card, "No aliens yet: call them at the Alien Signal.", 22, Theme.TextDim, TextAnchor.MiddleLeft, "PickHint");

            foreach (var boon in def.Boons)
                Ui.Label(card, CityScreen.BoonLine(boon.Kind, CitySystem.BoonValue(State, Content, b, boon.Kind)), Theme.FontSmall, Theme.Cream, TextAnchor.MiddleLeft, "Boon:" + boon.Kind);

            // level-up (its cost type) / the running upgrade
            if (b.TimerId != null) TimerControls(card, b.TimerId, "Upgrade");
            else if (b.Level >= CitySystem.MaxLevel(Content)) Ui.Button(card, "Max level", null, Theme.Button, -1, 90, false, 28).name = "Upgrade";
            else
            {
                var cost = CitySystem.UpgradeCost(Content, def, b.Level);
                var urow = Ui.Row(card, 96, 10, null, 0, "UpgradeRow");
                Ui.Button(urow, "Level up", () => Run(new CityUpgrade { Uid = b.Uid }), Theme.Hex(0xF6C94A), 230, 90, State.Wallet.CanAfford(cost), 30).name = "Upgrade";
                var costs = Ui.Column(urow, 2, 0, null, "UpgradeCost");
                Ui.Size(costs, -1, 90, 1);
                var crow = Ui.Row(costs, 46, 6, null, 0, "Costs");
                crow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                foreach (var c in cost)
                {
                    var ci = SummonKit.Img(crow, SummonArt.CurrencyIcon(c.Currency), "CostIcon:" + c.Currency);
                    Ui.Size(ci, 40, 40, 0);
                    var ct = Ui.Label(crow, Fmt.Num(c.Amount), 26, State.Wallet.Get(c.Currency) >= c.Amount ? Theme.Text : Theme.Bad, TextAnchor.MiddleLeft, "Cost:" + c.Currency);
                    Ui.Size(ct, 110, 46, 0);
                }
                Ui.Label(costs, "Lv " + b.Level + " → " + (b.Level + 1) + " · " + CityScreen.CostTypeName(def.CostType) + " · "
                    + Fmt.Duration(CitySystem.UpgradeDurationMs(State, Content, def, b.Level, Now)), 20, Theme.TextDim, TextAnchor.MiddleLeft, "CostType");
            }
            // star-up (blueprint shards): more stars open more seats (5★, 10★)
            if (owned != null && owned.Stars < CitySystem.MaxStars)
            {
                long shards = State.Wallet.Get(Currencies.BlueprintShards(def.Id)), need = CitySystem.StarCost(Content, owned);
                Ui.Button(card, "★ Star up  " + shards + "/" + need + " shards", () => Run(new BlueprintStarUp { BlueprintId = def.Id }), Theme.Warn, -1, 72, shards >= need, 24).name = "StarUp";
            }
            if (!Session.IsUnlocked(Features.BiomassFarm))
            {
                long at = Features.UnlockLevel(Content.Feature(Features.BiomassFarm));
                var note = Ui.Label(card, "The crash site grows into your City at level " + at + ".", 22, HubKit.Cyan, TextAnchor.MiddleCenter, "CityNote");
                note.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
        }
    }
}
