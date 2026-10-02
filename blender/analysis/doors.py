import bpy,bmesh
S=17.32/176.7176
o=bpy.data.objects["MiG-29-landingOff"]; b=bmesh.new(); b.from_mesh(o.data); b.verts.ensure_lookup_table()
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
    xs=[-p.y*S for p in co]; ys=[p.z*S for p in co]; zs=[p.x*S for p in co]
    print("DOOR n=%d x[%.2f..%.2f] y[%.2f..%.2f] z[%.2f..%.2f]"%(len(co),min(xs),max(xs),min(ys),max(ys),min(zs),max(zs)))
