# Plateau de Serpents et echelles -> Assets/Resources/Serpents/plateau.png : 10 x 10 cases numerotees en serpentin
# (1 en bas a gauche, 100 en haut a gauche). Serpents et echelles sont en 3D (SerpentsView), pas peints.
#   python Tools/serpents_plateau.py
import os
from PIL import Image, ImageDraw, ImageFont

S, N = 2048, 10
C = S / N
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Serpents", "plateau.png")
# Couleurs franches du plateau de reference : rouge, bleu, jaune, vert, blanc (voisines toujours differentes).
PALETTE = [(226, 58, 48), (52, 142, 214), (245, 200, 30), (88, 178, 72), (250, 248, 240)]
INK = (74, 44, 26)

def cell_xy(n):   # case n (1..100) -> (colonne, ligne depuis le haut)
    r = (n - 1) // 10
    c = (n - 1) % 10 if r % 2 == 0 else 9 - (n - 1) % 10
    return c, 9 - r

img = Image.new("RGB", (S, S), (255, 248, 230))
d = ImageDraw.Draw(img)
big = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", int(C * 0.36))
small = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", int(C * 0.16))
for n in range(1, 101):
    c, r = cell_xy(n)
    x0, y0 = c * C, r * C
    col = PALETTE[(c * 3 + r * 2 + (c * r) % 2) % len(PALETTE)]
    d.rounded_rectangle([x0 + 5, y0 + 5, x0 + C - 5, y0 + C - 5], radius=22, fill=col, outline=INK, width=5)
    d.text((x0 + C / 2, y0 + C / 2), str(n), fill=(60, 40, 30), font=big, anchor="mm", stroke_width=4, stroke_fill=(255, 255, 255))
    if n == 100:   # etoile d'arrivee
        import math
        cx, cy, R = x0 + C * 0.24, y0 + C * 0.26, C * 0.15
        d.polygon([(cx + (R if k % 2 == 0 else R * 0.45) * math.cos(math.pi / 2 + k * math.pi / 5), cy - (R if k % 2 == 0 else R * 0.45) * math.sin(math.pi / 2 + k * math.pi / 5)) for k in range(10)], fill=(245, 200, 30), outline=INK)
    if n == 1: d.text((x0 + C / 2, y0 + C - 34), "DÉPART", fill=INK, font=small, anchor="mm")
    if n == 100: d.text((x0 + C / 2, y0 + C - 34), "ARRIVÉE", fill=INK, font=small, anchor="mm")
d.rectangle([3, 3, S - 4, S - 4], outline=INK, width=8)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
img.save(OUT)
print("OK", OUT)
