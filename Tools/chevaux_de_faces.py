# Texture du de et du gobelet en PNG, et face de chaque valeur du de (points comptes dans chaque face de l'OBJ).
# A lancer apres Tools/chevaux_de_blender.py :  python Tools/chevaux_de_faces.py
import os
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
D = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Chevaux", "De")
A = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Board Game Items", "Textures")
Image.open(os.path.join(A, "dice_shaker_D512.tif")).convert("RGB").save(os.path.join(D, "de_gobelet.png"))
V, T, F = [], [], []
for l in open(os.path.join(D, "de_rouge.obj")):
    p = l.split()
    if not p: continue
    if p[0] == "v": V.append(list(map(float, p[1:4])))
    elif p[0] == "vt": T.append(list(map(float, p[1:3])))
    elif p[0] == "f": F.append([tuple(int(x) for x in q.split("/")[:2]) for q in p[1:]])
V, T = np.array(V), np.array(T)
im = np.array(Image.open(os.path.join(D, "de_gobelet.png")).convert("L")); H, W = im.shape
print("taille", V.max(0) - V.min(0))
faces = {}
for f in F:
    pts = V[[a - 1 for a, _ in f]]
    n = np.cross(pts[1] - pts[0], pts[2] - pts[0]); l = np.linalg.norm(n)
    if l == 0: continue
    n /= l; ax = int(np.argmax(abs(n)))
    if abs(n[ax]) < 0.95: continue
    key = ("+" if n[ax] > 0 else "-") + "xyz"[ax]
    mask = Image.new("L", (W, H), 0); ImageDraw.Draw(mask).polygon([(u * W, (1 - v) * H) for u, v in T[[b - 1 for _, b in f]]], fill=1)
    faces[key] = faces.get(key, np.zeros((H, W), bool)) | np.array(mask).astype(bool)
for k, m in sorted(faces.items()):
    print(k, "points", ndimage.label((im > 200) & m)[1])
