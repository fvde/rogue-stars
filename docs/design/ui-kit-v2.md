# UI kit v2: graphics to replace code-drawn UI

Source: the owner-approved screen layouts on the design canvas "Rogue Stars UI Redesign" (boards "New · worm", "New · home", "New · battle"), which follow the reference screenshots' layout and treatment. Everything below is drawn in code today (flat rounded rects, CSS-like gradients); each entry becomes a proper sprite so all screens share one consistent kit.

Style for every piece: bold cartoon game UI, thick dark navy outline (#1d1b33), soft inner highlight on the top edge, flat colours with a two-tone cel shade, no text baked in (text is set in code), no icons baked into frames. Sizes are at 1080 px screen width. "9-slice" = stretchable; give the border in px. One neutral-tint variant is fine where a piece appears in several colours (code tints), but rarity frames get their own art.

## A. Frames, panels and bars

| # | Id | Where | Size | Notes |
|---|---|---|---|---|
| 1 | `topbar` | dark navy strip at the top of every hub screen | 1080x230, 9-slice 24 | bottom edge darker lip |
| 2 | `avatar_frame` | player avatar, top-left | 160x160 | gold square frame, rounded, inner window transparent |
| 3 | `currency_pill` | currency chips in the top bar | 240x70, 9-slice 34 | dark navy pill |
| 4 | `plus_button` | green "+" on currency pills | 48x48 | green rounded square, white plus may be baked |
| 5 | `season_bar` / `season_bar_fill` | season progress under the top bar | 640x90, 9-slice 40 | gold frame + yellow fill |
| 6 | `level_hex` | hexagon level badge (season, run level) | 110x110 | gold/orange hexagon, empty centre |
| 7 | ~~`side_rail`~~ | translucent rails left/right on home | — | dropped (owner): drawn from `nav_tile`, 9-slice 56 px, at 75% opacity |
| 8 | `timer_chip` | "7h 14m" under rail icons | 140x40, 9-slice 20 | small dark pill |
| 9 | `badge_red` | red notification dot ("!" or number) | 48x48 | red circle with white ring + navy outline |
| 10 | `quick_band` | band behind the 4 home shortcuts | 900x170, 9-slice 40 | translucent dark |
| 11 | `start_button` (+ `_pressed`) | the big START | 540x210, 9-slice 60 | glossy yellow, darker bottom lip; pressed state 6 px lower |
| 12 | `edge_tab_left` / `edge_tab_right` | Guild / Modes buttons attached to the screen edge | 190x170 | dark navy, flat edge on the screen side |
| 13 | `nav_tile` | bottom nav buttons | 170x170 | dark navy rounded square |
| 14 | `nav_tile_active` | active bottom nav button (raised, label space) | 190x230 | purple, taller, room for the label at the bottom |
| 15 | `parchment_bg` | stage behind the worm on the worm screen | 512x512 tileable | cream with a faint diamond pattern |
| 16 | `power_plate` | power value under the worm | 460x90, 9-slice 40 + `power_icon` 90x90 | yellow plate + round purple badge on the left |
| 17 | `stat_bar` | HP / ATK / DEF bars | 340x70, 9-slice 34 | dark navy pill, icon overlaps the left end |
| 18 | `feature_band` | beige band under the stats (Talents, Runes, …) | 1000x140, 9-slice 40 | warm beige, rounded |
| 19 | `tab_active` / `tab_inactive` | Parts / Levels / Stats tabs | 340x110, 9-slice 30 (top corners only) | orange active, purple inactive |
| 20 | `grid_panel` + `grid_header` | dark brown inventory area + its header strip | 1080x900 / 1080x130, 9-slice 30 | warm dark brown, header darker |
| 21 | `button_small_blue` / `button_small_yellow` | "By Rarity", "Merge" | 260x86, 9-slice 30 | glossy, dark outline |
| 22 | `list_button` | small beige list button next to the power plate | 80x70 | beige with brown rim |

## B. Item tiles (parts, also reused for rewards)

| # | Id | Size | Notes |
|---|---|---|---|
| 23 | `tile_common` | 170x170 | grey; frame + inner highlight; empty centre for the item art |
| 24 | `tile_rare` | 170x170 | green |
| 25 | `tile_epic` | 170x170 | blue |
| 26 | `tile_legendary` | 170x170 | purple |
| 27 | `tile_mythic` | 170x170 | red/orange (the top rarity should feel hottest) |
| 28 | `tile_selected` / `tile_equipped` overlays | 170x170 | glow ring / small check corner |
| 29 | `slot_badge` | 48x48 | small purple square, top-left of a tile; plus 5 white glyphs: head, body, arm, legs, tail (32x32 each) |
| 30 | `copy_diamond` | 48x48 | gold diamond for the copy count, empty centre for the number |
| 31 | `pip_strip` + `pip_gem` (5 colours) + `pip_lock` | 150x34 / 22x22 | dark strip under a tile with small gems; rune/skill slots |

## C. Battle HUD

| # | Id | Where | Size | Notes |
|---|---|---|---|---|
| 32 | `hud_pill` | Lv, ATK, DEF, coins at the top of a battle/run | 200x70, 9-slice 34 | dark navy |
| 33 | `hp_frame` + fills `hp_red`, `hp_shield_blue`, `hp_rage_yellow` | top HP bar and the stacked bars under fighters | 600x60 / 460x30, 9-slice 14 | rounded, navy frame; fills slightly glossy |
| 34 | `skill_tile` (+ `_active` green glow) | skill tiles at the bottom of a battle | 170x170 | light grey/silver frame, empty centre |
| 35 | `speed_button` | x2 / x3 bottom-left | 200x110, 9-slice 30 | dark navy, arrows baked |
| 36 | `round_plate` (optional) | "Round 3" | 300x70 | can stay outlined text |
| 37 | `round_button` | cog (settings), top-right | 80x80 | generic round navy button; the settings button is `round_button` + the HUD cog icon |

## D. Illustrated icons (no frame, big and readable, 128x128 unless noted)

- Bottom nav: shop (exists), worm (use the worm sprite), planet (exists), city (new), arena (new).
- Home rails and shortcuts: quests, hero/summon event, mail, invasion, starter pack, pass (exists), growth fund, event (exists), portal map, chapter rewards (exists), patrol (exists), dark side, guild, modes.
- Worm screen features: talents, runes, mounts, laboratory.
- Currencies (64x64): stamina, diamond, ore, biomass, run coin (exists), Signals / Schematics / Crates (in progress).
- Stats (64x64): HP (exists), ATK (sword), DEF (shield), power (crossed sword badge).

## E. Not in the kit but blocking the look

- Part art: every part in the redesign needs its own item sprite (the parts screen uses stand-ins today). This is the largest visual gap; plan it with the parts redesign.
- Fonts: keep "Lilita One"-style outlined display text for numbers and titles (code, not sprites).

## Code-drawn on purpose (no sprite needed)

Rank letters (M, S, L, E, R, C) and all numbers (outlined font), damage numbers, the dimmed lower battle area, and plain dividers.

## Import (how the approved pieces get into the client)

Approved kit files live in `~/AI/assets/ui-kit-v2/<id>/<file>.png`, already at the sizes above. They are picked in
`tools/art/slice-picks.json` like the other approved art and copied by the art importer (`Tools/Art/Import Generated Art`, batch:
`-executeMethod RogueStars.Client.Editor.ArtImporter.ImportCli`) to `client/RogueStars/Assets/Art/Resources/Art/Ui/Kit/<id>.png`,
where `KitArt.Get(id)` / `KitV2` load them by id:

- `"ui-kit-v2/<id>": "<file>"`: a kit piece, copied full-frame (no trim, so the 9-slice borders stay where they were drawn).
- `"ui-kit-icons/<id>": "~/AI/assets/…"`: an approved game icon reused as a kit icon, trimmed to its opaque pixels.

Import settings (`ArtImportSettings` + `KitImportRules` in `client/RogueStars/Assets/Editor/ArtImporter.cs`): Sprite, single, full
rect, no mip maps, bilinear, high-quality compression; icons capped at 256 px. `KitImportRules.NineSlice` is the kit import list of
the 9-slice pieces with the borders from the tables above; the importer writes the border into the sprite, scaled to the file's
actual pixel size (a file at 2× the spec size gets 2× the border). Every other piece is fixed (no border). A new 9-slice piece =
one line there plus its pick; `KitArtImportTests` checks every id loads as a sprite and every 9-slice id has its border.

Batch 1 (2026-11-28, all `N_s9001.png`): every piece of sections A to C except `side_rail`, `feature_band`, `parchment_bg`,
`power_icon`, `tile_selected` / `tile_equipped`, `slot_glyph_*`, `pip_lock`, `hud_pill`, `round_plate`, `settings_button` and
`start_button_pressed` (not delivered yet) and `copy_diamond` (dropped: no copy count, every owned part item is its own tile). Pip gems come as three coloured sprites
(`pip_gem_blue`, `_purple`, `_gold`; `KitV2.PipGemId` picks one by the rune colour, any other colour stays the tinted code
diamond). Kit icons: `icon_stamina`, `icon_diamond`, `icon_ore` (the approved currency icons; `KitV2.CurrencyIcon`) and
`icon_settings` (the approved HUD cog, the glyph on the code-drawn round settings button until the generic `round_button` arrives).

Old kit (docs/design/ui-kit.md) slots drawn with the v2 sprite where it is the same element: `badge_dot` → `badge_red`
(`Kit.NotifyDot`), `chip` → `currency_pill` (`Kit.CurrencyChip`, which also takes the kit currency icons), `stat_pill` → `stat_bar`
(`Kit.StatPills`). The v2 sprites are coloured, so these draw untinted.

Batch 2 (2026-11-28): `round_button`. `nav_tile` became a 9-slice piece (border 56) so the home side rails draw it stretched at 75% opacity.
