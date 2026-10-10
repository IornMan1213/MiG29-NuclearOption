"""Builds blender/dash/MiG29_dash_sandbox.blend: the current MiG-29 cockpit (cockpit_build.py) inside the airframe, for trying
out a new dash by hand. Nothing here feeds the build; the sandbox works on copies of the source files in blender/dash/src.
Objects: ck_tub (tub, panel, consoles, glareshield), ck_ins_* / ck_lamp_* (instrument needles and lamps), ck_screen, ck_glass,
ck_seat, ck_stick, ck_throttle, frames (windscreen bow, sills), mig_body / mig_canopy (airframe). Camera 'pilot_eye' sits at the
pilot's eye (Numpad 0). Put new work in the 'dash_new' collection.
Blender axes: X = MiG x (right), Y = MiG z (forward), Z = MiG y (up); metres.
blender -b --python blender/dash_sandbox.py -- <MiG29Source>"""
import bpy, math, os, runpy, shutil, sys

argv = sys.argv[sys.argv.index("--") + 1:]
SRC = argv[0]
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "dash")
WORK = os.path.join(OUT, "src")
os.makedirs(WORK, exist_ok=True)
for f in ("mig29_mesh.json", "cockpit_atlas.json", "cockpit_atlas.png", "mig29_basecolor.png"):
    shutil.copy(os.path.join(SRC, f), WORK)

# cockpit_build with a preview dir but no views to render: it builds and links every object with preview materials
os.environ["MIG29_VIEWS"] = "none"
sys.argv = [sys.argv[0], "--", WORK, os.path.join(WORK, "renders")]
g = runpy.run_path(os.path.join(HERE, "cockpit_build.py"))

scene = bpy.context.scene
for ob in list(scene.collection.objects):
    if ob.name.startswith("mig_cockpit"):   # the stock KR-67 interior the build hides
        bpy.data.objects.remove(ob)
    elif ob.type == "CAMERA" and ob.name != "pilot_eye":
        bpy.data.objects.remove(ob)


def coll(name, prefixes):
    c = bpy.data.collections.new(name); scene.collection.children.link(c)
    for ob in list(scene.collection.objects):
        if any(ob.name.startswith(p) for p in prefixes):
            scene.collection.objects.unlink(ob); c.objects.link(ob)
    return c


coll("airframe", ("mig_", "frames"))
coll("cockpit_current", ("ck_",))
coll("lighting", ("sun",))
scene.collection.children.link(bpy.data.collections.new("dash_new"))

E = g["P"](*g["EYE"])
cam = g["cs"].camera("pilot_eye", E, E + g["P"](0, -0.45, 1), lens=16)
scene.camera = cam
bpy.data.objects["mig_body"].hide_set(False)

# open on the pilot's view in Material Preview
bpy.context.preferences.view.show_splash = False
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == "VIEW_3D":
            sp = area.spaces[0]
            sp.shading.type = "MATERIAL"
            sp.clip_start = 0.005
            sp.region_3d.view_perspective = "CAMERA"
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "MiG29_dash_sandbox.blend"), relative_remap=True)
print("[dash] wrote", os.path.join(OUT, "MiG29_dash_sandbox.blend"))
