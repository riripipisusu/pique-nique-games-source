# Map Agrou (USD exporte par AgrouExtract) -> FBX pour Unity + JSON (lumieres, feu de camp).
# blender -b --factory-startup -P map2fbx.py -- <map.usda> <sortie.fbx> <sortie.json>
import bpy, sys, json, re, collections
src, dst, meta = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.usd_import(filepath=src, import_cameras=False)
D = bpy.data

def kill(o):
    for c in list(o.children_recursive): D.objects.remove(c, do_unlink=True)
    D.objects.remove(o, do_unlink=True)

# Ciel (sphere geante), personnages poses (squelettes) : pas dans la map Unity.
for o in [o for o in D.objects if o.type == 'ARMATURE' or re.search(r'GoodSky|Skybox|SkySphere', o.name, re.I)]:
    if o.name in D.objects: kill(o)

# Feu de camp d'Agrou (centre du cercle des joueurs), pose sur le sol ; sinon l'origine de la map.
from mathutils import Vector
# Plusieurs feux (Foret) : celui le plus proche du point d'apparition des joueurs.
start = next((o.matrix_world.translation.copy() for o in D.objects if o.name.startswith("PlayerStart") or (o.parent and o.parent.name.startswith("PlayerStart"))), Vector((0, 0, 0)))
fires = [o for o in D.objects if "FeuDeCamps" in o.name and (not o.parent or "FeuDeCamps" not in o.parent.name)]
fire = min(fires, key=lambda o: (o.matrix_world.translation - start).length) if fires else None
p = fire.matrix_world.translation.copy() if fire else Vector((0, 0, 0))
for o in [o for o in D.objects if "FeuDeCamps" in o.name]:   # leur feu (et son tronc) : on a le notre
    for c in [c for c in o.children_recursive if c.type == 'MESH']: D.objects.remove(c, do_unlink=True)
bpy.context.view_layer.update()
hit, loc, *_ = bpy.context.scene.ray_cast(bpy.context.evaluated_depsgraph_get(), p + Vector((0, 0, 2)), Vector((0, 0, -1)))
if hit: p = loc
# Cercle des joueurs degage (rayon en metres de la map, avant l'agrandissement x1,35 dans le jeu).
CLEAR = 6.5   # cercle + place de la potence (a cote)
def near(v): return (Vector((v.x, v.y)) - Vector((p.x, p.y))).length < CLEAR
for o in [o for o in D.objects if o.type == 'MESH' and max(o.dimensions) < 40 and "Landscape" not in o.name and (not o.parent or "Landscape" not in o.parent.name)]:
    c = sum((o.matrix_world @ Vector(b) for b in o.bound_box), Vector()) / 8
    d = o.dimensions * o.matrix_world.to_scale().length / 1.732
    if near(c) and c.z < p.z + 6 and max(d.x, d.y) < 6 and c.z > p.z + 0.15 and o.name in D.objects: D.objects.remove(o, do_unlink=True)   # pas le sol (iles, rochers plats)
# Vegetation (PointInstancer) : instances rendues reelles, regroupees en un maillage par espece.
dg = bpy.context.evaluated_depsgraph_get()
groups = collections.defaultdict(list)
for inst in dg.object_instances:
    if inst.is_instance and inst.parent and inst.parent.original.type == 'POINTCLOUD' and inst.object.type == 'MESH':
        if not near(inst.matrix_world.translation): groups[inst.object.original.name].append(inst.matrix_world.copy())
made = 0
import random
random.seed(1)
for name, mats in groups.items():
    if len(mats) > 6000: mats = random.sample(mats, 6000)   # tapis d'herbe (centaines de milliers) : un echantillon suffit
    srcobj = D.objects[name]
    me = srcobj.data
    import bmesh
    bm = bmesh.new()
    for m in mats:
        tmp = me.copy(); tmp.transform(m); bm.from_mesh(tmp); D.meshes.remove(tmp)
    out = D.meshes.new("Foliage_" + name); bm.to_mesh(out); bm.free()
    for mat in [sl.material for sl in srcobj.material_slots] or list(me.materials): out.materials.append(mat)   # materiau lie a l'objet (USD)
    ob = D.objects.new("Foliage_" + name, out); bpy.context.scene.collection.objects.link(ob); made += len(mats)
for o in [o for o in D.objects if o.type == 'POINTCLOUD'] + [D.objects[n] for n in groups if n in D.objects]:
    if o.name in D.objects: kill(o)   # nuages de points et maillages sources (restes a l'echelle Unreal, x100)
# Ciel (sphere de 30 km) et sols de secours geants (cube moteur) : hors map.
for o in [o for o in D.objects if o.type == 'MESH' and (max(o.dimensions) > 1500 or max(o.dimensions) > 100 and any(s.material and s.material.name.startswith('BasicShapeMaterial') for s in o.material_slots))]:
    if o.name in D.objects: kill(o)
print("FOLIAGE", len(groups), made)

# Un materiau par nom de base (l'import USD en cree un par maillage : X, X.001...).
base = {}
for m in list(D.materials):
    b = re.sub(r'\.\d{3}$', '', m.name)
    if b not in base: base[b] = m; m.name = b
for o in D.objects:
    if o.type == 'MESH':
        for s in o.material_slots:
            if s.material: s.material = base[re.sub(r'\.\d{3}$', '', s.material.name)]

# Lumieres et feu de camp -> empties nommes (meme conversion d'axes que les maillages), parametres dans le JSON.
info = {"lights": [], "fire": None}
for i, o in enumerate([o for o in D.objects if o.type == 'LIGHT']):
    l = o.data
    info["lights"].append({"type": l.type, "color": list(l.color), "energy": l.energy,
                           "radius": getattr(l, "cutoff_distance", 0) or 0, "spot": getattr(l, "spot_size", 0)})
    e = D.objects.new("LIGHT_%d" % i, None); bpy.context.scene.collection.objects.link(e); e.matrix_world = o.matrix_world.copy()
    D.objects.remove(o, do_unlink=True)
e = D.objects.new("FIRE_CENTER", None); bpy.context.scene.collection.objects.link(e); e.location = p
info["fire"] = bool(fire); info["ground"] = bool(hit)
print("TRIS", sum(len(o.data.polygons) for o in D.objects if o.type == 'MESH'), "OBJS", len(D.objects), "MATS", len(base))
json.dump(info, open(meta, "w"))
bpy.ops.export_scene.fbx(filepath=dst, use_selection=False, object_types={'MESH', 'EMPTY'}, apply_scale_options='FBX_SCALE_ALL',
                         mesh_smooth_type='FACE', path_mode='STRIP', embed_textures=False, bake_anim=False)
