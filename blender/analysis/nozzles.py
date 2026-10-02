import bpy
S=17.32/176.7176
o=bpy.data.objects["MiG-29-airframe"]
pts=[o.matrix_world@v.co for v in o.data.vertices]
for side,sg in (("L",1),("R",-1)):
    ps=[p for p in pts if p.x< -36 and 2< sg*p.y <13 and p.z<6]
    xs=[p.x for p in ps]; ys=[p.y for p in ps]; zs=[p.z for p in ps]
    print("NOZ",side,"u=(%.3f,%.3f,%.3f) diam_y=%.2f diam_z=%.2f n=%d"%(-(min(ys)+max(ys))/2*S,(min(zs)+max(zs))/2*S,min(xs)*S,(max(ys)-min(ys))*S,(max(zs)-min(zs))*S,len(ps)))
