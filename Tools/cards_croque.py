# Cartes Croque-Carotte : redresse, detoure, recadre et met au meme format les PNG de "CARTES CROQUE CAROTTE/PNG".
#   python Tools/cards_croque.py  -> Assets/Resources/UI/CroqueCards/*.png
import glob, os
import cv2, numpy as np

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "CARTES CROQUE CAROTTE", "PNG")
OUT = os.path.join(ROOT, "Assets", "Resources", "UI", "CroqueCards")
W, H, R = 750, 1050, 60          # format commun (5:7) et rayon des coins
NAMES = {"1 case": "1", "2 cases": "2", "3 cases": "3", "carotte 1": "carotte", "carotte double tour": "carotte2"}
os.makedirs(OUT, exist_ok=True)

def card(path):
    img = cv2.imread(path, cv2.IMREAD_UNCHANGED)
    alpha = img[:, :, 3]
    # Plus grande tache opaque = la carte (on ignore les points parasites).
    n, lab, stats, _ = cv2.connectedComponentsWithStats((alpha > 128).astype(np.uint8))
    big = 1 + np.argmax(stats[1:, cv2.CC_STAT_AREA])
    mask = (lab == big).astype(np.uint8) * 255
    pts = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)[0][0]
    rect = cv2.minAreaRect(pts)
    (cx, cy), (w, h), _ = rect
    w, h = min(w, h), max(w, h)
    # Inclinaison du grand cote par rapport a la verticale.
    box = cv2.boxPoints(rect)
    edges = [box[(i + 1) % 4] - box[i] for i in range(4)]
    e = max(edges, key=lambda v: np.hypot(*v))
    ang = np.degrees(np.arctan2(e[0], e[1]))
    while ang > 45: ang -= 180
    while ang < -45: ang += 180
    ang = -ang
    # Redresse autour du centre, puis decoupe le rectangle de la carte (un peu rentre pour eliminer le liseré).
    M = cv2.getRotationMatrix2D((cx, cy), ang, 1.0)
    img[:, :, 3] = cv2.bitwise_and(alpha, mask)
    rot = cv2.warpAffine(img, M, (img.shape[1], img.shape[0]), flags=cv2.INTER_CUBIC, borderValue=(0, 0, 0, 0))
    m = 6
    x0, y0 = int(round(cx - w / 2)) + m, int(round(cy - h / 2)) + m
    crop = rot[y0:int(round(cy + h / 2)) - m, x0:int(round(cx + w / 2)) - m]
    out = cv2.resize(crop, (W, H), interpolation=cv2.INTER_AREA)
    # Coins arrondis identiques pour toutes.
    rr = np.zeros((H, W), np.uint8)
    cv2.rectangle(rr, (R, 0), (W - R, H), 255, -1); cv2.rectangle(rr, (0, R), (W, H - R), 255, -1)
    for x, y in [(R, R), (W - R - 1, R), (R, H - R - 1), (W - R - 1, H - R - 1)]: cv2.circle(rr, (x, y), R, 255, -1, cv2.LINE_AA)
    out[:, :, 3] = rr
    return out, ang, (w, h)

IW, IH, IR, B = 664, 964, 30, 43     # illustration des cartes lapin (5:7 environ), coins, bordure blanche

def rounded(w, h, r):
    rr = np.zeros((h, w), np.uint8)
    cv2.rectangle(rr, (r, 0), (w - r, h), 255, -1); cv2.rectangle(rr, (0, r), (w, h - r), 255, -1)
    for x, y in [(r, r), (w - r - 1, r), (r, h - r - 1), (w - r - 1, h - r - 1)]: cv2.circle(rr, (x, y), r, 255, -1, cv2.LINE_AA)
    return rr

# Cartes lapin : l'illustration (ciel + herbe) est redressee seule puis reposee dans une bordure blanche identique.
def rabbit(path):
    img = cv2.imread(path, cv2.IMREAD_UNCHANGED)
    bgr, alpha = img[:, :, :3], img[:, :, 3]
    ink = ((bgr.min(axis=2) < 225) & (alpha > 128)).astype(np.uint8)
    ink = cv2.morphologyEx(ink, cv2.MORPH_CLOSE, np.ones((25, 25), np.uint8))
    n, lab, stats, _ = cv2.connectedComponentsWithStats(ink)
    big = 1 + np.argmax(stats[1:, cv2.CC_STAT_AREA])
    panel = (lab == big).astype(np.uint8)
    # Le bas de l'illustration se fond dans le blanc : l'angle vient de la moitie haute (bords nets).
    top, bottom = stats[big, cv2.CC_STAT_TOP], stats[big, cv2.CC_STAT_TOP] + stats[big, cv2.CC_STAT_HEIGHT]
    upper = panel.copy(); upper[(top + bottom) // 2:] = 0
    rect = cv2.minAreaRect(cv2.findContours(upper, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)[0][0])
    box = cv2.boxPoints(rect)
    e = max([box[(i + 1) % 4] - box[i] for i in range(4)], key=lambda v: np.hypot(*v))
    # grand cote de la moitie haute = largeur (horizontale) : angle par rapport a l'horizontale
    ang = np.degrees(np.arctan2(e[1], e[0]))
    while ang > 45: ang -= 180
    while ang < -45: ang += 180
    card_mask = (alpha > 128).astype(np.uint8)
    n2, lab2, st2, _ = cv2.connectedComponentsWithStats(card_mask)
    card_mask = (lab2 == 1 + np.argmax(st2[1:, cv2.CC_STAT_AREA])).astype(np.uint8)
    (cx, cy) = rect[0]
    M = cv2.getRotationMatrix2D((cx, cy), ang, 1.0)
    rot = cv2.warpAffine(bgr, M, (img.shape[1], img.shape[0]), flags=cv2.INTER_CUBIC, borderValue=(255, 255, 255))
    rp = cv2.warpAffine(panel, M, (img.shape[1], img.shape[0]), flags=cv2.INTER_NEAREST)
    rc = cv2.warpAffine(card_mask, M, (img.shape[1], img.shape[0]), flags=cv2.INTER_NEAREST)
    ys, xs = np.nonzero(rp[: (top + bottom) // 2 + 1])
    cys, cxs = np.nonzero(rc)
    m = 8
    x0, x1, y0 = np.percentile(xs, 0.5) + m, np.percentile(xs, 99.5) - m, ys.min() + m
    side = x0 - cxs.min()
    y1 = cys.max() - side          # meme marge en bas que sur les cotes (le caillou reste dedans)
    x0, x1, y0, y1 = int(x0), int(x1), int(y0), int(y1)
    w, h = x1 - x0, y1 - y0
    art = cv2.resize(rot[y0:y1, x0:x1], (IW, IH), interpolation=cv2.INTER_AREA)
    out = np.full((H, W, 4), 255, np.uint8)
    inner = rounded(IW, IH, IR)[:, :, None] / 255.0
    region = out[B:B + IH, B:B + IW, :3]
    out[B:B + IH, B:B + IW, :3] = (art * inner + region * (1 - inner)).astype(np.uint8)
    out[:, :, 3] = rounded(W, H, R)
    return out, ang, (w, h)

sheet = []
for f in sorted(glob.glob(os.path.join(SRC, "*.png"))):
    name = os.path.splitext(os.path.basename(f))[0]
    out, ang, size = (rabbit if "case" in name else card)(f)
    cv2.imwrite(os.path.join(OUT, NAMES.get(name, name) + ".png"), out)
    print(f"{name}: rotation {ang:+.2f} deg, carte {size[0]:.0f}x{size[1]:.0f} -> {W}x{H}")
    bg = np.full((H, W, 3), (140, 110, 90), np.uint8)
    a = out[:, :, 3:4] / 255.0
    sheet.append((out[:, :, :3] * a + bg * (1 - a)).astype(np.uint8))

