#!/usr/bin/env python3
"""Accept art picks in one go (runs in the ComfyUI venv):

    accept.py <raw image> [<raw image> ...]

For each picked raw generator image: finds its collection in ~/AI/out/art-state.json, cuts out the transparent version
(export_assets.py for its package, plus normalize_icons.py for 512 px icon packages), adds the transparent file to the
collection's approved list and archives the raw one. The page builder then archives every other candidate of the same
subject on its next build (review_page.py reconcile), so the page never keeps accepted-and-rejected leftovers.
"""
import glob, json, os, re, subprocess, sys

ROOT = os.path.dirname(os.path.abspath(__file__))
STATE = os.path.expanduser("~/AI/out/art-state.json")
ASSETS = os.path.expanduser("~/AI/assets")
PY = sys.executable
state = json.load(open(STATE))


def run(*a):
    subprocess.run([PY, *a], check=False, stdout=subprocess.DEVNULL)


def collection_of(path):
    for c in state["collections"]:
        for pat in c.get("candidates", []) + c.get("approved", []):
            if path in glob.glob(os.path.expanduser(pat)):
                return c
    return None


for raw in sys.argv[1:]:
    raw = os.path.expanduser(raw)
    m = re.match(r".*/AI/out/(pkg-[^/]+)/([^/]+)__([A-Za-z0-9]+)__[^_]+__s(\d+)\.png$", raw)
    if not m:
        print("skip (not a raw generator file):", raw)
        continue
    pkg, sub, var, seed = m.groups()
    cat = pkg.removeprefix("pkg-")
    run(os.path.join(ROOT, "export_assets.py"), os.path.dirname(raw))
    out = f"{ASSETS}/{cat}/{sub}/{var}_s{seed}.png"
    if not os.path.exists(out):
        print("export failed:", raw)
        continue
    if cat in ("skills-v2", "skills-v3", "status-icons2", "summon-icons", "currencies"):
        run(os.path.join(ROOT, "normalize_icons.py"), out)
    c = collection_of(raw)
    if c is None:
        print("no collection for", raw)
        continue
    c.setdefault("approved", []).append(out)
    c.setdefault("rejected", []).append(raw)
    print("accepted", os.path.basename(out))
json.dump(state, open(STATE, "w"), indent=1)
