#!/usr/bin/env python3
"""Cut sprites out of their plain background into transparent PNGs (rembg; runs in the ComfyUI venv).

    arch -arm64 ~/AI/ComfyUI/.venv/bin/python tools/art/cutout.py <folder-or-png>... [--model isnet-anime]

Writes <name>.cut.png next to each input and trims the transparent margin (+8 px padding).
"""
import sys, os
from PIL import Image
from rembg import remove, new_session

args = [a for a in sys.argv[1:] if not a.startswith("--")]
model = sys.argv[sys.argv.index("--model") + 1] if "--model" in sys.argv else "isnet-anime"
if "--model" in sys.argv:
    args.remove(model)
session = new_session(model)
files = []
for a in args:
    if os.path.isdir(a):
        files += [os.path.join(a, f) for f in sorted(os.listdir(a)) if f.endswith(".png") and not f.endswith(".cut.png")]
    else:
        files.append(a)
for f in files:
    out = remove(Image.open(f).convert("RGBA"), session=session)
    box = out.getbbox()
    if box:
        l, t, r, b = box
        out = out.crop((max(0, l - 8), max(0, t - 8), min(out.width, r + 8), min(out.height, b + 8)))
    dst = f[:-4] + ".cut.png"
    out.save(dst)
    print("cut", dst, out.size)
