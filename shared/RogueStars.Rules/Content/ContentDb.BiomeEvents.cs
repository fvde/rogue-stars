using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    // Biome identity, part B (docs/design/biomes/data.py, owner-approved 2026-11-28): every campaign biome has 4 events and a
    // Rite (its 5th event, RunTuning.RiteWeight, first half of the run only), keyed by the biome id (CampaignBiomeDef.Id;
    // planned biomes are defined ahead and start drawing once their biome is in CampaignDef.Biomes). Plus the 13 new shared
    // events. Every option is one gain and at most one price, made of existing RunEffectKinds; the only new piece is the
    // Rite Offering (EventOption.Offering: give up an owned skill of one synergy). Art ids ("npc:" / "prop:") are recorded on
    // EventDef.Art; the client draws its placeholder until the file is imported.
    public sealed partial class ContentDb
    {
        /// <summary>Biome id → its 4 biome events (in order). The Rite is apart (<see cref="BiomeRites"/>).</summary>
        public readonly Dictionary<string, List<string>> BiomeEvents = new Dictionary<string, List<string>>();
        /// <summary>Biome id → its Rite event id.</summary>
        public readonly Dictionary<string, string> BiomeRites = new Dictionary<string, string>();

        // ---- builders ----------------------------------------------------------------------------------------------

        private static RunEffect BxStat(StatId s, double v) => RunStatFx(s, v);
        private static RunEffect BxLose(double pct) => RunFx(RunEffectKind.DamagePct, pct);
        private static RunEffect BxHeal(double pct) => RunFx(RunEffectKind.HealPct, pct);
        private static RunEffect BxCoins(double v) => RunFx(RunEffectKind.Coins, v);
        private static RunEffect BxShards(double v) => RunFx(RunEffectKind.Shards, v);
        private static RunEffect BxXp(double v) => RunFx(RunEffectKind.Xp, v);
        private static RunEffect BxWake(double v) => RunFx(RunEffectKind.Meter, v);
        private static RunEffect BxPace(double pct) => RunFx(RunEffectKind.AwakenRatePct, pct);
        private static RunEffect BxEnemies(double pct) => RunFx(RunEffectKind.StrengthenEnemies, pct);
        private static RunEffect BxElite(double bonus = 0) => RunFx(RunEffectKind.Battle, bonus, "elite");
        private static RunEffect BxFoe() => RunFx(RunEffectKind.Battle, 0, "enemy");
        private static RunEffect BxSkill(Rarity min) => RunSkillFx(min);
        private static RunEffect BxLoot() => RunFx(RunEffectKind.Loot, 1);
        private static RunEffect BxLoseSkill() => RunFx(RunEffectKind.LoseRandomSkill, 1);
        private static RunEffect BxTrain() => RunFx(RunEffectKind.UpgradeSkill, 1);
        private static RunEffect BxTrainRandom() => RunFx(RunEffectKind.UpgradeSkill, 1, RunSystem.RandomSkillParam);
        private static RunEffect BxBossAtk(double pct) => RunBossFx(RunSystem.BossAtkParam, pct);
        private static RunEffect BxBossHp(double pct) => RunBossFx(RunSystem.BossHpParam, pct);
        /// <summary>A biome chest: one level-scaled roll of the currency (RunTuning.BiomeChestRange), paid on clear, +Danger.</summary>
        private static RunEffect BxChest(string currency) => new RunEffect(RunEffectKind.PendingReward, 1, RunSystem.BiomeChestParam) { Reward = Reward.Cur(currency, 0) };

        /// <summary>A deterministic option (one outcome).</summary>
        private static EventOption BxDo(string text, string risk, string result, params RunEffect[] fx) => RunOpt(text, risk, RunOut(1, result, fx));

        /// <summary>A deterministic option paid with run coins.</summary>
        private static EventOption BxBuy(long coins, string text, string risk, string result, params RunEffect[] fx)
        {
            var o = BxDo(text, risk, result, fx);
            o.CoinCost = coins;
            return o;
        }

        /// <summary>A gamble: weighted outcomes (the first is the lucky one).</summary>
        private static EventOption BxBet(string text, string risk, params EventOutcome[] outcomes) => RunOpt(text, risk, outcomes);

        private static EventOption BxLeave() => RunOpt("Leave", "You move on.", RunNothing);

        /// <summary>A Rite Offering: give up one owned skill of the synergy (None = no synergy) for one effect on this level.</summary>
        private static EventOption BxOffer(SkillFamily family, string text, string risk, string result, RunEffect fx)
        {
            var o = BxDo(text, risk, result, fx);
            o.Offering = true;
            o.OfferFamily = family;
            return o;
        }

        /// <summary>The default (auto) option: Leave if there is one, else the last option without a coin / shard cost.</summary>
        private static int BxDefault(EventOption[] options)
        {
            for (int i = 0; i < options.Length; i++) if (options[i].Text == "Leave") return i;
            for (int i = options.Length - 1; i >= 0; i--) if (options[i].CoinCost == 0 && options[i].ShardCost == 0) return i;
            return 0;
        }

        private EventDef BxEvent(string biome, string id, string name, string text, string art, params EventOption[] options)
        {
            var e = new EventDef { Id = id, Name = name, Text = text, Art = art, Biome = biome, DefaultOption = BxDefault(options) };
            e.Options.AddRange(options);
            RunEvents[id] = e;
            if (!BiomeEvents.TryGetValue(biome, out var list)) BiomeEvents[biome] = list = new List<string>();
            list.Add(id);
            return e;
        }

        private void BxRite(string biome, string name, string text, string art, params EventOption[] options)
        {
            string id = "rite_" + biome;
            var e = new EventDef { Id = id, Name = name, Text = text, Art = art, Biome = biome, Rite = true, DefaultOption = BxDefault(options) };
            e.Options.AddRange(options);
            RunEvents[id] = e;
            BiomeRites[biome] = id;
        }

        private void BxShared(string id, string name, string text, string art, double weight, params EventOption[] options)
        {
            var e = new EventDef { Id = id, Name = name, Text = text, Art = art, DefaultOption = BxDefault(options) };
            e.Options.AddRange(options);
            RunEvents[id] = e;
            RandomRunEvents.Add(new WeightedId(id, weight));
        }

        private const string ChestNote = " chest (paid on clear, +Danger)";

        private void RunBuildBiomeEvents()
        {
            BuildSharedEvents();
            BuildBiomeEventsEmber();
            BuildBiomeEventsGlacier();
            BuildBiomeEventsStatic();
            BuildBiomeEventsVoid();
            BuildBiomeEventsChrono();
            BuildRites();
        }

        // ---- the 13 new shared events (SHARED_NEW) ---------------------------------------------------------------

        private void BuildSharedEvents()
        {
            BxShared("wandering_trader", "Wandering Trader", "A trader with a huge backpack waves you over.", "npc:shopkeeper", 2.5,
                BxBuy(60, "Buy a skill (60 coins)", "Gain a Common+ skill.", "A fine find.", BxSkill(Rarity.Common)),
                BxBuy(40, "Buy a map (40 coins)", "Boss awakening −10.", "A shortcut around the boss's scouts.", BxWake(-10)),
                BxLeave());
            BxShared("sparring_master", "Sparring Master", "An old master offers a quick lesson.", "npc:sparring_master", 2.5,
                BxBuy(60, "Learn to counter (60 coins)", "+10% Counter chance for the run.", "Block, then strike.", BxStat(StatId.CounterChance, 10)),
                BxBuy(60, "Learn to combo (60 coins)", "+10% Combo chance for the run.", "One hit becomes two.", BxStat(StatId.ComboChance, 10)),
                BxLeave());
            BxShared("lost_alien_pup", "Lost Alien Pup", "A lost alien pup follows you around.", "npc:alien_pup", 2,
                BxDo("Adopt it", "Gain a Legendary+ skill. −5% max HP for the run.", "It teaches you a trick or two.", BxSkill(Rarity.Legendary), BxStat(StatId.Hp, -5)),
                BxLeave());
            BxShared("comet_fragment", "Comet Fragment", "A burning comet fragment lands nearby.", "prop:comet_fragment", 2,
                BxDo("Absorb it (lose 10% HP)", "+15% Fire damage for the run.", "The fire is yours now.", BxLose(10), BxStat(StatId.FireDamage, 15)),
                BxLeave());
            BxShared("storm_front", "Storm Front", "A lightning storm rolls over the board.", "prop:storm_rod", 2,
                BxBet("Ride the storm", "50%: +15% Lightning damage for the run. 50%: lose 20% HP.",
                    RunOut(50, "The lightning answers you.", BxStat(StatId.LightningDamage, 15)), RunOut(50, "Struck!", BxLose(20))),
                BxDo("Wait it out", "Boss awakening +10.", "The storm passes. So does time.", BxWake(10)));
            BxShared("cold_snap", "Cold Snap", "The air turns ice cold.", "prop:frost_crystal", 2,
                BxDo("Embrace the cold", "+15% Ice damage. −5% Speed for the run.", "Your fangs frost over.", BxStat(StatId.IceDamage, 15), BxStat(StatId.Speed, -5)),
                BxBuy(30, "Light a fire (30 coins)", "Heal 15% HP.", "Warm again.", BxHeal(15)));
            BxShared("reactor_leak", "Reactor Leak", "A cracked reactor leaks glowing waste.", "prop:leaking_barrel", 2,
                BxDo("Soak it up", "+15% Radiation damage. −10% max HP for the run.", "You glow a little.", BxStat(StatId.PoisonDamage, 15), BxStat(StatId.Hp, -10)),
                BxLeave());
            BxShared("blood_moon", "Blood Moon", "A red moon rises. Wounds bleed faster tonight.", "prop:event_shrine", 2,
                BxDo("Hunt (lose 10% HP)", "Nearby enemies −15%.", "The hunters become the hunted.", BxLose(10), BxEnemies(-15)),
                BxDo("Hide", "Boss awakening +10.", "You wait for dawn.", BxWake(10)));
            BxShared("time_rift", "Time Rift", "A small rift replays the last few minutes.", "prop:time_rift", 2,
                BxDo("Step back", "Heal 30% HP. Boss awakening +10.", "Your wounds were never there.", BxHeal(30), BxWake(10)),
                BxDo("Seal it", "Boss awakening −10.", "Time runs slower here.", BxWake(-10)));
            BxShared("bounty_board", "Bounty Board", "A bounty board names a nearby elite.", "prop:bounty_board", 2.5,
                BxDo("Take the bounty", "Elite fight (+20%). Win: +150 coins.", "Bounty collected.", BxElite(20), BxCoins(150)),
                BxLeave());
            var well = BxBet("Throw in 50 coins", "60%: improve a random skill. 40%: nothing.",
                RunOut(60, "The well hums. One of your skills grows.", BxTrainRandom()), RunOut(40, "Plop. Nothing."));
            well.CoinCost = 50;
            BxShared("wishing_well", "Wishing Well", "A well hums when coins drop in.", "prop:wishing_well", 2, well, BxLeave());
            BxShared("strange_mirror", "Strange Mirror", "A mirror shows a different you.", "prop:strange_mirror", 2,
                BxDo("Swap", "Lose a random skill. Gain a Legendary+ skill.", "The other you steps out.", BxLoseSkill(), BxSkill(Rarity.Legendary)),
                BxLeave());
            BxShared("supply_drop", "Supply Drop", "A supply drop crashes nearby.", "prop:supply_drop", 3,
                BxDo("Open it", "+80 coins.", "Coins!", BxCoins(80)),
                BxLeave());
        }

        // ---- biome events ---------------------------------------------------------------------------------------------

        private void BuildBiomeEventsEmber()
        {
            const string b = "lava";
            BxEvent(b, "lava_salamander_smith", "Salamander Smith", "A salamander smith hammers at a glowing anvil.", "npc:salamander_smith",
                BxBuy(60, "Forge armor (60 coins)", "+10% DEF for the run.", "Black-glass plates, still warm.", BxStat(StatId.Def, 10)),
                BxBuy(60, "Forge a blade (60 coins)", "+15% Fire damage for the run.", "The blade never cools.", BxStat(StatId.FireDamage, 15)),
                BxLeave());
            BxEvent(b, "lava_ore_geyser", "Ore Geyser", "A geyser spits glowing ore every few seconds.", "prop:ore_geyser",
                BxDo("Catch the ore", "Ore" + ChestNote + ". Lose 15% HP.", "Hot, hot, hot!", BxLose(15), BxChest(Currencies.Ore)),
                BxLeave());
            BxEvent(b, "lava_magma_forge", "Magma Forge", "An abandoned forge still burns hot.", "prop:magma_forge",
                BxBet("Overheat it", "50%: +15% Fire damage for the run. 50%: lose 20% HP.",
                    RunOut(50, "The forge roars and your fangs glow.", BxStat(StatId.FireDamage, 15)), RunOut(50, "It blows up in your face.", BxLose(20))),
                BxDo("Warm up", "Heal 15% HP.", "Toasty.", BxHeal(15)));
            BxEvent(b, "lava_bridge", "Lava Bridge", "A crumbling bridge crosses a lava river. The long way round is safe.", "prop:event_wreck",
                BxBet("Run across", "70%: +2 portal shards. 30%: lose 25% HP.",
                    RunOut(70, "Across! Shards glitter on the far bank.", BxShards(2)), RunOut(30, "The bridge gives way under you.", BxLose(25))),
                BxDo("Go round", "Boss awakening +10.", "The long way.", BxWake(10)));

            const string g = "gas_giant";
            BxEvent(g, "gas_rig_foreman", "Rig Foreman", "A rig foreman needs a hand with a stuck valve.", "npc:rig_foreman",
                BxDo("Help (lose 10% HP)", "+60 coins.", "The valve gives. He pays well.", BxLose(10), BxCoins(60)),
                BxLeave());
            BxEvent(g, "gas_updraft", "Updraft", "A roaring updraft howls between the rigs.", "prop:gas_vent",
                BxBet("Ride it", "60%: +10% Speed for the run. 40%: lose 15% HP.",
                    RunOut(60, "You fly!", BxStat(StatId.Speed, 10)), RunOut(40, "It slams you into a rig.", BxLose(15))),
                BxLeave());
            BxEvent(g, "gas_pocket", "Gas Pocket", "A pocket of glowing gas. One spark and it blows.", "prop:gas_vent",
                BxDo("Ignite it (lose 10% HP)", "Nearby enemies −15%.", "The blast scorches everything ahead.", BxLose(10), BxEnemies(-15)),
                BxBet("Breathe it in", "50%: +10% ATK for the run. 50%: lose 10% HP.",
                    RunOut(50, "You feel explosive.", BxStat(StatId.Atk, 10)), RunOut(50, "Cough, cough.", BxLose(10))),
                BxLeave());
            BxEvent(g, "gas_refinery_overflow", "Refinery Overflow", "A refinery tank overflows with Fuel.", "prop:fuel_tank",
                BxDo("Fill up", "Fuel" + ChestNote + ". The boss awakens 10% faster.", "The tank alarm wakes the storm.", BxChest(Currencies.Fuel), BxPace(10)),
                BxLeave());

            const string d = "desert_ruins";
            BxEvent(d, "desert_sarcophagus", "Sealed Sarcophagus", "A sealed sarcophagus. The lid is carved with warnings.", "prop:sarcophagus",
                BxBet("Open it", "50%: gain a Legendary+ skill. 50%: elite fight.",
                    RunOut(50, "An ancient technique, still sharp.", BxSkill(Rarity.Legendary)), RunOut(50, "Its guardian wakes!", BxElite())),
                BxLeave());
            BxEvent(d, "desert_tomb_keeper", "Tomb Keeper", "An old keeper sells blessings of the sun.", "npc:tomb_keeper",
                BxBuy(80, "Sun blessing (80 coins)", "+10% ATK for the run.", "The sun fights with you.", BxStat(StatId.Atk, 10)),
                BxBuy(30, "Water (30 coins)", "Heal 25% HP.", "Cool water.", BxHeal(25)),
                BxLeave());
            BxEvent(d, "desert_buried_vault", "Buried Vault", "Sand slides away from a metal door.", "prop:buried_vault",
                BxDo("Dig it out", "Ore" + ChestNote + ". Boss awakening +15.", "The door grinds open. Loudly.", BxChest(Currencies.Ore), BxWake(15)),
                BxLeave());
            BxEvent(d, "desert_mirage_oasis", "Mirage Oasis", "An oasis shimmers ahead. It might be real.", "prop:oasis",
                BxBet("Walk to it", "60%: heal 40% HP. 40%: lose 15% HP.",
                    RunOut(60, "It was real!", BxHeal(40)), RunOut(40, "Only sand. Hot sand.", BxLose(15))),
                BxLeave());
        }

        private void BuildBiomeEventsGlacier()
        {
            const string b = "seabed";
            BxEvent(b, "seabed_giant_clam", "Giant Clam", "A giant clam slowly opens. A pearl glows inside.", "prop:giant_clam",
                BxBet("Grab the pearl", "60%: +2 portal shards. 40%: it snaps shut: lose 20% HP.",
                    RunOut(60, "Got it!", BxShards(2)), RunOut(40, "Snap!", BxLose(20))),
                BxLeave());
            var trade = BxDo("Trade 1 portal shard", "+10% max HP for the run.", "A lucky charm from the deep.", BxStat(StatId.Hp, 10));
            trade.ShardCost = 1;
            BxEvent(b, "seabed_diver_hermit", "Diver Hermit", "An old diver trades for shiny things.", "npc:diver_hermit",
                trade,
                BxBuy(60, "Buy a harpoon (60 coins)", "+15% Ice damage for the run.", "Cold steel.", BxStat(StatId.IceDamage, 15)),
                BxLeave());
            BxEvent(b, "seabed_kelp_harvest", "Kelp Harvest", "A forest of glowing kelp. Something big sleeps in it.", "prop:kelp_patch",
                BxDo("Harvest it", "Elite fight. Win: Biomass" + ChestNote + ".", "The kelp is yours.", BxElite(), BxChest(Currencies.Biomass)),
                BxLeave());
            BxEvent(b, "seabed_bubble_vent", "Bubble Vent", "Warm bubbles rise from a vent.", "prop:kelp_patch",
                BxDo("Rest in the bubbles", "Heal 25% HP. Boss awakening +5.", "So warm.", BxHeal(25), BxWake(5)),
                BxDo("Seal the vent", "Boss awakening −10.", "The deep grows quiet.", BxWake(-10)));

            const string c = "cloud_islands";
            BxEvent(c, "cloud_sky_farmer", "Sky Farmer", "A farmer on a floating field waves you over.", "npc:sky_farmer",
                BxDo("Help harvest (lose 10% HP)", "Grain" + ChestNote + ".", "Hard work, good harvest.", BxLose(10), BxChest(Currencies.Grain)),
                BxBuy(30, "Buy bread (30 coins)", "Heal 25% HP.", "Fresh from the sky oven.", BxHeal(25)),
                BxLeave());
            BxEvent(c, "cloud_chain_bridge", "Chain Bridge", "A frozen chain links two islands.", "prop:event_wreck",
                BxBet("Climb across", "60%: +2 portal shards. 40%: you fall: lose 20% HP.",
                    RunOut(60, "Shards on the other side!", BxShards(2)), RunOut(40, "You slip and fall.", BxLose(20))),
                BxDo("Go around", "Boss awakening +10.", "The long way.", BxWake(10)));
            BxEvent(c, "cloud_wind_shrine", "Wind Shrine", "A shrine where the wind always blows.", "prop:wind_shrine",
                BxDo("Pray for speed", "+10% Speed for the run.", "The wind carries you.", BxStat(StatId.Speed, 10)),
                BxDo("Pray for calm", "Boss awakening −15.", "The skies settle.", BxWake(-15)));
            BxEvent(c, "cloud_roc_nest", "Roc Nest", "An unguarded roc nest. Something glitters in it.", "prop:roc_nest",
                BxDo("Rob the nest", "Elite fight (+20%). Win: 1 reward chest (paid on clear, +Danger).", "The glitter is yours.", BxElite(20), BxLoot()),
                BxLeave());

            const string i = "ice";
            BxEvent(i, "ice_fisher", "Ice Fisher", "A fisher sits by a hole in the ice.", "npc:ice_fisher",
                BxBuy(30, "Buy a fish (30 coins)", "Heal 25% HP.", "Grilled on the spot.", BxHeal(25)),
                BxBet("Try fishing", "60%: heal 40% HP. 40%: nothing bites.",
                    RunOut(60, "A big one!", BxHeal(40)), RunOut(40, "Nothing bites.")),
                BxLeave());
            BxEvent(i, "ice_frozen_hero", "Frozen Hero", "Someone is frozen inside a block of ice.", "prop:frozen_block",
                BxDo("Thaw them (lose 15% HP)", "Gain a Legendary+ skill.", "They teach you before they go.", BxLose(15), BxSkill(Rarity.Legendary)),
                BxLeave());
            var shelf = BxEvent(i, "ice_cracking_shelf", "Cracking Shelf", "The ice cracks under you.", "prop:methane_vent",
                BxDo("Jump to safety", "Lose 10% HP.", "A hard landing.", BxLose(10)),
                BxBet("Ride the floe", "50%: +2 portal shards. 50%: lose 25% HP.",
                    RunOut(50, "The floe drifts past a shard cache.", BxShards(2)), RunOut(50, "Into the cold water.", BxLose(25))));
            shelf.DefaultOption = 0;
            shelf.Negative = true;
            BxEvent(i, "ice_methane_vent", "Methane Vent", "Bubbles of frozen gas glow under the ice.", "prop:methane_vent",
                BxDo("Drill it", "Fuel" + ChestNote + ". The boss awakens 10% faster.", "The ice groans.", BxChest(Currencies.Fuel), BxPace(10)),
                BxLeave());

            const string r = "coral_reef";
            BxEvent(r, "reef_crab_trader", "Crab Trader", "A hermit crab trader shows off his shells.", "npc:crab_trader",
                BxBuy(60, "Shell armor (60 coins)", "+10% DEF for the run.", "A snug shell.", BxStat(StatId.Def, 10)),
                BxBuy(60, "Spiked shell (60 coins)", "+10% Counter chance for the run.", "Spiky!", BxStat(StatId.CounterChance, 10)),
                BxLeave());
            BxEvent(r, "reef_bright_anemone", "Bright Anemone", "A glowing anemone pulses with light.", "prop:anemone",
                BxBet("Touch it", "50%: +10% ATK for the run. 50%: lose 15% HP.",
                    RunOut(50, "Its light flows into you.", BxStat(StatId.Atk, 10)), RunOut(50, "It stings!", BxLose(15))),
                BxLeave());
            BxEvent(r, "reef_nursery", "Reef Nursery", "A reef nursery full of young fish.", "prop:reef_nursery",
                BxDo("Gather", "Biomass" + ChestNote + ". The boss gains +10% ATK.", "The reef's guardian is furious.", BxChest(Currencies.Biomass), BxBossAtk(10)),
                BxLeave());
            BxEvent(r, "reef_fast_current", "Fast Current", "A fast current runs along the reef.", "prop:reef_nursery",
                BxDo("Ride it", "+10% Speed for the run. Boss awakening +10.", "Whoosh!", BxStat(StatId.Speed, 10), BxWake(10)),
                BxLeave());
        }

        private void BuildBiomeEventsStatic()
        {
            const string b = "space_station";
            BxEvent(b, "station_ai", "Station AI", "A cracked screen flickers: 'Visitor. Authorize upgrade?'", "npc:station_ai",
                BxBuy(80, "Authorize (80 coins)", "+15% Lightning damage for the run.", "Upgrade installed.", BxStat(StatId.LightningDamage, 15)),
                BxBet("Hack it", "50%: gain a Legendary+ skill. 50%: elite fight.",
                    RunOut(50, "Access granted.", BxSkill(Rarity.Legendary)), RunOut(50, "Security alert!", BxElite())),
                BxLeave());
            BxEvent(b, "station_server_room", "Server Room", "Racks of servers, still humming.", "prop:server_rack",
                BxDo("Download", "Data" + ChestNote + ". Boss awakening +15.", "Download complete. Alarms blaring.", BxChest(Currencies.Data), BxWake(15)),
                BxLeave());
            BxEvent(b, "station_airlock", "Airlock", "An airlock blinks: OPEN?", "prop:event_wreck",
                BxDo("Vent the deck", "Nearby enemies −10%.", "Whatever was waiting ahead is gasping for air.", BxEnemies(-10)),
                BxLeave());
            BxEvent(b, "station_med_bay", "Med Bay", "An automated med bay. One pod still works.", "prop:med_pod",
                BxBuy(40, "Use the pod (40 coins)", "Heal 40% HP.", "Good as new.", BxHeal(40)),
                BxBet("Overclock it", "50%: +10% max HP for the run. 50%: lose 15% HP.",
                    RunOut(50, "Enhanced!", BxStat(StatId.Hp, 10)), RunOut(50, "It malfunctions.", BxLose(15))),
                BxLeave());

            const string j = "alien_jungle";
            BxEvent(j, "jungle_frog_shaman", "Frog Shaman", "A frog shaman offers a bitter brew.", "npc:frog_shaman",
                BxBuy(60, "Drink the brew (60 coins)", "Gain a Legendary+ skill.", "Visions!", BxSkill(Rarity.Legendary)),
                BxLeave());
            BxEvent(j, "jungle_fruit_tree", "Fruit Tree", "A tree heavy with glowing fruit.", "prop:fruit_tree",
                BxDo("Pick the fruit", "Grain" + ChestNote + ". Nearby enemies grow stronger.", "The smell draws hungry things.", BxChest(Currencies.Grain), BxEnemies(10)),
                BxBet("Eat one", "60%: heal 30% HP. 40%: lose 10% HP.",
                    RunOut(60, "Sweet!", BxHeal(30)), RunOut(40, "Sour. Very sour.", BxLose(10))),
                BxLeave());
            BxEvent(j, "jungle_giant_plant", "Giant Plant", "A giant plant has swallowed a supply crate.", "prop:swallow_plant",
                BxDo("Cut it open", "Elite fight. Win: 1 reward chest (paid on clear, +Danger).", "The crate is yours.", BxElite(), BxLoot()),
                BxLeave());
            BxEvent(j, "jungle_lightning_totem", "Lightning Totem", "A totem crackles with stored lightning.", "prop:lightning_totem",
                BxDo("Touch it (lose 10% HP)", "+10% Lightning damage for the run.", "Zap! The charge stays with you.", BxLose(10), BxStat(StatId.LightningDamage, 10)),
                BxDo("Break it", "+60 coins. Boss awakening +10.", "Coins spill out. The jungle hears.", BxCoins(60), BxWake(10)));

            const string n = "neon_city";
            BxEvent(n, "neon_dealer", "Back-Alley Dealer", "A dealer opens a coat full of skill chips.", "npc:black_marketeer",
                BxBuy(100, "Buy a chip (100 coins)", "Gain a Legendary+ skill.", "Plug and play.", BxSkill(Rarity.Legendary)),
                BxDo("Sell a skill", "Lose a random skill. +120 coins.", "Sold. No refunds.", BxLoseSkill(), BxCoins(120)),
                BxLeave());
            BxEvent(n, "neon_data_broker", "Data Broker", "A broker sells stolen data by the gigabyte.", "prop:data_kiosk",
                BxBuy(120, "Buy data (120 coins)", "Data" + ChestNote + ".", "Encrypted, but yours.", BxChest(Currencies.Data)),
                BxLeave());
            var can = BxBet("Buy a can (20 coins)", "50%: heal 30% HP. 30%: +5% ATK for the run. 20%: empty can.",
                RunOut(50, "Refreshing!", BxHeal(30)), RunOut(30, "Energy drink!", BxStat(StatId.Atk, 5)), RunOut(20, "Empty. Of course."));
            can.CoinCost = 20;
            BxEvent(n, "neon_vending_machine", "Vending Machine", "'Mystery can: 20 coins.'", "prop:vending_machine", can, BxLeave());
            BxEvent(n, "neon_street_race", "Street Race", "Street racers dare you to a race.", "prop:data_kiosk",
                BxDo("Race", "Elite fight. Win: +10% Speed for the run.", "You win the race.", BxElite(), BxStat(StatId.Speed, 10)),
                BxLeave());
        }

        private void BuildBiomeEventsVoid()
        {
            const string b = "shipwreck";
            BxEvent(b, "wreck_engine_room", "Engine Room", "A wrecked engine still holds its cores.", "prop:event_wreck",
                BxDo("Pull the cores", "Gear Core" + ChestNote + ". Lose 15% HP.", "Sparks fly.", BxLose(15), BxChest(Currencies.GearCores)),
                BxLeave());
            BxEvent(b, "wreck_scavenger", "Scavenger", "A scavenger sells parts from the wrecks.", "npc:scavenger",
                BxBuy(60, "Hull plating (60 coins)", "+10% DEF for the run.", "Dented, but solid.", BxStat(StatId.Def, 10)),
                BxBuy(60, "Rusty blade (60 coins)", "+15% Bleed damage for the run.", "Tetanus included.", BxStat(StatId.BleedDamage, 15)),
                BxLeave());
            BxEvent(b, "wreck_distress_beacon", "Distress Beacon", "A beacon still calls for help.", "prop:distress_beacon",
                BxBet("Answer it", "50%: a survivor teaches you: gain a Legendary+ skill. 50%: ambush: elite fight.",
                    RunOut(50, "A survivor! They teach you a trick.", BxSkill(Rarity.Legendary)), RunOut(50, "Ambush!", BxElite())),
                BxDo("Switch it off", "Boss awakening −10.", "Silence.", BxWake(-10)));
            BxEvent(b, "wreck_captains_locker", "Captain's Locker", "A locked captain's locker.", "prop:abandoned_cart",
                BxBet("Force it open", "60%: +100 coins. 40%: toxic gas: lose 20% HP.",
                    RunOut(60, "The captain's savings!", BxCoins(100)), RunOut(40, "Toxic gas!", BxLose(20))),
                BxLeave());

            const string t = "toxic_swamp";
            BxEvent(t, "swamp_witch", "Swamp Witch", "A witch stirs a bubbling cauldron.", "npc:cursed_vendor",
                BxBet("Drink the brew", "50%: +15% ATK for the run. 50%: −10% max HP for the run.",
                    RunOut(50, "Power!", BxStat(StatId.Atk, 15)), RunOut(50, "Ugh.", BxStat(StatId.Hp, -10))),
                BxBuy(40, "Antidote (40 coins)", "Heal 30% HP.", "Bitter, but it works.", BxHeal(30)),
                BxLeave());
            BxEvent(t, "swamp_gas_bubbles", "Gas Bubbles", "Huge bubbles of swamp gas rise and pop.", "prop:gas_bubbles",
                BxDo("Bottle the gas", "Fuel" + ChestNote + ". Lose 15% HP.", "Pop! Your eyes water.", BxLose(15), BxChest(Currencies.Fuel)),
                BxLeave());
            BxEvent(t, "swamp_toxic_bloom", "Toxic Bloom", "A flower drips glowing poison.", "prop:toxic_bloom",
                BxDo("Coat your fangs (lose 10% HP)", "+15% Radiation damage for the run.", "Your bite glows green.", BxLose(10), BxStat(StatId.PoisonDamage, 15)),
                BxLeave());
            BxEvent(t, "swamp_sunken_hut", "Sunken Hut", "A hut sinks into the mud. The door is open.", "prop:sunken_hut",
                BxBet("Search the hut", "60%: +2 portal shards. 40%: a gator: enemy fight.",
                    RunOut(60, "Shards under the floorboards.", BxShards(2)), RunOut(40, "A gator lives here!", BxFoe())),
                BxLeave());

            const string m = "mushroom_forest";
            // moved from the shared pool (biomes pass); the id stays
            BxEvent(m, "strange_spores", "Strange Spores", "Glowing spores drift by. They smell edible.", "prop:fairy_ring",
                BxBet("Eat them", "50%: +10% ATK for the run. 50%: lose 15% HP.",
                    RunOut(50, "Delicious power.", BxStat(StatId.Atk, 10)), RunOut(50, "Bad spores.", BxLose(15))),
                BxLeave());
            BxEvent(m, "mushroom_fairy_ring", "Fairy Ring", "A perfect ring of glowing mushrooms.", "prop:fairy_ring",
                BxBet("Step inside", "50%: improve a random skill. 50%: lose a random skill.",
                    RunOut(50, "The ring blesses one of your skills.", BxTrainRandom()), RunOut(50, "The ring takes a skill.", BxLoseSkill())),
                BxLeave());
            BxEvent(m, "mushroom_myco_hermit", "Myco Hermit", "A mushroom hermit grows medicine.", "npc:myco_hermit",
                BxBuy(40, "Healing cap (40 coins)", "Heal 35% HP.", "Earthy.", BxHeal(35)),
                BxBuy(80, "Giant cap (80 coins)", "+10% max HP for the run.", "You feel sturdier.", BxStat(StatId.Hp, 10)),
                BxLeave());
            BxEvent(m, "mushroom_giant_mushroom", "Giant Mushroom", "A mushroom the size of a house, ripe for harvest.", "prop:giant_mushroom",
                BxDo("Harvest it", "Biomass" + ChestNote + ". Boss awakening +15.", "It falls with a mighty THUD.", BxChest(Currencies.Biomass), BxWake(15)),
                BxLeave());

            const string c = "candy_planet";
            var candy = BxBet("Mystery candy (20 coins)", "50%: +8% ATK for the run. 50%: lose 10% HP.",
                RunOut(50, "Sugar power!", BxStat(StatId.Atk, 8)), RunOut(50, "Tummy ache.", BxLose(10)));
            candy.CoinCost = 20;
            BxEvent(c, "candy_vendor", "Candy Vendor", "A gumdrop vendor rings a little bell.", "npc:candy_vendor",
                BxBuy(40, "Sugar rush (40 coins)", "+10% Speed for the run.", "Zoom!", BxStat(StatId.Speed, 10)),
                candy,
                BxLeave());
            BxEvent(c, "candy_chocolate_fountain", "Chocolate Fountain", "A fountain of warm chocolate.", "prop:chocolate_fountain",
                BxDo("Drink deep", "Heal 40% HP. −5% Speed for the run.", "So full.", BxHeal(40), BxStat(StatId.Speed, -5)),
                BxLeave());
            BxEvent(c, "candy_gingerbread_house", "Gingerbread House", "A gingerbread house with an open door.", "prop:gingerbread_house",
                BxDo("Go inside", "Elite fight. Win: Hero XP" + ChestNote + ".", "The house's owner is beaten.", BxElite(), BxChest(Currencies.HeroXp)),
                BxLeave());
            BxEvent(c, "candy_sprinkle_rain", "Sprinkle Rain", "It starts raining sprinkles.", "prop:chocolate_fountain",
                BxDo("Catch them", "+25 XP.", "Crunchy wisdom.", BxXp(25)),
                BxLeave());
        }

        private void BuildBiomeEventsChrono()
        {
            const string b = "crystal_cave";
            var crystal = BxEvent(b, "crystal_time_crystal", "Time Crystal", "A crystal holds a moment of the past.", "prop:time_crystal",
                BxDo("Look inside", "Boss awakening −15.", "You see where the boss sleeps.", BxWake(-15)),
                BxBet("Break it", "50%: gain a Legendary+ skill. 50%: lose 20% HP.",
                    RunOut(50, "A past technique is yours.", BxSkill(Rarity.Legendary)), RunOut(50, "Shards everywhere.", BxLose(20))));
            crystal.DefaultOption = 0;
            BxEvent(b, "crystal_singer", "Crystal Singer", "A singer tunes crystals to your voice.", "npc:crystal_singer",
                BxBuy(60, "Tune a skill (60 coins)", "Improve 1 skill of your choice.", "Perfect pitch.", BxTrain()),
                BxLeave());
            BxEvent(b, "crystal_sealed_geode", "Sealed Geode", "A giant geode hums. Something guards it.", "prop:geode",
                BxDo("Crack it", "Elite fight. Win: Ore" + ChestNote + ".", "Crystal ore spills out.", BxElite(), BxChest(Currencies.Ore)),
                BxLeave());
            BxEvent(b, "crystal_echo_chamber", "Echo Chamber", "Your echo answers before you speak.", "prop:time_crystal",
                BxBet("Shout", "60%: +2 portal shards. 40%: rocks fall: lose 20% HP.",
                    RunOut(60, "Shards ring down from the ceiling.", BxShards(2)), RunOut(40, "Rocks fall!", BxLose(20))),
                BxLeave());

            const string c = "clockwork_city";
            BxEvent(c, "clock_clockmaker", "Clockmaker", "A clockmaker offers to tune your rhythm.", "npc:clockmaker",
                BxBuy(60, "Tune speed (60 coins)", "+10% Speed for the run.", "Tick-tock, faster.", BxStat(StatId.Speed, 10)),
                BxBuy(60, "Tune combos (60 coins)", "+10% Combo chance for the run.", "Gears mesh.", BxStat(StatId.ComboChance, 10)),
                BxLeave());
            var clock = BxEvent(c, "clock_great_clock", "Great Clock", "The city's great clock. You could move its hands.", "prop:great_clock",
                BxDo("Turn it back (lose 15% HP)", "Boss awakening −20.", "The hands groan backwards.", BxLose(15), BxWake(-20)),
                BxDo("Turn it forward", "Gain a Legendary+ skill. Boss awakening +20.", "Tomorrow's technique, today.", BxSkill(Rarity.Legendary), BxWake(20)));
            clock.DefaultOption = 0;
            BxEvent(c, "clock_gear_pile", "Gear Pile", "A heap of spare gears and springs.", "prop:gear_pile",
                BxDo("Sort through it", "Gear Core" + ChestNote + ". Nearby enemies grow stronger.", "The clatter carries.", BxChest(Currencies.GearCores), BxEnemies(10)),
                BxLeave());
            var repair = BxBet("Repair it (40 coins)", "60%: it helps: +10% ATK for the run. 40%: it attacks: elite fight.",
                RunOut(60, "It whirs gratefully and tunes your fangs.", BxStat(StatId.Atk, 10)), RunOut(40, "It attacks!", BxElite()));
            repair.CoinCost = 40;
            BxEvent(c, "clock_broken_automaton", "Broken Automaton", "A broken automaton twitches in the street.", "prop:automaton", repair, BxLeave());

            const string f = "frozen_future";
            BxEvent(f, "future_cryo_pod", "Cryo Pod", "A cryo pod with a sleeping soldier inside.", "prop:cryo_pod",
                BxBet("Wake them", "50%: they train you: Hero XP" + ChestNote + ". 50%: they panic: elite fight.",
                    RunOut(50, "They train you.", BxChest(Currencies.HeroXp)), RunOut(50, "They panic!", BxElite())),
                BxLeave());
            BxEvent(f, "future_time_traveler", "Time Traveler", "A traveler from yesterday asks the date.", "npc:time_traveler",
                BxDo("Tell them", "+25 XP.", "They tell you about tomorrow.", BxXp(25)),
                BxBuy(80, "Buy their watch (80 coins)", "Boss awakening −20.", "Time is on your side.", BxWake(-20)),
                BxLeave());
            BxEvent(f, "future_frozen_rain", "Frozen Rain", "Raindrops hang in the air like glass beads.", "prop:frozen_car",
                BxDo("Collect them (lose 10% HP)", "+2 portal shards.", "Glassy shards.", BxLose(10), BxShards(2)),
                BxLeave());
            BxEvent(f, "future_frozen_car", "Frozen Car", "A car hangs mid-air above the road.", "prop:frozen_car",
                BxBet("Search it", "60%: +100 coins. 40%: it falls: lose 25% HP.",
                    RunOut(60, "Coins in the glovebox!", BxCoins(100)), RunOut(40, "It falls!", BxLose(25))),
                BxLeave());
        }

        // ---- Rites (one per biome) --------------------------------------------------------------------------------------

        private void BuildRites()
        {
            BxRite("lava", "Quench the Crust", "Steam hisses where the crust is thin. Something cold could seal it.", "prop:magma_forge",
                BxOffer(SkillFamily.Ice, "Offer an Ice skill", "Boss −20% ATK.", "The crust seals. The boss below weakens.", BxBossAtk(-20)),
                BxDo("Bathe in the lava (lose 20% HP)", "+20% Fire damage for the run.", "You come out burning.", BxLose(20), BxStat(StatId.FireDamage, 20)),
                BxLeave());
            BxRite("gas_giant", "Storm Valve", "A master valve controls the storm feeding the rigs.", "prop:gas_vent",
                BxOffer(SkillFamily.Counter, "Offer a Counter skill", "The boss awakens 25% slower.", "The storm calms.", BxPace(-25)),
                BxDo("Open it fully (boss +10% ATK)", "+2 portal shards.", "Shards rain from the storm.", BxBossAtk(10), BxShards(2)),
                BxLeave());
            BxRite("desert_ruins", "Sun Altar", "The Sun-King's altar waits for a sacrifice at noon.", "prop:event_shrine",
                BxOffer(SkillFamily.Ice, "Offer an Ice skill", "Boss −20% HP.", "The sun dims.", BxBossHp(-20)),
                BxBuy(60, "Pray (60 coins)", "Boss awakening −20.", "The altar accepts your coins.", BxWake(-20)),
                BxLeave());
            BxRite("seabed", "Drowned Brazier", "A brazier still burns at the bottom of the sea. The deep wants it out.", "prop:event_shrine",
                BxOffer(SkillFamily.Fire, "Offer a Heat skill", "Nearby enemies −25%.", "The deep is grateful.", BxEnemies(-25)),
                BxBuy(40, "Relight it (40 coins)", "+15% Fire damage for the run.", "Fire under water!", BxStat(StatId.FireDamage, 15)),
                BxLeave());
            BxRite("cloud_islands", "Roc Nest", "A Hailwing Roc's nest, eggs still warm.", "prop:event_wreck",
                BxOffer(SkillFamily.Poison, "Offer a Radiation skill", "Boss −20% ATK.", "The roc falls ill.", BxBossAtk(-20)),
                BxDo("Steal an egg (boss +15% ATK)", "Gain a Legendary+ skill.", "The roc screams for its egg.", BxBossAtk(15), BxSkill(Rarity.Legendary)),
                BxLeave());
            BxRite("ice", "Thin Ice", "The ice over the Colossus's den is cracking.", "prop:frozen_block",
                BxOffer(SkillFamily.Combo, "Offer a Combo skill", "Boss −20% HP.", "The den caves in.", BxBossHp(-20)),
                BxDo("Break it (lose 15% HP)", "+15% Ice damage for the run.", "You climb out colder.", BxLose(15), BxStat(StatId.IceDamage, 15)),
                BxLeave());
            BxRite("coral_reef", "Blood in the Water", "The Tide Hydra hunts by scent. You could leave it a trail.", "prop:event_wreck",
                BxOffer(SkillFamily.Poison, "Offer a Radiation skill", "Boss −20% HP.", "The hydra swallows the bait.", BxBossHp(-20)),
                BxDo("Cut yourself (lose 15% HP)", "+10% ATK for the run.", "The scent makes you fierce.", BxLose(15), BxStat(StatId.Atk, 10)),
                BxLeave());
            BxRite("space_station", "Purge Protocol", "A console offers a full quarantine purge of the hive deck.", "prop:event_wreck",
                BxOffer(SkillFamily.AlienSynergy, "Offer an Alien skill", "Nearby enemies −25%.", "Purge complete.", BxEnemies(-25)),
                BxBuy(60, "Overload the grid (60 coins)", "Boss awakening −20.", "The hive goes dark.", BxWake(-20)),
                BxLeave());
            BxRite("alien_jungle", "Prune the Orchid", "Vines lead back to the Volt Orchid's root.", "prop:event_wreck",
                BxOffer(SkillFamily.None, "Offer a skill with no synergy", "Boss −20% ATK.", "The orchid wilts.", BxBossAtk(-20)),
                BxDo("Eat a petal (lose 10% HP)", "+10% Speed for the run.", "Your heart races.", BxLose(10), BxStat(StatId.Speed, 10)),
                BxLeave());
            BxRite("neon_city", "Cut the Speakers", "The Cantor's song plays from every speaker in the city.", "prop:event_wreck",
                BxOffer(SkillFamily.Time, "Offer a Time skill", "The boss awakens 25% slower.", "The city goes quiet.", BxPace(-25)),
                BxDo("Turn it up (boss +10% ATK)", "+15% Lightning damage for the run.", "The bass hits you like lightning.", BxBossAtk(10), BxStat(StatId.LightningDamage, 15)),
                BxLeave());
            BxRite("shipwreck", "Dead Generator", "The wreck's generator could be scuttled for good.", "prop:event_wreck",
                BxOffer(SkillFamily.Lightning, "Offer a Shock skill", "Boss −20% HP.", "The last spark dies.", BxBossHp(-20)),
                BxDo("Strip it (lose 10% HP)", "+100 coins.", "Copper sells well.", BxLose(10), BxCoins(100)),
                BxLeave());
            BxRite("toxic_swamp", "Drain the Bog", "A sluice gate holds back the Gloop Matriarch's pool.", "prop:leaking_barrel",
                BxOffer(SkillFamily.Combo, "Offer a Combo skill", "Nearby enemies −25%.", "The bog drains away.", BxEnemies(-25)),
                BxDo("Drink from it (−10% max HP for the run)", "Gain a Legendary+ skill.", "Foul, but enlightening.", BxStat(StatId.Hp, -10), BxSkill(Rarity.Legendary)),
                BxLeave());
            BxRite("mushroom_forest", "Burn the Spores", "Dry spore stalks around the Monarch's ring would burn fast.", "prop:event_wreck",
                BxOffer(SkillFamily.Lightning, "Offer a Shock skill", "Boss −20% ATK.", "The spores go up in flames.", BxBossAtk(-20)),
                BxDo("Inhale (lose 10% HP)", "+50 XP.", "Strange thoughts. Useful ones.", BxLose(10), BxXp(50)),
                BxLeave());
            BxRite("candy_planet", "Sugar Crash", "A sticky syrup lake next to the Behemoth's den.", "prop:chocolate_fountain",
                BxOffer(SkillFamily.Counter, "Offer a Counter skill", "The boss awakens 25% slower.", "The Behemoth sleeps it off.", BxPace(-25)),
                BxDo("Taste it (lose 10% HP)", "+10% Speed for the run.", "Sugar rush!", BxLose(10), BxStat(StatId.Speed, 10)),
                BxLeave());
            BxRite("crystal_cave", "Shatter the Prism", "The Prism Tyrant draws light from one giant crystal.", "prop:time_crystal",
                BxOffer(SkillFamily.Bleed, "Offer a Bleed skill", "Boss −20% ATK.", "The prism cracks.", BxBossAtk(-20)),
                BxBuy(40, "Take a shard (40 coins)", "+10% Counter chance for the run.", "It reflects every blow.", BxStat(StatId.CounterChance, 10)),
                BxLeave());
            BxRite("clockwork_city", "Stop the Clock", "The Chrono Wyrm rewinds itself with the great clock's spring.", "prop:great_clock",
                BxOffer(SkillFamily.Bleed, "Offer a Bleed skill", "Boss −20% HP.", "The spring snaps.", BxBossHp(-20)),
                BxDo("Wind it back (lose 15% HP)", "Boss awakening −20.", "The hands turn back.", BxLose(15), BxWake(-20)),
                BxLeave());
            BxRite("frozen_future", "Thaw the Moment", "One moment, frozen. Warmth would set it moving again.", "prop:cryo_pod",
                BxOffer(SkillFamily.Fire, "Offer a Heat skill", "The boss awakens 25% slower.", "Time drips forward, slowly.", BxPace(-25)),
                BxDo("Freeze with it (−5% Speed for the run)", "Boss awakening −15.", "You stand still with the world.", BxStat(StatId.Speed, -5), BxWake(-15)),
                BxLeave());
        }
    }
}
