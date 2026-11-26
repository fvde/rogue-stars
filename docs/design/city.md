# City (redesign, rules part)

Implements the owner-approved City design (section 6 "Every building" is authoritative). Code: `Content/ContentDb.City.cs`,
`Systems/City/CityState.cs`, `CitySystem.cs`, `CityLayout.cs`, `CityPlanner.cs`, `CityCommands.cs`. Tests:
`shared/RogueStars.Rules.Tests/City/` (CityTests, CityLayoutTests, CityPullTests). Sim results: `docs/balance/data/city_city.md`.

## Buildings

25 blueprints, footprint and copies by rarity (rotation swaps width and height):

| Rarity | Footprint | Copies | Production worker slots |
|---|---|---|---|
| Common | 1×1 | 3 (Launch Pod 1) | 1 (+1 at 5★) |
| Rare | 2×1 | 3 | 2 (+1 at 5★) |
| Epic | 2×2 | 2 | 2 (+1 at 5★) |
| Legendary | 3×2 | 2 | 2 (+1 at 5★) |
| Mythic | 3×3 | 1 | – |

Other buildings have 1 worker slot; Shard Reactor and Launch Pod (= squad slots) +1 at 5★ and 10★.

**Stasis Beacon** (owner, 2026-11-25: too strong stacked): Mythic, 1 copy, 3×3; boss-awakening slow 10% → 30% at level 10 / 10★, so
the maximum with the +30% placement cap is 39% (≤ 40%). Other stacking (War Academy, Scout Post: +1 flat each copy) stays.

Uids are `<blueprintId>#<n>` (n = 1..copies). Stars live on the blueprint and are shared by every copy; each copy has its own
level (1–10), timers, storage, chances and workers.

**Growth.** value = placed value × (1 + level ladder + star ladder); each ladder +10% per step and +15% at 5 and 10 (0 → 100%),
so a maxed building does 3×. Boons that grow with levels only: Launch Pod alien damage, Laboratory research speed. Slots
(squad, research, breeding, workers) come from star unlocks. Whole-number boons (rerolls, skips, stamina, run coins, slots)
round down per copy. No collection bonus.

**Runs only when staffed.** A building produces or gives its boon while placed, built and staffed by at least one alien. The
Launch Pod runs when built; its aliens are the squad and work nowhere else. Copies add up for everything except squad slots.

## The crash site comes first (owner, 2026-11-25)

Aliens unlock at level 6 but everything they do used to live in the City (level 15). Now the **Aliens unlock brings the Launch
Pod** (blueprint 1★) and its first copy stands **built at once** (level 1, no build timer) on the crash site:
`CitySystem.EnsureCrashSitePod` places it at `CityPlanner.Find`'s spot with no new streets and off the fireplace's street axes
(`CityPlanner.SpineCells`), i.e. touching the 3×3 plaza inside the 30 start tiles: **(8, 7)**, north of the plaza, west of the
axis, for every player (deterministic). An idle alien (the first, Blorp) takes the seat, so the squad flies from level 6. It runs
at the end of every unlock check (`OnboardingSystem.CheckUnlocks`), so a save that already had the Aliens unlock gets its pod on
load the same way (no migration; its Launch Pod feature unlocks then with its own moment). Dev unlock-all states without a
recorded Aliens unlock get none.

* **Camp mode (levels 6-14, owner 2026-11-26).** The City screen exists from level 6 in a restricted "camp" mode: only the crash-site
  plaza with the fireplace, the saucer and the Launch Pod on the start land, the squad aliens walking around. No digging, no Build
  mode, streets, Workers (other buildings), Output or Collect; the only interaction is the pod's card with its seats. The rules
  refuse everything else before the City feature unlocks ("feature locked"): `CityUpgrade` (and with it level-ups), `BlueprintStarUp`,
  `CityMove`, `CityStore`, `CityPlace`, `CityStreets`, `CityDig`, for the pod and every other building. Only the seats work:
  `AlienAssign` to / from the pod and `AlienSetSquad`. The pod can neither be upgraded, starred nor moved yet.
* **One hub entry.** The hub has one tab for it: the "Camp" tab with the crashed-saucer icon from level 6, the "City" tab with the
  City icon from level 15 (same screen). The crash-site props on the planet are decoration and a shortcut to the same screen.
* **At the City** (level 15) the camp becomes the full City: the pod already stands on the crash site, the land around it is buildable,
  and the pod can be moved, upgraded and starred like any building.
  A pulled Launch Pod later is a batch of shards (one copy).
* **Features:** the Launch Pod feature (`alien_squad`) is no longer a building feature: it unlocks at level 6 in the Aliens' group
  (one unlock moment "Aliens + Launch Pod": the Aliens' 2 panels, then the pod's 3; the Aliens' 50 Diamonds and the pod's 30).
  The pod blueprint is not an unlock grant (blueprint rewards wait for the City in the reward gate); EnsureCrashSitePod gives it.

## Costs

`BuildingCostType` per blueprint (8 Biomass only, 7 Ore only, 10 both). Single-currency buildings pay 3× the Biomass part or
1.5× the Ore part. New copy (both): C 60 + 40 · R 300 + 200 · E 1 200 + 800 · L 4 500 + 3 000 · M 15 000 + 10 000.
First level-up (both): C 50 + 30 · R 200 + 120 · E 800 + 500 · L 2 800 + 1 800 · M 9 000 + 6 000 (= the design's table).

**Level curve (9 level-ups):** cost of level L → L+1 = first level-up × `LevelCostMult[L−1]` =
×1, 2, 3, 5, 9, 15, 25, 45, 80 (185× the first cost in all), rounded to two significant digits (multiples of 5 below 100).
Example Biomass Farm (Biomass only): 150, 300, 450, 750, 1 400, 2 300, 3 800, 6 800, 12 000. Level-up time ×1.25 per level.
The curve is steep on purpose: with 10 levels instead of 32, Legendary / Mythic level-ups are the long-term Biomass and Ore sink
next to the digs.

## Land, streets, digging

20×20 grid (`ContentDb.CityMap`): 30 start tiles (the 3×3 plaza at 8..10 with the fireplace at 9,9, plus 21 around), 20
patches of 7–16 tiles (210), 240 land tiles. Ten terrain features (4 Crystal Outcrops, 4 Springs, 2 Old Ruins) are obstacles.

* A patch can be dug once it side-touches the land; any order. Dig costs and the slower production are kept (owner, 2026-11-25:
  no producer ×2, see docs/balance/progression-report.md "City redesign"). Cost and timer by dig number (`CityTuning.Digs`, the dig table:
  2 500 Biomass + 750 Ore / 1 min for #1 … 600 000 + 150 000 + 12 000 Gear Cores / 24 h for #20). One dig at a time; timer
  speed-ups apply. `FreeDigs` (from the reset) are instant and free.
* Streets are 1×1 on free land and must link back to the plaza (side steps). Buildings need a footprint tile side-touching a
  linked street or the plaza; corners never count. Nothing overlaps; nothing on features or the plaza.

## Placement skills

One skill per building (none: Launch Pod, Laboratory, Shard Reactor), a multiplier on its main number (output, chance speed,
boon value), own skill + auras capped at +30%. Scout Post and War Academy give +1 flat instead. Kinds: Neighbours (per match,
"tag:farm" / "cat:Housing" / blueprint id), Melting Pot, Row of Three, Diagonal, Across the Street (target directly opposite a
1-wide street), Hearth (≤ N tiles from the fireplace), Outskirts (≥ N), Open Ground (N+ empty land tiles touching), Cliffside
(touches undug / void), Aura (Barracks Dome: +10% to touching buildings while it runs), Touch plaza (Command Spire).
Distances: straight steps from the closest footprint tile. Bonuses are cached per `CityState.LayoutRev`.

Readings confirmed by the owner (2026-11-25): the Barracks Dome aura counts toward the +30% cap; the Command Spire's production
bonus and the production priority do not apply to pull buildings; free digs from the reset are instant.

## Pull buildings

Blueprint Relay, Alien Signal Beacon, Part Fabricator make chances (Research Scrolls, Incoming Transmissions, Meteor Drops):
0.5 a day × (1 + level ladder + 10% per star 2..4) × (1 + placement); storage 3 × (1 + level ladder) (3 → 6). Work bonus and
city production bonuses do not apply. A pop rolls 1..3 pulls on the star odds (`CityTuning.PopOdds`: 100% one pull up to 4★,
90/10 at 5★ … 70/30 at 9★, 70/20/10 at 10★) with the player RNG and grants the token currency.

## Commands and queries (client API)

Commands (one per edit, applied at once; undo = send the reverse command):

| Command | Fields | Notes |
|---|---|---|
| `CityPlace` | BlueprintId, Uid?, X, Y, Rotated | Uid = a stored copy (free, instant); empty = new copy (pays, starts construction). Payload: uid |
| `CityMove` | Uid, X, Y, Rotated | free, keeps everything |
| `CityStore` | Uid | back to the tray (level kept, storage collected, workers idle); a copy still under its first construction is cancelled and refunded |
| `CityStreets` | Adds, Removes (cells y×20+x) | one stroke |
| `CityDig` | PatchId | |
| `CityUpgrade` | Uid | |
| `CityCollect` | Uid? | empty = all |
| `CityPop` | Uid?, All | empty Uid = pop everything; payload `List<CityPopResult>` |
| `CitySetPriority`, `BlueprintStarUp` | as before | |
| `AlienAssign` | AlienTypeId, BuildingUid? | the Launch Pod = squad |
| `AlienSetSquad` | AlienTypeIds | moves them into the Launch Pod |

Queries: `CityLayout.Validate(state, content, CityEdit)` (the same check the handlers run; `CityCheck.Error`, `BadCells`,
`BadUids` for red flashes), `CityLayout.Preview(..., out pct, out flat)` (ghost bonus), `CityLayout.Grid(state, content, edit?)`
(cell contents: Land / Plaza / Feature / Street / Owner → Items), `CityLayout.LinkedStreets`, `CityLayout.PlacementPct /
PlacementFlat(uid)`, `CityLayout.IsLand`, `CityLayout.Cell/CellX/CellY`; `CitySystem.Building(uid)`, `Copies`, `CopiesBuilt`,
`IsBuilt`, `IsActive`, `WorkerSlots`, `Workers(uid)`, `BoonValue(building, kind)`, `ProductionPerHour`, `StorageCap`,
`ChancesPerDay`, `ChanceStorage`, `ChancesReady`, `MaxPullsPerPop`, `PopOdds`, `BuildCost`, `UpgradeCost`,
`NextDigNumber`, `DigDef`, `DigCost`, `DigBlockReason(patchId)`, `LaunchPod`, `Laboratory`, `SquadSlots`; `CityPlanner.Find`
(a valid spot with up to two new street tiles, best bonus first) for an editor "suggest" button.

## Unlocks (owner, 2026-11-25: unlocks come with pulls)

| Unlock | Level | Comes with |
|---|---|---|
| Parts | 1 | the Part Signal (part pulls) and one free 10× Part pull |
| Aliens | 6 | the first alien, the Alien Signal (alien pulls), one free 10× Alien pull and **the Launch Pod, built on the crash site** with the first alien in its seat (the Launch Pod feature unlocks in the same moment) |
| City | 15 | the Biomass Farm blueprint, the Blueprint Signal and one free 10× Blueprint pull (a normal 10×: the Launch Pod came with the Aliens) |

The separate Part Signal (10), Alien Signal (13) and Blueprint Signal (20) unlocks are gone (`Features.PartPulls / AlienPulls /
Blueprints` are now the same ids as Parts / Aliens / City). **Buildings have no level:** the Laboratory, Grain Farm, Ore Mine and
Breeding Pen features unlock the moment their blueprint is owned (`RequirementKind.Blueprint`, after the City), with their intro
and gift (the Laboratory's 60 Data); the hub teaser skips them. The Launch Pod is the exception: it comes with the Aliens (above).
Research needs the Laboratory **pulled, built and staffed** (cost unchanged; the friction is intended). Free pulls:
`GachaState.FreeTenPulls[type]`, spent with `GachaPull { Count = 10, Payment = GachaPayment.Free }`; none of them has a
guarantee any more (`GachaTypeDef.FreePullGuarantee` is removed).

Sim (9 profiles × 3 seeds × 730 days, `docs/balance/data/city_city.md` "Building blueprints", before the crash site): the Launch Pod
came with the City (day 1–14; now with the Aliens: Active F2P day 1, Casual F2P day 5, see progression-report.md "Crash site"),
the Laboratory is pulled on day 2–5 by Hardcore players and spenders, ~19 by Active F2P and ~110 by Casual F2P (first
research a day to ~2 months after; see progression-report.md "City follow-up").

Blueprint banners (see pull-system.md "Banner schedule"): a weekly rotation from Monday 00:00 UTC whose **core blueprint banner**
(Laboratory in Mythic; Blueprint Relay, Alien Signal Beacon, Part Fabricator in Legendary; nothing else) returns every other week.

## Old saves

Pre-release, no layout migration: on the first settle of a save with `CityState.Version` 0 every built building goes to
storage with its level (capped at 10) and stars, the Housing Pod becomes the Launch Pod, removed blueprints are dropped, aliens
go idle, streets and digs are cleared and the player gets one free dig per old area unlocked beyond the first.
