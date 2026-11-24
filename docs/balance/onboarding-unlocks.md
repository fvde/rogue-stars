# Level unlocks: simulated unlock times

Generated with `dotnet run --project tools/RogueStars.Sim -c Release -- --unlocks --days 30 --seeds 3` (bots play the real
rules; Casual = 2 × 7.5 min/day, Active = 4 × 15 min ≈ 1 h/day, Hardcore = 7 sessions ≈ 3 h/day, Active Whale = Active with
the whale spend profile). Gate = the main level cleared that unlocks the feature (`ContentDb.Onboarding.cs`, GDD §16): no
account-day gate and no daily limit any more, so every profile gets each feature the moment it clears the level, and
the minutes / runs / days below only reflect how fast it plays. "(run)" = run content introduced before the first board
that has it. Lab research rows: when the package's research finished (it needs the package's level unlock, Data and time).

Novelty: the first played hour brings 7–10 new things (meta and run) per profile, the longest stretch without one in
the first hour is 8–20 minutes; past level 20 the gaps widen on purpose (one feature every 2, later 3–4 levels).

## Feature unlocks: minutes played · runs · main level · account day (median of seeds; – = not unlocked)

| Feature | Gate | Casual F2P | Active F2P | Hardcore F2P | Active Whale |
|---|---|---|---|---|---|
| Parts | L1 | 4 min · 0 runs · L1 · D1 | 6 min · 0 runs · L1 · D1 | 6 min · 0 runs · L1 · D1 | 2 min · 0 runs · L1 · D1 |
| Shops (run) | L2 | 10 min · 1 runs · L2 · D1 | 8 min · 1 runs · L2 · D1 | 8 min · 1 runs · L2 · D1 | 5 min · 1 runs · L2 · D1 |
| Worm Level & Talents | L3 | 19 min · 2 runs · L3 · D2 | 16 min · 2 runs · L3 · D1 | 29 min · 5 runs · L3 · D1 | 8 min · 2 runs · L3 · D1 |
| Minibosses & Mythics (run) | L4 | 27 min · 3 runs · L4 · D2 | 20 min · 3 runs · L4 · D1 | 33 min · 6 runs · L4 · D1 | 16 min · 3 runs · L4 · D1 |
| Two Dice & Forks (run) | L5 | 37 min · 4 runs · L5 · D3 | 26 min · 4 runs · L5 · D1 | 37 min · 7 runs · L5 · D1 | 20 min · 4 runs · L5 · D1 |
| City: Biomass Farm | L6 | 46 min · 5 runs · L6 · D3 | 34 min · 5 runs · L6 · D1 | 43 min · 8 runs · L6 · D1 | 25 min · 5 runs · L6 · D1 |
| Auto Roll | L7 | 58 min · 6 runs · L7 · D4 | 40 min · 6 runs · L7 · D1 | 49 min · 9 runs · L7 · D1 | 34 min · 6 runs · L7 · D1 |
| Daily Goals | L8 | 66 min · 7 runs · L8 · D4 | 49 min · 7 runs · L8 · D1 | 58 min · 10 runs · L8 · D1 | 39 min · 7 runs · L8 · D1 |
| Campfires (run) | L9 | 77 min · 8 runs · L9 · D5 | 54 min · 8 runs · L9 · D1 | 69 min · 12 runs · L9 · D1 | 45 min · 8 runs · L9 · D1 |
| Part Signal | L10 | 88 min · 9 runs · L10 · D5 | 87 min · 11 runs · L10 · D2 | 85 min · 14 runs · L10 · D1 | 55 min · 9 runs · L10 · D1 |
| Signature Choice (run) | L11 | 106 min · 10 runs · L11 · D6 | 107 min · 13 runs · L11 · D2 | 116 min · 18 runs · L11 · D1 | 73 min · 11 runs · L11 · D2 |
| Shop | L12 | 122 min · 11 runs · L12 · D7 | 112 min · 14 runs · L12 · D2 | 124 min · 19 runs · L12 · D1 | 79 min · 12 runs · L12 · D2 |
| Laboratory | L13 | 137 min · 12 runs · L13 · D8 | 123 min · 15 runs · L13 · D2 | 140 min · 21 runs · L13 · D1 | 87 min · 13 runs · L13 · D2 |
| Alien Arcade (run) | L14 | 171 min · 14 runs · L15 · D9 | 129 min · 16 runs · L14 · D2 | 162 min · 25 runs · L14 · D1 | 92 min · 14 runs · L14 · D2 |
| Aliens | L15 | 171 min · 14 runs · L15 · D10 | 144 min · 17 runs · L15 · D3 | 171 min · 26 runs · L15 · D1 | 103 min · 15 runs · L15 · D2 |
| Housing (Squad Slot 1) | L16 | 206 min · 16 runs · L17 · D11 | 165 min · 19 runs · L17 · D3 | 180 min · 27 runs · L16 · D1 | 110 min · 16 runs · L16 · D2 |
| Grain Farm | L17 | 206 min · 16 runs · L17 · D12 | 165 min · 19 runs · L17 · D3 | 205 min · 29 runs · L18 · D2 | 129 min · 18 runs · L18 · D2 |
| Heroes | L18 | 244 min · 18 runs · L19 · D13 | 200 min · 22 runs · L19 · D3 | 237 min · 33 runs · L18 · D2 | 129 min · 18 runs · L18 · D2 |
| Alien Signal | L19 | 244 min · 18 runs · L19 · D14 | 200 min · 22 runs · L19 · D3 | 274 min · 36 runs · L20 · D2 | 149 min · 20 runs · L20 · D3 |
| Blueprint Signal | L20 | 310 min · 22 runs · L22 · D15 | 277 min · 29 runs · L22 · D3 | 315 min · 41 runs · L20 · D2 | 158 min · 21 runs · L20 · D3 |
| Ore Mine | L22 | 310 min · 22 runs · L22 · D17 | 277 min · 29 runs · L22 · D4 | 455 min · 55 runs · L24 · D3 | 194 min · 24 runs · L24 · D3 |
| Ice Package | L24 | 373 min · 25 runs · L24 · D19 | 342 min · 35 runs · L25 · D5 | 464 min · 57 runs · L24 · D3 | 199 min · 25 runs · L24 · D3 |
| Mounts | L26 | 397 min · 26 runs · L26 · D21 | 388 min · 38 runs · L26 · D6 | 638 min · 77 runs · L26 · D4 | 278 min · 32 runs · L26 · D4 |
| Runes | L28 | 470 min · 28 runs · L28 · D24 | 529 min · 48 runs · L28 · D8 | 909 min · 100 runs · L28 · D5 | 292 min · 34 runs · L28 · D5 |
| Funds | L30 | 558 min · 32 runs · L30 · D28 | 622 min · 58 runs · L30 · D9 | 1003 min · 110 runs · L30 · D5 | 342 min · 40 runs · L30 · D5 |
| Arena | L32 | – | 772 min · 72 runs · L32 · D11 | 1518 min · 160 runs · L32 · D8 | 476 min · 50 runs · L32 · D7 |
| Breeding Pen | L34 | – | 934 min · 81 runs · L34 · D13 | 1700 min · 180 runs · L34 · D9 | 525 min · 54 runs · L34 · D8 |
| Season Pass | L36 | – | 1076 min · 92 runs · L36 · D14 | 1926 min · 193 runs · L36 · D10 | 562 min · 57 runs · L36 · D8 |
| Lightning Package | L38 | – | 1229 min · 104 runs · L38 · D17 | 2087 min · 206 runs · L38 · D10 | 595 min · 60 runs · L38 · D8 |
| Overcharged Levels | L40 | – | 1445 min · 117 runs · L40 · D20 | 2140 min · 209 runs · L40 · D11 | 632 min · 62 runs · L40 · D9 |
| Guilds | L42 | – | 1582 min · 131 runs · L42 · D22 | 2881 min · 309 runs · L42 · D15 | 672 min · 65 runs · L42 · D9 |
| Invasions | L44 | – | 1814 min · 148 runs · L44 · D25 | 3445 min · 419 runs · L44 · D18 | 706 min · 68 runs · L44 · D10 |
| Burrow | L46 | – | 1884 min · 152 runs · L46 · D26 | 4086 min · 538 runs · L46 · D21 | 749 min · 77 runs · L46 · D11 |
| Counter Package | L48 | – | 2052 min · 156 runs · L48 · D28 | 4312 min · 562 runs · L48 · D22 | 889 min · 79 runs · L48 · D12 |
| Forge | L50 | – | – | 4669 min · 610 runs · L50 · D23 | 984 min · 87 runs · L50 · D14 |
| Dark Side of the Planet | L53 | – | – | 5168 min · 666 runs · L53 · D26 | 1120 min · 90 runs · L53 · D15 |
| Alien Package | L56 | – | – | 5773 min · 736 runs · L56 · D29 | 1282 min · 96 runs · L56 · D17 |
| Time Package | L60 | – | – | – | 1433 min · 102 runs · L60 · D19 |
| Lab research done: Combo Package | – | 153 min · 13 runs · L13 · D9 | 155 min · 18 runs · L15 · D3 | 195 min · 28 runs · L16 · D2 | 119 min · 17 runs · L16 · D2 |
| Lab research done: Ice Package | – | 373 min · 25 runs · L24 · D20 | 356 min · 36 runs · L25 · D5 | 517 min · 63 runs · L24 · D3 | 292 min · 33 runs · L26 · D5 |
| Lab research done: Lightning Package | – | – | 1341 min · 110 runs · L39 · D18 | 2169 min · 212 runs · L40 · D11 | 730 min · 73 runs · L45 · D10 |
| Lab research done: Counter Package | – | – | – | 4601 min · 601 runs · L49 · D23 | 1259 min · 98 runs · L56 · D17 |
| Lab research done: Alien Package | – | – | – | 5836 min · 736 runs · L57 · D30 | 1313 min · 98 runs · L58 · D17 |
| Lab research done: Time Package | – | – | – | – | 1501 min · 103 runs · L61 · D20 |

## Novelty gaps (played minutes; median of seeds)

New things = feature unlocks (meta and run content). Longest gap = the longest stretch of play without one.

| Profile | new in first 30 min | new in first 60 min | new in first 120 min | new in first 180 min | longest gap, first 60 min | longest gap, first 120 min | longest gap, first 180 min | median gap, first 180 min |
|---|---|---|---|---|---|---|---|---|
| Casual F2P | 4 | 7 | 11 | 15 | 11 | 18 | 33 | 10 |
| Active F2P | 5 | 9 | 12 | 17 | 8 | 33 | 46 | 7 |
| Hardcore F2P | 3 | 8 | 11 | 16 | 20 | 26 | 39 | 7 |
| Active Whale | 6 | 10 | 16 | 20 | 10 | 17 | 31 | 6 |

Main level at the end of each day (median):

| Profile | day 1 | day 2 | day 3 | day 4 | day 5 | day 7 | day 10 | day 14 | day 21 | day 28 |
|---|---|---|---|---|---|---|---|---|---|---|
| Casual F2P | 2 | 4 | 6 | 8 | 10 | 12 | 15 | 19 | 26 | 30 |
| Active F2P | 9 | 14 | 20 | 22 | 25 | 27 | 31 | 36 | 41 | 47 |
| Hardcore F2P | 16 | 20 | 25 | 27 | 30 | 31 | 38 | 41 | 47 | 55 |
| Active Whale | 10 | 18 | 24 | 27 | 30 | 33 | 45 | 51 | 63 | 72 |

