# Run UI glitches (playtest 0.1.359 audit)

Report only: nothing in this list is fixed yet, except where marked "fixed on playtest/0-1-359".
Owner feedback: "many small visual glitches in the UI, take a look at run UI and see what you notice".

**How it was captured.** `RunUiAuditShowcaseTests` (PlayMode, explicit; run in batchmode *without* `-nographics`,
`ROGUESTARS_SHOTS_DIR` = output folder, `ROGUESTARS_SHOT_SIZES` = `1080x2340,720x1600`) renders every run state at both
sizes: board (HUD, footer, roll, fork), event card, vendor event, treasure pod (chest), campfire + Train, chance outcome
sheet, chest after a fight, level-up draft, Mythic chest + choice, revive, shop (full HP / hurt + sold + poor), quest card
(3 nodes), signature pick, five battles (2 enemies, 3 enemies, elite without mount, miniboss champion, boss defeat;
start / mid / end), run result (victory, defeat, doubled). It also writes `audit_log.txt` with an automatic layout pass
per shot (truncated / overflowing text, text off screen, overlapping labels, each fighter's sprite centre vs its HP bar).

Screenshots and crops are in the agent scratchpad (not committed):
`/private/tmp/claude-501/-Users-ferdinandvondeneichen-Projects/dccbc46f-deac-5027-b8b5-62e10bc0f356/scratchpad/`
`shots/` (first run), `shots2/` (after the fixes), `pair/` (1080 | 720 side by side), `crops/gNN_*.png` (one per finding).

Known / deferred items are not listed again: letter / star skill icons (D5), enemy / boss / prop art (D12), part icons
(D1), animation (D15), auto roll (D8), menu art kit (D19).

Severity: **H** = visible on every run or looks broken, **M** = noticeable, **L** = polish. Effort: S < 1 h, M = half a
day, L = a day or more.

## Board (HUD, footer, fork)

| # | Sev | What is wrong | Crop | Suggested fix | Effort |
|---|---|---|---|---|---|
| 1 | M | The top HUD pills float straight on the board with no backing: board markers (chest, mystery rocks, the portal well) show between and behind the pills, so the HUD reads as noisy and the board looks cut off under it. | `g01_hud_no_backing` | A soft dark gradient band (top 220 px → transparent) behind `TopStrip`, or keep the camera from framing tiles under the strip. | S |
| 2 | H | Every decision overlay (event, vendor, chest pod, campfire, shop, quest) draws its **own copy of the top strip about 10 px to the right and 5 px higher** than the board's, and without the XP fill in the Lv pill and with a larger coin icon: the whole HUD jumps whenever a sheet opens or closes. | `g02_modal_hud_offset` (top = board, bottom = event) | Build the modal strip with the same `SliceHud` call and anchors as the board strip (or reparent the one strip above the modal layer instead of drawing a copy). | S–M |
| 3 | M | Footer labels "x1" (speed) and "Skills" sit on a hard black rectangle (text background box), unlike every other label (outlined text). | `g03_footer_label_boxes` | Remove the label background or use the chunky outline style (`SliceKit.Chunky`). | S |
| 4 | L | Dice faces on the ROLL cap: the pips sit in a tight cluster in the upper half of each face (the 3 and the 5 do not reach the corners), so the dice look smudged. | `g04_dice_pips` | Lay the pips out on a 3×3 grid at 25 / 50 / 75 % of the face. | S |
| 5 | M | "Hold for Auto" / "Pick a path" / "Choose" is dark red on the red cap: hard to read. | `g05_roll_label_contrast` | White chunky text with a dark outline, or a darker recess for the caption. | S |
| 6 | M | The vertical boss clock rail (horned skull, miniboss pips, dice + roll counter) is drawn on top of path tiles at the left edge; at 720 it covers a tile and its marker. | `g06_boss_rail_over_board` | Give the rail a backing pill and keep the camera's left margin clear of it (or move the rail into the HUD band). | M |
| 7 | L | The roll counter "0/28" starts at x = −4: its first digit touches / is cut by the left screen edge (both sizes; layout log `OFFSCREEN … RollCounter`). | `g06_boss_rail_over_board` | Anchor the counter inside the rail's rect (left padding ≥ 8 px). | S |
| 8 | M | Draft / Mythic "Reroll (1)" and the outcome sheet's "Continue" are placed **on top of the dimmed ROLL button** (its dice still visible around them). | `g08_reroll_over_roll_button`, `g34_continue_over_roll` | Hide the roll cap while a sheet is open, or place the sheet buttons above the footer. | S |
| 9 | M | z-order: the **Skills** book stays bright above every modal's dim (draft, Mythic, outcome, chest, shop, vendor, quest scene) while the speed chip next to it is dimmed; in event scenes it floats over the scene art at a different height per sheet (y 490 on the vendor, y 1110 on the cache, on top of Mothwick's name plate in the quest card). | `g09_skills_not_dimmed`, `g15_skills_floating_over_scene`, `g35_quest_skills_over_plate` | Decide one rule: either dim it like the rest of the footer, or give it a fixed slot in each sheet's button row; never over scene art / name plates. | S–M |
| 10 | M | Fork: the "A · LAND / B · LAND" highlights are axis-aligned rectangles over diamond-shaped isometric tiles; at 720 the two boxes touch each other, B is left of A, and at 1080 the A box sits on the boss rail's skull. | `g10_fork_land_boxes` | Draw the highlight as the tile's diamond (the slab outline) and put the A/B tag above the tile; keep the tags apart. | M |

## Event, chest, campfire, quest, outcome

| # | Sev | What is wrong | Crop | Suggested fix | Effort |
|---|---|---|---|---|---|
| 11 | H | The event / battle / result **backdrop repeats vertically**: two moons (one under the HUD, one under the coin pill) — the background is a stacked copy with a visible seam in every event scene, the battle arena and the result screen. | `g11_backdrop_double_moon` | Fit the backdrop to the frame (cover, cropped) instead of tiling, or use a taller image. | S |
| 12 | L | Event props (the cart) have a **light grey ellipse** as their ground shadow — it reads as a white blob. | `g12_prop_light_shadow` | Use the dark soft shadow (`rs_softshadow` tinted black, ~35 % alpha). | S |
| 13 | M | Event option outcome text: the two chance branches run together ("65%: +1 reward chest, Boss wakes 8% sooner 35%: Lose 20% HP, …"), 5 lines under one card vs 1 line under the other, ending ~25 px above the card's edge; the description leaves a large empty band above the options. | `g13_event_option_text` | One line per branch (line break before each "n%:"), cap at 3 lines with a "?" for detail, move the options up. | S |
| 14 | M | Vendor event (Smuggler): the neutral **"Leave"** card uses the green *gain* star ("Nothing changes") and the red *loss* star ("Gain nothing at all") — the same icons as the real gain/cost card. | `g14_vendor_neutral_icons` | A neutral icon (the » / leave arrow) and one line "Nothing happens" for a no-op option. | S |
| 15 | L | The event title ribbon (Smuggler) and the shop sign touch the coin pill (0–4 px gap). | `g16_ribbon_vs_coins`, `g38_shop_sign_vs_coins` | Start the ribbon / sign ≥ 16 px under the HUD's second row. | S |
| 16 | M | Campfire → Train: at 1080×2340 the 4th skill card is **clipped by the list mask** mid-icon ("…for the rest of / the run" cut), with no scroll hint; at 720 it fits. | `g17_train_list_clipped` | Size the cards with `SliceCards.Layout` like the draft (shrink to fit) or show a scroll fade + arrow. | S |
| 17 | L | Campfire option results ("Restore 35% HP", "Improve 1 existing skill") are pale olive on cream: low contrast. | `g37_campfire_pale_text` | Use the darker good-green of the outcome rows (`Theme.Good` darkened) or the body ink. | S |
| 18 | L | The chest sprite (Mythic chest intro / chest after a fight) has a **white fringe** along its outline (cut-out halo). | `g33_chest_white_fringe` | Re-export with premultiplied / defringed alpha (or alpha-bleed in the importer). | S |
| 19 | M | Revive ("Defeated!") is still the **old plain panel** (system font, flat buttons, purple "Your skills" bar), the only run overlay not in the Slice style. | `g22_revive_old_style` | Rebuild it with `SliceCards.Overlay` + ribbon + chunky buttons like the outcome sheet. | M |
| 20 | L | Outcome sheet: "Tap Continue." hint sits on the brick pedestal under the Continue button (redundant), and a big empty band between the card and the button. | `g34_continue_over_roll` | Drop the hint; anchor Continue under the card. | S |

## Draft, Mythic, signature, shop

| # | Sev | What is wrong | Crop | Suggested fix | Effort |
|---|---|---|---|---|---|
| 21 | M | Skill card text with a **decimal**: "Kindling Heart: +1.5% damage per Heat stack" (owner rule: no decimals on cards). | `g36_card_decimal` | Round the tuning (e.g. +2% per stack, or +1% with a higher cap). | S |
| 22 | L | Card layout: the "?" button overlaps the right end of the grey name bar and sits ~6 px above its centre; family tags (ALIEN / BLEED / COMBO) on the signature cards crowd against it. | `g21_hub_through_dim` (cards), `g36_card_decimal` | End the name bar before the "?" (or put "?" inside the card's top-right padding), centre it on the bar. | S |
| 23 | L | At 720×1600 three-line card texts (Mirror Carapace) end 2–4 px above the card's bottom edge. | `g39_mythic_text_tight_720` | Give the text best-fit down to ~85 % or let cards grow by one line. | S |
| 24 | H | **Signature pick** (run start from the hub): the overlay barely dims the hub — the resource bar, nav bar, hub labels and red notification badges ("1", "3", "4") stay bright beside / between the cards (z-order: badges above the modal dim). | `g21_hub_through_dim` | Open the pick on the modal layer above the resource bar and nav (full-screen dim ≥ 85 %), or hide badges while a modal is up. | S |
| 25 | M | Shop stat perk: the perk text ("+8% speed", at 720 "+4% life / steal" on two lines) is printed **over the book icon**; every stat uses the same book-with-arrow icon. | `g18_perk_text_over_icon_720` | Caption under the tile (like the heal's "Heal 35%" but outside the art), per-stat icons from the HUD set (sword, shield, heart…). | S |
| 26 | M | Shop SALE: "SALE!" label collides with the "?" button and the struck-through old price sits half outside the price button's top edge. | `g19_sale_label_vs_help` | Put SALE as a corner ribbon on the price button; strike price inside the button above the new price. | S |
| 27 | L | Sold offers: "Sold" is off-centre (low-right) in the grey button, the "SALE!" tag stays on a sold offer, and the SOLD stamp covers the description. | `g20_sold_button_text_720` | Centre the label (same anchors as the price label), hide SALE when sold. | S |
| 28 | L | Shop / draft price & reroll buttons use three different formats: "Reroll (1)" (draft), "Reroll + coin 10" (shop, two lines), price chips small in the hero area vs big on the skill cards. | `board_rolling`, `shop` shots | One button style for rerolls ("Reroll · 10 🪙" / "Reroll (1 left)"), one price-chip size. | S |

## Battle playback

| # | Sev | What is wrong | Crop | Suggested fix | Effort |
|---|---|---|---|---|---|
| 29 | H | **Worm not centred on its HP bar** (owner report): the art stands at 30 % of its card, the bars were centred at 50 % → worm ~70 px left of its bar; the enemies have the mirror error (~70 px right of their bars), it is just less visible on wide enemy art. | `battle_two_enemies_start` before/after (`shots/` vs `shots2/`) | **Fixed on playtest/0-1-359**: bars centred on the art's line (`BattlePlaybackScreen.SpriteAnchorX`), test `Battle_EveryFightersBars_AreCentredUnderItsArt`. | done |
| 30 | M | Three enemies: the stage scales them to 0.6, so their HP numbers ("840 / 840") are ~10 px tall and unreadable, and the enemies look tiny next to the worm. | `g24_three_enemies_tiny_bars` | Keep bars / labels at a minimum size (counter-scale the HpHolder) and use two columns for 3+ enemies instead of shrinking. | M |
| 31 | M | Floating combat text spawns in a lane near the top: "BLOCKED" / "−109" stack on top of each other **under the NEXT strip**, "−142" lands on the speed button; the number is far from the fighter that took the hit. | `g25_popups_under_next`, `g42_fight_popup_over_speed` | Spawn hit numbers over the target's sprite; keep the ability lane below the NEXT strip and above the arena; z-order popups over the strip. | M |
| 32 | M | Bosses / elites on a family sprite run off the **right screen edge** (the boss's wing, the Lava Brute's arm). | `g26_boss_clipped_right` | Clamp the enemy x so the drawn art box stays ≥ 16 px inside the screen (the art box is known from the sprite). | S |
| 33 | M | The left sidekick (green blob alien) is cut by the left screen edge. | `g30_sidekick_clipped_left` | Shift `CompanionPosition` right by ~40 px or scale the back column down. | S |
| 34 | L | After the last action the **empty "NEXT" plate** stays on screen. | `g27_empty_next_strip` | Hide the next-actor strip when `Finished`. | S |
| 35 | M | Defeat: "Continue" is off-centre (pushed right by the Log button). | `g28_continue_off_centre` | Centre Continue on the screen; keep Log in its own corner slot. | S |
| 36 | L | "VICTORY" / "DEFEAT" banner covers the worm's face. | `g29_victory_over_worm` | Place the banner above the fighters (under the NEXT strip). | S |
| 37 | L | Status chip counts ("2", "1") hang outside the chip's bottom-right corner, overlapping the next chip. | `g40_status_chip_digits` | Put the count inside the chip (badge with its own background) and add spacing. | S |
| 38 | L | The firing highlight (red frame) stays on the first skill tile after the battle ended; the skill tray uses only the top third of the footer (large empty area above "Log"). | `battle_*_end` shots | Clear the highlight on finish; centre the tray vertically in the footer. | S |

## Run result

| # | Sev | What is wrong | Crop | Suggested fix | Effort |
|---|---|---|---|---|---|
| 39 | H | Only 5 reward tiles fit; the 6th reward is hidden behind "+1 more", which is printed **under the 3rd tile's caption** (it reads "Diamonds +1 more"). After "double rewards" the hidden reward looks as if it was not doubled. | `g31_result_more_and_surge` | **Fixed on playtest/0-1-359**: up to 7 tiles shrink to fit one row (`RunResultScreen.MaxRewardTiles`), "+N more" only beyond that. Still to do: put "+N more" at the row's end, not under a tile. | done / S |
| 40 | M | "Biomass surge ×4 applied" is squeezed onto the strip's lower gold edge line, touching the chips row. | `g31_result_more_and_surge` | Move it into the strip (above the tiles) or under the chips. | S |
| 41 | L | "You Survived" sits on the pennant's top rod; the stars overlap the pennant's lower edge. | `g32_result_survived_on_rod` | Move the caption ~20 px down / shrink the rod. | S |
| 42 | L | Reward captions are ~12 px at 720 ("Rare random part" wider than its tile). | `g31_result_more_and_surge` | Min caption size 20 px at 1080, two lines allowed. | S |

## General

| # | Sev | What is wrong | Fix | Effort |
|---|---|---|---|---|
| 43 | L | Numbers are formatted two ways: HUD / outcome rows "4266 / 4382", "+3375 HP" (no separator) vs result / shop "x1,250" (`Fmt.Num`). | Use `Fmt.Num` in the HUD bar text and outcome rows. | S |
| 44 | L | Overlays use three backdrops: dimmed board (draft, Mythic, outcome), opaque black (Train), full scene (events). | One rule per sheet type (decision on the board = dimmed board). | S |

## Summary

44 findings: **5 H** (2 fixed), **20 M**, **19 L**. Top 10 by impact: #2 HUD jumps when a sheet opens, #11 doubled
backdrop / two moons, #24 hub bleeding through the signature pick, #29 worm vs HP bar (fixed), #39 hidden 6th reward
(fixed), #9 Skills book above every dim, #8 sheet buttons on the roll cap, #31 combat text under the NEXT strip, #19 old
revive panel, #10 fork highlight boxes.
