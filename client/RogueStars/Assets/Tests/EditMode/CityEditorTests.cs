using System.Collections.Generic;
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
    /// The City screen's editor and life (City design §2, §7, §8): the ghost mirrors CityLayout.Validate (and the server handler), the
    /// undo stack (reverse commands, 50 steps, a new build refunded), a street stroke = one command, the autotile, the day / night
    /// cycle, walker social-event rules, cached routes, the sprite budget, the pop reveal and the squad around the worm (C4).
    /// </summary>
    public sealed class CityEditorTests
    {
        private AppFixture _f;
        private const string Farm = "bp_biomass_farm", Mine = "bp_ore_mine", Pen = "bp_breeding_pen", Spire = "bp_command_spire";

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() { _f.Dispose(); CityArt.ClearCache(); }

        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;
        private int Cell(int x, int y) => CityLayout.Cell(C, x, y);

        private void Grant(params string[] blueprints)
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            var list = new List<Reward> { Reward.Cur(Currencies.Biomass, 5_000_000), Reward.Cur(Currencies.Ore, 5_000_000), Reward.Cur(Currencies.Diamonds, 50_000) };
            foreach (var b in blueprints) list.Add(new Reward(RewardKinds.Blueprint, b, 1));
            _f.Service.DevGrant(list);
        }

        /// <summary>Opens the City as on a phone (the test canvas is landscape, which would read as a tablet).</summary>
        private CityScreen OpenCity()
        {
            _f.App.Open("city");
            var city = _f.App.Router.Current as CityScreen;
            Assert.That(city, Is.Not.Null);
            city.ForceTablet = false;
            city.Rebuild();
            return city;
        }

        private static T Named<T>(Transform root, string name) where T : Component =>
            root.GetComponentsInChildren<T>(true).FirstOrDefault(c => c.name == name);

        // ---- the ghost ------------------------------------------------------------------------------------------------------

        [Test]
        public void Ghost_MirrorsValidate_EveryCell()
        {
            Grant(Farm, "bp_fuel_refinery", Spire);
            _f.Service.ExecuteNow(new CityStreets { Adds = new List<int> { Cell(7, 9), Cell(6, 9) } });
            foreach (var bp in new[] { Farm, "bp_fuel_refinery", Spire })
                foreach (bool rot in new[] { false, true })
                    for (int y = -1; y <= C.CityTuning.GridHeight; y++)
                        for (int x = -1; x <= C.CityTuning.GridWidth; x++)
                        {
                            var g = CityGhost.ForPlace(S, C, bp, null, x, y, rot);
                            var check = CityLayout.Validate(S, C, CityEdit.Place(bp, x, y, rot));
                            Assert.That(g.Ok, Is.EqualTo(check.Ok), bp + " at " + x + "," + y);
                            if (!g.Ok) Assert.That(g.Chip, Is.EqualTo(CityGhost.Reason(check.Error)));
                        }
        }

        [Test]
        public void Ghost_AgreesWithTheServerHandler()
        {
            Grant(Farm);
            int tried = 0, placed = 0;
            for (int y = 5; y <= 13; y++)
                for (int x = 5; x <= 13; x++)
                {
                    var g = CityGhost.ForPlace(S, C, Farm, null, x, y, false);
                    var r = _f.Service.ExecuteNow(g.Command);
                    Assert.That(r.Ok, Is.EqualTo(g.Ok), "farm at " + x + "," + y + ": " + r.Error);
                    tried++;
                    if (r.Ok) { placed++; Assert.That(_f.Service.ExecuteNow(new CityStore { Uid = (string)r.Payload }).Ok, Is.True); }
                }
            Assert.That(placed, Is.GreaterThan(4));
            Assert.That(tried, Is.EqualTo(81));
        }

        [Test]
        public void Ghost_ChipSaysReasonOrBonus()
        {
            Grant(Farm);
            Assert.That(CityGhost.ForPlace(S, C, Farm, null, 1, 1, false).Chip, Is.EqualTo("Not on dug land"));
            Assert.That(CityGhost.ForPlace(S, C, Farm, null, 9, 9, false).Chip, Is.EqualTo("That is the crash site"));
            Assert.That(CityGhost.ForPlace(S, C, Farm, null, 6, 10, false).Chip, Is.EqualTo("Must touch a street"));
            // two farms beside the plaza, the third touching both: Fields +20%
            Assert.That(_f.Service.ExecuteNow(new CityPlace { BlueprintId = Farm, X = 7, Y = 9 }).Ok, Is.True);
            Assert.That(_f.Service.ExecuteNow(new CityPlace { BlueprintId = Farm, X = 7, Y = 10 }).Ok, Is.True);
            var g = CityGhost.ForPlace(S, C, Farm, null, 7, 8, false);
            Assert.That(g.Ok, Is.True, g.Chip);
            Assert.That(g.Chip, Is.EqualTo("+10% output · Fields"));
            Assert.That(CityGhost.ForPlace(S, C, Farm, null, 11, 9, false).Chip, Is.EqualTo("No bonus here · Fields"));
        }

        [Test]
        public void Ghost_RotateSwapsTheFootprint()
        {
            Grant("bp_fuel_refinery");
            var g = CityGhost.ForPlace(S, C, "bp_fuel_refinery", null, 11, 8, false);
            Assert.That(g.W, Is.EqualTo(2)); Assert.That(g.H, Is.EqualTo(1));
            Assert.That(g.Rotate(S, C), Is.True);
            Assert.That(g.W, Is.EqualTo(1)); Assert.That(g.H, Is.EqualTo(2));
            Assert.That(CityGhost.ForPlace(S, C, Farm, null, 7, 9, false).Rotate(S, C), Is.False, "square footprints don't rotate");
        }

        // ---- undo ---------------------------------------------------------------------------------------------------------

        [Test]
        public void Undo_KeepsFiftySteps()
        {
            var u = new CityUndo();
            for (int i = 0; i < 60; i++) u.Push(new CityStreets { Adds = new List<int> { i } });
            Assert.That(u.Count, Is.EqualTo(CityUndo.Max));
            Assert.That(((CityStreets)u.Pop()).Adds[0], Is.EqualTo(59));
            Assert.That(((CityStreets)u.Peek()).Adds[0], Is.EqualTo(58));
        }

        [Test]
        public void Undo_ReverseCommands()
        {
            Grant(Farm);
            var st = new CityStreets { Adds = new List<int> { 1, 2 }, Removes = new List<int> { 3 } };
            var rs = (CityStreets)CityUndo.Reverse(S, st);
            CollectionAssert.AreEqual(new[] { 3 }, rs.Adds);
            CollectionAssert.AreEqual(new[] { 1, 2 }, rs.Removes);
            var uid = (string)_f.Service.ExecuteNow(new CityPlace { BlueprintId = Farm, X = 7, Y = 9 }).Payload;
            _f.Clock.Advance(3_600_000);
            _f.Service.Refresh(_ => { });
            var mv = (CityMove)CityUndo.Reverse(S, new CityMove { Uid = uid, X = 11, Y = 9 });
            Assert.That(new[] { mv.X, mv.Y }, Is.EqualTo(new[] { 7, 9 }));
            var back = (CityPlace)CityUndo.Reverse(S, new CityStore { Uid = uid });
            Assert.That(back.Uid, Is.EqualTo(uid), "a built copy comes back from the tray");
            Assert.That(CityUndo.Reverse(S, new CityPlace { BlueprintId = Farm, X = 1, Y = 1 }), Is.InstanceOf<CityStore>());
        }

        [Test]
        public void Undo_NewBuildIsRefunded_MoveGoesBack()
        {
            Grant(Farm);
            var city = OpenCity();
            city.EnterEdit();
            long bio = S.Wallet.Get(Currencies.Biomass);
            city.StartPlace(Farm, null);
            city.GhostAt(7, 10);
            Assert.That(city.Ghost.Ok, Is.True, city.GhostChip);
            city.DropGhost();
            Assert.That(CitySystem.CopiesBuilt(S, Farm), Is.EqualTo(1));
            Assert.That(S.Wallet.Get(Currencies.Biomass), Is.LessThan(bio));
            Assert.That(city.Undo.Count, Is.EqualTo(1));
            city.UndoLast();
            Assert.That(CitySystem.CopiesBuilt(S, Farm), Is.EqualTo(0), "undoing a new build cancels it");
            Assert.That(S.Wallet.Get(Currencies.Biomass), Is.EqualTo(bio), "and refunds it");

            // build again, finish it, move it, undo the move
            city.StartPlace(Farm, null);
            city.GhostAt(7, 10);
            city.DropGhost();
            _f.Clock.Advance(3_600_000);
            _f.Service.Refresh(_ => { });
            var uid = CitySystem.Copies(S, Farm)[0].Uid;
            city.StartMove(uid);
            city.GhostAt(11, 9);
            Assert.That(city.Ghost.Ok, Is.True, city.GhostChip);
            city.DropGhost();
            Assert.That(CitySystem.Building(S, uid).X, Is.EqualTo(11));
            city.UndoLast();
            Assert.That(CitySystem.Building(S, uid).X, Is.EqualTo(7));
            Assert.That(CitySystem.Building(S, uid).Y, Is.EqualTo(10));
            city.ExitEdit();
            Assert.That(city.Undo.Count, Is.EqualTo(0), "undo ends with edit mode");
        }

        [Test]
        public void Ghost_InvalidDropWaits_ThenRotateAndPlace()
        {
            Grant("bp_fuel_refinery");
            var city = OpenCity();
            city.StartPlace("bp_fuel_refinery", null);
            city.GhostAt(1, 1);
            Assert.That(city.DropGhost(), Is.False);
            Assert.That(city.Ghost, Is.Not.Null, "an invalid drop leaves the ghost where it is");
            Assert.That(Named<RectTransform>(city.Root, "GhostChip"), Is.Not.Null);
            Assert.That(CitySystem.CopiesBuilt(S, "bp_fuel_refinery"), Is.EqualTo(0));
            city.GhostAt(11, 8);
            city.RotateGhost();
            if (!city.Ghost.Ok) city.RotateGhost();
            Assert.That(city.Ghost.Ok, Is.True, city.GhostChip);
            city.DropGhost();
            Assert.That(CitySystem.CopiesBuilt(S, "bp_fuel_refinery"), Is.EqualTo(1));
            Assert.That(city.Ghost, Is.Null);
        }

        // ---- streets ------------------------------------------------------------------------------------------------------

        [Test]
        public void StreetStroke_IsOneCommand()
        {
            Grant(Farm);
            var city = OpenCity();
            city.SetTool(CityTool.Street);
            int events = 0;
            _f.App.Session.EventRaised += e => { if (e.Type == "city_streets") events++; };
            city.StrokeBegin(Cell(8, 9)); // the plaza: an anchor
            city.StrokeTo(Cell(7, 9));
            city.StrokeTo(Cell(6, 9));
            city.StrokeTo(Cell(6, 8));
            CollectionAssert.AreEqual(new[] { Cell(7, 9), Cell(6, 9), Cell(6, 8) }, city.Stroke.Cells);
            city.StrokeEnd();
            Assert.That(events, Is.EqualTo(1), "one stroke = one CityStreets");
            CollectionAssert.AreEquivalent(new[] { Cell(7, 9), Cell(6, 9), Cell(6, 8) }, S.City.Streets);
            Assert.That(city.Undo.Count, Is.EqualTo(1));
            city.UndoLast();
            Assert.That(S.City.Streets, Is.Empty, "undo removes the whole stroke");
        }

        [Test]
        public void StreetStroke_GrowsOutOfTheNetwork_FillsSkips_BacksUp()
        {
            Grant(Farm);
            var refused = new StreetStroke(S, C, false);
            Assert.That(refused.Begin(Cell(6, 10)), Is.False);
            Assert.That(refused.Refused, Is.EqualTo("Streets must start from a street or the fireplace"));

            var s = new StreetStroke(S, C, false);
            Assert.That(s.Begin(Cell(8, 9)), Is.True);
            s.Extend(Cell(6, 9)); // skipped (7, 9): filled with a straight step
            CollectionAssert.AreEqual(new[] { Cell(7, 9), Cell(6, 9) }, s.Cells);
            s.Extend(Cell(7, 9)); // back onto the previous tile: the last one is taken back
            CollectionAssert.AreEqual(new[] { Cell(7, 9) }, s.Cells);
            s.Extend(Cell(7, 8));
            s.Extend(Cell(7, 7));
            s.Extend(Cell(7, 6)); // rubble: refused, the stroke keeps what it had
            Assert.That(s.Refused, Is.EqualTo("Not on dug land"));
            CollectionAssert.AreEqual(new[] { Cell(7, 9), Cell(7, 8), Cell(7, 7) }, s.Cells);
            Assert.That(s.Check().Ok, Is.True);
        }

        [Test]
        public void StreetErase_ThatCutsOff_IsRefusedAndFlashes()
        {
            Grant(Farm);
            _f.Service.ExecuteNow(new CityStreets { Adds = new List<int> { Cell(7, 9), Cell(6, 9) } });
            var uid = (string)_f.Service.ExecuteNow(new CityPlace { BlueprintId = Farm, X = 6, Y = 10 }).Payload;
            Assert.That(uid, Is.Not.Null);
            var s = new StreetStroke(S, C, true);
            s.Begin(Cell(7, 9));
            Assert.That(s.Cells, Is.Empty);
            Assert.That(s.Refused, Does.StartWith("That would cut off"));
            Assert.That(s.FlashUids.Concat(s.FlashCells.Select(c => c.ToString())), Is.Not.Empty);
            var end = new StreetStroke(S, C, true);
            end.Begin(Cell(6, 9));
            Assert.That(end.Refused, Is.EqualTo("That would cut off a building"));
            CollectionAssert.Contains(end.FlashUids, uid);
        }

        [Test]
        public void StreetAutotile_SixteenVariants()
        {
            var seen = new HashSet<int>();
            for (int m = 0; m < 16; m++)
            {
                int mask = StreetTiles.Mask((x, y) => (x == 1 && y == 0 && (m & 1) != 0) || (x == 0 && y == 1 && (m & 2) != 0) || (x == -1 && y == 0 && (m & 4) != 0) || (x == 0 && y == -1 && (m & 8) != 0), 0, 0);
                Assert.That(mask, Is.EqualTo(m));
                seen.Add(mask);
            }
            Assert.That(seen.Count, Is.EqualTo(16));
            Assert.That(StreetTiles.Variant(0), Is.EqualTo("dot"));
            Assert.That(StreetTiles.Variant(1), Is.EqualTo("end"));
            Assert.That(StreetTiles.Variant(5), Is.EqualTo("straight"));
            Assert.That(StreetTiles.Variant(3), Is.EqualTo("corner"));
            Assert.That(StreetTiles.Variant(7), Is.EqualTo("t"));
            Assert.That(StreetTiles.Variant(15), Is.EqualTo("cross"));
        }

        // ---- life ---------------------------------------------------------------------------------------------------------

        [Test]
        public void DayNight_TwentyMinuteCycle()
        {
            long m = CityDayNight.Minute, start = 40 * CityDayNight.CycleMs;
            Assert.That(CityDayNight.Phase(start), Is.EqualTo("day"));
            Assert.That(CityDayNight.Phase(start + 11 * m), Is.EqualTo("day"));
            Assert.That(CityDayNight.Phase(start + 12 * m + 1), Is.EqualTo("dusk"));
            Assert.That(CityDayNight.Phase(start + 14 * m + 1), Is.EqualTo("night"));
            Assert.That(CityDayNight.Phase(start + 19 * m + 1), Is.EqualTo("dawn"));
            Assert.That(CityDayNight.Phase(start + 20 * m), Is.EqualTo("day"));
            Assert.That(CityDayNight.Darkness(start + 5 * m), Is.EqualTo(0));
            Assert.That(CityDayNight.Darkness(start + 13 * m), Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(CityDayNight.Darkness(start + 16 * m), Is.EqualTo(1));
            Assert.That(CityDayNight.Phase(CityDayNight.MiddleOf("night", start + 3 * m)), Is.EqualTo("night"));
        }

        [Test]
        public void Walkers_SocialEvents_FollowTheRules()
        {
            var sim = new WalkerSim(3) { Roll = () => 0.1 };
            var a = sim.Add(new Walker { AlienId = "a", Mode = WalkerMode.Sit, State = WalkerState.Waiting, Timer = 999, Pos = new Vector2(0, 0) });
            var b = sim.Add(new Walker { AlienId = "b", Mode = WalkerMode.Sit, State = WalkerState.Waiting, Timer = 999, Pos = new Vector2(0.3f, 0) });
            var c = sim.Add(new Walker { AlienId = "c", Mode = WalkerMode.Sit, State = WalkerState.Waiting, Timer = 999, Pos = new Vector2(5, 5) });
            var d = sim.Add(new Walker { AlienId = "d", Mode = WalkerMode.Sit, State = WalkerState.Waiting, Timer = 999, Pos = new Vector2(5.2f, 5) });
            int events = 0;
            sim.Social += (x, y, k) => events++;
            sim.Step(0.3f);
            Assert.That(events, Is.EqualTo(1), "one event per check");
            Assert.That(a.State, Is.EqualTo(WalkerState.Social));
            Assert.That(b.State, Is.EqualTo(WalkerState.Social));
            Assert.That(a.Timer, Is.EqualTo(sim.StopTime).Within(0.01f));
            for (int i = 0; i < 20; i++) sim.Step(0.25f); // 5 s: the global gap of 6 s holds the second pair back
            Assert.That(events, Is.EqualTo(1));
            for (int i = 0; i < 8; i++) sim.Step(0.25f);
            Assert.That(events, Is.EqualTo(2), "the other pair after the 6 s gap");
            Assert.That(c.State == WalkerState.Social || d.State == WalkerState.Social, Is.True);
            Assert.That(sim.PairWait(a, b), Is.GreaterThan(50), "the same pair waits 60 s");

            // a failed roll (2 in 3) starts nothing and the pair rolls again only after a short wait
            var sim2 = new WalkerSim(4) { Roll = () => 0.9 };
            var e = sim2.Add(new Walker { State = WalkerState.Waiting, Timer = 999, Pos = Vector2.zero });
            var f = sim2.Add(new Walker { State = WalkerState.Waiting, Timer = 999, Pos = new Vector2(0.2f, 0) });
            int n2 = 0;
            sim2.Social += (x, y, k) => n2++;
            sim2.Step(0.3f);
            Assert.That(n2, Is.EqualTo(0));
            Assert.That(sim2.PairWait(e, f), Is.GreaterThan(0));
            // too far apart: no roll at all
            var g = sim2.Add(new Walker { State = WalkerState.Waiting, Timer = 999, Pos = new Vector2(9, 9) });
            Assert.That(sim2.PairWait(e, g), Is.EqualTo(0));
        }

        [Test]
        public void Walkers_CommuteToTheFireAndBack()
        {
            var sim = new WalkerSim(1);
            var path = new List<Vector2> { new Vector2(0, 0), new Vector2(3, 0) };
            var w = sim.Add(new Walker { Mode = WalkerMode.Commute, Path = path, S = 0, Dir = 1, Speed = 1, Goods = Currencies.Ore, Carrying = true });
            int delivered = 0;
            sim.Delivered += x => delivered++;
            for (int i = 0; i < 40; i++) sim.Step(0.1f);
            Assert.That(delivered, Is.EqualTo(1));
            Assert.That(w.Carrying, Is.False);
            Assert.That(w.Dir, Is.EqualTo(-1), "walks back after popping the goods");
        }

        [Test]
        public void Routes_AreCachedUntilTheLayoutChanges()
        {
            Grant(Farm);
            _f.Service.ExecuteNow(new CityStreets { Adds = new List<int> { Cell(7, 9), Cell(6, 9) } });
            var uid = (string)_f.Service.ExecuteNow(new CityPlace { BlueprintId = Farm, X = 6, Y = 10 }).Payload;
            var r = new CityRoutes();
            r.Ensure(S, C);
            var route = r.ToFire(uid);
            Assert.That(route, Is.Not.Null);
            Assert.That(route[0], Is.EqualTo(Cell(6, 9)), "the door is the street it touches");
            int last = route[route.Count - 1];
            Assert.That(Mathf.Abs(last % 20 - C.CityTuning.FireX) + Mathf.Abs(last / 20 - C.CityTuning.FireY), Is.EqualTo(1));
            r.Ensure(S, C);
            Assert.That(r.Builds, Is.EqualTo(1), "cached");
            _f.Service.ExecuteNow(new CityStreets { Adds = new List<int> { Cell(6, 8) } });
            r.Ensure(S, C);
            Assert.That(r.Builds, Is.EqualTo(2), "rebuilt on a layout change");
        }

        // ---- the screen ---------------------------------------------------------------------------------------------------

        [Test]
        public void Screen_DockCardAndDigPopup()
        {
            Grant(Farm);
            var uid = (string)_f.Service.ExecuteNow(new CityPlace { BlueprintId = Farm, X = 7, Y = 9 }).Payload;
            _f.Clock.Advance(3_600_000);
            _f.Service.Refresh(_ => { });
            var city = OpenCity();
            Assert.That(Named<Button>(city.Root, "Dock:Build"), Is.Not.Null);
            Assert.That(Named<Button>(city.Root, "Dock:Workers"), Is.Not.Null);
            Assert.That(Named<Button>(city.Root, "Dock:Output"), Is.Not.Null);
            Assert.That(Named<Button>(city.Root, "CollectAll"), Is.Not.Null);
            Assert.That(Named<RectTransform>(city.Root, "Rail"), Is.Null, "phones: the rail only in edit mode");
            Assert.That(Named<Text>(city.Root, "Zz:" + uid), Is.Not.Null, "nobody works there");
            city.Select(uid);
            var card = Named<RectTransform>(city.Root, "BuildingCard");
            Assert.That(card, Is.Not.Null);
            Assert.That(Named<Text>(card, "LevelLine").text, Does.StartWith("Lv 1/10"));
            Assert.That(Named<Text>(card, "PlacementSkill").text, Does.Contain("Fields"));
            Assert.That(Named<Button>(card, "EmptySlot"), Is.Not.Null);
            Assert.That(Named<Text>(card, "CostType").text, Does.Contain("Biomass"));
            Assert.That(card.GetComponentsInChildren<Text>(true).Any(t => t.text == "Upgrade"), Is.True);

            var patch = C.CityPatches.First(p => CitySystem.DigBlockReason(S, C, p.Id) == null);
            city.OpenDig(patch.Id);
            var pop = Named<RectTransform>(city.Root, "DigPopup");
            Assert.That(pop, Is.Not.Null);
            Assert.That(Named<Text>(pop, "DigNumber").text, Is.EqualTo("Dig #1"));
            Assert.That(city.World.MarkCount, Is.EqualTo(patch.Cells.Count), "the patch is framed");
            pop.GetComponentsInChildren<Button>(true).First(b => b.name == "Dig").onClick.Invoke();
            Assert.That(S.City.DigPatchId == patch.Id || S.City.DugPatches.Contains(patch.Id), Is.True);
            city.Rebuild();
            Assert.That(city.World.DrillVisible || S.City.DugPatches.Contains(patch.Id), Is.True);
        }

        [Test]
        public void Screen_EditModeRail_TabletAlwaysShowsIt()
        {
            Grant(Farm, Mine);
            var city = OpenCity();
            city.EnterEdit();
            Assert.That(Named<RectTransform>(city.Root, "Rail"), Is.Not.Null);
            Assert.That(Named<Image>(city.Root, "Tray:streets"), Is.Not.Null);
            Assert.That(Named<Image>(city.Root, "Tray:new:" + Farm), Is.Not.Null);
            Assert.That(Named<Button>(city.Root, "EditDone"), Is.Not.Null);
            Assert.That(Named<Button>(city.Root, "Tool:Street"), Is.Not.Null);
            Assert.That(city.World.Width, Is.LessThanOrEqualTo(CityWorld.OverviewWidth));
            city.ExitEdit();
            Assert.That(Named<RectTransform>(city.Root, "Rail"), Is.Null);
            city.ForceTablet = true;
            city.Rebuild();
            Assert.That(Named<RectTransform>(city.Root, "Rail"), Is.Not.Null, "tablets: always on");
        }

        [Test]
        public void Screen_RailCardDimsAtTheCap()
        {
            Grant(Mine);
            for (int i = 0; i < 3; i++)
            {
                var plan = CityPlanner.Find(S, C, Mine);
                if (plan.Streets.Count > 0) _f.Service.ExecuteNow(new CityStreets { Adds = plan.Streets });
                Assert.That(_f.Service.ExecuteNow(new CityPlace { BlueprintId = Mine, X = plan.X, Y = plan.Y, Rotated = plan.Rotated }).Ok, Is.True);
            }
            var city = OpenCity();
            city.EnterEdit();
            var card = Named<Image>(city.Root, "Tray:new:" + Mine);
            Assert.That(card.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("3/3 placed")), Is.True);
            Assert.That(card.GetComponent<CityDragSource>(), Is.Null, "over the cap: not draggable");
        }

        [Test]
        public void Screen_StoreTool_AndUndoBringsItBack()
        {
            Grant(Farm);
            var uid = (string)_f.Service.ExecuteNow(new CityPlace { BlueprintId = Farm, X = 7, Y = 9 }).Payload;
            _f.Clock.Advance(3_600_000);
            _f.Service.Refresh(_ => { });
            var city = OpenCity();
            city.SetTool(CityTool.Store);
            city.StoreBuilding(uid);
            Assert.That(CitySystem.Building(S, uid).Placed, Is.False);
            Assert.That(Named<Image>(city.Root, "Tray:stored:" + uid), Is.Not.Null, "it waits in the tray");
            city.UndoLast();
            Assert.That(CitySystem.Building(S, uid).Placed, Is.True);
        }

        [Test]
        public void Screen_DiorameStaysWithinTheSpriteBudget_FullCityAtNight()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            var rewards = new List<Reward> { Reward.Cur(Currencies.Biomass, 50_000_000), Reward.Cur(Currencies.Ore, 50_000_000), Reward.Cur(Currencies.GearCores, 1_000_000) };
            foreach (var bp in C.Blueprints.Keys) rewards.Add(new Reward(RewardKinds.Blueprint, bp, 1));
            foreach (var a in C.AlienTypes.Keys) rewards.Add(new Reward(RewardKinds.Alien, a, 1));
            _f.Service.DevGrant(rewards);
            // all the land, then as many buildings as fit
            for (int i = 0; i < 20; i++)
            {
                var p = C.CityPatches.FirstOrDefault(x => CitySystem.DigBlockReason(S, C, x.Id) == null);
                if (p == null) break;
                _f.Service.ExecuteNow(new CityDig { PatchId = p.Id });
                _f.Clock.Advance(2 * 24 * 3_600_000L);
                _f.Service.Refresh(_ => { });
            }
            // a street spine like the sim bot's, then as many buildings as fit beside it
            var spine = CityPlanner.SpineCells(C);
            _f.Service.ExecuteNow(new CityStreets { Adds = CityPlanner.PaveableSpine(S, C, spine) });
            int placed = 0;
            foreach (var bp in C.Blueprints.Keys.OrderBy(k => (int)C.Blueprint(k).Rarity).ThenBy(k => k))
                for (int copy = 0; copy < C.Blueprint(bp).Copies; copy++)
                {
                    var plan = CityPlanner.Find(S, C, bp, null, 2, spine);
                    if (plan == null) break;
                    if (plan.Streets.Count > 0) _f.Service.ExecuteNow(new CityStreets { Adds = plan.Streets });
                    if (_f.Service.ExecuteNow(new CityPlace { BlueprintId = bp, X = plan.X, Y = plan.Y, Rotated = plan.Rotated }).Ok) placed++;
                }
            _f.Clock.Advance(3 * 24 * 3_600_000L);
            _f.Service.Refresh(_ => { });
            var built = S.City.Buildings.Where(b => b.Placed).ToList();
            int k2 = 0;
            foreach (var a in S.Aliens.Owned.Keys.ToList())
            {
                if (k2 >= built.Count) break;
                _f.Service.ExecuteNow(new AlienAssign { AlienTypeId = a, BuildingUid = built[k2++].Uid });
            }
            _f.Clock.NowMs = CityDayNight.MiddleOf("night", _f.Clock.NowMs);
            var city = OpenCity();
            city.Rebuild();
            Assert.That(placed, Is.GreaterThan(25), "a crowded city");
            Assert.That(city.World.Darkness, Is.EqualTo(1));
            Assert.That(city.World.LampsLit, Is.GreaterThan(0));
            Assert.That(city.World.WalkersVisible, Is.GreaterThan(5));
            Assert.That(city.World.ActiveSprites, Is.LessThanOrEqualTo(CityWorld.SpriteBudget), "≤ 180 sprites");
            Assert.That(city.World.TerrainBakes, Is.EqualTo(1), "the land is baked once until the layout changes");
        }

        [Test]
        public void Screen_TerrainRebakesOnlyOnLayoutChange()
        {
            Grant(Farm);
            var city = OpenCity();
            int bakes = city.World.TerrainBakes;
            city.Rebuild();
            city.Rebuild();
            Assert.That(city.World.TerrainBakes, Is.EqualTo(bakes));
            city.SetTool(CityTool.Street);
            city.StrokeBegin(Cell(8, 9));
            city.StrokeTo(Cell(7, 9));
            city.StrokeEnd();
            Assert.That(city.World.TerrainBakes, Is.EqualTo(bakes + 1));
        }

        [Test]
        public void Screen_WorkersAssignByDrop_UnstaffedBecomesActive()
        {
            Grant(Farm);
            _f.Service.DevGrant(new[] { new Reward(RewardKinds.Alien, "alien_blorp", 1) });
            var uid = (string)_f.Service.ExecuteNow(new CityPlace { BlueprintId = Farm, X = 7, Y = 9 }).Payload;
            _f.Clock.Advance(3_600_000);
            _f.Service.Refresh(_ => { });
            var city = OpenCity();
            Assert.That(city.World.IsDim(uid), Is.True, "Zz: lights off");
            city.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "Dock:Workers").onClick.Invoke();
            Assert.That(Named<Image>(city.Root, "Alien:alien_blorp"), Is.Not.Null);
            city.AssignAlien("alien_blorp", uid);
            Assert.That(CitySystem.IsActive(S, C, CitySystem.Building(S, uid)), Is.True);
            Assert.That(city.World.IsDim(uid), Is.False);
            Assert.That(Named<Text>(city.Root, "Zz:" + uid), Is.Null);
        }

        // ---- pop reveal ----------------------------------------------------------------------------------------------------

        [Test]
        public void PopReveal_OneCardPerChance_TripleGetsTheBanner()
        {
            var results = new List<CityPopResult>
            {
                new CityPopResult { Uid = "bp_token_relay#1", Currency = Currencies.BlueprintTokens, Pulls = 1 },
                new CityPopResult { Uid = "bp_token_relay#1", Currency = Currencies.BlueprintTokens, Pulls = 2 },
                new CityPopResult { Uid = "bp_token_relay#1", Currency = Currencies.BlueprintTokens, Pulls = 3 },
            };
            var items = CityScreen.PopItems(results, C);
            Assert.That(items.Select(i => i.Tier), Is.EqualTo(new[] { Rarity.Rare, Rarity.Epic, Rarity.Legendary }));
            Assert.That(items[2].Amount, Is.EqualTo("×3"));
            Assert.That(items[2].Name, Is.EqualTo("Blueprint pulls"));
            Assert.That(items[0].Name, Is.EqualTo("Blueprint pull"));
            Assert.That(items[0].Note, Is.EqualTo("Research Scroll"));
            var modal = CityScreen.ShowPopReveal(_f.App.ModalLayer, results, C);
            var player = modal.Root.GetComponent<PullRevealPlayer>();
            player.Finish();
            Assert.That(player.BannerShown, Is.True);
            Assert.That(modal.Root.GetComponentsInChildren<Text>(true).Any(t => t.text == "×3!"), Is.True);
            Assert.That(player.Cards.Count, Is.EqualTo(3));
            modal.Close();
            var single = CityScreen.ShowPopReveal(_f.App.ModalLayer, results.Take(1).ToList(), C).Root.GetComponent<PullRevealPlayer>();
            single.Finish();
            Assert.That(single.BannerShown, Is.False, "one pull: no banner");
        }

        [Test]
        public void PopChances_FromTheBuilding_GrantsAndReveals()
        {
            Grant("bp_token_relay");
            var plan = CityPlanner.Find(S, C, "bp_token_relay");
            if (plan.Streets.Count > 0) _f.Service.ExecuteNow(new CityStreets { Adds = plan.Streets });
            var uid = (string)_f.Service.ExecuteNow(new CityPlace { BlueprintId = "bp_token_relay", X = plan.X, Y = plan.Y, Rotated = plan.Rotated }).Payload;
            _f.Clock.Advance(24 * 3_600_000L);
            _f.Service.Refresh(_ => { });
            CitySystem.Building(S, uid).Chances = 3;
            _f.Service.Refresh(_ => { });
            var city = OpenCity();
            Assert.That(Named<Button>(city.Root, "Chance:" + uid), Is.Not.Null, "stored chances show on the building");
            long before = S.Wallet.Get(Currencies.BlueprintTokens);
            city.PopChances(uid, true);
            Assert.That(S.Wallet.Get(Currencies.BlueprintTokens), Is.GreaterThanOrEqualTo(before + 3));
            Assert.That(city.LastPopReveal, Is.Not.Null);
            Assert.That(city.LastPopReveal.Root.GetComponent<PullRevealPlayer>().Cards.Count, Is.EqualTo(3));
            city.LastPopReveal.Close();
        }

        // ---- feedback / hub ------------------------------------------------------------------------------------------------

        [Test]
        public void Feedback_CityMoments()
        {
            Assert.That(GameFeedback.KindOf("city_dug"), Is.EqualTo(FeedbackKind.Banner));
            Assert.That(GameFeedback.BannerText(new GameEvent { Type = "city_dug", Args = new List<string> { "patch_a" } }, C, S, out var sub, out _), Is.EqualTo("NEW LAND!"));
            Assert.That(sub, Is.EqualTo(C.CityPatch("patch_a").Name));
            Assert.That(GameFeedback.KindOf("building_placed"), Is.EqualTo(FeedbackKind.Silent), "T05");
            Assert.That(GameFeedback.KindOf("building_moved"), Is.EqualTo(FeedbackKind.Silent), "T08");
            Assert.That(GameFeedback.KindOf("building_built"), Is.EqualTo(FeedbackKind.Animation), "T06");
            Assert.That(GameFeedback.KindOf("building_upgraded"), Is.EqualTo(FeedbackKind.Animation), "T07");
            Assert.That(GameFeedback.KindOf("building_stored"), Is.EqualTo(FeedbackKind.Animation), "T09");
            Assert.That(GameFeedback.KindOf("alien_assigned"), Is.EqualTo(FeedbackKind.Silent), "T18: the walker shows it");
        }

        [Test]
        public void Crumble_StartsAtTheEdgeTouchingTheLand()
        {
            var patch = C.CityPatches.First(p => CityLayout.PatchTouchesLand(S, C, p));
            var order = CityWorld.CrumbleOrder(patch, S, C);
            Assert.That(order.Count, Is.EqualTo(patch.Cells.Count));
            int first = order[0], w = C.CityTuning.GridWidth;
            bool touches = new[] { first + 1, first - 1, first + w, first - w }.Any(n => n >= 0 && n < 400 && CityLayout.IsLand(S, C, n));
            Assert.That(touches, Is.True);
        }

        [Test]
        public void Hub_SquadWandersAroundTheWorm()
        {
            Grant(ContentDb.LaunchPodId);
            _f.Service.DevGrant(new[] { new Reward(RewardKinds.Alien, "alien_blorp", 1) });
            var plan = CityPlanner.Find(S, C, ContentDb.LaunchPodId);
            if (plan.Streets.Count > 0) _f.Service.ExecuteNow(new CityStreets { Adds = plan.Streets });
            var pod = (string)_f.Service.ExecuteNow(new CityPlace { BlueprintId = ContentDb.LaunchPodId, X = plan.X, Y = plan.Y }).Payload;
            _f.Clock.Advance(3_600_000);
            _f.Service.Refresh(_ => { });
            Assert.That(_f.Service.ExecuteNow(new AlienAssign { AlienTypeId = "alien_blorp", BuildingUid = pod }).Ok, Is.True);
            _f.App.Router.Reset(new HubScreen());
            var hub = (HubScreen)_f.App.Router.Current;
            Assert.That(AlienSystem.ActiveSquad(S, C), Has.Count.EqualTo(1));
            Assert.That(hub.Squad, Is.Not.Null);
            Assert.That(hub.Squad.Count, Is.EqualTo(1));
            Assert.That(Named<Image>(hub.Root, "Squad:alien_blorp"), Is.Not.Null);
            // the walker stays on the planet inside the screen
            var rt = Named<Image>(hub.Root, "Squad:alien_blorp").rectTransform;
            Assert.That(rt.anchoredPosition.x, Is.InRange(0f, 1080f));
        }
    }
}
