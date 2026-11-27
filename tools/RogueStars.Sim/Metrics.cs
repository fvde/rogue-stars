using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim;

/// <summary>"Meaningful upgrade" events per system, read from the rules' own counters (docs/design/counters.md).</summary>
public static class SystemEvents
{
    public static readonly (string system, string[] counters)[] Systems =
    {
        ("Campaign", new[] { "levels_cleared" }),
        ("Worm", new[] { "worm_levels", "worm_talent_tiers" }),
        // parts v2: a better part equipped (pulls, boss first clears), a Mythic ladder step, a Forge unlock, an awakening
        ("Parts", new[] { "part_equips", "part_climbs", "part_forges", "part_awakens" }),
        ("PartLevels", new[] { "part_levels" }),
        ("Runes", new[] { "rune_merges" }),
        ("Heroes", new[] { "hero_stars", "heroes_unlocked" }),
        ("HeroLevels", new[] { "hero_levels" }),
        ("Mounts", new[] { "mount_stars", "mount_awakens", "mounts_unlocked" }),
        ("MountLevels", new[] { "mount_levels" }),
        ("Blueprints", new[] { "blueprint_star_ups", "blueprints_obtained" }),
        ("Buildings", new[] { "city_building_upgrades", "city_buildings_built", "city_digs" }),
        ("Aliens", new[] { "alien_star_ups", "aliens_obtained" }),
        ("Breeding", new[] { "breeds_kept", "breeds_discarded" }),
        ("Laboratory", new[] { "lab_research_completed" }),
        ("Arena", new[] { "arena_wins" }),
        ("Invasions", new[] { "invasion_battles" }),
    };

    public static long Total(GameState s, string[] counters)
    {
        long v = 0;
        foreach (var c in counters) v += s.Counter(c);
        return v;
    }
}

/// <summary>One day of a simulation (compact; kept in memory for the smoothness analysis).</summary>
public sealed class DayRecord
{
    public int Day;
    public long Power;
    public int MainLevel;
    public long[] SystemTotals = new long[SystemEvents.Systems.Length];
    public long[] Balances = new long[SimPlayer.Tracked.Length];
    public double[] Earned = new double[SimPlayer.Tracked.Length];
    public double[] Spent = new double[SimPlayer.Tracked.Length];
    public double Minutes, RunMinutes;
    /// <summary>Highest rarity index of any owned part, awakened parts owned, laboratory levels done / total.</summary>
    public int MaxPartRarity, AwakenedParts, LabDone, LabTotal;
    public int Runs;
    public long SpentCents;
    /// <summary>City: patches dug, buildings placed / running, pulls popped from pull buildings (cumulative), pull buildings placed.</summary>
    public int Digs, CityPlaced, CityRunning, PullBuildings;
    public long CityPulls;
    /// <summary>Runs per next upgrade of each <see cref="Endless.Sinks"/> entry (NaN = maxed/locked).</summary>
    public double[] RunsPerUpgrade = Array.Empty<double>();
}

public static class Metrics
{
    public static readonly int[] Checkpoints = { 1, 3, 7, 14, 30, 60, 90, 180, 365, 730, 1095 };

    public static DayRecord Day(SimPlayer p, int day)
    {
        var s = p.S;
        var d = new DayRecord
        {
            Day = day, Power = p.Power, MainLevel = (int)s.Counter(Requirements.MainLevelCounter),
            Minutes = p.MinutesPlayed, RunMinutes = p.MinutesInRuns, Runs = p.RunsPlayed, SpentCents = p.SpentCents,
        };
        d.MaxPartRarity = s.Parts.Items.Count == 0 ? -1 : s.Parts.Items.Values.Max(x => x.Rarity);
        d.AwakenedParts = s.Parts.Items.Values.Count(x => x.Awaken > 0);
        d.LabDone = LabDone(s);
        d.LabTotal = LabTotal(p.C);
        d.RunsPerUpgrade = Endless.RunsPerUpgrade(p);
        d.Digs = s.City.DugPatches.Count;
        d.CityPlaced = s.City.Buildings.Count(b => b.Placed);
        d.CityRunning = s.City.Buildings.Count(b => CitySystem.IsActive(s, p.C, b));
        d.PullBuildings = s.City.Buildings.Count(b => b.Placed && p.C.Blueprint(b.BlueprintId)?.Pull != null);
        d.CityPulls = s.Counter("city_pulls_popped");
        for (int i = 0; i < SystemEvents.Systems.Length; i++) d.SystemTotals[i] = SystemEvents.Total(s, SystemEvents.Systems[i].counters);
        for (int i = 0; i < SimPlayer.Tracked.Length; i++)
        {
            d.Balances[i] = s.Wallet.Get(SimPlayer.Tracked[i]);
            d.Earned[i] = p.Earned[i];
            d.Spent[i] = p.SpentRes[i];
        }
        return d;
    }

    public static int LabDone(GameState s) => s.Lab.Entries.Values.Sum(e => e.Level);
    public static int LabTotal(ContentDb c) => c.LabResearch.Values.Sum(d => d.Levels.Count);

    /// <summary>Share of the tree's nominal research hours completed (the k-th research takes LabTuning.Hours(k)).</summary>
    public static double LabHoursPct(GameState s, ContentDb c)
    {
        int defaults = c.LabResearch.Values.Count(d => d.DefaultResearched);
        int done = Math.Max(0, LabDone(s) - defaults), total = LabTotal(c) - defaults;
        double h = 0, all = 0;
        for (int k = 0; k < total; k++) { all += c.LabTuning.Hours(k); if (k < done) h += c.LabTuning.Hours(k); }
        return all <= 0 ? 0 : 100.0 * h / all;
    }

    /// <summary>Checkpoint snapshot: every per-system metric of the report (ordered name → value).</summary>
    public static List<(string name, double value)> Snapshot(SimPlayer p)
    {
        var s = p.S; var c = p.C; long now = p.Now;
        var m = new List<(string, double)>();
        void A(string n, double v) => m.Add((n, Math.Round(v, 3)));

        var loadout = p.E.BuildLoadout(s, now);
        A("power", loadout.Power);
        int main = (int)s.Counter(Requirements.MainLevelCounter);
        A("main_level", main);
        A("portal", main == 0 ? 0 : (main - 1) / 10 + 1);
        A("overcharged_cleared", Enumerable.Range(1, main).Count(n => RunSystem.Progress(s, c, c.MainLevelId(n)).OverchargedCleared));
        A("burrow_floors", s.Counter("burrow_floors_cleared"));
        int darkMax = (int)s.Counter(RunSystem.DarkSideMaxCounter);
        A("darkside_cleared", Enumerable.Range(1, darkMax).Count(i => RunSystem.Progress(s, c, c.DarkSideLevel(i).Id).Cleared));
        A("milestone_chests", s.Counter("milestone_chests"));
        A("run_levels_records", s.Run.Levels.Count);
        A("archive_bytes", s.Run.MainArchive.Flags.Length + s.Run.DarkArchive.Flags.Length);
        A("worm_level", s.Worm.Level);
        A("talent_tier", s.Worm.TalentTiersDone);

        // Parts (equipped)
        foreach (var slot in new[] { PartSlot.Head, PartSlot.Body, PartSlot.Tail, PartSlot.Legs, PartSlot.LeftArm, PartSlot.RightArm })
            A("part_rarity_" + slot.ToString().ToLowerInvariant(), s.Parts.Equipped.TryGetValue(slot, out var uid) ? s.Parts.Items[uid].Rarity : -1);
        var eq = s.Parts.Equipped.Values.Select(u => s.Parts.Items[u]).ToList();
        A("parts_equipped", eq.Count);
        A("part_rarity_avg", eq.Count == 0 ? 0 : eq.Average(x => x.Rarity));
        A("part_level_avg", eq.Count == 0 ? 0 : eq.Average(x => x.Level));
        A("rune_slots", eq.Sum(x => PartsSystem.RuneSlotCount(c, x)));
        var socketed = eq.SelectMany(x => x.Runes).Where(r => !string.IsNullOrEmpty(r) && s.Parts.Runes.ContainsKey(r)).Select(r => s.Parts.Runes[r]).ToList();
        A("runes_socketed", socketed.Count);
        A("rune_rarity_avg", socketed.Count == 0 ? 0 : socketed.Average(r => r.Rarity));
        A("forge_unlocks", eq.Sum(x => x.ForgedSlots));
        A("parts_awakened", s.Parts.Items.Values.Count(x => x.Awaken > 0));
        A("parts_owned", s.Parts.Items.Count);
        A("part_rarity_best", s.Parts.Items.Count == 0 ? -1 : s.Parts.Items.Values.Max(x => x.Rarity));
        int legendary = c.PartsConfig.Rarities.FindIndex(r => r.Name == "Legendary");
        A("parts_spare_legendary_plus", s.Parts.Items.Values.Count(x => x.Rarity >= legendary && !PartsSystem.IsEquipped(s, x.Uid)));
        A("parts_spare_below_legendary", s.Parts.Items.Values.Count(x => x.Rarity < legendary && !PartsSystem.IsEquipped(s, x.Uid)));
        A("parts_obtained", s.Counter("parts_obtained"));
        A("parts_fed", s.Counter("parts_fed"));
        A("part_climbs", s.Counter("part_climbs"));
        A("part_equips", s.Counter("part_equips"));
        A("part_replications", s.Counter("part_replications"));
        A("bal_metal", s.Wallet.Get(Currencies.Metal));

        // Heroes, mounts
        var heroes = s.Heroes.Owned.Values.ToList();
        A("heroes_owned", heroes.Count);
        A("hero_stars_best", heroes.Count == 0 ? 0 : heroes.Max(h => h.Stars));
        A("hero_stars_avg", heroes.Count == 0 ? 0 : heroes.Average(h => h.Stars));
        A("hero_level_best", heroes.Count == 0 ? 0 : heroes.Max(h => h.Level));
        A("target_hero_stars", HeroSystem.Get(s, SimPlayer.TargetHero)?.Stars ?? 0);
        var mounts = s.Mounts.Owned.Values.ToList();
        A("mounts_owned", mounts.Count);
        A("mount_stars_best", mounts.Count == 0 ? 0 : mounts.Max(x => x.Stars));
        A("mount_level_best", mounts.Count == 0 ? 0 : mounts.Max(x => x.Level));

        // City
        var bps = s.City.Blueprints.Values.ToList();
        A("blueprints_owned", bps.Count);
        A("blueprint_stars_avg", bps.Count == 0 ? 0 : bps.Average(b => b.Stars));
        var built = s.City.Buildings.Where(CitySystem.IsBuilt).ToList();
        var active = built.Where(b => CitySystem.IsActive(s, c, b)).ToList();
        A("buildings", built.Count);
        A("buildings_running", active.Count);
        A("building_level_avg", built.Count == 0 ? 0 : built.Average(b => b.Level));
        A("city_digs", s.City.DugPatches.Count);
        A("city_streets", s.City.Streets.Count);
        A("city_placement_avg", built.Count == 0 ? 0 : built.Average(b => CityLayout.PlacementPct(s, c, b.Uid)));
        A("city_chances_popped", s.Counter("city_chances_popped"));
        foreach (var cur in new[] { Currencies.Biomass, Currencies.Ore, Currencies.Grain, Currencies.Fuel, Currencies.GearCores, Currencies.HeroXp,
                     Currencies.Diamonds, Currencies.BlueprintTokens, Currencies.AlienTokens })
        {
            double ph = 0;
            foreach (var b in active)
                if (c.Blueprint(b.BlueprintId).ProduceCurrency == cur) ph += CitySystem.ProductionPerHour(s, c, b, now);
            A("prod_h_" + cur, ph);
        }

        // Aliens
        var aliens = s.Aliens.Owned.Values.ToList();
        A("aliens_owned", aliens.Count);
        A("alien_stars_avg", aliens.Count == 0 ? 0 : aliens.Average(a => a.Stars));
        A("squad_size", AlienSystem.ActiveSquad(s, c).Count);
        A("breeds", s.Counter("breeds_collected"));
        A("breeds_started", s.Counter("breeds_started"));
        A("breeds_discarded", s.Counter("breeds_discarded"));
        A("alien_star_ups", s.Counter("alien_star_ups"));
        A("alien_stars_best", aliens.Count == 0 ? 0 : aliens.Max(a => a.Stars));
        A("squad_stars_avg", AlienSystem.ActiveSquad(s, c).Count == 0 ? 0 : AlienSystem.ActiveSquad(s, c).Average(id => s.Aliens.Owned[id].Stars));
        A("lab_research", s.Lab.Entries.Values.Sum(e => e.Level));
        A("lab_research_max", c.LabResearch.Values.Sum(d => d.Levels.Count));
        A("lab_pct", 100.0 * LabDone(s) / Math.Max(1, LabTotal(c)));
        A("lab_hours_pct", LabHoursPct(s, c));
        A("lab_slots", LaboratorySystem.Slots(s, c));
        for (int i = 0; i < SimPlayer.SourceTracked.Length; i++)
            for (int j = 0; j < SimPlayer.Sources.Length; j++)
                A("src_" + SimPlayer.SourceTracked[i] + "_" + SimPlayer.Sources[j], p.EarnedBySource[i, j]);

        // Modes, social
        A("arena_league", s.Arena.League);
        A("arena_best_points", s.Arena.BestPoints);
        A("invasion_score", s.Counter("invasion_score"));
        A("gacha_mythics", s.Counter("pulls_mythic"));
        A("pulls_total", s.Counter("pulls_total"));

        // Wallet (every tracked currency) and money.
        foreach (var cur in SimPlayer.Tracked) A("bal_" + cur, s.Wallet.Get(cur));
        A("shards_hero_total", s.Wallet.Balances.Where(k => k.Key.StartsWith("shards_hero_")).Sum(k => k.Value));
        A("shards_alien_total", s.Wallet.Balances.Where(k => k.Key.StartsWith("shards_alien_")).Sum(k => k.Value));
        A("shards_bp_total", s.Wallet.Balances.Where(k => k.Key.StartsWith("shards_bp_")).Sum(k => k.Value));
        A("shards_mount_total", s.Wallet.Balances.Where(k => k.Key.StartsWith("shards_mount_")).Sum(k => k.Value));
        A("spent_usd", p.SpentCents / 100.0);
        A("minutes_per_day", p.DayIndex >= 0 ? p.MinutesPlayed / (p.DayIndex + 1) : 0);
        A("runs_total", p.RunsPlayed);
        A("run_win_rate", p.RunsPlayed == 0 ? 0 : (double)p.RunsWon / p.RunsPlayed);
        return m;
    }
}
