# Rogue Stars Unity client

Unity 6.6 (6000.6.10f1) project in `client/RogueStars`, Android (IL2CPP, ARM64, portrait). All UI is uGUI
built from code at runtime from one bootstrap scene; no art assets (rounded shapes are generated at runtime, one
font asset). The shared rules (`shared/RogueStars.Rules`) are
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
| `Assets/Scripts/App/` | `RogueStars.Client` | `ClientApp` (composition root), `GameSession`, `UiScreen` + `ScreenRouter`, `ToastCenter`, `ResourceBar`, `ScreenCatalog`, `AppBootstrap` |
| `Assets/Scripts/Ui/` | `RogueStars.Client` | `Ui` toolkit (namespace `RogueStars.Client.UiKit`), `Theme`, `UiShapes` (runtime rounded sprites), `UiMotion` (`UiTweens`, `UiBob`, `UiPulse`), `UiTween`, `UiSequence`, `SafeAreaFitter` |
| `Assets/Resources/Fonts/` | – | `Fredoka-Bold.ttf` (display font: static wght 700 instance of Fredoka[wdth,wght] from the google/fonts repo, made with fontTools `varLib.instancer`) + `Fredoka-OFL.txt` (SIL Open Font License 1.1) |
| `Assets/Scripts/Screens/` | `RogueStars.Client` | One class per screen |
| `Assets/Scripts/Minigames/` | `RogueStars.Client` | Run mini-games: `Logic/` (one Unity-free class per game + `MinigameLogic` base + `MinigameCatalog`), `MinigameRenderer` (shapes → pooled uGUI nodes), `MinigameHost` (the full-screen flow) |
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
     └─ ResourceBar ── profile chip (worm level, name, power → profile: Worm, Settings) + up to 5 currency chips the player has a use for (HubLayout.VisibleCurrencies = the rules' reward-use mapping, RewardGate.CurrencyHasUse; a new account shows stamina and Diamonds) with regen timers (live); "All" opens every balance
```
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
  `ScrollToAfterBuild(rect)` centres a node/tile after a rebuild (otherwise the scroll position is kept).
- **Screens**: subclass `UiScreen`, implement `Title` and `Build(body)`; the body is rebuilt on every state change
  (`AutoRebuild`, off for screens with text inputs) and countdowns use `Live(text, () => …)` (ticked twice a second).
  `TimerControls(parent, timerId)` renders any rules timer with Diamonds / ad −15% / free speed-ups.

## Screens
| Screen | Feature gate | Notes |
|---|---|---|
| Hub (Planet) | – | onboarding v2 (GDD §16, design canvas Main/HubDay1), what shows comes from `App/HubLayout.cs`: left column = daily goals + live events (from the Invasions unlock, ≤ 3 icons + Observatory), planet + frontier portal chip in the centre, right column = Shop, Offer (timed offer on sale), Pass, Mail, More (every other unlocked screen); the tutorial goal card when it needs another screen; the **next-unlock teaser** (`Features.Next`: "Laboratory — clear level 14", "unlocks tomorrow", level progress or the unlock day); fixed footer: PLAY card (portal · level, stamina cost; Continue during a run) and the bottom tabs (Worm, City, Planet, Signals, Arena) that appear as their features unlock (NEW badge on the unlock day). Locked features are never shown. Pending celebrations open the **unlock moment** |
| Unlock moment | – | `UnlockScreen` (design canvas Unlock): full screen, "DAY n · NEW ON YOUR PLANET", the unlock group's name, its `FeatureDef.Description`, reward chips (`UnlockRewards` + `UnlockGrants` + the reward stash it released, `RewardGate.ReleasedAt`; `Fmt.RewardChips` sums currencies, counts items: "3 Parts"), ONE guided action (`HubLayout.UnlockAction`: claims `FeatureCelebrate` for every member, then replaces itself with the feature's screen) and the next teaser. Opened by the hub (`HubScreen.ShowPendingUnlock`, from `Frame` while the hub is on top, never mid-run); backing out snoozes it for the session and the hub shows "NEW: … — tap to open" |
| Portals | – (modes: `overcharged`, `burrow`, `dark_side`) | **level map**: header = the selected level's portal name + "Portal n · x of 10 cleared · y of 30 stars" + a stamina chip; Main / Overcharged / Burrow / Dark Side tabs; a zig-zag path of level nodes climbing upwards (`PortalsScreen.MapX`), connector dots between them, the newest portal at the top with a portal banner; node states (`NodeStateOf`): **next** = big glowing orange node ("NEXT"), **cleared** = stars ★★☆, **open**, **locked** = dimmed and not tappable; the last level of each main portal is a **boss node** ("BOSS · n" + boss name); **CHEST** (every 10th level) / **BONUS** (every 50th, `CampaignDef.MilestoneEveryLevels`/`PortalBonusEveryLevels`) chips beside nodes, ✓ once claimed. Tapping a node selects it for the fixed **bottom level card** (`SelectedLevel`, default = the next level of the tab): "Level n · name", threat, the three star goals (★ achieved / ○ open), `i` (`LevelDetails`: the level modal with threat, boss, dice/rolls, modifiers, feats, completion rewards, milestone chest / portal bonus, entry cost) and **START RUN** with the stamina cost (`StartRun`; LOCKED for locked levels, CONTINUE RUN while a run is active); the map scrolls to the selected/next node when opened or paged. Stamina refill modal (daily free / ad / Diamonds), **daily Biomass surge** counter (`RunSystem.BiomassBonusRunsLeft`; not shown before Biomass has a use). The campaign is endless and never iterated: Main/Overcharged show a page of 5 portals (`PortalsScreen.MainWindow`: 2 cleared, the frontier portal holding `NextMainLevel`, 2 upcoming; Prev pages back through cleared portals, Next never past frontier + 2, *Jump to level n*); the endless Dark Side (`ContentDb.DarkSideLevel`) is paged 25 at a time around the next level (`dark_side_level_max` + 1), one page ahead at most, each node showing its Data |
| Run | – | header: pause button (pause modal: Resume / Leave / Abandon), "Level n · name", "Roll r/N · Boss x% awake · Threat t" (N = `RunSystem.BossRollEstimate`: the roll the boss is expected after; its awakening meter is the clock), coins chip; **HUD** (fixed above the board, `RunPresentation.Hud`): HP bar (`HpBar`, "hp / max"), **ATK** / **DEF** chips (`AtkChip`/`DefChip`: `BattleSimulator.FinalStats(RunSystem.WormSpec)` = loadout + run skills + skill levels + run stat perks, the numbers fights use; TODO swap for the rules' in-run stat helper), run level chip (`RunLevel`) + XP bar (`XpBar`, "XP x / need", `RunPresentation.XpNeeded` mirrors `RunSystem.AddXp`), **Roll r/N** chip (`RollCounter`), **Skills n** chip (`SkillCount` = `RunSystem.BuildSize`: every skill, skill level and stat perk gained this run), "Auto-pick: On/Off" (`AutoPickToggle`), boss banner with its awakening, "x rolls left", danger and shards chips; HUD numbers tween to their new values when the worm lands (after a fight: when the playback returns), changed ATK/DEF chips pop; **board** (zoomed in, in its own viewport `BoardHolder` under the HUD; a **follow camera** moves the board: `RunScreen.StepCamera` each frame → `RunFx.CameraStep`, exponential ease capped at `CameraMaxSpeed`, so it never snaps — not at a roll, not at a rebuild (the camera is kept in window-independent board coordinates); a drag on the board looks around and holds the camera for 3 s, the next move follows again): a long path that never repeats (generated ahead in segments by the rules) drawn as a snake grid (5 columns of 164×94 tiles, 3 lanes per band, `TileCenter`), only a window of it from a little behind the worm to 70 columns ahead (`WindowStart`/`InWindow`, origin a multiple of two bands so tiles keep their place); once the boss arena opens ahead of the worm only the walked path and the arena ("BOSS arena", its own lane) are drawn; resolved tiles keep their look with a ✓ (`TileLook.Cleared`, a beaten fight never turns into a new tile under the worm) drawn flipped so the path starts at the bottom and climbs (`DrawPoint`); tiles are rounded cards outlined and labelled by type (`Theme.TileLabel`/`TileAccent`: FIGHT red, SHOP blue, EVENT purple, CHEST/LOOT gold, REST green, ELITE, MINI BOSS, BUFF, CURSE, TRAP, SHARD, ITEM, START, BOSS gate), walked edges lit; **passed tiles keep their look**, only tiles the worm **landed** on (`TileLook.Landed` = `RunTile.TimesResolved > 0`) are filled with their colour and get a `LandMark` dot (the tile landed on in the current move changes at the landing), forks marked ("fork"), branch options glow with A/B; **at a fork** (no modal) each option's path is tinted and the tile it would land on with the steps left (`BranchLanding`/`BranchPath`: stops early at a shop or the next fork) gets a big pulsing "A · LAND" / "B · LAND" tap target (`BranchPick:A/B`; the option tile itself is `BranchOption:A/B`), the ROLL button reads "PICK A PATH", campfire-lit tiles show +n%, boss marker ("BOSS n%", a free cell next to the gate, never on a tile); the worm token is a board-game piece (round glowing base, the worm bobbing on it with `UiBob`, a shadow on the ground) smaller than a tile; a roll first shows **tumbling dice** over the board (`DiceView`/`RunFx.DiceFaceAt`/`DicePose`: as many dice as rolled, ~1 s by hand, 0.45 s in auto, landing on the rolled values; the footer dice follow), then the token **hops** tile by tile (`RunFx.HopPosition`/`HopScale`: eased step, arc, squash at take-off and landing, stretch in the air; 0.24 s per tile, 0.15 s in auto; a tap on ROLL fast-forwards ×4), passed pickups (shards, coin clusters) pop a small text, and the **landing** pops the tile (scale overshoot, two rings, a glow in its colour) followed by **feedback popups** (`RunPresentation.Landing`: from the rules' outcome lines when a landing publishes a non-sheet outcome, else from what the tile is — FIGHT!/EVENT!/SHOP!… — or its definition: "+6% ATK", "+25 coins" with coins flying to the coin chip, "−10% HP" with a shake, boss tiles…; `RunPresentation.FallbackKinds` lists the kinds without rules outcome data) and a short beat before a fight or a decision opens (`RunScreen.LookOf` = the pure tile look; `TileCenter` is the one board layout function). **Bottom bar** (fixed, toast strip reserved above it): dice + last landing text, 4 skill slots (**rarity colours**: Common grey, Rare blue, Epic purple, Legendary orange, Mythic red, `RunPresentation.SkillColor`; the family is a small corner tag `FamilyTag`; "+n more", empty = outlined), Loot (count), big **ROLL** (hold for auto; CHOOSE reopens the decision; **FIGHT!** / **ENTER ARENA** at an encounter → `RunEncounter`: a clock miniboss attacks or the worm walks into the boss arena, each fight its own step and playback), **AUTO** on/off, hint line. Decision popups: **event** card (EVENT caption, name, text, option cards with GAIN/RISK/COST/CHANCE/ACCOUNT LOOT chips from the option's outcomes, `RunScreen.OptionTags`, plus the risk text; unaffordable options disabled), **outcome result** (`Modal:Outcome`, from `ActiveRun.LastOutcome` once per `OutcomeSeq`, after any battle playback, before the next decision): "EVENT/CHEST/CAMPFIRE/SHOP RESULT", title, "You chose: …", for chance options a **chance roll** (`ChanceMeter`: "65% chance", a green lucky zone and a red one, a marker sweeping and settling for `ChanceSpinSeconds`, then SUCCESS! / NO LUCK; `RunScreen.InstantOutcomes` = skip mode), then one row per gain/loss with an icon and numbers (`OutcomeLineText`: coins, HP, "+8% ATK for the run", new/lost skill, XP, level-ups, shards, "account loot, paid when you beat the boss", boss awakening and pace, boss strength, danger, fight won/lost) and Continue; **no result sheet after a shop visit** (its purchases were seen in the shop) nor for tile outcomes (shown as landing popups; `RunPresentation.IsSheetSource`); auto mode passes it after `AutoOutcomeSeconds`, **draft** "NEW SKILL!" (1 of 3 cards tinted and outlined by rarity, RARITY · FAMILY caption, family tag on the icon, UPGRADE / SYNERGY / "LEVEL n → n+1" chips with the level-up text, reroll); after a beaten miniboss the **MYTHIC** choice (`MythicTitle`, red sheet, "Miniboss defeated — take 1 of 3 Mythic skills"); shop (coins and "n bought this run" line, offer rows coloured by rarity with rarity/family or the level-up, price buttons that turn into "Sold" in place — the sheet stays open while buying, `RefreshShop`; reroll, leave), revive (ad / Diamonds / give up), Burrow (descend / cash out); **auto mode**: hold ROLL (or AUTO) → `RunAutoStep` loop for the clear steps (roll, tiles, battles with fast playback), any tap stops it; at a player decision (`RunSystem.RequiresPlayer`) it pauses ("AUTO PAUSED — your choice", tap-to-stop overlay removed), the decision popup stays open, and auto resumes by itself after the decision while still on; "Auto-pick: On/Off" (HUD and Settings, `ClientSettings.AutoPickDrafts`, off by default) = farming: default choices incl. mythic picks, shops buy sensibly (`RunSystem.FarmShopPick`, one purchase per step). Without auto-pick no auto step ever answers a choice: every event / chest / campfire (even single-option) pauses, the event sheet never sends an auto-pick step |
| Battle playback | – | animates `LastBattle` on a stage (player side left, enemies right, `StagePosition`): rounded role sprites (pink worm pill, A alien, M mount, E/EL/B enemies) with name, HP / shield / rage bars (values straight from the log entries: `TargetHp/TargetShield/TargetRage`, `ActorShield/ActorRage`, fighters' `StartShield/StartRage`), status chips (BURN 2…). Per entry (`UiTweens`, no packages): attacker lunges toward its target (enemies on their turns too, rage attacks further), target flashes white and shakes, floating numbers rise (crits bigger with "CRIT"), DODGE (side-step) / heal / shield / status / RESIST / STUNNED / PARRY / KO popups, HP bars tween down, knocked-out fighters fade; turn-order strip ("NEXT": upcoming actors), round counter, the worm's HP / rage / shield panel, VICTORY / DEFEAT / TIME UP banner, Continue, collapsible battle log, speed ×1/×2 (×3 needs the monthly pass, marked PASS). `BattlePlaybackScreen.Instant` = skip mode (no animations, the log is applied on the next frame, auto playbacks close at once); `TimeScale` speeds scripted playthroughs; auto mode keeps a finished fight on screen at least `MinVisibleSeconds` (1.8 s) in total, so even a one-blow fight is seen |
| Run result | – | VICTORY / DEFEAT, portal · level · boss defeated, the level's three stars (NEW for feats earned this run), rolls / run level / skills / minibosses / shop buys chips, **skills gained this run** (`GainedSkills`: one chip per gain coloured by rarity, "Name Lv n · draft / MYTHIC / shop / event", `RunResultScreen.GainText`), LOOT SECURED reward tiles (or pending loot lost), "Biomass surge ×4 applied" + surge runs left (only once Biomass has a use, `RunSystem.BiomassSurgeActive`), **double rewards** (outlined; rewarded ad, instant with No Ads → `RunDoubleRewards`, daily cap shown), **NEXT: LEVEL n** after a won main run (else PLAY / TRY AGAIN, named `Again`), Back to portals |
| Worm | `parts` | header card with the worm, name, level and power; level up (×1/×10, Biomass), stats, six body slots (cards outlined and tinted by part rarity, empty = outlined) with equip modal + "Equip best parts", talent road (ranks, tier completion, perk) once `worm_level` unlocks, "All parts" (Parts screen) |
| Parts | `parts` | 14-step ladder (Common … Mythic, Mythic+1/+2, Mythic S, S+1/+2, Mythic SS, SS+1/+2; `Theme.PartRarityColor`), inventory with slot filters (equipped first, then rarity), quick merge (asks first if locked parts below Legendary exist); detail modal: stats, the five skills locked/unlocked by rarity (Epic/Legendary/Mythic/Mythic S/Mythic SS), level up / cap raise / level reset, equip, **lock** (client-side `PartLocks`, PlayerPrefs per player), rune sockets, Forge, dismantle (confirm for Legendary+/locked); **merge picker**: own-def copies (needed/owned), fodder points and LE, Ore, candidate list with add/remove, auto-select of the best fodder (`PartPlanner`: cheapest set without superfluous parts, other defs first; locked, Mythic+ and Legendary+ copies of equipped defs only as a fallback that needs a confirmation; equipped/awakened/forged never), checked by `PartsSystem.ValidateMerge`; **Replicator** (Mythic+ parts: 10 LE of other-def Legendary+ surplus + Ore → Legendary copy, `ValidateReplicate`); **awakening** (Mythic+, once, 50 Awaken Cores): preview of the new name, stats before → after and the five skills swapped; Runes tab: lock, merge 3→1 |
| Signals | `part_pulls` (+ per type) | Part / Blueprint / Alien Signals, featured mythic banner with countdown vs standard pool, published odds, 1×/10× with tokens or Diamonds, daily ad pull, reveal animation (a result without a use yet, e.g. hero shards before Heroes, is marked "kept for later": it waits in the reward stash) |
| Laboratory | `laboratory` | header: Data, next research cost/duration (`LaboratorySystem.NextDataCost`/`NextDurationMs`, Annex ×2), slots busy, Research Annex requirement/staffing (`LaboratoryScreen.AnnexStatus`); active research slots (main/Annex) with Diamonds/ad/free speed-ups and collect; **tree** per branch (offense/defense/elements/command, `LabTreeLayout`: one row per tier, nodes ordered by their prerequisites, no overlaps): stat nodes steel-edged, skill nodes family-coloured with a SKILL band and on/off, edges gold = ALL prerequisites, thin = ANY; node modal: effect now/next, ANY/ALL prerequisites (other branches named), research button with the block reason, draft-pool toggle for skill nodes |
| Heroes / Mounts | `heroes` / `mounts` | unlock from shards, stars, levels (Hero XP / Fuel), awakening (mounts), equip, trait/weapon skills by star |
| City | `biomass_farm` | COLLECT ALL, 12×12 grid (green city palette, rounded cells) with areas (locked areas labelled), place/move/store buildings, upgrade with timers + speed-ups, collect (one/all), production priority, blueprint star-up, workers, area unlock; staffed boons (Research Annex): banner while unbuilt/unstaffed and "needs 2 alien workers of 3★+ (staffed n/2)" in the details |
| Aliens | `aliens` | list by rarity, stats, level/star up, assign to building, squad toggle |
| Breeding Pen | `breeding` | pick parents (cost/duration preview), jobs with timers, collect, replace/discard modal comparing genes |
| Observatory | `invasions` | one invasion list (`ObservatoryScreen.MergeInvasions`: rules calendar occurrences matched by `occurrenceId` with the service's attackable instances, server-pushed extras appended with their content names) with Attack per invasion, planetary events, upcoming, modifier stack with countdowns |
| Quests | `quests` | daily/weekly with point chests, achievements, 7-day login calendar, new-player event |
| Shop | `shop` | offers (starter/rolling/flash/gift), Diamond packs, monthly pass, season pass, funds, permanent, Diamond items, rewarded ads |
| Mail | – | inbox, open/read, claim, claim all, delete |
| Arena | `arena` | league/points/tickets/Arena Medals, opponents + fight result modal, weekly leaderboard, **medal shop** (`ShopBuyItem`, section `arena`: Awaken Core 200 medals 2/week first, part tokens, Data, Gear Cores; bought n/limit this week, reset countdown, "Sold out"); the Diamond shop no longer lists arena/guild items |
| Guild | `guilds` | online only (offline shows "requires online"): create/search/join, members by weekly invasion damage, chat, guild shop, leave |
| Settings | – | offline/online, server URL, display name, account, run auto mode "Auto-pick drafts (farming)" toggle, **Developer** card (below) |

**Developer card** (Settings, `SettingsScreen.DeveloperCard`): owner dev tools through `IGameService.DevTools`, which is
non-null offline only in the editor and development builds (offline mode is open to every player) and online only when the server's session said `devTools: true` (the player's Firebase uid is in
the server's `dev_tools_player_ids` allowlist, docs/setup.md step 15; the server checks every call, docs/server.md → Dev tools).
- *Reset progress…* → confirm modal → a brand-new player (day 1, starter kit; same player id and name, version + 1).
- *Jump to day…* → modal: day 1–60 with −7/−1/+1/+7 steps (clamped, default 7), profile tabs Casual (~15 min/day), Active
  (~1 h/day, default), Hardcore (~3 h/day) → the simulator's bot plays a fresh account up to that day (offline: on device,
  synchronously — a 60-day hardcore account takes a few seconds; online: `POST /v1/dev/jump`).
- Afterwards the app reloads (`App.Start` on the same service: `/v1/session` online, the save offline) and lands on the hub;
  toast `DevToolsOutcome.Summary`, e.g. "Day 14 · active player · level 33" / "Progress reset · day 1"; failures toast
  "Dev tools: …" and change nothing.
- Offline only, in the same card: the save path and *Unlock all features (dev)* (the rules' dev-unlock flag).

**Adding a screen:** write a `UiScreen` subclass in `Assets/Scripts/Screens/` and add a `ScreenEntry` (with its feature
gate) to `ScreenCatalog.All` (`Todo(...)` creates a placeholder). The hub lists it under "More" once unlocked unless
`HubLayout` gives it a tab or a column slot; the smoke tests pick it up automatically. Extend `Fmt.Reward` / `EventText` for new reward kinds and events. Reward previews use `Fmt.Rewards(rewards, content, state, now)`, which leaves out rewards without a use (GDD §13.1; "—" when none is usable); offers and shop items whose headline reward has no use are hidden.

**Onboarding / tutorial** (`App/Tutorial.cs`, GDD §16): no forced popups besides the unlock moment. Content tutorial
steps are completed automatically (`TutorialAdvance`) as soon as the player has done the thing (counters/state); the hub
shows the next goal with a Go button when it needs another screen (the run goals are the PLAY card). The first run is
played without parts; once Parts unlock (level 1) it asks to equip the starter parts, and when nothing is actionable it
points at `Features.Next`. UI helpers of the hub and the moment (rounded cards, planet, icon buttons, PLAY card, teaser,
tabs; procedural sprites, wireframe palette) are in `Ui/HubKit.cs`; `UiScreen.HeaderHeight` = 0 hides the header. Unlocks, goals and rewards appear as toasts (at most two, 1.8 s, never blocking input) in a strip just above the
screen's fixed bottom bar (`UiScreen.FooterHeight`/`ReserveToastStrip`): the Run screen keeps that strip free between
the board and its ROLL bar, so toasts never cover the board.

## Run mini-games (GDD §3.6)
A GAME tile (`RunPhase.Minigame`) opens `MinigameHost` as the run screen's decision (`RunScreen.ShowDecision`): it sends
`RunMinigameStart` (the rules pick the game, seed and reward type), shows the title, the one-line instruction
(`MinigameDef.Instruction`), the prize ("RUN COINS" / "SKILL ROLL") and an animated hint (a finger tapping, dragging or
swiping on the game's highlighted target, `MinigameLogic.HintTarget/HintTo`), then **PLAY** (or a tap on the arena) →
3-2-1 → the game (timer bar, status line) → the tier medal → `RunMinigameFinish {Tier}` → the run screen's result sheet
("MINI-GAME RESULT", "Medal: Gold", the coins or "Skill roll: pick 1 of 3 (Epic odds)") and, for a skill roll, the draft.
**Skip** finishes as Miss. Auto mode pauses at the tile; auto-pick (farming) lets the rules resolve it as Miss.
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
EditMode (`Assets/Tests/EditMode`, 214 tests; `RunPresentationTests`: dice skip mode (lands on the value, never early, two dice in the footer), follow camera bounded per frame and never reset by a roll/rebuild, only the landed tile coloured, a popup for every tile kind and outcome line, landing popup on the board, HUD HP/ATK/DEF/level/XP with run perks, rarity colours (slots, draft cards), no sheet after a shop; `RunDepthUiTests`: MYTHIC choice, skill count, shop in place, skills gained, no early surge, encounter step, auto never answers a choice, cleared tiles):
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
- `HubOnboardingTests`: the day-1 hub of a brand-new account (only PLAY, the Planet tab, the teaser "Parts — clear level 1",
  the stamina, Diamonds and profile chips; nothing greyed out), level 1 cleared for real → unlock moment (name, description,
  +20 Diamonds, one action "EQUIP YOUR PARTS") claims the gift and opens the Worm, back = hub with the Worm tab (NEW) and
  the equip goal; tabs, side icons and currency chips appear as features unlock; every unlocked catalog screen is
  reachable (tabs, columns, More, profile); teaser texts for the level gate and the day gate; the day-2 City moment
  ("DAY 2", "BUILD THE FARM", "Tomorrow: Worm Level & Talents") opens the City; a pair (Part Signal + Shop) is one moment
  that claims both; backing out does not reopen it every frame; Continue replaces PLAY during a run.
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
  saved, toast) and jump (day steps clamped to 1–60, profile tabs, account day and creation time of `AccountAgeJump`, hub,
  toast "Day 4 · casual player · level n"); online jump/reset against a fake server (request body, 120 s timeout, state
  applied, session reloaded, toast) and a 403 (error toast, state unchanged).
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
- `RunAndProgressionScreenTests` (auto mode): auto pauses at a shop with buyable skills (no command sent, tap-to-stop
  overlay removed, shop modal usable), the player buys a skill and leaves through the modal, the shop result lists the
  skill and auto passes it, then auto resumes by itself;
  the run screen's "Auto-pick" toggle turns farming on and farming leaves the shop without buying.
- `RunForkOutcomeTests`: branch landing (steps left, shop stop, path), fork picked on the board (A/B landing tap targets
  on the landing tiles, no modal, PICK A PATH, path B taken), zoomed board + "Roll 0/N" clock (subtitle, chip, banner),
  chance event result (chance meter "65% chance", SUCCESS!/NO LUCK, every outcome line, account-loot / max-HP risk text,
  icons, shown once), guarded chest result (fight, no chance meter), auto passes the result after a short look and rolls
  on, outcome line texts.
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
- `RunMotionShowcaseTests` (**Explicit**) captures the run's motion (HUD, dice tumbling, mid-hop, landing pop, popups, two dice) into `$ROGUESTARS_SHOTS_DIR` (default `client/RogueStars/Screenshots/run-motion/`).
- Skip flags: `RunFx.Instant` (dice land at once, hops jump, popups settle in place; everything in `RunFx` is also synchronous outside play mode), `RunScreen.InstantOutcomes`, `BattlePlaybackScreen.Instant`.
- `PlaythroughTests.FreshSave_Playthrough_WithScreenshots` boots `Main.unity`, plays a fresh offline save (day-1 hub with
  PLAY only → first run without parts with a manual roll, then auto mode to the boss, making every decision auto pauses at
  through the decision modal (shop: buy a skill then leave, draft: first card, event: default option, branch: path A's
  landing tile on the board, defeat: give up, a GAME tile (none on level 1; for later runs): the mini-game's scripted player plays it for Silver; outcome results are passed by auto, the first one is captured as
  `10_outcome`) → result → Parts unlock moment → its action opens the Worm → equip the starter parts → hub with the
  Worm tab → worm/parts → city build → dev unlock → hub with everything unlocked → 10× Signal with reveal → stamina refill → parts ladder, part detail with skills, Mythic merge picker,
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
- Play Games sign-in needs the console setup of docs/setup.md step 3 (fingerprints, Play Games Services credentials, web
  client id); until then release builds sign in anonymously. Push notifications are not integrated.
- Ads are disabled (no AppLovin account yet); switching them on is docs/setup.md step 5.
- Mediation network adapters are not installed (AppLovin's own demand only); add them with the AppLovin Integration Manager.
