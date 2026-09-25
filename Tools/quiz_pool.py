# Banque de questions du Quiz : appelle les API une fois, sur le PC du developpeur, et ecrit
# Assets/Resources/Quiz/pool.json (le jeu ne contient que les URL des images et les reponses).
#   python Tools/quiz_pool.py            -> toutes les categories dont la cle est renseignee
#   python Tools/quiz_pool.py drapeau    -> une seule categorie (les autres sont gardees)
# Cles : Tools/quiz_keys.json (hors Git). Reponses des API mises en cache dans Tools/quiz_cache/.
import hashlib, json, os, random, re, sys, time, unicodedata
import requests

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(ROOT, "..", "Assets", "Resources", "Quiz", "pool.json")
CACHE = os.path.join(ROOT, "quiz_cache")
KEYS = json.load(open(os.path.join(ROOT, "quiz_keys.json"), encoding="utf-8"))
os.makedirs(CACHE, exist_ok=True)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
rng = random.Random(42)
S = requests.Session()
S.headers["User-Agent"] = "PiqueNiqueGames-Quiz/1.0 (belcourtanastasia@gmail.com)"

def get(url, params=None, headers=None, method="GET", data=None, pause=0.0, cache=True):
    key = hashlib.sha1(json.dumps([method, url, params, data], sort_keys=True).encode()).hexdigest()
    path = os.path.join(CACHE, key + ".json")
    if cache and os.path.exists(path):
        return json.load(open(path, encoding="utf-8"))
    for attempt in range(5):
        r = S.request(method, url, params=params, headers=headers, data=data, timeout=30)
        if r.status_code == 429:
            time.sleep(float(r.headers.get("Retry-After", 2 + attempt * 2))); continue
        if r.status_code == 404:
            return None
        r.raise_for_status()
        out = r.json()
        if cache:
            json.dump(out, open(path, "w", encoding="utf-8"))
        time.sleep(pause)
        return out
    raise RuntimeError("trop de 429 : " + url)

# --- Reponses ---------------------------------------------------------------------------
STOP = {"the", "a", "an", "of", "le", "la", "les", "de", "du", "des", "l", "un", "une", "and", "et", "d", "in", "to", "on"}

def acronyms(title):
    words = [w for w in re.split(r"[\s:,_\-–—.!?/]+", title) if w]
    out = []
    sig = [w for w in words if w.lower().strip("'’") not in STOP]
    if len(words) >= 3: out.append("".join(w[0] for w in words).upper())
    if len(sig) >= 3: out.append("".join(w[0] for w in sig).upper())
    return [a for a in out if len(a) >= 3 and a.isalnum()]

def clean(s):
    return re.sub(r"\s+", " ", (s or "").strip())

def answers(*names, acro=True):
    out = []
    for n in names:
        n = clean(n)
        if not n or n in out: continue
        out.append(n)
        # "Titre : sous-titre" -> accepte aussi "Titre" seul si assez long
        head = re.split(r"\s*[:–—]\s*|\s+-\s+", n)[0]
        if head != n and len(head) >= 4 and head not in out: out.append(head)
    if acro:
        for n in list(out):
            for a in acronyms(n):
                if a not in out: out.append(a)
    return out

def has_latin(s):
    return any("a" <= c.lower() <= "z" for c in unicodedata.normalize("NFD", s or ""))

pool = []
if os.path.exists(OUT):
    pool = json.load(open(OUT, encoding="utf-8"))
only = sys.argv[1:] or None

def category(name, need=None):
    if only and name not in only: return False
    if need and not all(KEYS.get(k) for k in need):
        print(f"{name} : cle manquante ({', '.join(need)}), categorie ignoree"); return False
    global pool
    pool = [q for q in pool if q["c"] != name]
    return True

def add(c, url, ans, display, src, credit=None, hint=None):
    if not url or not ans: return
    q = {"c": c, "u": url, "a": ans, "d": display, "id": src}
    if credit: q["cr"] = credit
    if hint: q["h"] = hint
    pool.append(q)

# --- Drapeaux (flagcdn, sans cle : noms FR et EN + images ; REST Countries v3 a ete retire) --------
if category("drapeau"):
    fr = get("https://flagcdn.com/fr/codes.json")
    en = get("https://flagcdn.com/en/codes.json")
    for code, name in fr.items():
        if "-" in code or code in ("eu", "un"): continue   # etats americains, UE, ONU
        add("drapeau", f"https://flagcdn.com/w640/{code}.png", answers(name, en.get(code, ""), acro=False), name, "fc:" + code)
    print("drapeau :", sum(q["c"] == "drapeau" for q in pool))

# --- TMDB : films, series, anime, personnalites -----------------------------------------
TMDB = "https://api.themoviedb.org/3"
def tmdb(path, **params):
    params["api_key"] = KEYS["tmdb_api_key"]
    return get(TMDB + path, params, pause=0.03)
IMG = "https://image.tmdb.org/t/p/w1280"

def alt_titles(kind, id_):
    d = tmdb(f"/{kind}/{id_}/alternative_titles") or {}
    rows = d.get("titles") or d.get("results") or []
    return [r["title"] for r in rows if r.get("iso_3166_1") in ("FR", "US", "GB", "CA", "BE") and has_latin(r["title"])]

def textless_backdrops(kind, id_):
    d = tmdb(f"/{kind}/{id_}/images", include_image_language="null") or {}
    return [b["file_path"] for b in d.get("backdrops", []) if b.get("vote_count", 0) >= 0]

if category("film", ["tmdb_api_key"]):
    seen = set()
    for endpoint in ("/movie/popular", "/movie/top_rated"):
        for page in range(1, 13):
            for m in tmdb(endpoint, language="fr-FR", page=page)["results"]:
                if m["id"] in seen or m.get("adult"): continue
                seen.add(m["id"])
                bd = textless_backdrops("movie", m["id"])
                if not bd: continue
                ans = answers(m["title"], m["original_title"] if has_latin(m["original_title"]) else "", *alt_titles("movie", m["id"]))
                add("film", IMG + rng.choice(bd[:6]), ans, m["title"], f"tmdb:m{m['id']}")
    print("film :", sum(q["c"] == "film" for q in pool))

def tv_still(id_):
    show = tmdb(f"/tv/{id_}", language="fr-FR") or {}
    seasons = [s["season_number"] for s in show.get("seasons", []) if s["season_number"] > 0 and s.get("episode_count", 0) > 0]
    if seasons:
        s = rng.choice(seasons)
        eps = (tmdb(f"/tv/{id_}/season/{s}", language="fr-FR") or {}).get("episodes", [])
        rng.shuffle(eps)
        for e in eps[:3]:
            st = (tmdb(f"/tv/{id_}/season/{s}/episode/{e['episode_number']}/images") or {}).get("stills", [])
            if st: return rng.choice(st)["file_path"]
    bd = textless_backdrops("tv", id_)
    return rng.choice(bd[:6]) if bd else None

def tv_category(name, pages, **discover):
    seen = set()
    for page in range(1, pages + 1):
        rows = tmdb("/discover/tv", language="fr-FR", page=page, sort_by="popularity.desc", **discover)["results"]
        for t in rows:
            if t["id"] in seen: continue
            seen.add(t["id"])
            path = tv_still(t["id"])
            if not path: continue
            extra = alt_titles("tv", t["id"])
            if name == "anime": extra += jikan_titles(t["original_name"], t["name"])
            ans = answers(t["name"], t["original_name"] if has_latin(t["original_name"]) else "", *extra)
            add(name, IMG + path, ans, t["name"], f"tmdb:t{t['id']}")
    print(name, ":", sum(q["c"] == name for q in pool))

def jikan_titles(original, fr):
    for q in (original, fr):
        if not q: continue
        d = get("https://api.jikan.moe/v4/anime", {"q": q, "limit": 1}, pause=0.45)
        rows = (d or {}).get("data") or []
        if rows:
            a = rows[0]
            return [t for t in [a.get("title"), a.get("title_english")] + a.get("title_synonyms", []) if t and has_latin(t)]
    return []

if category("serie", ["tmdb_api_key"]):
    tv_category("serie", 12, without_genres="16", with_original_language="en|fr", vote_count_gte=300)
if category("anime", ["tmdb_api_key"]):
    tv_category("anime", 9, with_genres="16", with_origin_country="JP", vote_count_gte=50)

if category("personnalite", ["tmdb_api_key"]):
    for page in range(1, 16):
        for p in tmdb("/person/popular", language="fr-FR", page=page)["results"]:
            if not p.get("profile_path") or p.get("adult") or not has_latin(p["name"]): continue
            known = [k.get("title") or k.get("name") for k in p.get("known_for", []) if k.get("original_language") in ("en", "fr")]
            if len(known) < 2: continue   # personnalites vraiment connues ici
            parts = p["name"].split()
            ans = answers(p["name"], parts[-1] if len(parts) > 1 and len(parts[-1]) >= 4 else "", acro=False)
            hint = {"Acting": "Acteur ou actrice", "Directing": "Réalisateur ou réalisatrice"}.get(p.get("known_for_department"), "")
            add("personnalite", "https://image.tmdb.org/t/p/w780" + p["profile_path"], ans, p["name"], f"tmdb:p{p['id']}",
                hint=(hint + " — " if hint else "") + ", ".join(k for k in known[:2] if k))
    print("personnalite :", sum(q["c"] == "personnalite" for q in pool))

# --- IGDB : jeux video --------------------------------------------------------------------
if category("jeu_video", ["twitch_client_id", "twitch_client_secret"]):
    tok = S.post("https://id.twitch.tv/oauth2/token", params={"client_id": KEYS["twitch_client_id"],
                 "client_secret": KEYS["twitch_client_secret"], "grant_type": "client_credentials"}, timeout=30).json()["access_token"]
    H = {"Client-ID": KEYS["twitch_client_id"], "Authorization": "Bearer " + tok}
    for offset in range(0, 400, 100):
        body = ("fields name, screenshots.image_id, alternative_names.name, alternative_names.comment, total_rating_count;"
                " where screenshots != null & total_rating_count > 150 & category = 0; sort total_rating_count desc;"
                f" limit 100; offset {offset};")
        for g in get("https://api.igdb.com/v4/games", headers=H, method="POST", data=body, pause=0.3):
            shots = [s["image_id"] for s in g.get("screenshots", [])]
            alts = [a["name"] for a in g.get("alternative_names", []) if has_latin(a["name"]) and len(a["name"]) <= 60]
            add("jeu_video", f"https://images.igdb.com/igdb/image/upload/t_1080p/{rng.choice(shots)}.jpg",
                answers(g["name"], *alts), g["name"], f"igdb:{g['id']}")
    print("jeu_video :", sum(q["c"] == "jeu_video" for q in pool))

# --- Spotify : pochettes d'album -----------------------------------------------------------
ARTISTS = """Michael Jackson; Daft Punk; Stromae; Angèle; Aya Nakamura; PNL; Nekfeu; Orelsan; Jul; Ninho; Booba; Damso; SCH; Gims;
Indochine; Téléphone; Johnny Hallyday; Céline Dion; Mylène Farmer; Christine and the Queens; Zaz; Louane; Vianney; Soprano; IAM; NTM; MC Solaar;
The Beatles; Queen; Pink Floyd; Nirvana; Metallica; AC/DC; Led Zeppelin; The Rolling Stones; David Bowie; Prince; Madonna; ABBA; Fleetwood Mac;
Radiohead; Coldplay; Muse; Arctic Monkeys; Gorillaz; Linkin Park; Green Day; Red Hot Chili Peppers; Foo Fighters; Imagine Dragons;
Beyoncé; Rihanna; Taylor Swift; Adele; Lady Gaga; Billie Eilish; Ariana Grande; Dua Lipa; Katy Perry; Bruno Mars; Ed Sheeran; Harry Styles;
The Weeknd; Drake; Kanye West; Kendrick Lamar; Eminem; Jay-Z; Travis Scott; Frank Ocean; Tyler, The Creator; Post Malone; SZA; Doja Cat;
Olivia Rodrigo; Lana Del Rey; Amy Winehouse; Whitney Houston; Bob Marley; Elton John; Stevie Wonder; Marvin Gaye; Nina Simone; Dr. Dre;
Snoop Dogg; 2Pac; The Notorious B.I.G.; Outkast; Justin Timberlake; Britney Spears; Christina Aguilera; Shakira; Bad Bunny; Rosalía;
Justice; Air; Phoenix; M83; David Guetta; Avicii; Calvin Harris; Kavinsky; Sia; Lorde; Tame Impala; The Strokes; Oasis; Blur; U2""".replace("\n", " ")
if category("pochette_album", ["spotify_client_id", "spotify_client_secret"]):
    tok = S.post("https://accounts.spotify.com/api/token", data={"grant_type": "client_credentials"},
                 auth=(KEYS["spotify_client_id"], KEYS["spotify_client_secret"]), timeout=30).json()["access_token"]
    H = {"Authorization": "Bearer " + tok}
    for artist in [a.strip() for a in ARTISTS.split(";") if a.strip()]:
        d = get("https://api.spotify.com/v1/search", {"q": f'artist:"{artist}"', "type": "album", "market": "FR", "limit": 10}, headers=H, pause=0.1)
        albums = [a for a in (d or {}).get("albums", {}).get("items", []) if a["album_type"] == "album" and a.get("images")
                  and any(x["name"].lower() == artist.lower() for x in a["artists"])]
        names = set()
        for a in albums:
            title = re.sub(r"\s*[\(\[].*?(deluxe|remaster|edition|version|anniversary).*?[\)\]]", "", a["name"], flags=re.I).strip()
            if title.lower() in names: continue
            names.add(title.lower())
            add("pochette_album", a["images"][0]["url"], answers(title, artist), f"{artist} — {title}", "sp:" + a["id"])
            if len(names) >= 3: break
    print("pochette_album :", sum(q["c"] == "pochette_album" for q in pool))

# --- Unsplash : photos variees (50 requetes / heure en mode demo : relancer pour completer) ----
THEMES = [("eiffel tower", "Tour Eiffel", "Eiffel Tower"), ("statue of liberty", "Statue de la Liberté", "Statue of Liberty"),
    ("colosseum rome", "Colisée", "Colosseum"), ("taj mahal", "Taj Mahal"), ("big ben london", "Big Ben"), ("golden gate bridge", "Golden Gate", "Golden Gate Bridge"),
    ("mount fuji", "Mont Fuji", "Fuji"), ("pyramids giza", "Pyramides", "Pyramides de Gizeh"), ("sydney opera house", "Opéra de Sydney", "Sydney Opera House"),
    ("great wall of china", "Muraille de Chine", "Grande Muraille"), ("mont saint michel", "Mont-Saint-Michel"), ("arc de triomphe", "Arc de Triomphe"),
    ("leaning tower of pisa", "Tour de Pise", "Tour penchée de Pise"), ("sagrada familia", "Sagrada Familia"), ("machu picchu", "Machu Picchu"),
    ("cat", "Chat"), ("dog", "Chien"), ("horse", "Cheval"), ("elephant", "Éléphant"), ("giraffe", "Girafe"), ("zebra", "Zèbre"), ("lion", "Lion"),
    ("tiger", "Tigre"), ("penguin", "Pingouin", "Manchot"), ("owl", "Hibou", "Chouette"), ("fox", "Renard"), ("panda", "Panda"), ("koala", "Koala"),
    ("dolphin", "Dauphin"), ("shark", "Requin"), ("turtle", "Tortue"), ("butterfly", "Papillon"), ("frog", "Grenouille"), ("rabbit", "Lapin"),
    ("pizza", "Pizza"), ("burger", "Burger", "Hamburger"), ("sushi", "Sushi"), ("croissant", "Croissant"), ("ice cream", "Glace"), ("strawberry", "Fraise"),
    ("pineapple", "Ananas"), ("banana", "Banane"), ("watermelon", "Pastèque"), ("coffee", "Café"), ("chocolate", "Chocolat"), ("cheese", "Fromage"),
    ("guitar", "Guitare"), ("piano", "Piano"), ("violin", "Violon"), ("bicycle", "Vélo", "Bicyclette"), ("motorcycle", "Moto"), ("airplane", "Avion"),
    ("sailboat", "Voilier", "Bateau"), ("train", "Train"), ("hot air balloon", "Montgolfière"), ("lighthouse", "Phare"), ("volcano", "Volcan"),
    ("waterfall", "Cascade", "Chute d'eau"), ("desert", "Désert"), ("glacier", "Glacier"), ("rainbow", "Arc-en-ciel"), ("snowman", "Bonhomme de neige"),
    ("christmas tree", "Sapin de Noël"), ("pumpkin", "Citrouille"), ("sunflower", "Tournesol"), ("rose flower", "Rose"), ("cactus", "Cactus"),
    ("mushroom", "Champignon"), ("umbrella", "Parapluie"), ("chess", "Échecs", "Jeu d'échecs"), ("football soccer ball", "Football", "Ballon de foot"),
    ("basketball", "Basket", "Basketball"), ("skateboard", "Skateboard", "Skate"), ("typewriter", "Machine à écrire"), ("camera vintage", "Appareil photo"),
    ("headphones", "Casque", "Casque audio"), ("clock", "Horloge"), ("candle", "Bougie"), ("book library", "Bibliothèque", "Livres"), ("globe", "Globe")]
if category("photo", ["unsplash_access_key"]):
    H = {"Authorization": "Client-ID " + KEYS["unsplash_access_key"], "Accept-Version": "v1"}
    done = 0
    for q, *names in THEMES:
        try:
            d = get("https://api.unsplash.com/search/photos", {"query": q, "per_page": 4, "orientation": "landscape", "content_filter": "high"}, headers=H, pause=0.2)
        except requests.HTTPError as e:
            print("unsplash : limite atteinte, relancer dans une heure pour completer (", e, ")"); break
        for ph in (d or {}).get("results", [])[:3]:
            credit = f"Photo : {ph['user']['name']} sur Unsplash"
            add("photo", ph["urls"]["regular"], answers(*names, acro=False), names[0], "us:" + ph["id"], credit=credit)
        done += 1
    print("photo :", sum(q["c"] == "photo" for q in pool), f"({done}/{len(THEMES)} themes)")

json.dump(pool, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=0)
counts = {}
for q in pool: counts[q["c"]] = counts.get(q["c"], 0) + 1
print("BANQUE :", len(pool), "questions", counts)
