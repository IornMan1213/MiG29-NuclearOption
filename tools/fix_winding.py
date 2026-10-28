"""Repair body triangles whose shading normals point into the airframe, and corners on sharp creases.

The source model has about a hundred triangles (1.4 m2: a band across the top of the engine nacelles by the upper airbrake, and
patches at the front of the LERX) whose vertex normals point against their own winding. The winding agrees with the surrounding
skin (checked across shared edges), so it is the normals that are wrong: those spots were lit as if from inside and showed as
dark patches on the outside ("normals issue on the outside", user report, v0.9.1). Their vertex normals are recomputed from the
triangles that use them. A triangle wound against its neighbours as well would be flipped instead (none are in the current model).

Corners on sharp creases (thin trailing edges, chines, the nose) were the per-corner leftover: the model shares one vertex between
the faces either side of a crease, so its averaged normal can lie 75 deg or more off one of them (373 body corners facing away,
about 2,500 more at 75-90 deg), and that face shades as if lit from behind or at a graze: dark streaks along the edges. Such a
corner gets a vertex of its own with the area-weighted normal of the faces on its side of the crease (within 60 deg of its own
face). The new vertices are appended, so no existing index moves (smt_hump.py, main_bay_door.py and the builder refer to them).
Idempotent: run on every build before the tools that copy body triangles (airbrake_cut.py takes the airbrake skin from here).

python tools/fix_winding.py <MiG29Source dir>"""
import json, os, sys
import numpy as np

SRC = sys.argv[1]
PATH = os.path.join(SRC, "mig29_mesh.json")
PARTS = ("body", "cockpit", "aileron_L", "aileron_R", "flap_L", "flap_R", "rudder_L", "rudder_R", "stab_L", "stab_R")
COS_CREASE = np.cos(np.radians(75))   # a corner normal further than this off its own face is on a crease
COS_SIDE = np.cos(np.radians(60))     # faces within this of it are on its side of the crease

d = json.load(open(PATH))
changed_any = False
for p in d["parts"]:
    if p["name"] not in PARTS:
        continue
    v = np.array(p["vertices"], float).reshape(-1, 3)
    t = np.array(p["triangles"], int).reshape(-1, 3)
    n = np.array(p["normals"], float).reshape(-1, 3)

    def against():
        g = np.cross(v[t[:, 1]] - v[t[:, 0]], v[t[:, 2]] - v[t[:, 0]])
        a = np.linalg.norm(g, axis=1)
        return (np.einsum("ij,ij->i", g, n[t[:, 0]] + n[t[:, 1]] + n[t[:, 2]]) < 0) & (a > 1e-10), g

    # welded positions (the mesh splits vertices by normal / uv)
    key = {}
    w = np.array([key.setdefault(tuple(np.round(x, 4)), len(key)) for x in v])
    changed = False
    bad, g = against()
    if not bad.any():
        print(f"[normals] {p['name']}: no triangle against its normals")
    else:
        # winding check against the neighbours
        tw = w[t]
        edges = {}
        for i, (a, b, c) in enumerate(tw):
            for e in ((a, b), (b, c), (c, a)):
                edges.setdefault(e, []).append(i)
        flipped = 0
        for i in np.where(bad)[0]:
            a, b, c = tw[i]
            same = sum(1 for e in ((a, b), (b, c), (c, a)) for j in edges.get(e, []) if j != i and not bad[j])
            rev = sum(1 for e in ((a, b), (b, c), (c, a)) for j in edges.get((e[1], e[0]), []) if not bad[j])
            if same > rev:
                t[i] = t[i][[0, 2, 1]]; flipped += 1
        bad, g = against()
        # normals: every vertex of a still-inconsistent triangle gets the area-weighted normal of the triangles using it, so long as
        # that agrees with the winding of all of them (a vertex shared with consistent triangles keeps its normal)
        users = {}
        for i, tri in enumerate(t):
            for k in tri:
                users.setdefault(int(k), []).append(i)
        fixed = 0
        for k in sorted({int(x) for x in t[bad].reshape(-1)}):
            acc = g[users[k]].sum(axis=0)
            L = np.linalg.norm(acc)
            if L < 1e-12 or not all(np.dot(g[i], acc) > 0 for i in users[k]):
                continue
            n[k] = acc / L; fixed += 1
        bad, g = against()
        print(f"[normals] {p['name']}: {fixed} vertex normals recomputed, {flipped} triangles flipped, {int(bad.sum())} left")
        changed = True

    # creases: corners whose normal lies 75+ deg off their own face get a vertex with the normal of the faces on their side
    g = np.cross(v[t[:, 1]] - v[t[:, 0]], v[t[:, 2]] - v[t[:, 0]])
    a2 = np.linalg.norm(g, axis=1)
    fn = g / np.maximum(a2, 1e-12)[:, None]
    crease = (np.einsum("ij,ikj->ik", fn, n[t]) < COS_CREASE) & (a2 > 1e-10)[:, None]
    if crease.any():
        uv = np.array(p["uvs"], float).reshape(-1, 2)
        around = {}
        for i, tri in enumerate(t):
            for k in tri:
                around.setdefault(w[k], []).append(i)
        made, add_v, add_n, add_uv = {}, [], [], []
        for i, c in zip(*np.where(crease)):
            k = int(t[i, c])
            side = [j for j in around[w[k]] if np.dot(fn[j], fn[i]) > COS_SIDE]
            acc = g[side].sum(axis=0)
            nn = acc / np.linalg.norm(acc)
            mk = (k, tuple(np.round(nn, 3)))   # triangles on the same side of the crease share the new vertex
            if mk not in made:
                made[mk] = len(v) + len(add_v)
                add_v.append(v[k]); add_n.append(nn); add_uv.append(uv[k])
            t[i, c] = made[mk]
        v = np.vstack([v, add_v]); n = np.vstack([n, add_n]); uv = np.vstack([uv, add_uv])
        p["vertices"] = v.reshape(-1).tolist()
        p["uvs"] = uv.reshape(-1).tolist()
        print(f"[normals] {p['name']}: {int(crease.sum())} corners on creases given their own normals ({len(add_v)} vertices added)")
        changed = True
    else:
        print(f"[normals] {p['name']}: no corner on a crease")
    if changed:
        p["triangles"] = t.reshape(-1).tolist()
        p["normals"] = np.round(n, 6).reshape(-1).tolist()
        changed_any = True
if changed_any:
    json.dump(d, open(PATH, "w"))
