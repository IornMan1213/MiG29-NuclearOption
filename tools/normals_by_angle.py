"""The skin's normals rebuilt by angle, as Blender's "smooth by angle" makes them (what players fix by hand with Normals > Reset
Vectors): smooth across gentle curvature, sharp across creases.

The export (blender/export_mig29.py) gives each smooth face's corner its vertex's normal, the mean over every face at the vertex.
On a model this light that averages faces meeting at sharp edges: the flaps and ailerons (thin slabs whose top, bottom and edges
share corners) had normals about 50 degrees off on 90% of their area, the rudders and the rest of the skin on 20-25%; the light
and the shadows' normal offset follow those normals, so the skin showed dark smears and shadow streaks, worst on and round the
moving surfaces.

Here each corner's normal is the corner-angle-weighted mean of the face normals at its position (welded, so UV seams stay smooth)
that lie within ANGLE of its own face's. ANGLE sits between the model's curvature (faces meet at up to ~45 degrees: the eight-sided
nozzle rings, the rounded leading edges) and its creases (80-120 degrees: the wing and tail edges, the control surfaces' ends and
trailing edges). Corners that now need a different normal than their vertex has get a vertex of their own, appended: vertex indices
never move, so the scripts' index maps stay valid.

Parts: the body, every moving surface (the parts with a pivot) and the closed gear doors. Edits <source>/mig29_mesh.json in place,
once (info.normals_by_angle). Run after the scripts that shape the skin (tools/fix_winding.py, tools/fin_antenna.py) and before
those that copy its triangles or record vertex indices.
python normals_by_angle.py <source dir>"""
import json, os, sys
import numpy as np

SRC = sys.argv[1]
PATH = os.path.join(SRC, "mig29_mesh.json")
ANGLE = 50.0
PARTS = lambda p: p["name"] == "body" or p["name"] == "gear_doors_closed" or bool(p.get("pivot"))


def corner_normals(V, N, T, angle):
    """(len(T), 3, 3): each corner's rebuilt normal, facing as the stored normals do."""
    fn = np.cross(V[T[:, 1]] - V[T[:, 0]], V[T[:, 2]] - V[T[:, 0]])
    fn /= np.maximum(np.linalg.norm(fn, axis=1), 1e-12)[:, None]
    flip = np.einsum("ij,ij->i", fn, N[T].sum(1)) < 0          # the stored normals say which side is out
    fn[flip] *= -1
    cw = np.zeros(T.shape)                                       # each corner's angle: its weight
    for k in range(3):
        u = V[T[:, (k + 1) % 3]] - V[T[:, k]]; w = V[T[:, (k + 2) % 3]] - V[T[:, k]]
        c = np.einsum("ij,ij->i", u, w) / np.maximum(np.linalg.norm(u, axis=1) * np.linalg.norm(w, axis=1), 1e-12)
        cw[:, k] = np.arccos(np.clip(c, -1, 1))
    _, pid = np.unique(np.round(V, 4), axis=0, return_inverse=True); pid = pid.ravel()
    order = np.argsort(pid[T].ravel(), kind="stable")
    tk = np.stack(np.unravel_index(order, T.shape), 1)           # corners sorted by position
    starts = np.flatnonzero(np.r_[True, np.diff(pid[T].ravel()[order]) != 0, True])
    cos = np.cos(np.radians(angle)); out = np.zeros((len(T), 3, 3))
    for a, b in zip(starts[:-1], starts[1:]):
        ts, ks = tk[a:b, 0], tk[a:b, 1]
        f = fn[ts]; w = cw[ts, ks]
        m = (f @ f.T) >= cos
        n = m @ (f * w[:, None])
        out[ts, ks] = n / np.maximum(np.linalg.norm(n, axis=1), 1e-12)[:, None]
    degenerate = np.linalg.norm(out, axis=2) < 0.5               # slivers: keep what they had
    out[degenerate] = N[T][degenerate]
    return out


def apply(p):
    V = np.array(p["vertices"], float).reshape(-1, 3); N = np.array(p["normals"], float).reshape(-1, 3)
    UV = np.array(p["uvs"], float).reshape(-1, 2); T = np.array(p["triangles"], int).reshape(-1, 3)
    if len(T) == 0: return 0, 0.0, 0.0
    before = np.degrees(np.arccos(np.clip(np.einsum("ijk,ijk->ij", N[T], corner_normals(V, N, T, 0.0)), -1, 1)))
    CN = corner_normals(V, N, T, ANGLE)
    newV, newN, newUV = [V], [N.copy()], [UV]
    NN = newN[0]; used = {}; claimed = set(); extra = 0; base = len(V)
    T2 = T.copy()
    for t in range(len(T)):
        for k in range(3):
            i = T[t, k]; key = (i, *np.round(CN[t, k], 3))
            if key in used: T2[t, k] = used[key]; continue
            if i not in claimed:                                 # the vertex's first corner keeps the vertex
                NN[i] = CN[t, k]; used[key] = i; claimed.add(i); continue
            newV.append(V[i:i + 1]); newN.append(CN[t, k][None]); newUV.append(UV[i:i + 1])
            used[key] = base + extra; T2[t, k] = base + extra; extra += 1
    p["vertices"] = np.vstack(newV).round(6).reshape(-1).tolist()
    p["normals"] = np.vstack(newN).round(6).reshape(-1).tolist()
    p["uvs"] = np.vstack(newUV).round(6).reshape(-1).tolist()
    p["triangles"] = T2.reshape(-1).tolist()
    return extra, float(np.mean(before.max(1) > 45) * 100), len(T)


mesh = json.load(open(PATH))
info = mesh.setdefault("info", {})
if info.get("normals_by_angle"):
    print("[normals] already done"); sys.exit(0)
for p in mesh["parts"]:
    if not PARTS(p): continue
    extra, bad, n = apply(p)
    print(f"[normals] {p['name']}: {n} triangles, {bad:.0f}% had a corner over 45 deg off its face; {extra} vertices added for sharp edges")
info["normals_by_angle"] = ANGLE
json.dump(mesh, open(PATH, "w"))
print(f"[normals] rebuilt by angle ({ANGLE:.0f} deg)")
