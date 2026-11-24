# Summon events — how heroes and mounts are obtained

Owner design (decided): heroes and mounts come from **temporary rotating summon events**. All numbers live in
`ContentDb.SummonEvents` (`shared/RogueStars.Rules/Content/ContentDb.SummonEvents.cs`); the system is
`Systems/Meta/SummonEventSystem.cs`; the client screens are `SummonEventScreen` (hub, draw result, packs, tasks),
`SummonExchangeScreen` and `SummonPassScreen`. Tests: `Meta/SummonEventTests` (rules), `SummonEventServerTests`
(server), `SummonEventScreenTests` (EditMode), `SummonShowcaseTests` (explicit PlayMode screenshots),
`EconomyLongHaulTests` + the simulator guard (`tools/RogueStars.Sim.Tests`).

## Schedule
- **One hero event (6 days) and one mount event (3 days) at a time**, back to back, each featuring one hero / mount.
  Global deterministic rotation from the live-ops epoch (Monday 2026-01-05 00:00 UTC): occurrence k =
  `[epoch + k·duration, +duration)`, featured = `Rotation[k mod n]`. Heroes: Star Knight → Chef → Void Pirate → Glacier
  Monk → Sparkplug → Alchemist → Beast Tamer → Shadow Noodle (each every 48 days); mounts: Scout Saucer → Magma Barge →
  Frost Frigate → Toxic Hauler → Star Lancer → Hive Carrier (each every 18 days). Occurrences start at a daily reset.
  Event ids: `hero@<k>`, `mount@<k>`.
- **Onboarding:** the events unlock with the Heroes (D10) / Mounts (D12) features. The player's **first event starts at
  the unlock** (`hero@welcome`): it features the hero / mount of the occurrence running at the unlock and lasts until the
  end of the global occurrence that contains *unlock + 4 days* (mounts: 2 days) — so 4–10 days (mounts 2–5); then the
  player is in the global rotation from the start of the next occurrence. The unlocks also grant 10 tickets of the type
  (the first 10× draw). There is **no starter hero** (heroes only come from the events; the tutorial step is "Pull in the
  hero summon event"); the Mounts unlock still gives the first mount (a Scout Saucer copy).
- **Task progress counts from the event start:** the settler joins the event (a fresh `SummonEventProgress` with the task
  counters as baselines) on the first command after it starts; counters only change through commands, so the baseline
  equals the counters at the start.

## Pulls, tickets, Luck
- A pull costs **1 ticket** (10× = 10) — `hero_tickets` / `mount_tickets`, **persistent between events** (save up across
  banners) — or Diamonds: heroes 200 / 1 800, mounts 150 / 1 350. Each pull pays **10 exchange tokens** (Star Sigils /
  Hangar Bolts, also persistent).
- **Luck (visible pity):** "Luck x / max" = pulls since the last featured copy, max **300 (heroes) / 200 (mounts)**: the
  pull that fills it is the featured copy. Luck is per type and **kept until the next event of that type** (and beyond).
  It is the Mythic counter of the Signals' pity machinery (`GachaSystem.RollTier`): the hidden Legendary soft pity (+2.5
  weight per pull after 25 without Legendary+) and hard pity (40) apply as in the Signals, and a 10× draw holds at least
  one Epic+.

| Tier (published %) | Hero event | Mount event |
|---|---|---|
| Common (55 / 55.6) | Hero XP ×400 | Fuel ×500 |
| Rare (30) | featured shards ×3 | featured shards ×1 |
| Epic (11.85 / 11.2) | featured shards ×8 | featured shards ×4 (⅔) · Mount Cores ×5 (⅓) |
| Legendary (3) | featured shards ×20 (½) · another hero's shards ×15 (½) | featured shards ×10 (½) · Mount Cores ×10 (½) |
| Mythic (0.15 / 0.2) | **featured hero copy** | **featured mount copy** |

## Duplicates → stars
- **Hero copy** (`RewardKinds.Hero`): the first unlocks the hero at 1★ (and equips it when none is equipped), every further
  copy **+1★** up to 10★; at 10★ a copy becomes 300 Star Sigils. Shards keep working (HeroUnlock 40, HeroStarUp) — the
  two roads add up.
- **Mount copy** (`RewardKinds.Mount`, same kind as before, new meaning): the first unlocks at 1★, every further copy **+1★
  up to 5★**, then pays the **mount shards of the next awakening level** (100 / 150 / 200 / 260 / 330; the Mount Cores of
  awakening are still needed), and once fully awakened 300 Hangar Bolts. **MountUnlock** (new): a mount unlocks with 30 of
  its shards, like heroes (the events pay featured shards long before the first copy).

## Per event
- **Tasks** (one ticket per reached target; claim one line or all). Targets from `--counter-rates` (Active F2P per day:
  ~120 enemies, ~3.5 won levels, ~5 mini-game medals, ~6 Alien / ~10 Part / ~3 Blueprint pulls):

| Task | Hero event (6 days) | Mount event (3 days) |
|---|---|---|
| Defeat enemies (`run_enemies_defeated`) | 50 / 150 / 250 / 500 / 1 000 | 50 / 150 / 300 / 500 / 800 |
| Win levels (`runs_completed`) | 2 / 5 / 10 / 20 / 35 | 2 / 4 / 8 / 15 / 25 |
| Win mini-games (`run_minigames_won`, Bronze+) | 2 / 5 / 10 / 20 / 35 | 2 / 5 / 10 / 20 / 35 |
| Alien pulls | 5 / 10 / 20 / 35 / 60 | 3 / 8 / 15 / 25 / 40 |
| Part pulls | 10 / 20 / 35 / 60 / 100 | 5 / 15 / 25 / 45 / 70 |
| Blueprint pulls | 3 / 6 / 10 / 15 / 25 | 2 / 4 / 8 / 12 / 20 |

  An active F2P player reaches ~4 of 5 targets per hero line and ~3 per mount line; a casual player 1–2; a binge player all.
- **Pull milestones** (pulls in this event): heroes 10 Hero XP ×2 000 · 25 featured shards ×20 · 50 tickets ×3 · 100
  featured shards ×60 · 150 **featured copy**; mounts 10 Fuel ×2 000 · 25 featured shards ×10 · 50 tickets ×3 · 80 Mount
  Cores ×30 · 120 **featured copy**.
- **Exchange shop** (tokens, limits per event): heroes — featured shards ×10 for 60 (3), Hero Summon Ticket 150 (3), Hero XP
  ×2 000 for 40 (10), Alien Tokens ×2 for 80 (3), Diamonds ×50 for 100 (5); mounts — featured shards ×10 for 80 (2), Mount
  Summon Ticket 150 (3), Mount Cores ×10 for 100 (2), Fuel ×1 500 for 40 (10), Blueprint Tokens ×2 for 80 (3). No featured
  copy in the exchange (saving tokens across eight events made 10★ far too fast; see "Tuning").
- **Pass** (a day unlocks at every reset of the event; Claim All): heroes free 1 / 1 / 1 / 2 / 2 / 3 tickets, premium 5 / 5 /
  5 / 6 / 6 / 8 + featured shards ×40 on day 6 (**$9.99**, `summon_pass_hero`); mounts free 1 / 1 / 2, premium 4 / 5 / 7 +
  featured shards ×20 on day 3 (**$4.99**, `summon_pass_mount`). The premium track is bought for the current event only and
  is retroactive for its unlocked days.
- **Event end:** unclaimed rewards do not lapse. When the next event of the type starts (on the player's first command in
  it), every reached but unclaimed task target and every unclaimed pass day of the ended event (free, and premium when bought)
  is mailed ("Hero Summon: unclaimed rewards", merged per reward, 30-day mail). Pull milestones are not mailed.
- **Packs** (`ProductKind.SummonPack`, sold only while an event of the type runs, `ProductDef.DailyLimit`): heroes $2.99 (5
  tickets + 300 Diamonds, 3/day), $7.99 (14 + 800, 3/day), $19.99 (38 + 2 000, 2/day); mounts $1.99 (4 + 200), $5.99
  (12 + 600), $14.99 (32 + 1 500). Plus the **ad ticket**: one free ticket a day per type (`summon_ad_hero` /
  `summon_ad_mount`, instant while ads are disabled or with No Ads).
- **Monetisation is off:** the products go through the existing IAP plumbing (PurchaseStart → server PurchaseComplete) and
  are not in the initial Play product set, so the client shows "Coming soon" (also whenever the server reports
  `purchasesDisabled`); the ad ticket completes instantly in the ads-disabled mode. Nothing was enabled.

## Signals, banners and old saves
- Heroes and mounts **left the Signals**. Their old share of the outcomes now feeds the events and keeps blueprint / alien
  pacing as it was: Alien Signal (~40% hero shards before) → Grain 300 / 800 (Common / Rare), Hero Summon Tickets 1 / 3 /
  10 (Epic / Legendary / Mythic); Blueprint Signal (~25% mount copies before) → Ore 300 / 800, Mount Summon Tickets 1 / 3 /
  10. The hero-themed Alien banners and the Lancer Shipyard banner are gone (the Alien rotation is Nebula Court + Swarm
  Season, both Mythic aliens).
- **Migration** (`SummonState.Migrated`, once per save): heroes, mounts, stars and shards stay as they are; a save that already
  had Heroes / Mounts gets 10 tickets of that type (its first event's 10× draw) and goes straight into the global rotation.
  A save written before summon events (no `Summons` field) loads with a fresh `SummonState`.

## Tuning (simulator, `--summon`, 3 seeds, median)
Pulls per complete event — Casual F2P 28 hero / 13 mount, Active F2P 47 / 27, Hardcore F2P 57 / 36, Active Dolphin 54 /
30, Active Whale 165 / 33 (spends ~$317 / 30 days: passes, crates, Diamonds into its target hero's events).

| Profile | Day | Heroes owned | Hero ★ sum / best | Hero copies | Mounts owned | Mount ★ sum / best | Mount awakening sum |
|---|---|---|---|---|---|---|---|
| Casual F2P | 30 / 60 / 120 / 365 | 1 / 6 / 8 / 8 | 3/3 · 13/3 · 27/4 · 51/8 | 0 / 0 / 1 / 7 | 0 / 2 / 6 / 6 | 0 · 3/2 · 11/2 · 24/5 | 0 / 0 / 0 / 2 |
| Active F2P | 30 / 60 / 120 / 365 | 4 / 8 / 8 / 8 | 10/4 · 25/5 · 38/6 · 62/8 | 1 / 1 / 4 / 12 | 3 / 6 / 6 / 6 | 5/2 · 14/3 · 21/4 · 30/5 | 0 / 0 / 0 / 18 |
| Hardcore F2P | 30 / 60 / 120 / 365 | 4 / 8 / 8 / 8 | 13/4 · 27/5 · 38/6 · 65/9 | 0 / 1 / 3 / 12 | 6 / 6 / 6 / 6 | 9/3 · 17/4 · 26/5 · 30/5 | 0 / 0 / 2 / 22 |
| Active Whale | 30 / 60 / 120 / 365 | 5 / 8 / 8 / 8 | 24/7 · 49/7 · 72/10 · 80/10 | 3 / 11 / 29 / 97 | 6 / 6 / 6 / 6 | 10/2 · 19/4 · 27/5 · 30/5 | 0 / 0 / 3 / 22 |

- `EconomyLongHaulTests`: an F2P player who puts everything into one hero (all event tickets pulled, tokens saved for that
  hero's events) reaches 10★ in ~78 weeks (seeds 50 / 78 / 84; band 9 months – 3 years); a 30× Diamond spender in ~9 weeks.
- First pass (Luck 80, 0.8% copies, a featured copy for 1 600 tokens in the exchange) gave 10★ in ~23 weeks and every hero
  9–10★ within a year; Luck 300 / 0.15%, no exchange copy and fewer featured shards brought it to the numbers above.
- Mount stars are quick (5★ is the end of the star road, not of the mount); awakening — 2 500 Mount Cores per mount — stays
  the long haul. The 60-day guard bands (`ProgressionGuardTests`) hold.

## Art (ArtCatalog keys, code-drawn fallbacks in `World/SummonArt`)
- `Art/Summon/backdrop_hero`, `Art/Summon/backdrop_mount` — hub stage backdrops (portrait, full frame).
- `Art/Summon/Heroes/<hero id>`, `Art/Summon/Mounts/<mount id>` — featured art, big (fallbacks: the hero worm sprite; the
  battle mount art / drawn saucer).
- `Art/Summon/Icons/<id>`: `hero_ticket`, `mount_ticket`, `hero_sigil`, `mount_bolt`, `luck_gem`, `diamond`, `exchange`,
  `packs`, `pass`, `tasks`, `shards`, `hero_xp`, `fuel`, `mount_core`, `token`, `grain`, `ore`, `lock`, `check`, `ad`, `star`,
  `spotlight`.

## Open questions
- Featured copies are uniformly spread over heroes by the shared Luck; a "pick your featured" or a wish list could come later.
- The whale maxes all eight heroes in ~4–5 months; if that is too fast, the premium pass / packs ticket amounts are the lever.
- Hero / mount redesign in progress: the rotation lists today's ids; new ids need adding to `Rotation` (a test checks every
  hero / mount is in it).
