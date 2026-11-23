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
- Parts (`docs/design/parts.md`): 14-step ladder to Mythic SS+2. Below Mythic three parts of a rarity merge into one
  (any def/kind); Mythic+ steps take Legendary+ fodder (fodder points, Legendary = 81 = 1 LE): 2 / 8 / 14 / 24 / 40 /
  70 / 120 / 200 / 340 LE from Mythic to SS+2, own-def copies (Legendary+) 1 / 2 / 3 / 1 for Mythic / S / SS / SS+2 and
  Ore 1.5k ×1.5 per step. Level cap 10 (Common) → 115 (SS+2) + up to 6 plate raises (60…1 600 plates); Gear Cores from
  part level 10. Five part skills (Epic / Legendary / Mythic / Mythic S / Mythic SS). Awakening: Mythic+, once,
  50 Awaken Cores.
- Awaken Cores (extremely rare): arena shop (200 Arena Medals, 2 per week), repelled high-tier invasions (top 10
  contributors, 4–25%), Burrow floors 9/10 (1% / 1.5%); no routine source. Measured: ~0.6 per week Casual,
  ~1.2 Active, ~2 Hardcore; first awakening ~day 160–300 (Hardcore/Active), ~day 520–580 (Casual).
- Arena Medals: 4 per win, 2 per loss, 20–120 per weekly rank (×1–2.5 by league); arena shop: Awaken Core, part
  tokens, Data, Gear Cores.
- Laboratory: research costs Data; the k-th research level takes h = 1.5 h × 1.0355^k nominal and costs 14 × h^0.75
  Data (19 … 2 464; ≈ 28.7k h and ≈ 95k Data for the whole tree). Data: Dark Side clears 24 × 1.025^(i−1) × 2.5 (level i of 150; 60 … 2 400),
  daily chests 20/day, weekly chest 100, Laboratory unlock 60, arena shop 300 per 50 medals.
- Biomass: portal runs are the main source — completion (120 + 30·i) × portal scale, loot 45–90 × scale, and the
  **daily Biomass surge**: the first 3 won portal runs (Normal/Overcharged) of each reset-day pay their Biomass ×4
  (`RunTuning.DailyBiomassBonusRuns/Mult`), so short sessions also get most of their Biomass from runs. City farms
  (Biomass Farm 30/h, Spore Vats 75/h, half of before) are the secondary source; the daily point chest pays 2 000.
  Sinks: worm levels, talents, buildings (no laboratory).
- Mounts: 5★ costs 350 shards (35 duplicates), awakening 1–5 another 1 040 shards + 2 500 Mount Cores. Mount copies
  are ~25% of Blueprint Signal outcomes (1/1/2/5/10 copies by tier, first copy unlocks the mount) and a mount banner
  ("Lancer Shipyard", 15 copies) rotates with the blueprint banners; Mount Cores come from Burrow floors 6+, the Dark
  Side (1–4 per clear) and the guild shop.

The full meta-progression (every system, 9 player profiles, 2 years) is simulated by `tools/RogueStars.Sim`; see
`docs/balance/progression-report.md` for the curves, walls and the balance pass that set the numbers above.

## Levers
- Mythic frequency: tokens per day and `MythicHardPity` (mean pulls per Mythic ≈ (1−0.995^pity)/0.005 ≈ 44 at 50).
- Hero pacing: `HeroConfig.StarShardCosts`, hero-shard share/amounts in the Alien pool, featured hero amount.
- Part pacing: `PartMergeRecipe.FodderPoints/SameDefCount/Ore` per rarity; part power: `PartRarityDef.StatMultiplier`,
  `PartsConfig.SkillPowerScale` (0.25; awakened 0.3).
- Awaken Cores: arena shop price/limit, invasion chances, Burrow rare-drop chances.
- Laboratory: `LabTuningDef.BaseHours/Growth/DataBase/DataExponent`, `CampaignDef.DarkSideDataBase/Growth`, chest Data.

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
