#!/usr/bin/env bash
# Overnight art queue: every content package in tools/art/packages once (pass 1), then again with shifted seeds so each
# subject also gets a different style variant (pass 2, fills the rest of the night). After each package the images are
# exported as game-ready transparent sprites to ~/AI/assets/<category>/<id>/ (export_assets.py). Needs tools/art/prompts/direction.json (the style variants) and a running ComfyUI.
#
#   tools/art/night.sh            # run in the background; resumable (existing images are skipped)
set -uo pipefail
cd "$(dirname "$0")/../.."
[[ -f tools/art/prompts/direction.json ]] || { echo "missing tools/art/prompts/direction.json" >&2; exit 1; }
curl -sf 127.0.0.1:8188/system_stats >/dev/null || { echo "ComfyUI not running on :8188" >&2; exit 1; }
PY="arch -arm64 $HOME/AI/ComfyUI/.venv/bin/python"
ORDER="${ORDER:-slice-setpieces run-characters run-props run-icons hub-planet hub-scene hub-icons slice-texture slice-battle slice-hud slice-skills slice-scatter sidekicks currencies avatar-frames simple-redo tile-redo grounds decor parts mounts hero-skins bosses buildings skills}"
for pass in 1 2; do
  for p in $ORDER; do
    sheet=tools/art/packages/$p.json
    if [[ $pass == 1 ]]; then python3 tools/art/gen.py "$sheet"
    else
      seeds=$(python3 -c "import json;print(','.join(str(s+50) for s in json.load(open('$sheet'))['seeds']))")
      python3 tools/art/gen.py "$sheet" --seeds "$seeds"
    fi
    name=$(python3 -c "import json;print(json.load(open('$sheet'))['name'])")
    full=$(python3 -c "import json;print('--fullframe' if json.load(open('$sheet')).get('fullframe') else '')")
    $PY tools/art/export_assets.py "$HOME/AI/out/$name" $full >/dev/null 2>&1 || echo "export failed: $name"
    echo "== pass $pass $p done $(date +%H:%M)"
  done
done
# Pass 3 (queue filler): resources the client still draws as placeholders (icons, tiles, statuses, events, shop art,
# collectibles, minigame sprites) and full-frame art (loading screens, biome backgrounds; exported without cutout).
for sheet in tools/art/packages/extras/*.json; do
  python3 tools/art/gen.py "$sheet"
  name=$(python3 -c "import json;print(json.load(open('$sheet'))['name'])")
  full=$(python3 -c "import json;print('--fullframe' if json.load(open('$sheet')).get('fullframe') else '')")
  $PY tools/art/export_assets.py "$HOME/AI/out/$name" $full >/dev/null 2>&1 || echo "export failed: $name"
  echo "== extras $name done $(date +%H:%M)"
done
