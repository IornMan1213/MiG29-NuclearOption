"""Make the skin's normal map use Unity's convention (green = +Y, "OpenGL"), converting it in place if it does not.

The Sketchfab model's Normal.png is DirectX-style (green = -Y), though the game reads every normal map as OpenGL-style. With
it, panel lines and rivets were lit from the wrong side, top for bottom across the texture, on every livery of the 9.12 and
the SMT ("normals issue", player reports). tools/prepare_textures.py copies the map as it comes; this runs on
every build before tools/damage_textures.py (which blends the panel lines into the damaged-skin map), so a source prepared
before the fix is converted on its next build. Idempotent.

The convention is measured, not assumed: a tangent-space normal map is the slope of a height field, and a slope field has no
curl. Read with the wrong green sign it picks up curl (twice the height's mixed second derivative). The game's own maps
(multirole1_skin_n, CAS1_*) all read 1.4-1.8x lower as OpenGL; the MiG's Normal.png reads 2.2x lower as DirectX.

python tools/normal_map_y.py <MiG29Source folder> [--check]"""
import os, sys
import numpy as np
from PIL import Image

MARGIN = 1.25   # convert only on a clear reading: DirectX curl at least this much lower than OpenGL curl


def curl_ratio(rgb):
    """mean curl read as OpenGL / read as DirectX, over the texels with detail (rows run down the image)"""
    a = rgb.astype(np.float64) / 127.5 - 1.0
    r, g = a[..., 0], a[..., 1]
    detail = (np.abs(r) + np.abs(g)) > 0.05
    if detail.mean() < 0.01:
        return 1.0   # flat: nothing to read
    curl = lambda fx, fy: np.abs(np.gradient(fy, axis=1) - np.gradient(fx, axis=0))[detail].mean()
    return curl(r, -g) / curl(r, g)


path = os.path.join(sys.argv[1], "mig29_normal.png")
img = Image.open(path)
rgb = np.asarray(img.convert("RGB"))
ratio = curl_ratio(rgb)
if ratio < MARGIN:
    print(f"[normal map] {os.path.basename(path)}: OpenGL-style (curl ratio {ratio:.2f})")
elif "--check" in sys.argv:
    print(f"[normal map] {os.path.basename(path)}: DirectX-style (curl ratio {ratio:.2f}): would convert"); sys.exit(1)
else:
    out = rgb.copy()
    out[..., 1] = 255 - out[..., 1]
    Image.fromarray(out, "RGB").save(path)
    print(f"[normal map] {os.path.basename(path)}: was DirectX-style (curl ratio {ratio:.2f}), green flipped to Unity's convention"
          f" (now {curl_ratio(out):.2f})")
