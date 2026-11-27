#!/usr/bin/env python3
"""Kit preview: compose the UI kit v2 sprites into full-screen mockups of the three
target screens (home, worm, battle) so the owner can review the kit assembled.

Layout data: tools/art/kit_layouts.json (transcribed from the approved boards
"New · home", "New · worm", "New · battle"; rects at 1080 px width).
Kit list, sizes and 9-slice borders: docs/design/ui-kit-v2.md (+ overrides in the json).

Kit source (first that applies):
  --no-kit                 everything drawn as the code-drawn fallback
  --kit DIR                DIR/<id>.png, DIR/<id>__<anything>.png or DIR/<id>/<file>.png
  (default)                ~/AI/out/art-state.json collection "UI kit v2"
                           (approved first, else the newest candidate);
                           --collection TITLE picks another collection.

Missing kit pieces are drawn like the boards (flat colours) with a dashed magenta
outline and the id label. A coverage summary is printed and written to coverage.json.

Run (repo root):
  arch -arm64 ~/AI/ComfyUI/.venv/bin/python tools/art/kit_preview.py --out DIR
  ... --variant start_button=s3101,s3105     render both START candidates side by side
  ... --alias badge_red=badge_dot:#ff3b3b    use kit piece OLD for id NEW (optional tint)
  ... --selftest                             layout ids vs ui-kit-v2.md, content sprites resolve
"""
import argparse
import glob
import json
import os
import re
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFont, ImageOps

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
LAYOUTS = os.path.join(HERE, "kit_layouts.json")
KIT_DOC = os.path.join(REPO, "docs", "design", "ui-kit-v2.md")
CONTENT = os.path.join(REPO, "client", "RogueStars", "Assets", "Art", "Resources", "Art")
STANDINS = os.path.join(HERE, "kit_standins")
FONT = os.path.join(HERE, "fonts", "LilitaOne-Regular.ttf")
ART_STATE = os.path.expanduser("~/AI/out/art-state.json")
SCREENS = ["home", "worm", "battle"]
MISSING = (255, 0, 255, 255)
SS = 3  # supersampling for code-drawn shapes


# ---------------------------------------------------------------- helpers

def color(c, alpha=None):
    """'#rrggbb' / '#rrggbbaa' -> RGBA tuple."""
    if isinstance(c, (list, tuple)):
        return tuple(c)
    c = c.lstrip("#")
    r, g, b = int(c[0:2], 16), int(c[2:4], 16), int(c[4:6], 16)
    a = int(c[6:8], 16) if len(c) == 8 else 255
    if alpha is not None:
        a = int(round(a * alpha))
    return (r, g, b, a)


_fonts = {}


def font(px):
    px = max(6, int(round(px)))
    if px not in _fonts:
        _fonts[px] = ImageFont.truetype(FONT, px)
    return _fonts[px]


def contain(img, w, h, cover=False):
    iw, ih = img.size
    s = max(w / iw, h / ih) if cover else min(w / iw, h / ih)
    nw, nh = max(1, round(iw * s)), max(1, round(ih * s))
    img = img.resize((nw, nh), Image.LANCZOS)
    if cover:
        l, t = (nw - w) // 2, (nh - h) // 2
        return img.crop((l, t, l + w, t + h)), 0, 0
    return img, (w - nw) // 2, (h - nh) // 2


def trim(img):
    bb = img.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    return img.crop(bb) if bb else img


def tint(img, c):
    r, g, b, _ = color(c)
    rgb = img.convert("RGB")
    rgb = ImageChops.multiply(rgb, Image.new("RGB", img.size, (r, g, b)))
    out = rgb.convert("RGBA")
    out.putalpha(img.getchannel("A"))
    return out


def with_alpha(img, a):
    if a is None or a >= 1:
        return img
    img = img.copy()
    img.putalpha(img.getchannel("A").point(lambda v: int(v * a)))
    return img


def desaturate(img, amount):
    grey = ImageOps.grayscale(img.convert("RGB")).convert("RGB")
    mixed = Image.blend(img.convert("RGB"), grey, amount).convert("RGBA")
    mixed.putalpha(img.getchannel("A"))
    return mixed


def nine_slice(img, src_b, dw, dh, dst_b):
    """src_b / dst_b = (l, t, r, b) in source / destination pixels."""
    iw, ih = img.size
    sl, st, sr, sb = src_b
    dl, dt, dr, db = dst_b
    # never let the corners overlap
    if dl + dr > dw:
        f = dw / (dl + dr); dl, dr = int(dl * f), dw - int(dl * f)
    if dt + db > dh:
        f = dh / (dt + db); dt, db = int(dt * f), dh - int(dt * f)
    out = Image.new("RGBA", (dw, dh), (0, 0, 0, 0))
    xs_s, xs_d = [0, sl, iw - sr, iw], [0, dl, dw - dr, dw]
    ys_s, ys_d = [0, st, ih - sb, ih], [0, dt, dh - db, dh]
    for yi in range(3):
        for xi in range(3):
            sx0, sx1, sy0, sy1 = xs_s[xi], xs_s[xi + 1], ys_s[yi], ys_s[yi + 1]
            dx0, dx1, dy0, dy1 = xs_d[xi], xs_d[xi + 1], ys_d[yi], ys_d[yi + 1]
            if sx1 <= sx0 or sy1 <= sy0 or dx1 <= dx0 or dy1 <= dy0:
                continue
            part = img.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.LANCZOS)
            out.alpha_composite(part, (dx0, dy0))
    return out


# ---------------------------------------------------------------- code-drawn shapes

def _shape_mask(kind, W, H, inset, radius, corners, points):
    """Mask of the shape inset by (l, t, r, b) px (supersampled space)."""
    m = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(m)
    l, t, r, b = inset
    box = (l, t, W - 1 - r, H - 1 - b)
    if box[2] <= box[0] or box[3] <= box[1]:
        return m
    if kind == "circle":
        d.ellipse(box, fill=255)
    elif kind == "hex":  # flat-top hexagon like the boards' clip-path
        x0, y0, x1, y1 = box
        w, h = x1 - x0, y1 - y0
        d.polygon([(x0 + .25 * w, y0 + .03 * h), (x0 + .75 * w, y0 + .03 * h), (x1, y0 + .5 * h),
                   (x0 + .75 * w, y1 - .03 * h), (x0 + .25 * w, y1 - .03 * h), (x0, y0 + .5 * h)], fill=255)
    elif kind == "diamond":
        x0, y0, x1, y1 = box
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        d.polygon([(cx, y0), (x1, cy), (cx, y1), (x0, cy)], fill=255)
    elif kind == "poly":
        d.polygon([(x * W, y * H) for x, y in points], fill=255)
    else:
        rad = max(0, radius - min(l, t))
        if rad > 0:
            d.rounded_rectangle(box, radius=rad, fill=255, corners=corners)
        else:
            d.rectangle(box, fill=255)
    return m


def _gradient(W, H, stops):
    """Vertical gradient; stops = list of colours or [colour, pos 0..1]."""
    cols, pos = [], []
    for i, s in enumerate(stops):
        if isinstance(s, list) and len(s) == 2 and not isinstance(s[0], int):
            cols.append(color(s[0])); pos.append(float(s[1]))
        else:
            cols.append(color(s)); pos.append(i / max(1, len(stops) - 1))
    col = Image.new("RGBA", (1, H))
    for y in range(H):
        f = y / max(1, H - 1)
        j = 0
        while j < len(pos) - 2 and f > pos[j + 1]:
            j += 1
        a, b = pos[j], pos[j + 1]
        t = 0 if b <= a else min(1, max(0, (f - a) / (b - a)))
        col.putpixel((0, y), tuple(int(cols[j][k] + (cols[j + 1][k] - cols[j][k]) * t) for k in range(4)))
    return col.resize((W, H))


def shape_patch(spec, w, h):
    """Code-drawn fallback / plain shape -> RGBA patch of w x h."""
    W, H = max(1, w * SS), max(1, h * SS)
    kind = spec.get("shape", "rrect")
    if kind == "none":
        return Image.new("RGBA", (max(1, w), max(1, h)), (0, 0, 0, 0))
    rad = spec.get("radius", 0)
    if isinstance(rad, list):
        corners = tuple(r > 0 for r in rad)
        rad = max(rad)
    else:
        corners = (True, True, True, True)
    rad = int(rad * SS)
    pts = spec.get("points")
    sides = spec.get("sides", "trbl")
    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    inset = [0, 0, 0, 0]
    # concentric borders, outermost first: [[colour, width], ...]
    borders = list(spec.get("borders", []))
    if spec.get("border"):
        borders.append([spec.get("border_color", "#1d1b33"), spec["border"]])
    for bc, bw in borders:
        m = _shape_mask(kind, W, H, inset, rad, corners, pts)
        out.paste(Image.new("RGBA", (W, H), color(bc)), (0, 0), m)
        bw = int(round(bw * SS))
        inset = [inset[0] + (bw if "l" in sides else 0), inset[1] + (bw if "t" in sides else 0),
                 inset[2] + (bw if "r" in sides else 0), inset[3] + (bw if "b" in sides else 0)]
    m = _shape_mask(kind, W, H, inset, rad, corners, pts)
    if "grad" in spec:
        fill = _gradient(W, H, spec["grad"])
    else:
        fill = Image.new("RGBA", (W, H), color(spec.get("fill", "#00000000")))
    # paste fill (keep its own alpha, e.g. translucent rails)
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    layer.paste(fill, (0, 0), m)
    if any(inset):
        hole = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        out.paste(hole, (0, 0), m)  # clear under the (maybe translucent) fill
    out.alpha_composite(layer)
    if "pattern" in spec:  # diagonal diamond lattice like the parchment: [colour, line width, spacing]
        pc, pw, sp = spec["pattern"]
        pw, sp = max(1, int(round(pw * SS))), max(2, int(round(sp * SS)))
        pat = Image.new("L", (W, H), 0)
        pd = ImageDraw.Draw(pat)
        for off in range(-H, W + H, sp):
            pd.line([(off, 0), (off + H, H)], fill=255, width=pw)
            pd.line([(off, 0), (off - H, H)], fill=255, width=pw)
        pl = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        pl.paste(Image.new("RGBA", (W, H), color(pc)), (0, 0), ImageChops.multiply(pat, m))
        out.alpha_composite(pl)
    if "inner" in spec:  # inset highlight ring: [colour, width]
        ic, iw = spec["inner"]
        iw = int(round(iw * SS))
        ring = ImageChops.subtract(m, _shape_mask(kind, W, H, [i + iw for i in inset], rad, corners, pts))
        ringl = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        ringl.paste(Image.new("RGBA", (W, H), color(ic)), (0, 0), ring)
        out.alpha_composite(ringl)
    if "lip" in spec:  # darker band at the bottom inside the fill: [colour, height]
        lc, lh = spec["lip"]
        lh = int(round(lh * SS))
        band = Image.new("L", (W, H), 0)
        ImageDraw.Draw(band).rectangle((0, H - inset[3] - lh, W, H), fill=255)
        bandm = ImageChops.multiply(band, m)
        bl = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        bl.paste(Image.new("RGBA", (W, H), color(lc)), (0, 0), bandm)
        out.alpha_composite(bl)
    if "hole" in spec:  # transparent window: inset px from the outer edge
        hi = int(round(spec["hole"] * SS))
        hm = _shape_mask(kind, W, H, [hi] * 4, int(spec.get("hole_radius", rad / SS) * SS) + hi, corners, pts)
        a = ImageChops.subtract(out.getchannel("A"), hm)
        out.putalpha(a)
    out = out.resize((w, h), Image.LANCZOS)
    return with_alpha(out, spec.get("alpha"))


# ---------------------------------------------------------------- text

def draw_text(canvas, el, k=1.0):
    x, y, w, h = el["rect"]
    size = el["size"]
    f = font(size)
    asc, desc = f.getmetrics()
    base_col = el.get("color", "#ffffff")
    runs = el.get("runs") or [[el["text"], base_col]]
    lines, cur = [], []
    for t, c in runs:
        parts = t.split("\n")
        for i, p in enumerate(parts):
            if i > 0:
                lines.append(cur); cur = []
            if p:
                cur.append((p, c))
    lines.append(cur)
    lines = [ln for ln in lines if ln] or [[("", base_col)]]
    outline = int(round(el.get("outline", 0)))
    oc = color(el.get("outline_color", "#1d1b33"))
    shadow = int(round(el.get("shadow", 0)))
    lh = h / len(lines)
    d = ImageDraw.Draw(canvas)
    for li, ln in enumerate(lines):
        widths = [d.textlength(t, font=f) for t, _ in ln]
        tw = sum(widths)
        align = el.get("align", "center")
        if align == "left":
            cx = x
        elif align == "right":
            cx = x + w - tw
        else:
            cx = x + (w - tw) / 2
        base = y + li * lh + lh / 2 + (asc - desc) / 2
        for pass_ in ("shadow", "text"):
            px = cx
            for (t, c), tw_ in zip(ln, widths):
                if pass_ == "shadow" and shadow:
                    d.text((px, base + shadow), t, font=f, fill=oc, anchor="ls", stroke_width=outline, stroke_fill=oc)
                elif pass_ == "text":
                    d.text((px, base), t, font=f, fill=color(c), anchor="ls",
                           stroke_width=outline, stroke_fill=oc)
                px += tw_


def dashed_rect(canvas, rect, col=MISSING, width=3, dash=12):
    x, y, w, h = rect
    d = ImageDraw.Draw(canvas)
    x1, y1 = x + w - 1, y + h - 1
    for sx in range(int(x), int(x1), dash * 2):
        ex = min(sx + dash, x1)
        d.line([(sx, y), (ex, y)], fill=col, width=width)
        d.line([(sx, y1), (ex, y1)], fill=col, width=width)
    for sy in range(int(y), int(y1), dash * 2):
        ey = min(sy + dash, y1)
        d.line([(x, sy), (x, ey)], fill=col, width=width)
        d.line([(x1, sy), (x1, ey)], fill=col, width=width)


def id_label(canvas, rect, text):
    """Id tag for a missing piece: inside the top-left corner when it fits, else just above the rect."""
    x, y, w, h = rect
    f = font(17)
    d = ImageDraw.Draw(canvas)
    tw = d.textlength(text, font=f)
    if w >= tw + 10 and h >= 48:
        lx, ly = x + 4, y + 4
    else:
        lx, ly = x, y - 21
    lx = int(min(max(0, lx), canvas.width - tw - 8))
    ly = int(min(max(0, ly), canvas.height - 21))
    d.rounded_rectangle((lx, ly, lx + tw + 6, ly + 19), radius=4, fill=(40, 0, 40))
    d.text((lx + 3, ly + 1), text, font=f, fill=MISSING)


# ---------------------------------------------------------------- doc / layout data

def load_layouts():
    with open(LAYOUTS) as fh:
        return json.load(fh)


def parse_kit_doc(path=KIT_DOC):
    """Ids, nominal sizes and 9-slice borders from ui-kit-v2.md tables (sections A-C)."""
    text = open(path).read()
    kit = {}
    section = text.split("## D.")[0]
    for line in section.splitlines():
        if not line.startswith("|") or "---" in line:
            continue
        cells = [c.strip() for c in line.strip("|").split("|")]
        size_cell = next((c for c in cells if re.search(r"\d+x\d+", c)), "")
        # ids live in the Id column; a few extra ones in the size column ("+ `power_icon` 90x90")
        ids = re.findall(r"`([a-z0-9_]+)`", (cells[1] if len(cells) > 1 else "") + " " + size_cell)
        if not ids:
            continue
        sizes = [tuple(map(int, s)) for s in re.findall(r"(\d+)x(\d+)", size_cell)]
        bm = re.search(r"9-slice (\d+)", size_cell)
        border = int(bm.group(1)) if bm else None
        # "(+ `_pressed`)" style suffix ids
        full = []
        for i in ids:
            if i.startswith("_") and full:
                full.append(full[0] + i)
            else:
                full.append(i)
        for n, i in enumerate(full):
            size = sizes[min(n, len(sizes) - 1)] if sizes else None
            kit[i] = {"size": size, "border": border, "mode": "9slice" if border else "fit"}
    section_d = text.split("## D.")[1].split("## E.")[0] if "## D." in text else ""
    return kit, section_d


def kit_table(layouts):
    kit, _ = parse_kit_doc()
    for i, o in layouts.get("kit_overrides", {}).items():
        if i.startswith("_"):
            continue
        kit.setdefault(i, {"size": None, "border": None, "mode": "fit"})
        kit[i].update({k: (tuple(v) if k == "size" else v) for k, v in o.items() if not k.startswith("_")})
    return kit


# ---------------------------------------------------------------- kit source

def _newest(paths):
    return max(paths, key=lambda p: os.path.getmtime(p)) if paths else None


def scan_kit(args):
    """id -> {"approved": [paths], "candidates": [paths]}"""
    src = {}

    def add(i, p, key):
        src.setdefault(i, {"approved": [], "candidates": []})[key].append(p)

    if args.no_kit:
        return src, "none (all fallbacks)"
    if args.kit:
        root = os.path.expanduser(args.kit)
        for p in sorted(glob.glob(os.path.join(root, "*.png"))):
            add(os.path.basename(p)[:-4].split("__")[0], p, "candidates")
        for p in sorted(glob.glob(os.path.join(root, "*", "*.png"))):
            add(os.path.basename(os.path.dirname(p)), p, "candidates")
        return src, "folder " + root
    if not os.path.exists(ART_STATE):
        return src, "none (no art-state.json)"
    state = json.load(open(ART_STATE))
    coll = next((c for c in state.get("collections", [])
                 if c.get("title", "").lower() == args.collection.lower()), None)
    if coll is None:
        return src, 'none (no "%s" collection in art-state.json)' % args.collection
    for p in coll.get("approved", []):
        p = os.path.expanduser(p)
        if os.path.exists(p):
            add(os.path.basename(os.path.dirname(p)), p, "approved")
    for p in coll.get("candidates", []):
        p = os.path.expanduser(p)
        if os.path.exists(p):
            add(os.path.basename(p).split("__")[0], p, "candidates")
    return src, 'art-state collection "%s"' % coll["title"]


def pick(src, kid, sel=None):
    e = src.get(kid)
    if not e:
        return None
    allp = e["approved"] + e["candidates"]
    if sel:
        hits = [p for p in allp if sel in os.path.basename(p) or sel in p]
        if not hits:
            print("  ! variant %s=%s matches nothing (have: %s)" % (kid, sel, ", ".join(os.path.basename(p) for p in allp)))
            return None
        appr = [p for p in hits if p in e["approved"]]
        return appr[-1] if appr else _newest(hits)
    return e["approved"][-1] if e["approved"] else _newest(e["candidates"])


# ---------------------------------------------------------------- rendering

class Renderer:
    def __init__(self, layouts, kit, src, choice, aliases, label_all=False):
        self.L, self.kit, self.src, self.choice, self.aliases = layouts, kit, src, choice, aliases
        self.label_all = label_all
        self.cache = {}

    def resolve(self, kid):
        """-> (path, tint) or (None, None)"""
        if kid in self.aliases:
            old, tnt = self.aliases[kid]
            return pick(self.src, old, self.choice.get(old)), tnt
        path = pick(self.src, kid, self.choice.get(kid))
        if path is None and kid in self.L.get("id_fallbacks", {}) and not kid.startswith("_"):
            base, tnt = self.L["id_fallbacks"][kid]
            base_path = pick(self.src, base, self.choice.get(base))
            if base_path:
                return base_path, tnt
        return path, None

    def load(self, path):
        if path not in self.cache:
            self.cache[path] = Image.open(path).convert("RGBA")
        return self.cache[path]

    def content(self, ref):
        if ref.startswith("standin:"):
            return os.path.join(STANDINS, ref.split(":", 1)[1] + ".png")
        return os.path.join(CONTENT, ref)

    def paste_image(self, canvas, img, rect, fit="contain", flip=False, alpha=None, desat=None):
        x, y, w, h = rect
        if flip:
            img = ImageOps.mirror(img)
        if desat:
            img = desaturate(img, desat)
        if fit == "stretch":
            im, ox, oy = img.resize((w, h), Image.LANCZOS), 0, 0
        else:
            im, ox, oy = contain(img, w, h, cover=(fit == "cover"))
        canvas.alpha_composite(with_alpha(im, alpha), (int(x + ox), int(y + oy)))

    def draw_kit(self, canvas, el, path, tnt):
        kid = el["id"]
        info = self.kit.get(kid, {"size": None, "border": None, "mode": "fit"})
        img = trim(self.load(path))
        if el.get("tint") or tnt:
            img = tint(img, tnt or el["tint"])
        x, y, w, h = el["rect"]
        mode = el.get("mode", info.get("mode", "fit"))
        if el.get("flip"):
            img = ImageOps.mirror(img)
        if mode == "9slice":
            b = el.get("border", info.get("border")) or 0
            b = b if isinstance(b, list) else [b] * 4
            nom = info.get("size")
            s = img.width / nom[0] if nom else 1.0  # sprite px per 1080-space px
            src_b = [min(int(round(v * s)), img.width // 2 if i % 2 == 0 else img.height // 2) for i, v in enumerate(b)]
            patch = nine_slice(img, src_b, w, h, b)
            canvas.alpha_composite(with_alpha(patch, el.get("alpha")), (x, y))
        elif mode == "tile":
            nom = info.get("size") or img.size
            tw, th = nom
            tile_img = img.resize((tw, th), Image.LANCZOS)
            patch = Image.new("RGBA", (w, h))
            for ty in range(0, h, th):
                for tx in range(0, w, tw):
                    patch.alpha_composite(tile_img, (tx, ty)) if tx + tw <= w and ty + th <= h else \
                        patch.paste(tile_img.crop((0, 0, min(tw, w - tx), min(th, h - ty))), (tx, ty))
            canvas.alpha_composite(with_alpha(patch, el.get("alpha")), (x, y))
        else:
            self.paste_image(canvas, img, el["rect"], "stretch" if mode == "stretch" else "contain",
                             alpha=el.get("alpha"))

    def draw_fallback(self, canvas, el, missing_marker):
        fb = el.get("fallback")
        x, y, w, h = el["rect"]
        if fb:
            for un in fb.get("under", []):
                self.draw_el(canvas, un, [], set())
            canvas.alpha_composite(shape_patch(fb, w, h), (x, y))
            for ex in fb.get("extras", []):
                self.draw_el(canvas, ex, [], set())
        return missing_marker

    def draw_el(self, canvas, el, marks, seen):
        kind = el["kind"]
        if kind == "shape":
            x, y, w, h = el["rect"]
            canvas.alpha_composite(shape_patch(el, w, h), (x, y))
        elif kind == "text":
            draw_text(canvas, el)
        elif kind == "sprite":
            self.paste_image(canvas, self.load(self.content(el["src"])), el["rect"], el.get("fit", "contain"),
                             el.get("flip"), el.get("alpha"), el.get("desat"))
        elif kind == "kit":
            path, tnt = self.resolve(el["id"])
            if path:
                self.draw_kit(canvas, el, path, tnt)
                self.used[el["id"]] = path
            elif el.get("optional"):
                return
            else:
                self.draw_fallback(canvas, el, True)
                marks.append(el)
        elif kind == "icon":
            icon = self.L["icons"][el["id"]]
            path, tnt = self.resolve(el["id"])
            if path:
                img = trim(self.load(path))
                if tnt:
                    img = tint(img, tnt)
                self.paste_image(canvas, img, el["rect"], "contain", el.get("flip"), el.get("alpha"))
                self.used[el["id"]] = path
            else:
                self.paste_image(canvas, self.load(self.content(icon["standin"])), el["rect"], "contain",
                                 el.get("flip"), el.get("alpha"), el.get("desat"))
                if not icon.get("exists"):
                    marks.append(el)

    def render(self, screen):
        sc = self.L["screens"][screen]
        W, H = self.L["canvas"]
        canvas = Image.new("RGBA", (W, H), color(sc.get("background", "#000000")))
        self.used = {}
        marks = []
        els = sorted(enumerate(sc["elements"]), key=lambda t: (t[1].get("z", 0), t[0]))
        for _, el in els:
            self.draw_el(canvas, el, marks, set())
        for el in marks:
            dashed_rect(canvas, el["rect"])
        # one label per id (on its largest instance) unless --label-all
        biggest = {}
        for el in marks:
            a = el["rect"][2] * el["rect"][3]
            if self.label_all or el["id"] not in biggest or a > biggest[el["id"]][0]:
                biggest[el["id"] if not self.label_all else id(el)] = (a, el)
        for _, el in biggest.values():
            id_label(canvas, el["rect"], el["id"])
        return canvas.convert("RGB"), self.used


def required_ids(layouts):
    """(kit ids, icon ids needing new art, optional ids) referenced per screen."""
    kit_ids, icon_ids, optional = {}, {}, set()

    def walk(els, screen):
        for el in els:
            if el["kind"] == "kit":
                if el.get("optional"):
                    optional.add(el["id"])
                else:
                    kit_ids.setdefault(el["id"], set()).add(screen)
            elif el["kind"] == "icon":
                icon_ids.setdefault(el["id"], set()).add(screen)
            for ex in el.get("fallback", {}).get("extras", []) if isinstance(el.get("fallback"), dict) else []:
                walk([ex], screen)

    for s, sc in layouts["screens"].items():
        walk(sc["elements"], s)
    return kit_ids, icon_ids, optional


# ---------------------------------------------------------------- self-check

def selftest():
    L = load_layouts()
    kit, section_d = parse_kit_doc()
    doc_text = open(KIT_DOC).read()
    errors = []
    kit_ids, icon_ids, optional = required_ids(L)
    extra = L.get("extra_ids", {})
    for i in sorted(set(kit_ids) | optional):
        if i in kit:
            continue
        if i in extra:
            if extra[i] not in doc_text:
                errors.append("extra id %s: doc phrase %r not in ui-kit-v2.md" % (i, extra[i]))
        else:
            errors.append("kit id %s is not in ui-kit-v2.md (nor in extra_ids)" % i)
    for i in sorted(k for k in L.get("kit_overrides", {}) if not k.startswith("_")):
        if i not in kit and i not in extra:
            errors.append("kit_overrides: %s is not a kit id" % i)
    sd = section_d.lower()
    for i in sorted(icon_ids):
        icon = L["icons"].get(i)
        if not icon:
            errors.append("icon %s missing from the icons table" % i)
            continue
        if icon["doc"].lower() not in sd:
            errors.append("icon %s: doc phrase %r not in section D of ui-kit-v2.md" % (i, icon["doc"]))
    refs = set()

    def collect(el):
        if el["kind"] == "sprite":
            refs.add(el["src"])
        if el["kind"] == "icon":
            refs.add(L["icons"][el["id"]]["standin"])
        fb = el.get("fallback") or {}
        for ex in fb.get("extras", []):
            collect(ex)

    for s in SCREENS:
        if s not in L["screens"]:
            errors.append("screen %s missing from kit_layouts.json" % s)
            continue
        W, H = L["canvas"]
        for el in L["screens"][s]["elements"]:
            collect(el)
            x, y, w, h = el["rect"]
            if w <= 0 or h <= 0 or x + w < 0 or y + h < 0 or x > W or y > H:
                errors.append("%s: element %s has an off-screen/empty rect %s" % (s, el.get("id") or el.get("text") or el["kind"], el["rect"]))
    r = Renderer(L, kit, {}, {}, {})
    for ref in sorted(refs):
        if not os.path.exists(r.content(ref)):
            errors.append("content sprite does not resolve: %s" % ref)
    if not os.path.exists(FONT):
        errors.append("font missing: " + FONT)
    print("selftest: %d kit ids, %d optional, %d icon slots, %d content sprites" % (
        len(kit_ids), len(optional), len(icon_ids), len(refs)))
    for e in errors:
        print("  FAIL", e)
    print("selftest", "FAILED" if errors else "OK")
    return 1 if errors else 0


# ---------------------------------------------------------------- sheet

def make_sheet(rows, out_path, title, col_w=380):
    """rows: list of (row label, [(caption, PIL image)])"""
    gap, head, cap = 16, 54, 30
    W, H = 1080, 2400
    cw, ch = col_w, int(col_w * H / W)
    ncol = max(len(r[1]) for r in rows)
    sw = gap + ncol * (cw + gap)
    rh = cap + ch + gap
    sh = head + sum(rh + (28 if lbl else 0) for lbl, _ in rows)
    sheet = Image.new("RGB", (sw, sh), (24, 22, 40))
    d = ImageDraw.Draw(sheet)
    d.text((gap, 14), title, font=font(24), fill=(255, 255, 255))
    y = head
    for lbl, items in rows:
        if lbl:
            d.text((gap, y), lbl, font=font(22), fill=(255, 210, 63))
            y += 28
        for i, (c, im) in enumerate(items):
            x = gap + i * (cw + gap)
            d.text((x, y), c, font=font(20), fill=(220, 220, 235))
            sheet.paste(im.resize((cw, ch), Image.LANCZOS), (x, y + cap))
        y += rh
    sheet.save(out_path, optimize=True)


# ---------------------------------------------------------------- main

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", help="output dir for home.png, worm.png, battle.png, kit-preview.png")
    ap.add_argument("--kit", help="folder of kit PNGs (<id>.png or <id>/<file>.png)")
    ap.add_argument("--no-kit", action="store_true", help="draw every piece as the code-drawn fallback")
    ap.add_argument("--collection", default="UI kit v2", help='art-state.json collection title (default "UI kit v2")')
    ap.add_argument("--variant", action="append", default=[], metavar="ID=SEL[,SEL..]",
                    help="pick a candidate per id by file-name substring; several = rendered side by side")
    ap.add_argument("--alias", action="append", default=[], metavar="ID=OLDID[:#tint]",
                    help="use the kit piece OLDID for ID (e.g. to preview an older kit)")
    ap.add_argument("--screens", default=",".join(SCREENS))
    ap.add_argument("--label-all", action="store_true", help="label every missing piece, not only the first per id")
    ap.add_argument("--selftest", action="store_true")
    args = ap.parse_args(argv)

    if args.selftest:
        return selftest()
    if not args.out:
        ap.error("--out is required")
    os.makedirs(args.out, exist_ok=True)

    L = load_layouts()
    kit = kit_table(L)
    src, src_desc = scan_kit(args)
    aliases = {}
    for a in args.alias:
        new, old = a.split("=", 1)
        old, _, tnt = old.partition(":")
        aliases[new] = (old, tnt or None)
    choice, multi = {}, {}
    for v in args.variant:
        kid, sels = v.split("=", 1)
        sels = [s for s in sels.split(",") if s]
        choice[kid] = sels[0]
        if len(sels) > 1:
            multi[kid] = sels
    screens = [s for s in args.screens.split(",") if s]

    r = Renderer(L, kit, src, choice, aliases, args.label_all)
    imgs, used = {}, {}
    for s in screens:
        im, u = r.render(s)
        im.save(os.path.join(args.out, s + ".png"))
        imgs[s] = im
        used.update(u)

    # coverage
    kit_ids, icon_ids, optional = required_ids(L)
    new_icons = sorted(i for i in icon_ids if not L["icons"][i].get("exists"))
    need = sorted(kit_ids) + new_icons
    present = [i for i in need if r.resolve(i)[0]]
    missing = [i for i in need if i not in present]
    opt_present = sorted(i for i in optional if r.resolve(i)[0])
    doc_kit, _ = parse_kit_doc()
    used_ids = set(kit_ids) | optional
    # a doc id is covered when used directly or through colour/state ids (pip_gem -> pip_gem_blue, ...)
    not_on_boards = sorted(i for i in set(doc_kit) - used_ids if not any(u.startswith(i + "_") for u in used_ids))
    cov = {"source": src_desc, "present": present, "missing": missing, "optional_present": opt_present,
           "optional_missing": sorted(optional - set(opt_present)), "not_on_boards": not_on_boards,
           "files": {i: used.get(i) or r.resolve(i)[0] for i in present}}
    json.dump(cov, open(os.path.join(args.out, "coverage.json"), "w"), indent=1)

    print("kit source: %s" % src_desc)
    print("coverage: %d of %d kit ids present (%d frames/panels/HUD, %d new icons)" % (
        len(present), len(need), len(kit_ids), len(new_icons)))
    for i in present:
        print("  ok      %-22s %s" % (i, os.path.basename(cov["files"][i] or "")))
    if missing:
        print("  missing (%d): %s" % (len(missing), ", ".join(missing)))
    if optional:
        print("  optional: %s" % ", ".join("%s (%s)" % (i, "ok" if i in opt_present else "code text") for i in sorted(optional)))
    if not_on_boards:
        print("  in ui-kit-v2.md but not on the three boards: %s" % ", ".join(not_on_boards))

    rows = [("", [(s, imgs[s]) for s in screens])]
    for kid, sels in multi.items():
        items = []
        for sel in sels:
            r.choice = dict(choice, **{kid: sel})
            for s in screens:
                ids = {e.get("id") for e in L["screens"][s]["elements"]}
                if s in imgs and (kid in ids or any(aliases.get(i, ("",))[0] == kid for i in ids)):
                    im, _ = r.render(s)
                    name = "%s__%s-%s.png" % (s, kid, re.sub(r"[^A-Za-z0-9_.-]", "_", sel))
                    im.save(os.path.join(args.out, name))
                    items.append(("%s · %s" % (s, sel), im))
        if items:
            rows.append(("variants of %s" % kid, items))
    short = src_desc.replace(os.path.expanduser("~"), "~")
    short = short if len(short) <= 60 else "..." + short[-57:]
    title = "UI kit v2 preview · %d/%d kit ids · source: %s" % (len(present), len(need), short)
    make_sheet(rows, os.path.join(args.out, "kit-preview.png"), title)
    print("wrote %s/{%s}.png and kit-preview.png" % (args.out, ",".join(screens)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
