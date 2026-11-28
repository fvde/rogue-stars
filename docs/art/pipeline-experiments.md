# Art pipeline experiments (hourly improvement loop)

Each hourly check: run `python3 tools/art/metrics.py --hours 1 --log`, compare it with the previous hour, write the
result of the last experiment below, then make ONE change aimed at the biggest loss and log it here with the metric it
should move. Metrics: generated, reviewable (QC passed, approved or not picked), yield = reviewable / decided,
retry share, s/image, approval rate (approved / (approved + not picked)), fail-reason buckets.

Caveat: "owner_rejected" also counts candidates archived because another candidate of the subject was approved.

## Baseline (2026-11-27 15:28)

- Last 1 h: 11 generated, 0 reviewable, retries 82%, 188 s/image. 3 of 11 were retries for subjects already approved.
- Last 6 h: 107 generated, 24 reviewable (yield 0.25), 12 approved, retries 45%, 181 s/image.
  Fail reasons: shadow 16, base/ground 15, shape 12, white 8, fire 6, clutter 4, facing 3.
- Last 24 h: 509 generated, yield 0.43, approval 0.49, 130 s/image.

## Experiments

### E1 (2026-11-27 15:35): skip retries for already-approved subjects; signal prompt as one object
- Change: `pipeline.py retry-sheet` marks queued retries superseded when the subject (collection + subject) already has an
  approved asset. Signal redo prompt changed from "dot with three arcs" (always auto-failed as "several separate objects")
  to "a round purple badge with a white wifi symbol".
- Expect: superseded count in new generation drops to 0; more of each hour goes to unapproved subjects; signal passes the
  auto-check.
- Result (16:31, hour 15:31-16:31): superseded in new generation 0 (was 3 of 11), retry share 0.15 (was 0.82),
  140 s/image (was 188), 26 generated (was 11), yield 0.30 (was 0.0). Signal badge passed the auto-check (2 of 3).
  Kept.

### E2 (2026-11-27 16:40): no clutter words in skill prompts
- Loss: this hour's QC fails were mostly clutter taken literally ("raining dozens of bolts", "many flying shards",
  "a few sparks", "star burst", "aura"): 4 of 6 skill-icon QC fails.
- Change: rewrote the 17 unapproved skills-v3 prompts that named sparks/shards/dozens/bursts/auras/rays into one object
  with at most three parts; negatives add "scattered pieces, debris, particles, many small objects". Restarted night.sh.
- Expect: skills-v3 QC pass rate up (this hour 3 pass of 8 QC'd; target >= 5 of 8); clutter fail bucket down.
- Result (17:31, hour 16:31-17:31; 41 generated, 88 s/image): of the rewritten prompts that generated, 6 of 10 passed
  QC (target was 5 of 8), and none failed for clutter. The 5 retries queued before the rewrite still used old prompts and
  all failed on clutter. Kept. Those 5 subjects got a redo package (skills-v3-redo2) with the new prompts.

### E3 (2026-11-27 17:45): no negated words in the positive prompt suffix
- Loss: 8 of 37 QC'd icons wore a crown (poison_spit, gamma_burst, bleed_frenzy, alien_bodyguard, alien_rally_cry,
  alien_decoy, sacrifice, symbiote). The skills-v3 suffix said "no glow, no halo, no sparkles, no crown, no background
  shapes": Flux takes the nouns literally (same as "plum" and "crown" before). Negatives stay in the negative field.
- Change: removed that phrase from the skills-v3 and skills-v3-redo suffix. Restarted night.sh.
- Expect: crown fails in new skills-v3 images 0 (was 8/37); halo/sparkle fails do not rise.
- Result (18:31, hour 17:31-18:31, measured after QC): crown fails 0 (was 8). 38 generated, 23 reviewable, yield 0.61
  (17:31 measured before QC showed 0.0; see E4). Halo/sparkle fails 0. Kept.

### E4 (2026-11-27 18:45): no negated nouns in the retry template; measure after QC
- Loss: 4 of 9 final retry fails this hour were pedestals or ground patches (gamma_burst on a stand, bleed_frenzy and
  bleed_tearing on pedestals, counter_brace in a ground patch), plus 2 with shadow ellipses. Every retry prompt ends in
  "no pedestal, no base, no ground, no shadow": the same literal-noun effect as the crowns (E3).
- Change: `pipeline.py` RETRY_SUFFIX is now ", single object, centered, floating alone on a plain pure white background";
  the shadow/text hints describe the wanted state instead of negating. Negatives stay in STRONG_NEG. Brief §6 rule added.
  Measurement: the hourly loop now runs metrics.py AFTER the QC step (17:31 read 0.0 only because nothing was QC'd yet).
- Expect: base/shadow fails among retries this hour < 2 (was 6 of 9 final fails); retry pass rate up.
- Result: (next check)
- Result (2026-11-28 09:20, first QC after the overnight run): retries this round failed mostly on base-worm drift
  (hero skins, see E5 note) and only 2 of 13 on pedestals/shadows (light_ground_loop x2 spire bases); poison_critical
  retry still drew a shadow ellipse. Base/shadow share among retry fails dropped from 6/9 to 3/14. Kept.

### E5 (2026-11-28 09:25): no "worm" in the mounts prompt
- Loss: 13 of 18 QC'd mount images had a worm sitting in the seat. The suffix said "sized for a small worm to ride in,
  empty seat": Flux draws the noun (same literal effect as E3/E4). The simplified retries, which drop that phrase, all
  came out empty (4 of 4 passed). Hero skins: 14 of 17 failed (arms drawn; the retries lose the "pink limbless worm"
  base and drift off-model) - next candidate change.
- Change: mounts suffix now "... open round cockpit seat on top with an empty padded seat" (worm only in negatives).
  Parts v2 prompts got the same treatment ("for a small cartoon space hero").
- Expect: worm-in-seat fails on new mount images 0 (was 13/18).
- Result: (next check)
