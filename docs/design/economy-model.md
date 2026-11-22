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
- Parts: Mythic needs 2 Legendary copies of the same part on top of the Legendary+3 chain; Mythic+3 needs
  4 further Mythic-rarity parts. Level cap 10 (Common) → 100 (Mythic+3) + up to 6 plate raises; S/SS awakening
  costs endgame Part Awaken Cores (20 / 60).
- Mounts: 5★ costs 350 shards (35 duplicates), awakening 1–5 another 1 620 shards + 1 300 Mount Cores.

## Levers
- Mythic frequency: tokens per day and `MythicHardPity` (mean pulls per Mythic ≈ (1−0.995^pity)/0.005 ≈ 44 at 50).
- Hero pacing: `HeroConfig.StarShardCosts`, hero-shard share/amounts in the Alien pool, featured hero amount.
