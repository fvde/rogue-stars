using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Signature skills (owner design, signatures pass). Every run starts with one signature, picked from
    /// RunTuning.SignatureChoices random signatures of the synergies the player has unlocked (starters + Laboratory packages;
    /// with exactly three unlocked all three are offered). The offer is a pure function of the player id, the run counter and
    /// the unlocked synergies, so the client shows exactly what the server validates; RunStart.SignatureId picks one, an empty
    /// id (auto mode, bots, old clients) takes the first. The signature awakens as the build commits: level 2 at 3 owned skills of
    /// its synergy, level 3 at 6 (RunTuning.SignatureAwakenAt; the signature itself not counted). Its one effect only grows
    /// (SkillLeveling: stacks / jumps / aliens ×level, chances ×1, ×1.5, ×2).
    /// </summary>
    public static partial class RunSystem
    {
        /// <summary>The signatures offered at the start of the player's next run (ids, display order).</summary>
        public static List<string> SignatureOffer(GameState state, ContentDb content, CombatLoadout loadout)
        {
            var families = loadout != null && loadout.UnlockedFamilies.Count > 0
                ? loadout.UnlockedFamilies
                : LaboratorySystem.UnlockedFamilies(state, content);
            var pool = new List<string>();
            foreach (var id in RunDraft.SortedSkillIds(content))
            {
                var s = content.Skills[id];
                if (s.Signature && families.Contains(s.Family)) pool.Add(id);
            }
            var rng = new Rng(0x5167UL).Fork("signature:" + state.PlayerId + ":" + state.Run.RunsStarted);
            rng.Shuffle(pool);
            if (pool.Count > content.RunTuning.SignatureChoices) pool.RemoveRange(content.RunTuning.SignatureChoices, pool.Count - content.RunTuning.SignatureChoices);
            return pool;
        }

        /// <summary>The next run's signature offer for the client (builds the loadout like RunStart).</summary>
        public static List<string> SignatureOffer(GameEngine engine, GameState state, long nowMs) =>
            SignatureOffer(state, engine.Content, engine.BuildLoadout(state, nowMs));

        /// <summary>Awakening level of a signature with <paramref name="familyCount"/> owned skills of its synergy (1..3).</summary>
        public static int SignatureLevel(ContentDb content, int familyCount)
        {
            int lv = 1;
            foreach (var at in content.RunTuning.SignatureAwakenAt) if (familyCount >= at) lv++;
            return lv;
        }

        /// <summary>Owned skills of the signature's synergy needed for its next awakening (0 when fully awake or without a signature).</summary>
        public static int NextAwakeningAt(ContentDb content, ActiveRun run)
        {
            var s = run?.SignatureId != null ? content.Skill(run.SignatureId) : null;
            if (s == null) return 0;
            int n = RunDraft.FamilyCount(content, run, s.Family);
            foreach (var at in content.RunTuning.SignatureAwakenAt) if (n < at) return at;
            return 0;
        }

        private static void GrantSignature(CommandContext ctx, ActiveRun run, string requested, List<string> offer)
        {
            if (offer.Count == 0) return;
            string pick = string.IsNullOrEmpty(requested) ? offer[0] : requested;
            Require(offer.Contains(pick), "signature not offered");
            run.SignatureId = pick;
            run.Skills.Add(pick);
            run.Gains.Add(new RunGain("signature", pick, 1));
            ctx.State.Count("run_signature_" + pick);
            ctx.Emit("run_signature", pick);
            UpdateSignature(ctx, run);
        }

        /// <summary>Re-reads the signature's awakening level after the build changed (a skill gained or lost).</summary>
        private static void UpdateSignature(CommandContext ctx, ActiveRun run)
        {
            var c = ctx.Content;
            var s = run.SignatureId != null ? c.Skill(run.SignatureId) : null;
            if (s == null || !run.Skills.Contains(s.Id)) return;
            int lv = SignatureLevel(c, RunDraft.FamilyCount(c, run, s.Family));
            int was = RunDraft.Level(run, s.Id);
            if (lv == was) return;
            if (lv > 1) run.SkillLevels[s.Id] = lv; else run.SkillLevels.Remove(s.Id);
            if (lv > was) { ctx.State.Count("run_signature_awakened"); ctx.Emit("run_signature_awakened", s.Id, lv.ToString()); }
        }
    }
}
