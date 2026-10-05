"""Main wheel wells, shaped around the gear. The old wells were prisms standing on the strip-door outline (flat panels with gaps,
user screenshot, v0.9.0), and the leg now stows in the wing-root glove above the intake duct, far outside them. Here each well is
the hidden space inside the closed skin within CLEAR of everything the leg sweeps through (plugin path and the game's own arc,
unsprung at rest and extended), connected to the bay doors; its surface is meshed with surface nets (smoothed), facing into the
well, and left open where the doors are. Box-projected UVs, 1 texture tile per metre (tools/bay_texture.py).
Motion: tools/data/main_gear_motion.json; leg: MiG29Source/main_gear.json (tools/main_gear_gen.py); wheel: tools/data/kr67_main_wheel_L.json.
python main_wells.py <MiG29Source>  ->  MiG29Source/main_wells.json"""
import json, os, sys
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bay_grid

SRC = sys.argv[1]
H = 0.045                 # coarse: it only has to look right through a doorway
CLEAR = 0.10
LO, HI = (-2.35, -1.15, -1.30), (-0.35, 0.85, 5.00)     # the region proven not to leak the flood (bay_grid)


def rot(axis, deg):
    k = np.asarray(axis, float); k = k / np.linalg.norm(k); a = np.radians(deg)
    K = np.array([[0, -k[2], k[1]], [k[2], 0, -k[0]], [-k[1], k[0], 0]])
    return np.eye(3) + np.sin(a) * K + (1 - np.cos(a)) * K @ K      # Rodrigues, numerically Unity's Quaternion.AngleAxis


def surface_samples(V, T, step):
    out = [V]
    for t in T:
        a, b, c = V[t]
        n = int(max(np.linalg.norm(b - a), np.linalg.norm(c - a), np.linalg.norm(c - b)) / step)
        if n < 2: continue
        u, v = np.meshgrid(np.linspace(0, 1, n + 1), np.linspace(0, 1, n + 1)); m = u + v <= 1
        out.append(a + np.outer(u[m], b - a) + np.outer(v[m], c - a))
    return np.concatenate(out)


# --- the hidden space (closed doors, intake blockers sealing the ducts) ---
hid = bay_grid.Hidden(bay_grid.skin_triangles(SRC, ("body", "gear_doors_closed", "canopy")), LO, HI, H,
                      boxes=[((-0.98, -0.88, 3.59), (-0.42, -0.30, 3.61))])
shape = np.array(hid.shape)

# --- the swept leg: plugin path and the game's straight arc (MiG29Instruments/GearPath.cs, MiG29Tools.MiG29MainGear) ---
mot = json.load(open(os.path.join(HERE, "data", "main_gear_motion.json")))
st, pa = mot["stow"], mot["path"]
legd = json.load(open(os.path.join(SRC, "main_gear.json")))
piv = legd["pivots"]
HINGE = np.array(piv["hinge"]); SP = np.array(piv["strut_pos"]); SY = np.array(piv["strut_y"]); HX = np.array(piv["hinge_x"])
EXT = float(piv["extension"])
MX = HX / np.linalg.norm(HX); MY = SY - MX * (SY @ MX); MY /= np.linalg.norm(MY); MZ = np.cross(MX, MY)
axis = lambda yaw: np.cos(np.radians(yaw)) * MX - np.sin(np.radians(yaw)) * MZ
RF = rot(axis(st["yaw"]), st["fold"])


def path_pose(t):
    R1 = rot(axis(pa["yaw1"]), pa["fold1"])
    if t <= pa["K"]:
        u = t / pa["K"]; return rot(axis(pa["yaw1"]), pa["fold1"] * u), pa["strut1"] * u
    u = (t - pa["K"]) / (1 - pa["K"])
    Rr = RF @ R1.T
    ang = np.degrees(np.arccos(np.clip((np.trace(Rr) - 1) / 2, -1, 1)))
    v = np.array([Rr[2, 1] - Rr[1, 2], Rr[0, 2] - Rr[2, 0], Rr[1, 0] - Rr[0, 1]])
    ax = v / np.linalg.norm(v) if ang > 1e-6 else MX
    return rot(ax, ang * u) @ R1, pa["strut1"] + (st["strut"] - pa["strut1"]) * u


leg = {p["name"]: p for p in legd["parts"]}
Sd = surface_samples(np.array(leg["sprung"]["vertices"]).reshape(-1, 3), np.array(leg["sprung"]["triangles"]).reshape(-1, 3), 0.04)
w = json.load(open(os.path.join(HERE, "data", "kr67_main_wheel_L.json")))
Ud = np.concatenate([surface_samples(np.array(leg["unsprung"]["vertices"]).reshape(-1, 3), np.array(leg["unsprung"]["triangles"]).reshape(-1, 3), 0.04),
                     surface_samples(np.array(w["vertices"]).reshape(-1, 3), np.array(w["triangles"]).reshape(-1, 3), 0.05)])
near = np.zeros(hid.shape, bool)
for t in np.linspace(0, 1, 49):
    for Rm, s in (path_pose(t), (rot(axis(st["yaw"]), st["fold"] * t), st["strut"] * t)):
        for ext in (0.0, EXT):
            U2 = (Ud - ext * SY - SP) @ rot(SY, s).T + SP
            P = (np.concatenate([Sd, U2]) - HINGE) @ Rm.T + HINGE
            k = hid.index(P); ok = np.all((k >= 0) & (k < shape), axis=1); k = k[ok]
            near[k[:, 0], k[:, 1], k[:, 2]] = True
for _ in range(int(round(CLEAR / H))): near = bay_grid.Hidden._grow(near)
# --- the doorways: voxels the closed door panels pass through (strip door from the model, forward door cut by main_bay_door.py).
# Once a door opens, an eye outside sees through exactly these voxels; the model is hollow behind them, so the well must be a
# closed pocket, open only here, covering every hidden voxel next to a doorway (user report: you could see through the jet) ---
mesh = json.load(open(os.path.join(SRC, "mig29_mesh.json")))
dp = next(p for p in mesh["parts"] if p["name"] == "gear_doors_closed")
DV = np.array(dp["vertices"]).reshape(-1, 3); DT = np.array(dp["triangles"]).reshape(-1, 3)
c = DV[DT].mean(1)
DT = DT[(c[:, 0] < -1.0) & (c[:, 0] > -1.8) & (c[:, 2] > -0.3) & (c[:, 2] < 2.95)]          # MiG29Polish.DoorOf main_L
fd = json.load(open(os.path.join(SRC, "main_bay_door.json")))["doors"]["L"]
doorpts = np.concatenate([surface_samples(DV, DT, H * 0.3),
                          surface_samples(np.array(fd["vertices"]).reshape(-1, 3), np.array(fd["triangles"]).reshape(-1, 3), H * 0.3)])
doorway = np.zeros(hid.shape, bool)
k = hid.index(doorpts); ok = np.all((k >= 0) & (k < shape), axis=1); k = k[ok]; doorway[k[:, 0], k[:, 1], k[:, 2]] = True
# the skin is rasterised as a wall two voxels thick (bay_grid seals it by one voxel of dilation): widen the doorway through it
for _ in range(2): doorway = bay_grid.Hidden._grow(doorway)
doorway &= ~hid.hidden

# --- the well: hidden voxels near the swept leg (thin slivers dropped) plus every hidden voxel touching a doorway, connected ---
seen = hid.hidden & bay_grid.Hidden._grow(doorway)
cand = hid.hidden & near
cand = (cand & bay_grid.Hidden._grow(~bay_grid.Hidden._grow(~cand))) | seen
cav = seen.copy()
while True:
    g = (cav | bay_grid.Hidden._grow(cav)) & cand
    if g.sum() == cav.sum(): break
    cav = g
print(f"[wells] cavity {cav.sum()} voxels ({cav.sum() * H ** 3:.2f} m3), {seen.sum()} behind the doorways")
opening = doorway | hid.air          # faces toward these are left out: the doorways (and nothing outside the skin)
# --- surface nets: a vertex per mixed dual cell, a quad per boundary face (cavity voxel next to a non-cavity voxel) ---
nx, ny, nz = cav.shape
verts = {}
def vid(d):
    if d not in verts: verts[d] = len(verts)
    return verts[d]
quads, qn = [], []
for a in range(3):
    e = np.zeros(3, int); e[a] = 1
    A = cav[:nx - e[0], :ny - e[1], :nz - e[2]]; B = cav[e[0]:, e[1]:, e[2]:]
    OA = opening[:nx - e[0], :ny - e[1], :nz - e[2]]; OB = opening[e[0]:, e[1]:, e[2]:]
    for inside_first in (True, False):
        mask = (A & ~B & ~OB) if inside_first else (B & ~A & ~OA)      # open toward the doorways only
        for p in np.argwhere(mask):
            o = [i for i in range(3) if i != a]
            cells = []
            for d1, d2 in ((-1, -1), (0, -1), (0, 0), (-1, 0)):
                dd = p.copy(); dd[o[0]] += d1; dd[o[1]] += d2
                cells.append(tuple(dd))
            if any(min(cl) < 0 or cl[0] >= nx - 1 or cl[1] >= ny - 1 or cl[2] >= nz - 1 for cl in cells): continue
            quads.append([vid(cl) for cl in cells])
            n = np.zeros(3); n[a] = -1.0 if inside_first else 1.0          # facing into the cavity
            qn.append(n)
D = np.array(sorted(verts, key=verts.get), float)
V = (D + 1.0) * H + np.array(LO)          # dual cell (i) spans voxels i..i+1: centre at voxel i + 0.5 -> voxel centres sit at LO + i*H
V0 = V.copy()
Q = np.array(quads); QN = np.array(qn)
nb = [set() for _ in range(len(V))]
for q in Q:
    for i in range(4): nb[q[i]].update((q[(i + 1) % 4], q[(i + 3) % 4]))
for _ in range(6):                         # smooth, held within 0.6 voxel of the voxel surface
    avg = np.array([V[list(s)].mean(0) if s else V[i] for i, s in enumerate(nb)])
    V = V + 0.5 * (avg - V)
    V = V0 + np.clip(V - V0, -0.6 * H, 0.6 * H)
# vertex normals (into the cavity) and triangles with Unity winding (cross(b - a, c - a) toward the viewer = the normal)
N = np.zeros_like(V)
for q, n in zip(Q, QN): N[q] += n
N /= np.linalg.norm(N, axis=1, keepdims=True) + 1e-9
# the voxel surface sits within half a voxel of the skin: pull it into the well so no part of it can show outside the airframe
open_v = np.zeros(len(V), bool)
V = V + N * (0.3 * H)
tris = []
for q, n in zip(Q, QN):
    a, b, c, d = q
    if np.cross(V[b] - V[a], V[c] - V[a]) @ n < 0: a, b, c, d = d, c, b, a
    tris += [a, b, c, a, c, d]
# box-projected UVs, 1 tile per metre
ax = np.argmax(np.abs(N), axis=1)
UV = np.where(ax[:, None] == 0, V[:, [2, 1]], np.where(ax[:, None] == 1, V[:, [0, 2]], V[:, [0, 1]]))
print(f"[wells] main_L: {len(V)} verts, {len(tris) // 3} tris, x {V[:, 0].min():.2f}..{V[:, 0].max():.2f}, y {V[:, 1].min():.2f}..{V[:, 1].max():.2f}, z {V[:, 2].min():.2f}..{V[:, 2].max():.2f}")
T = np.array(tris).reshape(-1, 3)

# --- a shallow recess behind the forward door: the gap between the trunk skin and the intake duct is too thin for the voxel
# well, so the open door showed straight into the duct and its black blocker (user screenshot). Back panel RECESS in from the
# door, facing out, walls from the door outline to it facing into the recess. ---
RECESS = 0.07
DVf = np.array(fd["vertices"]).reshape(-1, 3); DTf = np.array(fd["triangles"]).reshape(-1, 3)
nout = np.array(fd["normals"]).reshape(-1, 3).mean(0); nout /= np.linalg.norm(nout)
key = {}; wid = np.array([key.setdefault(tuple(np.round(v, 4)), len(key)) for v in DVf])
Wd = np.zeros((len(key), 3))
for i, kk in enumerate(wid): Wd[kk] = DVf[i]
TW = wid[DTf]
edges = {}
for t in TW:
    for a_, b_ in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])): edges.setdefault((min(a_, b_), max(a_, b_)), []).append(t)
Vl, Nl, Tl = list(V), list(N), list(T.ravel())
def add_poly(pts, n):
    pts = np.array(pts)
    if np.cross(pts[1] - pts[0], pts[2] - pts[0]) @ n < 0: pts = pts[::-1]
    b0 = len(Vl)
    for p_ in pts: Vl.append(p_); Nl.append(n)
    Tl.extend([b0, b0 + 1, b0 + 2] + ([b0, b0 + 2, b0 + 3] if len(pts) == 4 else []))
back = Wd - nout * RECESS
LOW = -0.15                                                             # above this the voxel well covers it and the wheel passes
for t in TW:
    if Wd[t].mean(0)[1] < LOW: add_poly([back[i] for i in t], nout)     # back panel, seen from outside through the doorway
cen = Wd.mean(0)
for (a_, b_), own in edges.items():
    if len(own) != 1 or (Wd[a_][1] + Wd[b_][1]) / 2 > LOW: continue     # outline edges below the voxel well only
    mid = (Wd[a_] + Wd[b_]) / 2; hint = cen - mid; hint -= nout * (hint @ nout)
    add_poly([Wd[a_] - nout * 0.005, Wd[b_] - nout * 0.005, back[b_], back[a_]], hint / (np.linalg.norm(hint) + 1e-9))
V, N, T = np.array(Vl), np.array(Nl), np.array(Tl).reshape(-1, 3)
ax = np.argmax(np.abs(N), axis=1)
UV = np.where(ax[:, None] == 0, V[:, [2, 1]], np.where(ax[:, None] == 1, V[:, [0, 2]], V[:, [0, 1]]))
print(f"[wells] forward-door recess added: main_L now {len(T)} tris")
# hand-edited well (blender/well_edit.py -> well_export.py) replaces the generated one
ovr = os.path.join(HERE, "data", "main_well_L_override.json")
if os.path.exists(ovr):
    o = json.load(open(ovr))
    V = np.array(o["vertices"]).reshape(-1, 3); N = np.array(o["normals"]).reshape(-1, 3); T = np.array(o["triangles"]).reshape(-1, 3)
    ax = np.argmax(np.abs(N), axis=1)
    UV = np.where(ax[:, None] == 0, V[:, [2, 1]], np.where(ax[:, None] == 1, V[:, [0, 2]], V[:, [0, 1]]))
    print(f"[wells] using the hand-edited well {ovr}: {len(T)} tris")
M = np.array([-1.0, 1, 1])
parts = [{"name": "main_L", "vertices": np.round(V, 4).ravel().tolist(), "normals": np.round(N, 4).ravel().tolist(),
          "uvs": np.round(UV, 4).ravel().tolist(), "triangles": T.ravel().tolist()},
         {"name": "main_R", "vertices": np.round(V * M, 4).ravel().tolist(), "normals": np.round(N * M, 4).ravel().tolist(),
          "uvs": np.round(UV * np.array([1, 1]), 4).ravel().tolist(), "triangles": T[:, [0, 2, 1]].ravel().tolist()}]
json.dump({"parts": parts}, open(os.path.join(SRC, "main_wells.json"), "w"))
