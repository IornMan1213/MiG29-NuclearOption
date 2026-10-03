"""Ray casts into the MiG mesh (mig29_mesh.json, MiG frame) for the drop-tank station and the chaff/flare dispensers.
python stores_pos.py <MiG29Source>"""
import json, sys, numpy as np
d = json.load(open(sys.argv[1] + "/mig29_mesh.json"))
body = next(p for p in d["parts"] if p["name"] == "body")
V = np.array(body["vertices"]).reshape(-1, 3); T = np.array(body["triangles"]).reshape(-1, 3)
A, B, C = V[T[:, 0]], V[T[:, 1]], V[T[:, 2]]
E1, E2 = B - A, C - A


def hits(o, dvec):
    o = np.asarray(o, float); dvec = np.asarray(dvec, float)
    p = np.cross(dvec, E2); det = (E1 * p).sum(1)
    ok = np.abs(det) > 1e-9; inv = np.where(ok, 1 / np.where(ok, det, 1), 0)
    s = o - A; u = (s * p).sum(1) * inv
    q = np.cross(s, E1); v = (dvec * q).sum(1) * inv; t = (E2 * q).sum(1) * inv
    m = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 0)
    return np.sort(t[m])


print("wheel contact y", d["info"]["main_wheel_contact_L"][1], d["info"]["nose_wheel_contact"][1])
print("belly (lowest surface) along the centreline and under the nacelles:")
for z in np.arange(-3.0, 6.01, 0.5):
    row = []
    for x in (0.0, 0.3, 0.6, 0.9, 1.2):
        h = hits([x, -4, z], [0, 1, 0]); row.append(f"{(-4 + h[0]) if len(h) else float('nan'):6.2f}")
    print(f"z {z:5.1f}: " + " ".join(row))
print("top surface on the rear fuselage (y), x across, z along:")
for z in np.arange(-4.5, -0.49, 0.5):
    row = []
    for x in np.arange(0.0, 2.41, 0.3):
        h = hits([x, 5, z], [0, -1, 0]); row.append(f"{(5 - h[0]) if len(h) else float('nan'):5.2f}")
    print(f"z {z:5.1f}: " + " ".join(row))
