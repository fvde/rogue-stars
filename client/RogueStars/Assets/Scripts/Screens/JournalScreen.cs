using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// The journal of the Run Quests ("More" → Journal; docs/design/run-quests/recipe.md): the account bonus the quests built
    /// (e.g. Starglint +2 coins on every coin gain), and per character its portrait, chapter progress, what the story waits for
    /// next and the story so far, one line per chapter.
    /// </summary>
    public sealed class JournalScreen : UiScreen
    {
        public override string Title => "Journal";
        public override string[] IntroFeatures => new[] { Features.RunQuests };

        protected override void Build(RectTransform body)
        {
            var summary = Card(body, Theme.PanelAlt);
            summary.name = "QuestBonus";
            Ui.Display(summary, "Gifts from the road", Theme.FontLarge, Theme.Gold, TextAnchor.MiddleLeft, "BonusTitle");
            Ui.Label(summary, QuestView.BonusLine(Content, State), Theme.FontNormal, Theme.Text, TextAnchor.MiddleLeft, "BonusLine");
            Note(summary, "Added to every run-coin gain: fights, caches, pickups, events and mini-games.");

            bool any = false;
            foreach (var q in Content.RunQuestOrder)
            {
                var p = RunSystem.QuestProgressOf(State, q.Id);
                if (p == null) continue;
                any = true;
                Character(body, q, p);
            }
            if (!any)
            {
                var empty = Card(body);
                empty.name = "JournalEmpty";
                Ui.Display(empty, "No visitors yet", Theme.FontLarge, Theme.Text, TextAnchor.MiddleLeft);
                Note(empty, "Now and then someone waits on the board. Keep running: about one run in five, a stranger crosses your path.");
            }
        }

        private void Character(RectTransform body, RunQuestDef q, QuestProgress p)
        {
            var card = Card(body, Theme.Panel, 12);
            card.name = "Quest:" + q.Id;
            var head = Ui.Row(card, 220, 20);
            head.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var portrait = Ui.Panel(head, Color.white, "Portrait");
            portrait.sprite = ScreenArt.Character(q.Portrait); portrait.preserveAspect = true; portrait.raycastTarget = false;
            Ui.Size(portrait, 220, 220, 0);
            var col = Ui.Column(head, 6, 0, null, "Info");
            Ui.Size(col, -1, 220, 1);
            Ui.Display(col, q.Character, Theme.FontLarge, Theme.Text, TextAnchor.MiddleLeft, "Name");
            Ui.Label(col, q.Epithet, Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleLeft, "Epithet");
            int chapter = RunSystem.QuestChapter(Content, State, q);
            float done = Mathf.Clamp01((chapter - 1) / (float)q.Chapters);
            Ui.Bar(col, done, Theme.Gold, QuestView.Progress(q, chapter), 40).Label.name = "ChapterBar";
            Ui.Label(col, q.BonusName + ": +" + p.CoinBonus + " coin" + (p.CoinBonus == 1 ? "" : "s") + " per gain", Theme.FontSmall, Theme.Gold, TextAnchor.MiddleLeft, "QuestBonus");
            Ui.Label(card, QuestView.Waiting(Content, State, q), Theme.FontSmall, Theme.Accent, TextAnchor.MiddleLeft, "Waiting");
            if (p.Completed) Note(card, "Visits after the story: " + p.EpiloguesSeen);
            Section(card, "The story so far");
            Entries(card, Content, State, q, 0);
        }

        /// <summary>The journal lines of a chain, oldest first (the last <paramref name="last"/> only when above 0).</summary>
        public static void Entries(Transform parent, ContentDb c, GameState s, RunQuestDef q, int last)
        {
            var p = RunSystem.QuestProgressOf(s, q.Id);
            var list = p?.Journal ?? new List<QuestJournalEntry>();
            if (list.Count == 0) { Ui.Label(parent, "Nothing written yet.", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleLeft, "JournalEmpty"); return; }
            int from = last > 0 ? Mathf.Max(0, list.Count - last) : 0;
            for (int i = from; i < list.Count; i++)
            {
                var e = list[i];
                var title = q.Node(e.NodeId)?.Title ?? e.NodeId;
                var row = Ui.Column(parent, 2, 0, null, "Entry:" + e.Chapter);
                Ui.Label(row, "Chapter " + e.Chapter + " · " + title, Theme.FontSmall, Theme.Gold, TextAnchor.MiddleLeft, "EntryTitle");
                var line = Ui.Label(row, e.Line, Theme.FontSmall, Theme.Text, TextAnchor.UpperLeft, "EntryLine");
                line.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
        }
    }
}
