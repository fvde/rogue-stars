# Quest / achievement counters

Append-only list of `state.Count(...)` keys: `key` — meaning — owner.

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

