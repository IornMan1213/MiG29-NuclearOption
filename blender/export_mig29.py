"""Export the Sketchfab MiG-29 into a deterministic JSON mesh dump in Unity coordinates.

Run: blender -b MiG-29.blend --python export_mig29.py -- <out_dir>

Blender world: +X nose, +Y left wing, +Z up (model units ~ decimetres).
Unity (left-handed): +x right, +y up, +z forward, metres.
  x_u = -Y_b * S,  y_u = Z_b * S,  z_u = X_b * S   (det -1, so triangle winding is reversed)
"""
import bpy, bmesh, json, sys, os
from mathutils import Vector

S = 17.32 / 176.7176  # real MiG-29 length incl. pitot / model length
OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "."
os.makedirs(OUT, exist_ok=True)


def to_u(v):
    return [-v.y * S, v.z * S, v.x * S]


def nto_u(n):
    return [-n.y, n.z, n.x]


def islands(bm):
    seen, out = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, comp = [v], set([v.index])
        seen.add(v.index)
        while stack:
            x = stack.pop()
            for e in x.link_edges:
                y = e.other_vert(x)
                if y.index not in seen:
                    seen.add(y.index); comp.add(y.index); stack.append(y)
        out.append(comp)
    return out


def classify(co):
    """co: list of world-space Blender vectors of one island -> surface name or None."""
    xs = [c.x for c in co]; ys = [c.y for c in co]; zs = [c.z for c in co]
    cx, cy, cz = sum(xs) / len(xs), sum(ys) / len(ys), sum(zs) / len(zs)
    sx, sy, sz = max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)
    side = "L" if cy > 0 else "R"
    ay = abs(cy)
    if -42 < cx < -30 and 16 < ay < 20.5 and sz > 10 and len(co) > 80:
        return "rudder_" + side
    if -54 < cx < -19 and 17 < ay < 40 and sx > 25 and sy > 15 and sz < 4 and len(co) > 60:
        return "stab_" + side
    if -16 < cx < -6 and 35 < ay < 50 and sy > 10 and sz < 3 and len(co) > 25:
        return "aileron_" + side
    if -13.5 < cx < -1 and 17 < ay < 37 and sy > 15 and sz < 3 and len(co) > 35:
        return "flap_" + side
    return None


def hinge(name, co):
    """Pivot and hinge axis (Unity coords). Axis is oriented right (+x) for horizontal surfaces, up (+y) for rudders."""
    if name.startswith("rudder"):
        zs = sorted(c.z for c in co); zmid = (zs[0] + zs[-1]) / 2
        lo = [c for c in co if c.z < zmid]; hi = [c for c in co if c.z >= zmid]
        a = max(lo, key=lambda c: c.x); b = max(hi, key=lambda c: c.x)
        pa, pb = Vector(to_u(a)), Vector(to_u(b))
        axis = (pb - pa).normalized()
        if axis.y < 0: axis = -axis
        piv = (pa + pb) / 2
        # move pivot to mid-thickness
        piv.x = sum(to_u(c)[0] for c in co) / len(co)
        return piv, axis
    ay = [abs(c.y) for c in co]; ymid = (min(ay) + max(ay)) / 2
    inb = [c for c in co if abs(c.y) < ymid]; outb = [c for c in co if abs(c.y) >= ymid]
    a = max(inb, key=lambda c: c.x); b = max(outb, key=lambda c: c.x)
    pa, pb = Vector(to_u(a)), Vector(to_u(b))
    if name.startswith("stab"):
        # all-moving stabilator: pivot near 40% root chord, lateral axis
        xs = [c.x for c in inb]
        root_le, root_te = max(xs), min(xs)
        px = root_le - 0.4 * (root_le - root_te)
        cen = Vector(to_u(Vector((px, sum(c.y for c in inb) / len(inb), sum(c.z for c in co) / len(co)))))
        axis = Vector((1, 0, 0))
        return cen, axis
    axis = (pb - pa).normalized()
    if axis.x < 0: axis = -axis
    piv = (pa + pb) / 2
    piv.y = sum(to_u(c)[1] for c in co) / len(co)
    return piv, axis


def dump_faces(name, obj, bmx, faces, mat_name, pivot=None):
    """faces: list of BMFace in obj's bmesh. Emits per-corner-deduped vertex buffers in Unity coords relative to pivot."""
    mw = obj.matrix_world
    nm = mw.to_3x3().inverted().transposed()
    uv_layer = bmx.loops.layers.uv.active
    verts, norms, uvs, tris, index = [], [], [], [], {}
    piv = pivot if pivot is not None else Vector((0, 0, 0))
    for f in faces:
        corner_ids = []
        for loop in f.loops:
            p = Vector(to_u(mw @ loop.vert.co)) - piv
            n = Vector(nto_u((nm @ (loop.vert.normal if f.smooth else f.normal)).normalized()))
            uv = loop[uv_layer].uv if uv_layer else Vector((0, 0))
            key = (round(p.x, 5), round(p.y, 5), round(p.z, 5), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(uv.x, 5), round(uv.y, 5))
            if key not in index:
                index[key] = len(verts)
                verts.append([p.x, p.y, p.z]); norms.append([n.x, n.y, n.z]); uvs.append([uv.x, uv.y])
            corner_ids.append(index[key])
        for i in range(1, len(corner_ids) - 1):  # fan triangulate, reversed winding for handedness flip
            tris += [corner_ids[0], corner_ids[i + 1], corner_ids[i]]
    return dict(name=name, material=mat_name, vertices=sum(verts, []), normals=sum(norms, []), uvs=sum(uvs, []), triangles=tris)


parts, info = [], {}

# airframe: split control surfaces out
af = bpy.data.objects["MiG-29-airframe"]
bm = bmesh.new(); bm.from_mesh(af.data); bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()
isl = islands(bm)
surface_of_vert = {}
found = {}
for comp in isl:
    co = [af.matrix_world @ bm.verts[i].co for i in comp]
    name = classify(co)
    if name:
        if name in found:
            raise SystemExit(f"duplicate island for {name}")
        found[name] = (comp, co)
        for i in comp: surface_of_vert[i] = name
print("SURFACES", sorted(found))
assert len(found) == 8, found.keys()
body_faces = [f for f in bm.faces if f.verts[0].index not in surface_of_vert]
parts.append(dump_faces("body", af, bm, body_faces, "airframe"))
for name, (comp, co) in sorted(found.items()):
    piv, axis = hinge(name, co)
    faces = [f for f in bm.faces if surface_of_vert.get(f.verts[0].index) == name]
    p = dump_faces(name, af, bm, faces, "airframe", pivot=piv)
    p["pivot"] = list(piv); p["axis"] = list(axis)
    parts.append(p)
    print("HINGE", name, [round(v, 3) for v in piv], [round(v, 3) for v in axis])

for obj_name, part_name, mat in [("MiG-29-canopy", "canopy", "glass"), ("MiG-29-cockpit", "cockpit", "airframe"),
                                 ("MiG-29-rails", "rails", "airframe"), ("MiG-29-landingOff", "gear_doors_closed", "airframe")]:
    o = bpy.data.objects[obj_name]
    b = bmesh.new(); b.from_mesh(o.data); b.faces.ensure_lookup_table()
    parts.append(dump_faces(part_name, o, b, list(b.faces), mat))

# reference points from the gear-down mesh (wheel contact points) and nozzles
lo = bpy.data.objects["MiG-29-landingOn"]
pts = [lo.matrix_world @ v.co for v in lo.data.vertices]
def contact(sel):
    ps = [p for p in pts if sel(p)]
    zm = min(p.z for p in ps)
    return [p for p in ps if p.z < zm + 0.4]
avg = lambda ps: Vector((sum(p.x for p in ps) / len(ps), sum(p.y for p in ps) / len(ps), sum(p.z for p in ps) / len(ps)))
info["nose_wheel_contact"] = to_u(avg(contact(lambda p: p.x > 35)))
info["main_wheel_contact_L"] = to_u(avg(contact(lambda p: p.x <= 35 and p.y > 5)))
info["main_wheel_contact_R"] = to_u(avg(contact(lambda p: p.x <= 35 and p.y < -5)))
apts = [af.matrix_world @ v.co for v in af.data.vertices]
xmin = min(p.x for p in apts)
tailend = [p for p in apts if p.x < xmin + 1.5 and abs(p.z - 0) < 30]
info["tail_min_x_u"] = xmin * S
cp = bpy.data.objects["MiG-29-canopy"]; cps = [cp.matrix_world @ v.co for v in cp.data.vertices]
info["canopy_center"] = to_u(avg(cps))
info["canopy_top_y"] = max(p.z for p in cps) * S
ck = bpy.data.objects["MiG-29-cockpit"]; info["cockpit_center"] = to_u(avg([ck.matrix_world @ v.co for v in ck.data.vertices]))
# rails / pylons: islands of the rails mesh
rb = bmesh.new(); rb.from_mesh(bpy.data.objects["MiG-29-rails"].data); rb.verts.ensure_lookup_table()
rails = []
for comp in islands(rb):
    co = [bpy.data.objects["MiG-29-rails"].matrix_world @ rb.verts[i].co for i in comp]
    if len(co) > 20:
        c = avg(co); rails.append(dict(center=to_u(c), bottom_y=min(p.z for p in co) * S, n=len(co)))
info["rails"] = sorted(rails, key=lambda r: r["center"][0])
info["scale"] = S
json.dump(dict(parts=parts, info=info), open(os.path.join(OUT, "mig29_mesh.json"), "w"))
print("INFO", json.dumps(info, indent=1))
print("PARTS", [(p["name"], len(p["vertices"]) // 3, len(p["triangles"]) // 3) for p in parts])
