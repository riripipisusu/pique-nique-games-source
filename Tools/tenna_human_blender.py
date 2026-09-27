# Tenna : rattache les os "a plat" de l'export Rigify (cuisses, epaules, bras...) au bassin / torse,
# en gardant exactement la meme animation (pose re-cuite image par image), puis reexporte le FBX.
import bpy
SRC = r"C:/Users/ANASTA~1/AppData/Local/Temp/claude/C--Users-Anastasia-Documents-Projets-Claude-croquecarotte/d4207216-2dfa-4f16-a05b-c3042a2c5e01/scratchpad/tenna/source/sk/TennaRig/Tenna_Sketchfab.fbx"
OUT = r"C:/Users/Anastasia/Documents/Projets Claude/CroqueCarotte3D/Assets/Stage/Tenna/Tenna.fbx"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
for n in ("Floor", "Lights", "Sign"):
    if n in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[n])
arm = bpy.data.objects["rig_deform"]
act = arm.animation_data.action
sc = bpy.context.scene
f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])

PARENT = {}
for s in ("L", "R"):
    PARENT[f"DEF-thigh.{s}"] = "DEF-spine"
    PARENT[f"DEF-pelvis.{s}"] = "DEF-spine"
    PARENT[f"DEF-shoulder.{s}"] = "DEF-spine.003"
    PARENT[f"DEF-upper_arm.{s}"] = f"DEF-shoulder.{s}"
    PARENT[f"DEF-breast.{s}"] = "DEF-spine.003"
    PARENT[f"Antenna01_{s}"] = "DEF-spine.006"
    for i in range(3): PARENT[f"DEF-Cape_{i}_{s}"] = "DEF-spine.001"
for i in range(1, 4): PARENT[f"DEF-Tie_0{i}"] = "DEF-spine.003"

# 1. Pose de chaque os (espace armature) a chaque image.
poses = {}
for f in range(f0, f1 + 1):
    sc.frame_set(f)
    poses[f] = {pb.name: pb.matrix.copy() for pb in arm.pose.bones}

# 2. Nouveaux parents (sans bouger la pose de repos).
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
for b, p in PARENT.items():
    if b in eb and p in eb:
        eb[b].use_connect = False
        eb[b].parent = eb[p]
bpy.ops.object.mode_set(mode="POSE")

# 3. Nouvelle action : on reapplique les poses enregistrees, parents d'abord.
arm.animation_data.action = None
new = bpy.data.actions.new("Idle")
arm.animation_data.action = new
order = sorted(arm.pose.bones, key=lambda pb: len(pb.parent_recursive))
for f in range(f0, f1 + 1):
    sc.frame_set(f)
    for pb in order:
        pb.matrix = poses[f][pb.name]
        bpy.context.view_layer.update()
    for pb in order:
        pb.keyframe_insert("location", frame=f)
        pb.keyframe_insert("rotation_quaternion" if pb.rotation_mode == "QUATERNION" else "rotation_euler", frame=f)
        pb.keyframe_insert("scale", frame=f)
bpy.ops.object.mode_set(mode="OBJECT")
for a in list(bpy.data.actions):
    if a != new: bpy.data.actions.remove(a)
sc.frame_start, sc.frame_end = f0, f1
bpy.ops.export_scene.fbx(filepath=OUT, object_types={"MESH", "ARMATURE"}, bake_anim=True, bake_anim_use_all_actions=True,
    bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True, add_leaf_bones=False, path_mode="STRIP")
print("EXPORT OK", f0, f1)
