using System;
using System.Collections.Generic;
using System.Linq;

// La Roue de la fortune, regle du jeu TF1 Games (celle de l'emission) :
// - 4 manches (option) en deux etapes. Enigme rapide : les cases se devoilent une a une (l'hote : "reveal") ; le premier
//   qui trouve gagne 500 EUR et prend la main (faux = elimine de l'enigme rapide). Enigme longue : a son tour on tourne la
//   roue (case argent : une consonne, montant x occurrences ; 10 000 : non multiplie ; 0 : sans gain ; Caverne : on
//   empoche et on rejoue ; Banqueroute : gains de la manche perdus, main suivante ; Passe : main suivante), on achete une
//   voyelle (200 EUR, perdue si absente = main suivante) ou on propose la solution (avant de tourner ; faux = elimine
//   de la manche). Seul celui qui trouve garde les gains de la manche. Manche 2 : enigme a double sens ; celui qui la
//   trouve a 20 s pour donner la reponse (+500 EUR).
// - Finale, pour chacun du plus riche au moins riche : roue des enveloppes (montant cache), R S T L N E devoilees,
//   3 consonnes + 1 voyelle au choix, 20 s pour trouver ; trouve = le montant de l'enveloppe.
// Deterministe : la case de la roue et l'ordre des cases devoilees viennent du tirage des regles.
public enum FPhase { Intro, Rapid, Spin, Letter, Bonus, RoundEnd, FinalSpin, FinalPick, FinalSolve, FinalEnd, Over }
public enum FEv { Round, Reveal, RapidWin, RapidWrong, Spin, Letter, Miss, Vowel, Solved, WrongSolve, Bankrupt, Pass, Cave, Bonus, NoWinner, Turn,
                  Final, FinalSpin, FinalLetters, FinalWin, FinalLose, Over }
public class FEvent { public FEv type; public int seat = -1, segment = -1, count, amount; public char letter; public string text; }

public class Roue : IMatch
{
    public const int MaxPlayers = 6, VowelCost = 200, RapidPrize = 500, BonusPrize = 500, Jackpot = 10000;
    public const int Bankrupt = -1, PassTurn = -2, Cave = -3;
    public const int FinalMs = 20000, BonusMs = 20000;
    public const string Vowels = "AEIOUY", Consonants = "BCDFGHJKLMNPQRSTVWXZ", FinalGiven = "RSTLNE";
    public static readonly int[] RoundChoices = { 4, 3, 2 };
    // Roues des manches (les montants montent ; 10 000 a partir de la 3e).
    public static readonly int[][] Wheels =
    {
        new[] { Bankrupt, 100, 300, 150, 500, 200, 0, 250, PassTurn, 150, 400, 100, Cave, 300, 200, 500, Bankrupt, 250, 150, 350, 0, 200, 450, 300 },
        new[] { Bankrupt, 200, 500, 300, 800, 250, 0, 400, PassTurn, 300, 600, 200, Cave, 450, 350, 750, Bankrupt, 400, 250, 500, 0, 300, 700, 500 },
        new[] { Bankrupt, 300, 600, 400, 1000, 350, 0, 500, PassTurn, 400, Jackpot, 300, Cave, 600, 450, 900, Bankrupt, 500, 350, 700, 0, 400, 800, 600 },
        new[] { Bankrupt, 400, 800, 500, 1500, 450, 0, 700, PassTurn, 500, Jackpot, 400, Cave, 800, 600, 1200, Bankrupt, 700, Bankrupt, 900, 0, 500, 1000, 800 },
    };
    public static readonly int[] Envelopes =
        { 1000, 2000, 5000, 1500, 3000, 10000, 2500, 1000, 4000, 2000, 7500, 1500, 3000, 20000, 2500, 1000, 5000, 2000, 3500, 1500, 6000, 2500, 4000, 1000 };
    public static string SegmentText(int v) => v == Bankrupt ? "BANQUEROUTE" : v == PassTurn ? "PASSE" : v == Cave ? "CAVERNE" : v + " €";
    public static int CaveValue(int round) => 500 * (round + 1);

    public readonly List<QPlayer> players = new List<QPlayer>();   // score = banque
    public readonly int[] roundMoney;
    public readonly List<FEvent> events = new List<FEvent>();
    public readonly List<string> log = new List<string>();
    public readonly HashSet<char> used = new HashSet<char>();
    public readonly HashSet<int> shownCells = new HashSet<int>(), outs = new HashSet<int>();
    public readonly List<int> finalOrder = new List<int>();
    public readonly int rounds;
    public FPhase phase = FPhase.Intro;
    public int turn, round, value, lastSegment = -1, finalist = -1, envelope, winner = -1;
    public string category, answer, bonusAnswer;   // answer : majuscules sans accents
    readonly List<int> revealOrder = new List<int>();
    readonly Random rng;
    readonly Queue<(string c, string t)> deck, rapidDeck, finalDeck;
    readonly Queue<(string q, string a)> doubleDeck;

    public int Actor => phase == FPhase.Rapid ? Quiz.Everyone
        : phase == FPhase.Spin || phase == FPhase.Letter || phase == FPhase.Bonus ? turn
        : phase == FPhase.FinalSpin || phase == FPhase.FinalPick || phase == FPhase.FinalSolve ? finalist : -1;
    public bool Finished => phase == FPhase.Over;
    public QPlayer Current => players[turn];
    public bool InFinal => phase >= FPhase.FinalSpin && phase <= FPhase.FinalEnd;
    public int[] Wheel => InFinal ? Envelopes : Wheels[Math.Min(Math.Max(round, 1), 4) - 1];

    public Roue(IList<string> names, int option, int seed)
    {
        rng = new Random(seed);
        rounds = RoundChoices[Math.Min(option & 3, RoundChoices.Length - 1)];
        for (int i = 0; i < names.Count; i++) players.Add(new QPlayer { name = names[i], seat = i });
        roundMoney = new int[names.Count];
        var all = Puzzles.OrderBy(_ => rng.Next()).ToList();
        int q = all.Count / 3;
        deck = new Queue<(string, string)>(all.Take(q));
        rapidDeck = new Queue<(string, string)>(all.Skip(q).Take(q));
        finalDeck = new Queue<(string, string)>(all.Skip(2 * q).Where(p => Clean(p.t).Length <= 22));
        doubleDeck = new Queue<(string, string)>(DoubleSens.OrderBy(_ => rng.Next()));
        turn = 0;
    }

    public static string Clean(string s)
    {
        var d = s.ToUpperInvariant().Replace("Œ", "OE").Replace("Æ", "AE").Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }
    public static string Squash(string s) => new string(Clean(s).Where(char.IsLetterOrDigit).ToArray());

    // Case i de l'enigme visible ? (enigme rapide : cases devoilees ; sinon : lettres dites ; les signes sont toujours visibles)
    public bool Shown(int i) => answer != null && (!char.IsLetter(answer[i]) || shownCells.Contains(i) || used.Contains(answer[i]) || phase == FPhase.RoundEnd || phase == FPhase.FinalEnd || phase == FPhase.Bonus);
    public bool ConsonantsLeft => answer.Any(c => Consonants.IndexOf(c) >= 0 && !used.Contains(c));
    public bool CanSpin => phase == FPhase.Spin && ConsonantsLeft && !outs.Contains(turn);
    public bool CanBuyVowel => phase == FPhase.Spin && roundMoney[turn] >= VowelCost && Vowels.Any(v => !used.Contains(v));
    public int Hidden => Enumerable.Range(0, answer?.Length ?? 0).Count(i => !Shown(i));

    void Emit(FEv t, int seat = -1, int segment = -1, char letter = ' ', int count = 0, int amount = 0, string text = null) =>
        events.Add(new FEvent { type = t, seat = seat, segment = segment, letter = letter, count = count, amount = amount, text = text });

    void Load((string c, string t) p)
    {
        category = p.c; answer = Clean(p.t);
        used.Clear(); shownCells.Clear(); outs.Clear();
        revealOrder.Clear();
        revealOrder.AddRange(Enumerable.Range(0, answer.Length).Where(i => char.IsLetter(answer[i])).OrderBy(_ => rng.Next()));
    }

    public bool TryApply(string[] a)
    {
        events.Clear();
        if (Finished || a.Length == 0) return false;
        switch (a[0])
        {
            case "next": return Next();
            case "reveal":   // enigme rapide : une case de plus (l'hote)
                if (phase != FPhase.Rapid) return false;
                var more = revealOrder.FirstOrDefault(i => !shownCells.Contains(i) && !Shown(i));
                if (revealOrder.All(i => shownCells.Contains(i)) || revealOrder.Count(i => !shownCells.Contains(i)) <= 1)
                {   // tout est presque devoile sans gagnant : la main va au joueur suivant l'ancien gagnant
                    Emit(FEv.NoWinner);
                    StartLong((winner + 1 + players.Count) % players.Count);
                    return true;
                }
                shownCells.Add(more);
                Emit(FEv.Reveal, segment: more);
                return true;
            case "buzz":   // buzz|siege|texte (tout le monde en meme temps)
            {
                if (phase != FPhase.Rapid || a.Length < 3 || !int.TryParse(a[1], out int s) || s < 0 || s >= players.Count || outs.Contains(s)) return false;
                if (Squash(a[2]) == Squash(answer))
                {
                    players[s].score += RapidPrize;
                    Emit(FEv.RapidWin, s, amount: RapidPrize, text: answer);
                    log.Add($"{players[s].name} trouve l'énigme rapide (+{RapidPrize} €) et prend la main !");
                    StartLong(s);
                }
                else
                {
                    outs.Add(s);
                    Emit(FEv.RapidWrong, s, text: a[2]);
                    log.Add($"{players[s].name} propose « {a[2]} »... raté !");
                    if (outs.Count >= players.Count) { Emit(FEv.NoWinner); StartLong((winner + 1 + players.Count) % players.Count); }
                }
                return true;
            }
            case "spin":
            {
                if (!CanSpin) return false;
                lastSegment = rng.Next(Wheel.Length);
                value = Wheel[lastSegment];
                Emit(FEv.Spin, turn, lastSegment, amount: value);
                if (value == Bankrupt)
                {
                    log.Add($"{Current.name} : BANQUEROUTE !");
                    roundMoney[turn] = 0;
                    Emit(FEv.Bankrupt, turn);
                    NextTurn();
                }
                else if (value == PassTurn) { log.Add($"{Current.name} passe son tour."); Emit(FEv.Pass, turn); NextTurn(); }
                else if (value == Cave)
                {
                    int won = CaveValue(round);
                    roundMoney[turn] += won;
                    Emit(FEv.Cave, turn, amount: won);
                    log.Add($"{Current.name} : CAVERNE ! +{won} € et il rejoue.");
                }
                else phase = FPhase.Letter;
                return true;
            }
            case "cons":
            {
                if (phase != FPhase.Letter || a.Length < 2 || a[1].Length != 1 || Consonants.IndexOf(a[1][0]) < 0) return false;
                char c = a[1][0];
                int n = used.Contains(c) ? 0 : answer.Count(x => x == c);
                used.Add(c);
                if (n > 0)
                {
                    int won = value == Jackpot ? Jackpot : value * n;
                    roundMoney[turn] += won;
                    Emit(FEv.Letter, turn, letter: c, count: n, amount: won);
                    log.Add($"{Current.name} : {n} « {c} »" + (won > 0 ? $" (+{won} €)" : ""));
                    phase = FPhase.Spin;
                }
                else { Emit(FEv.Miss, turn, letter: c); log.Add($"{Current.name} : pas de « {c} »."); NextTurn(); }
                return true;
            }
            case "vowel":
            {
                if (!CanBuyVowel || a.Length < 2 || a[1].Length != 1 || Vowels.IndexOf(a[1][0]) < 0 || used.Contains(a[1][0])) return false;
                char v = a[1][0];
                roundMoney[turn] -= VowelCost;
                used.Add(v);
                int n = answer.Count(x => x == v);
                Emit(FEv.Vowel, turn, letter: v, count: n, amount: VowelCost);
                if (n > 0) { Emit(FEv.Letter, turn, letter: v, count: n); log.Add($"{Current.name} achète un {v} : {n} !"); }
                else { Emit(FEv.Miss, turn, letter: v); log.Add($"{Current.name} achète un {v}... absent !"); NextTurn(); }
                return true;
            }
            case "solve":
                if (phase != FPhase.Spin || a.Length < 2 || outs.Contains(turn)) return false;
                if (Squash(a[1]) == Squash(answer))
                {
                    winner = turn;
                    int won = Math.Max(0, roundMoney[turn]);
                    Current.score += won;
                    Emit(FEv.Solved, turn, amount: won, text: answer);
                    log.Add($"{Current.name} trouve « {answer} » et garde {won} € !");
                    if (bonusAnswer != null) { phase = FPhase.Bonus; Emit(FEv.Bonus, turn, text: category); }
                    else phase = FPhase.RoundEnd;
                }
                else
                {
                    outs.Add(turn);
                    Emit(FEv.WrongSolve, turn, text: a[1]);
                    log.Add($"{Current.name} propose « {a[1]} »... non ! Il ne peut plus jouer cette manche.");
                    if (outs.Count >= players.Count) { Emit(FEv.NoWinner); phase = FPhase.RoundEnd; }
                    else NextTurn();
                }
                return true;
            case "bonus":   // manche 2 : la reponse a l'enigme a double sens ("bonus|" vide = temps ecoule, l'hote)
                if (phase != FPhase.Bonus || a.Length < 2) return false;
                bool ok = a[1].Length > 0 && Quiz.Matches(a[1], new[] { bonusAnswer });
                if (ok) players[turn].score += BonusPrize;
                Emit(FEv.Bonus, turn, amount: ok ? BonusPrize : 0, text: bonusAnswer);
                log.Add(ok ? $"{Current.name} répond « {bonusAnswer} » : +{BonusPrize} € !" : $"C'était « {bonusAnswer} ».");
                if (a[1].Length > 0 && !ok) return true;   // mauvaise reponse : il peut reessayer tant que le temps court
                phase = FPhase.RoundEnd;
                return true;
            // --- Finale ---
            case "fspin":
                if (phase != FPhase.FinalSpin) return false;
                lastSegment = rng.Next(Envelopes.Length);
                envelope = Envelopes[lastSegment];
                Emit(FEv.FinalSpin, finalist, lastSegment);
                phase = FPhase.FinalPick;
                return true;
            case "fpick":   // fpick|BCDA : 3 consonnes + 1 voyelle
            {
                if (phase != FPhase.FinalPick || a.Length < 2 || a[1].Length != 4) return false;
                var l = a[1];
                if (l.Take(3).Any(c => Consonants.IndexOf(c) < 0) || Vowels.IndexOf(l[3]) < 0 || l.Distinct().Count() != 4) return false;
                foreach (var c in l) used.Add(c);
                phase = FPhase.FinalSolve;
                Emit(FEv.FinalLetters, finalist, text: l);
                return true;
            }
            case "fsolve":   // fsolve|texte (plusieurs essais) ; "fsolve|" vide = temps ecoule (l'hote)
            {
                if (phase != FPhase.FinalSolve || a.Length < 2) return false;
                bool win = a[1].Length > 0 && Squash(a[1]) == Squash(answer);
                if (!win && a[1].Length > 0) { Emit(FEv.RapidWrong, finalist, text: a[1]); return true; }
                var p = players[finalist];
                if (win) p.score += envelope;
                Emit(win ? FEv.FinalWin : FEv.FinalLose, finalist, amount: envelope, text: answer);
                log.Add(win ? $"{p.name} trouve « {answer} » et remporte l'enveloppe de {envelope} € !" : $"{p.name} n'a pas trouvé « {answer} ». L'enveloppe contenait {envelope} €.");
                phase = FPhase.FinalEnd;
                return true;
            }
        }
        return false;
    }

    bool Next()
    {
        if (phase == FPhase.Intro || phase == FPhase.RoundEnd)
        {
            if (round >= rounds) { StartFinal(); return true; }
            round++;
            Array.Clear(roundMoney, 0, roundMoney.Length);
            Load(rapidDeck.Count > 0 ? rapidDeck.Dequeue() : ("Énigme rapide", "Pique-nique"));
            bonusAnswer = null;
            phase = FPhase.Rapid;
            Emit(FEv.Round, text: "Énigme rapide · " + category);
            log.Add($"Manche {round} : énigme rapide ({category}) !");
            return true;
        }
        if (phase == FPhase.FinalEnd)
        {
            int k = finalOrder.IndexOf(finalist);
            if (k + 1 >= finalOrder.Count) { phase = FPhase.Over; Emit(FEv.Over); log.Add("Fin de la partie !"); return true; }
            StartFinalist(finalOrder[k + 1]);
            return true;
        }
        return false;
    }

    void StartLong(int first)
    {
        if (round == 2 && doubleDeck.Count > 0)
        {
            var d = doubleDeck.Dequeue();
            Load(("Énigme à double sens", d.q));
            bonusAnswer = d.a;
        }
        else Load(deck.Count > 0 ? deck.Dequeue() : ("Expression", "Pique-nique"));
        turn = first; winner = first;
        phase = FPhase.Spin;
        Emit(FEv.Round, text: category);
        Emit(FEv.Turn, turn);
    }

    void NextTurn()
    {
        for (int k = 1; k <= players.Count; k++)
        {
            int s = (turn + k) % players.Count;
            if (!outs.Contains(s)) { turn = s; break; }
        }
        phase = FPhase.Spin;
        Emit(FEv.Turn, turn);
    }

    void StartFinal()
    {
        finalOrder.Clear();
        finalOrder.AddRange(players.OrderByDescending(p => p.score).ThenBy(p => p.seat).Select(p => p.seat));
        Emit(FEv.Final);
        log.Add("LA FINALE ! Chacun son tour, du plus riche au moins riche.");
        StartFinalist(finalOrder[0]);
    }

    void StartFinalist(int s)
    {
        finalist = s; turn = s;
        Load(finalDeck.Count > 0 ? finalDeck.Dequeue() : deck.Dequeue());
        foreach (var c in FinalGiven) used.Add(c);
        phase = FPhase.FinalSpin;
        Emit(FEv.Turn, s);
    }

    // --- Bots (hors ligne, ou joueur parti en ligne) : consonnes les plus frequentes, une voyelle de temps en temps,
    // la solution quand l'enigme est bien devoilee. Generateur a part : le tirage des regles reste le meme partout.
    static readonly Random botRng = new Random();
    const string ConsFreq = "SRTNLCDMPVGBFHQJXZKW", VowFreq = "EAIUOY";
    public bool BotKnows(float bonusChance = 0)
    {
        int letters = answer.Count(char.IsLetter);
        return Hidden <= letters * 0.4 + bonusChance * letters;
    }
    public string[] Bot()
    {
        switch (phase)
        {
            case FPhase.Letter:
            {
                var pool = ConsFreq.Where(c => !used.Contains(c)).ToList();
                return new[] { "cons", (botRng.NextDouble() < 0.75 ? pool[0] : pool[botRng.Next(Math.Min(6, pool.Count))]).ToString() };
            }
            case FPhase.Spin:
                if (BotKnows((float)botRng.NextDouble() * 0.1f) || (!CanSpin && !CanBuyVowel)) return new[] { "solve", answer };
                if (CanBuyVowel && (!CanSpin || botRng.NextDouble() < 0.3)) return new[] { "vowel", VowFreq.First(v => !used.Contains(v)).ToString() };
                return CanSpin ? new[] { "spin" } : new[] { "solve", answer };
            case FPhase.Bonus: return new[] { "bonus", botRng.NextDouble() < 0.5 ? bonusAnswer : "" };
            case FPhase.FinalSpin: return new[] { "fspin" };
            case FPhase.FinalPick: return new[] { "fpick", "DMPA" };
        }
        return null;
    }

    // --- Enigmes (categorie, texte) ---------------------------------------------------------
    public static readonly (string c, string t)[] Puzzles =
    {
        ("Expression", "Avoir un poil dans la main"), ("Expression", "Poser un lapin"), ("Expression", "Casser les pieds"),
        ("Expression", "Mettre les pieds dans le plat"), ("Expression", "Avoir la tête dans les nuages"), ("Expression", "Tomber dans les pommes"),
        ("Expression", "Donner sa langue au chat"), ("Expression", "Coûter les yeux de la tête"), ("Expression", "Être sur son trente et un"),
        ("Expression", "Avoir le cafard"), ("Expression", "Faire la grasse matinée"), ("Expression", "Raconter des salades"),
        ("Expression", "Avoir un chat dans la gorge"), ("Expression", "Se prendre un râteau"), ("Expression", "Rouler sur l'or"),
        ("Expression", "Avoir les dents longues"), ("Expression", "Mettre son grain de sel"), ("Expression", "Tirer les vers du nez"),
        ("Expression", "Avoir du pain sur la planche"), ("Expression", "Prendre ses jambes à son cou"), ("Expression", "Être comme un poisson dans l'eau"),
        ("Expression", "Couper la poire en deux"), ("Expression", "Avoir le coeur sur la main"), ("Expression", "Se mettre le doigt dans l'oeil"),
        ("Expression", "À la croisée des chemins"), ("Expression", "Avoir la main verte"), ("Expression", "Faire chou blanc"),
        ("Proverbe", "Qui vole un oeuf vole un boeuf"), ("Proverbe", "Petit à petit l'oiseau fait son nid"), ("Proverbe", "Tel père tel fils"),
        ("Proverbe", "L'habit ne fait pas le moine"), ("Proverbe", "Il faut battre le fer tant qu'il est chaud"), ("Proverbe", "Qui dort dîne"),
        ("Proverbe", "Après la pluie le beau temps"), ("Proverbe", "Les absents ont toujours tort"), ("Proverbe", "Mieux vaut tard que jamais"),
        ("Proverbe", "La nuit porte conseil"), ("Proverbe", "Un tiens vaut mieux que deux tu l'auras"), ("Proverbe", "Chat échaudé craint l'eau froide"),
        ("Cinéma", "Le Seigneur des anneaux"), ("Cinéma", "Retour vers le futur"), ("Cinéma", "Intouchables"), ("Cinéma", "Le fabuleux destin d'Amélie Poulain"),
        ("Cinéma", "Bienvenue chez les Ch'tis"), ("Cinéma", "Les Visiteurs"), ("Cinéma", "Le Dîner de cons"), ("Cinéma", "Titanic"),
        ("Cinéma", "La Guerre des étoiles"), ("Cinéma", "Le Parrain"), ("Cinéma", "Jurassic Park"), ("Cinéma", "La Cité de la peur"),
        ("Cinéma", "Les Bronzés font du ski"), ("Cinéma", "Le Roi lion"), ("Cinéma", "La Reine des neiges"), ("Cinéma", "Toy Story"),
        ("Cinéma", "Le Monde de Nemo"), ("Cinéma", "Les Aristochats"), ("Cinéma", "La Belle et la Bête"), ("Cinéma", "Ratatouille"),
        ("Personnage", "Harry Potter"), ("Personnage", "Le Petit Chaperon rouge"), ("Personnage", "Blanche-Neige"), ("Personnage", "Le Père Noël"),
        ("Personnage", "Sherlock Holmes"), ("Personnage", "Bob l'éponge"), ("Personnage", "Super Mario"), ("Personnage", "Tintin et Milou"),
        ("Personnage", "Dark Vador"), ("Personnage", "Pikachu"), ("Personnage", "Astérix le Gaulois"), ("Personnage", "Lucky Luke"),
        ("Personnalité", "Napoléon Bonaparte"), ("Personnalité", "Céline Dion"), ("Personnalité", "Zinedine Zidane"), ("Personnalité", "Victor Hugo"),
        ("Personnalité", "Marie Curie"), ("Personnalité", "Albert Einstein"), ("Personnalité", "Louis de Funès"), ("Personnalité", "Jeanne d'Arc"),
        ("Lieu", "La tour Eiffel"), ("Lieu", "Le Mont-Saint-Michel"), ("Lieu", "La muraille de Chine"), ("Lieu", "Les pyramides d'Égypte"),
        ("Lieu", "La Côte d'Azur"), ("Lieu", "Le château de Versailles"), ("Lieu", "La statue de la Liberté"), ("Lieu", "Les chutes du Niagara"),
        ("Lieu", "Le Grand Canyon"), ("Lieu", "La place de la Concorde"),
        ("Cuisine", "Une tarte aux pommes"), ("Cuisine", "Un croque-monsieur"), ("Cuisine", "La raclette savoyarde"), ("Cuisine", "Une crêpe au chocolat"),
        ("Cuisine", "Le boeuf bourguignon"), ("Cuisine", "Des frites maison"), ("Cuisine", "Une baguette de pain"), ("Cuisine", "Le gratin dauphinois"),
        ("Cuisine", "Une mousse au chocolat"), ("Cuisine", "Un pique-nique au soleil"),
        ("Maison", "Le lave-vaisselle"), ("Maison", "Une machine à laver"), ("Maison", "Le canapé du salon"), ("Maison", "Une brosse à dents"),
        ("Maison", "La télécommande"), ("Maison", "Un aspirateur"), ("Maison", "Le réfrigérateur"),
        ("Métier", "Boulanger pâtissier"), ("Métier", "Pompier volontaire"), ("Métier", "Astronaute"), ("Métier", "Chirurgien dentiste"),
        ("Métier", "Professeur des écoles"), ("Métier", "Chauffeur de taxi"),
        ("Animal", "Un hippopotame"), ("Animal", "Le crocodile du Nil"), ("Animal", "Une coccinelle"), ("Animal", "Un ours polaire"),
        ("Animal", "Le paresseux"), ("Animal", "Une girafe"), ("Animal", "Un dauphin"), ("Animal", "Un kangourou"),
        ("Jeu vidéo", "Minecraft"), ("Jeu vidéo", "Mario Kart"), ("Jeu vidéo", "The Legend of Zelda"), ("Jeu vidéo", "Pac-Man"),
        ("Jeu vidéo", "Animal Crossing"), ("Jeu vidéo", "Tetris"),
        ("Loisirs", "Le Monopoly"), ("Loisirs", "Les petits chevaux"), ("Loisirs", "La bataille navale"), ("Loisirs", "Le jeu des sept familles"),
        ("Sport", "La Coupe du monde de football"), ("Sport", "Le Tour de France"), ("Sport", "Les Jeux olympiques"), ("Sport", "Le saut à la perche"),
        ("Événement", "Un anniversaire surprise"), ("Événement", "La fête de la musique"), ("Événement", "Le quatorze juillet"), ("Événement", "Les grandes vacances"),
    };

    // Manche 2 : enigmes a double sens (la phrase a decouvrir, puis la reponse).
    public static readonly (string q, string a)[] DoubleSens =
    {
        ("D'or ou de chemise", "Un bouton"), ("De porte ou de sol", "Une clé"), ("De bois ou de marbre", "Une table"),
        ("De vélo ou de musique", "Une chaîne"), ("D'arbre ou de famille", "Une branche"), ("De table ou de joie", "Un pied"),
        ("De pêche ou de ski", "Une canne"), ("De tennis ou de hockey", "Une raquette"), ("De mer ou de rire", "Une vague"),
        ("De soleil ou de foudre", "Un coup"), ("D'avion ou de moulin", "Une aile"), ("De livre ou de maison", "Une couverture"),
        ("De vie ou de nuit", "Un train"), ("De rose ou de cactus", "Une épine"), ("De piano ou d'ordinateur", "Un clavier"),
        ("De cheval ou de vélo", "Une selle"), ("De théâtre ou de lit", "Un rideau"), ("De montre ou de pin", "Une aiguille"),
        ("De poisson ou de piano", "Une queue"), ("De crayon ou d'or", "Une mine"),
    };
}
