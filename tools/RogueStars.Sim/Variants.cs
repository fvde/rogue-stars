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
                        if (t.Effect == EffectKind.Summon) continue;
                        t.Value *= k;
                        if (t.Stat != null) { t.Stat.Flat *= k; t.Stat.Percent *= k; }
                        if (t.Effect == EffectKind.ApplyStatus) t.Chance *= k;
                    }
                }
            return;
        }
        if (v.StartsWith("lategl_"))
        {
            // Late glacier strength % (CampaignDef.LateFamilyStrengthPct[1]): generated portals read it lazily; materialized
            // glacier portals from LateFamilyFromPortal on are patched here.
            double pct = double.Parse(v.Substring("lategl_".Length), System.Globalization.CultureInfo.InvariantCulture);
            c.Campaign.LateFamilyStrengthPct[1] = pct;
            for (int i = c.Campaign.LateFamilyFromPortal; i < c.Campaign.Portals; i++)
            {
                if (i % 4 != 1) continue;
                foreach (var id in c.MainPortal(i).LevelIds)
                {
                    var m = c.RunLevel(id).Modifiers;
                    double mult = (1 + m.EnemyHpPct / 100.0) * (1 + pct / 100.0);
                    m.EnemyHpPct = m.EnemyAtkPct = Math.Round((mult - 1) * 100, 2);
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
                c.Blueprints["bp_biomass_farm"].BaseRatePerHour = 60; c.Blueprints["bp_spore_vats"].BaseRatePerHour = 150;
                break;
            default: throw new ArgumentException("unknown variant " + v);
        }
    }
}
