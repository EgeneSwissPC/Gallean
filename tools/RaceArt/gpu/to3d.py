"""Concept image -> textured 3D model with Hunyuan3D-2.1. Run from /workspace/Hunyuan3D-2.1 in /venv/h3d.

usage: HY3DGEN_MODELS=/workspace/hf python to3d.py <image.png> <out dir>
"""
import os
import sys
import time

sys.path.insert(0, "./hy3dshape")
sys.path.insert(0, "./hy3dpaint")
import torch  # noqa: E402,F401  (loads libc10 before the custom rasterizer)
from PIL import Image  # noqa: E402
from torchvision_fix import apply_fix  # noqa: E402

apply_fix()
from hy3dshape.rembg import BackgroundRemover  # noqa: E402
from hy3dshape.pipelines import Hunyuan3DDiTFlowMatchingPipeline  # noqa: E402
from textureGenPipeline import Hunyuan3DPaintPipeline, Hunyuan3DPaintConfig  # noqa: E402

src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
t = time.time()
image = BackgroundRemover()(Image.open(src).convert("RGB"))
image.save(f"{out}/cutout.png")
shape = Hunyuan3DDiTFlowMatchingPipeline.from_pretrained("Hunyuan3D-2.1")
mesh = shape(image=image)[0]
mesh.export(f"{out}/shape.glb")
print(f"shape {time.time() - t:.0f} s, {len(mesh.faces)} faces", flush=True)
del shape
torch.cuda.empty_cache()

conf = Hunyuan3DPaintConfig(max_num_view=8, resolution=768)
conf.realesrgan_ckpt_path = "hy3dpaint/ckpt/RealESRGAN_x4plus.pth"
conf.multiview_cfg_path = "hy3dpaint/cfgs/hunyuan-paint-pbr.yaml"
conf.custom_pipeline = "hy3dpaint/hunyuanpaintpbr"
paint = Hunyuan3DPaintPipeline(conf)
paint(mesh_path=f"{out}/shape.glb", image_path=f"{out}/cutout.png", output_mesh_path=f"{out}/textured.obj")
print(f"done {time.time() - t:.0f} s, peak VRAM {torch.cuda.max_memory_allocated() / 2**30:.1f} GiB")
