"""Give the left fin the antenna spike the right fin has.

The source model has a small rear-pointing spike at the top of the right fin's trailing edge (three loose pieces: a rod, a cross
plate and a collar, 144 triangles) and nothing on the left fin, where real MiG-29s carry one too ("the little spike on the back
left vertical stabiliser is missing but the right is there", player reports). This appends a mirrored copy of those pieces to the
body on the left: x flipped, triangle winding reversed and normals mirrored to match, the same texture. Appended, so no existing
vertex index moves (smt_hump.py, main_bay_door.py and the builder refer to them).
Idempotent: nothing happens when the left fin already has the spike. Run on every build after tools/fix_winding.py.

python tools/fin_antenna.py <MiG29Source dir>"""
import json, os, sys
import numpy as np

PATH = os.path.join(sys.argv[1], "mig29_mesh.json")
BOX = (np.array([1.75, 2.0, -4.25]), np.array([2.0, 2.2, -3.84]))   # the right spike's pieces lie wholly inside this (Unity, m)

d = json.load(open(PATH))
body = next(p for p in d["parts"] if p["name"] == "body")
v = np.array(body["vertices"], float).reshape(-1, 3)
n = np.array(body["normals"], float).reshape(-1, 3)
uv = np.array(body["uvs"], float).reshape(-1, 2)
t = np.array(body["triangles"], int).reshape(-1, 3)
inside = lambda pts, lo, hi: np.all((pts >= lo) & (pts <= hi), axis=-1)

mlo, mhi = BOX[0] * [-1, 1, 1], BOX[1] * [-1, 1, 1]
if inside(v, np.minimum(mlo, mhi), np.maximum(mlo, mhi)).sum() > 50:
    print("[fin antenna] the left fin has its spike"); sys.exit(0)

# loose pieces (welded positions) lying wholly inside the box on the right fin
key = {}
w = np.array([key.setdefault(tuple(np.round(x, 4)), len(key)) for x in v])
parent = list(range(len(key)))
def root(a):
    while parent[a] != a:
        parent[a] = parent[parent[a]]; a = parent[a]
    return a
for a, b, c in w[t]:
    ra = root(a); parent[root(b)] = ra; parent[root(c)] = ra
piece = np.array([root(w[x]) for x in t[:, 0]])
take = np.zeros(len(t), bool)
for pid in set(piece[inside(v[t].mean(1), *BOX)]):
    m = piece == pid
    if inside(v[t[m]].reshape(-1, 3), *BOX).all():
        take |= m
if not take.any():
    sys.exit("[fin antenna] no spike on the right fin to copy: the model has changed")

src = np.unique(t[take])
remap = {int(k): len(v) + i for i, k in enumerate(src)}
nv = v[src] * [-1, 1, 1]
nn = n[src] * [-1, 1, 1]
nt = np.array([[remap[int(a)], remap[int(c)], remap[int(b)]] for a, b, c in t[take]])   # mirrored: winding reversed
body["vertices"] = np.vstack([v, nv]).reshape(-1).tolist()
body["normals"] = np.vstack([n, nn]).reshape(-1).tolist()
body["uvs"] = np.vstack([uv, uv[src]]).reshape(-1).tolist()
body["triangles"] = np.vstack([t, nt]).reshape(-1).tolist()
json.dump(d, open(PATH, "w"))
print(f"[fin antenna] left fin spike added: {len(nt)} triangles mirrored from the right fin")
