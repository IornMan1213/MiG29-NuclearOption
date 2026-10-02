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

c0=(0,0.575,3.218)
print("NOSE DEFAULT frac=%.2f"%frac(c0,0.32,0.1))
best=[]
for y in [j*0.05 for j in range(-10,14)]:
  for z in [k*0.1 for k in range(20,50)]:
    f=frac((0,y,z),0.32,0.1); d=math.dist((0,y,z),c0); best.append((-f+0.05*d,f,d,y,z))
best.sort()
for b in best[:6]: print("NOSE frac=%.2f d=%.2f y=%.2f z=%.2f"%b[1:])
