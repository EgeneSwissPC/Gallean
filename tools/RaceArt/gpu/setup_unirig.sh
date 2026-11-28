#!/bin/bash
# UniRig (automatic skeleton + skinning) on the Vast box. Python 3.11, torch 2.5.1 cu124 (prebuilt pyg/spconv/flash-attn wheels exist).
set -x
stage() { echo "=== STAGE $1 $(date +%T)"; }
stage venv
uv venv -q --python 3.11 /venv/unirig
. /venv/unirig/bin/activate
uv pip install -q torch==2.5.1 torchvision==0.20.1 --index-url https://download.pytorch.org/whl/cu124
cd /workspace/UniRig
grep -v -E "^(flash_attn|wandb1?)$" requirements.txt > /tmp/unirig-req.txt
uv pip install -q -r /tmp/unirig-req.txt || echo "REQ FAILED"
uv pip install -q spconv-cu124 || echo "SPCONV FAILED"
uv pip install -q torch_scatter torch_cluster -f https://data.pyg.org/whl/torch-2.5.0+cu124.html || echo "PYG FAILED"
uv pip install -q "https://github.com/Dao-AILab/flash-attention/releases/download/v2.7.4.post1/flash_attn-2.7.4.post1+cu12torch2.5cxx11abiFALSE-cp311-cp311-linux_x86_64.whl" || echo "FLASH FAILED"
uv pip install -q numpy==1.26.4 wandb
python -c "import torch, bpy, spconv, torch_scatter, flash_attn; print('unirig ok', torch.cuda.is_available(), bpy.app.version_string)"
stage done
