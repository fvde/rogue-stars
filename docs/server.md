# Rogue Stars server

ASP.NET Core (.NET 10) minimal API in `server/RogueStars.Server`. It runs the shared rules engine
(`shared/RogueStars.Rules`) authoritatively and hosts the cross-player features (arena, guilds, invasions).
Tests: `server/RogueStars.Server.Tests` (xUnit + WebApplicationFactory). Infra: `infra/terraform`.

## How commands work
The server knows no game command by name. `POST /v1/command` resolves `type` through
`GameEngine.CommandType(name)` (the class name registered by any system), deserializes `args` into it
(System.Text.Json, fields included, case-insensitive), runs `engine.Execute`, and saves with compare-and-set on
`GameState.Version`. New commands added by any workstream work without server changes.

- **Server-only commands** are rejected from clients with `403 server_only_command`. A command is server-only when
  its class has `[ServerOnly]` (`shared/RogueStars.Rules/Core/ServerOnlyAttribute.cs`), when its name is one of the
  hook names below, or when it is in `RogueStars:Commands:ClientDenyList`. `ClientAllowList` (if non-empty)
  restricts clients to the listed commands.
- **Idempotency:** a `requestId` is stored with its result in the player document (last 64). Repeating it returns
  the stored result with `replayed: true` without executing again. Rule rejections are not stored.
- **Concurrency:** saves are compare-and-set on the version. On conflict the server reloads and re-executes after a short jittered backoff (up to
  8 times). If the client pins `expectedVersion` and it differs, the response is `200 { ok: false, error:
  "version_conflict", version, state }` (local-first: plus `stateHash`, `match: false`).
- **Serialization:** storage uses C# member names (`StateJson.Options`), the wire uses camelCase
  (`StateJson.Wire`). Dictionary keys (currency ids, counters) are never re-cased. The engine's `Cloner` is a JSON
  round trip, so a failed command never changes state.

### Local-first commands
The client runs every player command on its own rules engine first, shows the result at once and syncs in the background
(`client/…/Services/RemoteGameService.LocalFirst.cs`). The server stays the store of record.

- **Request:** `{ type, requestId, args, expectedVersion, atMs, stateHash, battles[] }`. `stateHash` (16 hex digits,
  `Rules.Core.StateHash`: public fields by name, dictionaries/sets sorted, null = empty) is the client's state after the
  command; its presence switches the request to this protocol. `atMs` is when the client ran it: the command executes at
  that time, clamped to `[max(lastCommandMs, now − Commands:ClientTimeMaxLagMs), now + Commands:ClientTimeMaxLeadMs]`
  (10 min / 5 s). `battles` are the client's battle results without logs (`BattleResult.Summary()`), in fight order.
- **Response:** agreement → `{ ok: true, version, stateHash, match: true }` (no state, events or payload). Disagreement
  (hash mismatch, rule rejection, `expectedVersion` conflict) → `{ ok, error?, version, stateHash, match: false, state }`. The client adopts `state` and re-runs its unsent commands
  on it. A retried `requestId` is replayed and answered the same way (ack when the client's hash is current).
- **Queue:** in order, one request in flight, network failures retried with backoff (1–30 s) while play continues.
- **Server-first** (busy until the server answers, after the queue drained): commands carrying an ad token (server-verified
  ad credits), `PurchaseStart`, and the non-command calls that change the player on the server: `/v1/iap/verify`,
  `/v1/ads/instant`, arena fights, invasion attacks, guild create/join/leave (membership hook), dev tools. Mail, live ops and
  arena results written by server hooks bump the version; the client's next command meets a version conflict and adopts.
- **Only commands write.** `/v1/session` and `/v1/state` return the stored state settled at server time (with `stateHash`)
  and do not save it; the player document is written by commands, server hooks (mail, arena, invasions, purchases, ads,
  guild membership) and the dev tools only, so an idle or watching player costs no writes. The client settles its copy
  locally, and the server settles the stored state at each command's `atMs` before running it; both reach the same state
  because settling is path-independent (below).
- **Refresh** (every 30 s online): the client settles locally and reads `GET /v1/version` (the stored version, a read). Only
  when it is ahead of the client's (a change made on the server: mail, ad reward, arena, live ops, another device) does the
  client download `/v1/state`; with commands still queued it skips that (their replies bring the state on a version conflict).
- **Server-first calls** (no `stateHash`: ad-paid commands, `PurchaseStart`, hooks) run at server time and answer the full
  state.

### Path-independent settling
`GameEngine.Settle(state, t2)` after `Settle(state, t1)` gives exactly `Settle(state, t2)` (same `StateHash`), and a command
after any number of settles gives the same state as on the unsettled copy. Property tests:
`tools/RogueStars.Sim.Tests/SettlePathIndependenceTests.cs` (random split points from minutes to 75 days on a new player, a
dev-unlocked one, simulator accounts of 1–20 days and a 60-day production state in `Fixtures/state60.json`; and command
sequences where only the client settles in between). How:
- **Breakpoints.** Systems register the times at which time-driven state changes discretely
  (`GameEngine.RegisterBreakpoints`): modifier starts/ends, planetary event occurrence boundaries, auto-completing timers,
  summon event ends, daily resets. A settle runs the settlers at every breakpoint after `GameState.SettledMs` in order, then
  at the target time, so a discrete change always happens at its own time, never at whenever somebody settled.
- **Exact accumulators.** Regen counts whole ticks with the remainder kept in `LastTickMs`; regen speed and cap bonuses are
  taken just before the settle time (each stretch between breakpoints has one speed). City production and pull chances add
  integer millionths with the sub-unit remainder in `CityBuilding.Carry` (a double sum splits differently at every settle).
- **Commands settle twice.** `Execute` settles at the command time before the handler and again after it, so whatever the
  command made due (unlocks, stash releases, event joins) happens in the command; a new player is settled at creation too.
- **Presence-only effects need a command** (`CommandContext.Acting`): the returning-player gift comes with the first command
  after the break (not the session), a summon event's unclaimed rewards are mailed at its end only if the player ran a
  command while it was on (`SummonEventProgress.Active`), and a regen tick time in the future (clock moved back) restarts only
  on a command. Planetary events are materialised from the last settle on, without the former 60-day lookback.

### Offline battles and sampled validation
Run battles are simulated on the client inside the command (`CommandContext.ResolveBattle`) and played back from that
device's log (`ActiveRun.LastBattleForPlayback()`); the playback shows "Round n" only, never the battle's length. The run
state keeps `BattleResult.Summary()` (no log). The server takes the client's claim as the result and re-simulates a sample
from the same inputs (setup + the run's battle RNG fork): `Battles:StandardSampleRate` 0.10, `EliteSampleRate` 0.50
(elite tiles, minibosses, ambushes), `BossSampleRate` 1.0. A sampled battle always resolves to the server's result: a
disagreeing claim changes the state hash, the client gets the server state and the run continues from the server's outcome
(silently, also for bosses). A claim without its fighters is treated as a mismatch.

Every re-simulated battle is recorded in `battle_audits/{playerId}`: `Validated`, `Mismatches`, the match flags of the last
`Battles:FlagWindow` (20) validated battles, the details of the last `KeepRecentMismatches` (5) and `Flagged` (sticky) once
`Battles:FlagMismatches` (3) of the window disagreed; a warning is logged for each mismatch and for the flag. Nothing is
blocked. `Battles:ReportToClient` (on in Development) adds `battleChecks[]` (both results as text) to the response so a dev
build shows a "DEV: BATTLE MISMATCH" banner; owners on the dev-tools allowlist get it on every server. Production players
never see it.

Determinism: battles use only IEEE basic arithmetic plus `CombatMath.PowInt` (no `Math.Pow` in battles or enemy scaling),
the rules RNG and no dictionary-order outcomes. `BattleDeterminism` (rules) simulates a fixed probe of 48 battles built from
content; the .NET suite and the Unity EditMode suite (Mono) check its fingerprint against
`shared/RogueStars.Rules.Tests/Golden/battle-determinism.txt`. Changing skills, enemies or battle rules changes the golden:
update the file from the .NET test's message.

### Hook commands (resolved by name at runtime)
The server issues these itself. Their classes belong to the rules workstreams; the server passes the listed fields
(extra fields are ignored by the deserializer). Names are configurable under `RogueStars:Commands:*`.

| Config key | Default name (rules command) | Issued by | Args passed | Rules effect | When missing |
|---|---|---|---|---|---|
| `PurchaseComplete` | `PurchaseComplete` (Meta/Shop) | `/v1/iap/verify` | `ProductId, ReceiptId (= Play order id, or gp:<token hash>)` (+ `OrderId, PurchaseToken, Store, Test`, ignored) | grants the product; needs the client's `PurchaseStart` (pending record) first, else `200 {ok:false, error:"no pending purchase"}` and the receipt can be retried | `501` |
| `AdWatched` | `AdWatched` (Meta/Shop) | `/v1/ads/ssv` | `PlacementId (= custom_data), AdToken (= event_id)` | consumes the placement's daily ad cap and grants its direct rewards (placements whose effect belongs to another system become ad credits spent by that system's command) | `501` |
| `InvasionAttack` | `InvasionRecordDamage` (Meta/LiveOps) | `/v1/invasions/{id}/attack` | `OccurrenceId (= the instance's rules occurrence id), Damage, InvasionId (= TypeId), StartMs, EndMs` | starts the attempt atomically (feature gate, one `invasion_attempts`), records damage/score, guild contribution; a failure rejects the attack (`409`) | attacks are free |
| `InvasionOutcome` | `LiveOpsApplyOutcome` (Meta/LiveOps) | invasion-resolve job | `InvasionId (= TypeId), OccurrenceId, Repelled, ContributionRank` | lasting win/lose modifiers of the content invasion + rank rewards (+ a chance of an Awaken Core for top contributors of a repelled high-tier invader, rolled by the rules) | invasion stays pending |
| `ArenaStart` | `ArenaStartFight` (Meta/Arena) | `/v1/arena/fight`, before resolving | `OpponentId` | feature gate + one arena ticket; a failure rejects the fight (`409`) | fights are free |
| `ArenaFight` | `ArenaFightResult` (Meta/Arena) | `/v1/arena/fight`, after resolving | `Won, OpponentPoints` | rules-side arena points, Arena Medals, `arena_fights`/`arena_wins` | no rules bookkeeping |
| `MailDeliver` | `MailPush` (Meta/Mail) | arena weekly rewards, invasion top rewards | `Title, Body, Rewards (Reward[])` | puts the mail in the inbox (claimed by the player) | queued in `mail_outbox`, delivered by `daily-reset` |
| `GuildMembership` | `GuildSetMembership` (Meta/Guild) | guild create/join/leave/kick/promote | `GuildId (null when left), Role` | membership for guild shop and contribution | skipped |

All hook commands are `[ServerOnly]`; every name in this table is also refused from clients (so clients cannot start
arena fights themselves). Every server invasion instance carries the rules live-ops `occurrenceId` its player-state
contributions and outcomes are keyed by (see Invasions), and `typeId` = a content invasion id (`gloop_tide`,
`rust_swarm`, `void_choir`, `prism_horde`); an extra instance created without `typeId` must use a content id as its id,
otherwise creation fails with `400 unknown_invasion_type`.

Every hook call carries a deterministic request id (e.g. `arena-2026-W47-<player>`, `inv-outcome-<invasion>`,
`iap-<purchase token hash>`, `ad-<event id hash>`), so retried jobs and callbacks never apply twice.

## Auth
| Mode | Config | Behaviour |
|---|---|---|
| `firebase` (default) | `RogueStars:Auth:FirebaseProjectId` (Production: `rogue-stars-515610` from `appsettings.Production.json`; Terraform sets the same) | `Authorization: Bearer <Firebase ID token>` (the client signs in with Play Games or anonymously, docs/client.md → Auth). RS256 against Google's securetoken certs (cached by max-age); `iss = https://securetoken.google.com/<project>`, `aud = <project>`, `exp/iat/auth_time` (5 min skew), non-empty `sub`. Player id = Firebase uid. |
| `dev` | `RogueStars:Auth:DevSecret` | `POST /v1/auth/dev {deviceId}` returns `{token, playerId: "dev_<deviceId>"}`; the token is an HMAC of the player id. **Startup fails if dev mode is configured with `ASPNETCORE_ENVIRONMENT=Production`.** |

Jobs (`/internal/*`): `oidc` (default) requires a Google-signed ID token (`iss` accounts.google.com, `aud =
RogueStars:Jobs:Audience`, verified `email = RogueStars:Jobs:ServiceAccountEmail`); `secret` (dev only, refused in
Production) requires header `X-Job-Secret: <RogueStars:Jobs:Secret>`.

## API reference
All `/v1/*` endpoints except `/v1/auth/dev` and `/v1/ads/ssv` need the bearer token. Errors are
`{ "ok": false, "error": "<code>", "details"?: {...} }` with a 4xx/5xx status; rule rejections of a command are
`200 { "ok": false, "error": "<rule message>" }`.

### Player
| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/v1/auth/dev` | `{ deviceId }` | `{ token, playerId }` (dev mode only) |
| POST | `/v1/session` | `{ displayName? }` | `{ ok, playerId, created, serverTimeMs, version, stateHash, state, commands[], adsDisabled, purchasesDisabled, devTools }` — creates the player with `engine.NewPlayer` and a server-chosen seed, else loads; `state` is settled (not saved); `commands` lists client-allowed command names; `devTools` = this player may use the dev tools (below) |
| GET | `/v1/state` | – | `{ ok, serverTimeMs, version, stateHash, state }` (settled at server time, not saved) |
| GET | `/v1/version` | – | `{ ok, serverTimeMs, version }` — the stored version (a read): the client's refresh downloads the state only when it moved |
| POST | `/v1/command` | `{ type, requestId?, args?, expectedVersion?, atMs?, stateHash?, battles? }` | `{ ok, error, events[], payload, version, replayed, state }`; local-first (`stateHash` sent): `{ ok, version, stateHash, match, state? , battleChecks? }` (see Local-first commands) |

Example:
```json
POST /v1/command
{ "type": "GachaPull", "requestId": "c3f1…", "args": { "bannerId": "parts_std", "count": 10 } }
→ { "ok": true, "error": null, "events": [{ "type": "pulled", "args": ["…"] }], "payload": {…}, "version": 42, "replayed": false, "state": {…} }
```

### Payments
| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/v1/iap/config` | – | `{ ok, obfuscatedAccountId, productSet, products[{productId, storeProductId, type}] }` (type `consumable` / `non_consumable`; only the products of `Iap:ProductSet`) |
| POST | `/v1/iap/verify` | `{ productId (rules id), purchaseToken }` | command response of `PurchaseComplete`; `{ ok, alreadyCompleted, orderId }` when the token was already redeemed by this player; `400 unknown_product`; `400 product_not_offered` (not in `Iap:ProductSet`); `402 <reason>` (invalid_receipt, purchase_pending, purchase_not_completed, product_mismatch, unsupported_quantity, account_mismatch, test_purchase_not_allowed, already_consumed); `409 receipt_already_used` (token claimed by another player); `503 store_unavailable`; `503 purchases_disabled` (`Iap:Mode=disabled`); `501` if `PurchaseComplete` is not registered |
| GET | `/v1/ads/credits` | – | `{ ok, credits[{eventId, placement, atMs}] }` — verified, unspent ad views (see below) |
| POST | `/v1/ads/instant` | `{ placementId, requestId }` | command response of the `AdWatched` hook with an empty token: grants a direct-reward placement without an ad. The rules accept it only for No Ads owners or in the ads-disabled mode (`ad token required` otherwise); daily caps apply; idempotent per `requestId`; `400 unknown_placement` / `placement_spent_by_command` / `request_id_required` |
| GET/POST | `/v1/ads/ssv?user_id=&event_id=&custom_data=&placement=&ad_unit_id=&event_token=` | – | `{ ok, outcome }` (`granted` / `credited` / `unknown_placement` / `rejected:<rule>`); `{ ok, duplicate: true }` for a repeated event; `400 malformed_callback`; `401 invalid_signature`; `403 ad_unit_not_allowed`; `409 event_already_used` (same event id, other user); `404 ads_disabled` in the ads-disabled mode |

#### Purchases disabled (`Iap:Mode = disabled`, the default for now)
Google Play payments are on hold (no payments profile yet). `/v1/session` returns `purchasesDisabled: true`,
`/v1/iap/config` returns `enabled: false` and no products, `/v1/iap/verify` answers `503 purchases_disabled` before any
receipt is looked at. Diamond-priced shop items (`ShopBuyItem`) and everything else are unaffected. The client shows
real-money products as "Coming soon" and never initializes Unity IAP (docs/client.md). Switch on with `Iap:Mode=google`
(Terraform `iap_mode`, docs/setup.md step 14).

#### Google Play (`Iap:Mode = google`)
Implemented in `Payments/GooglePlay.cs` with `Google.Apis.AndroidPublisher.v3`, following
https://developer.android.com/google/play/billing/security#verify:
1. The rules product id is mapped to its SKU (`ProductDef.StoreProductId`, e.g. `rs.diamonds_60`) and Play type
   (`Payments/PlayCatalog.cs`: lifetime limit 1 → non-consumable, everything else → consumable; the **monthly pass is a
   consumable 30-day block**, not a subscription, because the rules stack 30 daily claims per purchase and model no renewal).
2. Lookup `purchases.productsv2.getproductpurchasev2(package, token)`. Granted only when
   `purchaseStateContext.purchaseState == PURCHASED` (PENDING → `402 purchase_pending`; the client keeps the purchase
   unconfirmed and retries when Play delivers it), the single line item's `productId` is the SKU, quantity is 1, and
   `obfuscatedExternalAccountId` equals `hex(sha256("rs-account:" + playerId))` (the client sets it through Unity IAP;
   required when `Iap:RequireAccountId`, which Terraform enables). `testPurchaseContext` marks license testers
   (accepted while `Iap:AllowTestPurchases`). A token that Google reports consumed/acknowledged but that we never granted
   is refused (`already_consumed`).
3. The purchase token (globally unique) is the key of `iap_receipts/{sha256(token)}`: one player, one grant. Order ids
   are only informational (promo purchases have none; the receipt id is then `gp:<token hash>`).
4. After the grant: consumables `purchases.products.consume`, non-consumables `purchases.products.acknowledge`
   (server-side, as Google recommends; Play refunds unacknowledged purchases after 3 days). A failed finalize is retried on
   the next `/v1/iap/verify` of the same token. Rejected receipts are never acknowledged (Play refunds them).

Credentials: `Iap:ServiceAccountJson` (Secret Manager `rogue-stars-play-publisher-credentials`) or the Cloud Run
identity (ADC) — either must be invited in Play Console (docs/setup.md). `fake` mode: tokens starting with `fake-` are valid.
The Play Console catalogues `docs/play-console-products.csv` (all) and `docs/play-console-products-initial.csv` (the first
four products: `diamonds_60`, `starter_worm`, `monthly_pass`, `perm_daily_diamonds` — `PlayCatalog.InitialProductIds`, same
list as the client's `StoreCatalog.InitialProductIds`) are generated from the shop content
(`UPDATE_PLAY_CATALOG=1 dotnet test --filter PlayConsole`; tests fail when they are stale). `Iap:ProductSet = initial`
(production default, Terraform `iap_product_set`) sells only the initial products until the rest exist in Play Console.

#### Ads disabled (`Ads:Mode = disabled`, the default for now)
No ad network yet (no AppLovin account): every player is treated as owning the No Ads entitlement.
- One rules content flag, `ShopConfig.AdsDisabled` (`shared/RogueStars.Rules/Content/ContentDb.Shop.cs`), makes
  `ShopSystem.HasNoAds` true for everyone: every placement completes instantly with an empty `AdToken`, daily caps still
  apply, no entitlement flag is written to player state (switching ads on later restores the normal rules for everybody).
- `ServerContent.Create` builds a private `ContentDb` with the flag (`ContentDb.Default` is never mutated); `/v1/session`
  returns `adsDisabled: true` so the client mirrors it.
- Command placements (revive, stamina, timer, pulls, double rewards): the client sends an empty token; the credit check
  only applies to non-empty tokens (a made-up token is still `403 ad_not_verified`). Direct-reward placements:
  `POST /v1/ads/instant`. S2S callbacks answer `404 ads_disabled`; no Event Key is needed (Terraform `ads_mode`).
- Everything of the applovin mode below (signature, event claims, credits) is unchanged and active again with
  `Ads:Mode = applovin`.

#### AppLovin MAX S2S rewarded callbacks (`Ads:Mode = applovin`)
Source: AppLovin, *S2S Rewarded Callback API* — https://support.applovin.com/en/max/advanced-features/s2s-rewarded-callback-api
(macro table) and *How Server-to-Server Callback Works* — https://support.applovin.com/en/max/faq/how-server-to-server-callback-works
(worked example). The documented rule, quoted:

> `{EVENT_TOKEN}` — `sha1( EVENT_ID + Your-Event-Key )`
>
> `{EVENT_TOKEN_ALL}` — `sha256( All macros alphabetically, URL-decoded + Your-Event-Key )` — "if EVENT_TOKEN is one of
> your macros, it is omitted from the 'All macros alphabetically' string"
>
> "if your event key is `uHXOnmfO2f3wd-syBwXxsj7m53qiJoI9VzXTvE8LOSnIWjS6zzxS8HgSIBJSbQWdS1czmgwXSWDaKwehtjyYl_` and the
> Event ID is `14a97273742c0c9e8864fe40193dad5506e930bc`, the {EVENT_TOKEN} macro will populate with
> `3724081c17d32a66276ffff2a14b124a278934e9`"

- Callbacks are HTTP(S) **GET**, may arrive minutes after the ad, time out after 5 s and are retried up to twice; they never
  pass through the device. The Event Key is in the AppLovin dashboard under Account → General → Keys (Secret Manager
  `rogue-stars-applovin-ssv-key`).
- Implementation (`Payments/AppLovinAds.cs`): `Ads:Verification = event_token` (default) checks
  `event_token == lowercase hex(sha1(event_id + EventKey))` in constant time; the FAQ example is a unit-test vector.
  `event_token_all` (opt-in) hashes the URL-decoded values of the macros present in the URL, ordered by macro name, plus the
  key. AppLovin publishes no worked example for it, so in the default mode it is checked in shadow and logged
  (`EVENT_TOKEN_ALL shadow check … match/MISMATCH`): switch only after a real callback logged `match`.
- **Callback URL** to configure on each rewarded ad unit (MAX → Ad Units → Edit → *Server Side Callback URL*):
  `https://<service-url>/v1/ads/ssv?user_id={USER_ID}&event_id={EVENT_ID}&custom_data={CUSTOM_DATA}&placement={PLACEMENT}&ad_unit_id={AD_UNIT_ID}&event_token={EVENT_TOKEN}&event_token_all={EVENT_TOKEN_ALL}`
- **Mapping:** `{USER_ID}` = our player id (client `MaxSdk.SetUserId(playerId)` before each show); `{CUSTOM_DATA}` = the
  rules ad placement id (`MaxSdk.ShowRewardedAd(adUnit, placement, customData: placement)`; `{PLACEMENT}` is the fallback).
  `EVENT_TOKEN` signs only the event id, so `user_id`/`custom_data` are trusted because the callback is server-to-server
  and each event id is claimed once: `ad_events/{sha256(event_id)}` is created first (first writer wins), a repeat is
  `duplicate`, the same event id for a different `user_id` is `409`. The claim outlives the player's request window
  (64 ids; test `Replay_IsRejected_EvenAfterThePlayersRequestWindowMovedOn`). Unknown placements are recorded and not
  granted; `Ads:AllowedAdUnitIds` optionally pins the ad units.
- Placements with direct rewards (e.g. `daily_diamonds_ad`) run the `AdWatched` hook. Placements consumed by a rules
  command (`revive`, `stamina_refill`, `timer_speedup`, `free_pull_*`, `double_run_rewards`) become an **ad credit**
  (`ad_credits/{playerId}`, 24 h): a client command carrying an `AdToken` (`RunRevive`, `BuyStamina`, `GachaPull`,
  `TimerSpeedUp`, `RunDoubleRewards`) must use the event id of an unspent credit (`403 ad_not_verified` otherwise,
  `400 request_id_required` without a request id); the credit is spent by that request id and refunded when the rules
  reject the command. An empty token (No Ads entitlement / ads disabled; the rules decide) skips the check. `Ads:Mode = fake` disables the check (dev).

### Arena
| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/v1/arena/snapshot` | – | `{ ok, power, loadout }` — stores `engine.BuildLoadout` of the settled state |
| POST | `/v1/arena/fight` | `{ opponentId?, requestId? }` | `{ ok, fightId, won, pointsDelta, points, seed, opponent{playerId,displayName,power}, log, state }` |
| GET | `/v1/arena/opponents` | – | `{ ok, joined, opponents[{playerId,displayName,points,power}] }` |
| GET | `/v1/arena/leaderboard` | – | `{ ok, week, joined, league, groupId, groupSize, myRank, entries[{rank,playerId,displayName,points,power,wins,losses}] }` |

- Seasons are ISO weeks (UTC). On the first fight of a week the player is assigned to the open group of their league
  (`arena_groups/<week>_L<league>_<n>`, max **150**; a full group opens the next).
- A fight refreshes the attacker's snapshot, picks an opponent at random among the `OpponentWindow` (5) group members
  closest in points (falls back to any stored snapshot near in power when the group is empty), resolves the battle on
  the server via `IBattleResolver` (after the `ArenaStart` hook consumed a ticket), then runs the `ArenaFight` hook (`ArenaFightResult { Won, OpponentPoints }`). The rules (`ArenaSystem`) are the single
  source of truth for weekly points (Elo-style from `StartPoints`, reset lazily per reset-week): the server mirrors the
  `points`/`delta` of the hook's payload (`ArenaFightOutcome`) into the group member. Group members start at the
  rules' `StartPoints`; defenders' points do not change.
- `weekly-arena` job (`?week=` optional, default previous week): ranks every group, mails rank rewards — the rules'
  league ladder (`ArenaSystem.RankRewards`: Diamonds, Arena Tickets and **Arena Medals**, the PvP currency for the arena
  shop's Awaken Cores) unless `Arena:RankRewards` overrides it (then scaled ×(1+0.5·league)) — promotes the top
  `PromoteTop` (15) / demotes the bottom `DemoteBottom` (15). Resumable and idempotent. Per-fight medals are paid by the
  `ArenaFight` hook (`ArenaFightResult`).

### Guilds
| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/v1/guilds?query=` | – | `{ ok, guilds[{id,name,description,level,members,maxMembers}] }` (non-full) |
| POST | `/v1/guilds` | `{ name, description? }` | `{ ok, guild }` — creator becomes leader; names unique (case/space-insensitive) |
| GET | `/v1/guilds/mine` | – | `{ ok, guild, level, contributions }` (guild null when none) |
| GET | `/v1/guilds/{id}` | – | `{ ok, guild{id,name,description,level,members} }` |
| GET | `/v1/guilds/{id}/contributions?week=` | – | `{ ok, contributions{week,total,members[{playerId,displayName,damage}]} }` |
| POST | `/v1/guilds/{id}/join` | – | `{ ok, guild }`; `409 already_in_guild / guild_full` |
| POST | `/v1/guilds/leave` | – | `{ ok }` — leader leaving hands over to the oldest officer, else oldest member; last member disbands |
| POST | `/v1/guilds/kick` | `{ playerId }` | leader: anyone; officer: members only |
| POST | `/v1/guilds/promote` | `{ playerId, role: leader/officer/member }` | leader only; `leader` transfers leadership |
| GET | `/v1/guilds/chat?since=<ms>` | – | `{ ok, messages[{id,playerId,displayName,text,atMs}] }` (last 200 kept) |
| POST | `/v1/guilds/chat` | `{ text }` | `{ ok, message }`; `429 rate_limited` (5 per 30 s, ≥1 s apart; stored in the chat doc so it holds across instances); max 300 chars; `IChatFilter` masks `Guilds:BlockedWords` |
| POST | `/v1/guilds/chat/{messageId}/report` | `{ reason? }` | `{ ok, reportId }` (stored in `chat_reports`, logged as warning) |

Invasion damage of guild members is added to the guild's weekly totals (last 4 weeks kept) and to guild XP.

### Invasions
| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/v1/invasions` | – | `{ ok, invasions[] }` active now: `{ id, occurrenceId, typeId, name, scheduled, startsMs, endsMs, hpPerMember, invaderPower, … }` |
| POST | `/v1/invasions/{id}/join` | – | `{ ok, groupId, members }` — groups of **20** (`Invasions:GroupSize`) |
| POST | `/v1/invasions/{id}/attack` | `{ requestId? }` | `{ ok, attackId, damage, seed, log, groupDamage, groupHp, state }` (auto-joins) |
| GET | `/v1/invasions/{id}/group` | – | `{ ok, invasion, groupId, outcome, groupDamage, groupHp, members[] }` |
| POST | `/internal/invasions` | `{ id?, name?, typeId, startsMs, endsMs, hpPerMember?, invaderPower? }` | job auth; creates an extra (surprise) instance: occurrence `server_<typeId>@<startsMs>`, id `server-<type>-<startsMs>` unless given; `409 invasion_type_already_running` / `too_many_invasions` under the calendar rules below |

**Instances = rules occurrences.** The rules calendar (`LiveOpsSystem.ActiveEvents`, content `LiveOpsSchedule`) decides
which invasions run: never two of the same type at once, at most `LiveOps.MaxConcurrentInvasions` (2) together. The
`invasion-start` job (and, lazily, every `GET /v1/invasions`) creates one instance per active calendar occurrence:
id = `InvasionService.KeyFor(occurrenceId)` (`weekly_rust_swarm@1768176000000` → `weekly-rust-swarm-1768176000000`),
`occurrenceId`, `typeId`, content name, `scheduled: true`, HP/power from `Invasions:HpPerMember` / `InvaderPower`.
Creation is idempotent (create-if-absent per key). Attacks and outcomes pass the instance's `occurrenceId` to the
rules, so the player's contribution for the Observatory's calendar entry and the server instance are the same record.

`invasion-resolve` job: for each ended, unresolved invasion and each group: outcome = `repelled` when group damage ≥
`hpPerMember × members`, else `overrun`; runs the outcome hook for every member (rank by damage), mails
`Invasions:TopRewards` to the top `TopContributors` (3) with damage > 0. Progress is stored per member, so the job
is resumable; the invasion is marked resolved only when every member got the outcome.

### Dev tools (owner only)
Owner tools for normal Play builds against any server, production included (client: Settings → Developer, docs/client.md).
Code: `DevTools/DevTools.cs`; the bot is `shared/RogueStars.Sim.Core` (the simulator's `SimPlayer`, shared with
`tools/RogueStars.Sim` and the offline client).

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/v1/dev/reset` | – | `{ ok, day: 1, profile: null, mainLevel, wormLevel, power, createdMs, serverTimeMs, version, state }` — a brand-new player (`engine.NewPlayer`, account created now, starter kit, new seed) |
| POST | `/v1/dev/jump` | `{ day (1–60), profile (casual/active/hardcore, default active), seed? }` | `{ ok, day, profile, seed, mainLevel, wormLevel, power, commands, elapsedMs, createdMs, serverTimeMs, version, state }`; `400 invalid_day` / `unknown_profile`; `429 dev_tools_busy` (one jump per instance at a time, 30 s queue) |

- **Access control (server-authoritative):** every `/v1/dev/*` call checks the caller's player id (= Firebase uid)
  against `RogueStars:DevTools:AllowedPlayerIds` (env `RogueStars__DevTools__AllowedPlayerIds__<n>`, Terraform
  `dev_tools_player_ids`, default empty; an entry may hold several ids separated by commas). Anyone else: `403
  dev_tools_forbidden` and a warning in the log (`Dev tools refused: <action> by <player>`). `RogueStars:DevTools:AllowAll`
  opens them to everyone for local/dev servers (`appsettings.Development.json`); **the server refuses to start with it in
  Production**. `/v1/session` returns `devTools` so the client shows the card only then; the client flag is cosmetic, the
  check is on the server.
- **What is replaced:** the caller's `players/{uid}` record only, compare-and-set like a command (retried on concurrent
  writes). Kept: player id, display name; `version` = old version + 1 (other clients of the player see a newer version and
  resync; a pinned `expectedVersion` gets `version_conflict` with the new state); the request log is cleared (old request ids execute again). Cross-player
  records stay as they are (guild membership, arena group/snapshot, invasion groups, mail outbox, IAP/ad claims); the rules'
  guild membership is re-applied from `guild_members` (`GuildSetMembership`; refused while Guilds is still locked for the new
  state, then the rules state has no guild).
- **Jump = the simulator's bot** (`AccountAgeJump.Run`): a fresh F2P account created at the daily reset (00:00 UTC) `day −
  1` days before now, played by `SimPlayer` with the chosen time profile through the real rules (runs, city, aliens, lab,
  parts, gacha, quests, login, arena via `ArenaStartFight`/`ArenaFightResult` against a snapshot ladder, invasions with
  their outcomes, tutorial steps, unlock celebrations) on the server's own content (ads-disabled flag included). Every past
  day is played in full, today only the sessions that ended (+30 min margin) before now, then the state is settled at now:
  unlocks carry their real days (≤ N), timers/research/breeding started in the past and end within days, stamina and
  regen are settled to now, daily/weekly resets and the live-ops calendar are the real ones; invasion occurrences still
  running now get no simulated attempts (the server's own instances take over). The profiles are the simulator's
  (`Casual` 2 × 7.5 min/day, `Active` 4 × 15 min, `Hardcore` 7 × ~26 min); spending is F2P. Deterministic: same day,
  profile, seed, player id and clock → the same state (the response returns the seed). Typical results at day 60: casual
  main level ~44, active ~60, hardcore ~72.
- **Cost:** one CPU, measured on an M2 Pro (single thread, warm JIT): day 60 casual ~0.5 s, active ~1.3–2 s, hardcore
  ~1.6–2 s (90k commands); expect roughly 2× on a Cloud Run vCPU plus JIT on a cold instance. The client waits up to
  120 s. Sim CLI benchmark: `dotnet run --project tools/RogueStars.Sim -c Release -- --age-jump --days 60`.

### Jobs and health
| Method | Path | Notes |
|---|---|---|
| POST | `/internal/jobs/daily-reset` | delivers queued mail (daily resets themselves are lazy in the rules settlers) |
| POST | `/internal/jobs/weekly-arena?week=` | see Arena |
| POST | `/internal/jobs/invasion-start` | materialises active calendar invasion occurrences (`{ created[], active[] }`); every 5 min |
| POST | `/internal/jobs/invasion-resolve` | see Invasions |
| GET | `/health` (public) and `/healthz` | `{ status, version, auth, store, ads, iap, iapProducts, devTools }` (`devTools` = number of allowed players or `all`, never the ids), anonymous. Cloud Run's front end reserves paths ending in `z`, so `/healthz` is a 404 from outside; Terraform probes and smoke tests use `/health` |

## Storage
- `IPlayerStore`: `InMemoryPlayerStore` / `FirestorePlayerStore` — `players/{uid}` = `{ state: json, requests: json,
  version, updatedAt }`. A JSON field above 256 KiB is stored gzip-compressed instead (`stateGz` / `requestsGz`, bytes);
  if the document would still exceed ~1,000,000 bytes the oldest request payloads are dropped. Measured with the
  simulator (2-year Hardcore Whale, the largest profile, plus a real 64-request window of run steps): state JSON
  **313 KB**, request window 52 KB (largest single request 4.1 KB → worst-case window 263 KB), **stored 77.5 KB**
  after compression. Uncompressed the document would be 0.37–0.58 MB, i.e. above half the 1 MiB (1,048,576 bytes) limit
  with little headroom for content growth; compressed it is ~7 % of it.
- `IDocStore` (cross-player): `InMemoryDocStore` / `FirestoreDocStore` — `{ json, version, updatedAt }` per document,
  `MutateAsync` read-modify-write loops with jittered backoff. Collections are listed in `Storage/Collections.cs`;
  ids are designed so no composite indexes are needed. `Store:CollectionPrefix` separates environments.
- Compare-and-set on Firestore is optimistic (`FirestoreCas`): read the document, compare `version`, write with an exact
  `update_time` precondition (create = must not exist). A lost race returns false and the caller retries on fresh data.
  Pessimistic transactions were replaced because hot documents hit lock timeouts under contention (found by the emulator
  tests).

### Firestore emulator tests
`server/RogueStars.Server.FirestoreTests` (trait `Category=Firestore`) runs against the local Firestore emulator:
CRUD and compare-and-set of both stores, 12 concurrent writers on one player (exactly one wins per version), 16 concurrent
`MutateAsync` writers (no lost update), id-prefix listing, first-writer-wins claims, the 2-year state (size above, round
trip), a 3 MB synthetic state (compression), and a full server flow on Firestore (session, idempotent replay,
`expectedVersion`, concurrent commands, IAP and ad claims, arena fight + weekly job, guild create/join/chat, invasion
attack + guild contribution + resolve job, daily reset). They are **skipped** unless `FIRESTORE_EMULATOR_HOST` is set, so
`dotnet test RogueStars.slnx` never needs the emulator. Run them (Node + Java 21+, project id `demo-roguestars`, which
never reaches a real project):
```bash
scripts/firestore-tests.sh          # emulator suite + the whole server unit suite on Firestore (ROGUESTARS_TEST_STORE=firestore)
scripts/firestore-tests.sh --quick  # emulator suite only
# equivalent: npx firebase-tools@14 emulators:exec --only firestore --project demo-roguestars \
#   --config scripts/firebase.emulator.json "dotnet test server/RogueStars.Server.FirestoreTests --filter Category=Firestore"
```
CI: a separate step after the unit tests (Node + `actions/setup-java`). The first run simulates 730 days (~7 min); the
state is cached in `$TMPDIR/roguestars-sim/` per rules build.

## Configuration (environment variables)
Section `RogueStars`, env syntax `RogueStars__<Group>__<Key>`:

| Variable | Default (Production) | Development |
|---|---|---|
| `RogueStars__Auth__Mode` | `firebase` | `dev` |
| `RogueStars__Auth__FirebaseProjectId` | – (required) | – |
| `RogueStars__Store__Mode` | `firestore` | `memory` |
| `RogueStars__Store__FirestoreProjectId` / `FirestoreDatabase` / `CollectionPrefix` | – / `(default)` / "" | |
| `RogueStars__Jobs__Mode` | `oidc` | `secret` (`local-job-secret`) |
| `RogueStars__Jobs__Audience` / `ServiceAccountEmail` | – (required for oidc) | |
| `RogueStars__Iap__Mode` / `PackageName` / `ServiceAccountJson` | `disabled` (payments on hold; `google` at monetization go-live) / `com.oddbyte.roguestars` | `fake` |
| `RogueStars__Iap__ProductSet` | `initial` (only `PlayCatalog.InitialProductIds`, `docs/play-console-products-initial.csv`) | `all` |
| `RogueStars__Iap__AllowTestPurchases` / `RequireAccountId` | `true` / `false` (Terraform: variable / `true`) | |
| `RogueStars__Ads__Mode` / `EventKey` | `disabled` (`applovin` once AppLovin exists) | `fake` |
| `RogueStars__Ads__Verification` / `AllowedAdUnitIds__<n>` / `CreditTtlHours` | `event_token` / any / 24 | |
| `RogueStars__Commands__*` | hook names above, deny/allow lists | |
| `RogueStars__Arena__*`, `RogueStars__Guilds__*`, `RogueStars__Invasions__*` | tuning (see `ServerOptions.cs`) | |
| `RogueStars__DevTools__AllowedPlayerIds__<n>` | – (nobody; Terraform `dev_tools_player_ids`) | |
| `RogueStars__DevTools__AllowAll` | `false` (startup fails if `true` in Production) | `true` |
| `PORT` | 8080 (Cloud Run) | |

Logs: outside Development, one JSON object per line with `severity`/`message` (Cloud Logging format) plus the
structured-logging fields.

## Run locally
```bash
cd server/RogueStars.Server
DOTNET_CLI_TELEMETRY_OPTOUT=1 arch -arm64 ~/.dotnet/dotnet run     # Development: dev auth + in-memory stores on :8080

TOKEN=$(curl -s -XPOST localhost:8080/v1/auth/dev -H 'content-type: application/json' -d '{"deviceId":"me"}' | jq -r .token)
curl -s -XPOST localhost:8080/v1/session -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' -d '{"displayName":"Me"}'
curl -s -XPOST localhost:8080/v1/command -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' \
  -d '{"type":"<CommandName>","requestId":"1","args":{}}'
curl -s -XPOST localhost:8080/internal/jobs/daily-reset -H 'X-Job-Secret: local-job-secret'
```
Tests: `dotnet test RogueStars.slnx` from the repo root (Firestore emulator tests: see Storage).

## Deployment
`server/Dockerfile` (build context = repo root) publishes rules + server and runs as the image's non-root user on
port 8080; `cloudbuild.yaml` runs the unit tests and builds/pushes it on Cloud Build. Owner runbook: `docs/setup.md`;
Terraform details: `infra/terraform/README.md`. Cloud Scheduler calls the four job endpoints with OIDC tokens of the
scheduler service account.

## Open wiring (after merge)
- `IBattleResolver` is `SimulatorBattleResolver`: arena = `BattleSimulator.PlayerVsSnapshot`, invasion attack =
  `BattleSimulator.PlayerVsBoss` against the content invasion's boss (`InvasionDef.BossId`, scaled by
  `invaderPower / 1000`, HP pool = the group's remaining HP); the score is `PlayerDamageDealt`. The battle log is
  returned as `log`.
- Hook commands above are the real rules commands (server tests exercise them end-to-end).
- Scale notes: weekly/invasion jobs process groups sequentially (fine for the prototype; fan out per group later);
  chat rate limit and ad/IAP dedup are document based and hold across instances.
- The Unity client signs in with Firebase Auth (Play Games, anonymous fallback); dev auth only against a Development server.
