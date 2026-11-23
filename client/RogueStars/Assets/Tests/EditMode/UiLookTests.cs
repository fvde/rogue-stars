using System.Collections.Generic;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>The game look: UI kit, portal level map node states, run board layout from the run state, battle animation skip mode.</summary>
    public sealed class UiLookTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() { BattlePlaybackScreen.Instant = false; _f.Dispose(); }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static string Texts(Transform root)
        {
            var parts = new List<string>();
            foreach (var t in root.GetComponentsInChildren<Text>(true)) if (t.gameObject.activeInHierarchy) parts.Add(t.text);
            return string.Join(" | ", parts);
        }

        // ---- UI kit ----

        [Test]
        public void Kit_RoundedButtonsWithLip_AndTheDisplayFont()
        {
            Assert.That(Ui.DisplayFont, Is.Not.SameAs(Ui.Font), "Fredoka is imported from Resources/Fonts");
            Assert.That(Ui.FontFor("START RUN"), Is.SameAs(Ui.DisplayFont));
            Assert.That(Ui.FontFor("★★☆"), Is.SameAs(Ui.Font), "glyphs missing in the display font fall back to the body font");
            var b = Ui.Button(_f.Canvas, "START RUN", null, Theme.Accent);
            Assert.That(b.name, Is.EqualTo("Button:START RUN"));
            var face = (Image)b.targetGraphic;
            Assert.That(face.name, Is.EqualTo("Face"));
            Assert.That(face.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(face.sprite, Is.SameAs(UiShapes.Fill));
            Assert.That(b.GetComponent<Image>().color, Is.EqualTo(Theme.AccentLip), "darker lip under the orange face");
            Assert.That(b.GetComponentInChildren<Text>().color, Is.EqualTo(Theme.OnAccent), "dark caption on a bright face");
            Assert.That(Theme.TextOn(Theme.Bad), Is.EqualTo(Theme.Text));
            Ui.SetButtonColor(b, Theme.Bad);
            Assert.That(b.GetComponentInChildren<Text>().color, Is.EqualTo(Theme.Text));
            var chip = Ui.Chip(_f.Canvas, "BURN 2", Theme.Accent, Theme.OnAccent);
            Assert.That(chip.GetComponent<Image>().sprite, Is.SameAs(UiShapes.Fill));
            Assert.That(UiShapes.Ring(4), Is.SameAs(UiShapes.Ring(4)), "ring sprites are cached per thickness");
        }

        // ---- portal map ----

        [Test]
        public void PortalMap_NodeStates()
        {
            Assert.That(PortalsScreen.NodeStateOf(true, false, true), Is.EqualTo(PortalsScreen.MapNode.Next));
            Assert.That(PortalsScreen.NodeStateOf(true, true, true), Is.EqualTo(PortalsScreen.MapNode.Next), "a replayable next level still glows");
            Assert.That(PortalsScreen.NodeStateOf(true, true, false), Is.EqualTo(PortalsScreen.MapNode.Cleared));
            Assert.That(PortalsScreen.NodeStateOf(true, false, false), Is.EqualTo(PortalsScreen.MapNode.Open));
            Assert.That(PortalsScreen.NodeStateOf(false, false, false), Is.EqualTo(PortalsScreen.MapNode.Locked));
            Assert.That(PortalsScreen.NodeStateOf(false, false, true), Is.EqualTo(PortalsScreen.MapNode.Locked));
            for (int i = 0; i < 60; i++)
            {
                Assert.That(PortalsScreen.MapX(i), Is.InRange(0.2f, 0.8f));
                Assert.That(Mathf.Abs(PortalsScreen.MapX(i + 1) - PortalsScreen.MapX(i)), Is.GreaterThan(0.05f), "neighbouring nodes zig-zag");
            }
        }

        [Test]
        public void PortalMap_FreshSave_NextGlows_LaterDimmed_BossAtThePortalEnd()
        {
            _f.App.Open("portals");
            var screen = (PortalsScreen)_f.App.Router.Current;
            var n1 = FindNamed(screen.Root, "Level:" + C.MainLevelId(1));
            var n2 = FindNamed(screen.Root, "Level:" + C.MainLevelId(2));
            var n10 = FindNamed(screen.Root, "Level:" + C.MainLevelId(10));
            Assert.That(FindNamed(n1, "Glow"), Is.Not.Null, "next level glows");
            Assert.That(FindNamed(n2, "Glow"), Is.Null);
            Assert.That(n2.GetComponent<Button>().interactable, Is.False);
            Assert.That(Texts(n10), Does.Contain("BOSS · 10").And.Contain("CHEST"));
            Assert.That(((RectTransform)n10).sizeDelta, Is.EqualTo(PortalsScreen.BossSize));
            // the path climbs: level 1 below level 2, level 10 at the top of its portal
            Assert.That(((RectTransform)n1).anchoredPosition.y, Is.LessThan(((RectTransform)n2).anchoredPosition.y));
            Assert.That(((RectTransform)n10).anchoredPosition.y, Is.GreaterThan(((RectTransform)n2).anchoredPosition.y));
            Assert.That(FindNamed(screen.Root, "Connector"), Is.Not.Null, "connector dots between nodes");
            // bottom card: the next level, its three star goals and START RUN with the stamina cost
            Assert.That(screen.SelectedLevel.Id, Is.EqualTo(C.MainLevelId(1)));
            var footer = screen.Root.Find("Footer");
            Assert.That(Texts(footer), Does.Contain("Level 1 ·").And.Contain("Clear the level").And.Contain("stamina"));
            Assert.That(FindNamed(footer, "StartRun").GetComponent<Button>().interactable, Is.True);
        }

        [Test]
        public void PortalMap_ClearedLevelShowsStars_SelectingANodeFillsTheCard()
        {
            S.Counters[Requirements.MainLevelCounter] = 1;
            S.Run.Levels[C.MainLevelId(1)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _f.App.Open("portals");
            var screen = (PortalsScreen)_f.App.Router.Current;
            var n1 = FindNamed(screen.Root, "Level:" + C.MainLevelId(1));
            Assert.That(Texts(n1), Does.Contain("★★☆"));
            Assert.That(FindNamed(n1, "Glow"), Is.Null);
            Assert.That(FindNamed(FindNamed(screen.Root, "Level:" + C.MainLevelId(2)), "Glow"), Is.Not.Null, "level 2 is next");
            Assert.That(screen.Subtitle, Does.Contain("1 of " + C.Campaign.LevelsPerPortal + " cleared").And.Contain("2 of " + 3 * C.Campaign.LevelsPerPortal + " stars"));
            n1.GetComponent<Button>().onClick.Invoke();
            Assert.That(screen.SelectedLevel.Id, Is.EqualTo(C.MainLevelId(1)));
            n1 = FindNamed(screen.Root, "Level:" + C.MainLevelId(1));
            Assert.That(FindNamed(n1, "SelectedRing"), Is.Not.Null);
            Assert.That(Texts(screen.Root.Find("Footer")), Does.Contain("Level 1 ·"));
        }

        // ---- run board ----

        [Test]
        public void RunBoard_LooksFollowTheRunState()
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            var run = S.Run.Active;
            run.Tiles.Clear();
            string[] ids = { "start", "shop", "empty", "empty", "empty", "empty", "empty", "empty", "empty", "boss_gate" };
            for (int i = 0; i < ids.Length; i++)
            {
                var t = new RunTile { Id = i, TileId = ids[i], X = i };
                if (i + 1 < ids.Length) t.Next.Add(i + 1);
                run.Tiles.Add(t);
            }
            run.Tiles[2].Next.Add(5); // a fork after tile 2
            run.BossGateTile = -1;    // the arena is not open yet: the whole window is drawn
            run.BoardEnd = ids.Length - 1;
            run.Trail.Clear(); run.Trail.AddRange(new[] { 0, 1, 2 });
            run.Position = 2;

            var start = RunScreen.LookOf(run, run.Tiles[0], C);
            var shop = RunScreen.LookOf(run, run.Tiles[1], C);
            var cur = RunScreen.LookOf(run, run.Tiles[2], C);
            var ahead = RunScreen.LookOf(run, run.Tiles[3], C);
            var gate = RunScreen.LookOf(run, run.Tiles[ids.Length - 1], C);
            Assert.That(start.Label, Is.EqualTo("START"));
            Assert.That(start.Visited && shop.Visited, Is.True, "walked tiles");
            Assert.That(shop.Landed, Is.False, "passed, never landed on: keeps its look");
            Assert.That(shop.Label, Is.EqualTo("SHOP"));
            Assert.That(shop.Accent, Is.EqualTo(Theme.Blue));
            Assert.That(cur.Current && !cur.Visited && cur.Fork, Is.True);
            Assert.That(ahead.Visited || ahead.Current, Is.False);
            Assert.That(gate.Gate, Is.True);
            run.Phase = RunPhase.ChooseBranch;
            run.BranchOptions.Clear(); run.BranchOptions.AddRange(new[] { 3, 5 });
            Assert.That(RunScreen.LookOf(run, run.Tiles[5], C).Option, Is.True);
            run.Phase = RunPhase.AwaitRoll;
            run.BranchOptions.Clear();

            // the board is drawn flipped: the path starts at the bottom and climbs band by band
            float h = RunScreen.BoardHeight(run);
            var first = RunScreen.DrawPoint(RunScreen.TileCenter(new RunTile { X = 0 }), h);
            var secondBand = RunScreen.DrawPoint(RunScreen.TileCenter(new RunTile { X = RunScreen.Columns }), h);
            Assert.That(secondBand.y, Is.GreaterThan(first.y));

            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            for (int i = 0; i < ids.Length; i++) Assert.That(FindNamed(screen.Root, "Tile:" + i), Is.Not.Null);
            Assert.That(FindNamed(screen.Root, "Token"), Is.Not.Null);
            Assert.That(FindNamed(FindNamed(screen.Root, "Token"), "Worm").GetComponent<UiBob>(), Is.Not.Null, "the worm bobs");
            Assert.That(FindNamed(screen.Root, "BossBanner"), Is.Not.Null);
            Assert.That(screen.Title, Does.Contain(level.Name));
            Assert.That(screen.Subtitle, Does.StartWith("Roll 0/"));
            Assert.That(FindNamed(screen.Root, "RollButton"), Is.Not.Null);
            Assert.That(FindNamed(screen.Root, "AutoButton"), Is.Not.Null);
            Assert.That(FindNamed(screen.Root, "Slots").GetComponentsInChildren<Button>().Length, Is.EqualTo(RunScreen.SkillSlots));
        }

        [Test]
        public void EventOptions_ShowGainAndRiskChips()
        {
            EventOption surge = null;
            foreach (var ev in C.RunEvents.Values)
                foreach (var o in ev.Options)
                    if (o.Outcomes.Exists(oc => oc.Effects.Exists(fx => fx.Kind == RunEffectKind.GainSkill)) &&
                        o.Outcomes.Exists(oc => oc.Effects.Exists(fx => fx.Kind == RunEffectKind.BossStatPct))) surge = o;
            Assert.That(surge, Is.Not.Null, "an option trading a skill for a stronger boss");
            var tags = RunScreen.OptionTags(surge);
            Assert.That(tags.Exists(t => t.Key && t.Value.StartsWith("GAIN")), Is.True);
            Assert.That(tags.Exists(t => !t.Key && t.Value.StartsWith("RISK boss")), Is.True);
            var paid = new EventOption { Text = "Buy", CoinCost = 25 };
            Assert.That(RunScreen.OptionTags(paid)[0].Value, Is.EqualTo("COST 25 coins"));
        }

        // ---- battle ----

        private BattleResult SampleBattle()
        {
            var setup = new BattleSetup { RoundCap = 8 };
            var lo = new CombatLoadout { MountId = "skiff" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 3000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Speed, 100));
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, C));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(C, "cinder_hound", 3));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(C, "cinder_hound", 3));
            return BattleSimulator.Simulate(setup, C, new Rng(11));
        }

        [Test]
        public void Battle_InstantMode_SkipsTheAnimationsAndEndsAtTheLoggedHp()
        {
            var battle = SampleBattle();
            _f.App.Router.Reset(new HubScreen());
            BattlePlaybackScreen.Instant = true;
            var screen = new BattlePlaybackScreen(battle);
            _f.App.Router.Push(screen);
            screen.Frame(0.001f);
            Assert.That(screen.Finished, Is.True, "one frame plays the whole log");
            for (int i = 0; i < battle.Fighters.Count; i++)
            {
                Assert.That(screen.TryGetBars(i, out double hp, out _, out _), Is.True);
                if (battle.Fighters[i].Alive) Assert.That(hp, Is.EqualTo(battle.Fighters[i].EndHp), battle.Fighters[i].Name);
            }
            Assert.That(FindNamed(screen.Root, "Popup"), Is.Null, "no floating numbers in skip mode");
            Assert.That(Texts(screen.Root), Does.Contain(battle.PlayerWon() ? "VICTORY" : battle.EndReason == BattleEndReason.RoundCap ? "TIME UP" : "DEFEAT"));
        }

        [Test]
        public void Battle_AnimatedSteps_SettleDeterministically()
        {
            var battle = SampleBattle();
            _f.App.Router.Reset(new HubScreen());
            var screen = new BattlePlaybackScreen(battle);
            _f.App.Router.Push(screen);
            Assert.That(FindNamed(screen.Root, "TurnOrder").GetComponentsInChildren<Image>().Length, Is.GreaterThan(0), "turn order strip");
            for (int guard = 0; guard < 10000 && !screen.Finished; guard++) screen.Frame(screen.StepSeconds); // animate = true
            Assert.That(screen.Finished, Is.True);
            // in EditMode every tween completes at once: fighters are back at their places, no leftover popups
            var players = battle.Fighters.FindAll(f => f.Side == BattleSide.Player);
            var enemies = battle.Fighters.FindAll(f => f.Side != BattleSide.Player);
            foreach (var f in battle.Fighters)
            {
                var root = (RectTransform)FindNamed(screen.Root, "Fighter:" + f.Index);
                var side = f.Side == BattleSide.Player ? players : enemies;
                Assert.That(root.anchoredPosition, Is.EqualTo(BattlePlaybackScreen.StagePosition(f.Side, side.IndexOf(f), side.Count)), f.Name + " back home");
                Assert.That(((RectTransform)root.Find("Sprite")).anchoredPosition, Is.EqualTo(new Vector2(0, 46)));
                if (!f.Alive && f.SummonedBy < 0) Assert.That(root.GetComponent<CanvasGroup>().alpha, Is.LessThan(0.5f), f.Name + " faded out");
            }
            Assert.That(FindNamed(screen.Root, "Popup"), Is.Null);
            Assert.That(FindNamed(screen.Root, "Speed3"), Is.Not.Null);
        }
    }
}
