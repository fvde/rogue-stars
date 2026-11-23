# Rogue Stars — Game Design Document v0.2 (prototype spec)

Status: binding spec for the first working prototype. Numbers are first-pass tuning values and live in
content definitions, never hard-coded in systems. Placeholder art everywhere.

## 1. Fantasy and pillars
Raise a tiny limbless worm on an empty planet into a mech-enhanced god-worm who leads an alien
civilization, while unexpected invaders keep arriving. Joyful, quirky, a little absurd; deep RPG
progression that lasts for years.

1. **The run is the heart.** Dice-driven roguelike runs, auto-battles, skill drafts, high-impact events.
2. **Everything is intertwined.** Runs can drop progress for every system; the city feeds the runs.
3. **Built for years.** Every hero, mount, blueprint and alien keeps benefiting from more pulls for years;
   high stars are hard to reach and unlock meaningful skill upgrades.
4. **Paying speeds you up.** Whales progress faster, free players reach the same content slower.
   No A/B tests on prices or rewards, no prices personalised by spending.
5. **Lasting world consequences.** Invasions and planetary events modify the economy for days.

## 2. Core loop
Planet hub → pick a Portal (costs Stamina) → Run on a dice board → auto-battles, events, skill drafts →
boss → on **completion only**, run rewards are paid out → spend on Worm, Parts, Heroes, Mounts, City,
Aliens, Laboratory → stronger runs, harder portals.

## 3. The Run
### 3.1 Board
- Each Portal level defines a board: a graph of tiles. Boards branch; some loop. At a branch the player
  picks a leg (reachable tiles previewed).
- **Dice:** the player rolls dice (count per level, default 1 d6; skills/buildings can add dice or
  modifiers). The sum is the number of steps. Clean random: no forced or pre-chosen rolls.
- Tiles trigger on landing. Some tiles also trigger on **pass** (e.g. pickups); skills can add pass effects.
  Shop tiles **force a stop** when passed.
- Tiles are generated per level from weighted pools plus fixed special positions. Consumed tiles are
  replaced from the pool; some special tiles appear at most once.
- Tile valence colours: positive, negative, enemy, elite, boss, special.
- **Auto mode:** holding Roll enables auto-roll; popups auto-resolve with the default choice after a short
  delay; any tap interrupts. Auto mode is meant for easy re-runs.

### 3.2 Tile types (prototype set)
| Tile | Effect |
|---|---|
| Enemy (lv 1–3) | Auto-battle; XP and run coins by level |
| Elite | Harder battle, better loot |
| Stat buff / debuff | ± % of a stat for the rest of the run (can cost or grant stat %) |
| Loot | Run loot (pending outside-run rewards). **Each loot item carried raises the Danger level** (enemy stats +x%) |
| Treasure | Choose: open (more loot, more danger) or leave |
| Event | Narrative choice with 2–3 options, visible risk/reward text, weighted hidden outcomes allowed |
| Campfire | Heal or upgrade a skill; strengthens nearby enemy tiles |
| Shop (force stop) | Buy skills / buffs / heal with run coins; reroll with rising price |
| Hazard | Costs HP or coins, may push the token |
| Portal shard | Run collectible used by events |
| Boss tile | Dedicated per boss: interacts with the boss (buffs it, changes the board, feeds or drains the awakening meter) |
| Miniboss | Spawns at fixed points |

Risk/reward mini-events replace gambling minigames: e.g. *Unstable Reactor* (take a skill now; the boss
gains +X% ATK), *Smuggler* (trade max HP for a rare skill), *Rift Echo* (fight an echo of a strong enemy for a
part drop that only pays out on completion). No wheels, slots or card games.

### 3.3 Boss and awakening meter
The boss is visible from the start. An **awakening meter** fills per roll and from boss tiles; at
thresholds the boss acts on the board (buff enemy tiles, transform tiles, summon). Skills and city buildings
interact with the meter (slow it, drain it, convert it into rewards). The run ends with the boss fight
after a set number of rolls or when the token reaches the boss gate.

### 3.4 Run rewards and failure
- Run loot is **pending** until the boss is beaten. Fail → all pending loot is lost (no banking).
- One revive per run: watch an ad **or** pay Diamonds.
- Runs cost **Stamina**. Stamina regenerates over time; max and regen are raised by city blueprints and
  talent perks; refills can be bought (Diamonds, ads, daily free).

### 3.5 Portals, levels and modes
- Portals open one by one on the planet; each Portal = a biome with N levels (main). Each level has 3 feats
  (clear, HP left ≥ x%, beat boss within N rounds).
- **Overcharged** levels: harder variant of each portal, 2× stamina, better rewards.
- **Dark Side of the Planet:** late mode, levels with extreme modifiers, own rewards and leaderboard.
- **Burrow** (limited daily attempts): multi-floor dive with cash-out between floors; worthy rewards.
- **Invasion fights** (limited attempts): see §9.
- **Arena**: async PvP, see §10.
- **Threat curve:** enemy strength scales per level along a long curve (designed for years of content).

## 4. Combat (fully automatic, deterministic, seeded)
- Battles are turn-based in rounds with a round cap; reaching the cap with enemies alive is a loss.
- Combatants: the **Worm** (only the worm can be targeted unless a skill says otherwise), squad **Aliens**
  (attack, cast skills; untargetable by default), the **Mount** (fires mount weapons), enemies.
- Turn order: Mount → Aliens → Worm → enemies. Speed stat decides which side opens (worm side default).
- Each attacker has Basic, Rage, Combo and Counter attacks. Rage fills per action/hit; a full bar fires
  the Rage attack in addition to the basic attack. Rage may overfill to 125%.
- Hit resolution order: crit roll → dodge roll (consecutive dodges ×0.8 odds) → damage → parry.
  Combo and counter chains halve their chance each link.
- Damage: `raw = max(ATK·(1+bonuses) − DEF, 0.1·ATK·(1+bonuses))`, then × (1 − softcap(DR)/100),
  × crit multiplier `(1 + CritDmg·(1 − critRes))`, × conditional bonuses (vs boss, vs >70% HP, vs <40% HP,
  vs shielded). `softcap(x) = min(x, 100x/(x+100) + 10)` for DR, dodge, crit chance (+15 knee for crit).
  True damage ignores DEF and DR. No random damage variance.
- Statuses: Burn, Freeze, Poison, Bleed, Stun, Shock, Charm(Alien) — stacks with caps, durations,
  per-target application cooldown; reapplying at max stacks refreshes duration.
- Shields absorb before HP; overheal may convert to shield via skills.
- Battle speed ×1/×2 free; ×3 included in the monthly pass. No battle skip in the prototype.

### 4.1 Stats
HP, ATK, DEF, Speed, CritChance, CritDamage, CritResist, Dodge, DodgeIgnore, ComboChance, CounterChance,
ParryChance, ParryReduction, DamageReduction, LifeSteal, Regen, RageGain, ShieldPower, HealPower,
StatusChance, StatusResist, BossDamage, ElementDamage(per element), AlienPower, MountPower, LootLuck, XpGain.

## 5. Skills
- One universal **Skill** format for every power source: run picks, Laboratory research, hero traits,
  mount weapons, alien archetype/combat skills, building boons. A skill = passive stat package + triggered
  effects (condition → effect).
- **Families (build archetypes):** Fire, Ice, Lightning, Poison, Bleed (physical), **Alien Synergy**,
  **Counter**, **Combo**, **Crit**. Loose coupling: Parts, heroes and aliens boost "their" families
  without locking others out. Matching your build should feel great: the draft gives a synergy bonus slot.
- Rarities: Common, Rare, Epic, Legendary, Mythic.
- Level-up in a run → draft "choose 1 of 3" (rerolls from meta). A hidden legendary pity guarantees a
  Legendary after N level-ups without one. Skills can have upgrade chains, prerequisites and family gating.
- **Laboratory:** research unlocks skills into the run pool and upgrades them. Researched skills can be
  toggled on/off for the draft pool.

## 6. Worm progression (built for years)
### 6.1 Worm level
Biomass raises worm level (base HP/ATK/DEF growth). Talent road with tiers granting perks (stamina cap,
draft rerolls, run coins, speed).

### 6.2 Body slots and Parts
Six slots: **Head, Body, Tail, Legs, Left Arm, Right Arm** (arms interchangeable; arm parts are weapons and
define Basic/Combo/Counter/Rage attack styles).
- **Rarity ladder (merge):** Common → Uncommon → Rare → Rare+ → Epic → Epic+1 → Epic+2 → Legendary →
  Legendary+1 → Legendary+2 → Legendary+3 → Mythic → Mythic+1 → Mythic+2 → Mythic+3. Merging consumes copies
  or fodder per rule; level, runes and infusion carry over.
- **Level** via Ore + Gear Cores; level cap raised by rarity and Advancement Plates.
- **Rune slots** (0→4) unlocked by rarity and by the **Forge** (paid slot unlocks with a % stat bonus to the
  part's base stats). Runes have types, rarities and merge 3→1 upward.
- **SS awakening:** Legendary+ parts can awaken into S-tier, then SS-tier versions with an extra signature
  effect. Endgame reward sink.
- Dismantle/level-down refund 100% of level materials.

## 7. Heroes, Mounts, Aliens
### 7.1 Heroes (traits for the worm)
- The worm equips one **Hero persona** (e.g. "Star Knight Worm", "Chef Worm"). Heroes give the worm a
  defining set of traits and attack visuals. Unlock by shards; **stars 1–10** via shards; each star raises
  stats and at stars 3/5/7/10 upgrades or adds a trait skill (meaningful skill improvements at high stars).
- Hero level via Hero XP. Duplicates always become shards; shards keep value at every star.

### 7.2 Mounts (spaceships)
- One active mount; mounts fire weapons in combat and absorb part of the damage. **Stars 1–5**, then
  **awakening levels 1–5** (both via cores from duplicates). Weapon skill upgrades at stars 2/4/5 and
  awakening 3/5. Mount level via Fuel.

### 7.3 Aliens
- One alien **per alien type** (species). Each has: archetype skill (its typical function), stats (combat
  and work), up to 3 traits (combat or city), rarity, stars 1–10 via duplicates (shards), level via Grain.
- **Work:** assign to city buildings; work stats boost production.
- **Fight:** squad slots unlocked by progression: 0 at alien unlock, 1 after first housing, 2 and 3 via
  city upgrades / rare blueprints. Aliens act in turn and cast their skills; they are not targetable by default.
- **Breeding Pen:** pick two aliens; costs Grain and time (hours by rarity; buildings speed it and improve
  stat/trait quality). Child inherits a mix of stats/traits with mutation chance. When born the player
  chooses: replace the current alien of that type, or discard (convert into shards/Grain).

## 8. City and Blueprints (replace the artifact system)
- The city is a grid on the planet unlocked area by area (early game = unlocking city elements one by one:
  Biomass farm, Grain farm, Ore mine, Housing, Laboratory, Breeding Pen, Diamond mine, Observatory, …).
- **Blueprints** are pulled (gacha) or earned. Place a building, level it (Ore + Biomass + time), raise its
  stars with duplicate blueprints (1–10 stars; star milestones add effects). Rarity: Common → Mythic.
- **Production buildings** generate resources over time (settled from timestamps); alien workers boost
  output by their work stats/traits. Player sets a **production priority** (which resource gets the boosted
  share).
- **Boon buildings** give run effects: stamina max/regen, start run with X, skip X negative events per run,
  increased loot/XP in run, extra dice, 1–3 skills at run start (extremely rare Epic+ buildings), awakening
  meter slowdown, squad slot unlocks.
- **Collection bonus:** owned blueprints give small permanent stats by stars (long-haul pull value).
- Construction and upgrade timers: speed up with Diamonds (linear published rate) or ads (each ad −15% of
  the remaining time, max 3 ads per timer).

## 9. Invasions and Planetary events
- **Invasions:** scheduled (weekly) and surprise. Players are batched into small groups (≈20) that fight
  the invader together (async damage contribution, limited attempts). Outcome for the group: repelled →
  lasting bonus modifier; overrun → lasting malus modifier. Top contributors get extra rewards.
- **Planetary events:** temporary economy modifiers (e.g. Diamond output ×2 for 7 days, Grain ÷4), sometimes
  with surprising side effects. Modifiers apply to city production and run rewards.
- A **modifier stack** (source, target, multiplier, start, end) is part of the economy model; all
  production/reward calculations read it.

## 10. Arena (async PvP)
Fight snapshots of other players' worms (loadout, parts, hero, mount, squad). Weekly season, leagues in
groups of 150, promotion/demotion, weekly rewards by rank. Attempts via Arena tickets (regenerate).

## 11. Guilds
Create/join, roles (leader, officer, member), guild level from activity, guild chat only, weekly guild
invasion contribution, small guild shop. Purchase-triggered guild gifts later.

## 12. Gacha (three pull types)
| Pull | Free pull resource | Contents |
|---|---|---|
| Blueprint Signal | Blueprint Tokens | Blueprints |
| Part Signal | Part Tokens | Parts, runes |
| Alien Signal | Alien Tokens | Aliens, hero shards (hero shards may also come from a hero banner) |
- Each pull type can always be paid with Diamonds instead.
- Weighted rarity tiers; **hidden pity** per pull type (shared across banners of that type, carries over);
  10-pull guarantees at least one Epic+. Duplicates convert to shards/stars.
- **Themed rotating mythic banners** for all three pull types (one featured Mythic item per banner).
- Target: an active F2P player reaches mythic pity at least once a week.

## 13. Currencies
| Currency | Use |
|---|---|
| Biomass | Worm level, building upgrades |
| Grain | Alien levels, breeding, upkeep |
| Ore | Parts level, buildings |
| Gear Cores, Advancement Plates | Part level / cap |
| Fuel | Mount levels |
| Hero XP | Hero levels |
| Diamonds | Premium; also earnable (city Diamond mine, quests, events) |
| Blueprint/Part/Alien Tokens | Free pulls |
| Shards (per hero/alien/blueprint), Mount Cores | Stars |
| Stamina | Runs (regenerating) |
| Arena Tickets, Burrow Keys, Invasion Attempts | Mode entry (regenerating / daily) |
| Run Coins | In-run only |
| Guild Marks | Guild shop |
All currencies live in one string-keyed wallet with a generic regen engine (cap, per-tick amount, tick
length, temporary cap boosts, rewards may exceed cap).

## 14. Monetization (mainstream RPG model, no A/B, no spend-based pricing)
- Diamond packs (fixed price ladder), starter packs, rolling and flash offers (same for everyone),
  periodic gift packs (daily rewards over N days).
- **Permanent one-time purchases** that everyone should want: *No Ads* (all ad placements become instant),
  *Daily Diamonds* (X Diamonds per day forever).
- **Monthly pass**: daily Diamonds, battle speed ×3, extra patrol/production, stamina cap +.
- **Season pass** (4 weeks): free + premium track, premium is retroactive.
- **Funds** (one-time paid reward tracks per system: worm level, hero, mount, city).
- Rewarded ads in many placements (stamina refill, revive, timer −15%, extra free pull per day, double run
  rewards), each with daily caps; ad-free purchase makes them instant.
- Whales progress faster; F2P reaches the same content slower. Drop rates published.

## 15. Retention
Daily login (7-day cycles, no streak loss), daily/weekly quests and achievements with point tracks,
new-player 7-day event, mailbox, idle production (city), push notification hooks (local), returning-player
gifts. Lobby popups capped.

## 16. Onboarding / unlock order
Worm wakes → first Portal → first Part drop → Biomass farm → Worm level → Laboratory → Grain farm →
Aliens arrive (first alien) → Housing (squad slot 1) → Blueprint pulls → Ore mine → Heroes → Mounts →
Breeding Pen → Arena → Guilds → Invasions → Overcharged → Burrow → Dark Side. Gates are content-defined
(chapter cleared or other requirement); locked features are visible with their requirement.

## 17. Technical architecture
- **Shared rules** (`shared/RogueStars.Rules`, netstandard2.1, C# 9): all game logic, content definitions,
  deterministic RNG, command handlers. Compiled into both the Unity client (local UPM package) and the server.
- **Server** (`server/`, ASP.NET Core, .NET 10): runs the same command handlers authoritatively; Firestore
  persistence; Firebase Auth token verification (dev auth locally); cross-player features (arena, guilds,
  invasions groups, leaderboards).
- **Client** (`client/`, Unity 6.6, Android): placeholder UI built in code; can run fully offline using the
  shared handlers locally, or against the server.
- **Infrastructure** (`infra/terraform`): GCP Cloud Run, Artifact Registry, Firestore, service account,
  Secret Manager, Cloud Scheduler.
- Unit tests for all rules (xUnit) and server endpoints.
