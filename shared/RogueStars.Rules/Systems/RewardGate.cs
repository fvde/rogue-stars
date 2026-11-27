using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        /// <summary>Rewards waiting for their use to unlock (<see cref="RogueStars.Rules.Systems.RewardGate"/>).</summary>
        public RogueStars.Rules.Systems.RewardStashState Stash = new RogueStars.Rules.Systems.RewardStashState();
    }
}

namespace RogueStars.Rules.Systems
{
    [Serializable]
    public sealed class RewardStashState
    {
        /// <summary>Currency → amount kept until the currency has a use.</summary>
        public Dictionary<string, long> Currencies = new Dictionary<string, long>();
        /// <summary>Item rewards (parts, runes, blueprints, aliens, hero shards, mounts) kept until their feature unlocks.</summary>
        public List<Reward> Items = new List<Reward>();
        /// <summary>Currency id or item kind → amount dropped because the stash was full (free sources only).</summary>
        public Dictionary<string, long> Dropped = new Dictionary<string, long>();
        /// <summary>Stashes released by a feature unlock (one entry per feature), shown by its unlock moment.</summary>
        public List<StashRelease> Releases = new List<StashRelease>();
    }

    [Serializable]
    public sealed class StashRelease
    {
        public string FeatureId;
        public long AtMs;
        public List<Reward> Rewards = new List<Reward>();
    }

    /// <summary>
    /// Rewards follow use (GDD §13): every grant passes <see cref="Admit"/> (called by <see cref="RewardRegistry.Grant"/>).
    /// A reward whose currency / item kind has no use yet (<see cref="ContentDb.RewardUses"/>) is not granted: it waits in
    /// the stash (free sources up to <see cref="RewardUseDef.StashCap"/>, the rest is dropped; paid and explicit sources in
    /// full) and is released through the owning granter the moment the use unlocks — by the onboarding settler right after
    /// recording the feature (the unlock moment shows it) or by <see cref="ReleaseReady"/> before every command (uses that
    /// are not features, e.g. owning an Uncommon part for Gear Cores).
    /// Events: "reward_stashed"(kind, id, kept), "stash_released"(kind, id, amount) followed by the usual "reward".
    /// </summary>
    public static class RewardGate
    {
        /// <summary>True when the player can use the reward now (unmapped rewards always pass).</summary>
        public static bool HasUse(GameState s, ContentDb c, Reward r, long nowMs = 0)
        {
            if (r == null) return false;
            return Met(s, c, c.RewardUse(r), nowMs);
        }

        /// <summary>True when the player can spend <paramref name="currency"/> now (unmapped currencies always pass).</summary>
        public static bool CurrencyHasUse(GameState s, ContentDb c, string currency, long nowMs = 0)
        {
            return Met(s, c, c.CurrencyUse(currency), nowMs);
        }

        /// <summary>Unmapped = always; the dev unlock-all flag gives everything a use.</summary>
        private static bool Met(GameState s, ContentDb c, RewardUseDef use, long nowMs) =>
            use == null || s.Flags.Contains(Features.DevUnlockAllFlag) || Requirements.IsMet(s, c, use.UsedWhen, nowMs);

        /// <summary>The rewards of a list the player can use now (reward previews, loot tables, shop contents).</summary>
        public static List<Reward> Usable(GameState s, ContentDb c, IEnumerable<Reward> rewards, long nowMs = 0)
        {
            var list = new List<Reward>();
            if (rewards != null) foreach (var r in rewards) if (HasUse(s, c, r, nowMs)) list.Add(r);
            return list;
        }

        /// <summary>True when at least one reward of the list is usable now (empty list: false).</summary>
        public static bool AnyUsable(GameState s, ContentDb c, IEnumerable<Reward> rewards, long nowMs = 0)
        {
            if (rewards != null) foreach (var r in rewards) if (HasUse(s, c, r, nowMs)) return true;
            return false;
        }

        /// <summary>
        /// Grant-time gate: true = grant now; false = the reward went to the stash (or was dropped because it is full).
        /// </summary>
        public static bool Admit(CommandContext ctx, Reward r)
        {
            var s = ctx.State; var c = ctx.Content;
            if (ctx.IgnoreRewardGate) return true;
            var use = c.RewardUse(r);
            if (Met(s, c, use, ctx.NowMs)) return true;
            bool keepAll = ctx.Command != null && c.GateConfig.KeepAllCommands.Contains(ctx.Command.Type);
            long kept = r.Kind == RewardKinds.Currency ? StashCurrency(s, use, r, keepAll) : StashItem(s, use, r, keepAll);
            ctx.Emit("reward_stashed", r.Kind, r.Id, kept.ToString());
            return false;
        }

        private static long StashCurrency(GameState s, RewardUseDef use, Reward r, bool keepAll)
        {
            var st = s.Stash;
            long have = st.Currencies.TryGetValue(r.Id, out var h) ? h : 0;
            long kept = keepAll || use.StashCap < 0 ? r.Amount : Math.Max(0, Math.Min(r.Amount, use.StashCap - have));
            if (kept > 0) st.Currencies[r.Id] = have + kept;
            if (kept < r.Amount) AddDropped(st, r.Id, r.Amount - kept);
            return kept;
        }

        /// <summary>Item stash: up to StashCap rewards of the kind from free sources; a better rarity replaces the worst kept.</summary>
        private static long StashItem(GameState s, RewardUseDef use, Reward r, bool keepAll)
        {
            var st = s.Stash;
            var copy = new Reward(r.Kind, r.Id, r.Amount, r.Rarity);
            int count = 0, worst = -1;
            for (int i = 0; i < st.Items.Count; i++)
            {
                if (st.Items[i].Kind != r.Kind) continue;
                count++;
                if (worst < 0 || st.Items[i].Rarity < st.Items[worst].Rarity) worst = i;
            }
            if (keepAll || use.StashCap < 0 || count < use.StashCap) { st.Items.Add(copy); return r.Amount; }
            if (worst >= 0 && st.Items[worst].Rarity < r.Rarity)
            {
                AddDropped(st, r.Kind, st.Items[worst].Amount);
                st.Items.RemoveAt(worst);
                st.Items.Add(copy);
                return r.Amount;
            }
            AddDropped(st, r.Kind, r.Amount);
            return 0;
        }

        private static void AddDropped(RewardStashState st, string key, long amount) =>
            st.Dropped[key] = (st.Dropped.TryGetValue(key, out var d) ? d : 0) + amount;

        /// <summary>Settler: releases every stashed reward whose use has unlocked (non-feature uses; features release at unlock).</summary>
        public static void ReleaseReady(CommandContext ctx)
        {
            DropRemovedCurrencies(ctx.State, ctx.Content);
            Release(ctx, null);
        }

        /// <summary>
        /// Currencies removed from the content (pre-release, no migration; e.g. Advancement Plates): a wallet balance or stashed
        /// amount whose currency has no <see cref="ContentDb.RewardUses"/> entry (every content currency has one) is dropped.
        /// </summary>
        public static void DropRemovedCurrencies(GameState s, ContentDb c)
        {
            List<string> gone = null;
            foreach (var k in s.Wallet.Balances.Keys) if (c.CurrencyUse(k) == null) (gone ??= new List<string>()).Add(k);
            foreach (var k in s.Stash.Currencies.Keys) if (c.CurrencyUse(k) == null) (gone ??= new List<string>()).Add(k);
            if (gone == null) return;
            foreach (var k in gone) { s.Wallet.Balances.Remove(k); s.Stash.Currencies.Remove(k); }
        }

        /// <summary>
        /// Grants every stashed reward that has a use now through its owner's granter (currencies in ordinal order, then
        /// items in stash order) and returns them. With a <paramref name="featureId"/> the release is recorded for that
        /// feature's unlock moment.
        /// </summary>
        public static List<Reward> Release(CommandContext ctx, string featureId)
        {
            var s = ctx.State; var c = ctx.Content; var st = s.Stash;
            var released = new List<Reward>();
            if (st.Currencies.Count == 0 && st.Items.Count == 0) return released;
            if (st.Currencies.Count > 0)
            {
                var keys = new List<string>(st.Currencies.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var k in keys)
                {
                    if (!CurrencyHasUse(s, c, k, ctx.NowMs)) continue;
                    long amount = st.Currencies[k];
                    st.Currencies.Remove(k);
                    released.Add(Reward.Cur(k, amount));
                }
            }
            for (int i = 0; i < st.Items.Count;)
            {
                var r = st.Items[i];
                if (!HasUse(s, c, r, ctx.NowMs)) { i++; continue; }
                st.Items.RemoveAt(i);
                released.Add(r);
            }
            foreach (var r in released)
            {
                ctx.Emit("stash_released", r.Kind, r.Id, r.Amount.ToString());
                ctx.Grant(r);
            }
            if (featureId != null && released.Count > 0)
            {
                var rec = new StashRelease { FeatureId = featureId, AtMs = ctx.NowMs };
                foreach (var r in released) rec.Rewards.Add(new Reward(r.Kind, r.Id, r.Amount, r.Rarity));
                s.Stash.Releases.Add(rec);
            }
            return released;
        }

        /// <summary>What the unlock of <paramref name="featureId"/> released from the stash (empty when nothing).</summary>
        public static List<Reward> ReleasedAt(GameState s, string featureId)
        {
            foreach (var r in s.Stash.Releases) if (r.FeatureId == featureId) return r.Rewards;
            return new List<Reward>();
        }

        /// <summary>Stashed amount of a currency (0 when none).</summary>
        public static long Stashed(GameState s, string currency) => s.Stash.Currencies.TryGetValue(currency, out var v) ? v : 0;
    }
}
