# Rogue Stars

Raise a tiny worm into a mech-enhanced god-worm who leads an alien civilization. Dice-board roguelike
runs, auto-battles, a city of blueprint buildings staffed by breedable aliens, invasions and planetary events.

- Design spec: [docs/design/GDD.md](docs/design/GDD.md)
- Engineering conventions: [docs/ENGINEERING.md](docs/ENGINEERING.md)
- Server: [docs/server.md](docs/server.md) · Unity client: [docs/client.md](docs/client.md) ·
  Balance report: [docs/balance/progression-report.md](docs/balance/progression-report.md)

```
shared/   game rules (C# 9, netstandard2.1, shared by client and server) + tests
server/   authoritative ASP.NET Core (.NET 10) server + tests
client/   Unity 6.6 Android client (placeholder art)
tools/    meta-progression simulator (bots play the real rules) + guard test
infra/    Terraform (GCP)
```

## Getting started

Requirements: .NET 10 SDK, Unity 6000.6.10f1 with Android support, Python 3 (only for the balance charts).
On Apple Silicon with a Rosetta shell use the native SDK: `arch -arm64 ~/.dotnet/dotnet …` (below: `dotnet`).

**Build and test the rules and the server**
```bash
dotnet build RogueStars.slnx
dotnet test RogueStars.slnx          # rules, server (in-process WebApplicationFactory) and simulator guard tests
```

**Run the server locally (dev auth, in-memory storage)**
```bash
cd server/RogueStars.Server && dotnet run          # http://localhost:8080, ASPNETCORE_ENVIRONMENT=Development
TOKEN=$(curl -s -XPOST localhost:8080/v1/auth/dev -H 'content-type: application/json' -d '{"deviceId":"me"}' | jq -r .token)
curl -s -XPOST localhost:8080/v1/session -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' -d '{"displayName":"Me"}'
curl -s -XPOST localhost:8080/internal/jobs/invasion-start -H 'X-Job-Secret: local-job-secret'   # materialise live invasions
```
Dev auth is refused in Production; see docs/server.md for Firebase auth, Firestore, IAP and the scheduled jobs.

**Open, play and build the Unity client**
- Once per checkout: `scripts/fetch-unity-packages.sh` (Firebase App/Auth + EDM from Google's archive, git-ignored).
- Open `client/RogueStars` in Unity 6.6, open `Assets/Scenes/Main.unity`, press Play (portrait Game view, e.g. 1080×1920).
  It starts **offline** (local rules engine, save in `persistentDataPath`); Settings has dev cheats.
- **Online:** start the server, then Settings → server URL `http://localhost:8080` (editor) or `http://10.0.2.2:8080`
  (Android emulator) → *Online*.
- Batchmode (absolute paths, one Unity instance per project):
```bash
U=/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity; P=$PWD/client/RogueStars
$U -batchmode -nographics -projectPath $P -logFile $PWD/rs-tests.log -runTests -testPlatform EditMode -testResults $PWD/rs-tests.xml
$U -batchmode -projectPath $P -logFile $PWD/rs-play.log -runTests -testPlatform PlayMode -testResults $PWD/rs-play.xml   # playthrough + screenshots
$U -batchmode -nographics -quit -buildTarget Android -projectPath $P -logFile $PWD/rs-build.log -executeMethod RogueStars.Client.Editor.BuildAndroid.Build
```
  The AAB (Play) and APK land in `client/RogueStars/Builds/` (IL2CPP, ARM64, upload-key signed via `ROGUESTARS_KEYSTORE*`,
  else debug-signed). Prefix with `arch -arm64` on Apple Silicon. Release configuration: docs/setup.md step 11.

**Run the progression simulator**
```bash
dotnet run --project tools/RogueStars.Sim -c Release -- --days 365 --seeds 3 --profiles Active:F2P,Casual:F2P,Active:Whale --out /tmp/sim
dotnet run --project tools/RogueStars.Sim -c Release -- --quiet          # all 9 profiles × 3 seeds × 730 days → docs/balance (~9 min)
python3 tools/RogueStars.Sim/charts.py docs/balance before                # charts (needs matplotlib in a venv)
```

## Status

Playable prototype (placeholder UI), offline and online.

- **Rules:** run board, battles, worm/parts/runes/heroes/mounts, city/aliens/breeding, laboratory, gacha, quests, shop,
  mail, arena, guilds, live ops (weekly + surprise invasions, never two of one type at once, max 2 concurrent;
  planetary events). Endless procedural campaign and Dark Side (50 named portals, then generated) tuned with the simulator.
- **Server:** authoritative command endpoint, idempotency, compare-and-set saves, arena, guilds, invasions (instances
  = rules live-ops occurrences), jobs, Google Play receipt validation (productsv2, server consume/acknowledge; initial 4-product set), global
  ads-disabled mode (AppLovin MAX S2S verification ready for later), Firestore (emulator-tested), Cloud Run + Cloud Build + Terraform; owner runbook docs/setup.md.
- **Client:** every screen, battle playback from the logged HP/shield/rage, offline + online services, playthrough
  screenshots in [docs/client-screenshots](docs/client-screenshots), Firebase Auth (Google Play Games sign-in, anonymous fallback +
  linking), Unity IAP (Google Play Billing), AppLovin MAX gateway (disabled for now), signed AAB + APK builds.
- **Tests:** .NET 686 (rules 567, server 91, simulator guard 28) + 9 Firestore emulator tests (scripts/firestore-tests.sh),
  Unity EditMode 262 + PlayMode playthrough 1 (+ explicit screenshot showcases); APK ≈ 30 MB.
- **Open:** console setup for Play Games sign-in (docs/setup.md step 3), AppLovin + mediation networks, push notifications, real art;
  balance follow-ups in the report's "Remaining recommendations" (late-game ceilings for spenders, Dark Side length,
  part drops at the portal 4–6 frontier).
