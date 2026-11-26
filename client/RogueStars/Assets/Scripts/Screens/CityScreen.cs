using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// The City (City design, version A): the whole screen is a living iso diorama (<see cref="CityWorld"/> in a RenderTexture under
    /// the HUD) with a slim dock — Build, Workers, Output and a big COLLECT ALL. Tapping a building opens its card in place (level,
    /// stars, placement bonus and its reason, worker slots, output or boon, upgrade with its cost type); tapping a rubble patch opens
    /// the dig pop-up; pull buildings show their stored chances and pop through the shared pull reveal. Build (or a long press on a
    /// building) enters edit mode (<c>CityScreen.Edit.cs</c>): C's side rail, every edit applied at once, undo up to 50 steps.
    /// Workers: drag an alien from the strip onto a building. Ready goods float over buildings as bubbles; unstaffed ones sleep (Zz).
    /// </summary>
    public sealed partial class CityScreen : UiScreen
    {
        public override string Title => "City";
        public override float HeaderHeight => 0;
        public override string[] IntroFeatures => new[] { Features.BiomassFarm, Features.GrainFarm, Features.OreMine, Features.AlienSquad };

        private CityWorld _world;
        private RectTransform _holder, _overlay, _pinLayer;
        private Vector2 _size;
        private Vector2 _cam = new Vector2(float.NaN, float.NaN);
        private float _width = CityWorld.OverviewWidth, _targetWidth = CityWorld.OverviewWidth;
        private string _selected;
        private string _digPatch;
        private bool _workersOpen;
        private readonly Queue<GameEvent> _events = new Queue<GameEvent>();
        private bool _subscribed;
        private bool _popping;
        private readonly Dictionary<string, long> _popFly = new Dictionary<string, long>();
        private float _deliveredTick;

        private struct Pin
        {
            public RectTransform Rt;
            public Vector2 World;
            public Vector2 Offset;
            /// <summary>Bounce amplitude (px): full storage, Zz (pins move with the camera, so the bounce lives here).</summary>
            public float Bob;
            public float Phase;
            /// <summary>Keep the whole rect inside the view (left of the rail): the ghost's chip.</summary>
            public bool Clamp;
        }

        private readonly List<Pin> _pins = new List<Pin>();

        /// <summary>The diorama (tests, showcase).</summary>
        public CityWorld World => _world;
        /// <summary>Phones show the build rail in edit mode only; tablets always (tests force either).</summary>
        public bool? ForceTablet;
        public bool IsTablet => ForceTablet ?? (_size.x > 0 && _size.y > 0 && _size.x / _size.y > 0.62f);
        public string Selected => _selected;
        public string DigPatch => _digPatch;
        public bool WorkersOpen => _workersOpen;

        /// <summary>
        /// Camp mode (levels 6-14, before the City feature): only the crash-site plaza with the fireplace, the saucer and the Launch Pod on
        /// the start land. No digging, building, streets, workers on other buildings or output; the pod's card shows only its seats.
        /// </summary>
        public bool CampMode => Session != null && !Session.IsUnlocked(Features.BiomassFarm);

        // ---- building ----------------------------------------------------------------------------------------------------

        protected override void Build(RectTransform body)
        {
            var bl = body.GetComponent<VerticalLayoutGroup>();
            if (bl != null) { bl.padding = new RectOffset(0, 0, 0, 0); bl.spacing = 0; }
            var scroll = Root.GetComponentInChildren<ScrollRect>(true);
            if (scroll != null) scroll.vertical = false;
            float frameH = BodyFrame != null && BodyFrame.rect.height > 100 ? BodyFrame.rect.height : 1750;
            float frameW = BodyFrame != null && BodyFrame.rect.width > 100 ? BodyFrame.rect.width : 1080;
            _size = new Vector2(frameW, frameH);
            _pins.Clear();

            _holder = Ui.Rect(body, "CityHolder");
            Ui.Size(_holder, -1, frameH);
            var input = _holder.gameObject.AddComponent<CityInput>();
            input.Screen = this;
            input.Space = _holder;

            if (_world == null || _world.Disposed)
            {
                _world = new CityWorld(Content);
                var life = Root.gameObject.GetComponent<WorldLifetime>(); if (life == null) life = Root.gameObject.AddComponent<WorldLifetime>();
                var w = _world;
                life.OnGone = () => w.Dispose();
                _world.Delivered += OnDelivered;
            }
            var canvas = Root.GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.rootCanvas != null ? canvas.rootCanvas.scaleFactor : 1;
            if (scale <= 0) scale = 1;
            var rt = _world.EnsureTarget(Mathf.RoundToInt(frameW * scale), Mathf.RoundToInt(frameH * scale));
            var view = Ui.Rect(_holder, "CityView");
            Ui.Stretch(view);
            var raw = view.gameObject.AddComponent<RawImage>();
            raw.texture = rt;
            raw.color = Color.white;

            _world.Sync(State, Content, Now, _edit);
            if (float.IsNaN(_cam.x))
            {
                var t = Content.CityTuning;
                _cam = _world.Pos(t.FireX, t.FireY) + new Vector2(0, -0.6f);
            }
            ApplyCamera();

            _overlay = Ui.Rect(_holder, "CityOverlay");
            Ui.Stretch(_overlay);
            _pinLayer = Ui.Rect(_overlay, "Pins");
            Ui.Stretch(_pinLayer);

            BuildingPins();
            Hud();
            if (_edit) BuildEdit();
            else
            {
                if (CampMode)
                {
                    if (_selected != null) Card();
                }
                else
                {
                    Dock();
                    if (IsTablet) Rail(false);
                    if (_workersOpen) WorkersStrip();
                    if (_selected != null) Card();
                    if (_digPatch != null) DigPopup();
                }
            }
            UpdatePins();
        }

        public override void OnShow()
        {
            _world?.SetActive(true);
            if (!_subscribed && Session != null) { Session.EventRaised += OnEvent; _subscribed = true; }
        }

        public override void OnHide()
        {
            _world?.SetActive(false);
            if (_subscribed) { Session.EventRaised -= OnEvent; _subscribed = false; }
        }

        // ---- camera ------------------------------------------------------------------------------------------------------

        private void ApplyCamera()
        {
            if (_world == null) return;
            _cam = _world.ClampCentre(_cam);
            _world.SetView(_cam, _width);
        }

        /// <summary>Pans / zooms: the world point under <paramref name="from"/> goes under <paramref name="to"/> (holder px), <paramref name="zoom"/>× closer.</summary>
        internal void MoveView(Vector2 from, Vector2 to, float zoom)
        {
            if (_world == null) return;
            var before = _world.FromOverlay(from, _size);
            _width = Mathf.Clamp(_width / Mathf.Max(0.05f, zoom), CityWorld.MinWidth, CityWorld.MaxWidth);
            _targetWidth = _width;
            _world.SetView(_cam, _width);
            var after = _world.FromOverlay(to, _size);
            _cam += before - after;
            ApplyCamera();
            UpdatePins();
        }

        /// <summary>Two zoom stops: a pinch settles on the closer one.</summary>
        internal void SettleZoom()
        {
            _targetWidth = Mathf.Abs(_width - CityWorld.EditWidth) < Mathf.Abs(_width - CityWorld.OverviewWidth) ? CityWorld.EditWidth : CityWorld.OverviewWidth;
        }

        /// <summary>Centres the camera on map point (x, y), optionally at a zoom width (showcase, "find" buttons).</summary>
        public void Focus(float x, float y, float width = 0)
        {
            if (_world == null) return;
            _cam = _world.Pos(x, y);
            if (width > 0) { _width = _targetWidth = width; }
            ApplyCamera();
            UpdatePins();
        }

        /// <summary>Double tap: centre there and switch zoom stop.</summary>
        internal void DoubleTap(Vector2 px)
        {
            _cam = _world.FromOverlay(px, _size);
            _targetWidth = _targetWidth > (CityWorld.EditWidth + CityWorld.OverviewWidth) / 2 ? CityWorld.EditWidth : CityWorld.OverviewWidth;
            ApplyCamera();
        }

        public override void Frame(float dt)
        {
            if (_world == null || _world.Disposed) return;
            if (Mathf.Abs(_width - _targetWidth) > 0.01f)
            {
                _width = Mathf.Lerp(_width, _targetWidth, 1 - Mathf.Exp(-10 * dt));
                ApplyCamera();
            }
            _world.SetTime(Now);
            while (_events.Count > 0) Play(_events.Dequeue());
            UpdatePins();
            _deliveredTick -= dt;
        }

        // ---- pins (uGUI over the diorama) ---------------------------------------------------------------------------------

        private RectTransform PinRect(string name, Vector2 world, Vector2 offset, Vector2 size, Transform parent = null)
        {
            var rt = Ui.Rect(parent ?? _pinLayer, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            _pins.Add(new Pin { Rt = rt, World = world, Offset = offset });
            return rt;
        }

        private void UpdatePins()
        {
            if (_world == null) return;
            for (int i = 0; i < _pins.Count; i++)
            {
                var p = _pins[i];
                if (p.Rt == null) continue;
                var at = _world.ToOverlay(p.World, _size) + p.Offset;
                if (p.Bob > 0 && Application.isPlaying) at.y += Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.2f + p.Phase)) * p.Bob;
                if (p.Clamp)
                {
                    float hw = p.Rt.sizeDelta.x / 2, right = _size.x - ((_edit || IsTablet) ? 240 : 12);
                    at.x = Mathf.Clamp(at.x, hw + 12, Mathf.Max(hw + 12, right - hw));
                }
                p.Rt.anchoredPosition = at;
            }
        }

        /// <summary>A holder px (top-left origin, y down) as a canvas world position (fly-to sources).</summary>
        private Vector3 HolderToWorld(Vector2 px)
        {
            var r = _holder.rect;
            return _holder.TransformPoint(new Vector3(r.xMin + px.x, r.yMax + px.y, 0));
        }

        /// <summary>
        /// Per building: a tap target "Building:uid" (tests; players tap the diorama), the status over it: a timer while building or
        /// upgrading, stored chances on pull buildings (tap = pop), a goods bubble when something is ready (bouncing when full, tap =
        /// collect), Zz when nobody works there; the level in edit mode.
        /// </summary>
        private void BuildingPins()
        {
            foreach (var b in State.City.Buildings)
            {
                if (!b.Placed) continue;
                var anchor = _world.PinAnchor(b.Uid);
                if (!anchor.HasValue) continue;
                var def = Content.Blueprint(b.BlueprintId);
                string uid = b.Uid;
                var pin = PinRect("Building:" + uid, anchor.Value, Vector2.zero, new Vector2(10, 10));
                var tap = pin.gameObject.AddComponent<Button>();
                tap.transition = Selectable.Transition.None;
                tap.onClick.AddListener(() => Select(uid));

                if (b.TimerId != null)
                {
                    var t = TimerSystem.Get(State, b.TimerId);
                    var chip = Ui.RoundPanel(_pinLayer, new Color(0.1f, 0.07f, 0.18f, 0.88f), 22, "Timer:" + uid);
                    chip.raycastTarget = false;
                    var crt = chip.rectTransform;
                    crt.anchorMin = crt.anchorMax = new Vector2(0, 1); crt.sizeDelta = new Vector2(150, 44);
                    _pins.Add(new Pin { Rt = crt, World = anchor.Value, Offset = new Vector2(0, 10) });
                    var label = SliceKit.Chunky(chip.transform, "", 26, Theme.Gold, TextAnchor.MiddleCenter, "Text");
                    Ui.Stretch(label.rectTransform);
                    Live(label, () =>
                    {
                        var cur = TimerSystem.Get(State, b.TimerId);
                        return cur == null ? "" : (b.Level == 0 ? "⚒ " : "▲ ") + Fmt.Duration(TimerSystem.Remaining(cur, Now));
                    });
                    continue;
                }
                if (_edit) { LevelPin(b, anchor.Value); continue; }
                if (def?.Pull != null && CitySystem.IsBuilt(b))
                {
                    int ready = CitySystem.ChancesReady(b), cap = CitySystem.ChanceStorage(Content, def, b.Level);
                    if (ready >= 1)
                    {
                        var btn = StatusButton("Chance:" + uid, anchor.Value, SummonArt.CurrencyIcon(def.Pull.TokenCurrency), ready + "/" + cap,
                            Theme.RarityColor(Rarity.Legendary), () => PopChances(uid, false), ready >= cap);
                        btn.name = "Chance:" + uid;
                    }
                }
                else if (def?.ProduceCurrency != null && CitySystem.IsBuilt(b) && b.Stored >= 1 && RewardGate.CurrencyHasUse(State, Content, def.ProduceCurrency, Now))
                {
                    bool full = b.Stored >= CitySystem.StorageCap(State, Content, b) - 1e-6;
                    StatusButton("Bubble:" + uid, anchor.Value, SummonArt.CurrencyIcon(def.ProduceCurrency), Fmt.Num(Math.Floor(b.Stored)),
                        Theme.CurrencyColor(def.ProduceCurrency), () => Run(new CityCollect { Uid = uid }), full);
                }
                if (CitySystem.IsBuilt(b) && !CitySystem.IsActive(State, Content, b))
                {
                    var zz = SliceKit.Chunky(_pinLayer, "Zz", 40, new Color(0.85f, 0.9f, 1f), TextAnchor.MiddleCenter, "Zz:" + uid);
                    var zrt = zz.rectTransform;
                    zrt.anchorMin = zrt.anchorMax = new Vector2(0, 1); zrt.sizeDelta = new Vector2(90, 60);
                    _pins.Add(new Pin { Rt = zrt, World = anchor.Value, Offset = new Vector2(46, 18), Bob = 5, Phase = _pins.Count });
                }
            }
        }

        /// <summary>Edit mode: just the level over each building (no goods, no Zz).</summary>
        private void LevelPin(CityBuilding b, Vector2 anchor)
        {
            if (b.Level < 1) return;
            var lv = Ui.RoundPanel(_pinLayer, new Color(0.1f, 0.07f, 0.18f, 0.85f), 18, "Level:" + b.Uid);
            lv.raycastTarget = false;
            var lrt = lv.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0, 1); lrt.sizeDelta = new Vector2(96, 40);
            _pins.Add(new Pin { Rt = lrt, World = anchor, Offset = new Vector2(0, 4) });
            var lt = SliceKit.Chunky(lv.transform, "Lv " + b.Level, 24, Color.white, TextAnchor.MiddleCenter, "Text");
            Ui.Stretch(lt.rectTransform);
        }

        /// <summary>A round status bubble over a building: icon + count; bounces when full.</summary>
        private Button StatusButton(string name, Vector2 world, Sprite icon, string count, Color ring, Action onClick, bool full)
        {
            var btn = Ui.Button(_pinLayer, "", onClick, new Color(1, 1, 1, 0.96f), 104, 104, true, 20);
            btn.name = name;
            var brt = (RectTransform)btn.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0, 1);
            brt.pivot = new Vector2(0.5f, 0);
            brt.sizeDelta = new Vector2(104, 104);
            _pins.Add(new Pin { Rt = brt, World = world, Offset = new Vector2(0, 6), Bob = full ? 16 : 0, Phase = _pins.Count });
            UiShapes.Outline(brt, ring, 26, 6);
            var img = SummonKit.Img(btn.transform, icon, "Icon");
            SummonKit.Place(img, new Vector2(0.5f, 0.5f), new Vector2(0, 8), new Vector2(64, 64));
            var t = SliceKit.Chunky(btn.transform, count, 26, Color.white, TextAnchor.MiddleCenter, "Count");
            SummonKit.Place(t, new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(104, 34));
            return btn;
        }

        // ---- HUD and dock ------------------------------------------------------------------------------------------------

        /// <summary>Production per hour of every running building, per currency (the HUD's "City +6.2k/h").</summary>
        public static SortedDictionary<string, double> Rates(GameState s, ContentDb c, long now)
        {
            var rates = new SortedDictionary<string, double>(StringComparer.Ordinal);
            foreach (var b in s.City.Buildings)
            {
                var def = c.Blueprint(b.BlueprintId);
                if (def?.ProduceCurrency == null || !CitySystem.IsActive(s, c, b)) continue;
                rates[def.ProduceCurrency] = (rates.TryGetValue(def.ProduceCurrency, out var r) ? r : 0) + CitySystem.ProductionPerHour(s, c, b, now);
            }
            return rates;
        }

        private void Hud()
        {
            if (_edit) return; // edit mode has its own top bar
            var back = Ui.Button(_overlay, "<", () => App.Router.Pop(), new Color(0.12f, 0.1f, 0.22f, 0.92f), 96, 96, true, Theme.FontTitle);
            back.name = "BackButton";
            SummonKit.Place((RectTransform)back.transform, new Vector2(0, 1), new Vector2(20, -16), new Vector2(96, 96));
            var pill = Ui.RoundPanel(_overlay, new Color(0.1f, 0.07f, 0.2f, 0.86f), 40, "CityRates");
            pill.raycastTarget = false;
            SummonKit.Place(pill, new Vector2(0, 1), new Vector2(128, -20), new Vector2(560, 88));
            var row = Ui.Row(pill.transform, 88, 10, null, 0, "Row");
            Ui.Stretch(row, 22, 0, 18, 0);
            var hl = row.GetComponent<HorizontalLayoutGroup>();
            hl.childForceExpandWidth = false; hl.childAlignment = TextAnchor.MiddleLeft;
            var title = SliceKit.Chunky(row, CampMode ? "Camp" : "City", 40, Color.white, TextAnchor.MiddleLeft, "Title");
            Ui.Size(title, 100, 88, 0);
            int shown = 0;
            foreach (var kv in CampMode ? new SortedDictionary<string, double>() : Rates(State, Content, Now))
            {
                if (shown >= 2 || kv.Value <= 0) continue;
                if (kv.Key != Currencies.Biomass && kv.Key != Currencies.Ore && shown > 0) continue;
                var icon = SummonKit.Img(row, SummonArt.CurrencyIcon(kv.Key), "RateIcon:" + kv.Key);
                Ui.Size(icon, 40, 40, 0);
                var t = SliceKit.Chunky(row, "+" + Fmt.Num(kv.Value) + "/h", 30, Theme.CurrencyColor(kv.Key), TextAnchor.MiddleLeft, "Rate:" + kv.Key);
                Ui.Size(t, 150, 88, 0);
                shown++;
            }
            string phase = CityDayNight.Phase(Now);
            var sun = SliceKit.Chunky(_overlay, phase == "night" ? "☾" : phase == "day" ? "☀" : "◐", 44, phase == "night" ? new Color(0.8f, 0.85f, 1f) : Theme.Gold, TextAnchor.MiddleCenter, "DayPhase");
            SummonKit.Place(sun, new Vector2(1, 1), new Vector2(-136, -24), new Vector2(80, 80));
        }

        private void Dock()
        {
            float h = 176;
            var dock = Ui.Rect(_overlay, "Dock");
            dock.anchorMin = new Vector2(0, 0); dock.anchorMax = new Vector2(1, 0); dock.pivot = new Vector2(0.5f, 0);
            dock.sizeDelta = new Vector2(0, h); dock.anchoredPosition = new Vector2(0, 12);
            var row = Ui.Row(dock, h, 14, null, 0, "Row");
            Ui.Stretch(row, 20, 0, 20, 0);
            var hl = row.GetComponent<HorizontalLayoutGroup>();
            hl.childForceExpandWidth = false; hl.childAlignment = TextAnchor.MiddleCenter;
            DockButton(row, "Build", "build", EnterEdit, "Dock:Build", 0);
            int idle = 0;
            foreach (var a in State.Aliens.Owned.Values) if (string.IsNullOrEmpty(a.WorkBuildingId)) idle++;
            DockButton(row, "Workers", "workers", () => { _workersOpen = !_workersOpen; _selected = null; _digPatch = null; Rebuild(); }, "Dock:Workers", idle, _workersOpen);
            DockButton(row, "Output", "output", OutputSheet, "Dock:Output", 0);
            bool any = false;
            foreach (var b in State.City.Buildings) if (CitySystem.IsBuilt(b) && b.Stored >= 1 && Content.Blueprint(b.BlueprintId)?.ProduceCurrency != null) any = true;
            var collect = Ui.Button(row, "COLLECT\n<size=26>ALL</size>", () => Run(new CityCollect()), Theme.Hex(0xF6C94A), 300, 150, any, 40);
            collect.name = "CollectAll";
        }

        private void DockButton(Transform row, string caption, string icon, Action onClick, string name, int badge, bool active = false)
        {
            var b = Ui.Button(row, "", onClick, active ? Theme.Hex(0x7C5CE0) : new Color(0.16f, 0.12f, 0.3f, 0.95f), 150, 150, true, 20);
            b.name = name;
            var img = SummonKit.Img(b.transform, DockIcon(icon), "Icon");
            SummonKit.Place(img, new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(76, 76));
            var t = SliceKit.Chunky(b.transform, caption, 28, Color.white, TextAnchor.MiddleCenter, "Caption");
            SummonKit.Place(t, new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(150, 40));
            if (badge > 0) SummonKit.Badge(b.transform, badge, 52);
        }

        private static Sprite DockIcon(string id)
        {
            switch (id)
            {
                case "build": return ScreenArt.HubIcon("tab_city");
                case "workers": return ArtCatalog.Alien("alien_blorp") ?? HudArt.AlienCritter(Theme.Good);
                case "output": return SummonArt.CurrencyIcon(Currencies.Biomass);
                default: return ScreenArt.HubIcon(id);
            }
        }

        // ---- selection / hit testing ---------------------------------------------------------------------------------------

        /// <summary>Select a placed building (its card in place), as a tap on it does; null closes the card.</summary>
        public void Select(string uid)
        {
            if (_edit) { SelectEdit(uid); return; }
            _selected = _selected == uid ? null : uid;
            _digPatch = null;
            // the card opens above the building: bring the building into the lower part of the view
            var foot = _selected != null && _world != null ? _world.FootOf(uid) : null;
            if (foot.HasValue) { _cam = foot.Value + new Vector2(0, _world.ViewHeight * 0.16f); ApplyCamera(); }
            Rebuild();
        }

        /// <summary>The map cell under a holder point.</summary>
        internal Vector2Int CellAt(Vector2 px) => CityWorld.CellOf(_world.ToGrid(_world.FromOverlay(px, _size)));

        internal int CellIndex(Vector2Int c) => c.x < 0 || c.y < 0 || c.x >= Content.CityTuning.GridWidth || c.y >= Content.CityTuning.GridHeight ? -1 : CityLayout.Cell(Content, c.x, c.y);

        /// <summary>The placed building on a cell, or null.</summary>
        public string BuildingAt(int x, int y)
        {
            foreach (var b in State.City.Buildings)
            {
                if (!b.Placed) continue;
                var def = Content.Blueprint(b.BlueprintId);
                if (def == null) continue;
                CityLayout.Footprint(def, b.Rotated, out int w, out int h);
                if (x >= b.X && x < b.X + w && y >= b.Y && y < b.Y + h) return b.Uid;
            }
            return null;
        }

        /// <summary>The undug patch on a cell, or null.</summary>
        public CityPatchDef PatchAt(int x, int y)
        {
            int cell = CityLayout.Cell(Content, x, y);
            if (x < 0 || y < 0 || x >= Content.CityTuning.GridWidth || y >= Content.CityTuning.GridHeight || CityLayout.IsLand(State, Content, cell)) return null;
            foreach (var p in Content.CityPatches) if (p.Cells.Contains(cell)) return p;
            return null;
        }

        /// <summary>A tap on the diorama (normal mode): a building opens its card, rubble its dig pop-up, anything else closes them.</summary>
        internal void Tap(Vector2 px)
        {
            var c = CellAt(px);
            if (_edit) { EditTap(c); return; }
            // the building's sprite stands above its footprint: test the tile under the finger and the one below it
            string uid = BuildingAt(c.x, c.y) ?? BuildingAt(c.x + 1, c.y + 1);
            if (uid != null) { Select(uid); return; }
            var patch = CampMode ? null : PatchAt(c.x, c.y);
            if (patch != null) { OpenDig(patch.Id); return; }
            if (_selected != null || _digPatch != null || _workersOpen) { _selected = null; _digPatch = null; _workersOpen = false; Rebuild(); }
        }

        internal void LongPress(Vector2 px)
        {
            if (_edit || CampMode) return;
            var c = CellAt(px);
            string uid = BuildingAt(c.x, c.y) ?? BuildingAt(c.x + 1, c.y + 1);
            if (uid == null) return;
            EnterEdit();
            StartMove(uid);
        }

        // ---- the building card ------------------------------------------------------------------------------------------------

        /// <summary>The placement line of a building: its bonus with the reason, or what it needs.</summary>
        public static string PlacementLine(GameState s, ContentDb c, CityBuilding b, out bool active)
        {
            var def = c.Blueprint(b.BlueprintId);
            active = false;
            if (def?.Placement == null) return null;
            double pct = CityLayout.PlacementPct(s, c, b.Uid), flat = CityLayout.PlacementFlat(s, c, b.Uid);
            if (def.Placement.Kind == PlacementKind.Aura) return def.Placement.Text;
            if (pct > 0 || flat > 0)
            {
                active = true;
                string chip = CityGhost.BonusChip(def, pct, flat);
                CityLayout.Bonuses(s, c).TryGetValue(b.Uid, out var bonus);
                bool aura = bonus != null && bonus.AuraFrom.Count > 0 && bonus.OwnPct <= 0;
                return chip + (aura ? " (aura)" : "") + "\n<size=22>" + def.Placement.Text + "</size>";
            }
            return "No bonus here\n<size=22>" + def.Placement.Text + "</size>";
        }

        /// <summary>A boon's value as the card shows it ("Max Stamina +6", "Stamina regen +12%").</summary>
        public static string BoonLine(BoonKind kind, double v)
        {
            string w = Fmt.Whole(Math.Floor(v + 1e-9)), p = Fmt.Pct(Math.Round(v, 1));
            switch (kind)
            {
                case BoonKind.StaminaCap: return "Max Stamina +" + w;
                case BoonKind.StaminaRegenPct: return "Stamina regen +" + p;
                case BoonKind.DraftRerolls: return "Draft rerolls +" + w;
                case BoonKind.SquadSlots: return "Squad slots " + w;
                case BoonKind.BreedingSlots: return "Breeding slots " + w;
                case BoonKind.BreedingSpeedPct: return "Breeding speed +" + p;
                case BoonKind.BreedingQualityPct: return "Gene quality +" + w;
                case BoonKind.ResearchSpeedPct: return "Research speed +" + p;
                case BoonKind.ResearchSlots: return "Research slots " + w;
                case BoonKind.ProductionPct: return "Production everywhere +" + p;
                case BoonKind.RunXpPct: return "Run XP +" + p;
                case BoonKind.RunStartCoins: return "Run coins at start +" + w;
                case BoonKind.SkipNegativeEvents: return "Bad outcomes skipped " + w;
                case BoonKind.AwakeningSlowPct: return "Boss awakening slower " + p;
                case BoonKind.AlienDamagePct: return "Alien damage +" + p;
                default: return Fmt.Humanize(kind.ToString()) + " " + w;
            }
        }

        /// <summary>The upgrade's cost type in words: Biomass, Ore or both.</summary>
        public static string CostTypeName(BuildingCostType t) => t == BuildingCostType.Biomass ? "Biomass" : t == BuildingCostType.Ore ? "Ore" : "Biomass + Ore";

        private void Card()
        {
            var b = CitySystem.Building(State, _selected);
            var anchor = b != null && b.Placed ? _world.PinAnchor(b.Uid) : null;
            if (b == null || !b.Placed || !anchor.HasValue) { _selected = null; return; }
            var def = Content.Blueprint(b.BlueprintId);
            int stars = CitySystem.Stars(State, def.Id);
            float w = 640;
            var card = Ui.Card(_overlay, new Color(0.1f, 0.07f, 0.2f, 1f), Color.Lerp(Theme.RarityColor(def.Rarity), Color.white, 0.15f), 36, 22, 8, 4, "BuildingCard");
            card.anchorMin = card.anchorMax = new Vector2(0, 1);
            card.pivot = new Vector2(0.5f, 0);
            card.sizeDelta = new Vector2(w, 0);
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var head = Ui.Row(card, 64, 8, null, 0, "Head");
            var name = SliceKit.Chunky(head, def.Name, 38, Color.white, TextAnchor.MiddleLeft, "Name");
            Ui.Size(name, -1, 64, 1);
            name.horizontalOverflow = HorizontalWrapMode.Wrap; name.resizeTextForBestFit = true; name.resizeTextMinSize = 24; name.resizeTextMaxSize = 38;
            var tag = Ui.Chip(head, CampMode ? "Camp" : def.Rarity + " · " + CitySystem.CopiesBuilt(State, def.Id) + "/" + def.Copies, Theme.RarityColor(def.Rarity), Color.white, 22, 44);
            tag.name = "RarityTag";
            Ui.Button(head, "✕", () => { _selected = null; Rebuild(); }, new Color(0.25f, 0.2f, 0.4f, 1), 56, 56, true, 28).name = "CloseCard";

            if (!CampMode) Ui.Label(card, "Lv " + b.Level + "/" + CitySystem.MaxLevel(Content) + "   " + Fmt.Stars(stars) + "   " + def.Category, Theme.FontSmall, Theme.Gold, TextAnchor.MiddleLeft, "LevelLine");

            if (CampMode) CampSeats(card, b, def);
            else FullCardBody(card, b, def, stars);

            // place the card above the building (under it when there is no room), inside the view
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            float ch = card.rect.height;
            var at = _world.ToOverlay(anchor.Value, _size);
            float x = Mathf.Clamp(at.x, w / 2 + 16, _size.x - w / 2 - 16);
            float y = at.y + 24;
            float top = -150, bottom = -_size.y + 200;
            if (y + ch > top) y = Mathf.Min(top - ch, at.y - 260 - ch);
            y = Mathf.Max(y, bottom);
            card.anchoredPosition = new Vector2(x, y);
            ScaleIn(card);
        }

        /// <summary>The City card's body: placement, workers, output / chances / boons, upgrade, Move / Store / star-up, building screens.</summary>
        private void FullCardBody(RectTransform card, CityBuilding b, BlueprintDef def, int stars)
        {
            var placement = PlacementLine(State, Content, b, out bool met);
            if (placement != null)
            {
                var pl = Ui.Label(card, placement, 24, met ? Theme.Good : Theme.TextDim, TextAnchor.MiddleLeft, "PlacementSkill");
                pl.horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            // workers: one slot per worker seat (the Launch Pod: the squad); empty slots pulse, a tap on an alien sends it home
            int slots = CitySystem.WorkerSlots(State, Content, b);
            var workers = CitySystem.Workers(State, b.Uid);
            var wrow = Ui.Row(card, 84, 10, null, 0, "Workers");
            var wl = SliceKit.Chunky(wrow, def.Id == ContentDb.LaunchPodId ? "Squad" : "Workers", 26, Theme.TextDim, TextAnchor.MiddleLeft, "Label");
            Ui.Size(wl, 120, 84, 0);
            for (int i = 0; i < slots; i++)
            {
                if (i < workers.Count)
                {
                    var a = workers[i];
                    string id = a.TypeId;
                    var sb = Ui.Button(wrow, "", () => Run(new AlienAssign { AlienTypeId = id, BuildingUid = null }), new Color(0.22f, 0.4f, 0.3f, 1), 80, 80, true, 20);
                    sb.name = "Worker:" + id;
                    var img = SummonKit.Img(sb.transform, ArtCatalog.Alien(id) ?? HudArt.AlienCritter(Theme.Good), "Alien");
                    Ui.Stretch(img.rectTransform, 6, 6, 6, 6);
                }
                else
                {
                    var eb = Ui.Button(wrow, "+", () => { _workersOpen = true; Rebuild(); }, new Color(0.3f, 0.26f, 0.5f, 1), 80, 80, true, 40);
                    eb.name = "EmptySlot";
                    if (Application.isPlaying) UiPulse.On(eb.GetComponentInChildren<Image>(), 0.06f, 1.2f, 0.6f);
                }
            }
            if (def.ProduceCurrency != null && workers.Count > 0)
            {
                var bonus = SliceKit.Chunky(wrow, "+" + Fmt.Pct(Math.Round(CitySystem.WorkerBonusPct(State, Content, b))), 26, Theme.Good, TextAnchor.MiddleLeft, "WorkBonus");
                Ui.Size(bonus, 110, 84, 0);
            }
            if (CitySystem.IsBuilt(b) && !CitySystem.IsActive(State, Content, b))
                Ui.Label(card, "Zz: no alien works here, so it does nothing.", 24, new Color(0.75f, 0.8f, 1f), TextAnchor.MiddleLeft, "Unstaffed");

            // output / chances / boons
            if (def.ProduceCurrency != null && CitySystem.IsBuilt(b))
            {
                double rate = CitySystem.ProductionPerHour(State, Content, b, Now);
                double cap = CitySystem.StorageCap(State, Content, b);
                var orow = Ui.Row(card, 64, 8, null, 0, "Output");
                var oi = SummonKit.Img(orow, SummonArt.CurrencyIcon(def.ProduceCurrency), "Icon");
                Ui.Size(oi, 48, 48, 0);
                var ot = Ui.Label(orow, "+" + Fmt.Num(rate) + "/h · " + Fmt.Num(Math.Floor(b.Stored)) + "/" + Fmt.Num(cap), Theme.FontSmall, Theme.Text, TextAnchor.MiddleLeft, "OutputText");
                Ui.Size(ot, -1, 64, 1);
                Ui.Button(orow, "Collect", () => Run(new CityCollect { Uid = b.Uid }), Theme.Good, 170, 64, b.Stored >= 1, 26).name = "Collect";
            }
            if (def.Pull != null && CitySystem.IsBuilt(b))
            {
                int ready = CitySystem.ChancesReady(b), cap = CitySystem.ChanceStorage(Content, def, b.Level);
                var prow = Ui.Row(card, 64, 8, null, 0, "Chances");
                var pi = SummonKit.Img(prow, SummonArt.CurrencyIcon(def.Pull.TokenCurrency), "Icon");
                Ui.Size(pi, 48, 48, 0);
                var pt = Ui.Label(prow, def.Pull.ChanceName + "s " + ready + "/" + cap + " · " + Fmt.OneDecimal(CitySystem.ChancesPerDay(State, Content, b)) + " a day · up to "
                    + CitySystem.MaxPullsPerPop(Content, stars) + " pulls", 22, Theme.Text, TextAnchor.MiddleLeft, "ChanceText");
                pt.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Size(pt, -1, 64, 1);
                var pops = Ui.Row(card, 70, 10, null, 0, "PopRow");
                Ui.Button(pops, "Pop", () => PopChances(b.Uid, false), Theme.Purple, -1, 70, ready >= 1, 28).name = "PopOne";
                Ui.Button(pops, "Pop all", () => PopChances(b.Uid, true), Theme.Purple, -1, 70, ready >= 2, 28).name = "PopAll";
            }
            foreach (var boon in def.Boons)
            {
                if (boon.Kind == BoonKind.SquadSlots) continue;
                Ui.Label(card, BoonLine(boon.Kind, CitySystem.BoonValue(State, Content, b, boon.Kind)), Theme.FontSmall, Theme.Cream, TextAnchor.MiddleLeft, "Boon:" + boon.Kind);
            }

            // upgrade (cost type) / timers
            if (b.TimerId != null) TimerControls(card, b.TimerId, b.Level == 0 ? "Construction" : "Upgrade");
            else
            {
                bool max = b.Level >= CitySystem.MaxLevel(Content);
                var urow = Ui.Row(card, 96, 10, null, 0, "UpgradeRow");
                if (max) Ui.Button(urow, "Max level", null, Theme.Button, -1, 90, false, 28).name = "Upgrade";
                else
                {
                    var cost = CitySystem.UpgradeCost(Content, def, b.Level);
                    bool can = State.Wallet.CanAfford(cost);
                    var ub = Ui.Button(urow, "Upgrade", () => Run(new CityUpgrade { Uid = b.Uid }), Theme.Hex(0xF6C94A), 230, 90, can, 30);
                    ub.name = "Upgrade";
                    var costs = Ui.Column(urow, 2, 0, null, "UpgradeCost");
                    Ui.Size(costs, -1, 90, 1);
                    var crow = Ui.Row(costs, 46, 6, null, 0, "Costs");
                    crow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                    foreach (var c in cost)
                    {
                        var ci = SummonKit.Img(crow, SummonArt.CurrencyIcon(c.Currency), "CostIcon:" + c.Currency);
                        Ui.Size(ci, 40, 40, 0);
                        var ct = Ui.Label(crow, Fmt.Num(c.Amount), 26, State.Wallet.Get(c.Currency) >= c.Amount ? Theme.Text : Theme.Bad, TextAnchor.MiddleLeft, "Cost:" + c.Currency);
                        Ui.Size(ct, 90, 46, 0);
                    }
                    Ui.Label(costs, "Lv " + b.Level + " → " + (b.Level + 1) + " · " + CostTypeName(def.CostType) + " · " + Fmt.Duration(CitySystem.UpgradeDurationMs(State, Content, def, b.Level, Now)),
                        20, Theme.TextDim, TextAnchor.MiddleLeft, "CostType");
                }
            }
            var arow = Ui.Row(card, 76, 10, null, 0, "Actions");
            Ui.Button(arow, "Move", () => { EnterEdit(); StartMove(b.Uid); }, Theme.Button, -1, 72, true, 26).name = "Move";
            bool cancel = b.Level == 0;
            Ui.Button(arow, cancel ? "Cancel" : "Store", () => Apply(new CityStore { Uid = b.Uid }), Theme.Bad, -1, 72, cancel || b.TimerId == null, 26).name = "Store";
            long shards = State.Wallet.Get(Currencies.BlueprintShards(def.Id));
            var owned = CitySystem.Owned(State, def.Id);
            long need = CitySystem.StarCost(Content, owned);
            if (owned.Stars < CitySystem.MaxStars)
                Ui.Button(arow, "★ " + shards + "/" + need, () => Run(new BlueprintStarUp { BlueprintId = def.Id }), Theme.Warn, -1, 72, shards >= need, 24).name = "StarUp";
            if (def.Id == ContentDb.ShardReactorId)
                Ui.Button(card, "Open the Shard Reactor", () => App.Router.Push(new ReactorScreen()), Theme.Purple, -1, 72, CitySystem.IsActive(State, Content, b), 26).name = "OpenReactor";
            if (def.Id == ContentDb.LaboratoryId)
                Ui.Button(card, "Open the Laboratory", () => App.Open("laboratory"), Theme.Purple, -1, 72, CitySystem.IsActive(State, Content, b), 26).name = "OpenLaboratory";
        }

        private static void ScaleIn(RectTransform rt)
        {
            if (!Application.isPlaying) return;
            UiTweens.Play(rt.gameObject, "pop", 0.18f, k => { if (rt != null) rt.localScale = Vector3.one * Mathf.Lerp(0.85f, 1, 1 - (1 - k) * (1 - k)); });
        }

        // ---- digging ----------------------------------------------------------------------------------------------------------

        /// <summary>Frames a rubble patch and opens its dig pop-up (what is inside, the dig's cost and timer).</summary>
        public void OpenDig(string patchId)
        {
            if (CampMode) return;
            _digPatch = patchId;
            _selected = null;
            _workersOpen = false;
            Rebuild();
        }

        private void DigPopup()
        {
            var patch = Content.CityPatch(_digPatch);
            if (patch == null) { _digPatch = null; return; }
            // the frame: every tile of the patch outlined
            var marks = new List<CityWorld.Mark>();
            foreach (var cell in patch.Cells) marks.Add(new CityWorld.Mark { X = cell % Content.CityTuning.GridWidth, Y = cell / Content.CityTuning.GridWidth, Color = Theme.Gold, Ring = true });
            _world.SetMarks(marks);

            string block = CitySystem.DigBlockReason(State, Content, patch.Id);
            int n = CitySystem.NextDigNumber(State);
            var dig = CitySystem.DigDef(Content, n);
            var cost = CitySystem.DigCost(Content, n);
            bool free = State.City.FreeDigs > 0;
            bool running = State.City.DigPatchId == patch.Id;
            float w = 640;
            var card = Ui.Card(_overlay, new Color(0.1f, 0.07f, 0.2f, 0.97f), Theme.Gold, 36, 22, 8, 4, "DigPopup");
            card.anchorMin = card.anchorMax = new Vector2(0, 1);
            card.pivot = new Vector2(0.5f, 0);
            card.sizeDelta = new Vector2(w, 0);
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var head = Ui.Row(card, 60, 8, null, 0, "Head");
            var title = SliceKit.Chunky(head, patch.Name, 38, Color.white, TextAnchor.MiddleLeft, "Name");
            Ui.Size(title, -1, 60, 1);
            Ui.Button(head, "✕", () => { _digPatch = null; _world.SetMarks(null); Rebuild(); }, new Color(0.25f, 0.2f, 0.4f, 1), 56, 56, true, 28).name = "CloseDig";
            var inside = new List<string>();
            var kinds = new Dictionary<string, int>();
            foreach (var f in patch.Features) kinds[f.Kind] = (kinds.TryGetValue(f.Kind, out var k) ? k : 0) + 1;
            foreach (var kv in kinds) inside.Add((kv.Value > 1 ? kv.Value + " " : "a ") + CityArt.FeatureName(kv.Key) + (kv.Value > 1 ? "s" : ""));
            Ui.Label(card, patch.Cells.Count + " tiles" + (inside.Count > 0 ? " · inside: " + string.Join(", ", inside) : ""), Theme.FontSmall, Theme.Text, TextAnchor.MiddleLeft, "DigTiles");
            if (running)
            {
                TimerControls(card, State.City.DigTimerId, "Digging");
            }
            else if (dig != null)
            {
                var crow = Ui.Row(card, 52, 8, null, 0, "DigCost");
                crow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                var dl = SliceKit.Chunky(crow, "Dig #" + n, 28, Theme.Gold, TextAnchor.MiddleLeft, "DigNumber");
                Ui.Size(dl, 130, 52, 0);
                if (free) Ui.Label(crow, "free · instant", Theme.FontSmall, Theme.Good, TextAnchor.MiddleLeft, "Free");
                else
                {
                    foreach (var c in cost)
                    {
                        var ci = SummonKit.Img(crow, SummonArt.CurrencyIcon(c.Currency), "CostIcon:" + c.Currency);
                        Ui.Size(ci, 40, 40, 0);
                        var ct = Ui.Label(crow, Fmt.Num(c.Amount), 26, State.Wallet.Get(c.Currency) >= c.Amount ? Theme.Text : Theme.Bad, TextAnchor.MiddleLeft, "Cost:" + c.Currency);
                        Ui.Size(ct, 96, 52, 0);
                    }
                    Ui.Label(crow, "· " + Fmt.Duration(dig.Ms), 26, Theme.TextDim, TextAnchor.MiddleLeft, "DigTime");
                }
                bool can = block == null && (free || State.Wallet.CanAfford(cost));
                string reason = block != null ? Fmt.Humanize(block) : !can ? "Not enough resources" : null;
                if (block == "a dig is running") reason = "One dig at a time";
                else if (block != null && block.Contains("touch")) reason = "Dig a patch that touches your land first";
                Ui.ButtonWithReason(card, free ? "Dig (free)" : "Dig", () => { var id = patch.Id; Run(new CityDig { PatchId = id }, r => { if (r.Ok) { _digPatch = null; _world.SetMarks(null); Rebuild(); } }); },
                    Theme.Hex(0xF6C94A), -1, 84, can, reason, 32).name = "Dig";
            }
            else Ui.Label(card, "No digs left.", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleLeft, "NoDigs");
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            var at = _world.ToOverlay(_world.PatchCentre(patch), _size);
            float ch = card.rect.height;
            float x = Mathf.Clamp(at.x, w / 2 + 16, _size.x - w / 2 - 16);
            float y = at.y + 80;
            if (y + ch > -150) y = at.y - 80 - ch;
            y = Mathf.Max(y, -_size.y + 210);
            card.anchoredPosition = new Vector2(x, y);
            ScaleIn(card);
        }

        // ---- pull buildings: pop with the shared reveal ------------------------------------------------------------------------

        /// <summary>Pops one (or every) chance of a pull building, or of all of them (uid null), through the shared pull reveal.</summary>
        public void PopChances(string uid, bool all)
        {
            _popping = true;
            _popFly.Clear();
            Run(new CityPop { Uid = uid, All = all }, r =>
            {
                if (!r.Ok) { _popping = false; return; }
                var results = PopResults(r.Payload);
                var modal = ShowPopReveal(results, Content);
                LastPopReveal = modal;
                var from = uid != null && _world != null && _world.PinAnchor(uid).HasValue ? HolderToWorld(_world.ToOverlay(_world.PinAnchor(uid).Value, _size)) : FeedbackFx.Centre(_holder);
                modal.Closed += () =>
                {
                    _popping = false;
                    // the tokens fly to the bar (the Signals tab lives on the hub)
                    foreach (var kv in _popFly)
                    {
                        var target = App.Bar.ChipOf(kv.Key) ?? App.Bar.Wallet;
                        if (target != null) FeedbackFx.FlyTo(App.Feedback.Layer, SummonArt.CurrencyIcon(kv.Key), from, target, (int)Math.Min(6, 2 + kv.Value));
                    }
                    _popFly.Clear();
                };
            });
        }

        /// <summary>The last pop reveal (tests, showcase).</summary>
        public UiModal LastPopReveal { get; private set; }

        public static List<CityPopResult> PopResults(object payload)
        {
            if (payload is List<CityPopResult> list) return list;
            if (payload is Newtonsoft.Json.Linq.JToken t) return t.ToObject<List<CityPopResult>>(RogueStars.Client.Services.ClientJson.WireSerializer) ?? new List<CityPopResult>();
            return new List<CityPopResult>();
        }

        /// <summary>
        /// The pop as the shared pull reveal (SG1): one card per chance popped, 1 / 2 / 3 pulls with rising effects (×2 sparks as an
        /// Epic, ×3 is held, cracks the portal and gets the banner).
        /// </summary>
        public static UiModal ShowPopReveal(Transform layer, IList<CityPopResult> results, ContentDb c)
        {
            var items = PopItems(results, c);
            bool triple = false;
            foreach (var r in results) if (r.Pulls >= 3) triple = true;
            string chance = null;
            foreach (var r in results)
            {
                var b = r.Uid != null && r.Uid.Contains("#") ? c.Blueprint(r.Uid.Substring(0, r.Uid.IndexOf('#'))) : null;
                chance = b?.Pull?.ChanceName ?? chance;
            }
            var o = new RevealOptions
            {
                Title = results.Count == 1 ? chance ?? "Chance" : results.Count + " chances popped",
                Skip = SummonEventScreen.SkipAnimation,
                Banner = triple ? "×3!" : null,
                BannerSub = triple ? "Three pulls in one" : null,
                Name = "PopReveal",
                Accent = Theme.RarityColor(Rarity.Legendary),
            };
            return PullReveal.Show(layer, items, o);
        }

        private UiModal ShowPopReveal(IList<CityPopResult> results, ContentDb c) => ShowPopReveal(App.ModalLayer, results, c);

        public static List<RevealItem> PopItems(IList<CityPopResult> results, ContentDb c)
        {
            var items = new List<RevealItem>();
            foreach (var r in results)
            {
                var def = r.Uid != null && r.Uid.Contains("#") ? c.Blueprint(r.Uid.Substring(0, r.Uid.IndexOf('#'))) : null;
                string cur = r.Currency;
                items.Add(new RevealItem
                {
                    Kind = "chance",
                    Id = cur,
                    Name = (PullWord(cur) + (r.Pulls == 1 ? " pull" : " pulls")).Trim(),
                    Note = def?.Pull?.ChanceName ?? Fmt.CurrencyName(cur),
                    Tier = PopTier(r.Pulls),
                    Amount = "×" + r.Pulls,
                    Art = box => Ui.Stretch(SummonKit.Img(box, SummonArt.CurrencyIcon(cur), "TokenArt").rectTransform),
                });
            }
            return items;
        }

        /// <summary>1 pull reads as Rare, 2 as Epic (sparks), 3 as Legendary (held, crack, banner).</summary>
        public static Rarity PopTier(int pulls) => pulls >= 3 ? Rarity.Legendary : pulls == 2 ? Rarity.Epic : Rarity.Rare;

        private static string PullWord(string currency)
        {
            if (currency == Currencies.BlueprintTokens) return "Blueprint";
            if (currency == Currencies.AlienTokens) return "Alien";
            if (currency == Currencies.PartTokens) return "Part";
            return "";
        }

        // ---- output sheet ----------------------------------------------------------------------------------------------------

        private void OutputSheet()
        {
            Ui.Sheet(App.ModalLayer, "Output", new Color(0.1f, 0.07f, 0.2f, 0.98f), Theme.Gold, (box, modal) =>
            {
                SliceKit.Chunky(box, "Output", 46, Color.white, TextAnchor.MiddleLeft, "Title");
                var rates = Rates(State, Content, Now);
                var stored = new Dictionary<string, double>();
                var caps = new Dictionary<string, double>();
                var counts = new Dictionary<string, int>();
                foreach (var b in State.City.Buildings)
                {
                    var def = Content.Blueprint(b.BlueprintId);
                    if (def?.ProduceCurrency == null || !CitySystem.IsBuilt(b)) continue;
                    string cur = def.ProduceCurrency;
                    stored[cur] = (stored.TryGetValue(cur, out var s) ? s : 0) + Math.Floor(b.Stored);
                    caps[cur] = (caps.TryGetValue(cur, out var cp) ? cp : 0) + CitySystem.StorageCap(State, Content, b);
                    counts[cur] = (counts.TryGetValue(cur, out var n) ? n : 0) + 1;
                }
                if (counts.Count == 0) Ui.Label(box, "Nothing produces yet: build a farm or a mine and put an alien in it.", Theme.FontSmall, Theme.TextDim);
                foreach (var kv in counts)
                {
                    string cur = kv.Key;
                    var row = Ui.Row(box, 70, 10, null, 0, "OutputRow:" + cur);
                    var icon = SummonKit.Img(row, SummonArt.CurrencyIcon(cur), "Icon");
                    Ui.Size(icon, 56, 56, 0);
                    var t = Ui.Label(row, Fmt.CurrencyName(cur) + "  +" + Fmt.Num(rates.TryGetValue(cur, out var r) ? r : 0) + "/h", Theme.FontSmall, Theme.Text);
                    Ui.Size(t, 380, 70, 0);
                    float fill = caps[cur] > 0 ? (float)(stored[cur] / caps[cur]) : 0;
                    Ui.Bar(row, fill, Theme.CurrencyColor(cur), Fmt.Num(stored[cur]) + "/" + Fmt.Num(caps[cur]), 44);
                    var pr = Ui.Button(row, State.City.PriorityCurrency == cur ? "★" : "☆", () => { modal.Close(); Run(new CitySetPriority { Currency = State.City.PriorityCurrency == cur ? null : cur }); },
                        State.City.PriorityCurrency == cur ? Theme.Gold : Theme.Button, 70, 64, true, 32);
                    pr.name = "Priority:" + cur;
                }
                Ui.Label(box, "★ priority: +" + Fmt.Pct(Content.CityTuning.PriorityBoostPct) + " on that resource, −" + Fmt.Pct(Content.CityTuning.PriorityPenaltyPct) + " on the others.", 22, Theme.TextDim);
                int chances = 0;
                foreach (var b in State.City.Buildings) chances += CitySystem.ChancesReady(b);
                var row2 = Ui.Row(box, 90, 12, null, 0, "Buttons");
                Ui.Button(row2, "Close", () => modal.Close(), Theme.Button, -1, 84).name = "Close";
                Ui.Button(row2, "Pop all (" + chances + ")", () => { modal.Close(); PopChances(null, true); }, Theme.Purple, -1, 84, chances > 0).name = "PopAll";
            });
        }

        // ---- workers ----------------------------------------------------------------------------------------------------------

        /// <summary>The idle aliens strip over the dock: drag one onto a building (empty slots pulse); idle first.</summary>
        private void WorkersStrip()
        {
            var strip = Ui.RoundPanel(_overlay, new Color(0.08f, 0.06f, 0.16f, 0.94f), 30, "WorkersStrip");
            var srt = strip.rectTransform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0); srt.pivot = new Vector2(0.5f, 0);
            srt.sizeDelta = new Vector2(-24, 236); srt.anchoredPosition = new Vector2(0, 200);
            var hint = SliceKit.Chunky(strip.transform, "Drag an alien onto a building", 28, Theme.Cream, TextAnchor.MiddleLeft, "Hint");
            SummonKit.Place(hint, new Vector2(0, 1), new Vector2(24, -8), new Vector2(800, 44));
            var view = Ui.Rect(strip.transform, "Viewport");
            Ui.Stretch(view, 12, 56, 12, 10);
            view.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect(view, "Content");
            content.anchorMin = new Vector2(0, 0); content.anchorMax = new Vector2(0, 1); content.pivot = new Vector2(0, 0.5f);
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.content = content; sr.horizontal = true; sr.vertical = false; sr.viewport = view; sr.movementType = ScrollRect.MovementType.Clamped;
            var hl = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 12; hl.childControlWidth = false; hl.childControlHeight = false; hl.childForceExpandWidth = false; hl.childAlignment = TextAnchor.MiddleLeft;
            content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var list = new List<OwnedAlien>(State.Aliens.Owned.Values);
            list.Sort((a, b) =>
            {
                int ia = string.IsNullOrEmpty(a.WorkBuildingId) ? 0 : 1, ib = string.IsNullOrEmpty(b.WorkBuildingId) ? 0 : 1;
                return ia != ib ? ia.CompareTo(ib) : string.CompareOrdinal(a.TypeId, b.TypeId);
            });
            if (list.Count == 0) Ui.Label(content, "No aliens yet: pull them in Alien Signal.", Theme.FontSmall, Theme.TextDim);
            foreach (var a in list)
            {
                var chip = Ui.RoundPanel(content, string.IsNullOrEmpty(a.WorkBuildingId) ? new Color(0.22f, 0.36f, 0.28f, 1) : new Color(0.2f, 0.17f, 0.32f, 1), 22, "Alien:" + a.TypeId);
                chip.rectTransform.sizeDelta = new Vector2(150, 164);
                var img = SummonKit.Img(chip.transform, ArtCatalog.Alien(a.TypeId) ?? HudArt.AlienCritter(Theme.Good), "Art");
                SummonKit.Place(img, new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(100, 100));
                string where = string.IsNullOrEmpty(a.WorkBuildingId) ? "idle" : Content.Blueprint(CitySystem.Building(State, a.WorkBuildingId)?.BlueprintId)?.Name ?? "";
                var t = Ui.Label(chip.transform, (Content.AlienType(a.TypeId)?.Name ?? a.TypeId) + "\n<size=18>" + where + "</size>", 20, Color.white, TextAnchor.MiddleCenter, "Name");
                SummonKit.Place(t, new Vector2(0.5f, 0), new Vector2(0, 4), new Vector2(146, 56));
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                var drag = chip.gameObject.AddComponent<CityDragSource>();
                drag.Screen = this; drag.AlienId = a.TypeId; drag.Scroll = sr; drag.Horizontal = true;
            }
        }

        /// <summary>Drops an alien on a building (the Launch Pod = the squad).</summary>
        public void AssignAlien(string alienId, string uid)
        {
            if (alienId == null || uid == null) return;
            Run(new AlienAssign { AlienTypeId = alienId, BuildingUid = uid });
        }

        // ---- feedback (G1 toast map T05–T10, T18) ------------------------------------------------------------------------------

        private void OnEvent(GameEvent e)
        {
            switch (e.Type)
            {
                case "building_built": case "building_upgraded": case "building_stored": case "blueprint_star_up":
                case "city_dug": case "city_dig_started":
                    _events.Enqueue(e);
                    break;
            }
        }

        /// <summary>Plays one city moment on the diorama (after the state change was synced).</summary>
        private void Play(GameEvent e)
        {
            string A(int i) => i < e.Args.Count ? e.Args[i] : "";
            switch (e.Type)
            {
                case "building_built": _world.Built(A(0)); break; // T06
                case "building_upgraded": // T07: the level badge pops, a party
                    _world.Party(A(0));
                    LevelBadgePop(A(0), A(1));
                    break;
                case "blueprint_star_up":
                    foreach (var b in CitySystem.Copies(State, A(0))) if (b.Placed) _world.Party(b.Uid, true);
                    break;
                case "building_stored": // T09: shrinks and flies into the tray
                {
                    var at = _world.Stored(A(0));
                    var def = Content.Blueprint(A(0).Contains("#") ? A(0).Substring(0, A(0).IndexOf('#')) : A(0));
                    var target = FindRect(_overlay, IsTablet || _edit ? "Rail" : "Dock:Build");
                    if (at.HasValue && target != null && def != null)
                        FeedbackFx.FlyTo(App.Feedback.Layer, CityArt.Building(def), HolderToWorld(_world.ToOverlay(at.Value, _size)), target, 1, 140);
                    break;
                }
                case "city_dug": _world.Crumble(Content.CityPatch(A(0)), State, Content); break; // T10 (the banner comes from GameFeedback)
                case "city_dig_started": _world.DriveDrill(Content.CityPatch(A(0))); break;
            }
        }

        private void LevelBadgePop(string uid, string level)
        {
            var anchor = _world.PinAnchor(uid);
            if (!anchor.HasValue) return;
            var badge = Ui.RoundPanel(_pinLayer, Theme.Gold, 24, "LevelPop:" + uid);
            badge.raycastTarget = false;
            var rt = badge.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.sizeDelta = new Vector2(140, 56);
            _pins.Add(new Pin { Rt = rt, World = anchor.Value, Offset = new Vector2(0, 40) });
            var t = SliceKit.Chunky(badge.transform, "Lv " + level, 32, Color.white, TextAnchor.MiddleCenter, "Text");
            Ui.Stretch(t.rectTransform);
            FeedbackFx.StarPop(rt, Theme.Gold);
            var go = badge.gameObject;
            UiTweens.Play(go, "life", 2.2f, null, () => Ui.DestroyGo(go));
        }

        /// <summary>
        /// G1 hooks: collected goods fly from their building (T01 from the diorama), built / upgraded / stored play on the diorama
        /// (T06 / T07 / T09); a pop's token rewards wait for the reveal to close.
        /// </summary>
        public override bool OnFeedback(GameEvent e)
        {
            string A(int i) => i < e.Args.Count ? e.Args[i] : "";
            switch (e.Type)
            {
                case "reward":
                    if (!_popping || A(0) != RewardKinds.Currency) return false;
                    long.TryParse(A(2), out var amount);
                    _popFly[A(1)] = (_popFly.TryGetValue(A(1), out var have) ? have : 0) + amount;
                    return true;
                case "city_collected":
                {
                    var anchor = _world != null ? _world.PinAnchor(A(0)) : null;
                    if (!anchor.HasValue) return false;
                    var target = App.Bar.ChipOf(A(1)) ?? App.Bar.Wallet;
                    if (target == null) return true;
                    long.TryParse(A(2), out var n);
                    FeedbackFx.FlyTo(App.Feedback.Layer, SummonArt.CurrencyIcon(A(1)), HolderToWorld(_world.ToOverlay(anchor.Value, _size)), target, n >= 100 ? 4 : 3);
                    return true;
                }
                case "building_built": case "building_upgraded": case "building_stored":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>A walker popped goods at the fire: the bar pill ticks (at most every 2 s).</summary>
        private void OnDelivered(string currency)
        {
            if (_deliveredTick > 0 || currency == null || App?.Bar == null) return;
            _deliveredTick = 2;
            var chip = App.Bar.ChipOf(currency);
            if (chip != null) FeedbackFx.Pop(chip, 0.08f, 0.2f);
        }

        private static RectTransform FindRect(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true)) if (rt.name == name) return rt;
            return null;
        }
    }
}
