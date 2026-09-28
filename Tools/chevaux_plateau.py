# Plateau des petits chevaux -> Assets/Resources/Chevaux/plateau.png (dessus du plateau en bois, 15 x 15 cases).
# Meme geometrie que ChevauxView.cs : colonne c, ligne r (r = 0 en haut, loin de la camera).
#   python Tools/chevaux_plateau.py
import os, math
from PIL import Image, ImageDraw, ImageFont

S, N = 2048, 15
C = S / N
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Chevaux", "plateau.png")
# Bras 0 haut vert, 1 droite jaune, 2 bas rouge, 3 gauche bleu (Board.Colors)
COL = [(69, 179, 95), (240, 185, 42), (232, 72, 59), (59, 125, 224)]
INK, PAPER = (92, 58, 34), (246, 234, 206)

def track():
    t = [(8, r) for r in range(6)] + [(8, 6)] + [(c, 6) for c in range(9, 15)] + [(14, 7)]
    t += [(c, 8) for c in range(14, 8, -1)] + [(8, 8)] + [(8, r) for r in range(9, 15)] + [(7, 14)]
    t += [(6, r) for r in range(14, 8, -1)] + [(6, 8)] + [(c, 8) for c in range(5, -1, -1)] + [(0, 7)]
    t += [(c, 6) for c in range(6)] + [(6, 6)] + [(6, r) for r in range(5, -1, -1)] + [(7, 0)]
    assert len(t) == 56 and len(set(t)) == 56
    return t

def ladder(arm):   # marches 1..6 depuis le bout du bras vers le centre
    return [[(7, k) for k in range(1, 7)], [(14 - k, 7) for k in range(1, 7)], [(7, 14 - k) for k in range(1, 7)], [(k, 7) for k in range(1, 7)]][arm]

STABLE = [(9, 0), (9, 9), (0, 9), (0, 0)]   # coin haut-gauche (c, r) de l'ecurie 6x6 de chaque bras

def mix(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))
def center(c, r): return ((c + 0.5) * C, (r + 0.5) * C)

img = Image.new("RGB", (S, S), PAPER)
d = ImageDraw.Draw(img)
font = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", int(C * 0.5))
T = track()

# bras de la croix legerement teintes, ecuries
for arm in range(4):
    for (c, r) in ladder(arm): d.rectangle([c * C, r * C, (c + 1) * C, (r + 1) * C], fill=mix(PAPER, COL[arm], 0.25))
    x, y = STABLE[arm]
    d.rounded_rectangle([x * C + 14, y * C + 14, (x + 6) * C - 14, (y + 6) * C - 14], radius=60, fill=COL[arm], outline=INK, width=8)
    d.rounded_rectangle([x * C + 60, y * C + 60, (x + 6) * C - 60, (y + 6) * C - 60], radius=40, fill=mix(COL[arm], PAPER, 0.55))
    for i in range(4):   # 4 places au box
        cx, cy = (x + 1.75 + (i % 2) * 2.5) * C, (y + 1.75 + (i // 2) * 2.5) * C
        d.ellipse([cx - C * 0.62, cy - C * 0.62, cx + C * 0.62, cy + C * 0.62], fill=PAPER, outline=COL[arm], width=14)
# trait du parcours sous les cases
for i in range(56):
    a, b = center(*T[i]), center(*T[(i + 1) % 56])
    d.line([a, b], fill=mix(INK, PAPER, 0.5), width=10)
# cases du parcours (depart de chaque couleur en couleur pleine)
for i, (c, r) in enumerate(T):
    x, y = center(c, r); rad = C * 0.4
    arm = i // 14 if i % 14 == 0 else -1
    d.ellipse([x - rad, y - rad, x + rad, y + rad], fill=COL[arm] if arm >= 0 else (255, 250, 240), outline=INK, width=7)
    if arm >= 0: d.ellipse([x - rad * 0.45, y - rad * 0.45, x + rad * 0.45, y + rad * 0.45], fill=(255, 250, 240))
# escaliers numerotes
for arm in range(4):
    for k, (c, r) in enumerate(ladder(arm)):
        x, y = center(c, r); rad = C * 0.42
        d.ellipse([x - rad, y - rad, x + rad, y + rad], fill=mix(COL[arm], (255, 255, 255), 0.15), outline=INK, width=7)
        d.text((x, y + 2), str(k + 1), fill=(255, 255, 255), font=font, anchor="mm", stroke_width=5, stroke_fill=INK)
# arrivee : rosace des quatre couleurs
x, y = center(7, 7); rad = C * 0.48
for arm in range(4):
    start = [-135, -45, 45, 135][arm]
    d.pieslice([x - rad, y - rad, x + rad, y + rad], start, start + 90, fill=COL[arm], outline=INK, width=6)
d.ellipse([x - rad * 0.3, y - rad * 0.3, x + rad * 0.3, y + rad * 0.3], fill=(255, 250, 240), outline=INK, width=5)
# cadre
d.rectangle([4, 4, S - 5, S - 5], outline=INK, width=10)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
img.save(OUT)
print("OK", OUT)
