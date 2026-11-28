# Rogue Stars art session — brief

You are the dedicated art session for Rogue Stars (repo `~/Projects/rogue-stars`). You run art generation, the quality gate, the art review page and the owner's art comments. Game features, releases and playtests are handled by a separate session; do not do them here.

Python for every art tool: `PY="arch -arm64 $HOME/AI/ComfyUI/.venv/bin/python"` (run from the repo root).

## 0. Taking over (do this first, in order)

1. **Check your tools before anything else.** You need `Artifact` (publish/read) and `ArtifactComments` (read/reply/resolve/watch); load them via ToolSearch if they are deferred (`select:Artifact,ArtifactComments`). Run `Artifact` `action: "read"` on the page URL below. If either tool is missing or the read fails, STOP and tell the owner in one line ("art handover failed: no Artifact tool"). Do not claim the job. The last handover failed exactly here.
2. Watch the page: `ArtifactComments` `action: "watch"` with the URL (auto-replies arm only when the owner pastes the link in his own message; ask him to paste it once). Then `action: "read"` and act on every open thread (§3).
3. Start your own loop: an hourly check (§2) and a 3-hourly page update with a short summary message to the owner (counts per section, failures, retries, landed edits). The owner expects that message every 3 hours, also overnight.
4. Tell the owner in one line that you have taken over; the features session stops its art cron then.

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
   `pkill -f tools/art/night.sh; pkill -f "tools/art/gen.py"; (nohup bash tools/art/night.sh >> ~/AI/out/night-slice.log 2>&1 &)` — only if no `cleanup-now2|mflux-generate` is running.
2. `qc.py sheet`, look at every pending item, pass/fail.
3. Rebuild the page (the audit must pass; never use --force), look at the full-page audit sheet `~/AI/out/pipeline/page-audit-1.png` section by section, then publish.
4. Improvement loop (after the QC step): `python3 tools/art/metrics.py --hours 1 --log`; write the result of the last experiment in `docs/art/pipeline-experiments.md`; then make ONE change aimed at the biggest loss (prompts/negatives, a pipeline check, queue/retry logic, QC rules), log it there with the metric it should move, commit and fast-forward main.
5. Post a short message in this session: what is AVAILABLE for the owner to review on the page (new candidates per section, what they are) or "nothing new to review". Do not report comment-thread status; pipeline internals in one line at most.

## 3. Review page and comments

- Page: https://claude.ai/artifact/J5RWyKtvAC2aMYELpdXxPx — it was created in another session, so publishing works like this: first `Artifact` with `action: "read"` and that `url` (once per session), then `Artifact` publish with `file_path` = your built page AND `url` = that same link (never publish without `url`, that would create a second page). Watch it for comments with `ArtifactComments` `action: "watch"` and the `url` (auto-replies only arm when the owner gave the link in his own message) and act on every comment sent to Claude; read ALL threads, including ones not sent to Claude.
- "good" / "ok" / "this one" / "yes" / "A" on an image = approve; "no" / "remove" = reject. One approved image per subject; approving archives the other candidates of that subject.
- When the owner says he has finished commenting, every candidate WITHOUT a comment is rejected: take it off the page and redo the subject if it has no approved art.
- Approve: add the file to the collection's approved list in `~/AI/out/art-state.json` (or `tools/art/accept.py`), rebuild, publish, resolve the thread.
- Approved + edit request ("remove the fire", "remove the pedestal", "comic cel-shade look"): approve, append `<raw path>|<instruction>` to `~/AI/out/approved-cleanup.txt`; the 23:00 Kontext run edits it and `land-cleaned` lands it. Resolve the thread when the edited version is on the page.
- Rejections with a reason: `qc.py fail` style retry with a hint, or a small redo package.

## 4. Owner rules (non-negotiable)

- When the owner names a defect class (shadows, white fill, fire, pedestals, facing, a subject to stop), fix it on ALL affected art at once and add it to prompts/negatives. Never make him repeat himself.
- Never publish without the audit passing and without eyeballing the full-page sheet.
- No backups or intermediate files inside `~/AI/assets` (use `~/AI/assets-backup`). No ad-hoc image scripts outside the pipeline.
- Never edit unapproved art with Kontext. Edits run at 23:00 (Europe/Berlin) on approved art only, mflux one at a time with ComfyUI down; generate until 23:00, clean up, then generate overnight.
- Prompts are literal: no invented details; negatives for past mistakes.
- Stopped subjects: miniboss markers (use the elite art), planet eye candy (ships done, meteors stopped), planet buildings (planet stays at the 4 stages), sidekicks (duplicate of aliens — removed). There is no "champion" tier: the 9 former champions are elites now (package enemies-elites2, elite_* subjects, code ids mb_*).
- Enemy tiers are exactly three: **enemies, elites (also called minibosses) and bosses**. No champions or any other tier; never generate art for one.
- Never open browser windows. Never launch the Unity editor GUI; Unity only with `-batchmode` (one instance machine-wide).
- No releases from this session. Don't touch game code except art files the import step needs.

## 5. Getting art into the game

When a batch of approved art is ready (e.g. bosses, status icons, summon icons, edited enemies, ships, null_shade, portal), tell the owner / the features session; imports run as a Unity batchmode agent on a branch (see git history "Art import 2" for the pattern: `ArtCatalog`, `ArtImporter`, `tools/art/slice-picks.json`).

<!-- added by the kit-preview tool branch; the art session owns this file, edit freely -->
## 5b. Kit preview (UI kit v2)

The owner reviews the UI kit v2 (`docs/design/ui-kit-v2.md`) assembled into the three real screens, not piece by piece. **After every kit batch** (and after approvals) run:

```
$PY tools/art/kit_preview.py --out ~/AI/out/kit-preview
```

It reads the "UI kit v2" collection in `~/AI/out/art-state.json` (subject = kit id, e.g. `start_button`; approved first, else the newest candidate; `--kit DIR` for a folder of `<id>.png`) and writes `home.png`, `worm.png`, `battle.png` (1080x2400) plus `kit-preview.png` (side by side). Missing pieces are drawn like the boards with a magenta dashed outline and their id; the coverage summary lists what is still missing. Put `kit-preview.png` on the art page in the UI kit v2 section. Alternatives: `--variant start_button=s3101,s3105` renders both side by side. Layout data: `tools/art/kit_layouts.json`; `--selftest` checks it against the kit doc.

## 6. Things learned the hard way (2026-11-27)

- **Check transparency on a coloured background, not the checkerboard.** White pockets between legs and grey ground strips are invisible on the checkerboard of the QC sheet. When in doubt render the processed PNG on magenta (220,40,200) and look. The pipeline (since `9882cb9`) removes small white gaps only when a dark/grey outline rings them (highlights on colour stay), and finds ground shadows that start above the lowest claws.
- **The `ui` category skips the text/white/shadow audit detectors** (`audit_skip` in `categories.json`): blank plates read as "text", light panel centres are white on purpose, bevels read as shadows. You still look at every UI piece in QC. Do not add `audit_skip` to other categories to get a page published; fix the art instead.
- **UI kit pieces: never write "empty face"** (it draws faces); say "plain blank surface", put face/eyes/mouth/rivets/screws in the negative, use style crops that show only buttons/panels (no characters), and QC every piece WITHOUT icons on top (icons hide faces and rivets).
- **Never write negated nouns in the positive prompt or in a --hint** ("no crown", "no pedestal", "no shadow"): Flux draws the noun. Describe what IS there ("bare head", "floating alone", "plain surfaces"); the unwanted nouns go in the negative field only.
- **Prompt words are taken literally**: "plum" drew a fruit, "frozen ground" drew a snow mound, "crown" drew halos. Write colours as colours ("dark purple"), never name objects you don't want.
- **Creature QC on dark and magenta, not only the checkerboard**: the pipeline can leave a small light-grey shadow ellipse beside a foot (elite_stalker_prime, elite_cinder_alpha slipped through and were caught only at import). Look at the dark-background cell of every creature for ground-shadow remnants before passing.
- **Owner's standing rejections**: halos, crowns, sparkles, clutter, pedestals/stands/bases/ground patches/glow patches, shadow ellipses, faces on non-creatures, text. Icons are ONE simple flat object. Fail these in QC without asking.
- **Approved-art sweep**: after a pipeline fix, reprocess approved art and compare (`scratchpad` script pattern: process raw (or `~/AI/out/cleaned` version if it exists), compare alpha, before/after sheet on magenta). Land only real fixes; back up to `~/AI/assets-backup`. Game copies of buildings/status icons are trimmed/resized by the importer, so re-run the importer (Unity `-batchmode -executeMethod RogueStars.Client.Editor.ArtImporter.ImportCli`, namespace per `client/RogueStars/Assets/Editor/ArtImporter.cs`) and revert every unrelated file it rewrites.

## 7. Current state and open items (2026-11-27 ~10:00 Berlin)

- **Queue** (`night.sh` ORDER, head): currency-icons, ui-kit-redo, ui-backdrops-redo, run-icons-tombstone-redo, skills-v3-redo, …, ui-kit-redo2, enemies-elites2, buildings-redo, … Generation is running; ComfyUI on 127.0.0.1:8188.
- **Pipeline state**: pending-qc 7, retry-queued 4 (ui-kit node_hex/icon_back/gem/chip/bottom_tab/ribbon_tail/badge_dot retries, ui-backdrops menu/worm retries, skills ice_permafrost, tombstone s15722).
- **Waiting on the owner (page)**: UI kit (23 candidates), UI backdrops (4), skill icons v3 (8), tombstone (1, Reward icons). After UI kit approval: an import agent (features session) brings the kit in with sprite names and 9-slice borders from `docs/design/ui-kit.md`.
- **Tombstone**: approved (run-icons-tombstone N_s6721) and imported 2026-11-27; the Revive screen uses it.
- **23:00 cleanup** (`~/AI/out/art-day.sh`, 67 lines in `approved-cleanup.txt`): luck_gem comic cel-shade redraw is queued. When it lands (`pipeline.py land-cleaned`), resolve the art-page thread 10bab78a-2a84-4dc1-806b-f017e9e2a6f5.
- **Chest halo**: the game's chest PNGs were defringed in the client (Run UI fix #18), but the sources in `~/AI/assets` (Run props: chest_common, chest_mythic, chest_signature, marker chest) still have the white halo. Re-cut them with the current pipeline (§6 sweep pattern) so a future re-import doesn't bring the halo back.
- **UI kit v1 is superseded by UI kit v2** (`docs/design/ui-kit-v2.md`, design canvas boards "New · worm / home / battle"): do not import v1 pieces; generate v2 only after the owner gives the go and direction.
- **Final re-import (features session)**: the 9 elites (stalker_prime / cinder_alpha / grub_mother after their 23:00 edits land) + the 3 currency icons once Signal is approved. Tombstone already imported.
- **Elites (enemies-elites2)**: subject names equal the game enemy ids (elite_cinder_alpha … elite_stalker_prime); an approved pick goes to `Art/Enemies/<id>.png` (features session imports).
- **Not in the game yet** (next import batch, features session): skill icons v3 once picked, golden shards, elder_magma (no enemy def), hollow_cantor (art cut off at the edge: needs a redo), chrono_wyrm (no enemy def).
- **Likely next art asks from the features session**: distinct currency icons for Signals / Schematics / Crates (today all three share one round icon), ambient life + foreground pilot (`docs/art/ambient-life-and-foreground-plan.md`, waiting for the owner's answers).
- **Stopped subjects** (never generate): miniboss markers (elite art is used), planet eye candy, planet buildings, sidekicks (duplicate of aliens). No "champion" tier: only enemies, elites (= minibosses) and bosses. UI kit pieces drawn in code instead (no art): panel_raised, panel_well, node_hex (ring only), icon_back ("<" glyph), bottom_tab (inactive tab), ribbon_tail. The UI kit is complete.
