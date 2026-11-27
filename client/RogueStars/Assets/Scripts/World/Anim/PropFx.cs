using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.World.Anim
{
    public enum FxKind { None, Flame, Sway, Pulse, Shimmer, Swirl }

    /// <summary>One animated look of a prop: a shader kind with its parameters and optional particle extras.</summary>
    public sealed class PropLook
    {
        public FxKind Kind;
        /// <summary>Shader parameters (property name, value) on top of the base material's defaults.</summary>
        public (string name, float value)[] Params = Array.Empty<(string, float)>();
        public Color? Glow;
        /// <summary>Flame: take the flames from the sprite's warm bright pixels instead of the hand-painted mask (campfire).</summary>
        public bool AutoMask;
        /// <summary>Swirl: the disc in uv (centre x, y, radius, height share).</summary>
        public Vector4? Disc;
        /// <summary>Extras: embers over a fire, smoke over a vent, an additive glow disc behind / over the prop. Positions: uv of the sprite.</summary>
        public bool Embers, Smoke;
        public Vector2 Emit = new Vector2(0.5f, 0.35f);
        public float EmitWidth = 0.3f;
        public Color SmokeTint = new Color(0.62f, 0.62f, 0.66f, 0.9f);
        public bool GlowDisc;
        public Color GlowDiscColor = new Color(1f, 0.55f, 0.2f, 1);
        public Vector2 GlowAt = new Vector2(0.5f, 0.3f);
        public float GlowSize = 0.9f;

        public PropLook(FxKind kind, params (string, float)[] p) { Kind = kind; if (p != null && p.Length > 0) Params = p; }
    }

    /// <summary>
    /// The animated look of props, set pieces and City buildings: tables by sprite name (props, set pieces, markers) and by
    /// blueprint id (building stand-ins) onto five reusable shaders (flame flicker, sway, glow pulse, shimmer, swirl) plus ember /
    /// smoke particles, with colour-based masks so any art works without a hand-painted mask. <see cref="Apply(SpriteRenderer, PropLook)"/>
    /// swaps the renderer's material for its own instance (its own phase, so copies never move in lockstep) and adds the
    /// extras; with reduced animation on, everything reverts to the plain sprite. GPU only: no script runs per frame.
    /// </summary>
    public static class PropFx
    {
        private const string Mats = "Art/Anim/Materials/";
        private static readonly Dictionary<FxKind, Material> Bases = new Dictionary<FxKind, Material>();

        private static PropLook Pulse(Color glow, float amount = 0.55f, float speed = 1.8f, float whole = 0, float lo = 0.35f) =>
            new PropLook(FxKind.Pulse, ("_Pulse", amount), ("_Speed", speed), ("_Whole", whole), ("_MaskLo", lo)) { Glow = glow };
        private static PropLook Sway(float amount, float speed = 1.2f, float basis = 0.25f) =>
            new PropLook(FxKind.Sway, ("_Amount", amount), ("_Speed", speed), ("_Base", basis), ("_Power", 1.9f), ("_Gust", 0.3f));
        private static PropLook Shimmer(float strength = 0.7f, float speed = 0.22f, float whole = 0.25f) =>
            new PropLook(FxKind.Shimmer, ("_Strength", strength), ("_Speed", speed), ("_Whole", whole));

        private static readonly Color Cyan = new Color(0.45f, 0.95f, 1f), Violet = new Color(0.75f, 0.45f, 1f), Warm = new Color(1f, 0.6f, 0.25f),
            Gold = new Color(1f, 0.85f, 0.4f), Green = new Color(0.55f, 1f, 0.6f);

        /// <summary>Props, set pieces and markers by sprite name.</summary>
        public static readonly Dictionary<string, PropLook> Props = new Dictionary<string, PropLook>
        {
            { "campfire_big", new PropLook(FxKind.Flame, ("_Distort", 0.026f), ("_NoiseScale", 7), ("_Speed", 1.6f), ("_Flicker", 0.25f), ("_BaseY", 0.3f))
                { AutoMask = true, Embers = true, Emit = new Vector2(0.5f, 0.45f), EmitWidth = 0.3f, GlowDisc = true, GlowAt = new Vector2(0.5f, 0.3f), GlowSize = 1.0f } },
            { "alien_tree", Sway(0.03f, 1.2f, 0.25f) },
            { "giant_mushroom", Sway(0.016f, 0.9f, 0.35f) },
            { "giant_mushroom_2", Sway(0.016f, 0.9f, 0.35f) },
            { "giant_mushroom_3", Sway(0.016f, 0.9f, 0.35f) },
            { "crystal_spire", Pulse(Cyan, 0.7f) },
            { "crystal_spire_2", Pulse(Cyan, 0.7f, 2.1f) },
            { "crystal_small", Pulse(Cyan, 0.7f, 2.4f) },
            { "pebble_glow", Pulse(Warm, 0.6f, 2.0f) },
            { "event_shrine", Pulse(Gold, 0.55f, 1.4f, 0.1f) },
            { "chest_signature", Pulse(Violet, 0.7f, 1.6f, 0.1f) },
            { "chest_mythic", Shimmer(0.9f, 0.5f, 0.3f) },
            { "event_wreck", new PropLook(FxKind.None) { Smoke = true, Emit = new Vector2(0.5f, 0.55f), EmitWidth = 0.25f } },
            { "crashed_saucer", new PropLook(FxKind.Pulse, ("_Pulse", 0.5f), ("_Speed", 2.2f), ("_MaskLo", 0.3f)) { Glow = Cyan, Smoke = true, Emit = new Vector2(0.55f, 0.6f), EmitWidth = 0.18f } },
            { "crashed_saucer_2", new PropLook(FxKind.Pulse, ("_Pulse", 0.5f), ("_Speed", 2.2f), ("_MaskLo", 0.3f)) { Glow = Cyan, Smoke = true, Emit = new Vector2(0.55f, 0.6f), EmitWidth = 0.18f } },
            { "portal_well", new PropLook(FxKind.Swirl, ("_Spin", 0.25f), ("_Twist", 1.1f), ("_GlowAmount", 0.35f)) { Disc = new Vector4(0.5f, 0.42f, 0.2f, 0.5f), Glow = Violet } },
            { "portal", new PropLook(FxKind.Swirl, ("_Spin", 0.25f), ("_Twist", 1.1f), ("_GlowAmount", 0.3f)) { Disc = new Vector4(0.5f, 0.5f, 0.24f, 1f), Glow = Violet } },
            { "mystery", Pulse(Violet, 0.5f, 1.6f, 0.15f) },
            { "minigame", Pulse(Green, 0.5f, 2.0f, 0.15f) },
            { "quest", Pulse(Gold, 0.8f, 1.6f, 0.2f) },
            { "event", Pulse(Gold, 0.9f, 2.2f, 0.3f) },
            { "time_clock", Pulse(Cyan, 0.9f, 1.6f, 0.15f, 0.25f) },
            { "time_clock_2", Pulse(Cyan, 0.9f, 1.6f, 0.15f, 0.25f) },
        };

        /// <summary>City buildings by blueprint id: what moves on their stand-in sprite (sway for farms, glow pulse for labs, crystals, the reactor, beacons and spires; smoke for the refinery).</summary>
        public static readonly Dictionary<string, PropLook> Buildings = new Dictionary<string, PropLook>
        {
            { "bp_biomass_farm", Sway(0.03f, 1.2f, 0.25f) },
            { "bp_grain_farm", Sway(0.016f, 0.9f, 0.35f) },
            { "bp_ore_mine", new PropLook(FxKind.None) { Smoke = true, Emit = new Vector2(0.55f, 0.55f), EmitWidth = 0.2f, SmokeTint = new Color(0.68f, 0.58f, 0.46f, 0.8f) } },
            { "bp_fuel_refinery", new PropLook(FxKind.None) { Smoke = true, Emit = new Vector2(0.5f, 0.6f), EmitWidth = 0.25f } },
            { "bp_gear_works", Shimmer(0.6f, 0.5f, 0.3f) },
            { "bp_training_grounds", Shimmer(0.5f, 0.5f, 0.25f) },
            { "bp_diamond_mine", Pulse(Cyan, 0.8f, 2.0f) },
            { "bp_token_relay", Pulse(Gold, 0.6f, 1.8f, 0.15f) },
            { "bp_signal_beacon", Pulse(Violet, 0.8f, 1.4f, 0.1f) },
            { "bp_part_fabricator", new PropLook(FxKind.Pulse, ("_Pulse", 0.6f), ("_Speed", 2.2f), ("_MaskLo", 0.3f)) { Glow = Cyan, Smoke = true, Emit = new Vector2(0.55f, 0.6f), EmitWidth = 0.15f } },
            { "bp_launch_pod", new PropLook(FxKind.None) { Smoke = true, Emit = new Vector2(0.5f, 0.6f), EmitWidth = 0.2f, SmokeTint = new Color(0.85f, 0.85f, 0.9f, 0.75f) } },
            { "bp_barracks_dome", Sway(0.012f, 0.8f, 0.4f) },
            { "bp_command_spire", Pulse(Cyan, 0.6f, 1.5f, 0.05f) },
            { "bp_breeding_pen", Sway(0.012f, 0.7f, 0.4f) },
            { "bp_gene_lab", Pulse(Green, 0.7f, 1.6f, 0.1f) },
            { "bp_laboratory", Pulse(Gold, 0.6f, 1.5f, 0.1f) },
            { "bp_observatory", Pulse(Cyan, 0.9f, 1.6f, 0.15f, 0.25f) },
            { "bp_shard_reactor", Pulse(Violet, 1.0f, 2.4f, 0.15f, 0.25f) },
            { "bp_stamina_well", new PropLook(FxKind.Swirl, ("_Spin", 0.25f), ("_Twist", 1.1f), ("_GlowAmount", 0.3f)) { Disc = new Vector4(0.5f, 0.5f, 0.24f, 1f), Glow = Violet } },
            { "bp_regen_spire", Pulse(Green, 0.8f, 2.0f, 0.15f) },
            { "bp_supply_depot", Sway(0.01f, 1.0f, 0.5f) },
            { "bp_scout_post", new PropLook(FxKind.Flame, ("_Distort", 0.014f), ("_BaseY", 0.2f)) { AutoMask = true } },
            { "bp_war_academy", Shimmer(0.6f, 0.5f, 0.25f) },
            { "bp_stasis_beacon", Pulse(Violet, 0.7f, 1.3f, 0.15f) },
        };

        /// <summary>The look of a prop sprite by its name (null = none).</summary>
        public static PropLook LookOfSprite(Sprite s) => s != null && Props.TryGetValue(s.name, out var l) ? l : null;

        /// <summary>The look of a building by blueprint id (null = none).</summary>
        public static PropLook LookOfBuilding(string blueprintId)
        {
            if (string.IsNullOrEmpty(blueprintId)) return null;
            return Buildings.TryGetValue(blueprintId, out var l) ? l : null;
        }

        /// <summary>A material instance of a look (base material + the look's parameters + a phase); null when the base is missing.</summary>
        public static Material MakeMaterial(PropLook look, float phase)
        {
            if (look == null || look.Kind == FxKind.None) return null;
            if (!Bases.TryGetValue(look.Kind, out var b) || b == null)
            {
                b = Resources.Load<Material>(Mats + "Fx" + look.Kind);
                Bases[look.Kind] = b;
            }
            if (b == null) return null;
            var m = new Material(b) { name = "Fx" + look.Kind, hideFlags = HideFlags.DontSave };
            foreach (var (name, value) in look.Params) if (m.HasProperty(name)) m.SetFloat(name, value);
            if (look.Glow.HasValue && m.HasProperty("_Glow")) m.SetColor("_Glow", look.Glow.Value);
            if (look.Kind == FxKind.Flame && m.HasProperty("_AutoMask")) m.SetFloat("_AutoMask", look.AutoMask ? 1 : 0);
            if (look.Disc.HasValue && m.HasProperty("_Center"))
            {
                var d = look.Disc.Value;
                m.SetVector("_Center", new Vector4(d.x, d.y, 0, 0)); m.SetFloat("_Radius", d.z); m.SetFloat("_Squash", d.w);
            }
            if (m.HasProperty("_Phase")) m.SetFloat("_Phase", phase);
            return m;
        }

        /// <summary>A phase from a world position.</summary>
        public static float PhaseOf(Vector3 world) => world.x * 1.7f + world.y * 2.3f;

        /// <summary>Animates a world sprite with <paramref name="look"/> (null look / sprite = nothing). Idempotent per renderer.</summary>
        public static PropFxRef Apply(SpriteRenderer sr, PropLook look)
        {
            if (sr == null || sr.sprite == null || look == null) return null;
            var host = sr.GetComponent<PropFxRef>();
            if (host == null) host = sr.gameObject.AddComponent<PropFxRef>();
            host.Setup(sr, look);
            return host;
        }

        /// <summary>Animates a UI prop image (shader only: no particles in the UI).</summary>
        public static UiPropFxRef Apply(Image img, PropLook look)
        {
            if (img == null || img.sprite == null || look == null) return null;
            var host = img.GetComponent<UiPropFxRef>();
            if (host == null) host = img.gameObject.AddComponent<UiPropFxRef>();
            host.Setup(img, look);
            return host;
        }
    }

    /// <summary>The animated look of one world sprite: its material instance and extras; reverts with reduced animation.</summary>
    public sealed class PropFxRef : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private Material _orig, _inst;
        private readonly List<GameObject> _extras = new List<GameObject>();
        public PropLook Look { get; private set; }
        public Material Instance => _inst;
        public bool Active { get; private set; }
        public bool HasSmoke, HasEmbers, HasGlow;

        public void Setup(SpriteRenderer sr, PropLook look)
        {
            Clear();
            _sr = sr; Look = look;
            _orig = sr.sharedMaterial;
            _inst = PropFx.MakeMaterial(look, PropFx.PhaseOf(transform.position));
            BuildExtras();
            AnimSettings.Changed -= Refresh; AnimSettings.Changed += Refresh;
            Refresh();
        }

        private void OnEnable() { AnimSettings.Changed -= Refresh; AnimSettings.Changed += Refresh; if (_sr != null) Refresh(); }
        private void OnDisable() { AnimSettings.Changed -= Refresh; }

        /// <summary>Applies the reduced-animation switch (it raises <see cref="AnimSettings.Changed"/>; tests call it directly in edit mode).</summary>
        public void Refresh()
        {
            if (_sr == null) return;
            Active = !AnimSettings.Reduced;
            if (_inst != null) _sr.sharedMaterial = Active ? _inst : _orig;
            foreach (var e in _extras) if (e != null) e.SetActive(Active);
        }

        private void BuildExtras()
        {
            var look = Look;
            var b = _sr.sprite.bounds;
            // extras are children of the sprite's object, sized in "sprite heights": the child's scale is the sprite's local height, so
            // (with hierarchy scaling) one unit of the builders = one sprite height on the board
            float u = b.size.y;
            Vector3 At(Vector2 uv) => new Vector3(b.min.x + uv.x * b.size.x, b.min.y + uv.y * b.size.y, 0);
            int order = _sr.sortingOrder;
            if (look.GlowDisc)
            {
                var mat = Resources.Load<Material>("Art/Anim/Materials/FireGlow");
                if (mat != null)
                {
                    var inst = new Material(mat) { hideFlags = HideFlags.DontSave };
                    var holder = new GameObject("FxGlow") { layer = gameObject.layer };
                    holder.transform.SetParent(transform, false);
                    holder.transform.localPosition = At(look.GlowAt);
                    holder.transform.localScale = Vector3.one * u * look.GlowSize;
                    var mr = AnimProp.Quad(holder.transform, "GlowQuad", inst, 1.2f, 1, 0, order - 1, 0.5f);
                    _extras.Add(holder); HasGlow = true;
                }
            }
            if (look.Embers)
            {
                var mat = Resources.Load<Material>("Art/Anim/Materials/Ember");
                if (mat != null)
                {
                    var holder = new GameObject("FxEmbers") { layer = gameObject.layer };
                    holder.transform.SetParent(transform, false);
                    holder.transform.localPosition = At(look.Emit);
                    holder.transform.localScale = Vector3.one * u;
                    AnimProp.Embers(holder.transform, mat, look.EmitWidth, order + 1);
                    _extras.Add(holder); HasEmbers = true;
                }
            }
            if (look.Smoke)
            {
                var mat = Resources.Load<Material>("Art/Anim/Materials/Puff");
                if (mat != null)
                {
                    var holder = new GameObject("FxSmoke") { layer = gameObject.layer };
                    holder.transform.SetParent(transform, false);
                    holder.transform.localPosition = At(look.Emit);
                    holder.transform.localScale = Vector3.one * u;
                    AnimProp.Smoke(holder.transform, mat, look.EmitWidth, order + 2, look.SmokeTint);
                    _extras.Add(holder); HasSmoke = true;
                }
            }
        }

        private void Clear()
        {
            foreach (var e in _extras) if (e != null) Destroy(e);
            _extras.Clear();
            HasSmoke = HasEmbers = HasGlow = false;
            if (_inst != null) { Destroy(_inst); _inst = null; }
        }

        private void OnDestroy()
        {
            AnimSettings.Changed -= Refresh;
            if (_sr != null && _orig != null && _sr.sharedMaterial == _inst) _sr.sharedMaterial = _orig;
            if (_inst != null) Destroy(_inst);
        }
    }

    /// <summary>The animated look of one UI prop image (shader only).</summary>
    public sealed class UiPropFxRef : MonoBehaviour
    {
        private Image _img;
        private Material _orig, _inst;
        public PropLook Look { get; private set; }
        public Material Instance => _inst;
        public bool Active { get; private set; }

        public void Setup(Image img, PropLook look)
        {
            if (_inst != null) Destroy(_inst);
            _img = img; Look = look;
            _orig = img.material;
            _inst = PropFx.MakeMaterial(look, PropFx.PhaseOf(transform.position));
            AnimSettings.Changed -= Refresh; AnimSettings.Changed += Refresh;
            Refresh();
        }

        private void OnEnable() { AnimSettings.Changed -= Refresh; AnimSettings.Changed += Refresh; if (_img != null) Refresh(); }
        private void OnDisable() { AnimSettings.Changed -= Refresh; }

        public void Refresh()
        {
            if (_img == null) return;
            Active = !AnimSettings.Reduced && _inst != null;
            _img.material = Active ? _inst : _orig;
        }

        private void OnDestroy()
        {
            AnimSettings.Changed -= Refresh;
            if (_inst != null) Destroy(_inst);
        }
    }
}
