# Rogue Stars Unity client

Unity 6.6 (6000.6.10f1) project in `client/RogueStars`, Android (IL2CPP, ARM64, portrait). All UI is placeholder uGUI
built from code at runtime from one bootstrap scene; no art assets. The shared rules (`shared/RogueStars.Rules`) are
referenced as the local UPM package `com.roguestars.rules` and compile into `RogueStars.Rules.dll`.

## Layout
| Path | Assembly | What |
|---|---|---|
| `Assets/Scripts/Services/` | `RogueStars.Client` | `IGameService`, `LocalGameService`, `RemoteGameService`, HTTP transport, settings, store/ad gateways, DTOs |
| `Assets/Scripts/App/` | `RogueStars.Client` | `ClientApp` (composition root), `GameSession`, `UiScreen` + `ScreenRouter`, `ToastCenter`, `ResourceBar`, `ScreenCatalog`, `AppBootstrap` |
| `Assets/Scripts/Ui/` | `RogueStars.Client` | `Ui` toolkit (namespace `RogueStars.Client.UiKit`), `Theme`, `UiTween`, `SafeAreaFitter` |
| `Assets/Scripts/Screens/` | `RogueStars.Client` | One class per screen |
| `Assets/Editor/` | `RogueStars.Client.Editor` | `Bootstrap`, `ProjectSetup`, `BuildAndroid` |
| `Assets/Tests/EditMode/` | `RogueStars.Client.Tests` | EditMode tests |
| `Assets/Tests/PlayMode/` | `RogueStars.Client.PlayTests` | PlayMode playthrough + screenshots |
| `Assets/Resources/ServerConfig.asset` | – | Default server URL / online flag (ScriptableObject) |
| `Assets/link.xml` | – | Keeps rules/client types for Newtonsoft reflection under IL2CPP stripping |

## Architecture
```
AppBootstrap (only object in Main.unity)
 └─ ClientApp ── builds Canvas (1080×1920, ScaleWithScreenSize, match 0.5) → Background / SafeArea[TopBar, Screens, Modals, Toasts]
     ├─ GameSession ── wraps IGameService; Run(cmd), Purchase(productId), WatchAd(placement); events Changed / EventRaised / ErrorRaised / InfoRaised
     │    └─ IGameService = LocalGameService (offline) | RemoteGameService (online)
     ├─ ScreenRouter ── stack of UiScreen; Android back closes the top modal, then pops
     ├─ ToastCenter ── GameEvents (EventText: display names only, never uids/player ids), errors and info: max 2, 1.8 s, in a strip above the screen's bottom bar
     └─ ResourceBar ── main currencies + regen timers (live)
```
- **Same commands for both modes.** Screens only create rules command objects (`new CityUpgrade { BlueprintId = … }`)
  and call `Session.Run`. They read `Session.State`/`Content` and the rules' static query helpers
  (`CitySystem.UpgradeCost`, `QuestSystem.Visible`, `Features.List`, …) — no game logic in the client.
- **`LocalGameService`** (offline): own `ContentDb` instance with `Shop.AllowDevReceipts = true`, `GameEngine` with a
  Newtonsoft JSON `Cloner` (atomic commands), save file `Application.persistentDataPath/rogue_stars_save.json`
  (PascalCase fields like the server's storage format, collections replaced on load, atomic tmp-file write).
  `[ServerOnly]` commands run locally only here: purchases complete with `PurchaseComplete { ReceiptId = "dev:<token>" }`,
  arena fights run against local bots (`ArenaStartFight` + `ArenaFightResult`, power comparison) and invasion attacks
  roll local damage (`InvasionStartAttempt` + `InvasionRecordDamage`). Dev cheats (Settings): starter kit,
  unlock-all flag, reset save. Time-based state is settled every 5 s.
- **`RemoteGameService`** (online, docs/server.md): `POST /v1/auth/dev {deviceId}` → bearer token; `POST /v1/session`
  → state + server time offset + allowed commands; `POST /v1/command {type, requestId, args, expectedVersion?}`
  (args = the command's fields in camelCase, enums as numbers, dictionary keys untouched); every response with
  `state` replaces the local copy; `GET /v1/state` every 30 s. `[ServerOnly]` commands are refused locally with
  `server_only_command`. A `401` re-authenticates once and retries. Arena (`/v1/arena/*`), invasions
  (`/v1/invasions*`), guilds (`/v1/guilds*`, chat polling every 5 s) and IAP (`/v1/iap/verify`) map to DTOs in
  `ServiceTypes.cs`. HTTP goes through `IHttpTransport` (`UnityHttpTransport` = UnityWebRequest; tests use a fake).
- **Store / ads**: `IStoreGateway` (`FakeStoreGateway` returns `fake-…` tokens, which the server's fake IAP mode accepts)
  and `IAdGateway` (`FakeAdGateway`). TODO: Google Play Billing gateway (consume only after the server confirmed) and
  AppLovin MAX (online ad rewards arrive through the server's `/v1/ads/ssv` callback).
- **UI toolkit** (`Ui.*`): `Panel, Label, Button, Row, Column, Spacer, Input, Scroll, List, Grid, Bar, TabBar, Modal`,
  all layout-group based with `LayoutElement` sizes. `Theme` has the palette, rarity colours (Common→Mythic), valence
  colours (positive/negative/enemy/elite/boss/special) and per-currency colours. `UiTween` does pop-in/fade
  (play mode only), `SafeAreaFitter` keeps the UI inside `Screen.safeArea`.
- **Screens**: subclass `UiScreen`, implement `Title` and `Build(body)`; the body is rebuilt on every state change
  (`AutoRebuild`, off for screens with text inputs) and countdowns use `Live(text, () => …)` (ticked twice a second).
  `TimerControls(parent, timerId)` renders any rules timer with Diamonds / ad −15% / free speed-ups.

## Screens
| Screen | Feature gate | Notes |
|---|---|---|
| Hub (Planet) | – | main portals + PLAY / Continue run, **next goal** card (tutorial), key buildings, live-ops ticker, unlock celebrations (`FeatureCelebrate`), navigation grid from `ScreenCatalog`; locked features show their requirement text/progress |
| Portals | – (modes: `overcharged`, `burrow`, `dark_side`) | Main / Overcharged / Burrow / Dark Side tabs, level grid with 3 feats (★), level modal (threat, boss, dice/rolls, modifiers, feats, completion rewards, entry cost), stamina refill modal (daily free / ad / Diamonds) |
| Run | – | snake board (8 columns, 3 lanes per band) of tiles coloured by valence with letter icons (E enemy, EL elite, MB miniboss, $ loot, T treasure, ? event, C campfire, S shop, ! hazard, B boss tile, BOSS gate…), edges, branch highlight, boss marker ("BOSS n%", placed in a free cell next to the gate, never on a tile), worm token animated along the trail, dice roll animation; fixed bottom bar (dice + last landing, ROLL/Auto, Decision/Abandon) with a reserved toast strip above it; HUD: HP, run level/XP, awakening meter, coins, shards, danger, pending loot, skills; decision modals: branch (path preview), event/campfire/treasure (options with visible risk text and costs), draft 1-of-3 (family colours, rarity, reroll), shop (buy, reroll, leave), revive (ad / Diamonds / give up), Burrow (descend / cash out); **auto mode**: hold ROLL (or Auto) → `RunAutoStep` loop with modals shown briefly, any tap stops it |
| Battle playback | – | animates `LastBattle`: placeholder sprites (W worm, A alien, M mount, E/EL/B enemies), HP / shield / rage bars (values straight from the log entries: `TargetHp/TargetShield/TargetRage`, `ActorShield/ActorRage`, fighters' `StartShield/StartRage`), status badges, floating damage/heal numbers, CRIT/DODGE/PARRY/RESIST/STUNNED/KO popups, round counter, battle log, speed ×1/×2 (×3 needs the monthly pass) |
| Run result | – | victory/defeat, rolls, run level, new feats, granted rewards (or lost pending loot), **double rewards** (rewarded ad, instant with No Ads → `RunDoubleRewards`, daily cap shown), Again / Portals |
| Worm | – | level up (×1/×10, Biomass), power and stats, six body slots with equip modal + "Equip best parts", talent road (ranks, tier completion, perk) |
| Parts | `parts` | inventory with slot filters, quick merge, detail modal: stats/skills, level up / cap raise / level reset, equip (left/right arm), merge to next rarity (materials picked automatically, validated with `PartsSystem.ValidateMerge`), rune sockets, Forge, S/SS awakening, dismantle; Runes tab: lock, merge 3→1 |
| Signals | `part_pulls` (+ per type) | Part / Blueprint / Alien Signals, featured mythic banner with countdown vs standard pool, published odds, 1×/10× with tokens or Diamonds, daily ad pull, reveal animation |
| Laboratory | `laboratory` | research lines (cost, duration, timer with speed-ups, collect), draft-pool toggles |
| Heroes / Mounts | `heroes` / `mounts` | unlock from shards, stars, levels (Hero XP / Fuel), awakening (mounts), equip, trait/weapon skills by star |
| City | – | 12×12 grid with areas (locked areas labelled), place/move/store buildings, upgrade with timers + speed-ups, collect (one/all), production priority, blueprint star-up, workers, area unlock |
| Aliens | `aliens` | list by rarity, stats, level/star up, assign to building, squad toggle |
| Breeding Pen | `breeding` | pick parents (cost/duration preview), jobs with timers, collect, replace/discard modal comparing genes |
| Observatory | – | one invasion list (`ObservatoryScreen.MergeInvasions`: rules calendar occurrences matched by `occurrenceId` with the service's attackable instances, server-pushed extras appended with their content names) with Attack per invasion, planetary events, upcoming, modifier stack with countdowns |
| Quests | `quests` | daily/weekly with point chests, achievements, 7-day login calendar, new-player event |
| Shop | – | offers (starter/rolling/flash/gift), Diamond packs, monthly pass, season pass, funds, permanent, Diamond items, rewarded ads |
| Mail | – | inbox, open/read, claim, claim all, delete |
| Arena | `arena` | league/points/tickets, opponents + fight result modal, weekly leaderboard |
| Guild | `guilds` | online only (offline shows "requires online"): create/search/join, members by weekly invasion damage, chat, guild shop, leave |
| Settings | – | offline/online, server URL, display name, reset save, dev cheats (unlock all features) |

**Adding a screen:** write a `UiScreen` subclass in `Assets/Scripts/Screens/` and add a `ScreenEntry` to
`ScreenCatalog.All` (`Todo(...)` creates a placeholder). The hub, navigation gating and the smoke tests pick it up
automatically. Extend `Fmt.Reward` / `EventText` for new reward kinds and events.

**Onboarding / tutorial** (`App/Tutorial.cs`): no forced popups. Content tutorial steps are completed automatically
(`TutorialAdvance`) as soon as the player has done the thing (counters/state), and the hub shows the next goal with a
Go button; before the first run it asks to equip the starter parts, and when nothing is actionable it points at the
next feature unlock. Unlocks, goals and rewards appear as toasts (at most two, 1.8 s, never blocking input) in a strip just above the
screen's fixed bottom bar (`UiScreen.FooterHeight`/`ReserveToastStrip`): the Run screen keeps that strip free between
the board and its ROLL/Decision bar, so toasts never cover the board.

## Running
- **Offline (default):** open `client/RogueStars` in Unity 6.6, open `Assets/Scenes/Main.unity`, press Play
  (Game view in portrait, e.g. 1080×1920). A new save is playable from the start (starter kit from content); Settings → *Unlock all features* is a dev cheat.
- **Online:** start the server (`cd server/RogueStars.Server && arch -arm64 ~/.dotnet/dotnet run`, dev auth on :8080),
  then Settings → server URL `http://localhost:8080` (editor) or `http://10.0.2.2:8080` (Android emulator) →
  *Online*. The defaults come from `Assets/Resources/ServerConfig.asset`; Settings values are stored in PlayerPrefs.
  Plain http is allowed in development builds only (`insecureHttpOption = DevelopmentOnly`).

## Batchmode (Apple Silicon)
```bash
U=/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity
P=$PWD/client/RogueStars            # absolute paths only, one Unity instance per project
arch -arm64 $U -batchmode -nographics -quit -projectPath $P -logFile /tmp/rs-scene.log -executeMethod RogueStars.Client.Editor.Bootstrap.CreateScene
arch -arm64 $U -batchmode -nographics -quit -projectPath $P -logFile /tmp/rs-setup.log -executeMethod RogueStars.Client.Editor.ProjectSetup.Apply
arch -arm64 $U -batchmode -nographics -projectPath $P -logFile /tmp/rs-tests.log -runTests -testPlatform EditMode -testResults /tmp/rs-tests.xml
arch -arm64 $U -batchmode -nographics -quit -buildTarget Android -projectPath $P -logFile /tmp/rs-build.log -executeMethod RogueStars.Client.Editor.BuildAndroid.Build
```
`BuildAndroid.Build` applies `ProjectSetup`, creates the scene if missing, sets version `0.1.<git commit count>`
(versionCode = commit count; `0.1.0`/1 without git) and writes `client/RogueStars/Builds/RogueStars.apk`
(package `com.roguestars.game`, IL2CPP, ARM64, minSdk 26, portrait). It exits with code 1 on failure.

## Tests
EditMode (`Assets/Tests/EditMode`, 48 tests):
- `LocalGameServiceTests`: new player, command success/failure atomicity, offline dev-receipt purchase, lossless
  save/load (incl. ulong RNG state), reset, timers settling with a manual clock, local arena.
- `RemoteGameServiceTests` (fake transport): dev auth + session mapping, command body (type/requestId/camelCase args,
  numeric enums), response mapping (events/payload/version/state), rule rejection, HTTP/network errors, server-only
  refusal, 401 re-auth, IAP verify, arena, invasion (occurrence/type + Observatory merge) and guild DTOs.
- `RouterTests`: root, push/pop visibility and lifecycle, rebuild on state change, reset, back closes modals, toasts.
- `ScreenSmokeTests`: every catalog screen for a fresh and a populated player, tab clicking, city building details,
  guild offline message, no placeholders left.
- `RunAndProgressionScreenTests`: a new player plays level 1 end to end through the Run screen (battle playbacks,
  result screen, tutorial steps), decision modals per phase, board layout and boss marker never overlap tiles (all main levels × 5 seeds), toast strip/limit, event toasts
  without internal ids, double rewards,
  battle playback ends at the logged HP, rage/shield bars equal the logged values after every entry, Signals reveal, merge-material finder, worm/hero actions.
- `RunSeedSweepTests`: 100 fresh saves (seeds 1–100, with and without starter parts) play level 1 like the playthrough
  (manual roll, then `RunAutoStep`) to completion within a 200-decision budget, every auto decision accepted
  (failure names seed, last decision and run state); same seed + clock replay the same run.

`LocalGameService(dir, id, name, clock, randomSeed)`: with a `randomSeed` the new player's rules RNG is derived from
it only (no wall clock), so a seeded service on a `ManualClock` plays out identically every time.

PlayMode (`Assets/Tests/PlayMode`, needs a graphics device — run WITHOUT `-nographics`):
- `PlaythroughTests.FreshSave_Playthrough_WithScreenshots` boots `Main.unity`, plays a fresh offline save (equip
  starter parts → first run with a manual roll, then auto mode to the boss → result → hub/worm/parts → city build →
  dev unlock → 10× Signal with reveal → stamina refill → every catalog screen) and renders the canvas through a
  1080×1920 RenderTexture camera into `client/RogueStars/Screenshots/` (gitignored). A curated, downscaled set lives
  in `docs/client-screenshots/`. Deterministic: fixed seed (11) and game clock (2026-03-02 12:00 UTC), a fresh save
  directory per run. Its first manual roll lands on a battle; a playback opened outside auto mode waits for
  Continue, which the test presses. The auto run is bounded by game decisions (400 commands) and a stall guard (no
  state/screen change for 900 frames and 30 s) whose message carries the last result and the run/UI state.
```bash
arch -arm64 $U -batchmode -projectPath $P -logFile /tmp/rs-play.log -runTests -testPlatform PlayMode -testResults /tmp/rs-play.xml
```
(`WaitForEndOfFrame` never fires in batchmode, so the capture camera is rendered by hand.)

## Known gaps / TODO
- Online rewarded ads that grant rewards directly arrive through the server's SSV callback; ad tokens sent with
  `RunRevive`, `BuyStamina`, `GachaPull` are fake until the AppLovin gateway exists.
- Firebase auth, Play Billing, AppLovin MAX and push notifications are interfaces/TODOs.
