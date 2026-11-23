#!/usr/bin/env bash
# Runs the Firestore integration tests against the local Firestore emulator (no cloud access, no credentials).
#   scripts/firestore-tests.sh            # emulator suite (Category=Firestore) + the full server suite on Firestore
#   scripts/firestore-tests.sh --quick    # emulator suite only
# Needs Node (npx) and Java 21+ (JAVA_HOME). The emulator project id is a demo-* id, which never touches a real project.
set -euo pipefail
cd "$(dirname "$0")/.."

DOTNET="${DOTNET:-dotnet}"
if [[ "$(uname)" == "Darwin" && -x "$HOME/.dotnet/dotnet" ]]; then DOTNET="arch -arm64 $HOME/.dotnet/dotnet"; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export GCLOUD_PROJECT="${GCLOUD_PROJECT:-demo-roguestars}"
FIREBASE_TOOLS="${FIREBASE_TOOLS:-firebase-tools@14}"
if [[ -n "${JAVA_HOME:-}" ]]; then export PATH="$JAVA_HOME/bin:$PATH"; fi

$DOTNET build RogueStars.slnx -v q -nologo

CMD="$DOTNET test server/RogueStars.Server.FirestoreTests --no-build --filter Category=Firestore --logger 'console;verbosity=normal'"
if [[ "${1:-}" != "--quick" ]]; then
  CMD="$CMD && ROGUESTARS_TEST_STORE=firestore $DOTNET test server/RogueStars.Server.Tests --no-build"
fi

npx -y "$FIREBASE_TOOLS" emulators:exec --only firestore --project "$GCLOUD_PROJECT" --config scripts/firebase.emulator.json "$CMD"
