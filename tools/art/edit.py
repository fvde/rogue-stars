#!/usr/bin/env python3
"""Instruction-based edits of existing images with FLUX.1 Kontext (ComfyUI): "remove the base", "make it gold", ...

    python3 tools/art/edit.py --instruction "Remove the stone base under the object. Keep everything else the same." \
        --out ~/AI/out/edit-noplate ~/AI/out/pkg-slice-markers2/chest__I__flux__s5111.png [more images...]

Each input is copied into ComfyUI's input/ folder and edited; the result is saved as <out>/<name>__edit_s<seed>.png next to a
before/after contact sheet (index.html). Needs models/unet/flux1-kontext-dev-Q4_K_S.gguf and a running ComfyUI.
Licence note: Kontext [dev] weights are under the FLUX.1 [dev] non-commercial licence; confirm output use before shipping.
"""
import argparse, os, shutil, sys, html

sys.path.insert(0, os.path.dirname(__file__))
from gen import run_one  # noqa: E402

COMFY_INPUT = os.path.expanduser("~/AI/ComfyUI/input")


def kontext_graph(image_name, instruction, seed, steps=20, guidance=2.5):
    return {
        "1": {"class_type": "UnetLoaderGGUF", "inputs": {"unet_name": "flux1-kontext-dev-Q4_K_S.gguf"}},
        "2": {"class_type": "DualCLIPLoaderGGUF", "inputs": {"clip_name1": "clip_l.safetensors", "clip_name2": "t5-v1_1-xxl-encoder-Q5_K_M.gguf", "type": "flux"}},
        "3": {"class_type": "CLIPTextEncode", "inputs": {"text": instruction, "clip": ["2", 0]}},
        "4": {"class_type": "LoadImage", "inputs": {"image": image_name}},
        "5": {"class_type": "FluxKontextImageScale", "inputs": {"image": ["4", 0]}},
        "6": {"class_type": "VAELoader", "inputs": {"vae_name": "flux_ae.safetensors"}},
        "7": {"class_type": "VAEEncode", "inputs": {"pixels": ["5", 0], "vae": ["6", 0]}},
        "8": {"class_type": "ReferenceLatent", "inputs": {"conditioning": ["3", 0], "latent": ["7", 0]}},
        "9": {"class_type": "FluxGuidance", "inputs": {"conditioning": ["8", 0], "guidance": guidance}},
        "10": {"class_type": "ConditioningZeroOut", "inputs": {"conditioning": ["3", 0]}},
        "11": {"class_type": "KSampler", "inputs": {"model": ["1", 0], "positive": ["9", 0], "negative": ["10", 0], "latent_image": ["7", 0],
                                                     "seed": seed, "steps": steps, "cfg": 1.0, "sampler_name": "euler", "scheduler": "simple", "denoise": 1.0}},
        "12": {"class_type": "VAEDecode", "inputs": {"samples": ["11", 0], "vae": ["6", 0]}},
        "13": {"class_type": "SaveImage", "inputs": {"images": ["12", 0], "filename_prefix": "rs_edit"}},
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("images", nargs="+")
    ap.add_argument("--instruction", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--seed", type=int, default=7001)
    ap.add_argument("--steps", type=int, default=20)
    a = ap.parse_args()
    out = os.path.expanduser(a.out)
    os.makedirs(out, exist_ok=True)
    rows = []
    for src in a.images:
        src = os.path.expanduser(src)
        name = os.path.splitext(os.path.basename(src))[0]
        dst = os.path.join(out, f"{name}__edit_s{a.seed}.png")
        if not os.path.exists(dst):
            shutil.copy(src, os.path.join(COMFY_INPUT, os.path.basename(src)))
            data = run_one(kontext_graph(os.path.basename(src), a.instruction, a.seed, a.steps))
            with open(dst, "wb") as f:
                f.write(data)
            print("edited", dst, flush=True)
        rows.append((src, dst))
    page = ("<!doctype html><meta charset=utf-8><title>edits</title><style>body{background:#fff;font:13px sans-serif}"
            "img{width:320px;border:1px solid #ccc}td{padding:6px}</style><p>" + html.escape(a.instruction) + "</p><table>")
    for s, d in rows:
        page += f"<tr><td><img src='file://{html.escape(s)}'></td><td><img src='{html.escape(os.path.basename(d))}'></td><td>{html.escape(os.path.basename(s))}</td></tr>"
    open(os.path.join(out, "index.html"), "w").write(page + "</table>")


if __name__ == "__main__":
    main()
