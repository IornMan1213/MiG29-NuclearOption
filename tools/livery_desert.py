"""Desert-tan livery: recolour only the two camo tones on texels used by exterior parts (UV masks from mig29_mesh.json).
The two tones come from the source model's camouflage (mig29_basecolor.orig.png, kept by tools/base_flat.py), the paint's detail
from the base (the flat grey one: no stars or board numbers come through)."""
import json, os, sys, numpy as np
from PIL import Image, ImageDraw, ImageFilter
SRC, MESH, DST = sys.argv[1], sys.argv[2], sys.argv[3]
d = json.load(open(MESH)); N = 2048
def uvmask(names, grow):
    m = Image.new("L", (N, N), 0); dr = ImageDraw.Draw(m)
    for p in d["parts"]:
        if p["name"] not in names: continue
        uv = np.array(p["uvs"]).reshape(-1, 2); t = np.array(p["triangles"]).reshape(-1, 3)
        for tr in t: dr.polygon([(uv[k][0] % 1 * N, (1 - uv[k][1] % 1) * N) for k in tr], fill=255)
    return np.asarray(m.filter(ImageFilter.MaxFilter(grow))) > 0
exterior = uvmask([p["name"] for p in d["parts"] if p["name"] not in ("cockpit", "canopy")], 13)
cockpit = uvmask(["cockpit"], 3)
im = np.asarray(Image.open(SRC).convert("RGB").resize((N, N))).astype(np.float32) / 255
r, g, b = im[..., 0], im[..., 1], im[..., 2]
sat = (im.max(-1) - im.min(-1)) / (im.max(-1) + 1e-6); lum = 0.299 * r + 0.587 * g + 0.114 * b
paint = exterior & ~cockpit & (sat < 0.28) & (lum > 0.42)
orig = os.path.join(os.path.dirname(SRC), "mig29_basecolor.orig.png")
tones = np.asarray(Image.open(orig if os.path.exists(orig) else SRC).convert("RGB").resize((N, N))).astype(np.float32) / 255
tg = (tones[..., 1] - tones[..., 0]) > 0.035
# under the source's markings (stars, numbers: saturated, with rims) the tone is the one round them, not the marking's own
mark = np.asarray(Image.fromarray(((tones.max(-1) - tones.min(-1)) / (tones.max(-1) + 1e-6) > 0.28).astype(np.uint8) * 255).filter(ImageFilter.MaxFilter(21))) > 0
blur = lambda a: np.asarray(Image.fromarray((a * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(12)), np.float32) / 255
around = blur((tg & ~mark).astype(np.float32)) / np.maximum(blur((~mark).astype(np.float32)), 1e-3)
green = paint & np.where(mark, around > 0.5, tg); light = paint & ~green
out = im.copy()
for mask, target in ((light, np.array([0.80, 0.70, 0.52])), (green, np.array([0.58, 0.45, 0.30]))):
    out[mask] = np.clip(target[None, :] * (lum[mask] / lum[mask].mean())[:, None], 0, 1)
Image.fromarray((out * 255).astype(np.uint8)).save(DST)
print("desert livery ->", DST)
