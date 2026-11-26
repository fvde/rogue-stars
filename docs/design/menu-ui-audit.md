# Menu UI audit: screens that still run on placeholder UI

Date: 2026-11-26. Requested by the owner. Scope: every menu-style screen, tab, modal and sheet reachable in the Unity client
(`client/RogueStars/Assets/Scripts/Screens`, `App/ScreenCatalog.cs`, `App/HubLayout.cs`, `Ui/*`, `World/*Art.cs`), with the
unlock levels from `ContentDb.Onboarding.cs` (the code is the source of truth, see "Doc drift" at the end).

Method: static reading of the screen code, plus real PlayMode screenshots (batchmode, with graphics, one Unity at a time, a
throw-away explicit test that opened each screen with every feature unlocked; the test is not committed). Screenshots are in
`docs/design/menu-ui-audit/` (44 PNGs, 600 px wide, all under 300 KB). Items already in `docs/backlog.md` "Known and deferred"
are marked **[D#]** and are not new findings; they appear in the art queue as already queued.

How to read an element row: **what is there now** (verified in code or screenshot) -> **suggested replacement** -> **new art?**
(asset type, size, quantity, shared or not). Sizes are source sizes (the canvas is 1080 x 1920 reference, uGUI).

## 0. The big picture (read this first)

The whole menu layer is built from one code-only kit. This is the single largest source of "non-game UI":

* **Every button, card, chip, tab, bar, modal and input is a runtime-generated rounded rectangle** (`Ui/UiShapes.cs`: one 9-slice
  texture generated in code; `Ui.Button` = flat colour face + darker "lip" strip; `Ui.Card`, `Ui.Chip`, `Ui.TabBar`, `Ui.Bar`,
  `Ui.Modal`, `Ui.Sheet`, `Ui.Input` in `Ui/Ui.cs`; colours from `Ui/Theme.cs`). No skinned sprites exist for any of them.
* **Screen background = one flat colour** (`Theme.Background 0x170E10`, `ClientApp` "Background" panel; each screen is a scroll
  column of cards on it). Only the Hub (sky art), Portals (board backdrops), City (iso world), Run, Summon (drawn backdrop)
  have a real backdrop.
* **Screen header = back button (a "<" text glyph) + title text** (`UiScreen.Create`) and a "?" text glyph for help.
* **Currency icons are all code-drawn.** `SummonArt.Icon/CurrencyIcon` looks in `Art/Summon/Icons/` first, but that folder does
  not exist in `Assets/Art/Resources/Art/` (nor do `Art/Intro`, `Art/Skills`, `Art/Hub/Planet`, `Art/Decor`, `Art/Sidekicks`), so
  every currency, token, ticket, lock, check, chest, envelope, avatar, comet and star icon, the stamina bolt, the intro art and
  the planet come from procedural fallbacks (`World/SummonArt.cs`, `ScreenArt.cs`, `IntroArt.cs`). They look decent but are not
  real art, and 28 + a few ids are involved.
* **Real art that does exist** (do not redo): hero worm, 8 alien sprites, enemies, board backdrops/tiles, run characters/props/
  icons, slice HUD icons and markers, set pieces, 8 hub icons (shop, pass, event, patrol, chapter rewards, play button, worm tab,
  profile level), 4 sky backdrops, result crown/banner/crest. The Hub, Run overlays, Battle and Result screens are largely good.
* **Text-only affordances**: icons by glyph (`<`, `?`, `✕`, `★`/`☆`, `▲`, `+`, `i`, `X`, `~`, `#`, `@`), headers by plain `Ui.Display`
  text, lists by `Ui.Row`/`Ui.Card` of labels. Part tiles, alien cards, hero/mount cards, quest rows, shop rows, mail rows,
  arena rows are all label stacks on flat cards.
* A bug seen while auditing (cosmetic, not art): in the Shop tab bar the tab "Diamonds" wraps to "Diamond/s" (6 tabs squeezed;
  `ShopScreen.cs:24`). Icon tabs would fix it for good.

## 1. Screen inventory

Legend: **L#** = main level cleared (player level) at which the screen unlocks. Screenshot file names refer to
`docs/design/menu-ui-audit/`. Status: **OK** = mostly real art, **PARTIAL** = real hero art + plain chrome, **PLAIN** = all
placeholder chrome, **REDESIGN** = flagged for a layout redesign, not just a reskin.

Count: 28 top-level screens/overlays (rows S01 to S28) and about 35 sub-views (tabs, modals, sheets) inside them.

### S01 Hub (planet) - always (L0), tabs and columns appear as features unlock. Status: PARTIAL. `00_hub.png`
Code: `HubScreen.cs`, `HubKit.cs`, `HubLayout.cs`, `ClientApp.cs` (top bar).
| Element | Now | Replacement | New art? |
|---|---|---|---|
| Sky backdrop, START card, rocket, "Modes" skull, shop/pass/event/patrol/chapter-rewards icons, worm tab, profile level star | real art | keep | no |
| Planet sphere with green blobs | **code-drawn** (`ScreenArt.DrawPlanet`; `Art/Hub/Planet/` missing; 5 stages) | painted planet, 5 growth stages, set pieces (saucer, burrow, campfire) already real | yes: 5 planet sprites 1024x1024 (hub only) |
| Left column: Daily Quests (clipboard), Summon (4-point star in a square), Events (telescope) icons; right column: Offer (star in a square), Mail (envelope), More (hamburger lines); bottom: Map, Challenges; tabs City, Signals, Arena, Planet | **code-drawn hub icons** (`ScreenArt.DrawHubIcon`, 8 of ~21 ids have real files). Summon and Offer are visibly generic stand-ins | one hub icon pack in the style of the existing shop/pass icons | yes: 13 icons 192x192 (offer, mail, quests, observatory, invasion, map, challenges, tab_city, tab_signals, tab_arena, tab_planet, summon, more), shared with intro art and shop/mail/city dock |
| Column background (translucent rounded rects), season bar frame, teaser banner "Equip your starter parts on the worm" + green Go | flat rounded panels, flat green button | kit panel + button skins | kit (K1, K2) |
| Top bar: profile chip, currency chips (flat dark pills with drawn icons), wallet icon | drawn icons, flat pills | currency icon set, chip skin | kit (K6, K13) |
| Tab bar buttons (Worm real, others drawn icons) | partly drawn | tab icons from the hub pack above | in pack |

### S02 Portals / level map - always. Status: OK with plain chrome. `01_portals.png`
Code: `PortalsScreen.cs`. Real biome backdrops behind the map.
| Element | Now | Replacement | New art? |
|---|---|---|---|
| Level nodes (medals), roads, connectors, "PortalBanner" chips, Boss portrait medal | code-drawn round panels/dots (`Ui.RoundPanel`, `Ui.Dot`); boss portrait from enemy art | node medal set (normal, next, boss, cleared, locked, star row), road/path sprite | yes: 6 node frames 256x256, 1 tiling road 128x512; shared with Dark Side and Burrow pages |
| Bottom bar: level details, 3 feat checkboxes ("o Clear the level"), "i" button, START RUN, stamina cost chip | flat | kit skins, star/feat icons | kit + 3 feat icons 64 |
| Stamina refill modal (Daily free / Watch ad / Diamonds buttons) | plain `Ui.Modal` with 3 text buttons | modal frame, icons on the 3 options | kit (K11) + icons from K6/K7 |
| Level details modal, Burrow keys chip, "< Prev / Next >" paging, "Jump to level" | text buttons | arrows/page chips from kit | kit |

### S03 Worm - L1 (talent road L3). Status: PLAIN, **REDESIGN**. `02_worm.png`, `03_part_detail.png`, `04_merge_picker.png`
Code: `WormScreen.cs`, `GearScreen.cs` (shared base).
| Element | Now | Replacement | New art? |
|---|---|---|---|
| Worm header card (portrait is real art), "Level up / +10" green and brown buttons | real portrait, flat buttons | button skins | kit |
| "Equipped" 3x2 grid of slot cards (HEAD/BODY/TAIL/LEGS/LEFT ARM/RIGHT ARM): text "Empty", "1 part fits" | **text-only tiles**, no body-slot silhouettes, no part icon | **paperdoll**: the worm portrait big in the middle with 6 slot sockets around it that show the equipped part icon | yes: 6 slot silhouettes/sockets 192x192 (5 unique, arms mirror), 1 large worm paperdoll base 800x800 |
| Inventory tiles ("ARM · DRILL / Drill Lance / Common · Lv 1/10") | text-only tiles, rarity as border colour | tile with part icon, rarity frame, level pip, up-arrow badge | **[D1]** part icons (31 defs; note: there is no part-icon loader in the client at all: `ArtCatalog` has no part lookup, so D1 also needs a small code slot on every part tile, detail, merge picker, reveal and result) + rarity frames (K8) |
| Filter chips (All/Head/Body/Tail/Legs/Arms), sort tabs, "ORE 80K" chip, "Any rarity" | flat chips with count text | slot icons on the filter chips, tab skin | 6 slot icons 64 (reuse the 6 slot silhouettes) |
| Stats list (10 rows "ATK: 15" text) | text label grid | stat icon + value rows | yes: 10 stat icons 64x64 (Hp, Atk, Def, Speed, Crit chance, Crit damage, Dodge, Combo, Counter, Lifesteal), shared with Lab, Heroes, Mounts, Aliens, part detail |
| Talent road (tier card with text rows and "BIO" buttons) | text rows | a drawn talent road: tier banner + 3-4 node buttons | yes: 1 tier banner, 4 node icons 96, road line (shared with Lab connectors); **REDESIGN** as a vertical road |
| Part detail modal: chips (COMMON, ARM · DRILL, CRIT), Level up bar, stats with "-> 17 next level", skills list with "o" bullets and "unlocks at Epic", Merge (yellow), Dismantle (red), Lock/Equip buttons | long stacked text modal | header with big part icon + rarity plate; skill rows with icons; star/rank row | part icon (D1), skill icons **[D5]**, rarity plate (K8), lock/unlock icon (K7) |
| Merge picker modal (checked rows, "Copies/Fodder/Ore" panel) | text rows with check glyph | rows with part icons and a "3 in, 1 out" visual | part icons (D1) |
| Awaken / rune socket modals | text + buttons (see code `AwakenModal`) | awaken preview with before/after icon | 1 awaken effect frame 512 (optional) |

### S04 Parts / Runes - Parts L1 (same inventory as Worm), Runes L28. Status: PLAIN. `05_parts.png`, `06_runes.png`
Code: `PartsScreen.cs`, `GearScreen.cs`.
| Element | Now | Replacement | New art? |
|---|---|---|---|
| Inventory grid | same text tiles as S03 | same | D1 + K8 |
| Runes tab: rows "Bulwark Rune +2.5% / Common" with Lock and "Merge 3" buttons | **text rows, no rune icon** | rune tiles with icon + rarity gem | yes: rune icons ~8 (one per rune stat, count in `ContentDb.Parts`) 96x96 + K8 frames |
| Resource line "Ore 80K · Gear Cores 2000 · Awaken Cores 0/50" | text | currency chips with icons | K6 |
| Forge (L50) | uses part detail (cap raise) | same as S03 | none new |

### S05 Daily Quests (Daily / Weekly / Achieve / Login / Newbie tabs) - after the first level cleared (Parts, L1). Status: PLAIN, **REDESIGN**. `07_quests_daily.png`, `08_quests_login.png`, `09_quests_newbie.png`
Code: `QuestsScreen.cs`.
| Element | Now | Replacement | New art? |
|---|---|---|---|
| 5-tab bar | flat tab strip, "Achieve"/"Newbie" abbreviations | icon tabs | kit (K3) + 5 tab icons 64 |
| "Claim all completed" green bar | flat button | skin | kit |
| Points track with 5 chest icons on a thin bar | chest icon **code-drawn** (`SummonArt "chest"`), grey when locked; bar is two rounded rects | chest set with 3 states (closed grey, ready glow, opened) per track tier; bar skin | yes: 5 chests x 3 states = 15 sprites 192x192 (or 3 tiers x 3 states = 9 + glow overlay), shared with Season pass, Funds |
| Quest rows (name label, flat green progress bar, "+20 pts", Claim) | text + `Ui.Bar` | row with a quest-type icon, themed bar | yes: ~12 quest category icons 96x96 (fight, win, run, log in, shop, ads, signal, merge, arena, build, level up...) |
| Login calendar: 7 flat day tiles "Day 3 / reward text", Claim today's | flat panels | calendar card with reward icons, a big Day 7 chest, check stamps | yes: day tile 9-slice x3 states (claimed, today, future), Day-7 big chest 256 |
| Newbie event: day list rows + new-player quests | plain rows | event banner header | 1 banner 1080x300 |

### S06 Mail - after first level (Parts, L1). Status: PARTIAL. `30_mail.png`
Code: `MailScreen.cs`.
| Element | Now | Replacement | New art? |
|---|---|---|---|
| Message cards: orange unread dot, title, reward tiles (drawn icons in orange squares), Open/Claim/Delete buttons | flat cards, drawn currency icons | envelope-style card, mail-type icon, reward tiles from kit | mail-type icons 4 x 96 (welcome, reward, event, news); K10 reward tile |
| Empty state: envelope + "Your mailbox is empty." | **drawn envelope** (`SummonArt "envelope"`) | illustrated empty state | shared empty-state illustration (K16) |

### S07 Shop (Offers / Diamonds / Passes / Forever / Items / Ads) - L12. Status: PLAIN, **REDESIGN**. `10_shop_offers.png` to `14_shop_items.png`
Code: `ShopScreen.cs`. (Rolling offer names "Special Offer · Step N" are **[D7]**.)
| Element | Now | Replacement | New art? |
|---|---|---|---|
| 6-tab bar ("Diamonds" wraps to two lines) | flat tabs | icon tabs | kit (K3) + 6 tab icons |
| Shop header | none (title text only) | shop banner with the shopkeeper (the `Run/Characters/shopkeeper` sprite already exists) | reuse; 1 banner background 1080x320 |
| Pack cards: one generic drawn icon per product (a money pouch for starters/gifts, an hourglass for monthly, a star for forever, a gem for every diamond pack), price in cyan text, "Buy $0.99" orange / "Soon" grey buttons | **stand-in icons**, identical across packs | per-product illustration, tier badges ("Best value"), price tag | yes: ~16 pack illustrations 256x256: 6 diamond tiers (growing gem piles), 2 starter packs, 3 gift packs, monthly pass, forever x2, flash/rolling x2; shop-pack art is **[D12]** (already queued) |
| "Soon" buttons, "PENDING" | grey flat | kit disabled skin with a ribbon | K12 |
| Season pass: "Level 0 - 0 XP" bar + rows "Lv 1 | 500 Biomass | 30 Diamonds, 400 Ore" | text/button rows | horizontal reward track with reward icons, a locked premium lane | track tile 9-slice x3 states, premium lane frame (shared with Summon pass) |
| Growth Funds: 5-column grid of "10 / 300 Diamonds" buttons | text grid | milestone track with chests/gems | shared with quest chests |
| Items tab: text rows with cyan Diamond price buttons | text rows | rows with the token/currency icon | K6 icons |
| Ads tab: Watch/Claim rows, drawn spinner | text rows | ad row with video icon | K7 |

### S08 Signals (gacha): Part / Blueprint / Alien tabs - L1 (Part Signal, same id as Parts), Alien L6, Blueprint L15 (City). Status: PARTIAL. `15_signals_part.png`, `16_signals_blueprint.png`, `17_pull_reveal.png`
Code: `SignalsScreen.cs`, `PullReveal.cs`.
| Element | Now | Replacement | New art? |
|---|---|---|---|
| Featured banner card (dark red panel, title, big **gold star** placeholder in a pink circle when the featured item has no art) | star stand-in; banner colour only | banner art per signal and per featured banner | yes: 3 signal machine illustrations 1000x600 (Part, Blueprint, Alien), banner plate 9-slice; featured items need their own art (parts D1, blueprints D3, aliens D4) |
| Pity bars (Mythic/Legendary), "Published odds" button | flat `Ui.Bar`s | bar skin in rarity colours | kit (K9) |
| Pull buttons: Free 10x green, token orange, diamond cyan (drawn token and gem icons) | flat buttons, drawn icons | skinned buttons with token art | K2, K6 |
| Reveal sheet (`PullReveal`): dark cards with an abstract white circle/diamond **glyph** per part, green/blue tint for rarity; blueprints and runes have no art ("parts, runes and blueprints have none yet", `PullReveal.cs:137`) | placeholder glyphs | rarity card frames with the item icon | D1 (parts), D3 (blueprints), rune icons (S04), frames K8 |

### S09 Aliens - L6 (Aliens unlock; squad slots L6 with the Launch Pod). Status: PARTIAL. `18_aliens.png`
Code: `AliensScreen.cs`.
| Element | Now | Replacement | New art? |
|---|---|---|---|
| Squad header "Squad 1/1 Arcwhisk" | text card | squad seats strip with 3-4 portrait sockets | 1 socket frame 160 |
| Alien cards: rarity-tinted flat card, portrait (real sprite for 8 species; **coloured critter placeholder** for the rest), long text blocks (ability, "Next: 2*", traits, work bonus), 4 buttons Star up / Breed / Assign / Squad | wall of text on tinted cards | collection grid (portrait tile, stars, family badge) + a detail sheet for the text | **[D4]** sprites; yes: star icons (shared), 10 synergy/family icons 64 (Bleed, Fire, Radiation, Combo, Time, Lightning, Counter, Ice, Poison, Arc), rarity frames K8 |
| Assign modal (list of buildings as text buttons) | text buttons | building thumbnails (D3) | D3 |
| Tab "role" text in the card corner ("Time", "Lightning") | text | family badge | in family icons |

### S10 City / Camp - Camp L6 (crash site + Launch Pod), City L15. Status: PARTIAL, building art is **[D3]** and the camp layout **[D16]**. `20_city.png`, `21_city_build.png`, `22_city_workers.png`, `23_city_output_modal.png`
Code: `CityScreen*.cs`, `CityEditor.cs`, `World/CityWorld.cs`.
| Element | Now | Replacement | New art? |
|---|---|---|---|
| Iso ground, set pieces, aliens (real art / stand-ins) | D3, D16 | queued | queued |
| Bottom dock: Build / Workers / Output (drawn icons on purple squares) and a flat **gold COLLECT ALL** button | `DockButton` = flat button + drawn icon | skinned dock + 3 real icons + a gold collect button with a coin burst | 3 icons 96 + collect button skin (K2) |
| Build mode: right rail of blueprint cards (drawn thumbnails), bottom toolbar Undo/Rotate/Street/Erase/Move/Store (text glyphs and flat squares) | flat | 6 tool icons, blueprint thumbnails (D3) | 6 icons 64 (shared with nothing else) |
| Selected-building card (stored amount, Upgrade, Move, Store, stars, "Open the Laboratory/Reactor") with "✕" close | text card | skinned info card | kit |
| Output modal (plain `Ui.Modal`: "Nothing produces yet..." + Close / Pop all) and the currency priority star ("★"/"☆" glyphs) | plain modal, glyph stars | modal frame (K11), star icons (K7) | kit |
| Workers tray (alien chips, drag hint text) | flat chips | alien portrait chips (D4) | D4 |

### S11 Laboratory - unlocks with its blueprint (code: building feature; GDD table says L13, see Doc drift). Status: PLAIN, **REDESIGN**. `24_laboratory.png`
Code: `LaboratoryScreen.cs`.
| Element | Now | Replacement | New art? |
|---|---|---|---|
| Status card (Data, next research, slots) and legend chips (Researched/Available/Researching/Locked) | text rows, flat chips | lab header with Data icon, legend with icons | K6, K13 |
| The research tree: drag-to-pan canvas, lane stripes (flat translucent bands), nodes as **text buttons with a colour band** ("STAT · Lv 0/5 Servo Muscles"), connectors = flat rectangles and dots, a **lock icon drawn from three rectangles** (`LockIcon`), check dot | **all code-drawn**, no node art | skill-tree board: themed backdrop, node frames per kind (Stat / Skill / Package) and state (locked, available, researching, done), package emblems, connector line sprites | yes: board background 1080x2000 tileable (1), node frames 3 kinds x 4 states = 12 at 256x256 9-slice, 5-6 package emblems 160 (Combo, Ice, Lightning, Counter, Alien, Time), 3 connector sprites (thin, gold, glow), lock/check icons (K7); stat node icons = the 10 stat icons (S03); skill node icons = **[D5]** |
| Node detail modal (Research Lv N, Data cost, draft-pool toggle) | text + buttons | header with node icon, level pips | kit |

### S12 Breeding Pen - Pen blueprint (GDD L34). Status: PLAIN, **REDESIGN**. `19_breeding_locked.png`
Code: `BreedingScreen.cs` (genes are inactive, **[F5]**).
| Element | Now | Replacement | New art? |
|---|---|---|---|
| Locked state: drawn padlock + "Build a Breeding Pen in the City" + Go to City | drawn lock, text | pen illustration + CTA | shared locked-state card (K16) |
| Parent picker: grid of **text buttons with alien names** ("Blorp 1*"); jobs as text cards with a Collect button; newborn "Decide" modal with trait text | text | two parent portrait sockets with an egg/heart in the middle, portrait picker, newborn reveal | yes: pen banner 1080x400, egg sprite (3 stages) 256, parent socket frame (reuse S09), heart/DNA joiner 128; alien portraits D4; genes F5 (do not draw yet) |

### S13 Shard Reactor - building (opened from the City or Pulls). Status: PLAIN. `19b_reactor_locked.png`
Code: `ReactorScreen.cs`. Screenshot shows the empty state ("Place and finish building the Shard Reactor in the City first"). **[D6]** covers its empty state and selected chip colour; not re-reported.
Needs (new): reactor illustration 1080x400, shard icons (alien shard, blueprint shard, 5 rarity tints; count in K6), source -> target job card with arrow, progress ring. 2 new sprites + shard set.

### S14 Heroes - L18. Status: PARTIAL, **REDESIGN** (merge with S15). `25_heroes.png`
Code: `HeroesMountsScreens.cs`. Hero art is shown when it exists (`Art/Summon/Heroes/<id>`), else the worm art; locked heroes are a **black silhouette with a padlock** (verified in the screenshot).
| Element | Now | Replacement | New art? |
|---|---|---|---|
| "Summon events: get heroes and stars" purple full-width button | flat | banner CTA with the event art | kit |
| Hero cards: 160 px portrait frame, name, 2 text lines, "Details" button, Unlock / Star up / Lv / Equip buttons, "Get in Summon 0/50 shards" purple button | flat card + text; the silhouette stand-in | hero portrait cards, star row, shard progress ring | yes: 9 hero portraits 512x512 (`Art/Summon/Heroes/<id>`: none exist in the repo), star icons (shared), shard icon; trait/ability icons (reuse family icons) |
| Details modal (step lines "Step 3 Slot: text") | text | tiered ability list with icons | kit |

### S15 Mounts - L26. Status: PARTIAL. `26_mounts.png`
Same as S14 (`MountsScreen`): the owned mount shows a real slice mount sprite (Aegis Walker); locked ones are padlock silhouettes. New: 9 mount portraits 512x512 (mount art is **[D12]**, already queued), Fuel icon (K6).

### S16 Summon events (Hero / Mount), Exchange, Pass, Packs and Tasks sheets, odds - Hero L18, Mount L26. Status: PARTIAL (the best-designed menu, with its own plum kit in `SummonKit.cs`; all art is drawn fallbacks). `27_summon_event.png`, `28_summon_exchange.png`, `29_summon_pass.png`
Code: `SummonEventScreen.cs`, `SummonExchangeScreen.cs`, `SummonPassScreen.cs`, `SummonKit.cs`, `World/SummonArt.cs`.
| Element | Now | Replacement | New art? |
|---|---|---|---|
| Stage backdrop (purple hero / teal mount), spotlight, luck gem, sparkles | **drawn** (`SummonArt.DrawBackdrop`, "spotlight", "luck_gem") | painted stage backdrops | yes: 2 backdrops 1080x1100, spotlight 1, luck gem 1 (can be shared with S08 banner) |
| Side column (Exchange book, Packs pouch, Pass hourglass, Tasks clipboard) with badges | drawn icons | real icons | 4 icons 128 (also used as shop tab and quest icons) |
| Reward milestone track (5 small orange tiles) and reward tiles (orange squares) | drawn icons in flat tiles | K10 reward tile | kit |
| Pass screen (free/premium per day, 7 tiles), Exchange grid (3 columns) | flat plum cards | kit panels | kit |
| Featured hero/mount art, ticket / sigil / bolt icons | hero worm fallback, drawn tickets | hero/mount art S14/S15, currency icons | in S14, K6 |

### S17 Observatory - L44 (Invasions); also the hub's left "Events" icon. Status: PLAIN, **REDESIGN**. `31_observatory.png`
Code: `ObservatoryScreen.cs`.
| Element | Now | Replacement | New art? |
|---|---|---|---|
| Sections "Invasions / Planetary events / Upcoming (14 days) / Active modifiers": plain text with `Note` lines | text sheet | planet/sky banner header, event cards with art | yes: header banner 1080x360, 1 illustration per event type: invader saucer and comet are **drawn icons** (`SummonArt "invader"/"comet"`); event/invasion/enemy art is **[D12]** |
| Event rows: small drawn icon + name + brown countdown chip | flat rows | event card with art + countdown plate | queued D12 + kit |
| Invasion card: "Your contribution", Repelled / Overrun lines, Attack button | text card | boss HP bar illustration, reward preview | boss art D12; bar skin K9 |

### S18 Arena - L32. Status: PLAIN. `32_arena.png`, `33_arena_medal_shop.png`
Code: `ArenaScreen.cs`. Arena feature design is open (**[F1]**), so redesign is not re-reported; only art is listed.
| Element | Now | Replacement | New art? |
|---|---|---|---|
| League card ("Pebble League", Points / This week / Tickets / Medals as key-value text) | text card | league badge + rank crest | yes: one badge per league tier 192 (count = number of league tiers in `ContentDb.Arena`) |
| Opponent rows with a **drawn avatar disc** and a red Fight button; Leaderboard; Medal shop rows (text + grey price buttons, the featured "Awaken Core" in gold) | drawn avatar, flat rows | avatar frames, rank numbers, item icons | yes: avatar set 16 x 128 (shared with Guild chat), rank medals 1-3 + number plate, K6/K7 icons |
| "Offline: fighting local bots (power comparison placeholder)" | dev-only note (`Debug.isDebugBuild`) | none | none |

### S19 Guild - L42. Status: PLAIN (online only; offline shows "Guilds require online mode"). `34_guild_offline.png`
Code: `GuildScreen.cs`. Guild design is open (**[F2]**); art listed only. Online views (not screenshot-able offline; read from code): create form (two `Ui.Input` fields), search rows (Join), Members tab (text rows, role labels), Chat tab (text list, input, Send), Shop tab, Leave guild.
Needs: guild crest set 12 x 192 (create/search/members), 3 role badges 64, chat bubble 9-slice x2, input field frame (K15), shared avatars (S18). New if F2 is specified.

### S20 Settings - always (profile chip). Status: PLAIN, low value. `35_settings.png`
Code: `SettingsScreen.cs`. Mode toggle (Offline/Online), server URL input, name input, auto-mode note, animation button (hidden while D15), Developer card (dev builds only). Needs: kit only (K1, K2, K15) plus a gear icon. No dedicated art; the Developer card is not a player screen.

### S21 More menu (modal) - from the right column. Status: PLAIN. `36_more_menu.png`
`HubScreen` "More" = a `Ui.Modal` of **6 plain text buttons** (Laboratory, Heroes, Mounts, Aliens, Breeding Pen, Guild). Replace with an icon grid of the 6 destinations (icons from S11, S14, S15, S09, S12, S19; 6 x 128; same icons double as hub shortcuts).

### S22 Unlock moment and feature intro ("?") - every feature. Status: PARTIAL. `37_unlock_intro.png`
Code: `UnlockScreen.cs`, `World/IntroArt.cs`, `FeatureDef.Intro` (about 40 features, 118 art keys in `IntroArt.Fallbacks`).
| Element | Now | Replacement | New art? |
|---|---|---|---|
| Panel art (600x600 card) | `Art/Intro/` is empty: each key resolves to a **reused existing icon** (worm for parts/hero, the signature chest for every Laboratory package, hub icons for city/signals, drawn badges), many keys share one sprite | one illustration per feature | yes, lower priority: about 38 feature illustrations 512x512 (one per row of the GDD §16 table), plus 1 hand/pointer sprite (the glove is drawn, `IntroArt.Hand`) |
| Name ribbon, glow ring, dots, SKIP/NEXT buttons | code-drawn ribbon, flat buttons | ribbon sprite, kit buttons | 1 ribbon 9-slice |

### S23 Run result (Victory / Defeat) - every run. Status: OK. `43_run_result.png`
Real crown, banner, crest and rewards. Remaining placeholders: reward orbs for parts use a **sword icon** and for non-currency rewards a **"blessing" icon** (`RunResultScreen.cs:226`) -> D1 part icons and rune/blueprint icons; the purple Continue / orange Next buttons are flat kit buttons (K2).

### S24 Run overlays (events, vendors, chest, rest, level-up draft, signature pick, run shop, Skills list) - run phases. Status: OK. `40_run_event.png`, `41_run_shop.png`, `42_run_levelup.png`
Built with `SliceKit`/`SliceCards`/`RunScenes`: real characters, props, board backdrops, ribbons. Remaining: skill and outcome icons **[D5]**, status/enemy art **[D12]**, toast replacements **[D9]**. Nothing new.

### S25 Battle playback - every run. Status: OK (sprites, backdrop). Remaining: enemy/boss/status art **[D12]**; nothing new.

### S26 Mini-games (14: Portal Slip, Comet Catch, Saucer Landing, Clock Stopper, Wormhole Dash, Shield Parry, Slingshot Spore, Asteroid Slice, Jelly Stack, Signal Echo, Fossil Flip, Codebreaker Hatch, Odd Alien Out, Slot Pod) - L15 boards. Status: PLAIN (code shapes). `44_minigame_comet.png`
`MinigameRenderer` draws **flat shapes** (discs, rects, text) on a near-black background with a plain title/instruction header. Mini-game art is **[D12]** (already queued: whole backgrounds and sprites per game). Not re-reported beyond: shared mini-game frame (header plate, timer bar, result card) as kit.

### S27 Coach marks / tutorial hand, currency explainers (tap a chip), "All currencies" wallet sheet, stamina modal. Status: PLAIN overlays.
`CoachMarks.cs`, `ClientApp` (`ResourceBar.ShowCurrency`), `GearGuide`. Text modals with a drawn icon; covered by K6, K11 and the hand sprite in S22.

### S28 PlaceholderScreen - unused (`ScreenCatalog.Todo(...)` has no entries). Nothing to do.

## 2. Consolidated art queue

Priority: **P0** = unblocks the most screens or the daily-use screens (Worm, Hub, Shop, Quests); **P1** = high-visibility per screen;
**P2** = polish. "Shared" = used on 3 or more screens. Counts are source files, 9-slice where noted.
The recommended order is shared kit first (K1 to K16), then per-screen sets.

### A. Shared kit (do first)
| ID | Piece | Spec | Count | Shared by | Prio |
|---|---|---|---|---|---|
| K1 | Panel / card frames: card, raised/selected card, inset well (list row), modal box, banner plate | 9-slice PNG 256x256 (tintable, white with soft inner shading) | 5 | every screen | P0 |
| K2 | Button skins: primary (orange), secondary (brown), positive (green), danger (red), premium (cyan), accent (purple), gold (collect); each with a pressed and a disabled state | 9-slice 256x112 incl. lip | 7 colours x 3 states (or 3 tintable bases x 3 states = 9) | every screen (all `Ui.Button`) | P0 |
| K6 | Currency icon set (replaces all drawn icons): stamina, diamond, biomass, data, grain, ore, fuel, gear core, awaken core, advancement plate, arena ticket, invasion attempt, arena medal, guild mark, burrow key, hero XP, hero ticket, mount ticket, hero sigil, mount bolt, part token, alien token, blueprint token, 3 shard kinds (alien, blueprint, hero) | 128x128 transparent, render at 256 | 27 | top bar, chips, shop, mail, rewards, quests, run result, signals, city, lab, arena | P0 |
| K8 | Rarity frames and gems for tiles (Common, Rare, Epic, Legendary, Mythic; the 14-step part ladder tints these) | 9-slice 192x192 frame + 64 gem | 5 frames + 5 gems | part, alien, hero, reward, reveal, runes | P0 (needed before D1 icons are useful) |
| K3 | Tab bar: strip, active tab, inactive tab | 9-slice | 3 | Shop, Quests, Signals, Arena, Guild, Parts, Summon | P1 |
| K4 | Screen header: back button (arrow icon), title plaque, help "?" button | 9-slice + 2 icons 96 | 3 | every screen | P1 |
| K5 | Menu backgrounds: neutral dark-warm default, purple/space (Summon, City UI, Lab), plus the Shop/Quests gold variant | 1080x2340 or tileable 512 | 3 | all non-world screens | P1 |
| K9 | Progress bars: track + fill (tintable), thin and thick | 9-slice | 2 | Quests, Shop pass, Signals pity, Lab, Observatory, XP | P1 |
| K10 | Reward / item slot: square slot, quantity badge, NEW and FEATURED ribbons, claimed check overlay, locked overlay | 9-slice + 4 overlays | 6 | Mail, Quests, Summon, Reveal, Shop, Pass | P1 |
| K11 | Modal / sheet frame + dim + close "X" button (standard and celebratory) | 9-slice + 1 icon | 3 | all modals (about 20) | P1 |
| K7 | UI glyph icons replacing text glyphs: lock, check, star (filled/empty), plus, close, info, gear, arrow left/right, chest, envelope, wallet, video-ad, exchange, packs, pass, tasks, spotlight | 96x96 | 18 | all | P1 |
| K12 | Badges: red count dot, "NEW" pill, "SOON" ribbon, price tag pill, "PENDING" | 9-slice | 5 | Hub, Shop, Worm, Summon | P2 |
| K13 | Chip / pill backgrounds (currency chip, countdown chip, filter chip) | 9-slice | 3 | Hub bar, Observatory, Worm | P2 |
| K14 | Section header ornament + divider | 9-slice | 2 | all list screens | P2 |
| K15 | Input field frame, toggle/checkbox | 9-slice + 2 | 3 | Settings, Guild, Summon | P2 |
| K16 | Empty/locked state illustrations (empty list, locked feature, online required, error) | 512x512 | 4 | Mail, Breeding, Guild, Arena, Reactor | P2 |
| K17 | Stat icons (Hp, Atk, Def, Speed, Crit chance, Crit damage, Dodge, Combo, Counter, Lifesteal) | 64x64 | 10 | Worm, parts, Lab, heroes, mounts, aliens | P1 |
| K18 | Hub icon pack (13 icons, see S01); doubles as More-menu / intro / dock icons | 192x192 | 13 | Hub, More, intro art, shop | P0 |
| K19 | Slot silhouettes (Head, Body, Tail, Legs, Arm L/R) | 192x192 | 5-6 | Worm slots, filter chips, part tiles | P0 |

### B. Per-screen pieces (after the kit)
| Screen | Piece | Spec | Count | Prio | Note |
|---|---|---|---|---|---|
| Worm | Worm paperdoll base + 6 sockets | 800x800 | 1 | P0 | with K19 |
| Worm | Talent road: tier banner + node icons + road line | 96-512 | about 6 | P2 | |
| Hub | 5 planet stages | 1024x1024 | 5 | P2 | currently drawn |
| Portals | Level node medals (6) + tiling road | 256 | 7 | P1 | shared with Burrow / Dark Side |
| Quests | Chest set (3 tiers x 3 states) + glow | 192 | 10 | P1 | shared with Season pass and Funds |
| Quests | Quest category icons | 96 | 12 | P1 | |
| Quests | Calendar day tiles (3 states) + Day-7 chest + newbie banner | 9-slice, 256, 1080x300 | 5 | P2 | |
| Shop | Pack illustrations (diamond tiers 6, starters 2, gifts 3, monthly, forever 2, flash 2) | 256 | about 16 | P0 | queued as D12 (shop packs); this audit confirms all products share 4 stand-in icons |
| Shop | Header banner (reuse shopkeeper) + tab icons x6 | 1080x320 + 64 | 7 | P1 | |
| Shop / Summon | Pass track tiles (3 states) + premium lane | 9-slice | 4 | P1 | shared |
| Signals | 3 signal machine illustrations + banner plate | 1000x600 | 4 | P1 | star placeholder on every banner today |
| Aliens | Synergy / family icons | 64 | 10 | P1 | shared with skill icons D5 |
| Aliens / Breeding | Portrait socket frame, egg (3 stages), heart/DNA joiner, pen banner | 128-1080 | 6 | P2 | |
| City | Dock icons (3), toolbar icons (6), collect button skin | 64-96 | 9 | P1 | |
| Laboratory | Board background, node frames (3 kinds x 4 states), 6 package emblems, 3 connector sprites | mixed | 22 | P1 | needs the REDESIGN; D5 skill icons fill the nodes |
| Heroes | 9 hero portraits | 512 | 9 | P1 | none exist in the repo today |
| Mounts | 9 mount portraits | 512 | 9 | P1 | D12 |
| Summon | 2 stage backdrops, spotlight, luck gem, 4 side-column icons | mixed | 8 | P1 | currently drawn |
| Observatory | Header banner + one illustration per event type | 1080x360, 512 | 1 + event types | P2 | D12 for the invaders |
| Arena | League badges (per tier), 16 avatars, rank medals | 192 / 128 | tiers + 19 | P2 | F1 design open |
| Guild | 12 crests, 3 role badges, chat bubbles x2 | 192 / 64 | 17 | P2 | F2 design open |
| Runes | Rune icons | 96 | about 8 | P1 | |
| Unlock intros | One illustration per feature + hand + ribbon | 512 | about 38 + 2 | P2 | |
| Reactor | Illustration + shard rarity tints | 1080x400 | 1 + 5 | P2 | |
| More menu | (reuses K18) | | 0 | | |
| Mini-games | shared frame (header plate, timer bar, result card) | 9-slice | 3 | P2 | game art D12 |

### C. Already queued in the backlog (not new, listed for completeness)
D1 part icons (31 defs; this audit adds: there is no part-icon loader in the client, so a code slot is needed on every tile),
D3 building art, D4 alien sprites, D5 skill icons, D6 Shard Reactor UI, D7 rolling offer names, D9 toast replacements,
D12 enemy / boss / event / mount / minigame / status / currency / shop-pack art (note: **currency icons are in D12 and are the
only art here that every screen shows** - this audit recommends promoting them to P0), D14 dice pedestal, D16 camp, D15 animation.
F1 Arena and F2 Guilds screens are feature-design backlog items; their art needs are listed above but conditional on the spec.

Totals of new (not already queued) pieces: kit about 100 sprites (K1 to K19, most are 9-slice or small icons), per-screen about
200 (of which about 90 are the cheap icon sets: quest 12, stat 10, family 10, node frames 12, intro 38, avatar 16).

## 3. Redesign candidates (layout, not just reskin)

1. **Worm** (S03): the most-used screen has no body at all. Turn the slot grid into a worm paperdoll; make the inventory a
   dense icon grid (needs D1 icons) and move the long part detail into a sheet with icon, skills and actions.
2. **Laboratory** (S11): a pan/zoom tree drawn only with rectangles and text buttons. Needs a skill-tree board look (node
   frames, connectors, emblems); it is the one menu where the layout itself is the product.
3. **Shop** (S07): six tabs of identical text rows with the same stand-in icons; the Season pass and Funds tables read as spreadsheets.
   Needs a store front layout (hero offer, pack cards with art and "best value", reward tracks).
4. **Daily Quests** (S05): rows of text and bars; the chest track is the only illustrated part. Needs themed rows with
   category icons and a proper calendar.
5. **Aliens** (S09): one long wall of text per alien (about 12 lines). Needs a collection grid with a detail sheet.
6. **Breeding Pen / Shard Reactor** (S12, S13): both are button-and-label forms for what should be a visual "two parents -> baby"
   and "shards in -> shards out" machine.
7. **Observatory** (S17): a text report; wants an event board with art and boss HP.
8. **Heroes + Mounts** (S14, S15): two near-identical card lists; merge into one collection layout with portraits.
9. **More menu** (S21): a modal of 6 text buttons; becomes an icon grid.
10. Guild (S19) and Arena (S18): redesign belongs to feature specs F2 and F1.

## 4. Notes, stale entries and doc drift

* **D1 wording**: "30 parts have no dedicated icon" understates it: the client has no part-icon lookup at all (`ArtCatalog` has
  no part loader; `GearScreen` tiles render text only), so even an existing part icon could not show yet. All 31 `AddPartDef`
  parts are affected, not 30.
* **D12 wording**: currency icons are listed there but they are by far the most visible placeholder (every screen's top bar);
  suggest promoting them out of D12 as their own P0 item. All 27 ids are procedural fallbacks today.
* **D4**: 21 alien species in `ContentDb.Aliens`, 8 sprites on disk, so 13 lack a sprite (the backlog says 12; recount when the next batch lands).
* **Doc drift (not art)**: GDD §16 lists Laboratory at L13 and Part Signal at L10, but the code makes the Laboratory a building
  feature (unlocks when its blueprint is owned, `B(Features.Laboratory, ...)`) and `Features.PartPulls = Parts` (L1), so the Signals
  tab appears at L1. This audit follows the code.
* The Shop tab bar wraps "Diamonds" onto two lines (6 tabs); a layout bug fixed for free by icon tabs.
* Guild online views and the Observatory with an active invasion were not screenshot-able offline; they were read from code.
* Camp mode (L6 to L14) and the new-player hub were not re-shot (D16 covers camp; the existing `docs/client-screenshots/01_hub_new_player.png` is from 23 Nov).

## 5. How the screenshots were taken

A throw-away explicit PlayMode test (not committed) used `LocalGameService` with `DevUnlockAllFlag`, a starter kit, 30 cleared
levels, 20k Diamonds, 60k Grain/Ore, 50k Biomass, opened every `ScreenCatalog` entry plus the extra screens, clicked tabs and
dock buttons by name, and read the canvas into a PNG at 1080 x 2340 (then downscaled to 600 px). Run orders:
`arch -arm64 $U -batchmode -projectPath $P -runTests -testPlatform PlayMode -testFilter <Test>` (no `-nographics`). Run overlays
and mini-games came from the existing `ScreensShowcaseTests` and `MinigameShowcaseTests`.
