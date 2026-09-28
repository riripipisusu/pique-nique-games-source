using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

// Multijoueur en ligne : Sessions Unity (Relay) + messages Netcode.
// Principe : Rules est deterministe. L'hote tire la graine, puis relaie chaque action
// (piocher / avancer un lapin) a tout le monde ; chaque PC rejoue la meme partie.
// Salon persistant : l'hote choisit le jeu, lance des parties successives et ramene tout le monde au salon
// (meme code). Les membres sont numerotes dans l'ordre d'arrivee ; les "Players" premiers jouent la partie en cours,
// ceux arrives pendant la partie la regardent (spectateurs : ils recoivent le depart et l'historique des actions).
public class Net : MonoBehaviour
{
    const string Channel = "cc";
    public static Net I;

    public bool Active => session != null;
    public bool IsHost;
    public string Code => session?.Code ?? "";
    public string Status = "";
    public GameId LobbyGame;
    public int LobbyOption;
    public string LobbyText = "";   // reglage en texte libre (petit bac : categories ecrites par l'hote)
    public readonly List<string> Lobby = new List<string>();
    public readonly List<string> LobbyAvatars = new List<string>();
    public bool InGame;
    public int Players;          // nombre de joueurs de la partie en cours (les premiers membres du salon)
    public bool Watching;        // ce PC regarde la partie en cours sans y jouer
    public const int SalonMax = 10;

    Game game;
    ISession session;
    NetworkManager nm;
    string myName;
    readonly Dictionary<ulong, int> seats = new Dictionary<ulong, int>();
    readonly HashSet<int> gone = new HashSet<int>();         // joueurs absents de la partie en cours (l'hote joue pour eux)
    readonly HashSet<int> disconnected = new HashSet<int>(); // partis pour de bon : retires du salon au retour
    readonly List<string> actions = new List<string>();     // historique de la partie, pour les spectateurs
    string startBody;
    IMatch shadow;   // copie instantanee de la partie cote hote, pour valider les actions
    public event Action Changed;

    public static Net Create(Game g)
    {
        var go = new GameObject("Net");
        I = go.AddComponent<Net>();
        I.game = g;
        return I;
    }

    async Task Init()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            // Profil aleatoire : permet de lancer deux jeux sur le meme PC pour tester.
            var opt = new InitializationOptions();
            opt.SetProfile("p" + UnityEngine.Random.Range(0, 1000000));
            await UnityServices.InitializeAsync(opt);
        }
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        if (!nm)
        {
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            nm = go.AddComponent<NetworkManager>();
            var transport = go.AddComponent<UnityTransport>();
            nm.NetworkConfig = new NetworkConfig { NetworkTransport = transport, ConnectionApproval = false };
            go.SetActive(true);
            DontDestroyOnLoad(go);
        }
    }

    static string Clean(string s) => (s ?? "").Replace("|", "").Replace(";", "").Trim();

    void Say(string s) { Status = s; Changed?.Invoke(); }

    public async void Host(string name, GameId g, int option)
    {
        try
        {
            myName = Clean(name);
            LobbyGame = g;
            LobbyOption = option;
            Say("Création de la partie...");
            await Init();
            session = await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions { MaxPlayers = SalonMax, IsPrivate = true }.WithRelayNetwork());
            IsHost = true;
            Hook();
            seats.Clear();
            seats[nm.LocalClientId] = 0;
            Lobby.Clear();
            Lobby.Add(myName);
            LobbyAvatars.Clear();
            LobbyAvatars.Add(game.myAvatar);
            game.mySeat = 0;
            InGame = false; Players = 0; Watching = false;
            gone.Clear(); disconnected.Clear(); actions.Clear();
            Say("Partage le code à tes amis !");
            SendLobby();
        }
        catch (Exception e) { Fail(e); }
    }

    public async void Join(string code, string name)
    {
        try
        {
            myName = Clean(name);
            Say("Connexion...");
            await Init();
            IsHost = false;
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(CodeFrom(code));
            Hook();
            Say("Connecté ! En attente de l'hôte...");
            if (nm.IsConnectedClient) Send($"hello|{myName}|{game.myAvatar}");
        }
        catch (Exception e) { Fail(e); }
    }

    // Accepte le code seul ou le message d'invitation colle en entier ("... code : ABC123").
    public static string CodeFrom(string text)
    {
        var t = (text ?? "").Trim();
        int i = t.LastIndexOf(':');
        if (i >= 0) t = t.Substring(i + 1);
        return new string(t.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    public string Invite => $"Rejoins-moi sur Pique-Nique's Games ! Lance le jeu, clique sur « Jouer en ligne » puis « Rejoindre » et colle ce message (ou tape le code) : {Code}";

    void Fail(Exception e)
    {
        Debug.LogException(e);
        Leave();
        Say("Impossible : " + (e.Message.Length > 90 ? e.Message.Substring(0, 90) + "..." : e.Message));
    }

    void Hook()
    {
        nm.CustomMessagingManager.RegisterNamedMessageHandler(Channel, OnMessage);
        nm.OnClientConnectedCallback += OnConnected;
        nm.OnClientDisconnectCallback += OnDisconnected;
    }

    public async void Leave()
    {
        var s = session;
        session = null;
        InGame = false;
        Players = 0;
        Watching = false;
        actions.Clear();
        disconnected.Clear();
        Lobby.Clear();
        LobbyAvatars.Clear();
        seats.Clear();
        gone.Clear();
        game.mySeat = -1;
        if (nm)
        {
            nm.OnClientConnectedCallback -= OnConnected;
            nm.OnClientDisconnectCallback -= OnDisconnected;
            nm.CustomMessagingManager?.UnregisterNamedMessageHandler(Channel);
        }
        if (s != null) try { await s.LeaveAsync(); } catch (Exception e) { Debug.LogWarning(e.Message); }
        if (nm && nm.IsListening) nm.Shutdown();
        Changed?.Invoke();
    }

    void OnConnected(ulong id)
    {
        if (!IsHost && id == nm.LocalClientId) Send($"hello|{myName}|{game.myAvatar}");
    }

    void OnDisconnected(ulong id)
    {
        if (!IsHost)
        {
            if (id == nm.LocalClientId || id == NetworkManager.ServerClientId)
            {
                Leave();
                game.ToMenu();
                Say("L'hôte a quitté la partie.");
            }
            return;
        }
        if (!seats.TryGetValue(id, out int seat)) return;
        seats.Remove(id);
        if (!InGame || seat >= Players) RemoveMember(seat);   // au salon, ou simple spectateur
        else
        {
            disconnected.Add(seat);
            if (gone.Add(seat)) Broadcast("left|" + seat);
        }
    }

    void RemoveMember(int seat)
    {
        Lobby.RemoveAt(seat);
        LobbyAvatars.RemoveAt(seat);
        foreach (var k in seats.Keys.ToList()) if (seats[k] > seat) seats[k]--;
        SendLobby();
    }

    // --- Messages -----------------------------------------------------------------
    void SendTo(ulong id, string s)
    {
        using var w = new FastBufferWriter(1100, Allocator.Temp);
        w.WriteValueSafe(s);
        nm.CustomMessagingManager.SendNamedMessage(Channel, id, w, NetworkDelivery.ReliableSequenced);
    }

    void Send(string s) => SendTo(NetworkManager.ServerClientId, s);

    // L'hote envoie a tous, et se l'applique aussi a lui-meme.
    void Broadcast(string s)
    {
        foreach (var id in nm.ConnectedClientsIds)
            if (id != nm.LocalClientId) SendTo(id, s);
        Apply(s);
    }

    void SendLobby()
    {
        Broadcast($"lobby|{LobbyGame}|{LobbyOption}|{string.Join(";", Lobby)}|{string.Join(";", LobbyAvatars)}|{(InGame ? Players : 0)}|{LobbyText}");
        foreach (var kv in seats) if (kv.Key != nm.LocalClientId) SendTo(kv.Key, "seat|" + kv.Value);
    }

    void OnMessage(ulong sender, FastBufferReader r)
    {
        r.ReadValueSafe(out string s);
        if (!IsHost) { Apply(s); return; }
        var p = s.Split('|');
        if (p[0] == "hello")
        {
            if (seats.ContainsKey(sender) || Lobby.Count >= SalonMax) return;
            seats[sender] = Lobby.Count;
            Lobby.Add(Clean(p[1]) is var n && n.Length > 0 ? n : "Joueur " + (Lobby.Count + 1));
            LobbyAvatars.Add(p.Length > 2 && Chars.Valid(p[2]) ? p[2] : Chars.Default);
            SendLobby();
            // Partie en cours : le nouveau venu la regarde (depart + actions deja jouees, rejouees en accelere).
            if (InGame) { SendTo(sender, "watch|" + startBody); foreach (var a in actions) SendTo(sender, a); }
        }
        else if (p[0] == "act" && InGame && seats.TryGetValue(sender, out int seat) && CanPlay(seat, p.Length > 1 ? p[1] : null))
            HostAct(p, seat);
        else if (p[0] == "quit" && InGame && seats.TryGetValue(sender, out int who) && who < Players && gone.Add(who))
            Broadcast("left|" + who);   // retourne au salon en pleine partie : l'hote joue pour lui
    }

    void Apply(string s)
    {
        var p = s.Split('|');
        switch (p[0])
        {
            case "lobby":
                LobbyGame = (GameId)Enum.Parse(typeof(GameId), p[1]);
                LobbyOption = int.Parse(p[2]);
                Lobby.Clear();
                Lobby.AddRange(p[3].Split(';'));
                LobbyAvatars.Clear();
                LobbyAvatars.AddRange(p[4].Split(';'));
                if (!IsHost) { int.TryParse(p.Length > 5 ? p[5] : "0", out Players); InGame = Players > 0; LobbyText = p.Length > 6 ? p[6] : ""; }
                Changed?.Invoke();
                break;
            case "seat":
                game.mySeat = int.Parse(p[1]);
                Changed?.Invoke();
                break;
            case "start":
            case "watch":
                InGame = true;
                var names = p[4].Split(';').ToList();
                Players = names.Count;
                Watching = game.mySeat >= Players;
                game.StartGame((GameId)Enum.Parse(typeof(GameId), p[1]), int.Parse(p[2]), names, int.Parse(p[3]), p[5].Split(';').ToList(), p[0] == "watch", p.Length > 6 ? p[6] : "");
                break;
            case "tolobby":
                InGame = false;
                Watching = false;
                Players = 0;
                game.BackToLobby();
                break;
            case "act":
                game.Enqueue(s);
                break;
            case "left":
                game.PlayerLeft(int.Parse(p[1]));
                break;
        }
    }

    // --- Actions de l'hote / des joueurs -------------------------------------------
    public void SetOption(int option) { if (IsHost && !InGame) { LobbyOption = option; SendLobby(); } }
    public void SetText(string text) { if (IsHost && !InGame) { LobbyText = (text ?? "").Replace("|", ""); SendLobby(); } }

    // Au quiz, tout le monde repond en meme temps ; ailleurs, seul le joueur dont c'est le tour agit.
    bool CanPlay(int seat, string action = null) => seat < Players && !gone.Contains(seat)
        && (shadow.Actor == seat || shadow.Actor == Quiz.Everyone || (shadow is Uno && (action == "uno" || action == "catch" || action == "jump")));   // Uno : a tout moment

    public void SetGame(GameId g)
    {
        if (!IsHost || InGame || g == LobbyGame) return;
        LobbyGame = g;
        LobbyOption = Game.DefaultOption(g);
        LobbyText = "";
        SendLobby();
    }

    public int MinPlayers => Games.TvTime(LobbyGame) ? 1 : 2;

    // Nouvelle partie avec les membres du salon (au-dela du maximum du jeu, les derniers arrives regardent).
    public void StartMatch()
    {
        if (!IsHost || Lobby.Count < MinPlayers) return;
        int n = Math.Min(Lobby.Count, Games.MaxPlayers(LobbyGame));
        int seed = UnityEngine.Random.Range(0, int.MaxValue);
        var names = Lobby.Take(n).ToList();
        shadow = Games.Create(LobbyGame, LobbyOption, names, seed, LobbyText);
        gone.Clear();
        actions.Clear();
        startBody = $"{LobbyGame}|{LobbyOption}|{seed}|{string.Join(";", names)}|{string.Join(";", LobbyAvatars.Take(n))}|{LobbyText}";
        Broadcast("start|" + startBody);
        SendLobby();
    }

    // L'hote ramene tout le monde au salon ; ceux partis pour de bon en sont retires.
    public void ReturnToLobby()
    {
        if (!IsHost) return;
        Broadcast("tolobby");
        foreach (int seat in disconnected.OrderByDescending(x => x)) { Lobby.RemoveAt(seat); LobbyAvatars.RemoveAt(seat); foreach (var k in seats.Keys.ToList()) if (seats[k] > seat) seats[k]--; }
        disconnected.Clear();
        gone.Clear();
        actions.Clear();
        SendLobby();
    }

    // Un invite quitte la partie en cours pour attendre au salon (l'hote joue pour lui).
    public void QuitToLobby()
    {
        if (IsHost) { ReturnToLobby(); return; }
        if (InGame && !Watching) Send("quit");
        game.BackToLobby();
    }

    public void Act(string action)
    {
        if (IsHost) { if (CanPlay(game.mySeat, action.Split('|')[0])) HostAct(("act|" + action).Split('|'), game.mySeat); }
        else Send("act|" + action);
    }

    // Valide l'action sur la copie de l'hote puis la diffuse. Quiz : l'hote ajoute le siege et le temps ecoule
    // (sa propre horloge fait foi), la proposition devient "guess|siege|ms|texte".
    void HostAct(string[] p, int seat = -1)
    {
        if (shadow is Quiz && p.Length > 2 && p[1] == "guess")
            p = new[] { "act", "guess", seat.ToString(), game.QuizElapsedMs.ToString(), Clean(p[2]) };
        if (shadow is Rhythm && p.Length > 2 && (p[1] == "sc" || p[1] == "done")) p[2] = seat.ToString();   // chacun ne donne que son score
        if (shadow is PetitBac && p.Length > 1 && (p[1] == "ans" || p[1] == "stop" || p[1] == "vote" || p[1] == "ready"))   // petit bac : l'hote ajoute le siege
        {
            if (seat < 0) return;
            p = new[] { "act", p[1], seat.ToString() }.Concat(p.Skip(2).Select(x => p[1] == "ans" ? Clean(x) : x)).ToArray();
        }
        if (shadow is PetitBac && p.Length > 1 && (p[1] == "next" || p[1] == "end" || p[1] == "score") && seat >= 0) return;   // reserve a l'hote
        if (shadow is Uno && p.Length > 2 && (p[1] == "uno" || p[1] == "catch" || p[1] == "jump") && seat >= 0) p[2] = seat.ToString();   // on n'annonce que pour soi
        if (shadow is Uno && p.Length > 1 && p[1] == "next" && seat >= 0) return;
        if (shadow is Rhythm && p.Length > 1 && (p[1] == "go" || p[1] == "end") && seat >= 0) return;          // reserve a l'hote
        if (shadow.Finished || !shadow.TryApply(p.Skip(1).ToArray())) return;
        var msg = string.Join("|", p);
        actions.Add(msg);
        Broadcast(msg);
    }

    // ponytail: un joueur parti est joue par l'hote avec une strategie simple (IMatch.Bot).
    void Update()
    {
        // Quiz : l'hote pilote les phases (fin du temps ou tout le monde a trouve, puis question suivante).
        if (IsHost && InGame && (shadow is Quiz || shadow is Rhythm || shadow is PetitBac || (shadow is Uno u0 && u0.phase == UPhase.RoundOver)) && game.Idle)
        {
            var tick = shadow is Rhythm r ? game.RhythmTick(r) : shadow is Uno u ? game.UnoTick(u) : shadow is PetitBac b ? game.BacTick(b) : game.QuizTick((Quiz)shadow);
            if (tick != null) HostAct(new[] { "act", tick });
            return;
        }
        if (!IsHost || !InGame || shadow == null || shadow.Finished || !gone.Contains(shadow.Actor) || !game.Idle) return;
        var a = shadow.Bot();
        if (a != null) HostAct(new[] { "act" }.Concat(a).ToArray());
    }
}
