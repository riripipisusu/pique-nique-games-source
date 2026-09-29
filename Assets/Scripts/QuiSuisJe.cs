using System;
using System.Collections.Generic;
using System.Linq;

// Qui suis-je ? (en ligne) : d'abord chacun ecrit le personnage de son voisin (le joueur suivant), sans qu'il le voie.
// Puis, a son tour, on pose une question fermee, ecrite ou a l'oral (Discord) ; les autres votent Oui / Non / Je ne sais
// pas, la majorite repond. Oui = on rejoue, sinon la main passe ; on peut proposer un nom a tout moment de son tour
// (faux = main suivante). Trouver vite rapporte plus. Fin : tout le monde a trouve, ou les tours sont ecoules.
// Hors ligne (contre des bots) : mode guide, personnages d'une liste connue, questions d'une liste avec reponse automatique.
// Option : bit 0 = libre (en ligne) ; bits 5-6 = nombre de tours.
public enum WPhase { Choose, Ask, Vote, Over }
public enum WEv { Chosen, Ask, Answer, Found, Wrong, Pass, Turn, Over }
public class WEvent { public WEv type; public int seat = -1, answer = -1, points; public string text; }

[Serializable] public class Perso { public string n, f; public string[] a, y, u; }
[Serializable] class PersoFile { public Perso[] items; }

public class QuiSuisJe : IMatch
{
    public const int MaxPlayers = 8, Oui = 1, Non = 0, NeSaitPas = 2;
    public const string Oral = "(question posée à l'oral)";
    public static readonly int[] RoundChoices = { 8, 12, 20, 5 };
    public static string AnswerText(int a) => a == Oui ? "Oui !" : a == Non ? "Non !" : "Je ne sais pas...";

    // Mode guide : questions (cle, texte, groupe) et personnages connus (Tools/quisuisje_pool.py).
    public static readonly (string id, string text, string group)[] Questions =
    {
        ("humain", "Je suis un être humain ?", "Général"), ("fiction", "Je suis un personnage de fiction ?", "Général"),
        ("animal", "Je suis un animal ?", "Général"), ("objet", "Je suis un objet ?", "Général"),
        ("femme", "Je suis une femme ?", "Général"), ("grand", "Je suis plus grand qu'un humain ?", "Général"),
        ("vivant", "Je suis encore en vie ?", "Personne"), ("avant1900", "Je suis né(e) avant 1900 ?", "Personne"),
        ("avant1970", "Je suis né(e) avant 1970 ?", "Personne"), ("francais", "Je suis français(e) ?", "Personne"),
        ("europe", "Je viens d'Europe ?", "Personne"), ("amerique", "Je viens d'Amérique ?", "Personne"),
        ("asie", "Je viens d'Asie ?", "Personne"), ("afrique", "Je viens d'Afrique ?", "Personne"),
        ("musique", "Je fais de la musique ?", "Personne"), ("cinema", "Je fais du cinéma ou de la télé ?", "Personne"),
        ("sport", "Je fais du sport ?", "Personne"), ("politique", "Je fais de la politique ou la guerre ?", "Personne"),
        ("science", "Je suis scientifique ou inventeur ?", "Personne"), ("lettres", "J'écris des livres ou des idées ?", "Personne"),
        ("art", "Je suis artiste (peinture, mode...) ?", "Personne"), ("royaute", "Je suis roi, reine ou empereur ?", "Personne"),
        ("anime", "Je viens d'un dessin animé ?", "Fiction"), ("jeuvideo", "Je viens d'un jeu vidéo ?", "Fiction"),
        ("bd", "Je viens d'une BD ou d'un manga ?", "Fiction"), ("film", "Je viens d'un film ou d'une série ?", "Fiction"),
        ("livre", "Je viens d'un livre ?", "Fiction"), ("mechant", "Je suis un méchant ?", "Fiction"),
        ("pouvoirs", "J'ai des pouvoirs magiques ou surhumains ?", "Fiction"),
        ("mammifere", "Je suis un animal mammifère ?", "Animal"), ("oiseau", "Je suis un oiseau ?", "Animal"),
        ("poisson", "Je suis un poisson ?", "Animal"), ("reptile", "Je suis un reptile ou un amphibien ?", "Animal"),
        ("insecte", "Je suis un insecte ou une araignée ?", "Animal"), ("vole", "Je sais voler ?", "Animal"),
        ("eau", "Je vis dans l'eau ?", "Animal"), ("domestique", "Je vis à la ferme ou à la maison ?", "Animal"),
        ("carnivore", "Je mange de la viande ?", "Animal"),
        ("electrique", "Je marche à l'électricité ?", "Objet"), ("cuisine", "On me trouve dans la cuisine ?", "Objet"),
        ("maison", "On me trouve dans une maison ?", "Objet"), ("porte", "On me porte sur soi ?", "Objet"),
        ("jouet", "Je suis un jouet ?", "Objet"), ("transport", "Je sers à se déplacer ?", "Objet"), ("outil", "Je suis un outil ?", "Objet"),
    };
    public static string QuestionText(string id) => Questions.FirstOrDefault(q => q.id == id).text;
    static string IdOf(string text) => Questions.FirstOrDefault(q => q.text == text).id;
    static List<Perso> all;
    public static List<Perso> All => all ??= UnityEngine.JsonUtility.FromJson<PersoFile>(
        "{\"items\":" + UnityEngine.Resources.Load<UnityEngine.TextAsset>("QuiSuisJe/persos").text + "}").items.ToList();
    public static Perso Find(string name) => All.FirstOrDefault(p => p.n == name);
    public static int Answer(Perso p, string id) => p.y != null && p.y.Contains(id) ? Oui : p.u != null && p.u.Contains(id) ? NeSaitPas : Non;

    public class Player
    {
        public string name, perso; public int seat, asked, score, foundOrder = -1;
        public bool Found => foundOrder >= 0;
        public readonly List<(string q, int answer)> history = new List<(string, int)>();
    }

    public readonly List<Player> players = new List<Player>();
    public readonly List<WEvent> events = new List<WEvent>();
    public readonly List<string> log = new List<string>();
    public readonly int maxRounds;
    public readonly bool free;          // en ligne : questions et personnages libres ; hors ligne : mode guide
    public WPhase phase = WPhase.Choose;
    public int turn, round = 1, foundCount;
    public bool canAsk = true;          // debut de tour, ou derniere reponse Oui
    public string pending;              // la question en cours de vote
    public readonly Dictionary<int, int> votes = new Dictionary<int, int>();

    public int Actor => phase == WPhase.Over ? -1 : phase == WPhase.Ask ? turn : Quiz.Everyone;
    public bool Finished => phase == WPhase.Over;
    public Player Current => players[turn];
    public int TargetOf(int seat) => (seat + 1) % players.Count;   // je choisis le personnage de mon voisin

    public QuiSuisJe(IList<string> names, int option, int seed)
    {
        maxRounds = RoundChoices[(option >> 5) & 3];
        free = (option & 1) != 0;
        for (int i = 0; i < names.Count; i++) players.Add(new Player { name = names[i], seat = i });
        log.Add("Chacun choisit le personnage de son voisin...");
    }

    public bool TryApply(string[] a)
    {
        events.Clear();
        if (Finished || a.Length == 0) return false;
        switch (a[0])
        {
            case "pick":   // pick|siege|personnage : le personnage du voisin de ce siege
            {
                if (phase != WPhase.Choose || a.Length < 3 || !int.TryParse(a[1], out int s) || s < 0 || s >= players.Count) return false;
                var name = a[2].Trim();
                var t = players[TargetOf(s)];
                if (name.Length == 0 || name.Length > 60 || t.perso != null || (!free && (Find(name) == null || players.Any(p => p.perso == name)))) return false;
                t.perso = name;
                events.Add(new WEvent { type = WEv.Chosen, seat = t.seat });
                if (players.All(p => p.perso != null))
                {
                    phase = WPhase.Ask;
                    log.Add("Tout le monde a son post-it. C'est parti !");
                    events.Add(new WEvent { type = WEv.Turn, seat = turn });
                }
                return true;
            }
            case "ask":   // guide : ask|cle, le jeu repond d'apres les donnees
                if (free || phase != WPhase.Ask || !canAsk || a.Length < 2 || QuestionText(a[1]) == null) return false;
                pending = QuestionText(a[1]);
                events.Add(new WEvent { type = WEv.Ask, seat = turn, text = pending });
                Answered(Answer(Find(Current.perso), a[1]));
                return true;
            case "askfree":   // askfree|texte
                if (!free || phase != WPhase.Ask || !canAsk || a.Length < 2 || a[1].Trim().Length == 0) return false;
                return StartVote(a[1].Trim());
            case "askoral":   // la question a ete posee a voix haute (Discord)
                if (!free || phase != WPhase.Ask || !canAsk) return false;
                return StartVote(Oral);
            case "vote":   // vote|siege|0,1,2 (tous sauf celui qui demande)
                if (phase != WPhase.Vote || a.Length < 3 || !int.TryParse(a[1], out int vs) || !int.TryParse(a[2], out int v)
                    || vs < 0 || vs >= players.Count || vs == turn || v < 0 || v > 2 || votes.ContainsKey(vs)) return false;
                votes[vs] = v;
                if (votes.Count >= players.Count - 1) Resolve();
                return true;
            case "voteend":   // l'hote coupe le vote (temps ecoule, joueur parti)
                if (phase != WPhase.Vote) return false;
                Resolve();
                return true;
            case "guess":
                if (phase != WPhase.Ask || a.Length < 2 || a[1].Trim().Length == 0) return false;
                Guess(a[1].Trim());
                return true;
            case "pass":
                if (phase != WPhase.Ask) return false;
                events.Add(new WEvent { type = WEv.Pass, seat = turn });
                Next();
                return true;
        }
        return false;
    }

    bool StartVote(string q)
    {
        pending = q; votes.Clear(); phase = WPhase.Vote;
        events.Add(new WEvent { type = WEv.Ask, seat = turn, text = q });
        log.Add($"{Current.name} : « {q} »");
        if (players.Count == 1) Resolve();
        return true;
    }

    void Resolve()
    {
        int yes = votes.Values.Count(x => x == Oui), no = votes.Values.Count(x => x == Non);
        Answered(yes > no ? Oui : no > yes ? Non : NeSaitPas);
    }

    void Answered(int ans)
    {
        var p = Current;
        p.asked++;
        p.history.Add((pending, ans));
        phase = WPhase.Ask;
        canAsk = ans == Oui;
        events.Add(new WEvent { type = WEv.Answer, seat = turn, answer = ans, text = pending });
        log.Add(free ? $"→ {AnswerText(ans)}" : $"{p.name} : « {pending} » → {AnswerText(ans)}");
        pending = null;
    }

    // Noms ecrits par des humains : fautes de frappe toleres, et "Napoléon" suffit pour "Napoléon Bonaparte".
    public static bool SameName(string guess, string answer)
    {
        if (Quiz.Matches(guess, new[] { answer })) return true;
        string g = Quiz.Normalize(guess), a = Quiz.Normalize(answer);
        return g.Length >= 4 && a.Length >= 4 && (a.Contains(g) || g.Contains(a));
    }

    void Guess(string text)
    {
        var p = Current;
        if (SameName(text, p.perso))
        {
            p.foundOrder = foundCount++;
            int pts = Math.Max(2, 12 - p.asked) + (p.foundOrder == 0 ? 3 : 0);
            p.score += pts;
            events.Add(new WEvent { type = WEv.Found, seat = turn, points = pts, text = p.perso });
            log.Add($"{p.name} a trouvé : {p.perso} ! (+{pts})");
        }
        else
        {
            events.Add(new WEvent { type = WEv.Wrong, seat = turn, text = text });
            log.Add($"{p.name} propose « {text} »... raté !");
        }
        Next();
    }

    void Next()
    {
        canAsk = true;
        if (players.All(x => x.Found)) { End(); return; }
        int from = turn;
        for (int k = 1; k <= players.Count; k++)
        {
            int s = (from + k) % players.Count;
            if (!players[s].Found) { turn = s; break; }
        }
        if (turn <= from) round++;   // on est repasse par le premier joueur
        if (round > maxRounds) { End(); return; }
        events.Add(new WEvent { type = WEv.Turn, seat = turn });
    }

    void End()
    {
        phase = WPhase.Over;
        events.Add(new WEvent { type = WEv.Over });
        log.Add("Fin de la partie !");
    }

    // --- Bots (mode guide) ---------------------------------------------------------------
    static readonly Random botRng = new Random();   // a part : le tirage des regles reste identique partout
    // Choix : un personnage connu pas encore pris.
    public string BotPick() { var left = All.Where(p => players.All(x => x.perso != p.n)).ToList(); return left[botRng.Next(left.Count)].n; }

    // Tour : garde les personnages compatibles avec ses reponses, pose la question qui coupe le mieux le reste en deux,
    // propose un nom quand il en reste peu. En ligne (joueur parti), il passe.
    public string[] Bot()
    {
        if (phase != WPhase.Ask) return null;
        if (free) return new[] { "pass" };
        var me = Current;
        var cand = All.Where(c => me.history.All(h => IdOf(h.q) != null && Answer(c, IdOf(h.q)) == h.answer)).ToList();
        cand.RemoveAll(c => players.Any(o => o.seat != turn && o.perso == c.n));   // ceux que je vois sur les autres fronts
        if (cand.Count == 0) return new[] { "pass" };
        bool sure = cand.Count == 1 || (cand.Count <= 3 && botRng.NextDouble() < 0.5);
        if (sure || !canAsk) return cand.Count <= 4 || sure ? new[] { "guess", cand[botRng.Next(cand.Count)].n } : new[] { "pass" };
        var asked = new HashSet<string>(me.history.Select(h => IdOf(h.q)));
        var best = Questions.Where(q => !asked.Contains(q.id))
            .Select(q => (q.id, score: Math.Min(cand.Count(c => Answer(c, q.id) == Oui), cand.Count(c => Answer(c, q.id) != Oui)) + botRng.NextDouble()))
            .OrderByDescending(x => x.score).ToList();
        if (best.Count == 0) return new[] { "guess", cand[botRng.Next(cand.Count)].n };
        var pick = best[botRng.NextDouble() < 0.3 ? botRng.Next(Math.Min(5, best.Count)) : 0];   // un peu d'erreur
        return new[] { "ask", pick.id };
    }
}
