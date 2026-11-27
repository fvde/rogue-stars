# Level unlocks: simulated unlock times

Generated with `dotnet run --project tools/RogueStars.Sim -c Release -- --unlocks --days 30 --seeds 3` (bots play the real
rules; Casual = 2 × 7.5 min/day, Active = 4 × 15 min ≈ 1 h/day, Hardcore = 7 sessions ≈ 3 h/day, Active Whale = Active with
the whale spend profile). Gate = the main level cleared that unlocks the feature (`ContentDb.Onboarding.cs`, GDD §16): no
account-day gate and no daily limit any more, so every profile gets each feature the moment it clears the level, and
the minutes / runs / days below only reflect how fast it plays. "(run)" = run content introduced before the first board
that has it. Lab research rows: when the package's research finished (it needs the package's level unlock, Data and time).

Regenerated 2026-11-27. Daily Quests are no feature (no unlock moment, no row): the hub button appears with Parts, level 1.
Run shops are no feature either (owner, 2026-11-27): on the board from the first run. The schedule below is the current one
(Parts 1, Signatures 2 (the pick from level 3; levels 1–2 have no signature), Worm 3, ..., Aliens 6, City 15, ...; building
features unlock with their blueprint).

Novelty: the first played hour brings 7–9 new things (meta and run) per profile, the longest stretch without one in
the first hour is 15–17 minutes. Level 11's slot (the signature explanation before) is empty now, so the first two hours
have a stretch of 34–52 minutes between Campfires (level 9) and the Alien Arcade (level 14); past level 20 the gaps widen
on purpose (one feature every 2, later 3–4 levels).

## Feature unlocks: minutes played · runs · main level · account day (median of seeds; – = not unlocked)

| Feature | Gate | Casual F2P | Active F2P | Hardcore F2P | Active Whale |
|---|---|---|---|---|---|
| Parts | L1 | 4 min · 0 runs · L1 · D1 | 6 min · 0 runs · L1 · D1 | 6 min · 0 runs · L1 · D1 | 2 min · 0 runs · L1 · D1 |
| Signatures (run) | L2 | 11 min · 1 runs · L2 · D1 | 9 min · 1 runs · L2 · D1 | 9 min · 1 runs · L2 · D1 | 5 min · 1 runs · L2 · D1 |
| Worm Level & Talents | L3 | 26 min · 2 runs · L3 · D2 | 19 min · 2 runs · L3 · D1 | 15 min · 2 runs · L3 · D1 | 10 min · 2 runs · L3 · D1 |
| Minibosses & Mythics (run) | L4 | 41 min · 3 runs · L4 · D3 | 24 min · 3 runs · L4 · D1 | 20 min · 3 runs · L4 · D1 | 18 min · 3 runs · L4 · D1 |
| Two Dice & Forks (run) | L5 | 44 min · 4 runs · L5 · D3 | 27 min · 4 runs · L5 · D1 | 29 min · 4 runs · L5 · D1 | 21 min · 4 runs · L5 · D1 |
| Aliens | L6 | 60 min · 5 runs · L6 · D4 | 35 min · 5 runs · L6 · D1 | 34 min · 5 runs · L6 · D1 | 26 min · 5 runs · L6 · D1 |
| Launch Pod | L6 | 60 min · 5 runs · L6 · D4 | 35 min · 5 runs · L6 · D1 | 34 min · 5 runs · L6 · D1 | 26 min · 5 runs · L6 · D1 |
| Auto Roll | L7 | 77 min · 6 runs · L7 · D5 | 41 min · 6 runs · L7 · D1 | 39 min · 6 runs · L7 · D1 | 35 min · 6 runs · L7 · D1 |
| Campfires (run) | L9 | 112 min · 8 runs · L9 · D7 | 58 min · 8 runs · L9 · D1 | 56 min · 8 runs · L9 · D1 | 50 min · 8 runs · L9 · D1 |
| Alien Arcade (run) | L14 | 197 min · 13 runs · L14 · D12 | 109 min · 13 runs · L14 · D2 | 98 min · 15 runs · L14 · D1 | 89 min · 13 runs · L14 · D2 |
| City | L15 | 214 min · 14 runs · L15 · D13 | 127 min · 16 runs · L15 · D2 | 114 min · 17 runs · L15 · D1 | 97 min · 14 runs · L15 · D2 |
| Laboratory | L0 | – | 1461 min · 150 runs · L32 · D20 | 2310 min · 258 runs · L34 · D12 | 424 min · 49 runs · L26 · D7 |
| Heroes | L18 | 269 min · 17 runs · L18 · D16 | 165 min · 19 runs · L18 · D3 | 241 min · 33 runs · L18 · D2 | 127 min · 17 runs · L18 · D2 |
| Grain Farm | L0 | 222 min · 15 runs · L15 · D13 | 135 min · 17 runs · L15 · D2 | 142 min · 21 runs · L16 · D1 | 103 min · 16 runs · L15 · D2 |
| Ore Mine | L0 | 222 min · 15 runs · L15 · D13 | 134 min · 17 runs · L15 · D2 | 138 min · 20 runs · L16 · D1 | 103 min · 16 runs · L15 · D2 |
| Ice Package | L24 | 427 min · 25 runs · L24 · D24 | 520 min · 54 runs · L24 · D8 | 900 min · 109 runs · L24 · D5 | 318 min · 38 runs · L24 · D5 |
| Mounts | L26 | 485 min · 28 runs · L26 · D27 | 710 min · 74 runs · L26 · D10 | 1222 min · 136 runs · L26 · D7 | 402 min · 47 runs · L26 · D6 |
| Runes | L28 | – | 865 min · 88 runs · L28 · D12 | 1868 min · 208 runs · L28 · D10 | 638 min · 72 runs · L28 · D9 |
| Funds | L30 | – | 1119 min · 110 runs · L30 · D16 | 1911 min · 240 runs · L30 · D10 | 673 min · 74 runs · L30 · D10 |
| Arena | L32 | – | 1146 min · 112 runs · L32 · D16 | 2365 min · 313 runs · L32 · D12 | 749 min · 83 runs · L32 · D11 |
| Breeding Pen | L0 | 226 min · 15 runs · L15 · D14 | 705 min · 74 runs · L24 · D10 | 521 min · 69 runs · L20 · D3 | 291 min · 34 runs · L22 · D5 |
| Season Pass | L36 | – | 1712 min · 158 runs · L38 · D21 | 3173 min · 460 runs · L36 · D15 | 1064 min · 113 runs · L36 · D15 |
| Lightning Package | L38 | – | 1712 min · 158 runs · L38 · D23 | 3173 min · 460 runs · L38 · D17 | 1125 min · 118 runs · L40 · D16 |
| Overcharged Levels | L40 | – | 1988 min · 181 runs · L42 · D27 | 3249 min · 465 runs · L40 · D17 | 1125 min · 118 runs · L40 · D16 |
| Guilds | L42 | – | 2166 min · 197 runs · L42 · D29 | 3487 min · 465 runs · L42 · D17 | 1165 min · 122 runs · L44 · D16 |
| Invasions | L44 | – | – | 4317 min · 646 runs · L44 · D20 | 1165 min · 122 runs · L44 · D16 |
| Burrow | L46 | – | – | 4388 min · 646 runs · L46 · D23 | 1286 min · 128 runs · L48 · D17 |
| Counter Package | L48 | – | – | 5065 min · 858 runs · L50 · D26 | 1305 min · 128 runs · L48 · D18 |
| Forge | L50 | – | – | 5346 min · 858 runs · L50 · D28 | 1547 min · 141 runs · L53 · D19 |
| Dark Side of the Planet | L53 | – | – | – | 1587 min · 141 runs · L53 · D22 |
| Alien Package | L56 | – | – | – | 1836 min · 150 runs · L59 · D23 |
| Time Package | L60 | – | – | – | 1790 min · 141 runs · L60 · D24 |
| Lab research done: Combo Package | – | – | 1503 min · 155 runs · L32 · D21 | 2834 min · 322 runs · L36 · D14 | 782 min · 86 runs · L31 · D11 |
| Lab research done: Ice Package | – | – | – | – | 1915 min · 158 runs · L59 · D26 |
| Lab research done: Lightning Package | – | – | – | – | 2018 min · 161 runs · L61 · D27 |
| Lab research done: Counter Package | – | – | – | – | 2244 min · 169 runs · L62 · D30 |
| Lab research done: Alien Package | – | – | – | – | 2160 min · 162 runs · L60 · D28 |
| Lab research done: Time Package | – | – | – | – | – |

## Novelty gaps (played minutes; median of seeds)

New things = feature unlocks (meta and run content). Longest gap = the longest stretch of play without one.

| Profile | new in first 30 min | new in first 60 min | new in first 120 min | new in first 180 min | longest gap, first 60 min | longest gap, first 120 min | longest gap, first 180 min | median gap, first 180 min |
|---|---|---|---|---|---|---|---|---|
| Casual F2P | 3 | 7 | 9 | 9 | 16 | 34 | 68 | 15 |
| Active F2P | 5 | 9 | 10 | 14 | 16 | 52 | 52 | 7 |
| Hardcore F2P | 5 | 9 | 11 | 13 | 17 | 34 | 42 | 6 |
| Active Whale | 7 | 9 | 14 | 15 | 15 | 40 | 53 | 5 |

Main level at the end of each day (median):

| Profile | day 1 | day 2 | day 3 | day 4 | day 5 | day 7 | day 10 | day 14 | day 21 | day 28 |
|---|---|---|---|---|---|---|---|---|---|---|
| Casual F2P | 2 | 3 | 5 | 6 | 7 | 9 | 12 | 16 | 21 | 26 |
| Active F2P | 9 | 15 | 19 | 20 | 22 | 23 | 26 | 28 | 37 | 41 |
| Hardcore F2P | 16 | 19 | 21 | 23 | 24 | 27 | 30 | 35 | 44 | 50 |
| Active Whale | 10 | 19 | 21 | 23 | 25 | 26 | 31 | 33 | 52 | 61 |

