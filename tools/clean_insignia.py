"""Take the source model's insignia out of the skin's metallic / smoothness texture.

mig29_metallic.png (R = metallic, A = smoothness) came with the source model's markings baked in: red stars, board numbers "71",
a badge on the fins and stencils. Every livery paints only the base colour, so those shapes showed through on all of them as
shiny or dull ghosts (a star and a circle on the fin of a black livery: user screenshot, v0.9.1). The markings are darker than
the skin around them in R; they are found against a local background (median over ~80 px) and filled in R and A from the
surrounding skin (normalised blur, so the smoothness keeps its local level). Large features (the black intake / nozzle discs,
the bright metal panels) are bigger than the filter and stay.

Reads mig29_metallic.orig.png (made from mig29_metallic.png on the first run), writes mig29_metallic.png: safe to run on every build.
python tools/clean_insignia.py <MiG29Source dir> [<debug out dir>]"""
import os, shutil, sys
import numpy as np
from PIL import Image, ImageFilter

SRC = sys.argv[1]
DBG = sys.argv[2] if len(sys.argv) > 2 else None
OUT = os.path.join(SRC, "mig29_metallic.png")
ORIG = os.path.join(SRC, "mig29_metallic.orig.png")
if not os.path.exists(ORIG):
    shutil.copy(OUT, ORIG)

im = Image.open(ORIG).convert("RGBA")
W, H = im.size
r, g, b, a = [np.asarray(c, dtype=np.float32) for c in im.split()]


def background(ch):
    """Local median at quarter resolution (21 px there, ~84 px here), back to full size."""
    small = Image.fromarray(ch.astype(np.uint8)).resize((W // 4, H // 4), Image.BILINEAR).filter(ImageFilter.MedianFilter(21))
    return np.asarray(small.resize((W, H), Image.BILINEAR), dtype=np.float32)


bg_r, bg_a = background(r), background(a)
skin = bg_r < 90                            # painted skin, not the bright metal panels


def components(m):
    """4-connected components of a boolean image: list of (ys, xs) index arrays."""
    seen = np.zeros_like(m, dtype=bool)
    out = []
    for y0, x0 in zip(*np.nonzero(m)):
        if seen[y0, x0]:
            continue
        stack = [(y0, x0)]; seen[y0, x0] = True; ys = []; xs = []
        while stack:
            y, x = stack.pop(); ys.append(y); xs.append(x)
            for yy, xx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                if 0 <= yy < H and 0 <= xx < W and m[yy, xx] and not seen[yy, xx]:
                    seen[yy, xx] = True; stack.append((yy, xx))
        out.append((np.array(ys), np.array(xs)))
    return out


def markings(ch, bgc, cand, ring_tol, nmax, other=None):
    """Compact blobs of cand sitting inside plain skin: their 6 px surround must match the local background (within ring_tol),
    hold no bright metal and not be more of cand (so the edges of the intake / nozzle discs, dark panels and the like stay).
    Pixels of other (the opposite-polarity candidates) are left out of the surround: a two-tone fitting is cleaned whole."""
    found = np.zeros((H, W), dtype=bool); count = 0
    for ys, xs in components(cand):
        n = len(ys)
        if n < 6 or n > nmax:
            continue
        y0, y1, x0, x1 = max(ys.min() - 6, 0), min(ys.max() + 7, H), max(xs.min() - 6, 0), min(xs.max() + 7, W)
        if (y1 - y0) * (x1 - x0) > 40 * n:          # thin and long: an edge or a panel line, not a marking
            continue
        blob = np.zeros((y1 - y0, x1 - x0), dtype=bool); blob[ys - y0, xs - x0] = True
        grown = np.asarray(Image.fromarray(blob.astype(np.uint8) * 255).filter(ImageFilter.MaxFilter(13))) > 0
        ring = grown & ~blob & ~cand[y0:y1, x0:x1]
        if other is not None and n <= 300:          # fittings are tiny; a big feature's opposite-tone rim is not part of it
            ring &= ~other[y0:y1, x0:x1]
        if ring.sum() < 10 or not skin[y0:y1, x0:x1][ring].all():
            continue
        cv, bv = ch[y0:y1, x0:x1][ring], bgc[y0:y1, x0:x1][ring]
        if np.abs(cv - bv).mean() > ring_tol or r[y0:y1, x0:x1][ring].max() > 110:
            continue
        found[ys, xs] = True; count += 1
    return found, count


# the source model's stars, board numbers and badge are darker than the skin in R; small fittings and stencils are brighter in R or
# stand out only in the smoothness (A) as round shiny or dull spots
# (only on the airframe's own skin: the left two thirds of the atlas above the bottom strip; the right third holds small parts whose
# detail is real)
airframe = np.zeros((H, W), dtype=bool); airframe[: int(H * 0.87), : int(W * 0.66)] = True
dark, bright = (r < bg_r - 12) & skin, (r > bg_r + 15) & skin & airframe
m1, n1 = markings(r, bg_r, dark, 6, 12000, other=bright)
m2, n2 = markings(r, bg_r, bright, 6, 1500, other=dark)
m3, n3 = markings(a, bg_a, (np.abs(a - bg_a) > 40) & skin & airframe, 10, 1500)
mask = m1 | m2 | m3
m = Image.fromarray((mask * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5))   # grow 2 px over antialiased edges
mask = np.asarray(m) > 0
print(f"[insignia] {n1} dark markings, {n2} bright spots, {n3} smoothness spots")


def box(x, rad):
    """Separable box blur (edge-clamped) via cumulative sums."""
    for axis in (0, 1):
        p = np.pad(x, [(rad + 1, rad) if ax == axis else (0, 0) for ax in (0, 1)], mode="edge")
        c = np.cumsum(p, axis=axis, dtype=np.float64)
        x = ((np.take(c, range(2 * rad + 1, c.shape[axis]), axis=axis) - np.take(c, range(0, c.shape[axis] - 2 * rad - 1), axis=axis)) / (2 * rad + 1)).astype(np.float32)
    return x


def blur(x, radius):
    for _ in range(3):   # three box passes ~ a Gaussian
        x = box(x, radius // 2)
    return x


def fill(ch, bgc, radius=12):
    """Fill the mask with real skin from nearby: copied from an unmasked spot a little way off (keeps the grime and rivet texture;
    a flat average left star-shaped smooth patches in the smoothness), shifted to the local level around the hole."""
    keep = (~mask).astype(np.float32)
    den = blur(keep, radius)
    level = np.where(den > 0.2, blur(ch * keep, radius) / np.maximum(den, 1e-3), bgc)   # local skin level, holes excluded (deep inside a big hole: the wider median)
    out = ch.copy(); todo = mask.copy()
    for d in (24, 40, 64, 96, 140):
        for dy, dx in ((0, d), (0, -d), (d, 0), (-d, 0), (d, d), (-d, -d), (d, -d), (-d, d)):
            src_ok = np.roll(PLAIN, (dy, dx), axis=(0, 1))
            take = todo & src_ok & ~np.roll(~skin, (dy, dx), axis=(0, 1))
            if not take.any():
                continue
            src = np.roll(ch, (dy, dx), axis=(0, 1)) - np.roll(level, (dy, dx), axis=(0, 1)) + level
            out[take] = src[take]; todo &= ~take
    out[todo] = level[todo]                                                      # nothing suitable nearby: the local level
    return out


# source pixels for the fill: plain skin in both channels, never another hole, fitting or panel edge (the same pixels for R and A)
PLAIN = ~mask & skin & (np.abs(r - bg_r) < 10) & (np.abs(a - bg_a) < 30)
r2, a2 = fill(r, bg_r), fill(a, bg_a)
out = Image.merge("RGBA", [Image.fromarray(np.clip(x, 0, 255).astype(np.uint8)) for x in (r2, g, b, a2)])
out.save(OUT)
print(f"[insignia] {mask.mean() * 100:.2f}% of the metallic/smoothness map filled from the surrounding skin")
if DBG:
    os.makedirs(DBG, exist_ok=True)
    Image.fromarray((mask * 255).astype(np.uint8)).save(os.path.join(DBG, "insignia_mask.png"))
