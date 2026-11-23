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
     └─ ResourceBar ── main currencies + regen timers (live); Data / Arena Medals / Awaken Cores in the first row, "All" opens every balance
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
- **Store** (`Services/PlatformGateways.cs`, `UnityIapStoreGateway.cs`): `IStoreGateway` = `UnityIapStoreGateway`
  (Google Play Billing through **Unity IAP 5.5.0**, `com.unity.purchasing`, StoreController API) on devices or
  `FakeStoreGateway` (editor, tests, `ROGUESTARS_STORE=fake`; `fake-…` tokens for the server's fake IAP mode).
  Catalogue = rules products with a SKU (`StoreCatalog`, same rule as the server's `PlayCatalog`: lifetime limit 1 →
  non-consumable, else consumable; the monthly pass is a 30-day consumable, no subscriptions). Unity IAP gets
  `SetObfuscatedAccountId(hex(sha256("rs-account:" + playerId)))`. Flow (`GameSession.Purchase`): `PurchaseStart` →
  store purchase (`OnPurchasePending`) → `POST /v1/iap/verify {productId, purchaseToken}` → `ConfirmPurchase` only after
  the server granted (the server already consumed/acknowledged at Google). Deferred payments show "payment pending";
  when Play completes them, restarts before verification and owned non-consumables (restore via `FetchPurchases`) come
  back through `PurchaseDelivered` and take the same verification path; rejected receipts are never confirmed.
- **Ads** (`MaxAdGateway.cs`, `ServerVerifiedAdGateway` in `GameSession.cs`): `Session.PlatformAds` = AppLovin **MAX
  Unity plugin 8.6.6** (official `.unitypackage` from AppLovin's GitHub releases, with External Dependency Manager
  1.2.186; `Assets/MaxSdk`, `Assets/ExternalDependencyManager`) or `FakeAdGateway`. Before each show
  `MaxSdk.SetUserId(playerId)`, then `ShowRewardedAd(adUnit, placement, customData: placement)`. Screens use
  `Session.Ads` (`ServerVerifiedAdGateway`): offline the platform token; online the server grants direct-reward
  placements from AppLovin's S2S callback (`WatchAd` shows "pending" and refreshes until the placement's ad view count
  moved), and command placements (revive, stamina, timer, pulls, double rewards) poll `GET /v1/ads/credits` until the
  verified event id is there and send it as `AdToken`. No Ads → no ad, empty token. With the fake ad gateway online, the
  client plays the callback itself against a dev server in fake ads mode.
- **Platform config** (`Assets/Resources/PlatformConfig.asset`): `StoreMode` (`unity_iap`/`fake`), `AdsMode` (`max`/`fake`),
  `MaxRewardedAdUnitId` — committed with placeholders; the MAX SDK key lives in `Assets/MaxSdk/Resources/AppLovinSettings.asset`
  (committed empty). `BuildAndroid` injects real values from environment variables for one build and restores the
  placeholders (`Editor/BuildSecrets.cs`; see docs/setup.md step 11). Without an ad unit id the build uses fake ads; without
  an SDK key the AppLovin Quality Service plugin stays off (it would call AppLovin).
- **Android dependencies**: custom Gradle templates in `Assets/Plugins/Android` (main, settings, properties with AndroidX).
  `BuildAndroid` runs EDM's `PlayServicesResolver.ResolveSync`, which writes `com.applovin:applovin-sdk` into
  `mainTemplate.gradle`; Unity IAP adds `com.android.billingclient:billing` itself. Gradle downloads both (network).
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
| Portals | – (modes: `overcharged`, `burrow`, `dark_side`) | Main / Overcharged / Burrow / Dark Side tabs, level grid with 3 feats (★), level modal (threat, boss, dice/rolls, modifiers, feats, completion rewards, entry cost), stamina refill modal (daily free / ad / Diamonds), **daily Biomass surge** counter (`RunSystem.BiomassBonusRunsLeft`); the procedural Dark Side (150 levels) is paged 25 at a time around the next level to clear, each level showing its Data |
| Run | – | snake board (8 columns, 3 lanes per band) of tiles coloured by valence with letter icons (E enemy, EL elite, MB miniboss, $ loot, T treasure, ? event, C campfire, S shop, ! hazard, B boss tile, BOSS gate…), edges, branch highlight, boss marker ("BOSS n%", placed in a free cell next to the gate, never on a tile), worm token animated along the trail, dice roll animation; fixed bottom bar (dice + last landing, ROLL/Auto, Decision/Abandon) with a reserved toast strip above it; HUD: HP, run level/XP, awakening meter, coins, shards, danger, pending loot, skills; decision modals: branch (path preview), event/campfire/treasure (options with visible risk text and costs), draft 1-of-3 (family colours, rarity, reroll), shop (buy, reroll, leave), revive (ad / Diamonds / give up), Burrow (descend / cash out); **auto mode**: hold ROLL (or Auto) → `RunAutoStep` loop with modals shown briefly, any tap stops it |
| Battle playback | – | animates `LastBattle`: placeholder sprites (W worm, A alien, M mount, E/EL/B enemies), HP / shield / rage bars (values straight from the log entries: `TargetHp/TargetShield/TargetRage`, `ActorShield/ActorRage`, fighters' `StartShield/StartRage`), status badges, floating damage/heal numbers, CRIT/DODGE/PARRY/RESIST/STUNNED/KO popups, round counter, battle log, speed ×1/×2 (×3 needs the monthly pass) |
| Run result | – | victory/defeat, rolls, run level, new feats, granted rewards (or lost pending loot), "Biomass surge ×4 applied" + surge runs left, **double rewards** (rewarded ad, instant with No Ads → `RunDoubleRewards`, daily cap shown), Again / Portals |
| Worm | – | level up (×1/×10, Biomass), power and stats, six body slots with equip modal + "Equip best parts", talent road (ranks, tier completion, perk) |
| Parts | `parts` | 14-step ladder (Common … Mythic, Mythic+1/+2, Mythic S, S+1/+2, Mythic SS, SS+1/+2; `Theme.PartRarityColor`), inventory with slot filters (equipped first, then rarity), quick merge (asks first if locked parts below Legendary exist); detail modal: stats, the five skills locked/unlocked by rarity (Epic/Legendary/Mythic/Mythic S/Mythic SS), level up / cap raise / level reset, equip, **lock** (client-side `PartLocks`, PlayerPrefs per player), rune sockets, Forge, dismantle (confirm for Legendary+/locked); **merge picker**: own-def copies (needed/owned), fodder points and LE, Ore, candidate list with add/remove, auto-select of the best fodder (`PartPlanner`: cheapest set without superfluous parts, other defs first; locked, Mythic+ and Legendary+ copies of equipped defs only as a fallback that needs a confirmation; equipped/awakened/forged never), checked by `PartsSystem.ValidateMerge`; **Replicator** (Mythic+ parts: 10 LE of other-def Legendary+ surplus + Ore → Legendary copy, `ValidateReplicate`); **awakening** (Mythic+, once, 50 Awaken Cores): preview of the new name, stats before → after and the five skills swapped; Runes tab: lock, merge 3→1 |
| Signals | `part_pulls` (+ per type) | Part / Blueprint / Alien Signals, featured mythic banner with countdown vs standard pool, published odds, 1×/10× with tokens or Diamonds, daily ad pull, reveal animation |
| Laboratory | `laboratory` | header: Data, next research cost/duration (`LaboratorySystem.NextDataCost`/`NextDurationMs`, Annex ×2), slots busy, Research Annex requirement/staffing (`LaboratoryScreen.AnnexStatus`); active research slots (main/Annex) with Diamonds/ad/free speed-ups and collect; **tree** per branch (offense/defense/elements/command, `LabTreeLayout`: one row per tier, nodes ordered by their prerequisites, no overlaps): stat nodes steel-edged, skill nodes family-coloured with a SKILL band and on/off, edges gold = ALL prerequisites, thin = ANY; node modal: effect now/next, ANY/ALL prerequisites (other branches named), research button with the block reason, draft-pool toggle for skill nodes |
| Heroes / Mounts | `heroes` / `mounts` | unlock from shards, stars, levels (Hero XP / Fuel), awakening (mounts), equip, trait/weapon skills by star |
| City | – | 12×12 grid with areas (locked areas labelled), place/move/store buildings, upgrade with timers + speed-ups, collect (one/all), production priority, blueprint star-up, workers, area unlock; staffed boons (Research Annex): banner while unbuilt/unstaffed and "needs 2 alien workers of 3★+ (staffed n/2)" in the details |
| Aliens | `aliens` | list by rarity, stats, level/star up, assign to building, squad toggle |
| Breeding Pen | `breeding` | pick parents (cost/duration preview), jobs with timers, collect, replace/discard modal comparing genes |
| Observatory | – | one invasion list (`ObservatoryScreen.MergeInvasions`: rules calendar occurrences matched by `occurrenceId` with the service's attackable instances, server-pushed extras appended with their content names) with Attack per invasion, planetary events, upcoming, modifier stack with countdowns |
| Quests | `quests` | daily/weekly with point chests, achievements, 7-day login calendar, new-player event |
| Shop | – | offers (starter/rolling/flash/gift), Diamond packs, monthly pass, season pass, funds, permanent, Diamond items, rewarded ads |
| Mail | – | inbox, open/read, claim, claim all, delete |
| Arena | `arena` | league/points/tickets/Arena Medals, opponents + fight result modal, weekly leaderboard, **medal shop** (`ShopBuyItem`, section `arena`: Awaken Core 200 medals 2/week first, part tokens, Data, Gear Cores; bought n/limit this week, reset countdown, "Sold out"); the Diamond shop no longer lists arena/guild items |
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
`BuildAndroid.Build` applies `ProjectSetup`, creates the scene if missing, injects build-time values
(`ROGUESTARS_SERVER_URL`, `ROGUESTARS_STORE`, `ROGUESTARS_ADS`, `ROGUESTARS_MAX_SDK_KEY`, `ROGUESTARS_MAX_REWARDED_AD_UNIT`,
`ROGUESTARS_KEYSTORE`/`_KEYSTORE_PASS`/`_KEY_ALIAS`/`_KEY_PASS`, `ROGUESTARS_AAB=1` for an app bundle), resolves the Android
dependencies, sets version `0.1.<git commit count>` (versionCode = commit count; `0.1.0`/1 without git) and writes
`client/RogueStars/Builds/RogueStars.apk` (or `.aab`; package `com.oddbyte.roguestars`, IL2CPP, ARM64, minSdk 26, portrait).
It exits with code 1 on failure.

## Tests
EditMode (`Assets/Tests/EditMode`, 73 tests):
- `PlatformTests`: store catalogue = server catalogue (types, SKUs, obfuscated account id vector), fakes in the editor,
  purchase → grant → confirm, deferred purchase delivered later, cancelled purchase, rejected/pending receipts never
  confirmed + restore, server-verified ads (credit polling, timeout, direct-reward pending → refresh, No Ads, fake ads
  playing the dev callback).
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
- `PartsLabArenaScreenTests`: best-fodder picker (covers the need, no superfluous part, cheapest set), Mythic merge
  with copy + 2 LE auto-selected and executed, equipped parts never offered, locked parts only with a confirmation,
  manual selections checked by the rules, Replicator, five skills per rarity, awakening preview (name/stats/skills) and
  awakening, lab tree layout without overlaps (all branches, all 38 nodes, edges downwards), research from the node modal
  at `NextDataCost`/`NextDurationMs` with a busy slot, skill toggles + ANY/ALL prerequisites, Research Annex status and
  City banner, arena shop weekly core limit, resource bar chips/details, Biomass surge + Dark Side paging.
- `RunSeedSweepTests`: 100 fresh saves (seeds 1–100, with and without starter parts) play level 1 like the playthrough
  (manual roll, then `RunAutoStep`) to completion within a 200-decision budget, every auto decision accepted
  (failure names seed, last decision and run state); same seed + clock replay the same run.

`LocalGameService(dir, id, name, clock, randomSeed)`: with a `randomSeed` the new player's rules RNG is derived from
it only (no wall clock), so a seeded service on a `ManualClock` plays out identically every time.

PlayMode (`Assets/Tests/PlayMode`, needs a graphics device — run WITHOUT `-nographics`):
- `PlaythroughTests.FreshSave_Playthrough_WithScreenshots` boots `Main.unity`, plays a fresh offline save (equip
  starter parts → first run with a manual roll, then auto mode to the boss → result → hub/worm/parts → city build →
  dev unlock → 10× Signal with reveal → stamina refill → parts ladder, part detail with skills, Mythic merge picker,
  awakening preview → lab tree with a running research + a capstone node → arena medal shop → Dark Side → every catalog screen) and renders the canvas through a
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
- Firebase Auth (Play Games / anonymous sign-in, `google-services.json`) and push notifications are not integrated:
  the client signs in with `/v1/auth/dev`, which a production server (`Auth:Mode=firebase`) refuses (docs/setup.md step 3).
- Mediation network adapters are not installed (AppLovin's own demand only); add them with the AppLovin Integration Manager.
