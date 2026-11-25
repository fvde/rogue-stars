using System;
using System.Collections.Generic;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Owner playtest (run shop): a stat perk bought in the shop shows on the run HUD and the worm's stats at once, while the
    /// shop is still open; the reroll price starts at the base price on every new shop visit.
    /// </summary>
    public sealed class RunShopFixTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; }
        [TearDown] public void TearDown() { RunScreen.InstantOutcomes = false; _f.Dispose(); }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private ActiveRun StartRun(params int[] shops)
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            var run = S.Run.Active;
            run.Tiles.Clear();
            for (int i = 0; i <= 30; i++) { var t = new RunTile { Id = i, TileId = i == 0 ? "start" : Array.IndexOf(shops, i) >= 0 ? "shop" : "empty", X = i }; if (i < 30) t.Next.Add(i + 1); run.Tiles.Add(t); }
            run.BoardEnd = 30; run.BossGateTile = -1; run.Position = 0; run.Trail.Clear(); run.Trail.Add(0);
            run.Coins = 5000;
            return run;
        }

        private RunScreen Open()
        {
            _f.App.Router.Push(new RunScreen());
            return (RunScreen)_f.App.Router.Current;
        }

        private Button BuyButton(int index)
        {
            var slot = FindNamed(_f.App.ModalLayer, "OfferSlot:" + index);
            Assert.That(slot, Is.Not.Null, "offer slot " + index);
            var b = slot.GetComponentInChildren<Button>(true);
            Assert.That(b, Is.Not.Null);
            return b;
        }

        private Button RerollButton()
        {
            foreach (var b in _f.App.ModalLayer.GetComponentsInChildren<Button>(true)) if (b.name.StartsWith("Button:Reroll")) return b;
            return null;
        }

        private string ShopHudAtk()
        {
            var shop = FindNamed(_f.App.ModalLayer, "Modal:Shop") ?? _f.App.ModalLayer;
            var chip = FindNamed(shop, "AtkChip");
            Assert.That(chip, Is.Not.Null, "the shop's run HUD shows ATK");
            return chip.GetComponent<Text>().text;
        }

        [Test]
        public void ShopStatPurchase_UpdatesTheHudAndTheWormsStats_WhileTheShopIsOpen()
        {
            StartRun(2);
            Assert.That(_f.Service.ExecuteNow(new RunAutoStep()).Ok, Is.True); // forced stop at the shop
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Shop));
            // a big ATK perk, so the rounded HUD number surely changes
            int index = S.Run.Active.Shop.FindIndex(o => o.Kind == ShopOfferKind.Stat);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "the shop offers stat perks");
            S.Run.Active.Shop[index] = new ShopOffer { Kind = ShopOfferKind.Stat, Stat = new StatMod(StatId.Atk, 0, 50), Value = 50, Price = 10 };
            var screen = Open();
            double atkBefore = RunSystem.WormStats(C, S.Run.Active).Atk;
            Assert.That(ShopHudAtk(), Is.EqualTo(Fmt.Num(Math.Round(atkBefore))));
            var sheet = FindNamed(_f.App.ModalLayer, "Modal:Shop");

            BuyButton(index).onClick.Invoke();

            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Shop), "still shopping");
            Assert.That(FindNamed(_f.App.ModalLayer, "Modal:Shop"), Is.SameAs(sheet), "the same sheet");
            double atkAfter = RunSystem.WormStats(C, S.Run.Active).Atk;
            Assert.That(Math.Round(atkAfter), Is.GreaterThan(Math.Round(atkBefore)));
            Assert.That(ShopHudAtk(), Is.EqualTo(Fmt.Num(Math.Round(atkAfter))), "the shop's HUD shows the new ATK at once");
            Assert.That(screen.HudText, Does.Contain("ATK " + Fmt.Num(Math.Round(atkAfter))), "the run HUD under the shop too");
            Assert.That(screen.HudText, Does.Not.Contain("ATK " + Fmt.Num(Math.Round(atkBefore))));
        }

        /// <summary>
        /// The shop's HUD is bound to the state, not to the buy button's callback: any state change while the sheet is open
        /// (a purchase sent from elsewhere, an online state refresh) shows at once, without leaving the shop.
        /// </summary>
        [Test]
        public void OpenShop_FollowsEveryStateChange_InPlace()
        {
            StartRun(2);
            Assert.That(_f.Service.ExecuteNow(new RunAutoStep()).Ok, Is.True);
            int index = S.Run.Active.Shop.FindIndex(o => o.Kind == ShopOfferKind.Stat);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            S.Run.Active.Shop[index] = new ShopOffer { Kind = ShopOfferKind.Stat, Stat = new StatMod(StatId.Atk, 0, 50), Value = 50, Price = 10 };
            Open();
            var sheet = FindNamed(_f.App.ModalLayer, "Modal:Shop");
            long coins = S.Run.Active.Coins;

            // the purchase arrives as a state change only (no callback into the sheet)
            _f.App.Session.Run(new RunShopBuy { Index = index });
            Assert.That(S.Run.Active.ShopBuys, Is.EqualTo(1));
            Assert.That(FindNamed(_f.App.ModalLayer, "Modal:Shop"), Is.SameAs(sheet), "the same sheet");
            Assert.That(ShopHudAtk(), Is.EqualTo(Fmt.Num(Math.Round(RunSystem.WormStats(C, S.Run.Active).Atk))));
            Assert.That(FindNamed(sheet, "Coins").GetComponent<Text>().text, Is.EqualTo(Fmt.Num(coins - 10)));
            Assert.That(BuyButton(index).interactable, Is.False, "sold");

            // an online refresh that changed the run's stats
            S.Run.Active.RunStats.Add(new StatMod(StatId.Atk, 0, 100));
            _f.App.Session.NotifyChanged();
            Assert.That(ShopHudAtk(), Is.EqualTo(Fmt.Num(Math.Round(RunSystem.WormStats(C, S.Run.Active).Atk))));
            Assert.That(FindNamed(_f.App.ModalLayer, "Modal:Shop"), Is.SameAs(sheet));
        }

        [Test]
        public void ShopRerollPrice_StartsAtTheBasePrice_OnEveryNewShop()
        {
            StartRun(2, 3);
            Assert.That(_f.Service.ExecuteNow(new RunAutoStep()).Ok, Is.True);
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Shop));
            long basePrice = RunSystem.ShopRerollCost(C, S.Run.Active);
            Open();
            var reroll = RerollButton();
            Assert.That(reroll.name, Is.EqualTo("Button:Reroll (" + basePrice + ")"));
            reroll.onClick.Invoke();
            reroll.onClick.Invoke();
            long raised = RunSystem.ShopRerollCost(C, S.Run.Active);
            Assert.That(raised, Is.GreaterThan(basePrice));
            Assert.That(RerollButton().name, Is.EqualTo("Button:Reroll (" + raised + ")"), "the price rises within a visit");

            var leave = FindNamed(_f.App.ModalLayer, "Button:Leave").GetComponent<Button>();
            leave.onClick.Invoke();
            Assert.That(_f.Service.ExecuteNow(new RunAutoStep()).Ok, Is.True); // the next shop tile
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Shop));
            Assert.That(S.Run.Active.ShopsVisited, Is.EqualTo(2));
            ((RunScreen)_f.App.Router.Current).Rebuild();
            var next = RerollButton();
            Assert.That(next, Is.Not.Null, "the new shop is open");
            Assert.That(next.name, Is.EqualTo("Button:Reroll (" + basePrice + ")"), "a new shop: the base price again");
            Assert.That(next.transform.Find("RerollPrice").GetComponent<Text>().text, Is.EqualTo(basePrice.ToString()));
        }
    }
}
