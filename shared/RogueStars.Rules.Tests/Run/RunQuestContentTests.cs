using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using Xunit.Abstractions;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Validates every Run Quest chain against the recipe (docs/design/run-quests/recipe.md): the graph (reachable, no dead ends,
/// every path exactly Chapters encounters, chapters in order), node kinds and their options, the type ratios on every path
/// (50 / 20 / 20 / 10 ± tolerance, exactly the boons the account bonus needs), round numbers, text, the epilogue scenes, and
/// that every requirement can be met by a player at the unlock level.
/// </summary>
public class RunQuestContentTests(ITestOutputHelper output)
{
    private static readonly ContentDb C = ContentDb.Default;

    public static IEnumerable<object[]> Quests() => C.RunQuestOrder.Select(q => new object[] { q.Id });

    /// <summary>Every distinct node sequence from the start to the end of the chain (story options to the same node count once).</summary>
    private static List<List<QuestNodeDef>> Paths(RunQuestDef q)
    {
        var paths = new List<List<QuestNodeDef>>();
        void Walk(QuestNodeDef n, List<QuestNodeDef> so)
        {
            so.Add(n);
            var next = n.Options.Where(o => o.Action != QuestOptionAction.Wait).Select(o => o.Next).Distinct().ToList();
            foreach (var id in next)
            {
                if (id == null) paths.Add(new List<QuestNodeDef>(so));
                else Walk(q.Node(id)!, so);
            }
            so.RemoveAt(so.Count - 1);
        }
        Walk(q.Node(q.StartNode)!, new List<QuestNodeDef>());
        return paths;
    }

    [Fact]
    public void AtLeastOneChain_IsRegistered_WithTheQuestTile()
    {
        Assert.NotEmpty(C.RunQuestOrder);
        var tile = C.BoardTiles[ContentDb.QuestTileId];
        Assert.Equal(TileCategory.Quest, tile.Category);
        Assert.True(tile.ForceStop);
        // never from a tile pool: only scheduled runs place it
        Assert.DoesNotContain(C.TilePools.Values.SelectMany(p => p), w => w.Id == ContentDb.QuestTileId);
    }

    [Theory]
    [MemberData(nameof(Quests))]
    public void Graph_Reachable_NoDeadEnds_ChaptersInOrder_EveryPathIsChaptersLong(string id)
    {
        var q = C.RunQuest(id)!;
        Assert.NotNull(q.Node(q.StartNode));
        Assert.Equal(1, q.Node(q.StartNode)!.Chapter);
        foreach (var n in q.Nodes.Values)
        {
            Assert.Equal(n.Id, q.Nodes.First(kv => kv.Value == n).Key);
            Assert.InRange(n.Chapter, 1, q.Chapters);
            var advancing = n.Options.Where(o => o.Action != QuestOptionAction.Wait).ToList();
            Assert.True(advancing.Count > 0, n.Id + " is a dead end");
            foreach (var o in advancing)
            {
                if (o.Next == null) Assert.True(n.Chapter == q.Chapters, n.Id + " ends the chain before the last chapter");
                else
                {
                    var to = q.Node(o.Next);
                    Assert.True(to != null, n.Id + " → missing " + o.Next);
                    Assert.True(to!.Chapter == n.Chapter + 1, n.Id + " → " + o.Next + " skips or repeats a chapter");
                }
            }
        }
        // reachability
        var seen = new HashSet<string>();
        var stack = new Stack<string>(new[] { q.StartNode });
        while (stack.Count > 0)
        {
            var nid = stack.Pop();
            if (!seen.Add(nid)) continue;
            foreach (var o in q.Node(nid)!.Options) if (o.Next != null) stack.Push(o.Next);
        }
        Assert.Equal(q.Nodes.Keys.OrderBy(x => x), seen.OrderBy(x => x));
        var paths = Paths(q);
        output.WriteLine($"{id}: {q.Nodes.Count} nodes, {paths.Count} distinct paths of {q.Chapters} encounters");
        Assert.All(paths, p => Assert.Equal(q.Chapters, p.Count));
        Assert.InRange(q.Chapters, 25, 35); // "roughly 30 encounters"
    }

    [Theory]
    [MemberData(nameof(Quests))]
    public void EveryPath_HasTheRecipeRatios(string id)
    {
        var q = C.RunQuest(id)!;
        foreach (var p in Paths(q))
        {
            double n = p.Count;
            double Pct(QuestNodeKind k) => 100.0 * p.Count(x => x.Kind == k) / n;
            string path = string.Join(" ", p.Select(x => x.Id));
            Assert.True(Math.Abs(Pct(QuestNodeKind.Story) - 50) <= 5, "story " + Pct(QuestNodeKind.Story) + "%: " + path);
            Assert.True(Math.Abs(Pct(QuestNodeKind.Battle) - 20) <= 5, "battle " + Pct(QuestNodeKind.Battle) + "%: " + path);
            Assert.True(Math.Abs(Pct(QuestNodeKind.Sacrifice) - 20) <= 5, "sacrifice " + Pct(QuestNodeKind.Sacrifice) + "%: " + path);
            Assert.True(Math.Abs(Pct(QuestNodeKind.Boon) - 10) <= 5, "boon " + Pct(QuestNodeKind.Boon) + "%: " + path);
            // every playthrough builds the same account bonus (Mothwick: +3 coins per gain)
            Assert.Equal(3, p.Where(x => x.Kind == QuestNodeKind.Boon).Sum(x => x.Boon!.CoinsPerGain));
            // the story opens with the character and ends on a boon
            Assert.Equal(QuestNodeKind.Story, p[0].Kind);
            Assert.Equal(QuestNodeKind.Boon, p[p.Count - 1].Kind);
            // no two battles or two prices in a row (pacing)
            for (int i = 1; i < p.Count; i++)
                Assert.False(p[i].Kind != QuestNodeKind.Story && p[i].Kind == p[i - 1].Kind, "two " + p[i].Kind + " in a row: " + p[i - 1].Id + ", " + p[i].Id);
        }
    }

    [Theory]
    [MemberData(nameof(Quests))]
    public void Nodes_HaveTheirKindsOptions_RoundNumbers_AndText(string id)
    {
        var q = C.RunQuest(id)!;
        Assert.False(string.IsNullOrWhiteSpace(q.Character));
        Assert.False(string.IsNullOrWhiteSpace(q.Epithet));
        Assert.False(string.IsNullOrWhiteSpace(q.Portrait));
        Assert.False(string.IsNullOrWhiteSpace(q.BonusName));
        foreach (var n in q.Nodes.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(n.Title), n.Id);
            Assert.InRange(n.Text.Length, 60, 300);
            Assert.All(n.Options, o => Assert.InRange(o.Text.Length, 3, 26));
            Assert.All(n.Options.Where(o => o.Action != QuestOptionAction.Wait), o => Assert.False(string.IsNullOrWhiteSpace(o.Journal), n.Id));
            switch (n.Kind)
            {
                case QuestNodeKind.Story:
                    Assert.True(n.Options.Count >= 2, n.Id + ": a story node is a choice");
                    Assert.All(n.Options, o => Assert.Equal(QuestOptionAction.Advance, o.Action));
                    Assert.True(n.Cost == null && n.Battle == null && n.Boon == null, n.Id);
                    break;
                case QuestNodeKind.Battle:
                    Assert.Equal(2, n.Options.Count);
                    Assert.Single(n.Options, o => o.Action == QuestOptionAction.Fight);
                    Assert.Single(n.Options, o => o.Action == QuestOptionAction.Wait);
                    Assert.NotNull(n.Battle);
                    Assert.Equal(0, n.Battle!.BonusPct % 5);
                    Assert.InRange(n.Battle.Escorts, 0, 3);
                    break;
                case QuestNodeKind.Sacrifice:
                    Assert.Equal(2, n.Options.Count);
                    Assert.Single(n.Options, o => o.Action == QuestOptionAction.Pay);
                    Assert.Single(n.Options, o => o.Action == QuestOptionAction.Wait);
                    Assert.NotNull(n.Cost);
                    Assert.NotEqual(QuestCostKind.None, n.Cost!.Kind);
                    if (n.Cost.Kind == QuestCostKind.Coins) { Assert.InRange(n.Cost.Amount, 100, 300); Assert.Equal(0, n.Cost.Amount % 50); }
                    if (n.Cost.Kind == QuestCostKind.CurrentHpPct) { Assert.InRange(n.Cost.Amount, 50, 80); Assert.Equal(0, n.Cost.Amount % 10); }
                    break;
                case QuestNodeKind.Boon:
                    Assert.All(n.Options, o => Assert.Equal(QuestOptionAction.Advance, o.Action));
                    Assert.True(n.Boon != null && n.Boon.CoinsPerGain > 0, n.Id);
                    break;
            }
        }
    }

    [Theory]
    [MemberData(nameof(Quests))]
    public void EverySacrificeKind_TheOwnerAskedFor_IsInTheChain(string id)
    {
        var kinds = C.RunQuest(id)!.Nodes.Values.Where(n => n.Kind == QuestNodeKind.Sacrifice).Select(n => n.Cost!.Kind).Distinct().ToList();
        foreach (var k in new[] { QuestCostKind.Coins, QuestCostKind.LegendarySkill, QuestCostKind.MythicSkill, QuestCostKind.CurrentHpPct, QuestCostKind.AllRunSkills })
            Assert.Contains(k, kinds);
    }

    [Theory]
    [MemberData(nameof(Quests))]
    public void Epilogues_ArePositive_Small_AndRound(string id)
    {
        var q = C.RunQuest(id)!;
        Assert.InRange(q.Epilogues.Count, 3, 6);
        Assert.Equal(q.Epilogues.Count, q.Epilogues.Select(e => e.Id).Distinct().Count());
        foreach (var e in q.Epilogues)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Title));
            Assert.InRange(e.Text.Length, 40, 300);
            Assert.True(e.Amount > 0);
            switch (e.Kind)
            {
                case QuestEpilogueKind.Coins: Assert.InRange(e.Amount, 20, 100); Assert.Equal(0, e.Amount % 10); break; // a shop skill at most
                case QuestEpilogueKind.HealPct: Assert.InRange(e.Amount, 10, 40); Assert.Equal(0, e.Amount % 5); break;
                case QuestEpilogueKind.MaxHpPct: Assert.InRange(e.Amount, 5, 10); Assert.Equal(0, e.Amount % 5); break; // a shop perk
                case QuestEpilogueKind.SkillPick: Assert.Equal(1, e.Amount); break;
            }
        }
    }

    [Fact]
    public void Requirements_AreResolvable_ByAPlayerAtTheUnlockLevel()
    {
        int unlock = C.QuestTuning.UnlockLevel;
        Assert.True(unlock > 1, "never on the tutorial level");
        int L(string f) => (int)Features.UnlockLevel(C.Feature(f)!);
        // Mythic sacrifices need minibosses (Mythic choices), skill sacrifices need drafts and shops, battles need the elites
        Assert.True(L(Features.RunMiniboss) < unlock);
        Assert.True(L(Features.RunShop) < unlock);
        Assert.True(C.Campaign.MinibossesFromLevel <= unlock);
        // the draft pool has Legendary and Mythic tier skills without any Laboratory research
        Assert.Contains(C.Skills.Values, s => s.Draftable && !s.Signature && RunTuning.RunTier(s.Rarity) == Rarity.Legendary);
        Assert.Contains(C.Skills.Values, s => s.Draftable && !s.Signature && RunTuning.RunTier(s.Rarity) == Rarity.Mythic);
        // every level a quest can appear on has a champion and elites for the battles (main levels past the unlock, Dark Side)
        for (int n = unlock; n <= 600; n += 7)
        {
            var l = C.MainLevel(n)!;
            Assert.True(l.MinibossId != null && C.Enemies.ContainsKey(l.MinibossId), "main " + n);
            Assert.NotEmpty(l.ElitePool);
        }
        var dark = C.Portal(ContentDb.DarkSidePortalId)!;
        for (int i = 1; i <= 60; i += 5)
        {
            var l = C.PortalLevel(dark, i)!;
            Assert.True(l.MinibossId != null && C.Enemies.ContainsKey(l.MinibossId), "dark " + i);
        }
        // the coin prices are affordable from one run's income (a run earns ~550–800 coins)
        Assert.All(C.RunQuestOrder.SelectMany(q => q.Nodes.Values).Where(x => x.Cost?.Kind == QuestCostKind.Coins), x => Assert.True(x.Cost!.Amount <= 300));
    }
}
