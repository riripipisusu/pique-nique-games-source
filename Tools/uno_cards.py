# Cartes du Uno : decoupe la planche fournie par l'utilisatrice (SourceArt/uno_sheet.webp, 12 x 6 cartes,
# hors depot comme les autres visuels sous licence) -> Assets/Resources/Uno/<code>.png, coins transparents.
#   codes : r0..r9, rS (passe), rR (inversion), rD (+2) pour r/y/g/b ; W / W4 (jokers noirs),
#           W_r.. / W4_r.. (jokers une fois la couleur choisie), back (dos)
import os
from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "SourceArt", "uno_sheet.webp")
OUT = os.path.join(ROOT, "Assets", "Resources", "Uno")
ROWS = [(0, 250), (252, 503), (504, 755), (756, 1008), (1009, 1260), (1261, 1513)]
COLS = [(0, 162), (163, 326), (327, 489), (490, 653), (654, 816), (817, 980), (981, 1143), (1147, 1309), (1310, 1473), (1474, 1636), (1637, 1800), (1801, 1963)]
FIRST = ["back", "W", "W_y", "W_r", "W_b", "W_g", "W4", "W4_y", "W4_r", "W4_b", "W4_g", None]   # None : carte vierge
REST = []
for c in "yrbg":
    REST += [c + str(i) for i in range(1, 10)] + [c + "0", c + "D", c + "S", c + "R"]

def main():
    os.makedirs(OUT, exist_ok=True)
    sheet = Image.open(SRC).convert("RGBA")
    names = FIRST + REST
    n = 0
    for k, name in enumerate(names):
        if name is None: continue
        r, c = divmod(k, 12)
        (y0, y1), (x0, x1) = ROWS[r], COLS[c]
        card = sheet.crop((x0, y0, x1, y1))
        # Fond de la planche -> transparent (remplissage depuis les 4 coins, les bords blancs de la carte l'arretent).
        for p in ((0, 0), (card.width - 1, 0), (0, card.height - 1), (card.width - 1, card.height - 1)):
            ImageDraw.floodfill(card, p, (0, 0, 0, 0), thresh=40)
        card.save(os.path.join(OUT, name + ".png"))
        n += 1
    assert n == 63, n   # 11 speciales + 4 couleurs x 13 (verification du decoupage)
    print(n, "cartes ->", OUT)

if __name__ == "__main__":
    main()
