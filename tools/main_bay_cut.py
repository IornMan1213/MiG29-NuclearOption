"""Main-bay side door for the real MiG-29 retraction. The model's main-gear door is a narrow strip along the nacelle / wing-root
junction; a leg swinging forward about the skewed hinge (blender/analysis/main_gear_search2.py) with the wheel turning flat passes
through the outer skin of the intake trunk ahead of it. The real jet has a large door on the side of the trunk there. This finds the
OUTER skin triangles (facing outside air) the swept leg (all poses, unsprung at rest and extended) comes within CLEAR of, closes
small gaps in that set, and writes them out: the builder cuts them from the skin and hangs them on a door that opens with the gear.
python main_bay_cut.py <MiG29Source> <search result json>  ->  MiG29Source/main_bay_cut.json
needs MiG29Source/main_wheel_L.json (the KR-67 wheel, MiG frame, rest pose)."""
import json, os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bay_grid

SRC = sys.argv[1]
R = json.load(open(sys.argv[2]))
CLEAR = float(os.environ.get("CLEAR", "0.03"))


def rot(axis, deg):
    k = np.asarray(axis, float); k = k / np.linalg.norm(k); a = np.radians(deg)
    K = np.array([[0, -k[2], k[1]], [k[2], 0, -k[0]], [-k[1], k[0], 0]])
    return np.eye(3) + np.sin(a) * K + (1 - np.cos(a)) * K @ K


def surface_samples(V, T, step=0.03):
    out = [V]
    for t in T:
        a, b, c = V[t]
        n = int(max(np.linalg.norm(b - a), np.linalg.norm(c - a), np.linalg.norm(c - b)) / step)
        if n < 2: continue
        u, v = np.meshgrid(np.linspace(0, 1, n + 1), np.linspace(0, 1, n + 1)); m = u + v <= 1
        out.append(a + np.outer(u[m], b - a) + np.outer(v[m], c - a))
    return np.concatenate(out)


def swept_leg(R, step_t=1 / 60):
    leg = json.load(open(os.path.join(SRC, "main_gear.json")))
    piv = leg["pivots"]
    HINGE = np.array(piv["hinge"]); SP = np.array(piv["strut_pos"]); SY = np.array(piv["strut_y"]); HX = np.array(piv["hinge_x"])
    EXT = float(piv["extension"])
    MX = HX / np.linalg.norm(HX); MY = SY - MX * (SY @ MX); MY /= np.linalg.norm(MY); MZ = np.cross(MX, MY)
    a = np.radians(R["yaw"]); AX = np.cos(a) * MX - np.sin(a) * MZ
    parts = {p["name"]: p for p in leg["parts"]}
    S = surface_samples(np.array(parts["sprung"]["vertices"]).reshape(-1, 3), np.array(parts["sprung"]["triangles"]).reshape(-1, 3))
    U = surface_samples(np.array(parts["unsprung"]["vertices"]).reshape(-1, 3), np.array(parts["unsprung"]["triangles"]).reshape(-1, 3))
    w = json.load(open(os.path.join(SRC, "main_wheel_L.json")))
    U = np.concatenate([U, surface_samples(np.array(w["vertices"]).reshape(-1, 3), np.array(w["triangles"]).reshape(-1, 3), 0.04)])
    out = []
    for t in np.arange(0, 1 + 1e-9, step_t):
        for ext in (0.0, EXT):
            U2 = (U - ext * SY - SP) @ rot(SY, R["strut"] * t).T + SP
            out.append(((np.concatenate([S, U2]) - HINGE) @ rot(AX, R["fold"] * t).T + HINGE + np.array(R["T"]) * t, t))
    return out


if __name__ == "__main__":
    G = 0.025; GLO, GHI = (-2.35, -1.15, -1.3), (-0.35, 0.85, 5.0)
    hid = bay_grid.Hidden(bay_grid.skin_triangles(SRC, ("body", "gear_doors_closed", "canopy")), GLO, GHI, G,
                          boxes=[((-0.98, -0.88, 3.59), (-0.42, -0.30, 3.61))])
    poses = swept_leg(R)
    mesh = json.load(open(os.path.join(SRC, "mig29_mesh.json")))
    body = next(p for p in mesh["parts"] if p["name"] == "body")
    V = np.array(body["vertices"]).reshape(-1, 3); T = np.array(body["triangles"]).reshape(-1, 3)
    # candidates: left-side outer skin triangles (front side, Unity winding, looks into outside air with the doors closed)
    cand = []
    for i, t in enumerate(T):
        P = V[t]
        if P[:, 0].max() > -0.3 or P[:, 0].min() < -2.4 or P[:, 2].max() < -1.0 or P[:, 2].min() > 5.0: continue
        n = np.cross(P[1] - P[0], P[2] - P[0]); n /= np.linalg.norm(n) + 1e-12
        g = hid.index((P.mean(0) + n * 0.05)[None])[0]
        if np.all(g >= 0) and np.all(g < np.array(hid.shape)) and hid.air[g[0], g[1], g[2]]:
            cand.append((i, surface_samples(P, np.array([[0, 1, 2]]), step=CLEAR * 0.7)))
    h = CLEAR
    allp = np.concatenate([P for P, _ in poses]); lo = allp.min(0) - 3 * h
    shape = tuple(np.ceil((allp.max(0) + 3 * h - lo) / h).astype(int) + 1)
    cidx = [(i, np.clip(np.floor((pts - lo) / h).astype(int), 0, np.array(shape) - 1)) for i, pts in cand]
    pierced = set()
    for P, t in poses:
        # a skin panel is crossed when, in the same pose, the leg is just inside it and just outside it
        gk = hid.index(P); okg = np.all((gk >= 0) & (gk < np.array(hid.shape)), axis=1); gk = np.clip(gk, 0, np.array(hid.shape) - 1)
        out = ~okg | hid.air[gk[:, 0], gk[:, 1], gk[:, 2]]
        grids = []
        for sel in (out, ~out):
            g = np.zeros(shape, bool); k = np.floor((P[sel] - lo) / h).astype(int); g[k[:, 0], k[:, 1], k[:, 2]] = True
            grids.append(bay_grid.Hidden._grow(g))
        if not (~out).any() or not out.any(): continue
        for i, k in cidx:
            if i in pierced: continue
            if grids[0][k[:, 0], k[:, 1], k[:, 2]].any() and grids[1][k[:, 0], k[:, 1], k[:, 2]].any(): pierced.add(i)
    cut = sorted(pierced)
    cen = V[T].mean(1)
    rc = np.where(cen[:, 0] > 0.2)[0]
    right = [int(rc[np.argmin(np.linalg.norm(cen[rc] - cen[i] * np.array([-1, 1, 1]), axis=1))]) for i in cut]
    C = V[T[cut]].mean(1)
    for q in (0, 10, 50, 90, 100): print(f"[cut] centroid {q}%: {np.round(np.percentile(C, q, axis=0), 2)}")
    print(f"[cut] {len(cut)} crossed outer skin triangles per side")
    json.dump({"left": cut, "right": right, "result": R}, open(os.path.join(SRC, "main_bay_cut.json"), "w"))