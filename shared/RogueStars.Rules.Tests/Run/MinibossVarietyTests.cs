using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Playtest 0.1.359: "a level seems to only ever have the same miniboss". Every level with lairs draws its minibosses from its
/// biome's three elites; each run shuffles them with its own seed, so the run's two lairs meet two different minibosses and runs differ.
/// </summary>
public class MinibossVarietyTests
{
    private static readonly ContentDb C = ContentDb.Default;

    private static IEnumerable<LevelDef> LairLevels()
    {
        for (int n = C.Campaign.MinibossesFromLevel; n <= 60; n++) yield return C.MainLevel(n)!;
        foreach (var l in C.RunLevels.Values.Where(l => l.PortalId == ContentDb.DarkSidePortalId || C.Portal(l.PortalId)?.Kind == PortalKind.Burrow))
            yield return l;
    }

    [Fact]
    public void EveryLevelWithLairs_HasItsBiomesThreeElites()
    {
        foreach (var l in LairLevels())
        {
            Assert.Equal(3, l.ElitePool.Distinct().Count());
            Assert.True(l.ElitePool.Count >= C.RunTuning.MinibossesPerRun, l.Id);
            Assert.Equal(l.MinibossId, l.ElitePool[0]);
            foreach (var id in l.ElitePool)
            {
                Assert.True(C.Enemies.TryGetValue(id, out var e), l.Id + ": " + id);
                Assert.True(e!.Elite && !e.Boss, id);
                if (e.ArtId != null) Assert.True(C.Enemies.ContainsKey(e.ArtId), id + " art " + e.ArtId);
            }
        }
    }

    [Fact]
    public void BiomeElites_StayOnTheFirstElitesStatBudget()
    {
        foreach (var l in LairLevels().GroupBy(l => l.MinibossId).Select(g => g.First()))
        {
            double Budget(string id) => C.Enemies[id].Stats.Where(m => m.Stat == StatId.Hp).Sum(m => m.Flat) * C.Enemies[id].Stats.Where(m => m.Stat == StatId.Atk).Sum(m => m.Flat);
            double anchor = Budget(l.MinibossId);
            foreach (var id in l.ElitePool) Assert.InRange(Budget(id) / anchor, 0.9, 1.25);
        }
    }

    [Fact]
    public void RunOrder_IsSeededPerRun_DeterministicAndVaried()
    {
        var level = C.MainLevel(8)!;
        var firsts = new HashSet<string>();
        var pairs = new HashSet<string>();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var order = RunSystem.MinibossOrder(level, new Rng(seed));
            Assert.Equal(level.ElitePool.OrderBy(x => x), order.OrderBy(x => x)); // a permutation of the pool
            Assert.NotEqual(order[0], order[1]); // the run's two lairs differ
            Assert.Equal(order, RunSystem.MinibossOrder(level, new Rng(seed))); // same run seed, same order
            firsts.Add(order[0]);
            pairs.Add(order[0] + "+" + order[1]);
        }
        Assert.Equal(level.ElitePool.Count, firsts.Count); // every pool member leads some runs
        Assert.True(pairs.Count >= 4, "pairs: " + string.Join(", ", pairs));
    }

    [Fact]
    public void ARunsTwoLairs_MeetTwoDifferentMinibosses_AndRunsDiffer()
    {
        var firsts = new HashSet<string>();
        for (long seed = 1; seed <= 6; seed++)
        {
            var e = RunTestKit.Engine(RunTestKit.Strong());
            var s = RunTestKit.StartRun(e, "ember_wastes_1", seed); // the classic test boards: lairs on level 1 too
            var run = s.Run.Active!;
            Assert.Equal(2, run.MinibossOrder.Take(2).Distinct().Count());
            RunTestKit.LineBoard(run, new[] { "miniboss", "miniboss" }.Concat(Enumerable.Repeat("empty", 30)).ToArray());
            var met = new List<string>();
            // play on (the default picks: the Mythic choice, drafts) until both lairs are beaten; note who each one held
            for (int i = 0; i < 40 && met.Count < 2; i++)
            {
                int beaten = s.Run.Active!.MinibossesDefeated;
                var cmd = s.Run.Active.Phase == RunPhase.AwaitRoll ? (GameCommand)new RunRoll() : new RunAutoStep { Farming = true, FarmShops = true };
                Assert.True(RunTestKit.Exec(e, ref s, cmd).Ok);
                run = s.Run.Active!;
                if (run.MinibossesDefeated == beaten) continue;
                var enemy = run.LastBattle.Fighters.Single(f => f.Side == BattleSide.Enemy);
                Assert.Equal(run.MinibossOrder[beaten], enemy.Id);
                met.Add(enemy.Id);
            }
            Assert.Equal(2, met.Count);
            Assert.NotEqual(met[0], met[1]);
            firsts.Add(met[0]);
        }
        Assert.True(firsts.Count >= 2, "runs start with different minibosses: " + string.Join(", ", firsts));
    }

    [Fact]
    public void ARunSavedBeforeThePools_FallsBackToTheLevelsMiniboss()
    {
        var level = C.MainLevel(8)!;
        var run = new ActiveRun { LevelId = level.Id };
        Assert.Equal(level.MinibossId, RunSystem.NextMiniboss(level, run));
    }
}
