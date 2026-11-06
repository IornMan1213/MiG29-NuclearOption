"""Texture templates for players' liveries made with Shockfront's livery kit (liveries/README.md).

python tools/livery_template.py <mig29_basecolor.png> <mig29_mesh.json> <out dir>

Writes, at the texture's size (2048 x 2048):
  MiG-29_basecolor.png   the stock two-tone grey with its panel lines, stencils and insignia: the starting point to paint over
  MiG-29_uv_layout.png   the exterior's UV wireframe on transparency, to lay over the paint as a guide layer
The MiG-29 Fulcrum and MiG-29SMT share the mesh and the texture, so one set covers both. The canopy glass and the cockpit
(its own texture area, not on the outside) are left out, like in the tools/livery_*.py schemes.
"""
import json, os, shutil, sys
import numpy as np
from PIL import Image, ImageDraw

SRC, MESH, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(OUT, exist_ok=True)
base = Image.open(SRC)
N = base.size[0]
shutil.copyfile(SRC, os.path.join(OUT, "MiG-29_basecolor.png"))

layout = Image.new("RGBA", (N, N), (0, 0, 0, 0))
draw = ImageDraw.Draw(layout)
edges = set()
for part in json.load(open(MESH))["parts"]:
    if part["name"] in ("canopy", "cockpit"):
        continue
    uv = np.array(part["uvs"], np.float32).reshape(-1, 2) % 1.0
    px = np.stack([uv[:, 0] * N, (1 - uv[:, 1]) * N], 1)
    for a, b, c in np.array(part["triangles"]).reshape(-1, 3):
        for i, j in ((a, b), (b, c), (c, a)):
            p, q = tuple(np.round(px[i], 1)), tuple(np.round(px[j], 1))
            edges.add((min(p, q), max(p, q)))   # each shared edge once
for p, q in edges:
    draw.line([p, q], fill=(255, 64, 160, 200), width=1)
layout.save(os.path.join(OUT, "MiG-29_uv_layout.png"))
print(f"{len(edges)} edges, {N} x {N} -> {OUT}")
