using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.App
{
    /// <summary>
    /// What the planet hub shows for a state (onboarding v2, GDD §16): bottom tabs, the right-hand column (shop, offers,
    /// pass, mail, more), the left-hand live-event column, visible currency chips and the guided action of each unlock.
    /// Pure queries so tests can check the day-1 hub and reachability without building UI. Locked features never appear
    /// (no greyed-out entries); only <see cref="Features.Next"/> is teased.
    /// </summary>
    public static class HubLayout
    {
        public sealed class Entry
        {
            public string Id;
            public string Label;
            public string Glyph;
            /// <summary>ScreenCatalog id opened by the entry (null = none, e.g. the Planet tab itself).</summary>
            public string ScreenId;
            /// <summary>Hub icon id (<c>tab_&lt;id&gt;</c> by default; the camp tab shows the crashed saucer, "camp").</summary>
            public string Icon;
            public Entry(string id, string label, string glyph, string screenId) { Id = id; Label = label; Glyph = glyph; ScreenId = screenId; Icon = "tab_" + id; }
        }

        public const string PlanetTab = "planet";
        /// <summary>The camp tab's icon id: the crashed saucer (art from <c>CityArt.Saucer</c>).</summary>
        public const string CampIcon = "camp";

        /// <summary>Bottom tabs (daily features) in display order; the Planet is always there, the rest as they unlock.</summary>
        public static List<Entry> Tabs(GameState s, ContentDb c)
        {
            var list = new List<Entry>();
            if (Features.IsUnlocked(s, c, Features.Parts)) list.Add(new Entry("worm", "Worm", "~", "worm"));
            // one entry for the camp (level 6-14: crash site + Launch Pod) and the City (from 15): same screen, other icon
            if (Features.IsUnlocked(s, c, Features.BiomassFarm)) list.Add(new Entry("city", "City", "#", "city"));
            else if (Features.IsUnlocked(s, c, Features.AlienSquad)) list.Add(new Entry("city", "Camp", "#", "city") { Icon = CampIcon });
            list.Add(new Entry(PlanetTab, "Planet", "O", null));
            if (Features.IsUnlocked(s, c, Features.PartPulls)) list.Add(new Entry("signals", "Signals", "))", "signals"));
            if (Features.IsUnlocked(s, c, Features.Arena)) list.Add(new Entry("arena", "Arena", "X", "arena"));
            return list;
        }

        /// <summary>Left column, above the live events: daily quests once unlocked.</summary>
        public static List<Entry> LeftColumn(GameState s, ContentDb c)
        {
            var list = new List<Entry>();
            if (Features.IsUnlocked(s, c, Features.Parts)) list.Add(new Entry("quests", "Daily Quests", "!", "quests"));
            // Summon events (hero / mount): from the Heroes unlock. Badge: SummonView.HubBadge (screens-pass merge: show it).
            if (Features.IsUnlocked(s, c, Features.Heroes)) list.Add(new Entry("summon", "Summon", "&", "summon"));
            return list;
        }

        /// <summary>At most this many live-event icons; more events are behind the Observatory icon.</summary>
        public const int MaxLiveIcons = 3;

        /// <summary>Right column: shop, a limited offer, the pass, mail and "More" (each only when it applies).</summary>
        public static List<Entry> RightColumn(GameState s, ContentDb c, long now)
        {
            var list = new List<Entry>();
            if (Features.IsUnlocked(s, c, Features.Shop))
            {
                list.Add(new Entry("shop", "Shop", "$", "shop"));
                if (LimitedOffer(s, c, now) != null) list.Add(new Entry("offer", "Offer", "%", "shop"));
            }
            if (Features.IsUnlocked(s, c, Features.SeasonPass)) list.Add(new Entry("pass", "Pass", "P", "shop"));
            // Mail from day 1, but not before the first run (the very first hub is PLAY only, even with the welcome mail waiting).
            if (Features.IsUnlocked(s, c, Features.Parts)) list.Add(new Entry("mail", "Mail", "@", "mail"));
            if (MoreScreens(s, c).Count > 0) list.Add(new Entry("more", "More", "+", null));
            return list;
        }

        /// <summary>Left column: live events once Invasions are unlocked (each opens the Observatory).</summary>
        public static List<ActiveLiveEvent> LiveEvents(GameState s, ContentDb c, long now) =>
            Features.IsUnlocked(s, c, Features.Invasions) ? LiveOpsSystem.ActiveEvents(s, c, now) : new List<ActiveLiveEvent>();

        /// <summary>A timed offer on sale now (flash / rolling / starter / gift pack), for the "Offer" icon.</summary>
        public static ProductDef LimitedOffer(GameState s, ContentDb c, long now)
        {
            ProductDef best = null;
            foreach (var p in ShopSystem.AvailableProducts(s, c, now))
            {
                if (p.Kind != ProductKind.FlashOffer && p.Kind != ProductKind.RollingOffer && p.Kind != ProductKind.StarterPack && p.Kind != ProductKind.GiftPack) continue;
                if (p.PriceUsdCents <= 0) continue;
                if (best == null || string.CompareOrdinal(p.Id, best.Id) < 0) best = p;
            }
            return best;
        }

        /// <summary>Unlocked catalog screens that have no tab or column entry (opened from "More").</summary>
        public static List<ScreenEntry> MoreScreens(GameState s, ContentDb c)
        {
            // parts: its inventory is on the Worm tab (the Runes button opens the screen), so day 1 stays PLAY + Worm.
            var direct = new HashSet<string> { "portals", "settings", "worm", "parts", "city", "signals", "arena", "quests", "shop", "mail", "summon" };
            var list = new List<ScreenEntry>();
            foreach (var e in ScreenCatalog.All)
            {
                if (direct.Contains(e.Id)) continue;
                if (e.Id == "observatory" && Features.IsUnlocked(s, c, Features.Invasions)) continue; // the left column
                if (e.FeatureId != null && !Features.IsUnlocked(s, c, e.FeatureId)) continue;
                list.Add(e);
            }
            return list;
        }

        /// <summary>
        /// Every catalog screen the hub can open for this state: PLAY (portals), tabs, both columns, "More" and the profile
        /// chip (settings, worm). Tests assert this covers every unlocked catalog screen.
        /// </summary>
        public static HashSet<string> Reachable(GameState s, ContentDb c, long now)
        {
            var set = new HashSet<string> { "portals", "settings" };
            if (Features.IsUnlocked(s, c, Features.Parts)) { set.Add("worm"); set.Add("parts"); } // parts: the Worm tab holds the same inventory
            foreach (var t in Tabs(s, c)) if (t.ScreenId != null) set.Add(t.ScreenId);
            foreach (var r in RightColumn(s, c, now)) if (r.ScreenId != null) set.Add(r.ScreenId);
            foreach (var l in LeftColumn(s, c)) if (l.ScreenId != null) set.Add(l.ScreenId);
            if (Features.IsUnlocked(s, c, Features.Invasions)) set.Add("observatory");
            foreach (var m in MoreScreens(s, c)) set.Add(m.Id);
            return set;
        }

        /// <summary>Display order of the wallet currencies (top bar first, then the details list).</summary>
        public static readonly string[] CurrencyOrder =
        {
            Currencies.Stamina, Currencies.Diamonds, Currencies.Biomass, Currencies.Data, Currencies.Grain, Currencies.Ore,
            Currencies.Fuel, Currencies.ArenaTickets, Currencies.ArenaMedals, Currencies.GearCores, Currencies.HeroXp,
            Currencies.PartTokens, Currencies.AlienTokens, Currencies.BlueprintTokens, Currencies.BurrowKeys, Currencies.InvasionAttempts,
            Currencies.GuildMarks, Currencies.AdvancementPlates, ProgressionCurrencies.PartAwakenCores,
            Currencies.HeroTickets, Currencies.MountTickets, Currencies.HeroSigils, Currencies.MountBolts,
        };

        /// <summary>
        /// Currencies the player has a use for yet, in display order (the bar shows the first few, "All" lists the rest).
        /// Which ones is the rules' reward-use mapping (<see cref="RewardGate.CurrencyHasUse"/>, ContentDb.RewardUses): the
        /// same rule that keeps unusable rewards out of the wallet.
        /// </summary>
        public static List<string> VisibleCurrencies(GameState s, ContentDb c)
        {
            var list = new List<string>();
            foreach (var id in CurrencyOrder) if (RewardGate.CurrencyHasUse(s, c, id)) list.Add(id);
            return list;
        }

        /// <summary>The unlock moment's single guided action: button text and the screen it opens.</summary>
        public static Entry UnlockAction(string featureId)
        {
            switch (featureId)
            {
                case Features.Parts: return new Entry(featureId, "EQUIP YOUR PARTS", null, "worm");
                case Features.BiomassFarm: return new Entry(featureId, "BUILD THE FARM", null, "city");
                case Features.WormLevel: return new Entry(featureId, "LEVEL UP THE WORM", null, "worm");
                case Features.Shop: return new Entry(featureId, "OPEN THE SHOP", null, "shop");
                case Features.Laboratory: return new Entry(featureId, "START FIRST RESEARCH", null, "laboratory");
                case Features.GrainFarm: return new Entry(featureId, "BUILD THE GRAIN FARM", null, "city");
                case Features.Aliens: return new Entry(featureId, "MEET YOUR ALIEN", null, "aliens");
                case Features.AlienSquad: return new Entry(featureId, "MEET YOUR SQUAD", null, "aliens");
                case Features.Heroes: return new Entry(featureId, "CHOOSE A HERO", null, "heroes");
                case Features.OreMine: return new Entry(featureId, "BUILD THE ORE MINE", null, "city");
                case Features.Runes: return new Entry(featureId, "SEE YOUR PARTS", null, "parts");
                case Features.Mounts: return new Entry(featureId, "SADDLE UP", null, "mounts");
                case Features.Funds: return new Entry(featureId, "SEE THE FUNDS", null, "shop");
                case Features.Breeding: return new Entry(featureId, "BUILD THE PEN", null, "city");
                case Features.SeasonPass: return new Entry(featureId, "SEE THE PASS", null, "shop");
                case Features.Arena: return new Entry(featureId, "ENTER THE ARENA", null, "arena");
                case Features.Overcharged: return new Entry(featureId, "PLAY OVERCHARGED", null, "portals");
                case Features.Guilds: return new Entry(featureId, "FIND A GUILD", null, "guild");
                case Features.Invasions: return new Entry(featureId, "DEFEND THE PLANET", null, "observatory");
                case Features.Burrow: return new Entry(featureId, "DIG IN", null, "portals");
                case Features.Forge: return new Entry(featureId, "OPEN THE FORGE", null, "parts");
                case Features.DarkSide: return new Entry(featureId, "CROSS OVER", null, "portals");
                case Features.RunShop: case Features.RunMiniboss: case Features.RunTwoDice: case Features.RunCampfire:
                case Features.RunSignatures: case Features.RunMinigames: case Features.AutoRoll: case Features.RunQuests:
                    return new Entry(featureId, "PLAY", null, "portals");
                case Features.PackageIce: case Features.PackageLightning: case Features.PackageCounter: case Features.PackageAlien:
                case Features.PackageTime:
                    return new Entry(featureId, "OPEN THE LAB", null, "laboratory");
                default: return new Entry(featureId, "LET'S GO", null, "portals");
            }
        }

        /// <summary>Glyph of a feature on the unlock moment.</summary>
        public static string FeatureGlyph(string featureId)
        {
            switch (featureId)
            {
                case Features.Parts: case Features.WormLevel: return "~";
                case Features.BiomassFarm: case Features.GrainFarm: case Features.OreMine: return "#";
                case Features.Shop: case Features.Funds: return "$";
                case Features.Laboratory: return "Lab";
                case Features.Aliens: case Features.AlienSquad: case Features.Breeding: return "@";
                case Features.Arena: return "X";
                default: return "*";
            }
        }

        /// <summary>The first pending celebration's whole unlock group (its not yet celebrated members), or null.</summary>
        public static List<string> PendingGroup(GameState s, ContentDb c)
        {
            var pending = OnboardingSystem.PendingCelebrations(s, c);
            if (pending.Count == 0) return null;
            string group = c.Feature(pending[0])?.GroupId ?? pending[0];
            var list = new List<string>();
            foreach (var id in pending) if ((c.Feature(id)?.GroupId ?? id) == group) list.Add(id);
            return list;
        }
    }
}
