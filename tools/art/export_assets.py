#!/usr/bin/env python3
"""Turn a generated package folder into game-ready sprites (runs in the ComfyUI venv: rembg + Pillow).

    arch -arm64 ~/AI/ComfyUI/.venv/bin/python tools/art/export_assets.py ~/AI/out/pkg-aliens [--size 512] [--out ~/AI/assets]

For every <subject>__<variant>__<model>__s<seed>.png:
  - background removed (rembg, isnet-anime model; the prompts ask for a plain white background),
  - trimmed to the opaque pixels, centred on a square transparent canvas with 6% padding, scaled to --size px,
  - written to <out>/<category>/<subject>/<variant>_s<seed>.png  (category = folder name without "pkg-").
Also writes <out>/<category>/review.png: every sprite on a checkerboard (stray background shows up) with labels.
Existing outputs are skipped, so it can run after each package and again at the end.
"""
import argparse, os
from PIL import Image, ImageDraw
from rembg import remove, new_session
from PIL import ImageDraw, ImageFilter, ImageChops

ap = argparse.ArgumentParser()
ap.add_argument("folder")
ap.add_argument("--size", type=int, default=512)
ap.add_argument("--out", default=os.path.expanduser("~/AI/assets"))
ap.add_argument("--model", default="isnet-anime")
ap.add_argument("--fullframe", action="store_true", help="backgrounds/key art: keep the full image (no cutout), long side --size*2")
a = ap.parse_args()

folder = os.path.expanduser(a.folder)
category = os.path.basename(folder.rstrip("/")).removeprefix("pkg-")
session = new_session(a.model)
done = []
for f in sorted(os.listdir(folder)):
    if not f.endswith(".png") or f.endswith(".cut.png") or f.count("__") != 3:
        continue
    subject, variant, _model, seed = f[:-4].split("__")
    dst_dir = os.path.join(a.out, category, subject)
    dst = os.path.join(dst_dir, f"{variant}_{seed}.png")
    if not os.path.exists(dst) and a.fullframe:
        os.makedirs(dst_dir, exist_ok=True)
        im = Image.open(os.path.join(folder, f)).convert("RGB")
        im.thumbnail((a.size * 2, a.size * 2), Image.LANCZOS)
        im.save(dst, optimize=True)
        print("asset", dst)
    elif not os.path.exists(dst):
        os.makedirs(dst_dir, exist_ok=True)
        src = Image.open(os.path.join(folder, f)).convert("RGBA")
        im = remove(src, session=session)
        # rembg alone makes white/pale/glossy parts of an object see-through (e.g. white icons, highlights). The
        # prompts ask for a plain white background, so the real background is the near-white area CONNECTED TO THE
        # BORDER: flood-fill it from the edges; everything else is the object and stays opaque. rembg's alpha is only
        # used inside that background area, to keep soft anti-aliased edges.
        fill = src.convert("RGB")
        w, h = fill.size
        for xy in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1), (w // 2, 0), (w // 2, h - 1), (0, h // 2), (w - 1, h // 2)):
            if fill.getpixel(xy) != (255, 0, 255) and min(fill.getpixel(xy)) > 200:
                ImageDraw.floodfill(fill, xy, (255, 0, 255), thresh=40)
        px = fill.load(); m = Image.new("L", (w, h), 255); mp = m.load()
        for y in range(h):
            for x in range(w):
                if px[x, y] == (255, 0, 255): mp[x, y] = 0
        m = m.filter(ImageFilter.MaxFilter(3))
        alpha = ImageChops.lighter(m, im.getchannel("A"))
        # Drop faint halo pixels left by the white background.
        alpha = alpha.point(lambda v: 0 if v < 24 else v)
        im = src.copy()
        im.putalpha(alpha)
        box = im.getbbox()
        if box is None:
            print("empty after cutout:", f)
            continue
        im = im.crop(box)
        side = int(max(im.size) * 1.12)
        canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        canvas.paste(im, ((side - im.width) // 2, (side - im.height) // 2), im)
        canvas.resize((a.size, a.size), Image.LANCZOS).save(dst, optimize=True)
        print("asset", dst)
    done.append((f"{subject} {variant}", dst))

if done:
    cell, cols = 220, 6
    rows = (len(done) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * cell, rows * (cell + 18)), (255, 255, 255, 255))
    checker = Image.new("RGBA", (cell, cell))
    d = ImageDraw.Draw(checker)
    for y in range(0, cell, 16):
        for x in range(0, cell, 16):
            d.rectangle([x, y, x + 15, y + 15], fill=(200, 200, 200, 255) if (x + y) // 16 % 2 else (240, 240, 240, 255))
    draw = ImageDraw.Draw(sheet)
    for i, (label, path) in enumerate(done):
        x, y = (i % cols) * cell, (i // cols) * (cell + 18)
        sheet.paste(checker, (x, y))
        sp = Image.open(path).resize((cell, cell), Image.LANCZOS)
        sheet.paste(sp, (x, y), sp)
        draw.text((x + 4, y + cell + 2), label[:34], fill=(0, 0, 0, 255))
    review = os.path.join(a.out, category, "review.png")
    sheet.convert("RGB").save(review, quality=85)
    print("review", review)
