"""Exports the hand-edited left main well from MiG29_left_bay_edit.blend (object 'well_main_L') to
tools/data/main_well_L_override.json, MiG frame, Unity winding; tools/main_wells.py then uses it (and its mirror for the right).
blender -b <blend> --python well_export.py"""
import bpy, bmesh, json, os

ob = bpy.data.objects["well_main_L"]
me = ob.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces[:]); bm.verts.ensure_lookup_table()
M = ob.matrix_world
V, N, T = [], [], []
for f in bm.faces:
    for v in f.verts:     # split per face corner: flat-shaded, any edit topology works
        p = M @ v.co; n = (M.to_3x3() @ f.normal).normalized()
        V.append((p.x, p.z, p.y)); N.append((n.x, n.z, n.y))
    b = len(V) - 3
    T += [b, b + 2, b + 1]                              # Blender front faces -> Unity winding
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools", "data", "main_well_L_override.json")
json.dump({"vertices": [round(c, 5) for v in V for c in v], "normals": [round(c, 5) for n in N for c in n], "triangles": T}, open(out, "w"))
print(f"[well_export] {len(T) // 3} tris -> {out}")
