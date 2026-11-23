#!/usr/bin/env bash
# Overnight art queue: every content package in tools/art/packages once (pass 1), then again with shifted seeds so each
# subject also gets a different style variant (pass 2, fills the rest of the night), with sprite cutouts after each
# package. Needs tools/art/prompts/direction.json (the style variants) and a running ComfyUI.
#
#   tools/art/night.sh            # run in the background; resumable (existing images are skipped)
set -uo pipefail
cd "$(dirname "$0")/../.."
[[ -f tools/art/prompts/direction.json ]] || { echo "missing tools/art/prompts/direction.json" >&2; exit 1; }
curl -sf 127.0.0.1:8188/system_stats >/dev/null || { echo "ComfyUI not running on :8188" >&2; exit 1; }
PY="arch -arm64 $HOME/AI/ComfyUI/.venv/bin/python"
ORDER="aliens enemies parts mounts hero-skins bosses buildings skills"
for pass in 1 2; do
  for p in $ORDER; do
    sheet=tools/art/packages/$p.json
    if [[ $pass == 1 ]]; then python3 tools/art/gen.py "$sheet"
    else
      seeds=$(python3 -c "import json;print(','.join(str(s+50) for s in json.load(open('$sheet'))['seeds']))")
      python3 tools/art/gen.py "$sheet" --seeds "$seeds"
    fi
    name=$(python3 -c "import json;print(json.load(open('$sheet'))['name'])")
    [[ $p == skills ]] || $PY tools/art/cutout.py "$HOME/AI/out/$name" >/dev/null 2>&1 || true
    echo "== pass $pass $p done $(date +%H:%M)"
  done
done
