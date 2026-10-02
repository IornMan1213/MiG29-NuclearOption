"""Beauty renders of the MiG-29 (gear up, transparent background) for loading screens.
blender -b MiG-29.blend --python beauty.py -- <out_dir>"""
import bpy, math, sys, os
from mathutils import Vector, Euler

out = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "."
sc = bpy.context.scene
for o in list(bpy.data.objects):
    if o.type in ("CAMERA", "LIGHT"):
        bpy.data.objects.remove(o)
for name in ("MiG-29-landingOn", "MiG-29-landingOnLight", "MiG-29-hud", "MiG-29-instrGlass"):
    if name in bpy.data.objects:
        bpy.data.objects[name].hide_render = True

# parent everything to a pivot so we can bank/pitch the jet
pivot = bpy.data.objects.new("pivot", None); sc.collection.objects.link(pivot)
pivot.location = (34, 0, 9)
for o in bpy.data.objects:
    if o.type == "MESH":
        mw = o.matrix_world.copy(); o.parent = pivot; o.matrix_world = mw

sc.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE_NEXT"
sc.render.film_transparent = True
sc.render.resolution_x, sc.render.resolution_y = 2048, 1024
sc.view_settings.view_transform = "Filmic" if "Filmic" in [i.identifier for i in sc.view_settings.bl_rna.properties["view_transform"].enum_items] else "AgX"
world = sc.world or bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
bg = world.node_tree.nodes.get("Background"); bg.inputs[0].default_value = (0.55, 0.62, 0.72, 1); bg.inputs[1].default_value = 0.9

sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
sun.data.energy = 4.5; sun.data.angle = math.radians(2)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.lens = 50


def look(cam, target):
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


shots = [
    # name, pivot rotation (roll about X = fuselage axis, pitch about Y, yaw about Z), camera offset, sun rotation
    ("bank", (math.radians(-50), math.radians(-4), 0), (230, 250, 150), (math.radians(40), 0, math.radians(120))),
    ("climb", (math.radians(20), math.radians(-25), 0), (-170, -300, -120), (math.radians(60), 0, math.radians(-60))),
]
for name, rot, camloc, sunrot in shots:
    pivot.rotation_euler = Euler(rot)
    cam.location = Vector(camloc) + pivot.location
    look(cam, pivot.location)
    sun.rotation_euler = Euler(sunrot)
    sc.render.filepath = os.path.join(out, f"beauty_{name}.png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED", sc.render.filepath)
