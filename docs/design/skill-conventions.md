# Skill conventions (universal `SkillDef` format)

Owner: Run workstream (interpreter = `BattleSimulator` for battle triggers, `RunSystem` for board triggers).
Every power source (run picks, Laboratory, hero traits, mount weapons, alien skills, building boons, parts)
uses `SkillDef` = passive stat package (`Stats`) + `Triggers` (condition → effect). Other workstreams add
their own skills with `ContentDb.AddSkill` and expose them through the loadout:

| Where the skill id goes | Who owns the skill | Effect |
|---|---|---|
| `CombatLoadout.MetaSkillIds` | worm (hero traits, part effects, building boons) | passive stats + battle triggers on the worm; board triggers in runs |
| `CombatLoadout.MetaSkillIds` with `Source = Mount` | the mount combatant (if a mount is equipped, else the worm) | mount weapons |
| `CombatAlien.SkillIds` | that alien combatant | battle triggers only |
| `CombatLoadout.DraftPoolSkillIds` | nobody yet — only enters the run draft pool | Laboratory toggles |

Stats are percent points for chance-type stats (25 = 25%). `StatMod.Percent` is additive within the block.

## Battle triggers (BattleSimulator)

| Trigger | Fires | Context target (`TargetKind.Enemy`) |
|---|---|---|
| `Passive` | never (stat package only; see conditional bonuses) | – |
| `OnBattleStart` | once, before round 1, in turn order | front enemy |
| `OnRoundStart` / `OnRoundEnd` | every round for every living fighter | front enemy |
| `OnTurnStart` | owner's turn, after DoTs/regen, if not stunned/frozen | front enemy |
| `OnAttack` | before each of the owner's attacks (basic/rage/combo/counter) | the attacked fighter |
| `OnHit` / `OnCrit` | after the owner's attack lands (not dodged) | the hit fighter |
| `OnCombo` / `OnCounter` / `OnRageAttack` | after a combo / counter / rage attack | its target |
| `OnDodge` / `OnParry` | owner dodged / parried | the attacker |
| `OnDamaged` | owner took attack or skill damage | the attacker |
| `OnKill` | owner killed a fighter | – |
| `OnLowHp` | once per battle when HP drops below `Combat.LowHpTriggerPct` (30%) | the attacker |
| `OnStatusApplied` | owner applied a status; optional `Param` = status name filter (e.g. `"Burn"`) | the affected fighter |
| `OnBattleWon` | after victory (battle effects like Heal apply to the HP carried on; see run triggers) | – |

Effects in battle: `Damage` / `TrueDamage` (Value = % of owner ATK; skill hits can crit, cannot be dodged or
parried, get the family's element bonus: Fire→FireDamage…), `ApplyStatus` (Status, Stacks, Duration 0 =
status default; trigger Chance × (1 + StatusChance/100), then target StatusResist roll), `Heal` / `Shield`
(Value = % of target max HP, × HealPower / ShieldPower; `Param = "overheal_shield"` turns overheal into shield),
`GainRage` (flat), `BuffStat` (`Stat` for `Duration` rounds, 0 = rest of battle; negative values debuff),
`Summon` (enemy skills only: `Param` = enemy def id → a copy on the owner's threat scale, e.g. the Storm Hive's spark
drones; on the player side it does nothing — player drones were removed in the dead-effects pass). `CooldownRounds` blocks the trigger for N rounds after it fired.
Recursion is capped at depth 4.

**Conditional damage bonuses:** a `Passive` trigger with `Param = "cond:<condition>"` and `Value = +%`.
Conditions: `vs_boss`, `vs_elite`, `vs_high_hp` (> 70%), `vs_low_hp` (< 40%), `vs_shielded`,
`vs_status:<Status>`, `basic`, `combo`, `counter`, `rage`, `skill`. All matching bonuses (and the `BossDamage`
stat vs bosses) are summed and applied as one multiplier.

**Targeting:** only targetable fighters (worm, enemies) can be attacked or targeted by `Enemy`,
`AllEnemies`, `RandomEnemy`, `LowestHpEnemy`. Aliens, mount and summons are untargetable.
`Worm` = own side's worm; `AllAllies` = every living fighter of the owner's side.

**Other stat semantics:** `Regen` = % max HP healed at the start of each own turn; `LifeSteal` = % of damage
dealt (an untargetable fighter's lifesteal heals the worm); `AlienPower` / `MountPower` on the worm add % ATK (and HP for
aliens) to aliens / the mount (`AlienPower` on the mount counts too: Hive Carrier weapons); the mount's HP stat becomes a
battle-start shield on the worm (the mount absorbs damage). Shock / Freeze (element statuses without a DoT): the applier's
Lightning / Ice damage scales their damage-taken bonus. `LootLuck` = % chance of a bonus item on every run loot roll
(tile, event and completion rolls; 100 = always one more); `XpGain` = % more run XP.

**Untargetable fighters** (aliens, mount) are never hit: their HP, DEF, Speed, dodge, parry, counter and regen do nothing
in battle. Alien / mount skills that should help defensively buff the worm (`BuffStat` with `Target = Worm`).

## Run (board) triggers (RunSystem)

Board triggers fire for the worm's active skills (`MetaSkillIds` + drafted run skills). `Param = "tile:<TileCategory>"`
limits `OnTileLand` / `OnTilePass` to that tile category (e.g. `"tile:Campfire"`, `"tile:Loot"`). `Chance` works as usual.

| Trigger | When |
|---|---|
| `OnRunStart` | once at run start (and when a skill with it is gained mid-run) |
| `OnRoll` | after every dice roll |
| `OnTileLand` / `OnTilePass` | landing on / passing a tile |
| `OnLevelUp` | each run level-up |
| `OnBossAwaken` | each awakening threshold reached |
| `OnBattleWon` | after each won battle (board effects; battle effects run inside the battle) |

| Effect | Meaning |
|---|---|
| `ExtraDice` | Value extra dice: with `OnRunStart` for **every** roll of the run, otherwise for the **next** roll |
| `RunStatGrowth` | add `Stat` (StatMod) to the worm for the rest of the run |
| `GainRunCoins` | Value run coins |
| `HealOnTile` (or `Heal` on `OnRoll`/`OnTileLand`/`OnTilePass`/`OnLevelUp`) | heal Value % of max HP (`Heal` on `OnBattleWon` is applied inside the battle) |
| `SlowAwakening` | `OnRunStart`: all meter gains −Value % (max 90); any other trigger: drain Value meter points |
| `SkipNegativeEvent` | **preferred form**: `OnRunStart`, Value = N → skip the next N negative events this run |

### Param conventions on `OnRunStart`
- `Param = "<skillId>"` or `"skill:<skillId>"` → the run starts with that skill (Effect is ignored; replaces its
  `UpgradesFrom` if owned). Start skills' own `OnRunStart` effects apply too.
- `Param = "random_skill:<Rarity>"` → a random draft-eligible skill of at least that rarity.
- `Param = "skip_negative_events"`, `Value = N` → legacy alias of `EffectKind.SkipNegativeEvent`.

**What counts as negative (skippable):** landing on a `Negative`-valence tile (hazards, stat debuffs), opening an
event marked `Negative`, and event outcomes whose effects are all harmful (damage, stat loss, coin loss,
awakening +, boss buffs, battles, strengthening enemies). Deliberate trades (cost + benefit) are not skipped.

## Run content notes

- **Threat curve:** enemy HP/ATK/DEF × `ThreatMultiplier(t) = 1.075^t · (1 + 0.05·t)`. Main portals
  t = 0..29 (portal·10 + level−1), Overcharged +8, Burrow 4..22, Dark Side 30..48 (+40–90% ATK/HP modifiers).
  t=60 ≈ 260×, t=100 ≈ 8000×: room for years of portals as worm/parts/buildings scale. Chance stats do not scale.
- **Danger:** each pending loot item carried raises enemy HP/ATK by `RunTuning.DangerPctPerLoot` (5%).
- **Run drops with item kinds** (`part`, `rune`, `blueprint`, `alien`) use `Id = "*"` and `Rarity` = rarity index,
  meaning "a random def of that rarity" — the owning granter resolves it deterministically with `ctx.Rng`
  (`Systems/RewardWildcard.cs`; parts map the tier to the part ladder like the Part Signal). Every item kind has a
  granter after integration, so there is no fallback currency.
- **Run rewards** are pending until completion; currencies are multiplied by `run_reward:<currency>` /
  `run_reward:*` modifiers, the level reward multiplier and Overcharged ×1.8. Failure / abandon pays nothing.

## Run skill levels and numbers (skill tuning pass)

- **Ladder:** `RunTuning.SkillLevelLadder` = 15 / 25 / 40 (each rung the sum of the two below). Common skills climb rungs
  1→3 (Lv1 numbers ×1, ×5/3, ×8/3), Legendary rungs 2→3 (×1, ×8/5), Mythics never level (`SkillLeveling`).
- **One growing number per trigger:** stats, conditional bonuses, buff / growth stats and value effects grow their number;
  count effects (`ApplyStatus`, `ExtraDice`, `SkipNegativeEvent`) grow their chance up to 100%, then the count (status
  stacks; the duration of single-stack statuses such as Freeze / Stun / Charm; dice). Values round to whole numbers from 10
  (halves below; coins and counts whole) and always move past the previous level, so every level reads differently.
- **Authoring:** a typical Common gives +15% at Lv1 (proc chances 30%), a Legendary ~+25%; pick Lv1 numbers that are
  multiples of 3 (Common) or 5 (Legendary) so levels read as whole numbers. Point stats (everything but HP / ATK / DEF /
  Speed: crit, dodge, element damage, alien / mount power, rage gain, status chance, loot luck, run XP …) are **flat**
  (`Stat(StatId.FireDamage, 15)`) everywhere in the content: a `Percent` on them multiplies a zero base and does nothing.
  `ContentValidationTests` walks the whole `ContentDb` and fails on any; values built at runtime (run tiles and events,
  building collection bonuses) go through `StatMod.Bonus(stat, value)` (percent of a core stat, points of a point stat).
- **Every draftable skill must change an outcome** (`SkillTuningTests`): battle skills change a seeded battle at every
  level and each trigger lands its effect; board skills have their own run tests. Removed for doing (almost) nothing:
  drone summons (Mothership Link → replaced by the Overmind capstone, Swarm Beacon, lab Drone Bay) and loot % (Hoarder,
  lab Treasure Sense: it only touched the ~1 account-loot item a run carries). Saved states with removed ids keep working
  (unknown ids are skipped; `LaboratorySystem.StopRemovedResearch` stops research running on a removed node).
- **Every skill of every source must change an outcome** (`DeadEffectsTests.EverySkill_OfEverySource…`): run, lab, hero,
  mount (judged with the mount's other weapons of that step), alien, building, part and enemy skills each change a seeded
  battle, or a seeded auto-played run for board-only skills. The dead-effects pass removed the last drones (part opener /
  Hive Apex summons → an opening barrage / a squad rage opener; the Hive Carrier's "Drone Bay" weapon is the Spore Cannon)
  and the `ExtraLoot` effect (Salvage Yard now gives Loot Luck), and fixed alien support skills that sat on the alien
  (Snap Back's counter chance: aliens are never attacked) and dead alien traits (Tough → Brawler, Armored → Giant Slayer,
  Swift → dodge ignore; ids kept).
- **Text:** `SkillText.Upgrade(content, skill, from, to)` renders the upgrade inline ("+15% → +25% crit chance"); offers of
  an owned skill show it with the target level as title (`SkillText.Title(skill, level)` = "Keen Eye Lv 2").
