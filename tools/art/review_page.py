#!/usr/bin/env python3
"""Build the art review page from a state file (owner's rules: show only the CURRENT round of candidates, and next to
them every APPROVED asset of the same collection, so consistency can be judged; every image is its own element so
review comments anchor to one image).

    arch -arm64 ~/AI/ComfyUI/.venv/bin/python tools/art/review_page.py ~/AI/out/art-state.json out.html

State file: {"collections": [{"title", "note", "approved": [paths/globs], "candidates": [paths/globs],
"rejected": [paths]}]}. Paths may use ~ and globs. Needs Pillow (ComfyUI venv).
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


def main():
    state = json.load(open(os.path.expanduser(sys.argv[1])))
    rejected = set(os.path.expanduser(p) for c in state["collections"] for p in c.get("rejected", []))
    body = []
    for c in state["collections"]:
        appr = files(c.get("approved"))
        cand = [f for f in files(c.get("candidates")) if f not in rejected and f not in appr]
        if not appr and not cand:
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
                sec.append(f'<figure class="{cls}"><img src="{thumb(f)}" alt="{html.escape(label(f))}">'
                           f'<figcaption>{html.escape(label(f))}</figcaption></figure>')
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
figure{{margin:0;background:var(--card);border-radius:10px;padding:6px;border:2px solid transparent}}
figure.ok{{border-color:var(--ok)}}
figure img{{width:100%;aspect-ratio:1;object-fit:contain;border-radius:6px;
  background:repeating-conic-gradient(#d8d8d8 0 25%,#f4f4f4 0 50%) 0 0/16px 16px}}
figcaption{{font-size:12px;color:var(--mut);margin-top:4px;word-break:break-word}}
</style>
<main><h1>Art review</h1><p class="note">Green = approved; orange = new candidates. Comment on a single image to pick or reject it. Older rounds are no longer shown.</p>
{''.join(body)}</main>"""
    open(os.path.expanduser(sys.argv[2]), "w").write(page)
    print("ok", sum(len(files(c.get('approved'))) for c in state['collections']), "approved")


if __name__ == "__main__":
    main()
