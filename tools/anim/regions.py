import sys, numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
src, out = sys.argv[1], sys.argv[2]
im = np.array(Image.open(src).convert('RGBA')).astype(int)
a = im[..., 3]; lum = im[..., :3].mean(-1)
fill = (a > 128) & (lum > 70)
lab, n = ndimage.label(fill)
sizes = ndimage.sum(fill, lab, range(1, n + 1))
rng = np.random.default_rng(3)
cols = rng.integers(40, 255, (n + 1, 3)); cols[0] = 0
vis = cols[lab].astype(np.uint8)
img = Image.fromarray(vis); d = ImageDraw.Draw(img)
for i in range(1, n + 1):
    if sizes[i - 1] < 150: continue
    ys, xs = np.nonzero(lab == i)
    d.text((xs.mean() - 6, ys.mean() - 5), str(i), fill=(255, 255, 255))
img.save(out)
np.save(out.replace('.png', '.npy'), lab)
print(n, 'components;', sum(s >= 150 for s in sizes), 'big')
