"""Checks the stowed legs of a MiG29GearDump 'up' dump against the hidden space inside the MiG skin, with the unsprung part both at
its prefab rest pose (as dumped: an aircraft spawned with the gear up) and at full suspension extension. LandingGear.FixedUpdate keeps
setting unsprung = bumpStop - unsprung.up * (suspensionTravel - wheelRadius) while the gear moves in the air, so a leg retracted in
flight stows with the wheel further out along the leg (main 0.21 m, nose 0.31 m beyond the rest pose).
python stow_check.py <MiG29Source> <MiG29Out> [up dump, default gear_up.json]"""
import json, os, sys
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tools"))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bay_grid
from main_gear_search_lib import rot

SRC, OUT = sys.argv[1], sys.argv[2]
UP = sys.argv[3] if len(sys.argv) > 3 else "gear_up.json"
up = json.load(open(os.path.join(OUT, UP)))
H = 0.025
BOX = [((0.42, -0.88, 3.59), (0.98, -0.30, 3.61)), ((-0.98, -0.88, 3.59), (-0.42, -0.30, 3.61))]
skin = bay_grid.skin_triangles(SRC, ("body", "gear_doors_closed", "canopy"))
REGIONS = {"F": ((-0.7, -1.2, 2.6), (0.7, 1.4, 7.4)), "L": ((-2.35, -1.15, -1.3), (-0.35, 0.85, 5.0))}
EXT = {"F": float(os.environ.get("EXT_F", "0.308")), "L": float(os.environ.get("EXT_M", "0.2125"))}
for side, (lo, hi) in REGIONS.items():
    hid = bay_grid.Hidden(skin, lo, hi, H, boxes=BOX)
    M = bay_grid.signed_margin(~hid.air, H, steps=12)
    g = next(x for x in up["gears"] if x["name"] == f"gear_{side}_sprung")
    sy = np.array(g["srt_y"]); hx = np.array(g["hinge_x"])
    axis_up = rot(hx, g["fold"]) @ sy             # the unsprung's up axis once folded (strut twist is about that same axis)

    def m_of(P):
        k = hid.index(P); inside = np.all((k >= 0) & (k < np.array(hid.shape)), axis=1)
        k = np.clip(k, 0, np.array(hid.shape) - 1)
        return np.where(inside, M[k[:, 0], k[:, 1], k[:, 2]], -0.5)
    for label, ext in (("rest", 0.0), ("extended", EXT[side])):
        out = []
        for p in up["parts"]:
            if f"gearHinge_{side}" not in p["path"] or "chock" in p["name"]: continue
            P = np.array(p["v"]).reshape(-1, 3)
            if "_unsprung" in p["path"]: P = P - ext * axis_up
            m = m_of(P)
            w = P[m.argmin()]
            out.append(f"{p['name']} {m.min() * 100:+.1f} cm ({np.mean(m < 0) * 100:.0f}% out, worst {np.round(w, 2)})")
        print(f"[stow] {side} {label:8s}: " + "; ".join(out))

