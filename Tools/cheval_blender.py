# Pion des petits chevaux : tete de cavalier d'echecs low-poly (profil extrude, aretes biseautees) sur un socle tourne.
# 1 m de haut, origine sous le socle, la tete regarde vers -y Blender (= +z Unity, "devant").
# Materiaux : "Pion" (recolore par joueur dans Unity) et "Oeil".
#   blender -b -P Tools/cheval_blender.py -- <apercu.png>
import bpy, bmesh, sys, os, math
PREVIEW = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else None
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Models", "Cheval.fbx")
bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, rgb):
    m = bpy.data.materials.new(name); m.diffuse_color = (*rgb, 1); return m
pion, oeil = mat("Pion", (0.9, 0.9, 0.9)), mat("Oeil", (0.05, 0.04, 0.04))

def link(name, bm):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); return o

# --- Socle : profil tourne (rayon, hauteur), 20 facettes ---
prof = [(0, 0), (0.40, 0), (0.42, 0.03), (0.40, 0.07), (0.33, 0.09), (0.31, 0.12), (0.26, 0.16), (0.24, 0.22), (0.28, 0.25), (0.29, 0.28), (0, 0.28)]
bm = bmesh.new(); N = 20
rings = [[bm.verts.new((r * math.cos(2 * math.pi * k / N), r * math.sin(2 * math.pi * k / N), z)) for k in range(N)] for r, z in prof]
for a, b in zip(rings, rings[1:]):
    for k in range(N): bm.faces.new((a[k], a[(k + 1) % N], b[(k + 1) % N], b[k]))
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
socle = link("Socle", bm); socle.data.materials.append(pion)

# --- Tete et encolure : silhouette du cavalier (x vers le museau, z vers le haut), epaisseur en y ---
sil = [(-0.27, 0.26), (-0.30, 0.48), (-0.25, 0.70), (-0.15, 0.86), (-0.07, 0.95), (-0.03, 1.05), (0.03, 0.96), (0.13, 0.93),
       (0.25, 0.85), (0.35, 0.74), (0.43, 0.64), (0.46, 0.56), (0.41, 0.50), (0.30, 0.50), (0.18, 0.55), (0.10, 0.51),
       (0.12, 0.40), (0.21, 0.26)]
T = 0.27
bm = bmesh.new()
face = bm.faces.new([bm.verts.new((x, -T / 2, z)) for x, z in sil])
ext = bmesh.ops.extrude_face_region(bm, geom=[face])
bmesh.ops.translate(bm, vec=(0, T, 0), verts=[v for v in ext["geom"] if isinstance(v, bmesh.types.BMVert)])
bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])   # les deux flancs
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
tete = link("Tete", bm); tete.data.materials.append(pion)
bev = tete.modifiers.new("biseau", "BEVEL"); bev.width = 0.035; bev.segments = 2; bev.limit_method = "ANGLE"; bev.angle_limit = math.radians(30)

# --- Criniere : une arete le long de la nuque ---
bm = bmesh.new()
crin = [(-0.31, 0.46), (-0.27, 0.70), (-0.17, 0.87), (-0.09, 0.94), (-0.05, 0.90), (-0.12, 0.83), (-0.20, 0.68), (-0.24, 0.47)]
f = bm.faces.new([bm.verts.new((x, -0.05, z)) for x, z in crin])
ext = bmesh.ops.extrude_face_region(bm, geom=[f])
bmesh.ops.translate(bm, vec=(0, 0.10, 0), verts=[v for v in ext["geom"] if isinstance(v, bmesh.types.BMVert)])
bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
criniere = link("Criniere", bm); criniere.data.materials.append(pion)

# --- Yeux : deux petites billes de chaque cote ---
yeux = []
for s in (-1, 1):
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.035)
    bmesh.ops.translate(bm, vec=(0.22, s * (T / 2 + 0.005), 0.80), verts=bm.verts)
    o = link("Oeil", bm); o.data.materials.append(oeil); yeux.append(o)

# --- Assemblage, museau vers -y (devant pour Unity), export ---
bpy.ops.object.select_all(action="DESELECT")
for o in [socle, tete, criniere] + yeux: o.select_set(True)
bpy.context.view_layer.objects.active = tete
bpy.ops.object.convert(target="MESH")   # applique le biseau
bpy.ops.object.join()
p = bpy.context.active_object; p.name = "Cheval"
p.rotation_euler = (0, 0, math.radians(-90)); bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
for poly in p.data.polygons: poly.use_smooth = False
print("DIMS", tuple(round(x, 3) for x in p.dimensions), len(p.data.polygons), "faces")
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE")
if PREVIEW:
    pion.diffuse_color = (0.85, 0.2, 0.18, 1)
    cam = bpy.data.objects.new("c", bpy.data.cameras.new("c")); bpy.context.scene.collection.objects.link(cam)
    cam.location = (2.2, -2.2, 1.3); cam.rotation_euler = (math.radians(78), 0, math.radians(45)); bpy.context.scene.camera = cam
    bpy.context.scene.world = bpy.data.worlds.new("w")
    sc = bpy.context.scene; sc.render.engine = "BLENDER_WORKBENCH"; sc.render.resolution_x = sc.render.resolution_y = 500
    sc.display.shading.color_type = "MATERIAL"; sc.display.shading.light = "STUDIO"
    sc.render.filepath = PREVIEW; bpy.ops.render.render(write_still=True)
print("OK", OUT)
