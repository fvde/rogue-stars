using System;
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
    /// Run Quests on the run screen (docs/design/run-quests/recipe.md): the encounter card of a quest character — the stage
    /// with the worm and the character's portrait (drawn placeholder until the art exists), name plate, chapter progress, the
    /// scene text and the options with their cost / risk chips — the Legendary / Mythic skill picker of a sacrifice, and the
    /// friendly epilogue scenes after the chain. Every option is a button named "QuestOption:&lt;index&gt;".
    /// </summary>
    public sealed partial class RunScreen
    {
        private UiModal QuestModal(ActiveRun run)
        {
            var q = Content.RunQuest(run.Quest.QuestId);
            if (q == null) return Modal("Visitor", b => Ui.Label(b, "…", Theme.FontNormal), new ModalButton("Continue", () => Run(new RunQuestChoose { Index = 0 }), Theme.Accent));
            var epilogue = RunSystem.QuestOpenEpilogue(Content, run);
            var node = RunSystem.QuestOpenNode(Content, State, run);
            string title = epilogue != null ? epilogue.Title : node?.Title ?? q.Character;
            var root = RunScenes.Overlay(App.ModalLayer, "Quest", out var modal, out var frame, out float s);
            RunScenes.Stage(root, frame, s, Content, run, ScreenArt.Character(q.Portrait), OpenSkills, PauseModal, 520);

            // name plate and progress under the character
            var plate = Ui.RoundPanel(frame, new Color(0.08f, 0.06f, 0.14f, 0.82f), 30 * s, "QuestNameplate");
            plate.raycastTarget = false;
            RunScenes.At(plate.rectTransform, s, 790, 1010, 520, 150);
            var name = SliceKit.Chunky(plate.transform, q.Character, (int)(60 * s), Color.white, TextAnchor.UpperCenter, "QuestCharacter");
            Ui.Stretch(name.rectTransform, 10 * s, 12 * s, 10 * s, 70 * s);
            var sub = RunScenes.Text(plate.transform, q.Epithet + " · " + QuestView.Progress(q, RunSystem.QuestChapter(Content, State, q)), s, 34,
                new Color32(0xFF, 0xE0, 0x8A, 255), TextAnchor.LowerCenter, "QuestProgress", 24);
            Ui.Stretch(sub.rectTransform, 10 * s, 84 * s, 10 * s, 14 * s);

            RunScenes.Panel(root, frame, s, title, null);
            var text = RunScenes.Text(frame, epilogue != null ? epilogue.Text : node?.Text ?? "", s, 44, RunScenes.Ink, TextAnchor.MiddleCenter, "QuestText", 30);
            RunScenes.At(text.rectTransform, s, 540, 1560, 960, 330);

            if (epilogue != null)
            {
                var v = new QuestOptionView { Label = epilogue.Option, Detail = "A friendly visit. No strings attached.", Chip = QuestView.EpilogueReward(epilogue), ChipColor = Theme.Good, Enabled = true };
                QuestOptionCard(frame, s, 0, 1, v, () => { modal.Close(); CloseDecision(); Run(new RunQuestChoose { Index = 0 }); });
            }
            else if (node != null)
            {
                var views = QuestView.Options(Content, State, run, q, node);
                for (int i = 0; i < views.Count; i++)
                {
                    int index = i;
                    var v = views[i];
                    QuestOptionCard(frame, s, i, views.Count, v, () =>
                    {
                        if (v.PicksSkill) { QuestSkillPicker(run, node, index, modal); return; }
                        modal.Close(); CloseDecision(); Run(new RunQuestChoose { Index = index });
                    });
                }
            }
            var journal = Ui.Button(frame, "Story so far", () => QuestJournalModal(q), SliceCards.Purple, 330 * s, 96 * s, true, (int)(34 * s));
            journal.name = "QuestJournal";
            RunScenes.At((RectTransform)journal.transform, s, 200, 1180, 330, 96);
            if (_auto)
            {
                var hint = RunScenes.Text(frame, "Auto mode waits for your choice.", s, 32, RunScenes.Ink, TextAnchor.MiddleCenter, "AutoHint");
                RunScenes.At(hint.rectTransform, s, 540, 2350, 900, 50);
            }
            UiTween.PopIn(frame, 0.12f);
            return modal;
        }

        /// <summary>One option of the encounter: a wide button with its label, a detail line and the cost / risk chip on the right.</summary>
        private static Button QuestOptionCard(RectTransform frame, float s, int i, int n, QuestOptionView v, Action onClick)
        {
            float h = n <= 2 ? 200 : 170, gap = 22;
            float y0 = 1800 + (n <= 2 ? 40 : 0);
            float y = y0 + i * (h + gap);
            var btn = Ui.Button(frame, "", onClick, v.Enabled ? RunScenes.TileFill : (Color)new Color32(0x8A, 0x86, 0x96, 255), 980 * s, h * s, v.Enabled, 20);
            btn.name = "QuestOption:" + i;
            var brt = (RectTransform)btn.transform;
            RunScenes.At(brt, s, 540, y, 980, h);
            var le = btn.GetComponent<LayoutElement>(); if (le != null) le.ignoreLayout = true;
            UiShapes.Outline(brt, SliceKit.Ink, Theme.RadiusButton, 6);
            var label = SliceKit.Chunky(brt, v.Label, (int)(52 * s), Color.white, TextAnchor.UpperLeft, "OptionLabel");
            Ui.Stretch(label.rectTransform, 34 * s, 20 * s, 360 * s, h * 0.45f * s);
            label.resizeTextForBestFit = true; label.resizeTextMaxSize = label.fontSize; label.resizeTextMinSize = (int)(34 * s);
            var detail = RunScenes.Text(brt, v.Detail ?? "", s, 32, new Color(1, 1, 1, 0.86f), TextAnchor.UpperLeft, "OptionDetail", 22);
            Ui.Stretch(detail.rectTransform, 34 * s, h * 0.5f * s, 360 * s, 12 * s);
            if (!string.IsNullOrEmpty(v.Chip))
            {
                var chip = Ui.RoundPanel(brt, v.ChipColor, 28 * s, "OptionChip");
                chip.raycastTarget = false;
                var crt = chip.rectTransform;
                crt.anchorMin = crt.anchorMax = new Vector2(1, 0.5f); crt.pivot = new Vector2(1, 0.5f);
                crt.sizeDelta = new Vector2(320 * s, 86 * s); crt.anchoredPosition = new Vector2(-24 * s, 0);
                UiShapes.Outline(crt, SliceKit.Ink, 28 * s, 4);
                var ct = SliceKit.Chunky(chip.transform, v.Chip, (int)(34 * s), Theme.TextOn(v.ChipColor) == Theme.OnAccent ? (Color)SliceKit.Ink : Color.white, TextAnchor.MiddleCenter, "ChipText");
                Ui.Stretch(ct.rectTransform, 10 * s, 6 * s, 10 * s, 6 * s);
                ct.horizontalOverflow = HorizontalWrapMode.Wrap; ct.resizeTextForBestFit = true; ct.resizeTextMaxSize = ct.fontSize; ct.resizeTextMinSize = (int)(22 * s);
            }
            return btn;
        }

        /// <summary>A Legendary / Mythic skill sacrifice: the player picks which owned skill of that tier to give.</summary>
        private void QuestSkillPicker(ActiveRun run, QuestNodeDef node, int index, UiModal parent)
        {
            var choices = RunSystem.QuestSkillChoices(Content, run, node.Cost.Kind);
            Modal(node.Cost.Kind == QuestCostKind.MythicSkill ? "Give a Mythic skill" : "Give a Legendary skill", b =>
            {
                Ui.Label(b, "Pick the skill you give up for the rest of this run.", Theme.FontSmall, Theme.TextDim);
                foreach (var id in choices)
                {
                    string sid = id;
                    var sk = Content.Skill(sid);
                    var btn = Ui.Button(b, (sk?.Name ?? sid) + "  ·  " + RunTuning.RunTier(sk?.Rarity ?? Rarity.Common), () =>
                    {
                        parent.Close(); CloseDecision(); Run(new RunQuestChoose { Index = index, SkillId = sid });
                    }, RunPresentation.SkillColor(sk));
                    btn.name = "QuestGive:" + sid;
                }
            }, new ModalButton("Back", null));
        }

        /// <summary>The story so far (the journal) on top of the encounter.</summary>
        private void QuestJournalModal(RunQuestDef q)
        {
            Modal(q.Character + " · story so far", b => JournalScreen.Entries(b, Content, State, q, 12), new ModalButton("Close", null));
        }
    }
}
