using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    public enum QuestKind { Daily, Weekly, Achievement, NewPlayer }

    /// <summary>
    /// Counter-driven quest: progress = state.Counter(CounterKey) since the period start (daily/weekly)
    /// or absolute (achievements, new-player event).
    /// </summary>
    [Serializable]
    public sealed class QuestDef
    {
        public string Id;
        public string Name;
        public QuestKind Kind;
        public string CounterKey;
        public long Target;
        public List<Reward> Rewards = new List<Reward>();
        /// <summary>Points for the daily/weekly point track.</summary>
        public long Points;
        public long SeasonXp;
        /// <summary>New-player event: available from this day (1-based) of the event.</summary>
        public int Day;
        /// <summary>Hidden until this feature is unlocked (optional).</summary>
        public string Feature;
    }

    public enum PointTrack { Daily, Weekly }

    [Serializable]
    public sealed class PointChestDef
    {
        public PointTrack Track;
        public long Points;
        public List<Reward> Rewards = new List<Reward>();
    }

    [Serializable]
    public sealed class RetentionConfig
    {
        /// <summary>7-day login cycle: a missed day pauses the cycle, it never resets (no streak loss).</summary>
        public List<List<Reward>> LoginCycle = new List<List<Reward>>();
        /// <summary>New-player event day rewards (index 0 = day 1).</summary>
        public List<List<Reward>> NewPlayerDays = new List<List<Reward>>();
        /// <summary>New-player event length in days (day rewards unlock one per day).</summary>
        public int NewPlayerEventDays = 7;
        /// <summary>Extra days to claim leftovers after the event.</summary>
        public int NewPlayerGraceDays = 3;
        /// <summary>Absence that triggers the returning-player gift mail.</summary>
        public long ReturningAfterMs = 7 * MetaTime.Day;
        public long ReturningCooldownMs = 30 * MetaTime.Day;
        public string ReturningMailTemplate = "returning_gift";
    }

    public sealed partial class ContentDb
    {
        public readonly Dictionary<string, QuestDef> Quests = new Dictionary<string, QuestDef>();
        public readonly List<PointChestDef> PointChests = new List<PointChestDef>();
        public readonly RetentionConfig Retention = new RetentionConfig();

        public QuestDef Quest(string id) => id != null && Quests.TryGetValue(id, out var q) ? q : null;

        partial void BuildQuests()
        {
            Reward Dia(long n) => Reward.Cur(Currencies.Diamonds, n);
            void Q(string id, string name, QuestKind kind, string counter, long target, long points, long xp, string feature, params Reward[] rewards)
            {
                var q = new QuestDef { Id = id, Name = name, Kind = kind, CounterKey = counter, Target = target, Points = points, SeasonXp = xp, Feature = feature };
                q.Rewards.AddRange(rewards);
                Quests[id] = q;
            }

            // Daily (reset 00:00 UTC).
            Q("d_login", "Log in", QuestKind.Daily, "login_days", 1, 10, 10, null, Reward.Cur(Currencies.Stamina, 10));
            Q("d_runs", "Complete 3 runs", QuestKind.Daily, "runs_completed", 3, 20, 20, null, Dia(10));
            Q("d_battles", "Win 10 battles", QuestKind.Daily, "run_battles_won", 10, 20, 10, null, Reward.Cur(Currencies.Biomass, 1000));
            Q("d_stamina", "Spend 30 stamina", QuestKind.Daily, "stamina_spent", 30, 20, 10, null, Reward.Cur(Currencies.Ore, 1000));
            Q("d_pull", "Do 1 Part pull", QuestKind.Daily, "pulls_part", 1, 10, 10, Features.PartPulls, Dia(5));
            Q("d_ad", "Watch 1 ad", QuestKind.Daily, "ads_watched", 1, 10, 5, null, Reward.Cur(Currencies.Stamina, 5));
            Q("d_arena", "Fight 2 arena battles", QuestKind.Daily, "arena_fights", 2, 20, 10, Features.Arena, Reward.Cur(Currencies.ArenaTickets, 1));
            Q("d_shop", "Buy 1 shop item", QuestKind.Daily, "shop_items_bought", 1, 10, 5, null, Reward.Cur(Currencies.Fuel, 300));

            // Weekly (reset Monday 00:00 UTC).
            Q("w_runs", "Complete 20 runs", QuestKind.Weekly, "runs_completed", 20, 100, 100, null, Dia(50));
            Q("w_bosses", "Defeat 10 bosses", QuestKind.Weekly, "run_bosses_killed", 10, 100, 80, null, Reward.Cur(Currencies.GearCores, 100));
            Q("w_daily", "Finish 20 daily quests", QuestKind.Weekly, "daily_quests_claimed", 20, 100, 100, null, Dia(50));
            Q("w_blueprints", "Do 10 Blueprint pulls", QuestKind.Weekly, "pulls_blueprint", 10, 50, 50, Features.Blueprints, Reward.Cur(Currencies.BlueprintTokens, 3));
            Q("w_buildings", "Upgrade 5 buildings", QuestKind.Weekly, "city_building_upgrades", 5, 50, 50, null, Reward.Cur(Currencies.Biomass, 5000));
            Q("w_breed", "Breed 2 aliens", QuestKind.Weekly, "breeds_collected", 2, 50, 50, Features.Breeding, Reward.Cur(Currencies.Grain, 3000));
            Q("w_invasion", "Fight 3 invasion battles", QuestKind.Weekly, "invasion_battles", 3, 100, 80, Features.Invasions, Dia(50));

            // Achievements (absolute counters).
            long[] runTiers = { 1, 10, 50, 100, 500, 1000 };
            for (int i = 0; i < runTiers.Length; i++)
                Q("a_runs_" + runTiers[i], "Complete " + runTiers[i] + " runs", QuestKind.Achievement, "runs_completed", runTiers[i], 0, 0, null, Dia(20 + 20 * i));
            long[] wormTiers = { 10, 20, 30, 50 };
            for (int i = 0; i < wormTiers.Length; i++)
                Q("a_worm_" + wormTiers[i], "Reach worm level " + wormTiers[i], QuestKind.Achievement, "worm_level_max", wormTiers[i], 0, 0, null, Dia(50 + 50 * i));
            Q("a_pulls_100", "Do 100 Part pulls", QuestKind.Achievement, "pulls_part", 100, 0, 0, null, Dia(200));
            Q("a_arena_50", "Win 50 arena fights", QuestKind.Achievement, "arena_wins", 50, 0, 0, Features.Arena, Dia(200));
            Q("a_invasion_top", "Be top contributor of an invasion", QuestKind.Achievement, "invasion_top_contributor", 1, 0, 0, Features.Invasions, Dia(300));

            // New-player 7-day event quests (absolute counters, unlock per day).
            void NP(int day, string id, string name, string counter, long target, params Reward[] rewards)
            {
                Q(id, name, QuestKind.NewPlayer, counter, target, 0, 0, null, rewards);
                Quests[id].Day = day;
            }
            NP(1, "np_runs_3", "Complete 3 runs", "runs_completed", 3, Dia(50));
            NP(2, "np_worm_5", "Reach worm level 5", "worm_level_max", 5, Dia(50));
            NP(3, "np_pulls_10", "Do 10 Part pulls", "pulls_part", 10, Reward.Cur(Currencies.PartTokens, 5));
            NP(4, "np_runs_15", "Complete 15 runs", "runs_completed", 15, Dia(80));
            NP(5, "np_buildings_5", "Upgrade 5 buildings", "city_building_upgrades", 5, Dia(80));
            NP(6, "np_worm_12", "Reach worm level 12", "worm_level_max", 12, Dia(100));
            NP(7, "np_bosses_20", "Defeat 20 bosses", "run_bosses_killed", 20, Reward.Cur(Currencies.AlienTokens, 10));

            void Chest(PointTrack track, long points, params Reward[] rewards)
            {
                var c = new PointChestDef { Track = track, Points = points };
                c.Rewards.AddRange(rewards);
                PointChests.Add(c);
            }
            // Data trickle (Laboratory) so research works before the Dark Side opens.
            Chest(PointTrack.Daily, 20, Reward.Cur(Currencies.Biomass, 2000), Reward.Cur(Currencies.Data, 10));
            Chest(PointTrack.Daily, 40, Dia(10));
            Chest(PointTrack.Daily, 60, Reward.Cur(Currencies.Stamina, 10), Reward.Cur(Currencies.Data, 10));
            Chest(PointTrack.Daily, 80, Reward.Cur(Currencies.PartTokens, 1));
            Chest(PointTrack.Daily, 100, Dia(30));
            Chest(PointTrack.Weekly, 100, Reward.Cur(Currencies.Ore, 3000));
            Chest(PointTrack.Weekly, 200, Dia(50));
            Chest(PointTrack.Weekly, 300, Reward.Cur(Currencies.BlueprintTokens, 3), Reward.Cur(Currencies.Data, 100));
            Chest(PointTrack.Weekly, 400, Dia(100));
            Chest(PointTrack.Weekly, 500, Reward.Cur(Currencies.AlienTokens, 5));

            List<Reward> L(params Reward[] r) => new List<Reward>(r);
            var lc = Retention.LoginCycle;
            lc.Add(L(Dia(20)));
            lc.Add(L(Reward.Cur(Currencies.Stamina, 20)));
            lc.Add(L(Reward.Cur(Currencies.PartTokens, 2)));
            lc.Add(L(Dia(30)));
            lc.Add(L(Reward.Cur(Currencies.BlueprintTokens, 2)));
            lc.Add(L(Reward.Cur(Currencies.GearCores, 50)));
            lc.Add(L(Dia(80), Reward.Cur(Currencies.AlienTokens, 2)));

            var np = Retention.NewPlayerDays;
            np.Add(L(Dia(100)));
            np.Add(L(Reward.Cur(Currencies.PartTokens, 5)));
            np.Add(L(Reward.Cur(Currencies.Stamina, 60)));
            np.Add(L(Dia(150)));
            np.Add(L(Reward.Cur(Currencies.BlueprintTokens, 5)));
            np.Add(L(Reward.Cur(Currencies.AlienTokens, 5)));
            np.Add(L(Dia(300), Reward.Cur(Currencies.PartTokens, 10)));
        }
    }
}
