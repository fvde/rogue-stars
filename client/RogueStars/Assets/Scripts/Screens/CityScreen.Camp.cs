using System.Collections.Generic;
using RogueStars.Client.World.Anim;
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
    /// Camp mode of the City screen (levels 6-14, docs/design/city.md "The crash site comes first"): the same diorama, but only the
    /// crash-site plaza with the Launch Pod; no dock, no digging, no layout edits. The pod's card shows only its seats.
    /// </summary>
    public sealed partial class CityScreen
    {
        /// <summary>The Launch Pod card in camp mode: the seats (tap a squad alien to free its seat, tap one below to seat it) and nothing else.</summary>
        private void CampSeats(RectTransform card, CityBuilding b, BlueprintDef def)
        {
            var about = Ui.Label(card, "Aliens in its seats join your runs.", 24, Theme.TextDim, TextAnchor.MiddleLeft, "About");
            about.horizontalOverflow = HorizontalWrapMode.Wrap;
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
                    sb.name = "Worker:" + id;
                    var img = SummonKit.Img(sb.transform, ArtCatalog.Alien(id) ?? HudArt.AlienCritter(Theme.Good), "Alien");
                    Ui.Stretch(img.rectTransform, 6, 6, 6, 6);
                    UiCreature.Attach(img, true);
                }
                else Ui.Button(seats, "+", null, new Color(0.3f, 0.26f, 0.5f, 1), 92, 92, false, 40).name = "EmptySlot";
            }
            bool full = squad.Count >= slots;
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
                    if (shown++ >= 4) break;
                    string id = a.TypeId;
                    var ab = Ui.Button(row, "", () => Run(new AlienAssign { AlienTypeId = id, BuildingUid = b.Uid }), new Color(0.2f, 0.17f, 0.32f, 1), 120, 146, !full, 18);
                    ab.name = "Pick:" + id;
                    var img = SummonKit.Img(ab.transform, ArtCatalog.Alien(id) ?? HudArt.AlienCritter(Theme.Good), "Art");
                    SummonKit.Place(img, new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(92, 92));
                    UiCreature.Attach(img, true);
                    var t = Ui.Label(ab.transform, Content.AlienType(id)?.Name ?? id, 18, Color.white, TextAnchor.MiddleCenter, "Name");
                    SummonKit.Place(t, new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(116, 40));
                }
            }
            else if (squad.Count == 0) Ui.Label(card, "No aliens yet: call them at the Alien Signal.", 22, Theme.TextDim, TextAnchor.MiddleLeft, "PickHint");
        }
    }
}
