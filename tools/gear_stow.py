"""The left main gear in its stowed pose (MiG frame), as surface sample points: the leg (MiG29Source/main_gear.json) and the KR-67
wheel (tools/data/kr67_main_wheel_L.json) folded by tools/data/main_gear_motion.json's stow, with the strut at rest and extended.
The same pose math as main_wells.py (which also sweeps the path to it); intake_duct.py keeps the duct roof clear of it."""
import json, os
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))


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


def stowed_points(src, step=0.03):
    st = json.load(open(os.path.join(HERE, "data", "main_gear_motion.json")))["stow"]
    legd = json.load(open(os.path.join(src, "main_gear.json")))
    piv = legd["pivots"]
    HINGE = np.array(piv["hinge"]); SP = np.array(piv["strut_pos"]); SY = np.array(piv["strut_y"]); HX = np.array(piv["hinge_x"])
    EXT = float(piv["extension"])
    MX = HX / np.linalg.norm(HX); MY = SY - MX * (SY @ MX); MY /= np.linalg.norm(MY); MZ = np.cross(MX, MY)
    axis = np.cos(np.radians(st["yaw"])) * MX - np.sin(np.radians(st["yaw"])) * MZ
    RF = rot(axis, st["fold"])
    leg = {p["name"]: p for p in legd["parts"]}
    def samples(p): return surface_samples(np.array(p["vertices"]).reshape(-1, 3), np.array(p["triangles"]).reshape(-1, 3), step)
    w = json.load(open(os.path.join(HERE, "data", "kr67_main_wheel_L.json")))
    Sd = samples(leg["sprung"]); Ud = np.concatenate([samples(leg["unsprung"]), samples(w)])
    out = []
    for ext in (0.0, EXT):
        U2 = (Ud - ext * SY - SP) @ rot(SY, st["strut"]).T + SP
        out.append((np.concatenate([Sd, U2]) - HINGE) @ RF.T + HINGE)
    return np.concatenate(out)
