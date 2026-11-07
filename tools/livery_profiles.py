"""Five liveries after MiG-29 profile drawings, and four team liveries, designed on the airframe like livery_display.py.

python tools/livery_profiles.py <mig29_basecolor.png> <mig29_mesh.json> <out dir> [--preview <dir>] [--only key,key]

Writes mig29_basecolor_<key>.png for
  german   "29+20": yellow nose, red sweep, black rear and fins, yellow four-point star and flag on the fins
  digital741 "741": white with blocky red and blue digital patches
  ovt      "917": three-tone light blue splinter, dark radome, white/blue/red lightning flash on the fins
  white44  "44": white, blue fins and spine, black anti-glare panel, blue cheat line, red star on the fins
  lii      "84": light grey over dark blue, blue fins with a white sweep, flag on the fins
and four team schemes, offered only to their own faction (MiG29Polish.SetLiveries):
  bdf_coastal  "07": two blue-greys, pale belly, blue/white/green fin sweep, BDF shield
  bdf_woodland "22": green, dark green and earth blotches, grey-blue belly, green fin tips, BDF shield
  pala_desert  "315": sand, ochre and rust splinter, red fin tips, PALA roundel
  pala_night   "513": charcoal, red spine and cheat line, black fins with a red sweep, PALA roundel

Every exterior texel is rasterised once with its MiG-frame position and normal (control surfaces put back at their hinges); the
colour rules work on those. Only paint texels change (low saturation, or the base texture's red stars and numbers), with panel
lines kept by shading relative to their own camouflage tone (livery_shade.py). Numbers and insignia are drawn in side view per face direction, so they read
the right way on both sides, and skipped on texels the left and right sides share.
"""
import json, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
from livery_shade import detail_shade
import fonts

SRC, MESH, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
PREVIEW = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None
N = 2048
d = json.load(open(MESH))
FONT = fonts.find("arialbd.ttf", "LiberationSans-Bold.ttf")


def C(*rgb): return np.array(rgb, np.float32) / 255.0


# ---------------- rasterise every exterior texel: position, normal, side conflicts ----------------

def rasterise():
    pos = np.zeros((N, N, 3), np.float32); nrm = np.zeros((N, N, 3), np.float32)
    covered = np.zeros((N, N), bool); sides = np.zeros((N, N), np.int8)   # bit 1: left (x<-0.3), bit 2: right (x>0.3)
    for part in d["parts"]:
        if part["name"] in ("canopy", "cockpit"):
            continue
        v = np.array(part["vertices"], np.float32).reshape(-1, 3)
        if part.get("pivot"):
            v = v + np.array(part["pivot"], np.float32)      # control surfaces are stored relative to their hinge
        nr = np.array(part["normals"], np.float32).reshape(-1, 3)
        uv = np.array(part["uvs"], np.float32).reshape(-1, 2) % 1.0
        tris = np.array(part["triangles"]).reshape(-1, 3)
        px = np.stack([uv[:, 0] * N, (1 - uv[:, 1]) * N], 1)
        for a, b, c in tris:
            P = px[[a, b, c]]
            x0, y0 = np.floor(P.min(0)).astype(int); x1, y1 = np.ceil(P.max(0)).astype(int)
            x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, N - 1), min(y1, N - 1)
            if x1 < x0 or y1 < y0:
                continue
            gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
            (ax, ay), (bx, by), (cx, cy) = P
            den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
            if abs(den) < 1e-9:
                continue
            w0 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / den
            w1 = ((cy - ay) * (gx - cx) + (ax - cx) * (gy - cy)) / den
            w2 = 1 - w0 - w1
            inside = (w0 >= -0.02) & (w1 >= -0.02) & (w2 >= -0.02)
            if not inside.any():
                continue
            W = np.stack([w0[inside], w1[inside], w2[inside]], 1)
            iy, ix = np.nonzero(inside); iy += y0; ix += x0
            pos[iy, ix] = W @ v[[a, b, c]]
            nn = W @ nr[[a, b, c]]; nrm[iy, ix] = nn / (np.linalg.norm(nn, axis=1, keepdims=True) + 1e-9)
            covered[iy, ix] = True
            cxm = v[[a, b, c], 0].mean()
            if cxm < -0.3: sides[iy, ix] |= 1
            elif cxm > 0.3: sides[iy, ix] |= 2
    return pos, nrm, covered, sides == 3


cache = os.path.join(OUT, "livery_raster_cache.npz")
key = os.path.getmtime(MESH)
if os.path.exists(cache) and float(np.load(cache)["key"]) == key:
    z_ = np.load(cache); pos, nrm, covered, shared = z_["pos"], z_["nrm"], z_["covered"], z_["shared"]
else:
    pos, nrm, covered, shared = rasterise()
    np.savez_compressed(cache, pos=pos, nrm=nrm, covered=covered, shared=shared, key=key)
print("texels:", covered.sum(), "shared left/right:", shared.sum())

idx = np.nonzero(covered)
P = pos[idx]; Nn = nrm[idx]; SH = shared[idx]
X, Y, Z = P[:, 0], P[:, 1], P[:, 2]
AX = np.abs(X); NX, NY = Nn[:, 0], Nn[:, 1]

# surface classes
FIN = (np.abs(NX) > 0.7) & (Y > 0.75) & (Z < 0.3) & (AX > 0.8)
SIDE = (np.abs(NX) > 0.55) & ~FIN
TOP = NY > 0.3
RADOME = Z > 10.1


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


# ---------------- markings in side view ----------------

class Marking:
    """A picture (RGBA, drawn by draw(ImageDraw, w, h)) centred at MiG (z, y) with height h metres, on faces matching where."""
    RES = 300   # px per metre

    def __init__(self, zc, yc, w, h, draw, where):
        self.zc, self.yc, self.w, self.h, self.where = zc, yc, w, h, where
        W, H = int(w * self.RES), int(h * self.RES)
        im = Image.new("RGBA", (W, H), (0, 0, 0, 0)); draw(ImageDraw.Draw(im), W, H)
        self.img = np.asarray(im, np.float32) / 255

    def apply(self, col):
        s = np.sign(NX)                                     # face seen from -x: nose to the left, so screen right = -z
        hor = np.where(s < 0, -(Z - self.zc), (Z - self.zc))
        u = (hor + self.w / 2) * self.RES; v = (self.yc + self.h / 2 - Y) * self.RES
        H, W = self.img.shape[:2]
        ok = self.where & ~SH & (u >= 0) & (u < W) & (v >= 0) & (v < H)
        px = self.img[v[ok].astype(int), u[ok].astype(int)]
        a = px[:, 3:4]
        col[ok] = col[ok] * (1 - a) + px[:, :3] * a
        return col


def text(t, rgb, outline=None):
    def draw(dr, W, H):
        f = ImageFont.truetype(FONT, int(H * 0.95))
        bb = dr.textbbox((0, 0), t, font=f)
        xy = ((W - (bb[2] - bb[0])) / 2 - bb[0], (H - (bb[3] - bb[1])) / 2 - bb[1])
        kw = dict(stroke_width=max(2, H // 18), stroke_fill=tuple(int(c * 255) for c in outline) + (255,)) if outline is not None else {}
        dr.text(xy, t, font=f, fill=tuple(int(c * 255) for c in rgb) + (255,), **kw)
    return draw


def star(rgb, points=5, inner=0.4, outline=None):
    def draw(dr, W, H):
        cx, cy, r = W / 2, H / 2, min(W, H) / 2 * 0.95
        pts = [(cx + (r if i % 2 == 0 else r * inner) * np.sin(np.pi * i / points), cy - (r if i % 2 == 0 else r * inner) * np.cos(np.pi * i / points))
               for i in range(2 * points)]
        dr.polygon(pts, fill=tuple(int(c * 255) for c in rgb) + (255,),
                   outline=tuple(int(c * 255) for c in outline) + (255,) if outline is not None else None, width=max(2, int(r / 10)))
    return draw


def flag(*bands):
    def draw(dr, W, H):
        for i, c in enumerate(bands):
            dr.rectangle([0, H * i / len(bands), W, H * (i + 1) / len(bands)], fill=tuple(int(x * 255) for x in c) + (255,))
    return draw


# fin positions (both fins, both faces): leading edge z = -1.24 - 1.16 (y - 1), trailing edge z -3.6..-3.85 (measured)
FIN_LE = lambda y: -1.24 - 1.16 * (y - 1.0)
FIN_NUM = (-2.75, 1.45); FIN_MARK = (-3.2, 2.15); FIN_FLAG = (-3.42, 2.55)
RU = (C(255, 255, 255), C(0, 57, 166), C(213, 43, 30)); DE = (C(20, 20, 20), C(221, 0, 0), C(255, 206, 0))


# ---------------- noise helpers ----------------

def hash3(i, j, k, seed):
    return np.modf(np.sin(i * 12.9898 + j * 78.233 + k * 37.719 + seed * 4.1414) * 43758.5453)[0] % 1.0


def value_noise(p, cell, seed):
    q = p / cell; i0 = np.floor(q); f = q - i0; f = f * f * (3 - 2 * f)
    out = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                out = out + w * hash3(i0[:, 0] + dx, i0[:, 1] + dy, i0[:, 2] + dz, seed)
    return out


SYM = np.stack([AX, Y, Z], 1)   # mirror-symmetric patterns: the same on both sides (and on shared texels)


# ---------------- the five schemes ----------------

def german():
    YEL, RED, BLK, GRY = C(250, 205, 20), C(215, 25, 20), C(22, 22, 24), C(140, 142, 145)
    # side-view diagonals: yellow ahead of z 4.8 + 1.25 (y + 0.4); black behind z 3.0 - 3.5 (y + 0.4); red between
    yel = Z > 4.8 + 1.25 * (Y + 0.4)
    blk = Z < 3.0 - 3.5 * (Y + 0.4)
    col = np.where(yel[:, None], YEL, np.where(blk[:, None], BLK, RED))
    col[FIN] = BLK
    col[RADOME] = GRY
    marks = [Marking(4.05, -0.02, 1.25, 0.28, text("29+20", C(205, 208, 212)), SIDE),
             Marking(*FIN_MARK, 0.75, 0.75, star(YEL, points=4, inner=0.28), FIN),
             Marking(*FIN_FLAG, 0.36, 0.22, flag(*DE), FIN)]
    return col, marks


def digital():
    WHT, RED, BLU = C(240, 241, 243), C(205, 20, 25), C(25, 60, 190)
    q = np.floor(SYM / 0.16) * 0.16 + 0.08               # 16 cm pixels
    nb = 0.65 * value_noise(q, 1.1, 1) + 0.35 * value_noise(q, 0.45, 2)
    nr = 0.65 * value_noise(q, 1.1, 3) + 0.35 * value_noise(q, 0.45, 4)
    blue = nb + 0.08 * np.clip(-Z + 2, -2, 6) + 0.20 * np.clip(Y, -0.5, 2.5) > 0.80
    red = nr + 0.05 * np.clip(Z - 1, -3, 5) - 0.12 * np.clip(Y, 0, 2.5) > 0.80
    col = np.where(blue[:, None], BLU, np.where(red[:, None], RED, WHT))
    col[RADOME] = WHT
    marks = [Marking(4.6, 0.62, 0.62, 0.24, text("741", RED, outline=WHT), SIDE & (Y > 0.3))]
    return col, marks


def ovt():
    L1, L2, L3, RAD = C(196, 218, 236), C(140, 172, 212), C(96, 128, 178), C(88, 80, 74)
    rng = np.random.default_rng(917)
    seeds = np.stack([rng.uniform(0, 5.6, 140), rng.uniform(-1.0, 2.8, 140), rng.uniform(-5.4, 12, 140)], 1)
    tone = rng.integers(0, 3, len(seeds))
    best = np.full(len(P), np.inf); pick = np.zeros(len(P), int)
    for k, s in enumerate(seeds):
        dd = ((SYM - s) * np.array([0.8, 1.6, 0.7])) ** 2
        dd = dd.sum(1)
        m = dd < best; best[m] = dd[m]; pick[m] = tone[k]
    col = np.stack([L1, L2, L3])[pick]
    col[RADOME] = RAD
    # lightning flash across the upper fins: white / blue / red zigzag bands rising aft
    w = Y + 0.8 * Z + 0.10 * np.abs(((Z * 2.2) % 2) - 1)
    for lo, hi, c in ((-1.27, -1.15, RU[0]), (-1.15, -1.03, RU[1]), (-1.03, -0.87, RU[2])):
        col[FIN & (w > lo) & (w < hi)] = c
    marks = [Marking(3.95, -0.02, 0.95, 0.36, text("917", L3, outline=L1), SIDE),
             Marking(*FIN_FLAG, 0.36, 0.22, flag(*RU), FIN)]
    return col, marks


def white44():
    WHT, BLU, LBL, BLK, GRY = C(242, 243, 245), C(30, 110, 215), C(110, 170, 235), C(25, 25, 28), C(140, 142, 145)
    col = np.tile(WHT, (len(P), 1))
    col[TOP & (Z < 5.6) & (Z > -4.0) & (AX < 1.0)] = BLU          # blue spine behind the canopy
    col[TOP & (Z > 7.9) & (Z < 10.1) & (AX < 0.45)] = BLK          # anti-glare panel ahead of the windscreen
    yc = 0.02 + 0.045 * (10.0 - Z)                                 # cheat line rising from the nose to the fin root
    line = SIDE & (Z < 10.0) & (Z > -1.0)
    col[line & (np.abs(Y - yc) < 0.035)] = BLU
    col[line & (Y - yc > 0.035) & (Y - yc < 0.06)] = LBL
    col[FIN] = BLU
    col[FIN & (Y > 2.55)] = GRY
    col[RADOME] = GRY
    marks = [Marking(*FIN_NUM, 0.8, 0.5, text("44", WHT), FIN),
             Marking(*FIN_MARK, 0.42, 0.42, star(C(220, 25, 30), outline=WHT), FIN)]
    return col, marks


def lii():
    GRY, BLU, WHT = C(190, 194, 198), C(25, 70, 160), C(244, 245, 247)
    lower = (Y < -0.05 + 0.03 * np.clip(Z - 4, 0, 5)) & (Z < 9.0) & ~TOP
    col = np.where(lower[:, None], BLU, GRY)
    # fins: blue aft of a white sweep curving up and back from the leading edge root
    curve = FIN_LE(Y) - 0.9 + 0.35 * (Y - 1.0)
    col[FIN & (Z < curve)] = BLU
    col[FIN & (Z >= curve) & (Z < curve + 0.12)] = WHT
    marks = [Marking(-3.0, 1.55, 0.6, 0.36, text("84", WHT), FIN),
             Marking(*FIN_FLAG, 0.36, 0.22, flag(*RU), FIN)]
    return col, marks


# ---------------- team schemes: shown only to their own faction (MiG29Polish.SetLiveries) ----------------

def bdf_shield(dr, W, H):
    """The Boscali Defence Force's colours on a shield: blue over green, a white chevron, white rim."""
    rim = max(3, W // 14)
    sh = [(0.06 * W, 0.04 * H), (0.94 * W, 0.04 * H), (0.94 * W, 0.52 * H), (0.5 * W, 0.97 * H), (0.06 * W, 0.52 * H)]
    dr.polygon(sh, fill=(246, 247, 249, 255))
    inset = [(x + (rim if x < W / 2 else -rim if x > W / 2 else 0), y + (rim if y < H / 2 else -rim * 1.4)) for x, y in sh]
    dr.polygon(inset, fill=(28, 92, 178, 255))
    dr.polygon([(inset[4][0], 0.56 * H), (inset[3][0], inset[3][1]), (inset[2][0], 0.56 * H), (inset[2][0], 0.40 * H),
                (0.5 * W, 0.80 * H), (inset[4][0], 0.40 * H)], fill=(38, 140, 72, 255))
    dr.line([(inset[4][0], 0.40 * H), (0.5 * W, 0.70 * H), (inset[2][0], 0.40 * H)], fill=(246, 247, 249, 255), width=rim)


def pala_roundel(dr, W, H):
    """PALA's colours: a red disc in a black ring with six white triangles stacked point-down."""
    r = min(W, H) / 2 * 0.96; cx, cy = W / 2, H / 2
    dr.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(20, 20, 22, 255))
    r2 = r * 0.84
    dr.ellipse([cx - r2, cy - r2, cx + r2, cy + r2], fill=(200, 28, 30, 255))
    s = r2 * 0.36; h = s * 0.87
    for row, n in enumerate((3, 2, 1)):
        y0 = cy - 1.25 * h + row * h
        for i in range(n):
            x0 = cx - (n - 1) * s / 2 + i * s
            dr.polygon([(x0 - s / 2 * 0.9, y0), (x0 + s / 2 * 0.9, y0), (x0, y0 + h * 0.9)], fill=(246, 246, 246, 255))


def splinter(seed, tones, n=150, scale=(0.8, 1.6, 0.7)):
    """Mirror-symmetric angular camouflage: each texel takes the tone of its nearest random seed point."""
    rng = np.random.default_rng(seed)
    seeds = np.stack([rng.uniform(0, 5.6, n), rng.uniform(-1.0, 2.8, n), rng.uniform(-5.4, 12, n)], 1)
    tone = rng.integers(0, len(tones), n)
    best = np.full(len(P), np.inf); pick = np.zeros(len(P), int)
    for k, s in enumerate(seeds):
        dd = (((SYM - s) * np.array(scale)) ** 2).sum(1)
        m = dd < best; best[m] = dd[m]; pick[m] = tone[k]
    return np.stack(tones)[pick]


# Where the team schemes carry their number (--numbers, default intake). Each spot is a flat panel, so the digits are not bent
# over a body line: intake  the outer side of the intake duct, behind the lip (Soviet, Russian, Ukrainian, Polish practice)
#                  nose    the forward fuselage below the windscreen          fin  the outer face of both fins, under the badge
#                  tail    the engine nacelle side under the fins, a small serial
NUMBERS = sys.argv[sys.argv.index("--numbers") + 1] if "--numbers" in sys.argv else "intake"


def number(t, fill, outline):
    w = 0.30 * len(t)
    if NUMBERS == "nose":
        return [Marking(8.45, 0.12, w * 0.75, 0.30, text(t, fill, outline), SIDE & (AX < 0.5) & (Z > 7.7) & (Z < 9.3))]
    if NUMBERS == "fin":
        return [Marking(-2.85, 1.3, w * 0.9, 0.4, text(t, fill, outline), FIN)]
    if NUMBERS == "tail":
        return [Marking(-2.6, 0.42, w * 0.75, 0.30, text(t, fill, outline), SIDE & (AX > 1.5) & (Y < 0.7))]
    return [Marking(3.25, -0.40, w * 1.3, 0.52, text(t, fill, outline), SIDE & (AX > 0.9) & (AX < 1.3) & (Y > -0.72) & (Y < -0.08))]


UNDER = lambda: (NY < -0.35) | ((Y < -0.25) & ~TOP)    # belly and lower sides, for a light underside


def bdf_coastal():
    """BDF Coastal: two blue-greys over a pale underside, a blue and green sweep up the fins, the shield above it."""
    L, D, U, BLU, GRN, WHT = C(150, 166, 182), C(98, 118, 142), C(206, 212, 218), C(28, 92, 178), C(38, 140, 72), C(246, 247, 249)
    col = splinter(1107, [L, D])
    col[UNDER()] = U
    sweep = FIN_LE(Y) - 0.55 + 0.45 * (Y - 1.0)          # bands parallel to the leading edge, rising aft
    col[FIN & (Z < sweep) & (Z > sweep - 0.22)] = BLU
    col[FIN & (Z <= sweep - 0.22) & (Z > sweep - 0.34)] = WHT
    col[FIN & (Z <= sweep - 0.34) & (Z > sweep - 0.56)] = GRN
    col[RADOME] = C(70, 78, 86)
    marks = [*number("07", BLU, WHT),
             Marking(-3.05, 2.2, 0.5, 0.56, bdf_shield, FIN)]
    return col, marks


def bdf_woodland():
    """BDF Woodland: green, dark green and earth blotches for the Boscali hills, a light grey-blue belly, green fin tips."""
    G1, G2, BRN, U, GRN, WHT = C(92, 108, 66), C(52, 68, 46), C(112, 92, 62), C(176, 190, 200), C(38, 140, 72), C(246, 247, 249)
    n1 = 0.7 * value_noise(SYM, 1.3, 11) + 0.3 * value_noise(SYM, 0.5, 12)
    n2 = 0.7 * value_noise(SYM, 1.1, 13) + 0.3 * value_noise(SYM, 0.45, 14)
    col = np.where((n1 > 0.56)[:, None], G2, np.where((n2 > 0.60)[:, None], BRN, G1))
    col[UNDER()] = U
    col[FIN & (Y > 2.6)] = GRN
    col[FIN & (Y > 2.52) & (Y <= 2.6)] = WHT
    col[RADOME] = C(60, 66, 58)
    marks = [*number("22", WHT, C(30, 36, 28)),
             Marking(-3.05, 1.85, 0.5, 0.56, bdf_shield, FIN)]
    return col, marks


def pala_desert():
    """PALA Desert: sand, ochre and rust splinter, a sand belly, red fin tips and the red roundel on the fins."""
    S, O, R, U, RED, BLK = C(204, 180, 136), C(170, 126, 80), C(124, 66, 48), C(222, 208, 180), C(200, 28, 30), C(20, 20, 22)
    col = splinter(315, [S, O, R], n=130, scale=(0.7, 1.4, 0.6))
    col[UNDER()] = U
    col[FIN & (Y > 2.55)] = RED
    col[FIN & (Y > 2.49) & (Y <= 2.55)] = BLK
    col[RADOME] = C(96, 84, 70)
    marks = [*number("315", RED, BLK),
             Marking(-3.0, 1.85, 0.56, 0.56, pala_roundel, FIN)]
    return col, marks


def pala_night():
    """PALA Night: charcoal with a darker disruptive pattern, a red spine and cheat line, black fins with a red sweep."""
    CH, DK, RED, BLK, GRY = C(58, 58, 62), C(36, 36, 40), C(190, 26, 28), C(20, 20, 22), C(120, 120, 124)
    n = 0.7 * value_noise(SYM, 1.2, 21) + 0.3 * value_noise(SYM, 0.5, 22)
    col = np.where((n > 0.58)[:, None], DK, CH)
    col[TOP & (Z < 5.6) & (Z > -4.0) & (AX < 0.16)] = RED          # thin red spine behind the canopy
    yc = 0.02 + 0.045 * (10.0 - Z)                                 # cheat line rising from the nose to the fin root
    col[SIDE & (Z < 10.0) & (Z > -1.0) & (np.abs(Y - yc) < 0.03)] = RED
    col[FIN] = BLK
    curve = FIN_LE(Y) - 0.9 + 0.35 * (Y - 1.0)
    col[FIN & (Z >= curve) & (Z < curve + 0.16)] = RED
    col[RADOME] = GRY
    marks = [*number("513", RED, BLK),
             Marking(-3.05, 1.6, 0.56, 0.56, pala_roundel, FIN)]
    return col, marks


SCHEMES = {"german": german, "digital741": digital, "ovt": ovt, "white44": white44, "lii": lii,
           "bdf_coastal": bdf_coastal, "bdf_woodland": bdf_woodland, "pala_desert": pala_desert, "pala_night": pala_night}
if "--only" in sys.argv:
    SCHEMES = {k: SCHEMES[k] for k in sys.argv[sys.argv.index("--only") + 1].split(",")}


# ---------------- compose onto the base texture ----------------

im = np.asarray(Image.open(SRC).convert("RGB").resize((N, N))).astype(np.float32) / 255
r, g, b = im[..., 0], im[..., 1], im[..., 2]
sat = (im.max(-1) - im.min(-1)) / (im.max(-1) + 1e-6); lum = 0.299 * r + 0.587 * g + 0.114 * b
paint = covered & (sat < 0.28) & (lum > 0.42)
redmark = covered & (r > 0.35) & (r > 1.35 * g) & (r > 1.35 * b)     # base stars and "51": painted over, then redrawn per scheme
redmark = np.asarray(Image.fromarray(redmark.astype(np.uint8) * 255).filter(ImageFilter.MaxFilter(5)), bool) & covered   # with their soft edges
shade = detail_shade(lum, paint)   # panel lines, rivets and weathering, not the base's camouflage (livery_shade.py)
shade[redmark] = 1.0


def grow(target, cov, n=4):
    for _ in range(n):
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            sc = np.roll(cov, (dy, dx), (0, 1)); st = np.roll(target, (dy, dx), (0, 1))
            gmask = sc & ~cov
            target[gmask] = st[gmask]; cov |= gmask


for name, fn in SCHEMES.items():
    col, marks = fn()
    col = col.astype(np.float32).copy()
    for m in marks:
        col = m.apply(col)
    target = np.zeros((N, N, 3), np.float32); target[idx] = col
    cov = covered.copy(); grow(target, cov)
    out = im.copy()
    sel = paint | redmark
    out[sel] = np.clip(target[sel] * shade[sel][:, None], 0, 1)
    dst = os.path.join(OUT, f"mig29_basecolor_{name}.png")
    Image.fromarray((out * 255).astype(np.uint8)).save(dst)
    print(name, "->", dst)

    if PREVIEW:   # software render: left side and top views of the textured airframe
        tex = out
        views = {"side": (lambda v: (-v[:, 2], v[:, 1], -v[:, 0])), "top": (lambda v: (v[:, 0], v[:, 2], v[:, 1]))}
        tiles = []
        for vname, proj in views.items():
            S = 60; Wd, Ht = (int(17.5 * S), int(4.0 * S)) if vname == "side" else (int(11.4 * S), int(17.5 * S))
            ox, oy = (12.1 * S, 2.9 * S) if vname == "side" else (5.7 * S, 12.1 * S)
            img = np.full((Ht, Wd, 3), 0.15, np.float32); zb = np.full((Ht, Wd), -np.inf)
            for part in d["parts"]:
                if part["name"] == "cockpit":
                    continue
                v = np.array(part["vertices"], np.float32).reshape(-1, 3)
                if part.get("pivot"): v = v + np.array(part["pivot"], np.float32)
                uv = np.array(part["uvs"], np.float32).reshape(-1, 2) % 1.0
                hx, hy, dep = proj(v)
                sx = ox + hx * S; sy = oy - hy * S
                for a, bb, cc in np.array(part["triangles"]).reshape(-1, 3):
                    T = np.array([[sx[a], sy[a]], [sx[bb], sy[bb]], [sx[cc], sy[cc]]])
                    x0, y0 = np.floor(T.min(0)).astype(int); x1, y1 = np.ceil(T.max(0)).astype(int)
                    x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, Wd - 1), min(y1, Ht - 1)
                    if x1 < x0 or y1 < y0: continue
                    gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
                    (ax_, ay_), (bx_, by_), (cx_, cy_) = T
                    den = (by_ - cy_) * (ax_ - cx_) + (cx_ - bx_) * (ay_ - cy_)
                    if abs(den) < 1e-9: continue
                    w0 = ((by_ - cy_) * (gx - cx_) + (cx_ - bx_) * (gy - cy_)) / den
                    w1 = ((cy_ - ay_) * (gx - cx_) + (ax_ - cx_) * (gy - cy_)) / den
                    w2 = 1 - w0 - w1
                    ins = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
                    if not ins.any(): continue
                    D = w0 * dep[a] + w1 * dep[bb] + w2 * dep[cc]
                    iy, ix = np.nonzero(ins); iy += y0; ix += x0
                    Dv = D[ins]; front = Dv > zb[iy, ix]
                    iy, ix, Dv = iy[front], ix[front], Dv[front]
                    wu = np.stack([w0[ins][front], w1[ins][front], w2[ins][front]], 1) @ uv[[a, bb, cc]]
                    tx = np.clip((wu[:, 0] * N).astype(int), 0, N - 1); ty = np.clip(((1 - wu[:, 1]) * N).astype(int), 0, N - 1)
                    c3 = tex[ty, tx] if part["name"] != "canopy" else np.array([0.35, 0.45, 0.55])
                    img[iy, ix] = c3; zb[iy, ix] = Dv
            tiles.append(Image.fromarray((img * 255).astype(np.uint8)))
        sheet = Image.new("RGB", (tiles[0].width + tiles[1].width, max(t.height for t in tiles)), (38, 38, 38))
        sheet.paste(tiles[0], (0, 0)); sheet.paste(tiles[1], (tiles[0].width, 0))
        os.makedirs(PREVIEW, exist_ok=True)
        sheet.save(os.path.join(PREVIEW, f"preview_{name}.png"))
