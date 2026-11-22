# Quest / achievement counters

Append-only list of `state.Count(...)` keys: `key` — meaning — owner.

### Progression (economy, worm, parts, heroes, mounts, gacha)
- `stamina_refills` — stamina refills bought by any method — Progression/Economy
- `stamina_refills_diamonds` — stamina refills paid with Diamonds — Progression/Economy
- `stamina_refills_ad` — stamina refills via rewarded ad (or instant with No Ads) — Progression/Economy
- `worm_levels` — worm levels gained (sum) — Progression/Worm
- `worm_level_max` — highest worm level reached (max-style: set, not incremented) — Progression/Worm
- `worm_talent_ranks` — talent ranks bought — Progression/Worm
- `worm_talent_tiers` — talent tiers completed — Progression/Worm
- `parts_obtained` — part instances received (pulls, rewards) — Progression/Parts
- `part_rarity_max` — highest part rarity index reached, 0..14 (max-style) — Progression/Parts
- `runes_obtained` — rune instances received (incl. merge results) — Progression/Parts
- `part_levels` — part levels gained — Progression/Parts
- `part_cap_raises` — Advancement Plate cap raises — Progression/Parts
- `part_merges` — successful part merges (explicit and quick-merge) — Progression/Parts
- `parts_dismantled` — parts dismantled — Progression/Parts
- `rune_merges` — 3→1 rune merges — Progression/Parts
- `part_forges` — Forge slot unlocks — Progression/Parts
- `part_awakens` — S/SS awakenings — Progression/Parts
- `hero_shards_obtained` — hero shards received — Progression/Heroes
- `heroes_unlocked` — heroes unlocked — Progression/Heroes
- `hero_stars` — hero star-ups — Progression/Heroes
- `hero_stars_max` — highest hero star count reached (max-style) — Progression/Heroes
- `hero_levels` — hero levels gained — Progression/Heroes
- `mounts_unlocked` — mounts unlocked — Progression/Mounts
- `mount_stars` — mount star-ups — Progression/Mounts
- `mount_awakens` — mount awakenings — Progression/Mounts
- `mount_levels` — mount levels gained — Progression/Mounts
- `pulls_blueprint`, `pulls_part`, `pulls_alien` — pulls per type (a 10-pull counts 10) — Progression/Gacha
- `pulls_total` — pulls of any type — Progression/Gacha
- `pulls_legendary`, `pulls_mythic` — pull results of that tier — Progression/Gacha

