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

Uids are `<blueprintId>#<n>` (n = 1..copies). Stars live on the blueprint and are shared by every copy; each copy has its own
level (1–10), timers, storage, chances and workers.

**Growth.** value = placed value × (1 + level ladder + star ladder); each ladder +10% per step and +15% at 5 and 10 (0 → 100%),
so a maxed building does 3×. Boons that grow with levels only: Launch Pod alien damage, Laboratory research speed. Slots
(squad, research, breeding, workers) come from star unlocks. Whole-number boons (rerolls, skips, stamina, run coins, slots)
round down per copy. No collection bonus.

**Runs only when staffed.** A building produces or gives its boon while placed, built and staffed by at least one alien. The
Launch Pod runs when built; its aliens are the squad and work nowhere else. Copies add up for everything except squad slots.

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

* A patch can be dug once it side-touches the land; any order. Cost and timer by dig number (`CityTuning.Digs`, the dig table:
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

## Unlocks

Aliens at level 6 (first alien), Alien Signal 13, the City 15 (Biomass Farm), Launch Pod 16, Laboratory 17 (research is locked
until the Laboratory building is placed and built; its slots need an alien), Grain Farm 19.

## Old saves

Pre-release, no layout migration: on the first settle of a save with `CityState.Version` 0 every built building goes to
storage with its level (capped at 10) and stars, the Housing Pod becomes the Launch Pod, removed blueprints are dropped, aliens
go idle, streets and digs are cleared and the player gets one free dig per old area unlocked beyond the first.
