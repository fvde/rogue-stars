#!/usr/bin/env python3
"""Normalized BAR ICON set (owner, 2026-11-28): every icon that sits inside a bar or pill (HP, ATK, DEF, power, currencies,
exp, ...) gets the identical treatment so the client can place all of them in ONE fixed slot (inside the bar's left end,
centred) with no per-icon offsets.

Rules applied to every icon:
  * identical square canvas (128 px, transparent);
  * glow / halo pixels (alpha < 128) dropped, nothing spills outside the art;
  * identical visual size: scaled so the solid area's optical size (sqrt of the alpha area) matches a compact shape of
    84 % max extent; long thin shapes may grow up to 94 % on their long side so they don't read smaller;
  * centred by visual mass (alpha-weighted centroid), not just the bounding box (clamped so nothing leaves the canvas).

Sources: tools/art/bar_icons.json (kit id -> art-state collection + subject, newest approved file). Originals are copied
to ~/AI/assets-backup/bar-icons-src/ before anything is written; outputs go to ~/AI/assets/bar-icons/<id>/N_s9100.png.

    $PY tools/art/bar_icons.py                    # normalize everything that has approved art
    $PY tools/art/bar_icons.py --sheet <png>      # also render the contact sheet (alone + inside the kit bars)
"""
import argparse, json, os, shutil
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.abspath(__file__))
STATE = os.path.expanduser("~/AI/out/art-state.json")
OUT = os.path.expanduser("~/AI/assets/bar-icons")
BACKUP = os.path.expanduser("~/AI/assets-backup/bar-icons-src")
KIT = os.path.expanduser("~/AI/assets/ui-kit-v2")
CANVAS, MAX_COMPACT, MAX_LONG = 128, 0.84, 0.94
SLOT = 0.68                                       # client slot side as a fraction of the bar height
AREA_TARGET = 0.80 * MAX_COMPACT * CANVAS          # optical size of a compact shape (a disc of 84 % has sqrt(area) ~ 0.886*d)


def source_of(spec, state):
    coll = next((c for c in state["collections"] if c["title"] == spec["collection"]), None)
    if not coll:
        return None
    hits = [p for p in coll.get("approved", []) if f"/{spec['subject']}/" in p and os.path.exists(os.path.expanduser(p))]
    return os.path.expanduser(sorted(hits, key=lambda p: os.path.getmtime(os.path.expanduser(p)))[-1]) if hits else None


def normalize(src):
    a = np.asarray(Image.open(src).convert("RGBA")).astype(np.float64)
    solid = a[..., 3] >= 128
    a[~solid, 3] = 0                                                        # no glow / halo spill
    ys, xs = np.nonzero(solid)
    a = a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]; solid = solid[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    h, w = solid.shape
    s = AREA_TARGET / np.sqrt(solid.sum())                                  # same optical weight for every icon
    s = min(s, MAX_LONG * CANVAS / max(h, w))                               # long shapes: at most 94 % on the long side
    if max(h, w) / max(1, min(h, w)) < 1.35:
        s = min(s, MAX_COMPACT * CANVAS / max(h, w))                        # compact shapes: at most 84 %
    im = Image.fromarray(a.astype(np.uint8), "RGBA").resize((max(1, round(w * s)), max(1, round(h * s))), Image.LANCZOS)
    al = np.asarray(im)[..., 3].astype(np.float64)
    yy, xx = np.mgrid[0:al.shape[0], 0:al.shape[1]]
    cy, cx = (yy * al).sum() / al.sum(), (xx * al).sum() / al.sum()        # visual-mass centre
    ox = int(round(CANVAS / 2 - cx)); oy = int(round(CANVAS / 2 - cy))
    ox = min(max(ox, 0), CANVAS - im.width); oy = min(max(oy, 0), CANVAS - im.height)
    out = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0)); out.alpha_composite(im, (ox, oy))
    return out


def bar_slot(bar, icon):
    """The client's one fixed slot: a square INSIDE the bar's left end cap (inside the navy outline), side = 68 % of the
    bar height, centred on the end cap's centre (h/2, h/2)."""
    b = bar.copy(); h = b.height; side = int(round(h * SLOT))
    ic = icon.resize((side, side), Image.LANCZOS)
    b.alpha_composite(ic, (int(round(h / 2 - side / 2)), int(round(h / 2 - side / 2))))
    return b


def sheet(done, path):
    bars = {k: Image.open(f"{KIT}/{k}/N_s9001.png").convert("RGBA") for k in ("stat_bar", "currency_pill", "hp_frame")
            if os.path.exists(f"{KIT}/{k}/N_s9001.png")}
    rows = []
    for kid, icon in done:
        cells = [icon.resize((96, 96), Image.LANCZOS)]
        for k, bar in bars.items():
            cells.append(bar_slot(bar, icon))
        rows.append((kid, cells))
    W = 1500; pad = 14; y = 50; H = 50 + sum(max(c.height for c in cells) + pad + 18 for _, cells in rows)
    sh = Image.new("RGBA", (W, H), (24, 22, 36, 255)); d = ImageDraw.Draw(sh)
    d.text((pad, 14), "Bar icons: 128 px canvas, same visual size, centred by visual mass. Left: alone. Then inside "
           "stat_bar, currency_pill and hp_frame at client size (one fixed slot, no offsets).", fill=(230, 230, 240, 255))
    for kid, cells in rows:
        d.text((pad, y), kid, fill=(255, 210, 120, 255)); y += 18; x = pad
        for c in cells:
            bg = Image.new("RGBA", c.size, (24, 22, 36, 255)); bg.alpha_composite(c); sh.alpha_composite(bg, (x, y)); x += c.width + pad
        y += max(c.height for c in cells) + pad
    sh.save(path)


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--sheet"); a = ap.parse_args()
    spec = json.load(open(os.path.join(ROOT, "bar_icons.json")))
    state = json.load(open(STATE)); done, missing = [], []
    for kid, sp in spec.items():
        if kid.startswith("_"):
            continue
        src = source_of(sp, state)
        if not src:
            missing.append(kid); continue
        b = os.path.join(BACKUP, kid + os.path.splitext(src)[1]); os.makedirs(BACKUP, exist_ok=True)
        if not os.path.exists(b):
            shutil.copy(src, b)
        icon = normalize(src)
        os.makedirs(f"{OUT}/{kid}", exist_ok=True); icon.save(f"{OUT}/{kid}/N_s9100.png")
        done.append((kid, icon)); print(f"ok       {kid:22s} <- {src}")
    print(f"missing ({len(missing)}): {', '.join(missing)}")
    if a.sheet:
        sheet(done, os.path.expanduser(a.sheet)); print("sheet:", a.sheet)


if __name__ == "__main__":
    main()
