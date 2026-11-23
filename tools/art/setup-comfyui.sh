#!/usr/bin/env bash
# Local image generation for Rogue Stars art: ComfyUI (Apple Silicon / MPS) + models.
# Idempotent: re-run to resume interrupted downloads or to update.
#
#   tools/art/setup-comfyui.sh            # install into ~/AI/ComfyUI (COMFY_HOME to override)
#
# Models (all usable commercially; check again before adding others):
#   - SDXL base 1.0 + fp16-fixed VAE      (CreativeML Open RAIL++-M)
#   - FLUX.1 [schnell] GGUF Q4_K_S + T5 GGUF + CLIP-L + VAE   (Apache 2.0)
# Background removal (rembg/BiRefNet) is installed into the same venv.
set -euo pipefail

COMFY_HOME=${COMFY_HOME:-$HOME/AI/ComfyUI}
PY=cpython-3.12-macos-aarch64-none
HF=https://huggingface.co

if [[ $(arch -arm64 uname -m) != arm64 ]]; then echo "needs Apple Silicon" >&2; exit 1; fi
A="arch -arm64"

mkdir -p "$(dirname "$COMFY_HOME")"
if [[ ! -d $COMFY_HOME/.git ]]; then
  git clone --depth 1 https://github.com/comfyanonymous/ComfyUI "$COMFY_HOME"
else
  git -C "$COMFY_HOME" pull --ff-only || true
fi
cd "$COMFY_HOME"

$A uv python install "$PY"
[[ -d .venv ]] || $A uv venv --python "$PY" .venv
$A uv pip install --python .venv/bin/python torch torchvision torchaudio
$A uv pip install --python .venv/bin/python -r requirements.txt

# GGUF loader for quantized Flux / T5.
if [[ ! -d custom_nodes/ComfyUI-GGUF ]]; then
  git clone --depth 1 https://github.com/city96/ComfyUI-GGUF custom_nodes/ComfyUI-GGUF
fi
$A uv pip install --python .venv/bin/python -r custom_nodes/ComfyUI-GGUF/requirements.txt
$A uv pip install --python .venv/bin/python "rembg[cpu]" pillow
# IP-Adapter (style references for SDXL).
if [[ ! -d custom_nodes/ComfyUI_IPAdapter_plus ]]; then
  git clone --depth 1 https://github.com/cubiq/ComfyUI_IPAdapter_plus custom_nodes/ComfyUI_IPAdapter_plus
fi

fetch() { # url dest
  local url=$1 dest=$2
  mkdir -p "$(dirname "$dest")"
  if [[ -f $dest && ! -f $dest.part ]]; then echo "ok   $dest"; return; fi
  echo "get  $dest"
  curl -fL --retry 5 --retry-delay 10 -C - -o "$dest.part" "$url" && mv "$dest.part" "$dest"
}

M=models
fetch $HF/stabilityai/stable-diffusion-xl-base-1.0/resolve/main/sd_xl_base_1.0.safetensors $M/checkpoints/sd_xl_base_1.0.safetensors
fetch $HF/madebyollin/sdxl-vae-fp16-fix/resolve/main/sdxl_vae.safetensors $M/vae/sdxl_vae_fp16fix.safetensors
fetch $HF/city96/FLUX.1-schnell-gguf/resolve/main/flux1-schnell-Q4_K_S.gguf $M/unet/flux1-schnell-Q4_K_S.gguf
fetch $HF/city96/t5-v1_1-xxl-encoder-gguf/resolve/main/t5-v1_1-xxl-encoder-Q5_K_M.gguf $M/clip/t5-v1_1-xxl-encoder-Q5_K_M.gguf
fetch $HF/comfyanonymous/flux_text_encoders/resolve/main/clip_l.safetensors $M/clip/clip_l.safetensors
# Style references: IP-Adapter Plus for SDXL + its ViT-H image encoder (Apache 2.0).
fetch $HF/h94/IP-Adapter/resolve/main/sdxl_models/ip-adapter-plus_sdxl_vit-h.safetensors $M/ipadapter/ip-adapter-plus_sdxl_vit-h.safetensors
fetch $HF/h94/IP-Adapter/resolve/main/models/image_encoder/model.safetensors $M/clip_vision/CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors
# FLUX.1 [schnell] VAE (Apache 2.0). The official repo is gated (needs a login); fall back to an ungated mirror.
fetch $HF/black-forest-labs/FLUX.1-schnell/resolve/main/ae.safetensors $M/vae/flux_ae.safetensors \
  || fetch $HF/second-state/FLUX.1-schnell-GGUF/resolve/main/ae.safetensors $M/vae/flux_ae.safetensors \
  || echo "WARN flux VAE not downloaded (Flux workflows unavailable)"

echo "done: start with  arch -arm64 $COMFY_HOME/.venv/bin/python $COMFY_HOME/main.py --listen 127.0.0.1 --port 8188"
