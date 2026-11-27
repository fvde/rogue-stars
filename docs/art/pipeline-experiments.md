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
- Result: (next check)
