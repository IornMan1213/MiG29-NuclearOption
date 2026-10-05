"""Writes MiG29Out/gear_new_<pose>.json: the dumped gear poses with both main legs re-posed by the main_gear_search.json result (left
leg posed, right leg = its mirror), the strut-top yoke above the skin trimmed, for gear_look.py previews before a Unity build.
python main_gear_apply.py <MiG29Out>"""
import json, os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
OUT = sys.argv[1]
R = json.load(open(os.path.join(OUT, "main_gear_search.json")))
# optional keyframes [[t, fold, strut, [Tx, Ty, Tz]], ...] (plugin GearPathDriver); default: the game's straight path
KF = json.loads(os.environ["MAIN_KF"]) if "MAIN_KF" in os.environ else [[0, 0, 0, [0, 0, 0]], [1, R["fold"], R["strut"], R["T"]]]


def at(t):
    for (t0, f0, s0, T0), (t1, f1, s1, T1) in zip(KF, KF[1:]):
        if t <= t1 + 1e-9:
            a = (t - t0) / (t1 - t0)
            return f0 + (f1 - f0) * a, s0 + (s1 - s0) * a, np.array(T0) + (np.array(T1) - np.array(T0)) * a
    return KF[-1][1], KF[-1][2], np.array(KF[-1][3])

hidden = np.load(os.path.join(OUT, "main_hidden.npy"))   # not used for trimming (needs air); trimming uses the yoke rule below
from main_gear_search_lib import rot, pose_points   # noqa

down = json.load(open(os.path.join(OUT, "gear_down.json")))
g = next(g for g in down["gears"] if g["name"] == "gear_L_sprung")
for name, t, doors_from in (("down", 0, "down"), ("q1", 0.35, "mid"), ("mid", 0.7, "mid"), ("q3", 0.85, "mid"), ("up", 1.0, "up")):
    src = json.load(open(os.path.join(OUT, f"gear_{doors_from}.json")))
    parts = [p for p in src["parts"] if "gearHinge_L" not in p["path"] and "gearHinge_R" not in p["path"]]
    for p in down["parts"]:
        if "gearHinge_L" not in p["path"]: continue
        V = np.array(p["v"]).reshape(-1, 3); Tr = np.array(p["t"]).reshape(-1, 3)
        if p["name"] == "gear_L_sprung":
            # yoke: triangles above y 0.18 with the gear down stand above the wing root skin
            Tr = Tr[~np.any(V[Tr][:, :, 1] > 0.18, axis=1)]
        f, st, T = at(t)
        P = pose_points(g, V, "_unsprung" in p["path"], f, st, T)
        parts.append({"name": p["name"], "path": p["path"], "v": P.ravel().tolist(), "t": Tr.ravel().tolist()})
        M = P * np.array([-1, 1, 1])
        parts.append({"name": p["name"].replace("_L", "_R"), "path": p["path"].replace("_L", "_R"), "v": M.ravel().tolist(), "t": Tr[:, [0, 2, 1]].ravel().tolist()})
    json.dump({"parts": parts}, open(os.path.join(OUT, f"gear_new_{name}.json"), "w"))
    print("[apply]", name)
