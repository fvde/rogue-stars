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
        switch (v)
        {
            case "no_lab_stats":
                foreach (var d in c.LabResearch.Values) foreach (var l in d.Levels) l.Stats.Clear();
                break;
            case "no_part_skills":
                foreach (var d in c.PartsConfig.Defs.Values) { d.SkillIds.Clear(); d.AwakenedSkillIds.Clear(); }
                break;
            case "old_biomass":
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
