# Scene du jeu de rythme (TV Time), version 3D de la scene 2D "Lightners Live" du jeu Godot de l'utilisatrice :
# estrade ovale bleue a deux niveaux, marches devant, murs d'enceintes, grande enseigne en relief sous deux arches
# de treillis. Les materiaux sont de simples noms (Unity les remplace par ses couleurs, cf. RhythmView.Paint).
#   "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -P Tools/rhythm_stage_blender.py
import bpy, bmesh, math, os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
OUT = os.path.join(ROOT, "Assets", "Resources", "Rhythm", "LiveStage.fbx")
FONT = os.path.join(ROOT, "Assets", "Resources", "Fonts", "LilitaOne.ttf")

bpy.ops.wm.read_factory_settings(use_empty=True)
mats = {}
def mat(name):
    if name not in mats: mats[name] = bpy.data.materials.new(name)
    return mats[name]

def obj_from(bm, name, material):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(mat(material))
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    return o

def oval(name, rx, ry, z0, z1, material, top=None, seg=96, caps=True):
    """Cylindre ovale (devant = -y, vers le public). top : materiau distinct pour le dessus."""
    bm = bmesh.new()
    lo = [bm.verts.new((rx * math.cos(a), ry * math.sin(a), z0)) for a in (2 * math.pi * i / seg for i in range(seg))]
    hi = [bm.verts.new((v.co.x, v.co.y, z1)) for v in lo]
    for i in range(seg):
        j = (i + 1) % seg
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    if caps:   # un anneau (liseré) n'a pas de couvercle : il se battrait avec le dessus de l'estrade
        bm.faces.new(hi)
        bm.faces.new(list(reversed(lo)))
    o = obj_from(bm, name, material)
    if top:
        o.data.materials.append(mat(top))
        o.data.polygons[seg].material_index = 1
    return o

def box(name, x, y, z, sx, sy, sz, material, rot=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x, y, z), rotation=(0, 0, rot))
    o = bpy.context.object; o.name = name; o.scale = (sx, sy, sz)
    o.data.materials.append(mat(material))
    return o

def cyl(name, x, y, z, r, depth, material, rot=(0, 0, 0), verts=24):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=(x, y, z), rotation=rot)
    o = bpy.context.object; o.name = name
    o.data.materials.append(mat(material))
    return o

# --- Estrade : socle sombre evase, pont bleu, liseré lumineux, dessus clair --------------------------
oval("Socle", 7.6, 3.7, 0.0, 0.45, "StageDark")
oval("Pont", 7.2, 3.35, 0.45, 0.9, "StageSide", top="StageTop")
oval("Lisere", 7.24, 3.39, 0.86, 0.9, "Neon", caps=False)
# Trois ronds de lumiere sur le dessus (les "spots" de la scene d'origine), a peine en relief.
for x in (-3.8, 0, 3.8):
    oval(f"Rond{x}", 1.5, 0.75, 0.9, 0.915, "SpotPool", seg=48).location.y = -0.6

# --- Marches au centre, vers le public ----------------------------------------------------------------
for k in range(4):
    h = 0.9 - k * 0.225
    box(f"Marche{k}", 0, -3.35 - k * 0.42, h / 2, 2.6 + k * 0.35, 0.42, h, "StageTop" if k % 2 == 0 else "StageSide")
    box(f"Nez{k}", 0, -3.35 - k * 0.42 - 0.2, h - 0.02, 2.6 + k * 0.35, 0.04, 0.04, "Neon")

# --- Murs d'enceintes derriere, de part et d'autre du centre -----------------------------------------
def speaker(name, x, y, z, w, h, rot):
    b = box(name, x, y, z, w, 0.7, h, "Speaker", rot)
    c = cyl(name + "HP", 0, 0, 0, min(w, h) * 0.34, 0.08, "Cone", rot=(math.pi / 2, 0, 0))
    c.location = (x - 0.36 * math.sin(rot), y - 0.36 * math.cos(rot), z)
    c.rotation_euler = (math.pi / 2, 0, rot)
    return b
for s in (-1, 1):
    speaker(f"Enceinte{s}a", s * 2.3, 2.3, 1.55, 1.3, 1.3, -s * 0.25)
    speaker(f"Enceinte{s}b", s * 3.8, 1.9, 1.55, 1.4, 1.3, -s * 0.45)
    speaker(f"Enceinte{s}c", s * 2.35, 2.35, 2.85, 1.1, 1.1, -s * 0.25)
    speaker(f"Enceinte{s}d", s * 3.75, 1.95, 2.85, 1.2, 1.1, -s * 0.45)
box("Fond", 0, 2.75, 2.2, 3.2, 0.3, 2.6, "Speaker")          # panneau central derriere les musiciens
box("Grille", 0, 2.58, 2.2, 2.6, 0.05, 2.0, "Cone")

# --- Enseigne en relief : "PIQUE-NIQUE" sur "LIVE", face au public -------------------------------------
font = bpy.data.fonts.load(FONT)
def sign(text, z, size, name, bend):
    """Texte en relief, courbe en arche comme l'enseigne d'origine : face violette, liseré dore derriere."""
    for layer, material, offset, dy in (("Fond", "SignEdge", 0.045, 0.07), ("Face", "SignFill", 0.0, 0.0)):
        cu = bpy.data.curves.new(name + layer, "FONT")
        cu.body = text; cu.font = font; cu.size = size; cu.align_x = "CENTER"; cu.align_y = "CENTER"
        cu.space_character = 1.08
        cu.extrude = 0.12; cu.offset = offset; cu.bevel_depth = 0.015; cu.resolution_u = 3; cu.bevel_resolution = 1
        o = bpy.data.objects.new(name + layer, cu); bpy.context.collection.objects.link(o)
        cu.materials.append(mat(material))
        bpy.context.view_layer.objects.active = o; o.select_set(True)
        bpy.ops.object.convert(target="MESH")
        # Decoupe fine pour que la courbure soit reguliere, puis arche (courbure autour de l'axe du texte).
        bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.object.mode_set(mode="OBJECT")
        m = o.modifiers.new("arche", "SIMPLE_DEFORM"); m.deform_method = "BEND"; m.deform_axis = "Z"; m.angle = math.radians(bend)
        bpy.ops.object.modifier_apply(modifier="arche")
        o.select_set(False)
        o.rotation_euler = (math.pi / 2, 0, 0); o.location = (0, 3.0 + dy, z)
    return o
sign("PIQUE-NIQUE", 5.3, 1.3, "Titre", 28)
sign("LIVE", 4.05, 1.3, "Live", 12)

# --- LEDs facon enseigne de music-hall : une rangee d'ampoules qui epouse le haut de "PIQUE-NIQUE" et le bas de
# "LIVE" (contour releve sur les lettres), deux materiaux alternes pour le chenillard (RhythmView.Pulse).
bpy.context.view_layer.update()
def contour(obj, top, step=0.24, margin=0.28):
    pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    x0, x1 = min(p.x for p in pts), max(p.x for p in pts)
    out = []
    x = x0 + 0.1
    while x <= x1 - 0.1:
        near = [p.z for p in pts if abs(p.x - x) < 0.35]
        if near: out.append((x, (max(near) + margin) if top else (min(near) - margin)))
        x += step
    # lissage : les bosses des lettres ne doivent pas faire zigzaguer la rangee
    zs = [z for _, z in out]
    sm = [(max if top else min)(zs[max(0, i - 3):i + 4]) for i in range(len(zs))]
    return [(x, sum(sm[max(0, i - 2):i + 3]) / len(sm[max(0, i - 2):i + 3])) for i, (x, _) in enumerate(out)]
k = 0
for obj, top in ((bpy.data.objects["TitreFace"], True), (bpy.data.objects["LiveFace"], False)):
    for x, z in contour(obj, top):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=5, radius=0.085, location=(x, 2.93, z))
        o = bpy.context.object; o.name = f"Led{k}"
        o.data.materials.append(mat("LedA" if k % 2 == 0 else "LedB"))
        k += 1
print("LEDS", k)

# --- Grappes de projecteurs en losange de part et d'autre (comme la scene d'origine) --------------------
for s in (-1, 1):
    for k, (z, bulb) in enumerate(((2.3, "BulbA"), (3.6, "BulbB"), (4.9, "BulbC"))):
        x = s * 6.0
        for dx, dz in ((0, 0.42), (0, -0.42), (0.42, 0), (-0.42, 0)):
            b = box(f"Lampe{s}{k}{dx}{dz}", x + dx, 2.2, z + dz, 0.5, 0.35, 0.5, "Lamp")
            b.rotation_euler = (0, math.radians(45), 0)
        cyl(f"Ampoule{s}{k}", x, 1.98, z, 0.22, 0.1, bulb, rot=(math.pi / 2, 0, 0))

# --- Deux arches de treillis ("XXXX" de la scene d'origine) au-dessus de l'enseigne -------------------
def truss_arc(name, r, cy, cz, a0, a1, n):
    pts = [(r * math.cos(a), cy, cz + r * math.sin(a)) for a in (a0 + (a1 - a0) * i / n for i in range(n + 1))]
    for i in range(n):
        (x0, y0, z0), (x1, y1, z1) = pts[i], pts[i + 1]
        dx, dz = x1 - x0, z1 - z0
        L = math.hypot(dx, dz); ang = math.atan2(dz, dx)
        for off in (-0.22, 0.22):   # deux longerons
            b = box(f"{name}L{i}{off}", (x0 + x1) / 2 - off * math.sin(ang), y0, (z0 + z1) / 2 + off * math.cos(ang), L, 0.08, 0.08, "Truss")
            b.rotation_euler = (0, -ang, 0)
        for d in (1, -1):           # croisillon en X
            b = box(f"{name}X{i}{d}", (x0 + x1) / 2, y0, (z0 + z1) / 2, math.hypot(L, 0.44), 0.05, 0.05, "Truss")
            b.rotation_euler = (0, -ang - d * math.atan2(0.44, L), 0)
truss_arc("Arche", 5.2, 3.2, 1.8, math.radians(20), math.radians(160), 18)
truss_arc("Arche2", 6.6, 3.5, 1.2, math.radians(12), math.radians(168), 22)
# Etoiles scintillantes de part et d'autre (les "sparkles" dorees).
for s in (-1, 1):
    for (x, z, r) in ((5.9, 5.6, 0.35), (4.4, 7.2, 0.22)):
        bm = bmesh.new()
        pts = [bm.verts.new((s * x + (r if k % 2 == 0 else r * 0.3) * math.cos(k * math.pi / 4), 3.1, z + (r if k % 2 == 0 else r * 0.3) * math.sin(k * math.pi / 4))) for k in range(8)]
        bm.faces.new(pts)
        o = obj_from(bm, f"Etoile{s}{x}", "Star")
        o.modifiers.new("ep", "SOLIDIFY").thickness = 0.06

# --- Export : un seul objet (moins d'appels de rendu), axes Unity -------------------------------------
bpy.ops.object.select_all(action="SELECT")
bpy.context.view_layer.objects.active = bpy.data.objects["Pont"]
bpy.ops.object.convert(target="MESH")
bpy.ops.object.join()
o = bpy.context.object; o.name = "LiveStage"
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE")
print("OK", OUT, len(o.data.polygons), "faces", [m.name for m in o.data.materials])
