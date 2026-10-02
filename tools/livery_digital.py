"""Digital-grey livery: a blocky three-tone pixel camouflage painted over the exterior paint texels.

python tools/livery_digital.py <mig29_basecolor.png> <mig29_mesh.json> <out.png>

Same masking as livery_desert.py: only low-saturation paint on exterior parts changes, so markings (stars, numbers, stencils) and
panel-line shading survive. The pattern lives in texture space: 16-texel blocks grouped into larger blotches.
"""
import json, sys
import numpy as np
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

# pattern: smooth noise at blotch scale, sampled per 16-texel block, then thresholded into three tones
rng = np.random.default_rng(2912)
B = 16; nb = N // B


def smooth(cells):
    small = rng.random((cells, cells)).astype(np.float32)
    return np.asarray(Image.fromarray((small * 255).astype(np.uint8)).resize((nb, nb), Image.BICUBIC)).astype(np.float32) / 255


field = 0.65 * smooth(nb // 6) + 0.35 * smooth(nb // 2)          # per-block value
field += (rng.random((nb, nb)).astype(np.float32) - 0.5) * 0.18   # block-level jitter -> ragged pixel edges
q = np.digitize(field, np.quantile(field, [0.45, 0.80]))           # 45 % light, 35 % mid, 20 % dark
tones = np.array([[0.74, 0.76, 0.78], [0.53, 0.58, 0.63], [0.34, 0.37, 0.41]], np.float32)
pattern = tones[np.kron(q, np.ones((B, B), int))]

out = im.copy()
# keep panel lines, rivets and weathering but not the old camo shapes: brightness relative to a blurred copy (paint texels only)
lp = np.where(paint, lum, 0).astype(np.float32); wp = paint.astype(np.float32)
blur = lambda a: np.asarray(Image.fromarray((a * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(10)), np.float32) / 255
local = blur(lp) / np.maximum(blur(wp), 1e-3)
shade = np.clip(lum / np.maximum(local, 1e-3), 0.6, 1.25)[..., None]
out[paint] = np.clip(pattern[paint] * shade[paint], 0, 1)
Image.fromarray((out * 255).astype(np.uint8)).save(DST)
print("digital livery ->", DST)
