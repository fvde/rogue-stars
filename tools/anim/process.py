"""Video frames → seamless loop → cut-out (original alpha, static stone) → sprite sheet (anim pilot, technique 4)."""
import glob, json, sys, os
import numpy as np
from PIL import Image, ImageFilter
frames_glob, src, out_png, step, K, scale = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), int(sys.argv[5]), float(sys.argv[6])
orig = Image.open(src).convert("RGBA"); W, H = orig.size
files = sorted(glob.glob(frames_glob))
vid = [Image.open(f).convert("RGB") for f in files][::step]
cw, ch = vid[0].size; ox, oy = (cw - W) // 2, (ch - H) // 2
vid = [np.array(v.crop((ox, oy, ox + W, oy + H))).astype(float) for v in vid]
o = np.array(orig).astype(float)
# animated region: the saturated purple glow inside the well, dilated and feathered; the stone stays the original art
hsv = np.array(orig.convert("RGB").convert("HSV")).astype(float)
h, s, v = hsv[..., 0] * 360 / 255, hsv[..., 1] / 255, hsv[..., 2] / 255
if len(sys.argv) > 7:  # disk:cx,cy,r — animate inside a circle (the portal's vortex), keep the ring
    cx, cy, r = map(float, sys.argv[7].split(":")[1].split(","))
    yy, xx = np.mgrid[:H, :W]
    glow = (xx - cx) ** 2 + (yy - cy) ** 2 <= r * r
    m = Image.fromarray((glow * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(4))
else:
    glow = (o[..., 3] > 128) & (h > 250) & (h < 320) & (s > 0.35) & (v > 0.45)
    m = Image.fromarray((glow * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(5))
mask = (np.array(m).astype(float) / 255)[..., None]
n = len(vid)
L = n - K
loop = [vid[i + K] for i in range(n - 2 * K)] + [vid[n - K + j] * (1 - (j + 1) / (K + 1)) + vid[j] * ((j + 1) / (K + 1)) for j in range(K)]
outs = []
for f in loop:
    rgb = o[..., :3] * (1 - mask) + f * mask
    im = Image.fromarray(np.concatenate([rgb, o[..., 3:4]], -1).clip(0, 255).astype(np.uint8), "RGBA")
    outs.append(im.resize((int(W * scale), int(H * scale)), Image.LANCZOS))
fw, fh = outs[0].size
cols = int(np.ceil(np.sqrt(len(outs) * fh / fw))); rows = int(np.ceil(len(outs) / cols))
sheet = Image.new("RGBA", (cols * fw, rows * fh), (0, 0, 0, 0))
for i, im in enumerate(outs): sheet.paste(im, ((i % cols) * fw, (i // cols) * fh))
sheet.save(out_png)
fps = 24 / step
json.dump({"cols": cols, "rows": rows, "frames": len(outs), "fps": fps}, open(out_png.replace(".png", ".json"), "w"))
# motion check: mean abs change between consecutive loop frames (inside the glow) incl. the wrap
d = [np.abs(np.array(outs[(i + 1) % len(outs)]).astype(float) - np.array(outs[i]).astype(float))[..., :3].mean() for i in range(len(outs))]
print(f"{len(outs)} frames {fw}x{fh} sheet {sheet.size} cols {cols} rows {rows} fps {fps}; frame deltas", [round(x, 2) for x in d])
os.makedirs(os.path.join(os.path.dirname(out_png), "loop_frames"), exist_ok=True)
for i, im in enumerate(outs): im.save(os.path.join(os.path.dirname(out_png), "loop_frames", f"p_{i:02d}.png"))
