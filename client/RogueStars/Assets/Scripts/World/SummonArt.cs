using System.Collections.Generic;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using UnityEngine;
using static RogueStars.Client.World.IsoPaint;

namespace RogueStars.Client.World
{
    /// <summary>
    /// Art of the summon events (docs/design/summon-events.md). Every key is looked up through <see cref="ArtCatalog"/> first
    /// and falls back to a code-drawn placeholder:
    /// <list type="bullet">
    /// <item><c>Art/Summon/backdrop_hero</c>, <c>Art/Summon/backdrop_mount</c> — the event hub's stage backdrop (full frame, portrait)</item>
    /// <item><c>Art/Summon/Heroes/&lt;hero id&gt;</c> — featured hero, big (else the hero worm sprite)</item>
    /// <item><c>Art/Summon/Mounts/&lt;mount id&gt;</c> — featured mount, big (else the battle mount art / the drawn saucer)</item>
    /// <item><c>Art/Summon/Icons/&lt;id&gt;</c> — <see cref="IconIds"/>: tickets, exchange tokens, the Luck gem, the side
    /// column (exchange, packs, pass, tasks) and reward icons</item>
    /// </list>
    /// </summary>
    public static class SummonArt
    {
        public const string Root = "Art/Summon";
        public const string IconFolder = Root + "/Icons";

        public static readonly string[] IconIds =
        {
            "hero_ticket", "mount_ticket", "hero_sigil", "mount_bolt", "luck_gem", "diamond", "exchange", "packs", "pass", "tasks",
            "shards", "hero_xp", "fuel", "token", "grain", "ore", "lock", "check", "ad", "star", "spotlight",
        };

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static void ClearCache() => Cache.Clear();

        /// <summary>Icon by id (ArtCatalog <c>Art/Summon/Icons/&lt;id&gt;</c>, else drawn).</summary>
        public static Sprite Icon(string id)
        {
            var art = ArtCatalog.Load(IconFolder + "/" + id);
            if (art != null) return art;
            if (Cache.TryGetValue("icon:" + id, out var s) && s != null) return s;
            s = Draw(id);
            Cache["icon:" + id] = s;
            return s;
        }

        /// <summary>Icon of a currency (tickets, tokens, Diamonds, Fuel...).</summary>
        public static string IconOfCurrency(string currency)
        {
            if (currency == null) return "token";
            if (currency == Currencies.HeroTickets) return "hero_ticket";
            if (currency == Currencies.MountTickets) return "mount_ticket";
            if (currency == Currencies.HeroSigils) return "hero_sigil";
            if (currency == Currencies.MountBolts) return "mount_bolt";
            if (currency == Currencies.Diamonds) return "diamond";
            if (currency == Currencies.HeroXp) return "hero_xp";
            if (currency == Currencies.Fuel) return "fuel";
            if (currency == Currencies.Grain) return "grain";
            if (currency == Currencies.Ore) return "ore";
            if (currency.StartsWith("shards_")) return "shards";
            return "token";
        }

        /// <summary>Icon of a reward (copies show the featured art, see <see cref="Featured"/>).</summary>
        public static Sprite RewardIcon(Reward r)
        {
            if (r == null) return Icon("token");
            if (r.Kind == RewardKinds.Hero) return Featured(SummonEventType.Hero, r.Id);
            if (r.Kind == RewardKinds.Mount) return Featured(SummonEventType.Mount, r.Id);
            if (r.Kind == RewardKinds.HeroShards) return Icon("shards");
            return Icon(IconOfCurrency(r.Kind == RewardKinds.Currency ? r.Id : null));
        }

        /// <summary>The featured hero / mount, big.</summary>
        public static Sprite Featured(SummonEventType type, string id)
        {
            if (type == SummonEventType.Hero)
                return ArtCatalog.Load(Root + "/Heroes/" + id) ?? ArtCatalog.Hero() ?? DrawnWorm();
            return ArtCatalog.Load(Root + "/Mounts/" + id) ?? ArtCatalog.SliceMount(id) ?? HudArt.Mount();
        }

        /// <summary>The hub's stage backdrop (purple for heroes, teal for mounts).</summary>
        public static Sprite Backdrop(SummonEventType type)
        {
            string key = type == SummonEventType.Hero ? "backdrop_hero" : "backdrop_mount";
            var art = ArtCatalog.Load(Root + "/" + key);
            if (art != null) return art;
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            s = DrawBackdrop(type == SummonEventType.Hero ? new Color32(0x9A, 0x5C, 0xF0, 255) : new Color32(0x3C, 0xC8, 0xD8, 255), key);
            Cache[key] = s;
            return s;
        }

        /// <summary>Accent colour of an event type (stage, buttons).</summary>
        public static Color Accent(SummonEventType type) => type == SummonEventType.Hero ? Theme.Hex(0xA77BFF) : Theme.Hex(0x4FD6E0);

        // ---- drawing -------------------------------------------------------------------------------------------------

        private static readonly Color Ink = IsoArt.Ink;

        private static Sprite DrawBackdrop(Color tint, string key)
        {
            const int w = 540, h = 960;
            var p = new IsoPaint(w, h);
            var deep = Shade(tint, -0.78f);
            p.Poly(new[] { V(0, 0), V(w, 0), V(w, h), V(0, h) }, VGrad(Shade(tint, -0.55f), deep, 0, h));
            // a soft glow where the featured one stands
            p.Ellipse(w / 2f, h * 0.42f, 300, 380, (x, y) =>
            {
                float d = Mathf.Sqrt((x - w / 2f) * (x - w / 2f) / (300f * 300f) + (y - h * 0.42f) * (y - h * 0.42f) / (380f * 380f));
                var c = Shade(tint, 0.1f); c.a = 0.45f * Mathf.Clamp01(1 - d); return c;
            });
            // swirling mist bands at the bottom
            for (int i = 0; i < 4; i++)
            {
                float y = 60 + i * 70;
                var band = new List<Vector2>();
                for (int k = 0; k <= 24; k++) { float x = k / 24f * w; band.Add(V(x, y + Mathf.Sin(k * 0.7f + i) * 26)); }
                for (int k = 24; k >= 0; k--) { float x = k / 24f * w; band.Add(V(x, y - 40 + Mathf.Sin(k * 0.6f + i * 2) * 18)); }
                var c = Shade(tint, -0.15f - i * 0.12f); c.a = 0.55f;
                p.Poly(band, c);
            }
            // sparkles
            var rng = new System.Random(7);
            for (int i = 0; i < 26; i++)
            {
                float x = (float)rng.NextDouble() * w, y = 300 + (float)rng.NextDouble() * (h - 320), r = 2 + (float)rng.NextDouble() * 4;
                p.Poly(Star(x, y, r * 2.4f, 4, 0.3f), new Color(1, 1, 1, 0.6f));
            }
            return p.ToSprite("rs_summon_" + key, V(w / 2f, h / 2f));
        }

        private static Sprite DrawnWorm()
        {
            if (Cache.TryGetValue("worm", out var s) && s != null) return s;
            const int n = 256;
            var p = new IsoPaint(n, n);
            var pink = Theme.Worm;
            for (int i = 0; i < 5; i++) p.Blob(80 + i * 22, 70 + i * 26, 46 - i * 2, 40 - i * 2, IsoPaint.Radial(Shade(pink, 0.35f), Shade(pink, -0.2f), 70 + i * 22, 84 + i * 26, 60), Ink, 6);
            p.Circle(178, 182, 9, Ink); p.Circle(200, 176, 9, Ink);
            s = p.ToSprite("rs_summon_worm", V(n / 2f, n / 2f));
            Cache["worm"] = s;
            return s;
        }

        private static Sprite Draw(string id)
        {
            const int n = 128;
            var p = new IsoPaint(n, n);
            float c = n / 2f;
            switch (id)
            {
                case "hero_ticket":
                case "mount_ticket":
                {
                    var face = id == "hero_ticket" ? (Color)new Color32(0x9A, 0x5C, 0xF0, 255) : (Color)new Color32(0x3C, 0xC8, 0xD8, 255);
                    var pts = new List<Vector2> { V(14, 34), V(114, 34), V(114, 54), V(106, 64), V(114, 74), V(114, 94), V(14, 94), V(14, 74), V(22, 64), V(14, 54) };
                    var rot = new List<Vector2>();
                    foreach (var q in pts) { var d = q - V(c, c); float a = 14 * Mathf.Deg2Rad; rot.Add(V(c + d.x * Mathf.Cos(a) - d.y * Mathf.Sin(a), c + d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a))); }
                    p.Shape(rot, VGrad(Shade(face, -0.25f), Shade(face, 0.25f), 30, 100), Ink, 6);
                    if (id == "hero_ticket") p.Shape(Star(c, c, 24, 5, 0.45f), new Color32(0xFF, 0xE0, 0x6A, 255), Ink, 4);
                    else { p.Blob(c, c - 2, 26, 10, new Color32(0xE8, 0xF4, 0xFF, 255), Ink, 4); p.Blob(c, c + 6, 12, 9, new Color32(0x9C, 0xF0, 0xFF, 255), Ink, 4); }
                    break;
                }
                case "hero_sigil":
                {
                    var gold = (Color)new Color32(0xFF, 0xC8, 0x48, 255);
                    p.Shape(Hex(c, c, 52), IsoPaint.Radial(Shade(gold, 0.35f), Shade(gold, -0.3f), c - 16, c + 16, 80), Ink, 7);
                    p.Shape(Star(c, c, 26, 5, 0.45f), new Color32(0x8A, 0x4C, 0xE0, 255), Ink, 4);
                    break;
                }
                case "mount_bolt":
                {
                    var steel = (Color)new Color32(0xB8, 0xC4, 0xE0, 255);
                    p.Shape(Hex(c, c, 50), VGrad(Shade(steel, -0.35f), Shade(steel, 0.3f), 14, 114), Ink, 7);
                    p.Blob(c, c, 20, 20, new Color32(0x2C, 0x34, 0x58, 255), Ink, 5);
                    break;
                }
                case "luck_gem":
                case "diamond":
                {
                    var col = id == "luck_gem" ? (Color)new Color32(0xF2, 0x8B, 0xE6, 255) : (Color)new Color32(0x7F, 0xE3, 0xFF, 255);
                    var gem = new[] { V(c, 120), V(112, c + 6), V(c, 8), V(16, c + 6) };
                    p.Shape(gem, IsoPaint.Radial(Shade(col, 0.6f), Shade(col, -0.25f), c - 10, c + 20, 70), Ink, 6);
                    p.Poly(new[] { V(c, 120), V(c, 8), V(16, c + 6) }, new Color(1, 1, 1, 0.18f));
                    p.Line(V(16, c + 6), V(112, c + 6), 3, new Color(1, 1, 1, 0.5f));
                    break;
                }
                case "exchange":
                {
                    var page = (Color)new Color32(0xFF, 0xF1, 0xD6, 255);
                    p.Shape(new[] { V(10, 30), V(c, 22), V(c, 92), V(10, 100) }, page, Ink, 6);
                    p.Shape(new[] { V(118, 30), V(c, 22), V(c, 92), V(118, 100) }, Shade(page, -0.1f), Ink, 6);
                    for (int i = 0; i < 3; i++) { p.Line(V(22, 76 - i * 14), V(c - 10, 72 - i * 14), 3, Shade(page, -0.45f)); p.Line(V(c + 10, 72 - i * 14), V(106, 76 - i * 14), 3, Shade(page, -0.45f)); }
                    var gem = new[] { V(c, 126), V(c + 18, 108), V(c, 86), V(c - 18, 108) };
                    p.Shape(gem, new Color32(0xC9, 0x9C, 0xFF, 255), Ink, 4);
                    break;
                }
                case "packs":
                {
                    var bag = (Color)new Color32(0xD8, 0xA4, 0x62, 255);
                    p.Blob(c, 50, 46, 40, IsoPaint.Radial(Shade(bag, 0.3f), Shade(bag, -0.25f), c - 12, 62, 60), Ink, 6);
                    p.Shape(new[] { V(c - 22, 86), V(c + 22, 86), V(c + 30, 110), V(c - 30, 110) }, Shade(bag, -0.1f), Ink, 6);
                    p.Line(V(c - 26, 88), V(c + 26, 88), 9, new Color32(0xE0, 0x48, 0x3C, 255));
                    break;
                }
                case "pass":
                {
                    var wood = (Color)new Color32(0xC8, 0x82, 0x3C, 255);
                    var glass = (Color)new Color32(0x9C, 0xC8, 0xFF, 255);
                    p.Shape(new[] { V(30, 24), V(98, 24), V(c + 6, c), V(98, 104), V(30, 104), V(c - 6, c) }, VGrad(Shade(glass, -0.2f), Shade(glass, 0.3f), 24, 104), Ink, 5);
                    p.Poly(new[] { V(38, 30), V(90, 30), V(c, 52) }, new Color32(0x9A, 0x5C, 0xF0, 255));
                    p.Shape(IsoPaint.RoundRect(20, 10, 88, 16, 6), wood, Ink, 5);
                    p.Shape(IsoPaint.RoundRect(20, 102, 88, 16, 6), wood, Ink, 5);
                    break;
                }
                case "tasks":
                {
                    var paper = (Color)new Color32(0xF4, 0xF0, 0xE4, 255);
                    p.Shape(IsoPaint.RoundRect(22, 8, 84, 108, 10), (Color)new Color32(0xB8, 0x7A, 0x44, 255), Ink, 6);
                    p.Shape(IsoPaint.RoundRect(30, 16, 68, 88, 6), paper, Ink, 4);
                    for (int i = 0; i < 3; i++) p.Line(V(44, 82 - i * 20), V(86, 82 - i * 20), 5, Shade(paper, -0.4f));
                    p.Line(V(44, 34), V(56, 22), 9, Ink); p.Line(V(56, 22), V(90, 54), 9, Ink);
                    p.Line(V(44, 34), V(56, 22), 5, new Color32(0x3F, 0xD0, 0x7A, 255)); p.Line(V(56, 22), V(90, 54), 5, new Color32(0x3F, 0xD0, 0x7A, 255));
                    p.Shape(IsoPaint.RoundRect(46, 108, 36, 14, 5), new Color32(0x9C, 0xA4, 0xC8, 255), Ink, 4);
                    break;
                }
                case "shards":
                {
                    var col = (Color)new Color32(0xFF, 0xB3, 0x47, 255);
                    p.Shape(new[] { V(c - 10, 118), V(c + 30, 74), V(c + 6, 10), V(c - 34, 54) }, VGrad(Shade(col, -0.3f), Shade(col, 0.35f), 10, 118), Ink, 6);
                    p.Line(V(c - 10, 118), V(c + 6, 10), 3, new Color(1, 1, 1, 0.45f));
                    break;
                }
                case "hero_xp":
                {
                    var green = (Color)new Color32(0x7B, 0xE0, 0x8A, 255);
                    p.Blob(c, 50, 40, 40, IsoPaint.Radial(Shade(green, 0.4f), Shade(green, -0.3f), c - 12, 62, 50), Ink, 6);
                    p.Shape(IsoPaint.RoundRect(c - 12, 82, 24, 28, 4), new Color32(0xD8, 0xE8, 0xF0, 255), Ink, 5);
                    p.Shape(Star(c, 50, 18, 5, 0.45f), new Color(1, 1, 1, 0.85f), Ink, 3);
                    break;
                }
                case "fuel":
                {
                    var red = (Color)new Color32(0xF0, 0x7A, 0x2C, 255);
                    p.Shape(IsoPaint.RoundRect(26, 10, 76, 96, 12), VGrad(Shade(red, -0.3f), Shade(red, 0.25f), 10, 106), Ink, 6);
                    p.Shape(IsoPaint.RoundRect(72, 100, 22, 18, 4), new Color32(0x6C, 0x64, 0x80, 255), Ink, 5);
                    p.Line(V(40, 30), V(88, 86), 8, new Color(1, 1, 1, 0.35f));
                    break;
                }
                case "grain":
                {
                    var wheat = (Color)new Color32(0xF0, 0xC8, 0x5A, 255);
                    p.Line(V(c, 10), V(c, 90), 8, Ink); p.Line(V(c, 10), V(c, 90), 4, Shade(wheat, -0.3f));
                    for (int i = 0; i < 4; i++) { p.Blob(c - 14, 50 + i * 18, 12, 8, wheat, Ink, 3); p.Blob(c + 14, 50 + i * 18, 12, 8, wheat, Ink, 3); }
                    break;
                }
                case "ore":
                {
                    var rock = (Color)new Color32(0x9C, 0x8A, 0x7C, 255);
                    p.Shape(new[] { V(14, 30), V(40, 100), V(90, 112), V(116, 60), V(84, 18) }, VGrad(Shade(rock, -0.3f), Shade(rock, 0.25f), 18, 112), Ink, 6);
                    p.Blob(60, 66, 12, 10, new Color32(0xFF, 0xC8, 0x48, 255), Ink, 3);
                    break;
                }
                case "token":
                {
                    var silver = (Color)new Color32(0x9C, 0xC8, 0xF0, 255);
                    p.Blob(c, c, 52, 52, IsoPaint.Radial(Shade(silver, 0.35f), Shade(silver, -0.25f), c - 18, c + 18, 80), Ink, 7);
                    p.Shape(Star(c, c, 22, 4, 0.4f), new Color(1, 1, 1, 0.85f), Ink, 3);
                    break;
                }
                case "lock":
                {
                    var gold = (Color)new Color32(0xFF, 0xC3, 0x5C, 255);
                    var arc = new List<Vector2>();
                    for (int i = 0; i <= 12; i++) { float a = Mathf.Lerp(0, 180, i / 12f) * Mathf.Deg2Rad; arc.Add(V(c + Mathf.Cos(a) * 26, 70 + Mathf.Sin(a) * 30)); }
                    p.Stroke(arc, 18, Ink, false); p.Stroke(arc, 10, Shade(gold, -0.2f), false);
                    p.Shape(IsoPaint.RoundRect(24, 12, 80, 64, 12), gold, Ink, 6);
                    p.Circle(c, 46, 8, Ink);
                    break;
                }
                case "check":
                    p.Line(V(20, 64), V(50, 30), 26, Ink); p.Line(V(50, 30), V(110, 100), 26, Ink);
                    p.Line(V(20, 64), V(50, 30), 15, new Color32(0x7B, 0xE0, 0x6A, 255)); p.Line(V(50, 30), V(110, 100), 15, new Color32(0x7B, 0xE0, 0x6A, 255));
                    break;
                case "ad":
                    p.Shape(IsoPaint.RoundRect(14, 22, 100, 84, 16), new Color32(0x9A, 0x5C, 0xF0, 255), Ink, 6);
                    p.Shape(new[] { V(50, 42), V(50, 86), V(88, 64) }, Color.white, Ink, 4);
                    break;
                case "star":
                    p.Shape(Star(c, c, 56, 5, 0.45f), IsoPaint.Radial(new Color32(0xFF, 0xF0, 0x9C, 255), new Color32(0xFF, 0xB0, 0x2C, 255), c, c + 10, 60), Ink, 6);
                    break;
                case "spotlight":
                {
                    var q = new IsoPaint(256, 512);
                    q.Poly(new[] { V(96, 512), V(160, 512), V(256, 0), V(0, 0) }, (x, y) =>
                    {
                        float edge = 1 - Mathf.Abs(x - 128) / (128f * (0.3f + 0.7f * (1 - y / 512f)) + 1);
                        return new Color(1, 0.95f, 1, 0.32f * Mathf.Clamp01(edge) * (0.35f + 0.65f * y / 512f));
                    });
                    return q.ToSprite("rs_summon_spotlight", V(128, 256));
                }
                default:
                    p.Blob(c, c, 50, 50, Color.gray, Ink, 6);
                    break;
            }
            return p.ToSprite("rs_summon_" + id, V(c, c));
        }

        private static List<Vector2> Hex(float cx, float cy, float r)
        {
            var l = new List<Vector2>();
            for (int i = 0; i < 6; i++) { float a = (30 + 60 * i) * Mathf.Deg2Rad; l.Add(V(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r)); }
            return l;
        }
    }
}
