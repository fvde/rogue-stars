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


def subject_of(path):
    """The thing a picture shows: raw 'alien_zib__S__flux__s1011.png' -> alien_zib; export '.../alien_zib/S_s1011.png' -> alien_zib."""
    name = os.path.splitext(os.path.basename(path))[0]
    if "__" in name:
        return name.split("__")[0]
    return os.path.basename(os.path.dirname(path))


def reconcile(state):
    """Owner's rule: once an image is approved, every other candidate of the same subject in that collection is rejected and
    archived (never shown again, files stay on disk). Returns the number of candidates archived; the state is updated in place."""
    archived = 0
    for c in state["collections"]:
        appr = files(c.get("approved"))
        done = {subject_of(f) for f in appr}
        rej = set(os.path.expanduser(p) for p in c.get("rejected", []))
        for f in files(c.get("candidates")):
            if f in rej or f in appr:
                continue
            if subject_of(f) in done:
                c.setdefault("rejected", []).append(f)
                archived += 1
    return archived


def queue_table():
    """Progress of every package in tools/art/night.sh's order: done / expected images and a rough ETA."""
    import re
    root = os.path.expanduser("~/Projects/rogue-stars/tools/art")
    try:
        order = re.search(r'ORDER="\$\{ORDER:-([^}]*)\}"', open(os.path.join(root, "night.sh")).read()).group(1).split()
    except Exception:
        return ""
    rows, total_left, secs = [], 0, 0
    for name in order:
        try:
            d = json.load(open(os.path.join(root, "packages", name + ".json")))
        except Exception:
            continue
        nv = len(d.get("variants", []) or [])
        if not nv and d.get("variants_from"):
            try:
                nv = len(json.load(open(os.path.expanduser(d["variants_from"])))["variants"])
            except Exception:
                nv = 1
        exp = len(d.get("subjects", {})) * len(d.get("seeds", [1])) * max(nv, 1)
        out = os.path.expanduser("~/AI/out/" + d.get("name", "pkg-" + name))
        done = len([f for f in glob.glob(out + "/*.png")]) if os.path.isdir(out) else 0
        done = min(done, exp)
        per = 95 if d.get("width", 1024) <= 512 else 110 if d.get("width", 1024) <= 768 else 200
        left = exp - done
        rows.append((name, done, exp, left * per))
    rows = [r for r in rows if r[2] - r[1] > 0]
    shown, later = rows[:11], rows[11:]
    total_left = sum(r[2] - r[1] for r in shown); secs = sum(r[3] for r in shown)
    later_n = sum(r[2] - r[1] for r in later)
    rows = [r[:3] for r in shown]
    if not rows:
        return ""
    trs = "".join(f'<tr><td>{html.escape(n)}</td><td>{dn}/{ex}</td><td><div class="bar"><i style="width:{100*dn//ex}%"></i></div></td></tr>' for n, dn, ex in rows)
    return (f'<section><h2>Generation queue</h2><p class="note">Next in line, in order. About {total_left} images left in this list (roughly {secs/3600:.1f} h); {later_n} more are queued behind it (parts, mounts, heroes and others). '
            f'Counts are raw images (all variants and seeds).</p><table class="q"><tr><th>Package</th><th>Done</th><th></th></tr>{trs}</table></section>')


def prompt_tables(state):
    out = []
    for t in state.get("prompt_tables", []):
        try:
            sub = json.load(open(os.path.expanduser(t["package"])))["subjects"]
        except Exception:
            continue
        names = {}
        try:
            for line in open(os.path.expanduser(t.get("doc", ""))):
                if line.startswith("| `"):
                    c = [x.strip() for x in line.strip().strip("|").split("|")]
                    names[c[0].strip("`")] = (c[1], c[2])
        except Exception:
            pass
        trs = "".join(f'<tr id="prompt-{html.escape(k)}"><td><code>{html.escape(k)}</code></td><td>{html.escape(names.get(k, ("", ""))[0])}</td><td>{html.escape(names.get(k, ("", ""))[1])}</td><td>{html.escape(v)}</td></tr>' for k, v in sub.items())
        out.append(f'<section><h2>{html.escape(t["title"])}</h2><p class="note">{html.escape(t.get("note", ""))}</p><div class="ts"><table class="q"><tr><th>id</th><th>name</th><th>effect</th><th>prompt subject</th></tr>{trs}</table></div></section>')
    return "".join(out)


def main():
    state = json.load(open(os.path.expanduser(sys.argv[1])))
    n_arch = reconcile(state)
    if n_arch:
        json.dump(state, open(os.path.expanduser(sys.argv[1]), "w"), indent=1)
        print("archived", n_arch, "candidates of already approved subjects")
    reg = load_registry()
    _all_appr = [f for c in state["collections"] for f in files(c.get("approved"))]
    _rej = set(os.path.expanduser(p) for c in state["collections"] for p in c.get("rejected", []))
    _all_cand = [f for c in state["collections"] for f in files(c.get("candidates")) if f not in _rej and f not in _all_appr]
    reg = assign_rounds(reg, _all_appr, _all_cand)
    json.dump(reg, open(REG, "w"), indent=1)
    rejected = set(os.path.expanduser(p) for c in state["collections"] for p in c.get("rejected", []))
    body = []
    for c in state["collections"]:
        appr = [f for f in files(c.get("approved")) if f not in rejected]
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
table.q{{border-collapse:collapse;width:100%;font-size:13px}} table.q th,table.q td{{text-align:left;padding:5px 8px;border-bottom:1px solid #3a3040;vertical-align:top}} table.q th{{color:var(--mut)}}
.bar{{height:8px;background:#3a3040;border-radius:4px;min-width:120px}} .bar i{{display:block;height:8px;background:var(--ok);border-radius:4px}} .ts{{overflow-x:auto}} code{{background:#2a2230;padding:1px 5px;border-radius:4px}}
figcaption{{font-size:12px;color:var(--mut);margin-top:4px;word-break:break-word}}
</style>
<main><h1>Art review</h1>{queue_table()}{prompt_tables(state)}<p class="note">Green = approved; orange = new candidates. Every image carries its round (R1, R2, …; R0 = approved earlier). Comment on a single image to pick or reject it, or name a round ("R3: all rejected") to clear it.</p>
{''.join(body)}</main>"""
    open(os.path.expanduser(sys.argv[2]), "w").write(page)
    print("ok", sum(len(files(c.get('approved'))) for c in state['collections']), "approved,",
          sum(len(animated(c.get('animated'))) + len(animated(c.get('animated_approved'))) for c in state['collections']), "animated,",
          round(os.path.getsize(os.path.expanduser(sys.argv[2])) / 1e6, 2), "MB")


if __name__ == "__main__":
    main()
