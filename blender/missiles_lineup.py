"""Side-by-side render of the mod's missiles (from MiG29Source/missiles.json) for the project page.

blender -b --factory-startup --python missiles_lineup.py -- <MiG29Source dir> <out.png>
"""
import bpy, bmesh, json, math, os, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
sc = bpy.context.scene
dump = {p["name"]: p for p in json.load(open(os.path.join(src, "missiles.json")))["parts"]}

atlas = bpy.data.images.load(os.path.join(src, "missile_atlas.png"))
mat = bpy.data.materials.new("m"); mat.use_nodes = True
bsdf = mat.node_tree.nodes["Principled BSDF"]
tex = mat.node_tree.nodes.new("ShaderNodeTexImage"); tex.image = atlas; tex.interpolation = "Closest"
mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"]); bsdf.inputs["Roughness"].default_value = 0.4


def build(part, off):
    v, uv, t = part["vertices"], part["uvs"], part["triangles"]
    me = bpy.data.meshes.new(part["name"]); bm = bmesh.new()
    # Unity (x right, y up, z fwd) -> Blender (x fwd, y left, z up)
    verts = [bm.verts.new((v[i + 2] + off[0], -v[i] + off[1], v[i + 1] + off[2])) for i in range(0, len(v), 3)]
    layer = bm.loops.layers.uv.new()
    for i in range(0, len(t), 3):
        idx = (t[i], t[i + 2], t[i + 1])
        try:
            f = bm.faces.new([verts[k] for k in idx])
        except ValueError:
            continue
        for loop, k in zip(f.loops, idx):
            loop[layer].uv = (uv[2 * k], uv[2 * k + 1])
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(part["name"], me); sc.collection.objects.link(ob); me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = True
    ob.rotation_euler = (math.radians(45), 0, 0)  # fins in "+" so they show in profile
    return ob


rows = [("R27R", "R-27R"), ("R27T", "R-27T"), ("R73", "R-73"), ("R60M", "R-60M")]
for i, (name, label) in enumerate(rows):
    y = 1.45 - i * 0.98
    ob = build(dump[name], (0, 0, 0)); ob.location = (0, 0, y)
    txt = bpy.data.curves.new(label, "FONT"); txt.body = label; txt.size = 0.22; txt.align_x = "RIGHT"; txt.align_y = "CENTER"
    to = bpy.data.objects.new(label, txt); sc.collection.objects.link(to)
    to.location = (-2.35, 0, y); to.rotation_euler = (math.pi / 2, 0, 0); to.visible_shadow = False
    tm = bpy.data.materials.new("t"); tm.use_nodes = True; tm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.85, 0.88, 0.9, 1)
    txt.materials.append(tm)

cam = bpy.data.objects.new("c", bpy.data.cameras.new("c")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = "ORTHO"; cam.data.ortho_scale = 7.2
cam.location = (-0.6, -10, 0); cam.rotation_euler = (math.pi / 2, 0, 0)
sun = bpy.data.objects.new("s", bpy.data.lights.new("s", "SUN")); sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(50), 0, math.radians(-20)); sun.data.energy = 3.5; sun.data.use_shadow = False
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.09, 0.105, 0.13, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 1.0
engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
sc.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in engines else "BLENDER_EEVEE_NEXT"
sc.render.resolution_x, sc.render.resolution_y = 1600, 900
sc.view_settings.view_transform = "Standard"
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("RENDERED", out)
