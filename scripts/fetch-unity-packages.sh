#!/usr/bin/env bash
# Downloads Google's Unity packages (Firebase App + Auth, External Dependency Manager) from Google's official archive
# (https://developers.google.com/unity/archive), checks their SHA-256 and puts them where Packages/manifest.json expects
# them (client/RogueStars/Packages/google/*.tgz, git-ignored: the Firebase App tarball alone is ~60 MB).
# Run once after cloning (and after a version bump here), before opening the Unity project. Idempotent; downloads are
# cached in ~/Library/Caches/rogue-stars-unity-packages (or $XDG_CACHE_HOME) so further worktrees only copy.
set -euo pipefail
cd "$(dirname "$0")/.."

DEST=client/RogueStars/Packages/google
CACHE="${XDG_CACHE_HOME:-$HOME/Library/Caches}/rogue-stars-unity-packages"
BASE=https://dl.google.com/games/registry/unity

# name version sha256 — keep in sync with client/RogueStars/Packages/manifest.json
PACKAGES=(
  "com.google.external-dependency-manager 1.2.190 9a326599dab4e6bcaf13cd267689cec9e302abe213929335b3d0f93083b998de"
  "com.google.firebase.app 14.2.0 94b713e94514e3ef1c17143be64009ed9e8c7c9221b4c6078b33e7a3c381372b"
  "com.google.firebase.auth 14.2.0 c1bc03810acf318a8d98e5decb1b764bcb15c5f65bbe7c934308b815ded8325d"
)

mkdir -p "$DEST" "$CACHE"
for entry in "${PACKAGES[@]}"; do
  read -r name version sha <<<"$entry"
  file="$name-$version.tgz"
  if [[ ! -f "$CACHE/$file" ]]; then
    echo "download $file"
    curl -sSfL "$BASE/$name/$file" -o "$CACHE/$file.part"
    mv "$CACHE/$file.part" "$CACHE/$file"
  fi
  actual=$(shasum -a 256 "$CACHE/$file" | cut -d' ' -f1)
  if [[ "$actual" != "$sha" ]]; then
    echo "SHA-256 mismatch for $file: expected $sha, got $actual" >&2
    rm -f "$CACHE/$file"
    exit 1
  fi
  if ! cmp -s "$CACHE/$file" "$DEST/$file"; then cp "$CACHE/$file" "$DEST/$file"; fi
  echo "ok   $DEST/$file"
done
