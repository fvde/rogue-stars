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
- Result: (next check)
