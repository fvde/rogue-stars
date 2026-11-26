"""LTX-Video 2B 0.9.8 distilled image-to-video through a local ComfyUI (anim pilot, technique 4)."""
import json, sys, time, urllib.request, shutil, os
HOST = os.environ.get("COMFY", "http://127.0.0.1:8189")
img, prompt, out, w, h, length, seed = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6]), int(sys.argv[7])
NEG = "camera movement, zoom, pan, morphing, changing shape, blurry, distorted, text, watermark, low quality, flicker"
shutil.copy(img, os.path.expanduser("~/AI/ComfyUI/input/") + os.path.basename(img))
g = {
  "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "ltxv-2b-0.9.8-distilled.safetensors"}},
  "2": {"class_type": "CLIPLoaderGGUF", "inputs": {"clip_name": "t5-v1_1-xxl-encoder-Q5_K_M.gguf", "type": "ltxv"}},
  "3": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["2", 0]}},
  "4": {"class_type": "CLIPTextEncode", "inputs": {"text": NEG, "clip": ["2", 0]}},
  "5": {"class_type": "LoadImage", "inputs": {"image": os.path.basename(img)}},
  "6": {"class_type": "LTXVImgToVideo", "inputs": {"positive": ["3", 0], "negative": ["4", 0], "vae": ["1", 2], "image": ["5", 0],
        "width": w, "height": h, "length": length, "batch_size": 1, "strength": 1.0}},
  "7": {"class_type": "LTXVConditioning", "inputs": {"positive": ["6", 0], "negative": ["6", 1], "frame_rate": 24.0}},
  "8": {"class_type": "ManualSigmas", "inputs": {"sigmas": "1.0, 0.9937, 0.9875, 0.9812, 0.975, 0.9094, 0.725, 0.4219, 0.0"}},
  "9": {"class_type": "KSamplerSelect", "inputs": {"sampler_name": "euler_ancestral"}},
  "10": {"class_type": "SamplerCustom", "inputs": {"model": ["1", 0], "add_noise": True, "noise_seed": seed, "cfg": 1.0,
        "positive": ["7", 0], "negative": ["7", 1], "sampler": ["9", 0], "sigmas": ["8", 0], "latent_image": ["6", 2]}},
  "11": {"class_type": "VAEDecode", "inputs": {"samples": ["10", 0], "vae": ["1", 2]}},
  "12": {"class_type": "SaveImage", "inputs": {"images": ["11", 0], "filename_prefix": "animpilot/" + out}},
}
req = urllib.request.Request(HOST + "/prompt", data=json.dumps({"prompt": g}).encode(), headers={"Content-Type": "application/json"})
pid = json.load(urllib.request.urlopen(req))["prompt_id"]
t0 = time.time()
while True:
    time.sleep(5)
    hist = json.load(urllib.request.urlopen(HOST + "/history/" + pid))
    if pid in hist:
        st = hist[pid].get("status", {})
        print("done", st.get("status_str"), round(time.time() - t0), "s")
        if st.get("status_str") != "success": print(json.dumps(st)[:2000])
        break
