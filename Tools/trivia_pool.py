# Banque du "Grand quiz de Tenna" : questions OpenQuizzDB (openquizzdb.org, licence CC BY-SA 4.0,
# (c) Philippe Bresoux et contributeurs). Le telechargement libre donne un extrait de 4 questions par quiz.
#   python Tools/trivia_pool.py   -> Assets/Resources/Quiz/trivia.json
# Une page toutes les ~1.5 s pour menager le site ; tout est mis en cache dans Tools/quiz_cache/oqdb/.
import html, json, os, re, time
import requests

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(ROOT, "..", "Assets", "Resources", "Quiz", "trivia.json")
CACHE = os.path.join(ROOT, "quiz_cache", "oqdb")
os.makedirs(CACHE, exist_ok=True)
S = requests.Session()
S.headers["User-Agent"] = "PiqueNiqueGames-Quiz/1.0 (jeu entre amis, credit OpenQuizzDB)"
BASE = "https://www.openquizzdb.org/"

def cached(name, fetch):
    path = os.path.join(CACHE, name)
    if os.path.exists(path):
        return open(path, encoding="utf-8").read()
    text = fetch()
    open(path, "w", encoding="utf-8").write(text)
    time.sleep(1.5)
    return text

listing = cached("listing.html", lambda: S.get(BASE + "listing", timeout=30).content.decode("utf-8", "replace"))
# Theme courant ("· ANIMAUX ·") puis les quiz qui suivent : onclick="goq(491)"> Oiseaux<
quizzes, theme = [], None
for m in re.finditer(r"&middot;\s*([^&<]{2,40}?)\s*&middot;|goq\((\d+)\)\"?>\s*([^<]+)<", listing):
    if m.group(1): theme = html.unescape(m.group(1)).strip().capitalize()
    elif theme: quizzes.append((int(m.group(2)), theme, html.unescape(m.group(3)).strip()))
print(len(quizzes), "quiz trouves")

SKIP = ("Pour adultes", "Mots croisés", "Alphaquizz", "Orthoquizz", "Quadriquizz", "Défi chiffré")
out = []
for i, (qid, theme, title) in enumerate(quizzes):
    if theme.startswith(SKIP) or title.startswith(SKIP): continue
    try:
        page = cached(f"{qid}.html", lambda: S.post(BASE + "download.php", data={"id": qid}, timeout=30).content.decode("utf-8", "replace"))
        url = re.search(r'href="(https://download\.openquizzdb\.org/[^"]+\.json)"', page)
        if not url: continue
        data = json.loads(cached(f"{qid}.json", lambda: S.get(url.group(1), timeout=30).content.decode("utf-8")))
    except Exception as e:
        print("  ignore", qid, title, e); continue
    for q in data.get("quizz", []):
        props = [p.strip() for p in q.get("propositions", [])]
        ans = (q.get("réponse") or "").strip()
        if len(props) != 4 or ans not in props or not q.get("question"): continue
        out.append({"c": theme, "d": ans, "q": q["question"].strip(), "p": props, "a": [ans], "h": (q.get("anecdote") or "").strip(),
                    "id": f"oq:{qid}:{q.get('id')}", "cr": f"Quiz « {title} » · OpenQuizzDB (CC BY-SA)"})
    if i % 50 == 0: print(f"  {i}/{len(quizzes)} ... {len(out)} questions")

json.dump(out, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=0)
counts = {}
for q in out: counts[q["c"]] = counts.get(q["c"], 0) + 1
print("BANQUE TRIVIA :", len(out), "questions", counts)
