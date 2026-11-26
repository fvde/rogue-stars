"""Animation pilot recording → one MP4 + GIF per technique and a combined one (labels drawn with Pillow)."""
import glob, os, subprocess, sys, shutil
from PIL import Image, ImageDraw, ImageFont
frames_dir, out_dir = sys.argv[1], sys.argv[2]
FPS = 30
EDGES = [-12, -7.2, 0, 7.2, 12]
px = [int(round((e + 12) * 100)) for e in EDGES]
FONT = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 26)
SMALL = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 22)
TITLES = ["1 Worm: code spine bend", "2 Cut-out rigs (blob, biped)", "3 Shaders: flame + embers + glow, sway", "4 AI video flipbook (LTX-Video)"]
SLUGS = ["1_worm_spine", "2_cutout_rigs", "3_shader_props", "4_portal_flipbook"]
WORM = [(0, "idle"), (1.4, "move (hops)"), (3.1, "idle"), (3.6, "attack"), (4.7, "idle"), (5.2, "hit"), (5.9, "idle"), (6.4, "happy"), (7.8, "idle"), (8.2, "attack"), (9.3, "idle")]
ALIEN = [(0, "idle"), (1.4, "attack"), (2.9, "idle"), (3.3, "hit"), (4.1, "celebrate"), (6.5, "death"), (8.7, "idle")]
def at(tl, t): return [l for s, l in tl if t >= s][-1]
files = sorted(glob.glob(os.path.join(frames_dir, "f_*.png")))
tmp = os.path.join(out_dir, "_tmp"); shutil.rmtree(tmp, ignore_errors=True)
for k in range(5): os.makedirs(os.path.join(tmp, str(k)))
for i, f in enumerate(files):
    t = i / FPS
    im = Image.open(f).convert("RGB")
    W, H = im.size
    canvas = Image.new("RGB", (W, H + 50), (20, 24, 30)); canvas.paste(im, (0, 50))
    d = ImageDraw.Draw(canvas)
    for p in range(4):
        d.text((px[p] + 14, 10), TITLES[p], font=FONT, fill=(240, 240, 240))
    d.text((px[0] + 14, 62), at(WORM, t), font=SMALL, fill=(255, 220, 120))
    d.text((px[1] + 14, 62), at(ALIEN, t), font=SMALL, fill=(255, 220, 120))
    for p in range(4):
        canvas.crop((px[p], 0, px[p + 1], H + 50)).save(os.path.join(tmp, str(p), f"{i:03d}.png"))
    canvas.save(os.path.join(tmp, "4", f"{i:03d}.png"))
def enc(src, name, gif_w):
    pat = os.path.join(src, "%03d.png")
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(FPS), "-i", pat, "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18",
                    "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", os.path.join(out_dir, name + ".mp4")], check=True)
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(FPS), "-i", pat, "-vf",
                    f"fps=15,scale={gif_w}:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=200[p];[b][p]paletteuse=dither=sierra2_4a",
                    os.path.join(out_dir, name + ".gif")], check=True)
for p in range(4): enc(os.path.join(tmp, str(p)), SLUGS[p], min(px[p + 1] - px[p], 720))
enc(os.path.join(tmp, "4"), "0_combined", 1600)
shutil.rmtree(tmp)
print(sorted(os.listdir(out_dir)))
