# Skill icon prompts (skills-v3)

Source of truth: `tools/art/packages/skills-v3.json`. Edit the subject there; this page is for review. Each subject is one literal object, no text, no skill name.

## Escalation ladder

Every synergy family escalates with skill rarity. Each tier uses DIFFERENT objects (not the same motif scaled up) and explicit escalation words, so a Mythic skill outranks a Common one of the same family at a glance. Tiers: Common = tiny/simple single element; Rare = larger with one accent; Epic = bold, two accents and a glow; Legendary = large and dramatic, glowing halo and golden trim; Mythic = huge, multi-element, golden crown and gold sparkles. Signature emblems (sig_*) use the Mythic look. Content currently ships mostly Common, Legendary and Mythic skills (Rare/Epic are rarer), so those tiers carry most of the weight.

Approved anchors (fixed, not regenerated, removed from the package): sig_alien, sig_counter, sig_radiation, fire_kindle (heart-shaped flame, sits at the upper Common/Rare step of Fire), ice_armor (frost-covered breastplate, the Epic step of Ice), gen_pathfinder, gen_guard, gen_regen, gen_giant_slayer, gen_camper.

### Fire

| tier | rarity | example motif |
|---|---|---|
| 1 | Common | a single tiny ember spark (fire_ember) |
| 2 | Rare | a small campfire flame / heart-shaped flame (anchor fire_kindle) |
| 3 | Epic | a roaring bonfire with flying sparks |
| 4 | Legendary | a flaming sword or wreathed bomb with a bright halo and gold trim |
| 5 | Mythic | a blazing fire phoenix / sun-like inferno with a golden crown and gold sparkles |

### Ice

| tier | rarity | example motif |
|---|---|---|
| 1 | Common | a tiny frost chip or single cyan shard |
| 2 | Rare | an icicle cluster |
| 3 | Epic | a frozen shield / breastplate with frost aura (anchor ice_armor) |
| 4 | Legendary | a large ice block, glacier crown or frozen hand with halo and gold trim |
| 5 | Mythic | a huge ice dragon / giant frost claw / ice-and-fire orb, golden crown |

### Lightning

| tier | rarity | example motif |
|---|---|---|
| 1 | Common | a single tiny electric spark |
| 2 | Rare | one yellow lightning bolt |
| 3 | Epic | twin bolts arcing between glowing orbs |
| 4 | Legendary | a large bolt on a spire or a dark storm cloud with halo and gold trim |
| 5 | Mythic | a roaring thunderhead raining dozens of bolts, golden crown |

### Bleed

| tier | rarity | example motif |
|---|---|---|
| 1 | Common | one small blood drop, tiny knife or claw scratches |
| 2 | Rare | a dripping blade with a blood pool |
| 3 | Epic | a bloody fang with a red glow and a slash |
| 4 | Legendary | a snarling jaw or a dagger-pierced heart with halo and gold trim |
| 5 | Mythic | a blood waterfall / crimson aura emblem, golden crown |

### Radiation

| tier | rarity | example motif |
|---|---|---|
| 1 | Common | a small geiger counter or one green goo drop |
| 2 | Rare | a green trefoil |
| 3 | Epic | a glowing reactor core with two accents |
| 4 | Legendary | a cracked glowing sphere or splitting atom with halo and gold trim |
| 5 | Mythic | a cluster of endlessly splitting atoms / fallout ring, golden crown |

### Combo

| tier | rarity | example motif |
|---|---|---|
| 1 | Common | a small ball, flag or two fists with a few echoes |
| 2 | Rare | a short chain of links |
| 3 | Epic | a spinning chain whip with a glow |
| 4 | Legendary | a drum with orbiting chain or a blade whirlwind, halo and gold trim |
| 5 | Mythic | a huge infinity chain / chain wreath, golden crown |

### Counter

| tier | rarity | example motif |
|---|---|---|
| 1 | Common | a small buckler, sword or short spikes |
| 2 | Rare | a steel-blue shield with a return arrow |
| 3 | Epic | a spiked shield with a glow |
| 4 | Legendary | a gauntlet or armor plate bouncing arrows, halo and gold trim |
| 5 | Mythic | a castle fortress with golden banners / mirror shield, golden crown |

### Alien

| tier | rarity | example motif |
|---|---|---|
| 1 | Common | a small teal alien with one small prop |
| 2 | Rare | a saucer with a beam |
| 3 | Epic | an alien squad with a green glow |
| 4 | Legendary | a large decoy or glowing brain with halo and gold trim |
| 5 | Mythic | a huge swarm beacon / alien riding a giant worm, golden crown |

### Time

| tier | rarity | example motif |
|---|---|---|
| 1 | Common | a small clock hand, stopwatch or pocket watch |
| 2 | Rare | a clock with one violet ghost |
| 3 | Epic | a gear clock with a glow |
| 4 | Legendary | a violet figure-eight hourglass or clock in a bubble, halo and gold trim |
| 5 | Mythic | a huge clockwork wheel / watch of coins, golden crown |

### Crit

| tier | rarity | example motif |
|---|---|---|
| 1 | Common | a small star |
| 2 | Rare | a golden eye or fist with a star burst |
| 3 | Epic | a glowing star-eyed face |
| 4 | Legendary | a large star-burst weapon with halo and gold trim |
| 5 | Mythic | a huge crowned star burst |

### General / other

| tier | rarity | example motif |
|---|---|---|
| 1 | Common | a small plain item (boots, book, pickaxe) |
| 2 | Rare | a larger item with one accent |
| 3 | Epic | a bold item with two accents and a glow |
| 4 | Legendary | a large dramatic item with halo and gold trim |
| 5 | Mythic | a huge multi-element crowned emblem, gold sparkles |

## Skill table

Rarity is shown in square brackets at the start of the effect column.

| id | name | effect | subject |
|---|---|---|---|
| `sig_bleed` | Lacerate | [Mythic] Every hit makes the enemy bleed. | a huge red blood drop with a clean slash across it, crimson aura, glowing halo, golden crown and gold sparkles |
| `sig_fire` | Ignite | [Mythic] Every hit heats the enemy. | a huge blazing fire phoenix with spread wings, roaring flames, a bright halo, a golden crown and gold sparkles |
| `sig_combo` | Flurry | [Mythic] Hits may strike again. | a huge glowing purple chain wreath of links hitting a target, bright halo, golden crown and gold sparks |
| `sig_ice` | Chill | [Mythic] Every hit chills the enemy (and cools its Heat). | a huge glittering ice snowflake crown with a glowing halo, frost aura, golden trim and gold sparkles |
| `sig_lightning` | Arc | [Mythic] Every hit jumps to another enemy. | a huge golden-yellow lightning bolt arcing between two crackling orbs, bright halo, golden crown and gold sparks |
| `sig_time` | Echo | [Mythic] Your attacks echo. | a huge golden hourglass with violet ghost copies of itself, bright halo, golden crown and gold sparkles |
| `gen_ambush` | Ambush Instinct | [Common] Pick your fights: arrive angry. | a small clawed paw reaching out from dark bushes with two glowing eyes |
| `crit_brutal` | Brutal Force | [Rare] +24% crit damage. | a golden fist smashing a cracked wall with a gold star burst |
| `gen_calm_mind` | Calm Mind | [Rare] The boss awakens slower: a longer run. | a blue lotus floating above still water with ripples and one small glowing pearl |
| `crit_keen` | Keen Eye | [Common/Rare/Epic] Crit chance up (Keen Eye / Hawk Eye / Star Eye chain). | a golden eye with a star-shaped glint in the pupil and a faint glow |
| `gen_power` | Power | [Common/Rare] Flat attack bonus (Power / Greater Power). | a flexed arm muscle with a small red up arrow |
| `gen_wealth` | Prospector | [Common] Won battles give 12 run coins. | a small pickaxe leaning on a small pile of gold nuggets |
| `gen_scholar` | Scholar | [Common] +18% XP. | a small open book with a small yellow lightbulb above it |
| `gen_shield_breaker` | Shield Breaker | [Rare] +30% damage vs shielded enemies. | a blue shield split in two by a hammer blow with one spark |
| `gen_swift` | Swift | [Common] +12 Speed, +6 dodge. | a small pair of winged boots |
| `gen_vampire` | Vampiric | [Rare] +6% lifesteal. | two white fangs with a red blood drop and a faint dark red glow |
| `gen_vitality` | Vitality | [Common/Rare] Flat HP bonus (Vitality / Greater Vitality). | a small red heart with a small plus sign |
| `gen_cull` | Cull | [Legendary] The weak fall at once. | a large skull split by a huge axe, bright halo and golden trim |
| `gen_growth` | Growth Spurt | [Epic] Every level-up: +2% ATK and HP for the run. | a bold green young tree with glowing leaves and a golden up arrow, soft green glow |
| `gen_overclocked` | Overclocked Core | [Legendary] Power at a price. | a large glowing red-hot gear core leaking sparks and steam, bright halo and golden trim |
| `mythic_catalyst` | Catalyst | [Mythic] Afflictions feed each other. | a huge glass flask of swirling orange, green and red liquids, golden crown, gold sparkles |
| `mythic_last_light` | Last Light | [Mythic] Brightest before the end. | a huge blinding candle flame flaring in the dark, golden crown, gold sparkles |
| `mythic_overkill` | Overkill | [Mythic] Nothing goes to waste. | a huge golden arrow driven far through a bullseye, golden crown, gold sparkles |
| `fire_ember` | Ember Strikes | [Common] Crits may stoke Heat. | a single tiny glowing red ember spark, small and simple |
| `fire_plating` | Thermal Plating | [Common] Heated attackers build your shield. | a small orange metal plate with one tiny flame on its edge |
| `fire_controlled` | Controlled Burn | [Legendary] Safer, bigger, later Supernovas. | a large round bomb wreathed in a ring of fire, with a bright glowing halo and golden trim |
| `fire_flashpoint` | Flashpoint | [Legendary] Detonate a hot enemy at will. | a large barrel bursting into a roaring fireball with a bright halo, golden trim and flying sparks |
| `mythic_chain_reaction` | Chain Reaction | [Mythic] Every blast seeds the next. | a huge row of golden-rimmed bombs exploding one after another in a giant firestorm, golden crown, gold sparkles |
| `fire_heat_siphon` | Heat Siphon | [Mythic] Drink the fire. | a huge iron cauldron overflowing with roaring flames, a golden crown above, golden sparks and halo |
| `mythic_thermal_shock` | Thermal Shock | [Mythic] Heat and shock lock an enemy down. | a huge blazing sun split by a giant lightning bolt in an X shape, golden crown, gold sparkles |
| `ice_brittle` | Brittle | [Common] Cold makes enemies fragile. | a tiny pale blue ice chip with one thin crack |
| `ice_shard` | Ice Shard | [Common] Chilled enemies draw ice shards. | a small cyan ice shard flying to the right with a tiny frost trail |
| `ice_cold_snap` | Cold Snap | [Legendary] A freeze chills the pack. | a large hand snapping with a burst of jagged ice spikes, bright halo and golden trim |
| `ice_deep_freeze` | Deep Freeze | [Legendary] Deep cold locks an enemy in ice. | a large glowing blue ice block with a red cube frozen inside, bright halo and golden trim |
| `ice_permafrost` | Permafrost | [Legendary] Deep cold stays. | a large crown of jagged glacier ice spires on frozen ground, glowing halo and golden trim |
| `mythic_cold_fusion` | Cold Fusion | [Mythic] Fire and ice, together. | a huge swirling orb half roaring flame and half glittering ice, golden crown, gold sparkles |
| `mythic_shatter` | Thaw Blast | [Mythic] Break the ice. | a huge ice dragon head shattering into many flying crystal shards, golden crown, gold sparkles |
| `mythic_winters_grip` | Winter's Grip | [Mythic] Cold steals their tricks. | a huge icy clawed giant hand crushing a glowing orb with a frost aura, golden crown, gold sparkles |
| `light_conductor` | Conductor Coil | [Common] Stronger Arcs. | a small copper wire coil with a few yellow sparks |
| `light_overclock` | Overclock | [Common] Fully shocked enemies arc again. | a small yellow lightning bolt looping in a circle |
| `light_spark` | Spark | [Common] Arcs may shock. | a single tiny yellow electric spark, small and simple |
| `light_ground_loop` | Ground Loop | [Legendary] Arcs find the last enemy. | a large lightning bolt striking a tall metal spire, glowing halo and golden trim |
| `light_thunderclap` | Thunderclap | [Legendary] Arcs build a storm. | a large dark storm cloud with a huge yellow bolt and a white shock ring, bright halo and golden trim |
| `mythic_capacitor` | Capacitor | [Mythic] Store the charge, then release. | a huge glowing blue capacitor overloading with crackling yellow lightning, golden crown, gold sparkles |
| `mythic_superconductor` | Superconductor | [Mythic] Lightning loves the cold. | a huge yellow lightning bolt coiled around a frosty cyan pillar, golden crown, gold sparkles |
| `mythic_thunderhead` | Thunderhead | [Mythic] The storm hits everyone. | a huge roaring dark storm cloud raining dozens of yellow lightning bolts, golden crown, gold sparkles |
| `poison_geiger` | Geiger Counter | [Common] Radiation adds up to a burst. | a small geiger counter box with a tiny dial needle |
| `poison_spit` | Hot Spit | [Common] Attackers get irradiated. | a small open mouth spitting one tiny green goo drop |
| `poison_glands` | Reactor Glands | [Common] Hotter radiation that can crit. | a small green glowing gland sac with a tiny crit star |
| `poison_critical` | Critical Mass | [Legendary] Heavy radiation exposes. | a large cracked glowing green sphere bursting with bright rays, glowing halo and golden trim |
| `poison_fission` | Fission | [Legendary] Irradiated deaths spread. | a large glowing green atom splitting in two with energy rings, bright halo and golden trim |
| `mythic_chain_fission` | Chain Fission | [Mythic] Doubling cores. | a huge cluster of glowing green atoms splitting again and again, golden crown, gold sparkles |
| `mythic_fallout_aura` | Fallout Aura | [Mythic] Mixed afflictions breed radiation. | a huge swirling green fallout cloud ring around a glowing flask of mixed liquids, golden crown, gold sparkles |
| `mythic_gamma_burst` | Gamma Burst | [Mythic] Crits flash the core. | a huge blinding green star burst from a glowing core with a gold crit star, golden crown, gold sparkles |
| `bleed_scent` | Blood Scent | [Common] Crits cash in wounds. | a small nose sniffing a single tiny red blood drop |
| `bleed_serrated` | Serrated Edge | [Common] Open every fight with a deep cut. | a small serrated knife with one red drop on its teeth |
| `bleed_tearing` | Tearing Wounds | [Common] Bleeding attackers tear their wounds. | three small red claw scratch marks on a patch of cloth |
| `bleed_frenzy` | Blood Frenzy | [Legendary] Rage attacks drink the enemy's Bleed. | a large snarling fanged jaw biting a roaring red blood splash, bright halo and golden trim |
| `bleed_hemorrhage` | Hemorrhage | [Legendary] Deeper wounds, no longer through shields. | a large red heart pierced by a dagger gushing blood, glowing halo and golden trim |
| `mythic_crimson_pact` | Crimson Pact | [Mythic] Blood for blood. | two huge hands clasped around a glowing red blood drop with a crimson aura, golden crown, gold sparkles |
| `mythic_exsanguinate` | Exsanguinate | [Mythic] Bled dry. | a huge crimson blood waterfall pouring from a cracked golden vial, golden crown, gold sparkles |
| `mythic_fallout_wounds` | Fallout Wounds | [Mythic] Spent blood glows. | a huge red blood drop glowing green from inside with radiation sparks, golden crown, gold sparkles |
| `alien_bodyguard` | Bodyguard | [Common] Aliens throw themselves in front. | a small teal alien holding a small round shield |
| `alien_rally` | Mothership Link | [Common] Battle start: launch a drone sidekick. | a small teal drone saucer beaming a small light ray down |
| `alien_rally_cry` | Rally Cry | [Common] Your aliens fire the worm up. | a small teal alien shouting with a tiny flag |
| `alien_bond` | Squad Bond | [Common] Sidekick abilities are stronger. | two small teal aliens holding hands with a small green heart |
| `alien_decoy` | Decoy | [Legendary] Blocked attackers lose their way. | a large green inflatable alien balloon decoy with a spiral swirl, glowing halo and golden trim |
| `alien_hivemind` | Hivemind | [Legendary] Aliens channel your signatures. | a large glowing green brain with teal antennae and radiating links, bright halo and golden trim |
| `mythic_sacrifice` | Sacrifice Protocol | [Mythic] One falls so you stand. | a huge teal alien dissolving into golden-green sparks rising into a giant shield, golden crown |
| `mythic_swarm_call` | Swarm Beacon | [Mythic] Drones answer your call. | a huge teal beacon tower sending out rings while a swarm of saucers flies in, golden crown, gold sparkles |
| `mythic_swarm_link` | Swarm Link | [Mythic] A kill rallies the squad. | a huge row of teal aliens joined by blazing green lines, golden crown, gold sparkles |
| `mythic_symbiote` | Symbiote | [Mythic] The squad moves with the worm. | a huge teal alien riding a giant green worm head, glowing aura, golden crown, gold sparkles |
| `counter_brace` | Brace | [Common] Countering steadies you. | a small steel-blue shield planted in the ground |
| `counter_riposte` | Counter Stance | [Common] Chance to counter-attack. | a small steel-blue sword with a small curved arrow |
| `counter_spiked` | Spiked Hide | [Common] Shields bite back. | a small steel-blue round shield with a few sharp spikes |
| `counter_grudge` | Grudge | [Legendary] Every blow feeds the answer. | a large steel-blue gauntlet fist gripping a stack of red tally marks, glowing halo and golden trim |
| `counter_rebound` | Rebound Plating | [Legendary] Counters build a shield. | a large steel-blue armor plate bouncing a glowing arrow back, bright halo and golden trim |
| `mythic_bastion` | Bastion | [Mythic] Stand still; answer everything. | a huge steel-blue castle fortress with golden banners and arrows flying back out, golden crown, gold sparkles |
| `mythic_last_word` | Last Word | [Mythic] Not yet. | a huge cracked steel-blue shield firing one blazing golden arrow, golden crown, gold sparkles |
| `counter_mirror` | Mirror Carapace | [Mythic] The first hit each round bounces back to every enemy. | a huge shiny mirror shield flashing beams of light in all directions, golden crown, gold sparkles |
| `combo_momentum` | Momentum | [Common] Combos build rage. | a small purple ball rolling downhill with a few speed lines |
| `combo_opening` | Opening Flurry | [Common] Fast out of the gate. | a small purple flag with one small whoosh burst |
| `combo_flurry` | Quick Hands | [Common] Chance to strike again. | two small purple fists punching with a few motion echoes |
| `combo_rhythm` | Battle Rhythm | [Legendary] Keep the chain going. | a large purple drum with chain links circling it, glowing halo and golden trim |
| `combo_whirlwind` | Whirlwind | [Legendary] Every few strikes sweep the field. | a large purple whirlwind of spinning blades, bright halo and golden trim |
| `mythic_endless_chain` | Endless Chain | [Mythic] The chain never slows. | a huge glowing purple chain looping in an infinity sign, golden crown, gold sparkles |
| `mythic_fury_chain` | Fury Chain | [Mythic] Three strikes unleash fury. | three huge purple chain links glowing red and bursting apart in fury, golden crown, gold sparkles |
| `mythic_perfect_form` | Perfect Form | [Mythic] Offense is defense. | a huge purple fist inside a perfect glowing golden circle with a shield glow, golden crown, gold sparkles |
| `time_afterimage` | Afterimage | [Common] Stronger echoes. | a small golden clock hand with one faint violet ghost behind it |
| `time_head_start` | Head Start | [Common] Strike before they move. | a small golden stopwatch with a tiny dash arrow |
| `time_rewind_shell` | Rewind Shell | [Common] Undo the worst of it. | a small golden pocket watch with a small violet arrow curling back |
| `time_paradox` | Paradox | [Legendary] Echo your best. | a large violet hourglass twisted into a figure eight, glowing halo and golden trim |
| `time_stasis` | Stasis Field | [Legendary] Freeze time for everyone else. | a large golden clock frozen inside a violet bubble with ice sparkles, bright halo and golden trim |
| `mythic_borrowed_time` | Borrowed Time | [Mythic] Live faster, pay later. | a huge golden pocket watch overflowing with gold coins and spinning hands, golden crown, gold sparkles |
| `mythic_loop` | Loop | [Mythic] Not like this. Again. | a huge golden clockwork wheel with a violet arrow circling in a closed loop, golden crown, gold sparkles |
| `mythic_time_dilation` | Time Dilation | [Mythic] Wounds age twice as fast. | a huge cracked violet clock face with golden hands rushing, golden crown, gold sparkles |
