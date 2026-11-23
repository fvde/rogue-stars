#!/usr/bin/env bash
# Build a signed AAB and publish it to a Play track (default: internal).
#
#   scripts/release.sh [--track internal] [--notes "What's new"] [--skip-build] [--dry-run]
#
# Needs (see docs/setup.md steps 11-12):
#   - client/RogueStars/Assets/google-services.json (git-ignored)
#   - the upload keystore at $ROGUESTARS_KEYSTORE (default ~/keys/roguestars-upload.jks, alias upload)
#   - its password in the macOS keychain: service "roguestars-upload-keystore"
#   - gcloud application-default credentials with the androidpublisher scope
# The keystore password only lives in the build process environment; afterwards every file Unity/Gradle wrote it into
# is deleted (Library/Bee, Temp, ~/.gradle caches).
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
P=$ROOT/client/RogueStars
PACKAGE=com.oddbyte.roguestars
PROJECT=rogue-stars-515610
TRACK=internal
NOTES=""
SKIP_BUILD=0
DRY_RUN=0
U=${UNITY:-/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity}
GCLOUD=${GCLOUD:-$(command -v gcloud || echo "$HOME/.local/share/google-cloud-sdk/bin/gcloud")}
AAB=$P/Builds/RogueStars.aab
LOG=${TMPDIR:-/tmp}/rs-release-build.log

while [[ $# -gt 0 ]]; do
  case $1 in
    --track) TRACK=$2; shift 2 ;;
    --notes) NOTES=$2; shift 2 ;;
    --skip-build) SKIP_BUILD=1; shift ;;
    --dry-run) DRY_RUN=1; shift ;;
    *) echo "unknown argument $1" >&2; exit 2 ;;
  esac
done

say() { printf '\n== %s\n' "$*"; }
die() { echo "release: $*" >&2; exit 1; }

# --- preflight -------------------------------------------------------------------------------------------------
cd "$ROOT"
[[ -z $(git status --porcelain) ]] || die "working tree not clean (versionCode = commit count, commit first)"
[[ -f $P/Assets/google-services.json ]] || die "missing client/RogueStars/Assets/google-services.json"
export ROGUESTARS_KEYSTORE=${ROGUESTARS_KEYSTORE:-$HOME/keys/roguestars-upload.jks}
export ROGUESTARS_KEY_ALIAS=${ROGUESTARS_KEY_ALIAS:-upload}
[[ -f $ROGUESTARS_KEYSTORE ]] || die "keystore not found: $ROGUESTARS_KEYSTORE"
[[ -x $U ]] || die "Unity not found at $U (set UNITY=...)"
VERSION_CODE=$(git rev-list --count HEAD)
VERSION_NAME=0.1.$VERSION_CODE
[[ -n $NOTES ]] || NOTES=$(git log -1 --format=%s)
echo "release $VERSION_NAME ($VERSION_CODE) -> track $TRACK"

# --- build -----------------------------------------------------------------------------------------------------
scrub() {
  # Delete every file Unity/Gradle wrote the keystore password into. The password is passed to grep on an fd,
  # never on a command line.
  local pass=$1 hits
  hits=$( { grep -rlF -f /dev/fd/3 "$P/Library/Bee" "$P/Temp" "$P/Library/Android" "$HOME/.gradle" 2>/dev/null || true; } 3<<<"$pass")
  if [[ -n $hits ]]; then
    while IFS= read -r f; do rm -f -- "$f"; done <<<"$hits"
    echo "scrubbed $(wc -l <<<"$hits" | tr -d ' ') file(s) containing the keystore password"
  fi
  hits=$( { grep -rlF -f /dev/fd/3 "$P/Library/Bee" "$P/Temp" "$P/Library/Android" "$HOME/.gradle" 2>/dev/null || true; } 3<<<"$pass")
  [[ -z $hits ]] || die "password still present in: $hits"
  echo "scrub check clean"
}

if [[ $SKIP_BUILD == 0 ]]; then
  say "build"
  [[ -d $P/Packages ]] && [[ -x scripts/fetch-unity-packages.sh ]] && scripts/fetch-unity-packages.sh >/dev/null
  PASS=$(security find-generic-password -s roguestars-upload-keystore -w) || die "keystore password not in keychain"
  rm -f "$AAB"
  set +e
  ROGUESTARS_KEYSTORE_PASS=$PASS \
  ROGUESTARS_PLAY_GAMES_APP_ID=586749426081 \
  ROGUESTARS_PLAY_GAMES_WEB_CLIENT_ID=586749426081-0j1nid07fv9hbvfs0o7k2hudshg02eqc.apps.googleusercontent.com \
  ROGUESTARS_OUTPUT=aab \
    arch -arm64 "$U" -batchmode -nographics -quit -buildTarget Android -projectPath "$P" \
      -logFile "$LOG" -executeMethod RogueStars.Client.Editor.BuildAndroid.Build
  BUILD_RC=$?
  set -e
  scrub "$PASS"
  unset PASS
  # The build stamps version and keystore fields into ProjectSettings; keep the committed file.
  git checkout -- client/RogueStars/ProjectSettings/ProjectSettings.asset
  grep '\[BuildAndroid\]' "$LOG" | tail -3 || true
  [[ $BUILD_RC == 0 && -f $AAB ]] || die "build failed (log: $LOG)"
  grep -q 'signed True' "$LOG" || die "AAB is not release-signed (log: $LOG)"
fi
[[ -f $AAB ]] || die "no AAB at $AAB"

# --- publish ---------------------------------------------------------------------------------------------------
if [[ $DRY_RUN == 1 ]]; then say "dry run: not publishing $AAB"; exit 0; fi
say "publish"
T=$("$GCLOUD" auth application-default print-access-token 2>/dev/null) || die "gcloud ADC token failed (gcloud auth application-default login --scopes=...androidpublisher)"
A=https://androidpublisher.googleapis.com/androidpublisher/v3/applications/$PACKAGE
UP=https://androidpublisher.googleapis.com/upload/androidpublisher/v3/applications/$PACKAGE
H=(-H "Authorization: Bearer $T" -H "x-goog-user-project: $PROJECT")
api() { local out; out=$(curl -sS "${H[@]}" "$@"); if jq -e '.error' >/dev/null 2>&1 <<<"$out"; then echo "$out" >&2; return 1; fi; echo "$out"; }

EDIT=$(api -X POST "$A/edits" -H 'content-type: application/json' -d '{}' | jq -r .id)
echo "edit $EDIT"
UPLOADED=$(api -X POST "$UP/edits/$EDIT/bundles?uploadType=media" -H 'content-type: application/octet-stream' --data-binary @"$AAB" | jq -r .versionCode)
echo "uploaded versionCode $UPLOADED"
[[ $UPLOADED == "$VERSION_CODE" ]] || echo "note: uploaded versionCode $UPLOADED differs from commit count $VERSION_CODE"

track_body() {
  jq -n --arg name "$VERSION_NAME" --arg vc "$UPLOADED" --arg notes "$NOTES" --arg status "$1" \
    '{releases:[{name:$name,versionCodes:[$vc],status:$status,releaseNotes:[{language:"en-US",text:$notes}]}]}'
}
STATUS=completed
if ! api -X PUT "$A/edits/$EDIT/tracks/$TRACK" -H 'content-type: application/json' -d "$(track_body completed)" >/dev/null 2>"${TMPDIR:-/tmp}/rs-track-err"; then
  if grep -q 'draft app' "${TMPDIR:-/tmp}/rs-track-err"; then
    STATUS=draft
    api -X PUT "$A/edits/$EDIT/tracks/$TRACK" -H 'content-type: application/json' -d "$(track_body draft)" >/dev/null
  else
    cat "${TMPDIR:-/tmp}/rs-track-err" >&2; die "track update failed"
  fi
fi
api -X POST "$A/edits/$EDIT:commit" >/dev/null
say "done: $VERSION_NAME ($UPLOADED) on $TRACK, status $STATUS"
[[ $STATUS == completed ]] || echo "Play only accepts draft releases for this app: roll it out in Play Console -> Testing -> $TRACK."
