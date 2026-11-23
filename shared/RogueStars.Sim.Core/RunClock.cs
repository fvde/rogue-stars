using System;

namespace RogueStars.Sim
{
    /// <summary>How the player drives a run (wall-clock model, <see cref="RunClock"/>).</summary>
    public enum RunPlayStyle
    {
        /// <summary>Taps ROLL every time, reads and decides every popup, taps Continue after each fight.</summary>
        Manual,
        /// <summary>Auto mode on: rolls, moves and fights run by themselves; auto pauses at every real decision (the player decides).</summary>
        Auto,
        /// <summary>Auto mode with "Auto-pick" (farming): decisions take their default after a short look.</summary>
        Farming,
    }

    /// <summary>What happened in one run, counted for the wall-clock model (filled by SimPlayer.PlayRun from run events).</summary>
    public sealed class RunTimeTally
    {
        public int Rolls, Steps, Battles, BattleLogEntries, Drafts, MythicDrafts, Events, ChanceOutcomes, Outcomes;
        public int ShopVisits, ShopBuys, ShopRerolls, Branches, Revives, BurrowChoices;

        public void Add(RunTimeTally o)
        {
            Rolls += o.Rolls; Steps += o.Steps; Battles += o.Battles; BattleLogEntries += o.BattleLogEntries; Drafts += o.Drafts;
            MythicDrafts += o.MythicDrafts; Events += o.Events; ChanceOutcomes += o.ChanceOutcomes; Outcomes += o.Outcomes;
            ShopVisits += o.ShopVisits; ShopBuys += o.ShopBuys; ShopRerolls += o.ShopRerolls; Branches += o.Branches;
            Revives += o.Revives; BurrowChoices += o.BurrowChoices;
        }
    }

    /// <summary>
    /// Wall-clock model of a run (docs/balance/progression-report.md "Run time model"). Animation times are the client's
    /// (RunScreen: dice 0.45 s, 0.16 s per hop, auto steps 0.6 s apart, chance roll 1.3 s, auto closes a result after 1.4 s;
    /// BattlePlaybackScreen: 0.55 s per log entry ÷ speed, auto closes 0.8 s after the end); reading / deciding times are
    /// estimates for a player who knows the game. The battle speed is a player choice: ×1 is the default, ×2 free, ×3 pass.
    /// </summary>
    public static class RunClock
    {
        // client animation (seconds)
        public const double DiceSec = 0.45, HopSec = 0.16, AutoStepGapSec = 0.6, BattleEntrySec = 0.55, BattleOpenSec = 0.4;
        public const double BattleAutoCloseSec = 0.8, ChanceSpinSec = 1.3, AutoOutcomeSec = 1.4;
        // player (seconds)
        public const double RunIntroSec = 5, ResultScreenSec = 10, TapRollSec = 0.9, BattleContinueSec = 1.5;
        public const double DraftSec = 6, MythicDraftSec = 8, EventSec = 6, OutcomeReadSec = 2.5, ShopBrowseSec = 5, ShopBuySec = 2, ShopRerollSec = 1.5;
        public const double BranchSec = 2.5, ReviveSec = 4, BurrowChoiceSec = 4;

        /// <summary>Seconds a run takes at the given style and battle speed (1, 2 or 3).</summary>
        public static double Seconds(RunTimeTally t, RunPlayStyle style, int battleSpeed)
        {
            bool manual = style == RunPlayStyle.Manual, farming = style == RunPlayStyle.Farming;
            double sec = RunIntroSec + ResultScreenSec;
            // rolls and movement
            sec += t.Rolls * (DiceSec + (manual ? TapRollSec : AutoStepGapSec)) + t.Steps * HopSec;
            // battles: playback of the log at the chosen speed
            sec += t.Battles * (BattleOpenSec + (manual ? BattleContinueSec : BattleAutoCloseSec))
                   + t.BattleLogEntries * BattleEntrySec / Math.Max(1, battleSpeed);
            // decisions: auto pauses and the player decides; farming takes the default after a short look
            double d(double playerSec) => farming ? AutoStepGapSec : playerSec;
            sec += t.Drafts * d(DraftSec) + t.MythicDrafts * d(MythicDraftSec) + t.Events * d(EventSec) + t.Branches * d(BranchSec);
            sec += t.Revives * ReviveSec + t.BurrowChoices * d(BurrowChoiceSec);
            // shops: browse, each purchase, rerolls (farming buys step by step)
            sec += t.ShopVisits * d(ShopBrowseSec) + t.ShopBuys * d(ShopBuySec) + t.ShopRerolls * d(ShopRerollSec);
            // outcome result screens (events, chests, campfires, shop visits with a purchase): chance roll, then the result
            sec += t.ChanceOutcomes * ChanceSpinSec + t.Outcomes * (manual ? OutcomeReadSec : AutoOutcomeSec);
            return sec;
        }

        public static double Minutes(RunTimeTally t, RunPlayStyle style, int battleSpeed) => Seconds(t, style, battleSpeed) / 60.0;
    }
}
