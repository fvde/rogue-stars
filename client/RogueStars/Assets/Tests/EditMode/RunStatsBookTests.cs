using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Run screen playtest: the skills book's Stats tab (every battle stat of the worm right now, read from the battle's own
    /// stat block, grouped, zeros hidden but Core) and the smaller, lower dice stand that clears the speed chip, the skills
    /// book and the revive buttons at 1080×2340 and 720×1600.
    /// </summary>
    public sealed class RunStatsBookTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; RunFx.Instant = true; }
        [TearDown] public void TearDown() { RunScreen.InstantOutcomes = false; RunFx.Instant = false; _f.Dispose(); }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static string Texts(Transform root) =>
            string.Join(" | ", root.GetComponentsInChildren<Text>(true).Where(t => t.gameObject.activeInHierarchy).Select(t => t.text));

        private ActiveRun StartRun()
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            return S.Run.Active;
        }

        /// <summary>A run with a few drafted skills (passive packages) and run stats (perks / buffs).</summary>
        private ActiveRun BuiltRun()
        {
            var run = StartRun();
            int n = 0;
            foreach (var s in C.Skills.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (n >= 8) break;
                if (s.Source != SkillSource.Run || !s.Draftable || s.Signature || s.RequiresFamilyCount > 0 || s.Requires.Count > 0) continue;
                run.Skills.Add(s.Id);
                n++;
            }
            run.RunStats.Add(new StatMod(StatId.LifeSteal, 7));
            run.RunStats.Add(new StatMod(StatId.Atk, 0, 50));
            return run;
        }

        [Test]
        public void StatSheet_ListsEveryBattleStatOnce()
        {
            var listed = RunStatSheet.Groups.SelectMany(g => g.Stats).ToList();
            Assert.That(listed, Is.Unique, "a stat sits in one group");
            foreach (StatId id in Enum.GetValues(typeof(StatId)))
                Assert.That(listed.Contains(id) || RunStatSheet.Hidden.Contains(id), Is.True, id + " has a group");
            Assert.That(RunStatSheet.Groups.Select(g => g.Title), Is.EqualTo(new[] { "Core", "Offense", "Defense", "Sustain", "Status & Synergy", "Run" }));
        }

        [Test]
        public void StatSheet_ShowsTheBattleBlock_CoreAlways_ZerosHidden()
        {
            var run = BuiltRun();
            run.WormHp = 10;
            var w = RunSystem.WormStats(C, run);
            var sheet = RunStatSheet.Build(C, run);
            var rows = sheet.SelectMany(g => g.Rows).ToDictionary(r => r.Stat);
            Assert.That(sheet[0].Title, Is.EqualTo("Core"));
            Assert.That(sheet[0].Rows.Select(r => r.Stat), Is.EqualTo(new[] { StatId.Hp, StatId.Atk, StatId.Def, StatId.Speed }));
            Assert.That(rows[StatId.Hp].Value, Is.EqualTo("10 / " + RogueStars.Client.App.Fmt.Num(w.MaxHp)), "current / max HP");
            Assert.That(rows[StatId.Atk].Value, Is.EqualTo(RogueStars.Client.App.Fmt.Num(Math.Round(w.Atk))), "the run's ATK incl. the +50%");
            Assert.That(rows[StatId.LifeSteal].Value, Is.EqualTo(RunStatSheet.Value(StatId.LifeSteal, w.Stats.Value(StatId.LifeSteal))));
            Assert.That(w.Stats.Value(StatId.LifeSteal), Is.GreaterThanOrEqualTo(7), "the run stat counts");
            foreach (var g in RunStatSheet.Groups)
                foreach (var id in g.Stats)
                {
                    bool core = g.Title == "Core";
                    double v = w.Stats.Value(id);
                    if (core) Assert.That(rows.ContainsKey(id), Is.True, id + " (core) always shows");
                    else if (Math.Abs(v) < 0.05) Assert.That(rows.ContainsKey(id), Is.False, id + " is 0: hidden");
                    else Assert.That(rows[id].Value, Is.EqualTo(RunStatSheet.Value(id, v)), id + " from the battle block");
                }
            Assert.That(RunStatSheet.Value(StatId.CritChance, 42), Is.EqualTo("42%"));
            Assert.That(RunStatSheet.Value(StatId.Regen, 2.5), Is.EqualTo("2.5%"));
            Assert.That(RunStatSheet.Value(StatId.DamageTaken, 20), Is.EqualTo("+20%"));
            Assert.That(RunStatSheet.Value(StatId.Def, 20.4), Is.EqualTo("20"));
        }

        [Test]
        public void SkillsBook_HasSkillsAndStatsTabs()
        {
            var run = BuiltRun();
            _f.App.Router.Push(new RunScreen());
            FindNamed(_f.App.Router.Current.Root, "SkillsButton").GetComponent<Button>().onClick.Invoke();
            var book = FindNamed(_f.App.ModalLayer, "Modal:Skills");
            Assert.That(book, Is.Not.Null);
            Assert.That(FindNamed(book, "Skill:" + run.Skills[run.Skills.Count - 1]), Is.Not.Null, "opens on the Skills tab");
            Assert.That(FindNamed(book, "StatGroup:Core"), Is.Null);

            FindNamed(book, "Tab:Stats").GetComponent<Button>().onClick.Invoke();
            Assert.That(FindNamed(book, "Skill:" + run.Skills[run.Skills.Count - 1]), Is.Null, "the skills make room for the stats");
            foreach (var g in RunStatSheet.Build(C, run))
            {
                Assert.That(FindNamed(book, "StatGroup:" + g.Title), Is.Not.Null, g.Title);
                foreach (var r in g.Rows)
                {
                    var row = FindNamed(book, "Stat:" + r.Stat);
                    Assert.That(row, Is.Not.Null, r.Stat.ToString());
                    Assert.That(Texts(row), Does.Contain(r.Label).And.Contain(r.Value));
                }
            }
            Assert.That(FindNamed(book, "Stat:LifeSteal"), Is.Not.Null);

            FindNamed(book, "Tab:Skills").GetComponent<Button>().onClick.Invoke();
            Assert.That(FindNamed(book, "Skill:" + run.Skills[run.Skills.Count - 1]), Is.Not.Null, "back to the skills");
            Assert.That(FindNamed(book, "StatGroup:Core"), Is.Null);
        }

        private static Rect WorldRect(Transform t)
        {
            var c = new Vector3[4];
            ((RectTransform)t).GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        [Test]
        public void DiceStand_ClearsTheSpeedChipTheBookAndTheReviveButtons()
        {
            // the stand's widest drawn part: the slab (top and bottom edge) and the column's top, in frame px
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var ring in new[]
            {
                DiceStand.Ring(DiceStand.SlabHalfW, DiceStand.SlabHalfD, DiceStand.SlabRadius, 0),
                DiceStand.Ring(DiceStand.SlabHalfW, DiceStand.SlabHalfD, DiceStand.SlabRadius, -DiceStand.SlabThick),
                DiceStand.Ring(DiceStand.ColHalfW, DiceStand.ColHalfD, DiceStand.ColRadius, -DiceStand.SlabThick),
            })
                foreach (var v in ring) { float x = DiceStand.Project(v).x; minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }

            var run = StartRun();
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            foreach (var size in new[] { new Vector2(1080, 2340), new Vector2(720, 1600) })
            {
                float k = Mathf.Sqrt(size.x / 1080f * (size.y / 1920f)); // CanvasScaler: reference 1080 × 1920, match 0.5
                _f.Canvas.sizeDelta = size / k;
                run.Phase = RunPhase.AwaitRoll; run.WormHp = -1;
                screen.Rebuild();
                string at = size.x + "x" + size.y;
                var stand = WorldRect(FindNamed(screen.Root, "Pedestal"));
                float left = stand.xMin + minX / DiceStand.FrameW * stand.width, right = stand.xMin + maxX / DiceStand.FrameW * stand.width;
                float capTop = stand.yMin + DiceStand.CapTop / DiceStand.FrameH * stand.height;
                var chip = WorldRect(FindNamed(screen.Root, "SpeedChip"));
                var book = WorldRect(FindNamed(screen.Root, "SkillsButton"));
                Assert.That(left, Is.GreaterThan(chip.xMax), at + ": the stand clears the speed chip");
                Assert.That(right, Is.LessThan(book.xMin), at + ": the stand clears the skills book");
                var holder = WorldRect(FindNamed(screen.Root, "BoardHolder"));
                Assert.That(right - left, Is.GreaterThan(0.45f * holder.width), at + ": still the big thumb target");
                Assert.That(stand.yMin, Is.LessThan(holder.yMin), at + ": the column runs off the bottom");

                // revive: "Give up" stays above the cap
                run.Phase = RunPhase.Defeated; run.WormHp = 0;
                screen.Rebuild();
                var giveUp = FindNamed(_f.App.ModalLayer, "Button:Give up");
                Assert.That(giveUp, Is.Not.Null, at + ": the revive sheet");
                Assert.That(WorldRect(giveUp).yMin, Is.GreaterThan(capTop), at + ": Give up clears the dice cap");
                Ui.Clear(_f.App.ModalLayer);
            }
        }
    }
}
