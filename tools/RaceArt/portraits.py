"""Make a unit's face, round portrait and framed event portrait in the game's layout.

usage: python portraits.py <cutout.png (RGBA, figure on transparent)> <template unit dir> <out dir>
  <template unit dir> = an extract_unit.py folder of an original unit (its pictures give the layout:
  the round portrait's circle mask and the event portrait's ornamental frame are taken from it).
Writes face.png (140x170), portrait.png (260x284, round), event.png (700x900, figure inside the frame).
Needs Pillow.

Layout facts from the original pictures: face = head and shoulders filling the frame; portrait = head
filling a circle inscribed in the 260x284 image, transparent outside; event portrait = 700x900 with the
ornamental frame baked in, picture window about x 158-542, y 215-712, transparent outside the frame.
"""
import glob
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

cutout_path, template, out = sys.argv[1:4]
os.makedirs(out, exist_ok=True)
fig = Image.open(cutout_path).convert("RGBA")
fig = fig.crop(fig.getchannel("A").getbbox())
W, H = fig.size


def background(size, top=(14, 12, 10), bottom=(92, 52, 22)):
    """Dark top, warm brown bottom, like the original event portraits."""
    w, h = size
    bg = Image.new("RGBA", size)
    px = bg.load()
    for y in range(h):
        t = (y / max(1, h - 1)) ** 1.6
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
        for x in range(w):
            px[x, y] = c
    return bg


def grade(im):
    """Darken and warm the figure a little to sit with the game's painted pictures."""
    r, g, b, a = im.split()
    r = r.point(lambda v: int(v * 0.86))
    g = g.point(lambda v: int(v * 0.82))
    b = b.point(lambda v: int(v * 0.78))
    return Image.merge("RGBA", (r, g, b, a))


def shot(size, x0, y0, x1, y1):
    """Crop the figure to the fraction box (x0..x1, y0..y1 of its bbox), cover-fit into size."""
    box = (int(x0 * W), int(y0 * H), int(x1 * W), int(y1 * H))
    part = fig.crop(box)
    tw, th = size
    scale = max(tw / part.width, th / part.height)
    part = part.resize((max(1, round(part.width * scale)), max(1, round(part.height * scale))), Image.LANCZOS)
    left = (part.width - tw) // 2
    part = part.crop((left, 0, left + tw, th))
    img = background(size)
    img.alpha_composite(grade(part))
    return img


tmpl = {os.path.basename(p).split("_")[0]: p for p in glob.glob(os.path.join(template, "unit-*.webp"))}

# Face: head and shoulders, filling the frame.
face = shot((140, 170), 0.25, 0.10, 0.75, 0.34)
face.save(os.path.join(out, "face.png"))

# Round portrait: head a bit closer, circle mask inscribed in the image (as the originals).
portrait = shot((260, 284), 0.22, 0.0, 0.80, 0.38)
mask = Image.new("L", portrait.size, 0)
ImageDraw.Draw(mask).ellipse((0, 0, portrait.width - 1, portrait.height - 1), fill=255)
portrait.putalpha(mask.filter(ImageFilter.GaussianBlur(1)))
portrait.save(os.path.join(out, "portrait.png"))

# Event portrait: waist-up picture inside the template's ornamental frame.
frame = Image.open(tmpl["unit-event-portrait"]).convert("RGBA")
win = (158, 215, 542, 712)
picture = shot((win[2] - win[0], win[3] - win[1]), 0.08, 0.0, 0.92, 0.50)
event = frame.copy()
event.paste(picture, win[:2])
# keep the frame's ornaments that reach into the window (bright, saturated metal) on top
fr = frame.load()
ev = event.load()
for y in range(win[1], win[3]):
    for x in range(win[0], win[2]):
        r, g, b, a = fr[x, y]
        edge = min(x - win[0], win[2] - 1 - x, y - win[1], win[3] - 1 - y)
        if edge < 14 and a > 200 and max(r, g, b) > 110:
            ev[x, y] = (r, g, b, a)
event.save(os.path.join(out, "event.png"))
print("wrote face.png portrait.png event.png to", out)
