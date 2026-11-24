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
| `OnDodge` | owner dodged (`OnParry` is legacy: parry was removed, it never fires) | the attacker |
| `OnDamaged` | owner took attack or skill damage | the attacker |
| `OnKill` | owner killed a fighter | – |
| `OnLowHp` | once per battle when HP drops below `Combat.LowHpTriggerPct` (30%) | the attacker |
| `OnStatusApplied` | owner applied a status; optional `Param` = status name filter (e.g. `"Burn"`) | the affected fighter |
| `OnBattleWon` | after victory (battle effects like Heal apply to the HP carried on; see run triggers) | – |
| `OnAct` | a **sidekick's** action (its charge is full, or an extra action): its one ability | front enemy |
| `OnSidekickAct` | on the side's worm and mount, right after one of its sidekicks acted | front enemy |
| `OnEnemyDeath` | any fighter of the other side died (its statuses stay readable) | the dead fighter |
| `OnArcJump` | one of the owner's Arc jumps struck (on the worm also for its aliens' jumps) | the struck enemy |
| `OnShieldHit` | the owner's shield absorbed (part of) an attack; amount = absorbed | the attacker |
| `OnFatal` | a hit would kill the owner (CheatDeath / Sacrifice / Loop can save it) | the attacker |
| `OnBlock` | one of the owner's sidekicks intercepted a hit on it | the attacker |
| `OnStackThreshold` | a status the owner applied reached `Param = "Status:N"` stacks (crossing from below) | that fighter |
| `OnStatusConsumed` | the owner consumed stacks (`Param` = status filter); amount = stacks | that fighter |
| `OnTurnEnd` | end of the owner's own turn (after its attacks) | front enemy |

Effects in battle: `Damage` / `TrueDamage` (Value = % of owner ATK; skill hits can crit, cannot be dodged or
blocked, get the family's element bonus: Fire→FireDamage…), `ApplyStatus` (Status, Stacks, Duration 0 =
status default; trigger Chance × (1 + StatusChance/100), then target StatusResist roll), `Heal` / `Shield`
(Value = % of target max HP, × HealPower / ShieldPower; `Param = "overheal_shield"` turns overheal into shield),
`GainRage` (flat), `BuffStat` (`Stat` for `Duration` rounds, 0 = rest of battle; negative values debuff),
`Summon` (enemy skills: `Param` = enemy def id → a copy on the owner's threat scale, e.g. the Storm Hive's spark
drones; player side: `Param` = a `ContentDb.Sidekicks` id → `Stacks` battle-scoped sidekicks whose ability power is `Value` % of
the summoner's ATK — Hive Carrier's Drone Bay, Swarm Beacon; anything else does nothing), `AllyAction` (`Stacks` extra sidekick
actions, round-robin over the side's sidekicks), `Block` (on a sidekick: the worm's next `Stacks` hits are intercepted, guards
stack up to `Combat.MaxGuards` = 3). `CooldownRounds` blocks the trigger for N rounds after it fired.
Recursion is capped at depth 4.

**Conditional damage bonuses:** a `Passive` trigger with `Param = "cond:<condition>"` and `Value = +%`.
Conditions: `vs_boss`, `vs_elite`, `vs_high_hp` (> 70%), `vs_low_hp` (< 40%), `vs_shielded`,
`vs_status:<Status>`, `basic`, `combo`, `counter`, `rage`, `skill`. All matching bonuses (and the `BossDamage`
stat vs bosses) are summed and applied as one multiplier.

**Targeting:** only targetable fighters (worm, enemies) can be attacked or targeted by `Enemy`,
`AllEnemies`, `RandomEnemy`, `LowestHpEnemy`. Aliens, mount and summons are untargetable.
`Worm` = own side's worm; `AllAllies` = every living fighter of the owner's side.

**Other stat semantics:** `Regen` = % max HP healed at the start of each own turn; `LifeSteal` = % of damage
dealt (an untargetable fighter's lifesteal heals the worm); `AlienPower` on the worm adds % ability power to its sidekicks, `MountPower` % ATK to
the mount (`AlienPower` on the mount counts too: Hive Carrier weapons); the mount's HP stat becomes a
battle-start shield on the worm (the mount absorbs damage). Shock / Freeze (element statuses without a DoT): the applier's
Lightning / Ice damage scales their damage-taken bonus. `LootLuck` = % chance of a bonus item on every run loot roll
(tile, event and completion rolls; 100 = always one more); `XpGain` = % more run XP.

**Untargetable fighters** (aliens, mount) are never hit: their HP, DEF, Speed, dodge, counter and regen do nothing
in battle. Alien / mount skills that should help defensively buff the worm (`BuffStat` with `Target = Worm`).

## Skill redesign (owner-approved proposal rev 2): engine primitives

**Trigger fields.** `If` gates a trigger (and a `cond:` bonus) on a condition of the context fighter / the owner
(`BattleConditions`): `has:Status[:N]`, `lacks:Status`, `max:Status` (at the cap its applier's rules give), `unblocked:Status`,
`hp_below:P`, `boss`, `not_boss`, `statuses:N`, `self_hp_below:P`, `self_shielded`, `sidekicks`, `one_enemy`, `first_turn`,
`first_hit`, `first_hit_taken` (first hit taken this round), `combo_strikes:N`; terms joined with `&`; unknown terms fail content
validation. `EveryN` fires on every Nth unit (stacks applied for `OnStatusApplied`, else events), counted before the chance roll.
`MaxPerBattle` limits a trigger ("once per battle"). `Grow` picks the number a level grows (`Auto`, `Chance`: Ice Shard's proc,
`Value`, `Fixed`: a part that stays, Frost Armor's chill). Negative stats are costs and never scale with level.

**Skill fields.** `Needs` is an offer gate: drafts, shops and Mythic choices offer the skill only once the run owns the source
(`RunDraft.HasSource`: `arc`, `applies:Status` / `freeze`, `consume:Status`, `echo`, `shield` (a shield effect or a mount),
`aliens` (a squad or a sidekick summon), `blocks`), read from run skills (any signature), meta skills and the squad's skills.
`Text` is the card template: `|` between lines, `!` marks a cost (the client shows it in the warning colour), `{vN}` `{cN}` `{sN}`
`{dN}` `{bN}` (trigger N's value / chance / stacks / duration / buff stat) and `{pN}` (stat N) take the leveled numbers.
Card texts stay at most ~20 words at every level (`SkillRedesignContentTests`).

**Conditional bonuses.** `cond:per_stack:Status` (Value % per stack on the target), `cond:status_count` (per different status),
`cond:arc` (Arc jumps), `cond:if` (only the `If` gate). Any `cond:` may carry an `If`.

**Rules (R6, `EffectKind.Rule`, Passive).** `Param = "name[:fixed arg]"`, `Value` = the growing number. Status rules (with `Status`)
live on the applier and are read from `StatusInst.Source` (a sidekick's / mount's statuses use its worm's): `max` (+Value cap),
`burst_at:N` (Supernova threshold; Heat's cap rises with it), `no_recoil`, `keeps_shields`, `dot_crit`, `dot` (+Value % tick),
`consume_heal` (+Value % consume heals), `no_expire[:N]` (at N+ stacks), `no_cool` (Chill), `shutdown` (Chill: carriers can't
crit, combo or counter), `double_vs_chill` (Shock). Owner rules: `arc_seek_chill`, `arc_loop` (Value %), `basic_aoe` (Value %),
`combo_no_decay`, `combo_falloff` (Value %), `combo_cap:N`, `proc_cap:N`, `combo_as_counter`, `counter_combo`, `no_turn_attack`
(rage attacks still fire), `counter_all`, `hivemind`, `symbiote`, `overkill`, `last_light:P`, `echo_strongest`, `extra_turn`,
`lose_turn_every:N`, `dot_ticks:N`, `decay:N`.

**Effects (R2, R7, R13, R14).** `BlockStatus` (no Status for Duration rounds, the per-target re-apply cooldown `StatusCd`),
`ForceSupernova` (+Value %), `MultiplyStatus` (× Value/100, `Param "cap:N"`), `TransferStatus` (Value % of a dead fighter's
stacks), `TickStatus` (now, at Value %, once per target per round), `Execute` (non-bosses below Value % HP), `Reflect` (Value % of
the trigger's amount), `CheatDeath` (`Param "counter_window"`: counter every hit until your next turn), `FreeRageAttack`, `Charge`
(next counter +Value %), `LoseHp` (never below 1), `StackAll` (+Stacks of each multi-stack status, once per target per round),
`Sacrifice`, `Echo` (repeat the last / strongest attack Stacks times, × (1 + Echo damage %)), `Rewind` (undo the last Stacks hits),
`Loop` (every fighter back to the start of the round before last; logged per fighter), `StartRage` (board: the next battle).
`ConsumeStatus` Param tokens: `take:N` (partial), `dmg:X`, `heal:X`, `buff:Stat:X` (per stack, rest of the battle), `cap:X`,
`refund_max:X` (rage when the stacks were at their cap); `$` = Value (the growing number); the old form (`heal:N`, Value = damage
per stack) still reads. `ApplyStatus` Param tokens: `per_status` (× different statuses on the target), `per_unit` (× the trigger's
amount: Fallout Wounds' stacks consumed). `BuffStat` Param tokens: `until_turn` (ends when the holder's next turn starts),
`next_turn` (lasts through the holder's next turn), `reset_no_combo`. `Shield` `of_amount`: a share of the trigger's amount.
`AllyAction` `Param "all"`: every sidekick acts. A worm's `Block` (Bodyguard) is held by its sidekicks.

**New statuses.** `Exposed` (+1% damage taken per stack, permanent), `Stagger` (the next rage attack is lost; Thermal Shock on
bosses), `Delay` (skips the next turn, no re-apply cooldown). A status applied to a fighter during its own turn does not count
down at that turn's end (a counter's stun, Decoy's charm). Stats `ArcDamage`, `EchoDamage`, `DamageTaken` (a cost),
`HealReceived`. New log actions: `StatusBlocked`, `Execute`, `CheatDeath`, `Echo`, `Loop`, `Staggered`, `Sacrifice`.

**Parry is removed.** No roll, no content; `StatId.ParryChance` / `ParryReduction`, `TriggerKind.OnParry` and the log flag stay
for old saves (an old save's parry chance counts as counter chance). Lab Bulwark Protocol refreshes its shield on counters.

**Several signatures.** A run holds up to `RunTuning.MaxSignatures` (3): the start pick, a miniboss Mythic choice (25%: one Mythic
replaced by an unowned signature of an unlocked synergy, owned synergies preferred) and the rare Resonance event (1 of 2, or
coins). Never in shops. Each awakens on its own synergy count; every owned signature's synergy gets the draft weight and the synergy
slot cycles between them; none is ever lost. Hivemind tunes each alien to one signature (round-robin in the order found).

**Ladders.** Common ×1, ×5/3, ×8/3 (15 → 25 → 40); Legendary ×1, ×8/5 (25 → 40); Mythics never level. The list with every
level's text: [run-skill-levels.md](run-skill-levels.md).

## Sidekicks (sidekick pass)

Aliens and player-side summons (drones) are **sidekicks** (`CombatRole.Alien` / `CombatRole.Summon`, untargetable). Only the
worm is attacked; a sidekick never attacks, gains no rage and has no statuses. It sits in the turn order (Mount → Aliens →
Summons → Worm) and each of its turns adds **Speed × (1 + the worm's `SidekickHaste` %)** charge; at `Combat.SidekickActAt`
(100) it **acts** once (spending 100; at most one own action per turn; battles start at 50 charge). An action is a
`BattleAction.SidekickAct` log marker (SkillId = the ability, Stacks 1 = an extra action) followed by the ability's effects
(its `OnAct` triggers), then `OnSidekickAct` on the worm and mount (Hivemind, Alien Charm, Pack Tactics, Hive part skills).

- **Frequency:** alien base Speed by rarity 34 / 37 / 40 / 44 / 50 (Common … Mythic): a Common acts about every 3 worm
  turns, a Mythic every 2. Speed genes and the Swift trait raise it. Drones: Speed 40.
- **Ability power** (the sidekick's ATK in battle) = ATK + HP × 0.05 + DEF × 1 (`SidekickHpToPower` / `SidekickDefToPower`;
  base, genes, level, stars and traits all count), × (1 + alien power %). Damage abilities deal % of it; status abilities
  carry it as their DoT / Heat power. Heal / Shield / GainRage / BuffStat abilities are × the **support scale** =
  min(1.5, (power before alien power / 35)^0.2) × (1 + alien power %).
- **One ability per sidekick**, simple (Mythic Nebulisk: two buffs). A sidekick's buff refreshes on its next action instead of
  stacking with itself. A sidekick's lifesteal (Vampiric) heals the worm.
- **Block:** a guard on the worm intercepts the next attack hit (basic / rage / combo / counter): nothing lands, no on-hit or
  when-hit triggers; `BattleAction.Guard` (guard raised) and `BattleAction.Block` (Actor = blocker, Amount = damage stopped).
- **Command** (`sig_alien`): each round `Stacks` (1 / 2 / 3 awakened) extra sidekick actions.
- **Symbiote** stops every sidekick's own turns (aliens and drones); Bodyguard / Decoy work with any guard (Pebblehorn's too).
- Alien combat traits: Tough (+15% HP), Fierce (+15% ATK), Armored (+30% DEF) → power; Swift (+15% Speed) → frequency;
  Lucky / Vampiric / Pyromaniac / Stormborn for damage / Heat / Shock abilities.

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

## Signatures, synergies and key mechanics (signatures pass)

- **Synergies:** Fire, Radiation (internal id `Poison`: `SkillFamily.Poison`, `StatusId.Poison`, `StatId.PoisonDamage`; shown as
  "Radiation" via `SkillText.FamilyName` / `StatusName`) and Bleed are open from the start (`RunTuning.StarterSynergies`). Ice,
  Lightning, Alien, Counter and Combo are **Laboratory packages** (`LabNodeKind.Package`, branch `packages`, one level; a chain:
  Combo → Ice → Lightning → Counter → Alien, each needing the previous package and a stat node at level 2; Alien also needs the
  Aliens feature). `CombatLoadout.UnlockedFamilies` lists the open synergies; `RunDraft.FamilyUnlocked` keeps a locked synergy's
  run skills and Mythics out of drafts, shops and Mythic choices (an empty list = a run saved before: everything). Lab skill nodes
  of a package synergy need the package. Crit is no synergy any more: Keen Eye, Brutal Force, Executioner and the three former
  Crit Mythics are generic (`SkillFamily.Crit` stays for parts / heroes / mounts and old saves).
- **Signature skills** (`SkillDef.Signature`, `sig_<synergy>`, not draftable): one per synergy, each does one simple thing (Ignite,
  Irradiate, Lacerate, Chill: on hit 1 stack; Arc: on hit 50% ATK to 1 other enemy; Command: each round one alien attacks again;
  Riposte: when hit 30% chance to counterattack; Flurry: on hit 30% chance to strike again). `RunSystem.SignatureOffer` = up to
  `RunTuning.SignatureChoices` (3) of the unlocked synergies' signatures, shuffled from `RunState.SignatureSeed` + the run counter
  (stable until a run starts, identical on client and server); `RunStart.SignatureId` picks one (empty = the first). Awakening
  (`RunSystem.UpdateSignature`, `SkillLeveling.Awaken`): level 2 / 3 at `RunTuning.SignatureAwakenAt` = 3 / 6 owned skills of its
  synergy (the signature itself is never counted by `RunDraft.FamilyCount`): stacks, jumps and aliens ×level, proc chances ×1 /
  ×1.5 / ×2. It is never drafted, bought, levelled by drafts or events, or lost to "lose a random skill". Drafts, Mythic choices
  and shops weight its synergy ×`SignatureFamilyWeight` (1.4) and the synergy slot offers it from the first draft.
- **One Mythic pool:** Mythics are never in the normal draft or the shop and have no family / prerequisite gates; every miniboss
  choice draws from all Mythics of the unlocked synergies plus the generic ones (owned synergies weighted).
- **Heat (Fire):** permanent stacks (max 10). At 10 the fighter goes **Supernova** (`StatusDef.BurstAtStacks`): every targetable
  fighter of its side (itself included) takes `BurstAtkPct` 250% of the heater's ATK × (1 + fire damage %) × (1 + `SupernovaDamage`
  %), DR and damage-taken statuses apply; the heater's side's worm takes `BurstRecoilPct` 12% of its max HP × (1 − `SupernovaGuard`
  %); the Heat is removed; the heater's `OnSupernova` triggers fire. Log: `BattleAction.Supernova` (marker with Amount 0, then one
  entry per hit). Killing or cooling the enemy first avoids the recoil. Legacy `StatusId.Burn` has no content (old logs read "Heat").
- **Cooling:** `EffectKind.RemoveStatus` (Status, Stacks; 0 = all) and `StatusDef.Cools` (each Chill stack applied removes one Heat);
  logged as `BattleAction.StatusRemoved`.
- **Radiation:** permanent, unlimited stacks (9999), 4% of the applier's ATK × (1 + radiation damage %) per stack each turn, −30%
  healing received.
- **Bleed:** permanent, max 10 stacks, 6% ATK per stack each turn, ignores shields. `EffectKind.ConsumeStatus` removes all stacks:
  Value % ATK skill damage per stack, `Param = "heal:N"` heals the owner's worm N% of it (Blood Frenzy, Bloodlust).
- **Chill (Ice):** max 10, 3 turns, −2% damage dealt per stack (× the chiller's ice damage), cools Heat.
- **New effects / targets:** `Strike` (attack the context target again; `Param "counter"` = a counterattack), `AllyAction` (Stacks
  aliens attack again), `TargetKind.OtherEnemies` (Stacks random enemies other than the context target), trigger `OnSupernova`,
  stats `SupernovaDamage` / `SupernovaGuard` (points).
