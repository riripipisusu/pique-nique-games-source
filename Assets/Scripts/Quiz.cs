using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

// Quiz d'images facon PopSauce : une image floutee ou pixelisee se devoile en 20 s, tout le monde tape en meme temps.
// Logique pure et deterministe : l'hote horodate chaque proposition ("guess|siege|ms|texte") et pilote les phases
// ("end" apres 20 s ou quand tout le monde a trouve, "next" apres la revelation). Premier a 100 points.
public enum QPhase { Reveal, Guess, GameOver }

[Serializable]
public class QuizQuestion
{
    public string c, u, d, id, cr, h;
    public string[] a;
    public string q;          // Grand quiz de Tenna : texte de la question (pas d'image)
    public string[] p;        // ... et ses 4 propositions (QCM)
}

[Serializable] class QuizPoolFile { public QuizQuestion[] items; }

public class QPlayer
{
    public string name;
    public int seat, score, gained;   // gained : points du tour en cours
    public bool found, locked;   // locked : QCM, mauvaise reponse donnee, plus d'essai pour cette question
    public int foundMs = -1;
}

public enum QEv { Question, Wrong, Found, Reveal, GameOver }

public class QEvent
{
    public QEv type;
    public int seat = -1, points, ms;
    public string text;
}

public class Quiz : IMatch
{
    public const int Everyone = -2, Target = 100, RoundMs = 20000, MaxPlayers = 10;
    public static readonly string[] Categories = { "film", "serie", "anime", "jeu_video", "pochette_album", "drapeau", "photo", "personnalite" };
    public static string CategoryName(string c) => c switch
    {
        "film" => "Film", "serie" => "Série", "anime" => "Anime", "jeu_video" => "Jeu vidéo", "pochette_album" => "Pochette d'album",
        "drapeau" => "Drapeau", "photo" => "Photo", "personnalite" => "Personnalité", _ => c,
    };

    public readonly List<QPlayer> players = new List<QPlayer>();
    public readonly List<string> log = new List<string>();
    public readonly List<QEvent> events = new List<QEvent>();
    public readonly List<(int seat, string text)> wrong = new List<(int, string)>();
    public QPhase phase = QPhase.Reveal;
    public int round;            // numero de la question affichee (0 = intro)
    public int mode;             // images : 0 flou, 1 pixelise, 2 melange ; questions : 0 QCM, 1 reponse libre
    public readonly bool trivia; // Grand quiz de Tenna (questions de culture generale)
    public bool Mcq => trivia && mode == 0;
    public QuizQuestion Current => round > 0 ? order[(round - 1) % order.Count] : null;
    public QuizQuestion Next => order[round % order.Count];
    public bool Pixelated(int r) => mode == 1 || (mode == 2 && (r * 7919 + seed) % 2 == 0);

    readonly List<QuizQuestion> order;
    readonly int seed;

    public Quiz(IEnumerable<string> names, int mode, int seed, IList<QuizQuestion> pool, bool trivia = false)
    {
        this.mode = mode;
        this.trivia = trivia;
        this.seed = seed;
        int s = 0;
        foreach (var n in names) players.Add(new QPlayer { name = n, seat = s++ });
        // Ordre des images : on alterne les categories pour varier, chaque categorie melangee avec la graine.
        var rng = new Random(seed);
        var byCat = pool.GroupBy(q => q.c).Select(g => new Queue<QuizQuestion>(g.OrderBy(_ => rng.Next()))).ToList();
        order = new List<QuizQuestion>();
        while (byCat.Any(q => q.Count > 0))
            foreach (var q in byCat.OrderBy(_ => rng.Next()).Where(q => q.Count > 0)) order.Add(q.Dequeue());
    }

    static List<QuizQuestion> pool;
    // Banque de questions livree avec le jeu (Tools/quiz_pool.py) : identique chez tous les joueurs.
    public static List<QuizQuestion> Pool => pool ??= UnityEngine.JsonUtility.FromJson<QuizPoolFile>(
        "{\"items\":" + UnityEngine.Resources.Load<UnityEngine.TextAsset>("Quiz/pool").text + "}").items.ToList();

    static List<QuizQuestion> triviaPool;
    // Questions OpenQuizzDB (Tools/trivia_pool.py) : c = theme, q = question, p = propositions, h = anecdote.
    public static List<QuizQuestion> TriviaPool => triviaPool ??= UnityEngine.JsonUtility.FromJson<QuizPoolFile>(
        "{\"items\":" + UnityEngine.Resources.Load<UnityEngine.TextAsset>("Quiz/trivia").text + "}").items.ToList();

    public int Actor => phase == QPhase.Guess ? Everyone : -1;
    public bool Finished => phase == QPhase.GameOver;
    public bool AllFound => players.All(p => p.found || p.locked);
    public string[] Bot() => null;

    void Emit(QEv t, int seat = -1, int points = 0, int ms = 0, string text = null) =>
        events.Add(new QEvent { type = t, seat = seat, points = points, ms = ms, text = text });
    void Log(string s) { log.Add(s); if (log.Count > 60) log.RemoveAt(0); }

    // Rapide = plus de points : 10 immediatement, 3 a la derniere seconde, +2 pour le premier a trouver.
    public static int PointsFor(int ms, bool first) => 3 + (int)Math.Round(7 * (1 - Math.Min(1.0, ms / (double)RoundMs))) + (first ? 2 : 0);

    public bool TryApply(string[] a)
    {
        events.Clear();
        switch (a[0])
        {
            case "next":
                if (phase != QPhase.Reveal) return false;
                round++;
                phase = QPhase.Guess;
                wrong.Clear();
                foreach (var p in players) { p.found = false; p.locked = false; p.foundMs = -1; p.gained = 0; }
                Emit(QEv.Question, text: Current.c);
                return true;

            case "guess":
                if (phase != QPhase.Guess || a.Length < 4 || !int.TryParse(a[1], out int seat) || seat < 0 || seat >= players.Count
                    || !int.TryParse(a[2], out int ms)) return false;
                var pl = players[seat];
                string text = a[3].Trim();
                if (pl.found || pl.locked || text.Length == 0 || text.Length > 60) return false;
                // QCM : une seule reponse, forcement l'une des 4 propositions ; comparaison exacte.
                if (Mcq && Array.IndexOf(Current.p, text) < 0) return false;
                if (Mcq ? text == Current.d : Matches(text, Current.a))
                {
                    bool first = players.All(p => !p.found);
                    pl.found = true;
                    pl.foundMs = ms;
                    pl.gained = PointsFor(ms, first);
                    pl.score += pl.gained;
                    Emit(QEv.Found, seat, pl.gained, ms);
                    Log($"{pl.name} a trouvé en {ms / 1000.0:0.0} s (+{pl.gained}).");
                }
                else
                {
                    wrong.Add((seat, text));
                    if (Mcq) pl.locked = true;
                    // En QCM on n'affiche pas le choix des autres (ca eliminerait une proposition pour tout le monde).
                    Emit(QEv.Wrong, seat, text: Mcq ? "" : text);
                }
                return true;

            case "end":
                if (phase != QPhase.Guess) return false;
                Log($"C'était : {Current.d}.");
                if (players.Any(p => p.score >= Target))
                {
                    phase = QPhase.GameOver;
                    var best = players.Max(p => p.score);
                    Emit(QEv.Reveal, text: Current.d);
                    Emit(QEv.GameOver, text: string.Join(" et ", players.Where(p => p.score == best).Select(p => p.name)));
                    return true;
                }
                phase = QPhase.Reveal;
                Emit(QEv.Reveal, text: Current.d);
                return true;
        }
        return false;
    }

    // --- Comparaison tolerante des reponses -----------------------------------------------
    static readonly HashSet<string> Articles = new HashSet<string> { "le", "la", "les", "l", "un", "une", "des", "the", "a", "an" };

    public static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.ToLowerInvariant().Replace("&", " et ").Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }
        var words = sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && Articles.Contains(words[0])) words.RemoveAt(0);
        return string.Join("", words);   // sans espaces : "spider man" = "spiderman"
    }

    public static bool Matches(string guess, IEnumerable<string> answers)
    {
        var g = Normalize(guess);
        if (g.Length == 0) return false;
        foreach (var ans in answers)
        {
            var a = Normalize(ans);
            if (a.Length == 0) continue;
            if (g == a) return true;
            // Fautes de frappe : 80 % de ressemblance, seulement pour les reponses assez longues.
            if (a.Length >= 5 && 1 - Levenshtein(g, a) / (double)Math.Max(g.Length, a.Length) >= 0.8) return true;
        }
        return false;
    }

    static int Levenshtein(string s, string t)
    {
        var d = new int[t.Length + 1];
        for (int j = 0; j <= t.Length; j++) d[j] = j;
        for (int i = 1; i <= s.Length; i++)
        {
            int prev = d[0]; d[0] = i;
            for (int j = 1; j <= t.Length; j++)
            {
                int tmp = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (s[i - 1] == t[j - 1] ? 0 : 1));
                prev = tmp;
            }
        }
        return d[t.Length];
    }
}
