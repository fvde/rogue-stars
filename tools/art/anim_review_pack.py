#!/usr/bin/env python3
"""Packs the frame sequences of AnimReviewCaptureTests into short looping WebPs for the art review page.

    arch -arm64 ~/AI/ComfyUI/.venv/bin/python tools/art/anim_review_pack.py <frames dir> [~/AI/out/anim-review]

Per item folder (f_000.png ...): the first LOOP frames are the loop; the EXTRA frames after them are cross-faded into the
start so it is seamless; premultiplied colour (the shaders blend premultiplied) is un-premultiplied; scaled to 220 px wide;
written as <name>.webp (transparent, 12 fps, lossy q55) - about 60-120 KB each.
"""
import os, sys
from multiprocessing import Pool
import numpy as np
from PIL import Image

LOOP, EXTRA, FPS, WIDTH, QUALITY = 24, 6, 12, 220, 55


def load(path):
    a = np.asarray(Image.open(path).convert("RGBA"), dtype=np.float32) / 255
    return a


def unpremultiply(a):
    alpha = a[..., 3:4]
    rgb = np.where(alpha > 1e-3, a[..., :3] / np.maximum(alpha, 1e-3), 0)
    return np.concatenate([np.clip(rgb, 0, 1), alpha], axis=-1)


def pack(folder, out):
    frames = [load(os.path.join(folder, f"f_{i:03d}.png")) for i in range(LOOP + EXTRA)]
    outs = []
    for i in range(LOOP):
        if i < EXTRA:  # the tail continues into the start: blend S[LOOP+i] -> S[i]
            w = i / EXTRA
            a = frames[LOOP + i] * (1 - w) + frames[i] * w
        else:
            a = frames[i]
        im = Image.fromarray((unpremultiply(a) * 255 + 0.5).astype(np.uint8), "RGBA")
        h = round(im.height * WIDTH / im.width)
        outs.append(im.resize((WIDTH, h), Image.LANCZOS))
    outs[0].save(out, "WEBP", save_all=True, append_images=outs[1:], duration=int(1000 / FPS), loop=0, quality=QUALITY, method=4, lossless=False)


def main():
    src = os.path.expanduser(sys.argv[1])
    dst = os.path.expanduser(sys.argv[2] if len(sys.argv) > 2 else "~/AI/out/anim-review")
    os.makedirs(dst, exist_ok=True)
    jobs = []
    for name in sorted(os.listdir(src)):
        folder = os.path.join(src, name)
        if os.path.isdir(folder) and os.path.exists(os.path.join(folder, f"f_{LOOP + EXTRA - 1:03d}.png")):
            jobs.append((folder, os.path.join(dst, name + ".webp")))
    with Pool(max(1, (os.cpu_count() or 2) - 1)) as pool:
        pool.starmap(pack, jobs)
    total = 0
    for _folder, out in jobs:
        total += os.path.getsize(out)
        print(os.path.basename(out), os.path.getsize(out) // 1024, "KB")
    print("total", round(total / 1e6, 2), "MB")


if __name__ == "__main__":
    main()
