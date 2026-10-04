"""Cockpit texture atlas for the from-scratch MiG-29 interior: paint swatches, instrument faces, switch panels, caution panel.

python tools/cockpit_atlas.py <MiG29Source dir>
  -> cockpit_atlas.png (2048, sRGB base colour), cockpit_metallic.png (R metallic, A smoothness), cockpit_atlas.json (rects)

Everything is drawn here (no external art). Rects in the JSON are pixel boxes [x, y, w, h], y down from the top; the Blender
builder (blender/cockpit_build.py) turns them into UVs. Drawn at 4x and downsampled for clean edges.
"""
import json, math, os, random, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = sys.argv[1] if len(sys.argv) > 1 else "."
K = 2   # texel density: cells are laid out in "cell px" and stored at K texels per cell px (4096 atlas, sharp up close)
W = 2048 * K
SS = 4 * K  # drawing resolution per cell px (4x supersampling of the stored texels)
FONT = "C:/Windows/Fonts/arialbd.ttf"
FONT_R = "C:/Windows/Fonts/arial.ttf"
FONT_N = "C:/Windows/Fonts/bahnschrift.ttf"

# paint (sRGB). MiG-29 9.12 interior: blue-green ("turquoise") panels and consoles, black instruments and switch plates.
PAINT = {
    "turq": (82, 146, 142), "turq_dark": (58, 112, 110), "black": (24, 25, 27), "dgrey": (46, 48, 50), "mgrey": (92, 95, 97),
    "lgrey": (150, 152, 150), "metal": (165, 166, 168), "seat": (62, 68, 62), "cushion": (88, 92, 76), "headrest": (64, 66, 70),
    "yellow": (226, 186, 32), "red": (176, 32, 26), "white": (226, 226, 220), "floor": (40, 42, 44), "rubber": (30, 30, 30),
    "green_lamp": (40, 170, 70), "amber": (230, 140, 20), "olive": (86, 90, 62),
}
# (metallic 0-255, smoothness 0-255) per paint
SURF = {"metal": (200, 150), "black": (0, 70), "rubber": (0, 30), "cushion": (0, 25), "headrest": (0, 40), "floor": (0, 40)}

random.seed(29)


def font(size, path=FONT):
    return ImageFont.truetype(path, int(size))


class Packer:
    """Shelf packer, rows left to right."""

    def __init__(self, w):
        self.w, self.x, self.y, self.row = w, 0, 0, 0

    def place(self, w, h):
        if self.x + w > self.w:
            self.x, self.y, self.row = 0, self.y + self.row, 0
        r = (self.x, self.y, w, h)
        self.x += w; self.row = max(self.row, h)
        return r


atlas = Image.new("RGB", (W, W), (40, 40, 40))
metal = Image.new("RGBA", (W, W), (0, 0, 0, 60))
rects = {}
pk = Packer(W)


def put(name, img, surf=(0, 60)):
    r = pk.place(*img.size)
    atlas.paste(img, r[:2])
    m = Image.new("RGBA", img.size, (surf[0], 0, 0, surf[1]))
    metal.paste(m, r[:2])
    rects[name] = list(r)
    return r


def canvas(w, h, col):
    return Image.new("RGB", (w * SS, h * SS), col)


def down(img, w, h):
    return img.resize((w * K, h * K), Image.LANCZOS)


def noise_fill(w, h, col, amp=6, grime=0.0, seed=0):
    """Painted-metal swatch, w x h cell px (stored at K texels per px)."""
    w, h = w * K, h * K
    rnd = random.Random(seed)
    img = Image.new("RGB", (w, h), col)
    px = img.load()
    for y in range(h):
        for x in range(w):
            n = rnd.uniform(-amp, amp)
            px[x, y] = tuple(max(0, min(255, int(c + n))) for c in col)
    img = img.filter(ImageFilter.GaussianBlur(0.8 * K))
    if grime:
        g = Image.new("L", (w, h), 0); d = ImageDraw.Draw(g)
        for _ in range(int(40 * grime)):
            cx, cy, r = rnd.uniform(0, w), rnd.uniform(0, h), rnd.uniform(4 * K, w / 5)
            d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=rnd.randint(8, 30))
        g = g.filter(ImageFilter.GaussianBlur(w / 16))
        dark = Image.new("RGB", (w, h), tuple(int(c * 0.7) for c in col))
        img = Image.composite(dark, img, g)
    return img


# ---------------------------------------------------------------- paint swatches (planar-mapped surfaces)
for name, col in PAINT.items():
    grime = 0.6 if name in ("turq", "turq_dark", "floor", "dgrey", "seat") else 0.0
    amp = 10 if name in ("cushion", "floor") else 5
    img = noise_fill(128, 128, col, amp, grime, seed=hash(name) % 1000)
    # (cushions are separate padded blocks now: plain fabric, no printed quilting, which planar mapping turned into stray slashes)
    if name == "floor":     # anti-slip tread
        d = ImageDraw.Draw(img)
        for yy in range(2, 128, 6):
            for xx in range(2 + (yy // 6 % 2) * 3, 128, 6):
                d.rectangle((xx * K, yy * K, (xx + 2) * K, (yy + 1) * K), fill=(58, 60, 62))
    put("paint_" + name, img, SURF.get(name, (0, 70)))

# yellow/black ejection handle stripes
img = canvas(256, 64, PAINT["black"]); d = ImageDraw.Draw(img)
for i in range(-64, 256, 32):
    d.polygon([(i * SS, 64 * SS), ((i + 16) * SS, 64 * SS), ((i + 80) * SS, 0), ((i + 64) * SS, 0)], fill=PAINT["yellow"])
put("stripes", down(img, 256, 64))


# ---------------------------------------------------------------- instrument faces
def dial(size, draw_fn, bezel=True, round_=True):
    s = size * SS
    img = canvas(size, size, (14, 14, 15))
    d = ImageDraw.Draw(img)
    c = s / 2
    draw_fn(d, c, s, img)
    if bezel:  # thin inner lip
        d.ellipse((s * 0.02, s * 0.02, s * 0.98, s * 0.98), outline=(55, 57, 58), width=int(s * 0.03))
    # mask the corners to bezel black so the round face sits in a square cell
    mask = Image.new("L", (s, s), 0 if round_ else 255); ImageDraw.Draw(mask).ellipse((s * 0.005, s * 0.005, s * 0.995, s * 0.995), fill=255)
    out = Image.composite(img, Image.new("RGB", (s, s), (20, 20, 21)), mask)
    return down(out, size, size)


def ticks(d, c, s, a0, a1, n, r0, r1, width, col=(235, 235, 230)):
    for i in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        d.line((c + math.sin(a) * r0, c - math.cos(a) * r0, c + math.sin(a) * r1, c - math.cos(a) * r1), fill=col, width=int(width))


def label(d, xy, text, size, col=(235, 235, 230), path=FONT):
    f = font(size, path)
    d.text(xy, text, font=f, fill=col, anchor="mm")


def numbers(d, c, s, a0, a1, labels, r, size):
    for i, t in enumerate(labels):
        a = math.radians(a0 + (a1 - a0) * i / (len(labels) - 1))
        label(d, (c + math.sin(a) * r, c - math.cos(a) * r), t, size, path=FONT_N)


DRAW_NEEDLES = False   # needles, cards and the attitude ball are real moving parts (MiG29Instruments plugin)
LAYOUT = {}            # lamp / instrument layout shared with blender/cockpit_build.py (written to cockpit_atlas.json)


def needle(d, c, s, ang, length, width, col=(240, 240, 235), tail=0.15):
    if not DRAW_NEEDLES:
        return
    a = math.radians(ang)
    tip = (c + math.sin(a) * length, c - math.cos(a) * length)
    bk = (c - math.sin(a) * length * tail, c + math.cos(a) * length * tail)
    nx, ny = math.cos(a) * width / 2, math.sin(a) * width / 2
    d.polygon([(bk[0] - nx, bk[1] - ny), (tip[0], tip[1]), (bk[0] + nx, bk[1] + ny)], fill=col)
    d.ellipse((c - width, c - width, c + width, c + width), fill=(30, 30, 30), outline=(120, 120, 120), width=int(width * 0.3))


def g_asi(d, c, s, img):  # KUS: km/h 0-1600 outer, Mach inner
    ticks(d, c, s, -150, 150, 32, s * 0.40, s * 0.45, s * 0.008)
    ticks(d, c, s, -150, 150, 8, s * 0.37, s * 0.45, s * 0.016)
    numbers(d, c, s, -150, 150, ["0", "2", "4", "6", "8", "10", "12", "14", "16"], s * 0.31, s * 0.075)
    d.ellipse((c - s * 0.21, c - s * 0.21, c + s * 0.21, c + s * 0.21), outline=(200, 200, 195), width=int(s * 0.006))
    ticks(d, c, s, -150, 150, 10, s * 0.17, s * 0.205, s * 0.006, (240, 200, 70))
    for i, t in enumerate(["0.4", "0.8", "1.2", "1.6", "2.0", "2.4"]):
        a = math.radians(-150 + 60 * i); label(d, (c + math.sin(a) * s * 0.125, c - math.cos(a) * s * 0.125), t, s * 0.038, (240, 200, 70), FONT_N)
    label(d, (c, c + s * 0.27), "КМ/Ч ×100", s * 0.045, path=FONT_R)
    label(d, (c, c + s * 0.05), "M", s * 0.05, (240, 200, 70))
    needle(d, c, s, -40, s * 0.43, s * 0.03)
    needle(d, c, s, 60, s * 0.2, s * 0.025, col=(230, 180, 40))


def g_alt(d, c, s, img):  # VDI: 0-10 (x1000 m) small hand, 0-1000 m big hand
    ticks(d, c, s, 0, 360, 50, s * 0.41, s * 0.45, s * 0.008)
    ticks(d, c, s, 0, 360, 10, s * 0.37, s * 0.45, s * 0.018)
    numbers(d, c, s, 0, 324, [str(i) for i in range(10)], s * 0.31, s * 0.08)
    label(d, (c, c + s * 0.15), "ВД-30", s * 0.04, path=FONT_R)
    label(d, (c, c - s * 0.13), "М ×1000", s * 0.045, path=FONT_R)
    needle(d, c, s, 110, s * 0.25, s * 0.045)
    needle(d, c, s, 205, s * 0.43, s * 0.025)


def g_vsi(d, c, s, img):  # VAR: +-300 m/s, nonlinear, zero at 9 o'clock
    vals = ["0", "50", "100", "200", "300"]
    for sgn in (1, -1):
        for i, t in enumerate(vals):
            a = -90 + sgn * 160 * (i / 4)
            label(d, (c + math.sin(math.radians(a)) * s * 0.31, c - math.cos(math.radians(a)) * s * 0.31), t, s * 0.065, path=FONT_N)
        ticks(d, c, s, -90, -90 + sgn * 160, 16, s * 0.40, s * 0.45, s * 0.008)
        ticks(d, c, s, -90, -90 + sgn * 160, 4, s * 0.37, s * 0.45, s * 0.016)
    label(d, (c + s * 0.1, c), "М/С", s * 0.05, path=FONT_R)
    needle(d, c, s, -90, s * 0.43, s * 0.03)


def g_aoa(d, c, s, img):  # UAP: AoA left arc, G right arc
    ticks(d, c, s, -140, -20, 12, s * 0.40, s * 0.45, s * 0.008)
    for i, t in enumerate(["-10", "0", "10", "20", "30"]):
        a = math.radians(-140 + 120 * i / 4); label(d, (c + math.sin(a) * s * 0.32, c - math.cos(a) * s * 0.32), t, s * 0.06, path=FONT_N)
    d.arc((c - s * 0.45, c - s * 0.45, c + s * 0.45, c + s * 0.45), -90 + -50, -90 + -20, fill=(200, 40, 30), width=int(s * 0.03))
    ticks(d, c, s, 20, 140, 12, s * 0.40, s * 0.45, s * 0.008)
    for i, t in enumerate(["-2", "0", "2", "4", "6", "8", "10"]):
        a = math.radians(140 - 120 * i / 6); label(d, (c + math.sin(a) * s * 0.32, c - math.cos(a) * s * 0.32), t, s * 0.055, path=FONT_N)
    label(d, (c - s * 0.15, c + s * 0.2), "α", s * 0.08); label(d, (c + s * 0.15, c + s * 0.2), "n", s * 0.08)
    needle(d, c, s, -95, s * 0.43, s * 0.025); needle(d, c, s, 105, s * 0.43, s * 0.025)


def g_rpm(d, c, s, img):  # ITE-2T dual tacho, % rpm, two needles
    ticks(d, c, s, -135, 135, 22, s * 0.40, s * 0.45, s * 0.008)
    ticks(d, c, s, -135, 135, 11, s * 0.37, s * 0.45, s * 0.016)
    numbers(d, c, s, -135, 135, ["0", "", "20", "", "40", "", "60", "", "80", "", "100", ""], s * 0.31, s * 0.065)
    label(d, (c, c + s * 0.17), "ОБ/МИН %", s * 0.045, path=FONT_R)
    label(d, (c - s * 0.12, c + s * 0.05), "1", s * 0.07); label(d, (c + s * 0.12, c + s * 0.05), "2", s * 0.07)
    needle(d, c, s, 95, s * 0.42, s * 0.028); needle(d, c, s, 100, s * 0.36, s * 0.028, col=(240, 200, 60))


def g_egt(d, c, s, img):
    ticks(d, c, s, -135, 135, 18, s * 0.40, s * 0.45, s * 0.008)
    numbers(d, c, s, -135, 135, ["2", "4", "6", "8", "10"], s * 0.31, s * 0.07)
    d.arc((c - s * 0.45, c - s * 0.45, c + s * 0.45, c + s * 0.45), -90 + 110, -90 + 135, fill=(200, 40, 30), width=int(s * 0.03))
    label(d, (c, c + s * 0.17), "t°C ×100", s * 0.045, path=FONT_R)
    needle(d, c, s, 40, s * 0.42, s * 0.028); needle(d, c, s, 46, s * 0.36, s * 0.028, col=(240, 200, 60))


def g_fuel(d, c, s, img):
    ticks(d, c, s, -120, 120, 20, s * 0.40, s * 0.45, s * 0.008)
    numbers(d, c, s, -120, 120, ["0", "1", "2", "3", "4", "5"], s * 0.31, s * 0.075)
    label(d, (c, c + s * 0.17), "ТОПЛИВО", s * 0.05, path=FONT_R); label(d, (c, c - s * 0.15), "КГ ×1000", s * 0.04, path=FONT_R)
    needle(d, c, s, 50, s * 0.42, s * 0.03)


def g_clock(d, c, s, img):
    ticks(d, c, s, 0, 360, 60, s * 0.42, s * 0.45, s * 0.006)
    ticks(d, c, s, 0, 360, 12, s * 0.38, s * 0.45, s * 0.016)
    numbers(d, c, s, 30, 360, [str(i) for i in range(1, 13)], s * 0.32, s * 0.075)
    label(d, (c, c + s * 0.16), "АЧС-1", s * 0.04, path=FONT_R)
    needle(d, c, s, 300, s * 0.25, s * 0.04); needle(d, c, s, 60, s * 0.4, s * 0.025)


def g_small(title, lo, hi, unit, ang=40):
    def f(d, c, s, img):
        ticks(d, c, s, -120, 120, 10, s * 0.40, s * 0.45, s * 0.012)
        numbers(d, c, s, -120, 120, [str(lo), "", str((lo + hi) // 2), "", str(hi)], s * 0.30, s * 0.085)
        label(d, (c, c + s * 0.18), title, s * 0.06, path=FONT_R)
        if unit: label(d, (c, c - s * 0.15), unit, s * 0.05, path=FONT_R)
        needle(d, c, s, ang, s * 0.42, s * 0.04)
    return f


def g_adi(d, c, s, img):  # KPP mask: black surround with the bank scale; the attitude ball shows through the middle
    d.rectangle((0, 0, s, s), fill=(14, 14, 15))
    for ang in (-60, -45, -30, -20, -10, 10, 20, 30, 45, 60):
        a = math.radians(ang); r0 = s * (0.42 if abs(ang) in (30, 60) else 0.445)
        d.line((c + math.sin(a) * r0, c - math.cos(a) * r0, c + math.sin(a) * s * 0.49, c - math.cos(a) * s * 0.49), fill=(240, 240, 235), width=int(s * 0.012))
    d.polygon([(c, s * 0.085), (c - s * 0.025, s * 0.02), (c + s * 0.025, s * 0.02)], fill=(240, 240, 235))


def adi_ball(w=512, h=256):
    """Equirectangular attitude ball: u = azimuth (u 0.5 faces the pilot), v = pitch -90..90. Sky over ground, ladder every 10 deg."""
    img = Image.new("RGB", (w * SS, h * SS)); d = ImageDraw.Draw(img); W_, H_ = w * SS, h * SS
    sky, gnd = (88, 136, 186), (122, 82, 48)
    d.rectangle((0, 0, W_, H_ / 2), fill=sky); d.rectangle((0, H_ / 2, W_, H_), fill=gnd)
    d.line((0, H_ / 2, W_, H_ / 2), fill=(245, 245, 240), width=int(SS * 3))

    def y_of(p):
        return H_ / 2 - p / 90 * H_ / 2
    for p in range(-80, 90, 5):
        if p == 0:
            continue
        stretch = 1 / max(0.35, math.cos(math.radians(p)))
        half = (16 if p % 10 == 0 else 8) / 360 * W_ * stretch
        col = (245, 245, 240) if p > 0 else (240, 230, 210)
        d.line((W_ / 2 - half, y_of(p), W_ / 2 + half, y_of(p)), fill=col, width=int(SS * (2 if p % 10 == 0 else 1.2)))
        if p % 10 == 0:
            for sx in (-1, 1):
                label(d, (W_ / 2 + sx * (half + 9 / 360 * W_ * stretch), y_of(p)), str(abs(p)), SS * 9, col, FONT_N)
    for az in range(0, 360, 30):  # azimuth marks on the horizon
        x = (az / 360 + 0.5) % 1 * W_
        d.line((x, H_ / 2 - SS * 5, x, H_ / 2 + SS * 5), fill=(245, 245, 240), width=SS * 2)
    return down(img, w, h)


def g_hsi(d, c, s, img):  # PNP: compass card, course arrow
    ticks(d, c, s, 0, 360, 72, s * 0.41, s * 0.45, s * 0.006)
    ticks(d, c, s, 0, 360, 36, s * 0.38, s * 0.45, s * 0.012)
    for i in range(12):
        a = i * 30; t = {0: "С", 9: "З", 18: "Ю", 27: "В"}.get(i * 3, str(i * 3)) if i % 3 == 0 else str(i * 3)
        label(d, (c + math.sin(math.radians(a)) * s * 0.33, c - math.cos(math.radians(a)) * s * 0.33), t, s * 0.06, path=FONT_N)
    for a in range(0, 360, 45):   # inner ring marks
        ra = math.radians(a)
        d.line((c + math.sin(ra) * s * 0.2, c - math.cos(ra) * s * 0.2, c + math.sin(ra) * s * 0.24, c - math.cos(ra) * s * 0.24), fill=(160, 160, 155), width=int(s * 0.008))


def g_spo(d, c, s, img):  # SPO-15 RWR: aircraft plan with sector lamps, power bar
    d.rectangle((0, 0, s, s), fill=(18, 18, 20))
    lay = LAYOUT["spo"] = {"sectors": [], "power": [], "types": []}
    for i in range(10):  # power ladder
        d.rectangle((s * 0.1 + i * s * 0.08, s * 0.08, s * 0.16 + i * s * 0.08, s * 0.14), fill=(70, 50, 20))
        lay["power"].append([0.13 + i * 0.08, 0.11, 0.06, 0.06])
    # aircraft outline
    w = (190, 190, 180)
    d.polygon([(c, s * 0.3), (c + s * 0.03, s * 0.42), (c + s * 0.25, s * 0.55), (c + s * 0.25, s * 0.6), (c + s * 0.04, s * 0.56), (c + s * 0.03, s * 0.7),
               (c + s * 0.1, s * 0.76), (c - s * 0.1, s * 0.76), (c - s * 0.03, s * 0.7), (c - s * 0.04, s * 0.56), (c - s * 0.25, s * 0.6), (c - s * 0.25, s * 0.55), (c - s * 0.03, s * 0.42)], outline=w, width=int(s * 0.008))
    for a in (-150, -120, -60, -30, 30, 60, 120, 150):  # sector lamps (bearing from the nose, degrees)
        x, y = c + math.sin(math.radians(a)) * s * 0.33, c + s * 0.05 - math.cos(math.radians(a)) * s * 0.33
        d.ellipse((x - s * 0.03, y - s * 0.03, x + s * 0.03, y + s * 0.03), fill=(80, 40, 20), outline=(140, 140, 130), width=int(s * 0.004))
        lay["sectors"].append([a, x / s, y / s, 0.06])
    for i, t in enumerate(["П", "З", "Н", "Х", "F", "О"]):  # threat type lamps
        x = s * 0.15 + i * s * 0.14
        d.rectangle((x, s * 0.86, x + s * 0.1, s * 0.94), fill=(40, 38, 20), outline=(120, 120, 110), width=int(s * 0.004))
        label(d, (x + s * 0.05, s * 0.9), t, s * 0.05, (150, 140, 90))
        lay["types"].append([t, x / s + 0.05, 0.9, 0.1, 0.08])


def g_radar_frame(d, c, s, img):  # bezel graphics around the tac screen (the screen itself is the game's render texture)
    d.rectangle((0, 0, s, s), fill=(26, 27, 28))
    d.rounded_rectangle((s * 0.08, s * 0.08, s * 0.92, s * 0.92), radius=s * 0.06, fill=(8, 10, 9), outline=(70, 72, 72), width=int(s * 0.012))
    for i in range(5):  # soft keys
        y = s * 0.2 + i * s * 0.15
        d.rectangle((s * 0.015, y, s * 0.06, y + s * 0.07), fill=(60, 62, 62)); d.rectangle((s * 0.94, y, s * 0.985, y + s * 0.07), fill=(60, 62, 62))


GAUGES = [("adi", 384, g_adi), ("hsi", 384, g_hsi), ("asi", 256, g_asi), ("alt", 256, g_alt), ("vsi", 256, g_vsi), ("aoa", 256, g_aoa),
          ("rpm", 256, g_rpm), ("egt", 256, g_egt), ("fuel", 256, g_fuel), ("clock", 256, g_clock), ("spo", 256, g_spo),
          ("oxy", 192, g_small("КИСЛОРОД", 0, 150, "КГС/СМ²", -30)), ("hyd", 192, g_small("ГИДРО", 0, 300, "КГС/СМ²", 30)),
          ("cabin", 192, g_small("ВЫСОТА КАБ", 0, 20, "КМ", -60)), ("volt", 192, g_small("V", 0, 30, "", 35)),
          ("radalt", 192, g_small("РВ", 0, 1500, "М", -80)), ("brake", 192, g_small("ТОРМ", 0, 100, "%", -100))]
for name, size, fn in GAUGES:
    put("g_" + name, dial(size, fn, bezel=name not in ("spo", "adi"), round_=name != "spo"), (0, 200))   # glossy glass over the faces
put("adi_ball", adi_ball(), (0, 160))


def glow(col, size=64):
    """Lit lamp lens: bright centre, coloured rim (emissive at runtime)."""
    S_ = size * SS
    img = Image.new("RGB", (S_, S_), tuple(int(c * 0.8) for c in col)); d = ImageDraw.Draw(img)
    for k in range(12, 0, -1):
        f = k / 12; cc = tuple(min(255, int(c + (255 - c) * (1 - f) * 0.7)) for c in col)
        d.ellipse((S_ / 2 - S_ * f / 2, S_ / 2 - S_ * f / 2, S_ / 2 + S_ * f / 2, S_ / 2 + S_ * f / 2), fill=cc)
    return down(img, size, size)


for nm, col in (("red", (255, 40, 25)), ("amber", (255, 160, 20)), ("green", (60, 255, 90))):
    put("lamp_lit_" + nm, glow(col), (0, 200))

# ---------------------------------------------------------------- switch panels (black plates, white legends)
WORDS = ["ВКЛ", "ОТКЛ", "РЛС", "САУ", "ОСВЕЩ", "ПОДСВ", "АККУМ", "ГЕН", "ТОПЛ", "НАСОС", "ЗАПУСК", "ФОРС", "ОБОГР", "ПВД", "СТЕКЛО",
         "КИСЛОР", "РАДИО", "КОД", "РСБН", "ПРМ", "ОТВЕТ", "БЛОК", "СБРОС", "ТЕСТ", "КОНТР", "АРК", "ОРУЖ", "ФАРА", "АНО", "МАЯК",
         "ПЗУ", "КАНАЛ", "ГРОМК", "ЛЕВ", "ПРАВ", "ОСН", "РЕЗ", "АВАР", "ПОЖАР", "КОНД", "ДЕМПФ", "СПО", "ТАНК", "ЗАЛП", "ЛОК"]


def toggle(d, x, y, s, up=True, lever=True):
    """Toggle switch: bezel and nut; the lever is drawn only when no 3D lever stands on it (lever=False for console plates)."""
    r = s * 0.5
    d.ellipse((x - r, y - r, x + r, y + r), fill=(150, 150, 150), outline=(60, 60, 60), width=max(1, int(s * 0.08)))
    d.ellipse((x - r * 0.55, y - r * 0.55, x + r * 0.55, y + r * 0.55), fill=(70, 70, 70))
    if not lever:
        return
    ty = y - s * 0.9 if up else y + s * 0.9
    d.line((x, y, x, ty), fill=(205, 205, 200), width=max(2, int(s * 0.28)))
    d.ellipse((x - s * 0.22, ty - s * 0.22, x + s * 0.22, ty + s * 0.22), fill=(225, 225, 220))


def knob(d, x, y, s, pointer=True):
    d.ellipse((x - s, y - s, x + s, y + s), fill=(30, 30, 30), outline=(90, 90, 90), width=max(1, int(s * 0.15)))
    if pointer:
        d.line((x, y, x, y - s * 0.9), fill=(230, 230, 225), width=max(2, int(s * 0.2)))
    for a in range(-120, 121, 40):
        ra = math.radians(a)
        d.line((x + math.sin(ra) * s * 1.2, y - math.cos(ra) * s * 1.2, x + math.sin(ra) * s * 1.45, y - math.cos(ra) * s * 1.45), fill=(220, 220, 215), width=max(1, int(s * 0.1)))


def lamp(d, x, y, s, col):
    d.ellipse((x - s, y - s, x + s, y + s), fill=tuple(int(c * 0.55) for c in col), outline=(110, 110, 110), width=max(1, int(s * 0.18)))
    d.ellipse((x - s * 0.4, y - s * 0.5, x, y - s * 0.1), fill=tuple(min(255, int(c * 0.9)) for c in col))


def switch_panel(w, h, seed, base="black", rows=None, name=None):
    """Switch plate. With a name, toggles and knobs are left for 3D parts (cockpit_build.py stands real levers and knobs on them):
    LAYOUT[name] lists them as [kind, x, y, size, state] in fractions of the cell (y down), size = radius / cell width."""
    rnd = random.Random(seed)
    parts3d = LAYOUT.setdefault(name, []) if name else None
    img = canvas(w, h, PAINT[base]); d = ImageDraw.Draw(img); s = SS
    # sub-plates with white group outlines and screws
    nx = max(1, w // 128); ny = max(1, h // 128)
    cw, ch = w * s / nx, h * s / ny
    for gy in range(ny):
        for gx in range(nx):
            x0, y0 = gx * cw, gy * ch
            d.rectangle((x0 + 3 * s, y0 + 3 * s, x0 + cw - 3 * s, y0 + ch - 3 * s), outline=(70, 72, 74), width=s)
            for sx, sy in ((x0 + 8 * s, y0 + 8 * s), (x0 + cw - 8 * s, y0 + 8 * s), (x0 + 8 * s, y0 + ch - 8 * s), (x0 + cw - 8 * s, y0 + ch - 8 * s)):
                d.ellipse((sx - 2.5 * s, sy - 2.5 * s, sx + 2.5 * s, sy + 2.5 * s), fill=(110, 110, 108)); d.line((sx - 2 * s, sy, sx + 2 * s, sy), fill=(50, 50, 50), width=s)
            kind = rnd.choice(["toggles", "toggles", "knobs", "mixed", "lamps"])
            label(d, (x0 + cw / 2, y0 + 18 * s), rnd.choice(WORDS), 11 * s, (225, 225, 218), FONT_R)
            n = rnd.randint(2, 4)
            for i in range(n):
                x = x0 + cw * (i + 0.5) / n; y = y0 + ch * 0.55
                k = kind if kind != "mixed" else rnd.choice(["toggles", "knobs", "lamps"])
                if k == "toggles":
                    up = rnd.random() < 0.6
                    toggle(d, x, y, 9 * s, up, lever=parts3d is None)
                    if parts3d is not None: parts3d.append(["toggle", x / (w * s), y / (h * s), 4.5 / w, 1 if up else -1])
                    label(d, (x, y0 + ch - 20 * s), rnd.choice(WORDS), 8 * s, (215, 215, 208), FONT_R)
                elif k == "knobs":
                    ang = rnd.choice((-80, -40, 0, 40, 80))
                    knob(d, x, y, 10 * s, pointer=parts3d is None)
                    if parts3d is not None: parts3d.append(["knob", x / (w * s), y / (h * s), 10 / w, ang])
                    label(d, (x, y0 + ch - 18 * s), rnd.choice(WORDS), 8 * s, (215, 215, 208), FONT_R)
                else:
                    lamp(d, x, y, 7 * s, rnd.choice([PAINT["green_lamp"], PAINT["amber"], (200, 50, 40), (230, 230, 230)]))
                    label(d, (x, y + 16 * s), rnd.choice(WORDS), 7 * s, (215, 215, 208), FONT_R)
    return down(img, w, h)


def breaker_panel(w=512, h=128, seed=77):
    """Circuit-breaker panel (АЗС): three rows of breakers, each a white collar with a legend under it. The breaker buttons are
    3D (cockpit_build.py): LAYOUT["sp_cb"] lists them like the switches, kind "breaker"."""
    rnd = random.Random(seed)
    img = canvas(w, h, PAINT["black"]); d = ImageDraw.Draw(img); s = SS
    d.rectangle((3 * s, 3 * s, (w - 3) * s, (h - 3) * s), outline=(70, 72, 74), width=s)
    lay = LAYOUT["sp_cb"] = []
    label(d, (w / 2 * s, 12 * s), "АЗС", 10 * s, (225, 225, 218), FONT)
    cols, rows = 12, 3
    for r_ in range(rows):
        for c_ in range(cols):
            x = (24 + c_ * (w - 48) / (cols - 1)) * s; y = (34 + r_ * 32) * s
            d.ellipse((x - 7 * s, y - 7 * s, x + 7 * s, y + 7 * s), fill=(200, 200, 194), outline=(90, 90, 90), width=s)
            d.ellipse((x - 4.5 * s, y - 4.5 * s, x + 4.5 * s, y + 4.5 * s), fill=(30, 30, 30))
            label(d, (x, y + 12 * s), rnd.choice(WORDS), 6 * s, (215, 215, 208), FONT_R)
            lay.append(["breaker", x / (w * s), y / (h * s), 4.5 / w, 1 if rnd.random() < 0.9 else 0])
    for sx, sy in ((8, 8), (w - 8, 8), (8, h - 8), (w - 8, h - 8)):
        d.ellipse(((sx - 2.5) * s, (sy - 2.5) * s, (sx + 2.5) * s, (sy + 2.5) * s), fill=(110, 110, 108))
    return down(img, w, h)


put("sp_cb", breaker_panel(), (0, 90))

for i, (name, w, h) in enumerate([("sp_lc1", 384, 256), ("sp_lc2", 256, 256), ("sp_lc3", 256, 128), ("sp_rc1", 384, 256), ("sp_rc2", 256, 256),
                                   ("sp_rc3", 256, 128), ("sp_pl", 256, 256), ("sp_pr", 256, 256), ("sp_ped", 256, 384), ("sp_wl", 128, 256),
                                   ("sp_wr", 128, 256), ("sp_aft", 256, 128)]):
    put(name, switch_panel(w, h, 100 + i, name=name), (0, 90))

# caution / warning panel: every caption has a job (MiG29Instruments plugin). (id, caption, colour)
RED, AMB, GRN = (190, 40, 28), (220, 140, 20), (50, 170, 70)
CAUTION = [("fire_l", "ПОЖАР Л", RED), ("fire_r", "ПОЖАР П", RED), ("lowalt", "ОПАСН ВЫС", RED), ("aoa", "ВЫХОД α", RED),
           ("overg", "ПЕРЕГРУЗ", RED), ("missile", "РАКЕТА", RED),
           ("fuel_low", "ОСТАТОК", AMB), ("fuel_res", "РЕЗЕРВ", RED), ("gen_l", "ГЕН Л", AMB), ("gen_r", "ГЕН П", AMB),
           ("hyd", "ГИДРО", AMB), ("lock", "ЗАХВАТ", RED),
           ("pump", "НАСОС", AMB), ("oil", "МАСЛО", AMB), ("vib", "ВИБРАЦ", AMB), ("canopy", "ФОНАРЬ", AMB),
           ("gear", "ШАССИ", AMB), ("speed", "СКОРОСТЬ", RED),
           ("ptb", "ПТБ", AMB), ("flaps", "ЩИТКИ", GRN), ("brake", "ТОРМОЗ", GRN), ("fod", "ПЗУ", GRN),
           ("ab", "ФОРСАЖ", GRN), ("master", "ПРОВЕРЬ", AMB)]
LAYOUT["caution"] = {"cols": 6, "rows": 4, "ids": [c[0] for c in CAUTION]}


def caution(w, h, cols, rows, lit):
    img = canvas(w, h, (20, 20, 22)); d = ImageDraw.Draw(img); s = SS
    tw, th = w * s / cols, h * s / rows
    for k, (cid, cap, col) in enumerate(CAUTION):
        i, j = k % cols, k // cols
        x0, y0 = i * tw, j * th
        bg = tuple(min(255, int(c * 1.05)) for c in col) if lit else tuple(int(c * 0.3) for c in col)
        fg = (255, 250, 235) if lit else tuple(int(c * 0.85) for c in col)
        d.rectangle((x0 + 2 * s, y0 + 2 * s, x0 + tw - 2 * s, y0 + th - 2 * s), fill=bg, outline=(60, 60, 60), width=s)
        label(d, (x0 + tw / 2, y0 + th / 2), cap, th * 0.32, fg, FONT)
    return down(img, w, h)


put("caution", caution(512, 160, 6, 4, False), (0, 220))
put("caution_lit", caution(512, 160, 6, 4, True), (0, 220))

# landing gear indicator plate: three lamps (nose, left, right)
img = canvas(256, 128, PAINT["black"]); d = ImageDraw.Draw(img)
label(d, (128 * SS, 18 * SS), "ШАССИ", 14 * SS, (225, 225, 218), FONT_R)
LAYOUT["gear"] = []
for gid, (x, y) in (("nose", (128, 52)), ("left", (70, 92)), ("right", (186, 92))):
    d.ellipse(((x - 16) * SS, (y - 16) * SS, (x + 16) * SS, (y + 16) * SS), fill=(30, 45, 30), outline=(110, 110, 110), width=2 * SS)
    LAYOUT["gear"].append([gid, x / 256, y / 128, 32 / 256])
put("gear_plate", down(img, 256, 128))

put("radar_frame", dial(256, g_radar_frame, bezel=False, round_=False), (0, 120))

# placards / stencils
img = canvas(512, 64, PAINT["turq"]); d = ImageDraw.Draw(img)
label(d, (256 * SS, 32 * SS), "МиГ-29   ·   9.12   ·   ФОРСАЖ ↑   МАЛЫЙ ГАЗ ↓", 22 * SS, (20, 30, 30), FONT)
put("placard", down(img, 512, 64))

# cockpit side wall (one cell for the whole wall, mapped by station z and height y): painted panels, seams, rivets, access hatches
def side_wall(w=512, h=192, seed=41):
    rnd = random.Random(seed)
    base = noise_fill(w, h, PAINT["turq"], 5, 0.8, seed)
    img = base.resize((w * SS, h * SS)); d = ImageDraw.Draw(img); S = SS
    seam = tuple(int(c * 0.62) for c in PAINT["turq"]); rivet = tuple(int(c * 0.78) for c in PAINT["turq"])
    xs = [0, 70, 150, 236, 318, 404, 512]
    for x in xs[1:-1]:
        d.line((x * S, 0, x * S, h * S), fill=seam, width=2 * S)
        for y in range(4, h, 8):
            d.ellipse(((x - 4) * S - S, y * S - S, (x - 4) * S + S, y * S + S), fill=rivet)
            d.ellipse(((x + 4) * S - S, y * S - S, (x + 4) * S + S, y * S + S), fill=rivet)
    for y in (64, 132):
        d.line((0, y * S, w * S, y * S), fill=seam, width=2 * S)
        for x in range(4, w, 8):
            d.ellipse((x * S - S, (y - 4) * S - S, x * S + S, (y - 4) * S + S), fill=rivet)
    for k in range(4):   # access hatches with screws
        x0 = rnd.choice(xs[:-1]) + 12; y0 = rnd.choice((12, 76, 140)); ww = rnd.randint(30, 50); hh = rnd.randint(30, 44)
        d.rectangle((x0 * S, y0 * S, (x0 + ww) * S, (y0 + hh) * S), outline=seam, width=2 * S)
        for sx, sy in ((x0 + 4, y0 + 4), (x0 + ww - 4, y0 + 4), (x0 + 4, y0 + hh - 4), (x0 + ww - 4, y0 + hh - 4)):
            d.ellipse(((sx - 2.5) * S, (sy - 2.5) * S, (sx + 2.5) * S, (sy + 2.5) * S), fill=(120, 124, 122))
    label(d, (110 * S, 100 * S), "НЕ НАСТУПАТЬ", 8 * S, seam, FONT)
    return down(img, w, h)


put("wall", side_wall())
img = canvas(256, 64, PAINT["yellow"]); d = ImageDraw.Draw(img)
label(d, (128 * SS, 32 * SS), "КАТАПУЛЬТ", 28 * SS, (20, 20, 20), FONT)
put("eject_label", down(img, 256, 64))

# ---------------------------------------------------------------- compartment behind the seat (cockpit_build.py: rear bay)
# Soviet oxygen bottles are painted light blue
put("paint_o2", noise_fill(128, 128, (92, 142, 190), 5, 0.3, seed=7), (0, 90))


def avionics_front(w=256, h=128, seed=3):
    """Front face of an avionics block: crinkle-black case, two carry handles, round connectors, data plate, screws."""
    rnd = random.Random(seed)
    img = noise_fill(w, h, (44, 47, 46), 7, 0.4, seed).resize((w * SS, h * SS)); d = ImageDraw.Draw(img); S = SS
    d.rectangle((3 * S, 3 * S, (w - 3) * S, (h - 3) * S), outline=(78, 80, 80), width=2 * S)
    for x in (16, w - 16):   # carry handles
        d.rounded_rectangle(((x - 6) * S, 18 * S, (x + 6) * S, (h - 18) * S), radius=6 * S, outline=(170, 172, 170), width=3 * S)
    for i, cx in enumerate((62, 104, 146)):   # MIL round connectors with their caps
        cy = 88
        d.ellipse(((cx - 15) * S, (cy - 15) * S, (cx + 15) * S, (cy + 15) * S), fill=(120, 122, 118), outline=(60, 60, 60), width=2 * S)
        d.ellipse(((cx - 10) * S, (cy - 10) * S, (cx + 10) * S, (cy + 10) * S), fill=(30, 30, 30))
        for k in range(6):
            a = k / 6 * math.tau; d.ellipse(((cx + math.cos(a) * 5.5 - 1.5) * S, (cy + math.sin(a) * 5.5 - 1.5) * S, (cx + math.cos(a) * 5.5 + 1.5) * S, (cy + math.sin(a) * 5.5 + 1.5) * S), fill=(190, 170, 90))
    d.rectangle((40 * S, 16 * S, 200 * S, 56 * S), fill=(205, 205, 196), outline=(90, 90, 86), width=S)   # data plate
    label(d, (120 * S, 28 * S), "БЛОК " + rnd.choice(["Н019-01", "СУО-29", "СПО-15", "А-323"]), 11 * S, (25, 25, 25), FONT)
    label(d, (120 * S, 45 * S), f"№ {rnd.randint(1000, 9999)}   1987", 9 * S, (40, 40, 40), FONT_R)
    lamp(d, 196 * S, 88 * S, 6 * S, PAINT["green_lamp"])
    for sx, sy in ((10, 10), (w - 10, 10), (10, h - 10), (w - 10, h - 10)):
        d.ellipse(((sx - 3) * S, (sy - 3) * S, (sx + 3) * S, (sy + 3) * S), fill=(130, 130, 126)); d.line(((sx - 2) * S, sy * S, (sx + 2) * S, sy * S), fill=(40, 40, 40), width=S)
    return down(img, w, h)


put("avionics_a", avionics_front(seed=3), (40, 90))
put("avionics_b", avionics_front(seed=11), (40, 90))


def aft_bulkhead(w=256, h=128, seed=5):
    """Rear bulkhead under the canopy: dark grey panel, rivet lines, an access hatch and a canopy warning stencil."""
    base = noise_fill(w, h, PAINT["dgrey"], 5, 0.7, seed)
    img = base.resize((w * SS, h * SS)); d = ImageDraw.Draw(img); S = SS
    seam = tuple(int(c * 0.6) for c in PAINT["dgrey"]); rivet = tuple(int(c * 1.5) for c in PAINT["dgrey"])
    for x in (64, 192):
        d.line((x * S, 0, x * S, h * S), fill=seam, width=2 * S)
        for y in range(4, h, 8):
            d.ellipse(((x + 4) * S - S, y * S - S, (x + 4) * S + S, y * S + S), fill=rivet)
    for x in range(4, w, 8):
        d.ellipse((x * S - S, 6 * S - S, x * S + S, 6 * S + S), fill=rivet)
    d.rectangle((84 * S, 44 * S, 172 * S, 104 * S), outline=seam, width=2 * S)            # access hatch
    for sx, sy in ((90, 50), (166, 50), (90, 98), (166, 98)):
        d.ellipse(((sx - 2.5) * S, (sy - 2.5) * S, (sx + 2.5) * S, (sy + 2.5) * S), fill=(120, 122, 120))
    d.rectangle((92 * S, 16 * S, 164 * S, 34 * S), fill=PAINT["yellow"])                   # stencil: canopy, keep clear
    label(d, (128 * S, 25 * S), "ФОНАРЬ", 10 * S, (20, 20, 20), FONT)
    label(d, (128 * S, 116 * S), "НЕ НАСТУПАТЬ", 7 * S, seam, FONT)
    return down(img, w, h)


put("aft_bulk", aft_bulkhead())
assert pk.y + pk.row <= W, f"cockpit atlas overflow: {pk.y + pk.row} > {W}"

atlas.save(os.path.join(OUT, "cockpit_atlas.png"))
metal.save(os.path.join(OUT, "cockpit_metallic.png"))
json.dump({"size": W, "rects": rects, "layout": LAYOUT}, open(os.path.join(OUT, "cockpit_atlas.json"), "w"), indent=1)
print(f"cockpit atlas: {len(rects)} cells, packed to y={pk.y + pk.row}")
