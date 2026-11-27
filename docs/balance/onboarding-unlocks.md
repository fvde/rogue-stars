# Level unlocks: simulated unlock times

Generated with `dotnet run --project tools/RogueStars.Sim -c Release -- --unlocks --days 30 --seeds 3` (bots play the real
rules; Casual = 2 × 7.5 min/day, Active = 4 × 15 min ≈ 1 h/day, Hardcore = 7 sessions ≈ 3 h/day, Active Whale = Active with
the whale spend profile). Gate = the main level cleared that unlocks the feature (`ContentDb.Onboarding.cs`, GDD §16): no
account-day gate and no daily limit any more, so every profile gets each feature the moment it clears the level, and
the minutes / runs / days below only reflect how fast it plays. "(run)" = run content introduced before the first board
that has it. Lab research rows: when the package's research finished (it needs the package's level unlock, Data and time).

Regenerated 2026-11-26. Daily Quests are no feature (no unlock moment, no row): the hub button appears with Parts, level 1. The schedule
below is the current one (Parts 1, Shops 2, Worm 3, ..., Aliens 6, City 15, ...; building features unlock with their blueprint).

Novelty: the first played hour brings 5–9 new things (meta and run) per profile, the longest stretch without one in
the first hour is 15–21 minutes; past level 20 the gaps widen on purpose (one feature every 2, later 3–4 levels).

## Feature unlocks: minutes played · runs · main level · account day (median of seeds; – = not unlocked)

| Feature | Gate | Casual F2P | Active F2P | Hardcore F2P | Active Whale |
|---|---|---|---|---|---|
| Parts | L1 | 4 min · 0 runs · L1 · D1 | 6 min · 0 runs · L1 · D1 | 6 min · 0 runs · L1 · D1 | 2 min · 0 runs · L1 · D1 |
| Shops (run) | L2 | 11 min · 1 runs · L2 · D1 | 8 min · 1 runs · L2 · D1 | 8 min · 1 runs · L2 · D1 | 5 min · 1 runs · L2 · D1 |
| Worm Level & Talents | L3 | 25 min · 2 runs · L3 · D2 | 17 min · 2 runs · L3 · D1 | 30 min · 5 runs · L3 · D1 | 8 min · 2 runs · L3 · D1 |
| Minibosses & Mythics (run) | L4 | 40 min · 3 runs · L4 · D3 | 21 min · 3 runs · L4 · D1 | 34 min · 6 runs · L4 · D1 | 17 min · 3 runs · L4 · D1 |
| Two Dice & Forks (run) | L5 | 56 min · 4 runs · L5 · D4 | 26 min · 4 runs · L5 · D1 | 38 min · 7 runs · L5 · D1 | 20 min · 4 runs · L5 · D1 |
| Aliens | L6 | 71 min · 5 runs · L6 · D5 | 35 min · 5 runs · L6 · D1 | 42 min · 8 runs · L6 · D1 | 25 min · 5 runs · L6 · D1 |
| Launch Pod | L6 | 71 min · 5 runs · L6 · D5 | 35 min · 5 runs · L6 · D1 | 42 min · 8 runs · L6 · D1 | 25 min · 5 runs · L6 · D1 |
| Auto Roll | L7 | 90 min · 6 runs · L7 · D6 | 40 min · 6 runs · L7 · D1 | 50 min · 9 runs · L7 · D1 | 35 min · 6 runs · L7 · D1 |
| Campfires (run) | L9 | 123 min · 8 runs · L9 · D8 | 57 min · 8 runs · L9 · D1 | 67 min · 11 runs · L9 · D1 | 49 min · 8 runs · L9 · D1 |
| Signature Choice (run) | L11 | 157 min · 10 runs · L11 · D10 | 86 min · 10 runs · L11 · D2 | 79 min · 13 runs · L11 · D1 | 68 min · 10 runs · L11 · D2 |
| Alien Arcade (run) | L14 | 207 min · 13 runs · L14 · D13 | 111 min · 14 runs · L14 · D2 | 120 min · 18 runs · L14 · D1 | 90 min · 13 runs · L14 · D2 |
| City | L15 | 225 min · 14 runs · L15 · D14 | 120 min · 15 runs · L15 · D2 | 174 min · 26 runs · L15 · D1 | 98 min · 14 runs · L15 · D2 |
| Laboratory | L0 | – | 879 min · 83 runs · L26 · D12 | 202 min · 29 runs · L17 · D1 | 279 min · 32 runs · L21 · D5 |
| Heroes | L18 | 284 min · 17 runs · L18 · D17 | 199 min · 21 runs · L18 · D3 | 222 min · 31 runs · L18 · D2 | 127 min · 17 runs · L18 · D2 |
| Grain Farm | L0 | 235 min · 15 runs · L15 · D14 | 141 min · 17 runs · L15 · D2 | 200 min · 29 runs · L16 · D1 | 115 min · 16 runs · L16 · D2 |
| Ore Mine | L0 | 232 min · 15 runs · L15 · D14 | 152 min · 17 runs · L15 · D3 | 201 min · 29 runs · L16 · D1 | 114 min · 16 runs · L16 · D2 |
| Ice Package | L24 | 413 min · 23 runs · L24 · D23 | 488 min · 47 runs · L24 · D7 | 854 min · 101 runs · L24 · D5 | 304 min · 34 runs · L24 · D5 |
| Mounts | L26 | 458 min · 25 runs · L26 · D25 | 765 min · 74 runs · L26 · D11 | 1221 min · 135 runs · L26 · D7 | 379 min · 41 runs · L26 · D6 |
| Runes | L28 | 548 min · 29 runs · L28 · D29 | 994 min · 99 runs · L28 · D14 | 1737 min · 194 runs · L28 · D9 | 645 min · 67 runs · L28 · D10 |
| Funds | L30 | – | 1017 min · 101 runs · L30 · D14 | 2469 min · 264 runs · L30 · D11 | 665 min · 69 runs · L30 · D10 |
| Arena | L32 | – | 1218 min · 117 runs · L32 · D16 | 2469 min · 264 runs · L32 · D13 | 852 min · 88 runs · L32 · D11 |
| Breeding Pen | L0 | 235 min · 15 runs · L15 · D14 | 717 min · 71 runs · L27 · D10 | 800 min · 96 runs · L22 · D5 | 278 min · 32 runs · L21 · D5 |
| Season Pass | L36 | – | 1642 min · 151 runs · L36 · D21 | 3404 min · 367 runs · L36 · D15 | 990 min · 99 runs · L38 · D13 |
| Lightning Package | L38 | – | 1657 min · 151 runs · L38 · D22 | 3404 min · 413 runs · L38 · D17 | 990 min · 99 runs · L38 · D14 |
| Overcharged Levels | L40 | – | 1804 min · 166 runs · L40 · D23 | 3784 min · 467 runs · L40 · D18 | 1032 min · 103 runs · L42 · D14 |
| Guilds | L42 | – | 1944 min · 179 runs · L42 · D26 | 3784 min · 467 runs · L42 · D19 | 1032 min · 103 runs · L42 · D14 |
| Invasions | L44 | – | 2271 min · 212 runs · L45 · D30 | 4620 min · 545 runs · L44 · D22 | 1090 min · 107 runs · L46 · D15 |
| Burrow | L46 | – | – | 4838 min · 645 runs · L46 · D24 | 1090 min · 107 runs · L46 · D15 |
| Counter Package | L48 | – | – | 5655 min · 756 runs · L48 · D27 | 1389 min · 118 runs · L50 · D17 |
| Forge | L50 | – | – | 5020 min · 644 runs · L52 · D24 | 1389 min · 118 runs · L50 · D19 |
| Dark Side of the Planet | L53 | – | – | – | 1725 min · 134 runs · L54 · D21 |
| Alien Package | L56 | – | – | – | 1801 min · 137 runs · L56 · D24 |
| Time Package | L60 | – | – | – | – |
| Lab research done: Combo Package | – | – | 1269 min · 121 runs · L30 · D17 | 2172 min · 233 runs · L29 · D11 | 724 min · 76 runs · L31 · D10 |
| Lab research done: Ice Package | – | – | – | – | 1725 min · 134 runs · L56 · D24 |
| Lab research done: Lightning Package | – | – | – | – | 1840 min · 138 runs · L56 · D25 |
| Lab research done: Counter Package | – | – | – | – | 2081 min · 141 runs · L58 · D29 |
| Lab research done: Alien Package | – | – | – | – | 2099 min · 141 runs · L58 · D29 |
| Lab research done: Time Package | – | – | – | – | – |

## Novelty gaps (played minutes; median of seeds)

New things = feature unlocks (meta and run content). Longest gap = the longest stretch of play without one.

| Profile | new in first 30 min | new in first 60 min | new in first 120 min | new in first 180 min | longest gap, first 60 min | longest gap, first 120 min | longest gap, first 180 min | median gap, first 180 min |
|---|---|---|---|---|---|---|---|---|
| Casual F2P | 3 | 5 | 8 | 11 | 15 | 30 | 35 | 15 |
| Active F2P | 5 | 9 | 12 | 15 | 16 | 28 | 46 | 7 |
| Hardcore F2P | 3 | 8 | 11 | 13 | 21 | 29 | 54 | 7 |
| Active Whale | 7 | 9 | 15 | 16 | 14 | 20 | 53 | 7 |

Main level at the end of each day (median):

| Profile | day 1 | day 2 | day 3 | day 4 | day 5 | day 7 | day 10 | day 14 | day 21 | day 28 |
|---|---|---|---|---|---|---|---|---|---|---|
| Casual F2P | 2 | 3 | 4 | 5 | 6 | 8 | 11 | 15 | 22 | 27 |
| Active F2P | 9 | 15 | 18 | 20 | 21 | 24 | 25 | 30 | 37 | 43 |
| Hardcore F2P | 16 | 18 | 20 | 22 | 24 | 26 | 29 | 35 | 43 | 48 |
| Active Whale | 10 | 18 | 22 | 23 | 25 | 27 | 31 | 43 | 53 | 58 |

