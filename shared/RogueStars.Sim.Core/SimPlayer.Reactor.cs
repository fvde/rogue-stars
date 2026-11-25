using System;
using System.Collections.Generic;
using System.Linq;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim
{
    public sealed partial class SimPlayer
    {
        // ---- Shard Reactor (docs/design/pull-system.md) ----------------------------------------------------------------

        /// <summary>
        /// Collects finished jobs, then fills free jobs: excess shards (beyond 10★) of the lowest rarity first into the highest-rarity
        /// item of the same kind that is not at 10★ (owned items first), as many units as the excess and half the fuel balance allow.
        /// </summary>
        private void Reactor()
        {
            if (ShardReactorSystem.Building(S, C) == null) return;
            if (S.Reactor.Jobs.Any(j => ShardReactorSystem.IsDone(S, j, Now))) Exec(new ReactorCollect());
            foreach (var kind in new[] { ReactorKind.Alien, ReactorKind.Blueprint })
            {
                if (kind == ReactorKind.Alien && !Unlocked(Features.Aliens)) continue;
                for (int guard = 0; guard < 3 && S.Reactor.Jobs.Count < ShardReactorSystem.JobSlots(S, C); guard++)
                {
                    var items = ShardReactorSystem.Items(C, kind);
                    var target = items.Where(id => ShardReactorSystem.StarsOf(S, kind, id) < C.ShardStars.MaxStars)
                        .OrderByDescending(id => (int)ShardReactorSystem.RarityOf(C, kind, id)!.Value)
                        .ThenByDescending(id => ShardReactorSystem.StarsOf(S, kind, id) > 0 ? 1 : 0)
                        .ThenBy(id => id, StringComparer.Ordinal).FirstOrDefault();
                    if (target == null) break;
                    var tr = (int)ShardReactorSystem.RarityOf(C, kind, target)!.Value;
                    ReactorStart pick = null;
                    foreach (var src in items.Where(id => (int)ShardReactorSystem.RarityOf(C, kind, id)!.Value < tr)
                                 .OrderBy(id => (int)ShardReactorSystem.RarityOf(C, kind, id)!.Value).ThenBy(id => id, StringComparer.Ordinal))
                    {
                        long excess = ShardReactorSystem.ExcessShards(S, C, kind, src);
                        var one = ShardReactorSystem.Quote(S, C, kind, src, target, 1);
                        if (one.Error != null || excess < one.SourceShards) continue;
                        long fuelBudget = Bal(one.FuelCurrency) / 2;
                        int units = (int)Math.Min(C.ShardReactor.MaxUnits, Math.Min(excess / one.SourceShards, one.Fuel > 0 ? fuelBudget / one.Fuel : long.MaxValue));
                        if (units < 1) continue;
                        pick = new ReactorStart { Kind = kind, SourceId = src, TargetId = target, Units = units };
                        break;
                    }
                    if (pick == null || !Ok(pick)) break;
                }
            }
        }
    }
}
