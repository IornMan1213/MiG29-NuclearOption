import bpy, math
sc=bpy.context.scene
for o in list(bpy.data.objects):
    if o.type in ('CAMERA','LIGHT'): bpy.data.objects.remove(o)
    elif o.name not in ("MiG-29-airframe","MiG-29-rails"): o.hide_render=True
cam=bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera=cam
cam.data.type='ORTHO'
sun=bpy.data.objects.new("sun", bpy.data.lights.new("sun",'SUN')); sc.collection.objects.link(sun); sun.rotation_euler=(0.9,0.2,0.4); sun.data.energy=4
sc.render.engine='BLENDER_WORKBENCH'; sc.render.resolution_x=1200; sc.render.resolution_y=700
# front view of left wing (blender +Y = left), looking aft (-X)
cam.data.ortho_scale=45; cam.location=(150,30,-5); cam.rotation_euler=(math.pi/2,0,math.pi/2)
sc.render.filepath="//../../pylon_front.png"; bpy.ops.render.render(write_still=True)
# side view from below-left of the left wing pylons
cam.data.ortho_scale=40; cam.location=(5,120,-10); cam.rotation_euler=(math.pi/2,0,math.pi); 
sc.render.filepath="//../../pylon_side.png"; bpy.ops.render.render(write_still=True)
# bottom view
cam.data.ortho_scale=60; cam.location=(5,25,-150); cam.rotation_euler=(math.pi,0,math.pi/2)
sc.render.filepath="//../../pylon_below.png"; bpy.ops.render.render(write_still=True)
