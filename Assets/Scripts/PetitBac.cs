using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

// Petit bac (TV Time) : une lettre, des categories (choisies ou ecrites par l'hote), tout le monde ecrit en meme temps. Le premier qui a tout rempli
// peut crier STOP (sinon le temps s'ecoule). Puis chacun valide ou refuse les reponses des autres : une reponse est
// refusee si plus de la moitie des autres joueurs la refusent. Champ vide 0, meme mot qu'un autre 5, reponse unique 10,
// seul a avoir trouve un mot dans la categorie 20.
// Deterministe : l'hote pilote les phases ("next" : nouvelle lettre, "end" : fin de l'ecriture, "score" : fin du vote)
// et ajoute le siege aux actions des joueurs ("ans|siege|categorie|texte", "stop|siege", "vote|siege|cible|categorie|0/1",
// "ready|siege").
public enum BPhase { Intro, Write, Vote, Scores, GameOver }
public enum BEv { Letter, Stop, Vote, Scored, GameOver }
public class BEvent { public BEv type; public int seat = -1; }

public class PetitBac : IMatch
{
    public const int MaxPlayers = 10, MaxCats = 10, WriteMs = 90000, VoteMs = 45000;
    public static readonly string[] AllCategories =
    {
        "Prénom", "Pays", "Ville", "Animal", "Fruit ou légume", "Métier", "Objet", "Marque", "Sport",
        "Film ou série", "Plat ou aliment", "Vêtement", "Instrument de musique", "Célébrité", "Personnage de fiction", "Couleur",
    };
    const string LetterPool = "ABCDEFGHIJLMNOPRSTUV";   // sans K, Q, W, X, Y, Z : trop durs

    public readonly List<QPlayer> players = new List<QPlayer>();
    public readonly List<string> log = new List<string>();
    public readonly List<BEvent> events = new List<BEvent>();
    public readonly string[] categories;
    public readonly int rounds;
    public BPhase phase = BPhase.Intro;
    public int round;               // manche en cours (1..rounds)
    public char letter;
    public int stopper = -1;
    public string[,] answers;       // [siege, categorie]
    public HashSet<int>[,] rejects; // [siege, categorie] : qui refuse cette reponse
    public int[,] points;           // [siege, categorie] : points de la derniere manche
    public readonly HashSet<int> ready = new HashSet<int>();
    readonly List<char> letters;

    public int Actor => phase == BPhase.Write || phase == BPhase.Vote ? Quiz.Everyone : -1;
    public bool Finished => phase == BPhase.GameOver;

    // Option : bits 0-1 = manches (0 : 5, 1 : 3, 2 : 8) ; bits 2 et suivants = categories de la liste cochees par l'hote.
    // Les categories en saisie libre passent a part (texte du salon, separees par ';').
    public static int Rounds(int option) => (option & 3) == 1 ? 3 : (option & 3) == 2 ? 8 : 5;
    public static bool HasCat(int option, int i) => (option >> 2 & 1 << i) != 0;
    public static List<string> Customs(string text) => (text ?? "").Split(';', ',').Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().Take(MaxCats).ToList();
    public int CatCount => categories.Length;

    public PetitBac(IList<string> names, int option, int seed, string custom = null)
    {
        var rng = new Random(seed);
        rounds = Rounds(option);
        for (int i = 0; i < names.Count; i++) players.Add(new QPlayer { name = names[i], seat = i });
        // Categories : celles cochees + celles ecrites par l'hote ; moins de 3 : on complete au hasard jusqu'a 6.
        var cats = Enumerable.Range(0, AllCategories.Length).Where(i => HasCat(option, i)).Select(i => AllCategories[i]).Concat(Customs(custom)).Distinct().Take(MaxCats).ToList();
        if (cats.Count < 3) cats.AddRange(AllCategories.Where(c => !cats.Contains(c)).OrderBy(_ => rng.Next()).Take(6 - cats.Count));
        categories = cats.ToArray();
        letters = LetterPool.OrderBy(_ => rng.Next()).Take(rounds).ToList();
        answers = new string[names.Count, categories.Length];
        rejects = new HashSet<int>[names.Count, categories.Length];
        points = new int[names.Count, categories.Length];
    }

    // Comparaison sans accents, casse ni espaces superflus ; articles de tete ignores ("le chat" = "chat").
    public static string Norm(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        var t = string.Join(" ", sb.ToString().Split(new[] { ' ', '-', '\'' }, StringSplitOptions.RemoveEmptyEntries));
        foreach (var art in new[] { "le ", "la ", "les ", "l ", "un ", "une ", "des " }) if (t.StartsWith(art) && t.Length > art.Length) { t = t.Substring(art.Length); break; }
        return t;
    }
    public bool RightLetter(string s) { var n = Norm(s); return n.Length > 0 && char.ToUpperInvariant(n[0]) == letter; }
    public bool Filled(int seat) => Enumerable.Range(0, CatCount).All(c => RightLetter(answers[seat, c]));
    public bool Rejected(int seat, int cat) => rejects[seat, cat] != null && rejects[seat, cat].Count * 2 > players.Count - 1;
    public bool Valid(int seat, int cat) => RightLetter(answers[seat, cat]) && !Rejected(seat, cat);

    void Emit(BEv t, int seat = -1) => events.Add(new BEvent { type = t, seat = seat });
    bool Seat(string s, out int seat) => int.TryParse(s, out seat) && seat >= 0 && seat < players.Count;

    public bool TryApply(string[] a)
    {
        events.Clear();
        switch (a[0])
        {
            case "next":
                if (phase != BPhase.Intro && phase != BPhase.Scores) return false;
                round++;
                letter = letters[round - 1];
                stopper = -1;
                ready.Clear();
                for (int s = 0; s < players.Count; s++)
                    for (int c = 0; c < CatCount; c++) { answers[s, c] = null; rejects[s, c] = null; points[s, c] = 0; }
                foreach (var p in players) p.gained = 0;
                phase = BPhase.Write;
                log.Add($"Manche {round} : la lettre {letter} !");
                Emit(BEv.Letter);
                return true;
            case "ans":   // ans|siege|categorie|texte
            {
                if (phase != BPhase.Write || a.Length < 4 || !Seat(a[1], out int s) || !int.TryParse(a[2], out int c) || c < 0 || c >= CatCount) return false;
                answers[s, c] = a[3].Trim();
                return true;
            }
            case "stop":
            {
                if (phase != BPhase.Write || a.Length < 2 || !Seat(a[1], out int s) || !Filled(s)) return false;
                stopper = s;
                log.Add($"{players[s].name} crie STOP !");
                Emit(BEv.Stop, s);
                StartVote();
                return true;
            }
            case "end":
                if (phase != BPhase.Write) return false;
                Emit(BEv.Stop);
                StartVote();
                return true;
            case "vote":  // vote|siege|cible|categorie|1 (refuse) ou 0 (accepte)
            {
                if (phase != BPhase.Vote || a.Length < 5 || !Seat(a[1], out int s) || !Seat(a[2], out int t) || s == t
                    || !int.TryParse(a[3], out int c) || c < 0 || c >= CatCount || !RightLetter(answers[t, c])) return false;   // on ne vote que sur un vrai mot
                rejects[t, c] ??= new HashSet<int>();
                if (a[4] == "1") rejects[t, c].Add(s); else rejects[t, c].Remove(s);
                Emit(BEv.Vote, t);
                return true;
            }
            case "ready":
            {
                if (phase != BPhase.Vote || a.Length < 2 || !Seat(a[1], out int s)) return false;
                ready.Add(s);
                Emit(BEv.Vote, s);
                return true;
            }
            case "score":
                if (phase != BPhase.Vote) return false;
                Score();
                return true;
        }
        return false;
    }

    void StartVote()
    {
        phase = BPhase.Vote;
        ready.Clear();
        Emit(BEv.Vote);
    }

    void Score()
    {
        for (int c = 0; c < CatCount; c++)
            for (int s = 0; s < players.Count; s++)
            {
                if (!Valid(s, c)) { points[s, c] = 0; continue; }
                var n = Norm(answers[s, c]);
                var others = Enumerable.Range(0, players.Count).Where(o => o != s && Valid(o, c)).ToList();
                points[s, c] = others.Count == 0 ? 20 : others.Any(o => Norm(answers[o, c]) == n) ? 5 : 10;   // seul a trouver / unique / meme mot
            }
        foreach (var p in players)
        {
            p.gained = Enumerable.Range(0, CatCount).Sum(c => points[p.seat, c]);
            p.score += p.gained;
        }
        log.Add("Points : " + string.Join(", ", players.Select(p => $"{p.name} +{p.gained}")));
        phase = round >= rounds ? BPhase.GameOver : BPhase.Scores;
        Emit(BEv.Scored);
        if (phase == BPhase.GameOver) Emit(BEv.GameOver);
    }

    // Joueur parti : il ne fait rien (le temps et les autres terminent la manche).
    public string[] Bot() => null;
}
