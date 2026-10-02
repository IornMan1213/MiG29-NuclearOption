"""Render the MiG's own cockpit part, one colour per loose island, from the pilot's eye and from the side."""
import bpy, sys, os, random
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import cockpit_scene as cs
src, out = sys.argv[sys.argv.index("--") + 1:][:2]
cs.clear()
scene = bpy.context.scene
d, obs = cs.load_parts(src, {"cockpit", "body"})
ck = obs["cockpit"]
bpy.context.view_layer.objects.active = ck; ck.select_set(True)
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.separate(type="LOOSE"); bpy.ops.object.mode_set(mode="OBJECT")
random.seed(3)
for o in scene.objects:
    o.color = (random.random(), random.random(), random.random(), 1) if o.name.startswith("mig_cockpit") else (0.6, 0.6, 0.6, 1)
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.color_type = "OBJECT"; scene.display.shading.light = "STUDIO"
scene.render.resolution_x, scene.render.resolution_y = 1400, 900
ex, ey, ez = cs.u2b(*cs.EYE_MIG)
views = {"eye_fwd": ((ex, ey, ez), (ex, ey + 1, ez - 0.25)), "eye_down": ((ex, ey, ez), (ex, ey + 0.5, ez - 0.7)),
         "side": ((2.2, 6.7, 1.0), (0, 6.7, 0.8)), "top": ((0, 6.7, 3.0), (0, 6.71, 0.0)), "xside": ((6, 6.7, 0.8), (0, 6.7, 0.8)),
         "eye_left": ((ex, ey, ez), (ex - 1, ey + 0.1, ez - 0.4)), "eye_back": ((ex, ey, ez), (ex, ey - 1, ez - 0.2))}
body = obs["body"]
pil = cs.load_obj_unity(r"C:/Users/jayea/Downloads/Blueprinter-Editor/Blueprinter-Editor/MiG29Out/pilot_mig.obj", "pilot"); pil.color = (1, 0.5, 0.1, 1)
for zz in (5.5, 6.0, 6.5, 7.0, 7.5, 8.0):
    bpy.ops.mesh.primitive_cube_add(size=0.03, location=cs.u2b(0.45, 0.2, zz)); bpy.context.object.color = (1, 0, 0, 1)
for yy in (0.4, 0.6, 0.8, 1.0, 1.2):
    bpy.ops.mesh.primitive_cube_add(size=0.03, location=cs.u2b(0.45, yy, 5.4)); bpy.context.object.color = (0, 0, 1, 1)
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.04, location=cs.u2b(*cs.EYE_MIG)); bpy.context.object.color = (1, 1, 0, 1)
for name, (loc, look) in views.items():
    body.hide_render = name in ("top", "xside")
    cam = cs.camera(name, loc, look, lens=12 if name.startswith("eye") else 30)
    if name == "xside": cam.data.type = "ORTHO"; cam.data.ortho_scale = 3.0
    scene.camera = cam
    scene.render.filepath = os.path.join(out, f"look_{name}.png")
    bpy.ops.render.render(write_still=True)
