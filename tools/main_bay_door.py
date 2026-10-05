"""Forward main-bay doors: on the real MiG-29 the main wheel goes up into the wing-root glove through a door on the top of the intake
trunk side, ahead of the leg's strip door. The model has no such door, so the retracting wheel (blender/analysis/main_gear_path3.py)
would come through solid skin there for the last tenth of its swing. This cuts the door out of the body skin: every outer-skin body
triangle (front side facing outside air) in the door box is clipped exactly along the box planes; the inside pieces become the door
(left, and the mirrored right), the outside pieces replace the cut triangles in the skin. The door hinges along its lower edge and
swings down and out (DOOR_OPEN deg) so that it hangs clear of the leg's path; the open-door clearance is checked here.
python main_bay_door.py <MiG29Source> [MiG29Out]  ->  MiG29Source/main_bay_door.json"""
import json, os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bay_grid

SRC = sys.argv[1]
OUT = sys.argv[2] if len(sys.argv) > 2 else None
# door box, left side, MiG frame: skin crossings of the path are at x -1.56..-1.11, y -0.51..0.0, z 2.86..3.52 (t 0.88-0.98)
X0, X1 = -1.70, -0.95
Y0, Y1 = -0.58, 0.03
Z0, Z1 = 2.76, 3.62
DOOR_OPEN = float(os.environ.get("DOOR_OPEN", "150"))   # hangs down and out, >= 9 cm clear of the leg along the plugin path


def clip(poly, axis, value, keep_below):
    """Sutherland-Hodgman against the plane coord[axis] = value; poly = list of (pos, normal, uv) tuples."""
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
    """Pieces of a polygon inside the box (y, z planes) and outside it."""
    inside, outside = [], []
    rest = poly
    for axis, value in ((2, Z0), (2, Z1), (1, Y0), (1, Y1)):
        below = value in (Z0, Y0)          # the outside part is below the lower planes and above the upper ones
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


mesh = json.load(open(os.path.join(SRC, "mig29_mesh.json")))
body = next(p for p in mesh["parts"] if p["name"] == "body")
BV = np.array(body["vertices"]).reshape(-1, 3); BN = np.array(body["normals"]).reshape(-1, 3); BU = np.array(body["uvs"]).reshape(-1, 2)
BT = np.array(body["triangles"]).reshape(-1, 3)
G = 0.025
hid = bay_grid.Hidden(bay_grid.skin_triangles(SRC, ("body", "gear_doors_closed", "canopy")), (-2.0, -1.0, 2.0), (-0.6, 0.6, 4.3), G,
                      boxes=[((-0.98, -0.88, 3.59), (-0.42, -0.30, 3.61))])
remove, skinV, skinN, skinU, skinT = [], [], [], [], []
doors = {}
for side, sx in (("L", 1.0), ("R", -1.0)):
    dV, dN, dU, dT = [], [], [], []
    M = np.array([sx, 1.0, 1.0])
    for i, t in enumerate(BT):
        P = BV[t] * M                                     # right side mirrored onto the left to share the box
        if P[:, 0].max() < X0 or P[:, 0].min() > X1 or P[:, 1].max() < Y0 or P[:, 1].min() > Y1 or P[:, 2].max() < Z0 or P[:, 2].min() > Z1:
            continue
        n = np.cross(P[1] - P[0], P[2] - P[0]) * (1 if sx > 0 else -1)
        n /= np.linalg.norm(n) + 1e-12
        if n[0] > -0.35: continue                         # the trunk's outboard side only (not the wing-root underside)
        c = P.mean(0)
        if not (X0 < c[0] < X1): continue
        g = hid.index((c + n * 0.05)[None])[0]
        if not (np.all(g >= 0) and np.all(g < np.array(hid.shape)) and hid.air[g[0], g[1], g[2]]): continue
        poly = [(P[k], BN[t[k]] * M, BU[t[k]]) for k in range(3)]
        ins, outs = split(poly)
        if not ins: continue
        remove.append(i)
        # back to the true side (mirror x for the right). The polygons keep the original triangle's vertex order, so mirroring back
        # restores its winding by itself; reversing them as well turned the right door and skin pieces inside out (user video)
        un = lambda ps: [[(q[0] * M, q[1] * M, q[2]) for q in p] for p in ps]
        fan(un(ins), dV, dN, dU, dT)
        fan(un(outs), skinV, skinN, skinU, skinT)
    D = np.array(dV)
    doors[side] = {"vertices": np.round(D, 5).ravel().tolist(), "normals": np.round(np.array(dN), 5).ravel().tolist(),
                   "uvs": np.round(np.array(dU), 5).ravel().tolist(), "triangles": dT}
    print(f"[door] {side}: {len(dT) // 3} door tris, x {D[:, 0].min():.2f}..{D[:, 0].max():.2f}, y {D[:, 1].min():.2f}..{D[:, 1].max():.2f}, "
          f"z {D[:, 2].min():.2f}..{D[:, 2].max():.2f}")
# hinge: the door's lower edge, along z
DL = np.array(doors["L"]["vertices"]).reshape(-1, 3)
low = DL[DL[:, 1] < DL[:, 1].min() + 0.03]
hinge = np.array([low[:, 0].mean(), low[:, 1].mean(), (Z0 + Z1) / 2])
print(f"[door] {len(remove)} skin triangles cut, {len(skinT) // 3} skin pieces kept; hinge (left) {np.round(hinge, 3)}, opens {DOOR_OPEN} deg")
json.dump({"remove": remove, "skin": {"vertices": np.round(np.array(skinV), 5).ravel().tolist(), "normals": np.round(np.array(skinN), 5).ravel().tolist(),
                                      "uvs": np.round(np.array(skinU), 5).ravel().tolist(), "triangles": skinT},
           "doors": doors, "hinge_L": hinge.tolist(), "open_deg": DOOR_OPEN}, open(os.path.join(SRC, "main_bay_door.json"), "w"))

if OUT:      # open-door clearance along the plugin path (left side)
    sys.argv = [sys.argv[0], SRC, OUT]
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "blender", "analysis"))
    import main_gear_path3 as m
    P3 = json.load(open(os.path.join(OUT, "main_gear_path3.json"))); key = (P3["K"], P3["yaw1"], P3["fold1"], P3["strut1"])
    a = np.radians(DOOR_OPEN)            # left door: rotate about +z by +a swings its upper edge outboard (-x) and down
    Rz = np.array([[np.cos(a), -np.sin(a), 0], [np.sin(a), np.cos(a), 0], [0, 0, 1]])
    Dopen = (DL - hinge) @ Rz.T + hinge
    print(f"[door] open door spans x {Dopen[:, 0].min():.2f}..{Dopen[:, 0].max():.2f}, y {Dopen[:, 1].min():.2f}..{Dopen[:, 1].max():.2f}")
    worst = 9.0
    lo, hi = Dopen.min(0) - 0.1, Dopen.max(0) + 0.1
    for t in np.linspace(0, 1, 41):
        P = m.posed(*m.sample(key, t)) if t < 1 else m.posed(m.RF, m.S, m.TF)
        Q = P[np.all((P > lo) & (P < hi), axis=1)]
        if len(Q): worst = min(worst, np.min(np.linalg.norm(Q[:, None] - Dopen[None], axis=2)))
    print(f"[door] closest leg approach to the open door over the swing: {worst * 100:.0f} cm (vertex to vertex)")
