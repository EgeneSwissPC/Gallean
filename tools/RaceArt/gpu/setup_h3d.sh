#!/bin/bash
# Hunyuan3D-2.1 on the Vast box: Python 3.10 (its pins have no 3.12 wheels), no Chinese mirrors, no deepspeed.
set -x
W=/workspace
stage() { echo "=== STAGE $1 $(date +%T)"; }
stage venv
rm -rf /venv/h3d
uv venv -q --python 3.10 /venv/h3d
. /venv/h3d/bin/activate
uv pip install -q torch torchvision --index-url https://download.pytorch.org/whl/cu130
cd $W/Hunyuan3D-2.1
grep -v -E "^\s*(--extra-index-url|#|$)" requirements.txt \
  | grep -v -i -E "^(bpy|torch|torchvision|torchaudio|deepspeed|cupy-cuda12x)([=<> ]|$)" > /tmp/req.txt
echo "cupy-cuda13x" >> /tmp/req.txt
timeout 1800 uv pip install -q -r /tmp/req.txt || echo "REQ FAILED"
python -c "import torch;print('h3d torch',torch.__version__,torch.cuda.is_available())"

stage build
export CUDA_HOME=/usr/local/cuda TORCH_CUDA_ARCH_LIST=8.9
(cd hy3dpaint/custom_rasterizer && uv pip install -q --no-build-isolation . ) || echo "RASTERIZER FAILED"
(cd hy3dpaint/DifferentiableRenderer && bash compile_mesh_painter.sh) || echo "MESH PAINTER FAILED"
mkdir -p hy3dpaint/ckpt && wget -q -c -O hy3dpaint/ckpt/RealESRGAN_x4plus.pth \
  https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth

stage weights
hf download tencent/Hunyuan3D-2.1 --local-dir $W/hf/Hunyuan3D-2.1 >/dev/null || echo "WEIGHTS FAILED"
du -sh $W/hf/Hunyuan3D-2.1
stage done
