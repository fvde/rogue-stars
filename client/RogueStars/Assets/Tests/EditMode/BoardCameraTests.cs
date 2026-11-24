using System.Collections.Generic;
using NUnit.Framework;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// The run board's camera (playtest feedback): the worm framed horizontally centred and in the open band between the
    /// top HUD and the dice cap on every common aspect ratio, pinch / wheel zoom and drag panning within bounds, the eased
    /// return to the follow framing on a roll, taps on fork targets still picking after a pan, and the dice stand drawn
    /// through one camera (the cap's base on the slab).
    /// </summary>
    public sealed class BoardCameraTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; RunFx.Instant = true; }
        [TearDown] public void TearDown() { RunScreen.InstantOutcomes = false; RunFx.Instant = false; _f.Dispose(); }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        /// <summary>
        /// Phone and tablet screens (px), as the canvas sees them (CanvasScaler: reference 1080 × 1920, match 0.5): 16:9,
        /// 19.5:9, 20:9, 20:9 QHD, 21:9, iPad 4:3, 16:10 tablet.
        /// </summary>
        private static readonly Vector2[] Screens =
        {
            new Vector2(1080, 1920), new Vector2(1080, 2340), new Vector2(1080, 2400), new Vector2(1440, 3200),
            new Vector2(1080, 2520), new Vector2(1536, 2048), new Vector2(1600, 2560),
        };

        private static Vector2 CanvasSize(Vector2 screen)
        {
            float k = Mathf.Sqrt(screen.x / 1080f * (screen.y / 1920f));
            return screen / k;
        }

        /// <summary>The worm's middle on screen (holder px from the top-left) for a camera framing it.</summary>
        private static Vector2 WormOnScreen(WorldBoard board, Vector2 feet, Vector2 holder)
        {
            var o = board.ToOverlay(feet + new Vector2(0, RunScreen.WormFocus), holder);
            return new Vector2(o.x, -o.y);
        }

        [Test]
        public void Framing_PutsTheWormCentred_InTheOpenBand_AtEveryAspect()
        {
            var board = new WorldBoard("FramingTest");
            try
            {
                foreach (var screen in Screens)
                {
                    var holder = CanvasSize(screen);
                    string at = screen.x + "x" + screen.y;
                    board.EnsureTarget(Mathf.RoundToInt(screen.x), Mathf.RoundToInt(screen.y));
                    Assert.That(board.Target.width / (float)board.Target.height, Is.EqualTo(holder.x / holder.y).Within(0.002f), at + ": the target keeps the view's aspect");
                    float width = RunScreen.DefaultViewWidth(holder);
                    var band = RunScreen.OpenBand(holder);
                    float perUnit = holder.x / width;
                    Assert.That((band.y - band.x) / perUnit, Is.GreaterThanOrEqualTo(RunScreen.MinBandUnits - 0.01f), at + ": the open band shows enough board");
                    Assert.That(width, Is.InRange(WorldBoard.ViewWidth, RunScreen.MaxDefaultWidth), at);
                    foreach (var feet in new[] { Vector2.zero, new Vector2(3.4f, 12.2f), new Vector2(-5.1f, 40.6f) })
                    {
                        board.SetView(RunScreen.FollowCentre(feet, holder, width), width);
                        var mid = WormOnScreen(board, feet, holder);
                        Assert.That(mid.x, Is.EqualTo(holder.x / 2).Within(0.5f), at + ": horizontally centred");
                        float share = (mid.y - band.x) / (band.y - band.x);
                        Assert.That(share, Is.EqualTo(RunScreen.FocalShare).Within(0.005f), at + ": at the focal point of the open band");
                        // the whole worm stands clear of the HUD and the cap
                        var o = board.ToOverlay(feet + new Vector2(0, WorldBoard.WormHeight), holder);
                        Assert.That(-o.y, Is.GreaterThan(band.x + 0.3f * perUnit), at + ": head under the HUD");
                        o = board.ToOverlay(feet, holder);
                        Assert.That(-o.y, Is.LessThan(band.y - 1.2f * perUnit), at + ": feet well above the dice cap");
                    }
                }
            }
            finally { board.Dispose(); UnityEngine.Object.DestroyImmediate(board.Root); }
        }

        [Test]
        public void RunScreen_FramesTheWorm_BetweenTheMeasuredHudAndCap_AtEveryAspect()
        {
            // the real screen at each canvas size: the measured coin chip (the HUD's lowest piece) and the cap's top
            var canvas = _f.Canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            foreach (var size in Screens)
            {
                var canvasSize = CanvasSize(size);
                _f.Canvas.sizeDelta = canvasSize;
                screen.Rebuild();
                for (int i = 0; i < 300; i++) screen.Frame(1f / 60); // the camera eases to the new framing
                string at = size.x + "x" + size.y;
                var holder = (RectTransform)FindNamed(screen.Root, "BoardHolder");
                var hs = holder.rect.size;
                Assert.That(screen.World.Target.width / (float)screen.World.Target.height, Is.EqualTo(hs.x / hs.y).Within(0.01f), at + ": board target aspect = view aspect");
                float FromTop(RectTransform rt, bool bottomEdge)
                {
                    var corners = new Vector3[4];
                    rt.GetWorldCorners(corners);
                    var local = holder.InverseTransformPoint(bottomEdge ? corners[0] : corners[1]);
                    return holder.rect.yMax - local.y;
                }
                float hudBottom = FromTop((RectTransform)FindNamed(screen.Root, "CoinsChip"), true);
                var button = (RectTransform)FindNamed(screen.Root, "RollButton");
                float frameTop = FromTop(button, false), frameBottom = FromTop(button, true);
                float capTop = frameTop + (frameBottom - frameTop) * (1 - DiceStand.CapTop / DiceStand.FrameH);
                Assert.That(hudBottom, Is.LessThanOrEqualTo(RunScreen.OpenBand(hs).x + 1), at + ": the band starts under the HUD");
                Assert.That(capTop, Is.EqualTo(RunScreen.OpenBand(hs).y).Within(1.5f), at + ": the band ends at the cap");
                var feet = (Vector2)screen.World.Token.localPosition - new Vector2(0, 0.02f);
                var mid = screen.OverlayPoint(feet + new Vector2(0, RunScreen.WormFocus));
                Assert.That(mid.x, Is.EqualTo(hs.x / 2).Within(1f), at + ": the worm is horizontally centred");
                float y = -mid.y;
                Assert.That(y, Is.InRange(hudBottom + 0.4f * (capTop - hudBottom), capTop - 0.3f * (capTop - hudBottom)), at + ": the worm sits in the open band");
            }
        }

        private RunScreen OpenOnLevel()
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            for (int i = 0; i < 120; i++) screen.Frame(1f / 60);
            return screen;
        }

        [Test]
        public void PanAndZoom_StayWithinBounds_AndEaseBackOnARoll()
        {
            var screen = OpenOnLevel();
            var holder = ((RectTransform)FindNamed(screen.Root, "BoardHolder")).rect.size;
            var follow = screen.CameraCentre;
            float baseW = RunScreen.DefaultViewWidth(holder);
            Assert.That(screen.CameraWidth, Is.EqualTo(baseW).Within(1e-4f));

            // a one-finger drag: the board follows the finger exactly
            var c = holder / 2;
            screen.MoveView(c, c + new Vector2(120, -60), 1);
            float perUnit = holder.x / baseW;
            Assert.That(screen.CameraManual, Is.True);
            Assert.That(Vector2.Distance(screen.CameraCentre, follow + new Vector2(-120, -60) / perUnit), Is.LessThan(1e-3f), "the dragged point stays under the finger");
            // the view stays put while the player looks (no drift back)
            for (int i = 0; i < 300; i++) screen.Frame(1f / 60);
            Assert.That(Vector2.Distance(screen.CameraCentre, follow + new Vector2(-120, -60) / perUnit), Is.LessThan(1e-3f), "no drift while looking around");

            // zoom out about a point: the point under the fingers stays put, the width clamps at MaxZoom
            var anchor = new Vector2(holder.x * 0.3f, holder.y * 0.4f);
            screen.MoveView(anchor, anchor, 0.8f);
            Assert.That(screen.CameraWidth, Is.EqualTo(baseW / 0.8f).Within(1e-3f), "pinched out");
            for (int i = 0; i < 20; i++) screen.MoveView(anchor, anchor, 0.5f);
            Assert.That(screen.CameraWidth, Is.EqualTo(baseW * RunScreen.MaxZoom).Within(1e-3f), "zoom clamps at the widest view");
            for (int i = 0; i < 20; i++) screen.MoveView(anchor, anchor, 2f);
            Assert.That(screen.CameraWidth, Is.EqualTo(baseW * RunScreen.MinZoom).Within(1e-3f), "and at the closest");

            // a far drag never loses the board: the centre stays within the drawn path (plus a margin)
            for (int i = 0; i < 40; i++) screen.MoveView(c, c + new Vector2(-900, 900), 1);
            var b = screen.World.PathBounds;
            Assert.That(screen.CameraCentre.x, Is.InRange(Mathf.Min(b.xMin, follow.x) - 1.6f, Mathf.Max(b.xMax, follow.x) + 1.6f), "within the path");
            Assert.That(screen.CameraCentre.y, Is.InRange(Mathf.Min(b.yMin, follow.y) - 1.6f, Mathf.Max(b.yMax, follow.y) + 1.6f), "within the path");

            // a roll: the view eases back (no snap at the roll), reaching the follow framing at the default width in ~0.4 s
            var before = screen.CameraCentre;
            Assert.That(_f.Service.ExecuteNow(new RunRoll()).Ok, Is.True);
            while (_f.App.Router.Current is BattlePlaybackScreen bp) { bp.PlayAll(); _f.App.Router.Pop(); }
            Assert.That(screen.CameraManual, Is.False);
            Assert.That(screen.CameraReturning, Is.True, "a roll pans back");
            Assert.That(Vector2.Distance(screen.CameraCentre, before), Is.LessThan(1e-3f), "no snap at the roll");
            screen.Frame(RunScreen.ReturnSeconds * 0.5f);
            Assert.That(screen.CameraCentre, Is.Not.EqualTo(before), "halfway back");
            Assert.That(screen.CameraWidth, Is.GreaterThan(baseW * RunScreen.MinZoom).And.LessThan(baseW), "the zoom eases back too");
            screen.Frame(RunScreen.ReturnSeconds * 0.55f);
            Assert.That(screen.CameraReturning, Is.False);
            Assert.That(screen.CameraWidth, Is.EqualTo(baseW).Within(1e-4f), "back at the default zoom");
            var token = (Vector2)screen.World.Token.localPosition - new Vector2(0, 0.02f);
            Assert.That(Vector2.Distance(screen.CameraCentre, RunScreen.FollowCentre(token, holder, baseW)), Is.LessThan(0.05f), "back on the worm");
        }

        [Test]
        public void Gestures_DragPans_PinchZooms_WheelZooms()
        {
            var screen = OpenOnLevel();
            var holder = ((RectTransform)FindNamed(screen.Root, "BoardHolder")).rect.size;
            float baseW = screen.CameraWidth;
            var view = FindNamed(screen.Root, "BoardView").GetComponent<BoardGestures>();
            var overlay = FindNamed(screen.Root, "BoardOverlay").GetComponent<BoardGestures>();
            Assert.That(view, Is.Not.Null);
            Assert.That(overlay, Is.Not.Null, "drags that start on a tap target pan too");
            Assert.That(overlay.State, Is.SameAs(view.State), "one gesture across the board's surfaces");
            var state = view.State;
            // two fingers moving apart: zoom in about their midpoint
            state.Pointers.Clear();
            state.Pointers[1] = new Vector2(300, 900);
            state.Moved(2, new Vector2(600, 900), new Vector2(630, 900));
            Assert.That(screen.CameraWidth, Is.EqualTo(baseW * 300 / 330).Within(0.01f), "fingers apart zoom in");
            // one finger: pans
            state.Pointers.Clear();
            var cam = screen.CameraCentre;
            state.Moved(1, new Vector2(500, 900), new Vector2(500, 1000));
            Assert.That(screen.CameraCentre.y, Is.GreaterThan(cam.y), "dragging down shows what lies up the path");
            // the wheel zooms out at the cursor
            float w = screen.CameraWidth;
            var e = new PointerEventData(EventSystem.current) { position = new Vector2(10, 10), scrollDelta = new Vector2(0, -2) };
            view.OnScroll(e);
            Assert.That(screen.CameraWidth, Is.GreaterThan(w), "wheel down zooms out");
            Assert.That(holder.x, Is.GreaterThan(0));
        }

        [Test]
        public void ForkTargets_StayPinned_AndTapAfterAPan()
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            var run = S.Run.Active;
            run.Tiles.Clear();
            string[] ids = { "start", "empty", "empty", "empty", "empty", "empty", "empty", "empty", "shop", "empty", "boss_gate" };
            for (int i = 0; i < ids.Length; i++) run.Tiles.Add(new RunTile { Id = i, TileId = ids[i], X = i, Y = i >= 7 && i <= 8 ? 1 : 0 });
            void Link(int a, int b) => run.Tiles[a].Next.Add(b);
            Link(0, 1); Link(1, 2); Link(2, 3); Link(2, 7); Link(3, 4); Link(4, 5); Link(5, 6); Link(6, 9); Link(7, 8); Link(8, 9); Link(9, 10);
            run.Tiles[7].X = 3; run.Tiles[8].X = 4;
            run.BossGateTile = -1; run.BoardEnd = 10;
            run.Trail.Clear(); run.Trail.AddRange(new[] { 0, 1, 2 });
            run.Position = 2;
            run.Phase = RunPhase.ChooseBranch;
            run.BranchOptions.Clear(); run.BranchOptions.AddRange(new[] { 3, 7 });
            run.StepsLeft = 4;
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            var a = (RectTransform)FindNamed(screen.Root, "BranchPick:A");
            var holder = ((RectTransform)FindNamed(screen.Root, "BoardHolder")).rect.size;
            var c = holder / 2;
            // a drag that starts on the target: it pans the board, the target rides along over its tile
            var start = a.anchoredPosition;
            var state = FindNamed(screen.Root, "BoardOverlay").GetComponent<BoardGestures>().State;
            state.Pointers.Clear();
            state.Moved(7, c, c + new Vector2(80, 50));
            Assert.That(Vector2.Distance(a.anchoredPosition, start + new Vector2(80, -50)), Is.LessThan(0.5f), "the target stays pinned on its tile");
            Assert.That(Vector2.Distance(a.anchoredPosition, screen.OverlayPoint(WorldBoard.TopOf(run.Tiles[6]) + new Vector2(0, 0.2f))), Is.LessThan(0.5f));
            // a tap still picks
            FindNamed(screen.Root, "BranchPick:B").GetComponent<Button>().onClick.Invoke();
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Shop), "path B taken after the pan");
        }

        [Test]
        public void DiceStand_OneCamera_CapSitsOnTheSlab()
        {
            // the cap's base ring and the slab top are the same plane (y = 0): the cap's footprint lies inside the slab's,
            // with stone showing in front of it, and the top plane squashes like the slab (same camera)
            var capBase = DiceStand.Ring(DiceStand.CapHalfW + DiceStand.CapTaper, DiceStand.CapHalfD + DiceStand.CapTaper, DiceStand.CapRadius, 0);
            var slab = DiceStand.Ring(DiceStand.SlabHalfW, DiceStand.SlabHalfD, DiceStand.SlabRadius, 0);
            float capFront = float.MaxValue, slabFront = float.MaxValue;
            foreach (var v in capBase) capFront = Mathf.Min(capFront, DiceStand.Project(v).y);
            foreach (var v in slab) slabFront = Mathf.Min(slabFront, DiceStand.Project(v).y);
            Assert.That(slabFront, Is.LessThan(capFront - 25), "the slab's front edge shows in front of the cap");
            Assert.That(DiceStand.Project(Vector3.zero), Is.EqualTo(DiceStand.Base), "the model origin is the frame's base point");
            DiceStand.TopPlane(out var top, out float squash);
            Assert.That(squash, Is.InRange(0.45f, 0.7f), "a slightly-above front view");
            float slabDepth = DiceStand.Project(new Vector3(0, 0, -100)).y - DiceStand.Project(new Vector3(0, 0, 100)).y;
            float capDepth = DiceStand.Project(new Vector3(0, DiceStand.CapHeight, -100)).y - DiceStand.Project(new Vector3(0, DiceStand.CapHeight, 100)).y;
            Assert.That(capDepth / slabDepth, Is.InRange(0.9f, 1.15f), "the cap's top and the slab foreshorten alike");
            // vertical edges converge downwards (a camera above looks down): the column is narrower at its foot
            float wTop = DiceStand.Project(new Vector3(DiceStand.ColHalfW, -DiceStand.SlabThick, DiceStand.ColHalfD)).x - DiceStand.Project(new Vector3(-DiceStand.ColHalfW, -DiceStand.SlabThick, DiceStand.ColHalfD)).x;
            float wLow = DiceStand.Project(new Vector3(DiceStand.ColHalfW, -400, DiceStand.ColHalfD)).x - DiceStand.Project(new Vector3(-DiceStand.ColHalfW, -400, DiceStand.ColHalfD)).x;
            Assert.That(wLow, Is.LessThan(wTop), "one vanishing direction for the stand");
            Assert.That(DiceStand.CapTop, Is.LessThan(DiceStand.FrameH - 4), "the cap fits its frame");
            Assert.That(DiceStand.FrontWallHeight, Is.GreaterThan(60), "room for the engraved caption");
            // both layers are drawn into the same frame
            var ped = HudArt.Pedestal(); var cap = HudArt.DiceButton(true);
            Assert.That(ped.rect.size, Is.EqualTo(cap.rect.size));
            Assert.That(top.x, Is.EqualTo(0.5f).Within(0.01f));
        }
    }
}
