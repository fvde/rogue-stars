using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Sim;

/// <summary>Named content variants for tuning experiments (--variant a,b): each mutates a fresh ContentDb.</summary>
public static class Variants
{
    public static ContentDb Create(string spec)
    {
        var c = SimContent.Create();
        foreach (var v in (spec ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) Apply(c, v.Trim());
        return c;
    }

    private static void Apply(ContentDb c, string v)
    {
        if (v.StartsWith("part_skills_x"))
        {
            double k = double.Parse(v.Substring("part_skills_x".Length), System.Globalization.CultureInfo.InvariantCulture);
            foreach (var d in c.PartsConfig.Defs.Values)
                foreach (var id in d.SkillIds.Concat(d.AwakenedSkillIds))
                {
                    var s = c.Skills[id];
                    foreach (var m in s.Stats) { m.Flat *= k; m.Percent *= k; }
                    foreach (var t in s.Triggers)
                    {
                        t.Value *= k;
                        if (t.Stat != null) { t.Stat.Flat *= k; t.Stat.Percent *= k; }
                        if (t.Effect == EffectKind.ApplyStatus) t.Chance *= k;
                    }
                }
            return;
        }
        if (v.StartsWith("gate_"))
        {
            // Onboarding experiment: gate_<featureId>_L<level> moves a feature's main-level gate (e.g. gate_worm_level_L3).
            int at = v.LastIndexOf("_L", StringComparison.Ordinal);
            var f = c.Feature(v.Substring(5, at - 5)) ?? throw new ArgumentException("unknown feature in " + v);
            foreach (var r in f.Requires.Items) if (r.Kind == RequirementKind.MainLevel) r.Target = long.Parse(v.Substring(at + 2));
            return;
        }
        if (v.StartsWith("stash_"))
        {
            // Reward stash cap experiment: stash_<currency or item kind>_<cap> (-1 = no cap), e.g. stash_biomass_10000.
            int at = v.LastIndexOf('_');
            string id = v.Substring(6, at - 6);
            var use = c.CurrencyUse(id) ?? c.ItemUse(id) ?? throw new ArgumentException("unknown reward in " + v);
            use.StashCap = long.Parse(v.Substring(at + 1));
            return;
        }
        if (v.StartsWith("open_"))
        {
            // open_<currency or item kind>: that reward always has a use (isolates one entry of the reward gate).
            string id = v.Substring(5);
            (c.CurrencyUse(id) ?? c.ItemUse(id) ?? throw new ArgumentException("unknown reward in " + v)).UsedWhen.Items.Clear();
            return;
        }
        if (v == "no_reward_gate")
        {
            // Before "rewards follow use": every reward is usable at once (the old starting wallet is not restored).
            foreach (var u in c.RewardUses) u.UsedWhen.Items.Clear();
            return;
        }
        if (v.StartsWith("alpha_"))
        {
            // Endless campaign reward exponent α (generated portals and Dark Side levels are built lazily, after this).
            c.Campaign.EndlessRewardExponent = double.Parse(v.Substring("alpha_".Length), System.Globalization.CultureInfo.InvariantCulture);
            return;
        }
        switch (v)
        {
            case "no_lab_stats":
                foreach (var d in c.LabResearch.Values) foreach (var l in d.Levels) l.Stats.Clear();
                break;
            case "no_part_skills":
                foreach (var d in c.PartsConfig.Defs.Values) { d.SkillIds.Clear(); d.AwakenedSkillIds.Clear(); }
                break;
            case "old_biomass": // materialized portals only (levels 1–500)
                foreach (var l in c.RunLevels.Values)
                    foreach (var r in l.CompletionRewards) if (r.Id == Currencies.Biomass) r.Amount /= 2;
                foreach (var t in c.RunLootTables.Values)
                    foreach (var e in t.Entries) if (e.Id == Currencies.Biomass) { e.Min /= 2; e.Max /= 2; }
                c.Blueprints["bp_biomass_farm"].BaseRatePerHour = 60;
                break;
            case "city_rates_x2": // City design §6 "Balance": doubles every producer's placed rate (the open ×2 question)
                foreach (var d in c.Blueprints.Values) if (d.ProduceCurrency != null) d.BaseRatePerHour *= 2;
                break;
            default: throw new ArgumentException("unknown variant " + v);
        }
    }
}
