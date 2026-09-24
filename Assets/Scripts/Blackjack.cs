using System;
using System.Collections.Generic;
using System.Linq;

// Blackjack contre un croupier IA. Logique pure et deterministe (graine), comme Rules.
// Regles : sabot de 6 jeux, croupier reste sur 17 (y compris soft), blackjack paye 3:2,
// doubler sur 2 cartes, separer une paire (4 mains max, As separes : une carte chacun),
// assurance quand le croupier montre un As.
public enum BJPhase { Bet, Insurance, Play, GameOver }

public class BJHand
{
    public List<int> cards = new List<int>();
    public int bet;
    public bool done, doubled, split;
}

public class BJPlayer
{
    public string name;
    public int seat, chips = Blackjack.StartChips, insurance, lastBet = Blackjack.MinBet;
    public bool broke;
    public List<BJHand> hands = new List<BJHand>();
}

public enum BJEv { RoundStart, Shuffle, Bet, Deal, Reveal, Split, Insurance, Result, Broke, GameOver }

public class BJEvent
{
    public BJEv type;
    public int seat = -1, hand, card = -1, amount;   // seat -1 = croupier
    public bool faceDown;
    public string text;
}

public class Blackjack : IMatch
{
    public const int StartChips = 1000, MinBet = 10, Decks = 6;

    public readonly List<BJPlayer> players = new List<BJPlayer>();
    public readonly List<int> dealer = new List<int>();
    public bool holeHidden;
    public BJPhase phase;
    public int round, rounds;
    public readonly List<string> log = new List<string>();
    public readonly List<BJEvent> events = new List<BJEvent>();   // produits par la derniere action

    readonly Random rng;
    readonly List<int> shoe = new List<int>();
    int actor = -1, insuranceIndex;

    public Blackjack(IEnumerable<string> names, int rounds, int seed)
    {
        rng = new Random(seed);
        this.rounds = rounds;
        int s = 0;
        foreach (var n in names) players.Add(new BJPlayer { name = n, seat = s++ });
        StartRound();
    }

    // --- Cartes ---------------------------------------------------------------------
    public static int Rank(int c) => c % 13;                     // 0 = As ... 12 = Roi
    public static int Suit(int c) => c / 13;                     // 0 pique, 1 carreau, 2 coeur, 3 trefle
    static int Points(int c) { int r = Rank(c); return r == 0 ? 11 : Math.Min(10, r + 1); }

    public static int Value(IList<int> cards) => Eval(cards).total;

    public static (int total, bool soft) Eval(IList<int> cards)
    {
        int t = 0, aces = 0;
        foreach (var c in cards) { t += Points(c); if (Rank(c) == 0) aces++; }
        while (t > 21 && aces > 0) { t -= 10; aces--; }
        return (t, aces > 0);
    }

    public static bool IsBlackjack(BJHand h) => !h.split && h.cards.Count == 2 && Value(h.cards) == 21;

    void Shuffle()
    {
        shoe.Clear();
        for (int d = 0; d < Decks; d++) for (int c = 0; c < 52; c++) shoe.Add(c);
        for (int i = shoe.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (shoe[i], shoe[j]) = (shoe[j], shoe[i]); }
        Emit(BJEv.Shuffle);
        Log("Le croupier mélange le sabot.");
    }

    int Draw() { int c = shoe[shoe.Count - 1]; shoe.RemoveAt(shoe.Count - 1); return c; }

    void Emit(BJEv t, int seat = -1, int hand = 0, int card = -1, int amount = 0, bool faceDown = false, string text = null) =>
        events.Add(new BJEvent { type = t, seat = seat, hand = hand, card = card, amount = amount, faceDown = faceDown, text = text });

    void Log(string s) { log.Add(s); if (log.Count > 60) log.RemoveAt(0); }

    // --- Deroulement ------------------------------------------------------------------
    public int Actor => actor;
    public bool Finished => phase == BJPhase.GameOver;
    public BJPlayer Current => actor >= 0 ? players[actor] : null;
    public int DealerUp => dealer.Count > 0 ? dealer[0] : -1;

    public BJHand ActiveHand => phase == BJPhase.Play && actor >= 0 ? players[actor].hands.FirstOrDefault(h => !h.done) : null;

    IEnumerable<BJPlayer> Active => players.Where(p => !p.broke);

    void StartRound()
    {
        round++;
        if (round > rounds || !Active.Any()) { EndGame(); return; }
        if (shoe.Count < 60) Shuffle();
        dealer.Clear();
        holeHidden = true;
        foreach (var p in players) { p.hands.Clear(); p.insurance = 0; }
        phase = BJPhase.Bet;
        Emit(BJEv.RoundStart, amount: round, text: $"Manche {round}/{rounds}");
        Log($"--- Manche {round}/{rounds} ---");
        actor = Active.First().seat;
    }

    void EndGame()
    {
        phase = BJPhase.GameOver;
        actor = -1;
        int best = players.Max(p => p.chips);
        var winners = players.Where(p => p.chips == best).Select(p => p.name);
        Emit(BJEv.GameOver, text: string.Join(" et ", winners));
        Log($"Fin de la partie ! {string.Join(" et ", winners)} gagne avec {best} jetons.");
    }

    int NextActive(int after) => Active.Where(p => p.seat > after).Select(p => p.seat).DefaultIfEmpty(-1).First();

    public bool CanDouble(BJHand h, BJPlayer p) => h.cards.Count == 2 && p.chips >= h.bet && !(h.split && Rank(h.cards[0]) == 0);
    public bool CanSplit(BJHand h, BJPlayer p) => h.cards.Count == 2 && Rank(h.cards[0]) == Rank(h.cards[1]) && p.chips >= h.bet && p.hands.Count < 4;
    public int InsuranceCost(BJPlayer p) => p.hands[0].bet / 2;

    public bool TryApply(string[] a)
    {
        events.Clear();
        if (phase == BJPhase.GameOver || actor < 0) return false;
        var p = players[actor];
        switch (phase)
        {
            case BJPhase.Bet:
                if (a[0] != "bet" || !int.TryParse(a[1], out int bet) || bet < MinBet || bet > p.chips || bet % MinBet != 0) return false;
                p.chips -= bet;
                p.lastBet = bet;
                p.hands.Add(new BJHand { bet = bet });
                Emit(BJEv.Bet, p.seat, 0, amount: bet);
                Log($"{p.name} mise {bet}.");
                actor = NextActive(p.seat);
                if (actor < 0) Deal();
                return true;

            case BJPhase.Insurance:
                if (a[0] != "ins") return false;
                if (a[1] == "1" && p.chips >= InsuranceCost(p))
                {
                    p.insurance = InsuranceCost(p);
                    p.chips -= p.insurance;
                    Emit(BJEv.Insurance, p.seat, amount: p.insurance);
                    Log($"{p.name} prend l'assurance ({p.insurance}).");
                }
                NextInsurance();
                return true;

            case BJPhase.Play:
                var h = ActiveHand;
                int hi = p.hands.IndexOf(h);
                switch (a[0])
                {
                    case "hit":
                        Give(p, hi);
                        Log($"{p.name} tire : {Value(h.cards)}.");
                        break;
                    case "stand":
                        h.done = true;
                        Log($"{p.name} reste à {Value(h.cards)}.");
                        break;
                    case "double":
                        if (!CanDouble(h, p)) return false;
                        p.chips -= h.bet;
                        Emit(BJEv.Bet, p.seat, hi, amount: h.bet);
                        h.bet *= 2;
                        h.doubled = true;
                        Give(p, hi);
                        h.done = true;
                        Log($"{p.name} double : {Value(h.cards)}.");
                        break;
                    case "split":
                        if (!CanSplit(h, p)) return false;
                        p.chips -= h.bet;
                        var nh = new BJHand { bet = h.bet, split = true };
                        h.split = true;
                        nh.cards.Add(h.cards[1]);
                        h.cards.RemoveAt(1);
                        p.hands.Insert(hi + 1, nh);
                        Emit(BJEv.Split, p.seat, hi, amount: h.bet);
                        Log($"{p.name} sépare sa paire.");
                        bool aces = Rank(h.cards[0]) == 0;
                        Give(p, hi);
                        Give(p, hi + 1);
                        if (aces) { h.done = true; nh.done = true; }
                        break;
                    default:
                        return false;
                }
                AdvancePlay();
                return true;
        }
        return false;
    }

    void Give(BJPlayer p, int hi)
    {
        var h = p.hands[hi];
        int c = Draw();
        h.cards.Add(c);
        Emit(BJEv.Deal, p.seat, hi, c);
        if (Value(h.cards) >= 21) h.done = true;
    }

    void Deal()
    {
        var active = Active.Where(p => p.hands.Count > 0).ToList();
        for (int k = 0; k < 2; k++)
        {
            foreach (var p in active) Give(p, 0);
            int c = Draw();
            dealer.Add(c);
            Emit(BJEv.Deal, -1, 0, c, faceDown: k == 1);
        }
        foreach (var p in active) if (IsBlackjack(p.hands[0])) Log($"{p.name} a un blackjack !");
        if (Rank(dealer[0]) == 0)
        {
            phase = BJPhase.Insurance;
            insuranceIndex = -1;
            Log("Le croupier montre un As : assurance ?");
            NextInsurance();
        }
        else CheckDealerBlackjack();
    }

    void NextInsurance()
    {
        var candidates = Active.Where(p => p.hands.Count > 0 && p.chips >= InsuranceCost(p) && p.seat > insuranceIndex).ToList();
        if (candidates.Count > 0) { actor = insuranceIndex = candidates[0].seat; return; }
        CheckDealerBlackjack();
    }

    void CheckDealerBlackjack()
    {
        bool peek = Points(dealer[0]) >= 10;
        if (peek && Value(dealer) == 21)
        {
            Log("Le croupier a un blackjack !");
            DealerPlay(drawCards: false);
            return;
        }
        if (Rank(dealer[0]) == 0) Log("Pas de blackjack pour le croupier.");
        foreach (var p in players) if (p.insurance > 0) Log($"{p.name} perd son assurance.");
        phase = BJPhase.Play;
        actor = -1;
        AdvancePlay();
    }

    void AdvancePlay()
    {
        if (actor >= 0 && players[actor].hands.Any(h => !h.done)) return;
        int next = Active.Where(p => p.seat > actor && p.hands.Any(h => !h.done)).Select(p => p.seat).DefaultIfEmpty(-1).First();
        if (next >= 0) { actor = next; return; }
        DealerPlay(drawCards: true);
    }

    void DealerPlay(bool drawCards)
    {
        actor = -1;
        holeHidden = false;
        Emit(BJEv.Reveal, -1, 0, dealer[1]);
        bool anyLive = players.Any(p => p.hands.Any(h => Value(h.cards) <= 21 && !IsBlackjack(h)));
        if (drawCards && anyLive)
            while (Value(dealer) < 17)
            {
                int c = Draw();
                dealer.Add(c);
                Emit(BJEv.Deal, -1, 0, c);
            }
        int d = Value(dealer);
        bool dealerBJ = dealer.Count == 2 && d == 21;
        Log(d > 21 ? $"Le croupier saute à {d} !" : $"Le croupier fait {d}.");

        foreach (var p in players)
        {
            if (p.insurance > 0 && dealerBJ)
            {
                p.chips += p.insurance * 3;
                Emit(BJEv.Result, p.seat, -1, amount: p.insurance * 3, text: "Assurance payée");
            }
            for (int i = 0; i < p.hands.Count; i++)
            {
                var h = p.hands[i];
                int v = Value(h.cards);
                string outcome;
                int pay;
                if (v > 21) { outcome = "Perdu"; pay = 0; }
                else if (IsBlackjack(h) && !dealerBJ) { outcome = "Blackjack !"; pay = h.bet + h.bet * 3 / 2; }
                else if (dealerBJ && !IsBlackjack(h)) { outcome = "Perdu"; pay = 0; }
                else if (d > 21 || v > d) { outcome = "Gagné"; pay = h.bet * 2; }
                else if (v == d) { outcome = "Égalité"; pay = h.bet; }
                else { outcome = "Perdu"; pay = 0; }
                p.chips += pay;
                Emit(BJEv.Result, p.seat, i, amount: pay - h.bet, text: outcome);
                Log($"{p.name} : {outcome.ToLower().TrimEnd('!', ' ')} ({(pay - h.bet >= 0 ? "+" : "")}{pay - h.bet}).");
            }
            if (!p.broke && p.chips < MinBet)
            {
                p.broke = true;
                Emit(BJEv.Broke, p.seat);
                Log($"{p.name} est ruiné et quitte la table.");
            }
        }
        StartRound();
    }

    public string[] Bot()
    {
        var p = Current;
        if (p == null) return null;
        switch (phase)
        {
            case BJPhase.Bet: return new[] { "bet", (Math.Min(p.chips, p.lastBet) / MinBet * MinBet).ToString() };
            case BJPhase.Insurance: return new[] { "ins", "0" };
            default: return new[] { Value(ActiveHand.cards) < 17 ? "hit" : "stand" };
        }
    }
}
