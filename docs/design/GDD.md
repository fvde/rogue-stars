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
- **Auto mode:** holding Roll enables auto-roll; any tap interrupts. Auto only automates the **clear, choice-free**
  steps: rolling, moving, tiles without options (loot, stat buff/debuff, hazards, pickups, boss tiles) and battles
  (fast playback). At every **real decision** — shop (forced stop: the player can buy skills), skill draft, event,
  treasure (open or leave), campfire, branch, revive prompt, Burrow descend/cash-out — auto **pauses**, shows the
  decision and hands control back; after the player decides it resumes by itself while auto is still on. Giving up
  when no revive is left is not a choice and is automatic.
- **Auto-pick drafts (farming)**, a player setting, off by default: for easy re-runs auto also takes the default
  choice (best draft pick, the event's default option, first branch, give up when defeated, cash out in the Burrow).
  It never buys in shops: farming leaves shops untouched. Rules: `RunSystem.Decision` / `RequiresPlayer`;
  `RunAutoStep` refuses a player decision unless `Farming` is set (`FarmShops` adds the simulator's heal-below-50%
  shop heuristic, not offered to players).

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
- **The main campaign is endless:** after the 50 named portals (levels 1–500) new portals keep coming on the same long
  threat curve, generated from the portal number (adjective + biome names, the four biome families in turn, family
  bosses in six rotating variants with new boss-tile combinations every 5 portals). Per-run rewards keep growing more
  slowly than threat (threat^0.7), every 10th level pays a one-time milestone chest (Diamonds + one token of each pull
  type) and every 50th a bigger portal-completion bonus; run item drops shift up in rarity past level 500 but never
  above Legendary. Formulas: `docs/design/economy-model.md` ("Endless campaign rewards").
- **Overcharged** levels: harder variant of each portal, 2× stamina, better rewards.
- **Dark Side of the Planet:** late mode (opens at main level 40), endless procedural levels along the campaign curve (dark
  level i ≈ main level 40 + 3·(i−1)) with extreme modifiers (+45…90% enemy HP/ATK, −50% healing, faster awakening and
  danger), own rewards and leaderboard. The long-term source of **Data** for the Laboratory (Data per clear grows with
  the level).
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
- **Laboratory:** one branching tree of 38 nodes in four branches (offense, defense, elements, command). **Skill
  nodes** unlock skills into the run draft pool and upgrade them (toggle on/off for the draft pool); **stat nodes**
  give permanent loadout bonuses of +1% to +10% per node (HP, ATK, DEF, crit chance/damage, combo, counter, dodge,
  damage reduction, shield power, rage gain, element damage, status chance, boss damage, alien power, mount power, loot
  luck). Both kinds are mixed in every branch, so the player chooses between raw stats and new skills; a node of row
  n+1 needs any node of row n at level 2, capstones need two nodes (all). Five levels per node.
  - **Cost:** research costs **Data** (no Biomass/Ore). Every research makes the next one longer: the k-th research
    level takes h = 1.5 h × 1.0355^k nominal and costs 14·h^0.75 Data (`LabTuningDef`). The whole tree (187 levels
    beyond the three default ones) is ≈ 28.7k nominal hours and ≈ 95k Data.
  - **Size:** with one slot, all three ad speed-ups per timer (×0.85³) and the daily free speed-up (30 min), the tree
    takes ≈ 2 years (1.97); even with the strongest Laboratory building boon (+27%) and a Research Annex from day 1 a
    year of perfect timing clears at most ≈ 27.5k of the 28.7k hours (checked by `Tree_CannotBeFinishedInYearOne_…`).
  - **Research Annex** (Mythic blueprint): a second, concurrent research slot while two alien workers of 3★ or more
    staff it; research in the Annex takes twice as long.
  - **Data:** Dark Side clears (main source, grows with the Dark Side level), daily/weekly point chests (trickle so the
    lab works before the Dark Side), the Laboratory unlock grant and the arena shop.

## 6. Worm progression (built for years)
### 6.1 Worm level
Biomass raises worm level (base HP/ATK/DEF growth). Talent road with tiers granting perks (stamina cap,
draft rerolls, run coins, speed).

### 6.2 Body slots and Parts
Six slots: **Head, Body, Tail, Legs, Left Arm, Right Arm** (arms interchangeable; arm parts are weapons and
define Basic/Combo/Counter/Rage attack styles).
- **Rarity ladder (merge):** Common → Uncommon → Rare → Epic → Legendary → Mythic → Mythic+1 → Mythic+2 →
  **Mythic S** → S+1 → S+2 → **Mythic SS** → SS+1 → SS+2 (`docs/design/parts.md` has the full recipe table). Below
  Mythic any three parts of a rarity merge into one (any def, any kind), so surplus always becomes Legendary fodder;
  above Mythic a merge needs Legendary+ fodder worth 2…340 Legendary-equivalents (fodder points; ~×1.7 per step), Ore
  (1.5k, ×1.5 per step) and, for Mythic, S, SS and SS+2, copies of the part's own def (the **Replicator** turns 10
  Legendary-equivalents of surplus + Ore into such a copy). Level, runes and Forge carry over.
- **Part skills:** every part has five skills that unlock at **Epic, Legendary, Mythic, Mythic S and Mythic SS** (family
  core, kind technique, family hook, kind signature, family apex).
- **Level** via Ore + Gear Cores; level cap raised by rarity (10 … 115) and Advancement Plates (up to +30).
- **Rune slots** (0→5) unlocked by rarity and by the **Forge** (paid slot unlocks with a % stat bonus to the
  part's base stats). Runes have types, rarities and merge 3→1 upward.
- **Awakening:** a Mythic-or-higher part can be awakened **once** for **50 Awaken Cores**: it becomes a redesigned
  version of itself (new name, ×1.4 stats + its family stat, all five skills replaced by awakened redesigns with the
  same unlock pattern). Level, runes, Forge and rarity are kept. **Awaken Cores** are an extremely rare endgame resource:
  the arena shop (Arena Medals, weekly limit), top contributors of repelled high-tier invasions (chance) and a rare drop
  on the deepest Burrow floors — about 1–2 per week for an active player.
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
- **Staffed buildings:** some utility boons need workers (e.g. the Mythic **Research Annex**: a second laboratory slot
  while two aliens of 3★+ work there).
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
groups of 150, promotion/demotion, weekly rewards by rank. Attempts via Arena tickets (regenerate). Every fight and the
weekly rank pay **Arena Medals** (PvP currency) for the **arena shop**: Awaken Cores (200 medals, 2 per week), part
tokens, Data, Gear Cores.

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
| Biomass | Worm level, talents, building upgrades. Mainly from portal runs (grows with the level; the first 3 won portal runs a day pay ×4), city farms second |
| Grain | Alien levels, breeding, upkeep |
| Ore | Parts level, Mythic+ merges, buildings |
| Data | Laboratory research (Dark Side clears, daily/weekly chests, arena shop) |
| Awaken Cores | Part awakening (arena shop, high-tier invasions, deep Burrow floors; extremely rare) |
| Arena Medals | Arena shop (arena fights and weekly rank) |
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

### 13.1 Rewards follow use
A reward is only granted while the player can use it. Every currency and item kind has an entry in
`ContentDb.RewardUses` (`Content/ContentDb.RewardGate.cs`, one place; tests require an entry for every currency in the
content) saying what spends it and when the player has that use (any of the listed features, or owning a part of the
needed rarity). Every grant (run loot and completion, milestone chests, quests and point chests, login and new-player
calendars, mail, shop, pulls, events, unlock gifts, the starting wallet) passes the reward gate (`RewardGate`): a reward
without a use does not reach the wallet; it waits in the **reward stash** and is released through its normal granter the
moment the use unlocks. The unlock moment shows what it released ("3 Parts", "+5 000 Biomass").

- **Free sources** (runs, quests, chests, calendars, events, ads) keep at most the entry's stash cap (roughly the first
  uses after the unlock: a small welcome stash, never a backlog); the rest is dropped. Item kinds count rewards and keep
  the best rarities.
- **Paid and explicit sources** (purchases, pass and fund claims, items bought with a currency, mail, pulls) are kept in
  full. A pull result is always shown (published odds); one without a use is marked "kept for later".
- Regenerating entry currencies (arena tickets, burrow keys) do not regenerate before their feature; invasion attempts
  start with the Invasions unlock. City production of a currency without a use stays in the building's storage.
- The starting wallet holds only what day 1 uses (60 stamina, 300 Diamonds); the rest of the old starting wallet comes with
  the unlock that uses it (500 Ore with Parts, 500 Biomass with the City, 10 Part / 5 Alien / 5 Blueprint tokens with
  their Signals, 300 Grain with the Aliens).
- The client shows only currencies with a use (resource bar, details) and leaves rewards without a use out of every
  reward display (run result, level details, chests, calendars, quests, mail, shop; offers and shop items whose contents
  all lack a use are hidden).

| Currency / item | Spent on (rules) | Has a use when | Stash cap (free) |
|---|---|---|---|
| Stamina | runs | always | – |
| Diamonds | stamina refills, revives, timers, pulls, shop | always | – |
| Biomass | worm levels and talents, city buildings and areas | City (`biomass_farm`, D2) or Worm Level (D3) | 5 000 |
| Ore | part levels and merges, Replicator, city buildings | Parts (D1, level 1), City or Ore Mine | 1 000 |
| Gear Cores | part levels 11+ (Uncommon+ parts), Forge | an Uncommon+ part is owned, or Forge (D20) | 200 |
| Advancement Plates | part level cap raises | a Rare+ part is owned | 20 |
| Awaken Cores | part awakening | a Mythic+ part is owned | none |
| Grain | alien levels, breeding | Aliens (D7) or Breeding | 2 000 |
| Fuel | mount levels | Mounts (D12) | 4 000 |
| Hero XP | hero levels | Heroes (D10) | 3 000 |
| Data | laboratory research | Laboratory (D5) | 100 |
| Part / Alien / Blueprint Tokens | free pulls | Part Signal (D4) / Alien Signal (D7) / Blueprint Signal (D9) | 30 each |
| Mount Cores | mount awakening | Mounts | none |
| Arena Tickets / Arena Medals | arena fights / arena shop | Arena (D15) | 5 / none |
| Burrow Keys | Burrow runs | Burrow (D19) | 2 |
| Invasion Attempts | invasion battles | Invasions (D18) | 3 |
| Guild Marks | guild shop | Guilds (D17) | none |
| Hero / Alien / Blueprint / Mount shards | stars (hero unlock, mount awakening) | Heroes / Aliens / City / Mounts | none |
| Parts, Runes, Blueprints, Aliens, Mounts, hero shard rewards | – | Parts / Runes (D11) / City / Aliens / Mounts / Heroes | Runes 5 (best kept), others none |

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
A three-week journey, one new thing at a time (onboarding v2). Every feature has a gate = **main level cleared AND
earliest account day** (`RequirementKind.AccountAgeDays`, counted in daily resets: day 1 = the creation day, day 2 starts
at the first reset after it). Day-1 unlocks are not limited; after that **at most one unlock group per day** (the lowest
pending one, deterministic), so a binge player hits the day gate, a slow player the level gate, and a returning player
catches up one group per day. Pairs ("X + Y") are one group and unlock together.

| Day | Level | Unlock (feature id) | Guided action |
|---|---|---|---|
| 1 | – | only PLAY: the scripted first run (level 1 eased −35% so a worm without parts wins it) | – |
| 1 | 1 | Parts / Worm tab (`parts`): equip the three starter parts | Equip your parts |
| 1 | 5 | Daily goals (`quests`) | See today's goals |
| 2 | 8 | City with one plot: Biomass Farm (`biomass_farm`) | Build the farm |
| 3 | 6* | Worm level & talents (`worm_level`) | Level up the worm |
| 4 | 12 | Part Signal + Shop (`part_pulls`, `shop`); the first pulls are on the starting tokens | Tune in |
| 5 | 14 | Laboratory (`laboratory`) | Start first research |
| 6 | 16 | Grain Farm (`grain_farm`) | Build the grain farm |
| 7 | 18 | Aliens (first alien) + Alien Signal (`aliens`, `alien_pulls`) | Meet your alien |
| 8 | 20 | Housing → first squad slot (`alien_squad`) | Build housing |
| 9 | 22 | Blueprint Signal (`blueprints`) | Pull a blueprint |
| 10 | 24 | Heroes (`heroes`) | Choose a hero |
| 11 | 26 | Ore Mine + Runes (`ore_mine`, `runes`) | Build the ore mine |
| 12 | 28 | Mounts (`mounts`) | Saddle up |
| 13 | 29 | Funds (`funds`; not in the approved list, fills the free day 13) | See the funds |
| 14 | 30 | Breeding Pen + Season pass (`breeding`, `season_pass`) | Build the pen |
| 15 | 32 | Arena (`arena`) | Enter the arena |
| 16 | 34 | Overcharged levels (`overcharged`) | Play overcharged |
| 17 | 36 | Guilds (`guilds`) | Find a guild |
| 18 | 38 | Invasions (`invasions`; also opens the hub's live-event column and the Observatory) | Defend the planet |
| 19 | 40 | Burrow (`burrow`) | Dig in |
| 20 | 42 | Forge (`forge`) | Open the forge |
| 21 | 45 | Dark Side of the Planet (`dark_side`) | Cross over |

\* Approved as level 10. At level 10 casual players cannot clear the gate level without worm levels and stall for
~12 days (simulator); level 6 keeps day 3 for ~1 h/day and binge players. See `docs/balance/onboarding-unlocks.md`.

**Visibility:** locked features are invisible — no greyed-out entries. Only the single next group is teased
(`Features.Next`: "Next: Laboratory — clear level 14", "unlocks tomorrow"). Day-1 hub of a brand-new account: PLAY, the
Planet tab, the teaser, the stamina and Diamonds chips and the profile chip; the Worm tab (and Mail) join after level 1. Mail and the
currency chips follow use (§13.1): a currency shows once the player can use it, so a brand-new account shows the stamina and
Diamonds chips only (Diamonds buy stamina refills).

**Hub:** bottom tabs for daily features appear one by one (Worm, City, Planet in the centre, Signals, Arena); left
column = daily goals and live events (invasions, planetary/limited events, from the Invasions unlock); right column =
shop, offer, pass, mail, "More" (laboratory, aliens, heroes, mounts, breeding, guild); the PLAY card (Continue during a
run) sits above the tabs.

**Unlock moment:** each unlock opens a full-screen celebration on the hub (never mid-run): the feature's name, one line
of what it does, the reward (unlock gift and items), ONE guided action that claims the gift and opens the feature, and
the next teaser. Starter items arrive with the unlock (`UnlockGrants`: first alien, blueprints, Data, hero shards...) together
with the reward stash kept for the feature (§13.1), e.g. the starter parts and the first runs' Ore with Parts.

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
