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

    /// <summary>
    /// Skill redesign: parry is gone as a mechanic. No stat package anywhere gives parry chance / parry reduction, no trigger waits
    /// for a parry, and no skill text mentions it (the enum values stay so old saves and battle logs load).
    /// </summary>
    [Fact]
    public void NoContent_UsesParry()
    {
        var bad = AllStatMods(C).Where(x => x.Mod.Stat is StatId.ParryChance or StatId.ParryReduction).Select(x => x.Path).ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
        foreach (var s in C.Skills.Values)
        {
            Assert.DoesNotContain(s.Triggers, t => t.When == TriggerKind.OnParry);
            Assert.DoesNotContain("parr", (SkillText.Full(s) + " " + s.Name + " " + s.Description).ToLowerInvariant());
        }
    }

    /// <summary>
    /// CC pass (owner rule): no Common applies crowd control (a status that skips turns or charms: Stun, Freeze, Delay, Charm) in
    /// any source (run, lab, part, hero, mount, alien, building skills; run and lab skills are stored at their run tier), except the
    /// two the owner set: lab Knockout (every research level) and Head Start. A new Common with CC fails here.
    /// </summary>
    [Fact]
    public void NoCommon_AppliesCrowdControl_ExceptKnockoutAndHeadStart()
    {
        var allowed = new HashSet<string> { "lab_retaliation", "lab_retaliation_2", "lab_retaliation_3", "lab_retaliation_4", "lab_retaliation_5", "time_head_start" };
        var cc = C.Combat.Statuses.Values.Where(d => d.SkipsTurn || d.Charms).Select(d => d.Id).ToHashSet();
        Assert.Superset(new HashSet<StatusId> { StatusId.Stun, StatusId.Freeze, StatusId.Delay, StatusId.Charm }, cc);
        bool AppliesCc(SkillDef s) => s.Triggers.Any(t => t.Effect == EffectKind.ApplyStatus && cc.Contains(t.Status));
        var enemySkills = C.Enemies.Values.SelectMany(e => e.SkillIds).ToHashSet(); // enemy skills are no player tier
        var commons = C.Skills.Values.Where(s => !enemySkills.Contains(s.Id) && s.Rarity == Rarity.Common).ToList();
        Assert.True(commons.Count > 30, commons.Count.ToString());
        var bad = commons.Where(s => AppliesCc(s) && !allowed.Contains(s.Id)).Select(s => s.Id + " (" + s.Source + ")").ToList();
        Assert.True(bad.Count == 0, "Commons with crowd control:\n" + string.Join("\n", bad));
        Assert.All(allowed, id => Assert.True(C.Skill(id) is { Rarity: Rarity.Common } s && AppliesCc(s), id)); // the allowlist stays exact
    }

    /// <summary>CC pass: Ice gear and Ice family skills (parts, hero traits, mount weapons) chill instead of freezing; only an Ice
    /// part's ultimate (Frost Apex) still freezes.</summary>
    [Fact]
    public void IceGear_Chills_OnlyTheIcePartUltimateFreezes()
    {
        var gear = C.Skills.Values.Where(s => s.Source is SkillSource.Part or SkillSource.Hero or SkillSource.Mount).ToList();
        var freezing = gear.Where(s => s.Triggers.Any(t => t.Effect == EffectKind.ApplyStatus && t.Status == StatusId.Freeze)).ToList();
        Assert.NotEmpty(freezing);
        Assert.All(freezing, s => Assert.True(s.Source == SkillSource.Part && s.Family == SkillFamily.Ice && s.Id.EndsWith("_sk5", StringComparison.Ordinal), s.Id));
        Assert.Contains(gear, s => s.Family == SkillFamily.Ice && s.Triggers.Any(t => t.Effect == EffectKind.ApplyStatus && t.Status == StatusId.Chill));
    }

    /// <summary>Every trigger gate (SkillTrigger.If) and conditional bonus gate in the content parses (BattleConditions).</summary>
    [Fact]
    public void EveryTriggerCondition_Parses()
    {
        foreach (var s in C.Skills.Values)
            foreach (var t in s.Triggers)
                if (!string.IsNullOrWhiteSpace(t.If)) Assert.True(BattleConditions.IsValid(t.If), s.Id + ": " + t.If);
    }
}
