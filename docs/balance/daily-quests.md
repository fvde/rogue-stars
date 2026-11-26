# Daily Quests: availability, points and chests

Daily Quests are there from the first cleared level: **no feature, no unlock moment, no intro, no gift** (owner, 2026-11-26). The hub
button "Daily Quests" shows with Parts (level 1), so the day-1 hub keeps nothing but the play button. Content: `ContentDb.Quests.cs`;
tests: `QuestAvailabilityTests` (rules) and `QuestSimTests` (simulator); the first-days table below comes from
`dotnet run --project tools/RogueStars.Sim -c Release -- --quests --days 10 --seeds 5 --profiles Casual:F2P,Active:F2P`.

## Rules

- **Daily and weekly quests pay no reward of their own, only points.** The bar fills; the five chests on it are the reward.
- **A quest is hidden until the feature that makes it possible** (`QuestDef.Feature`, `QuestSystem.IsAvailable`): the Shop for
  "Buy 1 shop item" and the ad quest, the City for building and Blueprint quests, Breeding, Invasions, the Arena, the worm level (level 3)
  for the worm-level quests and achievement, Parts / Aliens / City for the pull achievements, the Alien Arcade for the mini-game one.
- **The early quests alone fill both point tracks.** Daily: login 10 + runs 25 + battles 20 + skills 20 + stamina 25 = exactly 100.
  Weekly: runs 150 + bosses 150 + daily quests 200 = exactly 500. No ad, Shop, Arena or City is needed; later quests are extra points
  (daily +40, weekly +250).
- **The ad quest** (`d_ad`) needs the Shop and hides while ads are disabled (`ShopConfig.AdsDisabled`, the rules' only notion of ad
  availability; backlog D17).
- **Random pull tickets** (daily 100 chest): `PointChestDef.RandomTokens` tokens, each of a random Signal the player has unlocked (Part
  from level 1, Alien from 6, Blueprint from the City at 15; before any is unlocked a Part token), drawn from a fork of the player's RNG
  stream by chest and day (deterministic). The draw is stored in `QuestState.LastChestTokens` for the client's chest-open animation (the
  shared reveal: the tickets flip out one by one).

## Daily (reset 00:00 UTC)

| Quest | Target | Points | Available |
|---|---|---|---|
| Log in (`d_login`) | 1 | 10 | level 1 |
| Complete 3 runs (`d_runs`) | 3 | 25 | level 1 |
| Win 10 battles (`d_battles`) | 10 | 20 | level 1 |
| Pick 5 run skills (`d_skills`, replaces "Do 1 Part pull") | 5 | 20 | level 1 |
| Spend 30 stamina (`d_stamina`) | 30 | 25 | level 1 |
| Buy 1 shop item (`d_shop`) | 1 | 10 | Shop, level 12 |
| Watch 1 ad (`d_ad`) | 1 | 10 | Shop, level 12, ads on |
| Fight 2 arena battles (`d_arena`) | 2 | 20 | Arena, level 32 |

| Chest | Contents |
|---|---|
| 20 | 10 Stamina, 500 Ore, 10 Diamonds |
| 40 | 1000 Biomass, 20 Diamonds |
| 60 | 20 Gear Cores, 10 Stamina, 20 Diamonds |
| 80 | 1000 Ore, 1000 Biomass, 20 Diamonds |
| 100 | 3 pull tickets (random unlocked Signal each), 30 Diamonds |

Diamonds over the five: 100. The first chest pays only what a level 1 player can use at once; Biomass waits for the worm (level 3, day 1-2)
and Gear Cores in the third chest wait in the reward stash until a part needs them (cap 200, released then).

"Do 1 Part pull" (`d_pull`) is gone: a pull costs tokens or Diamonds and a new player may have neither. "Open 1 chest in a run" was the
other option, but no counter exists for in-run chests (one-time milestone chests), so the quest counts something every run has and that
costs nothing: picked skills.

## Weekly (reset Monday 00:00 UTC)

| Quest | Target | Points | Available |
|---|---|---|---|
| Complete 20 runs (`w_runs`) | 20 | 150 | level 1 |
| Defeat 10 bosses (`w_bosses`) | 10 | 150 | level 1 |
| Finish 14 daily quests (`w_daily`, was 20: two a day) | 14 | 200 | level 1 |
| Do 10 Blueprint pulls (`w_blueprints`) | 10 | 50 | City, level 15 |
| Upgrade 5 buildings (`w_buildings`, was ungated) | 5 | 50 | City, level 15 |
| Breed 2 aliens (`w_breed`) | 2 | 50 | Breeding Pen (blueprint) |
| Fight 3 invasion battles (`w_invasion`) | 3 | 100 | Invasions, level 44 |

| Chest | Contents |
|---|---|
| 100 | 3000 Ore, 50 Diamonds |
| 200 | 3000 Biomass, 100 Diamonds |
| 300 | 100 Gear Cores, 2000 Ore, 100 Diamonds |
| 400 | 5 tickets of EACH unlocked Signal (Part / Alien / Blueprint), 2000 Biomass, 100 Diamonds |
| 500 | 1 Part awakening core (waits for a Mythic part), 150 Diamonds |

Diamonds over the five: 500. Ore 5000 and Biomass 5000 against 1500 and 2000 daily. The point chests no longer pay Data (the Laboratory
trickle is gone; owner question).

## New-player event (7 days + 3 days to claim; keeps its own rewards)

Targets follow what a Casual F2P bot (about 15 minutes a day, one run a day) reaches by that day; Active and Hardcore are far ahead.

| Day | Quest | Target | Reward | Was |
|---|---|---|---|---|
| 1 | Complete 3 runs (`np_runs_3`) | 3 | 50 Diamonds | |
| 2 | Reach worm level 5 (`np_worm_5`, needs the worm level feature) | 5 | 50 Diamonds | |
| 3 | Do 10 Part pulls (`np_pulls_10`) | 10 | 5 Part tokens | the free 10x pull of the Parts unlock is exactly 10 |
| 4 | Complete 5 runs (`np_runs_5`) | 5 | 80 Diamonds | 15 runs (Casual: 11 after 10 days) |
| 5 | Win 50 battles (`np_battles_50`) | 50 | 80 Diamonds | Upgrade 5 buildings (impossible before the City at 15) |
| 6 | Reach worm level 11 (`np_worm_11`) | 11 | 100 Diamonds | level 12 (Casual: 12 only on day 8) |
| 7 | Defeat 8 bosses (`np_bosses_8`) | 8 | 10 Alien tokens | 20 bosses (Casual: 11 after 10 days) |

Day 5 pick: "Reach worm level 8" would be done by Casual on day 3 and "Complete 8 runs" on day 7; "Win 50 battles" is reached on day 4 by Casual
(53) and day 1 by Active (92).

## Achievements: linear, repeating milestones

The same reward every N of a counter, for ever; several milestones passed at once pay together; the screen shows the next milestone.

| Achievement | Every | Reward | Counter | Available |
|---|---|---|---|---|
| Defeat 100 enemies | 100 | 10 Diamonds | `run_enemies_defeated` | level 1 |
| Defeat 10 bosses | 10 | 20 Diamonds | `run_bosses_killed` | level 1 |
| Play 10 mini-games | 10 | 20 Diamonds | `run_minigames_played` | Alien Arcade, level 14 |
| Gain 10 worm levels | 10 | 50 Diamonds | `worm_level_max` | worm level feature |
| Do 100 Part pulls | 100 | 10 Part tickets | `pulls_part` | Parts |
| Do 100 Alien pulls | 100 | 10 Alien tickets | `pulls_alien` | Aliens |
| Do 100 Blueprint pulls | 100 | 10 Blueprint tickets | `pulls_blueprint` | City |
| Win 50 arena fights | 50 | 50 Diamonds | `arena_wins` | Arena |
| Be top contributor of an invasion | 1 | 100 Diamonds | `invasion_top_contributor` | Invasions |

The old one-time tiers (1 / 10 / 50 / 100 / 500 / 1000 runs; worm level 10 / 20 / 30 / 50; 100 Part pulls) are gone: runs are covered by the
bosses milestone (one boss per run), the others by the every-N ones. Arena and invasion were converted to every-N (their old rewards were
200 / 300 Diamonds once).

## Simulated first days (median of 5 seeds, end of day; points = the quest points the bot claimed that day)

| Profile | End of day (median) | day 1 | day 2 | day 3 | day 4 | day 5 | day 6 | day 7 | day 8 | day 9 | day 10 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Active:F2P | main level | 9 | 15 | 18 | 20 | 20 | 21 | 22 | 23 | 23 | 25 |
| Active:F2P | worm level | 8 | 11 | 13 | 14 | 16 | 16 | 17 | 18 | 19 | 19 |
| Active:F2P | daily points claimed | 100 | 120 | 120 | 120 | 120 | 120 | 120 | 120 | 120 | 120 |
| Active:F2P | weekly points claimed | 0 | 150 | 550 | 600 | 600 | 600 | 600 | 50 | 250 | 400 |
| Active:F2P | runs_completed | 9 | 15 | 21 | 24 | 28 | 33 | 39 | 44 | 48 | 52 |
| Active:F2P | run_battles_won | 92 | 169 | 241 | 312 | 369 | 437 | 505 | 563 | 629 | 705 |
| Active:F2P | run_skills_drafted | 96 | 185 | 268 | 361 | 429 | 515 | 600 | 671 | 743 | 835 |
| Active:F2P | pulls_part | 22 | 45 | 68 | 73 | 83 | 89 | 111 | 116 | 127 | 140 |
| Active:F2P | daily_quests_claimed | 5 | 12 | 19 | 26 | 33 | 40 | 47 | 54 | 61 | 68 |
| Casual:F2P | main level | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 |
| Casual:F2P | worm level | 0 | 5 | 8 | 11 | 11 | 11 | 11 | 13 | 14 | 15 |
| Casual:F2P | daily points claimed | 50 | 30 | 30 | 50 | 50 | 50 | 50 | 50 | 50 | 50 |
| Casual:F2P | weekly points claimed | 0 | 0 | 0 | 0 | 0 | 200 | 200 | 0 | 0 | 0 |
| Casual:F2P | runs_completed | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 |
| Casual:F2P | run_battles_won | 23 | 31 | 38 | 53 | 66 | 74 | 89 | 100 | 111 | 121 |
| Casual:F2P | run_skills_drafted | 21 | 31 | 39 | 52 | 68 | 78 | 94 | 103 | 115 | 130 |
| Casual:F2P | pulls_part | 13 | 24 | 37 | 51 | 57 | 60 | 73 | 79 | 84 | 92 |
| Casual:F2P | daily_quests_claimed | 3 | 5 | 7 | 10 | 13 | 15 | 18 | 21 | 24 | 27 |

Reading (points claimed are the quest points of that day; the chests are separate): Active F2P fills the daily track from day 1 (100, then 120 with the Shop quests) and the weekly track in its first week
(600 with the City quests). Casual F2P (one run a day) claims 30-50 daily points and reaches weekly 200 (the "14 daily quests" quest); a run
a day does not fill the 3-runs / 30-stamina quests (owner question: lower those two for Casual?).
