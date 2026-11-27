#!/usr/bin/env python3
"""Art pipeline output and acceptance per time window (hourly improvement loop, docs/art/art-session.md §2):

    python3 tools/art/metrics.py                 # last 60 min
    python3 tools/art/metrics.py --hours 3
    python3 tools/art/metrics.py --since "2026-11-27 14:00" --until "2026-11-27 15:00"
    python3 tools/art/metrics.py --log           # also append the summary to ~/AI/out/pipeline/metrics.jsonl

An image counts in the window of its raw file's mtime (when the generator wrote it). Outcomes come from
pipeline-state.json: auto-fail (pipeline checks), QC fail / QC pass (coordinator gate), approved (owner), owner-rejected
(passed, then taken off the page without approval), superseded (cancelled), pending.
"""
import argparse, collections, json, os, re, time
from datetime import datetime, timedelta

OUT = os.path.expanduser("~/AI/out")
STATE = os.path.join(OUT, "pipeline-state.json")
ART = os.path.join(OUT, "art-state.json")
LOG = os.path.join(OUT, "pipeline", "metrics.jsonl")

BUCKETS = [  # first match wins
    ("shadow", r"shadow|ellipse"), ("text", r"text|letter"), ("white", r"white"), ("fire", r"fire|flame|torch|candle|ember"),
    ("facing", r"faces? right|facing|points? right|arrow"), ("base", r"pedestal|base|slab|plinth|ground|mound|dirt|stand"),
    ("halo", r"halo|glow"), ("edge", r"edge|cut off|touch"), ("duplicate", r"duplicate|near-cop"),
    ("shape", r"octagon|hexagon|shape|notch|bow|coin|egg|wasp|detached|limb|missing"),
    ("clutter", r"clutter|spark|dots|complicated|decorat|floating|creature in|worm|button object"),
]


def bucket(reasons):
    t = " ".join(reasons).lower()
    for name, rx in BUCKETS:
        if re.search(rx, t):
            return name
    return "other"


def outcome(it, rejected):
    st = it.get("stage")
    if st == "accepted":
        return "approved"
    if st == "superseded":
        return "superseded"
    if st in ("pending-qc", "retry-queued"):
        return "pending"
    if st == "passed":
        return "owner-rejected" if it.get("processed") in rejected else "qc-pass"
    if st == "failed":
        return "qc-fail" if it.get("qc") == "fail" else "auto-fail"
    return st or "?"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--hours", type=float, default=1.0)
    ap.add_argument("--since"); ap.add_argument("--until")
    ap.add_argument("--log", action="store_true")
    a = ap.parse_args()
    until = datetime.strptime(a.until, "%Y-%m-%d %H:%M") if a.until else datetime.now()
    since = datetime.strptime(a.since, "%Y-%m-%d %H:%M") if a.since else until - timedelta(hours=a.hours)
    items = json.load(open(STATE))["items"].values()
    art = json.load(open(ART))
    rejected = {os.path.expanduser(x) for c in art["collections"] for x in c.get("rejected", []) or []}

    rows, mtimes = [], []
    for it in items:
        raw = it.get("raw")
        if not raw or not os.path.exists(raw):
            continue
        t = datetime.fromtimestamp(os.path.getmtime(raw))
        if since <= t < until:
            rows.append((it, outcome(it, rejected)))
            mtimes.append(t)
    n = len(rows)
    oc = collections.Counter(o for _, o in rows)
    reviewable = oc["qc-pass"] + oc["approved"] + oc["owner-rejected"]
    decided = n - oc["pending"] - oc["superseded"]
    fails = collections.Counter(bucket(it.get("reasons", [])) for it, o in rows if o in ("auto-fail", "qc-fail"))
    by_pkg = collections.defaultdict(collections.Counter)
    for it, o in rows:
        by_pkg[it["package"]][o] += 1
    mtimes.sort()
    gaps = [(b - a_).total_seconds() for a_, b in zip(mtimes, mtimes[1:]) if (b - a_).total_seconds() < 1200]
    summary = {
        "at": datetime.now().strftime("%Y-%m-%d %H:%M"), "since": since.strftime("%Y-%m-%d %H:%M"),
        "until": until.strftime("%Y-%m-%d %H:%M"), "generated": n, "reviewable": reviewable,
        "yield": round(reviewable / decided, 3) if decided else None,
        "retry_share": round(sum(1 for it, _ in rows if it.get("attempt", 1) > 1) / n, 3) if n else None,
        "approved": oc["approved"], "owner_rejected": oc["owner-rejected"],
        "approval_rate": round(oc["approved"] / (oc["approved"] + oc["owner-rejected"]), 3) if oc["approved"] + oc["owner-rejected"] else None,
        "sec_per_image": round(sum(gaps) / len(gaps)) if gaps else None,
        "outcomes": dict(oc), "fail_reasons": dict(fails.most_common()),
    }
    print(f'{summary["since"]} -> {summary["until"]}: {n} generated, {reviewable} reviewable '
          f'(yield {summary["yield"]}), {oc["approved"]} approved / {oc["owner-rejected"]} owner-rejected '
          f'(approval {summary["approval_rate"]}), retries {summary["retry_share"]}, {summary["sec_per_image"]} s/image')
    print("outcomes:", dict(oc))
    print("fail reasons:", dict(fails.most_common()))
    for p, c in sorted(by_pkg.items()):
        print(f"  {p}: {dict(c)}")
    if a.log:
        os.makedirs(os.path.dirname(LOG), exist_ok=True)
        with open(LOG, "a") as f:
            f.write(json.dumps(summary) + "\n")


if __name__ == "__main__":
    main()
