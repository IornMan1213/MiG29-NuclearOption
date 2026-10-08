"""Airbrakes: the real MiG-29 has two speed-brake panels on the flat tail section between the engines (the "beaver tail", ahead of
the brake-chute housing), one on top opening up and one underneath opening down, both hinged at their front edge. The model has no
panel there, so this cuts them out of the body skin like main_bay_door.py: every body triangle over the panel outline is clipped
exactly along its x / z planes; the pieces inside become the panel (the tail's skin is a 1 cm double layer, so a panel gets the
outer skin and the inner sheet as its underside), the pieces outside go back into the skin. Under each panel a shallow well floor
(3.5 cm below the skin, following it) closes the hole when the panel is open.
python airbrake_cut.py <MiG29Source>  ->  MiG29Source/airbrakes.json"""
import json, os, sys
import numpy as np

SRC = sys.argv[1]
# panel outline, MiG frame (tail section: flat between the nacelles at |x| < 0.30; the chute housing starts at z -3.2)
X0, X1 = -0.28, 0.28
Z0, Z1 = -2.95, -2.10            # hinge along the front edge, z = Z1
SPLIT_Y = -0.03                  # tail section mid-plane: top skin above, bottom skin below (skin y +0.07 / -0.10 on the centreline)
DEPTH = 0.035                    # well floor below the skin
DGREY = (0.625, 0.875)           # missile atlas flat cells (missile_gen.py)


def clip(poly, axis, value, keep_below):
    out = []
    n = len(poly)
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]
        da, db = a[0][axis] - value, b[0][axis] - value
        ina, inb = (da <= 0) if keep_below else (da >= 0), (db <= 0) if keep_below else (db >= 0)
        if ina: out.append(a)
        if ina != inb:
            t = da / (da - db)
            out.append(tuple(a[k] + (b[k] - a[k]) * t for k in range(3)))
    return out


def split(poly):
    inside, outside = [], []
    rest = poly
    for axis, value in ((0, X0), (0, X1), (2, Z0), (2, Z1)):
        below = value in (X0, Z0)
        o = clip(rest, axis, value, keep_below=below)
        if len(o) >= 3: outside.append(o)
        rest = clip(rest, axis, value, keep_below=not below)
        if len(rest) < 3: return [], outside
    return [rest], outside


def fan(polys, V, N, U, T):
    for p in polys:
        base = len(V)
        for pos, nrm, uv in p:
            V.append(pos); N.append(nrm / (np.linalg.norm(nrm) + 1e-12)); U.append(uv)
        for k in range(1, len(p) - 1):
            T.extend([base, base + k, base + k + 1])


def pack(V, N, U, T):
    return {"vertices": np.round(np.array(V), 5).ravel().tolist(), "normals": np.round(np.array(N), 5).ravel().tolist(),
            "uvs": np.round(np.array(U), 5).ravel().tolist(), "triangles": T}


mesh = json.load(open(os.path.join(SRC, "mig29_mesh.json")))
body = next(p for p in mesh["parts"] if p["name"] == "body")
BV = np.array(body["vertices"]).reshape(-1, 3); BN = np.array(body["normals"]).reshape(-1, 3); BU = np.array(body["uvs"]).reshape(-1, 2)
BT = np.array(body["triangles"]).reshape(-1, 3)

TP = BV[BT]
TMIN, TMAX = TP.min(1), TP.max(1)
TUP = np.cross(TP[:, 1] - TP[:, 0], TP[:, 2] - TP[:, 0])[:, 1] > 0


def surface_y(x, z, top):
    """Height of the outer skin (top or bottom) at (x, z): vertical ray against the body's outward-facing triangles."""
    best = None
    cand = np.where((TMIN[:, 0] <= x) & (TMAX[:, 0] >= x) & (TMIN[:, 2] <= z) & (TMAX[:, 2] >= z) & (TUP == top))[0]
    for i in cand:
        P = TP[i]
        a, b, c = P[:, [0, 2]]
        v0, v1, v2 = b - a, c - a, np.array([x, z]) - a
        den = v0[0] * v1[1] - v1[0] * v0[1]
        if abs(den) < 1e-12: continue
        u = (v2[0] * v1[1] - v1[0] * v2[1]) / den; v = (v0[0] * v2[1] - v2[0] * v0[1]) / den
        if u < -1e-6 or v < -1e-6 or u + v > 1 + 1e-6: continue
        y = P[0, 1] + u * (P[1, 1] - P[0, 1]) + v * (P[2, 1] - P[0, 1])
        if not (-0.20 < y < 0.12): continue
        if best is None or (y > best if top else y < best): best = y
    return best


remove, skin = [], ([], [], [], [])
panels = {"upper": ([], [], [], []), "lower": ([], [], [], [])}
inner = 0
for i, t in enumerate(BT):
    P = BV[t]
    if P[:, 0].max() < X0 or P[:, 0].min() > X1 or P[:, 2].max() < Z0 or P[:, 2].min() > Z1:
        continue
    c = P.mean(0)
    if not (-0.20 < c[1] < 0.12): continue           # the tail section only (not the nacelles or fins nearby)
    top = c[1] > SPLIT_Y
    s = surface_y(min(max(c[0], X0), X1), min(max(c[2], Z0), Z1), top)
    if s is None or abs(c[1] - s) > 0.025:            # the skin's two layers only; structure inside the tail stays where it is
        inner += 1
        continue
    ins, outs = split([(P[k], BN[t[k]], BU[t[k]]) for k in range(3)])
    if not ins: continue
    remove.append(i)
    fan(ins, *panels["upper" if top else "lower"])
    fan(outs, *skin)
print(f"[airbrake] {inner} triangles inside the tail left alone")


wells = {}
for name, top in (("upper", True), ("lower", False)):
    nx, nz = 8, 10
    xs = np.linspace(X0 - 0.02, X1 + 0.02, nx + 1); zs = np.linspace(Z0 - 0.02, Z1 + 0.02, nz + 1)
    grid = np.zeros((nx + 1, nz + 1))
    for a, x in enumerate(xs):
        for b, z in enumerate(zs):
            yt, yb = surface_y(x, z, True), surface_y(x, z, False)
            d = min(DEPTH, 0.4 * (yt - yb))            # the tail thins aft: the two floors never cross
            grid[a, b] = (yt - d) if top else (yb + d)
    V, N, U, T = [], [], [], []
    for a in range(nx + 1):
        for b in range(nz + 1):
            V.append([xs[a], grid[a, b], zs[b]]); N.append([0, 1 if top else -1, 0]); U.append(DGREY)
    idx = lambda a, b: a * (nz + 1) + b
    for a in range(nx):
        for b in range(nz):
            q = [idx(a, b), idx(a + 1, b), idx(a + 1, b + 1), idx(a, b + 1)]
            # Unity front face: clockwise seen from the normal side (+y up: x then z order is clockwise from above)
            tri = [q[0], q[3], q[2], q[0], q[2], q[1]] if top else [q[0], q[1], q[2], q[0], q[2], q[3]]
            T.extend(tri)
    wells[name] = pack(V, N, U, T)
    print(f"[airbrake] {name} well floor y {grid.min():.3f}..{grid.max():.3f}")

out = {"remove": remove, "skin": pack(*skin), "panels": {}, "wells": wells, "hinges": {}}
for name, (V, N, U, T) in panels.items():
    D = np.array(V)
    out["panels"][name] = pack(V, N, U, T)
    edge = D[D[:, 2] > Z1 - 0.03]
    # hinge on the outer skin's front edge
    out["hinges"][name] = [0.0, float(edge[:, 1].max() if name == "upper" else edge[:, 1].min()), Z1]
    print(f"[airbrake] {name}: {len(T) // 3} panel tris, y {D[:, 1].min():.3f}..{D[:, 1].max():.3f}, hinge {np.round(out['hinges'][name], 3)}")
print(f"[airbrake] {len(remove)} skin triangles cut, {len(skin[3]) // 3} skin pieces kept")
json.dump(out, open(os.path.join(SRC, "airbrakes.json"), "w"))
