# Parts: rarity ladder, merge recipes, skills and awakening

Binding design for worm parts (GDD §6.2). Numbers live in `ContentDb.PartsConfig` (`Content/ContentDb.Parts.cs`,
skills in `Content/ContentDb.PartSkills.cs`); rules in `Systems/Progression/PartsSystem.cs`. Pacing results:
`docs/balance/progression-report.md`, section "Parts/lab/cores redesign".

## Ladder

Common → Uncommon → Rare → Epic → Legendary → **Mythic** → Mythic+1 → Mythic+2 → **Mythic S** → S+1 → S+2 →
**Mythic SS** → SS+1 → SS+2 (14 steps, index 0…13). Gacha and drops hand out Common, Rare, Epic, Legendary and Mythic
parts; everything else is reached by merging. Level, cap raises, runes and Forge slots of the target carry over;
materials are refunded 100% of their level materials and give back their runes.

| Rarity | Stat ×| Base level cap | Plate raises (+5 each) | Rune slots | Part skills active |
|---|---|---|---|---|---|
| Common | 1.0 | 10 | 0 | 0 | 0 |
| Uncommon | 1.3 | 15 | 0 | 0 | 0 |
| Rare | 1.7 | 20 | 1 | 1 | 0 |
| Epic | 2.4 | 30 | 1 | 1 | 1 |
| Legendary | 3.6 | 50 | 2 | 2 | 2 |
| Mythic | 5.6 | 70 | 3 | 3 | 3 |
| Mythic+1 | 6.2 | 75 | 3 | 3 | 3 |
| Mythic+2 | 6.8 | 80 | 3 | 3 | 3 |
| Mythic S | 7.8 | 90 | 4 | 4 | 4 |
| Mythic S+1 | 8.4 | 95 | 4 | 4 | 4 |
| Mythic S+2 | 9.0 | 100 | 5 | 4 | 4 |
| Mythic SS | 9.8 | 105 | 5 | 5 | 5 |
| Mythic SS+1 | 10.4 | 110 | 6 | 5 | 5 |
| Mythic SS+2 | 11.0 | 115 | 6 | 5 | 5 |

Max level 145 (SS+2 with all six plate raises). The top multipliers stay close to the old Mythic+3 (9.8 at Mythic SS) so the
campaign curve still fits (levels 1–500; the endless levels beyond are power-gated); the long road is in the merge cost, the skills and awakening, not in raw stats. Forge: Epic+, two unlocks (+1 rune slot and +10% / +15% base stats each),
rune slots capped at 5. Ore per level `30·L^1.4`, Gear Cores `2·(L−9)^1.25` from level 10.

## Merge recipes and fodder points

A merge takes the target (one step below) plus materials:

- **Copies**: N parts of the target's own def at the given rarity *or higher*.
- **Fodder**: any other parts — any def, any kind (a surplus head feeds an arm) — each at the minimum rarity or higher,
  worth at least the recipe's fodder points (no superfluous part: dropping the cheapest one must fall short).
- **Ore** from Mythic on.

**Fodder value** is everything that went into a part: Common 1, Uncommon 3, Rare 9, Epic 27, **Legendary 81 (= 1 LE,
"Legendary-equivalent")**, Mythic 324 (4 LE), Mythic+1 972, Mythic+2 2 106, Mythic S 4 212, … So three parts of a rarity
always merge into one part of the next (below Mythic), several lower parts substitute for one higher one, and a spare
Mythic of a def you do not use is worth four Legendaries.

| To reach | Copies (own def) | Fodder (any part, min rarity) | Fodder in LE | Ore | Cumulative LE from Legendary | Cumulative copies |
|---|---|---|---|---|---|---|
| Uncommon | – | 2 pts (Common+) | – | – | – | – |
| Rare | – | 6 pts (Uncommon+) | – | – | – | – |
| Epic | – | 18 pts (Rare+) | – | – | – | – |
| Legendary | – | 54 pts (Epic+) | – | – | – | – |
| **Mythic** | 1 Legendary+ | 162 pts (Legendary+) | 2 | 1 500 | 3 | 1 |
| Mythic+1 | – | 648 | 8 | 2 200 | 11 | 1 |
| Mythic+2 | – | 1 134 | 14 | 3 400 | 25 | 1 |
| **Mythic S** | 2 Legendary+ | 1 944 | 24 | 5 100 | 51 | 3 |
| Mythic S+1 | – | 3 240 | 40 | 7 600 | 91 | 3 |
| Mythic S+2 | – | 5 670 | 70 | 11 400 | 161 | 3 |
| **Mythic SS** | 3 Legendary+ | 9 720 | 120 | 17 100 | 284 | 6 |
| Mythic SS+1 | – | 16 200 | 200 | 25 600 | 484 | 6 |
| **Mythic SS+2** | 1 Legendary+ | 27 540 | 340 | 38 400 | 825 | 7 |

(Cumulative LE counts copies as 1 LE each, starting from a Legendary target.)

**Growth:** fodder ×1.6–1.75 and Ore ×1.5 per step above Mythic; the rungs that unlock a skill (S, SS) also need own-def
copies. One item from a fresh Legendary to Mythic S costs ~51 LE, to Mythic SS ~284 LE, to Mythic SS+2 ~825 LE —
each skill rung is roughly 5× the previous one.

**Why surplus always matters:** below Mythic every step is "3 of a rarity → 1", any def and kind, so every Common,
Rare and Epic drop ends up as Legendary-grade fodder (quick merge does this automatically, up to Legendary, using
exactly-matching rarities only). Above Mythic the only fuel is Legendary+ parts — Legendary drops, merged-up surplus,
spare Mythics of other defs (4 LE) — so Legendaries are the main currency of the Mythic+ road and never go stale. Copies
for Mythic/S/SS/SS+2 may be Legendary or higher, so non-featured defs are reachable from normal Legendary drops; banner
copies (Mythic) speed up the featured defs. Awakened and Forged parts are never materials; dismantling is only for
inventory hygiene.

**Replicator** (`PartReplicate`): Legendary+ surplus worth **10 LE** (810 points, any def/kind) + **5 000 Ore** → a
Legendary copy of the def of a Mythic+ part you own. Copies for Mythic S / SS / SS+2 therefore never strand the
surplus: when banners and drops do not bring the right def, Legendaries buy the copy (an S then costs ~24 LE of fodder
+ 2 copies ≈ 44 LE at most, an SS ~120 + 30 LE). Own-def drops remain the cheaper route.

## Part skills (five per part)

Every part def has five skills (`SkillDef`, `Source = Part`, not draftable) that unlock at **Epic (1), Legendary (2),
Mythic (3), Mythic S (4) and Mythic SS (5)**. They are built from the part's **family** (Fire, Ice, Lightning, Poison,
Bleed, Crit, Combo, Counter, Alien Synergy) and its **kind** (head = precision, body = defence, tail = momentum,
legs = evasion, arm = attack style):

| # | Rarity | Role | Shape |
|---|---|---|---|
| 1 | Epic | Core | family stat package + a light family proc on hits |
| 2 | Legendary | Technique | the kind's trigger fires the family payload (head: crits, body: taking damage, tail: combos, legs: dodges, arms: counters/crits/hits/combos/rage attacks by attack style) + a kind stat |
| 3 | Mythic | Hunter (build hook) | bonus damage against the family condition (vs Heat/Chill/…, vs low HP, combo/counter damage) or squad rage/power |
| 4 | Mythic S | Signature | kind-shaped battle opener: head = family effect on all enemies at battle start, body = last-stand shield + nova, tail = round sweeps, legs = true-damage dodges, arms = rage attacks that hit everyone |
| 5 | Mythic SS | Apex | the family's capstone: burn chains on kills, freeze all + vs-frozen damage, round lightning + stuns, poison every round + regen, crits bleed everyone, true-damage crits, combo rage/ATK stacking, parry reflection, a squad rage opener (Hive) |

**Ice parts chill, they never freeze** (crowd-control pass, owner): the Ice payload (core proc, technique, openers, awakened
skills) deals its frost damage and applies Chill (1 + power/1.2 stacks, the payload's chance), the Ice Hunter / Resonance build on
Chill. Only the Ice **Apex** (Mythic SS) still freezes every enemy at battle start. The shared family skill of Ice hero traits and
mount weapons (`ContentDb.ProgFamilySkill`) chills too (25% × power, 1 + power/1.5 stacks). Any Freeze resets the target's Chill.

Amounts are scaled by `PartsConfig.SkillPowerScale` (0.25; awakened 0.3) — the pacing knob; descriptions show the
scaled numbers.

## Awakening (once, Mythic or higher)

- Any part of **Mythic or higher** can be awakened **once** for **50 Awaken Cores** (`PartsConfig.AwakenCost`).
- It becomes a redesigned version of itself: **new name** (`PartDef.AwakenedName`, e.g. Plasma Blaster → Nova Lance),
  **higher base stats** (×1.4 base and per-level stats plus the family's signature stat) and **all five skills
  replaced** by awakened redesigns with the same unlock pattern (an awakened Mythic has three, Mythic S adds the 4th,
  Mythic SS the 5th).
- Level, cap raises, runes, Forge slots and rarity are kept; there are no further awakening levels; awakened parts
  can still be merged up (as targets) but never be materials or dismantled.

Awakened skills pair the family with a **twin family** (Fire+Crit, Ice+Counter, Lightning+Combo, Poison+Alien,
Bleed+Counter, Crit+Lightning, Combo+Fire, Counter+Poison, Alien+Bleed) and use other triggers, so an awakened part
can open a new build:

| # | Awakened role | Shape |
|---|---|---|
| 1 | Twin core | stats of both families; the kind trigger now also delivers the twin's payload |
| 2 | Pulse | every second turn the family and twin payloads hit a random enemy (no hit needed) |
| 3 | Resonance | status chain: every application of the build status (Heat/Chill/Shock/Radiation/Bleed) strikes again and builds rage (+ damage vs that status); status-less families get skill damage + rage gain |
| 4 | Awakened opener | body: heal every turn with overheal → shield; legs: dodges shield and hit all enemies; tail: speed every round + twin sweeps; head/arms: a 45% ATK opening barrage + opening nova |
| 5 | Ascension | once below 30% HP: heal 30%, true-damage nova, family effect on all; permanent skill-damage bonus |

## Awaken Cores (extremely rare)

No routine source (Burrow floors, Dark Side clears and loot no longer pay cores). Sources:

1. **Arena shop** (PvP currency **Arena Medals**: 4 per arena win, 2 per loss, 20–120 per weekly rank × 1–2.5 by
   league): 1 Awaken Core for 200 medals, at most 2 per week. Spare medals buy part tokens, Data and Gear Cores.
2. **High-tier invasions** (Void Choir, Prism Horde): top contributors of a *repelled* occurrence win a core with 25%
   (rank 1), 12% (rank 2–3) or 4% (rank 4–10).
3. **Deep Burrow floors:** rare drop on floors 9 (1%) and 10 (1.5%), paid on cash-out, never multiplied or doubled.

Target: an active F2P player who has the Arena gets ~1–2 cores per week (mostly the arena shop), so the first
awakening takes many months; measured rates per profile are in the progression report.
