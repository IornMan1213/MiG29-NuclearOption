"""Procedural (from-scratch) models of the R-73, R-27R and their APU-73 / AKU-470 launchers.

Output: MiG29Source/missiles.json (same mesh-dump format as mig29_mesh.json: Unity coords, metres, +Z forward,
Unity front-face winding) and MiG29Source/missile_atlas.png (+ flat normal / metallic textures).
Dimensions from open sources: R-73 2.90 m x 0.17 m, canard span 0.385 m, wing span 0.51 m;
R-27R 4.08 m x 0.23 m, canard span 0.97 m ("butterfly" control fins), wing span 0.80 m;
R-27T 3.80 m x 0.23 m (same airframe, short infrared nose with a glass dome);
R-60M 2.09 m x 0.12 m, wing span 0.39 m, with its APU-60 rail.
Drop tanks: PTB-1500 centreline tank (1,500 L, 4.9 m, slightly flattened to fit between the engine nacelles) and PTB-1150 wing
tank (1,150 L, 4.6 m x 0.64 m), each with a short pylon adapter whose bottom touches the tank top.
"""
import json, math, sys
import numpy as np
from PIL import Image

import os
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.environ["BLUEPRINTER_PROJECT"], "MiG29Source")

# 4x4 colour atlas: cell -> sRGB
CELLS = {
    "white": (232, 232, 226), "lgrey": (190, 192, 190), "dgrey": (62, 64, 66), "glass": (18, 22, 26),
    "olive": (110, 112, 70), "red": (170, 40, 40), "yellow": (200, 168, 48), "metal": (130, 134, 138),
    "brown": (120, 92, 62), "camo": (176, 186, 182), "black": (12, 12, 12), "orange": (210, 120, 40),
}
NAMES = list(CELLS)


def uv(cell):
    i = NAMES.index(cell); cx, cy = i % 4, i // 4
    return ((cx + 0.5) / 4, 1 - (cy + 0.5) / 4)


class Mesh:
    def __init__(self):
        self.v, self.n, self.uv, self.t = [], [], [], []

    def tri(self, a, b, c, cell, normal=None):
        a, b, c = map(np.asarray, (a, b, c))
        fn = np.cross(b - a, c - a)
        if normal is not None and np.dot(fn, normal) < 0:  # Unity front face: cross(b-a, c-a) points outward
            b, c = c, b; fn = -fn
        nn = fn / (np.linalg.norm(fn) + 1e-12) if normal is None else np.asarray(normal) / np.linalg.norm(normal)
        base = len(self.v)
        for p in (a, b, c):
            self.v.append(p.tolist()); self.n.append(nn.tolist()); self.uv.append(list(uv(cell)))
        self.t += [base, base + 1, base + 2]

    def quad(self, a, b, c, d, cell, normal):
        self.tri(a, b, c, cell, normal); self.tri(a, c, d, cell, normal)

    def revolve(self, profile, seg=24, cells=None, z_off=0.0, x_off=0.0, y_off=0.0):
        """profile: [(z, r), ...] nose first. cells(z) -> atlas cell. Smooth normals around the ring."""
        for i in range(len(profile) - 1):
            (z0, r0), (z1, r1) = profile[i], profile[i + 1]
            cell = cells((z0 + z1) / 2) if cells else "white"
            slope = (r0 - r1) / (z0 - z1 + 1e-9)
            for k in range(seg):
                a0, a1 = 2 * math.pi * k / seg, 2 * math.pi * (k + 1) / seg
                P = lambda r, z, a: np.array([x_off + r * math.cos(a), y_off + r * math.sin(a), z_off + z])
                N = lambda a: np.array([math.cos(a), math.sin(a), slope * (1 if z0 > z1 else -1) * 0 + slope])
                p00, p01, p10, p11 = P(r0, z0, a0), P(r0, z0, a1), P(r1, z1, a0), P(r1, z1, a1)
                am = (a0 + a1) / 2
                nrm = np.array([math.cos(am), math.sin(am), (r1 - r0) / (abs(z0 - z1) + 1e-9) * (1 if z0 > z1 else -1)])  # radius growing aft -> normal tilts forward
                if r0 < 1e-6:
                    self.tri(p00, p10, p11, cell, nrm)
                elif r1 < 1e-6:
                    self.tri(p00, p01, p10, cell, nrm)
                else:
                    self.quad(p00, p01, p11, p10, cell, nrm)
        # aft cap
        z, r = profile[-1]
        if r > 1e-6:
            c = np.array([x_off, y_off, z_off + z])
            for k in range(seg):
                a0, a1 = 2 * math.pi * k / seg, 2 * math.pi * (k + 1) / seg
                self.tri(c, np.array([x_off + r * math.cos(a0), y_off + r * math.sin(a0), z_off + z]),
                         np.array([x_off + r * math.cos(a1), y_off + r * math.sin(a1), z_off + z]), "dgrey", np.array([0, 0, -1.0]))

    def fin(self, planform, roll_deg, thickness, cell, z_off=0.0):
        """planform: polygon [(s, z)] with s = distance from the missile axis. Fin plane spans radial dir + z."""
        phi = math.radians(roll_deg)
        er = np.array([math.cos(phi), math.sin(phi), 0.0]); et = np.array([-math.sin(phi), math.cos(phi), 0.0])
        ez = np.array([0, 0, 1.0])
        P = lambda s, z, side: s * er + (z + z_off) * ez + side * thickness / 2 * et
        poly = planform
        for side, nrm in ((1, et), (-1, -et)):
            for i in range(1, len(poly) - 1):
                self.tri(P(*poly[0], side), P(*poly[i], side), P(*poly[i + 1], side), cell, nrm)
        for i in range(len(poly)):
            (s0, z0), (s1, z1) = poly[i], poly[(i + 1) % len(poly)]
            edge = P(s1, z1, 0) - P(s0, z0, 0)
            out = np.cross(edge, et); out = out if np.dot(out, P(s0, z0, 0) - P(*np.mean(poly, axis=0), 0)) > 0 else -out
            self.quad(P(s0, z0, 1), P(s1, z1, 1), P(s1, z1, -1), P(s0, z0, -1), cell, out)

    def box(self, x0, x1, y0, y1, z0, z1, cell, nose_taper=0.0):
        """Axis box; nose_taper > 0 pinches the front (+z) end to a wedge of that length."""
        c = lambda x, y, z: np.array([x, y, z])
        zf = z1
        corners = {}
        for xi, x in ((0, x0), (1, x1)):
            for yi, y in ((0, y0), (1, y1)):
                for zi, z in ((0, z0), (1, z1)):
                    zz = z
                    if zi == 1 and nose_taper > 0 and yi == 0:
                        zz = z1 - nose_taper  # bottom front edge pulled back -> wedge nose
                    corners[(xi, yi, zi)] = c(x, y, zz)
        faces = [((0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0), (0, 0, -1)), ((0, 0, 1), (0, 1, 1), (1, 1, 1), (1, 0, 1), (0, -0.4, 1)),
                 ((0, 0, 0), (0, 1, 0), (0, 1, 1), (0, 0, 1), (-1, 0, 0)), ((1, 0, 0), (1, 0, 1), (1, 1, 1), (1, 1, 0), (1, 0, 0)),
                 ((0, 1, 0), (1, 1, 0), (1, 1, 1), (0, 1, 1), (0, 1, 0)), ((0, 0, 0), (0, 0, 1), (1, 0, 1), (1, 0, 0), (0, -1, 0.3))]
        for a, b, cc, d, n in faces:
            self.quad(corners[a], corners[b], corners[cc], corners[d], cell, np.array(n, float))

    def dump(self, name):
        return dict(name=name, material="missile", vertices=sum(self.v, []), normals=sum(self.n, []), uvs=sum(self.uv, []), triangles=self.t)


def r73():
    m = Mesh(); L = 2.90; R = 0.085; zt = L / 2
    prof = [(zt, 0), (zt - 0.01, 0.03), (zt - 0.03, 0.052), (zt - 0.06, 0.07), (zt - 0.10, 0.081), (zt - 0.15, R),
            (-zt + 0.06, R), (-zt + 0.02, 0.078), (-zt, 0.066)]
    m.revolve(prof, cells=lambda z: "glass" if z > zt - 0.12 else ("yellow" if zt - 0.62 < z < zt - 0.56 else "white"))
    # destabilisers, control canards, tail wings with ailerons (X configuration)
    for roll in (45, 135, 225, 315):
        m.fin([(R, zt - 0.20), (R, zt - 0.30), (R + 0.045, zt - 0.30)], roll, 0.006, "lgrey")
        m.fin([(R, zt - 0.36), (R, zt - 0.62), (0.192, zt - 0.58), (0.192, zt - 0.50)], roll, 0.008, "lgrey")
        m.fin([(R, -zt + 0.56), (R, -zt + 0.06), (0.255, -zt + 0.10), (0.255, -zt + 0.30)], roll, 0.008, "lgrey")
    return m


def r27r():
    m = Mesh(); L = 4.08; R = 0.115; zt = L / 2
    prof = [(zt, 0), (zt - 0.05, 0.028), (zt - 0.14, 0.058), (zt - 0.28, 0.085), (zt - 0.44, 0.103), (zt - 0.60, 0.112), (zt - 0.68, R),
            (-zt + 0.08, R), (-zt + 0.03, 0.104), (-zt, 0.09)]
    m.revolve(prof, cells=lambda z: "lgrey" if z > zt - 0.66 else ("yellow" if zt - 1.00 < z < zt - 0.94 else "white"))
    for roll in (45, 135, 225, 315):
        # "butterfly" control fins: narrow root, wide swept tip
        m.fin([(R, zt - 1.05), (R, zt - 1.30), (0.485, zt - 1.62), (0.485, zt - 1.14)], roll, 0.010, "lgrey")
        # long-chord tail wings
        m.fin([(R, -zt + 1.05), (R, -zt + 0.10), (0.40, -zt + 0.12), (0.40, -zt + 0.42)], roll, 0.010, "lgrey")
    return m


def r27_body(m, L, nose_prof, nose_cells):
    R = 0.115; zt = L / 2
    prof = nose_prof(zt) + [(-zt + 0.08, R), (-zt + 0.03, 0.104), (-zt, 0.09)]
    m.revolve(prof, cells=nose_cells(zt))
    for roll in (45, 135, 225, 315):
        m.fin([(R, zt - 1.05), (R, zt - 1.30), (0.485, zt - 1.62), (0.485, zt - 1.14)], roll, 0.010, "lgrey")
        m.fin([(R, -zt + 1.05), (R, -zt + 0.10), (0.40, -zt + 0.12), (0.40, -zt + 0.42)], roll, 0.010, "lgrey")


def r27t():
    """R-27T: the R-27 airframe with a short infrared seeker section ending in a hemispherical glass dome."""
    m = Mesh(); L = 3.80; R = 0.115
    nose = lambda zt: [(zt, 0), (zt - 0.012, 0.042), (zt - 0.035, 0.066), (zt - 0.07, 0.080), (zt - 0.09, 0.083),  # dome
                       (zt - 0.10, 0.090), (zt - 0.22, 0.104), (zt - 0.36, R)]
    cells = lambda zt: (lambda z: "glass" if z > zt - 0.092 else ("dgrey" if z > zt - 0.36 else ("yellow" if zt - 0.72 < z < zt - 0.66 else "white")))
    r27_body(m, L, nose, cells)
    return m


def r60m():
    m = Mesh(); L = 2.09; R = 0.06; zt = L / 2
    prof = [(zt, 0), (zt - 0.01, 0.025), (zt - 0.03, 0.040), (zt - 0.05, 0.048), (zt - 0.09, 0.055), (zt - 0.14, R),
            (-zt + 0.05, R), (-zt + 0.015, 0.054), (-zt, 0.046)]
    m.revolve(prof, seg=20, cells=lambda z: "glass" if z > zt - 0.06 else ("yellow" if zt - 0.40 < z < zt - 0.36 else "white"))
    for roll in (45, 135, 225, 315):
        m.fin([(R, zt - 0.10), (R, zt - 0.16), (R + 0.03, zt - 0.16)], roll, 0.004, "lgrey")              # destabilisers
        m.fin([(R, zt - 0.20), (R, zt - 0.36), (0.150, zt - 0.33), (0.150, zt - 0.27)], roll, 0.006, "lgrey")  # control canards
        m.fin([(R, -zt + 0.42), (R, -zt + 0.04), (0.195, -zt + 0.06), (0.195, -zt + 0.20)], roll, 0.006, "lgrey")  # wings + rollerons
    return m


def levelled(m, z1, depth, deg):
    """The pylon stations follow their fairings, which slope `deg` nose-down; the missile must stay parallel to the fuselage (user
    reference photo, v0.8.9). In the station frame a level line rises toward the nose, so the beam's bottom and the rail are tilted by
    `deg` about the launcher's nose (z1): slim at the front, deeper at the back, filling the wedge as on the real launchers. The top
    stays flat against the fairing."""
    k = math.tan(math.radians(deg))
    for v in m.v:
        s = (v[2] - z1) * k                          # <= 0: lower toward the back
        if v[1] <= -depth + 0.006: v[1] += s         # rail, shoes, beam bottom
        elif v[1] < 0: v[1] += s * (-v[1] / depth)  # beam sides: blend from the flat top
    return m


def launcher(z0, z1, half_w, depth, nose, tail, rail_depth, rail_half_w, cell="lgrey", seg=6):
    """Streamlined launcher beam as on the real APU-73 / APU-60 / AKU-470 (user reference photo, v0.8.9): flat top against the
    pylon fairing, rounded sides, an ogive nose and a shorter tapered tail, with a metal guide rail and hanger shoes underneath.
    Spans z0..z1 (z1 forward), top at y = 0, beam bottom at -depth, rail bottom at -depth - rail_depth (where the missile touches)."""
    m = Mesh()
    L = z1 - z0
    zs = [z0 + L * k / 40 for k in range(41)]

    def scale(z):
        if z > z1 - nose:   # ogive nose: full size to a blunt point
            t = (z - (z1 - nose)) / nose; return max(0.18, math.cos(t * math.pi / 2) ** 0.7)
        if z < z0 + tail:   # tail taper
            t = ((z0 + tail) - z) / tail; return max(0.35, 1 - 0.65 * t ** 1.5)
        return 1.0

    # cross-section: flat top, sides rounding into the bottom (a half super-ellipse)
    def section(s):
        pts = [(-half_w * s, 0.0)]
        for i in range(seg + 1):
            a = math.pi * i / seg
            x = -math.cos(a) * half_w * s
            y = -(math.sin(a) ** 0.6) * depth * (0.55 + 0.45 * s)
            pts.append((x, y))
        pts.append((half_w * s, 0.0))
        return pts

    rings = [[(x, y, z) for x, y in section(scale(z))] for z in zs]
    for a, b in zip(rings, rings[1:]):
        for i in range(len(a) - 1):
            p0, p1, q1, q0 = a[i], a[i + 1], b[i + 1], b[i]
            mid = np.mean([p0, p1, q0, q1], axis=0)
            outward = np.array([mid[0], mid[1] + depth * 0.4, 0.0])
            m.quad(p0, p1, q1, q0, cell, outward if np.linalg.norm(outward) > 1e-6 else np.array([0, -1.0, 0]))
    for a, b in zip(rings, rings[1:]):                              # flat top (against the fairing; seen at the tapered ends)
        m.quad(a[0], a[-1], b[-1], b[0], cell, np.array([0, 1.0, 0]))
    for ring, nz in ((rings[0], -1.0), (rings[-1], 1.0)):          # end caps
        c = np.mean(ring, axis=0)
        for i in range(len(ring) - 1):
            m.tri(c, ring[i], ring[i + 1], cell, np.array([0, 0, nz]))
    # guide rail and two hanger shoes (the missile hangs from these)
    rz0, rz1 = z0 + tail * 0.6, z1 - nose * 0.9
    m.box(-rail_half_w, rail_half_w, -depth - rail_depth, -depth + 0.005, rz0, rz1, "metal")
    for zc in (rz0 + 0.25 * (rz1 - rz0), rz0 + 0.75 * (rz1 - rz0)):
        m.box(-rail_half_w * 1.6, rail_half_w * 1.6, -depth - rail_depth, -depth - rail_depth * 0.4, zc - 0.05, zc + 0.05, "dgrey")
    return m


def apu60(deg=2.5):
    """APU-60 rail launcher for the R-60M: missile body top touches the rail underside at y = -0.08."""
    return levelled(launcher(-0.95, 0.80, 0.035, 0.08, nose=0.40, tail=0.25, rail_depth=0.012, rail_half_w=0.016), 0.80, 0.08, deg)


def apu73(deg=2.5):
    """APU-73 rail launcher: missile body top touches the rail underside at y = -0.10."""
    return levelled(launcher(-1.25, 1.05, 0.045, 0.10, nose=0.50, tail=0.30, rail_depth=0.015, rail_half_w=0.020), 1.05, 0.10, deg)


def aku470(deg=2.5):
    """AKU-470 ejector launcher for the R-27. 2.1 m long, to fit under the model's inner pylon fairing (flat underside from 1.17 m
    behind to 0.90 m ahead of the hardpoint): at 3.15 m its ends hung in the air past the fairing (user screenshots, v0.8.8)."""
    return levelled(launcher(-1.15, 0.95, 0.06, 0.14, nose=0.50, tail=0.30, rail_depth=0.015, rail_half_w=0.028), 0.95, 0.14, deg)


def tank_profile(L, R, nose, tail, tail_r, n=10):
    zt = L / 2; prof = []
    for i in range(n + 1):                      # elliptical nose
        t = i / n; prof.append((zt - nose * (1 - math.cos(t * math.pi / 2)), R * math.sin(t * math.pi / 2) if i else 0.0))
    prof[0] = (zt, 0.0)
    for i in range(1, n + 1):                   # tapering tail cone, slightly convex
        t = i / n; prof.append((-zt + tail * (1 - t), tail_r + (R - tail_r) * (1 - t ** 1.6)))
    prof.insert(n + 1, (-zt + tail, R))
    return sorted(set(prof), key=lambda p: -p[0])


def squash(m, ky):
    """Flatten a mesh vertically by ky (normals corrected)."""
    for v in m.v: v[1] *= ky
    for nr in m.n:
        nr[1] /= ky; l = math.sqrt(sum(c * c for c in nr)) or 1.0
        nr[:] = [c / l for c in nr]


def ptb1500():
    """PTB-1500 centreline tank: 4.9 m long, 0.80 m wide, 0.68 m deep (about 1.5 m3)."""
    m = Mesh(); L = 4.9; R = 0.40
    m.revolve(tank_profile(L, R, nose=1.35, tail=1.25, tail_r=0.07), seg=28,
              cells=lambda z: "dgrey" if z > L / 2 - 0.05 else ("red" if -0.62 < z < -0.56 else "camo"))
    squash(m, 0.85)
    return m


def ptb1150():
    """PTB-1150 wing tank: 4.6 m x 0.64 m (about 1.15 m3)."""
    m = Mesh(); L = 4.6; R = 0.32
    m.revolve(tank_profile(L, R, nose=1.2, tail=1.0, tail_r=0.05), seg=24,
              cells=lambda z: "dgrey" if z > L / 2 - 0.04 else ("red" if -0.52 < z < -0.47 else "camo"))
    return m


def ptb_pylon(depth, length, width):
    """Pylon adapter for a drop tank: a streamlined blade from y = 0 (hardpoint) down to y = -depth (tank top)."""
    m = Mesh()
    m.box(-width / 2, width / 2, -depth, 0.0, -length / 2, length / 2, "camo", nose_taper=min(0.3, length / 4))
    m.box(-width / 2 - 0.01, width / 2 + 0.01, -depth - 0.012, -depth + 0.02, -length / 2 + 0.15, length / 2 - 0.3, "metal")   # sway braces / lugs
    return m


def textures():
    a = Image.new("RGB", (64, 64))
    for i, name in enumerate(NAMES):
        cx, cy = i % 4, i // 4
        a.paste(CELLS[name], (cx * 16, cy * 16, cx * 16 + 16, cy * 16 + 16))
    a.save(OUT + "/missile_atlas.png")
    Image.new("RGB", (8, 8), (128, 128, 255)).save(OUT + "/flat_normal.png")
    met = Image.new("RGBA", (64, 64), (0, 0, 0, 90))
    for name in ("glass", "metal"):
        i = NAMES.index(name); cx, cy = i % 4, i // 4
        met.paste((40 if name == "glass" else 200, 0, 0, 220 if name == "glass" else 150), (cx * 16, cy * 16, cx * 16 + 16, cy * 16 + 16))
    met.save(OUT + "/missile_metallic.png")


if __name__ == "__main__":
    textures()
    parts = [r73().dump("R73"), r27r().dump("R27R"), r27t().dump("R27T"), r60m().dump("R60M"),
             apu73().dump("APU73"), aku470().dump("AKU470"), apu60().dump("APU60"),
             apu73(5.0).dump("APU73_O"), apu60(5.0).dump("APU60_O"),   # outer pylons: their fairing slopes 5 deg
             ptb1500().dump("PTB1500"), ptb1150().dump("PTB1150"),
             ptb_pylon(0.08, 1.9, 0.12).dump("PTB_PYLON_C"), ptb_pylon(0.10, 1.6, 0.10).dump("PTB_PYLON_W")]
    json.dump({"parts": parts}, open(OUT + "/missiles.json", "w"))
    for p in parts:
        v = np.array(p["vertices"]).reshape(-1, 3)
        print(p["name"], len(p["triangles"]) // 3, "tris, bbox", v.min(0).round(3), v.max(0).round(3))
