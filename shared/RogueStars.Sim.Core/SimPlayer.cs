using System;
using System.Collections.Generic;
using System.Linq;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim
{
    /// <summary>
    /// A bot that plays one account through the real rules engine: every state change is a command executed by
    /// <see cref="GameEngine.Execute"/> (server-only commands are issued the way the server issues them: purchases with dev
    /// receipts, arena results, invasion damage/outcomes, guild membership). Battles outside runs use the rules'
    /// <see cref="BattleSimulator"/> like the server does. Bot decisions use their own deterministic RNG stream.
    /// </summary>
    public sealed partial class SimPlayer
    {
        public static readonly long T0 = 1_799_020_800_000; // Monday 2027-01-04 00:00 UTC (banner rotation epoch)
        public const long Day = GameClock.Day, Hour = GameClock.Hour, Minute = 60_000;

        public readonly GameEngine E;
        public readonly ContentDb C;
        public GameState S;
        public readonly TimeProfile Time;
        public readonly SpendProfile Spend;
        public readonly long Seed;
        private readonly Rng _bot;

        public long Now;
        private double _minutesLeft;
        public int DayIndex;
        /// <summary>Real money spent, US cents.</summary>
        public long SpentCents;
        private long _monthBudgetUsed;
        private int _month = -1;
        public long Commands, FailedCommands;
        /// <summary>Feature id → (account day 1-based, highest main level cleared) when the unlock was recorded.</summary>
        public readonly Dictionary<string, (int day, long level)> Unlocks = new();
        public double MinutesPlayed, MinutesInRuns;
        public int RunsPlayed, RunsWon;
        /// <summary>Per-run records (run length, drafts, battles, loot) when <see cref="KeepRunLog"/> is set (simulator --run-length).</summary>
        public readonly List<RunRecord> RunLog = new List<RunRecord>();
        public bool KeepRunLog;

        /// <summary>Hero the spender targets with banners (whales); F2P/dolphins simply play the best hero they have.</summary>
        public const string TargetHero = "hero_star_knight";
        public const string GuildId = "sim_guild";

        // Run selection memory: per level+mode, results history (EMA) and the power at the last failed attempt.
        private readonly Dictionary<string, double> _winEma = new();
        private readonly Dictionary<string, long> _failPower = new();
        private readonly Dictionary<string, int> _failCount = new();
        private readonly HashSet<string> _invasionsJoined = new();
        private long _power;

        // Income/spend tracking for hoarding/starvation analysis (gross flows per tracked currency).
        public static readonly string[] Tracked =
        {
            Currencies.Biomass, Currencies.Ore, Currencies.Grain, Currencies.GearCores, Currencies.AdvancementPlates, Currencies.Fuel,
            Currencies.HeroXp, Currencies.Diamonds, Currencies.PartTokens, Currencies.BlueprintTokens, Currencies.AlienTokens,
            Currencies.Stamina, Currencies.GuildMarks, Currencies.MountCores, ProgressionCurrencies.PartAwakenCores, Currencies.ArenaTickets,
            Currencies.Data, Currencies.ArenaMedals,
        };

        /// <summary>Currencies whose income is also split by source (runs by mode, city, quests, arena, invasions, shop, other).</summary>
        public static readonly string[] SourceTracked = { Currencies.Biomass, Currencies.Ore, ProgressionCurrencies.PartAwakenCores, Currencies.Data, Currencies.ArenaMedals, Currencies.Grain };
        public static readonly string[] Sources = { "run_main", "run_burrow", "run_dark", "city", "quests", "arena", "invasion", "shop", "other" };
        /// <summary>Cumulative income per SourceTracked currency (row) and Sources entry (column).</summary>
        public readonly double[,] EarnedBySource = new double[SourceTracked.Length, Sources.Length];
        public readonly double[] Earned = new double[Tracked.Length];
        public readonly double[] SpentRes = new double[Tracked.Length];
        private readonly long[] _before = new long[Tracked.Length];

        public SimPlayer(TimeProfile time, SpendProfile spend, long seed, ContentDb content = null)
            : this(time, spend, seed, content, null, T0)
        {
        }

        /// <summary>
        /// A bot for <paramref name="playerId"/> whose account is created at <paramref name="startMs"/> (day 0 starts there;
        /// session hours are relative to it, so pass a UTC midnight). Used by the server's dev tools (account-age jump).
        /// </summary>
        public SimPlayer(TimeProfile time, SpendProfile spend, long seed, ContentDb content, string playerId, long startMs)
        {
            Time = time; Spend = spend; Seed = seed;
            Start = startMs;
            C = content ?? SimContent.Create();
            E = new GameEngine(C);
            S = E.NewPlayer(playerId ?? "sim_" + time.Name + "_" + spend.Name + "_" + seed, startMs, seed);
            S.DisplayName = time.Name + " " + spend.Name;
            _bot = Rng.FromSeed(seed * 7919 + 17);
            Now = startMs;
        }

        /// <summary>Account creation time = start of simulated day 0 (<see cref="T0"/> for the simulator).</summary>
        public readonly long Start;

        // ---- command plumbing --------------------------------------------------------------------------------

        public CommandResult Exec(GameCommand cmd)
        {
            for (int i = 0; i < Tracked.Length; i++) _before[i] = S.Wallet.Get(Tracked[i]);
            int source = SourceOf(cmd);
            var r = E.Execute(ref S, cmd, Now);
            Commands++;
            if (!r.Ok) { FailedCommands++; return r; }
            if (S.Onboarding.Unlocked.Count != Unlocks.Count)
                foreach (var kv in S.Onboarding.Unlocked)
                    if (!Unlocks.ContainsKey(kv.Key))
                        Unlocks[kv.Key] = ((int)Requirements.AccountAgeDays(S, C, kv.Value) + 1, S.Counter(Requirements.MainLevelCounter));
            foreach (var kv in S.Lab.Entries) // synergy packages researched (signatures pass): recorded like feature unlocks
                if (kv.Value.Level > 0 && !Unlocks.ContainsKey(kv.Key) && C.LabNode(kv.Key)?.Kind == LabNodeKind.Package)
                    Unlocks[kv.Key] = (DayIndex + 1, S.Counter(Requirements.MainLevelCounter));
            for (int i = 0; i < Tracked.Length; i++)
            {
                long d = S.Wallet.Get(Tracked[i]) - _before[i];
                if (d > 0) Earned[i] += d; else if (d < 0) SpentRes[i] -= d;
                if (d > 0)
                {
                    int si = Array.IndexOf(SourceTracked, Tracked[i]);
                    if (si >= 0) EarnedBySource[si, source] += d;
                }
            }
            return r;
        }

        private bool Ok(GameCommand cmd) => Exec(cmd).Ok;

        /// <summary>Income source of a command (index into <see cref="Sources"/>).</summary>
        private int SourceOf(GameCommand cmd)
        {
            static int Mode(RunMode m) => m == RunMode.Burrow ? 1 : m == RunMode.DarkSide ? 2 : 0;
            switch (cmd)
            {
                case RunStart rs: return Mode(rs.Mode);
                case RunDoubleRewards: return S.Run.LastSummary != null ? Mode(S.Run.LastSummary.Mode) : 0;
                case RunAutoStep or RunBurrowDescend or RunBurrowCashOut or RunRevive or RunAbandon:
                    return S.Run.Active != null ? Mode(S.Run.Active.Mode) : 0;
                case CityCollect: return 3;
                case LoginClaim or NewPlayerClaim or QuestClaimAll or QuestClaimChest or SeasonPassClaimAll or MailClaimAll or FundClaim
                    or FeatureCelebrate or TutorialAdvance: return 4;
                case ArenaFightResult or RogueStars.Rules.Systems.ArenaWeekEnd: return 5;
                case LiveOpsApplyOutcome or InvasionRecordDamage: return 6;
                case ShopBuyItem or GuildShopBuy or ShopClaimDaily or ShopClaimFree or PurchaseComplete or AdWatched: return 7;
                default: return 8;
            }
        }

        private long Bal(string c) => S.Wallet.Get(c);

        private bool Unlocked(string feature) => Features.IsUnlocked(S, C, feature);

        private bool Spendable(long diamonds) => Bal(Currencies.Diamonds) - diamonds >= Spend.DiamondReserve;

        private void UseTime(double minutes) { _minutesLeft -= minutes; MinutesPlayed += minutes; Now += (long)(minutes * Minute); }

        private bool NoAds => ShopSystem.HasNoAds(S, C);

        private double AdCost => NoAds ? Timing.AdInstant : Timing.Ad;

        /// <summary>Watching ads: everyone with No Ads; F2P according to willingness and remaining time.</summary>
        private bool WillWatchAd(string placement)
        {
            if (ShopSystem.AdViewsLeft(S, C, placement, Now) <= 0) return false;
            if (NoAds) return true;
            var def = C.AdPlacement(placement);
            int used = def.DailyCap - ShopSystem.AdViewsLeft(S, C, placement, Now);
            return used < Math.Ceiling(def.DailyCap * Time.AdWillingness) && _minutesLeft > Timing.Ad;
        }

        private string AdToken => NoAds ? null : "sim-ad";

        public long Power => _power;

        public void RefreshPower() => _power = E.BuildLoadout(S, Now).Power;

        // ---- the day -------------------------------------------------------------------------------------------

        /// <summary>Plays one day (all sessions of the time profile).</summary>
        public void PlayDay(int day) => PlayDay(day, long.MaxValue);

        /// <summary>
        /// Plays one day up to <paramref name="untilMs"/>: a session that would still be running within
        /// <see cref="SessionEndMarginMs"/> of it is skipped together with the rest of the day, and time stops at the last
        /// session (a partly played "today" for the server's dev tools). <c>long.MaxValue</c> = the whole day.
        /// </summary>
        public void PlayDay(int day, long untilMs)
        {
            DayIndex = day;
            long dayStart = Start + day * Day;
            for (int i = 0; i < Time.SessionHours.Length; i++)
            {
                double jitter = (_bot.NextDouble() - 0.5) * 0.5; // ±15 min
                long start = dayStart + (long)((Time.SessionHours[i] + jitter) * Hour);
                if (start < Now) start = Now + Minute;
                if (untilMs != long.MaxValue && start + (long)(Time.MinutesPerSession * Minute) + SessionEndMarginMs > untilMs) return;
                Now = start;
                _minutesLeft = Time.MinutesPerSession;
                Session(i == 0);
            }
            if (untilMs != long.MaxValue && dayStart + Day - Minute > untilMs) return;
            Now = Math.Max(Now, dayStart + Day - Minute);
            E.Settle(S, Now);
        }

        /// <summary>Safety margin after a session's planned end (a run may overrun the session budget a little).</summary>
        public const long SessionEndMarginMs = 30 * Minute;

        /// <summary>
        /// Invasion occurrences still running at this time get no attempts: the server's dev tools end a simulation at the
        /// real "now", where the server's own invasion instances take over. Default: no limit (the simulator).
        /// </summary>
        public long InvasionsEndingBefore = long.MaxValue;

        /// <summary>
        /// Ends a simulation at <paramref name="nowMs"/> (≥ <see cref="Now"/>): applies the outcomes of invasions that ended
        /// since the last session (what the server's resolve job does) and settles the state.
        /// </summary>
        public void Finish(long nowMs)
        {
            if (nowMs > Now) Now = nowMs;
            ResolveInvasionOutcomes();
            E.Settle(S, Now);
        }

        private void Session(bool firstOfDay)
        {
            UseTime(Timing.SessionOverhead);
            RefreshPower();
            if (firstOfDay) { Purchases(); UseTime(Timing.Claims); }
            Claims();
            ResolveInvasionOutcomes();
            City();
            Lab();
            Gacha();
            UpgradeAll();
            RefreshPower();

            // Daily-quest minimums first (arena 2 fights, invasion attempts during events), then runs.
            Arena(Math.Min(2, Time.ArenaFightsPerDay));
            Invasions();
            Burrow();
            Runs();
            Arena(Time.ArenaFightsPerDay);

            Claims();
            Gacha();
            UpgradeAll();
            City();
            RefreshPower();
        }

        // ---- purchases (real money, dev receipts) ----------------------------------------------------------

        private bool Buy(string productId)
        {
            var p = C.Product(productId);
            if (p == null || ShopSystem.PurchaseBlockReason(S, C, p, Now) != null) return false;
            if (!Ok(new PurchaseStart { ProductId = productId })) return false;
            var r = Exec(new PurchaseComplete { ProductId = productId, ReceiptId = "dev:" + S.PlayerId + ":" + Commands });
            if (!r.Ok) return false;
            SpentCents += p.PriceUsdCents;
            return true;
        }

        private void Purchases()
        {
            if (!Spend.IsSpender) return;
            if (Spend.BuysNoAds && !NoAds) Buy("perm_no_ads");
            if (Spend.BuysDailyDiamonds) Buy("perm_daily_diamonds");
            if (Spend.BuysMonthlyPass && !ShopSystem.HasMonthlyPass(S, C, Now)) Buy(C.Shop.MonthlyPassProductId);
            if (Spend.BuysSeasonPass && Features.IsUnlocked(S, C, Features.SeasonPass) && !ShopSystem.HasPremiumSeason(S, C, Now))
                Buy(C.SeasonPass.PremiumProductId);
            if (Spend.MonthlyBudgetCents <= 0) return;

            // Whale: a monthly budget, spent evenly over the month (offers first, then the biggest Diamond packs that fit).
            int month = DayIndex / 30;
            if (month != _month) { _month = month; _monthBudgetUsed = 0; }
            long allowed = Spend.MonthlyBudgetCents * (DayIndex % 30 + 1) / 30 - _monthBudgetUsed;
            bool TryBuy(ProductDef p)
            {
                if (p.PriceUsdCents <= 0 || p.PriceUsdCents > allowed) return false;
                if (!Buy(p.Id)) return false;
                allowed -= p.PriceUsdCents; _monthBudgetUsed += p.PriceUsdCents;
                return true;
            }
            var avail = ShopSystem.AvailableProducts(S, C, Now);
            foreach (var kind in new[] { ProductKind.StarterPack, ProductKind.Fund, ProductKind.FlashOffer, ProductKind.GiftPack, ProductKind.RollingOffer })
                foreach (var p in avail.Where(x => x.Kind == kind).OrderBy(x => x.PriceUsdCents).ThenBy(x => x.Id, StringComparer.Ordinal))
                {
                    if (kind == ProductKind.GiftPack && ShopSystem.DailyGrantActive(S, C, p.Id, Now)) continue;
                    TryBuy(p);
                }
            var packs = C.Products.Values.Where(p => p.Kind == ProductKind.DiamondPack).OrderByDescending(p => p.PriceUsdCents).ToList();
            foreach (var p in packs)
                while (p.PriceUsdCents <= allowed && TryBuy(p)) { }
        }

        // ---- claims ---------------------------------------------------------------------------------------------

        private void Claims()
        {
            if (QuestSystem.CanClaimLogin(S, C, Now)) Exec(new LoginClaim());
            for (int d = 1; d <= C.Retention.NewPlayerDays.Count; d++)
                if (!S.Quests.NewPlayerDaysClaimed.Contains(d) && QuestSystem.NewPlayerEventOpen(S, C, Now) && QuestSystem.NewPlayerDay(S, C, Now) >= d)
                    Exec(new NewPlayerClaim { Day = d });
            foreach (var f in OnboardingSystem.PendingCelebrations(S, C)) Exec(new FeatureCelebrate { FeatureId = f });
            foreach (var t in C.TutorialSteps.Values.OrderBy(t => t.Id, StringComparer.Ordinal))
                if (!OnboardingSystem.IsStepDone(S, t.Id)) Exec(new TutorialAdvance { Step = t.Id });
            if (C.ShopItem("daily_free_gift") != null) Exec(new ShopBuyItem { ItemId = "daily_free_gift" });
            foreach (var p in C.Products.Values.Where(p => p.DailyRewards.Count > 0).OrderBy(p => p.Id, StringComparer.Ordinal))
                if (ShopSystem.DailyGrantActive(S, C, p.Id, Now) && ShopSystem.CanClaimDaily(S, C, p.Id, Now)) Exec(new ShopClaimDaily { ProductId = p.Id });
            foreach (var chain in C.RollingChains.Values)
            {
                var step = ShopSystem.CurrentRollingStep(S, C, chain.Id);
                if (step != null && C.Product(step)?.PriceUsdCents == 0) Exec(new ShopClaimFree { ProductId = step });
            }
            // Direct-reward ads (Diamond snack, Token snack).
            foreach (var ad in new[] { "daily_diamonds_ad", "shop_tokens_ad" })
                while (C.AdPlacement(ad) != null && WillWatchAd(ad))
                {
                    if (!Ok(new AdWatched { PlacementId = ad, AdToken = AdToken })) break;
                    UseTime(AdCost);
                }
            if (S.Mail.Inbox.Any(MailSystem.HasUnclaimed)) Exec(new MailClaimAll());
            if (C.Quests.Values.Any(q => QuestSystem.CanClaim(S, C, q, Now))) Exec(new QuestClaimAll());
            foreach (var chest in C.PointChests) Exec(new QuestClaimChest { Track = chest.Track, Points = chest.Points });
            if (Unlocked(Features.SeasonPass)) Exec(new SeasonPassClaimAll());
            foreach (var fid in S.Shop.FundsOwned.OrderBy(x => x, StringComparer.Ordinal).ToList())
            {
                var fund = C.Fund(fid);
                for (int i = 0; i < fund.Steps.Count; i++)
                    if (!S.Shop.FundClaims.Contains(fid + ":" + i) && ShopSystem.FundProgress(S, C, fund) >= fund.Steps[i].Target)
                        Exec(new FundClaim { FundId = fid, Step = i });
            }
            // Arena shop: Awaken Cores first (weekly limit); spare medals above two weeks of cores go to the other items.
            if (Unlocked(Features.Arena))
            {
                var core = C.ShopItem("arena_awaken_core");
                if (core != null) while (S.Wallet.CanAfford(core.Price) && Ok(new ShopBuyItem { ItemId = core.Id })) { }
                long reserve = core == null ? 0 : 2 * core.Limit * core.Price[0].Amount;
                foreach (var item in C.ShopItems.Values.Where(i => i.Section == ContentDb.ArenaShopSection && i.Id != "arena_awaken_core").OrderBy(i => i.Id, StringComparer.Ordinal))
                    while (Bal(Currencies.ArenaMedals) - item.Price[0].Amount >= reserve && Ok(new ShopBuyItem { ItemId = item.Id })) { }
            }
            if (Unlocked(Features.Guilds) && !GuildSystem.InGuild(S)) Exec(new GuildSetMembership { GuildId = GuildId, Role = "member" });
            if (GuildSystem.InGuild(S))
                foreach (var item in C.ShopItems.Values.Where(i => i.Section == GuildSystem.ShopSection).OrderByDescending(i => i.Price[0].Amount))
                    while (S.Wallet.CanAfford(item.Price) && Ok(new GuildShopBuy { ItemId = item.Id })) { }
        }
    }

    /// <summary>One played run (simulator run-length report).</summary>
    public sealed class RunRecord
    {
        public int Day, Level, Dice, Rolls, Drafts, LevelUps, Battles, TileLoot, LootOffers, XpTotal, Minigames;
        /// <summary>Skills gained (new skills, any source), skill level-ups, picks by source, shop purchases by kind, minibosses beaten.</summary>
        public int SkillsGained, SkillLevels, DraftPicks, MythicPicks, ShopSkills, ShopPerks, ShopHeals, Minibosses;
        public long CoinsEarned, CoinsSpent;
        public RunTimeTally Tally = new RunTimeTally();
        /// <summary>Skills and perks the run added to the build: every new skill or skill level, every stat perk bought.</summary>
        public int BuildGains => SkillsGained + SkillLevels + ShopPerks;
        public RunMode Mode;
        /// <summary>The run's signature (signatures pass), battle damage dealt / taken over the run, battle rounds, Supernovas.</summary>
        public string Signature;
        public long DamageDealt, DamageTaken;
        public int BattleRounds, Supernovas;
        /// <summary>Battle damage by the worm and by its sidekicks (aliens + drones), and by alien species / "(drones)" (aliens pass).</summary>
        public long WormDamage, SidekickDamage;
        public Dictionary<string, long> AlienDamage = new Dictionary<string, long>();
        public bool Won, BossReached;
        public long PeakCoins;
        public double Minutes;
        public List<Reward> Granted = new List<Reward>();
    }
}
