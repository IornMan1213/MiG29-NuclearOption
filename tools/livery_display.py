"""Display-team livery: blue upper surfaces, white undersides, red/white chevron on top and fin bands.

python tools/livery_display.py <mig29_basecolor.png> <mig29_mesh.json> <out.png>

Unlike the desert/digital schemes (texture-space recolours), this one is designed on the airframe: every exterior triangle is
rasterised into the texture with its interpolated MiG-frame position and normal, and the colour comes from those (upper/lower
split by the surface normal, chevron in planform coordinates, bands on the fins by height). Only low-saturation paint texels
change, so stars, numbers and stencils stay; panel lines are kept by shading relative to a blurred copy.
"""
import json, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SRC, MESH, DST = sys.argv[1], sys.argv[2], sys.argv[3]
N = 2048
d = json.load(open(MESH))
BLUE, WHITE, RED = np.array([0.07, 0.20, 0.52]), np.array([0.93, 0.94, 0.95]), np.array([0.78, 0.07, 0.09])


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


# planform leading edge from the mesh: the most forward point of the airframe per 5 cm spanwise bin (|x|)
_allv = np.concatenate([np.array(p_["vertices"], np.float32).reshape(-1, 3) for p_ in d["parts"] if p_["name"] not in ("canopy", "cockpit", "rails")])
_bins = np.arange(0, 6.0, 0.05)
_le = np.array([(_allv[(np.abs(_allv[:, 0]) >= b0) & (np.abs(_allv[:, 0]) < b0 + 0.05), 2].max()
                 if ((np.abs(_allv[:, 0]) >= b0) & (np.abs(_allv[:, 0]) < b0 + 0.05)).any() else np.nan) for b0 in _bins])
_ok = ~np.isnan(_le); _le = np.interp(_bins, _bins[_ok], _le[_ok])
# two straight leading edges (LERX and wing) fitted to the per-bin maxima; the planform LE is the forward-most of the two
_fit = lambda lo, hi: np.polyfit(_bins[(_bins >= lo) & (_bins <= hi)] + 0.025, _le[(_bins >= lo) & (_bins <= hi)], 1)
_lerx, _wing = _fit(1.05, 1.9), _fit(2.4, 5.3)
zLE = lambda ax: np.maximum(np.polyval(_lerx, ax), np.polyval(_wing, ax))
print("leading edges: LERX z = %.2f %+.2f|x|, wing z = %.2f %+.2f|x|" % (_lerx[1], _lerx[0], _wing[1], _wing[0]))


def colour(p, n):
    """p, n: (k,3) MiG-frame positions (x right, y up, z fwd, metres) and unit normals -> (k,3) linear-ish RGB."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    ny, nx = n[:, 1], np.abs(n[:, 0])
    # blue above / white below: by the normal on clearly up- or down-facing skin, by height (crisp line at y 0.15) on the sides,
    # where the normal alone flips back and forth and mottles the paint
    side = 1 - smooth(0.25, 0.4, np.abs(ny))
    top = (1 - side) * (ny > 0) + side * smooth(0.13, 0.17, y)
    c = WHITE[None] * (1 - top[:, None]) + BLUE[None] * top[:, None]
    # red/white stripe on the upper surfaces, following the LERX and wing leading edges (planform-derived), from the LERX root out
    ax = np.abs(x)
    behind = zLE(ax) - z
    on_top = (top > 0.5) & (ax > 0.95)
    c = np.where((on_top & (behind > 0.18) & (behind < 0.95))[:, None], WHITE[None], c)
    c = np.where((on_top & (behind > 0.30) & (behind < 0.83))[:, None], RED[None], c)
    # fins (vertical surfaces above the fuselage): blue with white/red bands
    fin = (nx > 0.75) & (y > 0.9)
    c = np.where(fin[:, None], BLUE[None], c)
    c = np.where((fin & (y > 1.85) & (y < 2.45))[:, None], WHITE[None], c)
    c = np.where((fin & (y > 1.97) & (y < 2.33))[:, None], RED[None], c)
    # white radome tip
    c = np.where((z > 10.3)[:, None], WHITE[None], c)
    return c


target = np.zeros((N, N, 3), np.float32)
covered = np.zeros((N, N), bool)
for part in d["parts"]:
    if part["name"] in ("canopy", "cockpit"):
        continue
    v = np.array(part["vertices"], np.float32).reshape(-1, 3)
    nrm = np.array(part["normals"], np.float32).reshape(-1, 3)
    uv = np.array(part["uvs"], np.float32).reshape(-1, 2) % 1.0
    tris = np.array(part["triangles"]).reshape(-1, 3)
    px = np.stack([uv[:, 0] * N, (1 - uv[:, 1]) * N], 1)
    for a, b, cc in tris:
        P = px[[a, b, cc]]
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
        inside = (w0 >= -0.02) & (w1 >= -0.02) & (w2 >= -0.02)   # slight overdraw closes seams between triangles
        if not inside.any():
            continue
        W = np.stack([w0[inside], w1[inside], w2[inside]], 1)
        pos = W @ v[[a, b, cc]]
        nn = W @ nrm[[a, b, cc]]
        nn /= np.linalg.norm(nn, axis=1, keepdims=True) + 1e-9
        iy, ix = np.nonzero(inside)
        target[iy + y0, ix + x0] = colour(pos, nn)
        covered[iy + y0, ix + x0] = True

# grow the painted islands a few texels so mip-mapping doesn't pull in the old colours at UV seams
for _ in range(4):
    for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        src_c = np.roll(covered, (dy, dx), (0, 1)); src_t = np.roll(target, (dy, dx), (0, 1))
        grow = src_c & ~covered
        target[grow] = src_t[grow]; covered |= grow

im = np.asarray(Image.open(SRC).convert("RGB").resize((N, N))).astype(np.float32) / 255
r, g, b = im[..., 0], im[..., 1], im[..., 2]
sat = (im.max(-1) - im.min(-1)) / (im.max(-1) + 1e-6); lum = 0.299 * r + 0.587 * g + 0.114 * b
paint = covered & (sat < 0.28) & (lum > 0.42)
lp = np.where(paint, lum, 0).astype(np.float32); wp = paint.astype(np.float32)
blur = lambda a: np.asarray(Image.fromarray((a * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(10)), np.float32) / 255
local = blur(lp) / np.maximum(blur(wp), 1e-3)
shade = np.clip(lum / np.maximum(local, 1e-3), 0.6, 1.25)[..., None]
out = im.copy()
out[paint] = np.clip(target[paint] * shade[paint], 0, 1)
Image.fromarray((out * 255).astype(np.uint8)).save(DST)
print("display livery ->", DST)
