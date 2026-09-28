using System;
using System.Collections.Generic;
using System.Linq;

// Serpents et echelles : chacun lance le de a son tour et avance d'autant (on part hors du plateau, case 0).
// Pied d'une echelle : on monte en haut ; tete d'un serpent : on glisse jusqu'a sa queue. Il faut tomber pile sur 100 :
// si le de est trop fort, on recule de l'excedent. Arriver sur une case deja occupee (apres echelle ou serpent) :
// on retourne au depart. Premier sur la case 100 : gagne. Deterministe (lancer en action,
// comme aux petits chevaux : "roll|valeur|lancer..." ou "roll" seul, valeur tiree ici).
public enum SEv { Rolled, Moved, Ladder, Snake, Bumped, Won, Turn }
public struct SerpEvent { public SEv type; public int seat, from, to, value; public int[] path; public float[] fling; }

public class Serpents : IMatch
{
    public const int End = 100, MaxPlayers = 4;
    // Plateau d'apres le modele fourni par l'utilisatrice : echelles (pied -> haut) et serpents (tete -> queue).
    public static readonly Dictionary<int, int> Ladders = new Dictionary<int, int> { [1] = 38, [4] = 14, [9] = 31, [21] = 42, [28] = 84, [51] = 67, [71] = 91, [80] = 100 };
    public static readonly Dictionary<int, int> Snakes = new Dictionary<int, int> { [17] = 7, [54] = 34, [62] = 19, [64] = 60, [87] = 24, [93] = 73, [95] = 75, [98] = 79 };

    public class Player { public string name; public int seat, pos; }
    public readonly List<Player> players = new List<Player>();
    public readonly List<SerpEvent> events = new List<SerpEvent>();
    public readonly List<string> log = new List<string>();
    public int turn, winner = -1, dice;
    readonly Random rng;

    public int Actor => Finished ? -1 : turn;
    public bool Finished => winner >= 0;
    public Player Current => players[turn];

    public Serpents(IList<string> names, int option, int seed)
    {
        rng = new Random(seed);
        for (int i = 0; i < names.Count; i++) players.Add(new Player { name = names[i], seat = i });
        events.Add(new SerpEvent { type = SEv.Turn, seat = 0 });
    }

    // Cases parcourues (rebond au bout compris).
    public static int[] Path(int from, int d)
    {
        var p = new List<int>();
        for (int k = 1; k <= d; k++) { int c = from + k; p.Add(c <= End ? c : 2 * End - c); }
        return p.ToArray();
    }

    public bool TryApply(string[] a)
    {
        events.Clear();
        if (Finished || a[0] != "roll") return false;
        float[] fling = null;
        if (a.Length >= 15)
        {
            fling = new float[13];
            for (int k = 0; k < 13; k++)
                if (!float.TryParse(a[k + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fling[k])) { fling = null; break; }
        }
        dice = a.Length > 1 && int.TryParse(a[1], out int v) && v >= 1 && v <= 6 ? v : rng.Next(1, 7);
        var p = Current;
        events.Add(new SerpEvent { type = SEv.Rolled, seat = turn, value = dice, fling = fling });
        var path = Path(p.pos, dice);
        int from = p.pos;
        p.pos = path[path.Length - 1];
        events.Add(new SerpEvent { type = SEv.Moved, seat = turn, from = from, to = p.pos, value = dice, path = path });
        string msg = $"{p.name} fait {dice}" + (from + dice > End ? $" : trop fort, il recule jusqu'à {p.pos}" : $" et va en {p.pos}");
        if (Ladders.TryGetValue(p.pos, out int up))
        {
            events.Add(new SerpEvent { type = SEv.Ladder, seat = turn, from = p.pos, to = up });
            msg += $", puis grimpe l'échelle jusqu'en {up} !";
            p.pos = up;
        }
        else if (Snakes.TryGetValue(p.pos, out int down))
        {
            events.Add(new SerpEvent { type = SEv.Snake, seat = turn, from = p.pos, to = down });
            msg += $"... et glisse sur un serpent jusqu'en {down} !";
            p.pos = down;
        }
        else msg += ".";
        if (p.pos > 0 && p.pos < End && players.Any(o => o != p && o.pos == p.pos))   // case deja occupee : retour au depart
        {
            events.Add(new SerpEvent { type = SEv.Bumped, seat = turn, from = p.pos, to = 0 });
            msg += $" Case {p.pos} déjà occupée : retour au départ !";
            p.pos = 0;
        }
        log.Add(msg);
        if (p.pos == End)
        {
            winner = turn;
            events.Add(new SerpEvent { type = SEv.Won, seat = turn });
            log.Add($"{p.name} arrive sur la case 100 et gagne !");
            return true;
        }
        turn = (turn + 1) % players.Count;
        events.Add(new SerpEvent { type = SEv.Turn, seat = turn });
        return true;
    }

    public string[] Bot() => new[] { "roll" };
}
