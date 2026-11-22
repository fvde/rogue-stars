using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Client.App
{
    /// <summary>Display formatting for numbers, durations, currencies and rewards.</summary>
    public static class Fmt
    {
        public static string Num(double v)
        {
            double a = System.Math.Abs(v);
            if (a >= 1e9) return (v / 1e9).ToString("0.##", CultureInfo.InvariantCulture) + "B";
            if (a >= 1e6) return (v / 1e6).ToString("0.##", CultureInfo.InvariantCulture) + "M";
            if (a >= 1e4) return (v / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return ((long)System.Math.Floor(v)).ToString(CultureInfo.InvariantCulture);
        }

        public static string Duration(long ms)
        {
            if (ms <= 0) return "0s";
            long s = (ms + 999) / 1000;
            long d = s / 86400, h = s % 86400 / 3600, m = s % 3600 / 60, sec = s % 60;
            if (d > 0) return d + "d " + h + "h";
            if (h > 0) return h + "h " + m.ToString("00") + "m";
            if (m > 0) return m + "m " + sec.ToString("00") + "s";
            return sec + "s";
        }

        public static string Pct(double v) => v.ToString("0.#", CultureInfo.InvariantCulture) + "%";

        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { Currencies.Biomass, "Biomass" }, { Currencies.Grain, "Grain" }, { Currencies.Ore, "Ore" },
            { Currencies.GearCores, "Gear Cores" }, { Currencies.AdvancementPlates, "Adv. Plates" }, { Currencies.Fuel, "Fuel" },
            { Currencies.HeroXp, "Hero XP" }, { Currencies.Diamonds, "Diamonds" }, { Currencies.BlueprintTokens, "Blueprint Tokens" },
            { Currencies.PartTokens, "Part Tokens" }, { Currencies.AlienTokens, "Alien Tokens" }, { Currencies.Stamina, "Stamina" },
            { Currencies.ArenaTickets, "Arena Tickets" }, { Currencies.BurrowKeys, "Burrow Keys" }, { Currencies.InvasionAttempts, "Invasion Attempts" },
            { Currencies.GuildMarks, "Guild Marks" }, { Currencies.MountCores, "Mount Cores" },
        };

        /// <summary>Short labels for the resource bar.</summary>
        private static readonly Dictionary<string, string> Short = new Dictionary<string, string>
        {
            { Currencies.Biomass, "BIO" }, { Currencies.Grain, "GRN" }, { Currencies.Ore, "ORE" }, { Currencies.Diamonds, "DIA" },
            { Currencies.Stamina, "STA" }, { Currencies.Fuel, "FUEL" }, { Currencies.GearCores, "CORE" }, { Currencies.ArenaTickets, "TIX" },
        };

        public static string CurrencyName(string id, ContentDb content = null)
        {
            if (id == null) return "?";
            if (Names.TryGetValue(id, out var n)) return n;
            if (content != null)
            {
                if (id.StartsWith("shards_bp_")) return (content.Blueprint(id.Substring(10))?.Name ?? id.Substring(10)) + " Shards";
                if (id.StartsWith("shards_alien_")) return (content.AlienType(id.Substring(13))?.Name ?? id.Substring(13)) + " Shards";
            }
            return id.Replace('_', ' ');
        }

        public static string CurrencyShort(string id) => Short.TryGetValue(id, out var s) ? s : CurrencyName(id);

        public static string Reward(Reward r, ContentDb content)
        {
            if (r == null) return "";
            switch (r.Kind)
            {
                case RewardKinds.Currency: return Num(r.Amount) + " " + CurrencyName(r.Id, content);
                case RewardKinds.Blueprint: return (r.Amount > 1 ? r.Amount + "× " : "") + "Blueprint " + (content?.Blueprint(r.Id)?.Name ?? r.Id);
                case RewardKinds.Alien: return (r.Amount > 1 ? r.Amount + "× " : "") + "Alien " + (content?.AlienType(r.Id)?.Name ?? r.Id);
                default: return r.Amount + "× " + r.Kind + " " + r.Id;
            }
        }

        public static string Rewards(IEnumerable<Reward> rewards, ContentDb content)
        {
            if (rewards == null) return "";
            var sb = new StringBuilder();
            foreach (var r in rewards) { if (sb.Length > 0) sb.Append(", "); sb.Append(Reward(r, content)); }
            return sb.ToString();
        }

        public static string Costs(IEnumerable<Cost> costs, ContentDb content = null)
        {
            if (costs == null) return "free";
            var sb = new StringBuilder();
            foreach (var c in costs) { if (sb.Length > 0) sb.Append(" + "); sb.Append(Num(c.Amount)).Append(' ').Append(CurrencyName(c.Currency, content)); }
            return sb.Length == 0 ? "free" : sb.ToString();
        }

        public static string Price(int usdCents) => usdCents <= 0 ? "FREE" : "$" + (usdCents / 100.0).ToString("0.00", CultureInfo.InvariantCulture);

        public static string Stars(int stars) => stars + "★";

        public static string Humanize(string snake)
        {
            if (string.IsNullOrEmpty(snake)) return "";
            var s = snake.Replace('_', ' ');
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
