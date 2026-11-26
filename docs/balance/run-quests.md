# Run Quests simulation

Generated with `dotnet run --project tools/RogueStars.Sim -c Release -- --run-quests --days 365 --seeds 5` (2026-11-26).
Bots: Typical = fights when at 50%+ HP, pays coins and skills when it can, life at 60%+ HP, gives up every run skill half of the times; Eager = takes every battle and pays every price it can. Bots buy freely in shops and never save coins for a waiting price, so coin prices often find them short (a real player can plan: the journal shows the waiting price).

Runs = eligible runs (not the Burrow) after the unlock (main level 20). Median [min–max] of seeds.

| Profile | Policy | Unlock day | Runs to finish | Days to finish | Runs/day | Encounters | Battles won/fought | Waits story/battle/price | Unaffordable | Done |
|---|---|---|---|---|---|---|---|---|---|---|
| Active F2P | Eager | 5 [3–7] | 187 [180–411] | 35 [25–160] | 5.1 | 39 [35–83] | 30/67 | 0/37/66 | 66 (Coins 63, LegendarySkill 1, MythicSkill 2) | 5/5 (unfinished at chapter ) |
| Active F2P | Typical | 5 [3–7] | 220 [187–411] | 53 [27–160] | 4.4 | 45 [35–83] | 30/67 | 0/37/80 | 66 (Coins 63, LegendarySkill 1, MythicSkill 2) | 5/5 (unfinished at chapter ) |
| Casual F2P | Eager | 19 [19–19] | 199 [199–199] | 340 [340–340] | 0.5 | 36 [32–41] | 23/60 | 0/37/36 | 36 (Coins 35, LegendarySkill 1) | 1/5 (unfinished at chapter 18, 18, 24, 23) |
| Casual F2P | Typical | 19 [19–19] | 199 [199–199] | 340 [340–340] | 0.5 | 36 [32–41] | 23/60 | 0/37/36 | 36 (Coins 35, LegendarySkill 1) | 1/5 (unfinished at chapter 23, 24, 18, 18) |
| Hardcore F2P | Eager | 2 [2–3] | 354 [182–419] | 15 [9–19] | 22.1 | 69 [40–84] | 30/33 | 0/3/144 | 144 (Coins 138, LegendarySkill 3, MythicSkill 3) | 5/5 (unfinished at chapter ) |
| Hardcore F2P | Typical | 2 [2–3] | 355 [194–471] | 15 [9–20] | 22.2 | 69 [40–95] | 30/33 | 0/3/158 | 144 (Coins 138, LegendarySkill 3, MythicSkill 3) | 5/5 (unfinished at chapter ) |

