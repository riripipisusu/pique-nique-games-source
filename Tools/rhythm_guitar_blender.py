# Guitare des musiciens du concert (guitare de Kris, modele glTF telecharge par l'utilisatrice) -> Resources/Rhythm/Guitar.fbx.
# Remise d'aplomb : face vers le public (-y Blender), longueur 1 m, origine au milieu du corps de la guitare
# (la ou le musicien la tient contre lui). Couleurs gardees dans les noms de materiaux (Unity les refait).
#   blender -b -P Tools/rhythm_guitar_blender.py -- <KrisGuitar.gltf>
import bpy, sys, os, math, mathutils
SRC = sys.argv[sys.argv.index("--") + 1]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Rhythm", "Guitar.fbx")
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
g = bpy.data.objects["Guitar"]
for o in [o for o in bpy.data.objects if o != g]: bpy.data.objects.remove(o)
g.parent = None
bpy.context.view_layer.objects.active = g; g.select_set(True)
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)   # echelle (0.09, 1, 1) portee par l'objet
g.rotation_mode = "XYZ"                                           # le glTF arrive en quaternion
g.rotation_euler = (0, 0, math.radians(-90))                      # la face regardait +x : on la tourne vers -y
bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
bb = [g.matrix_world @ mathutils.Vector(c) for c in g.bound_box]
lo = min(v.z for v in bb); hi = max(v.z for v in bb)
k = 1.0 / (hi - lo)
g.scale = (k, k, k)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
# origine : centre du corps (quart bas de la guitare), le manche monte
bb = [g.matrix_world @ mathutils.Vector(c) for c in g.bound_box]
cx = sum(v.x for v in bb) / 8; cy = sum(v.y for v in bb) / 8; lo = min(v.z for v in bb)
bpy.context.scene.cursor.location = (cx, cy, lo + 0.22)
bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
g.location = (0, 0, 0)
for m in g.data.materials:
    c = m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value
    m.name = "Guitar_%02x%02x%02x" % tuple(int(round((x if x > 0.0031308 else 0) ** (1 / 2.2) * 255)) for x in c[:3])
print("DIMS", tuple(round(x, 3) for x in g.dimensions), [m.name for m in g.data.materials])
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE")
# apercu de face
cam = bpy.data.objects.new("c", bpy.data.cameras.new("c")); bpy.context.scene.collection.objects.link(cam)
cam.location = (0, -3, 0.28); cam.rotation_euler = (math.radians(90), 0, 0); bpy.context.scene.camera = cam
w = bpy.data.worlds.new("w"); bpy.context.scene.world = w
sc = bpy.context.scene; sc.render.engine = "BLENDER_WORKBENCH"; sc.render.resolution_x = sc.render.resolution_y = 400
sc.render.filepath = os.path.join(os.path.dirname(SRC), "front.png"); bpy.ops.render.render(write_still=True)
print("OK", OUT)
