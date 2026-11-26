using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>Playtest fix: stamina at 0 regenerates, the HUD chip and its countdown follow it, and a misbehaving screen cannot freeze them.</summary>
    public sealed class StaminaRegenHudTests
    {
        private AppFixture _f;
        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private string Text(string name) => _f.Canvas.GetComponentsInChildren<Text>(true).First(t => t.name == name).text;
        private string Chip => Text("Chip:" + Currencies.Stamina);
        private string Timer => Text("Timer:" + Currencies.Stamina);

        private void SpendAll()
        {
            var w = _f.Service.State.Wallet;
            Assert.That(w.TrySpend(Currencies.Stamina, w.Get(Currencies.Stamina)), Is.True);
            _f.App.Bar.Rebuild();
        }

        [Test]
        public void StaminaAtZero_RegeneratesAfterTheInterval_AndTheHudShowsItWithACorrectCountdown()
        {
            _f.Clock.Advance(3_600_000);
            _f.Service.Refresh(_ => { });
            SpendAll();
            Assert.That(Chip, Does.StartWith("0/"));
            Assert.That(Timer, Is.EqualTo("6m 00s"), "a full interval to the first point");
            _f.Clock.Advance(3 * 60_000);
            _f.App.Update(0.6f);
            Assert.That(Timer, Is.EqualTo("3m 00s"), "the countdown runs");
            _f.Clock.Advance(3 * 60_000 + 1000);
            for (int i = 0; i < 12; i++) _f.App.Update(0.6f); // the app refreshes itself every few seconds
            Assert.That(Chip, Does.StartWith("1/"), "the counter increased");
            Assert.That(Timer, Is.EqualTo("5m 59s"), "and the countdown restarted for the next point");
        }

        [Test]
        public void AScreenThatThrowsEveryFrame_DoesNotFreezeTheStaminaClock()
        {
            _f.Clock.Advance(3_600_000);
            _f.Service.Refresh(_ => { });
            SpendAll();
            var screen = new ThrowingScreen();
            _f.App.Router.Reset(screen);
            screen.Armed = true; // from now on every Frame and Tick throws
            LogAssert.ignoreFailingMessages = true;
            try
            {
                _f.Clock.Advance(6 * 60_000 + 1000);
                for (int i = 0; i < 12; i++) _f.App.Update(0.6f);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
            Assert.That(_f.Service.State.Wallet.Get(Currencies.Stamina), Is.EqualTo(1));
        }

        private sealed class ThrowingScreen : UiScreen
        {
            public override string Title => "Throws";
            protected override void Build(RectTransform body) { }
            public bool Armed;
            public override void Frame(float dt) { if (Armed) throw new System.InvalidOperationException("screen bug"); }
            public override void Tick() { if (Armed) throw new System.InvalidOperationException("screen bug"); }
        }
    }
}
