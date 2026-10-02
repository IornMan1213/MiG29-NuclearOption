"""From-scratch MiG-29 (9.12) cockpit, modelled procedurally and fitted inside the MiG airframe.

blender -b --python blender/cockpit_build.py -- <MiG29Source dir> [<preview out dir>]
  reads  mig29_mesh.json (airframe, for fitting), cockpit_atlas.json (texture cells from tools/cockpit_atlas.py)
  writes cockpit_mesh.json: parts in the Unity MiG frame (same layout as mig29_mesh.json parts)
     tub      static interior: tub walls, floor, consoles, instrument panel + instruments, glareshield, HUD body, pedals, decks
     glass    HUD combiner glass                                   (canopy glass material)
     seat     ejection seat, rides on the game's EjectionSeat     (pivot = seat origin)
     stick    centre stick, on the game's joystick transform       (pivot = stick base)
     throttle throttle levers, on the game's throttle transform    (pivot = lever axle)
     screen   radar/tactical display quad, on the game's tacScreen (UVs = the tac screen render texture's main area)
     lamps    master warning lamps, on the game's warningLights    (UVs = the stock warning-light texture cells)
     frames   the MiG model's own windscreen bow, canopy sills and mirrors (MiG skin UVs)
  and preview renders (Eevee) when an output dir is given.

Blender frame: X = Unity x (right), Y = Unity z (forward), Z = Unity y (up). All numbers below are written as Unity (x, y, z)
through P() so they read like the rest of the project.
"""
import bpy, bmesh, json, math, os, sys
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cockpit_scene as cs

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = argv[0]
PREVIEW = argv[1] if len(argv) > 1 else None

ATLAS = json.load(open(os.path.join(SRC, "cockpit_atlas.json")))
AW = ATLAS["size"]

# pilot: the game's pilot + seat move back by PILOT_SHIFT (MiG29Builder.CockpitShift) so the head sits against our headrest
EYE = (0.0, 1.097, 6.553)


def P(x, y, z):
    return Vector((x, z, y))


def rect_uv(name, inset=1.5):
    x, y, w, h = ATLAS["rects"][name]
    u0, u1 = (x + inset) / AW, (x + w - inset) / AW
    v0, v1 = 1 - (y + h - inset) / AW, 1 - (y + inset) / AW
    return u0, v0, u1, v1


# ------------------------------------------------------------------------------------------------ geometry collector
class Part:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")

    def face(self, pts, uvs):
        vs = [self.bm.verts.new(p) for p in pts]
        f = self.bm.faces.new(vs)
        for l, uv in zip(f.loops, uvs):
            l[self.uv].uv = uv
        return f


PARTS = {n: Part(n) for n in ("tub", "glass", "seat", "stick", "throttle", "screen", "lamps")}


def paint_uvs(pts, normal, paint):
    """Planar-project a face into its paint cell: 1 m of surface spans the cell, bigger faces are scaled down to fit."""
    u0, v0, u1, v1 = rect_uv("paint_" + paint, 6)
    n = Vector(normal).normalized() if Vector(normal).length > 1e-9 else Vector((0, 0, 1))
    a = n.orthogonal().normalized(); b = n.cross(a).normalized()
    c = sum(pts, Vector()) / len(pts)
    q = [((p - c).dot(a), (p - c).dot(b)) for p in pts]
    ext = max(max(abs(x) for x, _ in q), max(abs(y) for _, y in q), 1e-6)
    k = min(1.0, 0.5 / ext)
    cu, cv, hu, hv = (u0 + u1) / 2, (v0 + v1) / 2, (u1 - u0) / 2, (v1 - v0) / 2
    return [(cu + x * k * hu * 2, cv + y * k * hv * 2) for x, y in q]


def add_bm(part, bm_src, paint, matrix=Matrix.Identity(4)):
    """Copy a bmesh into a part, painting every face."""
    for f in bm_src.faces:
        pts = [matrix @ v.co for v in f.verts]
        nrm = (matrix.to_3x3() @ f.normal)
        PARTS[part].face(pts, paint_uvs(pts, nrm, paint))


def box(part, center, size, paint, bevel=0.004, rot=None, segs=2):
    """Box in Unity frame: center (x,y,z), size (sx,sy,sz); rot = (axis 'x'|'y'|'z' Unity, degrees) or a Matrix."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector((size[0], size[2], size[1])), verts=bm.verts)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(size) * 0.45), segments=segs, affect="EDGES", profile=0.5)
    m = Matrix.Translation(P(*center)) @ rot_matrix(rot)
    add_bm(part, bm, paint, m)
    bm.free()


def rot_matrix(rot):
    if rot is None:
        return Matrix.Identity(4)
    if isinstance(rot, Matrix):
        return rot.to_4x4()
    m = Matrix.Identity(4)
    for axis, deg in (rot if isinstance(rot[0], tuple) else [rot]):
        baxis = {"x": "X", "y": "Z", "z": "Y"}[axis]
        sign = -1 if axis in ("x", "y", "z") else 1   # Unity left-handed rotations -> Blender right-handed
        m = m @ Matrix.Rotation(math.radians(deg * sign), 4, baxis)
    return m


def cylinder(part, center, axis, radius, depth, paint, segs=24, cap=True, r2=None):
    """Cylinder (or cone with r2) along a Unity-frame axis vector."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=cap, cap_tris=False, segments=segs, radius1=radius, radius2=radius if r2 is None else r2, depth=depth)
    ax = Vector((axis[0], axis[2], axis[1])).normalized()
    m = Matrix.Translation(P(*center)) @ Vector((0, 0, 1)).rotation_difference(ax).to_matrix().to_4x4()
    add_bm(part, bm, paint, m)
    bm.free()


def sphere(part, center, radius, paint, scale=(1, 1, 1)):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=radius)
    bmesh.ops.scale(bm, vec=Vector((scale[0], scale[2], scale[1])), verts=bm.verts)
    add_bm(part, bm, paint, Matrix.Translation(P(*center)))
    bm.free()


def tube(part, pts, radius, paint, segs=10):
    """Swept round tube through Unity-frame points."""
    pts = [P(*p) for p in pts]
    rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        a = t.orthogonal().normalized(); b = t.cross(a)
        rings.append([p + (a * math.cos(2 * math.pi * k / segs) + b * math.sin(2 * math.pi * k / segs)) * radius for k in range(segs)])
    for i in range(len(rings) - 1):
        for k in range(segs):
            q = [rings[i][k], rings[i][(k + 1) % segs], rings[i + 1][(k + 1) % segs], rings[i + 1][k]]
            n = (q[1] - q[0]).cross(q[3] - q[0])
            PARTS[part].face(q, paint_uvs(q, n, paint))


def quad_uv(part, corners, uvrect, flip=False):
    """Quad (Unity-frame corners: bottom-left, bottom-right, top-right, top-left as seen from the front) with UVs spanning uvrect."""
    u0, v0, u1, v1 = uvrect
    uvs = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
    pts = [P(*c) for c in corners]
    PARTS[part].face(pts, uvs)


def poly_paint(part, pts_u, paint):
    pts = [P(*p) for p in pts_u]
    n = (pts[1] - pts[0]).cross(pts[2] - pts[0])
    PARTS[part].face(pts, paint_uvs(pts, n, paint))


def prism(part, outline, axis_off, paint):
    """Extrude a planar outline (list of Unity points, CCW seen from the front) by the vector axis_off (Unity), capped."""
    a = [P(*p) for p in outline]; off = Vector((axis_off[0], axis_off[2], axis_off[1]))
    b = [p + off for p in a]
    n = len(a)
    def f(pts):
        nn = (pts[1] - pts[0]).cross(pts[2] - pts[0])
        PARTS[part].face(pts, paint_uvs(pts, nn, paint))
    f(a); f(list(reversed(b)))
    for i in range(n):
        f([a[(i + 1) % n], a[i], b[i], b[(i + 1) % n]])


# ------------------------------------------------------------------------------------------------ airframe for fitting
cs.clear()
data, obs = cs.load_parts(os.path.join(SRC, "mig29_mesh.json"), {"body", "canopy", "cockpit"})
dg = bpy.context.evaluated_depsgraph_get()
body_bvh = BVHTree.FromObject(obs["body"], dg)
glass_bvh = BVHTree.FromObject(obs["canopy"], dg)


def halfwidth(y, z, default=0.40):
    hit = body_bvh.ray_cast(P(0, y, z), Vector((1, 0, 0)), 1.5)
    return hit[3] if hit[0] is not None else default


def glass_top(x, z):
    hit = glass_bvh.ray_cast(P(x, 2.5, z), Vector((0, 0, -1)), 3.0)
    return hit[0].z if hit[0] is not None else None


def body_top(x, z):
    hit = body_bvh.ray_cast(P(x, 2.5, z), Vector((0, 0, -1)), 3.0)
    return hit[0].z if hit[0] is not None else None


FLOOR = 0.30
Z_BULK = 6.04          # rear bulkhead (behind the headrest)
Z_PANEL = 7.25         # instrument panel face (centre, at its mid height)
Z_FRONT = 7.78         # footwell front wall
INSET = 0.012


_sill = {}


def sill_y(z):
    """Top of the cockpit wall at station z: the highest point where the fuselage side still closes the cockpit (rim)."""
    k = round(z, 3)
    if k not in _sill:
        lo, hi = 0.6, 1.0
        for _ in range(18):
            mid = (lo + hi) / 2
            if body_bvh.ray_cast(P(0, mid, z), Vector((1, 0, 0)), 1.5)[0] is not None: lo = mid
            else: hi = mid
        _sill[k] = lo - 0.004
    return _sill[k]


def wall_x(y, z):
    y = min(y, sill_y(z))
    return min(halfwidth(y, z) - (INSET if y > 0.5 else 0.03), 0.46)


def under_glass(x, y, z, gap=0.03):
    """Clamp a height so it stays gap below the canopy / windscreen glass."""
    g = glass_top(x, z)
    return min(y, g - gap) if g is not None else y


_top = {}


def wall_top(z):
    """Wall top: the rim, raised (vertically, at the rim line) to just under the canopy glass edge so no airframe rim shows."""
    k = round(z, 3)
    if k not in _top:
        s_ = sill_y(z); xr = wall_x(s_, z)
        gs = [glass_top(xr - dx, z + dz) for dx in (-0.016, 0.004, 0.03) for dz in (-0.03, 0.0, 0.03)]
        gs = [g for g in gs if g is not None]
        _top[k] = max(s_ - 0.005, min(min(gs) - 0.012, s_ + 0.12)) if len(gs) == 9 else s_ - 0.005
    return _top[k]


# ------------------------------------------------------------------------------------------------ tub: walls, floor, bulkheads
zs = [Z_BULK + i * 0.05 for i in range(int((Z_FRONT - Z_BULK) / 0.05) + 1)]
for side in (-1, 1):
    for z0, z1 in zip(zs, zs[1:]):
        top0, top1 = wall_top(z0), wall_top(z1)
        ys0 = [FLOOR + (top0 - FLOOR) * k / 6 for k in range(7)]
        ys1 = [FLOOR + (top1 - FLOOR) * k / 6 for k in range(7)]
        for k in range(6):
            q = [(side * wall_x(ys0[k], z0), ys0[k], z0), (side * wall_x(ys1[k], z1), ys1[k], z1),
                 (side * wall_x(ys1[k + 1], z1), ys1[k + 1], z1), (side * wall_x(ys0[k + 1], z0), ys0[k + 1], z0)]
            q = q if side < 0 else list(reversed(q))
            # one continuous texture along the wall: u by station, v by height
            wu0, wv0, wu1, wv1 = rect_uv("wall", 1.0)
            uvs = [(wu0 + (p[2] - Z_BULK) / (Z_FRONT - Z_BULK) * (wu1 - wu0), wv0 + (p[1] - FLOOR) / (1.06 - FLOOR) * (wv1 - wv0)) for p in q]
            PARTS["tub"].face([P(*p) for p in q], uvs)
    # sill cap: covers the fuselage rim between the wall top and the canopy glass edge
    for z0, z1 in zip(zs, zs[1:]):
        if z0 > 7.30: break
        def cap(z, out):
            y = wall_top(z); xi = wall_x(y, z)
            return (side * (xi + (0.014 if out else 0.0)), under_glass(side * (xi + 0.014), y + (0.004 if out else 0.008), z, 0.012), z)
        q = [cap(z0, False), cap(z1, False), cap(z1, True), cap(z0, True)]
        poly_paint("tub", q if side > 0 else q[::-1], "dgrey")
    # canopy sill rail along the top of the wall
    rail = [(side * (wall_x(wall_top(z) - 0.03, z) - 0.02), wall_top(z) - 0.03, z) for z in zs if z < 7.30]
    tube("tub", rail, 0.014, "dgrey")
    # cable loom and canopy handle on the wall
    tube("tub", [(side * (wall_x(0.82, z) - 0.02), 0.82, z) for z in zs if 6.2 < z < 7.05], 0.009, "black")
    if side < 0:
        tube("tub", [(-wall_x(0.88, 6.62) + 0.03, 0.86, 6.55), (-wall_x(0.88, 6.62) + 0.06, 0.88, 6.62), (-wall_x(0.88, 6.62) + 0.03, 0.86, 6.70)], 0.010, "red")

# floor (tread plate) and footwell front wall
fl = []
for z0, z1 in zip(zs, zs[1:]):
    w0, w1 = wall_x(FLOOR + 0.01, z0), wall_x(FLOOR + 0.01, z1)
    poly_paint("tub", [(-w0, FLOOR, z0), (-w1, FLOOR, z1), (w1, FLOOR, z1), (w0, FLOOR, z0)][::-1], "floor")
def bulkhead(z, top, paint, facing):
    ys = [FLOOR + (top - FLOOR) * k / 8 for k in range(9)]
    for y0, y1 in zip(ys, ys[1:]):
        q = [(-wall_x(y0, z), y0, z), (wall_x(y0, z), y0, z), (wall_x(y1, z), y1, z), (-wall_x(y1, z), y1, z)]
        poly_paint("tub", q if facing > 0 else q[::-1], paint)
bulkhead(Z_FRONT, sill_y(Z_FRONT), "dgrey", -1)
# rear bulkhead and the deck behind it, up to the canopy
top_b = sill_y(Z_BULK)
bulkhead(Z_BULK, top_b, "dgrey", 1)
deck_z = [5.70, 5.78, 5.86, 5.94, Z_BULK]
for z0, z1 in zip(deck_z, deck_z[1:]):
    y0, y1 = sill_y(z0) - 0.02, sill_y(z1) - 0.02
    w0, w1 = wall_x(y0, z0) - 0.01, wall_x(y1, z1) - 0.01
    poly_paint("tub", [(-w0, y0, z0), (-w1, y1, z1), (w1, y1, z1), (w0, y0, z0)][::-1], "dgrey")
box("tub", (0, top_b + 0.01, 5.86), (0.30, 0.05, 0.22), "mgrey", 0.01)               # avionics bay cover behind the seat
box("tub", (0.10, top_b + 0.045, 5.84), (0.06, 0.025, 0.06), "black", 0.006)
box("tub", (-0.10, top_b + 0.045, 5.84), (0.06, 0.025, 0.06), "black", 0.006)

# ------------------------------------------------------------------------------------------------ side consoles
CON_Z0, CON_Z1 = 6.12, 7.02
CON_IN = 0.235
CON_TOP = 0.69
for side in (-1, 1):
    xo = min(wall_x(y, z) for y in (FLOOR, 0.45, 0.6, CON_TOP, CON_TOP + 0.05) for z in (CON_Z0, 6.4, 6.7, CON_Z1)) - 0.004
    outline = [(side * CON_IN, FLOOR, CON_Z0), (side * CON_IN, CON_TOP - 0.02, CON_Z0), (side * CON_IN, CON_TOP, CON_Z0 + 0.03),
               (side * CON_IN, CON_TOP + 0.015, CON_Z1 - 0.06), (side * CON_IN, CON_TOP + 0.05, CON_Z1), (side * CON_IN, FLOOR, CON_Z1)]
    # extrude toward the wall
    prism("tub", outline if side > 0 else list(reversed(outline)), (side * (xo - CON_IN), 0, 0), "turq")
    # decals on the console top (slightly sloped surface between CON_Z0+0.03 and CON_Z1-0.06)
    names = ["sp_lc1", "sp_lc2", "sp_lc3"] if side < 0 else ["sp_rc1", "sp_rc2", "sp_rc3"]
    def top_y(z):
        return CON_TOP + (z - (CON_Z0 + 0.03)) / ((CON_Z1 - 0.06) - (CON_Z0 + 0.03)) * 0.015
    x0, x1 = CON_IN + 0.012, xo - 0.012
    if side < 0:
        segs = [(6.17, 6.40, names[2]), (6.74, 6.94, names[0])]   # throttle quadrant in between (6.42..6.72)
    else:
        segs = [(6.17, 6.42, names[1]), (6.44, 6.68, names[2]), (6.70, 6.94, names[0])]
    for za, zb, nm in segs:
        ya, yb = top_y(za) + 0.003, top_y(zb) + 0.003
        if side < 0:
            quad_uv("tub", [(-x1, ya, za), (-x0, ya, za), (-x0, yb, zb), (-x1, yb, zb)], rect_uv(nm))
        else:
            quad_uv("tub", [(x0, ya, za), (x1, ya, za), (x1, yb, zb), (x0, yb, zb)], rect_uv(nm))
    # a few real knobs and toggles standing on the decals
    for k in range(4):
        z = 6.78 + k * 0.045
        cylinder("tub", (side * (x0 + 0.03 + (k % 2) * 0.05), top_y(z) + 0.012, z), (0, 1, 0), 0.009, 0.022, "black", 12)
    for k in range(5):
        z = 6.48 + k * 0.04 if side > 0 else 6.22 + k * 0.035
        cylinder("tub", (side * (x1 - 0.03), top_y(z) + 0.012, z), (0, 1, 0.25), 0.0025, 0.022, "metal", 6)

# equipment boxes on the side walls above the consoles (decal faces toward the pilot's side)
for side, cell, z0, z1 in ((-1, "sp_lc3", 6.18, 6.40), (1, "sp_aft", 6.20, 6.42), (1, "sp_rc3", 6.46, 6.66)):
    y0, y1 = CON_TOP + 0.02, CON_TOP + 0.095
    xw = min(wall_x(y, z) for y in (y0, y1) for z in (z0, z1)) - 0.002
    xf = xw - 0.035
    box("tub", (side * (xw + xf) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (xw - xf, y1 - y0, z1 - z0), "black", 0.004)
    X, a0, a1, b0, b1 = side * (xf - 0.001), y0 + 0.004, y1 - 0.004, z0 + 0.004, z1 - 0.004
    # corners bottom-left, bottom-right, top-right, top-left as the pilot sees the wall
    q = [(X, a0, b1), (X, a0, b0), (X, a1, b0), (X, a1, b1)] if side > 0 else [(X, a0, b0), (X, a0, b1), (X, a1, b1), (X, a1, b0)]
    quad_uv("tub", q, rect_uv(cell))

# throttle quadrant (left console): slotted cover, gate plate
TQ_Z0, TQ_Z1 = 6.42, 6.72
xq0, xq1 = -(wall_x(0.68, 6.57) - 0.012), -(CON_IN + 0.012)
box("tub", ((xq0 + xq1) / 2, CON_TOP + 0.012, (TQ_Z0 + TQ_Z1) / 2), (xq1 - xq0, 0.02, TQ_Z1 - TQ_Z0), "black", 0.004)
box("tub", (-0.335, CON_TOP + 0.024, (TQ_Z0 + TQ_Z1) / 2), (0.045, 0.006, 0.27), "rubber", 0.002)          # slot
quad_uv("tub", [(xq0 + 0.005, CON_TOP + 0.0235, TQ_Z0 + 0.01), (-0.362, CON_TOP + 0.0235, TQ_Z0 + 0.01), (-0.362, CON_TOP + 0.0235, TQ_Z1 - 0.01), (xq0 + 0.005, CON_TOP + 0.0235, TQ_Z1 - 0.01)], rect_uv("placard"))

# ------------------------------------------------------------------------------------------------ instrument panel
TILT = math.radians(14)    # panel top leans away from the pilot
PB, PT = 0.60, 0.935       # panel bottom/top heights at the centre section
pv = Vector((0, math.sin(TILT), math.cos(TILT)))       # (Unity: dy... ) panel 'up' direction in Unity (x, y=up, z=fwd): see below
# panel frame in Unity coords: origin at the bottom centre, u = +x, v = up along the face, n = toward the pilot
UP = Vector((0.0, math.cos(TILT), math.sin(TILT)))     # Unity (x, y, z)
NRM = Vector((0.0, math.sin(TILT), -math.cos(TILT)))
ORG = Vector((0.0, PB, Z_PANEL - (0.5 * (PT - PB)) * math.tan(TILT)))
PH = (PT - PB) / math.cos(TILT)                        # panel face height along UP


def pp(u, v, off=0.0, yaw_side=0, u_hinge=0.2, yaw=math.radians(28)):
    """Point on the panel face (Unity tuple). Wings beyond |u| > u_hinge fold toward the pilot by yaw."""
    if yaw_side and abs(u) > u_hinge:
        s = 1 if u > 0 else -1
        du = abs(u) - u_hinge
        base = ORG + Vector((s * u_hinge, 0, 0)) + UP * v
        dirv = Vector((s * math.cos(yaw), 0, -math.sin(yaw)))
        nrm = Vector((-s * math.sin(yaw) * math.cos(TILT), math.cos(yaw) * math.sin(TILT), -math.cos(yaw) * math.cos(TILT))).normalized()   # = dirv x UP, toward the pilot
        q = base + dirv * du + nrm * off
    else:
        q = ORG + Vector((u, 0, 0)) + UP * v + NRM * off
    return (q.x, q.y, q.z)


def panel_normal(u, yaw=math.radians(28), u_hinge=0.2):
    if abs(u) > u_hinge:
        s = 1 if u > 0 else -1
        return Vector((-s * math.sin(yaw) * math.cos(TILT), math.cos(yaw) * math.sin(TILT), -math.cos(yaw) * math.cos(TILT))).normalized()   # = dirv x UP, toward the pilot
    return NRM


U_WING = 0.34
# panel face: centre + two folded wings, as a 2 cm slab with a box enclosure behind it
for (ua, ub, vb, vt) in ((-0.2, 0.2, 0.0, PH), (0.2, U_WING, 0.04, PH - 0.03), (-U_WING, -0.2, 0.04, PH - 0.03)):
    fr = [pp(ua, vb, 0, 1), pp(ub, vb, 0, 1), pp(ub, vt, 0, 1), pp(ua, vt, 0, 1)]
    bk = [pp(ua, vb, -0.03, 1), pp(ub, vb, -0.03, 1), pp(ub, vt, -0.03, 1), pp(ua, vt, -0.03, 1)]
    poly_paint("tub", fr, "turq")
    poly_paint("tub", [bk[1], bk[0], bk[3], bk[2]], "turq")
    for i in range(4):
        poly_paint("tub", [fr[(i + 1) % 4], fr[i], bk[i], bk[(i + 1) % 4]], "turq_dark")
# enclosure behind the panel down to the footwell (keeps the nose interior closed)
eb = [pp(-U_WING, 0.04, -0.03, 1), pp(U_WING, 0.04, -0.03, 1)]
poly_paint("tub", [(eb[0][0], eb[0][1], eb[0][2]), (eb[1][0], eb[1][1], eb[1][2]), (eb[1][0], FLOOR + 0.25, Z_FRONT), (eb[0][0], FLOOR + 0.25, Z_FRONT)][::-1], "dgrey")

# lower centre pedestal between the pilot's legs
ped_top = pp(0, 0.0, 0)
ped = [(-0.085, PB, ped_top[2]), (0.085, PB, ped_top[2]), (0.085, FLOOR + 0.06, 7.10), (-0.085, FLOOR + 0.06, 7.10)]
prism("tub", [(x, y, z) for x, y, z in ped], (0, 0.0, 0.25), "turq")
quad_uv("tub", [(-0.07, FLOOR + 0.10, 7.11), (0.07, FLOOR + 0.10, 7.11), (0.07, PB - 0.015, ped_top[2] - 0.004), (-0.07, PB - 0.015, ped_top[2] - 0.004)], rect_uv("sp_ped"))


INS = {}   # moving instrument parts: name -> pivot / axis (toward the pilot) / up, Unity MiG frame


def gauge(u, v, d, cell, yaw_side=1, face_z=0.010, face_part="tub"):
    """Round instrument: bezel ring standing proud of the panel, recessed dial face with the atlas cell.
    Returns the dial frame (centre, normal toward the pilot, right, up, radius) for needles."""
    c = Vector(pp(u, v, 0.0, yaw_side)); n = panel_normal(u)
    r = d / 2
    cylinder("tub", tuple(c + n * 0.007), tuple(n), r * 1.16, 0.014, "black", 28, cap=False)
    cylinder("tub", tuple(c + n * 0.014), tuple(n), r * 1.16, 0.0015, "dgrey", 28, cap=False, r2=r * 1.02)
    # bezel front ring as a flat annulus
    a = n.orthogonal().normalized(); b = n.cross(a).normalized()
    # orient the dial upright: a = panel-right, b = panel-up
    right = Vector((1, 0, 0)) if abs(u) <= 0.2 else Vector(pp(u + 0.01, v, 0, yaw_side)) - Vector(pp(u, v, 0, yaw_side))
    right.normalize(); up = right.cross(n).normalized() * -1
    if up.y < 0: up = -up
    segs = 32
    ring_o = [c + n * 0.014 + (right * math.cos(t) + up * math.sin(t)) * r * 1.16 for t in [2 * math.pi * k / segs for k in range(segs)]]
    ring_i = [c + n * 0.014 + (right * math.cos(t) + up * math.sin(t)) * r * 1.0 for t in [2 * math.pi * k / segs for k in range(segs)]]
    for k in range(segs):
        q = [ring_o[k], ring_o[(k + 1) % segs], ring_i[(k + 1) % segs], ring_i[k]]
        poly_paint("tub", q[::-1], "black")
    # dial face (fan) recessed 4 mm behind the bezel front
    u0, v0, u1, v1 = rect_uv(cell, 0.5)
    cu, cv, hu, hv = (u0 + u1) / 2, (v0 + v1) / 2, (u1 - u0) / 2, (v1 - v0) / 2
    fc = c + n * face_z
    pts = [fc + (right * math.cos(t) + up * math.sin(t)) * r for t in [2 * math.pi * k / segs for k in range(segs)]]
    uvs = [(cu + math.cos(2 * math.pi * k / segs) * hu, cv + math.sin(2 * math.pi * k / segs) * hv) for k in range(segs)]
    PARTS[part(face_part)].face([P(*p) for p in pts], uvs)
    if face_part != "tub":   # a rotating card: a plain black disk behind it keeps the panel closed
        PARTS["tub"].face([P(*(p - n * 0.0015)) for p in pts], [((u0 + u1) / 2, (v0 + v1) / 2)] * segs)
    # inner wall of the bezel
    wall = [c + n * min(face_z, 0.010) + (right * math.cos(t) + up * math.sin(t)) * r for t in [2 * math.pi * k / segs for k in range(segs)]]
    for k in range(segs):
        q = [wall[k], wall[(k + 1) % segs], ring_i[(k + 1) % segs], ring_i[k]]
        poly_paint("tub", q, "black")
    return {"c": c, "n": n, "right": right, "up": up, "r": r}


def part(name):
    if name not in PARTS:
        PARTS[name] = Part(name)
    return name


def flat(pid, center, right, up, w, h, paint, uvrect=None):
    """Flat rectangle facing the pilot (corners bottom-left, bottom-right, top-right, top-left as seen from the seat)."""
    q = [center - right * w / 2 - up * h / 2, center + right * w / 2 - up * h / 2, center + right * w / 2 + up * h / 2, center - right * w / 2 + up * h / 2]
    if uvrect:
        quad_uv(part(pid), [tuple(x) for x in q], uvrect)
    else:
        poly_paint(part(pid), [tuple(x) for x in q], paint)


def needle(gid, f, length, width, paint="white", z=0.0115, tail=0.2):
    """A needle on its own pivot, pointing at 12 o'clock (the plugin turns it clockwise)."""
    c, n, right, up = f["c"], f["n"], f["right"], f["up"]
    base = c + n * z; pid = part("ins_" + gid)
    L = length * f["r"]; w = width * f["r"]
    q = [base - up * L * tail - right * w / 2, base - up * L * tail + right * w / 2, base + up * L + right * w * 0.12, base + up * L - right * w * 0.12]
    poly_paint(pid, [tuple(x) for x in q], paint)
    cylinder(pid, tuple(base + n * 0.0008), tuple(n), max(w * 0.9, 0.0025), 0.0016, "black", 12)
    INS["ins_" + gid] = (base, n, up)


def lamp(lid, u, v, w, h, uvrect, off):
    """Lit-state quad of a lamp on the panel at (u, v), just in front of its unlit picture (the plugin switches it on and off)."""
    quad_uv(part("lamp_" + lid), [pp(u - w / 2, v - h / 2, off, 1), pp(u + w / 2, v - h / 2, off, 1), pp(u + w / 2, v + h / 2, off, 1), pp(u - w / 2, v + h / 2, off, 1)], uvrect)


def sub_uv(cell, x0, y0, x1, y1):
    u0, v0, u1, v1 = rect_uv(cell, 0.5)
    return (u0 + x0 * (u1 - u0), v1 - y1 * (v1 - v0), u0 + x1 * (u1 - u0), v1 - y0 * (v1 - v0))


def panel_frame(u, v, off):
    c = Vector(pp(u, v, off, 1)); n = panel_normal(u)
    right = (Vector(pp(u + 0.01, v, 0, 1)) - Vector(pp(u, v, 0, 1))).normalized()
    up = right.cross(n).normalized() * -1
    if up.y < 0: up = -up
    return c, n, right, up


def plate(u, v, w, h, cell, off=0.003, yaw_side=1):
    """Decal plate on the panel face, centred at (u, v)."""
    cs_ = [pp(u - w / 2, v - h / 2, off, yaw_side), pp(u + w / 2, v - h / 2, off, yaw_side), pp(u + w / 2, v + h / 2, off, yaw_side), pp(u - w / 2, v + h / 2, off, yaw_side)]
    quad_uv("tub", cs_, rect_uv(cell))
    return cs_


# centre section (MiG-29 9.12 style T layout)
# attitude indicator: a real ball behind a black mask with the bank scale, fixed orange aircraft symbol in front
f = gauge(0.0, 0.245, 0.112, "g_adi", face_z=0.0012)
BALL_R, BALL_BACK = 0.10, 0.087
ball_c = f["c"] - f["n"] * BALL_BACK
bu0, bv0, bu1, bv1 = rect_uv("adi_ball", 1.0)
pid = part("ins_adi_ball"); NLON, NLAT = 36, 30
def ball_pt(i, j):
    lon = -math.pi + 2 * math.pi * i / NLON; lat = -math.pi / 2 + math.pi * j / NLAT
    dvec = f["up"] * math.sin(lat) + (f["n"] * math.cos(lon) + f["right"] * math.sin(lon)) * math.cos(lat)
    return ball_c + dvec * BALL_R, (bu0 + (i / NLON) * (bu1 - bu0), bv0 + (j / NLAT) * (bv1 - bv0))
for i in range(NLON):
    for j in range(NLAT):
        q = [ball_pt(i, j), ball_pt(i + 1, j), ball_pt(i + 1, j + 1), ball_pt(i, j + 1)]
        PARTS[pid].face([P(*x[0]) for x in q], [x[1] for x in q])
INS["ins_adi_ball"] = (ball_c, f["n"], f["up"])
sym = f["c"] + f["n"] * 0.0138
for sx in (-1, 1):
    flat("tub", sym + f["right"] * sx * 0.026, f["right"], f["up"], 0.026, 0.0032, "amber")
flat("tub", sym, f["right"], f["up"], 0.006, 0.006, "amber")
flat("tub", sym - f["up"] * 0.006, f["right"], f["up"], 0.0025, 0.009, "amber")

# HSI: the compass card turns with the heading; lubber line and aircraft symbol are fixed
f = gauge(0.0, 0.110, 0.104, "g_hsi", face_part=part("ins_hsi_card"))
INS["ins_hsi_card"] = (f["c"] + f["n"] * 0.010, f["n"], f["up"])
top = f["c"] + f["n"] * 0.0125 + f["up"] * f["r"] * 0.97
PARTS["tub"].face([P(*top), P(*(top + f["up"] * 0.0001 - f["right"] * 0.004 + f["up"] * 0.006)), P(*(top + f["right"] * 0.004 + f["up"] * 0.006))][::-1],
                  [((rect_uv("paint_white")[0] + rect_uv("paint_white")[2]) / 2, (rect_uv("paint_white")[1] + rect_uv("paint_white")[3]) / 2)] * 3)
hs = f["c"] + f["n"] * 0.0125
flat("tub", hs, f["right"], f["up"], 0.022, 0.0025, "white")
flat("tub", hs - f["up"] * 0.003, f["right"], f["up"], 0.0025, 0.020, "white")
flat("tub", hs - f["up"] * 0.012, f["right"], f["up"], 0.010, 0.0022, "white")

f = gauge(-0.125, 0.268, 0.080, "g_asi"); needle("asi_kmh", f, 0.86, 0.09); needle("asi_mach", f, 0.40, 0.08, "yellow", z=0.0122)
f = gauge(-0.125, 0.165, 0.080, "g_alt"); needle("alt_km", f, 0.52, 0.15); needle("alt_m", f, 0.88, 0.08, z=0.0122)
f = gauge(0.125, 0.268, 0.080, "g_aoa"); needle("aoa", f, 0.86, 0.08, "yellow"); needle("g", f, 0.86, 0.08, z=0.0122)
f = gauge(0.125, 0.165, 0.080, "g_vsi"); needle("vsi", f, 0.88, 0.09)
f = gauge(-0.13, 0.065, 0.058, "g_radalt"); needle("radalt", f, 0.84, 0.10)
f = gauge(0.13, 0.065, 0.058, "g_clock"); needle("clock_h", f, 0.50, 0.14); needle("clock_m", f, 0.82, 0.08, z=0.0120); needle("clock_s", f, 0.88, 0.035, "red", z=0.0126)
plate(0.0, PH - 0.018, 0.16, 0.026, "placard")
GL_U, GL_V = -0.178, 0.040
c, n, right, up = panel_frame(GL_U, GL_V, 0.0)
flat("tub", c + n * 0.003, right, up, 0.03, 0.06, "black")                       # lever plate
flat("tub", c + n * 0.0035, right, up, 0.006, 0.045, "rubber")                    # slot
pid = part("ins_gear_lever")
piv = c + n * 0.008
tube(pid, [tuple(piv), tuple(piv + up * 0.03 + n * 0.012)], 0.0035, "metal", 8)
cylinder(pid, tuple(piv + up * 0.034 + n * 0.014), tuple(right), 0.009, 0.010, "white", 14)   # wheel-shaped knob
cylinder(pid, tuple(piv + up * 0.034 + n * 0.014), tuple(right), 0.0095, 0.004, "red", 14)
INS[pid] = (piv, n, up)

# left wing: SPO-15 radar warning receiver (square display, lit sector / power / type lamps), gear lights, small gauges
SPO_U, SPO_V, SPO_S = -0.29, 0.255, 0.086
plate(SPO_U, SPO_V, SPO_S + 0.008, SPO_S + 0.008, "paint_black", off=0.0035)
plate(SPO_U, SPO_V, SPO_S, SPO_S, "g_spo", off=0.0042)
spo = ATLAS["layout"]["spo"]
for k, (brg, x, y, sz) in enumerate(spo["sectors"]):
    lamp(f"spo_s{k}", SPO_U + (x - 0.5) * SPO_S, SPO_V + (0.5 - y) * SPO_S, sz * SPO_S, sz * SPO_S, rect_uv("lamp_lit_red", 6), 0.0050)
for k, (x, y, w, h) in enumerate(spo["power"]):
    lamp(f"spo_p{k}", SPO_U + (x - 0.5) * SPO_S, SPO_V + (0.5 - y) * SPO_S, w * SPO_S, h * SPO_S, rect_uv("lamp_lit_amber", 6), 0.0050)
for k, (t, x, y, w, h) in enumerate(spo["types"]):
    lamp(f"spo_t{k}", SPO_U + (x - 0.5) * SPO_S, SPO_V + (0.5 - y) * SPO_S, w * SPO_S * 0.8, h * SPO_S * 0.8, rect_uv("lamp_lit_amber", 6), 0.0050)
GEAR_U, GEAR_V, GW, GH = -0.29, 0.163, 0.11, 0.055
plate(GEAR_U, GEAR_V, GW, GH, "gear_plate")
for gid, x, y, sz in ATLAS["layout"]["gear"]:
    gu, gv = GEAR_U + (x - 0.5) * GW, GEAR_V + (0.5 - y) * GH
    lamp(f"gear_{gid}_g", gu, gv, sz * GW * 0.95, sz * GW * 0.95, rect_uv("lamp_lit_green", 6), 0.0040)
    lamp(f"gear_{gid}_r", gu, gv, sz * GW * 0.95, sz * GW * 0.95, rect_uv("lamp_lit_red", 6), 0.0042)
f = gauge(-0.255, 0.075, 0.05, "g_oxy"); needle("oxy", f, 0.84, 0.11)
f = gauge(-0.325, 0.075, 0.05, "g_cabin"); needle("cabin", f, 0.84, 0.11)
# right wing: radar / tactical display, engine instruments, caution panel
f = gauge(0.255, 0.165, 0.062, "g_rpm"); needle("rpm_l", f, 0.86, 0.09); needle("rpm_r", f, 0.72, 0.09, "yellow", z=0.0122)
f = gauge(0.33, 0.165, 0.062, "g_egt"); needle("egt_l", f, 0.86, 0.09); needle("egt_r", f, 0.72, 0.09, "yellow", z=0.0122)
f = gauge(0.255, 0.091, 0.05, "g_fuel"); needle("fuel", f, 0.84, 0.11)
f = gauge(0.325, 0.091, 0.05, "g_hyd"); needle("hyd", f, 0.84, 0.11)
CAU_U, CAU_V, CAU_W, CAU_H = 0.29, 0.034, 0.15, 0.040
plate(CAU_U, CAU_V, CAU_W, CAU_H, "caution")
cl = ATLAS["layout"]["caution"]
for k, cid in enumerate(cl["ids"]):
    i, j = k % cl["cols"], k // cl["cols"]
    x0, x1 = i / cl["cols"], (i + 1) / cl["cols"]; y0, y1 = j / cl["rows"], (j + 1) / cl["rows"]
    lamp(f"cau_{cid}", CAU_U + ((x0 + x1) / 2 - 0.5) * CAU_W, CAU_V + (0.5 - (y0 + y1) / 2) * CAU_H, CAU_W / cl["cols"], CAU_H / cl["rows"],
         sub_uv("caution_lit", x0, y0, x1, y1), 0.0040)

# panel trim: black edge strips around the centre section, screws, setting knobs
def strip(u0, v0, u1, v1, w=0.008, off=0.004):
    a, b = Vector(pp(u0, v0, off, 1)), Vector(pp(u1, v1, off, 1))
    d = (b - a).normalized(); n = panel_normal((u0 + u1) / 2); side = d.cross(n).normalized() * w / 2
    poly_paint("tub", [tuple(a - side), tuple(b - side), tuple(b + side), tuple(a + side)][::-1], "black")
for (u0, v0, u1, v1) in ((-0.2, 0.003, 0.2, 0.003), (-0.2, PH - 0.004, 0.2, PH - 0.004), (-0.197, 0.0, -0.197, PH), (0.197, 0.0, 0.197, PH)):
    strip(u0, v0, u1, v1)
for (u, v) in ((-0.185, 0.015), (0.185, 0.015), (-0.185, PH - 0.035), (0.185, PH - 0.035), (-0.27, 0.33), (0.27, 0.33), (-0.32, 0.055), (0.32, 0.055)):
    c = Vector(pp(u, v, 0.002, 1)); cylinder("tub", tuple(c), tuple(panel_normal(u)), 0.004, 0.004, "metal", 8)
for (u, v) in ((-0.078, 0.14), (0.078, 0.14), (-0.172, 0.20), (0.172, 0.20), (-0.06, 0.03), (0.06, 0.03)):
    n = panel_normal(u); c = Vector(pp(u, v, 0.0, 1))
    cylinder("tub", tuple(c + n * 0.009), tuple(n), 0.0075, 0.018, "black", 12)
    cylinder("tub", tuple(c + n * 0.019), tuple(n), 0.0055, 0.004, "dgrey", 12)

# tac screen: bezel plate + the screen quad (the game's render texture, main area u 0-0.75 v 0.25-1, 2.1:1)
SW, SH = 0.165, 0.079
sc = plate(0.29, 0.262, SW + 0.026, SH + 0.034, "radar_frame", off=0.004)
cu, cvv = 0.29, 0.262
corners = [pp(cu - SW / 2, cvv - SH / 2, 0.006, 1), pp(cu + SW / 2, cvv - SH / 2, 0.006, 1), pp(cu + SW / 2, cvv + SH / 2, 0.006, 1), pp(cu - SW / 2, cvv + SH / 2, 0.006, 1)]
quad_uv("screen", corners, (0.0, 0.25, 0.75, 1.0))
# hood around the screen
c = Vector(pp(cu, cvv, 0)); n = panel_normal(cu)

# ------------------------------------------------------------------------------------------------ glareshield + HUD
GS_Y = PT + 0.012
gs_back = pp(0, PH, 0)[2] - 0.10     # hood overhangs the panel by 10 cm
gs_front = 7.33
for side in (-1, 1):
    pass
hood = []
xs = [-0.36, -0.30, -0.20, 0.0, 0.20, 0.30, 0.36]
def hood_y(x):
    return GS_Y + 0.025 * (1 - (x / 0.36) ** 2)
def hood_yf(x):
    return under_glass(x, hood_y(x), gs_front, 0.025)
for x0, x1 in zip(xs, xs[1:]):
    poly_paint("tub", [(x0, hood_y(x0), gs_back), (x1, hood_y(x1), gs_back), (x1, hood_yf(x1), gs_front), (x0, hood_yf(x0), gs_front)], "black")
    poly_paint("tub", [(x1, hood_y(x1) - 0.02, gs_back), (x0, hood_y(x0) - 0.02, gs_back), (x0, hood_y(x0), gs_back), (x1, hood_y(x1), gs_back)], "black")
    poly_paint("tub", [(x0, hood_y(x0) - 0.02, gs_back), (x1, hood_y(x1) - 0.02, gs_back), (x1, hood_yf(x1) - 0.02, gs_front), (x0, hood_yf(x0) - 0.02, gs_front)][::-1], "black")
tube("tub", [(x, hood_y(x) - 0.006, gs_back) for x in [-0.36 + i * 0.06 for i in range(13)]], 0.012, "rubber")   # padded lip
# coaming deck from the hood forward to the windscreen base
deck = [gs_front + i * (7.92 - gs_front) / 8 for i in range(9)]
dxs = [-1, -0.75, -0.5, -0.25, 0, 0.25, 0.5, 0.75, 1]
def deck_pt(f, z):
    w = min(0.36, wall_x(sill_y(z), z) - 0.005)
    x = f * w
    y = under_glass(x, min(hood_yf(x) if z <= gs_front + 1e-6 else 9, sill_y(z) + 0.06 * (1 - abs(f))), z, 0.025)
    return (x, y, z)
for z0, z1 in zip(deck, deck[1:]):
    for f0, f1 in zip(dxs, dxs[1:]):
        poly_paint("tub", [deck_pt(f0, z0), deck_pt(f1, z0), deck_pt(f1, z1), deck_pt(f0, z1)], "black")
# dark back wall behind the whole panel: closes the gaps around the folded side panels (the fuselage skin is hidden in the cockpit
# view, so any gap there looks straight outside)
Z_BW = 7.37
bw_ys = [PB - 0.06 + (GS_Y - (PB - 0.06)) * k / 6 for k in range(7)]
for y0, y1 in zip(bw_ys, bw_ys[1:]):
    w0, w1 = min(0.38, wall_x(y0, Z_BW) - 0.004), min(0.38, wall_x(y1, Z_BW) - 0.004)
    def bwp(x, y): return (x, under_glass(x, y, Z_BW, 0.02), Z_BW)
    poly_paint("tub", [bwp(-w0, y0), bwp(w0, y0), bwp(w1, y1), bwp(-w1, y1)], "black")
# HUD (ILS-31 style): housing on the hood, two posts, combiner glass
HZ = gs_back + 0.06
box("tub", (0, GS_Y + 0.055, HZ + 0.07), (0.15, 0.06, 0.16), "black", 0.008)
box("tub", (0, GS_Y + 0.088, HZ + 0.01), (0.13, 0.012, 0.06), "dgrey", 0.004)
hud_tilt = math.radians(28)
for s in (-1, 1):
    tube("tub", [(s * 0.062, GS_Y + 0.085, HZ + 0.02), (s * 0.062, GS_Y + 0.085 + 0.12 * math.cos(hud_tilt), HZ + 0.02 + 0.12 * math.sin(hud_tilt))], 0.006, "black", 8)
g0 = (GS_Y + 0.092, HZ + 0.02); g1 = (GS_Y + 0.092 + 0.115 * math.cos(hud_tilt), HZ + 0.02 + 0.115 * math.sin(hud_tilt))
gl = [(-0.058, g0[0], g0[1]), (0.058, g0[0], g0[1]), (0.058, g1[0], g1[1]), (-0.058, g1[0], g1[1])]
poly_paint("glass", gl, "black")
poly_paint("glass", gl[::-1], "black")
tube("tub", [(-0.058, g1[0], g1[1]), (0.058, g1[0], g1[1])], 0.004, "black", 6)

# master warning lamps either side of the HUD (game's warningLights renderer: stock texture cells)
LAMP_A = (0.165, 0.176, 0.188, 0.247)
LAMP_B = (0.865, 0.637, 0.888, 0.708)
for s, cells in ((-1, (LAMP_A, LAMP_B)), (1, (LAMP_A, LAMP_B))):
    for k, cell in enumerate(cells):
        x = s * (0.13 + k * 0.045); y = GS_Y + 0.028; z = gs_back + 0.012
        box("tub", (x, y, z + 0.006), (0.04, 0.03, 0.014), "dgrey", 0.003)
        uo = 1.0 if s > 0 else 0.0
        quad_uv("lamps", [(x - 0.016, y - 0.012, z - 0.002), (x + 0.016, y - 0.012, z - 0.002), (x + 0.016, y + 0.012, z - 0.002), (x - 0.016, y + 0.012, z - 0.002)],
                (cell[0] + uo, cell[1], cell[2] + uo, cell[3]))

# ------------------------------------------------------------------------------------------------ rudder pedals
for s in (-1, 1):
    pid = part("ins_pedal_" + ("l" if s < 0 else "r"))
    box(pid, (s * 0.13, 0.42, 7.62), (0.10, 0.16, 0.025), "dgrey", 0.006, rot=("x", -18))
    box(pid, (s * 0.13, 0.36, 7.66), (0.03, 0.06, 0.12), "black", 0.004)
    for k in range(3):
        box(pid, (s * 0.13, 0.38 + k * 0.035, 7.605 - k * 0.011), (0.09, 0.006, 0.008), "rubber", 0.002, rot=("x", -18))
    INS[pid] = (Vector((s * 0.13, 0.40, 7.63)), Vector((0.0, 0.0, -1.0)), Vector((0.0, 1.0, 0.0)))
    box("tub", (s * 0.13, FLOOR + 0.015, 7.66), (0.05, 0.03, 0.20), "black", 0.004)   # rail the pedal slides on

# ------------------------------------------------------------------------------------------------ ejection seat (K-36 style)
SEAT_O = (0.0, 0.30, 6.40)    # pivot (seat origin on the floor rails)
SB = 6.20                     # seat back plane at cushion height
def back_z(y):                # reclined back
    return SB - (y - 0.45) * math.tan(math.radians(13))
# bucket sides
for s in (-1, 1):
    side = [(s * 0.215, 0.33, 6.16), (s * 0.215, 0.33, 6.66), (s * 0.215, 0.47, 6.68), (s * 0.215, 0.56, 6.50), (s * 0.215, 0.72, back_z(0.72) + 0.06),
            (s * 0.215, 1.02, back_z(1.02) + 0.03), (s * 0.215, 1.02, back_z(1.02) - 0.07), (s * 0.215, 0.33, 6.05)]
    prism("seat", side if s > 0 else side[::-1], (s * 0.035, 0, 0), "seat")
    tube("seat", [(s * 0.235, 0.36, 6.00), (s * 0.235, 1.10, back_z(1.10) - 0.08)], 0.018, "dgrey", 10)   # guide rails
    for k in range(3):
        cylinder("seat", (s * 0.252, 0.50 + k * 0.22, back_z(0.5 + k * 0.22) - 0.07), (1, 0, 0), 0.016, 0.014, "metal", 12)
# seat pan cushion + survival kit
box("seat", (0, 0.40, 6.43), (0.40, 0.07, 0.46), "cushion", 0.03, segs=3)
box("seat", (0, 0.35, 6.42), (0.40, 0.05, 0.48), "seat", 0.01)
# back cushion
bc = Matrix.Translation(P(0, 0.72, back_z(0.72) + 0.03)) @ rot_matrix(("x", -13))
bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0); bmesh.ops.scale(bm, vec=Vector((0.38, 0.07, 0.56)), verts=bm.verts)
bmesh.ops.bevel(bm, geom=list(bm.edges), offset=0.028, segments=3, affect="EDGES"); add_bm("seat", bm, "cushion", bc); bm.free()
box("seat", (0, 0.72, back_z(0.72) - 0.03), (0.42, 0.62, 0.05), "seat", 0.01, rot=("x", -13))
# headrest / headbox
box("seat", (0, 1.115, back_z(1.115) + 0.0), (0.30, 0.17, 0.16), "headrest", 0.03, rot=("x", -13), segs=3)
box("seat", (0, 1.125, back_z(1.125) + 0.075), (0.20, 0.13, 0.025), "cushion", 0.012, rot=("x", -13))
for s in (-1, 1):
    box("seat", (s * 0.14, 1.12, back_z(1.12) + 0.06), (0.03, 0.15, 0.08), "headrest", 0.01, rot=("x", -13))
box("seat", (0, 1.205, back_z(1.205) - 0.02), (0.22, 0.012, 0.08), "yellow", 0.003, rot=("x", -13))
# harness straps
for s in (-1, 1):
    tube("seat", [(s * 0.09, 1.0, back_z(1.0) + 0.075), (s * 0.085, 0.80, back_z(0.80) + 0.08), (s * 0.06, 0.55, 6.32)], 0.008, "olive", 6)
    tube("seat", [(s * 0.17, 0.46, 6.25), (s * 0.08, 0.47, 6.40)], 0.007, "olive", 6)
# ejection handle loop between the legs
loop = [(-0.045, 0.415, 6.675), (-0.045, 0.455, 6.685), (-0.03, 0.47, 6.688), (0.03, 0.47, 6.688), (0.045, 0.455, 6.685), (0.045, 0.415, 6.675)]
tube("seat", loop, 0.009, "yellow", 8)
tube("seat", [(-0.022, 0.4705, 6.688), (-0.008, 0.4705, 6.688)], 0.0095, "black", 8)
tube("seat", [(0.008, 0.4705, 6.688), (0.022, 0.4705, 6.688)], 0.0095, "black", 8)
# leg guards
for s in (-1, 1):
    box("seat", (s * 0.19, 0.42, 6.64), (0.04, 0.10, 0.06), "dgrey", 0.008)

# ------------------------------------------------------------------------------------------------ centre stick (pivot = base boot)
STICK_O = (0.0, 0.40, 6.93)
box("tub", (0, FLOOR + 0.05, 6.93), (0.16, 0.10, 0.18), "black", 0.02)                      # boot housing (static)
cylinder("stick", (0, 0.43, 6.93), (0, 1, 0), 0.05, 0.05, "rubber", 16, r2=0.025)          # gaiter
tube("stick", [(0, 0.42, 6.93), (0, 0.56, 6.92), (0, 0.68, 6.90)], 0.013, "black", 10)
grip = [(0, 0.68, 6.90), (0, 0.72, 6.895), (0, 0.76, 6.89), (0, 0.785, 6.89)]
tube("stick", grip, 0.021, "rubber", 12)
sphere("stick", (0, 0.79, 6.89), 0.024, "rubber", (1, 0.7, 1.15))
box("stick", (0.0, 0.77, 6.868), (0.03, 0.03, 0.02), "black", 0.004)                        # trim hat
cylinder("stick", (0.0, 0.795, 6.885), (0, 1, 0), 0.007, 0.012, "red", 10)                   # trigger-guard button
box("stick", (0.0, 0.715, 6.918), (0.012, 0.045, 0.02), "metal", 0.003, rot=("x", 10))       # trigger
box("stick", (-0.022, 0.73, 6.90), (0.01, 0.012, 0.025), "dgrey", 0.003)                      # brake lever

# ------------------------------------------------------------------------------------------------ throttles (pivot = axle under the slot)
THR_O = (-0.335, 0.62, 6.57)
for k, dx in enumerate((-0.012, 0.012)):
    tube("throttle", [(-0.335 + dx, 0.62, 6.57), (-0.335 + dx, 0.73, 6.585), (-0.335 + dx, 0.78, 6.59)], 0.007, "dgrey", 8)
box("throttle", (-0.335, 0.80, 6.592), (0.06, 0.05, 0.075), "black", 0.012, segs=3)
box("throttle", (-0.362, 0.805, 6.60), (0.012, 0.026, 0.04), "dgrey", 0.004)
cylinder("throttle", (-0.335, 0.828, 6.60), (0, 1, 0), 0.008, 0.008, "red", 10)
box("throttle", (-0.302, 0.80, 6.612), (0.010, 0.018, 0.018), "dgrey", 0.003)

# ------------------------------------------------------------------------------------------------ the MiG model's own frames
def frames_part():
    """Windscreen bow and its mirrors from the MiG cockpit part (triangles hugging the glass at the bow station)."""
    ck = next(p for p in data["parts"] if p["name"] == "cockpit")
    v = ck["vertices"]; t = ck["triangles"]
    keep = []
    for i in range(0, len(t), 3):
        c = Vector((0, 0, 0))
        for k in range(3):
            j = t[i + k] * 3; c += Vector(cs.u2b(v[j], v[j + 1], v[j + 2]))
        c /= 3
        near = glass_bvh.find_nearest(c)
        d = (near[0] - c).length if near[0] is not None else 9
        # bow + sills hug the glass; mirrors sit up near the bow top
        in_bow = 6.88 < c.y < 7.32
        if in_bow and ((d < 0.045 and c.z > 0.86) or (c.z > 1.02 and abs(c.x) > 0.12)):
            keep += t[i:i + 3]
    used = sorted(set(keep)); remap = {o: n for n, o in enumerate(used)}
    out = {"name": "frames", "material": "skin",
           "vertices": [x for o in used for x in v[o * 3:o * 3 + 3]], "normals": [x for o in used for x in ck["normals"][o * 3:o * 3 + 3]],
           "uvs": [x for o in used for x in ck["uvs"][o * 2:o * 2 + 2]], "triangles": [remap[o] for o in keep]}
    print(f"[cockpit] frames: {len(keep) // 3} of {len(t) // 3} MiG cockpit tris")
    return out


# ------------------------------------------------------------------------------------------------ export
def winding_sign():
    b = next(p for p in data["parts"] if p["name"] == "body")
    v, t, n = b["vertices"], b["triangles"], b["normals"]
    acc = 0.0
    for i in range(0, min(len(t), 3000), 3):
        A, B, C = (Vector(v[t[i + k] * 3:t[i + k] * 3 + 3]) for k in range(3))
        nn = Vector(n[t[i] * 3:t[i] * 3 + 3])
        acc += (B - A).cross(C - A).dot(nn)
    return 1 if acc > 0 else -1


def export_part(part, sign, material, pivot=None):
    bm = part.bm
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    me = bpy.data.meshes.new(part.name); bm.to_mesh(me)
    for f in me.polygons: f.use_smooth = True
    me.set_sharp_from_angle(angle=math.radians(38))
    me.update()
    uvl = me.uv_layers.active.data
    cn = me.corner_normals
    verts, norms, uvs, tris, index = [], [], [], [], {}
    for poly in me.polygons:
        ids = []
        for li in poly.loop_indices:
            lo = me.loops[li]; co = me.vertices[lo.vertex_index].co; nn = cn[li].vector; uv = uvl[li].uv
            key = (round(co.x, 5), round(co.y, 5), round(co.z, 5), round(nn.x, 3), round(nn.y, 3), round(nn.z, 3), round(uv.x, 5), round(uv.y, 5))
            if key not in index:
                index[key] = len(verts) // 3
                verts += [co.x, co.z, co.y]; norms += [nn.x, nn.z, nn.y]; uvs += [uv.x, uv.y]
            ids.append(index[key])
        # Blender -> Unity is a reflection; match the airframe's stored winding convention
        a, b, c = ids
        A, B, C = (Vector(verts[i * 3:i * 3 + 3]) for i in (a, b, c))
        nrm = Vector(norms[a * 3:a * 3 + 3]) + Vector(norms[b * 3:b * 3 + 3]) + Vector(norms[c * 3:c * 3 + 3])
        if (B - A).cross(C - A).dot(nrm) * sign < 0:
            b, c = c, b
        tris += [a, b, c]
    out = {"name": part.name, "material": material, "vertices": verts, "normals": norms, "uvs": uvs, "triangles": tris}
    if pivot is not None:
        out["pivot"] = list(pivot)
    print(f"[cockpit] {part.name}: {len(tris) // 3} tris, {len(verts) // 3} verts")
    return out, me


sign = winding_sign()
parts_out, meshes = [], {}
for name, mat, pivot in (("tub", "cockpit", None), ("glass", "glass", None), ("seat", "cockpit", SEAT_O), ("stick", "cockpit", STICK_O),
                         ("throttle", "cockpit", THR_O), ("screen", "screen", None), ("lamps", "lamps", None)):
    o, me = export_part(PARTS[name], sign, mat, pivot)
    parts_out.append(o); meshes[name] = me
for name in sorted(k for k in PARTS if k.startswith("ins_") or k.startswith("lamp_")):
    o, me = export_part(PARTS[name], sign, "cockpit", None)
    if name in INS:
        piv, ax, upv = INS[name]
        o["pivot"] = list(piv); o["axis"] = list(ax); o["up"] = list(upv)
    parts_out.append(o); meshes[name] = me
parts_out.append(frames_part())
json.dump({"parts": parts_out, "eye": list(EYE)}, open(os.path.join(SRC, "cockpit_mesh.json"), "w"))
print("[cockpit] wrote cockpit_mesh.json")

# clearance checks against the airframe
for name in ("tub", "seat"):
    me = meshes[name]
    worst = 0
    for vtx in me.vertices:
        gt = glass_top(vtx.co.x, vtx.co.y)
        if gt is not None and vtx.co.z > gt - 0.01 and vtx.co.z - (gt - 0.01) > worst:
            worst = vtx.co.z - (gt - 0.01); wpos = (round(vtx.co.x, 3), round(vtx.co.z, 3), round(vtx.co.y, 3))
    print(f"[cockpit] {name}: max poke through canopy glass {worst * 1000:.1f} mm" + (f" at {wpos}" if worst > 0 else ""))
    outside = []
    for vtx in me.vertices:
        c = vtx.co
        if abs(c.x) < 0.05: continue
        d = Vector((1 if c.x > 0 else -1, 0, 0))
        if body_bvh.ray_cast(c + d * 0.003, d, 2.0)[0] is None and glass_bvh.ray_cast(c + d * 0.003, d, 2.0)[0] is None:
            outside.append((round(c.x, 3), round(c.z, 3), round(c.y, 3)))
    print(f"[cockpit] {name}: {len(outside)} verts outside the airframe (unity x,y,z): {sorted(set(outside))[:40]}")

# ------------------------------------------------------------------------------------------------ preview renders
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    scene = bpy.context.scene
    img = bpy.data.images.load(os.path.join(SRC, "cockpit_atlas.png"))
    skin = bpy.data.images.load(os.path.join(SRC, "mig29_basecolor.png"))

    def mat(name, image=None, color=(0.5, 0.5, 0.5, 1), alpha=1.0, rough=0.6, emit=None):
        m = bpy.data.materials.new(name); m.use_nodes = True
        bsdf = m.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Roughness"].default_value = rough
        if image:
            tex = m.node_tree.nodes.new("ShaderNodeTexImage"); tex.image = image
            m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        else:
            bsdf.inputs["Base Color"].default_value = color
        if alpha < 1:
            bsdf.inputs["Alpha"].default_value = alpha
            m.surface_render_method = "BLENDED" if hasattr(m, "surface_render_method") else None
        if emit:
            bsdf.inputs["Emission Color"].default_value = emit; bsdf.inputs["Emission Strength"].default_value = 1.0
        return m

    m_ck = mat("ck", img); m_glass = mat("glass", color=(0.6, 0.75, 0.7, 1), alpha=0.15, rough=0.05)
    m_screen = mat("screen", color=(0.02, 0.1, 0.06, 1), emit=(0.05, 0.35, 0.18, 1)); m_lamp = mat("lamp", color=(0.4, 0.1, 0.05, 1))
    m_skin = mat("skin", skin); m_body = mat("body", color=(0.45, 0.47, 0.5, 1), rough=0.5)
    for name, me in meshes.items():
        ob = bpy.data.objects.new("ck_" + name, me); scene.collection.objects.link(ob)
        ob.data.materials.append({"glass": m_glass, "screen": m_screen, "lamps": m_lamp}.get(name, m_ck))
    # frames as an object
    fr = parts_out[-1]
    fv = [cs.u2b(*fr["vertices"][i:i + 3]) for i in range(0, len(fr["vertices"]), 3)]
    ff = [tuple(fr["triangles"][i:i + 3]) for i in range(0, len(fr["triangles"]), 3)]
    fme = bpy.data.meshes.new("frames"); fme.from_pydata(fv, [], ff); fme.update()
    uvl = fme.uv_layers.new(name="UVMap")
    for li, lo in enumerate(fme.loops):
        vi = lo.vertex_index; uvl.data[li].uv = (fr["uvs"][vi * 2], fr["uvs"][vi * 2 + 1])
    for f in fme.polygons: f.use_smooth = True
    fob = bpy.data.objects.new("frames", fme); scene.collection.objects.link(fob); fme.materials.append(m_skin)
    obs["body"].data.materials.append(m_body)
    obs["canopy"].data.materials.append(m_glass)
    obs["cockpit"].hide_render = True
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.68, 0.85, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0
    sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 3.5; so = bpy.data.objects.new("sun", sun); scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(40), math.radians(15), math.radians(30))
    scene.render.resolution_x, scene.render.resolution_y = 1600, 1000
    scene.view_settings.view_transform = "Standard"
    E = P(*EYE)
    views = {"fwd": (E, E + P(0, -0.30, 1)), "down": (E, E + P(0, -0.75, 0.6)), "left": (E, E + P(-1, -0.6, 0.25)), "right": (E, E + P(1, -0.6, 0.25)),
             "back": (E, E + P(0.2, -0.3, -1)), "outside": (P(1.6, 1.9, 6.0), P(0, 0.75, 6.75)), "outside_front": (P(0.9, 1.6, 8.6), P(0, 0.85, 6.9))}
    for name, (loc, look) in views.items():
        cam = cs.camera(name, loc, look, lens=13 if name not in ("outside", "outside_front") else 30)
        scene.camera = cam
        scene.render.filepath = os.path.join(PREVIEW, f"ck_{name}.png")
        bpy.ops.render.render(write_still=True)
