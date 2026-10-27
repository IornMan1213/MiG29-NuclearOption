"""The paint's brightness detail (panel lines, rivets, weathering) for the livery generators, without the base livery's camouflage.

The liveries recolour the base texture's paint and multiply in its detail, as the paint's brightness relative to its
surroundings. That used to be relative to a 10 px blur of all the paint, which flattened the base's two-tone grey camouflage
inside big patches but kept a light and a dark rim along every patch edge, and the whole tone of patches narrower than the blur:
on every livery but the default the old camouflage showed through as triangles of lighter and darker skin, which look like bad
normals in game ("normals issue", player reports). Here each paint texel is divided by the local mean of its own camouflage
tone (a normalised blur within the tone), so the patch edges leave nothing behind.

The two tones: the base paint's brightness, median-filtered past panel lines and rivets, has peaks near 0.52 and 0.63-0.69
with a gap at 0.56-0.58."""
import numpy as np
from PIL import Image, ImageFilter

TONE_SPLIT = 0.565


def _blur(a, px):
    return np.asarray(Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(px)), np.float32) / 255


def detail_shade(lum, paint, px=10):
    """lum: paint brightness 0-1 (HxW), paint: the texels that are recoloured. Returns the factor to multiply the new colour by."""
    med = np.asarray(Image.fromarray((np.clip(lum, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(9)), np.float32) / 255
    dark = paint & (med < TONE_SPLIT)
    shade = np.ones_like(lum, dtype=np.float32)
    for tone in (dark, paint & ~dark):
        local = _blur(np.where(tone, lum, 0), px) / np.maximum(_blur(tone.astype(np.float32), px), 1e-3)
        shade = np.where(tone, lum / np.maximum(local, 1e-3), shade)
    return np.clip(shade, 0.6, 1.25)
