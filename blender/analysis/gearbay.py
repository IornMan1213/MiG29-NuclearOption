import bpy, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree
S=17.32/176.7176
o=bpy.data.objects["MiG-29-airframe"]
dg=bpy.context.evaluated_depsgraph_get()
bvh=BVHTree.FromObject(o,dg)
M=o.matrix_world; Mi=M.inverted()
def u2b(p):  # unity MiG frame -> blender world
    return Vector((p[2]/S, -p[0]/S, p[1]/S))
def inside(pw):
    pl=Mi@pw; loc,nrm,i,d=bvh.find_nearest(pl)
    if loc is None: return False
    return (loc-pl).dot(nrm)>0
def wheel_ok(c,r=0.43,t=0.13):
    for k in range(12):
        a=2*math.pi*k/12
        for dx in (-t,0,t):
            p=(c[0]+dx, c[1]+r*math.sin(a), c[2]+r*math.cos(a))
            if not inside(u2b(p)): return False
    return inside(u2b(c))

def frac(c,r=0.43,t=0.13):
    n=ok=0
    for k in range(16):
        a=2*math.pi*k/16
        for rr in (r,r*0.5):
            for dx in (-t,0,t):
                n+=1; ok+=inside(u2b((c[0]+dx, c[1]+rr*math.sin(a), c[2]+rr*math.cos(a))))
    return ok/n
print("DEFAULT frac=%.2f"%frac((1.608,0.18,2.90)))
best=[]
for x in [i*0.1 for i in range(5,18)]:
  for y in [j*0.05 for j in range(-8,14)]:
    for z in [k*0.1 for k in range(18,36)]:
      f=frac((x,y,z)); d=math.dist((x,y,z),(1.608,0.18,2.90))
      best.append((-f+0.05*d,f,d,x,y,z))
best.sort()
for b in best[:12]: print("CAND frac=%.2f d=%.2f x=%.2f y=%.2f z=%.2f"%b[1:])
