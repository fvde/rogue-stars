#!/usr/bin/env bash
# Pre-deployment checks that never touch the cloud: configuration and secret *presence* (values are never printed),
# terraform fmt/validate (no backend), and the test suites. See docs/setup.md.
#
#   scripts/preflight.sh [--tfvars infra/terraform/dev.tfvars] [--emulator] [--skip-tests]
#
# Inputs (all optional; missing ones are reported, required ones fail the run):
#   tfvars file                      project_id / image_tag / play_package_name, secrets_populated
#   PLAY_SA_JSON=path                Play Developer API service account key to upload to Secret Manager
#   APPLOVIN_EVENT_KEY               AppLovin S2S event key (Account → General → Keys)
#   ROGUESTARS_SERVER_URL            Cloud Run URL baked into the client build (https)
#   ROGUESTARS_ADS=max + ROGUESTARS_MAX_SDK_KEY, ROGUESTARS_MAX_REWARDED_AD_UNIT   AppLovin MAX client values (later)
#   ROGUESTARS_PLAY_GAMES_APP_ID, ROGUESTARS_PLAY_GAMES_WEB_CLIENT_ID   Play Games sign-in
#   ROGUESTARS_KEYSTORE, ROGUESTARS_KEYSTORE_PASS, ROGUESTARS_KEY_ALIAS                  release upload key
set -uo pipefail
cd "$(dirname "$0")/.."

TFVARS="" EMULATOR=0 SKIP_TESTS=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --tfvars) TFVARS="$2"; shift 2 ;;
    --emulator) EMULATOR=1; shift ;;
    --skip-tests) SKIP_TESTS=1; shift ;;
    -h|--help) sed -n 2,16p "$0"; exit 0 ;;
    *) echo "unknown argument $1"; exit 2 ;;
  esac
done

FAIL=0 WARN=0
ok()   { printf '  \033[32mOK\033[0m    %s\n' "$1"; }
warn() { printf '  \033[33mWARN\033[0m  %s\n' "$1"; WARN=$((WARN + 1)); }
bad()  { printf '  \033[31mFAIL\033[0m  %s\n' "$1"; FAIL=$((FAIL + 1)); }
have() { command -v "$1" >/dev/null 2>&1; }
DOTNET="dotnet"
if [[ "$(uname)" == "Darwin" && -x "$HOME/.dotnet/dotnet" ]]; then DOTNET="arch -arm64 $HOME/.dotnet/dotnet"; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1

echo "== Tools"
have terraform && ok "terraform $(terraform version | head -1 | awk '{print $2}')" || bad "terraform not installed"
$DOTNET --version >/dev/null 2>&1 && ok ".NET SDK $($DOTNET --version)" || bad ".NET SDK not installed"
have gcloud && ok "gcloud installed" || warn "gcloud not installed (needed for deployment, not for this check)"
have npx && ok "node/npx installed" || warn "node/npx missing (Firestore emulator tests)"
[[ -n "${JAVA_HOME:-}" ]] || have java && ok "java available" || warn "java missing (Firestore emulator tests)"

echo "== Terraform variables"
if [[ -z "$TFVARS" ]]; then
  warn "no --tfvars given (copy infra/terraform/example.tfvars to <env>.tfvars)"
elif [[ ! -f "$TFVARS" ]]; then
  bad "$TFVARS not found"
else
  get() { sed -nE "s/^[[:space:]]*$1[[:space:]]*=[[:space:]]*\"?([^\"]*)\"?.*/\1/p" "$TFVARS" | head -1; }
  pid=$(get project_id); tag=$(get image_tag); pkg=$(get play_package_name); sp=$(get secrets_populated)
  [[ -n "$pid" ]] && ok "project_id = $pid" || bad "project_id missing"
  [[ -n "$tag" ]] && ok "image_tag = $tag" || bad "image_tag missing"
  [[ "$pkg" =~ ^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ ]] && ok "play_package_name = $pkg" || bad "play_package_name invalid"
  [[ "$sp" == "true" ]] && ok "secrets_populated = true" || warn "secrets_populated = false (first apply; set true after adding secret versions)"
fi

echo "== Secrets (presence only)"
if [[ -n "${PLAY_SA_JSON:-}" ]]; then
  if [[ -f "$PLAY_SA_JSON" ]] && python3 -I -c 'import json,sys; d=json.load(open(sys.argv[1])); sys.exit(0 if d.get("type")=="service_account" and d.get("client_email") and d.get("private_key") else 1)' "$PLAY_SA_JSON" 2>/dev/null; then
    ok "PLAY_SA_JSON is a service account key"
  else
    bad "PLAY_SA_JSON is not a readable service account key JSON"
  fi
else
  warn "PLAY_SA_JSON not set (the server can use its runtime identity instead if it is linked in Play Console)"
fi
[[ -n "${APPLOVIN_EVENT_KEY:-}" ]] && ok "APPLOVIN_EVENT_KEY set (${#APPLOVIN_EVENT_KEY} chars)" || ok "APPLOVIN_EVENT_KEY not set (only needed with ads_mode = applovin)"

echo "== Client build configuration"
url="${ROGUESTARS_SERVER_URL:-}"
if [[ -z "$url" ]]; then warn "ROGUESTARS_SERVER_URL not set (client defaults to the local dev server)"
elif [[ "$url" == https://* ]]; then ok "ROGUESTARS_SERVER_URL is https"; else bad "ROGUESTARS_SERVER_URL must be https for release builds"; fi
if [[ "${ROGUESTARS_ADS:-disabled}" == "max" ]]; then
  [[ -n "${ROGUESTARS_MAX_SDK_KEY:-}" ]] && ok "ROGUESTARS_MAX_SDK_KEY set" || warn "ROGUESTARS_MAX_SDK_KEY not set (ads fall back to the fake gateway)"
  [[ -n "${ROGUESTARS_MAX_REWARDED_AD_UNIT:-}" ]] && ok "ROGUESTARS_MAX_REWARDED_AD_UNIT set" || warn "ROGUESTARS_MAX_REWARDED_AD_UNIT not set"
else
  ok "ads disabled (no AppLovin keys needed)"
fi
ls client/RogueStars/Packages/google/com.google.firebase.auth-*.tgz >/dev/null 2>&1 \
  && ok "Google Unity packages fetched" || bad "Google Unity packages missing (run scripts/fetch-unity-packages.sh)"
if [[ -f client/RogueStars/Assets/google-services.json ]]; then
  python3 -I -c 'import json,sys; d=json.load(open(sys.argv[1])); c=[x for x in d["client"] if x["client_info"]["android_client_info"]["package_name"]=="com.oddbyte.roguestars"]; sys.exit(0 if c and c[0].get("oauth_client") else 1)' \
    client/RogueStars/Assets/google-services.json 2>/dev/null \
    && ok "google-services.json for com.oddbyte.roguestars with OAuth clients" \
    || warn "google-services.json has no OAuth clients for com.oddbyte.roguestars (SHA fingerprints missing: Play Games sign-in will fail, docs/setup.md step 3)"
else
  warn "client/RogueStars/Assets/google-services.json missing (Firebase sign-in cannot initialize)"
fi
[[ -n "${ROGUESTARS_PLAY_GAMES_APP_ID:-}" && -n "${ROGUESTARS_PLAY_GAMES_WEB_CLIENT_ID:-}" ]] \
  && ok "Play Games app id and web client id set" \
  || warn "ROGUESTARS_PLAY_GAMES_APP_ID / _WEB_CLIENT_ID not set (build signs in anonymously unless PlayGamesSettings is committed)"
if [[ -n "${ROGUESTARS_KEYSTORE:-}" ]]; then
  [[ -f "$ROGUESTARS_KEYSTORE" ]] && ok "keystore file exists" || bad "ROGUESTARS_KEYSTORE file not found"
  [[ -n "${ROGUESTARS_KEYSTORE_PASS:-}" && -n "${ROGUESTARS_KEY_ALIAS:-}" ]] && ok "keystore password and alias set" || bad "ROGUESTARS_KEYSTORE_PASS / ROGUESTARS_KEY_ALIAS missing"
else
  warn "ROGUESTARS_KEYSTORE not set (debug-signed build; Play needs the upload key)"
fi
grep -q "YOUR_MAX_REWARDED_AD_UNIT_ID" client/RogueStars/Assets/Resources/PlatformConfig.asset 2>/dev/null \
  && ok "committed PlatformConfig keeps placeholders" || warn "PlatformConfig.asset does not contain the placeholder (real ids committed?)"
if grep -qE "sdkKey: [A-Za-z0-9_-]{20,}" client/RogueStars/Assets/MaxSdk/Resources/AppLovinSettings.asset 2>/dev/null; then
  bad "AppLovinSettings.asset contains an SDK key — keys are injected at build time, never committed"
else
  ok "no MAX SDK key committed"
fi

echo "== Terraform (offline)"
if have terraform; then
  (cd infra/terraform && terraform fmt -check -recursive >/dev/null) && ok "terraform fmt" || bad "terraform fmt -check (run terraform fmt)"
  (cd infra/terraform && terraform init -backend=false -input=false >/dev/null 2>&1 && terraform validate -no-color >/dev/null) \
    && ok "terraform validate" || bad "terraform validate"
fi

if [[ $SKIP_TESTS -eq 0 ]]; then
  echo "== Tests"
  if $DOTNET test RogueStars.slnx -nologo -v q >/tmp/rs-preflight-tests.log 2>&1; then
    ok "dotnet test RogueStars.slnx ($(grep -Eo 'Passed: +[0-9]+' /tmp/rs-preflight-tests.log | awk '{s+=$2} END {print s}') passed; emulator tests skipped)"
  else
    bad "dotnet test RogueStars.slnx (see /tmp/rs-preflight-tests.log)"
  fi
  if [[ $EMULATOR -eq 1 ]]; then
    scripts/firestore-tests.sh >/tmp/rs-preflight-firestore.log 2>&1 && ok "Firestore emulator tests" || bad "Firestore emulator tests (see /tmp/rs-preflight-firestore.log)"
  fi
fi

echo
echo "Preflight: $FAIL failed, $WARN warnings."
[[ $FAIL -eq 0 ]]
