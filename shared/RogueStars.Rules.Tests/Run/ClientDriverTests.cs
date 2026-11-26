using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using Xunit.Abstractions;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Drives runs exactly like the client's auto mode with "Auto-pick" off (RunScreen.Frame): RunAutoStep { Farming = false }
/// whenever RunSystem.RequiresPlayer is false, an explicit player command otherwise. Every phase transition is logged.
/// Playtest 0.1.86: "several choices were auto skipped" and "landed on battle tiles that did nothing".
/// </summary>
public class ClientDriverTests(ITestOutputHelper output)
{
    private static readonly string[] DecisionOpened = { "run_event", "run_draft", "run_mythic_draft", "run_shop", "run_quest" };
    private static readonly string[] DecisionResolved = { "run_event_outcome", "run_draft_pick", "run_shop_buy", "run_quest_choice", "run_quest_epilogue" };

    private sealed class Log
    {
        public int AutoSteps, PlayerCommands, Decisions, Fights, FightLandings;
        public readonly List<string> Lines = new();
    }

    private static Log Drive(GameEngine e, ref GameState s, int seed, ITestOutputHelper? output = null)
    {
        var log = new Log();
        var c = e.Content;
        var rng = new Random(seed);
        for (int i = 0; i < 5000 && s.Run.Active != null; i++)
        {
            var a = s.Run.Active;
            var phase = a.Phase;
            var decision = RunSystem.Decision(c, a);
            bool auto = !RunSystem.RequiresPlayer(c, a, farming: false);
            GameCommand cmd;
            if (auto) { cmd = new RunAutoStep { Farming = false }; log.AutoSteps++; }
            else
            {
                log.PlayerCommands++;
                log.Decisions++;
                switch (phase)
                {
                    case RunPhase.ChooseBranch: cmd = new RunChooseBranch { Index = rng.Next(a.BranchOptions.Count) }; break;
                    case RunPhase.Event:
                    {
                        var ev = c.RunEvents[a.EventId];
                        var ok = Enumerable.Range(0, ev.Options.Count).Where(k => a.Coins >= ev.Options[k].CoinCost && a.Shards >= ev.Options[k].ShardCost).ToList();
                        cmd = new RunChooseEventOption { Index = ok[rng.Next(ok.Count)] };
                        break;
                    }
                    case RunPhase.Draft: cmd = new RunDraftPick { SkillId = a.DraftOffer[rng.Next(a.DraftOffer.Count)] }; break;
                    case RunPhase.Shop:
                    {
                        int buy = a.Shop.FindIndex(o => !o.Sold && a.Coins >= o.Price);
                        cmd = buy >= 0 && rng.Next(3) > 0 ? new RunShopBuy { Index = buy } : new RunShopLeave();
                        break;
                    }
                    case RunPhase.Defeated: cmd = new RunAbandon(); break;
                    case RunPhase.BurrowChoice: cmd = new RunBurrowCashOut(); break;
                    case RunPhase.Minigame:
                        cmd = a.Minigame.Started ? new RunMinigameFinish { Tier = (MinigameTier)rng.Next(4) } : new RunMinigameStart();
                        break;
                    case RunPhase.Quest:
                    {
                        // a Run Quest encounter: any option the run can take (a price it cannot pay is refused by the rules)
                        var node = RunSystem.QuestOpenNode(c, s, a);
                        var ok = node == null ? new List<int> { 0 } : Enumerable.Range(0, node.Options.Count)
                            .Where(k => node.Options[k].Action != QuestOptionAction.Pay || RunSystem.QuestCanPay(c, a, node.Cost)).ToList();
                        cmd = new RunQuestChoose { Index = ok[rng.Next(ok.Count)] };
                        break;
                    }
                    default: throw new InvalidOperationException("unexpected player phase " + phase);
                }
            }
            var r = RunTestKit.Exec(e, ref s, cmd);
            Assert.True(r.Ok, r.Error + " at " + phase);
            var after = s.Run.Active;
            string line = $"{phase}/{decision} --{cmd.GetType().Name}--> {(after == null ? "end" : after.Phase.ToString())} [{string.Join(" ", r.Events.Select(ev => ev.Type))}]";
            log.Lines.Add(line);
            output?.WriteLine(line);

            if (auto)
            {
                // an auto step never answers a player decision ...
                Assert.DoesNotContain(r.Events, ev => DecisionResolved.Contains(ev.Type));
                Assert.False(phase is RunPhase.Event or RunPhase.Quest or RunPhase.Minigame or RunPhase.Draft or RunPhase.Shop or RunPhase.ChooseBranch or RunPhase.BurrowChoice, line);
                Assert.False(phase == RunPhase.Defeated && !a.ReviveUsed, line);
                // ... and a decision it opens is still open when it returns
                if (r.Events.Any(ev => DecisionOpened.Contains(ev.Type)))
                    Assert.True(after != null && after.Phase is RunPhase.Event or RunPhase.Draft or RunPhase.Shop or RunPhase.Quest, "opened and closed by auto: " + line);
            }
            // one playback per command: never two fights in a single step
            int fights = r.Events.Count(ev => ev.Type == "run_battle");
            Assert.True(fights <= 1, "two fights in one step: " + line);
            log.Fights += fights;
            // a landing on a fight tile always plays a fight, and the tile stays what it was (cleared, not replaced)
            foreach (var land in r.Events.Where(ev => ev.Type == "run_land"))
            {
                var cat = c.BoardTiles[land.Args[1]].Category;
                if (cat is not (TileCategory.Enemy or TileCategory.Elite or TileCategory.Miniboss)) continue;
                log.FightLandings++;
                Assert.Contains(r.Events, ev => ev.Type == "run_battle");
                var tiles = after?.Tiles ?? a.Tiles;
                var tile = tiles[int.Parse(land.Args[0])];
                Assert.Equal(land.Args[1], tile.TileId);
            }
        }
        Assert.Null(s.Run.Active);
        return log;
    }

    [Theory]
    [InlineData("ember_wastes_1", 1000, 100)]
    [InlineData("ember_wastes_8", 3000, 260)]
    [InlineData("glacier_rift_4", 3000, 300)]
    public void ClientAutoMode_NeverResolvesAPlayerChoice_AndEveryFightLandingFights(string level, double hp, double atk)
    {
        int decisions = 0, fights = 0, landings = 0;
        for (int seed = 1; seed <= 15; seed++)
        {
            var e = RunTestKit.Engine(new RunTestKit.WormSetup { Hp = hp, Atk = atk, Def = 30, Speed = 110 });
            var s = TestHelpers.NewPlayer(e, seed);
            s.Flags.Add(Features.DevUnlockAllFlag);
            var lv = e.Content.RunLevel(level)!;
            if (lv.Number > 1) { s.Run.Levels[e.Content.MainLevelId(lv.Number - 1)] = new LevelProgress { Cleared = true }; s.Counters[Requirements.MainLevelCounter] = lv.Number - 1; }
            s.Wallet.Add(Currencies.Stamina, 100);
            Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = level }).Ok);
            var log = Drive(e, ref s, seed, seed == 1 ? output : null);
            decisions += log.Decisions; fights += log.Fights; landings += log.FightLandings;
        }
        output.WriteLine($"{level}: player decisions {decisions}, fights {fights}, fight landings {landings}");
        Assert.True(decisions > 0 && fights > 0 && landings > 0);
    }

    [Fact]
    public void SingleOptionEvents_AndShieldedNegativeEvents_AreShown()
    {
        var c = new ContentDb();
        var single = new EventDef { Id = "only_way", Name = "Narrow Pass", Text = "One way through.", DefaultOption = 0 };
        single.Options.Add(new EventOption { Text = "Squeeze through", Outcomes = { new EventOutcome { Weight = 1, Text = "Tight.", Effects = { new RunEffect(RunEffectKind.Coins, 5) } } } });
        c.RunEvents[single.Id] = single;
        var e = RunTestKit.Engine(RunTestKit.Strong(), c);
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.Phase = RunPhase.Event; run.EventId = single.Id; run.EventTile = 0;
        Assert.True(RunSystem.RequiresPlayer(c, run));                         // shown even with one option
        Assert.False(RunTestKit.Exec(e, ref s, new RunAutoStep()).Ok);         // auto pauses
        Assert.True(RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true }).Ok); // auto-pick takes it

        // a boon that skips a negative event says so on the result screen
        var s2 = RunTestKit.StartRun(e, seed: 3);
        var r2 = s2.Run.Active!;
        r2.SkipNegativeEvents = 1;
        RunTestKit.LineBoard(r2, Enumerable.Repeat("event", 30).ToArray());
        int seq = r2.OutcomeSeq;
        for (int i = 0; i < 40 && s2.Run.Active != null && s2.Run.Active.SkipNegativeEvents > 0; i++)
        {
            var a = s2.Run.Active;
            if (a.Phase == RunPhase.Event) { RunTestKit.Exec(e, ref s2, new RunChooseEventOption { Index = c.RunEvents[a.EventId].DefaultOption }); continue; }
            var res = RunTestKit.Exec(e, ref s2, RunSystem.RequiresPlayer(c, a) ? new RunAutoStep { Farming = true } : new RunAutoStep());
            if (res.Events.Any(ev => ev.Type == "run_negative_skipped") && s2.Run.Active != null)
            {
                Assert.True(s2.Run.Active.OutcomeSeq > seq);
                Assert.Equal("Avoided", s2.Run.Active.LastOutcome.Choice);
                return;
            }
        }
    }
}
