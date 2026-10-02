# Potence (USD de potence_usda.py) -> FBX : blender -b -P potence2fbx.py -- <in.usda> <out.fbx> [rendu.png]
import bpy, sys, re, math
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.usd_import(filepath=a[0])
base = {}
for m in list(bpy.data.materials):
    b = re.sub(r'\.\d{3}$', '', m.name)
    if b not in base: base[b] = m; m.name = b
for o in bpy.data.objects:
    if o.type == 'MESH':
        for s in o.material_slots:
            if s.material: s.material = base[re.sub(r'\.\d{3}$', '', s.material.name)]
for o in bpy.data.objects: print("OBJ", o.name, o.type, [round(v, 2) for v in o.matrix_world.translation], [round(v, 2) for v in o.dimensions])
bpy.ops.export_scene.fbx(filepath=a[1], object_types={'MESH', 'EMPTY'}, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE', path_mode='STRIP', bake_anim=False)
if len(a) > 2:   # rendu de controle, vue de la camera du Blueprint
    cam = bpy.data.objects.new("c", bpy.data.cameras.new("c")); bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector((6, -4, 3)); cam.rotation_euler = (Vector((0, 0, 1.5)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam
    sun = bpy.data.objects.new("s", bpy.data.lights.new("s", 'SUN')); bpy.context.scene.collection.objects.link(sun); sun.rotation_euler = (0.8, 0.2, 0.5)
    for n in ("SPHERE_1", "SPHERE_2", "CABLE_1", "CABLE_2", "CAMERA"):
        e = bpy.data.objects.get(n)
        if e:
            bpy.ops.mesh.primitive_uv_sphere_add(radius=0.12, location=e.matrix_world.translation)
    sc = bpy.context.scene; sc.render.resolution_x, sc.render.resolution_y = 640, 480; sc.render.filepath = a[2]; bpy.ops.render.render(write_still=True)
