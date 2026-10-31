"""A livery kit for painting the jet outside Blender: the whole jet assembled as one OBJ (body, moving surfaces at rest, missile
rails, closed gear doors on the skin; the canopy as glass), with the game's own UV layout and its skin texture, plus a UV template
for 2D editors. Paint the 2048 x 2048 skin texture in any 3D painting app (Substance Painter, ArmorPaint, 3DCoat...) or over the
template in GIMP / Photoshop; the texture maps onto the jet exactly as in game.

The mesh dump is in Unity's frame (left-handed: x right, y up, z forward). OBJ is right-handed, so x is negated (normals too) and each
triangle's winding reversed: the jet is not mirrored in the painting app, and lettering painted there reads the right way in game.
The SMT's raised spine (hump.json, applied by the builder) is applied here too.

python livery_kit.py <source dir> <out dir> <name> [texture png]   (texture: the source's mig29_basecolor.png by default)"""
import json, os, shutil, sys
import numpy as np
from PIL import Image, ImageDraw

src, out, name = sys.argv[1], sys.argv[2], sys.argv[3]
tex = sys.argv[4] if len(sys.argv) > 4 else os.path.join(src, "mig29_basecolor.png")
SKIN = ("body", "aileron_L", "aileron_R", "flap_L", "flap_R", "rudder_L", "rudder_R", "stab_L", "stab_R", "rails", "gear_doors_closed")
GLASS = ("canopy",)

mesh = json.load(open(os.path.join(src, "mig29_mesh.json")))
parts = {p["name"]: p for p in mesh["parts"]}
hump = os.path.join(src, "hump.json")
hump = json.load(open(hump)) if os.path.exists(hump) else None

os.makedirs(out, exist_ok=True)
tex_name = f"{name}_basecolor.png"
shutil.copyfile(tex, os.path.join(out, tex_name))
size = Image.open(tex).size

obj, uvt = [f"# {name}: livery kit (tools/livery_kit.py). Right-handed, Y up, metres; UVs as in game.", f"mtllib {name}.mtl"], []
vbase = tbase = nbase = 1
tris_skin = 0
for pname in SKIN + GLASS:
    p = parts.get(pname)
    if p is None: continue
    V = np.array(p["vertices"], float).reshape(-1, 3); N = np.array(p["normals"], float).reshape(-1, 3)
    UV = np.array(p["uvs"], float).reshape(-1, 2); T = np.array(p["triangles"], int).reshape(-1, 3)
    if pname == "body" and hump is not None:
        idx = np.array(hump["index"]); V[idx] = np.array(hump["pos"]).reshape(-1, 3); N[idx] = np.array(hump["nrm"]).reshape(-1, 3)
    if p.get("pivot"): V = V + np.array(p["pivot"], float)
    V[:, 0] *= -1; N[:, 0] *= -1                       # Unity (left-handed) -> OBJ (right-handed)
    obj.append(f"o {pname}")
    obj.append(f"usemtl {'glass' if pname in GLASS else 'skin'}")
    obj += [f"v {x:.5f} {y:.5f} {z:.5f}" for x, y, z in V]
    obj += [f"vt {u:.6f} {v:.6f}" for u, v in UV]
    obj += [f"vn {x:.4f} {y:.4f} {z:.4f}" for x, y, z in N]
    for a, b, c in T:                                   # winding reversed with the mirror
        obj.append(f"f {a + vbase}/{a + tbase}/{a + nbase} {c + vbase}/{c + tbase}/{c + nbase} {b + vbase}/{b + tbase}/{b + nbase}")
    if pname in SKIN:
        uvt.append(UV[T]); tris_skin += len(T)
    vbase += len(V); tbase += len(UV); nbase += len(N)
open(os.path.join(out, f"{name}.obj"), "w").write("\n".join(obj) + "\n")
open(os.path.join(out, f"{name}.mtl"), "w").write(
    f"# {name}: the skin and the canopy glass\nnewmtl skin\nKd 1 1 1\nKs 0.1 0.1 0.1\nmap_Kd {tex_name}\n\n"
    "newmtl glass\nKd 0.35 0.55 0.6\nd 0.35\nTr 0.65\n")

# UV template: every skin triangle's edges, on a transparent image the texture's size (a layer for 2D editors), and over the texture
W, H = size
lines = Image.new("RGBA", (W, H), (0, 0, 0, 0)); dr = ImageDraw.Draw(lines)
for tri in np.concatenate(uvt):
    pts = [(u % 1.0 * W, (1 - v % 1.0) * H) for u, v in tri]
    if max(abs(pts[i][0] - pts[j][0]) for i in range(3) for j in range(3)) > W / 2: continue   # crosses a UV wrap
    dr.line(pts + [pts[0]], fill=(255, 40, 160, 255), width=1)
lines.save(os.path.join(out, f"{name}_uv_template.png"))
over = Image.open(tex).convert("RGBA"); over.alpha_composite(lines); over.convert("RGB").save(os.path.join(out, f"{name}_uv_over_texture.png"))
print(f"[livery kit] {name}: {tris_skin} skin triangles, texture {W}x{H}{' (spine raised)' if hump else ''} -> {out}")
