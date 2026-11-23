#!/usr/bin/env python3
"""Batch image generation against a local ComfyUI server (tools/art/setup-comfyui.sh).

    python3 tools/art/gen.py tools/art/prompts/concepts-01.json [--out ~/AI/out] [--limit N] [--dry-run]

A prompt sheet is JSON:
    {
      "name": "concepts-01",
      "width": 768, "height": 1344,
      "seeds": [1, 2, 3],
      "models": ["flux", "sdxl", "sdxl_ref"],
      "style_refs": ["ref1.png"], "ref_weight": 0.8,  # sdxl_ref: style images in ComfyUI/input (kept out of git)
      "negative": "...",                       # SDXL only
      "styles":   {"key": "style text", ...},
      "subjects": {"key": "subject text", ...}
    }
Every subject x style x model x seed becomes one image: <out>/<name>/<subject>__<style>__<model>__s<seed>.png.
Existing files are skipped, so an interrupted batch resumes. When done, <out>/<name>/index.html is a contact sheet and
<out>/<name>/jpg/ holds 1024px JPEG copies for sharing. Stdlib only (Pillow optional, for the JPEG copies).
"""
import argparse, json, os, sys, time, urllib.request, urllib.error, uuid, html

SERVER = os.environ.get("COMFY_URL", "http://127.0.0.1:8188")


def flux_graph(prompt, w, h, seed):
    return {
        "1": {"class_type": "UnetLoaderGGUF", "inputs": {"unet_name": "flux1-schnell-Q4_K_S.gguf"}},
        "2": {"class_type": "DualCLIPLoaderGGUF", "inputs": {"clip_name1": "clip_l.safetensors", "clip_name2": "t5-v1_1-xxl-encoder-Q5_K_M.gguf", "type": "flux"}},
        "3": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["2", 0]}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"text": "", "clip": ["2", 0]}},
        "5": {"class_type": "EmptySD3LatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}},
        "6": {"class_type": "KSampler", "inputs": {"model": ["1", 0], "positive": ["3", 0], "negative": ["4", 0], "latent_image": ["5", 0],
                                                    "seed": seed, "steps": 4, "cfg": 1.0, "sampler_name": "euler", "scheduler": "simple", "denoise": 1.0}},
        "7": {"class_type": "VAELoader", "inputs": {"vae_name": "flux_ae.safetensors"}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["6", 0], "vae": ["7", 0]}},
        "9": {"class_type": "SaveImage", "inputs": {"images": ["8", 0], "filename_prefix": "rs"}},
    }


def sdxl_graph(prompt, negative, w, h, seed):
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "sd_xl_base_1.0.safetensors"}},
        "2": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["1", 1]}},
        "3": {"class_type": "CLIPTextEncode", "inputs": {"text": negative, "clip": ["1", 1]}},
        "4": {"class_type": "EmptyLatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}},
        "5": {"class_type": "KSampler", "inputs": {"model": ["1", 0], "positive": ["2", 0], "negative": ["3", 0], "latent_image": ["4", 0],
                                                    "seed": seed, "steps": 30, "cfg": 6.0, "sampler_name": "dpmpp_2m", "scheduler": "karras", "denoise": 1.0}},
        "6": {"class_type": "VAELoader", "inputs": {"vae_name": "sdxl_vae_fp16fix.safetensors"}},
        "7": {"class_type": "VAEDecode", "inputs": {"samples": ["5", 0], "vae": ["6", 0]}},
        "8": {"class_type": "SaveImage", "inputs": {"images": ["7", 0], "filename_prefix": "rs"}},
    }


def sdxl_ref_graph(prompt, negative, w, h, seed, refs, weight):
    """SDXL with IP-Adapter Plus in style-transfer mode: the reference images steer rendering style, not content.
    refs = image file names in ComfyUI's input/ folder."""
    g = sdxl_graph(prompt, negative, w, h, seed)
    g["10"] = {"class_type": "IPAdapterModelLoader", "inputs": {"ipadapter_file": "ip-adapter-plus_sdxl_vit-h.safetensors"}}
    g["11"] = {"class_type": "CLIPVisionLoader", "inputs": {"clip_name": "CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors"}}
    last = None
    for i, ref in enumerate(refs):
        nid = str(20 + 2 * i)
        g[nid] = {"class_type": "LoadImage", "inputs": {"image": ref}}
        if last is None:
            last = [nid, 0]
        else:
            bid = str(21 + 2 * i)
            g[bid] = {"class_type": "ImageBatch", "inputs": {"image1": last, "image2": [nid, 0]}}
            last = [bid, 0]
    g["12"] = {"class_type": "IPAdapterAdvanced", "inputs": {"model": ["1", 0], "ipadapter": ["10", 0], "image": last, "weight": weight,
                                                             "weight_type": "style transfer", "combine_embeds": "average", "start_at": 0.0,
                                                             "end_at": 1.0, "embeds_scaling": "V only", "clip_vision": ["11", 0]}}
    g["5"]["inputs"]["model"] = ["12", 0]
    return g


def call(path, body=None):
    req = urllib.request.Request(SERVER + path, data=json.dumps(body).encode() if body is not None else None,
                                 headers={"Content-Type": "application/json"} if body is not None else {})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def run_one(graph):
    client = str(uuid.uuid4())
    pid = json.loads(call("/prompt", {"prompt": graph, "client_id": client}))["prompt_id"]
    while True:
        time.sleep(3)
        hist = json.loads(call("/history/" + pid))
        if pid in hist:
            entry = hist[pid]
            if entry.get("status", {}).get("status_str") == "error":
                raise RuntimeError(json.dumps(entry["status"])[:500])
            for node in entry["outputs"].values():
                for img in node.get("images", []):
                    q = urllib.parse.urlencode({"filename": img["filename"], "subfolder": img.get("subfolder", ""), "type": img["type"]})
                    return call("/view?" + q)
            raise RuntimeError("no image in output")


def contact_sheet(folder, sheet):
    files = sorted(f for f in os.listdir(folder) if f.endswith(".png"))
    try:
        from PIL import Image
        os.makedirs(os.path.join(folder, "jpg"), exist_ok=True)
        for f in files:
            dst = os.path.join(folder, "jpg", f[:-4] + ".jpg")
            if not os.path.exists(dst):
                im = Image.open(os.path.join(folder, f)).convert("RGB")
                im.thumbnail((1024, 1024))
                im.save(dst, quality=86)
    except ImportError:
        pass
    rows = []
    for subject in sheet["subjects"]:
        cells = "".join(f'<figure><img src="{html.escape(f)}" loading="lazy"><figcaption>{html.escape(f[:-4])}</figcaption></figure>'
                        for f in files if f.startswith(subject + "__"))
        rows.append(f"<h2>{html.escape(subject)}</h2><div class=g>{cells}</div>")
    page = ("<!doctype html><meta charset=utf-8><title>" + html.escape(sheet["name"]) + "</title><style>body{background:#111;color:#ddd;"
            "font:14px system-ui;margin:16px}.g{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:10px}"
            "img{width:100%;border-radius:8px}figure{margin:0}figcaption{font-size:11px;opacity:.7}</style>" + "".join(rows))
    with open(os.path.join(folder, "index.html"), "w") as fh:
        fh.write(page)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("sheet")
    ap.add_argument("--out", default=os.path.expanduser("~/AI/out"))
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()
    sheet = json.load(open(a.sheet))
    folder = os.path.join(a.out, sheet["name"])
    os.makedirs(folder, exist_ok=True)
    w, h = sheet.get("width", 768), sheet.get("height", 1344)
    jobs = []
    for seed in sheet.get("seeds", [1]):
        for model in sheet.get("models", ["flux"]):
            for skey, style in sheet["styles"].items():
                for subj, subject in sheet["subjects"].items():
                    jobs.append((subj, skey, model, seed, subject + ", " + style))
    done = 0
    for subj, skey, model, seed, prompt in jobs:
        dest = os.path.join(folder, f"{subj}__{skey}__{model}__s{seed}.png")
        if os.path.exists(dest):
            continue
        if a.limit and done >= a.limit:
            break
        if a.dry_run:
            print(dest, "\n   ", prompt)
            done += 1
            continue
        if model == "flux":
            graph = flux_graph(prompt, w, h, seed)
        elif model == "sdxl_ref":
            graph = sdxl_ref_graph(prompt, sheet.get("negative", ""), w, h, seed, sheet["style_refs"], sheet.get("ref_weight", 0.8))
        else:
            graph = sdxl_graph(prompt, sheet.get("negative", ""), w, h, seed)
        t = time.time()
        try:
            png = run_one(graph)
        except (RuntimeError, urllib.error.URLError) as e:
            print("FAIL", dest, e, file=sys.stderr, flush=True)
            continue
        with open(dest, "wb") as fh:
            fh.write(png)
        done += 1
        print(f"{done:4d} {time.time() - t:6.1f}s {os.path.basename(dest)}", flush=True)
    if not a.dry_run:
        contact_sheet(folder, sheet)
        print("contact sheet:", os.path.join(folder, "index.html"))


if __name__ == "__main__":
    import urllib.parse  # noqa: E402
    main()
