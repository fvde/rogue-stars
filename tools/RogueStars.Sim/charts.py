"""Renders the progression charts for docs/balance from the simulator's CSVs.

Usage (Apple Silicon, local venv outside the repo):
    arch -arm64 /usr/bin/python3 -m venv /tmp/rs-venv && /tmp/rs-venv/bin/pip install matplotlib
    /tmp/rs-venv/bin/python tools/RogueStars.Sim/charts.py docs/balance [label_before] [label_after] [compare.png]
"""
import csv
import os
import sys
from collections import defaultdict

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

TIME = ["Casual", "Active", "Hardcore"]
SPEND = ["F2P", "Dolphin", "Whale"]
COLORS = {"F2P": "#2a7ab9", "Dolphin": "#e3a72f", "Whale": "#c8413b"}
STYLES = {"Casual": ":", "Active": "-", "Hardcore": "--"}


def read_weekly(path):
    rows = defaultdict(list)
    with open(path) as f:
        for r in csv.DictReader(f):
            rows[r["profile"]].append({k: float(v) for k, v in r.items() if k != "profile"})
    return rows


def read_checkpoints(data_dir, label):
    out = {}
    for t in TIME:
        for s in SPEND:
            name = f"{t}_{s}".lower() + (f"_{label}" if label else "") + ".csv"
            p = os.path.join(data_dir, name)
            if not os.path.exists(p):
                continue
            with open(p) as f:
                rd = csv.reader(f)
                header = next(rd)
                days = [int(h[3:]) for h in header[1:]]
                out[f"{t} {s}"] = {row[0]: dict(zip(days, map(float, row[1:]))) for row in rd}
    return out


def style(ax, title, ylabel, log=False):
    ax.set_title(title, fontsize=11, loc="left")
    ax.set_xlabel("day")
    ax.set_ylabel(ylabel)
    if log:
        ax.set_yscale("log")
    ax.grid(True, alpha=0.25)
    for side in ("top", "right"):
        ax.spines[side].set_visible(False)


def weekly_chart(rows, key, title, ylabel, out, log=False):
    fig, ax = plt.subplots(figsize=(8, 4.5), dpi=120)
    for t in TIME:
        for s in SPEND:
            r = rows.get(f"{t} {s}")
            if not r:
                continue
            ax.plot([x["day"] for x in r], [max(x[key], 1e-9) if log else x[key] for x in r],
                    STYLES[t], color=COLORS[s], linewidth=1.6, label=f"{t} {s}")
    style(ax, title, ylabel, log)
    ax.legend(fontsize=7, ncol=3, frameon=False)
    fig.tight_layout()
    fig.savefig(out)
    plt.close(fig)


def checkpoint_chart(cp, metric, title, ylabel, out):
    fig, ax = plt.subplots(figsize=(8, 4.5), dpi=120)
    for t in TIME:
        for s in SPEND:
            m = cp.get(f"{t} {s}", {}).get(metric)
            if not m:
                continue
            days = sorted(m)
            ax.plot(days, [m[d] for d in days], STYLES[t], marker="o", markersize=3, color=COLORS[s], linewidth=1.4, label=f"{t} {s}")
    style(ax, title, ylabel)
    ax.set_xscale("log")
    ax.set_xticks([1, 7, 30, 90, 365, 730])
    ax.set_xticklabels(["1", "7", "30", "90", "365", "730"])
    ax.legend(fontsize=7, ncol=3, frameon=False)
    fig.tight_layout()
    fig.savefig(out)
    plt.close(fig)


def before_after(data_dir, before, after, out):
    b = read_weekly(os.path.join(data_dir, f"weekly_{before}.csv"))
    a = read_weekly(os.path.join(data_dir, f"weekly_{after}.csv" if after else "weekly.csv"))
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.2), dpi=120)
    for ax, key, title in ((axes[0], "main_level", "Main level"), (axes[1], "power", "Power (log)")):
        for prof, color in (("Active F2P", COLORS["F2P"]), ("Active Whale", COLORS["Whale"]), ("Casual F2P", "#6aa84f")):
            if prof in b:
                ax.plot([x["day"] for x in b[prof]], [x[key] for x in b[prof]], ":", color=color, label=prof + " before")
            if prof in a:
                ax.plot([x["day"] for x in a[prof]], [x[key] for x in a[prof]], "-", color=color, label=prof + " after")
        style(ax, title, key, log=key == "power")
        ax.legend(fontsize=7, frameon=False)
    fig.tight_layout()
    fig.savefig(out)
    plt.close(fig)


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "docs/balance"
    before = sys.argv[2] if len(sys.argv) > 2 else None
    after = sys.argv[3] if len(sys.argv) > 3 else ""
    compare_name = sys.argv[4] if len(sys.argv) > 4 else "before_after.png"
    data = os.path.join(root, "data")
    charts = os.path.join(root, "charts")
    os.makedirs(charts, exist_ok=True)
    suffix = f"_{after}" if after else ""
    rows = read_weekly(os.path.join(data, f"weekly{suffix}.csv"))
    weekly_chart(rows, "power", "Power over two years (median of seeds)", "power", os.path.join(charts, "power.png"), log=True)
    weekly_chart(rows, "main_level", "Main campaign level reached", "main level", os.path.join(charts, "main_level.png"))
    weekly_chart(rows, "spent_usd", "Cumulative spend", "USD", os.path.join(charts, "spend.png"))
    cp = read_checkpoints(data, after)
    checkpoint_chart(cp, "hero_stars_best", "Best hero stars", "stars", os.path.join(charts, "hero_stars.png"))
    checkpoint_chart(cp, "mount_stars_best", "Best mount stars (+ awakening below)", "stars", os.path.join(charts, "mount_stars.png"))
    checkpoint_chart(cp, "part_rarity_avg", "Average equipped part rarity (4 = Legendary, 5 = Mythic, 8 = Mythic S, 11 = Mythic SS)", "rarity step", os.path.join(charts, "part_rarity.png"))
    checkpoint_chart(cp, "building_level_avg", "Average building level", "level", os.path.join(charts, "building_level.png"))
    checkpoint_chart(cp, "worm_level", "Worm level", "level", os.path.join(charts, "worm_level.png"))
    checkpoint_chart(cp, "alien_stars_avg", "Average alien stars", "stars", os.path.join(charts, "alien_stars.png"))
    if before:
        before_after(data, before, after, os.path.join(charts, compare_name))


if __name__ == "__main__":
    main()
