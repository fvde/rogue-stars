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

        /// <summary>Every text of a button (map nodes: number, stars, data, milestone chip).</summary>
        private static string ButtonText(Button b)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var t in b.GetComponentsInChildren<Text>(true)) if (t.gameObject.activeSelf) parts.Add(t.text);
            return string.Join(" | ", parts);
        }

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
            // (first level, count): up to 20 cleared portals behind the next level, 9 upcoming ones, pages of 30
            Assert.That(PortalsScreen.MainWindow(1, -1), Is.EqualTo((1, 10)));
            Assert.That(PortalsScreen.MainWindow(2, -1), Is.EqualTo((1, 11)));
            Assert.That(PortalsScreen.MainWindow(1235, -1), Is.EqualTo((1215, 30)));
            Assert.That(PortalsScreen.MainWindow(1235, 1185), Is.EqualTo((1185, 30)));
            Assert.That(PortalsScreen.MainWindow(1235, 1), Is.EqualTo((1, 30)));
            Assert.That(PortalsScreen.MainWindow(1235, 999999), Is.EqualTo((1215, 30)), "never past next + ahead");
            Assert.That(PortalsScreen.MainWindow(99999, -1), Is.EqualTo((99979, 30)));
            Assert.That(PortalsScreen.DarkSidePage(1, -1), Is.EqualTo(0));
            Assert.That(PortalsScreen.DarkSidePage(1001, -1), Is.EqualTo(40));
            Assert.That(PortalsScreen.DarkSidePage(1001, int.MaxValue), Is.EqualTo(41));
            Assert.That(PortalsScreen.MilestoneMarker(C, 9), Is.Null);
            Assert.That(PortalsScreen.MilestoneMarker(C, 10), Is.EqualTo("CHEST"));
            Assert.That(PortalsScreen.MilestoneMarker(C, 50), Is.EqualTo("BONUS"));
            Assert.That(PortalsScreen.MilestoneMarker(C, 1240), Is.EqualTo("CHEST"));
            Assert.That(PortalsScreen.MilestoneMarker(C, 1250), Is.EqualTo("BONUS"));
            Assert.That(PortalsScreen.LevelTitle(C.MainLevel(55)), Is.EqualTo("55. " + C.CampaignBiome(55).Name));
        }

        [Test]
        public void FreshSave_ShowsTheFirstPortals_OneUpcomingPage()
        {
            var screen = OpenPortals();
            Assert.That(CountButtons(screen.Root, "Level:"), Is.EqualTo(1 + PortalsScreen.MainLevelsAhead), "one portal per level");
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1)).interactable, Is.True);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(2)).interactable, Is.False);
            Assert.That(FindButton(screen.Root, "MainPrev").interactable, Is.False);
            Assert.That(FindButton(screen.Root, "MainNext").interactable, Is.False);
            Assert.That(ButtonText(FindButton(screen.Root, "Level:" + C.MainLevelId(10))), Does.Contain("CHEST").And.Not.Contain("BOSS"));
            Assert.That(ButtonText(FindButton(screen.Root, "Level:" + C.MainLevelId(2))), Does.Contain(C.CampaignBiome(2).Name));
        }

        [Test]
        public void PastLevel500_FrontierPortalIsGeneratedAndPlayable()
        {
            ClearedThrough(500, 0);
            var screen = OpenPortals();
            var next = RunSystem.NextMainLevel(S, C);
            Assert.That(next.Number, Is.EqualTo(501));
            Assert.That(FindButton(screen.Root, "Level:" + next.Id).interactable, Is.True);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(500)), Is.Not.Null, "the last materialized level shown as cleared");
            Assert.That(ButtonText(FindButton(screen.Root, "Level:" + C.MainLevelId(500))), Does.Contain("BONUS ✓"));
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(510)), Is.Not.Null);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(511)), Is.Null);
        }

        [Test]
        public void PastLevel1000_PagesBackToClearedPortals_AndJumpsToTheFrontier()
        {
            ClearedThrough(1234, 0);
            var screen = OpenPortals();
            // Window: levels 1215..1244; the next level is 1235.
            Assert.That(CountButtons(screen.Root, "Level:"), Is.EqualTo(PortalsScreen.MainPageSize));
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1214)), Is.Null);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1215)), Is.Not.Null);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1234)).interactable, Is.True, "cleared level replayable");
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1235)).interactable, Is.True, "frontier");
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1236)).interactable, Is.False);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1244)), Is.Not.Null);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1245)), Is.Null);
            Assert.That(ButtonText(FindButton(screen.Root, "Level:" + C.MainLevelId(1230))), Does.Contain("CHEST ✓"));
            Assert.That(ButtonText(FindButton(screen.Root, "Level:" + C.MainLevelId(1240))), Does.Contain("CHEST").And.Not.Contain("✓"));
            Assert.That(FindButton(screen.Root, "MainNext").interactable, Is.False);
            Assert.That(FindButton(screen.Root, "MainFrontier"), Is.Null);

            FindButton(screen.Root, "MainPrev").onClick.Invoke();
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1185)), Is.Not.Null);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1214)), Is.Not.Null);
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1235)), Is.Null);
            Assert.That(ButtonText(FindButton(screen.Root, "Level:" + C.MainLevelId(1200))), Does.Contain("BONUS ✓"));
            Assert.That(FindButton(screen.Root, "MainNext").interactable, Is.True);
            FindButton(screen.Root, "MainFrontier").onClick.Invoke();
            Assert.That(FindButton(screen.Root, "Level:" + C.MainLevelId(1235)), Is.Not.Null);

            // Tapping a node selects it for the bottom card; its details modal names the milestone chest of an unclaimed 10th level.
            FindButton(screen.Root, "Level:" + C.MainLevelId(1240)).onClick.Invoke();
            Assert.That(((PortalsScreen)screen).SelectedLevel.Id, Is.EqualTo(C.MainLevelId(1240)));
            Assert.That(FindButton(screen.Root, "StartRun").interactable, Is.False, "locked level cannot be started");
            FindButton(screen.Root, "LevelDetails").onClick.Invoke();
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
            Assert.That(chip.text, Does.StartWith("Portal 1235 · " + C.MainLevel(1235).Name), "every level is its own portal");
            Assert.That(FindButton(root, "PlayButton"), Is.Not.Null);
        }
    }
}
