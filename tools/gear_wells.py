"""Wheel-well interiors for the MiG-29's gear bays: the model's bays are bare holes in the skin (black when the doors open; user
screenshot, v0.9.0). Each well follows its door outline: walls stand on the opening rim and rise to a ceiling just under the outer
skin (tools/bay_grid.py spans), faces pointing into the bay so only a look through the open doors sees them. Grey-green primer
texture with frames and stringers.
python gear_wells.py <MiG29Source>  ->  gear_wells.json, bay_atlas.png, bay_metallic.png"""
import json, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bay_grid

SRC = sys.argv[1]
d = json.load(open(os.path.join(SRC, "mig29_mesh.json")))
doors = next(p for p in d["parts"] if p["name"] == "gear_doors_closed")
DV = np.array(doors["vertices"]).reshape(-1, 3); DT = np.array(doors["triangles"]).reshape(-1, 3)
skin = bay_grid.skin_triangles(SRC)

# bays: door triangles (MiG29Polish.DoorOf rule), ceiling depth limit above the door
BAYS = {
    "nose": (lambda c: (np.abs(c[:, 0]) < 0.4) & (c[:, 2] > 3.4), 0.70),
    "main_L": (lambda c: (c[:, 0] < -1.0) & (c[:, 0] > -1.8) & (c[:, 2] > -0.3) & (c[:, 2] < 2.95), 0.42),
    "main_R": (lambda c: (c[:, 0] > 1.0) & (c[:, 0] < 1.8) & (c[:, 2] > -0.3) & (c[:, 2] < 2.95), 0.42),
}
RIM_LIFT = 0.006          # walls start just above the closed door surface
CEIL_GAP = 0.03           # ceiling this far under the outer skin
UVS = 1.0                 # metres per texture repeat (tools/bay_texture.py: one tile per metre)

cols = {}


def skin_top(x, z, y):
    """Outer skin above (x, y, z): top of the hidden span holding a point just above the door."""
    key = (round(x, 3), round(z, 3))
    if key not in cols:
        c = bay_grid.Columns(skin, x - 1e-4, x + 1e-4, z - 1e-4, z + 1e-4, 1.0)
        cols[key] = c.spans[0, 0]
    for lo, hi in cols[key]:
        if lo - 0.02 <= y <= hi: return hi
    return y + 0.25


parts = []
for name, (rule, depth) in BAYS.items():
    n = np.cross(DV[DT[:, 1]] - DV[DT[:, 0]], DV[DT[:, 2]] - DV[DT[:, 0]])
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
    T = DT[rule(DV[DT].mean(1)) & (np.abs(n[:, 1]) > 0.5)]
    # weld by position so the outline is found across split vertices
    key = {}; wid = np.array([key.setdefault(tuple(np.round(v, 4)), len(key)) for v in DV])
    W = np.zeros((len(key), 3))
    for i, k in enumerate(wid): W[k] = DV[i]
    TW = wid[T]
    edges = {}
    for t in TW:
        for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            edges.setdefault((min(a, b), max(a, b)), []).append(t)
    used = sorted({int(v) for v in TW.ravel()})
    ceil = {}
    for v in used:
        x, y, z = W[v]
        ceil[v] = min(skin_top(x, z, y + 0.02) - CEIL_GAP, y + depth)
    # smooth the ceiling over the door mesh neighbours (the skin spans step between columns)
    nb = {v: set() for v in used}
    for (a, b) in edges: nb[a].add(b); nb[b].add(a)
    for _ in range(3):
        ceil = {v: 0.5 * ceil[v] + 0.5 * np.mean([ceil[u] for u in nb[v]]) for v in used}
    centre = W[used].mean(0)
    verts, norms, uvs, tris = [], [], [], []

    def quad_or_tri(pts, inward_hint):
        """Adds a flat polygon (3 or 4 points) facing along inward_hint (Unity winding: clockwise seen from the front)."""
        p = np.array(pts)
        nrm = np.cross(p[1] - p[0], p[2] - p[0])
        if np.linalg.norm(nrm) < 1e-9: return
        nrm /= np.linalg.norm(nrm)
        # Unity front faces (clockwise seen from the viewer) have cross(b - a, c - a) pointing at the viewer (see MiG29Builder.AtlasBox)
        if nrm @ inward_hint < 0: p = p[::-1]; nrm = -nrm
        b = len(verts)
        for q in p:
            verts.append(q); norms.append(nrm)
            uvs.append(((q[0] + q[2]) / UVS, q[1] / UVS) if abs(nrm[1]) < 0.7 else (q[2] / UVS, q[0] / UVS))
        tris.extend([b, b + 1, b + 2] + ([b, b + 2, b + 3] if len(p) == 4 else []))

    for t in TW:     # ceiling, facing down
        quad_or_tri([np.array([W[v][0], ceil[v], W[v][2]]) for v in t], np.array([0, -1.0, 0]))
    for (a, b), owners in edges.items():
        if len(owners) != 1: continue          # outline edges only
        oc = W[owners[0]].mean(0)
        pa, pb = W[a], W[b]
        mid = (pa + pb) / 2
        hint = oc - mid; hint[1] = 0
        if np.linalg.norm(hint) < 1e-6: continue
        quad_or_tri([pa + [0, RIM_LIFT, 0], pb + [0, RIM_LIFT, 0], np.array([pb[0], ceil[b], pb[2]]), np.array([pa[0], ceil[a], pa[2]])],
                    hint / np.linalg.norm(hint))
    V = np.array(verts)
    print(f"[wells] {name}: {len(tris) // 3} tris, rim y {W[used][:, 1].min():.2f}..{W[used][:, 1].max():.2f}, "
          f"ceiling y {min(ceil.values()):.2f}..{max(ceil.values()):.2f}, x {V[:, 0].min():.2f}..{V[:, 0].max():.2f}, z {V[:, 2].min():.2f}..{V[:, 2].max():.2f}")
    parts.append({"name": name, "vertices": np.round(V, 5).ravel().tolist(), "normals": np.round(np.array(norms), 5).ravel().tolist(),
                  "uvs": np.round(np.array(uvs), 5).ravel().tolist(), "triangles": tris})
json.dump({"parts": parts}, open(os.path.join(SRC, "gear_wells.json"), "w"))

# (superseded by tools/bay_texture.py, which runs after this) primer-grey texture: one tile = UVS metres; frames every 0.15 m tile/4, stringers, rivet rows, light grime
N = 512
img = Image.new("RGB", (N, N), (150, 157, 148))
dr = ImageDraw.Draw(img)
rng = np.random.default_rng(29)
for i in range(4):                                     # frames (darker, with a light edge)
    x = int((i + 0.5) * N / 4)
    dr.rectangle([x - 7, 0, x + 7, N], fill=(118, 124, 116)); dr.line([x - 7, 0, x - 7, N], fill=(172, 178, 170), width=2)
for j in range(3):                                     # stringers
    y = int((j + 0.5) * N / 3)
    dr.rectangle([0, y - 3, N, y + 3], fill=(128, 134, 126))
for i in range(4):
    x = int((i + 0.5) * N / 4)
    for yy in range(4, N, 12):
        dr.point([(x - 11, yy), (x + 11, yy)], fill=(100, 104, 98))
for _ in range(60):                                    # small fittings / pipe clamps
    x, y = rng.integers(0, N, 2)
    dr.rectangle([x, y, x + rng.integers(6, 18), y + rng.integers(3, 8)], fill=(105, 110, 104))
noise = (rng.normal(0, 5, (N, N, 1))).repeat(3, 2)
img = Image.fromarray(np.clip(np.asarray(img, float) + noise, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))
img.save(os.path.join(SRC, "bay_atlas.png"))
Image.new("RGBA", (8, 8), (0, 0, 0, 70)).save(os.path.join(SRC, "bay_metallic.png"))
print("[wells] bay_atlas.png written")
