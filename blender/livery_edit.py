"""Builds MiG29_livery_edit.blend for painting a new MiG-29 livery directly on the model (Texture Paint).
The exterior (airframe, control surfaces, rails, closed gear doors) is one object, 'MiG29', textured with blender/livery/
mig29_livery_custom.png (started from the two-tone grey base colour if it does not exist yet). Paint, then save the image
(Image > Save, Alt+S, or accept "save modified images" on File > Save). tools/build_mig29.ps1 ships that PNG as the
"Fulcrum Custom" livery.
Blender axes: X = MiG x (left is negative), Y = MiG z (forward), Z = MiG y (up).
blender -b --python livery_edit.py -- <MiG29Source> <out .blend>"""
import bpy, json, os, shutil, sys

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, BLEND = argv
HERE = os.path.dirname(os.path.abspath(__file__))
PNG = os.path.join(HERE, "livery", "mig29_livery_custom.png")
PARTS = {"body", "aileron_L", "aileron_R", "flap_L", "flap_R", "rudder_L", "rudder_R", "stab_L", "stab_R", "rails", "gear_doors_closed"}

os.makedirs(os.path.dirname(PNG), exist_ok=True)
if not os.path.exists(PNG):
    shutil.copy(os.path.join(SRC, "mig29_basecolor.png"), PNG)

bpy.ops.wm.read_factory_settings(use_empty=True)
mesh = json.load(open(os.path.join(SRC, "mig29_mesh.json")))
verts, faces, uvs = [], [], []
for p in mesh["parts"]:
    if p["name"] not in PARTS:
        continue
    b = len(verts)
    V = p["vertices"]; U = p["uvs"]; T = p["triangles"]
    verts += [(V[i], V[i + 2], V[i + 1]) for i in range(0, len(V), 3)]
    uvs += [(U[i], U[i + 1]) for i in range(0, len(U), 2)]
    faces += [(b + T[i], b + T[i + 2], b + T[i + 1]) for i in range(0, len(T), 3)]   # Unity winding -> Blender front faces

me = bpy.data.meshes.new("MiG29")
me.from_pydata(verts, [], faces); me.update()
uv = me.uv_layers.new(name="UV")
for loop in me.loops:
    uv.data[loop.index].uv = uvs[loop.vertex_index]
for poly in me.polygons:
    poly.use_smooth = True

img = bpy.data.images.load(PNG)
mat = bpy.data.materials.new("MiG29_livery"); mat.use_nodes = True
nt = mat.node_tree; bsdf = nt.nodes["Principled BSDF"]
tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img; tex.location = (-400, 200)
nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.6
nt.nodes.active = tex                                   # the image texture paint mode paints into
me.materials.append(mat)

ob = bpy.data.objects.new("MiG29", me)
bpy.context.scene.collection.objects.link(ob)
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
ob.data.use_paint_mask = False

sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sun.rotation_euler = (0.6, 0.2, 0.8)
bpy.context.scene.collection.objects.link(sun)

notes = bpy.data.texts.new("HOW_TO")
notes.write(__doc__ + "\nTips: F = brush size, Shift+F = strength, X = toggle Mix/Erase colours, S = sample a colour from the model.\n"
            "Some UV areas are shared between the left and right sides: paint there shows on both.\n")

try:
    bpy.ops.object.mode_set(mode="TEXTURE_PAINT")
except Exception:
    pass
bpy.ops.wm.save_as_mainfile(filepath=BLEND)
print("[livery_edit] saved", BLEND, "painting", PNG)
