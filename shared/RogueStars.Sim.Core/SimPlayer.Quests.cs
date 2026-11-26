using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim
{
    /// <summary>How a bot answers Run Quest encounters (docs/design/run-quests/recipe.md).</summary>
    public enum QuestPolicy
    {
        /// <summary>A typical player: fights when healthy (a lost quest battle ends the run), pays coins and skills when it can,
        /// pays life when healthy, and only gives up every run skill half of the times it is asked (drama).</summary>
        Typical,
        /// <summary>Takes every battle and pays every price it can.</summary>
        Eager,
    }

    // Run Quests: the bot answers the character's encounter (phase Quest) like a player would; the simulator's --run-quests
    // report measures how many runs a chain takes.
    public sealed partial class SimPlayer
    {
        public QuestPolicy QuestPolicy = QuestPolicy.Typical;
        /// <summary>
        /// Quest encounters answered, battles fought / won / lost (a loss is the normal defeat: revive or the run ends), prices
        /// paid, and waits by node kind (index = QuestNodeKind; a lost battle counts as a battle wait).
        /// </summary>
        public int QuestEncounters, QuestBattles, QuestBattlesWon, QuestBattlesLost, QuestPaid;
        public readonly int[] QuestWaits = new int[4];
        /// <summary>Waits because a price could not be paid (no coins, no skill of the tier).</summary>
        public int QuestUnaffordable;
        /// <summary>Unaffordable prices by QuestCostKind.</summary>
        public readonly int[] QuestUnaffordableBy = new int[6];

        private Rng _questRng;

        /// <summary>
        /// Run coins a player keeps in this run's shops (playtest 0.1.325: shops show 3 skills + support, so buying everything
        /// would leave nothing): the price of the character's coin payment while the run still has the encounter ahead.
        /// </summary>
        private long QuestCoinsToKeep(ActiveRun run)
        {
            if (run?.Quest == null || run.Quest.QuestId == null || run.Quest.Met || run.Quest.Epilogue) return 0;
            var q = C.RunQuest(run.Quest.QuestId);
            var node = q != null ? RunSystem.QuestCurrentNode(C, S, q) : null;
            return node != null && node.Kind == QuestNodeKind.Sacrifice && node.Cost != null && node.Cost.Kind == QuestCostKind.Coins ? node.Cost.Amount : 0;
        }

        /// <summary>The shop pick the farming bot would make would dip into the coins kept for the quest payment.</summary>
        private bool ShopBuyWouldSpendReserve(ActiveRun run)
        {
            int pick = RunSystem.FarmShopPick(C, run);
            return pick >= 0 && run.Coins - run.Shop[pick].Price < QuestCoinsToKeep(run);
        }

        /// <summary>Answers the open quest encounter (story choice, battle, price, boon or epilogue scene).</summary>
        private CommandResult PlayQuest()
        {
            if (_questRng == null) _questRng = Rng.FromSeed(Seed * 7919 + 11);
            var run = S.Run.Active;
            QuestEncounters++;
            var node = RunSystem.QuestOpenNode(C, S, run);
            if (node == null) return Exec(new RunQuestChoose { Index = 0 }); // an epilogue scene
            int Find(QuestOptionAction a) => node.Options.FindIndex(o => o.Action == a);
            int wait = Find(QuestOptionAction.Wait);
            int pick;
            switch (node.Kind)
            {
                case QuestNodeKind.Battle:
                {
                    bool healthy = run.WormMaxHp > 0 && run.WormHp / run.WormMaxHp >= 0.5;
                    pick = QuestPolicy == QuestPolicy.Eager || healthy ? Find(QuestOptionAction.Fight) : wait;
                    break;
                }
                case QuestNodeKind.Sacrifice:
                {
                    bool can = RunSystem.QuestCanPay(C, run, node.Cost);
                    bool will = can;
                    if (can && QuestPolicy == QuestPolicy.Typical)
                    {
                        if (node.Cost.Kind == QuestCostKind.CurrentHpPct) will = run.WormMaxHp > 0 && run.WormHp / run.WormMaxHp >= 0.6;
                        if (node.Cost.Kind == QuestCostKind.AllRunSkills) will = _questRng.NextDouble() < 0.5;
                    }
                    if (!can) { QuestUnaffordable++; QuestUnaffordableBy[(int)node.Cost.Kind]++; }
                    pick = will ? Find(QuestOptionAction.Pay) : wait;
                    break;
                }
                case QuestNodeKind.Story: pick = _questRng.Range(0, node.Options.Count); break;
                default: pick = 0; break;
            }
            var r = Exec(new RunQuestChoose { Index = pick });
            if (!r.Ok) return r;
            var action = node.Options[pick].Action;
            if (action == QuestOptionAction.Fight)
            {
                QuestBattles++;
                if (r.Events.Exists(ev => ev.Type == "run_quest_choice" && ev.Args.Count > 3 && ev.Args[3] == "advanced")) QuestBattlesWon++;
                else { QuestBattlesLost++; QuestWaits[(int)node.Kind]++; }
            }
            else if (action == QuestOptionAction.Pay) QuestPaid++;
            else if (action == QuestOptionAction.Wait) QuestWaits[(int)node.Kind]++;
            return r;
        }
    }
}
