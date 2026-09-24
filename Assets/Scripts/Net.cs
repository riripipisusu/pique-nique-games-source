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
    public Mode LobbyMode = Mode.Classique;
    public readonly List<string> Lobby = new List<string>();
    public bool InGame;

    Game game;
    ISession session;
    NetworkManager nm;
    string myName;
    readonly Dictionary<ulong, int> seats = new Dictionary<ulong, int>();
    readonly HashSet<int> gone = new HashSet<int>();
    Rules shadow;   // copie instantanee de la partie cote hote, pour valider les actions
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

    public async void Host(string name)
    {
        try
        {
            myName = Clean(name);
            Say("Création de la partie...");
            await Init();
            session = await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions { MaxPlayers = Rules.MaxPlayers, IsPrivate = true }.WithRelayNetwork());
            IsHost = true;
            Hook();
            seats.Clear();
            seats[nm.LocalClientId] = 0;
            Lobby.Clear();
            Lobby.Add(myName);
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
            if (nm.IsConnectedClient) Send("hello|" + myName);
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
        if (!IsHost && id == nm.LocalClientId) Send("hello|" + myName);
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
        Broadcast($"lobby|{LobbyMode}|{string.Join(";", Lobby)}");
        foreach (var kv in seats) if (kv.Key != nm.LocalClientId) SendTo(kv.Key, "seat|" + kv.Value);
    }

    void OnMessage(ulong sender, FastBufferReader r)
    {
        r.ReadValueSafe(out string s);
        if (!IsHost) { Apply(s); return; }
        var p = s.Split('|');
        if (p[0] == "hello")
        {
            if (InGame || seats.ContainsKey(sender) || Lobby.Count >= Rules.MaxPlayers) return;
            seats[sender] = Lobby.Count;
            Lobby.Add(Clean(p[1]) is var n && n.Length > 0 ? n : "Joueur " + (Lobby.Count + 1));
            SendLobby();
        }
        else if (p[0] == "act" && InGame && seats.TryGetValue(sender, out int seat) && seat == shadow.turn)
            HostAct(p);
    }

    void Apply(string s)
    {
        var p = s.Split('|');
        switch (p[0])
        {
            case "lobby":
                LobbyMode = (Mode)Enum.Parse(typeof(Mode), p[1]);
                Lobby.Clear();
                Lobby.AddRange(p[2].Split(';'));
                Changed?.Invoke();
                break;
            case "seat":
                game.mySeat = int.Parse(p[1]);
                Changed?.Invoke();
                break;
            case "start":
                InGame = true;
                game.StartGame((Mode)Enum.Parse(typeof(Mode), p[1]), p[3].Split(';').ToList(), int.Parse(p[2]));
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
    public void SetMode(Mode m) { if (IsHost && !InGame) { LobbyMode = m; SendLobby(); } }

    public void StartMatch()
    {
        if (!IsHost || Lobby.Count < 2) return;
        int seed = UnityEngine.Random.Range(0, int.MaxValue);
        shadow = new Rules(LobbyMode, Lobby, seed);
        Broadcast($"start|{LobbyMode}|{seed}|{string.Join(";", Lobby)}");
    }

    public void Act(string action)
    {
        if (IsHost) { if (shadow.turn == game.mySeat) HostAct(("act|" + action).Split('|')); }
        else Send("act|" + action);
    }

    // Valide l'action sur la copie de l'hote puis la diffuse.
    void HostAct(string[] p)
    {
        if (shadow.Over) return;
        if (p[1] == "draw") { if (shadow.drawn != null) return; shadow.Draw(); }
        else { int k = int.Parse(p[2]); if (!shadow.CanMove(k)) return; shadow.Move(k); }
        Broadcast(string.Join("|", p));
    }

    // ponytail: un joueur parti est joue par l'hote (pioche + premier lapin jouable).
    void Update()
    {
        if (!IsHost || !InGame || shadow == null || shadow.Over || !gone.Contains(shadow.turn) || !game.Idle) return;
        if (shadow.drawn == null) HostAct(new[] { "act", "draw" });
        else for (int k = 0; k < Rules.RabbitsPerPlayer; k++) if (shadow.CanMove(k)) { HostAct(new[] { "act", "move", k.ToString() }); break; }
    }
}
