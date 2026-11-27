# Rogue Stars Unity client

Unity 6.6 (6000.6.10f1) project in `client/RogueStars`, Android (IL2CPP, ARM64, portrait). All UI is uGUI
built from code at runtime from one bootstrap scene; rounded shapes are generated at runtime, one font asset, and the
generated game art (hero worm, enemy and alien sprites, board grounds, tile slabs, decor props) is loaded through `ArtCatalog` with procedural fallbacks. The shared rules (`shared/RogueStars.Rules`) are
referenced as the local UPM package `com.roguestars.rules` and compile into `RogueStars.Rules.dll`; the simulator's bot
(`shared/RogueStars.Sim.Core`, local package `com.roguestars.simcore`, assembly `RogueStars.Sim.Core`) runs the offline
*Jump to day* dev tool.

## Layout
| Path | Assembly | What |
|---|---|---|
| `Assets/Scripts/Services/` | `RogueStars.Client` | `IGameService`, `LocalGameService`, `RemoteGameService`, HTTP transport, settings, store/ad gateways, DTOs |
| `Assets/Scripts/Services/Auth/` | `RogueStars.Client` | `IAuthProvider`, `FirebaseAuthProvider` (flow), `FirebaseSdkBackend` (Firebase Unity SDK), `GooglePlayGamesSignIn` (Play Games plugin), `AuthFactory` |
| `Assets/GooglePlayGames/` | `Google.Play.Games` | Google Play Games plugin for Unity v2.3.0 (official `.unitypackage`, without its bundled EDM copy) |
| `Packages/google/*.tgz` | – | Firebase App/Auth 14.2.0 + External Dependency Manager 1.2.190 from Google's archive, fetched by `scripts/fetch-unity-packages.sh` (git-ignored, SHA-256 pinned) |
| `Assets/Plugins/Android/RogueStarsConfig.androidlib` | – | manifest additions: Play Billing permission, Firebase Analytics off, `AD_ID` removed while ads are disabled |
| `Assets/Scripts/App/` | `RogueStars.Client` | `ClientApp` (composition root), `GameSession`, `UiScreen` + `ScreenRouter`, `GameFeedback` + `FeedbackFx`, `ResourceBar`, `ScreenCatalog`, `AppBootstrap`, `GearGuide` / `CurrencyGuide` (inventory filters, part level-up affordability, currency uses → screens) |
| `Assets/Scripts/Ui/` | `RogueStars.Client` | `Ui` toolkit (namespace `RogueStars.Client.UiKit`), `Theme`, `UiShapes` (runtime rounded sprites), `UiMotion` (`UiTweens`, `UiBob`, `UiPulse`), `UiTween`, `UiSequence`, `SafeAreaFitter` |
| `Assets/Resources/Fonts/` | – | `Fredoka-Bold.ttf` (display font: static wght 700 instance of Fredoka[wdth,wght] from the google/fonts repo, made with fontTools `varLib.instancer`) + `Fredoka-OFL.txt` (SIL Open Font License 1.1) |
| `Assets/Art/Resources/Art/` | – | Our own generated art, imported as UI sprites by `Editor/ArtImportSettings` (no mip maps, full rect) and copied in by `Tools/Art/Import Generated Art` (`Editor/ArtImporter.cs`): `Hero/hero_worm.png` (512 px, transparent), `Enemies/<enemy id>.png`, `Aliens/<alien id>.png` (512 px, transparent), `Boards/<biome>/background.png` (768×1344 top-down full-frame ground), `Boards/<biome>/ground.png` (seamless ground, optional), `Boards/<biome>/tiles/slab.png` / `<kind>.png` (tile sprites), `Decor/<biome>/*.png` (decor props); looked up only through `Ui/ArtCatalog.cs` |
| `Assets/Scripts/Screens/` | `RogueStars.Client` | One class per screen (`GearScreen` = shared base of Worm and Parts: inventory, part detail, merge picker, awakening, runes) |
| `Assets/Scripts/Minigames/` | `RogueStars.Client` | Run mini-games: `Logic/` (one Unity-free class per game + `MinigameLogic` base + `MinigameCatalog`), `MinigameRenderer` (shapes → pooled uGUI nodes), `MinigameHost` (the full-screen flow) |
| `Assets/Editor/` | `RogueStars.Client.Editor` | `Bootstrap`, `ProjectSetup`, `BuildAndroid` |
| `Assets/Tests/EditMode/` | `RogueStars.Client.Tests` | EditMode tests |
| `Assets/Tests/PlayMode/` | `RogueStars.Client.PlayTests` | PlayMode playthrough + screenshots |
| `Assets/Resources/ServerConfig.asset` | – | Default server URL / online flag (ScriptableObject) |
| `Assets/link.xml` | – | Keeps rules/client types for Newtonsoft reflection under IL2CPP stripping |

## Architecture
```
AppBootstrap (only object in Main.unity)
 └─ ClientApp ── builds Canvas (1080×1920, ScaleWithScreenSize, match 0.5) → Background / SafeArea[TopBar, Screens, Modals, FeedbackLayer, CoachLayer]
     ├─ GameSession ── wraps IGameService; Run(cmd), Purchase(productId), WatchAd(placement); events Changed / EventRaised / ErrorRaised / InfoRaised
     │    └─ IGameService = LocalGameService (offline) | RemoteGameService (online)
     ├─ ScreenRouter ── stack of UiScreen; Android back closes the top modal, then pops
     ├─ GameFeedback ── no toasts (G1): Session.ResultEvents → animations (rewards fly into the bar, star pops, screen hooks UiScreen.OnFeedback), hurray banners (RevealBanner, queued); Session.Failed → error bubble above the tapped button (2 s); "Not connected" → Reconnecting strip
     └─ ResourceBar ── profile chip (worm level, name, power → profile: Worm, Settings) + up to 5 currency chips the player has a use for (HubLayout.VisibleCurrencies = the rules' reward-use mapping, RewardGate.CurrencyHasUse; a new account shows stamina and Diamonds) with regen timers (live); "All" opens every balance
```
- **Currency explainers** (`App/GearGuide.cs` → `CurrencyGuide`, `ResourceBar.ShowCurrency`): every currency chip is tappable
  and opens "Ore — You have 500 — What it's for: Part levels and merges, the Replicator, city buildings" (the rules'
  `ContentDb.RewardUses` text, the same mapping that decides which currencies show), a line about what the player can do
  now (Ore: "You can level up 2 equipped parts right now") and **Go to …**: the screen of the first unlocked use
  (`CurrencyGuide.SpendScreen`: a feature use → the screen its unlock moment opens, `HubLayout.UnlockAction`; a part-rarity
  use → the Worm; stamina → Portals; Diamonds → Shop (no unlock)). Already on the Worm/Parts, Go scrolls to
  the inventory. "All" lists every currency with its use; a row opens its explainer. The part detail's Ore balance and the
  inventory's ORE chip open the Ore explainer.
- **Worm tab badge**: "▲ UP" when an equipped part's next level is affordable (`GearGuide.HasAffordableUpgrade`), else "NEW"
  on its unlock day (`HubScreen.TabBadge`).
- **Same commands for both modes.** Screens only create rules command objects (`new CityUpgrade { BlueprintId = … }`)
  and call `Session.Run`. They read `Session.State`/`Content` and the rules' static query helpers
  (`CitySystem.UpgradeCost`, `QuestSystem.Visible`, `Features.List`, …) — no game logic in the client.
- **`LocalGameService`** (offline): own `ContentDb` instance with `Shop.AllowDevReceipts = true`, `GameEngine` with a
  Newtonsoft JSON `Cloner` (atomic commands), save file `Application.persistentDataPath/rogue_stars_save.json`
  (PascalCase fields like the server's storage format, collections replaced on load, atomic tmp-file write).
  `[ServerOnly]` commands run locally only here: purchases complete with `PurchaseComplete { ReceiptId = "dev:<token>" }`,
  arena fights run against local bots (`ArenaStartFight` + `ArenaFightResult`, power comparison) and invasion attacks
  roll local damage (`InvasionStartAttempt` + `InvasionRecordDamage`). Dev tools (Settings → Developer, offline in the editor and development builds):
  reset progress, jump to day N (`DevReset`/`DevJump`), unlock-all flag; `DevGrantStarterKit` for tests. Time-based state is
  settled every 5 s.
- **Auth** (`Services/Auth/`): online sessions get their bearer token from an `IAuthProvider`. `ServerConfig.AuthMode`
  `firebase` (release builds: `BuildAndroid` sets it together with `ROGUESTARS_SERVER_URL`) → `FirebaseAuthProvider` on the
  **Firebase Unity SDK 14.2.0 (Auth module only)**: no user yet → Google Play Games v2 (`PlayGamesPlatform.Instance.Authenticate`
  → `RequestServerSideAccess(false)` → server auth code → `PlayGamesAuthProvider.GetCredential(code)` →
  `SignInWithCredentialAsync`); Play Games unavailable/declined → **anonymous** sign-in; an anonymous user is **linked** to Play
  Games once per app session when Play Games signs in (same uid = same player; if that Play Games account already has a
  player, the client switches to it); Settings → *Account* shows the account and a *Link Play Games* button (interactive
  sign-in). The ID token's `exp` is read from the JWT; `RemoteGameService` fetches a new one a minute before expiry (the SDK
  refreshes it) and force-refreshes once after a 401. Play Games is only touched when the plugin is configured
  (`PlayGamesSettings` app id + web client id, see docs/setup.md step 3.7). `dev` (committed default, and always in the
  editor) → dev auth below; the production server refuses it.
- **`RemoteGameService`** (online, docs/server.md): bearer token from the auth provider, or dev auth
  `POST /v1/auth/dev {deviceId}`; `POST /v1/session`
  → state + server time offset + allowed commands + `adsDisabled` + `devTools` (`DevToolsAvailable`: the Developer card and
  `IGameService.DevTools` = `POST /v1/dev/reset|jump`, 120 s timeout); `POST /v1/command {type, requestId, args, expectedVersion?}`
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
- **Ads disabled (current default)**: the rules content flag `ShopConfig.AdsDisabled` makes `ShopSystem.HasNoAds` true for
  everyone. Offline it comes from `PlatformConfig.AdsMode = disabled` (`LocalGameService.AdsDisabled`), online from the
  server's `adsDisabled` session flag (copied into the service's own `ContentDb`, never `ContentDb.Default`). Every placement
  then completes without an ad: command placements send an empty `AdToken`, direct-reward placements (`daily_diamonds_ad`)
  run `AdWatched` locally offline and `POST /v1/ads/instant` online (`GameSession.NoAds`). On devices `PlatformServices`
  creates `NoAdGateway`: the MAX plugin stays in the project but is never initialized and needs no key.
- **Purchases disabled (current default)**: Google Play payments are on hold. `PlatformConfig.StoreMode = disabled`
  → `DisabledStoreGateway` online on devices (Unity IAP / Play Billing never initialized; offline keeps the dev-receipt
  store), and the server's `purchasesDisabled` session flag (`Iap:Mode=disabled`) does the same for any build:
  `GameSession.PurchasesDisabled` → the shop shows the catalogue, real-money buttons read "Coming soon", `Purchase`
  refuses with `purchases_disabled`; Diamond-priced items keep working. Editor/offline: dev receipts as before.
- **Release server**: `ServerConfig.ReleaseBaseUrl` = `https://rogue-stars-server-t72a3eykvq-ew.a.run.app` (production);
  `BuildAndroid` bakes it in (online, Firebase auth) unless `ROGUESTARS_SERVER_URL` names another server or `local`.
- **Product set**: `PlatformConfig.ProductSet` `initial` (default) sells only `StoreCatalog.InitialProductIds`
  (= server `PlayCatalog.InitialProductIds`, `docs/play-console-products-initial.csv`); other products show "Coming soon",
  `GameSession.Purchase` refuses them (`product_not_offered`) and Unity IAP only registers the offered ones. `all` sells the
  whole catalogue. Sessions built without a config (tests) use `all`.
- **Platform config** (`Assets/Resources/PlatformConfig.asset`): `StoreMode` (`disabled`/`unity_iap`/`fake`), `AdsMode`
  (`disabled`/`max`/`fake`), `MaxRewardedAdUnitId`, `ProductSet` — committed with placeholders/defaults; `ServerConfig.asset`
  has `BaseUrl`, `OnlineByDefault`, `ReleaseBaseUrl`, `AuthMode`; the MAX SDK key lives in `Assets/MaxSdk/Resources/AppLovinSettings.asset`
  (committed empty). `BuildAndroid` injects real values from environment variables for one build and restores the
  committed files (`Editor/BuildSecrets.cs`; see docs/setup.md step 11), including the Play Games plugin settings
  (`ROGUESTARS_PLAY_GAMES_APP_ID` / `ROGUESTARS_PLAY_GAMES_WEB_CLIENT_ID`). The MAX SDK key is only read with
  `ROGUESTARS_ADS=max`; without it the AppLovin Quality Service plugin stays off (it would call AppLovin).
- **Android dependencies**: custom Gradle templates in `Assets/Plugins/Android` (main, settings, properties with AndroidX).
  External Dependency Manager comes as the UPM package (1.2.190, required by Firebase; the copy the MAX plugin shipped in
  `Assets/ExternalDependencyManager` was removed). `BuildAndroid` runs `PlayServicesResolver.ResolveSync`, which writes the
  Maven dependencies of MAX, Firebase App/Auth and Play Games v2 into `mainTemplate.gradle`; Unity IAP adds
  `com.android.billingclient:billing` itself. Gradle downloads them (network). `google-services.json`
  (`Assets/`, git-ignored) becomes `Assets/Plugins/Android/FirebaseApp.androidlib` and
  `Assets/StreamingAssets/google-services-desktop.json` (both generated, git-ignored).
- **Look** (approved wireframes): warm dark palette in `Theme` (background `#170E10`, cards `#2B1A1A`, bars `#22141A`,
  cream text, orange primary `Accent` with a lighter `AccentLight` and a dark `AccentLip`, gold, red, green, cyan, blue,
  purple; rarity, valence, family, currency and tile colours), radii/lip/gutter metrics, `Theme.TextOn(face)` (dark caption
  on bright faces). `UiShapes` generates an anti-aliased rounded-rect 9-slice sprite, ring (outline) sprites per thickness and
  a circle at runtime; `Image.pixelsPerUnitMultiplier` turns the one texture into any corner radius. Titles, buttons and
  numbers use the rounded display font (`Ui.DisplayFont` = `Resources/Fonts/Fredoka-Bold`); body text stays on the
  built-in font. `Ui.FontFor(text)` falls back to the body font when a text has glyphs Fredoka lacks (★ ☆ ✓ →; the list of
  Fredoka's non-ASCII ranges is in `Ui.cs`, not `Font.HasCharacter`, which reports OS fallback glyphs).
- **UI toolkit** (`Ui.*`): `Panel` (flat), `RoundPanel`, `Dot`, `Label`, `Display` (display font), `Caption`, `Chip` / `ChipRow`
  (pills sized to their text, filled or outlined), `Card` (rounded padded column, optional outline), `CardButton` (a tappable
  card: event options, draft cards), `Button` (chunky: rounded `Face` = `targetGraphic` over a darker lip, soft top shine on
  bright faces, caption in the display font; still named `"Button:" + text`), `SetButtonColor`, `Row`/`Column` (a background
  makes them rounded), `Spacer`, `Input`, `Scroll`, `List`, `Grid`, `Bar` (rounded track + fill), `TabBar` (segmented pill),
  `Modal` (rounded box, display-font title, button row), `Sheet` (modal with a fully custom box). Motion: `UiTween`
  (pop-in/fade), `UiSequence` (queued steps), `UiTweens.Play(go, channel, duration, update, done)` (one running tween per
  object and channel; a new one completes the old one first), `UiBob` (idle bob), `UiPulse` (breathing glow). All of them
  complete synchronously outside play mode, so EditMode tests stay deterministic. `SafeAreaFitter` keeps the UI inside
  `Screen.safeArea`. Layouts fit a 978-unit wide canvas (1080×2340 phones with the 0.5 width/height match).
- **Screen chrome** (`UiScreen`): header with a rounded back button (`BackLabel`/`OnBack`, the run screen shows a pause
  sign and opens a pause modal), display-font `Title` and an optional `Subtitle` line (both re-read on every rebuild),
  `BuildHeaderExtras` for chips (stamina, coins); optional fixed rounded `Footer` (`FooterSpacing`, `FooterPadTop/Bottom`);
  `ScrollToAfterBuild(rect)` centres a node/tile after a rebuild (otherwise the scroll position is kept);
  `CoversResourceBar` (run, battle) = a full-screen game screen: its root reaches over the app's resource bar with an opaque
  background, `HeaderHeight` 0 and its own top strip (`Screens/RunHud.cs`: `RunHudStrip`, also the boss clock row and the
  skill tile used by the run tray and the battle strip). `UiShapes.CircleRing` is a round ring sprite for radial progress
  (`Image.Type.Filled`, the run level's XP ring).
- **Screens**: subclass `UiScreen`, implement `Title` and `Build(body)`; the body is rebuilt on every state change
  (`AutoRebuild`, off for screens with text inputs) and countdowns use `Live(text, () => …)` (ticked twice a second).
  `TimerControls(parent, timerId)` renders any rules timer with Diamonds / ad −15% / free speed-ups.

## Screens
| Screen | Feature gate | Notes |
|---|---|---|
| Hub (Planet) | – | onboarding v2 (GDD §16, design canvas Main/HubDay1), what shows comes from `App/HubLayout.cs`: left column = Daily Quests + live events (from the Invasions unlock, ≤ 3 icons + Observatory), planet + frontier portal chip in the centre, right column = Offer (timed offer on sale: opens the Shop's Offers), Pass, Mail, More (every other unlocked screen); the tutorial goal card when it needs another screen; the **next-unlock teaser** (`Features.Next`: "Laboratory — clear level 13" with the level progress "11/13"; level unlocks, no calendar gate); fixed footer: PLAY card (portal · level, stamina cost; Continue during a run) and the bottom tabs (Worm, City, Planet, Shop, Arena): Planet and Shop always, the rest as their features unlock (NEW badge on the unlock day; the Shop's on the day a pull section opens). The bottom block is laid out in frame units and rebuilt when the frame changes size (`HubScreen.FrameSizeChanged`: the canvas taking its size, a safe-area inset), so the tab labels never fall below the screen edge. Locked features are never shown. Pending celebrations open the **unlock moment** |
| Unlock moment / feature intro | – | `UnlockScreen` (GDD §16, data-driven by `FeatureDef.Intro`): full screen, a kicker ("NEW UNLOCK", "NEW IN YOUR RUNS" for run content, "HOW IT WORKS" in a replay), the feature name on a ribbon (`FeatureName`), then its 2–4 panels one at a time: the art slot (`IntroArt`: `World/IntroArt` = `ArtCatalog.Intro` (`Art/Intro/<key>`) else a drawn fallback per key; every content key has one, tests) with a looping animation (`IntroAnimator`: tap = a hand pointer presses it, pulse, slide-in, count-up "+N", spin, bounce, a filling bar; play mode only, edit mode shows the resting pose), one short line (`StepText`), page dots (`Dot:i`), SKIP / NEXT (`IntroSkip` / `IntroNext`). The last panel adds the `FeatureDef.Description`, reward chips (`UnlockRewards` + `UnlockGrants` + the reward stash it released, `RewardGate.ReleasedAt`; `Fmt.RewardChips`), ONE guided action (`UnlockAction`, `HubLayout.UnlockAction`: claims `FeatureCelebrate` for every member, then replaces itself with the feature screen; run content: PLAY → portals) and the next teaser ("Next at level 7: Auto Roll"). Opened by the hub (`HubScreen.ShowPendingUnlock`, from `Frame` while the hub is on top, never mid-run, one at a time); backing out snoozes it for the session and the hub shows "NEW: … — tap to open". **"?"**: every feature screen lists its features (`UiScreen.IntroFeatures`: Worm, Parts, City, Aliens, Breeding, Laboratory (+ packages), Heroes, Mounts, Shop (its pull features), Passes (Funds, Pass), Observatory, Arena, Guild, Portals (run content, Auto Roll, Overcharged, Burrow, Dark Side)); `UiScreen.HelpButton` (top right, shown once one of them is unlocked) replays the intro (`UnlockScreen.Replay`: same panels, GOT IT closes it, nothing claimed) or, with several unlocked, opens a topic list (`Intro:<feature id>`) |
| Portals | – (modes: `overcharged`, `burrow`, `dark_side`) | **level map**: header = the selected level's portal name + "Portal n · x of 10 cleared · y of 30 stars" + a stamina chip; Main / Overcharged / Burrow / Dark Side tabs; a zig-zag path of level nodes climbing upwards (`PortalsScreen.MapX`), connector dots between them, the newest portal at the top with a portal banner; node states (`NodeStateOf`): **next** = big glowing orange node ("NEXT"), **cleared** = stars ★★☆, **open**, **locked** = dimmed and not tappable; the last level of each main portal is a **boss node** ("BOSS · n" + boss name); **CHEST** (every 10th level) / **BONUS** (every 50th, `CampaignDef.MilestoneEveryLevels`/`PortalBonusEveryLevels`) chips beside nodes, ✓ once claimed. Tapping a node selects it for the fixed **bottom level card** (`SelectedLevel`, default = the next level of the tab): "Level n · name", threat, the three star goals (★ achieved / ○ open), `i` (`LevelDetails`: the level modal with threat, boss, dice/rolls, modifiers, feats, completion rewards, milestone chest / portal bonus, entry cost) and **START RUN** with the stamina cost (`StartRun`; LOCKED for locked levels, CONTINUE RUN while a run is active); the map scrolls to the selected/next node when opened or paged. Stamina refill modal (daily free / ad / Diamonds), **daily Biomass surge** counter (`RunSystem.BiomassBonusRunsLeft`; not shown before Biomass has a use). The campaign is endless and never iterated: Main/Overcharged show a page of 5 portals (`PortalsScreen.MainWindow`: 2 cleared, the frontier portal holding `NextMainLevel`, 2 upcoming; Prev pages back through cleared portals, Next never past frontier + 2, *Jump to level n*); the endless Dark Side (`ContentDb.DarkSideLevel`) is paged 25 at a time around the next level (`dark_side_level_max` + 1), one page ahead at most, each node showing its Data |
| Run | – | **owner-approved design "A · Top status bar"** (design canvas `Main.dc.html`): no app header and no resource bar (`HeaderHeight` 0, `UiScreen.CoversResourceBar`: the screen reaches over the bar with an opaque background); **top strip** (`RunHudStrip`, shared with the battle; `TopStrip`): square pause button (`PauseButton` → pause modal with the level name, "Roll r/N · Boss x% awake · Threat t" (`Subtitle`; N = `RunSystem.BossRollEstimate`), the boss, Resume / Leave / Abandon), the **HP bar** ("HP 734 / 1,080", `HpBar`) over one line **"ATK n · DEF n · SKILLS n"** (`AtkChip`/`DefChip`: `RunSystem.WormStats` = loadout + run skills at their levels + perks, buffs, curses, level modifiers: the numbers fights use; `SkillCount` = `RunSystem.BuildSize`: every skill, skill level and stat perk gained this run; SHARDS / DANGER appended when > 0), the **run level ring** (`RunLevel`: "LV n" in a radial XP ring `XpRing` = `RunSystem.LevelInfo` XP / `XpNeeded`; a tap shows a tip bubble "Run level n · XP x / y" under it), the **coins chip** (`CoinsChip`); under it the **boss clock** (`BossClock`, `RunPresentation.BossClock`): "Roll n/N" (`RollCounter`), a bar filled with the boss's awakening (purple → red) with one **miniboss marker** per miniboss of the run (`RunTuning.MinibossesPerRun` = 2 lairs on the path, spread along the rail, `MinibossMarker:i`, bright once beaten; none on levels without lairs or in the Burrow), "BOSS"; strip numbers tween to their new values when the worm lands (after a fight: when the playback returns), changed ATK/DEF pop; `HudText` = "HP a / b | ATK n | DEF n | Lv n | XP x / y | SKILLS n"; **board** (zoomed in, in its own viewport `BoardHolder` under the strip; **board art** (`ArtCatalog`, below): a **layered board**: (1) the level's biome ground behind the path (`BoardGround`, one `Ui/GroundLayer` graphic: upright copies only — never mirrored — of the biome's seamless ground texture edge to edge, or of its full-frame background with a crossfaded band (`GroundLayer.FullFrameBand` 20 % of a copy) where the lower copy fades in over the one above, so no hard seam; **locked to the board**: it moves 1:1 with the camera/tiles, placed by `GroundLayer.Phase(cam, pitch)` and wrapping by whole pitches — no parallax), (2) **decor props** beside the path (`BoardDecor`, on the board itself so they move with the tiles; below), (3) the path tiles drawn as **chunky slabs** (`SlabShadow`, bevel side `SlabSide` `SlabDepth` 12 under the face, a highlight `Bevel` on top; slab face / side / edge colours per biome, or the biome's tile sprite), the tile kind's colour **marker** gem (`Marker`) + label on every slab, path edges with a dark under-stroke; the token is the **hero worm sprite** (`Art/Hero/hero_worm`) standing on a small base plate (the drawn pill worm when the sprite is missing); a **follow camera** moves the board: `RunScreen.StepCamera` each frame → `RunFx.CameraStep`, exponential ease capped at `CameraMaxSpeed`, so it never snaps — not at a roll, not at a rebuild (the camera is kept in window-independent board coordinates); a drag on the board looks around and holds the camera for 3 s, the next move follows again): a long path that never repeats (generated ahead in segments by the rules) drawn at the rules' 2D layout (`RunTile.PosX/PosY` × `LayoutUnitX/Y` 160 / 118 px, centred; varied shapes per segment: straights with side loops, zig-zags, wide curves, hooks, diamonds; hand-built boards without a layout, `RunTile.Segment` < 0, fall back to the snake grid of 5 columns × 3 lanes; `TileCenter`), only a window of it from a little behind the worm to 70 columns ahead (`WindowStart`/`InWindow`, origin a multiple of two bands so tiles keep their place); once the boss arena opens ahead of the worm only the walked path and the arena ("BOSS arena", its own lane) are drawn; resolved tiles keep their look with a ✓ (`TileLook.Cleared`, a beaten fight never turns into a new tile under the worm) drawn flipped so the path starts at the bottom and climbs (`DrawPoint`); tiles are rounded cards outlined and labelled by type (`Theme.TileLabel`/`TileAccent`: FIGHT red, SHOP blue, EVENT purple, CHEST/LOOT gold, REST green, ELITE, MINI BOSS, BUFF, CURSE, TRAP, SHARD, ITEM, START, BOSS gate), walked edges lit; **passed tiles keep their look**, only tiles the worm **landed** on (`TileLook.Landed` = `RunTile.TimesResolved > 0`) are filled with their colour and get a `LandMark` dot (the tile landed on in the current move changes at the landing), forks marked ("fork"), branch options glow with A/B; **at a fork** (no modal) each option's path is tinted and the tile it would land on with the steps left (`BranchLanding`/`BranchPath`: stops early at a shop or the next fork) gets a big pulsing "A · LAND" / "B · LAND" tap target (`BranchPick:A/B`; the option tile itself is `BranchOption:A/B`), the ROLL button reads "PICK A PATH", campfire-lit tiles show +n%, boss marker ("BOSS n%", a free cell next to the gate, never on a tile); the worm token is a board-game piece (round glowing base, the worm bobbing on it with `UiBob`, a shadow on the ground) smaller than a tile; a roll first shows **tumbling dice** over the board (`DiceView`/`RunFx.DiceFaceAt`/`DicePose`: as many dice as rolled, ~1 s by hand, 0.45 s in auto, landing on the rolled values; the footer dice follow), then the token **hops** tile by tile (`RunFx.HopPosition`/`HopScale`: eased step, arc, squash at take-off and landing, stretch in the air; 0.24 s per tile, 0.15 s in auto; a tap on ROLL fast-forwards ×4), passed pickups (shards, coin clusters) pop a small text, and the **landing** pops the tile (scale overshoot, two rings, a glow in its colour) followed by **feedback popups** (`RunPresentation.Landing`: from the rules' tile outcome of the landing, `ActiveRun.TileOutcomes` via `RunPresentation.TileOutcome` — "+6% ATK", "+25 coins" with coins flying to the coin chip, "−10% HP" with a shake, "Quiet… nothing here", "Avoided!", "Next roll −2", boss tiles… — or a non-sheet LastOutcome, else from what the tile is: FIGHT!/EVENT!/SHOP!…; passed pickups pop the pass outcome, `RunPresentation.Pass(content, run, tile)`; `FromTileDef` only serves hand-built boards) and a short beat before a fight or a decision opens (`RunScreen.LookOf` = the pure tile look; `TileCenter` is the one board layout function). **Bottom bar** (fixed): the **skill tray** (`Slots`, `RunPresentation.Tray`: 7 tiles `SkillSlots` of 118, one per run skill in the order gained: chunky squares in the skill's **tier colour** — run skills have three tiers: Common grey, Legendary orange, Mythic red, `RunPresentation.SkillColor` / `Theme.SkillTierColor`, outline `RarityBorder` — with **"Lv x/max"** in the corner (`RunTuning.MaxSkillLevelOf`: Common 3, Legendary 2, Mythic 1), the name, the family as a small corner tag `FamilyTag`; with more skills the last tile is **"+N"** (`MoreSkills`) and every tile opens the skills list; free slots are outlined `EmptySlot`s), then Loot (`LootButton`, count), big **ROLL** with the **dice glyph** (`FooterDice`: the last roll's dice, a 3 before the first roll; the tumbling dice land on it) (hold for auto; CHOOSE reopens the decision; **ENTER ARENA** at the boss encounter → `RunEncounter`: the worm walks into the boss arena, its own step and playback; the arena opening shows only a compact 1 s strip, `GameFeedback.Compact`; minibosses are lairs on the path since playtest 0.1.308, a forced stop with a big marker; no floating "!" markers on the board since 2026-11-26), **AUTO** on/off, a hint line (fork / auto paused / the last landing's text). Decision popups: **event** card (EVENT caption, name, text, option cards with GAIN/RISK/COST/CHANCE/ACCOUNT LOOT chips from the option's outcomes, `RunScreen.OptionTags`, plus the risk text; unaffordable options disabled), **outcome result** (`Modal:Outcome`, from `ActiveRun.LastOutcome` once per `OutcomeSeq`, after any battle playback, before the next decision): "EVENT/CHEST/CAMPFIRE/SHOP RESULT", title, "You chose: …", for chance options a **chance roll** (`ChanceMeter`: "65% chance", a green lucky zone and a red one, a marker sweeping and settling for `ChanceSpinSeconds`, then SUCCESS! / NO LUCK; `RunScreen.InstantOutcomes` = skip mode), then one row per gain/loss with an icon and numbers (`OutcomeLineText`: coins, HP, "+8% ATK for the run", new/lost skill, XP, level-ups, shards, "account loot, paid when you beat the boss", boss awakening and pace, boss strength, danger, fight won/lost) and Continue; **no result sheet after a shop visit** (its purchases were seen in the shop) nor for tile outcomes (shown as landing popups; `RunPresentation.IsSheetSource`); it waits for Continue (auto mode too), **draft** "NEW SKILL!" (1 of 3 cards tinted and outlined by rarity, RARITY · FAMILY caption, family tag on the icon, SYNERGY / "LEVEL n → n+1/max" chips (Common max 3, Legendary 2, Mythic never levels) titled with the target level ("Keen Eye Lv 2") and the upgrade inline (`SkillText.UpgradeFull`: "+15% → +25% crit chance"; shop offers too) and the level-up rule, reroll); after a beaten miniboss the **MYTHIC** choice (`MythicTitle`, red sheet, "Miniboss defeated — take 1 of 3 Mythic skills"); shop (coins and "n bought this run" line, offer rows coloured by rarity with rarity/family or the level-up, price buttons that turn into "Sold" in place — the sheet stays open while buying, `RefreshShop`; reroll, leave), revive (ad / Diamonds / give up), Burrow (descend / cash out); **auto mode** (a level unlock, `Features.AutoRoll`: before it the dice read "Tap to roll" and a hold shows a tip bubble "Auto Roll unlocks at level 7" (`RunScreen.HoldDice`, owner playtest 0.1.224); owner playtest 0.1.200 + level unlocks: auto rolls and starts fights): hold ROLL → while nothing is open over the board, `RunScreen.Frame` sends `RunRoll` again, and `RunEncounter` at a clock miniboss or the boss arena (manual play keeps the FIGHT! / ENTER ARENA tap); any tap stops it; everything else waits for the player's tap with auto paused (`AutoPaused`, `WaitsForPlayer`: any phase but AwaitRoll and Encounter — fork, event, chest, campfire, draft, Mythic choice, shop, revive, mini-game, Burrow — or an open result sheet, decision sheet, chest or battle playback; tap-to-stop overlay removed): nothing auto-closes, auto-opens or auto-picks, and auto rolls again by itself once the player closed it while still on. No auto-pick setting (removed from the run, home and Settings screens); the rules' `RunAutoStep { Farming }` default choices stay for bots, the simulator and tests only |
| Battle playback | – | **same layout language as the run (design A)**: no app header / resource bar; the run's **top strip** (`RunHudStrip`: `SkipButton` ">>" closes the playback, the worm's **live HP** from the log, ATK / DEF / SKILLS / level ring as the board showed them when the fight started — `BattleHudContext`, passed by the run screen, else read from the active run; "–" outside a run; coins); a **larger skill strip** in the fixed footer (`SkillStrip`, `StripSlots` 6 tiles of 146 + "+N"; the run's skills plus any other player-side skill that fires, the ones that fire in this fight first, the most active first, `BattleSkillFx.StripOrder`): whenever a skill fires in the log (`BattleLogEntry.SkillId` of a player-side actor: skill hits, statuses applied / resisted, heals, shields, rage, buffs, summons — `BattleSkillFx.FireOf`, no rules change needed) its tile pops and flashes (`FireFlash`) and a label rises above it ("Magma Tail · Burn ×2", "… · Crit 240", "… · +80 HP"; `EffectText`); fires **queue per tile** (`SkillFireQueue`: one at a time for `FireSeconds` = 0.9 s / speed, the same effect twice in a row waits once as "×2", at most 3 waiting), tiles fire in parallel, labels of simultaneous fires stack in rows, fires of skills behind "+N" pulse that tile; `Instant` / skip = no animation, only `FireCount` / `SkillFires` are recorded; speed ×1/×2/×3 shortens the fires. The stage: player side left, enemies right (`StagePosition`), each enemy with its HP bar ("hp / max") over it: fighter sprites through `ArtCatalog` (`BattlePlaybackScreen.FighterSprite`): the worm is the **hero sprite**, an alien companion its **alien sprite** (`Art/Aliens/<alien type id>`), an enemy its **enemy sprite** (`Art/Enemies/<enemy def id>`, `EnemySpriteSize` 150); a boss / elite / summon without its own art borrows the first sprite of its family (`ArtCatalog.EnemyFamily`: a boss's escorts, then the pools of the levels it rules; an elite's level pools) drawn bigger (`BossFamilyScale` 1.3, `EliteFamilyScale` 1.15), with no coloured glow under it (playtest 0.1.325: it read as a red shadow floating below); without any art the old rounded token with its letter (pink pill worm, A alien, M mount, E/EL/B enemies); sprites flash red on hits, lunge, side-step and grey out when knocked out like the tokens; each with name, HP / shield / rage bars (values straight from the log entries: `TargetHp/TargetShield/TargetRage`, `ActorShield/ActorRage`, fighters' `StartShield/StartRage`), status chips (BURN 2…). Per entry (`UiTweens`, no packages): attacker lunges toward its target (enemies on their turns too, rage attacks further), target flashes white and shakes, floating numbers rise (crits bigger with "CRIT"), DODGE (side-step) / heal / shield / status / RESIST / STUNNED / PARRY / KO popups, HP bars tween down, knocked-out fighters fade; turn-order strip ("NEXT": upcoming actors), round counter, the worm's rage / shield / status panel (`WormPanel`; its HP is in the top strip), VICTORY / DEFEAT / TIME UP banner, Continue, collapsible battle log, speed ×1/×2 (×3 needs the monthly pass, marked PASS). `BattlePlaybackScreen.Instant` = skip mode (no animations, the log is applied on the next frame); `TimeScale` speeds scripted playthroughs; a finished fight always waits for Continue, in auto mode too (owner playtest 0.1.200) |
| Run result | – | VICTORY / DEFEAT, portal · level · boss defeated, the level's three stars (NEW for feats earned this run), rolls / run level / skills / minibosses / shop buys chips, no list of the run's skills (owner playtest 0.1.200), LOOT SECURED reward tiles (identical rewards summed into one tile, `RunResultScreen.Aggregate`: "x30 Diamonds", not 10 + 10 + 10; defeat and doubled rewards too) (or pending loot lost), "Biomass surge ×4 applied" + surge runs left (only once Biomass has a use, `RunSystem.BiomassSurgeActive`), **double rewards** (outlined; rewarded ad, instant with No Ads → `RunDoubleRewards`, daily cap shown), **NEXT: LEVEL n** after a won main run (else PLAY / TRY AGAIN, named `Again`), Back to portals |
| Worm | `parts` | header card with the worm, name, level and power; level up (×1/×10, Biomass), stats, six body slots (cards outlined and tinted by part rarity, empty = outlined) with equip modal + "Equip best parts", talent road (ranks, tier completion, perk) once `worm_level` unlocks, "All parts" (Parts screen) |
| Parts | `parts` | 14-step ladder (Common … Mythic, Mythic+1/+2, Mythic S, S+1/+2, Mythic SS, SS+1/+2; `Theme.PartRarityColor`), inventory with slot filters (equipped first, then rarity), quick merge (asks first if locked parts below Legendary exist); detail modal: stats, the five skills locked/unlocked by rarity (Epic/Legendary/Mythic/Mythic S/Mythic SS), level up / cap raise / level reset, equip, **lock** (client-side `PartLocks`, PlayerPrefs per player), rune sockets, Forge, dismantle (confirm for Legendary+/locked); **merge picker**: own-def copies (needed/owned), fodder points and LE, Ore, candidate list with add/remove, auto-select of the best fodder (`PartPlanner`: cheapest set without superfluous parts, other defs first; locked, Mythic+ and Legendary+ copies of equipped defs only as a fallback that needs a confirmation; equipped/awakened/forged never), checked by `PartsSystem.ValidateMerge`; **Replicator** (Mythic+ parts: 10 LE of other-def Legendary+ surplus + Ore → Legendary copy, `ValidateReplicate`); **awakening** (Mythic+, once, 50 Awaken Cores): preview of the new name, stats before → after and the five skills swapped; Runes tab: lock, merge 3→1 |
| Run result | – | VICTORY / DEFEAT, portal · level · boss defeated, the level's three stars (NEW for feats earned this run), rolls / run level / skills / minibosses / shop buys chips, no list of the run's skills (owner playtest 0.1.200), LOOT SECURED reward tiles (identical rewards summed into one tile, `RunResultScreen.Aggregate`) (or pending loot lost), "Biomass surge ×4 applied" + surge runs left (only once Biomass has a use, `RunSystem.BiomassSurgeActive`), **double rewards** (outlined; rewarded ad, instant with No Ads → `RunDoubleRewards`, daily cap shown), **NEXT: LEVEL n** after a won main run (else PLAY / TRY AGAIN, named `Again`), Back to portals |
| Worm | `parts` | **the gear screen** (`WormScreen : GearScreen`): header card with the worm, name, level and power; level up (×1/×10, Biomass) once `worm_level` unlocks; **"▲ Level up ready" callout** when an equipped part's next level is affordable (`GearGuide.UpgradableEquipped`: "Ore levels up your parts", the first part "Lv n → n+1 · 30 Ore" as a one-tap `PartLevelUp` + Details); "Equip best parts" while slots can be filled; **Equipped**: six body-slot cards (part name, rarity, level, "+n spare" / "n parts fit", ▲ badge when affordable; tap = the inventory below filtered to that slot and scrolled into view, `ShowSlot`); the **inventory** of every owned part (below); stats (`Fmt.StatName`: "Crit chance"); talent road (ranks, tier completion, perk) once `worm_level` unlocks |
| Parts | `parts` | the same inventory without the worm (`PartsScreen : GearScreen`, subtitle Ore · Gear Cores · Awaken Cores) + a Runes tab (lock, merge 3→1); opened by the Worm's Runes button (once Runes unlock), the Forge unlock and deep links. **Inventory** (`GearScreen.BuildInventory`, queries in `App/GearGuide.cs`): "Inventory · n" + a tappable **ORE** balance (Ore explainer), slot tabs All/Head/Body/Tail/Legs/Arms with counts, sort Best first (equipped, rarity, level) / Level / Slot, a rarity filter (picker of the owned rarities), a 3-column grid of tiles (296×232, fits the 930-unit body) with slot · style, name, "Rarity · Lv n/cap", **EQUIPPED** / LEFT ARM / RIGHT ARM chip and green outline, LOCKED, AWAKE, ▲ badge (equipped + affordable); quick merge (asks first if locked parts below Legendary exist). 14-step ladder (Common … Mythic SS+2; `Theme.PartRarityColor`). **Part detail** (modal with a scrolling body, `Ui.ScrollingColumn`, max 1240): rarity / slot / family chips + "ON LEFT ARM"; **level card**: "Level n / cap", READY chip, **LEVEL UP · 30 Ore** (+ Gear Cores above level 10; green when affordable, else "Needs 30 Ore (you have 10). Ore drops from runs and dismantled parts."), "ORE n · what is Ore?" (explainer), at the cap: raise cap (plates) / merge hint with the next rarity's cap, reset to level 1 (refund); stats now → next level; equip (arms: Equip left / right arm, "On left arm"), **lock** (client-side `PartLocks`, PlayerPrefs per player); the five skills by rarity ("unlocks at Epic"); merge / Replicator; rune sockets; Forge only once unlocked, awakening only for Mythic+; dismantle (+Ore incl. refunds; confirm for Legendary+/locked); **merge picker**: own-def copies (needed/owned), fodder points and LE, Ore, candidate list with add/remove, auto-select of the best fodder (`PartPlanner`: cheapest set without superfluous parts, other defs first; locked, Mythic+ and Legendary+ copies of equipped defs only as a fallback that needs a confirmation; equipped/awakened/forged never), checked by `PartsSystem.ValidateMerge`; **Replicator** (Mythic+ parts: 10 LE of other-def Legendary+ surplus + Ore → Legendary copy, `ValidateReplicate`); **awakening** (Mythic+, once, 50 Awaken Cores): preview of the new name, stats before → after and the five skills swapped |
| Shop | – (pull sections per feature) | **One Shop** (owner, 2026-11-27; `ShopScreen` + `ShopScreen.Pulls.cs`, screen id `shop`, `signals` kept as an alias): the hub's bottom tab "Shop" (shop stall icon, `HubLayout.ShopTab`, always there: the offers need no unlock, owner 2026-11-27; NEW on the day a pull section opens, the Passes page's claim count while there is no Pass icon); no second entry in the right column. Section chips in the footer, two rows: the pull sections **Aliens / Blueprints / Parts** (each once its pull feature is unlocked: Aliens, City, Parts; a dot while its free 10× waits; the Aliens screen's "Shop" chip opens Aliens) and, always (no unlock), the offer sections Offers / Diamonds / Passes (opens the Passes page) / Items / Ads; it opens on the first pull section, else on Offers (a new player; the Offer icon opens Offers). A pull section: featured mythic banner with countdown vs standard pool, the visible pity, published odds, 1×/10× with Signals / Schematics / Crates or Diamonds, daily ad pull, reveal titled "Parts pull" / "10× Parts pull" (a result without a use yet, e.g. hero shards before Heroes, is marked "kept for later": it waits in the reward stash). Screenshots: `ShopMergeShowcaseTests` (explicit PlayMode) |
| Summon events | `heroes` (mounts from `mounts`) | `SummonEventScreen` (hub's left column "Summon", entry `HubLayout.LeftColumn` id `summon`; also from Heroes / Mounts): hero / mount tabs, top bar (featured name, Diamonds, exchange tokens, tickets), stage with the featured art under a spotlight, time left, "Yours · n★", side column Exchange / Packs / Pass / Tasks with badges (`SummonView`), Luck gem + "? Luck x/max" (odds modal) + the Luck rule, pull milestones (tap to claim), footer Skip Animation, 1× / 10× Draw (ticket have/need, the Diamond price when short), back, "Next: …"; draw result sheet (rarity cards, FEATURED / NEW / ★n badges, Luck after, tokens, Again); Packs sheet (daily ad ticket: "Free" while ads are disabled; paid packs "Coming soon" outside the offered product set); Tasks sheet (progress bars, CLAIM / GO → portals or Signals, Claim all); `SummonExchangeScreen` (3-column grid, event limits); `SummonPassScreen` (free / premium per day, premium "Coming soon", Claim All). Art: `World/SummonArt` (ArtCatalog `Art/Summon/...`, code-drawn fallbacks; keys in docs/design/summon-events.md). Screenshots: `SummonShowcaseTests` (explicit PlayMode, `-testFilter SummonShowcaseTests`, `$ROGUESTARS_SHOTS_DIR`) |
| Laboratory | `laboratory` | header: Data, next research cost/duration (`LaboratorySystem.NextDataCost`/`NextDurationMs`), slots busy, Laboratory building status (`LaboratoryScreen.BuildingStatus`: owned / built / staffed, slots by stars, speed by level); active research slots with Diamonds/ad/free speed-ups and collect; **tree** per branch (offense/defense/elements/command, `LabTreeLayout`: one row per tier, nodes ordered by their prerequisites, no overlaps): stat nodes steel-edged, skill nodes family-coloured with a SKILL band and on/off, edges gold = ALL prerequisites, thin = ANY; node modal: effect now/next, ANY/ALL prerequisites (other branches named), research button with the block reason, draft-pool toggle for skill nodes |
| Heroes / Mounts | `heroes` / `mounts` | unlock from shards, stars, levels (Hero XP / Fuel), awakening (mounts), equip, trait/weapon skills by star |
| City | `biomass_farm` | `CityScreen` (City design version A, see "City screen" below; from level 6 until the City unlock in camp mode, see "Camp mode"): the iso diorama (`World/CityWorld`) under a slim dock (Build, Workers, Output, COLLECT ALL); tap a building = its card in place (level, stars, placement bonus + reason, worker slots, output / chances / boons, upgrade with cost type, Move / Store / star up, Reactor / Laboratory); tap rubble = dig pop-up; chance badges pop through the shared pull reveal; Build / long press = edit mode with C's rail (tablets: always) |
| Aliens | `aliens` | list by rarity (aliens pass: no levels, no stats): ability card of the current star and the next star's change, act frequency, synergy badges, traits, genes "Coming soon", star up (shards), Breed shortcut (opens the pen with it as parent A), assign to building, squad toggle; squad card: Freeze-vs-Chill hint, the one-time "levels refunded" note |
| Breeding Pen | `breeding` | Grain's only sink and the targeted shard source: pick parents (cost/duration preview, "50% A · 50% B" with the release shards), jobs with timers, collect, replace (new traits) / discard (shards) modal; genes "Coming soon" |
| Observatory | `invasions` | one invasion list (`ObservatoryScreen.MergeInvasions`: rules calendar occurrences matched by `occurrenceId` with the service's attackable instances, server-pushed extras appended with their content names) with Attack per invasion, planetary events, upcoming, modifier stack with countdowns |
| Daily Quests | `quests` | title and hub button "Daily Quests"; no feature, unlock moment or "?" intro (the button shows with Parts, level 1); daily/weekly quests show points only, the five chests on the bar pay (the daily top chest's random tickets flip out in the shared reveal, `QuestsScreen.OpenChest`), achievements are repeating milestones naming the next one, 7-day login calendar, new-player event; quests hidden until their feature (`QuestDef.Feature`) |
| Shop (offer sections) | – (no unlock) | the Shop's second chip row: Offers (starter/rolling/flash/gift), Diamonds, Passes (opens the Passes page; "!" dot while something is claimable there), Items (Diamond items), Ads (rewarded ads) |
| Passes | – (no unlock) | `PassesScreen`, reached from Shop → Passes, the hub's Pass icon (badge = claimable count, on the Shop icon while the season pass is locked), the hub season bar and the Funds / Season pass unlock actions: a scrolling stack of colour cards: **Daily Gift** (`ShopConfig.DailyGiftItemId`, reset countdown, Claim / Claimed), then one card per pass product (`PassProducts`: Monthly Pass gold, the Forever products by price — No Ads purple, Daily Diamonds Forever pink-red — and running gift packs blue): title with one highlighted word (`CardTitle`), ACTIVE tag + green rim when owned, "On purchase" ribbon with the one-time contents, perks checklist from the real config numbers (`Perks`), ticket illustration (`ArtCatalog.PassTicket` = `Art/Shop/Passes/<product id>`, else a tilted kit plate with the pass icon), "Every day" reward slots (open / ready / claimed), time left ("Ends in …", "30 days", "Forever") and Buy / Claim / Claimed / Owned (+ "+30 days" extend while the Monthly Pass runs); below: Season pass and Funds; fixed bottom bar **Claim all (n)** = `PassesClaimAll` (`ShopSystem.PassClaimables`: gift, every owned daily reward, reached season levels, reached fund steps) |
| Mail | – | inbox, open/read, claim, claim all, delete |
| Arena | `arena` | league/points/tickets/Arena Medals, opponents + fight result modal, weekly leaderboard, **medal shop** (`ShopBuyItem`, section `arena`: Awaken Core 200 medals 2/week first, part tokens, Data, Gear Cores; bought n/limit this week, reset countdown, "Sold out"); the Diamond shop no longer lists arena/guild items |
| Guild | `guilds` | online only (offline shows "requires online"): create/search/join, members by weekly invasion damage, chat, guild shop, leave |
| Settings | – | offline/online, server URL, display name, account, a note on run auto mode (it only rolls), **Developer** card (below) |

**Developer card** (Settings, `SettingsScreen.DeveloperCard`): owner dev tools through `IGameService.DevTools`, which is
non-null offline only in the editor and development builds (offline mode is open to every player) and online only when the server's session said `devTools: true` (the player's Firebase uid is in
the server's `dev_tools_player_ids` allowlist, docs/setup.md step 15; the server checks every call, docs/server.md → Dev tools).
- *Reset progress…* → confirm modal → a brand-new player (day 1, starter kit; same player id and name, version + 1).
- *Jump to day…* → modal: day 1–60 with −7/−1/+1/+7 steps (clamped, default 7), profile tabs Casual (~15 min/day), Active
  (~1 h/day, default), Hardcore (~3 h/day) → the simulator's bot plays a fresh account up to that day (offline: on device,
  synchronously — a 60-day hardcore account takes a few seconds; online: `POST /v1/dev/jump`).
- Afterwards the app reloads (`App.Start` on the same service: `/v1/session` online, the save offline) and lands on the hub;
  show `DevToolsOutcome.Summary` inside the Developer card (`ClientApp.DevStatus`), e.g. "Day 14 · active player · level 33" / "Progress reset · day 1"; failures show
  "Dev tools: …" there and change nothing.
- Offline only, in the same card: the save path and *Unlock all features (dev)* (the rules' dev-unlock flag).

**Art (`Ui/ArtCatalog.cs`)** — the one place that maps art to the game: a level's portal family (its tile pool
without "pool_", `ArtCatalog.FamilyOf`) → biome (`FamilyBiome`: ember → `lava`, glacier → `seabed`, static →
`space_station`, void → `shipwreck`, burrow → `prehistoric`, dark (Dark Side) → `ash_wastes`, standard → `alien_jungle`;
unknown → `Default`) → `BiomeArt` (full-frame `background`, seamless `ground`, tile-sprite folder, decor folder, procedural
slab face / side / edge colours, ground dim, placeholder rock colour). Ground images and tile sprites were generated for
different biomes, so a biome borrows the tile set that suits its ground (`ArtCatalog.Make(id, tileSet, …)`: seabed → seabed
slabs, space_station → neon_city, shipwreck → crystal_cave, prehistoric → desert_ruins, alien_jungle → mushroom_forest;
lava / ash_wastes keep the procedural slabs). Every lookup falls back: no mapped biome → the plain dark board;
`GroundOf` = the seamless `ground` if present, else the full-frame `background`, else none; no tile sprite `tiles/<kind>`
(`TileKey`: enemy, shop, stat_buff, boss_gate…) → the biome's `tiles/slab` → a procedural slab in the biome's palette (a tile
sprite is drawn with its own aspect, `RunScreen.SpriteSlabSize`; the kind's colour marker gem and label stay on top); enemy
→ own sprite → family sprite → lettered box; alien → sprite → lettered token (battle, Aliens screen card `AlienSprite`,
Signals reveal card); no hero sprite → the drawn pill worm. Misses are cached; `ArtCatalog.Loader` / `FolderLoader` are
swappable (tests). Unused tile sets (candy_planet, clockwork_city, coral_reef, frozen_future, ice) and grounds (cloud_islands,
gas_giant, toxic_swamp) are imported for future biomes.

**Decor (`Screens/BoardDecor.cs`)** — props scattered by code beside the path (pure, deterministic: seed =
`BoardDecor.Seed(RunId)` (FNV-1a) × segment): for each path tile of a segment a `Density` 30 % chance of a prop (62–100 px) on
each side, a little out from the slab and jittered along the path; a bigger **landmark** (150–180 px) beside every fork and
the boss arena. A prop's footprint (`Radius` = 0.42 × size) keeps `TileGap` 16 from every slab, `EdgeGap` 14 from the drawn
path and `PropGap` 10 from other props, and stays on the board. Segment s is placed only against segments ≤ s, so the board
growing ahead never moves a prop behind; props later covered by a new tile, edge or the arena are dropped, and props never
cover the boss marker. Hand-built boards (no 2D layout) get none. Art: every PNG in `Art/Decor/<biome>/` (sorted by name;
`Variant` picks one, `Flip` mirrors left-right only); a biome with a ground image but no props yet gets **drawn placeholder
rocks** (one or two rocks, a three-rock cluster for landmarks, in `BiomeArt.Rock`) so the system is visible now. Drawn on the
board under the path (`Decor` / `Decor:Landmark` nodes) with a soft contact shadow; recomputed only when the board grows or
the arena opens.

**Importing generated art (`Tools/Art/Import Generated Art`, `Editor/ArtImporter.cs`)** — one command per drop: copies the
eligible files from `~/AI/assets` (env `ROGUESTARS_ART_ASSETS`) and `~/AI/out/pkg-boards` (`ROGUESTARS_ART_BOARDS`) into
`Assets/Art/Resources/Art/…` under stable names, writing only files whose bytes changed:
| Source | Destination |
|---|---|
| `aliens/<id>/`, `simple-redo/alien_*/` | `Aliens/<id>.png` |
| `enemies/<id>/`, `simple-redo/<id>/` | `Enemies/<id>.png` |
| `board-tile-sprites/<biome>/` | `Boards/<biome>/tiles/slab.png` (trimmed to the opaque pixels) |
| `grounds/<biome>/` | `Boards/<biome>/ground.png` (seamless, full-frame) |
| `decor/<biome>_<n>/` | `Decor/<biome>/<biome>_<n>.png` (trimmed) |
| `pkg-boards/<biome>__BB__*.png` | `Boards/<biome>/background.png` (only if missing: curated ones are kept) |

Selection (`ArtImportRules`, tested): only the simple variants `SpriteVariants` = C then S (C preferred unless only S exists;
reorder the list to change it), lowest seed first; `A_` files never; grounds `GroundVariants` G2, G1, C, S; boards only the
distinct `BB` set; `Skip` lists rejected files (`enemies/null_shade/C_s1301`, `enemies/magma_slug/C_s1301`,
`boards/moon_craters/BB_s3001`) — regenerated files of those ids import normally. Batchmode:
`arch -arm64 $U -batchmode -nographics -quit -projectPath $P -logFile /tmp/rs-art.log -executeMethod RogueStars.Client.Editor.ArtImporter.ImportCli`
(prints one line per file: written / unchanged / kept). New ids need no code: enemies and aliens are looked up by def id.

**Adding a screen:** write a `UiScreen` subclass in `Assets/Scripts/Screens/` and add a `ScreenEntry` (with its feature
gate) to `ScreenCatalog.All` (`Todo(...)` creates a placeholder). The hub lists it under "More" once unlocked unless
`HubLayout` gives it a tab or a column slot; the smoke tests pick it up automatically. Extend `Fmt.Reward` / `EventText` for new reward kinds and events. Reward previews use `Fmt.Rewards(rewards, content, state, now)`, which leaves out rewards without a use (GDD §13.1; "—" when none is usable); offers and shop items whose headline reward has no use are hidden.

**Onboarding / tutorial** (`App/Tutorial.cs`, GDD §16): no forced popups besides the unlock moment. Content tutorial
steps are completed automatically (`TutorialAdvance`) as soon as the player has done the thing (counters/state); the hub
shows the next goal with a Go button when it needs another screen (the run goals are the PLAY card). The first run is
played without parts; once Parts unlock (level 1) it asks to equip the starter parts, and when nothing is actionable it
points at `Features.Next`. **The tutorial run** (main level 1, never cleared yet; `App/CoachMarks.cs`): coach marks on
the app's `CoachLayer` (above modals, never blocking input): a bobbing hand pointer and a speech bubble — "Tap the dice
to roll!" on the dice for the first rolls, "Level up! Pick a skill." on a draft card, "The boss is here. Tap to fight!"
on the dice at the boss — one at a time (`RunScreen.UpdateCoach` each frame, hidden during animations; `CoachMark`),
"Skip tips" turns them off for the run. The first run has no signature pick (one offer before `run_signatures`:
`SignaturePick.StartRun` starts at once) and no auto roll. UI helpers of the hub and the moment (rounded cards, planet, icon buttons, PLAY card, teaser,
tabs; procedural sprites, wireframe palette) are in `Ui/HubKit.cs`; `UiScreen.HeaderHeight` = 0 hides the header. There are no toasts (playtest fixes 3, G1): rewards fly into the currency bar, celebrations are hurray banners and
failures an error bubble above the tapped button (`GameFeedback`).

## Vertical slice: iso board, slice HUD, card family, battle (branch `iso-slice`)

The run, the level-up draft and the battle follow three owner-supplied layout references (not in the repo). What replaced
design A (the board, tray and footer described in *Screens* above are superseded where this section differs):

- **Rules grid** (`RunBoard.GridLayout`): every tile sits on an iso grid cell (`RunTile.PosX/PosY` whole numbers, `Dir`
  0/90/180/−90 = the heading it is entered with, `Height` 0/1 = raised sections; neighbours differ by ≤ 1 = a step).
  Shapes: straight runs with right-angle turns, hairpins (wide: out 3, across 3, back 3, on 2; tight: 2, 2, 2, 2 for the
  short trunk stretches between forks; right after a fork only along its exit heading) and forks as rectangles whose
  arms run around the box from the fork tile to the join on the opposite corner (arm lengths share parity, so the
  rules nudge arm A by one toward arm B when they differ). Loops and hairpins leave **set-piece slots**
  (`ActiveRun.SetPieces`: `RunSetPiece` X/Y/W/H, `Shape` loop/hairpin) that no tile ever takes. Invariant: the open end
  is the only tile in its up-quadrant {x ≥ head.x, y ≥ head.y}, every shape stays inside it, so cells never repeat even
  across appended segments. Screen band: `RunTuning.GridBand` (8) on `PosX − PosY`. Tests: `GridLayoutTests`.
- **World board** (`World/WorldBoard`): SpriteRenderers on layer 30, far from the scene origin, an orthographic
  `BoardCamera` rendering into a RenderTexture (24-bit depth: the island's sprite mask needs the stencil) shown by the
  `BoardView` RawImage under the HUD (renders only in play mode on a real GPU). `Iso` = projection (step 0.84 × 0.58 world
  units, a ~1.45 : 1 dimetric matching the references) and painter's order (`Iso.Order`: larger x + y first); the view is
  `WorldBoard.ViewWidth` = 9 units wide (~5.4 tiles across a 1080-px screen, 120 px per unit) with a slight vignette.
  The path stands on one continuous **plinth** at one elevation (the rules' raised sections, `RunTile.Height`, are drawn
  flat: a step at a turn read as one tile cutting into the next): `IsoArt.Block(face, height, mask)` takes the neighbour
  mask (`IsoArt.PlinthMask`, path cells and set-piece floors) and draws wall faces only on exposed front
  edges (`IsoArt.ExposedWalls`), so corner seams appear only where the plinth turns or ends;
  enclosed areas between runs stay ground; walls carry a lit cap, a carved band of hooked spiral keys (repeats every 28 px of screen x, seamless across
  cells), the base ledge, ambient occlusion and a soft contact shadow on the ground; slabs are inset with thin grout.
  Palette: slate stone by default, soft accents only on special tiles (salmon boost, teal event, pale special).
  The **island**: `WorldBoard.IslandField` (chamfer distance from the plinth, radius `IslandRadius` wobbled by noise)
  → `IsoArt.IslandMask` → the tiled ground clipped by a `SpriteMask`, a two-band cliff and a drop shadow under its front
  edges, over the tiled dark surround (`IsoArt.Backdrop`, deep water / rock columns). Scatter is placed by that distance:
  dense ground cover along the plinth, mid props further out, large pieces (broken pillar, antenna pylon) at the rim
  where they never stand in front of the path; every prop, marker and the worm gets a soft shadow
  (`IsoArt.SoftShadow`). Drawn: tiles in a depth
  window around the worm (22 behind, 46 ahead; only trail + arena once the arena opens), each a `Block` (slab sides +
  platform wall with the glyph trim on exposed edges) and a `Top` (face colour per kind, engraving: boost and curse
  chevrons pointing from the tile to the next tile in walking order, `WorldBoard.PathHeading`: corners point the new way,
  forks down their first arm; event "?", hazard spikes), the kind's **marker** prop standing on it (`IsoArt.MarkerOf`:
  enemy, elite, miniboss, boss, event, mystery = item, chest, rest = campfire, shop, minigame, portal = start, reward =
  loot/shard; hidden under the worm; beaten enemies / taken pickups leave on landing), set pieces on slot floors
  (`WorldBoard.SetPieceFor`: loops get the portal well / spire / mushroom, hairpins the standing ones), scatter props on
  the island (see above), no "!" markers (owner, 2026-11-26), the worm (hero sprite, `WormHeight` 1.8 units tall).
  Markers are cropped tight (`IsoArt.MarkerHeight` = visible height): the common enemy is a small uniform gold skull,
  elites (horns), minibosses and the boss gate (crowns) step up; boss tiles (`TileCategory.BossTile`) show a small omen
  obelisk.
  Landed tiles: the top takes `WorldBoard.LandedTint(accent)` (landed-only colouring kept). Hops, dice, landing pop and
  popups are ported; fork tap targets (`BranchPick:A/B`, `BranchOption:A/B`) and rings are uGUI pins kept over their
  tiles every frame.
- **Camera** (`RunScreen`, pure helpers tested in `BoardCameraTests` at 16:9 … 21:9 and tablets): the worm's middle
  (`WormFocus` 0.9 units over its feet) sits **horizontally centred** and at `FocalShare` (0.55) of the **open band**
  between the top HUD (`HudTopRef` 340 ref px × width/1080) and the dice cap (`ControlsTopRef` × `ControlsScale`):
  `FollowCentre(feet, holder, width)`. The view is `WorldBoard.ViewWidth` (9 units) wide, wider on short screens so the
  band shows ≥ `MinBandUnits` 6.5 units (`DefaultViewWidth`, ≤ 14 on tablets); the bottom controls scale by
  `ControlsScale` = min(width/1080, height/1920) in a bottom-centred 1080 × 2400 frame. (Before: the camera aimed at
  80 % of the worm's x — "leaning to the middle", up to ~1.3 units off-centre on the side bands — and put its feet
  9 % of the view height below the centre, so on tall phones the worm sat low, near the dice button.) The board's
  render target keeps the view's aspect (both sides scale down together past 2048 × 4096); a size change of the screen
  (late safe area, rotation) rebuilds. The camera follows with `RunFx.CameraStep` (sharpness 4/s; it keeps pace with
  the board speed while the worm moves). **Look around**: one-finger drag pans, pinch (and the mouse wheel in the editor)
  zooms about the fingers / cursor (`BoardGestures` on the board view and on the tap-target overlay, one shared
  `BoardGestureState`; a drag that starts on a fork target pans and cancels its click, a tap still picks); the width
  stays within `MinZoom`..`MaxZoom` (0.8–1.9 × default), the centre within the drawn path (`WorldBoard.PathBounds`) plus
  1.5 units. The view stays where the player left it until a **roll or a move**, which eases position and zoom back to
  the follow framing (smoothstep, `ReturnSeconds` 0.4 s; `RunScreen.MoveView` / `ReturnCamera`).
- **Board speed** (×1, the speed chip divides): dice 0.9 s by hand / 0.7 s in auto (`RunFx.DiceRollSeconds`), a rest
  beat 0.2 / 0.18 s, 0.38 s per hop / 0.23 s in auto (`HopStepSeconds`), the landing pop 0.7 s (`LandPopSeconds`), a
  beat of 0.4 / 0.3 s before a fight or a decision covers the board (`RunScreen.PreDecisionSeconds`).
  RogueStars.Sim's `RunClock` mirrors the dice and hop times. The dice start tumbling on the tap (pre-roll, before
  the server answers) and land on the answer's values, the whole roll staying ~0.9 s. A tap on the dice while the
  board animates finishes everything up to the fight / decision at once (`UiSequence.FlushUntil(SkipMark)`); a tap
  while the roll is on its way does nothing.
- **Tap feedback** (`UiKit.UiPress` on the canvas root): every button presses (scale 0.94) on pointer down; a tap that
  sends a command keeps it pressed and dimmed (taps ignored) while `GameSession.InFlight` > 0.
- **HUD** (`Screens/SliceHud`): EXP hexagon + level pill with the XP fill, HP pill (heart), ATK (sword) and DEF (shield)
  pills (thick dark outline, inner lip and gloss), cog = pause (pending loot is in the pause sheet), coin chip; the boss
  rail down the left edge (a thick capsule, boss skull, a framed skull pip per clock miniboss, a gradient fill blue → the
  awakening's colour, dice icon and "n/N"). Bottom: the **dice stand** (`World/DiceStand`): a glossy red key cap on a
  carved stone pedestal (slab with a carved border on a coursed, staggered-joint column running off the screen's bottom
  edge), both a small 3D model drawn through **one** perspective camera (front, 34° above) into the same frame, so the
  cap's base is its footprint on the slab (contact shadow, occlusion line), its walls converge like the column's and its
  top foreshortens like the slab's (`HudArt.Pedestal` / `HudArt.DiceButton`; always code-drawn — an imported
  `Art/Slice/Hud/pedestal` would not match the cap's camera). `RollButton` (the frame; its `Cap` pulses about the base):
  tap = roll / fight / reopen the decision, hold = auto; caption `RunScreen.DiceCaption` engraved on the cap's front wall
  (`DiceStand.FrontWall01`); two white dice printed into its top plane (`DiceStand.TopPlane`: the camera's squash),
  `DicePrint` → `FooterDice`: the last roll, a one-die roll beside its opposite face, `RunScreen.PrintedDice`),
  `SpeedChip` (board animations ×0.5/×1/×2/×3, one button cycling through them on a tap, `PlaySpeed`, `RunScreen.BoardSpeed`), the
  skills book (`SkillsButton`, count badge `SkillCount`). No skill tray, no footer, no Loot / AUTO buttons. Chunky text:
  `SliceKit.Chunky` (Fredoka, white, double dark outline + drop).
- **Cards** (`Screens/SliceCards`): the draft is a full-screen overlay over the dimmed board: hero, gold "LEVEL UP!"
  ribbon (red "MYTHIC!" after a miniboss), "Select a Skill to learn!", one card per offer (tier tag, framed icon tile in
  the tier colour, name ribbon, cream panel with `SliceCards.Keywords` colouring numbers green and statuses / stats blue,
  a chip for LEVEL n → n+1 / UPGRADE / SYNERGY, "?" = the skill tooltip), purple "Reroll (n)". The shop uses the same cards
  (blue "SHOP" ribbon, price button per card, Reroll / Leave).
- **Battle** (`BattlePlaybackScreen`): top ~62% the arena (3/4 top-down floor `HudArt.Arena`: sand, a dark reef band at
  the back, cracked slabs, rocks and kelp at the edges — or the slice-battle art). The hero group (the worm, 380 px, seated
  on its mount: `SeatOnMount`, ArtCatalog `Art/Slice/Mounts/<mount id>` or `/mount`, else the drawn hover saucer
  `HudArt.Mount`) takes about half the arena left of centre (`StagePosition`: the player side's last fighter = the worm);
  companions line up small along the left edge (`CompanionPosition`; the mount's own slot shows nothing); enemies (330 px)
  stand right. HP / shield / rage bars under each, status icons with stack counts, the slice HUD with the worm's live HP
  and live ATK / DEF (Buff entries on the worm, resolved through the skill's BuffStat effect and its duration),
  "Round n/N", `SkipButton`; huge outlined damage numbers; EXP gems and coins burst from a beaten enemy to the HUD; a
  firing skill's label floats in the arena over the worm. Bottom (a dark gradient panel): a 5-column grid of tier-framed
  skill icons without labels right under the arena (`Slot:<id>`, 15 tiles, then "+N"); fires pop their tile
  (`SkillFireQueue`). One speed button bottom-left (`SpeedChip`, the same `PlaySpeed.BuildChip` as the board: shows the current speed, a tap goes ×0.5 → ×1 → ×2 → ×3 → ×0.5, ×3 skipped without the monthly pass), `ContinueButton`, `Log` bottom-right. The worm's art faces left, so it is mirrored while it looks right (`WormFacing`): in battle it starts turned to the enemies and turns to each target (the mount under it turns with it), on the board it looks where it hops (`WorldBoard.FaceToken`, ground contact kept).
- **Art keys** (`ArtCatalog`, every one falls back to a code-drawn placeholder in `World/IsoArt` / `World/HudArt`):
  `Art/Slice/Markers/<id>` (enemy, elite, miniboss, boss, omen, event, mystery, chest, rest, shop, minigame, portal, reward),
  `Art/Slice/SetPieces/<id>` (crashed_saucer, portal_well, crystal_spire, giant_mushroom, ruined_arch, burrow_mound,
  time_clock, alien_tree; alternates `<id>_2`, `<id>_3` …: `WorldBoard` picks one per slot by a hash of its cell,
  `IsoArt.SetPiece(id, pick)` / `ArtCatalog.SliceSetPieceVariants`; loops take portal_well / crystal_spire / giant_mushroom,
  other slots any of the standing seven), `Art/Slice/Scatter/<id>` (rock_small, rock_pile, bush, flowers, fern, pebble_glow, stump,
  crystal_small, mushrooms, barrel, antenna, bones), `Art/Slice/Hud/<id>` (dice, pedestal, heart, sword, shield, coin,
  exp, cog, skull, book, speed, reroll, boss_rail_top), `Art/Slice/Textures/<id>` (tile_top_moss — multiplied into tile
  tops, imported readable; tile_side_rock; ground_meadow = the tiled island ground; backdrop_deep = the tiled dark
  surround), `Art/Slice/Mounts/<mount id>` (or `mount`: the battle mount under the worm), `Art/Slice/Battle/<id>` (battle_meadow;
  battle_meadow_ruins), `Art/Skills/<skill id>` (draft cards, battle grid). Import: put the owner's picks in
  `tools/art/slice-picks.json` (`{"slice-markers/enemy": "C_s1301.png", …}`, files under `<assets>/<category>/<id>/`; a value
  with a "/" is a path under `<assets>/<category>/`, for alternates: `"slice-setpieces/giant_mushroom_2":
  "giant_mushroom/J_s5201.png"`) and run `Tools/Art/Import Generated Art` / `ArtImporter.ImportCli`
  (`ArtImportRules.PlanSlice`; categories slice-markers / -setpieces / -scatter / -hud / -skills / -texture / -battle and
  run-characters / run-props / run-icons → `Art/Run/…`; props, NPCs and icons are trimmed, textures and arenas kept
  full-frame). Nothing is auto-picked. **Imported (2026-11-25):** markers enemy, elite, miniboss, event, mystery, chest,
  rest, shop, minigame, portal, reward; all eight set pieces (+ alternates burrow_mound 2–3, crashed_saucer 2,
  crystal_spire 2, giant_mushroom 2–3, ruined_arch 2, time_clock 2); `Art/Run/Props` chest_common, chest_signature,
  abandoned_cart, campfire_big, event_shrine, event_wreck (chest_mythic keeps its placeholder); `Art/Run/Characters`
  shopkeeper, cursed_vendor, black_marketeer, camp_cook. They came from the cleaned packages (`~/AI/out/cleaned/pkg-…`,
  characters from `~/AI/out/pkg-run-characters` C variants), cut out with `tools/art/export_assets.py` into a staging
  root laid out as `<category>/<id>/<variant>_s<seed>.png` and imported with `ROGUESTARS_ART_ASSETS=<staging root>`;
  `.fix` files had background trapped inside the object cleared (between legs, inside the tree's loop, a window),
  `.us` files had the pale baked ground shadow removed. Import settings (`ArtImportSettings`): sprites, no mips, max
  1024 for `Art/Run`, markers and set pieces, compressed.
- **Import 2026-11-26** (owner "use this"): set pieces burrow_mound, crystal_spire, giant_mushroom, ruined_arch, time_clock = comic redraws `~/AI/out/cleaned/setpieces/*.png` cut out with the export_assets cutout into `~/AI/assets/slice-setpieces/<id>/comic.png`; props campfire_big (`run-props3` s6321) and abandoned_cart (s6323). White backgrounds trapped inside / baked pale shadows were cleared as `.fix.png` for hub icons shop, patrol, tab_worm, HUD cog, reward coins_small; `ArtImportTests.ImportedIcons_HaveNoOpaqueWhiteBackground` scans all Hub / HUD / Result / Reward icons.
- **Captures**: `SliceShowcaseTests` (**Explicit**) renders `s1_board_start`, `s1_board_mid`, `s1_board_zoomed` (pinched
  out and dragged), `s2_levelup`, `s2_levelup_tooltip`, `s3_battle`, `s3_battle_later`, `s3_battle_tooltip` at 1080×2400 (or `$ROGUESTARS_SHOT_SIZE`, e.g.
  `1440x3200`) into `$ROGUESTARS_SHOTS_DIR` (default `client/RogueStars/Screenshots/slice/`).
  Tests: `IsoBoardTests` (projection, faces/markers, ArtCatalog swaps, world contents, landing, dice caption), the run /
  battle tests updated to the slice names.

## Screens pass: rest, events, chest, shop, run result, home (branch `screens-pass`)

Six screens rebuilt to owner-supplied layout references (not in the repo; layout and design language only). Where this
differs from *Screens* and the slice section above, this wins. Geometry is in reference pixels of a 1080 × 2400 frame
(`RunScenes.Frame`: scaled by min(w/1080, h/2400), centred, top-anchored), so shorter screens keep the layout.

- **Rest (campfire)** (`RunScreen.Decisions.cs` `SceneModal`, `Screens/RunScenes.cs`): an opaque overlay with the
  **stage** on top (`RunScenes.Stage`: the battle arena art, the worm seated on its mount left of centre, up to three
  squad aliens down the left edge, the event's prop right on a soft glow, the run HUD (`SliceHud`) and the skills book),
  the dark **title band** ("A Moment to Rest"), the paper **panel** with the flavour line and two **option tiles**
  (`RunScenes.OptionTile`: label above, a purple framed icon tile = the button, the outcome under it). **Train** (owner
  rule) opens `TrainPicker`: one card per `RunSystem.TrainChoices` skill (the card family, "LEVEL n → n+1" chip, the
  upgrade inline; a chain's lower tier shows its upgrade, "UPGRADE"); picking sends `RunChooseEventOption { SkillId }`.
  Nothing to train: the tile says "+40 coins" and Train pays them. Buttons: `Button:Rest`, `Option:Rest` / `Option:Train`
  aliases, `Train:<skill id>`.
- **Events**: two layouts (`RunScenes.Look(eventId)`, tested for every event): **scene** (as the rest, the prop by event:
  `abandoned_cart`, `chest_common` for the treasure pod, `event_shrine`, else `event_wreck`; 2–3 tiles) and **vendor**
  for trades offered by a character (smuggler / black market → `black_marketeer`, cursed relic / unstable reactor →
  `cursed_vendor`, alien medic → `camp_cook`): stone wall, the NPC behind a counter, the red title ribbon, a speech bubble,
  two cream cards with the **blessing** half (gains) and the **curse** half (costs; "Gain nothing at all." for a decline)
  and **Sign** / **Pay** / **Leave** buttons with "OR" between. Option text (`RunScenes.EffectLines` / `OutcomeText`):
  one line per effect, gains' numbers green, losses' red; a gamble lists each outcome ("65%: +1 account loot, Boss wakes
  +8"), its tile shows the dice. Buttons keep their names (`Button:<option text>[ cost]`). The **result** sheet
  (`OutcomeSheet`) is restyled (dimmed board, caption, gold / red ribbon with the title, "You chose", the paper panel
  with the chance roll and one row per gain / loss, a dark-blue Continue) and keeps its names (`Modal:Outcome`,
  `ChanceMeter`, `ChanceResult`, `Line:<kind>`, `Icon`, `OutcomeContinue`) and the spin.
- **Chest** (`ChestIntro` / `BuildChest`): the board dimmed, a ribbon ("You found a Chest!", red for a Mythic, purple
  for a signal), the big chest on a glow, "Tap anywhere to open!" (`ChestTap`); the tap bursts it and reveals the
  content in the card family. Used for the **Mythic choice** after a miniboss (`chest_mythic` → the MYTHIC draft), the
  **signature offer** (Resonance, `chest_signature` → `SignatureReveal`: one card per offered signature + the coins) and
  **guarded loot** (a treasure result with account loot, `chest_common` → the result sheet). It always waits for the
  player's tap (auto mode only rolls); skip mode / EditMode (`ShowChestIntro`, tests: `ForceChestIntro`) goes straight to the content.
- **Shop** (`ShopModal`): wooden interior, the run HUD (its coin chip updates on every buy), the **SHOP** sign, the
  shopkeeper with a speech bubble, the utility offers (heal, stat perks) as small priced tiles, the skill offers as cards
  (tier tag, framed icon, name + level, coloured keywords, a yellow price button with the coin; **SALE!** with the struck
  list price when `ShopOffer.ListPrice` > `Price`), "?" = full text, green **Exit** (`Button:Leave`), purple **Reroll**
  with its price, the skills book; "n bought this run" (`ShopCoins`). Buy buttons sit in their own `OfferSlot:<i>` with
  the offer's name.
- **Run result** (`Screens/RunResultScreen.cs`, full screen): the dimmed arena, the crest (`crown`, or `crest_broken` on
  a defeat) on a halo, the ribbon **VICTORY!** / **DEFEATED** (`Banner`), the pennant (`banner` / `banner_defeat`) with
  the level tag, "You Survived / N Rolls / Longest Journey: N Rolls" (`RunSummary.LongestRolls`) and the three stars,
  the full-width **reward strip** (tiles: mark + "x1,250", the name under it; `Reward`; identical rewards — same kind, id
  and rarity — summed into one tile, `RunResultScreen.Aggregate`), chips (run level, `SkillsChip`, minibosses, shop buys;
  no list of the skills gained), `DoubleRewards`, **Continue** (`Button:Portals`) and
  `Again` (Next: Level n / Play again / Try again).
- **Home** (`HubScreen`): absolute layout over a sky; season bar (once the pass unlocks: tier hexagon, progress,
  "Completed!", `SeasonBar` opens the shop), left / right **feature columns** (`HubLayout.LeftColumn` + live events,
  `RightColumn`) as art icons with chunky labels, a red count badge and a timer pill (`Side:<id>`, `Live:<id>`), the
  `PortalChip`, the next level's title and **"Longest survived: N rolls"**, the **home planet** by stage
  (`HubScreen.PlanetStage`: main levels < 5 / 20 / 50 / 100 / more → `planet_stage1…5`, drawn placeholder per stage)
  with the worm on it, the **feature row** once Parts unlock (Portal Map, Chapter Rewards → portals, Farm Patrol → City
  once the farm unlocks, Dark Side once unlocked; `Feature:<id>`), the guide (NEW unlock, goal, next-unlock teaser), the
  big yellow **START** (`PlayButton`, `ContinueButton` during a run; `HubNextLevel` above it, stamina cost) with
  **Modes** once a side mode unlocks (no auto-pick chip: auto mode only rolls), and the tab bar (dark tabs, the Planet raised
  in purple with its label; NEW / ▲ UP badges). Features still appear only as they unlock; locked ones are never shown.
  The top bar (`ResourceBar`) is restyled for every screen: purple band, the worm's portrait in a gold frame with its
  level, the name and power, dark currency pills.
- **Type scale** (owner feedback: text slightly too small, measured against the references at 1080 × 2400): every
  `Ui.Label` / `Ui.Display` size passes `Theme.Type` (× `TypeScale` 1.15; ≤ 24 × `SmallTypeScale` 1.1); chunky text
  (`SliceKit.Chunky`) and the rebuilt screens use exact reference sizes; the card body (`SliceCards.BodySize`) 38 → 46,
  card names (`TitleSize`) 54 → 58, best-fit within the panel. The display font has no "→": chunky text and card bodies
  show "»".
- **Art keys** (`ArtCatalog`, every one with a code-drawn fallback in `World/ScreenArt`; imported so far: the
  characters and props listed in the slice section's import note):
  `Art/Run/Characters/<id>` (shopkeeper, cursed_vendor, black_marketeer, camp_cook), `Art/Run/Props/<id>`
  (chest_common, chest_mythic, chest_signature, abandoned_cart, campfire_big, event_shrine, event_wreck),
  `Art/Run/Icons/<id>` (coins_small, coins_big, heal, max_hp, skill_book, upgrade, reroll, curse, blessing, sale_tag;
  stamina is drawn only), `Art/Hub/Planet/planet_stage1…5`, `Art/Hub/Scene/<id>` (sky, or sky_stage1…5),
  `Art/Hub/Icons/<id>` (tab_worm, tab_city, tab_planet, tab_signals, tab_arena, shop, offer, pass, mail, more, quests,
  observatory, invasion, event, map, chapter_rewards, patrol, challenges, settings), `Art/Result/<id>` (crown,
  crest_broken, banner, banner_defeat).
- **Captures**: `ScreensShowcaseTests` (**Explicit**) renders `sc_board`, `sc_levelup`, `sc_rest`, `sc_rest_train`,
  `sc_event`, `sc_event_vendor`, `sc_event_pod`, `sc_event_shrine`, `sc_event_wreck`, `sc_event_cursed`, `sc_event_medic`,
  `sc_event_result`, `sc_chest`, `sc_chest_reveal`, `sc_shop`, `sc_result_victory`,
  `sc_result_defeat`, `sc_home` at 1080 × 2400 (`$ROGUESTARS_SHOT_SIZE`, `$ROGUESTARS_SHOTS_DIR`; `$ROGUESTARS_SHOTS_ONLY`
  = comma-separated groups: board, levelup, rest, event, chest, shop, result, home). Tests: `ScreensPassTests`.

## Run mini-games (GDD §3.6)
A GAME tile (`RunPhase.Minigame`) opens `MinigameHost` as the run screen's decision (`RunScreen.ShowDecision`): it sends
`RunMinigameStart` (the rules pick the game, seed and reward type), shows the title, the one-line instruction
(`MinigameDef.Instruction`), the prize ("RUN COINS" / "SKILL ROLL") and an animated hint (a finger tapping, dragging or
swiping on the game's highlighted target, `MinigameLogic.HintTarget/HintTo`), then **PLAY** (or a tap on the arena) →
3-2-1 → the game (timer bar, status line) → the tier medal → `RunMinigameFinish {Tier}` → the run screen's result sheet
("MINI-GAME RESULT", "Medal: Gold", the coins or "Skill roll: pick 1 of 3 (Epic odds)") and, for a skill roll, the draft.
**Skip** finishes as Miss. Auto mode pauses at the tile (it only rolls); `RunAutoStep { Farming }` (bots, simulator) lets the rules resolve it as Miss.
- **Resolved on the device:** each game's class owns its tier thresholds (`Thresholds` = Bronze/Silver/Gold scores, one
  place per game); the rules accept any tier once per start. Rewards per tier: GDD §3.6.
- **Logic/view split:** a game is pure C# (`System.Numerics.Vector2`, the rules' `Rng` from the seed — deterministic),
  stepped at a fixed 60 Hz (`MinigameLogic.Dt`) in a 900×1100 arena (bottom-left origin); it draws a list of `Shape`s
  (circle, rounded box, ring, text, lines as rotated boxes) that `MinigameRenderer` maps onto pooled `Image`/`Text` nodes
  (smooth 256-texel discs and rings for big shapes). Juice: floating texts (`Pop`), screen shake, squash/flip/trail
  animations computed in the logic; the medal pops in with `UiTween`. Input: `MinigamePointer` (touch and mouse) →
  `Down/Move/Up` in arena coordinates.
- **Scripted player:** `MinigameLogic.Bot(aim)` plays well while the score is below the aimed tier and holds back once
  there — used by the tests, the playthrough and the screenshot showcase.
- **Test flags:** `MinigameHost.Instant` (no intro / countdown / medal waits), `AutoTier` (finish with a tier right after the
  start), `Autoplay` (the scripted player plays, aiming a tier), `TimeScale`; `Advance(dt)` / `PlayToEnd(aim)` drive it in
  EditMode.
- Board: GAME tiles are labelled "GAME" in mint (`Theme.TileLabel/TileAccent`).

| # | Game | Input | Duration | Scoring | Bronze / Silver / Gold |
|---|---|---|---|---|---|
| 1 | Portal Slip | tap | ~10–15 s (25 s cap) | slips through the pulsing portal in 5 attempts (window and period shrink; a slow attempt fails after 2.5 cycles) | 2 / 3 / 5 slips |
| 2 | Comet Catch | drag (worm along the ground) | 20 s | +1 per icy comet eaten, −2 per fiery one (shadows show where they land) | 5 / 10 / 15 |
| 4 | Saucer Landing | drag (saucer on a spring, wind gusts) | ~15–25 s (28 s cap) | 3 landings × (softness 0–50 + accuracy 0–50); off the pad 0 | 90 / 170 / 240 pts |
| 5 | Clock Stopper | tap | ~10–20 s (30 s cap) | 6 target hours; ≤ 6° = 3, ≤ 15° = 2, ≤ 30° = 1 (4 s per target) | 6 / 11 / 15 pts |
| 6 | Wormhole Dash | tap (switch lane) | 22 s | +1 per coin, −3 per asteroid, speeding up | 8 / 16 / 24 coins |
| 7 | Shield Parry | tap (3 coloured shields) | ~21 s | 18 shots; matching shield within ±0.17 s of impact = parry; idle presses lock the shields 0.4 s | 6 / 11 / 15 parries |
| 9 | Slingshot Spore | drag back + release | ~15–25 s (30 s cap) | 8 spores at aliens on bobbing platforms (aim arc shown) | 2 / 4 / 6 hits |
| 13 | Asteroid Slice | swipe | 20 s | +1 per rock sliced, −3 per friendly alien's rock | 6 / 12 / 18 |
| 14 | Jelly Stack | tap (drop) | until 3 misses (26 s cap) | tower height; overhang is trimmed, perfect drops regrow | 4 / 8 / 12 blocks |
| 15 | Signal Echo | tap (4 aliens) | ~5–25 s (30 s cap) | rounds echoed (signals of 2…7 blinks); a wrong alien or a 4 s pause ends it | 2 / 4 / 6 rounds |
| 16 | Fossil Flip | tap (cards) | 28 s (rift closes) | pairs of 8 found (all shown for 1.4 s at the start) | 3 / 5 / 7 pairs |
| 21 | Codebreaker Hatch | tap (6 colours, 4 slots, no repeats) | ~15–30 s (30 s cap) | 7 − tries used (6 tries; gold/white pegs) | cracked by try 6 / 5 / 4 |
| 25 | Odd Alien Out | tap | ~10–27 s | 5 rounds (3×3 … 5×5); 5 pts under 1.2 s … 2 under 5 s; a wrong tap costs 1.2 s | 8 / 14 / 20 pts |
| 26 | Slot Pod | tap (SPIN, stop each reel, HOLD) | ~15–25 s (30 s cap) | 4 spins: 3 of a kind 12 pod coins, a pair 4; HOLD a reel for 3 pod coins (in-game coins only) | 6 / 14 / 24 pod coins |

Screenshots: `MinigameShowcaseTests` (PlayMode, **Explicit**) renders an intro and every game mid-play at 1080×2340:
`ROGUESTARS_SHOTS_DIR=/tmp/mg arch -arm64 $U -batchmode -projectPath $P -logFile /tmp/rs-mg.log -runTests -testPlatform PlayMode -testFilter MinigameShowcaseTests -testResults /tmp/rs-mg.xml`

## Running
- **First:** `scripts/fetch-unity-packages.sh` (once per checkout/worktree; downloads Firebase App/Auth and EDM from
  Google's archive into `Packages/google/`, cached in `~/Library/Caches/rogue-stars-unity-packages`). Without it Unity cannot
  resolve the packages. For device builds with Firebase run `scripts/fetch-firebase-config.sh` (writes the git-ignored `Assets/google-services.json`; `scripts/release.sh` does it itself).
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
(`ROGUESTARS_SERVER_URL` (+ Firebase auth), `ROGUESTARS_AUTH`, `ROGUESTARS_STORE`, `ROGUESTARS_ADS`, `ROGUESTARS_PRODUCT_SET`,
`ROGUESTARS_PLAY_GAMES_APP_ID`/`_WEB_CLIENT_ID`, `ROGUESTARS_MAX_SDK_KEY`/`_MAX_REWARDED_AD_UNIT` (ads `max` only),
`ROGUESTARS_KEYSTORE`/`_KEYSTORE_PASS`/`_KEY_ALIAS`/`_KEY_PASS`), resolves the Android dependencies, sets version
`0.1.<git commit count>` (versionCode = commit count; `0.1.0`/1 without git) and writes **both**
`client/RogueStars/Builds/RogueStars.aab` (Play) and `client/RogueStars/Builds/RogueStars.apk` (sideload) — package
`com.oddbyte.roguestars`, IL2CPP, ARM64, minSdk 26, portrait; `ROGUESTARS_OUTPUT=apk|aab` builds only one. Signed with the
upload key when `ROGUESTARS_KEYSTORE` is set (docs/setup.md step 12), otherwise debug-signed. Each artefact's manifest is
checked for `com.android.vending.BILLING`. It exits with code 1 on failure.

## Tests
EditMode (`Assets/Tests/EditMode`, 233 tests; `RunPresentationTests`: dice skip mode (lands on the value, never early, two dice in the footer), follow camera bounded per frame and never reset by a roll/rebuild, only the landed tile coloured, a popup for every tile kind and outcome line, landing popup on the board, HUD HP/ATK/DEF/level/XP with run perks, rarity colours (slots, draft cards), no sheet after a shop; `RunDepthUiTests`: MYTHIC choice, skill count, shop in place, skills gained, no early surge, encounter step, auto never answers a choice, cleared tiles):
- `RunDesignATests` (run screen design A + battle): the top strip (no app header, pause / HP / ATK / DEF / SKILLS = the
  rules' `WormStats` / `BuildSize`, level ring filled with `LevelInfo` XP, coins, the pause modal names the level), strip
  above the board and tray / ROLL with dice glyph / AUTO in the footer; the boss clock (markers at 25/50/75 %,
  passed ones bright, fill = awakening, none in the Burrow); the skill tray (tier colours, "Lv 2/3", 7 tiles with outlined
  empty slots, 6 + "+N" that opens the full list); battle skill fires mapped from the log (only the player side, "Name · Burn
  ×2", "120 dmg", "+30 HP", most active first, part / mount skills get a tile), skip mode counts every fire without
  animation and shows the worm's logged HP + run ATK in the strip, animated fires queue per tile (one at a time, same effect
  ×n, max 3 waiting, speed shortens them, drained after the fight); `ArtCatalog` fallbacks (unmapped family → procedural
  board, missing ground, per-kind → slab → procedural tile sprite, cached misses, slab colours per biome) and the imported
  art (hero sprite = token and battle worm, lava ground behind the path, placed 1:1 by `GroundLayer.Phase`).
- `ArtPassTests` (art pass 1): enemy sprites own → family (scaled, role glow) → lettered box, alien sprites in battle, on the
  Aliens screen and in the Signals reveal; every portal family mapped to its own biome with an imported ground, imported tile
  slabs, seamless ground preferred over the background; the importer's rules on a fake tree (only C/S, C preferred and
  configurable, lowest seed, A_ never, skip list, simple-redo routing, trimmed slabs/decor, BB boards only, curated
  backgrounds kept, stable sorted plan); the imported enemy/alien sprites present and the skipped ones absent; ground copies
  upright (UVs never flipped, no negative scale), full-frame crossfade bands vs seamless edge to edge, `Phase` 1:1 and
  wrapping by whole pitches; on the run screen ground and decor move exactly with the tiles over 40 drags (the ground keeps
  its offset to the board modulo the pitch); decor never overlaps tiles, path edges or other props, stays on the board,
  is deterministic per seed and differs between runs, puts landmarks beside forks / the arena, keeps earlier segments' props
  when a segment is added, none on hand-built boards, three grown level-1 runs clear, prop art replaces the placeholder rocks.
- `MinigameTests`: every game × every tier (56 cases) reaches the tier with its scripted player on seed 7919 within 30 s of
  simulated time and draws its scene; the catalog covers the rules' 14 games with rising thresholds; same seed = same game;
  on the run screen a GAME tile stops the worm and opens the host (name, instruction, prize), Gold → coins + "MINI-GAME
  RESULT · Medal: Gold"; Skip = Miss consolation; intro → PLAY → countdown → an idle game ends by itself → finish; a
  Silver skill roll opens the draft after the result; board label "GAME".
- `RewardVisibilityTests` (rewards follow use, GDD §13.1): `HubLayout.CurrencyOrder` covers every mapped currency, the visible
  currencies equal the rules' mapping (new account: stamina + Diamonds; Parts adds Ore; an Uncommon part Gear Cores; dev unlock:
  all), resource bar and details list only usable currencies and every balance, the first run's result shows no Biomass/Ore
  tiles while the Parts unlock moment shows "+20 Diamonds", "3 Parts" and the stashed Ore, reward previews (calendar, milestone
  chest, level details) leave locked rewards out, the login/newbie calendars and the shop never name a locked currency.
- `GearScreenTests` (the owner's level-2 playtest: main level 2 reached for real, Parts unlocked): Ore pays a part level
  from day 1 (Common level 1→2 = 30 Ore, no Gear Cores below level 10, the unlock gives 500); the Worm tab lists every owned
  part with EQUIPPED markers (equipped first); slot tabs / rarity picker / sorts (Level, Slot) filter and order the tiles;
  each body slot opens its inventory filter (both arms → Arms, no modal); part detail shows "30 Ore", READY and the skills,
  LEVEL UP spends the Ore and reopens on level 2, without Ore it is disabled and says what is missing, "what is Ore?" opens
  the explainer; the ▲ UP Worm tab badge and slot and tile ▲ badges appear (no callout, no auto-equip: removed in playtest fixes 3)
  only while affordable; the Ore chip explains Ore ("You can level up 3 equipped parts right now"); on the Worm it has no Go button (
  the current screen); with everything unlocked every visible currency's explainer shows its `RewardUses` text and its
  Go opens the screen of its use (Ore → Worm, stamina → Portals, Diamonds → Shop, Data → Laboratory, medals → Arena); the
  "All" list shows every use; a new account's Diamonds go to the Portals.
- `HubOnboardingTests`: the day-1 hub of a brand-new account (only PLAY, the Planet tab, the teaser "Parts — clear level 1",
  the stamina, Diamonds and profile chips; nothing greyed out), level 1 cleared for real → unlock moment (name, its intro
  panels one by one with NEXT, then description, +20 Diamonds, one action "EQUIP YOUR PARTS") claims the gift and opens
  the Worm, back = hub with the Worm tab (NEW) and the equip goal; tabs, side icons and currency chips appear as features
  unlock; every unlocked catalog screen is reachable (tabs, columns, More, profile); the teaser shows the level to clear
  ("City: Biomass Farm — clear level 6", "4/6"), never a day; level 6 cleared → the City moment the same day ("BUILD THE
  FARM", "Next at level 7: Auto Roll"); a run-content unlock (Shops: "NEW IN YOUR RUNS", dots, art, the panel animation,
  NEXT / SKIP, PLAY → portals); backing out does not reopen it every frame; Continue replaces PLAY during a run.
- `FeatureIntroTests`: every intro art key has a drawn fallback; every feature's unlock screen shows each panel (name, line,
  art, animation kind with its hand / counter / bar) and ends on its guided action; every feature has a screen whose "?"
  replays it; "?" on the Laboratory replays without claiming and GOT IT returns; the Worm's "?" lists Parts and Worm
  Level; the Portals "?" stays hidden until a run feature unlocks.
- `TutorialRunTests`: the first run's coach marks (roll on the dice, pick on a draft card, the boss; one at a time, gone
  with the screen, never blocking input), Skip tips, none on later runs, no signature pick and "Tap to roll" (no auto) before
  their unlocks.
- `UiLookTests`: UI kit (rounded sliced sprites, lip + dark caption on the orange face, Fredoka loaded, glyph fallback, chips,
  cached ring sprites), portal map node states (`NodeStateOf` truth table, zig-zag `MapX`), fresh save (level 1 glows, level 2
  locked, boss node at level 10 with its CHEST chip, the path climbs, connector dots, bottom card with goals / START RUN / cost),
  cleared level with stars + subtitle progress + node selection, run board look from the run state (`LookOf`: visited / current /
  fork / branch option / gate, labels and colours, flipped `DrawPoint`, tiles / token with bob / boss banner / slots drawn),
  event option GAIN/RISK/COST chips, battle skip mode (`Instant`: whole log in one frame, logged HP, no popups) and animated
  steps settling deterministically in EditMode (fighters back home, faded KOs, no leftover popups).
- `PortalsEndlessTests`: portal/Dark Side windows and milestone markers (pure; node texts = all texts of a map node), fresh save, frontier past level 500 (first
  generated portal), level 1234 (window 1211–1260, paging back, jump to frontier, chest/bonus markers, tapping a locked node selects it with START disabled, chest in the `LevelDetails` modal),
  Dark Side past level 1000 (paging, one page ahead), hub frontier portals.
- `PlatformTests`: store catalogue = server catalogue (types, SKUs, obfuscated account id vector), fakes in the editor,
  purchase → grant → confirm, deferred purchase delivered later, cancelled purchase, rejected/pending receipts never
  confirmed + restore, server-verified ads (credit polling, timeout, direct-reward pending → refresh, No Ads, fake ads
  playing the dev callback), initial product set (= `docs/play-console-products-initial.csv`, others refused without a
  store call), purchases disabled (coming soon, no store init/call, offline dev receipts kept), ads disabled offline (instant, no ad, no entitlement) and online (server flag, `/v1/ads/instant`).
- `FirebaseAuthTests` (fake Firebase SDK + fake Play Games + fake server): Play Games auth code → credential sign-in,
  anonymous fallback (no/declined Play Games, `AllowAnonymous=false`), Firebase unavailable, anonymous → Play Games link
  keeping the uid, link to an existing Play Games player switches accounts, JWT `exp`; online: the Firebase ID token is the
  bearer (never dev auth), reused while valid, renewed a minute before expiry, force-refreshed once after a 401, sign-in
  failure = connection error; dev auth in the editor.
- `DevToolsCardTests`: the Developer card offline (always, with the unlock-all cheat) and online (only with the server's
  `devTools` flag, never the offline cheats); offline reset (confirm modal, cancel changes nothing, day-1 player on the hub,
  saved, dev status) and jump (day steps clamped to 1–60, profile tabs, account day and creation time of `AccountAgeJump`, hub,
  dev status "Day 4 · casual player · level n"); online jump/reset against a fake server (request body, 120 s timeout, state
  applied, session reloaded, dev status) and a 403 (error status, state unchanged).
- `LocalGameServiceTests`: new player, command success/failure atomicity, offline dev-receipt purchase, lossless
  save/load (incl. ulong RNG state), reset, timers settling with a manual clock, local arena.
- `RemoteGameServiceTests` (fake transport): dev auth + session mapping, command body (type/requestId/camelCase args,
  numeric enums), response mapping (events/payload/version/state), rule rejection, HTTP/network errors, server-only
  refusal, 401 re-auth, IAP verify, arena, invasion (occurrence/type + Observatory merge) and guild DTOs.
- `RouterTests`: root, push/pop visibility and lifecycle, rebuild on state change, reset, back closes modals, events become feedback (no toast layer).
- `ScreenSmokeTests`: every catalog screen for a fresh and a populated player, tab clicking, city building details,
  guild offline message, no placeholders left.
- `RunAndProgressionScreenTests`: a new player plays level 1 end to end through the Run screen (battle playbacks,
  result screen, tutorial steps), decision modals per phase, board layout and boss marker never overlap tiles (all main levels × 5 seeds), feedback bubble/banners/map, banner texts
  without internal ids, double rewards,
  battle playback ends at the logged HP, rage/shield bars equal the logged values after every entry, Signals reveal, merge-material finder, worm/hero actions.
- `PartsLabArenaScreenTests`: best-fodder picker (covers the need, no superfluous part, cheapest set), Mythic merge
  with copy + 2 LE auto-selected and executed, equipped parts never offered, locked parts only with a confirmation,
  manual selections checked by the rules, Replicator, five skills per rarity, awakening preview (name/stats/skills) and
  awakening, lab tree layout without overlaps (all branches, all 36 nodes, edges downwards), research from the node modal
  at `NextDataCost`/`NextDurationMs` with a busy slot, skill toggles + ANY/ALL prerequisites, Research Annex status and
  City banner, arena shop weekly core limit, resource bar chips/details, Biomass surge + Dark Side paging.
- `RunAndProgressionScreenTests` (auto mode): auto pauses at a shop with buyable skills (no command sent, tap-to-stop
  overlay removed, shop modal usable), the player buys a skill and leaves through the modal, then auto resumes by itself.
- `AutoRollOnlyTests` (owner playtest 0.1.200 + level unlocks): with auto on, event scenes, vendors, the chest event,
  the campfire and its Train picker, the level-up draft, the Mythic chest and choice, the signature chest and offer and the
  revive stay open / wait (no command sent, auto on and paused); an encounter waits for FIGHT! in manual play and starts on
  its own with auto on; a finished battle playback waits for
  Continue (no tap-to-stop overlay over it) and auto rolls again after it; no auto-pick on the run and home screens.
- `FeedbackTests`: rewards fly into the bar, star-ups pop, banners queue, unknown events stay silent, the lab node pops,
  the error bubble sits above the tapped button, `ToastCenter`/`EventText` are gone.
- `RunForkOutcomeTests`: branch landing (steps left, shop stop, path), fork picked on the board (A/B landing tap targets
  on the landing tiles, no modal, PICK A PATH, path B taken), zoomed board + "Roll 0/N" clock (subtitle, chip, banner),
  chance event result (chance meter "65% chance", SUCCESS!/NO LUCK, every outcome line, account-loot / max-HP risk text,
  icons, shown once), guarded chest result (fight, no chance meter), the result waits for Continue with auto on and auto
  rolls on after it, outcome line texts.
- `RunSeedSweepTests`: 100 fresh saves (seeds 1–100, with and without starter parts) play level 1 like the playthrough
  (manual roll, then `RunAutoStep`) to completion within a 200-decision budget, every auto decision accepted
  (failure names seed, last decision and run state); same seed + clock replay the same run.

`LocalGameService(dir, id, name, clock, randomSeed)`: with a `randomSeed` the new player's rules RNG is derived from
it only (no wall clock), so a seeded service on a `ManualClock` plays out identically every time.

PlayMode (`Assets/Tests/PlayMode`, needs a graphics device — run WITHOUT `-nographics`):
- `UiShowcaseTests` (**Explicit**: only with `-testFilter UiShowcaseTests`) renders the look at 1080×2340 — portal map, run board,
  event and draft popups, battle mid-animation and at the end, run result, worm, city — into `$ROGUESTARS_SHOTS_DIR` (default
  `client/RogueStars/Screenshots/showcase/`):
  `ROGUESTARS_SHOTS_DIR=/tmp/shots arch -arm64 $U -batchmode -projectPath $P -logFile /tmp/rs-show.log -runTests -testPlatform PlayMode -testFilter UiShowcaseTests -testResults /tmp/rs-show.xml`
- `RunDesignAShowcaseTests` (**Explicit**: `-testFilter RunDesignAShowcaseTests`) renders design A: the lava board with a
  skill tray, a roll mid-hop, the battle with skill fires mid-fight and at the end, the seabed board and a few rolls later on it
  into `$ROGUESTARS_SHOTS_DIR` (default `client/RogueStars/Screenshots/design-a/`).
- `ArtPassShowcaseTests` (**Explicit**: `-testFilter ArtPassShowcaseTests`) renders the layered board on the first level of
  the ember (lava), static (space station), void (shipwreck) and glacier (seabed, tile sprites) families after a few steps and a
  battle with sprites (alien companion, ember imp, lava brute, the Magma Tyrant with its family's sprite; the static family with the Storm Hive Queen) into
  `$ROGUESTARS_SHOTS_DIR` (default `client/RogueStars/Screenshots/art-pass/`).
- `RunMotionShowcaseTests` (**Explicit**) captures the run's motion (HUD, dice tumbling, mid-hop, landing pop, popups, two dice) into `$ROGUESTARS_SHOTS_DIR` (default `client/RogueStars/Screenshots/run-motion/`).
- `LevelUnlocksShowcaseTests` (**Explicit**: `-testFilter LevelUnlocksShowcaseTests`, needs graphics) renders the tutorial run's
  coach marks (`lu_tutorial_1_roll`, `_2_pick`, `_3_boss`), the Shops and Two Dice unlock screens panel by panel (`lu_unlock_*`,
  checking that the art animates) and the Laboratory's "?" replay (`lu_lab_help_button`, `lu_replay_lab_*`, nothing claimed) into
  `$ROGUESTARS_SHOTS_DIR` (default `client/RogueStars/Screenshots/unlocks/`).
- Skip flags: `RunFx.Instant` (dice land at once, hops jump, popups settle in place; everything in `RunFx` is also synchronous outside play mode), `RunScreen.InstantOutcomes`, `BattlePlaybackScreen.Instant`.
- `GearShowcaseTests` (**Explicit**: `-testFilter GearShowcaseTests`) renders the gear screens for a main-level-2 player
  (hub, Worm before/after equipping, slot → filter, part detail, Ore explainer, hub ▲ UP badge, Parts, resources, a
  48-part inventory) into `$ROGUESTARS_SHOTS_DIR` (default `client/RogueStars/Screenshots/gear/`); curated copies: `docs/client-screenshots/gear_*.png`.
- `PlaythroughTests.FreshSave_Playthrough_WithScreenshots` boots `Main.unity`, plays a fresh offline save (day-1 hub with
  PLAY only → first run without parts with a manual roll, then auto mode to the boss, making every decision auto pauses at
  through the decision modal (shop: buy a skill then leave, draft: first card, event: default option, branch: path A's
  landing tile on the board, defeat: give up, an encounter: FIGHT! on the dice, a GAME tile (none on level 1; for later runs): the mini-game's scripted player plays it for Silver; outcome results (and a guarded chest) are tapped through (auto only rolls), the first one is captured as
  `10_outcome`) → result → Parts unlock moment → its action opens the Worm → equip the starter parts → level one with Ore
  (part detail) → hub with the
  Worm tab → worm/parts → city build → dev unlock → hub with everything unlocked → 10× Signal with reveal → stamina refill → parts ladder, part detail with skills, Mythic merge picker,
  awakening preview → lab tree with a running research + a capstone node → arena medal shop → Dark Side → every catalog screen) and renders the canvas through a
  1080×1920 RenderTexture camera into `client/RogueStars/Screenshots/` (gitignored). A curated, downscaled set lives
  in `docs/client-screenshots/`. Deterministic: fixed seed (11) and game clock (2026-03-02 12:00 UTC), a fresh save
  directory per run. Its first manual roll lands on a battle; every playback waits for Continue (auto mode too), which
  the test presses. The auto run is bounded by game decisions (400 commands) and a stall guard (no
  state/screen change for 900 frames and 30 s) whose message carries the last result and the run/UI state.
```bash
arch -arm64 $U -batchmode -projectPath $P -logFile /tmp/rs-play.log -runTests -testPlatform PlayMode -testResults /tmp/rs-play.xml
```
(`WaitForEndOfFrame` never fires in batchmode, so the capture camera is rendered by hand.)

## Signatures pass
- **Signature pick** (`Screens/SignaturePick.cs`, run UI glitches #24): every run start (portal START RUN, the result
  screen's next level / again) sends `RunStart { PickOnBoard }`; the run opens in phase `Signature` and the run screen shows
  "CHOOSE YOUR SIGNATURE" over its own board (`RunScreen.SignatureBoardPick`, `Modal:Choose a signature`, a light dim so the
  board shows) before the first ROLL, in the slice card family (violet ribbon, tier tag "Signature", the synergy as a chip,
  the effect and "Awakens at 3 / 6 … skills"); the offer is `ActiveRun.StartSignatures` (= `RunSystem.SignatureOffer`) and the
  pick is sent as `RunPickSignature` (saved with the run: a resumed run reopens the pick). The skills book lists the signature first (`Signature:<id>`: awakening level, next threshold).
- **Battle statuses**: Heat (orange; red, outlined and pulsing at 8+ = 80% of the Supernova threshold,
  `BattlePlaybackScreen.HeatDangerAt`), Radiation (green), Bleed (red), Chill (ice blue) icons with stack counts under the HP
  bars; `StatusRemoved` updates them ("COOLED"); a **Supernova** plays a flash and an expanding fire ring with a white core
  on the exploding enemy ("SUPERNOVA!") and a NOVA / RECOIL damage number on every hit fighter, the worm included.
- **Laboratory**: a first "Packages" tab with one node per synergy package ("SYNERGY PACKAGE · Ice Package · Chill + 7 skills",
  gold frame once researched); the node modal lists the run skills it unlocks.
- **Boss gate marker**: the iso board shows the run's own boss on the boss gate (`IsoArt.BossMarker`: the battle's sprite through
  `ArtCatalog.EnemyOrFamily`, ~2.5× an enemy marker); the generic `Art/Slice/Markers/boss` is retired (not generated, never imported).
- Screenshots: `SignatureShowcaseTests` (explicit PlayMode).

## Playtest fixes 2 (owner playtest of 0.1.224, branch `playtest-fixes-2`)
- **Auto Roll hint**: the level-7 gate stays (`Features.AutoRoll`); before it a hold on the dice shows a tip bubble "Auto Roll unlocks
  at level 7" (`RunScreen.HoldDice` / `AutoRollHint`, level from content) instead of doing nothing; from level 7 a real
  hold (press reaches the cap — nothing covers it — 0.5 s, `HoldButton.Advance`) starts auto (`TutorialRunTests`).
- **Shop reroll in place**: the decision key ignores `ShopRerolls` in the shop (it closed and reopened the whole sheet —
  a flash and a pop-in); a reroll replaces only the offers (`Offers` layer, one `Offer:<i>` group per offer, pivoted at its
  middle): the old ones flip out (`UiTween.FlipOut`, 0.12 s, staggered) while the new ones flip in after them
  (`UiTween.FlipIn`); the reroll button's price / name / affordability update (`RefreshShop(rerolled)`).
- **One skill tile** (`Screens/SkillTileView.cs`): a thick frame in the tier colour (`RarityBorder`, ink edge, lighter lip),
  the face (`Face`, a mask) filled edge to edge by `Art/Skills/<skill id>` (`IconArt`, no glyph), else a code-drawn face
  (tier-tinted slate, shine, family initial / ★ `Glyph`). Used by the battle grid (`Slot:<id>`), every `SliceCards.Card`
  (draft, Mythic, signature pick and signature event, shop, Train; the tile is `Frame`), the skills book (a 4-column grid of
  tiles with name and level, `Skill:<id>`; the signature row leads with its tile) and skill lines of event / chest results.
  The run result lists no skills (owner rule of 0.1.200). **Tooltip** (`SkillTooltip`): a popover by the tile with the
  title at the level, "Tier · Family · Lv x/max", `SkillText.Full` and the upgrade line (an offered level-up "Level 1 → 2:
  …", else "Next level: …"); a tap anywhere closes it. Opened by a tap on a tile where a tap does not pick (battle grid,
  skills book, shop cards, result lines) and by every card's "?" (`SkillCardSpec.Tip`).
- **Ground contact** (`World/SpriteContact.cs`): standing board art is placed by its footprint centre — the lowest solid
  row (alpha > 128, ≥ 3 % of the solid width: padding, specks and glow ignored) plus half the base's iso depth
  (`DepthShare` × base width), horizontally the lower quarter's centre — instead of the front edge of the sprite's rect.
  Imported markers / set pieces / scatter get it as their sprite pivot at import (`ArtImportSettings`, version 3;
  `Tools/Art/Reimport Standing Art`), code-drawn sprites register theirs when painted (`IsoPaint.ToSprite`). The contact
  shadow (`WorldBoard.ContactShadow`: the soft shadow with a darker `Core`) is centred on the footprint, at least the base's width and squashed to the iso ratio,
  so the base sits inside it (`GroundContactTests`).

## Playtest fixes 3 (visual playtest of 0.1.224, branch `playtest-fixes-3`)
- **Numbers (G3)**: one formatter, `RogueStars.Rules.Core.NumFmt` (`NumMode` Whole / OneDecimal / Abbrev), used by the
  rules' texts (`SkillText`, `BattleConditions`, part skill descriptions) and the client's `Fmt` (`Num` = Abbrev, `Whole`,
  `OneDecimal`, `Pct`, `Odds`). Whole by default; one decimal only below 10 (rates, "+1.5%", "every 2.5 turns") and for
  abbreviated amounts below 10 ("9.5M"; "20K", "61K"); no trailing ".0". Call sites never write format strings.
- **Look**: body text in Fredoka Regular (`Ui.BodyFont`, system font only for missing glyphs, G6); disabled buttons are flat
  and desaturated without the shine, caption at 60% (`UiButtonLook`), `Ui.ButtonWithReason` adds a one-line reason (G4);
  rarity pills filled with `Theme.RarityColor` (G5).
- **Resource bar (G2)**: icon + number chips, at most 4 per screen (`ResourceBar.RelevantCurrencies`), zero balances hidden
  (stamina and Diamonds always), wallet button instead of "All", regen timer under its pill.
- **Pull reveal (SG1)**: `Screens/PullReveal.cs` (`PullReveal.Show`, `PullRevealPlayer`) for summons, Signals, the Shard
  Reactor and quest chests: charge → crack → deal → Legendary/Mythic held and flipped last → banner (`Ui/RevealBanner`) →
  ×1 / ×10 again; tap skips; the Skip Animation setting opens the end state.
- **Laboratory (L1)**: one research tree (`LabTreeLayout`: rows by dependency depth, lanes per branch, lines for every
  prerequisite), node states, pan both ways, node detail with requirement rows; `PlayResearchDone(node)` pops a node.
- **No toasts (G1)**: `ToastCenter`, `EventText`, the toast layer and strip insets are gone. `GameFeedback.KindOf(type)` maps
  every event (owner-approved map T01–T58): Animation (`FeedbackFx.FlyTo` into the bar chip / wallet, `StarPop`, `Flash`,
  `GlowSweep`, screen hooks `UiScreen.OnFeedback`: Worm slot flight T26, lab node T35, run floor card / empty cache / boss
  track shake T44/T47/T48), Banner (one at a time, 2.2 s) or Silent (unknown types too). `Session.Failed` → error bubble
  above `Ui.LastPressed` (shake, 2 s); "Not connected" → persistent Reconnecting strip; `Feedback.Tip` for the run level
  (T50), card text and the Auto Roll hint; `Session.PendingPayments` / `IsAdPending` drive PENDING and spinner states
  (T51/T52); dev tool results stay in the Developer card. The alien level refund migration and its event were removed
  (pre-release, no migrations).
- **Removed**: "Equip best parts" (button, level-up callout, auto-pick), the draft cards' SYNERGY tag (display-only),
  the "LEVEL n → n+1" chip.

## Run shop fixes (owner playtest, branch `run-shop-fixes`)
- **Stat perks show at once**: the open shop follows every state change in place — `ShowDecision` calls `RefreshShop` when the
  shop sheet stays open, so the shop's run HUD (HP / ATK / DEF, level, coins), the buy buttons and the reroll price track the
  state, not only the buy button's callback (a purchase or an online refresh shows without leaving the shop).
- **Reroll price per visit**: `RunSystem.ShopRerollCost` (base + step × rerolls in this visit) is what the rules charge and what
  the Reroll button shows; `ActiveRun.ShopRerolls` resets when a new shop opens, so every shop starts at the base price.
- Tests: `RunShopFixTests` (EditMode), `ShopVisitTests` (rules); screenshots: `ShopFixShowcaseTests` (explicit PlayMode).

## Skill help (owner playtest, branch `skill-help`)
- **The "?" panel explains the mechanics** (`SkillTooltip`, used by every card's "?", the skills book, the battle grid, shop and
  result tiles): the skill's own text at its level and the next level (or the offered level-up), then one short block per
  mechanic the skill touches (`TipMechanic:<id>`: Heat / Supernova, Radiation, Bleed, Chill / Freeze — any Freeze resets Chill,
  Shock, Arc, Stun, Charm, Exposed, Stagger, Delay, Combo, Counter, Crit, Rage, Shield, Dodge, life steal, regeneration,
  aliens, Echo, signature, boss awakening, run XP, coins) and the synergy info (`TipSynergy`: more offers per owned skill,
  awakening at 3 / 6, family / source gates, Mythic origin; no synergy tag chip). Tall panels are centred and clamped.
- **One source of truth** in the rules: `RogueStars.Rules.Core.SkillGlossary` — `Entries(content)` builds every definition
  from content numbers (status caps, damage per stack, durations, cooldowns, rage, crit, chain decay, awakening thresholds)
  through `NumFmt`; `Keywords(content, skill)` maps a skill's family, stats, triggers, effects, statuses, gate conditions,
  rule changes and offer gate to entries (never per-skill text); `SynergyLines` the family block. Tests
  (`SkillGlossaryTests`): every run-pool skill resolves every mechanic its own text names, every status it applies and
  (signatures) Signature; glossary texts carry no raw ids and follow the number rule; numbers follow content.
- **Skills book on skill selections** (`RunScreen.OpenSkills`, `RunScenes.SkillsBook`, named `SkillsBook`; run UI glitches
  #9, owner: only screens that pick or improve a skill): the board's footer, the level-up draft and Mythic choice, the
  signature choice (signal event), the shop and the Train picker. Not on chests, scene events, the rest, vendors, quests,
  the outcome sheet, revive / burrow, the run-start signature pick (no skills yet) or inside a mini-game. It opens
  `Modal:Skills` on top; closing it returns to the same decision — nothing is sent, rerolled or rebuilt.
- Tests: `SkillHelpTests` (EditMode); screenshots: `SkillHelpShowcaseTests` (explicit PlayMode).

## Playtest decisions (owner, branch `playtest-decisions`)
- **Q2 — battles without a run** (hero / mount kit fights, the last battle of a finished run): the top strip shows only the
  worm's account max HP, ATK and DEF — no level badge, no EXP hexagon, no XP bar (`BattleHudContext.InRun` false →
  `SliceHud.Show(…, inRun: false)`; `RunScreen.BattleContext` sets it only while `Run.Active` exists). Inside a run nothing
  changes. Test: `RunDesignATests.Battle_OutsideARun_HudShowsTheWormsRealStats_NeverADash` (BT3).
- **Q4 — reveal only for big loot**: run results (`RunResultScreen.Reveal`, once per screen) and mail claims
  (`MailScreen.Claim` / `ClaimAll`, one reveal for all) open the shared pull reveal (`PullReveal.ShowIfEarned`) only when the
  rewards hold a Legendary or Mythic item (`PullReveal.Earns`; currencies never count). Otherwise the reward strip / flyout
  stays as before. Tests in `PullRevealTests`.
- **Q7 — unlock moments are dismissed for good**: SKIP or Android back (`UiScreen.HandleSystemBack`) on an unlock moment
  claims the celebration (counts as seen, gifts granted) and returns without opening the feature; the snooze
  (`App.SnoozedUnlocks`, `SnoozedAtRun`, the hub's "NEW: … tap to open" card) is gone. In a "?" replay SKIP still jumps to
  the last panel and back closes it. Tests in `HubOnboardingTests`.
- **Mythic chest art**: `Art/Run/Props/chest_mythic` (owner pick `run-props/chest_mythic: I_s6601.png` in
  `tools/art/slice-picks.json`, cut out with `tools/art/export_assets.py`) — the Mythic run chest no longer falls back to the
  board's wooden chest marker.
- **Small fixes**: the vendor's Skills button moved off the counter's crate; Last Light reads "Below 30% HP: every hit
  crits. You can't heal." (rules test: no skill line ends on a comma / colon / semicolon).

## City screen (branch `city-client`, docs/design/city.md)
- **Diorama** (`World/CityWorld`, like `WorldBoard`): layer 29, own camera → RenderTexture under the uGUI HUD. Map cell (x, y) sits at
  `Iso.Cell(H−1−y, W−1−x)`. The land (two-tone checker, rubble on undug patches with diggable ones lighter, stone plaza, springs,
  cliff walls under open front edges, streets as a 16-variant autotile from the 4-neighbour mask `StreetTiles.Mask`, contact
  shadows) is ONE baked texture (`World/CityArt.cs`: `CityTerrain`, 64 px/unit), re-baked only when the layout changes; the grid
  overlay is a second, half-resolution texture (15% / 40% in edit mode). Buildings use approved set pieces / markers / props as
  stand-ins (`CityArt.BuildingArtKey`), fire + saucer on the plaza, crystal / ruin features. Budget ≤ 180 active sprites
  (`CityWorld.ActiveSprites`, tested on a full city at night); walkers and FX pooled; one updater (`CityWorldDriver`); the root
  goes inactive while the screen is hidden. Zoom stops: overview 12.5 / edit 9 units wide; pinch settles on the closer one,
  double tap centres, pan clamped to the land.
- **Life** (`World/CityLife.cs` `WalkerSim`, pure): one walker per working alien, building door → fireplace on the cached BFS
  route (`App/CityEditor.cs` `CityRoutes`, per layout rev) with its goods icon, pops them at the fire (the bar pill ticks), walks
  back; idle aliens wander the plaza (sit by the fire at night); social events (heart, high five, scuffle, gossip, dance: half a
  tile, 1 in 3, same pair 60 s, one every 6 s, 1.5 s stop); level-up party (confetti, nearby walkers hop), scaffold + sparks while
  building, poof + bounce when built, drill bot drives to a dig and puffs dust, rubble crumbles tile by tile from the land's edge;
  fixed 20-minute day / night (`CityDayNight`: 12 / 2 / 5 / 1 min, wall clock) with lamps, lit windows of staffed buildings, a
  brighter fire; a bred child walks out of the pen with half an eggshell for one lap.
- **Edit mode** (`Screens/CityScreen.Edit.cs`): every edit is one command applied at once, Done just leaves; Undo = reverse command
  (`CityUndo`, 50 steps, edit mode only; undoing a new build sends CityStore = cancel + refund). The ghost (`CityGhost`, the rules'
  `CityLayout.Preview`) floats one tile above the finger, green / red tiles, a chip with the reason or the bonus ("+20% output ·
  Open Ground"); an invalid drop waits (✓ when fixed, ✕ / drop on the rail cancels), ⟳ rotates. Streets: a stroke grows out of
  the network (`StreetStroke`: skipped tiles filled, backing up takes the last tile back) and is ONE CityStreets; erasing what
  would cut something off is refused and flashes it red. Rail: Streets, stored copies, new copies (cost, n/cap; at the cap
  dimmed "3/3 placed"); tap = ghost on `CityPlanner.Find`'s spot, drag = ghost under the finger.
- **Feedback** (G1): T05 / T08 silent, T06 scaffold drops + poof, T07 level badge pops + party, T09 the building shrinks and flies to
  the Build button / rail, T10 `city_dug` = "NEW LAND!" banner + crumble, T18 the assigned alien walks from the fire into its
  building; collected goods fly from the building; a pop's tokens fly after the reveal closes.
- **Pop**: `CityScreen.ShowPopReveal` (shared `PullReveal`): one card per chance, ×1 Rare / ×2 Epic sparks / ×3 Legendary held +
  crack + "×3!" banner (`RevealOptions.Banner`).
- **Hub (C4)**: `Screens/HubSquad` — the Launch Pod squad walks the planet's top arc around the worm with the same `WalkerSim`.
- Tests: `CityEditorTests` (EditMode); screenshots `CityShowcaseTests` (explicit PlayMode, `-testFilter CityShowcaseTests`).

## Camp mode and the crash site on the hub (branch `camp-mode`, docs/design/city.md "The crash site comes first")
- **One entry** (`HubLayout.Tabs`): from the Launch Pod unlock (level 6) the tab "Camp" with the crashed-saucer icon (`HubLayout.CampIcon`,
  `CityArt.Saucer`); from the City unlock (level 15) the same tab id `city` reads "City" with the `tab_city` art. Both open `CityScreen`
  (`ScreenCatalog` gates it by the Launch Pod feature).
- **Camp mode** (`CityScreen.CampMode` = City feature not unlocked, `CityScreen.Camp.cs`): the same diorama, no dock (a note "The camp
  grows into a city at level 15" instead), no build rail, no dig pop-up (`OpenDig`, rubble taps ignored), no edit mode (`EnterEdit`,
  long press ignored), the header says "Camp" without rates. Tap the pod = its card with only the seats (`CampSeats`: tap a squad
  alien to free its seat, tap another to seat it); no level, Level up, Star up, Move or Store. The rules refuse those commands anyway.
- **Hub props** (`Screens/HubScreen.CrashSite.cs`): the crashed saucer, the fireplace and the pod on the planet's top arc with the squad
  walking around (C4 `HubSquad`); only decoration and one shortcut button (`PodButton`, `HubScreen.OpenCity`) to the City screen.
  Shown while the pod is built. No planet pod card, no seat badge any more.
- The Aliens unlock moment is one group "Aliens + Launch Pod" (2 + 3 panels; MEET YOUR ALIEN → Aliens screen); an old save that
  gets its pod on load sees the Launch Pod's own moment (MEET YOUR SQUAD → Aliens screen). The City moment frames the camp
  growing into a City.
- Tests: `CrashSiteHubTests` (EditMode: no crash site before the Aliens; props, squad, Camp tab and icon after; tab and props open the City
  in camp mode; camp mode shows no dock actions, dig or edit; the pod card has only seats and seating works; at level 15 the same
  entry shows the City icon and everything is on); screenshots `CrashSiteShowcaseTests` (explicit PlayMode, `-testFilter CrashSiteShowcaseTests`).

## Art import 2026-11-25 (owner-approved list `~/AI/out/art-state.json`, branch `art-import`)
- **Importer** (`ArtImportRules.PlanSlice`, `tools/art/slice-picks.json`): a pick's category may carry a package generation
  suffix (`slice-hud2/exp`, `slice-markers4/elite`, `run-icons2/…`, `run-result2/…`: `BaseCategory` strips the digits, the
  destination is the base category's folder); a value starting with `~/` or `/` is a file anywhere (the full-frame
  originals under `~/AI/out`), a value with `../` reaches another package (`hero/hero_worm`: `../hub-icons/tab_worm/S_s5001.png`).
  New categories: `hub-icons` → `Art/Hub/Icons` (trimmed), `hub-scene` → `Art/Hub/Scene` (full frame), `run-result` →
  `Art/Result`, `board-ground` → `Art/Slice/Ground`, `hero` → `Art/Hero` (kept square). A full import also re-copies
  older picks whose source differs from the curated files in git (set pieces, run props imported from a staging root):
  revert those (`git checkout -- Assets/Art/Resources/Art/Slice/SetPieces Assets/Art/Resources/Art/Run/Props`).
- **Worm**: the body `Art/Hero/hero_worm` = `hub-icons/tab_worm/S_s5001` (legless, walking left like the old one; board
  token, battle, home planet, rest scene, Worm screen). It now imports as **standing art** (`ArtImportSettings.IsStanding`,
  version 4): its pivot is the ground contact, so the board token stands on its tile. The icon worm `Art/Hub/Icons/tab_worm`
  = `C_s5002`: the Worm tab and the top bar's avatar (`ArtCatalog.WormIcon()`).
- **Home**: icons `tab_worm`, `shop` (side_shop), `pass` (side_pass), `event` (side_event; live events), `patrol`
  (quick_harvest: Farm Patrol), `chapter_rewards` (quick_chest), `profile_level` (the top bar's level badge: a star behind
  the number), `play_button` (a rocket launching off START's left edge, `PlayRocket`; nothing drawn without the art).
  Backdrops `Art/Hub/Scene/sky_day|night|event|invasion` from `~/AI/out/pkg-hub-scene2` (day/night s5211; event/invasion
  s5212 — their s5211 have a white frame round the picture). `HubScreen.SkyState`: an invasion running → invasion, another
  live event → event, else night 19–6 h local time, else day (`HubScreen.SkyOverride` for tests / screenshots). The sky
  covers the hub at its own aspect (`AspectRatioFitter` envelope, clipped by a `RectMask2D`).
- **HUD** `Art/Slice/Hud/`: skull, heart, coin, book (slice-hud L) and exp, dice, pedestal, boss_rail_top, cog (slice-hud2 L).
  `pedestal` is imported but the dice stand keeps its code-drawn pedestal: the red key cap is drawn by the same camera and
  sits exactly on it (`DiceStand`); the round drum art does not fit the rectangular cap.
- **Reward icons** `Art/Run/Icons/`: blessing, coins_big, coins_small, heal, reroll, sale_tag, skill_book, upgrade (run-icons2 N).
  **Max HP** has no icon: `ArtCatalog.RunIcon("max_hp")` falls back to the HUD heart. **Curse** is not imported yet (its
  arrow is being removed).
- **Result** `Art/Result/` crown, crest_broken, banner, banner_defeat (run-result2 N_s6511, the lowest approved seed).
- **Elite marker** `Art/Slice/Markers/elite` = slice-markers4 I_s5132 (the enemy marker waits for its straightened redo).
- **Ground chunks** `Art/Slice/Ground/<id>` and `<id>_2` (ground_corner, ground_large, ground_long, ground_small; seeds
  5901 / 5902), `ArtCatalog.SliceGround` / `SliceGroundIds`: imported for board decoration, not drawn yet (the island under
  the board is a generated mask, no obvious slot for iso chunks).
- **Battle background (shipped)**: `Art/Slice/Battle/battle_meadow` = `~/AI/out/pkg-slice-battle2/battle_meadow__V__flux__s5711.png`
  (owner-approved, side-view meadow, full frame 1024x1344, `slice-picks.json` `slice-battle/battle_meadow`). `ArtCatalog.BattleBackground(biome)`
  returns `battle_<biome>` when imported, else the shared default (every biome for now; no art: the drawn `HudArt.Arena`).
  `SliceKit.Backdrop` covers a box at the art's own aspect, never stretched, with a zoom and a shift-up; the battle screen uses
  zoom 1.3 / `BattlePlaybackScreen.BackdropShift` 200 so the fighters stand on the grass and the glowing crack at the art's foot is
  cropped away, plus a dark gradient behind the top HUD. The run-result backdrop and the rest / event stage use the same art.
- **Captures**: `ArtImportShowcaseTests` (**Explicit**, `-testFilter ArtImportShowcaseTests`): `ai_hub_day|night|event|invasion`,
  `ai_worm`, `ai_board`, `ai_battle_hud`, `ai_battlebg_run|kit_<W>x<H>` (`$ROGUESTARS_SHOT_SIZE`, 1080x2400 / 1080x1920), `ai_result_victory|defeat` into `$ROGUESTARS_SHOTS_DIR` (default
  `client/RogueStars/Screenshots/art-import/`). Tests: `ArtImportTests` (EditMode), `GroundContactTests` (the hero's pivot).

## Animation (branch `anim-integrate`; owner decisions 2026-11-26)

**DEFERRED COMPLETELY (owner, 2026-11-26): `AnimSettings.Enabled = false`.** Everything below stays in the code but is off for everyone: `AnimSettings.Reduced` is always true (creatures and props draw plain static sprites, nothing ticks, no prop shaders / particles), hub creature taps are inert (`HubCreatureTap.Tap` returns false, the target does not raycast) and the Settings "Animation" card is not built. Tests switch `Enabled` on in SetUp to keep testing the machinery (`CreatureAnimTests`, `CrashSiteHubTests`) and assert that nothing animates with it off.

Two techniques shipped, both code and shaders (no video, no cut-out rigs, no 2D Animation package): **a code spine** for every
upright creature and **shaders** for props and buildings. Namespace `RogueStars.Client.World.Anim` (`Scripts/World/Anim/`), shaders
`Assets/Art/Shaders/Anim/`, base materials `Art/Resources/Art/Anim/Materials/` (written by `Tools/Art/Anim Materials` =
`Editor/AnimAssets.Create`).

- **Creatures** (the worm, ALL aliens, ALL enemies incl. elites and bosses, sidekick drones, city / hub walkers):
  `SpineRig` turns a sprite into a 6×17 = **102-vertex** grid over its alpha bounds, bent along a vertical spine rooted at the feet
  (rows sit on the spine and turn with it: lean, travelling wave, head curl, stretch, squash; no seams, no extra art).
  `CreatureMotion` (plain C#, pure function of the clip clock, no allocation) plays the clips **idle** (breathing sway), **move**
  (slither + hop squash-stretch; flyers hover), **attack** (rear up + lunge toward the target; styles lunge / slam / swoop),
  **hit** (recoil + flash), **death** (collapse, held), **celebrate** (wiggle + hops); one-shots return to idle with a 0.12 s blend.
  `CreatureSpecs`: defaults from the alpha bounds' aspect (tall = bendy, wide = squashy slam) and a small override table by sprite name
  (`spark_drone`, `alien_glimmerfly`, `elite_rift_echo`, `elite_storm_caller` hover and swoop; `glacier_golem`, `elite_lava_brute`,
  `alien_voidmaw`, `alien_gravitoad` slam; amplitude / speed / bend / squash per species).
  Alpha bounds come from `Resources/Art/anim_bounds.json` (imported art is not CPU-readable; `Tools/Art/Build Anim Bounds` /
  `AnimBoundsBuilder.Build` rewrites it, and it re-runs when art in Hero / Enemies / Aliens / Sidekicks / Run/Characters is imported;
  `CreatureAnimTests` fails when a creature sprite is missing); readable textures (code-drawn sprites) are measured, anything else gets
  its whole rect (still animates, just a taller spine).
  Hosts: `UiCreature` (a `MaskableGraphic` child of the sprite's `Image`: same rect, so click areas, parent-scale flips (the worm's
  mirror toward its target) and stage layout keep working; the Image stays but does not draw, its colour drives the mesh, so hit
  flashes and the death tint work) and `WorldCreature` (a pooled mesh under the sprite's `SpriteRenderer`, which stays disabled but
  supplies sprite, sort order, colour, flipX; the board token, boss marker, City walkers).
  **Wired**: battle playback (`BattlePlaybackScreen`: every fighter; clips mapped by `CreatureEvents`: attack-type entries play
  Attack on the actor aimed at its target and Hit on the target (Dodge: a sidestep), a sidekick turn / guard / ability hops, a
  Death entry collapses (also in skip mode), the victory banner makes the surviving player side celebrate; playback speed scales
  the clips), the hub (worm + crash-site squad: Move while walking), the Worm screen portrait, the Aliens screen cards (idle only,
  20 Hz), the Rest scene (worm + sidekicks), summon / signal reveal art (whole art, not outfits), Camp / City worker chips, the board
  token (slithers while hopping: its hop arc stays `RunScreen`'s) and boss marker, City walkers (Move / Idle / Celebrate on a cheer).
- **Props and buildings** (`PropFx`): five reusable shaders - `FlameFlicker` (masked noise displacement + flicker; `_AutoMask` finds
  flames by warm bright colour, else a mask texture), `Sway` (height-weighted shear), `Pulse` (breathing glow on the bright, saturated
  pixels: colour mask; `_Whole` mixes in the whole sprite), `Shimmer` (a highlight band sweeping across), `Swirl` (the portal vortex
  inside a disc, a shader instead of the video) - plus `GlowPulse` (additive disc), `Ember` and `Puff` particles (`AnimProp.Embers`,
  `AnimProp.Smoke`). Tables: `PropFx.Props` by sprite name (campfire flame + embers + glow, alien tree / mushrooms sway, crystals and
  shrine pulse, saucer pulse + smoke, wreck smoke, portal swirl, event / mystery / minigame markers glow) and `PropFx.Buildings` by blueprint
  id for the City stand-ins (farms and domes sway, labs / reactor / beacons / spires / mines pulse, refinery / foundry / ore mine / launch pod smoke,
  foundry fire by colour, stamina well swirl); `CreatureAnimTests` checks that every `CityArt.BuildingArtKey` id has a look. `PropFx.Apply`
  gives the renderer its own material instance (own `_Phase`: batching ignores the object matrix) and the extras; applied on the run board
  (set pieces, scatter, markers), the City (buildings once built, plaza fire, saucer, features) and the UI scene props (`RunScenes`: shader
  only). GPU only: no script per frame; particles pause when culled.
- **Animation level** (Settings "Animation: Auto (Full) / Full / Reduced", one button cycling Auto -> Full -> Reduced; per device, PlayerPrefs
  `rs.animLevel`, default Auto; the old boolean `rs.reducedAnim` is ignored, pre-release): `AnimSettings.Level`; `AnimSettings.Reduced` is what it
  resolves to now. Auto = Reduced on a weak device (`AnimSettings.IsWeak(DeviceSpecs)`: `SystemInfo.systemMemorySize` < 3500 MB (a "4 GB" phone
  reports ~3.6 GB), fewer than 4 processors, or a GPU memory under 1000 MB when reported), else Full; unknown specs = Full. Reduced: every host
  draws its plain sprite / material and updates nothing; a change takes effect at once (`AnimSettings.Changed`). Hosts also stop while hidden
  (inactive hierarchy, culled by a mask, off screen via `Renderer.isVisible`).
- **Hub taps** (`HubCreatureTap`): tapping the hub worm (`HubScreen.WormTap`) or a wandering squad alien (`HubSquad.Taps`) plays its Celebrate clip
  with three heart bubbles + a sparkle burst; 1.5 s cooldown per creature; no sound. The target is a transparent button over the creature's
  middle (60-70 % of its width), clear of the crash-site pod / area buttons and the hub's buttons (test).
- **Budget**: 102 vertices and one draw call per creature, pooled world meshes, no per-frame allocation in `SpineRig.Deform` /
  `CreatureMotion.Tick` (test), idle-only cards throttled to 20 Hz, the beaten stop ticking once collapsed. Measured in the editor (Mono,
  Apple Silicon): a 7-creature battle = 714 vertices, deform + mesh update ~0.06 ms per creature per tick (0.46 ms for 7 x 8 ticks);
  the City with 21 building looks, 5-13 walkers: 35 sprites (budget 180). Not yet measured on a low-end phone.
- **Art review**: `AnimReviewCaptureTests` (**Explicit**, PlayMode, needs graphics) renders every animated prop / set piece / building
  stand-in as a transparent frame sequence; `tools/art/anim_review_pack.py <frames> ~/AI/out/anim-review` cross-fades a seamless 2 s
  loop and writes ~220 px WebPs; `tools/art/review_page.py` lists them from a collection's `"animated": [globs]` (and
  `"animated_approved"`), one figure with its own id each.
- **Captures**: `AnimShowcaseTests` (**Explicit**, `-testFilter AnimShowcaseTests`): hub, Worm, Aliens, board (+ hop), a battle as a frame
  sequence (`battle_frames/`), City (+ reduced) and `anim_stats.txt` into `$ROGUESTARS_SHOTS_DIR` (default `Screenshots/anim-integrate/`).
  Tests: `CreatureAnimTests` (EditMode: rig from alpha bounds, vertex budget, rest pose, bend directions, no allocation, clips, attack
  styles, flyers hover, species defaults, event mapping, battle wiring, reduced fallback, hosts, board token, prop tables and instances, Settings toggle).

## Known gaps / TODO
- Play Games sign-in needs the console setup of docs/setup.md step 3 (fingerprints, Play Games Services credentials, web
  client id); until then release builds sign in anonymously. Push notifications are not integrated.
- Ads are disabled (no AppLovin account yet); switching them on is docs/setup.md step 5.
- Mediation network adapters are not installed (AppLovin's own demand only); add them with the AppLovin Integration Manager.

## Playtest 0.1.308 (client)
- **One speed setting** (`PlaySpeed.Current`, PlayerPrefs `rs.play_speed`): the board's speed button (×0.5 / ×1 / ×2 / ×3 with the
  monthly pass) sets the speed of board movement and battle playback; the battle has no speed button of its own.
- **A won run battle returns to the board by itself** after a short VICTORY beat (`BattlePlaybackScreen.WinBeatSeconds` / speed);
  a lost one (and fights outside a run) keep the Continue button.
- **Red tiles cost the player**: buff tiles are green, curses and traps red, the boss's harmful tiles dark red; a boss tile
  that only helps (`IsoArt.FriendlyBossTile`) is drawn as a boost (`IsoArt.FaceOf(TileDef)`, `Theme.TileAccent(TileDef)`).
- **Battle "NEXT" strip** sits on a `Kit.Plate` (near-opaque, light rim, drop shadow) with an outlined label (`Kit.Legible`).
- Screenshots: `Playtest308ShowcaseTests` (PlayMode, Explicit, `ROGUESTARS_SHOTS_DIR`).

## Playtest 0.1.325 (client)
- **Signature pick on every run**, the first one included (`SignaturePick.StartRun`: the offer always has 3 cards now).
- **Shop**: 3 skill cards plus the utility tiles (a stat perk, and the heal while the worm is below 60% HP); a reroll flips
  only the skill cards' offers (the rules keep the support offers).
- **Forks** whose ways land on one tile are no choice (rules: `RunSystem.SameLanding`): the board just moves.
- **Battle**: no `RoleGlow` ellipse under enemies drawn from a family sprite (it read as a red shadow floating below them).
- **Run HUD**: the level / EXP pill (`SliceHud.LevelPillX/W`) ends clear of the HP heart (`HeartX`), the level text
  best-fits inside its pill instead of spilling under the heart.
- Screenshots: `Playtest325ShowcaseTests` (PlayMode, Explicit, `ROGUESTARS_SHOTS_DIR`); tests: `Playtest325Tests` (EditMode).

## Board path fix (client)
- **Arrows point forward**: boost and curse chevrons point from their tile to the next tile in walking order
  (`WorldBoard.PathHeading`: next cell − this cell; a corner points the new way, a fork down its first arm, the path's end
  the way it is entered), never by the tile's entering heading (`RunTile.Dir`) and never backwards for curses.
- **One walkway**: the path is drawn at one elevation (raised sections flat), wall faces only on exposed front edges
  (`IsoArt.ExposedWalls`).
- Tests: `BoardPathTests` (EditMode: arrow per step incl. all eight turns, walls per neighbour mask, pits). Screenshots:
  `BoardPathShowcaseTests` (PlayMode, Explicit, `ROGUESTARS_SHOTS_DIR`): a meander with every turn and chevrons on every
  corner, plus seeded boards of levels 2 / 6 / 11 / 17 at three places along the main path.

## Run Quests (branch `feature/run-quests`, docs/design/run-quests/recipe.md)
- **Board:** the visitor tile (`TileCategory.Quest`, label VISITOR) carries a star-lantern marker (`IsoArt.Lantern`, gold pulse);
  it forces a stop. Landing pops "A VISITOR!".
- **Encounter card** (`RunScreen.Quests.cs`, phase `Quest`): the stage with the worm and the character's portrait
  (`ScreenArt.Character("quest_<id>")`, `Art/Run/Characters/quest_<id>`; Mothwick has a drawn placeholder), a name plate
  with "the Lost Courier · Chapter 7" (never the total), the title band, the scene text and one wide button per option
  (`QuestOption:<i>`) with a detail line and a chip (Story / Hard battle / −100 coins / A random Mythic skill / +1 coin per
  gain, forever / Story waits). A price the run cannot pay is disabled with the reason. Legendary / Mythic sacrifices take a
  random skill of the tier (no picker; the result sheet's "Lost skill: …" names it). A lost battle is the normal defeat
  (revive modal). "Story so far" opens the last journal lines. Epilogue scenes have one option and a reward chip.
  Texts: `QuestView` (pure, tested).
- **Coin bonus:** outcome lines `CoinBonus` ("+3 Starglint" popup, "+3 coins from Starglint" on result sheets), `QuestBoon`
  and `QuestStep` on the STORY result sheet.
- **Journal** (`JournalScreen`, catalog id `journal`, under "More" once the player met a character, `ScreenEntry.Visible`; no feature gate, no intro): the account
  bonus line, per character the portrait, "Reached chapter N" (no total, no bar, no chapters left), what the story waits for
  next and the story so far.
- **Tests:** EditMode `RunQuestUiTests`; screenshots: `ROGUESTARS_SHOTS_DIR=/tmp/quests arch -arm64 $U -batchmode -projectPath $P
  -logFile /tmp/rs-quests.log -runTests -testPlatform PlayMode -testFilter RunQuestShowcaseTests -testResults /tmp/rs-quests.xml`.
