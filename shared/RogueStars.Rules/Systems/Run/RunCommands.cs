using System;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Start a run on a portal level. Costs the level's entry cost (Stamina, or Burrow Keys for the Burrow).</summary>
    /// <summary>SignatureId: one of RunSystem.SignatureOffer (empty = the first offered: auto mode, bots).</summary>
    [Serializable] public sealed class RunStart : GameCommand { public string PortalId; public string LevelId; public RunMode Mode; public string SignatureId; }

    /// <summary>Roll the dice (count from level + boons) and move. Clean random.</summary>
    [Serializable] public sealed class RunRoll : GameCommand { }

    /// <summary>Pick a leg at a branch: Index into ActiveRun.BranchOptions.</summary>
    [Serializable] public sealed class RunChooseBranch : GameCommand { public int Index; }

    /// <summary>Choose an option of the current event (also campfire / treasure).</summary>
    [Serializable] public sealed class RunChooseEventOption : GameCommand { public int Index; }

    /// <summary>Pick one skill of the current draft offer.</summary>
    [Serializable] public sealed class RunDraftPick : GameCommand { public string SkillId; }

    /// <summary>Reroll the draft offer (limited rerolls per run).</summary>
    [Serializable] public sealed class RunDraftReroll : GameCommand { }

    /// <summary>Buy shop offer Index with run coins.</summary>
    [Serializable] public sealed class RunShopBuy : GameCommand { public int Index; }

    /// <summary>Reroll the shop (rising price).</summary>
    [Serializable] public sealed class RunShopReroll : GameCommand { }

    [Serializable] public sealed class RunShopLeave : GameCommand { }

    /// <summary>
    /// Phase Encounter: face the miniboss that attacks, or walk into the boss arena that opened ahead and fight the boss.
    /// A clear step (auto mode performs it; RunAutoStep does the same).
    /// </summary>
    [Serializable] public sealed class RunEncounter : GameCommand { }

    /// <summary>Revive once per run after a lost battle (ad verified by the host, or Diamonds).</summary>
    /// <summary>Revive once per run. Ad: consumes Meta's "revive" ad placement (AdToken; instant with No Ads).</summary>
    [Serializable] public sealed class RunRevive : GameCommand { public ReviveMethod Method; public string AdToken; }

    /// <summary>
    /// Run result screen: watch a rewarded ad to receive the last won run's rewards a second time (feat Diamonds
    /// excluded). Once per run; consumes Meta's "double_run_rewards" placement (daily cap; instant with No Ads).
    /// </summary>
    [Serializable] public sealed class RunDoubleRewards : GameCommand { public string AdToken; }

    /// <summary>Give up: the run fails and all pending loot is lost.</summary>
    [Serializable] public sealed class RunAbandon : GameCommand { }

    /// <summary>
    /// Auto mode: perform the next clear, choice-free step (roll and move; tiles, pickups and battles resolve on the
    /// way). Refused ("needs a player decision") at a real decision (RunSystem.RequiresPlayer: branch, event /
    /// treasure / campfire, draft, shop, revive, Burrow choice) unless <see cref="Farming"/> is set.
    /// </summary>
    [Serializable]
    public sealed class RunAutoStep : GameCommand
    {
        /// <summary>
        /// Farming (player opt-in "auto-pick", simulator, tests): also resolve decisions with their default — first
        /// branch, the event's default option, the best draft / mythic pick, shops buy sensibly (RunSystem.FarmShopPick: the
        /// heal below half HP, then the skills and stat perks the draft logic wants most, one per step), give up when
        /// defeated, cash out in the Burrow.
        /// </summary>
        public bool Farming;
        /// <summary>Obsolete (farming shops always buy sensibly now); kept so older clients and tools still deserialize.</summary>
        public bool FarmShops;
    }

    /// <summary>Burrow: after a floor is cleared, dive to the next floor (loot stays pending).</summary>
    [Serializable] public sealed class RunBurrowDescend : GameCommand { }

    /// <summary>Burrow: cash out after a cleared floor (completes the run, pays pending loot).</summary>
    [Serializable] public sealed class RunBurrowCashOut : GameCommand { }

    /// <summary>Start researching the next level of a laboratory line (costs Biomass/Ore, takes time).</summary>
    [Serializable] public sealed class LabResearch : GameCommand { public string ResearchId; }

    /// <summary>Collect a finished research (now ≥ ReadyAtMs).</summary>
    [Serializable] public sealed class LabCollect : GameCommand { public string ResearchId; }

    /// <summary>Toggle a researched skill on/off for the run draft pool.</summary>
    [Serializable] public sealed class LabToggle : GameCommand { public string ResearchId; public bool Enabled; }
}
