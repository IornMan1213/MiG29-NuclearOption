"""Showcase renders for the project page: the MiG-29 armed with the mod's R-27R / R-73 (from MiG29Source/missiles.json)
on its pylons, in either livery, against a sky.

blender -b MiG-29.blend --python showcase.py -- <MiG29Source dir> <out_dir> [shot names...]
"""
import bpy, bmesh, json, math, os, sys
from mathutils import Vector, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
src = argv[0] if len(argv) > 0 else "."
out = argv[1] if len(argv) > 1 else "."
S = 17.32 / 176.7176  # same scale as export_mig29.py

# pylon hardpoints in the exported MiG frame (Unity, metres) -- keep in sync with MiG29Weapons.cs
INNER, MIDDLE, OUTER = (2.36, -0.27, 1.31), (3.10, -0.29, 0.66), (3.73, -0.40, 0.28)
LOADOUT = [(INNER, "AKU470", "R27R", -0.14 - 0.115), (MIDDLE, "APU73", "R73", -0.10 - 0.085), (OUTER, "APU73", "R73", -0.10 - 0.085)]


def u2b(x, y, z):
    return Vector((z / S, -x / S, y / S))


sc = bpy.context.scene
for o in list(bpy.data.objects):
    if o.type in ("CAMERA", "LIGHT"):
        bpy.data.objects.remove(o)
for name in ("MiG-29-landingOn", "MiG-29-landingOnLight", "MiG-29-hud", "MiG-29-instrGlass"):
    if name in bpy.data.objects:
        bpy.data.objects[name].hide_render = True

# --- missiles and launchers -------------------------------------------------------------------------------------
dump = {p["name"]: p for p in json.load(open(os.path.join(src, "missiles.json")))["parts"]}
atlas = bpy.data.images.load(os.path.join(src, "missile_atlas.png"))
mmat = bpy.data.materials.new("mig29_missile"); mmat.use_nodes = True
bsdf = mmat.node_tree.nodes["Principled BSDF"]
tex = mmat.node_tree.nodes.new("ShaderNodeTexImage"); tex.image = atlas; tex.interpolation = "Closest"
mmat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.45


def build(part, offset):
    v = part["vertices"]; uv = part["uvs"]; t = part["triangles"]
    me = bpy.data.meshes.new(part["name"]); bm = bmesh.new()
    verts = [bm.verts.new(u2b(v[i] + offset[0], v[i + 1] + offset[1], v[i + 2] + offset[2])) for i in range(0, len(v), 3)]
    layer = bm.loops.layers.uv.new()
    for i in range(0, len(t), 3):
        idx = (t[i], t[i + 2], t[i + 1])  # Unity -> Blender handedness flip reverses winding
        try:
            f = bm.faces.new([verts[k] for k in idx])
        except ValueError:
            continue
        for loop, k in zip(f.loops, idx):
            loop[layer].uv = (uv[2 * k], uv[2 * k + 1])
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(part["name"], me); sc.collection.objects.link(ob)
    me.materials.append(mmat)
    for p in me.polygons:
        p.use_smooth = True
    return ob


weapons = []
for (x, y, z), rail, msl, my in LOADOUT:
    for sx in (1, -1):
        weapons.append(build(dump[rail], (sx * x, y, z)))
        weapons.append(build(dump[msl], (sx * x, y + my, z)))

# --- render setup ---------------------------------------------------------------------------------------------
pivot = bpy.data.objects.new("pivot", None); sc.collection.objects.link(pivot)
pivot.location = (34, 0, 9)
for o in bpy.data.objects:
    if o.type == "MESH":
        mw = o.matrix_world.copy(); o.parent = pivot; o.matrix_world = mw

engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
sc.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in engines else "BLENDER_EEVEE_NEXT"
sc.render.film_transparent = False
sc.render.resolution_x, sc.render.resolution_y = 1920, 1080
vts = [i.identifier for i in sc.view_settings.bl_rna.properties["view_transform"].enum_items]
sc.view_settings.view_transform = "AgX" if "AgX" in vts else "Filmic"

world = sc.world or bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
nt = world.node_tree; bg = nt.nodes.get("Background")
# vertical gradient sky: horizon haze -> deep blue
coord = nt.nodes.new("ShaderNodeTexCoord"); sep = nt.nodes.new("ShaderNodeSeparateXYZ"); ramp = nt.nodes.new("ShaderNodeValToRGB")
nt.links.new(coord.outputs["Generated"], sep.inputs[0]); nt.links.new(sep.outputs["Z"], ramp.inputs[0]); nt.links.new(ramp.outputs["Color"], bg.inputs[0])
ramp.color_ramp.elements[0].position = 0.0; ramp.color_ramp.elements[0].color = (0.62, 0.68, 0.74, 1)
ramp.color_ramp.elements[1].position = 0.45; ramp.color_ramp.elements[1].color = (0.12, 0.25, 0.48, 1)
bg.inputs[1].default_value = 1.0

sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
sun.data.energy = 4.5; sun.data.angle = math.radians(2)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.lens = 50


def look(target):
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()


def set_livery(path):
    img = bpy.data.images.load(path, check_existing=True)
    for m in bpy.data.materials:
        if not m.use_nodes or m == mmat:
            continue
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image and n.image.name.lower().startswith("basecolor"):
                n.image = img


shots = [
    # name, livery, pivot rotation (roll X, pitch Y, yaw Z), camera offset, sun rotation, lens
    ("armed_quarter", None, (math.radians(-12), math.radians(-6), 0), (250, 200, -60), (math.radians(35), 0, math.radians(140)), 50),
    ("armed_below", None, (math.radians(-15), math.radians(-8), 0), (90, 70, -300), (math.radians(160), 0, math.radians(120)), 50),
    ("desert_bank", "mig29_basecolor_desert.png", (math.radians(-55), math.radians(-6), 0), (220, 260, 160), (math.radians(40), 0, math.radians(120)), 50),
    ("desert_side", "mig29_basecolor_desert.png", (math.radians(8), math.radians(-3), 0), (-20, -330, 20), (math.radians(50), 0, math.radians(-70)), 50),
    ("digital_bank", "mig29_basecolor_digital.png", (math.radians(-55), math.radians(-6), 0), (220, 260, 160), (math.radians(40), 0, math.radians(120)), 50),
    ("digital_quarter", "mig29_basecolor_digital.png", (math.radians(-12), math.radians(-6), 0), (250, 200, -60), (math.radians(35), 0, math.radians(140)), 50),
]
only = [a for a in argv[2:]]
if only:
    shots = [x for x in shots if x[0] in only]
for name, livery, rot, camloc, sunrot, lens in shots:
    if livery:
        set_livery(os.path.join(src, livery))
    pivot.rotation_euler = Euler(rot)
    cam.location = Vector(camloc) + pivot.location; cam.data.lens = lens
    look(pivot.location)
    sun.rotation_euler = Euler(sunrot)
    sc.render.filepath = os.path.join(out, f"showcase_{name}.png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED", sc.render.filepath)
