# Accessoires Agrou (glb) -> FBX (textures integrees) : blender -b -P props2fbx.py -- <dossier glb> <dossier fbx>
import bpy, sys, os, glob
src, dst = sys.argv[sys.argv.index("--") + 1:]
for f in glob.glob(os.path.join(src, "**", "*.glb"), recursive=True):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=f)
    for o in [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('Icosphere')]: bpy.data.objects.remove(o)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    imgs = [i.name for i in bpy.data.images]
    print("PROP", os.path.basename(f), [tuple(round(d, 3) for d in o.dimensions) for o in meshes], "imgs", imgs)
    bpy.ops.export_scene.fbx(filepath=os.path.join(dst, os.path.splitext(os.path.basename(f))[0] + ".fbx"), apply_scale_options='FBX_SCALE_ALL',
                             path_mode='COPY', embed_textures=True, object_types={'MESH', 'ARMATURE'}, add_leaf_bones=False, bake_anim=False)
