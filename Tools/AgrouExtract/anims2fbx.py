# Animations Agrou (USD : squelette Synty Modular + animation) -> FBX par animation, pour l'import humanoide Unity.
# blender -b -P anims2fbx.py -- <dossier usda> <dossier fbx>
import bpy, sys, os, glob
src, dst = sys.argv[sys.argv.index("--") + 1:]
os.makedirs(dst, exist_ok=True)
for f in glob.glob(os.path.join(src, "**", "*.usda"), recursive=True):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    import re
    m = re.search(r"timeCodesPerSecond = ([0-9.]+)", open(f, encoding="utf-8").read(2000))
    if m: bpy.context.scene.render.fps = max(1, round(float(m.group(1))))   # cadence Unreal (souvent 30 ou 60)
    bpy.ops.wm.usd_import(filepath=f, import_skeletons=True, import_blendshapes=False)
    arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
    if not arm: print("SANS SQUELETTE", f); continue
    act = arm.animation_data.action if arm.animation_data else None
    name = os.path.splitext(os.path.basename(f))[0]
    if act:
        s, e = act.frame_range
        bpy.context.scene.frame_start, bpy.context.scene.frame_end = int(s), max(int(e), int(s) + 1)
        act.name = name
    print("ANIM", name, len(arm.data.bones), act.frame_range[:] if act else None, bpy.context.scene.render.fps)
    bpy.ops.export_scene.fbx(filepath=os.path.join(dst, name + ".fbx"), object_types={'ARMATURE'}, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False, apply_scale_options='FBX_SCALE_ALL')
