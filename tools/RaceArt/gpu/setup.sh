#!/bin/bash
# Gallean race art: set up a Vast.ai ComfyUI-template box (FLUX.1-schnell, Hunyuan3D-2.1, Blender).
# Run on the box: nohup bash setup.sh > /workspace/setup.log 2>&1 &
set -x
W=/workspace
cd $W

stage() { echo "=== STAGE $1 $(date +%T)"; }

stage flux
# FLUX.1-schnell (Apache 2.0, not gated): ComfyUI all-in-one fp8 checkpoint (model + T5 + CLIP + VAE).
wget -q -c -O $W/ComfyUI/models/checkpoints/flux1-schnell-fp8.safetensors \
  https://huggingface.co/Comfy-Org/flux1-schnell/resolve/main/flux1-schnell-fp8.safetensors
ls -la $W/ComfyUI/models/checkpoints/

stage blender
wget -q -c -O $W/blender.tar.xz https://download.blender.org/release/Blender4.5/blender-4.5.4-linux-x64.tar.xz \
  && tar -xf $W/blender.tar.xz -C $W && rm $W/blender.tar.xz
ln -sf $W/blender-4.5.4-linux-x64/blender /usr/local/bin/blender
apt-get install -y -qq libsm6 libxext6 libxfixes3 >/dev/null 2>&1
blender -b --version | head -1

stage hunyuan-clone
git clone --depth 1 https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1 $W/Hunyuan3D-2.1

stage hunyuan-venv
uv venv -q --python 3.12 /venv/h3d
. /venv/h3d/bin/activate
uv pip install -q torch torchvision --index-url https://download.pytorch.org/whl/cu130
cd $W/Hunyuan3D-2.1
# bpy and some pins don't exist for py3.12; install the rest, report failures.
grep -v -i -E "^(bpy|torch|torchvision|torchaudio)([=<> ]|$)" requirements.txt > /tmp/req.txt
uv pip install -q -r /tmp/req.txt || echo "REQ FAILED"
python -c "import torch;print('h3d torch',torch.__version__,torch.cuda.is_available())"

stage hunyuan-build
export CUDA_HOME=/usr/local/cuda TORCH_CUDA_ARCH_LIST=8.9
(cd hy3dpaint/custom_rasterizer && uv pip install -q --no-build-isolation . ) || echo "RASTERIZER FAILED"
(cd hy3dpaint/DifferentiableRenderer && bash compile_mesh_painter.sh) || echo "MESH PAINTER FAILED"
mkdir -p hy3dpaint/ckpt && wget -q -c -O hy3dpaint/ckpt/RealESRGAN_x4plus.pth \
  https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth

stage hunyuan-weights
uv pip install -q "huggingface_hub[cli]"
hf download tencent/Hunyuan3D-2.1 --local-dir $W/hf/Hunyuan3D-2.1 || echo "WEIGHTS FAILED"
du -sh $W/hf/Hunyuan3D-2.1

stage done
df -h $W | tail -1
