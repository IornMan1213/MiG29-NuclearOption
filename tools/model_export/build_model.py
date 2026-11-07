"""Rebuild a ModelExport dump (tools/model_export/ModelExport.cs: Unity, left-handed, Y up) in Blender and save it.

blender -b --factory-startup -P tools/model_export/build_model.py -- <dump dir>/MiG-29 liveries/model/MiG-29.fbx
blender -b --factory-startup -P tools/model_export/build_model.py -- <dump dir>/MiG-29 liveries/model/MiG-29_livery_paint.blend

.fbx: every part with its pivot and the game's hierarchy, textures embedded. .blend: a file to paint liveries in (paint_file).
The game's own parts on the jet (the pilot, the KR-67's inner structure, the afterburner glow) are left out: they are not the MiG.
"""
import json, math, os, sys
import bpy
from mathutils import Quaternion, Vector

DIR, OUT = sys.argv[sys.argv.index("--") + 1:][:2]
scene = json.load(open(os.path.join(DIR, "scene.json")))

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

# Unity (x right, y up, z forward, left-handed) -> Blender (x, z, y): swapping two axes also makes it right-handed
def P(v): return Vector((v[0], v[2], v[1]))
def R(q): x, y, z, w = q; return Quaternion((w, -x, -z, -y))

mats = {}
def material(m):
    key = json.dumps(m, sort_keys=True)
    if key in mats: return mats[key]
    mat = bpy.data.materials.new(m.get("name", "mat"))
    mat.use_nodes = True
    nt = mat.node_tree; bsdf = nt.nodes.get("Principled BSDF")
    col = m.get("color", [1, 1, 1, 1])
    bsdf.inputs["Base Color"].default_value = (col[0], col[1], col[2], 1)
    bsdf.inputs["Roughness"].default_value = 1 - m.get("smooth", 0.3)
    bsdf.inputs["Metallic"].default_value = m.get("metal", 0)
    if m.get("glass"):
        bsdf.inputs["Base Color"].default_value = (0.35, 0.45, 0.55, 1); bsdf.inputs["Alpha"].default_value = 0.25; bsdf.inputs["Roughness"].default_value = 0.05
    if m.get("base"):
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = bpy.data.images.load(os.path.join(DIR, m["base"]))
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    if m.get("normal"):
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = bpy.data.images.load(os.path.join(DIR, m["normal"]))
        tex.image.colorspace_settings.name = "Non-Color"
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(tex.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    mats[key] = mat
    return mat

def placeholder(m): return any("PLACEHOLDER" in (m.get(k) or "") for k in ("base", "normal")) or m.get("name") in ("Multirole1_nozzle_glow",)

def paint_file(out):
    """A file to paint liveries in: the airframe skin joined into one object, the rest as unselectable grey reference."""
    sc = bpy.context.scene
    for c in list(bpy.data.collections): bpy.data.collections.remove(c)
    paint = bpy.data.collections.new("Paint this"); ref = bpy.data.collections.new("Reference (not part of the livery)")
    sc.collection.children.link(paint); sc.collection.children.link(ref)
    bpy.context.view_layer.update()          # world matrices are only recomputed here after the parenting above
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    skin = [o for o in meshes if any(m and m.name.startswith("MiG29_skin") for m in o.data.materials)]
    world = {o: o.matrix_world.copy() for o in meshes}
    for o in meshes:
        o.parent = None; o.matrix_world = world[o]
    for o in [o for o in bpy.data.objects if o.type == "EMPTY"]:
        bpy.data.objects.remove(o, do_unlink=True)
    for o in meshes:
        for c in list(o.users_collection): c.objects.unlink(o)
        (paint if o in skin else ref).objects.link(o)
    bpy.ops.object.select_all(action="DESELECT")
    for o in skin: o.select_set(True)
    bpy.context.view_layer.objects.active = skin[0]
    bpy.ops.object.join()
    air = bpy.context.view_layer.objects.active; air.name = air.data.name = "MiG-29 airframe (paint me)"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    # the painted image: one 2048 texture, the base colour of the skin material
    skin_mat = next(m for m in air.data.materials if m and m.name.startswith("MiG29_skin"))
    tex = next(n for n in skin_mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image and "basecolor" in n.image.name)
    tex.image.name = "MiG-29_basecolor (your livery)"
    for n in skin_mat.node_tree.nodes: n.select = False
    skin_mat.node_tree.nodes.active = tex; tex.select = True
    skin_mat.paint_active_slot = 0
    # other materials on the joined object (if any) would take paint strokes too: they share the skin image for painting
    for o in ref.objects:
        o.hide_select = True
    grey = bpy.data.materials.new("reference grey"); grey.diffuse_color = (0.35, 0.35, 0.37, 1)
    for img in bpy.data.images:
        if img.has_data or img.filepath: img.pack()
    bpy.context.view_layer.objects.active = air; air.select_set(True)
    # every 3D view opens showing the texture, framed on the jet; image views show the livery image
    for scr in bpy.data.screens:
        for area in scr.areas:
            for sp in area.spaces:
                if sp.type == "VIEW_3D":
                    sp.shading.type = "SOLID"; sp.shading.color_type = "TEXTURE"; sp.shading.light = "STUDIO"
                    r = sp.region_3d
                    if r: r.view_location = (0, 0, 0); r.view_distance = 22
                elif sp.type == "IMAGE_EDITOR":
                    sp.image = tex.image
    bpy.ops.wm.save_as_mainfile(filepath=out, compress=True)
    print("wrote", out)

objs = []
skipped = 0
for i, n in enumerate(scene["nodes"]):
    data = None
    if n.get("mesh"):
        ms = n["mats"]
        glass = "canopy" in n["name"].lower() and all(placeholder(m) or m.get("base") is None for m in ms)
        if not glass and all(placeholder(m) for m in ms):
            skipped += 1      # the game's own parts (pilot, KR-67 structure): not part of the MiG model
        else:
            d = json.load(open(os.path.join(DIR, n["mesh"])))
            v = d["v"]; verts = [P(v[k:k + 3]) for k in range(0, len(v), 3)]
            nr = d["n"]; norms = [P(nr[k:k + 3]).normalized() for k in range(0, len(nr), 3)] if len(nr) == len(v) else None
            uv = d["uv"]
            faces, fmat = [], []
            for si, tris in enumerate(d["sub"]):
                for k in range(0, len(tris), 3):
                    faces.append((tris[k], tris[k + 2], tris[k + 1]))   # winding flips with the handedness
                    fmat.append(si)
            me = bpy.data.meshes.new(n["name"])
            me.from_pydata(verts, [], faces)
            for si, m in enumerate(ms):
                me.materials.append(material(dict(m, glass=True) if glass else m))
            me.polygons.foreach_set("material_index", fmat)
            if uv:
                layer = me.uv_layers.new(name="UVMap")
                for li, loop in enumerate(me.loops):
                    vi = loop.vertex_index; layer.data[li].uv = (uv[2 * vi], uv[2 * vi + 1])
            me.update()
            if norms:
                me.normals_split_custom_set_from_vertices(norms)
            data = me
    o = bpy.data.objects.new(n["name"], data)
    if data is None: o.empty_display_size = 0.1
    bpy.context.scene.collection.objects.link(o)
    o.rotation_mode = "QUATERNION"
    o.location = P(n["p"]); o.rotation_quaternion = R(n["r"]); s = n["s"]; o.scale = (s[0], s[2], s[1])
    objs.append(o)
for i, n in enumerate(scene["nodes"]):
    if n["parent"] >= 0:
        objs[i].parent = objs[n["parent"]]

# the nose on Blender's front (-Y), so Unity and most tools import it facing +Z / forward
root = objs[0]
root.rotation_quaternion = Quaternion((0, 0, 1), math.pi) @ root.rotation_quaternion

# empties with nothing under them are dropped
def has_mesh(o): return o.type == "MESH" or any(has_mesh(c) for c in o.children)
for o in [o for o in objs if not has_mesh(o)]:
    bpy.data.objects.remove(o, do_unlink=True)

print("meshes:", sum(1 for o in bpy.data.objects if o.type == "MESH"), "skipped game parts:", skipped)
if OUT.endswith(".blend"):
    paint_file(OUT)
    sys.exit(0)
bpy.ops.export_scene.fbx(filepath=OUT, path_mode="COPY", embed_textures=True, apply_scale_options="FBX_SCALE_UNITS",
                         mesh_smooth_type="OFF", use_tspace=True, add_leaf_bones=False, object_types={"EMPTY", "MESH"})
print("wrote", OUT)
