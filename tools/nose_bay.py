"""Nose-gear bay envelope from the MiG model, for the stowed-leg search in MiG29Builder.StowGear (searchBay).

For a grid over the bay (x across, z along, MiG frame) it records:
  floor = the outer skin from below (closed gear doors or belly), the lowest a stowed part may reach
  roof  = the outer skin from above (top of the fuselage or the intake walls' outside), the highest it may reach
The model's bay-well walls in between are hidden once the doors close, so a stowed leg may pass through them.
python nose_bay.py <MiG29Source>  ->  <MiG29Source>/nose_bay.json
"""
import json, sys, os
import numpy as np

SRC = sys.argv[1]
d = json.load(open(os.path.join(SRC, "mig29_mesh.json")))
tris = []
for name in ("body", "gear_doors_closed"):
    p = next(p for p in d["parts"] if p["name"] == name)
    V = np.array(p["vertices"]).reshape(-1, 3); T = np.array(p["triangles"]).reshape(-1, 3)
    tris.append(V[T])
TR = np.concatenate(tris)
c = TR.mean(1)
TR = TR[(np.abs(c[:, 0]) < 0.9) & (c[:, 2] > 2.0) & (c[:, 2] < 6.5)]
A = TR[:, 0]; E1 = TR[:, 1] - A; E2 = TR[:, 2] - A


def hits_up(x, z, y0):
    o = np.array([x, y0, z]); dv = np.array([0.0, 1.0, 0.0])
    p = np.cross(dv, E2); det = (E1 * p).sum(1); ok = np.abs(det) > 1e-9; inv = np.where(ok, 1 / np.where(ok, det, 1), 0)
    s = o - A; u = (s * p).sum(1) * inv; q = np.cross(s, E1); v = (dv * q).sum(1) * inv; t = (E2 * q).sum(1) * inv
    m = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-4)
    return np.sort(t[m]) + y0


xs = np.round(np.arange(-0.44, 0.4401, 0.04), 3)
zs = np.round(np.arange(2.6, 5.801, 0.05), 3)
floor = np.full((len(zs), len(xs)), np.nan); roof = np.full((len(zs), len(xs)), np.nan)
for i, z in enumerate(zs):
    for j, x in enumerate(xs):
        h = hits_up(x, z, -2.0)
        if len(h) >= 2:
            floor[i, j] = h[0]; roof[i, j] = h[-1]
json.dump({"x0": float(xs[0]), "dx": 0.04, "nx": len(xs), "z0": float(zs[0]), "dz": 0.05, "nz": len(zs),
           # cells with no skin (outside the fuselage) forbid everything: floor above roof (Unity's JsonUtility has no nulls)
           "floor": [9.0 if np.isnan(v) else round(float(v), 4) for v in floor.ravel()],
           "roof": [-9.0 if np.isnan(v) else round(float(v), 4) for v in roof.ravel()]},
          open(os.path.join(SRC, "nose_bay.json"), "w"))
for i in range(0, len(zs), 6):
    print(f"z {zs[i]:.2f}  floor/roof at x 0: {floor[i, len(xs) // 2]:.2f}/{roof[i, len(xs) // 2]:.2f}   x 0.2: {floor[i, len(xs) // 2 + 5]:.2f}/{roof[i, len(xs) // 2 + 5]:.2f}   x 0.32: {floor[i, len(xs) // 2 + 8]:.2f}/{roof[i, len(xs) // 2 + 8]:.2f}")
