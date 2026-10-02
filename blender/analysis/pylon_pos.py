import bpy,bmesh
from mathutils import Vector
S=17.32/176.7176
o=bpy.data.objects["MiG-29-rails"]; b=bmesh.new(); b.from_mesh(o.data); b.verts.ensure_lookup_table()
seen=set(); out=[]
for v in b.verts:
    if v.index in seen: continue
    st=[v]; comp=[v.index]; seen.add(v.index)
    while st:
        x=st.pop()
        for e in x.link_edges:
            y=e.other_vert(x)
            if y.index not in seen: seen.add(y.index); comp.append(y.index); st.append(y)
    co=[o.matrix_world@b.verts[i].co for i in comp]
    if len(co)<8: continue
    xs=[p.x for p in co]; ys=[p.y for p in co]; zs=[p.z for p in co]
    out.append((-sum(ys)/len(ys)*S, min(zs)*S, max(zs)*S, max(xs)*S, min(xs)*S, len(co)))
for r in sorted(out):
    if r[0] < 0.2: continue
    print("PYL x=%.2f ybot=%.2f ytop=%.2f zfront=%.2f zrear=%.2f n=%d"%r)
