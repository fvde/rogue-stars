using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    public enum CityTool { Move, Street, Erase, Store }

    /// <summary>
    /// City edit mode (City design §2, C's side rail): every edit is applied at once as its own command (no draft, no Save; Done just
    /// leaves); Undo sends the reverse command (<see cref="CityUndo"/>, 50 steps). The ghost floats one tile above the finger, its tiles
    /// green or red with a chip (the reason or the bonus, <see cref="CityGhost"/> = the rules' own check); an invalid drop leaves the
    /// ghost where it is until it is fixed (rotate, drag again) or dropped back on the rail. Streets are painted out of the network in
    /// one stroke = one command (<see cref="StreetStroke"/>), erasing what would cut something off is refused and flashes it red.
    /// </summary>
    public sealed partial class CityScreen
    {
        private bool _edit;
        private CityTool _tool = CityTool.Move;
        private CityGhost _ghost;
        private bool _ghostDragging;
        private StreetStroke _stroke;
        private Text _chipText;
        private Image _chipBg;
        private int _chipPin = -1;
        private RectTransform _dragIcon;
        private CityDragSource _drag;

        public readonly CityUndo Undo = new CityUndo();
        public bool EditMode => _edit;
        public CityTool Tool => _tool;
        public CityGhost Ghost => _ghost;
        public StreetStroke Stroke => _stroke;
        /// <summary>The ghost's chip text as shown (tests).</summary>
        public string GhostChip => _ghost?.Chip;

        // ---- entering / leaving ------------------------------------------------------------------------------------------

        /// <summary>Build: edit mode — the camera zooms one step in, the grid goes from 15% to 40%.</summary>
        public void EnterEdit()
        {
            _edit = true;
            _selected = null;
            _digPatch = null;
            _workersOpen = false;
            _targetWidth = CityWorld.EditWidth;
            _world?.SetMarks(null);
            Rebuild();
        }

        /// <summary>Done: leaves edit mode (everything is already applied; the undo history ends here).</summary>
        public void ExitEdit()
        {
            _edit = false;
            _stroke = null;
            Undo.Clear();
            ClearGhost();
            _targetWidth = CityWorld.OverviewWidth;
            _tool = CityTool.Move;
            Rebuild();
        }

        public void SetTool(CityTool tool)
        {
            if (!_edit) { _edit = true; _targetWidth = CityWorld.EditWidth; }
            _tool = tool;
            ClearGhost();
            Rebuild();
        }

        // ---- applying edits (and their undo) -------------------------------------------------------------------------------

        /// <summary>Runs an edit; in edit mode its reverse goes on the undo stack.</summary>
        public void Apply(GameCommand cmd, Action<bool> then = null)
        {
            var reverse = CityUndo.Reverse(State, cmd);
            bool edit = _edit;
            Run(cmd, r =>
            {
                if (r.Ok && edit) Undo.Applied(reverse, r.Payload);
                then?.Invoke(r.Ok);
            });
        }

        /// <summary>Undo: the reverse of the last edit (undoing a new build stores it, which cancels it and refunds the cost).</summary>
        public void UndoLast()
        {
            var reverse = Undo.Pop();
            if (reverse == null) return;
            ClearGhost();
            Run(reverse, r => Rebuild());
        }

        // ---- the ghost ------------------------------------------------------------------------------------------------------

        /// <summary>Picks a building from the rail: the ghost appears on a suggested spot (the best valid spot near the streets).</summary>
        public void StartPlace(string blueprintId, string storedUid)
        {
            if (!_edit) { _edit = true; _targetWidth = CityWorld.EditWidth; }
            _tool = CityTool.Move;
            var def = Content.Blueprint(blueprintId);
            if (def == null) return;
            int x, y;
            bool rot = false;
            var plan = CityPlanner.Find(State, Content, blueprintId, storedUid, 0);
            if (plan != null) { x = plan.X; y = plan.Y; rot = plan.Rotated; }
            else
            {
                var c = CityWorld.CellOf(_world.ToGrid(_cam));
                x = c.x; y = c.y;
            }
            _ghost = CityGhost.ForPlace(State, Content, blueprintId, storedUid, x, y, rot);
            ShowGhost();
            Rebuild();
        }

        /// <summary>Picks up a placed building (Move): the ghost starts on its own spot.</summary>
        public void StartMove(string uid)
        {
            var b = CitySystem.Building(State, uid);
            if (b == null || !b.Placed) return;
            if (!_edit) { _edit = true; _targetWidth = CityWorld.EditWidth; }
            _ghost = CityGhost.ForMove(State, Content, uid, b.X, b.Y, b.Rotated);
            ShowGhost();
        }

        /// <summary>Moves the ghost's top-left tile to (x, y) and re-checks it.</summary>
        public void GhostAt(int x, int y)
        {
            if (_ghost == null) return;
            if (_ghost.X == x && _ghost.Y == y && _ghost.Check != null) return;
            _ghost.At(State, Content, x, y);
            ShowGhost();
        }

        /// <summary>The ghost under a finger: one tile ABOVE the finger (the finger never hides it), centred on that tile.</summary>
        internal void GhostFromFinger(Vector2 px)
        {
            if (_ghost == null || _world == null) return;
            var world = _world.FromOverlay(px, _size) + new Vector2(0, Iso.TileH);
            var c = CityWorld.CellOf(_world.ToGrid(world));
            var def = Content.Blueprint(_ghost.BlueprintId);
            CityLayout.Footprint(def, _ghost.Rotated, out int w, out int h);
            GhostAt(c.x - (w - 1) / 2, c.y - (h - 1) / 2);
        }

        /// <summary>Let go: a valid spot places / moves it (one command); an invalid one leaves the ghost waiting there.</summary>
        public bool DropGhost()
        {
            if (_ghost == null) return false;
            _ghostDragging = false;
            if (!_ghost.Ok) { Rebuild(); return false; }
            var g = _ghost;
            bool done = false;
            Apply(g.Command, ok =>
            {
                done = ok;
                if (ok) ClearGhost();
                Rebuild();
            });
            return done;
        }

        /// <summary>⟳: swaps a non-square footprint (2×1 ↔ 1×2, 3×2 ↔ 2×3).</summary>
        public void RotateGhost()
        {
            if (_ghost == null) return;
            if (_ghost.Rotate(State, Content)) { ShowGhost(); Rebuild(); }
        }

        public void CancelGhost()
        {
            ClearGhost();
            Rebuild();
        }

        private void ClearGhost()
        {
            _ghost = null;
            _ghostDragging = false;
            _chipText = null;
            _chipBg = null;
            _chipPin = -1;
            if (_world != null)
            {
                _world.SetGhost(null, 0, 0, false, false, null);
                _world.SetMarks(null);
            }
        }

        private static readonly Color GoodTile = new Color(0.62f, 1f, 0.55f, 0.72f), BadTile = new Color(1f, 0.32f, 0.3f, 0.66f);

        private void ShowGhost()
        {
            if (_ghost == null || _world == null) return;
            var marks = new List<CityWorld.Mark>();
            for (int y = _ghost.Y; y < _ghost.Y + _ghost.H; y++)
                for (int x = _ghost.X; x < _ghost.X + _ghost.W; x++)
                {
                    marks.Add(new CityWorld.Mark { X = x, Y = y, Color = _ghost.Ok ? GoodTile : BadTile });
                    marks.Add(new CityWorld.Mark { X = x, Y = y, Color = _ghost.Ok ? new Color(0.2f, 0.75f, 0.3f, 1) : new Color(0.85f, 0.15f, 0.15f, 1), Ring = true });
                }
            _world.SetMarks(marks);
            _world.SetGhost(Content.Blueprint(_ghost.BlueprintId), _ghost.X, _ghost.Y, _ghost.Rotated, _ghost.Ok, _ghost.Move ? _ghost.Uid : null);
            // the chip follows without a rebuild while dragging
            if (_chipText != null && _chipPin >= 0 && _chipPin < _pins.Count)
            {
                _chipText.text = _ghost.Chip;
                _chipBg.color = _ghost.Ok ? new Color(0.12f, 0.45f, 0.24f, 0.95f) : new Color(0.62f, 0.14f, 0.16f, 0.95f);
                var p = _pins[_chipPin];
                p.World = GhostFoot();
                _pins[_chipPin] = p;
                UpdatePins();
            }
        }

        private Vector2 GhostFoot() => _world.FootprintCentre(_ghost.X, _ghost.Y, _ghost.W, _ghost.H);

        private bool GhostContains(Vector2Int c) =>
            _ghost != null && c.x >= _ghost.X - 1 && c.x <= _ghost.X + _ghost.W && c.y >= _ghost.Y - 1 && c.y <= _ghost.Y + _ghost.H;

        // ---- streets -------------------------------------------------------------------------------------------------------

        public void StrokeBegin(int cell)
        {
            _stroke = new StreetStroke(State, Content, _tool == CityTool.Erase);
            _stroke.Begin(cell);
            ShowStroke();
        }

        public void StrokeTo(int cell)
        {
            if (_stroke == null) { StrokeBegin(cell); return; }
            int before = _stroke.Cells.Count;
            string refusedBefore = _stroke.Refused;
            _stroke.Extend(cell);
            if (_stroke.Erase && _stroke.Refused != null && _stroke.Refused != refusedBefore) _world.Flash(_stroke.FlashCells, _stroke.FlashUids);
            if (_stroke.Cells.Count != before || _stroke.Refused != refusedBefore) ShowStroke();
        }

        /// <summary>The finger lifts: the whole stroke is ONE CityStreets command (or a refusal that flashes what it would cut off).</summary>
        public void StrokeEnd()
        {
            var s = _stroke;
            _stroke = null;
            _world?.SetMarks(null);
            if (s == null) return;
            var cmd = s.ToCommand();
            if (cmd == null)
            {
                if (s.Refused != null) { if (s.Erase) _world.Flash(s.FlashCells, s.FlashUids); App.Feedback.Tip(s.Refused, ToolButton()); }
                return;
            }
            var check = s.Check();
            if (!check.Ok)
            {
                _world.Flash(check.BadCells, check.BadUids);
                App.Feedback.Tip(CityGhost.Reason(check.Error), ToolButton());
                return;
            }
            Apply(cmd, ok => Rebuild());
            if (s.Refused != null) App.Feedback.Tip(s.Refused, ToolButton());
        }

        private RectTransform ToolButton() => FindRect(_overlay, "Tool:" + _tool);

        private void ShowStroke()
        {
            if (_stroke == null || _world == null) return;
            var marks = new List<CityWorld.Mark>();
            int w = Content.CityTuning.GridWidth;
            foreach (var cell in _stroke.Cells)
                marks.Add(new CityWorld.Mark { X = cell % w, Y = cell / w, Color = _stroke.Erase ? new Color(1f, 0.35f, 0.3f, 0.7f) : new Color(0.98f, 0.86f, 0.55f, 0.92f) });
            _world.SetMarks(marks);
        }

        // ---- taps and drags (routed by CityInput) ----------------------------------------------------------------------------

        private void EditTap(Vector2Int c)
        {
            int cell = CellIndex(c);
            if (_ghost != null)
            {
                // a tap on a valid waiting ghost places it; elsewhere it moves the ghost there
                if (c.x >= _ghost.X && c.x < _ghost.X + _ghost.W && c.y >= _ghost.Y && c.y < _ghost.Y + _ghost.H && _ghost.Ok) { DropGhost(); return; }
                GhostAt(c.x - (_ghost.W - 1) / 2, c.y - (_ghost.H - 1) / 2);
                Rebuild();
                return;
            }
            switch (_tool)
            {
                case CityTool.Street:
                case CityTool.Erase:
                    if (cell < 0) return;
                    StrokeBegin(cell);
                    StrokeEnd();
                    return;
                case CityTool.Store:
                {
                    string uid = BuildingAt(c.x, c.y) ?? BuildingAt(c.x + 1, c.y + 1);
                    if (uid != null) StoreBuilding(uid);
                    return;
                }
                default:
                {
                    string uid = BuildingAt(c.x, c.y) ?? BuildingAt(c.x + 1, c.y + 1);
                    if (uid != null) { SelectEdit(uid); return; }
                    return;
                }
            }
        }

        private void SelectEdit(string uid)
        {
            StartMove(uid);
            Rebuild();
        }

        /// <summary>Store: back to the tray (a building being upgraded can't; one under its first construction is cancelled and refunded).</summary>
        public void StoreBuilding(string uid)
        {
            var b = CitySystem.Building(State, uid);
            if (b == null) return;
            if (b.Level >= 1 && b.TimerId != null) { App.Feedback.Tip("Buildings being upgraded can be moved, not stored", ToolButton()); return; }
            ClearGhost();
            Apply(new CityStore { Uid = uid }, ok => Rebuild());
        }

        /// <summary>A drag starts on the diorama: true = the editor takes it (ghost / stroke), false = it pans.</summary>
        internal bool DragBegin(Vector2 px)
        {
            if (!_edit) return false;
            var c = CellAt(px);
            if (_ghost != null && (GhostContains(c) || GhostContains(CellAt(px + new Vector2(0, -60)))))
            {
                _ghostDragging = true;
                GhostFromFinger(px);
                return true;
            }
            if (_tool == CityTool.Street || _tool == CityTool.Erase)
            {
                StrokeBegin(CellIndex(c));
                return true;
            }
            if (_tool == CityTool.Move)
            {
                string uid = BuildingAt(c.x, c.y) ?? BuildingAt(c.x + 1, c.y + 1);
                if (uid == null) return false;
                StartMove(uid);
                _ghostDragging = true;
                GhostFromFinger(px);
                return true;
            }
            return false;
        }

        internal void DragMove(Vector2 px)
        {
            if (_ghostDragging) GhostFromFinger(px);
            else if (_stroke != null) StrokeTo(CellIndex(CellAt(px)));
        }

        internal void DragEnd(Vector2 px)
        {
            if (_ghostDragging) { _ghostDragging = false; DropGhost(); }
            else if (_stroke != null) StrokeEnd();
        }

        // ---- rail cards and alien chips (CityDragSource) -----------------------------------------------------------------------

        internal Vector2 ToHolder(Vector2 screen, Camera cam)
        {
            if (_holder == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_holder, screen, cam, out var local)) return screen;
            var r = _holder.rect;
            return new Vector2(local.x - r.xMin, local.y - r.yMax);
        }

        internal void TrayTap(string blueprintId, string uid)
        {
            if (blueprintId == null) { SetTool(CityTool.Street); return; }
            StartPlace(blueprintId, uid);
        }

        internal void TrayBegin(CityDragSource src)
        {
            _drag = src;
            if (src.BlueprintId == null) return;
            if (!_edit) { _edit = true; _targetWidth = CityWorld.EditWidth; } // rebuilt when the drag ends (the card is being dragged)
            _tool = CityTool.Move;
            _ghost = new CityGhost { BlueprintId = src.BlueprintId, Uid = src.Uid, X = -50, Y = -50 };
            _ghostDragging = true;
        }

        internal void TrayDrag(Vector2 px)
        {
            if (_ghost != null && _ghostDragging) GhostFromFinger(px);
        }

        internal void TrayEnd(Vector2 px, bool overRail)
        {
            _drag = null;
            _ghostDragging = false;
            if (_ghost == null) { Rebuild(); return; }
            if (overRail || _ghost.Check == null) { CancelGhost(); return; }
            DropGhost();
        }

        internal void AlienBegin(CityDragSource src)
        {
            _drag = src;
            var img = SummonKit.Img(_overlay, ArtCatalog.Alien(src.AlienId) ?? HudArt.AlienCritter(Theme.Good), "DragAlien");
            _dragIcon = img.rectTransform;
            _dragIcon.anchorMin = _dragIcon.anchorMax = new Vector2(0, 1);
            _dragIcon.sizeDelta = new Vector2(130, 130);
        }

        internal void AlienDrag(Vector2 px)
        {
            if (_dragIcon != null) _dragIcon.anchoredPosition = px + new Vector2(0, 80);
            var uid = BuildingUnder(px + new Vector2(0, 80));
            var marks = new List<CityWorld.Mark>();
            var b = CitySystem.Building(State, uid);
            if (b != null)
            {
                var def = Content.Blueprint(b.BlueprintId);
                CityLayout.Footprint(def, b.Rotated, out int w, out int h);
                bool room = CitySystem.Workers(State, uid).Count < CitySystem.WorkerSlots(State, Content, b) && CitySystem.IsBuilt(b);
                for (int y = b.Y; y < b.Y + h; y++)
                    for (int x = b.X; x < b.X + w; x++)
                        marks.Add(new CityWorld.Mark { X = x, Y = y, Color = room ? GoodTile : BadTile });
            }
            _world.SetMarks(marks);
        }

        internal void AlienEnd(Vector2 px, string alienId)
        {
            _drag = null;
            if (_dragIcon != null) Ui.DestroyGo(_dragIcon.gameObject);
            _dragIcon = null;
            _world.SetMarks(null);
            var uid = BuildingUnder(px + new Vector2(0, 80));
            if (uid != null) AssignAlien(alienId, uid);
            else Rebuild();
        }

        private string BuildingUnder(Vector2 px)
        {
            var c = CellAt(px);
            return BuildingAt(c.x, c.y) ?? BuildingAt(c.x + 1, c.y + 1);
        }

        internal bool OverRail(Vector2 screen, Camera cam)
        {
            var rail = FindRect(_overlay, "Rail");
            return rail != null && RectTransformUtility.RectangleContainsScreenPoint(rail, screen, cam);
        }

        // ---- edit UI ---------------------------------------------------------------------------------------------------------

        private static string Hint(CityTool t)
        {
            switch (t)
            {
                case CityTool.Street: return "Drag from the fireplace or a street to pave";
                case CityTool.Erase: return "Drag over streets to remove them";
                case CityTool.Store: return "Tap a building to put it back in the tray";
                default: return "Drag a building to move it · pick one from the rail to build";
            }
        }

        private void BuildEdit()
        {
            // top bar: title, hint, Done (no Save: every edit is already applied)
            var top = Ui.RoundPanel(_overlay, new Color(0.1f, 0.07f, 0.2f, 0.94f), 32, "EditBar");
            var trt = top.rectTransform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1);
            trt.sizeDelta = new Vector2(-28, 112); trt.anchoredPosition = new Vector2(0, -14);
            var title = SliceKit.Chunky(top.transform, "Build mode", 44, Color.white, TextAnchor.MiddleLeft, "Title");
            SummonKit.Place(title, new Vector2(0, 0.5f), new Vector2(28, 14), new Vector2(420, 60));
            var hint = Ui.Label(top.transform, Hint(_tool), 22, Theme.Cream, TextAnchor.MiddleLeft, "Hint");
            SummonKit.Place(hint, new Vector2(0, 0.5f), new Vector2(28, -30), new Vector2(640, 34));
            var done = Ui.Button(top.transform, "Done", ExitEdit, Theme.Hex(0xF6C94A), 190, 84, true, 34);
            done.name = "EditDone";
            SummonKit.Place((RectTransform)done.transform, new Vector2(1, 0.5f), new Vector2(-124, 0), new Vector2(190, 84)); // left of the "?"

            // the bottom toolbar
            var bar = Ui.Rect(_overlay, "EditTools");
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0); bar.pivot = new Vector2(0.5f, 0);
            bar.sizeDelta = new Vector2(0, 160); bar.anchoredPosition = new Vector2(0, 12);
            var row = Ui.Row(bar, 160, 10, null, 0, "Row");
            Ui.Stretch(row, 16, 0, 16, 0);
            var hl = row.GetComponent<HorizontalLayoutGroup>();
            hl.childForceExpandWidth = true; hl.childAlignment = TextAnchor.MiddleCenter;
            ToolButton(row, "↶", "Undo" + (Undo.Count > 0 ? " " + Undo.Count : ""), UndoLast, "EditUndo", false, Undo.Count > 0);
            ToolButton(row, "⟳", "Rotate", RotateGhost, "EditRotate", false, _ghost != null && Content.Blueprint(_ghost.BlueprintId)?.Width != Content.Blueprint(_ghost.BlueprintId)?.Height);
            ToolButton(row, "▦", "Street", () => SetTool(CityTool.Street), "Tool:" + CityTool.Street, _tool == CityTool.Street, true);
            ToolButton(row, "⌫", "Erase", () => SetTool(CityTool.Erase), "Tool:" + CityTool.Erase, _tool == CityTool.Erase, true);
            ToolButton(row, "✥", "Move", () => SetTool(CityTool.Move), "Tool:" + CityTool.Move, _tool == CityTool.Move, true);
            ToolButton(row, "▣", "Store", () => SetTool(CityTool.Store), "Tool:" + CityTool.Store, _tool == CityTool.Store, true);

            Rail(true);
            if (_ghost != null) GhostChipPin();
        }

        private void ToolButton(Transform row, string glyph, string caption, Action onClick, string name, bool active, bool enabled)
        {
            var b = Ui.Button(row, "", onClick, active ? Theme.Hex(0x7C5CE0) : new Color(0.16f, 0.12f, 0.3f, 0.95f), -1, 140, enabled, 20);
            b.name = name;
            var g = SliceKit.Chunky(b.transform, glyph, 54, Color.white, TextAnchor.MiddleCenter, "Glyph");
            SummonKit.Place(g, new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(120, 70));
            var t = SliceKit.Chunky(b.transform, caption, 26, Color.white, TextAnchor.MiddleCenter, "Caption");
            SummonKit.Place(t, new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(150, 36));
        }

        /// <summary>The ghost's chip: the reason or the bonus; a waiting ghost adds ✓ (when valid), Store for a picked-up building and ✕.</summary>
        private void GhostChipPin()
        {
            var bg = Ui.RoundPanel(_pinLayer, _ghost.Ok ? new Color(0.12f, 0.45f, 0.24f, 0.95f) : new Color(0.62f, 0.14f, 0.16f, 0.95f), 26, "GhostChip");
            _chipBg = bg;
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(560, 66);
            _chipPin = _pins.Count;
            _pins.Add(new Pin { Rt = rt, World = GhostFoot(), Offset = new Vector2(0, -0.62f * _size.x / _width), Clamp = true });
            var t = SliceKit.Chunky(bg.transform, _ghost.Chip ?? "", 28, Color.white, TextAnchor.MiddleCenter, "Text");
            Ui.Stretch(t.rectTransform, 12, 0, 12, 0);
            _chipText = t;
            if (_ghostDragging) return;
            var buttons = Ui.Row(bg.transform, 76, 10, null, 0, "Buttons");
            buttons.anchorMin = new Vector2(0, 0); buttons.anchorMax = new Vector2(1, 0); buttons.pivot = new Vector2(0.5f, 1);
            buttons.sizeDelta = new Vector2(0, 76); buttons.anchoredPosition = new Vector2(0, -8);
            buttons.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            buttons.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            if (_ghost.Ok) Ui.Button(buttons, "✓ " + (_ghost.Move ? "Move here" : _ghost.Uid != null ? "Place" : "Build"), () => DropGhost(), Theme.Good, 230, 72, true, 28).name = "GhostOk";
            if (_ghost.Move) Ui.Button(buttons, "Store", () => StoreBuilding(_ghost.Uid), Theme.Bad, 150, 72, true, 28).name = "GhostStore";
            Ui.Button(buttons, "✕", CancelGhost, new Color(0.25f, 0.2f, 0.4f, 1), 80, 72, true, 30).name = "GhostCancel";
        }

        /// <summary>
        /// C's side rail: Streets first, then stored copies (free) and new copies (cost, copies placed "2/3"; over the cap the card dims
        /// and says "3/3 placed"). Tap a card = the ghost on a suggested spot; drag it onto the land = the ghost under the finger.
        /// </summary>
        private void Rail(bool edit)
        {
            var rail = Ui.RoundPanel(_overlay, new Color(0.08f, 0.06f, 0.16f, 0.9f), 30, "Rail");
            var rrt = rail.rectTransform;
            float top = edit ? 140 : 130, bottom = edit ? 186 : 204;
            rrt.anchorMin = new Vector2(1, 0); rrt.anchorMax = new Vector2(1, 1); rrt.pivot = new Vector2(1, 0.5f);
            rrt.offsetMin = new Vector2(-226, bottom); rrt.offsetMax = new Vector2(-12, -top);
            var view = Ui.Rect(rail.transform, "Viewport");
            Ui.Stretch(view, 8, 10, 8, 10);
            view.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.content = content; sr.horizontal = false; sr.vertical = true; sr.viewport = view; sr.movementType = ScrollRect.MovementType.Clamped;
            var vl = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 10; vl.childControlWidth = true; vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RailCard(content, sr, "Tray:streets", null, null, null, "Streets", "free", "∞", true, _tool == CityTool.Street);
            foreach (var ob in CitySystem.SortedBlueprints(State))
            {
                var def = Content.Blueprint(ob.Id);
                if (def == null) continue;
                foreach (var copy in CitySystem.Copies(State, def.Id))
                    if (!copy.Placed)
                        RailCard(content, sr, "Tray:stored:" + copy.Uid, def, def.Id, copy.Uid, def.Name, "stored · Lv " + copy.Level, null, true, _ghost != null && _ghost.Uid == copy.Uid);
                int built = CitySystem.CopiesBuilt(State, def.Id);
                bool room = built < def.Copies;
                var cost = CitySystem.BuildCost(Content, def);
                string costText = Fmt.Costs(cost, Content);
                RailCard(content, sr, "Tray:new:" + def.Id, def, def.Id, null, def.Name, room ? costText : built + "/" + def.Copies + " placed",
                    built + "/" + def.Copies, room && State.Wallet.CanAfford(cost), _ghost != null && _ghost.Uid == null && !_ghost.Move && _ghost.BlueprintId == def.Id);
            }
        }

        private void RailCard(Transform parent, ScrollRect scroll, string name, BlueprintDef def, string bp, string uid, string title, string sub, string count, bool enabled, bool active)
        {
            var card = Ui.RoundPanel(parent, active ? Theme.Hex(0x5A44B0) : enabled ? new Color(0.98f, 0.94f, 0.84f, 1) : new Color(0.45f, 0.42f, 0.5f, 1), 22, name);
            Ui.Size(card, -1, 226);
            if (def != null) UiShapes.Outline(card.rectTransform, Theme.RarityColor(def.Rarity), 22, 5);
            var art = SummonKit.Img(card.transform, def != null ? CityArt.Building(def) : CityArt.Tile(), "Art");
            SummonKit.Place(art, new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(120, 104));
            if (def == null) art.color = new Color32(0xE6, 0xCC, 0x94, 255);
            var dark = active ? Color.white : new Color(0.16f, 0.1f, 0.2f);
            var tt = Ui.Label(card.transform, title, 22, dark, TextAnchor.MiddleCenter, "Title");
            tt.fontStyle = FontStyle.Bold;
            tt.horizontalOverflow = HorizontalWrapMode.Wrap;
            SummonKit.Place(tt, new Vector2(0.5f, 0), new Vector2(0, 52), new Vector2(190, 54));
            string foot = def != null ? def.Width + "×" + def.Height + " · " : "";
            var st = Ui.Label(card.transform, foot + sub, 17, active ? Color.white : new Color(0.35f, 0.28f, 0.36f), TextAnchor.MiddleCenter, "Sub");
            st.horizontalOverflow = HorizontalWrapMode.Wrap;
            SummonKit.Place(st, new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(194, 46));
            if (count != null)
            {
                var chip = Ui.RoundPanel(card.transform, new Color(0.12f, 0.08f, 0.2f, 0.9f), 14, "Count");
                SummonKit.Place(chip, new Vector2(1, 1), new Vector2(-6, -6), new Vector2(64, 34));
                var ct = Ui.Label(chip.transform, count, 20, Color.white, TextAnchor.MiddleCenter, "Text");
                Ui.Stretch(ct.rectTransform);
            }
            if (!enabled && def != null) return; // over the cap / can't afford: dimmed, not draggable
            var src = card.gameObject.AddComponent<CityDragSource>();
            src.Screen = this; src.BlueprintId = bp; src.Uid = uid; src.Scroll = scroll; src.Horizontal = false;
        }
    }

    /// <summary>
    /// The diorama's gestures (on the city holder; the view and the pins bubble up here): one finger pans (or, in edit mode, drags the
    /// ghost / paints a street), two fingers pinch-zoom (settling on the two zoom stops), the wheel zooms, a tap selects, a long press
    /// on a building enters edit mode, a double tap centres.
    /// </summary>
    public sealed class CityInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, IPointerClickHandler
    {
        public CityScreen Screen;
        public RectTransform Space;
        public const float LongPressSeconds = 0.5f;
        private readonly Dictionary<int, Vector2> _pointers = new Dictionary<int, Vector2>();
        private bool _editDrag, _pinched, _longFired, _moved;
        private float _downAt;
        private Vector2 _downPos;
        private float _lastClick = -9;
        private Vector2 _lastClickPos;

        private Vector2 Local(Vector2 screen, Camera cam)
        {
            if (Space == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(Space, screen, cam, out var local)) return screen;
            var r = Space.rect;
            return new Vector2(local.x - r.xMin, local.y - r.yMax);
        }

        public void OnPointerDown(PointerEventData e)
        {
            var p = Local(e.position, e.pressEventCamera);
            _pointers[e.pointerId] = p;
            if (_pointers.Count == 1) { _downAt = Time.unscaledTime; _downPos = p; _longFired = false; _moved = false; _pinched = false; }
        }

        public void OnPointerUp(PointerEventData e)
        {
            _pointers.Remove(e.pointerId);
            if (_pointers.Count == 0 && _pinched) { _pinched = false; Screen?.SettleZoom(); }
        }

        private void Update()
        {
            if (Screen == null || _pointers.Count != 1 || _moved || _longFired) return;
            if (Time.unscaledTime - _downAt < LongPressSeconds) return;
            _longFired = true;
            Screen.LongPress(_downPos);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (Screen == null) return;
            _moved = true;
            var p = Local(e.position, e.pressEventCamera);
            if (!_pointers.ContainsKey(e.pointerId)) _pointers[e.pointerId] = Local(e.position - e.delta, e.pressEventCamera);
            if (_pointers.Count >= 2) { if (_editDrag) { Screen.DragEnd(p); _editDrag = false; } return; }
            _editDrag = Screen.DragBegin(_pointers[e.pointerId]);
            if (_editDrag) Screen.DragMove(p);
        }

        public void OnDrag(PointerEventData e)
        {
            if (Screen == null) return;
            var cur = Local(e.position, e.pressEventCamera);
            var prev = _pointers.TryGetValue(e.pointerId, out var pp) ? pp : Local(e.position - e.delta, e.pressEventCamera);
            _pointers[e.pointerId] = cur;
            if (_editDrag && _pointers.Count == 1) { Screen.DragMove(cur); return; }
            Vector2? other = null;
            foreach (var kv in _pointers) if (kv.Key != e.pointerId) { other = kv.Value; break; }
            if (other.HasValue)
            {
                _pinched = true;
                var o = other.Value;
                float d0 = Vector2.Distance(prev, o), d1 = Vector2.Distance(cur, o);
                float zoom = d0 > 24 && d1 > 24 ? d1 / d0 : 1;
                Screen.MoveView((prev + o) / 2, (cur + o) / 2, zoom);
            }
            else Screen.MoveView(prev, cur, 1);
        }

        public void OnEndDrag(PointerEventData e)
        {
            var p = Local(e.position, e.pressEventCamera);
            _pointers.Remove(e.pointerId);
            if (_editDrag) { _editDrag = false; Screen?.DragEnd(p); }
        }

        public void OnScroll(PointerEventData e)
        {
            if (Screen == null || Mathf.Abs(e.scrollDelta.y) < 0.01f) return;
            var at = Local(e.position, e.enterEventCamera);
            Screen.MoveView(at, at, Mathf.Pow(1.12f, e.scrollDelta.y));
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (Screen == null || e.dragging || _longFired || _moved) return;
            var p = Local(e.position, e.pressEventCamera);
            if (Time.unscaledTime - _lastClick < 0.3f && Vector2.Distance(p, _lastClickPos) < 50) { _lastClick = -9; Screen.DoubleTap(p); return; }
            _lastClick = Time.unscaledTime;
            _lastClickPos = p;
            Screen.Tap(p);
        }

        private void OnDisable() { _pointers.Clear(); _editDrag = false; }
    }

    /// <summary>
    /// A rail card (drag a building onto the land; Streets picks the street tool) or an alien chip of the Workers strip (drag it onto
    /// a building). Drags along the strip's own scroll direction scroll it instead.
    /// </summary>
    public sealed class CityDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IInitializePotentialDragHandler
    {
        public CityScreen Screen;
        public string BlueprintId, Uid, AlienId;
        public ScrollRect Scroll;
        /// <summary>The scroll runs horizontally (the alien strip) or vertically (the rail).</summary>
        public bool Horizontal;
        private bool _own, _forward;

        public void OnInitializePotentialDrag(PointerEventData e) { if (Scroll != null) Scroll.OnInitializePotentialDrag(e); }

        public void OnBeginDrag(PointerEventData e)
        {
            if (Screen == null) return;
            var d = e.delta;
            bool along = Horizontal ? Mathf.Abs(d.x) >= Mathf.Abs(d.y) : Mathf.Abs(d.y) >= Mathf.Abs(d.x);
            if (along && Scroll != null) { _forward = true; Scroll.OnBeginDrag(e); return; }
            if (AlienId == null && BlueprintId == null) { _forward = Scroll != null; if (_forward) Scroll.OnBeginDrag(e); return; }
            _own = true;
            if (AlienId != null) Screen.AlienBegin(this); else Screen.TrayBegin(this);
        }

        public void OnDrag(PointerEventData e)
        {
            if (_forward) { Scroll.OnDrag(e); return; }
            if (!_own) return;
            var p = Screen.ToHolder(e.position, e.pressEventCamera);
            if (AlienId != null) Screen.AlienDrag(p); else Screen.TrayDrag(p);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_forward) { _forward = false; Scroll.OnEndDrag(e); return; }
            if (!_own) return;
            _own = false;
            var p = Screen.ToHolder(e.position, e.pressEventCamera);
            if (AlienId != null) Screen.AlienEnd(p, AlienId);
            else Screen.TrayEnd(p, Screen.OverRail(e.position, e.pressEventCamera));
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (Screen == null || e.dragging) return;
            if (AlienId != null) { Screen.App.Feedback.Tip("Drag it onto a building", (RectTransform)transform); return; }
            Screen.TrayTap(BlueprintId, Uid);
        }
    }
}
