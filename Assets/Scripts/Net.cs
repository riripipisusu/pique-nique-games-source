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
    public readonly List<string> Lobby = new List<string>();
    public readonly List<string> LobbyAvatars = new List<string>();
    public bool InGame;

    Game game;
    ISession session;
    NetworkManager nm;
    string myName;
    readonly Dictionary<ulong, int> seats = new Dictionary<ulong, int>();
    readonly HashSet<int> gone = new HashSet<int>();
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
            session = await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions { MaxPlayers = Games.MaxPlayers(g), IsPrivate = true }.WithRelayNetwork());
            IsHost = true;
            Hook();
            seats.Clear();
            seats[nm.LocalClientId] = 0;
            Lobby.Clear();
            Lobby.Add(myName);
            LobbyAvatars.Clear();
            LobbyAvatars.Add(game.myAvatar);
            game.mySeat = 0;
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
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
            Hook();
            Say("Connecté ! En attente de l'hôte...");
            if (nm.IsConnectedClient) Send($"hello|{myName}|{game.myAvatar}");
        }
        catch (Exception e) { Fail(e); }
    }

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
        if (!InGame)
        {
            seats.Remove(id);
            Lobby.RemoveAt(seat);
            LobbyAvatars.RemoveAt(seat);
            foreach (var k in seats.Keys.ToList()) if (seats[k] > seat) seats[k]--;
            SendLobby();
        }
        else
        {
            gone.Add(seat);
            Broadcast("left|" + seat);
        }
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
        Broadcast($"lobby|{LobbyGame}|{LobbyOption}|{string.Join(";", Lobby)}|{string.Join(";", LobbyAvatars)}");
        foreach (var kv in seats) if (kv.Key != nm.LocalClientId) SendTo(kv.Key, "seat|" + kv.Value);
    }

    void OnMessage(ulong sender, FastBufferReader r)
    {
        r.ReadValueSafe(out string s);
        if (!IsHost) { Apply(s); return; }
        var p = s.Split('|');
        if (p[0] == "hello")
        {
            if (InGame || seats.ContainsKey(sender) || Lobby.Count >= Games.MaxPlayers(LobbyGame)) return;
            seats[sender] = Lobby.Count;
            Lobby.Add(Clean(p[1]) is var n && n.Length > 0 ? n : "Joueur " + (Lobby.Count + 1));
            LobbyAvatars.Add(p.Length > 2 && Array.IndexOf(Game.Characters, p[2]) >= 0 ? p[2] : "Casual_Male");
            SendLobby();
        }
        else if (p[0] == "act" && InGame && seats.TryGetValue(sender, out int seat) && CanPlay(seat))
            HostAct(p, seat);
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
                Changed?.Invoke();
                break;
            case "seat":
                game.mySeat = int.Parse(p[1]);
                Changed?.Invoke();
                break;
            case "start":
                InGame = true;
                game.StartGame((GameId)Enum.Parse(typeof(GameId), p[1]), int.Parse(p[2]), p[4].Split(';').ToList(), int.Parse(p[3]), p[5].Split(';').ToList());
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

    // Au quiz, tout le monde repond en meme temps ; ailleurs, seul le joueur dont c'est le tour agit.
    bool CanPlay(int seat) => shadow.Actor == seat || shadow.Actor == Quiz.Everyone;

    public void StartMatch()
    {
        if (!IsHost || Lobby.Count < (LobbyGame == GameId.Quiz ? 1 : 2)) return;
        int seed = UnityEngine.Random.Range(0, int.MaxValue);
        shadow = Games.Create(LobbyGame, LobbyOption, Lobby, seed);
        Broadcast($"start|{LobbyGame}|{LobbyOption}|{seed}|{string.Join(";", Lobby)}|{string.Join(";", LobbyAvatars)}");
    }

    public void Act(string action)
    {
        if (IsHost) { if (CanPlay(game.mySeat)) HostAct(("act|" + action).Split('|'), game.mySeat); }
        else Send("act|" + action);
    }

    // Valide l'action sur la copie de l'hote puis la diffuse. Quiz : l'hote ajoute le siege et le temps ecoule
    // (sa propre horloge fait foi), la proposition devient "guess|siege|ms|texte".
    void HostAct(string[] p, int seat = -1)
    {
        if (shadow is Quiz && p.Length > 2 && p[1] == "guess")
            p = new[] { "act", "guess", seat.ToString(), game.QuizElapsedMs.ToString(), Clean(p[2]) };
        if (shadow.Finished || !shadow.TryApply(p.Skip(1).ToArray())) return;
        Broadcast(string.Join("|", p));
    }

    // ponytail: un joueur parti est joue par l'hote avec une strategie simple (IMatch.Bot).
    void Update()
    {
        // Quiz : l'hote pilote les phases (fin du temps ou tout le monde a trouve, puis question suivante).
        if (IsHost && InGame && shadow is Quiz && game.Idle)
        {
            var tick = game.QuizTick((Quiz)shadow);
            if (tick != null) HostAct(new[] { "act", tick });
            return;
        }
        if (!IsHost || !InGame || shadow == null || shadow.Finished || !gone.Contains(shadow.Actor) || !game.Idle) return;
        var a = shadow.Bot();
        if (a != null) HostAct(new[] { "act" }.Concat(a).ToArray());
    }
}
