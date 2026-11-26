using System;
using RogueStars.Client.World.Anim;
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

        private string _selected;

        /// <summary>The collection / summon chips sit at the bottom (kit chip tabs in the footer).</summary>
        public override float FooterHeight => 124;

        private bool CanSummon => Features.IsUnlocked(State, Content, GachaSystem.FeatureFor(GachaType.Alien));

        protected override void Build(RectTransform body)
        {
            if (Footer != null)
            {
                var chips = new List<string> { "Collection" };
                if (CanSummon) chips.Add("Summon");
                Kit.TabChips(Footer, chips, 0, i => { if (i == 1) App.Router.Push(new SignalsScreen(GachaType.Alien)); }, 84);
            }
            int slots = CitySystem.SquadSlots(State, Content);
            var aliens = Sorted();
            if (_selected == null || !State.Aliens.Owned.ContainsKey(_selected)) _selected = aliens.Count > 0 ? aliens[0].TypeId : null;

            Stage(body, slots);

            // the bonus row: what the collection gives (squad seats, aliens owned, their work power)
            double work = 0;
            foreach (var o in aliens) work += AlienSystem.WorkPower(Content, o);
            Kit.StatPills(body, new List<(string key, string abbr, Color color, string value)>
            {
                ("Squad", "SQD", Theme.Accent, State.Aliens.Squad.Count + "/" + Math.Max(0, slots)),
                ("Owned", "OWN", Theme.Good, aliens.Count + "/" + Content.AlienTypes.Count),
                ("Work", "WRK", Theme.Blue, "+" + Fmt.Whole(work) + "%"),
            }, "BonusBar");

            if (aliens.Count == 0) { Note(body, "No aliens yet. Aliens arrive from Alien Signals and events."); return; }
            Kit.Ribbon(body, "Collection", new Color32(0x2E, 0x7A, 0x5A, 255), 76);
            var grid = Ui.Grid(body, new Vector2(CardW, CardH), 4, CardGap);
            grid.GetComponent<UnityEngine.UI.GridLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            foreach (var owned in aliens) CollectionCard(grid, owned);

            var sel = State.Aliens.Owned[_selected];
            var detail = AlienCard(body, sel, slots);
            if (_scrollToDetail) { ScrollToAfterBuild(detail); _scrollToDetail = false; }
        }

        private bool _scrollToDetail;
        public const float CardW = 223, CardH = 268, CardGap = 12;

        private void Select(string typeId)
        {
            _selected = typeId;
            _scrollToDetail = true;
            Rebuild();
        }

        /// <summary>The alien's picture: its art, else the drawn critter in the alien's colour. <paramref name="spriteName"/> is the name when art exists.</summary>
        private Image Portrait(Transform parent, OwnedAlien a, string spriteName)
        {
            var art = ArtCatalog.Alien(a.TypeId);
            var portrait = Ui.Panel(parent, Color.white, art != null ? spriteName : "AlienCritter");
            portrait.sprite = art != null ? art : HudArt.AlienCritter(FallbackColor(a.TypeId)); // one colour per alien until its art exists
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            return portrait;
        }

        /// <summary>The stage: the squad's aliens (up to three) on pedestals over an illustrated backdrop, or empty seats.</summary>
        private void Stage(RectTransform body, int slots)
        {
            var stage = Kit.Surface(body, Theme.Hex(0x2F6F5A), 40, Theme.Border, "backdrop_aliens", "Stage");
            Ui.Size(stage, -1, 540);
            var title = Kit.Title(stage.transform, SquadTitle(State.Aliens.Squad.Count, slots), 38, Color.white, TextAnchor.UpperCenter, "SquadTitle");
            Ui.Stretch(title.rectTransform, 16, 16, 16, 0);
            KitFit.On(title, 20);
            var squad = AlienSystem.ActiveSquad(State, Content);
            int shown = Math.Min(3, Math.Max(squad.Count, Math.Min(3, slots)));
            float[] xs = { 0.2f, 0.5f, 0.8f };
            float[] ys = { 0.05f, 0.11f, 0.05f };
            for (int i = 0; i < shown; i++)
            {
                int k = shown == 1 ? 1 : shown == 2 ? (i == 0 ? 0 : 2) : i; // one: centred, two: left and right
                var seat = Ui.Rect(stage.transform, "Seat:" + i);
                seat.anchorMin = seat.anchorMax = new Vector2(xs[k], ys[k]);
                seat.pivot = new Vector2(0.5f, 0);
                seat.sizeDelta = new Vector2(250, 330);
                seat.anchoredPosition = Vector2.zero;
                var ped = Ui.Panel(seat, new Color(0, 0, 0, 0.28f), "Pedestal");
                ped.sprite = UiShapes.Circle; ped.raycastTarget = false;
                var prt = ped.rectTransform;
                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0); prt.pivot = new Vector2(0.5f, 0.5f);
                prt.sizeDelta = new Vector2(220, 60); prt.anchoredPosition = new Vector2(0, 22);
                if (i < squad.Count && State.Aliens.Owned.TryGetValue(squad[i], out var a))
                {
                    var pic = Portrait(seat, a, "StageSprite");
                    var rt = pic.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0); rt.pivot = new Vector2(0.5f, 0);
                    rt.sizeDelta = new Vector2(240, 250); rt.anchoredPosition = new Vector2(0, 26);
                    UiCreature.Attach(pic, true);
                    var nm = Kit.Title(seat, (Content.AlienType(a.TypeId)?.Name ?? a.TypeId) + " " + Fmt.Stars(a.Stars), 26, Color.white, TextAnchor.LowerCenter, "SeatName");
                    nm.rectTransform.anchorMin = new Vector2(0, 0); nm.rectTransform.anchorMax = new Vector2(1, 0);
                    nm.rectTransform.pivot = new Vector2(0.5f, 0); nm.rectTransform.sizeDelta = new Vector2(0, 40); nm.rectTransform.anchoredPosition = new Vector2(0, -22);
                    string id = a.TypeId;
                    var btn = seat.gameObject.AddComponent<UnityEngine.UI.Button>();
                    btn.targetGraphic = ped;
                    ped.raycastTarget = true;
                    btn.onClick.AddListener(() => Select(id));
                }
                else
                {
                    var empty = Kit.Title(seat, "Empty\nseat", 28, new Color(1, 1, 1, 0.55f), TextAnchor.MiddleCenter, "EmptySeat");
                    empty.rectTransform.anchorMin = empty.rectTransform.anchorMax = new Vector2(0.5f, 0);
                    empty.rectTransform.sizeDelta = new Vector2(220, 120); empty.rectTransform.anchoredPosition = new Vector2(0, 90);
                }
            }
            string hint = SquadHint(Content, State.Aliens.Squad);
            if (hint != null)
            {
                var h = Ui.Label(stage.transform, hint, Theme.FontSmall, Theme.Warn, TextAnchor.LowerCenter, "SquadHint");
                Ui.Stretch(h.rectTransform, 24, 0, 24, 12);
            }
        }

        /// <summary>A collection card: rarity colour, picture, stars, a star-up arrow when it can star up, a shard progress pill.</summary>
        private void CollectionCard(Transform grid, OwnedAlien a)
        {
            var def = Content.AlienType(a.TypeId);
            var color = Theme.RarityColor(def?.Rarity ?? Rarity.Common);
            bool sel = a.TypeId == _selected;
            Kit.RarityCard(grid, "Alien:" + a.TypeId, color, () => Select(a.TypeId), out var c, 28, 10, 4, sel, sel ? Theme.Gold : (Color?)null, 0.42f);
            var pic = Portrait(c, a, "AlienSprite");
            Ui.Size(pic, -1, 132, 1);
            UiCreature.Attach(pic, true); // idle sway only on the cards
            var name = Ui.Display(c, def?.Name ?? a.TypeId, 24, Color.Lerp(color, Color.white, 0.6f), TextAnchor.MiddleCenter, "Name");
            Ui.Size(name, -1, 30, 1);
            KitFit.On(name, 14);
            bool inSquad = State.Aliens.Squad.Contains(a.TypeId);
            var row = Ui.Row(c, 34, 6, null, 0, "Stars");
            row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Kit.LevelBadge(row, Fmt.Stars(a.Stars), Theme.Gold, 32);
            if (inSquad) Kit.Pill(row, "SQUAD", Theme.Good, Theme.OnAccent, "InSquad", 32);
            long shards = State.Wallet.Get(Currencies.AlienShards(a.TypeId));
            long need = AlienSystem.StarCost(Content, a);
            bool max = a.Stars >= AlienSystem.MaxStars;
            Kit.ProgressPill(c, max ? 1 : need > 0 ? Mathf.Clamp01((float)shards / need) : 0, shards >= need && !max ? Theme.Good : Theme.Blue, max ? "MAX" : shards + "/" + need, 30);
            if (!max && shards >= need) Kit.CornerBadge(c, "▲", Theme.Good, "StarUpBadge", 48);
        }

        private RectTransform AlienCard(RectTransform body, OwnedAlien a, int slots)
        {
            var def = Content.AlienType(a.TypeId);
            var color = Theme.RarityColor(def?.Rarity ?? Rarity.Common);
            var card = Ui.Card(body, Color.Lerp(color, Theme.Panel, 0.75f), Color.Lerp(color, Color.white, 0.2f), Theme.RadiusCard, 24, 8, 5, "AlienDetail");
            var head = Ui.Row(card, 104);
            var portrait = Portrait(head, a, "DetailSprite");
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
            var workAt = CitySystem.Building(State, a.WorkBuildingId);
            string work = workAt != null ? "works at " + (Content.Blueprint(workAt.BlueprintId)?.Name ?? workAt.Uid) : "idle";
            bool inSquad = State.Aliens.Squad.Contains(a.TypeId);
            Note(card, "Work +" + Fmt.Whole(AlienSystem.WorkPower(Content, a)) + "% · " + work + (inSquad ? " · in squad" : ""));

            var row = Ui.Row(card, 80, 8);
            long shards = State.Wallet.Get(Currencies.AlienShards(a.TypeId));
            long need = AlienSystem.StarCost(Content, a);
            Kit.Button(row, a.Stars >= AlienSystem.MaxStars ? "Max ★" : "Star up\n<size=20>" + shards + "/" + need + " shards</size>",
                () => Run(new AlienStarUp { AlienTypeId = a.TypeId }), KitButtonColor.Gold, -1, 80, a.Stars < AlienSystem.MaxStars && shards >= need, 26);
            bool breeding = Features.IsUnlocked(State, Content, Features.Breeding);
            Kit.Button(row, "Breed\n<size=20>for shards</size>", () => App.Router.Push(new BreedingScreen(a.TypeId)), KitButtonColor.Positive, -1, 80, breeding, 26);
            Kit.Button(row, "Assign", () => AssignModal(a), KitButtonColor.Secondary, -1, 80, true, 26);
            Kit.Button(row, inSquad ? "Bench" : "Squad", () => ToggleSquad(a.TypeId, slots), KitButtonColor.Primary, -1, 80, inSquad || State.Aliens.Squad.Count < slots, 26);
            return card;
        }

        /// <summary>"Squad 2/3"; without housing slots (or fewer slots than members): "Squad 2 · build housing for slots" (AL1).</summary>
        public static string SquadTitle(int members, int slots) =>
            slots <= 0 || members > slots ? "Squad " + members + " · build the Launch Pod for seats" : "Squad " + members + "/" + slots;

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
                Ui.Button(b, "Unassign (idle)", () => Run(new AlienAssign { AlienTypeId = a.TypeId, BuildingUid = null }), Theme.Button, -1, 80, a.WorkBuildingId != null);
                int options = 0;
                foreach (var building in State.City.Buildings)
                {
                    var bld = building;
                    var def = Content.Blueprint(bld.BlueprintId);
                    if (def == null || !CitySystem.IsBuilt(bld)) continue;
                    int slots = CitySystem.WorkerSlots(State, Content, bld);
                    if (slots <= 0) continue;
                    int used = CitySystem.Workers(State, bld.Uid).Count;
                    options++;
                    string bonus = def.ProduceCurrency != null ? " +" + Fmt.Pct(AlienSystem.WorkBonusPct(State, Content, a, def.ProduceCurrency)) : "";
                    Ui.Button(b, def.Name + " " + bld.Uid.Substring(bld.Uid.IndexOf('#')) + " (" + used + "/" + slots + ")" + bonus,
                        () => Run(new AlienAssign { AlienTypeId = a.TypeId, BuildingUid = bld.Uid }), Theme.Button, -1, 80, used < slots && a.WorkBuildingId != bld.Uid);
                }
                if (options == 0) Note(b, "No built building with worker slots.");
            }, new ModalButton("Close", null));
        }
    }
}
