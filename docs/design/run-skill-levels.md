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
| Arc | `sig_lightning` | Lightning | Lightning Package | On hit: jump to 1 other enemy for 50% ATK damage. | On hit: jump to 2 other enemies for 50% ATK damage. On hit: jump to 3 other enemies for 50% ATK damage. |

## Draftable skills

| Skill | Id | Tier | Source | Lv | Text (upgrade inline) |
|---|---|---|---|---|---|
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
| Boss Hunter | `gen_boss_hunter` | Legendary | Run | 1 | +25% boss damage. |
| | | | | 2 | +25% → +40% boss damage. |
| Executioner | `crit_executioner` | Legendary | Run | 1 | +40% damage against enemies below 40% HP. |
| | | | | 2 | +40% → +64% damage against enemies below 40% HP. |
| Growth Spurt | `gen_growth` | Legendary | Run | 1 | Each run level up: +2% ATK for the rest of the run. Each run level up: +2% HP for the rest of the run. |
| | | | | 2 | Each run level up: +2% → +3% ATK for the rest of the run. Each run level up: +2% → +3% HP for the rest of the run. |
| Assassin's Mark | `crit_assassin` | Mythic | Run | 1 | +30% crit damage. On crit: deal 50% ATK true damage (ignores defense) to the enemy. |
| Chrono Shell | `mythic_chrono_shell` | Mythic | Run | 1 | +10% damage reduction. Battle start: gain a shield of 30% of max HP. |
| Deadeye | `mythic_deadeye` | Mythic | Run | 1 | +25% crit chance. +50% crit damage. |
| Reaper | `mythic_reaper` | Mythic | Run | 1 | +60% damage against enemies below 40% HP. On kill: gain 40 rage. |
| Titan Blood | `mythic_titan_blood` | Mythic | Run | 1 | +40% HP. +25% ATK. +3% regeneration. |
| Warlord | `mythic_warlord` | Mythic | Run | 1 | +40% boss damage. +25% ATK. |
| Ember Strikes | `fire_ember` | Common | Run · Fire | 1 | On hit: 30% chance to apply Heat ×2 to the enemy. |
| | | | | 2 | On hit: 30% → 50% chance to apply Heat ×2 to the enemy. |
| | | | | 3 | On hit: 50% → 80% chance to apply Heat ×2 to the enemy. |
| Kindling Heart | `fire_kindle` | Common | Run · Fire | 1 | +15% damage against enemies with Heat. |
| | | | | 2 | +15% → +25% damage against enemies with Heat. |
| | | | | 3 | +25% → +40% damage against enemies with Heat. |
| Solar Flare | `fire_flare` | Legendary | Run · Fire | 1 | On rage attack: apply Heat ×3 to all enemies. |
| | | | | 2 | On rage attack: apply Heat ×3 → ×5 to all enemies. |
| Wildfire | `fire_inferno` | Legendary | Run · Fire | 1 | +25% Supernova damage. +25% Supernova self-damage reduction. |
| | | | | 2 | +25% → +40% Supernova damage. +25% → +40% Supernova self-damage reduction. |
| Cinder Storm | `mythic_cinder_storm` | Mythic | Run · Fire | 1 | Each round: apply Heat ×2 to all enemies. On Supernova: apply Heat ×2 to all enemies. |
| Phoenix Coil | `fire_phoenix` | Mythic | Run · Fire | 1 | +100% Supernova self-damage reduction. On Supernova: heal 6% of max HP. |
| Sunheart Core | `mythic_sunheart` | Mythic | Run · Fire | 1 | +40% Supernova damage. Battle start: apply Heat ×5 to all enemies. |
| Frost Armor | `ice_armor` | Common | Run · Ice | 1 | +15% DEF. Battle start: gain a shield of 6% of max HP. |
| | | | | 2 | +15% → +25% DEF. Battle start: gain a shield of 6% → 10% of max HP. |
| | | | | 3 | +25% → +40% DEF. Battle start: gain a shield of 10% → 16% of max HP. |
| Ice Shard | `ice_shard` | Common | Run · Ice | 1 | On attack: 35% chance to deal 36% ATK damage to the enemy. On attack: 6% chance to apply Freeze to the enemy. |
| | | | | 2 | On attack: 35% chance to deal 36% → 60% ATK damage to the enemy. On attack: 6% → 10% chance to apply Freeze to the enemy. |
| | | | | 3 | On attack: 35% chance to deal 60% → 96% ATK damage to the enemy. On attack: 10% → 16% chance to apply Freeze to the enemy. |
| Frostbite | `ice_frostbite` | Legendary | Run · Ice | 1 | +20% ice damage. +40% damage against enemies with Freeze. |
| | | | | 2 | +20% → +32% ice damage. +40% → +64% damage against enemies with Freeze. |
| Glacier Skin | `ice_glacier` | Legendary | Run · Ice | 1 | +10% damage reduction. When hit: 20% chance to apply Freeze to the enemy. |
| | | | | 2 | +10% → +16% damage reduction. When hit: 20% → 32% chance to apply Freeze to the enemy. |
| Absolute Zero | `ice_absolute_zero` | Mythic | Run · Ice | 1 | +30% ice damage. Battle start: apply Freeze to all enemies. |
| Shatterpoint | `mythic_shatterpoint` | Mythic | Run · Ice | 1 | +30% ice damage. +60% damage against enemies with Freeze. On attack: 25% chance to apply Freeze to the enemy. |
| Winter Crown | `mythic_winter_crown` | Mythic | Run · Ice | 1 | +30% DEF. +10% damage reduction. Battle start: gain a shield of 25% of max HP. |
| Conductor Coil | `light_conductor` | Common | Run · Lightning | 1 | +12 speed. +15% lightning damage. |
| | | | | 2 | +12 → +20 speed. +15% → +25% lightning damage. |
| | | | | 3 | +20 → +32 speed. +25% → +40% lightning damage. |
| Spark | `light_spark` | Common | Run · Lightning | 1 | On hit: 30% chance to apply Shock to the enemy. |
| | | | | 2 | On hit: 30% → 50% chance to apply Shock to the enemy. |
| | | | | 3 | On hit: 50% → 80% chance to apply Shock to the enemy. |
| Chain Lightning | `light_chain` | Legendary | Run · Lightning | 1 | On attack: 35% chance to deal 50% ATK damage to a random enemy. |
| | | | | 2 | On attack: 35% chance to deal 50% → 80% ATK damage to a random enemy. |
| Static Storm | `light_storm` | Legendary | Run · Lightning | 1 | Each round: deal 35% ATK damage to all enemies. Each round: 50% chance to apply Shock to all enemies. |
| | | | | 2 | Each round: deal 35% → 56% ATK damage to all enemies. Each round: 50% → 80% chance to apply Shock to all enemies. |
| Ball Lightning | `mythic_ball_lightning` | Mythic | Run · Lightning | 1 | Each round: deal 60% ATK damage to a random enemy. Each round: apply Shock ×2 to a random enemy. |
| Overcharge | `mythic_overcharge` | Mythic | Run · Lightning | 1 | +20 speed. +40% lightning damage. On hit: 60% chance to apply Shock ×2 to the enemy. |
| Thunder God | `light_thunder_god` | Mythic | Run · Lightning | 1 | +30% lightning damage. Each round: 30% chance to apply Stun to a random enemy. |
| Isotope Spit | `poison_spit` | Common | Run · Radiation | 1 | On hit: 30% chance to apply Radiation ×2 to the enemy. |
| | | | | 2 | On hit: 30% → 50% chance to apply Radiation ×2 to the enemy. |
| | | | | 3 | On hit: 50% → 80% chance to apply Radiation ×2 to the enemy. |
| Reactor Glands | `poison_glands` | Common | Run · Radiation | 1 | +15% radiation damage. +9% status chance. |
| | | | | 2 | +15% → +25% radiation damage. +9% → +15% status chance. |
| | | | | 3 | +25% → +40% radiation damage. +15% → +24% status chance. |
| Contagion | `poison_plague` | Legendary | Run · Radiation | 1 | On kill: apply Radiation ×4 to all enemies. +25% damage against enemies with Radiation. |
| | | | | 2 | On kill: apply Radiation ×4 → ×6 to all enemies. +25% → +40% damage against enemies with Radiation. |
| Fallout | `poison_miasma` | Legendary | Run · Radiation | 1 | End of each round: apply Radiation ×2 to all enemies. |
| | | | | 2 | End of each round: apply Radiation ×2 → ×3 to all enemies. |
| Gamma Aura | `mythic_toxic_aura` | Mythic | Run · Radiation | 1 | End of each round: apply Radiation ×2 to all enemies. +25% damage against enemies with Radiation. |
| Meltdown | `mythic_blightbloom` | Mythic | Run · Radiation | 1 | +60% radiation damage. On hit: apply Radiation ×2 to the enemy. |
| Reactor Heart | `poison_venom_heart` | Mythic | Run · Radiation | 1 | +3% regeneration. Each round: apply Radiation ×3 to all enemies. |
| Razor Scales | `bleed_razor` | Common | Run · Bleed | 1 | +15% bleed damage. +6% ATK. |
| | | | | 2 | +15% → +25% bleed damage. +6% → +10% ATK. |
| | | | | 3 | +25% → +40% bleed damage. +10% → +16% ATK. |
| Serrated Edge | `bleed_serrated` | Common | Run · Bleed | 1 | On hit: 30% chance to apply Bleed ×2 to the enemy. |
| | | | | 2 | On hit: 30% → 50% chance to apply Bleed ×2 to the enemy. |
| | | | | 3 | On hit: 50% → 80% chance to apply Bleed ×2 to the enemy. |
| Blood Frenzy | `bleed_frenzy` | Legendary | Run · Bleed | 1 | On rage attack: consume the enemy's Bleed: 25% ATK damage per stack, heal 50% of it. |
| | | | | 2 | On rage attack: consume the enemy's Bleed: 25% → 40% ATK damage per stack, heal 50% of it. |
| Hemorrhage | `bleed_hemorrhage` | Legendary | Run · Bleed | 1 | +30% damage against enemies with Bleed. |
| | | | | 2 | +30% → +48% damage against enemies with Bleed. |
| Bloodlust | `mythic_bloodlust` | Mythic | Run · Bleed | 1 | End of each round: consume all enemies' Bleed: 20% ATK damage per stack, heal 60% of it (every 3 rounds). |
| Crimson Tide | `bleed_crimson_tide` | Mythic | Run · Bleed | 1 | +10% life steal. On crit: apply Bleed ×2 to all enemies. |
| Thousand Cuts | `mythic_thousand_cuts` | Mythic | Run · Bleed | 1 | +40% bleed damage. On hit: 60% chance to apply Bleed ×2 to the enemy. |
| Rally Cry | `alien_rally` | Common | Run · Alien | 1 | Battle start: 2 extra sidekick actions. |
| | | | | 2 | Battle start: 2 → 3 extra sidekick actions. |
| | | | | 3 | Battle start: 3 → 5 extra sidekick actions. |
| Squad Bond | `alien_bond` | Common | Run · Alien | 1 | +45% alien power. |
| | | | | 2 | +45% → +75% alien power. |
| | | | | 3 | +75% → +120% alien power. |
| Alien Charm | `alien_charmer` | Legendary | Run · Alien | 1 | When a sidekick acts: 45% chance to apply Charm to the enemy. |
| | | | | 2 | When a sidekick acts: 45% → 72% chance to apply Charm to the enemy. |
| Hivemind | `alien_hivemind` | Legendary | Run · Alien | 1 | +40% alien power. When a sidekick acts: heal the worm for 6% of max HP. |
| | | | | 2 | +40% → +64% alien power. When a sidekick acts: heal the worm for 6% → 9.5% of max HP. |
| Ark Command | `mythic_ark_command` | Mythic | Run · Alien | 1 | +60% alien power. Battle start: 3 extra sidekick actions. |
| Overmind | `alien_overmind` | Mythic | Run · Alien | 1 | +50% alien power. +50% sidekick speed. |
| Swarm Beacon | `mythic_swarm_call` | Mythic | Run · Alien | 1 | Battle start: launch 3 Drone sidekicks (40% of the summoner's ATK as power). |
| Counter Stance | `counter_riposte` | Common | Run · Counter | 1 | +15% counter chance. |
| | | | | 2 | +15% → +25% counter chance. |
| | | | | 3 | +25% → +40% counter chance. |
| Spiked Hide | `counter_spiked` | Common | Run · Counter | 1 | When hit: deal 24% ATK damage to the enemy. |
| | | | | 2 | When hit: deal 24% → 40% ATK damage to the enemy. |
| | | | | 3 | When hit: deal 40% → 64% ATK damage to the enemy. |
| Parry Stance | `counter_parry` | Legendary | Run · Counter | 1 | +15% parry chance. +20% parry damage reduction. |
| | | | | 2 | +15% → +24% parry chance. +20% → +32% parry damage reduction. |
| Vengeance | `counter_vengeance` | Legendary | Run · Counter | 1 | +50% damage with counterattacks. On counter: gain 20 rage. |
| | | | | 2 | +50% → +80% damage with counterattacks. On counter: gain 20 → 32 rage. |
| Bastion | `mythic_bastion` | Mythic | Run · Counter | 1 | +20% parry chance. +30% parry damage reduction. On parry: gain 30 rage. |
| Mirror Carapace | `counter_mirror` | Mythic | Run · Counter | 1 | +10% parry chance. On parry: deal 100% ATK true damage (ignores defense) to the enemy. |
| Thorn King | `mythic_thorn_king` | Mythic | Run · Counter | 1 | +20% counter chance. When hit: deal 50% ATK damage to the enemy. |
| Momentum | `combo_momentum` | Common | Run · Combo | 1 | On combo: gain 12 rage. |
| | | | | 2 | On combo: gain 12 → 20 rage. |
| | | | | 3 | On combo: gain 20 → 32 rage. |
| Quick Hands | `combo_flurry` | Common | Run · Combo | 1 | +15% combo chance. |
| | | | | 2 | +15% → +25% combo chance. |
| | | | | 3 | +25% → +40% combo chance. |
| Battle Rhythm | `combo_rhythm` | Legendary | Run · Combo | 1 | On combo: +3% ATK for the rest of the battle. |
| | | | | 2 | On combo: +3% → +5% ATK for the rest of the battle. |
| Whirlwind | `combo_whirlwind` | Legendary | Run · Combo | 1 | On combo: deal 35% ATK damage to all enemies. |
| | | | | 2 | On combo: deal 35% → 56% ATK damage to all enemies. |
| Blade Storm | `mythic_blade_storm` | Mythic | Run · Combo | 1 | +30% combo chance. On combo: +5% ATK for the rest of the battle. |
| Cascade | `mythic_cascade` | Mythic | Run · Combo | 1 | +20% rage gain. On combo: deal 50% ATK damage to a random enemy. |
| Endless Dance | `combo_endless` | Mythic | Run · Combo | 1 | +25% combo chance. On combo: heal 2% of max HP. |
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
| Pack Tactics | `lab_pack_tactics` | Legendary | Laboratory · Alien | 1 | +15% alien power. When a sidekick acts: the worm gain 10 rage. |
| | | | | 2 | +15% → +24% alien power. When a sidekick acts: the worm gain 10 → 16 rage. |
| Pack Tactics II | `lab_pack_tactics_2` | Legendary | Laboratory · Alien | 1 | +30% alien power. When a sidekick acts: the worm gain 15 rage. |
| | | | | 2 | +30% → +48% alien power. When a sidekick acts: the worm gain 15 → 24 rage. |
| Pack Tactics III | `lab_pack_tactics_3` | Legendary | Laboratory · Alien | 1 | +45% alien power. When a sidekick acts: the worm gain 20 rage. |
| | | | | 2 | +45% → +72% alien power. When a sidekick acts: the worm gain 20 → 32 rage. |
| Pack Tactics IV | `lab_pack_tactics_4` | Legendary | Laboratory · Alien | 1 | +60% alien power. When a sidekick acts: the worm gain 25 rage. |
| | | | | 2 | +60% → +96% alien power. When a sidekick acts: the worm gain 25 → 40 rage. |
| Pack Tactics V | `lab_pack_tactics_5` | Legendary | Laboratory · Alien | 1 | +75% alien power. When a sidekick acts: the worm gain 30 rage. |
| | | | | 2 | +75% → +120% alien power. When a sidekick acts: the worm gain 30 → 48 rage. |
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
| Bulwark Protocol | `lab_bulwark` | Legendary | Laboratory · Counter | 1 | Battle start: gain a shield of 10% of max HP. On parry: 50% chance to gain a shield of 2% of max HP. |
| | | | | 2 | Battle start: gain a shield of 10% → 16% of max HP. On parry: 50% chance to gain a shield of 2% → 3% of max HP. |
| Bulwark Protocol II | `lab_bulwark_2` | Legendary | Laboratory · Counter | 1 | Battle start: gain a shield of 14% of max HP. On parry: 50% chance to gain a shield of 4% of max HP. |
| | | | | 2 | Battle start: gain a shield of 14% → 22% of max HP. On parry: 50% chance to gain a shield of 4% → 6.5% of max HP. |
| Bulwark Protocol III | `lab_bulwark_3` | Legendary | Laboratory · Counter | 1 | Battle start: gain a shield of 18% of max HP. On parry: 50% chance to gain a shield of 6% of max HP. |
| | | | | 2 | Battle start: gain a shield of 18% → 29% of max HP. On parry: 50% chance to gain a shield of 6% → 9.5% of max HP. |
| Bulwark Protocol IV | `lab_bulwark_4` | Legendary | Laboratory · Counter | 1 | Battle start: gain a shield of 22% of max HP. On parry: 50% chance to gain a shield of 8% of max HP. |
| | | | | 2 | Battle start: gain a shield of 22% → 35% of max HP. On parry: 50% chance to gain a shield of 8% → 13% of max HP. |
| Bulwark Protocol V | `lab_bulwark_5` | Legendary | Laboratory · Counter | 1 | Battle start: gain a shield of 26% of max HP. On parry: 50% chance to gain a shield of 10% of max HP. |
| | | | | 2 | Battle start: gain a shield of 26% → 42% of max HP. On parry: 50% chance to gain a shield of 10% → 16% of max HP. |
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
