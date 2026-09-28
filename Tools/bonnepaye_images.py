# Bonne Paye : decoupe les scans fournis par l'utilisatrice (BONNE PAYE ASSETS/IMAGES, module Tabletop Simulator)
# en images du jeu -> Assets/Resources/BonnePaye/Images (hors depot, comme les autres assets fournis).
# Numeros de cartes = cases des planches, comme dans Resources/BonnePaye/cartes.json.
#   python Tools/bonnepaye_images.py
import os
from PIL import Image
Image.MAX_IMAGE_PIXELS = None
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "BONNE PAYE ASSETS", "IMAGES")
OUT = os.path.join(ROOT, "Assets", "Resources", "BonnePaye", "Images")
os.makedirs(OUT, exist_ok=True)
def src(suffix): return Image.open(os.path.join(SRC, next(f for f in os.listdir(SRC) if f.endswith(suffix)))).convert("RGB")

# Plateau (6032 x 5982) ramene a 3072 x 3072.
src("1A15AF2E.jpg").resize((3072, 3072), Image.LANCZOS).save(os.path.join(OUT, "plateau.jpg"), quality=90)

# Planches : courriers en paysage (7 x 8), les autres en portrait (5 x 5) avec le texte couche : on les redresse.
# Detourage : on recadre sur la carte (le fond blanc du scan autour) et on arrondit les coins en transparence.
def cutout(card):
    import numpy as np
    from PIL import ImageDraw
    a = np.asarray(card)
    ink = (a < 232).any(axis=2)
    rows, cols = np.where(ink.any(axis=1))[0], np.where(ink.any(axis=0))[0]
    if len(rows) == 0: return card.convert("RGBA").resize((768, 488))
    card = card.crop((cols[0], rows[0], cols[-1] + 1, rows[-1] + 1)).resize((768, 488), Image.LANCZOS).convert("RGBA")
    k = 4; mask = Image.new("L", (768 * k, 488 * k), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, 768 * k - 1, 488 * k - 1], radius=34 * k, fill=255)
    card.putalpha(mask.resize((768, 488), Image.LANCZOS))
    return card

def sheet(suffix, name, cols, rows, turn):
    im = src(suffix); w, h = im.size; cw, ch = w / cols, h / rows
    for r in range(rows):
        for c in range(cols):
            card = im.crop((int(c * cw), int(r * ch), int((c + 1) * cw), int((r + 1) * ch)))
            if turn: card = card.rotate(90, expand=True)
            cutout(card).save(os.path.join(OUT, f"{name}_{r * cols + c}.png"), optimize=True)
sheet("2B61FF68.png", "courrier", 7, 8, False)
sheet("0A84859D.png", "acquisition", 5, 5, True)
sheet("61E2C251.png", "evenement", 5, 5, True)
cutout(src("B174B8B9.png").crop((0, 0, 640, 1006)).rotate(90, expand=True)).save(os.path.join(OUT, "pret.png"), optimize=True)
# Dos des pioches et tuile "Jour de paye".
for suffix, name in [("6B9A6E3F.jpg", "dos_courrier"), ("3888A97A.jpg", "dos_acquisition"), ("ACFEE32D.jpg", "dos_evenement"), ("DDEE2E96.jpg", "dos_pret")]:
    cutout(src(suffix).rotate(90, expand=True)).save(os.path.join(OUT, name + ".png"), optimize=True)
print("OK", len(os.listdir(OUT)), "images")
