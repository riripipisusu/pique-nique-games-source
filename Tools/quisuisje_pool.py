# Personnages du "Qui suis-je ?" hors ligne (contre des bots) -> Assets/Resources/QuiSuisJe/persos.json
#   python Tools/quisuisje_pool.py
# Chaque personnage : n (nom), a (reponses acceptees), f (famille), y (questions dont la reponse est Oui),
# u (Je ne sais pas) ; toute autre question vaut Non. Personnalites tres connues choisies a la main (donnees Wikidata
# via le cache de tenna_pool.py) ; fiction, animaux et objets ecrits ici.
import json, os, re
from tenna_pool import sparql, people, qid, lbl, gender

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(ROOT, "..", "Assets", "Resources", "QuiSuisJe", "persos.json")
os.makedirs(os.path.dirname(OUT), exist_ok=True)

FAMOUS = """Barack Obama|William Shakespeare|Michael Jackson|Vladimir Poutine|Karl Marx|Léonard de Vinci|Isaac Newton|Nelson Mandela|
Napoléon Ier|Mohandas Karamchand Gandhi|Jules César|Charles Darwin|Socrate|Wolfgang Amadeus Mozart|Ludwig van Beethoven|Galilée|
Vincent van Gogh|Pablo Picasso|Alexandre le Grand|Charlie Chaplin|Winston Churchill|Voltaire|Élisabeth II|Thomas Edison|René Descartes|
Sigmund Freud|John Fitzgerald Kennedy|Martin Luther King|Salvador Dalí|Christophe Colomb|Bill Gates|Marilyn Monroe|Steve Jobs|Che Guevara|
Molière|Lionel Messi|Charles de Gaulle|Neil Armstrong|Nikola Tesla|Walt Disney|Elvis Presley|Cristiano Ronaldo|Louis Pasteur|
Marco Polo|Pythagore|Stephen Hawking|Elon Musk|Frida Kahlo|Agatha Christie|Jeanne d'Arc|Nicolas Sarkozy|Madonna|Jules Verne|Alan Turing|
Alfred Hitchcock|Charlemagne|Bob Dylan|John Lennon|Louis XIV|Pelé|Mohamed Ali|Taylor Swift|Freddie Mercury|Cléopâtre|Steven Spielberg|
Antoine de Saint-Exupéry|Anne Frank|Mark Zuckerberg|Émile Zola|Bob Marley|Bruce Lee|Jackie Chan|Céline Dion|Claude Monet|Greta Thunberg|
Rafael Nadal|Brigitte Bardot|Diana Spencer|Leonardo DiCaprio|Paul McCartney|Brad Pitt|Johnny Depp|Stephen King|Édith Piaf|Zinedine Zidane|
Jean de La Fontaine|Arthur Rimbaud|Ariana Grande|Michael Jordan|Alain Delon|Whitney Houston|Usain Bolt|David Bowie|Adele|Katy Perry|
Tom Hanks|Will Smith|Kylian Mbappé|Marie-Antoinette|Meryl Streep|Amy Winehouse|Rosa Parks|Kurt Cobain|George Clooney|Morgan Freeman|
Louis XVI|Jim Carrey|Billie Eilish|Serena Williams|Sylvester Stallone|Quentin Tarantino|Keanu Reeves|Rowan Atkinson|Henri VIII|
Emmanuel Macron|Donald Trump|Joe Biden|Angela Merkel|Jean-Paul II|Mère Teresa"""
# Absents du cache : ecrits a la main (nom | oui).
EXTRA_PEOPLE = """
Albert Einstein | humain avant1900 avant1970 europe science
Marie Curie | humain femme avant1900 avant1970 europe francais science
Victor Hugo | humain avant1900 avant1970 europe francais lettres politique
Coco Chanel | humain femme avant1900 avant1970 europe francais art
Beyoncé | humain femme vivant amerique musique cinema
Rihanna | humain femme vivant amerique musique
Eminem | humain vivant avant1970 amerique musique
Lady Gaga | humain femme vivant amerique musique cinema
Omar Sy | humain vivant europe francais cinema
Emmanuel Macron | humain vivant europe francais politique
Donald Trump | humain vivant avant1970 amerique politique
"""

JOBS = {
    "musique": r"chant|musicien|compositeur|rappeu|pianiste|guitariste|batteu|DJ|disc-jockey|chef d'orchestre|parolier",
    "cinema": r"acteur|actrice|réalisat|cinéaste|scénariste|comédien|humoriste|animat(eur|rice) de télévision|mannequin",
    "sport": r"joueu|footballeu|athlète|tennis|boxeu|pilote|nageu|cycliste|basket|golfeu|skieu|sprinteu|lutteu|entraîneu",
    "politique": r"politique|président|premier ministre|diplomate|militaire|homme d'État|femme d'État|révolutionnaire|chef d'État",
    "science": r"physicien|chimiste|mathématicien|biologiste|astronome|inventeu|ingénieu|médecin|informaticien|naturaliste|astronaute",
    "lettres": r"écrivain|poète|romancier|dramaturge|philosophe|essayiste",
    "art": r"peintre|sculpteu|architecte|photographe|dessinat|illustrat|styliste|designer",
    "royaute": r"monarque|\broi\b|\breine\b|empereur|impératrice|pharaon|sultan|\btsar",
}

def answers(name):
    parts = name.replace("Ier", "").split()
    a = [name] + ([parts[-1]] if len(parts) >= 2 and len(parts[-1]) >= 4 else [])
    if name.endswith(" Ier"): a.append(name[:-4])
    return a

def persos():
    wanted = [n.strip() for n in FAMOUS.replace("\n", "").split("|")]
    ppl = {p["name"]: p for p in people() if p["b"]}
    found = [ppl[n] for n in wanted if n in ppl]
    extra = {}
    ids = [p["id"] for p in found]
    for i in range(0, len(ids), 200):
        chunk = " ".join("wd:" + k for k in ids[i:i + 200])
        for r in sparql(f"qsj2_{i}", f"""
SELECT ?p ?death ?natLabel ?contLabel WHERE {{
  VALUES ?p {{ {chunk} }}
  OPTIONAL {{ ?p wdt:P570 ?death }}
  OPTIONAL {{ ?p wdt:P27 ?nat . OPTIONAL {{ ?nat wdt:P30 ?cont }} }}
  SERVICE wikibase:label {{ bd:serviceParam wikibase:language "fr". }} }}"""):
            e = extra.setdefault(qid(r["p"]), {"dead": False, "nat": set(), "cont": set()})
            if r.get("death"): e["dead"] = True
            if lbl(r.get("natLabel")): e["nat"].add(r["natLabel"])
            if lbl(r.get("contLabel")): e["cont"].add(r["contLabel"])
    out = []
    for p in found:
        e = extra.get(p["id"], {"dead": True, "nat": set(), "cont": set()})
        occ = " ".join(gender(o, p["g"]) for o in p["occ"])
        y = ["humain"]
        if p["g"] == "Q6581072": y.append("femme")
        if not e["dead"]: y.append("vivant")
        if p["b"] < 1900: y.append("avant1900")
        if p["b"] < 1970: y.append("avant1970")
        if "France" in e["nat"] or (not e["nat"] and p["country"] == "France"): y.append("francais")
        for c, key in (("Europe", "europe"), ("Amérique", "amerique"), ("Asie", "asie"), ("Afrique", "afrique")):
            if any(c in x for x in e["cont"]): y.append(key)
        y += [k for k, rx in JOBS.items() if re.search(rx, occ, re.I)]
        out.append({"n": p["name"], "a": answers(p["name"]), "f": "perso", "y": y})
    missing = [n for n in wanted if n not in ppl]
    have = {x["n"] for x in out}
    for line in EXTRA_PEOPLE.strip().splitlines():
        name, _, flags = line.partition("|")
        if name.strip() in have: continue
        out.append({"n": name.strip(), "a": answers(name.strip()), "f": "perso", "y": flags.split()})
    print("  absents du cache :", [m for m in missing if m not in EXTRA_PEOPLE])
    return out

FICTION = """
Mickey Mouse | anime animal_fic
Bugs Bunny | anime animal_fic
Homer Simpson | anime humain
Bart Simpson | anime humain
Bob l'éponge | anime animal_fic eau
Shrek | film anime grand
Pikachu | jeuvideo anime animal_fic pouvoirs
Mario | jeuvideo humain
Luigi | jeuvideo humain
Princesse Peach | jeuvideo humain femme
Bowser | jeuvideo mechant pouvoirs grand
Sonic | jeuvideo animal_fic pouvoirs
Link | jeuvideo humain
Lara Croft | jeuvideo humain femme
Pac-Man | jeuvideo
Kirby | jeuvideo pouvoirs
Donkey Kong | jeuvideo animal_fic grand
Harry Potter | livre film humain pouvoirs
Hermione Granger | livre film humain femme pouvoirs
Voldemort | livre film humain mechant pouvoirs
Dark Vador | film humain mechant pouvoirs
Yoda | film pouvoirs
Luke Skywalker | film humain pouvoirs
Superman | bd film humain pouvoirs
Batman | bd film humain
Spider-Man | bd film humain pouvoirs
Wonder Woman | bd film humain femme pouvoirs
Le Joker | bd film humain mechant
Iron Man | bd film humain
Hulk | bd film humain pouvoirs grand
Astérix | bd humain francais
Obélix | bd humain francais grand pouvoirs
Tintin | bd humain
Milou | bd animal_fic
Lucky Luke | bd humain
Gaston Lagaffe | bd humain francais
Les Schtroumpfs | bd anime
Garfield | bd anime animal_fic
Snoopy | bd anime animal_fic
Naruto | bd anime humain pouvoirs
Son Goku | bd anime humain pouvoirs
Luffy | bd anime humain pouvoirs
Blanche-Neige | anime livre humain femme
Cendrillon | anime livre humain femme
Elsa | anime humain femme pouvoirs
Simba | anime animal_fic
Buzz l'Éclair | anime
Woody | anime
Nemo | anime animal_fic eau
Winnie l'ourson | anime livre animal_fic
Pinocchio | anime livre
Peter Pan | anime livre humain pouvoirs
Capitaine Crochet | anime livre humain mechant
Aladdin | anime humain
Stitch | anime pouvoirs
Scooby-Doo | anime animal_fic
Sherlock Holmes | livre film humain
Dracula | livre film humain mechant pouvoirs
Le Petit Prince | livre humain
Gandalf | livre film humain pouvoirs
Gollum | livre film mechant
James Bond | film livre humain
Indiana Jones | film humain
E.T. | film pouvoirs
Godzilla | film mechant grand eau
King Kong | film animal_fic grand
Jack Sparrow | film humain
Dora l'exploratrice | anime humain femme
Peppa Pig | anime animal_fic femme
Hello Kitty | anime animal_fic femme
Les Minions | anime
Maléfique | anime film humain femme mechant pouvoirs
Le Père Noël | livre humain pouvoirs
"""

ANIMALS = """
Lion | mammifere carnivore grand
Tigre | mammifere carnivore grand
Éléphant | mammifere grand
Girafe | mammifere grand
Zèbre | mammifere grand
Hippopotame | mammifere grand eau
Ours polaire | mammifere carnivore grand
Panda | mammifere grand
Kangourou | mammifere
Koala | mammifere
Gorille | mammifere grand
Loup | mammifere carnivore
Renard | mammifere carnivore
Chien | mammifere carnivore domestique
Chat | mammifere carnivore domestique
Cheval | mammifere grand domestique
Vache | mammifere grand domestique
Cochon | mammifere domestique
Mouton | mammifere domestique
Lapin | mammifere domestique
Souris | mammifere
Hérisson | mammifere
Écureuil | mammifere
Chauve-souris | mammifere vole
Baleine | mammifere grand eau
Dauphin | mammifere carnivore eau
Aigle | oiseau carnivore vole
Hibou | oiseau carnivore vole
Perroquet | oiseau vole domestique
Pingouin | oiseau carnivore eau
Autruche | oiseau grand
Poule | oiseau domestique
Canard | oiseau vole eau domestique
Flamant rose | oiseau vole eau
Requin | poisson carnivore grand eau
Poisson rouge | poisson eau domestique
Crocodile | reptile carnivore grand eau
Serpent | reptile carnivore
Tortue | reptile eau domestique
Grenouille | reptile carnivore eau
Abeille | insecte vole
Papillon | insecte vole
Fourmi | insecte
Coccinelle | insecte vole carnivore
Araignée | insecte carnivore
Escargot |
Pieuvre | carnivore eau
Méduse | carnivore eau
Dinosaure | reptile carnivore grand
"""

OBJECTS = """
Parapluie | porte
Grille-pain | electrique cuisine maison
Réfrigérateur | electrique cuisine maison grand
Micro-ondes | electrique cuisine maison
Casserole | cuisine maison
Fourchette | cuisine maison
Téléphone portable | electrique porte
Ordinateur | electrique maison
Télévision | electrique maison
Lampe | electrique maison
Aspirateur | electrique maison outil
Machine à laver | electrique maison grand
Lit | maison grand
Chaise | maison
Canapé | maison grand
Montre | porte
Lunettes | porte
Chapeau | porte
Chaussure | porte
Clé | porte
Brosse à dents | maison
Guitare | maison
Piano | maison grand
Ballon | jouet
Peluche | jouet maison
Vélo | transport
Voiture | electrique transport grand
Avion | transport grand
Bateau | transport grand eau
Trottinette | transport
Marteau | outil
Ciseaux | outil maison
Crayon | outil porte
Livre | maison
Console de jeux | electrique jouet maison
Balai | outil maison
"""

def authored(block, fam, base):
    out = []
    for line in block.strip().splitlines():
        name, _, flags = line.partition("|")
        name = name.strip(); y = list(base) + flags.split()
        if "animal_fic" in y: y.remove("animal_fic"); y.append("animal")
        # Questions sur une personne reelle (epoque, pays) : sans reponse pour les autres ; "femme" inconnu chez les animaux.
        u = ["vivant", "avant1900", "avant1970", "europe", "amerique", "asie", "afrique"] + (["femme", "francais"] if fam != "fiction" else [])
        out.append({"n": name, "a": [name], "f": fam, "y": sorted(set(y)), "u": [x for x in u if x not in y]})
    return out

if __name__ == "__main__":
    items = persos() + authored(FICTION, "fiction", ["fiction"]) + authored(ANIMALS, "animal", ["animal"]) + authored(OBJECTS, "objet", ["objet"])
    json.dump(items, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=0)
    counts = {}
    for q in items: counts[q["f"]] = counts.get(q["f"], 0) + 1
    print("PERSONNAGES :", len(items), counts)
