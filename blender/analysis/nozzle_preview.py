"""Preview of the from-scratch nozzles (tools/nozzle_gen.py) on the MiG model, assembled exactly as MiG29Nozzles.cs places them,
with the model's own nozzle triangles stripped by the same rule. Eevee renders.
blender -b --python nozzle_preview.py -- <dir with mig29_mesh.json, mig29_basecolor.png, nozzles.json, nozzle_atlas.png> <out dir> [exit radius]"""
import bpy, os, sys, math, json
import numpy as np
from mathutils import Vector
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import cockpit_scene as cs

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], argv[1]
EXIT = float(argv[2]) if len(argv) > 2 else None
os.makedirs(OUT, exist_ok=True)
cs.clear()
noz = json.load(open(os.path.join(SRC, "nozzles.json")))
L = noz["layout"]
EXIT = EXIT or L["exit_rest"]


def axis(side):
    r = np.array(L["axis_root"], float); e = np.array(L["axis_end"], float)
    r[0] *= side; e[0] *= side
    return r, e


def strip_mask(V, T):
    """True for triangles of the model's nozzle (MiG29Nozzles.IsNozzleTri)."""
    c = V[T].mean(1)
    keep = np.ones(len(T), bool)
    for side in (-1, 1):
        r, e = axis(side)
        d = (r - e) / np.linalg.norm(r - e)               # forward
        rel = c - r
        s = -(rel @ d)                                    # distance aft of the seam
        radial = np.linalg.norm(rel - np.outer(rel @ d, d), axis=1)
        keep &= ~((s > -0.02) & (radial < 0.535))
    return keep


d = json.load(open(os.path.join(SRC, "mig29_mesh.json")))
body = next(p for p in d["parts"] if p["name"] == "body")
V = np.array(body["vertices"]).reshape(-1, 3); T = np.array(body["triangles"]).reshape(-1, 3)
UV = np.array(body["uvs"]).reshape(-1, 2)
keep = strip_mask(V, T)
print("[nozzle] stripped", (~keep).sum(), "model triangles")
T2 = T[keep]
me = bpy.data.meshes.new("body"); me.from_pydata([cs.u2b(*v) for v in V], [], [tuple(t) for t in T2]); me.update()
uvl = me.uv_layers.new(name="UVMap")
for li, lo in enumerate(me.loops): uvl.data[li].uv = UV[lo.vertex_index]
for f in me.polygons: f.use_smooth = True
ob = bpy.data.objects.new("body", me); bpy.context.collection.objects.link(ob)


def mat(name, img, rough=0.5, metal=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = rough; b.inputs["Metallic"].default_value = metal
    t = m.node_tree.nodes.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(os.path.join(SRC, img))
    m.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
    return m


ob.data.materials.append(mat("skin", "mig29_basecolor.png"))
m_noz = mat("noz", "nozzle_atlas.png", 0.45, 0.6)
parts = {p["name"]: p for p in noz["parts"]}


def rx(theta):   # Unity Euler(theta,0,0): y' = y cos - z sin, z' = y sin + z cos
    c, s = math.cos(theta), math.sin(theta)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def rz(phi):
    c, s = math.cos(phi), math.sin(phi)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def add(name, local_pts, tris, uvs, M, O):
    pts = (np.array(local_pts).reshape(-1, 3) @ M.T) + O
    me = bpy.data.meshes.new(name); me.from_pydata([cs.u2b(*p) for p in pts], [], [tuple(tris[i:i + 3]) for i in range(0, len(tris), 3)]); me.update()
    uvl = me.uv_layers.new(name="UVMap")
    for li, lo in enumerate(me.loops): uvl.data[li].uv = uvs[lo.vertex_index * 2:lo.vertex_index * 2 + 2]
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o); me.materials.append(m_noz)


for side in (-1, 1):
    r, e = axis(side)
    F = (r - e) / np.linalg.norm(r - e)
    U = np.array([0, 1.0, 0]); U = U - F * (U @ F); U /= np.linalg.norm(U)
    R = np.cross(U, F)
    B = np.column_stack([R, U, F])                       # local -> MiG frame
    st = parts["static"]
    add(f"static{side}", st["vertices"], st["triangles"], st["uvs"], B, r)
    n = L["count"]
    alpha = math.asin((L["r_root"] - EXIT) / L["l_flap"])
    beta = math.asin(((EXIT - 0.015) - L["r_throat"]) / L["l_inner"])
    for i in range(n):
        for kind, phi0, rad, z0, theta in (("flap", 0.0, L["r_root"], 0.0, -alpha),
                                           ("seal", 0.5, L["r_root"] - L["seal_inset"], 0.0, -alpha),
                                           ("inner", 0.25, L["r_throat"], -L["z_throat"], beta)):
            phi = 2 * math.pi * (i + phi0) / n
            Mz = rz(phi)
            hinge = Mz @ np.array([0, rad, z0])
            M = B @ Mz @ rx(theta)
            add(f"{kind}{side}_{i}", parts[kind]["vertices"], parts[kind]["triangles"], parts[kind]["uvs"], M, r + B @ hinge)

scene = bpy.context.scene
try:
    scene.render.engine = "BLENDER_EEVEE"
except TypeError:
    scene.render.engine = "BLENDER_EEVEE_NEXT"
world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.62, 0.72, 1)
sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 3.0; so = bpy.data.objects.new("sun", sun); scene.collection.objects.link(so)
so.rotation_euler = (math.radians(50), math.radians(10), math.radians(140))
scene.render.resolution_x, scene.render.resolution_y = 1400, 1000
scene.view_settings.view_transform = "Standard"
P = lambda x, y, z: Vector((x, z, y))
C = P(0.87, -0.29, -3.3)
for name, loc, look, lens in (("rear_q", P(2.4, 0.5, -6.4), C, 40), ("side", P(3.0, -0.2, -3.2), C, 40), ("aft", P(0.95, -0.25, -6.2), P(0.9, -0.29, -3.5), 45),
                              ("pair", P(0.0, 0.3, -8.5), P(0, -0.3, -3.4), 35)):
    cam = cs.camera(name, loc, look, lens=lens)
    scene.camera = cam
    scene.render.filepath = os.path.join(OUT, f"new_{name}.png")
    bpy.ops.render.render(write_still=True)
