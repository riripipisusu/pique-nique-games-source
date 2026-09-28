using System;
using System.Collections.Generic;
using System.Linq;

// Petits chevaux, regle officielle. Deterministe (de tire par un generateur a graine) : l'hote et les invites
// rejouent les memes actions et obtiennent la meme partie.
//  - Il faut un 6 pour sortir un cheval de l'ecurie ; un 6 fait rejouer. Trois 6 de suite : le joueur renvoie a
//    l'ecurie un de ses chevaux en jeu (au choix) et passe la main.
//  - 56 cases en croix, dans le sens des aiguilles d'une montre. Tomber pile sur un adversaire le renvoie a son
//    ecurie ; on ne peut pas passer par-dessus un adversaire. Arriver sur (ou passer) un cheval de sa couleur :
//    on s'arrete juste derriere.
//  - Compte exact pour s'arreter au pied de son escalier : si le de est trop fort, on va jusqu'au pied puis on recule
//    de l'excedent. Ensuite 1 pour la 1re marche, 2 pour la 2e... 6 pour la 6e, et un 6 pour arriver a la coupe.
// Progression d'un cheval : -1 ecurie, 0..55 sur le parcours (0 = case de depart, 55 = pied de l'escalier),
// 56..61 marches 1 a 6, 62 arrive.
public enum CEv { Rolled, Moved, Captured, NoMove, Turn, Won, Sacrifice }
public struct ChevEvent { public CEv type; public int seat, horse, from, to, value; public int[] path; public float[] fling; }

public class Chevaux : IMatch
{
    public const int Track = 56, Foot = 55, Home = 62, MaxPlayers = 4;
    public class Player { public string name; public int seat, arm; public int[] horses; public int Home => horses.Count(h => h == Chevaux.Home); }

    public readonly List<Player> players = new List<Player>();
    public readonly List<ChevEvent> events = new List<ChevEvent>();
    public readonly List<string> log = new List<string>();
    public readonly int count;      // chevaux par joueur (choisi par l'hote) : il faut tous les rentrer
    public int turn, dice;          // dice : dernier lancer ; 0 = il faut lancer
    public int sixes;               // 6 d'affilee du joueur en cours
    public bool sacrifice;          // trois 6 : le joueur doit renvoyer un de ses chevaux a l'ecurie
    public int winner = -1;
    readonly Random rng;

    public int Actor => Finished ? -1 : turn;
    public bool Finished => winner >= 0;
    public Player Current => players[turn];

    // Option : 0 = 4 chevaux par joueur, 1 = 3, 2 = 2, 3 = 1.
    public static int Count(int option) => 4 - Math.Max(0, Math.Min(3, option));

    // Bras de la croix (0 haut, 1 droite, 2 bas, 3 gauche, sens horaire) : le joueur 1 en bas face a la camera,
    // les suivants dans le sens du jeu ; a deux, face a face.
    public static int Arm(int seat, int count) => count == 2 ? (seat == 0 ? 2 : 0) : (2 + seat) % 4;
    // Couleur (index de Board.Colors) peinte sur chaque bras du plateau : haut vert, droite jaune, bas rouge, gauche bleu.
    public static readonly int[] ArmColor = { 2, 3, 0, 1 };
    public int ColorOf(int seat) => ArmColor[players[seat].arm];

    public Chevaux(IList<string> names, int option, int seed)
    {
        rng = new Random(seed);
        count = Count(option);
        for (int i = 0; i < names.Count; i++) players.Add(new Player { name = names[i], seat = i, arm = Arm(i, names.Count), horses = Enumerable.Repeat(-1, count).ToArray() });
        events.Add(new ChevEvent { type = CEv.Turn, seat = 0 });
    }

    // Case du parcours (0..55, indexee depuis le depart du bras du haut) ou se trouve un cheval, -1 hors parcours.
    public int Cell(int seat, int progress) => progress >= 0 && progress <= Foot ? (players[seat].arm * 14 + progress) % Track : -1;
    bool Enemy(int seat, int cell) => cell >= 0 && players.Any(o => o.seat != seat && o.horses.Any(x => Cell(o.seat, x) == cell));
    bool Own(int seat, int horse, int progress) => Enumerable.Range(0, count).Any(k => k != horse && players[seat].horses[k] == progress);

    // Chemin case par case (progressions successives) de ce cheval avec ce de ; null : coup impossible.
    public int[] Path(int seat, int horse, int d)
    {
        int h = players[seat].horses[horse];
        if (h == Home) return null;
        if (h < 0) return d == 6 && !Own(seat, horse, 0) ? new[] { 0 } : null;                    // sortie sur la case de depart
        if (h >= Foot) return d == Math.Min(6, h - Foot + 1) && !Own(seat, horse, h + 1) ? new[] { h + 1 } : null;   // marches
        var path = new List<int>();
        for (int k = h + 1; k <= Math.Min(h + d, Foot); k++) path.Add(k);
        for (int k = Foot - 1; k >= 2 * Foot - (h + d); k--) path.Add(k);                           // rebond au pied de l'escalier
        for (int i = 0; i < path.Count; i++)
        {
            if (Own(seat, horse, path[i])) return i == 0 ? null : path.Take(i).ToArray();          // on s'arrete juste derriere
            if (i < path.Count - 1 && Enemy(seat, Cell(seat, path[i]))) return null;                // pas par-dessus un adversaire
        }
        return path.ToArray();
    }
    public int Target(int seat, int horse, int d) => Path(seat, horse, d) is int[] p ? p[p.Length - 1] : -2;

    public bool CanMove(int horse) => dice > 0 && !sacrifice && !Finished && Path(turn, horse, dice) != null;
    public bool AnyMove => Enumerable.Range(0, count).Any(CanMove);
    // Cheval qu'on peut designer : pour avancer, ou (trois 6) pour le renvoyer a l'ecurie.
    public bool CanPick(int horse) => sacrifice ? OnBoard(Current.horses[horse]) : CanMove(horse);
    static bool OnBoard(int h) => h >= 0 && h < Home;

    public bool TryApply(string[] a)
    {
        events.Clear();
        if (Finished) return false;
        if (a[0] == "roll")
        {
            if (dice != 0 || sacrifice) return false;
            // "roll|valeur|lancer..." : le de a roule chez le joueur (valeur et trajectoire pour que tous voient le meme
            // lancer) ; "roll" seul (bot, joueur parti) : valeur tiree ici.
            float[] fling = null;
            if (a.Length >= 15)
            {
                fling = new float[13];
                for (int k = 0; k < 13; k++)
                    if (!float.TryParse(a[k + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fling[k])) { fling = null; break; }
            }
            dice = a.Length > 1 && int.TryParse(a[1], out int v) && v >= 1 && v <= 6 ? v : rng.Next(1, 7);
            events.Add(new ChevEvent { type = CEv.Rolled, seat = turn, value = dice, fling = fling });
            if (dice == 6 && ++sixes == 3)
            {
                if (Current.horses.Any(OnBoard))
                {
                    sacrifice = true;
                    events.Add(new ChevEvent { type = CEv.Sacrifice, seat = turn });
                    log.Add($"{Current.name} fait trois 6 de suite : un de ses chevaux doit retourner à l'écurie.");
                }
                else { log.Add($"{Current.name} fait trois 6 de suite : la main passe."); NextPlayer(); }
                return true;
            }
            if (!AnyMove)
            {
                events.Add(new ChevEvent { type = CEv.NoMove, seat = turn, value = dice });
                log.Add($"{Current.name} fait {dice} : aucun cheval ne peut bouger.");
                EndMove();
            }
            return true;
        }
        if (a.Length < 2 || !int.TryParse(a[1], out int horse) || horse < 0 || horse >= count) return false;
        var p = Current;
        if (a[0] == "sacrifice")
        {
            if (!sacrifice || !OnBoard(p.horses[horse])) return false;
            events.Add(new ChevEvent { type = CEv.Captured, seat = turn, horse = horse, from = p.horses[horse], to = -1 });
            p.horses[horse] = -1;
            log.Add($"{p.name} renvoie un de ses chevaux à l'écurie.");
            sacrifice = false;
            NextPlayer();
            return true;
        }
        if (a[0] != "move" || !CanMove(horse)) return false;
        int from = p.horses[horse];
        var path = Path(turn, horse, dice);
        int to = path[path.Length - 1];
        p.horses[horse] = to;
        events.Add(new ChevEvent { type = CEv.Moved, seat = turn, horse = horse, from = from, to = to, value = dice, path = path });
        log.Add(from < 0 ? $"{p.name} sort un cheval." : to == Home ? $"{p.name} rentre un cheval à la coupe !" : to > Foot ? $"{p.name} monte à la marche {to - Foot}."
              : to < Foot && path.Contains(Foot) ? $"{p.name} fait {dice} : trop fort, son cheval rebondit au pied de l'escalier."
              : path.Length < dice ? $"{p.name} fait {dice} et s'arrête derrière son autre cheval." : $"{p.name} avance de {dice}.");
        int cell = Cell(turn, to);
        if (cell >= 0)
            foreach (var o in players.Where(o => o != p))
                for (int k = 0; k < count; k++)
                    if (Cell(o.seat, o.horses[k]) == cell)
                    {
                        events.Add(new ChevEvent { type = CEv.Captured, seat = o.seat, horse = k, from = o.horses[k], to = -1 });
                        log.Add($"{p.name} renvoie un cheval de {o.name} à l'écurie !");
                        o.horses[k] = -1;
                    }
        if (p.Home == count)
        {
            winner = turn;
            events.Add(new ChevEvent { type = CEv.Won, seat = turn });
            log.Add($"{p.name} gagne la course !");
            return true;
        }
        EndMove();
        return true;
    }

    // Un 6 fait rejouer ; sinon au suivant.
    void EndMove()
    {
        if (dice == 6) { dice = 0; events.Add(new ChevEvent { type = CEv.Turn, seat = turn }); }
        else NextPlayer();
    }

    void NextPlayer()
    {
        turn = (turn + 1) % players.Count;
        dice = 0;
        sixes = 0;
        events.Add(new ChevEvent { type = CEv.Turn, seat = turn });
    }

    // Joueur automatique : manger, rentrer, sortir, sinon le cheval le plus avance ; trois 6 : sacrifie le moins avance.
    public string[] Bot()
    {
        if (sacrifice) return new[] { "sacrifice", Enumerable.Range(0, count).Where(k => OnBoard(Current.horses[k])).OrderBy(k => Current.horses[k]).First().ToString() };
        if (dice == 0) return new[] { "roll" };
        int best = -1, score = int.MinValue;
        for (int k = 0; k < count; k++)
        {
            int to = Target(turn, k, dice);
            if (to == -2) continue;
            int s = to;
            if (Enemy(turn, Cell(turn, to))) s += 200;
            if (Current.horses[k] < 0) s += 100;
            if (s > score) { score = s; best = k; }
        }
        return new[] { "move", best.ToString() };
    }
}
