using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using RogueStars.Sim;

namespace RogueStars.Sim.Tests;

/// <summary>
/// Rewards follow use (GDD §13), played by the simulator bot through the real rules: at the end of every one of the first
/// 21 days the wallet only holds currencies the player can use, and the stash never holds more than a feature's small
/// release (free-source caps; the bot buys nothing for locked features).
/// </summary>
public class RewardGateSimTests
{
    [Theory]
    [InlineData("Casual")]
    [InlineData("Active")]
    [InlineData("Hardcore")]
    public void Wallet_HoldsOnlyUsableCurrencies_StashStaysSmall(string profile)
    {
        var p = new SimPlayer(TimeProfile.ByName(profile), SpendProfile.F2P, 3);
        for (int d = 0; d < 21; d++)
        {
            p.PlayDay(d);
            foreach (var kv in p.S.Wallet.Balances)
                if (kv.Value > 0)
                    Assert.True(RewardGate.CurrencyHasUse(p.S, p.C, kv.Key, p.Now), $"{profile} day {d + 1}: {kv.Value} {kv.Key} without a use");
            foreach (var kv in p.S.Stash.Currencies)
            {
                long cap = p.C.CurrencyUse(kv.Key)!.StashCap;
                if (cap >= 0) Assert.True(kv.Value <= cap, $"{profile} day {d + 1}: stash {kv.Key} {kv.Value} > cap {cap}");
            }
        }
        // Three weeks in, every feature of the first two weeks has released its stash.
        Assert.DoesNotContain(p.S.Stash.Currencies.Keys, k => k == Currencies.Biomass || k == Currencies.Ore || k == Currencies.PartTokens);
    }

    [Fact]
    public void Day1_Inventory_IsStaminaDiamondsOreBiomassAndParts()
    {
        var p = new SimPlayer(TimeProfile.Active, SpendProfile.F2P, 1);
        p.PlayDay(0);
        var owned = p.S.Wallet.Balances.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToHashSet();
        // Day 1 uses (level unlocks, ~L8 for an Active player): runs (stamina), Diamonds, part levels (Ore; Gear Cores once a
        // part can pass level 10), worm levels (Biomass, Worm Level at L3); Aliens (L6) bring their Grain. Data and tokens still wait.
        Assert.Subset(new HashSet<string> { Currencies.Stamina, Currencies.Diamonds, Currencies.Ore, Currencies.GearCores, Currencies.Biomass, Currencies.Grain }, owned);
        Assert.Contains(Currencies.Biomass, owned);
        Assert.NotEmpty(p.S.Parts.Items);
    }
}
