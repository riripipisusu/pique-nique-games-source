# Tenna en smoking (Tenna_Tux_Rig.blend du rig de ThatAverageJoe) -> Assets/Stage/Tenna/TennaTux.fbx, pour le jeu de rythme.
# Le rig Rigify pilote ses os "DEF-" par contraintes : on recopie ces os dans une armature simple, avec une vraie
# hierarchie (comme tenna_human_blender.py pour Tenna normal), pour que Unity en fasse un humanoide.
# Pas d'animation exportee : Unity lui donne l'Idle de Tenna normal et les danses Mixamo (retarget humanoide).
#   blender -b <...>/Tenna_Tux_Rig.blend -P Tools/tenna_tux_blender.py
import bpy, os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Stage", "Tenna", "TennaTux.fbx")
rig = bpy.data.objects["rig"]
sc = bpy.context.scene
sc.frame_set(1)

# Parents a donner quand celui du rig n'est pas un os de deformation (cf. tenna_human_blender.py).
FALLBACK = {"Cane": "DEF-hand.R", "Hat": "DEF-spine.006", "Antenna01_L": "DEF-spine.006", "Antenna01_R": "DEF-spine.006"}
for s in ("L", "R"):
    FALLBACK[f"DEF-pelvis.{s}"] = "DEF-spine"
    for i in range(3): FALLBACK[f"DEF-Cape_{i}_{s}"] = "DEF-spine.001"

deform = [b for b in rig.data.bones if b.use_deform]
names = {b.name for b in deform}
def new_parent(b):
    if b.parent is None: return FALLBACK.get(b.name)   # canne : libre dans le rig, tenue en main droite ici
    p = b.parent
    while p is not None:
        if p.name in names: return p.name
        cand = "DEF-" + p.name[4:] if p.name.startswith("ORG-") else None
        if cand in names and cand != b.name: return cand
        if b.name in FALLBACK: return FALLBACK[b.name]
        p = p.parent
    return None

# 1. Armature neuve avec les os de deformation, a leur position de repos.
arm_data = bpy.data.armatures.new("TennaTux")
arm = bpy.data.objects.new("rig_deform", arm_data)
sc.collection.objects.link(arm)
arm.matrix_world = rig.matrix_world.copy()
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
for b in deform:
    eb = arm_data.edit_bones.new(b.name)
    eb.head, eb.tail = b.head_local, b.tail_local
    eb.matrix = b.matrix_local.copy()
    eb.length = (b.tail_local - b.head_local).length
for b in deform:
    p = new_parent(b)
    if p: arm_data.edit_bones[b.name].parent = arm_data.edit_bones[p]
bpy.ops.object.mode_set(mode="OBJECT")
print("OS", len(deform), "racines", [b.name for b in arm_data.bones if not b.parent])

# 2. Le corps et la canne suivent la nouvelle armature (memes groupes de sommets "DEF-...").
keep = []
for name in ("Body", "Cane"):
    o = bpy.data.objects[name]
    mw = o.matrix_world.copy()
    o.parent = arm
    o.matrix_world = mw
    for m in o.modifiers:
        if m.type == "ARMATURE": m.object = arm
    keep.append(o)

# 3. Materiaux simples nommes (Unity les refait : TennaTux_<nom>), sans les noeuds toon.
for o in keep:
    for i, m in enumerate(o.data.materials):
        if m: m.name = "TennaTux_" + m.name.split(".")[0]

bpy.ops.object.select_all(action="DESELECT")
for o in keep + [arm]: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"MESH", "ARMATURE"}, bake_anim=False,
                         add_leaf_bones=False, path_mode="STRIP", use_armature_deform_only=True)
print("EXPORT OK", OUT)
