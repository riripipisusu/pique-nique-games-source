using System;
using System.Collections.Generic;
using System.Linq;

// Paintball : deux equipes (siege pair = Orange, impair = Bleu). Une bille = eliminé ; le joueur touche revient a sa base
// ("spawn" envoye par sa propre machine apres quelques secondes). Chaque touche donne un point a l'equipe du tireur ;
// premiere equipe au score vise, ou la meilleure a la fin du temps ("end", envoye par l'hote).
// Les deplacements et les billes passent par un canal temps reel a part (non enregistre) ; ici, seulement ce qui compte.
// Actions : "hit|tireur|touche", "spawn|siege", "end".
public enum PbEv { Hit, Spawn, Over }
public struct PbEvent { public PbEv type; public int seat, by; }

public class Paintball : IMatch
{
    public const int MaxPlayers = 10, Minutes = 5;
    public static readonly int[] Targets = { 10, 20, 30 };
    public static int Target(int option) => Targets[Math.Min(option & 3, Targets.Length - 1)];
    public static readonly string[] TeamName = { "Orange", "Bleu" };

    public class Player { public string name; public int seat, team, hits, outs; public bool down; }
    public readonly List<Player> players = new List<Player>();
    public readonly List<PbEvent> events = new List<PbEvent>();
    public readonly List<string> log = new List<string>();
    public readonly int[] score = new int[2];
    public readonly int target;
    public bool over;
    public int winner = -1;   // equipe gagnante, -1 = egalite

    public int Actor => over ? -1 : Quiz.Everyone;
    public bool Finished => over;
    public static int TeamOf(int seat) => seat % 2;

    public Paintball(IList<string> names, int option, int seed)
    {
        target = Target(option);
        for (int i = 0; i < names.Count; i++) players.Add(new Player { name = names[i], seat = i, team = TeamOf(i) });
    }

    bool Seat(string s, out int v) => int.TryParse(s, out v) && v >= 0 && v < players.Count;

    public bool TryApply(string[] a)
    {
        events.Clear();
        if (over || a.Length == 0) return false;
        switch (a[0])
        {
            case "hit":
            {
                if (a.Length < 3 || !Seat(a[1], out int s) || !Seat(a[2], out int v)) return false;
                var sh = players[s]; var vi = players[v];
                if (sh.team == vi.team || vi.down || sh.down) return false;
                vi.down = true; vi.outs++; sh.hits++;
                score[sh.team]++;
                events.Add(new PbEvent { type = PbEv.Hit, seat = v, by = s });
                log.Add($"{sh.name} touche {vi.name}.");
                if (score[sh.team] >= target) End(sh.team);
                return true;
            }
            case "spawn":
                if (a.Length < 2 || !Seat(a[1], out int sp) || !players[sp].down) return false;
                players[sp].down = false;
                events.Add(new PbEvent { type = PbEv.Spawn, seat = sp });
                return true;
            case "end":
                End(score[0] == score[1] ? -1 : score[0] > score[1] ? 0 : 1);
                return true;
        }
        return false;
    }

    void End(int team)
    {
        over = true; winner = team;
        events.Add(new PbEvent { type = PbEv.Over, seat = team });
        log.Add(team < 0 ? $"Égalité {score[0]} à {score[1]} !" : $"L'équipe {TeamName[team]} gagne {score[team]} à {score[1 - team]} !");
    }

    public string[] Bot() => null;   // les joueurs partis restent a terre : rien a jouer pour eux
}
