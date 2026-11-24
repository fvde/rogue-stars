# Run skill levels (generated)

Every signature, draftable run and laboratory skill (each research level of a lab node is its own skill) with its Lv1 text and
each in-run upgrade as the cards show it (`SkillText.Full` / `SkillText.UpgradeFull`). Generated from `ContentDb.Default` by
`SkillDocsGenerator` (`ROGUESTARS_WRITE_DOCS=1 dotnet test shared/RogueStars.Rules.Tests --filter SkillDocsGenerator`);
rules in docs/design/skill-conventions.md.

Level up: its numbers climb +15% → +25% → +40% (Legendary +25% → +40%); chances stop at 100%, then stacks or duration grow.

## Signatures

One per synergy; the run starts with one (1 of 3 offered). Never drafted, bought or levelled by drafts: it awakens at 3 / 6 owned skills of its synergy.

| Signature | Id | Synergy | Unlock | Text | Awakened |
|---|---|---|---|---|---|
| Lacerate | `sig_bleed` | Bleed | start | On hit: apply Bleed to the enemy. | On hit: apply Bleed ×2 to the enemy. On hit: apply Bleed ×3 to the enemy. |
| Ignite | `sig_fire` | Fire | start | On hit: apply Heat to the enemy. | On hit: apply Heat ×2 to the enemy. On hit: apply Heat ×3 to the enemy. |
| Irradiate | `sig_radiation` | Radiation | start | On hit: apply Radiation to the enemy. | On hit: apply Radiation ×2 to the enemy. On hit: apply Radiation ×3 to the enemy. |
| Command | `sig_alien` | Alien | Alien Package | Each round: one of your sidekicks takes an extra action. | Each round: 2 extra sidekick actions. Each round: 3 extra sidekick actions. |
| Flurry | `sig_combo` | Combo | Combo Package | On hit: 30% chance to strike again. | On hit: 45% chance to strike again. On hit: 60% chance to strike again. |
| Riposte | `sig_counter` | Counter | Counter Package | When hit: 30% chance to counterattack. | When hit: 45% chance to counterattack. When hit: 60% chance to counterattack. |
| Chill | `sig_ice` | Ice | Ice Package | On hit: apply Chill to the enemy. | On hit: apply Chill ×2 to the enemy. On hit: apply Chill ×3 to the enemy. |
| Arc | `sig_lightning` | Lightning | Lightning Package | On hit: Arc to 1 other enemy for 50% ATK damage. | On hit: Arc to 2 other enemies for 50% ATK damage. On hit: Arc to 3 other enemies for 50% ATK damage. |
| Echo | `sig_time` | Time | Time Package | Every 3rd turn: repeat your last attack. | Every 3rd turn: repeat your last attack ×2. Every 3rd turn: repeat your last attack ×3. |

## Draftable skills

| Skill | Id | Tier | Source | Lv | Text (upgrade inline) |
|---|---|---|---|---|---|
| Ambush Instinct | `gen_ambush` | Common | Run | 1 | Landing on an enemy tile: start that battle with 15 rage. |
| | | | | 2 | Landing on an enemy tile: start that battle with 15 → 25 rage. |
| | | | | 3 | Landing on an enemy tile: start that battle with 25 → 40 rage. |
| Brutal Force | `crit_brutal` | Common | Run | 1 | +24% crit damage. |
| | | | | 2 | +24% → +40% crit damage. |
| | | | | 3 | +40% → +64% crit damage. |
| Calm Mind | `gen_calm_mind` | Common | Run | 1 | Run start: the boss awakens 9% slower. |
| | | | | 2 | Run start: the boss awakens 9% → 15% slower. |
| | | | | 3 | Run start: the boss awakens 15% → 24% slower. |
| Camper | `gen_camper` | Common | Run | 1 | Landing on a campfire: heal 15% of max HP. |
| | | | | 2 | Landing on a campfire: heal 15% → 25% of max HP. |
| | | | | 3 | Landing on a campfire: heal 25% → 40% of max HP. |
| Giant Slayer | `gen_giant_slayer` | Common | Run | 1 | +18% damage against enemies above 70% HP. |
| | | | | 2 | +18% → +30% damage against enemies above 70% HP. |
| | | | | 3 | +30% → +48% damage against enemies above 70% HP. |
| Guard | `gen_guard` | Common | Run | 1 | +15% DEF. |
| | | | | 2 | +15% → +25% DEF. |
| | | | | 3 | +25% → +40% DEF. |
| Keen Eye | `crit_keen` | Common | Run | 1 | +15% crit chance. |
| | | | | 2 | +15% → +25% crit chance. |
| | | | | 3 | +25% → +40% crit chance. |
| Pathfinder | `gen_pathfinder` | Common | Run | 1 | Each tile you pass: gain 3 coins. |
| | | | | 2 | Each tile you pass: gain 3 → 5 coins. |
| | | | | 3 | Each tile you pass: gain 5 → 8 coins. |
| Power | `gen_power` | Common | Run | 1 | +15% ATK. |
| | | | | 2 | +15% → +25% ATK. |
| | | | | 3 | +25% → +40% ATK. |
| Prospector | `gen_wealth` | Common | Run | 1 | After each won battle: gain 12 coins. |
| | | | | 2 | After each won battle: gain 12 → 20 coins. |
| | | | | 3 | After each won battle: gain 20 → 32 coins. |
| Regrowth | `gen_regen` | Common | Run | 1 | +2% regeneration. |
| | | | | 2 | +2% → +3.5% regeneration. |
| | | | | 3 | +3.5% → +5.5% regeneration. |
| Scholar | `gen_scholar` | Common | Run | 1 | +18% run XP. |
| | | | | 2 | +18% → +30% run XP. |
| | | | | 3 | +30% → +48% run XP. |
| Shield Breaker | `gen_shield_breaker` | Common | Run | 1 | +30% damage against shielded enemies. |
| | | | | 2 | +30% → +50% damage against shielded enemies. |
| | | | | 3 | +50% → +80% damage against shielded enemies. |
| Swift | `gen_swift` | Common | Run | 1 | +12 speed. +6% dodge. |
| | | | | 2 | +12 → +20 speed. +6% → +10% dodge. |
| | | | | 3 | +20 → +32 speed. +10% → +16% dodge. |
| Vampiric | `gen_vampire` | Common | Run | 1 | +6% life steal. |
| | | | | 2 | +6% → +10% life steal. |
| | | | | 3 | +10% → +16% life steal. |
| Vitality | `gen_vitality` | Common | Run | 1 | +15% HP. |
| | | | | 2 | +15% → +25% HP. |
| | | | | 3 | +25% → +40% HP. |
| Cull | `gen_cull` | Legendary | Run | 1 | Hitting a non-boss enemy below 15% HP kills it outright. |
| | | | | 2 | Hitting a non-boss enemy below 15% → 24% HP kills it outright. |
| Growth Spurt | `gen_growth` | Legendary | Run | 1 | Each run level up: +2% ATK and +2% HP for the rest of the run. |
| | | | | 2 | Each run level up: +2% → +3% ATK and +2% → +3% HP for the rest of the run. |
| Overclocked Core | `gen_overclocked` | Legendary | Run | 1 | +25% ATK. -15% max HP. |
| | | | | 2 | +25% → +40% ATK. -15% max HP. |
| Catalyst | `mythic_catalyst` | Mythic | Run | 1 | Enemies with 3+ different statuses gain 1 stack of each whenever you hit them. |
| Last Light | `mythic_last_light` | Mythic | Run | 1 | Below 30% HP: every hit crits,. but you can't heal. |
| Overkill | `mythic_overkill` | Mythic | Run | 1 | Damage beyond an enemy's HP carries over to the next enemy. |
| Ember Strikes | `fire_ember` | Common | Run · Fire | 1 | On crit: 30% chance to apply Heat ×3. |
| | | | | 2 | On crit: 30% → 50% chance to apply Heat ×3. |
| | | | | 3 | On crit: 50% → 80% chance to apply Heat ×3. |
| Kindling Heart | `fire_kindle` | Common | Run · Fire | 1 | +1.5% damage per Heat stack on the target. |
| | | | | 2 | +1.5% → +2.5% damage per Heat stack on the target. |
| | | | | 3 | +2.5% → +4% damage per Heat stack on the target. |
| Thermal Plating | `fire_plating` | Common | Run · Fire | 1 | When a heated enemy hits you: gain a shield of 3% max HP. |
| | | | | 2 | When a heated enemy hits you: gain a shield of 3% → 5% max HP. |
| | | | | 3 | When a heated enemy hits you: gain a shield of 5% → 8% max HP. |
| Controlled Burn | `fire_controlled` | Legendary | Run · Fire | 1 | Supernova needs 12 Heat, but never hurts you and deals +25% damage. |
| | | | | 2 | Supernova needs 12 Heat, but never hurts you and deals +25% → +40% damage. |
| Flashpoint | `fire_flashpoint` | Legendary | Run · Fire | 1 | Rage attack on an enemy with 6+ Heat: it goes Supernova now, +25% damage. |
| | | | | 2 | Rage attack on an enemy with 6+ Heat: it goes Supernova now, +25% → +40% damage. |
| Chain Reaction | `mythic_chain_reaction` | Mythic | Run · Fire | 1 | Supernova leaves 5 Heat on every enemy it hits (once per round). Each blast still singes you. |
| Heat Siphon | `fire_heat_siphon` | Mythic | Run · Fire | 1 | Your rage attack drains the target's Heat: +4% ATK per stack for the battle (max +60%). |
| Thermal Shock | `mythic_thermal_shock` | Mythic | Run · Fire | 1 | Hitting a heated enemy stuns it (bosses: stagger). It can't gain Heat for 3 turns. |
| Brittle | `ice_brittle` | Common | Run · Ice | 1 | Enemies take +1.5% damage per Chill stack. |
| | | | | 2 | Enemies take +1.5% → +2.5% damage per Chill stack. |
| | | | | 3 | Enemies take +2.5% → +4% damage per Chill stack. |
| Frost Armor | `ice_armor` | Common | Run · Ice | 1 | Battle start: gain a shield of 6% max HP. Hits on your shield chill the attacker. |
| | | | | 2 | Battle start: gain a shield of 6% → 10% max HP. Hits on your shield chill the attacker. |
| | | | | 3 | Battle start: gain a shield of 10% → 16% max HP. Hits on your shield chill the attacker. |
| Ice Shard | `ice_shard` | Common | Run · Ice | 1 | Hitting a chilled enemy: 30% chance to fire a shard for 50% ATK. |
| | | | | 2 | Hitting a chilled enemy: 30% → 50% chance to fire a shard for 50% ATK. |
| | | | | 3 | Hitting a chilled enemy: 50% → 80% chance to fire a shard for 50% ATK. |
| Cold Snap | `ice_cold_snap` | Legendary | Run · Ice | 1 | When you Freeze an enemy, every other enemy gains Chill ×3. |
| | | | | 2 | When you Freeze an enemy, every other enemy gains Chill ×3 → ×5. |
| Permafrost | `ice_permafrost` | Legendary | Run · Ice | 1 | Chill no longer wears off on enemies with 5+ stacks. +25% ice damage. |
| | | | | 2 | Chill no longer wears off on enemies with 5+ stacks. +25% → +40% ice damage. |
| Cold Fusion | `mythic_cold_fusion` | Mythic | Run · Ice | 1 | Chill stops cooling Heat. Enemies with both Heat and Chill take +40% damage from you. |
| Shatter | `mythic_shatter` | Mythic | Run · Ice | 1 | Hitting a frozen enemy shatters the ice: it thaws, and all other enemies take 150% ATK. |
| Winter's Grip | `mythic_winters_grip` | Mythic | Run · Ice | 1 | Chilled enemies can't crit, combo or counter. |
| Conductor Coil | `light_conductor` | Common | Run · Lightning | 1 | +15% Arc damage. |
| | | | | 2 | +15% → +25% Arc damage. |
| | | | | 3 | +25% → +40% Arc damage. |
| Overclock | `light_overclock` | Common | Run · Lightning | 1 | Hitting an enemy at max Shock: 30% chance to Arc one extra time. |
| | | | | 2 | Hitting an enemy at max Shock: 30% → 50% chance to Arc one extra time. |
| | | | | 3 | Hitting an enemy at max Shock: 50% → 80% chance to Arc one extra time. |
| Spark | `light_spark` | Common | Run · Lightning | 1 | Arc jumps: 30% chance to apply Shock. |
| | | | | 2 | Arc jumps: 30% → 50% chance to apply Shock. |
| | | | | 3 | Arc jumps: 50% → 80% chance to apply Shock. |
| Ground Loop | `light_ground_loop` | Legendary | Run · Lightning | 1 | Last enemy standing: your Arc jumps strike it instead, with +50% damage. |
| | | | | 2 | Last enemy standing: your Arc jumps strike it instead, with +50% → +80% damage. |
| Thunderclap | `light_thunderclap` | Legendary | Run · Lightning | 1 | Every 8th Arc jump: all enemies gain Shock ×2. Your Arcs deal +25% to enemies at max Shock. |
| | | | | 2 | Every 8th Arc jump: all enemies gain Shock ×2. Your Arcs deal +25% → +40% to enemies at max Shock. |
| Capacitor | `mythic_capacitor` | Mythic | Run · Lightning | 1 | Shock no longer expires. At 5 stacks it discharges for 150% ATK and resets. |
| Superconductor | `mythic_superconductor` | Mythic | Run · Lightning | 1 | Arcs jump to chilled or frozen enemies first. Shock on them counts double. |
| Thunderhead | `mythic_thunderhead` | Mythic | Run · Lightning | 1 | Your basic attack hits every enemy, but at 60% damage. |
| Geiger Counter | `poison_geiger` | Common | Run · Radiation | 1 | Every 10th Radiation stack you apply: deal 60% ATK to that enemy. |
| | | | | 2 | Every 10th Radiation stack you apply: deal 60% → 100% ATK to that enemy. |
| | | | | 3 | Every 10th Radiation stack you apply: deal 100% → 160% ATK to that enemy. |
| Hot Spit | `poison_spit` | Common | Run · Radiation | 1 | When hit: apply Radiation ×2 to the attacker. |
| | | | | 2 | When hit: apply Radiation ×2 → ×3 to the attacker. |
| | | | | 3 | When hit: apply Radiation ×3 → ×5 to the attacker. |
| Reactor Glands | `poison_glands` | Common | Run · Radiation | 1 | +15% radiation damage. Radiation ticks can crit. |
| | | | | 2 | +15% → +25% radiation damage. Radiation ticks can crit. |
| | | | | 3 | +25% → +40% radiation damage. Radiation ticks can crit. |
| Critical Mass | `poison_critical` | Legendary | Run · Radiation | 1 | Enemies with 20+ Radiation are Exposed: they take +25% damage from everything. |
| | | | | 2 | Enemies with 20+ Radiation are Exposed: they take +25% → +40% damage from everything. |
| Fission | `poison_fission` | Legendary | Run · Radiation | 1 | When an irradiated enemy dies, 50% of its Radiation spreads to each other enemy. |
| | | | | 2 | When an irradiated enemy dies, 50% → 80% of its Radiation spreads to each other enemy. |
| Chain Fission | `mythic_chain_fission` | Mythic | Run · Radiation | 1 | Rage attacks double the target's Radiation (at most +50). Your own healing is halved. |
| Fallout Aura | `mythic_fallout_aura` | Mythic | Run · Radiation | 1 | Each round: every enemy gains 2 Radiation per different status it has. |
| Gamma Burst | `mythic_gamma_burst` | Mythic | Run · Radiation | 1 | Crits on an irradiated enemy make its Radiation tick instantly, at double damage. |
| Blood Scent | `bleed_scent` | Common | Run · Bleed | 1 | On crit: consume 2 Bleed for 20% ATK per stack, heal 30% of it. |
| | | | | 2 | On crit: consume 2 Bleed for 20% ATK per stack, heal 30% → 50% of it. |
| | | | | 3 | On crit: consume 2 Bleed for 20% ATK per stack, heal 50% → 80% of it. |
| Serrated Edge | `bleed_serrated` | Common | Run · Bleed | 1 | Your first hit each battle applies Bleed ×3. |
| | | | | 2 | Your first hit each battle applies Bleed ×3 → ×5. |
| | | | | 3 | Your first hit each battle applies Bleed ×5 → ×8. |
| Tearing Wounds | `bleed_tearing` | Common | Run · Bleed | 1 | When a bleeding enemy attacks: 30% chance its wounds tear, +1 Bleed. |
| | | | | 2 | When a bleeding enemy attacks: 30% → 50% chance its wounds tear, +1 Bleed. |
| | | | | 3 | When a bleeding enemy attacks: 50% → 80% chance its wounds tear, +1 Bleed. |
| Blood Frenzy | `bleed_frenzy` | Legendary | Run · Bleed | 1 | Rage attack consumes the target's Bleed: 25% ATK per stack, heal 50% of it. At max stacks, refund 50 rage. |
| | | | | 2 | Rage attack consumes the target's Bleed: 25% → 40% ATK per stack, heal 50% of it. At max stacks, refund 50 rage. |
| Hemorrhage | `bleed_hemorrhage` | Legendary | Run · Bleed | 1 | Bleed can stack 5 higher. Bleed no longer ignores shields. |
| | | | | 2 | Bleed can stack 5 → 8 higher. Bleed no longer ignores shields. |
| Crimson Pact | `mythic_crimson_pact` | Mythic | Run · Bleed | 1 | Lose 3% HP each turn. Bleed ticks deal double, and consuming Bleed heals twice as much. |
| Exsanguinate | `mythic_exsanguinate` | Mythic | Run · Bleed | 1 | Hitting an enemy at max Bleed and below 25% HP kills it. Bosses take +100% instead. |
| Fallout Wounds | `mythic_fallout_wounds` | Mythic | Run · Bleed | 1 | Whenever Bleed is consumed, the enemy gains 3 Radiation per stack. |
| Bodyguard | `alien_bodyguard` | Common | Run · Alien | 1 | Your aliens block hits on the worm: the first 1 each battle. |
| | | | | 2 | Your aliens block hits on the worm: the first 1 → 2 each battle. |
| | | | | 3 | Your aliens block hits on the worm: the first 2 → 3 each battle. |
| Mothership Link | `alien_rally` | Common | Run · Alien | 1 | Battle start: launch a Drone sidekick (30% of the summoner's ATK as power). |
| | | | | 2 | Battle start: launch a Drone sidekick (30% → 50% of the summoner's ATK as power). |
| | | | | 3 | Battle start: launch a Drone sidekick (50% → 80% of the summoner's ATK as power). |
| Rally Cry | `alien_rally_cry` | Common | Run · Alien | 1 | When an alien uses its ability: the worm gets +6% ATK for its next turn. |
| | | | | 2 | When an alien uses its ability: the worm gets +6% → +10% ATK for its next turn. |
| | | | | 3 | When an alien uses its ability: the worm gets +10% → +16% ATK for its next turn. |
| Squad Bond | `alien_bond` | Common | Run · Alien | 1 | +45% alien power. |
| | | | | 2 | +45% → +75% alien power. |
| | | | | 3 | +75% → +120% alien power. |
| Decoy | `alien_decoy` | Legendary | Run · Alien | 1 | When an alien blocks a hit: 25% chance to Charm the attacker. |
| | | | | 2 | When an alien blocks a hit: 25% → 40% chance to Charm the attacker. |
| Hivemind | `alien_hivemind` | Legendary | Run · Alien | 1 | Each alien is tuned to one of your signatures; its ability also triggers it once. +25% alien power. |
| | | | | 2 | Each alien is tuned to one of your signatures; its ability also triggers it once. +25% → +40% alien power. |
| Sacrifice Protocol | `mythic_sacrifice` | Mythic | Run · Alien | 1 | Once per battle, an alien takes a fatal hit for you. It leaves the battle; you heal 30%. |
| Swarm Beacon | `mythic_swarm_call` | Mythic | Run · Alien | 1 | Battle start: launch 3 Drone sidekicks (40% of the summoner's ATK as power). |
| Swarm Link | `mythic_swarm_link` | Mythic | Run · Alien | 1 | When the worm kills an enemy, every alien acts immediately. |
| Symbiote | `mythic_symbiote` | Mythic | Run · Alien | 1 | Your aliens stop acting alone. Your rage attack also fires every alien's ability. |
| Brace | `counter_brace` | Common | Run · Counter | 1 | After you counter, take 12% less damage until your next turn. |
| | | | | 2 | After you counter, take 12% → 20% less damage until your next turn. |
| | | | | 3 | After you counter, take 20% → 32% less damage until your next turn. |
| Counter Stance | `counter_riposte` | Common | Run · Counter | 1 | +15% counter chance. |
| | | | | 2 | +15% → +25% counter chance. |
| | | | | 3 | +25% → +40% counter chance. |
| Spiked Hide | `counter_spiked` | Common | Run · Counter | 1 | When your shield absorbs a hit, reflect 30% of the absorbed damage. |
| | | | | 2 | When your shield absorbs a hit, reflect 30% → 50% of the absorbed damage. |
| | | | | 3 | When your shield absorbs a hit, reflect 50% → 80% of the absorbed damage. |
| Grudge | `counter_grudge` | Legendary | Run · Counter | 1 | Each hit you take adds +40% to your next counter. Stacks. |
| | | | | 2 | Each hit you take adds +40% → +64% to your next counter. Stacks. |
| Rebound Plating | `counter_rebound` | Legendary | Run · Counter | 1 | Your counterattacks grant a shield of 40% of the damage they deal. |
| | | | | 2 | Your counterattacks grant a shield of 40% → 64% of the damage they deal. |
| Bastion | `mythic_bastion` | Mythic | Run · Counter | 1 | You no longer make basic or combo attacks on your turn. Instead, you counter every hit at +50% damage. |
| Last Word | `mythic_last_word` | Mythic | Run · Counter | 1 | Once per battle, a fatal hit leaves you at 1 HP. Counter every hit until your next turn. |
| Mirror Carapace | `counter_mirror` | Mythic | Run · Counter | 1 | The first hit you take each round is reflected in full to every enemy. You take +20% damage. |
| Momentum | `combo_momentum` | Common | Run · Combo | 1 | On combo: gain 12 rage. |
| | | | | 2 | On combo: gain 12 → 20 rage. |
| | | | | 3 | On combo: gain 20 → 32 rage. |
| Opening Flurry | `combo_opening` | Common | Run · Combo | 1 | Your first turn each battle: +30% combo chance. |
| | | | | 2 | Your first turn each battle: +30% → +50% combo chance. |
| | | | | 3 | Your first turn each battle: +50% → +80% combo chance. |
| Quick Hands | `combo_flurry` | Common | Run · Combo | 1 | +15% combo chance. |
| | | | | 2 | +15% → +25% combo chance. |
| | | | | 3 | +25% → +40% combo chance. |
| Battle Rhythm | `combo_rhythm` | Legendary | Run · Combo | 1 | Each combo: +2.5% ATK this battle. A turn without a combo resets it. |
| | | | | 2 | Each combo: +2.5% → +4% ATK this battle. A turn without a combo resets it. |
| Whirlwind | `combo_whirlwind` | Legendary | Run · Combo | 1 | Every 4th combo strike sweeps all enemies for 100% ATK. |
| | | | | 2 | Every 4th combo strike sweeps all enemies for 100% → 160% ATK. |
| Endless Chain | `mythic_endless_chain` | Mythic | Run · Combo | 1 | Combo chance no longer drops within a turn. Each strike deals 15% less than the last; combo chance at most 80%. |
| Fury Chain | `mythic_fury_chain` | Mythic | Run · Combo | 1 | 3 combo strikes in one turn: unleash a free rage attack. |
| Perfect Form | `mythic_perfect_form` | Mythic | Run · Combo | 1 | Your combo chance also counts as counter chance, and counterattacks can combo. |
| Afterimage | `time_afterimage` | Common | Run · Time | 1 | Echoes deal +15% damage. |
| | | | | 2 | Echoes deal +15% → +25% damage. |
| | | | | 3 | Echoes deal +25% → +40% damage. |
| Head Start | `time_head_start` | Common | Run · Time | 1 | Battle start: 30% chance to delay every enemy's first turn. |
| | | | | 2 | Battle start: 30% → 50% chance to delay every enemy's first turn. |
| | | | | 3 | Battle start: 50% → 80% chance to delay every enemy's first turn. |
| Rewind Shell | `time_rewind_shell` | Common | Run · Time | 1 | Once per battle below 30% HP: undo the damage of your last 1 hit(s) taken. |
| | | | | 2 | Once per battle below 30% HP: undo the damage of your last 1 → 2 hit(s) taken. |
| | | | | 3 | Once per battle below 30% HP: undo the damage of your last 2 → 3 hit(s) taken. |
| Paradox | `time_paradox` | Legendary | Run · Time | 1 | Echo repeats your strongest attack this battle instead of the last one, at +25% damage. |
| | | | | 2 | Echo repeats your strongest attack this battle instead of the last one, at +25% → +40% damage. |
| Stasis Field | `time_stasis` | Legendary | Run · Time | 1 | Every 4th round, all enemies lose their turn. Your Echo that round deals +25%. |
| | | | | 2 | Every 4th round, all enemies lose their turn. Your Echo that round deals +25% → +40%. |
| Borrowed Time | `mythic_borrowed_time` | Mythic | Run · Time | 1 | Take an extra turn each round. Every 4th round, you lose your turn. |
| Loop | `mythic_loop` | Mythic | Run · Time | 1 | Once per battle, when you'd die, rewind yourself and all enemies 2 rounds. |
| Time Dilation | `mythic_time_dilation` | Mythic | Run · Time | 1 | Status damage ticks twice per round. Permanent stacks decay by 1 each round. |
| Eagle Eye | `lab_eagle_eye` | Common | Laboratory | 1 | +10% crit chance. +15% crit damage. |
| | | | | 2 | +10% → +17% crit chance. +15% → +25% crit damage. |
| | | | | 3 | +17% → +27% crit chance. +25% → +40% crit damage. |
| Eagle Eye II | `lab_eagle_eye_2` | Common | Laboratory | 1 | +12% crit chance. +20% crit damage. |
| | | | | 2 | +12% → +20% crit chance. +20% → +33% crit damage. |
| | | | | 3 | +20% → +32% crit chance. +33% → +53% crit damage. |
| Eagle Eye III | `lab_eagle_eye_3` | Common | Laboratory | 1 | +14% crit chance. +25% crit damage. |
| | | | | 2 | +14% → +23% crit chance. +25% → +42% crit damage. |
| | | | | 3 | +23% → +37% crit chance. +42% → +67% crit damage. |
| Eagle Eye IV | `lab_eagle_eye_4` | Common | Laboratory | 1 | +16% crit chance. +30% crit damage. |
| | | | | 2 | +16% → +27% crit chance. +30% → +50% crit damage. |
| | | | | 3 | +27% → +43% crit chance. +50% → +80% crit damage. |
| Eagle Eye V | `lab_eagle_eye_5` | Common | Laboratory | 1 | +18% crit chance. +35% crit damage. |
| | | | | 2 | +18% → +30% crit chance. +35% → +58% crit damage. |
| | | | | 3 | +30% → +48% crit chance. +58% → +93% crit damage. |
| Second Wind | `lab_second_wind` | Common | Laboratory | 1 | Below 30% HP: heal 25% of max HP (overheal becomes a shield). |
| | | | | 2 | Below 30% HP: heal 25% → 42% of max HP (overheal becomes a shield). |
| | | | | 3 | Below 30% HP: heal 42% → 67% of max HP (overheal becomes a shield). |
| Second Wind II | `lab_second_wind_2` | Common | Laboratory | 1 | Below 30% HP: heal 30% of max HP (overheal becomes a shield). |
| | | | | 2 | Below 30% HP: heal 30% → 50% of max HP (overheal becomes a shield). |
| | | | | 3 | Below 30% HP: heal 50% → 80% of max HP (overheal becomes a shield). |
| Second Wind III | `lab_second_wind_3` | Common | Laboratory | 1 | Below 30% HP: heal 35% of max HP (overheal becomes a shield). |
| | | | | 2 | Below 30% HP: heal 35% → 58% of max HP (overheal becomes a shield). |
| | | | | 3 | Below 30% HP: heal 58% → 93% of max HP (overheal becomes a shield). |
| Second Wind IV | `lab_second_wind_4` | Common | Laboratory | 1 | Below 30% HP: heal 40% of max HP (overheal becomes a shield). |
| | | | | 2 | Below 30% HP: heal 40% → 67% of max HP (overheal becomes a shield). |
| | | | | 3 | Below 30% HP: heal 67% → 107% of max HP (overheal becomes a shield). |
| Second Wind V | `lab_second_wind_5` | Common | Laboratory | 1 | Below 30% HP: heal 45% of max HP (overheal becomes a shield). |
| | | | | 2 | Below 30% HP: heal 45% → 75% of max HP (overheal becomes a shield). |
| | | | | 3 | Below 30% HP: heal 75% → 120% of max HP (overheal becomes a shield). |
| Elemental Storm | `lab_elemental_storm` | Legendary | Laboratory | 1 | Each round: 40% chance to apply Heat to all enemies. Each round: 40% chance to apply Shock to all enemies. Each round: 40% chance to apply Radiation to all enemies. |
| | | | | 2 | Each round: 40% → 64% chance to apply Heat to all enemies. Each round: 40% → 64% chance to apply Shock to all enemies. Each round: 40% → 64% chance to apply Radiation to all enemies. |
| Elemental Storm II | `lab_elemental_storm_2` | Legendary | Laboratory | 1 | Each round: 50% chance to apply Heat to all enemies. Each round: 50% chance to apply Shock to all enemies. Each round: 50% chance to apply Radiation ×2 to all enemies. |
| | | | | 2 | Each round: 50% → 80% chance to apply Heat to all enemies. Each round: 50% → 80% chance to apply Shock to all enemies. Each round: 50% → 80% chance to apply Radiation ×2 to all enemies. |
| Elemental Storm III | `lab_elemental_storm_3` | Legendary | Laboratory | 1 | Each round: 60% chance to apply Heat to all enemies. Each round: 60% chance to apply Shock to all enemies. Each round: 60% chance to apply Radiation ×2 to all enemies. |
| | | | | 2 | Each round: 60% → 96% chance to apply Heat to all enemies. Each round: 60% → 96% chance to apply Shock to all enemies. Each round: 60% → 96% chance to apply Radiation ×2 to all enemies. |
| Elemental Storm IV | `lab_elemental_storm_4` | Legendary | Laboratory | 1 | Each round: 70% chance to apply Heat to all enemies. Each round: 70% chance to apply Shock to all enemies. Each round: 70% chance to apply Radiation ×3 to all enemies. |
| | | | | 2 | Each round: 70% → 100% chance to apply Heat to all enemies. Each round: 70% → 100% chance to apply Shock to all enemies. Each round: 70% → 100% chance to apply Radiation ×3 to all enemies. |
| Elemental Storm V | `lab_elemental_storm_5` | Legendary | Laboratory | 1 | Each round: 80% chance to apply Heat to all enemies. Each round: 80% chance to apply Shock to all enemies. Each round: 80% chance to apply Radiation ×3 to all enemies. |
| | | | | 2 | Each round: 80% → 100% chance to apply Heat to all enemies. Each round: 80% → 100% chance to apply Shock to all enemies. Each round: 80% → 100% chance to apply Radiation ×3 → ×4 to all enemies. |
| Lucky Dice | `lab_lucky_dice` | Legendary | Laboratory | 1 | Each roll: 20% chance to roll 1 extra die next roll. |
| | | | | 2 | Each roll: 20% → 32% chance to roll 1 extra die next roll. |
| Lucky Dice II | `lab_lucky_dice_2` | Legendary | Laboratory | 1 | Each roll: 30% chance to roll 1 extra die next roll. |
| | | | | 2 | Each roll: 30% → 48% chance to roll 1 extra die next roll. |
| Lucky Dice III | `lab_lucky_dice_3` | Legendary | Laboratory | 1 | Each roll: 40% chance to roll 1 extra die next roll. |
| | | | | 2 | Each roll: 40% → 64% chance to roll 1 extra die next roll. |
| Lucky Dice IV | `lab_lucky_dice_4` | Legendary | Laboratory | 1 | Each roll: 50% chance to roll 1 extra die next roll. |
| | | | | 2 | Each roll: 50% → 80% chance to roll 1 extra die next roll. |
| Lucky Dice V | `lab_lucky_dice_5` | Legendary | Laboratory | 1 | Each roll: 60% chance to roll 1 extra die next roll. |
| | | | | 2 | Each roll: 60% → 96% chance to roll 1 extra die next roll. |
| Burning Soul | `lab_burning_soul` | Legendary | Laboratory · Fire | 1 | Battle start: apply Heat ×2 to all enemies. |
| | | | | 2 | Battle start: apply Heat ×2 → ×3 to all enemies. |
| Burning Soul II | `lab_burning_soul_2` | Legendary | Laboratory · Fire | 1 | Battle start: apply Heat ×3 to all enemies. |
| | | | | 2 | Battle start: apply Heat ×3 → ×5 to all enemies. |
| Burning Soul III | `lab_burning_soul_3` | Legendary | Laboratory · Fire | 1 | Battle start: apply Heat ×4 to all enemies. |
| | | | | 2 | Battle start: apply Heat ×4 → ×6 to all enemies. |
| Burning Soul IV | `lab_burning_soul_4` | Legendary | Laboratory · Fire | 1 | Battle start: apply Heat ×5 to all enemies. |
| | | | | 2 | Battle start: apply Heat ×5 → ×8 to all enemies. |
| Burning Soul V | `lab_burning_soul_5` | Legendary | Laboratory · Fire | 1 | Battle start: apply Heat ×6 to all enemies. |
| | | | | 2 | Battle start: apply Heat ×6 → ×10 to all enemies. |
| Frost Nova | `lab_frost_nova` | Legendary | Laboratory · Ice | 1 | On rage attack: 40% chance to apply Freeze to all enemies. |
| | | | | 2 | On rage attack: 40% → 64% chance to apply Freeze to all enemies. |
| Frost Nova II | `lab_frost_nova_2` | Legendary | Laboratory · Ice | 1 | On rage attack: 55% chance to apply Freeze to all enemies. |
| | | | | 2 | On rage attack: 55% → 88% chance to apply Freeze to all enemies. |
| Frost Nova III | `lab_frost_nova_3` | Legendary | Laboratory · Ice | 1 | On rage attack: 70% chance to apply Freeze to all enemies. |
| | | | | 2 | On rage attack: 70% → 100% chance to apply Freeze to all enemies. |
| Frost Nova IV | `lab_frost_nova_4` | Legendary | Laboratory · Ice | 1 | On rage attack: 85% chance to apply Freeze to all enemies. |
| | | | | 2 | On rage attack: 85% → 100% chance to apply Freeze to all enemies. |
| Frost Nova V | `lab_frost_nova_5` | Legendary | Laboratory · Ice | 1 | On rage attack: apply Freeze to all enemies. |
| | | | | 2 | On rage attack: apply Freeze for 1 round → 2 rounds to all enemies. |
| Overload | `lab_overload` | Legendary | Laboratory · Lightning | 1 | On crit: 50% chance to apply Shock to all enemies. |
| | | | | 2 | On crit: 50% → 80% chance to apply Shock to all enemies. |
| Overload II | `lab_overload_2` | Legendary | Laboratory · Lightning | 1 | On crit: 60% chance to apply Shock to all enemies. |
| | | | | 2 | On crit: 60% → 96% chance to apply Shock to all enemies. |
| Overload III | `lab_overload_3` | Legendary | Laboratory · Lightning | 1 | On crit: 70% chance to apply Shock to all enemies. |
| | | | | 2 | On crit: 70% → 100% chance to apply Shock to all enemies. |
| Overload IV | `lab_overload_4` | Legendary | Laboratory · Lightning | 1 | On crit: 80% chance to apply Shock to all enemies. |
| | | | | 2 | On crit: 80% → 100% chance to apply Shock to all enemies. |
| Overload V | `lab_overload_5` | Legendary | Laboratory · Lightning | 1 | On crit: 90% chance to apply Shock to all enemies. |
| | | | | 2 | On crit: 90% → 100% chance to apply Shock to all enemies. |
| Fallout Cloud | `lab_toxic_cloud` | Common | Laboratory · Radiation | 1 | +3% dodge. On dodge: apply Radiation to all enemies. |
| | | | | 2 | +3% → +5% dodge. On dodge: apply Radiation ×1 → ×2 to all enemies. |
| | | | | 3 | +5% → +8% dodge. On dodge: apply Radiation ×2 → ×3 to all enemies. |
| Fallout Cloud II | `lab_toxic_cloud_2` | Common | Laboratory · Radiation | 1 | +6% dodge. On dodge: apply Radiation ×2 to all enemies. |
| | | | | 2 | +6% → +10% dodge. On dodge: apply Radiation ×2 → ×3 to all enemies. |
| | | | | 3 | +10% → +16% dodge. On dodge: apply Radiation ×3 → ×5 to all enemies. |
| Fallout Cloud III | `lab_toxic_cloud_3` | Common | Laboratory · Radiation | 1 | +9% dodge. On dodge: apply Radiation ×3 to all enemies. |
| | | | | 2 | +9% → +15% dodge. On dodge: apply Radiation ×3 → ×5 to all enemies. |
| | | | | 3 | +15% → +24% dodge. On dodge: apply Radiation ×5 → ×8 to all enemies. |
| Fallout Cloud IV | `lab_toxic_cloud_4` | Common | Laboratory · Radiation | 1 | +12% dodge. On dodge: apply Radiation ×4 to all enemies. |
| | | | | 2 | +12% → +20% dodge. On dodge: apply Radiation ×4 → ×7 to all enemies. |
| | | | | 3 | +20% → +32% dodge. On dodge: apply Radiation ×7 → ×11 to all enemies. |
| Fallout Cloud V | `lab_toxic_cloud_5` | Common | Laboratory · Radiation | 1 | +15% dodge. On dodge: apply Radiation ×5 to all enemies. |
| | | | | 2 | +15% → +25% dodge. On dodge: apply Radiation ×5 → ×8 to all enemies. |
| | | | | 3 | +25% → +40% dodge. On dodge: apply Radiation ×8 → ×13 to all enemies. |
| Bloodletting | `lab_bloodletting` | Common | Laboratory · Bleed | 1 | On rage attack: deal 50% ATK damage to the enemy. On rage attack: apply Bleed ×2 to the enemy. |
| | | | | 2 | On rage attack: deal 50% → 83% ATK damage to the enemy. On rage attack: apply Bleed ×2 → ×3 to the enemy. |
| | | | | 3 | On rage attack: deal 83% → 133% ATK damage to the enemy. On rage attack: apply Bleed ×3 → ×5 to the enemy. |
| Bloodletting II | `lab_bloodletting_2` | Common | Laboratory · Bleed | 1 | On rage attack: deal 60% ATK damage to the enemy. On rage attack: apply Bleed ×2 to the enemy. |
| | | | | 2 | On rage attack: deal 60% → 100% ATK damage to the enemy. On rage attack: apply Bleed ×2 → ×3 to the enemy. |
| | | | | 3 | On rage attack: deal 100% → 160% ATK damage to the enemy. On rage attack: apply Bleed ×3 → ×5 to the enemy. |
| Bloodletting III | `lab_bloodletting_3` | Common | Laboratory · Bleed | 1 | On rage attack: deal 70% ATK damage to the enemy. On rage attack: apply Bleed ×2 to the enemy. |
| | | | | 2 | On rage attack: deal 70% → 117% ATK damage to the enemy. On rage attack: apply Bleed ×2 → ×3 to the enemy. |
| | | | | 3 | On rage attack: deal 117% → 187% ATK damage to the enemy. On rage attack: apply Bleed ×3 → ×5 to the enemy. |
| Bloodletting IV | `lab_bloodletting_4` | Common | Laboratory · Bleed | 1 | On rage attack: deal 80% ATK damage to the enemy. On rage attack: apply Bleed ×2 to the enemy. |
| | | | | 2 | On rage attack: deal 80% → 133% ATK damage to the enemy. On rage attack: apply Bleed ×2 → ×3 to the enemy. |
| | | | | 3 | On rage attack: deal 133% → 213% ATK damage to the enemy. On rage attack: apply Bleed ×3 → ×5 to the enemy. |
| Bloodletting V | `lab_bloodletting_5` | Common | Laboratory · Bleed | 1 | On rage attack: deal 90% ATK damage to the enemy. On rage attack: apply Bleed ×2 to the enemy. |
| | | | | 2 | On rage attack: deal 90% → 150% ATK damage to the enemy. On rage attack: apply Bleed ×2 → ×3 to the enemy. |
| | | | | 3 | On rage attack: deal 150% → 240% ATK damage to the enemy. On rage attack: apply Bleed ×3 → ×5 to the enemy. |
| Pack Tactics | `lab_pack_tactics` | Legendary | Laboratory · Alien | 1 | +15% alien power. When an alien acts: the worm gain 10 rage. |
| | | | | 2 | +15% → +24% alien power. When an alien acts: the worm gain 10 → 16 rage. |
| Pack Tactics II | `lab_pack_tactics_2` | Legendary | Laboratory · Alien | 1 | +30% alien power. When an alien acts: the worm gain 15 rage. |
| | | | | 2 | +30% → +48% alien power. When an alien acts: the worm gain 15 → 24 rage. |
| Pack Tactics III | `lab_pack_tactics_3` | Legendary | Laboratory · Alien | 1 | +45% alien power. When an alien acts: the worm gain 20 rage. |
| | | | | 2 | +45% → +72% alien power. When an alien acts: the worm gain 20 → 32 rage. |
| Pack Tactics IV | `lab_pack_tactics_4` | Legendary | Laboratory · Alien | 1 | +60% alien power. When an alien acts: the worm gain 25 rage. |
| | | | | 2 | +60% → +96% alien power. When an alien acts: the worm gain 25 → 40 rage. |
| Pack Tactics V | `lab_pack_tactics_5` | Legendary | Laboratory · Alien | 1 | +75% alien power. When an alien acts: the worm gain 30 rage. |
| | | | | 2 | +75% → +120% alien power. When an alien acts: the worm gain 30 → 48 rage. |
| Retaliation | `lab_retaliation` | Common | Laboratory · Counter | 1 | +10% counter chance. On counter: 10% chance to apply Stun to the enemy. |
| | | | | 2 | +10% → +17% counter chance. On counter: 10% → 17% chance to apply Stun to the enemy. |
| | | | | 3 | +17% → +27% counter chance. On counter: 17% → 27% chance to apply Stun to the enemy. |
| Retaliation II | `lab_retaliation_2` | Common | Laboratory · Counter | 1 | +15% counter chance. On counter: 20% chance to apply Stun to the enemy. |
| | | | | 2 | +15% → +25% counter chance. On counter: 20% → 33% chance to apply Stun to the enemy. |
| | | | | 3 | +25% → +40% counter chance. On counter: 33% → 53% chance to apply Stun to the enemy. |
| Retaliation III | `lab_retaliation_3` | Common | Laboratory · Counter | 1 | +20% counter chance. On counter: 30% chance to apply Stun to the enemy. |
| | | | | 2 | +20% → +33% counter chance. On counter: 30% → 50% chance to apply Stun to the enemy. |
| | | | | 3 | +33% → +53% counter chance. On counter: 50% → 80% chance to apply Stun to the enemy. |
| Retaliation IV | `lab_retaliation_4` | Common | Laboratory · Counter | 1 | +25% counter chance. On counter: 40% chance to apply Stun to the enemy. |
| | | | | 2 | +25% → +42% counter chance. On counter: 40% → 67% chance to apply Stun to the enemy. |
| | | | | 3 | +42% → +67% counter chance. On counter: 67% → 100% chance to apply Stun to the enemy. |
| Retaliation V | `lab_retaliation_5` | Common | Laboratory · Counter | 1 | +30% counter chance. On counter: 50% chance to apply Stun to the enemy. |
| | | | | 2 | +30% → +50% counter chance. On counter: 50% → 83% chance to apply Stun to the enemy. |
| | | | | 3 | +50% → +80% counter chance. On counter: 83% → 100% chance to apply Stun to the enemy. |
| Bulwark Protocol | `lab_bulwark` | Legendary | Laboratory · Counter | 1 | Battle start: gain a shield of 10% of max HP. On counter: 50% chance to gain a shield of 2% of max HP. |
| | | | | 2 | Battle start: gain a shield of 10% → 16% of max HP. On counter: 50% chance to gain a shield of 2% → 3% of max HP. |
| Bulwark Protocol II | `lab_bulwark_2` | Legendary | Laboratory · Counter | 1 | Battle start: gain a shield of 14% of max HP. On counter: 50% chance to gain a shield of 4% of max HP. |
| | | | | 2 | Battle start: gain a shield of 14% → 22% of max HP. On counter: 50% chance to gain a shield of 4% → 6.5% of max HP. |
| Bulwark Protocol III | `lab_bulwark_3` | Legendary | Laboratory · Counter | 1 | Battle start: gain a shield of 18% of max HP. On counter: 50% chance to gain a shield of 6% of max HP. |
| | | | | 2 | Battle start: gain a shield of 18% → 29% of max HP. On counter: 50% chance to gain a shield of 6% → 9.5% of max HP. |
| Bulwark Protocol IV | `lab_bulwark_4` | Legendary | Laboratory · Counter | 1 | Battle start: gain a shield of 22% of max HP. On counter: 50% chance to gain a shield of 8% of max HP. |
| | | | | 2 | Battle start: gain a shield of 22% → 35% of max HP. On counter: 50% chance to gain a shield of 8% → 13% of max HP. |
| Bulwark Protocol V | `lab_bulwark_5` | Legendary | Laboratory · Counter | 1 | Battle start: gain a shield of 26% of max HP. On counter: 50% chance to gain a shield of 10% of max HP. |
| | | | | 2 | Battle start: gain a shield of 26% → 42% of max HP. On counter: 50% chance to gain a shield of 10% → 16% of max HP. |
| Chain Strikes | `lab_chain_strikes` | Common | Laboratory · Combo | 1 | +10% combo chance. |
| | | | | 2 | +10% → +17% combo chance. |
| | | | | 3 | +17% → +27% combo chance. |
| Chain Strikes II | `lab_chain_strikes_2` | Common | Laboratory · Combo | 1 | +15% combo chance. |
| | | | | 2 | +15% → +25% combo chance. |
| | | | | 3 | +25% → +40% combo chance. |
| Chain Strikes III | `lab_chain_strikes_3` | Common | Laboratory · Combo | 1 | +20% combo chance. |
| | | | | 2 | +20% → +33% combo chance. |
| | | | | 3 | +33% → +53% combo chance. |
| Chain Strikes IV | `lab_chain_strikes_4` | Common | Laboratory · Combo | 1 | +25% combo chance. |
| | | | | 2 | +25% → +42% combo chance. |
| | | | | 3 | +42% → +67% combo chance. |
| Chain Strikes V | `lab_chain_strikes_5` | Common | Laboratory · Combo | 1 | +30% combo chance. |
| | | | | 2 | +30% → +50% combo chance. |
| | | | | 3 | +50% → +80% combo chance. |
