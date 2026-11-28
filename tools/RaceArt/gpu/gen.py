"""Queue FLUX.1-schnell images on the box's ComfyUI (127.0.0.1:18188) and save them.

usage: python3 gen.py <out dir> <seed> <width> <height> "<prompt>" [count]
"""
import json
import os
import sys
import time
import urllib.parse
import urllib.request

API = "http://127.0.0.1:18188"


def post(path, body):
    req = urllib.request.Request(API + path, json.dumps(body).encode(), {"Content-Type": "application/json"})
    return json.load(urllib.request.urlopen(req))


def graph(prompt, seed, w, h, count):
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "flux1-schnell-fp8.safetensors"}},
        "2": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["1", 1]}},
        "3": {"class_type": "CLIPTextEncode", "inputs": {"text": "", "clip": ["1", 1]}},
        "4": {"class_type": "EmptySD3LatentImage", "inputs": {"width": w, "height": h, "batch_size": count}},
        "5": {"class_type": "KSampler", "inputs": {"model": ["1", 0], "positive": ["2", 0], "negative": ["3", 0],
                                                  "latent_image": ["4", 0], "seed": seed, "steps": 4, "cfg": 1.0,
                                                  "sampler_name": "euler", "scheduler": "simple", "denoise": 1.0}},
        "6": {"class_type": "VAEDecode", "inputs": {"samples": ["5", 0], "vae": ["1", 2]}},
        "7": {"class_type": "SaveImage", "inputs": {"images": ["6", 0], "filename_prefix": "gallean"}},
    }


def main(out, seed, w, h, prompt, count="1"):
    os.makedirs(out, exist_ok=True)
    pid = post("/prompt", {"prompt": graph(prompt, int(seed), int(w), int(h), int(count)), "client_id": "gallean"})["prompt_id"]
    start = time.time()
    while True:
        hist = json.load(urllib.request.urlopen(f"{API}/history/{pid}"))
        if pid in hist and hist[pid].get("outputs"):
            break
        if pid in hist and hist[pid].get("status", {}).get("status_str") == "error":
            sys.exit(json.dumps(hist[pid]["status"])[:2000])
        time.sleep(1)
    for img in hist[pid]["outputs"]["7"]["images"]:
        q = urllib.parse.urlencode({"filename": img["filename"], "subfolder": img["subfolder"], "type": img["type"]})
        data = urllib.request.urlopen(f"{API}/view?{q}").read()
        path = os.path.join(out, f"s{seed}_{img['filename']}")
        open(path, "wb").write(data)
        print(path)
    print(f"{time.time() - start:.1f} s")


if __name__ == "__main__":
    main(*sys.argv[1:])
