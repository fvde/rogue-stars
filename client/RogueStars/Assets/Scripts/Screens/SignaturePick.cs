using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// The run-start signature pick (signatures pass) in the slice card family: a violet ribbon "CHOOSE YOUR SIGNATURE", "It
    /// gives this run its identity", one card per offered signature (tier tag "Signature", the synergy as a chip, its one
    /// effect and when it awakens). Run UI glitches #24 (owner): the pick happens at the start of the run on the run board
    /// (RunScreen.SignatureBoardPick, phase Signature, before the first ROLL): <see cref="StartRun"/> sends RunStart with
    /// PickOnBoard and the board opens the pick; <see cref="Show"/> stays for showcases.
    /// </summary>
    public static class SignaturePick
    {
        public static readonly Color SignatureColor = new Color32(0xC0, 0x6C, 0xF0, 255);
        private static Sprite _ribbon;
        public static Sprite VioletRibbon => _ribbon != null ? _ribbon : (_ribbon = SliceCards.Ribbon(new Color32(0x9A, 0x5C, 0xE0, 255)));

        /// <summary>Card text: the effect, then the awakening ("Awakens at 3 / 6 Fire skills: Heat ×2 / ×3.").</summary>
        public static string Body(ContentDb c, SkillDef s)
        {
            if (s == null) return "";
            var at = c.RunTuning.SignatureAwakenAt;
            return SkillText.Full(s) + "\nAwakens at " + string.Join(" / ", Array.ConvertAll(at, x => x.ToString())) + " " + SkillText.FamilyName(s.Family) + " skills.";
        }

        public static SkillCardSpec Spec(ContentDb c, SkillDef s) => new SkillCardSpec
        {
            Id = s.Id, Tier = "Signature", TierColor = Theme.FamilyColor(s.Family), Family = s.Family, IconArt = ArtCatalog.SkillIcon(s.Id),
            Title = s.Name, Body = Body(c, s), Chip = SkillText.FamilyName(s.Family).ToUpperInvariant(), ChipName = "SynergyChip",
            ChipColor = Color.Lerp(Theme.FamilyColor(s.Family), Color.white, 0.45f),
            Tip = RunScreen.OfferTip(s.Id, 0, true), TipContent = c,
        };

        /// <summary>Opens the pick over <paramref name="layer"/>; <paramref name="onPick"/> gets the chosen signature id.</summary>
        public static UiModal Show(Transform layer, ContentDb c, IList<string> offer, Action<string> onPick)
        {
            float w = layer is RectTransform rt ? rt.rect.width : 0;
            float s = (w > 100 ? w : 1080) / 1080f;
            var root = SliceCards.Overlay(layer, "Choose a signature", out var modal);
            SliceCards.Header(root, s, VioletRibbon, "CHOOSE YOUR SIGNATURE", "Title", "It gives this run its identity!");
            var title = root.Find("Ribbon/Title")?.GetComponent<Text>();
            if (title != null) title.fontSize = (int)(62 * s);
            float step = Mathf.Min(300, 900f / Math.Max(1, offer.Count)), y = 1050;
            foreach (var id in offer)
            {
                string sid = id;
                var sk = c.Skill(sid);
                if (sk == null) continue;
                SliceCards.Card(root, s, y, Spec(c, sk), () => { modal.Close(); onPick(sid); }, null, "Button:Signature:" + sid);
                y += step;
            }
            UiTween.PopIn(root, 0.15f);
            return modal;
        }

        /// <summary>
        /// The run start every screen uses: RunStart with PickOnBoard — every run starts with the pick (playtest 0.1.325), now
        /// on the run's own board before the first roll (#24); a single offer (one unlocked signature) needs no pick.
        /// </summary>
        public static void StartRun(ClientApp app, RunStart cmd, Action<CommandResult> then)
        {
            cmd.PickOnBoard = true;
            app.Session.Run(cmd, then);
        }
    }
}
