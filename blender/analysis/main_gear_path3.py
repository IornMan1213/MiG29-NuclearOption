"""Plugin retraction path for the main legs, scored by where the leg crosses the closed skin. The leg may only come through the bay
openings: the model's door strip under the wing root, plus a side door at the top of the intake trunk (where the real MiG-29's main
bay door is; tools/main_bay_door.py cuts it). Every other crossing of the skin, in any pose, is visible clipping.
Path (MiG29Instruments GearPathDriver): rotation about the mount x axis yawed by yaw1 up to keyframe K (fold1, strut twist strut1),
then the shortest rotation on to the skewed-hinge stow of main_gear_search2.json (what the game reaches without the plugin).
python main_gear_path3.py <MiG29Source> <MiG29Out>  ->  MiG29Out/main_gear_path3.json"""
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
_, keep = np.unique(np.round(allP / 0.03).astype(int), axis=0, return_index=True)
isU = np.arange(len(allP)) >= len(Sv)
Ss, Us = allP[keep][~isU[keep]], allP[keep][isU[keep]]

G = 0.03
GLO, GHI = (-2.4, -1.2, -1.4), (-0.35, 0.9, 5.0)
cache = os.path.join(OUT, "path3_grid.npy")
if os.path.exists(cache): AIR = np.load(cache)
else:
    hid = bay_grid.Hidden(bay_grid.skin_triangles(SRC, ("body", "gear_doors_closed", "canopy")), GLO, GHI, G,
                          boxes=[((-0.98, -0.88, 3.59), (-0.42, -0.30, 3.61))])
    AIR = hid.air; np.save(cache, AIR)
SH = np.array(AIR.shape)
# allowed openings (left side, MiG frame): the model's door strip, and the side door box at the top of the trunk
STRIP = (np.array([-1.85, -0.40, -0.35]), np.array([-0.95, 0.12, 3.0]))
SIDE = tuple(np.array(v) for v in json.loads(os.environ.get("SIDE", "[[-1.35, -0.45, 1.45], [-0.75, 0.10, 3.45]]")))


def axis(yaw):
    a = np.radians(yaw); return np.cos(a) * MX - np.sin(a) * MZ


def posed(Rm, s, T):
    U2 = (Us - SP) @ rot(SY, s).T + SP
    return (np.concatenate([Ss, U2]) - HINGE) @ Rm.T + HINGE + T


def crossings(P):
    """Cells where the leg is both inside and outside the closed skin (it crosses the skin there), outside the allowed openings."""
    k = np.round((P - np.array(GLO)) / G).astype(int)
    ok = np.all((k >= 0) & (k < SH), axis=1); k = np.clip(k, 0, SH - 1)
    out = ~ok | AIR[k[:, 0], k[:, 1], k[:, 2]]
    if out.all() or not out.any(): return 0
    cin = {tuple(c) for c in (k[~out] // 2)}; cout = {tuple(c) for c in (k[out] // 2)}
    both = []
    for c in cin:
        if any((c[0] + a, c[1] + b, c[2] + d) in cout for a in (-1, 0, 1) for b in (-1, 0, 1) for d in (-1, 0, 1)):
            both.append(c)
    if not both: return 0
    X = (np.array(both) * 2 + 1) * G + np.array(GLO)
    allowed = np.all((X > STRIP[0]) & (X < STRIP[1]), axis=1) | np.all((X > SIDE[0]) & (X < SIDE[1]), axis=1)
    return int((~allowed).sum())


def axang(Rm):
    ang = np.degrees(np.arccos(np.clip((np.trace(Rm) - 1) / 2, -1, 1)))
    if ang < 1e-6: return np.array([1.0, 0, 0]), 0.0
    v = np.array([Rm[2, 1] - Rm[1, 2], Rm[0, 2] - Rm[2, 0], Rm[1, 0] - Rm[0, 1]])
    return v / np.linalg.norm(v), ang


RF = rot(axis(R["yaw"]), F)


def sample(key, t):
    K, y1, f1, s1 = key
    R1 = rot(axis(y1), f1)
    if t <= K:
        u = t / K; return rot(axis(y1), f1 * u), s1 * u, np.zeros(3)
    u = (t - K) / (1 - K)
    ax, ang = axang(RF @ R1.T)
    return rot(ax, ang * u) @ R1, s1 + (S - s1) * u, TF * u


def score(key, n=31):
    c = [crossings(posed(*sample(key, t))) for t in np.linspace(0, 1, n)[1:-1]]
    return sum(c), max(c), c


if __name__ == "__main__":
    st = score((1.0, R["yaw"], F, S))
    print(f"[path3] game's straight arc: {st[0]} crossing cells, worst pose {st[1]}  {st[2]}")
    best = []
    for K, y1, f1, s1 in itertools.product((0.5, 0.6, 0.7, 0.8), (-10, 0, 10, 20), (-40, -50, -60, -70, -80), (0, 30, 60, 90, 120)):
        tot, mx, c = score((K, y1, f1, s1), n=21)
        best.append((tot, mx, (K, y1, f1, s1), c))
    best.sort(key=lambda b: (b[0], b[1]))
    for tot, mx, key, c in best[:8]:
        print(f"[path3] K {key[0]} yaw1 {key[1]} fold1 {key[2]} strut1 {key[3]}: {tot} cells, worst {mx}  {c}")
    tot, mx, key, c = best[0]
    json.dump({"K": key[0], "yaw1": key[1], "fold1": key[2], "strut1": key[3], "final": R}, open(os.path.join(OUT, "main_gear_path3.json"), "w"))
