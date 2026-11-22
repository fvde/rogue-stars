using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>Hero personas: unlock by shards, stars 1–10, levels (Hero XP), equip, trait skills by star.</summary>
    public sealed class HeroesScreen : UiScreen
    {
        public override string Title => "Heroes";

        protected override void Build(RectTransform body)
        {
            var cfg = Content.HeroConfig;
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
                var card = Card(body, Color.Lerp(Theme.FamilyColor(def.Family), Theme.Panel, equipped ? 0.55f : 0.8f));
                var head = Ui.Row(card, 56, 8);
                Ui.Label(head, def.Name + (h != null ? "  " + Fmt.Stars(h.Stars) : ""), Theme.FontNormal, h != null ? Theme.Text : Theme.TextDim);
                Ui.Label(head, def.Family + (def.SecondaryFamily != SkillFamily.None ? " / " + def.SecondaryFamily : ""), Theme.FontSmall, Theme.FamilyColor(def.Family), TextAnchor.MiddleRight);
                var traits = new List<string>();
                foreach (var t in def.Traits)
                {
                    bool have = h != null && h.Stars >= t.Star;
                    traits.Add((have ? "" : "<color=#777788>") + t.Star + "★ " + (Content.Skill(t.SkillId)?.Name ?? t.SkillId) + (have ? "" : "</color>"));
                }
                Ui.Label(card, string.Join(" · ", traits), 22);
                var row = Ui.Row(card, 80, 8);
                if (h == null)
                {
                    long need = cfg.StarShardCosts.Count > 1 ? cfg.StarShardCosts[1] : 0;
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

    /// <summary>Spaceship mounts: stars 1–5 then awakening 1–5 (shards + mount cores), Fuel levels, weapons, equip.</summary>
    public sealed class MountsScreen : UiScreen
    {
        public override string Title => "Mounts";

        protected override void Build(RectTransform body)
        {
            var cfg = Content.MountConfig;
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
                var card = Card(body, Color.Lerp(Theme.FamilyColor(def.Family), Theme.Panel, active ? 0.55f : 0.8f));
                var head = Ui.Row(card, 56, 8);
                Ui.Label(head, def.Name + (m != null ? "  " + Fmt.Stars(m.Stars) + (m.Awaken > 0 ? " A" + m.Awaken : "") : "  (not owned)"), Theme.FontNormal, m != null ? Theme.Text : Theme.TextDim);
                Ui.Label(head, def.Family.ToString(), Theme.FontSmall, Theme.FamilyColor(def.Family), TextAnchor.MiddleRight);
                var weapons = new List<string>();
                foreach (var w in def.Weapons) weapons.Add((w.Awaken > 0 ? "A" + w.Awaken : w.Star + "★") + " " + (Content.Skill(w.SkillId)?.Name ?? w.SkillId));
                Ui.Label(card, string.Join(" · ", weapons), 22);
                if (m == null) { Note(card, "Shards " + shards + " — mounts come from Signals and events."); continue; }
                var row = Ui.Row(card, 80, 8);
                if (m.Stars < cfg.MaxStars)
                {
                    long cost = cfg.StarShardCosts[m.Stars + 1];
                    Ui.Button(row, "Star up\n<size=20>" + shards + "/" + cost + "</size>", () => Run(new MountStarUp { MountId = mid }), Theme.Warn, -1, 80, shards >= cost, 26);
                }
                else
                {
                    int next = m.Awaken + 1;
                    bool can = m.Awaken < cfg.MaxAwaken && next < cfg.AwakenShardCosts.Count;
                    string c = can ? shards + "/" + cfg.AwakenShardCosts[next] + " + " + cfg.AwakenCoreCosts[next] + " cores" : "max";
                    Ui.Button(row, "Awaken\n<size=20>" + c + "</size>", () => Run(new MountAwaken { MountId = mid }), Theme.Warn, -1, 80,
                        can && shards >= cfg.AwakenShardCosts[next] && State.Wallet.Get(Currencies.MountCores) >= cfg.AwakenCoreCosts[next], 26);
                }
                int max = cfg.MaxLevel(m.Stars, m.Awaken);
                bool canLevel = m.Level < max && m.Level < cfg.LevelFuelCosts.Count;
                long fuel = canLevel ? cfg.LevelFuelCosts[m.Level] : 0;
                Ui.Button(row, "Lv " + m.Level + "/" + max + "\n<size=20>" + (canLevel ? Fmt.Num(fuel) + " fuel" : "cap") + "</size>", () => Run(new MountLevelUp { MountId = mid }), Theme.Good, -1, 80,
                    canLevel && State.Wallet.CanAfford(Currencies.Fuel, fuel), 26);
                Ui.Button(row, active ? "Active" : "Equip", () => Run(new MountEquip { MountId = mid }), active ? Theme.Accent : Theme.Button, 200, 80, !active, 26);
            }
        }
    }
}
