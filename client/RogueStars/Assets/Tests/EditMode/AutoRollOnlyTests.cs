using System.Collections.Generic;
using NUnit.Framework;
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
    /// Owner playtest 0.1.200: auto mode ONLY rolls the dice. Every decision and reveal screen (event, vendor, chest,
    /// campfire and its Train picker, level-up draft, Mythic choice, signature offer, revive, encounter, result sheet,
    /// battle playback) stays open with auto on until the player taps; auto rolls again once it is closed.
    /// </summary>
    public sealed class AutoRollOnlyTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; }
        [TearDown] public void TearDown() { RunScreen.InstantOutcomes = false; RunScreen.ForceChestIntro = false; _f.Dispose(); }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private Button ModalButton(string name)
        {
            foreach (var b in _f.App.ModalLayer.GetComponentsInChildren<Button>(true)) if (b.name == name) return b;
            return null;
        }

        private ActiveRun StartRun(string tile = "empty")
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            var run = S.Run.Active;
            run.Tiles.Clear();
            for (int i = 0; i <= 30; i++) { var t = new RunTile { Id = i, TileId = i == 0 ? "start" : tile, X = i }; if (i < 30) t.Next.Add(i + 1); run.Tiles.Add(t); }
            run.BoardEnd = 30; run.BossGateTile = -1; run.Position = 0; run.Trail.Clear(); run.Trail.Add(0);
            return run;
        }

        private RunScreen OpenWithAuto()
        {
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            screen.StartAuto();
            return screen;
        }

        /// <summary>Many auto frames: nothing is sent, the screen part named <paramref name="marker"/> stays open, auto stays on and paused.</summary>
        private void AssertStaysOpen(RunScreen screen, string marker, string what)
        {
            long version = S.Version;
            for (int i = 0; i < 30; i++) { screen.Frame(1f); _f.App.Update(1f); }
            Assert.That(S.Version, Is.EqualTo(version), what + ": auto must not answer it");
            Assert.That(FindNamed(_f.App.ModalLayer, marker), Is.Not.Null, what + " stays open (" + marker + ")");
            Assert.That(screen.Auto, Is.True, what + ": auto stays on");
            Assert.That(screen.AutoPaused, Is.True, what + ": auto is paused");
        }

        [Test]
        public void EventScene_Vendor_And_Chest_StayOpen([Values("abandoned_cache", "smuggler", "treasure_pod")] string eventId)
        {
            var run = StartRun();
            run.Phase = RunPhase.Event; run.EventId = eventId; run.EventTile = 1;
            var screen = OpenWithAuto();
            string option = "Button:" + C.RunEvents[eventId].Options[0].Text;
            Transform found = null;
            foreach (var b in _f.App.ModalLayer.GetComponentsInChildren<Button>(true)) if (b.name.StartsWith(option)) found = b.transform;
            Assert.That(found, Is.Not.Null, "event options shown");
            AssertStaysOpen(screen, found.name, eventId);
        }

        [Test]
        public void Campfire_And_ItsTrainPicker_StayOpen()
        {
            var run = StartRun();
            run.Skills.Add("crit_keen");
            run.Phase = RunPhase.Event; run.EventId = "campfire"; run.EventTile = 1;
            var screen = OpenWithAuto();
            AssertStaysOpen(screen, "Option:Train", "campfire");
            ModalButton("Option:Train").onClick.Invoke();
            AssertStaysOpen(screen, "TrainTitle", "Train picker");
        }

        [Test]
        public void LevelUpDraft_StaysOpen()
        {
            var run = StartRun();
            run.Phase = RunPhase.Draft; run.MythicDraft = false;
            run.DraftOffer.AddRange(new[] { "crit_keen", "gen_power", "gen_guard" });
            var screen = OpenWithAuto();
            AssertStaysOpen(screen, "Button:Pick", "level-up draft");
        }

        [Test]
        public void MythicChest_WaitsForTheTap_ThenTheChoiceStaysOpen()
        {
            RunScreen.ForceChestIntro = true;
            var run = StartRun();
            run.Phase = RunPhase.Draft; run.MythicDraft = true;
            run.DraftOffer.AddRange(new[] { "mythic_last_light", "mythic_catalyst", "mythic_overkill" });
            var screen = OpenWithAuto();
            AssertStaysOpen(screen, "ChestTap", "Mythic chest");
            Assert.That(FindNamed(_f.App.ModalLayer, "MythicTitle"), Is.Null, "the chest is not opened for the player");
            ModalButton("ChestTap").onClick.Invoke();
            AssertStaysOpen(screen, "MythicTitle", "Mythic choice");
        }

        [Test]
        public void SignatureChest_And_Offer_StayOpen()
        {
            RunScreen.ForceChestIntro = true;
            var run = StartRun();
            var sigs = new List<string>();
            foreach (var sk in C.Skills.Values) if (sk.Signature && !run.Skills.Contains(sk.Id) && sigs.Count < 2) sigs.Add(sk.Id);
            run.SignatureChoice.AddRange(sigs);
            run.Phase = RunPhase.Event; run.EventId = "resonance"; run.EventTile = 1;
            var screen = OpenWithAuto();
            AssertStaysOpen(screen, "ChestTap", "Resonance chest");
            ModalButton("ChestTap").onClick.Invoke();
            AssertStaysOpen(screen, "SignatureTitle", "signature offer");
        }

        [Test]
        public void Revive_StaysOpen()
        {
            var run = StartRun();
            run.Phase = RunPhase.Defeated;
            var screen = OpenWithAuto();
            AssertStaysOpen(screen, "Button:Give up", "revive");
        }

        [Test]
        public void Encounter_WaitsForTheFightTap()
        {
            var run = StartRun();
            run.MinibossesSpawned = 0;
            run.Awakening = 24.9; // the next roll crosses 25: a miniboss attacks after the move
            var screen = OpenWithAuto();
            screen.Frame(1f); // auto rolls
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Encounter));
            long version = S.Version;
            for (int i = 0; i < 30; i++) screen.Frame(1f);
            Assert.That(S.Version, Is.EqualTo(version), "auto never starts a fight");
            Assert.That(screen.AutoPaused, Is.True);
            screen.Rebuild();
            string caption = FindNamed(screen.Root, "RollButton").GetComponentInChildren<Text>().text;
            Assert.That(caption, Is.EqualTo("Fight!"), "the dice say what the player's tap does");
        }

        [Test]
        public void BattlePlayback_WaitsForContinue_ThenAutoRollsOn()
        {
            var run = StartRun("enemy_1");
            run.WormHp = 1e12; run.WormMaxHp = 1e12;
            var screen = OpenWithAuto();
            screen.Frame(1f); // auto rolls onto a fight
            Assert.That(_f.App.Router.Current, Is.InstanceOf<BattlePlaybackScreen>(), "the fight plays");
            var playback = (BattlePlaybackScreen)_f.App.Router.Current;
            playback.PlayAll();
            Assert.That(playback.Finished, Is.True);
            long rolls = S.Counter("run_rolls");
            for (int i = 0; i < 30; i++) { _f.App.Update(1f); screen.Frame(1f); }
            Assert.That(_f.App.Router.Current, Is.SameAs(playback), "the finished fight waits for Continue");
            Assert.That(S.Counter("run_rolls"), Is.EqualTo(rolls), "no roll under the playback");
            Assert.That(FindNamed(_f.App.ModalLayer, "Button:"), Is.Null, "no tap-to-stop overlay over the playback");
            playback.Continue();
            Assert.That(_f.App.Router.Current, Is.SameAs(screen));
            Assert.That(screen.Auto, Is.True);
            // the fight's XP may have opened a level-up draft: the player picks (auto never does)
            for (int i = 0; i < 5 && S.Run.Active.Phase == RunPhase.Draft; i++)
            {
                screen.Frame(1f);
                Assert.That(S.Counter("run_rolls"), Is.EqualTo(rolls), "no roll while the draft is open");
                ModalButton("Button:Pick").onClick.Invoke();
            }
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.AwaitRoll));
            screen.Frame(1f);
            Assert.That(S.Counter("run_rolls"), Is.EqualTo(rolls + 1), "auto rolls again after the player closed it");
        }

        [Test]
        public void RunAndHomeScreens_HaveNoAutoPick()
        {
            StartRun();
            var screen = OpenWithAuto();
            Assert.That(FindNamed(screen.Root, "AutoPickToggle"), Is.Null);
            _f.App.Router.Pop();
            Assert.That(FindNamed(_f.App.Router.Current.Root, "AutoPickChip"), Is.Null);
        }
    }
}
