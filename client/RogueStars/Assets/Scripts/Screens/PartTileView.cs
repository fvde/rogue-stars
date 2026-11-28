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
    /// The rarity tile of a part (board "New · worm"): rarity tile (<c>tile_&lt;rarity&gt;</c>), item icon (or the slot glyph
    /// in the role colour), slot badge top-left, "Lv.N" top-right, the big rank letter (C R E L M M+1 … S SS) bottom-left,
    /// the gold copy-count diamond bottom-right, the red "!" top-right and the rune pip strip under it. Board px: a tile is
    /// <c>size</c> square plus the strip (46 px), placed by the caller.
    /// </summary>
    public static class PartTileView
    {
        public const float GridTile = 181, SlotTile = 171, StripHeight = 46;

        /// <summary>Everything a tile shows.</summary>
        public sealed class Data
        {
            public PartDef Def;
            public PartKind Kind;
            public int Rarity, Level, Copies;
            public bool Alert, Locked, Equipped, Selected, Empty, Pips = true, Awakened, LevelUp;
            public List<Color?> Gems = new List<Color?>();
            public int OpenSockets;
            public string Caption;
        }

        public static Data From(GameState s, ContentDb c, PartItem p, ISet<string> locks, bool alerts = true)
        {
            var def = PartsSystem.Def(c, p);
            var d = new Data
            {
                Def = def, Kind = def.Kind, Rarity = p.Rarity, Level = p.Level, Copies = GearGuide.Copies(s, p),
                Alert = alerts && GearGuide.Alert(s, c, p, locks) != PartAlert.None,
                Locked = p.Locked || locks != null && locks.Contains(p.Uid), Equipped = PartsSystem.IsEquipped(s, p.Uid),
                OpenSockets = PartsSystem.RuneSlotCount(c, p), Awakened = p.Awaken > 0,
            };
            for (int i = 0; i < d.OpenSockets; i++)
            {
                string ru = i < p.Runes.Count ? p.Runes[i] : null;
                if (!string.IsNullOrEmpty(ru) && s.Parts.Runes.TryGetValue(ru, out var rune)) d.Gems.Add(GemColor(rune.Rarity));
                else d.Gems.Add(null);
            }
            return d;
        }

        /// <summary>Key of everything <see cref="From"/> reads (in-place parts).</summary>
        public static string Key(Data d) => string.Join("|", d.Def?.Id, d.Rarity, d.Level, d.Copies, d.Alert, d.Locked, d.Equipped, d.Selected,
            d.OpenSockets, string.Join(",", d.Gems), d.Awakened, d.Pips, d.Caption, d.LevelUp);

        /// <summary>Pip gem colour of a socketed rune by its rarity (blue, purple, gold).</summary>
        public static Color GemColor(int runeRarity) => runeRarity >= 3 ? Theme.Hex(0xFFC23D) : runeRarity >= 2 ? Theme.Hex(0xC084FC) : Theme.Hex(0x5FD3FF);

        /// <summary>
        /// Build a tile at (0, 0) of a fresh container under <paramref name="parent"/> (board px). Tap → <paramref name="onClick"/>.
        /// Returns the container (size × size + strip), named <paramref name="name"/>.
        /// </summary>
        public static RectTransform Build(Transform parent, Data d, float size, Action onClick, string name)
        {
            float k = size / GridTile;
            var root = Ui.Rect(parent, name);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0, 1);
            root.sizeDelta = new Vector2(size, size + (d.Pips ? StripHeight : 0));
            if (d.Selected)
            {
                var glow = KitV2.Box(root, "tile_selected", -10, -10, size + 20, size + 20, Theme.Hex(0xFFE14D), 32, 0, null, null, "Selected");
                glow.color = KitArt.Get("tile_selected") != null ? Color.white : Theme.Hex(0xFFE14D);
            }
            if (d.Empty)
            {
                var hole = KitV2.Box(root, "tile_empty", 0, 0, size, size, new Color(0.11f, 0.1f, 0.2f, 0.28f), 24 * k, 5.3f, new Color(0.11f, 0.1f, 0.2f, 0.55f), null, "Tile");
                var g = KitV2.Icon(root, PartArt.SlotGlyph(d.Kind), size * 0.27f, size * 0.24f, size * 0.46f, size * 0.46f, "Glyph", new Color(0.24f, 0.2f, 0.36f, 0.55f));
                if (d.Caption != null) KitV2.Text(root, d.Caption, 4, size * 0.72f, size - 8, size * 0.22f, 28 * k + 4, Theme.Hex(0x6A5A8C), TextAnchor.MiddleCenter, "Caption", false);
                if (d.Alert) KitV2.BadgeRed(root, size - 49 * k - 8, -17, "!", 64, "Alert");
                if (onClick != null) KitV2.Tap(root, onClick);
                return root;
            }
            var colors = KitV2.TileColors(d.Rarity);
            var tile = KitV2.Box(root, KitV2.TileId(d.Rarity), 0, 0, size, size, colors.top, 24 * k, 6.7f, KitV2.Ink, colors.bottom, "Tile");
            if (KitArt.Get(KitV2.TileId(d.Rarity)) == null)
            {
                var inner = Ui.Rect(tile.rectTransform, "Inner");
                Ui.Stretch(inner, 6.7f, 6.7f, 6.7f, 6.7f);
                inner.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                UiShapes.Outline(inner, new Color(1, 1, 1, 0.35f), 18 * k, 5.3f);
            }
            if (d.Rarity >= (int)PartRarity.MythicSS) UiShapes.Outline(tile.rectTransform, Theme.Hex(0xFFE14D), 24 * k, 4, "Crown");

            var icon = PartArt.Icon(d.Def);
            float ix = size * 0.177f, isz = size * 0.646f;
            if (icon != null) KitV2.Icon(root, icon, ix, ix, isz, isz, "Icon");
            else KitV2.Icon(root, PartArt.SlotGlyph(d.Kind), ix + isz * 0.08f, ix + isz * 0.06f, isz * 0.84f, isz * 0.84f, "Glyph", PartArt.GlyphColor(d.Def));

            // slot badge (kit slot_badge + the white slot glyph)
            var badge = KitV2.Box(root, "slot_badge", -3, -2, 56, 56, KitV2.SlotBadge, 13.3f, 5.3f, KitV2.Ink, null, "SlotBadge").rectTransform;
            KitV2.Icon(badge, PartArt.SlotGlyph(d.Kind), 13, 13, 30, 30, "Glyph");

            KitV2.Text(root, "Lv." + d.Level, size - 13 - 110, 11, 110, 37, 32, Color.white, TextAnchor.MiddleRight, "Level");
            var rank = KitV2.Text(root, PartArt.RankRich(d.Rarity, 59), 8, size - 61, 120, 67, 58.7f, KitV2.RankColor(d.Rarity), TextAnchor.MiddleLeft, "Rank");
            rank.supportRichText = true;
            if (d.Copies > 0)
            {
                var dia = KitV2.Diamond(root, "copy_diamond", size - 62, size - 61, 79, KitV2.DiamondGold, 5.3f);
                KitV2.Text(root, d.Copies > 99 ? "99+" : d.Copies.ToString(), size - 62, size - 61, 79, 79, d.Copies > 9 ? 22 : 26.7f, KitV2.Ink, TextAnchor.MiddleCenter, "Copies", false);
            }
            if (d.Locked)
            {
                var lk = KitV2.Icon(root, SummonArt.Icon("lock"), size - 46, 52, 34, 34, "LockIcon");
            }
            if (d.Equipped)
            {
                var eq = KitV2.Box(root, "tile_equipped", size - 44, size - 102, 40, 40, Theme.Good, 20, 4, KitV2.Ink, null, "EquippedMark");
                if (KitArt.Get("tile_equipped") == null) KitV2.Icon(eq.rectTransform, SummonArt.Icon("check"), 6, 6, 28, 28, "Check");
            }
            if (d.LevelUp)
            {
                // equipped and the wallet pays its next level: a small green arrow (the hub's Worm badge says the same)
                var up = KitV2.Circle(root, "badge_levelup", 4, 62, 46, Theme.Hex(0x3FBF4A), (KitV2.Ink, 4f));
                up.name = "LevelUpBadge";
                KitV2.Text(up.rectTransform, "▲", 0, 0, 46, 44, 26, Color.white, TextAnchor.MiddleCenter, "Arrow", false);
            }
            if (d.Alert) KitV2.BadgeRed(root, size - 49, -17, "!", 64, "Alert");
            if (d.Pips) KitV2.PipStrip(root, (size - 149) / 2, size + 14, d.Gems, d.OpenSockets);
            if (onClick != null) KitV2.Tap(root, onClick);
            return root;
        }
    }
}
