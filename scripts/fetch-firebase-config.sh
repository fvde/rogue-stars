#!/usr/bin/env bash
# Download the current google-services.json for the Android app from the Firebase Management API into
# client/RogueStars/Assets/google-services.json (git-ignored) and check that Play Games sign-in can work with it.
#
#   scripts/fetch-firebase-config.sh
#
# Needs gcloud application-default credentials with access to the Firebase project (cloud-platform scope).
# Fails (exit 1) when the config has no web OAuth client matching the Play Games web client id, or no Android OAuth
# client for the Play app signing certificate: a build with such a config falls back to guest sign-in.
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
PROJECT=${FIREBASE_PROJECT:-rogue-stars-515610}
PACKAGE=com.oddbyte.roguestars
WEB_CLIENT_ID=${ROGUESTARS_PLAY_GAMES_WEB_CLIENT_ID:-586749426081-0j1nid07fv9hbvfs0o7k2hudshg02eqc.apps.googleusercontent.com}
# Play App Signing key (Play Console -> App integrity), SHA-1 without colons, lower case.
SIGNING_SHA1=${ROGUESTARS_APP_SIGNING_SHA1:-5542008961afcbdd87aa4e4b4f1cc64328b403b1}
OUT=$ROOT/client/RogueStars/Assets/google-services.json
GCLOUD=${GCLOUD:-$(command -v gcloud || echo "$HOME/.local/share/google-cloud-sdk/bin/gcloud")}

die() { echo "fetch-firebase-config: $*" >&2; exit 1; }

T=$("$GCLOUD" auth application-default print-access-token 2>/dev/null) || die "no gcloud application-default credentials"
H=(-H "Authorization: Bearer $T" -H "x-goog-user-project: $PROJECT")
API=https://firebase.googleapis.com/v1beta1/projects/$PROJECT

APP_ID=$(curl -sS "${H[@]}" "$API/androidApps" | jq -r --arg p "$PACKAGE" '.apps[]? | select(.packageName == $p) | .appId' | head -1)
[[ -n $APP_ID ]] || die "no Firebase Android app for $PACKAGE in $PROJECT"

TMP=$(mktemp)
trap 'rm -f "$TMP"' EXIT
curl -sS "${H[@]}" "https://firebase.googleapis.com/v1beta1/projects/-/androidApps/$APP_ID/config" \
  | jq -er '.configFileContents' | base64 --decode > "$TMP" || die "could not download the config for $APP_ID"

jq -e --arg p "$PACKAGE" '.client[] | select(.client_info.android_client_info.package_name == $p)' "$TMP" >/dev/null \
  || die "config has no client for $PACKAGE"
jq -e --arg w "$WEB_CLIENT_ID" '[.client[].oauth_client[]? | select(.client_type == 3 and .client_id == $w)] | length > 0' "$TMP" >/dev/null \
  || die "config has no web OAuth client $WEB_CLIENT_ID (Play Games sign-in would fail): check Firebase -> Authentication -> Play Games"
jq -e --arg s "$SIGNING_SHA1" '[.client[].oauth_client[]? | select(.client_type == 1 and (.android_info.certificate_hash | ascii_downcase) == $s)] | length > 0' "$TMP" >/dev/null \
  || die "config has no Android OAuth client for the app signing key $SIGNING_SHA1: add its SHA-1 in Firebase -> Project settings -> Your apps"

mv "$TMP" "$OUT"
trap - EXIT
echo "wrote $OUT ($APP_ID, $(jq '[.client[].oauth_client[]?] | length' "$OUT") OAuth clients)"
