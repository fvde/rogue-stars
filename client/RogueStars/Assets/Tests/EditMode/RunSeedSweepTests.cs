using System;
using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json;
using RogueStars.Client.Services;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Headless guard for the PlayMode playthrough: fresh offline saves for many seeds play the first level the way the
    /// playthrough does (one manual roll, then RunAutoStep) and every run must end within a decision budget, with every
    /// auto decision accepted. A stalled seed fails with its seed, the last decision and the run state.
    /// Also pins the determinism the playthrough relies on (same service seed + clock → same game).
    /// </summary>
    public sealed class RunSeedSweepTests
    {
        private const int Seeds = 100;
        private const int MaxDecisions = 200;
        private const int MaxAttempts = 3;

        private string _dir;

        [SetUp] public void SetUp() => _dir = TestUtil.TempDir();
        [TearDown] public void TearDown() => TestUtil.DeleteDir(_dir);

        private LocalGameService Fresh(int seed, string sub)
        {
            var dir = System.IO.Path.Combine(_dir, sub);
            var s = new LocalGameService(dir, "local_sweep", "Sweep", new ManualClock(TestUtil.T0), seed);
            ServiceResult res = null;
            s.Connect(r => res = r);
            Assert.That(res != null && res.Ok, Is.True, "connect seed " + seed + ": " + res?.Error);
            return s;
        }

        private static LevelDef FirstLevel(ContentDb content)
        {
            return content.MainLevel(1);
        }

        private static void EquipStarterParts(LocalGameService s)
        {
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
                foreach (var uid in PartsSystem.SortedPartUids(s.State))
                {
                    if (PartsSystem.IsEquipped(s.State, uid)) continue;
                    if (s.ExecuteNow(new PartEquip { Uid = uid, Slot = slot }).Ok) break;
                }
        }

        private static string Describe(ActiveRun a) => a == null ? "no active run" :
            "phase=" + a.Phase + " rolls=" + a.Rolls + "/" + a.MaxRolls + " pos=" + a.Position + " hp=" + a.WormHp + "/" + a.WormMaxHp +
            " battles=" + a.BattleCount + " event=" + a.EventId + " draft=[" + string.Join(",", a.DraftOffer) + "] coins=" + a.Coins +
            " awakening=" + a.Awakening + " branch=[" + string.Join(",", a.BranchOptions) + "]";

        /// <summary>Plays one run like the playthrough; returns the number of decisions, fails with diagnostics on a stall.</summary>
        private static int PlayRun(LocalGameService s, LevelDef level, int seed, int attempt, out bool firstRollBattle)
        {
            var start = s.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, "seed " + seed + " attempt " + attempt + ": RunStart " + start.Error);
            int battlesBefore = s.State.Run.Active.BattleCount;
            var roll = s.ExecuteNow(new RunRoll());
            Assert.That(roll.Ok, Is.True, "seed " + seed + ": manual RunRoll " + roll.Error);
            firstRollBattle = s.State.Run.Active == null || s.State.Run.Active.BattleCount > battlesBefore;
            int decisions = 1;
            string last = "RunRoll";
            while (s.State.Run.Active != null)
            {
                string before = Describe(s.State.Run.Active);
                if (decisions >= MaxDecisions)
                    Assert.Fail("seed " + seed + " attempt " + attempt + ": run did not finish within " + MaxDecisions + " decisions; last=" + last + " state: " + before);
                long version = s.State.Version;
                var r = s.ExecuteNow(new RunAutoStep());
                Assert.That(r.Ok, Is.True, "seed " + seed + " attempt " + attempt + ": RunAutoStep refused (" + r.Error + ") after " + last + " in state: " + before);
                Assert.That(s.State.Version, Is.GreaterThan(version), "seed " + seed + ": RunAutoStep did not change the state: " + before);
                last = "RunAutoStep@" + before;
                decisions++;
            }
            Assert.That(s.State.Run.LastSummary, Is.Not.Null, "seed " + seed + ": finished run has a summary");
            return decisions;
        }

        [Test]
        public void FirstLevel_AutoRun_FinishesForManySeeds([Values(false, true)] bool equipped)
        {
            int maxDecisions = 0, firstRollBattles = 0, wins = 0;
            var battleSeeds = new List<int>();
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var s = Fresh(seed, (equipped ? "e" : "u") + seed);
                if (equipped) EquipStarterParts(s);
                var level = FirstLevel(s.Content);
                for (int attempt = 0; attempt < MaxAttempts; attempt++)
                {
                    maxDecisions = Math.Max(maxDecisions, PlayRun(s, level, seed, attempt, out bool battle));
                    if (attempt == 0 && battle) { firstRollBattles++; battleSeeds.Add(seed); }
                    if (s.State.Run.LastSummary.Won) { wins++; break; }
                }
            }
            Debug.Log("[RunSeedSweep] equipped=" + equipped + " seeds=" + Seeds + " wins=" + wins + " maxDecisions=" + maxDecisions +
                      " firstRollBattles=" + firstRollBattles + " (seeds " + string.Join(",", battleSeeds) + ")");
        }

        [Test]
        public void SameSeedAndClock_PlayTheSameRun()
        {
            string Play(string sub)
            {
                var s = Fresh(11, sub);
                EquipStarterParts(s);
                PlayRun(s, FirstLevel(s.Content), 11, 0, out _);
                return JsonConvert.SerializeObject(s.State.Run.LastSummary);
            }
            Assert.That(Play("a"), Is.EqualTo(Play("b")));
        }

        [Test]
        public void DifferentSeeds_CreateDifferentPlayers()
        {
            var a = Fresh(1, "a");
            var b = Fresh(2, "b");
            Assert.That(JsonConvert.SerializeObject(a.State.Rng), Is.Not.EqualTo(JsonConvert.SerializeObject(b.State.Rng)));
        }
    }
}
