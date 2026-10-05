"""Wheel-well texture: one 1024 px tile per metre (tools/main_wells.py box-projects UVs in metres). Light grey-green primer with
frames (raised flanges, rivet rows), stringers, panel tone variation and grime, hydraulic lines with clamps, a wiring loom and small
stencils; a height map turned into a normal map gives the frames, lines and rivets depth. Everything wraps, so it tiles.
The user found the old 512 px grid of grey tiles flat and plain (screenshot, v0.9.0).
python bay_texture.py <MiG29Source>  ->  bay_atlas.png, bay_normal.png, bay_metallic.png"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SRC = sys.argv[1]
N = 1024                                  # px per metre
rng = np.random.default_rng(1729)
col = np.zeros((N, N, 3)); col[:] = (168, 174, 163)
hgt = np.zeros((N, N))
yy, xx = np.mgrid[0:N, 0:N]


def wrapd(a, c):                          # signed wrapped distance along one axis
    return (a - c + N / 2) % N - N / 2


# panels: tone variation per panel (frames every 0.25 m across x, stringers every 0.2 m along y)
FR = [int((i + 0.5) * N / 4) for i in range(4)]
ST = [int((j + 0.5) * N / 5) for j in range(5)]
px = ((xx + N // 8) // (N // 4)) % 4; py = ((yy + N // 10) // (N // 5)) % 5
tone = rng.normal(0, 4, (4, 5))
col += tone[px, py][..., None]
# soft grime: darker toward the bottom of each panel, dirt streaks
for j in ST:
    d = wrapd(yy, j)
    col -= np.clip((d + 0.1 * N) / (0.1 * N), 0, 1)[..., None] * np.clip(1 - np.abs(d) / (0.1 * N), 0, 1)[..., None] * 10
streak = Image.fromarray((rng.random((N // 8, N // 8)) * 255).astype(np.uint8)).resize((N, N), Image.BICUBIC).filter(ImageFilter.GaussianBlur(6))
col -= (np.asarray(streak, float)[..., None] / 255 - 0.5) * 14

# stringers: 16 mm raised bands with a light top edge and a dark lower edge
for j in ST:
    d = wrapd(yy, j)
    band = np.abs(d) < 8
    hgt += band * 3.0 + np.clip(1 - np.abs(d) / 12, 0, 1) * 1.0
    col[band] -= 8
    col[(d > -10) & (d < -7)] += 18
    col[(d > 7) & (d < 11)] -= 22
# frames: 40 mm flanges, raised more, with rivet rows either side
for i in FR:
    d = wrapd(xx, i)
    fl = np.abs(d) < 20
    hgt += fl * 6.0 + np.clip(1 - np.abs(d) / 26, 0, 1) * 2.0
    col[fl] -= 12
    col[(d > -22) & (d < -18)] += 22
    col[(d > 18) & (d < 23)] -= 28
    for off in (-28, 28):
        for r in range(0, N, 24):
            cx, cy = (i + off) % N, r
            m = (wrapd(xx, cx) ** 2 + wrapd(yy, cy) ** 2) < 9
            hgt += m * 1.5; col[m] -= 20

# hydraulic lines along x: polished-ish metal tubes with clamps every 150 mm, a dark wiring loom
def tube(y0, r, base, clamps=True):
    global col, hgt
    d = wrapd(yy, y0)
    m = np.abs(d) < r
    prof = np.sqrt(np.clip(1 - (d / r) ** 2, 0, 1))
    hgt += m * (4 + 6 * prof)
    shade = (0.55 + 0.45 * prof) * (1 + 0.25 * np.clip(-d / r, 0, 1))
    for k in range(3): col[..., k] = np.where(m, base[k] * shade, col[..., k])
    if clamps:
        for cxp in range(37, N, 154):
            cm = (np.abs(wrapd(xx, cxp)) < 6) & (np.abs(d) < r + 4)
            hgt += cm * 4; col[cm] = (70, 74, 70)


tube(int(0.31 * N), 9, (196, 198, 196))
tube(int(0.345 * N), 7, (176, 170, 120))       # brass-tinted line
tube(int(0.72 * N), 11, (190, 194, 192))
tube(int(0.91 * N), 16, (62, 70, 60), clamps=True)      # wiring loom
# small stencils and fittings
img = Image.fromarray(np.clip(col, 0, 255).astype(np.uint8))
dr = ImageDraw.Draw(img)
for _ in range(10):
    x, y = int(rng.integers(40, N - 120)), int(rng.integers(40, N - 40))
    for k in range(int(rng.integers(2, 4))):
        w = int(rng.integers(30, 90))
        dr.rectangle([x, y + k * 9, x + w, y + k * 9 + 4], fill=(40, 42, 40))
for _ in range(14):
    x, y = int(rng.integers(0, N - 30)), int(rng.integers(0, N - 20))
    w, h = int(rng.integers(14, 34)), int(rng.integers(10, 22))
    dr.rectangle([x, y, x + w, y + h], fill=(120, 126, 118), outline=(80, 84, 80))
    hgt[y:y + h, x:x + w] += 4
col = np.asarray(img, float) + rng.normal(0, 2.5, (N, N, 1))
Image.fromarray(np.clip(col, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.5)).save(os.path.join(SRC, "bay_atlas.png"))
# normal map (tangent space, Unity: +y up in the texture)
hb = np.asarray(Image.fromarray(hgt.astype(np.float32)).filter(ImageFilter.GaussianBlur(1.2)), float) if False else hgt
gx = (np.roll(hb, -1, 1) - np.roll(hb, 1, 1)) / 2; gy = (np.roll(hb, -1, 0) - np.roll(hb, 1, 0)) / 2
s = 0.35
nrm = np.stack([-gx * s, gy * s, np.ones_like(gx)], -1)
nrm /= np.linalg.norm(nrm, axis=2, keepdims=True)
Image.fromarray(((nrm * 0.5 + 0.5) * 255).astype(np.uint8)).save(os.path.join(SRC, "bay_normal.png"))
Image.new("RGBA", (8, 8), (20, 0, 0, 80)).save(os.path.join(SRC, "bay_metallic.png"))
print("[bay] bay_atlas.png, bay_normal.png written")
