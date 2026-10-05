"""Two-stage main-gear retraction path for the plugin's GearPathDriver: fold + twist first (with translation T1, chosen so the leg
rises through the bay opening without cutting the skin), then slide into the stow (main_gear_search.json). Prints the skin clipping
along candidate paths. python main_gear_path.py <MiG29Source> <MiG29Out>"""
import json, os, sys
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tools"))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bay_grid
from main_gear_search_lib import pose_points

SRC, OUT = sys.argv[1], sys.argv[2]
R = json.load(open(os.path.join(OUT, "main_gear_search.json")))
F, S, TF = R["fold"], R["strut"], np.array(R["T"])
H = 0.025
box = [((0.42, -0.88, 3.59), (0.98, -0.30, 3.61))]
hid = bay_grid.Hidden(bay_grid.skin_triangles(SRC, ("body", "canopy")), (0.35, -1.15, -1.3), (2.35, 0.85, 5.0), H, boxes=box)
deep = bay_grid.signed_margin(~hid.air, H, steps=4) > 0.04
d = json.load(open(os.path.join(OUT, "gear_down.json")))
g = next(x for x in d["gears"] if x["name"] == "gear_L_sprung")
parts = [(np.array(p["v"]).reshape(-1, 3), "_unsprung" in p["path"]) for p in d["parts"] if "gearHinge_L" in p["path"] and "chock" not in p["name"]]
parts = [(V[V[:, 1] <= 0.18] if not u else V, u) for V, u in parts]     # yoke trimmed


def clip(f, s, T):
    P = np.concatenate([pose_points(g, V, u, f, s, T) for V, u in parts]) * np.array([-1, 1, 1])
    k = hid.index(P); ok = np.all((k >= 0) & (k < np.array(hid.shape)), axis=1); k = k[ok]
    return deep[k[:, 0], k[:, 1], k[:, 2]].mean()


def path(kf_t, f1, s1, T1, n=9):
    out = []
    for t in np.linspace(0, 1, n):
        if t <= kf_t: a = t / kf_t; f, s, T = f1 * a, s1 * a, T1 * a
        else: a = (t - kf_t) / (1 - kf_t); f, s, T = f1 + (F - f1) * a, s1 + (S - s1) * a, T1 + (TF - T1) * a
        out.append(clip(f, s, T))
    return np.array(out)


print("linear (game) path:", np.round(path(0.5, F / 2, S / 2, TF / 2) * 100, 1))
best = []
for f1 in (F, F * 0.9, F * 0.8):
    for s1 in (S, S * 0.8, S * 0.6):
        for tx in np.arange(-0.1, 0.45, 0.05):
            for ty in np.arange(-0.1, 0.25, 0.05):
                for tz in np.arange(-0.2, 0.25, 0.1):
                    T1 = np.array([tx, ty, tz]); c = clip(f1, s1, T1)
                    best.append((c, f1, s1, T1))
best.sort(key=lambda b: b[0])
for c, f1, s1, T1 in best[:8]:
    p = path(0.7, f1, s1, T1)
    print(f"kf fold {f1:.0f} strut {s1:.0f} T1 {np.round(T1, 2)}: kf clip {c * 100:.1f}%  path {np.round(p * 100, 1)}")
