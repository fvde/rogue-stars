"""Cut-out tool (anim pilot, technique 2): splits an alien sprite into rig parts along its cartoon outlines and writes
part PNGs + rig.json for CutoutRig. Regions = connected fills between the black outlines; each part lists its regions,
outline pixels go to the nearest part (within OUTLINE px) else the body. Areas hidden behind parts come from a fill image
(a Kontext edit with the part removed) or a flat colour."""
import json, sys, os
import numpy as np
from PIL import Image
from scipy import ndimage

OUTLINE = 8

def run(spec, out_dir):
    im = np.array(Image.open(os.path.expanduser(spec["src"])).convert("RGBA"))
    H, W = im.shape[:2]
    a = im[..., 3]; lum = im[..., :3].mean(-1)
    fill = (a > 128) & (lum > 70)
    lab, n = ndimage.label(fill)
    owner = np.full((H, W), "body", dtype=object)
    owner[a == 0] = ""
    drop = np.isin(lab, spec.get("drop", []))
    parts = spec["parts"]
    region_mask = {}
    for p in parts:
        if p["name"] == "body": continue
        m = np.isin(lab, p.get("regions", []))
        if "circle" in p:
            cx, cy, r = p["circle"]; yy, xx = np.mgrid[:H, :W]
            m |= ((xx - cx) ** 2 + (yy - cy) ** 2 <= r * r) & (a > 0)
        region_mask[p["name"]] = m
    # outline pixels (opaque, not a fill region) → nearest part within OUTLINE px
    dist = {k: ndimage.distance_transform_edt(~m) for k, m in region_mask.items()}
    outline = (a > 0) & ~fill
    best = np.full((H, W), np.inf); bestk = np.full((H, W), "", dtype=object)
    for k, d in dist.items():
        sel = d < best; best[sel] = d[sel]; bestk[sel] = k
    for k, m in region_mask.items(): owner[m] = k
    yy = np.mgrid[:H, :W][0]
    for p in parts:  # outline above this row stays with the body (a leg must not take the body's bottom edge)
        if "outline_below" in p: best[(bestk == p["name"]) & (yy < p["outline_below"])] = np.inf
    o = outline & (best <= spec.get("outline", OUTLINE)) & ~np.isin(lab, [])
    owner[o & (owner == "body")] = bestk[o & (owner == "body")]
    # drop shadows (we draw our own)
    owner[drop] = ""
    for p in parts:
        if "circle" in p:  # a circle part takes everything inside it (eye with its ring)
            owner[region_mask[p["name"]]] = p["name"]
    os.makedirs(out_dir, exist_ok=True)
    fill_img = np.array(Image.open(os.path.expanduser(spec["fill"])).convert("RGB").resize((W, H))) if spec.get("fill") else None
    defs = []
    for p in parts:
        name = p["name"]
        m = owner == name
        px = im.copy()
        if name == "body":
            hidden = np.zeros((H, W), bool)
            for q in parts:
                if q.get("hide_fill"): hidden |= ndimage.binary_dilation(owner == q["name"], iterations=2)
            hidden &= ~m
            if fill_img is not None:
                f = fill_img.astype(int)
                solid = (np.abs(f - 255).sum(-1) > 40) & hidden
                if p.get("fill_within_alpha"): solid &= ndimage.binary_dilation(a > 0, iterations=4)
                px[solid, :3] = f[solid]; px[solid, 3] = 255
                m = m | solid
            if p.get("diffuse_under"):  # smooth (Laplace) fill from the surrounding body pixels: soft shading, no detail
                du = ndimage.binary_dilation(np.isin(owner, p["diffuse_under"]), iterations=4) & (a > 0)
                known = m & ~du
                rgb = px[..., :3].astype(float); rgb[~known] = 0
                w = known.astype(float)
                # coarse-to-fine: blur known colours and weights, divide (normalized convolution), repeat at shrinking sigma
                acc = np.zeros_like(rgb)
                for sig in (40, 20, 10, 5):
                    num = np.stack([ndimage.gaussian_filter(rgb[..., c] * w, sig) for c in range(3)], -1)
                    den = ndimage.gaussian_filter(w, sig)[..., None]
                    est = num / np.maximum(den, 1e-6)
                    acc = np.where(du[..., None], est, acc) if sig == 40 else np.where(du[..., None] & (den > 0.35), est, acc)
                px[du, :3] = np.clip(acc[du], 0, 255).astype(np.uint8); px[du, 3] = 255; m = m | du
            if p.get("flat_fill") is not None:
                ff = np.isin(owner, p["flat_fill_under"]) 
                px[ff, :3] = p["flat_fill"]; px[ff, 3] = 255; m = m | ff
        px[~m, 3] = 0
        if p.get("extend_up"):  # leg tops hidden under the body: repeat the top rows upward so a turn never shows a gap
            ys, xs = np.nonzero(m); top = ys.min()
            for x in range(xs.min(), xs.max() + 1):
                col = np.nonzero(m[:, x])[0]
                if len(col) == 0: continue
                t = col.min(); src = px[min(t + 3, H - 1), x]
                for y in range(max(0, t - p["extend_up"]), t + 3): px[y, x] = src; m[y, x] = True
        ys, xs = np.nonzero(m)
        x0, x1, y0, y1 = max(0, xs.min() - 2), min(W, xs.max() + 3), max(0, ys.min() - 2), min(H, ys.max() + 3)
        Image.fromarray(px[y0:y1, x0:x1]).save(os.path.join(out_dir, name + ".png"))
        defs.append({"name": name, "role": p.get("role", name), "parent": p.get("parent", ""), "cx": (x0 + x1) / 2, "cy": (y0 + y1) / 2,
                     "px": p["pivot"][0], "py": p["pivot"][1], "order": p["order"]})
    # parents before children
    ordered, seen = [], set()
    while len(ordered) < len(defs):
        for d in defs:
            if d["name"] not in seen and (d["parent"] in ("", None) or d["parent"] in seen): ordered.append(d); seen.add(d["name"])
    rig = {"id": spec["id"], "originX": spec["origin"][0], "originY": spec["origin"][1], "parts": ordered}
    json.dump(rig, open(os.path.join(out_dir, "rig.json"), "w"), indent=1)
    # review sheet: parts exploded
    prev = Image.new("RGBA", (W * 2, H), (60, 70, 80, 255))
    prev.alpha_composite(Image.open(os.path.expanduser(spec["src"])).convert("RGBA"), (0, 0))
    for d in ordered:
        sp = Image.open(os.path.join(out_dir, d["name"] + ".png"))
        dx = (d["cx"] - W / 2) * 0.25
        prev.alpha_composite(sp, (int(W + d["cx"] - sp.width / 2 + dx), int(d["cy"] - sp.height / 2)))
    prev.save(os.path.join(os.path.expanduser(spec["review"])))
    print(spec["id"], [(d["name"], Image.open(os.path.join(out_dir, d["name"] + ".png")).size) for d in ordered])

if __name__ == "__main__":
    run(json.load(open(sys.argv[1])), sys.argv[2])
