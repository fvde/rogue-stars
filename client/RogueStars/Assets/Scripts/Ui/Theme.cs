using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using UnityEngine;

namespace RogueStars.Client.UiKit
{
    /// <summary>Tile/effect valence colours (GDD §3.1).</summary>
    public enum Valence { Positive, Negative, Enemy, Elite, Boss, Special }

    /// <summary>
    /// The game's look in one place: a warm dark palette (approved wireframes), rounded panels, chunky orange primary
    /// buttons with a darker bottom lip, a rounded display font (Fredoka) for titles and numbers, sizes and spacing.
    /// </summary>
    public static class Theme
    {
        // ---- surfaces ----
        public static readonly Color Background = Hex(0x170E10);
        /// <summary>Cards and tiles.</summary>
        public static readonly Color Panel = Hex(0x2B1A1A);
        /// <summary>Bottom bars, inner wells, secondary cards.</summary>
        public static readonly Color PanelAlt = Hex(0x22141A);
        /// <summary>Raised / highlighted card.</summary>
        public static readonly Color Header = Hex(0x3A2420);
        /// <summary>Secondary button face.</summary>
        public static readonly Color Button = Hex(0x472C22);
        /// <summary>Hairlines, dashed slots, connector dots.</summary>
        public static readonly Color Border = Hex(0x5C3B33);
        /// <summary>Empty part of a bar / track.</summary>
        public static readonly Color Track = Hex(0x3A2026);

        // ---- brand ----
        /// <summary>Primary action (START RUN, ROLL): orange with a lighter top and a dark "lip".</summary>
        public static readonly Color Accent = Hex(0xFF8A1F);
        public static readonly Color AccentLight = Hex(0xFFB347);
        public static readonly Color AccentLip = Hex(0xB85A0B);
        /// <summary>Text on bright faces (orange, gold, green, cyan buttons).</summary>
        public static readonly Color OnAccent = Hex(0x2A1300);
        public static readonly Color Gold = Hex(0xFFC35C);
        public static readonly Color Cream = Hex(0xFFE0B8);
        /// <summary>The worm's own pink (token, worm sprite).</summary>
        public static readonly Color Worm = Hex(0xF28BB6);
        public static readonly Color WormLight = Hex(0xFFD0E2);

        // ---- text ----
        public static readonly Color Text = Hex(0xFFF1E6);
        public static readonly Color TextDim = Hex(0xD9A98A);
        public static readonly Color TextFaint = Hex(0x8E6A5E);

        // ---- signal colours ----
        public static readonly Color Good = Hex(0x3FD07A);
        public static readonly Color Bad = Hex(0xE5484D);
        public static readonly Color Warn = Hex(0xFFC35C);
        public static readonly Color Locked = Hex(0x3A2A28);
        public static readonly Color Premium = Hex(0x7FE3FF);
        public static readonly Color Purple = Hex(0xA77BFF);
        public static readonly Color Blue = Hex(0x3E7BFA);
        public static readonly Color Dim = new Color(0.04f, 0.02f, 0.03f, 0.82f);

        // ---- type ----
        public const int FontSmall = 26;
        public const int FontNormal = 32;
        public const int FontLarge = 40;
        public const int FontTitle = 48;
        public const int FontHuge = 72;

        // ---- metrics ----
        public const float RowHeight = 96;
        public const float ButtonHeight = 88;
        /// <summary>Corner radii (canvas units).</summary>
        public const float RadiusCard = 36, RadiusButton = 26, RadiusSmall = 16;
        /// <summary>Height of the darker bottom edge ("lip") of chunky buttons.</summary>
        public const float ButtonLip = 8;
        public const float Gutter = 24;

        /// <summary>Readable text colour on a face of colour <paramref name="face"/> (dark on bright faces).</summary>
        public static Color TextOn(Color face)
        {
            float lum = 0.2126f * face.r + 0.7152f * face.g + 0.0722f * face.b;
            return lum > 0.5f ? OnAccent : Text;
        }

        /// <summary>Darker shade of a colour (button lips, borders).</summary>
        public static Color Shade(Color c, float k = 0.6f) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        /// <summary>A dark card tinted towards <paramref name="c"/> (risk/reward chips, rarity cards).</summary>
        public static Color Tint(Color c, float k = 0.22f)
        {
            var t = Color.Lerp(Panel, c, k);
            t.a = 1;
            return t;
        }

        public static Color RarityColor(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return Hex(0xB8AEA6);
                case Rarity.Rare: return Hex(0x4FA3FF);
                case Rarity.Epic: return Hex(0xB58CFF);
                case Rarity.Legendary: return Hex(0xFFB347);
                case Rarity.Mythic: return Hex(0xFF3D7F);
                default: return Color.gray;
            }
        }

        /// <summary>Colours of the 14-step part ladder (Common … Mythic SS+2): Mythic pink, Mythic S red-orange, Mythic SS gold, lighter per +1/+2.</summary>
        private static readonly int[] PartLadder =
        {
            0x9AA0A6, 0x5BC26B, 0x3D8BFF, 0xA65BFF, 0xFF9F1C,
            0xFF3D7F, 0xFF5C95, 0xFF7AAB,
            0xFF4A1F, 0xFF6A45, 0xFF8A6B,
            0xF5C518, 0xFFD54A, 0xFFE57F,
        };

        public static Color PartRarityColor(int partRarity) => Hex(PartLadder[Mathf.Clamp(partRarity, 0, PartLadder.Length - 1)]);

        public static Color ValenceColor(Valence v)
        {
            switch (v)
            {
                case Valence.Positive: return Hex(0x3FD07A);
                case Valence.Negative: return Hex(0xE5484D);
                case Valence.Enemy: return Hex(0xFF8A3D);
                case Valence.Elite: return Hex(0xB58CFF);
                case Valence.Boss: return Hex(0xE02A3A);
                case Valence.Special: return Hex(0x7FE3FF);
                default: return Color.gray;
            }
        }

        /// <summary>Stable colour per currency id.</summary>
        public static Color CurrencyColor(string currency)
        {
            switch (currency)
            {
                case Currencies.Biomass: return Hex(0x7BE08A);
                case Currencies.Grain: return Hex(0xE3C567);
                case Currencies.Ore: return Hex(0xB8A493);
                case Currencies.Diamonds: return Hex(0x7FE3FF);
                case Currencies.Stamina: return Hex(0xFFC35C);
                case Currencies.Fuel: return Hex(0xFF8A3D);
                default:
                    uint h = 2166136261;
                    foreach (char c in currency ?? "") { h ^= c; h *= 16777619; }
                    return Color.HSVToRGB((h % 360) / 360f, 0.45f, 0.9f);
            }
        }

        /// <summary>Build-archetype colours (draft cards, skills, heroes, parts).</summary>
        public static Color FamilyColor(SkillFamily f)
        {
            switch (f)
            {
                case SkillFamily.Fire: return Hex(0xFF8A1F);
                case SkillFamily.Ice: return Hex(0x4FA3FF);
                case SkillFamily.Lightning: return Hex(0xFFE03D);
                case SkillFamily.Poison: return Hex(0x8BE04E);
                case SkillFamily.Bleed: return Hex(0xE5484D);
                case SkillFamily.AlienSynergy: return Hex(0xC77DFF);
                case SkillFamily.Counter: return Hex(0x4FD3C4);
                case SkillFamily.Combo: return Hex(0xFF9FD2);
                case SkillFamily.Crit: return Hex(0xB58CFF);
                default: return Hex(0xB8AEA6);
            }
        }

        public static Color TileColor(TileValence v)
        {
            switch (v)
            {
                case TileValence.Positive: return ValenceColor(Valence.Positive);
                case TileValence.Negative: return ValenceColor(Valence.Negative);
                case TileValence.Enemy: return ValenceColor(Valence.Enemy);
                case TileValence.Elite: return ValenceColor(Valence.Elite);
                case TileValence.Boss: return ValenceColor(Valence.Boss);
                case TileValence.Special: return ValenceColor(Valence.Special);
                default: return Border;
            }
        }

        /// <summary>One-or-two letter icon per tile category (compact boards, path previews).</summary>
        public static string TileLetter(TileCategory c)
        {
            switch (c)
            {
                case TileCategory.Start: return "GO";
                case TileCategory.Empty: return "";
                case TileCategory.Enemy: return "E";
                case TileCategory.Elite: return "EL";
                case TileCategory.Miniboss: return "MB";
                case TileCategory.StatBuff: return "+";
                case TileCategory.StatDebuff: return "-";
                case TileCategory.Loot: return "$";
                case TileCategory.Treasure: return "T";
                case TileCategory.Event: return "?";
                case TileCategory.Campfire: return "C";
                case TileCategory.Shop: return "S";
                case TileCategory.Hazard: return "!";
                case TileCategory.Shard: return "*";
                case TileCategory.Pickup: return "o";
                case TileCategory.BossTile: return "B";
                case TileCategory.BossGate: return "BOSS";
                case TileCategory.Minigame: return "G";
                default: return "";
            }
        }

        /// <summary>Word label per tile category (board tiles: FIGHT, SHOP, EVENT, CHEST, REST …).</summary>
        public static string TileLabel(TileCategory c)
        {
            switch (c)
            {
                case TileCategory.Start: return "START";
                case TileCategory.Empty: return "";
                case TileCategory.Enemy: return "FIGHT";
                case TileCategory.Elite: return "ELITE";
                case TileCategory.Miniboss: return "MINI\nBOSS";
                case TileCategory.StatBuff: return "BUFF";
                case TileCategory.StatDebuff: return "CURSE";
                case TileCategory.Loot: return "LOOT";
                case TileCategory.Treasure: return "CHEST";
                case TileCategory.Event: return "EVENT";
                case TileCategory.Campfire: return "REST";
                case TileCategory.Shop: return "SHOP";
                case TileCategory.Hazard: return "TRAP";
                case TileCategory.Shard: return "SHARD";
                case TileCategory.Pickup: return "ITEM";
                case TileCategory.BossTile: return "BOSS";
                case TileCategory.BossGate: return "BOSS";
                case TileCategory.Minigame: return "GAME";
                default: return "";
            }
        }

        /// <summary>Border/label colour of a board tile: by category (wireframe colours), else by valence.</summary>
        public static Color TileAccent(TileCategory c, TileValence v)
        {
            switch (c)
            {
                case TileCategory.Start: return TextFaint;
                case TileCategory.Empty: return Border;
                case TileCategory.Enemy: return Bad;
                case TileCategory.Elite: return Hex(0xFF6B9A);
                case TileCategory.Miniboss: return Hex(0xFF5A3C);
                case TileCategory.Shop: return Blue;
                case TileCategory.Event: return Purple;
                case TileCategory.Treasure: case TileCategory.Loot: return Gold;
                case TileCategory.Campfire: return Good;
                case TileCategory.StatBuff: return Hex(0x7BE08A);
                case TileCategory.StatDebuff: case TileCategory.Hazard: return Hex(0xFF9B3D);
                case TileCategory.Shard: case TileCategory.Pickup: return Premium;
                case TileCategory.BossTile: case TileCategory.BossGate: return Hex(0xE02A3A);
                case TileCategory.Minigame: return Hex(0x5CF2C8);
                default: return TileColor(v);
            }
        }

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
