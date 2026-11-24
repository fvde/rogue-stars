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
    /// standing on tiles), set pieces, scatter props, the "!" marker, the ground and the HUD icons.
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
        public static readonly Color Gap = new Color32(0x26, 0x24, 0x3C, 255);
        public static readonly Color WallLight = new Color32(0xE6, 0xC4, 0x84, 255);
        public static readonly Color WallDark = new Color32(0xC0, 0x95, 0x55, 255);
        public static readonly Color WallBand = new Color32(0x8E, 0x64, 0x34, 255);
        public static readonly Color Bone = new Color32(0xF4, 0xE2, 0xA0, 255);
        public static readonly Color BoneShade = new Color32(0xD2, 0xB4, 0x6A, 255);

        /// <summary>Top colour of a tile face kind.</summary>
        public static Color FaceColor(TileFace f)
        {
            switch (f)
            {
                case TileFace.Boost: return new Color32(0xE8, 0x8E, 0x82, 255);
                case TileFace.Event: return new Color32(0x86, 0xD2, 0xC2, 255);
                case TileFace.Special: return new Color32(0x94, 0xC8, 0xEE, 255);
                case TileFace.Hazard: return new Color32(0xA8, 0x66, 0x52, 255);
                case TileFace.Curse: return new Color32(0x96, 0x78, 0xC4, 255);
                case TileFace.Boss: return new Color32(0xC8, 0x48, 0x54, 255);
                case TileFace.Floor: return new Color32(0x84, 0xC9, 0xB4, 255);
                default: return new Color32(0x63, 0x66, 0x8E, 255);
            }
        }

        /// <summary>The block face of a tile kind on the board.</summary>
        public static TileFace FaceOf(TileCategory c)
        {
            switch (c)
            {
                case TileCategory.StatBuff: return TileFace.Boost;
                case TileCategory.Event: case TileCategory.Pickup: return TileFace.Event;
                case TileCategory.Shop: case TileCategory.Campfire: case TileCategory.Treasure: case TileCategory.Minigame:
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
                case TileCategory.BossTile: case TileCategory.BossGate: return "boss";
                case TileCategory.Event: return "event";
                case TileCategory.Pickup: return "mystery";
                case TileCategory.Treasure: return "chest";
                case TileCategory.Campfire: return "rest";
                case TileCategory.Shop: return "shop";
                case TileCategory.Minigame: return "minigame";
                case TileCategory.Start: return "portal";
                case TileCategory.Loot: case TileCategory.Shard: return "reward";
                default: return null;
            }
        }

        /// <summary>Height a marker is drawn at (world units), matched to the worm (~1.3).</summary>
        public static float MarkerHeight(string id)
        {
            switch (id)
            {
                case "rest": return 1.4f;
                case "shop": return 1.35f;
                case "boss": return 1.35f;
                case "miniboss": return 1.15f;
                case "portal": return 1.35f;
                case "chest": return 0.92f;
                case "elite": return 0.95f;
                case "event": return 0.9f;
                case "mystery": return 0.9f;
                case "minigame": return 0.95f;
                case "reward": return 0.85f;
                default: return 0.8f;
            }
        }

        // ---- tile blocks ---------------------------------------------------------------------------------------------

        private const float SX = Iso.StepX * Iso.Ppu, SY = Iso.StepY * Iso.Ppu;
        private const float SlabPx = Iso.Slab * Iso.Ppu, WallPx = Iso.Wall * Iso.Ppu, RisePx = Iso.Rise * Iso.Ppu;
        private const float Inset = 5;

        /// <summary>
        /// The block under a tile (pivot = the centre of its top face): the platform's top in the gaps, the slab's two front
        /// faces in the kind's colour, and the platform wall down to the ground with its decorated band (taller per height).
        /// <paramref name="face"/> Floor = a platform cell without a slab (set-piece slots).
        /// </summary>
        public static Sprite Block(TileFace face, int height)
        {
            return Get("block:" + face + ":" + height, () =>
            {
                float wall = WallPx + height * RisePx;
                bool slab = face != TileFace.Floor;
                float slabPx = slab ? SlabPx : 0;
                int w = Mathf.CeilToInt(2 * SX + 8), h = Mathf.CeilToInt(2 * SY + slabPx + wall + 10);
                var p = new IsoPaint(w, h);
                float cx = w / 2f, cy = h - 5 - SY;
                // platform (full diamond) below the slab
                float py = cy - slabPx;
                var L = V(cx - SX, py); var B = V(cx, py - SY); var R = V(cx + SX, py); var T = V(cx, py + SY);
                var Ld = V(L.x, L.y - wall); var Bd = V(B.x, B.y - wall); var Rd = V(R.x, R.y - wall);
                WallFace(p, L, B, wall, WallLight, true);
                WallFace(p, B, R, wall, WallDark, false);
                p.Line(Ld, Bd, 3, Ink); p.Line(Bd, Rd, 3, Ink); p.Line(B, Bd, 2.5f, new Color(Ink.r, Ink.g, Ink.b, 0.6f));
                p.Line(L, Ld, 2.5f, Ink); p.Line(R, Rd, 2.5f, Ink);
                p.Poly(new[] { L, B, R, T }, slab ? Gap : FaceColor(TileFace.Floor));
                if (!slab)
                {
                    // the floor of a set-piece slot: mossy stone with faint engraved arcs
                    var fc = FaceColor(TileFace.Floor);
                    p.Poly(new[] { L, B, R, T }, Radial(Shade(fc, 0.12f), Shade(fc, -0.1f), cx, py, SX));
                    p.Stroke(new[] { L, B, R, T }, 2, Shade(fc, -0.35f));
                    p.Line(V(cx - SX * 0.45f, py - SY * 0.1f), V(cx + SX * 0.1f, py + SY * 0.35f), 2, Shade(fc, -0.22f));
                    return p.ToSprite("rs_floor_" + height, V(cx, cy));
                }
                // the slab's front faces (inset: the gap shows between tiles)
                var col = FaceColor(face);
                float k = 1 - Inset / SX;
                var l = V(cx - SX * k, cy); var b = V(cx, cy - SY * k); var r = V(cx + SX * k, cy);
                p.Poly(new[] { l, b, V(b.x, b.y - slabPx), V(l.x, l.y - slabPx) }, Shade(col, -0.28f));
                p.Poly(new[] { b, r, V(r.x, r.y - slabPx), V(b.x, b.y - slabPx) }, Shade(col, -0.45f));
                p.Stroke(new[] { l, V(l.x, l.y - slabPx), V(b.x, b.y - slabPx), V(r.x, r.y - slabPx), r }, 2.5f, Ink, false);
                return p.ToSprite("rs_block_" + face + "_" + height, V(cx, cy));
            });
        }

        /// <summary>One platform wall face from edge a→b straight down <paramref name="depth"/> px, with the trim band.</summary>
        private static void WallFace(IsoPaint p, Vector2 a, Vector2 b, float depth, Color baseCol, bool left)
        {
            var e = b - a;
            p.Poly(new[] { a, b, V(b.x, b.y - depth), V(a.x, a.y - depth) }, (x, y) =>
            {
                float u = (x - a.x) / e.x;               // 0..1 along the edge
                float v = a.y + e.y * u - y;             // px below the top edge
                float along = u * e.magnitude;
                float band0 = 14, band1 = 38;
                var c = baseCol;
                if (v < 5) c = Shade(baseCol, 0.25f);                  // lit lip
                else if (v > depth - 6) c = Shade(baseCol, -0.18f);    // foot
                else if (v >= band0 && v <= band1)
                {
                    // alien glyph band: a stepped hook every 24 px between two rails
                    float bx = along % 24, by = v - band0;
                    bool rail = by < 2.5f || by > band1 - band0 - 2.5f;
                    bool glyph = (bx > 3 && bx < 6 && by > 5 && by < 19) || (by > 5 && by < 8 && bx > 3 && bx < 17)
                                 || (bx > 14 && bx < 17 && by > 5 && by < 14) || (by > 11 && by < 14 && bx > 9 && bx < 17)
                                 || (bx > 9 && bx < 12 && by > 11 && by < 19);
                    c = rail || glyph ? WallBand : Shade(baseCol, -0.06f);
                }
                if (!left) c = Shade(c, -0.08f);
                return c;
            });
        }

        /// <summary>The top face of a tile (pivot = its centre): bevel, outline, a socket for the marker, the kind's engraving.</summary>
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
                        c = new Color(c.r * (0.55f + 0.45f * s.r * 1.4f), c.g * (0.55f + 0.45f * s.g * 1.4f), c.b * (0.55f + 0.45f * s.b * 1.4f), 1);
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
                        // two chevrons pointing along the path (curse: against it)
                        var st = Iso.Step(face == TileFace.Curse ? heading + 2 : heading);
                        var dir = new Vector2((st.x - st.y) * SX, (st.x + st.y) * SY).normalized;
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
            ArtCatalog.SliceMarker(id) ?? Get("marker:" + id, () => DrawMarker(id));

        private static Sprite DrawMarker(string id)
        {
            switch (id)
            {
                case "elite": return Skull("elite", Bone, true, false, 1f);
                case "miniboss": return Skull("miniboss", new Color32(0xF2, 0x9A, 0x6A, 255), true, true, 1.1f);
                case "boss": return Skull("boss", new Color32(0xE8, 0x55, 0x5E, 255), true, true, 1.25f);
                case "event": return EventScroll();
                case "mystery": return MysteryStone();
                case "chest": return Chest();
                case "rest": return Campfire();
                case "shop": return Cart();
                case "minigame": return Arcade();
                case "portal": return PortalRing();
                case "reward": return Crystals("reward", new Color32(0x72, 0xE6, 0xFF, 255), 3, 120, 120);
                default: return Skull("enemy", Bone, false, false, 1f);
            }
        }

        /// <summary>An alien skull (big cranium, glowing eyes, little antennae); horns for elites, a crown for bosses.</summary>
        private static Sprite Skull(string name, Color bone, bool horns, bool crown, float scale)
        {
            int w = (int)(150 * scale), h = (int)(150 * scale);
            var p = new IsoPaint(w, h);
            float s = scale, cx = w / 2f;
            var shade = Shade(bone, -0.25f);
            p.Ellipse(cx, 8 * s, 34 * s, 9 * s, new Color(0, 0, 0, 0.28f));
            if (horns)
            {
                var horn = crown ? new Color32(0x7B, 0x2B, 0x3A, 255) : new Color32(0xA9, 0x78, 0xE8, 255);
                foreach (int side in new[] { -1, 1 })
                {
                    var pts = new[] { V(cx + side * 22 * s, 82 * s), V(cx + side * 46 * s, 84 * s), V(cx + side * 62 * s, 108 * s), V(cx + side * 58 * s, 138 * s), V(cx + side * 50 * s, 112 * s), V(cx + side * 34 * s, 100 * s) };
                    p.Shape(pts, horn, Ink, 4 * s);
                }
            }
            // antennae (alien)
            foreach (int side in new[] { -1, 1 })
            {
                p.Line(V(cx + side * 10 * s, 96 * s), V(cx + side * 18 * s, 122 * s), 5 * s, Ink);
                p.Line(V(cx + side * 10 * s, 96 * s), V(cx + side * 18 * s, 122 * s), 2.5f * s, shade);
                p.Blob(cx + side * 18 * s, 124 * s, 6 * s, 6 * s, crown ? (Color)new Color32(0xFF, 0x6A, 0x5A, 255) : (Color)new Color32(0x9C, 0xF0, 0x8A, 255), Ink, 3 * s);
            }
            // jaw, then cranium over it
            var jaw = RoundRect(cx - 21 * s, 14 * s, 42 * s, 30 * s, 10 * s);
            p.Shape(jaw, VGrad(shade, bone, 14 * s, 44 * s), Ink, 4 * s);
            for (int i = -1; i <= 1; i++) p.Line(V(cx + i * 10 * s, 17 * s), V(cx + i * 10 * s, 29 * s), 2.5f * s, Ink);
            p.Blob(cx, 64 * s, 42 * s, 36 * s, IsoPaint.Radial(Shade(bone, 0.25f), shade, cx - 12 * s, 76 * s, 52 * s), Ink, 4.5f * s);
            // eyes: big dark sockets with glowing pupils
            foreach (int side in new[] { -1, 1 })
            {
                p.Ellipse(cx + side * 16 * s, 60 * s, 12 * s, 13 * s, Ink);
                p.Ellipse(cx + side * 15 * s, 59 * s, 5 * s, 5.5f * s, crown ? new Color32(0xFF, 0xE0, 0x5A, 255) : new Color32(0xFF, 0x4A, 0x3A, 255));
                p.Ellipse(cx + side * 13.5f * s, 61 * s, 2 * s, 2 * s, Color.white);
            }
            p.Shape(new[] { V(cx - 4 * s, 42 * s), V(cx + 4 * s, 42 * s), V(cx, 48 * s) }, Ink, Ink, 1);
            // a little cranium seam
            p.Line(V(cx - 6 * s, 92 * s), V(cx + 2 * s, 84 * s), 2.5f * s, shade);
            if (crown)
            {
                var gold = new Color32(0xFF, 0xC8, 0x3C, 255);
                var c = new[] { V(cx - 26 * s, 92 * s), V(cx - 30 * s, 118 * s), V(cx - 14 * s, 104 * s), V(cx, 126 * s), V(cx + 14 * s, 104 * s), V(cx + 30 * s, 118 * s), V(cx + 26 * s, 92 * s) };
                p.Shape(c, gold, Ink, 4 * s);
            }
            return p.ToSprite("rs_marker_" + name, V(cx, 8 * s));
        }

        private static Sprite EventScroll()
        {
            var p = new IsoPaint(150, 130);
            float cx = 75;
            p.Ellipse(cx, 10, 40, 9, new Color(0, 0, 0, 0.28f));
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
            p.Ellipse(cx, 10, 40, 9, new Color(0, 0, 0, 0.28f));
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
            p.Ellipse(cx, 10, 50, 10, new Color(0, 0, 0, 0.28f));
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
            p.Ellipse(cx, 30, 82, 30, new Color(0, 0, 0, 0.25f));
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
            p.Ellipse(cx, 14, 80, 13, new Color(0, 0, 0, 0.28f));
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
            p.Ellipse(cx, 10, 46, 10, new Color(0, 0, 0, 0.28f));
            var body = (Color)new Color32(0x3E, 0xC8, 0xB0, 255);
            p.Shape(RoundRect(24, 12, 92, 52, 14), VGrad(Shade(body, -0.3f), body, 12, 64), Ink, 5);
            p.Line(V(cx, 60), V(cx - 8, 104), 10, Ink); p.Line(V(cx, 60), V(cx - 8, 104), 5, new Color32(0xD8, 0xD8, 0xE8, 255));
            p.Blob(cx - 9, 110, 17, 17, new Color32(0xFF, 0x4E, 0x6A, 255), Ink, 5);
            p.Ellipse(cx - 14, 116, 5, 4, new Color(1, 1, 1, 0.8f));
            p.Blob(cx + 28, 40, 8, 8, new Color32(0xFF, 0xD0, 0x48, 255), Ink, 3);
            p.Blob(cx - 28, 40, 8, 8, new Color32(0x9C, 0x8C, 0xFF, 255), Ink, 3);
            return p.ToSprite("rs_marker_minigame", V(cx, 10));
        }

        private static Sprite PortalRing()
        {
            var p = new IsoPaint(170, 200);
            float cx = 85;
            p.Ellipse(cx, 14, 60, 12, new Color(0, 0, 0, 0.28f));
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
            p.Ellipse(cx, 10, w * 0.32f, 9, new Color(0, 0, 0, 0.28f));
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

        /// <summary>The "!" that floats over special tiles ahead.</summary>
        public static Sprite Exclaim() => Get("exclaim", () =>
        {
            var p = new IsoPaint(60, 120);
            var y = (Color)new Color32(0xFF, 0xD8, 0x2E, 255);
            p.Shape(RoundRect(17, 36, 26, 78, 9), VGrad(Shade(y, -0.1f), Shade(y, 0.2f), 36, 114), Ink, 6);
            p.Shape(RoundRect(17, 4, 26, 24, 9), y, Ink, 6);
            return p.ToSprite("rs_exclaim", V(30, 4));
        });

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
            p.Ellipse(cx, 22, 100, 20, new Color(0, 0, 0, 0.28f));
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
            p.Ellipse(cx, 30, 130, 26, new Color(0, 0, 0, 0.3f));
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
            p.Ellipse(cx, 22, 90, 18, new Color(0, 0, 0, 0.28f));
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
                case "rock_pile": case "barrel": return Rocks(3);
                case "bush": case "stump": return Bush();
                case "flowers": case "mushrooms": case "pebble_glow": return Bulbs();
                case "fern": case "antenna": return Weed();
                case "crystal_small": case "bones": return Crystals("crystal_small", new Color32(0x9E, 0x8C, 0xFF, 255), 3, 90, 90);
                default: return Rocks(1);
            }
        }

        private static Sprite Rocks(int n)
        {
            var p = new IsoPaint(150, 100);
            var rock = (Color)new Color32(0x86, 0x9A, 0xB4, 255);
            p.Ellipse(75, 14, 60, 12, new Color(0, 0, 0, 0.22f));
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
            p.Ellipse(75, 14, 56, 12, new Color(0, 0, 0, 0.22f));
            foreach (var b in new[] { new Vector4(48, 46, 34, 30), new Vector4(100, 44, 32, 28), new Vector4(74, 70, 38, 34) })
                p.Blob(b.x, b.y, b.z, b.w, IsoPaint.Radial(Shade(leaf, 0.3f), Shade(leaf, -0.25f), b.x - 10, b.y + 14, b.z * 1.5f), new Color32(0x1E, 0x4A, 0x2E, 255), 4);
            foreach (var d in new[] { V(60, 80), V(96, 56), V(78, 46) }) p.Circle(d.x, d.y, 5, new Color32(0xFF, 0x9C, 0xD8, 255));
            return p.ToSprite("rs_scatter_bush", V(75, 14));
        }

        private static Sprite Bulbs()
        {
            var p = new IsoPaint(120, 110);
            p.Ellipse(60, 12, 44, 10, new Color(0, 0, 0, 0.2f));
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
            p.Ellipse(55, 12, 36, 9, new Color(0, 0, 0, 0.2f));
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

        // ---- ground ----------------------------------------------------------------------------------------------------

        /// <summary>The tileable meadow ground under the board (slice-texture "ground_meadow", else drawn).</summary>
        public static Sprite Ground() => ArtCatalog.SliceTexture("ground_meadow") ?? Get("ground", () =>
        {
            const int n = 512;
            var p = new IsoPaint(n, n);
            var light = (Color)new Color32(0x6A, 0xBE, 0xA2, 255);
            var dark = (Color)new Color32(0x58, 0xA8, 0x90, 255);
            // seamless soft patches: noise sampled on a torus
            float Noise(float x, float y, float scale, float ox)
            {
                float a = x / n * Mathf.PI * 2, b = y / n * Mathf.PI * 2;
                return Mathf.PerlinNoise(ox + Mathf.Cos(a) * scale, Mathf.Sin(a) * scale + Mathf.Cos(b) * scale * 0.7f + ox) * 0.5f
                       + Mathf.PerlinNoise(ox * 2 + Mathf.Sin(b) * scale, Mathf.Cos(b) * scale + Mathf.Sin(a) * scale * 0.7f) * 0.5f;
            }
            p.Poly(new[] { V(0, 0), V(n, 0), V(n, n), V(0, n) }, (x, y) =>
            {
                float v = Noise(x, y, 1.4f, 3.3f);
                return Color.Lerp(dark, light, Mathf.SmoothStep(0.42f, 0.58f, v));
            });
            var rnd = new System.Random(7);
            for (int i = 0; i < 9; i++)
            {
                float x = (float)rnd.NextDouble() * (n - 60) + 30, y = (float)rnd.NextDouble() * (n - 60) + 30;
                float r = 4 + (float)rnd.NextDouble() * 6;
                p.Blob(x, y, r * 1.5f, r, new Color32(0x8E, 0xCF, 0xB8, 255), Shade(dark, -0.2f), 2.5f);
            }
            for (int i = 0; i < 7; i++)
            {
                float x = (float)rnd.NextDouble() * (n - 60) + 30, y = (float)rnd.NextDouble() * (n - 60) + 30;
                p.Line(V(x, y), V(x + 3, y + 10), 2.5f, Shade(dark, -0.15f));
                p.Line(V(x + 6, y), V(x + 11, y + 8), 2.5f, Shade(dark, -0.15f));
            }
            var s = p.ToSprite("rs_ground", V(n / 2f, n / 2f));
            s.texture.wrapMode = TextureWrapMode.Repeat;
            return s;
        });
    }
}
