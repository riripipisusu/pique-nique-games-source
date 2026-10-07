using System;
using System.Collections.Generic;
using System.Linq;

// Paintball : deux equipes (siege pair = Orange, impair = Bleu). Une bille = touche.
// Deux modes (option : bit 2 = match a mort, bits 0-1 = objectif) :
//  - Manches (comme CS:GO, MR24) : touche = hors jeu jusqu'a la manche suivante. Une manche est gagnee en eliminant
//    toute l'equipe adverse, ou au bout du temps par l'equipe qui a le plus de survivants (egalite : personne).
//    Premiere equipe a 13 manches ; 12-12 = egalite.
//  - Match a mort en equipe : on revient a sa base ("spawn"), chaque touche = 1 point ; premiere equipe a 50 ou 100,
//    sinon la meilleure a la fin du temps.
// Les deplacements et les billes passent par un canal temps reel a part (non enregistre) ; ici, seulement ce qui compte.
// Avant la partie, chacun choisit son equipe ("team|siege|0/1", 5 par equipe au plus) ; "go" (hote) lance la partie,
// les indecis completant l'equipe la moins nombreuse.
// Actions : "hit|tireur|touche", "spawn|siege", "round" (manche suivante, hote), "timeout" (fin du temps de manche, hote),
// "end" (fin du temps de partie, hote).
public enum PbEv { Hit, Spawn, RoundEnd, Round, Over, Team, Go }
public struct PbEvent { public PbEv type; public int seat, by; }

public class Paintball : IMatch
{
    public const int MaxPlayers = 10, TeamMax = 5, RoundsToWin = 13, MaxRounds = 24;
    public const float RoundTime = 115, FreezeTime = 4, DeathmatchMinutes = 10;
    public static readonly int[] DmTargets = { 50, 100 };
    public static bool IsDeathmatch(int option) => (option & 4) != 0;
    // Maps (decors d'Agrou, amenages en arene : bits 4-6 de l'option).
    public static readonly (string id, string name)[] Maps = { ("PlaceDuVillage", "Place du village"), ("MapIlePirate", "Île pirate"), ("Cimetiere", "Cimetière") };
    public static int MapOf(int option) => Math.Min((option >> 4) & 7, Maps.Length - 1);
    public static int WithMap(int option, int map) => (option & ~(7 << 4)) | (map << 4);
    public static int Target(int option) => IsDeathmatch(option) ? DmTargets[Math.Min(option & 3, DmTargets.Length - 1)] : RoundsToWin;
    public static readonly string[] TeamName = { "Orange", "Bleu" };

    public class Player { public string name; public int seat, team, hits, outs; public bool down; }
    public readonly List<Player> players = new List<Player>();
    public readonly List<PbEvent> events = new List<PbEvent>();
    public readonly List<string> log = new List<string>();
    public readonly int[] score = new int[2];   // manches gagnees, ou touches en match a mort
    public readonly int target;
    public readonly bool deathmatch;
    public int round = 1;
    public bool roundOver;
    public int roundWinner = -1;   // -1 = manche nulle
    public readonly List<int> history = new List<int>();   // gagnant de chaque manche (-1 = nulle)
    public bool over, picking = true;
    public int winner = -1;        // equipe gagnante, -1 = egalite
    public int TeamOf(int seat) => players[seat].team;
    public int Count(int team) => players.Count(p => p.team == team);

    public int Actor => over ? -1 : Quiz.Everyone;
    public bool Finished => over;
    public bool Respawn => deathmatch;
    public int Alive(int team) => players.Count(p => p.team == team && !p.down);

    public Paintball(IList<string> names, int option, int seed)
    {
        deathmatch = IsDeathmatch(option);
        target = Target(option);
        for (int i = 0; i < names.Count; i++) players.Add(new Player { name = names[i], seat = i, team = -1 });
    }

    bool Seat(string s, out int v) => int.TryParse(s, out v) && v >= 0 && v < players.Count;

    public bool TryApply(string[] a)
    {
        events.Clear();
        if (over || a.Length == 0) return false;
        if (picking)
        {
            if (a[0] == "team" && a.Length > 2 && Seat(a[1], out int ts) && int.TryParse(a[2], out int tt) && tt >= 0 && tt <= 1)
            {
                if (players[ts].team == tt || Count(tt) >= TeamMax) return false;
                players[ts].team = tt;
                events.Add(new PbEvent { type = PbEv.Team, seat = ts, by = tt });
                return true;
            }
            if (a[0] != "go") return false;
            // Les indecis completent l'equipe la moins nombreuse.
            foreach (var p in players.Where(p => p.team < 0)) p.team = Count(0) <= Count(1) ? 0 : 1;
            picking = false;
            events.Add(new PbEvent { type = PbEv.Go });
            log.Add("Orange : " + string.Join(", ", players.Where(p => p.team == 0).Select(p => p.name)) + " — Bleu : " + string.Join(", ", players.Where(p => p.team == 1).Select(p => p.name)));
            return true;
        }
        switch (a[0])
        {
            case "hit":
            {
                if (a.Length < 3 || !Seat(a[1], out int s) || !Seat(a[2], out int v)) return false;
                var sh = players[s]; var vi = players[v];
                if (sh.team == vi.team || vi.down || sh.down || roundOver) return false;
                vi.down = true; vi.outs++; sh.hits++;
                events.Add(new PbEvent { type = PbEv.Hit, seat = v, by = s });
                log.Add($"{sh.name} touche {vi.name}.");
                if (deathmatch)
                {
                    score[sh.team]++;
                    if (score[sh.team] >= target) End(sh.team);
                }
                else if (Alive(vi.team) == 0) EndRound(sh.team);
                return true;
            }
            case "spawn":
                if (!deathmatch || a.Length < 2 || !Seat(a[1], out int sp) || !players[sp].down) return false;
                players[sp].down = false;
                events.Add(new PbEvent { type = PbEv.Spawn, seat = sp });
                return true;
            case "timeout":
                if (deathmatch || roundOver) return false;
                int a0 = Alive(0), a1 = Alive(1);
                EndRound(a0 == a1 ? -1 : a0 > a1 ? 0 : 1);
                return true;
            case "round":
                if (deathmatch || !roundOver) return false;
                round++; roundOver = false; roundWinner = -1;
                foreach (var p in players) p.down = false;
                events.Add(new PbEvent { type = PbEv.Round, seat = round });
                return true;
            case "end":
                End(score[0] == score[1] ? -1 : score[0] > score[1] ? 0 : 1);
                return true;
        }
        return false;
    }

    void EndRound(int team)
    {
        roundOver = true; roundWinner = team; history.Add(team);
        if (team >= 0) score[team]++;
        events.Add(new PbEvent { type = PbEv.RoundEnd, seat = team });
        log.Add(team < 0 ? $"Manche {round} : nulle." : $"Manche {round} : l'équipe {TeamName[team]} l'emporte ({score[0]} - {score[1]}).");
        if (team >= 0 && score[team] >= RoundsToWin) End(team);
        else if (score[0] + score[1] >= MaxRounds || round >= MaxRounds && score[0] == score[1]) End(score[0] == score[1] ? -1 : score[0] > score[1] ? 0 : 1);
    }

    void End(int team)
    {
        over = true; winner = team;
        events.Add(new PbEvent { type = PbEv.Over, seat = team });
        log.Add(team < 0 ? $"Égalité {score[0]} à {score[1]} !" : $"L'équipe {TeamName[team]} gagne {score[team]} à {score[1 - team]} !");
    }

    public string[] Bot() => null;   // les joueurs partis restent a terre : rien a jouer pour eux
}
