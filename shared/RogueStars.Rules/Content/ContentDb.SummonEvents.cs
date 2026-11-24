using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    /// <summary>The two summon event types: heroes (6-day events) and mounts (3-day events).</summary>
    public enum SummonEventType { Hero, Mount }

    /// <summary>
    /// One task line of a summon event: a counter counted from the event start (the player's join) with rising targets;
    /// every reached target pays <see cref="Reward"/> once per event.
    /// </summary>
    [Serializable]
    public sealed class SummonTaskLineDef
    {
        public string Id;
        /// <summary>Display text with {0} = target ("Defeat {0} enemies").</summary>
        public string Text;
        /// <summary>state.Counter key (docs/design/counters.md).</summary>
        public string CounterKey;
        public List<long> Targets = new List<long>();
        public Reward Reward;

        public string Describe(long target) => string.Format(Text, target);
    }

    /// <summary>Pull-count milestone of one event (pulls made in this event).</summary>
    [Serializable]
    public sealed class SummonMilestoneDef
    {
        public int Pulls;
        /// <summary>Reward ids may be <see cref="SummonEventsConfig.Featured"/> (the event's featured hero / mount).</summary>
        public List<Reward> Rewards = new List<Reward>();
    }

    /// <summary>Exchange shop item: bought with the type's token currency, limited per event.</summary>
    [Serializable]
    public sealed class SummonExchangeItemDef
    {
        public string Id;
        public string Name;
        public long Price;
        public Reward Reward;
        public int EventLimit;
    }

    /// <summary>Event pass day: the free and the premium reward unlock on that day of the event (1-based, reset days).</summary>
    [Serializable]
    public sealed class SummonPassDayDef
    {
        public int Day;
        public List<Reward> Free = new List<Reward>();
        public List<Reward> Premium = new List<Reward>();
    }

    [Serializable]
    public sealed class SummonEventTypeDef
    {
        public SummonEventType Type;
        /// <summary>"Hero Summon" / "Mount Summon".</summary>
        public string Name;
        /// <summary>Feature gate (Heroes / Mounts): the events unlock with it.</summary>
        public string Feature;
        public string TicketCurrency;
        /// <summary>Exchange token currency, <see cref="TokensPerPull"/> per pull.</summary>
        public string TokenCurrency;
        public long TokensPerPull = 10;
        public long DiamondCost1, DiamondCost10;
        /// <summary>
        /// Tiers, pool and pity (reuses the Signals' roll: hidden Legendary soft/hard pity, 10-pull Epic+). The visible
        /// Luck meter is the Mythic pity: <see cref="GachaTypeDef.MythicHardPity"/> = Luck max, the Mythic tier is the
        /// featured copy, so Luck counts pulls since the last featured copy and the pull that fills it is the featured
        /// copy. Pool ids <see cref="SummonEventsConfig.Featured"/> = the event's featured id; an entry whose id is the
        /// featured id itself is skipped (the "other heroes" entries).
        /// </summary>
        public GachaTypeDef Gacha = new GachaTypeDef();
        /// <summary>Global rotation: occurrence k = [Epoch + k·Duration, +Duration), featured = Rotation[k mod n].</summary>
        public long EpochMs;
        public long DurationMs;
        public List<string> Rotation = new List<string>();
        /// <summary>
        /// The player's first event starts when the feature unlocks and runs to the end of the global occurrence that
        /// contains unlock + this (so it lasts at least this long).
        /// </summary>
        public long FirstEventMinMs;
        public List<SummonTaskLineDef> Tasks = new List<SummonTaskLineDef>();
        public List<SummonMilestoneDef> Milestones = new List<SummonMilestoneDef>();
        public List<SummonExchangeItemDef> Exchange = new List<SummonExchangeItemDef>();
        public List<SummonPassDayDef> Pass = new List<SummonPassDayDef>();
        /// <summary>Premium pass product (ProductKind.SummonPass) and the event packs (ProductKind.SummonPack), in display order.</summary>
        public string PassProductId;
        public List<string> PackProductIds = new List<string>();
        /// <summary>Rewarded-ad pack: one ticket a day (instant with No Ads / while ads are disabled).</summary>
        public string AdPlacementId;
        /// <summary>One-time migration gift for saves that had the feature before summon events existed.</summary>
        public List<Reward> MigrationGift = new List<Reward>();
    }

    [Serializable]
    public sealed class SummonEventsConfig
    {
        /// <summary>Placeholder reward id = the event's featured hero / mount.</summary>
        public const string Featured = "@featured";
        public Dictionary<SummonEventType, SummonEventTypeDef> Types = new Dictionary<SummonEventType, SummonEventTypeDef>();
    }

    public sealed partial class ContentDb
    {
        /// <summary>Hero and mount summon events (Meta): rotation, pulls, Luck, tasks, milestones, exchange, pass, packs.</summary>
        public readonly SummonEventsConfig SummonEvents = new SummonEventsConfig();

        public SummonEventTypeDef SummonType(SummonEventType t) => SummonEvents.Types.TryGetValue(t, out var d) ? d : null;

        partial void BuildSummonEvents()
        {
            const long D = MetaTime.Day;
            const string F = SummonEventsConfig.Featured;
            Reward Cur(string c, long n) => Reward.Cur(c, n);
            double[] heroWeights = { 55, 30, 11.85, 3, 0.15 };
            double[] mountWeights = { 55.6, 30, 11.2, 3, 0.2 };

            SummonTaskLineDef Task(string id, string text, string counter, Reward reward, params long[] targets)
            {
                var t = new SummonTaskLineDef { Id = id, Text = text, CounterKey = counter, Reward = reward };
                t.Targets.AddRange(targets);
                return t;
            }
            SummonExchangeItemDef X(string id, string name, long price, Reward r, int limit) =>
                new SummonExchangeItemDef { Id = id, Name = name, Price = price, Reward = r, EventLimit = limit };
            SummonMilestoneDef M(int pulls, params Reward[] r) { var m = new SummonMilestoneDef { Pulls = pulls }; m.Rewards.AddRange(r); return m; }
            void Pass(SummonEventTypeDef d, long[] free, long[] premium, List<Reward> premiumExtraLastDay)
            {
                for (int i = 0; i < free.Length; i++)
                {
                    var day = new SummonPassDayDef { Day = i + 1 };
                    day.Free.Add(Cur(d.TicketCurrency, free[i]));
                    day.Premium.Add(Cur(d.TicketCurrency, premium[i]));
                    if (i == free.Length - 1) day.Premium.AddRange(premiumExtraLastDay);
                    d.Pass.Add(day);
                }
            }
            ProductDef Product(string id, string name, ProductKind kind, SummonEventType type, int cents, int dailyLimit, params Reward[] contents)
            {
                var p = new ProductDef { Id = id, Name = name, Kind = kind, PriceUsdCents = cents, StoreProductId = "rs." + id, SummonEvent = type, DailyLimit = dailyLimit };
                p.Contents.AddRange(contents);
                Products[id] = p;
                return p;
            }
            void Ad(string id, string name, Reward r) => AdPlacements[id] = new AdPlacementDef { Id = id, Name = name, DailyCap = 1, Rewards = { r } };

            // ---- hero events: 6 days, every hero in turn (8 heroes → each one every 48 days) ---------------------------
            var hero = new SummonEventTypeDef
            {
                Type = SummonEventType.Hero, Name = "Hero Summon", Feature = Features.Heroes,
                TicketCurrency = Currencies.HeroTickets, TokenCurrency = Currencies.HeroSigils, TokensPerPull = 10,
                DiamondCost1 = 200, DiamondCost10 = 1800,
                EpochMs = LiveOps.EpochMs, DurationMs = 6 * D, FirstEventMinMs = 4 * D,
                PassProductId = "summon_pass_hero", AdPlacementId = "summon_ad_hero",
            };
            hero.Rotation.AddRange(new[]
            {
                "hero_star_knight", "hero_chef", "hero_void_pirate", "hero_glacier_monk",
                "hero_sparkplug", "hero_alchemist", "hero_beast_tamer", "hero_noodle_ninja",
            });
            hero.Gacha = SummonGacha("Hero Summon", hero.TicketCurrency, heroWeights, luckMax: 300);
            var heroOthers = GachaSortedKeys(HeroConfig.Defs);
            SummonTier(hero.Gacha, Rarity.Common, new GachaEntry(RewardKinds.Currency, Currencies.HeroXp, 400, 0, 1));
            SummonTier(hero.Gacha, Rarity.Rare, new GachaEntry(RewardKinds.HeroShards, F, 3, 0, 1));
            SummonTier(hero.Gacha, Rarity.Epic, new GachaEntry(RewardKinds.HeroShards, F, 8, 0, 1));
            SummonTier(hero.Gacha, Rarity.Legendary, new GachaEntry(RewardKinds.HeroShards, F, 20, 0, 1));
            // Half of the Legendary results are shards of another hero (spread evenly; the featured one is skipped).
            foreach (var id in heroOthers) SummonTier(hero.Gacha, Rarity.Legendary, new GachaEntry(RewardKinds.HeroShards, id, 15, 0, 1.0 / (heroOthers.Count - 1)));
            SummonTier(hero.Gacha, Rarity.Mythic, new GachaEntry(RewardKinds.Hero, F, 1, 0, 1));
            // Tasks (one ticket per reached target): an active F2P player reaches ~4 of 5 targets per line in 6 days
            // (simulator --counter-rates: ~120 enemies, ~3.5 won levels, ~5 mini-game medals, ~6 Alien / ~10 Part / ~3
            // Blueprint pulls a day), a casual player ~1–2, a binge player all.
            var ht = Cur(hero.TicketCurrency, 1);
            hero.Tasks.Add(Task("enemies", "Defeat {0} enemies", "run_enemies_defeated", ht, 50, 150, 250, 500, 1000));
            hero.Tasks.Add(Task("levels", "Win {0} levels", "runs_completed", ht, 2, 5, 10, 20, 35));
            hero.Tasks.Add(Task("minigames", "Win {0} mini-games", "run_minigames_won", ht, 2, 5, 10, 20, 35));
            hero.Tasks.Add(Task("alien_pulls", "Do {0} Alien pulls", "pulls_alien", ht, 5, 10, 20, 35, 60));
            hero.Tasks.Add(Task("part_pulls", "Do {0} Part pulls", "pulls_part", ht, 10, 20, 35, 60, 100));
            hero.Tasks.Add(Task("blueprint_pulls", "Do {0} Blueprint pulls", "pulls_blueprint", ht, 3, 6, 10, 15, 25));
            hero.Milestones.Add(M(10, Cur(Currencies.HeroXp, 2000)));
            hero.Milestones.Add(M(25, new Reward(RewardKinds.HeroShards, F, 20)));
            hero.Milestones.Add(M(50, Cur(hero.TicketCurrency, 3)));
            hero.Milestones.Add(M(100, new Reward(RewardKinds.HeroShards, F, 60)));
            hero.Milestones.Add(M(150, new Reward(RewardKinds.Hero, F, 1)));
            hero.Exchange.Add(X("hero_shards", "Featured shards", 60, new Reward(RewardKinds.HeroShards, F, 10), 3));
            hero.Exchange.Add(X("hero_ticket", "Hero Summon Ticket", 150, Cur(hero.TicketCurrency, 1), 3));
            hero.Exchange.Add(X("hero_xp", "Hero XP", 40, Cur(Currencies.HeroXp, 2000), 10));
            hero.Exchange.Add(X("hero_alien_tokens", "Alien Tokens", 80, Cur(Currencies.AlienTokens, 2), 3));
            hero.Exchange.Add(X("hero_diamonds", "Diamonds", 100, Cur(Currencies.Diamonds, 50), 5));
            Pass(hero, new long[] { 1, 1, 1, 2, 2, 3 }, new long[] { 5, 5, 5, 6, 6, 8 }, new List<Reward> { new Reward(RewardKinds.HeroShards, F, 40) });
            Product(hero.PassProductId, "Hero Summon Pass", ProductKind.SummonPass, SummonEventType.Hero, 999, 0);
            hero.PackProductIds.Add(Product("summon_pack_hero_s", "Hero Summon Pack", ProductKind.SummonPack, SummonEventType.Hero, 299, 3, Cur(hero.TicketCurrency, 5), Cur(Currencies.Diamonds, 300)).Id);
            hero.PackProductIds.Add(Product("summon_pack_hero_m", "Hero Summon Crate", ProductKind.SummonPack, SummonEventType.Hero, 799, 3, Cur(hero.TicketCurrency, 14), Cur(Currencies.Diamonds, 800)).Id);
            hero.PackProductIds.Add(Product("summon_pack_hero_l", "Hero Summon Vault", ProductKind.SummonPack, SummonEventType.Hero, 1999, 2, Cur(hero.TicketCurrency, 38), Cur(Currencies.Diamonds, 2000)).Id);
            Ad(hero.AdPlacementId, "Free Hero Summon Ticket", Cur(hero.TicketCurrency, 1));
            hero.MigrationGift.Add(Cur(hero.TicketCurrency, 10));
            SummonEvents.Types[hero.Type] = hero;

            // ---- mount events: 3 days, every mount in turn (6 mounts → each one every 18 days) ------------------------
            var mount = new SummonEventTypeDef
            {
                Type = SummonEventType.Mount, Name = "Mount Summon", Feature = Features.Mounts,
                TicketCurrency = Currencies.MountTickets, TokenCurrency = Currencies.MountBolts, TokensPerPull = 10,
                DiamondCost1 = 150, DiamondCost10 = 1350,
                EpochMs = LiveOps.EpochMs, DurationMs = 3 * D, FirstEventMinMs = 2 * D,
                PassProductId = "summon_pass_mount", AdPlacementId = "summon_ad_mount",
            };
            mount.Rotation.AddRange(new[]
            {
                "mount_scout_saucer", "mount_magma_barge", "mount_frost_frigate", "mount_toxic_hauler", "mount_star_lancer", "mount_hive_carrier",
            });
            mount.Gacha = SummonGacha("Mount Summon", mount.TicketCurrency, mountWeights, luckMax: 200);
            SummonTier(mount.Gacha, Rarity.Common, new GachaEntry(RewardKinds.Currency, Currencies.Fuel, 500, 0, 1));
            SummonTier(mount.Gacha, Rarity.Rare, new GachaEntry(RewardKinds.Currency, MountShardsOf(F), 1, 0, 1));
            SummonTier(mount.Gacha, Rarity.Epic, new GachaEntry(RewardKinds.Currency, MountShardsOf(F), 4, 0, 2));
            SummonTier(mount.Gacha, Rarity.Epic, new GachaEntry(RewardKinds.Currency, Currencies.MountCores, 5, 0, 1));
            SummonTier(mount.Gacha, Rarity.Legendary, new GachaEntry(RewardKinds.Currency, MountShardsOf(F), 10, 0, 1));
            SummonTier(mount.Gacha, Rarity.Legendary, new GachaEntry(RewardKinds.Currency, Currencies.MountCores, 10, 0, 1));
            SummonTier(mount.Gacha, Rarity.Mythic, new GachaEntry(RewardKinds.Mount, F, 1, 0, 1));
            // 3-day targets: an active F2P player reaches ~3 of 5 per line (mount events come twice as often).
            var mt = Cur(mount.TicketCurrency, 1);
            mount.Tasks.Add(Task("enemies", "Defeat {0} enemies", "run_enemies_defeated", mt, 50, 150, 300, 500, 800));
            mount.Tasks.Add(Task("levels", "Win {0} levels", "runs_completed", mt, 2, 4, 8, 15, 25));
            mount.Tasks.Add(Task("minigames", "Win {0} mini-games", "run_minigames_won", mt, 2, 5, 10, 20, 35));
            mount.Tasks.Add(Task("alien_pulls", "Do {0} Alien pulls", "pulls_alien", mt, 3, 8, 15, 25, 40));
            mount.Tasks.Add(Task("part_pulls", "Do {0} Part pulls", "pulls_part", mt, 5, 15, 25, 45, 70));
            mount.Tasks.Add(Task("blueprint_pulls", "Do {0} Blueprint pulls", "pulls_blueprint", mt, 2, 4, 8, 12, 20));
            mount.Milestones.Add(M(10, Cur(Currencies.Fuel, 2000)));
            mount.Milestones.Add(M(25, Cur(MountShardsOf(F), 10)));
            mount.Milestones.Add(M(50, Cur(mount.TicketCurrency, 3)));
            mount.Milestones.Add(M(80, Cur(Currencies.MountCores, 30)));
            mount.Milestones.Add(M(120, new Reward(RewardKinds.Mount, F, 1)));
            mount.Exchange.Add(X("mount_shards", "Featured shards", 80, Cur(MountShardsOf(F), 10), 2));
            mount.Exchange.Add(X("mount_ticket", "Mount Summon Ticket", 150, Cur(mount.TicketCurrency, 1), 3));
            mount.Exchange.Add(X("mount_cores", "Mount Cores", 100, Cur(Currencies.MountCores, 10), 2));
            mount.Exchange.Add(X("mount_fuel", "Fuel", 40, Cur(Currencies.Fuel, 1500), 10));
            mount.Exchange.Add(X("mount_bp_tokens", "Blueprint Tokens", 80, Cur(Currencies.BlueprintTokens, 2), 3));
            Pass(mount, new long[] { 1, 1, 2 }, new long[] { 4, 5, 7 }, new List<Reward> { Cur(MountShardsOf(F), 20) });
            Product(mount.PassProductId, "Mount Summon Pass", ProductKind.SummonPass, SummonEventType.Mount, 499, 0);
            mount.PackProductIds.Add(Product("summon_pack_mount_s", "Mount Summon Pack", ProductKind.SummonPack, SummonEventType.Mount, 199, 3, Cur(mount.TicketCurrency, 4), Cur(Currencies.Diamonds, 200)).Id);
            mount.PackProductIds.Add(Product("summon_pack_mount_m", "Mount Summon Crate", ProductKind.SummonPack, SummonEventType.Mount, 599, 3, Cur(mount.TicketCurrency, 12), Cur(Currencies.Diamonds, 600)).Id);
            mount.PackProductIds.Add(Product("summon_pack_mount_l", "Mount Summon Vault", ProductKind.SummonPack, SummonEventType.Mount, 1499, 2, Cur(mount.TicketCurrency, 32), Cur(Currencies.Diamonds, 1500)).Id);
            Ad(mount.AdPlacementId, "Free Mount Summon Ticket", Cur(mount.TicketCurrency, 1));
            mount.MigrationGift.Add(Cur(mount.TicketCurrency, 10));
            SummonEvents.Types[mount.Type] = mount;
        }

        /// <summary>Mount shard currency of a mount id, or of the featured placeholder (resolved by the summon system).</summary>
        public static string MountShardsOf(string mountId) => Currencies.MountShards(mountId);

        private static GachaTypeDef SummonGacha(string name, string ticket, double[] weights, int luckMax)
        {
            var g = new GachaTypeDef
            {
                Name = name, TokenCurrency = ticket, TokenCost1 = 1, TokenCost10 = 10,
                MythicHardPity = luckMax, LegendarySoftPityStart = 25, LegendarySoftPityStep = 2.5, LegendaryHardPity = 40,
                TenPullMinTier = Rarity.Epic,
            };
            for (int t = 0; t < weights.Length; t++) g.Tiers.Add(new GachaTierDef { Tier = (Rarity)t, Weight = weights[t] });
            return g;
        }

        private static void SummonTier(GachaTypeDef g, Rarity tier, GachaEntry e) => g.Tiers[(int)tier].Pool.Add(e);
    }
}
