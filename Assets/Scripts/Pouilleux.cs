using System;
using System.Collections.Generic;
using System.Linq;

// Le pouilleux : jeu de 52 cartes sans le valet de trefle ; le valet de pique est le pouilleux (il n'a pas de paire).
// On distribue tout ; chacun jette ses paires (meme valeur et meme couleur : rouge ou noire). Puis, a son tour, on pioche
// une carte cachee dans la main de son voisin de gauche (le suivant qui a encore des cartes) ; une paire formee est
// jetee. Qui n'a plus de cartes est sorti ; le dernier, avec le pouilleux en main, a perdu.
// Deterministe : la carte prise est designee par sa place dans la main ("take|index") ; apres chaque prise, la main de
// celui qui a pioche est melangee (generateur a graine) : personne ne peut suivre le pouilleux des yeux.
public enum PEv { Deal, Pair, Take, Out, Lose }
public class PEvent { public PEv type; public int seat = -1, other = -1, card = -1, card2 = -1, index = -1; }

public class Pouilleux : IMatch
{
    public const int MaxPlayers = 6, Knave = 10, Pouilleu = 0 * 13 + Knave, Removed = 3 * 13 + Knave;   // valet de pique / de trefle
    public class Player { public string name; public int seat; public readonly List<int> hand = new List<int>(); public int outRank = -1; }

    public readonly List<Player> players = new List<Player>();
    public readonly List<PEvent> events = new List<PEvent>();
    public readonly List<string> log = new List<string>();
    public readonly List<int> discard = new List<int>();
    public int turn, loser = -1, outCount;
    readonly Random rng;

    public int Actor => Finished ? -1 : turn;
    public bool Finished => loser >= 0;
    public Player Current => players[turn];
    public static int Rank(int c) => c % 13;
    public static bool Red(int c) => c / 13 == 1 || c / 13 == 2;
    public static bool Pair(int a, int b) => a != b && Rank(a) == Rank(b) && Red(a) == Red(b);
    public static string Name(int c)
    {
        string[] r = { "As", "2", "3", "4", "5", "6", "7", "8", "9", "10", "Valet", "Dame", "Roi" }, s = { "pique", "carreau", "cœur", "trèfle" };
        return $"{r[Rank(c)]} de {s[c / 13]}";
    }

    public Pouilleux(IList<string> names, int option, int seed)
    {
        rng = new Random(seed);
        for (int i = 0; i < names.Count; i++) players.Add(new Player { name = names[i], seat = i });
        var deck = Enumerable.Range(0, 52).Where(c => c != Removed).OrderBy(_ => rng.Next()).ToList();
        for (int i = 0; i < deck.Count; i++) players[i % players.Count].hand.Add(deck[i]);
        events.Add(new PEvent { type = PEv.Deal });
        foreach (var p in players) DropPairs(p);
        log.Add("Chacun jette ses paires. Gare au valet de pique !");
        foreach (var p in players) CheckOut(p);
        turn = NextWithCards(-1);
        if (players.Count(q => q.hand.Count > 0) <= 1) End();
    }

    // Jette toutes les paires de la main (au debut) : premiere carte avec sa premiere jumelle.
    void DropPairs(Player p)
    {
        for (int i = 0; i < p.hand.Count; i++)
            for (int j = i + 1; j < p.hand.Count; j++)
                if (Pair(p.hand[i], p.hand[j]))
                {
                    int a = p.hand[i], b = p.hand[j];
                    p.hand.RemoveAt(j); p.hand.RemoveAt(i); discard.Add(a); discard.Add(b);
                    events.Add(new PEvent { type = PEv.Pair, seat = p.seat, card = a, card2 = b });
                    i--; break;
                }
    }

    void CheckOut(Player p)
    {
        if (p.hand.Count > 0 || p.outRank >= 0) return;
        p.outRank = outCount++;
        events.Add(new PEvent { type = PEv.Out, seat = p.seat });
        log.Add($"{p.name} n'a plus de cartes : il est tranquille !");
    }

    // Joueur suivant (sens des aiguilles) qui a encore des cartes.
    int NextWithCards(int from)
    {
        for (int k = 1; k <= players.Count; k++)
        {
            int s = ((from + k) % players.Count + players.Count) % players.Count;
            if (players[s].hand.Count > 0) return s;
        }
        return -1;
    }
    public int Target => NextWithCards(turn);   // chez qui le joueur actif pioche

    void End()
    {
        var last = players.FirstOrDefault(q => q.hand.Count > 0);
        loser = last != null ? last.seat : players.Count - 1;
        events.Add(new PEvent { type = PEv.Lose, seat = loser });
        log.Add($"{players[loser].name} garde le valet de pique : c'est le POUILLEUX !");
    }

    public bool TryApply(string[] a)
    {
        events.Clear();
        if (Finished || a[0] != "take" || a.Length < 2 || !int.TryParse(a[1], out int idx)) return false;
        int t = Target;
        if (t < 0 || t == turn || idx < 0 || idx >= players[t].hand.Count) return false;
        var me = Current; var from = players[t];
        int card = from.hand[idx];
        from.hand.RemoveAt(idx);
        events.Add(new PEvent { type = PEv.Take, seat = turn, other = t, card = card, index = idx });
        log.Add($"{me.name} pioche une carte chez {from.name}.");
        int twin = me.hand.FindIndex(c => Pair(c, card));
        if (twin >= 0)
        {
            int b = me.hand[twin]; me.hand.RemoveAt(twin); discard.Add(card); discard.Add(b);
            events.Add(new PEvent { type = PEv.Pair, seat = turn, card = card, card2 = b });
            log.Add($"{me.name} forme une paire et la jette.");
        }
        else me.hand.Add(card);
        // La main de celui qui a pioche est melangee : on ne peut pas suivre une carte des yeux.
        for (int i = me.hand.Count - 1; i > 0; i--) { int k = rng.Next(i + 1); (me.hand[i], me.hand[k]) = (me.hand[k], me.hand[i]); }
        CheckOut(from); CheckOut(me);
        if (players.Count(q => q.hand.Count > 0) <= 1) { End(); return true; }
        turn = NextWithCards(turn);
        return true;
    }

    // Bot : une carte au hasard (generateur a part : le tirage des regles doit rester le meme sur tous les PC).
    static readonly Random botRng = new Random();
    public string[] Bot() => Finished || Target < 0 ? null : new[] { "take", botRng.Next(players[Target].hand.Count).ToString() };
}
