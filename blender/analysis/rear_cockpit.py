"""Ray casts for the compartment behind the seat: canopy glass and fuselage surfaces under the rear canopy (MiG frame).
python rear_cockpit.py <MiG29Source>"""
import json, sys, numpy as np

d = json.load(open(sys.argv[1] + "/mig29_mesh.json"))


def tri(name):
    p = next(p for p in d["parts"] if p["name"] == name)
    V = np.array(p["vertices"]).reshape(-1, 3); T = np.array(p["triangles"]).reshape(-1, 3)
    return V[T[:, 0]], V[T[:, 1]] - V[T[:, 0]], V[T[:, 2]] - V[T[:, 0]]


def hits(m, o, dv):
    A, E1, E2 = m; o = np.array(o, float); dv = np.array(dv, float)
    p = np.cross(dv, E2); det = (E1 * p).sum(1); ok = np.abs(det) > 1e-9; inv = np.where(ok, 1 / np.where(ok, det, 1), 0)
    s = o - A; u = (s * p).sum(1) * inv; q = np.cross(s, E1); v = (dv * q).sum(1) * inv; t = (E2 * q).sum(1) * inv
    mk = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 0)
    return np.sort(t[mk])


B, G = tri("body"), tri("canopy")
print("from above: glass top y | body hit ys, per station z and x")
for z in np.arange(5.35, 6.26, 0.1):
    row = []
    for x in (0.0, 0.15, 0.30, 0.40):
        g = hits(G, [x, 3, z], [0, -1, 0]); b = hits(B, [x, 3, z], [0, -1, 0])
        row.append(f"x{x:.2f} g {('%.2f' % (3 - g[0])) if len(g) else '--':>4} b {','.join('%.2f' % (3 - t) for t in b[:3]) or '--'}")
    print(f"z {z:.2f} | " + " | ".join(row))
print("sideways from the centreline: glass and body half-widths")
for z in (5.5, 5.6, 5.7, 5.8, 5.9, 6.0):
    out = []
    for y in (0.8, 0.9, 1.0, 1.1, 1.2):
        g = hits(G, [0, y, z], [1, 0, 0]); b = hits(B, [0, y, z], [1, 0, 0])
        out.append(f"y{y}: g {g[0]:.2f}" if len(g) else f"y{y}: g --")
        out[-1] += f" b {b[0]:.2f}" if len(b) else " b --"
    print(f"z {z} | " + " | ".join(out))
print("from the eye looking back along the centreline: first glass/body hit")
for pitch in (0, 10, 20, 30):
    dv = np.array([0, np.sin(np.radians(-pitch)), -np.cos(np.radians(pitch))])
    o = [0, 1.097, 6.553]
    g = hits(G, o, dv); b = hits(B, o, dv)
    print(f"down {pitch}: glass at {g[:2]}, body at {b[:2]}")
