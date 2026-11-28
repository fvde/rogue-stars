using System.Collections.Generic;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using UnityEngine;

namespace RogueStars.Client.World
{
    /// <summary>
    /// Art of the parts v2 items (docs/art/parts-v2-icons.md): one icon per part, keyed by the part's game id
    /// (<c>c_head_guard</c> … <c>l_arm_fury</c>, <c>myth_*</c>; a Mythic's awakened form uses its id too), under
    /// <c>Resources/Art/Parts/&lt;id&gt;</c>. Until an icon exists the tile shows the slot glyph (<see cref="SlotGlyph"/>) in the
    /// role / synergy colour. The slot glyphs are the kit's <c>slot_glyph_&lt;slot&gt;</c> sprites, else drawn here (white
    /// silhouettes with a navy outline, tinted by the caller).
    /// </summary>
    public static class PartArt
    {
        public const string Folder = "Art/Parts";

        /// <summary>The icon key of a part def: its game id (one icon per part).</summary>
        public static string IconKey(PartDef def) => def?.Id;

        /// <summary>Lower-case slot word of a kind (head, body, tail, legs, arm).</summary>
        public static string SlotKey(PartKind k) => k.ToString().ToLowerInvariant();

        /// <summary>The item icon of a part def, or null while no art exists (callers draw the glyph fallback).</summary>
        public static Sprite Icon(PartDef def)
        {
            var key = IconKey(def);
            return string.IsNullOrEmpty(key) ? null : ArtCatalog.Load(Folder + "/" + key);
        }

        /// <summary>Colour of the fallback glyph: the role colour (docs/art/parts-v2-icons.md) or a Mythic's synergy colour.</summary>
        public static Color GlyphColor(PartDef def)
        {
            if (def == null) return Color.white;
            if (def.IsMythic)
            {
                switch ((def.Synergy ?? "").ToLowerInvariant())
                {
                    case "fire": return Theme.Hex(0xFFB066);
                    case "ice": return Theme.Hex(0xBFEFFF);
                    case "lightning": return Theme.Hex(0xFFF07A);
                    case "radiation": return Theme.Hex(0xC8FF6A);
                    case "bleed": return Theme.Hex(0xFF9A9A);
                    case "combo": return Theme.Hex(0xE0B8FF);
                    case "counter": return Theme.Hex(0xE6E6EE);
                    case "alien": return Theme.Hex(0x9AF5E6);
                    case "time": return Theme.Hex(0xFFB3F0);
                    default: return Theme.Hex(0xFFE7A0);
                }
            }
            switch (def.Role)
            {
                case PartRole.Guard: return Theme.Hex(0xA9C8F0);
                case PartRole.Strike: return Theme.Hex(0xFFB08A);
                case PartRole.Swift: return Theme.Hex(0x9AF0E0);
                case PartRole.Keen: return Theme.Hex(0xFFE98A);
                case PartRole.Mend: return Theme.Hex(0xC4F5B0);
                case PartRole.Fury: return Theme.Hex(0xE7A6E0);
                default: return Color.white;
            }
        }

        /// <summary>Short rank of a part rarity (0–10): C, R, E, L, M, M+1, M+2, S, S+1, S+2, SS.</summary>
        public static string Rank(int partRarity)
        {
            switch (partRarity)
            {
                case 0: return "C";
                case 1: return "R";
                case 2: return "E";
                case 3: return "L";
                case 4: return "M";
                case 5: return "M+1";
                case 6: return "M+2";
                case 7: return "S";
                case 8: return "S+1";
                case 9: return "S+2";
                default: return partRarity >= 10 ? "SS" : "C";
            }
        }

        /// <summary>The rank as tile text: the letter big, a "+n" step smaller ("M<size=..>+1</size>").</summary>
        public static string RankRich(int partRarity, int bigSize)
        {
            var r = Rank(partRarity);
            int plus = r.IndexOf('+');
            return plus < 0 ? r : r.Substring(0, plus) + "<size=" + Mathf.RoundToInt(bigSize * 0.55f) + ">" + r.Substring(plus) + "</size>";
        }

        private static readonly Dictionary<PartKind, Sprite> Glyphs = new Dictionary<PartKind, Sprite>();

        /// <summary>The white slot glyph (kit <c>slot_glyph_&lt;slot&gt;</c>, else drawn).</summary>
        public static Sprite SlotGlyph(PartKind kind)
        {
            var kit = KitArt.Get("slot_glyph_" + SlotKey(kind));
            if (kit != null) return kit;
            if (Glyphs.TryGetValue(kind, out var s) && s != null) return s;
            s = DrawGlyph(kind);
            Glyphs[kind] = s;
            return s;
        }

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        /// <summary>A 128 px white silhouette of the slot with a navy outline (y up).</summary>
        private static Sprite DrawGlyph(PartKind kind)
        {
            const int n = 128;
            var p = new IsoPaint(n, n);
            var ink = KitV2.Ink;
            var w = Color.white;
            var shade = new Color(0.82f, 0.84f, 0.9f, 1);
            switch (kind)
            {
                case PartKind.Head:
                {
                    // a helm: dome, brim and a visor slit
                    var dome = new List<Vector2>();
                    for (int i = 0; i <= 16; i++) { float a = Mathf.PI * i / 16f; dome.Add(V(64 + Mathf.Cos(a) * 46, 52 + Mathf.Sin(a) * 52)); }
                    dome.Add(V(18, 30)); dome.Add(V(110, 30));
                    dome.Reverse();
                    p.Shape(dome, w, ink, 7);
                    p.Shape(IsoPaint.RoundRect(30, 46, 68, 18, 8), ink, ink, 2);
                    p.Line(V(64, 100), V(64, 116), 9, ink);
                    break;
                }
                case PartKind.Body:
                {
                    // a chest plate with shoulder pads
                    p.Shape(new[] { V(30, 104), V(98, 104), V(110, 78), V(96, 70), V(92, 30), V(64, 12), V(36, 30), V(32, 70), V(18, 78) }, w, ink, 7);
                    p.Line(V(64, 88), V(64, 24), 6, shade);
                    p.Shape(IsoPaint.RoundRect(46, 56, 36, 22, 8), shade, ink, 4);
                    break;
                }
                case PartKind.Tail:
                {
                    // a segmented tail curling up to a spike
                    var pts = new[] { V(24, 30), V(48, 22), V(72, 28), V(92, 46), V(100, 70) };
                    float[] r = { 17, 16, 15, 13, 11 };
                    for (int i = 0; i < pts.Length; i++) p.Blob(pts[i].x, pts[i].y, r[i], r[i], i % 2 == 0 ? w : shade, ink, 6);
                    p.Shape(new[] { V(92, 78), V(110, 78), V(106, 116) }, w, ink, 6);
                    break;
                }
                case PartKind.Legs:
                {
                    // a pair of boots
                    for (int i = 0; i < 2; i++)
                    {
                        float x = i == 0 ? 18 : 68;
                        p.Shape(IsoPaint.RoundRect(x + 8, 40, 26, 66, 8), w, ink, 6);
                        p.Shape(IsoPaint.RoundRect(x, 16, 44, 30, 10), shade, ink, 6);
                    }
                    break;
                }
                default:
                {
                    // a weapon arm: socket, forearm and a fist
                    p.Shape(IsoPaint.RoundRect(14, 16, 34, 40, 10), shade, ink, 6);
                    p.Shape(new[] { V(36, 34), V(54, 22), V(96, 76), V(78, 90) }, w, ink, 7);
                    p.Blob(94, 92, 22, 22, w, ink, 7);
                    p.Line(V(84, 100), V(104, 82), 5, shade);
                    break;
                }
            }
            return p.ToSprite("rs_slot_glyph_" + SlotKey(kind), V(n / 2f, n / 2f));
        }
    }
}
