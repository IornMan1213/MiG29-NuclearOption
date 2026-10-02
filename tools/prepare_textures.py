"""Turn the Sketchfab model's 4K textures into the mod's 2K set in <project>/MiG29Source.

python tools/prepare_textures.py <sketchfab textures dir> <project>/MiG29Source

Writes mig29_basecolor.png, mig29_normal.png, mig29_metallic.png (game convention: RGB = metal, A = 1 - roughness),
mig29_basecolor_dmg.png (procedural scorch for destroyed parts) and copies the loading screens from assets/loading.
Run tools/livery_desert.py afterwards (it needs mig29_mesh.json from blender/export_mig29.py).
"""
import os, shutil, sys
import numpy as np
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
os.makedirs(dst, exist_ok=True)
N = 2048
load = lambda n: Image.open(os.path.join(src, n)).convert("RGB")

base = load("BaseColor.png").resize((N, N), Image.LANCZOS); base.save(os.path.join(dst, "mig29_basecolor.png"))
load("Normal.png").resize((N, N), Image.LANCZOS).save(os.path.join(dst, "mig29_normal.png"))
rough = load("Metallic-Roughness@channels=G.png").split()[1].resize((N, N), Image.LANCZOS)
met = load("Metallic-Roughness@channels=B.png").split()[2].resize((N, N), Image.LANCZOS)
Image.merge("RGBA", (met, met, met, rough.point(lambda v: 255 - v))).save(os.path.join(dst, "mig29_metallic.png"))

# scorched variant: darkened, soot blotches, chipped bare-metal flecks
a = np.asarray(base).astype(np.float32) / 255
rng = np.random.default_rng(29)


def noise(scale):
    small = rng.random((max(N // scale, 2), max(N // scale, 2))).astype(np.float32)
    return np.asarray(Image.fromarray((small * 255).astype(np.uint8)).resize((N, N), Image.BICUBIC)).astype(np.float32) / 255


n = 0.6 * noise(64) + 0.3 * noise(16) + 0.1 * noise(4)
soot = np.clip((n - 0.30) * 2.5, 0, 1)[..., None]
s = a * (0.55 - 0.35 * soot) + np.array([0.04, 0.035, 0.03]) * soot
chip = np.clip((0.7 * noise(48) + 0.3 * noise(12) - 0.80) * 10, 0, 1)[..., None]
s = s * (1 - chip) + np.array([0.48, 0.48, 0.5]) * chip
Image.fromarray((np.clip(s, 0, 1) * 255).astype(np.uint8)).save(os.path.join(dst, "mig29_basecolor_dmg.png"))

here = os.path.dirname(os.path.abspath(__file__))
for f in ("loading_mig29_bank.png", "loading_mig29_climb.png"):
    shutil.copy(os.path.join(here, "..", "assets", "loading", f), dst)
print("textures written to", dst)
