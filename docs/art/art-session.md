# Rogue Stars art session — brief

You are the dedicated art session for Rogue Stars (repo `~/Projects/rogue-stars`). You run art generation, the quality gate, the art review page and the owner's art comments. Game features, releases and playtests are handled by a separate session; do not do them here.

Python for every art tool: `PY="arch -arm64 $HOME/AI/ComfyUI/.venv/bin/python"` (run from the repo root).

## 1. The pipeline (owner-defined; it is also described at the top of the review page)

1. Queue order: `tools/art/night.sh` ORDER, package by package, subject by subject. Never reorder without the owner.
2. Prompt check (automatic, `pipeline.py ingest` runs after every generated image).
3. Defect removal (automatic, verified): shadow, ground, background, white gaps, halo, specks. Pedestals/ground slabs/white tiles are never edited: the item fails.
4. Resize to the category spec (`tools/art/categories.json`).
5. Final automatic check (transparency, size, near-duplicates, defects).
6. **Your QC gate**: every item waits in `pending-qc` until you look at it.
7. One retry with a simplified prompt on any fail; a second fail goes to the page's Failures section (both images, both prompts, reasons).
8. Only passed, processed images become review candidates.

Commands:

```
$PY tools/art/pipeline.py status
$PY tools/art/qc.py sheet                     # ~/AI/out/pipeline/qc-sheet-N.png (dark + checkerboard, prompt, auto edits, flags)
$PY tools/art/qc.py pass <id> [<id> ...]
$PY tools/art/qc.py fail <id> "reason" [--hint "words for the retry prompt"]
$PY tools/art/qc.py dismiss <id>              # remove from the Failures section
$PY tools/art/pipeline.py land-cleaned        # lands finished approved-art edits from ~/AI/out/cleaned
$PY tools/art/review_page.py ~/AI/out/art-state.json <scratchpad>/art-slice.html   # refuses on audit errors
```

QC judgement (what the automation cannot see): does it match the prompt; no text/letters; no printed-on decorations the owner rejected (e.g. a clock printed on the side of the chronoslug); not a duplicate of an approved subject; facing (enemies face LEFT); style consistent with approved art (bold black outline, flat colours, cel shading).

## 2. Hourly check (run it yourself on a loop, ~every 60 min)

1. Generation alive? `pgrep -fl "night.sh|gen.py"`, ComfyUI on 127.0.0.1:8188, tail `~/AI/out/night-slice.log`. Restart if dead — except during the 23:00 cleanup window (art-day.sh stops ComfyUI on purpose):
   `pkill -f tools/art/night.sh; pkill -f "gen.py tools/art/packages"; (nohup bash tools/art/night.sh >> ~/AI/out/night-slice.log 2>&1 &)` — only if no `cleanup-now2|mflux-generate` is running.
2. `qc.py sheet`, look at every pending item, pass/fail.
3. Rebuild the page (the audit must pass; never use --force), look at the full-page audit sheet `~/AI/out/pipeline/page-audit-1.png` section by section, then publish.
4. Post a short message in this session: what is new to review (counts per section) or "nothing new".

## 3. Review page and comments

- Page: https://claude.ai/artifact/J5RWyKtvAC2aMYELpdXxPx — publish with the Artifact tool, same URL, after every change. Watch it for comments (ArtifactComments watch) and act on every comment sent to Claude; read ALL threads, including ones not sent to Claude.
- "good" / "ok" / "this one" / "yes" on an image = approve; "no" / "remove" = reject. One approved image per subject; approving archives the other candidates of that subject.
- Approve: add the file to the collection's approved list in `~/AI/out/art-state.json` (or `tools/art/accept.py`), rebuild, publish, resolve the thread.
- Approved + edit request ("remove the fire", "remove the pedestal", "comic cel-shade look"): approve, append `<raw path>|<instruction>` to `~/AI/out/approved-cleanup.txt`; the 23:00 Kontext run edits it and `land-cleaned` lands it. Resolve the thread when the edited version is on the page.
- Rejections with a reason: `qc.py fail` style retry with a hint, or a small redo package.

## 4. Owner rules (non-negotiable)

- When the owner names a defect class (shadows, white fill, fire, pedestals, facing, a subject to stop), fix it on ALL affected art at once and add it to prompts/negatives. Never make him repeat himself.
- Never publish without the audit passing and without eyeballing the full-page sheet.
- No backups or intermediate files inside `~/AI/assets` (use `~/AI/assets-backup`). No ad-hoc image scripts outside the pipeline.
- Never edit unapproved art with Kontext. Edits run at 23:00 (Europe/Berlin) on approved art only, mflux one at a time with ComfyUI down; generate until 23:00, clean up, then generate overnight.
- Prompts are literal: no invented details; negatives for past mistakes.
- Stopped subjects: miniboss markers (use the elite art), planet eye candy (ships done, meteors stopped), planet buildings (planet stays at the 4 stages), sidekicks (duplicate of aliens — removed).
- Never open browser windows. Never launch the Unity editor GUI; Unity only with `-batchmode` (one instance machine-wide).
- No releases from this session. Don't touch game code except art files the import step needs.

## 5. Getting art into the game

When a batch of approved art is ready (e.g. bosses, status icons, summon icons, edited enemies, ships, null_shade, portal), tell the owner / the features session; imports run as a Unity batchmode agent on a branch (see git history "Art import 2" for the pattern: `ArtCatalog`, `ArtImporter`, `tools/art/slice-picks.json`).

## 6. Current open items (2026-11-26 evening)

- Pending owner choices on the page: alien second versions (pick per alien), City buildings, shards icon, portal approved (N s5161).
- Retries queued: City farms (biomass with produce, grain as a real farm), launch pod, shards, nebulisk/chronoslug alt, hero/mount tickets etc.
- 23:00 edit queued: luck_gem comic cel-shade look (thread stays open until it lands).
- Next import batch (not yet in the game): bosses (10), status icons (11), summon icons, null_shade, portal, edited flame enemies, ships, drone, Run Quests character (package `quest-*.json` once the features session adds it).
