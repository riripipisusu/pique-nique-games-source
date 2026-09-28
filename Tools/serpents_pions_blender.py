# Pions de Serpents et echelles : pions "simple" du pack "Board Game Items" fourni par l'utilisatrice (pas dans le depot).
# Un OBJ par couleur, recentre (pied au sol, axe vertical au centre), y vers le haut -> Assets/Resources/Serpents/Pions (ignore par git).
#   blender -b -P Tools/serpents_pions_blender.py
import bpy, os, mathutils
A = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Board Game Items")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Serpents", "Pions")
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(A, "Models", "pawns.fbx"))
for o in [o for o in bpy.data.objects if o.type == "MESH" and "simple" in o.name]:
    color = o.name.split("_")[-1]
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    pts = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
    cx = sum(p.x for p in pts) / 8; cy = sum(p.y for p in pts) / 8; lo = min(p.z for p in pts)
    for v in o.data.vertices: v.co -= mathutils.Vector((cx, cy, lo))
    bpy.ops.wm.obj_export(filepath=os.path.join(OUT, "pion_" + color + ".obj"), export_selected_objects=True, export_materials=False,
                          export_uv=True, export_normals=True, forward_axis="NEGATIVE_Z", up_axis="Y")
    print("OK", color, tuple(round(x, 4) for x in o.dimensions))
img = bpy.data.images.load(os.path.join(A, "Textures_2048", "pawns_D2048.tif"))
img.scale(1024, 1024)
img.filepath_raw = os.path.join(OUT, "pions.png"); img.file_format = "PNG"; img.save()
print("OK texture")
