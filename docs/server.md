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
- **Concurrency:** saves are compare-and-set on the version. On conflict the server reloads and re-executes (up to
  8 times). If the client pins `expectedVersion` and it differs, the response is `409 version_conflict` with the
  current version in `details`.
- **Serialization:** storage uses C# member names (`StateJson.Options`), the wire uses camelCase
  (`StateJson.Wire`). Dictionary keys (currency ids, counters) are never re-cased. The engine's `Cloner` is a JSON
  round trip, so a failed command never changes state.

### Hook commands (resolved by name at runtime)
The server issues these itself. Their classes belong to the rules workstreams; the server passes the listed fields
(extra fields are ignored by the deserializer). Names are configurable under `RogueStars:Commands:*`.

| Config key | Default name | Issued by | Args | When missing |
|---|---|---|---|---|
| `PurchaseComplete` | `PurchaseComplete` | `/v1/iap/verify` | `ProductId, OrderId, PurchaseToken, Store, Test` | `501` |
| `AdWatched` | `AdWatched` | `/v1/ads/ssv` | `Placement, EventId, AdUnitId` | `501` |
| `InvasionOutcome` | `LiveOpsApplyOutcome` | invasion-resolve job | `InvasionId, Outcome ("repelled"/"overrun"), Damage, Rank, GroupSize, GroupDamage, GroupHp` | invasion stays pending |
| `InvasionAttack` | `InvasionAttackRecorded` | `/v1/invasions/{id}/attack` | `InvasionId, Damage, Seed, AttackId` — consume an attempt; a failure rejects the attack | attacks are free |
| `ArenaFight` | `ArenaFightResolved` | `/v1/arena/fight` | `OpponentId, Won, PointsDelta, Seed, Week, FightId` — consume a ticket, grant fight rewards; a failure rejects the fight | fights are free |
| `MailDeliver` | `MailDeliver` | arena weekly rewards, invasion top rewards | `Title, Body, Source, Rewards (Reward[])` | queued in `mail_outbox`, delivered by `daily-reset` |
| `GuildMembership` | `GuildMembershipChanged` | guild create/join/leave/kick/promote | `GuildId (null when left), Role` | skipped |

Every hook call carries a deterministic request id (e.g. `arena-2026-W47-<player>`, `inv-outcome-<invasion>`,
`iap-<orderId>`), so retried jobs and callbacks never apply twice.

## Auth
| Mode | Config | Behaviour |
|---|---|---|
| `firebase` (default) | `RogueStars:Auth:FirebaseProjectId` | `Authorization: Bearer <Firebase ID token>`. RS256 against Google's securetoken certs (cached by max-age); `iss = https://securetoken.google.com/<project>`, `aud = <project>`, `exp/iat/auth_time` (5 min skew), non-empty `sub`. Player id = Firebase uid. |
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
| POST | `/v1/session` | `{ displayName? }` | `{ ok, playerId, created, serverTimeMs, version, state, commands[] }` — creates the player with `engine.NewPlayer` and a server-chosen seed, else loads; `state` is settled; `commands` lists client-allowed command names |
| GET | `/v1/state` | – | `{ ok, serverTimeMs, version, state }` (settled, not persisted) |
| POST | `/v1/command` | `{ type, requestId?, args?, expectedVersion? }` | `{ ok, error, events[], payload, version, replayed, state }` |

Example:
```json
POST /v1/command
{ "type": "GachaPull", "requestId": "c3f1…", "args": { "bannerId": "parts_std", "count": 10 } }
→ { "ok": true, "error": null, "events": [{ "type": "pulled", "args": ["…"] }], "payload": {…}, "version": 42, "replayed": false, "state": {…} }
```

### Payments
| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/v1/iap/verify` | `{ productId, purchaseToken }` | command response of `PurchaseComplete`; `{ ok, alreadyCompleted, orderId }` when re-sent; `402` invalid receipt; `409 receipt_already_used` (token claimed by another player); `501` if `PurchaseComplete` is not registered |
| GET/POST | `/v1/ads/ssv?user_id=&event_id=&custom_data=&ad_unit_id=&event_token=` | – | `{ ok, error }`; `{ ok, duplicate: true }` for a repeated event; `401 invalid_signature` |

IAP validation (`google`): Android Publisher `purchases.products.get` with the Play service account JSON
(`RogueStars:Iap:ServiceAccountJson`, from Secret Manager) or ADC; `purchaseState == 0` required. The client
consumes/acknowledges via the Billing Library after the server confirms. `fake`: tokens starting with `fake-` are
valid.

Ads (`applovin`): `event_token` must equal `hex(sha256(event_id + EventKey))` — the parameter name
(`Ads:HashParameter`) and algorithm (`Ads:HashAlgorithm`, sha256/sha1) are configurable; **verify both against the
current AppLovin MAX S2S documentation before launch**. Configure the MAX callback URL with `user_id={USER_ID}`
(= Firebase uid), `custom_data` = placement.

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
  the server via `IBattleResolver`, then runs the `ArenaFight` hook and updates points (win +30, loss −10, defender −5
  on loss; floor 0).
- `weekly-arena` job (`?week=` optional, default previous week): ranks every group, mails rank rewards
  (`Arena:RankRewards`, scaled ×(1+0.5·league)), promotes the top `PromoteTop` (15) / demotes the bottom
  `DemoteBottom` (15). Resumable and idempotent.

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
| GET | `/v1/invasions` | – | `{ ok, invasions[] }` active now |
| POST | `/v1/invasions/{id}/join` | – | `{ ok, groupId, members }` — groups of **20** (`Invasions:GroupSize`) |
| POST | `/v1/invasions/{id}/attack` | `{ requestId? }` | `{ ok, attackId, damage, seed, log, groupDamage, groupHp, state }` (auto-joins) |
| GET | `/v1/invasions/{id}/group` | – | `{ ok, invasion, groupId, outcome, groupDamage, groupHp, members[] }` |
| POST | `/internal/invasions` | `{ id?, name, startsMs, endsMs, hpPerMember?, invaderPower? }` | job auth; creates an instance |

`invasion-resolve` job: for each ended, unresolved invasion and each group: outcome = `repelled` when group damage ≥
`hpPerMember × members`, else `overrun`; runs the outcome hook for every member (rank by damage), mails
`Invasions:TopRewards` to the top `TopContributors` (3) with damage > 0. Progress is stored per member, so the job
is resumable; the invasion is marked resolved only when every member got the outcome.

### Jobs and health
| Method | Path | Notes |
|---|---|---|
| POST | `/internal/jobs/daily-reset` | delivers queued mail (daily resets themselves are lazy in the rules settlers) |
| POST | `/internal/jobs/weekly-arena?week=` | see Arena |
| POST | `/internal/jobs/invasion-resolve` | see Invasions |
| GET | `/healthz` | `{ status, version, auth, store }`, anonymous |

## Storage
- `IPlayerStore`: `InMemoryPlayerStore` / `FirestorePlayerStore` — `players/{uid}` = `{ state: json, requests: json,
  version, updatedAt }`, compare-and-set in a Firestore transaction. State must stay below Firestore's 1 MiB doc limit.
- `IDocStore` (cross-player): `InMemoryDocStore` / `FirestoreDocStore` — `{ json, version, updatedAt }` per document,
  compare-and-set writes, `MutateAsync` read-modify-write loops. Collections are listed in `Storage/Collections.cs`;
  ids are designed so no composite indexes are needed. `Store:CollectionPrefix` separates environments.

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
| `RogueStars__Iap__Mode` / `PackageName` / `ServiceAccountJson` | `google` | `fake` |
| `RogueStars__Ads__Mode` / `EventKey` | `applovin` | `fake` |
| `RogueStars__Commands__*` | hook names above, deny/allow lists | |
| `RogueStars__Arena__*`, `RogueStars__Guilds__*`, `RogueStars__Invasions__*` | tuning (see `ServerOptions.cs`) | |
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
Tests: `dotnet test RogueStars.slnx` from the repo root.

## Deployment
`server/Dockerfile` (build context = repo root) publishes rules + server and runs as the image's non-root user on
port 8080. Infrastructure, image push and secrets: see `infra/terraform/README.md`. Cloud Scheduler calls the
three job endpoints with OIDC tokens of the scheduler service account.

## Open wiring (after merge)
- `IBattleResolver`: currently `PowerBattleResolver` (power comparison placeholder). Register a resolver backed by the
  Run workstream's `BattleSimulator` in `Program.cs`.
- Hook commands listed above must be implemented (with `[ServerOnly]`) by the owning workstreams, or their names
  configured to match.
- Scale notes: weekly/invasion jobs process groups sequentially (fine for the prototype; fan out per group later);
  chat rate limit and ad/IAP dedup are document based and hold across instances.
