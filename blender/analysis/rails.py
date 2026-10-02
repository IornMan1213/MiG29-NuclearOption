import bpy,bmesh
from mathutils import Vector
S=17.32/176.7176
o=bpy.data.objects["MiG-29-rails"]; b=bmesh.new(); b.from_mesh(o.data); b.verts.ensure_lookup_table()
seen=set()
for v in b.verts:
    if v.index in seen: continue
    st=[v]; comp=[v.index]; seen.add(v.index)
    while st:
        x=st.pop()
        for e in x.link_edges:
            y=e.other_vert(x)
            if y.index not in seen: seen.add(y.index); comp.append(y.index); st.append(y)
    co=[o.matrix_world@b.verts[i].co for i in comp]
    c=sum(co,Vector())/len(co)
    print("RAIL n=%d u=(%.2f,%.2f,%.2f) ylo=%.2f len=%.2f"%(len(co),-c.y*S,c.z*S,c.x*S,min(p.z for p in co)*S,(max(p.x for p in co)-min(p.x for p in co))*S))
