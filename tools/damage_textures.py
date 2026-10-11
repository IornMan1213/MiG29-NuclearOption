"""MiG-29 damage textures in the stock aircraft format (the game's AircraftSkin shader).

Measured in game (NODevDriver dmgtex/skinhp, 2026-10-11) on the skin shader every stock aircraft uses:
  _BasecolorDmg RGB  multiplies the livery, faded in as the part's hit points fall from 100 to about -20
  _BasecolorDmg A    burn mask: where alpha < ~0.5 the skin chars black once the part has taken ~10-20 damage
                     (lower alpha chars first); above 0.5 it never chars, it only takes the RGB overlay
  _NormalDmg         replaces the normal map with the same fade (so it carries the panel lines too)
The stock KR-67's map is grey soot/grunge with alpha ~0.6 over most of the skin and ~15% burn patches (alpha 0.16-0.45).
The MiG used to ship a dark recoloured livery with no alpha: every damaged part went uniformly black, without burns or
dents (user screenshots after being shot down, v0.9.2).

Here the patterns come from 3D noise over the MiG's own surface (each texel's position on the model, rasterised from the
UV layout), so soot, burns and dents run across texture seams and follow the airframe:
  MiG29Source/mig29_basecolor_dmg.png   RGBA  soot/grunge (streaked aft) + burn mask
  MiG29Source/mig29_normal_dmg.png      RGB   the skin's normal map with crumpled, dented panels blended in
Usage: python damage_textures.py <MiG29Source folder>
"""
import json, os, sys
import numpy as np
from PIL import Image

N = 2048
src = sys.argv[1]
outs = [os.path.join(src, n) for n in ("mig29_basecolor_dmg.png", "mig29_normal_dmg.png")]
ins = [os.path.join(src, n) for n in ("mig29_mesh.json", "mig29_normal.png")] + [os.path.abspath(__file__)]
if all(os.path.exists(o) for o in outs) and min(os.path.getmtime(o) for o in outs) > max(os.path.getmtime(i) for i in ins):
    print("[dmg] up to date"); sys.exit(0)   # ~70 s to bake
mesh = json.load(open(os.path.join(src, "mig29_mesh.json")))

# ---- texel -> model position (rasterised UV layout) ----
pos = np.zeros((N, N, 3), np.float32)
cov = np.zeros((N, N), bool)
for p in mesh["parts"]:
    if p["material"] != "airframe" or p["name"] == "cockpit":   # the model's cockpit is replaced; glass has its own map
        continue
    v = np.array(p["vertices"], np.float32).reshape(-1, 3)
    uv = np.array(p["uvs"], np.float32).reshape(-1, 2)
    px = np.stack([uv[:, 0] * N - 0.5, (1 - uv[:, 1]) * N - 0.5], 1)   # Unity v is up, image rows go down
    t = np.array(p["triangles"]).reshape(-1, 3)
    for a, b, c in t:
        pa, pb, pc = px[a], px[b], px[c]
        x0, x1 = int(max(0, np.floor(min(pa[0], pb[0], pc[0])))), int(min(N - 1, np.ceil(max(pa[0], pb[0], pc[0]))))
        y0, y1 = int(max(0, np.floor(min(pa[1], pb[1], pc[1])))), int(min(N - 1, np.ceil(max(pa[1], pb[1], pc[1]))))
        if x1 < x0 or y1 < y0:
            continue
        d = (pb[1] - pc[1]) * (pa[0] - pc[0]) + (pc[0] - pb[0]) * (pa[1] - pc[1])
        if abs(d) < 1e-9:
            continue
        X, Y = np.meshgrid(np.arange(x0, x1 + 1, dtype=np.float32), np.arange(y0, y1 + 1, dtype=np.float32))
        w0 = ((pb[1] - pc[1]) * (X - pc[0]) + (pc[0] - pb[0]) * (Y - pc[1])) / d
        w1 = ((pc[1] - pa[1]) * (X - pc[0]) + (pa[0] - pc[0]) * (Y - pc[1])) / d
        w2 = 1 - w0 - w1
        m = (w0 >= -0.02) & (w1 >= -0.02) & (w2 >= -0.02)   # a little over the edge: no cracks between triangles
        if not m.any():
            continue
        P = w0[..., None] * v[a] + w1[..., None] * v[b] + w2[..., None] * v[c]
        ys, xs = Y[m].astype(int), X[m].astype(int)
        pos[ys, xs] = P[m]
        cov[ys, xs] = True
print(f"[dmg] surface covers {cov.mean() * 100:.1f}% of the texture")

# ---- dilate into the gaps between UV islands (mip-mapping would pull in junk otherwise) ----
filled = cov.copy()
for _ in range(24):
    grow = np.zeros_like(filled)
    acc = np.zeros_like(pos); cnt = np.zeros((N, N), np.float32)
    for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
        s = np.roll(np.roll(filled, dy, 0), dx, 1)
        sp = np.roll(np.roll(pos, dy, 0), dx, 1)
        add = s & ~filled
        acc[add] += sp[add]; cnt[add] += 1
        grow |= add
    pos[grow] = acc[grow] / cnt[grow][:, None]
    filled |= grow
    if filled.all():
        break

# ---- 3D value noise ----
rng = np.random.default_rng(29)
PERM = rng.permutation(256)
PERM = np.concatenate([PERM, PERM])
VAL = rng.random(256).astype(np.float32)

def vnoise(p):
    i = np.floor(p).astype(np.int64); f = p - i
    f = f * f * (3 - 2 * f)
    i &= 255
    def h(dx, dy, dz):
        return VAL[PERM[PERM[PERM[i[..., 0] + dx] + i[..., 1] + dy] + i[..., 2] + dz] & 255]
    x00 = h(0, 0, 0) * (1 - f[..., 0]) + h(1, 0, 0) * f[..., 0]
    x10 = h(0, 1, 0) * (1 - f[..., 0]) + h(1, 1, 0) * f[..., 0]
    x01 = h(0, 0, 1) * (1 - f[..., 0]) + h(1, 0, 1) * f[..., 0]
    x11 = h(0, 1, 1) * (1 - f[..., 0]) + h(1, 1, 1) * f[..., 0]
    y0 = x00 * (1 - f[..., 1]) + x10 * f[..., 1]
    y1 = x01 * (1 - f[..., 1]) + x11 * f[..., 1]
    return y0 * (1 - f[..., 2]) + y1 * f[..., 2]

def fbm(p, octaves=5):
    s, a, tot = 0.0, 1.0, 0.0
    for o in range(octaves):
        s = s + a * vnoise(p * (2 ** o) + o * 17.3)
        tot += a; a *= 0.5
    return s / tot

def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)

P = pos.astype(np.float32)
streak = P * np.array([2.2, 2.2, 0.45], np.float32)        # long aft streaks (model z runs nose to tail)

# burns: irregular patches, ~14% of the skin
burn_n = fbm(P * 0.9 + 3.1) * 0.75 + fbm(P * 4.0 + 9.7, 3) * 0.25
thr = np.quantile(burn_n[cov], 0.86)
depth = smooth(thr, thr + 0.08, burn_n)                       # 0 at the patch edge, 1 inside
burn = burn_n > thr

# soot / grunge (grey multiplier): a general scorched film, heavier streaks, and soot spreading round the burns
grunge = 1.0 - 0.22 * smooth(0.35, 0.75, fbm(P * 1.6 + 1.0))
grunge -= 0.28 * smooth(0.45, 0.8, fbm(streak + 5.0, 4))
grunge -= 0.35 * smooth(thr - 0.10, thr, burn_n)
grunge -= 0.08 * (fbm(P * 22.0 + 2.0, 2) - 0.5)               # fine speckle
grunge = np.clip(grunge, 0.25, 1.0)

alpha = np.full((N, N), 0.60, np.float32)
alpha += 0.06 * (fbm(P * 3.0 + 4.0, 3) - 0.5)                 # never chars: stays above 0.5
alpha = np.where(burn, 0.45 - 0.29 * depth, alpha)            # 0.45 at the edge (chars last) .. 0.16 inside (first)

rgb = np.repeat(grunge[..., None], 3, 2)
rgba = np.concatenate([rgb, alpha[..., None]], 2)
Image.fromarray((np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(src, "mig29_basecolor_dmg.png"))

# ---- dented normal map ----
h = np.abs(fbm(P * 2.6 + 7.0, 4) - 0.5) * 2                   # ridged: crumpled sheet
h = h * 0.7 + 0.3 * fbm(P * 9.0 + 3.3, 3)
h += 0.6 * depth                                              # burnt-through patches bulge/sag
gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
k = 9.0                                                       # strength (height units per texel)
dn = np.stack([-gx * k, gy * k, np.ones_like(h)], 2)          # image rows run down = -v
dn /= np.linalg.norm(dn, axis=2, keepdims=True)
base = np.asarray(Image.open(os.path.join(src, "mig29_normal.png")).convert("RGB"), np.float32) / 255 * 2 - 1
if base.shape[0] != N:
    base = np.asarray(Image.open(os.path.join(src, "mig29_normal.png")).convert("RGB").resize((N, N), Image.BILINEAR), np.float32) / 255 * 2 - 1
n = np.stack([base[..., 0] + dn[..., 0], base[..., 1] + dn[..., 1], base[..., 2] * dn[..., 2]], 2)   # whiteout blend
n /= np.linalg.norm(n, axis=2, keepdims=True)
Image.fromarray(((n * 0.5 + 0.5) * 255 + 0.5).clip(0, 255).astype(np.uint8), "RGB").save(os.path.join(src, "mig29_normal_dmg.png"))
print(f"[dmg] burns on {burn[cov].mean() * 100:.1f}% of the skin, soot {grunge[cov].mean():.2f} mean, alpha {alpha[cov].min():.2f}-{alpha[cov].max():.2f}")
