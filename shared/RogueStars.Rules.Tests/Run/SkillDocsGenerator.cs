using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Regenerates docs/design/run-skill-levels.md from ContentDb.Default when ROGUESTARS_WRITE_DOCS=1 (a no-op otherwise):
/// <c>ROGUESTARS_WRITE_DOCS=1 dotnet test shared/RogueStars.Rules.Tests --filter SkillDocsGenerator</c>.
/// </summary>
public class SkillDocsGenerator
{
    [Fact]
    public void WriteRunSkillLevels()
    {
        var md = Markdown(ContentDb.Default);
        Assert.Contains("| Ignite |", md);
        if (Environment.GetEnvironmentVariable("ROGUESTARS_WRITE_DOCS") != "1") return;
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "RogueStars.slnx"))) dir = Path.GetDirectoryName(dir)!;
        File.WriteAllText(Path.Combine(dir!, "docs", "design", "run-skill-levels.md"), md);
    }

    private static string Source(SkillDef s) => s.Source + (s.Family != SkillFamily.None ? " · " + SkillText.FamilyName(s.Family) : "");

    public static string Markdown(ContentDb c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Run skill levels (generated)");
        sb.AppendLine();
        sb.AppendLine("Every signature, draftable run and laboratory skill (each research level of a lab node is its own skill) with its Lv1 text and");
        sb.AppendLine("each in-run upgrade as the cards show it (`SkillText.Full` / `SkillText.UpgradeFull`). Generated from `ContentDb.Default` by");
        sb.AppendLine("`SkillDocsGenerator` (`ROGUESTARS_WRITE_DOCS=1 dotnet test shared/RogueStars.Rules.Tests --filter SkillDocsGenerator`);");
        sb.AppendLine("rules in docs/design/skill-conventions.md.");
        sb.AppendLine();
        sb.AppendLine(SkillText.LevelUpRule(c) + ".");
        sb.AppendLine();
        sb.AppendLine("## Signatures");
        sb.AppendLine();
        sb.AppendLine("One per synergy; the run starts with one (1 of 3 offered). Never drafted, bought or levelled by drafts: it awakens at "
            + string.Join(" / ", c.RunTuning.SignatureAwakenAt) + " owned skills of its synergy.");
        sb.AppendLine();
        sb.AppendLine("| Signature | Id | Synergy | Unlock | Text | Awakened |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var s in c.Skills.Values.Where(x => x.Signature).OrderBy(x => c.RunTuning.StarterSynergies.Contains(x.Family) ? 0 : 1).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            var pkg = c.LabResearch.Values.FirstOrDefault(d => d.Kind == LabNodeKind.Package && d.Family == s.Family);
            sb.AppendLine($"| {s.Name} | `{s.Id}` | {SkillText.FamilyName(s.Family)} | {(pkg == null ? "start" : pkg.Name)} | {SkillText.Full(s)} | "
                + string.Join(" ", SkillText.Awakenings(c, s).Select(l => l.Substring(l.IndexOf(':') + 2))) + " |");
        }
        sb.AppendLine();
        sb.AppendLine("## Draftable skills");
        sb.AppendLine();
        sb.AppendLine("| Skill | Id | Tier | Source | Lv | Text (upgrade inline) |");
        sb.AppendLine("|---|---|---|---|---|---|");
        var pool = c.Skills.Values.Where(s => s.Draftable && (s.Source == SkillSource.Run || s.Source == SkillSource.Laboratory))
            .OrderBy(s => s.Source).ThenBy(s => s.Family).ThenBy(s => s.Rarity).ThenBy(s => s.Name, StringComparer.Ordinal);
        foreach (var s in pool)
        {
            sb.AppendLine($"| {s.Name} | `{s.Id}` | {s.Rarity} | {Source(s)} | 1 | {SkillText.Full(s)} |");
            for (int lv = 2; lv <= c.RunTuning.MaxSkillLevelOf(s.Rarity); lv++)
                sb.AppendLine($"| | | | | {lv} | {SkillText.UpgradeFull(c, s, lv - 1, lv)} |");
        }
        return sb.ToString();
    }
}
