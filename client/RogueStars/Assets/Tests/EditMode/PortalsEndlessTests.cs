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
    /// <summary>Endless campaign UI: the Portals screen, Dark Side and hub show bounded windows around the frontier.</summary>
    public sealed class PortalsEndlessTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private static Button FindButton(Transform root, string name)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(true)) if (b.name == name) return b;
            return null;
        }

        private static int CountButtons(Transform root, string prefix)
        {
            int n = 0;
            foreach (var b in root.GetComponentsInChildren<Button>(true)) if (b.name.StartsWith(prefix)) n++;
            return n;
        }

        private static string ButtonText(Button b) => b.GetComponentInChildren<Text>(true).text;

        /// <summary>A save that has cleared every main level up to <paramref name="main"/> and Dark Side level <paramref name="dark"/>.</summary>
        private void ClearedThrough(int main, int dark)
        {
            S.Counters[Requirements.MainLevelCounter] = main;
            S.Run.MainArchive = new ProgressArchive { Full = main };
            S.Counters[RunSystem.DarkSideMaxCounter] = dark;
            S.Run.DarkArchive = new ProgressArchive { Full = dark };
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
        }

        private UiScreen OpenPortals()
        {
            _f.App.Open("portals");
            return _f.App.Router.Current;
        }

        [Test]
        public void MainWindow_IsBoundedAroundTheFrontier()
        {
            Assert.That(PortalsScreen.MainWindow(0, -1), Is.EqualTo((0, 3)));
            Assert.That(PortalsScreen.MainWindow(1, -1), Is.EqualTo((0, 4)));
            Assert.That(PortalsScreen.MainWindow(123, -1), Is.EqualTo((121, 5)));
            Assert.That(PortalsScreen.MainWindow(123, 116), Is.EqualTo((116, 5)));
            Assert.That(PortalsScreen.MainWindow(123, 0), Is.EqualTo((0, 5)));
            Assert.That(PortalsScreen.MainWindow(123, 999999), Is.EqualTo((121, 5)), "never past frontier + ahead");
            Assert.That(PortalsScreen.MainWindow(99999, -1), Is.EqualTo((99997, 5)));
            Assert.That(PortalsScreen.DarkSidePage(1, -1), Is.EqualTo(0));
            Assert.That(PortalsScreen.DarkSidePage(1001, -1), Is.EqualTo(40));
            Assert.That(PortalsScreen.DarkSidePage(1001, int.MaxValue), Is.EqualTo(41));
            Assert.That(PortalsScreen.MilestoneMarker(C, 9), Is.Null);
            Assert.That(PortalsScreen.MilestoneMarker(C, 10), Is.EqualTo("CHEST"));
            Assert.That(PortalsScreen.MilestoneMarker(C, 50), Is.EqualTo("BONUS"));
            Assert.That(PortalsScreen.MilestoneMarker(C, 1240), Is.EqualTo("CHEST"));
            Assert.That(PortalsScreen.MilestoneMarker(C, 1250), Is.EqualTo("BONUS"));
        }

        [Test]
        public void FreshSave_ShowsTheFirstPortals_AndOneUpcomingPageLimit()
        {
            var screen = OpenPortals();
            Assert.That(CountButtons(screen.Root, "Level:"), Is.EqualTo(3 * C.Campaign.LevelsPerPortal));
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1)).interactable, Is.True);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(2)).interactable, Is.False);
            Assert.That(FindButton(screen.Root, "MainPrev").interactable, Is.False);
            Assert.That(FindButton(screen.Root, "MainNext").interactable, Is.False);
            Assert.That(ButtonText(FindButton(screen.Root, "Level:" + C.MainLevelId(10))), Does.Contain("CHEST"));
        }

        [Test]
        public void PastLevel500_FrontierPortalIsGeneratedAndPlayable()
        {
            ClearedThrough(500, 0);
            var screen = OpenPortals();
            var next = RunSystem.NextMainLevel(S, C);
            Assert.That(next.Number, Is.EqualTo(501));
            Assert.That(FindButton(screen.Root, "Level:" + next.Id).interactable, Is.True);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(500)), Is.Not.Null, "last named portal shown as cleared");
            Assert.That(ButtonText(FindButton(screen.Root, "Level:" + C.MainLevelId(500))), Does.Contain("BONUS ✓"));
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(530)), Is.Not.Null);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(531)), Is.Null);
        }

        [Test]
        public void PastLevel1000_PagesBackToClearedPortals_AndJumpsToTheFrontier()
        {
            ClearedThrough(1234, 0);
            var screen = OpenPortals();
            // Window: portals 121..125 = levels 1211..1260; the frontier is level 1235.
            Assert.That(CountButtons(screen.Root, "Level:"), Is.EqualTo(PortalsScreen.MainPageSize * C.Campaign.LevelsPerPortal));
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1210)), Is.Null);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1211)), Is.Not.Null);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1234)).interactable, Is.True, "cleared level replayable");
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1235)).interactable, Is.True, "frontier");
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1236)).interactable, Is.False);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1260)), Is.Not.Null);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1261)), Is.Null);
            Assert.That(ButtonText(FindButton(screen.Root, "Level:" + C.MainLevelId(1230))), Does.Contain("CHEST ✓"));
            Assert.That(ButtonText(FindButton(screen.Root, "Level:" + C.MainLevelId(1240))), Does.Contain("CHEST").And.Not.Contain("✓"));
            Assert.That(ButtonText(FindButton(screen.Root, "Level:" + C.MainLevelId(1250))), Does.Contain("BONUS"));
            Assert.That(FindButton(screen.Root, "MainNext").interactable, Is.False);
            Assert.That(FindButton(screen.Root, "MainFrontier"), Is.Null);

            FindButton(screen.Root, "MainPrev").onClick.Invoke();
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1161)), Is.Not.Null);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1210)), Is.Not.Null);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1235)), Is.Null);
            Assert.That(ButtonText(FindButton(screen.Root, "Level:" + C.MainLevelId(1200))), Does.Contain("BONUS ✓"));
            Assert.That(FindButton(screen.Root, "MainNext").interactable, Is.True);
            FindButton(screen.Root, "MainFrontier").onClick.Invoke();
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1235)), Is.Not.Null);

            // The level modal names the milestone chest of an unclaimed 10th level.
            FindButton(screen.Root, "Level:" + C.MainLevelId(1240)).onClick.Invoke();
            bool chest = false;
            foreach (var t in _f.App.ModalLayer.GetComponentsInChildren<Text>(true)) if (t.text.StartsWith("Milestone chest (first clear)")) chest = true;
            Assert.That(chest, Is.True);
        }

        [Test]
        public void DarkSide_PastLevel1000_PagesAroundTheFrontier()
        {
            ClearedThrough(1234, 1000);
            var screen = OpenPortals();
            FindButton(screen.Root, "Button:Dark Side").onClick.Invoke();
            Assert.That(CountButtons(screen.Root, "Level:dark_side_"), Is.EqualTo(PortalsScreen.DarkSidePageSize));
            Assert.That(FindButton(screen.Root, "Level:dark_side_1001").interactable, Is.True);
            Assert.That(FindButton(screen.Root, "Level:dark_side_1002").interactable, Is.False);
            Assert.That(FindButton(screen.Root, "Level:dark_side_1000"), Is.Null);
            FindButton(screen.Root, "DarkNext").onClick.Invoke();
            Assert.That(FindButton(screen.Root, "Level:dark_side_1050"), Is.Not.Null);
            Assert.That(FindButton(screen.Root, "DarkNext").interactable, Is.False, "one page of upcoming levels");
            FindButton(screen.Root, "DarkPrev").onClick.Invoke();
            FindButton(screen.Root, "DarkPrev").onClick.Invoke();
            Assert.That(FindButton(screen.Root, "Level:dark_side_976"), Is.Not.Null);
            Assert.That(ButtonText(FindButton(screen.Root, "Level:dark_side_1000")), Does.Contain("cleared"));
        }

        [Test]
        public void Hub_ShowsTheFrontierPortalAndTheNextLevel()
        {
            ClearedThrough(1234, 0);
            _f.App.Router.Current.Rebuild();
            var root = _f.App.Router.Current.Root;
            Text next = null, chip = null;
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                if (t.name == "HubNextLevel") next = t;
                if (t.name == "Text" && t.transform.parent.name == "PortalChip") chip = t;
            }
            Assert.That(next.text, Does.Contain("LEVEL 1235"));
            Assert.That(chip.text, Does.StartWith("Portal 124 · " + _f.Service.Content.MainPortal(123).Name));
            Assert.That(FindButton(root, "PlayButton"), Is.Not.Null);
        }
    }
}
