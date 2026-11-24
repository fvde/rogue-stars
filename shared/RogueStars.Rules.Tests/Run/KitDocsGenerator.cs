using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Regenerates docs/design/heroes-mounts.md (the hero and mount kit table) from ContentDb.Default when ROGUESTARS_WRITE_DOCS=1
/// (a no-op otherwise): <c>ROGUESTARS_WRITE_DOCS=1 dotnet test shared/RogueStars.Rules.Tests --filter KitDocsGenerator</c>.
/// </summary>
public class KitDocsGenerator
{
    [Fact]
    public void WriteHeroesMounts()
    {
        var md = Markdown(ContentDb.Default);
        Assert.Contains("| Starfall |", md);
        if (Environment.GetEnvironmentVariable("ROGUESTARS_WRITE_DOCS") != "1") return;
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "RogueStars.slnx"))) dir = Path.GetDirectoryName(dir)!;
        File.WriteAllText(Path.Combine(dir!, "docs", "design", "heroes-mounts.md"), md);
    }

    public static string Markdown(ContentDb c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Heroes and mounts (generated)");
        sb.AppendLine();
        sb.AppendLine("The 9 hero kits and 9 mount kits as the screens show them (`HeroDef.Kit`, `MountDef.Kit`), generated from `ContentDb.Default` by");
        sb.AppendLine("`KitDocsGenerator` (`ROGUESTARS_WRITE_DOCS=1 dotnet test shared/RogueStars.Rules.Tests --filter KitDocsGenerator`). Design: the owner-approved");
        sb.AppendLine("Hero and Mount Mechanics page (rev 2); engine rules in [skill-conventions.md](skill-conventions.md) (\"Hero and mount kits\").");
        sb.AppendLine();
        sb.AppendLine("- **Heroes:** 1★ Trait + Ability, 3★ a number, 5★ / 7★ clauses, 10★ capstone. Not tied to a synergy. Hero level and stars only raise");
        sb.AppendLine("  worm stats, one shared budget for every hero (HP 100 / ATK 10 +5% at 1★, ×1 … ×2.85 by star, +10 HP / +2 ATK per level).");
        sb.AppendLine("- **Mounts:** a vehicle on a reload, no stats: one passive + one active (a shot every N rounds, from round 1), texts at 1★ / 3★ /");
        sb.AppendLine("  5★ / A3 / A5. Damage = % of the worm's ATK × (1 + mount damage %): +" + c.MountConfig.DamagePctPerStep + "% at each step between (2★, 4★, A1, A2, A4), +"
            + c.MountConfig.DamagePctPerLevel + "% per Fuel level, + the collection (+1% per star / awakening of every owned mount) and lab Hangar Tuning.");
        sb.AppendLine("  Rider link: the mount's statuses are the worm's and its crits fire the worm's on-crit skills (base crit damage: no stats).");
        sb.AppendLine();
        sb.AppendLine("## Heroes");
        sb.AppendLine();
        foreach (var d in c.HeroConfig.Defs.Values.OrderBy(d => d.Id == "hero_star_knight" ? 0 : 1).ThenBy(d => d.Name, StringComparer.Ordinal))
        {
            sb.AppendLine($"### {d.Name.Replace(" Worm", "")} (`{d.Id}`)");
            sb.AppendLine();
            sb.AppendLine($"{d.Tagline} *{d.Meter}.*");
            sb.AppendLine();
            sb.AppendLine("| Star | Slot | Name | Text | Skill |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var k in d.Kit)
            {
                var step = d.Traits.FirstOrDefault(t => t.Star == k.Star && c.Skill(t.SkillId)!.Name == k.Name);
                sb.AppendLine($"| {k.StepLabel} | {k.Slot} | {k.Name} | {k.Text} | {(step != null ? "`" + step.SkillId + "`" : "")} |");
            }
            sb.AppendLine();
        }
        sb.AppendLine("## Mounts");
        sb.AppendLine();
        foreach (var d in c.MountConfig.Defs.Values.OrderBy(d => d.Id == "mount_scout_saucer" ? 0 : 1).ThenBy(d => d.Name, StringComparer.Ordinal))
        {
            sb.AppendLine($"### {d.Name} (`{d.Id}`)");
            sb.AppendLine();
            sb.AppendLine($"{d.Tagline} Reload {string.Join(" / ", d.Reloads)} (1★ / 3★ / 5★ / A3 / A5).");
            sb.AppendLine();
            sb.AppendLine("| Step | Passive | Active |");
            sb.AppendLine("|---|---|---|");
            var p = d.Kit.Where(k => k.Slot == "Passive").ToList();
            var a = d.Kit.Where(k => k.Slot == "Active").ToList();
            for (int i = 0; i < p.Count; i++) sb.AppendLine($"| {p[i].StepLabel} | **{p[i].Name}:** {p[i].Text} | **{a[i].Name}:** {a[i].Text} |");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
