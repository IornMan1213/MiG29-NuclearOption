"""Builds MiG29_left_bay_edit.blend for hand-editing the left main wheel well. Editable: 'well_main_L' (the right well is its
mirror). Reference only (not selectable): the skin with the forward door cut, the closed doors, the left leg at down / 80 / 90 / 95 %
/ stowed along the plugin path. Blender axes: X = MiG x (left is negative), Y = MiG z (forward), Z = MiG y (up).
Save the .blend, then blender/well_export.py writes tools/data/main_well_L_override.json, which tools/main_wells.py uses instead
of its generated well.
blender -b --python well_edit.py -- <MiG29Source> <MiG29Out> <out .blend>"""
import bpy, json, os, sys
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT, BLEND = argv
P = lambda v: (v[0], v[2], v[1])


def add(name, V, T, mat, coll, flip=True):
    V = np.asarray(V, float).reshape(-1, 3); T = np.asarray(T, int).reshape(-1, 3)
    faces = [(t[0], t[2], t[1]) for t in T] if flip else [tuple(t) for t in T]     # Unity winding -> Blender front faces
    me = bpy.data.meshes.new(name); me.from_pydata([P(v) for v in V], [], faces); me.update()
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me); coll.objects.link(ob)
    return ob


def mat(name, rgba, tex=None):
    m = bpy.data.materials.new(name); m.diffuse_color = rgba; m.use_backface_culling = True
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]; bsdf.inputs["Base Color"].default_value = rgba
    if tex:
        t = m.node_tree.nodes.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(tex)
        m.node_tree.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    return m


bpy.ops.wm.read_factory_settings(use_empty=True)
ref = bpy.data.collections.new("reference (locked)"); bpy.context.scene.collection.children.link(ref)
edit = bpy.data.collections.new("EDIT"); bpy.context.scene.collection.children.link(edit)
skin_m = mat("skin", (0.6, 0.62, 0.64, 1)); door_m = mat("door", (0.25, 0.45, 0.95, 1)); gear_m = mat("gear", (1, 0.45, 0.1, 1))
well_m = mat("well", (0.65, 0.68, 0.62, 1), os.path.join(SRC, "bay_atlas.png"))

mesh = json.load(open(os.path.join(SRC, "mig29_mesh.json")))
body = next(p for p in mesh["parts"] if p["name"] == "body")
BV = np.array(body["vertices"]).reshape(-1, 3); BT = np.array(body["triangles"]).reshape(-1, 3)
cut = json.load(open(os.path.join(SRC, "main_bay_door.json")))
keep = np.ones(len(BT), bool); keep[cut["remove"]] = False
c = BV[BT].mean(1); near = (c[:, 0] < 0.2)                       # left half is enough
add("skin", BV, BT[keep & near], skin_m, ref)
sv = np.array(cut["skin"]["vertices"]).reshape(-1, 3); st = np.array(cut["skin"]["triangles"]).reshape(-1, 3)
add("skin_cut_edges", sv, st, skin_m, ref)
dp = next(p for p in mesh["parts"] if p["name"] == "gear_doors_closed")
DV = np.array(dp["vertices"]).reshape(-1, 3); DT = np.array(dp["triangles"]).reshape(-1, 3); dc = DV[DT].mean(1)
add("strip_door_closed", DV, DT[(dc[:, 0] < -1.0) & (dc[:, 0] > -1.8) & (dc[:, 2] > -0.3) & (dc[:, 2] < 2.95)], door_m, ref)
fd = cut["doors"]["L"]; add("forward_door_closed", fd["vertices"], fd["triangles"], door_m, ref)
for pose, label in (("000", "down"), ("080", "80pct"), ("090", "90pct"), ("095", "95pct"), ("100", "stowed")):
    d = json.load(open(os.path.join(OUT, f"gear_new3_{pose}.json")))
    for p in d["parts"]:
        if "gearHinge_L" in p["path"]:
            ob = add(f"leg_{label}_{p['name']}", p["v"], p["t"], gear_m, ref)
            if label not in ("down", "stowed"): ob.hide_set(True)
for ob in ref.objects: ob.hide_select = True

w = json.load(open(os.path.join(SRC, "main_wells.json")))
wl = next(p for p in w["parts"] if p["name"] == "main_L")
ob = add("well_main_L", wl["vertices"], wl["triangles"], well_m, edit)
uv = ob.data.uv_layers.new(name="UV"); U = np.array(wl["uvs"]).reshape(-1, 2)
for loop in ob.data.loops: uv.data[loop.index].uv = U[loop.vertex_index]
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
bpy.context.scene.unit_settings.system = "METRIC"
bpy.ops.wm.save_as_mainfile(filepath=BLEND)
print("[well_edit] saved", BLEND)
