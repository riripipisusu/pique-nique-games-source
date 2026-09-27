# Jeu de rythme (TV Time) : convertit les charts du projet Godot de l'utilisatrice en un seul JSON pour Unity.
#   1. Les .mid importes par godot_midi (.godot/imported/*.mid-*.res, binaires) sont d'abord convertis en texte avec
#      GDRE Tools : gdre_tools.exe --headless --bin-to-txt=<fichier> (sortie dans le dossier de GDRE).
#   2. python Tools/rhythm_charts.py <dossier des .res texte> <index.txt>
#      -> Assets/Resources/Rhythm/charts.json   (notes, metadonnees)
#      -> Assets/StreamingAssets/Rhythm/<id>.ogg et <id>.png   (musiques et pochettes, hors depot)
# Notes MIDI (GlobalVariables.gd du jeu d'origine) : 38 gauche, 36 droite ; tenues 39/50 gauche, 35/48 droite ;
# 42-46 (Susie) et 28-32 / 53-77 (Ralsei...) = animations des musiciens, gardees comme "pulsations" de la troupe.
import json, os, re, shutil, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = r"C:\Users\Anastasia\Documents\MON JEU\jeu rythme univers deltarune\Charts"
OUT_JSON = os.path.join(ROOT, "Assets", "Resources", "Rhythm", "charts.json")
OUT_MEDIA = os.path.join(ROOT, "Assets", "StreamingAssets", "Rhythm")
TAP = {38: "L", 36: "R"}
HOLD = {39: "L", 50: "L", 35: "R", 48: "R"}
BAND = set(range(28, 33)) | set(range(42, 47)) | set(range(53, 78))

def slug(s):
    return re.sub(r"[^a-z0-9]+", "-", s.lower()).strip("-")

def meta(path):
    m = {}
    if os.path.exists(path):
        for line in open(path, encoding="utf-8", errors="replace"):
            if ":" in line:
                k, v = line.split(":", 1)
                m[k.strip().lower()] = v.strip()
    return m

def chart(text):
    head = dict(re.findall(r"^(\w+) = (\d+)$", text, re.M))
    division = int(head["division"])
    tracks = json.loads(text[text.index("tracks = ") + 9:].strip())
    # Temps absolu en ticks de chaque evenement, puis carte des tempos commune a toutes les pistes.
    events, tempos = [], [(0, int(head.get("tempo", 500000)))]
    for tr in tracks:
        tick = 0
        for e in tr["events"]:
            tick += e["delta"]
            if e["type"] == "meta" and e.get("subtype") == 81: tempos.append((tick, e["data"]))
            elif e["type"] == "note" and e.get("subtype") in (8, 9): events.append((tick, e))
    # Carte des tempos : le "tempo" d'en-tete de godot_midi vaut toujours 500000 (120 BPM par defaut) ; il ne sert
    # que si la chanson n'a pas de tempo au tout debut. A tick egal, le dernier tempo lu l'emporte.
    by_tick = {}
    for t, u in sorted(tempos[1:], key=lambda x: x[0]): by_tick[t] = u
    if 0 not in by_tick: by_tick[0] = tempos[0][1]
    tempos = sorted(by_tick.items())
    marks, sec, last_tick, us = [], 0.0, 0, tempos[0][1]
    for t, u in tempos:
        sec += (t - last_tick) * us / (division * 1e6); last_tick, us = t, u; marks.append((t, sec, u))
    def seconds(tick):
        t0, s0, u = max((m for m in marks if m[0] <= tick), key=lambda m: m[0])
        return s0 + (tick - t0) * u / (division * 1e6)
    notes, band, open_hold = [], [], {}
    for tick, e in sorted(events, key=lambda x: x[0]):
        on = e["subtype"] == 9 and e["data"] > 0
        n, t = e["note"], round(seconds(tick), 4)
        if on and n in TAP: notes.append([t, TAP[n], 0.0])
        elif on and n in HOLD: open_hold[HOLD[n]] = t
        elif not on and n in HOLD and HOLD[n] in open_hold:
            start = open_hold.pop(HOLD[n])
            if t - start > 0.05: notes.append([start, HOLD[n], round(t - start, 4)])
        elif on and n in BAND: band.append(t)
    # Une tenue qui commence sur une frappe du meme cote : une seule note (appuyer puis tenir).
    notes.sort(key=lambda x: (x[0], x[1], -x[2]))
    merged = []
    for nt in notes:
        if merged and merged[-1][1] == nt[1] and abs(merged[-1][0] - nt[0]) < 0.03:
            merged[-1][2] = max(merged[-1][2], nt[2]); continue
        merged.append(nt)
    bpm = round(60e6 / tempos[0][1], 2)
    return merged, sorted(set(band)), bpm

def main(txt_dir, index):
    os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
    os.makedirs(OUT_MEDIA, exist_ok=True)
    songs = []
    for line in open(index, encoding="utf-8"):
        num, rel = line.strip().split("|", 1)
        chapter, title = rel.split("/", 1)
        folder = os.path.join(SRC, chapter, title)
        notes, _, bpm = chart(open(os.path.join(txt_dir, num + ".res"), encoding="utf-8").read())
        if not notes: print("  sans notes :", rel); continue
        m = meta(os.path.join(folder, title + ".txt"))
        sid = slug(chapter + "-" + title)
        audio = next((os.path.join(folder, title + ext) for ext in (".ogg", ".mp3", ".wav") if os.path.exists(os.path.join(folder, title + ext))), None)
        if not audio: print("  sans musique :", rel); continue
        ext = os.path.splitext(audio)[1]
        shutil.copyfile(audio, os.path.join(OUT_MEDIA, sid + ext))
        cover = os.path.join(folder, title + ".png")
        if os.path.exists(cover): shutil.copyfile(cover, os.path.join(OUT_MEDIA, sid + ".png"))
        try: vol = float(m.get("volume", "0"))
        except ValueError: vol = 0
        try: order = int(float(m.get("order", "999")))
        except ValueError: order = 999
        songs.append({"id": sid, "t": title, "ch": chapter, "dif": m.get("difficulty", ""), "d": m.get("description", ""),
                      "by": m.get("composer", "Toby Fox"), "vol": vol, "ord": order, "bpm": bpm, "audio": sid + ext,
                      "cover": os.path.exists(cover), "end": round(max(x[0] + x[2] for x in notes), 3),
                      "n": [v for x in notes for v in (x[0], 0 if x[1] == "L" else 1, x[2])]})   # plat : t, piste, duree
    songs.sort(key=lambda s: (s["ch"], s["ord"], s["t"]))
    json.dump({"songs": songs}, open(OUT_JSON, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
    print(len(songs), "chansons,", sum(len(s["n"]) // 3 for s in songs), "notes,", sum(1 for s in songs for d in s["n"][2::3] if d > 0), "tenues")

# Verification : a 138 BPM (tempo 434783 au debut, en-tete a 500000), un temps plus tard = 60/138 s.
def _check():
    head = "division = 480\ntempo = 500000\ntracks = "
    ev = [{"delta": 0, "type": "meta", "subtype": 81, "data": 434783}, {"delta": 480, "type": "note", "subtype": 9, "note": 38, "data": 100}]
    notes, _, bpm = chart(head + json.dumps([{"events": ev}]))
    assert abs(notes[0][0] - 0.4348) < 1e-3 and abs(bpm - 138) < 0.1, (notes, bpm)

if __name__ == "__main__":
    _check()
    main(sys.argv[1], sys.argv[2])
