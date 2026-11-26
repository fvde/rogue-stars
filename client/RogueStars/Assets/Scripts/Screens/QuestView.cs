using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>One option of a quest encounter as the card shows it: the button text, a detail line and a cost / risk chip.</summary>
    public struct QuestOptionView
    {
        public string Label, Detail, Chip;
        public Color ChipColor;
        public bool Enabled;
    }

    /// <summary>
    /// Texts of the Run Quest encounter card and the journal (pure; tested): the chapter (never the total: the story's length
    /// is not spoiled), costs, risk chips, the account coin bonus line. Docs: docs/design/run-quests/recipe.md.
    /// </summary>
    public static class QuestView
    {
        /// <summary>"Chapter 7" (the chapter the story is at; never the total), "Story complete" once done.</summary>
        public static string Progress(RunQuestDef q, int chapter) =>
            chapter > q.Chapters ? "Story complete" : "Chapter " + chapter;

        /// <summary>The journal's chapter line: "Reached chapter 7" (never a total or how many are left), "Story complete" once done.</summary>
        public static string Reached(RunQuestDef q, int chapter) =>
            chapter > q.Chapters ? "Story complete" : "Reached chapter " + chapter;

        /// <summary>The price as a chip: "−100 coins", "−50% current HP", "A random Legendary skill", "−ALL run skills".</summary>
        public static string CostText(QuestCostDef cost)
        {
            if (cost == null) return "";
            switch (cost.Kind)
            {
                case QuestCostKind.Coins: return "−" + cost.Amount + " coins";
                case QuestCostKind.CurrentHpPct: return "−" + cost.Amount + "% current HP";
                case QuestCostKind.LegendarySkill: return "A random Legendary skill";
                case QuestCostKind.MythicSkill: return "A random Mythic skill";
                case QuestCostKind.AllRunSkills: return "−ALL run skills";
                default: return "";
            }
        }

        /// <summary>The price in a sentence ("100 coins", "a random Mythic skill") for the journal's "Next time" line.</summary>
        public static string PriceText(QuestCostDef cost)
        {
            if (cost == null) return "";
            switch (cost.Kind)
            {
                case QuestCostKind.LegendarySkill: return "a random Legendary skill";
                case QuestCostKind.MythicSkill: return "a random Mythic skill";
                case QuestCostKind.AllRunSkills: return "ALL run skills";
                default: return CostText(cost).TrimStart('−');
            }
        }

        /// <summary>Why a price cannot be paid now ("Not enough coins", "No Mythic skill" …), or null.</summary>
        public static string Unaffordable(ContentDb c, ActiveRun run, QuestCostDef cost)
        {
            if (cost == null || RunSystem.QuestCanPay(c, run, cost)) return null;
            switch (cost.Kind)
            {
                case QuestCostKind.Coins: return "Not enough coins";
                case QuestCostKind.LegendarySkill: return "No Legendary skill to give";
                case QuestCostKind.MythicSkill: return "No Mythic skill to give";
                case QuestCostKind.AllRunSkills: return "No skills to give";
                default: return "Cannot pay now";
            }
        }

        /// <summary>The account bonus line: "Starglint: +2 coins on every coin gain in runs" (or how to earn it).</summary>
        public static string BonusLine(ContentDb c, GameState s)
        {
            int b = RunSystem.QuestCoinBonus(s);
            string name = RunSystem.QuestBonusName(c, s);
            return b > 0 ? name + ": +" + b + " coin" + (b == 1 ? "" : "s") + " on every coin gain in runs" : name + ": not earned yet";
        }

        /// <summary>What an epilogue scene gives, as a chip.</summary>
        public static string EpilogueReward(QuestEpilogueDef e)
        {
            switch (e.Kind)
            {
                case QuestEpilogueKind.Coins: return "+" + e.Amount + " coins";
                case QuestEpilogueKind.HealPct: return "Heal " + e.Amount + "% HP";
                case QuestEpilogueKind.SkillPick: return "Free skill pick";
                case QuestEpilogueKind.MaxHpPct: return "+" + e.Amount + "% max HP";
                default: return "";
            }
        }

        /// <summary>Every option of the open node as the card shows it.</summary>
        public static List<QuestOptionView> Options(ContentDb c, GameState s, ActiveRun run, RunQuestDef q, QuestNodeDef node)
        {
            var list = new List<QuestOptionView>();
            int bonus = RunSystem.QuestCoinBonus(s);
            foreach (var o in node.Options)
            {
                var v = new QuestOptionView { Label = o.Text, Enabled = true, ChipColor = Theme.TextFaint };
                switch (o.Action)
                {
                    case QuestOptionAction.Advance:
                        if (node.Kind == QuestNodeKind.Boon && node.Boon != null)
                        {
                            v.Chip = "+" + node.Boon.CoinsPerGain + " coin per gain, forever";
                            v.ChipColor = Theme.Gold;
                            v.Detail = q.BonusName + ": +" + bonus + " → +" + (bonus + node.Boon.CoinsPerGain) + " coins on every coin gain";
                        }
                        else
                        {
                            v.Chip = "Story";
                            v.ChipColor = Theme.Purple;
                            v.Detail = "The story goes this way.";
                        }
                        break;
                    case QuestOptionAction.Fight:
                        v.Chip = "Hard battle";
                        v.ChipColor = Theme.Bad;
                        v.Detail = "Win: the story goes on. Lose: the run ends (unless you revive).";
                        break;
                    case QuestOptionAction.Pay:
                    {
                        v.Chip = CostText(node.Cost);
                        v.ChipColor = Theme.Bad;
                        string why = Unaffordable(c, run, node.Cost);
                        v.Enabled = why == null;
                        v.Detail = why ?? (node.Cost.Kind == QuestCostKind.LegendarySkill || node.Cost.Kind == QuestCostKind.MythicSkill
                            ? "One of yours, picked at random. The story goes on." : "Pay it and the story goes on.");
                        if (!v.Enabled) v.ChipColor = Theme.TextFaint;
                        break;
                    }
                    case QuestOptionAction.Wait:
                        v.Chip = "Story waits";
                        v.ChipColor = Theme.TextFaint;
                        v.Detail = "Nothing happens. " + q.Character + " will ask again next time.";
                        break;
                }
                list.Add(v);
            }
            return list;
        }

        /// <summary>"Waiting for you: Postage Due (−300 coins)" for the journal: the node the story waits on.</summary>
        public static string Waiting(ContentDb c, GameState s, RunQuestDef q)
        {
            var n = RunSystem.QuestCurrentNode(c, s, q);
            if (n == null) return q.Character + " still drops by now and then, just to say hello.";
            string what;
            switch (n.Kind)
            {
                case QuestNodeKind.Battle: what = "a hard battle"; break;
                case QuestNodeKind.Sacrifice: what = "a price: " + PriceText(n.Cost); break;
                case QuestNodeKind.Boon: what = "a gift"; break;
                default: what = "a choice"; break;
            }
            return "Next time: " + n.Title + " (" + what + ")";
        }
    }
}
