"""Writes MiG29Out/gear_cut_<pose>.json for gear_look.py: the airframe skin with the main_bay_cut.json triangles shown as door (blue),
over a gear_new3_<pose> dump. python cut_preview.py <MiG29Source> <MiG29Out> <pose,pose,...>"""
import json, os, sys
import numpy as np
S, O = sys.argv[1], sys.argv[2]
m = json.load(open(os.path.join(S, "mig29_mesh.json"))); b = next(p for p in m["parts"] if p["name"] == "body")
V = np.array(b["vertices"]).reshape(-1, 3); T = np.array(b["triangles"]).reshape(-1, 3)
c = json.load(open(os.path.join(S, "main_bay_cut.json"))); cut = set(c["left"]) | set(c["right"])
keep = np.array([i not in cut for i in range(len(T))])
for pose in sys.argv[3].split(","):
    dd = json.load(open(os.path.join(O, f"gear_{pose}.json")))
    parts = [p for p in dd["parts"] if not p["name"].startswith("MiG29_skin")]
    parts.append({"name": "MiG29_skin_all", "path": "x", "v": V.ravel().tolist(), "t": T[keep].ravel().tolist()})
    parts.append({"name": "MiG29_door_cut", "path": "x", "v": V.ravel().tolist(), "t": T[~keep].ravel().tolist()})
    json.dump({"parts": parts}, open(os.path.join(O, f"gear_cut_{pose}.json"), "w"))
    print(f"cut_{pose}")
