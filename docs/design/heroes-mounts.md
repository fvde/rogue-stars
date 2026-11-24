# Heroes and mounts (generated)

The 9 hero kits and 9 mount kits as the screens show them (`HeroDef.Kit`, `MountDef.Kit`), generated from `ContentDb.Default` by
`KitDocsGenerator` (`ROGUESTARS_WRITE_DOCS=1 dotnet test shared/RogueStars.Rules.Tests --filter KitDocsGenerator`). Design: the owner-approved
Hero and Mount Mechanics page (rev 2); engine rules in [skill-conventions.md](skill-conventions.md) ("Hero and mount kits").

- **Heroes:** 1★ Trait + Ability, 3★ a number, 5★ / 7★ clauses, 10★ capstone. Not tied to a synergy. Hero level and stars only raise
  worm stats, one shared budget for every hero (HP 100 / ATK 10 +5% at 1★, ×1 … ×2.85 by star, +10 HP / +2 ATK per level).
- **Mounts:** a vehicle on a reload, no stats: one passive + one active (a shot every N rounds, from round 1), texts at 1★ / 3★ /
  5★ / A3 / A5. Damage = % of the worm's ATK × (1 + mount damage %): +5% at each step between (2★, 4★, A1, A2, A4), +1% per Fuel level, + the collection (+1% per star / awakening of every owned mount) and lab Hangar Tuning.
  Rider link: the mount's statuses are the worm's and its crits fire the worm's on-crit skills (base crit damage: no stats).

## Heroes

### Star Knight (`hero_star_knight`)

Guardian: a shield from the first second; every 4th hit it absorbs calls down Starfall. *Counter: hits on your shields.*

| Star | Slot | Name | Text | Skill |
|---|---|---|---|---|
| 1★ | Trait | Starguard | Battle start: shield of 12% max HP. Each hit your shield absorbs gives 5 rage. | `hero_star_knight_trait` |
| 1★ | Ability | Starfall | Every 4th hit your shields absorb: Starfall hits every enemy for 50% ATK and shields you 6% max HP. | `hero_star_knight_ability` |
| 3★ | Number | Starfall | Starfall: 50% → 60% ATK. | `hero_star_knight_ability_3` |
| 5★ | Clause | Starlight | Starfall carries each of your signatures' on-hit to every enemy it hits. | `hero_star_knight_c5` |
| 7★ | Clause | Guiding Star | Healing counts too: every 10% max HP you heal counts as one absorbed hit. | `hero_star_knight_c7` |
| 10★ | Capstone | Last Constellation | Once per battle, a fatal hit leaves you at 1 HP and fires Starfall at double damage. | `hero_star_knight_c10` |

### Beast Tamer (`hero_beast_tamer`)

Pack leader: sidekicks and the mount buff the worm, and a whistle sends everyone at once. *Timer: every 4th round.*

| Star | Slot | Name | Text | Skill |
|---|---|---|---|---|
| 1★ | Trait | Pack Spirit | Each time a sidekick acts or your mount fires: +1% ATK this battle (max +15%). | `hero_beast_tamer_trait` |
| 1★ | Ability | Whistle | Every 4th round: every sidekick acts, fastest first, then your mount fires. | `hero_beast_tamer_ability` |
| 3★ | Number | Whistle | Whistle: every 4th → 3rd round. | `hero_beast_tamer_ability_3` |
| 5★ | Clause | Tamed | Sidekick hits apply your signatures' on-hit (once per action). | `hero_beast_tamer_c5` |
| 7★ | Clause | Pack Guard | Whistle shields the worm for 3% max HP per ally that acted. | `hero_beast_tamer_ability_7` |
| 10★ | Capstone | Alpha Howl | During Whistle, the worm follows each ally with a 20% ATK extra hit. | `hero_beast_tamer_ability_10` |

### Chef (`hero_chef`)

Status gourmet: heals whenever enemies lose stacks, and eats the biggest status on a timer. *Timer: every 4th round.*

| Star | Slot | Name | Text | Skill |
|---|---|---|---|---|
| 1★ | Trait | Taste Test | When an enemy loses status stacks, heal 1% max HP per stack (max 8% per round). | `hero_chef_trait` |
| 1★ | Ability | Feast | Every 4th round: eat up to 6 stacks of the front enemy's largest status, 25% ATK per stack. | `hero_chef_ability` |
| 3★ | Number | Feast | Feast: every 4th → 3rd round. | `hero_chef_ability_3` |
| 5★ | Clause | Seasoning | After a Feast, your next hit applies your signatures' on-hit three times. | `hero_chef_c5` |
| 7★ | Clause | Second Course | Feast also eats up to 3 stacks from the enemy behind. | `hero_chef_c7` |
| 10★ | Capstone | Banquet | A full Feast (6 stacks) becomes a Banquet: every enemy takes half its damage. | `hero_chef_ability_10` |

### Hourglass Hermit (`hero_hourglass_hermit`)

Time keeper: kills wind the clock forward, and the hourglass rewinds the last hits taken. *Timer: every 4th round.*

| Star | Slot | Name | Text | Skill |
|---|---|---|---|---|
| 1★ | Trait | Patience | Each kill brings your Ability and your mount's next shot 1 round sooner (once per round). | `hero_hourglass_hermit_trait` |
| 1★ | Ability | Rewind | Every 4th round: undo the damage of the last 2 hits you took. | `hero_hourglass_hermit_ability` |
| 3★ | Number | Rewind | Rewind: 2 → 3 hits. | `hero_hourglass_hermit_ability_3` |
| 5★ | Clause | Turning Sand | After a Rewind, your next hit applies your signatures' on-hit twice. | `hero_hourglass_hermit_c5` |
| 7★ | Clause | Borrowed Sand | Once per battle, a fatal hit is undone. | `hero_hourglass_hermit_c7` |
| 10★ | Capstone | Hour of Need | Each hit Rewind undoes strikes its attacker back for 50% ATK. | `hero_hourglass_hermit_ability_10` |

### Nebula Monk (`hero_glacier_monk`)

Calm centre: every attack on you builds Stillness; Mirror Palm spreads the biggest status down the line. *Counter: attacks on you.*

| Star | Slot | Name | Text | Skill |
|---|---|---|---|---|
| 1★ | Trait | Stillness | Each enemy attack on you gives 1 Stillness, even dodged or blocked ones (max 5). | `hero_glacier_monk_trait` |
| 1★ | Ability | Mirror Palm | At 5 Stillness: strike the front enemy; every other enemy gets half its largest status (max 20 stacks). | `hero_glacier_monk_ability` |
| 3★ | Number | Mirror Palm | Mirror Palm: max 20 → 30 stacks. | `hero_glacier_monk_ability_3` |
| 5★ | Clause | Open Palm | Mirror Palm also applies your signatures' on-hit to every enemy. | `hero_glacier_monk_c5` |
| 7★ | Clause | Inner Calm | Mirror Palm heals 10% max HP. | `hero_glacier_monk_c7` |
| 10★ | Capstone | Nebula Mind | Mirror Palm also hits every enemy for 8% ATK per stack it copied (max 200%). | `hero_glacier_monk_ability_10` |

### Prism Alchemist (`hero_alchemist`)

Status mixer: rewards every different colour on an enemy, throws flasks of what you own, deepens the wells. *Timer: every 3rd round.*

| Star | Slot | Name | Text | Skill |
|---|---|---|---|---|
| 1★ | Trait | Prism | Your hits deal +5% damage per different status on the target. | `hero_alchemist_trait` |
| 1★ | Ability | Flask | Every 3rd round: a flask hits the front enemy for 80% ATK and 2 stacks of a random owned status. | `hero_alchemist_ability` |
| 3★ | Number | Flask | Flask: 2 → 3 stacks. | `hero_alchemist_ability_3` |
| 5★ | Clause | Volatile Flask | Flasks also trigger your signatures' on-hit. | `hero_alchemist_c5` |
| 7★ | Clause | Conduit | Your highest element damage bonus also counts for your other elements, at half. | `hero_alchemist_c7` |
| 10★ | Capstone | Deep Wells | Bleed, Chill and Shock can stack twice as high on enemies (20, 20, 10). | `hero_alchemist_c10` |

### Shadow Noodle (`hero_noodle_ninja`)

Extra-hit engine: every extra hit doubles its statuses, and a knot turn makes extra hits from nothing. *Timer: every 3rd turn.*

| Star | Slot | Name | Text | Skill |
|---|---|---|---|---|
| 1★ | Trait | Double Tap | Your extra hits (combo strikes, counters, Echoes) apply their statuses twice. | `hero_noodle_ninja_trait` |
| 1★ | Ability | Noodle Knot | Every 3rd turn: +30% combo chance and +30% counter chance until your next turn. | `hero_noodle_ninja_ability` |
| 3★ | Number | Noodle Knot | Noodle Knot: +30% → +40% combo and counter chance. | `hero_noodle_ninja_ability_3` |
| 5★ | Clause | Sharp Noodle | Your extra hits get +15% crit chance. | `hero_noodle_ninja_c5` |
| 7★ | Clause | Pile-On | Each hit on the same enemy in one turn deals +6% more than the last (max +48%). | `hero_noodle_ninja_c7` |
| 10★ | Capstone | Thousand Noodles | Once per battle, a turn with 5+ extra hits ends with a strike on every enemy for 150% ATK. | `hero_noodle_ninja_c10` |

### Sparkplug (`hero_sparkplug`)

Living ignition: hits and extra hits make rage; overflow rage doubles the next rage attack. *Bank: rage above 100.*

| Star | Slot | Name | Text | Skill |
|---|---|---|---|---|
| 1★ | Trait | Ignition | Gain 5 rage when hit and for each extra hit you land (max 20 per round). | `hero_sparkplug_trait` |
| 1★ | Ability | Overcharge | Rage past 100 is banked (max 100). A rage attack with a full bank fires twice. | `hero_sparkplug_ability` |
| 3★ | Number | Ignition | Ignition: 5 → 8 rage. | `hero_sparkplug_trait_3` |
| 5★ | Clause | Spark Plug | Your rage attacks apply your signatures' on-hit to every enemy. | `hero_sparkplug_c5` |
| 7★ | Clause | Short Circuit | Each rage attack makes your mount fire now (once per round). | `hero_sparkplug_c7` |
| 10★ | Capstone | Live Wire | Below 30% HP: hits on you give 20 rage, and rage attacks heal 10% max HP. | `hero_sparkplug_c10` |

### Void Pirate (`hero_void_pirate`)

Crit raider: status damage crits, an always-crit broadside, crits carry the signature, crit kills pay coins. *Timer: every 4th round.*

| Star | Slot | Name | Text | Skill |
|---|---|---|---|---|
| 1★ | Trait | Dead Reckoning | Your status damage can crit: ticks, Supernovas, consumes and discharges. | `hero_void_pirate_trait` |
| 1★ | Ability | Broadside | Every 4th round: Broadside hits every enemy for 40% ATK. These hits always crit. | `hero_void_pirate_ability` |
| 3★ | Number | Broadside | Broadside: 40% → 50% ATK. | `hero_void_pirate_ability_3` |
| 5★ | Clause | Black Flag | Your crits apply your signatures' on-hit once more. | `hero_void_pirate_c5` |
| 7★ | Clause | Plunder | Crit kills pay 2 run coins (max 20 per battle). | `hero_void_pirate_c7` |
| 10★ | Capstone | Void Galleon | Broadside also fires at the start of every battle. | `hero_void_pirate_c10` |

## Mounts

### Scout Saucer (`mount_scout_saucer`)

Spotter: marks one enemy, feeds it a status you own, and passes its stacks on when it dies. Reload 3 / 3 / 3 / 2 / 2 (1★ / 3★ / 5★ / A3 / A5).

| Step | Passive | Active |
|---|---|---|
| 1★ | **Spotlight:** Marks the healthiest enemy. Your hits on the mark get +10% crit chance. | **Scan Pulse:** Every 3 rounds: zap the mark for 80% ATK and 1 stack of a random status you own. |
| 3★ | **Spotlight:** Marks the healthiest enemy. Your hits on the mark get +15% crit chance. | **Scan Pulse:** Every 3 rounds: zap the mark for 100% ATK and 2 stacks of a random status you own. |
| 5★ | **Spotlight:** Mark: +20% crit chance. When the mark dies, half its statuses jump to the next mark. | **Scan Pulse:** Every 3 rounds: 120% ATK on the mark, 3 stacks of a random owned status. Always crits. |
| A3 | **Spotlight:** Mark: +25% crit chance. When the mark dies, half its statuses jump to the next mark. | **Scan Pulse:** Every 2 rounds: 140% ATK on the mark, 4 stacks of a random owned status. Always crits. |
| A5 | **Spotlight:** Mark: +30% crit chance. When the mark dies, all its statuses jump to the next mark. | **Scan Pulse:** Every 2 rounds: 160% ATK, 5 stacks of a random owned status, always crits; others take 40%. |

### Aegis Walker (`mount_aegis_walker`)

Payback walker: banks the damage the worm takes, then fires it back. Reload 3 / 3 / 3 / 2 / 2 (1★ / 3★ / 5★ / A3 / A5).

| Step | Passive | Active |
|---|---|---|
| 1★ | **Payback Coil:** Stores 20% of the damage the worm takes, shielded hits included (max 300% ATK). | **Payback Cannon:** Every 3 rounds: hit the last attacker for 60% ATK plus all stored damage. Empties the coil. |
| 3★ | **Payback Coil:** Stores 25% of the damage the worm takes, shielded hits included (max 300% ATK). | **Payback Cannon:** Every 3 rounds: last attacker, 80% ATK plus stored damage. Empties the coil. |
| 5★ | **Payback Coil:** Stores 30% (max 400% ATK). Supernova recoil counts double; counterattacks store half their damage. | **Payback Cannon:** Every 3 rounds: 100% ATK + stored damage on the last attacker, with your signatures' on-hit twice. |
| A3 | **Payback Coil:** Stores 35% (max 450% ATK). Recoil counts double; counterattacks store half their damage. | **Payback Cannon:** Every 2 rounds: 100% ATK + stored damage, signatures' on-hit twice. |
| A5 | **Payback Coil:** Stores 40% (max 500% ATK). A full coil makes the worm counter every hit until its next turn. | **Payback Cannon:** Every 2 rounds: 100% ATK on every enemy that hit the worm since the last shot, sharing the stored damage. |

### Chrono Skiff (`mount_chrono_skiff`)

Time thief: steals one enemy turn at the start, then makes the worm repeat its last attack. Reload 4 / 4 / 3 / 3 / 3 (1★ / 3★ / 5★ / A3 / A5).

| Step | Passive | Active |
|---|---|---|
| 1★ | **Time Debt:** Battle start: Delay the fastest non-boss enemy once. Bosses lose 20 rage instead. | **Echo Shot:** Every 4 rounds: the worm repeats its last attack at 60%. |
| 3★ | **Time Debt:** Battle start: Delay the fastest non-boss enemy once. Bosses lose 25 rage instead. | **Echo Shot:** Every 4 rounds: the worm repeats its last attack at 70%. |
| 5★ | **Time Debt:** Battle start Delay (bosses −30 rage). Each enemy turn you skip gives the worm 15 rage. | **Echo Shot:** Every 3 rounds: the worm repeats its last attack at 80%. It counts as an Echo. |
| A3 | **Time Debt:** Battle start Delay (bosses −35 rage). Each skipped enemy turn gives the worm 20 rage. | **Echo Shot:** Every 3 rounds: the worm repeats its last attack at 90%. It counts as an Echo. |
| A5 | **Time Debt:** Bosses −40 rage, skips give 25 rage. Once per battle below 50% HP: Delay the attacker. | **Echo Shot:** Every 3 rounds: the worm repeats its last attack twice, at 100%. |

### Frost Frigate (`mount_frost_frigate`)

Cryo bay: coolant shields every round, a cryo pod against death, ice walls that burst. Reload 3 / 3 / 3 / 2 / 2 (1★ / 3★ / 5★ / A3 / A5).

| Step | Passive | Active |
|---|---|---|
| 1★ | **Coolant Loop:** Each round: remove 1 status stack from the worm and gain a shield of 2% max HP. | **Ice Wall:** Every 3 rounds: 60% ATK to the front enemy and a shield of 6% max HP. |
| 3★ | **Coolant Loop:** Each round: remove 1 status stack from the worm; shield of 3% max HP. | **Ice Wall:** Every 3 rounds: 70% ATK and a shield of 8% max HP. |
| 5★ | **Coolant Loop:** Shield 4%. Cryo pod: once per battle, a fatal hit skips your next turn instead and heals 25%. | **Ice Wall:** Every 3 rounds: 80% ATK, shield 10%. When this shield breaks, it bursts for 100% ATK. |
| A3 | **Coolant Loop:** Shield 5% per round. Cryo pod once per battle: skip a turn, heal 30%. | **Ice Wall:** Every 2 rounds: 90% ATK, shield 12%, bursts for 125% ATK when broken. |
| A5 | **Coolant Loop:** Shield 6% per round. Cryo pod heals 40%; removed stacks go onto the front enemy. | **Ice Wall:** Every 2 rounds: 100% ATK, shield 15%; a 150% burst hits every enemy with your signatures' on-hit. |

### Hive Carrier (`mount_hive_carrier`)

Drone deck: drones follow every extra hit and guard the worm. Reload 3 / 3 / 3 / 2 / 2 (1★ / 3★ / 5★ / A3 / A5).

| Step | Passive | Active |
|---|---|---|
| 1★ | **Wingman:** Once per turn, when the worm lands an extra hit (combo, counter, Echo), a drone strikes for 30% ATK. | **Launch Bay:** Every 3 rounds (from round 1): launch a Drone sidekick that zaps for 20% ATK. Max 2. |
| 3★ | **Wingman:** Once per turn, when the worm lands an extra hit, a drone strikes for 35% ATK. | **Launch Bay:** Every 3 rounds: launch a Drone (zaps for 25% ATK). Max 2. |
| 5★ | **Wingman:** Once per turn, an extra hit brings a 40% ATK drone strike with your signatures' on-hit. | **Launch Bay:** Every 3 rounds: launch a Drone (30% ATK), max 3. A Drone can guard one hit (−50%), then breaks. |
| A3 | **Wingman:** Twice per turn: extra hits bring a 45% ATK drone strike with your signatures' on-hit. | **Launch Bay:** Every 2 rounds: launch a Drone (35% ATK), max 3. Drones guard one hit, then break. |
| A5 | **Wingman:** Twice per turn, 50% ATK with your on-hit; each strike gives every sidekick 10 charge. | **Launch Bay:** Every 2 rounds, 40% ATK, max 3, guard; Drones trigger your signatures when they act. |

### Magma Barge (`mount_magma_barge`)

Pressure boiler: every status stack builds steam (rage); the bomb can pop a free rage attack. Reload 3 / 3 / 3 / 2 / 2 (1★ / 3★ / 5★ / A3 / A5).

| Step | Passive | Active |
|---|---|---|
| 1★ | **Pressure Valve:** Every 10 status stacks the worm applies: +10 rage. | **Eruption:** Every 3 rounds: magma bomb on the front enemy, 120% ATK. |
| 3★ | **Pressure Valve:** Every 10 status stacks the worm applies: +15 rage. | **Eruption:** Every 3 rounds: magma bomb on the front enemy, 140% ATK. |
| 5★ | **Pressure Valve:** Every 10 stacks: +20 rage. Each rage attack refunds 10 rage. | **Eruption:** Every 3 rounds: bomb for 160% ATK. If the worm has 70+ rage, it fills to full. |
| A3 | **Pressure Valve:** Every 10 stacks: +25 rage. Each rage attack refunds 15 rage. | **Eruption:** Every 2 rounds: bomb for 180% ATK. At 60+ rage, the worm's rage fills. |
| A5 | **Pressure Valve:** Every 10 stacks: +30 rage. Each rage attack refunds 20 rage. | **Eruption:** Every 2 rounds: 200% ATK. At full rage, the worm makes a free rage attack now. |

### Rift Racer (`mount_rift_racer`)

Tempo racer: the worm opens every battle, dashes for extra attacks, and kills speed up every clock. Reload 3 / 3 / 3 / 2 / 2 (1★ / 3★ / 5★ / A3 / A5).

| Step | Passive | Active |
|---|---|---|
| 1★ | **Slipstream:** Round 1: the worm acts before every enemy. | **Rift Dash:** Every 3 rounds: the worm makes an extra attack at 60% damage. |
| 3★ | **Slipstream:** Round 1: the worm acts first, and its first attack deals +30%. | **Rift Dash:** Every 3 rounds: the worm makes an extra attack at 70% damage. |
| 5★ | **Slipstream:** Acts first in round 1 (+40%). Each kill brings the Racer's next dash 1 round closer. | **Rift Dash:** Every 3 rounds: the worm makes an extra attack at 80%. It can combo. |
| A3 | **Slipstream:** Acts first in round 1 (+50%). Each kill brings the next dash 1 round closer. | **Rift Dash:** Every 2 rounds: the worm makes an extra attack at 90%. It can combo. |
| A5 | **Slipstream:** First in round 1 (+60%). Each kill advances every timer you own by 1 round. | **Rift Dash:** Every 2 rounds: the worm makes two extra attacks at 70%. They can combo. |

### Siphon Hauler (`mount_toxic_hauler`)

Siphon tanker: drinks a share of all status damage back into the worm. Reload 3 / 3 / 3 / 2 / 2 (1★ / 3★ / 5★ / A3 / A5).

| Step | Passive | Active |
|---|---|---|
| 1★ | **Siphon Line:** The worm heals 5% of the damage its statuses deal (ticks, Supernovas, consumes, discharges). | **Drain Hose:** Every 3 rounds: 60% ATK to the front enemy, then its Radiation and Bleed tick once now. |
| 3★ | **Siphon Line:** The worm heals 8% of the damage its statuses deal. | **Drain Hose:** Every 3 rounds: 80% ATK to the front enemy, then its Radiation and Bleed tick once now. |
| 5★ | **Siphon Line:** Heals 10% of status damage. Healing past full HP becomes a shield (max 10% max HP). | **Drain Hose:** Every 3 rounds: 100% ATK, then its Radiation and Bleed tick once now, at 150%. |
| A3 | **Siphon Line:** Heals 12% of status damage. Overheal becomes a shield (max 15% max HP). | **Drain Hose:** Every 2 rounds: 120% ATK, then its Radiation and Bleed tick once now, at 175%. |
| A5 | **Siphon Line:** Heals 15%; overheal shield (max 20%). While that shield is full, Drain Hose hits every enemy. | **Drain Hose:** Every 2 rounds: 140% ATK, ticks at 200%. With a full siphon shield, it hits every enemy. |

### Star Lancer (`mount_star_lancer`)

Crit hunter: worm crits charge the lance; Impale always crits and pierces the line. Reload 3 / 3 / 3 / 3 / 3 (1★ / 3★ / 5★ / A3 / A5).

| Step | Passive | Active |
|---|---|---|
| 1★ | **Lance Sight:** Each worm crit charges the lance: the next Impale deals +15% per crit (max +30%). | **Impale:** Every 3 rounds: lance the front enemy, 120% ATK. This hit always crits. |
| 3★ | **Lance Sight:** Each worm crit charges the lance: the next Impale deals +20% per crit (max +40%). | **Impale:** Every 3 rounds: lance the front enemy, 125% ATK. Always crits. |
| 5★ | **Lance Sight:** Each crit charges +25% (max +50%). Crits on enemies below 30% HP charge double. | **Impale:** Every 3 rounds: 130% ATK, always crits, and pierces the enemy behind for half. |
| A3 | **Lance Sight:** Each crit charges +30% (max +60%). Crits on enemies below 30% HP charge double. | **Impale:** Every 3 rounds: 140% ATK, always crits, pierces the enemy behind for half. |
| A5 | **Lance Sight:** Each crit charges +35% (max +70%). An Impale kill reloads the Lancer at once. | **Impale:** Every 3 rounds: 140% ATK, always crits, pierces the whole line for half. |

