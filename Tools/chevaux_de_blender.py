# De des petits chevaux, tires du pack "Board Game Items" fourni par l'utilisatrice (pas dans le depot) :
# FBX -> OBJ (Unity inverse l'axe x des OBJ, regle connue et verifiee pour les faces du de) + texture en PNG,
# dans Assets/Resources/Chevaux/De (dossier ignore par git). Taille d origine : 2 cm.
#   blender -b -P Tools/chevaux_de_blender.py
import bpy, os
A = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Board Game Items")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Chevaux", "De")
for fbx, name in [("dice6_red.fbx", "de_rouge")]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(A, "Models", fbx))
    for o in bpy.data.objects:
        o.select_set(o.type == "MESH")
    bpy.context.view_layer.objects.active = [o for o in bpy.data.objects if o.type == "MESH"][0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.wm.obj_export(filepath=os.path.join(OUT, name + ".obj"), export_selected_objects=True, export_materials=False,
                          export_uv=True, export_normals=True, forward_axis="NEGATIVE_Z", up_axis="Y")   # OBJ : y vers le haut
    print("OK", name)
# texture : convertie en PNG par Tools/chevaux_de_faces.py
