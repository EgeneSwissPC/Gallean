"""Pack rendered battle frames (from the Blender render script) onto a unit as a sparse pack.

usage: python3 -I pack_render.py <render dir> <unit id> <out.galleanpack> <ffmpeg> [--base <base pack>]
         [--portrait <png>] [--face <png>]

<render dir> holds frames.json and <side>/<anim>/<NNN>.png (side attacker/defender, anim idle/attack/hit).
PNGs are converted to lossless WebP with ffmpeg. The frame's foot point goes on the game's anchor
(bottom centre of the animation box); see SpriteAtlasSource.CreateAnchoredTransform.
With --base, every battle-unit-shadow / -aura animation the base pack has for the unit is replaced
by empty frames, so the old unit's shadow doesn't show under the new one.
No Normal layer is written yet.
"""
import argparse
import json
import os
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from galleanpack import PackReader, PackWriter, webp_size  # noqa: E402

ANCHOR_X, ANCHOR_Y = 400, 517   # canvas point of the foot (any value works; matches original units)


def to_webp(ffmpeg, png, size=None):
    with tempfile.NamedTemporaryFile(suffix=".webp") as tmp:
        vf = ["-vf", f"scale={size[0]}:{size[1]}:flags=lanczos"] if size else []
        subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-i", png, *vf, "-c:v", "libwebp", "-lossless", "1",
                        "-compression_level", "4", tmp.name], check=True)
        return open(tmp.name, "rb").read()


def empty_webp(ffmpeg):
    with tempfile.NamedTemporaryFile(suffix=".png") as png:
        subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=black@0.0:s=1x1,format=rgba",
                        "-frames:v", "1", png.name], check=True)
        return to_webp(ffmpeg, png.name)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("render")
    p.add_argument("unit")
    p.add_argument("out")
    p.add_argument("ffmpeg")
    p.add_argument("--base")
    p.add_argument("--portrait")
    p.add_argument("--face")
    a = p.parse_args()
    unit = a.unit.lower()
    info = json.load(open(os.path.join(a.render, "frames.json")))
    cw, ch = info["canvas"]
    foot = info["foot_from_bottom"]
    anim_box = {"Width": cw, "Height": ch - foot, "OffsetX": ANCHOR_X - cw // 2, "OffsetY": ANCHOR_Y - (ch - foot)}
    writer = PackWriter(a.out, sparse=True)
    counts = {}
    for side in ("attacker", "defender"):
        for anim, count in info["anims"].items():
            frames = []
            for i in range(count):
                data = to_webp(a.ffmpeg, os.path.join(a.render, side, anim, f"{i:03d}.png"))
                pw, ph = webp_size(data)
                frames.append({"Metadata": {"Width": cw, "Height": ch, "PixelWidth": pw, "PixelHeight": ph,
                                            "OffsetX": anim_box["OffsetX"], "OffsetY": anim_box["OffsetY"],
                                            "Additive": False}, "data": data})
            trigger = info["trigger"].get(anim)
            meta = dict(anim_box, FrameCount=count, TriggerFrame=trigger, SecondTriggerFrame=0 if trigger else None)
            writer.add_animation(f"battle-unit/{unit}/{anim}/{side}", meta, frames, [])
            counts[(anim, side)] = count
            print(f"battle-unit/{unit}/{anim}/{side}: {count} frames")

    if a.base:
        blank = empty_webp(a.ffmpeg)
        base = PackReader(a.base)
        for anim in base.manifest["Animations"]:
            parts = anim["Id"].split("/")
            if parts[0] in ("battle-unit-shadow", "battle-unit-aura") and parts[1].lower() == unit:
                count = counts.get((parts[2], parts[3]), anim["Metadata"]["FrameCount"])
                md = {"Width": 1, "Height": 1, "PixelWidth": 1, "PixelHeight": 1, "OffsetX": ANCHOR_X, "OffsetY": ANCHOR_Y,
                      "Additive": False}
                meta = {"FrameCount": count, "Width": 1, "Height": 1, "OffsetX": ANCHOR_X, "OffsetY": ANCHOR_Y,
                        "TriggerFrame": None, "SecondTriggerFrame": None}
                writer.add_animation(anim["Id"], meta, [{"Metadata": md, "data": blank}] * count, anim["Transforms"])
                print(f"{anim['Id']}: blanked")

    for kind, path, size in (("unit-portrait", a.portrait, (130, 142)), ("unit-face", a.face, (70, 85))):
        if path:
            data = to_webp(a.ffmpeg, path, (size[0] * 2, size[1] * 2))
            writer.add_sprite(f"{kind}/{unit}", {"Width": size[0], "Height": size[1], "PixelWidth": size[0] * 2,
                                                 "PixelHeight": size[1] * 2, "OffsetX": 0, "OffsetY": 0,
                                                 "Additive": False}, data, [])
            print(f"{kind}/{unit}")
    writer.complete()
    print(f"wrote {a.out} ({os.path.getsize(a.out)} bytes)")


if __name__ == "__main__":
    main()
