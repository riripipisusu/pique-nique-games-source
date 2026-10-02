using System;
using System.Collections.Generic;
using System.Linq;

// Loup-garou (regles de l'ancien projet de l'utilisatrice, cartes dans Resources/LoupGarou) : 23 roles.
// Deroulement : nuit (chaque role agit en meme temps ; les loups votent leur victime), puis tour de la Sorciere,
// aube (morts annoncees), au 1er jour election du maire, puis debat et vote du village, verdict, nuit suivante.
// Le Chasseur tire en mourant, le Maire mort designe son successeur, le Dictateur fait son coup d'Etat au matin.
// L'hote (ou le jeu hors ligne) coupe chaque etape au bout du temps imparti ("timeout") ; les bots jouent au hasard.
// Option : un bit par role special active (bit = (int)Role) ; les loups sont ajoutes selon le nombre de joueurs.
public enum Role
{
    Villageois, Loup, Voyante, Sorciere, Chasseur, Cupidon, Garde, PetiteFille, LoupBlanc, LoupNoir, Brumeux, Anesthesiste,
    Corbeau, Dictateur, Dresseur, Pyromane, Assassin, Ange, Blaster, Influenceur, Medium, Necromancien, Ninja
}
public enum Team { Village, Loups, Seul }
public enum WPh { Night, Witch, Dawn, Hunter, Heir, Dictator, Election, Vote, Verdict, Over, Tie }
public enum WGEv { Night, Day, Death, Saved, Seen, Info, Mayor, Vote, Over, Chat, Step, Tie, Left }
public class WGEvent { public WGEv type; public int seat = -1, other = -1; public string text; }

public class LoupGarou : IMatch
{
    public const int MaxPlayers = 12, MinPlayers = 4;
    public static readonly int DefaultOption = Bit(Role.Voyante) | Bit(Role.Sorciere) | Bit(Role.Chasseur) | Bit(Role.Cupidon) | Bit(Role.Garde) | Bit(Role.PetiteFille);
    public static int Bit(Role r) => 1 << (int)r;
    // Bits 24-27 de l'option : le lieu (AgrouMap.All, 15 = au hasard).
    public static int MapOf(int opt) => (opt >> 24) & 15;
    public static int WithMap(int opt, int map) => opt & ~(15 << 24) | map << 24;
    public static readonly Role[] Specials = Enum.GetValues(typeof(Role)).Cast<Role>().Where(r => r != Role.Villageois && r != Role.Loup).ToArray();

    // --- Les roles : nom, fichier de la carte, camp ---------------------------------------------------
    public static string Name(Role r) => r switch
    {
        Role.Villageois => "Villageois", Role.Loup => "Loup-garou", Role.Voyante => "Voyante", Role.Sorciere => "Sorcière",
        Role.Chasseur => "Chasseur", Role.Cupidon => "Cupidon", Role.Garde => "Garde", Role.PetiteFille => "Petite fille",
        Role.LoupBlanc => "Loup blanc", Role.LoupNoir => "Loup noir", Role.Brumeux => "Loup brumeux", Role.Anesthesiste => "Loup anesthésiste",
        Role.Corbeau => "Corbeau", Role.Dictateur => "Dictateur", Role.Dresseur => "Dresseur", Role.Pyromane => "Pyromane",
        Role.Assassin => "Assassin", Role.Ange => "Ange", Role.Blaster => "Blaster", Role.Influenceur => "Influenceur",
        Role.Medium => "Médium", Role.Necromancien => "Nécromancien", _ => "Ninja",
    };
    public static string Card(Role r) => r switch
    {
        Role.Loup => "loup", Role.LoupBlanc => "loupblanc", Role.LoupNoir => "loupnoir", Role.PetiteFille => "petitefille",
        Role.Sorciere => "sorciere", Role.Anesthesiste => "anesthesiste", _ => r.ToString().ToLowerInvariant(),
    };
    public static Team TeamOf(Role r) => r == Role.Loup || r == Role.LoupNoir || r == Role.Brumeux || r == Role.Anesthesiste ? Team.Loups
        : r == Role.LoupBlanc || r == Role.Pyromane || r == Role.Assassin || r == Role.Ange || r == Role.Influenceur || r == Role.Ninja ? Team.Seul : Team.Village;
    public static bool IsWolf(Role r) => TeamOf(r) == Team.Loups || r == Role.LoupBlanc;          // se reveille avec les loups
    static bool Killer(Role r) => IsWolf(r) || r == Role.Assassin || r == Role.Pyromane || r == Role.Ninja;   // hostile au village

    public class Player
    {
        public string name; public int seat; public Role role; public bool alive = true, mayor;
        public int lover = -1, bomb = -1, bombs = 1;      // bomb : tours avant explosion (si porteur) ; bombs : bombes du Blaster
        public bool doused, asleep, poisoned, lifeUsed, deathUsed, convertUsed, fogUsed, coupUsed, missionDone, angeDone;
        public int guardLast = -1, missionTarget = -1;
        public int deathLog = -1;
        public bool left;          // parti en cours de partie (retire, comme dans Agrou)   // taille du journal juste avant l'annonce de sa mort (le carnet attend la revelation)
    }

    public readonly List<Player> players = new List<Player>();
    public readonly List<WGEvent> events = new List<WGEvent>();
    public readonly List<string> log = new List<string>();
    public readonly List<(int seat, string chan, string text)> chat = new List<(int, string, string)>();
    public WPh phase = WPh.Night;
    public int day = 0, night = 1;
    public string winner;                                   // texte de fin
    public readonly List<int> winners = new List<int>();

    // Actions de la nuit en cours.
    public readonly Dictionary<int, int> wolfVotes = new Dictionary<int, int>();
    public readonly HashSet<int> nightDone = new HashSet<int>();
    readonly Dictionary<int, int> target = new Dictionary<int, int>();         // cible principale du role (voyante, garde...)
    readonly Dictionary<int, int> target2 = new Dictionary<int, int>();        // seconde cible (cupidon, pyromane)
    readonly HashSet<int> flags = new HashSet<int>();                          // convertir / brouillard / coup / immoler
    public int victim = -1;                                                    // victime des loups (connue de la Sorciere)
    public bool witchSave; public int witchKill = -1;
    // Jour.
    public readonly Dictionary<int, int> votes = new Dictionary<int, int>();
    public int crowed = -1;                         // +2 voix contre lui au vote
    public bool fog;                                // votes caches
    public int coupBy = -1, coupDue = -1;           // dictateur : choisit sa cible ce matin ; meurt le lendemain s'il a rate
    public readonly List<(WPh kind, int seat)> pending = new List<(WPh, int)>();   // chasseurs, successeurs de maire a traiter
    public int shownDeath = -1;
    public int eliminated = -1;
    public readonly List<int> tied = new List<int>();   // ex aequo du vote, a la potence pendant le revote
    readonly Random rng;

    public int Alive => players.Count(p => p.alive);
    public Player MayorP => players.FirstOrDefault(p => p.alive && p.mayor);
    public bool Finished => phase == WPh.Over;
    public int Actor => phase == WPh.Hunter || phase == WPh.Heir || phase == WPh.Dictator ? (pending.Count > 0 ? pending[0].seat : -1) : phase == WPh.Over ? -1 : Quiz.Everyone;

    public LoupGarou(IList<string> names, int option, int seed)
    {
        rng = new Random(seed);
        for (int i = 0; i < names.Count; i++) players.Add(new Player { name = names[i], seat = i });
        // Composition : 1 loup pour 4 joueurs ; les roles speciaux actives, tires au hasard, remplacent loups et villageois.
        int n = players.Count, wolves = Math.Max(1, (int)Math.Round(n / 4.0));
        var enabled = Specials.Where(r => (option & Bit(r)) != 0).OrderBy(_ => rng.Next()).ToList();
        var deck = new List<Role>();
        foreach (var r in enabled.Where(r => TeamOf(r) == Team.Loups)) if (deck.Count(IsWolf) < wolves) deck.Add(r);
        while (deck.Count(IsWolf) < wolves) deck.Add(Role.Loup);
        foreach (var r in enabled.Where(r => TeamOf(r) != Team.Loups)) if (deck.Count < n) deck.Add(r);
        while (deck.Count < n) deck.Add(Role.Villageois);
        deck = deck.OrderBy(_ => rng.Next()).ToList();
        for (int i = 0; i < n; i++) players[i].role = deck[i];
        foreach (var p in players.Where(p => p.role == Role.Influenceur))
        {
            var others = players.Where(o => o.seat != p.seat).ToList();
            p.missionTarget = others[rng.Next(others.Count)].seat;
        }
        log.Add("Les cartes sont distribuées. La nuit tombe sur le village...");
        StartNight();
    }

    // Test : donne le role r au siege s (echange avec celui qui l'a, sinon remplace ; il reste toujours un loup).
    public void TestSetRole(int s, Role r)
    {
        var other = players.FirstOrDefault(p => p.role == r && p.seat != s);
        var old = players[s].role;
        if (other != null) { other.role = old; players[s].role = r; }
        else
        {
            players[s].role = r;
            if (IsWolf(old) && !players.Any(p => IsWolf(p.role)))
                players.First(p => p.seat != s && !IsWolf(p.role)).role = Role.Loup;
        }
        foreach (var p in players.Where(p => p.role == Role.Influenceur && p.missionTarget < 0))
            p.missionTarget = players.First(o => o.seat != p.seat).seat;
        if (phase == WPh.Night) { events.Clear(); step = -1; NextStep(); }   // la nuit repart avec les nouveaux roles
    }

    // --- La nuit, role par role (comme Agrou) : chaque etape reveille ses roles, avec son propre temps ------------
    public static readonly (string name, Role[] roles, float time)[] Steps =
    {
        ("Cupidon", new[] { Role.Cupidon }, 40),
        ("La Voyante", new[] { Role.Voyante }, 30),
        ("Le Garde", new[] { Role.Garde }, 20),
        ("Le Corbeau", new[] { Role.Corbeau }, 20),
        ("Le Dictateur", new[] { Role.Dictateur }, 15),
        ("Les loups-garous", new[] { Role.Loup, Role.LoupNoir, Role.Brumeux, Role.Anesthesiste, Role.LoupBlanc }, 60),
        ("Le Loup blanc", new[] { Role.LoupBlanc }, 20),
        ("L'Assassin", new[] { Role.Assassin }, 15),
        ("Le Pyromane", new[] { Role.Pyromane }, 40),
        ("Le Blaster", new[] { Role.Blaster }, 20),
    };
    public int testShield = -1;    // autotest : siege epargne la nuit
    public int step = -1;          // etape en cours de la nuit
    public int stepSerial;         // change a chaque etape (le jeu remet le minuteur)
    public const int WolfStep = 5, WhiteStep = 6;
    public string StepName => phase == WPh.Night && step >= 0 && step < Steps.Length ? Steps[step].name : "";
    public float StepTime => step >= 0 && step < Steps.Length ? Steps[step].time : 30;

    // Ce joueur agit-il a l'etape en cours ?
    public bool NightRole(Player p)
    {
        if (!p.alive || phase != WPh.Night || step < 0 || step >= Steps.Length || Array.IndexOf(Steps[step].roles, p.role) < 0) return false;
        switch (p.role)
        {
            case Role.Cupidon: return night == 1;
            case Role.Dictateur: return !p.coupUsed;
            case Role.Blaster: return p.bombs > 0 && !players.Any(o => o.alive && o.bomb >= 0);
            case Role.LoupBlanc: return step == WolfStep || WhiteNight;
        }
        return true;
    }

    void NextStep()
    {
        nightDone.Clear();
        do step++; while (step < Steps.Length && !players.Any(NightRole));
        if (step >= Steps.Length) { EndNight(); return; }
        stepSerial++;
        events.Add(new WGEvent { type = WGEv.Step, text = Steps[step].name });
    }
    public bool CanAnesth => night >= 2;
    public bool WhiteNight => night % 2 == 0;
    public string Hint(Player p) => p.role switch
    {
        Role.Loup or Role.LoupNoir or Role.Brumeux or Role.Anesthesiste => "Avec les loups, choisis la victime de cette nuit.",
        Role.LoupBlanc => step == WhiteStep ? "Dévore seul un joueur, en secret (ou passe)." : WhiteNight ? "Vote avec les loups ; ensuite tu pourras dévorer seul un joueur." : "Vote avec les loups pour ne pas te faire repérer.",
        Role.Voyante => "Choisis un joueur : tu découvriras sa carte.",
        Role.Garde => "Choisis qui protéger cette nuit (pas le même que la dernière fois).",
        Role.Cupidon => "Choisis deux amoureux.",
        Role.Corbeau => "Désigne un joueur : il aura 2 voix contre lui demain.",
        Role.Pyromane => "Imbibe d'essence jusqu'à deux joueurs, ou immole tous ceux que tu as imbibés.",
        Role.Assassin => "Choisis qui assassiner (ou personne).",
        Role.Dictateur => "Tu peux préparer un coup d'État pour demain matin.",
        Role.Blaster => "Jette ta bombe sur un joueur.",
        _ => "",
    };

    void StartNight()
    {
        phase = WPh.Night;
        wolfVotes.Clear(); nightDone.Clear(); target.Clear(); target2.Clear(); flags.Clear();
        victim = -1; witchSave = false; witchKill = -1;
        foreach (var p in players) p.asleep = false;
        events.Add(new WGEvent { type = WGEv.Night, text = $"Nuit {night}" });
        log.Add($"Nuit {night} : le village s'endort.");
        step = -1;
        NextStep();
    }

    public bool TryApply(string[] a)
    {
        events.Clear();
        if (Finished || a.Length == 0) return false;
        switch (a[0])
        {
            case "chat":   // chat|siege|canal|texte
            {
                if (a.Length < 4 || !int.TryParse(a[1], out int s) || s < 0 || s >= players.Count || a[3].Trim().Length == 0) return false;
                if (!CanChat(s, a[2])) return false;
                chat.Add((s, a[2], a[3].Trim()));
                events.Add(new WGEvent { type = WGEv.Chat, seat = s, text = a[2] });
                return true;
            }
            case "night":  // night|siege|action|cible|cible2
                return Night(a);
            case "witch":  // witch|siege|save|kill|cible  (ou "none")
                return Witch(a);
            case "vote":   // vote|siege|cible
            {
                if ((phase != WPh.Vote && phase != WPh.Election && phase != WPh.Tie) || a.Length < 3 || !int.TryParse(a[1], out int s) || !int.TryParse(a[2], out int t)) return false;
                if (!CanVote(s) || t < 0 || t >= players.Count || !players[t].alive) return false;
                if (phase == WPh.Tie && (!tied.Contains(t) || t == s)) return false;
                votes[s] = t;
                events.Add(new WGEvent { type = WGEv.Vote, seat = s, other = t });
                if (players.Where(p => CanVote(p.seat)).All(p => votes.ContainsKey(p.seat))) EndVote();
                return true;
            }
            case "shuriken":   // ninja, le jour : la cible mourra demain matin
            {
                if ((phase != WPh.Vote && phase != WPh.Election) || a.Length < 3 || !int.TryParse(a[1], out int s) || !int.TryParse(a[2], out int t)) return false;
                var p = players[s];
                if (!p.alive || p.role != Role.Ninja || players.Any(o => o.poisoned) || !players[t].alive || t == s) return false;
                players[t].poisoned = true;
                events.Add(new WGEvent { type = WGEv.Info, seat = s, text = $"Ton shuriken empoisonné atteint {players[t].name}." });
                return true;
            }
            case "hunt": case "heir": case "dict":   // chasseur / successeur du maire / coup d'Etat : hunt|siege|cible
            {
                var want = a[0] == "hunt" ? WPh.Hunter : a[0] == "heir" ? WPh.Heir : WPh.Dictator;
                if (phase != want || pending.Count == 0 || a.Length < 3 || !int.TryParse(a[1], out int s) || s != pending[0].seat || !int.TryParse(a[2], out int t)) return false;
                if (t < 0 || t >= players.Count || !players[t].alive || t == s) return false;
                Resolve(want, s, t);
                return true;
            }
            case "quit":   // quit|siege (l'hote) : le joueur a quitte, il est retire de la partie
            {
                if (a.Length < 2 || !int.TryParse(a[1], out int s) || s < 0 || s >= players.Count || players[s].left) return false;
                var p = players[s];
                p.left = true;
                bool wasAlive = p.alive;
                p.alive = false; p.bomb = -1;
                events.Add(new WGEvent { type = WGEv.Left, seat = s });
                log.Add($"{p.name} a quitté la partie.");
                if (wasAlive && p.mayor)
                {
                    p.mayor = false;
                    var heir = players.Where(o => o.alive).OrderBy(_ => rng.Next()).FirstOrDefault();
                    if (heir != null) { heir.mayor = true; log.Add($"{heir.name} reprend l'écharpe de maire."); events.Add(new WGEvent { type = WGEv.Mayor, seat = heir.seat }); }
                }
                wolfVotes.Remove(s); votes.Remove(s); tied.Remove(s);
                pending.RemoveAll(x => x.seat == s);
                if (wasAlive && CheckWin()) return true;
                if (phase == WPh.Night) CheckNightDone();
                else if ((phase == WPh.Vote || phase == WPh.Election || phase == WPh.Tie) && players.Where(o => CanVote(o.seat)).All(o => votes.ContainsKey(o.seat))) EndVote();
                else if ((phase == WPh.Hunter || phase == WPh.Heir || phase == WPh.Dictator) && pending.Count == 0) AfterDeaths(!dayPhase);
                return true;
            }
            case "timeout": return Timeout();
            case "next":   // l'hote, apres l'annonce de l'aube ou du verdict
                if (phase == WPh.Dawn) { AfterDeaths(true); return true; }
                if (phase == WPh.Verdict) { AfterDeaths(false); return true; }
                return false;
        }
        return false;
    }

    // --- Nuit ----------------------------------------------------------------------------------------------
    bool Night(string[] a)
    {
        if (phase != WPh.Night || a.Length < 3 || !int.TryParse(a[1], out int s) || s < 0 || s >= players.Count) return false;
        var p = players[s];
        if (!NightRole(p) || nightDone.Contains(s) && a[2] != "wolf") return false;
        int t = a.Length > 3 && int.TryParse(a[3], out int tt) ? tt : -1, t2 = a.Length > 4 && int.TryParse(a[4], out int u) ? u : -1;
        bool Valid(int x) => x >= 0 && x < players.Count && players[x].alive;
        switch (a[2])
        {
            case "wolf":   // vote des loups (changeable) ; les loups voient les votes des autres
                if (step != WolfStep || !IsWolf(p.role) || !Valid(t) || IsWolf(players[t].role)) return false;
                wolfVotes[s] = t;
                if (!WolfPower(p)) nightDone.Add(s);
                break;
            case "see": if (p.role != Role.Voyante || !Valid(t) || t == s) return false; target[s] = t; nightDone.Add(s);
                events.Add(new WGEvent { type = WGEv.Seen, seat = s, other = t, text = $"{players[t].name} est {Name(players[t].role)}." });
                break;
            case "guard": if (p.role != Role.Garde || !Valid(t) || t == p.guardLast) return false; target[s] = t; nightDone.Add(s); break;
            case "love": if (p.role != Role.Cupidon || !Valid(t) || !Valid(t2) || t == t2) return false; target[s] = t; target2[s] = t2; nightDone.Add(s); break;
            case "crow": if (p.role != Role.Corbeau || !Valid(t)) return false; target[s] = t; nightDone.Add(s); break;
            case "white": if (p.role != Role.LoupBlanc || step != WhiteStep || !WhiteNight || !Valid(t) || t == s) return false; target[s] = t; nightDone.Add(s); break;
            case "sleep": if (p.role != Role.Anesthesiste || !CanAnesth || !Valid(t)) return false; target[s] = t; nightDone.Add(s); break;
            case "convert": if (p.role != Role.LoupNoir || p.convertUsed) return false; flags.Add(s); nightDone.Add(s); break;
            case "fog": if (p.role != Role.Brumeux || p.fogUsed) return false; flags.Add(s); nightDone.Add(s); break;
            case "coup": if (p.role != Role.Dictateur || p.coupUsed) return false; flags.Add(s); nightDone.Add(s); break;
            case "douse": if (p.role != Role.Pyromane || !Valid(t) || t == s) return false; target[s] = t; if (Valid(t2) && t2 != s) target2[s] = t2; nightDone.Add(s); break;
            case "ignite": if (p.role != Role.Pyromane || !players.Any(o => o.alive && o.doused)) return false; flags.Add(s); nightDone.Add(s); break;
            case "kill": if (p.role != Role.Assassin || !Valid(t) || t == s) return false; target[s] = t; nightDone.Add(s); break;
            case "bomb": if (p.role != Role.Blaster || !Valid(t) || t == s) return false; target[s] = t; nightDone.Add(s); break;
            case "done": nightDone.Add(s); break;
            default: return false;
        }
        // Confirmation dans le carnet de celui qui agit (lui seul la voit).
        string N(int x) => x >= 0 && x < players.Count ? players[x].name : "?";
        string note = a[2] switch
        {
            "guard" => t == s ? "Tu te protèges cette nuit." : $"Tu protèges {N(t)} cette nuit.",
            "crow" => $"Tu désignes {N(t)} : 2 voix contre lui demain.",
            "white" => $"Tu dévores {N(t)} en secret.",
            "sleep" => $"Tu anesthésies {N(t)} : demain, il ne pourra ni parler ni voter.",
            "convert" => "Cette nuit, la victime des loups deviendra un loup-garou.",
            "fog" => "Demain, le brouillard cachera les votes.",
            "coup" => "Demain matin, tu feras ton coup d'État.",
            "douse" => $"Tu imbibes d'essence {N(t)}" + (t2 >= 0 && t2 != s ? $" et {N(t2)}." : "."),
            "ignite" => "Tu immoles tous les joueurs imbibés !",
            "kill" => $"Tu assassines {N(t)}.",
            "bomb" => $"Tu lances ta bombe sur {N(t)}.",
            "love" => null,
            _ => null,
        };
        if (note != null) events.Add(new WGEvent { type = WGEv.Info, seat = s, text = note });
        CheckNightDone();
        return true;
    }

    // Loup a pouvoir encore utilisable cette nuit : il doit aussi s'en servir ou passer.
    public bool WolfPower(Player p) => p.role == Role.LoupNoir && !p.convertUsed || p.role == Role.Brumeux && !p.fogUsed || p.role == Role.Anesthesiste && CanAnesth;

    // Tous ceux de l'etape ont fini (les loups : un vote chacun, plus leur pouvoir) -> etape suivante.
    void CheckNightDone()
    {
        if (phase != WPh.Night) return;
        foreach (var p in players.Where(NightRole))
        {
            if (step == WolfStep && IsWolf(p.role) && !wolfVotes.ContainsKey(p.seat)) return;
            if (!nightDone.Contains(p.seat)) return;
        }
        NextStep();
    }

    void EndNight()
    {
        victim = wolfVotes.Count == 0 ? -1 : wolfVotes.Values.GroupBy(x => x).OrderByDescending(g => g.Count()).ThenBy(_ => rng.Next()).First().Key;
        if (wolfVotes.Count > 0)
        {
            var pack = VoteText($"Vote des loups (nuit {night})", wolfVotes, -1, false);
            foreach (var w in players.Where(p => IsWolf(p.role))) events.Add(new WGEvent { type = WGEv.Info, seat = w.seat, text = pack });
        }
        var witch = players.FirstOrDefault(p => p.alive && p.role == Role.Sorciere && (!p.lifeUsed || !p.deathUsed));
        if (witch != null) { phase = WPh.Witch; log.Add("La Sorcière se réveille..."); return; }
        Dawn();
    }

    bool Witch(string[] a)
    {
        if (phase != WPh.Witch || a.Length < 3 || !int.TryParse(a[1], out int s)) return false;
        var p = players[s];
        if (!p.alive || p.role != Role.Sorciere) return false;
        if (a[2] == "save") { if (p.lifeUsed || victim < 0) return false; witchSave = true; p.lifeUsed = true; events.Add(new WGEvent { type = WGEv.Info, seat = s, text = $"Tu sauves {players[victim].name} avec ta potion de vie." }); return true; }
        if (a[2] == "kill") { if (p.deathUsed || a.Length < 4 || !int.TryParse(a[3], out int t) || t < 0 || t >= players.Count || !players[t].alive) return false; witchKill = t; p.deathUsed = true; events.Add(new WGEvent { type = WGEv.Info, seat = s, text = $"Tu empoisonnes {players[t].name}." }); return true; }
        if (a[2] == "none") { Dawn(); return true; }
        return false;
    }

    // --- Aube : tout ce qui s'est passe dans la nuit ----------------------------------------------------------
    void Dawn()
    {
        day++;
        var dead = new List<(int seat, string why)>();
        int Guarded() { var g = players.FirstOrDefault(p => p.alive && p.role == Role.Garde); return g != null && target.TryGetValue(g.seat, out int t) ? t : -1; }
        int guarded = Guarded();
        foreach (var g in players.Where(p => p.role == Role.Garde)) g.guardLast = guarded;
        // Cupidon.
        foreach (var c in players.Where(p => p.role == Role.Cupidon && target.ContainsKey(p.seat) && target2.ContainsKey(p.seat)))
        {
            int x = target[c.seat], y = target2[c.seat];
            players[x].lover = y; players[y].lover = x;
            events.Add(new WGEvent { type = WGEv.Info, seat = x, other = y, text = $"Cupidon t'a lié(e) à {players[y].name} par l'amour." });
            events.Add(new WGEvent { type = WGEv.Info, seat = y, other = x, text = $"Cupidon t'a lié(e) à {players[x].name} par l'amour." });
            if (c.seat != x && c.seat != y) events.Add(new WGEvent { type = WGEv.Info, seat = c.seat, text = $"Tu as lié {players[x].name} et {players[y].name} par l'amour." });
        }
        // Anesthesiste, Corbeau, Brumeux, Dictateur.
        foreach (var p in players.Where(p => p.role == Role.Anesthesiste && target.ContainsKey(p.seat))) { players[target[p.seat]].asleep = true; log.Add($"{players[target[p.seat]].name} a été anesthésié : il ne peut ni parler ni voter aujourd'hui."); }
        crowed = players.Where(p => p.role == Role.Corbeau && target.ContainsKey(p.seat)).Select(p => target[p.seat]).DefaultIfEmpty(-1).First();
        if (crowed >= 0) log.Add($"Le Corbeau a désigné {players[crowed].name} : 2 voix contre lui aujourd'hui.");
        fog = false;
        foreach (var p in players.Where(p => p.role == Role.Brumeux && flags.Contains(p.seat))) { fog = true; p.fogUsed = true; log.Add("Un épais brouillard recouvre le village : les votes seront cachés."); }
        foreach (var p in players.Where(p => p.role == Role.Dictateur && flags.Contains(p.seat))) { p.coupUsed = true; coupBy = p.seat; }
        // Victime des loups.
        if (victim >= 0)
        {
            var v = players[victim];
            var noir = players.FirstOrDefault(p => p.alive && p.role == Role.LoupNoir && flags.Contains(p.seat));
            if (v.role == Role.Assassin) log.Add("Les loups n'ont pas pu dévorer l'Assassin...");
            else if (victim == guarded) { log.Add("Le Garde a protégé la victime des loups."); events.Add(new WGEvent { type = WGEv.Saved, seat = victim, text = "garde" }); }
            else if (witchSave) { log.Add("La Sorcière a sauvé la victime des loups."); events.Add(new WGEvent { type = WGEv.Saved, seat = victim, text = "sorciere" }); }
            else if (noir != null) { noir.convertUsed = true; v.role = Role.Loup; events.Add(new WGEvent { type = WGEv.Info, seat = victim, text = "Le Loup noir t'a mordu : tu es maintenant un loup-garou !" }); }
            else dead.Add((victim, "dévoré par les loups"));
        }
        foreach (var p in players.Where(p => p.alive && p.role == Role.LoupBlanc && target.ContainsKey(p.seat))) if (target[p.seat] != guarded) dead.Add((target[p.seat], "dévoré"));
        foreach (var p in players.Where(p => p.alive && p.role == Role.Assassin && target.ContainsKey(p.seat))) if (target[p.seat] != guarded) dead.Add((target[p.seat], "assassiné"));
        if (witchKill >= 0) dead.Add((witchKill, "empoisonné par la Sorcière"));
        // Pyromane.
        foreach (var p in players.Where(p => p.alive && p.role == Role.Pyromane))
        {
            if (flags.Contains(p.seat)) { foreach (var o in players.Where(o => o.alive && o.doused).Take(6)) dead.Add((o.seat, "immolé par le Pyromane")); }
            else foreach (var x in new[] { target.TryGetValue(p.seat, out int t1) ? t1 : -1, target2.TryGetValue(p.seat, out int t2) ? t2 : -1 }.Where(x => x >= 0))
                { players[x].doused = true; events.Add(new WGEvent { type = WGEv.Info, seat = x, text = "Tu as été imbibé d'essence par le Pyromane !" }); }
        }
        // Blaster : la bombe posee, et celles qui explosent.
        foreach (var o in players.Where(o => o.alive && o.bomb >= 0).ToList())
            if (--o.bomb <= 0)
            {
                o.bomb = -1; dead.Add((o.seat, "pulvérisé par la bombe du Blaster"));
                log.Add($"BOUM ! La bombe explose dans les mains de {o.name} !");
                if (Killer(o.role)) foreach (var b in players.Where(b => b.role == Role.Blaster)) b.bombs++;   // un ennemi du village : nouvelle bombe
            }
        foreach (var p in players.Where(p => p.alive && p.role == Role.Blaster && target.ContainsKey(p.seat)))
        {
            int t = target[p.seat], turns = 1 + rng.Next(3);
            players[t].bomb = turns; p.bombs--;
            events.Add(new WGEvent { type = WGEv.Info, seat = p.seat, text = $"Ta bombe est sur {players[t].name} : elle explosera dans {turns} tour{(turns > 1 ? "s" : "")}." });
        }
        // Ninja : les empoisonnes meurent ce matin, si le ninja vit encore.
        bool ninja = players.Any(p => p.alive && p.role == Role.Ninja);
        foreach (var o in players.Where(o => o.poisoned)) { o.poisoned = false; if (ninja && o.alive) dead.Add((o.seat, "empoisonné par un shuriken")); }
        // Dictateur qui a rate son coup hier.
        if (coupDue >= 0 && players[coupDue].alive) dead.Add((coupDue, "renversé après son coup d'État raté"));
        coupDue = -1;
        // Les morts.
        events.Add(new WGEvent { type = WGEv.Day, text = $"Jour {day}" });
        if (testShield >= 0) dead.RemoveAll(d => d.seat == testShield);   // autotest d'un role : il survit aux nuits
        var killed = dead.GroupBy(d => d.seat).Select(g => g.First()).ToList();
        if (killed.Count == 0) log.Add($"Jour {day} : le village se réveille... personne n'est mort cette nuit !");
        foreach (var (seat, why) in killed) Kill(seat, why, true);
        // Medium : tous les 2 tours, un esprit lui souffle le nom d'un ennemi du village.
        foreach (var m in players.Where(p => p.alive && p.role == Role.Medium && day % 2 == 0))
        {
            var bad = players.Where(o => o.alive && Killer(o.role) && o.seat != m.seat).ToList();
            if (bad.Count > 0) events.Add(new WGEvent { type = WGEv.Info, seat = m.seat, text = $"Un esprit te souffle : « Méfie-toi de {bad[rng.Next(bad.Count)].name}... »" });
        }
        // Dresseur : son chien grogne si un voisin (vivant) est un loup.
        foreach (var d in players.Where(p => p.alive && p.role == Role.Dresseur))
        {
            var near = Neighbors(d.seat);
            bool grr = near.Any(x => IsWolf(players[x].role));
            log.Add(grr ? $"Le chien de {d.name} grogne : un de ses voisins est un loup-garou !" : $"Le chien de {d.name} reste calme.");
        }
        foreach (var p in players.Where(p => p.role == Role.Influenceur && p.alive && !p.missionDone && !players[p.missionTarget].alive))
        {
            p.role = Role.Villageois;
            events.Add(new WGEvent { type = WGEv.Info, seat = p.seat, text = "Ta cible est morte sans vote du village : mission ratée, tu rejoins le village." });
        }
        dayPhase = false;
        phase = WPh.Dawn;
    }

    List<int> Neighbors(int seat)
    {
        var alive = players.Where(p => p.alive).Select(p => p.seat).ToList();
        int i = alive.IndexOf(seat);
        if (i < 0 || alive.Count < 2) return new List<int>();
        return new[] { alive[(i + alive.Count - 1) % alive.Count], alive[(i + 1) % alive.Count] }.Distinct().Where(x => x != seat).ToList();
    }

    // night : mort de la nuit, le journal ne dit pas qui l'a tue (l'evenement garde la cause, pour le son).
    void Kill(int seat, string why, bool night = false)
    {
        var p = players[seat];
        if (!p.alive) return;
        p.alive = false;
        p.deathLog = log.Count;
        p.bomb = -1;
        events.Add(new WGEvent { type = WGEv.Death, seat = seat, text = why });
        log.Add(night ? $"{p.name} est mort pendant la nuit. Il était {Name(p.role)}." : $"{p.name} est mort ({why}). Il était {Name(p.role)}.");
        if (p.role == Role.Chasseur) pending.Add((WPh.Hunter, seat));
        if (p.mayor) { p.mayor = false; if (players.Any(o => o.alive)) pending.Add((WPh.Heir, seat)); }
        if (p.lover >= 0 && players[p.lover].alive) { log.Add($"{players[p.lover].name}, fou de chagrin, meurt d'amour."); Kill(p.lover, "mort d'amour"); }
    }

    // Apres une annonce (aube ou verdict) : chasseurs, successeur du maire, coup d'Etat, puis la suite.
    void AfterDeaths(bool morning)
    {
        if (CheckWin()) return;
        if (pending.Count > 0) { phase = pending[0].kind; return; }
        if (morning && coupBy >= 0 && players[coupBy].alive) { pending.Add((WPh.Dictator, coupBy)); coupBy = -1; phase = WPh.Dictator; log.Add($"{players[pending[0].seat].name} est Dictateur : il fait un coup d'État !"); return; }
        coupBy = -1;
        if (morning) { StartVote(MayorP == null && day == 1 ? WPh.Election : WPh.Vote); return; }
        night++;
        StartNight();
    }

    void Resolve(WPh kind, int s, int t)
    {
        pending.RemoveAt(0);
        var p = players[s];
        if (kind == WPh.Hunter) { log.Add($"Le Chasseur {p.name} tire sur {players[t].name} !"); Kill(t, "abattu par le Chasseur"); }
        else if (kind == WPh.Heir) { players[t].mayor = true; log.Add($"{p.name} cède l'écharpe de maire à {players[t].name}."); events.Add(new WGEvent { type = WGEv.Mayor, seat = t }); }
        else
        {
            bool hit = IsWolf(players[t].role);
            Kill(t, "renversé par le Dictateur");
            if (hit) { foreach (var o in players) o.mayor = false; p.mayor = true; log.Add($"{p.name} a abattu un loup-garou : il devient maire !"); events.Add(new WGEvent { type = WGEv.Mayor, seat = s }); }
            else { coupDue = s; log.Add($"{players[t].name} n'était pas un loup... le Dictateur sera renversé demain."); }
            phase = WPh.Verdict; eliminated = t; dayPhase = true;   // le coup remplace le vote du jour
            return;
        }
        phase = phase == WPh.Hunter || phase == WPh.Heir ? (dayPhase ? WPh.Verdict : WPh.Dawn) : phase;
    }
    bool dayPhase;   // les morts en attente viennent du vote (verdict) ou de la nuit (aube)

    // --- Jour : election du maire et vote du village -------------------------------------------------------------
    public bool CanVote(int s) => s >= 0 && s < players.Count && players[s].alive && !players[s].asleep && !(phase == WPh.Tie && tied.Contains(s) && TieOthersVote);
    // Revote : les ex aequo ne votent pas... sauf s'il n'y a personne d'autre pour voter (ils ne votent alors pas pour eux).
    bool TieOthersVote => players.Any(o => o.alive && !o.asleep && !tied.Contains(o.seat));
    public bool CanChat(int s, string chan)
    {
        var p = players[s];
        if (chan == "morts") return !p.alive || p.alive && p.role == Role.Necromancien && phase == WPh.Night;
        if (!p.alive || p.asleep) return false;
        if (chan == "loups") return phase == WPh.Night && IsWolf(p.role);
        return chan == "village" && phase != WPh.Night && phase != WPh.Witch;
    }
    public bool CanRead(int s, string chan)
    {
        var p = players[s];
        if (!p.alive) return true;   // les morts voient tout
        if (chan == "morts") return p.role == Role.Necromancien;
        if (chan == "loups") return IsWolf(p.role) || p.role == Role.PetiteFille;
        return true;
    }

    void StartVote(WPh kind)
    {
        votes.Clear(); eliminated = -1;
        phase = kind;
        log.Add(kind == WPh.Election ? "Élisons le maire !" : $"Jour {day} : débat, puis vote du village.");
    }

    // Recapitulatif d'un vote, une ligne par joueur vise (le plus vote d'abord) : « Robo-Lea : 5 voix (Lilith, Pixel...) ».
    // Sous le brouillard, seulement les totaux.
    string VoteText(string what, Dictionary<int, int> v, int crow, bool hidden)
    {
        var lines = v.GroupBy(x => x.Value).Select(g => (seat: g.Key, who: g.Select(x => players[x.Key].name).ToList())).ToList();
        if (crow >= 0 && !lines.Any(x => x.seat == crow)) lines.Add((crow, new List<string>()));
        int Count((int seat, List<string> who) x) => x.who.Count + (x.seat == crow ? 2 : 0);
        var sb = new System.Text.StringBuilder(what + " :");
        foreach (var x in lines.OrderByDescending(Count))
        {
            var from = new List<string>(x.who);
            if (x.seat == crow) from.Add("Corbeau +2");
            sb.Append($"\n  • {players[x.seat].name} : {Count(x)} voix" + (hidden ? "" : $" ({string.Join(", ", from)})"));
        }
        return sb.ToString();
    }

    public Dictionary<int, int> Tally()
    {
        var t = new Dictionary<int, int>();
        foreach (var kv in votes) t[kv.Value] = (t.TryGetValue(kv.Value, out int c) ? c : 0) + 1;
        if (phase == WPh.Vote && crowed >= 0 && players[crowed].alive) t[crowed] = (t.TryGetValue(crowed, out int c2) ? c2 : 0) + 2;
        return t;
    }

    void EndVote()
    {
        var t = Tally();
        // Trace du vote dans le journal (relu dans le carnet) ; sous le brouillard, seulement les totaux.
        if (votes.Count > 0 || t.Count > 0)
            log.Add(VoteText(phase == WPh.Election ? "Vote du maire" : phase == WPh.Tie ? "Revote (égalité)" : "Vote du village", votes, phase == WPh.Vote && crowed >= 0 && players[crowed].alive ? crowed : -1, fog));
        dayPhase = true;
        if (phase == WPh.Tie)
        {
            int m2 = t.Count == 0 ? 0 : t.Values.Max();
            var top2 = t.Where(x => x.Value == m2).Select(x => x.Key).ToList();
            var mayor2 = MayorP;
            int o2 = top2.Count == 1 ? top2[0] : top2.Count > 1 && mayor2 != null && votes.TryGetValue(mayor2.seat, out int mv2) && top2.Contains(mv2) ? mv2 : -1;   // nouvelle egalite : le maire tranche
            if (o2 < 0) log.Add("Toujours l'égalité : personne n'est éliminé.");
            tied.Clear();
            Eliminate(o2);
            return;
        }
        if (phase == WPh.Election)
        {
            int best = t.Count == 0 ? players.Where(p => p.alive).OrderBy(_ => rng.Next()).First().seat : t.OrderByDescending(x => x.Value).ThenBy(_ => rng.Next()).First().Key;
            players[best].mayor = true;
            log.Add($"{players[best].name} est élu maire !");
            events.Add(new WGEvent { type = WGEv.Mayor, seat = best });
            StartVote(WPh.Vote);
            return;
        }
        crowed = -1;
        int max = t.Count == 0 ? 0 : t.Values.Max();
        var top = t.Where(x => x.Value == max).Select(x => x.Key).ToList();
        var mayor = MayorP;
        if (top.Count > 1)
        {
            // Egalite (Agrou) : les ex aequo montent a la potence, le village revote entre eux.
            tied.Clear(); tied.AddRange(top.OrderBy(x => x));
            votes.Clear();
            phase = WPh.Tie;
            log.Add("Égalité entre " + string.Join(" et ", tied.Select(x => players[x].name)) + " : nouveau vote entre eux !");
            events.Add(new WGEvent { type = WGEv.Tie });
            return;
        }
        Eliminate(top.Count == 1 ? top[0] : -1);
    }

    void Eliminate(int out_)
    {
        eliminated = out_;
        if (out_ >= 0)
        {
            var p = players[out_];
            if (p.role == Role.Ange && day == 1 && !p.angeDone) { Kill(out_, "éliminé par le village"); Win("L'Ange", new List<int> { out_ }); return; }
            foreach (var inf in players.Where(i => i.role == Role.Influenceur && i.missionTarget == out_ && !i.missionDone)) { inf.missionDone = true; log.Add($"{inf.name} a réussi sa mission d'Influenceur !"); }
            Kill(out_, "éliminé par le village");
        }
        foreach (var a in players.Where(a => a.role == Role.Ange && !a.angeDone))
        {
            a.angeDone = true;
            if (a.alive) { a.role = Role.Villageois; events.Add(new WGEvent { type = WGEv.Info, seat = a.seat, text = "Le village ne t'a pas éliminé au premier vote : tu deviens villageois." }); }
        }
        phase = WPh.Verdict;
    }

    // --- Fin de partie ----------------------------------------------------------------------------------------------
    bool CheckWin()
    {
        var alive = players.Where(p => p.alive).ToList();
        if (alive.Count == 0) { Win("Personne", new List<int>()); return true; }
        if (alive.Count == 2 && alive[0].lover == alive[1].seat) { Win("Les amoureux", alive.Select(p => p.seat).ToList()); return true; }
        if (alive.Count == 1 && TeamOf(alive[0].role) == Team.Seul && alive[0].role != Role.Ange && alive[0].role != Role.Influenceur)
        { Win(alive[0].name + $" ({Name(alive[0].role)})", new List<int> { alive[0].seat }); return true; }
        if (!alive.Any(p => Killer(p.role))) { Win("Le village", players.Where(p => TeamOf(p.role) == Team.Village || p.role == Role.Ange || p.role == Role.Influenceur && !p.missionDone).Select(p => p.seat).ToList()); return true; }
        if (alive.All(p => TeamOf(p.role) == Team.Loups)) { Win("Les loups-garous", players.Where(p => TeamOf(p.role) == Team.Loups).Select(p => p.seat).ToList()); return true; }
        // Un tueur seul face a un innocent : les votes seraient toujours a egalite et il le tuerait la nuit suivante -> il gagne.
        if (alive.Count == 2 && alive.Count(p => Killer(p.role)) == 1)
        {
            var k = alive.First(p => Killer(p.role));
            if (TeamOf(k.role) == Team.Loups) Win("Les loups-garous", players.Where(p => TeamOf(p.role) == Team.Loups).Select(p => p.seat).ToList());
            else Win(k.name + $" ({Name(k.role)})", new List<int> { k.seat });
            return true;
        }
        return false;
    }

    void Win(string who, List<int> seats)
    {
        winner = who;
        winners.Clear(); winners.AddRange(seats);
        foreach (var inf in players.Where(p => p.role == Role.Influenceur && p.missionDone && !winners.Contains(p.seat))) winners.Add(inf.seat);
        phase = WPh.Over;
        events.Add(new WGEvent { type = WGEv.Over, text = who });
        log.Add(who == "Personne" ? "Tout le monde est mort : personne ne gagne !" : $"{who} remporte{(who.StartsWith("Les") ? "nt" : "")} la partie !");
    }

    // --- Temps ecoule (l'hote) : les absents ne font rien (ou au hasard pour les choix obligatoires) -----------------
    bool Timeout()
    {
        switch (phase)
        {
            case WPh.Night: NextStep(); return true;   // les retardataires ne font rien
            case WPh.Witch: Dawn(); return true;
            case WPh.Election: case WPh.Vote: case WPh.Tie: EndVote(); return true;
            case WPh.Hunter: case WPh.Heir: case WPh.Dictator:
            {
                var s = pending[0].seat;
                var others = players.Where(p => p.alive && p.seat != s).ToList();
                if (others.Count == 0) { pending.RemoveAt(0); AfterDeaths(!dayPhase); return true; }
                Resolve(phase, s, others[rng.Next(others.Count)].seat);
                return true;
            }
        }
        return false;
    }

    // --- Bots ---------------------------------------------------------------------------------------------------------
    static readonly Random botRng = new Random();
    int RandomOther(int s, Func<Player, bool> ok = null)
    {
        var l = players.Where(p => p.alive && p.seat != s && (ok == null || ok(p))).ToList();
        return l.Count == 0 ? -1 : l[botRng.Next(l.Count)].seat;
    }
    // Action d'un bot pour son siege (null : rien a faire maintenant).
    public string[] BotFor(int s)
    {
        var p = players[s];
        switch (phase)
        {
            case WPh.Night:
                if (!NightRole(p)) return null;
                if (step == WolfStep && IsWolf(p.role) && !wolfVotes.ContainsKey(s)) { int v = wolfVotes.Count > 0 ? wolfVotes.Values.First() : RandomOther(s, o => !IsWolf(o.role)); return v < 0 ? new[] { "night", $"{s}", "done" } : new[] { "night", $"{s}", "wolf", $"{v}" }; }
                if (nightDone.Contains(s)) return null;
                int t = RandomOther(s);
                switch (p.role)
                {
                    case Role.Voyante: return t < 0 ? D(s) : new[] { "night", $"{s}", "see", $"{t}" };
                    case Role.Garde: { int g = RandomOther(-1, o => o.seat != p.guardLast); return g < 0 ? D(s) : new[] { "night", $"{s}", "guard", $"{g}" }; }
                    case Role.Cupidon: { var two = players.Where(o => o.alive).OrderBy(_ => botRng.Next()).Take(2).ToList(); return two.Count < 2 ? D(s) : new[] { "night", $"{s}", "love", $"{two[0].seat}", $"{two[1].seat}" }; }
                    case Role.Corbeau: return t < 0 ? D(s) : new[] { "night", $"{s}", "crow", $"{t}" };
                    case Role.LoupBlanc: return WhiteNight && botRng.NextDouble() < 0.5 && t >= 0 ? new[] { "night", $"{s}", "white", $"{t}" } : D(s);
                    case Role.Anesthesiste: return CanAnesth && t >= 0 ? new[] { "night", $"{s}", "sleep", $"{t}" } : D(s);
                    case Role.LoupNoir: return !p.convertUsed && botRng.NextDouble() < 0.25 ? new[] { "night", $"{s}", "convert" } : D(s);
                    case Role.Brumeux: return !p.fogUsed && botRng.NextDouble() < 0.2 ? new[] { "night", $"{s}", "fog" } : D(s);
                    case Role.Dictateur: return botRng.NextDouble() < 0.15 ? new[] { "night", $"{s}", "coup" } : D(s);
                    case Role.Pyromane: return players.Count(o => o.alive && o.doused) >= 2 && botRng.NextDouble() < 0.5 ? new[] { "night", $"{s}", "ignite" } : t < 0 ? D(s) : new[] { "night", $"{s}", "douse", $"{t}" };
                    case Role.Assassin: return t >= 0 && botRng.NextDouble() < 0.7 ? new[] { "night", $"{s}", "kill", $"{t}" } : D(s);
                    case Role.Blaster: return t < 0 ? D(s) : new[] { "night", $"{s}", "bomb", $"{t}" };
                }
                return D(s);
            case WPh.Witch:
                if (!p.alive || p.role != Role.Sorciere) return null;
                if (!p.lifeUsed && victim >= 0 && botRng.NextDouble() < 0.5) return new[] { "witch", $"{s}", "save" };
                return new[] { "witch", $"{s}", "none" };
            case WPh.Tie:
                if (!CanVote(s) || votes.ContainsKey(s) || tied.Count == 0) return null;
                { var c = tied.Where(x => x != s).ToList(); return c.Count == 0 ? null : new[] { "vote", $"{s}", $"{c[botRng.Next(c.Count)]}" }; }
            case WPh.Election: case WPh.Vote:
                if (p.alive && p.role == Role.Ninja && !players.Any(o => o.poisoned) && botRng.NextDouble() < 0.3) { int n = RandomOther(s); if (n >= 0) return new[] { "shuriken", $"{s}", $"{n}" }; }
                if (!CanVote(s) || votes.ContainsKey(s)) return null;
                { int v = phase == WPh.Election ? RandomOther(-1) : votes.Count > 0 && botRng.NextDouble() < 0.6 ? votes.Values.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key : RandomOther(s);
                  if (v == s) v = RandomOther(s);
                  return v < 0 ? null : new[] { "vote", $"{s}", $"{v}" }; }
            case WPh.Hunter: case WPh.Heir: case WPh.Dictator:
                if (pending.Count == 0 || pending[0].seat != s) return null;
                { int v = RandomOther(s); return v < 0 ? null : new[] { phase == WPh.Hunter ? "hunt" : phase == WPh.Heir ? "heir" : "dict", $"{s}", $"{v}" }; }
        }
        return null;
    }
    static string[] D(int s) => new[] { "night", $"{s}", "done" };
    // Joueur parti (en ligne) : l'hote joue pour lui.
    public string[] Bot()
    {
        foreach (var p in players) { var a = BotFor(p.seat); if (a != null) return a; }
        return null;
    }
}
