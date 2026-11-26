# UI kit (menu pieces and their sprite slots)

Code: `client/RogueStars/Assets/Scripts/Ui/Kit/` (`Kit`, `KitArt`, `KitGradient`, `KitFit`), built on `Ui.cs`, `UiShapes.cs` and `Theme.cs`.
The menu screens (Worm, Shop, Laboratory, Aliens, Quests) are composed only from these pieces; the audit
(`menu-ui-audit`) lists what else still draws plain chrome.

## How a slot works

Every kit piece is drawn procedurally today (rounded rectangles, gradients, rims, shadows, hexagons, rarity colours from `Theme`).
If a sprite with the piece's name exists at `Assets/Art/Resources/Art/Ui/Kit/<piece>.png` the piece uses it instead; nothing else
changes. Rules for the art:

* Authored **white / light grey, tintable**: code multiplies the piece's colour onto it (panel tone, rarity colour, button colour),
  so one sprite serves every colour. Keep baked-in colour to small details (gem facets, rivets).
* 9-slice pieces: set the sprite's **Border** in the importer (the numbers below, in source pixels). Canvas is 1080 wide at
  100 pixels per unit, so source pixels = canvas units: a 64 px corner shows as a 64-unit corner. Draw them at the size given.
* Transparent edges, no outer shadow baked in (code adds the drop shadow). Sprite mode Single, Mesh type Full Rect for 9-slice.

## Pieces

Shared pieces first (they unblock every screen), then the screen-specific ones.

| Piece (sprite name) | Used by | Size, 9-slice border | Notes |
|---|---|---|---|
| `panel` | every card, modal box, list row | 256x256, border 64 | Soft inner shading, light rim. Tinted with the card colour. |
| `panel_raised`, `panel_well` | highlighted card, inset well | 256x256, border 64 | Raised = brighter rim; well = inner shadow. |
| `button` (+ optional `button_primary`, `_secondary`, `_positive`, `_danger`, `_premium`, `_accent`, `_gold`, `button_lip`) | every button | 256x112, border 40 (lip: 256x112 flat colour strip, border 40) | Face only (the lip is a second image below it). Pressed/disabled are code tints; per-colour sprites optional. |
| `tab_chip`-style chips | sub-tabs, filters, shop tabs | use `button` | Active chip = gold tint, inactive = dark tint (code). |
| `bottom_bar` | main tab bar background | 1080x150, border 0/40 | Plum strip. |
| `bottom_tab`, `bottom_tab_active` | main tab bar tabs | 200x160, border 50; active 200x200 with a rounded raised top | Active tab is taller; the icon sits above the label. |
| `ribbon`, `ribbon_tail` | ribbon section headers | ribbon 256x96 border 40; tail 128x80 (swallow tail, mirrored in code) | Tinted per ribbon colour. |
| `icon_back` | screen header back button | 96x96, simple | Replaces the "<" glyph. |
| `chip` | currency chip | 128x64, border 32 | Dark pill with a thin rim. |
| `badge_level` | level / count badge | 96x48, border 24 | Dark pill. |
| `badge_dot` | red "!" notification dot | 64x64, simple | Code draws the "!". |
| `pill_track`, `pill_fill` | progress pills and bars | 128x48, border 24 | Fill is tinted per use (green, gold, cyan). |
| `stat_pill` | stat bar rows | 256x56, border 28 | The dark value pill; the icon disc overlaps its left end. |
| `stat_<key>` (`hp`, `atk`, `def`, `speed`, `critchance`, ...) | stat bar, stat cards | 64x64 | Replaces the coloured abbreviation disc. Keys are the `StatId` names lower-cased. |
| `card_frame` | rarity cards (part tiles, alien cards) | 192x192, border 48 | Tinted with the rarity colour (Common grey, Rare green, Epic blue, Legendary purple, Mythic red; the part ladder tints further). |
| `gem` | rarity gem on cards | 40x40, simple | Tinted with the rarity colour. |
| `slot_reward` | reward slots, tier cards | 128x128, border 32 | Square slot. |
| `node_hex`, `node_hex_ring` | skill-tree nodes, progression perks | 128x128, simple (pointy-top hexagon, ring 9% thick) | Fill tinted by node colour; ring tinted dark/gold. |
| `backdrop_menu` | every screen behind the scroll | 1080x2400 | Dark warm, subtle pattern, readable under cards. |
| `backdrop_worm`, `panel_stage` | worm stage behind the portrait | 512x640, border 64 | Soft spotlight. |
| `backdrop_aliens` | aliens stage | 1080x620 | Illustrated stage (see Aliens). |

Procedural-only helpers (no sprite): connector lines (`Kit.Line`), the gradient, the shadow.

## Kit API (what each piece is in code)

`Kit.Surface/Card/RarityCard/Gem` (panels), `Kit.Button/BackButton` (buttons), `Kit.TabChips` (chip tabs; labels never wrap, `KitFit`),
`Kit.BottomTabBar` (main tab bar with the raised active tab; screens opt in with `ActiveMainTab`), `Kit.Ribbon`,
`Kit.CurrencyChip`, `Kit.LevelBadge/NotifyDot/CornerBadge/Pill`, `Kit.ProgressPill`, `Kit.StatPills`, `Kit.HexNode`, `Kit.RewardSlot`,
`Kit.Backdrop`, `Kit.Line`, `Kit.Title` (outlined display text). `UiScreen` builds the header (back button, title) and the screen
backdrop from the kit.
