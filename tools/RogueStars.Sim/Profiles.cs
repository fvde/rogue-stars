namespace RogueStars.Sim;

/// <summary>
/// How much a bot plays per day: session start hours (UTC) and minutes per session. Every action costs session time
/// (see <see cref="Timing"/>); runs stop when the session budget or stamina (incl. allowed refills) runs out.
/// </summary>
public sealed class TimeProfile
{
    public string Name = "";
    public double[] SessionHours = Array.Empty<double>();
    public double MinutesPerSession;
    /// <summary>Share of each rewarded-ad daily cap the player is willing to watch (time permitting).</summary>
    public double AdWillingness = 1;
    /// <summary>Arena fights per day the player bothers with (tickets permitting).</summary>
    public int ArenaFightsPerDay;
    /// <summary>Share of a day's run stamina spent farming the Dark Side for laboratory Data (short sessions favour the campaign).</summary>
    public double DarkFarmShare = 0.4;
    public double MinutesPerDay => SessionHours.Length * MinutesPerSession;

    public static readonly TimeProfile Casual = new()
    {
        Name = "Casual", SessionHours = new[] { 8.25, 20.5 }, MinutesPerSession = 7.5, AdWillingness = 0.5, ArenaFightsPerDay = 2, DarkFarmShare = 0.15,
    };
    public static readonly TimeProfile Active = new()
    {
        Name = "Active", SessionHours = new[] { 7.5, 12.5, 18.0, 22.0 }, MinutesPerSession = 15, AdWillingness = 1, ArenaFightsPerDay = 5, DarkFarmShare = 0.35,
    };
    public static readonly TimeProfile Hardcore = new()
    {
        Name = "Hardcore", SessionHours = new[] { 7.0, 9.5, 12.0, 14.5, 17.0, 19.5, 22.0 }, MinutesPerSession = 180.0 / 7, AdWillingness = 1, ArenaFightsPerDay = 12,
    };
    public static readonly TimeProfile[] All = { Casual, Active, Hardcore };

    public static TimeProfile ByName(string n) => All.First(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
}

/// <summary>What a bot buys with real money (always through PurchaseStart + PurchaseComplete with a dev receipt).</summary>
public sealed class SpendProfile
{
    public string Name = "";
    public bool BuysNoAds, BuysDailyDiamonds, BuysMonthlyPass, BuysSeasonPass;
    /// <summary>Extra monthly budget (US cents) for Diamond packs and offers; 0 = none.</summary>
    public int MonthlyBudgetCents;
    /// <summary>Diamond stamina refills per day the player is willing to buy (escalating price list).</summary>
    public int DiamondRefillsPerDay;
    /// <summary>Diamonds kept back from optional spending.</summary>
    public long DiamondReserve;
    /// <summary>Completes building/lab/breeding timers with Diamonds.</summary>
    public bool RushesTimers;
    public bool IsSpender => BuysNoAds || MonthlyBudgetCents > 0;

    public static readonly SpendProfile F2P = new() { Name = "F2P", DiamondRefillsPerDay = 1, DiamondReserve = 300 };
    public static readonly SpendProfile Dolphin = new()
    {
        Name = "Dolphin", BuysNoAds = true, BuysDailyDiamonds = true, BuysMonthlyPass = true, BuysSeasonPass = true,
        DiamondRefillsPerDay = 2, DiamondReserve = 300,
    };
    public static readonly SpendProfile Whale = new()
    {
        Name = "Whale", BuysNoAds = true, BuysDailyDiamonds = true, BuysMonthlyPass = true, BuysSeasonPass = true,
        MonthlyBudgetCents = 30_000, DiamondRefillsPerDay = 5, DiamondReserve = 0, RushesTimers = true,
    };
    public static readonly SpendProfile[] All = { F2P, Dolphin, Whale };

    public static SpendProfile ByName(string n) => All.First(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Session-time cost of player actions (minutes). Run time is derived from what actually happened in the run:
/// overhead + per roll + per decision popup + per battle round (battle speed ×2 free, ×3 with the monthly pass).
/// </summary>
public static class Timing
{
    public const double SessionOverhead = 0.5;   // open the app, lobby, collect popups
    public const double Claims = 0.5;            // login, quests, mail, passes (first session of the day)
    public const double City = 0.75;             // collect, upgrades, workers, breeding
    public const double Upgrades = 0.5;          // worm, parts, heroes, mounts, aliens
    public const double PullCommand = 0.15;      // one 1- or 10-pull with its reveal
    public const double Ad = 0.5;                // one rewarded ad (No Ads: instant)
    public const double AdInstant = 0.03;
    public const double ArenaFight = 0.75;
    public const double InvasionAttempt = 1.0;

    public const double RunOverheadSec = 20;     // start, board reveal, results screen
    public const double RollSec = 2.5;           // auto-roll + token movement
    public const double DecisionSec = 3;         // draft / event / shop popups auto-resolved after a short delay
    public const double BattleRoundSec = 1.6;    // at ×1; divided by the battle speed (×2 free, ×3 monthly pass)
}
