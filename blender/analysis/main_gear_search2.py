"""Main-gear retraction search with a skewed hinge. A real forward-retracting leg swings about one fixed axis that is yawed in plan
view, so the leg comes up forward and inboard in a single arc, while a linkage turns the wheel about the leg. LandingGear folds the
hinge about its parent's x axis yawed by the hinge's own base y euler (localEulerAngles = base + (fold, 0, 0); Unity applies
Ry * Rx * Rz), so giving the hinge a base yaw (children compensated) makes the game swing the leg about a yawed axis with no plugin.
Searches hinge yaw, fold, strut twist and an optional (penalised) stow shift for the left leg (right = mirror) against the hidden
space inside the MiG skin (tools/bay_grid.py). Leg: tools/main_gear_gen.py (MiG29Source/main_gear.json, wheel included).
python main_gear_search2.py <MiG29Source> <MiG29Out>
env: YAWS="-40,41,5" FOLDS="-120,-65,5" STRUTS="-180,180,15" SPAN="0.15,0.1,0.15" T_WEIGHT=0.3 CLIP_WEIGHT=0.15 FLAT_WEIGHT=0.03"""
import json, os, sys, time
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tools"))
import bay_grid

SRC, OUT = sys.argv[1], sys.argv[2]
env = os.environ.get
YAWS = [int(v) for v in env("YAWS", "-40,41,5").split(",")]
FOLDS = [int(v) for v in env("FOLDS", "-120,-65,5").split(",")]
STRUTS = [int(v) for v in env("STRUTS", "-180,180,15").split(",")]
SPAN = tuple(float(v) for v in env("SPAN", "0.15,0.1,0.15").split(","))
T_WEIGHT = float(env("T_WEIGHT", "0.3"))         # margin (m) traded per metre of stow shift: a real leg does not slide
CLIP_WEIGHT = float(env("CLIP_WEIGHT", "0.15"))  # margin traded per unit of mid-swing clipping fraction
FLAT_WEIGHT = float(env("FLAT_WEIGHT", "0.03"))  # margin traded for a wheel that ends up on edge instead of lying flat
MIN_MARGIN = float(env("MIN_MARGIN", "0.02"))
RESULT = env("RESULT", "main_gear_search2.json")


def rot(axis, deg):
    k = np.asarray(axis, float); k = k / np.linalg.norm(k); a = np.radians(deg)
    K = np.array([[0, -k[2], k[1]], [k[2], 0, -k[0]], [-k[1], k[0], 0]])
    return np.eye(3) + np.sin(a) * K + (1 - np.cos(a)) * K @ K


leg = json.load(open(os.path.join(SRC, "main_gear.json")))
piv = leg["pivots"]
HINGE = np.array(piv["hinge"]); SP = np.array(piv["strut_pos"]); SY = np.array(piv["strut_y"]); HX = np.array(piv["hinge_x"])
WHEEL = np.array(piv["wheel"]); WAX = np.array(piv["wheel_axis"])
EXT = float(piv["extension"])            # unsprung travel past the rest pose in the air (LandingGear.FixedUpdate, no ground contact)
parts = {p["name"]: np.array(p["vertices"]).reshape(-1, 3) for p in leg["parts"]}
# the KR-67 wheel (kept: 0.85 x 0.29 m, the real KT-150 is 840 x 290 mm), rest pose, from the MiG29GearDump 'down' dump
d0 = json.load(open(os.path.join(OUT, "gear_down.json")))
parts["wheel"] = np.concatenate([np.array(p["v"]).reshape(-1, 3) for p in d0["parts"] if p["name"] == "wheel_L"])
S = parts["sprung"]; U = np.concatenate([parts["unsprung"], parts["wheel"]])
# a leg retracted in flight stows with the unsprung part fully extended; one spawned retracted keeps the rest pose: both must hide
U = np.concatenate([U, U - EXT * SY])
# hinge parent (mount) frame: x = hinge axis at zero yaw, y = leg axis, z = x cross y (Unity: Ry(+psi) takes x to cos x - sin z)
MX = HX / np.linalg.norm(HX)
MY = SY - MX * (SY @ MX); MY /= np.linalg.norm(MY)
MZ = np.cross(MX, MY)
if MZ[2] < 0: MZ = -MZ    # forward


def axis(yaw):
    """Fold axis once the mount is yawed about its own y (leg) axis: Quaternion.AngleAxis(yaw, mount.up) * mount.right."""
    a = np.radians(yaw)
    return np.cos(a) * MX - np.sin(a) * MZ


def pose(Sv, Uv, yaw, fold, strut, T):
    U2 = (Uv - SP) @ rot(SY, strut).T + SP
    R = rot(axis(yaw), fold)
    return (np.concatenate([Sv, U2]) - HINGE) @ R.T + HINGE + T


# hidden space (closed doors) and the enclosed space with the doors open, right bay (left leg mirrored into it)
t0 = time.time()
H = 0.025
LO, HI = (0.35, -1.15, -1.3), (2.35, 0.85, 5.0)       # x from 0.35: a region cut at 0.25 leaks the flood into the ducts
BOX = [((0.42, -0.88, 3.59), (0.98, -0.30, 3.61))]          # intake blockers
cache = os.path.join(OUT, "search2_grids.npz")
key = np.array([os.path.getmtime(os.path.join(SRC, "mig29_mesh.json")), *LO, *HI, H])
if os.path.exists(cache) and np.allclose(np.load(cache)["key"], key):
    z = np.load(cache); M, deep_open = z["M"], z["deep_open"]
else:
    hid = bay_grid.Hidden(bay_grid.skin_triangles(SRC, ("body", "gear_doors_closed", "canopy")), LO, HI, H, boxes=BOX)
    M = bay_grid.signed_margin(~hid.air, H, steps=8) - 1.5 * H
    hid_open = bay_grid.Hidden(bay_grid.skin_triangles(SRC, ("body", "canopy")), LO, HI, H, boxes=BOX)
    deep_open = bay_grid.signed_margin(~hid_open.air, H, steps=4) > 0.04
    np.savez_compressed(cache, M=M, deep_open=deep_open, key=key)
SHAPE = np.array(M.shape)
print(f"[search2] grids {tuple(SHAPE)} in {time.time() - t0:.0f} s")


def idx(P):
    return np.round((P * np.array([-1.0, 1.0, 1.0]) - np.array(LO)) / H).astype(int)


def margin(P):
    k = idx(P)
    out = np.any((k < 0) | (k >= SHAPE), axis=1)
    k = np.clip(k, 0, SHAPE - 1)
    return np.where(out, -0.2, M[k[:, 0], k[:, 1], k[:, 2]])


def clipping(P):
    k = idx(P)
    ok = np.all((k >= 0) & (k < SHAPE), axis=1)
    k = k[ok]
    return deep_open[k[:, 0], k[:, 1], k[:, 2]].sum() / len(P)


allP = np.concatenate([S, U])
_, keep = np.unique(np.round(allP / 0.02).astype(int), axis=0, return_index=True)
isU = np.arange(len(allP)) >= len(S)
Ss, Us = allP[keep][~isU[keep]], allP[keep][isU[keep]]
print(f"[search2] {len(Ss)} + {len(Us)} sample verts (unsprung at rest and extended)")


def flatness(yaw, fold, strut):
    """1 when the stowed wheel lies flat (axle vertical), 0 when on edge."""
    a = rot(axis(yaw), fold) @ (rot(SY, strut) @ WAX)
    return abs(a[1])


def score_T(yaw, fold, strut, span, step, T0):
    P = pose(Ss, Us, yaw, fold, strut, np.zeros(3))
    mids = [pose(Ss, Us, yaw, fold * t, strut * t, np.zeros(3)) for t in (0.25, 0.5, 0.75)]
    best = (-9.0, None, None)
    rng = [np.arange(-s, s + 1e-6, step) if s > 0 else np.zeros(1) for s in span]
    for dx in rng[0]:
        for dy in rng[1]:
            for dz in rng[2]:
                T = T0 + np.array([dx, dy, dz])
                m = margin(P + T).min()
                if m < MIN_MARGIN: continue
                clip = np.mean([clipping(Pm + T * t) for Pm, t in zip(mids, (0.25, 0.5, 0.75))])
                sc = min(m, 0.08) - CLIP_WEIGHT * clip - T_WEIGHT * np.linalg.norm(T) - FLAT_WEIGHT * (1 - flatness(yaw, fold, strut))
                if sc > best[0]: best = (sc, T, (m, clip))
    return best


results = []
t0 = time.time()
for yaw in range(*YAWS):
    for fold in range(*FOLDS):
        for strut in range(*STRUTS):
            sc, T, info = score_T(yaw, fold, strut, SPAN, 0.05, np.zeros(3))
            if T is not None: results.append((sc, yaw, fold, strut, T, info))
results.sort(key=lambda r: -r[0])
print(f"[search2] coarse: {len(results)} feasible in {time.time() - t0:.0f} s")
for sc, y, f, s, T, (m, c) in results[:10]:
    print(f"   coarse yaw {y} fold {f} strut {s} T {np.round(T, 2)}: score {sc * 100:.1f}, margin {m * 100:.1f} cm, clip {c * 100:.0f}%, flat {flatness(y, f, s):.2f}")
if not results: sys.exit("[search2] nothing feasible")

fine = []
for sc0, y0, f0, s0, T0, _ in results[:5]:
    for yaw in range(y0 - 4, y0 + 5, 2):
        for fold in range(f0 - 4, f0 + 5, 2):
            for strut in range(s0 - 10, s0 + 11, 5):
                sc, T, info = score_T(yaw, fold, strut, tuple(min(0.05, v) for v in SPAN), 0.025, T0)
                if T is not None: fine.append((sc, yaw, fold, strut, T, info))
fine.sort(key=lambda r: -r[0])
for sc, y, f, s, T, (m, c) in fine[:6]:
    clips = [clipping(pose(Ss, Us, y, f * t, s * t, T * t)) for t in (0.25, 0.5, 0.75)]
    wc = pose(np.zeros((0, 3)), WHEEL[None], y, f, s, T)[0]
    print(f"[search2] fine yaw {y} fold {f} strut {s} T {np.round(T, 3)}: margin {m * 100:.1f} cm, clipping {np.round(np.array(clips) * 100)} %, flat {flatness(y, f, s):.2f}, wheel at {np.round(wc, 2)}")
sc, y, f, s, T, _ = fine[0]
json.dump({"yaw": y, "fold": f, "strut": s, "T": T.tolist()}, open(os.path.join(OUT, RESULT), "w"))
P = pose(S, np.zeros((0, 3)), y, f, s, T); print(f"[search2] sprung stowed margin {margin(P).min() * 100:.1f} cm")
for ext in (0.0, EXT):
    for nm in ("unsprung", "wheel"):
        P = pose(np.zeros((0, 3)), parts[nm] - ext * SY, y, f, s, T)
        print(f"[search2] {nm} stowed margin {margin(P).min() * 100:.1f} cm ({'extended' if ext else 'rest pose'})")
