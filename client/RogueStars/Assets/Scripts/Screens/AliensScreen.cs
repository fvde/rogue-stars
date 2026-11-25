using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Owned aliens (aliens pass): stars are the only progression, so each card shows the ability of its star, what the next star
    /// changes, how often it acts, its synergies and traits (genes stay hidden until they do something); star up, breed (Grain's sink), work and squad.
    /// No levels, no stats.
    /// </summary>
    public sealed class AliensScreen : UiScreen
    {
        public override string Title => "Aliens";
        public override string[] IntroFeatures => new[] { Features.Aliens, Features.AlienSquad };

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
            Section(squadCard, SquadTitle(State.Aliens.Squad.Count, slots)).name = "SquadTitle";
            var squad = AlienSystem.ActiveSquad(State, Content);
            Ui.Label(squadCard, squad.Count == 0 ? "Empty" : string.Join(", ", squad.ConvertAll(id => Content.AlienType(id)?.Name ?? id)), Theme.FontNormal);
            string hint = SquadHint(Content, State.Aliens.Squad);
            if (hint != null) Note(squadCard, hint).color = Theme.Warn;
            if (State.Aliens.LevelGrainRefunded > 0)
                Note(squadCard, "Alien levels are gone: stars are the only progression. " + Fmt.Num(State.Aliens.LevelGrainRefunded) + " Grain spent on levels came back for breeding.");

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
            var head = Ui.Row(card, 104);
            var portrait = Ui.Panel(head, Color.white, art != null ? "AlienSprite" : "AlienCritter");
            portrait.sprite = art != null ? art : HudArt.AlienCritter(FallbackColor(a.TypeId)); // one colour per alien until its art exists
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            Ui.Size(portrait, 104, 104, 0, 0);
            Ui.Label(head, (def?.Name ?? a.TypeId) + "  " + Fmt.Stars(a.Stars), Theme.FontNormal, Color.Lerp(color, Color.white, 0.4f));
            Ui.Label(head, SynergyLine(def), Theme.FontSmall, Theme.Text, TextAnchor.MiddleRight, "Synergies");
            // its one ability at its star, what the next star changes, and how often it acts (Speed)
            var ability = Ui.Label(card, AbilityLine(Content, a), Theme.FontSmall, Color.Lerp(color, Color.white, 0.55f), TextAnchor.MiddleLeft, "Ability");
            ability.horizontalOverflow = HorizontalWrapMode.Wrap;
            string next = NextStarLine(Content, a);
            if (next != null) Note(card, next).name = "NextStar";
            Note(card, ActsText(AlienSystem.ActsEvery(Content, a)) + " · numbers are % of the worm's ATK / max HP").name = "Acts";
            Note(card, TraitsLine(Content, a)).name = "Traits";
            string work = a.WorkBuildingId != null ? "works at " + (Content.Blueprint(a.WorkBuildingId)?.Name ?? a.WorkBuildingId) : "idle";
            bool inSquad = State.Aliens.Squad.Contains(a.TypeId);
            Note(card, "Work +" + Fmt.Whole(AlienSystem.WorkPower(Content, a)) + "% · " + work + (inSquad ? " · in squad" : ""));

            var row = Ui.Row(card, 80, 8);
            long shards = State.Wallet.Get(Currencies.AlienShards(a.TypeId));
            long need = AlienSystem.StarCost(Content, a);
            Ui.Button(row, a.Stars >= AlienSystem.MaxStars ? "Max ★" : "Star up\n<size=20>" + shards + "/" + need + " shards</size>",
                () => Run(new AlienStarUp { AlienTypeId = a.TypeId }), Theme.Warn, -1, 80, a.Stars < AlienSystem.MaxStars && shards >= need, 26);
            bool breeding = Features.IsUnlocked(State, Content, Features.Breeding);
            Ui.Button(row, "Breed\n<size=20>for shards</size>", () => App.Router.Push(new BreedingScreen(a.TypeId)), Theme.Good, -1, 80, breeding, 26);
            Ui.Button(row, "Assign", () => AssignModal(a), Theme.Button, -1, 80, true, 26);
            Ui.Button(row, inSquad ? "Bench" : "Squad", () => ToggleSquad(a.TypeId, slots), Theme.Accent, -1, 80, inSquad || State.Aliens.Squad.Count < slots, 26);
        }

        /// <summary>"Squad 2/3"; without housing slots (or fewer slots than members): "Squad 2 · build housing for slots" (AL1).</summary>
        public static string SquadTitle(int members, int slots) =>
            slots <= 0 || members > slots ? "Squad " + members + " · build housing for slots" : "Squad " + members + "/" + slots;

        /// <summary>A stable colour per alien id for the drawn critter of an alien without art (no two aliens share the same face).</summary>
        public static Color FallbackColor(string typeId)
        {
            uint h = 2166136261;
            foreach (char ch in typeId ?? "") { h ^= ch; h *= 16777619; }
            return Color.HSVToRGB((h % 360) / 360f, 0.55f, 0.95f);
        }

        /// <summary>"Stone Guard — Guard the worm: the next hit it takes deals 20% less." (the ability of the alien's star).</summary>
        public static string AbilityLine(ContentDb content, OwnedAlien a)
        {
            var skill = content.Skill(AlienSystem.ArchetypeSkillId(content, a));
            if (skill == null) return "Ability: none";
            string name = BattlePlaybackScreen.AbilityName(skill.Id, content.AlienType(a.TypeId)?.Name, content);
            return name + " — " + SkillText.Full(skill);
        }

        /// <summary>
        /// What the next star brings: "Next ★ (4★): Hit … 18% …" or, between tier stars of a small range, the next tier star that changes
        /// the card; null at 10★.
        /// </summary>
        public static string NextStarLine(ContentDb content, OwnedAlien a)
        {
            if (a.Stars >= AlienSystem.MaxStars) return null;
            string now = SkillText.Full(content.Skill(AlienSystem.ArchetypeSkillId(content, a)));
            for (int star = a.Stars + 1; star <= AlienSystem.MaxStars; star++)
            {
                var s = content.Skill(AlienSystem.AbilityAt(content, a.TypeId, star));
                string text = SkillText.Full(s);
                if (text == now) continue;
                return "Next: " + star + "★" + (AlienSystem.IsTierStar(content, star) ? " (tier star)" : "") + " — " + text;
            }
            return null;
        }

        /// <summary>"Bleed · Fire · Radiation · Combo" (the synergies its ability serves).</summary>
        public static string SynergyLine(AlienTypeDef def)
        {
            if (def == null) return "";
            var names = new List<string>();
            foreach (var f in def.Synergies) names.Add(SkillText.FamilyName(f));
            return string.Join(" · ", names);
        }

        /// <summary>"Traits: Swift (+15% Speed: acts more often.) · Twin (+1 stack on every 2nd action.)" or "Traits: none".</summary>
        public static string TraitsLine(ContentDb content, OwnedAlien a)
        {
            if (a.Traits.Count == 0) return "Traits: none";
            var parts = new List<string>();
            foreach (var t in a.Traits)
            {
                var tr = content.AlienTrait(t);
                parts.Add(tr == null ? t : tr.Name + " (" + TraitText(tr).TrimEnd('.') + ")");
            }
            return "Traits: " + string.Join(" · ", parts);
        }

        /// <summary>A trait's description with display names (work traits name the resources: "+15% Hero XP, Blueprint Tokens and Alien Tokens production when working").</summary>
        public static string TraitText(AlienTraitDef tr)
        {
            if (tr.Kind != AlienTraitKind.Work) return tr.Description ?? "";
            var names = tr.WorkCurrencies.ConvertAll(c => Fmt.CurrencyName(c));
            string what = names.Count == 0 ? "" : names.Count == 1 ? names[0] + " " : string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1] + " ";
            return "+" + Fmt.OneDecimal(tr.WorkPct) + "% " + what + "production when working.";
        }

        /// <summary>A squad hint (review: Freeze resets Chill, so Frostmite works against Shiverpup's Chill), or null.</summary>
        public static string SquadHint(ContentDb content, List<string> squad)
        {
            bool chill = false, freeze = false;
            foreach (var id in squad)
            {
                var s = content.Skill(AlienSystem.AbilityAt(content, id, 1));
                if (s == null) continue;
                foreach (var t in s.Triggers)
                {
                    if (t.Effect != EffectKind.ApplyStatus) continue;
                    if (t.Status == StatusId.Chill) chill = true;
                    if (t.Status == StatusId.Freeze) freeze = true;
                }
            }
            return chill && freeze ? "Heads-up: any Freeze resets Chill, so a Freeze alien works against a Chill alien." : null;
        }

        /// <summary>"acts about every 3 turns" / "every 2.5 turns" / "every turn" (halves).</summary>
        public static string ActsText(double every)
        {
            if (double.IsInfinity(every) || every <= 0) return "never acts";
            double r = Math.Max(1, Math.Round(every * 2) / 2);
            return r <= 1 ? "acts every turn" : "acts about every " + Fmt.OneDecimal(r) + " turns";
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
