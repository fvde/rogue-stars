# Quest / achievement counters

Append-only list of `state.Count(...)` keys: `key` — meaning — owner.


### City workstream (City, Blueprints, Aliens, Breeding, Timers)
- `city_areas_unlocked` — city areas unlocked via CityUnlockArea — City
- `city_buildings_built` — constructions completed (first build of a blueprint; also gates later city areas) — City
- `city_building_upgrades` — building upgrades completed — City
- `city_resources_collected` — total whole units collected from production buildings (all currencies) — City
- `blueprints_obtained` — distinct blueprints owned (first copy) — City
- `blueprint_duplicates` — duplicate blueprint copies converted to shards — City
- `blueprint_star_ups` — blueprint star increases — City
- `timer_speedups_diamond` — timers sped up with Diamonds — City
- `timer_speedups_ad` — ad speed-ups used (incl. instant ones with the no-ads entitlement) — City
- `timer_speedups_free` — daily free speed-ups used — City
- `diamonds_spent_timers` — Diamonds spent on timer speed-ups — City
- `aliens_obtained` — distinct alien types owned — City
- `alien_duplicates` — duplicate alien copies converted to shards — City
- `alien_level_ups` — alien level increases — City
- `alien_star_ups` — alien star increases — City
- `alien_assignments` — aliens assigned to work in a building — City
- `breeds_started` — breeding jobs started — City
- `breeds_collected` — children born (collected) — City
- `breeding_mutations` — born children with a mutation — City
- `breeds_kept` — children kept (replaced the current alien of the type) — City
- `breeds_discarded` — children discarded for shards/Grain — City
## Meta (written by Meta)
- `login_days` — daily login rewards claimed — Meta/Quest
- `daily_quests_claimed` — daily quests claimed — Meta/Quest
- `weekly_quests_claimed` — weekly quests claimed — Meta/Quest
- `achievements_claimed` — achievements claimed — Meta/Quest
- `tutorial_steps` — tutorial steps completed — Meta/Onboarding
- `ads_watched` — rewarded ad views consumed (any placement, incl. No Ads instant views) — Meta/Shop
- `shop_items_bought` — soft-currency shop items bought (Diamond + guild shop) — Meta/Shop
- `diamonds_spent` — Diamonds spent in Meta shops (other workstreams should add their Diamond spends too) — Meta/Shop (shared)
- `iap_purchases` — completed real-money purchases (analytics only; never used to personalise offers) — Meta/Shop
- `mail_claimed` — mails whose rewards were claimed — Meta/Mail
- `invasion_attempts_used` — invasion attempts spent — Meta/LiveOps
- `invasion_battles` — invasion battles with reported damage — Meta/LiveOps
- `invasion_damage` — total invasion damage dealt — Meta/LiveOps
- `invasion_score` — total invasion contribution score — Meta/LiveOps
- `invasions_repelled` — invasion outcomes received as "repelled" — Meta/LiveOps
- `invasion_top_contributor` — times ranked #1 contributor in an invasion group — Meta/LiveOps
- `arena_fights` — arena fights resolved — Meta/Arena
- `arena_wins` — arena fights won — Meta/Arena
- `guild_contribution` — invasion score contributed while in a guild — Meta/Guild
- `guilds_joined` — guilds joined — Meta/Guild

## Expected from other workstreams (read by Meta quests, season pass, funds, feature gates)
If a key is missing after merge the lead reconciles (rename here or in content).
- `main_level_max` — highest main portal level number cleared, kept as a **max** (set, not summed; levels are numbered globally: portal 1 = 1..10, portal 2 = 11..20, ...) — Run. Used by every feature gate (`RequirementKind.MainLevel`); alternatively wire `Requirements.SetProgressProvider(RequirementKind.MainLevel, ...)`.
- `worm_level` — current worm level, kept equal to the level (set, not summed) — Progression. Feature gates (`RequirementKind.WormLevel`), Worm Fund, achievements, new-player quests.
- `runs_completed` — runs completed (boss beaten) — Run
- `battles_won` — battles won (any mode) — Run
- `bosses_defeated` — run bosses defeated — Run
- `stamina_spent` — stamina spent — Progression/Run
- `pulls_part`, `pulls_blueprint`, `pulls_alien` — gacha pulls per type (count each single pull) — Progression
- `buildings_upgraded` — building level-ups (incl. construction) — City
- `aliens_bred` — breeding results — City
- `hero_stars_total` — sum of stars gained over all heroes (+1 per star-up) — Progression (Hero Fund)
- `mount_levels_gained` — mount level-ups — Progression (Mount Fund)
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


## Integration reconciliation (lead, after merging City, Meta, Progression)
Meta content now reads the keys the owning workstreams actually write:
- `worm_level` → `worm_level_max` (Progression). Feature gates and the Worm Fund do not read a counter at all:
  `RequirementKind.WormLevel` is served by `Requirements.SetProgressProvider` from `state.Worm.Level`.
- `hero_stars_total` → `hero_stars` (+1 per hero star-up), `mount_levels_gained` → `mount_levels`.
- `buildings_upgraded` → `city_building_upgrades` (upgrades only, constructions are `city_buildings_built`).
- `aliens_bred` → `breeds_collected`.
- Still provided by **Run** (unchanged keys): `main_level_max` (set as a max; every `RequirementKind.MainLevel` gate
  reads it), `runs_completed`, `battles_won`, `bosses_defeated`, `stamina_spent`.
- `ads_watched` now also counts stamina refill ads, timer ad speed-ups and gacha ad pulls (all go through
  `ShopSystem.ConsumeAdView`); `timer_speedups_ad` / `stamina_refills_ad` keep counting per system.
## Run workstream (RunSystem, LaboratorySystem)
- `runs_started` — runs started (any mode) — Run
- `runs_started_<mode>` — runs started per mode (`normal`, `overcharged`, `burrow`, `darkside`) — Run
- `runs_completed` — runs won and paid out — Run
- `runs_completed_<mode>` — runs won per mode — Run
- `runs_failed` — runs lost or abandoned (pending loot lost) — Run
- `levels_cleared` — first clears of a level (normal or overcharged) — Run
- `run_rolls` — dice rolls in runs — Run
- `run_battles_won` — battles won in runs (incl. events and bosses) — Run
- `run_elites_killed` — elite and miniboss battles won — Run
- `run_bosses_killed` — run bosses (incl. burrow floor bosses) defeated — Run
- `run_boss_awakenings` — boss awakening thresholds reached — Run
- `run_skills_drafted` — draft picks — Run
- `run_events_resolved` — event / campfire / treasure choices resolved — Run
- `run_revives` — revives used — Run
- `run_feats_earned` — level feats earned (first time each) — Run
- `burrow_floors_cleared` — Burrow floors cleared — Run
- `lab_research_started` — laboratory research started — Run
- `lab_research_completed` — laboratory research collected — Run
- (after Run merge) Meta quests read Run's real keys: `battles_won` → `run_battles_won`, `bosses_defeated` →
  `run_bosses_killed`. Run now also writes `main_level_max` (max-style, first normal clears) and `stamina_spent`.
- `run_rewards_doubled` — won runs whose rewards were doubled by the result-screen ad (RunDoubleRewards) — Run
- `data_spent` — Data spent on laboratory research — Run
- `invasion_rare_rewards` — rare rewards (Awaken Cores) won from repelled high-tier invasions — Meta
- (parts redesign) `part_awakens` now counts the one-time awakening of Mythic+ parts (no S/SS awakening levels any more).
- `part_replications` — Legendary copies made with the Replicator (PartReplicate) — Progression/Parts
- (endless campaign) `main_level_max` keeps growing past level 500 (the campaign never ends).
- `dark_side_level_max` — highest Dark Side level cleared, kept as a **max** (drives the Dark Side progress archive) — Run
- `milestone_chests` — one-time milestone chests / portal-completion bonuses collected (first Normal clear of every 10th main level) — Run
- `run_minigames_started` — run mini-games started (RunMinigameStart on a GAME tile) — Run
- `run_minigames_played` — run mini-games finished with any tier (RunMinigameFinish, farming included) — Run
- `run_minigames_gold` — run mini-games finished with a Gold medal — Run
