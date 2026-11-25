using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Gacha: three pull types (Blueprint, Part, Alien), each payable with its free token or Diamonds;
    /// 1- and 10-pulls (the visible 10-pull rule: at least one Epic+); flat published rarity rates; the fixed, visible pity
    /// per pull type ("Mythic x / 100", "Legendary x / 40", shared across banners, carried over; no soft or hidden pity —
    /// docs/design/pull-system.md); themed rotating mythic banners with a featured rate-up. A blueprint / alien result is one
    /// batch of ShardStars.BatchShards shards (the first unlocks the item). Results use ctx.Rng and are granted through ctx.Grant.
    /// </summary>
    public static class GachaSystem
    {
        public static void Register(GameEngine e)
        {
            e.Register<GachaPull>(HandlePull);
            e.Rewards.Register(RewardKinds.FreePull, GrantFreePull);
            PullSystemMigration.Register(e);
        }

        /// <summary>A free 10× pull of the type named by the reward id (Part / Alien / Blueprint), Amount pulls.</summary>
        private static void GrantFreePull(CommandContext ctx, Reward r)
        {
            if (!Enum.TryParse(r.Id, out GachaType type) || r.Amount <= 0) return;
            ctx.State.Gacha.FreeTenPulls[type] = ctx.State.Gacha.FreePulls(type) + (int)r.Amount;
        }

        // ---- pity (visible) ------------------------------------------------------------------------

        /// <summary>The visible counters of a pull type: "Mythic x / 100", "Legendary x / 40".</summary>
        public static GachaPityStatus Pity(GameState s, ContentDb c, GachaType type)
        {
            var def = c.GachaConfig.Types.TryGetValue(type, out var d) ? d : null;
            var p = s.Gacha.Pity.TryGetValue(type, out var x) ? x : null;
            return PityOf(def, p);
        }

        /// <summary>The visible counters of any pull type definition (Signals and summon events).</summary>
        public static GachaPityStatus PityOf(GachaTypeDef def, GachaPityState p) => new GachaPityStatus
        {
            Mythic = p?.SinceMythic ?? 0, MythicPity = def?.MythicPity ?? 0,
            Legendary = def != null && def.LegendaryPity > 0 ? p?.SinceLegendary ?? 0 : 0, LegendaryPity = def?.LegendaryPity ?? 0,
        };

        // ---- banners -------------------------------------------------------------------------------

        private static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>The themed banner active now for a pull type (scheduled live-ops banners win over the rotation), or null.</summary>
        public static ActiveBannerInfo ActiveBanner(ContentDb c, GachaType type, long nowMs)
        {
            foreach (var b in c.GachaConfig.ScheduledBanners)
                if (b.Type == type && b.ActiveAt(nowMs))
                    return new ActiveBannerInfo { Id = b.Id, Type = type, Theme = b.Theme, StartMs = b.StartMs, EndMs = b.EndMs };
            foreach (var r in c.GachaConfig.Rotations)
            {
                if (r.Type != type || r.Themes.Count == 0 || r.DurationMs <= 0) continue;
                long period = FloorDiv(nowMs - r.EpochMs, r.DurationMs);
                int idx = (int)(((period % r.Themes.Count) + r.Themes.Count) % r.Themes.Count);
                var theme = r.Themes[idx];
                long start = r.EpochMs + period * r.DurationMs;
                return new ActiveBannerInfo { Id = theme.Id + "@" + period, Type = type, Theme = theme, StartMs = start, EndMs = start + r.DurationMs };
            }
            return null;
        }

        /// <summary>Active banners of all three pull types (for the gacha screen).</summary>
        public static List<ActiveBannerInfo> ActiveBanners(ContentDb c, long nowMs)
        {
            var list = new List<ActiveBannerInfo>();
            foreach (GachaType t in new[] { GachaType.Blueprint, GachaType.Part, GachaType.Alien })
            {
                var b = ActiveBanner(c, t, nowMs);
                if (b != null) list.Add(b);
            }
            return list;
        }

        // ---- odds ----------------------------------------------------------------------------------

        /// <summary>
        /// Published odds per tier and item (the flat rates of every pull). With a banner, the featured item takes
        /// FeaturedShare of the Mythic tier. The fixed pity values and the 10-pull rule are published with them.
        /// </summary>
        public static GachaOdds GetOdds(ContentDb c, GachaType type, string bannerId, long nowMs)
        {
            var def = c.GachaConfig.Types[type];
            var banner = ResolveBanner(c, type, bannerId, nowMs, false);
            var odds = new GachaOdds
            {
                Type = type, BannerId = banner != null ? banner.Id : "", TenPullGuarantee = def.TenPullGuarantee, TenPullMinTier = def.TenPullMinTier,
                MythicPity = def.MythicPity, LegendaryPity = def.LegendaryPity, BatchShards = c.ShardStars.BatchShards,
            };
            double total = 0;
            foreach (var t in def.Tiers) total += Math.Max(0, t.Weight);
            foreach (var t in def.Tiers)
            {
                double tierPct = total > 0 ? Math.Max(0, t.Weight) / total * 100 : 0;
                var ot = new GachaOddsTier { Tier = t.Tier, Percent = tierPct };
                var feats = FeaturedIn(banner, t.Tier);
                double poolW = OffBannerWeight(t, banner);
                double share = feats.Count == 0 ? 0 : poolW <= 0 ? 1 : Clamp01(banner.Theme.FeaturedShare);
                ot.FeaturedPercent = share * 100;
                foreach (var f in feats)
                    ot.Items.Add(new GachaOddsItem { Kind = f.Kind, Id = f.Id, Amount = f.Amount, Rarity = f.Rarity, Featured = true, Percent = tierPct * share / feats.Count });
                foreach (var e in t.Pool)
                {
                    if (e.Weight <= 0 || poolW <= 0 || (feats.Count > 0 && banner.Theme.IsFeatured(e.Kind, e.Id))) continue;
                    ot.Items.Add(new GachaOddsItem { Kind = e.Kind, Id = e.Id, Amount = e.Amount, Rarity = e.Rarity, Percent = tierPct * (1 - share) * e.Weight / poolW });
                }
                odds.Tiers.Add(ot);
            }
            return odds;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>Featured items of a tier on this banner (only Legendary and Mythic tiers have them).</summary>
        private static List<BannerFeature> FeaturedIn(ActiveBannerInfo banner, Rarity tier) =>
            banner == null || tier < Rarity.Legendary ? new List<BannerFeature>() : banner.Theme.FeaturedIn(tier);

        /// <summary>Pool weight of the "off banner" items of a tier: everything but the featured items (when the tier has any).</summary>
        private static double OffBannerWeight(GachaTierDef t, ActiveBannerInfo banner)
        {
            bool featuredTier = FeaturedIn(banner, t.Tier).Count > 0;
            double w = 0;
            foreach (var e in t.Pool)
                if (e.Weight > 0 && !(featuredTier && banner.Theme.IsFeatured(e.Kind, e.Id))) w += e.Weight;
            return w;
        }

        private static ActiveBannerInfo ResolveBanner(ContentDb c, GachaType type, string bannerId, long nowMs, bool require)
        {
            if (string.IsNullOrEmpty(bannerId)) return null;
            var b = ActiveBanner(c, type, nowMs);
            bool ok = b != null && b.Id == bannerId;
            if (require) GameEngine.Require(ok, "banner is not active");
            return ok ? b : null;
        }

        // ---- pulling -------------------------------------------------------------------------------

        /// <summary>Feature gate of a pull type (GDD §16: Part Signal, Alien Signal, Blueprint Signal).</summary>
        public static string FeatureFor(GachaType type) =>
            type == GachaType.Blueprint ? Features.Blueprints : type == GachaType.Alien ? Features.AlienPulls : Features.PartPulls;

        private static CommandResult HandlePull(GachaPull cmd, CommandContext ctx)
        {
            var s = ctx.State; var c = ctx.Content;
            GameEngine.Require(c.GachaConfig.Types.ContainsKey(cmd.PullType), "unknown pull type");
            var def = c.GachaConfig.Types[cmd.PullType];
            Features.Require(s, c, FeatureFor(cmd.PullType));
            GameEngine.Require(cmd.Count == 1 || cmd.Count == 10, "pull count must be 1 or 10");
            var banner = ResolveBanner(c, cmd.PullType, cmd.BannerId, ctx.NowMs, true);

            // Every reward kind the pull could produce must have a granter (City owns blueprint/alien).
            foreach (var t in def.Tiers) foreach (var e in t.Pool)
                GameEngine.Require(ctx.Engine.Rewards.CanGrant(e.Kind), "pull type not available yet");
            if (banner != null) GameEngine.Require(ctx.Engine.Rewards.CanGrant(banner.Theme.FeaturedKind), "pull type not available yet");

            bool free = cmd.Payment == GachaPayment.Free;
            if (free)
            {
                GameEngine.Require(cmd.Count == 10, "the free pull is a 10× pull");
                GameEngine.Require(s.Gacha.FreePulls(cmd.PullType) > 0, "no free pull");
                s.Gacha.FreeTenPulls[cmd.PullType] = s.Gacha.FreePulls(cmd.PullType) - 1;
            }
            else if (cmd.Payment == GachaPayment.Ad)
            {
                GameEngine.Require(cmd.Count == 1, "ad pulls are single pulls");
                GameEngine.Require(!string.IsNullOrEmpty(def.AdPlacementId), "no ad pull for this type");
                ShopSystem.ConsumeAdView(ctx, def.AdPlacementId, cmd.AdToken);
            }
            else if (cmd.Payment == GachaPayment.Tokens)
                ProgressionUtil.Spend(s, def.TokenCurrency, cmd.Count == 10 ? def.TokenCost10 : def.TokenCost1);
            else
                ProgressionUtil.Spend(s, Currencies.Diamonds, cmd.Count == 10 ? def.DiamondCost10 : def.DiamondCost1);

            var pity = s.Gacha.For(cmd.PullType);
            var result = new GachaPullResult { Type = cmd.PullType, BannerId = banner != null ? banner.Id : "" };
            bool gotMin = false;
            for (int i = 0; i < cmd.Count; i++)
            {
                if (free && i == 0 && def.FreePullGuarantee != null)
                {
                    // the free 10×'s guaranteed result (the City's: the Launch Pod); its tier counts for the pity and the 10× rule
                    var g = def.FreePullGuarantee;
                    var gt = (Rarity)Math.Max(0, Math.Min(4, g.Rarity));
                    pity.TotalPulls++;
                    AdvancePity(pity, gt);
                    if (gt >= def.TenPullMinTier) gotMin = true;
                    result.Items.Add(new GachaPullItem { Kind = g.Kind, Id = g.Id, Amount = g.Amount, Rarity = g.Rarity, Tier = gt });
                    continue;
                }
                bool forceMin = def.TenPullGuarantee && cmd.Count == 10 && i == cmd.Count - 1 && !gotMin;
                var tier = RollTier(def, pity, ctx.Rng, forceMin);
                if (tier.Tier >= def.TenPullMinTier) gotMin = true;
                var item = PickItem(tier, banner, ctx.Rng);
                result.Items.Add(item);
            }
            // Grant after rolling so a granter can never influence later rolls of the same command. A blueprint / alien is one
            // batch of shards: its note says whether it unlocked the item ("new") or paid shards ("shards").
            foreach (var item in result.Items)
            {
                if (item.Kind == RewardKinds.Blueprint || item.Kind == RewardKinds.Alien)
                {
                    bool owned = item.Kind == RewardKinds.Blueprint ? CitySystem.Owned(s, item.Id) != null : AlienSystem.Owned(s, item.Id) != null;
                    item.Note = owned ? "shards" : "new";
                    item.Shards = item.Amount * c.ShardStars.BatchShards;
                }
                ctx.Grant(new Reward(item.Kind, item.Id, item.Amount, item.Rarity));
            }
            result.Pity = PityOf(def, pity);

            string key = cmd.PullType.ToString().ToLowerInvariant();
            s.Count("pulls_" + key, cmd.Count);
            s.Count("pulls_total", cmd.Count);
            foreach (var item in result.Items)
            {
                if (item.Tier == Rarity.Mythic) s.Count("pulls_mythic");
                if (item.Tier == Rarity.Legendary) s.Count("pulls_legendary");
            }
            ctx.Emit("gacha_pull", key, cmd.Count.ToString(), result.BannerId);
            return CommandResult.Success(result);
        }

        /// <summary>
        /// One tier roll with the fixed, visible pity (also used by summon events, whose Mythic pity is the Luck meter):
        /// the MythicPity-th pull without a Mythic is a Mythic; otherwise one roll on the flat published rates, and a result below
        /// Legendary becomes Legendary on the LegendaryPity-th pull without Legendary+ (the Mythic chance of that pull stays the flat
        /// rate), and a result below TenPullMinTier becomes that tier on a 10-pull's last pull without one (<paramref name="forceMin"/>).
        /// </summary>
        public static GachaTierDef RollTier(GachaTypeDef def, GachaPityState pity, Rng rng, bool forceMin)
        {
            pity.TotalPulls++;
            GachaTierDef chosen = null;
            if (def.MythicPity > 0 && pity.SinceMythic + 1 >= def.MythicPity)
            {
                chosen = TierOf(def, Rarity.Mythic);
                if (chosen != null) pity.MythicPityTriggers++;
            }
            if (chosen == null)
            {
                var weights = new double[def.Tiers.Count];
                for (int i = 0; i < weights.Length; i++) weights[i] = Math.Max(0, def.Tiers[i].Weight);
                int idx = rng.PickWeighted(weights);
                chosen = def.Tiers[idx < 0 ? def.Tiers.Count - 1 : idx];
                if (chosen.Tier < Rarity.Legendary && def.LegendaryPity > 0 && pity.SinceLegendary + 1 >= def.LegendaryPity)
                {
                    var leg = TierOf(def, Rarity.Legendary);
                    if (leg != null) { chosen = leg; pity.LegendaryPityTriggers++; }
                }
                if (forceMin && chosen.Tier < def.TenPullMinTier) chosen = TierOf(def, def.TenPullMinTier) ?? chosen;
            }

            AdvancePity(pity, chosen.Tier);
            return chosen;
        }

        /// <summary>Moves the visible counters past one result of the given tier.</summary>
        private static void AdvancePity(GachaPityState pity, Rarity tier)
        {
            if (tier == Rarity.Mythic) { pity.SinceMythic = 0; pity.SinceLegendary = 0; pity.Mythics++; }
            else
            {
                pity.SinceMythic++;
                if (tier == Rarity.Legendary) pity.SinceLegendary = 0; else pity.SinceLegendary++;
            }
        }

        private static GachaTierDef TierOf(GachaTypeDef def, Rarity r)
        {
            foreach (var t in def.Tiers) if (t.Tier == r) return t;
            return null;
        }

        /// <summary>
        /// One item of the rolled tier. On a banner with featured items in this tier (Legendary / Mythic): FeaturedShare (80%) a
        /// featured item (split evenly), otherwise another item of the tier ("off banner": the pool without the featured items).
        /// No carry-over: every pull rolls the 80/20 afresh.
        /// </summary>
        private static GachaPullItem PickItem(GachaTierDef tier, ActiveBannerInfo banner, Rng rng)
        {
            var feats = FeaturedIn(banner, tier.Tier);
            if (feats.Count > 0)
            {
                if (OffBannerWeight(tier, banner) <= 0 || rng.Chance(Clamp01(banner.Theme.FeaturedShare)))
                {
                    var f = feats.Count == 1 ? feats[0] : feats[(int)(rng.NextDouble() * feats.Count) % feats.Count];
                    return new GachaPullItem { Kind = f.Kind, Id = f.Id, Amount = f.Amount, Rarity = f.Rarity, Tier = tier.Tier, Featured = true };
                }
            }
            var weights = new double[tier.Pool.Count];
            for (int i = 0; i < weights.Length; i++)
                weights[i] = feats.Count > 0 && banner.Theme.IsFeatured(tier.Pool[i].Kind, tier.Pool[i].Id) ? 0 : tier.Pool[i].Weight;
            int idx = rng.PickWeighted(weights);
            if (idx < 0) throw new RuleException("empty gacha pool");
            var en = tier.Pool[idx];
            return new GachaPullItem { Kind = en.Kind, Id = en.Id, Amount = en.Amount, Rarity = en.Rarity, Tier = tier.Tier };
        }
    }
}
