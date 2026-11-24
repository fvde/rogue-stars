using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Meta.MetaTestKit;

namespace RogueStars.Rules.Tests.Meta;

/// <summary>Hero / mount summon events (docs/design/summon-events.md).</summary>
public class SummonEventTests
{
    private const SummonEventType Hero = SummonEventType.Hero;
    private const SummonEventType Mount = SummonEventType.Mount;

    /// <summary>A player with every feature (no unlock times: straight into the global rotation).</summary>
    private static (GameEngine e, GameState s) Unlocked(bool devReceipts = false)
    {
        var e = Engine(devReceipts);
        var s = Player(e);
        UnlockAll(s);
        e.Settle(s, s.CreatedMs);
        return (e, s);
    }

    private static SummonPullResult Pull(GameEngine e, ref GameState s, SummonEventType t, int count, long now, SummonPayment pay = SummonPayment.Tickets)
    {
        var def = e.Content.SummonType(t)!;
        if (pay == SummonPayment.Tickets) s.Wallet.Add(def.TicketCurrency, count);
        else s.Wallet.Add(Currencies.Diamonds, count == 10 ? def.DiamondCost10 : def.DiamondCost1);
        return (SummonPullResult)Ok(e, ref s, new SummonPull { EventType = t, Count = count, Payment = pay }, now).Payload;
    }

    // ---- content & schedule ---------------------------------------------------------------------------------

    [Fact]
    public void Content_RotationsFeatureEveryHeroAndMount_PoolsAndRewardsAreReal()
    {
        var c = ContentDb.Default;
        var hero = c.SummonType(Hero)!;
        var mount = c.SummonType(Mount)!;
        Assert.Equal(6 * D, hero.DurationMs);
        Assert.Equal(3 * D, mount.DurationMs);
        Assert.Equal(c.HeroConfig.Defs.Keys.OrderBy(k => k), hero.Rotation.OrderBy(k => k));
        Assert.Equal(c.MountConfig.Defs.Keys.OrderBy(k => k), mount.Rotation.OrderBy(k => k));
        foreach (var def in new[] { hero, mount })
        {
            // Pull system: a filler tier and the Mythic tier (no Legendary / Rare hero or mount drops), Mythic a flat 1.2%.
            Assert.Equal(new[] { Rarity.Common, Rarity.Mythic }, def.Gacha.Tiers.Select(t => t.Tier));
            Assert.All(def.Gacha.Tiers, t => Assert.NotEmpty(t.Pool));
            Assert.Equal(1.2, def.Gacha.Tiers[1].Weight, 6);
            Assert.Equal(100, def.Gacha.Tiers.Sum(t => t.Weight), 6);
            Assert.Equal(100, def.Gacha.MythicPity);
            Assert.Equal(0, def.Gacha.LegendaryPity);
            Assert.False(def.Gacha.TenPullGuarantee);
            Assert.DoesNotContain(def.Gacha.Tiers[0].Pool, x => x.Kind == RewardKinds.Hero || x.Kind == RewardKinds.HeroShards || x.Kind == RewardKinds.Mount || x.Id.StartsWith("shards_"));
            Assert.Contains(def.Gacha.Tiers[0].Pool, x => x.Id == ProgressionCurrencies.PartAwakenCores && x.Weight <= 0.05);
            // The Mythic tier is the featured batch: the Luck meter counts pulls since the last one.
            var mythic = Assert.Single(def.Gacha.Tiers[1].Pool);
            Assert.Equal(SummonEventsConfig.Featured, mythic.Id);
            Assert.Equal(def.Type == Hero ? RewardKinds.Hero : RewardKinds.Mount, mythic.Kind);
            Assert.Equal(6, def.Tasks.Count);
            Assert.All(def.Tasks, l => Assert.Equal(5, l.Targets.Count));
            Assert.All(def.Tasks, l => Assert.True(l.Targets.SequenceEqual(l.Targets.OrderBy(x => x)) && l.Targets.Distinct().Count() == 5, l.Id));
            Assert.All(def.Tasks, l => Assert.Equal(def.TicketCurrency, l.Reward.Id));
            Assert.Equal(def.DurationMs / D, def.Pass.Count);
            Assert.NotNull(c.Product(def.PassProductId));
            Assert.All(def.PackProductIds, id => Assert.Equal(def.Type, c.Product(id)!.SummonEvent));
            Assert.NotNull(c.AdPlacement(def.AdPlacementId));
            // Every reward kind and currency of the event has a granter use entry (rewards follow use).
            var rewards = def.Milestones.SelectMany(m => m.Rewards).Concat(def.Exchange.Select(x => x.Reward))
                .Concat(def.Pass.SelectMany(p => p.Free.Concat(p.Premium))).Concat(def.Tasks.Select(t => t.Reward))
                .Concat(def.Gacha.Tiers.SelectMany(t => t.Pool).Select(p => new Reward(p.Kind, p.Id, p.Amount)));
            foreach (var r in rewards)
                Assert.NotNull(c.RewardUse(SummonEventSystem.Resolve(r, def.Rotation[0])));
        }
        // Whole numbers on every player-facing amount, larger ones on round numbers.
        foreach (var x in hero.Exchange.Concat(mount.Exchange)) Assert.True(x.Price % 10 == 0, x.Id);
    }

    [Fact]
    public void Schedule_IsAGlobalBackToBackRotation_SixDayHeroAndThreeDayMountEvents()
    {
        var c = ContentDb.Default;
        foreach (var t in new[] { Hero, Mount })
        {
            var def = c.SummonType(t)!;
            var ev = SummonEventSystem.GlobalEvent(c, t, TestHelpers.T0)!;
            Assert.True(ev.StartMs <= TestHelpers.T0 && TestHelpers.T0 < ev.EndMs);
            Assert.Equal(def.DurationMs, ev.EndMs - ev.StartMs);
            Assert.Equal(0, (ev.StartMs - def.EpochMs) % D); // starts at a daily reset
            var seen = new List<string>();
            long at = ev.StartMs;
            for (int i = 0; i < def.Rotation.Count; i++)
            {
                var o = SummonEventSystem.GlobalEvent(c, t, at)!;
                Assert.Equal(at, o.StartMs);
                seen.Add(o.FeaturedId);
                at = o.EndMs; // back to back
            }
            Assert.Equal(def.Rotation.OrderBy(x => x), seen.OrderBy(x => x)); // every one once per cycle
            Assert.Equal(ev.FeaturedId, SummonEventSystem.GlobalEvent(c, t, ev.StartMs + def.Rotation.Count * def.DurationMs)!.FeaturedId);
        }
    }

    [Fact]
    public void FirstEvent_StartsWhenTheFeatureUnlocks_ThenTheGlobalRotation()
    {
        var e = Engine();
        var s = Player(e);
        var def = e.Content.SummonType(Hero)!;
        Assert.Null(SummonEventSystem.CurrentEvent(s, e.Content, Hero, TestHelpers.T0));
        Assert.Contains("feature locked", Fail(e, ref s, new SummonPull { EventType = Hero }, TestHelpers.T0));

        long unlock = TestHelpers.T0 + 5 * H;
        s.Onboarding.Unlocked[Features.Heroes] = unlock;
        var first = SummonEventSystem.CurrentEvent(s, e.Content, Hero, unlock)!;
        Assert.True(first.Welcome);
        Assert.Equal(unlock, first.StartMs);
        Assert.Equal(SummonEventSystem.GlobalEvent(e.Content, Hero, unlock)!.FeaturedId, first.FeaturedId);
        Assert.Equal(SummonEventSystem.GlobalEvent(e.Content, Hero, unlock + def.FirstEventMinMs)!.EndMs, first.EndMs);
        Assert.True(first.EndMs - first.StartMs >= def.FirstEventMinMs);
        Assert.True(first.EndMs - first.StartMs < def.FirstEventMinMs + def.DurationMs);
        e.Settle(s, unlock + H);
        Assert.Equal(first.Id, s.Summons.Events[Hero].EventId);

        // Afterwards the player is in the global rotation, from the start of the next occurrence.
        var next = SummonEventSystem.CurrentEvent(s, e.Content, Hero, first.EndMs)!;
        Assert.False(next.Welcome);
        Assert.Equal(first.EndMs, next.StartMs);
        Assert.Equal(SummonEventSystem.NextEvent(s, e.Content, Hero, unlock)!.Id, next.Id);
    }

    [Fact]
    public void FeatureUnlock_JoinsTheFirstEvent_WithItsTicketGift()
    {
        var e = Engine();
        var s = Player(e);
        // Clear the gate of Heroes (main level and account day) and let the onboarding settler record it.
        long day10 = MetaTime.DayStartMs(e.Content, MetaTime.DayIndex(e.Content, s.CreatedMs) + 9) + H;
        s.Counters[Requirements.MainLevelCounter] = 24;
        foreach (var f in e.Content.FeatureOrder) if (f.Order < e.Content.Feature(Features.Heroes)!.Order) s.Onboarding.Unlocked[f.Id] = s.CreatedMs;
        e.Settle(s, day10);
        Assert.True(Features.IsUnlocked(s, e.Content, Features.Heroes));
        var p = SummonEventSystem.Progress(s, e.Content, Hero, day10)!;
        Assert.EndsWith("@welcome", p.EventId);
        Assert.Equal(10, Bal(s, Currencies.HeroTickets));
        Assert.Null(SummonEventSystem.Progress(s, e.Content, Mount, day10)); // mounts unlock later
    }

    // ---- pulls, luck, duplicates ----------------------------------------------------------------------------

    [Fact]
    public void Pull_SpendsTicketsOrDiamonds_PaysTokens_CountsPulls()
    {
        var (e, s) = Unlocked();
        long now = TestHelpers.T0;
        var def = e.Content.SummonType(Hero)!;
        Assert.Contains("not enough", Fail(e, ref s, new SummonPull { EventType = Hero, Count = 1 }, now));
        var r = Pull(e, ref s, Hero, 10, now);
        Assert.Equal(10, r.Items.Count);
        Assert.Contains(r.Items, i => i.Tier >= Rarity.Epic); // 10-pull guarantee
        Assert.Equal(0, Bal(s, def.TicketCurrency));
        Assert.Equal(10 * def.TokensPerPull, Bal(s, def.TokenCurrency));
        Assert.Equal(10, s.Summons.Events[Hero].Pulls);
        Pull(e, ref s, Hero, 1, now, SummonPayment.Diamonds);
        Assert.Equal(0, Bal(s, Currencies.Diamonds) - e.Content.EconomyConfig.StartingWallet[Currencies.Diamonds]);
        Assert.Equal(11, s.Counter("summon_pulls_hero"));
        Assert.Contains("1 or 10", Fail(e, ref s, new SummonPull { EventType = Hero, Count = 5 }, now));
        Assert.Contains("event ended", Fail(e, ref s, new SummonPull { EventType = Hero, EventId = "hero@1" }, now));
        // Featured shards land on the featured hero.
        string featured = s.Summons.Events[Hero].FeaturedId;
        foreach (var it in r.Items.Where(i => i.Kind == RewardKinds.HeroShards && i.Featured)) Assert.Equal(featured, it.Id);
    }

    [Fact]
    public void Pulls_AreDeterministic()
    {
        string Run(long seed)
        {
            var e = Engine();
            var s = e.NewPlayer("p1", TestHelpers.T0, seed);
            UnlockAll(s);
            var all = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                all.AddRange(Pull(e, ref s, Hero, 10, TestHelpers.T0).Items.Select(x => x.Kind + x.Id + x.Amount));
                all.AddRange(Pull(e, ref s, Mount, 10, TestHelpers.T0).Items.Select(x => x.Kind + x.Id + x.Amount));
            }
            return string.Join("|", all);
        }
        Assert.Equal(Run(3), Run(3));
        Assert.NotEqual(Run(3), Run(4));
    }

    [Fact]
    public void Luck_FillsUpToTheGuaranteedFeaturedCopy_AndCarriesOverToTheNextEvent()
    {
        var (e, s) = Unlocked();
        var def = e.Content.SummonType(Hero)!;
        int max = SummonEventSystem.LuckMax(e.Content, Hero);
        Assert.Equal(def.Gacha.MythicPity, max);
        var ev = SummonEventSystem.CurrentEvent(s, e.Content, Hero, TestHelpers.T0)!;
        // Pull single pulls; Luck counts pulls since the last featured copy and never reaches max without one.
        int copies = 0, sinceCopy = 0, guaranteed = 0;
        for (int i = 0; i < 3 * max; i++)
        {
            var r = Pull(e, ref s, Hero, 1, TestHelpers.T0);
            bool copy = r.Items[0].Kind == RewardKinds.Hero;
            sinceCopy = copy ? 0 : sinceCopy + 1;
            if (copy) { copies++; Assert.Equal(ev.FeaturedId, r.Items[0].Id); Assert.True(r.Items[0].Featured); }
            if (r.LuckTriggered) { guaranteed++; Assert.True(copy); }
            Assert.Equal(sinceCopy, r.Luck);
            Assert.Equal(sinceCopy, SummonEventSystem.Luck(s, Hero));
            Assert.True(r.Luck < max);
        }
        Assert.True(copies >= 3);

        // Luck is kept until the next event of the type: pull a few, move to the next event, Luck is still there.
        Pull(e, ref s, Hero, 10, TestHelpers.T0);
        int luck = SummonEventSystem.Luck(s, Hero);
        var next = SummonEventSystem.NextEvent(s, e.Content, Hero, TestHelpers.T0)!;
        e.Settle(s, next.StartMs);
        Assert.Equal(next.Id, s.Summons.Events[Hero].EventId);
        Assert.Equal(luck, SummonEventSystem.Luck(s, Hero));
        Assert.Equal(0, SummonEventSystem.Luck(s, Mount)); // per type
        // The next pulls continue from there and the guarantee comes at max.
        int toGo = max - luck;
        bool got = false;
        for (int i = 0; i < toGo && !got; i++) got = Pull(e, ref s, Hero, 1, next.StartMs).Items[0].Kind == RewardKinds.Hero;
        Assert.True(got);
    }

    [Fact]
    public void FeaturedBatches_Are50Shards_TheFirstUnlocks_StarsAreBought_At10StarsOverflow()
    {
        var (e, s) = Unlocked();
        var c = e.Content;
        var ctx = new CommandContext { State = s, Content = c, Engine = e, NowMs = TestHelpers.T0 };
        const string hero = "hero_chef";
        Assert.Equal("new", HeroSystem.CopyEffect(s, c, hero));
        ctx.Grant(new Reward(RewardKinds.Hero, hero, 1));
        Assert.Equal(1, s.Heroes.Owned[hero].Stars);
        Assert.Equal(hero, s.Heroes.Equipped);
        Assert.Equal(0, Bal(s, Currencies.HeroShards(hero)));
        ctx.Grant(new Reward(RewardKinds.Hero, hero, 4));
        Assert.Equal(1, s.Heroes.Owned[hero].Stars); // batches are shards; stars are raised with them
        Assert.Equal(200, Bal(s, Currencies.HeroShards(hero)));
        Assert.Equal("shards", HeroSystem.CopyEffect(s, c, hero));
        ctx.Grant(new Reward(RewardKinds.Hero, hero, 15));
        for (int star = 2; star <= 10; star++) Ok(e, ref s, new HeroStarUp { HeroId = hero }, TestHelpers.T0);
        Assert.Equal(10, s.Heroes.Owned[hero].Stars);
        Assert.Equal(0, Bal(s, Currencies.HeroShards(hero))); // 20 batches = 1 000 shards = unlock to 10★
        Assert.Equal("overflow", HeroSystem.CopyEffect(s, c, hero));
        ctx = new CommandContext { State = s, Content = c, Engine = e, NowMs = TestHelpers.T0 };
        ctx.Grant(new Reward(RewardKinds.Hero, hero, 1));
        Assert.Equal(0, Bal(s, Currencies.HeroShards(hero)));
        Assert.Equal(c.HeroConfig.CopyOverflow.Amount, Bal(s, Currencies.HeroSigils));
        Assert.Equal(10, s.Counter("hero_stars_max"));

        // A pulled batch reports what it did.
        s.Summons.Luck[Hero] = new GachaPityState { SinceMythic = c.SummonType(Hero)!.Gacha.MythicPity - 1 };
        var r = Pull(e, ref s, Hero, 1, TestHelpers.T0);
        var batch = Assert.Single(r.Items);
        Assert.Equal(RewardKinds.Hero, batch.Kind);
        Assert.True(r.LuckTriggered);
        Assert.Equal(batch.Id == hero ? "overflow" : "new", batch.Note);
        Assert.Equal(batch.Id == hero ? 0 : 50, batch.Shards);
        Assert.Equal(batch.Id == hero ? 10 : 1, batch.Stars);

        // Mounts: the same table to 10★ (stars 6–10 also take Mount Cores).
        ctx = new CommandContext { State = s, Content = c, Engine = e, NowMs = TestHelpers.T0 };
        ctx.Grant(new Reward(RewardKinds.Mount, "mount_magma_barge", 20));
        s.Wallet.Add(Currencies.MountCores, 2500);
        for (int star = 2; star <= 10; star++) Ok(e, ref s, new MountStarUp { MountId = "mount_magma_barge" }, TestHelpers.T0);
        Assert.Equal(10, s.Mounts.Owned["mount_magma_barge"].Stars);
        Assert.Equal(0, Bal(s, Currencies.MountShards("mount_magma_barge")));
        Assert.Equal(0, Bal(s, Currencies.MountCores));
    }

    // ---- tasks, milestones, exchange, pass ------------------------------------------------------------------

    [Fact]
    public void Tasks_CountFromTheEventStart_ClaimOnce_ResetWithTheNextEvent()
    {
        var (e, s) = Unlocked();
        var def = e.Content.SummonType(Hero)!;
        var ev = SummonEventSystem.CurrentEvent(s, e.Content, Hero, TestHelpers.T0)!;
        s.Count("run_enemies_defeated", 5000); // before the next event: does not count there
        var next = SummonEventSystem.NextEvent(s, e.Content, Hero, TestHelpers.T0)!;
        long now = next.StartMs + H;
        e.Settle(s, now);
        var line = def.Tasks.First(t => t.CounterKey == "run_enemies_defeated");
        var p = s.Summons.Events[Hero];
        Assert.Equal(next.Id, p.EventId);
        Assert.Equal(0, SummonEventSystem.TaskProgress(s, p, line));
        Assert.Contains("not complete", Fail(e, ref s, new SummonTaskClaim { EventType = Hero, LineId = line.Id, Index = 0 }, now));

        s.Count("run_enemies_defeated", line.Targets[2]); // reaches the first three targets
        var status = SummonEventSystem.Tasks(s, e.Content, Hero, now);
        var st = status.First(x => x.LineId == line.Id);
        Assert.True(st.Claimable);
        Assert.Equal(status[0].LineId, line.Id); // claimable lines first
        Assert.Equal(3, SummonEventSystem.ClaimableTasks(s, e.Content, Hero, now));
        Ok(e, ref s, new SummonTaskClaim { EventType = Hero, LineId = line.Id, Index = 0 }, now);
        Assert.Contains("already claimed", Fail(e, ref s, new SummonTaskClaim { EventType = Hero, LineId = line.Id, Index = 0 }, now));
        Assert.Equal(2, (int)Ok(e, ref s, new SummonTaskClaimAll { EventType = Hero }, now).Payload);
        Assert.Equal(3, Bal(s, def.TicketCurrency));
        Assert.Contains("nothing", Fail(e, ref s, new SummonTaskClaimAll { EventType = Hero }, now));
        Assert.Equal(line.Targets[3], SummonEventSystem.Tasks(s, e.Content, Hero, now).First(x => x.LineId == line.Id).Target);

        // The next event starts from scratch: new baselines, nothing claimed; the tickets stay (persistent).
        var third = SummonEventSystem.NextEvent(s, e.Content, Hero, now)!;
        e.Settle(s, third.StartMs);
        Assert.Equal(0, SummonEventSystem.ClaimableTasks(s, e.Content, Hero, third.StartMs));
        Assert.Empty(s.Summons.Events[Hero].Claimed);
        Assert.Equal(3, Bal(s, def.TicketCurrency));
        Assert.NotEqual(ev.FeaturedId, SummonEventSystem.CurrentEvent(s, e.Content, Hero, third.StartMs)!.FeaturedId);
    }

    [Fact]
    public void Milestones_PayByPullsInThisEvent()
    {
        var (e, s) = Unlocked();
        var def = e.Content.SummonType(Mount)!;
        var m = def.Milestones[0];
        Assert.Contains("not reached", Fail(e, ref s, new SummonMilestoneClaim { EventType = Mount, Pulls = m.Pulls }, TestHelpers.T0));
        for (int i = 0; i < m.Pulls / 10; i++) Pull(e, ref s, Mount, 10, TestHelpers.T0);
        long fuel = Bal(s, Currencies.Fuel);
        Assert.Equal(1, SummonEventSystem.ClaimableMilestones(s, e.Content, Mount, TestHelpers.T0));
        Ok(e, ref s, new SummonMilestoneClaim { EventType = Mount, Pulls = m.Pulls }, TestHelpers.T0);
        Assert.Equal(fuel + m.Rewards[0].Amount, Bal(s, Currencies.Fuel));
        Assert.Contains("already", Fail(e, ref s, new SummonMilestoneClaim { EventType = Mount, Pulls = m.Pulls }, TestHelpers.T0));
        // The featured copy milestone resolves to the event's featured mount.
        var last = def.Milestones[^1];
        s.Summons.Events[Mount].Pulls = last.Pulls;
        string featured = s.Summons.Events[Mount].FeaturedId;
        bool owned = MountSystem.Get(s, featured) != null;
        long shards = Bal(s, Currencies.MountShards(featured));
        Ok(e, ref s, new SummonMilestoneClaim { EventType = Mount, Pulls = last.Pulls }, TestHelpers.T0);
        // a batch of 50 shards: it unlocks the mount when not owned, else pays the shards
        Assert.Equal(1, MountSystem.Get(s, featured)!.Stars);
        Assert.Equal(owned ? shards + 50 : shards, Bal(s, Currencies.MountShards(featured)));
    }

    [Fact]
    public void Exchange_SpendsTokens_WithEventLimits_ThatResetNextEvent()
    {
        var (e, s) = Unlocked();
        var def = e.Content.SummonType(Hero)!;
        var item = def.Exchange.First(x => x.Id == "hero_shards");
        string featured = SummonEventSystem.CurrentEvent(s, e.Content, Hero, TestHelpers.T0)!.FeaturedId;
        Assert.Contains("not enough", Fail(e, ref s, new SummonExchangeBuy { EventType = Hero, ItemId = item.Id }, TestHelpers.T0));
        s.Wallet.Add(def.TokenCurrency, item.Price * (item.EventLimit + 1));
        for (int i = 0; i < item.EventLimit; i++) Ok(e, ref s, new SummonExchangeBuy { EventType = Hero, ItemId = item.Id }, TestHelpers.T0);
        Assert.Contains("limit", Fail(e, ref s, new SummonExchangeBuy { EventType = Hero, ItemId = item.Id }, TestHelpers.T0));
        Assert.Equal(item.Reward.Amount * item.EventLimit, Bal(s, Currencies.HeroShards(featured)));
        Assert.Equal(item.Price, Bal(s, def.TokenCurrency));
        var next = SummonEventSystem.NextEvent(s, e.Content, Hero, TestHelpers.T0)!;
        Ok(e, ref s, new SummonExchangeBuy { EventType = Hero, ItemId = item.Id }, next.StartMs);
        Assert.Equal(item.Reward.Amount, Bal(s, Currencies.HeroShards(next.FeaturedId)));
        Assert.Contains("unknown", Fail(e, ref s, new SummonExchangeBuy { EventType = Hero, ItemId = "nope" }, next.StartMs));
    }

    [Fact]
    public void Pass_UnlocksADayPerReset_PremiumNeedsThePurchaseOfThisEvent()
    {
        var (e, s) = Unlocked(devReceipts: true);
        var def = e.Content.SummonType(Mount)!;
        var ev = SummonEventSystem.NextEvent(s, e.Content, Mount, TestHelpers.T0)!; // starts at a reset after T0
        long day1 = ev.StartMs + H;
        e.Settle(s, day1);
        Assert.Equal(1, SummonEventSystem.PassDaysUnlocked(s, e.Content, Mount, day1));
        Assert.Contains("not reached", Fail(e, ref s, new SummonPassClaim { EventType = Mount, Day = 2 }, day1));
        Assert.Contains("premium", Fail(e, ref s, new SummonPassClaim { EventType = Mount, Day = 1, Premium = true }, day1));
        Ok(e, ref s, new SummonPassClaim { EventType = Mount, Day = 1 }, day1);
        Assert.Equal(def.Pass[0].Free[0].Amount, Bal(s, def.TicketCurrency));

        long day3 = ev.StartMs + 2 * D + H;
        Assert.Equal(3, SummonEventSystem.PassDaysUnlocked(s, e.Content, Mount, day3));
        // Premium: the pass product of this event (dev receipt offline); retroactive for the unlocked days.
        Ok(e, ref s, new PurchaseStart { ProductId = def.PassProductId }, day3);
        Ok(e, ref s, new PurchaseComplete { ProductId = def.PassProductId, ReceiptId = "dev:pass" }, day3);
        Assert.True(s.Summons.Events[Mount].PassPremium);
        Assert.Equal("already owned this event", ShopSystem.PurchaseBlockReason(s, e.Content, e.Content.Product(def.PassProductId), day3));
        Assert.Equal(5, (int)Ok(e, ref s, new SummonPassClaimAll { EventType = Mount }, day3).Payload); // free 2, 3 + premium 1, 2, 3
        long expected = def.Pass.Sum(d => d.Free[0].Amount + d.Premium.Where(r => r.Id == def.TicketCurrency).Sum(r => r.Amount));
        Assert.Equal(expected, Bal(s, def.TicketCurrency));
        string featured = ev.FeaturedId;
        Assert.Equal(def.Pass[^1].Premium.Where(r => r.Id != def.TicketCurrency).Sum(r => r.Amount), Bal(s, Currencies.MountShards(featured)));

        // The next event's pass is not owned.
        var next = SummonEventSystem.NextEvent(s, e.Content, Mount, day3)!;
        e.Settle(s, next.StartMs);
        Assert.False(s.Summons.Events[Mount].PassPremium);
        Assert.Null(ShopSystem.PurchaseBlockReason(s, e.Content, e.Content.Product(def.PassProductId), next.StartMs));
    }

    /// <summary>An ended event's unclaimed task and pass rewards are mailed when the player joins the next event (no lapse).</summary>
    [Fact]
    public void EventEnd_MailsTheUnclaimedTaskAndPassRewards()
    {
        var (e, s) = Unlocked(devReceipts: true);
        var def = e.Content.SummonType(Mount)!;
        var ev = SummonEventSystem.NextEvent(s, e.Content, Mount, TestHelpers.T0)!;
        long day1 = ev.StartMs + H;
        e.Settle(s, day1);
        var line = def.Tasks.First(t => t.CounterKey == "run_enemies_defeated");
        s.Count(line.CounterKey, line.Targets[1]);                                             // two targets reached ...
        Ok(e, ref s, new SummonTaskClaim { EventType = Mount, LineId = line.Id, Index = 0 }, day1); // ... one claimed
        Ok(e, ref s, new SummonPassClaim { EventType = Mount, Day = 1 }, day1);                   // free day 1 claimed
        Ok(e, ref s, new PurchaseStart { ProductId = def.PassProductId }, day1);
        Ok(e, ref s, new PurchaseComplete { ProductId = def.PassProductId, ReceiptId = "dev:pass" }, day1);
        // (the event running at T0 ended on joining this one: its unclaimed free pass days are in the mailbox already)
        long tickets = Bal(s, def.TicketCurrency) + s.Mail.Inbox.Where(MailSystem.HasUnclaimed).SelectMany(m => m.Rewards).Where(x => x.Id == def.TicketCurrency).Sum(x => x.Amount);
        int inbox = s.Mail.Inbox.Count;

        // The player comes back after the event: the first command joins the next event and mails what the last one owed.
        var next = SummonEventSystem.NextEvent(s, e.Content, Mount, day1)!;
        var r = Ok(e, ref s, new MailClaimAll(), next.StartMs + H);
        Assert.Contains(r.Events, x => x.Type == "summon_event_mailed" && x.Args[1] == ev.Id);
        Assert.Equal(next.Id, s.Summons.Events[Mount].EventId);
        Assert.Equal(inbox + 1, s.Mail.Inbox.Count);
        var mail = s.Mail.Inbox[^1];
        Assert.Contains("Mount Summon", mail.Title);
        Assert.True(mail.Claimed); // MailClaimAll claimed it in the same command
        // the reached unclaimed target, free days 2–3 and premium days 1–3 (merged); the featured shards of the ended event
        long expTickets = line.Reward.Amount + def.Pass.Skip(1).Sum(d => d.Free.Where(x => x.Id == def.TicketCurrency).Sum(x => x.Amount))
            + def.Pass.Sum(d => d.Premium.Where(x => x.Id == def.TicketCurrency).Sum(x => x.Amount));
        Assert.Equal(expTickets, mail.Rewards.Single(x => x.Id == def.TicketCurrency).Amount);
        Assert.Equal(tickets + expTickets, Bal(s, def.TicketCurrency));
        long shards = def.Pass.Sum(d => d.Premium.Where(x => x.Id != def.TicketCurrency).Sum(x => x.Amount));
        Assert.Equal(shards, Bal(s, Currencies.MountShards(ev.FeaturedId)));

        // The second event's free pass days unlocked and were never claimed: they are mailed when the third starts.
        var third = SummonEventSystem.NextEvent(s, e.Content, Mount, next.StartMs + H)!;
        var r3 = Ok(e, ref s, new MailRead { MailId = mail.Id }, third.StartMs + H);
        Assert.Equal(third.Id, s.Summons.Events[Mount].EventId);
        Assert.Contains(r3.Events, x => x.Type == "summon_event_mailed" && x.Args[1] == next.Id);
        Assert.Equal(def.Pass.Sum(d => d.Free.Sum(x => x.Amount)), s.Mail.Inbox[^1].Rewards.Single().Amount);
        // Everything claimed (and no task reached): no mail when the third ends.
        long lastDay = third.StartMs + (def.Pass.Count - 1) * D + H;
        Ok(e, ref s, new SummonPassClaimAll { EventType = Mount }, lastDay);
        Assert.Empty(SummonEventSystem.Unclaimed(s, e.Content, def, s.Summons.Events[Mount]));
        int count = s.Mail.Inbox.Count;
        var fourth = SummonEventSystem.NextEvent(s, e.Content, Mount, lastDay)!;
        var r4 = Ok(e, ref s, new MailClaimAll(), fourth.StartMs + H);
        Assert.Equal(fourth.Id, s.Summons.Events[Mount].EventId);
        Assert.DoesNotContain(r4.Events, x => x.Type == "summon_event_mailed");
        Assert.Equal(count, s.Mail.Inbox.Count);
    }

    [Fact]
    public void Packs_AreSoldDuringEvents_WithDailyLimits_AndTheAdTicketIsInstantWhileAdsAreDisabled()
    {
        var e = Engine(devReceipts: true);
        e.Content.Shop.AdsDisabled = true;
        var s = Player(e);
        var def = e.Content.SummonType(Hero)!;
        var pack = e.Content.Product(def.PackProductIds[0])!;
        Assert.Equal("feature locked", ShopSystem.PurchaseBlockReason(s, e.Content, pack, TestHelpers.T0));
        UnlockAll(s);
        long now = TestHelpers.T0;
        for (int i = 0; i < pack.DailyLimit; i++)
        {
            Ok(e, ref s, new PurchaseStart { ProductId = pack.Id }, now);
            Ok(e, ref s, new PurchaseComplete { ProductId = pack.Id, ReceiptId = "dev:p" + i }, now);
        }
        Assert.Equal(pack.Contents.First(r => r.Id == def.TicketCurrency).Amount * pack.DailyLimit, Bal(s, def.TicketCurrency));
        Assert.Equal("daily limit reached", ShopSystem.PurchaseBlockReason(s, e.Content, pack, now));
        Assert.Contains("daily limit", Fail(e, ref s, new PurchaseStart { ProductId = pack.Id }, now));
        Assert.Null(ShopSystem.PurchaseBlockReason(s, e.Content, pack, now + D));

        long tickets = Bal(s, def.TicketCurrency);
        Ok(e, ref s, new AdWatched { PlacementId = def.AdPlacementId }, now); // instant: no ad, no token
        Assert.Equal(tickets + 1, Bal(s, def.TicketCurrency));
        Assert.Contains("cap", Fail(e, ref s, new AdWatched { PlacementId = def.AdPlacementId }, now));
    }

    // ---- migration ------------------------------------------------------------------------------------------

    [Fact]
    public void Migration_KeepsHeroesMountsAndShards_GivesTheTicketGiftOnce()
    {
        var e = Engine();
        var s = Player(e);
        Assert.Equal(SummonEventSystem.MigrationVersion, s.Summons.Migrated); // new players have nothing to migrate
        // An old save: heroes and mounts unlocked before summon events, with heroes, shards and a mount.
        s.Summons = new SummonState();
        s.Onboarding.Unlocked[Features.Heroes] = s.CreatedMs;
        s.Onboarding.Unlocked[Features.Mounts] = s.CreatedMs;
        s.Heroes.Owned["hero_star_knight"] = new HeroEntry { Stars = 4, Level = 12 };
        s.Wallet.Add(Currencies.HeroShards("hero_chef"), 33);
        s.Mounts.Owned["mount_scout_saucer"] = new MountEntry { Stars = 2 };
        s.Wallet.Add(Currencies.MountShards("mount_scout_saucer"), 70);
        // The save as an old build wrote it: no "Summons" field at all.
        var json = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(s, TestHelpers.Json))!.AsObject();
        Assert.True(json.Remove("Summons"));
        var loaded = System.Text.Json.JsonSerializer.Deserialize<GameState>(json.ToJsonString(), TestHelpers.Json)!;
        Assert.Equal(0, loaded.Summons.Migrated);

        long later = TestHelpers.T0 + 30 * D;
        e.Settle(loaded, later);
        Assert.Equal(10, Bal(loaded, Currencies.HeroTickets));
        Assert.Equal(10, Bal(loaded, Currencies.MountTickets));
        Assert.Equal(4, loaded.Heroes.Owned["hero_star_knight"].Stars);
        Assert.Equal(33, Bal(loaded, Currencies.HeroShards("hero_chef")));
        Assert.Equal(70, Bal(loaded, Currencies.MountShards("mount_scout_saucer")));
        // Long-unlocked features go straight into the global rotation.
        Assert.False(SummonEventSystem.CurrentEvent(loaded, e.Content, Hero, later)!.Welcome);
        Assert.Equal(SummonEventSystem.GlobalEvent(e.Content, Hero, later)!.Id, loaded.Summons.Events[Hero].EventId);
        e.Settle(loaded, later + D);
        Assert.Equal(10, Bal(loaded, Currencies.HeroTickets)); // once
    }

    [Fact]
    public void State_SurvivesAJsonRoundTrip()
    {
        var (e, s) = Unlocked();
        Pull(e, ref s, Hero, 10, TestHelpers.T0);
        Pull(e, ref s, Mount, 10, TestHelpers.T0);
        var c = TestHelpers.Clone(s);
        Assert.Equal(s.Summons.Events[Hero].EventId, c.Summons.Events[Hero].EventId);
        Assert.Equal(s.Summons.Events[Mount].Pulls, c.Summons.Events[Mount].Pulls);
        Assert.Equal(SummonEventSystem.Luck(s, Mount), SummonEventSystem.Luck(c, Mount));
        Assert.Equal(s.Summons.Events[Hero].Baseline.Count, c.Summons.Events[Hero].Baseline.Count);
    }
}
