"""Turntable frames of the armed MiG-29 for the project page (loads showcase.py's scene setup, then spins the jet).

blender -b MiG-29.blend --python turntable.py -- <MiG29Source dir> <out_dir> [livery png] [frames]
"""
import bpy, math, os, sys

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
livery = argv[2] if len(argv) > 2 else "mig29_basecolor_digital.png"
frames = int(argv[3]) if len(argv) > 3 else 96

# reuse showcase.py for missiles, materials, sky and camera, but skip its shots
sys.argv = sys.argv[:sys.argv.index("--") + 1] + [src, out, "__none__"]
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "showcase.py"), encoding="utf-8").read())

set_livery(os.path.join(src, livery))
# spin about the airframe's bounding-box centre (showcase.py's pivot is off-centre)
af = bpy.data.objects["MiG-29-airframe"]
pts = [af.matrix_world @ Vector(c) for c in af.bound_box]
center = sum(pts, Vector()) / 8
kids = [(o, o.matrix_world.copy()) for o in bpy.data.objects if o.parent == pivot]
pivot.rotation_euler = Euler((0, 0, 0)); pivot.location = center
bpy.context.view_layer.update()
for o, mw in kids:
    o.matrix_world = mw
sc.render.resolution_x, sc.render.resolution_y = 960, 540
cam.data.lens = 50
sun.rotation_euler = Euler((math.radians(40), 0, math.radians(130)))
for i in range(frames):
    yaw = 2 * math.pi * i / frames
    pivot.rotation_euler = Euler((math.radians(-10), math.radians(-4), yaw))
    d = 265 + 50 * abs(math.sin(yaw))  # side-on views need more room than nose-on ones
    cam.location = Vector((d, 0, d * 0.32)) + pivot.location
    look(pivot.location)
    sc.render.filepath = os.path.join(out, f"tt_{i:03d}.png")
    bpy.ops.render.render(write_still=True)
print("TURNTABLE", frames, "frames ->", out)
