using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Owner playtest 0.1.325: every run starts with the signature pick (3 cards, from the first run on), the shop shows 3
    /// skills plus the support offers, a fork whose ways land on one tile is no choice, and the run HUD's heart no longer
    /// overlaps the level / EXP pill at phone widths.
    /// </summary>
    public sealed class Playtest325Tests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; RunFx.Instant = true; RunScreen.BoardSpeed = 1; }

        [TearDown]
        public void TearDown()
        {
            RunScreen.InstantOutcomes = false; RunFx.Instant = false; RunScreen.BoardSpeed = 1;
            _f.Dispose();
        }

        private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        [Test]
        public void FirstRun_StartsWithTheSignaturePick_OfThreeCards()
        {
            var level = C.MainLevel(1);
            Assert.That(Features.IsUnlocked(S, C, Features.RunSignatures), Is.False, "a brand-new player");
            var offer = RunSystem.SignatureOffer(_f.Service.Engine, S, _f.Service.NowMs);
            Assert.That(offer.Count, Is.EqualTo(3));
            bool started = false;
            SignaturePick.StartRun(_f.App, new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }, r => started = r.Ok);
            Assert.That(started, Is.False, "the run waits for the pick");
            var cards = _f.App.ModalLayer.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Button:Signature:")).ToList();
            Assert.That(cards.Select(b => b.name.Substring("Button:Signature:".Length)), Is.EquivalentTo(offer));
            cards[2].onClick.Invoke();
            Assert.That(started, Is.True);
            Assert.That(RunSystem.Signatures(S.Run.Active), Is.EqualTo(new[] { offer[2] }));
        }

        [Test]
        public void MinibossMarker_ItsPedestalIsCentredOnTheTile()
        {
            var art = IsoArt.Marker("miniboss");
            Assert.That(art, Is.Not.Null);
            // the import pivot is the slab's front footprint; the slab's middle goes on the tile centre (~17.5% up the image)
            float pivot01 = art.pivot.y / art.rect.height;
            Assert.That(pivot01 + IsoArt.PedestalDrop("miniboss", art), Is.EqualTo(0.175f).Within(0.02f));
            Assert.That(IsoArt.PedestalDrop("enemy", IsoArt.Marker("enemy")), Is.EqualTo(0f), "no pedestal, no shift");
        }

        [Test]
        public void Hud_HeartClearsTheLevelPill_AtPhoneWidths()
        {
            foreach (float width in new[] { 1080f, 720f })
            {
                var go = new GameObject("HudHost", typeof(RectTransform));
                try
                {
                    var host = (RectTransform)go.transform;
                    host.sizeDelta = new Vector2(width, width * 2340f / 1080f);
                    var hud = SliceHud.Build(host, width, () => { }, null, true);
                    var pill = (RectTransform)hud.Root.Find("RunLevel");
                    var heart = (RectTransform)hud.Root.Find("HeartIcon");
                    float pillRight = pill.anchoredPosition.x + pill.sizeDelta.x / 2;
                    float heartLeft = heart.anchoredPosition.x - heart.sizeDelta.x / 2;
                    Assert.That(heartLeft, Is.GreaterThan(pillRight), width + " px: the heart starts right of the level pill");
                    var lv = hud.Level;
                    Assert.That(lv.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Wrap), "the level text stays inside its pill");
                    Assert.That(lv.resizeTextForBestFit, Is.True);
                }
                finally { Object.DestroyImmediate(go); }
            }
        }
    }
}
