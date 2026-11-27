#!/usr/bin/env bash
# Overnight art queue: every content package in ORDER once (pass 1), then again with shifted seeds so each subject also
# gets a different style variant (pass 2), then the extras (pass 3). Every new image goes straight through the art
# pipeline (tools/art/pipeline.py ingest, via gen.py --after): prompt check -> defect removal -> resize -> final QC ->
# pending-qc (the hourly coordinator's gate, tools/art/qc.py). A failed image gets ONE simplified retry; queued retries
# are generated before the queue continues (before and after every package). Needs a running ComfyUI.
#
#   tools/art/night.sh            # run in the background; resumable (existing images are skipped)
set -uo pipefail
cd "$(dirname "$0")/../.."
[[ -f tools/art/prompts/direction.json ]] || { echo "missing tools/art/prompts/direction.json" >&2; exit 1; }
curl -sf 127.0.0.1:8188/system_stats >/dev/null || { echo "ComfyUI not running on :8188" >&2; exit 1; }
PY="arch -arm64 $HOME/AI/ComfyUI/.venv/bin/python"
PIPE="$PY tools/art/pipeline.py"
RS="$HOME/AI/out/pipeline/retry-sheet.json"
mkdir -p "$HOME/AI/out/pipeline"
ORDER="${ORDER:-currency-icons-redo enemies-elites2-redo currency-icons ui-kit-redo ui-backdrops-redo run-icons-tombstone-redo skills-v3-redo run-icons-tombstone ui-kit ui-backdrops ui-kit-redo2 ui-backdrops-redo2 run-icons-tombstone-redo2 enemies-elites2 buildings-redo summon-icons-redo status-icons2-redo slice-markers6 aliens3-redo enemies3-redo aliens3 enemies3 bosses status-icons2 summon-icons buildings skills-v3 hub-planet2 run-result2 hub-scene2 slice-battle2 board-ground slice-markers4 run-props2 run-icons3 slice-hud2 hub-icons2 slice-hud slice-scatter mounts hero-skins run-icons2 requeue-1 run-characters run-props run-icons run-result hub-planet hub-scene hub-icons slice-texture slice-battle currencies avatar-frames simple-redo tile-redo grounds decor parts}"

retries() {
  $PIPE land-cleaned 2>/dev/null   # finished 23:00 cleanups of approved art -> checked, replace the approved asset
  $PIPE sweep 2>/dev/null
  n=$($PIPE retry-sheet "$RS" 2>/dev/null | tail -1)
  if [[ -f "$RS" && "$n" != "0" ]]; then
    echo "== retries: $n $(date +%H:%M)"
    python3 tools/art/gen.py "$RS" --after "$PIPE ingest"
  fi
}

for pass in 1 2; do
  for p in $ORDER; do
    retries
    sheet=tools/art/packages/$p.json
    if [[ $pass == 1 ]]; then python3 tools/art/gen.py "$sheet" --after "$PIPE ingest --package $p"
    else
      seeds=$(python3 -c "import json;print(','.join(str(s+50) for s in json.load(open('$sheet'))['seeds']))")
      python3 tools/art/gen.py "$sheet" --seeds "$seeds" --after "$PIPE ingest --package $p"
    fi
    retries
    echo "== pass $pass $p done $(date +%H:%M)"
  done
done
# Pass 3 (queue filler): resources the client still draws as placeholders and full-frame art (category fullframe in
# tools/art/categories.json: no cut-out, only checked and resized).
for sheet in tools/art/packages/extras/*.json; do
  retries
  k="extras/$(basename "$sheet" .json)"
  python3 tools/art/gen.py "$sheet" --after "$PIPE ingest --package $k"
  retries
  name=$(python3 -c "import json;print(json.load(open('$sheet'))['name'])")
  echo "== extras $name done $(date +%H:%M)"
done
