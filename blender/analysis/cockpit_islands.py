"""List the islands of the MiG cockpit part (bbox, tri count) and slice the body around the cockpit."""
import json, sys
import numpy as np
d = json.load(open(sys.argv[1]))
P = {p['name']: p for p in d['parts']}

def islands(p):
    v = np.array(p['vertices']).reshape(-1, 3); t = np.array(p['triangles']).reshape(-1, 3)
    # weld by position so UV seams don't split islands
    key = {tuple(np.round(x, 4)): i for i, x in enumerate(v)}
    w = np.array([key[tuple(np.round(x, 4))] for x in v])
    par = list(range(len(v)))
    def f(a):
        while par[a] != a: par[a] = par[par[a]]; a = par[a]
        return a
    for a, b, c in w[t]:
        for x, y in ((a, b), (b, c)):
            rx, ry = f(x), f(y)
            if rx != ry: par[rx] = ry
    root = np.array([f(w[a]) for a in t[:, 0]])
    out = []
    for r in np.unique(root):
        tt = t[root == r]; vv = v[tt.ravel()]
        out.append((len(tt), vv.min(0), vv.max(0)))
    return sorted(out, key=lambda o: -o[0])

for n, lo, hi in islands(P['cockpit']):
    print(f"{n:5d} tris  min {np.round(lo,2)}  max {np.round(hi,2)}")

body = P['body']; v = np.array(body['vertices']).reshape(-1, 3); t = np.array(body['triangles']).reshape(-1, 3)
for z in [5.4, 5.8, 6.2, 6.6, 7.0, 7.4, 7.8, 8.2]:
    pts = []
    for tri in v[t]:
        s = tri[:, 2] - z
        for i in range(3):
            a, b = tri[i], tri[(i + 1) % 3]; sa, sb = s[i], s[(i + 1) % 3]
            if sa * sb < 0:
                q = a + (b - a) * sa / (sa - sb); pts.append(q)
    pts = np.array(pts); pts = pts[(pts[:, 0] >= 0) & (pts[:, 1] > 0.0)]
    # half-width at heights
    row = []
    for y in [0.3, 0.5, 0.7, 0.8, 0.9, 1.0, 1.1]:
        m = pts[np.abs(pts[:, 1] - y) < 0.03]
        row.append(f"y{y}:{m[:,0].max():.3f}" if len(m) else f"y{y}:-")
    print(f"z={z}: top y {pts[:,1].max():.3f} | " + " ".join(row))
