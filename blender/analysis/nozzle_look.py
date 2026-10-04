"""Close-up renders of the MiG model's engine nozzles (workbench), to plan the from-scratch nozzles.
blender -b --python nozzle_look.py -- <MiG29Source> <out dir>"""
import bpy, os, sys, math
from mathutils import Vector
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import cockpit_scene as cs

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], argv[1]
os.makedirs(OUT, exist_ok=True)
cs.clear()
data, obs = cs.load_parts(os.path.join(SRC, "mig29_mesh.json"), {"body"})
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "SINGLE"
scene.display.shading.show_cavity = True
scene.render.resolution_x, scene.render.resolution_y = 1400, 1000
P = lambda x, y, z: Vector((x, z, y))
C = P(0.866, -0.292, -3.4)
for name, loc, lens in (("rear_q", P(2.6, 0.6, -6.4), 40), ("side", P(3.2, -0.25, -3.3), 45), ("aft", P(0.9, -0.25, -6.5), 50),
                        ("cut", P(0.866, -0.292, -5.2), 35)):
    cam = cs.camera(name, loc, C, lens=lens)
    scene.camera = cam
    scene.render.filepath = os.path.join(OUT, f"nozzle_{name}.png")
    bpy.ops.render.render(write_still=True)
