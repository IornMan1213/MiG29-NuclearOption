"""Intake FOD doors, shaped from the duct liner (tools/intake_duct.py). On the real MiG-29 each door is a panel of the duct roof,
hinged along its front edge behind the intake ramp: in flight it lies flush with the roof, on the ground it swings down and aft
until its free edge meets the duct floor and seals the duct (the engines then breathe through the louvres on top of the wing roots).
The old door was outlined from rows of constant height on the model walls: the ducts are rolled ~6.5 deg, so it gapped at one
corner and clipped at the other, swung out of the black panel and hid in flight.

The hinge follows the roof's roll at HINGE_Z, a little under the liner roof. The closed door is the liner's own cross-section in the
plane through the hinge turned DOOR_DEG down from the roof, inset 6 mm; open, the same panel lies against the roof. The hinge drops
until the open door clears the roof everywhere (the roof is not flat).
python fod_door.py <MiG29Source>  ->  MiG29Source/fod_door.json: the left door closed (MiG frame), its hinge point and axis; the right
door mirrors it (MiG29Builder.FodDoor)"""
import json, os, sys
import numpy as np

SRC = sys.argv[1]
HINGE_Z = 4.35                                 # hinge station, 0.2 m ahead of the lower lip, under the raked roof
DOOR_DEG = 56.0                                # keep in step with MiG29InstrumentMath.FodClosedDeg (-56)
THICK = 0.012                                  # panel thickness: the back face is toward the roof when open
CLEAR = 0.004                                  # least gap between the open door's back face and the liner roof
INSET = 0.006

du = json.load(open(os.path.join(SRC, "intake_duct.json")))["L"]
V = np.array(du["vertices"]).reshape(-1, 3); T = np.array(du["triangles"]).reshape(-1, 3)
M = 64
# the liner's ring grid: rows of M + 1 points (seam repeated) until the end disc's centre, which is not on a ring
nring = 0
while (nring + 1) * (M + 1) <= len(V) and np.allclose(V[nring * (M + 1)], V[nring * (M + 1) + M]): nring += 1
G = V[:nring * (M + 1)].reshape(nring, M + 1, 3)[:, :M]
P0 = V[T[:, 0]]; E1 = V[T[:, 1]] - P0; E2 = V[T[:, 2]] - P0


def ray(o, d):
    """Distance along d from o to the first liner triangle, or inf."""
    h = np.cross(d, E2); a = np.einsum("ij,ij->i", E1, h); ok = np.abs(a) > 1e-12
    f = np.where(ok, 1.0 / np.where(ok, a, 1.0), 0.0); s = o - P0
    u = f * np.einsum("ij,ij->i", s, h); q = np.cross(s, E1); v = f * (q @ d); t = f * np.einsum("ij,ij->i", E2, q)
    m = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-5)
    return t[m].min() if m.any() else np.inf


def at_z(z):
    """The liner's wall lines (one point per column) where they cross station z."""
    out = []
    for k in range(M):
        line = G[:, k]
        for r in range(nring - 1):
            a, b = line[r], line[r + 1]
            if (a[2] - z) * (b[2] - z) <= 0 and a[2] != b[2]:
                out.append(a + (b - a) * (z - a[2]) / (b[2] - a[2])); break
        else:
            out.append(None)
    return out


ring = at_z(HINGE_Z)
pts = np.array([p for p in ring if p is not None])
cx, cy = pts[:, 0].mean(), pts[:, 1].mean()
half_w = (pts[:, 0].max() - pts[:, 0].min()) / 2
roof = pts[(pts[:, 1] > cy) & (np.abs(pts[:, 0] - cx) < 0.7 * half_w)]
m, b = np.polyfit(roof[:, 0], roof[:, 1], 1)                                           # the roof's roll across the duct
axis = np.array([1.0, m, 0.0]); axis /= np.linalg.norm(axis)                            # outboard to inboard (+x on the left duct)
def roof_point(z):
    ps = np.array([p for p in at_z(z) if p is not None]); c = ps[:, 0].mean()
    top = ps[(ps[:, 1] > ps[:, 1].mean()) & (np.abs(ps[:, 0] - c) < 0.25)]
    return np.array([c, top[:, 1].mean(), z])
top0, top1 = roof_point(HINGE_Z), roof_point(HINGE_Z - 0.6)
aft = top1 - top0; aft -= axis * (aft @ axis); aft /= np.linalg.norm(aft)               # along the roof, aft
down = np.cross(axis, aft)
if down[1] > 0: down = -down                                                             # into the duct
closed = np.cos(np.radians(DOOR_DEG)) * aft + np.sin(np.radians(DOOR_DEG)) * down       # the closed door, hinge to free edge
normal = np.cross(axis, closed); normal /= np.linalg.norm(normal)
if normal[2] < 0: normal = -normal                                                       # the front face looks into the mouth


def outline(hinge):
    """The liner's section in the closed door's plane: where each wall line crosses it, inset toward the middle."""
    s = (G - hinge) @ normal
    out = []
    for k in range(M):
        for r in range(nring - 1):
            if s[r, k] * s[r + 1, k] <= 0 and s[r, k] != s[r + 1, k]:
                a, bb = G[r, k], G[r + 1, k]; out.append(a + (bb - a) * s[r, k] / (s[r, k] - s[r + 1, k])); break
        else:
            raise SystemExit(f"fod_door: wall line {k} does not cross the door plane")
    out = np.array(out)
    c = out.mean(0)
    d = out - c; d -= np.outer(d @ normal, normal)
    return out - d / np.linalg.norm(d, axis=1)[:, None] * INSET


n_closed, n_open = np.cross(axis, closed), np.cross(axis, aft)
def opened(p, hinge):
    """A closed-door point turned about the hinge until the door lies along the roof (closed -> aft)."""
    q = p - hinge
    return hinge + axis * (q @ axis) + aft * (q @ closed) + n_open * (q @ n_closed)
assert opened(top0 + normal, top0)[1] < top0[1], "fod_door: the front face would face the roof when open"


# the hinge 2 cm under the roof, then lowered until the open door's back face clears the liner roof everywhere
drop = 0.02
for _ in range(40):
    hinge = top0 + down * drop
    poly = outline(hinge)
    # only the part below the hinge is door: the section also runs up the roof ahead of the hinge
    door = poly[(poly - hinge) @ closed > -0.005]
    back = [opened(p - normal * THICK, hinge) for p in door]
    gaps = [ray(q, -down) for q in back]
    worst = min(gaps)
    if worst >= CLEAR: break
    drop += max(CLEAR - worst, 0.002)
else:
    raise SystemExit("fod_door: the open door does not clear the roof")
length = ((door - hinge) @ closed).max()
print(f"[fod] hinge z {HINGE_Z} y {hinge[1]:.3f} ({drop * 100:.1f} cm under the roof), roll {np.degrees(np.arctan(m)):.1f} deg, "
      f"door {len(door)} edge points, {length:.2f} m long, {DOOR_DEG} deg down; open: back face {worst * 1000:.0f} mm under the roof")
json.dump(dict(hinge=hinge.round(5).tolist(), axis=axis.round(6).tolist(), normal=normal.round(6).tolist(),
               closed_deg=DOOR_DEG, thickness=THICK, outline=door.round(5).ravel().tolist()),
          open(os.path.join(SRC, "fod_door.json"), "w"))
