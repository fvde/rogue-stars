using NUnit.Framework;
using RogueStars.Client.Screens;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using UnityEngine;

namespace RogueStars.Client.Tests
{
    /// <summary>Skill redesign: card text colouring (new keywords, costs in the warning colour) and the new statuses' colours.</summary>
    public sealed class SkillRedesignUiTests
    {
        private static readonly ContentDb C = ContentDb.Default;

        [Test]
        public void CostLines_AreInTheWarningColour_AndNewKeywordsAreBlue()
        {
            string red = ColorUtility.ToHtmlStringRGB(SliceCards.CostRed), blue = ColorUtility.ToHtmlStringRGB(SliceCards.KeyBlue);
            var marked = SkillText.FullMarked(C, C.Skill("gen_overclocked"), 1);
            Assert.That(SkillText.StripMarks(marked), Is.EqualTo("+25% ATK. -15% max HP."));
            var rich = SliceCards.Keywords(marked);
            Assert.That(rich, Does.Contain("<color=#" + red + ">-15% max HP.</color>"));
            Assert.That(rich, Does.Not.Contain(SkillText.CostStart.ToString()));
            Assert.That(SliceCards.Keywords("Enemies are Exposed; Arcs and Echoes"), Does.Contain("<color=#" + blue + ">Exposed</color>"));
            Assert.That(SliceCards.Keywords("Enemies are Exposed; Arcs and Echoes"), Does.Contain("<color=#" + blue + ">Echoes</color>"));
            // an upgrade keeps the cost marked and the upside's arrow
            Assert.That(SkillText.StripMarks(SkillText.UpgradeFullMarked(C, C.Skill("gen_overclocked"), 1, 2)), Is.EqualTo("+25% → +40% ATK. -15% max HP."));
        }

        [Test]
        public void NewStatuses_HaveTheirOwnColours()
        {
            Assert.That(BattlePlaybackScreen.StatusColor(StatusId.Exposed), Is.Not.EqualTo(BattlePlaybackScreen.StatusColor(StatusId.None)));
            Assert.That(BattlePlaybackScreen.StatusColor(StatusId.Stagger), Is.Not.EqualTo(BattlePlaybackScreen.StatusColor(StatusId.None)));
            Assert.That(BattlePlaybackScreen.StatusColor(StatusId.Delay), Is.Not.EqualTo(BattlePlaybackScreen.StatusColor(StatusId.None)));
            Assert.That(BattlePlaybackScreen.StatusGlyph(StatusId.Exposed), Is.EqualTo("E"));
        }
    }
}
