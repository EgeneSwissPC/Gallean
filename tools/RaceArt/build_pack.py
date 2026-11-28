"""Write a sparse (overlay) Gallean pack from unit folders made by extract_unit.py.

usage: python3 -I build_pack.py <out.galleanpack> <unit dir>[=<target unit id>] ...

Each folder's assets are written under the target unit ID (default: the folder's own unit),
so `mermaid=g000uu0001` puts the Mermaid's art on unit g000uu0001. Images may be replaced
in the folder (same file names); pixel sizes are re-read from the WebP files, and the
logical Width/Height in unit.json must keep the same aspect ratio as the pixels.
Copy the result next to the base pack (~/.local/share/GalleanUpgrade/Assets/); the game
loads sparse packs after the base pack, so their assets win.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from galleanpack import PackWriter, webp_size  # noqa: E402


def read(folder, name):
    with open(os.path.join(folder, name), "rb") as f:
        return f.read()


def image(folder, metadata, name):
    data = read(folder, name)
    w, h = webp_size(data)
    if w * metadata["Height"] != h * metadata["Width"]:
        raise ValueError(f"{name}: {w}x{h} px does not match logical {metadata['Width']}x{metadata['Height']}")
    return data, dict(metadata, PixelWidth=w, PixelHeight=h)


def layers(folder, items):
    out = []
    for l in items:
        data = read(folder, l["file"])
        w, h = webp_size(data)
        out.append({"Type": l["Type"], "PixelWidth": w, "PixelHeight": h, "data": data})
    return out


def rename(asset_id, source, target):
    return "/".join(target if part.lower() == source else part for part in asset_id.split("/"))


def main(out_path, specs):
    writer = PackWriter(out_path, sparse=True)
    for spec in specs:
        folder, _, target = spec.partition("=")
        with open(os.path.join(folder, "unit.json")) as f:
            unit = json.load(f)
        source = unit["unit"]
        target = (target or source).lower()
        for s in unit["sprites"]:
            data, md = image(folder, s["Metadata"], s["file"])
            writer.add_sprite(rename(s["id"], source, target), md, data, s["Transforms"], layers(folder, s["Layers"]))
        for a in unit["animations"]:
            frames = []
            for fr in a["frames"]:
                data, md = image(folder, fr["Metadata"], fr["file"])
                frames.append({"Metadata": md, "data": data, "Layers": layers(folder, fr["Layers"])})
            writer.add_animation(rename(a["id"], source, target), dict(a["Metadata"], FrameCount=len(frames)),
                                 frames, a["Transforms"])
        print(f"{source} -> {target}: {len(unit['sprites'])} stills, {len(unit['animations'])} animations")
    writer.complete()
    print(f"wrote {out_path} ({os.path.getsize(out_path)} bytes)")


if __name__ == "__main__":
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2:])
