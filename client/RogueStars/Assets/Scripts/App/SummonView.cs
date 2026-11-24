using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.App
{
    /// <summary>
    /// What the summon event screens show for a state (docs/design/summon-events.md): pure queries so tests can check the
    /// texts, badges and targets without building UI. Whole numbers only.
    /// </summary>
    public static class SummonView
    {
        /// <summary>Event types the player can open (their feature unlocked), heroes first.</summary>
        public static List<SummonEventType> Available(GameState s, ContentDb c)
        {
            var list = new List<SummonEventType>();
            foreach (var t in SummonEventSystem.Types)
            {
                var def = c.SummonType(t);
                if (def != null && Features.IsUnlocked(s, c, def.Feature)) list.Add(t);
            }
            return list;
        }

        /// <summary>Display name of the featured hero / mount.</summary>
        public static string FeaturedName(ContentDb c, SummonEventType type, string id)
        {
            if (type == SummonEventType.Hero) return c.HeroConfig.Defs.TryGetValue(id ?? "", out var h) ? h.Name : id;
            return c.MountConfig.Defs.TryGetValue(id ?? "", out var m) ? m.Name : id;
        }

        public static string TypeNoun(SummonEventType type) => type == SummonEventType.Hero ? "hero" : "mount";

        /// <summary>"Luck 23/300".</summary>
        public static string LuckText(GameState s, ContentDb c, SummonEventType type) =>
            "Luck " + SummonEventSystem.Luck(s, type) + "/" + SummonEventSystem.LuckMax(c, type);

        /// <summary>The one-line Luck rule under the meter.</summary>
        public static string LuckRule(SummonEventType type) =>
            "Guaranteed featured " + TypeNoun(type) + " when Luck is full. Luck is kept until the next " + TypeNoun(type) + " summon event.";

        /// <summary>"Time left 5d 5h".</summary>
        public static string TimeLeft(long endMs, long nowMs) => "Time left " + Fmt.Duration(System.Math.Max(0, endMs - nowMs));

        /// <summary>Badge numbers of the side column: tasks, pass days, packs (free ad ticket) — 0 hides the badge.</summary>
        public static int TaskBadge(GameState s, ContentDb c, SummonEventType type, long now) => SummonEventSystem.ClaimableTasks(s, c, type, now);
        public static int PassBadge(GameState s, ContentDb c, SummonEventType type, long now) => SummonEventSystem.ClaimablePassDays(s, c, type, now);
        public static int PacksBadge(GameState s, ContentDb c, SummonEventType type, long now)
        {
            var def = c.SummonType(type);
            return def == null ? 0 : ShopSystem.AdViewsLeft(s, c, def.AdPlacementId, now);
        }

        /// <summary>Hub entry badge: everything waiting in both events.</summary>
        public static int HubBadge(GameState s, ContentDb c, long now)
        {
            int n = 0;
            foreach (var t in Available(s, c)) n += SummonEventSystem.BadgeCount(s, c, t, now) + PacksBadge(s, c, t, now);
            return n;
        }

        /// <summary>Screen a task's GO button opens (runs for fights, levels and mini-games; the Signals for pulls).</summary>
        public static string GoScreen(SummonTaskLineDef line) =>
            line != null && line.CounterKey.StartsWith("pulls_") ? "signals" : "portals";

        /// <summary>Short amount caption on a reward tile ("×5", "2,000" → "2000", a copy shows nothing).</summary>
        public static string Amount(Reward r) =>
            r == null || r.Kind == RewardKinds.Hero || r.Kind == RewardKinds.Mount ? "" : "×" + Fmt.Num(r.Amount);

        /// <summary>Name of a reward tile (featured placeholders resolved by the caller).</summary>
        public static string RewardName(Reward r, ContentDb c)
        {
            if (r == null) return "";
            switch (r.Kind)
            {
                case RewardKinds.Hero: return FeaturedName(c, SummonEventType.Hero, r.Id);
                case RewardKinds.Mount: return FeaturedName(c, SummonEventType.Mount, r.Id);
                case RewardKinds.HeroShards: return FeaturedName(c, SummonEventType.Hero, r.Id) + " shards";
                case RewardKinds.Currency: return Fmt.CurrencyName(r.Id, c);
                default: return Fmt.Reward(r, c);
            }
        }

        /// <summary>Badge of a pulled copy: "NEW", "★4", "AWAKEN", "+300 Star Sigils".</summary>
        public static string CopyBadge(SummonPullItem it, ContentDb c)
        {
            if (it == null || string.IsNullOrEmpty(it.Note)) return "";
            switch (it.Note)
            {
                case "new": return "NEW";
                case "star": return "★" + it.Stars;
                case "awaken": return "AWAKEN";
                case "overflow":
                    var r = it.Kind == RewardKinds.Hero ? c.HeroConfig.CopyOverflow : c.MountConfig.CopyOverflow;
                    return r == null ? "MAX" : "+" + Fmt.Num(r.Amount) + " " + Fmt.CurrencyName(r.Id, c);
                default: return "";
            }
        }

        /// <summary>Draw button caption: the ticket count it needs, or the Diamond price when tickets are short.</summary>
        public static bool PaysWithTickets(GameState s, SummonEventTypeDef def, int count) =>
            s.Wallet.Get(def.TicketCurrency) >= (count == 10 ? def.Gacha.TokenCost10 : def.Gacha.TokenCost1);

        public static long DiamondCost(SummonEventTypeDef def, int count) => count == 10 ? def.DiamondCost10 : def.DiamondCost1;

        /// <summary>Exchange item limit caption: "Event limit 1/3".</summary>
        public static string LimitText(SummonEventProgress p, SummonExchangeItemDef x) =>
            x.EventLimit > 0 ? "Event limit " + SummonEventSystem.ExchangeBought(p, x.Id) + "/" + x.EventLimit : "";

        /// <summary>Hours / minutes until the next pass day unlocks ("Next reward in 5h 16m"), or "" when every day is open.</summary>
        public static string NextPassDay(GameState s, ContentDb c, SummonEventType type, long now)
        {
            var def = c.SummonType(type);
            var p = SummonEventSystem.Progress(s, c, type, now);
            if (def == null || p == null || SummonEventSystem.PassDaysUnlocked(s, c, type, now) >= def.Pass.Count) return "";
            long next = GameClock.DayStartMs(c, GameClock.DayIndex(c, now) + 1);
            return "Next reward in " + Fmt.Duration(next - now);
        }
    }
}
