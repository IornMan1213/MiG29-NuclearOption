"""The base livery as one flat grey: the source model's paint without its camouflage, national stars or board numbers.

The base texture (mig29_basecolor.png) is the default livery and the source every other livery is painted from. Its two-tone
grey camouflage, the red stars on the wings and fins and the "51" on the nose showed through on other liveries as patches and
star outlines (the stars' blue and white rims, and the edges of the numbers, are not red, so the generators left them: player
reports and a user screenshot of star outlines on the wings and fins). Here the exterior paint becomes the camouflage's lighter
grey, keeping only the paint's detail (panel lines, rivets, weathering: tools/livery_shade.py), and the stars and numbers are
painted out with their rims. Everything that is not paint stays as it was: stencils, warning marks, the red and green light
lenses, the radome, intakes, nozzles. Liveries that want stars or numbers draw their own (tools/livery_profiles.py).

The stars are found by their blue rim; the board numbers by where they are in this texture (the Sketchfab model's 2048 layout,
two "51"s). The camouflage's two tones are kept in mig29_basecolor.orig.png for the desert livery.

Reads mig29_basecolor.orig.png (made from mig29_basecolor.png on the first run), writes mig29_basecolor.png: safe to run on every
build, before the livery tools.
python tools/base_flat.py <MiG29Source dir>"""
import json, os, shutil, sys
from collections import deque
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from livery_shade import detail_shade

SRC = sys.argv[1]
OUT = os.path.join(SRC, "mig29_basecolor.png")
ORIG = os.path.join(SRC, "mig29_basecolor.orig.png")
if not os.path.exists(ORIG):
    shutil.copy(OUT, ORIG)
# the board numbers in the source texture, as fractions of it (x0, y0, x1, y1): two "51"s
NUMBERS = [(0.012, 0.064, 0.040, 0.092), (0.181, 0.064, 0.209, 0.092)]

im = np.asarray(Image.open(ORIG).convert("RGB"), np.float32) / 255
N = im.shape[0]
mesh = json.load(open(os.path.join(SRC, "mig29_mesh.json")))


def uvmask(names, grow):
    m = Image.new("L", (N, N), 0); dr = ImageDraw.Draw(m)
    for p in mesh["parts"]:
        if p["name"] not in names: continue
        uv = np.array(p["uvs"]).reshape(-1, 2); t = np.array(p["triangles"]).reshape(-1, 3)
        for tr in t: dr.polygon([(uv[k][0] % 1 * N, (1 - uv[k][1] % 1) * N) for k in tr], fill=255)
    return np.asarray(m.filter(ImageFilter.MaxFilter(grow))) > 0


def grow(mask, px):
    return np.asarray(Image.fromarray(mask.astype(np.uint8) * 255).filter(ImageFilter.MaxFilter(px))) > 0


exterior = uvmask([p["name"] for p in mesh["parts"] if p["name"] not in ("cockpit", "canopy")], 13) & ~uvmask(["cockpit"], 3)
r, g, b = im[..., 0], im[..., 1], im[..., 2]
sat = (im.max(-1) - im.min(-1)) / (im.max(-1) + 1e-6); lum = 0.299 * r + 0.587 * g + 0.114 * b
paint = exterior & (sat < 0.28) & (lum > 0.42)

# markings to paint out: red shapes with a blue rim (the stars), and the board numbers; each grown over its rims
red = exterior & (r > 0.35) & (r > 1.35 * g) & (r > 1.35 * b)
blue_near = grow((b > 1.25 * r) & (b > 1.05 * g) & (b > 0.25), 9)
marks = np.zeros_like(red)
seen = np.zeros_like(red)
stars = 0
for y0, x0 in zip(*np.nonzero(red)):
    if seen[y0, x0]: continue
    q, blob = deque([(y0, x0)]), []
    seen[y0, x0] = True
    while q:
        y, x = q.popleft(); blob.append((y, x))
        for yy, xx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
            if 0 <= yy < N and 0 <= xx < N and red[yy, xx] and not seen[yy, xx]:
                seen[yy, xx] = True; q.append((yy, xx))
    ys, xs = np.array(blob).T
    if len(blob) >= 120 and blue_near[ys, xs].mean() > 0.5:
        marks[ys, xs] = True; stars += 1
for x0, y0, x1, y1 in NUMBERS:
    box = np.zeros_like(red); box[int(y0 * N):int(y1 * N), int(x0 * N):int(x1 * N)] = True
    marks |= box & red
marks = grow(marks, 21) & exterior   # the blue and white rims, which reach ~8-10 px past the red at the star points

# one grey: the camouflage's lighter tone (the darker one is greenish), as the paint's median colour
green = paint & ((g - r) > 0.035)
grey = np.median(im[paint & ~green & ~marks], axis=0)
shade = detail_shade(lum, paint & ~marks)
shade[marks] = 1.0                    # painted-out markings: plain grey, no outline
sel = (paint | marks) & exterior
out = im.copy()
out[sel] = np.clip(grey[None, :] * shade[sel][:, None], 0, 1)
Image.fromarray((out * 255 + 0.5).astype(np.uint8)).save(OUT)
print(f"[base] flat grey {np.round(grey * 255).astype(int).tolist()} on {sel.mean() * 100:.1f} % of the texture; "
      f"{stars} stars and {len(NUMBERS)} board numbers painted out ({marks.sum()} texels)")
