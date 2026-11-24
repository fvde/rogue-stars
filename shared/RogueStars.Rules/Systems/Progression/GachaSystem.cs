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
            PullSystemMigration.Register(e);
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
                double share = banner != null && t.Tier == Rarity.Mythic ? Clamp01(banner.Theme.FeaturedShare) : 0;
                double poolW = 0;
                foreach (var e in t.Pool) poolW += Math.Max(0, e.Weight);
                if (share > 0)
                    ot.Items.Add(new GachaOddsItem { Kind = banner.Theme.FeaturedKind, Id = banner.Theme.FeaturedId, Amount = banner.Theme.FeaturedAmount, Rarity = banner.Theme.FeaturedRarity, Featured = true, Percent = tierPct * share });
                double rest = poolW > 0 ? 1 - share : 0;
                if (poolW <= 0 && share > 0) ot.Items[0].Percent = tierPct; // featured takes the whole tier
                foreach (var e in t.Pool)
                {
                    if (e.Weight <= 0) continue;
                    ot.Items.Add(new GachaOddsItem { Kind = e.Kind, Id = e.Id, Amount = e.Amount, Rarity = e.Rarity, Percent = tierPct * rest * e.Weight / poolW });
                }
                odds.Tiers.Add(ot);
            }
            return odds;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

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

            if (cmd.Payment == GachaPayment.Ad)
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

            if (chosen.Tier == Rarity.Mythic) { pity.SinceMythic = 0; pity.SinceLegendary = 0; pity.Mythics++; }
            else
            {
                pity.SinceMythic++;
                if (chosen.Tier == Rarity.Legendary) pity.SinceLegendary = 0; else pity.SinceLegendary++;
            }
            return chosen;
        }

        private static GachaTierDef TierOf(GachaTypeDef def, Rarity r)
        {
            foreach (var t in def.Tiers) if (t.Tier == r) return t;
            return null;
        }

        private static GachaPullItem PickItem(GachaTierDef tier, ActiveBannerInfo banner, Rng rng)
        {
            if (banner != null && tier.Tier == Rarity.Mythic)
            {
                double share = Clamp01(banner.Theme.FeaturedShare);
                bool emptyPool = true;
                foreach (var e in tier.Pool) if (e.Weight > 0) emptyPool = false;
                if (emptyPool || rng.Chance(share))
                {
                    var th = banner.Theme;
                    return new GachaPullItem { Kind = th.FeaturedKind, Id = th.FeaturedId, Amount = th.FeaturedAmount, Rarity = th.FeaturedRarity, Tier = tier.Tier, Featured = true };
                }
            }
            var weights = new double[tier.Pool.Count];
            for (int i = 0; i < weights.Length; i++) weights[i] = tier.Pool[i].Weight;
            int idx = rng.PickWeighted(weights);
            if (idx < 0) throw new RuleException("empty gacha pool");
            var en = tier.Pool[idx];
            return new GachaPullItem { Kind = en.Kind, Id = en.Id, Amount = en.Amount, Rarity = en.Rarity, Tier = tier.Tier };
        }
    }
}
