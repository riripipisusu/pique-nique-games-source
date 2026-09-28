# Banque du "Grand quiz de Tenna" par categories -> Assets/Resources/Quiz/tenna.json
#   python Tools/tenna_pool.py            (tout)
#   python Tools/tenna_pool.py maths rebus (seulement ces categories, les autres sont gardees)
# Sources : OpenQuizzDB (CC BY-SA), Wikidata (CC0) et Wikimedia Commons (images et sons sous licence libre, charges
# par le jeu depuis leur URL : rien n'est embarque), Twemoji (CC BY 4.0) pour les rebus, et des questions generees ici.
# Tout est mis en cache dans Tools/quiz_cache/ (hors Git).
import hashlib, html, json, math, os, random, re, sys, time, urllib.parse
import requests

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(ROOT, "..", "Assets", "Resources", "Quiz", "tenna.json")
CACHE = os.path.join(ROOT, "quiz_cache", "tenna")
OQ = os.path.join(ROOT, "quiz_cache", "oqdb")
os.makedirs(CACHE, exist_ok=True)
S = requests.Session()
S.headers["User-Agent"] = "PiqueNiqueGames-Quiz/1.0 (https://github.com/riripipisusu/pique-nique-games)"

def cached(name, fetch, pause=1.0):
    path = os.path.join(CACHE, name)
    if os.path.exists(path):
        return json.load(open(path, encoding="utf-8"))
    data = fetch()
    json.dump(data, open(path, "w", encoding="utf-8"), ensure_ascii=False)
    time.sleep(pause)
    return data

def sparql(name, query):
    def fetch():
        for attempt in range(5):
            r = S.get("https://query.wikidata.org/sparql", params={"query": query, "format": "json"}, timeout=120)
            if r.status_code in (429, 500, 502, 503, 504): time.sleep(5 + attempt * 5); continue
            r.raise_for_status()
            return [{k: v["value"] for k, v in b.items()} for b in r.json()["results"]["bindings"]]
        raise RuntimeError("SPARQL indisponible : " + name)
    return cached("sparql_" + name + ".json", fetch, 2)

def commons(file, width=800):
    # URL de rendu d'un fichier Commons (PNG pour les SVG), redirigee vers upload.wikimedia.org.
    file = urllib.parse.unquote(file.split("/")[-1]).replace(" ", "_")
    return f"https://commons.wikimedia.org/wiki/Special:FilePath/{urllib.parse.quote(file)}?width={width}"

def resolve(url):
    # Suit la redirection une fois (cache) : le jeu charge directement le fichier final. Limite de debit (429) :
    # on attend et on reessaie ; seuls les vrais resultats (trouve / introuvable) vont dans le cache.
    path = os.path.join(CACHE, "head_" + hashlib.sha1(url.encode()).hexdigest() + ".json")
    if os.path.exists(path): return json.load(open(path, encoding="utf-8"))
    for attempt in range(8):
        try: r = S.head(url, allow_redirects=True, timeout=30)
        except requests.RequestException: time.sleep(5); continue
        if r.status_code == 429 or r.status_code >= 500:
            time.sleep(float(r.headers.get("Retry-After", 5 * (attempt + 1)))); continue
        val = r.url if r.status_code == 200 else None
        json.dump(val, open(path, "w", encoding="utf-8"))
        time.sleep(0.5)
        return val
    print("  abandon :", url[:90])
    return None

rng = random.Random(7)

def mcq(c, q, d, wrong, **extra):
    wrong = [w for w in dict.fromkeys(wrong) if w != d][:3]
    if len(wrong) < 3: return None
    p = wrong + [d]
    rng.shuffle(p)
    item = {"c": c, "q": q, "d": d, "p": p, "a": [d]}
    item.update({k: v for k, v in extra.items() if v not in (None, "", [])})
    return item

# ---------------------------------------------------------------- OpenQuizzDB
def oq_quiz(qid):
    path = os.path.join(OQ, f"{qid}.json")
    if not os.path.exists(path):
        page = S.post("https://www.openquizzdb.org/download.php", data={"id": qid}, timeout=30).content.decode("utf-8", "replace")
        open(os.path.join(OQ, f"{qid}.html"), "w", encoding="utf-8").write(page)
        url = re.search(r'href="(https://download\.openquizzdb\.org/[^"]+\.json)"', page)
        if not url: return []
        time.sleep(1.5)
        open(path, "w", encoding="utf-8").write(S.get(url.group(1), timeout=30).content.decode("utf-8"))
        time.sleep(1.5)
    return json.load(open(path, encoding="utf-8")).get("quizz", [])

def oq_listing():
    s = open(os.path.join(OQ, "listing.html"), encoding="utf-8").read()
    theme, out = None, []
    for m in re.finditer(r"&middot;\s*([^&<]{2,40}?)\s*&middot;|goq\((\d+)\)\"?>\s*([^<]+)<", s):
        if m.group(1): theme = html.unescape(m.group(1)).strip()
        elif theme: out.append((int(m.group(2)), theme, html.unescape(m.group(3)).strip()))
    return out

def oq_items(c, pick):
    out = []
    for qid, theme, title in oq_listing():
        if not pick(theme, title): continue
        for q in oq_quiz(qid):
            props = [p.strip() for p in q.get("propositions", [])]
            ans = (q.get("réponse") or "").strip()
            if len(props) != 4 or ans not in props or not q.get("question"): continue
            out.append({"c": c, "q": q["question"].strip(), "d": ans, "p": props, "a": [ans], "h": (q.get("anecdote") or "").strip(),
                        "cr": f"Quiz « {title} » · OpenQuizzDB (CC BY-SA)"})
    return out

CATS = {}
def cat(key):
    def deco(f): CATS[key] = f; return f
    return deco

@cat("culture")
def culture():
    # La banque generale deja livree (OpenQuizzDB).
    items = json.load(open(os.path.join(ROOT, "..", "Assets", "Resources", "Quiz", "trivia.json"), encoding="utf-8"))
    for q in items: q["c"] = "culture"; q.pop("id", None)
    return items

@cat("francais")
def francais():
    return oq_items("francais", lambda t, n: n.startswith("Orthoquizz"))

# ---------------------------------------------------------------- Maths (genere)
def near(n, spread=None):
    spread = spread or max(1, abs(n) // 10)
    out = set()
    for d in (1, -1, 2, -2, 10, -10, spread, -spread, 2 * spread):
        if n + d != n and n + d >= 0: out.add(n + d)
    s = str(n)
    if len(s) >= 2: out.add(int(s[::-1]))
    out.discard(n)
    out = sorted(out); rng.shuffle(out)
    return out

def num(n): return f"{n:,}".replace(",", " ")

@cat("maths")
def maths():
    out = []
    def add(q, n, wrong=None):
        wrong = [num(w) for w in (wrong or []) + near(n) if w != n and w >= 0]
        out.append(mcq("maths", q, num(n), wrong))
    for _ in range(40):
        a, b = rng.randint(12, 999), rng.randint(12, 999); add(f"Combien font {a} + {b} ?", a + b, [a + b + 10, a + b - 10, a + b + 100])
    for _ in range(30):
        a, b = rng.randint(100, 999), rng.randint(12, 99); add(f"Combien font {a} − {b} ?", a - b, [a - b + 10, a - b - 10])
    for _ in range(40):
        a, b = rng.randint(3, 12), rng.randint(3, 12); add(f"Combien font {a} × {b} ?", a * b, [a * (b + 1), (a + 1) * b, a * b + 2])
    for _ in range(25):
        a, b = rng.randint(11, 49), rng.randint(3, 9); add(f"Combien font {a} × {b} ?", a * b, [a * b + 10, a * b - b, a * (b - 1)])
    for _ in range(25):
        b, r = rng.randint(3, 12), rng.randint(4, 25); add(f"Combien font {b * r} ÷ {b} ?", r, [r + 1, r - 1, r + 2])
    for _ in range(25):
        p = rng.choice([10, 20, 25, 50, 5, 15, 30, 75]); base = rng.choice([40, 60, 80, 120, 200, 240, 300, 400, 500, 800])
        add(f"Combien font {p} % de {base} ?", base * p // 100, [base * p // 10, base - base * p // 100, base * p // 100 + 5])
    for _ in range(20):
        a, b, c = rng.randint(2, 20), rng.randint(2, 9), rng.randint(2, 9)
        add(f"Combien font {a} + {b} × {c} ?", a + b * c, [(a + b) * c, a + b + c, a * b + c])
    for n in range(11, 26):
        add(f"Combien font {n}² ?", n * n, [n * 2, n * n + n, (n + 1) * (n + 1)])
    for n in rng.sample(range(4, 31), 15):
        add(f"Quelle est la racine carrée de {n * n} ?", n, [n + 1, n - 1, n * 2])
    for _ in range(20):
        a, x = rng.randint(2, 9), rng.randint(2, 15); b = rng.randint(1, 30)
        add(f"Si {a}x + {b} = {a * x + b}, combien vaut x ?", x, [x + 1, x - 1, a * x])
    for _ in range(15):
        d = rng.choice([2, 3, 4, 5, 10]); base = d * rng.randint(3, 30)
        name = {2: "la moitié", 3: "le tiers", 4: "le quart", 5: "le cinquième", 10: "le dixième"}[d]
        add(f"Combien vaut {name} de {base} ?", base // d, [base // d + 1, base * d, base - base // d])
    conv = [("kilomètres", "mètres", 1000), ("heures", "minutes", 60), ("minutes", "secondes", 60), ("mètres", "centimètres", 100),
            ("kilos", "grammes", 1000), ("litres", "centilitres", 100), ("jours", "heures", 24)]
    for _ in range(20):
        a, b, k = rng.choice(conv); v = rng.randint(2, 12)
        out.append(mcq("maths", f"Combien de {b} dans {v} {a} ?", num(v * k), [num(v * k * 10), num(v * k // 10), num((v + 1) * k)]))
    return out

# ---------------------------------------------------------------- Psychotechnique (genere + ecrit)
ODD = [  # l'intrus en dernier
    ("Chien", "Chat", "Lapin", "Voiture"), ("Rouge", "Bleu", "Vert", "Carré"), ("Pomme", "Poire", "Banane", "Carotte"),
    ("Paris", "Lyon", "Marseille", "Belgique"), ("Violon", "Guitare", "Harpe", "Trompette"), ("Lundi", "Mardi", "Jeudi", "Janvier"),
    ("Aigle", "Moineau", "Pigeon", "Chauve-souris"), ("Baleine", "Dauphin", "Orque", "Requin"), ("Mercure", "Vénus", "Mars", "Lune"),
    ("Cuivre", "Fer", "Or", "Bois"), ("Tulipe", "Rose", "Marguerite", "Chêne"), ("Football", "Rugby", "Tennis", "Échecs"),
    ("Triangle", "Carré", "Losange", "Cercle"), ("Nil", "Amazone", "Danube", "Everest"), ("Stylo", "Crayon", "Feutre", "Gomme"),
    ("Tomate", "Fraise", "Cerise", "Citron"), ("Crocodile", "Lézard", "Serpent", "Grenouille"), ("Piano", "Orgue", "Clavecin", "Flûte"),
    ("Neige", "Pluie", "Grêle", "Vent"), ("Mozart", "Beethoven", "Bach", "Picasso"), ("Euro", "Dollar", "Yen", "Kilo"),
    ("Épée", "Sabre", "Dague", "Bouclier"), ("Hiver", "Printemps", "Été", "Midi"), ("Saphir", "Rubis", "Émeraude", "Perle"),
    ("Vélo", "Moto", "Trottinette", "Voiture"), ("Poulet", "Canard", "Oie", "Lapin"), ("Carotte", "Radis", "Betterave", "Salade"),
    ("Lion", "Tigre", "Guépard", "Loup"), ("Chaise", "Tabouret", "Fauteuil", "Table"), ("Seine", "Loire", "Garonne", "Léman"),
]
ANALOG = [
    ("Main", "gant", "pied", "Chaussure", ["Chaussette", "Jambe", "Orteil"]), ("Oiseau", "nid", "abeille", "Ruche", ["Miel", "Fleur", "Essaim"]),
    ("Jour", "nuit", "blanc", "Noir", ["Gris", "Clair", "Lumière"]), ("Chaud", "froid", "haut", "Bas", ["Grand", "Sommet", "Ciel"]),
    ("Livre", "lire", "chanson", "Écouter", ["Chanter", "Musique", "Parole"]), ("Poisson", "nager", "oiseau", "Voler", ["Plume", "Chanter", "Nid"]),
    ("Peintre", "pinceau", "écrivain", "Stylo", ["Livre", "Roman", "Papier"]), ("Veau", "vache", "poulain", "Jument", ["Cheval", "Âne", "Ferme"]),
    ("Œil", "voir", "oreille", "Entendre", ["Bruit", "Tête", "Parler"]), ("France", "Paris", "Italie", "Rome", ["Milan", "Venise", "Europe"]),
    ("Médecin", "hôpital", "professeur", "École", ["Élève", "Cours", "Livre"]), ("Été", "chaleur", "hiver", "Froid", ["Neige", "Noël", "Janvier"]),
    ("Lait", "vache", "miel", "Abeille", ["Fleur", "Sucre", "Ruche"]), ("Bateau", "port", "avion", "Aéroport", ["Ciel", "Pilote", "Aile"]),
    ("Soleil", "jour", "lune", "Nuit", ["Étoile", "Ciel", "Soir"]), ("Grand", "petit", "rapide", "Lent", ["Vite", "Court", "Léger"]),
    ("Pomme", "pommier", "gland", "Chêne", ["Écureuil", "Noisette", "Forêt"]), ("Chien", "aboyer", "chat", "Miauler", ["Ronronner", "Griffer", "Souris"]),
    ("Heure", "minute", "minute", "Seconde", ["Jour", "Temps", "Horloge"]),
]

@cat("psycho")
def psycho():
    out = []
    def seq(terms, ans, wrong):
        q = "Quel nombre complète la suite : " + ", ".join(num(t) for t in terms) + ", ?"
        out.append(mcq("psycho", q, num(ans), [num(w) for w in wrong + near(ans) if w != ans]))
    for _ in range(25):
        a, d = rng.randint(1, 30), rng.randint(2, 13); t = [a + d * i for i in range(5)]; seq(t, a + 5 * d, [a + 5 * d + 1, a + 7 * d])
    for _ in range(20):
        a, r = rng.randint(1, 5), rng.choice([2, 3, 4]); t = [a * r ** i for i in range(5)]; seq(t, a * r ** 5, [a * r ** 5 - a, t[-1] + t[-2]])
    for _ in range(20):
        a, x, y = rng.randint(1, 10), rng.randint(2, 9), rng.randint(1, 5)
        t = [a]
        for i in range(6): t.append(t[-1] + x if i % 2 == 0 else t[-1] - y)
        seq(t[:6], t[6], [t[5] - y, t[6] + 1])
    for _ in range(15):
        a, d = rng.randint(1, 10), rng.randint(1, 4); t = [a]
        for i in range(5): t.append(t[-1] + d * (i + 1))
        seq(t[:5], t[5], [t[4] + d * 4, t[5] + 1])
    for _ in range(10):
        a, b = rng.randint(1, 5), rng.randint(1, 6); t = [a, b]
        while len(t) < 7: t.append(t[-1] + t[-2])
        seq(t[:6], t[6], [t[5] + 1, t[5] * 2])
    for _ in range(10):
        s = rng.randint(1, 6); t = [(s + i) ** 2 for i in range(5)]; seq(t, (s + 5) ** 2, [t[-1] + (t[-1] - t[-2]), (s + 5) ** 2 + 1])
    for _ in range(12):
        a, d = rng.randint(80, 200), rng.randint(3, 15); t = [a - d * i for i in range(5)]; seq(t, a - 5 * d, [a - 5 * d - 1, a - 6 * d])
    L = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
    for _ in range(20):
        st, d = rng.randint(0, 8), rng.randint(1, 3); t = [L[st + d * i] for i in range(4)]; ans = L[st + d * 4]
        wrong = [L[(st + d * 4 + k) % 26] for k in (1, -1, 2)]
        out.append(mcq("psycho", "Quelle lettre complète la suite : " + ", ".join(t) + ", ?", ans, wrong))
    for words in ODD:
        p = list(words); rng.shuffle(p)
        out.append({"c": "psycho", "q": "Quel est l'intrus : " + ", ".join(p) + " ?", "d": words[3], "p": p, "a": [words[3]]})
    for a, b, c, d, wrong in ANALOG:
        out.append(mcq("psycho", f"{a} est à {b} ce que {c} est à... ?", d, wrong))
    return out

# ---------------------------------------------------------------- Wikidata : personnalites
def qid(uri): return uri.rsplit("/", 1)[-1]
def year(date):
    m = re.match(r"(-?)(\d+)-", date or "")
    return (-int(m.group(2)) if m.group(1) else int(m.group(2))) if m else None
def lbl(s): return s if s and not re.fullmatch(r"Q\d+", s) else None

def people():
    top = sparql("people_top", """
SELECT ?p ?sl WHERE { ?p wikibase:sitelinks ?sl . hint:Prior hint:rangeSafe true . FILTER(?sl >= 120) ?p wdt:P31 wd:Q5 . }""")
    ppl, sls = {}, {qid(r["p"]): int(r["sl"]) for r in top}
    ids = list(sls)
    for i in range(0, len(ids), 200):
        chunk = " ".join("wd:" + k for k in ids[i:i + 200])
        for r in sparql(f"people_info_{i}", f"""
SELECT ?p ?pLabel ?img ?g ?b ?placeLabel ?countryLabel WHERE {{
  VALUES ?p {{ {chunk} }} ?p wdt:P18 ?img; wdt:P21 ?g; wdt:P569 ?b .
  OPTIONAL {{ ?p wdt:P19 ?place . OPTIONAL {{ ?place wdt:P17 ?country }} }}
  SERVICE wikibase:label {{ bd:serviceParam wikibase:language "fr,en". }} }}"""):
            k = qid(r["p"])
            if k in ppl or not lbl(r.get("pLabel")): continue
            ppl[k] = {"id": k, "name": r["pLabel"], "img": r["img"], "sl": sls[k], "g": qid(r["g"]), "b": year(r["b"]),
                      "place": lbl(r.get("placeLabel")), "country": lbl(r.get("countryLabel")), "occ": [], "work": []}
    ids = list(ppl)
    for i in range(0, len(ids), 300):
        chunk = " ".join("wd:" + k for k in ids[i:i + 300])
        for r in sparql(f"people_occ_{i}", f"""
SELECT ?p ?oLabel WHERE {{ VALUES ?p {{ {chunk} }} ?p wdt:P106 ?o . SERVICE wikibase:label {{ bd:serviceParam wikibase:language "fr". }} }}"""):
            o = lbl(r.get("oLabel"))
            if o and len(ppl[qid(r["p"])]["occ"]) < 3: ppl[qid(r["p"])]["occ"].append(o)
        for r in sparql(f"people_work_{i}", f"""
SELECT ?p ?wLabel WHERE {{ VALUES ?p {{ {chunk} }} ?p wdt:P800 ?w . SERVICE wikibase:label {{ bd:serviceParam wikibase:language "fr". }} }}"""):
            w = lbl(r.get("wLabel"))
            if w and len(ppl[qid(r["p"])]["work"]) < 2: ppl[qid(r["p"])]["work"].append(w)
    return [p for p in ppl.values() if p["occ"]]

def gender(occ, g):
    # Libelles Wikidata du genre "écrivain ou écrivaine" : on garde la forme qui correspond a la personne.
    parts = occ.split(" ou ")
    return parts[1] if len(parts) == 2 and g == "Q6581072" else parts[0]

def distract(p, pool, n=3):
    same = [x for x in pool if x is not p and x["g"] == p["g"] and set(x["occ"]) & set(p["occ"]) and x["name"] != p["name"]]
    other = [x for x in pool if x is not p and x["g"] == p["g"] and x["name"] != p["name"]]
    rng.shuffle(same); rng.shuffle(other)
    return [x["name"] for x in (same + other)][:n + 3]

def surname_answers(name):
    parts = name.split()
    return [name] + ([parts[-1]] if len(parts) >= 2 and len(parts[-1]) >= 4 else [])

@cat("tetes")
def tetes():
    pool = [p for p in people() if p["b"] and p["b"] >= 1900]
    pool.sort(key=lambda p: -p["sl"])
    out = []
    for p in pool[:260]:
        url = resolve(commons(p["img"], 640))
        if not url: continue
        it = mcq("tetes", "Qui se cache derrière ce flou ?", p["name"], distract(p, pool), u=url, k="blur",
                 h=", ".join(dict.fromkeys(gender(o, p["g"]) for o in p["occ"][:2])).capitalize(), cr="Photo : Wikimedia Commons")
        if it: it["a"] = surname_answers(p["name"]); out.append(it)
    return out

@cat("quiestce")
def quiestce():
    pool = people()
    pool.sort(key=lambda p: -p["sl"])
    out = []
    for p in pool[:320]:
        clues = []
        born = "Née" if p["g"] == "Q6581072" else "Né"
        if p["b"]:
            where = f" à {p['place']}" if p["place"] else ""
            if p["country"] and p["place"] and p["country"] != p["place"]: where += f" ({p['country']})"
            clues.append(f"{born} en {p['b'] if p['b'] > 0 else str(-p['b']) + ' av. J.-C.'}{where}")
        clues.append(", ".join(dict.fromkeys(gender(o, p["g"]) for o in p["occ"][:3])))
        bits = [w for w in p["name"].split() if len(w) >= 4]
        works = [w for w in p["work"] if not any(b.lower() in w.lower() for b in bits)]
        if works: clues.append(("connue" if p["g"] == "Q6581072" else "connu") + " pour : " + " ; ".join(f"« {w} »" for w in works))
        if len(clues) < 3: continue
        q = "Qui est-ce ?\n" + "\n".join("• " + c[0].upper() + c[1:] for c in clues)
        it = mcq("quiestce", q, p["name"], distract(p, pool), cr="Indices : Wikidata")
        if it: it["a"] = surname_answers(p["name"]); out.append(it)
    return out

# ---------------------------------------------------------------- Wikidata : jeux video, lieux, classements, logos, animaux
def top(name, cls, minsl, extra="", select="", lang="fr"):
    # Elements d'une classe (P31 direct) avec assez de liens de langues, plus les proprietes demandees.
    return sparql(name, f"""
SELECT ?x ?xLabel ?sl {select} WHERE {{
  ?x wikibase:sitelinks ?sl . hint:Prior hint:rangeSafe true . FILTER(?sl >= {minsl})
  ?x wdt:P31 {cls} . {extra}
  SERVICE wikibase:label {{ bd:serviceParam wikibase:language "{lang}". }} }}""")

@cat("jeuxvideo")
def jeuxvideo():
    rows = top("games", "wd:Q7889", 45, "OPTIONAL { ?x wdt:P178 ?dev } OPTIONAL { ?x wdt:P577 ?date }", "?devLabel ?date", "fr,en")
    games = {}
    for r in rows:
        k = qid(r["x"]); name = lbl(r.get("xLabel"))
        if not name: continue
        g = games.setdefault(k, {"name": name, "dev": set(), "year": None})
        if lbl(r.get("devLabel")): g["dev"].add(r["devLabel"])
        y = year(r.get("date"))
        if y and (g["year"] is None or y < g["year"]): g["year"] = y
    games = list(games.values())
    devs = sorted({d for g in games for d in g["dev"]})
    out = []
    for g in games:
        if len(g["dev"]) == 1:
            d = next(iter(g["dev"]))
            if d.lower() not in g["name"].lower():
                out.append(mcq("jeuxvideo", f"Quel studio a développé « {g['name']} » ?", d, rng.sample(devs, 8), cr="Wikidata"))
        if g["year"]:
            y = g["year"]; w = [y + k for k in rng.sample([-4, -3, -2, -1, 1, 2, 3, 4], 3)]
            out.append(mcq("jeuxvideo", f"En quelle année est sorti « {g['name']} » ?", str(y), [str(x) for x in w], cr="Wikidata"))
    # Captures du quiz d'images : "de quel jeu ?", image nette.
    pool = [q for q in json.load(open(os.path.join(ROOT, "..", "Assets", "Resources", "Quiz", "pool.json"), encoding="utf-8")) if q["c"] == "jeu_video"]
    names = [q["d"] for q in pool]
    for q in rng.sample(pool, min(120, len(pool))):
        it = mcq("jeuxvideo", "De quel jeu vidéo vient cette image ?", q["d"], rng.sample(names, 8), u=q["u"], k="img", cr=q.get("cr"))
        if it: it["a"] = q["a"]; out.append(it)
    return out

PLACE_TYPES = "wd:Q16970 wd:Q2977 wd:Q23413 wd:Q12518 wd:Q16560 wd:Q12280 wd:Q44539 wd:Q32815 wd:Q483110 wd:Q24354 wd:Q4989906 wd:Q57821 wd:Q12516 wd:Q11303 wd:Q1370598 wd:Q179700 wd:Q751876 wd:Q1081138 wd:Q15243209 wd:Q1030034 wd:Q839954 wd:Q2319498"

@cat("geodate")
def geodate():
    rows = sparql("places", f"""
SELECT ?x ?xLabel ?img ?coord ?date ?countryLabel WHERE {{
  ?x wikibase:sitelinks ?sl . hint:Prior hint:rangeSafe true . FILTER(?sl >= 45)
  VALUES ?t {{ {PLACE_TYPES} }} ?x wdt:P31 ?t; wdt:P18 ?img; wdt:P625 ?coord; wdt:P571 ?date .
  OPTIONAL {{ ?x wdt:P17 ?country }}
  SERVICE wikibase:label {{ bd:serviceParam wikibase:language "fr,en". }} }}""")
    out, seen = [], set()
    for r in rows:
        k = qid(r["x"]); name = lbl(r.get("xLabel")); y = year(r.get("date"))
        m = re.match(r"Point\(([-\d.]+) ([-\d.]+)\)", r["coord"])
        if k in seen or not name or y is None or not m or y > 2024 or y < -3000: continue
        seen.add(k)
        url = resolve(commons(r["img"], 800))
        if not url: continue
        where = f", {r['countryLabel']}" if lbl(r.get("countryLabel")) else ""
        when = f"{y}" if y > 0 else f"{-y} av. J.-C."
        out.append({"c": "geodate", "k": "geo", "q": "Où et quand ? Place ce lieu sur la carte et devine l'année de sa construction.",
                    "d": f"{name}{where} ({when})", "a": [name], "p": [], "u": url, "la": float(m.group(2)), "lo": float(m.group(1)), "y": y,
                    "cr": "Photo : Wikimedia Commons · Wikidata"})
    return out

def fmt(v, unit):
    if unit == "hab.":
        return f"{v / 1e9:.1f} milliard".replace(".", ",") if v >= 1e9 else f"{v / 1e6:.1f} millions".replace(".", ",") if v >= 1e6 else f"{int(v):,} hab.".replace(",", " ")
    if unit == "année": return str(int(v)) if v > 0 else f"{-int(v)} av. J.-C."
    return f"{int(round(v)):,} {unit}".replace(",", " ")

def ranking(q, items, unit, n, gap=1.15, years=False):
    out, items = [], [x for x in items if x[1]]
    for _ in range(n):
        for _try in range(50):
            pick = rng.sample(items, 4)
            vals = sorted(v for _, v in pick)
            ok = all((b - a >= 5) if years else (b >= a * gap) for a, b in zip(vals, vals[1:]))
            if ok and len({nm for nm, _ in pick}) == 4: break
        else: continue
        order = sorted(pick, key=lambda x: x[1] if years else -x[1])
        p = [nm for nm, _ in pick]
        out.append({"c": "classement", "k": "rank", "q": q, "p": p, "d": " → ".join(nm for nm, _ in order), "a": [],
                    "h": " · ".join(f"{nm} : {fmt(v, unit)}" for nm, v in order), "cr": "Wikidata"})
    return out

def values(name, cls, prop, minsl, agg="MAX"):
    rows = sparql(name, f"""
SELECT ?x ?xLabel (MAX(?v) AS ?val) WHERE {{
  ?x wikibase:sitelinks ?sl . hint:Prior hint:rangeSafe true . FILTER(?sl >= {minsl})
  ?x wdt:P31 {cls}; wdt:{prop} ?v .
  SERVICE wikibase:label {{ bd:serviceParam wikibase:language "fr". }} }} GROUP BY ?x ?xLabel""")
    return [(r["xLabel"], float(r["val"])) for r in rows if lbl(r.get("xLabel")) and float(r["val"]) > 0]

@cat("classement")
def classement():
    out = []
    out += ranking("Classe ces pays du plus peuplé au moins peuplé.", values("pop", "wd:Q6256", "P1082", 100), "hab.", 40)
    out += ranking("Classe ces pays du plus grand au plus petit (superficie).", values("area", "wd:Q6256", "P2046", 100), "km²", 35)
    out += ranking("Classe ces montagnes de la plus haute à la moins haute.", values("mount", "wd:Q8502", "P2044", 40), "m", 25)
    out += ranking("Classe ces fleuves du plus long au moins long.", values("river", "wd:Q4022", "P2043", 50), "km", 20)
    out += ranking("Classe ces villes de la plus peuplée à la moins peuplée.", values("city", "wd:Q515", "P1082", 90), "hab.", 30)
    ppl = [(p["name"], p["b"]) for p in people() if p["b"]]
    out += ranking("Classe ces personnalités de la plus ancienne à la plus récente (année de naissance).", ppl, "année", 40, years=True)
    films = [(r["xLabel"], float(year(r["date"]))) for r in top("films", "wd:Q11424", 90, "?x wdt:P577 ?date .", "?date") if lbl(r.get("xLabel")) and year(r.get("date"))]
    out += ranking("Classe ces films du plus ancien au plus récent.", list(dict(films).items()), "année", 30, years=True)
    planets = [("Jupiter", 139820), ("Saturne", 116460), ("Uranus", 50724), ("Neptune", 49244), ("Terre", 12742), ("Vénus", 12104), ("Mars", 6779), ("Mercure", 4879)]
    out += ranking("Classe ces planètes de la plus grande à la plus petite (diamètre).", planets, "km", 10, gap=1.02)
    return out

@cat("logos")
def logos():
    rows = sparql("logos2", """
SELECT ?x ?xLabel ?logo ?indLabel ?sl WHERE {
  ?x wikibase:sitelinks ?sl . hint:Prior hint:rangeSafe true . FILTER(?sl >= 45)
  ?x wdt:P154 ?logo; wdt:P452 ?ind .
  SERVICE wikibase:label { bd:serviceParam wikibase:language "fr,en". } }""")
    brands = {}
    for r in rows:
        k = qid(r["x"]); name = lbl(r.get("xLabel"))
        if not name: continue
        b = brands.setdefault(k, {"name": name, "logo": r["logo"], "ind": set(), "sl": int(r["sl"])})
        if lbl(r.get("indLabel")): b["ind"].add(r["indLabel"])
    brands = sorted(brands.values(), key=lambda b: -b["sl"])[:260]
    out = []
    for b in brands:
        same = [x["name"] for x in brands if x is not b and x["ind"] & b["ind"]]
        rng.shuffle(same)
        url = resolve(commons(b["logo"], 640))
        if not url: continue
        it = mcq("logos", "À quelle marque appartient ce logo ?", b["name"], same + [x["name"] for x in rng.sample(brands, 6)], u=url, k="pix",
                 cr="Logo : Wikimedia Commons")
        if it: out.append(it)
    return out

@cat("animal")
def animal():
    rows = sparql("animals2", """
SELECT ?x ?xLabel ?audio ?img ?taxon ?sl WHERE {
  ?x wikibase:sitelinks ?sl . hint:Prior hint:rangeSafe true . FILTER(?sl >= 40)
  ?x wdt:P51 ?audio; wdt:P225 ?taxon . OPTIONAL { ?x wdt:P18 ?img }
  SERVICE wikibase:label { bd:serviceParam wikibase:language "fr". } }""")
    ani = {}
    for r in rows:
        k = qid(r["x"]); name = lbl(r.get("xLabel"))
        if not name or name == r.get("taxon") or k in ani: continue
        if not re.search(r"\.(ogg|mp3|wav)$", r["audio"], re.I): continue
        ani[k] = {"name": name[0].upper() + name[1:], "audio": r["audio"], "img": r.get("img"), "sl": int(r["sl"])}
    ani = sorted(ani.values(), key=lambda a: -a["sl"])[:220]
    out = []
    for a in ani:
        s = resolve(commons(a["audio"]).split("?")[0])
        if not s: continue
        img = resolve(commons(a["img"], 640)) if a["img"] else None
        it = mcq("animal", "Quel est cet animal ? Écoute bien...", a["name"], [x["name"] for x in rng.sample(ani, 8)], s=s, u=img, k="audio",
                 cr="Son et photo : Wikimedia Commons")
        if it: out.append(it)
    return out

# ---------------------------------------------------------------- Code de la route (panneaux francais, domaine public)
SIGNS = {
    "A1a": "Virage à droite", "A1b": "Virage à gauche", "A1c": "Succession de virages, le premier à droite", "A1d": "Succession de virages, le premier à gauche",
    "A2a": "Cassis", "A2b": "Dos-d'âne", "A3": "Chaussée rétrécie", "A3a": "Chaussée rétrécie par la droite", "A3b": "Chaussée rétrécie par la gauche",
    "A4": "Chaussée particulièrement glissante", "A6": "Pont mobile", "A7": "Passage à niveau avec barrières", "A8": "Passage à niveau sans barrière",
    "A9": "Traversée de voies de tramway", "A13a": "Endroit fréquenté par les enfants", "A13b": "Passage pour piétons", "A14": "Autres dangers",
    "A15a1": "Passage d'animaux domestiques", "A15b": "Passage d'animaux sauvages", "A15c": "Passage de cavaliers", "A16": "Descente dangereuse",
    "A17": "Circulation dans les deux sens", "A18": "Risque de chute de pierres", "A19": "Débouché sur un quai ou une berge", "A20": "Feux tricolores",
    "A21": "Débouché de cyclistes", "A24": "Vent latéral", "AB1": "Intersection avec priorité à droite", "AB2": "Intersection avec une route non prioritaire",
    "AB3a": "Cédez le passage", "AB4": "Stop", "AB6": "Route prioritaire", "AB7": "Fin de route prioritaire", "AB25": "Carrefour à sens giratoire",
    "B0": "Circulation interdite à tout véhicule", "B1": "Sens interdit", "B2a": "Interdiction de tourner à gauche", "B2b": "Interdiction de tourner à droite",
    "B2c": "Demi-tour interdit", "B3": "Interdiction de dépasser", "B6a1": "Stationnement interdit", "B6d": "Arrêt et stationnement interdits",
    "B7b": "Accès interdit aux véhicules à moteur", "B9a": "Accès interdit aux piétons", "B9b": "Accès interdit aux cycles", "B15": "Cédez le passage à la circulation venant en sens inverse",
    "B21-1": "Obligation de tourner à droite", "B21-2": "Obligation de tourner à gauche", "B21a1": "Contournement obligatoire par la droite",
    "B22a": "Piste ou bande obligatoire pour les cycles", "B31": "Fin de toutes les interdictions", "C1a": "Parking (lieu aménagé pour le stationnement)",
    "C12": "Circulation à sens unique", "C13a": "Impasse", "C18": "Priorité par rapport à la circulation venant en sens inverse",
    "C20a": "Passage pour piétons", "C107": "Route à accès réglementé", "C207": "Section d'autoroute",
}
ROAD_RULES = [
    ("Quelle est la vitesse maximale sur autoroute par temps sec ?", "130 km/h", ["110 km/h", "120 km/h", "150 km/h"]),
    ("Quelle est la vitesse maximale sur autoroute par temps de pluie ?", "110 km/h", ["100 km/h", "90 km/h", "130 km/h"]),
    ("Quelle est la vitesse maximale en agglomération (sauf indication) ?", "50 km/h", ["30 km/h", "60 km/h", "70 km/h"]),
    ("Quelle est la vitesse maximale sur une route à double sens sans séparateur central ?", "80 km/h", ["70 km/h", "90 km/h", "110 km/h"]),
    ("Par temps de brouillard (visibilité < 50 m), la vitesse est limitée à...", "50 km/h", ["30 km/h", "70 km/h", "80 km/h"]),
    ("Quel est le taux d'alcool maximal autorisé pour un conducteur confirmé ?", "0,5 g/l de sang", ["0,2 g/l de sang", "0,8 g/l de sang", "1 g/l de sang"]),
    ("Quel est le taux d'alcool maximal autorisé pour un jeune conducteur ?", "0,2 g/l de sang", ["0 g/l de sang", "0,5 g/l de sang", "0,3 g/l de sang"]),
    ("Combien de points compte un permis probatoire au départ ?", "6 points", ["12 points", "8 points", "3 points"]),
    ("Combien de points compte un permis définitif ?", "12 points", ["10 points", "15 points", "6 points"]),
    ("Combien de temps dure la période probatoire après un permis classique ?", "3 ans", ["2 ans", "1 an", "5 ans"]),
    ("Dans un rond-point signalé, qui a la priorité ?", "Les véhicules déjà engagés dans l'anneau", ["Ceux qui arrivent à droite", "Les poids lourds", "Ceux qui entrent"]),
    ("Quel feu faut-il allumer la nuit en ville, sur une rue éclairée ?", "Les feux de croisement", ["Les feux de route", "Les feux de brouillard", "Aucun"]),
    ("Quelle est la distance de sécurité conseillée à 90 km/h ?", "2 secondes", ["1 seconde", "5 secondes", "10 mètres"]),
    ("De quelle couleur est la ligne qu'on ne doit pas franchir ?", "Continue", ["Discontinue", "Pointillée", "Jaune"]),
    ("Que signifie un feu orange fixe ?", "S'arrêter, sauf si c'est dangereux", ["Accélérer", "Passer prudemment", "Priorité à droite"]),
    ("À partir de quel âge peut-on passer le permis B en France ?", "17 ans", ["16 ans", "18 ans", "15 ans"]),
    ("Que doit porter tout conducteur sortant d'un véhicule en panne sur l'autoroute ?", "Un gilet jaune", ["Un casque", "Des gants", "Un triangle"]),
    ("Où place-t-on le triangle de présignalisation ?", "À au moins 30 m du véhicule", ["Sur le toit", "Juste derrière le véhicule", "À 5 m devant"]),
    ("Téléphoner avec le téléphone tenu en main en conduisant coûte...", "3 points", ["1 point", "6 points", "Rien"]),
    ("Quel est le numéro d'appel d'urgence européen ?", "112", ["15", "18", "911"]),
]

@cat("route")
def route():
    out = []
    ok = {}
    for code, meaning in SIGNS.items():
        url = resolve(commons(f"France road sign {code}.svg", 512))
        if url: ok[code] = (meaning, url)
    for code, (meaning, url) in ok.items():
        same = [m for c, (m, _) in ok.items() if c != code and c[0] == code[0]]
        other = [m for c, (m, _) in ok.items() if c != code]
        rng.shuffle(same); rng.shuffle(other)
        out.append(mcq("route", "Que signifie ce panneau ?", meaning, same + other, u=url, k="img", cr="Panneau : Wikimedia Commons (domaine public)"))
    for q, d, w in ROAD_RULES:
        out.append(mcq("route", q, d, w))
    return out

# ---------------------------------------------------------------- 4 images 1 mot (images d'en-tete de Wikipedia en francais)
FOUR = {
    "Froid": ["Neige", "Glaçon", "Manchot empereur", "Iceberg"], "Chaud": ["Soleil", "Désert", "Feu", "Volcan"],
    "Lait": ["Vache", "Biberon", "Fromage", "Yaourt"], "Rouge": ["Fraise", "Coquelicot", "Tomate", "Rubis"],
    "Roi": [["Couronne (coiffe)", "Couronne royale", "Couronne de saint Édouard"], "Trône", "Roi (échecs)", "Louis XIV"], "Plage": ["Sable", "Parasol", "Château de sable", ["Coquillage", "Coquille Saint-Jacques"]],
    "Noël": ["Sapin de Noël", "Père Noël", "Bûche de Noël", "Crèche de Noël"], "Musique": ["Piano", "Guitare", "Partition de musique", "Violon"],
    "Espace": [["Fusée", "Saturn V"], "Astronaute", "Lune", "Satellite artificiel"], "École": ["Tableau noir", "Cartable", "Crayon", "Craie"],
    "Pluie": ["Parapluie", "Nuage", "Arc-en-ciel", "Imperméable"], "Abeille": ["Miel", "Ruche", "Pollen", "Apiculture"],
    "Pirate": ["Galion", "Perroquet", "Jolly Roger", "Trésor"], "Cirque": ["Clown", ["Chapiteau", "Chapiteau (cirque)", "Tente de cirque"], "Jonglerie", "Acrobatie"],
    "Café": ["Tasse", "Café", "Cafetière", "Expresso"], "Pain": [["Baguette (pain)", "Baguette de pain", "Baguette"], "Boulangerie", "Blé", "Four à pain"],
    "Montagne": ["Ski", "Chamois", "Mont Blanc", "Téléphérique"], "Ferme": ["Tracteur", "Poule", "Grange", "Cochon domestique"],
    "Égypte": ["Pyramides de Gizeh", "Grand Sphinx de Gizeh", "Pharaon", "Momie"], "Japon": ["Sushi", "Mont Fuji", "Kimono", "Samouraï"],
    "Italie": ["Pizza", "Colisée", "Tour de Pise", "Gondole"], "Paris": ["Tour Eiffel", "Arc de triomphe de l'Étoile", "Cathédrale Notre-Dame de Paris", "Musée du Louvre"],
    "Jardin": ["Arrosoir", "Brouette", "Sécateur", "Tondeuse à gazon"], "Hôpital": ["Ambulance", "Stéthoscope", "Seringue", "Infirmier"],
    "Cinéma": ["Pop-corn", "Caméra", ["Clap (cinéma)", "Clap", "Claquette (cinéma)"], "Salle de cinéma"], "Football": ["Ballon de football", "Stade", ["Gardien de but", "Gardien de but (football)", "But (football)"], "Carton rouge"],
    "Vampire": ["Chauve-souris", "Ail cultivé", "Cercueil", "Dracula"], "Halloween": ["Citrouille", "Sorcière", "Fantôme", "Toile d'araignée"],
    "Mer": ["Vague", "Voilier", "Phare", "Crabe"], "Temps": ["Horloge", "Sablier", ["Montre", "Montre (horlogerie)", "Montre-bracelet"], ["Calendrier", "Calendrier grégorien", "Éphéméride (calendrier)"]],
    "Énergie": ["Éolienne", "Panneau solaire", "Barrage", "Centrale nucléaire"], "Hiver": ["Bonhomme de neige", ["Luge", "Luge (sport)", "Traîneau"], ["Écharpe", "Écharpe (vêtement)", "Cache-nez"], "Flocon de neige"],
    "Pique-nique": ["Panier", "Fourmi", "Sandwich", "Pelouse"], "Cuisine": ["Casserole", "Fouet (cuisine)", "Four", "Rouleau à pâtisserie"],
    "Chevalier": [["Armure", "Armure (équipement)", "Armure de plaques"], "Épée", "Château fort", ["Bouclier", "Bouclier (arme)", "Écu (bouclier)"]], "Détective": ["Loupe", "Empreinte digitale", "Sherlock Holmes", "Chapeau melon"],
}

def wiki_image(title):
    def fetch():
        r = S.get("https://fr.wikipedia.org/w/api.php", params={"action": "query", "prop": "pageimages", "titles": title, "pithumbsize": 500,
                                                                 "redirects": 1, "format": "json"}, timeout=30).json()
        for p in r.get("query", {}).get("pages", {}).values():
            if "thumbnail" in p: return p["thumbnail"]["source"]
        return None
    return cached("wimg_" + hashlib.sha1(title.encode()).hexdigest() + ".json", fetch, 0.3)

@cat("4images")
def four_images():
    out = []
    words = list(FOUR)
    for w, titles in FOUR.items():
        imgs = [next((u for u in (wiki_image(x) for x in (t if isinstance(t, list) else [t])) if u), None) for t in titles]
        if any(i is None for i in imgs):
            print("  4 images : image manquante pour", w, [t for t, i in zip(titles, imgs) if i is None]); continue
        out.append(mcq("4images", "Quel mot relie ces 4 images ?", w, rng.sample(words, 6), i=imgs, k="4img",
                       h="Images : " + ", ".join(t[0] if isinstance(t, list) else t for t in titles), cr="Images : Wikipédia / Wikimedia Commons"))
    return out

# ---------------------------------------------------------------- Rebus (emoji Noto, Apache 2.0) : un morceau = emoji ou texte
REBUS = [
    ("Chaton", ["🐱", "🐟"], "chat + thon"), ("Poisson", ["🫛", "🔊"], "pois + son"), ("Château", ["🐱", "💧"], "chat + eau"),
    ("Pinceau", ["🍞", "🪣"], "pain + seau"), ("Poireau", ["🍐", "💧"], "poire + eau"), ("Rideau", ["🍚", "🎲", "💧"], "riz + dé + eau"),
    ("Radeau", ["🐀", "🎲", "💧"], "rat + dé + eau"), ("Chalet", ["🐱", "🥛"], "chat + lait"), ("Coussin", ["🦒", "🦢"], "cou + cygne"),
    ("Oiseau", ["🪿", "🪣"], "oie + seau"), ("Lapin", ["LA", "🍞"], "la + pain"), ("Pinson", ["🍞", "🔊"], "pain + son"),
    ("Maison", ["🌽", "🔊"], "maïs + son"), ("Chaussette", ["🔥", "7"], "chaud + sept"), ("Chandail", ["🎤", "🧄"], "chant + ail"),
    ("Chamois", ["🐱", "📅"], "chat + mois"), ("Jambon", ["🦵", "👍"], "jambe + bon"), ("Paris", ["👣", "🍚"], "pas + riz"),
    ("Pompier", ["🍎", "🦶"], "pomme + pied"), ("Chapitre", ["🐱", "🤡"], "chat + pitre"), ("Morceau", ["💀", "🪣"], "mort + seau"),
    ("Radis", ["🐀", "🔟"], "rat + dix"), ("Chaudron", ["🔥", "⭕"], "chaud + rond"), ("Verrou", ["🍷", "🛞"], "verre + roue"),
    ("Tonneau", ["🐟", "💧"], "thon + eau"), ("Bouleau", ["🎱", "💧"], "boule + eau"), ("Voleur", ["✈️", "⏰"], "vol + heure"),
    ("Bonheur", ["👍", "⏰"], "bon + heure"), ("Pâté", ["👣", "🍵"], "pas + thé"), ("Cerf-volant", ["🦌", "✈️", "📅"], "cerf + vol + an"),
    ("Parapluie", ["👣", "🐀", "🌧️"], "pas + rat + pluie"), ("Chapeau", ["🐱", "🍯"], "chat + pot"), ("Lionceau", ["🦁", "🪣"], "lion + seau"), ("Marteau", ["MAR", "🍵", "💧"], "mar + thé + eau"), ("Serpent", ["🦌", "PAN"], "cerf + pan"), ("Chameau", ["🐱", "MO"], "chat + mot"),
]

def noto(e):
    # Noto Emoji servi par Google Fonts ; certaines sequences gardent le selecteur FE0F.
    for keep in (False, True):
        code = "_".join(f"{ord(c):x}" for c in e if keep or ord(c) != 0xFE0F)
        u = f"https://fonts.gstatic.com/s/e/notoemoji/latest/{code}/512.png"
        if head_ok(u): return u
    return None

def head_ok(url):
    key = hashlib.sha1(url.encode()).hexdigest()
    return cached("ok_" + key + ".json", lambda: S.head(url, timeout=30).status_code == 200, 0.1)

@cat("rebus")
def rebus():
    out = []
    words = [w for w, _, _ in REBUS]
    for w, parts, how in REBUS:
        if how.strip().endswith("..."): continue
        imgs = []
        for p in parts:
            if all(ord(ch) < 128 for ch in p): imgs.append("txt:" + p)   # morceau ecrit (lettres)
            else:
                u = noto(p)
                if not u: print("  rebus : emoji introuvable", w, p); imgs = None; break
                imgs.append(u)
        if not imgs: continue
        it = mcq("rebus", "Déchiffre ce rébus !", w, rng.sample(words, 6), i=imgs, k="rebus", h=how, cr="Emoji : Noto Emoji (Apache 2.0)")
        if it: out.append(it)
    return out

if __name__ == "__main__":
    only = sys.argv[1:]
    pool = json.load(open(OUT, encoding="utf-8")) if os.path.exists(OUT) and only else []
    pool = [q for q in pool if q["c"] not in only]
    for key, f in CATS.items():
        if only and key not in only: continue
        items = [q for q in f() if q]
        print(f"{key:12s} {len(items)}")
        pool += items
    order = list(CATS)
    pool.sort(key=lambda q: order.index(q["c"]) if q["c"] in order else 99)
    json.dump(pool, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=0)
    counts = {}
    for q in pool: counts[q["c"]] = counts.get(q["c"], 0) + 1
    print("TOTAL", len(pool), counts)
