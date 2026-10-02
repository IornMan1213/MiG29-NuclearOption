"""Shared Blender helpers for the cockpit work: load MiG parts from mig29_mesh.json in a Blender frame.

Frame: Blender X = Unity x (right), Blender Y = Unity z (forward), Blender Z = Unity y (up). MiG frame (no ModelOffset).
"""
import bpy, bmesh, json
from mathutils import Vector


def u2b(x, y, z):
    return (x, z, y)


def load_parts(path, names, weld=True):
    d = json.load(open(path))
    out = {}
    for p in d["parts"]:
        if p["name"] not in names:
            continue
        v = p["vertices"]; t = p["triangles"]
        verts = [u2b(v[i], v[i + 1], v[i + 2]) for i in range(0, len(v), 3)]
        faces = [(t[i], t[i + 1], t[i + 2]) for i in range(0, len(t), 3)]
        me = bpy.data.meshes.new(p["name"]); me.from_pydata(verts, [], faces); me.update()
        ob = bpy.data.objects.new("mig_" + p["name"], me); bpy.context.collection.objects.link(ob)
        if weld:
            bm = bmesh.new(); bm.from_mesh(me)
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            bm.to_mesh(me); bm.free()
        for f in me.polygons: f.use_smooth = True
        out[p["name"]] = ob
    return d, out


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


EYE_MIG = (0.0, 1.097, 6.723)   # Unity MiG frame, the KR-67 pilot's helmetCamPoint


def camera(name, loc, look, lens=14):
    cam = bpy.data.cameras.new(name); cam.lens = lens; cam.clip_start = 0.01
    ob = bpy.data.objects.new(name, cam); bpy.context.collection.objects.link(ob)
    ob.location = loc
    d = Vector(look) - Vector(loc)
    ob.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    return ob


def load_obj_unity(path, name, offset=(0, 0, 0)):
    """OBJ written by MiG29Inspect.RunCockpitRefs (Unity MiG frame) -> one Blender object."""
    verts, faces, base = [], [], 0
    for line in open(path):
        if line.startswith("v "):
            x, y, z = map(float, line.split()[1:4]); verts.append(u2b(x + offset[0], y + offset[1], z + offset[2]))
        elif line.startswith("f "):
            faces.append(tuple(int(i) - 1 for i in line.split()[1:4]))
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], faces); me.update()
    ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob)
    return ob
