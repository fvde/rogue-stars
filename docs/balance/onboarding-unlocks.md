# Onboarding v2: simulated unlock days

Generated with `dotnet run --project tools/RogueStars.Sim -c Release -- --unlocks --days 30 --seeds 3` (F2P bots;
Casual = 2 × 7.5 min/day, Active = 4 × 15 min ≈ 1 h/day, Hardcore = 7 sessions ≈ 3 h/day, the binge player). Schedule =
earliest account day / main level from `ContentDb.Onboarding.cs` (GDD §16). Day-1 unlocks are unlimited; from day 2 on
at most one unlock group per day, so a profile that is ahead hits the day gate and a slow one the level gate.

## Feature unlocks: account day / main level (median of seeds; – = not unlocked within 30 days)

| Feature | Schedule | Casual F2P | Active F2P | Hardcore F2P |
|---|---|---|---|---|
| Parts | D1 L1 | D1 L1 | D1 L1 | D1 L1 |
| Daily Goals | D1 L5 | D5 L5 | D1 L5 | D1 L5 |
| City: Biomass Farm | D2 L8 | D6 L8 | D2 L8 | D2 L8 |
| Worm Level & Talents | D3 L6 | D5 L6 | D3 L9 | D3 L10 |
| Part Signal | D4 L12 | D8 L12 | D4 L14 | D4 L17 |
| Shop | D4 L12 | D8 L12 | D4 L14 | D4 L17 |
| Laboratory | D5 L14 | D9 L14 | D5 L20 | D5 L22 |
| Grain Farm | D6 L16 | D10 L16 | D6 L22 | D6 L24 |
| Aliens | D7 L18 | D11 L18 | D7 L23 | D7 L24 |
| Alien Signal | D7 L18 | D11 L18 | D7 L23 | D7 L24 |
| Housing (Squad Slot 1) | D8 L20 | D13 L20 | D8 L24 | D8 L26 |
| Blueprint Signal | D9 L22 | D17 L22 | D9 L25 | D9 L26 |
| Heroes | D10 L24 | D22 L24 | D10 L26 | D10 L27 |
| Ore Mine | D11 L26 | – | D11 L26 | D11 L27 |
| Runes | D11 L26 | – | D11 L26 | D11 L27 |
| Mounts | D12 L28 | – | D12 L29 | D12 L30 |
| Funds | D13 L29 | – | D13 L32 | D13 L32 |
| Breeding Pen | D14 L30 | – | D14 L33 | D14 L34 |
| Season Pass | D14 L30 | – | D14 L33 | D14 L34 |
| Arena | D15 L32 | – | D15 L34 | D15 L34 |
| Overcharged Levels | D16 L34 | – | D16 L36 | D16 L36 |
| Guilds | D17 L36 | – | D17 L38 | D17 L37 |
| Invasions | D18 L38 | – | D19 L38 | D18 L38 |
| Burrow | D19 L40 | – | D21 L40 | D20 L40 |
| Forge | D20 L42 | – | D26 L42 | D21 L42 |
| Dark Side of the Planet | D21 L45 | – | D30 L45 | D23 L45 |

Main level at the end of each day (median):

| Profile | day 1 | day 2 | day 3 | day 4 | day 5 | day 7 | day 10 | day 14 | day 21 | day 28 |
|---|---|---|---|---|---|---|---|---|---|---|
| Casual F2P | 2 | 3 | 4 | 4 | 7 | 11 | 16 | 21 | 23 | 25 |
| Active F2P | 6 | 9 | 14 | 18 | 20 | 24 | 26 | 33 | 40 | 43 |
| Hardcore F2P | 7 | 10 | 17 | 22 | 24 | 26 | 27 | 34 | 44 | 48 |

## Worm Level gate (L6 instead of the approved L10)

Worm Level & Talents was approved as D3 L10. With L10 the casual bot cannot clear level 10 without worm levels and sits
at level 9 from about day 10 to day 22 (main level on day 7 / 14 / 21: 7 / 9 / 9, against 16 / 20 / 23 before onboarding
v2). L6 keeps the day-3 unlock for Active and Hardcore (they are at level 9–10 by then) and removes the wall (11 / 21 / 23).
L8 still walls (7 / 14 / 22). Try other gates with `--variant gate_<featureId>_L<level>`.

The Active bot clears ~6 levels on day 1 and ~14 by day 3 (the design note assumed ~14 and ~27), so ~1 h/day players
meet the level gate in week 1 (Part Signal at L14 on day 4) and the day gate from week 2 on.
