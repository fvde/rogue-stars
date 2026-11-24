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
  picks a leg: the board highlights, for each leg, the path and the tile the worm would **land on** with the steps
  it has left (A / B markers; tap the landing tile or the leg's first tile).
- **The board is a long path that never repeats** (playtest 0.1.86: the looping circuit felt weird and the same fork came
  back three times in one run). It is generated ahead of the worm in **segments** (`RunBoard.AppendSegment`): each segment
  is a trunk of the level's `BoardLength` (≈ 25–45 tiles with one die, ×1.5 with two) with forks that rejoin further
  ahead (**both arms 8–11 tiles**, `RunTuning.BranchMinArm/MaxArm`: no short cut-throughs), the level's fixed specials
  (campfire, two shops, miniboss lair) **on trunk tiles only** (every path passes them: a fork never bypasses a miniboss or
  a shop), the boss's tiles and a pool fill with the
  segment's own mix (`RunTuning.SegmentThemes`: Hunting Grounds, Old Ruins, Badlands, Quiet Valley; the first segment is
  plain). Edges only point forward, so no tile and no fork is visited twice; the rules append a segment whenever the open
  end is closer than the largest roll + 16 steps, so the worm never runs out of board (45 rolls of two dice included).
  Resolved tiles stay what they were and are drawn cleared. The boss does not wait at a gate: when its awakening meter
  is full (§3.3) its **arena opens right ahead of the worm** (the path ends there) and the next step walks in and fights.
- **Path shapes:** every segment gets a 2D layout style (`RunTuning.BoardLayouts`): *Long Straight* (gentle drift, forks
  are side loops: one arm runs on, the other swings out and back), *Zig-Zag* (switchbacks every 4 tiles), *Wide Curve*
  (slow S-bends), *Hook* (leans out and curls hard before hooking back) and *Diamond Splits* (calm trunk, more forks, each
  a symmetric diamond). The rules give every tile `RunTile.PosX/PosY` (tile units, +Y forward; trunk within ±1.3, arms
  within ±2.2, neighbours ~1 apart, no two tiles closer than 0.85) and `Dir` (path heading); display only, no randomness.
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
  treasure (open or leave), campfire, branch, revive prompt, Burrow descend/cash-out, mini-game (§3.6) — auto **pauses**, shows the
  decision and hands control back; after the player decides it resumes by itself while auto is still on. The result screen of an event / chest / campfire / shop (§3.3) is shown for a moment and passed by
  auto. Giving up
  when no revive is left is not a choice and is automatic.
- **Auto-pick (farming)**, a player setting, off by default: for easy re-runs auto also takes the default choice (best
  draft and mythic pick, the event's default option, first branch, give up when defeated, cash out in the Burrow) and
  **buys sensibly in shops** (`RunSystem.FarmShopPick`: the heal below half HP, then the skills and stat perks the draft
  logic wants most, one purchase per step, never a reroll; leaves when nothing affordable is worth it). Rules:
  `RunSystem.Decision` / `RequiresPlayer`; `RunAutoStep` refuses a player decision unless `Farming` is set. Every event,
  chest and campfire is a player decision, even with a single option; a boon that keeps a negative event away and a
  looted-out cache both show a result instead of being skipped silently. Encounters (a miniboss attacking, the boss
  arena opening) are clear steps of their own, so every fight gets its own playback.

### 3.2 Tile types (prototype set)
| Tile | Effect |
|---|---|
| Enemy (lv 1–3) | Auto-battle; XP and run coins by level |
| Elite | Harder battle, more XP and coins |
| Stat buff / debuff | ± % of a stat for the rest of the run (can cost or grant stat %) |
| Supply cache | Run coins and run XP (in-run resources only) |
| Treasure (guarded) | Choose: fight its elite guardian for 2 account-loot items (more danger, stronger nearby enemies) or leave |
| Event | Narrative choice with 2–3 options, visible risk/reward text, weighted hidden outcomes allowed |
| Campfire | Heal or upgrade a skill; strengthens nearby enemy tiles |
| Shop (force stop) | Two per segment. 4 skills (owned ones offered as level-ups), 2 stat perks and a heal for run coins; reroll with rising price |
| Hazard | Costs HP or coins, or shortens the next roll (Gravity Vortex; the path never goes back) |
| Portal shard | Run collectible used by events |
| Boss tile | Dedicated per boss: interacts with the boss (buffs it, changes the board, feeds or drains the awakening meter) |
| Miniboss | A lair per segment (fight on landing) plus three that attack with the awakening clock (25/50/75%). Each beaten miniboss grants a **MYTHIC choice** |
| GAME (Alien Arcade) | A short skill mini-game (§3.6) for run coins or a skill roll; stops the worm when passed |

**Account loot is rare and always risky.** (Once a run's opportunities are used up, the chests ahead turn into supply
caches, so no chest does nothing.) Rewards that outlive the run (currencies, parts, tokens: "account loot") are
paid mainly by the level's completion rewards (+ 3 loot-table rolls) and first-clear milestones. During a run they come
only from **at most 3 opportunities per run** (`RunTuning.MaxAccountLootOffersPerRun`; per floor in the Burrow): guarded
Treasure Pods and a few events, each with a clear price — a fight (elite / guardian / echo), a heavy loss (HP, max HP)
or a lasting effect for the rest of the run (e.g. "the boss awakens 15% faster", "−8% max HP"). Later caches are
empty / replaced by other events. Ordinary tiles pay in-run resources (coins, XP, HP, temporary buffs). The option text
says "account loot, paid on clear" and names the risk; the result screen lists the item and the price paid. **Each
account-loot item carried raises the Danger level** (enemy stats +x%).

Risk/reward mini-events: e.g. *Unstable Reactor* (take a skill now; the boss gains +X% ATK), *Smuggler* (trade max HP
for a rare skill), *Rift Echo* (fight an echo of a strong enemy for a part drop that only pays out on completion). No
pure-chance gambling: the arcade's mini-games (§3.6) are skill games; even *Slot Pod* is a timing game played for
in-game pod coins, never for real money or Diamonds.

### 3.3 Boss and awakening meter
The boss is visible from the start. An **awakening meter** fills per roll (`BossDef.AwakenPerRoll` 3.6 points) and from
boss tiles (feed +10/+12, relief −10, tribute +5 with run coins) and some event choices; at thresholds (25/50/75) the boss
acts on the board (buff enemy tiles, transform tiles, summon) and at 100 it is fully awake (+15%). **The meter is the
run's clock: when it is full the boss's arena opens right after the current move** and the next step walks in and
fights (at the latest after the level's hard roll limit, `LevelDef.MaxRolls` 45). Without skills a run lasts **~30
rolls** (27–33 on average for every level band, one or two dice; base pace 28 rolls, relief tiles and choices shift
it); runs give ~8–10 level-up drafts (run XP curve 3 + 1 per level; **one XP grant — a battle, a pickup, an event — gives
at most one level-up**, the overflow carries (at most two levels' worth) and pays one level per roll) and ~10–13 fights. Skills and city buildings interact with the meter: slowing it (Stasis
Beacon 5–18%, Calm Mind 9 / 15 / 24% slower, lab/draft skills) makes the run longer (more drafts, coins and fights, more time
with danger), events and boss tiles can speed it up. The HUD shows **"Roll n/N"**: N = the roll after which the boss is
expected at the current pace (`RunSystem.BossRollEstimate`), and the boss banner shows "x rolls left". Board fights are
a little softer than the threat curve (`RunTuning.BoardEnemyPct` −15%) and every won fight heals 15%, since a run has
~3× the fights of the old short runs. Burrow floors are short (6 rolls, the boss comes at the roll limit).

**Build depth** (playtest: a run should end with ~20–40 skills and perks; progression report "Run depth"):
- **Minibosses:** three come with the awakening clock (at 25 / 50 / 75%, `RunTuning.MinibossAtMeter`): after that move
  a lieutenant of the boss attacks (an Encounter step, its own fight). The segment's miniboss lair adds one when the worm
  lands on it. Every beaten miniboss grants a **MYTHIC choice**: 1 of 3 Mythic skills (30 Mythics: two per family plus
  three generic, family gates waived for this choice; Mythics never level, so owned ones are not offered again). Minibosses
  pay coins, no XP.
- **Skill tiers and levels** (owner decision, playtest): run skills have exactly **three fixed tiers — Common, Legendary,
  Mythic** — and never upgrade into another tier (the old upgrade chains are one skill each). A skill drafted, bought or
  forged again levels up to its tier's cap: **Common Lv3, Legendary Lv2, Mythic Lv1** (`RunTuning.MaxSkillLevelOf`). A level
  improves the skill's **own** effect along the **level ladder** (`SkillLeveling`, `RunTuning.SkillLevelLadder` 15 / 25 /
  40; owner: "+15% at level one, 25% at level 2, 40% at level 3 — the upgrades deepen the commitment to the build but yield
  less than the first pick"): a Common skill's numbers ×1, ×5/3, ×8/3 (a typical Common: +15% → +25% → +40%), a Legendary's
  ×1, ×8/5 (~+25% → +40%); Mythics are one strong package. Every trigger grows one number: stats, damage / heal / shield /
  rage / coins / buff values, and for status / dice triggers the chance — capped at 100%, past it the stacks grow (or the
  duration of single-stack statuses such as Freeze). Numbers round to readable steps and every level changes the text.
  No generic ATK / HP per level. `SkillText.Effects(content, skill, level)` / `Title` ("Spiked Hide Lv 2/3") show the
  leveled numbers; an owned skill offered again shows the target level ("Keen Eye Lv 2") and the upgrade inline
  (`SkillText.Upgrade`: "+15% → +25% crit chance").
- **Shops:** two per segment (forced stops), 4 skills (owned ones as level-ups at lower weight), 2 different stat perks
  (+8% ATK, +10% HP, +10% DEF, +5% crit, +8% Speed, +4% life steal, +4% damage reduction) and a heal. Prices: skills
  **40 / 80 / 120** (Common / Legendary = 2× / Mythic = 3×; Dark Side 60 / 120 / 180), perks 40, heal 30, reroll 10 + 10 per
  reroll. A run **starts with the cheapest skill price + 30 coins** (`RunSystem.StartCoins`) and its first shop leads with a
  Common skill, so the first shop always allows a purchase. No result screen after a shop. Income ~3× (fight coins 10
  per tile level + 6 per enemy, elites 25, supply caches 25 / 50, coin clusters 12, minibosses +30): a run earns ~550–800
  coins and buys 10–20 perks. Shops spend in-run coins only.
- **Pacing against the bigger build:** board fights get +0.8% HP/ATK per roll of the run so far (the boss's forces grow
  while the build grows) and the boss +60% (`RunTuning.EnemyPctPerRoll`, `BossBuildPct`), so win rates per level band stay
  where they were with ~9 drafts.
- The HUD shows the build size ("Skills n" = every skill, skill level and stat perk gained this run); the result screen
  lists them with their source (draft, MYTHIC, shop, event).
- **Run time** (wall-clock model `RunClock` in the simulator: client animation times, battle playback 0.55 s per log
  entry ÷ speed, decisions, result screens): ~6–9 minutes in auto mode at ×2 battle speed (4–8 on average across all
  runs played), see the progression report.

**Every tile shows what it did:** each landing on a tile that resolves at once (empty, buff / curse, supply, hazard,
shard, pickup, boss tile, fights) and each pickup / skill effect while passing records a structured outcome in
`ActiveRun.TileOutcomes` (source "tile" / "battle" / "pass", the tile, a sequence number; lines incl. Empty, Avoided,
StepPenalty), cleared at every roll; the client turns them into landing popups. Decisions report as below.

**Outcome results:** every event / chest / campfire choice (shops: none) produces a structured
outcome (`ActiveRun.LastOutcome`: source, choice, whether chance was involved, the lucky outcome's chance and whether it
hit, and one line per gain or loss — coins, HP, stats, skills, XP / level-ups, shards, account loot, boss awakening and
its pace, boss strength, danger, fights). The client animates the chance roll (a marker sweeping over the % bar) and then
shows the result screen with icons and numbers; auto mode passes it after a short look.

### 3.4 Run rewards and failure
- Run loot is **pending** until the boss is beaten. Fail → all pending loot is lost (no banking).
- **Double rewards** (result-screen ad, once per won run): pays every *doublable* reward again — run loot (Burrow floor
  rewards included), the level's completion rewards and completion loot rolls, as granted (Biomass surge and run-reward
  modifiers included). Not doubled: first-clear milestone chests / portal bonuses, feat Diamonds, rare drops; rewards
  stashed for lack of a use are neither shown nor doubled. `RunSummary.Rewards` lists each reward with its source and
  `Doublable` / `Doubled`; after the ad `Granted` shows the doubled totals and `DoubledGranted` what the ad paid.
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

### 3.6 Mini-games (Alien Arcade)
- **GAME tiles** come from every tile pool (`MinigameTuning.PoolWeight`), at most `MaxPerRun` = 2 per run
  (`TileDef.MaxPerRun`, enforced where the segment generator picks pool tiles, `RunBoard.AppendSegment` → `PickPoolTile` →
  `RunTileLimits`, counted per run), so every segment can hold one until the run has had two; ~1.4 played per run (simulator). The first main level (the tutorial run) has none
  (`MinMainLevel` 2). A GAME tile **forces a stop** like a
  shop, so every one on the worm's path is played; afterwards it is **cleared** like every resolved tile.
- Landing → `RunPhase.Minigame` (decision `Minigame`). **Auto mode pauses** there (the player plays). **Farming**
  (auto-pick) resolves it as `MinigameTuning.FarmingTier` = **Miss** (consolation coins only, no skill roll): AFK play
  never earns the skill rewards.
- `RunMinigameStart`: the rules pick the game (never one already played this run; seeded by the run RNG), the seed for
  the client game and the reward type (`SkillRollChancePct` 50%: **run coins** or a **skill roll**; coins when the
  draft pool is empty). `RunMinigameFinish {Tier}`: **resolved on the device** — the client plays the game and decides
  the tier (thresholds in each game's client class); the rules only validate the tier value and accept exactly one
  finish per start. Skip = finish as Miss.
- Rewards per tier (`MinigameTuning.Rewards`, the one table to retune; coins scale with the level's shop prices,
  `shop_dark` ×1.49):

| Tier | Run coins | Skill roll (1 of 3, rarity odds C/R/E/L/M) |
|---|---|---|
| Miss | 10 | no draft, 10 coins |
| Bronze | 50 | 55 / 28 / 12 / 4 / 1 (the normal level-up odds) |
| Silver | 100 | 30 / 38 / 22 / 8 / 2 |
| Gold | 160 (≈ an Epic skill + a stat perk, or a Legendary) | 8 / 30 / 36 / 20 / 6 |

- Coins are sized for the run economy (~600–800 run coins per run, 10–20 shop buys): two Golds ≈ 40% of a run's income.
- The result screen (§3.3 outcome, source `minigame`) shows the medal and the reward; a skill roll then opens the draft,
  and its pick is a `RunGain` with source `minigame` (the build list).
- **The 14 games** (single screen, 15–30 s, one-line instruction + animated hint; details in docs/client.md):
  Portal Slip, Comet Catch, Saucer Landing, Clock Stopper, Wormhole Dash, Shield Parry, Slingshot Spore, Asteroid
  Slice, Jelly Stack, Signal Echo, Fossil Flip, Codebreaker Hatch, Odd Alien Out, Slot Pod.
- Simulator: the bot plays GAME tiles with a configurable skill (`SimPlayer.MinigameSkill`, median player = Silver: 60%
  on it, 20% one tier below / above); counters `run_minigames_started/_played/_gold`.

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
- Rarities: Common, Rare, Epic, Legendary, Mythic (parts, aliens, blueprints, hero / city skills). **Run skills use three
  tiers only: Common, Legendary, Mythic** (§3.3).
- Level-up in a run → draft "choose 1 of 3" (rerolls from meta). A hidden legendary pity guarantees a
  Legendary after N level-ups without one. Run skills have prerequisites and family gating (no upgrade chains).
- **Laboratory:** one branching tree of 36 nodes in four branches (offense, defense, elements, command). **Skill
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
