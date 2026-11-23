using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.App
{
    /// <summary>Slot filter of the gear inventory (both arms are one filter).</summary>
    public enum GearSlotFilter { All, Head, Body, Tail, Legs, Arms }

    /// <summary>Sort order of the gear inventory.</summary>
    public enum GearSort { Best, Level, Slot }

    /// <summary>
    /// Pure queries behind the gear screens (Worm tab, parts inventory, part detail) and the hub's Worm badge: which parts an
    /// inventory filter shows, what the next level of a part costs and whether the player can pay it now. Rules only (no UI).
    /// </summary>
    public static class GearGuide
    {
        public static readonly GearSlotFilter[] SlotFilters = { GearSlotFilter.All, GearSlotFilter.Head, GearSlotFilter.Body, GearSlotFilter.Tail, GearSlotFilter.Legs, GearSlotFilter.Arms };
        public static readonly GearSort[] Sorts = { GearSort.Best, GearSort.Level, GearSort.Slot };

        public static string SlotFilterLabel(GearSlotFilter f) => f == GearSlotFilter.All ? "All" : f.ToString();
        public static string SortLabel(GearSort s) => s == GearSort.Best ? "Best first" : s == GearSort.Level ? "Level" : "Slot";

        /// <summary>The inventory filter that shows the parts of a body slot (both arm slots → Arms).</summary>
        public static GearSlotFilter FilterFor(PartSlot slot)
        {
            switch (slot)
            {
                case PartSlot.Head: return GearSlotFilter.Head;
                case PartSlot.Body: return GearSlotFilter.Body;
                case PartSlot.Tail: return GearSlotFilter.Tail;
                case PartSlot.Legs: return GearSlotFilter.Legs;
                default: return GearSlotFilter.Arms;
            }
        }

        public static bool Matches(PartDef def, GearSlotFilter f)
        {
            switch (f)
            {
                case GearSlotFilter.All: return true;
                case GearSlotFilter.Head: return def.Kind == PartKind.Head;
                case GearSlotFilter.Body: return def.Kind == PartKind.Body;
                case GearSlotFilter.Tail: return def.Kind == PartKind.Tail;
                case GearSlotFilter.Legs: return def.Kind == PartKind.Legs;
                default: return def.Kind == PartKind.Arm;
            }
        }

        /// <summary>Display name of a body slot ("Left arm").</summary>
        public static string SlotName(PartSlot slot) =>
            slot == PartSlot.LeftArm ? "Left arm" : slot == PartSlot.RightArm ? "Right arm" : slot.ToString();

        /// <summary>The slot a part is equipped in, or null.</summary>
        public static PartSlot? EquippedSlot(GameState s, string uid)
        {
            foreach (var kv in s.Parts.Equipped) if (kv.Value == uid) return kv.Key;
            return null;
        }

        /// <summary>Whether a part fits a body slot (an arm part fits both arms).</summary>
        public static bool Fits(PartDef def, PartSlot slot) =>
            def.Kind == PartKind.Arm ? slot == PartSlot.LeftArm || slot == PartSlot.RightArm : (int)def.Kind == (int)slot;

        /// <summary>
        /// Every owned part the filter lets through (<paramref name="rarity"/> -1 = any), sorted: Best = equipped first, then
        /// rarity and level; Level = level, then rarity; Slot = slot order (head … arms), then rarity and level. Ties by uid.
        /// </summary>
        public static List<PartItem> Inventory(GameState s, ContentDb c, GearSlotFilter slot, int rarity, GearSort sort)
        {
            var list = new List<PartItem>();
            foreach (var p in s.Parts.Items.Values)
            {
                if (!Matches(PartsSystem.Def(c, p), slot)) continue;
                if (rarity >= 0 && p.Rarity != rarity) continue;
                list.Add(p);
            }
            list.Sort((a, b) =>
            {
                if (sort == GearSort.Best)
                {
                    int ea = PartsSystem.IsEquipped(s, a.Uid) ? 0 : 1, eb = PartsSystem.IsEquipped(s, b.Uid) ? 0 : 1;
                    if (ea != eb) return ea.CompareTo(eb);
                }
                if (sort == GearSort.Slot)
                {
                    int ka = (int)PartsSystem.Def(c, a).Kind, kb = (int)PartsSystem.Def(c, b).Kind;
                    if (ka != kb) return ka.CompareTo(kb);
                }
                if (sort == GearSort.Level && a.Level != b.Level) return b.Level.CompareTo(a.Level);
                if (a.Rarity != b.Rarity) return b.Rarity.CompareTo(a.Rarity);
                if (a.Level != b.Level) return b.Level.CompareTo(a.Level);
                return string.CompareOrdinal(a.Uid, b.Uid);
            });
            return list;
        }

        /// <summary>Rarities the player owns parts of (highest first), for the rarity filter.</summary>
        public static List<int> OwnedRarities(GameState s)
        {
            var set = new SortedSet<int>();
            foreach (var p in s.Parts.Items.Values) set.Add(p.Rarity);
            var list = new List<int>(set);
            list.Reverse();
            return list;
        }

        /// <summary>Spare parts (not equipped) that fit a slot.</summary>
        public static int SpareFor(GameState s, ContentDb c, PartSlot slot)
        {
            int n = 0;
            foreach (var p in s.Parts.Items.Values)
                if (Fits(PartsSystem.Def(c, p), slot) && !PartsSystem.IsEquipped(s, p.Uid)) n++;
            return n;
        }

        // ---- level ups (Ore) -------------------------------------------------------------------------

        /// <summary>What the next level of a part costs (the rules' PartLevelUp prices); false at its level cap.</summary>
        public static bool NextLevelCost(ContentDb c, PartItem p, out long ore, out long cores)
        {
            ore = cores = 0;
            var cfg = c.PartsConfig;
            if (p.Level >= PartsSystem.LevelCap(c, p) || p.Level >= cfg.LevelOreCosts.Count) return false;
            ore = cfg.LevelOreCosts[p.Level];
            cores = cfg.LevelGearCoreCosts[p.Level];
            return true;
        }

        /// <summary>The part is below its cap and the wallet pays its next level now.</summary>
        public static bool CanLevelUp(GameState s, ContentDb c, PartItem p) =>
            p != null && NextLevelCost(c, p, out long ore, out long cores)
            && s.Wallet.CanAfford(Currencies.Ore, ore) && (cores <= 0 || s.Wallet.CanAfford(Currencies.GearCores, cores));

        /// <summary>Equipped parts whose next level the player can pay now (slot order).</summary>
        public static List<PartItem> UpgradableEquipped(GameState s, ContentDb c)
        {
            var list = new List<PartItem>();
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
            {
                if (!s.Parts.Equipped.TryGetValue(slot, out var uid)) continue;
                var p = PartsSystem.Get(s, uid);
                if (p != null && CanLevelUp(s, c, p) && !list.Contains(p)) list.Add(p);
            }
            return list;
        }

        /// <summary>Badge for the Worm tab: Parts unlocked and an equipped part can be levelled up with what the player has.</summary>
        public static bool HasAffordableUpgrade(GameState s, ContentDb c) =>
            Features.IsUnlocked(s, c, Features.Parts) && UpgradableEquipped(s, c).Count > 0;
    }

    /// <summary>
    /// What a currency is for and where it is spent, from the rules' reward-use mapping (ContentDb.RewardUses: the same
    /// rule that decides which currencies the player sees): the use text, and the screen of the first unlocked use.
    /// </summary>
    public static class CurrencyGuide
    {
        public sealed class Info
        {
            public string Id;
            public string Name;
            /// <summary>What it is spent on (RewardUseDef.Use).</summary>
            public string Use;
            /// <summary>Screen (ScreenCatalog id) where it is spent now; null = none unlocked.</summary>
            public string ScreenId;
            /// <summary>Caption of the Go button ("Go to Worm").</summary>
            public string GoLabel;
            /// <summary>An extra line about what the player can do right now (Ore: parts to level up), or null.</summary>
            public string Hint;
        }

        public static Info For(GameState s, ContentDb c, string currency)
        {
            var use = c.CurrencyUse(currency);
            var info = new Info { Id = currency, Name = Fmt.CurrencyName(currency, c), Use = use?.Use ?? "" };
            info.ScreenId = SpendScreen(s, c, currency, use);
            if (info.ScreenId != null)
            {
                var entry = ScreenCatalog.Find(info.ScreenId);
                info.GoLabel = "Go to " + (entry?.Title ?? Fmt.Humanize(info.ScreenId));
            }
            if (currency == Currencies.Ore && Features.IsUnlocked(s, c, Features.Parts))
            {
                int n = GearGuide.UpgradableEquipped(s, c).Count;
                info.Hint = n > 0 ? "You can level up " + n + " equipped part" + (n == 1 ? "" : "s") + " right now."
                    : s.Parts.Equipped.Count == 0 ? "Equip parts on the worm, then level them up with Ore." : null;
            }
            return info;
        }

        /// <summary>
        /// The screen of the first met requirement of the currency's use (a feature → the screen its unlock moment opens,
        /// <see cref="HubLayout.UnlockAction"/>; a part-rarity counter → the Worm's gear); uses without a requirement
        /// (stamina, Diamonds) → the portals (stamina refills) / the shop once it is unlocked. Null when nothing is unlocked or the screen is locked.
        /// </summary>
        public static string SpendScreen(GameState s, ContentDb c, string currency, RewardUseDef use = null)
        {
            use = use ?? c.CurrencyUse(currency);
            if (use == null) return null;
            string screen = null;
            if (use.UsedWhen == null || use.UsedWhen.Items.Count == 0)
                screen = currency == Currencies.Diamonds && Features.IsUnlocked(s, c, Features.Shop) ? "shop" : "portals";
            else
            {
                RequirementDef pick = null;
                foreach (var req in use.UsedWhen.Items)
                    if (Requirements.IsMet(s, c, req)) { pick = req; break; }
                // usable without a met requirement (the dev unlock-all flag): its first use
                if (pick == null && RewardGate.CurrencyHasUse(s, c, currency)) pick = use.UsedWhen.Items[0];
                if (pick != null) screen = pick.Kind == RequirementKind.Feature ? HubLayout.UnlockAction(pick.Key).ScreenId : "worm";
            }
            if (screen == null) return null;
            var entry = ScreenCatalog.Find(screen);
            if (entry == null || entry.FeatureId != null && !Features.IsUnlocked(s, c, entry.FeatureId)) return null;
            return screen;
        }
    }
}
