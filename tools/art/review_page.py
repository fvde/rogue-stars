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
import re, base64, glob, html, io, json, os, sys
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


OUT = os.path.expanduser(os.environ.get("ART_OUT", "~/AI/out"))  # ART_OUT: a scratch copy for tests
REG = os.path.join(OUT, "art-rounds.json")


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
    sub = name.split("__")[0] if "__" in name else os.path.basename(os.path.dirname(path))
    return re.sub(r"_v\d+$", "", sub)  # concept variants (alien_chronoslug_v1..v4) count as one subject


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
    """Progress of every package in tools/art/night.sh's order, counted per package from exactly the jobs gen.py
    enumerates for it (several packages share an output folder, and variants_from packages render one variant per
    subject), the package generating right now (from ~/AI/out/night-slice.log) and the art pipeline's counts."""
    import re, sys
    root = os.path.expanduser("~/Projects/rogue-stars/tools/art")
    sys.path.insert(0, root)
    try:
        import gen
        order = re.search(r'ORDER="\$\{ORDER:-([^}]*)\}"', open(os.path.join(root, "night.sh")).read()).group(1).split()
    except Exception:
        return ""
    out_root = os.path.expanduser("~/AI/out")
    # where night.sh is: the last "== pass N <pkg> done" line -> the next package in ORDER is generating now
    cur_pass, last_done = 1, None
    try:
        for line in open(os.path.join(out_root, "night-slice.log"), errors="ignore"):
            m = re.match(r"== pass (\d) (\S+) done", line)
            if m:
                cur_pass, last_done = int(m.group(1)), m.group(2)
            elif line.startswith("== ") and "switch to cleanup" in line:
                pass
    except OSError:
        pass
    if last_done in order:
        k = order.index(last_done) + 1
        if k >= len(order):
            cur_pass, k = cur_pass + 1, 0
        current = order[k] if cur_pass <= 2 else None
    else:
        current = order[0]
    rows, finished = [], []
    for name in order:
        try:
            d = json.load(open(os.path.join(root, "packages", name + ".json")))
        except Exception:
            continue
        if cur_pass == 2:
            d = dict(d, seeds=[s + 50 for s in d.get("seeds", [1])])
        try:
            jobs = gen.enumerate_jobs(d, out_root)
        except Exception:
            continue
        folder = os.path.join(out_root, d.get("name", "pkg-" + name))
        exp = len(jobs)
        done = sum(os.path.exists(os.path.join(v.get("folder", folder), f"{s}__{k}__{m}__s{sd}.png")) for s, k, m, sd, _p, v in jobs)
        per = 95 if d.get("width", 1024) <= 512 else 110 if d.get("width", 1024) <= 768 else 200
        if done >= exp and name != current:
            finished.append(name)
        else:
            rows.append((name, done, exp, (exp - done) * per))
    shown, later = rows[:11], rows[11:]
    total_left = sum(r[2] - r[1] for r in shown); secs = sum(r[3] for r in shown)
    later_n = sum(r[2] - r[1] for r in later)
    trs = "".join(f'<tr{" class=now" if n == current else ""}><td>{html.escape(n)}{" <b>generating now</b>" if n == current else ""}</td><td>{dn}/{ex}</td>'
                  f'<td><div class="bar"><i style="width:{100*dn//max(ex,1)}%"></i></div></td></tr>' for n, dn, ex, _ in shown)
    try:
        import collections
        ps = json.load(open(os.path.join(out_root, "pipeline-state.json")))["items"].values()
        c = collections.Counter(o.get("stage") for o in ps)
        pipe = (f' Pipeline: {c.get("pending-qc", 0)} waiting for the QC gate, {c.get("retry-queued", 0)} retries queued, '
                f'{c.get("passed", 0)} passed.')
    except Exception:
        pipe = ""
    fin = f'<p class="note">Finished (pass {cur_pass}): {html.escape(", ".join(finished))}.</p>' if finished else ""
    if not rows:
        return f'<section><h2>Generation queue</h2><p class="note">Pass {cur_pass}: every package is generated.{pipe}</p>{fin}</section>'
    return (f'<section><h2>Generation queue</h2><p class="note">Pass {cur_pass}, in night.sh order (top = generating now). About {total_left} images left in this list (roughly {secs/3600:.1f} h); {later_n} more are queued behind it. '
            f'Counts are this pass\'s jobs of each package.{pipe}</p><table class="q"><tr><th>Package</th><th>Done</th><th></th></tr>{trs}</table>{fin}</section>')


PIPELINE_TEXT = [
    ("Queue order", "Images are generated in the queue's order, package by package and subject by subject."),
    ("Prompt check", "Each new image is checked against its prompt: plain white background, nothing cut off by the image edge, one main object, and a local image-text model (CLIP) compares it with the package's other subjects and looks for written text."),
    ("Defect removal", "The background is cut away, then ground shadows, loose shadow pieces, white gaps (between legs, inside rings, cog holes) and specks are removed automatically. Every automatic edit is checked; if it would damage the art, the image fails instead. Pedestals, bases and ground slabs are never edited away: the image fails."),
    ("Resize", "The clean image is scaled to its category's game size (icons 512 px with a 458 px object, creatures 512 px bottom-aligned at 470 px, markers/props/tiles trimmed at their in-game size)."),
    ("Final check", "Transparent background, no defect left, size correct, not a near-copy of another image of the same subject, not blank, not touching the edge."),
    ("Look check", "Images that pass are looked at by the hourly coordinator (does it match the prompt, does it look right) and passed or failed."),
    ("One retry", "A failed image is generated once more with a shorter, simpler prompt and stronger 'no pedestal, no base, no ground, no shadow, plain white background, single object' wording and a new seed."),
    ("Result", "Only images that passed every step appear below as candidates. If the retry fails as well, both attempts are shown in the Failures section with their prompts and reasons; there is no third automatic attempt."),
]


def pipeline_section():
    lis = "".join(f"<li><b>{html.escape(k)}</b> — {html.escape(v)}</li>" for k, v in PIPELINE_TEXT)
    return f'<section id="art-pipeline"><h2>Art pipeline</h2><ol class="pipe">{lis}</ol></section>'


def failures_section():
    """Items whose retry failed too (~/AI/out/pipeline-state.json, final), each with both attempts, prompts and reasons."""
    try:
        items = json.load(open(os.path.join(OUT, "pipeline-state.json")))["items"]
    except (OSError, ValueError):
        return ""
    finals = [o for o in items.values() if o.get("final") and not o.get("dismissed")]
    if not finals:
        return ""
    cards = []
    for r in sorted(finals, key=lambda o: o.get("updated", "")):
        first = items.get(r.get("parent") or "", {})
        figs = []
        for n, it in (("1st attempt", first), ("retry", r)):
            img = it.get("processed") if it.get("processed") and os.path.exists(it["processed"]) else it.get("raw")
            src = thumb(img) if img and os.path.exists(img) else ""
            figs.append(f'<figure class="fail" id="{fig_id("fail", img or it.get("id", n))}"><img src="{src}" alt="{html.escape(it.get("id", ""))}">'
                        f'<figcaption><b>{n}</b> {html.escape(it.get("id", ""))}<br><i>prompt:</i> {html.escape(it.get("prompt", ""))}'
                        f'<br><i>why:</i> {html.escape("; ".join(it.get("reasons", [])))}</figcaption></figure>')
        cards.append(f'<div class="failpair"><h3 class="bad">{html.escape(r.get("subject", ""))} <small>{html.escape(r.get("collection") or "")}</small></h3><div class="grid2">{"".join(figs)}</div></div>')
    return (f'<section id="failures"><h2>Failures <small>{len(finals)}</small></h2><p class="note">These failed twice (first attempt and the one '
            f'simplified retry). Nothing more is generated for them automatically; comment to give new directions.</p>{"".join(cards)}</section>')


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
table.q{{border-collapse:collapse;width:100%;font-size:13px}} table.q th,table.q td{{text-align:left;padding:5px 8px;border-bottom:1px solid #3a3040;vertical-align:top}} table.q th{{color:var(--mut)}} tr.now td{{color:var(--new)}} tr.now b{{font-size:11px;margin-left:6px}}
.bar{{height:8px;background:#3a3040;border-radius:4px;min-width:120px}} .bar i{{display:block;height:8px;background:var(--ok);border-radius:4px}} .ts{{overflow-x:auto}} code{{background:#2a2230;padding:1px 5px;border-radius:4px}}
ol.pipe{{margin:6px 0 0;padding-left:22px;color:var(--fg)}} ol.pipe li{{margin:3px 0}} ol.pipe b{{color:var(--new)}}
h3.bad{{color:#ff6e6e}} figure.fail{{border-color:#ff6e6e}} .grid2{{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:10px}}
figcaption{{font-size:12px;color:var(--mut);margin-top:4px;word-break:break-word}}
</style>
<main><h1>Art review</h1>{pipeline_section()}{queue_table()}<p class="note">Green = approved; orange = new candidates. Every image carries its round (R1, R2, …; R0 = approved earlier). Comment on a single image to pick or reject it, or name a round ("R3: all rejected") to clear it.</p>
{''.join(body)}{failures_section()}</main>"""
    open(os.path.expanduser(sys.argv[2]), "w").write(page)
    print("ok", sum(len(files(c.get('approved'))) for c in state['collections']), "approved,",
          sum(len(animated(c.get('animated'))) + len(animated(c.get('animated_approved'))) for c in state['collections']), "animated,",
          round(os.path.getsize(os.path.expanduser(sys.argv[2])) / 1e6, 2), "MB")


if __name__ == "__main__":
    main()
