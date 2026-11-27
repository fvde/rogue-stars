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
        /// <summary>Needs rewarded ads: hidden while ads are disabled (<see cref="ShopConfig.AdsDisabled"/>).</summary>
        public bool NeedsAds;
        /// <summary>
        /// Achievements only: a repeating milestone every <see cref="Every"/> of the counter (claimed milestones are counted in
        /// <c>QuestState.AchievementClaims</c>); <see cref="Target"/> is the same number (the next milestone's distance).
        /// 0 = a one-time quest.
        /// </summary>
        public long Every;
    }

    public enum PointTrack { Daily, Weekly }

    [Serializable]
    public sealed class PointChestDef
    {
        public PointTrack Track;
        public long Points;
        public List<Reward> Rewards = new List<Reward>();
        /// <summary>Pull tokens, each of a random type the player has unlocked (Part, Alien, Blueprint), drawn with the deterministic player stream.</summary>
        public int RandomTokens;
        /// <summary>This many tokens of EACH pull type the player has unlocked.</summary>
        public int TokensEachType;
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

            // Quest availability rule (owner, 2026-11-26): Daily Quests are there from level 1 (no unlock moment), so every quest is gated by the
            // feature that makes it possible (the Feature field keeps it hidden until then) and the early track must be completable alone.
            // Daily and weekly quests pay no reward of their own, only points: the bar's chests are the reward (owner, 2026-11-26).
            // Daily (reset 00:00 UTC). Early set (no feature, from level 1): login, runs, battles, skills, stamina = exactly 100 points, so
            // every daily chest is reachable without ads, the shop or the arena; the later quests are extra points.
            Q("d_login", "Log in", QuestKind.Daily, "login_days", 1, 10, 10, null);
            Q("d_runs", "Complete 3 runs", QuestKind.Daily, "runs_completed", 3, 25, 20, null);
            Q("d_battles", "Win 10 battles", QuestKind.Daily, "run_battles_won", 10, 20, 10, null);
            // Replaces "Do 1 Part pull" (a pull costs tokens or Diamonds, a new player may have none): picking skills costs nothing, and a
            // single run of a Casual player has 7 or more picks.
            Q("d_skills", "Pick 5 run skills", QuestKind.Daily, "run_skills_drafted", 5, 20, 10, null);
            Q("d_stamina", "Spend 30 stamina", QuestKind.Daily, "stamina_spent", 30, 25, 10, null);
            // Extras from the feature that makes them possible. The ad quest also hides while ads are off (QuestDef.NeedsAds).
            Q("d_shop", "Buy 1 shop item", QuestKind.Daily, "shop_items_bought", 1, 10, 5, null);
            Q("d_ad", "Watch 1 ad", QuestKind.Daily, "ads_watched", 1, 10, 5, null);
            Quests["d_ad"].NeedsAds = true;
            Q("d_arena", "Fight 2 arena battles", QuestKind.Daily, "arena_fights", 2, 20, 10, Features.Arena);

            // Weekly (reset Monday 00:00 UTC). Early set (runs, bosses, daily quests) = exactly 500 points: the weekly track is complete
            // before the City; the City, Breeding and Invasion quests are extra points.
            Q("w_runs", "Complete 20 runs", QuestKind.Weekly, "runs_completed", 20, 150, 100, null);
            Q("w_bosses", "Defeat 10 bosses", QuestKind.Weekly, "run_bosses_killed", 10, 150, 80, null);
            Q("w_daily", "Finish 14 daily quests", QuestKind.Weekly, "daily_quests_claimed", 14, 200, 100, null);
            Q("w_blueprints", "Do 10 Blueprint pulls", QuestKind.Weekly, "pulls_blueprint", 10, 50, 50, Features.Blueprints);
            Q("w_buildings", "Upgrade 5 buildings", QuestKind.Weekly, "city_building_upgrades", 5, 50, 50, Features.BiomassFarm);
            Q("w_breed", "Breed 2 aliens", QuestKind.Weekly, "breeds_collected", 2, 50, 50, Features.Breeding);
            Q("w_invasion", "Fight 3 invasion battles", QuestKind.Weekly, "invasion_battles", 3, 100, 80, Features.Invasions);

            // Achievements: repeating milestones, linear (owner, 2026-11-26): the same reward every N of a counter, for ever. Counters are
            // absolute; claimed milestones are counted per achievement (QuestState.AchievementClaims).
            void A(string id, string name, string counter, long every, string feature, Reward reward)
            {
                Q(id, name, QuestKind.Achievement, counter, every, 0, 0, feature, reward);
                Quests[id].Every = every;
            }
            Reward Tok(string currency) => Reward.Cur(currency, 10);
            A("a_enemies", "Defeat 100 enemies", "run_enemies_defeated", 100, null, Dia(10));
            A("a_bosses", "Defeat 10 bosses", "run_bosses_killed", 10, null, Dia(20));
            A("a_minigames", "Play 10 mini-games", "run_minigames_played", 10, Features.RunMinigames, Dia(20));
            A("a_worm", "Gain 10 worm levels", "worm_level_max", 10, Features.WormLevel, Dia(50));
            A("a_pulls_part", "Do 100 Part pulls", "pulls_part", 100, Features.PartPulls, Tok(Currencies.PartTokens));
            A("a_pulls_alien", "Do 100 Alien pulls", "pulls_alien", 100, Features.AlienPulls, Tok(Currencies.AlienTokens));
            A("a_pulls_blueprint", "Do 100 Blueprint pulls", "pulls_blueprint", 100, Features.Blueprints, Tok(Currencies.BlueprintTokens));
            A("a_arena", "Win 50 arena fights", "arena_wins", 50, Features.Arena, Dia(50));
            A("a_invasion_top", "Be top contributor of an invasion", "invasion_top_contributor", 1, Features.Invasions, Dia(100));

            // New-player 7-day event quests (absolute counters, unlock per day). Targets follow what a Casual F2P bot (about 15 minutes a
            // day, one run a day) reaches by that day (docs/balance/daily-quests.md); Active and Hardcore are far ahead of them.
            void NP(int day, string id, string name, string counter, long target, string feature, params Reward[] rewards)
            {
                Q(id, name, QuestKind.NewPlayer, counter, target, 0, 0, feature, rewards);
                Quests[id].Day = day;
            }
            NP(1, "np_runs_3", "Complete 3 runs", "runs_completed", 3, null, Dia(50));
            NP(2, "np_worm_5", "Reach worm level 5", "worm_level_max", 5, Features.WormLevel, Dia(50));
            NP(3, "np_pulls_10", "Do 10 Part pulls", "pulls_part", 10, Features.PartPulls, Reward.Cur(Currencies.PartTokens, 5)); // the free 10× pull of the Parts unlock is exactly 10
            NP(4, "np_runs_5", "Complete 5 runs", "runs_completed", 5, null, Dia(80));
            NP(5, "np_battles_50", "Win 50 battles", "run_battles_won", 50, null, Dia(80));
            NP(6, "np_worm_11", "Reach worm level 11", "worm_level_max", 11, Features.WormLevel, Dia(100));
            NP(7, "np_bosses_8", "Defeat 8 bosses", "run_bosses_killed", 8, null, Reward.Cur(Currencies.AlienTokens, 10));

            void Chest(PointTrack track, long points, params Reward[] rewards)
            {
                var c = new PointChestDef { Track = track, Points = points };
                c.Rewards.AddRange(rewards);
                PointChests.Add(c);
            }
            // Point chests (owner, 2026-11-26). Daily: Stamina, Biomass, Ore, Gear Cores and 100 Diamonds spread over the five; the last
            // chest holds 3 pull tokens, each of a random unlocked type. The first chests pay what a level 1 player can use; Gear Cores
            // wait for the third (the reward gate holds them until a part can use them).
            Chest(PointTrack.Daily, 20, Reward.Cur(Currencies.Stamina, 10), Reward.Cur(Currencies.Ore, 500), Dia(10));
            Chest(PointTrack.Daily, 40, Reward.Cur(Currencies.Biomass, 1000), Dia(20));
            Chest(PointTrack.Daily, 60, Reward.Cur(Currencies.GearCores, 20), Reward.Cur(Currencies.Stamina, 10), Dia(20));
            Chest(PointTrack.Daily, 80, Reward.Cur(Currencies.Ore, 1000), Reward.Cur(Currencies.Biomass, 1000), Dia(20));
            Chest(PointTrack.Daily, 100, Dia(30));
            PointChests[PointChests.Count - 1].RandomTokens = 3;
            // Weekly: 500 Diamonds spread, more Ore and Biomass than the daily ones, 5 tokens of each unlocked pull type, Gear Cores and
            // one Part awakening core in the top chest.
            Chest(PointTrack.Weekly, 100, Reward.Cur(Currencies.Ore, 3000), Dia(50));
            Chest(PointTrack.Weekly, 200, Reward.Cur(Currencies.Biomass, 3000), Dia(100));
            Chest(PointTrack.Weekly, 300, Reward.Cur(Currencies.GearCores, 100), Reward.Cur(Currencies.Ore, 2000), Dia(100));
            Chest(PointTrack.Weekly, 400, Reward.Cur(Currencies.Biomass, 2000), Dia(100));
            PointChests[PointChests.Count - 1].TokensEachType = 5;
            Chest(PointTrack.Weekly, 500, Reward.Cur(ProgressionCurrencies.PartAwakenCores, 1), Dia(150));

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
