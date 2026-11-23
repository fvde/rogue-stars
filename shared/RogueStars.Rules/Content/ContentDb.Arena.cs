using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    [Serializable]
    public sealed class RankRewardDef
    {
        /// <summary>Applies to ranks up to and including MaxRank (first match wins).</summary>
        public int MaxRank;
        public List<Reward> Rewards = new List<Reward>();
    }

    [Serializable]
    public sealed class ArenaLeagueDef
    {
        public string Id;
        public string Name;
        /// <summary>Top % of the group promoted at week end (ignored in the top league).</summary>
        public double PromotePercent;
        /// <summary>Bottom % of the group demoted at week end (ignored in the bottom league).</summary>
        public double DemotePercent;
        public List<RankRewardDef> RankRewards = new List<RankRewardDef>();
    }

    [Serializable]
    public sealed class ArenaConfig
    {
        /// <summary>Weekly leaderboard group size.</summary>
        public int GroupSize = 150;
        public long StartPoints = 1000;
        /// <summary>Elo-style K factor.</summary>
        public double K = 40;
        /// <summary>Minimum points for a win (so every win counts).</summary>
        public long MinWinGain = 5;
        public long MinPoints = 0;
        public long StartTickets = 5;
        public long TicketsPerFight = 1;
        /// <summary>Arena Medals per fight (PvP currency for the arena shop).</summary>
        public long MedalsPerWin = 4, MedalsPerLoss = 2;
        public List<ArenaLeagueDef> Leagues = new List<ArenaLeagueDef>();
    }

    public sealed partial class ContentDb
    {
        public readonly ArenaConfig Arena = new ArenaConfig();

        partial void BuildArena()
        {
            RegenRules.Add(new RegenRule { Currency = Currencies.ArenaTickets, BaseCap = 5, AmountPerTick = 1, TickMs = 2 * MetaTime.Hour });

            string[] names = { "Pebble", "Copper", "Silver", "Gold", "Comet", "Nova", "Galaxy" };
            for (int i = 0; i < names.Length; i++)
            {
                long scale = i + 1;
                double medalScale = 1 + 0.25 * i;
                var l = new ArenaLeagueDef { Id = names[i].ToLowerInvariant(), Name = names[i] + " League", PromotePercent = 20, DemotePercent = i == 0 ? 0 : 20 };
                RankRewardDef R(int maxRank, long dia, long tickets, long medals) => new RankRewardDef
                {
                    MaxRank = maxRank,
                    Rewards =
                    {
                        Reward.Cur(Currencies.Diamonds, dia * scale), Reward.Cur(Currencies.ArenaTickets, tickets),
                        Reward.Cur(Currencies.ArenaMedals, (long)Math.Round(medals * medalScale)),
                    },
                };
                l.RankRewards.Add(R(1, 200, 5, 120));
                l.RankRewards.Add(R(3, 150, 4, 100));
                l.RankRewards.Add(R(10, 100, 3, 80));
                l.RankRewards.Add(R(30, 60, 2, 60));
                l.RankRewards.Add(R(75, 40, 1, 40));
                l.RankRewards.Add(R(150, 20, 0, 20));
                foreach (var rr in l.RankRewards) rr.Rewards.RemoveAll(x => x.Amount <= 0);
                Arena.Leagues.Add(l);
            }
        }
    }
}
