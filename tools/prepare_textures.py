"""Turn the Sketchfab model's 4K textures into the mod's 2K set in <project>/MiG29Source.

python tools/prepare_textures.py <sketchfab textures dir> <project>/MiG29Source

Writes mig29_basecolor.png, mig29_normal.png, mig29_metallic.png (game convention: RGB = metal, A = 1 - roughness)
and copies the loading screens from assets/loading (the damage maps come from tools/damage_textures.py, run by the build).
Run tools/livery_desert.py afterwards (it needs mig29_mesh.json from blender/export_mig29.py).
"""
import os, shutil, sys
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

here = os.path.dirname(os.path.abspath(__file__))
for f in ("loading_mig29_bank.png", "loading_mig29_climb.png", "loading_mig29_digital.png", "loading_mig29_display.png"):
    shutil.copy(os.path.join(here, "..", "assets", "loading", f), dst)
print("textures written to", dst)
