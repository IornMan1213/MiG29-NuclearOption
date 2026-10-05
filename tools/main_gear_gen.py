"""From-scratch MiG-29 main landing-gear leg (left leg, gear down, MiG frame; the right leg is its mirror). The KR-67 leg it
replaces is too bulky for the MiG's wing-root bay (brake housing, links and yoke stood 11-14 cm out of the skin when folded the way
the real leg folds), so this one is a slim MiG-style leg built on the KR-67's own pivots, keeping its physics exactly:
  sprung   : hinge trunnion, oleo cylinder with collar, side-brace lug, upper torque link, brake line
  unsprung : chrome piston, trailing lever to the axle, axle stub into the KR-67 wheel hub (wheel kept: 0.85 m, as the real 840 mm),
             lower torque link
Pivots (MiG frame) from MiG29Tools.MiG29GearDump: hinge, strut axis (strut-rotation pivot), wheel centre.
python main_gear_gen.py <MiG29Source>  ->  main_gear.json, gear_atlas.png, gear_metallic.png"""
import json, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SRC = sys.argv[1]
HINGE = np.array([-1.45946, 0.11610, 1.38257])
HINGE_X = np.array([0.99586, -0.08933, -0.01676])
STRUT_POS = np.array([-1.59433, -1.38786, 1.38484])       # strut-rotation pivot: bottom of the strut axis (unsprung origin, rest pose)
STRUT_Y = np.array([0.08932, 0.99600, -0.00150])           # unsprung up: the leg axis, the strut twist axis and the suspension axis
WHEEL = np.array([-1.877, -1.387, 1.18])                    # KR-67 wheel centre (axle along x)
# In the air LandingGear puts the unsprung at bumpStop - up * (travel 0.8 - wheel radius 0.427): 0.213 m past its rest pose
# (bumpStop (-1.580, -1.2285, 1.3846), MiG29GearDump). The piston must still overlap the barrel there.
EXTENSION = 0.213
DOWN = (STRUT_POS - HINGE) / np.linalg.norm(STRUT_POS - HINGE)
L = np.linalg.norm(STRUT_POS - HINGE)
FWD = np.cross(HINGE_X, DOWN); FWD /= np.linalg.norm(FWD)
if FWD[2] < 0: FWD = -FWD
SIDE = np.cross(DOWN, FWD)

# texture zones (u ranges): paint, chrome, dark
PAINT, CHROME, DARK = (0.02, 0.48), (0.52, 0.73), (0.77, 0.98)


class Mesh:
    def __init__(self): self.v, self.n, self.uv, self.t = [], [], [], []

    def tri(self, a, b, c, na, nb, nc, ua, ub, uc):
        i = len(self.v)
        self.v += [a, b, c]; self.n += [na, nb, nc]; self.uv += [ua, ub, uc]
        self.t += [i, i + 1, i + 2]

    def quad(self, a, b, c, d, nrm_out, zone, v0=0.0, v1=1.0, smooth=None):
        """Quad a-b-c-d, front face toward nrm_out (Unity: clockwise seen from the front, cross(b-a, c-a) toward the viewer)."""
        if np.dot(np.cross(b - a, c - a), nrm_out) < 0: a, b, c, d = d, c, b, a
        u0, u1 = zone
        na = nb = nc = nd = nrm_out
        if smooth is not None: na, nb, nc, nd = smooth if np.dot(np.cross(b - a, c - a), nrm_out) >= 0 else smooth[::-1]
        self.tri(a, b, c, na, nb, nc, (u0, v0), (u1, v0), (u1, v1))
        self.tri(a, c, d, na, nc, nd, (u0, v0), (u1, v1), (u0, v1))

    def cylinder(self, p0, p1, r0, zone, r1=None, n=16, caps=True):
        r1 = r0 if r1 is None else r1
        ax = p1 - p0; ln = np.linalg.norm(ax); ax = ax / ln
        ref = np.array([0, 1.0, 0]) if abs(ax[1]) < 0.9 else np.array([1.0, 0, 0])
        e1 = np.cross(ax, ref); e1 /= np.linalg.norm(e1); e2 = np.cross(ax, e1)
        ring = [np.cos(2 * np.pi * k / n) * e1 + np.sin(2 * np.pi * k / n) * e2 for k in range(n)]
        for k in range(n):
            a, b = ring[k], ring[(k + 1) % n]
            q = [p0 + a * r0, p0 + b * r0, p1 + b * r1, p1 + a * r1]
            mid = (a + b) / 2; mid /= np.linalg.norm(mid)
            self.quad(*q, mid, (zone[0] + (zone[1] - zone[0]) * k / n, zone[0] + (zone[1] - zone[0]) * (k + 1) / n), 0.05, 0.95,
                      smooth=(a, b, b, a))
        if caps:
            for c, r, s in ((p0, r0, -1), (p1, r1, 1)):
                for k in range(n):
                    a, b = c + ring[k] * r, c + ring[(k + 1) % n] * r
                    nrm = ax * s
                    tri = [c, a, b]
                    if np.dot(np.cross(tri[1] - tri[0], tri[2] - tri[0]), nrm) < 0: tri = [c, b, a]
                    uc = ((zone[0] + zone[1]) / 2, 0.5)
                    self.tri(*tri, nrm, nrm, nrm, uc, uc, uc)

    def box(self, c, axes, half, zone):
        X, Y, Z = [np.asarray(a, float) / np.linalg.norm(a) * h for a, h in zip(axes, half)]
        for s in (-1, 1):
            for A, B, C in ((X, Y, Z), (Y, Z, X), (Z, X, Y)):
                f = c + s * A
                self.quad(f - B - C, f + B - C, f + B + C, f - B + C, s * A / np.linalg.norm(A), zone)

    def dump(self, name):
        return {"name": name, "vertices": np.round(np.array(self.v), 5).ravel().tolist(),
                "normals": np.round(np.array([np.asarray(n) / np.linalg.norm(n) for n in self.n]), 5).ravel().tolist(),
                "uvs": np.round(np.array(self.uv), 5).ravel().tolist(), "triangles": self.t}


def at(s): return HINGE + DOWN * s                          # point on the strut axis, s metres below the hinge


sprung, unsprung = Mesh(), Mesh()
# hinge trunnion across the leg, along the hinge axis
sprung.cylinder(HINGE - HINGE_X * 0.12, HINGE + HINGE_X * 0.12, 0.05, PAINT)
sprung.box(at(0.05), (HINGE_X, DOWN, FWD), (0.07, 0.07, 0.065), PAINT)
# oleo cylinder: shoulder, barrel, collar
sprung.cylinder(at(0.06), at(0.16), 0.068, PAINT, r1=0.078)
sprung.cylinder(at(0.16), at(0.86), 0.078, PAINT)
sprung.cylinder(at(0.86), at(0.92), 0.088, PAINT)
# side-brace / retraction lug on the inboard face and the brace stub
sprung.box(at(0.30) - SIDE * 0.09, (DOWN, SIDE, FWD), (0.07, 0.03, 0.035), PAINT)
sprung.cylinder(at(0.30) - SIDE * 0.10, at(0.10) - SIDE * 0.16 + FWD * 0.10, 0.022, PAINT)
# upper torque link (front of the strut): collar -> knee
knee = at(1.06) + FWD * 0.15
sprung.box((at(0.90) + FWD * 0.08 + knee) / 2, (knee - at(0.90) - FWD * 0.08, SIDE, np.cross(knee - at(0.90), SIDE)),
           (np.linalg.norm(knee - at(0.90) - FWD * 0.08) / 2 + 0.02, 0.025, 0.018), PAINT)
# brake line down the back of the barrel
sprung.cylinder(at(0.12) - FWD * 0.09, at(0.88) - FWD * 0.095, 0.009, DARK, n=6)

# chrome piston (slides into the barrel; 0.35 m overlap covers the suspension travel)
unsprung.cylinder(at(0.55), at(L - 0.06), 0.055, CHROME)
unsprung.cylinder(at(L - 0.10), at(L + 0.02), 0.07, PAINT)          # piston foot
# trailing lever from the foot back to the axle point inboard of the wheel, then the axle stub into the hub
axle_in = np.array([STRUT_POS[0] - 0.02, WHEEL[1], WHEEL[2]])
foot = at(L - 0.02)
lev = axle_in - foot
unsprung.box((foot + axle_in) / 2, (lev, SIDE, np.cross(lev, SIDE)), (np.linalg.norm(lev) / 2 + 0.05, 0.05, 0.045), PAINT)
unsprung.cylinder(axle_in + np.array([0.04, 0, 0]), np.array([WHEEL[0] + 0.06, WHEEL[1], WHEEL[2]]), 0.048, PAINT)
# lower torque link: knee -> piston foot front
lo = at(L - 0.12) + FWD * 0.07
unsprung.box((knee + lo) / 2, (lo - knee, SIDE, np.cross(lo - knee, SIDE)), (np.linalg.norm(lo - knee) / 2 + 0.02, 0.025, 0.018), PAINT)
unsprung.cylinder(knee - SIDE * 0.035, knee + SIDE * 0.035, 0.02, CHROME, n=8)
# brake line continues down the piston to the brake
unsprung.cylinder(at(0.95) - FWD * 0.07, axle_in + np.array([0.02, 0.06, -0.05]), 0.008, DARK, n=6)

parts = [sprung.dump("sprung"), unsprung.dump("unsprung")]
json.dump({"parts": parts, "pivots": {"hinge": HINGE.tolist(), "hinge_x": HINGE_X.tolist(), "strut_pos": STRUT_POS.tolist(),
                                      "strut_y": STRUT_Y.tolist(), "wheel": WHEEL.tolist(), "wheel_axis": [1.0, 0.0, 0.0],
                                      "extension": EXTENSION}},
          open(os.path.join(SRC, "main_gear.json"), "w"))
for p in parts:
    V = np.array(p["vertices"]).reshape(-1, 3)
    print(f"[gear] {p['name']}: {len(p['triangles']) // 3} tris, x {V[:, 0].min():.2f}..{V[:, 0].max():.2f}, y {V[:, 1].min():.2f}..{V[:, 1].max():.2f}, z {V[:, 2].min():.2f}..{V[:, 2].max():.2f}")

# atlas: light grey gear paint | chrome | dark rubber
N = 256
img = Image.new("RGB", (N, N), (184, 188, 184))
d = ImageDraw.Draw(img)
d.rectangle([int(CHROME[0] * N) - 3, 0, int(CHROME[1] * N) + 3, N], fill=(214, 218, 222))
for i in range(int(CHROME[0] * N), int(CHROME[1] * N), 3):
    d.line([i, 0, i, N], fill=(196 + (i * 37) % 30, 200 + (i * 37) % 30, 206 + (i * 37) % 30))
d.rectangle([int(DARK[0] * N) - 3, 0, N, N], fill=(38, 38, 36))
rng = np.random.default_rng(7)
a = np.asarray(img, float) + rng.normal(0, 3, (N, N, 1))
Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.5)).save(os.path.join(SRC, "gear_atlas.png"))
met = Image.new("RGBA", (N, N), (30, 0, 0, 110))
md = ImageDraw.Draw(met)
md.rectangle([int(CHROME[0] * N) - 3, 0, int(CHROME[1] * N) + 3, N], fill=(235, 0, 0, 225))
md.rectangle([int(DARK[0] * N) - 3, 0, N, N], fill=(0, 0, 0, 60))
met.save(os.path.join(SRC, "gear_metallic.png"))
print("[gear] gear_atlas.png written")
