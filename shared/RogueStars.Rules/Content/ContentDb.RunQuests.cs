using System;
using System.Collections.Generic;
using RogueStars.Rules.Content.Quests;

namespace RogueStars.Rules.Content
{
    // Run Quests (docs/design/run-quests/recipe.md): the quest chains, their shared tuning and the board tile of the encounter.
    public sealed partial class ContentDb
    {
        /// <summary>Quest chains by id (one data class per chain in Content/Quests).</summary>
        public readonly Dictionary<string, RunQuestDef> RunQuests = new Dictionary<string, RunQuestDef>();
        /// <summary>The chains in encounter order (RunQuestDef.Order): the encounter goes to the first unfinished one.</summary>
        public readonly List<RunQuestDef> RunQuestOrder = new List<RunQuestDef>();
        public readonly QuestTuning QuestTuning = new QuestTuning();

        /// <summary>The board tile of a quest encounter (placed by RunBoard on scheduled runs only, never from a pool).</summary>
        public const string QuestTileId = "quest";

        public RunQuestDef RunQuest(string id) => id != null && RunQuests.TryGetValue(id, out var q) ? q : null;

        public void AddRunQuest(RunQuestDef q)
        {
            if (RunQuests.ContainsKey(q.Id)) throw new InvalidOperationException("duplicate quest " + q.Id);
            RunQuests[q.Id] = q;
            RunQuestOrder.Add(q);
            RunQuestOrder.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
        }

        partial void BuildRunQuests()
        {
            BoardTiles[QuestTileId] = new TileDef
            {
                Id = QuestTileId, Name = "Strange Visitor", Text = "Someone is waiting for you here.",
                Category = TileCategory.Quest, Valence = TileValence.Special, ForceStop = true, Consumable = false, Once = true,
            };
            AddRunQuest(QuestMothwick.Build());
        }
    }

    /// <summary>Fluent helper for authoring a quest chain as data (see Content/Quests/QuestMothwick.cs and the recipe).</summary>
    public sealed class QuestBuilder
    {
        public readonly RunQuestDef Quest;

        public QuestBuilder(string id, string character, string epithet, string portrait, string bonusName, int order, int chapters, string startNode)
        {
            Quest = new RunQuestDef
            {
                Id = id, Character = character, Epithet = epithet, Portrait = portrait, BonusName = bonusName, Order = order,
                Chapters = chapters, StartNode = startNode,
            };
        }

        /// <summary>An option that moves the story on to <paramref name="next"/> (null = the chain ends) with a journal line.</summary>
        public static QuestOptionDef Go(string text, string next, string journal) => new QuestOptionDef(text, QuestOptionAction.Advance, next, journal);

        private QuestNodeDef Add(string id, int chapter, QuestNodeKind kind, string title, string text)
        {
            var n = new QuestNodeDef { Id = id, Chapter = chapter, Kind = kind, Title = title, Text = text };
            if (Quest.Nodes.ContainsKey(id)) throw new InvalidOperationException("duplicate quest node " + id);
            Quest.Nodes[id] = n;
            return n;
        }

        /// <summary>Fluff: every option advances (to different nodes = a new direction, or the same node = a different tone).</summary>
        public QuestNodeDef Story(string id, int chapter, string title, string text, params QuestOptionDef[] options)
        {
            var n = Add(id, chapter, QuestNodeKind.Story, title, text);
            n.Options.AddRange(options);
            return n;
        }

        /// <summary>A difficult battle: Fight (won = next) or wait.</summary>
        public QuestNodeDef Battle(string id, int chapter, string title, string text, double bonusPct, int escorts, string fight, string next, string journal, string wait = "Not now")
        {
            var n = Add(id, chapter, QuestNodeKind.Battle, title, text);
            n.Battle = new QuestBattleDef(bonusPct, escorts);
            n.Options.Add(new QuestOptionDef(fight, QuestOptionAction.Fight, next, journal));
            n.Options.Add(new QuestOptionDef(wait, QuestOptionAction.Wait, null, null));
            return n;
        }

        /// <summary>A sacrifice: Pay (then next) or refuse.</summary>
        public QuestNodeDef Sacrifice(string id, int chapter, string title, string text, QuestCostDef cost, string pay, string next, string journal, string refuse = "Not now")
        {
            var n = Add(id, chapter, QuestNodeKind.Sacrifice, title, text);
            n.Cost = cost;
            n.Options.Add(new QuestOptionDef(pay, QuestOptionAction.Pay, next, journal));
            n.Options.Add(new QuestOptionDef(refuse, QuestOptionAction.Wait, null, null));
            return n;
        }

        /// <summary>A permanent account benefit (+coinsPerGain run coins on every coin gain), then next.</summary>
        public QuestNodeDef Boon(string id, int chapter, string title, string text, int coinsPerGain, string accept, string next, string journal)
        {
            var n = Add(id, chapter, QuestNodeKind.Boon, title, text);
            n.Boon = new QuestBoonDef(coinsPerGain);
            n.Options.Add(Go(accept, next, journal));
            return n;
        }

        public QuestEpilogueDef Epilogue(string id, string title, string text, string option, QuestEpilogueKind kind, double amount)
        {
            var e = new QuestEpilogueDef { Id = id, Title = title, Text = text, Option = option, Kind = kind, Amount = amount };
            Quest.Epilogues.Add(e);
            return e;
        }

        public static QuestCostDef Coins(long amount) => new QuestCostDef(QuestCostKind.Coins, amount);
        public static QuestCostDef HpPct(long pct) => new QuestCostDef(QuestCostKind.CurrentHpPct, pct);
        public static QuestCostDef Legendary() => new QuestCostDef(QuestCostKind.LegendarySkill, 1);
        public static QuestCostDef Mythic() => new QuestCostDef(QuestCostKind.MythicSkill, 1);
        public static QuestCostDef AllSkills() => new QuestCostDef(QuestCostKind.AllRunSkills);
    }
}
