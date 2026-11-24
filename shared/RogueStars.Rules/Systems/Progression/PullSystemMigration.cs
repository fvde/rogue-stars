using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Save migration of the pull system (docs/design/pull-system.md, "Old saves"), once per save (GachaState.Migrated):
    /// <list type="bullet">
    /// <item>Stars are kept: a hero / alien / blueprint at n★ stays at n★; a mount at s★ + awakening a becomes (s + a)★ (awakening
    /// folded into stars 6★–10★, the kit steps A3 / A5 are 8★ / 10★).</item>
    /// <item>Loose shards keep their progress: the shards toward the next star(s) are converted by the share of each step they
    /// cover on the old table, onto the same steps of the new table (rounded up). Beyond the old 10★ cost the last step's ratio
    /// applies.</item>
    /// <item>Pity: the Signals' counters (hidden before) become the visible counters as they are; a summon event Luck above the new
    /// pity (old max 300 / 200) is capped at 99, so the next pull is the guaranteed one.</item>
    /// </list>
    /// </summary>
    public static class PullSystemMigration
    {
        /// <summary>The save migration this build applies.</summary>
        public const int Version = 1;

        /// <summary>Old shard steps (index s = shards to reach star s; index 1 = unlock or the first copy).</summary>
        public static readonly long[] LegacyHeroSteps = { 0, 40, 30, 60, 100, 160, 250, 380, 550, 800, 1100 };
        /// <summary>Mounts: unlock 30, stars 2–5, then the five awakening levels as stars 6–10.</summary>
        public static readonly long[] LegacyMountSteps = { 0, 30, 20, 50, 100, 180, 100, 150, 200, 260, 330 };
        /// <summary>Aliens: one copy (1 shard) unlocked, then the old star costs 2 … 60.</summary>
        public static readonly long[] LegacyAlienSteps = { 0, 1, 2, 4, 6, 10, 15, 22, 30, 42, 60 };
        /// <summary>Blueprints: one copy unlocked, then the old star costs 1 … 36.</summary>
        public static readonly long[] LegacyBlueprintSteps = { 0, 1, 1, 2, 3, 5, 8, 12, 18, 26, 36 };

        public static void Register(GameEngine e)
        {
            e.RegisterNewPlayer(ctx => ctx.State.Gacha.Migrated = Version); // a new save has nothing to migrate
            e.RegisterSettler(Migrate);
        }

        /// <summary>
        /// New wallet shards for an item at <paramref name="stars"/> (0 = not owned) holding <paramref name="oldShards"/> on the old
        /// table: the old position (cost of the stars + loose shards) mapped step by step onto the new table, minus the new cost of the
        /// stars. Never negative; rounded up.
        /// </summary>
        public static long ConvertShards(long[] oldSteps, long[] newSteps, int stars, long oldShards)
        {
            if (oldShards <= 0) return 0;
            int max = Math.Min(oldSteps.Length, newSteps.Length) - 1;
            stars = Math.Max(0, Math.Min(max, stars));
            double left = oldShards, mapped = 0;
            int k = stars;
            while (left > 0)
            {
                int step = Math.Min(k + 1, max); // past max: the last step's ratio
                double oldCost = Math.Max(1, oldSteps[step]);
                double take = k + 1 > max ? left : Math.Min(left, oldCost);
                mapped += take * newSteps[step] / oldCost;
                left -= take;
                k++;
            }
            return (long)Math.Ceiling(mapped - 1e-9);
        }

        public static void Migrate(CommandContext ctx)
        {
            var s = ctx.State; var c = ctx.Content;
            if (s.Gacha.Migrated >= Version) return;
            s.Gacha.Migrated = Version;
            var steps = c.ShardStars.StepCosts;

            foreach (var id in Sorted(c.HeroConfig.Defs.Keys))
            {
                var h = HeroSystem.Get(s, id);
                Convert(s, Currencies.HeroShards(id), LegacyHeroSteps, steps, h != null ? h.Stars : 0);
            }
            foreach (var id in Sorted(c.MountConfig.Defs.Keys))
            {
                var m = MountSystem.Get(s, id);
                if (m != null && m.Awaken > 0)
                {
                    m.Stars = Math.Min(c.ShardStars.MaxStars, m.Stars + m.Awaken);
                    m.Awaken = 0;
                }
                Convert(s, Currencies.MountShards(id), LegacyMountSteps, steps, m != null ? m.Stars : 0);
            }
            foreach (var id in Sorted(c.AlienTypes.Keys))
            {
                var a = AlienSystem.Owned(s, id);
                Convert(s, Currencies.AlienShards(id), LegacyAlienSteps, steps, a != null ? a.Stars : 0);
            }
            foreach (var id in Sorted(c.Blueprints.Keys))
            {
                var b = CitySystem.Owned(s, id);
                Convert(s, Currencies.BlueprintShards(id), LegacyBlueprintSteps, steps, b != null ? b.Stars : 0);
            }

            foreach (var kv in s.Gacha.Pity) Cap(kv.Value, c.GachaConfig.Types.TryGetValue(kv.Key, out var d) ? d : null);
            foreach (var kv in s.Summons.Luck) Cap(kv.Value, c.SummonType(kv.Key)?.Gacha);
            ctx.Emit("pull_system_migrated");
        }

        private static void Cap(GachaPityState p, GachaTypeDef def)
        {
            if (p == null || def == null) return;
            if (def.MythicPity > 0) p.SinceMythic = Math.Min(p.SinceMythic, def.MythicPity - 1);
            if (def.LegendaryPity > 0) p.SinceLegendary = Math.Min(p.SinceLegendary, def.LegendaryPity - 1);
        }

        private static void Convert(GameState s, string currency, long[] oldSteps, long[] newSteps, int stars)
        {
            long have = s.Wallet.Get(currency);
            if (have <= 0) return;
            long now = ConvertShards(oldSteps, newSteps, stars, have);
            if (now > have) s.Wallet.Add(currency, now - have);
            else if (now < have) s.Wallet.TrySpend(currency, have - now);
        }

        private static List<string> Sorted(IEnumerable<string> keys)
        {
            var l = new List<string>(keys);
            l.Sort(string.CompareOrdinal);
            return l;
        }
    }
}
