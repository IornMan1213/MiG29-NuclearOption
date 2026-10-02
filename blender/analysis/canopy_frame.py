import bpy,bmesh
from mathutils import Vector
S=17.32/176.7176
cp=bpy.data.objects["MiG-29-canopy"]; cps=[cp.matrix_world@v.co for v in cp.data.vertices]
cmin=Vector((min(p.x for p in cps),min(p.y for p in cps),min(p.z for p in cps))); cmax=Vector((max(p.x for p in cps),max(p.y for p in cps),max(p.z for p in cps)))
print("CANOPY bmin",[round(v*S,3) for v in cmin],"bmax",[round(v*S,3) for v in cmax])
o=bpy.data.objects["MiG-29-cockpit"]; b=bmesh.new(); b.from_mesh(o.data); b.verts.ensure_lookup_table()
seen=set(); n_in=0
for v in b.verts:
    if v.index in seen: continue
    st=[v]; comp=[v.index]; seen.add(v.index)
    while st:
        x=st.pop()
        for e in x.link_edges:
            y=e.other_vert(x)
            if y.index not in seen: seen.add(y.index); comp.append(y.index); st.append(y)
    co=[o.matrix_world@b.verts[i].co for i in comp]
    mn=Vector((min(p.x for p in co),min(p.y for p in co),min(p.z for p in co))); mx=Vector((max(p.x for p in co),max(p.y for p in co),max(p.z for p in co)))
    inside = mn.x>=cmin.x-1 and mx.x<=cmax.x+1 and mn.z>=cmin.z+0.6*(cmax.z-cmin.z)-0.0 and len(co)>8
    if inside: n_in+=1; print("FRAME? n=%d x[%.2f..%.2f] z[%.2f..%.2f]"%(len(co),mn.x*S,mx.x*S,mn.z*S,mx.z*S))
print("islands above sill:",n_in)
