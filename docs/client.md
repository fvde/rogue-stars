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
| `Assets/Resources/ServerConfig.asset` | – | Default server URL / online flag (ScriptableObject) |
| `Assets/link.xml` | – | Keeps rules/client types for Newtonsoft reflection under IL2CPP stripping |

## Architecture
```
AppBootstrap (only object in Main.unity)
 └─ ClientApp ── builds Canvas (1080×1920, ScaleWithScreenSize, match 0.5) → Background / SafeArea[TopBar, Screens, Modals, Toasts]
     ├─ GameSession ── wraps IGameService; Run(cmd), Purchase(productId), WatchAd(placement); events Changed / EventRaised / ErrorRaised / InfoRaised
     │    └─ IGameService = LocalGameService (offline) | RemoteGameService (online)
     ├─ ScreenRouter ── stack of UiScreen; Android back closes the top modal, then pops
     ├─ ToastCenter ── GameEvents (EventText), errors and info as fading toasts
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
| Hub (Planet) | – | portals placeholder, key buildings, live-ops ticker, unlock celebrations (`FeatureCelebrate`), navigation grid from `ScreenCatalog` with locked features showing their requirement text/progress |
| City | – | 12×12 grid with areas, place/move/store buildings, upgrade with timers + speed-ups, collect (one/all), production priority, blueprint star-up, workers, area unlock |
| Aliens | `aliens` | list by rarity, stats, level/star up, assign to building, squad toggle |
| Breeding Pen | `breeding` | pick parents (cost/duration preview), jobs with timers, collect, replace/discard modal comparing genes |
| Observatory | – | active/upcoming invasions and planetary events, modifier stack with countdowns, invasion attacks |
| Quests | `quests` | daily/weekly with point chests, achievements, 7-day login calendar, new-player event |
| Shop | – | offers (starter/rolling/flash/gift), Diamond packs, monthly pass, season pass, funds, permanent, Diamond items, rewarded ads |
| Mail | – | inbox, open/read, claim, claim all, delete |
| Arena | `arena` | league/points/tickets, opponents + fight result modal, weekly leaderboard |
| Guild | `guilds` | online only (offline shows "requires online"): create/search/join, members by weekly invasion damage, chat, guild shop, leave |
| Settings | – | offline/online, server URL, display name, reset save, dev cheats |
| Portals, Laboratory, Worm, Parts, Heroes, Mounts, Signals | various | `PlaceholderScreen` + `TODO(merge Run/Progression)` |

**Adding screens after the Progression/Run merge:** write a `UiScreen` subclass in `Assets/Scripts/Screens/` and
replace the `Todo(...)` entry in `ScreenCatalog.All` with a real `ScreenEntry`. The hub, navigation gating and the
smoke test pick it up automatically. Also extend `Fmt.Reward` / `EventText` for new reward kinds and events.

## Running
- **Offline (default):** open `client/RogueStars` in Unity 6.6, open `Assets/Scenes/Main.unity`, press Play
  (Game view in portrait, e.g. 1080×1920). Settings → *Grant starter kit* / *Unlock all features* for a populated city.
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

## Tests (EditMode)
- `LocalGameServiceTests`: new player, command success/failure atomicity, offline dev-receipt purchase, lossless
  save/load (incl. ulong RNG state), reset, timers settling with a manual clock, local arena.
- `RemoteGameServiceTests` (fake transport): dev auth + session mapping, command body (type/requestId/camelCase args,
  numeric enums), response mapping (events/payload/version/state), rule rejection, HTTP/network errors, server-only
  refusal, 401 re-auth, IAP verify, arena and guild DTOs.
- `RouterTests`: root, push/pop visibility and lifecycle, rebuild on state change, reset, back closes modals, toasts.
- `ScreenSmokeTests`: every catalog screen for a fresh and a populated player, tab clicking, city building details,
  guild offline message.

## Known gaps / TODO
- Progression and Run screens are placeholders until those workstreams are merged (see `ScreenCatalog`).
- Stamina regen/timer shows once a `RegenRule` for stamina exists in content (Progression).
- Online invasions: the server's `/v1/invasions` instances are not the rules' calendar occurrence ids; the Observatory
  shows both (calendar from `LiveOpsSystem`, attackable instances from the server).
- Firebase auth, Play Billing, AppLovin MAX and push notifications are interfaces/TODOs.
