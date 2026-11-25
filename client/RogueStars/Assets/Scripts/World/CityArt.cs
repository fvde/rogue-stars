using System;
using System.Collections.Generic;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using UnityEngine;
using static RogueStars.Client.World.IsoPaint;

namespace RogueStars.Client.World
{
    /// <summary>
    /// City diorama art: approved set pieces, markers and props stand in for building sprites (City design mockups; real building
    /// art is a content task), the fire and the saucer on the plaza, the features; everything else is code-drawn here (tile
    /// diamonds for the ghost and previews, scaffolds, lamps, glows, the drill bot, the eggshell hat, social-event bubbles, FX).
    /// </summary>
    public static class CityArt
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static void ClearCache() => Cache.Clear();

        private static Sprite Get(string key, Func<Sprite> make)
        {
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            s = make();
            Cache[key] = s;
            return s;
        }

        // ---- stand-in building art ---------------------------------------------------------------------------------------

        /// <summary>The stand-in art per blueprint: "set:" = Slice set piece, "prop:" = run prop, "marker:" = Slice marker.</summary>
        public static readonly Dictionary<string, string> BuildingArtKey = new Dictionary<string, string>
        {
            { "bp_biomass_farm", "set:alien_tree" },
            { "bp_grain_farm", "set:giant_mushroom_3" },
            { "bp_ore_mine", "set:burrow_mound" },
            { "bp_fuel_refinery", "prop:event_wreck" },
            { "bp_gear_works", "prop:abandoned_cart" },
            { "bp_training_grounds", "set:ruined_arch_2" },
            { "bp_plate_foundry", "prop:chest_common" },
            { "bp_diamond_mine", "set:crystal_spire_2" },
            { "bp_token_relay", "marker:event" },
            { "bp_signal_beacon", "set:portal_well" },
            { "bp_part_fabricator", "set:crashed_saucer_2" },
            { ContentDb.LaunchPodId, "set:burrow_mound_2" },
            { "bp_barracks_dome", "set:giant_mushroom_2" },
            { "bp_command_spire", "set:time_clock_2" },
            { "bp_breeding_pen", "set:burrow_mound_3" },
            { "bp_gene_lab", "set:giant_mushroom" },
            { ContentDb.LaboratoryId, "prop:event_shrine" },
            { "bp_observatory", "set:time_clock" },
            { ContentDb.ShardReactorId, "prop:chest_signature" },
            { "bp_stamina_well", "marker:portal" },
            { "bp_regen_spire", "marker:minigame" },
            { "bp_supply_depot", "marker:shop" },
            { "bp_scout_post", "marker:rest" },
            { "bp_war_academy", "marker:chest" },
            { "bp_stasis_beacon", "marker:mystery" },
        };

        /// <summary>Loads an art key ("set:alien_tree"); null if the file is missing.</summary>
        public static Sprite LoadKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int colon = key.IndexOf(':');
            string kind = colon > 0 ? key.Substring(0, colon) : "set", id = colon > 0 ? key.Substring(colon + 1) : key;
            switch (kind)
            {
                case "prop": return ArtCatalog.RunProp(id);
                case "marker": return ArtCatalog.SliceMarker(id);
                default: return ArtCatalog.SliceSetPiece(id);
            }
        }

        /// <summary>The building's sprite: its stand-in art, else a drawn hut in its rarity colour.</summary>
        public static Sprite Building(BlueprintDef def)
        {
            if (def == null) return Hut(Color.grey);
            if (BuildingArtKey.TryGetValue(def.Id, out var key))
            {
                var s = LoadKey(key);
                if (s != null) return s;
            }
            return Hut(Theme.RarityColor(def.Rarity));
        }

        /// <summary>Standing height (world units) of a building by footprint size.</summary>
        public static float BuildingHeight(int w, int h)
        {
            int a = w * h;
            return a <= 1 ? 1.35f : a <= 2 ? 1.75f : a <= 4 ? 2.3f : a <= 6 ? 2.6f : 3.1f;
        }

        public static Sprite Fire() => ArtCatalog.RunProp("campfire_big") ?? ScreenArt.Prop("campfire_big");
        public static Sprite Saucer() => ArtCatalog.SliceSetPiece("crashed_saucer") ?? IsoArt.SetPiece("crashed_saucer");

        public static Sprite Feature(string kind)
        {
            switch (kind)
            {
                case "crystal": return ArtCatalog.SliceSetPiece("crystal_spire") ?? IsoArt.SetPiece("crystal_spire");
                case "ruin": return ArtCatalog.SliceSetPiece("ruined_arch") ?? IsoArt.SetPiece("ruined_arch");
                default: return null; // springs are painted into the land
            }
        }

        public static string FeatureName(string kind) => kind == "crystal" ? "Crystal Outcrop" : kind == "spring" ? "Spring" : kind == "ruin" ? "Old Ruin" : kind;

        // ---- code-drawn ----------------------------------------------------------------------------------------------

        /// <summary>A plain white square (the night tint over the whole view).</summary>
        public static Sprite Square() => Get("square", () =>
        {
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false) { name = "rs_city_square", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[64];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8);
        });

        /// <summary>A white tile diamond (one cell's top face) for ghosts, previews, flashes and patch frames.</summary>
        public static Sprite Tile() => Get("tile", () =>
        {
            int w = 168, h = 116;
            var p = new IsoPaint(w, h);
            p.Poly(new List<Vector2> { V(w / 2f, h - 1), V(w - 1, h / 2f), V(w / 2f, 1), V(1, h / 2f) }, Color.white);
            return p.ToSprite("rs_city_tile", V(w / 2f, h / 2f));
        });

        /// <summary>A diamond ring (the dig frame, the selection).</summary>
        public static Sprite TileRing() => Get("tilering", () =>
        {
            int w = 168, h = 116;
            var p = new IsoPaint(w, h);
            p.Stroke(new List<Vector2> { V(w / 2f, h - 4), V(w - 5, h / 2f), V(w / 2f, 4), V(5, h / 2f) }, 7, Color.white);
            return p.ToSprite("rs_city_tilering", V(w / 2f, h / 2f));
        });

        /// <summary>Scaffold over a construction site (size 1 = 1×1, 2 = 2×1 / 2×2, 3 = 3×2 / 3×3), pivot at the footprint centre.</summary>
        public static Sprite Scaffold(int size) => Get("scaffold" + size, () =>
        {
            int w = 120 + 70 * size, h = 110 + 60 * size;
            var p = new IsoPaint(w, h);
            var wood = new Color32(0xC8, 0x92, 0x52, 255);
            var dark = new Color32(0x6E, 0x48, 0x22, 255);
            float baseY = 24;
            int poles = 2 + size;
            float x0 = 16, x1 = w - 16;
            for (int i = 0; i < poles; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / (float)(poles - 1));
                float off = (i % 2 == 0) ? 0 : 10;
                p.Line(V(x, baseY + off), V(x, h - 12 - off), 9, dark);
                p.Line(V(x, baseY + off), V(x, h - 12 - off), 5, wood);
            }
            for (int k = 1; k <= 3; k++)
            {
                float y = baseY + (h - baseY - 20) * k / 3.4f;
                p.Line(V(x0 - 6, y), V(x1 + 6, y + 6), 12, dark);
                p.Line(V(x0 - 6, y), V(x1 + 6, y + 6), 8, wood);
            }
            p.Line(V(x0, baseY + 8), V(x1, h - 40), 6, dark);
            p.Line(V(x1, baseY + 8), V(x0, h - 40), 6, dark);
            // a hazard band and a little flag
            p.Shape(new List<Vector2> { V(w / 2f - 4, h - 12), V(w / 2f + 34, h - 22), V(w / 2f - 4, h - 34) }, new Color32(0xFF, 0x8A, 0x1F, 255), dark, 3);
            p.Line(V(w / 2f - 4, h - 40), V(w / 2f - 4, h - 8), 4, dark);
            return p.ToSprite("rs_city_scaffold" + size, V(w / 2f, baseY));
        });

        /// <summary>A street lamp (lit: a warm head), pivot at its foot.</summary>
        public static Sprite Lamp(bool lit) => Get(lit ? "lamp_on" : "lamp_off", () =>
        {
            int w = 40, h = 96;
            var p = new IsoPaint(w, h);
            var ink = new Color32(0x2B, 0x22, 0x33, 255);
            p.Ellipse(w / 2f, 6, 12, 5, new Color(0, 0, 0, 0.35f));
            p.Line(V(w / 2f, 6), V(w / 2f, h - 26), 6, ink);
            p.Blob(w / 2f, h - 16, 12, 12, lit ? (Color)new Color32(0xFF, 0xE2, 0x8A, 255) : (Color)new Color32(0x8D, 0x95, 0xA8, 255), ink, 4);
            return p.ToSprite(lit ? "rs_city_lamp_on" : "rs_city_lamp_off", V(w / 2f, 6));
        });

        /// <summary>A soft warm light (additive-looking glow over the night tint), pivot centre.</summary>
        public static Sprite Glow() => Get("glow", () =>
        {
            int s = 96;
            var p = new IsoPaint(s, s);
            p.Ellipse(s / 2f, s / 2f, s / 2f - 1, s / 2f - 1, (x, y) =>
            {
                float d = Mathf.Clamp01(Vector2.Distance(V(x, y), V(s / 2f, s / 2f)) / (s / 2f));
                return new Color(1, 0.85f, 0.5f, (1 - d) * (1 - d) * 0.9f);
            });
            return p.ToSprite("rs_city_glow", V(s / 2f, s / 2f));
        });

        /// <summary>The dig drill bot (pivot at its wheels).</summary>
        public static Sprite DrillBot() => Get("drillbot", () =>
        {
            int w = 120, h = 110;
            var p = new IsoPaint(w, h);
            var ink = new Color32(0x2B, 0x22, 0x33, 255);
            p.Ellipse(w / 2f, 10, 44, 9, new Color(0, 0, 0, 0.35f));
            p.Blob(34, 18, 13, 13, new Color32(0x4A, 0x4E, 0x5E, 255), ink, 4);
            p.Blob(82, 18, 13, 13, new Color32(0x4A, 0x4E, 0x5E, 255), ink, 4);
            p.Shape(RoundRect(18, 24, 78, 46, 12), VGrad(new Color32(0xE8, 0xA0, 0x2C, 255), new Color32(0xFF, 0xD0, 0x5C, 255), 24, 70), ink, 4);
            p.Shape(RoundRect(30, 66, 44, 30, 10), new Color32(0x7F, 0xE3, 0xFF, 255), ink, 4);
            p.Circle(52, 82, 5, Color.white);
            // the drill
            p.Shape(new List<Vector2> { V(94, 52), V(118, 40), V(94, 30) }, new Color32(0xB8, 0xC4, 0xD4, 255), ink, 3);
            p.Line(V(98, 46), V(108, 38), 2, ink);
            return p.ToSprite("rs_city_drillbot", V(w / 2f, 10));
        });

        /// <summary>Half an eggshell worn as a hat (pivot at its rim).</summary>
        public static Sprite Eggshell() => Get("eggshell", () =>
        {
            int w = 64, h = 44;
            var p = new IsoPaint(w, h);
            var ink = new Color32(0x2B, 0x22, 0x33, 255);
            var shell = new List<Vector2>();
            for (int i = 0; i <= 16; i++)
            {
                float a = Mathf.PI * i / 16;
                shell.Add(V(w / 2f + Mathf.Cos(a) * 26, 8 + Mathf.Sin(a) * 30));
            }
            // a zig-zag rim
            for (int i = 0; i <= 6; i++) shell.Add(V(w / 2f - 26 + i * 52 / 6f, i % 2 == 0 ? 8 : 2));
            p.Shape(shell, new Color32(0xFF, 0xF6, 0xE4, 255), ink, 3);
            p.Circle(24, 26, 4, new Color32(0x9E, 0xD8, 0x6A, 255));
            p.Circle(40, 20, 3, new Color32(0x9E, 0xD8, 0x6A, 255));
            return p.ToSprite("rs_city_eggshell", V(w / 2f, 5));
        });

        /// <summary>A social event bubble ("heart", "highfive", "scuffle", "gossip", "dance"), pivot at its tail.</summary>
        public static Sprite SocialBubble(string kind) => Get("social_" + kind, () =>
        {
            int w = 96, h = 96;
            var p = new IsoPaint(w, h);
            var ink = new Color32(0x2B, 0x22, 0x33, 255);
            p.Shape(new List<Vector2> { V(40, 20), V(48, 4), V(58, 20) }, Color.white, ink, 3);
            p.Blob(w / 2f, 54, 40, 34, Color.white, ink, 4);
            float cx = w / 2f, cy = 54;
            switch (kind)
            {
                case "heart":
                    p.Circle(cx - 9, cy + 6, 11, new Color32(0xF0, 0x4E, 0x6E, 255));
                    p.Circle(cx + 9, cy + 6, 11, new Color32(0xF0, 0x4E, 0x6E, 255));
                    p.Poly(new List<Vector2> { V(cx - 20, cy + 4), V(cx + 20, cy + 4), V(cx, cy - 20) }, new Color32(0xF0, 0x4E, 0x6E, 255));
                    break;
                case "highfive":
                    p.Shape(RoundRect(cx - 14, cy - 18, 28, 30, 8), new Color32(0xFF, 0xD0, 0x5C, 255), ink, 3);
                    for (int f = 0; f < 4; f++) p.Line(V(cx - 10 + f * 7, cy + 10), V(cx - 12 + f * 8, cy + 24), 6, new Color32(0xFF, 0xD0, 0x5C, 255));
                    p.Line(V(cx - 26, cy + 20), V(cx - 20, cy + 14), 3, ink);
                    p.Line(V(cx + 26, cy + 20), V(cx + 20, cy + 14), 3, ink);
                    break;
                case "scuffle":
                    p.Blob(cx - 10, cy, 14, 12, new Color32(0xC8, 0xC0, 0xB4, 255), ink, 3);
                    p.Blob(cx + 10, cy + 4, 14, 12, new Color32(0xC8, 0xC0, 0xB4, 255), ink, 3);
                    p.Poly(Star(cx + 2, cy + 18, 9, 5, 0.45f), new Color32(0xFF, 0xD0, 0x5C, 255));
                    p.Poly(Star(cx - 18, cy - 12, 6, 5, 0.45f), new Color32(0xFF, 0xD0, 0x5C, 255));
                    break;
                case "gossip":
                    for (int d = -1; d <= 1; d++) p.Circle(cx + d * 16, cy, 6, ink);
                    break;
                default: // dance: two notes
                    var note = new Color32(0xA7, 0x7B, 0xFF, 255);
                    p.Circle(cx - 12, cy - 10, 8, note);
                    p.Circle(cx + 12, cy - 4, 8, note);
                    p.Line(V(cx - 5, cy - 10), V(cx - 5, cy + 18), 4, note);
                    p.Line(V(cx + 19, cy - 4), V(cx + 19, cy + 22), 4, note);
                    p.Line(V(cx - 5, cy + 18), V(cx + 19, cy + 22), 6, note);
                    break;
            }
            return p.ToSprite("rs_city_social_" + kind, V(48, 4));
        });

        /// <summary>FX particles: "poof" (cloud), "dust", "spark" (star), "confetti" (white chip, tinted), "rock", "ring".</summary>
        public static Sprite Fx(string kind) => Get("fx_" + kind, () =>
        {
            int s = 64;
            var p = new IsoPaint(s, s);
            switch (kind)
            {
                case "poof":
                    p.Circle(24, 28, 16, new Color(1, 1, 1, 0.95f)); p.Circle(40, 30, 14, new Color(1, 1, 1, 0.95f)); p.Circle(32, 40, 14, new Color(1, 1, 1, 0.95f));
                    break;
                case "dust":
                    p.Circle(24, 28, 15, new Color(0.78f, 0.66f, 0.5f, 0.85f)); p.Circle(40, 30, 13, new Color(0.78f, 0.66f, 0.5f, 0.85f)); p.Circle(32, 40, 12, new Color(0.84f, 0.74f, 0.58f, 0.85f));
                    break;
                case "spark":
                    p.Poly(Star(32, 32, 28, 4, 0.32f), Color.white);
                    break;
                case "confetti":
                    p.Poly(new List<Vector2> { V(20, 26), V(44, 22), V(46, 38), V(22, 42) }, Color.white);
                    break;
                case "rock":
                    p.Shape(new List<Vector2> { V(10, 16), V(30, 8), V(54, 18), V(50, 40), V(28, 50), V(12, 36) }, new Color32(0x8E, 0x83, 0x99, 255), new Color32(0x3A, 0x34, 0x44, 255), 4);
                    break;
                default: // ring
                    p.Ellipse(32, 32, 30, 30, (x, y) =>
                    {
                        float d = Vector2.Distance(V(x, y), V(32, 32));
                        return new Color(1, 1, 1, Mathf.Clamp01(1 - Mathf.Abs(d - 26) / 4));
                    });
                    break;
            }
            return p.ToSprite("rs_city_fx_" + kind, V(s / 2f, s / 2f));
        });

        /// <summary>A plain drawn hut (fallback when the stand-in art is missing), pivot at its foot.</summary>
        public static Sprite Hut(Color roof) => Get("hut_" + ColorUtility.ToHtmlStringRGB(roof), () =>
        {
            int w = 140, h = 140;
            var p = new IsoPaint(w, h);
            var ink = new Color32(0x2B, 0x22, 0x33, 255);
            p.Shape(new List<Vector2> { V(20, 20), V(70, 4), V(120, 20), V(120, 74), V(70, 58), V(20, 74) }, new Color32(0xE8, 0xD6, 0xB0, 255), ink, 4);
            p.Shape(new List<Vector2> { V(10, 72), V(70, 54), V(130, 72), V(70, 132) }, Shade(roof, -0.05f), ink, 4);
            p.Shape(RoundRect(58, 8, 24, 34, 8), new Color32(0x7A, 0x4E, 0x2A, 255), ink, 3);
            return p.ToSprite("rs_city_hut", V(70, 10));
        });

        /// <summary>The goods a walker carries (the resource's icon).</summary>
        public static Sprite Goods(string currency) => SummonArt.CurrencyIcon(currency);
    }

    /// <summary>
    /// The baked land (City design §8 budget: ≤ 180 sprites): every cell's top (two-tone checker, rubble on undug patches, stone on
    /// the plaza, water on springs), the cliff walls under open front edges, the streets (16-variant autotile from the 4-neighbour
    /// mask) and the buildings' contact shadows painted into ONE texture, re-baked when the layout changes; the grid overlay is a
    /// second, half-resolution texture (15% normally, 40% in edit mode).
    /// </summary>
    public sealed class CityCanvas
    {
        public readonly int W, H;
        public readonly float Ppu;
        public readonly Vector2 Origin;
        public readonly Color32[] Px;

        public CityCanvas(Rect world, float ppu)
        {
            Ppu = ppu;
            Origin = world.min;
            W = Mathf.Max(4, Mathf.CeilToInt(world.width * ppu));
            H = Mathf.Max(4, Mathf.CeilToInt(world.height * ppu));
            Px = new Color32[W * H];
        }

        public Vector2 ToPx(Vector2 world) => (world - Origin) * Ppu;

        private void Blend(int i, Color32 c, float a)
        {
            a *= c.a / 255f;
            if (a <= 0.003f) return;
            if (a >= 0.997f) { Px[i] = new Color32(c.r, c.g, c.b, 255); return; }
            var d = Px[i];
            float da = d.a / 255f;
            float oa = a + da * (1 - a);
            if (oa <= 0) return;
            float k = da * (1 - a);
            Px[i] = new Color32((byte)((c.r * a + d.r * k) / oa), (byte)((c.g * a + d.g * k) / oa), (byte)((c.b * a + d.b * k) / oa), (byte)(oa * 255));
        }

        /// <summary>A convex polygon in world units (horizontal anti-aliasing on the span ends).</summary>
        public void Fill(IList<Vector2> world, Color32 c, float alpha = 1)
        {
            int n = world.Count;
            var pts = new Vector2[n];
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++) { pts[i] = ToPx(world[i]); minY = Mathf.Min(minY, pts[i].y); maxY = Mathf.Max(maxY, pts[i].y); }
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(maxY));
            for (int y = y0; y <= y1; y++)
            {
                float sy = y + 0.5f;
                float xl = float.MaxValue, xr = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % n];
                    if ((a.y <= sy && b.y > sy) || (b.y <= sy && a.y > sy))
                    {
                        float x = a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x);
                        if (x < xl) xl = x;
                        if (x > xr) xr = x;
                    }
                }
                if (xr <= xl) continue;
                int xa = Mathf.Max(0, Mathf.FloorToInt(xl)), xb = Mathf.Min(W - 1, Mathf.FloorToInt(xr));
                int row = y * W;
                for (int x = xa; x <= xb; x++)
                {
                    float cov = Mathf.Clamp01(Mathf.Min(x + 1, xr) - Mathf.Max(x, xl));
                    if (cov > 0) Blend(row + x, c, cov * alpha);
                }
            }
        }

        /// <summary>A soft ellipse (contact shadows): alpha falls off toward the rim.</summary>
        public void Soft(Vector2 centre, float rx, float ry, Color32 c, float alpha)
        {
            var p = ToPx(centre);
            float prx = rx * Ppu, pry = ry * Ppu;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(p.x - prx)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(p.x + prx));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(p.y - pry)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(p.y + pry));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x + 0.5f - p.x) / prx, dy = (y + 0.5f - p.y) / pry;
                    float d = dx * dx + dy * dy;
                    if (d >= 1) continue;
                    Blend(y * W + x, c, alpha * (1 - d));
                }
        }

        public Texture2D ToTexture(string name, Texture2D reuse = null)
        {
            var tex = reuse != null && reuse.width == W && reuse.height == H ? reuse
                : new Texture2D(W, H, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(Px);
            tex.Apply(false, false);
            return tex;
        }
    }

    /// <summary>Paints the land of a city grid (see <see cref="CityCanvas"/>).</summary>
    public static class CityTerrain
    {
        public const float Ppu = 64, GridPpu = 32;
        public const float WallTop = 0.07f, WallMid = 0.4f, WallDepth = 0.62f;

        private static readonly Color32 GrassA = new Color32(0x86, 0xC9, 0x6E, 255), GrassB = new Color32(0x7A, 0xBE, 0x64, 255);
        private static readonly Color32 GrassLip = new Color32(0x5C, 0x9E, 0x4E, 255);
        private static readonly Color32 RubbleA = new Color32(0x8C, 0x80, 0x98, 255), RubbleB = new Color32(0x82, 0x76, 0x8E, 255);
        private static readonly Color32 RubbleOpenA = new Color32(0xA4, 0x96, 0xB2, 255), RubbleOpenB = new Color32(0x9A, 0x8C, 0xA8, 255);
        private static readonly Color32 RubbleLip = new Color32(0x6A, 0x60, 0x78, 255);
        private static readonly Color32 Pebble = new Color32(0x5E, 0x55, 0x6C, 255), PebbleLight = new Color32(0xBC, 0xB0, 0xC8, 255);
        private static readonly Color32 StoneA = new Color32(0xD8, 0xC4, 0x9C, 255), StoneB = new Color32(0xCC, 0xB6, 0x8C, 255);
        private static readonly Color32 StreetEdge = new Color32(0xB4, 0x92, 0x5E, 255), StreetFill = new Color32(0xE6, 0xCC, 0x94, 255);
        private static readonly Color32 WaterEdge = new Color32(0x2E, 0x86, 0xB0, 255), Water = new Color32(0x5C, 0xC4, 0xEA, 255), WaterLight = new Color32(0xB8, 0xEC, 0xFF, 255);
        private static readonly Color32 WallLeftTop = new Color32(0xC0, 0x8E, 0x58, 255), WallLeftLow = new Color32(0x8C, 0x60, 0x3A, 255);
        private static readonly Color32 WallRightTop = new Color32(0x9E, 0x70, 0x42, 255), WallRightLow = new Color32(0x6C, 0x48, 0x2C, 255);
        private static readonly Color32 Shadow = new Color32(0x10, 0x20, 0x18, 255);

        /// <summary>What the painter needs to know about every cell.</summary>
        public enum CellKind : byte { Void, Land, Rubble, RubbleOpen, Plaza, Street, Spring }

        /// <summary>The world rect covering every non-void cell (plus the cliff and a margin).</summary>
        public static Rect Bounds(CellKind[] kinds, int w, int h, Func<float, float, Vector2> pos)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (kinds[y * w + x] == CellKind.Void) continue;
                    foreach (var c in Corners(pos, x, y))
                    {
                        x0 = Mathf.Min(x0, c.x); x1 = Mathf.Max(x1, c.x);
                        y0 = Mathf.Min(y0, c.y); y1 = Mathf.Max(y1, c.y);
                    }
                }
            if (x0 > x1) return new Rect(-1, -1, 2, 2);
            return Rect.MinMaxRect(x0 - 0.3f, y0 - WallDepth - 0.3f, x1 + 0.3f, y1 + 0.3f);
        }

        /// <summary>Top, right, bottom, left corners of a cell's diamond.</summary>
        public static Vector2[] Corners(Func<float, float, Vector2> pos, float x, float y) =>
            new[] { pos(x - 0.5f, y - 0.5f), pos(x + 0.5f, y - 0.5f), pos(x + 0.5f, y + 0.5f), pos(x - 0.5f, y + 0.5f) };

        /// <summary>
        /// Paints the land. <paramref name="kinds"/>: per cell; <paramref name="shadows"/>: footprints (x, y, w, h) that cast a soft
        /// shadow. Cells are painted back to front, so a front cell's top covers the cliff of the one behind it.
        /// </summary>
        public static CityCanvas Paint(CellKind[] kinds, int w, int h, Func<float, float, Vector2> pos, IList<RectInt> shadows, Rect bounds)
        {
            var cv = new CityCanvas(bounds, Ppu);
            bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && kinds[y * w + x] != CellKind.Void;
            bool StreetLike(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && (kinds[y * w + x] == CellKind.Street || kinds[y * w + x] == CellKind.Plaza);
            for (int d = 0; d <= w + h - 2; d++)
                for (int x = 0; x < w; x++)
                {
                    int y = d - x;
                    if (y < 0 || y >= h) continue;
                    var k = kinds[y * w + x];
                    if (k == CellKind.Void) continue;
                    var c = Corners(pos, x, y);
                    bool rubble = k == CellKind.Rubble || k == CellKind.RubbleOpen;
                    // cliff walls under the open front edges: x+1 → the lower-right face, y+1 → the lower-left face
                    if (!Solid(x + 1, y)) Wall(cv, c[1], c[2], WallRightTop, WallRightLow, rubble ? RubbleLip : GrassLip);
                    if (!Solid(x, y + 1)) Wall(cv, c[2], c[3], WallLeftTop, WallLeftLow, rubble ? RubbleLip : GrassLip);
                    bool alt = ((x + y) & 1) == 0;
                    switch (k)
                    {
                        case CellKind.Rubble:
                        case CellKind.RubbleOpen:
                        {
                            bool open = k == CellKind.RubbleOpen;
                            cv.Fill(c, open ? (alt ? RubbleOpenA : RubbleOpenB) : (alt ? RubbleA : RubbleB));
                            for (int i = 0; i < 3; i++)
                            {
                                float hx = WorldBoard.Hash(7, x, y, i * 2) - 0.5f, hy = WorldBoard.Hash(7, x, y, i * 2 + 1) - 0.5f;
                                var at = pos(x + hx * 0.7f, y + hy * 0.7f);
                                float r = 0.07f + 0.06f * WorldBoard.Hash(9, x, y, i);
                                cv.Soft(at + new Vector2(0.02f, -0.02f), r * 1.3f, r * 0.9f, Pebble, 0.9f);
                                cv.Soft(at, r, r * 0.7f, PebbleLight, open ? 0.9f : 0.6f);
                            }
                            break;
                        }
                        case CellKind.Plaza:
                            cv.Fill(c, alt ? StoneA : StoneB);
                            cv.Fill(Inset(c, 0.9f), alt ? StoneB : StoneA, 0.35f);
                            break;
                        case CellKind.Spring:
                            cv.Fill(c, alt ? GrassA : GrassB);
                            cv.Fill(Inset(c, 0.78f), WaterEdge);
                            cv.Fill(Inset(c, 0.66f), Water);
                            cv.Soft(pos(x - 0.1f, y - 0.1f), 0.22f, 0.1f, WaterLight, 0.8f);
                            break;
                        default:
                            cv.Fill(c, alt ? GrassA : GrassB);
                            break;
                    }
                    if (k == CellKind.Street) StreetTile(cv, pos, x, y, RogueStars.Client.App.StreetTiles.Mask(StreetLike, x, y));
                }
            // a worn ring around the fireplace on the plaza
            if (shadows != null)
                foreach (var r in shadows)
                {
                    var centre = pos(r.x + (r.width - 1) / 2f, r.y + (r.height - 1) / 2f);
                    float span = (r.width + r.height) * Iso.StepX * 0.5f;
                    cv.Soft(centre + new Vector2(0, -0.05f), span * 0.95f, span * 0.95f * Iso.StepY / Iso.StepX, Shadow, 0.42f);
                }
            return cv;
        }

        private static Vector2[] Inset(Vector2[] c, float k)
        {
            var m = (c[0] + c[2]) / 2;
            return new[] { m + (c[0] - m) * k, m + (c[1] - m) * k, m + (c[2] - m) * k, m + (c[3] - m) * k };
        }

        private static void Wall(CityCanvas cv, Vector2 a, Vector2 b, Color32 top, Color32 low, Color32 lip)
        {
            var down1 = new Vector2(0, -WallTop);
            var down2 = new Vector2(0, -WallMid);
            var down3 = new Vector2(0, -WallDepth);
            cv.Fill(new[] { a, b, b + down1, a + down1 }, lip);
            cv.Fill(new[] { a + down1, b + down1, b + down2, a + down2 }, top);
            cv.Fill(new[] { a + down2, b + down2, b + down3, a + down3 }, low);
        }

        /// <summary>One street tile: a centre square and an arm toward every connected neighbour (in grid space, projected).</summary>
        private static void StreetTile(CityCanvas cv, Func<float, float, Vector2> pos, int x, int y, int mask)
        {
            void Shape(float hw, Color32 col)
            {
                Quad(cv, pos, x - hw, y - hw, x + hw, y + hw, col);
                if ((mask & 1) != 0) Quad(cv, pos, x, y - hw, x + 0.5f, y + hw, col);
                if ((mask & 4) != 0) Quad(cv, pos, x - 0.5f, y - hw, x, y + hw, col);
                if ((mask & 2) != 0) Quad(cv, pos, x - hw, y, x + hw, y + 0.5f, col);
                if ((mask & 8) != 0) Quad(cv, pos, x - hw, y - 0.5f, x + hw, y, col);
            }
            Shape(0.36f, StreetEdge);
            Shape(0.29f, StreetFill);
        }

        private static void Quad(CityCanvas cv, Func<float, float, Vector2> pos, float x0, float y0, float x1, float y1, Color32 col) =>
            cv.Fill(new[] { pos(x0, y0), pos(x1, y0), pos(x1, y1), pos(x0, y1) }, col);

        /// <summary>The grid overlay: thin white lines around every dug land cell (half resolution).</summary>
        public static CityCanvas Grid(CellKind[] kinds, int w, int h, Func<float, float, Vector2> pos, Rect bounds)
        {
            var cv = new CityCanvas(bounds, GridPpu);
            var white = new Color32(255, 255, 255, 255);
            const float t = 0.035f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var k = kinds[y * w + x];
                    if (k != CellKind.Land && k != CellKind.Street && k != CellKind.Spring) continue;
                    var c = Corners(pos, x, y);
                    for (int e = 0; e < 4; e++)
                    {
                        var a = c[e]; var b = c[(e + 1) % 4];
                        var n = new Vector2(0, t);
                        cv.Fill(new[] { a + n, b + n, b - n, a - n }, white);
                    }
                }
            return cv;
        }
    }
}
