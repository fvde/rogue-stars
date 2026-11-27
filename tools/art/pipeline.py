#!/usr/bin/env python3
"""Rogue Stars art pipeline: every generated image is checked, cleaned, resized and checked again before the owner sees it.
Runs in the ComfyUI venv (Pillow, numpy, scipy, rembg, torch + transformers for the optional CLIP pre-filter):

    PY="arch -arm64 ~/AI/ComfyUI/.venv/bin/python"
    $PY tools/art/pipeline.py ingest --package enemies3 ~/AI/out/pkg-enemies3/x__S__flux__s1311.png   # gen.py --after hook
    $PY tools/art/pipeline.py retry-sheet ~/AI/out/pipeline/retry-sheet.json   # queued retries as a gen.py jobs sheet
    $PY tools/art/pipeline.py dry-run --out <scratch> <raw.png> ...             # offline test, no state written
    $PY tools/art/pipeline.py backfill <raw.png> ...                            # put existing raw images through it
    $PY tools/art/pipeline.py status

Stages (state in ~/AI/out/pipeline-state.json, one entry per image id <package>.<subject>.<variant>.s<seed>):
  1 queue order       night.sh ORDER, package by package, subject by subject (gen.py --after calls `ingest`)
  2 prompt check      background is plain white, object not cut off by the image edge, one main object, CLIP (local,
                      openai/clip-vit-base-patch16) ranks the image against the package's other subjects and probes for text
  3 defect removal    cut-out (white border flood fill + rembg isnet-anime), then ground shadow, detached shadow ellipses,
                      shadow outline arcs, enclosed background-white gaps and specks are removed programmatically; every
                      edit is verified and a damaging edit is not applied (the item fails instead). Pedestals / bases /
                      ground slabs are only detected (never edited on unapproved art) and fail the item.
  4 resize            to the category spec in tools/art/categories.json
  5 final QC          transparent background, no defect detections left, size spec met, not a near-duplicate (perceptual
                      hash) of another candidate/approved image of the subject, not blank, not touching the canvas edge
  6 pending-qc        human-level gate: tools/art/qc.py sheet / pass / fail (the hourly coordinator)
  7 retry once        a failed attempt queues ONE retry with a simplified prompt + stronger negatives + new seed (night.sh
                      runs retries before continuing the queue); a failed retry goes to the review page's Failures section
  8 success           qc pass adds the processed image (~/AI/out/processed/...) to its art-state.json collection as a candidate
  edits of approved art: `land-cleaned` takes finished Kontext cleanups (~/AI/out/cleaned, from approved-cleanup.txt),
                      runs cut-out + defect removal + size spec + audit and replaces the approved asset (old one backed up
                      to ~/AI/assets-backup); night.sh calls it at every package boundary
"""
import argparse, contextlib, fcntl, glob, json, os, re, sys, time
os.environ.setdefault("TRANSFORMERS_VERBOSITY", "error")
os.environ.setdefault("HF_HUB_OFFLINE", "1")  # the CLIP weights are cached locally; never reach out at night
os.environ.setdefault("TQDM_DISABLE", "1")
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.expanduser(os.environ.get("ART_OUT", "~/AI/out"))  # ART_OUT: a scratch copy for tests
STATE = os.path.join(OUT, "pipeline-state.json")
ART_STATE = os.path.join(OUT, "art-state.json")
PROC = os.path.join(OUT, "processed")
LOCK = os.path.join(OUT, "pipeline.lock")
CFG = json.load(open(os.path.join(ROOT, "categories.json")))
RETRY_SEED_OFFSET = 9000

# ---------------------------------------------------------------- thresholds (documented in the module doc / report)
T = {
    "bg_min": 200,            # border median must be at least this bright in every channel (plain white background)
    "bg_tol": 40,             # flood-fill tolerance from the border colour
    "alpha_floor": 24,        # drop faint halo pixels
    "band": 0.30,             # bottom band = lowest 30% of the object's bbox (shadows / pedestals live here)
    "shadow_chroma": 28,      # shadow-like: max-min channel spread below this ...
    "shadow_lum": (95, 243),  # ... and luminance in this range (grey, not dark body, not white)
    "shadow_rembg_max": 0.5,  # ... and rembg calls it background (it cuts shadows away but keeps grey body parts)
    "shadow_seed_rows": 0.10,  # shadow must touch the lowest 10% of the object (claws can reach below it)
    "shadow_min_area": 0.006, # regions below 0.6% of the object are anti-aliased edge pixels, not a shadow
    "shadow_max_height": 0.16,  # verified shadow: spans at most 16% of the object height ...
    "shadow_min_aspect": 1.8,   # ... is at least 1.8x wider than tall ...
    "shadow_max_area": 0.30,    # ... and at most 30% of the opaque area
    "shadow_max_grad": 22.0,    # ... and is smooth (mean gradient magnitude), not shaded/outlined body
    "white_min": 236, "white_chroma": 14,  # near-white pixels
    "white_min_area": 0.002,  # enclosed white component must be > 0.2% of the opaque area to be considered (small leg gaps too)
    "white_rembg_max": 0.35,  # ... and rembg must see it as background (mean alpha below this) ...
    "white_flat_std": 6.0,    # ... and it must be flat like the background (luminance std)
    "white_max_total": 0.35,
    "white_inner_max": 0.02,  # background gaps have no features inside (a die face has pips, a clock face hands)
    "white_ring_dark": 0.5, "white_ring_dark_lum": 90,  # gap ring (4 px): mostly dark outline ...
    "white_ring_chroma": 40,  # ... or grey; a highlight's ring is saturated body colour
    "white_dbg_max": 2.5,     # ... and are the background's exact colour (max channel difference)
    "white_dbg_max_sure": 4.0,  # ... or up to this when rembg's alpha there is below 0.05
    "white_big_frac": 0.15,   # featureless exact-background white >= 15% of the object that rembg keeps: fail, not edit  # never remove more than 35% of the opaque area as white
    "speck_px_1024": 300,     # tiny components (scaled to the raw size) that are pale / faint are specks
    "outside_band_loss": 0.03,  # verification: at most 3% of the opaque area may disappear outside the bottom band
    "ped_tol": 45, "ped_band": 0.35, "ped_band_objects": 0.5, "ped_min_width": 0.45, "ped_min_aspect": 1.3, "ped_min_frac": 0.025,
    "tile_cover": 0.6, "tile_rect": 0.88,  # a white region covering >= 60% of the silhouette and squarish = white tile/badge
    "light_edge_share": 0.3,  # > 30% light outline pixels + fragmented object = cut-out failed (no outline)
    "multi_ratio": 0.45,      # second-largest component >= 45% of the largest = several objects
    "edge_touch_px": 3,       # object within 3 px of the raw image edge = cut off
    "dup_hamming": 6,         # perceptual hash distance <= 6 bits of 64 = near-duplicate
    "clip_fail_frac": 0.5,    # CLIP: own subject ranked in the bottom half of >= 6 package subjects = prompt not achieved
    "clip_text_flag": 0.6,
    "audit_text": 0.8,        # publish gate: CLIP text probe at or above this blocks a candidate    # CLIP text probe above this = 'possible text' flag for the QC gate
}

STRONG_NEG = "pedestal, base, plinth, platform, stand, ground, floor, shadow, drop shadow, text, letters, multiple objects, frame"
RETRY_SUFFIX = ", single object, centered, isolated on a plain pure white background, no pedestal, no base, no ground, no shadow, no text"
REASON_HINTS = [
    (r"pedestal|base|plinth|slab|ground|platform|tile", "floating in the air with nothing underneath"),
    (r"shadow", "no drop shadow, no cast shadow"),
    (r"cut off|edge|cropped", "fully visible with a wide empty margin around it, small in the frame"),
    (r"multiple|several|objects", "only one single object, nothing else around it"),
    (r"text|letter|word", "no letters, no words, no writing"),
    (r"outline", "thick dark outline around the whole object"),
    (r"white area|portal hole|tile", "the inside filled with colour, no white areas, no tile behind it"),
    (r"background", "plain pure white background, nothing else"),
]


# ---------------------------------------------------------------- state
@contextlib.contextmanager
def locked():
    os.makedirs(OUT, exist_ok=True)
    with open(LOCK, "w") as lk:
        fcntl.flock(lk, fcntl.LOCK_EX)
        try:
            yield
        finally:
            fcntl.flock(lk, fcntl.LOCK_UN)


def load_state():
    try:
        return json.load(open(STATE))
    except (OSError, ValueError):
        return {"items": {}}


def save_state(st):
    tmp = STATE + ".tmp"
    json.dump(st, open(tmp, "w"), indent=1)
    os.replace(tmp, STATE)


# ---------------------------------------------------------------- packages
def sheet_path(key):
    return os.path.join(ROOT, "packages", key + ".json")


def load_sheet(key):
    return json.load(open(sheet_path(key)))


def category_of(key):
    cat, coll = CFG["packages"].get(key, ["icon", None])
    return cat, coll


def key_for_raw(raw):
    """Package key of a raw generator file: the package whose sheet name is the folder and that lists the subject."""
    name = os.path.basename(os.path.dirname(raw))
    subject = os.path.basename(raw).split("__")[0]
    hits = []
    for key in CFG["packages"]:
        try:
            sh = load_sheet(key)
        except OSError:
            continue
        if sh.get("name") == name:
            hits.append((subject in sh.get("subjects", {}), key))
    hits.sort(reverse=True)
    return hits[0][1] if hits else None


def parse_raw(raw):
    m = re.match(r"(.+)__([A-Za-z0-9]+)__([a-z0-9_]+)__s(\d+)\.png$", os.path.basename(raw))
    if not m:
        return None
    return {"subject": m.group(1), "variant": m.group(2), "model": m.group(3), "seed": int(m.group(4))}


def prompt_for(key, p):
    """The prompt gen.py used for this image (sidecar first, else rebuilt from the sheet like gen.py does)."""
    side = None
    for f in (p["raw"][:-4] + ".json",):
        if os.path.exists(f):
            side = json.load(open(f))
    sh = load_sheet(key)
    if side:
        return side["prompt"], side.get("negative", sh.get("negative", "")), sh
    subj = sh.get("subjects", {}).get(p["subject"], p["subject"].replace("_", " "))
    if "variants_from" in sh:
        vs = json.load(open(os.path.expanduser(sh["variants_from"])))["variants"]
        v = next((v for v in vs if v["key"] == p["variant"]), {"style": ""})
        return subj + ", " + v.get("style", "") + sh.get("suffix", ""), v.get("negative", sh.get("negative", "")), sh
    if "variants" in sh:
        v = next((v for v in sh["variants"] if v["key"] == p["variant"]), {"style": ""})
        return subj + ", " + v.get("style", ""), v.get("negative", sh.get("negative", "")), sh
    return subj + ", " + sh.get("styles", {}).get(p["variant"], ""), sh.get("negative", ""), sh


def item_id(name, p):
    return f'{name.removeprefix("pkg-")}.{p["subject"]}.{p["variant"]}.s{p["seed"]}'


# ---------------------------------------------------------------- image helpers
def lum_of(rgb):
    return rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114


def chroma_of(rgb):
    return rgb.max(-1) - rgb.min(-1)


_REMBG = None


def rembg_alpha(img):
    global _REMBG
    from rembg import remove, new_session
    if _REMBG is None:
        _REMBG = new_session("isnet-anime")
    return np.asarray(remove(img.convert("RGBA"), session=_REMBG).getchannel("A")).astype(np.float32) / 255.0


def bbox(mask):
    ys, xs = np.where(mask)
    if len(xs) == 0:
        return None
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def cutout(rgb_img):
    """White background -> alpha. The background is the near-white area connected to the image border (so white parts
    of the object stay opaque); rembg's alpha only adds soft edges inside that area. Returns (rgb float, alpha uint8,
    rembg alpha, info)."""
    rgb = np.asarray(rgb_img.convert("RGB")).astype(np.float32)
    h, w, _ = rgb.shape
    border = np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]])
    bg = np.median(border, axis=0)
    info = {"bg": [int(x) for x in bg]}
    near = np.abs(rgb - bg).max(-1) <= T["bg_tol"]
    lab, _ = ndi.label(near)
    edge = np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
    edge = edge[edge > 0]
    bgmask = np.isin(lab, edge)
    rem = rembg_alpha(rgb_img)
    # unoutlined white surfaces (a soft-shaded white die) touch the background and would be flood-filled away; where
    # rembg is sure it is object (alpha > 0.8, eroded so its halo does not count) it stays object
    bgmask &= ~ndi.binary_erosion(rem > 0.8, iterations=2)
    obj = ndi.binary_dilation(~bgmask, iterations=1)
    alpha = np.maximum(obj * 255.0, rem * 255.0)
    alpha[alpha < T["alpha_floor"]] = 0
    info["border_white"] = bool(bg.min() >= T["bg_min"])
    # share of the border that is NOT background: a non-white / busy background or a scene filling the frame
    info["border_busy"] = float(1 - near[[0, -1]].mean() * 0.5 - near[:, [0, -1]].mean() * 0.5)
    return rgb, alpha.astype(np.uint8), rem, info


def components(mask):
    lab, n = ndi.label(mask, structure=np.ones((3, 3)))
    if n == 0:
        return lab, np.array([0])
    sizes = ndi.sum(mask, lab, range(n + 1))
    return lab, sizes


def grad_mag(rgb):
    l = lum_of(rgb)
    gy, gx = np.gradient(l)
    return np.hypot(gx, gy)


# ---------------------------------------------------------------- defect detectors / removers (each verified)
def shadow_region(rgb, alpha, rem):
    """Shadow-like (grey, smooth) pixels in the bottom band connected to the object's bottom rows."""
    op = alpha >= 128
    bb = bbox(op)
    if bb is None:
        return None, None
    x0, y0, x1, y1 = bb
    H = y1 - y0
    band_top = int(y1 - T["band"] * H)
    lum, chroma = lum_of(rgb), chroma_of(rgb)
    lo, hi = T["shadow_lum"]
    like = (alpha > 0) & (chroma < T["shadow_chroma"]) & (lum >= lo) & (lum <= hi) & (rem < T["shadow_rembg_max"])
    like[:band_top] = False
    # a tinted shadow: the colour of the lowest opaque rows, if it is a light, low-texture colour
    bottom = op.copy(); bottom[: max(band_top, y1 - max(3, int(0.03 * H)))] = False
    if bottom.sum() > 20:
        ref = np.median(rgb[bottom], axis=0)
        if lum_of(ref[None])[0] >= 130 and chroma_of(ref[None])[0] < 90:
            tint = (alpha > 0) & (np.abs(rgb - ref).max(-1) < 30) & (rem < 0.3)
            tint[:band_top] = False
            like |= tint
    # anti-aliased outline pixels are grey too: thin lines are not shadow
    like = ndi.binary_opening(like, structure=np.ones((3, 3)), iterations=2)
    seeds = like.copy(); seeds[: y1 - max(3, int(T["shadow_seed_rows"] * H))] = False
    if seeds.sum() < 10:
        return None, band_top
    lab, _ = ndi.label(like, structure=np.ones((3, 3)))
    ids = np.unique(lab[seeds]); ids = ids[ids > 0]
    reg = np.isin(lab, ids)
    if reg.sum() < T["shadow_min_area"] * op.sum():
        return None, band_top  # a few edge pixels, not a shadow
    # soft partial-alpha pixels hugging the region belong to the shadow too
    reg |= ndi.binary_dilation(reg, iterations=2) & (alpha < 200) & (alpha > 0) & (np.arange(alpha.shape[0])[:, None] >= band_top)
    return reg, band_top


def verify_shadow(reg, rgb, alpha):
    op = alpha >= 128
    area = op.sum()
    rb = bbox(reg)
    ob = bbox(op)
    if rb is None or ob is None:
        return False, "empty"
    rh, rw = rb[3] - rb[1], rb[2] - rb[0]
    H = ob[3] - ob[1]
    g = grad_mag(rgb)[reg & op].mean() if (reg & op).any() else 0
    checks = {
        "height": rh <= T["shadow_max_height"] * H,
        "aspect": rw >= T["shadow_min_aspect"] * rh,
        "area": (reg & op).sum() <= T["shadow_max_area"] * area,
        "smooth": g <= T["shadow_max_grad"],
    }
    bad = [k for k, v in checks.items() if not v]
    return not bad, (",".join(bad) or f"h={rh/H:.2f} aspect={rw/max(rh,1):.1f} grad={g:.1f}")


def remove_shadows(rgb, alpha, rem, log):
    reg, band_top = shadow_region(rgb, alpha, rem)
    if reg is None or reg.sum() == 0:
        return alpha, band_top, None
    ok, why = verify_shadow(reg, rgb, alpha)
    if not ok:
        log.append(f"shadow edit rejected ({why})")
        return alpha, band_top, reg
    a = alpha.copy(); a[reg] = 0
    log.append(f"ground shadow removed ({int(reg.sum())} px, {why})")
    # leftover outline arcs of the shadow ellipse: thin dark strokes in the band next to the removed region that span a
    # good part of its width (a dark body's legs are thick, so the opening keeps them; short leg-edge bits are too narrow)
    op = a >= 128
    lum = lum_of(rgb)
    near = ndi.binary_dilation(reg, iterations=6)
    thin = op & ~ndi.binary_opening(op, structure=np.ones((5, 5)))
    arc = thin & near & (lum < 110) & (chroma_of(rgb) < 40)
    arc[:band_top] = False
    lab, n = ndi.label(arc, structure=np.ones((3, 3)))
    rb = bbox(reg); rw = rb[2] - rb[0]
    removed = 0
    for i in range(1, n + 1):
        m = lab == i
        b = bbox(m)
        if b[2] - b[0] >= 0.3 * rw:
            a[m] = 0; removed += m.sum()
    if removed:
        if removed <= 0.015 * (alpha >= 128).sum():
            log.append(f"shadow outline arcs removed ({removed} px)")
        else:
            a2 = alpha.copy(); a2[reg] = 0
            log.append("shadow outline edit rejected (too large)")
            a = a2
    return a, band_top, None


def remove_detached_band(rgb, alpha, band_top, log):
    """Components that are not the main object, lie entirely in the bottom band and are grey/dark and flat: detached
    shadow ellipses and their rings."""
    op = alpha > 0
    lab, sizes = components(op)
    if len(sizes) < 3:
        return alpha
    main = int(np.argmax(sizes[1:]) + 1)
    total = (alpha >= 128).sum()
    lum, chroma = lum_of(rgb), chroma_of(rgb)
    a = alpha.copy()
    n_rm = px = 0
    for i in range(1, len(sizes)):
        if i == main:
            continue
        m = lab == i
        b = bbox(m)
        if b[1] < band_top or sizes[i] > 0.15 * total:
            continue
        greyish = ((chroma[m] < T["shadow_chroma"]) | (lum[m] < 110)).mean()
        flat = (b[2] - b[0]) >= 1.8 * (b[3] - b[1])
        if greyish >= 0.6 and flat:
            a[m] = 0
            n_rm += 1; px += int(sizes[i])
    if n_rm:
        log.append(f"{n_rm} detached shadow pieces removed ({px} px)")
    return a


def remove_enclosed_white(rgb, alpha, rem, bg, log):
    """Background-white gaps enclosed by the object (between limbs, inside rings, cog holes): near-white, flat like the
    background, larger than 2% of the object, and rembg sees them as background. White that rembg keeps as part of the
    object (dice faces, clock faces, blades, white fur) stays."""
    op = alpha >= 128
    area = op.sum()
    white = op & (rgb.min(-1) >= T["white_min"]) & (chroma_of(rgb) <= T["white_chroma"])
    lab, sizes = components(white)
    a = alpha.copy()
    lum = lum_of(rgb)
    gone = 0
    kept, big = [], []
    bgcol = np.array(bg)
    for i in range(1, len(sizes)):
        if sizes[i] < max(T["white_min_area"] * area, 150):
            continue
        m = lab == i
        r = rem[m].mean()
        sd = lum[m].std()
        d_bg = np.abs(rgb[m].mean(0) - bgcol).max()
        # a surface carries features (pips, eyes, clock hands, sand): holes inside the white region
        inner = (ndi.binary_fill_holes(m) & ~m).sum() / m.sum()
        # rembg certain it is background (alpha ~0) tolerates a slightly off-white tone (JPEG-ish drift near outlines)
        dbg_max = T["white_dbg_max"] if r >= 0.05 else T["white_dbg_max_sure"]
        bg_like = inner < T["white_inner_max"] and d_bg <= dbg_max and sd < T["white_flat_std"]
        if bg_like:
            # a gap between limbs is ringed by the black outline; a highlight sits on saturated colour (a red heart)
            ring = ndi.binary_dilation(m, iterations=4) & ~m & op
            ring &= lum < 200
            if ring.any():
                dark = (lum[ring] < T["white_ring_dark_lum"]).mean()
                rch = np.median(chroma_of(rgb)[ring])
                bg_like = dark >= T["white_ring_dark"] or rch < T["white_ring_chroma"]
        if bg_like and r < T["white_rembg_max"]:
            fringe = ndi.binary_dilation(m, iterations=3) & (lum > 200) & (chroma_of(rgb) < 30) & ~m
            a[m | fringe] = 0
            gone += int((m | fringe).sum())
        elif tile_like(m, op):
            big.append("a white tile/badge behind the object")
        elif bg_like and d_bg <= 1.0 and sizes[i] >= T["white_big_frac"] * area and \
                (alpha[ndi.binary_dilation(m, iterations=10) & ~m] == 0).mean() < 0.1:
            big.append(f"{sizes[i]/area:.0%} of the object")
        else:
            kept.append(f"{int(sizes[i])}px rembg={r:.2f} inner={inner:.2f} d_bg={d_bg:.1f}")
    if gone:
        if gone > T["white_max_total"] * area:
            log.append(f"white-gap edit rejected (would remove {gone/area:.0%} of the object)")
            return alpha, "large background-white area that cannot be removed safely"
        log.append(f"enclosed background white removed ({gone} px)")
    if kept:
        log.append("white kept as part of the object: " + "; ".join(kept[:3]))
    if big:
        return a, "large plain white area in the object (" + ", ".join(big) + "), e.g. an empty ring/portal hole or a white tile"
    return a, 0


def remove_halo(rgb, alpha, log):
    """Light fringe left by the white background along the silhouette (shows as a white rim on dark screens): near-white,
    grey pixels within 2 px of the transparent area. Capped at 3% of the object; outlined art loses only the fringe."""
    a = alpha.copy()
    light = (lum_of(rgb) > 225) & (chroma_of(rgb) < 20)
    gone = 0
    for _ in range(2):
        edge = (a > 0) & ndi.binary_dilation(a == 0, iterations=1) & light
        a[edge] = 0
        gone += int(edge.sum())
    if gone > 0.03 * (alpha >= 128).sum():
        log.append("halo edit rejected (too large)")
        return alpha
    if gone > 50:
        log.append(f"light halo removed ({gone} px)")
    return a


def tile_like(m, op):
    f = ndi.binary_fill_holes(m)
    sil = ndi.binary_fill_holes(op)
    b = bbox(f)
    rect = f.sum() / max(1, (b[2] - b[0]) * (b[3] - b[1]))
    return f.sum() >= T["tile_cover"] * sil.sum() and rect >= T["tile_rect"]


def remove_specks(rgb, alpha, log):
    op = alpha > 0
    lab, sizes = components(op)
    if len(sizes) < 3:
        return alpha
    h, w = alpha.shape
    thr = T["speck_px_1024"] * (h * w) / (1024 * 1024)
    main = int(np.argmax(sizes[1:]) + 1)
    a = alpha.copy()
    lum, chroma = lum_of(rgb), chroma_of(rgb)
    n = 0
    for i in range(1, len(sizes)):
        if i == main or sizes[i] >= thr:
            continue
        m = lab == i
        pale = (lum[m].mean() > 180 and chroma[m].mean() < 35)
        faint = alpha[m].mean() < 160
        greydark = chroma[m].mean() < 25
        if pale or faint or greydark or sizes[i] < thr / 10:
            a[m] = 0; n += 1
    if n:
        log.append(f"{n} specks removed")
    return a


def detect_pedestal(rgb, alpha, band=None):
    """A distinct slab under the object (pedestal, base, plinth, ground disc): the region grown by colour from the
    object's lowest rows starts in the bottom 35% of the object, is wide (>= 45% of the object width), wider than tall,
    solid, at least 2.5% of the object, and a different colour from the object right above it (so a slug's own belly or
    a creature's feet do not count). Returns a reason or None. Never edited: a pedestal fails the item (-> retry)."""
    op = alpha >= 128
    bb = bbox(op)
    if bb is None:
        return None
    x0, y0, x1, y1 = bb
    H, W = y1 - y0, x1 - x0
    band = band or T["ped_band"]
    lum = lum_of(rgb)
    # a slab floating detached below the object (separate component, wide and flat, low down)
    lab0, sz0 = components(op)
    if len(sz0) > 2:
        main = int(np.argmax(sz0[1:]) + 1)
        for i in range(1, len(sz0)):
            if i == main or sz0[i] < T["ped_min_frac"] * op.sum():
                continue
            b = bbox(lab0 == i)
            if (b[1] - y0) / H >= 1 - band and b[2] - b[0] >= 0.3 * W and (b[2] - b[0]) >= 2 * (b[3] - b[1]):
                return f"pedestal/base under the object (detached {b[2]-b[0]}x{b[3]-b[1]} px slab)"
    bottom = op.copy(); bottom[: y1 - max(3, int(0.03 * H))] = False
    if bottom.sum() < 20:
        return None
    ref = np.median(rgb[bottom], axis=0)
    sim = op & (np.abs(rgb - ref).max(-1) < T["ped_tol"]) & (lum > 40)
    lab, _ = ndi.label(sim, structure=np.ones((3, 3)))
    ids = np.unique(lab[bottom & sim]); ids = ids[ids > 0]
    if len(ids) == 0:
        return None
    reg = np.isin(lab, ids)
    rb = bbox(reg)
    rh, rw = rb[3] - rb[1], rb[2] - rb[0]
    top_rel = (rb[1] - y0) / H
    if top_rel < 1 - band or rw < T["ped_min_width"] * W or rw < T["ped_min_aspect"] * rh \
            or reg.sum() < T["ped_min_frac"] * op.sum():
        return None
    rows = reg[rb[1]:rb[3], rb[0]:rb[2]].astype(np.int8)
    runs = np.median((np.diff(rows, axis=1) == 1).sum(1) + rows[:, 0])
    if lum_of(ref[None])[0] < 70 and runs > 1.5:
        return None  # dark legs/feet joined by their outline, not a slab
    above = op & ~reg; above[rb[1]:] = False; above[: max(0, rb[1] - int(0.12 * H))] = False
    if above.sum() < 50:
        return None
    diff = np.abs(np.median(rgb[above], axis=0) - ref).max()
    if diff < T["ped_tol"] * 0.8:
        return None
    return f"pedestal/base/ground under the object ({rw}x{rh} px slab at the bottom, {reg.sum()/op.sum():.0%} of the object)"


# ---------------------------------------------------------------- CLIP pre-filter (local, optional)
_CLIP = None


def clip_model():
    global _CLIP
    if _CLIP is None:
        try:
            import torch  # noqa
            from transformers import CLIPModel, CLIPProcessor
            snap = glob.glob(os.path.expanduser("~/.cache/huggingface/hub/models--openai--clip-vit-base-patch16/snapshots/*"))
            if not snap:
                _CLIP = False
            else:
                _CLIP = (CLIPModel.from_pretrained(snap[0]).eval(), CLIPProcessor.from_pretrained(snap[0]))
        except Exception as e:  # pragma: no cover
            print("CLIP unavailable:", e, file=sys.stderr)
            _CLIP = False
    return _CLIP


def clip_scores(img, subject_text, others):
    cm = clip_model()
    if not cm:
        return {}
    import torch
    m, pr = cm

    def norm(x):
        x = getattr(x, "pooler_output", x)
        return x / x.norm(dim=-1, keepdim=True)
    with torch.no_grad():
        v = norm(m.get_image_features(**pr(images=img, return_tensors="pt")))
        texts = [subject_text] + others + ["an image containing written text, letters and words", "a picture with no text"]
        t = norm(m.get_text_features(**pr(text=texts, return_tensors="pt", padding=True, truncation=True, max_length=77)))
    sims = (v @ t.T)[0]
    subj = sims[: 1 + len(others)]
    rank = int((subj > subj[0]).sum().item()) + 1
    tp = (100 * sims[-2:]).softmax(-1)[0].item()
    return {"clip_rank": rank, "clip_n": 1 + len(others), "clip_text": round(tp, 2)}


# ---------------------------------------------------------------- resize / final checks
def resize_to_spec(rgba, cat):
    spec = CFG["categories"][cat]
    a = rgba[..., 3]
    bb = bbox(a > 0)
    crop = Image.fromarray(rgba).crop(bb)
    s = spec["long_side"] / max(crop.size)
    crop = crop.resize((max(1, round(crop.width * s)), max(1, round(crop.height * s))), Image.LANCZOS)
    if spec.get("canvas"):
        c = spec["canvas"]
        out = Image.new("RGBA", (c, c), (0, 0, 0, 0))
        x = (c - crop.width) // 2
        y = c - spec.get("margin_bottom", 0) - crop.height if spec.get("align") == "bottom" else (c - crop.height) // 2
        out.paste(crop, (x, y), crop)
        return out
    p = spec.get("pad", 0)
    out = Image.new("RGBA", (crop.width + 2 * p, crop.height + 2 * p), (0, 0, 0, 0))
    out.paste(crop, (p, p), crop)
    return out


def check_size(img, cat):
    spec = CFG["categories"][cat]
    a = np.asarray(img)[..., 3]
    bb = bbox(a > 0)
    long = max(bb[2] - bb[0], bb[3] - bb[1])
    if spec.get("canvas") and img.size != (spec["canvas"], spec["canvas"]):
        return f"canvas {img.size} != {spec['canvas']}"
    if abs(long - spec["long_side"]) > 3:
        return f"long side {long} != {spec['long_side']}"
    if bb[0] < 1 or bb[1] < 1 or bb[2] > img.width - 1 or bb[3] > img.height - 1:
        return "object touches the canvas edge"
    return None


def phash(img):
    from scipy.fft import dctn
    im = Image.new("RGBA", img.size, (128, 128, 128, 255))
    im.alpha_composite(img.convert("RGBA"))
    g = np.asarray(im.convert("L").resize((32, 32), Image.LANCZOS)).astype(np.float32)
    d = dctn(g, norm="ortho")[:8, :8].flatten()
    bits = d > np.median(d[1:])
    return "".join("1" if b else "0" for b in bits)


def same_source(path, variant, seed):
    """An export / processed copy of the very same generator image (same variant and seed) is not a duplicate."""
    n = os.path.basename(path)
    return f"__{variant}__" in n and f"__s{seed}." in n or n.startswith(f"{variant}_s{seed}") or f"__{variant}__s{seed}." in n


def hamming(a, b):
    return sum(x != y for x, y in zip(a, b))


def approved_hashes(collection, subject, variant=None, seed=None):
    """pHashes of the approved images of this subject in its art-state collection (cached by path)."""
    try:
        st = json.load(open(ART_STATE))
    except (OSError, ValueError):
        return []
    out = []
    for c in st.get("collections", []):
        if c.get("title") != collection:
            continue
        for pat in c.get("approved", []) + c.get("candidates", []):
            for f in glob.glob(os.path.expanduser(pat)):
                n = os.path.splitext(os.path.basename(f))[0]
                sub = n.split("__")[0] if "__" in n else os.path.basename(os.path.dirname(f))
                if sub == subject and f.endswith(".png") and not same_source(f, variant, seed):
                    try:
                        out.append((f, phash(Image.open(f))))
                    except OSError:
                        pass
    return out


# ---------------------------------------------------------------- the per-image pipeline
def process(raw, key, prompt, st=None, dry=False, out_dir=None, use_clip=True):
    """Stages 2-5 for one raw image. Returns a result dict (stage pending-qc or failed, reasons, edits, paths)."""
    p = parse_raw(raw)
    cat, coll = category_of(key)
    spec = CFG["categories"][cat]
    sh = load_sheet(key)
    name = sh["name"]
    iid = item_id(name, p)
    res = {"id": iid, "package": key, "name": name, "category": cat, "collection": coll, "raw": raw,
           "prompt": prompt, **p, "reasons": [], "flags": [], "edits": [], "metrics": {}}
    img = Image.open(raw).convert("RGB")
    dest_dir = out_dir or os.path.join(PROC, name.removeprefix("pkg-"))
    os.makedirs(dest_dir, exist_ok=True)
    dest = os.path.join(dest_dir, f'{p["subject"]}__{p["variant"]}__s{p["seed"]}.png')
    res["processed"] = dest

    # ---- stage 2: prompt check (things a script can tell)
    subj_text = sh.get("subjects", {}).get(p["subject"], p["subject"].replace("_", " "))
    if use_clip:
        others = [v for k, v in sh.get("subjects", {}).items() if k != p["subject"]]
        sc = clip_scores(img, subj_text, others)
        res["metrics"].update(sc)
        if sc and sc["clip_n"] >= 6 and sc["clip_rank"] > T["clip_fail_frac"] * sc["clip_n"]:
            res["reasons"].append(f'prompt not achieved (CLIP ranks it {sc["clip_rank"]}/{sc["clip_n"]} among the package subjects)')
        elif sc and sc["clip_rank"] > 1 and sc["clip_n"] >= 3:
            res["flags"].append(f'CLIP rank {sc["clip_rank"]}/{sc["clip_n"]}')
        if sc and sc.get("clip_text", 0) >= T["audit_text"]:
            res["reasons"].append(f'possible text or letters (CLIP {sc["clip_text"]:.2f})')
        elif sc and sc.get("clip_text", 0) >= T["clip_text_flag"]:
            res["flags"].append(f'possible text (CLIP {sc["clip_text"]:.2f})')

    if spec.get("cutout") is False:
        a = np.asarray(img)
        if a.std() < 10:
            res["reasons"].append("blank image")
        im = img.copy(); im.thumbnail((spec["max_side"], spec["max_side"]), Image.LANCZOS)
        im.save(dest, optimize=True)
        res["phash"] = phash(im.convert("RGBA"))
        return finish(res, st, coll, p["subject"])

    rgb, alpha, rem, info = cutout(img)
    res["metrics"]["bg"] = info["bg"]
    if not info["border_white"]:
        res["reasons"].append(f'background is not plain white (border colour {info["bg"]})')
    op0 = alpha >= 128
    if op0.mean() < 0.02:
        res["reasons"].append("blank or nearly empty image")
        return finish(res, st, coll, p["subject"], save=False)
    if op0.mean() > 0.92:
        res["reasons"].append("no background left after cut-out (scene fills the frame)")
    lab0, sz0 = components(op0)
    big_ids = np.where(sz0 >= 0.01 * op0.sum())[0]
    sig = np.isin(lab0, big_ids[big_ids > 0])
    bb0 = bbox(sig)
    e = T["edge_touch_px"]
    h, w = op0.shape
    if bb0[0] < e or bb0[1] < e or bb0[2] > w - e or bb0[3] > h - e:
        res["reasons"].append("object cut off by the image edge")
    edge_px = op0 & ~ndi.binary_erosion(op0, iterations=2)
    light_edge = float((lum_of(rgb)[edge_px] > 215).mean()) if edge_px.any() else 0.0
    s0 = np.sort(sz0[1:])[::-1] if len(sz0) > 1 else np.array([1])
    n_sig = int((s0 >= 0.01 * op0.sum()).sum())
    if light_edge > T["light_edge_share"] and n_sig >= 5 and s0[1] >= 0.2 * s0[0]:
        res["reasons"].append("background removal failed: the object has no dark outline against the white background")

    # ---- stage 3: defect removal (programmatic, each edit verified)
    log = res["edits"]
    a1, band_top, unremoved_shadow = remove_shadows(rgb, alpha, rem, log)
    a1 = remove_detached_band(rgb, a1, band_top, log)
    a1, white_left = remove_enclosed_white(rgb, a1, rem, info["bg"], log)
    a_mid = a1
    a1 = remove_halo(rgb, a1, log)
    a1 = remove_specks(rgb, a1, log)
    # global verification: nothing outside the bottom band may vanish except verified white gaps (the halo and speck
    # steps are capped / limited to fringe and tiny pale pieces on their own)
    lost = op0 & (a_mid < 128)
    lost[band_top:] = False
    white_gone = sum(int(re.search(r"\((\d+) px\)", x).group(1)) for x in log if x.startswith("enclosed background white"))
    if lost.sum() - white_gone > T["outside_band_loss"] * op0.sum():
        res["reasons"].append(f"cleanup would damage the object ({(lost.sum()-white_gone)/op0.sum():.0%} lost outside the bottom band)")
        a1 = alpha
    if unremoved_shadow is not None:
        res["reasons"].append("ground shadow that cannot be removed safely")
    if white_left:
        res["reasons"].append(white_left)
    ped = detect_pedestal(rgb, a1, band=T["ped_band"] if cat in ("creature", "character") else T["ped_band_objects"]) \
        if cat in ("marker", "prop", "creature", "eyecandy", "character") else None
    if ped:
        res["reasons"] = [r for r in res["reasons"] if not r.startswith("background removal failed")]
        res["reasons"].append(ped)
    # one main object
    lab, sizes = components(a1 >= 128)
    if len(sizes) > 2 and not any(r.startswith("background removal failed") for r in res["reasons"]):
        s = np.sort(sizes[1:])[::-1]
        single = re.search(r"\b(single|one)\b", prompt.lower()) is not None
        if s[1] >= T["multi_ratio"] * s[0]:
            (res["reasons"] if single else res["flags"]).append(f"several separate objects ({len(s)} parts, second is {s[1]/s[0]:.0%} of the first)")

    # ---- stage 4: resize
    rgba = np.dstack([np.clip(rgb, 0, 255).astype(np.uint8), a1])
    if bbox(a1 > 0) is None:
        res["reasons"].append("nothing left after cleanup")
        return finish(res, st, coll, p["subject"], save=False)
    final = resize_to_spec(rgba, cat)
    final.save(dest, optimize=True)

    # ---- stage 5: final QC (re-detect on the result)
    fa = np.asarray(final).astype(np.float32)
    frgb, falpha = fa[..., :3], fa[..., 3].astype(np.uint8)
    if falpha[0, 0] or falpha[0, -1] or falpha[-1, 0] or falpha[-1, -1] or (falpha == 0).mean() < 0.05:
        res["reasons"].append("background not transparent")
    sz = check_size(final, cat)
    if sz:
        res["reasons"].append("size spec: " + sz)
    if unremoved_shadow is None:
        onwhite = Image.new("RGB", final.size, (255, 255, 255)); onwhite.paste(final, (0, 0), final)
        reg, _ = shadow_region(frgb, falpha, rembg_alpha(onwhite))
        if reg is not None and reg.sum() > 0:
            ok, _why = verify_shadow(reg, frgb, falpha)
            if ok and reg.sum() > 0.01 * (falpha >= 128).sum():
                res["reasons"].append("ground shadow still detected after cleanup")
    res["phash"] = phash(final)
    return finish(res, st, coll, p["subject"])


def finish(res, st, coll, subject, save=True):
    # near-duplicate of another live item / approved image of the same subject
    if res.get("phash"):
        others = []
        if st:
            for o in st["items"].values():
                if o["id"] != res["id"] and o.get("subject") == subject and o.get("collection") == coll \
                        and o.get("stage") in ("pending-qc", "passed", "accepted") and o.get("phash"):
                    others.append((o["id"], o["phash"]))
        others += approved_hashes(coll, subject, res.get("variant"), res.get("seed")) if coll else []
        for oid, ph in others:
            d = hamming(res["phash"], ph)
            if d <= T["dup_hamming"]:
                res["reasons"].append(f"near-duplicate of {os.path.basename(str(oid))} (hash distance {d})")
                break
    res["stage"] = "failed" if res["reasons"] else "pending-qc"
    return res


def inspect_final(path, cat=None, text=True):
    """Re-run the defect detectors on a finished (transparent) image: what the publish gate (audit_page.py) checks on
    every candidate. Returns a list of problems (empty = clean)."""
    im = Image.open(path)
    if cat and CFG["categories"].get(cat, {}).get("cutout") is False:
        return []
    if im.mode != "RGBA":
        return ["no transparency (raw generator image?)"]
    a = np.asarray(im).astype(np.float32)
    rgb, alpha = a[..., :3], a[..., 3].astype(np.uint8)
    probs = []
    if alpha[0, 0] or alpha[0, -1] or alpha[-1, 0] or alpha[-1, -1]:
        probs.append("background not transparent")
    bb = bbox(alpha > 0)
    if bb is None:
        return ["blank"]
    h, w = alpha.shape
    if bb[0] < 1 or bb[1] < 1 or bb[2] > w - 1 or bb[3] > h - 1:
        probs.append("touches the canvas edge")
    if cat in CFG["categories"]:
        sz = check_size(im, cat)
        if sz and "canvas edge" not in sz:
            probs.append("size spec: " + sz)
    # categories can skip detectors that misfire on their shapes (ui: blank plates read as text, light panel centres
    # are white on purpose, the bottom bevel reads as a shadow); the QC gate still looks at every one
    skip = set(CFG["categories"].get(cat, {}).get("audit_skip", [])) if cat else set()
    onwhite = Image.new("RGB", im.size, (255, 255, 255)); onwhite.paste(im, (0, 0), im)
    rem = rembg_alpha(onwhite)
    reg, _ = shadow_region(rgb, alpha, rem)
    if "shadow" not in skip and reg is not None and reg.sum() > 0.01 * (alpha >= 128).sum() and verify_shadow(reg, rgb, alpha)[0]:
        probs.append("shadow ellipse")
    log = []
    _a, big = remove_enclosed_white(rgb, alpha, rem, [255, 255, 255], log)
    if "white" in skip:
        pass
    elif big:
        probs.append("white fill: " + big)
    elif any(x.startswith("enclosed background white removed") for x in log):
        probs.append("white fill (enclosed background-white gap)")
    if cat in ("marker", "prop", "creature", "eyecandy", "character"):
        ped = detect_pedestal(rgb, alpha, band=T["ped_band"] if cat in ("creature", "character") else T["ped_band_objects"])
        if ped:
            probs.append(ped)
    if text and "text" not in skip:
        sc = clip_scores(onwhite, "an object", [])
        if sc and sc.get("clip_text", 0) >= T["audit_text"]:
            probs.append(f'possible text (CLIP {sc["clip_text"]:.2f})')
    return probs


# ---------------------------------------------------------------- retries
def simplify(subject_text):
    parts = [x.strip() for x in subject_text.split(",") if x.strip()]
    short = ", ".join(parts[:2])
    words = short.split()
    return " ".join(words[:14])


def retry_prompt(item, sh, reasons):
    subj = simplify(sh.get("subjects", {}).get(item["subject"], item["subject"].replace("_", " ")))
    style = ""
    if "variants_from" in sh:
        vs = json.load(open(os.path.expanduser(sh["variants_from"])))["variants"]
        style = next((v.get("style", "") for v in vs if v["key"] == item["variant"]), "")
    elif "variants" in sh:
        style = next((v.get("style", "") for v in sh["variants"] if v["key"] == item["variant"]), "")
    hints = []
    text = " ".join(reasons).lower()
    for pat, hint in REASON_HINTS:
        if re.search(pat, text) and hint not in hints:
            hints.append(hint)
    if item.get("hint"):
        hints.insert(0, item["hint"])
    prompt = subj + (", " + style if style else "") + RETRY_SUFFIX + "".join(", " + x for x in hints)
    terms = [t.strip() for t in (sh.get("negative", "") + ", " + STRONG_NEG).split(",") if t.strip()]
    neg = ", ".join(dict.fromkeys(terms))
    return prompt, neg


def record(st, res):
    """Store a processed result; queue the one retry for a failed first attempt."""
    prev = st["items"].get(res["id"], {})
    res["attempt"] = prev.get("attempt", res.get("attempt", 1))
    res["parent"] = prev.get("parent", res.get("parent"))
    res["updated"] = time.strftime("%Y-%m-%d %H:%M")
    st["items"][res["id"]] = {**prev, **res}
    it = st["items"][res["id"]]
    if it["stage"] == "failed":
        fail_item(st, it, it["reasons"])
    return it


def fail_item(st, it, reasons, hint=None):
    it["stage"] = "failed"
    it["reasons"] = list(dict.fromkeys(it.get("reasons", []) + list(reasons)))
    if hint:
        it["hint"] = hint
    if it.get("attempt", 1) >= 2:
        it["final"] = True  # -> Failures section (with its parent)
        return
    if it.get("retry"):
        return
    sh = load_sheet(it["package"])
    prompt, neg = retry_prompt(it, sh, it["reasons"])
    seed = it["seed"] + RETRY_SEED_OFFSET
    w = h = None
    side = it["raw"][:-4] + ".json"
    if os.path.exists(side):
        sd = json.load(open(side)); w, h = sd.get("width"), sd.get("height")
    job = {"name": it["name"], "subject": it["subject"], "variant": it["variant"], "model": it.get("model", "flux"),
           "seed": seed, "prompt": prompt, "negative": neg, "width": w or sh.get("width", 768), "height": h or sh.get("height", 768)}
    rid = item_id(it["name"], {"subject": it["subject"], "variant": it["variant"], "seed": seed})
    it["retry"] = rid
    st["items"][rid] = {"id": rid, "package": it["package"], "name": it["name"], "category": it["category"],
                        "collection": it["collection"], "subject": it["subject"], "variant": it["variant"],
                        "model": job["model"], "seed": seed, "attempt": 2, "parent": it["id"], "stage": "retry-queued",
                        "prompt": prompt, "negative": neg, "job": job, "reasons": [], "flags": [], "edits": [],
                        "raw": os.path.join(OUT, it["name"], f'{it["subject"]}__{it["variant"]}__{job["model"]}__s{seed}.png'),
                        "updated": time.strftime("%Y-%m-%d %H:%M")}


# ---------------------------------------------------------------- commands
def cmd_ingest(a):
    for raw in a.raw:
        raw = os.path.abspath(os.path.expanduser(raw))
        p = parse_raw(raw)
        if not p:
            print("skip (not a generator file):", raw); continue
        with locked():
            st = load_state()
            existing = next((o for o in st["items"].values() if o.get("raw") == raw), None)
        key = (existing or {}).get("package") or (a.package if a.package and a.package != "retry" else None) or key_for_raw(raw)
        if not key:
            print("skip (no package):", raw); continue
        prompt = (existing or {}).get("prompt") or prompt_for(key, {**p, "raw": raw})[0]
        res = process(raw, key, prompt, st=st, use_clip=not a.no_clip)
        if getattr(a, "collection", None):
            res["collection"] = a.collection
        if existing:
            res["id"] = existing["id"]
        with locked():
            st = load_state()
            if existing:
                res["attempt"] = existing.get("attempt", 1); res["parent"] = existing.get("parent")
            it = record(st, res)
            save_state(st)
        print(f'{it["id"]}: {it["stage"]}' + (f' — {"; ".join(it["reasons"])}' if it["reasons"] else "")
              + (f' (retry queued: {it["retry"]})' if it.get("retry") and it["stage"] == "failed" and not it.get("final") else ""), flush=True)


def approved_subjects():
    """(collection title, subject) pairs that already have an approved ~/AI/assets file."""
    try:
        art = json.load(open(ART_STATE))
    except (OSError, ValueError):
        return set()
    return {(c.get("title"), os.path.basename(os.path.dirname(os.path.expanduser(x))))
            for c in art["collections"] for x in c.get("approved", []) or []}


def cmd_retry_sheet(a):
    done = approved_subjects()
    with locked():
        st = load_state()
        # a retry for a subject the owner already approved is wasted generation time
        for o in st["items"].values():
            if o.get("stage") == "retry-queued" and (o.get("collection"), o.get("subject")) in done:
                o["stage"] = "superseded"; o["dismissed"] = True; o["updated"] = time.strftime("%Y-%m-%d %H:%M")
        save_state(st)
    jobs = [o["job"] for o in st["items"].values() if o.get("stage") == "retry-queued" and not os.path.exists(o["raw"])]
    # a queued retry whose image already exists (gen.py ran but the hook did not) is ingested by night.sh's sweep
    if not jobs:
        if os.path.exists(a.path):
            os.remove(a.path)
        print(0); return
    json.dump({"name": "pipeline", "jobs": jobs}, open(a.path, "w"), indent=1)
    print(len(jobs))


def cmd_sweep(a):
    """Ingest queued retries whose image exists but was never processed (e.g. interrupted hook)."""
    with locked():
        st = load_state()
    todo = [o["raw"] for o in st["items"].values() if o.get("stage") == "retry-queued" and os.path.exists(o["raw"])]
    if todo:
        a.raw = todo; a.package = None
        cmd_ingest(a)


def cmd_backfill(a):
    cmd_ingest(a)


def cmd_dry_run(a):
    os.makedirs(a.out, exist_ok=True)
    rows = []
    for raw in a.raw:
        raw = os.path.abspath(os.path.expanduser(raw))
        p = parse_raw(raw)
        key = a.package or key_for_raw(raw)
        prompt = prompt_for(key, {**p, "raw": raw})[0]
        res = process(raw, key, prompt, st=None, out_dir=a.out, use_clip=not a.no_clip)
        rows.append(res)
        print(f'{res["id"]}: {res["stage"]}' + (f' — {"; ".join(res["reasons"])}' if res["reasons"] else "")
              + (f' | edits: {"; ".join(res["edits"])}' if res["edits"] else "") + (f' | flags: {"; ".join(res["flags"])}' if res["flags"] else ""), flush=True)
    json.dump(rows, open(os.path.join(a.out, "dry-run.json"), "w"), indent=1)
    from qc import before_after_sheet
    print("sheet:", before_after_sheet(rows, os.path.join(a.out, "before-after")))


CLEANUP_LIST = os.path.expanduser("~/AI/out/approved-cleanup.txt")
CLEANED = os.path.expanduser("~/AI/out/cleaned")
ASSETS = os.path.expanduser("~/AI/assets")
BACKUP = os.path.expanduser("~/AI/assets-backup")


def approved_target(raw):
    """The approved ~/AI/assets file that a raw generator image (an approved-cleanup.txt line) stands for."""
    p = parse_raw(raw)
    if not p:
        return None
    if raw.startswith(ASSETS + os.sep):
        return raw
    cat = os.path.basename(os.path.dirname(raw)).removeprefix("pkg-")
    want = os.path.join(ASSETS, cat, p["subject"], f'{p["variant"]}_s{p["seed"]}.png')
    try:
        art = json.load(open(ART_STATE))
    except (OSError, ValueError):
        return None
    approved = {os.path.expanduser(x) for c in art["collections"] for x in c.get("approved", []) or []}
    return want if want in approved else None


def cmd_land_cleaned(a):
    """Stage for owner-requested edits of APPROVED art: the 23:00 Kontext cleanup writes ~/AI/out/cleaned/<pkg>/<raw
    name>.png; each new result is cut out, cleaned, resized to its category spec and audited (inspect_final). A clean
    result replaces the approved asset in place (the previous file goes to ~/AI/assets-backup/<same path>); a result
    with problems is left alone and reported (pipeline-state.json "landed")."""
    import shutil
    try:
        lines = [l.rstrip("\n") for l in open(CLEANUP_LIST) if l.strip()]
    except OSError:
        return
    with locked():
        st = load_state()
    landed = st.setdefault("landed", {})
    work = os.path.join(OUT, "pipeline", "landing")
    for line in lines:
        raw = os.path.expanduser(line.split("|", 1)[0])
        done = os.path.join(CLEANED, os.path.basename(os.path.dirname(raw)), os.path.basename(raw))
        if not os.path.exists(done):
            continue
        mt = os.path.getmtime(done)
        if landed.get(done, {}).get("mtime") == mt:
            continue
        rec = {"mtime": mt, "when": time.strftime("%Y-%m-%d %H:%M")}
        if a.baseline:
            landed[done] = {**rec, "status": "baseline"}
            continue
        target = approved_target(raw)
        key = key_for_raw(raw)
        if not target or not key:
            rec.update(status="skipped", reason="no approved ~/AI/assets file for it (or unknown package)")
        else:
            cat, _coll = category_of(key)
            res = process(done, key, "", st=None, out_dir=work, use_clip=False)
            probs = [r for r in res["reasons"] if not r.startswith("near-duplicate")]
            if not probs and os.path.exists(res["processed"]):
                probs = inspect_final(res["processed"], cat, text=False)
            if probs:
                rec.update(status="rejected", target=target, reason="; ".join(probs), result=res["processed"])
            else:
                bk = os.path.join(BACKUP, os.path.relpath(target, ASSETS))
                if os.path.exists(bk):
                    bk = bk[:-4] + time.strftime("-%Y%m%d%H%M") + ".png"
                os.makedirs(os.path.dirname(bk), exist_ok=True)
                if os.path.exists(target):
                    shutil.copy2(target, bk)
                shutil.copy(res["processed"], target)
                rec.update(status="landed", target=target, backup=bk, edits=res["edits"])
        landed[done] = rec
        print(f'cleanup {os.path.basename(done)}: {rec["status"]}' + (f' ({rec.get("reason")})' if rec.get("reason") else ""), flush=True)
    with locked():
        st2 = load_state()
        st2["landed"] = {**st2.get("landed", {}), **landed}
        save_state(st2)


def cmd_status(a):
    st = load_state()
    from collections import Counter
    c = Counter(o.get("stage") for o in st["items"].values())
    print(dict(c))
    for o in st["items"].values():
        if o.get("stage") in ("pending-qc", "retry-queued"):
            print(f'  {o["stage"]:12} {o["id"]}')


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("ingest"); s.add_argument("raw", nargs="+"); s.add_argument("--package"); s.add_argument("--no-clip", action="store_true"); s.set_defaults(f=cmd_ingest)
    s = sub.add_parser("backfill"); s.add_argument("raw", nargs="+"); s.add_argument("--no-clip", action="store_true")
    s.add_argument("--package", help="package key (default: found from the folder and subject)")
    s.add_argument("--collection", help="art-state collection title overriding categories.json"); s.set_defaults(f=cmd_backfill)
    s = sub.add_parser("retry-sheet"); s.add_argument("path"); s.set_defaults(f=cmd_retry_sheet)
    s = sub.add_parser("sweep"); s.add_argument("--no-clip", action="store_true"); s.set_defaults(f=cmd_sweep)
    s = sub.add_parser("dry-run"); s.add_argument("raw", nargs="+"); s.add_argument("--out", required=True); s.add_argument("--package"); s.add_argument("--no-clip", action="store_true"); s.set_defaults(f=cmd_dry_run)
    s = sub.add_parser("status"); s.set_defaults(f=cmd_status)
    s = sub.add_parser("land-cleaned"); s.add_argument("--baseline", action="store_true", help="mark existing cleanups as handled (no replacement)")
    s.set_defaults(f=cmd_land_cleaned)
    a = ap.parse_args()
    a.f(a)


if __name__ == "__main__":
    sys.path.insert(0, ROOT)
    main()
