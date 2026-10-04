"""From-scratch RD-33 exhaust nozzles for the MiG-29 (replace the model's low-poly nozzle cans).

Output: MiG29Source/nozzles.json and MiG29Source/nozzle_atlas.png.
Geometry is in the nozzle's local frame (Unity axes): origin on the engine axis at the nacelle seam, +z forward (toward the nose),
+y up, +x right. The engine axis toes out and down about 2 deg on the MiG (blender/analysis: fitted duct centres); Unity places
the frame (MiG29Nozzles.cs). Moving parts are single petals in their own hinge frame, so the instruments plugin can open and close
the nozzle with the engine like the real convergent-divergent iris:
  outer flap / seal : hinge at the seam ring (radius R_ROOT), petal runs aft (local -z) along the shroud, outer surface at y = 0
  inner petal       : hinge at the throat ring (radius R_THROAT, z = -Z_THROAT), runs aft to just inside the outer flaps' tips
Static: inner duct (seam -> throat), throat ring, root band, afterburner flame-holder rings with radial gutters, turbine cone.
"""
import json, math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.environ["BLUEPRINTER_PROJECT"], "MiG29Source")

# MiG frame, measured on the model (blender/analysis/nozzle_look.py and the ray-cast fits in DEVLOG)
AXIS_ROOT = (0.869, -0.265, -2.80)                 # right engine; the left mirrors x
AXIS_END = (0.900, -0.295, -3.72)
N_FLAPS = 16
R_ROOT = 0.490          # shroud radius at the seam (nacelle outer ~0.47-0.49)
L_FLAP = 0.935          # seam -> exit lip along the axis
R_DUCT = 0.386
Z_THROAT, R_THROAT = 0.52, 0.352
L_INNER = 0.42
THICK = 0.008
EXIT_REST = 0.435       # exit radius at rest (engine off / as built); the plugin moves it 0.405 (military) .. 0.47 (afterburner)

W = 512
atlas = Image.new("RGB", (W, W), (30, 30, 30))
CELLS = {}


def cell(name, x, y, w, h, img):
    atlas.paste(img, (x, y)); CELLS[name] = (x / W, 1 - (y + h) / W, (x + w) / W, 1 - y / W)


def gradient_img(w, h, stops, streaks=0.0, seed=1, rivets=False):
    """Vertical gradient (top = petal root, bottom = exit) with heat streaks along the length."""
    rnd = np.random.default_rng(seed)
    a = np.zeros((h, w, 3))
    ts = np.linspace(0, 1, h)
    for k in range(3):
        a[:, :, k] = np.interp(ts, [s[0] for s in stops], [s[1][k] for s in stops])[:, None]
    if streaks:
        st = rnd.normal(0, 1, w); st = np.convolve(st, np.ones(5) / 5, mode="same")
        a *= (1 + streaks * st)[None, :, None]
    a += rnd.normal(0, 3, a.shape)
    img = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))
    if rivets:
        d = ImageDraw.Draw(img)
        for y in (6, 14):
            for x in range(4, w, 8):
                d.ellipse((x - 1.2, y - 1.2, x + 1.2, y + 1.2), fill=(150, 150, 152))
        d.line((0, 20, w, 20), fill=(70, 70, 72), width=1)
    return img


# outer flap: steel at the seam -> straw/bronze heat tint -> blue-violet -> dark at the hot lip
# (tints kept subtle: the real RD-33 petals read grey-brown with faint straw and blue bands, not rainbow)
cell("outer", 0, 0, 128, 256, gradient_img(128, 256, [(0, (146, 146, 148)), (0.25, (136, 133, 128)), (0.5, (132, 120, 102)),
                                                      (0.72, (100, 98, 108)), (0.9, (72, 72, 78)), (1, (50, 48, 50))], streaks=0.06, seed=3, rivets=True))
cell("seal", 128, 0, 128, 256, gradient_img(128, 256, [(0, (118, 118, 120)), (0.4, (112, 104, 92)), (0.75, (84, 82, 90)), (1, (42, 40, 42))],
                                            streaks=0.05, seed=5))
# inner faces and petals: soot over heat-blued steel
cell("inner", 256, 0, 128, 256, gradient_img(128, 256, [(0, (46, 42, 40)), (0.5, (66, 58, 52)), (1, (84, 76, 70))], streaks=0.08, seed=7))
cell("duct", 384, 0, 128, 256, gradient_img(128, 256, [(0, (22, 21, 20)), (0.6, (34, 31, 29)), (1, (52, 47, 43))], streaks=0.05, seed=9))
cell("ring", 0, 256, 128, 128, gradient_img(128, 128, [(0, (78, 76, 74)), (1, (60, 58, 56))], seed=11))
cell("holder", 128, 256, 128, 128, gradient_img(128, 128, [(0, (58, 50, 44)), (1, (36, 32, 30))], seed=13))
cell("cone", 256, 256, 128, 128, gradient_img(128, 128, [(0, (40, 38, 38)), (1, (18, 18, 18))], seed=15))


class Mesh:
    def __init__(self):
        self.v, self.n, self.uv, self.t = [], [], [], []

    def quad(self, p, uvs, normal):
        p = [np.asarray(x, float) for x in p]
        fn = np.cross(p[1] - p[0], p[2] - p[0])
        if np.dot(fn, normal) < 0:          # Unity front face: cross(b-a, c-a) points outward (as tools/missile_gen.py)
            p = [p[0], p[3], p[2], p[1]]; uvs = [uvs[0], uvs[3], uvs[2], uvs[1]]
        nn = np.asarray(normal, float); nn = nn / (np.linalg.norm(nn) + 1e-12)
        b = len(self.v)
        for q, u in zip(p, uvs):
            self.v.append(q.tolist()); self.n.append(nn.tolist()); self.uv.append(list(u))
        self.t += [b, b + 1, b + 2, b, b + 2, b + 3]

    def dump(self, name, **extra):
        d = dict(name=name, vertices=sum(self.v, []), normals=sum(self.n, []), uvs=sum(self.uv, []), triangles=self.t)
        d.update(extra)
        return d


def uvmap(c, s, t):
    u0, v0, u1, v1 = CELLS[c]
    return (u0 + s * (u1 - u0), v1 - t * (v1 - v0))


def petal(length, w_root, w_tip, curve_r, outer_cell, inner_cell, lip=0.012):
    """Curved plate in its hinge frame: x across, y = 0 outer surface (normal +y), runs to z = -length; thickness THICK inward."""
    m = Mesh()
    nx, nz = 3, 5
    def P(i, k, inside):
        t = k / nz; w = w_root + (w_tip - w_root) * t
        x = (i / nx - 0.5) * w
        y = -(x * x) / (2 * curve_r) - (THICK if inside else 0.0)
        return (x, y, -length * t)
    for k in range(nz):
        for i in range(nx):
            q = [P(i, k, False), P(i + 1, k, False), P(i + 1, k + 1, False), P(i, k + 1, False)]
            uv = [uvmap(outer_cell, i / nx, k / nz), uvmap(outer_cell, (i + 1) / nx, k / nz), uvmap(outer_cell, (i + 1) / nx, (k + 1) / nz), uvmap(outer_cell, i / nx, (k + 1) / nz)]
            xc = (q[0][0] + q[1][0]) / 2
            m.quad(q, uv, (xc / curve_r, 1.0, 0.0))
            qi = [P(i, k, True), P(i + 1, k, True), P(i + 1, k + 1, True), P(i, k + 1, True)]
            uvi = [uvmap(inner_cell, i / nx, k / nz), uvmap(inner_cell, (i + 1) / nx, k / nz), uvmap(inner_cell, (i + 1) / nx, (k + 1) / nz), uvmap(inner_cell, i / nx, (k + 1) / nz)]
            m.quad(qi, uvi, (-xc / curve_r, -1.0, 0.0))
    for k in range(nz):                                    # side edges
        for i, sx in ((0, -1), (nx, 1)):
            q = [P(i, k, False), P(i, k + 1, False), P(i, k + 1, True), P(i, k, True)]
            m.quad(q, [uvmap(inner_cell, 0, 0)] * 4, (sx, 0, 0))
    for i in range(nx):                                    # thickened exit lip (darker band)
        a, b = P(i, nz, False), P(i + 1, nz, False)
        ai, bi = P(i, nz, True), P(i + 1, nz, True)
        m.quad([a, b, bi, ai], [uvmap(inner_cell, 0.5, 1)] * 4, (0, 0, -1))
    return m


def revolve(m, prof, cellname, inward, seg=32, vrange=(0.0, 1.0)):
    """Surface of revolution about the local z axis. prof: [(z, r)] in order; inward=True -> normals toward the axis."""
    for j in range(len(prof) - 1):
        (z0, r0), (z1, r1) = prof[j], prof[j + 1]
        t0 = vrange[0] + (vrange[1] - vrange[0]) * j / (len(prof) - 1)
        t1 = vrange[0] + (vrange[1] - vrange[0]) * (j + 1) / (len(prof) - 1)
        for k in range(seg):
            a0, a1 = 2 * math.pi * k / seg, 2 * math.pi * (k + 1) / seg
            q = [(r0 * math.cos(a0), r0 * math.sin(a0), z0), (r0 * math.cos(a1), r0 * math.sin(a1), z0),
                 (r1 * math.cos(a1), r1 * math.sin(a1), z1), (r1 * math.cos(a0), r1 * math.sin(a0), z0 + (z1 - z0))]
            am = (a0 + a1) / 2
            nrm = np.array([math.cos(am), math.sin(am), (r0 - r1) / (abs(z1 - z0) + 1e-9) * (1 if z1 < z0 else -1) * 0.0])
            m.quad(q, [uvmap(cellname, k / seg, t0), uvmap(cellname, (k + 1) / seg, t0), uvmap(cellname, (k + 1) / seg, t1), uvmap(cellname, k / seg, t1)],
                   -nrm if inward else nrm)


def disk(m, z, r_in, r_out, cellname, normal_z=-1.0, seg=32):
    for k in range(seg):
        a0, a1 = 2 * math.pi * k / seg, 2 * math.pi * (k + 1) / seg
        q = [(r_in * math.cos(a0), r_in * math.sin(a0), z), (r_out * math.cos(a0), r_out * math.sin(a0), z),
             (r_out * math.cos(a1), r_out * math.sin(a1), z), (r_in * math.cos(a1), r_in * math.sin(a1), z)]
        m.quad(q, [uvmap(cellname, 0.5, 0.5)] * 4, (0, 0, normal_z))


def torus_ring(m, z, r, rt, cellname, seg=32, segt=5):
    for k in range(seg):
        for j in range(segt):
            def p(kk, jj):
                a = 2 * math.pi * kk / seg; b = 2 * math.pi * jj / segt
                rr = r + rt * math.cos(b)
                return (rr * math.cos(a), rr * math.sin(a), z + rt * math.sin(b))
            q = [p(k, j), p(k + 1, j), p(k + 1, j + 1), p(k, j + 1)]
            a = 2 * math.pi * (k + 0.5) / seg; b = 2 * math.pi * (j + 0.5) / segt
            nrm = (math.cos(b) * math.cos(a), math.cos(b) * math.sin(a), math.sin(b))
            m.quad(q, [uvmap(cellname, 0.5, 0.5)] * 4, nrm)


def static_parts():
    m = Mesh()
    # inner duct seam -> throat (seen through the exit), sooty, faces the axis
    revolve(m, [(0.06, R_DUCT), (-0.15, R_DUCT - 0.004), (-0.35, 0.370), (-Z_THROAT, R_THROAT)], "duct", inward=True)
    # duct continues forward into the engine, closing on the turbine face
    revolve(m, [(0.70, 0.37), (0.06, R_DUCT)], "duct", inward=True, vrange=(0.0, 0.3))
    # throat ring the inner petals hinge on
    torus_ring(m, -Z_THROAT, R_THROAT + 0.004, 0.009, "ring")
    # root band: closes the gap between the nacelle skin and the flap hinges, outer surface flush with the shroud
    revolve(m, [(0.05, R_ROOT + 0.006), (0.0, R_ROOT + 0.006), (-0.02, R_ROOT + 0.002)], "ring", inward=False)
    disk(m, 0.0, R_DUCT + 0.004, R_ROOT + 0.006, "ring")
    # afterburner flame holders: two concentric V-gutter rings with radial gutters, inside the duct ahead of the seam
    zf = 0.28
    torus_ring(m, zf, 0.27, 0.014, "holder", seg=32)
    torus_ring(m, zf + 0.04, 0.15, 0.012, "holder", seg=24)
    for k in range(8):                                      # radial gutters
        a = 2 * math.pi * (k + 0.5) / 8
        ca, sa = math.cos(a), math.sin(a)
        tx, ty = -sa * 0.012, ca * 0.012
        for z0, z1 in ((zf - 0.012, zf + 0.012),):
            p0 = (0.15 * ca, 0.15 * sa); p1 = (0.36 * ca, 0.36 * sa)
            m.quad([(p0[0] - tx, p0[1] - ty, zf), (p1[0] - tx, p1[1] - ty, zf), (p1[0] + tx, p1[1] + ty, zf), (p0[0] + tx, p0[1] + ty, zf)],
                   [uvmap("holder", 0.5, 0.5)] * 4, (0, 0, -1))
    # turbine exit cone (tail cone), pointing aft
    revolve(m, [(0.70, 0.17), (0.58, 0.16), (0.46, 0.10), (0.38, 0.02)], "cone", inward=False)
    disk(m, 0.70, 0.17, 0.37, "cone")                                     # turbine face annulus (dark), faces aft
    return m


def main():
    os.makedirs(OUT, exist_ok=True)
    atlas.save(os.path.join(OUT, "nozzle_atlas.png"))
    # metallic (R) / smoothness (A): bare hot-section metal on the petals, sooty matte inside
    met = Image.new("RGBA", (W, W), (60, 0, 0, 40))
    for name, (mv, sv) in {"outer": (190, 120), "seal": (180, 100), "ring": (170, 90), "inner": (90, 50), "holder": (120, 50)}.items():
        u0, v0, u1, v1 = CELLS[name]
        met.paste((mv, 0, 0, sv), (int(u0 * W), int((1 - v1) * W), int(u1 * W), int((1 - v0) * W)))
    met.save(os.path.join(OUT, "nozzle_metallic.png"))
    Image.new("RGB", (8, 8), (128, 128, 255)).save(os.path.join(OUT, "flat_normal.png"))
    slot_root = 2 * math.pi * R_ROOT / N_FLAPS
    flap = petal(L_FLAP, slot_root * 0.80, 2 * math.pi * 0.47 / N_FLAPS * 0.66, 0.46, "outer", "inner")
    seal = petal(L_FLAP - 0.01, slot_root * 0.42, 2 * math.pi * 0.47 / N_FLAPS * 0.40, 0.46, "seal", "inner")
    slot_throat = 2 * math.pi * R_THROAT / N_FLAPS
    inner = petal(L_INNER, slot_throat * 1.10, 2 * math.pi * 0.46 / N_FLAPS * 1.04, 0.40, "inner", "inner")
    parts = [flap.dump("flap"), seal.dump("seal"), inner.dump("inner"), static_parts().dump("static")]
    layout = dict(axis_root=AXIS_ROOT, axis_end=AXIS_END, count=N_FLAPS, r_root=R_ROOT, l_flap=L_FLAP, seal_inset=0.006,
                  z_throat=Z_THROAT, r_throat=R_THROAT, l_inner=L_INNER, exit_rest=EXIT_REST)
    json.dump(dict(parts=parts, layout=layout), open(os.path.join(OUT, "nozzles.json"), "w"))
    for p in parts:
        v = np.array(p["vertices"]).reshape(-1, 3)
        print(p["name"], len(p["triangles"]) // 3, "tris, bbox", v.min(0).round(3), v.max(0).round(3))


if __name__ == "__main__":
    main()
