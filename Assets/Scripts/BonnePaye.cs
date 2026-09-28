using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// La Bonne Paye (regle Parker/Hasbro, plateau 2014 fourni par l'utilisatrice). Tout l'argent passe par ces regles :
// soldes, livret d'epargne, prets et cagnotte ; aucun mouvement a la main (pas de triche possible). Les joueurs ne
// decident que ce que le vrai jeu leur laisse decider : acheter, assurer, vendre, miser, epargner, emprunter, rembourser.
// Deterministe : le de de deplacement vient de l'action ("roll|valeur|lancer..."), les autres tirages (pioches, loterie,
// commission, Besoin d'argent) viennent du generateur a graine. Les effets en attente sont une file de taches ; une tache
// qui demande une decision met la partie en pause (Actor = celui qui doit decider).
public enum BPEv { Rolled, Moved, Card, Pay, Jackpot, Info, Month, Finished, Lotto }
public class BPEvent { public BPEv type; public int seat = -1, from, to, amount, deck, img; public string text; }

public class BonnePaye : IMatch
{
    public const int MaxPlayers = 6, Salary = 1500, LoanSize = 1500, Days = 31, Bank = -1, Pot = -2;
    public const int Start = 1500, WithdrawFee = 150, LoanInterest = 150;

    // --- Donnees des cartes (Resources/BonnePaye/cartes.json) ---
    [Serializable] public class Mail { public int img; public string nom, t, cat; public int v; }
    [Serializable] public class Acq { public int img; public string nom; public int achat, valeur, commission; }
    [Serializable] public class Evt { public int img; public string nom, t; public int v; }
    [Serializable] class Data { public Mail[] courrier; public Acq[] acquisition; public Evt[] evenement; }
    static Data data;
    public static Mail[] Mails => Load().courrier;
    public static Acq[] Acqs => Load().acquisition;
    public static Evt[] Evts => Load().evenement;
    static Data Load()
    {
        if (data != null) return data;
        var t = Resources.Load<TextAsset>("BonnePaye/cartes");
        data = t ? JsonUtility.FromJson<Data>(t.text) : new Data { courrier = new Mail[0], acquisition = new Acq[0], evenement = new Evt[0] };
        return data;
    }

    // --- Cases du plateau (jour du mois) ---
    public enum Cell { Start, Mail, Acquisition, Sell, News, Pay, PayPot, AllPot, Birthday, Gain, Rest, Clock, Lottery, Payday }
    public struct Day { public Cell kind; public int n; public string name; }
    public static readonly Day[] Board = BuildBoard();
    static Day[] BuildBoard()
    {
        var b = new Day[Days + 1];
        b[0] = new Day { kind = Cell.Start, name = "Départ" };
        foreach (int d in new[] { 1, 11, 18, 24 }) b[d] = new Day { kind = Cell.Mail, n = 1, name = "Courrier ×1" };
        foreach (int d in new[] { 5, 27 }) b[d] = new Day { kind = Cell.Mail, n = 2, name = "Courrier ×2" };
        foreach (int d in new[] { 3, 16 }) b[d] = new Day { kind = Cell.Mail, n = 3, name = "Courrier ×3" };
        foreach (int d in new[] { 4, 12, 17, 25 }) b[d] = new Day { kind = Cell.Acquisition, name = "Acquisition" };
        foreach (int d in new[] { 9, 20, 23, 29 }) b[d] = new Day { kind = Cell.Sell, name = "Vendez !" };
        foreach (int d in new[] { 13, 22 }) b[d] = new Day { kind = Cell.News, name = "Quoi de neuf ?" };
        b[2] = new Day { kind = Cell.Gain, n = 1500, name = "Toutou remporte 1500 € à un concours de beauté" };
        b[6] = new Day { kind = Cell.Pay, n = 150, name = "Sortie en famille : payez 150 €" };
        b[7] = new Day { kind = Cell.Rest, name = "Détendez-vous" };
        b[8] = new Day { kind = Cell.AllPot, n = 150, name = "Course de vélo : tout le monde verse 150 € à la cagnotte" };
        b[10] = new Day { kind = Cell.Birthday, n = 150, name = "Joyeux anniversaire : chaque joueur vous verse 150 €" };
        b[14] = new Day { kind = Cell.Rest, name = "Journée de balade" };
        b[15] = new Day { kind = Cell.PayPot, n = 150, name = "Bricolage : versez 150 € à la cagnotte" };
        b[19] = new Day { kind = Cell.Pay, n = 250, name = "Shopping ! Dépensez 250 €" };
        b[21] = new Day { kind = Cell.Rest, name = "Bricolage au jardin" };
        b[26] = new Day { kind = Cell.Clock, name = "Changement d'heure : tout le monde recule d'une case" };
        b[28] = new Day { kind = Cell.Rest, name = "Restez cool !" };
        b[30] = new Day { kind = Cell.Lottery, name = "Loterie" };
        b[31] = new Day { kind = Cell.Payday, name = "Jour de paye" };
        return b;
    }

    // --- Joueurs ---
    public class Player
    {
        public string name; public int seat, pos, money = Start, savings, loans, month = 1;
        public bool medic, auto, done;
        public readonly List<int> bills = new List<int>();     // courriers a regler au Jour de paye
        public readonly List<int> acqs = new List<int>();      // acquisitions detenues
        public readonly List<int> besoin = new List<int>();    // cartes "Besoin d'argent ?" en main
        public readonly List<int> unread = new List<int>();    // courrier recu, pas encore ouvert (surprise au Jour de paye)
        public int Capital => money + savings - loans * LoanSize;
        public int BillsTotal => bills.Sum(i => Mails[i].v);
    }

    // Decision en attente (et taches automatiques) : file traitee dans l'ordre.
    public enum Task { Extend, Cell, MailDraw, AcqDraw, EvtDraw, MailOpen, Insure, Buy, Sell, LottoJoin, LottoRoll, LottoResolve, CommRoll, CommResolve, BesoinCheck, BesoinBet, BesoinRoll, Repay, PaydayEnd, ClockBack }
    public class Job { public Task t; public int seat, a, b; }

    public readonly List<Player> players = new List<Player>();
    public readonly List<BPEvent> events = new List<BPEvent>();
    public readonly List<string> log = new List<string>();
    public readonly List<Job> jobs = new List<Job>();
    public int months;   // peut etre prolonge a la fin ("extend")
    public int turn, pot, winner = -1, dice;
    public bool rolled;                                 // le joueur actif a deja lance ce tour-ci
    // Loterie : ceux qui ont mise 100 € lancent le de chacun leur tour ; le plus gros chiffre gagne (egalite : on relance).
    public readonly List<int> lottoIn = new List<int>();
    public readonly Dictionary<int, int> lottoRolls = new Dictionary<int, int>();   // siege -> chiffre du lancer en cours
    // Commission d'une vente : tout le monde lance le de, le plus haut la touche (egalite : les ex aequo relancent).
    public int commission;
    public readonly Dictionary<int, int> commRolls = new Dictionary<int, int>();
    public int lottoPot;
    readonly System.Random rng;
    readonly List<int> mailDeck = new List<int>(), acqDeck = new List<int>(), evtDeck = new List<int>();
    readonly List<int> mailDrop = new List<int>(), acqDrop = new List<int>(), evtDrop = new List<int>();

    public Job Pending
    {
        get
        {
            // Remboursement propose seulement s'il y a un pret et de quoi en rendre un (sinon on passe).
            while (jobs.Count > 0 && jobs[0].t == Task.Repay && (players[jobs[0].seat].loans == 0 || players[jobs[0].seat].money < LoanSize)) jobs.RemoveAt(0);
            return jobs.Count > 0 && IsDecision(jobs[0].t) ? jobs[0] : null;
        }
    }
    public static bool IsDraw(Task t) => t == Task.MailDraw || t == Task.AcqDraw || t == Task.EvtDraw;
    static bool IsDecision(Task t) => t == Task.Extend || IsDraw(t) || t == Task.Insure || t == Task.Buy || t == Task.Sell || t == Task.LottoJoin || t == Task.LottoRoll || t == Task.CommRoll || t == Task.BesoinBet || t == Task.BesoinRoll || t == Task.Repay;
    public static bool IsRoll(Task t) => t == Task.LottoRoll || t == Task.CommRoll || t == Task.BesoinRoll;
    public int Actor => Finished ? -1 : Pending != null ? Pending.seat : turn;
    public bool Finished => winner >= 0;
    public Player Current => players[turn];
    public bool CanDeposit(int seat) => players[seat].pos < 23 && Pending == null && seat == turn && !rolled;

    // Option : nombre de mois (1 a 24).
    public const int MaxMonths = 24;
    public static int Months(int option) => Mathf.Clamp(option, 0, MaxMonths - 1) + 1;   // option = nombre de mois - 1

    public BonnePaye(IList<string> names, int option, int seed)
    {
        rng = new System.Random(seed);
        months = Months(option);
        for (int i = 0; i < names.Count; i++) players.Add(new Player { name = names[i], seat = i });
        Refill(mailDeck, mailDrop, Mails.Length); Refill(acqDeck, acqDrop, Acqs.Length); Refill(evtDeck, evtDrop, Evts.Length);
        Emit(BPEv.Info, 0, $"Mois 1 / {months} : à {players[0].name} de commencer !");
    }

    // Pioches finies, comme le vrai jeu : une pile epuisee est refaite en melangeant son talon (cartes jouees ou
    // refusees). Les cartes gardees par les joueurs n'y reviennent pas ; pile et talon vides : plus de carte (-1).
    void Refill(List<int> deck, List<int> drop, int n)
    {
        deck.Clear();
        deck.AddRange(n > 0 ? Enumerable.Range(0, n) : drop);
        drop.Clear();
        for (int i = deck.Count - 1; i > 0; i--) { int k = rng.Next(i + 1); (deck[i], deck[k]) = (deck[k], deck[i]); }
    }
    int Draw(List<int> deck, List<int> drop)
    {
        if (deck.Count == 0) { if (drop.Count == 0) return -1; Refill(deck, drop, 0); Emit(BPEv.Info, turn, "Pile épuisée : on mélange le talon pour la refaire."); }
        int c = deck[0]; deck.RemoveAt(0); return c;
    }
    public int MailLeft => mailDeck.Count;
    public int AcqLeft => acqDeck.Count;
    public int EvtLeft => evtDeck.Count;

    BPEvent Emit(BPEv t, int seat, string text = null) { var e = new BPEvent { type = t, seat = seat, text = text }; events.Add(e); if (text != null && t != BPEv.Pay) log.Add(text); return e; }

    // --- Argent : jamais de solde negatif ; il manque de l'argent -> pret automatique par tranches de 1500 ---
    void Credit(int seat, int amount, int from = Bank)
    {
        if (seat >= 0) players[seat].money += amount; else pot += amount;
        events.Add(new BPEvent { type = BPEv.Pay, seat = seat, from = from, to = seat, amount = amount });
    }
    void Debit(int seat, int amount, int to = Bank, string why = null)
    {
        var p = players[seat];
        if (p.money < amount)
        {
            int k = (amount - p.money + LoanSize - 1) / LoanSize;
            p.loans += k; p.money += k * LoanSize;
            Emit(BPEv.Info, seat, $"{p.name} n'a pas assez : la banque lui prête {k * LoanSize} €.");
        }
        p.money -= amount;
        if (to == Pot) pot += amount; else if (to >= 0) players[to].money += amount;
        events.Add(new BPEvent { type = BPEv.Pay, seat = seat, from = seat, to = to, amount = amount, text = why });
    }

    // --- Actions ---
    // "xxx|valeur|lancer..." : le de a roule chez le joueur (valeur + trajectoire pour que tous voient le meme lancer).
    int DieValue(string[] a) => a.Length > 1 && int.TryParse(a[1], out int v) && v >= 1 && v <= 6 ? v : rng.Next(1, 7);
    BPEvent EmitRoll(int seat, int v, string[] a)
    {
        var ev = Emit(BPEv.Rolled, seat); ev.amount = v;
        if (a.Length >= 15) ev.text = string.Join("|", a.Skip(2).Take(13));
        return ev;
    }

    public bool TryApply(string[] a)
    {
        events.Clear();
        if (Finished) return false;
        int me = Actor;
        var job = Pending;
        switch (a[0])
        {
            case "roll":
                if (job != null || rolled) return false;
                dice = DieValue(a);
                EmitRoll(turn, dice, a);
                rolled = true;
                if (dice == 6 && pot > 0)
                {
                    int won = pot; pot = 0; Current.money += won;
                    Emit(BPEv.Jackpot, turn, $"6 ! {Current.name} rafle la cagnotte : {won} € !").amount = won;
                }
                Move(turn, dice);
                break;
            case "extend":   // extend|nombre de mois en plus (0 : fin de la partie)
            {
                if (job == null || job.t != Task.Extend || a.Length < 2 || !int.TryParse(a[1], out int n) || n < 0 || n > MaxMonths) return false;
                jobs.RemoveAt(0);
                if (n == 0) { EndGame(); return true; }
                months += n;
                Emit(BPEv.Month, 0, $"La partie continue : {n} mois de plus ({months} en tout) !");
                foreach (var p in players)
                {
                    p.done = false; p.month++;
                    int from = p.pos; p.pos = 0;
                    var e = Emit(BPEv.Moved, p.seat); e.from = from; e.to = 0;
                }
                turn = 0; rolled = false; dice = 0;
                Emit(BPEv.Info, 0, $"Mois {players[0].month} : à {players[0].name} de jouer !");
                return true;
            }
            case "draw":   // le joueur pioche lui-meme dans la pile demandee
            {
                if (job == null || !IsDraw(job.t)) return false;
                jobs.RemoveAt(0);
                var more = new List<Job>();
                DoDraw(job, more);
                jobs.InsertRange(0, more);
                break;
            }
            case "yes": case "no":   // assurance ou acquisition
                if (job == null || (job.t != Task.Insure && job.t != Task.Buy)) return false;
                jobs.RemoveAt(0);
                if (job.t == Task.Insure)
                {
                    var m = Mails[job.a];
                    if (a[0] == "yes")
                    {
                        Debit(job.seat, m.v, Bank);
                        if (m.cat == "med") players[job.seat].medic = true; else players[job.seat].auto = true;
                        Emit(BPEv.Info, job.seat, $"{players[job.seat].name} souscrit {m.nom} ({m.v} €).");
                    }
                    mailDrop.Add(job.a);
                }
                else
                {
                    var q = Acqs[job.a];
                    if (a[0] == "yes") { Debit(job.seat, q.achat, Bank); players[job.seat].acqs.Add(job.a); Emit(BPEv.Info, job.seat, $"{players[job.seat].name} achète {q.nom} pour {q.achat} €."); }
                    else { acqDrop.Add(job.a); Emit(BPEv.Info, job.seat, $"{players[job.seat].name} passe son tour sur l'affaire."); }
                }
                break;
            case "sell":   // sell|index dans ses acquisitions (-1 : ne vend rien)
            {
                if (job == null || job.t != Task.Sell || a.Length < 2 || !int.TryParse(a[1], out int k)) return false;
                var p = players[job.seat];
                if (k >= p.acqs.Count) return false;
                jobs.RemoveAt(0);
                if (k >= 0)
                {
                    var q = Acqs[p.acqs[k]];
                    acqDrop.Add(p.acqs[k]); p.acqs.RemoveAt(k);
                    Credit(job.seat, q.valeur);
                    Emit(BPEv.Info, job.seat, $"{p.name} vend {q.nom} {q.valeur} € (bénéfice {q.valeur - q.achat} €).");
                    // Commission : chacun lance le de a son tour (le vendeur d'abord), le plus haut la touche.
                    commission = q.commission; commRolls.Clear();
                    Emit(BPEv.Info, job.seat, $"Commission de {q.commission} € : tout le monde lance le dé, le plus gros chiffre la touche !");
                    var cj = new List<Job>();
                    for (int i = 0; i < players.Count; i++) cj.Add(new Job { t = Task.CommRoll, seat = (job.seat + i) % players.Count });
                    cj.Add(new Job { t = Task.CommResolve, seat = job.seat });
                    jobs.InsertRange(0, cj);
                }
                break;
            }
            case "lotto":  // lotto|1 : je joue (100 €), lotto|0 : je ne joue pas
            {
                if (job == null || job.t != Task.LottoJoin || a.Length < 2) return false;
                jobs.RemoveAt(0);
                if (a[1] != "0") { lottoIn.Add(job.seat); Debit(job.seat, 100, Bank); lottoPot += 100; Emit(BPEv.Info, job.seat, $"{players[job.seat].name} mise 100 € à la loterie."); }
                break;
            }
            case "lottoroll":   // un participant lance le de de la loterie
            {
                if (job == null || job.t != Task.LottoRoll) return false;
                jobs.RemoveAt(0);
                int v = DieValue(a);
                EmitRoll(job.seat, v, a);
                lottoRolls[job.seat] = v;
                Emit(BPEv.Info, job.seat, $"Loterie : {players[job.seat].name} fait {v}.");
                break;
            }
            case "commroll":   // un joueur lance le de de la commission
            {
                if (job == null || job.t != Task.CommRoll) return false;
                jobs.RemoveAt(0);
                int v = DieValue(a);
                EmitRoll(job.seat, v, a);
                commRolls[job.seat] = v;
                Emit(BPEv.Info, job.seat, $"Commission : {players[job.seat].name} fait {v}.");
                break;
            }
            case "besoinroll":  // Besoin d'argent ? : le joueur lance le de apres avoir mise
            {
                if (job == null || job.t != Task.BesoinRoll) return false;
                jobs.RemoveAt(0);
                int v = DieValue(a), x = job.a;
                var p = players[job.seat];
                EmitRoll(job.seat, v, a);
                if (v >= 5) { Credit(job.seat, x * 10); Emit(BPEv.Info, job.seat, $"Besoin d'argent ? {p.name} fait {v} : il touche {x * 10} € !"); }
                else { Debit(job.seat, x, Pot); Emit(BPEv.Info, job.seat, $"Besoin d'argent ? {p.name} fait {v} : ses {x} € partent à la cagnotte."); }
                break;
            }
            case "repay":  // repay|nombre de prets rembourses
            {
                if (job == null || job.t != Task.Repay || a.Length < 2 || !int.TryParse(a[1], out int k)) return false;
                var p = players[job.seat];
                if (k < 0 || k > p.loans || k * LoanSize > p.money) return false;
                jobs.RemoveAt(0);
                if (k > 0) { p.loans -= k; Debit(job.seat, k * LoanSize, Bank); Emit(BPEv.Info, job.seat, $"{p.name} rembourse {k} prêt{(k > 1 ? "s" : "")} ({k * LoanSize} €)."); }
                break;
            }
            // Au tour du joueur, avant de lancer : epargner, retirer, emprunter, jouer une carte "Besoin d'argent ?".
            case "besoin" when job != null && job.t == Task.BesoinBet:   // Jour de paye : carte "Besoin d'argent ?" obligatoire
            {
                if (a.Length < 2 || !int.TryParse(a[1], out int x) || x <= 0 || x >= 300 || x % 50 != 0) return false;
                var p = players[job.seat];
                jobs.RemoveAt(0);
                if (p.besoin.Count > 0) { mailDrop.Add(p.besoin[0]); p.besoin.RemoveAt(0); }
                Emit(BPEv.Info, job.seat, $"Besoin d'argent ? {p.name} mise {x} € : 5 ou 6 au dé et il touche {x * 10} € !");
                jobs.Insert(0, new Job { t = Task.BesoinRoll, seat = job.seat, a = x });
                return true;
            }
            case "save": case "withdraw": case "borrow": case "besoin":
            {
                if (job != null || rolled || a.Length < 2 || !int.TryParse(a[1], out int x) || x <= 0) return false;
                var p = Current;
                if (a[0] == "save")
                {
                    if (!CanDeposit(turn) || x > p.money) return false;
                    p.money -= x; p.savings += x;
                    Emit(BPEv.Info, turn, $"{p.name} place {x} € sur son livret d'épargne.");
                }
                else if (a[0] == "withdraw")
                {
                    if (x > p.savings) return false;
                    p.savings -= x; p.money += x;
                    Debit(turn, WithdrawFee, Bank);
                    Emit(BPEv.Info, turn, $"{p.name} retire {x} € de son livret (frais : {WithdrawFee} €).");
                }
                else if (a[0] == "borrow")
                {
                    if (x > 10) return false;
                    p.loans += x; p.money += x * LoanSize;
                    Emit(BPEv.Info, turn, $"{p.name} emprunte {x * LoanSize} € à la banque.");
                }
                else
                {
                    if (p.besoin.Count == 0 || x >= 300 || x % 50 != 0 || x > p.money) return false;
                    mailDrop.Add(p.besoin[0]); p.besoin.RemoveAt(0);
                    Emit(BPEv.Info, turn, $"Besoin d'argent ? {p.name} mise {x} € : 5 ou 6 au dé et il touche {x * 10} € !");
                    jobs.Insert(0, new Job { t = Task.BesoinRoll, seat = turn, a = x });   // il lance lui-meme le de
                }
                return true;
            }
            default: return false;
        }
        Run();
        return true;
    }

    // Deplacement : on s'arrete toujours au Jour de paye.
    void Move(int seat, int steps)
    {
        var p = players[seat];
        int from = p.pos;
        p.pos = Math.Min(Days, p.pos + steps);
        var e = Emit(BPEv.Moved, seat); e.from = from; e.to = p.pos;
        jobs.Add(new Job { t = Task.Cell, seat = seat, a = p.pos });
    }

    // Traite les taches automatiques jusqu'a la prochaine decision ; file vide : joueur suivant.
    void Run()
    {
        while (Pending == null && jobs.Count > 0)   // Pending retire au passage les remboursements inutiles
        {
            var j = jobs[0]; jobs.RemoveAt(0);
            var more = new List<Job>();
            Do(j, more);
            jobs.InsertRange(0, more);
        }
        if (Pending == null && jobs.Count == 0 && rolled) NextTurn();
    }

    void Do(Job j, List<Job> more)
    {
        var p = players[j.seat];
        switch (j.t)
        {
            case Task.Cell: DoCell(j.seat, j.a, more); break;
            case Task.MailOpen:
            {
                int c = j.a;
                p.unread.Remove(c);
                var m = Mails[c];
                var ev = Emit(BPEv.Card, j.seat, $"{p.name} ouvre son courrier : {m.nom}."); ev.deck = 0; ev.img = m.img;
                bool covered = (m.cat == "med" && p.medic) || (m.cat == "garage" && p.auto);
                switch (m.t)
                {
                    case "bill":
                        if (covered) { Emit(BPEv.Info, j.seat, $"Annulée par {(m.cat == "med" ? "Médic'Assur" : "Assur'Auto")} !"); mailDrop.Add(c); }
                        else p.bills.Add(c);
                        break;
                    case "cash":
                        if (covered) Emit(BPEv.Info, j.seat, "Annulée par Assur'Auto !");
                        else Debit(j.seat, m.v, Bank);
                        mailDrop.Add(c);
                        break;
                    case "gain": Credit(j.seat, m.v); mailDrop.Add(c); break;
                    case "assur":
                        if ((m.cat == "med" && p.medic) || (m.cat == "garage" && p.auto)) { Emit(BPEv.Info, j.seat, "Déjà assuré."); mailDrop.Add(c); }
                        else more.Add(new Job { t = Task.Insure, seat = j.seat, a = c });
                        break;
                    case "besoin": p.besoin.Add(c); break;
                    default: mailDrop.Add(c); break;
                }
                break;
            }
            case Task.CommResolve:
            {
                if (commRolls.Count == 0) break;
                int best = commRolls.Values.Max();
                var top = commRolls.Where(kv => kv.Value == best).Select(kv => kv.Key).ToList();
                if (top.Count > 1)
                {
                    Emit(BPEv.Info, j.seat, $"Égalité à {best} entre {string.Join(" et ", top.Select(s => players[s].name))} : on relance !");
                    commRolls.Clear();
                    foreach (int s in top) more.Add(new Job { t = Task.CommRoll, seat = s });
                    more.Add(new Job { t = Task.CommResolve, seat = j.seat });
                    break;
                }
                Credit(top[0], commission);
                Emit(BPEv.Lotto, top[0], $"{players[top[0]].name} touche la commission de {commission} € avec un {best} !").amount = commission;
                commRolls.Clear(); commission = 0;
                break;
            }
            case Task.BesoinCheck:
                if (p.besoin.Count > 0) Emit(BPEv.Info, j.seat, $"Fin du tour de plateau : {p.name} doit jouer {(p.besoin.Count > 1 ? $"ses {p.besoin.Count} cartes" : "sa carte")} « Besoin d'argent ? » !");
                for (int k = 0; k < p.besoin.Count; k++) more.Add(new Job { t = Task.BesoinBet, seat = j.seat });
                break;
            case Task.LottoResolve:
            {
                if (lottoIn.Count == 0) { Emit(BPEv.Info, j.seat, "Personne ne joue à la loterie."); lottoPot = 0; break; }
                if (lottoRolls.Count == 0)   // tout le monde a mise : chacun lance a son tour
                {
                    foreach (int s in lottoIn) more.Add(new Job { t = Task.LottoRoll, seat = s });
                    more.Add(new Job { t = Task.LottoResolve, seat = j.seat });
                    break;
                }
                int best = lottoRolls.Values.Max();
                var top = lottoRolls.Where(kv => kv.Value == best).Select(kv => kv.Key).ToList();
                if (top.Count > 1)   // egalite : seuls les ex aequo relancent
                {
                    Emit(BPEv.Info, j.seat, $"Égalité à {best} entre {string.Join(" et ", top.Select(s => players[s].name))} : on relance !");
                    lottoRolls.Clear();
                    foreach (int s in top) more.Add(new Job { t = Task.LottoRoll, seat = s });
                    more.Add(new Job { t = Task.LottoResolve, seat = j.seat });
                    break;
                }
                int w = top[0];
                players[w].money += lottoPot;
                events.Add(new BPEvent { type = BPEv.Pay, seat = w, from = Bank, to = w, amount = lottoPot });
                Emit(BPEv.Lotto, w, $"Loterie : {players[w].name} gagne avec un {best} et remporte {lottoPot} € !").amount = lottoPot;
                lottoIn.Clear(); lottoRolls.Clear(); lottoPot = 0;
                break;
            }
            case Task.PaydayEnd:
            {
                // Factures du mois (les assurances souscrites entre-temps annulent aussi les factures deja recues).
                foreach (int c in p.bills.ToList())
                {
                    var m = Mails[c];
                    if ((m.cat == "med" && p.medic) || (m.cat == "garage" && p.auto)) continue;
                    Debit(j.seat, m.v, Bank);
                }
                int total = p.bills.Sum(c => (Mails[c].cat == "med" && p.medic) || (Mails[c].cat == "garage" && p.auto) ? 0 : Mails[c].v);
                if (p.bills.Count > 0) Emit(BPEv.Info, j.seat, $"{p.name} règle ses factures : {total} €.");
                mailDrop.AddRange(p.bills); p.bills.Clear();
                if (p.month >= months)
                {
                    p.done = true;
                    Emit(BPEv.Month, j.seat, $"{p.name} a fini ses {months} mois ! Capital : {p.Capital} €.");
                }
                else
                {
                    p.month++;
                    int from = p.pos; p.pos = 0;
                    var e = Emit(BPEv.Moved, j.seat); e.from = from; e.to = 0;
                    Emit(BPEv.Month, j.seat, $"{p.name} commence le mois {p.month}.");
                }
                break;
            }
            case Task.ClockBack:
            {
                // Changement d'heure : chacun (a partir du joueur actif) recule d'une case et suit la nouvelle case.
                for (int k = 0; k < players.Count; k++)
                {
                    int s = (j.seat + k) % players.Count;
                    var q = players[s];
                    if (q.done || q.pos == 0) continue;
                    int from = q.pos, to = q.pos - 1;
                    if (to == 26) to = 25;                               // 27 -> Changement d'heure : on va a l'Acquisition du 25
                    q.pos = to;
                    var e = Emit(BPEv.Moved, s); e.from = from; e.to = to;
                    if (to == 0) { q.pos = Days; var e2 = Emit(BPEv.Moved, s); e2.from = 0; e2.to = Days; more.Add(new Job { t = Task.Cell, seat = s, a = Days }); }
                    else more.Add(new Job { t = Task.Cell, seat = s, a = to });
                }
                break;
            }
        }
    }

    // Pioche : courrier mis de cote ferme (surprise au Jour de paye), acquisition a acheter ou non, evenement applique.
    void DoDraw(Job j, List<Job> more)
    {
        var p = players[j.seat];
        if (j.t == Task.MailDraw)
        {
            int c = Draw(mailDeck, mailDrop);
            if (c < 0) { Emit(BPEv.Info, j.seat, "Plus aucune lettre dans la pile de courrier !"); return; }
            p.unread.Add(c);
            var ev = Emit(BPEv.Card, j.seat, $"{p.name} reçoit du courrier : on l'ouvrira au Jour de paye !"); ev.deck = 0; ev.img = -1;
        }
        else if (j.t == Task.AcqDraw)
        {
            int c = Draw(acqDeck, acqDrop);
            if (c < 0) { Emit(BPEv.Info, j.seat, "Plus aucune affaire à acheter : toutes sont chez les joueurs !"); return; }
            var q = Acqs[c];
            var ev = Emit(BPEv.Card, j.seat, $"Acquisition : {q.nom} (prix {q.achat} €, valeur réelle {q.valeur} €)."); ev.deck = 1; ev.img = q.img;
            more.Add(new Job { t = Task.Buy, seat = j.seat, a = c });
        }
        else
        {
            int c = Draw(evtDeck, evtDrop);
            if (c < 0) { Emit(BPEv.Info, j.seat, "Plus aucun événement dans la pile !"); return; }
            var e = Evts[c];
            var ev = Emit(BPEv.Card, j.seat, $"Quoi de neuf ? {e.nom} : {(e.t == "gain" ? "recevez" : "cotisez pour la cagnotte")} {e.v} €."); ev.deck = 2; ev.img = e.img;
            if (e.t == "gain") Credit(j.seat, e.v); else Debit(j.seat, e.v, Pot);
            evtDrop.Add(c);
        }
    }

    void DoCell(int seat, int d, List<Job> more)
    {
        var p = players[seat];
        var day = Board[d];
        switch (day.kind)
        {
            case Cell.Mail:
                Emit(BPEv.Info, seat, $"{p.name} : courrier ×{day.n}.");
                for (int k = 0; k < day.n; k++) more.Add(new Job { t = Task.MailDraw, seat = seat });
                break;
            case Cell.Acquisition:
                Emit(BPEv.Info, seat, $"{p.name} : Acquisition ! Piochez une carte.");
                more.Add(new Job { t = Task.AcqDraw, seat = seat });
                break;
            case Cell.Sell:
                if (p.acqs.Count > 0) more.Add(new Job { t = Task.Sell, seat = seat });
                else Emit(BPEv.Info, seat, $"Vendez ! {p.name} n'a rien à vendre.");
                break;
            case Cell.News:
                Emit(BPEv.Info, seat, $"{p.name} : Quoi de neuf ? Piochez un événement.");
                more.Add(new Job { t = Task.EvtDraw, seat = seat });
                break;
            case Cell.Pay: Emit(BPEv.Info, seat, $"{p.name} : {day.name}."); Debit(seat, day.n, Bank); break;
            case Cell.PayPot: Emit(BPEv.Info, seat, $"{p.name} : {day.name}."); Debit(seat, day.n, Pot); break;
            case Cell.AllPot:
                Emit(BPEv.Info, seat, day.name + " !");
                for (int s = 0; s < players.Count; s++) if (!players[s].done) Debit(s, day.n, Pot);
                break;
            case Cell.Birthday:
                Emit(BPEv.Info, seat, $"Joyeux anniversaire {p.name} ! Chaque joueur lui verse {day.n} €.");
                for (int s = 0; s < players.Count; s++) if (s != seat) Debit(s, day.n, seat);
                break;
            case Cell.Gain: Emit(BPEv.Info, seat, $"{p.name} : Toutou remporte un concours de beauté, {day.n} € !"); Credit(seat, day.n); break;
            case Cell.Rest: Emit(BPEv.Info, seat, $"{p.name} : {day.name}."); break;
            case Cell.Clock:
                Emit(BPEv.Info, seat, "Changement d'heure : tout le monde recule d'une case !");
                more.Add(new Job { t = Task.ClockBack, seat = seat });
                break;
            case Cell.Lottery:
                Emit(BPEv.Info, seat, "Loterie ! La banque met 1000 € en jeu, 100 € la mise.");
                lottoIn.Clear(); lottoRolls.Clear(); lottoPot = 1000;
                // Meme ceux qui ont fini leurs mois peuvent jouer a la loterie (regle officielle).
                for (int k = 0; k < players.Count; k++) more.Add(new Job { t = Task.LottoJoin, seat = (seat + k) % players.Count });
                more.Add(new Job { t = Task.LottoResolve, seat = seat });
                break;
            case Cell.Payday:
            {
                Emit(BPEv.Info, seat, $"Jour de paye pour {p.name} : salaire {Salary} € !");
                Credit(seat, Salary);
                int interest = SavingsInterest(p.savings);
                if (interest > 0) { Credit(seat, interest); Emit(BPEv.Info, seat, $"Intérêts du livret : {interest} €."); }
                if (p.loans > 0) { Debit(seat, p.loans * LoanInterest, Bank); Emit(BPEv.Info, seat, $"Intérêts des prêts : {p.loans * LoanInterest} €."); }
                if (p.unread.Count > 0) Emit(BPEv.Info, seat, $"{p.name} ouvre son courrier du mois ({p.unread.Count} lettre{(p.unread.Count > 1 ? "s" : "")}) !");
                foreach (int c in p.unread) more.Add(new Job { t = Task.MailOpen, seat = seat, a = c });
                more.Add(new Job { t = Task.BesoinCheck, seat = seat });   // fin du tour : les cartes "Besoin d'argent ?" se jouent
                more.Add(new Job { t = Task.Repay, seat = seat });
                more.Add(new Job { t = Task.PaydayEnd, seat = seat });
                break;
            }
        }
    }

    // Bareme du livret (dos du livret, repris du module Tabletop Simulator) : 50 € par tranche de 500 €, des 100 €.
    public static int SavingsInterest(int s) => s >= 9000 ? 950 : s >= 100 ? (s / 500 + 1) * 50 : 0;

    void NextTurn()
    {
        rolled = false;
        // Tout le monde a fini ses mois : l'hote (siege 0) choisit de terminer ou de prolonger.
        if (players.All(p => p.done)) { jobs.Add(new Job { t = Task.Extend, seat = 0 }); Emit(BPEv.Info, 0, $"Fin du mois {months} ! On prolonge la partie ?"); return; }
        do turn = (turn + 1) % players.Count; while (players[turn].done);
        Emit(BPEv.Info, turn, null);
    }

    void EndGame()
    {
        foreach (var p in players) { acqDrop.AddRange(p.acqs); p.acqs.Clear(); }   // les affaires non vendues ne valent rien
        var best = players.OrderByDescending(p => p.Capital).First();
        winner = best.seat;
        Emit(BPEv.Finished, winner, $"Fin de la partie ! {best.name} gagne avec un capital de {best.Capital} €.");
    }

    // --- Joueur automatique (joueur parti, autotest) ---
    public string[] Bot()
    {
        var j = Pending;
        if (j == null) return new[] { "roll" };
        var p = players[j.seat];
        if (IsDraw(j.t)) return new[] { "draw" };
        if (j.t == Task.Extend) return new[] { "extend", "0" };
        switch (j.t)
        {
            case Task.Insure: return new[] { p.money >= Mails[j.a].v + 500 ? "yes" : "no" };
            case Task.Buy: return new[] { p.money >= Acqs[j.a].achat ? "yes" : "no" };
            case Task.Sell: return new[] { "sell", p.acqs.Count > 0 ? p.acqs.Select((c, i) => (Acqs[c].valeur - Acqs[c].achat, i)).Max().i.ToString() : "-1" };
            case Task.LottoJoin: return new[] { "lotto", p.money >= 300 ? "1" : "0" };
            case Task.LottoRoll: return new[] { "lottoroll" };
            case Task.CommRoll: return new[] { "commroll" };
            case Task.BesoinRoll: return new[] { "besoinroll" };
            case Task.BesoinBet: return new[] { "besoin", "100" };
            case Task.Repay: return new[] { "repay", Math.Min(p.loans, p.money / LoanSize).ToString() };
        }
        return new[] { "roll" };
    }
}
