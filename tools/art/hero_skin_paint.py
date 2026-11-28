#!/usr/bin/env python3
"""Hero skins on the base worm (owner rule 2026-11-28): the costume is painted ONTO the current base worm sprite, never
drawn freely. The worm's alpha (silhouette, position) is copied unchanged into every skin, the face (eyes, mouth) is kept,
and only the body inside the outline is repainted by SDXL inpainting (DreamShaper XL Turbo + IP-Adapter with a style
reference, e.g. a rejected free-drawn skin whose costume idea the owner liked).

Runs in the ComfyUI venv against ComfyUI on 127.0.0.1:8188:
    hero_skin_paint.py <skin_id> "<costume prompt>" [--ref <style image>] [--seeds 8201,8202] [--denoise 0.7]
Writes ~/AI/out/processed/hero-skins-v2/<skin_id>__B__s<seed>.png (512x512, alpha = base worm alpha).
"""
import argparse, json, os, shutil, time, urllib.request
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage as ndi

BASE = os.path.expanduser("~/Projects/rogue-stars/client/RogueStars/Assets/Art/Resources/Art/Hero/hero_worm.png")
OUT = os.path.expanduser("~/AI/out/processed/hero-skins-v2")
COMFY = os.path.expanduser("~/AI/ComfyUI"); API = "http://127.0.0.1:8188"
FACE = (105, 195, 280, 315)          # eyes + mouth + cheeks on the 512 px base sprite: kept as is
STYLE = ("cute cartoon pink worm hero wearing the costume painted on its body, bold black outline, flat bright colors, "
         "two-tone cel shading, mobile game character sprite")
NEG = "arms, hands, legs, feet, extra limbs, second character, background, scenery, text, letters, blurry, photo, realistic"


def masks():
    base = Image.open(BASE).convert("RGBA"); a = np.asarray(base)[..., 3] > 128
    inner = ndi.binary_erosion(a, iterations=7)                 # keep the black outline untouched
    face = np.zeros_like(a); x0, y0, x1, y1 = FACE; face[y0:y1, x0:x1] = True
    m = inner & ~face
    soft = Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(2))
    return base, soft


def wf(prefix, text, seed, den, ref):
    nodes = {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "DreamShaperXL_Turbo_v2_1.safetensors"}},
        "6": {"class_type": "LoadImage", "inputs": {"image": "hs_base.png"}},
        "7": {"class_type": "LoadImage", "inputs": {"image": "hs_mask.png"}},
        "7m": {"class_type": "ImageToMask", "inputs": {"image": ["7", 0], "channel": "red"}},
        "8": {"class_type": "VAEEncode", "inputs": {"pixels": ["6", 0], "vae": ["1", 2]}},
        "8m": {"class_type": "SetLatentNoiseMask", "inputs": {"samples": ["8", 0], "mask": ["7m", 0]}},
        "9": {"class_type": "CLIPTextEncode", "inputs": {"text": text + ", " + STYLE, "clip": ["1", 1]}},
        "10": {"class_type": "CLIPTextEncode", "inputs": {"text": NEG, "clip": ["1", 1]}},
        "12": {"class_type": "VAEDecode", "inputs": {"samples": ["11", 0], "vae": ["1", 2]}},
        "13": {"class_type": "SaveImage", "inputs": {"images": ["12", 0], "filename_prefix": prefix}},
    }
    model = ["1", 0]
    if ref:
        nodes.update({
            "2": {"class_type": "IPAdapterModelLoader", "inputs": {"ipadapter_file": "ip-adapter-plus_sdxl_vit-h.safetensors"}},
            "3": {"class_type": "CLIPVisionLoader", "inputs": {"clip_name": "CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors"}},
            "4": {"class_type": "LoadImage", "inputs": {"image": "hs_ref.png"}},
            "5": {"class_type": "IPAdapterAdvanced", "inputs": {"model": ["1", 0], "ipadapter": ["2", 0], "image": ["4", 0], "clip_vision": ["3", 0],
                  "weight": 0.6, "weight_type": "style transfer", "combine_embeds": "average", "start_at": 0.0, "end_at": 1.0, "embeds_scaling": "V only"}}})
        model = ["5", 0]
    nodes["11"] = {"class_type": "KSampler", "inputs": {"model": model, "positive": ["9", 0], "negative": ["10", 0], "latent_image": ["8m", 0],
                   "seed": seed, "steps": 8, "cfg": 2.0, "sampler_name": "dpmpp_sde", "scheduler": "karras", "denoise": den}}
    return nodes


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("skin"); ap.add_argument("prompt"); ap.add_argument("--ref")
    ap.add_argument("--seeds", default="8201,8202"); ap.add_argument("--denoise", type=float, default=0.7)
    a = ap.parse_args(); os.makedirs(OUT, exist_ok=True)
    base, soft = masks()
    bg = Image.new("RGBA", base.size, (255, 255, 255, 255)); bg.alpha_composite(base)
    bg.convert("RGB").save(f"{COMFY}/input/hs_base.png"); soft.save(f"{COMFY}/input/hs_mask.png")
    if a.ref:
        r = Image.open(os.path.expanduser(a.ref)).convert("RGBA"); rb = Image.new("RGBA", r.size, (255, 255, 255, 255)); rb.alpha_composite(r)
        rb.convert("RGB").save(f"{COMFY}/input/hs_ref.png")
    m = np.asarray(soft).astype(float)[..., None] / 255; b = np.asarray(base).astype(float)
    for seed in [int(s) for s in a.seeds.split(",")]:
        prefix = f"hs_{a.skin}__s{seed}"
        req = urllib.request.Request(API + "/prompt", data=json.dumps({"prompt": wf(prefix, a.prompt, seed, a.denoise, a.ref)}).encode(),
                                     headers={"Content-Type": "application/json"})
        q = json.load(urllib.request.urlopen(req))["prompt_id"]; t = time.time()
        while True:
            h = json.load(urllib.request.urlopen(f"{API}/history/{q}"))
            if q in h: break
            time.sleep(2)
        ims = [im for n in h[q]["outputs"].values() for im in n.get("images", [])]
        if not ims: print("ERROR", prefix, h[q].get("status")); continue
        g = np.asarray(Image.open(os.path.join(COMFY, "output", ims[0].get("subfolder", ""), ims[0]["filename"])).convert("RGB").resize(base.size)).astype(float)
        out = b.copy(); out[..., :3] = b[..., :3] * (1 - m) + g * m      # only inside the outline, face untouched
        out[..., 3] = b[..., 3]                                          # silhouette identical to the base worm
        dst = f"{OUT}/{a.skin}__B__s{seed}.png"
        Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA").save(dst); print(dst, round(time.time() - t), "s", flush=True)


if __name__ == "__main__":
    main()
