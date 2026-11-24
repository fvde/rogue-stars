using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Hero personas: unlock by shards, stars 1–10, levels (Hero XP), equip. Hero and mount kits: each card shows the hero's line, what
    /// drives its Ability, the kit lines it has (Trait, Ability, the star clauses) and the next change in grey.
    /// </summary>
    public sealed class HeroesScreen : UiScreen
    {
        public override string Title => "Heroes";

        protected override void Build(RectTransform body)
        {
            var cfg = Content.HeroConfig;
            Ui.Button(body, "Summon events: get heroes and stars", () => App.Router.Push(new SummonEventScreen(RogueStars.Rules.Content.SummonEventType.Hero)), Theme.Purple).name = "OpenSummon";
            Note(body, "Hero XP " + Fmt.Num(State.Wallet.Get(Currencies.HeroXp)) + " · equipped: " + (string.IsNullOrEmpty(State.Heroes.Equipped) ? "none" : cfg.Defs.TryGetValue(State.Heroes.Equipped, out var eq) ? eq.Name : State.Heroes.Equipped));
            var ids = new List<string>(cfg.Defs.Keys);
            ids.Sort((a, b) =>
            {
                bool oa = HeroSystem.Get(State, a) != null, ob = HeroSystem.Get(State, b) != null;
                return oa != ob ? ob.CompareTo(oa) : string.CompareOrdinal(a, b);
            });
            foreach (var id in ids)
            {
                string hid = id;
                var def = cfg.Defs[hid];
                var h = HeroSystem.Get(State, hid);
                long shards = State.Wallet.Get(Currencies.HeroShards(hid));
                bool equipped = State.Heroes.Equipped == hid;
                var card = Card(body, Color.Lerp(Theme.Hex(0x7FB2FF), Theme.Panel, equipped ? 0.62f : 0.84f));
                card.name = "Hero:" + hid;
                var head = Ui.Row(card, 56, 8);
                Ui.Label(head, def.Name + (h != null ? "  " + Fmt.Stars(h.Stars) : ""), Theme.FontNormal, h != null ? Theme.Text : Theme.TextDim);
                Ui.Label(head, def.Meter, Theme.FontSmall, Theme.Gold, TextAnchor.MiddleRight);
                Ui.Label(card, def.Tagline, 22, Theme.TextDim);
                KitView.Lines(card, HeroSystem.KitAt(Content, hid, h != null ? h.Stars : 1), HeroSystem.NextKit(Content, hid, h != null ? h.Stars : 1));
                var row = Ui.Row(card, 80, 8);
                if (h == null)
                {
                    long need = Content.ShardStars.UnlockShards;
                    Ui.Button(row, "Unlock (" + shards + "/" + need + " shards)", () => Run(new HeroUnlock { HeroId = hid }), Theme.Accent, -1, 80, shards >= need);
                    continue;
                }
                long star = HeroSystem.NextStarCost(State, Content, hid);
                Ui.Button(row, star < 0 ? "Max stars" : "Star up\n<size=20>" + shards + "/" + star + "</size>", () => Run(new HeroStarUp { HeroId = hid }), Theme.Warn, -1, 80, star >= 0 && shards >= star, 26);
                bool canLevel = h.Level < cfg.MaxLevel(h.Stars) && h.Level < cfg.LevelXpCosts.Count;
                long xp = canLevel ? cfg.LevelXpCosts[h.Level] : 0;
                Ui.Button(row, "Lv " + h.Level + "/" + cfg.MaxLevel(h.Stars) + "\n<size=20>" + (canLevel ? Fmt.Num(xp) + " XP" : "cap") + "</size>", () => Run(new HeroLevelUp { HeroId = hid }), Theme.Good, -1, 80,
                    canLevel && State.Wallet.CanAfford(Currencies.HeroXp, xp), 26);
                Ui.Button(row, equipped ? "Equipped" : "Equip", () => Run(new HeroEquip { HeroId = hid }), equipped ? Theme.Accent : Theme.Button, 200, 80, !equipped, 26);
            }
        }
    }

    /// <summary>
    /// Spaceship mounts: stars 1–10 on the shared shard table (6★–10★ also take Mount Cores), Fuel levels, equip. Hero and mount kits: a vehicle on
    /// a reload; each card shows its passive and active at its step, the reload, its mount damage and the next step's lines in grey.
    /// </summary>
    public sealed class MountsScreen : UiScreen
    {
        public override string Title => "Mounts";

        protected override void Build(RectTransform body)
        {
            var cfg = Content.MountConfig;
            Ui.Button(body, "Summon events: get mounts and stars", () => App.Router.Push(new SummonEventScreen(RogueStars.Rules.Content.SummonEventType.Mount)), Theme.Purple).name = "OpenSummon";
            Note(body, "Fuel " + Fmt.Num(State.Wallet.Get(Currencies.Fuel)) + " · Mount Cores " + State.Wallet.Get(Currencies.MountCores));
            var ids = new List<string>(cfg.Defs.Keys);
            ids.Sort((a, b) =>
            {
                bool oa = MountSystem.Get(State, a) != null, ob = MountSystem.Get(State, b) != null;
                return oa != ob ? ob.CompareTo(oa) : string.CompareOrdinal(a, b);
            });
            foreach (var id in ids)
            {
                string mid = id;
                var def = cfg.Defs[mid];
                var m = MountSystem.Get(State, mid);
                long shards = State.Wallet.Get(Currencies.MountShards(mid));
                bool active = State.Mounts.Active == mid;
                var card = Card(body, Color.Lerp(Theme.Hex(0x7FB2FF), Theme.Panel, active ? 0.62f : 0.84f));
                card.name = "Mount:" + mid;
                var head = Ui.Row(card, 56, 8);
                Ui.Label(head, def.Name + (m != null ? "  " + Fmt.Stars(m.Stars) : "  (not owned)"), Theme.FontNormal, m != null ? Theme.Text : Theme.TextDim);
                int k = cfg.KitStep(m != null ? m.Stars : 1);
                double dmg = m != null ? MountSystem.MountStats(Content, mid, m).Value(StatId.MountPower) : 0;
                Ui.Label(head, "Reload " + def.Reloads[k] + (dmg > 0 ? " · +" + Fmt.Num(dmg) + "% dmg" : ""), Theme.FontSmall, Theme.Gold, TextAnchor.MiddleRight);
                Ui.Label(card, def.Tagline, 22, Theme.TextDim);
                var now = MountSystem.KitNow(Content, mid, m != null ? m.Stars : 1);
                KitView.Lines(card, now, null);
                if (k + 1 < cfg.KitSteps.Length)
                {
                    foreach (var line in MountSystem.KitNow(Content, mid, cfg.KitSteps[k + 1])) KitView.Lines(card, new List<KitText>(), line);
                }
                if (m == null)
                {
                    long unlock = Content.ShardStars.UnlockShards;
                    Ui.Button(card, "Unlock (" + shards + "/" + unlock + " shards)", () => Run(new MountUnlock { MountId = mid }), Theme.Accent, -1, 80, shards >= unlock).name = "Unlock:" + mid;
                    Note(card, "Mounts come from mount summon events: a drop is " + Content.ShardStars.BatchShards + " shards.");
                    continue;
                }
                var row = Ui.Row(card, 80, 8);
                long star = MountSystem.NextStarCost(State, Content, mid), cores = MountSystem.NextStarCores(State, Content, mid);
                string need = star < 0 ? "max" : shards + "/" + star + (cores > 0 ? " + " + Fmt.Num(cores) + " cores" : "");
                Ui.Button(row, (star < 0 ? "Max stars" : "Star up") + "\n<size=20>" + need + "</size>", () => Run(new MountStarUp { MountId = mid }), Theme.Warn, -1, 80,
                    star >= 0 && shards >= star && State.Wallet.Get(Currencies.MountCores) >= cores, 26);
                int max = cfg.MaxLevel(m.Stars);
                bool canLevel = m.Level < max && m.Level < cfg.LevelFuelCosts.Count;
                long fuel = canLevel ? cfg.LevelFuelCosts[m.Level] : 0;
                Ui.Button(row, "Lv " + m.Level + "/" + max + "\n<size=20>" + (canLevel ? Fmt.Num(fuel) + " fuel" : "cap") + "</size>", () => Run(new MountLevelUp { MountId = mid }), Theme.Good, -1, 80,
                    canLevel && State.Wallet.CanAfford(Currencies.Fuel, fuel), 26);
                Ui.Button(row, active ? "Active" : "Equip", () => Run(new MountEquip { MountId = mid }), active ? Theme.Accent : Theme.Button, 200, 80, !active, 26);
            }
        }
    }

    /// <summary>Kit lines on a hero / mount card: "1★ Trait · Starguard: …" (what it has), then the next change in grey.</summary>
    internal static class KitView
    {
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
