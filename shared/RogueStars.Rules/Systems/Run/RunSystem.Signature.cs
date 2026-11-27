using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Signature skills (owner design, signatures pass). Every run starts with one signature, picked from
    /// RunTuning.SignatureChoices distinct random signatures of the synergies the player has unlocked (starters + Laboratory
    /// packages; with exactly three unlocked all three are offered), from the signature unlock on (the second unlock, main level 3:
    /// LevelDef.SignatureChoice; the first levels' runs have none). The offer is a pure function of the player id, the run counter and
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
            var rng = new Rng(0x5167UL ^ state.Run.SignatureSeed).Fork("signature:" + state.Run.RunsStarted);
            rng.Shuffle(pool);
            // Every run starts with the pick (playtest 0.1.325: from the first run on, never a random signature).
            int choices = content.RunTuning.SignatureChoices;
            if (pool.Count > choices) pool.RemoveRange(choices, pool.Count - choices);
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

        /// <summary>
        /// The run's signatures in the order found (skill redesign: several per run, at most RunTuning.MaxSignatures). Runs saved
        /// before keep their single SignatureId.
        /// </summary>
        public static List<string> Signatures(ActiveRun run)
        {
            var list = new List<string>();
            if (run == null) return list;
            if (run.SignatureIds != null && run.SignatureIds.Count > 0) list.AddRange(run.SignatureIds);
            else if (!string.IsNullOrEmpty(run.SignatureId)) list.Add(run.SignatureId);
            list.RemoveAll(id => !run.Skills.Contains(id));
            return list;
        }

        /// <summary>Owned skills of the run's first signature's synergy needed for its next awakening (0 when fully awake or none).</summary>
        public static int NextAwakeningAt(ContentDb content, ActiveRun run)
        {
            var sigs = Signatures(run);
            return sigs.Count == 0 ? 0 : NextAwakeningAt(content, run, sigs[0]);
        }

        /// <summary>Owned skills of this signature's synergy needed for its next awakening (0 when fully awake).</summary>
        public static int NextAwakeningAt(ContentDb content, ActiveRun run, string signatureId)
        {
            var s = signatureId != null ? content.Skill(signatureId) : null;
            if (s == null) return 0;
            int n = RunDraft.FamilyCount(content, run, s.Family);
            foreach (var at in content.RunTuning.SignatureAwakenAt) if (n < at) return at;
            return 0;
        }

        /// <summary>
        /// Unowned signatures the run could still find (unlocked synergies, below the cap), each with its offer weight: synergies the
        /// run already owns skills in are preferred (1 + SynergyWeightPerOwned × owned skills).
        /// </summary>
        public static List<string> FindableSignatures(ContentDb content, ActiveRun run, List<double> weights = null)
        {
            var list = new List<string>();
            if (Signatures(run).Count >= content.RunTuning.MaxSignatures) return list;
            foreach (var id in RunDraft.SortedSkillIds(content))
            {
                var s = content.Skills[id];
                if (!s.Signature || run.Skills.Contains(id) || !RunDraft.FamilyUnlocked(run, s.Family)) continue;
                list.Add(id);
                if (weights != null) weights.Add(1 + content.RunTuning.SynergyWeightPerOwned * RunDraft.FamilyCount(content, run, s.Family));
            }
            return list;
        }

        /// <summary>Up to <paramref name="n"/> findable signatures, drawn by weight with the run's stream.</summary>
        private static List<string> DrawSignatures(ContentDb content, ActiveRun run, int n)
        {
            var w = new List<double>();
            var pool = FindableSignatures(content, run, w);
            var res = new List<string>();
            while (res.Count < n && pool.Count > 0)
            {
                int i = run.Rng.PickWeighted(w);
                if (i < 0) break;
                res.Add(pool[i]);
                pool.RemoveAt(i); w.RemoveAt(i);
            }
            return res;
        }

        /// <summary>
        /// A miniboss's Mythic choice: with RunTuning.SignatureMythicChancePct one Mythic is replaced by an unowned signature (the
        /// last card), while the run holds fewer than MaxSignatures.
        /// </summary>
        private static void MaybeOfferSignature(CommandContext ctx, ActiveRun run, List<string> offer)
        {
            var c = ctx.Content;
            if (FindableSignatures(c, run).Count == 0) return;
            if (!run.Rng.Chance(c.RunTuning.SignatureMythicChancePct / 100.0)) return;
            var sig = DrawSignatures(c, run, 1);
            if (sig.Count == 0) return;
            if (offer.Count >= c.RunTuning.MythicChoices && offer.Count > 0) offer[offer.Count - 1] = sig[0];
            else offer.Add(sig[0]);
        }

        /// <summary>Adds a found signature (miniboss choice, Resonance event): it awakens at once if the run already owns enough of its synergy.</summary>
        private static void AddSignature(CommandContext ctx, ActiveRun run, string id, string source)
        {
            var c = ctx.Content;
            var s = c.Skill(id);
            Require(s != null && s.Signature, "not a signature");
            Require(!run.Skills.Contains(id), "signature already owned");
            Require(Signatures(run).Count < c.RunTuning.MaxSignatures, "signature cap reached");
            if (run.SignatureIds.Count == 0 && !string.IsNullOrEmpty(run.SignatureId) && run.Skills.Contains(run.SignatureId)) run.SignatureIds.Add(run.SignatureId);
            if (string.IsNullOrEmpty(run.SignatureId)) run.SignatureId = id;
            run.SignatureIds.Add(id);
            run.Skills.Add(id);
            run.Gains.Add(new RunGain(source, id, 1));
            ctx.State.Count("run_signature_" + id);
            ctx.State.Count("run_signatures_found");
            ctx.Emit("run_signature", id);
            UpdateSignature(ctx, run);
        }

        private static void GrantSignature(CommandContext ctx, ActiveRun run, string requested, List<string> offer)
        {
            if (offer.Count == 0) return;
            string pick = string.IsNullOrEmpty(requested) ? offer[0] : requested;
            Require(offer.Contains(pick), "signature not offered");
            run.SignatureId = pick;
            run.SignatureIds.Clear();
            run.SignatureIds.Add(pick);
            run.Skills.Add(pick);
            run.Gains.Add(new RunGain("signature", pick, 1));
            ctx.State.Count("run_signature_" + pick);
            ctx.Emit("run_signature", pick);
            UpdateSignature(ctx, run);
        }

        /// <summary>
        /// Phase Signature (run UI glitches #24): the run-start pick on the board. Grants one of ActiveRun.StartSignatures (empty =
        /// the first, like RunStart), then the run waits for its first roll.
        /// </summary>
        private static void PickStartSignature(CommandContext ctx, ActiveRun run, string id)
        {
            Require(run.Phase == RunPhase.Signature && run.StartSignatures.Count > 0, "no signature to pick");
            GrantSignature(ctx, run, id, run.StartSignatures);
            run.StartSignatures.Clear();
            RefreshMaxHp(ctx.Content, run);
            run.Phase = RunPhase.AwaitRoll;
            Continue(ctx, run);
        }

        /// <summary>Re-reads every signature's awakening level (each counts its own synergy) after the build changed.</summary>
        private static void UpdateSignature(CommandContext ctx, ActiveRun run)
        {
            var c = ctx.Content;
            foreach (var id in Signatures(run))
            {
                var s = c.Skill(id);
                if (s == null) continue;
                int lv = SignatureLevel(c, RunDraft.FamilyCount(c, run, s.Family));
                int was = RunDraft.Level(run, s.Id);
                if (lv == was) continue;
                if (lv > 1) run.SkillLevels[s.Id] = lv; else run.SkillLevels.Remove(s.Id);
                if (lv > was) { ctx.State.Count("run_signature_awakened"); ctx.Emit("run_signature_awakened", s.Id, lv.ToString()); }
            }
        }
    }
}
