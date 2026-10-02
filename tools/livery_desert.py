"""Desert-tan livery: recolour only the two camo tones on texels used by exterior parts (UV masks from mig29_mesh.json)."""
import json, sys, numpy as np
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
green = paint & ((g - r) > 0.035); light = paint & ~green
out = im.copy()
for mask, target in ((light, np.array([0.80, 0.70, 0.52])), (green, np.array([0.58, 0.45, 0.30]))):
    out[mask] = np.clip(target[None, :] * (lum[mask] / lum[mask].mean())[:, None], 0, 1)
Image.fromarray((out * 255).astype(np.uint8)).save(DST)
print("desert livery ->", DST)
