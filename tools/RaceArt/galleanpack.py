"""Read and write Gallean asset packs (format 27) with the Python standard library.

Layout (Gallean.Assets/AssetPack.cs): 32-byte header ("GALLEANPACK", int32 format at 12,
int64 manifest file offset at 16, int64 manifest length at 24), then blobs (blob offsets in
the manifest are relative to the end of the header), then the JSON manifest.
"""
import datetime
import hashlib
import json
import os
import struct

MAGIC = b"GALLEANPACK"
FORMAT = 27
HEADER = 32


class PackReader:
    def __init__(self, path):
        self.file = open(path, "rb")
        header = self.file.read(HEADER)
        if header[:len(MAGIC)] != MAGIC:
            raise ValueError(f"{path} is not a Gallean asset pack")
        fmt = struct.unpack_from("<i", header, 12)[0]
        if fmt != FORMAT:
            raise ValueError(f"{path}: unsupported pack format {fmt} (expected {FORMAT})")
        offset, length = struct.unpack_from("<qq", header, 16)
        self.file.seek(offset)
        self.manifest = json.loads(self.file.read(length))

    def blob(self, offset, length):
        self.file.seek(HEADER + offset)
        data = self.file.read(length)
        if len(data) != length:
            raise EOFError("blob past end of pack")
        return data

    def close(self):
        self.file.close()


class PackWriter:
    """Mirrors AssetPack.Writer: dedupes identical blobs by SHA-256."""

    def __init__(self, path, sparse=True, created_by="tools/RaceArt"):
        self.path = path
        self.tmp = path + ".tmp"
        self.file = open(self.tmp, "wb")
        self.file.write(b"\0" * HEADER)
        self.sparse = sparse
        self.created_by = created_by
        self.seen = {}
        self.sprites, self.animations, self.sounds = [], [], []

    def _blob(self, data):
        key = hashlib.sha256(data).hexdigest()
        if key not in self.seen:
            offset = self.file.tell() - HEADER
            self.file.write(data)
            self.seen[key] = (offset, len(data))
        return self.seen[key]

    def _layers(self, layers):
        out = []
        for layer in layers:
            offset, length = self._blob(layer["data"])
            out.append({"Type": layer["Type"], "PixelWidth": layer["PixelWidth"],
                        "PixelHeight": layer["PixelHeight"], "Offset": offset, "Length": length})
        return out

    def add_sprite(self, asset_id, metadata, data, transforms, layers=()):
        offset, length = self._blob(data)
        self.sprites.append({"Id": asset_id, "Metadata": metadata, "Offset": offset, "Length": length,
                             "Transforms": transforms, "Layers": self._layers(layers)})

    def add_animation(self, asset_id, metadata, frames, transforms):
        if not frames or metadata["FrameCount"] != len(frames):
            raise ValueError(f"{asset_id}: FrameCount {metadata['FrameCount']} != {len(frames)} frames")
        out = []
        for frame in frames:
            offset, length = self._blob(frame["data"])
            out.append({"Metadata": frame["Metadata"], "Offset": offset, "Length": length,
                        "Layers": self._layers(frame.get("Layers", ()))})
        self.animations.append({"Id": asset_id, "Metadata": metadata, "Frames": out, "Transforms": transforms})

    def complete(self):
        now = datetime.datetime.now(datetime.timezone.utc).isoformat()
        manifest = {"CreatedAt": now, "CreatedBy": self.created_by, "Sparse": self.sparse,
                    "Sprites": self.sprites, "Animations": self.animations, "Sounds": self.sounds}
        data = json.dumps(manifest, separators=(",", ":")).encode()
        offset = self.file.tell()
        self.file.write(data)
        self.file.seek(0)
        self.file.write(MAGIC + b"\0" + struct.pack("<i", FORMAT) + struct.pack("<qq", offset, len(data)))
        self.file.close()
        os.replace(self.tmp, self.path)


def webp_size(data):
    """Pixel size of a WebP image (VP8, VP8L or VP8X)."""
    if data[:4] != b"RIFF" or data[8:12] != b"WEBP":
        raise ValueError("not a WebP image")
    chunk = data[12:16]
    if chunk == b"VP8X":
        return 1 + int.from_bytes(data[24:27], "little"), 1 + int.from_bytes(data[27:30], "little")
    if chunk == b"VP8L":
        bits = int.from_bytes(data[21:25], "little")
        return 1 + (bits & 0x3FFF), 1 + ((bits >> 14) & 0x3FFF)
    if chunk == b"VP8 ":
        w, h = struct.unpack_from("<HH", data, 26)
        return w & 0x3FFF, h & 0x3FFF
    raise ValueError("unknown WebP chunk")
