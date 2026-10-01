# Plateau de la Roue de la fortune (decor), d'apres les photos de l'emission : sol noir incruste de neons, podium de
# la roue a trois etages cercles de lumiere, estrade et comptoir blanc des candidats avec rampe chromee, mur du fond
# a bandes lumineuses et rangees d'ampoules, cabine vitree, gradins multicolores avec public et escalier eclaire,
# ovale d'ampoules, mur video courbe, treillis et projecteurs au plafond. Les parties vivantes (tableau des enigmes,
# anneau, roue, panneaux des candidats, logo, lumieres) restent dans RoueSet.cs.
# Materiaux = simples noms, peints par Unity (RoueSet.Paint).
#   "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -P Tools/roue_plateau_blender.py
# Coordonnees ecrites comme dans Unity (x droite, y haut, z vers le fond) : P() les convertit pour Blender.
import bpy, bmesh, math, os, random

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
OUT = os.path.join(ROOT, "Assets", "Resources", "Roue", "Plateau.fbx")
os.makedirs(os.path.dirname(OUT), exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
rng = random.Random(4)
mats = {}
def mat(name):
    if name not in mats: mats[name] = bpy.data.materials.new(name)
    return mats[name]

def P(x, y, z): return (x, z, y)   # Unity -> Blender (le modele est tourne de 180 deg a l'import, cf. RoueSet)
def D(a, r): return (math.sin(math.radians(a)) * r, math.cos(math.radians(a)) * r)   # angle 0 = vers le fond (+z)

def obj_from(bm, name, material):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for m in (material if isinstance(material, (list, tuple)) else [material]): me.materials.append(mat(m))
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    return o

def sector(name, c, r0, r1, a0, a1, y0, y1, material, top=None, seg=None):
    """Anneau (ou part d'anneau) plein : de r0 a r1, entre les angles a0 et a1, de la hauteur y0 a y1."""
    seg = seg or max(8, int(abs(a1 - a0) / 2.5))
    full = abs(a1 - a0) >= 359.9
    bm = bmesh.new()
    rings = []
    for (r, y) in ((r0, y0), (r1, y0), (r1, y1), (r0, y1)):
        row = []
        for i in range(seg + (0 if full else 1)):
            a = a0 + (a1 - a0) * i / seg
            dx, dz = D(a, r)
            row.append(bm.verts.new(P(c[0] + dx, y, c[1] + dz)))
        rings.append(row)
    n = len(rings[0])
    faces = []
    for k in range(4):
        A, B = rings[k], rings[(k + 1) % 4]
        for i in range(n if full else n - 1):
            j = (i + 1) % n
            f = bm.faces.new((A[i], A[j], B[j], B[i])); faces.append((f, k))
    if not full:
        for idx in (0, n - 1):
            bm.faces.new([rings[k][idx] for k in range(4)])
    bm.normal_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if top:
        for f, k in faces:
            if k == 2: f.material_index = 1   # dessus (r1->r0 a y1)
        return obj_from(bm, name, [material, top])
    return obj_from(bm, name, material)

def disc(name, c, r, y0, y1, material, seg=96):
    bm = bmesh.new()
    lo = [bm.verts.new(P(c[0] + D(360 * i / seg, r)[0], y0, c[1] + D(360 * i / seg, r)[1])) for i in range(seg)]
    hi = [bm.verts.new(P(v.co.x, y1, v.co.y)) for v in lo]
    for i in range(seg):
        j = (i + 1) % seg; bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    bm.faces.new(hi); bm.faces.new(list(reversed(lo)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from(bm, name, material)

def box(name, x, y, z, sx, sy, sz, material, yaw=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=P(x, y, z), rotation=(0, 0, -math.radians(yaw)))
    o = bpy.context.object; o.name = name; o.scale = (sx, sz, sy)
    o.data.materials.append(mat(material)); return o

def cyl(name, x, y, z, r, h, material, verts=16, r2=None):
    if r2 is None: bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=h, location=P(x, y, z))
    else: bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=h, location=P(x, y, z))
    o = bpy.context.object; o.name = name; o.data.materials.append(mat(material)); return o

def sphere(name, x, y, z, r, material, seg=10):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=max(4, seg // 2), radius=r, location=P(x, y, z))
    o = bpy.context.object; o.name = name; o.data.materials.append(mat(material)); return o

def tube(name, pts, radius, material, closed=False):
    """Tube lisse le long d'une suite de points (coordonnees Unity)."""
    cu = bpy.data.curves.new(name, "CURVE"); cu.dimensions = "3D"
    sp = cu.splines.new("POLY"); sp.points.add(len(pts) - 1)
    for p, q in zip(sp.points, pts): p.co = (*P(*q), 1)
    sp.use_cyclic_u = closed
    cu.bevel_depth = radius; cu.bevel_resolution = 2; cu.use_fill_caps = True
    o = bpy.data.objects.new(name, cu); bpy.context.collection.objects.link(o)
    cu.materials.append(mat(material))
    bpy.context.view_layer.objects.active = o; o.select_set(True)
    bpy.ops.object.convert(target="MESH"); o.select_set(False)
    return o

def arc_pts(c, r, a0, a1, y, n=64): return [(c[0] + D(a0 + (a1 - a0) * i / n, r)[0], y, c[1] + D(a0 + (a1 - a0) * i / n, r)[1]) for i in range(n + 1)]
def ellipse_pts(c, rx, ry, n=72):   # ovale vertical, face au public (dans le plan x/y, a la profondeur c[2])
    return [(c[0] + math.cos(2 * math.pi * i / n) * rx, c[1] + math.sin(2 * math.pi * i / n) * ry, c[2]) for i in range(n)]

# ============================================================================================ sol
disc("Sol", (0, 3), 26, -0.1, 0.0, "Floor", seg=128)
# Neons incrustes dans le sol (fins rubans a fleur du sol).
def ribbon(name, c, r, a0, a1, w, material): sector(name, c, r - w / 2, r + w / 2, a0, a1, 0.0, 0.012, material)
for i, (r, m) in enumerate(((3.55, "NeonPurple"), (3.95, "NeonGreen"), (4.35, "NeonYellow"), (4.75, "NeonPink"), (5.15, "NeonCyan"))):
    ribbon(f"Cercle{i}", (0, 0), r, 105 + i * 4, 255 - i * 4, 0.07, m)
for i, r in enumerate((6.4, 6.9, 7.4)):
    ribbon(f"Allee{i}", (-2.6, 4.2), r, 175, 320, 0.05, "NeonWhite")
for i, (r, m) in enumerate(((9.6, "NeonPurple"), (10.2, "NeonBlue"), (10.8, "NeonPink"))):
    ribbon(f"Grand{i}", (2, 2), r, 145, 255, 0.06, m)
# Rayons lumineux qui partent du podium vers les gradins (comme les bandes de LED au sol de l'emission).
for i in range(5):
    x0 = 3.2 + i * 0.35
    box(f"Rayon{i}", x0 + 2.4, 0.006, 5.2 + i * 0.28, 5.2, 0.012, 0.05, "NeonWhite" if i % 2 == 0 else "NeonBlue", yaw=62)

# ============================================================================================ podium de la roue
tiers = ((3.1, 0.0, 0.24, "NeonGreen"), (2.75, 0.24, 0.52, "NeonCyan"), (2.45, 0.52, 0.80, "NeonOrange"))
for i, (r, y0, y1, glow) in enumerate(tiers):
    disc(f"Etage{i}", (0, 0), r, y0, y1, "PodiumSide", seg=96)
    sector(f"EtageBord{i}", (0, 0), r - 0.02, r + 0.03, 0, 360, y1 - 0.035, y1 + 0.004, glow)          # liseré du dessus
    sector(f"EtageBande{i}", (0, 0), r, r + 0.015, 0, 360, y0 + (y1 - y0) * 0.35, y0 + (y1 - y0) * 0.55, "NeonBlue")   # bande de LED sur le flanc
disc("DessusPodium", (0, 0), 2.44, 0.80, 0.81, "PodiumTop")
disc("SocleRoue", (0, 0), 1.62, 0.81, 0.83, "Chrome")

# ============================================================================================ candidats : estrade et comptoir
sector("Estrade", (0, 0), 2.8, 3.65, -86, 86, 0.0, 0.55, "StageSide", top="StageTop")
sector("EstradeBord", (0, 0), 2.78, 2.82, -86, 86, 0.50, 0.55, "NeonCyan")
sector("EstradeLed", (0, 0), 3.64, 3.67, -86, 86, 0.1, 0.16, "NeonPink")
# Marches aux deux bouts de l'estrade.
for s in (-1, 1):
    for k in range(3):
        a = s * (88 + k * 2.4)
        sector(f"Marche{s}{k}", (0, 0), 2.8, 3.65, a - (1.2 if s > 0 else -1.2), a + (1.2 if s > 0 else -1.2), 0.0, 0.55 - (k + 1) * 0.14, "StageSide", top="StageTop")
# Comptoir : jupe sombre (les panneaux des candidats sont devant), plateau blanc, LED rose dessous, rampe chromee.
sector("ComptoirJupe", (0, 0), 2.31, 2.41, -83, 83, 0.55, 1.30, "DeskFront")
sector("ComptoirPlateau", (0, 0), 2.18, 2.82, -84, 84, 1.30, 1.40, "DeskWhite")
sector("ComptoirLed", (0, 0), 2.17, 2.2, -84, 84, 1.26, 1.30, "NeonPink")
sector("ComptoirFilet", (0, 0), 2.17, 2.19, -84, 84, 1.40, 1.42, "Chrome")
tube("Rampe", arc_pts((0, 0), 2.24, -82, 82, 1.58, 80), 0.035, "Chrome")
for k in range(9):
    a = -80 + k * 20
    x, z = D(a, 2.24); cyl(f"Pied{k}", x, 1.49, z, 0.022, 0.18, "Chrome", verts=8)
for s in (-1, 1):   # bouts arrondis du comptoir
    x, z = D(s * 84, 2.5); cyl(f"Bout{s}", x, 0.975, z, 0.34, 0.85, "DeskWhite", verts=20)

# ============================================================================================ mur du fond
sector("Mur", (0, 3), 14.0, 14.4, -78, 78, 0.0, 9.5, "WallDark")
for y, m in ((0.9, "NeonBlue"), (2.9, "NeonWhite"), (3.2, "NeonWhite"), (6.9, "NeonBlue"), (8.4, "NeonWhite")):
    sector(f"MurBande{y}", (0, 3), 13.9, 14.0, -78, 78, y, y + 0.08, m)
for k in range(0, 157, 3):
    x, z = D(-78 + k, 13.85); sphere(f"MurAmpoule{k}", x, 7.6, 3 + z, 0.13, "Bulb", seg=8)
for k in range(11):
    a = -75 + k * 15
    x, z = D(a, 13.9); box(f"MurColonne{k}", x, 5.0, 3 + z, 0.22, 3.4, 0.06, "LedWarm", yaw=a)

# ============================================================================================ cabine vitree (fond, au milieu)
cab = (1.2, 10.5)
disc("CabineSol", cab, 1.62, 0.0, 0.12, "Chrome")
disc("CabineInterieur", cab, 1.35, 0.12, 4.3, "CabinInside")
for k in range(14):
    a = k * 360 / 14
    x, z = D(a, 1.52); box(f"Vitre{k}", cab[0] + x, 2.3, cab[1] + z, 0.64, 4.3, 0.04, "Glass", yaw=a)
    x, z = D(a + 360 / 28, 1.55); box(f"Montant{k}", cab[0] + x, 2.3, cab[1] + z, 0.05, 4.3, 0.05, "Chrome", yaw=a)
    for b in range(4):
        x, z = D(a, 1.45); sphere(f"CabineAmpoule{k}_{b}", cab[0] + x, 0.8 + b * 1.0, cab[1] + z, 0.07, "Bulb", seg=8)
tube("CabineHaut", arc_pts(cab, 1.56, 0, 360, 4.45, 48), 0.06, "Chrome")
sector("CabineHautLed", cab, 1.5, 1.62, 0, 360, 4.46, 4.52, "NeonWhite")

# ============================================================================================ gradins et public (a droite)
rows = 5
colors = ("NeonGreen", "NeonPurple", "NeonBlue", "NeonYellow", "NeonPink")
for s in range(rows):
    y0, z = 1.1 + s * 0.55, 11.0 + s * 0.85
    for side, (xa, xb) in (("G", (3.0, 7.9)), ("D", (9.1, 14.0))):
        box(f"Gradin{s}{side}", (xa + xb) / 2, y0, z, xb - xa, 0.55, 0.85, "SeatDark")
        box(f"GradinNez{s}{side}", (xa + xb) / 2, y0 + 0.27, z - 0.43, xb - xa, 0.05, 0.04, colors[s % len(colors)])
    # Escalier central eclaire.
    box(f"Escalier{s}", 8.5, y0 - 0.27 + 0.275, z, 1.2, 0.55, 0.85, "StepLight")
    box(f"EscalierNez{s}", 8.5, y0 + 0.28, z - 0.43, 1.2, 0.03, 0.03, "NeonWhite")
for k in range(3):
    box(f"EscalierBas{k}", 8.5, 0.18 + k * 0.28, 10.0 + k * 0.3, 1.2, 0.08, 0.3, "StepLight")
# Mur LED dore devant les gradins, avec bandes lumineuses.
box("MurDore", 8.5, 0.55, 10.35, 11.0, 1.1, 0.1, "LedGold")
for k, m in enumerate(("NeonWhite", "NeonYellow", "NeonWhite")):
    box(f"MurDoreBande{k}", 8.5, 0.2 + k * 0.35, 10.28, 11.0, 0.035, 0.03, m)
# Le public : corps, tete, cheveux ; quelques bras leves.
for s in range(rows):
    y0, z = 1.1 + s * 0.55 + 0.27, 11.1 + s * 0.85
    x = 3.3
    while x < 13.8:
        if abs(x - 8.5) < 0.75: x += 0.62; continue
        c = rng.randrange(8); skin = rng.randrange(4); hair = rng.randrange(4)
        cyl(f"Corps{s}_{x:.1f}", x, y0 + 0.26, z, 0.2, 0.52, f"Crowd{c}", verts=10, r2=0.15)
        sphere(f"Tete{s}_{x:.1f}", x, y0 + 0.64, z, 0.12, f"Skin{skin}")
        sphere(f"Cheveux{s}_{x:.1f}", x, y0 + 0.69, z + 0.03, 0.115, f"Hair{hair}")
        if rng.random() < 0.2:
            for sx in (-1, 1): box(f"Bras{s}_{x:.1f}{sx}", x + sx * 0.2, y0 + 0.62, z, 0.07, 0.42, 0.07, f"Crowd{c}", yaw=0)
        x += 0.58 + rng.random() * 0.1

# ============================================================================================ ovale d'ampoules (au-dessus des gradins)
oc = (6.5, 6.4, 13.6)
tube("Ovale", ellipse_pts(oc, 2.5, 1.05), 0.14, "Chrome", closed=True)
tube("OvaleLed", ellipse_pts((oc[0], oc[1], oc[2] - 0.08), 2.62, 1.16), 0.035, "NeonWhite", closed=True)
box("OvaleFond", oc[0], oc[1], oc[2] + 0.1, 4.8, 1.9, 0.04, "WallDark")
for k in range(13):
    x = oc[0] + (k - 6) * 0.34
    h = 2 * 1.0 * math.sqrt(max(0, 1 - ((x - oc[0]) / 2.45) ** 2)) - 0.15
    box(f"OvaleBarre{k}", x, oc[1], oc[2], 0.07, max(0.1, h), 0.04, "LedOrange")

# ============================================================================================ mur video courbe (a droite)
for k in range(10):
    a = 38 + k * 5.4
    x, z = D(a, 12.6)
    box(f"Ecran{k}", x, 3.0, 3 + z, 1.15, 4.4, 0.08, f"Screen{k % 5}", yaw=a)
    x, z = D(a + 2.7, 12.62); box(f"EcranJoint{k}", x, 3.0, 3 + z, 0.04, 4.5, 0.1, "Chrome", yaw=a)
for y, m in ((0.6, "NeonGreen"), (0.75, "NeonYellow"), (0.9, "NeonPurple"), (5.35, "NeonWhite")):
    sector(f"EcranBande{y}", (0, 3), 12.45, 12.5, 36, 92, y, y + 0.06, m)

# ============================================================================================ plafond : treillis, projecteurs, rampe d'ampoules
def truss(name, a, b, y):
    """Poutre en treillis (deux tubes et des croisillons) de a a b (x, z) a la hauteur y."""
    (xa, za), (xb, zb) = a, b
    L = math.hypot(xb - xa, zb - za); yaw = math.degrees(math.atan2(xb - xa, zb - za))
    for dy in (0, 0.35):
        tube(f"{name}T{dy}", [(xa, y + dy, za), (xb, y + dy, zb)], 0.04, "Truss")
    n = int(L / 0.5)
    for i in range(n):
        t0, t1 = i / n, (i + 1) / n
        tube(f"{name}X{i}", [(xa + (xb - xa) * t0, y, za + (zb - za) * t0), (xa + (xb - xa) * t1, y + 0.35, za + (zb - za) * t1)], 0.018, "Truss")
for i, z in enumerate((0.0, 4.0, 8.0)):
    truss(f"Treillis{i}", (-12, z), (12, z), 10.0)
for i, x in enumerate((-8.0, 0.0, 8.0)):
    truss(f"TreillisL{i}", (x, -5), (x, 12), 10.4)
for i in range(18):
    x, z = -10 + (i % 6) * 4, (i // 6) * 4.0
    cyl(f"Projo{i}", x, 9.7, z, 0.18, 0.42, "Can", verts=12)
    cyl(f"Lentille{i}", x, 9.48, z, 0.15, 0.02, "Lens", verts=12)
for k in range(27):
    x, z = D(-62 + k * 124 / 26, 11); sphere(f"Rampe{k}", x, 7.2, 3 + z, 0.2, "Bulb", seg=10)
tube("RampeHaut", arc_pts((0, 3), 11, -62, 62, 7.45, 60), 0.05, "NeonWhite")
tube("RampeBas", arc_pts((0, 3), 11.2, -62, 62, 6.9, 60), 0.045, "NeonBlue")

# ============================================================================================ piliers lumineux (cote gauche)
for i, (x, z) in enumerate(((-13.0, -1.0), (-12.5, 5.5), (-9.5, 11.0))):   # hors du champ du tableau
    cyl(f"Pilier{i}", x, 4.5, z, 0.3, 9.0, "WallDark", verts=16)
    for k in range(3):
        a = k * 120
        dx, dz = D(a, 0.3); cyl(f"PilierLed{i}{k}", x + dx, 4.5, z + dz, 0.035, 9.0, ("NeonPurple", "NeonBlue", "NeonPink")[i], verts=6)

# ============================================================================================ export
# Un seul objet (un sous-maillage par materiau) : quelques dizaines d'appels de rendu au lieu de 900.
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.join()
bpy.context.object.name = "Plateau"
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE")
print("PLATEAU ROUE :", OUT, len([o for o in bpy.context.scene.objects if o.type == "MESH"]), "objets,", len(mats), "materiaux")
