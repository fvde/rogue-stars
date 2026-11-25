using System;
using System.Globalization;

namespace RogueStars.Rules.Core
{
    /// <summary>How a player-facing number is written (owner rule G3).</summary>
    public enum NumMode
    {
        /// <summary>Whole number, rounded ("12", "-3"). The default.</summary>
        Whole,
        /// <summary>One decimal only below 10 (rates, percentages: "+1.5%", "every 2.5 turns"), else whole; never a trailing ".0".</summary>
        OneDecimal,
        /// <summary>Amounts: whole below 10K, then K / M / B; one decimal only while the abbreviated value is below 10 ("9.5M"; "20K", "61K").</summary>
        Abbrev,
    }

    /// <summary>
    /// The one number formatter for player-facing text (rules texts and the client's <c>Fmt</c>). Call sites pick a
    /// <see cref="NumMode"/>; they never write format strings themselves.
    /// </summary>
    public static class NumFmt
    {
        public static string Format(double v, NumMode mode)
        {
            switch (mode)
            {
                case NumMode.OneDecimal: return OneDecimal(v);
                case NumMode.Abbrev: return Abbrev(v);
                default: return Whole(v);
            }
        }

        public static string Whole(double v) =>
            ((long)Math.Round(v, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

        public static string OneDecimal(double v)
        {
            double r = Math.Round(v, 1, MidpointRounding.AwayFromZero);
            if (Math.Abs(r) >= 10 || Math.Abs(r - Math.Round(r)) < 1e-9) return Whole(v);
            return r.ToString("0.0", CultureInfo.InvariantCulture);
        }

        public static string Abbrev(double v)
        {
            double a = Math.Abs(v);
            if (a >= 1e9) return Scaled(v / 1e9) + "B";
            if (a >= 1e6) return Scaled(v / 1e6) + "M";
            if (a >= 1e4) return Scaled(v / 1e3) + "K";
            // Amounts below 10K: whole, rounded down (never show an amount the player does not have yet).
            return ((long)Math.Floor(v)).ToString(CultureInfo.InvariantCulture);
        }

        private static string Scaled(double s)
        {
            if (Math.Abs(s) < 10)
            {
                double t = Math.Floor(s * 10) / 10;
                if (Math.Abs(t - Math.Round(t)) < 1e-9) return ((long)Math.Round(t)).ToString(CultureInfo.InvariantCulture);
                return t.ToString("0.0", CultureInfo.InvariantCulture);
            }
            return ((long)Math.Floor(s)).ToString(CultureInfo.InvariantCulture);
        }
    }
}
