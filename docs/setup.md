# Rogue Stars — deployment runbook (owner)

Step by step from the existing Google/Firebase project to a signed build on Play's internal testing track talking to
the deployed server. Commands assume macOS/Linux, the repository root as working directory, and these shell variables:

```bash
export PROJECT_ID=rogue-stars-515610        # production GCP + Firebase project (a dev project may follow: own tfvars/env)
export REGION=europe-west1
export PACKAGE=com.oddbyte.roguestars       # Android package (client ProjectSetup.PackageName, Terraform play_package_name)
```

Before anything touches the cloud, run the offline checks (config/secret presence, `terraform validate`, all tests):
`scripts/preflight.sh --tfvars infra/terraform/prod.tfvars` (add `--emulator` for the Firestore emulator suite).
After cloning (and in every new worktree) fetch Google's Unity packages once: `scripts/fetch-unity-packages.sh`.

**Current scope:** no AppLovin (ads are globally **disabled**: every player is treated as owning No Ads, see
docs/server.md → *Ads disabled*), only the **initial IAP set** (`docs/play-console-products-initial.csv`, 4 products) is
sold, players sign in with **Firebase Auth** (Google Play Games, anonymous fallback).

## What the owner must provide vs. what is automated

| Item | Owner provides (manual, once) | Already automated in the repo |
|---|---|---|
| GCP project, billing account | project `rogue-stars-515610` exists; link billing, `gcloud auth` | – |
| Terraform state | GCS bucket for remote state | backend config, `terraform validate` in preflight |
| APIs, Artifact Registry, Firestore, service accounts, IAM, Secret Manager secrets (empty), Cloud Run, Cloud Scheduler jobs | run `terraform apply` (steps 6–8) | all of it in `infra/terraform` (`ads_mode = "disabled"`, `iap_product_set = "initial"`) |
| Server image | run `gcloud builds submit` | `cloudbuild.yaml` (tests + Docker build + push), `.gcloudignore` |
| Firebase Auth | SHA fingerprints, enable Play Games + Anonymous providers (step 3) | server verifies Firebase ID tokens of `rogue-stars-515610` (`appsettings.Production.json`, Terraform); dev auth refused in Production |
| Play Games sign-in | Play Games Services project, credentials, web client id/secret, testers (step 3) | Firebase Unity SDK 14.2.0 (Auth) + Google Play Games plugin 2.3.0, Play Games → Firebase credential, anonymous fallback + linking |
| Play Console app, products, testers, internal track | create app, upload the first AAB, create the **4 initial products**, license testers | product CSVs generated from the rules, Unity IAP integration, server receipt validation + consume/acknowledge, BILLING permission checked in every build |
| Play Developer API access | invite the Cloud Run identity (or a key) in Play Console | Secret Manager secret + Cloud Run mount (`secrets_populated`) |
| Release signing | create + safeguard the upload keystore (step 12) | `BuildAndroid` signs AAB + APK from `ROGUESTARS_KEYSTORE*` env vars |
| Client build values | server URL, Play Games app id + web client id as env vars at build time | `BuildSecrets` injects them for the build and restores the committed defaults |
| AppLovin MAX | **later** (step 5) — nothing now | plugin stays in the repo, never initialized while ads are disabled; S2S verification + ad credits ready (`ads_mode = "applovin"`) |

## 1. GCP project and billing
1. The project `rogue-stars-515610` exists (created with Firebase). Billing → link a billing account (Cloud Run, Firestore,
   Scheduler, Cloud Build need it; scale-to-zero keeps idle cost ~0).
2. Locally: `gcloud auth login && gcloud auth application-default login && gcloud config set project $PROJECT_ID`.
3. Grant yourself (or the CI principal) `roles/owner` or at least Editor + Security Admin + Secret Manager Admin + Service
   Account Admin for the Terraform run.

## 2. Firebase project and Android app
1. Firebase console → project `rogue-stars-515610` → Project settings → *Your apps*: the Android app `$PACKAGE` exists.
   Its `google-services.json` belongs at `client/RogueStars/Assets/google-services.json` (git-ignored; not a secret but
   environment specific — a dev project gets its own file). The Firebase editor tooling turns it into Android resources
   (`Assets/Plugins/Android/FirebaseApp.androidlib`, git-ignored) at build time.
2. The current file has an **empty `oauth_client`**: no SHA certificate fingerprints are registered yet, which Play Games
   sign-in needs. They are added in step 3 once the signing keys exist; then download the file again.

## 3. Sign-in: Firebase Auth + Google Play Games (console clicks, in this order)
The client signs in with Play Games (v2: `PlayGamesPlatform.Instance.Authenticate` → `RequestServerSideAccess` → server
auth code → `PlayGamesAuthProvider.GetCredential(code)` → `SignInWithCredentialAsync`); without Play Games it signs in
anonymously and links the anonymous account to Play Games later (same uid = same player). The server only sees the
Firebase ID token (player id = Firebase uid).

1. **Upload key + first upload** (steps 12 and 4.2): create the upload keystore, build a signed AAB, upload it to
   *Internal testing*. This enrols **Play App Signing** (Google creates the app signing key).
2. **Fingerprints**: Play Console → Test and release → *App integrity* → App signing: copy **SHA-1 and SHA-256** of the
   **App signing key certificate** and of the **Upload key certificate**. (Upload key locally:
   `keytool -list -v -keystore ~/secure/rogue-stars-upload.jks -alias upload`; local debug-signed builds:
   `keytool -list -v -keystore ~/.android/debug.keystore -alias androiddebugkey -storepass android`.)
3. Firebase console → Project settings → Your apps → `$PACKAGE` → *Add fingerprint*: add all four (app signing SHA-1/
   SHA-256, upload SHA-1/SHA-256; optionally the debug SHA-1). **Download `google-services.json` again** (now with
   `oauth_client` entries) and replace `client/RogueStars/Assets/google-services.json`.
4. Google Cloud console (project `rogue-stars-515610`) → APIs & Services → *OAuth consent screen*: user type External, app
   name, support e-mail; while in *Testing*, add the tester accounts as test users.
5. Play Console → Grow users → **Play Games Services** → Setup and management → *Configuration* → "Yes, my game already
   uses Google APIs" → select the Cloud project `rogue-stars-515610`. Then *Credentials*:
   - **Android** credential: package `$PACKAGE`, certificate fingerprint = **App signing key SHA-1** (installs from Play).
     Add a second Android credential with the **upload key SHA-1** (and one with the debug SHA-1) to sign in from
     locally built/sideloaded APKs.
   - **Game server** credential: OAuth client of type *Web application* — pick the existing
     "Web client (auto created by Google Service)" that Firebase created, or create one. Note its **client ID** and
     **client secret** (Cloud console → Credentials → that client).
   - Copy the numeric **Play Games project id** (the `APP_ID`, shown on the Configuration page / in "Get resources").
   - *Testers*: add the testers' Google accounts (needed while Play Games Services is unpublished); publish later.
6. Firebase console → Authentication → Sign-in method: enable **Play Games** and paste the **web client ID + secret**
   from 5; enable **Anonymous** (fallback, testing, smoke tests).
7. Unity configuration (not secret, per environment): the **web client ID** and the **Play Games APP_ID** go into the
   Play Games plugin settings — either per build as `ROGUESTARS_PLAY_GAMES_WEB_CLIENT_ID` /
   `ROGUESTARS_PLAY_GAMES_APP_ID` (step 11; `BuildSecrets` writes `Assets/GooglePlayGames/Resources/PlayGamesSettings.asset`
   and `Assets/Plugins/Android/GooglePlayGamesManifest.androidlib` for the build and removes them afterwards) or
   permanently through Unity → *Window → Google Play Games → Setup → Android setup* (paste the resources XML from Play
   Console and the web client ID; commit the two generated files). Without them the build signs in anonymously only
   and never touches the Play Games SDK.

## 4. Google Play Console
1. https://play.google.com/console → *Create app* (name, default language, game, free with in-app purchases),
   package `$PACKAGE` is fixed by the first upload.
2. **Upload a first build** to *Internal testing*: the signed AAB from step 11/12. Every build declares
   `com.android.vending.BILLING` (checked by `BuildAndroid`), which Play needs before products can be created.
3. **Products — initial set**: Monetize with Play → Products → *One-time products*. Create one product per row of
   **`docs/play-console-products-initial.csv`** (4 rows: `rs.diamonds_60` consumable 0.99, `rs.starter_worm`
   non-consumable 0.99, `rs.perm_daily_diamonds` non-consumable 9.99, `rs.monthly_pass` consumable 4.99):
   product id = `product_id`; name = `title`; description; one purchase option, *Buy*, price from `price_usd` with automatic
   local prices. Consumable vs non-consumable is a client/server concept (consumables are consumed by the server after the
   grant, non-consumables acknowledged) — in Play just leave *multi-quantity* **off** (the server refuses quantity > 1).
   The monthly pass is a one-time 30-day product, **not** a subscription. Activate every product.
   Server (`Iap:ProductSet=initial`, Terraform `iap_product_set`) and client (`PlatformConfig.ProductSet`) only sell these;
   the shop shows the others as "Coming soon".
4. **Later — full catalogue**: create the remaining rows of `docs/play-console-products.csv`, then switch
   `iap_product_set = "all"` (Terraform) and build the client with `ROGUESTARS_PRODUCT_SET=all`. (Both CSVs are generated
   from the shop content by `UPDATE_PLAY_CATALOG=1 dotnet test --filter PlayConsole`; a test fails when they are stale.)
5. **License testers**: Settings (all apps) → License testing → add the testers' Google accounts, response
   `RESPOND_NORMALLY`. Their purchases carry `testPurchaseContext` and are accepted while `iap_allow_test_purchases = true`.
6. **Internal testing track**: Test and release → Testing → Internal testing → create the tester list (same accounts),
   copy the opt-in link to the testers.
7. **Android Publisher API access** (server receipt validation, consume, acknowledge):
   - The API is enabled by Terraform (`androidpublisher.googleapis.com`).
   - Option A (recommended, no key file): Play Console → Users and permissions → *Invite new users* →
     `rogue-stars-server@$PROJECT_ID.iam.gserviceaccount.com` (Terraform output `server_service_account`) with app
     permissions **View financial data** and **Manage orders and subscriptions** for this app. Leave the
     `rogue-stars-play-publisher-credentials` secret empty; the server uses its runtime identity (ADC).
   - Option B (key file): create a service account, invite it the same way, create a JSON key and store it in Secret
     Manager (step 8, `play_credentials_in_secret = true`). Keep the key file out of the repository.
   - Permissions can take up to 24 h to apply to the API.

## 5. AppLovin MAX (later — skip for now)
Ads are disabled globally (`ads_mode = "disabled"`, client `PlatformConfig.AdsMode = disabled`): no ad SDK is initialized,
no AppLovin key is needed, every placement completes instantly. When an AppLovin account exists:
1. https://dash.applovin.com → add the Android app (`$PACKAGE`); **SDK key** (Account → Keys) → client build only
   (`ROGUESTARS_MAX_SDK_KEY`); **Event Key** → Secret Manager (step 8).
2. Rewarded ad unit (MAX → Ad Units → Android, *Rewarded*) → `ROGUESTARS_MAX_REWARDED_AD_UNIT` (and optionally Terraform
   `ads_allowed_ad_unit_ids`).
3. S2S callback on the ad unit (*Server Side Callback URL*):
   ```
   https://<service_url>/v1/ads/ssv?user_id={USER_ID}&event_id={EVENT_ID}&custom_data={CUSTOM_DATA}&placement={PLACEMENT}&ad_unit_id={AD_UNIT_ID}&event_token={EVENT_TOKEN}&event_token_all={EVENT_TOKEN_ALL}
   ```
4. Switch on: Terraform `ads_mode = "applovin"` + `secrets_populated = true` (mounts the Event Key), client build with
   `ROGUESTARS_ADS=max`. Mediation adapters via the AppLovin Integration Manager; publish app-ads.txt.
   Verification details: docs/server.md → *AppLovin MAX S2S rewarded callbacks*.

## 6. Terraform remote state
```bash
gcloud storage buckets create gs://$PROJECT_ID-tfstate --location=$REGION --uniform-bucket-level-access
gcloud storage buckets update gs://$PROJECT_ID-tfstate --versioning
cd infra/terraform
cp example.tfvars prod.tfvars        # *.tfvars except example.tfvars are git-ignored
# prod.tfvars: project_id = "rogue-stars-515610", image_tag (step 7), secrets_populated = false,
#              ads_mode = "disabled", iap_product_set = "initial"
terraform init -backend-config="bucket=$PROJECT_ID-tfstate" -backend-config="prefix=rogue-stars/prod"
```

## 7. First apply (no image yet) and image build on Cloud Build
Local Docker is not needed: Cloud Build runs the unit tests and builds `server/Dockerfile`.
```bash
# APIs, registry and the build service account first:
terraform apply -var-file=prod.tfvars \
  -target=google_project_service.apis -target=google_artifact_registry_repository.server \
  -target=google_service_account.build -target=google_artifact_registry_repository_iam_member.build_push \
  -target=google_project_iam_member.build_logging -target=google_project_iam_member.build_source
cd ../..
TAG=$(git rev-parse --short HEAD)
gcloud builds submit --config cloudbuild.yaml \
  --service-account "$(terraform -chdir=infra/terraform output -raw build_service_account)" \
  --substitutions _REGION=$REGION,_TAG=$TAG .
```
(If the organisation policy blocks user-specified build service accounts, omit `--service-account` and grant the default
Cloud Build service account `roles/artifactregistry.writer` on the repository instead.)

## 8. Full apply, then secrets
```bash
cd infra/terraform
terraform plan  -var-file=prod.tfvars -var image_tag=$TAG -out=plan.out    # secrets_populated = false
terraform apply plan.out
terraform output service_url
```
With ads disabled and Play option A there is **no secret to add**. Only for Play option B (key file):
```bash
gcloud secrets versions add rogue-stars-play-publisher-credentials --data-file=play-service-account.json
```
then `secrets_populated = true` and `play_credentials_in_secret = true` in `prod.tfvars` and apply again. (The AppLovin
Event Key secret is only mounted with `ads_mode = "applovin"`.)

Later deployments: `gcloud builds submit …` with a new `_TAG`, then `terraform apply -var-file=prod.tfvars -var image_tag=<tag>`.

## 9. Cloud Scheduler
Created by Terraform (UTC): `daily-reset` 00:05, `weekly-arena` Monday 00:15, `invasion-start` every 5 min,
`invasion-resolve` every 15 min, each with an OIDC token of `rogue-stars-scheduler@…` and audience `jobs_audience`.
Run one by hand to verify: `gcloud scheduler jobs run rogue-stars-daily-reset --location=$REGION` and check Cloud Run logs
for `POST /internal/jobs/daily-reset` with status 200.

## 10. Smoke tests against the deployed URL
```bash
URL=$(terraform -chdir=infra/terraform output -raw service_url)
curl -s $URL/healthz        # {"status":"ok",...,"auth":"firebase","store":"firestore","ads":"disabled","iapProducts":"initial"}
curl -s -o /dev/null -w '%{http_code}\n' -XPOST $URL/v1/auth/dev                                              # 404 (no dev auth)
# Player token: Firebase anonymous sign-up (Web API key: Firebase project settings → General)
TOKEN=$(curl -s "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=$FIREBASE_WEB_API_KEY" \
  -H 'content-type: application/json' -d '{"returnSecureToken":true}' | jq -r .idToken)
curl -s -XPOST $URL/v1/session -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' -d '{"displayName":"Smoke"}' | jq '{ok,playerId,version,adsDisabled}'
curl -s $URL/v1/invasions -H "authorization: Bearer $TOKEN" | jq '.invasions | length'
curl -s $URL/v1/arena/leaderboard -H "authorization: Bearer $TOKEN" | jq .ok
curl -s $URL/v1/iap/config -H "authorization: Bearer $TOKEN" | jq '.products | length'   # 4 (initial set)
# Ads disabled: a direct-reward placement is granted without an ad (daily cap applies)
curl -s -XPOST $URL/v1/ads/instant -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' \
  -d '{"placementId":"daily_diamonds_ad","requestId":"smoke-1"}' | jq '{ok,error}'                      # {"ok":true,...}
curl -s -o /dev/null -w '%{http_code}\n' -XPOST $URL/internal/jobs/daily-reset                                # 401 (OIDC only)
```
IAP end to end: install the internal-testing build as a license tester, buy `60 Diamonds` with a test card; Cloud Run
logs show `Purchase diamonds_60 order GPA.… : True (test True)`; Play Console → Order management shows it consumed.
Sign-in end to end: on a tester device the first start shows the Play Games sign-in banner; Settings → Account shows
"Google Play Games" (a device without Play Games shows "Guest (anonymous)" and a *Link Play Games* button).

## 11. Client build pointing at the server
Unity 6.6 batchmode (docs/client.md). Build-time values are environment variables; `BuildSecrets` writes them into the
assets for the build and restores the committed defaults afterwards:
```bash
scripts/fetch-unity-packages.sh                         # once per checkout (Firebase/EDM packages)
cp /secure/place/google-services.json client/RogueStars/Assets/   # the re-downloaded file from step 3.3
export ROGUESTARS_SERVER_URL=$URL                       # online by default, Firebase auth (ROGUESTARS_AUTH=dev to override)
export ROGUESTARS_PLAY_GAMES_APP_ID=<numeric Play Games project id> ROGUESTARS_PLAY_GAMES_WEB_CLIENT_ID=<…apps.googleusercontent.com>
export ROGUESTARS_KEYSTORE=$HOME/secure/rogue-stars-upload.jks ROGUESTARS_KEY_ALIAS=upload
read -s ROGUESTARS_KEYSTORE_PASS && export ROGUESTARS_KEYSTORE_PASS
# defaults: ROGUESTARS_STORE=unity_iap, ROGUESTARS_ADS=disabled, ROGUESTARS_PRODUCT_SET=initial, ROGUESTARS_OUTPUT=both
U=/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity
arch -arm64 $U -batchmode -nographics -quit -buildTarget Android -projectPath $PWD/client/RogueStars \
  -logFile /tmp/rs-build.log -executeMethod RogueStars.Client.Editor.BuildAndroid.Build
grep '\[BuildAndroid\]' /tmp/rs-build.log    # OK Builds/RogueStars.aab (…, auth firebase, playGames True, signed True, billing permission yes) …
```
Output: `client/RogueStars/Builds/RogueStars.aab` (upload to Play) and `client/RogueStars/Builds/RogueStars.apk`
(sideload: `adb install -r …apk`). Gradle downloads Firebase Auth, Play Games v2 and Play Billing during the build
(network required). In the editor, tests and offline play the fake store/ads and dev auth are always used.

## 12. Release signing (upload key + Play App Signing)
1. Create the upload keystore once (keep it **outside** the repository; `*.jks`/`*.keystore` are git-ignored anyway):
   ```bash
   mkdir -p ~/secure && keytool -genkeypair -v -keystore ~/secure/rogue-stars-upload.jks -alias upload \
     -keyalg RSA -keysize 4096 -validity 10000 -dname "CN=Oddbyte, O=Oddbyte"
   ```
2. Store the keystore file and both passwords in a password manager / secret store with an offline backup. Losing it
   means requesting an upload-key reset from Google.
3. Play App Signing is the default for new apps: on the first AAB upload Play generates and keeps the **app signing key**;
   your keystore is only the **upload key**. Register both certificates' SHA-1/SHA-256 in Firebase and the Play Games
   Android credentials (step 3).
4. Builds without `ROGUESTARS_KEYSTORE` are debug-signed with `~/.android/debug.keystore` (fine for local APKs, refused by
   Play).

## 13. Go-live checklist
- `scripts/preflight.sh --tfvars infra/terraform/prod.tfvars --emulator` is green.
- `/healthz` reports `auth: firebase`, `store: firestore`, `ads: disabled`, `iapProducts: initial`; jobs ran.
- A tester signed in with Play Games (Settings → Account "Google Play Games"); a guest account was linked.
- A license-tester purchase of each initial product was granted (consumables consumed, non-consumables acknowledged).
- Decide `iap_allow_test_purchases` for production; switch `iap_product_set`/`ROGUESTARS_PRODUCT_SET` to `all` once every
  product exists in Play Console; Play Console *Data safety*: no advertising id while ads are disabled (the manifest
  removes `AD_ID`), Firebase Auth collects user ids.
