"""Two-ship formation render (loading screen / promo): showcase.py's armed jet plus a wingman copy, 2048x1024.

blender -b MiG-29.blend --python formation.py -- <MiG29Source dir> <out.png> [livery png]
"""
import bpy, math, os, sys

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_png = argv[0], argv[1]
livery = argv[2] if len(argv) > 2 else "mig29_basecolor_display.png"
sys.argv = sys.argv[:sys.argv.index("--") + 1] + [src, os.path.dirname(out_png), "__none__"]
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "showcase.py"), encoding="utf-8").read())

set_livery(os.path.join(src, livery))
# wingman: linked copies of every visible mesh under the pivot, on a second pivot echeloned back and right
lead_rot = Euler((math.radians(-24), math.radians(-12), math.radians(6)))
pivot.rotation_euler = lead_rot
wing = bpy.data.objects.new("wing_pivot", None); sc.collection.objects.link(wing)
wing.location = pivot.location + Vector((-110, -150, -40))  # blender units (~dm): 11 m back, 15 m right, 4 m low
wing.rotation_euler = lead_rot
for o in [o for o in bpy.data.objects if o.parent == pivot and o.type == "MESH" and not o.hide_render]:
    c = o.copy(); sc.collection.objects.link(c)
    c.parent = wing; c.matrix_parent_inverse = o.matrix_parent_inverse.copy()
bpy.context.view_layer.update()

sc.render.resolution_x, sc.render.resolution_y = 2048, 1024
mid = (pivot.location + wing.location) / 2
cam.data.lens = 40
cam.location = mid + Vector((190, 480, -85))
look(mid)
sun.rotation_euler = Euler((math.radians(35), 0, math.radians(145)))
sc.render.filepath = out_png
bpy.ops.render.render(write_still=True)
print("RENDERED", out_png)
