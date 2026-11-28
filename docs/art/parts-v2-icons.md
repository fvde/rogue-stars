# Parts v2 icons (for the art session)

Source of truth for the items: `docs/design/parts-v2/data.py` (also generated into `shared/RogueStars.Rules/Content/ContentDb.PartData.g.cs`).

## How many icons and how they're keyed

- **Standard parts (Common, Rare, Epic, Legendary): 30 icons, one per slot × role.** The four tiers of the same slot and role share one icon; the tier is shown by the rarity tile behind it (grey, green, blue, purple) and the rank letter. Icon key: `part_<slot>_<role>` (slots `head`, `body`, `tail`, `legs`, `arm`; roles `guard`, `strike`, `swift`, `keen`, `mend`, `fury`). Game item ids look like `c_head_guard`, `r_head_guard`, `e_head_guard`, `l_head_guard`; the client maps all four to `part_head_guard`.
- **Mythics: 30 unique icons**, key = the game id (`myth_cinder_crown`, …). The awakened form uses the same icon (no separate awakened art for now).
- **Total: 60 icons.**

## Drawing rules

- 512x512, transparent, one object, centred, filling ~80% of the canvas. The client draws it on a 170x170 rarity tile (at 1080 width) with ~12% padding, so keep a clear margin; no frame, no background, no ground, no shadow, no text.
- Same style as the approved icons: bold black outline, flat colours, two-tone cel shading.
- Parts are robot/sci-fi gear for a cartoon space worm: a **helm** (head), a **shell / chest plate** (body), a **tail piece** (tail), a **pair of leg units** (legs), a **weapon arm** (arm). Arms: the weapon noun is the look (hammer, cannon, whip, claw, drill, blaster), mounted on a short mechanical arm socket.
- Standard parts: role colour + role motif, plain and readable; Mythics: richer, the synergy's element clearly visible (fire, ice, lightning, radiation, bleed, combo, counter, alien, time, general).

## Standard parts (30): `part_<slot>_<role>`

Role colours/motifs: guard = steel blue, heavy plating, rivets; strike = orange-red, spikes/edges; swift = teal, fins, streamlined; keen = yellow, a lens / targeting sight; mend = green, a glowing capsule; fury = red-purple, vents and a glowing core.

| Key | Look |
|---|---|
| part_head_guard | heavy steel-blue helm with a thick visor plate and rivets |
| part_head_strike | orange-red helm with a forward spike crest |
| part_head_swift | teal streamlined helm with swept-back fins |
| part_head_keen | yellow helm with one big round targeting lens |
| part_head_mend | white-green helm with a glowing green capsule on top |
| part_head_fury | red-purple horned helm with glowing vents |
| part_body_guard | steel-blue chest plate, thick riveted armour |
| part_body_strike | orange-red chest plate with spiked shoulders |
| part_body_swift | teal light chest plate with air fins |
| part_body_keen | yellow chest plate with a targeting lens in the centre |
| part_body_mend | white-green chest plate with a glowing green heart capsule |
| part_body_fury | red-purple chest plate with a glowing furnace core |
| part_tail_guard | steel-blue armoured tail segment with a round shield end |
| part_tail_strike | orange-red tail segment with a blade tip |
| part_tail_swift | teal tail segment with a small jet booster |
| part_tail_keen | yellow tail segment with a stinger and a lens |
| part_tail_mend | white-green tail segment with a glowing capsule tip |
| part_tail_fury | red-purple tail segment with a glowing spiked tip |
| part_legs_guard | pair of steel-blue heavy armoured boots |
| part_legs_strike | pair of orange-red spiked stomper boots |
| part_legs_swift | pair of teal hover boots with small thrusters |
| part_legs_keen | pair of yellow spring legs |
| part_legs_mend | pair of white-green boots with glowing green soles |
| part_legs_fury | pair of red-purple clawed boots with glowing vents |
| part_arm_guard | steel-blue hammer arm, a heavy square hammer head |
| part_arm_strike | orange-red cannon arm, a short barrel |
| part_arm_swift | teal whip arm, a coiled segmented whip |
| part_arm_keen | yellow claw arm, three sharp claws |
| part_arm_mend | white-green drill arm, a glowing green drill bit |
| part_arm_fury | red-purple blaster arm, a glowing energy barrel |

## Mythics (30): key = id

| Key | Name | Slot | Look |
|---|---|---|---|
| myth_cinder_crown | Cinder Crown | head | black-iron crown-helm with glowing orange ember cracks and flames on top |
| myth_ashwalkers | Ashwalkers | legs | pair of charcoal boots with molten orange soles and smoke wisps |
| myth_firebrand_blaster | Firebrand Blaster | arm | blaster arm with a red-hot barrel spitting flame |
| myth_rimeguard_plate | Rimeguard Plate | body | thick ice-blue chest plate with frost crystals on the shoulders |
| myth_frostglide_skates | Frostglide Skates | legs | pair of silver-blue ice skates with frosted blades |
| myth_shatter_cannon | Shatter Cannon | arm | cannon arm with an ice-crystal barrel |
| myth_dynamo_hull | Dynamo Hull | body | yellow-black chest plate with a spinning coil and lightning sparks |
| myth_storm_coil | Storm Coil | tail | tail segment ending in a glowing yellow tesla coil |
| myth_arcwright_drill | Arcwright Drill | arm | drill arm with a yellow bit crackling with lightning |
| myth_isotope_visor | Isotope Visor | head | helm with a glowing lime-green visor and a radiation symbol plate |
| myth_hotcell_tank | Hotcell Tank | body | chest plate with a glowing green fuel cell tank |
| myth_decay_stinger | Decay Stinger | tail | tail with a dripping lime-green stinger |
| myth_bloodtide_fin | Bloodtide Fin | tail | crimson finned tail segment with a sharp fin edge |
| myth_thornstep_legs | Thornstep Legs | legs | pair of dark red boots covered in thorns |
| myth_bloodletter_claw | Bloodletter Claw | arm | claw arm with long crimson blades |
| myth_afterburner_tail | Afterburner Tail | tail | violet tail segment with a big glowing afterburner jet |
| myth_flicker_treads | Flicker Treads | legs | pair of violet hover treads with motion streaks |
| myth_chainlash_whip | Chainlash Whip | arm | violet chain whip arm with glowing links |
| myth_feint_mask | Feint Mask | head | grey duelist face mask with crossed blade marks |
| myth_rampart_carapace | Rampart Carapace | body | grey fortress-like carapace with tower-shaped shoulders |
| myth_backlash_hammer | Backlash Hammer | arm | grey heavy hammer arm with a recoil spring |
| myth_swarmcaller_antenna | Swarmcaller Antenna | head | teal helm with two tall glowing antennae |
| myth_broodguard_legs | Broodguard Legs | legs | pair of teal insect legs with small shield plates |
| myth_hatchery_tail | Hatchery Tail | tail | teal tail segment carrying a glowing egg pod |
| myth_foresight_visor | Foresight Visor | head | magenta helm with a clock-face visor |
| myth_hindsight_shell | Hindsight Shell | body | magenta chest plate with an hourglass in the centre |
| myth_timeslip_boots | Timeslip Boots | legs | pair of magenta boots with small clock gears |
| myth_deadeye_visor | Deadeye Visor | head | dark helm with a red crosshair visor |
| myth_last_stand_plate | Last Stand Plate | body | battered dark-steel chest plate with a glowing red heart core |
| myth_rage_turbine | Rage Turbine | tail | red tail segment with a spinning turbine |

## Old part icons (`tools/art/packages/parts.json`)

All 30 old ids are **dropped**: the old parts no longer exist in the game (none carries over by id). Old art you like can be reused as a starting point for a matching new key (e.g. the old frost/ember/tesla looks fit the Ice/Fire/Lightning Mythics), but nothing carries over automatically.

## Currencies

`advancement_plates` is gone (Plate Foundry and plates removed). Drop it from the queue. New currency needing an icon: **Metal** (`metal`): a small stack of grey metal ingots.
