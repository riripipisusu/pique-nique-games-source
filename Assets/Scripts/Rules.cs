using System;
using System.Collections.Generic;
using System.Linq;

// Port de server.js : logique pure, sans Unity, pour pouvoir tourner cote hote en ligne.
public enum Mode { Classique, Ameliore }

public class Card { public bool carrot; public int steps; public int turns; }

public class PlayerState
{
    public string name;
    public int color;
    public int[] rabbits = new int[Rules.RabbitsPerPlayer];
}

public struct Fall { public int player, rabbit, pos; }

public class CarrotResult
{
    public List<int> opened = new List<int>();
    public List<int> closed = new List<int>();
    public int index = -1, turns = 1;
    public List<Fall> fallen = new List<Fall>();
}

public class MoveResult { public int player, rabbit; public bool fell; public List<int> path = new List<int>(); }

public class Rules
{
    public const int Start = 0, RabbitsPerPlayer = 3, MaxPlayers = 4, CycleLength = 25, OuterRing = 15;

    public Mode mode;
    public int summit, holeMin;
    public List<PlayerState> players = new List<PlayerState>();
    public int turn;
    public Card drawn;
    public int winner = -1;
    public List<string> log = new List<string>();
    public int[] cycle;
    public int[] known;          // -1 = cran pas encore observe
    public int rotations;
    public int[] cycleStarts, cycleOrder;
    public int cycleStep;
    public List<int> open = new List<int>();   // trous ouverts jusqu'a la prochaine rotation

    readonly List<Card> deck = new List<Card>();
    readonly Random rng;

    public Rules(Mode mode, IEnumerable<string> names, int seed)
    {
        this.mode = mode;
        rng = new Random(seed);
        summit = mode == Mode.Classique ? 20 : 26;
        holeMin = mode == Mode.Classique ? 3 : 1;
        int i = 0;
        foreach (var n in names) players.Add(new PlayerState { name = n, color = i++ });
        BuildDeck();
        if (mode == Mode.Ameliore) BuildCycle();
        Log($"Partie en mode {(mode == Mode.Classique ? "Classique" : "Amélioré")}, au tour de {players[0].name}.");
    }

    public PlayerState Current => players[turn];
    public bool Over => winner >= 0;
    public int DeckLeft => deck.Count;

    void Log(string s) { log.Add(s); if (log.Count > 60) log.RemoveAt(0); }

    void Shuffle<T>(IList<T> a)
    {
        for (int i = a.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
    }

    void BuildDeck()
    {
        deck.Clear();
        int c1 = mode == Mode.Classique ? 10 : 6, c2 = mode == Mode.Classique ? 0 : 4;
        for (int i = 0; i < 14; i++) deck.Add(new Card { steps = 1 });
        for (int i = 0; i < 10; i++) deck.Add(new Card { steps = 2 });
        for (int i = 0; i < 6; i++) deck.Add(new Card { steps = 3 });
        for (int i = 0; i < c1; i++) deck.Add(new Card { carrot = true, turns = 1 });
        for (int i = 0; i < c2; i++) deck.Add(new Card { carrot = true, turns = 2 });
        Shuffle(deck);
    }

    // Pentagones 1-3 : couronne exterieure (1 case sur 3). 4-5 : interieure (1 sur 2).
    public static int CaseOfPentagon(int p, int j) => p <= 3 ? (j - 1) * 3 + p : 16 + (j - 1) * 2 + (p - 4);

    void BuildCycle()
    {
        cycleStarts = Enumerable.Range(0, 5).Select(_ => 1 + rng.Next(5)).ToArray();
        cycleStep = 1 + rng.Next(4);
        cycleOrder = new[] { 1, 2, 3, 4, 5 };
        Shuffle(cycleOrder);
        cycle = new int[CycleLength];
        for (int t = 0; t < CycleLength; t++)
        {
            int p = cycleOrder[t % 5];
            int j = ((cycleStarts[p - 1] - 1 + cycleStep * (t / 5)) % 5) + 1;
            cycle[t] = CaseOfPentagon(p, j);
        }
        known = Enumerable.Repeat(-1, CycleLength).ToArray();
    }

    public (int player, int rabbit)? Occupant(int pos)
    {
        if (pos == Start || pos == summit) return null;
        for (int p = 0; p < players.Count; p++)
            for (int r = 0; r < RabbitsPerPlayer; r++)
                if (players[p].rabbits[r] == pos) return (p, r);
        return null;
    }

    /// Pioche. Carte deplacement : reste en attente dans `drawn`. Carotte : resolue tout de suite.
    public CarrotResult Draw()
    {
        if (Over || drawn != null) return null;
        if (deck.Count == 0) { BuildDeck(); Log("La pioche est épuisée : on remélange les cartes."); }
        var card = deck[deck.Count - 1];
        deck.RemoveAt(deck.Count - 1);
        if (!card.carrot) { drawn = card; return null; }

        var res = new CarrotResult { turns = card.turns, closed = open };
        if (mode == Mode.Ameliore)
        {
            rotations += card.turns;
            res.index = rotations % CycleLength;
            res.opened.Add(cycle[res.index]);
            known[res.index] = cycle[res.index];
        }
        else
        {
            var holes = Enumerable.Range(holeMin, summit - holeMin).ToList();
            Shuffle(holes);
            res.opened.AddRange(holes.Take(1 + rng.Next(3)));
        }
        open = res.opened;
        foreach (var pos in res.opened)
        {
            var occ = Occupant(pos);
            if (occ == null) continue;
            players[occ.Value.player].rabbits[occ.Value.rabbit] = Start;
            res.fallen.Add(new Fall { player = occ.Value.player, rabbit = occ.Value.rabbit, pos = pos });
        }
        string quoi = card.turns == 2 ? "la double carotte" : "la carotte";
        Log($"{Current.name} tourne {quoi} : case {string.Join(", ", res.opened)}" + (res.fallen.Count == 0 ? "... personne ne tombe." : " s'ouvre !"));
        foreach (var f in res.fallen) Log($"{players[f.player].name} perd un lapin (case {f.pos}) !");
        NextTurn();
        return res;
    }

    public bool CanMove(int rabbit) => drawn != null && !Over && Current.rabbits[rabbit] != summit;

    public MoveResult Move(int rabbit)
    {
        if (!CanMove(rabbit)) return null;
        var p = Current;
        int steps = drawn.steps;
        drawn = null;
        var res = new MoveResult { player = turn, rabbit = rabbit };
        int from = p.rabbits[rabbit], target = from + steps;
        // Une case ne porte qu'un lapin : on glisse jusqu'a la premiere libre.
        while (target < summit && Occupant(target) != null) target++;
        target = Math.Min(target, summit);
        for (int i = from + 1; i <= target; i++) res.path.Add(i);
        if (open.Contains(target))
        {
            res.fell = true;
            p.rabbits[rabbit] = Start;
            Log($"{p.name} saute dans le trou de la case {target} !");
            NextTurn();
            return res;
        }
        p.rabbits[rabbit] = target;
        Log(target == summit ? $"{p.name} amène un lapin au potager !" : $"{p.name} avance un lapin de {steps} (case {target}).");
        if (p.rabbits.All(r => r == summit)) { winner = turn; Log($"{p.name} gagne la partie !"); }
        else NextTurn();
        return res;
    }

    void NextTurn() => turn = (turn + 1) % players.Count;
}
