using RogueStars.Rules.Core;
using UnityEngine;

namespace RogueStars.Client.UiKit
{
    /// <summary>Tile/effect valence colours (GDD §3.1).</summary>
    public enum Valence { Positive, Negative, Enemy, Elite, Boss, Special }

    /// <summary>Placeholder theme: one place for every colour and font size.</summary>
    public static class Theme
    {
        public static readonly Color Background = Hex(0x10101A);
        public static readonly Color Panel = Hex(0x1C1C2B);
        public static readonly Color PanelAlt = Hex(0x262638);
        public static readonly Color Header = Hex(0x2B2B44);
        public static readonly Color Button = Hex(0x3A3A5C);
        public static readonly Color Accent = Hex(0x4F7DF0);
        public static readonly Color Text = Color.white;
        public static readonly Color TextDim = Hex(0xA0A0B8);
        public static readonly Color Good = Hex(0x3FBF6A);
        public static readonly Color Bad = Hex(0xE0525A);
        public static readonly Color Warn = Hex(0xE8A23A);
        public static readonly Color Locked = Hex(0x44445A);
        public static readonly Color Premium = Hex(0x29C5D6);
        public static readonly Color Dim = new Color(0, 0, 0, 0.65f);

        public const int FontSmall = 26;
        public const int FontNormal = 32;
        public const int FontLarge = 40;
        public const int FontTitle = 48;
        public const float RowHeight = 96;
        public const float ButtonHeight = 88;

        public static Color RarityColor(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return Hex(0x9AA0A6);
                case Rarity.Rare: return Hex(0x3D8BFF);
                case Rarity.Epic: return Hex(0xA65BFF);
                case Rarity.Legendary: return Hex(0xFF9F1C);
                case Rarity.Mythic: return Hex(0xFF3D7F);
                default: return Color.gray;
            }
        }

        public static Color ValenceColor(Valence v)
        {
            switch (v)
            {
                case Valence.Positive: return Hex(0x3FBF6A);
                case Valence.Negative: return Hex(0xE0525A);
                case Valence.Enemy: return Hex(0xE8743A);
                case Valence.Elite: return Hex(0xB04BE0);
                case Valence.Boss: return Hex(0xC4162A);
                case Valence.Special: return Hex(0x29C5D6);
                default: return Color.gray;
            }
        }

        /// <summary>Stable placeholder colour per currency id.</summary>
        public static Color CurrencyColor(string currency)
        {
            switch (currency)
            {
                case Currencies.Biomass: return Hex(0x6CC24A);
                case Currencies.Grain: return Hex(0xE3C567);
                case Currencies.Ore: return Hex(0x9C8B7A);
                case Currencies.Diamonds: return Hex(0x29C5D6);
                case Currencies.Stamina: return Hex(0xF2D024);
                case Currencies.Fuel: return Hex(0xE8743A);
                default:
                    uint h = 2166136261;
                    foreach (char c in currency ?? "") { h ^= c; h *= 16777619; }
                    return Color.HSVToRGB((h % 360) / 360f, 0.45f, 0.85f);
            }
        }

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
