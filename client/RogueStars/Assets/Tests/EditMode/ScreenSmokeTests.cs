using System.Collections.Generic;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>Instantiates every screen (fresh player and a populated one) and pokes its main interactions.</summary>
    public sealed class ScreenSmokeTests
    {
        private AppFixture _f;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private static IEnumerable<string> ScreenIds()
        {
            foreach (var e in ScreenCatalog.All) yield return e.Id;
        }

        private void OpenEach()
        {
            foreach (var entry in ScreenCatalog.All)
            {
                var screen = entry.Create();
                _f.App.Router.Push(screen);
                Assert.That(screen.IsCreated, Is.True, entry.Id);
                Assert.That(screen.Root.GetComponentsInChildren<Text>(true).Length, Is.GreaterThan(1), entry.Id + " renders text");
                screen.Rebuild();
                screen.Tick();
                _f.App.Update(0.6f);
                Assert.That(_f.App.Router.Pop(), Is.True, entry.Id);
            }
        }

        [Test]
        public void EveryScreen_FreshPlayer() => OpenEach();

        [Test]
        public void EveryScreen_PopulatedPlayer()
        {
            Populate();
            OpenEach();
        }

        [Test]
        public void Catalog_HasEveryRequiredScreen()
        {
            var ids = new List<string>(ScreenIds());
            foreach (var id in new[] { "portals", "worm", "parts", "signals", "laboratory", "heroes", "mounts", "city", "aliens", "breeding", "observatory", "quests", "shop", "mail", "arena", "guild", "settings" })
                Assert.That(ids, Does.Contain(id));
            Assert.That(ScreenCatalog.All.Exists(e => e.Placeholder), Is.False, "no placeholder screens left");
        }

        [Test]
        public void TabsAndModals_DoNotThrow()
        {
            Populate();
            foreach (var id in new[] { "quests", "shop", "arena" })
            {
                _f.App.Open(id);
                var screen = _f.App.Router.Current;
                // The tab bar is the first Row directly under the scroll content; click each tab (each click rebuilds).
                for (int tab = 0; tab < 8; tab++)
                {
                    Button target = null;
                    foreach (var b in screen.Root.GetComponentsInChildren<Button>(true))
                    {
                        var row = b.transform.parent;
                        if (row.name == "Row" && row.parent.name == "Content" && row.GetSiblingIndex() == 0)
                        {
                            if (b.transform.GetSiblingIndex() == tab) { target = b; break; }
                        }
                    }
                    if (target == null) break;
                    target.onClick.Invoke();
                    screen.Tick();
                }
                _f.App.Router.Pop();
            }
            for (int i = _f.App.ModalLayer.childCount; i > 0; i--) _f.App.HandleBack();
        }

        [Test]
        public void CityScreen_SelectBuildingShowsDetails()
        {
            Populate();
            _f.App.Open("city");
            var city = _f.App.Router.Current;
            Button building = null;
            foreach (var b in city.Root.GetComponentsInChildren<Button>(true))
                if (b.name.StartsWith("Building:")) { building = b; break; }
            Assert.That(building, Is.Not.Null, "a placed building is drawn on the grid");
            building.onClick.Invoke();
            bool hasUpgrade = false;
            foreach (var t in city.Root.GetComponentsInChildren<Text>(true)) if (t.text.StartsWith("Upgrade") || t.text.StartsWith("Max level") || t.text.StartsWith("Construction")) hasUpgrade = true;
            Assert.That(hasUpgrade, Is.True);
        }

        [Test]
        public void GuildScreen_OfflineShowsRequiresOnline()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.App.Open("guild");
            bool found = false;
            foreach (var t in _f.App.Router.Current.Root.GetComponentsInChildren<Text>(true)) if (t.text.Contains("require online")) found = true;
            Assert.That(found, Is.True);
        }

        /// <summary>Starter kit, all features, one building placed and finished, one under construction, a breeding job.</summary>
        private void Populate()
        {
            var s = _f.Service;
            s.DevSetFlag(Features.DevUnlockAllFlag, true);
            s.DevGrantStarterKit();
            int placed = 0;
            foreach (var b in CitySystem.SortedBlueprints(s.State))
            {
                var def = s.Content.Blueprint(b.Id);
                bool done = false;
                for (int y = 0; y < s.Content.CityTuning.GridHeight && !done; y++)
                    for (int x = 0; x < s.Content.CityTuning.GridWidth && !done; x++)
                        if (CitySystem.PlacementError(s.State, s.Content, def, x, y) == null)
                            done = s.ExecuteNow(new CityBuild { BlueprintId = b.Id, X = x, Y = y }).Ok;
                if (done && ++placed == 3) _f.Clock.Advance(7L * 24 * 3600 * 1000); // first three finish
                if (placed >= 5) break;
            }
            s.Refresh(_ => { });
            var aliens = new List<string>(s.State.Aliens.Owned.Keys);
            if (aliens.Count >= 2) s.ExecuteNow(new BreedStart { ParentA = aliens[0], ParentB = aliens[1] });
            s.ExecuteNow(new LoginClaim());
            _f.App.Router.RebuildCurrent();
            Assert.That(placed, Is.GreaterThan(0));
        }
    }
}
