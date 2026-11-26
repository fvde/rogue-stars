using System;
using System.Collections.Generic;

namespace RogueStars.Rules.Content
{
    // Run Quests (docs/design/run-quests/recipe.md): a rare character met on the board who tells a ~30-encounter story.
    // One data definition per quest chain (Content/Quests/Quest<Name>.cs), registered in ContentDb.RunQuests.cs. Data only.

    /// <summary>What a quest node is (recipe: ~50% Story, ~20% Battle, ~20% Sacrifice, ~10% Boon per playthrough).</summary>
    public enum QuestNodeKind
    {
        /// <summary>Fluff: a choice that steers the story (options lead to different next nodes or tones). Always advances.</summary>
        Story,
        /// <summary>A difficult battle the player may accept. Won: advance. Lost or declined: the story waits on this node.</summary>
        Battle,
        /// <summary>A price (coins, a skill, life...). Paid: advance. Refused or unaffordable: the story waits on this node.</summary>
        Sacrifice,
        /// <summary>A permanent account-wide benefit (QuestBoonDef). Always advances.</summary>
        Boon,
    }

    /// <summary>What picking an option does.</summary>
    public enum QuestOptionAction
    {
        /// <summary>Go on to <see cref="QuestOptionDef.Next"/> (story, boon).</summary>
        Advance,
        /// <summary>Fight the node's battle: won = Next, lost = the story waits (non-lethal, QuestTuning.LossHpPct).</summary>
        Fight,
        /// <summary>Pay the node's cost, then go on to Next. Refused if the run cannot pay.</summary>
        Pay,
        /// <summary>"Not now": nothing happens, the story waits on this node until the next encounter.</summary>
        Wait,
    }

    /// <summary>The price of a sacrifice node (RunSystem.Quests: QuestCanPay / QuestPay).</summary>
    public enum QuestCostKind
    {
        None,
        /// <summary>Amount run coins.</summary>
        Coins,
        /// <summary>One owned Legendary-tier run skill of the player's choice (signatures never).</summary>
        LegendarySkill,
        /// <summary>One owned Mythic-tier run skill of the player's choice (signatures never).</summary>
        MythicSkill,
        /// <summary>Amount % of the worm's current HP (it never drops below 1).</summary>
        CurrentHpPct,
        /// <summary>Every run skill the worm holds (signatures stay): needs at least one to give.</summary>
        AllRunSkills,
    }

    [Serializable]
    public sealed class QuestCostDef
    {
        public QuestCostKind Kind;
        public long Amount;
        public QuestCostDef() { }
        public QuestCostDef(QuestCostKind kind, long amount = 0) { Kind = kind; Amount = amount; }
    }

    /// <summary>
    /// A difficult battle (above the level's elites): the level's miniboss as champion at the run's usual board strength
    /// + QuestTuning.BattleBasePct + <see cref="BonusPct"/> HP/ATK, plus <see cref="Escorts"/> elites of the level at elite strength.
    /// </summary>
    [Serializable]
    public sealed class QuestBattleDef
    {
        public double BonusPct;
        public int Escorts;
        public QuestBattleDef() { }
        public QuestBattleDef(double bonusPct, int escorts) { BonusPct = bonusPct; Escorts = escorts; }
    }

    /// <summary>The permanent benefit of a boon node. CoinsPerGain: +n run coins on every run-coin gain, account-wide, forever.</summary>
    [Serializable]
    public sealed class QuestBoonDef
    {
        public int CoinsPerGain;
        public QuestBoonDef() { }
        public QuestBoonDef(int coinsPerGain) { CoinsPerGain = coinsPerGain; }
    }

    [Serializable]
    public sealed class QuestOptionDef
    {
        /// <summary>The button text.</summary>
        public string Text;
        public QuestOptionAction Action;
        /// <summary>The node this leads to (Advance / Fight won / Pay); null = the chain ends (only on the last chapter).</summary>
        public string Next;
        /// <summary>The journal line recorded when the story moves on with this option (past tense, the worm's view).</summary>
        public string Journal;
        public QuestOptionDef() { }
        public QuestOptionDef(string text, QuestOptionAction action, string next, string journal)
        { Text = text; Action = action; Next = next; Journal = journal; }
    }

    [Serializable]
    public sealed class QuestNodeDef
    {
        public string Id;
        /// <summary>Chapter number (1-based): every path through the chain visits chapters 1, 2, … RunQuestDef.Chapters in order.</summary>
        public int Chapter;
        public QuestNodeKind Kind;
        public string Title;
        /// <summary>The character's scene, spoken / described (shown on the encounter card).</summary>
        public string Text;
        public List<QuestOptionDef> Options = new List<QuestOptionDef>();
        public QuestCostDef Cost;
        public QuestBattleDef Battle;
        public QuestBoonDef Boon;
    }

    /// <summary>What an epilogue encounter gives (after the chain is complete: a friendly scene, a small reward).</summary>
    public enum QuestEpilogueKind
    {
        /// <summary>Amount run coins (the quest coin bonus applies like on every gain).</summary>
        Coins,
        /// <summary>Heal Amount % of max HP.</summary>
        HealPct,
        /// <summary>A free skill pick: a normal level-up draft (1 of 3).</summary>
        SkillPick,
        /// <summary>+Amount % max HP for the rest of the run (a stat perk).</summary>
        MaxHpPct,
    }

    [Serializable]
    public sealed class QuestEpilogueDef
    {
        public string Id;
        public string Title;
        public string Text;
        /// <summary>The only button ("Thank you!").</summary>
        public string Option;
        public QuestEpilogueKind Kind;
        public double Amount;
    }

    /// <summary>One quest chain: its character, the story graph and the epilogue scenes.</summary>
    [Serializable]
    public sealed class RunQuestDef
    {
        public string Id;
        public string Character;
        /// <summary>Short epithet under the name ("the Lost Courier").</summary>
        public string Epithet;
        /// <summary>ArtCatalog key of the portrait (Art/Run/Characters/&lt;key&gt;; a drawn placeholder until the art exists).</summary>
        public string Portrait;
        /// <summary>Name of the account bonus the boons build (journal, outcome lines): e.g. "Starglint".</summary>
        public string BonusName;
        /// <summary>Order among the chains (the encounter goes to the first unfinished chain).</summary>
        public int Order;
        public string StartNode;
        /// <summary>Chapters of one playthrough (every path has exactly this many nodes).</summary>
        public int Chapters;
        public Dictionary<string, QuestNodeDef> Nodes = new Dictionary<string, QuestNodeDef>();
        /// <summary>Epilogue scenes after completion, given in rotation.</summary>
        public List<QuestEpilogueDef> Epilogues = new List<QuestEpilogueDef>();

        public QuestNodeDef Node(string id) => id != null && Nodes.TryGetValue(id, out var n) ? n : null;
    }

    /// <summary>Shared tuning of every quest chain (encounter rate, battles, losses).</summary>
    [Serializable]
    public sealed class QuestTuning
    {
        /// <summary>The Run Quests feature unlocks when this main level is cleared (the encounters start with the next run).</summary>
        public int UnlockLevel = 20;
        /// <summary>
        /// Chance (%) that an eligible run has the encounter, by eligible runs since the last encounter (index 0 = the run right
        /// after it; the last entry repeats). Mean ≈ 5 runs between encounters, never two runs in a row, never more than 8.
        /// </summary>
        public List<double> ChanceByRunsWithoutPct = new List<double> { 0, 5, 15, 25, 35, 55, 80, 100 };
        /// <summary>Pity index before the very first encounter (80%, then 100%): the new feature shows itself within two runs.</summary>
        public int FirstEncounterIndex = 6;
        /// <summary>The encounter tile stands on a trunk tile of this board segment (0-based), at this fraction of it.</summary>
        public int Segment = 1;
        public double SegmentAt = 0.5;
        /// <summary>Quest battle champion: HP/ATK % above the run's usual board strength, on top of the node's BonusPct.</summary>
        public double BattleBasePct = 40;
        /// <summary>A lost quest battle does not end the run: the worm is left with at least this % of max HP.</summary>
        public double LossHpPct = 10;
    }
}
