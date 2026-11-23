# Onboarding v2: simulated unlock days

Generated with `dotnet run --project tools/RogueStars.Sim -c Release -- --unlocks --days 30 --seeds 3` (F2P bots;
Casual = 2 × 7.5 min/day, Active = 4 × 15 min ≈ 1 h/day, Hardcore = 7 sessions ≈ 3 h/day, the binge player). Schedule =
earliest account day / main level from `ContentDb.Onboarding.cs` (GDD §16). Day-1 unlocks are unlimited; from day 2 on
at most one unlock group per day, so a profile that is ahead hits the day gate and a slow one the level gate.

## Feature unlocks: account day / main level (median of seeds; – = not unlocked within 30 days)

| Feature | Schedule | Casual F2P | Active F2P | Hardcore F2P |
|---|---|---|---|---|
| Parts | D1 L1 | D1 L1 | D1 L1 | D1 L1 |
| Daily Goals | D1 L5 | D2 L5 | D1 L5 | D1 L5 |
| City: Biomass Farm | D2 L8 | D4 L8 | D2 L8 | D2 L10 |
| Worm Level & Talents | D3 L6 | D3 L6 | D3 L10 | D3 L12 |
| Part Signal | D4 L12 | D8 L12 | D4 L14 | D4 L18 |
| Shop | D4 L12 | D8 L12 | D4 L14 | D4 L18 |
| Laboratory | D5 L14 | D9 L14 | D5 L18 | D5 L24 |
| Grain Farm | D6 L16 | D10 L16 | D6 L21 | D6 L24 |
| Aliens | D7 L18 | D11 L18 | D7 L21 | D7 L24 |
| Alien Signal | D7 L18 | D11 L18 | D7 L21 | D7 L24 |
| Housing (Squad Slot 1) | D8 L20 | D12 L20 | D8 L23 | D8 L25 |
| Blueprint Signal | D9 L22 | D15 L22 | D9 L24 | D9 L26 |
| Heroes | D10 L24 | D18.5 L24 | D10 L26 | D10 L26 |
| Ore Mine | D11 L26 | – | D11 L27 | D11 L28 |
| Runes | D11 L26 | – | D11 L27 | D11 L28 |
| Mounts | D12 L28 | – | D12 L32 | D12 L30 |
| Funds | D13 L29 | – | D13 L32 | D13 L32 |
| Breeding Pen | D14 L30 | – | D14 L33 | D14 L32 |
| Season Pass | D14 L30 | – | D14 L33 | D14 L32 |
| Arena | D15 L32 | – | D15 L34 | D15 L33 |
| Overcharged Levels | D16 L34 | – | D16 L35 | D16 L36 |
| Guilds | D17 L36 | – | D17 L37 | D17 L37 |
| Invasions | D18 L38 | – | D22 L38 | D18 L39 |
| Burrow | D19 L40 | – | D23 L40 | D19 L40 |
| Forge | D20 L42 | – | D27 L42 | D23 L42 |
| Dark Side of the Planet | D21 L45 | – | D27 L45 | D25 L45 |

Main level at the end of each day (median):

| Profile | day 1 | day 2 | day 3 | day 4 | day 5 | day 7 | day 10 | day 14 | day 21 | day 28 |
|---|---|---|---|---|---|---|---|---|---|---|
| Casual F2P | 3 | 6 | 8 | 10 | 10 | 11 | 18 | 21 | 24 | 24 |
| Active F2P | 8 | 10 | 14 | 18 | 21 | 23 | 27 | 34 | 37 | 43 |
| Hardcore F2P | 10 | 12 | 18 | 24 | 24 | 25 | 28 | 33 | 41 | 49 |

Since "rewards follow use" (GDD §13.1) the bot can no longer build the Biomass Farm before the City unlocks (the blueprint
waits in the reward stash; the rules never gated `CityBuild`, the client always hid it), so the casual bot is 1–3 levels
behind the earlier table in week 1 and reaches the City on day 4 instead of day 2–5. Active and Hardcore stay within a
level or two (token stash caps sized so the first pulls are not cut, see docs/design/economy-model.md).

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
