# Economy model (Progression) — pull income and long-haul pacing

This page documents the "typical active F2P" income assumptions behind the gacha and hero/mount pacing.
The numbers live in content (`ContentDb.EconomyConfig.F2p`, `ContentDb.GachaConfig`, `ContentDb.HeroConfig`)
and are checked by `EconomyLongHaulTests` (shared/RogueStars.Rules.Tests/Progression). Change both together.

## Income assumptions — active F2P (plays daily, does dailies/weeklies, watches capped ads)
| Source | Per day | Notes |
|---|---|---|
| Diamonds | 300 | quests ~100, Diamond mine ~80, arena/events/login ~120 |
| Share of Diamonds spent on pulls | 60% → 180/day | rest goes to stamina refills, timers, shop |
| Free pull tokens, per pull type | 6 | daily/weekly quests, login cycle, free season track, run drops, 1 ad pull |
| Shards for the hero the player focuses (outside pulls) | 2 | shard shop, events, season track |

Diamonds for pulls are spent as 10-pulls (1 350 Diamonds) rotating over the three pull types.

## Gacha tuning (per pull type)
- Base tiers: Common 60%, Rare 28%, Epic 9.5%, Legendary 2%, Mythic 0.5% (published via `GachaSystem.GetOdds`).
- 1 pull = 1 token or 150 Diamonds; 10 pulls = 10 tokens or 1 350 Diamonds; a 10-pull contains Epic+.
- Hidden pity (never shown): the 50th pull without a Mythic is Mythic; Legendary weight +2.5 per pull after
  25 pulls without Legendary+, the 40th is Legendary+. Pity is per pull type, shared by all its banners and
  carried over between banners/weeks.
- Weekly themed banners for every type; the featured Mythic takes 50% of Mythic outcomes.
- Alien Signal: ~40% of outcomes are hero shards (2/5/10/25/60 shards by tier, spread over all heroes);
  hero-themed Alien banners feature 80 shards of one hero as the Mythic.

## Results (52-week simulation through the real command handlers)
| Metric | Value | Target |
|---|---|---|
| Pulls per type per week | ~45 (42 from tokens, ~3 from Diamonds) | — |
| Mythics per type per week | ~1.07 (about half from the hidden pity) | about weekly |
| Shards to hero 10★ | 3 470 (unlock 40, then 30/60/100/160/250/380/550/800/1 100) | genuinely hard |
| Focused hero shards per week, F2P | ~37 | — |
| Weeks to 10★, F2P | ~95 (≈ 22 months) | many months (test band: 9 months – 3 years) |
| Weeks to 10★, spender (30× Diamonds, saves for the hero banner) | ~11 | clearly faster |

Every hero, mount, blueprint and alien keeps converting duplicates into shards; hero and mount collection
bonuses (per star of every owned hero / per star+awakening of every owned mount) keep extra pulls valuable
after the main hero is maxed.

## Other long-haul curves (content)
- Worm: 500 levels, Biomass cost `80·L^1.5·1.01^L` (>1 billion Biomass to max), +3% HP/ATK/DEF every 25 levels;
  60 talent tiers.
- Parts: Mythic needs 2 Legendary copies of the same part on top of the Legendary+3 chain; Mythic+1/+2/+3 need
  3 / 4 / 1 same + 5 further Mythic parts of the same kind (balance pass: was 1 / 2 / 1+1). Level cap 10 (Common) →
  100 (Mythic+3) + up to 6 plate raises (60…1 600 plates); Gear Cores from part level 10 (2·(L−9)^1.25 per level);
  S/SS awakening costs Part Awaken Cores (40 / 150) from Burrow floor 10 and the Dark Side.
- Mounts: 5★ costs 350 shards (35 duplicates), awakening 1–5 another 1 040 shards + 2 500 Mount Cores. Mount copies
  are ~25% of Blueprint Signal outcomes (1/1/2/5/10 copies by tier, first copy unlocks the mount) and a mount banner
  ("Lancer Shipyard", 15 copies) rotates with the blueprint banners; Mount Cores come from Burrow floors 6+, the Dark
  Side and the guild shop.

The full meta-progression (every system, 9 player profiles, 2 years) is simulated by `tools/RogueStars.Sim`; see
`docs/balance/progression-report.md` for the curves, walls and the balance pass that set the numbers above.

## Levers
- Mythic frequency: tokens per day and `MythicHardPity` (mean pulls per Mythic ≈ (1−0.995^pity)/0.005 ≈ 44 at 50).
- Hero pacing: `HeroConfig.StarShardCosts`, hero-shard share/amounts in the Alien pool, featured hero amount.

## Early run balance (integration with the Run workstream)
Runs use the real Progression loadout (the Run fallback worm stats are only a safety net and no longer trigger).
Worm base stats were raised to HP 900 / ATK 90 / DEF 20 / Speed 100 / Crit 5% (was 500 / 50 / 20 / 100 / 0%) so the
starting worm matches the Run threat curve. Auto-play win rates over 30 seeds (fresh starter kit, parts equipped):

| Worm level | Ember Wastes 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|
| 1 | 100% | 87% | 40% | 0% | 0% | 0% | 0% |
| 4 | 100% | 100% | 63% | 50% | 7% | 0% | 0% |
| 8 | 100% | 100% | 100% | 90% | 57% | 37% | 3% |

So the first levels are won most of the time, levels 4–6 are a soft wall, and a few worm levels bought with the
Biomass those runs pay overcome it. `RunIntegrationTests.NewPlayer_AutoPlays_ToLevel3_WithUpgradesBetweenRuns`
checks that a new player reaches level 3 within 25 auto-played runs while spending rewards between runs.
