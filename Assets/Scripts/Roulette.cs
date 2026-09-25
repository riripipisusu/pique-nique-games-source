using System;
using System.Collections.Generic;
using System.Linq;

// Roulette francaise (37 cases, un seul zero). Logique pure et deterministe (graine), comme Blackjack.
// Mises : plein 35:1, cheval 17:1, transversale 11:1, carre 8:1, sixain 5:1, douzaine / colonne 2:1,
// chances simples 1:1. Sur le zero, les chances simples vont en prison (regle des casinos francais) :
// rendues si la chance sort au coup suivant, perdues sinon ; un nouveau zero les enferme un cran de plus.
public enum RPhase { Bet, GameOver }

public class RBet
{
    public string key;      // "P:17", "C:14-17", "T:4", "Q:13", "S:2", "D:1", "L:0", "R", "N", "PA", "IM", "MA", "PS"
    public int amount;
    public int prison;      // chances simples : nombre de zeros a racheter
}

public class RPlayer
{
    public string name;
    public int seat, chips = Roulette.StartChips;
    public bool broke;
    public List<RBet> bets = new List<RBet>();       // enjeux sur le tapis pour ce coup
    public List<RBet> prison = new List<RBet>();     // enjeux en prison, rejoues d'office
    public string lastBets = "";
}

public enum REv { RoundStart, Bets, Spin, Result, Prison, Broke, GameOver }

public class REvent
{
    public REv type;
    public int seat = -1, amount, number = -1;
    public string text;
}

public class Roulette : IMatch
{
    public const int StartChips = 1000, MinBet = 10;
    public static readonly int[] Wheel = { 0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26 };
    static readonly HashSet<int> Reds = new HashSet<int> { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };
    public static readonly string[] Simple = { "MA", "PA", "R", "N", "IM", "PS" };

    public readonly List<RPlayer> players = new List<RPlayer>();
    public readonly List<int> history = new List<int>();
    public readonly List<string> log = new List<string>();
    public readonly List<REvent> events = new List<REvent>();
    public RPhase phase;
    public int round, rounds;

    readonly Random rng;
    int actor = -1;

    public Roulette(IEnumerable<string> names, int rounds, int seed)
    {
        rng = new Random(seed);
        this.rounds = rounds;
        int s = 0;
        foreach (var n in names) players.Add(new RPlayer { name = n, seat = s++ });
        StartRound();
    }

    public static bool IsRed(int n) => Reds.Contains(n);
    public static string Color(int n) => n == 0 ? "vert" : IsRed(n) ? "rouge" : "noir";
    public static string Describe(int n) => n == 0 ? "0, zéro" : $"{n} {Color(n)}, {(n % 2 == 0 ? "pair" : "impair")}, {(n <= 18 ? "manque" : "passe")}";

    // Numeros couverts par une mise, ou null si la mise n'existe pas.
    public static int[] Numbers(string key)
    {
        var p = key.Split(':');
        int Arg() => p.Length > 1 && int.TryParse(p[1], out int v) ? v : -1;
        switch (p[0])
        {
            case "P": { int n = Arg(); return n >= 0 && n <= 36 ? new[] { n } : null; }
            case "C":
            {
                var ab = p.Length > 1 ? p[1].Split('-') : new string[0];
                if (ab.Length != 2 || !int.TryParse(ab[0], out int a) || !int.TryParse(ab[1], out int b)) return null;
                if (a > b) (a, b) = (b, a);
                bool ok = a == 0 ? b >= 1 && b <= 3 : b <= 36 && (b - a == 3 || (b - a == 1 && a % 3 != 0));
                return ok ? new[] { a, b } : null;
            }
            case "T":
                if (p.Length > 1 && p[1] == "0a") return new[] { 0, 1, 2 };
                if (p.Length > 1 && p[1] == "0b") return new[] { 0, 2, 3 };
                { int r = Arg(); return r >= 0 && r <= 11 ? new[] { 3 * r + 1, 3 * r + 2, 3 * r + 3 } : null; }
            case "Q":
            {
                int a = Arg();
                if (a == 0) return new[] { 0, 1, 2, 3 };
                return a >= 1 && a <= 32 && a % 3 != 0 ? new[] { a, a + 1, a + 3, a + 4 } : null;
            }
            case "S": { int r = Arg(); return r >= 0 && r <= 10 ? Enumerable.Range(3 * r + 1, 6).ToArray() : null; }
            case "D": { int k = Arg(); return k >= 0 && k <= 2 ? Enumerable.Range(12 * k + 1, 12).ToArray() : null; }
            case "L": { int k = Arg(); return k >= 0 && k <= 2 ? Enumerable.Range(0, 12).Select(i => 3 * i + k + 1).ToArray() : null; }
            case "R": return Enumerable.Range(1, 36).Where(IsRed).ToArray();
            case "N": return Enumerable.Range(1, 36).Where(n => !IsRed(n)).ToArray();
            case "PA": return Enumerable.Range(1, 36).Where(n => n % 2 == 0).ToArray();
            case "IM": return Enumerable.Range(1, 36).Where(n => n % 2 == 1).ToArray();
            case "MA": return Enumerable.Range(1, 18).ToArray();
            case "PS": return Enumerable.Range(19, 18).ToArray();
        }
        return null;
    }

    public static bool IsSimple(string key) => Array.IndexOf(Simple, key) >= 0;
    public static int Payout(string key) => IsSimple(key) ? 1 : 36 / Numbers(key).Length - 1;

    public static string Label(string key)
    {
        var p = key.Split(':');
        switch (p[0])
        {
            case "P": return "plein " + p[1];
            case "C": return "cheval " + p[1];
            case "T": return "transversale";
            case "Q": return "carré";
            case "S": return "sixain";
            case "D": return new[] { "12 premiers", "12 milieu", "12 derniers" }[int.Parse(p[1])];
            case "L": return "colonne " + (int.Parse(p[1]) + 1);
            case "R": return "rouge";
            case "N": return "noir";
            case "PA": return "pair";
            case "IM": return "impair";
            case "MA": return "manque";
            default: return "passe";
        }
    }

    // "R:0:50;P:17:10" -> mises (null si une mise est invalide). Le ":0" des chances simples est ignore.
    public static List<RBet> Parse(string s)
    {
        var list = new List<RBet>();
        if (string.IsNullOrEmpty(s)) return list;
        foreach (var part in s.Split(';'))
        {
            int cut = part.LastIndexOf(':');
            if (cut <= 0 || !int.TryParse(part.Substring(cut + 1), out int amt) || amt < MinBet || amt % MinBet != 0) return null;
            string key = part.Substring(0, cut);
            if (key.EndsWith(":-")) key = key.Substring(0, key.Length - 2);
            if (Numbers(key) == null) return null;
            var same = list.FirstOrDefault(b => b.key == key);
            if (same != null) same.amount += amt; else list.Add(new RBet { key = key, amount = amt });
        }
        return list;
    }

    public static string Format(IEnumerable<RBet> bets) => string.Join(";", bets.Select(b => $"{(IsSimple(b.key) ? b.key + ":-" : b.key)}:{b.amount}"));

    void Emit(REv t, int seat = -1, int amount = 0, int number = -1, string text = null) =>
        events.Add(new REvent { type = t, seat = seat, amount = amount, number = number, text = text });

    void Log(string s) { log.Add(s); if (log.Count > 60) log.RemoveAt(0); }

    // --- Deroulement ------------------------------------------------------------------
    public int Actor => actor;
    public bool Finished => phase == RPhase.GameOver;
    public RPlayer Current => actor >= 0 ? players[actor] : null;
    IEnumerable<RPlayer> Active => players.Where(p => !p.broke);

    void StartRound()
    {
        round++;
        if (round > rounds || !Active.Any()) { EndGame(); return; }
        foreach (var p in players) p.bets.Clear();
        phase = RPhase.Bet;
        Emit(REv.RoundStart, amount: round, text: $"Coup {round}/{rounds}");
        Log($"--- Coup {round}/{rounds} : faites vos jeux ! ---");
        actor = Active.First().seat;
    }

    void EndGame()
    {
        phase = RPhase.GameOver;
        actor = -1;
        // Les enjeux encore en prison reviennent a leurs joueurs.
        foreach (var p in players) { p.chips += p.prison.Sum(b => b.amount); p.prison.Clear(); }
        int best = players.Max(p => p.chips);
        var winners = players.Where(p => p.chips == best).Select(p => p.name);
        Emit(REv.GameOver, text: string.Join(" et ", winners));
        Log($"Fin de la partie ! {string.Join(" et ", winners)} gagne avec {best} jetons.");
    }

    public bool TryApply(string[] a)
    {
        events.Clear();
        if (phase != RPhase.Bet || actor < 0 || a[0] != "bets") return false;
        var p = players[actor];
        var bets = Parse(a.Length > 1 ? a[1] : "");
        if (bets == null || bets.Sum(b => b.amount) > p.chips) return false;
        p.chips -= bets.Sum(b => b.amount);
        p.bets = bets;
        if (bets.Count > 0) p.lastBets = Format(bets);
        Emit(REv.Bets, p.seat, bets.Sum(b => b.amount), text: Format(bets));
        Log(bets.Count == 0 ? $"{p.name} passe ce coup." : $"{p.name} mise {bets.Sum(b => b.amount)} ({string.Join(", ", bets.Select(b => Label(b.key)))}).");
        actor = Active.Where(o => o.seat > p.seat).Select(o => o.seat).DefaultIfEmpty(-1).First();
        if (actor < 0) Spin();
        return true;
    }

    void Spin()
    {
        actor = -1;
        int n = rng.Next(37);
        history.Add(n);
        Emit(REv.Spin, number: n, text: Describe(n));
        Log($"Rien ne va plus... le {Describe(n)} !");
        foreach (var p in players.Where(o => !o.broke))
        {
            int won = 0, lost = 0, freed = 0;
            foreach (var b in p.bets)
            {
                if (IsSimple(b.key) && n == 0) { b.prison = 1; p.prison.Add(b); continue; }
                if (Numbers(b.key).Contains(n)) won += b.amount * (Payout(b.key) + 1);
                else lost += b.amount;
            }
            foreach (var b in p.prison.ToList())
            {
                if (p.bets.Contains(b)) continue;                       // vient d'entrer en prison
                if (n == 0) { b.prison++; continue; }
                p.prison.Remove(b);
                if (!Numbers(b.key).Contains(n)) { lost += b.amount; continue; }
                if (--b.prison == 0) freed += b.amount; else p.prison.Add(b);
            }
            p.chips += won + freed;
            int staked = p.bets.Sum(b => b.amount);
            int jailed = p.bets.Where(b => b.prison > 0).Sum(b => b.amount);
            if (staked > 0 || freed > 0 || lost > 0)
            {
                string txt = won + freed > 0 ? $"+{won + freed}" : jailed > 0 ? "En prison" : "Perdu";
                Emit(REv.Result, p.seat, won + freed, text: txt);
                Log(won > 0 ? $"{p.name} gagne {won} jetons !" : freed > 0 ? $"{p.name} sort {freed} jetons de prison." : jailed > 0 ? $"{p.name} : {jailed} jetons en prison." : $"{p.name} perd sa mise.");
            }
            if (jailed > 0) Emit(REv.Prison, p.seat, jailed);
            if (p.chips < MinBet && p.prison.Count == 0 && !p.broke)
            {
                p.broke = true;
                Emit(REv.Broke, p.seat);
                Log($"{p.name} est ruiné et quitte la table.");
            }
        }
        StartRound();
    }

    public string[] Bot()
    {
        var p = Current;
        if (p == null) return null;
        // Hasard propre au bot : le tirage de la partie ne doit pas avancer, sinon les PC en ligne divergent.
        var r = new Random(round * 31 + p.seat);
        var pick = new[] { "R", "N", "PA", "IM", "MA", "PS" }[r.Next(6)];
        int amt = Math.Min(p.chips / MinBet * MinBet, MinBet * (1 + r.Next(5)));
        return amt < MinBet ? new[] { "bets", "" } : new[] { "bets", $"{pick}:-:{amt}" };
    }
}
