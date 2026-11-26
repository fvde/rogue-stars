using System;
using System.Collections.Generic;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using UnityEngine;
using static RogueStars.Client.World.IsoPaint;

namespace RogueStars.Client.World
{
    /// <summary>How a path tile's block looks (top colour and engraving).</summary>
    public enum TileFace { Stone, Boost, Event, Special, Hazard, Curse, Boss, Floor }

    /// <summary>
    /// The iso board's art kit. Every piece is looked up through <see cref="ArtCatalog"/> first (imported generated art under
    /// <c>Art/Slice/…</c>, see <see cref="ArtCatalog.SliceMarker"/> etc.) and falls back to a clean code-drawn placeholder
    /// (<see cref="IsoPaint"/>), cached per key: tile blocks (top face + slab + decorated platform wall), tile markers (props
    /// standing on tiles), set pieces, scatter props, the ground and the HUD icons.
    /// </summary>
    public static class IsoArt
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>Drops every generated sprite (tests that swap the ArtCatalog loader).</summary>
        public static void ClearCache() => Cache.Clear();

        private static Sprite Get(string key, Func<Sprite> make)
        {
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            s = make();
            Cache[key] = s;
            return s;
        }

        // ---- palette ---------------------------------------------------------------------------------------------------

        public static readonly Color Ink = new Color32(0x2B, 0x22, 0x33, 255);
        /// <summary>The plinth top between the slabs (thin grout lines).</summary>
        public static readonly Color Gap = new Color32(0x3A, 0x3C, 0x50, 255);
        public static readonly Color WallLight = new Color32(0xDC, 0xBC, 0x80, 255);
        public static readonly Color WallDark = new Color32(0xB4, 0x8E, 0x56, 255);
        public static readonly Color WallBand = new Color32(0x7E, 0x58, 0x30, 255);
        public static readonly Color Bone = new Color32(0xF4, 0xE2, 0xA0, 255);
        public static readonly Color BoneShade = new Color32(0xD2, 0xB4, 0x6A, 255);

        /// <summary>Top colour of a tile face kind.</summary>
        public static Color FaceColor(TileFace f)
        {
            switch (f)
            {
                // colour is used sparingly: slate stone by default, soft accents only on special tiles
                // playtest 0.1.308: red means it costs you. Buffs are green; curses and traps are red (the boss's tiles dark red)
                case TileFace.Boost: return new Color32(0x9E, 0xCC, 0x6C, 255);
                case TileFace.Event: return new Color32(0x8C, 0xC0, 0xB6, 255);
                case TileFace.Special: return new Color32(0x9C, 0xB2, 0xC2, 255);
                case TileFace.Hazard: return new Color32(0xB8, 0x5E, 0x50, 255);
                case TileFace.Curse: return new Color32(0xCC, 0x6A, 0x66, 255);
                case TileFace.Boss: return new Color32(0x8C, 0x5A, 0x62, 255);
                case TileFace.Floor: return new Color32(0x86, 0xA8, 0xA2, 255);
                default: return new Color32(0x72, 0x76, 0x96, 255);
            }
        }

        /// <summary>
        /// The block face of a tile on the board: its kind's face, except a boss tile that only helps the player (it slows the
        /// boss and makes it no stronger, e.g. Cooling Pool) is drawn as a boost: a red face always costs the player.
        /// </summary>
        public static TileFace FaceOf(TileDef def) =>
            def == null ? TileFace.Stone : def.Category == TileCategory.BossTile && FriendlyBossTile(def) ? TileFace.Boost : FaceOf(def.Category);

        /// <summary>A boss tile that is good for the player: it slows the boss's awakening and makes the boss no stronger.</summary>
        public static bool FriendlyBossTile(TileDef def) => def != null && def.Category == TileCategory.BossTile && def.MeterDelta < 0 && def.BossStatPct <= 0;

        /// <summary>The red faces (curse, trap, the boss's tiles): landing there costs the player (tested).</summary>
        public static bool IsRedFace(TileFace f) => f == TileFace.Curse || f == TileFace.Hazard || f == TileFace.Boss;

        /// <summary>The block face of a tile kind on the board.</summary>
        public static TileFace FaceOf(TileCategory c)
        {
            switch (c)
            {
                case TileCategory.StatBuff: return TileFace.Boost;
                case TileCategory.Event: case TileCategory.Pickup: return TileFace.Event;
                case TileCategory.Shop: case TileCategory.Campfire: case TileCategory.Treasure: case TileCategory.Minigame: case TileCategory.Quest:
                case TileCategory.Start: case TileCategory.Miniboss: case TileCategory.Loot: return TileFace.Special;
                case TileCategory.Hazard: return TileFace.Hazard;
                case TileCategory.StatDebuff: return TileFace.Curse;
                case TileCategory.BossGate: case TileCategory.BossTile: return TileFace.Boss;
                default: return TileFace.Stone;
            }
        }

        /// <summary>The marker prop standing on a tile kind (ArtCatalog slice-markers id), or null (plain tile).</summary>
        public static string MarkerOf(TileCategory c)
        {
            switch (c)
            {
                case TileCategory.Enemy: return "enemy";
                case TileCategory.Elite: return "elite";
                case TileCategory.Miniboss: return "miniboss";
                case TileCategory.BossGate: return "boss";
                case TileCategory.BossTile: return "omen";
                case TileCategory.Event: return "event";
                case TileCategory.Pickup: return "mystery";
                case TileCategory.Treasure: return "chest";
                case TileCategory.Campfire: return "rest";
                case TileCategory.Shop: return "shop";
                case TileCategory.Minigame: return "minigame";
                case TileCategory.Quest: return "quest";
                case TileCategory.Start: return "portal";
                case TileCategory.Loot: case TileCategory.Shard: return "reward";
                default: return null;
            }
        }

        /// <summary>
        /// Height a marker is drawn at (world units; the code-drawn placeholders are cropped tight, so this is the visible
        /// height). The common enemy skull is small and uniform; elites, minibosses and the boss step up clearly.
        /// </summary>
        public static float MarkerHeight(string id)
        {
            switch (id)
            {
                case "rest": return 1.35f;
                case "shop": return 1.4f;
                case "boss": return 1.5f; // × 1.25 on the gate: ~2.5× the enemy skull, the run's own boss
                case "miniboss": return 1.6f; // playtest 0.1.308: the run's two minibosses stand on the path, readable from afar
                case "elite": return 1.0f;
                case "omen": return 0.82f;
                case "portal": return 1.4f;
                case "chest": return 0.82f;
                case "event": return 0.85f;
                case "mystery": return 0.88f;
                case "minigame": return 0.95f;
                case "quest": return 1.3f; // a rare visitor: readable from afar like the minibosses
                case "reward": return 0.85f;
                default: return 0.74f;
            }
        }

        // ---- tile blocks ---------------------------------------------------------------------------------------------

        private const float SX = Iso.StepX * Iso.Ppu, SY = Iso.StepY * Iso.Ppu;
        private const float SlabPx = Iso.Slab * Iso.Ppu, WallPx = Iso.Wall * Iso.Ppu, RisePx = Iso.Rise * Iso.Ppu;
        private const float Inset = 3;

        /// <summary>Neighbour bits of a plinth cell (a neighbour on the same height): the walls run on / are covered there.</summary>
        public const int NbBackLeft = 1, NbBackRight = 2, NbFrontLeft = 4, NbFrontRight = 8;

        /// <summary>How far (px of screen x) a wall face reaches past a cell edge where the wall runs on into the next cell.</summary>
        private const float Overlap = 2;

        /// <summary>Ground shadow under the plinth's walls (px below the wall foot).</summary>
        private const float ShadowPx = 22;

        /// <summary>The block's two front wall faces (<see cref="ExposedWalls"/>): left = under its front-left edge, right = front-right.</summary>
        public const int WallLeft = 1, WallRight = 2;

        /// <summary>
        /// The wall faces a plinth cell shows: only the front edges with no plinth cell in front of them. Back edges are never
        /// seen; a front edge with a neighbour is the joint between two cells (the neighbour's top and walls stand there).
        /// </summary>
        public static int ExposedWalls(int mask) =>
            ((mask & NbFrontLeft) == 0 ? WallLeft : 0) | ((mask & NbFrontRight) == 0 ? WallRight : 0);

        /// <summary>
        /// The neighbour mask of plinth cell (x, y) (<see cref="NbBackLeft"/> …): which side neighbours stand on the same
        /// continuous plinth (path tiles and set-piece floors of one height).
        /// </summary>
        public static int PlinthMask(Func<int, int, bool> same, int x, int y) =>
            (same(x, y + 1) ? NbBackLeft : 0) | (same(x + 1, y) ? NbBackRight : 0) | (same(x - 1, y) ? NbFrontLeft : 0) | (same(x, y - 1) ? NbFrontRight : 0);

        /// <summary>
        /// The block under a tile (pivot = the centre of its top face): one cell of the continuous plinth the path sits on —
        /// its top in the grout lines between slabs, the inset slab's two front faces in the kind's colour, the plinth walls
        /// down to the ground with the carved glyph band, the base ledge and a soft contact shadow on the ground. The
        /// <paramref name="mask"/> (<see cref="PlinthMask"/>) drops the corner seams where the plinth runs on, so a path reads
        /// as one raised platform, not as separate blocks. <paramref name="face"/> Floor = a plinth cell without a slab
        /// (set-piece slots, flush with the plinth).
        /// </summary>
        public static Sprite Block(TileFace face, int height, int mask = 0)
        {
            return Get("block:" + face + ":" + height + ":" + mask, () =>
            {
                float wall = WallPx + height * RisePx;
                bool slab = face != TileFace.Floor;
                float slabPx = slab ? SlabPx : 0;
                int w = Mathf.CeilToInt(2 * SX + 8), h = Mathf.CeilToInt(2 * SY + slabPx + wall + 10 + ShadowPx);
                var p = new IsoPaint(w, h);
                float cx = w / 2f, cy = h - 5 - SY;
                // plinth (full diamond) below the slab
                float py = cy - slabPx;
                var L = V(cx - SX, py); var B = V(cx, py - SY); var R = V(cx + SX, py); var T = V(cx, py + SY);
                var Ld = V(L.x, L.y - wall); var Bd = V(B.x, B.y - wall); var Rd = V(R.x, R.y - wall);
                int walls = ExposedWalls(mask);
                bool leftOpen = (walls & WallLeft) != 0, rightOpen = (walls & WallRight) != 0;
                // contact shadow on the ground under the open walls (light from the top left: deeper on the right)
                if (leftOpen) GroundShadow(p, Ld, Bd, ShadowPx * 0.55f, 0.3f);
                if (rightOpen) GroundShadow(p, Bd, Rd, ShadowPx, 0.42f);
                // wall faces only on the exposed edges: a joint to the next cell has none (nothing to overlap or cut in)
                // where a wall runs on into the neighbour's, it reaches a little past the cell edge so the two sprites
                // overlap (no filtered seam line between cells); a free end stops at the edge under its ink seam
                if (leftOpen) WallFace(p, L, B, wall, WallLight, true, (mask & NbBackLeft) != 0 ? Overlap : 0, (mask & NbFrontRight) != 0 ? Overlap : 0);
                if (rightOpen) WallFace(p, B, R, wall, WallDark, false, (mask & NbFrontLeft) != 0 ? Overlap : 0, (mask & NbBackRight) != 0 ? Overlap : 0);
                var ink = new Color(Ink.r, Ink.g, Ink.b, 0.85f);
                if (leftOpen) p.Line(Ld, Bd, 3, Ink);
                if (rightOpen) p.Line(Bd, Rd, 3, Ink);
                // corner seams only where the plinth turns (a convex corner) or ends
                if (leftOpen && rightOpen) p.Line(B, Bd, 2.5f, ink);
                if ((mask & NbBackLeft) == 0 && leftOpen) p.Line(L, Ld, 2.5f, Ink);
                if ((mask & NbBackRight) == 0 && rightOpen) p.Line(R, Rd, 2.5f, Ink);
                p.Poly(new[] { L, B, R, T }, slab ? Gap : FaceColor(TileFace.Floor));
                // the plinth's top rim catches the light along its open front edges
                if (leftOpen) p.Line(L + V(2, -1), B + V(0, 1.5f), 2.5f, Shade(Gap, 0.25f));
                if (rightOpen) p.Line(B + V(0, 1.5f), R + V(-2, -1), 2.5f, Shade(Gap, 0.12f));
                if (!slab)
                {
                    // the floor of a set-piece slot: carved stone flush with the plinth, faint engraved arcs and joints
                    var fc = FaceColor(TileFace.Floor);
                    p.Poly(new[] { L, B, R, T }, (x, y) =>
                    {
                        float lit = Mathf.Clamp01((y - B.y) / (T.y - B.y));
                        return Color.Lerp(Shade(fc, -0.1f), Shade(fc, 0.06f), lit);
                    });
                    var seam = Shade(fc, -0.24f);
                    p.Line(Vector2.Lerp(L, T, 0.5f), Vector2.Lerp(B, R, 0.5f), 2, seam);
                    p.Line(Vector2.Lerp(L, B, 0.5f), Vector2.Lerp(T, R, 0.5f), 2, seam);
                    var arc = new List<Vector2>();
                    for (int i = 0; i <= 8; i++) { float a = Mathf.Lerp(200, 340, i / 8f) * Mathf.Deg2Rad; arc.Add(V(cx + Mathf.Cos(a) * SX * 0.5f, py + SY * 0.2f + Mathf.Sin(a) * SY * 0.5f)); }
                    p.Stroke(arc, 2.5f, Shade(fc, -0.18f), false);
                    if (leftOpen) p.Line(L, B, 2.5f, ink);
                    if (rightOpen) p.Line(B, R, 2.5f, ink);
                    return p.ToSprite("rs_floor_" + height + "_" + mask, V(cx, cy));
                }
                // the inset slab's front faces (the grout shows between slabs)
                var col = FaceColor(face);
                float k = 1 - Inset / SX;
                var l = V(cx - SX * k, cy); var b = V(cx, cy - SY * k); var r = V(cx + SX * k, cy);
                p.Poly(new[] { l, b, V(b.x, b.y - slabPx), V(l.x, l.y - slabPx) }, Shade(col, -0.26f));
                p.Poly(new[] { b, r, V(r.x, r.y - slabPx), V(b.x, b.y - slabPx) }, Shade(col, -0.42f));
                p.Line(V(l.x + 2, l.y - 2), V(b.x, b.y - 2), 2, Shade(col, -0.1f));
                p.Stroke(new[] { l, V(l.x, l.y - slabPx), V(b.x, b.y - slabPx), V(r.x, r.y - slabPx), r }, 2.5f, ink, false);
                p.Line(b, V(b.x, b.y - slabPx), 2, new Color(Ink.r, Ink.g, Ink.b, 0.5f));
                return p.ToSprite("rs_block_" + face + "_" + height + "_" + mask, V(cx, cy));
            });
        }

        /// <summary>A soft shadow on the ground below a wall's foot edge a→b, fading over <paramref name="depth"/> px.</summary>
        private static void GroundShadow(IsoPaint p, Vector2 a, Vector2 b, float depth, float alpha)
        {
            var e = b - a;
            p.Poly(new[] { a, b, V(b.x, b.y - depth), V(a.x, a.y - depth) }, (x, y) =>
            {
                float edge = a.y + e.y * Mathf.Clamp01((x - a.x) / e.x);
                float t = Mathf.Clamp01((edge - y) / depth);
                return new Color(0.04f, 0.1f, 0.12f, alpha * (1 - t) * (1 - t));
            });
        }

        /// <summary>The carved band's glyph: a hooked spiral key, 7 × 5 cells of 4 px (rows top → bottom), repeating along the wall.</summary>
        private static readonly string[] KeyRows = { "######.", "#....#.", "#.##.#.", "#..#.#.", "####.##" };

        /// <summary>
        /// One plinth wall face from edge a→b straight down <paramref name="depth"/> px: a lit cap, the carved band of hooked
        /// spiral keys between two rails, plain stone, the base ledge and ambient occlusion where it meets the ground. The key
        /// repeats every 28 px of screen x (3 per cell), so neighbouring walls continue it seamlessly.
        /// </summary>
        private static void WallFace(IsoPaint p, Vector2 a, Vector2 b, float depth, Color baseCol, bool left, float extA = 0, float extB = 0)
        {
            var e = b - a;
            var a2 = a - e / e.x * extA; var b2 = b + e / e.x * extB; // reach extA / extB px of screen x past the ends
            p.Poly(new[] { a2, b2, V(b2.x, b2.y - depth), V(a2.x, a2.y - depth) }, (x, y) =>
            {
                float u = (x - a.x) / e.x;               // 0..1 along the edge
                float v = a.y + e.y * u - y;             // px below the top edge
                float along = x - a.x;
                const float band0 = 12, band1 = 32;
                var c = baseCol;
                if (v < 4) c = Shade(baseCol, 0.3f);                                    // lit cap
                else if (v < 7) c = Shade(baseCol, -0.22f);                             // under the cap
                else if (v >= band0 - 3 && v < band0 - 1) c = Shade(baseCol, -0.3f);    // upper rail
                else if (v >= band0 && v < band1)
                {
                    int col = Mathf.FloorToInt(((along % 28) + 28) % 28 / 4f), row = Mathf.FloorToInt((v - band0) / 4f);
                    bool key = row >= 0 && row < 5 && col >= 0 && col < 7 && KeyRows[row][col] == '#';
                    c = key ? WallBand : Shade(baseCol, -0.05f);
                }
                else if (v >= band1 + 1 && v < band1 + 3) c = Shade(baseCol, -0.3f);   // lower rail
                else if (v > depth - 13 && v <= depth - 10) c = Shade(baseCol, 0.22f);  // base ledge
                else if (v > depth - 10) c = Shade(baseCol, -0.2f - 0.2f * Mathf.Clamp01((v - (depth - 10)) / 10f)); // base + occlusion
                else c = Shade(baseCol, -0.04f + 0.05f * Mathf.PerlinNoise(x * 0.11f, y * 0.13f));
                if (!left) c = Shade(c, -0.1f);
                return c;
            });
        }

        /// <summary>
        /// Screen direction (sprite pixels, y up, normalised) of a grid heading (0 +Y up-left, 1 +X up-right, 2 −Y
        /// down-right, 3 −X down-left): the way a tile's chevrons point.
        /// </summary>
        public static Vector2 ChevronDir(int heading)
        {
            var st = Iso.Step(heading);
            return new Vector2((st.x - st.y) * SX, (st.x + st.y) * SY).normalized;
        }

        /// <summary>
        /// The top face of a tile (pivot = its centre): bevel, outline, a socket for the marker, the kind's engraving. Boost
        /// and curse chevrons point along <paramref name="heading"/>: the way the path goes on from the tile
        /// (WorldBoard.PathHeading).
        /// </summary>
        public static Sprite Top(TileFace face, int heading)
        {
            heading &= 3;
            bool directional = face == TileFace.Boost || face == TileFace.Curse;
            return Get("top:" + face + ":" + (directional ? heading : 0), () =>
            {
                var tex = ArtCatalog.SliceTexture("tile_top_moss");
                int w = Mathf.CeilToInt(2 * SX + 4), h = Mathf.CeilToInt(2 * SY + 4);
                var p = new IsoPaint(w, h);
                float cx = w / 2f, cy = h / 2f;
                float k = 1 - Inset / SX;
                var l = V(cx - SX * k, cy); var b = V(cx, cy - SY * k); var r = V(cx + SX * k, cy); var t = V(cx, cy + SY * k);
                var col = FaceColor(face);
                Color[] texPx = null;
                int tw = 0, th = 0;
                if (tex != null && tex.texture != null && tex.texture.isReadable)
                {
                    texPx = tex.texture.GetPixels();
                    tw = tex.texture.width; th = tex.texture.height;
                }
                p.Poly(new[] { l, b, r, t }, (x, y) =>
                {
                    // lighter towards the back corner (light from above), texture slot multiplied in
                    float lit = Mathf.Clamp01((y - b.y) / (t.y - b.y));
                    var c = Color.Lerp(Shade(col, -0.06f), Shade(col, 0.1f), lit);
                    if (texPx != null)
                    {
                        var s = texPx[Mathf.Clamp((int)(y / h * th), 0, th - 1) * tw + Mathf.Clamp((int)(x / w * tw), 0, tw - 1)];
                        c = new Color(c.r * (0.74f + 0.26f * s.r * 1.4f), c.g * (0.74f + 0.26f * s.g * 1.4f), c.b * (0.74f + 0.26f * s.b * 1.4f), 1);
                    }
                    return c;
                });
                // bevel highlight along the two back edges, shade along the front ones
                p.Line(Vector2.Lerp(l, t, 0.06f), Vector2.Lerp(t, l, 0.06f) + V(0, -3), 5, Shade(col, 0.28f));
                p.Line(Vector2.Lerp(t, r, 0.06f) + V(0, -3), Vector2.Lerp(r, t, 0.06f), 5, Shade(col, 0.2f));
                p.Line(Vector2.Lerp(l, b, 0.05f) + V(0, 2), Vector2.Lerp(b, l, 0.05f) + V(0, 3), 4, Shade(col, -0.16f));
                p.Line(Vector2.Lerp(b, r, 0.05f) + V(0, 3), Vector2.Lerp(r, b, 0.05f) + V(0, 2), 4, Shade(col, -0.22f));
                p.Stroke(new[] { l, b, r, t }, 3, Ink);
                var engrave = Shade(col, -0.2f);
                switch (face)
                {
                    case TileFace.Boost:
                    case TileFace.Curse:
                    {
                        // two chevrons pointing the way the path goes on (boost and curse alike)
                        var dir = ChevronDir(heading);
                        var side = new Vector2(-dir.y * 1.45f, dir.x * 0.7f);
                        for (int i = 0; i < 2; i++)
                        {
                            var tip = V(cx, cy) + dir * (14 - i * 22);
                            p.Line(tip - dir * 18 + side * 22, tip, 9, engrave);
                            p.Line(tip - dir * 18 - side * 22, tip, 9, engrave);
                        }
                        break;
                    }
                    case TileFace.Event:
                        QuestionMark(p, cx, cy - 2, 0.9f, engrave, 8);
                        break;
                    case TileFace.Hazard:
                        for (int i = -1; i <= 1; i++)
                        {
                            var bx = cx + i * 30;
                            p.Shape(new[] { V(bx - 10, cy - 8 - Math.Abs(i) * 6), V(bx + 10, cy - 8 - Math.Abs(i) * 6), V(bx, cy + 18 - Math.Abs(i) * 6) }, Shade(col, 0.35f), Ink, 2);
                        }
                        break;
                    default:
                        // the socket a marker stands in (stone) / a soft glow ring (special)
                        p.Ellipse(cx, cy - 3, SX * 0.3f, SY * 0.3f, Shade(col, face == TileFace.Special ? 0.18f : -0.16f));
                        if (face == TileFace.Stone) p.Line(V(cx + SX * 0.35f, cy + SY * 0.25f), V(cx + SX * 0.55f, cy + SY * 0.05f), 2, Shade(col, -0.25f));
                        break;
                }
                return p.ToSprite("rs_top_" + face + "_" + heading, V(cx, cy));
            });
        }

        private static void QuestionMark(IsoPaint p, float cx, float cy, float s, Color c, float width)
        {
            // an arc (hook) and a dot, squashed onto the iso face
            var pts = new List<Vector2>();
            for (int i = 0; i <= 10; i++)
            {
                float a = Mathf.Lerp(160, -60, i / 10f) * Mathf.Deg2Rad;
                pts.Add(V(cx + Mathf.Cos(a) * 16 * s, cy + 18 * s + Mathf.Sin(a) * 12 * s));
            }
            pts.Add(V(cx, cy + 2 * s));
            p.Stroke(pts, width * s, c, false);
            p.Ellipse(cx, cy - 12 * s, 5 * s, 4.2f * s, c);
        }

        // ---- markers ---------------------------------------------------------------------------------------------------

        /// <summary>A tile marker prop (slice-markers id; pivot at its foot): the imported art, else a drawn placeholder.</summary>
        public static Sprite Marker(string id) =>
            (id == "boss" ? null : ArtCatalog.SliceMarker(id)) ?? Get("marker:" + id, () => DrawMarker(id));

        /// <summary>
        /// The boss gate's marker: the run's own boss, drawn with the sprite its battle uses (ArtCatalog.EnemyOrFamily: its
        /// sprite, else its family's); the code-drawn skull only when there is no art at all. No generic slice "boss" marker.
        /// </summary>
        public static Sprite BossMarker(ContentDb content, string bossId)
        {
            var boss = content != null && bossId != null && content.RunBosses.TryGetValue(bossId, out var b) ? b : null;
            var art = boss != null ? ArtCatalog.EnemyOrFamily(boss.EnemyId, content, out _) : null;
            return art != null ? art : Marker("boss");
        }

        /// <summary>The boss marker's width limit (it dominates its tile like a set piece).</summary>
        public const float BossMarkerWidth = Iso.TileW * 1.8f;

        private static Sprite DrawMarker(string id)
        {
            switch (id)
            {
                case "elite": return Skull("elite", SkullGold, true, false, 1f);
                case "miniboss": return Skull("miniboss", new Color32(0xF0, 0xA4, 0x6C, 255), true, true, 1.1f);
                case "boss": return Skull("boss", new Color32(0xE0, 0x5A, 0x60, 255), true, true, 1.2f);
                case "omen": return Omen();
                case "event": return EventScroll();
                case "mystery": return MysteryStone();
                case "chest": return Chest();
                case "rest": return Campfire();
                case "shop": return Cart();
                case "minigame": return Arcade();
                case "quest": return Lantern();
                case "portal": return PortalRing();
                case "reward": return Crystals("reward", new Color32(0x72, 0xE6, 0xFF, 255), 3, 120, 120);
                default: return Skull("enemy", SkullGold, false, false, 1f);
            }
        }

        /// <summary>The warm gold of the enemy skulls.</summary>
        public static readonly Color SkullGold = new Color32(0xF0, 0xCC, 0x6A, 255);

        /// <summary>
        /// An alien skull (big cranium, glowing eyes, two antenna nubs), cropped tight: the common enemy is a small plain gold
        /// skull; elites get curled horns, minibosses and the boss a crown over the horns (bigger through MarkerHeight).
        /// </summary>
        private static Sprite Skull(string name, Color bone, bool horns, bool crown, float scale)
        {
            float s = scale;
            float halfW = (horns ? 66 : 48) * s;
            int w = Mathf.CeilToInt(halfW * 2 + 8), h = Mathf.CeilToInt(((crown ? 132 : horns ? 132 : 116)) * s + 6);
            var p = new IsoPaint(w, h);
            float cx = w / 2f, y0 = 2;
            var shade = Shade(bone, -0.32f);
            var light = Shade(bone, 0.35f);
            if (horns)
            {
                var horn = crown ? (Color)new Color32(0x6E, 0x2A, 0x38, 255) : (Color)new Color32(0xE8, 0xD6, 0xA8, 255);
                foreach (int side in new[] { -1, 1 })
                {
                    var pts = new[] { V(cx + side * 24 * s, y0 + 74 * s), V(cx + side * 48 * s, y0 + 76 * s), V(cx + side * 64 * s, y0 + 100 * s), V(cx + side * 58 * s, y0 + 128 * s), V(cx + side * 50 * s, y0 + 104 * s), V(cx + side * 34 * s, y0 + 94 * s) };
                    p.Shape(pts, VGrad(Shade(horn, -0.3f), Shade(horn, 0.15f), y0 + 74 * s, y0 + 128 * s), Ink, 4.5f * s);
                }
            }
            // antenna nubs (alien)
            foreach (int side in new[] { -1, 1 })
            {
                p.Line(V(cx + side * 12 * s, y0 + 88 * s), V(cx + side * 20 * s, y0 + 106 * s), 6 * s, Ink);
                p.Line(V(cx + side * 12 * s, y0 + 88 * s), V(cx + side * 20 * s, y0 + 106 * s), 3 * s, shade);
                p.Blob(cx + side * 20 * s, y0 + 108 * s, 6 * s, 6 * s, crown ? (Color)new Color32(0xFF, 0x6A, 0x5A, 255) : light, Ink, 3.5f * s);
            }
            // jaw, then the cranium over it
            var jaw = RoundRect(cx - 22 * s, y0 + 4 * s, 44 * s, 30 * s, 10 * s);
            p.Shape(jaw, VGrad(shade, bone, y0 + 4 * s, y0 + 34 * s), Ink, 5 * s);
            for (int i = -1; i <= 1; i++) p.Line(V(cx + i * 10 * s, y0 + 7 * s), V(cx + i * 10 * s, y0 + 19 * s), 3 * s, Ink);
            p.Blob(cx, y0 + 56 * s, 44 * s, 38 * s, IsoPaint.Radial(light, shade, cx - 14 * s, y0 + 70 * s, 56 * s), Ink, 5.5f * s);
            // a gloss on the cranium (light from the top left)
            p.Ellipse(cx - 18 * s, y0 + 76 * s, 11 * s, 7 * s, new Color(1, 1, 1, 0.55f));
            // eyes: big dark sockets with glowing pupils
            foreach (int side in new[] { -1, 1 })
            {
                p.Ellipse(cx + side * 17 * s, y0 + 50 * s, 13 * s, 13 * s, Ink);
                p.Ellipse(cx + side * 16 * s, y0 + 49 * s, 5.5f * s, 6 * s, crown ? new Color32(0xFF, 0xE0, 0x5A, 255) : new Color32(0xF0, 0x3A, 0x30, 255));
                p.Ellipse(cx + side * 14.5f * s, y0 + 51 * s, 2 * s, 2 * s, Color.white);
            }
            p.Shape(new[] { V(cx - 4 * s, y0 + 32 * s), V(cx + 4 * s, y0 + 32 * s), V(cx, y0 + 39 * s) }, Ink, Ink, 1);
            if (crown)
            {
                var gold = new Color32(0xFF, 0xC8, 0x3C, 255);
                var c = new[] { V(cx - 27 * s, y0 + 84 * s), V(cx - 31 * s, y0 + 112 * s), V(cx - 14 * s, y0 + 98 * s), V(cx, y0 + 124 * s), V(cx + 14 * s, y0 + 98 * s), V(cx + 31 * s, y0 + 112 * s), V(cx + 27 * s, y0 + 84 * s) };
                p.Shape(c, VGrad(Shade(gold, -0.2f), Shade(gold, 0.25f), y0 + 84 * s, y0 + 124 * s), Ink, 4.5f * s);
                p.Circle(cx, y0 + 92 * s, 5 * s, new Color32(0xE8, 0x3A, 0x5A, 255));
            }
            return p.ToSprite("rs_marker_" + name, V(cx, y0));
        }

        /// <summary>A boss-omen tile (it feeds or calms the boss): a small dark obelisk with a glowing skull rune.</summary>
        private static Sprite Omen()
        {
            var p = new IsoPaint(96, 130);
            float cx = 48;
            var stone = (Color)new Color32(0x5A, 0x4A, 0x62, 255);
            p.Shape(new[] { V(cx - 30, 4), V(cx + 30, 4), V(cx + 22, 92), V(cx, 124), V(cx - 22, 92) }, VGrad(Shade(stone, -0.3f), Shade(stone, 0.2f), 4, 124), Ink, 5);
            p.Line(V(cx - 14, 12), V(cx - 10, 96), 4, new Color(1, 1, 1, 0.18f));
            var glow = (Color)new Color32(0xFF, 0x6A, 0x6A, 255);
            p.Blob(cx, 60, 14, 13, glow, Shade(glow, -0.5f), 3);
            p.Ellipse(cx - 5, 60, 3.5f, 4, Ink); p.Ellipse(cx + 5, 60, 3.5f, 4, Ink);
            p.Shape(RoundRect(cx - 34, 2, 68, 14, 6), Shade(stone, -0.15f), Ink, 4);
            return p.ToSprite("rs_marker_omen", V(cx, 2));
        }

        private static Sprite EventScroll()
        {
            var p = new IsoPaint(150, 130);
            float cx = 75;
            var paper = (Color)new Color32(0xF6, 0xE8, 0xC4, 255);
            // a tilted data scroll: sheet with rolled ends, teal glyph lines
            var sheet = new[] { V(30, 34), V(108, 18), V(124, 92), V(46, 108) };
            p.Shape(sheet, VGrad(Shade(paper, -0.12f), paper, 18, 108), Ink, 4);
            p.Blob(38, 104, 10, 10, Shade(paper, -0.1f), Ink, 4);
            p.Blob(117, 22, 10, 10, Shade(paper, -0.1f), Ink, 4);
            var glyph = (Color)new Color32(0x2E, 0xA8, 0x9A, 255);
            for (int i = 0; i < 4; i++)
            {
                var a = Vector2.Lerp(V(44, 44), V(56, 98), i / 3.6f) + V(4, 0);
                p.Line(a, a + V(48 - i * 6, -10 + i), 4, glyph);
            }
            // a stylus
            p.Line(V(110, 60), V(140, 118), 9, Ink);
            p.Line(V(110, 60), V(140, 118), 5, new Color32(0x6C, 0xE4, 0xD2, 255));
            p.Circle(141, 120, 5, new Color32(0xFF, 0x9F, 0xE8, 255));
            return p.ToSprite("rs_marker_event", V(cx, 10));
        }

        private static Sprite MysteryStone()
        {
            var p = new IsoPaint(130, 140);
            float cx = 65;
            var stone = (Color)new Color32(0xA3, 0xAD, 0xC4, 255);
            var pts = new[] { V(22, 14), V(108, 14), V(116, 70), V(96, 124), V(36, 128), V(14, 72) };
            p.Shape(pts, IsoPaint.Radial(Shade(stone, 0.22f), Shade(stone, -0.25f), 50, 96, 110), Ink, 5);
            p.Line(V(92, 22), V(104, 66), 4, Shade(stone, -0.3f));
            QuestionMark(p, cx, 66, 1.6f, new Color32(0x3C, 0xD8, 0xC0, 255), 9);
            return p.ToSprite("rs_marker_mystery", V(cx, 10));
        }

        private static Sprite Chest()
        {
            var p = new IsoPaint(150, 130);
            float cx = 75;
            var body = (Color)new Color32(0x7E, 0x58, 0xD0, 255);
            var gold = (Color)new Color32(0xFF, 0xC8, 0x48, 255);
            p.Shape(RoundRect(22, 12, 106, 62, 10), VGrad(Shade(body, -0.25f), body, 12, 74), Ink, 5);
            // domed lid
            var lid = new List<Vector2> { V(20, 70) };
            for (int i = 0; i <= 12; i++) { float a = Mathf.PI * i / 12; lid.Add(V(cx - Mathf.Cos(a) * 55, 70 + Mathf.Sin(a) * 40)); }
            lid.Add(V(130, 70));
            p.Shape(lid, VGrad(body, Shade(body, 0.3f), 70, 110), Ink, 5);
            foreach (float x in new[] { 40f, 110f }) { p.Line(V(x, 14), V(x, 104 - Mathf.Abs(x - cx) * 0.35f), 9, Ink); p.Line(V(x, 16), V(x, 102 - Mathf.Abs(x - cx) * 0.35f), 5, gold); }
            p.Blob(cx, 66, 13, 15, gold, Ink, 4);
            p.Ellipse(cx, 64, 5, 6, new Color32(0x3C, 0xF0, 0xD0, 255));
            return p.ToSprite("rs_marker_chest", V(cx, 10));
        }

        private static Sprite Campfire()
        {
            var p = new IsoPaint(190, 200);
            float cx = 95;
            var stone = (Color)new Color32(0x8C, 0x90, 0xA6, 255);
            // a ring of stones around a dark pit
            p.Blob(cx, 34, 74, 28, Shade(stone, -0.2f), Ink, 5);
            p.Ellipse(cx, 38, 54, 18, new Color32(0x3A, 0x30, 0x40, 255));
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2;
                p.Blob(cx + Mathf.Cos(a) * 64, 34 + Mathf.Sin(a) * 23, 13, 9, Shade(stone, Mathf.Sin(a) > 0 ? -0.05f : 0.12f), Ink, 3);
            }
            var log = (Color)new Color32(0xB0, 0x6A, 0x3E, 255);
            p.Line(V(cx - 40, 30), V(cx + 30, 52), 16, Ink); p.Line(V(cx - 40, 30), V(cx + 30, 52), 10, log);
            p.Line(V(cx + 40, 30), V(cx - 30, 52), 16, Ink); p.Line(V(cx + 40, 30), V(cx - 30, 52), 10, Shade(log, -0.15f));
            // a cosmic green flame
            var flame = new[] { V(cx - 38, 52), V(cx - 46, 96), V(cx - 26, 84), V(cx - 22, 138), V(cx - 4, 110), V(cx + 6, 182), V(cx + 20, 116), V(cx + 34, 140), V(cx + 34, 92), V(cx + 46, 100), V(cx + 38, 52) };
            p.Shape(flame, VGrad(new Color32(0x3A, 0xE0, 0x9A, 255), new Color32(0xC8, 0xFF, 0xE6, 255), 52, 170), new Color32(0x1E, 0x6A, 0x50, 255), 4);
            var inner = new[] { V(cx - 18, 56), V(cx - 20, 90), V(cx - 6, 82), V(cx + 4, 126), V(cx + 14, 88), V(cx + 22, 96), V(cx + 18, 56) };
            p.Poly(inner, new Color32(0xE8, 0xFF, 0xF4, 255));
            p.Circle(cx + 30, 160, 4, new Color32(0xC8, 0xFF, 0xE6, 255));
            p.Circle(cx - 34, 150, 3, new Color32(0xC8, 0xFF, 0xE6, 255));
            return p.ToSprite("rs_marker_rest", V(cx, 14));
        }

        private static Sprite Cart()
        {
            var p = new IsoPaint(200, 190);
            float cx = 100;
            var wood = (Color)new Color32(0xC0, 0x84, 0x4E, 255);
            // goods
            var goods = new Color32[] { new Color32(0xFF, 0x6A, 0x8A, 255), new Color32(0x6C, 0xD8, 0xFF, 255), new Color32(0xFF, 0xD0, 0x48, 255), new Color32(0x9C, 0xF0, 0x8A, 255), new Color32(0xB8, 0x8C, 0xFF, 255) };
            for (int i = 0; i < 5; i++) p.Blob(40 + i * 30, 98 + (i % 2) * 10, 15, 15, goods[i], Ink, 4);
            p.Shape(new[] { V(22, 40), V(178, 40), V(186, 100), V(14, 100) }, VGrad(Shade(wood, -0.25f), wood, 40, 100), Ink, 5);
            for (int i = 1; i < 4; i++) p.Line(V(22 + i * 39, 44), V(18 + i * 40, 96), 3, Shade(wood, -0.35f));
            // awning poles and a striped awning
            p.Line(V(28, 98), V(30, 160), 7, Ink); p.Line(V(172, 98), V(170, 160), 7, Ink);
            for (int i = 0; i < 6; i++)
            {
                float x0 = 16 + i * 28;
                p.Shape(new[] { V(x0, 150), V(x0 + 28, 150), V(x0 + 30, 176), V(x0 + 2, 176) }, i % 2 == 0 ? (Color)new Color32(0xE8, 0x4E, 0x64, 255) : (Color)new Color32(0xFF, 0xF0, 0xD8, 255), Ink, 3.5f);
            }
            // wheels
            foreach (float x in new[] { 46f, 154f })
            {
                p.Blob(x, 34, 20, 20, new Color32(0x5A, 0x4A, 0x60, 255), Ink, 5);
                p.Circle(x, 34, 6, new Color32(0xB8, 0xB0, 0xC8, 255));
            }
            return p.ToSprite("rs_marker_shop", V(cx, 14));
        }

        private static Sprite Arcade()
        {
            var p = new IsoPaint(140, 140);
            float cx = 70;
            var body = (Color)new Color32(0x3E, 0xC8, 0xB0, 255);
            p.Shape(RoundRect(24, 12, 92, 52, 14), VGrad(Shade(body, -0.3f), body, 12, 64), Ink, 5);
            p.Line(V(cx, 60), V(cx - 8, 104), 10, Ink); p.Line(V(cx, 60), V(cx - 8, 104), 5, new Color32(0xD8, 0xD8, 0xE8, 255));
            p.Blob(cx - 9, 110, 17, 17, new Color32(0xFF, 0x4E, 0x6A, 255), Ink, 5);
            p.Ellipse(cx - 14, 116, 5, 4, new Color(1, 1, 1, 0.8f));
            p.Blob(cx + 28, 40, 8, 8, new Color32(0xFF, 0xD0, 0x48, 255), Ink, 3);
            p.Blob(cx - 28, 40, 8, 8, new Color32(0x9C, 0x8C, 0xFF, 255), Ink, 3);
            return p.ToSprite("rs_marker_minigame", V(cx, 10));
        }

        /// <summary>The Run Quest visitor's marker: a little glass lantern with a star inside (Mothwick's lantern).</summary>
        private static Sprite Lantern()
        {
            var p = new IsoPaint(130, 190);
            float cx = 65;
            var brass = (Color)new Color32(0xD8, 0xA0, 0x48, 255);
            var glow = (Color)new Color32(0xFF, 0xE6, 0x8A, 255);
            p.Ellipse(cx, 90, 58, 62, new Color(1f, 0.9f, 0.5f, 0.28f));
            p.Shape(RoundRect(cx - 36, 6, 72, 20, 8), VGrad(Shade(brass, -0.3f), brass, 6, 26), Ink, 5);
            p.Shape(RoundRect(cx - 30, 24, 60, 100, 18), IsoPaint.Radial(Color.white, glow, cx, 74, 60), Ink, 6);
            p.Shape(Star(cx, 74, 22, 5, 0.45f, 90), new Color32(0xFF, 0xC8, 0x30, 255), Ink, 4);
            p.Shape(RoundRect(cx - 34, 120, 68, 18, 8), VGrad(Shade(brass, -0.3f), brass, 120, 138), Ink, 5);
            p.Line(V(cx - 18, 138), V(cx, 168), 6, Ink); p.Line(V(cx + 18, 138), V(cx, 168), 6, Ink);
            p.Blob(cx, 170, 10, 10, brass, Ink, 4);
            p.Ellipse(cx - 14, 96, 6, 12, new Color(1, 1, 1, 0.75f));
            return p.ToSprite("rs_marker_quest", V(cx, 8));
        }

        private static Sprite PortalRing()
        {
            var p = new IsoPaint(170, 200);
            float cx = 85;
            var stone = (Color)new Color32(0x8C, 0x7C, 0xB8, 255);
            p.Blob(cx, 100, 66, 84, stone, Ink, 6);
            p.Ellipse(cx, 100, 48, 66, IsoPaint.Radial(new Color32(0xFF, 0x9C, 0xF0, 255), new Color32(0x3A, 0x1C, 0x6A, 255), cx, 100, 60));
            for (int i = 0; i < 3; i++)
            {
                var pts = new List<Vector2>();
                for (int j = 0; j <= 10; j++) { float a = (i * 120 + j * 22) * Mathf.Deg2Rad; float rr = 10 + j * 3.4f; pts.Add(V(cx + Mathf.Cos(a) * rr, 100 + Mathf.Sin(a) * rr * 1.3f)); }
                p.Stroke(pts, 4, new Color(1, 0.85f, 1, 0.7f), false);
            }
            p.Shape(RoundRect(cx - 44, 8, 88, 18, 8), Shade(stone, -0.2f), Ink, 4);
            return p.ToSprite("rs_marker_portal", V(cx, 12));
        }

        private static Sprite Crystals(string name, Color col, int n, int w, int h)
        {
            var p = new IsoPaint(w, h);
            float cx = w / 2f;
            for (int i = 0; i < n; i++)
            {
                float off = (i - (n - 1) / 2f) * w * 0.2f;
                float tall = h * (i == n / 2 ? 0.82f : 0.55f);
                float wd = w * 0.13f;
                float tilt = off * 0.35f;
                var pts = new[] { V(cx + off - wd, 12), V(cx + off + wd, 12), V(cx + off + wd + tilt * 0.3f, tall * 0.7f), V(cx + off + tilt, tall), V(cx + off - wd + tilt * 0.3f, tall * 0.7f) };
                p.Shape(pts, VGrad(Shade(col, -0.35f), Shade(col, 0.35f), 12, tall), Ink, 4);
                p.Line(V(cx + off - wd * 0.2f, 18), V(cx + off + tilt * 0.8f - wd * 0.2f, tall * 0.9f), 3, new Color(1, 1, 1, 0.55f));
            }
            return p.ToSprite("rs_marker_" + name, V(cx, 10));
        }


        // ---- set pieces & scatter ------------------------------------------------------------------------------------

        /// <summary>Set pieces in the order slots pick them (ArtCatalog slice-setpieces ids).</summary>
        public static readonly string[] SetPieceIds = { "portal_well", "crystal_spire", "giant_mushroom", "crashed_saucer", "alien_tree", "ruined_arch", "burrow_mound", "time_clock" };
        /// <summary>Scatter props (ArtCatalog slice-scatter ids).</summary>
        public static readonly string[] ScatterIds = { "rock_small", "rock_pile", "bush", "flowers", "fern", "pebble_glow", "stump", "crystal_small", "mushrooms", "barrel", "antenna", "bones" };

        /// <summary>
        /// A set piece (pivot at its foot, i.e. the slot's centre on the ground). Placeholders: a portal well (a big flat
        /// ring, footprint-sized), a crystal spire, a giant mushroom, a crashed saucer and an alien tree; the others borrow.
        /// </summary>
        public static Sprite SetPiece(string id) =>
            ArtCatalog.SliceSetPiece(id) ?? Get("setpiece:" + id, () => DrawSetPiece(id));

        /// <summary>
        /// A set piece for a slot: one of its imported variants (main + alternates <c>&lt;id&gt;_2</c> …) picked by
        /// <paramref name="pick"/> (a stable per-slot hash), else the main sprite / the drawn placeholder.
        /// </summary>
        public static Sprite SetPiece(string id, int pick)
        {
            int n = ArtCatalog.SliceSetPieceVariants(id);
            return n > 1 ? ArtCatalog.SliceSetPiece(id, (pick & int.MaxValue) % n) : SetPiece(id);
        }

        /// <summary>Set pieces drawn flat on the slot floor (sized to the footprint) rather than standing up.</summary>
        public static bool IsFlat(string id) => id == "portal_well";

        private static Sprite DrawSetPiece(string id)
        {
            switch (id)
            {
                case "portal_well": return PortalWell();
                case "giant_mushroom": case "burrow_mound": return GiantMushroom();
                case "crashed_saucer": case "time_clock": return Saucer();
                case "alien_tree": case "ruined_arch": return AlienTree();
                default: return Crystals("spire", new Color32(0x8E, 0x7C, 0xFF, 255), 5, 260, 300);
            }
        }

        private static Sprite PortalWell()
        {
            // drawn for a unit footprint diamond 400 x 276 px (the world scales it to the slot)
            int w = 420, h = 300;
            var p = new IsoPaint(w, h);
            float cx = w / 2f, cy = h / 2f;
            var ringC = (Color)new Color32(0x9A, 0xDC, 0xC8, 255);
            p.Blob(cx, cy, 190, 128, IsoPaint.Radial(Shade(ringC, 0.15f), Shade(ringC, -0.15f), cx - 40, cy + 40, 200), Ink, 6);
            // engraved spirals on the rim
            for (int k = 0; k < 6; k++)
            {
                float a0 = k / 6f * Mathf.PI * 2;
                var pts = new List<Vector2>();
                for (int j = 0; j <= 8; j++) { float a = a0 + j * 0.5f; float rr = 4 + j * 2.2f; pts.Add(V(cx + Mathf.Cos(a0) * 160 + Mathf.Cos(a) * rr, cy + Mathf.Sin(a0) * 108 + Mathf.Sin(a) * rr * 0.7f)); }
                p.Stroke(pts, 4, Shade(ringC, -0.28f), false);
            }
            // the stone lip and the deep swirl
            p.Blob(cx, cy - 6, 128, 82, Shade(ringC, -0.12f), Ink, 5);
            p.Ellipse(cx, cy - 10, 112, 70, IsoPaint.Radial(new Color32(0x6A, 0x3C, 0xC8, 255), new Color32(0x0E, 0x10, 0x30, 255), cx + 10, cy - 18, 110));
            for (int i = 0; i < 4; i++)
            {
                var pts = new List<Vector2>();
                for (int j = 0; j <= 14; j++) { float a = (i * 90 + j * 26) * Mathf.Deg2Rad; float rr = 8 + j * 6.5f; pts.Add(V(cx + Mathf.Cos(a) * rr, cy - 12 + Mathf.Sin(a) * rr * 0.6f)); }
                p.Stroke(pts, 5, new Color(0.75f, 0.55f, 1f, 0.45f), false);
            }
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2;
                p.Line(V(cx + Mathf.Cos(a) * 118, cy - 6 + Mathf.Sin(a) * 76), V(cx + Mathf.Cos(a) * 136, cy - 6 + Mathf.Sin(a) * 88), 4, Ink);
            }
            // moss
            p.Blob(cx - 150, cy + 30, 24, 12, new Color32(0x5E, 0xB0, 0x5A, 255), new Color32(0x2E, 0x6A, 0x30, 255), 3);
            p.Blob(cx + 120, cy - 70, 28, 10, new Color32(0x5E, 0xB0, 0x5A, 255), new Color32(0x2E, 0x6A, 0x30, 255), 3);
            return p.ToSprite("rs_set_portal_well", V(cx, cy));
        }

        private static Sprite GiantMushroom()
        {
            int w = 280, h = 320;
            var p = new IsoPaint(w, h);
            float cx = w / 2f;
            var stem = (Color)new Color32(0xF2, 0xE6, 0xC8, 255);
            p.Shape(new[] { V(cx - 34, 20), V(cx + 34, 20), V(cx + 26, 170), V(cx - 26, 170) }, VGrad(Shade(stem, -0.2f), stem, 20, 170), Ink, 6);
            var cap = (Color)new Color32(0xC8, 0x5C, 0xE0, 255);
            var pts = new List<Vector2>();
            for (int i = 0; i <= 20; i++) { float a = Mathf.PI * i / 20; pts.Add(V(cx - Mathf.Cos(a) * 128, 150 + Mathf.Sin(a) * 150)); }
            p.Shape(pts, IsoPaint.Radial(Shade(cap, 0.3f), Shade(cap, -0.2f), cx - 40, 250, 170), Ink, 6);
            p.Blob(cx, 152, 124, 16, Shade(cap, -0.35f), Ink, 5);
            var dot = (Color)new Color32(0xFF, 0xF6, 0xC8, 255);
            foreach (var d in new[] { V(cx - 60, 220), V(cx + 20, 262), V(cx + 70, 200), V(cx - 10, 190), V(cx - 88, 176) }) p.Blob(d.x, d.y, 16, 12, dot, Ink, 3);
            return p.ToSprite("rs_set_giant_mushroom", V(cx, 22));
        }

        private static Sprite Saucer()
        {
            int w = 320, h = 260;
            var p = new IsoPaint(w, h);
            float cx = w / 2f;
            // a crater of dirt and the tilted saucer stuck in it
            p.Blob(cx, 34, 128, 30, new Color32(0x8A, 0x70, 0x5A, 255), Ink, 5);
            var metal = (Color)new Color32(0xB8, 0xC0, 0xD8, 255);
            var disc = new List<Vector2>();
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2;
                var q = V(Mathf.Cos(a) * 130, Mathf.Sin(a) * 40);
                float rot = 18 * Mathf.Deg2Rad;
                disc.Add(V(cx + q.x * Mathf.Cos(rot) - q.y * Mathf.Sin(rot), 110 + q.x * Mathf.Sin(rot) + q.y * Mathf.Cos(rot)));
            }
            p.Shape(disc, IsoPaint.Radial(Shade(metal, 0.3f), Shade(metal, -0.3f), cx - 30, 140, 150), Ink, 6);
            p.Blob(cx + 10, 146, 52, 42, IsoPaint.Radial(new Color32(0xC8, 0xFF, 0xFF, 255), new Color32(0x3C, 0xB8, 0xE0, 255), cx - 4, 166, 60), Ink, 5);
            for (int i = 0; i < 5; i++) p.Blob(cx - 90 + i * 45, 92 + i * 14, 7, 7, new Color32(0xFF, 0xD0, 0x48, 255), Ink, 2.5f);
            p.Line(V(cx + 120, 150), V(cx + 140, 210), 8, new Color(0.3f, 0.3f, 0.35f, 0.35f));
            return p.ToSprite("rs_set_saucer", V(cx, 30));
        }

        private static Sprite AlienTree()
        {
            int w = 280, h = 340;
            var p = new IsoPaint(w, h);
            float cx = w / 2f;
            var bark = (Color)new Color32(0x7A, 0x52, 0x7E, 255);
            p.Shape(new[] { V(cx - 34, 18), V(cx + 30, 18), V(cx + 14, 120), V(cx + 40, 200), V(cx + 10, 210), V(cx - 6, 150), V(cx - 40, 200), V(cx - 56, 190), V(cx - 18, 120) }, VGrad(Shade(bark, -0.3f), bark, 18, 210), Ink, 6);
            var leaf = (Color)new Color32(0x4C, 0xD6, 0xB0, 255);
            foreach (var b in new[] { new Vector4(cx - 60, 220, 62, 50), new Vector4(cx + 56, 228, 60, 48), new Vector4(cx, 270, 76, 56), new Vector4(cx - 4, 214, 50, 36) })
                p.Blob(b.x, b.y, b.z, b.w, IsoPaint.Radial(Shade(leaf, 0.3f), Shade(leaf, -0.25f), b.x - 20, b.y + 20, b.z * 1.4f), Ink, 5);
            foreach (var d in new[] { V(cx - 70, 236), V(cx + 40, 250), V(cx + 4, 300), V(cx + 76, 214) }) p.Blob(d.x, d.y, 8, 8, new Color32(0xFF, 0x8C, 0xD8, 255), Ink, 3);
            return p.ToSprite("rs_set_alien_tree", V(cx, 22));
        }

        /// <summary>A scatter prop beside the path (pivot at its foot).</summary>
        public static Sprite Scatter(string id) =>
            ArtCatalog.SliceScatter(id) ?? Get("scatter:" + id, () => DrawScatter(id));

        private static Sprite DrawScatter(string id)
        {
            switch (id)
            {
                case "rock_pile": return Rocks(3);
                case "barrel": return Urn();
                case "bush": return Bush();
                case "stump": return BrokenPillar();
                case "flowers": return Bulbs();
                case "mushrooms": return Mushrooms();
                case "pebble_glow": return GlowPebbles();
                case "fern": return Weed();
                case "antenna": return Pylon();
                case "bones": return Ribs();
                case "crystal_small": return Crystals("crystal_small", new Color32(0x9E, 0x8C, 0xFF, 255), 3, 90, 90);
                default: return Rocks(1);
            }
        }

        /// <summary>How big a scatter prop stands (world units tall): ground cover, mid props, or a large background piece.</summary>
        public static float ScatterHeight(string id)
        {
            switch (id)
            {
                case "stump": return 2.1f;
                case "antenna": return 2.3f;
                case "fern": return 0.85f;
                case "bush": case "barrel": case "bones": case "rock_pile": return 0.75f;
                case "crystal_small": return 0.7f;
                case "flowers": case "mushrooms": return 0.5f;
                case "pebble_glow": return 0.28f;
                default: return 0.45f;
            }
        }

        /// <summary>Large background pieces (placed further out from the path, at the edges of the view).</summary>
        public static bool IsLargeScatter(string id) => id == "stump" || id == "antenna";

        /// <summary>A broken alien pillar (ruin): a fluted column snapped at the top, moss, a fallen chunk at its foot.</summary>
        private static Sprite BrokenPillar()
        {
            int w = 170, h = 300;
            var p = new IsoPaint(w, h);
            float cx = 76;
            var stone = (Color)new Color32(0x86, 0xB0, 0xB2, 255);
            // base plinth
            p.Shape(new[] { V(cx - 58, 6), V(cx + 58, 6), V(cx + 52, 40), V(cx - 52, 40) }, VGrad(Shade(stone, -0.3f), Shade(stone, -0.05f), 6, 40), Ink, 5);
            // the shaft with a jagged break at the top
            var shaft = new[] { V(cx - 40, 38), V(cx + 40, 38), V(cx + 38, 236), V(cx + 22, 262), V(cx + 8, 244), V(cx - 6, 276), V(cx - 24, 250), V(cx - 38, 258) };
            p.Shape(shaft, (x, y) => Color.Lerp(Shade(stone, 0.18f), Shade(stone, -0.28f), Mathf.Clamp01((x - (cx - 40)) / 80f)), Ink, 5);
            for (int i = -1; i <= 1; i++) p.Line(V(cx + i * 20, 46), V(cx + i * 20 - 1, 236 - Mathf.Abs(i) * 8), 4, Shade(stone, -0.34f));
            // a carved glyph ring and moss
            p.Line(V(cx - 39, 160), V(cx + 38, 160), 5, Shade(stone, -0.4f));
            p.Line(V(cx - 39, 176), V(cx + 38, 176), 3, Shade(stone, -0.3f));
            var moss = (Color)new Color32(0x5C, 0xA8, 0x5A, 255);
            p.Blob(cx - 30, 110, 14, 30, moss, new Color32(0x2E, 0x62, 0x34, 255), 3);
            p.Blob(cx + 24, 230, 16, 10, moss, new Color32(0x2E, 0x62, 0x34, 255), 3);
            p.Blob(cx - 50, 40, 20, 10, moss, new Color32(0x2E, 0x62, 0x34, 255), 3);
            // a fallen chunk
            p.Shape(new[] { V(cx + 62, 6), V(cx + 92, 6), V(cx + 98, 30), V(cx + 70, 38) }, Shade(stone, -0.12f), Ink, 4);
            return p.ToSprite("rs_scatter_pillar", V(cx, 6));
        }

        /// <summary>An alien antenna pylon: a leaning mast with a cracked dish and a glowing tip.</summary>
        private static Sprite Pylon()
        {
            int w = 180, h = 300;
            var p = new IsoPaint(w, h);
            float cx = 80;
            var metal = (Color)new Color32(0x9A, 0xA6, 0xBE, 255);
            p.Shape(new[] { V(cx - 46, 4), V(cx + 46, 4), V(cx + 30, 30), V(cx - 30, 30) }, VGrad(Shade(metal, -0.4f), Shade(metal, -0.1f), 4, 30), Ink, 5);
            p.Line(V(cx, 28), V(cx + 16, 232), 18, Ink);
            p.Line(V(cx, 28), V(cx + 16, 232), 10, Shade(metal, -0.05f));
            p.Line(V(cx - 3, 30), V(cx + 12, 228), 3, new Color(1, 1, 1, 0.4f));
            // the dish
            var dish = new List<Vector2>();
            for (int i = 0; i <= 14; i++) { float a = Mathf.Lerp(200, 340, i / 14f) * Mathf.Deg2Rad; dish.Add(V(cx + 30 + Mathf.Cos(a) * 58, 200 + Mathf.Sin(a) * 40)); }
            dish.Add(V(cx + 30 + 54, 220)); dish.Add(V(cx + 30 - 54, 220));
            p.Shape(dish, VGrad(Shade(metal, -0.3f), Shade(metal, 0.2f), 160, 220), Ink, 5);
            p.Line(V(cx + 30, 196), V(cx + 30, 250), 6, Ink);
            p.Blob(cx + 30, 258, 13, 13, IsoPaint.Radial(new Color32(0xE8, 0xFF, 0xF6, 255), new Color32(0x3C, 0xE0, 0xB0, 255), cx + 26, 262, 16), Ink, 4);
            // cables / weeds at its foot
            p.Line(V(cx - 30, 10), V(cx - 50, 60), 7, new Color32(0x1E, 0x4A, 0x2E, 255));
            p.Line(V(cx - 30, 10), V(cx - 50, 60), 3.5f, new Color32(0x5C, 0xC8, 0x8E, 255));
            return p.ToSprite("rs_scatter_pylon", V(cx, 4));
        }

        /// <summary>A tipped clay urn spilling glowing beads.</summary>
        private static Sprite Urn()
        {
            var p = new IsoPaint(170, 110);
            var clay = (Color)new Color32(0xB8, 0x6A, 0x52, 255);
            var body = new List<Vector2>();
            for (int i = 0; i < 24; i++) { float a = i / 24f * Mathf.PI * 2; body.Add(V(78 + Mathf.Cos(a) * 54, 44 + Mathf.Sin(a) * 36)); }
            p.Shape(body, IsoPaint.Radial(Shade(clay, 0.3f), Shade(clay, -0.3f), 60, 64, 70), Ink, 5);
            p.Shape(new[] { V(124, 28), V(156, 24), V(160, 70), V(126, 66) }, Shade(clay, -0.15f), Ink, 5);
            p.Ellipse(150, 47, 8, 16, new Color32(0x3A, 0x22, 0x26, 255));
            p.Line(V(40, 30), V(110, 22), 4, Shade(clay, -0.35f));
            foreach (var d in new[] { V(160, 10), V(146, 8), V(166, 20) }) p.Blob(d.x, d.y + 4, 6, 5, new Color32(0x9C, 0xF0, 0xE0, 255), Ink, 2.5f);
            return p.ToSprite("rs_scatter_urn", V(78, 8));
        }

        /// <summary>Half-buried rib bones of some huge creature.</summary>
        private static Sprite Ribs()
        {
            var p = new IsoPaint(170, 120);
            var bone = (Color)new Color32(0xEE, 0xE2, 0xC4, 255);
            for (int i = 0; i < 4; i++)
            {
                var pts = new List<Vector2>();
                float x0 = 26 + i * 36;
                for (int j = 0; j <= 8; j++) { float t = j / 8f; pts.Add(V(x0 + t * 20, 8 + Mathf.Sin(t * Mathf.PI) * (80 - i * 12))); }
                p.Stroke(pts, 15, Ink, false);
                p.Stroke(pts, 8, Shade(bone, -i * 0.06f), false);
            }
            p.Blob(30, 10, 22, 8, Shade(bone, -0.2f), Ink, 3);
            return p.ToSprite("rs_scatter_ribs", V(85, 6));
        }

        /// <summary>A cluster of small glowing mushrooms.</summary>
        private static Sprite Mushrooms()
        {
            var p = new IsoPaint(120, 90);
            var caps = new Color32[] { new Color32(0xE8, 0x6A, 0xC8, 255), new Color32(0xB0, 0x7C, 0xF0, 255), new Color32(0xE8, 0x6A, 0xC8, 255) };
            var spots = new[] { new Vector3(36, 40, 22), new Vector3(74, 58, 28), new Vector3(96, 30, 16) };
            for (int i = 0; i < 3; i++)
            {
                var sp = spots[i];
                p.Shape(RoundRect(sp.x - sp.z * 0.3f, 6, sp.z * 0.6f, sp.y - 8, 4), new Color32(0xF2, 0xE8, 0xD0, 255), Ink, 3);
                var cap = new List<Vector2>();
                for (int j = 0; j <= 12; j++) { float a = Mathf.PI * j / 12; cap.Add(V(sp.x - Mathf.Cos(a) * sp.z, sp.y + Mathf.Sin(a) * sp.z * 0.8f)); }
                p.Shape(cap, IsoPaint.Radial(Shade(caps[i], 0.35f), Shade(caps[i], -0.2f), sp.x - 6, sp.y + 10, sp.z * 1.4f), Ink, 3.5f);
                p.Circle(sp.x - sp.z * 0.3f, sp.y + sp.z * 0.4f, 3, new Color(1, 1, 1, 0.8f));
            }
            return p.ToSprite("rs_scatter_mushrooms", V(60, 6));
        }

        /// <summary>A few half-buried pebbles, one glowing.</summary>
        private static Sprite GlowPebbles()
        {
            var p = new IsoPaint(120, 50);
            var rock = (Color)new Color32(0x9A, 0xA8, 0xBC, 255);
            p.Blob(34, 18, 20, 11, IsoPaint.Radial(Shade(rock, 0.3f), Shade(rock, -0.2f), 28, 24, 24), Ink, 3);
            p.Blob(66, 14, 14, 8, IsoPaint.Radial(Shade(rock, 0.3f), Shade(rock, -0.2f), 62, 18, 18), Ink, 3);
            p.Blob(92, 20, 12, 9, IsoPaint.Radial(new Color32(0xE0, 0xFF, 0xF4, 255), new Color32(0x3C, 0xD8, 0xB0, 255), 90, 24, 14), Ink, 3);
            return p.ToSprite("rs_scatter_pebbles", V(60, 6));
        }

        private static Sprite Rocks(int n)
        {
            var p = new IsoPaint(150, 100);
            var rock = (Color)new Color32(0x86, 0x9A, 0xB4, 255);
            var spots = n == 1 ? new[] { new Vector4(75, 40, 42, 30) } : new[] { new Vector4(56, 38, 36, 26), new Vector4(100, 32, 28, 20), new Vector4(80, 62, 26, 22) };
            foreach (var s in spots)
            {
                p.Blob(s.x, s.y, s.z, s.w, IsoPaint.Radial(Shade(rock, 0.3f), Shade(rock, -0.25f), s.x - s.z * 0.4f, s.y + s.w * 0.5f, s.z * 1.6f), Ink, 4);
                p.Line(V(s.x + s.z * 0.2f, s.y - s.w * 0.4f), V(s.x + s.z * 0.5f, s.y), 3, Shade(rock, -0.3f));
            }
            return p.ToSprite("rs_scatter_rocks" + n, V(75, 14));
        }

        private static Sprite Bush()
        {
            var p = new IsoPaint(150, 120);
            var leaf = (Color)new Color32(0x56, 0xB8, 0x6A, 255);
            foreach (var b in new[] { new Vector4(48, 46, 34, 30), new Vector4(100, 44, 32, 28), new Vector4(74, 70, 38, 34) })
                p.Blob(b.x, b.y, b.z, b.w, IsoPaint.Radial(Shade(leaf, 0.3f), Shade(leaf, -0.25f), b.x - 10, b.y + 14, b.z * 1.5f), new Color32(0x1E, 0x4A, 0x2E, 255), 4);
            foreach (var d in new[] { V(60, 80), V(96, 56), V(78, 46) }) p.Circle(d.x, d.y, 5, new Color32(0xFF, 0x9C, 0xD8, 255));
            return p.ToSprite("rs_scatter_bush", V(75, 14));
        }

        private static Sprite Bulbs()
        {
            var p = new IsoPaint(120, 110);
            var stem = (Color)new Color32(0x4E, 0xA8, 0x5E, 255);
            var cols = new Color32[] { new Color32(0x6C, 0xE8, 0xFF, 255), new Color32(0xFF, 0x8C, 0xD0, 255), new Color32(0xFF, 0xE0, 0x6A, 255) };
            for (int i = 0; i < 3; i++)
            {
                float x = 30 + i * 30, top = 50 + (i == 1 ? 30 : 10);
                p.Line(V(x, 12), V(x + (i - 1) * 6, top), 6, new Color32(0x1E, 0x4A, 0x2E, 255));
                p.Line(V(x, 12), V(x + (i - 1) * 6, top), 3, stem);
                p.Blob(x + (i - 1) * 6, top + 8, 11, 12, cols[i], Ink, 3.5f);
            }
            return p.ToSprite("rs_scatter_bulbs", V(60, 12));
        }

        private static Sprite Weed()
        {
            var p = new IsoPaint(110, 140);
            var c = (Color)new Color32(0x5C, 0xC8, 0x8E, 255);
            for (int i = 0; i < 4; i++)
            {
                var pts = new List<Vector2>();
                float x0 = 34 + i * 14, sway = (i % 2 == 0 ? 1 : -1) * 14;
                for (int j = 0; j <= 8; j++) { float t = j / 8f; pts.Add(V(x0 + Mathf.Sin(t * 3.2f) * sway, 12 + t * (90 + i * 8))); }
                p.Stroke(pts, 11, new Color32(0x1E, 0x4A, 0x2E, 255), false);
                p.Stroke(pts, 6, c, false);
            }
            return p.ToSprite("rs_scatter_weed", V(55, 12));
        }

        // ---- ground, surround, shadows -----------------------------------------------------------------------------------

        /// <summary>Seamless 0..1 noise on a torus of <paramref name="n"/> px (tileable textures).</summary>
        private static float TorusNoise(float x, float y, int n, float scale, float ox)
        {
            float a = x / n * Mathf.PI * 2, b = y / n * Mathf.PI * 2;
            return Mathf.PerlinNoise(ox + Mathf.Cos(a) * scale, Mathf.Sin(a) * scale + Mathf.Cos(b) * scale * 0.7f + ox) * 0.5f
                   + Mathf.PerlinNoise(ox * 2 + Mathf.Sin(b) * scale, Mathf.Cos(b) * scale + Mathf.Sin(a) * scale * 0.7f) * 0.5f;
        }

        /// <summary>
        /// The tileable meadow ground of the island the path sits on (slice-texture "ground_meadow", else drawn): layered
        /// tonal patches, grass tufts, pebbles and little shells.
        /// </summary>
        public static Sprite Ground() => ArtCatalog.SliceTexture("ground_meadow") ?? Get("ground", () =>
        {
            const int n = 512;
            var p = new IsoPaint(n, n);
            var mid = (Color)new Color32(0x70, 0xBC, 0xA2, 255);
            var dark = (Color)new Color32(0x54, 0x9E, 0x8C, 255);
            var light = (Color)new Color32(0x8E, 0xCA, 0x9E, 255);
            p.Poly(new[] { V(0, 0), V(n, 0), V(n, n), V(0, n) }, (x, y) =>
            {
                float big = TorusNoise(x, y, n, 1.3f, 3.3f), small = TorusNoise(x, y, n, 4.2f, 9.1f);
                var c = Color.Lerp(dark, mid, Mathf.SmoothStep(0.36f, 0.5f, big));
                c = Color.Lerp(c, light, Mathf.SmoothStep(0.56f, 0.68f, big) * 0.8f);
                return Shade(c, (small - 0.5f) * 0.12f);
            });
            var rnd = new System.Random(7);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var tuftDark = (Color)new Color32(0x3E, 0x84, 0x66, 255);
            var tuftLight = (Color)new Color32(0x9C, 0xD6, 0x8E, 255);
            for (int i = 0; i < 46; i++)
            {
                float x = R(24, n - 24), y = R(24, n - 24), k = R(0.7f, 1.3f);
                for (int j = -1; j <= 1; j++)
                {
                    var root = V(x + j * 4 * k, y);
                    var tip = root + V(j * 6 * k + R(-2, 2), R(10, 16) * k);
                    p.Line(root, tip, 3.2f * k, tuftDark);
                    p.Line(root + V(0, 2), tip, 1.4f * k, tuftLight);
                }
            }
            var pebble = (Color)new Color32(0x9A, 0xB0, 0xB4, 255);
            for (int i = 0; i < 26; i++)
            {
                float x = R(20, n - 20), y = R(20, n - 20), r = R(3, 8);
                p.Ellipse(x + 1, y - 2, r * 1.4f, r, new Color(0.1f, 0.25f, 0.2f, 0.35f));
                p.Ellipse(x, y, r * 1.4f, r, IsoPaint.Radial(Shade(pebble, 0.3f), Shade(pebble, -0.15f), x - r * 0.5f, y + r * 0.5f, r * 2));
            }
            var shell = (Color)new Color32(0xF2, 0xE4, 0xD0, 255);
            for (int i = 0; i < 6; i++)
            {
                float x = R(40, n - 40), y = R(40, n - 40), r = R(7, 11);
                var fan = new List<Vector2> { V(x, y - r * 0.6f) };
                for (int j = 0; j <= 8; j++) { float a = Mathf.Lerp(20, 160, j / 8f) * Mathf.Deg2Rad; fan.Add(V(x + Mathf.Cos(a) * r, y - r * 0.6f + Mathf.Sin(a) * r)); }
                p.Shape(fan, shell, Shade(shell, -0.45f), 1.5f);
                for (int j = 1; j < 4; j++) { float a = Mathf.Lerp(20, 160, j / 4f) * Mathf.Deg2Rad; p.Line(V(x, y - r * 0.6f), V(x + Mathf.Cos(a) * r * 0.9f, y - r * 0.6f + Mathf.Sin(a) * r * 0.9f), 1.2f, Shade(shell, -0.3f)); }
            }
            var s = p.ToSprite("rs_ground", V(n / 2f, n / 2f));
            s.texture.wrapMode = TextureWrapMode.Repeat;
            return s;
        });

        /// <summary>
        /// The dark surround outside the island (slice-texture "backdrop_deep", else drawn, tileable): deep blue-teal water
        /// with darker rock columns and faint caustics — low saturation so the lit diorama pops.
        /// </summary>
        public static Sprite Backdrop() => ArtCatalog.SliceTexture("backdrop_deep") ?? Get("backdrop", () =>
        {
            const int n = 256;
            var p = new IsoPaint(n, n);
            var deep = (Color)new Color32(0x17, 0x34, 0x4A, 255);
            var mid = (Color)new Color32(0x20, 0x46, 0x5C, 255);
            var col = (Color)new Color32(0x12, 0x2A, 0x3E, 255);
            p.Poly(new[] { V(0, 0), V(n, 0), V(n, n), V(0, n) }, (x, y) =>
            {
                float big = TorusNoise(x, y, n, 1.6f, 5.7f);
                // vertical rock columns: a tileable stripe pattern bent by noise
                float stripe = Mathf.Sin((x / n * 3 + (TorusNoise(x, y, n, 2.2f, 1.9f) - 0.5f) * 0.6f) * Mathf.PI * 2);
                var c = Color.Lerp(deep, mid, Mathf.SmoothStep(0.4f, 0.62f, big));
                c = Color.Lerp(c, col, Mathf.SmoothStep(0.55f, 0.85f, stripe) * 0.8f);
                return Shade(c, (TorusNoise(x, y, n, 6f, 2.4f) - 0.5f) * 0.1f);
            });
            var s = p.ToSprite("rs_backdrop", V(n / 2f, n / 2f));
            s.texture.wrapMode = TextureWrapMode.Repeat;
            return s;
        });

        /// <summary>A soft round contact shadow (pivot: centre) put under every prop, marker and the worm.</summary>
        public static Sprite SoftShadow() => Get("softshadow", () =>
        {
            var p = new IsoPaint(128, 64);
            p.Ellipse(64, 32, 63, 31, (x, y) =>
            {
                float dx = (x - 64) / 63f, dy = (y - 32) / 31f;
                float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                return new Color(0.02f, 0.08f, 0.1f, 0.6f * (1 - r * r));
            });
            return p.ToSprite("rs_softshadow", V(64, 32));
        });

        /// <summary>A screen vignette (pivot: centre): clear in the middle, darkening the corners and edges.</summary>
        public static Sprite Vignette() => Get("vignette", () =>
        {
            const int w = 128, h = 256;
            var p = new IsoPaint(w, h);
            p.Poly(new[] { V(0, 0), V(w, 0), V(w, h), V(0, h) }, (x, y) =>
            {
                float dx = (x - w / 2f) / (w / 2f), dy = (y - h / 2f) / (h / 2f);
                float r = Mathf.Sqrt(dx * dx * 0.9f + dy * dy * 0.75f);
                return new Color(0.03f, 0.06f, 0.1f, 0.5f * Mathf.SmoothStep(0.62f, 1.25f, r));
            });
            return p.ToSprite("rs_vignette", V(w / 2f, h / 2f));
        });

        /// <summary>Pixels per world unit of the island mask (the ground's outline).</summary>
        public const float IslandPpu = 16;

        /// <summary>
        /// The island's soft mask over a world rect (pivot: bottom-left): alpha 1 on the ground the path's diorama stands on,
        /// fading to 0 at its organic edge. <paramref name="alpha"/> is the island alpha at a ground point given in grid
        /// coordinates (gx, gy).
        /// </summary>
        public static Sprite IslandMask(Rect world, Func<float, float, float> alpha)
        {
            int w = Mathf.Clamp(Mathf.CeilToInt(world.width * IslandPpu), 8, 2048), h = Mathf.Clamp(Mathf.CeilToInt(world.height * IslandPpu), 8, 4096);
            var px = new Color32[w * h];
            float groundDrop = Iso.Slab + Iso.Wall;
            for (int j = 0; j < h; j++)
            {
                float wy = world.yMin + (j + 0.5f) / h * world.height;
                float b = (wy + groundDrop) / Iso.StepY;
                for (int i = 0; i < w; i++)
                {
                    float wx = world.xMin + (i + 0.5f) / w * world.width;
                    float a = wx / Iso.StepX;
                    byte v = (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(alpha((a + b) / 2, (b - a) / 2)));
                    px[j * w + i] = new Color32(255, 255, 255, v);
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "rs_island", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            var s = Sprite.Create(tex, new Rect(0, 0, w, h), Vector2.zero, w / world.width, 0, SpriteMeshType.FullRect);
            s.name = "rs_island";
            return s;
        }
    }
}
