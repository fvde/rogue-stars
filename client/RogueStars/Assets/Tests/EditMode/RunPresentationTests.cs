using System;
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
    /// <summary>
    /// Run presentation (playtest feedback): tumbling dice that land on the rolled value (skip mode instant), a follow
    /// camera that never snaps, only landed tiles coloured, feedback popups for every landing / outcome, the HUD's
    /// HP / ATK / DEF / run level / XP with run modifications, skills coloured by rarity, no result sheet after a shop.
    /// </summary>
    public sealed class RunPresentationTests
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

        private static List<Transform> AllNamed(Transform root, string prefix)
        {
            var list = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith(prefix)) list.Add(t);
            return list;
        }

        /// <summary>A level-1 run on a straight board of the given tiles after the start tile.</summary>
        private ActiveRun StartRun(params string[] tiles)
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            var run = S.Run.Active;
            run.Tiles.Clear();
            var all = new List<string> { "start" };
            all.AddRange(tiles);
            while (all.Count < 30) all.Add("empty");
            for (int i = 0; i < all.Count; i++)
            {
                var t = new RunTile { Id = i, TileId = all[i], X = i };
                if (i + 1 < all.Count) t.Next.Add(i + 1);
                run.Tiles.Add(t);
            }
            run.BoardEnd = all.Count - 1; run.BossGateTile = -1; run.Position = 0; run.Trail.Clear(); run.Trail.Add(0);
            return run;
        }

        private RunScreen Open()
        {
            _f.App.Router.Push(new RunScreen());
            return (RunScreen)_f.App.Router.Current;
        }

        private void CloseBattles()
        {
            while (_f.App.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); _f.App.Router.Pop(); }
        }

        // ---- 1. dice ----

        [Test]
        public void Dice_SkipMode_LandsOnTheRolledValueAtOnce()
        {
            Assert.That(RunFx.Skip, Is.True);
            Assert.That(RunFx.DiceSeconds(false), Is.EqualTo(0));
            for (int v = 1; v <= 6; v++)
            {
                Assert.That(RunFx.DiceFaceAt(1, v, 0), Is.EqualTo(v), "lands on the value");
                Assert.That(RunFx.DiceFaceAt(RunFx.DiceSettleAt, v, 1), Is.EqualTo(v));
                Assert.That(RunFx.Pips(v).Count, Is.EqualTo(v));
                var seen = new HashSet<int>();
                for (float t = 0; t < RunFx.DiceSettleAt - 0.001f; t += 0.01f)
                {
                    int f = RunFx.DiceFaceAt(t, v, 0);
                    Assert.That(f, Is.InRange(1, 6));
                    Assert.That(f, Is.Not.EqualTo(v), "tumbling faces never show the result early");
                    seen.Add(f);
                }
                Assert.That(seen.Count, Is.GreaterThanOrEqualTo(3), "the die tumbles through several faces");
            }
            // the tumble ends straight and unsquashed on its value
            RunFx.DicePose(1, 0, out float angle, out float height, out Vector2 scale);
            Assert.That(Mathf.Abs(angle), Is.LessThan(0.01f));
            Assert.That(height, Is.EqualTo(0));
            Assert.That(scale.x, Is.EqualTo(1).Within(0.01f));

            // on the screen: two dice for a two-dice level, showing the rolled values, no tumbling stage left behind
            var run = StartRun();
            run.Dice = 2;
            var screen = Open();
            Assert.That(_f.Service.ExecuteNow(new RunRoll()).Ok, Is.True);
            CloseBattles();
            var a = S.Run.Active;
            Assert.That(a.LastDice.Count, Is.EqualTo(2));
            Assert.That(FindNamed(screen.Root, "DiceStage"), Is.Null, "skip mode: no tumbling dice");
            var footer = FindNamed(screen.Root, "FooterDice");
            Assert.That(footer, Is.Not.Null);
            var faces = new List<int>();
            foreach (Transform die in footer) faces.Add(die.GetComponentsInChildren<Image>(true).Length - 2); // face + border + pips
            Assert.That(faces, Is.EqualTo(a.LastDice), "the footer dice show the rolled pips");
            Assert.That(RunFx.DiceSeconds(true), Is.LessThanOrEqualTo(RunFx.DiceSeconds(false)), "auto never rolls slower");
        }

        // ---- 2. camera ----

        [Test]
        public void Camera_StepIsBoundedPerFrame_AndConverges()
        {
            float dt = 1f / 60;
            float cam = 0, target = 5000;
            for (int i = 0; i < 600; i++)
            {
                float next = RunFx.CameraStep(cam, target, dt);
                Assert.That(Mathf.Abs(next - cam), Is.LessThanOrEqualTo(RunFx.CameraMaxSpeed * dt + 0.001f), "frame " + i);
                Assert.That(next, Is.LessThanOrEqualTo(target + 0.001f), "never overshoots");
                cam = next;
            }
            Assert.That(cam, Is.EqualTo(target).Within(1f));
            Assert.That(RunFx.CameraStep(float.NaN, 12, dt), Is.EqualTo(12), "first placement");
        }

        [Test]
        public void Camera_FollowsTheWormSmoothly_NoSnapAtRollOrRebuild()
        {
            StartRun();
            S.Run.Active.ExtraDicePermanent = 5; // long moves: the worm climbs a band or more
            var screen = Open();
            float dt = 1f / 60;
            for (int i = 0; i < 300; i++) screen.Frame(dt); // settle on the start
            for (int roll = 0; roll < 4 && S.Run.Active != null; roll++)
            {
                float before = screen.CameraY;
                if (S.Run.Active.Phase != RunPhase.AwaitRoll) break;
                Assert.That(_f.Service.ExecuteNow(new RunRoll()).Ok, Is.True);
                CloseBattles();
                if (S.Run.Active == null || !(_f.App.Router.Current is RunScreen)) break;
                Assert.That(screen.CameraY, Is.EqualTo(before).Within(0.01f), "a roll / rebuild never resets the camera");
                float prev = screen.CameraY, moved = 0;
                for (int i = 0; i < 400; i++)
                {
                    screen.Frame(dt);
                    float step = Mathf.Abs(screen.CameraY - prev);
                    Assert.That(step, Is.LessThanOrEqualTo(RunFx.CameraMaxSpeed * dt + 0.01f), "eased, bounded per frame");
                    moved += step;
                    prev = screen.CameraY;
                }
                if (S.Run.Active.Position > 0) Assert.That(moved, Is.GreaterThan(0), "the camera followed the worm");
                // it ends centred near the token (a little ahead of it)
                var token = (RectTransform)FindNamed(screen.Root, "Token");
                var viewport = (RectTransform)FindNamed(screen.Root, "BoardHolder");
                var corners = new Vector3[4];
                token.GetWorldCorners(corners);
                var mid = viewport.InverseTransformPoint((corners[0] + corners[2]) / 2);
                Assert.That(Mathf.Abs(mid.y - viewport.rect.center.y), Is.LessThan(viewport.rect.height * 0.35f), "token in view");
                while (S.Run.Active != null && S.Run.Active.Phase != RunPhase.AwaitRoll)
                {
                    if (!_f.Service.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = true }).Ok) break;
                    CloseBattles();
                }
            }
        }

        // ---- 3. landed tiles only ----

        [Test]
        public void OnlyTheLandedTileIsColoured_PassedTilesKeepTheirLook()
        {
            var buffs = new string[24];
            for (int i = 0; i < buffs.Length; i++) buffs[i] = "buff_atk";
            var run = StartRun(buffs);
            run.Trail.Clear(); run.Trail.AddRange(new[] { 0, 1, 2, 3 });
            run.Position = 3;
            run.Tiles[3].TimesResolved = 1; run.Tiles[3].Cleared = true;
            run.Tiles[2].Cleared = true; // a passed pickup resolved by the rules: still drawn as passed
            Assert.That(RunScreen.LookOf(run, run.Tiles[1], C).Landed, Is.False);
            Assert.That(RunScreen.LookOf(run, run.Tiles[3], C).Landed, Is.True);
            var screen = Open();
            Color Fill(int id) => FindNamed(screen.Root, "Tile:" + id).GetComponent<Image>().color;
            Assert.That(Fill(1), Is.EqualTo(Fill(20)), "a passed tile looks like an unwalked one");
            Assert.That(Fill(2), Is.EqualTo(Fill(20)), "a passed (resolved) tile looks like an unwalked one");
            Assert.That(Fill(3), Is.Not.EqualTo(Fill(20)), "the landed tile is coloured");
            Assert.That(FindNamed(FindNamed(screen.Root, "Tile:3"), "LandMark"), Is.Not.Null, "landed marker");
            Assert.That(FindNamed(FindNamed(screen.Root, "Tile:1"), "LandMark"), Is.Null);

            // a real move: of the tiles the worm hops over only the one it stops on is marked
            var r2 = S.Run.Active;
            int from = r2.Position;
            Assert.That(_f.Service.ExecuteNow(new RunRoll()).Ok, Is.True);
            CloseBattles();
            int land = S.Run.Active.Position;
            Assert.That(screen.LastLandedTile, Is.EqualTo(land));
            for (int id = from + 1; id < land; id++)
            {
                Assert.That(FindNamed(FindNamed(screen.Root, "Tile:" + id), "LandMark"), Is.Null, "passed tile " + id);
                Assert.That(Fill(id), Is.EqualTo(Fill(20)), "passed tile " + id + " keeps its look");
            }
            Assert.That(FindNamed(FindNamed(screen.Root, "Tile:" + land), "LandMark").gameObject.activeSelf, Is.True);
        }

        // ---- 4. feedback on every landing ----

        [Test]
        public void EveryTileKindAndOutcomeLine_GivesAPopup()
        {
            var run = StartRun();
            foreach (var def in C.BoardTiles.Values)
            {
                var popups = RunPresentation.Landing(C, run, new RunTile { TileId = def.Id }, false, null);
                Assert.That(popups.Count, Is.GreaterThan(0), def.Id);
                Assert.That(popups.TrueForAll(p => !string.IsNullOrEmpty(p.Text)), Is.True, def.Id);
            }
            Assert.That(RunPresentation.Landing(C, run, new RunTile { TileId = "buff_atk" }, false, null)[0].Text, Is.EqualTo("+6% ATK"));
            Assert.That(RunPresentation.Landing(C, run, new RunTile { TileId = "loot" }, false, null).ConvertAll(p => p.Text), Is.EqualTo(new[] { "+25 coins", "+3 XP" }));
            Assert.That(RunPresentation.Landing(C, run, new RunTile { TileId = "enemy_1" }, true, null)[0].Kind, Is.EqualTo(PopupKind.Fight));

            foreach (OutcomeKind k in Enum.GetValues(typeof(OutcomeKind)))
            {
                if (k == OutcomeKind.Battle || k == OutcomeKind.Nothing) continue;
                var p = RunPresentation.FromLine(new OutcomeLine(k, k == OutcomeKind.Stat ? "Atk" : "fire_ember", 5, true), C);
                Assert.That(p.HasValue && !string.IsNullOrEmpty(p.Value.Text), Is.True, k.ToString());
            }
            // a tile's structured outcome (any non-sheet source) becomes popups instead of a result sheet
            var outcome = new RunOutcome { Source = "tile" };
            outcome.Lines.Add(new OutcomeLine(OutcomeKind.Coins, null, 12, true));
            outcome.Lines.Add(new OutcomeLine(OutcomeKind.Hp, null, 30, true));
            var fromRules = RunPresentation.Landing(C, run, new RunTile { TileId = "empty" }, false, outcome);
            Assert.That(fromRules.ConvertAll(p => p.Text), Is.EqualTo(new[] { "+12 coins", "+30 HP" }));
            Assert.That(fromRules[1].Kind, Is.EqualTo(PopupKind.Heal));
            Assert.That(RunPresentation.IsSheetSource("event") && RunPresentation.IsSheetSource("treasure") && !RunPresentation.IsSheetSource("shop"), Is.True);
        }

        [Test]
        public void EveryResolvingTileKind_PopsFromTheRulesOutcome()
        {
            // Empty, buffs, curses, supply, hazards, shards, pickups, boss tiles: the rules publish a tile outcome per landing
            foreach (var def in C.BoardTiles.Values)
            {
                if (Array.IndexOf(RunPresentation.FallbackKinds, def.Category) < 0 || def.Category == TileCategory.Start) continue;
                var tiles = new string[14];
                for (int i = 0; i < tiles.Length; i++) tiles[i] = def.Id;
                var run = StartRun(tiles);
                run.MinibossesSpawned = C.RunTuning.MinibossAtMeter.Count;
                Assert.That(_f.Service.ExecuteNow(new RunRoll()).Ok, Is.True, def.Id);
                var a = S.Run.Active;
                var land = a.Tiles[a.Position];
                var outcome = RunPresentation.TileOutcome(a, land);
                Assert.That(outcome, Is.Not.Null, def.Id + ": no tile outcome");
                var expected = new List<string>();
                foreach (var l in outcome.Lines) { var p = RunPresentation.FromLine(l, C); if (p.HasValue) expected.Add(p.Value.Text); }
                Assert.That(expected.Count, Is.GreaterThan(0), def.Id + ": outcome without a popup");
                Assert.That(RunPresentation.Landing(C, a, land, false, null).ConvertAll(p => p.Text), Is.EqualTo(expected), def.Id);
                Assert.That(_f.Service.ExecuteNow(new RunAbandon()).Ok, Is.True);
            }
        }

        [Test]
        public void LandingOnABuff_PopsTheTileAndShowsThePopup()
        {
            var run = StartRun("buff_atk", "buff_atk", "buff_atk", "buff_atk", "buff_atk", "buff_atk", "buff_atk", "buff_atk");
            var screen = Open();
            Assert.That(_f.Service.ExecuteNow(new RunRoll()).Ok, Is.True);
            var a = S.Run.Active;
            Assert.That(screen.LastLandedTile, Is.EqualTo(a.Position));
            Assert.That(screen.LastPopups, Does.Contain("+6% ATK"));
            Assert.That(screen.PopupsSpawned, Is.GreaterThan(0), "popups were drawn on the board");
        }

        // ---- 5. HUD ----

        [Test]
        public void Hud_ShowsHpAtkDefWithRunModifications_AndRunLevelXp()
        {
            var run = StartRun();
            var screen = Open();
            var hud = RunPresentation.Hud(C, run);
            Assert.That(hud.Atk, Is.GreaterThan(0));
            Assert.That(hud.MaxHp, Is.EqualTo(run.WormMaxHp));
            Assert.That(hud.XpNeed, Is.EqualTo(C.RunTuning.XpBase + C.RunTuning.XpPerLevel * (run.Level - 1)));
            string text = screen.HudText;
            Assert.That(text, Does.Contain("ATK " + Fmt.Num(Math.Round(hud.Atk))));
            Assert.That(text, Does.Contain("DEF " + Fmt.Num(Math.Round(hud.Def))));
            Assert.That(text, Does.Contain(Fmt.Num(Math.Round(hud.Hp)) + " / " + Fmt.Num(Math.Round(hud.MaxHp))));
            Assert.That(text, Does.Contain("Lv " + run.Level));
            Assert.That(text, Does.Contain("XP " + run.Xp + " / " + hud.XpNeed));
            Assert.That(FindNamed(screen.Root, "HpBar"), Is.Not.Null);
            Assert.That(FindNamed(screen.Root, "XpBar"), Is.Not.Null);

            // run modifications apply: +50% ATK and +50% DEF perks, some XP
            run.RunStats.Add(new StatMod(StatId.Atk, 0, 50));
            run.RunStats.Add(new StatMod(StatId.Def, 0, 50));
            run.Xp = 2;
            screen.Rebuild();
            var after = RunPresentation.Hud(C, run);
            Assert.That(after.Atk, Is.GreaterThan(hud.Atk * 1.3));
            Assert.That(after.Def, Is.GreaterThanOrEqualTo(hud.Def));
            Assert.That(screen.HudText, Does.Contain("ATK " + Fmt.Num(Math.Round(after.Atk))));
            Assert.That(screen.HudText, Does.Contain("XP 2 / " + after.XpNeed));
            Assert.That(after.Atk, Is.EqualTo(BattleSimulator.FinalStats(RunSystem.WormSpec(C, run), C).Value(StatId.Atk)), "the numbers fights use");
        }

        // ---- 6. rarity colours ----

        [Test]
        public void Skills_AreColouredByRarity_Everywhere()
        {
            Assert.That(Theme.RarityColor(Rarity.Common), Is.EqualTo(Theme.Hex(0xB8AEA6)), "grey");
            Assert.That(Theme.RarityColor(Rarity.Rare).b, Is.GreaterThan(Theme.RarityColor(Rarity.Rare).r), "blue");
            var epic = Theme.RarityColor(Rarity.Epic);
            Assert.That(epic.r > epic.g && epic.b > epic.g, Is.True, "purple");
            var leg = Theme.RarityColor(Rarity.Legendary);
            Assert.That(leg.r > leg.g && leg.g > leg.b, Is.True, "orange");
            var myth = Theme.RarityColor(Rarity.Mythic);
            Assert.That(myth.r > 0.9f && myth.g < 0.4f && myth.b < 0.4f, Is.True, "red");

            // pick two skills of different rarity but (ideally) the same family: colour follows rarity, not family
            SkillDef common = null, epicSkill = null;
            foreach (var s in C.Skills.Values)
            {
                if (s.Rarity == Rarity.Common && common == null) common = s;
                if (s.Rarity == Rarity.Epic && epicSkill == null) epicSkill = s;
            }
            Assert.That(common, Is.Not.Null); Assert.That(epicSkill, Is.Not.Null);
            Assert.That(RunPresentation.SkillColor(common), Is.EqualTo(Theme.RarityColor(Rarity.Common)));
            Assert.That(RunPresentation.SkillColor(epicSkill), Is.EqualTo(Theme.RarityColor(Rarity.Epic)));

            var run = StartRun();
            run.Skills.Add(epicSkill.Id);
            run.Phase = RunPhase.Draft;
            run.DraftOffer.Clear(); run.DraftOffer.Add(common.Id); run.DraftOffer.Add(epicSkill.Id);
            var screen = Open();
            // skill slot
            var slot = FindNamed(screen.Root, "Slot:" + epicSkill.Id);
            Assert.That(slot, Is.Not.Null);
            Assert.That(FindNamed(slot, "RarityBorder").GetComponent<Image>().color, Is.EqualTo(Theme.RarityColor(Rarity.Epic)));
            // draft cards: the icon is the rarity colour
            var card = FindNamed(_f.App.ModalLayer, "Draft:" + common.Id);
            Assert.That(card, Is.Not.Null, "draft open");
            Assert.That(FindNamed(card, "Icon").GetComponent<Image>().color, Is.EqualTo(Theme.RarityColor(Rarity.Common)));
            Assert.That(FindNamed(FindNamed(_f.App.ModalLayer, "Draft:" + epicSkill.Id), "Icon").GetComponent<Image>().color, Is.EqualTo(Theme.RarityColor(Rarity.Epic)));

            // result screen: gained skills by rarity
            Assert.That(RunPresentation.SkillColor(C, epicSkill.Id), Is.EqualTo(Theme.RarityColor(Rarity.Epic)));
        }

        // ---- 7. shop ----

        [Test]
        public void LeavingAShop_ShowsNoResultSheet()
        {
            var run = StartRun("shop");
            run.Coins = 1000;
            var screen = Open();
            Assert.That(_f.Service.ExecuteNow(new RunRoll()).Ok, Is.True);
            CloseBattles();
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Shop));
            int offer = S.Run.Active.Shop.FindIndex(o => o.Price <= S.Run.Active.Coins);
            Assert.That(offer, Is.GreaterThanOrEqualTo(0));
            Assert.That(_f.Service.ExecuteNow(new RunShopBuy { Index = offer }).Ok, Is.True);
            Assert.That(_f.Service.ExecuteNow(new RunShopLeave()).Ok, Is.True);
            screen.Rebuild();
            Assert.That(S.Run.Active.LastOutcome.Source, Is.EqualTo("shop"));
            Assert.That(screen.OutcomeOpen, Is.False, "no outcome summary after a shop visit");
        }
    }
}
