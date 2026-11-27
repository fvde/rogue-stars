# Parts v2: item sets, the Mythic ladder, Metal, skills and awakening

Binding design for worm parts (GDD §6.2), approved by the owner as the parts redesign rev 3
([parts-v2/parts-redesign.html](parts-v2/parts-redesign.html); source data [parts-v2/data.py](parts-v2/data.py), checks
[parts-v2/check.py](parts-v2/check.py), pacing [parts-v2/pacing.py](parts-v2/pacing.py) / [pacing.json](parts-v2/pacing.json)).
Content: `Content/ContentDb.Parts.cs` (items, rarities, ladder, runes, Forge), `Content/ContentDb.PartSkills.cs` (the 390 skills),
`Content/ContentDb.PartData.g.cs` (the approved texts, generated from `data.py` by `parts-v2/gen_cs.py`); rules
`Systems/Progression/PartsSystem.cs`; battle primitives `Systems/Run/BattleSimulator.Parts.cs` (see
[skill-conventions.md](skill-conventions.md), "Parts v2 primitives"). Pacing results: `docs/balance/progression-report.md`, "Parts v2".

## The model

- **Rarities.** Common, Rare, Epic and Legendary are four separate item sets: per slot (head, body, tail, legs, arm; an arm part
  fits either arm) six items each, one per standard role: **Guard** (tank), **Strike** (offense), **Swift** (speed), **Keen**
  (crit), **Mend** (sustain), **Fury** (rage). Names are *tier role slot*: Scrap / Alloy / Plasma / Quantum + role + Helm / Shell /
  Tail / Legs / weapon (Hammer, Cannon, Whip, Claw, Drill, Blaster by role; cosmetic). Uncommon is retired. **Mythic** is a fifth
  set of 30 build enablers (three per synergy in three different slots, plus three General ones; six per slot). Ids:
  `c_ / r_ / e_ / l_<slot>_<role>` and `myth_<name>`.
- **Obtain.** Mainly **Parts pulls** (the Shop's Parts section, paid with Crates) at the published rates: Common 58.3%, Rare 28%,
  Epic 9.5%, Legendary 3%, Mythic 1.2% (pity Mythic 100 / Legendary 40, every 10× has an Epic or better); each rarity draws
  uniformly from its 30 items. **10 Part banners of 3 Mythics** (one per synergy, its 3 Mythics featured: 80% of Mythic results
  split between them, 20% over the other 27), one a week (10-week cycle, all 30 Mythics at launch): Inferno Arsenal (Fire), Glacier
  Vault (Ice), Storm Works (Lightning), Fallout Foundry (Radiation), Shadow Forge (Bleed), Whirlwind Workshop (Combo), Bulwark
  Armory (Counter), Hive Hangar (Alien), Hourglass Depot (Time), Proving Grounds (General). Direct drops: every **campaign boss
  first clear** (the first Normal clear of a main level) gives one random **Rare (75%) or Epic (25%)** part; every **Dark Side
  clear** has a **5%** chance of a random Legendary part. Runs, run chests, events and loot tables give **no parts** (the Rift Echo
  event leaves a Parts Crate). Runes are no longer in the Parts pull (run loot keeps them).
- **Skills.** Common and Rare: stats only. Epic: exactly 1 skill. Legendary: exactly 2. Mythic: 5 skills, 3 active at Mythic, the
  4th at Mythic S, the 5th at Mythic SS. Awakening upgrades these five; it never adds one.
- **Stats.** Slot core (head HP 50; body HP 100 + DEF 6; tail HP 40 + ATK 5; legs DEF 8 + speed 3; arm ATK 15) × rarity (1.0 /
  1.7 / 2.4 / 3.6 / 5.6) plus one role stat (Guard DEF 6, Strike ATK 5, Swift speed 4, Keen crit chance 3%, Mend life steal 2%,
  Fury rage gain 6%; Guard arms add counter chance 3%, Swift arms combo chance 3%), all × the rarity and rounded (whole numbers
  below 20, multiples of 5 from 20). A Mythic carries its synergy stat instead (+10% fire / ice / Arc / radiation / bleed / Echo
  damage, +10% alien power, +5% combo / counter / crit chance, +10% HP, +10% rage gain). **Arms are interchangeable**: no attack
  styles; the worm always attacks with the neutral style (basic 100%, combo 60%, counter 70%, rage 220%).
- **Levels.** Caps: Common 10, Rare 20, Epic 30, Legendary 50; a Mythic's cap rises with its ladder step: Mythic 70, +1 75, +2 80,
  S 85, S+1 90, S+2 95, **SS 100** (final; no plate raises). Flat stats (HP, ATK, DEF, speed) add 15% of their level-1 value per
  level (whole numbers: Firebrand Blaster +85 ATK, +13 per level → +1 372 at 100); every percentage stat grows linearly from its
  level-1 value to **3×** at the part's max level (a Mythic's at level 100: +10% fire damage → +30%; a Common +3% → +9% at 10). Ore
  per level `30·L^1.4`, Gear Cores `2·(L−9)^1.25` from level 10 (unchanged curves, now up to 100).
- **No merging.** Nothing merges into a higher rarity. Only Mythics climb: Mythic → +1 → +2 → **S** → S+1 → S+2 → **SS** (final).
- **Awakening** (once, Mythic or higher, 50 Awaken Cores): a new name, ×1.4 level-1 stats, and each of the five skills becomes its
  **awakened version** (same trigger and mechanic, a bigger number, a lower threshold or a twist). The ladder step still decides
  how many are active. An awakened Mythic still climbs as the target; it is never material.
- **Interplay.** A part never sets a game value absolutely: it shifts it ("2 Heat sooner", "25 less rage"), all sources add up, and
  every shifted value has a floor or ceiling: Supernova ≥ 6 Heat, a rage attack ≥ 50 rage, a sidekick action ≥ 50 charge, dodge
  ≤ 85%. Blow-ups are capped across all sources: fully avoided hits 1 per round, extra turns 2 per round (more move to the next
  round; an extra turn never grants another), free rage attacks 2 per turn, a status ticks at most 3 times per enemy per round,
  Echoes chain at most 2 deep, Drones from all sources share one cap of 5. "Stacks by rule" items (battle-start shields,
  overheal-into-shield caps, rage refunds, strike-backs on undone hits, Drone guards) simply add up.
- **Crowd control** exists only on Mythic S / SS skills, at most once per enemy per battle; bosses lose 20 rage instead.

## The Mythic ladder and Metal

| Step | Level cap | Copies (same Mythic) | Other Mythics (same slot) | Metal | Ore | Unlocks |
|---|---|---|---|---|---|---|
| Mythic+1 | 75 | 1 | – | 100 | 2 200 | level cap |
| Mythic+2 | 80 | 1 | – | 150 | 3 400 | level cap |
| **Mythic S** | 85 | 1 | – | 200 | 5 100 | skill 4 |
| Mythic S+1 | 90 | 2 | 2 | 300 | 7 600 | level cap |
| Mythic S+2 | 95 | 2 | 2 | 450 | 11 400 | level cap |
| **Mythic SS** | 100 | 5 | 5 | 600 | 17 100 | skill 5 (final) |
| Total | | 12 | 9 | 1 800 | 46 800 | |

- **A copy** is the same Mythic at its base step (not climbed, not awakened). **An other Mythic of the same slot** is any different
  Mythic of that slot at its base step, not awakened (the two arm slots share one pool). Equipped, locked, forged, climbed or
  awakened Mythics are never material. A copy of the item being climbed counts only as a copy. Materials refund their level Ore,
  Gear Cores and runes (`PartClimb`).
- **Metal** (player-facing name; currency id `metal`) is the value of a spare standard part: Common **1**, Rare **2**, Epic **6**,
  Legendary **20**. Feeding (`PartFeed`) turns parts into Metal at once and refunds their level Ore, Gear Cores and runes.
  Equipped, locked and forged parts are never fed; Mythics are never Metal. The surplus is known and accepted (owner: no second
  sink).
- **Replicator** (`PartReplicate`): 3 spare Mythics (any slot, base step, not awakened) + 5 000 Ore → 1 copy (base step) of a Mythic
  the player owns.
- **Lock** (`PartLock`): a locked part is never fed or used as material.
- **Runes:** slots by rarity as before (Common 0, Rare 1, Epic 1, Legendary 2, Mythic / +1 / +2 3, S / S+1 / S+2 4, SS 5); the
  **Forge** (Epic+, two paid unlocks: +1 rune slot and +10% / +15% level-1 stats each) stays; rune slots are capped at 5.

## Part skills

- Every skill is a `SkillDef` (`Source = Part`, not draftable) whose `Description` and card `Text` are the approved text word for
  word; its numbers are the text's numbers (`PartSkillTests`). Ids: `<item>_sk1…` (Epic 1, Legendary 2, Mythic 5) and the awakened
  `myth_<name>_aw1…5`. Slot trigger languages: head = openers and precision, body = when hit / low HP, tail = rhythm, legs = dodges
  and speed, arm = attacks.
- Standard skills never use a synergy mechanic (no statuses, Arcs, Echoes, sidekicks or Supernovas); every Mythic skill uses its
  build identity's keyword.
- The full list with every text (and each awakened version): [parts-v2/parts-redesign.html](parts-v2/parts-redesign.html).

## Old saves

Pre-release, no migration (owner): a save from before parts v2 (`PartsState.Model` < 2) drops its old parts and gets the starter
set (one Common per slot, both arms: Scrap Keen Helm, Scrap Guard Shell, Scrap Strike Tail, Scrap Swift Legs, Scrap Strike Cannon,
Scrap Fury Blaster), equipped; runes stay in the inventory (unsocketed). New players get the same set in the starter kit.

## Awaken Cores (extremely rare)

Unchanged: the arena shop (200 medals each, at most 2 per week), top contributors of repelled high-tier invasions, and a rare drop on
Burrow floors 9 (1%) and 10 (1.5%).
