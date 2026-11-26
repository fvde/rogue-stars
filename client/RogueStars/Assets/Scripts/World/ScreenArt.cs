using System.Collections.Generic;
using RogueStars.Client.UiKit;
using UnityEngine;
using static RogueStars.Client.World.IsoPaint;

namespace RogueStars.Client.World
{
    /// <summary>
    /// Art of the screens pass (rest, events, chest, shop, home, run result): ArtCatalog first (Art/Run/Characters, Props,
    /// Icons; Art/Hub/Planet, Scene, Icons; Art/Result), else a code-drawn placeholder in the slice style (thick dark ink
    /// outline, soft gradients, a gloss). Our own flavour: aliens, worm, space, portals.
    /// </summary>
    public static class ScreenArt
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static readonly Color Ink = IsoArt.Ink;

        public static readonly string[] CharacterIds = { "shopkeeper", "cursed_vendor", "black_marketeer", "camp_cook" };
        public static readonly string[] PropIds = { "chest_common", "chest_mythic", "chest_signature", "abandoned_cart", "campfire_big", "event_shrine", "event_wreck" };
        public static readonly string[] IconIds = { "coins_small", "coins_big", "heal", "max_hp", "skill_book", "upgrade", "reroll", "curse", "blessing", "sale_tag" };
        public static readonly string[] ResultIds = { "crown", "crest_broken", "banner", "banner_defeat" };

        public static Sprite Character(string id) => ArtCatalog.RunCharacter(id) ?? Drawn("char:" + id, () => DrawCharacter(id));
        public static Sprite Prop(string id) => ArtCatalog.RunProp(id) ?? Drawn("prop:" + id, () => DrawProp(id));
        public static Sprite Icon(string id) => ArtCatalog.RunIcon(id) ?? Drawn("icon:" + id, () => DrawIcon(id));
        public static Sprite Result(string id) => ArtCatalog.Result(id) ?? Drawn("result:" + id, () => DrawResult(id));
        public static Sprite Planet(int stage) => ArtCatalog.Planet(stage) ?? Drawn("planet:" + Mathf.Clamp(stage, 1, 5), () => DrawPlanet(Mathf.Clamp(stage, 1, 5)));
        public static Sprite HubIcon(string id) => ArtCatalog.HubIcon(id) ?? Drawn("hub:" + id, () => DrawHubIcon(id));
        /// <summary>
        /// The home backdrop for a sky state (<see cref="ArtCatalog.SkyStates"/>: day, night, event, invasion): <c>Art/Hub/Scene/sky_&lt;state&gt;</c>,
        /// else the day sky, else the older per-stage / generic sky, else drawn.
        /// </summary>
        public static Sprite Sky(string state, int stage) => ArtCatalog.HubSceneArt("sky_" + (state ?? ArtCatalog.SkyDay)) ?? ArtCatalog.HubSceneArt("sky_" + ArtCatalog.SkyDay)
            ?? ArtCatalog.HubSceneArt("sky_stage" + stage) ?? ArtCatalog.HubSceneArt("sky") ?? Drawn("sky", DrawSky);

        /// <summary>A soft round glow (white, alpha falling off to the edge) for tinted halos behind props.</summary>
        public static Sprite Glow() => Drawn("glow", () =>
        {
            const int n = 256;
            var p = new IsoPaint(n, n);
            p.Ellipse(n / 2f, n / 2f, n / 2f, n / 2f, (x, y) =>
            {
                float d = Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), new Vector2(n / 2f, n / 2f)) / (n / 2f));
                return new Color(1, 1, 1, Mathf.Pow(1 - d, 1.8f));
            });
            return p.ToSprite("rs_glow", V(n / 2f, n / 2f));
        });

        private static Sprite Drawn(string key, System.Func<Sprite> draw)
        {
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            s = draw();
            Cache[key] = s;
            return s;
        }

        // ---- characters (busts, 512 × 512, pivot bottom-centre) ---------------------------------------------------

        /// <summary>
        /// Placeholder portrait of Mothwick, the Lost Courier (Run Quest 1) until its art exists (tools/art/packages/quest-mothwick.json):
        /// a round fuzzy moth with big soft wings, huge eyes, an oversized blue courier cap and a lantern with a star inside.
        /// </summary>
        private static Sprite DrawMothwick(IsoPaint p, float c)
        {
            var fur = (Color)new Color32(0xD8, 0xC8, 0xEC, 255);
            var wing = (Color)new Color32(0xB4, 0x9C, 0xE0, 255);
            var cap = (Color)new Color32(0x3C, 0x6C, 0xD8, 255);
            var gold = (Color)new Color32(0xFF, 0xC8, 0x3C, 255);
            foreach (int sd in new[] { -1, 1 })
            {
                p.Blob(c + sd * 150, 300, 120, 160, VGrad(Shade(wing, -0.25f), Shade(wing, 0.2f), 140, 460), Ink, 10);
                p.Blob(c + sd * 170, 330, 40, 52, Shade(wing, 0.35f), Ink, 0);
                p.Blob(c + sd * 120, 150, 70, 80, Shade(wing, -0.1f), Ink, 8);
            }
            p.Blob(c, 150, 120, 140, IsoPaint.Radial(Shade(fur, 0.25f), Shade(fur, -0.25f), c - 30, 200, 160), Ink, 10);
            p.Blob(c, 320, 130, 115, IsoPaint.Radial(Shade(fur, 0.3f), Shade(fur, -0.2f), c - 40, 360, 160), Ink, 10);
            foreach (int sd in new[] { -1, 1 })
            {
                p.Blob(c + sd * 52, 318, 40, 46, Color.white, Ink, 6);
                p.Blob(c + sd * 48, 312, 24, 30, Ink, Ink, 0);
                p.Circle(c + sd * 42, 326, 9, Color.white);
                p.Line(V(c + sd * 40, 430), V(c + sd * 90, 500), 9, Ink);
                p.Blob(c + sd * 92, 500, 14, 14, gold, Ink, 5);
            }
            p.Ellipse(c, 262, 24, 10, Shade(fur, -0.4f));
            // the oversized courier cap, slipping over one eye
            p.Shape(new[] { V(c - 140, 392), V(c + 120, 420), V(c + 110, 470), V(c - 120, 452) }, VGrad(Shade(cap, -0.25f), Shade(cap, 0.2f), 392, 470), Ink, 8);
            p.Shape(new[] { V(c - 160, 388), V(c + 40, 402), V(c + 20, 374), V(c - 150, 366) }, Shade(cap, -0.35f), Ink, 6);
            p.Shape(Star(c - 10, 440, 18, 5, 0.45f, 90), gold, Ink, 3);
            // the lantern with Pim inside
            p.Ellipse(c + 160, 120, 70, 70, new Color(1f, 0.9f, 0.5f, 0.35f));
            p.Shape(RoundRect(c + 128, 70, 64, 96, 16), IsoPaint.Radial(Color.white, new Color32(0xFF, 0xE6, 0x8A, 255), c + 160, 118, 56), Ink, 6);
            p.Shape(Star(c + 160, 118, 20, 5, 0.45f, 90), gold, Ink, 3);
            p.Line(V(c + 160, 166), V(c + 120, 210), 7, Ink);
            return p.ToSprite("rs_char_quest_mothwick", V(c, 0));
        }

        private static Sprite DrawCharacter(string id)
        {
            const int n = 512;
            var p = new IsoPaint(n, n);
            float c = n / 2f;
            Color skin, robe, accent;
            switch (id)
            {
                case "cursed_vendor": skin = new Color32(0x6E, 0x3C, 0xA8, 255); robe = new Color32(0x5A, 0x2E, 0x90, 255); accent = new Color32(0xF6, 0xD8, 0x5A, 255); break;
                case "black_marketeer": skin = new Color32(0x6A, 0x7C, 0x94, 255); robe = new Color32(0x2C, 0x30, 0x44, 255); accent = new Color32(0x5C, 0xF0, 0xC8, 255); break;
                case "camp_cook": skin = new Color32(0xF4, 0x9A, 0x48, 255); robe = new Color32(0xE8, 0xE2, 0xD6, 255); accent = new Color32(0xE8, 0x4A, 0x4A, 255); break;
                default: skin = new Color32(0x7C, 0xD0, 0x6A, 255); robe = new Color32(0xC8, 0x8A, 0x3C, 255); accent = new Color32(0x8A, 0x4C, 0xC8, 255); break;
            }
            if (id == "quest_mothwick") return DrawMothwick(p, c);
            // shoulders / robe
            p.Shape(new[] { V(40, 0), V(472, 0), V(436, 150), V(c + 120, 210), V(c - 120, 210), V(76, 150) },
                VGrad(Shade(robe, -0.35f), Shade(robe, 0.15f), 0, 210), Ink, 10);
            if (id == "cursed_vendor")
            {
                // a deep hood, three glowing eyes in the dark
                p.Shape(new[] { V(c - 190, 150), V(c - 150, 360), V(c - 40, 470), V(c + 90, 490), V(c + 150, 430), V(c + 190, 150) },
                    VGrad(Shade(robe, -0.3f), Shade(robe, 0.25f), 150, 490), Ink, 10);
                p.Blob(c, 270, 120, 110, new Color32(0x16, 0x0E, 0x24, 255), Ink, 6);
                p.Blob(c - 48, 290, 22, 14, accent, Ink, 0);
                p.Blob(c + 48, 290, 22, 14, accent, Ink, 0);
                p.Blob(c, 330, 16, 11, accent, Ink, 0);
                p.Shape(Star(c, 120, 34, 4, 0.45f, 90), accent, Ink, 5);
                return p.ToSprite("rs_char_" + id, V(c, 0));
            }
            // head: a big round alien head, antennae
            foreach (int sd in new[] { -1, 1 })
            {
                p.Line(V(c + sd * 60, 400), V(c + sd * 110, 488), 14, Ink);
                p.Line(V(c + sd * 60, 400), V(c + sd * 110, 488), 7, Shade(skin, -0.2f));
                p.Blob(c + sd * 112, 488, 18, 18, accent, Ink, 6);
            }
            p.Blob(c, 300, 150, 130, IsoPaint.Radial(Shade(skin, 0.3f), Shade(skin, -0.3f), c - 50, 360, 230), Ink, 10);
            p.Ellipse(c - 70, 370, 34, 18, new Color(1, 1, 1, 0.35f));
            if (id == "black_marketeer")
            {
                // goggles and a high collar
                p.Line(V(c - 150, 310), V(c + 150, 310), 18, Ink);
                foreach (int sd in new[] { -1, 1 })
                {
                    p.Blob(c + sd * 56, 312, 44, 40, Shade(robe, 0.1f), Ink, 8);
                    p.Blob(c + sd * 56, 312, 28, 26, accent, Ink, 0);
                    p.Circle(c + sd * 56 - 8, 320, 8, Color.white);
                }
                p.Shape(new[] { V(c - 150, 200), V(c - 60, 250), V(c, 210), V(c + 60, 250), V(c + 150, 200), V(c + 120, 150), V(c - 120, 150) }, Shade(robe, 0.1f), Ink, 8);
                p.Line(V(c - 40, 236), V(c + 40, 236), 8, Ink);
            }
            else
            {
                foreach (int sd in new[] { -1, 1 })
                {
                    p.Blob(c + sd * 52, 320, 32, 38, Color.white, Ink, 7);
                    p.Circle(c + sd * 52 + 6, 314, 16, new Color32(0x1C, 0x1A, 0x2E, 255));
                    p.Circle(c + sd * 52 + 12, 322, 6, Color.white);
                }
                // a wide smile
                var smile = new List<Vector2>();
                for (int i = 0; i <= 12; i++) { float a = Mathf.Lerp(200, 340, i / 12f) * Mathf.Deg2Rad; smile.Add(V(c + Mathf.Cos(a) * 60, 250 + Mathf.Sin(a) * 34)); }
                p.Stroke(smile, 10, Ink, false);
            }
            if (id == "camp_cook")
            {
                // a chef's hat and a ladle
                p.Shape(RoundRect(c - 80, 410, 160, 50, 14), Color.white, Ink, 8);
                p.Blob(c - 50, 476, 50, 44, Color.white, Ink, 8);
                p.Blob(c + 50, 476, 50, 44, Color.white, Ink, 8);
                p.Blob(c, 494, 54, 48, Color.white, Ink, 8);
                p.Line(V(c + 170, 40), V(c + 200, 300), 16, Ink);
                p.Line(V(c + 170, 40), V(c + 200, 300), 8, new Color32(0xB8, 0xC0, 0xD0, 255));
                p.Blob(c + 204, 316, 36, 22, new Color32(0xB8, 0xC0, 0xD0, 255), Ink, 7);
            }
            else if (id == "shopkeeper")
            {
                // a floppy purple cap and a gem held up
                p.Shape(new[] { V(c - 160, 380), V(c - 120, 470), V(c + 40, 500), V(c + 170, 450), V(c + 150, 380) }, VGrad(Shade(accent, -0.25f), Shade(accent, 0.2f), 380, 500), Ink, 9);
                p.Blob(c + 186, 230, 40, 40, skin, Ink, 8);
                p.Shape(new[] { V(c + 186, 330), V(c + 220, 290), V(c + 186, 250), V(c + 152, 290) }, new Color32(0xF0, 0x40, 0x60, 255), Ink, 6);
                p.Line(V(c - 120, 120), V(c + 120, 120), 12, new Color32(0xF6, 0xC8, 0x48, 255));
            }
            return p.ToSprite("rs_char_" + id, V(c, 0));
        }

        // ---- props (pivot bottom-centre) --------------------------------------------------------------------------

        private static Sprite DrawProp(string id)
        {
            switch (id)
            {
                case "chest_common": return Chest(new Color32(0xB8, 0x6A, 0x36, 255), new Color32(0xF2, 0xC2, 0x40, 255), "rs_chest_common");
                case "chest_mythic": return Chest(new Color32(0xD8, 0x4A, 0x3A, 255), new Color32(0xF8, 0xCC, 0x48, 255), "rs_chest_mythic");
                case "chest_signature": return Chest(new Color32(0x6C, 0x3C, 0xC8, 255), new Color32(0x6C, 0xF0, 0xE0, 255), "rs_chest_signature");
                case "campfire_big": return Campfire();
                case "abandoned_cart": return Cart();
                case "event_shrine": return Shrine();
                default: return Wreck();
            }
        }

        private static Sprite Chest(Color wood, Color metal, string name)
        {
            int w = 640, h = 560;
            var p = new IsoPaint(w, h);
            float c = w / 2f;
            p.Ellipse(c, 40, 270, 34, new Color(0, 0, 0, 0.35f));
            // body
            p.Shape(RoundRect(70, 50, 500, 250, 30), VGrad(Shade(wood, -0.35f), Shade(wood, 0.1f), 50, 300), Ink, 12);
            // lid (a rounded hump)
            var lid = new List<Vector2> { V(70, 290), V(570, 290) };
            for (int i = 0; i <= 20; i++) { float a = Mathf.Lerp(0, 180, i / 20f) * Mathf.Deg2Rad; lid.Add(V(c + Mathf.Cos(a) * 250, 290 + Mathf.Sin(a) * 200)); }
            p.Shape(lid, VGrad(Shade(wood, -0.1f), Shade(wood, 0.3f), 290, 490), Ink, 12);
            // metal bands
            foreach (float x in new[] { 150f, 490f })
            {
                p.Shape(new[] { V(x - 36, 50), V(x + 36, 50), V(x + 36, 300), V(x - 36, 300) }, VGrad(Shade(metal, -0.3f), Shade(metal, 0.2f), 50, 300), Ink, 9);
                var band = new List<Vector2>();
                for (int i = 0; i <= 12; i++) { float a = Mathf.Lerp(0, 180, i / 12f) * Mathf.Deg2Rad; band.Add(V(x + Mathf.Cos(a) * 36, 300 + Mathf.Sin(a) * (180 - Mathf.Abs(x - c) * 0.35f))); }
                p.Shape(band, VGrad(Shade(metal, -0.1f), Shade(metal, 0.35f), 300, 480), Ink, 9);
            }
            p.Shape(RoundRect(70, 270, 500, 44, 12), VGrad(Shade(metal, -0.25f), Shade(metal, 0.25f), 270, 314), Ink, 10);
            // lock plate with a keyhole
            p.Shape(new[] { V(c - 60, 330), V(c + 60, 330), V(c + 60, 220), V(c, 170), V(c - 60, 220) }, VGrad(Shade(metal, -0.2f), Shade(metal, 0.4f), 170, 330), Ink, 10);
            p.Circle(c, 270, 18, Ink);
            p.Shape(new[] { V(c - 10, 270), V(c + 10, 270), V(c + 14, 210), V(c - 14, 210) }, Ink, Ink, 0);
            p.Ellipse(c - 160, 440, 50, 18, new Color(1, 1, 1, 0.3f));
            return p.ToSprite(name, V(c, 0));
        }

        private static Sprite Campfire()
        {
            int w = 560, h = 600;
            var p = new IsoPaint(w, h);
            float c = w / 2f;
            var stone = (Color)new Color32(0x9C, 0xA0, 0xB4, 255);
            p.Ellipse(c, 80, 250, 80, new Color(0, 0, 0, 0.3f));
            // stone ring (back half), logs, flame, ring (front half)
            for (int i = 0; i < 10; i++)
            {
                float a = (i / 10f) * Mathf.PI * 2;
                float x = c + Mathf.Cos(a) * 200, y = 120 + Mathf.Sin(a) * 60;
                if (Mathf.Sin(a) > 0) p.Blob(x, y, 46, 30, VGrad(Shade(stone, -0.3f), Shade(stone, 0.2f), y - 30, y + 30), Ink, 8);
            }
            var wood = (Color)new Color32(0x9A, 0x5A, 0x30, 255);
            p.Line(V(c - 150, 100), V(c + 120, 170), 46, Ink); p.Line(V(c - 150, 100), V(c + 120, 170), 32, wood);
            p.Line(V(c + 150, 100), V(c - 120, 170), 46, Ink); p.Line(V(c + 150, 100), V(c - 120, 170), 32, Shade(wood, 0.15f));
            // an alien flame: teal-green, layered
            var outer = (Color)new Color32(0x4C, 0xE0, 0x8C, 255);
            p.Shape(new[] { V(c - 140, 150), V(c - 172, 250), V(c - 128, 360), V(c - 96, 300), V(c - 70, 420), V(c - 20, 560), V(c + 20, 470), V(c + 70, 520), V(c + 96, 400), V(c + 150, 440), V(c + 170, 290), V(c + 140, 150) },
                VGrad(Shade(outer, -0.2f), Shade(outer, 0.35f), 150, 560), Ink, 10);
            p.Shape(new[] { V(c - 80, 160), V(c - 96, 250), V(c - 50, 330), V(c - 10, 420), V(c + 20, 340), V(c + 60, 380), V(c + 92, 260), V(c + 80, 160) }, new Color32(0xB8, 0xFF, 0xD8, 255), Ink, 0);
            p.Shape(Star(c - 10, 250, 20, 4, 0.4f, 90), Color.white, Ink, 0);
            p.Shape(Star(c + 50, 320, 14, 4, 0.4f, 90), Color.white, Ink, 0);
            for (int i = 0; i < 10; i++)
            {
                float a = (i / 10f) * Mathf.PI * 2;
                float x = c + Mathf.Cos(a) * 200, y = 120 + Mathf.Sin(a) * 60;
                if (Mathf.Sin(a) <= 0) p.Blob(x, y, 50, 34, VGrad(Shade(stone, -0.3f), Shade(stone, 0.25f), y - 34, y + 34), Ink, 8);
            }
            return p.ToSprite("rs_campfire_big", V(c, 40));
        }

        /// <summary>A flame outline: a round belly narrowing into a curled tip with two side tongues.</summary>
        private static List<Vector2> Flame(float cx, float y0, float r, float h)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i <= 24; i++)
            {
                float t = i / 24f;                 // 0 = right base … 1 = left base, around the top
                float a = Mathf.Lerp(-20, 200, t) * Mathf.Deg2Rad;
                float lift = Mathf.Sin(t * Mathf.PI);
                float tongue = 0.18f * Mathf.Sin(t * Mathf.PI * 3) * lift;
                float x = cx + Mathf.Cos(a) * r * (1 - 0.55f * lift * lift);
                float y = y0 + r * 0.5f + Mathf.Sin(a) * r * 0.5f + h * Mathf.Pow(lift, 3) * (1 + tongue);
                pts.Add(V(x + 22 * lift * lift * Mathf.Sin(t * 9), y));
            }
            return pts;
        }

        private static Sprite Cart()
        {
            int w = 640, h = 520;
            var p = new IsoPaint(w, h);
            float c = w / 2f;
            var hull = (Color)new Color32(0x8C, 0x7A, 0xB8, 255);
            p.Ellipse(c, 40, 260, 30, new Color(0.4f, 1f, 0.9f, 0.35f));
            // a tipped-over hover cart with a torn tarp and spilled coins
            p.Shape(new[] { V(80, 110), V(560, 140), V(590, 300), V(60, 270) }, VGrad(Shade(hull, -0.35f), Shade(hull, 0.15f), 110, 300), Ink, 11);
            p.Shape(new[] { V(70, 270), V(590, 300), V(520, 440), V(160, 430) }, VGrad(new Color32(0xB0, 0x8A, 0x5A, 255), new Color32(0xE0, 0xC0, 0x84, 255), 270, 440), Ink, 11);
            p.Shape(new[] { V(400, 437), V(460, 437), V(440, 380), V(420, 400) }, new Color32(0x2A, 0x20, 0x30, 255), Ink, 0);
            foreach (float x in new[] { 150f, 470f }) { p.Blob(x, 110, 40, 22, new Color32(0x5C, 0xF0, 0xE0, 255), Ink, 6); }
            var coin = (Color)new Color32(0xF6, 0xCC, 0x48, 255);
            for (int i = 0; i < 9; i++) p.Blob(380 + (i % 5) * 44 - (i / 5) * 20, 60 + (i / 5) * 26, 24, 12, coin, Ink, 5);
            return p.ToSprite("rs_abandoned_cart", V(c, 20));
        }

        private static Sprite Shrine()
        {
            int w = 480, h = 640;
            var p = new IsoPaint(w, h);
            float c = w / 2f;
            var stone = (Color)new Color32(0x7C, 0x84, 0xB0, 255);
            p.Ellipse(c, 40, 200, 34, new Color(0, 0, 0, 0.3f));
            p.Shape(RoundRect(c - 180, 30, 360, 90, 20), VGrad(Shade(stone, -0.4f), Shade(stone, 0.1f), 30, 120), Ink, 10);
            p.Shape(new[] { V(c - 100, 110), V(c + 100, 110), V(c + 60, 560), V(c, 620), V(c - 60, 560) }, VGrad(Shade(stone, -0.3f), Shade(stone, 0.3f), 110, 620), Ink, 10);
            var glow = (Color)new Color32(0x9C, 0x6C, 0xFF, 255);
            p.Ellipse(c, 360, 70, 120, new Color(glow.r, glow.g, glow.b, 0.25f));
            p.Shape(Star(c, 360, 56, 4, 0.4f, 90), new Color32(0xE6, 0xD8, 0xFF, 255), Ink, 6);
            p.Circle(c, 360, 14, glow);
            return p.ToSprite("rs_event_shrine", V(c, 20));
        }

        private static Sprite Wreck()
        {
            int w = 640, h = 520;
            var p = new IsoPaint(w, h);
            float c = w / 2f;
            var hull = (Color)new Color32(0xB8, 0xC0, 0xD8, 255);
            p.Ellipse(c, 50, 280, 40, new Color(0, 0, 0, 0.3f));
            // a crashed escape pod half-sunk in the ground, a cracked window, smoke
            p.Shape(new[] { V(60, 60), V(420, 40), V(560, 180), V(500, 330), V(160, 360), V(40, 200) }, VGrad(Shade(hull, -0.4f), Shade(hull, 0.2f), 40, 360), Ink, 11);
            p.Blob(330, 220, 90, 70, new Color32(0x3C, 0xD0, 0xF0, 255), Ink, 9);
            p.Line(V(290, 260), V(340, 210), 6, Ink); p.Line(V(340, 210), V(320, 170), 6, Ink);
            p.Line(V(120, 120), V(470, 100), 10, new Color32(0xF0, 0x80, 0x40, 255));
            for (int i = 0; i < 4; i++) p.Blob(500 + i * 18, 380 + i * 34, 40 - i * 4, 34 - i * 4, new Color(0.6f, 0.6f, 0.7f, 0.5f - i * 0.08f), new Color(0, 0, 0, 0), 0);
            return p.ToSprite("rs_event_wreck", V(c, 30));
        }

        // ---- icons (256 × 256, centred) ---------------------------------------------------------------------------

        private static Sprite DrawIcon(string id)
        {
            const int n = 256;
            var p = new IsoPaint(n, n);
            float c = n / 2f;
            var coin = (Color)new Color32(0x9C, 0xC8, 0xF0, 255);
            var red = (Color)new Color32(0xF0, 0x3E, 0x4E, 255);
            var green = (Color)new Color32(0x6C, 0xC8, 0x3C, 255);
            switch (id)
            {
                case "coins_small":
                    for (int i = 0; i < 3; i++) Coin(p, 80 + i * 40, 70 + (i % 2) * 20, 46, coin);
                    for (int i = 0; i < 2; i++) Coin(p, 110 + i * 46, 130 + i * 10, 50, coin);
                    break;
                case "coins_big":
                    p.Blob(c, 90, 108, 70, VGrad(new Color32(0x7A, 0x4C, 0x2C, 255), new Color32(0xB0, 0x78, 0x48, 255), 20, 160), Ink, 9);
                    for (int i = 0; i < 6; i++) Coin(p, 60 + (i % 3) * 66, 150 + (i / 3) * 34, 44, coin);
                    Coin(p, 50, 60, 44, coin); Coin(p, 206, 56, 40, coin);
                    break;
                case "heal": Heart(p, c - 8, c - 12, 5.4f, red); Plus(p, 196, 196, 40, green); break;
                case "max_hp": Heart(p, c - 6, c - 12, 5.4f, red); Arrow(p, 206, 160, 42, green); break;
                case "skill_book": return HudArt.Icon("book");
                case "reroll": return HudArt.Icon("reroll");
                case "upgrade":
                    Arrow(p, c, 150, 90, green);
                    Arrow(p, c, 70, 70, Shade(green, -0.15f));
                    break;
                case "blessing": Burst(p, c, c, (Color)new Color32(0x7A, 0xA8, 0x3C, 255), true); break;
                case "curse": Burst(p, c, c, (Color)new Color32(0xA8, 0x34, 0x5A, 255), false); break;
                case "stamina":
                {
                    var blue = (Color)new Color32(0x5C, 0xC8, 0xF0, 255);
                    p.Shape(new[] { V(150, 240), V(60, 120), V(120, 120), V(90, 16), V(200, 150), V(138, 150), V(190, 240) }, VGrad(Shade(blue, -0.2f), Shade(blue, 0.35f), 16, 240), Ink, 9);
                    break;
                }
                case "sale_tag":
                    p.Shape(new[] { V(30, 150), V(110, 230), V(230, 230), V(230, 70), V(110, 70) }, VGrad(Shade(red, -0.2f), Shade(red, 0.2f), 70, 230), Ink, 8);
                    p.Circle(200, 200, 12, Ink);
                    break;
                default: p.Blob(c, c, 90, 90, Color.gray, Ink, 8); break;
            }
            return p.ToSprite("rs_icon_" + id, V(c, c));
        }

        private static void Coin(IsoPaint p, float x, float y, float r, Color col)
        {
            p.Blob(x, y - r * 0.18f, r, r * 0.55f, Shade(col, -0.45f), Ink, 6);
            p.Blob(x, y, r, r * 0.55f, IsoPaint.Radial(Shade(col, 0.35f), Shade(col, -0.2f), x - r * 0.3f, y + r * 0.2f, r * 1.4f), Ink, 6);
            p.Blob(x, y, r * 0.6f, r * 0.32f, Shade(col, -0.15f), Shade(col, -0.45f), 3);
        }

        private static void Heart(IsoPaint p, float cx, float cy, float k, Color red)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 40f * Mathf.PI * 2;
                float x = 16 * Mathf.Pow(Mathf.Sin(t), 3);
                float y = 13 * Mathf.Cos(t) - 5 * Mathf.Cos(2 * t) - 2 * Mathf.Cos(3 * t) - Mathf.Cos(4 * t);
                pts.Add(V(cx + x * k, cy + 6 + y * k));
            }
            p.Shape(pts, IsoPaint.Radial(Shade(red, 0.12f), Shade(red, -0.28f), cx - 20, cy + 26, 150), Ink, 10);
            p.Ellipse(cx - 9 * k, cy + 9 * k, 4 * k, 2.6f * k, new Color(1, 1, 1, 0.7f));
        }

        private static void Plus(IsoPaint p, float x, float y, float r, Color col)
        {
            p.Shape(new[] { V(x - r * 0.35f, y - r), V(x + r * 0.35f, y - r), V(x + r * 0.35f, y - r * 0.35f), V(x + r, y - r * 0.35f), V(x + r, y + r * 0.35f), V(x + r * 0.35f, y + r * 0.35f),
                V(x + r * 0.35f, y + r), V(x - r * 0.35f, y + r), V(x - r * 0.35f, y + r * 0.35f), V(x - r, y + r * 0.35f), V(x - r, y - r * 0.35f), V(x - r * 0.35f, y - r * 0.35f) }, col, Ink, 6);
        }

        private static void Arrow(IsoPaint p, float x, float y, float r, Color col)
        {
            p.Shape(new[] { V(x, y + r), V(x + r, y), V(x + r * 0.42f, y), V(x + r * 0.42f, y - r), V(x - r * 0.42f, y - r), V(x - r * 0.42f, y), V(x - r, y) }, VGrad(Shade(col, -0.2f), Shade(col, 0.25f), y - r, y + r), Ink, 7);
        }

        /// <summary>The blessing / curse emblem: a four-pointed burst with small chevrons pointing up (blessing) or down (curse).</summary>
        private static void Burst(IsoPaint p, float cx, float cy, Color col, bool up)
        {
            p.Shape(Star(cx, cy, 104, 4, 0.26f, 90), col, col, 0);
            p.Shape(Star(cx, cy, 62, 4, 0.34f, 45), col, col, 0);
            p.Circle(cx, cy, 15, new Color32(0xFB, 0xF0, 0xD0, 255));
            float d = up ? 1 : -1;
            foreach (var q in new[] { V(cx - 104, cy + 64 * d), V(cx + 100, cy - 40 * d) })
            {
                p.Line(V(q.x - 18, q.y - 10 * d), V(q.x, q.y + 8 * d), 12, col);
                p.Line(V(q.x, q.y + 8 * d), V(q.x + 18, q.y - 10 * d), 12, col);
            }
        }

        // ---- result crest -----------------------------------------------------------------------------------------

        private static Sprite DrawResult(string id)
        {
            int w = 560, h = 400;
            var p = new IsoPaint(w, h);
            float c = w / 2f;
            bool broken = id == "crest_broken";
            if (id == "banner" || id == "banner_defeat")
            {
                // a hanging pennant: gold frame, a diamond-checked field, two gold tails behind it
                int bw = 600, bh = 640;
                var q = new IsoPaint(bw, bh);
                float bc = bw / 2f;
                var gold2 = (Color)new Color32(0xF2, 0xC2, 0x3C, 255);
                var field = id == "banner" ? (Color)new Color32(0xE8, 0x58, 0x6C, 255) : (Color)new Color32(0x5C, 0x60, 0x7C, 255);
                foreach (int sd in new[] { -1, 1 })
                    q.Shape(new[] { V(bc + sd * 150, 260), V(bc + sd * 270, 200), V(bc + sd * 270, 10), V(bc + sd * 215, 70), V(bc + sd * 150, 20) }, gold2, Ink, 8);
                q.Shape(new[] { V(30, bh - 6), V(bw - 30, bh - 6), V(bw - 30, 250), V(bc, 50), V(30, 250) }, gold2, Ink, 10);
                q.Shape(new[] { V(66, bh - 6), V(bw - 66, bh - 6), V(bw - 66, 270), V(bc, 100), V(66, 270) }, (x, y) =>
                {
                    float u = (x + y) / 70f, v = (x - y) / 70f;
                    bool alt = (Mathf.FloorToInt(u) + Mathf.FloorToInt(v)) % 2 == 0;
                    return alt ? field : Shade(field, 0.08f);
                }, Shade(field, -0.35f), 5);
                return q.ToSprite("rs_result_" + id, V(bc, bh / 2f));
            }
            var gold = broken ? (Color)new Color32(0x9C, 0x98, 0xA8, 255) : (Color)new Color32(0xF6, 0xCC, 0x40, 255);
            // laurels
            foreach (int sd in new[] { -1, 1 })
                for (int i = 0; i < 5; i++)
                {
                    float a = Mathf.Lerp(200, 120, i / 4f) * Mathf.Deg2Rad;
                    float x = c + sd * (Mathf.Cos(a) * -190), y = 120 + Mathf.Sin(a) * 110;
                    p.Blob(x, y, 46, 26, VGrad(Shade(gold, -0.3f), Shade(gold, 0.15f), y - 26, y + 26), Ink, 8);
                }
            // the crown
            p.Shape(new[] { V(c - 150, 110), V(c + 150, 110), V(c + 170, 330), V(c + 90, 250), V(c + 40, 360), V(c, 260), V(c - 40, 360), V(c - 90, 250), V(c - 170, 330) },
                VGrad(Shade(gold, -0.25f), Shade(gold, 0.35f), 110, 360), Ink, 12);
            p.Shape(RoundRect(c - 150, 100, 300, 50, 10), VGrad(Shade(gold, -0.35f), Shade(gold, 0.1f), 100, 150), Ink, 10);
            p.Poly(new[] { V(c - 40, 150), V(c + 10, 150), V(c - 10, 330), V(c - 40, 340) }, new Color(1, 1, 1, 0.25f));
            if (broken)
            {
                p.Line(V(c + 20, 360), V(c - 10, 260), 9, Ink);
                p.Line(V(c - 10, 260), V(c + 30, 190), 9, Ink);
                p.Line(V(c + 30, 190), V(c - 5, 110), 9, Ink);
            }
            else
            {
                p.Shape(Star(c, 200, 30, 5, 0.45f, 90), new Color32(0xE8, 0x4A, 0x5A, 255), Ink, 6);
            }
            return p.ToSprite("rs_result_" + id, V(c, h / 2f));
        }

        // ---- home: planet, sky, icons -----------------------------------------------------------------------------

        /// <summary>
        /// The home planet (stage 1 = a bare rock with one portal; each stage adds green, domes, towers and a ring) seen from
        /// a little above, 900 × 900.
        /// </summary>
        private static Sprite DrawPlanet(int stage)
        {
            int n = 900;
            var p = new IsoPaint(n, n);
            float c = n / 2f, r = 330;
            var rock = stage <= 1 ? (Color)new Color32(0xA8, 0x8C, 0x7C, 255) : (Color)new Color32(0x6C, 0x9C, 0xD8, 255);
            var land = (Color)new Color32(0x7C, 0xC8, 0x5A, 255);
            // a back ring from stage 4
            if (stage >= 4) p.Ellipse(c, c - 20, r + 150, 70, new Color(0.85f, 0.8f, 1f, 0.5f));
            // atmosphere glow
            p.Ellipse(c, c, r + 40, r + 40, new Color(0.55f, 0.85f, 1f, 0.22f));
            p.Blob(c, c, r, r, IsoPaint.Radial(Shade(rock, 0.3f), Shade(rock, -0.45f), c - 110, c + 120, r * 1.7f), Ink, 14);
            // land masses / craters
            if (stage >= 2)
            {
                p.Blob(c - 120, c + 60, 150, 110, IsoPaint.Radial(Shade(land, 0.25f), Shade(land, -0.3f), c - 160, c + 110, 220), Ink, 8);
                p.Blob(c + 150, c - 90, 120, 90, IsoPaint.Radial(Shade(land, 0.2f), Shade(land, -0.35f), c + 120, c - 50, 180), Ink, 8);
                if (stage >= 3) p.Blob(c + 60, c + 210, 110, 50, Shade(land, -0.05f), Ink, 7);
            }
            else
            {
                foreach (var (x, y, rr) in new[] { (c - 120f, c + 80f, 60f), (c + 140f, c - 60f, 46f), (c + 40f, c + 190f, 34f), (c - 60f, c - 170f, 40f) })
                {
                    p.Blob(x, y, rr, rr * 0.8f, Shade(rock, -0.25f), Shade(rock, -0.5f), 5);
                    p.Ellipse(x + rr * 0.15f, y - rr * 0.1f, rr * 0.7f, rr * 0.5f, Shade(rock, -0.4f));
                }
            }
            // buildings: domes and towers sitting on the upper rim, more per stage
            int domes = Mathf.Clamp(stage - 1, 0, 4) * 2;
            for (int i = 0; i < domes; i++)
            {
                float a = Mathf.Lerp(150, 30, (i + 0.5f) / domes) * Mathf.Deg2Rad;
                float x = c + Mathf.Cos(a) * (r - 30), y = c + Mathf.Sin(a) * (r - 30);
                if (i % 2 == 0) p.Blob(x, y + 18, 40, 34, IsoPaint.Radial(new Color32(0xE8, 0xF8, 0xFF, 255), new Color32(0x8C, 0xC8, 0xE8, 255), x - 10, y + 30, 70), Ink, 6);
                else { p.Shape(RoundRect(x - 16, y, 32, 90, 8), VGrad(new Color32(0x9C, 0x8C, 0xC8, 255), new Color32(0xD8, 0xD0, 0xF0, 255), y, y + 90), Ink, 6); p.Circle(x, y + 92, 10, new Color32(0xFF, 0xD8, 0x48, 255)); }
            }
            // the portal on the planet: a glowing ring
            p.Blob(c + 10, c - 10, 62, 74, new Color32(0x2A, 0x1C, 0x48, 255), Ink, 9);
            p.Blob(c + 10, c - 10, 44, 56, IsoPaint.Radial(new Color32(0xD8, 0xA8, 0xFF, 255), new Color32(0x6C, 0x3C, 0xC8, 255), c + 10, c - 10, 70), new Color(0, 0, 0, 0), 0);
            // the front of the ring
            if (stage >= 4)
            {
                var ring = new List<Vector2>();
                for (int i = 0; i <= 30; i++) { float a = Mathf.Lerp(180, 360, i / 30f) * Mathf.Deg2Rad; ring.Add(V(c + Mathf.Cos(a) * (r + 150), c - 20 + Mathf.Sin(a) * 70)); }
                p.Stroke(ring, 30, Ink, false);
                p.Stroke(ring, 20, new Color32(0xD8, 0xC8, 0xFF, 255), false);
            }
            p.Ellipse(c - 140, c + 170, 70, 36, new Color(1, 1, 1, 0.28f));
            return p.ToSprite("rs_planet_" + stage, V(c, c));
        }

        private static Sprite DrawSky()
        {
            int w = 270, h = 600;
            var p = new IsoPaint(w, h);
            var top = (Color)new Color32(0x1A, 0x16, 0x3C, 255);
            var bottom = (Color)new Color32(0x3E, 0x2C, 0x74, 255);
            p.Poly(new[] { V(0, 0), V(w, 0), V(w, h), V(0, h) }, (x, y) =>
            {
                var col = Color.Lerp(bottom, top, y / h);
                float neb = Mathf.PerlinNoise(x / 60f + 3, y / 70f + 1);
                return Color.Lerp(col, new Color32(0x6C, 0x3C, 0x9C, 255), Mathf.SmoothStep(0.55f, 0.85f, neb) * 0.45f);
            });
            var rng = new System.Random(7);
            for (int i = 0; i < 90; i++)
            {
                float x = (float)rng.NextDouble() * w, y = (float)rng.NextDouble() * h, s = 0.6f + (float)rng.NextDouble() * 1.6f;
                p.Circle(x, y, s, new Color(1, 1, 1, 0.5f + (float)rng.NextDouble() * 0.5f));
            }
            return p.ToSprite("rs_sky", V(w / 2f, h / 2f));
        }

        /// <summary>A home icon: a coloured badge with a simple symbol (tabs, side columns, the feature row).</summary>
        private static Sprite DrawHubIcon(string id)
        {
            const int n = 256;
            var p = new IsoPaint(n, n);
            float c = n / 2f;
            Color col;
            switch (id)
            {
                case "shop": case "tab_shop": col = new Color32(0xE8, 0x5A, 0x6A, 255); break;
                case "offer": case "starter": col = new Color32(0xF6, 0xB4, 0x3C, 255); break;
                case "pass": col = new Color32(0x4C, 0xC8, 0xE8, 255); break;
                case "mail": col = new Color32(0xE8, 0xE2, 0xD6, 255); break;
                case "quests": col = new Color32(0xF0, 0xEC, 0xF8, 255); break;
                case "invasion": case "observatory": case "event": col = new Color32(0xE0, 0x4A, 0x4A, 255); break;
                case "map": col = new Color32(0x5C, 0xB8, 0xE8, 255); break;
                case "chapter_rewards": col = new Color32(0xF6, 0xCC, 0x40, 255); break;
                case "patrol": col = new Color32(0x4C, 0x9C, 0x5A, 255); break;
                case "challenges": col = new Color32(0xF0, 0x6C, 0x4C, 255); break;
                case "tab_worm": col = new Color32(0xF0, 0x8C, 0xB8, 255); break;
                case "tab_city": col = new Color32(0x6C, 0xC8, 0x5A, 255); break;
                case "tab_signals": col = new Color32(0x9C, 0x6C, 0xE8, 255); break;
                case "tab_arena": col = new Color32(0x9C, 0xA8, 0xC8, 255); break;
                case "tab_planet": col = new Color32(0x5C, 0x9C, 0xF0, 255); break;
                default: col = new Color32(0xA8, 0x9C, 0xD8, 255); break;
            }
            switch (id)
            {
                case "shop": case "tab_shop":
                    p.Shape(RoundRect(40, 40, 176, 120, 16), VGrad(Shade(col, -0.3f), Shade(col, 0.1f), 40, 160), Ink, 9);
                    for (int i = 0; i < 4; i++) p.Shape(new[] { V(30 + i * 49, 160), V(79 + i * 49, 160), V(72 + i * 49, 214), V(40 + i * 49, 214) }, i % 2 == 0 ? Shade(col, 0.2f) : (Color)new Color32(0xF8, 0xF0, 0xE8, 255), Ink, 7);
                    p.Shape(RoundRect(96, 40, 64, 80, 10), new Color32(0x4C, 0x8C, 0xE8, 255), Ink, 7);
                    break;
                case "mail":
                    p.Shape(RoundRect(30, 60, 196, 136, 14), VGrad(Shade(col, -0.2f), col, 60, 196), Ink, 9);
                    p.Stroke(new[] { V(34, 190), V(c, 110), V(222, 190) }, 9, Ink, false);
                    break;
                case "quests":
                    p.Shape(RoundRect(50, 26, 156, 200, 14), VGrad(Shade(col, -0.2f), col, 26, 226), Ink, 9);
                    for (int i = 0; i < 3; i++) p.Line(V(80, 170 - i * 44), V(176, 170 - i * 44), 9, new Color32(0x9C, 0x98, 0xB8, 255));
                    p.Stroke(new[] { V(120, 70), V(150, 40), V(214, 110) }, 22, Ink, false);
                    p.Stroke(new[] { V(120, 70), V(150, 40), V(214, 110) }, 12, new Color32(0x6C, 0xC8, 0x3C, 255), false);
                    break;
                case "pass":
                    p.Shape(new[] { V(40, 40), V(216, 40), V(216, 216), V(40, 216) }, VGrad(Shade(col, -0.3f), Shade(col, 0.2f), 40, 216), Ink, 9);
                    p.Shape(Star(c, c, 60, 5, 0.45f, 90), new Color32(0xFF, 0xE0, 0x5A, 255), Ink, 7);
                    break;
                case "map":
                    p.Blob(c, c, 96, 96, IsoPaint.Radial(Shade(col, 0.3f), Shade(col, -0.3f), c - 30, c + 30, 160), Ink, 9);
                    p.Blob(c - 30, c + 20, 40, 30, new Color32(0x7C, 0xC8, 0x5A, 255), new Color(0, 0, 0, 0), 0);
                    p.Shape(new[] { V(c, c + 70), V(c + 20, c), V(c, c - 70), V(c - 20, c) }, new Color32(0xE8, 0x4A, 0x4A, 255), Ink, 6);
                    break;
                case "chapter_rewards":
                    return Prop("chest_common");
                case "patrol":
                    p.Blob(c, 100, 90, 76, VGrad(Shade(col, -0.35f), Shade(col, 0.15f), 24, 176), Ink, 9);
                    p.Shape(RoundRect(c - 50, 160, 100, 46, 14), Shade(col, -0.2f), Ink, 8);
                    p.Blob(70, 70, 40, 40, new Color32(0xF6, 0xE6, 0xB8, 255), Ink, 7);
                    p.Line(V(70, 70), V(70, 96), 6, Ink); p.Line(V(70, 70), V(88, 70), 6, Ink);
                    break;
                case "challenges":
                    p.Shape(Star(c, 140, 96, 5, 0.48f, 90), VGrad(new Color32(0xF0, 0xB0, 0x30, 255), new Color32(0xFF, 0xE0, 0x6A, 255), 40, 236), Ink, 9);
                    p.Shape(RoundRect(40, 30, 176, 50, 16), col, Ink, 8);
                    break;
                case "event":
                    // a starburst with a pennant flag (H2: red stays for notification badges only)
                    p.Shape(Star(c, c, 110, 12, 0.72f, 90), VGrad(new Color32(0xF0, 0xA0, 0x30, 255), new Color32(0xFF, 0xE0, 0x6A, 255), 18, 238), Ink, 8);
                    p.Line(V(c - 30, 50), V(c - 30, 200), 12, Ink); p.Line(V(c - 30, 50), V(c - 30, 200), 6, new Color32(0xF8, 0xF0, 0xE8, 255));
                    p.Shape(new[] { V(c - 26, 200), V(c + 62, 176), V(c - 26, 140) }, new Color32(0x5C, 0x9C, 0xF0, 255), Ink, 7);
                    break;
                case "observatory":
                    // a telescope on a tripod
                    p.Line(V(c, 110), V(70, 30), 12, Ink); p.Line(V(c, 110), V(186, 30), 12, Ink); p.Line(V(c, 110), V(c, 30), 12, Ink);
                    p.Shape(new[] { V(60, 120), V(200, 196), V(216, 168), V(76, 92) }, VGrad(new Color32(0x4C, 0x5C, 0xA8, 255), new Color32(0x9C, 0xB0, 0xF0, 255), 92, 196), Ink, 8);
                    p.Shape(new[] { V(196, 204), V(232, 222), V(244, 176), V(210, 160) }, new Color32(0xF6, 0xCC, 0x40, 255), Ink, 7);
                    p.Shape(Star(70, 210, 22, 4, 0.4f, 90), Color.white, Ink, 4);
                    break;
                case "invasion":
                    // a flying saucer with a beam
                    p.Shape(new[] { V(c - 40, 110), V(c + 40, 110), V(c + 80, 20), V(c - 80, 20) }, new Color(0.6f, 1f, 0.6f, 0.45f), new Color(0, 0, 0, 0), 0);
                    p.Blob(c, 150, 104, 34, VGrad(new Color32(0x8C, 0x94, 0xA8, 255), new Color32(0xD8, 0xDE, 0xE8, 255), 116, 184), Ink, 8);
                    p.Blob(c, 182, 50, 40, IsoPaint.Radial(new Color32(0xC8, 0xF8, 0xFF, 255), new Color32(0x5C, 0xB8, 0xE8, 255), c - 14, 196, 70), Ink, 7);
                    for (int i = -1; i <= 1; i++) p.Circle(c + i * 52, 148, 9, new Color32(0xFF, 0xE0, 0x5A, 255));
                    break;
                case "tab_worm":
                    // the worm's head: a round pink face with big eyes and antennae (H1)
                    p.Line(V(c - 30, 196), V(c - 58, 236), 10, Ink); p.Line(V(c + 30, 196), V(c + 58, 236), 10, Ink);
                    p.Circle(c - 58, 236, 14, col); p.Circle(c + 58, 236, 14, col);
                    p.Blob(c, 122, 96, 90, IsoPaint.Radial(Shade(col, 0.35f), Shade(col, -0.3f), c - 30, 150, 170), Ink, 9);
                    p.Blob(c - 34, 140, 26, 30, Color.white, Ink, 6); p.Blob(c + 34, 140, 26, 30, Color.white, Ink, 6);
                    p.Circle(c - 28, 136, 12, Ink); p.Circle(c + 40, 136, 12, Ink);
                    p.Stroke(new[] { V(c - 30, 84), V(c, 68), V(c + 30, 84) }, 8, Ink, false);
                    break;
                case "tab_city":
                    // three little buildings with roofs and lit windows (H1)
                    p.Shape(RoundRect(26, 24, 70, 110, 8), VGrad(Shade(col, -0.3f), col, 24, 134), Ink, 8);
                    p.Shape(new[] { V(18, 130), V(104, 130), V(61, 180) }, new Color32(0xE8, 0x5A, 0x4A, 255), Ink, 7);
                    p.Shape(RoundRect(160, 24, 70, 96, 8), VGrad(Shade(col, -0.3f), col, 24, 120), Ink, 8);
                    p.Shape(new[] { V(152, 116), V(238, 116), V(195, 160) }, new Color32(0xE8, 0x5A, 0x4A, 255), Ink, 7);
                    p.Shape(RoundRect(92, 24, 72, 160, 8), VGrad(Shade(col, -0.2f), Shade(col, 0.25f), 24, 184), Ink, 8);
                    p.Shape(new[] { V(84, 180), V(172, 180), V(128, 236) }, new Color32(0x5C, 0x9C, 0xF0, 255), Ink, 7);
                    for (int i = 0; i < 2; i++) p.Shape(RoundRect(112, 70 + i * 50, 32, 26, 5), new Color32(0xFF, 0xE0, 0x6A, 255), Ink, 4);
                    p.Shape(RoundRect(46, 60, 30, 26, 5), new Color32(0xFF, 0xE0, 0x6A, 255), Ink, 4);
                    p.Shape(RoundRect(180, 54, 30, 26, 5), new Color32(0xFF, 0xE0, 0x6A, 255), Ink, 4);
                    break;
                case "tab_signals":
                {
                    // a satellite dish on a stand with signal waves (H1)
                    p.Shape(new[] { V(c - 40, 20), V(c + 40, 20), V(c + 14, 90), V(c - 14, 90) }, Shade(col, -0.25f), Ink, 8);
                    var dish = new List<Vector2>();
                    for (int k = 0; k <= 16; k++) { float a = Mathf.Lerp(200, 380, k / 16f) * Mathf.Deg2Rad; dish.Add(V(c - 10 + Mathf.Cos(a) * 92, 150 + Mathf.Sin(a) * 92)); }
                    dish.Reverse();
                    p.Shape(dish, VGrad(Shade(col, -0.2f), Shade(col, 0.35f), 60, 160), Ink, 9);
                    p.Line(V(c - 10, 120), V(c + 50, 190), 8, Ink);
                    p.Circle(c + 54, 194, 14, new Color32(0xFF, 0xE0, 0x6A, 255));
                    for (int i = 1; i <= 2; i++) { var arc = new List<Vector2>(); for (int k = 0; k <= 8; k++) { float a = Mathf.Lerp(0, 90, k / 8f) * Mathf.Deg2Rad; arc.Add(V(c + 54 + Mathf.Cos(a) * (24 + i * 22), 194 + Mathf.Sin(a) * (24 + i * 22))); } p.Stroke(arc, 8, Ink, false); }
                    break;
                }
                case "tab_arena":
                    // crossed swords (H1: the plain X read as a close button)
                    foreach (int sd in new[] { -1, 1 })
                    {
                        var a = V(c - sd * 90, 40); var b = V(c + sd * 60, 190);
                        var dir = (b - a).normalized; var perp = new Vector2(-dir.y, dir.x);
                        p.Line(a, b, 26, Ink); p.Line(a, b, 14, new Color32(0xE8, 0xEC, 0xF4, 255));
                        var g = b - dir * 6;
                        p.Line(g - perp * 36, g + perp * 36, 22, Ink); p.Line(g - perp * 32, g + perp * 32, 12, new Color32(0xF6, 0xCC, 0x40, 255));
                        p.Line(b, b + dir * 44, 22, Ink); p.Line(b, b + dir * 44, 12, new Color32(0x8C, 0x5C, 0x3C, 255));
                    }
                    break;
                case "tab_planet":
                    p.Ellipse(c, c, 120, 34, new Color(1, 1, 1, 0.5f));
                    p.Blob(c, c, 80, 80, IsoPaint.Radial(Shade(col, 0.35f), Shade(col, -0.3f), c - 26, c + 26, 140), Ink, 9);
                    p.Blob(c - 20, c + 20, 30, 22, new Color32(0x7C, 0xC8, 0x5A, 255), new Color(0, 0, 0, 0), 0);
                    break;
                case "more":
                    for (int i = 0; i < 3; i++) p.Shape(RoundRect(40, 50 + i * 60, 176, 34, 14), new Color32(0xE8, 0xE4, 0xF4, 255), Ink, 7);
                    break;
                case "settings":
                    return HudArt.Icon("cog");
                default:
                    p.Shape(RoundRect(36, 36, 184, 184, 40), VGrad(Shade(col, -0.3f), Shade(col, 0.2f), 36, 220), Ink, 9);
                    p.Shape(Star(c, c, 56, 4, 0.4f, 90), Color.white, Ink, 6);
                    break;
            }
            return p.ToSprite("rs_hub_" + id, V(c, c));
        }
    }
}
