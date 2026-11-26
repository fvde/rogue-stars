#!/usr/bin/env python3
"""Publish gate for the art review page: checks the exact set of images review_page.py will show and fails on anything
the owner should never see (runs in the ComfyUI venv):

    arch -arm64 ~/AI/ComfyUI/.venv/bin/python tools/art/audit_page.py ~/AI/out/art-state.json

Exit 1 with a list of problems, exit 0 when clean. Always renders a contact sheet of EVERYTHING the page shows, section by
section, the way the owner sees it (checkerboard, green = approved, orange = candidate, red = problem):
~/AI/out/pipeline/page-audit-1.png, -2.png ... review_page.py runs this before writing and refuses to write on failure
unless --force is passed.

Errors:
  - approved/candidates given as globs (collections must list explicit files; candidates come from qc.py pass)
  - non-canonical file names (*.orig.png, *.preclean.png, ...; ~/AI/assets files must be <variant>_s<seed>.png) or files
    outside the expected folders (candidates: ~/AI/out/processed only)
  - approved entries whose file is missing
  - the same image (perceptual hash) shown more than once anywhere on the page / near-identical candidates of a subject
  - a subject that is approved and still has visible candidates (unless the collection lists it under "alternatives")
  - the same subject in two collections
  - candidates with a detected defect (shadow ellipse, white fill, possible text, pedestal, touching the edge) or not
    matching their category's size spec (pipeline.inspect_final)
"""
import glob, json, os, re, sys
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pipeline as P  # noqa: E402

HOME = os.path.expanduser("~")
CAND_ROOTS = [os.path.join(P.OUT, "processed") + os.sep]
APPR_ROOTS = [HOME + "/AI/assets/", HOME + "/Projects/rogue-stars/client/RogueStars/Assets/Art/Resources/Art/",
              os.path.join(P.OUT, "processed") + os.sep, HOME + "/AI/out/pkg-", HOME + "/AI/out/cleaned/"]
CACHE = os.path.join(P.OUT, "pipeline", "audit-cache.json")
SHEET_DIR = os.path.join(P.OUT, "pipeline")
COLL_CAT = {coll: cat for cat, coll in P.CFG["packages"].values()}


def is_glob(p):
    return any(ch in p for ch in "*?[")


def category_for(path, title, items_by_proc):
    it = items_by_proc.get(path)
    return it["category"] if it else COLL_CAT.get(title)


def audit(state, secs, sheet=True):
    import review_page as R
    errors, flagged = [], {}

    def err(msg, *paths):
        errors.append(msg)
        for p in paths:
            flagged.setdefault(p, []).append(msg)

    for c in state["collections"]:
        for kind in ("approved", "candidates"):
            for p in c.get(kind, []) or []:
                if is_glob(p):
                    err(f'{c["title"]}: {kind} uses a glob ({p}); list explicit files')
                    continue
                f = os.path.expanduser(p)
                if kind == "approved" and not os.path.exists(f):
                    err(f'{c["title"]}: approved file missing: {f}')
    try:
        items = P.load_state()["items"]
    except Exception:
        items = {}
    by_proc = {o["processed"]: o for o in items.values() if o.get("processed")}
    try:
        cache = json.load(open(CACHE))
    except (OSError, ValueError):
        cache = {}

    shown = []  # (title, kind, path)
    subj_coll = {}
    for c, appr, cand, _ao, _an in secs:
        title = c["title"]
        alts = c.get("alternatives")
        appr_subj = {R.subject_of(f) for f in appr}
        for kind, files in (("approved", appr), ("candidate", cand)):
            for f in files:
                shown.append((title, kind, f))
                name = os.path.basename(f)
                if not R.canonical(f):
                    err(f"{title}: non-canonical file name {f}", f)
                roots = CAND_ROOTS if kind == "candidate" else APPR_ROOTS
                if not any(f.startswith(r) for r in roots):
                    err(f"{title}: {kind} outside the expected folders: {f}", f)
                sub = R.subject_of(f)
                subj_coll.setdefault(sub, set()).add(title)
                if kind == "candidate" and sub in appr_subj and not (alts is True or (isinstance(alts, list) and sub in alts)):
                    err(f"{title}: '{sub}' is approved but still has a visible candidate {name}", f)
    for sub, colls in subj_coll.items():
        if len(colls) > 1:
            err(f"subject '{sub}' appears in several sections: {', '.join(sorted(colls))}",
                *[f for t, _k, f in shown if R.subject_of(f) == sub])
    # the same picture twice anywhere on the page
    hashes = []
    for title, kind, f in shown:
        key = f"{f}|{os.path.getmtime(f) if os.path.exists(f) else 0}"
        h = cache.get("h|" + key)
        if h is None:
            try:
                h = P.phash(Image.open(f)); cache["h|" + key] = h
            except OSError:
                continue
        for (t2, k2, f2, h2) in hashes:
            if P.hamming(h, h2) <= P.T["dup_hamming"]:
                err(f"same image twice: {f2} ({t2}, {k2}) and {f} ({title}, {kind})", f, f2)
                break
        hashes.append((title, kind, f, h))
    # candidates: defects and size spec
    for title, kind, f in shown:
        if kind != "candidate" or not os.path.exists(f):
            continue
        cat = category_for(f, title, by_proc)
        key = f"d|{f}|{os.path.getmtime(f)}|{cat}"
        probs = cache.get(key)
        if probs is None:
            probs = P.inspect_final(f, cat)
            cache[key] = probs
        for pr in probs:
            err(f"{title}: candidate {os.path.basename(f)}: {pr}", f)
    os.makedirs(SHEET_DIR, exist_ok=True)
    json.dump(cache, open(CACHE, "w"))
    sheets = render(secs, flagged) if sheet else []
    return errors, sheets


def render(secs, flagged, cols=10, cell=150, max_h=6000):
    """Everything the page shows, per section, on a checkerboard; problems get a red frame and a number."""
    from qc import checker, font
    for f in glob.glob(os.path.join(SHEET_DIR, "page-audit-*.png")):
        os.remove(f)
    blocks = []
    f_h, f_s = font(18), font(11)
    for c, appr, cand, _ao, _an in secs:
        items = [("approved", f) for f in appr] + [("candidate", f) for f in cand]
        rows = (len(items) + cols - 1) // cols
        im = Image.new("RGB", (cols * cell, 30 + rows * (cell + 16)), (20, 16, 22))
        d = ImageDraw.Draw(im)
        d.text((6, 5), f'{c["title"]}  ({len(appr)} approved, {len(cand)} candidates)', fill=(241, 232, 220), font=f_h)
        for i, (kind, f) in enumerate(items):
            x, y = (i % cols) * cell, 30 + (i // cols) * (cell + 16)
            t = checker(cell - 6, cell - 6, 10)
            try:
                s = Image.open(f); s.thumbnail((cell - 10, cell - 10))
                if s.mode in ("RGBA", "LA", "P"):
                    s = s.convert("RGBA"); t.paste(s, ((cell - 6 - s.width) // 2, (cell - 6 - s.height) // 2), s)
                else:
                    t.paste(s.convert("RGB"), ((cell - 6 - s.width) // 2, (cell - 6 - s.height) // 2))
            except OSError:
                pass
            col = (255, 80, 80) if f in flagged else (76, 194, 90) if kind == "approved" else (255, 179, 71)
            d.rectangle([x + 1, y + 1, x + cell - 2, y + cell - 2], outline=col, width=3)
            im.paste(t, (x + 3, y + 3))
            d.text((x + 3, y + cell), os.path.basename(os.path.dirname(f))[:12] + "/" + os.path.basename(f)[:14], fill=col, font=f_s)
        blocks.append(im)
    pages, cur, h = [], [], 0
    for b in blocks:
        if cur and h + b.height > max_h:
            pages.append(cur); cur, h = [], 0
        cur.append(b); h += b.height
    if cur:
        pages.append(cur)
    out = []
    for n, pg in enumerate(pages, 1):
        sheet = Image.new("RGB", (cols * cell, sum(b.height for b in pg)), (20, 16, 22))
        y = 0
        for b in pg:
            sheet.paste(b, (0, y)); y += b.height
        p = os.path.join(SHEET_DIR, f"page-audit-{n}.png")
        sheet.save(p)
        out.append(p)
    return out


def main():
    import review_page as R
    state = json.load(open(os.path.expanduser(sys.argv[1] if len(sys.argv) > 1 else P.ART_STATE)))
    errors, sheets = audit(state, R.sections(state))
    for s in sheets:
        print("sheet:", s)
    if errors:
        print(f"AUDIT FAILED ({len(errors)}):")
        for e in errors:
            print("  -", e)
        sys.exit(1)
    print("audit ok")


if __name__ == "__main__":
    main()
