"""Extract every asset of one unit from a Gallean pack into a folder.

usage: python3 -I extract_unit.py <pack.galleanpack> <unit id, e.g. g000uu5126> <out dir>

Writes the WebP images as they are stored (colour + Normal/Depth/Albedo layers) and
unit.json with the metadata needed to pack them again (build_pack.py).
Asset IDs are matched on the path segment equal to the unit ID (battle-unit/<id>/...,
stack-unit/<id>/..., unit-face/<id>, ...).
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from galleanpack import PackReader  # noqa: E402


def main(pack_path, unit_id, out_dir):
    unit_id = unit_id.lower()
    pack = PackReader(pack_path)
    os.makedirs(out_dir, exist_ok=True)
    result = {"unit": unit_id, "sprites": [], "animations": []}

    def save(name, data):
        path = os.path.join(out_dir, name)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as f:
            f.write(data)
        return name

    def layers(base, items):
        return [{"Type": l["Type"], "PixelWidth": l["PixelWidth"], "PixelHeight": l["PixelHeight"],
                 "file": save(f"{base}.{l['Type'].lower()}.webp", pack.blob(l["Offset"], l["Length"]))}
                for l in items]

    def matches(asset_id):
        return unit_id in asset_id.lower().split("/")

    for s in pack.manifest["Sprites"]:
        if matches(s["Id"]):
            base = s["Id"].replace("/", "_")
            result["sprites"].append({
                "id": s["Id"], "Metadata": s["Metadata"], "Transforms": s["Transforms"],
                "file": save(f"{base}.webp", pack.blob(s["Offset"], s["Length"])),
                "Layers": layers(base, s["Layers"])})

    frames = 0
    for a in pack.manifest["Animations"]:
        if matches(a["Id"]):
            folder = a["Id"].replace("/", "_")
            out = []
            for i, fr in enumerate(a["Frames"]):
                base = f"{folder}/{i:03d}"
                out.append({"Metadata": fr["Metadata"],
                            "file": save(base + ".webp", pack.blob(fr["Offset"], fr["Length"])),
                            "Layers": layers(base, fr["Layers"])})
            frames += len(out)
            result["animations"].append({"id": a["Id"], "Metadata": a["Metadata"],
                                         "Transforms": a["Transforms"], "frames": out})

    with open(os.path.join(out_dir, "unit.json"), "w") as f:
        json.dump(result, f, indent=1)
    print(f"{unit_id}: {len(result['sprites'])} stills, {len(result['animations'])} animations, {frames} frames -> {out_dir}")
    if not result["sprites"] and not result["animations"]:
        sys.exit("no assets found for this unit")


if __name__ == "__main__":
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    main(*sys.argv[1:])
