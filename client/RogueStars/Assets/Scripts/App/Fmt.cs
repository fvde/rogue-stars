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
            { Currencies.GuildMarks, "Guild Marks" }, { Currencies.MountCores, "Mount Cores" }, { RogueStars.Rules.Systems.ProgressionCurrencies.PartAwakenCores, "Awaken Cores" },
            { Currencies.Data, "Data" }, { Currencies.ArenaMedals, "Arena Medals" },
        };

        /// <summary>Short labels for the resource bar.</summary>
        private static readonly Dictionary<string, string> Short = new Dictionary<string, string>
        {
            { Currencies.Biomass, "BIO" }, { Currencies.Grain, "GRN" }, { Currencies.Ore, "ORE" }, { Currencies.Diamonds, "DIA" },
            { Currencies.Stamina, "STA" }, { Currencies.Fuel, "FUEL" }, { Currencies.GearCores, "CORE" }, { Currencies.ArenaTickets, "TIX" },
            { Currencies.Data, "DATA" }, { Currencies.ArenaMedals, "MEDAL" }, { RogueStars.Rules.Systems.ProgressionCurrencies.PartAwakenCores, "AWAKEN" },
        };

        public static string CurrencyName(string id, ContentDb content = null)
        {
            if (id == null) return "?";
            if (Names.TryGetValue(id, out var n)) return n;
            if (content != null)
            {
                if (id.StartsWith("shards_bp_")) return (content.Blueprint(id.Substring(10))?.Name ?? id.Substring(10)) + " Shards";
                if (id.StartsWith("shards_alien_")) return (content.AlienType(id.Substring(13))?.Name ?? id.Substring(13)) + " Shards";
                if (id.StartsWith("shards_hero_")) return (content.HeroConfig.Defs.TryGetValue(id.Substring(12), out var h) ? h.Name : id.Substring(12)) + " Shards";
                if (id.StartsWith("shards_mount_")) return (content.MountConfig.Defs.TryGetValue(id.Substring(13), out var m) ? m.Name : id.Substring(13)) + " Shards";
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
                case RewardKinds.Part:
                {
                    string rar = content != null ? content.PartsConfig.RarityDef(r.Rarity).Name : r.Rarity.ToString();
                    string name = r.Id == "*" ? "random part" : content != null && content.PartsConfig.Defs.TryGetValue(r.Id, out var pd) ? pd.Name : r.Id;
                    return (r.Amount > 1 ? r.Amount + "× " : "") + rar + " " + name;
                }
                case RewardKinds.Rune:
                {
                    string name = r.Id == "*" ? "random rune" : content != null && content.PartsConfig.Runes.TryGetValue(r.Id, out var rd) ? rd.Name : r.Id;
                    return (r.Amount > 1 ? r.Amount + "× " : "") + ((Rarity)r.Rarity) + " " + name;
                }
                case RewardKinds.HeroShards:
                    return r.Amount + " " + (content != null && content.HeroConfig.Defs.TryGetValue(r.Id, out var hd) ? hd.Name : r.Id) + " shards";
                case RewardKinds.Mount:
                    return (r.Amount > 1 ? r.Amount + "× " : "") + "Mount " + (content != null && content.MountConfig.Defs.TryGetValue(r.Id, out var md) ? md.Name : r.Id);
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

        /// <summary>
        /// Rewards the player can use now, formatted (reward previews, chests, calendars, shop contents): rewards whose currency
        /// or item kind has no use yet are left out (they would go to the reward stash, <see cref="RogueStars.Rules.Systems.RewardGate"/>).
        /// </summary>
        public static string Rewards(IEnumerable<Reward> rewards, ContentDb content, GameState state, long nowMs = 0)
        {
            if (state == null || rewards == null) return Rewards(rewards, content);
            var usable = RogueStars.Rules.Systems.RewardGate.Usable(state, content, rewards, nowMs);
            if (usable.Count == 0) foreach (var _ in rewards) return "—"; // nothing usable yet (not empty)
            return Rewards(usable, content);
        }

        /// <summary>
        /// Short chip texts for a reward list: currencies summed per id ("+520 Ore"), items of one kind counted when there are
        /// several ("3 Parts"), single items by name. Order of first appearance.
        /// </summary>
        public static List<string> RewardChips(IEnumerable<Reward> rewards, ContentDb content)
        {
            var order = new List<string>();
            var sums = new Dictionary<string, long>();
            var items = new Dictionary<string, List<Reward>>();
            if (rewards != null)
                foreach (var r in rewards)
                {
                    string key = r.Kind == RewardKinds.Currency ? "c:" + r.Id : "k:" + r.Kind;
                    if (!order.Contains(key)) order.Add(key);
                    if (r.Kind == RewardKinds.Currency) sums[key] = (sums.TryGetValue(key, out var v) ? v : 0) + r.Amount;
                    else { if (!items.TryGetValue(key, out var l)) items[key] = l = new List<Reward>(); l.Add(r); }
                }
            var chips = new List<string>();
            foreach (var key in order)
            {
                if (sums.TryGetValue(key, out var amount)) { chips.Add("+" + Num(amount) + " " + CurrencyName(key.Substring(2), content)); continue; }
                var l = items[key];
                long count = 0; foreach (var r in l) count += r.Kind == RewardKinds.HeroShards ? 1 : r.Amount;
                chips.Add(l.Count == 1 ? Reward(l[0], content) : count + " " + KindPlural(l[0].Kind));
            }
            return chips;
        }

        private static string KindPlural(string kind)
        {
            switch (kind)
            {
                case RewardKinds.Part: return "Parts";
                case RewardKinds.Rune: return "Runes";
                case RewardKinds.Blueprint: return "Blueprints";
                case RewardKinds.Alien: return "Aliens";
                case RewardKinds.Mount: return "Mounts";
                case RewardKinds.HeroShards: return "hero shard packs";
                default: return kind;
            }
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

        /// <summary>Display name of an owned part by uid (the awakened name once awakened); "Part" when unknown — never the uid.</summary>
        public static string PartName(ContentDb content, GameState state, string uid)
        {
            if (state != null && uid != null && state.Parts.Items.TryGetValue(uid, out var p)) return PartName(content, p);
            return "Part";
        }

        public static string PartName(ContentDb content, RogueStars.Rules.Systems.PartItem p)
        {
            if (content == null || p == null || !content.PartsConfig.Defs.ContainsKey(p.DefId)) return "Part";
            return RogueStars.Rules.Systems.PartsSystem.DisplayName(content, p);
        }

        /// <summary>A player's shown name: the display name, never the internal player id.</summary>
        public static string PlayerName(string displayName) => string.IsNullOrWhiteSpace(displayName) ? "Unnamed worm" : displayName;

        /// <summary>"LeftArm" → "left arm".</summary>
        public static string SplitWords(string camel)
        {
            if (string.IsNullOrEmpty(camel)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < camel.Length; i++)
            {
                char ch = camel[i];
                if (i > 0 && char.IsUpper(ch)) sb.Append(' ');
                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        /// <summary>Display name of a stat: "HP", "ATK", "DEF", else the words of its id ("Crit chance").</summary>
        public static string StatName(RogueStars.Rules.Core.StatId stat)
        {
            string id = stat.ToString();
            if (id == "Hp" || id == "Atk" || id == "Def") return id.ToUpperInvariant();
            return Words(id);
        }

        /// <summary>The words of a PascalCase id: "AlienSynergy" → "Alien synergy".</summary>
        public static string Words(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < id.Length; i++)
            {
                char ch = id[i];
                if (i > 0 && char.IsUpper(ch)) { sb.Append(' '); sb.Append(char.ToLowerInvariant(ch)); }
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        public static string Humanize(string snake)
        {
            if (string.IsNullOrEmpty(snake)) return "";
            var s = snake.Replace('_', ' ');
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>Exact skill text with numbers (generated from the skill's effects), else its description.</summary>
        public static string SkillFull(RogueStars.Rules.Core.SkillDef s)
        {
            if (s == null) return "";
            var full = RogueStars.Rules.Core.SkillText.Full(s);
            return string.IsNullOrEmpty(full) ? s.Description ?? "" : full;
        }

        /// <summary>A card's text: the skill at a level (or its upgrade from <paramref name="owned"/>), costs marked for the warning colour.</summary>
        public static string SkillCard(RogueStars.Rules.Content.ContentDb c, RogueStars.Rules.Core.SkillDef s, int owned)
        {
            if (s == null) return "";
            var full = owned > 0 ? RogueStars.Rules.Core.SkillText.UpgradeFullMarked(c, s, owned, owned + 1) : RogueStars.Rules.Core.SkillText.FullMarked(c, s, 1);
            return string.IsNullOrEmpty(full) ? s.Description ?? "" : full;
        }
    }
}
