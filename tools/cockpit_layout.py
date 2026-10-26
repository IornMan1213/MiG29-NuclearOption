"""Cockpit editor layout: moves, turns, scales or hides the cockpit's instrument groups before the Unity build.

    python tools/cockpit_layout.py <source dir> <repo>/cockpit/layout.json

blender/cockpit_build.py writes cockpit_mesh.json with "groups": each instrument, display, plate or switch panel as a vertex
range of the tub plus the parts it owns (needles, lamps, screens), with its frame (centre, normal toward the pilot, right, up;
Unity MiG frame). The editor (Documents\\CockpitEditor) saves, per group it changed, the frame it should have now:
    {"groups": {"<id>": {"pos": [x, y, z], "normal": [..], "up": [..], "scale": 1.0, "hidden": false}}}
Each changed group is turned so its normal and up match, scaled about its centre and moved there; a hidden one shrinks to
nothing (its parts stay, so the plugin still finds them). The Blender output is kept as cockpit_mesh_base.json, so this can run
again (a quick build skips Blender) without moving anything twice.
"""
import json, os, shutil, sys

import numpy as np


def unit(v):
    v = np.asarray(v, dtype=np.float64)
    return v / max(np.linalg.norm(v), 1e-12)


def basis(n, up):
    n = unit(n)
    up = unit(np.asarray(up, dtype=np.float64) - n * np.dot(up, n))
    r = np.cross(up, n)                 # right = up x n, as the build script's frames (up = n x right)
    return np.stack([unit(r), up, n], axis=1)


def apply(src, layout_path):
    mesh_p, base_p = os.path.join(src, "cockpit_mesh.json"), os.path.join(src, "cockpit_mesh_base.json")
    d = json.load(open(mesh_p))
    if not d.get("layout_applied"):
        shutil.copyfile(mesh_p, base_p)            # fresh from Blender
    else:
        d = json.load(open(base_p))
    layout = json.load(open(layout_path)) if layout_path and os.path.exists(layout_path) else {}
    changes = layout.get("groups", {})
    parts = {p["name"]: p for p in d["parts"]}
    tub = parts["tub"]
    done = []
    for g in d.get("groups", []):
        L = changes.get(g["id"])
        if not L:
            continue
        c0, n0, r0, u0 = (np.array(x, dtype=np.float64) for x in g["frame"])
        B0 = basis(n0, u0)
        if L.get("hidden"):
            R, s, pos = np.eye(3), 1e-4, c0
        else:
            B1 = basis(L["normal"], L["up"])
            R, s, pos = B1 @ B0.T, float(L.get("scale", 1.0)), np.array(L["pos"], dtype=np.float64)

        def pts(arr, lo=0, hi=None):
            a = np.array(arr, dtype=np.float64).reshape(-1, 3)
            hi = len(a) if hi is None else hi
            a[lo:hi] = (a[lo:hi] - c0) @ R.T * s + pos
            return a.reshape(-1).tolist()

        def dirs(arr, lo=0, hi=None):
            a = np.array(arr, dtype=np.float64).reshape(-1, 3)
            hi = len(a) if hi is None else hi
            a[lo:hi] = a[lo:hi] @ R.T
            return a.reshape(-1).tolist()

        v0, v1 = g["verts"]
        tub["vertices"] = pts(tub["vertices"], v0, v1)
        tub["normals"] = dirs(tub["normals"], v0, v1)
        for name in g.get("parts", []):
            p = parts.get(name)
            if not p:
                continue
            p["vertices"] = pts(p["vertices"]); p["normals"] = dirs(p["normals"])
            if p.get("pivot") and name.startswith("ins_"):
                p["pivot"] = pts(p["pivot"])
            for k in ("axis", "up"):
                if p.get(k):
                    p[k] = dirs(p[k])
        done.append(g["id"] + (" (hidden)" if L.get("hidden") else ""))
    d["layout_applied"] = True
    json.dump(d, open(mesh_p, "w"))
    print(f"[cockpit layout] {len(done)} groups changed" + (f": {', '.join(done)}" if done else ""))


if __name__ == "__main__":
    apply(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None)
