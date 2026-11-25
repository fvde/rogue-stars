using System.Collections.Generic;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Tests
{
    /// <summary>Visual playtest 0.1.224, run side (pure layout and text rules): D1, S2, B4, T1, E2, E3, G8.</summary>
    public sealed class RunPlaytestFixes3Tests
    {
        [TestCase(2400f)]
        [TestCase(2120f)]
        [TestCase(1920f)]
        public void Draft_CardsEndAboveTheReroll_OnEveryAspect(float refHeight)
        {
            foreach (int n in new[] { 1, 2, 3 })
            {
                var l = SliceCards.Layout(refHeight, n, SliceCards.RerollReserve);
                float lastBottom = l.FirstY + (n - 1) * l.Step + l.CardH / 2;
                Assert.That(lastBottom, Is.LessThanOrEqualTo(refHeight - SliceCards.RerollReserve + 0.01f), n + " cards at " + refHeight);
                Assert.That(l.FirstY - l.CardH / 2, Is.GreaterThanOrEqualTo(l.RibbonY + 149.9f), "cards start under the subtitle");
            }
            var tall = SliceCards.Layout(2400, 3, SliceCards.RerollReserve);
            Assert.That(tall.FirstY, Is.EqualTo(1050), "20:9 keeps the reference layout");
            Assert.That(tall.CardH, Is.EqualTo(280));
        }

        [Test]
        public void Shop_UtilityTiles_FitRightOfTheShopkeeper()
        {
            foreach (int n in new[] { 1, 2, 3 })
                for (int k = 0; k < n; k++)
                {
                    RunScreen.ShopTileSlot(n, k, out float x, out float size);
                    Assert.That(x + size / 2, Is.LessThanOrEqualTo(1080 - RunScreen.ShopTilesMargin + 0.01f));
                    Assert.That(x - size / 2, Is.GreaterThanOrEqualTo(RunScreen.ShopTilesLeft - 0.01f));
                }
        }

        [Test]
        public void Dice_PopAboveTheWormsHead_OnScreen()
        {
            var size = new Vector2(400, 210);
            var head = new Vector2(540, -1100);
            var at = RunScreen.DicePlacement(head, size, 1080, 1920);
            Assert.That(at.y - size.y / 2, Is.GreaterThan(head.y), "the dice sit above the head");
            var edge = RunScreen.DicePlacement(new Vector2(20, -1100), size, 1080, 1920);
            Assert.That(edge.x - size.x / 2, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void CoachHand_SitsAtTheTargetsLowerRightCorner()
        {
            var tip = CoachMarks.HandTip(new Vector2(-200, -100), new Vector2(200, 100));
            Assert.That(tip.x, Is.GreaterThan(150).And.LessThan(200));
            Assert.That(tip.y, Is.LessThan(-60).And.GreaterThan(-100));
        }

        [Test]
        public void OutcomeRows_UseStatIcons_NotLetters()
        {
            Assert.That(RunScreen.OutcomeIconId(new OutcomeLine { Kind = OutcomeKind.Hp }), Is.EqualTo("heart"));
            Assert.That(RunScreen.OutcomeIconId(new OutcomeLine { Kind = OutcomeKind.Stat, Id = "Atk" }), Is.EqualTo("sword"));
            Assert.That(RunScreen.OutcomeIconId(new OutcomeLine { Kind = OutcomeKind.Stat, Id = "Def" }), Is.EqualTo("shield"));
            Assert.That(RunScreen.OutcomeIconId(new OutcomeLine { Kind = OutcomeKind.Coins }), Is.EqualTo("coin"));
        }

        [Test]
        public void EventTexts_PlayerWords_NoDoubleNegatives()
        {
            var c = ContentDb.Default;
            foreach (var ev in c.RunEvents.Values)
                foreach (var o in ev.Options)
                    foreach (var l in RunScenes.EffectLines(o, c))
                    {
                        Assert.That(l.Value, Does.Not.Match(@"^(Lose|Pay|Gain|Restore) [−+-]"), ev.Id + ": " + l.Value);
                        Assert.That(l.Value, Does.Not.Contain("account loot"), ev.Id);
                        Assert.That(l.Value, Does.Not.Contain("Enemies nearby"), ev.Id);
                    }
            var leave = new EventOption { Text = "Leave" };
            leave.Outcomes.Add(new EventOutcome());
            RunScenes.Halves(leave, c, out var blessing, out _);
            Assert.That(blessing, Is.EqualTo("Nothing changes"));
        }
    }
}
