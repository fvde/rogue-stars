# Skill conventions (universal `SkillDef` format)

Owner: Run workstream (interpreter = `BattleSimulator` for battle triggers, `RunSystem` for board triggers).
Every power source (run picks, Laboratory, hero traits, mount weapons, alien skills, building boons, parts)
uses `SkillDef` = passive stat package (`Stats`) + `Triggers` (condition → effect). Other workstreams add
their own skills with `ContentDb.AddSkill` and expose them through the loadout:

| Where the skill id goes | Who owns the skill | Effect |
|---|---|---|
| `CombatLoadout.MetaSkillIds` | worm (hero traits, part effects, building boons) | passive stats + battle triggers on the worm; board triggers in runs |
| `CombatLoadout.MetaSkillIds` with `Source = Mount` | the mount combatant (if a mount is equipped, else the worm) | mount kits (passive + active) |
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
drones; player side: `Param` = a `ContentDb.Sidekicks` id [`;max:N`] → `Stacks` battle-scoped sidekicks whose power is `Value` %
of the worm's ATK — Hive Carrier's Drone Bay, Swarm Beacon, Swarmheart; anything else does nothing), `AllyAction` (`Stacks` extra
sidekick actions, round-robin over the side's sidekicks; `all`, `others`), `Block` (the worm's next `Stacks` hits are intercepted,
guards stack up to `Combat.MaxGuards` = 3: Bodyguard), `Guard` (aliens pass: the target's next attack hit deals `Value` % less). `CooldownRounds` blocks the trigger for N rounds after it fired.
Recursion is capped at depth 4.

**Conditional damage bonuses:** a `Passive` trigger with `Param = "cond:<condition>"` and `Value = +%`.
Conditions: `vs_boss`, `vs_elite`, `vs_high_hp` (> 70%), `vs_low_hp` (< 40%), `vs_shielded`,
`vs_status:<Status>`, `basic`, `combo`, `counter`, `rage`, `skill`. All matching bonuses (and the `BossDamage`
stat vs bosses) are summed and applied as one multiplier.

**Targeting:** only targetable fighters (worm, enemies) can be attacked or targeted by `Enemy`,
`AllEnemies`, `RandomEnemy`, `LowestHpEnemy`. Aliens, mount and summons are untargetable.
`Worm` = own side's worm; `AllAllies` = every living fighter of the owner's side.

**Other stat semantics:** `Regen` = % max HP healed at the start of each own turn; `LifeSteal` = % of damage
dealt (an untargetable fighter's lifesteal heals the worm); `AlienPower` on the worm adds that % to its sidekicks' hits, heals and shields (never chances, stacks or buffs), `MountPower` = mount damage
(the worm's plus the mount's own: its steps and Fuel level; the mount has no other stats and gives no shield). Shock / Freeze (element statuses without a DoT): the applier's
Lightning / Ice damage scales their damage-taken bonus. `LootLuck` = % chance of a bonus item on every run loot roll
(tile, event and completion rolls; 100 = always one more); `XpGain` = % more run XP.

**Untargetable fighters** (aliens, mount) are never hit: their HP, DEF, Speed, dodge, counter and regen do nothing
in battle. Alien / mount skills that should help defensively buff the worm (`BuffStat` with `Target = Worm`).

## Skill redesign (owner-approved proposal rev 2): engine primitives

**Trigger fields.** `If` gates a trigger (and a `cond:` bonus) on a condition of the context fighter / the owner
(`BattleConditions`): `has:Status[:N]`, `lacks:Status`, `max:Status` (at the cap its applier's rules give), `unblocked:Status`,
`hp_below:P`, `boss`, `not_boss`, `statuses:N`, `self_hp_below:P`, `self_shielded`, `sidekicks`, `one_enemy`, `first_turn`,
`first_hit`, `first_hit_taken` (first hit taken this round), `combo_strikes:N`, `rage_crit` (the owner's latest rage attack landed as
a crit: lab Frost Nova); terms joined with `&`; unknown terms fail content
validation. `EveryN` fires on every Nth unit (stacks applied for `OnStatusApplied`, else events), counted before the chance roll.
`MaxPerBattle` limits a trigger ("once per battle"). `Grow` picks the number a level grows (`Auto`, `Chance`: Ice Shard's proc,
`Value`, `Fixed`: a part that stays, Frost Armor's chill). `Step` > 0 grows that number linearly instead (+Step per level: the
owner's crowd-control ladders, Knockout 10% → 15% → 20%). Negative stats are costs and never scale with level.

**Skill fields.** `Needs` is an offer gate: drafts, shops and Mythic choices offer the skill only once the run owns the source
(`RunDraft.HasSource`: `arc`, `applies:Status` / `freeze`, `consume:Status`, `echo`, `shield` (a shield effect: a hero or mount kit counts, a bare mount no longer),
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

## Crowd control (CC pass, owner decisions)

CC = statuses that skip turns or charm (`StatusDef.SkipsTurn` / `Charms`: Stun, Freeze, Delay, Charm).

- **No Common applies crowd control**, in any source (run, lab, part, hero, mount, alien, building), except the two the owner set:
  lab **Knockout** (every research level; ids `lab_retaliation*`) and **Head Start**. `ContentValidationTests` holds that exact
  allowlist, so a new Common with CC fails. Rejected by the owner: caps on CC chance, "levels never raise CC chance", CC-immune
  bosses, "all-enemy CC once per battle".
- **Any Freeze resets the target's Chill to 0** (`StatusDef.Clears`, whatever the source, also an enemy freezing the worm).
- Ice has its own Freeze source: **Deep Freeze** (Legendary: an enemy at 5+ Chill is frozen when Chill is applied and Freeze is off
  its re-apply cooldown; levels raise the damage vs frozen). Cold Snap and Shatter keep `Needs("freeze")`: Deep Freeze, lab Frost
  Nova, the Ice part Apex and Frost Sigil all satisfy it.
- Numbers: Knockout (lab Common, was Retaliation: no counter chance any more) stun on counter 10 → 15 → 20% at research I,
  14 → 21 → 28% at research V; lab Frost Nova casts on a critical rage attack (Freeze + 60% ATK to every enemy at research I,
  +10% per research level; in-run levels raise the damage, never the freeze); Head Start 30 → 40 → 50%; Decoy 20 → 30%;
  Frostmite's Rime Bite (Freeze) and Chronoslug's Slow Ooze (Delay, with a 3-round re-apply cooldown like Freeze's) 5 / 9 / 13 /
  16 / 20% at tier stars 1 / 3 / 5 / 7 / 10 (held between; traits never raise them); no Common alien applies CC (Shiverpup only
  Chills), so the allowlist is unchanged; Ice parts and Ice family skills chill instead of freezing
  ([parts.md](parts.md)); Stasis Field, Thermal Shock and the Lightning / Ice part Apex are unchanged.

## Sidekicks (aliens pass, owner review rev 4.3)

Aliens and player-side summons (drones) are **sidekicks** (`CombatRole.Alien` / `CombatRole.Summon`, untargetable). Only the
worm is attacked; a sidekick never attacks, gains no rage and has no statuses. It sits in the turn order (Mount → Aliens →
Summons → Worm) and each of its turns adds **Speed × (1 + the worm's `SidekickHaste` %)** charge; at `Combat.SidekickActAt`
(100) it **acts** once (spending 100; at most one own action per turn; battles start at 50 charge). An action is a
`BattleAction.SidekickAct` log marker (SkillId = the ability, Stacks 1 = an extra action) followed by the ability's effects
(its `OnAct` triggers), then `OnSidekickAct` on the worm and mount (Hivemind, Alien Charm, Pack Tactics, Hive part skills).

- **No stats.** A sidekick's HP / ATK / DEF are ignored (old snapshots fight the same). Its ATK is its worm's live ATK × (1 +
  alien power %) (`AtkOf`); **"X% ATK" = X% of the worm's hit through the target's DEF** (`ComputeDamage` computes the full-strength
  hit, then takes X% — small hits are never floored away by DEF). Drones: their `AtkPct` (the summon's `Value`) multiplies the same.
  Heals / shields = % of the worm's max HP × (1 + alien power %); Thorn Harvest heals % of ATK per stack. Alien power (Squad Bond,
  Pack Tactics, Hive parts, mount) is never applied to chances, stacks or buffs.
- **Shared statuses:** a sidekick's statuses are applied with **the worm as their source** (the worm's power, rules and
  `OnStatusApplied` / `OnStackThreshold` triggers; DoT ticks are the worm's damage); the log names the sidekick. A sidekick's
  consume fires the worm's `OnStatusConsumed`.
- **Frequency:** Speed 34 (Common) … 50 (Mythic), per-species profiles below; Swift +15%. Drones: Speed 40.
- **Progression:** stars only (no levels). Each star is its own skill `ask_<species>_<star>` (1 … 10). Numbers are whole; owner
  ranges are exact at 1★ and 10★; wide ranges are interpolated between the tier-star values (rounded half up), small ranges and
  every count / chance move only at tier stars 1 / 3 / 5 / 7 / 10 and hold in between.
- **Guard** (`EffectKind.Guard`, Pebblehorn): one guard per fighter, a new one refreshes it; the next attack hit taken deals
  `Value` % less (cap 75%), the hit still lands so on-hit / when-hit triggers and counters fire. `BattleAction.Guard` (raised,
  Amount = %, Stacks = hits) and `BattleAction.Guarded` (Actor = the guarding sidekick, Amount = damage prevented). Guards fire
  `OnBlock` (Decoy). Bodyguard (run skill) keeps the old full `Block` (`BattleAction.Block`).
- **Traits in battle** (`CombatantSpec.Traits`): Eager acts once at battle start (then starts at 0 charge); Twin +1 stack on every
  2nd action; Wide: a single-target hit also strikes one random other enemy at 50% (a Wide summoner's drones are Wide); Lingering:
  a timed buff, Chill or Shock +1 round, a guard +1 hit (never Freeze / Delay); Bulwark guards and shields +20%; Vampiric 10%
  lifesteal to the worm (also on Void Echo); Swift +15% Speed (a stat).
- **Extra actions** (Command, Tidal Call, Swarm Link, Symbiote, Hive parts, Hivemind's Command): at most
  `Combat.MaxExtraSidekickActions` = 6 per round per side.
- **Drones** zap for their % of the worm's ATK and each action also triggers the worm's **start signature** once (Hivemind: its
  tuned one): Ignite / Irradiate / Lacerate / Chill one stack, Arc a `HiveArcPct` 25% jump, Flurry / Riposte / Echo a
  `HiveStrikePct` 30% strike, Command an extra action (once per round).
- **Command** (`sig_alien`): each round `Stacks` (1 / 2 / 3 awakened) extra sidekick actions. **Symbiote** stops every
  sidekick's own turns.
- **Param tokens** (aliens pass): Damage `combo` (the strike rolls the worm's combo chance for more strikes, decaying like a
  chain, each firing the worm's `OnCombo`); ApplyStatus `cd:N` (re-apply cooldown); ForceSupernova `at:N` (only from N Heat);
  BuffStat `stack:N` (stacks up to N, then holds); ConsumeStatus `heal_atk:$`; Summon `;max:N` (alive from this summoner);
  AllyAction `others`.

| Alien | Rarity | Speed | Ability (card at 1★) | 1★ → 10★ (per star) | Tier-star effect changes |
|---|---|---|---|---|---|
| Blorp | C | 34 | Thorn Lash: Hit the front enemy for 15% ATK and apply 1 Bleed | hit 15 16 17 18 19 21 22 23 24 25 | Bleed 1 → 2 (5★) → 3 (10★) |
| Mossling | C | 34 | Spore Puff: Hit … 15% ATK and apply 2 Radiation | hit 15 → 25 (as Blorp) | Radiation 2 → 3 (5★) → 4 (10★) |
| Pebblehorn | C | 34 | Stone Guard: Guard the worm: the next hit it takes deals 20% less | guard 20 23 25 30 35 38 40 43 47 50 | — |
| Zib | C | 37 | Zip Strike: Strike the front enemy for 20% ATK; uses your combo chance | hit 20 21 22 23 24 26 27 28 29 30 | — |
| Glimmerfly | C | 37 | Static Wings: Zap … 15% ATK and apply 1 Shock | hit 15 → 25 | Shock 1 → 2 (5★) → 3 (10★) |
| Shiverpup (new) | C | 34 | Frost Bite: Bite … 15% ATK and apply 1 Chill | hit 15 → 25 | Chill 1 → 2 (5★) → 3 (10★) |
| Emberkit (was R) | C | 34 | Kindle: Hit … 15% ATK and apply 1 Heat | hit 15 → 25 | Heat 1 → 2 (5★) → 3 (10★) |
| Frostmite | R | 37 | Rime Bite: Bite … 20% ATK. 5% chance to Freeze it | hit 20 → 30 (as Zib) | Freeze 5 / 9 / 13 / 16 / 20% |
| Sporeling | R | 37 | Spore Cloud: Hit every enemy for 8% ATK and apply 1 Radiation | hit 8 8 9 9 10 10 11 11 11 12 | Radiation 1 → 2 (7★) |
| Clackclaw | R | 34 | Snap Back: The worm gets +5% counter chance for 3 rounds | 5 5 6 6 7 7 8 8 8 9 | — |
| Whirrbit | R | 37 | Whirl: +5% combo chance for 3 rounds | 5 → 9 (as Clackclaw) | — |
| Arcwhisk (new) | R | 41 | Arc Whip: Arc to 1 enemy for 20% ATK. Counts as your Arc jump | hit 20 → 30 | targets 1 → 2 (5★) → 3 (10★) |
| Voidmaw | E | 40 | Void Echo: Repeat the worm's last attack at 20% damage (an Echo) | 20 23 25 30 35 38 40 43 47 50 | — |
| Thornqueen | E | 36 | Thorn Harvest: Consume the front enemy's Bleed. Heal the worm 1% of ATK per stack | 1 1 2 2 3 3 4 4 4 5 | — |
| Gravitoad | E | 36 | Rock Ward: Shield the worm for 5% of max HP | 5 8 10 11 12 14 15 17 18 20 | — |
| Chronoslug (new) | E | 40 | Slow Ooze: Hit … 20% ATK. 5% chance to Delay it | hit 20 → 30 | Delay 5 / 9 / 13 / 16 / 20% (cooldown 3) |
| Solarch | L | 44 | Solar Flare: Apply 2 Heat. At 8+ Heat it goes Supernova now, +10% damage | bonus 10 15 20 25 30 33 35 40 45 50 | Heat 2 → 3 (7★) |
| Tidecaller | L | 44 | Tidal Call: Heal the worm 5% of max HP. Another sidekick acts | 5 5 6 6 7 7 8 8 8 10 | — |
| Nebulisk | M | 50 | Starlight Eye: a Starlight stack +3% crit chance, +10% crit damage; max 3, lasts the battle | chance 3 3 4 4 5 5 6 6 6 7; damage 10 10 12 12 14 14 17 17 17 20 | — |
| Swarmheart (new) | M | 50 | Hatch: Hatch a Drone that zaps for 15% ATK. Max 1 Drone | zap 15 → 25 | drones max 1 → 2 (7★) |

Card wording: the full 1★ sentence at every star (the review page abbreviated 3★+); singular for one target / drone ("Arc to 1
enemy", "Max 1 Drone"). Cards ≤ 20 words, no decimals (`AlienAbilityTests`).

## Hero and mount kits (owner-approved Hero and Mount Mechanics page, rev 2)

Hand-written kits replace the ProgFamilySkill hero traits and mount weapons (ProgFamilySkill stays for parts only). Kit table:
[heroes-mounts.md](heroes-mounts.md) (generated). Kit skills are `Source = Hero / Mount`, `Family = None`, no tiers (Common in the
data), texts from `HeroDef.Kit` / `MountDef.Kit` (≤ 20 words, whole numbers, different at every step: `HeroMountKitContentTests`).

- **Ids.** Heroes `hero_x_trait`, `_ability`, `_c5`, `_c7`, `_c10`; a step that changes an element is its own skill replacing it
  (`_ability_3`, `_trait_3`, `_ability_7`, `_ability_10`). Mounts `mount_x_passive_k` / `mount_x_active_k`, k = 1…5 for 1★ / 3★ / 5★ /
  A3 / A5, each replacing the step before. Old ids (`_a1 … _b2`, `_w1 … _s2`) in saved loadouts read as the kit at their step
  (`KitMigration`); hero / mount ids, stars, awakening and levels are unchanged.
- **Mount = vehicle on a reload (X-list R-M1/2/3, X6).** No stats: `AtkOf(mount)` = the worm's ATK × (1 + mount damage %); its
  "X% ATK" is X% of a full-strength hit (like a sidekick's). It never attacks or is attacked; on its turn the timer counts down and
  at 0 it fires its `OnVolley` triggers (rounds 1, 1 + Reload, …; rule `reload:N`; `needs_attack`: held until the worm attacked). The reload is set before
  the shot, so a kill during it can bring the next one sooner (Lance Sight A5, Slipstream, Patience).
  `BattleAction.Volley` marks a shot (Amount 1 = an extra shot: Short Circuit, Whistle; at most one per side per round).
  Rider link: its statuses are applied with the worm as source; its crits roll the worm's crit chance, deal the base crit damage
  and fire the worm's `OnCrit` triggers. `SkillTrigger.Rider` = a mount trigger that listens to its worm's events (status
  applications, crits, rage attacks, extra hits, fatal hits, damage taken, status damage); its `If` reads the worm.
- **Hero abilities (X4, X9).** Rules on the worm: `ability_every:N` (round timer, ticks at the worm's turn even when skipped),
  `ability_turns:N` (its acting turns), `ability_shield_hits:N` (hits its shields absorb; `heals_count:P`: every P% max HP healed
  counts one), `ability_stillness:N` (enemy attacks on the worm, dodged or blocked ones too), `rage_bank:N` (rage above 100 banked; a full bank fires the rage
  attack twice, once per round). `OnAbility` triggers are the ability; `BattleAction.Ability` marks it, `BattleAction.KitCounter`
  (Amount / Stacks = value / max) feeds the HUD ("Starfall 2/4", Stillness 1/5, the bank, the coil, the lance charge).
  `AdvanceTimer` (`ability`, `mount`, `all`, `reload`) moves a timer 1 round sooner, each timer at most once per round.
- **New triggers.** `OnAbility`, `OnVolley`, `OnMountFire` (worm), `OnStatusLost` (X1: any stacks an enemy loses — consumed,
  Supernova, Freeze reset, cooled, expired; amount = stacks), `OnExtraHit` (X3: combo strike, counter, Echo, mount-made attack),
  `OnStatusDamage` (ticks, Supernovas, consumes), `OnSkipTurn` (an enemy turn skipped), `OnSidekickHit` (a sidekick action hit).
- **New effects.** `CarrySignatures` (X5: every owned signature's on-hit on the target × Stacks; Flurry / Riposte / Echo strike for
  HiveStrikePct, Command an extra action; `next_hit`: the worm's next hit carries them), `MountFire`, `FireAbility` (Value = damage
  %), `RiderAttack` (the worm attacks at Value %, `combo`, `echo`, `as_echo`; credited to the mount), `Whistle`, `SpreadStatus`
  (Mirror Palm: Value % of the largest status, `cap:N`, `dmg_per`, `dmg_cap`), `ChargeAllies`, in-battle `GainRunCoins` (Plunder;
  `BattleResult.RunCoins`, the battle's last kill still pays). Targets `SecondEnemy`, `LastAttacker`, `Attackers`, `FastestNonBoss`.
- **Param tokens.** Damage `crit` (always crits), `oncrit` (fires the owner's `OnCrit`), `coil` (+ the stored coil), `coil_split` (the targets share it), `wide_siphon`;
  ApplyStatus `owned` (X10: a random status the side applies, never crowd control / Stagger), `boss_rage:N` (a boss loses rage
  instead); Heal `of_amount`, `per_unit`, `round_cap:N`, `siphon`; GainRage `round_cap:N`, `fill`, `bosses`, `debt` (rage below 0);
  Shield `wall:N`, `wall_all` (Ice Wall burst); RemoveStatus / ConsumeStatus `largest` (X2), `to_front`, `splash:N`; CheatDeath
  `ability:N`, `undo`, `stasis` (Cryo Pod: skip the next turn, heal Value %); FreeRageAttack `worm`; Rewind `reflect:N`; Summon
  `nosig`, `carry`, `guard:N`; any trigger `turn_cap:N`. Rules `status_crit` (X7), `double_tap`, `extra_crit`, `pile_on:max`,
  `element_pool` (X11), `deep_wells:2` (X12: Bleed / Chill / Shock cap ×2 for applying; `max:` conditions read the base cap),
  `spotlight`, `contagion` (X15), `slipstream`, `coil`, `coil_cap`, `coil_recoil:N`, `coil_counter`, `coil_window` (X16), `siphon_cap`.
  Conditions `worm_rage:N`, `crit_kill`, `extra_hits:N`, `siphon_full`.
- **Shared caps.** One fatal-save per battle per fighter across every source (Last Word, Sacrifice Protocol, Loop, Last
  Constellation, Borrowed Sand, Cryo Pod). Rage per round: Ignition 20, Pressure Valve 30, Barge refunds ≤ 20, the bank doubles at
  most one rage attack per round. Timer advances once per round per timer; one extra mount shot per side per round; Mirror
  Palm's copies capped 20 / 30 per enemy. Crowd control: only the Chrono Skiff's single-target Delay (bosses lose rage instead);
  `ContentValidationTests` allowlists the five Time Debt steps.

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
  choice draws from all Mythics of the unlocked synergies plus the generic ones (owned synergies weighted) and the researched
  Laboratory Mythics (Source Laboratory, in `CombatLoadout.DraftPoolSkillIds` like lab skills; one-level nodes, `LabMythic`).
- **The owner's four Mythics (2026-11-26):** **Horrifying Presence** (generic): every enemy is stunned in round 1 (a battle-start
  Stun, so status resist and the Stun re-apply cooldown apply). **Beast** (generic): +30 counter, combo and crit chance; the
  one Mythic allowed to be a stat package (owner). **Shatter** (lab, Ice, `Needs("freeze")`): each hit of the worm (OnHit) or of
  one of its aliens (OnSidekickHit, once per action) on a frozen enemy gives +10% ATK for the battle (`stack:15`: max +150%,
  no decay; reset every battle, owner). **Plague** (lab, generic): run-scoped. While the run owns it, the battle counts the
  stacks the player side adds to enemies per status (`BattleResult.StacksInflicted`, Plague's own opening stacks excluded);
  the run adds them up (`ActiveRun.PlagueStacks`), and every 20 of a status give every enemy of each later battle one opening
  stack of it (`RunSystem.PlagueOpening` → `CombatantSpec.OpeningStatuses`, applied before battle-start triggers).
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
