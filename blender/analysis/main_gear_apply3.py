"""gear_new3_<pose>.json for gear_look.py: main legs posed along the plugin path of main_gear_path2.json (or the game's straight arc
with STRAIGHT=1), unsprung extended as in flight. python main_gear_apply3.py <MiG29Source> <MiG29Out>  env: TS="0,0.2,..." """
import json, os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
SRC, OUT = sys.argv[1], sys.argv[2]
sys.argv = sys.argv[:3]
from main_gear_search_lib import rot
if os.environ.get("PATH3") == "1":
    import main_gear_path3 as m
    P2 = json.load(open(os.path.join(OUT, "main_gear_path3.json")))
    KEY = (P2["K"], P2["yaw1"], P2["fold1"], P2["strut1"])
else:
    import main_gear_path2 as m
    P2 = json.load(open(os.path.join(OUT, "main_gear_path2.json")))
    KEY = (P2["K"], P2["yaw1"], P2["fold1"], P2["strut1"], np.array(P2["T1"]))
STRAIGHT = os.environ.get("STRAIGHT") == "1"
leg = {p["name"]: p for p in m.leg["parts"]}
wheel = next(p for p in m.d0["parts"] if p["name"] == "wheel_L")
pieces = [("gear_L_sprung", np.array(leg["sprung"]["vertices"]).reshape(-1, 3), np.array(leg["sprung"]["triangles"]).reshape(-1, 3), False),
          ("gear_L_unsprung", np.array(leg["unsprung"]["vertices"]).reshape(-1, 3), np.array(leg["unsprung"]["triangles"]).reshape(-1, 3), True),
          ("wheel_L", np.array(wheel["v"]).reshape(-1, 3), np.array(wheel["t"]).reshape(-1, 3), True)]
for t in [float(x) for x in os.environ.get("TS", "0,0.2,0.4,0.6,0.8,0.85,0.9,0.95,1").split(",")]:
    if STRAIGHT: Rm, s, T = rot(m.axis(m.R["yaw"]), m.F * t), m.S * t, m.TF * t
    else: Rm, s, T = m.sample(KEY, t) if t < 1 else (m.RF, m.S, m.TF)
    src = json.load(open(os.path.join(OUT, "gear_mid.json" if 0 < t < 1 else ("gear_down.json" if t == 0 else "gear_up.json"))))
    parts = [p for p in src["parts"] if "gearHinge_L" not in p["path"] and "gearHinge_R" not in p["path"]]
    for pname, V, Tr, uns in pieces:
        if uns:
            V = V - (m.EXT if t > 0 else 0) * m.SY
            V = (V - m.SP) @ rot(m.SY, s).T + m.SP
        P = (V - m.HINGE) @ Rm.T + m.HINGE + T
        path = f"gearMount_L/gearHinge_L/{pname}"
        parts.append({"name": pname, "path": path, "v": P.ravel().tolist(), "t": Tr.ravel().tolist()})
        M = P * np.array([-1, 1, 1])
        parts.append({"name": pname.replace("_L", "_R"), "path": path.replace("_L", "_R"), "v": M.ravel().tolist(), "t": Tr[:, [0, 2, 1]].ravel().tolist()})
    name = f"new3_{int(round(t * 100)):03d}"
    json.dump({"parts": parts}, open(os.path.join(OUT, f"gear_{name}.json"), "w"))
    print(name)
