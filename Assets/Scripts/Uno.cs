using System;
using System.Collections.Generic;
using System.Linq;

// Uno (regles officielles, cf. joueclub.fr) : 108 cartes, 7 par joueur, meme couleur ou meme symbole,
// +2 / inversion / passe / joker / +4, piocher une carte si on ne peut (ou ne veut) pas jouer, "UNO" a une carte
// sinon 2 cartes de penalite si un autre le remarque, points des cartes des adversaires, premier a 200 ou 500 points.
// Comme le UNO d'Ubisoft : contestation du +4 (on peut denoncer un +4 pose alors qu'on avait la couleur demandee),
// et regles maison en option (cumul, 7-0, intervention, piocher jusqu'a pouvoir jouer, jeu force).
// Logique pure et deterministe (graine partagee) : en ligne, chacun rejoue les memes actions.
public enum UPhase { Play, Drawn, Challenge, SwapPick, RoundOver, GameOver }
public enum UEv { Deal, Played, Drew, Skipped, Reversed, Uno, Caught, RoundOver, GameOver, Challenge, ChallengeResult, Swapped, Rotated, JumpIn }

public class UEvent { public UEv type; public int seat = -1, card = -1, count, other = -1; }

public class UPlayer
{
    public string name;
    public int seat, score, lastGain;
    public readonly List<int> hand = new List<int>();
    public bool said;   // a annonce "UNO"
}

public class Uno : IMatch
{
    public const int MaxPlayers = 10, HandSize = 7;
    public const int Skip = 10, Reverse = 11, Draw2 = 12, Wild = 13, Wild4 = 14, WildColor = 4;
    static readonly string[] ColorCodes = { "r", "y", "g", "b" };
    public static readonly string[] ColorNames = { "Rouge", "Jaune", "Vert", "Bleu" };

    // Options (bits) : 0-1 duree (une manche / 200 / 500 points), 2 cumul des +2/+4, 3 regle du 7-0, 4 intervention,
    // 5 piocher jusqu'a pouvoir jouer, 6 jeu force (carte piochee jouable = on la pose).
    public const int OptStacking = 4, OptSevenZero = 8, OptJumpIn = 16, OptDrawMatch = 32, OptForcePlay = 64;

    // Les 108 cartes : par couleur un 0, deux de chaque 1-9, deux passe, deux inversion, deux +2 ; 4 jokers, 4 +4.
    static readonly (int color, int kind)[] Deck = BuildDeck();
    static (int, int)[] BuildDeck()
    {
        var d = new List<(int, int)>();
        for (int c = 0; c < 4; c++)
        {
            d.Add((c, 0));
            for (int k = 1; k <= 12; k++) { d.Add((c, k)); d.Add((c, k)); }
        }
        for (int i = 0; i < 4; i++) { d.Add((WildColor, Wild)); d.Add((WildColor, Wild4)); }
        return d.ToArray();
    }
    public static int CardColor(int id) => Deck[id].color;
    public static int Kind(int id) => Deck[id].kind;
    public static bool IsWild(int id) => Kind(id) >= Wild;
    public static int Points(int id) => Kind(id) < 10 ? Kind(id) : Kind(id) < Wild ? 20 : 50;
    // Nom de l'image (Resources/Uno) : r0..r9, rS, rR, rD, W, W4.
    public static string Code(int id) => Kind(id) == Wild ? "W" : Kind(id) == Wild4 ? "W4" : ColorCodes[CardColor(id)] + (Kind(id) < 10 ? Kind(id).ToString() : Kind(id) == Skip ? "S" : Kind(id) == Reverse ? "R" : "D");
    public static string Name(int id) => Kind(id) == Wild ? "Joker" : Kind(id) == Wild4 ? "+4" : (Kind(id) < 10 ? Kind(id).ToString() : Kind(id) == Skip ? "Passe" : Kind(id) == Reverse ? "Inversion" : "+2") + " " + ColorNames[CardColor(id)].ToLower();

    public readonly List<UPlayer> players = new List<UPlayer>();
    public readonly List<UEvent> events = new List<UEvent>();
    public readonly List<string> log = new List<string>();
    public readonly List<int> drawPile = new List<int>(), discard = new List<int>();
    public UPhase phase = UPhase.Play;
    public int turn, dir = 1, color, pending, drawn = -1, vulnerable = -1, round, dealer, roundWinner = -1;
    public int w4Seat = -1;              // contestation : qui a pose le +4 ...
    bool w4Legal;                         // ... et avait-il le droit (aucune carte de la couleur demandee) ?
    public readonly int target;          // 0 = une seule manche
    public readonly bool stacking, sevenZero, jumpIn, drawMatch, forcePlay;
    readonly Random rng;
    static readonly Random botRng = new Random();

    public int Top => discard.Count > 0 ? discard[discard.Count - 1] : -1;
    public UPlayer Current => players[turn];
    public static int TargetOf(int option) => new[] { 0, 200, 500 }[Math.Min(2, option & 3)];

    public Uno(IEnumerable<string> names, int option, int seed)
    {
        target = TargetOf(option);
        stacking = (option & OptStacking) != 0;
        sevenZero = (option & OptSevenZero) != 0;
        jumpIn = (option & OptJumpIn) != 0;
        drawMatch = (option & OptDrawMatch) != 0;
        forcePlay = (option & OptForcePlay) != 0;
        rng = new Random(seed);
        int s = 0;
        foreach (var n in names) players.Add(new UPlayer { name = n, seat = s++ });
        dealer = rng.Next(players.Count);
        Deal();
    }

    public int Actor => phase == UPhase.RoundOver || phase == UPhase.GameOver ? -1 : turn;
    public bool Finished => phase == UPhase.GameOver;
    int Next(int from) => ((from + dir) % players.Count + players.Count) % players.Count;
    public int NextSeat => Next(turn);

    void Emit(UEv t, int seat = -1, int card = -1, int count = 0, int other = -1) =>
        events.Add(new UEvent { type = t, seat = seat, card = card, count = count, other = other });
    void Log(string s) { log.Add(s); if (log.Count > 60) log.RemoveAt(0); }

    void Shuffle(List<int> l) { for (int i = l.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); } }

    void Deal()
    {
        drawPile.Clear(); discard.Clear();
        drawPile.AddRange(Enumerable.Range(0, Deck.Length));
        Shuffle(drawPile);
        foreach (var p in players) { p.hand.Clear(); p.said = false; p.lastGain = 0; }
        for (int k = 0; k < HandSize; k++) foreach (var p in players) p.hand.Add(Pop());
        dir = 1; pending = 0; drawn = -1; vulnerable = -1; roundWinner = -1; w4Seat = -1;
        // Premiere carte : un +4 retourne repart dans le paquet ; un joker laisse la couleur au hasard.
        int first;
        while (Kind(first = Pop()) == Wild4) drawPile.Insert(rng.Next(drawPile.Count + 1), first);
        discard.Add(first);
        color = IsWild(first) ? rng.Next(4) : CardColor(first);
        phase = UPhase.Play;
        turn = Next(dealer);
        Emit(UEv.Deal, dealer, first);
        Log($"Manche {round + 1} : {players[dealer].name} distribue.");
        switch (Kind(first))
        {
            case Skip: Emit(UEv.Skipped, turn); turn = Next(turn); break;
            case Reverse: dir = -1; turn = Next(dealer); Emit(UEv.Reversed); break;
            case Draw2: GiveCards(turn, 2); Emit(UEv.Skipped, turn); turn = Next(turn); break;
        }
    }

    int Pop()
    {
        if (drawPile.Count == 0)
        {
            // Pioche vide : on remelange la defausse sauf la carte du dessus.
            int top = Top;
            drawPile.AddRange(discard.Take(discard.Count - 1));
            discard.Clear(); discard.Add(top);
            Shuffle(drawPile);
        }
        if (drawPile.Count == 0) return -1;
        int c = drawPile[drawPile.Count - 1];
        drawPile.RemoveAt(drawPile.Count - 1);
        return c;
    }

    void GiveCards(int seat, int n)
    {
        int got = 0;
        for (int i = 0; i < n; i++) { int c = Pop(); if (c < 0) break; players[seat].hand.Add(c); got++; }
        if (players[seat].hand.Count > 1) players[seat].said = false;
        Emit(UEv.Drew, seat, count: got);
    }

    // Meme couleur, meme chiffre ou symbole, ou joker. Cumul en cours : seulement un +2 (sur +2) ou un +4.
    public bool Playable(int card)
    {
        if (pending > 0) return Kind(card) == Wild4 || (Kind(card) == Draw2 && Kind(Top) == Draw2);
        return IsWild(card) || CardColor(card) == color || Kind(card) == Kind(Top);
    }

    public bool CanPlay(int seat, int card) =>
        Actor == seat && (phase == UPhase.Play || (phase == UPhase.Drawn && card == drawn)) && players[seat].hand.Contains(card) && Playable(card);

    // Intervention : hors de son tour, la carte exactement identique a celle du dessus (meme couleur, meme symbole).
    public bool CanJumpIn(int seat, int card) =>
        jumpIn && phase == UPhase.Play && pending == 0 && seat != turn && Top >= 0 && !IsWild(card) && !IsWild(Top)
        && players[seat].hand.Contains(card) && Kind(card) == Kind(Top) && CardColor(card) == CardColor(Top);

    public bool TryApply(string[] a)
    {
        events.Clear();
        switch (a[0])
        {
            case "play":   // play|carte|couleur choisie (joker)|annonce UNO (0/1)
            {
                if (a.Length < 2 || !int.TryParse(a[1], out int card) || !CanPlay(turn, card)) return false;
                int chosen = a.Length > 2 && int.TryParse(a[2], out int cc) ? cc : -1;
                if (IsWild(card) && (chosen < 0 || chosen > 3)) return false;
                PlayCard(turn, card, chosen, a.Length > 3 && a[3] == "1");
                return true;
            }
            case "jump":   // jump|siege|carte : intervention hors tour
            {
                if (a.Length < 3 || !int.TryParse(a[1], out int seat) || !int.TryParse(a[2], out int card) || seat < 0 || seat >= players.Count) return false;
                if (!CanJumpIn(seat, card)) return false;
                Emit(UEv.JumpIn, seat, card);
                Log($"{players[seat].name} intervient !");
                turn = seat;
                PlayCard(seat, card, -1, false);
                return true;
            }
            case "draw":
            {
                if (phase != UPhase.Play) return false;
                var p = Current;
                vulnerable = -1;
                if (pending > 0)
                {
                    GiveCards(turn, pending);
                    Log($"{p.name} pioche {pending} cartes.");
                    pending = 0;
                    Emit(UEv.Skipped, turn);
                    turn = Next(turn);
                    return true;
                }
                // Une carte (ou, regle maison, jusqu'a en trouver une jouable).
                int c = -1, got = 0;
                do { int before = p.hand.Count; GiveCards(turn, 1); if (p.hand.Count == before) break; c = p.hand[p.hand.Count - 1]; got++; }
                while (drawMatch && !Playable(c) && got < 40);
                if (c >= 0 && Playable(c)) { phase = UPhase.Drawn; drawn = c; }   // il peut (ou doit) la poser tout de suite
                else turn = Next(turn);
                return true;
            }
            case "keep":   // garde la carte piochee et passe (interdit en "jeu force")
                if (phase != UPhase.Drawn || forcePlay) return false;
                phase = UPhase.Play; drawn = -1;
                turn = Next(turn);
                return true;
            case "challenge":   // le joueur vise conteste le +4
            case "accept":      // ... ou l'accepte
            {
                if (phase != UPhase.Challenge) return false;
                int victim = turn, off = w4Seat;
                phase = UPhase.Play; w4Seat = -1;
                if (a[0] == "accept")
                {
                    GiveCards(victim, 4); Emit(UEv.Skipped, victim);
                    Log($"{players[victim].name} pioche 4 cartes.");
                    turn = Next(victim);
                }
                else if (!w4Legal)
                {
                    // Bluff demasque : le poseur pioche 4, le joueur vise joue normalement.
                    GiveCards(off, 4);
                    Emit(UEv.ChallengeResult, off, count: 4, other: 1);
                    Log($"{players[victim].name} a démasqué le bluff de {players[off].name} : +4 pour lui !");
                }
                else
                {
                    GiveCards(victim, 6); Emit(UEv.ChallengeResult, victim, count: 6, other: 0); Emit(UEv.Skipped, victim);
                    Log($"Le +4 de {players[off].name} était réglo : {players[victim].name} pioche 6 cartes.");
                    turn = Next(victim);
                }
                return true;
            }
            case "swap":   // swap|siege : regle du 7, j'echange ma main avec ce joueur
            {
                if (phase != UPhase.SwapPick || a.Length < 2 || !int.TryParse(a[1], out int with) || with == turn || with < 0 || with >= players.Count) return false;
                SwapHands(turn, with);
                Emit(UEv.Swapped, turn, other: with);
                Log($"{Current.name} échange sa main avec {players[with].name} !");
                phase = UPhase.Play;
                turn = Next(turn);
                return true;
            }
            case "uno":    // uno|siege (a tout moment, avec une ou deux cartes)
            {
                if (a.Length < 2 || !int.TryParse(a[1], out int seat) || seat < 0 || seat >= players.Count) return false;
                var p = players[seat];
                if (p.said || p.hand.Count > 2 || phase == UPhase.RoundOver || phase == UPhase.GameOver) return false;
                p.said = true;
                if (vulnerable == seat) vulnerable = -1;
                Emit(UEv.Uno, seat);
                Log($"{p.name} : « UNO ! »");
                return true;
            }
            case "catch":  // catch|siege|cible : la cible a oublie "UNO" -> 2 cartes
            {
                if (a.Length < 3 || !int.TryParse(a[1], out int seat) || !int.TryParse(a[2], out int who)) return false;
                if (who != vulnerable || who == seat || seat < 0 || seat >= players.Count) return false;
                vulnerable = -1;
                GiveCards(who, 2);
                Emit(UEv.Caught, who, other: seat);
                Log($"{players[seat].name} a vu que {players[who].name} n'a pas dit UNO : +2 cartes !");
                return true;
            }
            case "next":   // manche suivante (l'hote, apres le decompte des points)
                if (phase != UPhase.RoundOver) return false;
                round++;
                dealer = Next(dealer);
                Deal();
                return true;
        }
        return false;
    }

    void SwapHands(int a, int b)
    {
        var tmp = players[a].hand.ToList();
        players[a].hand.Clear(); players[a].hand.AddRange(players[b].hand);
        players[b].hand.Clear(); players[b].hand.AddRange(tmp);
        players[a].said = players[b].said = false;
    }

    void PlayCard(int seat, int card, int chosen, bool sayUno)
    {
        var p = players[seat];
        vulnerable = -1;
        if (sayUno && p.hand.Count <= 2) { p.said = true; Emit(UEv.Uno, seat); }
        // +4 reglo seulement sans carte de la couleur demandee (on le verifie avant de changer de couleur).
        if (Kind(card) == Wild4) { w4Legal = !p.hand.Any(c => c != card && !IsWild(c) && CardColor(c) == color); w4Seat = seat; }
        p.hand.Remove(card);
        discard.Add(card);
        color = IsWild(card) ? chosen : CardColor(card);
        drawn = -1;
        phase = UPhase.Play;
        Emit(UEv.Played, seat, card, other: color);
        Log($"{p.name} joue {Name(card)}{(IsWild(card) ? " (" + ColorNames[color].ToLower() + ")" : "")}.");
        if (p.hand.Count == 1 && !p.said) vulnerable = seat;   // oubli de "UNO" : on peut le prendre
        int victim = Next(turn);
        switch (Kind(card))
        {
            case Skip: Emit(UEv.Skipped, victim); turn = Next(victim); break;
            case Reverse:
                dir = -dir; Emit(UEv.Reversed);
                turn = players.Count == 2 ? turn : Next(turn);   // a deux, l'inversion fait rejouer
                break;
            case Draw2:
                if (stacking) { pending += 2; turn = victim; }
                else { GiveCards(victim, 2); Emit(UEv.Skipped, victim); turn = Next(victim); }
                break;
            case Wild4:
                if (stacking) { pending += 4; turn = victim; }
                else if (p.hand.Count == 0) { GiveCards(victim, 4); turn = Next(victim); }
                else { phase = UPhase.Challenge; turn = victim; Emit(UEv.Challenge, victim, other: seat); }   // le suivant peut contester
                break;
            case 7 when sevenZero && p.hand.Count > 0:
                phase = UPhase.SwapPick;   // il choisit avec qui echanger sa main
                break;
            case 0 when sevenZero && p.hand.Count > 0:
                // Toutes les mains passent au joueur suivant, dans le sens du jeu.
                var hands = players.Select(x => x.hand.ToList()).ToList();
                for (int i = 0; i < players.Count; i++) { var h = players[Next(i)].hand; h.Clear(); h.AddRange(hands[i]); players[Next(i)].said = false; }
                Emit(UEv.Rotated);
                Log("Toutes les mains tournent !");
                turn = victim;
                break;
            default: turn = victim; break;
        }
        if (p.hand.Count == 0)
        {
            if (pending > 0) { GiveCards(turn, pending); pending = 0; }   // le dernier +2/+4 compte quand meme
            EndRound(seat);
        }
    }

    void EndRound(int winner)
    {
        var w = players[winner];
        int pts = players.Where(p => p != w).Sum(p => p.hand.Sum(Points));
        w.score += pts;
        foreach (var p in players) p.lastGain = p == w ? pts : 0;
        roundWinner = winner;
        vulnerable = -1;
        Log($"{w.name} n'a plus de cartes : +{pts} points.");
        Emit(UEv.RoundOver, winner, count: pts);
        if (target == 0 || w.score >= target) { phase = UPhase.GameOver; Emit(UEv.GameOver, winner); }
        else phase = UPhase.RoundOver;
    }

    // Choix automatique (bot hors ligne, joueur deconnecte) : une carte de la couleur, sinon du meme symbole,
    // les jokers en dernier (et un +4 seulement s'il est reglo) ; conteste un +4 de temps en temps ; echange sa
    // main avec celui qui en a le moins ; pioche s'il ne peut rien jouer.
    public string[] Bot()
    {
        var p = Current;
        switch (phase)
        {
            // (hasard propre au bot : surtout pas le generateur de la partie, partage par tous les joueurs en ligne)
            case UPhase.Challenge: return new[] { players[w4Seat].hand.Count <= 3 || botRng.Next(4) == 0 ? "challenge" : "accept" };
            case UPhase.SwapPick:
                return new[] { "swap", players.Where(x => x.seat != turn).OrderBy(x => x.hand.Count).First().seat.ToString() };
            case UPhase.Drawn: return Play(drawn, p);
        }
        var ok = p.hand.Where(Playable).ToList();
        bool hasColor = p.hand.Any(c => !IsWild(c) && CardColor(c) == color);
        ok = ok.Where(c => Kind(c) != Wild4 || !hasColor || pending > 0 || ok.Count == 1).ToList();
        if (ok.Count == 0) return new[] { "draw" };
        bool nextNearWin = players[NextSeat].hand.Count <= 2;
        var pick = ok.OrderBy(c => IsWild(c) ? 1 : 0)
                     .ThenByDescending(c => nextNearWin && Kind(c) >= Skip ? 1 : 0)
                     .ThenByDescending(c => CardColor(c) == color ? 1 : 0)
                     .ThenByDescending(Points).First();
        return Play(pick, p);
    }

    string[] Play(int card, UPlayer p)
    {
        int best = p.hand.Where(c => !IsWild(c) && c != card).GroupBy(CardColor).OrderByDescending(g => g.Count()).Select(g => g.Key).DefaultIfEmpty(color).First();
        return new[] { "play", card.ToString(), IsWild(card) ? best.ToString() : "-1", p.hand.Count == 2 ? "1" : "0" };
    }
}
