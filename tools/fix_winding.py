"""Repair body triangles whose shading normals point into the airframe.

The source model has about a hundred triangles (1.4 m2: a band across the top of the engine nacelles by the upper airbrake, and
patches at the front of the LERX) whose vertex normals point against their own winding. The winding agrees with the surrounding
skin (checked across shared edges), so it is the normals that are wrong: those spots were lit as if from inside and showed as
dark patches on the outside ("normals issue on the outside", user report, v0.9.1). Their vertex normals are recomputed from the
triangles that use them. A triangle wound against its neighbours as well would be flipped instead (none are in the current model).
Idempotent: run on every build before the tools that copy body triangles (airbrake_cut.py takes the airbrake skin from here).

python tools/fix_winding.py <MiG29Source dir>"""
import json, os, sys
import numpy as np

SRC = sys.argv[1]
PATH = os.path.join(SRC, "mig29_mesh.json")
PARTS = ("body", "cockpit")

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

    bad, g = against()
    if not bad.any():
        print(f"[normals] {p['name']}: clean"); continue
    # winding check against the neighbours (welded positions; the mesh splits vertices by normal / uv)
    key = {}
    w = np.array([key.setdefault(tuple(np.round(x, 4)), len(key)) for x in v])
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
    p["triangles"] = t.reshape(-1).tolist()
    p["normals"] = np.round(n, 6).reshape(-1).tolist()
    changed_any = True
if changed_any:
    json.dump(d, open(PATH, "w"))
