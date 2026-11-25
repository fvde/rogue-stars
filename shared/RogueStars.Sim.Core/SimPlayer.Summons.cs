using System;
using System.Linq;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim
{
    public sealed partial class SimPlayer
    {
        // ---- summon events (docs/design/summon-events.md) ----------------------------------------------------------

        /// <summary>Exchange priorities: featured shards first, then tickets, then the rest (every item up to its event limit).</summary>
        private static readonly string[] HeroExchangeOrder = { "hero_shards", "hero_ticket", "hero_xp" };
        private static readonly string[] MountExchangeOrder = { "mount_shards", "mount_ticket", "mount_fuel" };

        /// <summary>
        /// Hero / mount summon events: the daily ad ticket, task / milestone / pass claims, every ticket pulled (10s, then
        /// singles), the exchange shop in priority order. Spenders put Diamonds into the event of the hero they target
        /// (whales also into mount events) and whales buy the event passes and crates within their monthly budget.
        /// </summary>
        private void Summons()
        {
            foreach (var type in SummonEventSystem.Types)
            {
                var def = C.SummonType(type);
                if (def == null || !Unlocked(def.Feature)) continue;
                var prog = SummonEventSystem.Progress(S, C, type, Now);
                if (prog == null) { E.Settle(S, Now); prog = SummonEventSystem.Progress(S, C, type, Now); }
                if (prog == null) continue;

                if (WillWatchAd(def.AdPlacementId) && Ok(new AdWatched { PlacementId = def.AdPlacementId, AdToken = AdToken })) UseTime(AdCost);
                SummonClaims(type, def);

                while (Bal(def.TicketCurrency) >= 10 && Ok(new SummonPull { EventType = type, Count = 10 })) UseTime(Timing.PullCommand);
                while (Bal(def.TicketCurrency) >= 1 && Ok(new SummonPull { EventType = type, Count = 1 })) UseTime(Timing.PullCommand);

                // Diamonds: spenders chase their target hero's event; whales also pull in mount events with what is left.
                bool target = type == SummonEventType.Hero && prog.FeaturedId == TargetHero;
                long cushion = type == SummonEventType.Hero ? (target ? 0 : long.MaxValue) : 5000;
                if (Spend.MonthlyBudgetCents > 0 && cushion != long.MaxValue)
                    while (Spendable(def.DiamondCost10 + cushion) && Ok(new SummonPull { EventType = type, Count = 10, Payment = SummonPayment.Diamonds }))
                        UseTime(Timing.PullCommand);

                SummonClaims(type, def); // milestones reached by the pulls
                foreach (var id in type == SummonEventType.Hero ? HeroExchangeOrder : MountExchangeOrder)
                    while (def.Exchange.Any(x => x.Id == id) && Ok(new SummonExchangeBuy { EventType = type, ItemId = id })) { }
            }
        }

        private void SummonClaims(SummonEventType type, SummonEventTypeDef def)
        {
            if (SummonEventSystem.ClaimableTasks(S, C, type, Now) > 0) Exec(new SummonTaskClaimAll { EventType = type });
            var prog = SummonEventSystem.Progress(S, C, type, Now);
            foreach (var m in def.Milestones)
                if (SummonEventSystem.MilestoneClaimable(prog, m)) Exec(new SummonMilestoneClaim { EventType = type, Pulls = m.Pulls });
            if (SummonEventSystem.ClaimablePassDays(S, C, type, Now) > 0) Exec(new SummonPassClaimAll { EventType = type });
        }

        /// <summary>Whales: each event's pass and its middle pack (crate), before Diamond packs (Purchases).</summary>
        private void SummonPurchases(Func<ProductDef, bool> tryBuy)
        {
            foreach (var type in SummonEventSystem.Types)
            {
                var def = C.SummonType(type);
                if (def == null || !Unlocked(def.Feature)) continue;
                var pass = C.Product(def.PassProductId);
                if (pass != null && ShopSystem.PurchaseBlockReason(S, C, pass, Now) == null) tryBuy(pass);
                if (def.PackProductIds.Count > 1)
                {
                    var crate = C.Product(def.PackProductIds[1]);
                    if (crate != null && ShopSystem.PurchaseBlockReason(S, C, crate, Now) == null) tryBuy(crate);
                }
            }
        }
    }
}
