using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// The owner's review doc of every Run Quest chain (docs/design/run-quests/&lt;quest-id&gt;.md), generated from the content so it
/// never drifts: the character concept, the full story graph (every node: type, text, options and where they lead, prices,
/// battles, boons) and the ratio table. Regenerate with ROGUESTARS_WRITE_DOCS=1 (the up-to-date test fails otherwise).
/// </summary>
public class RunQuestDocsGenerator
{
    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "RogueStars.slnx"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    public static string PathOf(string questId) => Path.Combine(RepoRoot(), "docs", "design", "run-quests", questId + ".md");

    [Fact]
    public void ReviewDocs_AreUpToDate()
    {
        foreach (var q in ContentDb.Default.RunQuestOrder)
        {
            string md = Markdown(q, ContentDb.Default);
            if (Environment.GetEnvironmentVariable("ROGUESTARS_WRITE_DOCS") == "1") File.WriteAllText(PathOf(q.Id), md);
            Assert.True(File.Exists(PathOf(q.Id)), "missing review doc for " + q.Id);
            Assert.True(File.ReadAllText(PathOf(q.Id)) == md, q.Id + ".md is stale: run the Rules tests with ROGUESTARS_WRITE_DOCS=1");
        }
    }

    private static string Kind(QuestNodeKind k) => k switch
    {
        QuestNodeKind.Story => "Story", QuestNodeKind.Battle => "Battle", QuestNodeKind.Sacrifice => "Sacrifice", _ => "Boon",
    };

    private static string Cost(QuestCostDef c) => c.Kind switch
    {
        QuestCostKind.Coins => c.Amount + " run coins",
        QuestCostKind.CurrentHpPct => c.Amount + "% of current HP",
        QuestCostKind.LegendarySkill => "one random Legendary skill",
        QuestCostKind.MythicSkill => "one random Mythic skill",
        QuestCostKind.AllRunSkills => "ALL run skills (signatures stay)",
        _ => "-",
    };

    private static string Detail(QuestNodeDef n, ContentDb c) => n.Kind switch
    {
        QuestNodeKind.Battle => $"Hard battle: the level's miniboss at +{c.QuestTuning.BattleBasePct + n.Battle!.BonusPct:0}% HP/ATK"
            + (n.Battle.Escorts > 0 ? $" with {n.Battle.Escorts} elite{(n.Battle.Escorts > 1 ? "s" : "")}" : "") + ". Declined: the story waits. Lost: the run ends (revive offer) and the story waits.",
        QuestNodeKind.Sacrifice => "Price: " + Cost(n.Cost!) + ". Refused or not affordable: the story waits.",
        QuestNodeKind.Boon => $"Boon: +{n.Boon!.CoinsPerGain} run coin on every run-coin gain, account-wide, forever.",
        _ => "Story choice: always moves on.",
    };

    public static string Markdown(RunQuestDef q, ContentDb c)
    {
        var sb = new StringBuilder();
        var t = c.QuestTuning;
        var nodes = q.Nodes.Values.OrderBy(n => n.Chapter).ThenBy(n => n.Id, StringComparer.Ordinal).ToList();
        int paths = CountPaths(q);
        sb.AppendLine($"# {q.Character}, {q.Epithet}: Run Quest review");
        sb.AppendLine();
        sb.AppendLine($"Generated from `shared/RogueStars.Rules/Content/Quests/Quest{char.ToUpperInvariant(q.Id[0]) + q.Id.Substring(1)}.cs` by `RunQuestDocsGenerator`; how quests are built: [recipe.md](recipe.md).");
        sb.AppendLine("Comment on any node by its id (for example `m05a`).");
        sb.AppendLine();
        sb.AppendLine("## The character");
        sb.AppendLine();
        sb.AppendLine($"- **Name:** {q.Character}, {q.Epithet}");
        sb.AppendLine($"- **Look:** {q.Look}");
        sb.AppendLine($"- **Voice:** {q.Voice}");
        sb.AppendLine($"- **The story:** {q.Premise}");
        sb.AppendLine($"- **Portrait:** `{q.Portrait}` (art package `tools/art/packages/quest-{q.Id}.json`; the client draws a placeholder until the art exists).");
        sb.AppendLine($"- **Account bonus:** {q.BonusName}. Each boon adds +1 run coin to every run-coin gain, for the whole account; after the chain: +{nodes.Where(n => n.Kind == QuestNodeKind.Boon).GroupBy(n => n.Chapter).Count()} on every coin gain.");
        sb.AppendLine();
        sb.AppendLine("## How it plays");
        sb.AppendLine();
        sb.AppendLine($"- Part of the game from the start: no level gate, no unlock entry. Not on the tutorial run (main level 1), not in the Burrow.");
        sb.AppendLine($"- About one run in five meets {q.Character}: a plain {t.ChancePct:0}% chance per run, decided at run start (no pity, no streak rules: two meetings in a row or a long drought can happen; mean 5 runs).");
        sb.AppendLine($"- At most one meeting per run: a visitor tile on the main path of the second board segment (every path passes it, it stops the worm).");
        sb.AppendLine($"- {q.Chapters} chapters per playthrough (the player only ever sees \"Chapter N\", never the total). Story choices always move on; battles and prices can be declined or not affordable, then the story waits on the same node until the next meeting.");
        sb.AppendLine("- A lost quest battle ends the run like any lost battle (the normal defeat with its revive offer; a revive retries the battle and a win moves the story on). The story waits on the battle.");
        sb.AppendLine("- A Legendary / Mythic skill price takes a random owned skill of that tier (the run's random stream); with none of that tier the price cannot be paid and the story waits.");
        sb.AppendLine($"- After the last chapter {q.Character} keeps visiting at the same rate with a friendly epilogue scene and a small reward (no battles, no prices).");
        sb.AppendLine();
        sb.AppendLine("## Ratios (every playthrough)");
        sb.AppendLine();
        var path = OnePath(q);
        sb.AppendLine("| Type | Per playthrough | Share | Target |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var (k, target) in new[] { (QuestNodeKind.Story, "~50%"), (QuestNodeKind.Battle, "~20%"), (QuestNodeKind.Sacrifice, "~20%"), (QuestNodeKind.Boon, "~10%") })
        {
            int n = path.Count(x => x.Kind == k);
            sb.AppendLine($"| {Kind(k)} | {n} | {100.0 * n / path.Count:0}% | {target} |");
        }
        sb.AppendLine($"| **Total** | **{path.Count}** | | |");
        sb.AppendLine();
        sb.AppendLine($"{q.Nodes.Count} nodes in all; {paths} different playthroughs (branches re-merge, every one is {q.Chapters} chapters with the same ratios and the same {q.BonusName} at the end).");
        sb.AppendLine();
        sb.AppendLine("## Story map");
        sb.AppendLine();
        sb.AppendLine("| Ch | Type | Node(s) |");
        sb.AppendLine("|---|---|---|");
        foreach (var g in nodes.GroupBy(n => n.Chapter))
            sb.AppendLine($"| {g.Key} | {string.Join(" / ", g.Select(n => Kind(n.Kind)).Distinct())} | {string.Join(" · ", g.Select(n => $"`{n.Id}` {n.Title}"))} |");
        sb.AppendLine();
        sb.AppendLine("## Every node");
        foreach (var n in nodes)
        {
            sb.AppendLine();
            sb.AppendLine($"### Ch {n.Chapter} · `{n.Id}` · {Kind(n.Kind)}: {n.Title}");
            sb.AppendLine();
            sb.AppendLine("> " + n.Text);
            sb.AppendLine();
            sb.AppendLine(Detail(n, c));
            sb.AppendLine();
            foreach (var o in n.Options)
            {
                string to = o.Action == QuestOptionAction.Wait ? "the story waits here" : o.Next == null ? "**the end**" : $"`{o.Next}` {q.Node(o.Next)!.Title}";
                string act = o.Action switch { QuestOptionAction.Fight => " (fight; won →)", QuestOptionAction.Pay => " (pay →)", _ => "" };
                sb.AppendLine($"- **{o.Text}**{act} → {to}" + (string.IsNullOrEmpty(o.Journal) ? "" : $"  \n  Journal: *{o.Journal}*"));
            }
        }
        sb.AppendLine();
        sb.AppendLine("## Epilogue scenes (after the chain, in rotation)");
        sb.AppendLine();
        sb.AppendLine("| Scene | Text | Reward |");
        sb.AppendLine("|---|---|---|");
        foreach (var e in q.Epilogues)
        {
            string reward = e.Kind switch
            {
                QuestEpilogueKind.Coins => $"+{e.Amount} run coins (+{q.BonusName})",
                QuestEpilogueKind.HealPct => $"heal {e.Amount}% HP",
                QuestEpilogueKind.SkillPick => "a free skill pick (1 of 3)",
                _ => $"+{e.Amount}% max HP for the run",
            };
            sb.AppendLine($"| {e.Title} | {e.Text} | {reward} |");
        }
        return sb.ToString();
    }

    private static int CountPaths(RunQuestDef q)
    {
        int Walk(string id)
        {
            var n = q.Node(id)!;
            int sum = 0;
            foreach (var next in n.Options.Where(o => o.Action != QuestOptionAction.Wait).Select(o => o.Next).Distinct())
                sum += next == null ? 1 : Walk(next);
            return sum;
        }
        return Walk(q.StartNode);
    }

    private static List<QuestNodeDef> OnePath(RunQuestDef q)
    {
        var list = new List<QuestNodeDef>();
        var n = q.Node(q.StartNode);
        while (n != null)
        {
            list.Add(n);
            var next = n.Options.First(o => o.Action != QuestOptionAction.Wait).Next;
            n = next == null ? null : q.Node(next);
        }
        return list;
    }
}
