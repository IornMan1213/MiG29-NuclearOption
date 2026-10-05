"""Two-stage main-gear retraction path for the plugin's GearPathDriver, ending at the skewed-hinge stow of main_gear_search2.py
(which is also what the game does on its own, in one straight arc). The model's main bay opening is a narrow strip along the nacelle /
wing-root junction, and the straight arc carries the wheel through the skin ahead of it. With the plugin the leg first rotates about
its own axis choice (yaw1) up to a keyframe at t = K (fold1, strut twist strut1, shift T1), edge-on through the slot, then takes the
shortest rotation on to the stow while already inside the skin. A pose scores by how much of the lower leg / wheel is buried in
structure while part of it is still outside the airframe (visible piercing), unsprung extended as in flight.
python main_gear_path2.py <MiG29Source> <MiG29Out>  ->  MiG29Out/main_gear_path2.json"""
import json, os, sys, itertools
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tools"))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bay_grid
from main_gear_search_lib import rot

SRC, OUT = sys.argv[1], sys.argv[2]
R = json.load(open(os.path.join(OUT, os.environ.get("RESULT", "main_gear_search2.json"))))
leg = json.load(open(os.path.join(SRC, "main_gear.json")))
piv = leg["pivots"]
HINGE = np.array(piv["hinge"]); SP = np.array(piv["strut_pos"]); SY = np.array(piv["strut_y"]); HX = np.array(piv["hinge_x"])
EXT = float(piv["extension"])
MX = HX / np.linalg.norm(HX); MY = SY - MX * (SY @ MX); MY /= np.linalg.norm(MY); MZ = np.cross(MX, MY)
F, S, TF = R["fold"], R["strut"], np.array(R["T"])
d0 = json.load(open(os.path.join(OUT, "gear_down.json")))
W = np.concatenate([np.array(p["v"]).reshape(-1, 3) for p in d0["parts"] if p["name"] == "wheel_L"])
Sv = np.array([p for p in leg["parts"] if p["name"] == "sprung"][0]["vertices"]).reshape(-1, 3)
Uv = np.concatenate([np.array([p for p in leg["parts"] if p["name"] == "unsprung"][0]["vertices"]).reshape(-1, 3), W]) - EXT * SY
allP = np.concatenate([Sv, Uv])
_, keep = np.unique(np.round(allP / 0.02).astype(int), axis=0, return_index=True)
isU = np.arange(len(allP)) >= len(Sv)
Ss, Us = allP[keep][~isU[keep]], allP[keep][isU[keep]]
nS = len(Ss)

H = 0.025
LO, HI = (0.35, -1.15, -1.3), (2.35, 0.85, 5.0)
BOX = [((0.42, -0.88, 3.59), (0.98, -0.30, 3.61))]
cache = os.path.join(OUT, "path2_grid.npz")
if os.path.exists(cache):
    z = np.load(cache); deep, outside = z["deep"], z["outside"]
else:
    hid = bay_grid.Hidden(bay_grid.skin_triangles(SRC, ("body", "canopy")), LO, HI, H, boxes=BOX)
    deep = bay_grid.signed_margin(~hid.air, H, steps=4) > 0.04          # inside structure even with the doors open
    hidc = bay_grid.Hidden(bay_grid.skin_triangles(SRC, ("body", "gear_doors_closed", "canopy")), LO, HI, H, boxes=BOX)
    outside = hidc.air                                                   # outside the closed airframe envelope
    np.savez_compressed(cache, deep=deep, outside=outside)
SH = np.array(deep.shape)


def axis(yaw):
    a = np.radians(yaw); return np.cos(a) * MX - np.sin(a) * MZ


def clipR(Rm, s, T):
    U2 = (Us - SP) @ rot(SY, s).T + SP
    P = (np.concatenate([Ss, U2]) - HINGE) @ Rm.T + HINGE + T
    k = np.round((P * np.array([-1.0, 1, 1]) - np.array(LO)) / H).astype(int)
    ok = np.all((k >= 0) & (k < SH), axis=1)
    k = np.clip(k, 0, SH - 1)
    dp = deep[k[:, 0], k[:, 1], k[:, 2]] & ok
    out = outside[k[:, 0], k[:, 1], k[:, 2]] | ~ok
    # the strut top legitimately sits in the bay roof and a stowed leg is all inside: only the lower leg / wheel straddling counts
    du, ou = dp[nS:].mean(), out[nS:].mean()
    return min(du, ou) * 2 if ou > 0.03 else 0.0


def axang(Rm):
    ang = np.degrees(np.arccos(np.clip((np.trace(Rm) - 1) / 2, -1, 1)))
    if ang < 1e-6: return np.array([1.0, 0, 0]), 0.0
    v = np.array([Rm[2, 1] - Rm[1, 2], Rm[0, 2] - Rm[2, 0], Rm[1, 0] - Rm[0, 1]])
    return v / np.linalg.norm(v), ang


RF = rot(axis(R["yaw"]), F)


def sample(key, t):
    K, y1, f1, s1, T1 = key
    R1 = rot(axis(y1), f1)
    if t <= K:
        u = t / K; return rot(axis(y1), f1 * u), s1 * u, T1 * u
    u = (t - K) / (1 - K)
    ax, ang = axang(RF @ R1.T)
    return rot(ax, ang * u) @ R1, s1 + (S - s1) * u, T1 + (TF - T1) * u


def path(key, n=17):
    return np.array([clipR(*sample(key, t)) for t in np.linspace(0, 1, n)[1:-1]])


if __name__ == "__main__":
    straight = np.array([clipR(RF if t == 1 else rot(axis(R["yaw"]), F * t), S * t, TF * t) for t in np.linspace(0, 1, 17)[1:-1]])
    print(f"[path2] straight arc: max {straight.max() * 100:.0f}%  {np.round(straight * 100)}")
    best = []
    for K, y1, f1, s1, tx, ty, tz in itertools.product((0.5, 0.6, 0.7, 0.8), (-20, -10, 0, 10, 20), np.linspace(-90, -40, 6),
                                                       (-30, 0, 30, 60), (0, 0.15, 0.3), (-0.1, 0, 0.1), (-0.15, 0, 0.15)):
        key = (K, y1, f1, s1, np.array([tx, ty, tz]))
        if clipR(*sample(key, K)) > 0.05: continue
        p = path(key)
        best.append((p.max(), p.mean(), key, p))
    best.sort(key=lambda b: (b[0], b[1]))
    for m, mean, key, p in best[:8]:
        print(f"[path2] K {key[0]} yaw {key[1]} fold {key[2]:.0f} strut {key[3]} T1 {key[4]}: max {m * 100:.0f}%, mean {mean * 100:.1f}%  {np.round(p * 100)}")
    m, mean, key, p = best[0]
    json.dump({"K": key[0], "yaw1": key[1], "fold1": float(key[2]), "strut1": key[3], "T1": key[4].tolist(), "final": R},
              open(os.path.join(OUT, "main_gear_path2.json"), "w"))
