using System;
using System.Collections.Generic;
using System.Linq;

// Limite Limite (Blanc Manger Coco) : chacun a 7 cartes blanches (reponses). Le Boss retourne une carte noire
// (question a trous "_") ; les autres posent face cachee autant de cartes de leur main qu'il y a de trous. Le Boss les decouvre et choisit sa preferee : un point pour son auteur, qui devient le Boss.
// Tout le monde recomplete sa main a 7. Le premier a N points gagne.
// Option : bit 0 = paquet "Streamer" (plus soft) ; bits 1-2 = points pour gagner (5, 7, 10).
// Cartes : Resources/Limite/cartes.json (hors depot), meme ordre partout grace a la graine.
public enum LPhase { Play, Judge, Result, Over }
public enum LEv { Round, Played, Reveal, Win, Over }
public class LEvent { public LEv type; public int seat = -1; public string text; }

public class Limite : IMatch
{
    public const int MaxPlayers = 10, MinPlayers = 3, Hand = 7;
    public static readonly int[] Targets = { 5, 7, 10 };
    public static int Target(int option) => Targets[Math.Min(2, (option >> 1) & 3)];

    [Serializable] class Deck { public string[] q, a; }
    [Serializable] class File { public Deck normal, streamer; }
    static File cards;
    static Deck Cards(bool streamer)
    {
        cards ??= UnityEngine.JsonUtility.FromJson<File>(UnityEngine.Resources.Load<UnityEngine.TextAsset>("Limite/cartes")?.text ?? "{}");
        var d = streamer ? cards?.streamer : cards?.normal;
        return d != null && d.q != null && d.q.Length > 0 ? d : new Deck { q = new[] { "Il manque le fichier des cartes : _" }, a = new[] { "Rien du tout" } };
    }

    public class Player
    {
        public string name; public int seat, score;
        public readonly List<string> hand = new List<string>();
    }

    public readonly List<Player> players = new List<Player>();
    public readonly List<LEvent> events = new List<LEvent>();
    public readonly List<string> log = new List<string>();
    public readonly int target;
    public LPhase phase = LPhase.Play;
    public int boss, round, lastWinner = -1;
    public string question;
    public int Blanks => Math.Max(1, CountBlanks(question));
    public readonly Dictionary<int, string[]> played = new Dictionary<int, string[]>();
    public readonly List<int> order = new List<int>();   // ordre de decouverte des reponses (melange)

    readonly Random rng;
    readonly List<string> qDeck;
    List<string> aDeck;
    int qi, ai;

    public int Actor => phase == LPhase.Play ? Quiz.Everyone : phase == LPhase.Judge ? boss : -1;
    public bool Finished => phase == LPhase.Over;
    public Player Boss => players[boss];

    public static int CountBlanks(string q) => q == null ? 0 : System.Text.RegularExpressions.Regex.Matches(q, "_+").Count;

    // La phrase complete : chaque trou remplace par une reponse (en gras pour l'interface).
    public static string Fill(string q, IList<string> answers, string open = "<b>", string close = "</b>")
    {
        int k = 0;
        return System.Text.RegularExpressions.Regex.Replace(q, "_+", _ =>
        {
            var a = k < answers.Count ? answers[k++] : "____";
            return open + a + close;
        });
    }

    public Limite(IList<string> names, int option, int seed)
    {
        target = Target(option);
        rng = new Random(seed);
        var deck = Cards((option & 1) != 0);
        qDeck = deck.q.OrderBy(_ => rng.Next()).ToList();
        aDeck = deck.a.OrderBy(_ => rng.Next()).ToList();
        for (int i = 0; i < names.Count; i++) players.Add(new Player { name = names[i], seat = i });
        foreach (var p in players) Refill(p);
        boss = rng.Next(players.Count);
        NewRound();
    }

    string DrawAnswer() { if (ai >= aDeck.Count) { ai = 0; aDeck = aDeck.OrderBy(_ => rng.Next()).ToList(); } return aDeck[ai++]; }
    void Refill(Player p) { while (p.hand.Count < Hand) p.hand.Add(DrawAnswer()); }

    void NewRound()
    {
        round++;
        if (qi >= qDeck.Count) qi = 0;
        question = qDeck[qi++];
        played.Clear(); order.Clear();
        phase = LPhase.Play;
        events.Add(new LEvent { type = LEv.Round, seat = boss, text = question });
        log.Add($"Manche {round} : {Boss.name} est le Boss.");
    }

    public bool TryApply(string[] a)
    {
        events.Clear();
        if (Finished || a.Length == 0) return false;
        switch (a[0])
        {
            case "play":   // play|siege|reponse1;reponse2 : cartes de la main
            {
                if (phase != LPhase.Play || a.Length < 3 || !int.TryParse(a[1], out int s) || s < 0 || s >= players.Count || s == boss || played.ContainsKey(s)) return false;
                var picks = a[2].Split(';');
                if (picks.Length != Blanks) return false;
                var p = players[s];
                var hand = p.hand.ToList();
                var texts = new string[picks.Length];
                for (int i = 0; i < picks.Length; i++)
                {
                    if (!hand.Remove(picks[i])) return false;   // uniquement les cartes de sa main
                    texts[i] = picks[i];
                }
                p.hand.Clear(); p.hand.AddRange(hand);
                played[s] = texts;
                events.Add(new LEvent { type = LEv.Played, seat = s });
                if (players.Count(x => x.seat != boss) == played.Count) StartJudge();
                return true;
            }
            case "pick":   // pick|siege : le Boss choisit la reponse de ce joueur
            {
                if (phase != LPhase.Judge || a.Length < 2 || !int.TryParse(a[1], out int w) || !played.ContainsKey(w)) return false;
                players[w].score++;
                lastWinner = w;
                phase = LPhase.Result;
                var full = Fill(question, played[w], "« ", " »");
                events.Add(new LEvent { type = LEv.Win, seat = w, text = full });
                log.Add($"{Boss.name} choisit {players[w].name} : {full}");
                return true;
            }
            case "next":   // l'hote, apres le resultat : la main passe au gagnant, tout le monde recomplete
            {
                if (phase != LPhase.Result) return false;
                if (players[lastWinner].score >= target)
                {
                    phase = LPhase.Over;
                    events.Add(new LEvent { type = LEv.Over, seat = lastWinner });
                    log.Add($"{players[lastWinner].name} gagne la partie !");
                    return true;
                }
                foreach (var p in players) Refill(p);
                boss = lastWinner;
                NewRound();
                return true;
            }
            case "timeout":   // l'hote : trop d'attente -> les absents jouent au hasard, ou le Boss choisit au hasard
            {
                if (phase == LPhase.Play)
                {
                    foreach (var p in players.Where(x => x.seat != boss && !played.ContainsKey(x.seat)).ToList())
                        TryApplyInner(new[] { "play", p.seat.ToString(), string.Join(";", RandomPick(p)) });
                    return true;
                }
                if (phase == LPhase.Judge) { TryApplyInner(new[] { "pick", order[rng.Next(order.Count)].ToString() }); return true; }
                return false;
            }
        }
        return false;
    }

    // Applique sans effacer les evenements deja produits (timeout = plusieurs actions d'un coup).
    void TryApplyInner(string[] a)
    {
        var keep = events.ToList();
        TryApply(a);
        keep.AddRange(events);
        events.Clear(); events.AddRange(keep);
    }

    void StartJudge()
    {
        phase = LPhase.Judge;
        order.Clear();
        order.AddRange(played.Keys.OrderBy(_ => rng.Next()));
        events.Add(new LEvent { type = LEv.Reveal, seat = boss });
        log.Add("Tout le monde a joué : le Boss découvre les réponses...");
    }

    List<string> RandomPick(Player p) => p.hand.OrderBy(_ => rng.Next()).Take(Blanks).ToList();

    // --- Bots (hors ligne, ou joueur parti) ---------------------------------------------------
    static readonly Random botRng = new Random();
    public string[] BotPlay(int seat)
    {
        var p = players[seat];
        return new[] { "play", seat.ToString(), string.Join(";", p.hand.OrderBy(_ => botRng.Next()).Take(Blanks)) };
    }
    public string[] Bot()
    {
        if (phase == LPhase.Judge && order.Count > 0) return new[] { "pick", order[botRng.Next(order.Count)].ToString() };
        return null;
    }
}
