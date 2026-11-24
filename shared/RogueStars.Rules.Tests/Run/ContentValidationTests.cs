using System.Collections;
using System.Reflection;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Whole-content validation (dead-effects pass): content that reads as a bonus but does nothing is caught here for every
/// source at once, not one system at a time.
/// </summary>
public class ContentValidationTests
{
    private static readonly ContentDb C = ContentDb.Default;

    /// <summary>Every <see cref="StatMod"/> reachable from the content, with a readable path (reflection over all fields).</summary>
    internal static List<(string Path, StatMod Mod)> AllStatMods(ContentDb c)
    {
        var found = new List<(string, StatMod)>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Walk(object? o, string path, int depth)
        {
            if (o == null || depth > 12) return;
            var t = o.GetType();
            if (t.IsPrimitive || t.IsEnum || o is string || o is decimal || t.IsPointer) return;
            if (!t.IsValueType && !seen.Add(o)) return;
            if (o is StatMod m) { found.Add((path, m)); return; }
            if (o is IDictionary d)
            {
                foreach (DictionaryEntry kv in d) Walk(kv.Value, path + "[" + kv.Key + "]", depth + 1);
                return;
            }
            if (o is IEnumerable e)
            {
                int i = 0;
                foreach (var x in e) { Walk(x, path + "[" + (x is SkillDef s ? s.Id : i.ToString()) + "]", depth + 1); i++; }
                return;
            }
            if (t.Namespace == null || !t.Namespace.StartsWith("RogueStars", StringComparison.Ordinal)) return;
            for (var tt = t; tt != null && tt != typeof(object); tt = tt.BaseType)
                foreach (var f in tt.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    Walk(f.GetValue(o), path + "." + f.Name.Replace("<", "").Replace(">k__BackingField", ""), depth + 1);
        }
        Walk(c, "Content", 0);
        return found;
    }

    /// <summary>
    /// A % bonus multiplies the stat's base. Only HP / ATK / DEF / Speed have a base on every fighter; every other stat
    /// (crit, dodge, element damage, alien / mount power, rage gain, run XP …) starts at 0 (or a few points), so "+20%" of it
    /// does nothing. Those are point stats and must be flat everywhere in the content: skills of every source, parts, runes,
    /// lab nodes, heroes, mounts, aliens, buildings, enemies, run tiles and events.
    /// </summary>
    [Fact]
    public void NoPercentBonus_OnAPointStat_AnywhereInTheContent()
    {
        var mods = AllStatMods(C);
        Assert.True(mods.Count > 500, mods.Count.ToString()); // the walk reaches the content
        var bad = mods.Where(x => x.Mod.Percent != 0 && SkillText.IsPointStat(x.Mod.Stat))
            .Select(x => x.Path + ": " + x.Mod.Stat + " " + x.Mod.Percent + "%").Distinct().ToList();
        Assert.True(bad.Count == 0, bad.Count + " percent bonuses on point stats:\n" + string.Join("\n", bad.Take(200)));
    }
}
