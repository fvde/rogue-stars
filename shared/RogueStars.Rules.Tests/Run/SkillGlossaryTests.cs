using System.Text.RegularExpressions;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using Xunit.Abstractions;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// The skill "?" panel's glossary (owner playtest: explain every mechanic a skill touches, in player language): one set of
/// definitions built from content, mapped to skills from their data.
/// </summary>
public class SkillGlossaryTests(ITestOutputHelper output)
{
    private static readonly ContentDb C = new ContentDb();

    /// <summary>Every skill the run can offer: draftable run / laboratory skills and signatures.</summary>
    private static List<SkillDef> Pool() => C.Skills.Values
        .Where(s => (s.Source == SkillSource.Run || s.Source == SkillSource.Laboratory) && (s.Draftable || s.Signature))
        .OrderBy(s => s.Id, StringComparer.Ordinal).ToList();

    /// <summary>Everything the skill's own text says, at every level (and a signature's awakenings).</summary>
    private static string AllText(SkillDef s)
    {
        var parts = new List<string>();
        int max = Math.Max(1, C.RunTuning.MaxSkillLevelOf(s.Rarity));
        for (int lv = 1; lv <= max; lv++) parts.Add(SkillText.StripMarks(SkillText.FullMarked(C, s, lv)));
        parts.AddRange(SkillText.Awakenings(C, s));
        return string.Join(" ", parts).ToLowerInvariant();
    }

    private static bool Names(string text, string word) => Regex.IsMatch(text, @"(?<![\w])" + Regex.Escape(word) + @"(?![\w])");

    [Fact]
    public void EveryDraftableSkill_ResolvesEveryMechanicItsTextNames()
    {
        var entries = SkillGlossary.Entries(C);
        var missing = new List<string>();
        foreach (var s in Pool())
        {
            var keys = SkillGlossary.Keywords(C, s);
            string text = AllText(s);
            output.WriteLine($"{s.Id}: {string.Join(", ", keys)}");
            foreach (var e in entries)
                if (e.Words.Any(w => Names(text, w)) && !keys.Contains(e.Id))
                    missing.Add($"{s.Id} names {e.Name} but does not resolve it ({text})");
        }
        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    [Fact]
    public void EveryStatusSkill_ResolvesItsStatuses_AndSignaturesExplainThemselves()
    {
        foreach (var s in Pool())
        {
            var keys = SkillGlossary.Keywords(C, s);
            foreach (var t in s.Triggers.Where(t => t.Effect == EffectKind.ApplyStatus && t.Status != StatusId.None))
                Assert.True(SkillGlossary.Entries(C).Any(e => keys.Contains(e.Id) && e.Name == SkillText.StatusName(t.Status)), s.Id + " applies " + t.Status);
            if (s.Signature) Assert.Contains(SkillGlossary.Signature, keys);
        }
    }

    [Theory]
    [InlineData("bleed_frenzy", new[] { SkillGlossary.Bleed, SkillGlossary.Rage })]
    [InlineData("sig_bleed", new[] { SkillGlossary.Bleed, SkillGlossary.Signature })]
    [InlineData("ice_deep_freeze", new[] { SkillGlossary.Chill, SkillGlossary.Freeze })]
    [InlineData("light_spark", new[] { SkillGlossary.Arc, SkillGlossary.Shock })]
    [InlineData("fire_flashpoint", new[] { SkillGlossary.Heat, SkillGlossary.Supernova, SkillGlossary.Rage })]
    [InlineData("mythic_winters_grip", new[] { SkillGlossary.Chill, SkillGlossary.Crit, SkillGlossary.Combo, SkillGlossary.Counter })]
    [InlineData("sig_alien", new[] { SkillGlossary.Aliens, SkillGlossary.Signature })]
    [InlineData("poison_critical", new[] { SkillGlossary.Radiation, SkillGlossary.Exposed })]
    [InlineData("lab_frost_nova", new[] { SkillGlossary.Freeze, SkillGlossary.Chill, SkillGlossary.Rage, SkillGlossary.Crit })]
    public void Keywords_FollowTheSkillsData(string id, string[] expected)
    {
        var keys = SkillGlossary.Keywords(C, C.Skill(id));
        foreach (var k in expected) Assert.Contains(k, keys);
    }

    [Fact]
    public void Keywords_AreInGlossaryOrder_AndExplainReturnsTheEntries()
    {
        var order = SkillGlossary.Entries(C).Select(e => e.Id).ToList();
        foreach (var s in Pool())
        {
            var keys = SkillGlossary.Keywords(C, s);
            Assert.Equal(keys.OrderBy(k => order.IndexOf(k)), keys);
            Assert.Equal(keys, SkillGlossary.Explain(C, s).Select(e => e.Id));
        }
    }

    private static readonly Regex Number = new(@"\d+(?:[.,]\d+)?");
    private static readonly Regex CamelId = new(@"\b[A-Za-z][a-z]+[A-Z]\w*\b");
    private static readonly string[] InternalIds = { "Poison", "AlienSynergy", "Burn", "Parry", "Sidekick:" };

    private static void AssertPlayerText(string text, string where)
    {
        Assert.False(string.IsNullOrWhiteSpace(text), where);
        Assert.DoesNotContain("_", text);
        Assert.DoesNotContain("{", text);
        Assert.DoesNotContain("}", text);
        Assert.False(CamelId.IsMatch(text), where + ": raw id in \"" + text + "\"");
        foreach (var id in InternalIds) Assert.False(Regex.IsMatch(text, @"\b" + Regex.Escape(id) + @"\b"), where + ": " + id + " in \"" + text + "\"");
        foreach (Match m in Number.Matches(text))
        {
            // the project's number rule (NumFmt): whole numbers; one decimal only below 10
            if (!m.Value.Contains('.') && !m.Value.Contains(',')) continue;
            var frac = m.Value.Split('.', ',')[1];
            Assert.True(!m.Value.Contains(',') && frac.Length == 1 && double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture) < 10,
                where + ": number " + m.Value + " in \"" + text + "\"");
        }
    }

    [Fact]
    public void GlossaryTexts_ArePlayerLanguage_WithFormattedNumbers()
    {
        foreach (var e in SkillGlossary.Entries(C))
        {
            output.WriteLine($"{e.Name}: {e.Text}");
            AssertPlayerText(e.Name, e.Id);
            AssertPlayerText(e.Text, e.Id);
            Assert.NotEmpty(e.Words);
        }
        foreach (var s in Pool())
            foreach (var l in SkillGlossary.SynergyLines(C, s))
                AssertPlayerText(l, s.Id);
    }

    [Fact]
    public void GlossaryNumbers_ComeFromContent()
    {
        var c = new ContentDb();
        var bleed = SkillGlossary.Entry(c, SkillGlossary.Bleed)!.Text;
        Assert.Contains(c.Combat.Statuses[StatusId.Bleed].MaxStacks.ToString(), bleed);
        Assert.Contains(NumFmt.OneDecimal(c.Combat.Statuses[StatusId.Bleed].DotAtkPct) + "%", bleed);
        c.Combat.Statuses[StatusId.Bleed].MaxStacks = 12;
        c.Combat.Statuses[StatusId.Shock].DamageTakenPctPerStack = 7.5;
        Assert.Contains("up to 12", SkillGlossary.Entry(c, SkillGlossary.Bleed)!.Text);
        Assert.Contains("+7.5%", SkillGlossary.Entry(c, SkillGlossary.Shock)!.Text);
        Assert.Contains("Any Freeze resets Chill to 0", SkillGlossary.Entry(c, SkillGlossary.Chill)!.Text);
        Assert.Contains(c.Combat.Statuses[StatusId.Heat].BurstAtStacks.ToString(), SkillGlossary.Entry(c, SkillGlossary.Heat)!.Text);
    }

    [Fact]
    public void SynergyLines_ExplainTheFamily_GatesAndMythics()
    {
        var frenzy = SkillGlossary.SynergyLines(C, C.Skill("bleed_frenzy"));
        Assert.Contains(frenzy, l => l.StartsWith("Bleed synergy:"));
        Assert.Contains(frenzy, l => l == "Offered once you own 2 Bleed skills.");
        var shatter = SkillGlossary.SynergyLines(C, C.Skill("mythic_shatter"));
        Assert.Contains(shatter, l => l.StartsWith("Mythic:"));
        Assert.Contains(shatter, l => l == "Offered once your run has a skill that freezes.");
        var sig = SkillGlossary.SynergyLines(C, C.Skill("sig_fire"));
        Assert.Contains(sig, l => l.StartsWith("Awakened ("));
        Assert.Empty(SkillGlossary.SynergyLines(C, C.Skill("gen_power")));
    }
}
