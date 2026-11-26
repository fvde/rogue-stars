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
- **Auto mode** (a level unlock, `auto_roll`, §16): holding Roll enables auto-roll; any tap interrupts. Auto only automates
  the **clear, choice-free** steps: rolling, moving, tiles without options (loot, stat buff/debuff, hazards, pickups, boss
  tiles) and **starting the fights it meets** (the boss arena: no Enter Arena tap while auto is
  on; manual play keeps the tap). The battle playback and every result sheet wait for the player's tap (playtest 0.1.200). At every **real decision** — shop (forced stop: the player can buy skills), skill draft, event,
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
  looted-out cache both show a result instead of being skipped silently. The boss arena opening is a clear step of its
  own, so the boss fight gets its own playback (minibosses stand on the path, see §3.3).

### 3.2 Tile types (prototype set)
| Tile | Effect |
|---|---|
| Enemy (lv 1–3) | Auto-battle; XP and run coins by level |
| Elite | Harder battle, more XP and coins |
| Stat buff / debuff | ± % of a stat for the rest of the run (can cost or grant stat %) |
| Supply cache | Run coins and run XP (in-run resources only) |
| Treasure (guarded) | Choose: fight its elite guardian for 2 account-loot items (more danger, stronger nearby enemies) or leave |
| Event | Narrative choice with 2–3 options, visible risk/reward text, weighted hidden outcomes allowed |
| Campfire | **Rest** (restore 35% HP) or **Train** (improve 1 owned skill **of the player's choice**: one level, or a chain's lower tier into its upgrade; signatures and maxed skills cannot train; nothing to train = coins; auto picks the lowest-level skill, earliest gained on ties); either strengthens nearby enemy tiles |
| Shop (force stop) | Two per segment. 3 offers at a time: 2 skills (owned ones offered as level-ups) and the heal (worm below 60% HP) or a stat perk, for run coins; reroll with rising price (again 3 offers) |
| Hazard | Costs HP or coins, or shortens the next roll (Gravity Vortex; the path never goes back) |
| Portal shard | Run collectible used by events |
| Boss tile | Dedicated per boss: interacts with the boss (buffs it, changes the board, feeds or drains the awakening meter) |
| Miniboss | **Two per run** (playtest 0.1.308), both on the path: a lair in each of the first two segments, a forced stop shown from afar by its crowned-skull marker (no clock ambushes). Each beaten miniboss grants a **MYTHIC choice** |
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
- **Minibosses:** **two per run** (playtest 0.1.308, `RunTuning.MinibossesPerRun`), both **on the path**: the level's
  miniboss lair is placed in each of the first two segments only (trunk tiles, never bypassed by a fork) and is a forced
  stop, so the worm always fights it and the player sees it coming (its crowned-skull marker, a "!" over it ahead). The
  awakening clock sends no minibosses any more (`RunTuning.MinibossAtMeter` empty; the old ambush step stays in the code
  unused). A lair pays its fight's XP and coins. Every beaten miniboss grants a **MYTHIC choice**: 1 of 3 Mythic skills (three per synergy plus
  generic ones, family gates waived for this choice; Mythics never level, so owned ones are not offered again). The pool
  is every Mythic of the run's unlocked synergies (starters Fire / Radiation / Bleed + researched ones), the generic
  ones (always open, no synergy: Overkill, Last Light, Catalyst, **Horrifying Presence**, **Beast**) and the Laboratory
  Mythics the player has researched (**Plague**, **Shatter**; toggleable like lab skills); owned synergies and signature synergies weigh more, but **every choice shows at least one Mythic outside the
  signature synergies** (playtest 0.1.308: a focused build saw all-signature choices ~37% of the time).
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
- **Shops:** two per segment (forced stops), **3 offers at a time** (playtest 0.1.308; every reroll again 3): 2 skills
  (owned ones as level-ups at lower weight) and one support offer (`ShopTableDef.SupportOffers`): the heal while the worm is
  below 60% HP (`HealOfferBelowPct`), else a stat perk (+8% ATK, +10% HP, +10% DEF, +5% crit, +8% Speed, +4% life steal,
  +4% damage reduction). Prices: skills
  **40 / 80 / 120** (Common / Legendary = 2× / Mythic = 3×; Dark Side 60 / 120 / 180), perks 40, heal 30, reroll 10 + 10 per
  reroll. A run **starts with the cheapest skill price + 30 coins** (`RunSystem.StartCoins`) and its first shop leads with a
  Common skill, so the first shop always allows a purchase. **SALE:** with 20% (`ShopTableDef.SaleChancePct`) one skill
  offer of a fill costs 50% less (`SalePct`; the list price is shown struck through). No result screen after a shop. Income ~3× (fight coins 10
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

**Longest survived:** every finished run (won or lost) records its rolls per level (`RunSystem.LongestRolls`, the 20 most
recently played levels); the home shows "Longest survived: N rolls" for the next level, the result "Longest Journey".

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
- **Dark Side of the Planet:** late mode (the feature unlocks at main level 53; its first level sits on the main level 40 curve), endless procedural levels along the campaign curve (dark
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
  `RunTileLimits`, counted per run), so every segment can hold one until the run has had two; ~1.4 played per run (simulator). Main levels below `MinMainLevel` 15 have none: the
  Alien Arcade is level-unlocked run content (`run_minigames`, §16). A GAME tile **forces a stop** like a
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

### 3.7 Run Quests (rare story characters)
A rare character waits on the board about **one run in five** (a plain, independent 20% chance per run decided at run
start; no pity, no streak rules; at most one per run). Part of the game from the start: no level gate and no unlock entry;
never on the tutorial run (main level 1) or in the Burrow. The meeting is a visitor tile on the main path of the second
segment (forced stop). Over ~30 meetings the character tells a story (the player only sees "Chapter N", never the total);
each meeting is a node of a branching, re-merging story graph: ~50% **story** choices (always move on), ~20% **hard battles**
(the level's miniboss +40% and more, with elites; declined = the story waits; **lost = the run ends** through the normal
defeat with its revive offer, and the story waits), ~20% **sacrifices** (100–300 run coins, a **random** Legendary or Mythic
skill of the run, 50–80% of current HP, all run skills; refused or not affordable = the story waits) and ~10% **boons**
(account-wide, forever: +1 run coin on every run-coin gain each). After the chain the character keeps visiting with friendly
epilogue scenes (small coins / heal / free skill pick / max HP). First quest: Mothwick, the Lost Courier (Starglint, +3).
Recipe and checklist: docs/design/run-quests/recipe.md; review doc: docs/design/run-quests/mothwick.md.

## 4. Combat (fully automatic, deterministic, seeded)
- Battles are turn-based in rounds with a round cap; reaching the cap with enemies alive is a loss.
- Combatants: the **Worm** (the only one that is attacked), squad **Aliens** and battle-scoped summons (drones) as
  **sidekicks** (never attacked; they charge by Speed — a Common acts about every 3 worm turns — and then use their one
  ability; they have no stats: hits are a % of the worm's ATK, heals / shields a % of its max HP, statuses are the worm's;
  Pebblehorn's Guard softens the worm's next hit), the **Mount** (a vehicle without stats that fires its active every few
  rounds; never attacks, never attacked), enemies. Details: docs/design/skill-conventions.md "Sidekicks", "Hero and mount kits".
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
- **Laboratory:** one branching tree of 38 nodes in four branches (offense, defense, elements, command). **Skill
  nodes** unlock skills into the run draft pool and upgrade them (toggle on/off for the draft pool); **stat nodes**
  give permanent loadout bonuses of +1% to +10% per node (HP, ATK, DEF, crit chance/damage, combo, counter, dodge,
  damage reduction, shield power, rage gain, element damage, status chance, boss damage, alien power, mount power, loot
  luck). Both kinds are mixed in every branch, so the player chooses between raw stats and new skills; a node of row
  n+1 needs any node of row n at level 2, capstones need two nodes (all). Five levels per node, except the two **Mythic nodes** (owner, 2026-11-26), one level
  each, capstones of row 5 (two nodes at level 3): **Plague** (elements; Catalyst Glands + Fallout Cloud) and **Shatter**
  (offense; Hyper Servos + Frost Nova). Researching one puts its Mythic into the miniboss Mythic choice (see the Mythic
  skill list at the end of this section).
  - **Cost:** research costs **Data** (no Biomass/Ore). Every research makes the next one longer: the k-th research
    level takes h = 1.5 h × 1.0355^k nominal and costs 14·h^0.75 Data (`LabTuningDef`). The whole tree (189 levels
    beyond the three default ones) is ≈ 30.8k nominal hours and ≈ 100k Data.
  - **Size:** with one slot, all three ad speed-ups per timer (×0.85³) and the daily free speed-up (30 min), the tree
    takes ≈ 2.1 years; even with the strongest Laboratory building boon (+27%) and a Research Annex from day 1 a
    year of perfect timing clears at most ≈ 27.5k of the 30.8k hours (checked by `Tree_CannotBeFinishedInYearOne_…`).
  - **Research Annex** (Mythic blueprint): a second, concurrent research slot while two alien workers of 3★ or more
    staff it; research in the Annex takes twice as long.
  - **Data:** Dark Side clears (main source, grows with the Dark Side level), the Laboratory unlock grant and the arena shop
    (the point chests paid a trickle until 2026-11-26).
- **Mythic skill list:** every run skill with its card text is generated into docs/design/run-skill-levels.md. The owner's
  four Mythics of 2026-11-26:
  - **Horrifying Presence** (generic, always open): every enemy is stunned in round 1.
  - **Beast** (generic, always open): +30% counter, combo and crit chance.
  - **Shatter** (Laboratory, Ice; offered once the run can freeze): each hit by the worm or an alien on a frozen enemy gives
    +10% ATK for the rest of that battle, up to 15 stacks (+150%); no decay, reset at the next battle. (The older Ice Mythic
    with the thaw-and-blast effect, `mythic_shatter`, is now called **Thaw Blast**.)
  - **Plague** (Laboratory): for every 20 stacks of a status the player side inflicts while the run owns Plague, enemies of
    every later battle of the run start with one more stack of that status (run-scoped count).

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
- The worm equips one **Hero persona** (9: Star Knight, Chef, Void Pirate, Shadow Noodle, Prism Alchemist, Sparkplug, Beast Tamer,
  Nebula Monk, Hourglass Hermit). Heroes count as Mythic. Unlock with 50 shards; **stars 1–10** on the shared shard table
  (§12.2). Each hero has a hand-written **kit** (hero and mount
  kits, owner-approved page rev 2): 1★ Trait + Ability (a timer or a counter that says what fills it, e.g. "Starfall 2/4"), 3★ one
  number, 5★ / 7★ clauses, 10★ capstone. Heroes are not tied to a synergy. Stars and hero level raise worm stats from one shared
  budget. Kits per star: docs/design/heroes-mounts.md.
- Hero level via Hero XP. Heroes come from **summon events** (§12.1): a won hero is a batch of 50 shards (the first unlocks the
  hero; at 10★ a batch becomes exchange tokens); stars are bought with shards. There is **no starter hero**: the Heroes unlock
  gives only the summon events and the first event's 10 tickets.

### 7.2 Mounts (spaceships)
- One active mount: a **vehicle on a reload** with **no stats** (no ATK / HP / DR, no shield from mount HP): one passive + one
  active (a shot every N rounds from round 1), both changing at 1★ / 3★ / 5★ / 8★ / 10★; damage is % of the worm's ATK × (1 + mount
  damage %: +5% at 2★ / 4★ / 6★ / 7★ / 9★, +1% per Fuel level, collection, lab). Its statuses and crits are the worm's (rider
  link). 9 mounts (Scout Saucer, Magma Barge, Siphon Hauler, Star Lancer, Frost Frigate, Rift Racer, Aegis Walker, Hive
  Carrier, Chrono Skiff): docs/design/heroes-mounts.md. Mounts count as Mythic: **stars 1–10** on the shared shard table
  (§12.2); the former awakening 1–5 is stars 6★–10★, shards only like every other star. Mounts come
  from **summon events** (§12.1): a won mount is a batch of 50 shards (the first unlocks it). **No starter mount**: the Mounts
  unlock gives the mount events and the first event's 10 tickets. Mount level via Fuel.

### 7.3 Aliens
- One alien **per alien type** (species); 20 species (7 Common · 5 Rare · 4 Epic · 2 Legendary · 2 Mythic). Each has one
  ability (aliens pass, owner review rev 4.3), a Speed (34 Common … 50 Mythic), up to 3 traits, rarity and **stars 1–10 via
  shards** (the shared table of §12.2 at every rarity: pulled batches of 50, breeding, the Shard Reactor) — **the only progression**; alien levels were removed. **No combat stats:** the ability
  works in % of the worm's ATK (hits, statuses) or max HP (heals, shields), × alien power. Every star is its own ability
  step: the main % grows (wide ranges every star, small ranges only at tier stars), tier stars 1/3/5/7/10 also change the
  effect (stacks, targets, chances). Whole numbers only. Per-alien table: docs/design/skill-conventions.md "Sidekicks".
- **Traits:** pool per species = Swift *or* Eager (tempo) + effect traits that fit its ability + one work trait. Swift +15%
  Speed, Eager first action at battle start, Twin +1 stack every 2nd action, Wide also strikes 1 other enemy at half,
  Lingering buff / Chill / Shock +1 round (a guard +1 hit; never Freeze / Delay), Bulwark guards and shields +20%, Vampiric
  heals the worm 10% of its damage. Old traits are remapped (Tough / Armored → Bulwark or Lingering, Fierce → Twin or Wide,
  Lucky → Swift, Pyromaniac / Stormborn → Twin); traits outside a species' pool convert on breeding.
- **Genes:** stored and still bred, but inactive — shown as "Coming soon".
- **Work:** assign to city buildings; work power grows +40% per star (plus work traits).
- **Fight:** squad slots unlocked by progression: 0 at alien unlock, 1 after first housing, 2 and 3 via
  city upgrades / rare blueprints. Aliens are sidekicks: never targetable, they act by Speed with their one ability.
- **Breeding Pen** (Grain's only sink, the targeted shard source): pick two aliens; costs Grain and time (by the higher
  rarity; buildings speed it and improve quality). The child is randomly one of the two parents' species (genes mixed, traits
  inherited with mutation chance). When born the player chooses: replace the current alien of that type (new traits; stars
  kept), or discard it for shards of its species (1/1/2/2/3 by rarity) and 30% of the Grain back.
- **Save migration:** an old save's alien levels reset to 1 and 100% of the Grain spent on them is refunded (once, by the
  rarity paid at: Emberkit as a Rare); stars, shards and genes are kept.

## 8. City and Blueprints (replace the artifact system)
- The city is a grid on the planet unlocked area by area (early game = unlocking city elements one by one:
  Biomass farm, Grain farm, Ore mine, Housing, Laboratory, Breeding Pen, Diamond mine, Observatory, …).
- **Blueprints** are pulled (gacha) or earned. Place a building, level it (Ore + Biomass + time), raise its
  stars with shards (1–10 stars on the shared table of §12.2: a won blueprint is a batch of 50 shards; star milestones add
  effects). Rarity: Common → Mythic.
- **Production buildings** generate resources over time (settled from timestamps); alien workers boost
  output by their work stats/traits. Player sets a **production priority** (which resource gets the boosted
  share).
- **Boon buildings** give run effects: stamina max/regen, start run with X, skip X negative events per run,
  increased loot/XP in run, extra dice, 1–3 skills at run start (extremely rare Epic+ buildings), awakening
  meter slowdown, squad slot unlocks.
- **Staffed buildings:** some utility boons need workers (e.g. the Mythic **Research Annex**: a second laboratory slot
  while two aliens of 3★+ work there).
- **Shard Reactor** (Mythic): turns spare shards into shards of a rarer item of the same type that the player picks (alien
  shards → a higher-rarity alien; blueprint shards → a higher-rarity blueprint): ×4 source shards per rarity step, Grain (aliens)
  or Ore (blueprints) and time; one job per working alien (1 worker slot, +1 at 3★ and 6★). docs/design/pull-system.md.
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
| Alien Signal | Alien Tokens | Aliens; Grain and hero summon tickets (the old hero-shard share) |
- Each pull type can always be paid with Diamonds instead.
- Flat published rates: Common 58.3%, Rare 28%, Epic 9.5%, **Legendary 3%**, **Mythic 1.2%** on every pull. **Fixed, visible
  pity** per pull type (shared across banners of that type, carries over): "Mythic x/100" (the 100th pull without a Mythic is
  one) and "Legendary x/40" (the 40th without a Legendary or better is at least Legendary; its Mythic chance stays 1.2%). No
  soft or hidden pity anywhere. The visible 10-pull rule: every 10× pull has at least one Epic. A blueprint / alien result is a
  batch of 50 shards (§12.2). docs/design/pull-system.md.
- **Themed rotating mythic banners** for all three pull types (one featured Mythic item per banner). The Blueprint Signal's
  old mount share is Ore and mount summon tickets; heroes and mounts are not in the Signals any more.

### 12.1 Summon events (heroes and mounts)
Heroes and mounts are obtained in **temporary rotating summon events** (docs/design/summon-events.md, all numbers in
`ContentDb.SummonEvents`): one **hero event (6 days)** and one **mount event (3 days)** at a time, each featuring one hero /
mount from a global rotation; the player's first event of a type starts at its feature unlock. Pulls cost **persistent
tickets** (or Diamonds) and pay exchange tokens. The featured hero / mount (a batch of 50 shards) drops at a flat 1.2%; the
other 98.8% are filler (Hero XP / Fuel, Signal tokens, a 0.05% Awaken Core). The visible **Luck** meter (100)
guarantees the featured batch when full and is **kept until the next event of that type**. No Legendary / Rare hero or mount drops.
Per event: six task lines counted from the event start (enemies, won levels, won mini-games, Alien / Part / Blueprint pulls;
one ticket per target), pull-count milestones, an exchange shop with event limits, a free + premium day pass, packs with
daily limits and a daily ad ticket.

### 12.2 One star system for pulled items
Heroes and mounts (Mythic), and aliens and blueprints of every rarity, are shard based on one table: **50 shards unlock**
(1★), 1★→2★ and 2★→3★ 50 each, 3★→4★ … 6★→7★ 100 each, 7★→8★ … 9★→10★ 150 each: **1 000 shards from nothing to 10★**.
Pulls never pay small shard amounts: winning an item is a batch of 50 shards, and the first batch of an item the player does
not own unlocks it. Parts keep their own merge ladder (§6). Old saves: stars kept (mounts: stars + awakening), loose shards
converted step by step (docs/design/pull-system.md).

## 13. Currencies
| Currency | Use |
|---|---|
| Biomass | Worm level, talents, building upgrades. Mainly from portal runs (grows with the level; the first 3 won portal runs a day pay ×4), city farms second |
| Grain | Breeding, Shard Reactor alien jobs |
| Ore | Parts level, Mythic+ merges, buildings |
| Data | Laboratory research (Dark Side clears, arena shop) |
| Awaken Cores | Part awakening (arena shop, high-tier invasions, deep Burrow floors; extremely rare) |
| Arena Medals | Arena shop (arena fights and weekly rank) |
| Gear Cores, Advancement Plates | Part level / cap |
| Fuel | Mount levels |
| Hero XP | Hero levels |
| Diamonds | Premium; also earnable (city Diamond mine, quests, events) |
| Blueprint/Part/Alien Tokens | Free pulls |
| Hero / Mount Summon Tickets | Summon event pulls (kept between events) |
| Star Sigils / Hangar Bolts | Summon event exchange shops (per pull) |
| Shards (per hero/alien/mount/blueprint) | Unlock (50) and stars (the shared table, §12.2); spare alien / blueprint shards feed the Shard Reactor |
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
| Grain | breeding | Aliens (D7) or Breeding | 2 000 |
| Fuel | mount levels | Mounts (D12) | 4 000 |
| Hero XP | hero levels | Heroes (D10) | 3 000 |
| Data | laboratory research | Laboratory (D5) | 100 |
| Part / Alien / Blueprint Tokens | free pulls | Part Signal (D4) / Alien Signal (D7) / Blueprint Signal (D9) | 30 each |
| Hero / Mount Summon Tickets | summon event pulls | Heroes / Mounts | 30 each |
| Star Sigils / Hangar Bolts | summon exchange shops | Heroes / Mounts | none |
| Arena Tickets / Arena Medals | arena fights / arena shop | Arena (D15) | 5 / none |
| Burrow Keys | Burrow runs | Burrow (D19) | 2 |
| Invasion Attempts | invasion battles | Invasions (D18) | 3 |
| Guild Marks | guild shop | Guilds (D17) | none |
| Hero / Alien / Blueprint / Mount shards | unlock and stars | Heroes / Aliens / City / Mounts | none |
| Parts, Runes, Blueprints, Aliens, Mounts, hero copies, hero shard rewards | – | Parts / Runes (D11) / City / Aliens / Mounts / Heroes | Runes 5 (best kept), others none |

## 14. Monetization (mainstream RPG model, no A/B, no spend-based pricing)
- Diamond packs (fixed price ladder), starter packs, rolling and flash offers (same for everyone),
  periodic gift packs (daily rewards over N days).
- **Permanent one-time purchases** that everyone should want: *No Ads* (all ad placements become instant),
  *Daily Diamonds* (X Diamonds per day forever).
- **Monthly pass**: daily Diamonds, battle speed ×3, extra patrol/production, stamina cap +.
- **Season pass** (4 weeks): free + premium track, premium is retroactive.
- **Summon events:** a premium day pass per event ($9.99 heroes / $4.99 mounts) and ticket packs with daily limits (§12.1).
- **Funds** (one-time paid reward tracks per system: worm level, hero, mount, city).
- Rewarded ads in many placements (stamina refill, revive, timer −15%, extra free pull per day, double run
  rewards), each with daily caps; ad-free purchase makes them instant.
- Whales progress faster; F2P reaches the same content slower. Drop rates published.

## 15. Retention
Daily login (7-day cycles, no streak loss), daily/weekly quests and achievements with point tracks,
new-player 7-day event, mailbox, idle production (city), push notification hooks (local), returning-player
gifts. Lobby popups capped.

**Daily Quests** are there from the first cleared level (no feature, unlock moment, intro or gift; the hub button shows with Parts), so
every quest makes sense the moment it is offered (`docs/balance/daily-quests.md`): a quest is hidden until the feature that makes it
possible (its `Feature` field: the Shop for "Buy 1 shop item" and the ad quest, the City for building quests, the Arena, Breeding,
Invasions), the **early quests alone fill both point tracks** (daily 100, weekly 500: no ad, shop, arena or City needed; later quests are
extra points). Daily and weekly quests pay **points only**: the bar's five chests are the reward (Stamina, Diamonds 100 a day / 500 a
week, Biomass, Ore, Gear Cores; the daily top chest 3 pull tickets of random unlocked Signals; the weekly 400 chest 5 tickets of each
unlocked Signal; one Part awakening core in the weekly top chest). Achievements are linear repeating milestones (Diamonds per 100 enemies,
10 bosses, 10 mini-games; 10 pull tickets per 100 pulls of a type). The ad quest hides while ads are disabled (`ShopConfig.AdsDisabled`).

## 16. Onboarding / unlock order
Level unlocks (owner decision 2026-11-24): **nothing limits progression by calendar day.** Every feature unlocks at a
**player level = the main campaign level cleared** (`main_level_max`, `RequirementKind.MainLevel`; the worm level is itself
a feature, so the campaign level is the account's level). How fast the player gets there is up to them: there is no
account-day gate and no "one unlock per day" limit (daily resets, daily quests, timers and event schedules stay). The
features are spaced out for a steady stream of new things: **one per level up to level 20, then every 2 levels, then
every 3–4**; systems come before what depends on them and every reward waits for its use (§13.1). Run content is
introduced the same way, after the level before the first board that has it (the level's own fields, `ContentDb.Campaign`).
The simulator's `--unlocks` table (`docs/balance/onboarding-unlocks.md`) shows the minutes, runs and days per profile.

| Level | Unlock (feature id) | Guided action |
|---|---|---|
| – | **Level 1 = the tutorial run**: roll the dice, pick a skill on level-up, beat the boss (coach marks with a hand pointer; its own small tile pool: enemies, buffs, pickups; no shops, events, elites, hazards, campfires or minibosses; the boss after ~20 rolls; eased so a worm without parts wins it) | – |
| 1 | Parts / Worm tab (`parts`): equip the three starter parts. Daily Quests and the 7-day calendar are there from here on, with no unlock moment of their own (owner, 2026-11-26) | Equip your parts |
| 2 | Shops on the board from level 3 (`run_shop`) | Play |
| 3 | Worm level & talents (`worm_level`) | Level up the worm |
| 4 | Minibosses & Mythics from level 5: two miniboss lairs on the path (`run_miniboss`) | Play |
| 5 | Two dice & forks from level 6 (`run_two_dice`) | Play |
| 6 | City with one plot: Biomass Farm (`biomass_farm`) | Build the farm |
| 7 | Auto roll: hold the dice (`auto_roll`; auto stops for every choice, starts fights on its own) | Play |
| 9 | Campfires from level 10 (`run_campfire`) | Play |
| 10 | Part Signal (`part_pulls`); the first pulls are on the house | Tune in |
| 11 | Signature awakening explained (`run_signatures`, intro only: every run, the first one included, starts by picking 1 of 3 signatures; playtest 0.1.325) | Play |
| 12 | Shop (`shop`) | Open the shop |
| 13 | Laboratory (`laboratory`; its first package, Combo, comes with it) | Start first research |
| 14 | Alien Arcade mini-games from level 15 (`run_minigames`) | Play |
| 15 | Aliens: the first alien (`aliens`) | Meet your alien |
| 16 | Housing → first squad slot (`alien_squad`) | Build housing |
| 17 | Grain Farm (`grain_farm`) | Build the grain farm |
| 18 | Heroes (`heroes`; the hero summon events, first event + 10 tickets; no starter hero) | Choose a hero |
| 19 | Alien Signal (`alien_pulls`) | Call an alien |
| 20 | Blueprint Signal (`blueprints`) | Pull a blueprint |
| 22 | Ore Mine (`ore_mine`) | Build the ore mine |
| 24 | Laboratory: Ice Package (`package_ice`) | Open the lab |
| 26 | Mounts (`mounts`; also the mount summon events, first event + 10 tickets) | Saddle up |
| 28 | Runes (`runes`) | See your parts |
| 30 | Funds (`funds`) | See the funds |
| 32 | Arena (`arena`) | Enter the arena |
| 34 | Breeding Pen (`breeding`) | Build the pen |
| 36 | Season pass (`season_pass`) | See the pass |
| 38 | Laboratory: Lightning Package (`package_lightning`) | Open the lab |
| 40 | Overcharged levels (`overcharged`) | Play overcharged |
| 42 | Guilds (`guilds`) | Find a guild |
| 44 | Invasions (`invasions`; also opens the hub's live-event column and the Observatory) | Defend the planet |
| 46 | Burrow (`burrow`) | Dig in |
| 48 | Laboratory: Counter Package (`package_counter`) | Open the lab |
| 50 | Forge (`forge`) | Open the forge |
| 53 | Dark Side of the Planet (`dark_side`) | Cross over |
| 56 | Laboratory: Alien Package (`package_alien`) | Open the lab |
| 60 | Laboratory: Time Package (`package_time`) | Open the lab |

Each package node needs its feature (`LabResearchDef.RequiresFeature`) besides the chain of prerequisites. The first boards
(main levels 1–14, numbers in `CampaignDef`: `ShopsFromLevel`, `MinibossesFromLevel`, `ForksFromLevel`, `CampfiresFromLevel`,
`SignatureChoiceFromLevel`, `MinigameTuning.MinMainLevel`) leave out what is not introduced yet; later portals, the Burrow
and the Dark Side have everything. The run-content features read their levels from those numbers, so they never drift.

**Old saves** keep every unlock they have (unlocks never re-lock). A save from before the level unlocks
(`OnboardingState.Version` 0) gets, once, the new features its level already meets (`FeatureDef.Since` 2: the run content,
auto roll, packages) recorded and celebrated silently — it already had that content; their "?" replays the intro. The
original features it now meets earlier get their normal unlock moments.

**Visibility:** locked features are invisible — no greyed-out entries. Only the single next unlock is teased
(`Features.Next`: "Laboratory — clear level 13" with the level progress "11/13"). Day-1 hub of a brand-new account: PLAY, the
Planet tab, the teaser, the stamina and Diamonds chips and the profile chip; the Worm tab (and Mail) join after level 1. Mail and the
currency chips follow use (§13.1): a currency shows once the player can use it, so a brand-new account shows the stamina and
Diamonds chips only (Diamonds buy stamina refills).

**Hub:** bottom tabs for daily features appear one by one (Worm, City, Planet in the centre, Signals, Arena); left
column = daily goals, summon events (from Heroes) and live events (invasions, planetary/limited events, from the Invasions unlock); right column =
shop, offer, pass, mail, "More" (laboratory, aliens, heroes, mounts, breeding, guild); the PLAY card (Continue during a
run) sits above the tabs.

**Unlock moment and feature intro:** each unlock opens a full-screen screen on the hub (never mid-run, one at a time):
the feature's name on a ribbon, then its intro: 2–4 short panels (`FeatureDef.Intro`: one line, an art key from ArtCatalog
`Art/Intro/` with a drawn fallback, a simple looping animation: a tapping hand, pulse, slide-in, count-up, spin, hop, a
filling bar) with NEXT and SKIP; the last panel adds one line of what it does, the reward (unlock gift and items), ONE
guided action that claims the gift and opens the feature, and the next teaser. Every feature screen has a **"?"** that
replays the intro (a topic list when the screen has several features), without claiming anything. Starter items arrive with the unlock (`UnlockGrants`: first alien, blueprints, Data, hero shards...) together
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
