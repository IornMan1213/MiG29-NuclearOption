"""Writes MiG29Out/gear_new2_<pose>.json for gear_look.py: the dumped gear poses with both main legs replaced by the from-scratch leg
(MiG29Source/main_gear.json + the KR-67 wheel) posed by the main_gear_search2.py result (skewed hinge: yaw, fold, strut twist, shift;
left leg posed, right leg its mirror). In-flight poses use the fully extended unsprung (EXT=1, default), as LandingGear holds it.
python main_gear_apply2.py <MiG29Source> <MiG29Out>   env: RESULT (default main_gear_search2.json), EXT (1 / 0)"""
import json, os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from main_gear_search_lib import rot

SRC, OUT = sys.argv[1], sys.argv[2]
R = json.load(open(os.path.join(OUT, os.environ.get("RESULT", "main_gear_search2.json"))))
leg = json.load(open(os.path.join(SRC, "main_gear.json")))
piv = leg["pivots"]
HINGE = np.array(piv["hinge"]); SP = np.array(piv["strut_pos"]); SY = np.array(piv["strut_y"]); HX = np.array(piv["hinge_x"])
EXT = float(piv["extension"]) * float(os.environ.get("EXT", "1"))
MX = HX / np.linalg.norm(HX); MY = SY - MX * (SY @ MX); MY /= np.linalg.norm(MY); MZ = np.cross(MX, MY)
a = np.radians(R["yaw"]); AX = np.cos(a) * MX - np.sin(a) * MZ
down = json.load(open(os.path.join(OUT, "gear_down.json")))
wheel = next(p for p in down["parts"] if p["name"] == "wheel_L")
new = {p["name"]: p for p in leg["parts"]}
pieces = [("gear_L_sprung", np.array(new["sprung"]["vertices"]).reshape(-1, 3), np.array(new["sprung"]["triangles"]).reshape(-1, 3), False),
          ("gear_L_unsprung", np.array(new["unsprung"]["vertices"]).reshape(-1, 3), np.array(new["unsprung"]["triangles"]).reshape(-1, 3), True),
          ("wheel_L", np.array(wheel["v"]).reshape(-1, 3), np.array(wheel["t"]).reshape(-1, 3), True)]


def pose(V, uns, t):
    if uns:
        V = V - (EXT if t > 0 else 0.0) * SY
        V = (V - SP) @ rot(SY, R["strut"] * t).T + SP
    return (V - HINGE) @ rot(AX, R["fold"] * t).T + HINGE + np.array(R["T"]) * t


POSES = [(n, float(t), d) for n, t, d in (x.split(":") for x in os.environ["POSES"].split(","))] if "POSES" in os.environ else \
    [("down", 0, "down"), ("q1", 0.25, "mid"), ("mid", 0.5, "mid"), ("q3", 0.75, "mid"), ("up", 1.0, "up")]
for name, t, doors_from in POSES:
    src = json.load(open(os.path.join(OUT, f"gear_{doors_from}.json")))
    parts = [p for p in src["parts"] if "gearHinge_L" not in p["path"] and "gearHinge_R" not in p["path"]]
    for pname, V, Tr, uns in pieces:
        P = pose(V, uns, t)
        path = f"gearMount_L/gearHinge_L/{pname}"
        parts.append({"name": pname, "path": path, "v": P.ravel().tolist(), "t": Tr.ravel().tolist()})
        M = P * np.array([-1, 1, 1])
        parts.append({"name": pname.replace("_L", "_R"), "path": path.replace("_L", "_R"), "v": M.ravel().tolist(), "t": Tr[:, [0, 2, 1]].ravel().tolist()})
    json.dump({"parts": parts}, open(os.path.join(OUT, f"gear_new2_{name}.json"), "w"))
    print("[apply2]", name)
