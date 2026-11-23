using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    /// <summary>
    /// Global schedule in server time (UTC ms): first occurrence at <see cref="StartMs"/>, lasting
    /// <see cref="DurationMs"/>, repeating every <see cref="RecurrenceMs"/> (0 = once) until <see cref="EndMs"/> (0 = forever).
    /// </summary>
    [Serializable]
    public sealed class ScheduleDef
    {
        public long StartMs;
        public long DurationMs;
        public long RecurrenceMs;
        public long EndMs;

        /// <summary>Latest occurrence that started at or before <paramref name="nowMs"/> (may be over).</summary>
        public bool TryLatest(long nowMs, out long start, out long end)
        {
            start = end = 0;
            if (nowMs < StartMs || DurationMs <= 0) return false;
            long k = RecurrenceMs > 0 ? (nowMs - StartMs) / RecurrenceMs : 0;
            start = StartMs + k * RecurrenceMs;
            if (EndMs > 0 && start >= EndMs)
            {
                if (RecurrenceMs <= 0) return false;
                k = (EndMs - 1 - StartMs) / RecurrenceMs;
                if (k < 0) return false;
                start = StartMs + k * RecurrenceMs;
            }
            end = start + DurationMs;
            return true;
        }

        /// <summary>Occurrence active at <paramref name="nowMs"/>.</summary>
        public bool TryActive(long nowMs, out long start, out long end) => TryLatest(nowMs, out start, out end) && nowMs < end;

        /// <summary>First occurrence starting after <paramref name="nowMs"/>.</summary>
        public bool TryNext(long nowMs, out long start, out long end)
        {
            start = end = 0;
            if (DurationMs <= 0) return false;
            if (nowMs < StartMs) start = StartMs;
            else if (RecurrenceMs <= 0) return false;
            else start = StartMs + ((nowMs - StartMs) / RecurrenceMs + 1) * RecurrenceMs;
            if (EndMs > 0 && start >= EndMs) return false;
            end = start + DurationMs;
            return true;
        }

        /// <summary>Start times of the occurrences overlapping [fromMs, toMs), newest first (bounded walk).</summary>
        public List<long> Overlapping(long fromMs, long toMs)
        {
            var list = new List<long>();
            if (toMs <= fromMs || !TryLatest(toMs - 1, out long start, out long end)) return list;
            for (int guard = 0; guard < 64; guard++)
            {
                if (end > fromMs) list.Add(start);
                else break;
                if (RecurrenceMs <= 0 || start - RecurrenceMs < StartMs) break;
                start -= RecurrenceMs;
                end = start + DurationMs;
            }
            return list;
        }
    }

    /// <summary>Template of a timed modifier written into state.Modifiers (see <see cref="Modifier"/> targets).</summary>
    [Serializable]
    public sealed class ModifierDef
    {
        public string Target;
        public double Multiplier = 1;
        /// <summary>Lasting duration for invasion outcomes; planetary events use the event window.</summary>
        public long DurationMs;
        public string Description;

        public ModifierDef() { }
        public ModifierDef(string target, double multiplier, long durationMs, string description)
        { Target = target; Multiplier = multiplier; DurationMs = durationMs; Description = description; }
    }

    [Serializable]
    public sealed class ContributionTierDef
    {
        public string Name;
        /// <summary>Applies to contribution ranks 1..MaxRank (first matching tier wins).</summary>
        public int MaxRank;
        public List<Reward> Rewards = new List<Reward>();
    }

    /// <summary>A reward that a contribution rank wins only with a chance (rolled with the player's RNG stream).</summary>
    [Serializable]
    public sealed class RankChanceRewardDef
    {
        /// <summary>Applies to contribution ranks 1..MaxRank (first matching entry wins).</summary>
        public int MaxRank;
        public double ChancePct;
        public Reward Reward;
    }

    [Serializable]
    public sealed class InvasionDef
    {
        public string Id;
        public string Name;
        public string Description;
        /// <summary>Boss enemy id for the battle simulator (Run workstream content).</summary>
        public string BossId;
        /// <summary>Group HP pool: the group repels the invasion when total score reaches this.</summary>
        public long GroupScoreTarget;
        /// <summary>Repelled → lasting bonus (×1.5..×3, 2..7 days).</summary>
        public List<ModifierDef> WinModifiers = new List<ModifierDef>();
        /// <summary>Overrun → lasting malus (÷1.5..÷4, 2..7 days).</summary>
        public List<ModifierDef> LoseModifiers = new List<ModifierDef>();
        /// <summary>By contribution rank in the group; top contributors get extra.</summary>
        public List<ContributionTierDef> Tiers = new List<ContributionTierDef>();
        /// <summary>Extra for every participant when the group repels the invader.</summary>
        public List<Reward> RepelledRewards = new List<Reward>();
        /// <summary>High-stakes invader: its repelled outcome can pay rare rewards (Awaken Cores) to top contributors.</summary>
        public bool HighTier;
        /// <summary>Rare rewards for top contributors of a repelled occurrence (chance per rank band).</summary>
        public List<RankChanceRewardDef> RepelledRareRewards = new List<RankChanceRewardDef>();
    }

    [Serializable]
    public sealed class PlanetaryEventDef
    {
        public string Id;
        public string Name;
        public string Description;
        /// <summary>Applied for the event window (start..end of the occurrence).</summary>
        public List<ModifierDef> Modifiers = new List<ModifierDef>();
        /// <summary>Surprising side effect text (also encoded in Modifiers).</summary>
        public string SideEffect;
    }

    public enum LiveOpsKind { Invasion, PlanetaryEvent }

    /// <summary>One scheduled live event (an invasion or a planetary event) on the global calendar.</summary>
    [Serializable]
    public sealed class LiveOpsEventDef
    {
        public string Id;
        public LiveOpsKind Kind;
        /// <summary>Invasion id or planetary event id.</summary>
        public string RefId;
        public ScheduleDef Schedule = new ScheduleDef();
        /// <summary>False for surprise invasions: not shown in the upcoming list before they start.</summary>
        public bool Announced = true;
    }

    [Serializable]
    public sealed class LiveOpsConfig
    {
        /// <summary>Calendar origin of the live-ops schedules (Monday 2026-01-05 00:00 UTC).</summary>
        public long EpochMs = 1_767_571_200_000L;
        /// <summary>Invasion attempts topped up at each daily reset (extra attempts above are kept).</summary>
        public long InvasionAttemptsPerDay = 3;
        /// <summary>Battle damage per contribution score point.</summary>
        public long DamagePerScorePoint = 100;
        /// <summary>Guild marks earned per contribution score point (members only; 0 = none).</summary>
        public double GuildMarksPerScorePoint = 0.01;
        /// <summary>
        /// At most this many invasions run at once; an occurrence that would exceed it, or that overlaps a running
        /// occurrence of the same invasion type, is skipped (announced invasions win over surprises, then earlier starts).
        /// </summary>
        public int MaxConcurrentInvasions = 2;
        /// <summary>Approximate invasion group size (server batching).</summary>
        public int InvasionGroupSize = 20;
        // Bounds for invasion outcome modifiers (validated).
        public double BoostMin = 1.5, BoostMax = 3.0, CutMinDivisor = 1.5, CutMaxDivisor = 4.0;
        public long OutcomeMinDurationMs = 2 * MetaTime.Day, OutcomeMaxDurationMs = 7 * MetaTime.Day;
    }

    public sealed partial class ContentDb
    {
        public readonly Dictionary<string, InvasionDef> Invasions = new Dictionary<string, InvasionDef>();
        public readonly Dictionary<string, PlanetaryEventDef> PlanetaryEvents = new Dictionary<string, PlanetaryEventDef>();
        /// <summary>Global live-ops calendar, ordered.</summary>
        public readonly List<LiveOpsEventDef> LiveOpsSchedule = new List<LiveOpsEventDef>();
        public readonly LiveOpsConfig LiveOps = new LiveOpsConfig();

        public InvasionDef Invasion(string id) => id != null && Invasions.TryGetValue(id, out var d) ? d : null;
        public PlanetaryEventDef PlanetaryEvent(string id) => id != null && PlanetaryEvents.TryGetValue(id, out var d) ? d : null;

        partial void BuildLiveOps()
        {
            const long D = MetaTime.Day;
            List<ContributionTierDef> Tiers(long scale)
            {
                ContributionTierDef T(string n, int rank, long dia, long tok) => new ContributionTierDef
                {
                    Name = n, MaxRank = rank,
                    Rewards = { Reward.Cur(Currencies.Diamonds, dia * scale), Reward.Cur(Currencies.PartTokens, tok) },
                };
                return new List<ContributionTierDef>
                {
                    T("Champion", 1, 100, 5), T("Vanguard", 3, 60, 3), T("Defender", 10, 30, 2), T("Participant", int.MaxValue, 10, 1),
                };
            }
            void Inv(string id, string name, string boss, string desc, ModifierDef[] win, ModifierDef[] lose)
            {
                var d = new InvasionDef { Id = id, Name = name, BossId = boss, Description = desc, GroupScoreTarget = 20_000, Tiers = Tiers(1) };
                d.WinModifiers.AddRange(win); d.LoseModifiers.AddRange(lose);
                d.RepelledRewards.Add(Reward.Cur(Currencies.Diamonds, 30));
                Invasions[id] = d;
            }

            Inv("gloop_tide", "Gloop Tide", "boss_gloop_matriarch", "A sticky tide of gloop rolls over the fields.",
                new[] { new ModifierDef("production:grain", 2.0, 5 * D, "Gloop fertilizer: Grain ×2") },
                new[] { new ModifierDef("production:biomass", 0.5, 3 * D, "Gloop-choked farms: Biomass ÷2") });
            Inv("rust_swarm", "Rust Swarm", "boss_rust_queen", "Metal-eating mites swarm the mines.",
                new[] { new ModifierDef("production:ore", 1.5, 7 * D, "Scrap harvest: Ore ×1.5"), new ModifierDef("run_reward:ore", 1.5, 7 * D, "Scrap loot: run Ore ×1.5") },
                new[] { new ModifierDef("production:ore", 1.0 / 1.5, 4 * D, "Rusted drills: Ore ÷1.5") });
            // High-stakes invaders (Void Choir, Prism Horde): top contributors of a repelled occurrence may win an Awaken Core.
            void HighTier(string id)
            {
                var d = Invasions[id];
                d.HighTier = true;
                RankChanceRewardDef C(int rank, double pct) => new RankChanceRewardDef { MaxRank = rank, ChancePct = pct, Reward = Reward.Cur(ProgressionCurrencies.PartAwakenCores, 1) };
                d.RepelledRareRewards.Add(C(1, 25));
                d.RepelledRareRewards.Add(C(3, 12));
                d.RepelledRareRewards.Add(C(10, 4));
            }
            Inv("void_choir", "Void Choir", "boss_hollow_cantor", "A humming choir from the void lulls the planet to sleep.",
                new[] { new ModifierDef("production:diamonds", 2.0, 7 * D, "Resonant crystals: Diamonds ×2") },
                new[] { new ModifierDef("stamina_regen", 1.0 / 1.5, 2 * D, "Drowsy worm: stamina regen ÷1.5") });
            Inv("prism_horde", "Prism Horde", "boss_prism_tyrant", "Crystal beasts march out of the canyon.",
                new[] { new ModifierDef("run_reward:*", 1.5, 3 * D, "Shattered prisms: all run rewards ×1.5") },
                new[] { new ModifierDef("production:grain", 0.25, 2 * D, "Trampled fields: Grain ÷4") });

            HighTier("void_choir");
            HighTier("prism_horde");

            void Ev(string id, string name, string desc, string side, params ModifierDef[] mods)
            {
                var e = new PlanetaryEventDef { Id = id, Name = name, Description = desc, SideEffect = side };
                e.Modifiers.AddRange(mods);
                PlanetaryEvents[id] = e;
            }
            Ev("diamond_rain", "Diamond Rain", "Glittering rain soaks the crust.", null,
                new ModifierDef("production:diamonds", 2.0, 0, "Diamonds ×2"));
            Ev("grain_blight", "Grain Blight", "A purple blight withers the crops...", "...but the aliens find it romantic: breeding ×2 speed.",
                new ModifierDef("production:grain", 0.25, 0, "Grain ÷4"), new ModifierDef("breeding_speed", 2.0, 0, "Breeding ×2 speed"));
            Ev("ore_quake", "Ore Quake", "Quakes push ore veins to the surface.", "Builders keep falling over: construction ÷1.33 speed.",
                new ModifierDef("production:ore", 2.0, 0, "Ore ×2"), new ModifierDef("build_speed", 0.75, 0, "Construction slowed"));
            Ev("biomass_bloom", "Biomass Bloom", "Everything grows. Everything.", "Enemies grow too: enemy ATK +10%.",
                new ModifierDef("production:biomass", 2.0, 0, "Biomass ×2"), new ModifierDef("enemy_atk", 1.1, 0, "Overgrown enemies"));
            Ev("solar_flare", "Solar Flare", "The star burps. The worm feels energized.", "Diamond mines overheat: Diamonds ÷2.",
                new ModifierDef("stamina_regen", 1.5, 0, "Stamina regen ×1.5"), new ModifierDef("production:diamonds", 0.5, 0, "Diamonds ÷2"));
            Ev("meteor_shower", "Meteor Shower", "Rocks with gear cores inside fall from the sky.", null,
                new ModifierDef("run_reward:gear_cores", 2.0, 0, "Run Gear Cores ×2"), new ModifierDef("run_reward:*", 1.25, 0, "All run rewards ×1.25"));
            Ev("low_gravity", "Low Gravity", "The moon came too close. Things float.", "Ships fly on fumes: run Fuel ×2, but Grain ÷1.5 as seeds drift away.",
                new ModifierDef("run_reward:fuel", 2.0, 0, "Run Fuel ×2"), new ModifierDef("production:grain", 1.0 / 1.5, 0, "Seeds drift away"));
            Ev("alien_festival", "Alien Festival", "Every species throws a party at once.", "Nobody rests: stamina regen ÷1.33.",
                new ModifierDef("breeding_speed", 1.5, 0, "Breeding ×1.5 speed"), new ModifierDef("production:grain", 1.5, 0, "Grain ×1.5"),
                new ModifierDef("stamina_regen", 0.75, 0, "Stamina regen slowed"));

            long epoch = LiveOps.EpochMs;
            // Weekly scheduled invasions: Saturday 00:00 → Monday 00:00 UTC, rotating through the 4 invaders.
            string[] invOrder = { "gloop_tide", "rust_swarm", "void_choir", "prism_horde" };
            for (int i = 0; i < invOrder.Length; i++)
                LiveOpsSchedule.Add(new LiveOpsEventDef
                {
                    Id = "weekly_" + invOrder[i], Kind = LiveOpsKind.Invasion, RefId = invOrder[i],
                    Schedule = new ScheduleDef { StartMs = epoch + i * 7 * D + 5 * D, DurationMs = 2 * D, RecurrenceMs = 28 * D },
                });
            // Surprise invasions: unannounced, 12 h, on an irregular 11/13-day rhythm.
            LiveOpsSchedule.Add(new LiveOpsEventDef
            {
                Id = "surprise_rust", Kind = LiveOpsKind.Invasion, RefId = "rust_swarm", Announced = false,
                Schedule = new ScheduleDef { StartMs = epoch + 2 * D + 14 * MetaTime.Hour, DurationMs = 12 * MetaTime.Hour, RecurrenceMs = 11 * D },
            });
            LiveOpsSchedule.Add(new LiveOpsEventDef
            {
                Id = "surprise_gloop", Kind = LiveOpsKind.Invasion, RefId = "gloop_tide", Announced = false,
                Schedule = new ScheduleDef { StartMs = epoch + 9 * D + 18 * MetaTime.Hour, DurationMs = 12 * MetaTime.Hour, RecurrenceMs = 13 * D },
            });
            // Planetary events: one per week (Tuesday 00:00 UTC, 4 days), cycling through all 8 every 8 weeks.
            string[] evOrder = { "diamond_rain", "grain_blight", "ore_quake", "biomass_bloom", "solar_flare", "meteor_shower", "low_gravity", "alien_festival" };
            for (int i = 0; i < evOrder.Length; i++)
                LiveOpsSchedule.Add(new LiveOpsEventDef
                {
                    Id = "event_" + evOrder[i], Kind = LiveOpsKind.PlanetaryEvent, RefId = evOrder[i],
                    Schedule = new ScheduleDef { StartMs = epoch + i * 7 * D + 1 * D, DurationMs = 4 * D, RecurrenceMs = 56 * D },
                });
        }
    }
}
