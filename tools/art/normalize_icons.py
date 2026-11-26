#!/usr/bin/env python3
"""Make icon sprites fill their canvas like the others (runs in the ComfyUI venv).

    normalize_icons.py <png> [<png> ...]   # or --size 512

Faint glow/halo pixels (alpha below 128) are dropped, the solid art is trimmed, scaled so its longer side is 458 px
(the same as the other 512 px icons) and centred. The original is kept next to it as <name>.orig.png the first time.
"""
import os, shutil, sys
import numpy as np
from PIL import Image

size = 512
fill = round(size * 0.895)
args = [a for a in sys.argv[1:] if not a.startswith("--")]
for f in args:
    f = os.path.expanduser(f)
    im = Image.open(f).convert("RGBA")
    a = np.asarray(im).copy()
    solid = a[:, :, 3] >= 128
    if not solid.any():
        continue
    ys, xs = np.where(solid)
    box = (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)
    if max(box[2] - box[0], box[3] - box[1]) >= fill - 12:
        continue  # the solid art already fills the canvas
    orig = f[:-4] + ".orig.png"
    if not os.path.exists(orig):
        shutil.copy(f, orig)
    a[~solid, 3] = 0
    crop = Image.fromarray(a).crop(box)
    s = fill / max(crop.size)
    crop = crop.resize((max(1, round(crop.width * s)), max(1, round(crop.height * s))), Image.LANCZOS)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(crop, ((size - crop.width) // 2, (size - crop.height) // 2), crop)
    out.save(f)
    print("normalized", f)
