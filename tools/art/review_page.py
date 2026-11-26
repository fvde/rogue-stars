#!/usr/bin/env python3
"""Build the art review page from a state file (owner's rules: show only the CURRENT round of candidates, and next to
them every APPROVED asset of the same collection, so consistency can be judged; every image is its own element so
review comments anchor to one image).

    arch -arm64 ~/AI/ComfyUI/.venv/bin/python tools/art/review_page.py ~/AI/out/art-state.json out.html

State file: {"collections": [{"title", "note", "approved": [paths/globs], "candidates": [paths/globs],
"animated": [globs of looping .webp/.gif previews], "animated_approved": [globs], "rejected": [paths]}]}. Paths may use ~ and
globs. Needs Pillow (ComfyUI venv).

Animated previews (short looping WebP/GIF, e.g. ~/AI/out/anim-review/*.webp from the game's shader animation capture) are
embedded as they are (never re-encoded, so they keep animating), one figure per item with its own id (so a comment anchors to
a single item); they are "new" unless listed under animated_approved. Every figure carries a stable id (slug of the file).
"""
import base64, glob, html, io, json, os, sys
from PIL import Image

THUMB = 220


def files(patterns):
    out = []
    for p in patterns or []:
        hits = sorted(glob.glob(os.path.expanduser(p)))
        out += [h for h in hits if h.lower().endswith(".png") and not h.endswith("review.png")]
    return out


def animated(patterns):
    out = []
    for p in patterns or []:
        hits = sorted(glob.glob(os.path.expanduser(p)))
        out += [h for h in hits if h.lower().endswith((".webp", ".gif"))]
    return out


def embed(path):
    """An animated file as a data URI, untouched (a thumbnail would flatten it to one frame)."""
    mime = "gif" if path.lower().endswith(".gif") else "webp"
    return f"data:image/{mime};base64," + base64.b64encode(open(path, "rb").read()).decode()


def fig_id(prefix, path):
    slug = "".join(ch if ch.isalnum() else "-" for ch in os.path.splitext(os.path.basename(path))[0]).strip("-").lower()
    return f"{prefix}-{slug}"


def thumb(path):
    im = Image.open(path)
    im.thumbnail((THUMB + 60, THUMB + 60))
    buf = io.BytesIO()
    im.convert("RGBA" if im.mode in ("RGBA", "LA", "P") else "RGB").save(buf, "WEBP", quality=72); mime = "webp"
    return f"data:image/{mime};base64," + base64.b64encode(buf.getvalue()).decode()


def label(path):
    parts = path.split(os.sep)
    name = os.path.splitext(parts[-1])[0]
    subject = parts[-2]
    if "__" in name:  # raw generator output: subject__variant__model__seed
        s, v, _m, seed = name.split("__")
        return f"{s} · {v} · {seed}"
    return f"{subject} · {name}"


REG = os.path.expanduser("~/AI/out/art-rounds.json")


def load_registry():
    try:
        return json.load(open(REG))
    except (OSError, ValueError):
        return {"current": 0, "rounds": {"0": "earlier"}, "files": {}}


def assign_rounds(reg, approved, candidates):
    """Every file keeps the round it was FIRST shown in (so a comment can name its round and a whole round can be cleared).
    Unregistered approved files count as round 0 ("earlier"); unregistered candidates start a new round together."""
    import datetime
    for f in approved:
        reg["files"].setdefault(f, 0)
    fresh = [f for f in candidates if f not in reg["files"]]
    if fresh:
        reg["current"] += 1
        reg["rounds"][str(reg["current"])] = datetime.datetime.now().strftime("%d %b %H:%M")
        for f in fresh:
            reg["files"][f] = reg["current"]
    return reg


def round_tag(reg, f):
    r = reg["files"].get(f, 0)
    return "R0" if r == 0 else f"R{r}"


def main():
    state = json.load(open(os.path.expanduser(sys.argv[1])))
    reg = load_registry()
    _all_appr = [f for c in state["collections"] for f in files(c.get("approved"))]
    _rej = set(os.path.expanduser(p) for c in state["collections"] for p in c.get("rejected", []))
    _all_cand = [f for c in state["collections"] for f in files(c.get("candidates")) if f not in _rej and f not in _all_appr]
    reg = assign_rounds(reg, _all_appr, _all_cand)
    json.dump(reg, open(REG, "w"), indent=1)
    rejected = set(os.path.expanduser(p) for c in state["collections"] for p in c.get("rejected", []))
    body = []
    for c in state["collections"]:
        appr = files(c.get("approved"))
        cand = [f for f in files(c.get("candidates")) if f not in rejected and f not in appr]
        cand.sort(key=lambda f: reg["files"].get(f, 0))
        appr.sort(key=lambda f: reg["files"].get(f, 0))
        anim_ok = [f for f in animated(c.get("animated_approved")) if f not in rejected]
        anim_new = [f for f in animated(c.get("animated")) if f not in rejected and f not in anim_ok]
        if not appr and not cand and not anim_ok and not anim_new:
            continue
        sec = [f'<section><h2>{html.escape(c["title"])}</h2>']
        if c.get("note"):
            sec.append(f'<p class="note">{html.escape(c["note"])}</p>')
        for kind, items in (("Approved (in the game or going in)", appr), ("New candidates — pick", cand)):
            if not items:
                continue
            cls = "ok" if kind.startswith("Approved") else "new"
            sec.append(f'<h3 class="{cls}">{kind} <small>{len(items)}</small></h3><div class="grid">')
            for f in items:
                tag = round_tag(reg, f)
                sec.append(f'<figure class="{cls}" id="{fig_id("img", f)}"><img src="{thumb(f)}" alt="{html.escape(label(f))}">'
                           f'<figcaption><b class="rt">{tag}</b> {html.escape(label(f))}</figcaption></figure>')
            sec.append("</div>")
        for kind, items in (("Animated, approved", anim_ok), ("Animated, new — pick", anim_new)):
            if not items:
                continue
            cls = "ok" if "approved" in kind else "new"
            sec.append(f'<h3 class="{cls}">{kind} <small>{len(items)}</small></h3><div class="grid anim">')
            for f in items:
                name = os.path.splitext(os.path.basename(f))[0]
                sec.append(f'<figure class="{cls} anim" id="{fig_id("anim", f)}"><img src="{embed(f)}" alt="{html.escape(name)}" loading="lazy">'
                           f'<figcaption>{html.escape(name)}</figcaption></figure>')
            sec.append("</div>")
        sec.append("</section>")
        body.append("".join(sec))
    page = f"""<title>Slice Art Picks</title>
<meta name="claude:viewport-fit-sides" content="cover">
<style>
:root{{--bg:#141016;--fg:#f1e8dc;--mut:#a89a8c;--ok:#4cc25a;--new:#ffb347;--card:#1f1822;color-scheme:dark}}
body{{background:var(--bg);color:var(--fg);font:15px/1.5 system-ui,sans-serif}}
main{{max-width:1200px;margin:0 auto;padding-block:20px 48px;padding-left:max(16px,env(safe-area-inset-left,0px));padding-right:max(16px,env(safe-area-inset-right,0px))}}
h2{{margin:34px 0 4px}} h3{{font-size:15px;margin:14px 0 8px}} h3.ok{{color:var(--ok)}} h3.new{{color:var(--new)}} h3 small{{color:var(--mut)}}
.note{{color:var(--mut);margin:0}}
.grid{{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:10px}}
.grid.anim{{grid-template-columns:repeat(auto-fill,minmax(200px,1fr))}}
figure.anim img{{aspect-ratio:auto;max-height:340px}}
figure{{margin:0;background:var(--card);border-radius:10px;padding:6px;border:2px solid transparent}}
figure.ok{{border-color:var(--ok)}} figure.new.anim{{border-color:var(--new)}}
figure img{{width:100%;aspect-ratio:1;object-fit:contain;border-radius:6px;
  background:repeating-conic-gradient(#d8d8d8 0 25%,#f4f4f4 0 50%) 0 0/16px 16px}}
b.rt{{color:var(--new);margin-right:4px}} figure.ok b.rt{{color:var(--ok)}}
figcaption{{font-size:12px;color:var(--mut);margin-top:4px;word-break:break-word}}
</style>
<main><h1>Art review</h1><p class="note">Green = approved; orange = new candidates. Every image carries its round (R1, R2, …; R0 = approved earlier). Comment on a single image to pick or reject it, or name a round ("R3: all rejected") to clear it.</p>
{''.join(body)}</main>"""
    open(os.path.expanduser(sys.argv[2]), "w").write(page)
    print("ok", sum(len(files(c.get('approved'))) for c in state['collections']), "approved,",
          sum(len(animated(c.get('animated'))) + len(animated(c.get('animated_approved'))) for c in state['collections']), "animated,",
          round(os.path.getsize(os.path.expanduser(sys.argv[2])) / 1e6, 2), "MB")


if __name__ == "__main__":
    main()
