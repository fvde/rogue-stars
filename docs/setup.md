# Rogue Stars — deployment runbook (owner)

Step by step from an empty Google account to a signed build on Play's internal testing track talking to the deployed
server. Commands assume macOS/Linux, the repository root as working directory, and these shell variables:

```bash
export PROJECT_ID=rogue-stars-prod          # globally unique GCP project id
export REGION=europe-west1
export PACKAGE=com.roguestars.game          # Android package (client ProjectSetup.PackageName, Terraform play_package_name)
```

Before anything touches the cloud, run the offline checks (config/secret presence, `terraform validate`, all tests):
`scripts/preflight.sh --tfvars infra/terraform/prod.tfvars` (add `--emulator` for the Firestore emulator suite).

## What the owner must provide vs. what is automated

| Item | Owner provides (manual, once) | Already automated in the repo |
|---|---|---|
| GCP project, billing account | create project, link billing, `gcloud auth` | – |
| Terraform state | GCS bucket for remote state | backend config, `terraform validate` in preflight |
| APIs, Artifact Registry, Firestore, service accounts, IAM, Secret Manager secrets (empty), Cloud Run, Cloud Scheduler jobs | run `terraform apply` (steps 6–8) | all of it in `infra/terraform` |
| Server image | run `gcloud builds submit` | `cloudbuild.yaml` (tests + Docker build + push), `.gcloudignore` |
| Firebase project + Auth providers | link Firebase, enable Play Games + Anonymous, Play Games OAuth client | server verifies Firebase ID tokens (`Auth:Mode=firebase`) |
| Firebase sign-in in the Unity client | **not integrated yet** (client uses dev auth) — needs Firebase Unity SDK + `google-services.json` | – |
| Play Console app, products, testers, internal track | create app, create products from `docs/play-console-products.csv`, license testers, upload AAB | product catalogue CSV generated from the rules (`PlayCatalog`), Unity IAP integration, server receipt validation + consume/acknowledge |
| Play Developer API access | service account key (or invite the Cloud Run identity) in Play Console | Secret Manager secret + Cloud Run mount (`secrets_populated`) |
| AppLovin MAX | account, app, SDK key, rewarded ad unit, mediation networks, S2S callback URL, Event Key | MAX Unity plugin 8.6.6 + EDM, `MaxAdGateway`, S2S verification `/v1/ads/ssv`, ad credits |
| Secrets | Play SA JSON, AppLovin Event Key → `gcloud secrets versions add` | secret ids, per-secret IAM, env mounting |
| Release signing | create + safeguard the upload keystore, enrol Play App Signing | `BuildAndroid` signs from `ROGUESTARS_KEYSTORE*` env vars, AAB with `ROGUESTARS_AAB=1` |
| Client build values | server URL, MAX SDK key, ad unit id as env vars at build time | `BuildSecrets` injects them for the build and restores placeholders |

## 1. GCP project and billing
1. https://console.cloud.google.com/projectcreate → project id `$PROJECT_ID`.
2. Billing → link a billing account (Cloud Run, Firestore, Scheduler, Cloud Build need it; scale-to-zero keeps idle cost ~0).
3. Locally: `gcloud auth login && gcloud auth application-default login && gcloud config set project $PROJECT_ID`.
4. Grant yourself (or the CI principal) `roles/owner` or at least Editor + Security Admin + Secret Manager Admin + Service
   Account Admin for the Terraform run.

## 2. Firebase project
1. https://console.firebase.google.com → *Add project* → select the existing GCP project `$PROJECT_ID` (links them;
   Terraform's `firebase_project_id` defaults to the same id).
2. Project settings → *Your apps* → add an **Android app** with package `$PACKAGE` and the SHA-1/SHA-256 of the
   **Play App Signing key** (Play Console → Test and release → App integrity) and of your upload key. Download
   `google-services.json`.

## 3. Firebase Auth providers
1. Authentication → Sign-in method → enable **Anonymous** (testing, smoke tests) and **Play Games**.
2. Play Games: Play Console → Grow users → Play Games Services → Setup and management → Configuration → create the Play
   Games project for the app, add an **Android credential** (package + signing SHA-1) and a **Game server credential**
   (OAuth web client). Paste the web client id/secret into Firebase's Play Games provider.
3. Client config placement: `google-services.json` belongs at `client/RogueStars/Assets/google-services.json` (git-ignore
   it or keep it out of public repos; it is not a secret but identifies the project). **The Unity client does not include
   the Firebase SDK yet** — it still signs in with `/v1/auth/dev`, which the production server (`Auth:Mode=firebase`)
   refuses. Until the Firebase Unity SDK (Auth) is integrated, an internal-testing build must point at a server deployed
   with `RogueStars__Auth__Mode=dev` *and* `ASPNETCORE_ENVIRONMENT=Staging` (dev auth is refused in Production), or the
   integration has to be added first (see docs/client.md, Known gaps).

## 4. Google Play Console
1. https://play.google.com/console → *Create app* (name, default language, game, free with in-app purchases).
2. **Upload a first build** to *Internal testing* (needed before products can be created): build an AAB with
   `ROGUESTARS_AAB=1` and the upload key (step 12) and upload it.
3. **Products**: Monetize with Play → Products → *One-time products*. Create one product per row of
   `docs/play-console-products.csv` (product id = `product_id`, e.g. `rs.diamonds_60`; name = `title`; description;
   one purchase option, *Buy*, price from `price_usd` with automatic local prices). Product type is a client/server
   concept for one-time products: consumables are consumed by the server after the grant, non-consumables are
   acknowledged — nothing to configure in Play except leaving *multi-quantity* **off** (the server refuses quantity > 1).
   The monthly pass is a one-time 30-day product, **not** a subscription. Activate every product.
   (The CSV is our catalogue export, not a Play import schema; it is regenerated from the shop content by
   `UPDATE_PLAY_CATALOG=1 dotnet test --filter PlayConsoleCsv`, and a test fails when it is stale.)
4. **License testers**: Settings (all apps) → License testing → add the testers' Google accounts, response
   `RESPOND_NORMALLY`. Their purchases carry `testPurchaseContext` and are accepted while `iap_allow_test_purchases = true`.
5. **Internal testing track**: Test and release → Testing → Internal testing → create the tester list (same accounts),
   copy the opt-in link to the testers.
6. **Android Publisher API access** (server receipt validation, consume, acknowledge):
   - The API is enabled by Terraform (`androidpublisher.googleapis.com`).
   - Option A (recommended, no key file): Play Console → Users and permissions → *Invite new users* →
     `rogue-stars-server@$PROJECT_ID.iam.gserviceaccount.com` (Terraform output `server_service_account`) with app
     permissions **View financial data** and **Manage orders and subscriptions** for this app. Leave the
     `rogue-stars-play-publisher-credentials` secret empty; the server uses its runtime identity (ADC).
   - Option B (key file): create a service account (e.g. `play-publisher@…`), invite it the same way, create a JSON key,
     and store it in Secret Manager (step 8, `play_credentials_in_secret = true`). Keep the key file out of the repository
     and delete it locally afterwards.
   - Permissions can take up to 24 h to apply to the API.

## 5. AppLovin MAX
1. https://dash.applovin.com → sign up, add the Android app (`$PACKAGE`).
2. **SDK key**: Account → General → Keys → *SDK Key* → used only at client build time (`ROGUESTARS_MAX_SDK_KEY`).
3. **Event key** (S2S verification): same page → *Event Key* → Secret Manager (step 8).
4. **Rewarded ad unit**: MAX → Manage → Ad Units → Create → Android, *Rewarded* → copy the 16-hex ad unit id
   (`ROGUESTARS_MAX_REWARDED_AD_UNIT`; optionally also Terraform `ads_allowed_ad_unit_ids`).
5. **S2S callback**: edit the ad unit → *Server Side Callback URL* (after the server is deployed, step 9):
   ```
   https://<service_url>/v1/ads/ssv?user_id={USER_ID}&event_id={EVENT_ID}&custom_data={CUSTOM_DATA}&placement={PLACEMENT}&ad_unit_id={AD_UNIT_ID}&event_token={EVENT_TOKEN}&event_token_all={EVENT_TOKEN_ALL}
   ```
   Verification rule and sources: docs/server.md → *AppLovin MAX S2S rewarded callbacks*. After the first live callback,
   check the logs for `EVENT_TOKEN_ALL shadow check … match` before considering `ads_verification = "event_token_all"`.
6. **Mediation networks** (optional): MAX → Mediation → Networks; add the networks' adapters to the Unity project with the
   AppLovin Integration Manager (Unity menu AppLovin → Integration Manager), commit the resulting
   `Assets/MaxSdk/Mediation/*` and run a build. AdMob requires its app id in the Integration Manager.
7. App-ads.txt: publish AppLovin's app-ads.txt lines on the developer website listed in Play Console.

## 6. Terraform remote state
```bash
gcloud storage buckets create gs://$PROJECT_ID-tfstate --location=$REGION --uniform-bucket-level-access
gcloud storage buckets update gs://$PROJECT_ID-tfstate --versioning
cd infra/terraform
cp example.tfvars prod.tfvars        # *.tfvars except example.tfvars are git-ignored
# edit prod.tfvars: project_id, region, firestore_location, image_tag (set in step 7), secrets_populated = false
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
Add the secret values (never in Terraform, never in git):
```bash
# AppLovin Event Key (printf: no trailing newline)
printf '%s' "$APPLOVIN_EVENT_KEY" | gcloud secrets versions add rogue-stars-applovin-ssv-key --data-file=-
# Only for Play option B:
gcloud secrets versions add rogue-stars-play-publisher-credentials --data-file=play-service-account.json
```
Then set `secrets_populated = true` in `prod.tfvars` (and `play_credentials_in_secret = true` only for Play option B)
and apply again. Cloud Run mounts the Event Key as `RogueStars__Ads__EventKey` and, with option B, the key file as
`RogueStars__Iap__ServiceAccountJson`; with option A the server uses its runtime identity. Until the Event Key is
mounted every ad callback is rejected (`401 invalid_signature`).

Later deployments: `gcloud builds submit …` with a new `_TAG`, then `terraform apply -var-file=prod.tfvars -var image_tag=<tag>`.

## 9. Cloud Scheduler
Created by Terraform (UTC): `daily-reset` 00:05, `weekly-arena` Monday 00:15, `invasion-start` every 5 min,
`invasion-resolve` every 15 min, each with an OIDC token of `rogue-stars-scheduler@…` and audience `jobs_audience`.
Run one by hand to verify: `gcloud scheduler jobs run rogue-stars-daily-reset --location=$REGION` and check Cloud Run logs
for `POST /internal/jobs/daily-reset` with status 200.

## 10. Smoke tests against the deployed URL
```bash
URL=$(terraform -chdir=infra/terraform output -raw service_url)
curl -s $URL/healthz                                     # {"status":"ok",...,"auth":"firebase","store":"firestore"}
# Player token: Firebase anonymous sign-up (Web API key: Firebase project settings → General)
TOKEN=$(curl -s "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=$FIREBASE_WEB_API_KEY" \
  -H 'content-type: application/json' -d '{"returnSecureToken":true}' | jq -r .idToken)
curl -s -XPOST $URL/v1/session -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' -d '{"displayName":"Smoke"}' | jq '{ok,playerId,version}'
curl -s $URL/v1/invasions -H "authorization: Bearer $TOKEN" | jq '.invasions | length'
curl -s $URL/v1/arena/leaderboard -H "authorization: Bearer $TOKEN" | jq .ok
curl -s $URL/v1/iap/config -H "authorization: Bearer $TOKEN" | jq '.products | length'   # 23
# Signed AppLovin callback (exactly as AppLovin signs it): sha1(event_id + event key)
PLAYER=$(curl -s -XPOST $URL/v1/session -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' -d '{}' | jq -r .playerId)
EV=$(uuidgen | tr A-Z a-z); SIG=$(printf '%s%s' "$EV" "$APPLOVIN_EVENT_KEY" | shasum -a 1 | cut -d' ' -f1)
curl -s "$URL/v1/ads/ssv?user_id=$PLAYER&event_id=$EV&custom_data=daily_diamonds_ad&event_token=$SIG"   # {"ok":true,"outcome":"granted"}
curl -s "$URL/v1/ads/ssv?user_id=$PLAYER&event_id=$EV&custom_data=daily_diamonds_ad&event_token=$SIG"   # {"ok":true,"duplicate":true}
curl -s -o /dev/null -w '%{http_code}\n' "$URL/v1/ads/ssv?user_id=$PLAYER&event_id=$EV-x&event_token=$SIG"   # 401
curl -s -o /dev/null -w '%{http_code}\n' -XPOST $URL/internal/jobs/daily-reset                                # 401 (OIDC only)
```
IAP end to end: install the internal-testing build as a license tester, buy `60 Diamonds` with a test card; Cloud Run
logs show `Purchase diamonds_60 order GPA.… : True (test True)`; Play Console → Order management shows it consumed.

## 11. Client build pointing at the server
Unity 6.6 batchmode (docs/client.md). Build-time values are environment variables; `BuildSecrets` writes them into the
assets for the build and restores the committed placeholders afterwards:
```bash
export ROGUESTARS_SERVER_URL=$URL                       # online by default
export ROGUESTARS_STORE=unity_iap ROGUESTARS_ADS=max    # (fake/fake for a dev build against a local server)
export ROGUESTARS_MAX_SDK_KEY=…  ROGUESTARS_MAX_REWARDED_AD_UNIT=…
export ROGUESTARS_KEYSTORE=$HOME/secure/rogue-stars-upload.jks ROGUESTARS_KEY_ALIAS=upload
read -s ROGUESTARS_KEYSTORE_PASS && export ROGUESTARS_KEYSTORE_PASS
export ROGUESTARS_AAB=1                                 # Play requires an AAB; omit for a sideloadable APK
U=/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity
arch -arm64 $U -batchmode -nographics -quit -buildTarget Android -projectPath $PWD/client/RogueStars \
  -logFile /tmp/rs-build.log -executeMethod RogueStars.Client.Editor.BuildAndroid.Build
grep '\[BuildAndroid\]' /tmp/rs-build.log    # OK Builds/RogueStars.aab (store unity_iap, ads max, signed True) …
```
Gradle downloads the AppLovin SDK (Maven Central) and Play Billing during the build (network required). In the editor,
tests and offline play the fake store/ads are always used.

## 12. Release signing (upload key + Play App Signing)
1. Create the upload keystore once (keep it **outside** the repository):
   ```bash
   keytool -genkeypair -v -keystore ~/secure/rogue-stars-upload.jks -alias upload -keyalg RSA -keysize 4096 -validity 10000
   ```
2. Store the keystore file and both passwords in a password manager / secret store (e.g. 1Password, or Secret Manager
   in a separate admin project) with an offline backup. Losing it means requesting an upload-key reset from Google.
3. Play App Signing is the default for new apps: on the first AAB upload Play generates and keeps the **app signing key**;
   your keystore is only the **upload key**. Register both certificates' SHA-1/SHA-256 in Firebase (step 2) and the Play
   Games credential (step 3).
4. Builds without `ROGUESTARS_KEYSTORE` are debug-signed (fine for local APKs, rejected by Play).

## 13. Go-live checklist
- `scripts/preflight.sh --tfvars infra/terraform/prod.tfvars --emulator` is green.
- `/healthz` reports `auth: firebase`, `store: firestore`; jobs ran (Scheduler → last run status OK).
- A license-tester purchase was granted and consumed; a No Ads purchase acknowledged; a rewarded ad produced
  `outcome: granted` (or `credited`) in the logs; replaying the callback gives `duplicate`.
- Decide `iap_allow_test_purchases` for production and set `ads_allowed_ad_unit_ids`.
- Firebase sign-in integrated in the client (step 3) before opening the app to non-testers.
