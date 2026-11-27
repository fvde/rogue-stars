# Pull system — one system for Mythic pulls, shard batches, the Shard Reactor

Owner decisions (2026-11-24), implemented as decided and nothing more ("I need to play more to get a feeling for the
economy": no welcome-gift, per-star, batch-size or income tuning). Numbers live in `ContentDb.ShardStars`
(`Content/ContentDb.ShardStars.cs`), `ContentDb.GachaConfig` (`ContentDb.Gacha.cs`), `ContentDb.SummonEvents` and
`ContentDb.ShardReactor` (`ContentDb.Reactor.cs`). Systems: `GachaSystem`, `SummonEventSystem`, `HeroSystem`, `MountSystem`,
`AlienSystem`, `CitySystem`, `ShardReactorSystem`, `PullSystemMigration`. Tests: `Progression/PullSystemTests`,
`City/ShardReactorTests` (rules), `PullSystemServerTests` (server), `PullSystemScreenTests` (EditMode),
`PullSystemShowcaseTests` (explicit PlayMode screenshots), the simulator's `--pulls` report.

> **Pull currency names (owner, 2026-11-27):** the player sees **Signals** (pull Aliens, `Currencies.AlienTokens`),
> **Schematics** (Blueprints, `BlueprintTokens`), **Crates** (Parts, `PartTokens`), **Crests** (hero events, `HeroTickets`) and
> **Saddles** (mount events, `MountTickets`); singular for 1 ("1 Crate"). Ids, save keys and API fields keep the old names.
> **The Shop (owner, 2026-11-27):** the pull screen is the **Shop**, merged with the former purchase screen: its pull sections
> **Aliens**, **Blueprints** and **Parts** (paid with Signals, Schematics, Crates) come first, then the offer sections (Offers,
> Diamonds, Passes, Items, Ads). A pull is an "Aliens / Blueprints / Parts pull" ("Free 10× Parts pull"); the bottom tab "Shop"
> (shop icon) is there from the first session: the offer sections need no unlock (owner, 2026-11-27; it opens on Offers until a
> pull section exists), each pull section comes with its own feature. Code ids stay
> (`GachaType`, the `signals` screen id as an alias). The "Alien Signal Beacon" building keeps its name.

## Rates and pity (every pull, published, fixed)

| Pull type | Common | Rare | Epic | Legendary | Mythic | Mythic pity | Legendary pity | 10× rule |
|---|---|---|---|---|---|---|---|---|
| Aliens / Blueprints / Parts pull | 58.3% | 28% | 9.5% | **3%** | **1.2%** | **100** | **40** | at least one Epic |
| Hero / Mount summon event | 98.8% filler | – | – | – | **1.2%** (featured batch) | **100** (Luck) | none | none |

- **Mythic:** a flat 1.2% on every pull. The 100th pull without a Mythic is a Mythic ("Mythic x/100"). Average 58 pulls per
  Mythic ((1 − 0.988^100) / 0.012); 30% of Mythics land on the pity pull.
- **Legendary:** a flat 3% (chosen: rate × pity = 1.2 for both counters, the same spirit as Mythic; the 1.7 points for Legendary
  +1 and Mythic +0.7 come out of Common). The 40th pull without a Legendary or better is at least Legendary ("Legendary x/40");
  that pull still rolls Mythic at 1.2%, every lower result becomes Legendary. A Mythic also resets the Legendary counter.
- **No soft or hidden pity anywhere:** the hidden Mythic pity at 50 and the Legendary soft pity (+2.5 weight per pull after 25)
  with its hidden hard pity at 40 are removed (`GachaTypeDef` has only `MythicPity`, `LegendaryPity`, the 10× rule).
- **The 10× rule stays and is shown** (pull card and odds): the last pull of a 10× without an Epic or better becomes Epic.
  It only lifts results below Epic, so the Mythic and Legendary chances of that pull are unchanged. Summon events have no Epic
  tier and no 10× rule.
- **Visible everywhere:** the Shop's pull sections show "Mythic 37/100 · Legendary 12/40" (rarity colours, `Theme.RarityColor`) and the line
  "Guaranteed Mythic by pull 100, Legendary by pull 40 (fixed, never changes). Every 10× pull has at least one Epic." Pull results
  carry the counters (`GachaPullResult.Pity`); the odds modal states the pity and the 10× rule. Summon events show "Luck x/100".
- Pity is per pull type, shared by all its banners and carried over (unchanged).
- **Featured 80 / 20 (owner, 2026-11-25), every featured banner:** when a pull on a banner hits Legendary or Mythic and the banner
  features items of that tier, 80% of those results are a featured item (split evenly when several share the tier) and 20% another
  item of that tier ("off banner": the tier's pool without the featured items). The tier odds and the pity are unchanged, and there
  is **no hidden guarantee**: no "lost the 50/50 → the next is featured" carry-over existed before and none was added; every pull
  rolls the 80/20 afresh. Published: `GachaOddsTier.FeaturedPercent` (80) and the featured / off-banner item percentages in
  `GachaOdds` (e.g. the core blueprint banner: Laboratory 0.96%, other Mythics 0.24% in all; each pull building 0.8%, the other
  Legendary results 0.6%). Featured items are Legendary or Mythic only (`BannerTheme.FeaturedTier`, `MoreFeatured`).
  Summon events are unchanged: their Mythic tier is the featured batch alone (98.8% filler, no other Mythic and no Legendary tier),
  so there is no off-banner Mythic to give the 20% to — owner to decide if one should be added.
- The Signal Mythic tier holds Mythic items only (its hero / mount ticket supply entries are gone); Epic and Legendary keep their
  ticket supplies, Common and Rare their Ore / Grain.

## One star table (heroes, mounts, aliens, blueprints — every rarity)

| Step | Unlock (1★) | 2★ | 3★ | 4★ | 5★ | 6★ | 7★ | 8★ | 9★ | 10★ | Total |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Shards | 50 | 50 | 50 | 100 | 100 | 100 | 100 | 150 | 150 | 150 | **1 000** |
| Batches | 1 | 1 | 1 | 2 | 2 | 2 | 2 | 3 | 3 | 3 | 20 |

- **Pulls never pay small shard amounts:** winning an item (a Signal blueprint / alien of any rarity, a summon event's
  featured hero / mount, a reward of kind Hero / Mount / Blueprint / Alien) is a **batch of 50 shards**. The first batch of an
  item the player does not own unlocks it (the unlock costs exactly one batch; loose shards stay). Stars are raised with the
  StarUp commands. A batch for a 10★ hero / mount becomes the old overflow (300 Star Sigils / Hangar Bolts).
- **Everything goes to 10★.** Mounts: no awakening; the former awakening 1–5 is stars 6★–10★ (`MountAwaken` is gone), paid with
  shards on the shared table like every other star (owner, 2026-11-25: Mount Cores are removed, see below). Kit steps 1★ / 3★ /
  5★ / 8★ / 10★, the +5% mount damage steps 2★ / 4★ / 6★ / 7★ / 9★, level cap 20 + 10 per star (all as before, renamed).
- Epic stays a rarity (5 rarities for aliens and blueprints). Common and Epic aliens / blueprints use the same batches and table
  as Rare and Legendary (decided here: a 1-shard copy on a 1 000-shard table would be meaningless).
- Shards outside pulls are **tiny** next to the 50-shard batch (owner, 2026-11-25): the summon exchange featured shards ×5 per
  purchase (limits 3 / 2 as before), milestones ×5 / ×10 (heroes, pulls 25 / 100) and ×5 (mounts, pull 25), the premium pass ×5
  on its last day (heroes and mounts), breeding 1–3 shards on release. See "Shard sources outside pulls" below. Shards that are not a won batch never unlock by themselves:
  heroes and mounts unlock with the Unlock button at 50; Shard Reactor output unlocks its target at 50 on collect.

## Banner schedule (owner, 2026-11-25)

Every Signal has one rotating banner, one week each from Monday 00:00 UTC (epoch Monday 2027-01-04; the rotation runs before it
too). Rates and pity never change; the banner only decides which Legendary / Mythic results are featured (80 / 20, above).

| Signal | Cycle | Weeks |
|---|---|---|
| Blueprint | 6 weeks | **Core Blueprints** · Command Spire Rising · **Core Blueprints** · Reactor Ignition · **Core Blueprints** · Stasis Beacon Week |
| Alien | 2 weeks | Nebula Court (Nebulisk) · Swarm Season (Swarmheart) |
| Part | 4 weeks | Inferno Arsenal · Glacier Vault · Storm Works · Shadow Forge |

- **Core Blueprints** (`ContentDb.CoreBlueprintBannerId` = `bp_core`), every other week: the Laboratory featured in Mythic and the
  three pull buildings (Blueprint Relay, Alien Signal Beacon, Part Fabricator) featured in Legendary. Nothing else rotates into it.
  Odds per pull: Laboratory 0.96%, each pull building 0.8%. With the Mythic pity (a Mythic every ~58 pulls on average) a player
  pulling in core weeks gets the Laboratory about once per ~73 pulls.
- The themed weeks in between feature one Mythic blueprint each (Command Spire, Shard Reactor, the Stasis Beacon — now Mythic).
- Why every other week: the Laboratory gates research (pulled, built and staffed) and the pull buildings make the pull chances, so
  the core banner should never be more than a week away; alternating keeps the Mythic themes (and the Reactor's own banner) in.
- The free 10× pulls of the Parts / Aliens / City unlocks are normal 10× pulls with no guarantee (owner, 2026-11-25, "the crash
  site comes first": the Launch Pod now comes with the Aliens unlock, built on the crash site, so the City's free Blueprint pull
  no longer holds it; `GachaTypeDef.FreePullGuarantee` is removed). See docs/design/city.md "The crash site comes first".

Re-run of `--pulls` with the City redesign and these rules: `docs/balance/data/pulls_city.md` (the table below is from before the
City redesign). In that re-run the Reactor columns read 0 for every profile although the Reactor blueprint is owned ("4 M"): the
bot does not run Reactor jobs in the redesigned city (not investigated here).

## Unlocks come with pulls

| Unlock | Level | Comes with |
|---|---|---|
| – | – | the Shop tab with its offer sections (Offers, Diamonds, Passes, Items, Ads): no unlock, there from the start |
| Parts | 1 | the Shop's Parts section and one free 10× Parts pull |
| Aliens | 6 | the first alien, the Shop's Aliens section, one free 10× Aliens pull and the Launch Pod built on the crash site (squad seat from level 6) |
| City | 15 | the Biomass Farm blueprint, the Shop's Blueprints section and one free 10× Blueprints pull |

Every free 10× is a normal 10× (the 10× rule and the pity apply, no fixed result). Before the crash site (owner, 2026-11-25) the
City's free pull always held the Launch Pod. Details: docs/design/city.md "Unlocks" and "The crash site comes first".

## Hero and mount event pools (filler)

Heroes and mounts have no Legendary / Rare drops: 98.8% of the pulls are filler, 1.2% the featured batch (entry weights are
percent of all pulls).

| Hero event | % | Mount event | % |
|---|---|---|---|
| Hero XP ×300 | 88.75 | Fuel ×300 | 81.75 |
| Hero XP ×1 000 | 5 | Fuel ×1 500 | 12 |
| Signal ×1 | 2 | Schematic ×1 | 2 |
| Crate ×1 | 2 | Crate ×1 | 2 |
| Schematic ×1 | 1 | Signal ×1 | 1 |
| Awaken Core ×1 (extremely rare) | 0.05 | Awaken Core ×1 | 0.05 |
| **Featured hero: 50 shards** | **1.2** | **Featured mount: 50 shards** | **1.2** |

Signals / Crates / Schematics stay at 5% of the filler (an Active F2P player's ~2 600 hero and ~3 100 mount pulls a year add ~130 / ~155
Signal pulls), the Awaken Core at 0.05% (~1.3 + 1.6 a year next to ~60 from the arena shop, invasions and the Burrow).
Hero XP / Fuel stay close to the old Common / Rare value per pull. No Crests / Saddles in the filler (it would change their income).

## No starter mount

The Mounts unlock gives the mount summon event and its 10 tickets only (one line removed in `ContentDb.Onboarding`; the hero
starter was already gone). The `level-unlocks` branch owns the onboarding gates and the tutorial; nothing else changed there.

## Shard Reactor (Mythic building)

Fuses spare shards into shards of a **higher rarity of the same type**, of an item the player picks: alien shards → a rarer
alien's shards, blueprint shards → a rarer blueprint's shards (never across types, never the same or a lower rarity).

| | |
|---|---|
| Blueprint | `bp_shard_reactor`, Mythic, Utility, 2×2, in the Blueprint Signal's Mythic pool and its own banner ("Reactor Ignition") |
| Jobs | one job per alien working in the Reactor (the city's worker model): **1 worker slot, +1 at 3★, +1 at 6★ → up to 3 parallel jobs** |
| Job | 1–10 units; a unit makes **10 target shards** |
| Ratio | source shards per target shard: **×4 per rarity step** — 4 (1 step), 16 (2), 64 (3), 256 (Common → Mythic) |
| Fuel per unit (by target rarity) | aliens: Grain 200 / 500 / 1 200 / 3 000 (Rare / Epic / Legendary / Mythic); blueprints: Ore 300 / 800 / 2 000 / 5 000 |
| Time per unit (by target rarity) | 20 min / 45 min / 1 h 30 / 3 h; −1% per Reactor level above 1 (star scaled like other boons) |
| Timers | city timers: Diamonds / ad / daily free speed-ups apply |
| Collect | the target's shards; a target not owned yet unlocks once it holds 50 |
| Stars | the shared table; milestones 3★ / 6★ = worker slots; collection bonus Alien Power |

Commands `ReactorStart { Kind, SourceId, TargetId, Units }` (source shards and fuel paid at the start) and
`ReactorCollect { JobId }` (empty = every finished job). Counters `reactor_jobs_started`, `reactor_shards_in`,
`reactor_shards_out`. Screen: City → the Reactor → "Open the Shard Reactor" (`ReactorScreen`): jobs x/y, running jobs with timer,
speed-ups and Collect, then a new job: alien / blueprint tab, source shards (spare beyond 10★ in brackets), targets of a higher
rarity (rarity colours, stars or "not owned"), units, and the cost / time / output before Start.

## Old saves (`PullSystemMigration`, once per save, `GachaState.Migrated` = 1)

- **Stars are kept** (n★ stays n★). Mounts: stars + awakening (5★ A3 → 8★), awakening set to 0.
- **Loose shards keep their progress**, step by step: the old shards toward the next star cover a share of each old step; the
  same share of the same new steps is paid (rounded up; past the old 10★ cost, the last step's ratio). Old steps:
  heroes 40 (unlock) / 30 / 60 / 100 / 160 / 250 / 380 / 550 / 800 / 1 100; mounts 30 / 20 / 50 / 100 / 180, awakening 100 / 150 /
  200 / 260 / 330 as stars 6–10; aliens 1 copy, then 2 / 4 / 6 / 10 / 15 / 22 / 30 / 42 / 60; blueprints 1 copy, then 1 / 2 / 3 / 5 /
  8 / 12 / 18 / 26 / 36. Examples: a 4★ hero with 80 shards (half of the old 160) → 50 (half of the new 100); a 5★ A2 mount with
  100 shards → 7★ with 75; a 2★ alien with 2 shards → 25; a hero not owned with 40 shards → 50 (unlockable).
- **Pity counters:** the Signals' counters (hidden before) are the visible ones as they are (Mythic ≤ 49, Legendary ≤ 39). A summon
  Luck above the new 100 (the old max was 300 / 200) is capped at 99: the next pull is the guaranteed one.
- Rewards already in the mailbox or the reward stash keep their kind: a Hero / Mount / Blueprint / Alien reward is now a batch.

## Simulator (`--pulls`, 365 days, 3 seeds, median)

| Profile | Day | Heroes (owned · ★ sum / best) | Mounts | Aliens (owned · ★ · Mythic) | Blueprints (owned · ★ · Mythic) | Spare alien / bp shards | Reactor (owned · ★ · jobs) | Reactor shards in → out |
|---|---|---|---|---|---|---|---|---|
| Casual F2P | 30 / 60 / 120 / 365 | 1·1/1 · 4·5/2 · 8·15/3 · 9·39/6 | 0 · 2·2/1 · 4·7/2 · 9·21/4 | 17·41 · 19·67 · 20·133 · 20·200 (2 M) | 14·26 · 22·55 · 28·101 · 31·196 (4 M) | 0 · 0 · 250/300 · 6.7k/10.3k | –, –, 33%, 100% · 3★ · 28 | 21k → 500 |
| Active F2P | 30 / 60 / 120 / 365 | 3·4/2 · 7·12/3 · 9·25/4 · 9·59/8 | 3·3/1 · 5·8/2 · 8·17/3 · 9·51/7 | 19·78 · 20·132 · 20·169 · 20·200 | 17·49 · 26·96 · 30·145 · 31·278 | 0 · 950/0 · 10.6k/2.9k · 86k/10k | 0%, 33%, 100%, 100% · 7★ · 68 | 74k → 890 |
| Hardcore F2P | 30 / 60 / 120 / 365 | 3·4/3 · 8·16/3 · 9·27/4 · 9·65/8 | 4·5/2 · 7·10/3 · 9·24/4 · 9·60/8 | 20·96 · 20·146 · 20·185 · 20·200 | 23·67 · 28·117 · 30·163 · 31·310 | 0 · 2.5k/300 · 14k/6k · 129k/27k | 0%, 67%, 100%, 100% · 10★ · 200 | 145k → 2 050 |
| Active Dolphin | 30 / 60 / 120 / 365 | 4·6/2 · 8·13/3 · 9·25/4 · 9·59/8 | 4·4/2 · 5·8/2 · 9·20/4 · 9·59/8 | 20·105 · 20·157 · 20·188 · 20·200 | 24·64 · 28·115 · 29·170 · 31·309 | 1 · 4k/250 · 14k/5k · 127k/12k | 67%, 100%, 100%, 100% · 10★ · 179 | 134k → 1 880 |
| Active Whale | 30 / 60 / 120 / 365 | 4·16/5 · 9·40/5 · 9·66/8 · 9·90/10 | 3·5/2 · 7·12/3 · 9·28/4 · 9·66/8 | 20·117 · 20·163 · 20·193 · 20·200 | 29·133 · 31·184 · 31·258 · 31·310 | 352/1.3k · 5.9k/8k · 9.2k/16k · 149k/114k | 67%, 100%, 100%, 100% · 10★ · 200 | 137k → 2 140 |

Signal Mythics per year: Casual 72, Active 185, Hardcore 272, Dolphin 273, Whale 339 (1 per ~60 pulls, as published).

**Blueprint count (2026-11-27):** the table above predates the City redesign (31 blueprints, max 310★). The Blueprints pull
pool is every City blueprint: **24** since the Plate Foundry (Epic) was removed (max 240★). Re-run of `--pulls` on today's rules, blueprints owned · ★ sum at day 365: Casual F2P 24 · 175, Active
F2P 24 · 207, Hardcore F2P 24 · 221, Active Dolphin 24 · 217, Active Whale 24 · 235.

Reading it:
- Heroes: every hero is owned by day ~120 except Casual (day ~365); the best hero is 8★ at day 365 for active players, 10★ for the
  whale. Mounts track heroes a little behind (no starter mount: Casual owns none on day 30).
- **Aliens are all at 10★ within the first year for every profile** (200★ = 20 × 10 at day 365; Active F2P is at 169★ on day 120). A batch of 50 for
  every alien pulled is ~10× the old shard value per copy (old: 191 copies to 10★, new 19). Blueprints reach 10★ across the
  board in year one for active players (310★ = 31 × 10).
- **Excess flow:** spare shards appear from ~day 60 (aliens) and grow to 7k (Casual) … 150k (Whale) alien and 10k … 114k
  blueprint shards at day 365. The Reactor turns ~74k–145k spare shards a year into 900–2 100 shards of rarer items (mostly Common
  → Legendary / Mythic at 64–256 : 1). Once every alien / blueprint is at 10★ the Reactor has no target left and the spare shards
  have no sink (the bot's day-365 state for active profiles).
- The bot staffs the Reactor with its weakest workers, picks the rarest unfinished item of the type (owned first) and spends at
  most half its Grain / Ore per job.

## Mount Cores removed (owner, 2026-11-25)

"Mounts have no awakening and stars 6–10 come from shards, just like all the others." Mount stars cost exactly the shared star
table. Mount Cores had no other use, so the currency is gone (no conversion of old balances: early development). Its sources now
pay Fuel at 1 core = 300 Fuel (the mount event's filler value), or tokens' worth of Fuel:

| Source | Was | Now |
|---|---|---|
| Mount event filler (7%) | Mount Cores ×5 | Fuel ×1 500 (merged with the 5% Fuel ×1 500 entry: 12%) |
| Mount event milestone 80 | Mount Cores ×30 | Fuel ×9 000 |
| Mount exchange | Mount Cores ×10 for 100 tokens (limit 2) | removed; Fuel ×1 500 for 40 limit 10 → 15 (the same 200 tokens) |
| Burrow floors 6–10 | Mount Cores ×1 | Fuel ×300 |
| Dark Side clear | Mount Cores ×1–4 | Fuel ×300–1 200 |
| Guild shop | Mount Cores ×5 for 400 marks (2 / week) | Fuel ×1 500 for 400 marks (2 / week, `guild_fuel`) |

## Shard sources outside pulls (2026-11-25)

| Source | Amount | Decision |
|---|---|---|
| Hero event milestones (pulls 25 / 100) | ×20 / ×60 → **×5 / ×10** | tiny |
| Mount event milestone (pull 25) | ×10 → **×5** | tiny |
| Hero / mount exchange "Featured shards" | ×10 → **×5** per purchase (limits 3 / 2) | tiny |
| Hero / mount premium pass, last day | ×40 / ×20 → **×5** | tiny |
| Hero / mount milestone 150 / 120 | a featured batch (50) | kept: a reward for pulling 150 / 120 times in the event |
| Breeding (discard a child) | 1–3 alien shards | kept (already tiny) |
| Shard Reactor | converts spare shards | kept (made from shards, not a new source) |
| Feature-unlock gifts (Onboarding: blueprints, Blorp; the Launch Pod at the Aliens unlock) | an unlock copy each | kept (the unlock itself) |
| Run loot: campaign portals (~0.25% of loot rolls each: a Common Blueprint / Alien), Dark Side (~0.9% each, Rare) | a batch (50) or the unlock | kept, flagged for the owner |
| Quests, mail templates, shops, packs, arena, guild, invasions | none | – |

## Open questions for the owner (not changed, by instruction)

- Aliens and blueprints max in year one with 50-shard batches at every rarity: decide later whether low rarities should pay
  smaller batches or appear less (the math page's option), or whether a further sink for shards past 10★ is wanted.
- Run loot still drops whole Blueprint / Alien batches (campaign portals at a low weight, the Dark Side more often): kept, see
  "Shard sources outside pulls"; say if those should become Signal tokens.
- The run's level-up draft keeps its own hidden Legendary pity (GDD §6, in-run skills, not a pull); say if "no hidden pity
  anywhere" should cover it too.
- Summon events: their featured batch is the whole Mythic tier, so the 80 / 20 rule has no off-banner Mythic to point at (see
  "Featured 80 / 20"). Add an off-banner Mythic (another hero / mount's batch) if the rule should bite there too.
