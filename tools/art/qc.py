#!/usr/bin/env python3
"""Human-level QC gate of the art pipeline (stage 6). The hourly coordinator looks at the contact sheet of the images
waiting for review and passes or fails each one (runs in the ComfyUI venv):

    PY="arch -arm64 ~/AI/ComfyUI/.venv/bin/python"
    $PY tools/art/qc.py sheet                       # -> ~/AI/out/pipeline/qc-sheet-1.png, -2.png ... (12 per page)
    $PY tools/art/qc.py list                        # pending ids with prompt and flags
    $PY tools/art/qc.py pass <id> [<id> ...]        # -> candidate on the review page (art-state.json collection)
    $PY tools/art/qc.py fail <id> "reason" [--hint "extra words for the retry prompt"]
    $PY tools/art/qc.py dismiss <id>                # take a failure off the review page's Failures section

A fail on a first attempt queues the one retry (simplified prompt, stronger negatives, new seed; night.sh runs it before
continuing the queue). A fail on a retry is final: both attempts go to the review page's Failures section.
Each sheet cell shows the processed image on a dark background and on a checkerboard, its id, the prompt, the automatic
edits and any pre-filter flags (CLIP rank / possible text).
"""
import argparse, json, os, sys, textwrap, time
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pipeline as P  # noqa: E402

SHEETS = os.path.join(P.OUT, "pipeline")
DARK = (20, 16, 22)


def font(size):
    for f in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc"):
        if os.path.exists(f):
            return ImageFont.truetype(f, size)
    return ImageFont.load_default(size=size)


def checker(w, h, sq=12):
    im = Image.new("RGB", (w, h), (244, 244, 244))
    d = ImageDraw.Draw(im)
    for y in range(0, h, sq):
        for x in range(0, w, sq):
            if (x // sq + y // sq) % 2:
                d.rectangle([x, y, x + sq - 1, y + sq - 1], fill=(214, 214, 214))
    return im


def tile(path, size, bg):
    base = checker(size, size) if bg == "checker" else Image.new("RGB", (size, size), bg)
    if not path or not os.path.exists(path):
        ImageDraw.Draw(base).text((8, size // 2), "no image", fill=(200, 80, 80), font=font(14))
        return base
    im = Image.open(path)
    im.thumbnail((size - 8, size - 8), Image.LANCZOS)
    if im.mode == "RGBA":
        base.paste(im, ((size - im.width) // 2, (size - im.height) // 2), im)
    else:
        base.paste(im.convert("RGB"), ((size - im.width) // 2, (size - im.height) // 2))
    return base


def wrap(d, xy, text, f, fill, width_chars, max_lines):
    x, y = xy
    lines = []
    for para in text.split("\n"):
        lines += textwrap.wrap(para, width_chars) or [""]
    for ln in lines[:max_lines]:
        d.text((x, y), ln, fill=fill, font=f)
        y += f.size + 3
    return y


def qc_sheets(items, prefix, per_page=12, cols=3):
    """Pending items: [dark | checker] + id, prompt, edits, flags."""
    S, PAD, TXT = 230, 10, 150
    cw, ch = 2 * S + 3 * PAD, S + TXT + 2 * PAD
    paths = []
    f_id, f_t = font(16), font(12)
    for page in range(0, len(items), per_page):
        chunk = items[page:page + per_page]
        rows = (len(chunk) + cols - 1) // cols
        sheet = Image.new("RGB", (cols * cw, rows * ch), (40, 34, 44))
        d = ImageDraw.Draw(sheet)
        for i, it in enumerate(chunk):
            x, y = (i % cols) * cw, (i // cols) * ch
            sheet.paste(tile(it.get("processed"), S, DARK), (x + PAD, y + PAD))
            sheet.paste(tile(it.get("processed"), S, "checker"), (x + 2 * PAD + S, y + PAD))
            ty = y + PAD + S + 6
            tag = "RETRY " if it.get("attempt", 1) >= 2 else ""
            d.text((x + PAD, ty), f"{tag}{it['id']}", fill=(255, 179, 71), font=f_id)
            ty += 21
            ty = wrap(d, (x + PAD, ty), "prompt: " + it.get("prompt", ""), f_t, (225, 215, 205), 78, 5)
            extra = "; ".join(it.get("edits", []))
            if extra:
                ty = wrap(d, (x + PAD, ty), "auto: " + extra, f_t, (150, 200, 150), 78, 2)
            if it.get("flags"):
                wrap(d, (x + PAD, ty), "check: " + "; ".join(it["flags"]), f_t, (255, 120, 120), 78, 2)
        out = f"{prefix}-{page // per_page + 1}.png"
        sheet.save(out)
        paths.append(out)
    return paths


def before_after_sheet(rows, prefix, per_page=10):
    """Dry-run sheet: raw | processed on dark | processed on checker | decision text."""
    S, PAD, TW = 200, 8, 420
    cw, ch = 3 * S + 4 * PAD + TW, S + 2 * PAD
    f_id, f_t = font(15), font(12)
    paths = []
    for page in range(0, len(rows), per_page):
        chunk = rows[page:page + per_page]
        sheet = Image.new("RGB", (cw, len(chunk) * ch), (40, 34, 44))
        d = ImageDraw.Draw(sheet)
        for i, r in enumerate(chunk):
            y = i * ch
            sheet.paste(tile(r["raw"], S, (255, 255, 255)), (PAD, y + PAD))
            proc = r.get("processed") if os.path.exists(r.get("processed", "")) else None
            sheet.paste(tile(proc, S, DARK), (2 * PAD + S, y + PAD))
            sheet.paste(tile(proc, S, "checker"), (3 * PAD + 2 * S, y + PAD))
            x = 4 * PAD + 3 * S
            ok = r["stage"] != "failed"
            d.text((x, y + PAD), ("PASS -> pending-qc  " if ok else "FAIL -> retry  ") + r["id"], fill=(76, 194, 90) if ok else (255, 110, 110), font=f_id)
            ty = y + PAD + 22
            if r["reasons"]:
                ty = wrap(d, (x, ty), "why: " + "; ".join(r["reasons"]), f_t, (255, 150, 150), 66, 4)
            if r["edits"]:
                ty = wrap(d, (x, ty), "auto: " + "; ".join(r["edits"]), f_t, (150, 200, 150), 66, 5)
            if r.get("flags"):
                ty = wrap(d, (x, ty), "flags: " + "; ".join(r["flags"]), f_t, (255, 200, 120), 66, 2)
            m = r.get("metrics", {})
            if "clip_rank" in m:
                wrap(d, (x, ty), f'CLIP rank {m["clip_rank"]}/{m["clip_n"]}, text {m.get("clip_text")}', f_t, (170, 160, 150), 66, 1)
        out = f"{prefix}-{page // per_page + 1}.png"
        sheet.save(out)
        paths.append(out)
    return paths


def pending(st):
    return sorted((o for o in st["items"].values() if o.get("stage") == "pending-qc"), key=lambda o: o.get("updated", ""))


def add_candidate(it):
    """Stage 8: the processed image becomes a candidate of its art-state.json collection (created if missing)."""
    st = json.load(open(P.ART_STATE))
    title = it.get("collection") or it["package"]
    c = next((c for c in st["collections"] if c.get("title") == title), None)
    if c is None:
        c = {"title": title, "approved": [], "candidates": []}
        st["collections"].append(c)
    c.setdefault("candidates", [])
    if it["processed"] not in c["candidates"]:
        c["candidates"].append(it["processed"])
    tmp = P.ART_STATE + ".tmp"
    json.dump(st, open(tmp, "w"), indent=1)
    os.replace(tmp, P.ART_STATE)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("sheet")
    sub.add_parser("list")
    s = sub.add_parser("pass"); s.add_argument("ids", nargs="+")
    s = sub.add_parser("fail"); s.add_argument("id"); s.add_argument("reason"); s.add_argument("--hint")
    s = sub.add_parser("dismiss"); s.add_argument("ids", nargs="+")
    a = ap.parse_args()
    if a.cmd in ("sheet", "list"):
        st = P.load_state()
        items = pending(st)
        if a.cmd == "list":
            for it in items:
                print(f'{it["id"]}  [{it["category"]}]  {it.get("prompt", "")[:140]}' + (f'  FLAGS: {"; ".join(it["flags"])}' if it.get("flags") else ""))
            print(len(items), "pending")
            return
        os.makedirs(SHEETS, exist_ok=True)
        for f in os.listdir(SHEETS):
            if f.startswith("qc-sheet-"):
                os.remove(os.path.join(SHEETS, f))
        paths = qc_sheets(items, os.path.join(SHEETS, "qc-sheet")) if items else []
        print(len(items), "pending")
        for p in paths:
            print(p)
        return
    with P.locked():
        st = P.load_state()
        ids = a.ids if hasattr(a, "ids") else [a.id]
        for i in ids:
            it = st["items"].get(i)
            if not it:
                print("unknown id:", i); continue
            if a.cmd == "pass":
                if it.get("stage") != "pending-qc":
                    print(f"{i}: not pending ({it.get('stage')})"); continue
                it["stage"] = "passed"; it["qc"] = "pass"; it["updated"] = time.strftime("%Y-%m-%d %H:%M")
                add_candidate(it)
                print(f"{i}: passed -> candidate in '{it.get('collection')}'")
            elif a.cmd == "fail":
                if it.get("stage") not in ("pending-qc", "passed"):
                    print(f"{i}: not pending ({it.get('stage')})"); continue
                it["qc"] = "fail"; it["updated"] = time.strftime("%Y-%m-%d %H:%M")
                P.fail_item(st, it, ["QC: " + a.reason], hint=a.hint)
                print(f"{i}: failed" + (" (final -> Failures section)" if it.get("final") else f" -> retry {it.get('retry')} queued"))
            elif a.cmd == "dismiss":
                it["dismissed"] = True
                print(f"{i}: dismissed")
        P.save_state(st)


if __name__ == "__main__":
    main()
