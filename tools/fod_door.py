"""Intake FOD door outline. On the real MiG-29 the door seals the whole duct on the ground; a plain rectangle left gaps all round
and floated in the middle of the intake (user screenshot, v0.9.0). This measures the duct's inside walls along the line the door
closes on (from its hinge under the duct roof, just behind the lower lip, down and aft to the duct floor) and writes the door's
outline in the closed pose: for each height, the left and right wall positions, inset 6 mm.
python fod_door.py <MiG29Source>  ->  MiG29Source/fod_door.json (left intake, MiG frame; the right one mirrors it)"""
import json, os, sys
import numpy as np

SRC = sys.argv[1]
X_C = -0.70                                    # left duct centre (MiG frame)
HINGE = np.array([-0.295, 4.15])               # (y, z): under the duct roof, above the lower lip
FLOOR_END = np.array([-0.85, 3.80])            # (y, z): the closed door's lower edge on the duct floor
INSET = 0.006
N = 14

mesh = json.load(open(os.path.join(SRC, "mig29_mesh.json")))
body = next(p for p in mesh["parts"] if p["name"] == "body")
V = np.array(body["vertices"]).reshape(-1, 3); T = np.array(body["triangles"]).reshape(-1, 3)
TP = V[T]; TMIN, TMAX = TP.min(1), TP.max(1)


def wall(y, z, direction):
    """Distance from the duct centre to the first body surface along +x (direction 1) or -x (-1) at height y, station z."""
    o = np.array([X_C, y, z]); d = np.array([direction, 0.0, 0.0])
    cand = np.where((TMIN[:, 1] <= y) & (TMAX[:, 1] >= y) & (TMIN[:, 2] <= z) & (TMAX[:, 2] >= z))[0]
    best = None
    for i in cand:
        a, b, c = TP[i]
        e1, e2 = b - a, c - a
        p = np.cross(d, e2); det = np.dot(e1, p)
        if abs(det) < 1e-12: continue
        s = o - a; u = np.dot(s, p) / det
        if u < 0 or u > 1: continue
        q = np.cross(s, e1); v = np.dot(d, q) / det
        if v < 0 or u + v > 1: continue
        t = np.dot(e2, q) / det
        if t > 0.02 and (best is None or t < best): best = t
    return best


rows = []
for k in range(N + 1):
    f = k / N
    y, z = HINGE + (FLOOR_END - HINGE) * f
    y = min(y, HINGE[0] - 0.004) if k == 0 else max(y, FLOOR_END[0] + 0.004) if k == N else y
    r_in = wall(y, z, 1.0); r_out = wall(y, z, -1.0)        # +x: inboard wall (toward the fuselage), -x: outboard
    if r_in is None or r_out is None:
        print(f"[fod] no wall at y {y:.3f} z {z:.3f}"); continue
    rows.append([float(y), float(z), float(X_C - r_out + INSET), float(X_C + r_in - INSET)])
    print(f"[fod] y {y:6.3f} z {z:5.2f}: x {rows[-1][2]:.3f} .. {rows[-1][3]:.3f} (width {rows[-1][3] - rows[-1][2]:.3f})")
json.dump({"hinge": [X_C, float(HINGE[0]), float(HINGE[1])], "rows": rows}, open(os.path.join(SRC, "fod_door.json"), "w"))
print(f"[fod] {len(rows)} rows")
