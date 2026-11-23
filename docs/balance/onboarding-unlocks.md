# Onboarding v2: simulated unlock days

Generated with `dotnet run --project tools/RogueStars.Sim -c Release -- --unlocks --days 30 --seeds 3` (F2P bots;
Casual = 2 × 7.5 min/day, Active = 4 × 15 min ≈ 1 h/day, Hardcore = 7 sessions ≈ 3 h/day, the binge player). Schedule =
earliest account day / main level from `ContentDb.Onboarding.cs` (GDD §16). Day-1 unlocks are unlimited; from day 2 on
at most one unlock group per day, so a profile that is ahead hits the day gate and a slow one the level gate.

## Feature unlocks: account day / main level (median of seeds; – = not unlocked within 30 days)

| Feature | Schedule | Casual F2P | Active F2P | Hardcore F2P |
|---|---|---|---|---|
| Parts | D1 L1 | D1 L1 | D1 L1 | D1 L1 |
| Daily Goals | D1 L5 | D4 L5 | D1 L5 | D1 L5 |
| City: Biomass Farm | D2 L8 | D5 L8 | D2 L8 | D2 L8 |
| Worm Level & Talents | D3 L6 | D4 L6 | D3 L9 | D3 L10 |
| Part Signal | D4 L12 | D7 L12 | D4 L14 | D4 L18 |
| Shop | D4 L12 | D7 L12 | D4 L14 | D4 L18 |
| Laboratory | D5 L14 | D8 L14 | D5 L20 | D5 L22 |
| Grain Farm | D6 L16 | D9 L16 | D6 L21 | D6 L23 |
| Aliens | D7 L18 | D10 L18 | D7 L23 | D7 L24 |
| Alien Signal | D7 L18 | D10 L18 | D7 L23 | D7 L24 |
| Housing (Squad Slot 1) | D8 L20 | D13 L20 | D8 L24 | D8 L25 |
| Blueprint Signal | D9 L22 | D16 L22 | D9 L25 | D9 L26 |
| Heroes | D10 L24 | D22 L24 | D10 L26 | D10 L28 |
| Ore Mine | D11 L26 | – | D11 L26 | D11 L29 |
| Runes | D11 L26 | – | D11 L26 | D11 L29 |
| Mounts | D12 L28 | – | D12 L29 | D12 L33 |
| Funds | D13 L29 | – | D13 L29 | D13 L36 |
| Breeding Pen | D14 L30 | – | D14 L30 | D14 L37 |
| Season Pass | D14 L30 | – | D14 L30 | D14 L37 |
| Arena | D15 L32 | – | D15 L33 | D15 L37 |
| Overcharged Levels | D16 L34 | – | D16 L35 | D16 L40 |
| Guilds | D17 L36 | – | D17 L36 | D17 L40 |
| Invasions | D18 L38 | – | D21 L38 | D18 L41 |
| Burrow | D19 L40 | – | D23 L40 | D19 L41 |
| Forge | D20 L42 | – | D25 L42 | D20 L42 |
| Dark Side of the Planet | D21 L45 | – | – | D22 L45 |

Main level at the end of each day (median):

| Profile | day 1 | day 2 | day 3 | day 4 | day 5 | day 7 | day 10 | day 14 | day 21 | day 28 |
|---|---|---|---|---|---|---|---|---|---|---|
| Casual F2P | 3 | 4 | 4 | 6 | 10 | 14 | 18 | 21 | 22 | 24 |
| Active F2P | 6 | 9 | 14 | 20 | 21 | 24 | 26 | 33 | 38 | 42 |
| Hardcore F2P | 8 | 10 | 18 | 22 | 23 | 25 | 29 | 37 | 44 | 50 |

## Worm Level gate (L6 instead of the approved L10)

Worm Level & Talents was approved as D3 L10. With L10 the casual bot cannot clear level 10 without worm levels and sits
at level 9 from about day 10 to day 22 (main level on day 7 / 14 / 21: 7 / 9 / 9, against 16 / 20 / 23 before onboarding
v2). L6 keeps the day-3 unlock for Active and Hardcore (they are at level 9–10 by then) and removes the wall (14 / 21 / 22,
with main level 1 eased for the part-less first run).
L8 still walls (7 / 14 / 22). Try other gates with `--variant gate_<featureId>_L<level>`.

The Active bot clears ~6 levels on day 1 and ~14 by day 3 (the design note assumed ~14 and ~27). It is still a little
ahead of every gate from day 2 to day 17, so the ~1 h/day player gets exactly one unlock per scheduled day there.

From week 3 the ~1 h/day player meets the level gate again (Invasions day 21, Burrow 23, Forge 25, Dark Side after day 30):
the day schedule of week 3 assumes a bit more progress than the Active bot makes.
