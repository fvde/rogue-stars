# Run Quests simulation

Generated with `dotnet run --project tools/RogueStars.Sim -c Release -- --run-quests --days 365 --seeds 5` (2026-11-26).
Bots: Typical = fights when at 50%+ HP, pays coins and skills when it can, life at 60%+ HP, gives up every run skill half of the times; Eager = takes every battle and pays every price it can. A lost quest battle is the normal defeat: bots take the revive offer as in any defeat (an ad when they watch ads and the run has loot pending, diamonds when they spend), otherwise the run ends. "Lost" counts first tries lost; most of them end the run. Bots buy freely in shops and never save coins for a waiting price, so coin prices often find them short (a real player can plan: the journal shows the waiting price).

No level gate: runs = every run but the Burrow from the first day (the tutorial run included). Chance 20% per run, no pity. Median [min–max] of seeds.

| Profile | Policy | First meeting (day) | Runs to finish | Days to finish | Runs/day | Encounters | Battles won/fought (lost = run over) | Waits story/battle/price | Unaffordable | Done |
|---|---|---|---|---|---|---|---|---|---|---|
| Active F2P | Eager | 2 [1–2] | 275 [158–412] | 54 [23–134] | 5.1 | 43 [34–73] | 30/69 (39 lost) | 0/39/54 | 54 (Coins 53, LegendarySkill 1) | 5/5 (unfinished at chapter ) |
| Active F2P | Typical | 2 [1–2] | 337 [158–428] | 85 [23–134] | 4.0 | 48 [34–73] | 30/68 (38 lost) | 0/39/68 | 54 (Coins 53, LegendarySkill 1) | 5/5 (unfinished at chapter ) |
| Casual F2P | Eager | 3 [2–19] | 190 [190–190] | 284 [284–284] | 0.6 | 35 [30–42] | 26/60 (34 lost) | 0/34/27 | 27 (Coins 27) | 1/5 (unfinished at chapter 28, 18, 29, 18) |
| Casual F2P | Typical | 3 [2–19] | 190 [190–190] | 284 [284–284] | 0.6 | 35 [30–42] | 26/60 (34 lost) | 0/34/27 | 27 (Coins 27) | 1/5 (unfinished at chapter 18, 29, 18, 28) |
| Hardcore F2P | Eager | 1 [1–1] | 209 [199–860] | 9 [9–23] | 23.2 | 40 [38–65] | 30/45 (15 lost) | 0/15/59 | 59 (Coins 59) | 5/5 (unfinished at chapter ) |
| Hardcore F2P | Typical | 1 [1–1] | 226 [209–877] | 10 [9–24] | 23.2 | 45 [38–65] | 30/45 (15 lost) | 0/15/73 | 59 (Coins 59) | 5/5 (unfinished at chapter ) |

