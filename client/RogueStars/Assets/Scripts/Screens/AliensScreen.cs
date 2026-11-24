using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>Owned aliens: level/star up, work assignment and the combat squad.</summary>
    public sealed class AliensScreen : UiScreen
    {
        public override string Title => "Aliens";

        private List<OwnedAlien> Sorted()
        {
            var list = new List<OwnedAlien>(State.Aliens.Owned.Values);
            list.Sort((a, b) =>
            {
                var ra = Content.AlienType(a.TypeId)?.Rarity ?? Rarity.Common;
                var rb = Content.AlienType(b.TypeId)?.Rarity ?? Rarity.Common;
                return ra != rb ? rb.CompareTo(ra) : string.CompareOrdinal(a.TypeId, b.TypeId);
            });
            return list;
        }

        protected override void Build(RectTransform body)
        {
            int slots = CitySystem.SquadSlots(State, Content);
            var squadCard = Card(body, Theme.Header);
            Section(squadCard, "Squad (" + State.Aliens.Squad.Count + "/" + slots + " slots)");
            if (slots == 0) Note(squadCard, "Build housing to unlock squad slots.");
            var squad = AlienSystem.ActiveSquad(State, Content);
            Ui.Label(squadCard, squad.Count == 0 ? "Empty" : string.Join(", ", squad.ConvertAll(id => Content.AlienType(id)?.Name ?? id)), Theme.FontNormal);

            var aliens = Sorted();
            if (aliens.Count == 0) Note(body, "No aliens yet. Aliens arrive from Alien Signals and events.");
            foreach (var owned in aliens) AlienCard(body, owned, slots);
        }

        private void AlienCard(RectTransform body, OwnedAlien a, int slots)
        {
            var def = Content.AlienType(a.TypeId);
            var color = Theme.RarityColor(def?.Rarity ?? Rarity.Common);
            var card = Card(body, Color.Lerp(color, Theme.Panel, 0.75f));
            var art = ArtCatalog.Alien(a.TypeId);
            var head = Ui.Row(card, art != null ? 104 : 60);
            if (art != null)
            {
                var portrait = Ui.Panel(head, Color.white, "AlienSprite");
                portrait.sprite = art;
                portrait.preserveAspect = true;
                portrait.raycastTarget = false;
                Ui.Size(portrait, 104, 104, 0, 0);
            }
            Ui.Label(head, (def?.Name ?? a.TypeId) + "  " + Fmt.Stars(a.Stars), Theme.FontNormal, Color.Lerp(color, Color.white, 0.4f));
            Ui.Label(head, "Lv " + a.Level + "/" + AlienSystem.MaxLevel(Content, a), Theme.FontSmall, Theme.Text, TextAnchor.MiddleRight);
            // a sidekick (sidekick pass): its one ability, its power (HP / ATK / DEF fold into it) and how often it acts (Speed)
            var ability = Ui.Label(card, AbilityLine(Content, a), Theme.FontSmall, Color.Lerp(color, Color.white, 0.55f), TextAnchor.MiddleLeft, "Ability");
            ability.horizontalOverflow = HorizontalWrapMode.Wrap;
            Note(card, PowerLine(Content, a) + " · Work " + AlienSystem.WorkPower(Content, a).ToString("0.#"));
            string work = a.WorkBuildingId != null ? "works at " + (Content.Blueprint(a.WorkBuildingId)?.Name ?? a.WorkBuildingId) : "idle";
            bool inSquad = State.Aliens.Squad.Contains(a.TypeId);
            Note(card, work + (inSquad ? " · in squad" : "") + (a.Traits.Count > 0 ? " · traits: " + string.Join(", ", a.Traits.ConvertAll(t => Content.AlienTrait(t)?.Name ?? t)) : ""));

            var row = Ui.Row(card, 80, 8);
            long grain = AlienSystem.LevelCost(Content, a);
            bool canLevel = a.Level < AlienSystem.MaxLevel(Content, a) && State.Wallet.CanAfford(Currencies.Grain, grain);
            Ui.Button(row, "Lv up\n<size=20>" + Fmt.Num(grain) + " grain</size>", () => Run(new AlienLevelUp { AlienTypeId = a.TypeId }), Theme.Good, -1, 80, canLevel, 26);
            long shards = State.Wallet.Get(Currencies.AlienShards(a.TypeId));
            long need = AlienSystem.StarCost(Content, a);
            Ui.Button(row, "Star up\n<size=20>" + shards + "/" + need + " shards</size>", () => Run(new AlienStarUp { AlienTypeId = a.TypeId }), Theme.Warn, -1, 80,
                a.Stars < AlienSystem.MaxStars && shards >= need, 26);
            Ui.Button(row, "Assign", () => AssignModal(a), Theme.Button, -1, 80, true, 26);
            Ui.Button(row, inSquad ? "Bench" : "Squad", () => ToggleSquad(a.TypeId, slots), Theme.Accent, -1, 80, inSquad || State.Aliens.Squad.Count < slots, 26);
        }

        /// <summary>"Ability: Block — Intercept the next hit on the worm." (the ability of the alien's current star tier).</summary>
        public static string AbilityLine(ContentDb content, OwnedAlien a)
        {
            var skill = content.Skill(AlienSystem.ArchetypeSkillId(content, a));
            if (skill == null) return "Ability: none";
            string name = BattlePlaybackScreen.AbilityName(skill.Id, content.AlienType(a.TypeId)?.Name, content);
            return "Ability: " + name + " — " + SkillText.Full(skill);
        }

        /// <summary>"Power 52 · acts about every 3 turns" (ability power before the squad's alien power; Speed sets the act frequency).</summary>
        public static string PowerLine(ContentDb content, OwnedAlien a) =>
            "Power " + Fmt.Num(Math.Round(AlienSystem.AbilityPower(content, a))) + " · " + ActsText(AlienSystem.ActsEvery(content, a));

        /// <summary>"acts about every 3 turns" / "every 2.5 turns" / "every turn" (halves).</summary>
        public static string ActsText(double every)
        {
            if (double.IsInfinity(every) || every <= 0) return "never acts";
            double r = Math.Max(1, Math.Round(every * 2) / 2);
            return r <= 1 ? "acts every turn" : "acts about every " + r.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " turns";
        }

        private void ToggleSquad(string typeId, int slots)
        {
            var squad = new List<string>(State.Aliens.Squad);
            if (!squad.Remove(typeId)) squad.Add(typeId);
            if (squad.Count > slots) squad.RemoveRange(slots, squad.Count - slots);
            var cmd = new AlienSetSquad();
            cmd.AlienTypeIds.AddRange(squad);
            Run(cmd);
        }

        private void AssignModal(OwnedAlien a)
        {
            Modal("Assign " + (Content.AlienType(a.TypeId)?.Name ?? a.TypeId), b =>
            {
                Ui.Button(b, "Unassign (idle)", () => Run(new AlienAssign { AlienTypeId = a.TypeId, BlueprintId = null }), Theme.Button, -1, 80, a.WorkBuildingId != null);
                int options = 0;
                foreach (var owned in CitySystem.SortedBlueprints(State))
                {
                    var bp = owned;
                    var def = Content.Blueprint(bp.Id);
                    if (def == null || !CitySystem.IsActive(bp)) continue;
                    int slots = CitySystem.WorkerSlots(Content, def, bp);
                    if (slots <= 0) continue;
                    int used = CitySystem.Workers(State, bp.Id).Count;
                    options++;
                    string bonus = def.ProduceCurrency != null ? " +" + Fmt.Pct(AlienSystem.WorkBonusPct(State, Content, a, def.ProduceCurrency)) : "";
                    Ui.Button(b, def.Name + " (" + used + "/" + slots + ")" + bonus, () => Run(new AlienAssign { AlienTypeId = a.TypeId, BlueprintId = bp.Id }),
                        Theme.Button, -1, 80, used < slots && a.WorkBuildingId != bp.Id);
                }
                if (options == 0) Note(b, "No active building with worker slots.");
            }, new ModalButton("Close", null));
        }
    }
}
