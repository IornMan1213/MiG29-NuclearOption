"""Renders the gear poses dumped by MiG29Tools.MiG29GearDump (MiG29Out/gear_<pose>.json): skin grey, MiG doors blue, KR-67 gear
orange, landing-light pieces yellow. Back faces are culled as in Unity. Workbench renders.
blender -b --python gear_look.py -- <MiG29Out dir> <out dir> [poses comma list] [views comma list]"""
import bpy, os, sys, math, json
from mathutils import Vector
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import cockpit_scene as cs

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], argv[1]
POSES = argv[2].split(",") if len(argv) > 2 else ["down", "mid", "up"]
VIEWS = argv[3].split(",") if len(argv) > 3 else None
os.makedirs(OUT, exist_ok=True)
P = lambda x, y, z: Vector((x, z, y))

COL = {"skin": (0.62, 0.64, 0.66, 1), "door": (0.25, 0.45, 0.95, 1), "gear": (1.0, 0.45, 0.1, 1), "light": (1, 0.9, 0.1, 1),
       "well": (0.75, 0.2, 0.75, 1), "other": (0.3, 0.8, 0.3, 1)}
mats = {}


def make_mats():
    for k, c in COL.items():
        m = bpy.data.materials.new(k); m.diffuse_color = c; m.use_backface_culling = True; mats[k] = m


def cat(name, path):
    if "gearLight" in path: return "light"
    if name.startswith("MiG29_door"): return "door"
    if name.startswith("MiG29_well"): return "well"
    if name.startswith("MiG29_"): return "skin"
    if any(k in path for k in ("gear", "wheel", "axle", "bumpstop")): return "gear"
    return "other"


VIEW = {
    "below": (P(0, -30, 1.5), P(0, 0, 1.5), True, 9.5),
    "above": (P(0, 30, 1.5), P(0, 0, 1.5), True, 9.5),
    "side": (P(-30, -0.6, 2.5), P(0, -0.6, 2.5), True, 4.5),
    "front": (P(0, -0.8, 30), P(0, -0.8, 0), True, 3.2),
    "nose_low": (P(-1.6, -1.8, 6.6), P(0.2, -0.3, 3.8), False, 28),     # like the user's under-nose shot
    "main_low": (P(2.8, -2.2, 3.6), P(1.3, -0.2, 1.4), False, 28),
    "main_rear": (P(1.6, -1.2, -3.0), P(1.4, -0.3, 1.6), False, 30),
    "top_root": (P(0, 6, -2), P(0, 0, 2.0), False, 30),
    "nose_bay": (P(0.9, -2.0, 4.2), P(0, -0.1, 4.9), False, 26),
    "main_bay": (P(2.6, -1.6, 0.4), P(1.35, 0.0, 1.5), False, 26),
}

for pose in POSES:
    cs.clear()
    make_mats()
    d = json.load(open(os.path.join(SRC, f"gear_{pose}.json")))
    for p in d["parts"]:
        c = cat(p["name"], p["path"])
        V = p["v"]; T = p["t"]
        if not V or not T: continue
        verts = [P(V[i], V[i + 1], V[i + 2]) for i in range(0, len(V), 3)]
        faces = [(T[i], T[i + 2], T[i + 1]) for i in range(0, len(T), 3)]   # Unity winding -> Blender front faces
        me = bpy.data.meshes.new(p["name"]); me.from_pydata(verts, [], faces); me.update()
        me.materials.append(mats[c])
        ob = bpy.data.objects.new(p["name"], me); bpy.context.collection.objects.link(ob)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"; sh.color_type = "MATERIAL"; sh.show_backface_culling = True; sh.show_cavity = False
    sc.render.resolution_x, sc.render.resolution_y = 1400, 1000
    sc.view_settings.view_transform = "Standard"
    for name, (loc, look, ortho, size) in VIEW.items():
        if VIEWS and name not in VIEWS: continue
        cam = cs.camera(name, loc, look, lens=size if not ortho else 50)
        if ortho:
            cam.data.type = "ORTHO"; cam.data.ortho_scale = size * 2
        cam.data.clip_start = 0.05; cam.data.clip_end = 200
        sc.camera = cam
        sc.render.filepath = os.path.join(OUT, f"{pose}_{name}.png")
        bpy.ops.render.render(write_still=True)
    print("[gear] rendered", pose)
