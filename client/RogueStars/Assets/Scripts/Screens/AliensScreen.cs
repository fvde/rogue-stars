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
            var head = Ui.Row(card, 60);
            Ui.Label(head, (def?.Name ?? a.TypeId) + "  " + Fmt.Stars(a.Stars), Theme.FontNormal, Color.Lerp(color, Color.white, 0.4f));
            Ui.Label(head, "Lv " + a.Level + "/" + AlienSystem.MaxLevel(Content, a), Theme.FontSmall, Theme.Text, TextAnchor.MiddleRight);
            var stats = AlienSystem.CombatStats(Content, a);
            Note(card, "HP " + Fmt.Num(stats.Value(StatId.Hp)) + " · ATK " + Fmt.Num(stats.Value(StatId.Atk)) + " · DEF " + Fmt.Num(stats.Value(StatId.Def))
                       + " · Work " + AlienSystem.WorkPower(Content, a).ToString("0.#"));
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
