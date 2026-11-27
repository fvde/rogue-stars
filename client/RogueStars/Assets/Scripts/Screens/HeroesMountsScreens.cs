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
    /// Hero personas: unlock by shards, stars 1–10, levels (Hero XP), equip. HM1: each card = portrait on the left (the summon
    /// art; a locked silhouette when not owned), name + stars, a 2-line summary (tagline, what drives its Ability) and
    /// "Details" (the kit lines it has at its stars and the next change in grey, in a modal). Not owned: "Get in Summon".
    /// </summary>
    public sealed class HeroesScreen : UiScreen
    {
        public override string Title => "Heroes";
        public override string[] IntroFeatures => new[] { Features.Heroes };

        /// <summary>In place: one keyed part per hero card; the Hero XP line is live.</summary>
        protected override bool InPlace => true;

        protected override void Build(RectTransform body)
        {
            var cfg = Content.HeroConfig;
            Part(body, "summon", "", b => Ui.Button(b, "Summon events: get heroes and stars", () => OpenSummon(), Theme.Purple).name = "OpenSummon");
            string equippedName = string.IsNullOrEmpty(State.Heroes.Equipped) ? "none" : cfg.Defs.TryGetValue(State.Heroes.Equipped, out var eq) ? eq.Name : State.Heroes.Equipped;
            Part(body, "xp", equippedName, b => Live(Note(b, ""), () => "Hero XP " + Fmt.Num(State.Wallet.Get(Currencies.HeroXp)) + " · equipped: " + equippedName));
            var ids = new List<string>(cfg.Defs.Keys);
            ids.Sort((a, b) =>
            {
                bool oa = HeroSystem.Get(State, a) != null, ob = HeroSystem.Get(State, b) != null;
                return oa != ob ? ob.CompareTo(oa) : string.CompareOrdinal(a, b);
            });
            foreach (var id in ids)
            {
                string hid = id;
                Part(body, "hero:" + hid, HeroKey(hid), b => HeroCard(b, hid));
            }
        }

        /// <summary>Everything a hero card shows (its part key).</summary>
        private string HeroKey(string hid)
        {
            var cfg = Content.HeroConfig;
            var h = HeroSystem.Get(State, hid);
            long shards = State.Wallet.Get(Currencies.HeroShards(hid));
            if (h == null) return K("locked", shards >= Content.ShardStars.UnlockShards ? -1 : shards);
            bool canLevel = h.Level < cfg.MaxLevel(h.Stars) && h.Level < cfg.LevelXpCosts.Count;
            long xp = canLevel ? cfg.LevelXpCosts[h.Level] : 0;
            bool afford = canLevel && State.Wallet.CanAfford(Currencies.HeroXp, xp);
            return K(h.Stars, h.Level, shards, HeroSystem.NextStarCost(State, Content, hid), State.Heroes.Equipped == hid, canLevel, xp, afford,
                canLevel && !afford ? xp - State.Wallet.Get(Currencies.HeroXp) : 0);
        }

        private void HeroCard(RectTransform body, string hid)
        {
            var cfg = Content.HeroConfig;
            {
                var def = cfg.Defs[hid];
                var h = HeroSystem.Get(State, hid);
                long shards = State.Wallet.Get(Currencies.HeroShards(hid));
                bool equipped = State.Heroes.Equipped == hid;
                int stars = h != null ? h.Stars : 1;
                var info = KitView.Card(body, "Hero:" + hid, SummonArt.Featured(SummonEventType.Hero, hid), h != null, equipped,
                    def.Name + (h != null ? "  " + Fmt.Stars(h.Stars) : ""), def.Tagline, def.Meter,
                    () => KitView.Details(App.ModalLayer, hid, def.Name, HeroSystem.KitAt(Content, hid, stars), HeroSystem.NextKit(Content, hid, stars)));
                var row = KitView.ButtonRow(info);
                if (h == null)
                {
                    long need = Content.ShardStars.UnlockShards;
                    if (shards >= need) Ui.Button(row, "Unlock", () => Run(new HeroUnlock { HeroId = hid }), Theme.Accent, -1, 80, true, 26).name = "Unlock:" + hid;
                    else KitView.GetInSummon(row, shards, need, () => OpenSummon()).name = "GetInSummon:" + hid;
                    return;
                }
                long star = HeroSystem.NextStarCost(State, Content, hid);
                bool canStar = star >= 0 && shards >= star;
                Ui.ButtonWithReason(row, star < 0 ? "Max stars" : "Star up\n<size=20>" + shards + "/" + star + "</size>", () => Run(new HeroStarUp { HeroId = hid }), Theme.Warn, -1, 80, canStar,
                    star < 0 ? null : "Needs " + (star - shards) + " shards", 26);
                bool canLevel = h.Level < cfg.MaxLevel(h.Stars) && h.Level < cfg.LevelXpCosts.Count;
                long xp = canLevel ? cfg.LevelXpCosts[h.Level] : 0;
                bool affordLevel = canLevel && State.Wallet.CanAfford(Currencies.HeroXp, xp);
                Ui.ButtonWithReason(row, "Lv " + h.Level + "/" + cfg.MaxLevel(h.Stars) + "\n<size=20>" + (canLevel ? Fmt.Num(xp) + " XP" : "cap") + "</size>", () => Run(new HeroLevelUp { HeroId = hid }), Theme.Good, -1, 80,
                    affordLevel, canLevel ? "Needs " + Fmt.Num(xp - State.Wallet.Get(Currencies.HeroXp)) + " Hero XP" : "Star up to raise the cap", 26);
                Ui.Button(row, equipped ? "Equipped" : "Equip", () => Run(new HeroEquip { HeroId = hid }), equipped ? Theme.Accent : Theme.Button, 170, 80, !equipped, 26);
            }
        }

        private void OpenSummon() => App.Router.Push(new SummonEventScreen(SummonEventType.Hero));
    }

    /// <summary>
    /// Spaceship mounts: stars 1–10 on the shared shard table (shards only), Fuel levels, equip. HM1: portrait card like the heroes
    /// (summary: tagline, reload and mount damage; "Details" = passive and active at its step and the next step in grey).
    /// </summary>
    public sealed class MountsScreen : UiScreen
    {
        public override string Title => "Mounts";
        public override string[] IntroFeatures => new[] { Features.Mounts };

        /// <summary>In place: one keyed part per mount card; the Fuel line is live.</summary>
        protected override bool InPlace => true;

        protected override void Build(RectTransform body)
        {
            var cfg = Content.MountConfig;
            Part(body, "summon", "", b => Ui.Button(b, "Summon events: get mounts and stars", () => OpenSummon(), Theme.Purple).name = "OpenSummon");
            Part(body, "fuel", "", b => Live(Note(b, ""), () => "Fuel " + Fmt.Num(State.Wallet.Get(Currencies.Fuel))));
            var ids = new List<string>(cfg.Defs.Keys);
            ids.Sort((a, b) =>
            {
                bool oa = MountSystem.Get(State, a) != null, ob = MountSystem.Get(State, b) != null;
                return oa != ob ? ob.CompareTo(oa) : string.CompareOrdinal(a, b);
            });
            foreach (var id in ids)
            {
                string mid = id;
                Part(body, "mount:" + mid, MountKey(mid), b => MountCard(b, mid));
            }
        }

        /// <summary>Everything a mount card shows (its part key).</summary>
        private string MountKey(string mid)
        {
            var cfg = Content.MountConfig;
            var m = MountSystem.Get(State, mid);
            long shards = State.Wallet.Get(Currencies.MountShards(mid));
            if (m == null) return K("locked", shards >= Content.ShardStars.UnlockShards ? -1 : shards);
            int max = cfg.MaxLevel(m.Stars);
            bool canLevel = m.Level < max && m.Level < cfg.LevelFuelCosts.Count;
            long fuel = canLevel ? cfg.LevelFuelCosts[m.Level] : 0;
            bool afford = canLevel && State.Wallet.CanAfford(Currencies.Fuel, fuel);
            return K(m.Stars, m.Level, shards, MountSystem.NextStarCost(State, Content, mid), State.Mounts.Active == mid, canLevel, fuel, afford,
                canLevel && !afford ? fuel - State.Wallet.Get(Currencies.Fuel) : 0);
        }

        private void MountCard(RectTransform body, string mid)
        {
            var cfg = Content.MountConfig;
            {
                var def = cfg.Defs[mid];
                var m = MountSystem.Get(State, mid);
                long shards = State.Wallet.Get(Currencies.MountShards(mid));
                bool active = State.Mounts.Active == mid;
                int k = cfg.KitStep(m != null ? m.Stars : 1);
                double dmg = m != null ? MountSystem.MountStats(Content, mid, m).Value(StatId.MountPower) : 0;
                var now = MountSystem.KitNow(Content, mid, m != null ? m.Stars : 1);
                var next = k + 1 < cfg.KitSteps.Length ? MountSystem.KitNow(Content, mid, cfg.KitSteps[k + 1]) : null;
                var info = KitView.Card(body, "Mount:" + mid, SummonArt.Featured(SummonEventType.Mount, mid), m != null, active,
                    def.Name + (m != null ? "  " + Fmt.Stars(m.Stars) : ""), def.Tagline,
                    "Reload " + def.Reloads[k] + (dmg > 0 ? " · +" + Fmt.Num(dmg) + "% dmg" : ""),
                    () => KitView.Details(App.ModalLayer, mid, def.Name, now, null, next));
                var row = KitView.ButtonRow(info);
                if (m == null)
                {
                    long unlock = Content.ShardStars.UnlockShards;
                    if (shards >= unlock) Ui.Button(row, "Unlock", () => Run(new MountUnlock { MountId = mid }), Theme.Accent, -1, 80, true, 26).name = "Unlock:" + mid;
                    else KitView.GetInSummon(row, shards, unlock, () => OpenSummon()).name = "GetInSummon:" + mid;
                    return;
                }
                long star = MountSystem.NextStarCost(State, Content, mid);
                string need = star < 0 ? "max" : shards + "/" + star;
                Ui.ButtonWithReason(row, (star < 0 ? "Max stars" : "Star up") + "\n<size=20>" + need + "</size>", () => Run(new MountStarUp { MountId = mid }), Theme.Warn, -1, 80,
                    star >= 0 && shards >= star, star < 0 ? null : "Needs " + (star - shards) + " shards", 26);
                int max = cfg.MaxLevel(m.Stars);
                bool canLevel = m.Level < max && m.Level < cfg.LevelFuelCosts.Count;
                long fuel = canLevel ? cfg.LevelFuelCosts[m.Level] : 0;
                Ui.ButtonWithReason(row, "Lv " + m.Level + "/" + max + "\n<size=20>" + (canLevel ? Fmt.Num(fuel) + " Fuel" : "cap") + "</size>", () => Run(new MountLevelUp { MountId = mid }), Theme.Good, -1, 80,
                    canLevel && State.Wallet.CanAfford(Currencies.Fuel, fuel), canLevel ? "Needs " + Fmt.Num(fuel - State.Wallet.Get(Currencies.Fuel)) + " Fuel" : "Star up to raise the cap", 26);
                Ui.Button(row, active ? "Active" : "Equip", () => Run(new MountEquip { MountId = mid }), active ? Theme.Accent : Theme.Button, 170, 80, !active, 26);
            }
        }

        private void OpenSummon() => App.Router.Push(new SummonEventScreen(SummonEventType.Mount));
    }

    /// <summary>Hero / mount cards (HM1) and their kit lines: "1★ Trait · Starguard: …" (what it has), then the next change in grey.</summary>
    internal static class KitView
    {
        public const float PortraitSize = 170;

        /// <summary>
        /// A portrait card: the art on the left (a dark locked silhouette when not owned), name, two summary lines and a
        /// "Details" button on the right. Returns the right-hand column for the action row.
        /// </summary>
        public static RectTransform Card(Transform parent, string name, Sprite art, bool owned, bool selected, string title, string line1, string line2, Action details)
        {
            var card = Ui.Card(parent, Color.Lerp(Theme.Hex(0x7FB2FF), Theme.Panel, selected ? 0.62f : 0.84f), selected ? Theme.Accent : (Color?)null, Theme.RadiusCard, 20, 8, 4, name);
            var top = Ui.Row(card, PortraitSize, 18, null, 0, "Top");
            top.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            var frame = Ui.RoundPanel(top, Theme.PanelAlt, 30, "Portrait");
            Ui.Size(frame, PortraitSize, PortraitSize, 0);
            var img = Ui.Rect(frame.transform, "Art");
            Ui.Stretch(img, 8, 8, 8, 8);
            var pic = img.gameObject.AddComponent<Image>();
            pic.sprite = art; pic.preserveAspect = true; pic.raycastTarget = false;
            if (!owned)
            {
                pic.color = new Color(0.05f, 0.03f, 0.06f, 0.92f); // locked silhouette
                var lockRt = Ui.Rect(frame.transform, "Lock");
                lockRt.anchorMin = lockRt.anchorMax = new Vector2(0.5f, 0.5f);
                lockRt.sizeDelta = new Vector2(64, 64);
                var li = lockRt.gameObject.AddComponent<Image>();
                li.sprite = SummonArt.Icon("lock"); li.preserveAspect = true; li.raycastTarget = false;
            }
            var col = Ui.Column(top, 4, 0, null, "Info");
            Ui.Size(col, -1, PortraitSize, 1);
            col.GetComponent<VerticalLayoutGroup>().childForceExpandHeight = false;
            var head = Ui.Row(col, 54, 8, null, 0, "Head");
            Ui.Display(head, title, 34, owned ? Theme.Text : Theme.TextDim, TextAnchor.MiddleLeft, "Name");
            Ui.Button(head, "Details", details, Theme.Button, 150, 54, true, 22).name = "Details";
            Ui.Label(col, line1, 22, Theme.TextDim, TextAnchor.UpperLeft, "Summary");
            Ui.Label(col, line2, 22, Theme.Gold, TextAnchor.UpperLeft, "Summary2");
            return card;
        }

        /// <summary>The card's button row: buttons top-aligned at 80, room under them for a disabled reason (G4).</summary>
        public static RectTransform ButtonRow(Transform card)
        {
            var row = Ui.Row(card, 114, 8, null, 0, "Buttons");
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.childForceExpandHeight = false; h.childAlignment = TextAnchor.UpperLeft;
            return row;
        }

        /// <summary>The "Get in Summon" button of a hero / mount not owned yet (shard progress under it).</summary>
        public static Button GetInSummon(Transform row, long shards, long need, Action open) =>
            Ui.Button(row, "Get in Summon\n<size=20>" + shards + "/" + need + " shards</size>", open, Theme.Purple, -1, 80, true, 26);

        /// <summary>The kit in a modal: the lines it has, the next change in grey (heroes) or the next step's lines (mounts).</summary>
        public static UiModal Details(Transform layer, string id, string title, List<KitText> have, KitText next, List<KitText> nextStep = null)
        {
            Ui.Clear(layer);
            var modal = Ui.Modal(layer, title, b =>
            {
                Lines(b, have, next);
                if (nextStep != null) foreach (var line in nextStep) Lines(b, new List<KitText>(), line);
            });
            modal.Root.name = "Modal:Kit:" + id;
            return modal;
        }

        public static void Lines(Transform card, List<KitText> have, KitText next)
        {
            foreach (var line in have)
            {
                // a number line already names its element ("Starfall: 60% → 90% ATK.")
                bool named = line.Text.StartsWith(line.Name + ":", System.StringComparison.Ordinal);
                var l = Ui.Label(card, "<b>" + line.StepLabel + " " + line.Slot + (named ? ":</b> " : " · " + line.Name + ":</b> ") + line.Text, 22);
                l.name = "Kit:" + line.Slot + ":" + line.StepLabel;
            }
            if (next != null)
            {
                bool named = next.Text.StartsWith(next.Name + ":", System.StringComparison.Ordinal);
                var l = Ui.Label(card, "<color=#8A8AA0>Next " + next.StepLabel + " " + next.Slot + (named ? ": " : " · " + next.Name + ": ") + next.Text + "</color>", 20);
                l.name = "KitNext:" + next.Slot;
            }
        }
    }
}
